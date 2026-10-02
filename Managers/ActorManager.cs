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

    // Glamourer Identity 用のユニークな "Forename Surname" 名前シリアル (HDM The 0.8.44 Bug対策)
    private int nameSerial = 0;

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
        GameDataService? gameDataService = null)
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

    private string NextPuppetName()
    {
        var n = nameSerial++;
        var hi = (char)('A' + (n / 26) % 26);
        var lo = (char)('a' + n % 26);
        return $"Csp {hi}{lo}";
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
            nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

            // 3. ベースラインのリセット
            nativeChara->ModelContainer.ModelCharaId = 0;
            nativeChara->GameObject.Scale = 1.0f;
            nativeChara->DrawData.HideWeapons(!template.WeaponVisible);
            nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            // クラス分類:
            // 重要: Penumbra の IPC (SetCollectionForObject / AssignTemporaryCollection) およびリソース解決フックは、
            // 内部で allowPlayerNpc: false で FromObject を呼ぶ。
            // ObjectKind が BattleNpc だと CreateBNpcFromObject に進み、NameId 0 のアクターは必ず InvalidActor (ec=16 / ec=255) となり、
            // Penumbra のコレクションがアクターに割り当てられずバニラになってしまう。
            // 人型パペット (Glamourer / MCDF / PlayerClone / Humanoid) は ObjectKind.Pc に設定することで、
            // Penumbra の CreatePlayerFromObject が走り、100% 確実にコレクション・MOD が解決される。
            nativeChara->GameObject.ObjectKind = template.ModelCharaId > 0 ? ObjectKind.BattleNpc : ObjectKind.Pc;
            nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;
            nativeChara->GameObject.TargetableStatus = 0;
            nativeChara->GameObject.EventId = 0;

            // Glamourer & Penumbra Identity のスタンプ (HDM The 0.8.44 Bug対策)
            // OwnerId は触らない (デフォルト 0)。OwnerId != 0 だと Penumbra が存在しない親を探して ec=255 となり Glamourer も GetState null になる
            // NameId == 0 かつ HomeWorld == プレイヤーのワールド かつ 有効な名前 の組み合わせで、
            // Penumbra/Glamourer は独立した有効な Player Identifier として解決する
            nativeChara->NameId = 0;
            nativeChara->HomeWorld = meNative->HomeWorld;
            string puppetName = NextPuppetName();
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

            // CustomizePlus 一時プロファイルのクリーンアップ
            if (actor.TemporaryCustomizePlusGuid.HasValue && customizePlusIpc != null)
            {
                customizePlusIpc.DeleteTemporaryProfile(actor.TemporaryCustomizePlusGuid.Value);
                actor.TemporaryCustomizePlusGuid = null;
            }

            if (actor.NativeAddress != 0)
            {
                var com = ClientObjectManager.Instance();
                if (com != null)
                {
                    // ライブオブジェクトから最新の COM インデックスを解決
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
    public void ApplyAppearanceDirect(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        logManager?.Info($"ApplyAppearanceDirect: '{template.Name}' (GlobalIndex: {actorIndex}, Source: {template.SourceType}, ModelChara: {template.ModelCharaId})...");

        // 前のキャラの Penumbra コレクション割り当て（通常・一時）を完全にクリア
        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.UnassignCollectionForActor(actorIndex);
        }

        // 人型モデルの場合は ObjectKind.Pc を担保（Penumbra Identifier 解決の生命線）
        if (template.ModelCharaId == 0)
        {
            chara->GameObject.ObjectKind = ObjectKind.Pc;
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

                        // Glamourer デザインの適用
                        string? designString = bundle.GlamourerDesign;
                        if (string.IsNullOrWhiteSpace(designString)) designString = template.GlamourerDesignString;

                        if (glamourerIpc.IsAvailable && !string.IsNullOrWhiteSpace(designString))
                        {
                            var (glamSuccess, custBytes) = glamourerIpc.ApplyDesignToActorEx(designString, actorIndex);
                            logManager?.Info($"MCDF Glamourer ApplyDesign result on Global#{actorIndex}: {glamSuccess}");
                            if (custBytes != null && custBytes.Length >= 26)
                            {
                                fixed (byte* pCust = custBytes)
                                {
                                    Buffer.MemoryCopy(pCust, &chara->DrawData.CustomizeData, 26, 26);
                                }
                                chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);
                                logManager?.Info($"MCDF: Synchronized 26 CustomizeData bytes directly to native actor #{actorIndex}.");
                            }
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
                        ApplyCustomizePlusProfile(actorIndex, template, spawned, bundle.CustomizePlusData);

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

        // 5. Glamourer / PlayerClone の適用 (AQR 方式)
        if (glamourerIpc.IsAvailable)
        {
            string? designString = template.GlamourerDesignString;

            if (!string.IsNullOrWhiteSpace(designString))
            {
                var (glamSuccess, custBytes) = glamourerIpc.ApplyDesignToActorEx(designString, actorIndex);
                logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex}: {glamSuccess}");
                if (custBytes != null && custBytes.Length >= 26)
                {
                    fixed (byte* pCust = custBytes)
                    {
                        Buffer.MemoryCopy(pCust, &chara->DrawData.CustomizeData, 26, 26);
                    }
                    chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);
                    logManager?.Info($"Glamourer: Synchronized 26 CustomizeData bytes directly to native actor #{actorIndex}.");
                }
            }
            else if (template.SourceType == CharacterSourceType.PlayerClone)
            {
                var playerDesign = glamourerIpc.GetCustomization(0);
                if (!string.IsNullOrWhiteSpace(playerDesign))
                {
                    glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex);
                    logManager?.Info($"Applied player customization clone via Glamourer to Global#{actorIndex}.");
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

        // 6. Penumbra Redraw (AQuestReborn 方式: 最後に必ず Redraw)
        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
            logManager?.Info($"Triggered Penumbra Redraw for Global#{actorIndex}.");
        }

        // 7. Customize+ Profile の適用
        ApplyCustomizePlusProfile(actorIndex, template, spawned);
        if (spawned != null) spawned.IsReady = true;
    }

    /// <summary>
    /// Customize+ Profile (テンプレート指定 または MCDF内包) をアクターに一時適用
    /// </summary>
    private void ApplyCustomizePlusProfile(int actorIndex, CharacterTemplate template, SpawnedActorData? spawned, string? fallbackMcdfCPlusData = null)
    {
        if (customizePlusIpc == null || !customizePlusIpc.IsAvailable) return;

        try
        {
            // 既存の一時プロファイルがあれば削除
            if (spawned?.TemporaryCustomizePlusGuid.HasValue == true)
            {
                customizePlusIpc.DeleteTemporaryProfile(spawned.TemporaryCustomizePlusGuid.Value);
                spawned.TemporaryCustomizePlusGuid = null;
            }

            Guid? assignedGuid = null;

            // 1. テンプレートで明示指定された CustomizePlus プロファイル
            if (!string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid) &&
                Guid.TryParse(template.CustomizePlusProfileGuid, out var profileGuid))
            {
                assignedGuid = customizePlusIpc.SetTemporaryProfileByGuid((ushort)actorIndex, profileGuid);
                if (assignedGuid.HasValue)
                {
                    logManager?.Info($"CustomizePlus: Applied profile '{template.CustomizePlusProfileName ?? profileGuid.ToString()}' ({assignedGuid.Value}) to Global#{actorIndex}.");
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

                assignedGuid = customizePlusIpc.SetTemporaryProfile((ushort)actorIndex, cPlusJson);
                if (assignedGuid.HasValue)
                {
                    logManager?.Info($"CustomizePlus: Applied embedded MCDF profile ({assignedGuid.Value}) to Global#{actorIndex}.");
                }
            }

            if (assignedGuid.HasValue && spawned != null)
            {
                spawned.TemporaryCustomizePlusGuid = assignedGuid.Value;
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

    public void Dispose()
    {
        clientState.TerritoryChanged -= OnTerritoryChanged;
        DespawnAll();
    }
}
