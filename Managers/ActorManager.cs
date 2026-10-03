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

    // HDM GuiseService.cs 準拠: モンスター/再描画待機キュー (Phase: WaitEnable)
    private sealed class MonsterRedrawJob
    {
        public SpawnedActorData Spawned = null!;
        public ushort GlobalIndex;
        public int Ticks;
    }
    private readonly List<MonsterRedrawJob> monsterRedrawJobs = new();

    // HDM HumanGuise.cs 準拠: 人型NPC専用の非同期同期待機キュー (Cold-Spawn Race 解消)
    private sealed class HumanoidNpcApplyJob
    {
        public SpawnedActorData Spawned = null!;
        public CharacterTemplate Template = null!;
        public int Ticks;
        public bool DrawEnabled;
        public bool AppliedGlamourer;
        public int RebuildTicks;
        public const int MaxTicks = 60; // 最大約1秒間 (60フレーム) リトライ
    }
    private readonly List<HumanoidNpcApplyJob> humanoidNpcApplyJobs = new();

    // 統合アピアランス遅延安定化キュー (Glamourer モデル再構築 & Penumbra 非同期ロード完了待ち確定処理)
    // Chonk や MCDF などの Glamourer + Penumbra + CustomizePlus 複合アクターにおける体型打ち消し・MOD抜け・ActorNotFoundを完全根絶
    private sealed class AppearanceDeferredJob
    {
        public SpawnedActorData Spawned = null!;
        public ushort GlobalIndex;
        public CharacterTemplate Template = null!;
        public string? FallbackMcdfCPlusData;
        public string? PendingGlamourerDesign; // MCDF等の遅延Glamourerデザイン (DrawObject生成待ち)
        public bool HasPenumbra;
        public bool HasCustomizePlus;
        public int Ticks;
        public int RebuildTicks;
        public bool GlamourerApplied; // Phase 0 (Glamourer 遅延適用) 完了フラグ
        public bool DrawRebuilt; // Phase 0 後の DrawObject 再構築完了フラグ
        public bool RedrawDone; // Phase 1 (Penumbra Redraw) 完了フラグ
        public const int DelayTicks = 4; // Phase 1 待機: Glamourer再構築 & Penumbra初期解決 (~60ms)
        public const int PostRedrawTicks = 4; // Phase 2 待機: Penumbra Redraw後の DrawObject 確定待機 (~60ms)
    }
    private readonly List<AppearanceDeferredJob> appearanceDeferredJobs = new();

    private int puppetSerial = 0;
    private const int MaxReadyTicks = 200;


    // 単一プレビューアクター（Characterタブ専用）
    public SpawnedActorData? CurrentPreviewActor { get; private set; }

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    /// <summary>
    /// 自キャラ (LocalPlayer Index 0) がワールドに正常に存在し、クローン生成の元として利用可能か
    /// </summary>
    public bool IsLocalPlayerReady
    {
        get
        {
            if (objectTable.Length == 0) return false;
            var localPlayer = objectTable[0] as ICharacter;
            return localPlayer != null && localPlayer.Address != 0;
        }
    }

    /// <summary>
    /// 自キャラ (LocalPlayer Index 0) の現在ワールド座標を取得
    /// </summary>
    public Vector3 LocalPlayerPosition
    {
        get
        {
            if (objectTable.Length > 0 && objectTable[0] is ICharacter localPlayer && localPlayer.Address != 0)
            {
                return localPlayer.Position;
            }
            return Vector3.Zero;
        }
    }

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
        monsterRedrawJobs.Clear();
        humanoidNpcApplyJobs.Clear();
        appearanceDeferredJobs.Clear();
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
    }

    private static readonly string[] SlotSurnames = new[]
    {
        "Alpha",    // COM#0
        "Bravo",    // COM#1
        "Charlie",  // COM#2
        "Delta",    // COM#3
        "Echo",     // COM#4
        "Foxtrot",  // COM#5
        "Golf",     // COM#6
        "Hotel",    // COM#7
        "India",    // COM#8
        "Juliet",   // COM#9
        "Kilo",     // COM#10
        "Lima",     // COM#11
        "Mike",     // COM#12
        "November", // COM#13
        "Oscar",    // COM#14
        "Papa",     // COM#15
        "Quebec",   // COM#16
        "Romeo",    // COM#17
        "Sierra",   // COM#18
        "Tango",    // COM#19
        "Uniform",  // COM#20
        "Victor",   // COM#21
        "Whiskey",  // COM#22
        "Xray",     // COM#23
        "Yankee",   // COM#24
        "Zulu"      // COM#25
    };

    private string GetPuppetName(ushort comIdx)
    {
        // HDM & AQR 黄金律:
        // FF14 の PlayerIdentifier / VerifyPlayerName 規則:
        // Forename + " " + Surname, 各3〜15文字以内, 合計20文字以内, 純粋な ASCII 英字のみ
        // 【重要】数字（0〜9）は FF14 の PlayerName で厳格に禁止されており、数字が入ると名前破損（Character: A.C）を引き起こし全IPCが停止する！
        // したがって、COM スロット番号 (0〜25等) と 1対1 に対応する英字フォネティックコード "Puppet Alpha", "Puppet Bravo", ... を生成する。
        // これにより、同じスロットを再利用した際にも同一名で Glamourer / Penumbra のステートキャッシュを 100% 確実にリバート・パージ可能。
        // ※頭上のネームプレート表示やUI表示は SpawnedActorData.DisplayName / NamePlate.CustomName (template.Name) が保持されるため完全に日本語で表示される。
        if (comIdx < SlotSurnames.Length)
        {
            return $"Puppet {SlotSurnames[comIdx]}";
        }
        int s = comIdx - SlotSurnames.Length;
        char c1 = (char)('A' + ((s / 26) % 26));
        char c2 = (char)('a' + (s % 26));
        return $"Puppet Extra{c1}{c2}";
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
    public SpawnedActorData? SpawnCharacter(CharacterTemplate template, Vector3? spawnPosition = null, float? spawnRotation = null, float? spawnScale = null)
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

            // NPCデータの補完・自動リフレッシュ (HDM ENpcBase 逆引き解決)
            if (template.SourceType == CharacterSourceType.Npc && gameDataService != null)
            {
                var app = gameDataService.ResolveNpcAppearance(template.DataId, template.Name);
                if (app != null)
                {
                    // テンプレートが未設定、または自キャラデータ等で汚染されている場合でも最新の正しいデータに自動更新
                    if (app.ModelCharaId > 0 || (app.CustomizeData != null && app.CustomizeData.Length > 0 && app.CustomizeData[0] > 0))
                    {
                        template.ModelCharaId = app.ModelCharaId;
                        template.CustomizeData = app.CustomizeData;
                        template.NpcEquipmentModelIds = app.EquipmentModelIds;
                        template.McType = app.McType;
                        logManager?.Info($"Auto-resolved NPC appearance data for '{template.Name}' from ENpc (ModelChara: {template.ModelCharaId}, McType: {template.McType}, Race: {(app.CustomizeData != null && app.CustomizeData.Length > 0 ? app.CustomizeData[0].ToString() : "N/A")}).");
                    }
                }
            }

            float targetScale = (spawnScale.HasValue && spawnScale.Value > 0.001f)
                ? spawnScale.Value
                : (template.Scale > 0.001f ? template.Scale : 1.0f);

            logManager?.Info($"Spawning '{template.Name}' (Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, Scale: {targetScale}, Weapon: {template.WeaponVisible}) at COM#{comIdx}...");

            // 1. 自キャラからベースラインをコピーして drawable 骨格を確立 (AQR / Brio 準拠)
            nativeChara->CharacterSetup.CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding);
            nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

            // 2. ベースラインのリセット
            nativeChara->ModelContainer.ModelCharaId = 0;
            nativeChara->GameObject.Scale = targetScale;
            if (template.ModelCharaId == 0)
            {
                nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;
            }

            // AQR 黄金律:
            // ObjectKind, BattleNpcSubKind, OwnerId, NameId, HomeWorld の改変は一切行わない（素の BattleCharacter を維持）！
            nativeChara->GameObject.TargetableStatus = 0;
            nativeChara->GameObject.EventId = 0;

            string puppetName = GetPuppetName(comIdx);
            nativeChara->GameObject.SetName(puppetName);

            // 位置・回転・透明度の設定
            nativeChara->GameObject.SetPosition(pos.X, pos.Y, pos.Z);
            nativeChara->GameObject.SetRotation(rot);
            nativeChara->GameObject.DefaultPosition = pos;
            nativeChara->GameObject.DefaultRotation = rot;
            nativeChara->Alpha = 1.0f;

            // ※ ここでの即時 EnableDraw() は廃止！
            // 各パイプライン（A/B: 直列適用後、C: NPC準備後、D: モンスター準備後）で安全に一度だけ EnableDraw() を行う。
            // これにより、未完成な DrawObject / Weapon がレンダラーに晒される競合クラッシュ（0xC0000005）を完全に防止する。

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

            // スロット再利用時の外見汚染（Glamourerステートキャッシュ/Penumbra/CustomizePlus残存）を完全パージ
            ClearActorSlotState(globalIdx, puppetName);

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
                    Scale = targetScale
                },
                NamePlate = new NamePlateSettings
                {
                    Show = true,
                    CustomName = template.Name
                },
                IsTargetable = false
            };

            // =========================================================================
            // パイプライン D: Monster / MOB (ModelCharaId > 0) [HDM GuiseService 準拠]
            // =========================================================================
            if (template.ModelCharaId > 0)
            {
                nativeChara->GameObject.DisableDraw();
                nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
                nativeChara->GameObject.Scale = targetScale;

                if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
                {
                    var equipSpan = nativeChara->DrawData.EquipmentModelIds;
                    for (int idx = 0; idx < template.NpcEquipmentModelIds.Length && idx < equipSpan.Length; idx++)
                    {
                        equipSpan[idx] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[idx] };
                    }
                    nativeChara->DrawData.IsHatHidden = false;
                }

                nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

                monsterRedrawJobs.Add(new MonsterRedrawJob
                {
                    Spawned = spawned,
                    GlobalIndex = globalIdx,
                    Ticks = 0
                });

                activeActors.Add(spawned);
                createdIndexes.Add(globalIdx);
                logManager?.Info($"[Pipeline D: Monster] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Enqueued to MonsterRedrawJob.");
                return spawned;
            }

            // =========================================================================
            // パイプライン C: NPC (人型 ENpc: SourceType == Npc かつ ModelCharaId == 0) [HDM HumanGuise 準拠]
            // =========================================================================
            if (template.SourceType == CharacterSourceType.Npc)
            {
                nativeChara->GameObject.DisableDraw();
                SafeSetWeaponVisibility(nativeChara, template.WeaponVisible);

                humanoidNpcApplyJobs.Add(new HumanoidNpcApplyJob
                {
                    Spawned = spawned,
                    Template = template,
                    Ticks = 0,
                    DrawEnabled = false
                });

                activeActors.Add(spawned);
                createdIndexes.Add(globalIdx);
                logManager?.Info($"[Pipeline C: NPC] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Enqueued to HumanoidNpcApplyJob (HDM ReadyJob synchronization).");
                return spawned;
            }

            // =========================================================================
            // パイプライン A & B: AQR系統 (PlayerClone / Glamourer / MCDF) [AQR 準拠]
            // =========================================================================
            // スポーン完了直後に同一フレーム・同一コンテキストで即時直列実行！
            ApplyAppearanceDirect(nativeChara, globalIdx, template, spawned);
            nativeChara->GameObject.EnableDraw();
            spawned.IsReady = true;

            activeActors.Add(spawned);
            createdIndexes.Add(globalIdx);
            logManager?.Info($"[Pipeline A/B: AQR] Spawned '{spawned.DisplayName}' on Global#{globalIdx}. Penumbra & Glamourer finalized immediately.");
            return spawned;
        }
        catch (Exception ex)
        {
            logManager?.Error($"Exception during SpawnCharacter: {ex}");
            return null;
        }
    }

    /// <summary>
    /// スポーン中アクターの位置・回転・スケールを更新
    /// </summary>
    public void UpdateActorTransform(SpawnedActorData actor, Vector3 newPosition, float newRotation, float? newScale = null)
    {
        actor.Transform.Position = newPosition;
        actor.Transform.Rotation = newRotation;
        if (newScale.HasValue && newScale.Value > 0.001f)
        {
            actor.Transform.Scale = newScale.Value;
        }

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

                    float targetScale = actor.Transform.Scale;
                    if (targetScale > 0.001f)
                    {
                        chara->GameObject.Scale = targetScale;
                        if (chara->GameObject.DrawObject != null)
                        {
                            try
                            {
                                chara->GameObject.DrawObject->NotifyTransformChanged();
                            }
                            catch { }
                        }
                    }
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
    /// アクターが新しくスポーンされる際、過去にそのスロット(globalIdx)を使用していたアクターの外見ステート
    /// （Glamourerステートキャッシュ、Penumbraコレクション割り当て、CustomizePlusプロファイル等）を安全に初期化・パージする。
    /// これにより、以前同じスロットにいたChonk等の見た目がモンスターやNPCに誤爆・感染することを100%防止する。
    /// </summary>
    private void ClearActorSlotState(ushort globalIdx, string? puppetName)
    {
        // 自キャラ (LocalPlayer Index 0) は絶対に触らない
        if (globalIdx == 0) return;

        try
        {
            // 1. Glamourer ロック解除 & リバート（ステートキャッシュ破棄）
            if (glamourerIpc != null && glamourerIpc.IsAvailable)
            {
                glamourerIpc.UnlockState(globalIdx, puppetName);
                glamourerIpc.RevertState(globalIdx, puppetName);
            }

            // 2. Penumbra コレクション割り当て解除
            if (penumbraIpc != null && penumbraIpc.IsAvailable)
            {
                penumbraIpc.UnassignCollectionForActor(globalIdx);
            }

            // 3. CustomizePlus 一時プロファイル削除
            if (customizePlusIpc != null && customizePlusIpc.IsAvailable)
            {
                customizePlusIpc.DeleteTemporaryProfileOnCharacter(globalIdx);
            }
        }
        catch (Exception ex)
        {
            logManager?.Warning($"ClearActorSlotState for Global#{globalIdx} failed (ignored): {ex.Message}");
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
            monsterRedrawJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);
            humanoidNpcApplyJobs.RemoveAll(j => j.Spawned == actor || j.Spawned.GlobalIndex == actor.GlobalIndex);
            appearanceDeferredJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);

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
                customizePlusIpc.DeleteTemporaryProfileOnCharacter((ushort)actor.GlobalIndex);
            }

            if (actor.NativeAddress != 0)
            {
                var chara = (Character*)actor.NativeAddress;
                try
                {
                    // 武器のグラフィックフラグを非表示にし、描画パイプラインから完全アンロード (孤立武器残留防止)
                    SafeSetWeaponVisibility(chara, false);
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
            humanoidNpcApplyJobs.RemoveAll(j => j.Spawned == actor || j.Spawned.GlobalIndex == actor.GlobalIndex);
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
        monsterRedrawJobs.Clear();
        humanoidNpcApplyJobs.Clear();
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
    /// 毎フレームの更新処理: 視線追従 & モンスターRedraw (HDM GuiseService 準拠)
    /// </summary>
    public void UpdateFrame()
    {
        try
        {
            headTrackingManager.UpdateTracking(activeActors);

            // モンスター / 再描画待機キュー (HDM GuiseService RedrawPhase.WaitEnable 方式)
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

            // HDM SpawnService & HumanGuise 準拠: 人型NPCの外見非同期同期待機ジョブ
            if (humanoidNpcApplyJobs.Count > 0)
            {
                for (int i = humanoidNpcApplyJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = humanoidNpcApplyJobs[i];
                        job.Ticks++;

                        if (!activeActors.Contains(job.Spawned))
                        {
                            humanoidNpcApplyJobs.RemoveAt(i);
                            continue;
                        }

                        // 最低 2 フレーム待機 (COM生成とエンジンの登録完了待ち)
                        if (job.Ticks < 2) continue;

                        if (job.Spawned.GlobalIndex >= objectTable.Length)
                        {
                            if (job.Ticks >= HumanoidNpcApplyJob.MaxTicks) humanoidNpcApplyJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.Spawned.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            if (job.Ticks >= HumanoidNpcApplyJob.MaxTicks) humanoidNpcApplyJobs.RemoveAt(i);
                            continue;
                        }

                        job.Spawned.NativeAddress = charaObj.Address;
                        var chara = (Character*)charaObj.Address;
                        int actorIndex = (int)job.Spawned.GlobalIndex;

                        // 1. HDM 準拠: ゲームエンジンが描画準備完了 (IsReadyToDraw) になるのを待って EnableDraw (初期骨格確立)
                        if (!job.DrawEnabled)
                        {
                            bool ready = false;
                            try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                            if (!ready && job.Ticks < 15) continue;

                            try { chara->GameObject.EnableDraw(); } catch { }
                            job.DrawEnabled = true;
                        }

                        // 2. HDM 準拠: DrawObject が存在することを確認
                        var drawObj = (nint)chara->GameObject.DrawObject;
                        if (drawObj == nint.Zero && job.Ticks < 30) continue;

                        // 3. Glamourer 経由で外見を適用 (HDM HumanGuise.ApplyState flags=6UL)
                        if (!job.AppliedGlamourer)
                        {
                            bool glamSuccess = false;
                            if (glamourerIpc != null && glamourerIpc.IsAvailable)
                            {
                                var res = glamourerIpc.TryApplyNpcAppearance(
                                    actorIndex,
                                    job.Template.CustomizeData,
                                    job.Template.NpcEquipmentModelIds,
                                    showHeadgear: true,
                                    job.Spawned.PuppetName);

                                if (res == GlamourerIpc.NpcApplyResult.Applied)
                                {
                                    glamSuccess = true;
                                }
                            }

                            if (glamSuccess)
                            {
                                job.AppliedGlamourer = true;
                                // HDM (HumanGuise.RedrawGuise / GuiseService.BeginRedraw) 黄金律:
                                // ApplyState 直後に DisableDraw を行い、DrawObject を強制再構築する！
                                try { chara->GameObject.DisableDraw(); } catch { }
                                logManager?.Info($"[Pipeline C: NPC] Glamourer applied to Global#{actorIndex} ('{job.Spawned.DisplayName}'). Initiating HDM RedrawGuise (DisableDraw -> Rebuild)...");
                                continue;
                            }
                            else if (job.Ticks >= HumanoidNpcApplyJob.MaxTicks)
                            {
                                logManager?.Warning($"[Pipeline C: NPC] Glamourer NPC appearance timed out after {job.Ticks} ticks on Global#{actorIndex} ('{job.Spawned.DisplayName}'). Applying direct memory fallback...");
                                ApplyNpcAppearanceDirectFallback(chara, job.Template);

                                SafeSetWeaponVisibility(chara, job.Template.WeaponVisible);

                                try { chara->GameObject.EnableDraw(); } catch { }
                                job.Spawned.IsReady = true;
                                humanoidNpcApplyJobs.RemoveAt(i);
                                logManager?.Info($"[Pipeline C: NPC] Applied Humanoid NPC appearance fallback on Global#{actorIndex} after {job.Ticks} ticks.");
                                continue;
                            }
                        }
                        else
                        {
                            // 4. HDM (GuiseService.OnUpdate / RedrawPhase) 準拠:
                            // DisableDraw 後、最低 2 ticks 待機し、ゲームエンジンの準備完了を待って EnableDraw
                            job.RebuildTicks++;
                            if (job.RebuildTicks < 2) continue;

                            bool ready = false;
                            try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                            if (!ready && job.RebuildTicks < 15) continue;

                            SafeSetWeaponVisibility(chara, job.Template.WeaponVisible);

                            try { chara->GameObject.EnableDraw(); } catch { }
                            job.Spawned.IsReady = true;
                            humanoidNpcApplyJobs.RemoveAt(i);
                            logManager?.Info($"[Pipeline C: NPC] Humanoid NPC DrawObject rebuild complete on Global#{actorIndex} ('{job.Spawned.DisplayName}') after {job.Ticks} ticks (Rebuild: {job.RebuildTicks} ticks).");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"HumanoidNpcApplyJob exception: {ex}");
                        if (i < humanoidNpcApplyJobs.Count) humanoidNpcApplyJobs.RemoveAt(i);
                    }
                }
            }

            // 統合アピアランス遅延安定化ジョブ (Glamourer モデル再構築完了後の CustomizePlus 体型復元 & Penumbra 非同期ロード待機 Redraw)
            if (appearanceDeferredJobs.Count > 0)
            {
                for (int i = appearanceDeferredJobs.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var job = appearanceDeferredJobs[i];
                        job.Ticks++;

                        if (!activeActors.Contains(job.Spawned))
                        {
                            appearanceDeferredJobs.RemoveAt(i);
                            continue;
                        }

                        int actorIndex = (int)job.GlobalIndex;

                        // 自キャラ誤爆ガード
                        if (actorIndex <= 0 || (objectTable.Length > 0 && objectTable[0]?.Address == job.Spawned.NativeAddress))
                        {
                            appearanceDeferredJobs.RemoveAt(i);
                            continue;
                        }

                        if (job.GlobalIndex >= objectTable.Length)
                        {
                            appearanceDeferredJobs.RemoveAt(i);
                            continue;
                        }

                        var obj = objectTable[job.GlobalIndex];
                        if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero)
                        {
                            appearanceDeferredJobs.RemoveAt(i);
                            continue;
                        }

                        var chara = (Character*)charaObj.Address;

                        // Phase 0: MCDF 等の Base64 デザイン遅延適用（ゲームエンジンの DrawObject 生成を待って確実に適用）
                        if (!string.IsNullOrEmpty(job.PendingGlamourerDesign) && !job.GlamourerApplied)
                        {
                            if (job.Ticks < 2) continue;

                            bool ready = false;
                            try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                            var drawObj = (nint)chara->GameObject.DrawObject;
                            if ((!ready || drawObj == nint.Zero) && job.Ticks < 30)
                            {
                                try { chara->GameObject.EnableDraw(); } catch { }
                                continue;
                            }

                            bool glamSuccess = false;
                            if (glamourerIpc.IsAvailable)
                            {
                                glamSuccess = glamourerIpc.ApplyDesignToActor(job.PendingGlamourerDesign, actorIndex, job.Spawned.PuppetName);
                                logManager?.Info($"[AppearanceDeferredJob Phase 0] Deferred Glamourer ApplyDesign for '{job.Spawned.DisplayName}' on Global#{actorIndex} result: {glamSuccess} (after {job.Ticks} ticks).");
                            }

                            if (glamSuccess)
                            {
                                job.GlamourerApplied = true;
                                // HDM 黄金律: ApplyDesign 直後に DisableDraw を行い、DrawObject を強制再構築する！
                                try { chara->GameObject.DisableDraw(); } catch { }
                                job.Ticks = 0;
                                continue;
                            }
                            else if (job.Ticks < 30)
                            {
                                // まだゲームエンジンまたは Glamourer が認識していなければ次フレームでリトライ
                                continue;
                            }
                            else
                            {
                                logManager?.Warning($"[AppearanceDeferredJob Phase 0] Deferred Glamourer timed out on Global#{actorIndex}. Proceeding to Redraw...");
                                job.GlamourerApplied = true;
                                job.DrawRebuilt = true;
                                job.Ticks = 0;
                                continue;
                            }
                        }

                        // Phase 0 後の DrawObject 再構築待ち
                        if (!string.IsNullOrEmpty(job.PendingGlamourerDesign) && job.GlamourerApplied && !job.DrawRebuilt)
                        {
                            job.RebuildTicks++;
                            if (job.RebuildTicks < 2) continue;

                            bool ready = false;
                            try { ready = chara->GameObject.IsReadyToDraw(); } catch { }
                            if (!ready && job.RebuildTicks < 15) continue;

                            try { chara->GameObject.EnableDraw(); } catch { }
                            job.DrawRebuilt = true;
                            job.Ticks = 0;
                            continue;
                        }

                        // Phase 1: Penumbra の遅延 Redraw（Glamourer および非同期ファイル解決完了後の確定）
                        if (job.HasPenumbra && !job.RedrawDone)
                        {
                            if (job.Ticks < AppearanceDeferredJob.DelayTicks)
                                continue;

                            if (penumbraIpc.IsAvailable)
                            {
                                penumbraIpc.Redraw(actorIndex);
                                logManager?.Info($"[AppearanceDeferredJob Phase 1] Penumbra Redraw executed for '{job.Spawned.DisplayName}' on Global#{actorIndex} after {job.Ticks} ticks.");
                            }
                            job.RedrawDone = true;
                            job.Ticks = 0; // Phase 2 待機カウンターをリセット
                            continue;
                        }

                        // Phase 2: CustomizePlus の体型プロファイル確定注入（Penumbra RedrawによるDrawObject再構築完了待ち）
                        if (job.HasPenumbra && job.RedrawDone)
                        {
                            if (job.Ticks < AppearanceDeferredJob.PostRedrawTicks)
                                continue;
                        }
                        else if (!job.HasPenumbra)
                        {
                            if (job.Ticks < AppearanceDeferredJob.DelayTicks)
                                continue;
                        }

                        if (job.HasCustomizePlus)
                        {
                            ApplyCustomizePlusProfile(chara, actorIndex, job.Template, job.Spawned, job.FallbackMcdfCPlusData);
                            logManager?.Info($"[AppearanceDeferredJob Phase 2] CustomizePlus profile finalized for '{job.Spawned.DisplayName}' on Global#{actorIndex} after {job.Ticks} ticks (PostRedraw).");
                        }

                        job.Spawned.IsReady = true;
                        appearanceDeferredJobs.RemoveAt(i);
                    }
                    catch (Exception ex)
                    {
                        logManager?.Error($"AppearanceDeferredJob exception: {ex}");
                        if (i < appearanceDeferredJobs.Count) appearanceDeferredJobs.RemoveAt(i);
                    }
                }
            }

            // 全アクティブアクターの描画可視化保証 (Brio DrawWhenReady & AQR 準拠)
            foreach (var actor in activeActors)
            {
                if (actor.NativeAddress == 0) continue;
                if (actor.GlobalIndex >= objectTable.Length) continue;

                var obj = objectTable[actor.GlobalIndex];
                if (obj is not ICharacter charaObj || charaObj.Address == nint.Zero) continue;
                var chara = (Character*)charaObj.Address;

                // モンスターまたは人型NPCの適用待機中（描画準備中）は干渉しない
                if (monsterRedrawJobs.Any(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex) ||
                    humanoidNpcApplyJobs.Any(j => j.Spawned == actor || j.Spawned.GlobalIndex == actor.GlobalIndex)) continue;

                // 1. DrawObject が存在する場合、非表示フラグ(0x10)があれば解除
                if (chara->GameObject.DrawObject != null)
                {
                    if ((chara->GameObject.DrawObject->Flags & 0x10) != 0)
                    {
                        chara->GameObject.DrawObject->Flags &= unchecked((byte)~0x10);
                        chara->GameObject.EnableDraw();
                    }
                }
                else
                {
                    // DrawObject 未生成なら EnableDraw を試行
                    chara->GameObject.EnableDraw();
                }

                // 2. 描画準備完了状態なら確実に EnableDraw を実行
                if (chara->GameObject.IsReadyToDraw())
                {
                    chara->GameObject.EnableDraw();
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
    /// パイプライン C: NPC (人型 ENpc) 外見適用 (HDM HumanGuise 方式)
    /// </summary>
    public void ApplyNpcAppearance(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        // 自キャラ誤爆ガード
        if (globalIndex <= 0 || (objectTable.Length > 0 && objectTable[0]?.Address == (nint)chara))
        {
            logManager?.Warning($"ApplyNpcAppearance: Aborted attempt to apply appearance to LocalPlayer (GlobalIndex: {globalIndex}).");
            return;
        }

        logManager?.Info($"[Pipeline C: NPC] ApplyNpcAppearance: '{template.Name}' on Global#{globalIndex}...");

        bool glamSuccess = false;
        if (glamourerIpc != null && glamourerIpc.IsAvailable)
        {
            glamSuccess = glamourerIpc.ApplyNpcAppearance(actorIndex, template.CustomizeData, template.NpcEquipmentModelIds, showHeadgear: true, spawned?.PuppetName);
            logManager?.Info($"[Pipeline C: NPC] Glamourer ApplyNpcAppearance result on Global#{actorIndex}: {glamSuccess}");
        }

        if (!glamSuccess)
        {
            logManager?.Warning($"[Pipeline C: NPC] Glamourer NPC appearance failed or unavailable. Applying direct memory fallback...");
            ApplyNpcAppearanceDirectFallback(chara, template);
        }

        SafeSetWeaponVisibility(chara, template.WeaponVisible);
        logManager?.Info($"[Pipeline C: NPC] Applied Humanoid NPC appearance to Global#{actorIndex} (Glamourer: {glamSuccess}).");
    }

    /// <summary>
    /// 人型アクターの武器表示状態を安全に設定（DrawObject 未生成時やモンスターへの誤呼び出しによるクラッシュを防止）
    /// </summary>
    private static void SafeSetWeaponVisibility(Character* chara, bool visible)
    {
        if (chara == null) return;
        // モンスター（ModelCharaId > 0）には武器が存在しないため絶対に呼ばない
        if (chara->ModelContainer.ModelCharaId != 0) return;

        try
        {
            chara->DrawData.IsWeaponHidden = !visible;
            // DrawObject が存在する場合のみネイティブ HideWeapons を呼ぶ
            if (chara->GameObject.DrawObject != null)
            {
                chara->DrawData.HideWeapons(!visible);
            }
        }
        catch
        {
            // ネイティブアクセス例外を安全に吸収
        }
    }

    /// <summary>
    /// パイプライン A & B: 外見（Glamourer / Penumbra / MCDF / Customize+）の即時直接適用
    /// AQuestReborn (AQR) アーキテクチャ完全準拠
    /// </summary>
    public void ApplyAppearanceDirect(Character* chara, ushort globalIndex, CharacterTemplate template, SpawnedActorData? spawned = null, bool applyGlamourer = true)
    {
        if (chara == null) return;
        int actorIndex = (int)globalIndex;

        // 自キャラ誤爆の物理遮断ガード
        if (globalIndex <= 0 || (objectTable.Length > 0 && objectTable[0]?.Address == (nint)chara))
        {
            logManager?.Warning($"ApplyAppearanceDirect: Aborted attempt to apply appearance to LocalPlayer (GlobalIndex: {globalIndex}).");
            return;
        }

        logManager?.Info($"[Pipeline A/B] ApplyAppearanceDirect: '{template.Name}' (GlobalIndex: {actorIndex}, Source: {template.SourceType})...");

        // 前のステートや割り当てをクリーンアップ
        if (glamourerIpc != null && glamourerIpc.IsAvailable)
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

        // =========================================================================
        // パイプライン B: MCDF の場合 (AQR McdfCharaFileManager 準拠)
        // =========================================================================
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

                        // AQR 準拠: MCDF 内包の Base64 データを取得
                        string? designString = bundle.GlamourerDesign;
                        if (string.IsNullOrWhiteSpace(designString)) designString = template.GlamourerDesignString;

                        // 武器の表示・非表示
                        chara->DrawData.HideWeapons(!template.WeaponVisible);
                        chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

                        // 統合アピアランス遅延安定化キューへ登録 (DrawObject生成待ち Glamourer 適用 & Penumbra Redraw & CustomizePlus 確定注入)
                        if (spawned != null)
                        {
                            appearanceDeferredJobs.RemoveAll(j => j.Spawned == spawned || j.GlobalIndex == (ushort)actorIndex);
                            appearanceDeferredJobs.Add(new AppearanceDeferredJob
                            {
                                Spawned = spawned,
                                GlobalIndex = (ushort)actorIndex,
                                Template = template,
                                FallbackMcdfCPlusData = bundle.CustomizePlusData,
                                PendingGlamourerDesign = designString,
                                HasPenumbra = true,
                                HasCustomizePlus = !string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid) || !string.IsNullOrWhiteSpace(bundle.CustomizePlusData),
                                Ticks = 0
                            });
                        }

                        if (spawned != null) spawned.IsReady = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Error($"Error applying MCDF bundle: {ex.Message}");
                }
            }
            return;
        }

        // =========================================================================
        // パイプライン A: 通常の Glamourer & Penumbra & Customize+ (AQR 準拠)
        // =========================================================================

        // 1. Penumbra コレクションの適用 (Guid渡し)
        bool penSuccess = false;
        if (penumbraIpc.IsAvailable && !string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
        {
            penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
            logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on Global#{actorIndex}: {penSuccess}");
        }

        // 2. Glamourer デザインの適用 (Guid 指定または PlayerClone)
        bool glamApplied = false;
        string? activeDesignString = null;
        if (glamourerIpc.IsAvailable)
        {
            string? designString = template.GlamourerDesignString;

            if (!string.IsNullOrWhiteSpace(designString))
            {
                activeDesignString = designString;
                glamApplied = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
                logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamApplied}");
            }
            else if (template.SourceType == CharacterSourceType.PlayerClone)
            {
                var playerDesign = glamourerIpc.GetCustomization(0);
                if (!string.IsNullOrWhiteSpace(playerDesign))
                {
                    activeDesignString = playerDesign;
                    glamApplied = glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex, spawned?.PuppetName);
                    logManager?.Info($"Applied player customization clone via Glamourer to Global#{actorIndex} ('{spawned?.PuppetName}'): {glamApplied}");
                }
            }
        }

        // 3. Glamourer が適用されず、かつデザイン指定もない場合のみ、Penumbra 側で明示的に Redraw をトリガー
        // ※ デザイン指定がある場合は Phase 0 の遅延 Glamourer 適用で同期されるため先行 Redraw を抑止
        if (penSuccess && !glamApplied && string.IsNullOrWhiteSpace(activeDesignString))
        {
            penumbraIpc.Redraw(actorIndex);
        }

        // 4. 武器の表示・非表示
        SafeSetWeaponVisibility(chara, template.WeaponVisible);

        // 5. Customize+ Profile の初期適用
        ApplyCustomizePlusProfile(chara, actorIndex, template, spawned);

        // 6. 統合アピアランス遅延安定化キューへ登録 (Glamourer 遅延リトライ & モデル再構築完了後の CustomizePlus 体型復元 & Penumbra 確定)
        if (spawned != null)
        {
            appearanceDeferredJobs.RemoveAll(j => j.Spawned == spawned || j.GlobalIndex == (ushort)actorIndex);
            appearanceDeferredJobs.Add(new AppearanceDeferredJob
            {
                Spawned = spawned,
                GlobalIndex = (ushort)actorIndex,
                Template = template,
                PendingGlamourerDesign = activeDesignString,
                GlamourerApplied = glamApplied,
                DrawRebuilt = glamApplied,
                HasPenumbra = penSuccess,
                HasCustomizePlus = !string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid),
                Ticks = 0
            });
        }

        if (spawned != null) spawned.IsReady = true;
    }

    /// <summary>
    /// Customize+ Profile (テンプレート指定 または MCDF内包) を公式一時プロファイル IPC で適用
    /// ユーザーの設定ファイル (profiles/*.json) を一切汚染せず、メモリ上だけで安全にパペットに注入
    /// </summary>
    private void ApplyCustomizePlusProfile(Character* chara, int actorIndex, CharacterTemplate template, SpawnedActorData? spawned, string? fallbackMcdfCPlusData = null)
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
            customizePlusIpc.DeleteTemporaryProfileOnCharacter((ushort)actorIndex);

            // 過去バージョンで残骸となった恒久プロファイル紐付けがあれば解除
            if (spawned?.AssignedCustomizePlusGuid.HasValue == true)
            {
                ushort worldId = chara != null ? (ushort)chara->HomeWorld : (ushort)0;
                string puppetName = spawned.PuppetName ?? string.Empty;
                if (!string.IsNullOrEmpty(puppetName))
                {
                    customizePlusIpc.RemovePlayerCharacter(spawned.AssignedCustomizePlusGuid.Value, puppetName, worldId);
                }
                spawned.AssignedCustomizePlusGuid = null;
            }

            // 1. テンプレートで明示指定された CustomizePlus プロファイル
            if (!string.IsNullOrWhiteSpace(template.CustomizePlusProfileGuid) &&
                Guid.TryParse(template.CustomizePlusProfileGuid, out var profileGuid))
            {
                var tempGuid = customizePlusIpc.SetTemporaryProfileByGuid((ushort)actorIndex, profileGuid);
                if (tempGuid.HasValue && spawned != null)
                {
                    spawned.TemporaryCustomizePlusGuid = tempGuid.Value;
                    logManager?.Info($"CustomizePlus: Applied temporary profile '{template.CustomizePlusProfileName ?? profileGuid.ToString()}' ({profileGuid}) as temp {tempGuid.Value} on actor index {actorIndex}.");
                }
                else
                {
                    logManager?.Warning($"CustomizePlus: Failed to apply temporary profile '{profileGuid}' on actor index {actorIndex}.");
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

                try
                {
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(cPlusJson);
                    parsed["Enabled"] = true; // 強制有効化
                    string jsonToInject = parsed.ToString(Newtonsoft.Json.Formatting.None);

                    var tempGuid = customizePlusIpc.SetTemporaryProfile((ushort)actorIndex, jsonToInject);
                    if (tempGuid.HasValue && spawned != null)
                    {
                        spawned.TemporaryCustomizePlusGuid = tempGuid.Value;
                        logManager?.Info($"CustomizePlus: Applied MCDF temporary profile as temp {tempGuid.Value} on actor index {actorIndex}.");
                    }
                    else
                    {
                        logManager?.Warning($"CustomizePlus: Failed to apply MCDF temporary profile on actor index {actorIndex}.");
                    }
                }
                catch (Exception ex)
                {
                    logManager?.Warning($"Could not parse/apply MCDF CustomizePlus profile: {ex.Message}");
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
