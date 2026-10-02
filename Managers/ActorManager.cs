using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterCopyFlags = FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterSetupContainer.CopyFlags;
using ClientObjectManager = FFXIVClientStructs.FFXIV.Client.Game.Object.ClientObjectManager;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.Managers;

public unsafe class ActorManager : IDisposable
{
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly LogManager? logManager;
    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser? mcdfParser;
    private readonly IDalamudPluginInterface? pluginInterface;
    private readonly CustomizePlusIpc? customizePlusIpc;
    private readonly GameDataService? gameDataService;
    private readonly IFramework? framework;

    private readonly List<SpawnedActorData> activeActors = new();
    private readonly List<ushort> createdIndexes = new();

    // HDM (Enceladeum/HDM) 準拠: Draw-when-ready 2フェーズ待機キュー
    private sealed class ReadyJob
    {
        public SpawnedActorData Spawned = null!;
        public CharacterTemplate Template = null!;
        public ushort GlobalIndex;
        public int Ticks;
        public bool DrawEnabled;
    }
    private readonly List<ReadyJob> readyJobs = new();

    // HDM GuiseService.cs 準拠: モンスター/再描画待機キュー (Phase: WaitEnable)
    private sealed class MonsterRedrawJob
    {
        public SpawnedActorData Spawned = null!;
        public ushort GlobalIndex;
        public int Ticks;
    }
    private readonly List<MonsterRedrawJob> monsterRedrawJobs = new();

    private class PendingNpcJob
    {
        public SpawnedActorData Spawned { get; set; } = null!;
        public CharacterTemplate Template { get; set; } = null!;
        public ushort GlobalIndex { get; set; }
        public int Attempts { get; set; }
    }
    private readonly List<PendingNpcJob> pendingNpcJobs = new();

    private const int ReadyWarmupTicks = 2;
    private const int MaxReadyTicks = 200;


    // 単一プレビューアクター（Characterタブ専用）
    public SpawnedActorData? CurrentPreviewActor { get; private set; }

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    public ActorManager(
        IClientState clientState,
        IObjectTable objectTable,
        ISigScanner? sigScanner,
        IPluginLog log,
        TimelineManager timelineManager,
        HeadTrackingManager headTrackingManager,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc,
        LogManager? logManager = null,
        McdfParser? mcdfParser = null,
        IDalamudPluginInterface? pluginInterface = null,
        CustomizePlusIpc? customizePlusIpc = null,
        GameDataService? gameDataService = null,
        IFramework? framework = null)
    {
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.logManager = logManager;
        this.timelineManager = timelineManager;
        this.headTrackingManager = headTrackingManager;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;
        this.mcdfParser = mcdfParser;
        this.pluginInterface = pluginInterface;
        this.customizePlusIpc = customizePlusIpc;
        this.gameDataService = gameDataService;
        this.framework = framework;

        this.clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        logManager?.Info("Territory changed. Clearing spawned actors tracking.");
        foreach (var actor in activeActors)
        {
            if (actor.TemporaryCollectionGuid.HasValue)
            {
                penumbraIpc.DeleteTemporaryCollection(actor.TemporaryCollectionGuid.Value);
            }
            if (actor.TemporaryCustomizePlusGuid.HasValue && customizePlusIpc != null)
            {
                customizePlusIpc.DeleteTemporaryProfile(actor.TemporaryCustomizePlusGuid.Value);
            }
        }
        readyJobs.Clear();
        monsterRedrawJobs.Clear();
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
    }

    private string GetPuppetName(CharacterTemplate template)
    {
        // FF14 の PlayerIdentifier / VerifyPlayerName 規則:
        // 数字 (0-9) は絶対不可！英字のみ (A-Z, a-z) で構成された 2 単語が必要
        // 形式: Forename (3〜15文字, 先頭大文字) + " " + Surname (3〜15文字, 先頭大文字)
        byte[] bytes;
        if (Guid.TryParse(template.Id, out var g))
        {
            bytes = g.ToByteArray();
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(template.Id);
        }

        // 8文字の完全アルファベット Surname を生成 (例: "Evjkkhzl")
        // 26^8 通り (約2088億通り) で衝突を完全に防止しつつ、VerifyPlayerName を100%パスする
        char[] surname = new char[8];
        surname[0] = (char)('A' + (bytes[0] % 26));
        for (int i = 1; i < 8; i++)
        {
            int b = i < bytes.Length ? bytes[i] : bytes[i % bytes.Length];
            surname[i] = (char)('a' + (b % 26));
        }

        return $"Csp {new string(surname)}";
    }

    /// <summary>
    /// Characterタブ専用のプレビュー用スポーン
    /// 既存のプレビューアクターがあれば自動的にデスポーンしてから新しく生成する
    /// </summary>
    public SpawnedActorData? SpawnPreviewCharacter(CharacterTemplate template)
    {
        DespawnPreviewCharacter();

        var spawned = SpawnCharacter(template);
        if (spawned != null)
        {
            CurrentPreviewActor = spawned;
            logManager?.Info($"Set CurrentPreviewActor to '{spawned.DisplayName}' (Instance: {spawned.InstanceId}).");
        }
        return spawned;
    }

    /// <summary>
    /// Characterタブ専用のプレビューアクターを破棄
    /// </summary>
    public void DespawnPreviewCharacter()
    {
        if (CurrentPreviewActor != null)
        {
            logManager?.Info($"Despawning current preview character: {CurrentPreviewActor.DisplayName}");
            DespawnCharacter(CurrentPreviewActor);
            CurrentPreviewActor = null;
        }
    }

    /// <summary>
    /// テンプレートをもとに新しいキャラクターをスポーンする (HDM & AQR アーキテクチャ準拠)
    /// </summary>
    public SpawnedActorData? SpawnCharacter(CharacterTemplate template, Vector3? spawnPosition = null, float? spawnRotation = null)
    {
        try
        {
            var localPlayer = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
            if (localPlayer == null || localPlayer.Address == 0)
            {
                logManager?.Error("Spawn failed: Local player not found. Cannot seed character clone.");
                return null;
            }

            var com = ClientObjectManager.Instance();
            if (com == null)
            {
                logManager?.Error("Spawn failed: ClientObjectManager instance is null.");
                return null;
            }

            // COMの空きスロットを作成（自動割り当て）
            uint comId = com->CreateBattleCharacter();
            if (comId == 0xFFFFFFFF)
            {
                logManager?.Error("ClientObjectManager.CreateBattleCharacter returned 0xFFFFFFFF (slot limit reached).");
                return null;
            }

            ushort comIdx = (ushort)comId;
            var newObject = com->GetObjectByIndex(comIdx);
            if (newObject == null)
            {
                logManager?.Error($"Failed to retrieve spawned GameObject at COM index {comIdx}. Reclaiming slot.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            var nativeChara = (Character*)newObject;
            var meNative = (Character*)localPlayer.Address;
            var pos = spawnPosition ?? GetDefaultSpawnPosition();
            var rot = spawnRotation ?? localPlayer.Rotation;

            // モンスターモデルIDの補完 (未設定の場合のみ補完)
            if (template.SourceType == CharacterSourceType.Monster && template.ModelCharaId == 0 && template.DataId > 0 && gameDataService != null)
            {
                var resolvedId = gameDataService.GetMonsterModelCharaId(template.DataId);
                if (resolvedId > 0)
                {
                    logManager?.Info($"Resolved Monster ModelCharaId for '{template.Name}' to {resolvedId} (DataId: {template.DataId}).");
                    template.ModelCharaId = resolvedId;
                }
            }

            // NPCデータの補完 (CustomizeData または NpcEquipmentModelIds の補完)
            if (template.SourceType == CharacterSourceType.Npc && template.DataId > 0 && gameDataService != null)
            {
                if (template.CustomizeData == null && template.NpcEquipmentModelIds == null)
                {
                    var app = gameDataService.GetNpcAppearanceData(template.DataId);
                    if (app != null)
                    {
                        template.ModelCharaId = app.ModelCharaId;
                        template.CustomizeData = app.CustomizeData;
                        template.NpcEquipmentModelIds = app.EquipmentModelIds;
                        template.McType = app.McType;
                        logManager?.Info($"Auto-resolved NPC appearance data for '{template.Name}' from ENpcId {template.DataId} (ModelChara: {template.ModelCharaId}, McType: {template.McType}).");
                    }
                }
            }

            logManager?.Info($"Spawning '{template.Name}' (Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, Weapon: {template.WeaponVisible}) at COM#{comIdx}...");

            // HDM 黄金パターン:
            // 1. スポーン直後に描画を無効化（自キャラの姿が一瞬でも表示されるのを防ぐ）
            nativeChara->GameObject.DisableDraw();

            // 2. 自キャラからベースラインをコピーして drawable 骨格を確立
            nativeChara->CharacterSetup.CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding);

            // 3. ベースラインのリセット
            nativeChara->ModelContainer.ModelCharaId = 0;
            nativeChara->GameObject.Scale = 1.0f;
            nativeChara->DrawData.HideWeapons(!template.WeaponVisible);
            nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            // 0.1.23 & AQR 黄金パターン:
            // BattleNpcSubKind.Player, OwnerId = 0xE000_0000, NameId = 0, HomeWorld を設定し、
            // Penumbra / Glamourer が正規のプレイヤー型アクターとして識別できるようにする
            nativeChara->GameObject.ObjectKind = ObjectKind.BattleNpc;
            nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;
            nativeChara->GameObject.OwnerId = 0xE000_0000;
            nativeChara->NameId = 0;
            nativeChara->HomeWorld = meNative->HomeWorld;
            nativeChara->GameObject.TargetableStatus = 0;
            nativeChara->GameObject.EventId = 0;

            string puppetName = GetPuppetName(template);
            nativeChara->GameObject.SetName(puppetName);

            // 位置・回転・透明度の設定
            nativeChara->GameObject.SetPosition(pos.X, pos.Y, pos.Z);
            nativeChara->GameObject.SetRotation(rot);
            nativeChara->GameObject.DefaultPosition = pos;
            nativeChara->GameObject.DefaultRotation = rot;
            nativeChara->Alpha = 1.0f;

            // グローバルインデックスの解決 (Two Index Spaces Trap 対策)
            var objRef = objectTable.CreateObjectReference((nint)nativeChara) as ICharacter;
            if (objRef == null)
            {
                logManager?.Error($"COM#{comIdx} created but no ICharacter reference resolved. Reclaiming slot.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            ushort globalIdx = (ushort)objRef.ObjectIndex;
            if (globalIdx == localPlayer.ObjectIndex)
            {
                logManager?.Error($"Spawn: Puppet resolved to local player's global index ({globalIdx})! Reclaiming COM#{comIdx}.");
                com->DeleteObjectByIndex(comIdx, 0);
                return null;
            }

            var spawned = new SpawnedActorData
            {
                TemplateId = template.Id,
                DisplayName = string.IsNullOrWhiteSpace(template.Name) ? puppetName : template.Name,
                PuppetName = puppetName,
                NativeAddress = (nint)nativeChara,
                GlobalIndex = globalIdx,
                ComIndex = comIdx,
                GameObjectId = nativeChara->EntityId,
                Transform = new TransformData
                {
                    Position = pos,
                    Rotation = rot,
                    Scale = 1.0f
                },
                NamePlate = new NamePlateSettings
                {
                    Show = true,
                    CustomName = template.Name
                },
                IsTargetable = false
            };

            // 4. MCDF および通常Penumbraコレクションの初期事前適用 (0.1.23 準拠)
            // ゲームエンジンが DrawObject を構築する前にコレクションを割り当てることで、MODテクスチャを初回から正しくバインドさせる
            // ※ Glamourer はまだ ActorObjectManager に登録されていないため、ここではコレクションのみ割り当て、Glamourer は ReadyJob で適用する
            if (template.ModelCharaId == 0 && template.SourceType != CharacterSourceType.Npc)
            {
                ApplyAppearanceDirect(nativeChara, globalIdx, template, spawned, applyGlamourer: false);
            }

            // 5. 描画準備完了待機ジョブにエンキュー（IsReadyToDraw() を待って EnableDraw() を実行）
            readyJobs.Add(new ReadyJob
            {
                Spawned = spawned,
                Template = template,
                GlobalIndex = globalIdx,
                Ticks = 0,
                DrawEnabled = false
            });

            activeActors.Add(spawned);
            createdIndexes.Add(globalIdx);

            logManager?.Info($"Spawn seeded: '{spawned.DisplayName}' at Global#{globalIdx} (COM#{comIdx}, Identity: '{puppetName}'). Waiting for draw-ready...");

            return spawned;
        }
        catch (Exception ex)
        {
            logManager?.Error($"Exception during SpawnCharacter: {ex}");
            return null;
        }
    }

    /// <summary>
    /// スポーン中アクターの位置・回転を更新
    /// </summary>
    public void UpdateActorTransform(SpawnedActorData actor, Vector3 newPosition, float newRotation)
    {
        actor.Transform.Position = newPosition;
        actor.Transform.Rotation = newRotation;

        if (actor.NativeAddress == 0 || !actor.IsReady) return;

        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var chara = (Character*)charaObj.Address;
                    chara->GameObject.SetPosition(newPosition.X, newPosition.Y, newPosition.Z);
                    chara->GameObject.SetRotation(newRotation);
                    chara->GameObject.DefaultPosition = newPosition;
                    chara->GameObject.DefaultRotation = newRotation;
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"UpdateActorTransform failed: {ex.Message}");
        }
    }

    /// <summary>
    /// アニメーション・表情の適用
    /// </summary>
    public void ApplyActorAnimation(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var chara = (Character*)charaObj.Address;
                    timelineManager.ApplyTimeline(chara, actor.Animation);
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"ApplyActorAnimation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// ターゲット可否設定を適用
    /// </summary>
    public void ApplyTargetable(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        try
        {
            if (actor.GlobalIndex < objectTable.Length)
            {
                var obj = objectTable[actor.GlobalIndex];
                if (obj is ICharacter charaObj && charaObj.Address != nint.Zero)
                {
                    var go = (GameObject*)charaObj.Address;
                    if (actor.IsTargetable)
                        go->TargetableStatus |= ObjectTargetableFlags.IsTargetable;
                    else
                        go->TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"ApplyTargetable failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定アクターをデスポーン（破棄）: HDM & Brio DestroyObject パターン
    /// </summary>
    public void DespawnCharacter(SpawnedActorData actor)
    {
        try
        {
            actor.IsReady = false;
            readyJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);
            monsterRedrawJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);
            pendingNpcJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);

            // Penumbra 一時コレクションのクリーンアップ (AQR / Mare 準拠)
            if (actor.TemporaryCollectionGuid.HasValue)
            {
                penumbraIpc.DeleteTemporaryCollection(actor.TemporaryCollectionGuid.Value);
                actor.TemporaryCollectionGuid = null;
            }

            // Penumbra コレクション割り当ての完全解除 (通常 & 一時)
            if (penumbraIpc.IsAvailable)
            {
                penumbraIpc.UnassignCollectionForActor(actor.GlobalIndex);
            }

            // Glamourer ロック解除 (削除するアクターに対して RevertState は非同期再描画を誘発して武器残存の原因になるため呼ばない)
            if (glamourerIpc != null && glamourerIpc.IsAvailable)
            {
                glamourerIpc.UnlockState(actor.GlobalIndex, actor.PuppetName);
                if (!string.IsNullOrEmpty(actor.DisplayName) && actor.DisplayName != actor.PuppetName)
                {
                    glamourerIpc.UnlockState(actor.GlobalIndex, actor.DisplayName);
                }
            }

            // CustomizePlus プロファイル紐付けおよび一時プロファイルの完全クリーンアップ (AQR / Caraxi 準拠)
            if (customizePlusIpc != null && customizePlusIpc.IsAvailable)
            {
                if (actor.AssignedCustomizePlusGuid.HasValue && !string.IsNullOrEmpty(actor.PuppetName))
                {
                    ushort worldId = 0;
                    if (actor.NativeAddress != 0)
                    {
                        var c = (Character*)actor.NativeAddress;
                        worldId = (ushort)c->HomeWorld;
                    }
                    customizePlusIpc.RemovePlayerCharacter(actor.AssignedCustomizePlusGuid.Value, actor.PuppetName, worldId);
                    actor.AssignedCustomizePlusGuid = null;
                }

                if (actor.TemporaryCustomizePlusGuid.HasValue)
                {
                    customizePlusIpc.DeleteTemporaryProfile(actor.TemporaryCustomizePlusGuid.Value);
                    actor.TemporaryCustomizePlusGuid = null;
                }
            }

            if (actor.NativeAddress != 0)
            {
                var chara = (Character*)actor.NativeAddress;
                try
                {
                    // 武器のグラフィックフラグを非表示にし、描画パイプラインから完全アンロード (孤立武器残留防止)
                    chara->DrawData.HideWeapons(true);
                    chara->DrawData.IsWeaponHidden = true;
                    chara->GameObject.DisableDraw();
                }
                catch { }

                var com = ClientObjectManager.Instance();
                if (com != null)
                {
                    // ライブオブジェクトから最新の COM インデックスを解決して正規破棄
                    var comIdx = com->GetIndexByObject((GameObject*)actor.NativeAddress);
                    if (comIdx != 0xFFFFFFFF)
                    {
                        com->DeleteObjectByIndex((ushort)comIdx, 0);
                        logManager?.Info($"Despawned actor '{actor.DisplayName}' (COM#{comIdx}, Global#{actor.GlobalIndex}).");
                    }
                    else
                    {
                        logManager?.Warning($"Despawn: actor '{actor.DisplayName}' not COM-managed (GetIndexByObject returned 0xFFFFFFFF).");
                    }
                }
                actor.NativeAddress = 0;
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to despawn actor {actor.DisplayName}: {ex.Message}");
        }
        finally
        {
            createdIndexes.Remove(actor.GlobalIndex);
            activeActors.Remove(actor);
            if (CurrentPreviewActor == actor)
            {
                CurrentPreviewActor = null;
            }
        }
    }

    /// <summary>
    /// 全アクターを一括デスポーン
    /// </summary>
    public void DespawnAll()
    {
        readyJobs.Clear();
        monsterRedrawJobs.Clear();
        pendingNpcJobs.Clear();
        var list = activeActors.ToList();
        foreach (var actor in list)
        {
            DespawnCharacter(actor);
        }
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
        logManager?.Info("Despawned all active actors.");
    }

    /// <summary>
    /// 毎フレームの更新処理: HDM Draw-when-ready 2フェーズポーリング & 視線追従 & モンスターRedraw
    /// </summary>
    public void UpdateFrame()
    {
        try
        {
            headTrackingManager.UpdateTracking(activeActors);

            // 1. HDM Draw-when-ready 2フェーズポーリング (人間ベースラインの実体化待機)
            if (readyJobs.Count > 0)
            {
                for (int i = readyJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = readyJobs[i];
                        job.Ticks++;

                        // 追跡解除されたアクターは除外
                        if (!activeActors.Contains(job.Spawned))
                        {
                            readyJobs.RemoveAt(i);
                            continue;
                        }

                        // ウォームアップ待機（Brio dontStartFor: 2）
                        if (job.Ticks <= ReadyWarmupTicks) continue;

                        // HDM 黄金律: 生ポインタは決して信用しない！毎フレーム ObjectTable から新鮮に解決し直す
                        if (job.GlobalIndex >= objectTable.Length)
                        {
                            if (job.Ticks >= MaxReadyTicks) readyJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            if (job.Ticks >= MaxReadyTicks)
                            {
                                logManager?.Warning($"ReadyJob: Timed out waiting for ObjectTable entry at Global#{job.GlobalIndex}.");
                                readyJobs.RemoveAt(i);
                            }
                            continue;
                        }

                        // 最新のネイティブアドレスを同期
                        job.Spawned.NativeAddress = charaObj.Address;
                        var chara = (Character*)charaObj.Address;

                        // Phase 1: IsReadyToDraw() を待って EnableDraw() を実行
                        if (!job.DrawEnabled)
                        {
                            bool ready = false;
                            try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                            if (!ready && job.Ticks < MaxReadyTicks) continue;

                            try { chara->GameObject.EnableDraw(); } catch { }
                            job.DrawEnabled = true;
                            continue;
                        }

                        // Phase 2: 人間ベースライン描画オブジェクトの可視化待機
                        var draw = chara->GameObject.DrawObject;
                        bool visible = false;
                        try { visible = draw != null && draw->IsVisible; } catch { }
                        if (!visible && job.Ticks < MaxReadyTicks) continue;

                        // 人間ベースラインが完全に描画実体化した！
                        readyJobs.RemoveAt(i);

                        // A. モンスター / 非人型モデル (ModelCharaId > 0) の場合: HDM GuiseService.cs 準拠
                        if (job.Template.ModelCharaId > 0)
                        {
                            chara->GameObject.ObjectKind = ObjectKind.BattleNpc;
                            chara->ModelContainer.ModelCharaId = (int)job.Template.ModelCharaId;
                            chara->GameObject.Scale = job.Template.Scale > 0 ? job.Template.Scale : 1.0f;
                            chara->DrawData.HideWeapons(true);
                            chara->DrawData.IsWeaponHidden = true;

                            // Demihuman 装備データの補完＆適用 (モーグリ・ナマズオ等の McType 2)
                            if (job.Template.NpcEquipmentModelIds == null && job.Template.DataId > 0 && gameDataService != null)
                            {
                                var app = gameDataService.GetNpcAppearanceData(job.Template.DataId);
                                if (app != null && app.EquipmentModelIds != null)
                                {
                                    job.Template.NpcEquipmentModelIds = app.EquipmentModelIds;
                                }
                            }

                            if (job.Template.NpcEquipmentModelIds != null && job.Template.NpcEquipmentModelIds.Length > 0)
                            {
                                var equipSpan = chara->DrawData.EquipmentModelIds;
                                for (int idx = 0; idx < job.Template.NpcEquipmentModelIds.Length && idx < equipSpan.Length; idx++)
                                {
                                    equipSpan[idx] = new EquipmentModelId { Value = job.Template.NpcEquipmentModelIds[idx] };
                                }
                                chara->DrawData.IsHatHidden = false;
                            }

                            chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);

                            // モンスターモデルへの再描画を開始: DisableDraw() して RedrawJob にエンキュー (HDM BeginRedraw 方式)
                            chara->GameObject.DisableDraw();
                            monsterRedrawJobs.Add(new MonsterRedrawJob
                            {
                                Spawned = job.Spawned,
                                GlobalIndex = job.GlobalIndex,
                                Ticks = 0
                            });
                            logManager?.Info($"Transitioning obj#{job.GlobalIndex} '{job.Spawned.DisplayName}' to Monster ModelCharaId {job.Template.ModelCharaId} via RedrawJob...");
                            continue;
                        }

                        // B. 人型 NPC (SourceType == Npc かつ ModelCharaId == 0) の場合: HDM HumanGuise.cs 準拠 (非ブロッキングキュー)
                        if (job.Template.SourceType == CharacterSourceType.Npc)
                        {
                            pendingNpcJobs.Add(new PendingNpcJob
                            {
                                Spawned = job.Spawned,
                                Template = job.Template,
                                GlobalIndex = job.GlobalIndex,
                                Attempts = 0
                            });
                            logManager?.Info($"Enqueued obj#{job.GlobalIndex} '{job.Spawned.DisplayName}' to PendingNpcJob for non-blocking Glamourer appearance sync...");
                            continue;
                        }

                        // C. その他の人型アクター (MCDF / Glamourer / PlayerClone) の最終確定
                        try
                        {
                            ApplyAppearanceDirect(chara, job.GlobalIndex, job.Template, job.Spawned);
                            job.Spawned.IsReady = true;
                            logManager?.Info($"Appearance finalized for humanoid '{job.Spawned.DisplayName}' on Global#{job.GlobalIndex}.");
                        }
                        catch (Exception ex)
                        {
                            logManager?.Error($"Error finalizing appearance for {job.Spawned.DisplayName}: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"ReadyJob exception: {ex}");
                        if (i < readyJobs.Count) readyJobs.RemoveAt(i);
                    }
                }
            }

            // 2. 人型 NPC 非同期外見適用キュー (HDM HumanGuise.cs 準拠のフレームポーリング)
            if (pendingNpcJobs.Count > 0)
            {
                for (int i = pendingNpcJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = pendingNpcJobs[i];
                        job.Attempts++;

                        if (!activeActors.Contains(job.Spawned))
                        {
                            pendingNpcJobs.RemoveAt(i);
                            continue;
                        }

                        if (job.GlobalIndex >= objectTable.Length)
                        {
                            if (job.Attempts > 120) pendingNpcJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            if (job.Attempts > 120) pendingNpcJobs.RemoveAt(i);
                            continue;
                        }

                        job.Spawned.NativeAddress = charaObj.Address;
                        var chara = (Character*)charaObj.Address;

                        if (!glamourerIpc.IsAvailable)
                        {
                            // Glamourer が無い場合はダイレクトフォールバック
                            ApplyNpcAppearanceDirectFallback(chara, job.Template);
                            job.Spawned.IsReady = true;
                            pendingNpcJobs.RemoveAt(i);
                            continue;
                        }

                        var outcome = glamourerIpc.TryApplyNpcAppearance(
                            job.GlobalIndex,
                            job.Template.CustomizeData,
                            job.Template.NpcEquipmentModelIds,
                            showHeadgear: true
                        );

                        if (outcome == GlamourerIpc.NpcApplyResult.StateNull)
                        {
                            if (job.Attempts > 120) // 最大120フレーム（約2秒）待機
                            {
                                pendingNpcJobs.RemoveAt(i);
                                logManager?.Warning($"PendingNpcJob: Glamourer state timeout after 120 frames for obj#{job.GlobalIndex} '{job.Spawned.DisplayName}'. Falling back to direct appearance.");
                                ApplyNpcAppearanceDirectFallback(chara, job.Template);
                                job.Spawned.IsReady = true;
                            }
                            continue;
                        }

                        // 適用完了（Applied または Failed）
                        pendingNpcJobs.RemoveAt(i);
                        job.Spawned.IsReady = true;
                        logManager?.Info($"PendingNpcJob: Applied NPC appearance for '{job.Spawned.DisplayName}' on Global#{job.GlobalIndex} after {job.Attempts} frame(s) ({outcome}).");

                        chara->DrawData.HideWeapons(!job.Template.WeaponVisible);
                        chara->DrawData.IsWeaponHidden = !job.Template.WeaponVisible;

                        // HDM RedrawGuise 準拠: スケルトン再構築と外見確定
                        if (penumbraIpc.IsAvailable)
                        {
                            penumbraIpc.Redraw(job.GlobalIndex);
                        }
                        else
                        {
                            chara->GameObject.DisableDraw();
                            monsterRedrawJobs.Add(new MonsterRedrawJob
                            {
                                Spawned = job.Spawned,
                                GlobalIndex = job.GlobalIndex,
                                Ticks = 0
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"PendingNpcJob exception: {ex}");
                        if (i < pendingNpcJobs.Count) pendingNpcJobs.RemoveAt(i);
                    }
                }
            }

            // 3. モンスター / 再描画待機キュー (HDM GuiseService RedrawPhase.WaitEnable 方式)
            if (monsterRedrawJobs.Count > 0)
            {
                for (int i = monsterRedrawJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = monsterRedrawJobs[i];
                        job.Ticks++;

                        if (!activeActors.Contains(job.Spawned))
                        {
                            monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        // 最低 2 フレーム待機 (DisableDraw がゲームエンジンに反映されるのを待つ)
                        if (job.Ticks < 2) continue;

                        if (job.GlobalIndex >= objectTable.Length)
                        {
                            if (job.Ticks >= MaxReadyTicks) monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            if (job.Ticks >= MaxReadyTicks) monsterRedrawJobs.RemoveAt(i);
                            continue;
                        }

                        job.Spawned.NativeAddress = charaObj.Address;
                        var chara = (Character*)charaObj.Address;

                        bool ready = false;
                        try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                        if (!ready && job.Ticks < MaxReadyTicks) continue;

                        try { chara->GameObject.EnableDraw(); } catch { }
                        job.Spawned.IsReady = true;
                        monsterRedrawJobs.RemoveAt(i);
                        logManager?.Info($"Monster/Actor redraw complete for '{job.Spawned.DisplayName}' on Global#{job.GlobalIndex} after {job.Ticks} ticks.");
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"MonsterRedrawJob exception: {ex}");
                        if (i < monsterRedrawJobs.Count) monsterRedrawJobs.RemoveAt(i);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"ActorManager.UpdateFrame outer exception: {ex}");
        }
    }

    private void ApplyNpcAppearanceDirectFallback(Character* chara, CharacterTemplate template)
    {
        if (template.CustomizeData != null && template.CustomizeData.Length >= 26)
        {
            fixed (byte* pCust = template.CustomizeData)
            {
                Buffer.MemoryCopy(pCust, &chara->DrawData.CustomizeData, 26, 26);
            }
        }

        if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
        {
            var equipSpan = chara->DrawData.EquipmentModelIds;
            for (int idx = 0; idx < template.NpcEquipmentModelIds.Length && idx < equipSpan.Length; idx++)
            {
                equipSpan[idx] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[idx] };
            }
        }

        chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);
    }

    /// <summary>
    /// 外見（Glamourer / Penumbra / MCDF）の即時直接適用
    /// HDM & AQR アーキテクチャ準拠
    /// </summary>
    public void ApplyAppearanceDirect(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null, bool applyGlamourer = true)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        logManager?.Info($"ApplyAppearanceDirect: '{template.Name}' (GlobalIndex: {actorIndex}, Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, ApplyGlamourer: {applyGlamourer})...");

        // Fresh な ICharacter 参照を ObjectTable から解決 (AQuestReborn 準拠: 生ポインタに直接外見適用)
        var charaObj = (globalIndex < objectTable.Length) ? objectTable[globalIndex] as ICharacter : null;

        // 前のキャラのステートや割り当てをリセット
        if (applyGlamourer && glamourerIpc != null && glamourerIpc.IsAvailable)
        {
            glamourerIpc.UnlockState(actorIndex, spawned?.PuppetName);
            if (!string.IsNullOrEmpty(spawned?.DisplayName) && spawned.DisplayName != spawned.PuppetName)
            {
                glamourerIpc.UnlockState(actorIndex, spawned.DisplayName);
            }
        }

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.UnassignCollectionForActor(actorIndex);
        }

        // 1. MCDF の場合: AQR / Mare 準拠（内包 Mod ファイルのキャッシュ展開 + Penumbra Temporary Collection + Glamourer）
        if (template.SourceType == CharacterSourceType.Mcdf)
        {
            if (!string.IsNullOrWhiteSpace(template.McdfFilePath) && mcdfParser != null)
            {
                try
                {
                    string configDir = pluginInterface?.ConfigDirectory.FullName ?? Path.GetTempPath();
                    string cacheDir = Path.Combine(configDir, "mcdf_cache");
                    Directory.CreateDirectory(cacheDir);

                    var bundle = mcdfParser.ExtractMcdfBundle(template.McdfFilePath, cacheDir);
                    if (bundle != null)
                    {
                        // 既存の一時コレクションがあれば削除して再作成
                        if (spawned?.TemporaryCollectionGuid.HasValue == true)
                        {
                            penumbraIpc.DeleteTemporaryCollection(spawned.TemporaryCollectionGuid.Value);
                            spawned.TemporaryCollectionGuid = null;
                        }

                        if (penumbraIpc.IsAvailable)
                        {
                            var tempGuid = penumbraIpc.CreateTemporaryCollection(template.Name);
                            if (tempGuid != Guid.Empty)
                            {
                                if (bundle.ModPaths.Count > 0 || !string.IsNullOrEmpty(bundle.ManipulationData))
                                {
                                    penumbraIpc.AddTemporaryMod(tempGuid, bundle.ModPaths, bundle.ManipulationData ?? string.Empty);
                                }
                                bool assignOk = penumbraIpc.AssignTemporaryCollection(tempGuid, actorIndex);
                                if (spawned != null)
                                {
                                    spawned.TemporaryCollectionGuid = tempGuid;
                                }
                                logManager?.Info($"MCDF: Assigned Penumbra temporary collection {tempGuid} to actor #{actorIndex} (Success: {assignOk}) with {bundle.ModPaths.Count} mod files.");
                            }
                        }

                        // 事前適用フェーズ（まだ描画準備未完了）の場合はコレクション割り当てのみで一旦戻る
                        if (!applyGlamourer)
                        {
                            return;
                        }

                        // Glamourer デザインの適用 (ICharacter ポインタ直接適用で自キャラ誤爆を 100% 根絶)
                        string? designString = bundle.GlamourerDesign;
                        if (string.IsNullOrWhiteSpace(designString)) designString = template.GlamourerDesignString;

                        if (glamourerIpc.IsAvailable && !string.IsNullOrWhiteSpace(designString))
                        {
                            bool glamSuccess = false;
                            if (charaObj != null && charaObj.Address != nint.Zero)
                            {
                                glamSuccess = glamourerIpc.ApplyDesignToCharacter(charaObj, designString);
                            }
                            if (!glamSuccess)
                            {
                                glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
                            }
                            logManager?.Info($"MCDF Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
                        }

                        // 武器の表示・非表示
                        chara->DrawData.HideWeapons(!template.WeaponVisible);
                        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

                        // Penumbra Redraw (AQuestReborn 方式: 最後に必ず Redraw)
                        if (penumbraIpc.IsAvailable)
                        {
                            penumbraIpc.Redraw(actorIndex);
                            logManager?.Info($"MCDF Penumbra Redraw for Global#{actorIndex}.");
                        }

                        // Customize+ Profile の適用 (テンプレート指定 または MCDF内包データ)
                        ApplyCustomizePlusProfile(chara, actorIndex, template, spawned, bundle.CustomizePlusData);

                        if (spawned != null) spawned.IsReady = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Error($"Error applying MCDF bundle: {ex.Message}");
                }
            }
        }

        // 2. Penumbra コレクションの適用 (通常指定のコレクション)
        if (penumbraIpc.IsAvailable && !string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
        {
            bool penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
            logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on Global#{actorIndex}: {penSuccess}");
            if (penSuccess && applyGlamourer)
            {
                penumbraIpc.Redraw(actorIndex);
            }
        }

        // 3. モンスター / 非人型アクターの場合 (HDM GuiseService 方式)
        // ※ Penumbra Redraw は絶対に呼ばない（非人型 DrawObject が無効化されて消えるため）
        if (template.ModelCharaId > 0)
        {
            chara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
            chara->GameObject.Scale = template.Scale > 0 ? template.Scale : 1.0f;
            chara->DrawData.HideWeapons(true);
            chara->DrawData.IsWeaponHidden = true;
            chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);

            logManager?.Info($"Applied Monster ModelCharaId {template.ModelCharaId} to Global#{actorIndex} (Native draw mode).");
            return;
        }

        // 4. NPC (人型) の場合
        if (template.SourceType == CharacterSourceType.Npc)
        {
            if (glamourerIpc.IsAvailable)
            {
                glamourerIpc.ApplyNpcAppearance(actorIndex, template.CustomizeData, template.NpcEquipmentModelIds, showHeadgear: true);
            }
            else
            {
                ApplyNpcAppearanceDirectFallback(chara, template);
            }

            chara->DrawData.HideWeapons(!template.WeaponVisible);
            chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            if (penumbraIpc.IsAvailable)
            {
                penumbraIpc.Redraw(actorIndex);
            }
            logManager?.Info($"Applied Humanoid NPC appearance to Global#{actorIndex}.");
            return;
        }

        // 事前適用フェーズ（まだ描画準備未完了）の場合はここで終了。Glamourer は ReadyJob で適用する
        if (!applyGlamourer)
        {
            return;
        }

        // 5. Glamourer / PlayerClone の適用 (ICharacter ポインタ直接適用で自キャラ誤爆を 100% 根絶)
        if (glamourerIpc.IsAvailable)
        {
            string? designString = template.GlamourerDesignString;

            if (!string.IsNullOrWhiteSpace(designString))
            {
                bool glamSuccess = false;
                if (charaObj != null && charaObj.Address != nint.Zero)
                {
                    glamSuccess = glamourerIpc.ApplyDesignToCharacter(charaObj, designString);
                }
                if (!glamSuccess)
                {
                    glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
                }
                logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
            }
            else if (template.SourceType == CharacterSourceType.PlayerClone)
            {
                var playerDesign = glamourerIpc.GetCustomization(0);
                if (!string.IsNullOrWhiteSpace(playerDesign))
                {
                    bool glamSuccess = false;
                    if (charaObj != null && charaObj.Address != nint.Zero)
                    {
                        glamSuccess = glamourerIpc.ApplyDesignToCharacter(charaObj, playerDesign);
                    }
                    if (!glamSuccess)
                    {
                        glamSuccess = glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex, spawned?.PuppetName);
                    }
                    logManager?.Info($"Applied player customization clone via Glamourer to Global#{actorIndex} ('{spawned?.PuppetName}'): {glamSuccess}");
                }
                else
                {
                    glamourerIpc.ReapplyState(actorIndex);
                }
            }
        }
        else
        {
            if (template.SourceType == CharacterSourceType.Glamourer)
            {
                logManager?.Warning("Glamourer IPC not detected. Could not apply external appearance design.");
            }
        }

        // 武器の表示・非表示
        chara->DrawData.HideWeapons(!template.WeaponVisible);
        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

        // 6. Penumbra Redraw (Glamourer & Penumbra 適用後の確定再描画)
        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
            logManager?.Info($"Triggered Penumbra Redraw for Global#{actorIndex}.");
        }

        // 7. Customize+ Profile の適用 (AQR / Caraxi 準拠: PuppetName と WorldId によるプロファイル紐付け)
        ApplyCustomizePlusProfile(chara, actorIndex, template, spawned);
        if (spawned != null) spawned.IsReady = true;
    }

    /// <summary>
    /// Customize+ Profile (テンプレート指定 または MCDF内包) をパペット名とワールドIDで紐付け (Caraxi / AQR 準拠)
    /// インデックス指定を行わないため、自キャラ(LocalPlayer Index 0)への誤爆は物理的に完全不可能
    /// </summary>
    private void ApplyCustomizePlusProfile(Character* chara, int actorIndex, CharacterTemplate template, SpawnedActorData? spawned, string? fallbackMcdfCPlusData = null)
    {
        if (customizePlusIpc == null || !customizePlusIpc.IsAvailable) return;

        try
        {
            ushort worldId = chara != null ? (ushort)chara->HomeWorld : (ushort)0;
            string puppetName = spawned?.PuppetName ?? string.Empty;

            // 既存の紐付け解除
            if (spawned?.AssignedCustomizePlusGuid.HasValue == true && !string.IsNullOrEmpty(puppetName))
            {
                customizePlusIpc.RemovePlayerCharacter(spawned.AssignedCustomizePlusGuid.Value, puppetName, worldId);
                spawned.AssignedCustomizePlusGuid = null;
            }

            // 1. テンプレートで明示指定された CustomizePlus プロファイル
            if (!string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid) &&
                Guid.TryParse(template.CustomizePlusProfileGuid, out var profileGuid))
            {
                if (!string.IsNullOrEmpty(puppetName))
                {
                    bool ok = customizePlusIpc.AddPlayerCharacter(profileGuid, puppetName, worldId);
                    if (ok && spawned != null)
                    {
                        spawned.AssignedCustomizePlusGuid = profileGuid;
                        logManager?.Info($"CustomizePlus: Mapped profile '{template.CustomizePlusProfileName ?? profileGuid.ToString()}' ({profileGuid}) to puppet '{puppetName}' (World: {worldId}).");
                    }
                    else
                    {
                        logManager?.Warning($"CustomizePlus: Failed to map profile '{profileGuid}' to puppet '{puppetName}'.");
                    }
                }
            }
            // 2. MCDF に内包された CustomizePlus データ
            else if (!string.IsNullOrWhiteSpace(fallbackMcdfCPlusData))
            {
                string cPlusJson = fallbackMcdfCPlusData.Trim();
                if (!cPlusJson.StartsWith("{") && !cPlusJson.StartsWith("["))
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(cPlusJson);
                        cPlusJson = Encoding.UTF8.GetString(bytes);
                    }
                    catch (Exception ex)
                    {
                        logManager?.Warning($"Failed to base64-decode MCDF CustomizePlus data: {ex.Message}");
                    }
                }

                // MCDF のプロファイル JSON から Guid を取得して紐付け試行
                try
                {
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(cPlusJson);
                    if (parsed["UniqueId"] != null && Guid.TryParse(parsed["UniqueId"]!.ToString(), out var mcdfProfileGuid))
                    {
                        if (!string.IsNullOrEmpty(puppetName))
                        {
                            bool ok = customizePlusIpc.AddPlayerCharacter(mcdfProfileGuid, puppetName, worldId);
                            if (ok && spawned != null)
                            {
                                spawned.AssignedCustomizePlusGuid = mcdfProfileGuid;
                                logManager?.Info($"CustomizePlus: Mapped MCDF profile {mcdfProfileGuid} to puppet '{puppetName}'.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Warning($"Could not map MCDF CustomizePlus profile: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Error applying CustomizePlus profile: {ex.Message}");
        }
    }

    /// <summary>
    /// 互換用ラッパー
    /// </summary>
    private void ApplyExternalAppearance(SpawnedActorData spawned, CharacterTemplate template)
    {
        if (spawned.NativeAddress == 0) return;
        ApplyAppearanceDirect((Character*)spawned.NativeAddress, spawned.GlobalIndex, template, spawned);
    }

    /// <summary>
    /// プレビュー中アクターの武器表示状態を切り替えて即座に再描画する
    /// </summary>
    public void SetWeaponVisibility(SpawnedActorData actor, bool visible)
    {
        if (actor.NativeAddress == 0) return;
        var nativeChara = (Character*)actor.NativeAddress;
        int actorIndex = (int)actor.GlobalIndex;

        nativeChara->DrawData.HideWeapons(!visible);
        nativeChara->DrawData.IsWeaponHidden = !visible;
        nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
        }
        logManager?.Info($"Updated weapon visibility for '{actor.DisplayName}' (Visible: {visible}, GlobalIndex: {actorIndex}).");
    }

    private Vector3 GetDefaultSpawnPosition()
    {
        var player = objectTable.Length > 0 ? objectTable[0] : null;
        if (player == null) return Vector3.Zero;

        // 自キャラの正面1.5mの位置を初期位置にする
        var rot = player.Rotation;
        var forward = new Vector3((float)Math.Sin(rot), 0, (float)Math.Cos(rot));
        return player.Position + (forward * 1.5f);
    }

    /// <summary>
    /// 自キャラ (LocalPlayer Index 0) の Glamourer ステートおよび Penumbra コレクションをリバートして本来の姿に復元する
    /// </summary>
    public void RevertLocalPlayer()
    {
        void ExecuteRevert()
        {
            try
            {
                var player = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
                string? name = player?.Name.TextValue;
                glamourerIpc?.RevertLocalPlayer(name, player);
                if (penumbraIpc != null && penumbraIpc.IsAvailable)
                {
                    penumbraIpc.UnassignCollectionForActor(0);
                }
                logManager?.Info($"Reverted LocalPlayer state via Glamourer & Penumbra on main thread (Name: '{name}').");
            }
            catch (Exception ex)
            {
                logManager?.Warning($"RevertLocalPlayer failed: {ex.Message}");
            }
        }

        if (framework != null)
        {
            framework.RunOnFrameworkThread(ExecuteRevert);
        }
        else
        {
            ExecuteRevert();
        }
    }

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        DespawnAll();
    }
}
