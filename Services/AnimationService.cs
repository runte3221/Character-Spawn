using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using CharacterSpawn.Models;
using CharacterSpawn.Managers;

namespace CharacterSpawn.Services;

/// <summary>
/// アクターのActionTimelineモーション再生、シームレスループ、再生速度、表情固定、視線追従を制御するサービス
/// HDM (AnimationService) アーキテクチャ準拠
/// </summary>
public unsafe class AnimationService : IDisposable
{
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly GameDataService gameDataService;
    private readonly IPluginLog log;
    private readonly LogManager? logManager;

    private class ActiveAnimationState
    {
        public ulong GameObjectId { get; set; }
        public ushort TimelineId { get; set; }
        public bool IsLoop { get; set; }
        public float Speed { get; set; } = 1.0f;
        public ushort FacialTimelineId { get; set; }
        public bool LookAtPlayer { get; set; }
        public float LookAtMaxDistance { get; set; } = 8.0f;
        public float OriginalRotation { get; set; }
        public int TicksSinceApply { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, ActiveAnimationState> activeStates = new();

    public AnimationService(
        IFramework framework,
        IObjectTable objectTable,
        GameDataService gameDataService,
        IPluginLog log,
        LogManager? logManager = null)
    {
        this.framework = framework;
        this.objectTable = objectTable;
        this.gameDataService = gameDataService;
        this.log = log;
        this.logManager = logManager;

        this.framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    /// 指定の配置アクターにモーション・表情・視線設定を適用
    /// </summary>
    public void ApplyMotion(SpawnedActorData spawned, SceneActorMotionConfig config, float defaultRotation)
    {
        if (spawned == null || spawned.NativeAddress == 0) return;

        var chara = (Character*)spawned.NativeAddress;
        if (chara == null) return;

        try
        {
            var state = new ActiveAnimationState
            {
                GameObjectId = spawned.GameObjectId,
                TimelineId = config.TimelineId,
                IsLoop = config.IsLoop,
                Speed = config.Speed > 0.01f ? config.Speed : 1.0f,
                FacialTimelineId = config.FacialTimelineId,
                LookAtPlayer = config.LookAtPlayer,
                OriginalRotation = defaultRotation,
                TicksSinceApply = 0
            };

            // 1. 基本モーションの再生
            if (config.TimelineId > 0)
            {
                // HDM 準拠: BaseOverride を設定して基本待機モーションとして定着させる
                if (config.IsLoop)
                {
                    chara->Timeline.BaseOverride = config.TimelineId;
                }
                else
                {
                    chara->Timeline.BaseOverride = 0;
                }

                // 速度設定
                chara->Timeline.OverallSpeed = state.Speed;

                // スロット0でモーション再生
                chara->PlayTimeline(config.TimelineId, 0);
            }
            else
            {
                // モーションなし (通常待機)
                chara->Timeline.BaseOverride = 0;
                chara->Timeline.OverallSpeed = 1.0f;
                chara->StopTimeline(0);
                chara->PlayTimeline(1, 0); // 1 = Default Idle
            }

            // 2. 表情の再生 (ActionTimeline の fac_ は表情スロットに重なる)
            if (config.FacialTimelineId > 0)
            {
                chara->PlayTimeline(config.FacialTimelineId, 0);
            }

            activeStates[spawned.InstanceId] = state;
            logManager?.Info($"ApplyMotion: '{spawned.DisplayName}' -> Timeline: {config.TimelineId}, Loop: {config.IsLoop}, Facial: {config.FacialTimelineId}, LookAt: {config.LookAtPlayer}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply motion to {spawned.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// モーションを安全に停止し通常待機状態に復帰
    /// </summary>
    public void StopMotion(SpawnedActorData spawned)
    {
        if (spawned == null || spawned.NativeAddress == 0) return;

        activeStates.TryRemove(spawned.InstanceId, out _);

        var chara = (Character*)spawned.NativeAddress;
        if (chara == null) return;

        try
        {
            chara->Timeline.BaseOverride = 0;
            chara->Timeline.OverallSpeed = 1.0f;
            chara->StopTimeline(0);
            chara->PlayTimeline(1, 0); // Default Idle
        }
        catch (Exception ex)
        {
            log.Error($"Failed to stop motion on {spawned.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// 毎フレームのループ監視と視線追従更新
    /// </summary>
    private void OnFrameworkUpdate(IFramework _)
    {
        if (activeStates.IsEmpty) return;

        var localPlayer = objectTable.Length > 0 ? objectTable[0] as ICharacter : null;
        var myPos = localPlayer != null ? localPlayer.Position : Vector3.Zero;

        foreach (var (instanceId, state) in activeStates)
        {
            try
            {
                // GameObject を検索
                var obj = objectTable.SearchById(state.GameObjectId);
                if (obj == null || obj.Address == 0) continue;

                var chara = (Character*)obj.Address;
                if (chara == null) continue;

                state.TicksSinceApply++;

                // A. ループ維持 (TickReplays)
                // 30フレーム(約0.5秒)ごとにチェックし、もしBaseOverrideがリセットされていたり
                // 再生が終了している場合は再トリガー
                if (state.IsLoop && state.TimelineId > 0 && state.TicksSinceApply > 30)
                {
                    if (chara->Timeline.BaseOverride != state.TimelineId)
                    {
                        chara->Timeline.BaseOverride = state.TimelineId;
                    }

                    // 速度維持
                    if (MathF.Abs(chara->Timeline.OverallSpeed - state.Speed) > 0.05f)
                    {
                        chara->Timeline.OverallSpeed = state.Speed;
                    }
                }

                // B. 視線追従 (LookAt Player)
                if (state.LookAtPlayer && localPlayer != null)
                {
                    var actorPos = obj.Position;
                    var distSq = Vector3.DistanceSquared(myPos, actorPos);

                    if (distSq <= state.LookAtMaxDistance * state.LookAtMaxDistance && distSq > 0.1f)
                    {
                        // 自キャラの方を向く角度を計算 (Yaw)
                        var dir = myPos - actorPos;
                        float targetRot = MathF.Atan2(dir.X, dir.Z);

                        // 滑らかに向きを変更
                        float currentRot = obj.Rotation;
                        float diff = NormalizeAngle(targetRot - currentRot);
                        float smoothedRot = currentRot + diff * 0.15f;

                        chara->SetTargetId(localPlayer.EntityId);
                        chara->SetRotation(smoothedRot);
                    }
                    else
                    {
                        // 範囲外なら元の向きに戻す
                        float currentRot = obj.Rotation;
                        float diff = NormalizeAngle(state.OriginalRotation - currentRot);
                        if (MathF.Abs(diff) > 0.01f)
                        {
                            chara->SetRotation(currentRot + diff * 0.1f);
                        }
                    }
                }
            }
            catch
            {
                // 安全のため例外は握りつぶし次回フレームで再試行
            }
        }
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.PI * 2;
        while (angle < -MathF.PI) angle += MathF.PI * 2;
        return angle;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        activeStates.Clear();
    }
}
