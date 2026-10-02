using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using CharacterSpawn.Models;

namespace CharacterSpawn.Managers;

public unsafe class HeadTrackingManager
{
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;

    public HeadTrackingManager(IObjectTable objectTable, IPluginLog log)
    {
        this.objectTable = objectTable;
        this.log = log;
    }

    /// <summary>
    /// 毎フレーム呼び出され、視線追従が有効なアクターの視線をLocalPlayerに向ける
    /// </summary>
    public void UpdateTracking(IReadOnlyList<SpawnedActorData> actors)
    {
        var localPlayer = objectTable.Length > 0 ? objectTable[0] : null;
        if (localPlayer == null) return;

        var targetPos = localPlayer.Position;
        var targetEntityId = localPlayer.EntityId;

        foreach (var actor in actors)
        {
            if (!actor.IsSpawned || !actor.Animation.LookAtPlayer || actor.NativeAddress == 0)
                continue;

            var chara = (Character*)actor.NativeAddress;
            if (chara == null) continue;

            try
            {
                // ターゲットIDを自キャラにセットすることで視線追従を促す
                chara->SetTargetId(targetEntityId);

                // アクターからプレイヤーへの方位角を算出して向きを補正
                var diff = targetPos - actor.Transform.Position;
                if (diff.LengthSquared() > 0.01f)
                {
                    float angle = (float)Math.Atan2(diff.X, diff.Z);
                    chara->SetRotation(angle);
                }
            }
            catch
            {
                // Head tracking failed safely
            }
        }
    }
}
