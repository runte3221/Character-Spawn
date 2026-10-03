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
    public record MountMinionEntry(uint Id, string Name, uint ModelCharaId, uint IconId, bool IsMount, float Scale = 1.0f);
    public record TimelineEntry(ushort Id, string Key, string Description, bool IsEmote);

    public record NpcAppearanceData(
        uint ModelCharaId,
        byte[]? CustomizeData,
        ulong[]? EquipmentModelIds,
        int McType = 1
    );

    private List<NpcEntry>? cachedNpcs;
    private List<MonsterEntry>? cachedMonsters;
    private List<MountMinionEntry>? cachedCompanions;
    private List<MountMinionEntry>? cachedMounts;
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

    public IReadOnlyList<MountMinionEntry> SearchCompanions(string query, int maxResults = 0)
    {
        cachedCompanions ??= BuildCompanionCache();

        IEnumerable<MountMinionEntry> filtered = cachedCompanions;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           c.Id.ToString().Contains(query) ||
                                           c.ModelCharaId.ToString().Contains(query));
        }

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public IReadOnlyList<MountMinionEntry> SearchMounts(string query, int maxResults = 0)
    {
        cachedMounts ??= BuildMountCache();

        IEnumerable<MountMinionEntry> filtered = cachedMounts;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           m.Id.ToString().Contains(query) ||
                                           m.ModelCharaId.ToString().Contains(query));
        }

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public uint GetMonsterModelCharaId(uint bnpcBaseId)
    {
        cachedMonsters ??= BuildMonsterCache();
        var match = cachedMonsters.FirstOrDefault(m => m.BaseId == bnpcBaseId || m.Id == bnpcBaseId);
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
            else
            {
                demiEquip = [
                    baseRow.ModelHead, baseRow.ModelBody, baseRow.ModelHands, baseRow.ModelLegs, baseRow.ModelFeet,
                    baseRow.ModelEars, baseRow.ModelNeck, baseRow.ModelWrists, baseRow.ModelRightRing, baseRow.ModelLeftRing
                ];
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

    public IReadOnlyList<TimelineEntry> SearchTimelines(string query, string category = "All", string? modelPrefix = null, int maxResults = 0, HashSet<ushort>? favoriteIds = null)
    {
        cachedTimelines ??= BuildTimelineCache();

        IEnumerable<TimelineEntry> filtered = cachedTimelines;

        // モデル固有プレフィックスによる絞り込み (例: "m0024", "d1016")
        if (!string.IsNullOrWhiteSpace(modelPrefix))
        {
            string numPart = modelPrefix.Length > 1 && char.IsLetter(modelPrefix[0]) ? modelPrefix.Substring(1) : modelPrefix;
            filtered = filtered.Where(t => t.Key.Contains(modelPrefix, StringComparison.OrdinalIgnoreCase) ||
                                           (!string.IsNullOrEmpty(numPart) && t.Key.Contains(numPart, StringComparison.OrdinalIgnoreCase)) ||
                                           IsCommonMonsterAction(t.Key));
        }

        // カテゴリ絞り込み
        if (!string.IsNullOrWhiteSpace(category) && category != "All")
        {
            if (category == "Favorite")
                filtered = filtered.Where(t => favoriteIds != null && favoriteIds.Contains(t.Id));
            else if (category == "Emotes")
                filtered = filtered.Where(t => t.IsEmote);
            else if (category == "NPC")
                filtered = filtered.Where(t => t.Description.StartsWith("[NPC]"));
            else if (category == "Monster")
                filtered = filtered.Where(t => t.Description.StartsWith("[Monster]"));
            else if (category == "Battle")
                filtered = filtered.Where(t => t.Description.StartsWith("[Battle]"));
            else if (category == "General")
                filtered = filtered.Where(t => t.Description.StartsWith("[General]"));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(t => t.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           t.Id.ToString().Contains(query));
        }

        // お気に入り優先、その後エモート優先、その後 ID 順
        filtered = filtered.OrderByDescending(t => favoriteIds != null && favoriteIds.Contains(t.Id))
                           .ThenByDescending(t => t.IsEmote)
                           .ThenBy(t => t.Id);

        return maxResults > 0 ? filtered.Take(maxResults).ToList() : filtered.ToList();
    }

    public string? GetModelPrefix(uint modelCharaId)
    {
        if (modelCharaId == 0) return null;
        try
        {
            var sheet = dataManager.GetExcelSheet<ModelChara>();
            if (sheet != null && sheet.TryGetRow(modelCharaId, out var row))
            {
                // Type 1: Human (c****), Type 2: DemiHuman (d****), Type 3: Monster (m****)
                char pfx = row.Type switch
                {
                    2 => 'd',
                    3 => 'm',
                    _ => 'm'
                };
                return $"{pfx}{row.Model:D4}";
            }
        }
        catch { }
        return null;
    }

    public uint GetModelNumber(uint modelCharaId)
    {
        if (modelCharaId == 0) return 0;
        try
        {
            var sheet = dataManager.GetExcelSheet<ModelChara>();
            if (sheet != null && sheet.TryGetRow(modelCharaId, out var row))
            {
                return row.Model;
            }
        }
        catch { }
        return 0;
    }

    public static bool IsCommonMonsterAction(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        // 基本待機・移動
        if (key.StartsWith("normal/idle", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("normal/walk", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("normal/run", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("normal/sprint", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("normal/turn", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("normal/bt_idle", StringComparison.OrdinalIgnoreCase))
            return true;

        // 戦闘待機・通常攻撃(auto_attack)・モンスター汎用特殊技
        if (key.StartsWith("battle/idle", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("battle/battle_start", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("battle/battle_end", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("battle/auto_attack", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("battle/mon_sp_", StringComparison.OrdinalIgnoreCase))
            return true;

        // 被弾(damage)・死亡(dead)
        if (key.Contains("damage", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("dead", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("specialdead/", StringComparison.OrdinalIgnoreCase))
            return true;

        // ジャンプ・威嚇・咆哮・察知
        if (key.StartsWith("jump/", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("notice/", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("roar/", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("threat/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public ulong[]? GetDemiHumanEquipment(uint modelCharaId)
    {
        if (modelCharaId == 0) return null;
        try
        {
            var sheet = dataManager.GetExcelSheet<ModelChara>();
            if (sheet != null && sheet.TryGetRow(modelCharaId, out var row))
            {
                // Type == 2 (DemiHuman: サキュバス、ゴブリン、シルフ、コボルド、サハギン等)
                if (row.Type == 2)
                {
                    var equip = new ulong[10];
                    // EquipmentModelId のビット構成: Id (16-bit) | (Variant << 16) | (Dye << 24)
                    // DemiHuman のモデルIDは row.Base (例: サキュバスは Base: 1 -> e0001)
                    ulong val = ((ulong)row.Base) | ((ulong)row.Variant << 16);

                    // サキュバス(1016), スケルトン(1015) などは胴(Body / slot 1: top)のみの一体型モデル
                    // 一体型デミヒューマンは Body スロットにのみ値を設定し、他スロットは 0 とする
                    // （存在しない met, glv, dwn, sho の読み込み失敗によるギズモ化を完全に解消）
                    if (row.Model == 1016 || row.Model == 1015 || row.Model == 1005)
                    {
                        equip[1] = val; // Body (Top)
                    }
                    else
                    {
                        equip[0] = val; // Head
                        equip[1] = val; // Body
                        equip[2] = val; // Hands
                        equip[3] = val; // Legs
                        equip[4] = val; // Feet
                    }
                    return equip;
                }
            }
        }
        catch { }
        return null;
    }

    public IReadOnlyList<TimelineEntry> GetFacialExpressions()
    {
        return cachedFacialExpressions ??= BuildFacialExpressionCache();
    }

    public NpcAppearanceData? ResolveNpcAppearance(uint enpcId, string? name = null)
    {
        var app = GetNpcAppearanceData(enpcId);
        // 有効なアピアランス（非人型、または人型でRace > 0）であればそのまま返す
        if (app != null && (app.ModelCharaId > 0 || (app.CustomizeData != null && app.CustomizeData.Length > 0 && app.CustomizeData[0] > 0)))
        {
            return app;
        }

        // ResidentId と BaseId の乖離、またはデータ不整合の場合、NPC名から正しい BaseId を逆引き解決 (HDM準拠)
        if (!string.IsNullOrWhiteSpace(name))
        {
            cachedNpcs ??= BuildNpcCache();
            var matched = cachedNpcs.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                       ?? cachedNpcs.FirstOrDefault(n => n.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (matched != null && matched.Id != enpcId)
            {
                var resolvedApp = GetNpcAppearanceData(matched.Id);
                if (resolvedApp != null)
                {
                    logManager?.Info($"ResolveNpcAppearance: Remapped '{name}' from ID {enpcId} to correct ENpcBaseId {matched.Id}.");
                    return resolvedApp;
                }
            }
        }

        return app;
    }

    private List<NpcEntry> BuildNpcCache()
    {
        var list = new List<NpcEntry>();
        var baseSheet = dataManager.GetExcelSheet<ENpcBase>();
        var residentSheet = dataManager.GetExcelSheet<ENpcResident>();

        if (baseSheet == null) return list;

        // HDM (EventNpcIndex.cs) 準拠:
        // ENpcBase を主軸に走査し、ID空間を ENpcBase.RowId (BaseId) で統一する
        foreach (var baseRow in baseSheet)
        {
            // 名前を ENpcResident から解決
            string name = string.Empty;
            if (residentSheet != null && residentSheet.TryGetRow(baseRow.RowId, out var resRow))
            {
                name = resRow.Singular.ExtractText().Trim();
            }

            if (string.IsNullOrWhiteSpace(name)) continue;

            var modelChara = baseRow.ModelChara.ValueNullable;
            uint modelCharaId = baseRow.ModelChara.RowId;
            int mcType = modelChara != null ? (int)modelChara.Value.Type : 1;

            // 人型NPC (mcType == 1): 有効な人間種族データを持っているか検証 (HDM ValidHuman 準拠)
            if (modelCharaId == 0 || mcType == 1)
            {
                uint race = baseRow.Race.RowId;
                uint tribe = baseRow.Tribe.RowId;
                byte gender = baseRow.Gender;
                if (race < 1 || race > 8 || gender > 1 || tribe < 1 || tribe > 16)
                    continue;

                list.Add(new NpcEntry(baseRow.RowId, name, 0));
            }
            else
            {
                // 非人型 / デミヒューマン (レターモーグリ等)
                list.Add(new NpcEntry(baseRow.RowId, name, modelCharaId));
            }
        }

        logManager?.Info($"Built ENpc cache (HDM ENpcBase-indexed): {list.Count} NPCs loaded.");
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
                    var parts = ParseCsvLine(line);
                    if (parts.Count < 9) continue;

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
                        baseId,
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

    /// <summary>
    /// クォート文字（"..."）内のカンマを保護する RFC 4180 / HDM 準拠の CSV 行パーサー
    /// </summary>
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim(' ', '\t', '"'));
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim(' ', '\t', '"'));
        return result;
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
            string desc;

            if (isEmote)
            {
                desc = $"[Emote] {emoteName}";
            }
            else if (key.StartsWith("human_sp/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("event_base/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("event/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("emote_sp/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("speak/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("resident/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("idle_sp/", StringComparison.OrdinalIgnoreCase))
            {
                desc = $"[NPC] {key}";
            }
            else if (key.StartsWith("mon_sp/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("battle/mon_sp_", StringComparison.OrdinalIgnoreCase))
            {
                desc = $"[Monster] {key}";
            }
            else if (key.StartsWith("battle/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("ability/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("ws/", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("magic/", StringComparison.OrdinalIgnoreCase))
            {
                desc = $"[Battle] {key}";
            }
            else
            {
                desc = $"[General] {key}";
            }

            if (key.Contains("1016", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("succubus", StringComparison.OrdinalIgnoreCase))
            {
                logManager?.Info($"[SuccubusTimeline] RowId: {id}, Key: '{key}', Slot: {row.Slot}, Desc: '{desc}'");
            }

            list.Add(new TimelineEntry(id, key, desc, isEmote));
        }

        return list;
    }

    private List<TimelineEntry> BuildFacialExpressionCache()
    {
        var list = new List<TimelineEntry>();
        var emoteSheet = dataManager.GetExcelSheet<Emote>();
        if (emoteSheet == null) return list;

        foreach (var emote in emoteSheet)
        {
            if (emote.EmoteCategory.RowId != 3) continue;

            string name = emote.Name.ExtractText().Trim();
            if (string.IsNullOrEmpty(name)) continue;

            ushort timelineId = emote.ActionTimeline.Count > 0 ? (ushort)emote.ActionTimeline[0].RowId : (ushort)0;
            if (timelineId == 0) continue;

            list.Add(new TimelineEntry(timelineId, name, name, true));
        }

        // 素顔(604)を最優先、それ以外は名前順
        return list.OrderBy(t => t.Id == 604 ? 0 : 1).ThenBy(t => t.Description).ToList();
    }

    public string GetTerritoryName(uint territoryId)
    {
        if (territoryId == 0) return "制限なし (どこでも)";
        try
        {
            var sheet = dataManager.GetExcelSheet<TerritoryType>();
            if (sheet != null && sheet.TryGetRow(territoryId, out var row))
            {
                var placeName = row.PlaceName.ValueNullable?.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(placeName))
                    return placeName;

                var placeZone = row.PlaceNameZone.ValueNullable?.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(placeZone))
                    return placeZone;
            }
        }
        catch { }

        return $"エリア {territoryId}";
    }

    private List<MountMinionEntry> BuildCompanionCache()
    {
        var list = new List<MountMinionEntry>();
        var sheet = dataManager.GetExcelSheet<Companion>();
        if (sheet == null) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet)
        {
            string name = row.Singular.ExtractText().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            uint modelCharaId = row.Model.RowId;
            if (modelCharaId == 0) continue;

            float scale = row.Scale > 0 ? (row.Scale > 10 ? row.Scale / 100f : row.Scale) : 1.0f;
            if (scale <= 0.01f) scale = 1.0f;

            string dedupKey = $"{name}_{modelCharaId}";
            if (!seen.Add(dedupKey)) continue;

            list.Add(new MountMinionEntry(row.RowId, name, modelCharaId, row.Icon, false, scale));
        }

        logManager?.Info($"Built Companion cache: {list.Count} minions.");
        return list;
    }

    private List<MountMinionEntry> BuildMountCache()
    {
        var list = new List<MountMinionEntry>();
        var sheet = dataManager.GetExcelSheet<Mount>();
        if (sheet == null) return list;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet)
        {
            string name = row.Singular.ExtractText().Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            uint modelCharaId = row.ModelChara.RowId;
            if (modelCharaId == 0) continue;

            string dedupKey = $"{name}_{modelCharaId}";
            if (!seen.Add(dedupKey)) continue;

            list.Add(new MountMinionEntry(row.RowId, name, modelCharaId, row.Icon, true, 1.0f));
        }

        logManager?.Info($"Built Mount cache: {list.Count} mounts.");
        return list;
    }
}
