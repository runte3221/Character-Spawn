using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
    /// アクターにActionTimeline（モーション）を設定
    /// </summary>
    public void ApplyTimeline(Character* chara, AnimationSettings anim)
    {
        if (chara == null || anim.TimelineId == 0) return;

        try
        {
            chara->Mode = Character.CharacterModes.Normal;
            chara->PlayTimeline(anim.TimelineId);

            if (anim.FacialExpressionId > 0)
            {
                chara->PlayTimeline(anim.FacialExpressionId);
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
        if (chara == null || facialId == 0) return;

        try
        {
            chara->PlayTimeline(facialId);
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
            chara->StopTimeline();
            chara->PlayTimeline(1); // 1 = Default Idle
        }
        catch (Exception ex)
        {
            log.Error($"Failed to reset timeline: {ex.Message}");
        }
    }
}
