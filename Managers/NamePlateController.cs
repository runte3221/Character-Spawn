using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.NamePlate;
using CharacterSpawn.Models;

namespace CharacterSpawn.Managers;

public unsafe class NamePlateController : IDisposable
{
    private readonly INamePlateGui? namePlateGui;
    private readonly IPluginLog log;
    private readonly Func<IReadOnlyList<SpawnedActorData>> getActors;
    private readonly Func<bool>? isEditOpenFunc;
    private readonly Func<SpawnedActorData, SceneActorPlacement?>? getPlacementFunc;

    public NamePlateController(
        INamePlateGui? namePlateGui,
        IPluginLog log,
        Func<IReadOnlyList<SpawnedActorData>> getActors,
        Func<bool>? isEditOpenFunc = null,
        Func<SpawnedActorData, SceneActorPlacement?>? getPlacementFunc = null)
    {
        this.namePlateGui = namePlateGui;
        this.log = log;
        this.getActors = getActors;
        this.isEditOpenFunc = isEditOpenFunc;
        this.getPlacementFunc = getPlacementFunc;

        if (namePlateGui != null)
        {
            try
            {
                namePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
            }
            catch (Exception ex)
            {
                log.Warning($"NamePlateGui event subscription failed: {ex.Message}");
            }
        }
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        var spawned = getActors();
        if (spawned.Count == 0) return;

        bool isEditOpen = isEditOpenFunc != null && isEditOpenFunc();

        foreach (var handler in handlers)
        {
            var go = handler.GameObject;
            if (go == null) continue;

            var charaAddr = (nint)go.Address;
            var match = spawned.FirstOrDefault(a => a.NativeAddress == charaAddr);
            if (match == null) continue;

            var placement = getPlacementFunc != null ? getPlacementFunc(match) : null;

            // 表示する名前（カスタム表示名があればそれ、なければテンプレート名や表示名）
            string displayName = !string.IsNullOrWhiteSpace(placement?.CustomDisplayName)
                ? placement.CustomDisplayName
                : (!string.IsNullOrWhiteSpace(match.NamePlate.CustomName)
                    ? match.NamePlate.CustomName
                    : match.DisplayName);

            if (isEditOpen)
            {
                // 1. Edit ウィンドウ表示中:
                // 編集・位置合わせ時にどのアクターか識別できるよう、全カスタムスポーンの名前を表示
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    handler.Name = displayName;
                }
            }
            else
            {
                // 2. Edit ウィンドウ非表示時 (通常プレイ・鑑賞時):
                // 「カスタムネームを表示」に設定しているスポーンだけ名前を表示
                bool shouldShow = placement != null
                    ? placement.NamePlate.ShowCustomName
                    : match.NamePlate.Show;

                if (shouldShow && !string.IsNullOrWhiteSpace(displayName))
                {
                    handler.Name = displayName;
                }
                else
                {
                    // 表示設定になっていないスポーンは名前を非表示
                    handler.RemoveName();
                }
            }
        }
    }

    public void Dispose()
    {
        if (namePlateGui != null)
        {
            try
            {
                namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
            }
            catch
            {
            }
        }
    }
}
