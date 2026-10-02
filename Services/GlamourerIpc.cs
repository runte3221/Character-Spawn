using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Services;

public class GlamourerIpc
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;

    // V2 IPC Subscribers
    private readonly ICallGateSubscriber<(int, int)>? apiVersionV2;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getDesignListV2;
    private readonly ICallGateSubscriber<Guid, int, uint, ulong, int>? applyDesignV2Ulong;
    private readonly ICallGateSubscriber<Guid, int, uint, uint, int>? applyDesignV2Uint;
    private readonly ICallGateSubscriber<string, int, uint, ulong, int>? applyStateV2Ulong;
    private readonly ICallGateSubscriber<string, int, uint, uint, int>? applyStateV2Uint;
    private readonly ICallGateSubscriber<int, uint, ulong, int>? reapplyStateV2Ulong;
    private readonly ICallGateSubscriber<int, uint, uint, int>? reapplyStateV2Uint;
    private readonly ICallGateSubscriber<int, string?>? getCustomizationFromActor;

    // Fallback Subscribers
    private readonly ICallGateSubscriber<int, (int, int)>? apiVersionsLegacy;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getDesignListLegacy;
    private readonly ICallGateSubscriber<Guid, int, object?>? applyByGuidLegacy;
    private readonly ICallGateSubscriber<string, int, object?>? applyByStringLegacy;

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

    public GlamourerIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;

        try
        {
            apiVersionV2 = pi.GetIpcSubscriber<(int, int)>("Glamourer.ApiVersion.V2");
            getDesignListV2 = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList.V2");
            applyDesignV2Ulong = pi.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
            applyDesignV2Uint = pi.GetIpcSubscriber<Guid, int, uint, uint, int>("Glamourer.ApplyDesign");
            applyStateV2Ulong = pi.GetIpcSubscriber<string, int, uint, ulong, int>("Glamourer.ApplyState");
            applyStateV2Uint = pi.GetIpcSubscriber<string, int, uint, uint, int>("Glamourer.ApplyState");
            reapplyStateV2Ulong = pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.ReapplyState");
            reapplyStateV2Uint = pi.GetIpcSubscriber<int, uint, uint, int>("Glamourer.ReapplyState");
            getCustomizationFromActor = pi.GetIpcSubscriber<int, string?>("Glamourer.GetCustomizationFromActor");

            apiVersionsLegacy = pi.GetIpcSubscriber<int, (int, int)>("Glamourer.ApiVersions");
            getDesignListLegacy = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList");
            applyByGuidLegacy = pi.GetIpcSubscriber<Guid, int, object?>("Glamourer.ApplyByGuid");
            applyByStringLegacy = pi.GetIpcSubscriber<string, int, object?>("Glamourer.ApplyByString");

            CheckAvailability();
        }
        catch (Exception ex)
        {
            log.Warning($"Glamourer IPC subscription init failed: {ex.Message}");
            isAvailable = false;
        }
    }

    public bool CheckAvailability()
    {
        lastAvailabilityCheck = DateTime.UtcNow;

        // Try V2 first
        if (apiVersionV2 != null)
        {
            try
            {
                var (major, minor) = apiVersionV2.InvokeFunc();
                isAvailable = major >= 1;
                if (isAvailable) return true;
            }
            catch
            {
                // V2 not available, try fallback
            }
        }

        // Try Legacy
        if (apiVersionsLegacy != null)
        {
            try
            {
                var (major, minor) = apiVersionsLegacy.InvokeFunc(0);
                isAvailable = major >= 1;
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

    public Dictionary<Guid, string> GetDesigns()
    {
        if (!IsAvailable) return new();

        // 1. Try V2
        if (getDesignListV2 != null)
        {
            try
            {
                var list = getDesignListV2.InvokeFunc();
                if (list != null) return list;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer V2 GetDesignList failed: {ex.Message}");
            }
        }

        // 2. Try Legacy
        if (getDesignListLegacy != null)
        {
            try
            {
                var list = getDesignListLegacy.InvokeFunc();
                if (list != null) return list;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer Legacy GetDesignList failed: {ex.Message}");
            }
        }

        return new();
    }

    /// <summary>
    /// Guid または State 文字列（MCDF / Base64 / JSON）を適切な API でアクターに適用する
    /// <summary>
    /// Guid、名前、または State 文字列（MCDF Base64 / JSON）を適切な API (flags = 7: 全適用) でアクターに適用する
    /// </summary>
    public bool ApplyDesignToActor(string designString, int actorIndex)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(designString)) return false;

        // 1. Guid 文字列かどうか判定、あるいは名前から Guid を解決
        Guid targetGuid = Guid.Empty;
        if (Guid.TryParse(designString, out var parsedGuid))
        {
            targetGuid = parsedGuid;
        }
        else if (!designString.StartsWith("{") && !designString.StartsWith("[") && designString.Length < 100)
        {
            // 名前からデザインリストを検索 (AQuestReborn スタイル)
            var designs = GetDesigns();
            foreach (var kvp in designs)
            {
                if (string.Equals(kvp.Value, designString, StringComparison.OrdinalIgnoreCase))
                {
                    targetGuid = kvp.Key;
                    break;
                }
            }
        }

        if (targetGuid != Guid.Empty)
        {
            // ApplyDesign V2 (ulong flags = 7)
            if (applyDesignV2Ulong != null)
            {
                try
                {
                    int res = applyDesignV2Ulong.InvokeFunc(targetGuid, actorIndex, 0, 7UL);
                    log.Information($"Glamourer ApplyDesign(Guid: {targetGuid}, Flags: 7UL) result: {res}");
                    if (res == 0) return true;
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyDesign V2 (ulong) failed: {ex.Message}");
                }
            }

            // ApplyDesign V2 (uint flags = 7)
            if (applyDesignV2Uint != null)
            {
                try
                {
                    int res = applyDesignV2Uint.InvokeFunc(targetGuid, actorIndex, 0, 7U);
                    log.Information($"Glamourer ApplyDesign(Guid: {targetGuid}, Flags: 7U) result: {res}");
                    if (res == 0) return true;
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyDesign V2 (uint) failed: {ex.Message}");
                }
            }

            // Legacy ApplyByGuid
            if (applyByGuidLegacy != null)
            {
                try
                {
                    applyByGuidLegacy.InvokeAction(targetGuid, actorIndex);
                    log.Information($"Glamourer ApplyByGuid Legacy executed for actor {actorIndex}.");
                    return true;
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyByGuid Legacy failed: {ex.Message}");
                }
            }
        }

        // 2. State 文字列（Base64 / MCDF / JSON）(flags = 7)
        if (applyStateV2Ulong != null)
        {
            try
            {
                int res = applyStateV2Ulong.InvokeFunc(designString, actorIndex, 0, 7UL);
                log.Information($"Glamourer ApplyState (ulong flags=7) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Glamourer ApplyState V2 (ulong) failed: {ex.Message}");
            }
        }

        if (applyStateV2Uint != null)
        {
            try
            {
                int res = applyStateV2Uint.InvokeFunc(designString, actorIndex, 0, 7U);
                log.Information($"Glamourer ApplyState (uint flags=7) result: {res}");
                if (res == 0) return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Glamourer ApplyState V2 (uint) failed: {ex.Message}");
            }
        }

        // Legacy ApplyByString
        if (applyByStringLegacy != null)
        {
            try
            {
                applyByStringLegacy.InvokeAction(designString, actorIndex);
                log.Information($"Glamourer ApplyByString Legacy executed for actor {actorIndex}.");
                return true;
            }
            catch (Exception ex)
            {
                log.Warning($"Glamourer ApplyByString Legacy failed: {ex.Message}");
            }
        }

        return false;
    }

    public string? GetCustomization(int actorIndex)
    {
        if (!IsAvailable || getCustomizationFromActor == null) return null;

        try
        {
            return getCustomizationFromActor.InvokeFunc(actorIndex);
        }
        catch (Exception ex)
        {
            log.Debug($"Failed to get Glamourer customization: {ex.Message}");
            return null;
        }
    }

    public bool ReapplyState(int actorIndex)
    {
        if (!IsAvailable) return false;

        if (reapplyStateV2Ulong != null)
        {
            try
            {
                int res = reapplyStateV2Ulong.InvokeFunc(actorIndex, 0, 7UL);
                return res == 0;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer ReapplyState (ulong) failed: {ex.Message}");
            }
        }

        if (reapplyStateV2Uint != null)
        {
            try
            {
                int res = reapplyStateV2Uint.InvokeFunc(actorIndex, 0, 7U);
                return res == 0;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer ReapplyState (uint) failed: {ex.Message}");
            }
        }

        return false;
    }
}
