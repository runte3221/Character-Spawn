using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Services;

public class PenumbraIpc
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<(int, int)>? getApiVersions;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollections;
    private readonly ICallGateSubscriber<string, int, int>? setCollectionForObject;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObject;

    public bool IsAvailable { get; private set; }

    public PenumbraIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;

        try
        {
            getApiVersions = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion");
            getCollections = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections");
            setCollectionForObject = pi.GetIpcSubscriber<string, int, int>("Penumbra.SetCollectionForObject");
            redrawObject = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject");

            CheckAvailability();
        }
        catch (Exception ex)
        {
            log.Warning($"Penumbra IPC subscription failed: {ex.Message}");
            IsAvailable = false;
        }
    }

    public bool CheckAvailability()
    {
        try
        {
            if (getApiVersions == null) return false;
            var (major, minor) = getApiVersions.InvokeFunc();
            IsAvailable = major >= 4;
            return IsAvailable;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }

    public Dictionary<Guid, string> GetCollections()
    {
        if (!IsAvailable || getCollections == null) return new();

        try
        {
            return getCollections.InvokeFunc();
        }
        catch
        {
            return new();
        }
    }

    public bool SetCollectionForActor(string collectionName, int actorIndex)
    {
        if (!IsAvailable || setCollectionForObject == null) return false;

        try
        {
            var result = setCollectionForObject.InvokeFunc(collectionName, actorIndex);
            return result == 0;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to set Penumbra collection: {ex.Message}");
            return false;
        }
    }

    public bool Redraw(int actorIndex)
    {
        if (!IsAvailable || redrawObject == null) return false;

        try
        {
            redrawObject.InvokeAction(actorIndex, 0);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to trigger Penumbra Redraw for actor {actorIndex}: {ex.Message}");
            return false;
        }
    }
}
