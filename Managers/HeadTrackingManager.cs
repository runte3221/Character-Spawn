using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using CharacterSpawn.Models;

namespace CharacterSpawn.Managers;

public unsafe class HeadTrackingManager
{
    private readonly IClientState clientState;
    private readonly IPluginLog log;

    public HeadTrackingManager(IClientState clientState, IPluginLog log)
    {
        this.clientState = clientState;
        this.log = log;
    }

    /// <summary>
    /// 毎フレーム呼び出され、視線追従が有効なアクターの視線をLocalPlayerに向ける
    /// </summary>
    public void UpdateTracking(IReadOnlyList<SpawnedActorData> actors)
    {
        var localPlayer = clientState.LocalPlayer;
        if (localPlayer == null) return;

        var targetPos = localPlayer.Position;

        foreach (var actor in actors)
        {
            if (!actor.IsSpawned || !actor.Animation.LookAtPlayer || actor.NativeAddress == 0)
                continue;

            var chara = (Character*)actor.NativeAddress;
            if (chara == null) continue;

            try
            {
                // キャラクターの頭部・視線IKターゲットをプレイヤー位置に更新
                var ffxivTarget = new FFXIVClientStructs.FFXIV.Common.Math.Vector3(targetPos.X, targetPos.Y + 1.6f, targetPos.Z);
                
                // Set head tracking target position
                chara->LookAtPosition(ffxivTarget);
            }
            catch
            {
                // Head tracking failed safely
            }
        }
    }
}
