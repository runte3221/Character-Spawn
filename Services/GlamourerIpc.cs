using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CharacterSpawn.Services;

public class GlamourerIpc
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<string, int, object?>? applyByString;
    private readonly ICallGateSubscriber<int, string?>? getCustomizationFromActor;
    private readonly ICallGateSubscriber<int, (int, int)>? getApiVersions;
    private readonly ICallGateSubscriber<Dictionary<Guid, string>>? getDesignList;
    private readonly ICallGateSubscriber<int, uint, uint, object?>? reapplyState;

    public bool IsAvailable { get; private set; }

    public GlamourerIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;

        try
        {
            getApiVersions = pi.GetIpcSubscriber<int, (int, int)>("Glamourer.ApiVersions");
            applyByString = pi.GetIpcSubscriber<string, int, object?>("Glamourer.ApplyByString");
            getCustomizationFromActor = pi.GetIpcSubscriber<int, string?>("Glamourer.GetCustomizationFromActor");
            getDesignList = pi.GetIpcSubscriber<Dictionary<Guid, string>>("Glamourer.GetDesignList");
            reapplyState = pi.GetIpcSubscriber<int, uint, uint, object?>("Glamourer.ReapplyState");

            CheckAvailability();
        }
        catch (Exception ex)
        {
            log.Warning($"Glamourer IPC subscription failed: {ex.Message}");
            IsAvailable = false;
        }
    }

    public bool CheckAvailability()
    {
        try
        {
            if (getApiVersions == null) return false;
            var (major, minor) = getApiVersions.InvokeFunc(0);
            IsAvailable = major >= 1;
            return IsAvailable;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }

    public bool ApplyDesignToActor(string designString, int actorIndex)
    {
        if (!IsAvailable || applyByString == null) return false;

        try
        {
            applyByString.InvokeAction(designString, actorIndex);
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to apply Glamourer design: {ex.Message}");
            return false;
        }
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
            log.Error($"Failed to get Glamourer customization: {ex.Message}");
            return null;
        }
    }

    public Dictionary<Guid, string> GetDesigns()
    {
        if (!IsAvailable || getDesignList == null) return new();

        try
        {
            return getDesignList.InvokeFunc();
        }
        catch
        {
            return new();
        }
    }

    public bool ReapplyState(int actorIndex)
    {
        if (!IsAvailable || reapplyState == null) return false;

        try
        {
            reapplyState.InvokeAction(actorIndex, 0, 0);
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to reapply Glamourer state for actor {actorIndex}: {ex.Message}");
            return false;
        }
    }
}
