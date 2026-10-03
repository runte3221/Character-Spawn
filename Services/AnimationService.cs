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

    private readonly ConcurrentDictionary<string, ActiveAnimationState> activeStates = new();
    private Func<Guid, SpawnedActorData?>? targetActorResolver;

    private class ActiveAnimationState
    {
        public string InstanceId { get; set; } = string.Empty;
        public nint NativeAddress { get; set; }
        public ushort TimelineId { get; set; }
        public bool IsLoop { get; set; }
        public float Speed { get; set; } = 1.0f;
        public ushort FacialTimelineId { get; set; }
        public bool LookAtPlayer { get; set; }
        public bool LookAtCustomSpawn { get; set; }
        public Guid LookAtTargetPlacementId { get; set; } = Guid.Empty;
        public float BodyTurnAngleLimit { get; set; } = 0.0f; // 0=顔と視線のみ, >0=指定角まで体も向く
        public float LookAtMaxDistance { get; set; } = 8.0f;
        public float OriginalRotation { get; set; }
        public int TicksSinceApply { get; set; }
        public bool IsInitialSpawn { get; set; } = false;
    }

    public void SetTargetActorResolver(Func<Guid, SpawnedActorData?> resolver)
    {
        targetActorResolver = resolver;
    }

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
    public void ApplyMotion(SpawnedActorData spawned, SceneActorMotionConfig config, float defaultRotation, bool isInitialSpawn = false)
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
                LookAtCustomSpawn = config.LookAtCustomSpawn,
                LookAtTargetPlacementId = config.LookAtTargetPlacementId,
                BodyTurnAngleLimit = config.BodyTurnAngleLimit,
                LookAtMaxDistance = config.LookAtMaxDistance > 0.1f ? config.LookAtMaxDistance : 8.0f,
                OriginalRotation = defaultRotation,
                TicksSinceApply = 0,
                IsInitialSpawn = isInitialSpawn
            };

            // 1. 基本モーションの再生 (スロット0: Base)
            if (config.TimelineId > 0)
            {
                chara->SetMode(CharacterModes.Normal, 0);

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

                // 即時反映: スロット0を停止しTimelineSequencerで直接再生 (HDM Loop / PlayAction 準拠)
                chara->StopTimeline(0);
                chara->Timeline.TimelineSequencer.PlayTimeline(config.TimelineId);
            }
            else
            {
                // モーションなし (通常待機に戻す)
                chara->SetMode(CharacterModes.Normal, 0);
                chara->Timeline.BaseOverride = 0;
                chara->Timeline.OverallSpeed = 1.0f;
                chara->Timeline.TimelineSequencer.SetSlotSpeed(0, 1.0f);
                chara->StopTimeline(0);
                chara->Timeline.TimelineSequencer.PlayTimeline(1); // 1 = Default Idle
            }

            // 2. 表情の再生 ＆ フリーズ固定 (Brio DFC アーキテクチャ)
            if (config.FacialTimelineId > 0)
            {
                chara->Timeline.TimelineSequencer.PlayTimeline(config.FacialTimelineId);
                chara->Timeline.TimelineSequencer.SetSlotSpeed(2, 0.0f); // 表情スロットの速度を0にして固定！
            }
            else
            {
                chara->Timeline.TimelineSequencer.SetSlotSpeed(2, 1.0f);
                chara->Timeline.TimelineSequencer.PlayTimeline(604); // 表情：素顔
            }

            // 3. 視線追従のトグル制御 (どちらも無効な時は即時解除)
            if (!config.LookAtPlayer && !config.LookAtCustomSpawn)
            {
                chara->SetTargetId(0);
                chara->SetRotation(defaultRotation);
            }

            activeStates[spawned.InstanceId] = state;
            logManager?.Info($"ApplyMotion: '{spawned.DisplayName}' -> Timeline: {config.TimelineId}, Loop: {config.IsLoop}, Speed: {state.Speed:F2}x, Facial: {config.FacialTimelineId}, LookAt: {config.LookAtPlayer}, Dist: {state.LookAtMaxDistance}m, BodyLimit: {config.BodyTurnAngleLimit}°");
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
            chara->SetTargetId(0);
            chara->SetMode(CharacterModes.Normal, 0);
            chara->Timeline.BaseOverride = 0;
            chara->Timeline.OverallSpeed = 1.0f;
            chara->Timeline.TimelineSequencer.SetSlotSpeed(0, 1.0f);
            chara->Timeline.TimelineSequencer.SetSlotSpeed(2, 1.0f);
            chara->Timeline.TimelineSequencer.PlayTimeline(604); // 表情：素顔
            chara->StopTimeline(0);
            chara->PlayTimeline(1, 0); // Default Idle
        }
        catch (Exception ex)
        {
            log.Error($"Failed to stop motion on {spawned.DisplayName}: {ex.Message}");
        }
    }

    /// <summary>
    /// 毎フレームのループ監視、速度維持、表情固定、視線追従更新
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

                // A. 初期スポーン直後の非同期モデルロード(Glamourer/Penumbra)完了を待って、30フレーム(約0.5秒)後に再同期
                if (state.IsInitialSpawn && state.TicksSinceApply == 30)
                {
                    if (state.TimelineId > 0)
                    {
                        chara->Timeline.TimelineSequencer.PlayTimeline(state.TimelineId);
                    }
                    if (state.FacialTimelineId > 0)
                    {
                        chara->Timeline.TimelineSequencer.PlayTimeline(state.FacialTimelineId);
                        chara->Timeline.TimelineSequencer.SetSlotSpeed(2, 0.0f);
                    }
                }

                // B. モーション速度維持
                if (state.Speed > 0.01f && MathF.Abs(state.Speed - 1.0f) > 0.01f)
                {
                    chara->Timeline.OverallSpeed = state.Speed;
                    chara->Timeline.TimelineSequencer.SetSlotSpeed(0, state.Speed);
                }

                // C. 表情スロットの速度0（固定）を維持
                if (state.FacialTimelineId > 0)
                {
                    chara->Timeline.TimelineSequencer.SetSlotSpeed(2, 0.0f);
                }

                // D. ループ維持 (TickReplays)
                if (state.IsLoop && state.TimelineId > 0 && state.TicksSinceApply > 15)
                {
                    if (chara->Timeline.BaseOverride != state.TimelineId)
                    {
                        chara->Timeline.BaseOverride = state.TimelineId;
                    }
                }

                // E. 視線追従 (LookAt Player または LookAt Custom Spawn) ＆ 範囲内外制御
                Vector3 targetPos = Vector3.Zero;
                uint targetEntityId = 0;
                bool hasTarget = false;

                if (state.LookAtPlayer && localPlayer != null && myEntityId != 0)
                {
                    targetPos = myPos;
                    targetEntityId = myEntityId;
                    hasTarget = true;
                }
                else if (state.LookAtCustomSpawn && targetActorResolver != null && state.LookAtTargetPlacementId != Guid.Empty)
                {
                    var targetActor = targetActorResolver(state.LookAtTargetPlacementId);
                    if (targetActor != null && targetActor.NativeAddress != 0)
                    {
                        var tChara = (Character*)targetActor.NativeAddress;
                        targetPos = tChara->Position;
                        targetEntityId = tChara->EntityId;
                        hasTarget = true;
                    }
                }

                if (hasTarget && targetEntityId != 0)
                {
                    float dx = targetPos.X - chara->Position.X;
                    float dz = targetPos.Z - chara->Position.Z;
                    float distSq = dx * dx + dz * dz;

                    if (distSq <= state.LookAtMaxDistance * state.LookAtMaxDistance && distSq > 0.04f)
                    {
                        // 1. 範囲内：首・目線は常にネイティブで対象を追従！
                        chara->SetTargetId(targetEntityId);

                        // 2. 体幹（全身）の回転制御
                        if (state.BodyTurnAngleLimit <= 0.01f)
                        {
                            // 0度の場合：体は一切回さず、初期向きを厳格に維持（顔と目線のみ追従！）
                            float currentRot = chara->Rotation;
                            float angleDiff = NormalizeAngle(state.OriginalRotation - currentRot);
                            if (MathF.Abs(angleDiff) > 0.01f)
                            {
                                chara->SetRotation(currentRot + angleDiff * 0.15f);
                            }
                        }
                        else
                        {
                            // 角度制限がある場合：初期向きからの差分を制限角内にクランプ
                            float targetRot = MathF.Atan2(dx, dz);
                            float desiredDiff = NormalizeAngle(targetRot - state.OriginalRotation);
                            float maxRad = state.BodyTurnAngleLimit * (MathF.PI / 180.0f);
                            float clampedDiff = Math.Clamp(desiredDiff, -maxRad, maxRad);
                            float targetClampedRot = NormalizeAngle(state.OriginalRotation + clampedDiff);

                            float currentRot = chara->Rotation;
                            float turnDiff = NormalizeAngle(targetClampedRot - currentRot);
                            float smoothedRot = currentRot + turnDiff * 0.15f;
                            chara->SetRotation(smoothedRot);
                        }
                    }
                    else
                    {
                        // 範囲外に出た場合：ターゲットを0にして視線・首追従を即時解除！
                        chara->SetTargetId(0);

                        // 体も元の向きに戻す
                        float currentRot = chara->Rotation;
                        float angleDiff = NormalizeAngle(state.OriginalRotation - currentRot);
                        if (MathF.Abs(angleDiff) > 0.01f)
                        {
                            chara->SetRotation(currentRot + angleDiff * 0.1f);
                        }
                    }
                }
                else if (state.LookAtPlayer || state.LookAtCustomSpawn)
                {
                    // ターゲットが見つからない場合：解除
                    chara->SetTargetId(0);
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
