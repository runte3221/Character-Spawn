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
    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;

    private readonly List<SpawnedActorData> activeActors = new();
    private readonly List<ushort> createdIndexes = new();

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    public ActorManager(
        IClientState clientState,
        IObjectTable objectTable,
        ISigScanner? sigScanner,
        IPluginLog log,
        TimelineManager timelineManager,
        HeadTrackingManager headTrackingManager,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc)
    {
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.timelineManager = timelineManager;
        this.headTrackingManager = headTrackingManager;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;

        this.clientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnTerritoryChanged(uint territoryType)
    {
        log.Information("Territory changed. Clearing spawned actors tracking.");
        activeActors.Clear();
        createdIndexes.Clear();
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
                log.Error("ClientObjectManager instance is null.");
                return null;
            }

            // 次のキャラクタースロットを作成
            uint idCheck = com->CreateBattleCharacter((uint)(2 + createdIndexes.Count), 0);
            if (idCheck == 0xFFFFFFFF)
            {
                log.Error("ClientObjectManager.CreateBattleCharacter returned 0xFFFFFFFF (failed to allocate).");
                return null;
            }

            ushort newId = (ushort)idCheck;
            createdIndexes.Add(newId);

            var newObject = com->GetObjectByIndex(newId);
            if (newObject == null)
            {
                log.Error($"Failed to retrieve spawned GameObject at index {newId}.");
                return null;
            }

            var nativeChara = (Character*)newObject;
            var pos = spawnPosition ?? GetDefaultSpawnPosition();
            var rot = spawnRotation ?? 0.0f;

            // 1. 外見のベースコピー（自キャラが存在する場合はプレイヤーからコピー）
            var localPlayer = objectTable.Length > 0 ? objectTable[0] : null;
            if (localPlayer != null && localPlayer.Address != 0)
            {
                var sourceNative = (Character*)localPlayer.Address;
                nativeChara->CharacterSetup.CopyFromCharacter(sourceNative, CharacterCopyFlags.WeaponHiding);
                nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);
            }

            // 2. モンスター／NPCモデルおよび外見データの適用
            if (template.SourceType == CharacterSourceType.Monster && template.ModelCharaId > 0)
            {
                nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
                nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);
            }
            else if (template.SourceType == CharacterSourceType.Npc)
            {
                if (template.ModelCharaId > 0)
                {
                    // 非人型NPC（モーグリ等の固有モデル）
                    nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
                    nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);
                }
                else
                {
                    // 人型NPC（ミューヌ等のHumanモデル）
                    // カスタマイズデータ（26バイト）を適用
                    if (template.CustomizeData != null && template.CustomizeData.Length >= 26)
                    {
                        fixed (byte* pCust = template.CustomizeData)
                        {
                            Buffer.MemoryCopy(pCust, &nativeChara->DrawData.CustomizeData, 26, 26);
                        }
                    }

                    // 装備モデルIDを適用
                    if (template.NpcEquipmentModelIds != null && template.NpcEquipmentModelIds.Length > 0)
                    {
                        var equipSpan = nativeChara->DrawData.EquipmentModelIds;
                        for (int i = 0; i < template.NpcEquipmentModelIds.Length && i < equipSpan.Length; i++)
                        {
                            equipSpan[i] = template.NpcEquipmentModelIds[i];
                        }
                    }

                    nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);
                }
            }

            // 3. 位置・回転を設定
            nativeChara->GameObject.DefaultPosition = pos;
            nativeChara->GameObject.Position = pos;
            nativeChara->GameObject.Rotation = rot;
            nativeChara->GameObject.DefaultRotation = rot;

            // 4. キャラクター名を設定（ゲーム内制限20文字）
            var rawName = string.IsNullOrWhiteSpace(template.Name) ? "Character" : template.Name;
            var cnpcName = rawName.Length > 20 ? rawName.Substring(0, 20) : rawName;
            ((GameObject*)nativeChara)->SetName(cnpcName);

            // 5. 描画を有効化
            nativeChara->GameObject.EnableDraw();

            var spawned = new SpawnedActorData
            {
                TemplateId = template.Id,
                DisplayName = template.Name,
                NativeAddress = (nint)nativeChara,
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

            // 外部アピアランス（Glamourer / Penumbra）の適用
            ApplyExternalAppearance(spawned, template);

            activeActors.Add(spawned);
            log.Information($"Successfully spawned character '{spawned.DisplayName}' (ID:{newId}, Addr:0x{spawned.NativeAddress:X}) at {pos}");

            return spawned;
        }
        catch (Exception ex)
        {
            log.Error($"Exception during SpawnCharacter: {ex}");
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
                        log.Information($"Deleted actor at object index {idx}.");
                    }
                }
                actor.NativeAddress = 0;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to despawn actor {actor.DisplayName}: {ex.Message}");
        }
        finally
        {
            activeActors.Remove(actor);
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
    }

    /// <summary>
    /// 毎フレームの更新処理（視線追従および描画状態の継続監視・保証）
    /// </summary>
    public void UpdateFrame()
    {
        headTrackingManager.UpdateTracking(activeActors);

        // 各アクターの描画状態を監視・強制（Brio / AQR方式）
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
        var actorIndex = chara->ObjectIndex;

        // Glamourerの適用
        if (glamourerIpc.IsAvailable)
        {
            if (!string.IsNullOrWhiteSpace(template.GlamourerDesignString))
            {
                glamourerIpc.ApplyDesignToActor(template.GlamourerDesignString, actorIndex);
            }
            else if (template.SourceType == CharacterSourceType.PlayerClone)
            {
                var playerDesign = glamourerIpc.GetCustomization(0);
                if (!string.IsNullOrWhiteSpace(playerDesign))
                {
                    glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex);
                }
                else
                {
                    glamourerIpc.ReapplyState(actorIndex);
                }
            }
            else
            {
                glamourerIpc.ReapplyState(actorIndex);
            }
        }

        // Penumbraコレクションの適用と RedrawObject のトリガー
        if (penumbraIpc.IsAvailable)
        {
            if (!string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
            {
                penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
            }
            penumbraIpc.Redraw(actorIndex);
        }
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
