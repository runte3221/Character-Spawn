using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

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
    private readonly ICallGateSubscriber<int, uint, (int, JObject?)>? getStateV2;
    private readonly ICallGateSubscriber<int, (int, JObject?)>? getStateLegacy;
    private readonly ICallGateSubscriber<Guid, JObject?>? getDesignJObject;
    private readonly ICallGateSubscriber<int, uint, ulong, int>? revertStateV2Ulong;
    private readonly ICallGateSubscriber<int, uint, uint, int>? revertStateV2Uint;
    private readonly ICallGateSubscriber<int, uint, ulong, int>? revertToAutomationV2Ulong;
    private readonly ICallGateSubscriber<int, uint, uint, int>? revertToAutomationV2Uint;
    private readonly ICallGateSubscriber<int, uint, int>? unlockStateV2;
    private readonly ICallGateSubscriber<string, uint, ulong, int>? revertStateNameV2Ulong;
    private readonly ICallGateSubscriber<string, uint, int>? unlockStateNameV2;

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
            getDesignJObject = pi.GetIpcSubscriber<Guid, JObject?>("Glamourer.GetDesignJObject");
            applyDesignV2Ulong = pi.GetIpcSubscriber<Guid, int, uint, ulong, int>("Glamourer.ApplyDesign");
            applyDesignV2Uint = pi.GetIpcSubscriber<Guid, int, uint, uint, int>("Glamourer.ApplyDesign");
            applyStateV2Ulong = pi.GetIpcSubscriber<string, int, uint, ulong, int>("Glamourer.ApplyState");
            applyStateV2Uint = pi.GetIpcSubscriber<string, int, uint, uint, int>("Glamourer.ApplyState");
            reapplyStateV2Ulong = pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.ReapplyState");
            reapplyStateV2Uint = pi.GetIpcSubscriber<int, uint, uint, int>("Glamourer.ReapplyState");
            getCustomizationFromActor = pi.GetIpcSubscriber<int, string?>("Glamourer.GetCustomizationFromActor");
            getStateV2 = pi.GetIpcSubscriber<int, uint, (int, JObject?)>("Glamourer.GetState");
            getStateLegacy = pi.GetIpcSubscriber<int, (int, JObject?)>("Glamourer.GetState");
            revertStateV2Ulong = pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertState");
            revertStateV2Uint = pi.GetIpcSubscriber<int, uint, uint, int>("Glamourer.RevertState");
            revertToAutomationV2Ulong = pi.GetIpcSubscriber<int, uint, ulong, int>("Glamourer.RevertToAutomation");
            revertToAutomationV2Uint = pi.GetIpcSubscriber<int, uint, uint, int>("Glamourer.RevertToAutomation");
            unlockStateV2 = pi.GetIpcSubscriber<int, uint, int>("Glamourer.UnlockState");
            revertStateNameV2Ulong = pi.GetIpcSubscriber<string, uint, ulong, int>("Glamourer.RevertStateName");
            unlockStateNameV2 = pi.GetIpcSubscriber<string, uint, int>("Glamourer.UnlockStateName");

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

    public JObject? GetDesign(Guid guid)
    {
        if (getDesignJObject != null)
        {
            try
            {
                var jobj = getDesignJObject.InvokeFunc(guid);
                if (jobj != null) return jobj;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer GetDesignJObject IPC failed: {ex.Message}");
            }
        }

        // Disk fallback: %APPDATA%\XIVLauncher\pluginConfigs\Glamourer\designs\{guid}.json
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string designPath = Path.Combine(appData, "XIVLauncher", "pluginConfigs", "Glamourer", "designs", $"{guid}.json");
            if (File.Exists(designPath))
            {
                string json = File.ReadAllText(designPath);
                return JObject.Parse(json);
            }
        }
        catch (Exception ex)
        {
            log.Debug($"Failed to read design file from disk: {ex.Message}");
        }

        return null;
    }

    public JObject? ParseDesignString(string designString)
    {
        if (string.IsNullOrWhiteSpace(designString)) return null;
        string trimmed = designString.Trim();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            try { return JObject.Parse(trimmed); } catch { }
        }

        // Base64 + Gzip decode
        try
        {
            byte[] rawBytes = Convert.FromBase64String(trimmed);
            if (rawBytes.Length > 2)
            {
                int gzipOffset = -1;
                for (int i = 0; i < rawBytes.Length - 1; i++)
                {
                    if (rawBytes[i] == 0x1F && rawBytes[i + 1] == 0x8B)
                    {
                        gzipOffset = i;
                        break;
                    }
                }

                if (gzipOffset >= 0)
                {
                    using var ms = new MemoryStream(rawBytes, gzipOffset, rawBytes.Length - gzipOffset);
                    using var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var reader = new StreamReader(gz, System.Text.Encoding.UTF8);
                    string json = reader.ReadToEnd();
                    return JObject.Parse(json);
                }
            }
        }
        catch (Exception ex)
        {
            log.Debug($"Failed to parse compressed design string: {ex.Message}");
        }

        return null;
    }

    public static string CompressToBase64(JObject jObj)
    {
        string json = jObj.ToString(Newtonsoft.Json.Formatting.None);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true))
        {
            gz.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(ms.ToArray());
    }

    public static void ForceAllApply(JObject jObj)
    {
        // 1. Customize スロットの全 Apply を true に (Race, Clan, Gender 等の Apply: false による自キャラロールバックを撲滅)
        if (jObj["Customize"] is JObject cust)
        {
            foreach (var prop in cust.Properties())
            {
                if (prop.Value is JObject slotObj)
                {
                    slotObj["Apply"] = true;
                }
            }
        }

        // 2. Equipment スロットの全 Apply を true に
        if (jObj["Equipment"] is JObject equip)
        {
            foreach (var prop in equip.Properties())
            {
                if (prop.Value is JObject slotObj)
                {
                    slotObj["Apply"] = true;
                    slotObj["ApplyStain"] = true;
                }
            }
        }
    }

    public static byte[]? ExtractCustomizeBytes(JObject jObj)
    {
        if (jObj["Customize"] is not JObject cust) return null;
        byte[] bytes = new byte[26];

        foreach (var (key, byteIdx, mask) in CustomizeMap)
        {
            if (cust[key] is JObject slotObj && slotObj["Value"] != null)
            {
                try
                {
                    int val = slotObj["Value"]!.Value<int>();
                    if (mask == 0xFF)
                    {
                        bytes[byteIdx] = (byte)(val & 0xFF);
                    }
                    else
                    {
                        if (val != 0)
                            bytes[byteIdx] |= mask;
                        else
                            bytes[byteIdx] &= (byte)~mask;
                    }
                }
                catch { }
            }
        }

        return bytes;
    }

    /// <summary>
    /// Guid、名前、または State 文字列（MCDF Base64 / JSON）を全スロット強制適用(ForceAllApply)し、
    /// GZip圧縮Base64を通じて Glamourer.ApplyState または ApplyDesign で確実にアクターを変身させる
    /// </summary>
    public (bool Success, byte[]? CustomizeBytes) ApplyDesignToActorEx(string designString, int actorIndex)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(designString)) return (false, null);

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

        // A. Guid がある場合: デザイン取得 -> ForceAllApply -> GZip圧縮Base64で ApplyState (Race: Apply=falseバグを完全解消)
        if (targetGuid != Guid.Empty)
        {
            byte[]? customizeBytes = null;
            var targetDesignObj = GetDesign(targetGuid);
            if (targetDesignObj != null)
            {
                ForceAllApply(targetDesignObj);
                customizeBytes = ExtractCustomizeBytes(targetDesignObj);

                // GZip圧縮Base64にエンコードして ApplyState に渡す
                try
                {
                    string compressedBase64 = CompressToBase64(targetDesignObj);
                    if (applyStateV2Ulong != null)
                    {
                        int res = applyStateV2Ulong.InvokeFunc(compressedBase64, actorIndex, 0, 7UL);
                        log.Information($"Glamourer ApplyState (ForceAllApply compressed, Flags: 7UL) for Guid {targetGuid} result: {res}");
                        if (res == 0) return (true, customizeBytes);
                    }
                    else if (applyStateV2Uint != null)
                    {
                        int res = applyStateV2Uint.InvokeFunc(compressedBase64, actorIndex, 0, 7U);
                        log.Information($"Glamourer ApplyState (ForceAllApply compressed, Flags: 7U) for Guid {targetGuid} result: {res}");
                        if (res == 0) return (true, customizeBytes);
                    }
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyState with compressed base64 failed for Guid {targetGuid}: {ex.Message}");
                }
            }

            // フォールバック: ApplyDesign IPC を直接呼び出し (flags = 7UL: Once | Equipment | Customization)
            if (applyDesignV2Ulong != null)
            {
                try
                {
                    int res = applyDesignV2Ulong.InvokeFunc(targetGuid, actorIndex, 0, 7UL);
                    log.Information($"Glamourer ApplyDesign (Guid: {targetGuid}, Flags: 7UL) fallback result: {res}");
                    if (res == 0) return (true, customizeBytes);
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyDesign V2 (ulong) failed: {ex.Message}");
                }
            }

            if (applyDesignV2Uint != null)
            {
                try
                {
                    int res = applyDesignV2Uint.InvokeFunc(targetGuid, actorIndex, 0, 7U);
                    log.Information($"Glamourer ApplyDesign (Guid: {targetGuid}, Flags: 7U) fallback result: {res}");
                    if (res == 0) return (true, customizeBytes);
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyDesign V2 (uint) failed: {ex.Message}");
                }
            }

            // 2. Legacy ApplyByGuid
            if (applyByGuidLegacy != null)
            {
                try
                {
                    applyByGuidLegacy.InvokeFunc(targetGuid, actorIndex);
                    log.Information($"Glamourer ApplyByGuid (Legacy) executed for Guid {targetGuid}.");
                    return (true, customizeBytes);
                }
                catch (Exception ex)
                {
                    log.Warning($"Glamourer ApplyByGuid Legacy failed: {ex.Message}");
                }
            }

            // Guid の場合は Base64 デコード処理にはフォールスルーしない
            return (false, customizeBytes);
        }

        // B. Guid ではない場合 (MCDF 等の Base64 / JSON デザイン文字列): Parse して ApplyState を実行
        JObject? parsedObj = ParseDesignString(designString);
        byte[]? parsedCustBytes = null;
        string targetStateString = designString;
        if (parsedObj != null)
        {
            ForceAllApply(parsedObj);
            parsedCustBytes = ExtractCustomizeBytes(parsedObj);
            try
            {
                targetStateString = CompressToBase64(parsedObj);
            }
            catch { }
        }

        if (applyStateV2Ulong != null)
        {
            try
            {
                int res = applyStateV2Ulong.InvokeFunc(targetStateString, actorIndex, 0, 7UL);
                log.Information($"Glamourer ApplyState (ulong flags=7) result: {res}");
                if (res == 0) return (true, parsedCustBytes);
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
                int res = applyStateV2Uint.InvokeFunc(targetStateString, actorIndex, 0, 7U);
                log.Information($"Glamourer ApplyState (uint flags=7) result: {res}");
                if (res == 0) return (true, parsedCustBytes);
            }
            catch (Exception ex)
            {
                log.Warning($"Glamourer ApplyState V2 (uint) failed: {ex.Message}");
            }
        }
        return (false, null);
    }

    public bool ApplyDesignToActor(string designString, int actorIndex)
    {
        return ApplyDesignToActorEx(designString, actorIndex).Success;
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
                int res = reapplyStateV2Ulong.InvokeFunc(actorIndex, 0, 6UL);
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
                int res = reapplyStateV2Uint.InvokeFunc(actorIndex, 0, 6U);
                return res == 0;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer ReapplyState (uint) failed: {ex.Message}");
            }
        }

        return false;
    }

    public JObject? GetState(int actorIndex)
    {
        if (!IsAvailable) return null;

        if (getStateV2 != null)
        {
            try
            {
                var (ec, state) = getStateV2.InvokeFunc(actorIndex, 0);
                if (ec == 0 && state != null) return state;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer GetState V2 failed: {ex.Message}");
            }
        }

        if (getStateLegacy != null)
        {
            try
            {
                var (ec, state) = getStateLegacy.InvokeFunc(actorIndex);
                if (ec == 0 && state != null) return state;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer GetState Legacy failed: {ex.Message}");
            }
        }

        return null;
    }

    public enum NpcApplyResult
    {
        Applied,
        StateNull,
        Failed
    }

    /// <summary>
    /// HDM (HumanGuise.cs) 準拠の 1 フレーム非ブロッキング NPC 外見適用試行
    /// 26バイト CustomizeData と 10スロットの EquipmentModelIds を Glamourer JObject にマッピングして適用
    /// </summary>
    public NpcApplyResult TryApplyNpcAppearance(int actorIndex, byte[]? customizeData, ulong[]? equipmentModelIds, bool showHeadgear = true)
    {
        if (!IsAvailable) return NpcApplyResult.Failed;

        var state = GetState(actorIndex);
        if (state == null)
            return NpcApplyResult.StateNull;

        // 1. CustomizeData (26バイト) の適用
        if (customizeData != null && customizeData.Length >= 26 && state["Customize"] is JObject custObj)
        {
            WriteCustomize(custObj, customizeData);
        }

        // 2. EquipmentModelIds (10スロット) の適用
        if (equipmentModelIds != null && equipmentModelIds.Length > 0 && state["Equipment"] is JObject equipObj)
        {
            WriteEquipment(equipObj, equipmentModelIds);
            SetHeadgearShown(equipObj, showHeadgear);
        }

        // 3. 武器スロットの管理解除（自キャラの武器がNPCに上書きされるのを防止）
        if (state["Equipment"] is JObject eqObj)
        {
            UnmanageWeaponSlot(eqObj, "MainHand");
            UnmanageWeaponSlot(eqObj, "OffHand");
            if (eqObj["Weapon"] is JObject wv) wv["Apply"] = false;
        }

        // 4. 自キャラの肌色・シェーダーパラメータ汚染の完全除去 (HDM 方式)
        state.Remove("Parameters");
        state.Remove("Materials");

        // 5. ApplyState で一括適用
        string stateJson = state.ToString(Newtonsoft.Json.Formatting.None);
        bool success = ApplyDesignToActor(stateJson, actorIndex);
        log.Information($"Glamourer TryApplyNpcAppearance on actor #{actorIndex} result: {success}");
        return success ? NpcApplyResult.Applied : NpcApplyResult.Failed;
    }

    public bool ApplyNpcAppearance(int actorIndex, byte[]? customizeData, ulong[]? equipmentModelIds, bool showHeadgear = true)
    {
        return TryApplyNpcAppearance(actorIndex, customizeData, equipmentModelIds, showHeadgear) == NpcApplyResult.Applied;
    }

    private static readonly (string Key, ulong EquipType)[] Slots =
    [
        ("Head",    1),
        ("Body",    2),
        ("Hands",   3),
        ("Legs",    4),
        ("Feet",    5),
        ("Ears",    6),
        ("Neck",    7),
        ("Wrists",  8),
        ("RFinger", 9),
        ("LFinger", 9),
    ];

    private const ulong CustomFlag = 1ul << 48;

    private static ulong CustomItemId(ushort model, byte variant, ulong equipType)
        => model | ((ulong)variant << 32) | (equipType << 40) | CustomFlag;

    private static void UnmanageWeaponSlot(JObject equip, string key)
    {
        if (equip[key] is not JObject slot) return;
        slot["Apply"] = false;
        slot["ApplyStain"] = false;
    }

    private static void SetHeadgearShown(JObject equip, bool show)
    {
        if (equip["Hat"] is JObject hat)
        {
            hat["Show"] = show;
            hat["Apply"] = true;
        }
    }

    private static void WriteEquipment(JObject equip, ulong[] pieces)
    {
        for (var i = 0; i < Slots.Length && i < pieces.Length; i++)
        {
            var (key, equipType) = Slots[i];
            if (equip[key] is not JObject slot) continue;

            ulong val = pieces[i];
            ushort model = (ushort)(val & 0xFFFF);
            byte variant = (byte)((val >> 16) & 0xFF);

            if (model == 0)
            {
                slot["ItemId"] = 0;
                slot["Apply"] = true;
                slot["Stain"] = 0;
                slot["Stain2"] = 0;
                slot["ApplyStain"] = true;
                continue;
            }

            slot["ItemId"] = CustomItemId(model, variant, equipType);
            slot["Apply"] = true;
            slot["Stain"] = 0;
            slot["Stain2"] = 0;
            slot["ApplyStain"] = true;
        }
    }

    private static readonly (string Key, int Byte, byte Mask)[] CustomizeMap =
    [
        ("Race",              0,  0xFF),
        ("Gender",            1,  0xFF),
        ("BodyType",          2,  0xFF),
        ("Height",            3,  0xFF),
        ("Clan",              4,  0xFF),
        ("Face",              5,  0xFF),
        ("Hairstyle",         6,  0xFF),
        ("Highlights",        7,  0x80),
        ("SkinColor",         8,  0xFF),
        ("EyeColorRight",     9,  0xFF),
        ("HairColor",         10, 0xFF),
        ("HighlightsColor",   11, 0xFF),
        ("FacialFeature1",    12, 0x01),
        ("FacialFeature2",    12, 0x02),
        ("FacialFeature3",    12, 0x04),
        ("FacialFeature4",    12, 0x08),
        ("FacialFeature5",    12, 0x10),
        ("FacialFeature6",    12, 0x20),
        ("FacialFeature7",    12, 0x40),
        ("LegacyTattoo",      12, 0x80),
        ("TattooColor",       13, 0xFF),
        ("Eyebrows",          14, 0xFF),
        ("EyeColorLeft",      15, 0xFF),
        ("EyeShape",          16, 0x7F),
        ("SmallIris",         16, 0x80),
        ("Nose",              17, 0xFF),
        ("Jaw",               18, 0xFF),
        ("Mouth",             19, 0x7F),
        ("Lipstick",          19, 0x80),
        ("LipColor",          20, 0xFF),
        ("MuscleMass",        21, 0xFF),
        ("TailShape",         22, 0xFF),
        ("BustSize",          23, 0xFF),
        ("FacePaint",         24, 0x7F),
        ("FacePaintReversed", 24, 0x80),
        ("FacePaintColor",    25, 0xFF),
    ];

    private static void WriteCustomize(JObject cust, byte[] c)
    {
        foreach (var (key, byteIdx, mask) in CustomizeMap)
        {
            if (byteIdx >= c.Length) continue;
            if (cust[key] is not JObject field) continue;
            field["Value"] = (byte)(c[byteIdx] & mask);
            field["Apply"] = true;
        }
    }

    /// <summary>
    /// アクターの Glamourer ステートをリセット・初期状態に戻す (Brio UnlockAndRevertCharacter 準拠)
    /// </summary>
    public bool RevertState(int actorIndex, string? actorName = null)
    {
        if (!IsAvailable) return false;
        bool ok = false;

        // 名前による Revert (Brio 準拠)
        if (!string.IsNullOrWhiteSpace(actorName) && revertStateNameV2Ulong != null)
        {
            try
            {
                int ecName = revertStateNameV2Ulong.InvokeFunc(actorName, 0, 7UL);
                log.Information($"Glamourer RevertStateName for '{actorName}' result: ec={ecName}");
                if (ecName == 0) ok = true;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer RevertStateName failed: {ex.Message}");
            }
        }

        try
        {
            if (revertStateV2Ulong != null)
            {
                int ec = revertStateV2Ulong.InvokeFunc(actorIndex, 0, 7UL);
                log.Information($"Glamourer RevertState (ulong) for actor #{actorIndex} result: ec={ec}");
                if (ec == 0) ok = true;
            }
            else if (revertStateV2Uint != null)
            {
                int ec = revertStateV2Uint.InvokeFunc(actorIndex, 0, 7U);
                log.Information($"Glamourer RevertState (uint) for actor #{actorIndex} result: ec={ec}");
                if (ec == 0) ok = true;
            }
        }
        catch (Exception ex)
        {
            log.Debug($"Glamourer RevertState failed: {ex.Message}");
        }

        try
        {
            if (revertToAutomationV2Ulong != null)
            {
                revertToAutomationV2Ulong.InvokeFunc(actorIndex, 0, 7UL);
            }
            else if (revertToAutomationV2Uint != null)
            {
                revertToAutomationV2Uint.InvokeFunc(actorIndex, 0, 7U);
            }
        }
        catch { }

        return ok;
    }

    /// <summary>
    /// アクターのステートロックを解除する
    /// </summary>
    public bool UnlockState(int actorIndex, string? actorName = null)
    {
        if (!IsAvailable) return false;

        if (!string.IsNullOrWhiteSpace(actorName) && unlockStateNameV2 != null)
        {
            try
            {
                unlockStateNameV2.InvokeFunc(actorName, 0);
            }
            catch { }
        }

        if (unlockStateV2 != null)
        {
            try
            {
                int ec = unlockStateV2.InvokeFunc(actorIndex, 0);
                return ec == 0;
            }
            catch (Exception ex)
            {
                log.Debug($"Glamourer UnlockState failed: {ex.Message}");
                return false;
            }
        }

        return false;
    }
}
