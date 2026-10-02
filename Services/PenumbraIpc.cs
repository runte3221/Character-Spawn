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
    private readonly ICallGateSubscriber<int, Guid, bool, bool, int>? setCollectionForObjectV5Guid;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectV5;

    // Fallback Subscribers
    private readonly ICallGateSubscriber<(int, int)>? apiVersionLegacy;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsLegacy;
    private readonly ICallGateSubscriber<int, string, bool, bool, int>? setCollectionForObjectLegacyString;
    private readonly ICallGateSubscriber<string, int, int>? setCollectionForObjectOldLegacy;
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
            setCollectionForObjectV5Guid = pi.GetIpcSubscriber<int, Guid, bool, bool, int>("Penumbra.SetCollectionForObject.V5");
            redrawObjectV5 = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject.V5");

            apiVersionLegacy = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion");
            getCollectionsLegacy = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections");
            setCollectionForObjectLegacyString = pi.GetIpcSubscriber<int, string, bool, bool, int>("Penumbra.SetCollectionForObject");
            setCollectionForObjectOldLegacy = pi.GetIpcSubscriber<string, int, int>("Penumbra.SetCollectionForObject");
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

    public bool SetCollectionForActor(string collectionIdentifier, int actorIndex)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(collectionIdentifier)) return false;

        // 1. Guid 指定または名前から Guid への解決
        Guid collGuid = Guid.Empty;
        if (Guid.TryParse(collectionIdentifier, out var parsedGuid))
        {
            collGuid = parsedGuid;
        }
        else
        {
            // 名前からコレクション一覧を引いて Guid を検索
            var colls = GetCollections();
            foreach (var kvp in colls)
            {
                if (string.Equals(kvp.Value, collectionIdentifier, StringComparison.OrdinalIgnoreCase))
                {
                    collGuid = kvp.Key;
                    break;
                }
            }
        }

        // V5 (Guid 引数)
        if (collGuid != Guid.Empty && setCollectionForObjectV5Guid != null)
        {
            try
            {
                int res = setCollectionForObjectV5Guid.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5(Index:{actorIndex}, Guid:{collGuid}) result: {res}");
                return res == 0;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject V5 failed: {ex.Message}");
            }
        }

        // Legacy (string 引数: actorIndex, collectionName, allowCreate, allowDelete)
        if (setCollectionForObjectLegacyString != null)
        {
            try
            {
                int res = setCollectionForObjectLegacyString.InvokeFunc(actorIndex, collectionIdentifier, true, true);
                log.Information($"Penumbra SetCollectionForObject Legacy(Index:{actorIndex}, Name:{collectionIdentifier}) result: {res}");
                return res == 0;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject Legacy failed: {ex.Message}");
            }
        }

        // Old Legacy (collectionName, actorIndex)
        if (setCollectionForObjectOldLegacy != null)
        {
            try
            {
                var result = setCollectionForObjectOldLegacy.InvokeFunc(collectionIdentifier, actorIndex);
                return result == 0;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject Old Legacy failed: {ex.Message}");
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
