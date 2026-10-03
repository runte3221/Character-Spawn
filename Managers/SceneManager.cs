using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using CharacterSpawn.Models;

namespace CharacterSpawn.Managers;

public class SceneSaveData
{
    public List<SceneData> Scenes { get; set; } = new();
    public List<string> Folders { get; set; } = new();
}

public class SceneManager : IDisposable
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly LogManager? logManager;
    private readonly ActorManager actorManager;
    private readonly Configuration configuration;

    private readonly string scenesFilePath;

    /// <summary>
    /// 登録されている全シーンリスト
    /// </summary>
    public List<SceneData> Scenes { get; private set; } = new();

    /// <summary>
    /// フォルダ一覧 (例: "My House", "My House/1F", "Solution Nine")
    /// </summary>
    public List<string> Folders { get; private set; } = new();

    /// <summary>
    /// 現在選択／編集中のシーン
    /// </summary>
    public SceneData? SelectedScene { get; set; }

    /// <summary>
    /// 現在選択中の配置アクター（別ウィンドウ編集用）
    /// </summary>
    public SceneActorPlacement? SelectedPlacement { get; set; }

    /// <summary>
    /// 現在スポーン中のシーン
    /// </summary>
    public SceneData? ActiveSpawnedScene { get; private set; }

    /// <summary>
    /// スポーンされた配置アクターの実体管理 (PlacementId -> SpawnedActorData)
    /// </summary>
    private readonly Dictionary<Guid, SpawnedActorData> spawnedSceneActors = new();

    private SceneData? pendingAutoSpawnScene;
    private int pendingAutoSpawnTicks = 0;

    // 非同期フレーム分散（スタッガー）スポーンキュー（363msヒッチ解消・将来100体規模対応）
    private readonly Queue<SceneActorPlacement> staggeredSpawnQueue = new();
    private int spawnIntervalTicks = 0;
    private const int DefaultSpawnIntervalTicks = 2; // 2フレームに1体スポーン (~33ms間隔、メインスレッドへの負荷ゼロ)

    public SceneManager(
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        LogManager? logManager,
        ActorManager actorManager,
        Configuration configuration)
    {
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.logManager = logManager;
        this.actorManager = actorManager;
        this.configuration = configuration;

        var configDir = pluginInterface.GetPluginConfigDirectory();
        Directory.CreateDirectory(configDir);
        this.scenesFilePath = Path.Combine(configDir, "scenes.json");

        LoadScenes();

        this.clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        // 1. 移動前のシーンを即座に安全クリーンアップ
        if (ActiveSpawnedScene != null || spawnedSceneActors.Count > 0)
        {
            logManager?.Info($"Territory changed to {territoryType}. Automatically despawning active scene '{ActiveSpawnedScene?.Name}'.");
            DespawnScene();
        }

        pendingAutoSpawnScene = null;
        pendingAutoSpawnTicks = 0;
        staggeredSpawnQueue.Clear();
        spawnIntervalTicks = 0;

        // 2. Auto Spawn 対象シーンの探索 (ロード画面中は実行せず、ローディング完了を待機)
        var autoScene = Scenes.FirstOrDefault(s => s.TerritoryId == territoryType && s.AutoSpawn);
        if (autoScene != null)
        {
            logManager?.Info($"Territory {territoryType}: Scene '{autoScene.Name}' is marked for Auto Spawn. Waiting for world and LocalPlayer to fully load...");
            pendingAutoSpawnScene = autoScene;
            pendingAutoSpawnTicks = 60; // 自キャラ出現後 60 フレーム (約1秒) 待機して安全スポーン
        }
    }

    /// <summary>
    /// Framework.Update ごとに呼び出され、ゾーンロード完了待機、Auto Spawn、および非同期フレーム分散スポーンを実行
    /// </summary>
    public void UpdateFrame()
    {
        // 1. Auto Spawn 待機処理
        if (pendingAutoSpawnScene != null)
        {
            // ログイン状態およびゾーンIDの一致確認
            if (!clientState.IsLoggedIn || clientState.TerritoryType != pendingAutoSpawnScene.TerritoryId)
            {
                return;
            }

            // 自キャラ (LocalPlayer) がワールドに完全に生成され、準備完了しているか確認
            if (!actorManager.IsLocalPlayerReady)
            {
                return;
            }

            // 安全マージン待機カウントダウン (ゾーン暗転明けの確実な待機)
            if (pendingAutoSpawnTicks > 0)
            {
                pendingAutoSpawnTicks--;
                return;
            }

            // 安全確認完了: スポーン実行
            var sceneToSpawn = pendingAutoSpawnScene;
            pendingAutoSpawnScene = null;

            logManager?.Info($"World stabilized and LocalPlayer ready. Auto-spawning scene '{sceneToSpawn.Name}' on territory {clientState.TerritoryType}.");
            SpawnScene(sceneToSpawn);
        }

        // 2. 非同期フレーム分散（スタッガー）スポーンキューの処理
        if (staggeredSpawnQueue.Count > 0)
        {
            if (spawnIntervalTicks > 0)
            {
                spawnIntervalTicks--;
            }
            else
            {
                var placement = staggeredSpawnQueue.Dequeue();
                if (!IsPlacementSpawned(placement.PlacementId) && placement.IsVisible)
                {
                    SpawnPlacementInternal(placement);
                }
                spawnIntervalTicks = DefaultSpawnIntervalTicks;
            }
        }
    }

    #region Persistence (Load / Save)

    public void LoadScenes()
    {
        try
        {
            if (File.Exists(scenesFilePath))
            {
                var json = File.ReadAllText(scenesFilePath);
                var token = Newtonsoft.Json.Linq.JToken.Parse(json);
                if (token is Newtonsoft.Json.Linq.JArray)
                {
                    // 旧フォーマット: List<SceneData>
                    var loaded = JsonConvert.DeserializeObject<List<SceneData>>(json);
                    if (loaded != null)
                    {
                        Scenes = loaded;
                    }
                }
                else
                {
                    // 新フォーマット: SceneSaveData (Scenes + Folders)
                    var loadedData = JsonConvert.DeserializeObject<SceneSaveData>(json);
                    if (loadedData != null)
                    {
                        Scenes = loadedData.Scenes ?? new();
                        Folders = loadedData.Folders ?? new();
                    }
                }

                // シーン内の FolderPath から未登録のフォルダも自動マージ
                foreach (var scene in Scenes)
                {
                    if (!string.IsNullOrWhiteSpace(scene.FolderPath) && !Folders.Contains(scene.FolderPath))
                    {
                        AddFolderInternal(scene.FolderPath);
                    }
                }

                logManager?.Info($"Loaded {Scenes.Count} scenes and {Folders.Count} folders from {scenesFilePath}.");
                if (SelectedScene == null && Scenes.Count > 0)
                {
                    SelectedScene = Scenes[0];
                    SelectedPlacement = SelectedScene.Placements.FirstOrDefault();
                }
                return;
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to load scenes: {ex.Message}");
        }

        Scenes = new List<SceneData>();
        Folders = new List<string>();
    }

    public void SaveScenes()
    {
        try
        {
            var data = new SceneSaveData
            {
                Scenes = this.Scenes,
                Folders = this.Folders
            };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(scenesFilePath, json);
            logManager?.Info($"Saved {Scenes.Count} scenes to {scenesFilePath}.");
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to save scenes: {ex.Message}");
        }
    }

    #endregion

    #region Folder Management

    public void AddFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        folderPath = folderPath.Trim().Replace('\\', '/');
        if (!Folders.Contains(folderPath))
        {
            AddFolderInternal(folderPath);
            SaveScenes();
        }
    }

    private void AddFolderInternal(string folderPath)
    {
        var parts = folderPath.Split('/');
        var current = "";
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            current = string.IsNullOrEmpty(current) ? p : $"{current}/{p}";
            if (!Folders.Contains(current))
            {
                Folders.Add(current);
            }
        }
    }

    public void DeleteFolder(string folderPath)
    {
        Folders.RemoveAll(f => f == folderPath || f.StartsWith(folderPath + "/"));
        foreach (var s in Scenes.Where(s => s.FolderPath == folderPath || s.FolderPath.StartsWith(folderPath + "/")))
        {
            s.FolderPath = "";
        }
        SaveScenes();
    }

    #endregion

    #region Scene CRUD

    public SceneData CreateScene(string name = "新規シーン", uint territoryId = 0, string territoryName = "", string folderPath = "")
    {
        var scene = new SceneData
        {
            Id = Guid.NewGuid(),
            Name = name,
            TerritoryId = territoryId,
            TerritoryName = territoryName,
            FolderPath = folderPath,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Scenes.Add(scene);
        SelectedScene = scene;
        SelectedPlacement = null;
        SaveScenes();
        logManager?.Info($"Created new scene '{name}' in folder '{folderPath}' ({scene.Id}).");
        return scene;
    }

    public void DeleteScene(SceneData scene)
    {
        if (ActiveSpawnedScene?.Id == scene.Id)
        {
            DespawnScene();
        }

        Scenes.Remove(scene);
        if (SelectedScene?.Id == scene.Id)
        {
            SelectedScene = Scenes.FirstOrDefault();
        }
        SaveScenes();
        logManager?.Info($"Deleted scene '{scene.Name}' ({scene.Id}).");
    }

    public SceneActorPlacement AddPlacement(SceneData scene, CharacterTemplate template, Vector3 position, float rotation)
    {
        var placement = new SceneActorPlacement
        {
            PlacementId = Guid.NewGuid(),
            CharacterTemplateId = template.Id,
            CustomDisplayName = template.Name,
            Position = position,
            Rotation = rotation
        };
        scene.Placements.Add(placement);
        scene.UpdatedAt = DateTime.UtcNow;
        SaveScenes();
        logManager?.Info($"Added actor placement '{template.Name}' to scene '{scene.Name}'.");
        return placement;
    }

    public void RemovePlacement(SceneData scene, SceneActorPlacement placement)
    {
        if (IsPlacementSpawned(placement.PlacementId))
        {
            DespawnPlacement(placement);
        }

        scene.Placements.Remove(placement);
        scene.UpdatedAt = DateTime.UtcNow;
        SaveScenes();
        logManager?.Info($"Removed actor placement from scene '{scene.Name}'.");
    }

    #endregion

    #region Spawning & Lifecycle

    public bool IsPlacementSpawned(Guid placementId)
    {
        return spawnedSceneActors.ContainsKey(placementId);
    }

    public bool IsSceneSpawned(SceneData scene)
    {
        return ActiveSpawnedScene?.Id == scene.Id && (spawnedSceneActors.Count > 0 || staggeredSpawnQueue.Count > 0);
    }

    public SpawnedActorData? GetSpawnedActor(Guid placementId)
    {
        spawnedSceneActors.TryGetValue(placementId, out var actor);
        return actor;
    }

    /// <summary>
    /// シーン内のすべての配置キャラクターを非同期フレーム分散（スタッガー）キューで順次スポーン
    /// 自キャラに近いアクターから優先スポーン（363msヒッチ解消・将来100体規模対応）
    /// </summary>
    public int SpawnScene(SceneData scene)
    {
        if (ActiveSpawnedScene != null && ActiveSpawnedScene.Id != scene.Id)
        {
            DespawnScene();
        }

        ActiveSpawnedScene = scene;
        staggeredSpawnQueue.Clear();
        spawnIntervalTicks = DefaultSpawnIntervalTicks;

        Vector3 playerPos = actorManager.LocalPlayerPosition;

        // 自キャラからの距離でソート（近いアクターから優先順位を高くして順次スポーン）
        var sortedPlacements = scene.Placements
            .Where(p => !IsPlacementSpawned(p.PlacementId) && p.IsVisible)
            .OrderBy(p => Vector3.DistanceSquared(playerPos, p.Position))
            .ToList();

        foreach (var placement in sortedPlacements)
        {
            staggeredSpawnQueue.Enqueue(placement);
        }

        logManager?.Info($"Enqueued {staggeredSpawnQueue.Count} actors for staggered spawning in scene '{scene.Name}' (Sorted by proximity to player).");
        return sortedPlacements.Count;
    }

    /// <summary>
    /// 現在スポーン中のシーンアクターを一括デスポーン
    /// </summary>
    public void DespawnScene()
    {
        staggeredSpawnQueue.Clear();
        spawnIntervalTicks = 0;

        if (spawnedSceneActors.Count == 0 && ActiveSpawnedScene == null)
            return;

        logManager?.Info($"Despawning active scene '{ActiveSpawnedScene?.Name}' ({spawnedSceneActors.Count} actors)...");

        var placements = spawnedSceneActors.Keys.ToList();
        foreach (var pid in placements)
        {
            if (spawnedSceneActors.TryGetValue(pid, out var actor))
            {
                actorManager.DespawnCharacter(actor);
            }
        }
        spawnedSceneActors.Clear();
        ActiveSpawnedScene = null;
    }

    /// <summary>
    /// 特定の配置キャラクターの表示／非表示（目のアイコン）を切り替え
    /// </summary>
    public void TogglePlacementVisibility(SceneData scene, SceneActorPlacement placement)
    {
        placement.IsVisible = !placement.IsVisible;
        SaveScenes();

        if (IsSceneSpawned(scene))
        {
            if (!placement.IsVisible)
            {
                DespawnPlacement(placement);
            }
            else
            {
                SpawnPlacement(scene, placement);
            }
        }
    }

    /// <summary>
    /// 特定の配置キャラクターを個別にスポーン
    /// </summary>
    public SpawnedActorData? SpawnPlacement(SceneData scene, SceneActorPlacement placement)
    {
        if (IsPlacementSpawned(placement.PlacementId))
        {
            logManager?.Warning($"Placement '{placement.CustomDisplayName}' is already spawned.");
            return spawnedSceneActors[placement.PlacementId];
        }

        ActiveSpawnedScene ??= scene;
        return SpawnPlacementInternal(placement);
    }

    /// <summary>
    /// 特定の配置キャラクターを個別にデスポーン
    /// </summary>
    public void DespawnPlacement(SceneActorPlacement placement)
    {
        if (spawnedSceneActors.TryGetValue(placement.PlacementId, out var actor))
        {
            actorManager.DespawnCharacter(actor);
            spawnedSceneActors.Remove(placement.PlacementId);
            logManager?.Info($"Despawned scene actor '{placement.CustomDisplayName}'.");

            if (spawnedSceneActors.Count == 0)
            {
                ActiveSpawnedScene = null;
            }
        }
    }

    private SpawnedActorData? SpawnPlacementInternal(SceneActorPlacement placement)
    {
        var template = configuration.Templates.FirstOrDefault(t => t.Id == placement.CharacterTemplateId);
        if (template == null)
        {
            logManager?.Warning($"Cannot spawn placement: Template '{placement.CharacterTemplateId}' not found in library.");
            return null;
        }

        // 第1工程のコア SpawnCharacter を呼び出し（完全隔離・安全実行）
        var spawned = actorManager.SpawnCharacter(template, placement.Position, placement.Rotation);
        if (spawned != null)
        {
            spawned.Transform.Scale = placement.Scale > 0 ? placement.Scale : 1.0f;
            if (!string.IsNullOrWhiteSpace(placement.CustomDisplayName))
            {
                spawned.DisplayName = placement.CustomDisplayName;
            }
            spawnedSceneActors[placement.PlacementId] = spawned;
            logManager?.Info($"Spawned scene actor '{spawned.DisplayName}' at {placement.Position}.");
            return spawned;
        }

        logManager?.Error($"Failed to spawn scene actor '{placement.CustomDisplayName}'.");
        return null;
    }

    #endregion

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        pendingAutoSpawnScene = null;
        DespawnScene();
    }
}
