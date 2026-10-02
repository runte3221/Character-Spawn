using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterSpawn.Models;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Managers;

public unsafe class TimelineManager
{
    private readonly IPluginLog log;

    public TimelineManager(IPluginLog log)
    {
        this.log = log;
    }

    /// <summary>
    /// アクターにActionTimeline（モーション）およびループを設定
    /// </summary>
    public void ApplyTimeline(Character* chara, AnimationSettings anim)
    {
        if (chara == null || anim.TimelineId == 0) return;

        try
        {
            // ActionTimelineの再生
            chara->Mode = Character.CharacterModes.Normal;
            
            if (anim.IsLoop)
            {
                // BaseTimeline（通常待機モーション）を差し替えることで、永続ループを実現
                chara->Timeline.BaseTimeline = anim.TimelineId;
            }
            
            // アニメーション再生トリガー
            chara->Timeline.SetTimeline(anim.TimelineId);

            // 表情（Facial Expression）の適用
            if (anim.FacialExpressionId > 0)
            {
                chara->Timeline.FacialTimeline = anim.FacialExpressionId;
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply timeline {anim.TimelineId}: {ex.Message}");
        }
    }

    /// <summary>
    /// 表情のみを個別に更新
    /// </summary>
    public void ApplyFacialExpression(Character* chara, ushort facialId)
    {
        if (chara == null) return;

        try
        {
            chara->Timeline.FacialTimeline = facialId;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply facial expression {facialId}: {ex.Message}");
        }
    }

    /// <summary>
    /// アニメーションを通常待機状態にリセット
    /// </summary>
    public void ResetTimeline(Character* chara)
    {
        if (chara == null) return;

        try
        {
            chara->Timeline.BaseTimeline = 1; // 1 = Default Idle
            chara->Timeline.SetTimeline(1);
            chara->Timeline.FacialTimeline = 0;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to reset timeline: {ex.Message}");
        }
    }
}
