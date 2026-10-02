using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
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
        McdfParser? mcdfParser = null)
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

        this.clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        logManager?.Info("Territory changed. Clearing spawned actors tracking.");
        readyJobs.Clear();
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
    }

    private string NextPuppetName()
    {
        var n = nameSerial++;
        var hi = (char)('A' + (n / 26) % 26);
        var lo = (char)('a' + n % 26);
        return $"Cs {hi}{lo}";
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

            logManager?.Info($"Spawning '{template.Name}' (Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, Weapon: {template.WeaponVisible}) at COM#{comIdx}...");

            // HDM & Brio 黄金パターン:
            // モンスター・NPC・Glamourer問わず、まず自キャラからダブルコピーして完全な drawable 状態を確立する
            // （素の SetupBNpc や空の BattleNpc は不可視となり、Glamourer も認識できない）
            nativeChara->CharacterSetup.CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding);
            nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

            // ベースラインのリセット（自キャラがマウント中や変身中であってもクリーンな状態から開始）
            nativeChara->ModelContainer.ModelCharaId = 0;
            nativeChara->GameObject.Scale = 1.0f;
            nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            // クラス分類: 非ターゲット・プレイヤー骨格 BattleNpc
            nativeChara->GameObject.ObjectKind = ObjectKind.BattleNpc;
            nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;
            nativeChara->GameObject.TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;

            // Glamourer Identity のスタンプ (The 0.8.44 Bug 対策: SE有効な一意の姓名とワールドを付与)
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
            // COM index と ObjectTable の global index は異なる（GPose/カットシーン枠 ~200-244 に入る）
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

            // 2フェーズ待機キュー (Draw-when-ready) にエンキュー
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

        if (actor.NativeAddress == 0) return;

        var chara = (Character*)actor.NativeAddress;
        if (chara == null) return;

        chara->GameObject.SetPosition(newPosition.X, newPosition.Y, newPosition.Z);
        chara->GameObject.SetRotation(newRotation);
        chara->GameObject.DefaultPosition = newPosition;
        chara->GameObject.DefaultRotation = newRotation;
    }

    /// <summary>
    /// アニメーション・表情の適用
    /// </summary>
    public void ApplyActorAnimation(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        var chara = (Character*)actor.NativeAddress;
        if (chara == null) return;

        timelineManager.ApplyTimeline(chara, actor.Animation);
    }

    /// <summary>
    /// ターゲット可否設定を適用
    /// </summary>
    public void ApplyTargetable(SpawnedActorData actor)
    {
        if (actor.NativeAddress == 0) return;
        var obj = (GameObject*)actor.NativeAddress;
        if (obj == null) return;

        if (actor.IsTargetable)
            obj->TargetableStatus |= ObjectTargetableFlags.IsTargetable;
        else
            obj->TargetableStatus &= ~ObjectTargetableFlags.IsTargetable;
    }

    /// <summary>
    /// 指定アクターをデスポーン（破棄）: HDM & Brio DestroyObject パターン
    /// </summary>
    public void DespawnCharacter(SpawnedActorData actor)
    {
        try
        {
            readyJobs.RemoveAll(j => j.Spawned == actor || j.GlobalIndex == actor.GlobalIndex);

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
    /// 毎フレームの更新処理: HDM Draw-when-ready 2フェーズポーリング & 視線追従
    /// </summary>
    public void UpdateFrame()
    {
        headTrackingManager.UpdateTracking(activeActors);

        // HDM Draw-when-ready 2フェーズポーリング
        if (readyJobs.Count > 0)
        {
            for (int i = readyJobs.Count - 1; i >= 0; i--)
            {
                var job = readyJobs[i];
                job.Ticks++;

                // 追跡解除されたアクターは除外
                if (!activeActors.Contains(job.Spawned) || job.Spawned.NativeAddress == 0)
                {
                    readyJobs.RemoveAt(i);
                    continue;
                }

                // ウォームアップ待機（Brio dontStartFor: 2）
                if (job.Ticks <= ReadyWarmupTicks) continue;

                var chara = (Character*)job.Spawned.NativeAddress;

                // Phase 1: IsReadyToDraw() を待って EnableDraw()
                if (!job.DrawEnabled)
                {
                    bool ready = chara->GameObject.IsReadyToDraw();
                    if (!ready && job.Ticks < MaxReadyTicks) continue;

                    chara->GameObject.EnableDraw();
                    job.DrawEnabled = true;
                    continue;
                }

                // Phase 2: DrawObject が存在し、かつ IsVisible になるまで待機
                // Glamourer は描画オブジェクトが可視状態になって初めて完全な外見を適用できる
                var draw = chara->GameObject.DrawObject;
                bool visible = draw != null && draw->IsVisible;
                if (!visible && job.Ticks < MaxReadyTicks) continue;

                // 準備完了！外見適用を実行
                readyJobs.RemoveAt(i);
                try
                {
                    ApplyExternalAppearance(job.Spawned, job.Template);
                }
                catch (Exception ex)
                {
                    logManager?.Error($"Error applying appearance for {job.Spawned.DisplayName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 外見（Glamourer / Penumbra / MCDF / モンスター / NPC）の適用
    /// </summary>
    private void ApplyExternalAppearance(SpawnedActorData spawned, CharacterTemplate template)
    {
        if (spawned.NativeAddress == 0) return;

        var chara = (Character*)spawned.NativeAddress;
        int actorIndex = (int)spawned.GlobalIndex;

        logManager?.Info($"Applying appearance to actor '{spawned.DisplayName}' (GlobalIndex: {actorIndex}, Source: {template.SourceType})...");

        // 1. Penumbra コレクションの適用 (AQuestReborn 方式: コレクションを先に設定)
        if (penumbraIpc.IsAvailable && !string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
        {
            bool penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
            logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on Global#{actorIndex}: {penSuccess}");
        }

        // 2. モンスター / 非人型アクターの場合 (HDM GuiseService 方式)
        if (template.ModelCharaId > 0)
        {
            chara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
            chara->GameObject.Scale = 1.0f;
            chara->DrawData.HideWeapons(true);
            chara->DrawData.IsWeaponHidden = true;

            // HDM Redraw: Penumbra Redraw が利用可能なら実行、なければ DisableDraw/EnableDraw
            if (penumbraIpc.IsAvailable)
            {
                penumbraIpc.Redraw(actorIndex);
            }
            else
            {
                chara->GameObject.DisableDraw();
                chara->GameObject.EnableDraw();
            }

            logManager?.Info($"Applied Monster ModelCharaId {template.ModelCharaId} to Global#{actorIndex}.");
            return;
        }

        // 3. NPC (人型, ENpc) の場合
        if (template.SourceType == CharacterSourceType.Npc)
        {
            if (template.CustomizeData != null && template.CustomizeData.Length >= 26)
            {
                fixed (byte* pCust = template.CustomizeData)
                {
                    Buffer.MemoryCopy(pCust, &chara->DrawData.CustomizeData, 26, 26);
                }
                logManager?.Info($"Applied NPC CustomizeData to Global#{actorIndex}.");
            }

            if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
            {
                var equipSpan = chara->DrawData.EquipmentModelIds;
                for (int i = 0; i < template.NpcEquipmentModelIds.Length && i < equipSpan.Length; i++)
                {
                    equipSpan[i] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[i] };
                }
                logManager?.Info($"Applied {template.NpcEquipmentModelIds.Length} NPC EquipmentModelIds to Global#{actorIndex}.");
            }

            // 武器の表示・非表示
            chara->DrawData.HideWeapons(!template.WeaponVisible);
            chara->DrawData.IsWeaponHidden = !template.WeaponVisible;

            chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None);

            if (penumbraIpc.IsAvailable)
            {
                penumbraIpc.Redraw(actorIndex);
            }
            logManager?.Info($"Applied Humanoid NPC appearance to Global#{actorIndex}.");
            return;
        }

        // 4. Glamourer / MCDF / PlayerClone の適用 (AQR 方式)
        if (glamourerIpc.IsAvailable)
        {
            string? designString = template.GlamourerDesignString;

            // MCDF の場合：デザイン文字列が空ならファイルから再読み込み
            if (template.SourceType == CharacterSourceType.Mcdf && string.IsNullOrWhiteSpace(designString) && !string.IsNullOrWhiteSpace(template.McdfFilePath) && mcdfParser != null)
            {
                var parsed = mcdfParser.ParseMcdf(template.McdfFilePath);
                if (parsed != null && !string.IsNullOrEmpty(parsed.GlamourerDesign))
                {
                    designString = parsed.GlamourerDesign;
                    template.GlamourerDesignString = designString;
                    logManager?.Info("Loaded Glamourer design string from MCDF file on draw ready.");
                }
            }

            if (!string.IsNullOrWhiteSpace(designString))
            {
                bool glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex);
                logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex}: {glamSuccess}");
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
            if (template.SourceType == CharacterSourceType.Glamourer || template.SourceType == CharacterSourceType.Mcdf)
            {
                logManager?.Warning("Glamourer IPC not detected. Could not apply external appearance design.");
            }
        }

        // 5. Penumbra Redraw (AQuestReborn 方式: 最後に必ず Redraw)
        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
            logManager?.Info($"Triggered Penumbra Redraw for Global#{actorIndex}.");
        }
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
