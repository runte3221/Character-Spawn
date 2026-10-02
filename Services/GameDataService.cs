using System.Reflection;
using System.Text;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using CharacterSpawn.Managers;

namespace CharacterSpawn.Services;

public class GameDataService
{
    private readonly IDataManager dataManager;
    private readonly LogManager? logManager;

    public record NpcEntry(uint Id, string Name, uint ModelCharaId);
    public record MonsterEntry(uint Id, string Name, uint ModelCharaId, uint BaseId = 0, int McType = 3, float Scale = 1.0f);
    public record TimelineEntry(ushort Id, string Key, string Description, bool IsEmote);

    public record NpcAppearanceData(
        uint ModelCharaId,
        byte[]? CustomizeData,
        ulong[]? EquipmentModelIds,
        int McType = 1
    );

    private List<NpcEntry>? cachedNpcs;
    private List<MonsterEntry>? cachedMonsters;
    private List<TimelineEntry>? cachedTimelines;
    private List<TimelineEntry>? cachedFacialExpressions;

    public GameDataService(IDataManager dataManager, LogManager? logManager = null)
    {
        this.dataManager = dataManager;
        this.logManager = logManager;
    }

    public IReadOnlyList<NpcEntry> SearchNpcs(string query, int maxResults = 0)
    {
        cachedNpcs ??= BuildNpcCache();

        IEnumerable<NpcEntry> filtered = cachedNpcs;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Id.ToString().Contains(query));
        }

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public IReadOnlyList<MonsterEntry> SearchMonsters(string query, int maxResults = 0)
    {
        cachedMonsters ??= BuildMonsterCache();

        IEnumerable<MonsterEntry> filtered = cachedMonsters;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || m.Id.ToString().Contains(query));
        }

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public uint GetMonsterModelCharaId(uint bnpcNameId)
    {
        cachedMonsters ??= BuildMonsterCache();
        var match = cachedMonsters.FirstOrDefault(m => m.Id == bnpcNameId || m.BaseId == bnpcNameId);
        if (match != null && match.ModelCharaId > 0)
            return match.ModelCharaId;

        return 0;
    }

    public NpcAppearanceData? GetNpcAppearanceData(uint enpcId)
    {
        var baseSheet = dataManager.GetExcelSheet<ENpcBase>();
        if (baseSheet == null || !baseSheet.TryGetRow(enpcId, out var baseRow))
            return null;

        var modelChara = baseRow.ModelChara.ValueNullable;
        int mcType = modelChara != null ? (int)modelChara.Value.Type : 1;
        var modelCharaId = baseRow.ModelChara.RowId;

        // 非人型NPC（モーグリ等の特殊モデル / Demihuman）
        if (modelCharaId > 0 && mcType != 1)
        {
            ulong[]? demiEquip = null;
            if (baseRow.NpcEquip.RowId != 0)
            {
                var npcEquipSheet = dataManager.GetExcelSheet<NpcEquip>();
                if (npcEquipSheet != null && npcEquipSheet.TryGetRow(baseRow.NpcEquip.RowId, out var eqRow))
                {
                    demiEquip = [
                        eqRow.ModelHead, eqRow.ModelBody, eqRow.ModelHands, eqRow.ModelLegs, eqRow.ModelFeet,
                        eqRow.ModelEars, eqRow.ModelNeck, eqRow.ModelWrists, eqRow.ModelRightRing, eqRow.ModelLeftRing
                    ];
                }
            }
            return new NpcAppearanceData(modelCharaId, null, demiEquip, mcType);
        }

        // 人型NPC（ミューヌ等のHumanモデル）
        var cust = new byte[26];
        cust[0] = (byte)baseRow.Race.RowId;
        cust[1] = baseRow.Gender;
        cust[2] = baseRow.BodyType;
        cust[3] = baseRow.Height;
        cust[4] = (byte)baseRow.Tribe.RowId;
        cust[5] = baseRow.Face;
        cust[6] = baseRow.HairStyle;
        cust[7] = baseRow.HairHighlight;
        cust[8] = baseRow.SkinColor;
        cust[9] = baseRow.EyeHeterochromia;
        cust[10] = baseRow.HairColor;
        cust[11] = baseRow.HairHighlightColor;
        cust[12] = baseRow.FacialFeature;
        cust[13] = baseRow.FacialFeatureColor;
        cust[14] = baseRow.Eyebrows;
        cust[15] = baseRow.EyeColor;
        cust[16] = baseRow.EyeShape;
        cust[17] = baseRow.Nose;
        cust[18] = baseRow.Jaw;
        cust[19] = baseRow.Mouth;
        cust[20] = baseRow.LipColor;
        cust[21] = baseRow.BustOrTone1;
        cust[22] = baseRow.ExtraFeature1;
        cust[23] = baseRow.ExtraFeature2OrBust;
        cust[24] = baseRow.FacePaint;
        cust[25] = baseRow.FacePaintColor;

        var equip = new ulong[10];
        if (baseRow.NpcEquip.RowId != 0)
        {
            var npcEquipSheet = dataManager.GetExcelSheet<NpcEquip>();
            if (npcEquipSheet != null && npcEquipSheet.TryGetRow(baseRow.NpcEquip.RowId, out var eqRow))
            {
                equip[0] = eqRow.ModelHead;
                equip[1] = eqRow.ModelBody;
                equip[2] = eqRow.ModelHands;
                equip[3] = eqRow.ModelLegs;
                equip[4] = eqRow.ModelFeet;
                equip[5] = eqRow.ModelEars;
                equip[6] = eqRow.ModelNeck;
                equip[7] = eqRow.ModelWrists;
                equip[8] = eqRow.ModelRightRing;
                equip[9] = eqRow.ModelLeftRing;
            }
        }
        else
        {
            equip[0] = baseRow.ModelHead;
            equip[1] = baseRow.ModelBody;
            equip[2] = baseRow.ModelHands;
            equip[3] = baseRow.ModelLegs;
            equip[4] = baseRow.ModelFeet;
            equip[5] = baseRow.ModelEars;
            equip[6] = baseRow.ModelNeck;
            equip[7] = baseRow.ModelWrists;
            equip[8] = baseRow.ModelRightRing;
            equip[9] = baseRow.ModelLeftRing;
        }

        return new NpcAppearanceData(0, cust, equip);
    }

    public IReadOnlyList<TimelineEntry> SearchTimelines(string query, int maxResults = 0)
    {
        cachedTimelines ??= BuildTimelineCache();

        IEnumerable<TimelineEntry> filtered = cachedTimelines;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(t => t.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           t.Id.ToString().Contains(query));
        }

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public IReadOnlyList<TimelineEntry> GetFacialExpressions()
    {
        return cachedFacialExpressions ??= BuildFacialExpressionCache();
    }

    private List<NpcEntry> BuildNpcCache()
    {
        var list = new List<NpcEntry>();
        var sheet = dataManager.GetExcelSheet<ENpcResident>();
        var baseSheet = dataManager.GetExcelSheet<ENpcBase>();

        if (sheet == null) return list;

        foreach (var row in sheet)
        {
            var name = row.Singular.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;

            uint modelChara = 0;
            if (baseSheet != null && baseSheet.TryGetRow(row.RowId, out var baseRow))
            {
                modelChara = baseRow.ModelChara.RowId;
            }

            list.Add(new NpcEntry(row.RowId, name, modelChara));
        }

        logManager?.Info($"Built ENpc cache: {list.Count} NPCs loaded.");
        return list;
    }

    private List<MonsterEntry> BuildMonsterCache()
    {
        var list = new List<MonsterEntry>();
        var nameSheet = dataManager.GetExcelSheet<BNpcName>();

        Stream? stream = null;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("mob-model-index.csv", StringComparison.OrdinalIgnoreCase));

            if (resourceName != null)
            {
                stream = assembly.GetManifestResourceStream(resourceName);
            }

            if (stream == null)
            {
                var localPath = Path.Combine(AppContext.BaseDirectory, "Resources", "mob-model-index.csv");
                if (File.Exists(localPath))
                {
                    stream = File.OpenRead(localPath);
                }
            }

            if (stream != null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                reader.ReadLine(); // header
                string? line;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 9) continue;

                    if (!uint.TryParse(parts[0], out var baseId)) continue;
                    uint.TryParse(parts[1], out var nameId);
                    var csvName = parts[2].Trim();
                    if (!uint.TryParse(parts[3], out var modelCharaId) || modelCharaId == 0) continue;
                    int.TryParse(parts[4], out var mcType);
                    float.TryParse(parts[8], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale);
                    if (scale <= 0.01f) scale = 1.0f;

                    // 日本語名解決: BNpcName シートから優先取得
                    string displayName = "";
                    if (nameSheet != null && nameId > 0 && nameSheet.TryGetRow(nameId, out var nameRow))
                    {
                        var jp = nameRow.Singular.ExtractText();
                        if (!string.IsNullOrWhiteSpace(jp))
                            displayName = jp;
                    }
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = csvName;
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = $"Monster #{baseId}";

                    // (DisplayName, ModelCharaId, McType) の重複排除
                    string dedupKey = $"{displayName}_{modelCharaId}_{mcType}";
                    if (!seen.Add(dedupKey)) continue;

                    list.Add(new MonsterEntry(
                        nameId > 0 ? nameId : baseId,
                        displayName,
                        modelCharaId,
                        baseId,
                        mcType > 0 ? mcType : 3,
                        scale
                    ));
                }

                logManager?.Info($"Built Monster cache: {list.Count} monsters loaded from mob-model-index.csv.");
                return list;
            }
            else
            {
                logManager?.Warning("mob-model-index.csv could not be loaded from embedded resources or Resources directory.");
            }
        }
        catch (Exception ex)
        {
            logManager?.Error($"Failed to load mob-model-index.csv: {ex.Message}");
        }
        finally
        {
            stream?.Dispose();
        }

        // フォールバック: BNpcName シートから直接スキャン
        if (nameSheet != null)
        {
            var baseSheet = dataManager.GetExcelSheet<BNpcBase>();
            foreach (var row in nameSheet)
            {
                var name = row.Singular.ExtractText();
                if (string.IsNullOrWhiteSpace(name)) continue;

                uint modelChara = 0;
                if (baseSheet != null && baseSheet.TryGetRow(row.RowId, out var fallbackRow))
                {
                    modelChara = fallbackRow.ModelChara.RowId;
                }

                if (modelChara == 0) continue;
                list.Add(new MonsterEntry(row.RowId, name, modelChara));
            }
        }

        logManager?.Info($"Built Monster fallback cache: {list.Count} monsters loaded.");
        return list;
    }

    private List<TimelineEntry> BuildTimelineCache()
    {
        var list = new List<TimelineEntry>();
        var sheet = dataManager.GetExcelSheet<ActionTimeline>();
        var emoteSheet = dataManager.GetExcelSheet<Emote>();

        if (sheet == null) return list;

        var emoteMap = new Dictionary<ushort, string>();
        if (emoteSheet != null)
        {
            foreach (var emote in emoteSheet)
            {
                var emoteName = emote.Name.ExtractText();
                if (string.IsNullOrEmpty(emoteName)) continue;

                var animRow = emote.ActionTimeline.Count > 0 ? emote.ActionTimeline[0].RowId : 0;
                if (animRow > 0 && !emoteMap.ContainsKey((ushort)animRow))
                {
                    emoteMap[(ushort)animRow] = emoteName;
                }
            }
        }

        foreach (var row in sheet)
        {
            var key = row.Key.ExtractText();
            if (string.IsNullOrEmpty(key)) continue;

            var id = (ushort)row.RowId;
            var isEmote = emoteMap.TryGetValue(id, out var emoteName);
            var desc = isEmote ? $"[Emote] {emoteName}" : $"ActionTimeline {id}";

            list.Add(new TimelineEntry(id, key, desc, isEmote));
        }

        return list;
    }

    private List<TimelineEntry> BuildFacialExpressionCache()
    {
        var list = new List<TimelineEntry>();
        var sheet = dataManager.GetExcelSheet<ActionTimeline>();
        if (sheet == null) return list;

        foreach (var row in sheet)
        {
            var key = row.Key.ExtractText();
            if (string.IsNullOrEmpty(key)) continue;

            if (key.StartsWith("fac_", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new TimelineEntry((ushort)row.RowId, key, $"Facial: {key}", false));
            }
        }

        return list;
    }
}
