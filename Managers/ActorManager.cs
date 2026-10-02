using System.Numerics;
using System.Text;
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
        activeActors.Clear();
        createdIndexes.Clear();
        CurrentPreviewActor = null;
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
    /// テンプレートをもとに新しいキャラクターをスポーンする
    /// </summary>
    public SpawnedActorData? SpawnCharacter(CharacterTemplate template, Vector3? spawnPosition = null, float? spawnRotation = null)
    {
        try
        {
            var com = ClientObjectManager.Instance();
            if (com == null)
            {
                logManager?.Error("ClientObjectManager instance is null.");
                return null;
            }

            // 次のキャラクタースロットを作成
            uint idCheck = com->CreateBattleCharacter((uint)(2 + createdIndexes.Count), 0);
            if (idCheck == 0xFFFFFFFF)
            {
                logManager?.Error("ClientObjectManager.CreateBattleCharacter returned 0xFFFFFFFF (slot limit reached).");
                return null;
            }

            ushort newId = (ushort)idCheck;
            createdIndexes.Add(newId);

            var newObject = com->GetObjectByIndex(newId);
            if (newObject == null)
            {
                logManager?.Error($"Failed to retrieve spawned GameObject at index {newId}.");
                return null;
            }

            var nativeChara = (Character*)newObject;
            var pos = spawnPosition ?? GetDefaultSpawnPosition();
            var rot = spawnRotation ?? 0.0f;

            logManager?.Info($"Spawning '{template.Name}' (Source: {template.SourceType}, ModelChara: {template.ModelCharaId}, WeaponVisible: {template.WeaponVisible}) at index {newId}...");

            bool isMonsterOrNonHumanoid = template.ModelCharaId > 0;

            if (isMonsterOrNonHumanoid)
            {
                // ========== 非人型アクター（モンスター、モーグリ等の固有モデル） ==========
                // AQuestReborn 方式: 自前での人型コピーや再構築は行わず、ModelCharaId を設定し武器を隠す
                logManager?.Info($"Setting Non-humanoid ModelCharaId: {template.ModelCharaId}");
                nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;

                // 武器は非表示に設定
                nativeChara->DrawData.HideWeapons(true);
                nativeChara->DrawData.IsWeaponHidden = true;
            }
            else
            {
                // ========== 人型アクター（Humanモデル） ==========
                // 自キャラが存在する場合は人型スケルトンのベースとしてコピー
                var localPlayer = objectTable.Length > 0 ? objectTable[0] : null;
                if (localPlayer != null && localPlayer.Address != 0)
                {
                    var sourceNative = (Character*)localPlayer.Address;
                    nativeChara->CharacterSetup.CopyFromCharacter(sourceNative, CharacterCopyFlags.WeaponHiding);
                }

                // NPC (ENpc) 人型外見データの適用
                if (template.SourceType == CharacterSourceType.Npc)
                {
                    // カスタマイズデータ（26バイト）を適用
                    if (template.CustomizeData != null && template.CustomizeData.Length >= 26)
                    {
                        fixed (byte* pCust = template.CustomizeData)
                        {
                            Buffer.MemoryCopy(pCust, &nativeChara->DrawData.CustomizeData, 26, 26);
                        }
                        logManager?.Info("Applied NPC 26-byte CustomizeData.");
                    }

                    // 装備モデルIDを適用
                    if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
                    {
                        var equipSpan = nativeChara->DrawData.EquipmentModelIds;
                        for (int i = 0; i < template.NpcEquipmentModelIds.Length && i < equipSpan.Length; i++)
                        {
                            equipSpan[i] = new EquipmentModelId { Value = template.NpcEquipmentModelIds[i] };
                        }
                        logManager?.Info($"Applied {template.NpcEquipmentModelIds.Length} NPC EquipmentModelIds.");
                    }
                }

                // 武器の表示・非表示制御
                if (!template.WeaponVisible)
                {
                    nativeChara->DrawData.HideWeapons(true);
                    nativeChara->DrawData.IsWeaponHidden = true;
                    logManager?.Info("Weapon hidden per template WeaponVisible=false setting.");
                }
                else
                {
                    nativeChara->DrawData.HideWeapons(false);
                    nativeChara->DrawData.IsWeaponHidden = false;
                }

                nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);
            }

            // 位置・回転を設定
            nativeChara->GameObject.DefaultPosition = pos;
            nativeChara->GameObject.Position = pos;
            nativeChara->GameObject.Rotation = rot;
            nativeChara->GameObject.DefaultRotation = rot;

            // キャラクター名を設定（ゲーム内制限20文字）
            var rawName = string.IsNullOrWhiteSpace(template.Name) ? "Character" : template.Name;
            var cnpcName = rawName.Length > 20 ? rawName.Substring(0, 20) : rawName;
            ((GameObject*)nativeChara)->SetName(cnpcName);

            // 描画を有効化
            nativeChara->GameObject.EnableDraw();

            var spawned = new SpawnedActorData
            {
                TemplateId = template.Id,
                DisplayName = template.Name,
                NativeAddress = (nint)nativeChara,
                SlotIndex = newId,
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
                IsTargetable = true
            };

            // ターゲット可否フラグ
            ApplyTargetable(spawned);

            // 外部アピアランス（Glamourer / Penumbra / MCDF）の適用
            ApplyExternalAppearance(spawned, template);

            activeActors.Add(spawned);
            logManager?.Info($"Spawn successful: '{spawned.DisplayName}' at slot {newId} (Addr: 0x{spawned.NativeAddress:X}).");

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

        chara->GameObject.Position = newPosition;
        chara->GameObject.DefaultPosition = newPosition;
        chara->GameObject.Rotation = newRotation;
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

        obj->TargetableStatus = actor.IsTargetable ? ObjectTargetableFlags.IsTargetable : 0;
    }

    /// <summary>
    /// 指定アクターをデスポーン（破棄）
    /// </summary>
    public void DespawnCharacter(SpawnedActorData actor)
    {
        try
        {
            if (actor.NativeAddress != 0)
            {
                var com = ClientObjectManager.Instance();
                if (com != null)
                {
                    var idx = com->GetIndexByObject((GameObject*)actor.NativeAddress);
                    if (idx != 0xFFFFFFFF)
                    {
                        createdIndexes.Remove((ushort)idx);
                        com->DeleteObjectByIndex((ushort)idx, 0);
                        logManager?.Info($"Deleted actor '{actor.DisplayName}' at slot {idx}.");
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
    /// 毎フレームの更新処理（視線追従および描画状態の継続監視・保証）
    /// </summary>
    public void UpdateFrame()
    {
        headTrackingManager.UpdateTracking(activeActors);

        // 各アクターの描画状態を監視・保証
        foreach (var actor in activeActors)
        {
            if (actor.NativeAddress == 0) continue;
            var chara = (Character*)actor.NativeAddress;

            // 1. DrawObject が存在する場合、隠蔽フラグ (0x10) をクリア
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
                // DrawObject が生成されるまで EnableDraw を試行
                chara->GameObject.EnableDraw();
            }

            // 2. 準備ができていれば描画を有効化
            if (chara->GameObject.IsReadyToDraw())
            {
                chara->GameObject.EnableDraw();
            }
        }
    }

    private void ApplyExternalAppearance(SpawnedActorData spawned, CharacterTemplate template)
    {
        if (spawned.NativeAddress == 0) return;

        var chara = (Character*)spawned.NativeAddress;
        int actorIndex = spawned.SlotIndex > 0 ? (int)spawned.SlotIndex : (int)chara->ObjectIndex;

        // 1. Penumbraコレクションの適用 (AQuestReborn順序: コレクションを先に設定)
        if (penumbraIpc.IsAvailable)
        {
            if (!string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
            {
                bool penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
                logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on slot {actorIndex}: {penSuccess}");
            }
        }

        // 2. モンスター / 非人型アクターの場合
        if (template.ModelCharaId > 0)
        {
            chara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
            chara->DrawData.HideWeapons(true);
            chara->DrawData.IsWeaponHidden = true;
            logManager?.Info($"Applied ModelCharaId {template.ModelCharaId} to actor slot {actorIndex}.");
        }
        else
        {
            // 3. 人型アクターの外見（Glamourer / MCDF / PlayerClone）の適用
            if (glamourerIpc.IsAvailable)
            {
                string? designString = template.GlamourerDesignString;

                // MCDF の場合：デザイン文字列が空なら MCDF ファイルから再パースを試みる
                if (template.SourceType == CharacterSourceType.Mcdf && string.IsNullOrWhiteSpace(designString) && !string.IsNullOrWhiteSpace(template.McdfFilePath) && mcdfParser != null)
                {
                    var parsed = mcdfParser.ParseMcdf(template.McdfFilePath);
                    if (parsed != null && !string.IsNullOrEmpty(parsed.GlamourerDesign))
                    {
                        designString = parsed.GlamourerDesign;
                        template.GlamourerDesignString = designString;
                        logManager?.Info("Loaded Glamourer design string from MCDF file on spawn.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(designString))
                {
                    bool glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex);
                    logManager?.Info($"Glamourer ApplyDesign result on slot {actorIndex}: {glamSuccess}");
                }
                else if (template.SourceType == CharacterSourceType.PlayerClone)
                {
                    var playerDesign = glamourerIpc.GetCustomization(0);
                    if (!string.IsNullOrWhiteSpace(playerDesign))
                    {
                        glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex);
                        logManager?.Info("Applied player customization clone via Glamourer.");
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
        }

        // 4. Penumbra RedrawObject (AQuestReborn方式: 最後に必ず Redraw)
        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
            logManager?.Info($"Triggered Penumbra Redraw for slot {actorIndex}.");
        }
    }

    /// <summary>
    /// プレビュー中アクターの武器表示状態を切り替えて即座に再描画する
    /// </summary>
    public void SetWeaponVisibility(SpawnedActorData actor, bool visible)
    {
        if (actor.NativeAddress == 0) return;
        var nativeChara = (Character*)actor.NativeAddress;
        int actorIndex = actor.SlotIndex > 0 ? (int)actor.SlotIndex : (int)nativeChara->ObjectIndex;

        nativeChara->DrawData.HideWeapons(!visible);
        nativeChara->DrawData.IsWeaponHidden = !visible;
        nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);

        if (penumbraIpc.IsAvailable)
        {
            penumbraIpc.Redraw(actorIndex);
        }
        logManager?.Info($"Updated weapon visibility for '{actor.DisplayName}' (Visible: {visible}, Slot: {actorIndex}).");
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
