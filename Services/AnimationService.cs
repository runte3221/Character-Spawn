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
/// HDM & Brio アーキテクチャ準拠
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
        public string InstanceId { get; set; } = string.Empty;
        public nint NativeAddress { get; set; }
        public ushort TimelineId { get; set; }
        public bool IsLoop { get; set; }
        public float Speed { get; set; } = 1.0f;
        public ushort FacialTimelineId { get; set; }
        public bool LookAtPlayer { get; set; }
        public float LookAtMaxDistance { get; set; } = 8.0f;
        public float OriginalRotation { get; set; }
        public int TicksSinceApply { get; set; }
    }

    private readonly ConcurrentDictionary<string, ActiveAnimationState> activeStates = new();

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
                InstanceId = spawned.InstanceId,
                NativeAddress = spawned.NativeAddress,
                TimelineId = config.TimelineId,
                IsLoop = config.IsLoop,
                Speed = config.Speed > 0.01f ? config.Speed : 1.0f,
                FacialTimelineId = config.FacialTimelineId,
                LookAtPlayer = config.LookAtPlayer,
                OriginalRotation = defaultRotation,
                TicksSinceApply = 0
            };

            // 1. 基本モーションの再生 (スロット0: Base)
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

                // 速度設定 (OverallSpeed と SlotSpeed の両方に適用)
                chara->Timeline.OverallSpeed = state.Speed;
                chara->Timeline.TimelineSequencer.SetSlotSpeed(0, state.Speed);

                // スロット0でモーション再生
                chara->PlayTimeline(config.TimelineId, 0);
            }
            else
            {
                // モーションなし (通常待機)
                chara->Timeline.BaseOverride = 0;
                chara->Timeline.OverallSpeed = 1.0f;
                chara->Timeline.TimelineSequencer.SetSlotSpeed(0, 1.0f);
                chara->StopTimeline(0);
                chara->PlayTimeline(1, 0); // 1 = Default Idle
            }

            // 2. 表情の再生 (スロット2: Facial / ActionTimelineSlots.Facial)
            if (config.FacialTimelineId > 0)
            {
                chara->PlayTimeline(config.FacialTimelineId, 2);
            }
            else
            {
                chara->StopTimeline(2);
            }

            activeStates[spawned.InstanceId] = state;
            logManager?.Info($"ApplyMotion: '{spawned.DisplayName}' -> Timeline: {config.TimelineId}, Loop: {config.IsLoop}, Speed: {state.Speed:F2}x, Facial: {config.FacialTimelineId}, LookAt: {config.LookAtPlayer}");
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
            chara->Timeline.TimelineSequencer.SetSlotSpeed(0, 1.0f);
            chara->StopTimeline(0);
            chara->StopTimeline(2); // 表情スロットも停止
            chara->PlayTimeline(1, 0); // Default Idle
        }
        catch (Exception ex)
        {
            log.Error($"Failed to stop motion on {spawned.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// 毎フレームのループ監視、速度維持、視線追従更新
    /// </summary>
    private void OnFrameworkUpdate(IFramework _)
    {
        if (activeStates.IsEmpty) return;

        var localPlayer = objectTable.Length > 0 ? objectTable[0] : null;
        var myPos = localPlayer != null ? localPlayer.Position : Vector3.Zero;
        var myEntityId = localPlayer != null ? localPlayer.EntityId : 0;

        foreach (var (instanceId, state) in activeStates)
        {
            try
            {
                if (state.NativeAddress == 0) continue;

                var chara = (Character*)state.NativeAddress;
                if (chara == null || chara->GameObject.DrawObject == null) continue;

                state.TicksSinceApply++;

                // 初期スポーン直後の非同期モデルロード(Glamourer/Penumbra)完了を待って、30フレーム(約0.5秒)後に再同期
                if (state.TicksSinceApply == 30)
                {
                    if (state.TimelineId > 0)
                    {
                        chara->PlayTimeline(state.TimelineId, 0);
                    }
                    if (state.FacialTimelineId > 0)
                    {
                        chara->PlayTimeline(state.FacialTimelineId, 2);
                    }
                }

                // A. 速度維持 (ゲーム内部の更新で上書きされるのを防ぐ)
                if (state.Speed > 0.01f && MathF.Abs(state.Speed - 1.0f) > 0.01f)
                {
                    chara->Timeline.OverallSpeed = state.Speed;
                    chara->Timeline.TimelineSequencer.SetSlotSpeed(0, state.Speed);
                }

                // B. ループ維持 (TickReplays)
                if (state.IsLoop && state.TimelineId > 0 && state.TicksSinceApply > 15)
                {
                    if (chara->Timeline.BaseOverride != state.TimelineId)
                    {
                        chara->Timeline.BaseOverride = state.TimelineId;
                    }
                }

                // C. 視線追従 (LookAt Player)
                if (state.LookAtPlayer && localPlayer != null && myEntityId != 0)
                {
                    var actorPos = chara->Position;
                    var diff = myPos - actorPos;
                    var distSq = diff.LengthSquared();

                    if (distSq <= state.LookAtMaxDistance * state.LookAtMaxDistance && distSq > 0.04f)
                    {
                        // 1. ゲームネイティブの視線・首の追従
                        chara->SetTargetId(myEntityId);

                        // 2. 自キャラの方向へ滑らかに向き（体幹）を補正
                        float targetRot = MathF.Atan2(diff.X, diff.Z);
                        float currentRot = chara->Rotation;
                        float angleDiff = NormalizeAngle(targetRot - currentRot);
                        float smoothedRot = currentRot + angleDiff * 0.15f;

                        chara->SetRotation(smoothedRot);
                    }
                    else
                    {
                        // 範囲外なら元の向きに戻す
                        float currentRot = chara->Rotation;
                        float angleDiff = NormalizeAngle(state.OriginalRotation - currentRot);
                        if (MathF.Abs(angleDiff) > 0.01f)
                        {
                            chara->SetRotation(currentRot + angleDiff * 0.1f);
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
