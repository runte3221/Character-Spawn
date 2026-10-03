using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using CharacterSpawn.Managers;
using CharacterSpawn.Models;
using CharacterSpawn.Services;

namespace CharacterSpawn.UI;

/// <summary>
/// メインウィンドウ内の「Scene」タブ
/// 左ペインにフォルダ階層ツリービュー、右ペインにシーン詳細（Auto Spawn、Show/Hide、Edit展開）を提供
/// </summary>
public class StageSceneTab
{
    private readonly Configuration configuration;
    private readonly ActorManager actorManager;
    private readonly SceneManager sceneManager;
    private readonly GameDataService gameDataService;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Action openEditWindowAction;

    // UI state
    private string selectedFolderPath = string.Empty;
    private bool isCreateFolderModalOpen = false;
    private string newFolderNameInput = string.Empty;

    public SpawnedActorData? SelectedActor
    {
        get
        {
            var p = sceneManager.SelectedPlacement;
            return p != null ? sceneManager.GetSpawnedActor(p.PlacementId) : null;
        }
    }

    public void SyncPlacementTransformFromGizmo(Vector3 newPos, float newRot, float newScale)
    {
        var p = sceneManager.SelectedPlacement;
        if (p != null)
        {
            p.Position = newPos;
            p.Rotation = newRot;
            if (newScale > 0.001f)
            {
                p.Scale = newScale;
            }
            sceneManager.SaveScenes();
        }
    }

    public StageSceneTab(
        Configuration configuration,
        ActorManager actorManager,
        SceneManager sceneManager,
        GameDataService gameDataService,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog log,
        Action openEditWindowAction)
    {
        this.configuration = configuration;
        this.actorManager = actorManager;
        this.sceneManager = sceneManager;
        this.gameDataService = gameDataService;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.openEditWindowAction = openEditWindowAction;
    }

    public void Draw()
    {
        ImGui.Columns(2, "SceneMainColumns", true);

        // 左ペイン: フォルダ・シーンのツリー構造と管理ボタン
        DrawLeftTreePanel();

        ImGui.NextColumn();

        // 右ペイン: 選択中シーンの詳細設定と Show/Hide / Edit 操作
        DrawRightDetailsPanel();

        ImGui.Columns(1);

        // フォルダ新規作成ポップアップモーダル
        DrawNewFolderModal();
    }

    #region Left Tree Panel

    private void DrawLeftTreePanel()
    {
        float bottomBarHeight = 36f;
        float treeHeight = ImGui.GetContentRegionAvail().Y - bottomBarHeight - 8f;

        // ツリービュー領域 (枠線・背景付き Child)
        if (ImGui.BeginChild("##SceneTreeChild", new Vector2(-1, treeHeight), true))
        {
            DrawFolderTreeRecursive("");
        }
        ImGui.EndChild();

        ImGui.Spacing();

        // 下部ボタンバー: [New Scene] [New Folder] [Delete]
        float availW = ImGui.GetContentRegionAvail().X;
        float btnWidth = (availW - 16f) / 3f;

        if (ImGui.Button("New Scene", new Vector2(btnWidth, 26)))
        {
            uint territoryId = clientState.TerritoryType;
            string territoryName = gameDataService.GetTerritoryName(territoryId);
            string defaultName = string.IsNullOrEmpty(selectedFolderPath) ? "New Scene" : "New Scene";
            var created = sceneManager.CreateScene(defaultName, territoryId, territoryName, selectedFolderPath);
            sceneManager.SelectedScene = created;
        }

        ImGui.SameLine();
        if (ImGui.Button("New Folder", new Vector2(btnWidth, 26)))
        {
            newFolderNameInput = "New Folder";
            isCreateFolderModalOpen = true;
            ImGui.OpenPopup("CreateFolderModal");
        }

        ImGui.SameLine();
        var curScene = sceneManager.SelectedScene;
        bool canDelete = curScene != null || !string.IsNullOrEmpty(selectedFolderPath);
        if (!canDelete)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Delete", new Vector2(btnWidth, 26));
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Delete", new Vector2(btnWidth, 26)))
            {
                if (curScene != null)
                {
                    sceneManager.DeleteScene(curScene);
                }
                else if (!string.IsNullOrEmpty(selectedFolderPath))
                {
                    sceneManager.DeleteFolder(selectedFolderPath);
                    selectedFolderPath = "";
                }
            }
        }
    }

    /// <summary>
    /// フォルダおよびシーンを階層ツリーとして再帰描画
    /// </summary>
    private void DrawFolderTreeRecursive(string parentPath)
    {
        // 1. 直下の子フォルダを特定
        var subFolders = sceneManager.Folders
            .Where(f =>
            {
                if (string.IsNullOrEmpty(parentPath))
                {
                    return !f.Contains('/');
                }
                else
                {
                    return f.StartsWith(parentPath + "/") && f.Substring(parentPath.Length + 1).IndexOf('/') == -1;
                }
            })
            .OrderBy(f => f)
            .ToList();

        foreach (var folder in subFolders)
        {
            string folderName = string.IsNullOrEmpty(parentPath) ? folder : folder.Substring(parentPath.Length + 1);

            ImGui.PushID($"Folder_{folder}");

            // フォルダノード
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (selectedFolderPath == folder && sceneManager.SelectedScene == null)
            {
                flags |= ImGuiTreeNodeFlags.Selected;
            }

            bool isNodeOpen = ImGui.TreeNodeEx($"##Node_{folder}", flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
            {
                selectedFolderPath = folder;
                sceneManager.SelectedScene = null;
            }

            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.78f, 0.25f, 1.0f));
            ImGui.TextUnformatted(isNodeOpen ? FontAwesomeIcon.FolderOpen.ToIconString() : FontAwesomeIcon.Folder.ToIconString());
            ImGui.PopStyleColor();
            ImGui.PopFont();

            ImGui.SameLine();
            ImGui.TextUnformatted(folderName);

            if (isNodeOpen)
            {
                DrawFolderTreeRecursive(folder);
                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        // 2. このフォルダ直下に属するシーンを描画
        var childScenes = sceneManager.Scenes
            .Where(s => (s.FolderPath ?? "") == parentPath)
            .OrderBy(s => s.Name)
            .ToList();

        foreach (var scene in childScenes)
        {
            ImGui.PushID($"Scene_{scene.Id}");

            bool isSelected = sceneManager.SelectedScene?.Id == scene.Id;
            bool isSpawned = sceneManager.IsSceneSpawned(scene);

            // インデントを考慮した Selectable
            string icon = isSpawned ? "● " : "  ";
            string label = $"{icon}{scene.Name}";

            if (isSpawned)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1.0f, 0.4f, 1.0f));
            }

            if (ImGui.Selectable(label, isSelected))
            {
                sceneManager.SelectedScene = scene;
                selectedFolderPath = scene.FolderPath ?? "";
                sceneManager.SelectedPlacement = scene.Placements.FirstOrDefault();
            }

            if (isSpawned)
            {
                ImGui.PopStyleColor();
            }

            ImGui.PopID();
        }
    }

    private void DrawNewFolderModal()
    {
        if (ImGui.BeginPopupModal("CreateFolderModal", ref isCreateFolderModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("新規フォルダ作成");
            ImGui.Separator();
            ImGui.Spacing();

            if (!string.IsNullOrEmpty(selectedFolderPath))
            {
                ImGui.TextDisabled($"親フォルダ: {selectedFolderPath}");
            }

            ImGui.TextUnformatted("フォルダ名:");
            ImGui.SetNextItemWidth(250);
            ImGui.InputText("##NewFolderInput", ref newFolderNameInput, 64);

            ImGui.Spacing();
            if (ImGui.Button("作成", new Vector2(100, 26)))
            {
                if (!string.IsNullOrWhiteSpace(newFolderNameInput))
                {
                    string finalPath = string.IsNullOrEmpty(selectedFolderPath)
                        ? newFolderNameInput.Trim()
                        : $"{selectedFolderPath}/{newFolderNameInput.Trim()}";
                    sceneManager.AddFolder(finalPath);
                    selectedFolderPath = finalPath;
                }
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("キャンセル", new Vector2(100, 26)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    #endregion

    #region Right Details Panel

    private void DrawRightDetailsPanel()
    {
        var scene = sceneManager.SelectedScene;
        if (scene == null)
        {
            ImGui.TextDisabled("左側のツリーからシーンを選択してください。");
            return;
        }

        // 1. Scene Name 入力欄
        string sceneName = scene.Name;
        ImGui.TextUnformatted("Scene Name");
        ImGui.SameLine(100);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputText("##SceneNameInput", ref sceneName, 64))
        {
            scene.Name = sceneName;
            sceneManager.SaveScenes();
        }

        ImGui.Spacing();

        // 2. Location (テリトリー名表示)
        string locName = !string.IsNullOrWhiteSpace(scene.TerritoryName)
            ? scene.TerritoryName
            : gameDataService.GetTerritoryName(scene.TerritoryId);
        ImGui.TextUnformatted("Location   :");
        ImGui.SameLine(100);
        ImGui.TextUnformatted(locName);

        ImGui.Spacing();

        // 3. Auto Spawn チェックボックス
        bool autoSpawn = scene.AutoSpawn;
        if (ImGui.Checkbox("Auto Spawn", ref autoSpawn))
        {
            scene.AutoSpawn = autoSpawn;
            sceneManager.SaveScenes();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("対象エリアに入ったら自動的にスポーンする");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 4. Show / Hide ボタン & Edit ボタン
        bool isSpawned = sceneManager.IsSceneSpawned(scene);

        // [Show] / [Hide] (左側)
        var showBtnCol = isSpawned
            ? new Vector4(0.75f, 0.2f, 0.2f, 1.0f) // Hide (赤)
            : new Vector4(0.2f, 0.6f, 0.25f, 1.0f); // Show (緑)

        ImGui.PushStyleColor(ImGuiCol.Button, showBtnCol);
        string showHideLabel = isSpawned ? "Hide" : "Show";
        if (ImGui.Button($"{showHideLabel}##SceneShowHide", new Vector2(75, 30)))
        {
            if (isSpawned)
            {
                sceneManager.DespawnScene();
            }
            else
            {
                sceneManager.SpawnScene(scene);
            }
        }
        ImGui.PopStyleColor();

        // [Edit] (右側)
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 70);
        if (ImGui.Button("Edit##SceneEditBtn", new Vector2(70, 30)))
        {
            openEditWindowAction?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("シーン編集ウィンドウを開きます（アクター追加・座標編集・モーション等）");
        }
    }

    #endregion
}
