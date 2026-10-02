using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Services;

// C+ IPCProfileDataTuple matching CustomizePlus API definition
using ProfileTuple = (
    System.Guid UniqueId,
    string Name,
    string VirtualPath,
    System.Collections.Generic.List<(string Name, ushort WorldId, byte CharacterType, ushort CharacterSubType)> Characters,
    int Priority,
    bool IsEnabled);

public class CustomizePlusProfileInfo
{
    public Guid UniqueId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string VirtualPath { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class CustomizePlusIpc
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;

    // IPC Subscribers
    private readonly ICallGateSubscriber<(int Breaking, int Feature)>? getApiVersion;
    private readonly ICallGateSubscriber<bool>? isValid;
    private readonly ICallGateSubscriber<IList<ProfileTuple>>? getProfileList;
    private readonly ICallGateSubscriber<Guid, (int ErrorCode, string? ProfileJson)>? getProfileByUniqueId;
    private readonly ICallGateSubscriber<ushort, string, (int ErrorCode, Guid? UniqueId)>? setTemporaryProfileOnCharacter;
    private readonly ICallGateSubscriber<Guid, int>? deleteTemporaryProfileByUniqueId;
    private readonly ICallGateSubscriber<ushort, int>? deleteTemporaryProfileOnCharacter;

    private bool isAvailable = false;
    private DateTime lastAvailabilityCheck = DateTime.MinValue;

    private List<CustomizePlusProfileInfo> cachedProfiles = new();
    private DateTime lastProfilesFetch = DateTime.MinValue;

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

    public CustomizePlusIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;

        try
        {
            getApiVersion = pi.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
            isValid = pi.GetIpcSubscriber<bool>("CustomizePlus.General.IsValid");
            getProfileList = pi.GetIpcSubscriber<IList<ProfileTuple>>("CustomizePlus.Profile.GetList");
            getProfileByUniqueId = pi.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
            setTemporaryProfileOnCharacter = pi.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
            deleteTemporaryProfileByUniqueId = pi.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
            deleteTemporaryProfileOnCharacter = pi.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        }
        catch (Exception ex)
        {
            log.Warning($"Error initializing CustomizePlus IPC subscribers: {ex.Message}");
        }

        CheckAvailability();
    }

    public void CheckAvailability()
    {
        lastAvailabilityCheck = DateTime.UtcNow;
        try
        {
            if (getApiVersion == null)
            {
                isAvailable = false;
                return;
            }

            var (breaking, feature) = getApiVersion.InvokeFunc();
            // CustomizePlus API v6+
            isAvailable = breaking >= 6;
        }
        catch
        {
            isAvailable = false;
        }
    }

    public IReadOnlyList<CustomizePlusProfileInfo> GetProfiles(bool forceRefresh = false)
    {
        if (!IsAvailable) return Array.Empty<CustomizePlusProfileInfo>();

        if (!forceRefresh && (DateTime.UtcNow - lastProfilesFetch).TotalSeconds < 2.0)
        {
            return cachedProfiles;
        }

        lastProfilesFetch = DateTime.UtcNow;
        try
        {
            if (getProfileList != null)
            {
                var list = getProfileList.InvokeFunc();
                if (list != null)
                {
                    cachedProfiles = list.Select(p => new CustomizePlusProfileInfo
                    {
                        UniqueId = p.UniqueId,
                        Name = p.Name,
                        VirtualPath = p.VirtualPath,
                        IsEnabled = p.IsEnabled
                    }).OrderBy(p => p.Name).ToList();
                    return cachedProfiles;
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug($"Failed to fetch CustomizePlus profiles: {ex.Message}");
        }

        return cachedProfiles;
    }

    public string? GetProfileJson(Guid uniqueId)
    {
        if (!IsAvailable || getProfileByUniqueId == null) return null;
        try
        {
            var (ec, json) = getProfileByUniqueId.InvokeFunc(uniqueId);
            if (ec == 0) return json;
            log.Warning($"CustomizePlus.Profile.GetByUniqueId returned error code {ec} for {uniqueId}");
        }
        catch (Exception ex)
        {
            log.Error($"Error calling CustomizePlus.Profile.GetByUniqueId: {ex.Message}");
        }
        return null;
    }

    public Guid? SetTemporaryProfile(ushort gameObjectIndex, string profileJson)
    {
        if (!IsAvailable || setTemporaryProfileOnCharacter == null) return null;
        try
        {
            var (ec, guid) = setTemporaryProfileOnCharacter.InvokeFunc(gameObjectIndex, profileJson);
            if (ec == 0 && guid.HasValue)
            {
                log.Info($"CustomizePlus: Set temporary profile {guid.Value} on actor index {gameObjectIndex}");
                return guid.Value;
            }
            log.Warning($"CustomizePlus.Profile.SetTemporaryProfileOnCharacter returned error code {ec} for actor index {gameObjectIndex}");
        }
        catch (Exception ex)
        {
            log.Error($"Error calling CustomizePlus.Profile.SetTemporaryProfileOnCharacter: {ex.Message}");
        }
        return null;
    }

    public Guid? SetTemporaryProfileByGuid(ushort gameObjectIndex, Guid uniqueId)
    {
        var json = GetProfileJson(uniqueId);
        if (string.IsNullOrEmpty(json)) return null;
        return SetTemporaryProfile(gameObjectIndex, json);
    }

    public bool DeleteTemporaryProfile(Guid uniqueId)
    {
        if (!IsAvailable || deleteTemporaryProfileByUniqueId == null) return false;
        try
        {
            int ec = deleteTemporaryProfileByUniqueId.InvokeFunc(uniqueId);
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning($"Error deleting CustomizePlus temporary profile {uniqueId}: {ex.Message}");
            return false;
        }
    }

    public bool DeleteTemporaryProfileOnCharacter(ushort gameObjectIndex)
    {
        if (!IsAvailable || deleteTemporaryProfileOnCharacter == null) return false;
        try
        {
            int ec = deleteTemporaryProfileOnCharacter.InvokeFunc(gameObjectIndex);
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning($"Error deleting CustomizePlus temporary profile on actor index {gameObjectIndex}: {ex.Message}");
            return false;
        }
    }
}
