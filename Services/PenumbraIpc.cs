using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Services;

public class PenumbraIpc
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;

    // V5 IPC Subscribers
    private readonly ICallGateSubscriber<(int, int)>? apiVersionV5;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsV5;
    private readonly ICallGateSubscriber<string, int, int, object?>? setCollectionForObjectV5;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectV5;

    // Fallback Subscribers
    private readonly ICallGateSubscriber<(int, int)>? apiVersionLegacy;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsLegacy;
    private readonly ICallGateSubscriber<string, int, int>? setCollectionForObjectLegacy;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectLegacy;

    private bool isAvailable = false;
    private DateTime lastAvailabilityCheck = DateTime.MinValue;

    public bool IsAvailable
    {
        get
        {
            if ((DateTime.UtcNow - lastAvailabilityCheck).TotalSeconds > 1.5)
            {
                CheckAvailability();
            }
            return isAvailable;
        }
    }

    public PenumbraIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;

        try
        {
            apiVersionV5 = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
            getCollectionsV5 = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections.V5");
            setCollectionForObjectV5 = pi.GetIpcSubscriber<string, int, int, object?>("Penumbra.SetCollectionForObject.V5");
            redrawObjectV5 = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject.V5");

            apiVersionLegacy = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion");
            getCollectionsLegacy = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections");
            setCollectionForObjectLegacy = pi.GetIpcSubscriber<string, int, int>("Penumbra.SetCollectionForObject");
            redrawObjectLegacy = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject");

            CheckAvailability();
        }
        catch (Exception ex)
        {
            log.Warning($"Penumbra IPC subscription init failed: {ex.Message}");
            isAvailable = false;
        }
    }

    public bool CheckAvailability()
    {
        lastAvailabilityCheck = DateTime.UtcNow;

        // Try V5 first
        if (apiVersionV5 != null)
        {
            try
            {
                var (major, minor) = apiVersionV5.InvokeFunc();
                isAvailable = major >= 4;
                if (isAvailable) return true;
            }
            catch
            {
                // V5 not available
            }
        }

        // Try Legacy
        if (apiVersionLegacy != null)
        {
            try
            {
                var (major, minor) = apiVersionLegacy.InvokeFunc();
                isAvailable = major >= 4;
                return isAvailable;
            }
            catch
            {
                // Legacy not available
            }
        }

        isAvailable = false;
        return false;
    }

    public Dictionary<Guid, string> GetCollections()
    {
        if (!IsAvailable) return new();

        // 1. Try V5
        if (getCollectionsV5 != null)
        {
            try
            {
                var list = getCollectionsV5.InvokeFunc();
                if (list != null) return list;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra V5 GetCollections failed: {ex.Message}");
            }
        }

        // 2. Try Legacy
        if (getCollectionsLegacy != null)
        {
            try
            {
                var list = getCollectionsLegacy.InvokeFunc();
                if (list != null) return list;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra Legacy GetCollections failed: {ex.Message}");
            }
        }

        return new();
    }

    public bool SetCollectionForActor(string collectionName, int actorIndex)
    {
        if (!IsAvailable) return false;

        // 1. Try V5
        if (setCollectionForObjectV5 != null)
        {
            try
            {
                setCollectionForObjectV5.InvokeAction(collectionName, actorIndex, 0);
                return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra V5 SetCollectionForObject failed: {ex.Message}");
            }
        }

        // 2. Try Legacy
        if (setCollectionForObjectLegacy != null)
        {
            try
            {
                var result = setCollectionForObjectLegacy.InvokeFunc(collectionName, actorIndex);
                return result == 0;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject fallback failed: {ex.Message}");
            }
        }

        return false;
    }

    public bool Redraw(int actorIndex)
    {
        if (!IsAvailable) return false;

        // 1. Try V5
        if (redrawObjectV5 != null)
        {
            try
            {
                redrawObjectV5.InvokeAction(actorIndex, 0);
                return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra V5 RedrawObject failed: {ex.Message}");
            }
        }

        // 2. Try Legacy
        if (redrawObjectLegacy != null)
        {
            try
            {
                redrawObjectLegacy.InvokeAction(actorIndex, 0);
                return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra RedrawObject fallback failed: {ex.Message}");
            }
        }

        return false;
    }
}
