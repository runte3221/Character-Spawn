using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.Managers;

public unsafe class ActorManager : IDisposable
{
    private readonly IClientState clientState;
    private readonly IGameInteropProvider? interopProvider;
    private readonly IPluginLog log;
    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;

    private readonly List<SpawnedActorData> activeActors = new();

    // Native function delegates
    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate nint CreateBattleCharaDelegate(
        nint characterManager,
        byte* name,
        byte* customizeData,
        uint dataId,
        byte unk1,
        byte unk2,
        byte unk3
    );

    [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
    private delegate void DeleteBattleCharaDelegate(
        nint characterManager,
        nint chara
    );

    private CreateBattleCharaDelegate? createBattleChara;
    private DeleteBattleCharaDelegate? deleteBattleChara;

    public IReadOnlyList<SpawnedActorData> ActiveActors => activeActors;

    public ActorManager(
        IClientState clientState,
        IGameInteropProvider? interopProvider,
        IPluginLog log,
        TimelineManager timelineManager,
        HeadTrackingManager headTrackingManager,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc)
    {
        this.clientState = clientState;
        this.interopProvider = interopProvider;
        this.log = log;
        this.timelineManager = timelineManager;
        this.headTrackingManager = headTrackingManager;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;

        InitializeNativeDelegates();
    }

    private void InitializeNativeDelegates()
    {
        if (interopProvider == null) return;

        try
        {
            // FFXIV CreateBattleChara signature
            if (interopProvider.TryScanSig("E8 ?? ?? ?? ?? 48 8B F8 48 85 C0 74 38 48 8B CB", out var createPtr))
            {
                createBattleChara = Marshal.GetDelegateForFunctionPointer<CreateBattleCharaDelegate>(createPtr);
                log.Information($"Found CreateBattleChara at 0x{createPtr:X}");
            }
            else
            {
                log.Warning("Could not resolve CreateBattleChara signature via SigScanner.");
            }

            // FFXIV DeleteBattleChara signature
            if (interopProvider.TryScanSig("E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 20 5F C3 48 8B 0D", out var deletePtr))
            {
                deleteBattleChara = Marshal.GetDelegateForFunctionPointer<DeleteBattleCharaDelegate>(deletePtr);
                log.Information($"Found DeleteBattleChara at 0x{deletePtr:X}");
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Native delegate resolution exception: {ex.Message}");
        }
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

            var actorName = string.IsNullOrWhiteSpace(template.Name) ? "Character" : template.Name;
            var nameBytes = Encoding.UTF8.GetBytes(actorName + "\0");
            var customBytes = template.CustomizeData ?? new byte[26];

            nint nativeCharaAddr = 0;

            if (createBattleChara != null)
            {
                fixed (byte* pName = nameBytes)
                fixed (byte* pCustom = customBytes)
                {
                    nativeCharaAddr = createBattleChara(
                        (nint)charaManager,
                        pName,
                        template.SourceType == CharacterSourceType.PlayerClone || template.SourceType == CharacterSourceType.Glamourer ? pCustom : (byte*)0,
                        template.DataId,
                        0,
                        0,
                        0
                    );
                }
            }

            if (nativeCharaAddr == 0)
            {
                log.Warning("CreateBattleChara was not available or returned null. Operating in staged mode.");
            }

            var nativeChara = (Character*)nativeCharaAddr;
            if (nativeChara != null)
            {
                nativeChara->SetPosition(pos.X, pos.Y, pos.Z);
                nativeChara->SetRotation(rot);
            }

            var spawned = new SpawnedActorData
            {
                TemplateId = template.Id,
                DisplayName = template.Name,
                NativeAddress = nativeCharaAddr,
                GameObjectId = nativeChara != null ? nativeChara->EntityId : 0,
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

        obj->TargetableStatus = (byte)(actor.IsTargetable ? 1 : 0);
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
                if (charaManager != null && deleteBattleChara != null)
                {
                    deleteBattleChara((nint)charaManager, actor.NativeAddress);
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
