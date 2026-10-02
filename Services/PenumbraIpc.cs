using System;
using System.Collections.Generic;
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
    private readonly ICallGateSubscriber<int, Guid, bool, bool, (int, Guid)>? setCollectionForObjectV5Tuple;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, int>? setCollectionForObjectV5Int;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectV5;

    // Fallback Subscribers
    private readonly ICallGateSubscriber<(int, int)>? apiVersionLegacy;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsLegacy;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, (int, Guid)>? setCollectionForObjectLegacyTuple;
    private readonly ICallGateSubscriber<int, string, bool, bool, (int, string)>? setCollectionForObjectLegacyStringTuple;
    private readonly ICallGateSubscriber<int, string, bool, bool, int>? setCollectionForObjectLegacyStringInt;
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
            setCollectionForObjectV5Tuple = pi.GetIpcSubscriber<int, Guid, bool, bool, (int, Guid)>("Penumbra.SetCollectionForObject.V5");
            setCollectionForObjectV5Int = pi.GetIpcSubscriber<int, Guid, bool, bool, int>("Penumbra.SetCollectionForObject.V5");
            redrawObjectV5 = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject.V5");

            apiVersionLegacy = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion");
            getCollectionsLegacy = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections");
            setCollectionForObjectLegacyTuple = pi.GetIpcSubscriber<int, Guid, bool, bool, (int, Guid)>("Penumbra.SetCollectionForObject");
            setCollectionForObjectLegacyStringTuple = pi.GetIpcSubscriber<int, string, bool, bool, (int, string)>("Penumbra.SetCollectionForObject");
            setCollectionForObjectLegacyStringInt = pi.GetIpcSubscriber<int, string, bool, bool, int>("Penumbra.SetCollectionForObject");
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

        if (apiVersionV5 != null)
        {
            try
            {
                var (major, minor) = apiVersionV5.InvokeFunc();
                isAvailable = major >= 4;
                if (isAvailable) return true;
            }
            catch { }
        }

        if (apiVersionLegacy != null)
        {
            try
            {
                var (major, minor) = apiVersionLegacy.InvokeFunc();
                isAvailable = major >= 4;
                return isAvailable;
            }
            catch { }
        }

        isAvailable = false;
        return false;
    }

    public Dictionary<Guid, string> GetCollections()
    {
        if (!IsAvailable) return new();

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

        Guid collGuid = Guid.Empty;
        if (Guid.TryParse(collectionIdentifier, out var parsedGuid))
        {
            collGuid = parsedGuid;
        }
        else
        {
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

        // 1. V5 Tuple: (PenumbraApiEc, Guid) -> (int, Guid)
        if (collGuid != Guid.Empty && setCollectionForObjectV5Tuple != null)
        {
            try
            {
                var res = setCollectionForObjectV5Tuple.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 Tuple(Index:{actorIndex}, Guid:{collGuid}) result: ({res.Item1}, {res.Item2})");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject V5 Tuple failed: {ex.Message}");
            }
        }

        // 2. Legacy Tuple: (int, Guid) -> AQR standard
        if (collGuid != Guid.Empty && setCollectionForObjectLegacyTuple != null)
        {
            try
            {
                var res = setCollectionForObjectLegacyTuple.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject Legacy Tuple(Index:{actorIndex}, Guid:{collGuid}) result: ({res.Item1}, {res.Item2})");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject Legacy Tuple failed: {ex.Message}");
            }
        }

        // 3. V5 Int
        if (collGuid != Guid.Empty && setCollectionForObjectV5Int != null)
        {
            try
            {
                int res = setCollectionForObjectV5Int.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 Int(Index:{actorIndex}, Guid:{collGuid}) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject V5 Int failed: {ex.Message}");
            }
        }

        // 4. Legacy String Tuple
        if (setCollectionForObjectLegacyStringTuple != null)
        {
            try
            {
                var res = setCollectionForObjectLegacyStringTuple.InvokeFunc(actorIndex, collectionIdentifier, true, true);
                log.Information($"Penumbra SetCollectionForObject String Tuple(Index:{actorIndex}, Name:{collectionIdentifier}) result: ({res.Item1}, {res.Item2})");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject String Tuple failed: {ex.Message}");
            }
        }

        // 5. Legacy String Int
        if (setCollectionForObjectLegacyStringInt != null)
        {
            try
            {
                int res = setCollectionForObjectLegacyStringInt.InvokeFunc(actorIndex, collectionIdentifier, true, true);
                log.Information($"Penumbra SetCollectionForObject String Int(Index:{actorIndex}, Name:{collectionIdentifier}) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject String Int failed: {ex.Message}");
            }
        }

        // 6. Old Legacy (name, actorIndex)
        if (setCollectionForObjectOldLegacy != null)
        {
            try
            {
                var res = setCollectionForObjectOldLegacy.InvokeFunc(collectionIdentifier, actorIndex);
                log.Information($"Penumbra SetCollectionForObject Old Legacy(Name:{collectionIdentifier}, Index:{actorIndex}) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject Old Legacy failed: {ex.Message}");
            }
        }

        log.Warning($"All SetCollectionForObject attempts failed for collection '{collectionIdentifier}' on actor #{actorIndex}.");
        return false;
    }

    public bool Redraw(int actorIndex)
    {
        if (!IsAvailable) return false;

        // 1. Try V5 RedrawObject(actorIndex, RedrawType.Redraw = 0)
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

        // 2. Try Legacy RedrawObject
        if (redrawObjectLegacy != null)
        {
            try
            {
                redrawObjectLegacy.InvokeAction(actorIndex, 0);
                return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra Legacy RedrawObject failed: {ex.Message}");
            }
        }

        return false;
    }
}
