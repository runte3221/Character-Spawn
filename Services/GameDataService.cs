using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace CharacterSpawn.Services;

public class GameDataService
{
    private readonly IDataManager dataManager;

    public record NpcEntry(uint Id, string Name, uint ModelCharaId);
    public record MonsterEntry(uint Id, string Name, uint ModelCharaId);
    public record TimelineEntry(ushort Id, string Key, string Description, bool IsEmote);

    private List<NpcEntry>? cachedNpcs;
    private List<MonsterEntry>? cachedMonsters;
    private List<TimelineEntry>? cachedTimelines;
    private List<TimelineEntry>? cachedFacialExpressions;

    public GameDataService(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<NpcEntry> SearchNpcs(string query, int maxResults = 50)
    {
        cachedNpcs ??= BuildNpcCache();

        if (string.IsNullOrWhiteSpace(query))
            return cachedNpcs.Take(maxResults).ToList();

        return cachedNpcs
            .Where(n => n.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Id.ToString().Contains(query))
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<MonsterEntry> SearchMonsters(string query, int maxResults = 50)
    {
        cachedMonsters ??= BuildMonsterCache();

        if (string.IsNullOrWhiteSpace(query))
            return cachedMonsters.Take(maxResults).ToList();

        return cachedMonsters
            .Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || m.Id.ToString().Contains(query))
            .Take(maxResults)
            .ToList();
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

        // ActionTimeline for facial expressions typically have keys starting with "fac_"
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
