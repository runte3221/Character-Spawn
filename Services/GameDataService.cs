using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CharacterSpawn.Services;

public class GameDataService
{
    private readonly IDataManager dataManager;

    public record NpcEntry(uint Id, string Name, uint ModelCharaId);
    public record MonsterEntry(uint Id, string Name, uint ModelCharaId);
    public record TimelineEntry(ushort Id, string Key, string Description, bool IsEmote);

    public record NpcAppearanceData(
        uint ModelCharaId,
        byte[]? CustomizeData,
        ulong[]? EquipmentModelIds
    );

    private List<NpcEntry>? cachedNpcs;
    private List<MonsterEntry>? cachedMonsters;
    private List<TimelineEntry>? cachedTimelines;
    private List<TimelineEntry>? cachedFacialExpressions;

    public GameDataService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<NpcEntry> SearchNpcs(string query, int maxResults = 500)
    {
        cachedNpcs ??= BuildNpcCache();

        if (string.IsNullOrWhiteSpace(query))
            return cachedNpcs.Take(maxResults).ToList();

        return cachedNpcs
            .Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Id.ToString().Contains(query))
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<MonsterEntry> SearchMonsters(string query, int maxResults = 500)
    {
        cachedMonsters ??= BuildMonsterCache();

        if (string.IsNullOrWhiteSpace(query))
            return cachedMonsters.Take(maxResults).ToList();

        return cachedMonsters
            .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || m.Id.ToString().Contains(query))
            .Take(maxResults)
            .ToList();
    }

    public NpcAppearanceData? GetNpcAppearanceData(uint enpcId)
    {
        var baseSheet = dataManager.GetExcelSheet<ENpcBase>();
        if (baseSheet == null || !baseSheet.TryGetRow(enpcId, out var baseRow))
            return null;

        var modelCharaId = baseRow.ModelChara.RowId;
        if (modelCharaId > 0)
        {
            // 非人型NPC（モーグリ等の特殊モデル）
            return new NpcAppearanceData(modelCharaId, null, null);
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

    public IReadOnlyList<TimelineEntry> SearchTimelines(string query, int maxResults = 50)
    {
        cachedTimelines ??= BuildTimelineCache();

        if (string.IsNullOrWhiteSpace(query))
            return cachedTimelines.Take(maxResults).ToList();

        return cachedTimelines
            .Where(t => t.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Id.ToString().Contains(query))
            .Take(maxResults)
            .ToList();
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

        return list;
    }

    private List<MonsterEntry> BuildMonsterCache()
    {
        var list = new List<MonsterEntry>();
        var sheet = dataManager.GetExcelSheet<BNpcName>();
        var baseSheet = dataManager.GetExcelSheet<BNpcBase>();

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

            list.Add(new MonsterEntry(row.RowId, name, modelChara));
        }

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
