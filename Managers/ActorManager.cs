using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.Managers;

public unsafe class ActorManager : IDisposable
{
    private readonly IClientState clientState;
    private readonly IPluginLog log;
    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;

    private readonly List<SpawnedActorData> activeActors = new();

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    public ActorManager(
        IClientState clientState,
        IPluginLog log,
        TimelineManager timelineManager,
        HeadTrackingManager headTrackingManager,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc)
    {
        this.clientState = clientState;
        this.log = log;
        this.timelineManager = timelineManager;
        this.headTrackingManager = headTrackingManager;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;
    }

    /// <summary>
    /// テンプレートをもとに新しいキャラクターをスポーンする
    /// </summary>
    public SpawnedActorData? SpawnCharacter(CharacterTemplate template, Vector3? spawnPosition = null, float? spawnRotation = null)
    {
        try
        {
            var charaManager = CharacterManager.Instance();
            if (charaManager == null)
            {
                log.Error("CharacterManager instance is null.");
                return null;
            }

            var pos = spawnPosition ?? GetDefaultSpawnPosition();
            var rot = spawnRotation ?? 0.0f;

            // ネイティブアクターの生成
            var actorName = string.IsNullOrWhiteSpace(template.Name) ? "Character" : template.Name;
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(actorName + "\0");
            var customBytes = template.CustomizeData ?? new byte[26];

            Character* nativeChara = null;

            fixed (byte* pName = nameBytes)
            {
                if (template.SourceType == CharacterSourceType.Monster)
                {
                    // モンスター/BNpc生成
                    nativeChara = (Character*)charaManager->CreateBattleChara(
                        pName,
                        (byte*)0,
                        template.DataId,
                        0,
                        0,
                        0
                    );
                }
                else if (template.SourceType == CharacterSourceType.Npc)
                {
                    // NPC/ENpc生成
                    nativeChara = (Character*)charaManager->CreateBattleChara(
                        pName,
                        (byte*)0,
                        template.DataId,
                        0,
                        0,
                        0
                    );
                }
                else
                {
                    // プレイヤー互換/カスタムキャラクター生成
                    fixed (byte* pCustom = customBytes)
                    {
                        nativeChara = (Character*)charaManager->CreateBattleChara(
                            pName,
                            pCustom,
                            0,
                            0,
                            0,
                            0
                        );
                    }
                }
            }

            if (nativeChara == null)
            {
                log.Error("Failed to create native character instance.");
                return null;
            }

            // 位置と向きを設定
            nativeChara->SetPosition(pos.X, pos.Y, pos.Z);
            nativeChara->SetRotation(rot);

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

            // ターゲット可否フラグの適用
            ApplyTargetable(spawned);

            // Glamourer / Penumbra の適用
            ApplyExternalAppearance(spawned, template);

            activeActors.Add(spawned);
            log.Information($"Spawned character '{spawned.DisplayName}' at {pos}");

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

        chara->SetPosition(newPosition.X, newPosition.Y, newPosition.Z);
        chara->SetRotation(newRotation);
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

        if (!actor.IsTargetable)
        {
            // ターゲット不可に設定
            obj->TargetableStatus = 0;
        }
        else
        {
            // ターゲット可能に設定
            obj->TargetableStatus = 1;
        }
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
                var charaManager = CharacterManager.Instance();
                if (charaManager != null)
                {
                    charaManager->DeleteBattleChara((BattleChara*)actor.NativeAddress);
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
    }

    /// <summary>
    /// 毎フレームの更新処理（視線追従等）
    /// </summary>
    public void UpdateFrame()
    {
        headTrackingManager.UpdateTracking(activeActors);
    }

    private void ApplyExternalAppearance(SpawnedActorData spawned, CharacterTemplate template)
    {
        if (spawned.NativeAddress == 0) return;

        var chara = (Character*)spawned.NativeAddress;
        var actorIndex = chara->ObjectIndex;

        // Glamourerの適用
        if (!string.IsNullOrWhiteSpace(template.GlamourerDesignString))
        {
            glamourerIpc.ApplyDesignToActor(template.GlamourerDesignString, actorIndex);
        }

        // Penumbraコレクションの適用
        if (!string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
        {
            penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
        }
    }

    private Vector3 GetDefaultSpawnPosition()
    {
        var player = clientState.LocalPlayer;
        if (player == null) return Vector3.Zero;

        // 自キャラの正面1.5mの位置を初期位置にする
        var rot = player.Rotation;
        var forward = new Vector3((float)Math.Sin(rot), 0, (float)Math.Cos(rot));
        return player.Position + (forward * 1.5f);
    }

    public void Dispose()
    {
        DespawnAll();
    }
}
