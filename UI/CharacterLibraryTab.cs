using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;
using CharacterSpawn.Services;
using CharacterSpawn.Managers;

namespace CharacterSpawn.UI;

public class CharacterLibraryTab
{
    private readonly Configuration configuration;
    private readonly GameDataService gameDataService;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser mcdfParser;
    private readonly ActorManager actorManager;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly IPluginLog log;
    private readonly LogManager? logManager;

    // Selection state
    private CharacterTemplate? selectedTemplate;
    private string? selectedFolder;
    private string editInlineName = string.Empty;

    // Modal state (New / Edit)
    private bool isModalOpen = false;
    private bool isEditing = false;
    private CharacterTemplate editingTemplate = new();

    // Modal fields
    private string modalName = string.Empty;
    private string modalFolder = string.Empty;
    private CharacterSourceType modalSourceType = CharacterSourceType.Glamourer;

    // Glamourer & Penumbra modal fields
    private string glamourerSearch = string.Empty;
    private string selectedGlamourerDesignGuid = string.Empty;
    private string selectedGlamourerDesignName = string.Empty;
    private string customGlamourerString = string.Empty;
    private string penumbraSearch = string.Empty;
    private string selectedPenumbraCollection = string.Empty;

    // Monster modal fields
    private string monsterSearchQuery = string.Empty;
    private GameDataService.MonsterEntry? modalSelectedMonster;

    // NPC modal fields
    private string npcSearchQuery = string.Empty;
    private GameDataService.NpcEntry? modalSelectedNpc;
    private GameDataService.NpcAppearanceData? cachedNpcAppearance;

    // MCDF modal fields
    private string modalMcdfPath = string.Empty;

    // Folder creation popup state
    private bool openNewFolderPopup = false;
    private string newFolderName = string.Empty;

    public CharacterLibraryTab(
        Configuration configuration,
        GameDataService gameDataService,
        GlamourerIpc glamourerIpc,
        PenumbraIpc penumbraIpc,
        McdfParser mcdfParser,
        ActorManager actorManager,
        IObjectTable objectTable,
        ITargetManager targetManager,
        IPluginLog log,
        LogManager? logManager = null)
    {
        this.configuration = configuration;
        this.gameDataService = gameDataService;
        this.glamourerIpc = glamourerIpc;
        this.penumbraIpc = penumbraIpc;
        this.mcdfParser = mcdfParser;
        this.actorManager = actorManager;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.log = log;
        this.logManager = logManager;
    }

    public void Draw()
    {
        // Split view: Left = Tree pane, Right = Detail pane
        ImGui.Columns(2, "LibraryMainColumns", true);

        // --- LEFT PANE: Tree View & Action Buttons ---
        DrawLeftPane();

        ImGui.NextColumn();

        // --- RIGHT PANE: Selected Character Detail & Preview ---
        DrawRightPane();

        ImGui.Columns(1);

        // Modals / Popups
        DrawCharacterModal();
        DrawNewFolderPopup();
    }

    private void DrawLeftPane()
    {
        var contentHeight = ImGui.GetContentRegionAvail().Y - (ImGui.GetFrameHeightWithSpacing() + 8f);
        if (ImGui.BeginChild("LibraryTreeScroll", new Vector2(-1, contentHeight), true))
        {
            var allFolders = new HashSet<string>(configuration.Folders);
            foreach (var t in configuration.Templates)
            {
                if (!string.IsNullOrWhiteSpace(t.FolderPath))
                    allFolders.Add(t.FolderPath);
            }
            var sortedFolders = allFolders.OrderBy(f => f).ToList();

            // 1. Root level characters (no folder)
            var rootTemplates = configuration.Templates.Where(t => string.IsNullOrWhiteSpace(t.FolderPath)).ToList();
            foreach (var template in rootTemplates)
            {
                DrawCharacterLeaf(template);
            }

            // 2. Folders and their characters
            foreach (var folder in sortedFolders)
            {
                bool isFolderSelected = selectedFolder == folder && selectedTemplate == null;
                ImGuiTreeNodeFlags folderFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                if (isFolderSelected) folderFlags |= ImGuiTreeNodeFlags.Selected;

                bool folderOpen = ImGui.TreeNodeEx($"folder_{folder}", folderFlags, $"📁 {folder}");
                if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                {
                    selectedFolder = folder;
                    selectedTemplate = null;
                }

                if (folderOpen)
                {
                    var folderTemplates = configuration.Templates.Where(t => t.FolderPath == folder).ToList();
                    foreach (var template in folderTemplates)
                    {
                        DrawCharacterLeaf(template);
                    }
                    ImGui.TreePop();
                }
            }

            ImGui.EndChild();
        }

        // Action Buttons at bottom of Left Pane: [New Chara] [New Folder] [Delete]
        float buttonSpacing = ImGui.GetStyle().ItemSpacing.X;
        float totalWidth = ImGui.GetContentRegionAvail().X;
        float btnWidth = (totalWidth - (buttonSpacing * 2)) / 3f;

        if (ImGui.Button("New Chara", new Vector2(btnWidth, 0)))
        {
            OpenNewCharacterModal();
        }

        ImGui.SameLine();

        if (ImGui.Button("New Folder", new Vector2(btnWidth, 0)))
        {
            newFolderName = string.Empty;
            openNewFolderPopup = true;
        }

        ImGui.SameLine();

        if (ImGui.Button("Delete", new Vector2(btnWidth, 0)))
        {
            DeleteSelected();
        }
    }

    private void DrawCharacterLeaf(CharacterTemplate template)
    {
        bool isSelected = selectedTemplate?.Id == template.Id;
        ImGuiTreeNodeFlags leafFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isSelected) leafFlags |= ImGuiTreeNodeFlags.Selected;

        string icon = template.SourceType switch
        {
            CharacterSourceType.Monster => "👾",
            CharacterSourceType.Npc => "👤",
            CharacterSourceType.Glamourer => "✨",
            CharacterSourceType.Mcdf => "📦",
            _ => "🎮"
        };

        ImGui.TreeNodeEx($"chara_{template.Id}", leafFlags, $"{icon} {template.Name}");
        if (ImGui.IsItemClicked())
        {
            selectedTemplate = template;
            selectedFolder = template.FolderPath;
            editInlineName = template.Name;
        }
    }

    private void DrawRightPane()
    {
        if (selectedTemplate == null)
        {
            ImGui.TextDisabled("Select a character from the list, or click [New Chara] to create one.");
            return;
        }

        // Chara Name
        ImGui.TextUnformatted("Chara Name");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##InlineCharaName", ref editInlineName, 64))
        {
            selectedTemplate.Name = editInlineName;
            configuration.Save();
        }

        ImGui.Spacing();

        // [x] Weapon Visible
        bool weaponVis = selectedTemplate.WeaponVisible;
        if (ImGui.Checkbox("Weapon Visible", ref weaponVis))
        {
            selectedTemplate.WeaponVisible = weaponVis;
            configuration.Save();

            // もしプレビュー中なら武器非表示をリアルタイム反映
            if (actorManager.CurrentPreviewActor != null && actorManager.CurrentPreviewActor.NativeAddress != 0)
            {
                unsafe
                {
                    var nativeChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actorManager.CurrentPreviewActor.NativeAddress;
                    if (!weaponVis)
                    {
                        nativeChara->DrawData.HideWeapons();
                    }
                    else
                    {
                        nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterSetupContainer.CopyFlags.None);
                    }
                }
            }
        }
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "  <= 武器表示ON/OFF");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // プレビュー状態の判定
        bool isPreviewingCurrent = actorManager.CurrentPreviewActor != null &&
                                  actorManager.CurrentPreviewActor.TemplateId == selectedTemplate.Id &&
                                  actorManager.CurrentPreviewActor.IsSpawned;

        // Action buttons: [Spawn / Despawn] [edit] [delete]
        if (isPreviewingCurrent)
        {
            // 赤色 Despawn ボタン
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.15f, 0.15f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.25f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));

            if (ImGui.Button("Despawn", new Vector2(90, 26)))
            {
                actorManager.DespawnPreviewCharacter();
            }

            ImGui.PopStyleColor(3);
        }
        else
        {
            // 通常 Spawn ボタン
            if (ImGui.Button("Spawn", new Vector2(90, 26)))
            {
                logManager?.Info($"Preview Spawn requested for template: {selectedTemplate.Name}");
                actorManager.SpawnPreviewCharacter(selectedTemplate);
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("edit", new Vector2(70, 26)))
        {
            OpenEditCharacterModal(selectedTemplate);
        }

        ImGui.SameLine();

        if (ImGui.Button("delete", new Vector2(70, 26)))
        {
            if (isPreviewingCurrent)
            {
                actorManager.DespawnPreviewCharacter();
            }
            configuration.Templates.Remove(selectedTemplate);
            configuration.Save();
            selectedTemplate = null;
            return;
        }

        ImGui.Spacing();

        // スポーン状態に応じた表示（画像3, 4準拠）
        if (isPreviewingCurrent)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.3f, 1.0f), $"{selectedTemplate.Name} Spawning...");

            ImGui.Spacing();

            bool showGizmo = configuration.ShowGizmo;
            if (ImGui.Checkbox("Gizmo", ref showGizmo))
            {
                configuration.ShowGizmo = showGizmo;
                configuration.Save();
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "  <= ギズモ表示ON/OFF");
        }
        else
        {
            // プレビュー機能の説明文（画像3準拠）
            ImGui.Spacing();
            ImGui.BeginChild("PreviewExplanationBox", new Vector2(-1, 140), true);
            ImGui.TextWrapped("現在のMAP上に仮置きして見た目などの情報がきちんと反映してるか確認をできるようにするだけの機能（プレビュー機能）\n\nSpawnを実行後、ボタンが Despawn に切り替わる。\nDespawnを実行するとスポーンしていたものが消える");
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // テンプレート詳細情報
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Template Details:");
        ImGui.BulletText($"Source Type: {selectedTemplate.SourceType}");
        if (!string.IsNullOrWhiteSpace(selectedTemplate.FolderPath))
            ImGui.BulletText($"Folder: {selectedTemplate.FolderPath}");

        switch (selectedTemplate.SourceType)
        {
            case CharacterSourceType.Monster:
                ImGui.BulletText($"Monster ID: {selectedTemplate.DataId}");
                ImGui.BulletText($"Model ID: {selectedTemplate.ModelCharaId}");
                break;
            case CharacterSourceType.Npc:
                ImGui.BulletText($"ENpc ID: {selectedTemplate.DataId}");
                ImGui.BulletText($"Model ID: {selectedTemplate.ModelCharaId}");
                if (selectedTemplate.CustomizeData != null)
                    ImGui.BulletText("Customize Data: Included");
                if (selectedTemplate.NpcEquipmentModelIds != null)
                    ImGui.BulletText("Equipment Data: Included");
                break;
            case CharacterSourceType.Glamourer:
                if (!string.IsNullOrWhiteSpace(selectedTemplate.GlamourerDesignString))
                    ImGui.BulletText($"Glamourer Design: {selectedTemplate.GlamourerDesignString[..Math.Min(24, selectedTemplate.GlamourerDesignString.Length)]}...");
                if (!string.IsNullOrWhiteSpace(selectedTemplate.PenumbraCollectionName))
                    ImGui.BulletText($"Penumbra Collection: {selectedTemplate.PenumbraCollectionName}");
                break;
            case CharacterSourceType.Mcdf:
                ImGui.BulletText($"File: {System.IO.Path.GetFileName(selectedTemplate.McdfFilePath)}");
                break;
        }
    }

    private void DeleteSelected()
    {
        if (selectedTemplate != null)
        {
            if (actorManager.CurrentPreviewActor?.TemplateId == selectedTemplate.Id)
            {
                actorManager.DespawnPreviewCharacter();
            }
            configuration.Templates.Remove(selectedTemplate);
            configuration.Save();
            selectedTemplate = null;
        }
        else if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            configuration.Folders.Remove(selectedFolder);
            foreach (var t in configuration.Templates.Where(t => t.FolderPath == selectedFolder))
            {
                t.FolderPath = string.Empty;
            }
            configuration.Save();
            selectedFolder = null;
        }
    }

    private void OpenNewCharacterModal()
    {
        isEditing = false;
        editingTemplate = new CharacterTemplate();
        modalName = "New Character";
        modalFolder = selectedFolder ?? string.Empty;
        modalSourceType = CharacterSourceType.Glamourer;

        glamourerSearch = string.Empty;
        selectedGlamourerDesignGuid = string.Empty;
        selectedGlamourerDesignName = string.Empty;
        customGlamourerString = string.Empty;
        penumbraSearch = string.Empty;
        selectedPenumbraCollection = string.Empty;

        monsterSearchQuery = string.Empty;
        modalSelectedMonster = null;

        npcSearchQuery = string.Empty;
        modalSelectedNpc = null;
        cachedNpcAppearance = null;

        modalMcdfPath = string.Empty;

        isModalOpen = true;
    }

    private void OpenEditCharacterModal(CharacterTemplate template)
    {
        isEditing = true;
        editingTemplate = template;
        modalName = template.Name;
        modalFolder = template.FolderPath;
        modalSourceType = template.SourceType;

        customGlamourerString = template.GlamourerDesignString ?? string.Empty;
        selectedPenumbraCollection = template.PenumbraCollectionName ?? string.Empty;
        modalMcdfPath = template.McdfFilePath ?? string.Empty;

        glamourerSearch = string.Empty;
        selectedGlamourerDesignGuid = string.Empty;
        selectedGlamourerDesignName = string.Empty;

        if (template.SourceType == CharacterSourceType.Monster && template.DataId > 0)
        {
            modalSelectedMonster = new GameDataService.MonsterEntry(template.DataId, template.Name, template.ModelCharaId);
        }
        else if (template.SourceType == CharacterSourceType.Npc && template.DataId > 0)
        {
            modalSelectedNpc = new GameDataService.NpcEntry(template.DataId, template.Name, template.ModelCharaId);
            cachedNpcAppearance = new GameDataService.NpcAppearanceData(template.ModelCharaId, template.CustomizeData, template.NpcEquipmentModelIds);
        }

        isModalOpen = true;
    }

    private void DrawCharacterModal()
    {
        if (!isModalOpen) return;

        ImGui.SetNextWindowSize(new Vector2(500, 520), ImGuiCond.FirstUseEver);
        string title = isEditing ? "Edit Character###CharModal" : "New Chara###CharModal";

        if (ImGui.Begin(title, ref isModalOpen, ImGuiWindowFlags.NoCollapse))
        {
            // Chara Name
            ImGui.TextUnformatted("Chara Name");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##ModalCharaName", ref modalName, 64);

            ImGui.Spacing();

            // Select Folder
            ImGui.TextUnformatted("Select Folder");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##ModalFolderCombo", string.IsNullOrEmpty(modalFolder) ? "(Root / No Folder)" : modalFolder))
            {
                if (ImGui.Selectable("(Root / No Folder)", string.IsNullOrEmpty(modalFolder)))
                {
                    modalFolder = string.Empty;
                }
                foreach (var folder in configuration.Folders)
                {
                    if (ImGui.Selectable(folder, modalFolder == folder))
                    {
                        modalFolder = folder;
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Select Appearance Source (4ボタングリッド: 画像2準拠)
            ImGui.TextUnformatted("Select Appearance Source");

            float btnWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
            float btnHeight = 32f;

            var activeBtnCol = new Vector4(0.8f, 0.12f, 0.12f, 1.0f);
            var hoverBtnCol = new Vector4(0.95f, 0.25f, 0.25f, 1.0f);

            // 上段: [ Glamourer&Penumbra ] [ MCDF ]
            bool isGlam = modalSourceType == CharacterSourceType.Glamourer;
            if (isGlam)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, activeBtnCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverBtnCol);
            }
            if (ImGui.Button("Glamourer&Penumbra", new Vector2(btnWidth, btnHeight)))
            {
                modalSourceType = CharacterSourceType.Glamourer;
            }
            if (isGlam) ImGui.PopStyleColor(2);

            ImGui.SameLine();

            bool isMcdf = modalSourceType == CharacterSourceType.Mcdf;
            if (isMcdf)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, activeBtnCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverBtnCol);
            }
            if (ImGui.Button("MCDF", new Vector2(btnWidth, btnHeight)))
            {
                modalSourceType = CharacterSourceType.Mcdf;
            }
            if (isMcdf) ImGui.PopStyleColor(2);

            // 下段: [ NPC(ENpc) ] [ Monster/Mob ]
            bool isNpc = modalSourceType == CharacterSourceType.Npc;
            if (isNpc)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, activeBtnCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverBtnCol);
            }
            if (ImGui.Button("NPC(ENpc)", new Vector2(btnWidth, btnHeight)))
            {
                modalSourceType = CharacterSourceType.Npc;
            }
            if (isNpc) ImGui.PopStyleColor(2);

            ImGui.SameLine();

            bool isMonster = modalSourceType == CharacterSourceType.Monster;
            if (isMonster)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, activeBtnCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverBtnCol);
            }
            if (ImGui.Button("Monster/Mob", new Vector2(btnWidth, btnHeight)))
            {
                modalSourceType = CharacterSourceType.Monster;
            }
            if (isMonster) ImGui.PopStyleColor(2);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // 選んだ source に対応した項目が下段に表示される（画像2準拠）
            var detailHeight = ImGui.GetContentRegionAvail().Y - (36f + ImGui.GetStyle().ItemSpacing.Y);
            if (ImGui.BeginChild("SourceDetailRegion", new Vector2(-1, detailHeight), false))
            {
                switch (modalSourceType)
                {
                    case CharacterSourceType.Glamourer:
                        DrawModalGlamourerSection();
                        break;
                    case CharacterSourceType.Mcdf:
                        DrawModalMcdfSection();
                        break;
                    case CharacterSourceType.Npc:
                        DrawModalNpcSection();
                        break;
                    case CharacterSourceType.Monster:
                        DrawModalMonsterSection();
                        break;
                }
                ImGui.EndChild();
            }

            // 最下部 [ Save to Chara ]
            if (ImGui.Button("Save to Chara", new Vector2(-1, 32)))
            {
                SaveModalTemplate();
                isModalOpen = false;
            }

            ImGui.End();
        }
    }

    private void DrawModalGlamourerSection()
    {
        if (glamourerIpc.IsAvailable)
        {
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.2f, 1.0f), "Glamourer IPC: Connected");
        }
        else
        {
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.2f, 1.0f), "Glamourer IPC: Not Detected");
        }

        ImGui.Spacing();

        // Glamourer Design Combo (AQR Style)
        ImGui.TextUnformatted("Glamourer Design:");
        var designs = glamourerIpc.GetDesigns();
        string currentPreview = !string.IsNullOrEmpty(selectedGlamourerDesignName) 
            ? selectedGlamourerDesignName 
            : (!string.IsNullOrEmpty(customGlamourerString) ? "(Custom / Saved Design)" : "Select a design...");

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##GlamourerDesignCombo", currentPreview))
        {
            ImGui.InputTextWithHint("##GlamSearch", "Search designs...", ref glamourerSearch, 64);
            ImGui.Separator();

            foreach (var kvp in designs)
            {
                if (!string.IsNullOrWhiteSpace(glamourerSearch) && !kvp.Value.Contains(glamourerSearch, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isSelected = selectedGlamourerDesignGuid == kvp.Key.ToString();
                if (ImGui.Selectable(kvp.Value, isSelected))
                {
                    selectedGlamourerDesignGuid = kvp.Key.ToString();
                    selectedGlamourerDesignName = kvp.Value;
                    customGlamourerString = kvp.Key.ToString();
                    if (string.IsNullOrWhiteSpace(modalName) || modalName == "New Character")
                    {
                        modalName = kvp.Value;
                    }
                }
            }
            ImGui.EndCombo();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Or Direct Design String / Code:");
        ImGui.InputTextMultiline("##CustomGlamString", ref customGlamourerString, 4096, new Vector2(-1, 55));

        ImGui.Spacing();

        // Penumbra Collection Combo
        ImGui.TextUnformatted("Penumbra Collection:");
        if (penumbraIpc.IsAvailable)
        {
            var collections = penumbraIpc.GetCollections();
            string penumbraPreview = !string.IsNullOrEmpty(selectedPenumbraCollection) ? selectedPenumbraCollection : "Select a collection (Optional)...";

            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##PenumbraCollCombo", penumbraPreview))
            {
                ImGui.InputTextWithHint("##PenSearch", "Search collections...", ref penumbraSearch, 64);
                ImGui.Separator();

                if (ImGui.Selectable("(None / Default)", string.IsNullOrEmpty(selectedPenumbraCollection)))
                {
                    selectedPenumbraCollection = string.Empty;
                }

                foreach (var coll in collections.Values)
                {
                    if (!string.IsNullOrWhiteSpace(penumbraSearch) && !coll.Contains(penumbraSearch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelected = selectedPenumbraCollection == coll;
                    if (ImGui.Selectable(coll, isSelected))
                    {
                        selectedPenumbraCollection = coll;
                    }
                }
                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextDisabled("Penumbra IPC not detected.");
        }
    }

    private void DrawModalMcdfSection()
    {
        ImGui.TextUnformatted("MCDF File:");
        ImGui.SetNextItemWidth(-100);
        ImGui.InputText("##ModalMcdfPath", ref modalMcdfPath, 260);

        ImGui.SameLine();

        if (ImGui.Button("Browse...", new Vector2(90, 0)))
        {
            FilePicker.OpenFileDialog(
                "Select MCDF File",
                "MCDF Files (*.mcdf)\0*.mcdf\0All Files (*.*)\0*.*\0\0",
                path =>
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        modalMcdfPath = path;
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrWhiteSpace(modalName) || modalName == "New Character")
                        {
                            modalName = fileName;
                        }

                        // Auto-parse on selection
                        var parsed = mcdfParser.ParseMcdf(path);
                        if (parsed != null && !string.IsNullOrEmpty(parsed.GlamourerDesign))
                        {
                            customGlamourerString = parsed.GlamourerDesign;
                            logManager?.Info($"Parsed Glamourer design from selected MCDF: {fileName}");
                        }
                    }
                }
            );
        }

        if (!string.IsNullOrWhiteSpace(modalMcdfPath))
        {
            ImGui.Spacing();
            if (ImGui.Button("Re-parse MCDF"))
            {
                var parsed = mcdfParser.ParseMcdf(modalMcdfPath);
                if (parsed != null && !string.IsNullOrEmpty(parsed.GlamourerDesign))
                {
                    customGlamourerString = parsed.GlamourerDesign;
                    logManager?.Info("Reloaded Glamourer design from MCDF archive.");
                }
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), System.IO.Path.GetFileName(modalMcdfPath));
        }
    }

    private void DrawModalNpcSection()
    {
        ImGui.InputTextWithHint("##SearchNpcs", "Search NPCs (All items, no limit)...", ref npcSearchQuery, 64);
        var npcs = gameDataService.SearchNpcs(npcSearchQuery, 0);

        if (ImGui.BeginListBox("##ModalNpcList", new Vector2(-1, 160)))
        {
            foreach (var n in npcs)
            {
                bool isSelected = modalSelectedNpc?.Id == n.Id;
                if (ImGui.Selectable($"[{n.Id}] {n.Name}", isSelected))
                {
                    modalSelectedNpc = n;
                    modalName = n.Name;
                    cachedNpcAppearance = gameDataService.GetNpcAppearanceData(n.Id);
                }
            }
            ImGui.EndListBox();
        }

        if (modalSelectedNpc != null)
        {
            string typeDesc = modalSelectedNpc.ModelCharaId > 0 
                ? $"Non-humanoid Model ({modalSelectedNpc.ModelCharaId})" 
                : "Humanoid (Custom Appearance & Equipment loaded)";
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Selected: [{modalSelectedNpc.Id}] {modalSelectedNpc.Name} - {typeDesc}");
        }
    }

    private void DrawModalMonsterSection()
    {
        ImGui.InputTextWithHint("##SearchMonsters", "Search Monsters (All items, no limit)...", ref monsterSearchQuery, 64);
        var monsters = gameDataService.SearchMonsters(monsterSearchQuery, 0);

        if (ImGui.BeginListBox("##ModalMonsterList", new Vector2(-1, 160)))
        {
            foreach (var m in monsters)
            {
                bool isSelected = modalSelectedMonster?.Id == m.Id;
                if (ImGui.Selectable($"[{m.Id}] {m.Name}", isSelected))
                {
                    modalSelectedMonster = m;
                    modalName = m.Name;
                }
            }
            ImGui.EndListBox();
        }

        if (modalSelectedMonster != null)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Selected: [{modalSelectedMonster.Id}] {modalSelectedMonster.Name} (Model: {modalSelectedMonster.ModelCharaId})");
        }
    }

    private void SaveModalTemplate()
    {
        var target = isEditing ? editingTemplate : new CharacterTemplate();
        target.Name = string.IsNullOrWhiteSpace(modalName) ? "Character" : modalName;
        target.FolderPath = modalFolder;
        target.SourceType = modalSourceType;
        target.PenumbraCollectionName = selectedPenumbraCollection;
        target.McdfFilePath = modalMcdfPath;

        var design = customGlamourerString;
        if (string.IsNullOrWhiteSpace(design) && modalSourceType == CharacterSourceType.PlayerClone && glamourerIpc.IsAvailable)
        {
            design = glamourerIpc.GetCustomization(0) ?? string.Empty;
        }
        target.GlamourerDesignString = design;

        if (modalSourceType == CharacterSourceType.Monster && modalSelectedMonster != null)
        {
            target.DataId = modalSelectedMonster.Id;
            target.ModelCharaId = modalSelectedMonster.ModelCharaId;
            target.CustomizeData = null;
            target.NpcEquipmentModelIds = null;
            target.WeaponVisible = false; // モンスターはデフォルトで武器非表示
        }
        else if (modalSourceType == CharacterSourceType.Npc && modalSelectedNpc != null)
        {
            target.DataId = modalSelectedNpc.Id;
            target.ModelCharaId = modalSelectedNpc.ModelCharaId;

            cachedNpcAppearance ??= gameDataService.GetNpcAppearanceData(modalSelectedNpc.Id);
            if (cachedNpcAppearance != null)
            {
                target.CustomizeData = cachedNpcAppearance.CustomizeData;
                target.NpcEquipmentModelIds = cachedNpcAppearance.EquipmentModelIds;
            }
            if (target.ModelCharaId > 0)
            {
                target.WeaponVisible = false; // 非人型NPC（モーグリ等）は武器非表示
            }
        }
        else if (modalSourceType == CharacterSourceType.Mcdf)
        {
            target.ModelCharaId = 0;
        }

        if (!isEditing)
        {
            configuration.Templates.Add(target);
        }

        if (!string.IsNullOrWhiteSpace(modalFolder) && !configuration.Folders.Contains(modalFolder))
        {
            configuration.Folders.Add(modalFolder);
        }

        configuration.Save();
        selectedTemplate = target;
        editInlineName = target.Name;
        logManager?.Info($"Saved character template '{target.Name}' (Source: {target.SourceType}, Model: {target.ModelCharaId}, Folder: {target.FolderPath}).");
    }

    private void DrawNewFolderPopup()
    {
        if (openNewFolderPopup)
        {
            ImGui.OpenPopup("NewFolderModal");
            openNewFolderPopup = false;
        }

        bool pOpen = true;
        if (ImGui.BeginPopupModal("NewFolderModal", ref pOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Enter Folder Name:");
            ImGui.InputText("##NewFolderNameInput", ref newFolderName, 64);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Create", new Vector2(90, 0)))
            {
                if (!string.IsNullOrWhiteSpace(newFolderName) && !configuration.Folders.Contains(newFolderName))
                {
                    configuration.Folders.Add(newFolderName.Trim());
                    configuration.Save();
                }
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(80, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
