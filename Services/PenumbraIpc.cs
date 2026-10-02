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

    // V5 IPC Subscribers (Official Penumbra V5: returns (PenumbraApiEc, (Guid, string)?))
    private readonly ICallGateSubscriber<(int, int)>? apiVersionV5;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsV5;
    private readonly ICallGateSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>? setCollectionForObjectV5NullableGuid;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, (int, (Guid, string)?)>? setCollectionForObjectV5Guid;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, (int, Guid)>? setCollectionForObjectV5GuidTuple;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, int>? setCollectionForObjectV5Int;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectV5;

    // Fallback Subscribers (Legacy Penumbra: returns (PenumbraApiEc, string))
    private readonly ICallGateSubscriber<(int, int)>? apiVersionLegacy;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getCollectionsLegacy;
    private readonly ICallGateSubscriber<int, string, bool, bool, (int, string)>? setCollectionForObjectLegacyStringTuple;
    private readonly ICallGateSubscriber<int, Guid, bool, bool, (int, Guid)>? setCollectionForObjectLegacyGuidTuple;
    private readonly ICallGateSubscriber<int, string, bool, bool, int>? setCollectionForObjectLegacyStringInt;
    private readonly ICallGateSubscriber<string, int, int>? setCollectionForObjectOldLegacy;
    private readonly ICallGateSubscriber<int, int, object?>? redrawObjectLegacy;

    // Temporary Collection IPC Subscribers (AQuestReborn / Mare Architecture)
    private readonly ICallGateSubscriber<string, string, (int, Guid)>? createTemporaryCollectionV6;
    private readonly ICallGateSubscriber<Guid, int, bool, int>? assignTemporaryCollectionV5;
    private readonly ICallGateSubscriber<string, Guid, Dictionary<string, string>, string, int, int>? addTemporaryModV5;
    private readonly ICallGateSubscriber<Guid, int>? deleteTemporaryCollectionV5;

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
            setCollectionForObjectV5NullableGuid = pi.GetIpcSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>("Penumbra.SetCollectionForObject.V5");
            setCollectionForObjectV5Guid = pi.GetIpcSubscriber<int, Guid, bool, bool, (int, (Guid, string)?)>("Penumbra.SetCollectionForObject.V5");
            setCollectionForObjectV5GuidTuple = pi.GetIpcSubscriber<int, Guid, bool, bool, (int, Guid)>("Penumbra.SetCollectionForObject.V5");
            setCollectionForObjectV5Int = pi.GetIpcSubscriber<int, Guid, bool, bool, int>("Penumbra.SetCollectionForObject.V5");
            redrawObjectV5 = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject.V5");

            apiVersionLegacy = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion");
            getCollectionsLegacy = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Penumbra.GetCollections");
            setCollectionForObjectLegacyStringTuple = pi.GetIpcSubscriber<int, string, bool, bool, (int, string)>("Penumbra.SetCollectionForObject");
            setCollectionForObjectLegacyGuidTuple = pi.GetIpcSubscriber<int, Guid, bool, bool, (int, Guid)>("Penumbra.SetCollectionForObject");
            setCollectionForObjectLegacyStringInt = pi.GetIpcSubscriber<int, string, bool, bool, int>("Penumbra.SetCollectionForObject");
            setCollectionForObjectOldLegacy = pi.GetIpcSubscriber<string, int, int>("Penumbra.SetCollectionForObject");
            redrawObjectLegacy = pi.GetIpcSubscriber<int, int, object?>("Penumbra.RedrawObject");

            createTemporaryCollectionV6 = pi.GetIpcSubscriber<string, string, (int, Guid)>("Penumbra.CreateTemporaryCollection.V6");
            assignTemporaryCollectionV5 = pi.GetIpcSubscriber<Guid, int, bool, int>("Penumbra.AssignTemporaryCollection.V5");
            addTemporaryModV5 = pi.GetIpcSubscriber<string, Guid, Dictionary<string, string>, string, int, int>("Penumbra.AddTemporaryMod.V5");
            deleteTemporaryCollectionV5 = pi.GetIpcSubscriber<Guid, int>("Penumbra.DeleteTemporaryCollection.V5");

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

    /// <summary>
    /// 指定されたコレクション（名前またはGUID）をアクターに設定する
    /// </summary>
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

        // 1. V5 Official: (int, (Guid, string)?) with Guid?
        if (collGuid != Guid.Empty && setCollectionForObjectV5NullableGuid != null)
        {
            try
            {
                var res = setCollectionForObjectV5NullableGuid.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 (Guid?:{collGuid}) result: ec={res.Item1}");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject V5 (Guid?) failed: {ex.Message}");
            }
        }

        // 2. V5 Official: (int, (Guid, string)?) with non-null Guid
        if (collGuid != Guid.Empty && setCollectionForObjectV5Guid != null)
        {
            try
            {
                var res = setCollectionForObjectV5Guid.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 (Guid:{collGuid}) result: ec={res.Item1}");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject V5 (Guid) failed: {ex.Message}");
            }
        }

        // 3. Legacy Official String Tuple: (int, string)
        if (setCollectionForObjectLegacyStringTuple != null)
        {
            try
            {
                var res = setCollectionForObjectLegacyStringTuple.InvokeFunc(actorIndex, collectionIdentifier, true, true);
                log.Information($"Penumbra SetCollectionForObject Legacy (Name:{collectionIdentifier}) result: ec={res.Item1}");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Penumbra SetCollectionForObject Legacy String Tuple failed: {ex.Message}");
            }
        }

        // 4. V5 Tuple (int, Guid)
        if (collGuid != Guid.Empty && setCollectionForObjectV5GuidTuple != null)
        {
            try
            {
                var res = setCollectionForObjectV5GuidTuple.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 Tuple(Guid:{collGuid}) result: ec={res.Item1}");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject V5 (int, Guid) failed: {ex.Message}");
            }
        }

        // 5. Legacy Guid Tuple (int, Guid)
        if (collGuid != Guid.Empty && setCollectionForObjectLegacyGuidTuple != null)
        {
            try
            {
                var res = setCollectionForObjectLegacyGuidTuple.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject Legacy Tuple(Guid:{collGuid}) result: ec={res.Item1}");
                if (res.Item1 == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject Legacy Guid Tuple failed: {ex.Message}");
            }
        }

        // 6. V5 Int
        if (collGuid != Guid.Empty && setCollectionForObjectV5Int != null)
        {
            try
            {
                int res = setCollectionForObjectV5Int.InvokeFunc(actorIndex, collGuid, true, true);
                log.Information($"Penumbra SetCollectionForObject.V5 Int(Guid:{collGuid}) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject V5 Int failed: {ex.Message}");
            }
        }

        // 7. Legacy String Int
        if (setCollectionForObjectLegacyStringInt != null)
        {
            try
            {
                int res = setCollectionForObjectLegacyStringInt.InvokeFunc(actorIndex, collectionIdentifier, true, true);
                log.Information($"Penumbra SetCollectionForObject String Int(Name:{collectionIdentifier}) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Debug($"Penumbra SetCollectionForObject String Int failed: {ex.Message}");
            }
        }

        // 8. Old Legacy (name, actorIndex)
        if (setCollectionForObjectOldLegacy != null)
        {
            try
            {
                var res = setCollectionForObjectOldLegacy.InvokeFunc(collectionIdentifier, actorIndex);
                log.Information($"Penumbra SetCollectionForObject Old Legacy(Name:{collectionIdentifier}) result: {res}");
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

    /// <summary>
    /// アクターを再描画する
    /// </summary>
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

    /// <summary>
    /// AQR / Mare 準拠: 一時コレクション（Temporary Collection）を作成する
    /// </summary>
    public Guid CreateTemporaryCollection(string name)
    {
        if (!IsAvailable || createTemporaryCollectionV6 == null) return Guid.Empty;
        try
        {
            var res = createTemporaryCollectionV6.InvokeFunc("CharacterSpawn", "CS_" + name);
            log.Information($"Penumbra CreateTemporaryCollection 'CS_{name}' result: ec={res.Item1}, Guid={res.Item2}");
            if (res.Item1 == 0) return res.Item2;
        }
        catch (Exception ex)
        {
            log.Warning($"CreateTemporaryCollection failed: {ex.Message}");
        }
        return Guid.Empty;
    }

    /// <summary>
    /// AQR / Mare 準拠: 一時コレクションをアクターに強制割り当てする
    /// </summary>
    public bool AssignTemporaryCollection(Guid collectionId, int actorIndex)
    {
        if (!IsAvailable || assignTemporaryCollectionV5 == null || collectionId == Guid.Empty) return false;
        try
        {
            int ec = assignTemporaryCollectionV5.InvokeFunc(collectionId, actorIndex, true);
            log.Information($"Penumbra AssignTemporaryCollection ({collectionId}) to actor #{actorIndex} result: ec={ec}");
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning($"AssignTemporaryCollection failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// AQR / Mare 準拠: 一時コレクションに Mod ファイル群および ManipulationData を登録する
    /// </summary>
    public bool AddTemporaryMod(Guid collectionId, Dictionary<string, string> paths, string manipulationData)
    {
        if (!IsAvailable || addTemporaryModV5 == null || collectionId == Guid.Empty) return false;
        try
        {
            int ec = addTemporaryModV5.InvokeFunc("CharacterSpawn_Mcdf", collectionId, paths, manipulationData ?? string.Empty, 0);
            log.Information($"Penumbra AddTemporaryMod ({paths.Count} files) to coll {collectionId} result: ec={ec}");
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning($"AddTemporaryMod failed: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// AQR / Mare 準拠: 一時コレクションを削除してリソースを解放する
    /// </summary>
    public bool DeleteTemporaryCollection(Guid collectionId)
    {
        if (!IsAvailable || deleteTemporaryCollectionV5 == null || collectionId == Guid.Empty) return false;
        try
        {
            int ec = deleteTemporaryCollectionV5.InvokeFunc(collectionId);
            log.Information($"Penumbra DeleteTemporaryCollection ({collectionId}) result: ec={ec}");
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Debug($"DeleteTemporaryCollection failed: {ex.Message}");
        }
        return false;
    }
}
