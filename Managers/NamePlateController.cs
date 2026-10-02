using Dalamud.Plugin.Services;
using Dalamud.Game.Gui.NamePlate;
using CharacterSpawn.Models;

namespace CharacterSpawn.Managers;

public unsafe class NamePlateController : IDisposable
{
    private readonly INamePlateGui? namePlateGui;
    private readonly IPluginLog log;
    private readonly Func<IReadOnlyList<SpawnedActorData>> getActors;

    public NamePlateController(INamePlateGui? namePlateGui, IPluginLog log, Func<IReadOnlyList<SpawnedActorData>> getActors)
    {
        this.namePlateGui = namePlateGui;
        this.log = log;
        this.getActors = getActors;

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

        foreach (var handler in handlers)
        {
            var go = handler.GameObject;
            if (go == null) continue;

            var charaAddr = (nint)go.Address;
            var match = spawned.FirstOrDefault(a => a.NativeAddress == charaAddr);
            if (match == null) continue;

            // 非表示設定の場合
            if (!match.NamePlate.Show)
            {
                handler.RemoveName();
            }
            // カスタム名が指定されている場合
            else if (!string.IsNullOrWhiteSpace(match.NamePlate.CustomName))
            {
                handler.Name = match.NamePlate.CustomName;
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
