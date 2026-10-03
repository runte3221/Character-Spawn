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
    /// 現在選択／編集中のシーン
    /// </summary>
    public SceneData? SelectedScene { get; set; }

    /// <summary>
    /// 現在スポーン中のシーン
    /// </summary>
    public SceneData? ActiveSpawnedScene { get; private set; }

    /// <summary>
    /// スポーンされた配置アクターの実体管理 (PlacementId -> SpawnedActorData)
    /// </summary>
    private readonly Dictionary<Guid, SpawnedActorData> spawnedSceneActors = new();

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
        if (ActiveSpawnedScene != null || spawnedSceneActors.Count > 0)
        {
            logManager?.Info($"Territory changed to {territoryType}. Automatically despawning active scene '{ActiveSpawnedScene?.Name}'.");
            DespawnScene();
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
                var loaded = JsonConvert.DeserializeObject<List<SceneData>>(json);
                if (loaded != null)
                {
                    Scenes = loaded;
                    logManager?.Info($"Loaded {Scenes.Count} scenes from {scenesFilePath}.");
                    if (SelectedScene == null && Scenes.Count > 0)
                    {
                        SelectedScene = Scenes[0];
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to load scenes: {ex.Message}");
        }

        // デフォルト空リスト
        Scenes = new List<SceneData>();
    }

    public void SaveScenes()
    {
        try
        {
            var json = JsonConvert.SerializeObject(Scenes, Formatting.Indented);
            File.WriteAllText(scenesFilePath, json);
            logManager?.Info($"Saved {Scenes.Count} scenes to {scenesFilePath}.");
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to save scenes: {ex.Message}");
        }
    }

    #endregion

    #region Scene CRUD

    public SceneData CreateScene(string name = "新規シーン", uint territoryId = 0)
    {
        var scene = new SceneData
        {
            Id = Guid.NewGuid(),
            Name = name,
            TerritoryId = territoryId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Scenes.Add(scene);
        SelectedScene = scene;
        SaveScenes();
        logManager?.Info($"Created new scene '{name}' ({scene.Id}).");
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
        return ActiveSpawnedScene?.Id == scene.Id && spawnedSceneActors.Count > 0;
    }

    public SpawnedActorData? GetSpawnedActor(Guid placementId)
    {
        spawnedSceneActors.TryGetValue(placementId, out var actor);
        return actor;
    }

    /// <summary>
    /// シーン内のすべての配置キャラクターを一括スポーン
    /// </summary>
    public int SpawnScene(SceneData scene)
    {
        if (ActiveSpawnedScene != null && ActiveSpawnedScene.Id != scene.Id)
        {
            DespawnScene();
        }

        ActiveSpawnedScene = scene;
        int spawnedCount = 0;

        foreach (var placement in scene.Placements)
        {
            if (IsPlacementSpawned(placement.PlacementId))
                continue;

            var spawned = SpawnPlacementInternal(placement);
            if (spawned != null)
            {
                spawnedCount++;
            }
        }

        logManager?.Info($"Spawned scene '{scene.Name}': {spawnedCount}/{scene.Placements.Count} actors active.");
        return spawnedCount;
    }

    /// <summary>
    /// 現在スポーン中のシーンアクターを一括デスポーン
    /// </summary>
    public void DespawnScene()
    {
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
        DespawnScene();
    }
}
