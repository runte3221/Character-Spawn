using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.UI;

public class CharacterLibraryTab
{
    private readonly Configuration configuration;
    private readonly GameDataService gameDataService;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser mcdfParser;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly IPluginLog log;

    // Creation form state
    private string newName = "New Character";
    private CharacterSourceType selectedSourceType = CharacterSourceType.PlayerClone;

    // Search filters
    private string npcSearchQuery = string.Empty;
    private string monsterSearchQuery = string.Empty;
    private GameDataService.NpcEntry? selectedNpc;
    private GameDataService.MonsterEntry? selectedMonster;

    // Glamourer / Penumbra state
    private string glamourerDesignInput = string.Empty;
    private string selectedPenumbraCollection = string.Empty;

    // MCDF state
    private string mcdfFilePath = string.Empty;

    public CharacterLibraryTab(
        Configuration configuration,
        GameDataService gameDataService,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc,
        McdfParser mcdfParser,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.gameDataService = gameDataService;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;
        this.mcdfParser = mcdfParser;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.log = log;
    }

    public void Draw(Action<CharacterTemplate> onSpawnRequested)
    {
        ImGui.Columns(2, "LibraryColumns", true);

        // Left Column: Saved Character Templates List
        DrawTemplateList(onSpawnRequested);

        ImGui.NextColumn();

        // Right Column: Character Creator (Step 1)
        DrawCharacterCreator();

        ImGui.Columns(1);
    }

    private void DrawTemplateList(Action<CharacterTemplate> onSpawnRequested)
    {
        ImGui.TextUnformatted("Saved Characters (Templates)");
        ImGui.Separator();

        if (configuration.Templates.Count == 0)
        {
            ImGui.TextDisabled("No character templates saved yet.");
            ImGui.TextWrapped("Create a new character template on the right panel.");
            return;
        }

        for (int i = 0; i < configuration.Templates.Count; i++)
        {
            var template = configuration.Templates[i];
            ImGui.PushID($"Template_{template.Id}");

            bool isSelected = false;
            if (ImGui.Selectable($"{template.Name} ({template.SourceType})", isSelected))
            {
                // Select to view or spawn
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Spawn onto Map"))
            {
                onSpawnRequested(template);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
            {
                configuration.Templates.RemoveAt(i);
                configuration.Save();
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }

    private void DrawCharacterCreator()
    {
        ImGui.TextUnformatted("Step 1: Create Character Template");
        ImGui.Separator();

        ImGui.InputText("Character Name", ref newName, 64);

        ImGui.Spacing();
        ImGui.TextUnformatted("Select Appearance Source:");

        int currentType = (int)selectedSourceType;
        string[] sourceNames = ["Glamourer & Penumbra", "Monster / Mob", "NPC (ENpc)", "MCDF File", "Clone Player/Target"];
        
        // Map to enum
        int comboIndex = selectedSourceType switch
        {
            CharacterSourceType.Glamourer => 0,
            CharacterSourceType.Monster => 1,
            CharacterSourceType.Npc => 2,
            CharacterSourceType.Mcdf => 3,
            _ => 4
        };

        if (ImGui.Combo("Source Type", ref comboIndex, sourceNames, sourceNames.Length))
        {
            selectedSourceType = comboIndex switch
            {
                0 => CharacterSourceType.Glamourer,
                1 => CharacterSourceType.Monster,
                2 => CharacterSourceType.Npc,
                3 => CharacterSourceType.Mcdf,
                _ => CharacterSourceType.PlayerClone
            };
        }

        ImGui.Separator();

        switch (selectedSourceType)
        {
            case CharacterSourceType.Glamourer:
                DrawGlamourerSection();
                break;
            case CharacterSourceType.Monster:
                DrawMonsterSection();
                break;
            case CharacterSourceType.Npc:
                DrawNpcSection();
                break;
            case CharacterSourceType.Mcdf:
                DrawMcdfSection();
                break;
            case CharacterSourceType.PlayerClone:
                DrawCloneSection();
                break;
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("Save to Library", new Vector2(-1, 32)))
        {
            SaveNewTemplate();
        }
    }

    private void DrawGlamourerSection()
    {
        ImGui.TextUnformatted("Glamourer & Penumbra Integration");

        if (glamourerIpc.IsAvailable)
        {
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "Glamourer IPC: Connected");
        }
        else
        {
            ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.2f, 1.0f), "Glamourer IPC: Not Detected");
        }

        ImGui.InputTextMultiline("Glamourer Design String / Code", ref glamourerDesignInput, 4096, new Vector2(-1, 80));

        if (penumbraIpc.IsAvailable)
        {
            var collections = penumbraIpc.GetCollections();
            if (collections.Count > 0)
            {
                var collectionNames = collections.Values.ToArray();
                int selectedIdx = Array.IndexOf(collectionNames, selectedPenumbraCollection);
                if (selectedIdx < 0) selectedIdx = 0;

                if (ImGui.Combo("Penumbra Collection", ref selectedIdx, collectionNames, collectionNames.Length))
                {
                    selectedPenumbraCollection = collectionNames[selectedIdx];
                }
            }
        }
    }

    private void DrawMonsterSection()
    {
        ImGui.InputText("Search Monsters", ref monsterSearchQuery, 64);
        var monsters = gameDataService.SearchMonsters(monsterSearchQuery, 10);

        if (ImGui.BeginListBox("##MonsterList", new Vector2(-1, 120)))
        {
            foreach (var m in monsters)
            {
                bool isSelected = selectedMonster?.Id == m.Id;
                if (ImGui.Selectable($"[{m.Id}] {m.Name}", isSelected))
                {
                    selectedMonster = m;
                    newName = m.Name;
                }
            }
            ImGui.EndListBox();
        }

        if (selectedMonster != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Selected: [{selectedMonster.Id}] {selectedMonster.Name}");
        }
    }

    private void DrawNpcSection()
    {
        ImGui.InputText("Search NPCs", ref npcSearchQuery, 64);
        var npcs = gameDataService.SearchNpcs(npcSearchQuery, 10);

        if (ImGui.BeginListBox("##NpcList", new Vector2(-1, 120)))
        {
            foreach (var n in npcs)
            {
                bool isSelected = selectedNpc?.Id == n.Id;
                if (ImGui.Selectable($"[{n.Id}] {n.Name}", isSelected))
                {
                    selectedNpc = n;
                    newName = n.Name;
                }
            }
            ImGui.EndListBox();
        }

        if (selectedNpc != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Selected: [{selectedNpc.Id}] {selectedNpc.Name}");
        }
    }

    private void DrawMcdfSection()
    {
        ImGui.InputText("MCDF File Path", ref mcdfFilePath, 260);

        if (ImGui.Button("Parse MCDF File"))
        {
            var parsed = mcdfParser.ParseMcdf(mcdfFilePath);
            if (parsed != null && !string.IsNullOrEmpty(parsed.GlamourerDesign))
            {
                glamourerDesignInput = parsed.GlamourerDesign;
                log.Information("Loaded Glamourer design from MCDF archive.");
            }
        }
    }

    private void DrawCloneSection()
    {
        ImGui.TextUnformatted("Clone Appearance from World Actor:");

        if (ImGui.Button("Copy from Local Player"))
        {
            var player = objectTable.Length > 0 ? objectTable[0] : null;
            if (player != null)
            {
                newName = $"{player.Name.ExtractText()} Clone";
                if (glamourerIpc.IsAvailable)
                {
                    glamourerDesignInput = glamourerIpc.GetCustomization(0) ?? string.Empty;
                }
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Copy from Current Target"))
        {
            var target = targetManager.Target;
            if (target != null)
            {
                newName = $"{target.Name.ExtractText()} Clone";
                if (glamourerIpc.IsAvailable)
                {
                    glamourerDesignInput = glamourerIpc.GetCustomization(target.ObjectIndex) ?? string.Empty;
                }
            }
        }
    }

    private void SaveNewTemplate()
    {
        var template = new CharacterTemplate
        {
            Name = string.IsNullOrWhiteSpace(newName) ? "Character" : newName,
            SourceType = selectedSourceType,
            GlamourerDesignString = glamourerDesignInput,
            PenumbraCollectionName = selectedPenumbraCollection,
            McdfFilePath = mcdfFilePath
        };

        if (selectedSourceType == CharacterSourceType.Monster && selectedMonster != null)
        {
            template.DataId = selectedMonster.Id;
            template.ModelCharaId = selectedMonster.ModelCharaId;
        }
        else if (selectedSourceType == CharacterSourceType.Npc && selectedNpc != null)
        {
            template.DataId = selectedNpc.Id;
            template.ModelCharaId = selectedNpc.ModelCharaId;
        }

        configuration.Templates.Add(template);
        configuration.Save();
        log.Information($"Saved template '{template.Name}' to library.");
    }
}
