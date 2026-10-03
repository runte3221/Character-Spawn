using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using CharacterSpawn.Models;
using CharacterSpawn.Managers;

namespace CharacterSpawn.Services;

/// <summary>
/// アクターの自律移動、ウェイポイント巡回（パトロール）、プレイヤー接近追従、ホーム帰還ルーチンを制御するサービス
/// </summary>
public unsafe class MovementService : IDisposable
{
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly ActorManager actorManager;
    private readonly AnimationService? animationService;
    private readonly IPluginLog log;
    private readonly LogManager? logManager;

    private readonly ConcurrentDictionary<Guid, ActiveMovementState> activeMovements = new();

    public enum MovementRuntimeState
    {
        Idle,               // 静止・初期待機
        MovingToWaypoint,   // 次のウェイポイントへ移動中
        WaitingAtWaypoint,  // ウェイポイント到着後の待機中
        FollowingPlayer,    // プレイヤーに歩み寄り・追従中
        ReturningHome       // ホーム位置へ帰還中
    }

    private class ActiveMovementState
    {
        public Guid PlacementId { get; set; }
        public string InstanceId { get; set; } = string.Empty;
        public SpawnedActorData Actor { get; set; } = null!;
        public SceneActorMovementConfig Config { get; set; } = new();
        public SceneActorMotionConfig MotionConfig { get; set; } = new();

        public Vector3 HomePosition { get; set; }
        public float HomeRotation { get; set; }

        public MovementRuntimeState State { get; set; } = MovementRuntimeState.Idle;
        public int CurrentWaypointIndex { get; set; } = 0;
        public int PatrolDirection { get; set; } = 1; // +1 or -1 (PingPong用)
        public float WaitTimer { get; set; } = 0.0f;

        public Vector3 CurrentPosition { get; set; }
        public float CurrentRotation { get; set; }

        public Vector3 FollowStartPosition { get; set; } = Vector3.Zero; // 追従を開始した地点の座標

        public bool IsMovingAnimationPlaying { get; set; } = false;
    }

    public MovementService(
        IFramework framework,
        IObjectTable objectTable,
        ActorManager actorManager,
        AnimationService? animationService,
        IPluginLog log,
        LogManager? logManager = null)
    {
        this.framework = framework;
        this.objectTable = objectTable;
        this.actorManager = actorManager;
        this.animationService = animationService;
        this.log = log;
        this.logManager = logManager;

        this.framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    /// アクターの自律移動を開始・登録
    /// </summary>
    public void StartMovement(SceneActorPlacement placement, SpawnedActorData actor)
    {
        if (placement == null || actor == null || actor.NativeAddress == 0) return;

        // 移動設定が無効の場合は解除
        if (placement.Movement.Mode == MovementMode.None)
        {
            StopMovement(placement.PlacementId);
            return;
        }

        var state = new ActiveMovementState
        {
            PlacementId = placement.PlacementId,
            InstanceId = actor.InstanceId,
            Actor = actor,
            Config = placement.Movement,
            MotionConfig = placement.Motion,
            HomePosition = placement.Position,
            HomeRotation = placement.Rotation,
            CurrentPosition = placement.Position,
            CurrentRotation = placement.Rotation,
            State = MovementRuntimeState.Idle,
            CurrentWaypointIndex = 0,
            PatrolDirection = 1,
            WaitTimer = 0.0f
        };

        if (placement.Movement.Mode == MovementMode.Patrol || placement.Movement.Mode == MovementMode.PatrolAndFollow)
        {
            if (placement.Movement.Waypoints.Count > 0)
            {
                state.State = MovementRuntimeState.MovingToWaypoint;
            }
        }

        activeMovements[placement.PlacementId] = state;
        log.Information($"[MovementService] Started movement for actor '{placement.CustomDisplayName}' (Mode: {placement.Movement.Mode}, Waypoints: {placement.Movement.Waypoints.Count})");
    }

    /// <summary>
    /// アクターの自律移動を停止・解除
    /// </summary>
    public void StopMovement(Guid placementId)
    {
        if (activeMovements.TryRemove(placementId, out var state))
        {
            log.Information($"[MovementService] Stopped movement for placement {placementId}");
        }
    }

    /// <summary>
    /// 全アクターの自律移動をクリア
    /// </summary>
    public void ClearAll()
    {
        activeMovements.Clear();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (activeMovements.IsEmpty) return;

        float deltaTime = (float)framework.UpdateDelta.TotalSeconds;
        if (deltaTime <= 0.0001f) return;
        // フレームレート急減時の巨大ワープ防止
        deltaTime = MathF.Min(deltaTime, 0.1f);

        Vector3 localPlayerPos = actorManager.LocalPlayerPosition;
        bool isLocalPlayerValid = actorManager.IsLocalPlayerReady;

        foreach (var kvp in activeMovements)
        {
            var state = kvp.Value;
            try
            {
                UpdateActorMovement(state, deltaTime, localPlayerPos, isLocalPlayerValid);
            }
            catch (Exception ex)
            {
                log.Error(ex, $"[MovementService] Error updating movement for actor '{state.InstanceId}'");
            }
        }
    }

    private void UpdateActorMovement(ActiveMovementState state, float deltaTime, Vector3 playerPos, bool isPlayerValid)
    {
        var actor = state.Actor;
        if (actor == null || actor.NativeAddress == 0 || !actor.IsReady) return;

        var chara = (Character*)actor.NativeAddress;
        if (chara == null) return;

        // 現在位置とホーム位置
        Vector3 curPos = state.CurrentPosition;
        float curRot = state.CurrentRotation;
        var config = state.Config;

        // 1. プレイヤー接近検知 ＆ 追従判定 (FollowPlayer または PatrolAndFollow)
        bool allowFollow = config.Mode == MovementMode.FollowPlayer || config.Mode == MovementMode.PatrolAndFollow;
        if (allowFollow && isPlayerValid)
        {
            // 動いているカスタムスポーンの現在位置（中心）からプレイヤーまでの実距離
            float distToPlayer = Vector3.Distance(curPos, playerPos);

            if (state.State == MovementRuntimeState.FollowingPlayer)
            {
                // 追従中断判定:
                // 1) プレイヤーが検知距離より離れた
                // 2) 追従開始地点からの移動距離がテリトリー限界を超えた (PatrolAndFollow時)
                // 3) または初期ホーム位置からの距離がテリトリー限界を超えた (FollowPlayer時)
                bool isOutOfTerritory = config.Mode == MovementMode.PatrolAndFollow
                    ? (state.FollowStartPosition != Vector3.Zero && Vector3.Distance(curPos, state.FollowStartPosition) > config.MaxTerritoryDistance)
                    : Vector3.Distance(curPos, state.HomePosition) > config.MaxTerritoryDistance;

                if (distToPlayer > config.FollowTriggerDistance * 1.6f || isOutOfTerritory)
                {
                    if (config.Mode == MovementMode.PatrolAndFollow && config.Waypoints.Count > 0)
                    {
                        // 巡回中の追従中断時: ホームではなく、直前に目指していたウェイポイントへ直接復帰して巡回を続行！
                        state.State = MovementRuntimeState.MovingToWaypoint;
                    }
                    else if (config.ReturnToHome)
                    {
                        state.State = MovementRuntimeState.ReturningHome;
                    }
                    else
                    {
                        state.State = MovementRuntimeState.Idle;
                    }
                }
                else
                {
                    // 追従移動継続
                    StepTowardTarget(state, playerPos, config.FollowStopDistance, deltaTime, isPlayerTarget: true);
                    return;
                }
            }
            else if (state.State != MovementRuntimeState.ReturningHome)
            {
                // 追従開始判定:
                // 巡回中(PatrolAndFollow)は、現在地からプレイヤーまでの距離(Trigger Dist)のみで即座に反応！
                // 単体追従(FollowPlayer)の場合は、ホームからの距離制限も加味。
                bool canTriggerFollow = config.Mode == MovementMode.PatrolAndFollow
                    ? distToPlayer <= config.FollowTriggerDistance
                    : (distToPlayer <= config.FollowTriggerDistance && Vector3.Distance(curPos, state.HomePosition) <= config.MaxTerritoryDistance);

                if (canTriggerFollow)
                {
                    state.State = MovementRuntimeState.FollowingPlayer;
                    state.FollowStartPosition = curPos; // 追従を開始した現在位置を記録
                    StepTowardTarget(state, playerPos, config.FollowStopDistance, deltaTime, isPlayerTarget: true);
                    return;
                }
            }
        }

        // 2. ホーム帰還中 (ReturningHome)
        if (state.State == MovementRuntimeState.ReturningHome)
        {
            Vector3 targetPos = config.Waypoints.Count > 0 ? config.Waypoints[state.CurrentWaypointIndex].Position : state.HomePosition;
            bool arrived = StepTowardTarget(state, targetPos, 0.2f, deltaTime, isPlayerTarget: false);
            if (arrived)
            {
                if (config.Mode == MovementMode.PatrolAndFollow && config.Waypoints.Count > 0)
                {
                    state.State = MovementRuntimeState.MovingToWaypoint;
                }
                else
                {
                    state.State = MovementRuntimeState.Idle;
                    // ホーム到着時、元の回転角へ戻す
                    state.CurrentRotation = state.HomeRotation;
                    actorManager.UpdateActorTransform(actor, state.CurrentPosition, state.CurrentRotation);
                }
            }
            return;
        }

        // 3. ウェイポイント巡回 (Patrol または PatrolAndFollow)
        if (config.Mode == MovementMode.Patrol || config.Mode == MovementMode.PatrolAndFollow)
        {
            if (config.Waypoints.Count == 0) return;

            if (state.State == MovementRuntimeState.WaitingAtWaypoint)
            {
                state.WaitTimer -= deltaTime;
                if (state.WaitTimer <= 0.0f)
                {
                    // 待機終了 -> 次のウェイポイントへ
                    AdvanceWaypoint(state);
                    state.State = MovementRuntimeState.MovingToWaypoint;
                }
                return;
            }

            if (state.State == MovementRuntimeState.MovingToWaypoint)
            {
                if (state.CurrentWaypointIndex >= config.Waypoints.Count)
                {
                    state.CurrentWaypointIndex = 0;
                }

                var currentWp = config.Waypoints[state.CurrentWaypointIndex];
                bool arrived = StepTowardTarget(state, currentWp.Position, 0.2f, deltaTime, isPlayerTarget: false);
                if (arrived)
                {
                    // 到着！
                    if (currentWp.WaitSeconds > 0.05f)
                    {
                        state.State = MovementRuntimeState.WaitingAtWaypoint;
                        state.WaitTimer = currentWp.WaitSeconds;

                        // 到着時モーション再生
                        if (currentWp.ActionTimelineId > 0 && animationService != null)
                        {
                            var wpMotion = new SceneActorMotionConfig
                            {
                                TimelineId = currentWp.ActionTimelineId,
                                TimelineKey = currentWp.ActionTimelineKey,
                                IsLoop = true,
                                Speed = 1.0f
                            };
                            animationService.ApplyMotion(actor, wpMotion, state.CurrentRotation);
                        }
                    }
                    else
                    {
                        // 待機時間なし -> 即座に次のウェイポイントへ
                        AdvanceWaypoint(state);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 指定座標の直下にある地面・階段・床の高さ（Y座標）をゲームエンジンのBGCollisionレイキャストで検出
    /// </summary>
    /// <param name="pos">判定対象の座標</param>
    /// <param name="groundY">検出された床・階段の上面Y座標</param>
    /// <param name="upOffset">頭上からのレイキャスト開始高さ（デフォルト: 2.5m）</param>
    /// <param name="maxDistance">真下への最大探索距離（デフォルト: 6.0m）</param>
    /// <returns>地面が検出されたかどうか</returns>
    public static bool TryGetGroundHeight(Vector3 pos, out float groundY, float upOffset = 2.5f, float maxDistance = 6.0f)
    {
        groundY = pos.Y;
        try
        {
            Vector3 origin = new Vector3(pos.X, pos.Y + upOffset, pos.Z);
            Vector3 direction = new Vector3(0, -1, 0);

            if (BGCollisionModule.RaycastMaterialFilter(origin, direction, out var hit, maxDistance))
            {
                groundY = hit.Point.Y;
                return true;
            }
        }
        catch
        {
            // ネイティブアクセス例外を安全に吸収
        }
        return false;
    }

    /// <summary>
    /// 目標座標に向かって向きを補間し、前進する
    /// </summary>
    /// <returns>目標に到達したかどうか</returns>
    private bool StepTowardTarget(ActiveMovementState state, Vector3 targetPos, float stopDistance, float deltaTime, bool isPlayerTarget)
    {
        var actor = state.Actor;
        Vector3 curPos = state.CurrentPosition;
        float curRot = state.CurrentRotation;

        Vector3 diff = targetPos - curPos;
        float horizDist = MathF.Sqrt(diff.X * diff.X + diff.Z * diff.Z);

        // 停止距離に到達
        if (horizDist <= stopDistance)
        {
            // 停止時も足元を階段・地面に接地（BGCollision 地面レイキャスト）
            if (TryGetGroundHeight(curPos, out float gY))
            {
                if (MathF.Abs(gY - curPos.Y) > 0.01f)
                {
                    state.CurrentPosition = new Vector3(curPos.X, gY, curPos.Z);
                }
            }

            // プレイヤーをターゲットにしている場合、立ち止まってプレイヤーの方を向く
            if (isPlayerTarget)
            {
                if (horizDist > 0.1f)
                {
                    float targetYaw = MathF.Atan2(diff.X, diff.Z);
                    state.CurrentRotation = RotateToward(curRot, targetYaw, state.Config.TurnSpeed * 2.0f, deltaTime);
                }
            }

            actorManager.UpdateActorTransform(actor, state.CurrentPosition, state.CurrentRotation);
            return true;
        }

        // 目標方向への目標Yaw角度
        float desiredYaw = MathF.Atan2(diff.X, diff.Z);
        float newRot = RotateToward(curRot, desiredYaw, state.Config.TurnSpeed, deltaTime);

        // 向きのズレが大きい場合は速度を落として旋回を優先
        float angleDiff = MathF.Abs(NormalizeAngle(desiredYaw - newRot));
        float speedFactor = MathF.Cos(MathF.Min(angleDiff, MathF.PI / 2.0f));
        if (speedFactor < 0.2f) speedFactor = 0.2f;

        float moveSpeed = state.Config.Speed * speedFactor;
        float moveStep = MathF.Min(moveSpeed * deltaTime, horizDist - (isPlayerTarget ? stopDistance : 0.0f));
        if (moveStep < 0.0f) moveStep = 0.0f;

        // 平面移動方向
        Vector3 moveDir = new Vector3(diff.X / horizDist, 0, diff.Z / horizDist);
        Vector3 newPos = curPos + moveDir * moveStep;

        // =========================================================================
        // Y座標（高度）の地形適応・階段／段差オートスナップ (BGCollision Raycast)
        // =========================================================================
        if (TryGetGroundHeight(newPos, out float groundY))
        {
            float heightDiff = groundY - curPos.Y;
            if (heightDiff > 0f)
            {
                // 階段の上り・段差の上昇:
                // 階段の1段〜2段（0.45m以内）は足元を瞬時に踏み面にスナップしてめり込みを100%防止！
                // それ以上の急激な段差であっても、毎秒最大 10m の垂直速度で素早く追従
                if (heightDiff <= 0.45f)
                {
                    newPos.Y = groundY;
                }
                else
                {
                    newPos.Y = curPos.Y + MathF.Min(heightDiff, 10.0f * deltaTime);
                }
            }
            else
            {
                // 階段の下り・段差の降下:
                // 階段の1段〜2段（0.45m以内）は瞬時にステップ面に接地させて宙浮きを防止！
                // 高所からの落差であっても、毎秒最大 10m の垂直降下速度で滑らかに接地
                if (heightDiff >= -0.45f)
                {
                    newPos.Y = groundY;
                }
                else
                {
                    newPos.Y = curPos.Y + MathF.Max(heightDiff, -10.0f * deltaTime);
                }
            }
        }
        else
        {
            // レイキャストがヒットしない特殊な空中・エリア境界等のフォールバック
            float yDiff = diff.Y;
            if (MathF.Abs(yDiff) > 0.01f)
            {
                if (yDiff > 0f)
                {
                    float maxUpStep = 5.0f * deltaTime;
                    newPos.Y = curPos.Y + MathF.Min(yDiff, maxUpStep);
                }
                else
                {
                    if (horizDist > 0.01f)
                    {
                        float yStep = (yDiff / horizDist) * moveStep;
                        if (horizDist <= stopDistance + 0.6f)
                        {
                            float maxDownStep = 5.0f * deltaTime;
                            newPos.Y = curPos.Y + MathF.Max(yDiff, -maxDownStep);
                        }
                        else
                        {
                            newPos.Y = curPos.Y + yStep;
                        }
                    }
                }
            }
        }

        state.CurrentPosition = newPos;
        state.CurrentRotation = newRot;

        // ゲーム内 Puppet に反映
        actorManager.UpdateActorTransform(actor, newPos, newRot);

        return false;
    }

    /// <summary>
    /// ウェイポイントのインデックスを進める (Loop, PingPong, Once)
    /// </summary>
    private void AdvanceWaypoint(ActiveMovementState state)
    {
        var config = state.Config;
        int count = config.Waypoints.Count;
        if (count <= 1) return;

        switch (config.LoopType)
        {
            case PatrolLoopType.Loop:
                state.CurrentWaypointIndex = (state.CurrentWaypointIndex + 1) % count;
                break;

            case PatrolLoopType.PingPong:
                state.CurrentWaypointIndex += state.PatrolDirection;
                if (state.CurrentWaypointIndex >= count)
                {
                    state.CurrentWaypointIndex = count - 2;
                    state.PatrolDirection = -1;
                }
                else if (state.CurrentWaypointIndex < 0)
                {
                    state.CurrentWaypointIndex = 1;
                    state.PatrolDirection = 1;
                }
                break;

            case PatrolLoopType.Once:
                if (state.CurrentWaypointIndex < count - 1)
                {
                    state.CurrentWaypointIndex++;
                }
                else
                {
                    state.State = MovementRuntimeState.Idle;
                }
                break;
        }
    }

    private static float RotateToward(float current, float target, float speedDegreesPerSec, float deltaTime)
    {
        float speedRad = speedDegreesPerSec * (MathF.PI / 180.0f) * deltaTime;
        float diff = NormalizeAngle(target - current);
        if (MathF.Abs(diff) <= speedRad) return target;
        return current + MathF.Sign(diff) * speedRad;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2.0f;
        while (angle < -MathF.PI) angle += MathF.PI * 2.0f;
        return angle;
    }

    public void Dispose()
    {
        this.framework.Update -= OnFrameworkUpdate;
        activeMovements.Clear();
    }
}
