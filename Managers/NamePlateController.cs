using Dalamud.Plugin.Services;
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

    private void OnNamePlateUpdate(INamePlateUpdateHandlerArgs args)
    {
        var spawned = getActors();
        if (spawned.Count == 0) return;

        var chara = args.Actor;
        if (chara == null) return;

        var charaAddr = (nint)chara.Address;

        var match = spawned.FirstOrDefault(a => a.NativeAddress == charaAddr);
        if (match == null) return;

        // 非表示設定の場合
        if (!match.NamePlate.Show)
        {
            args.IsVisible = false;
            return;
        }

        // カスタム名が指定されている場合
        if (!string.IsNullOrWhiteSpace(match.NamePlate.CustomName))
        {
            args.Name = match.NamePlate.CustomName;
        }
    }

    public void Dispose()
    {
        if (namePlateGui != null)
        {
            namePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        }
    }
}
