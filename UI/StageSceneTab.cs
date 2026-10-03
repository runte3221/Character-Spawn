using System;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;
using CharacterSpawn.Managers;
using CharacterSpawn.Services;

namespace CharacterSpawn.UI;

public class StageSceneTab
{
    private readonly Configuration configuration;
    private readonly ActorManager actorManager;
    private readonly SceneManager sceneManager;
    private readonly GameDataService gameDataService;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly GizmoRenderer? gizmoRenderer;

    // UI state
    private string newSceneName = "新しいシーン";
    private SceneActorPlacement? selectedPlacement;
    private string selectedTemplateIdForAdd = string.Empty;

    public SceneActorPlacement? SelectedPlacement => selectedPlacement;
    public SpawnedActorData? SelectedActor => selectedPlacement != null ? sceneManager.GetSpawnedActor(selectedPlacement.PlacementId) : null;

    public void SyncPlacementTransformFromGizmo(Vector3 newPos, float newRot)
    {
        if (selectedPlacement != null)
        {
            selectedPlacement.Position = newPos;
            selectedPlacement.Rotation = newRot;
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
        GizmoRenderer? gizmoRenderer = null)
    {
        this.configuration = configuration;
        this.actorManager = actorManager;
        this.sceneManager = sceneManager;
        this.gameDataService = gameDataService;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.gizmoRenderer = gizmoRenderer;
    }

    public void Draw()
    {
        ImGui.Columns(2, "SceneColumns", true);

        // 左ペイン: シーン一覧・作成・全体操作
        DrawLeftPanel();

        ImGui.NextColumn();

        // 右ペイン: 選択中シーンの配置キャラクター一覧・編集・追加
        DrawRightPanel();

        ImGui.Columns(1);
    }

    #region Left Panel (Scene List & Global Actions)

    private void DrawLeftPanel()
    {
        ImGui.TextUnformatted("シーン一覧 (Scene List)");
        ImGui.Separator();

        // 新規シーン作成バー
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 80);
        ImGui.InputText("##NewSceneName", ref newSceneName, 64);
        ImGui.SameLine();
        if (ImGui.Button("新規作成", new Vector2(70, 0)))
        {
            if (!string.IsNullOrWhiteSpace(newSceneName))
            {
                uint territory = clientState.TerritoryType;
                sceneManager.CreateScene(newSceneName.Trim(), territory);
                newSceneName = "新しいシーン";
            }
        }

        ImGui.Spacing();

        // シーンリストボックス
        if (sceneManager.Scenes.Count == 0)
        {
            ImGui.TextDisabled("登録されているシーンがありません。");
        }
        else
        {
            if (ImGui.BeginListBox("##SceneListBox", new Vector2(-1, 220)))
            {
                foreach (var scene in sceneManager.Scenes)
                {
                    bool isSelected = sceneManager.SelectedScene?.Id == scene.Id;
                    bool isSpawned = sceneManager.IsSceneSpawned(scene);

                    string statusIcon = isSpawned ? "[●] " : "[ ] ";
                    string label = $"{statusIcon}{scene.Name} ({scene.Placements.Count}体)##Scene_{scene.Id}";

                    if (isSpawned)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1.0f, 0.4f, 1.0f));
                    }

                    if (ImGui.Selectable(label, isSelected))
                    {
                        sceneManager.SelectedScene = scene;
                        selectedPlacement = scene.Placements.FirstOrDefault();
                    }

                    if (isSpawned)
                    {
                        ImGui.PopStyleColor();
                    }
                }
                ImGui.EndListBox();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // 選択中シーンの一括操作ボタン
        var curScene = sceneManager.SelectedScene;
        if (curScene != null)
        {
            bool isCurrentSpawned = sceneManager.IsSceneSpawned(curScene);

            if (isCurrentSpawned)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                if (ImGui.Button("シーンを一括デスポーン (Despawn All)##SceneDespawn", new Vector2(-1, 32)))
                {
                    sceneManager.DespawnScene();
                }
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.6f, 0.2f, 1.0f));
                if (ImGui.Button("シーンを一括スポーン (Spawn All)##SceneSpawn", new Vector2(-1, 32)))
                {
                    sceneManager.SpawnScene(curScene);
                }
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();

            // シーン情報編集
            string editName = curScene.Name;
            ImGui.TextUnformatted("シーン名編集:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##EditSceneName", ref editName, 64))
            {
                curScene.Name = editName;
                sceneManager.SaveScenes();
            }

            string editDesc = curScene.Description;
            ImGui.TextUnformatted("説明・メモ:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##EditSceneDesc", ref editDesc, 128))
            {
                curScene.Description = editDesc;
                sceneManager.SaveScenes();
            }

            ImGui.Spacing();
            if (ImGui.Button("このシーンを削除##DeleteScene", new Vector2(-1, 24)))
            {
                sceneManager.DeleteScene(curScene);
                selectedPlacement = null;
            }
        }
    }

    #endregion

    #region Right Panel (Placement Table & Editing)

    private void DrawRightPanel()
    {
        var curScene = sceneManager.SelectedScene;
        if (curScene == null)
        {
            ImGui.TextDisabled("左側のリストからシーンを選択してください。");
            return;
        }

        ImGui.TextUnformatted($"配置キャラクター管理: {curScene.Name}");
        ImGui.Separator();

        // 1. 新規キャラクター配置の追加セクション
        DrawAddPlacementSection(curScene);

        ImGui.Spacing();
        ImGui.Separator();

        // 2. 配置キャラクター一覧テーブル
        DrawPlacementTable(curScene);

        ImGui.Spacing();
        ImGui.Separator();

        // 3. 選択された配置アクターの詳細編集（座標・向き）
        DrawSelectedPlacementInspector(curScene);
    }

    private void DrawAddPlacementSection(SceneData scene)
    {
        ImGui.TextUnformatted("配置キャラクターの追加 (Add Actor):");

        var templates = configuration.Templates;
        if (templates.Count == 0)
        {
            ImGui.TextDisabled("キャラクターテンプレートがありません。先に「Character」タブで作成してください。");
            return;
        }

        // テンプレート選択コンボ
        var currentTemplate = templates.FirstOrDefault(t => t.Id == selectedTemplateIdForAdd);
        string previewName = currentTemplate != null ? currentTemplate.Name : "テンプレートを選択...";

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 220);
        if (ImGui.BeginCombo("##SelectTemplateCombo", previewName))
        {
            foreach (var t in templates)
            {
                bool isSelected = selectedTemplateIdForAdd == t.Id;
                if (ImGui.Selectable($"{t.Name} ({t.SourceType})##T_{t.Id}", isSelected))
                {
                    selectedTemplateIdForAdd = t.Id;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("現在地に配置して追加##AddAtMe", new Vector2(210, 0)))
        {
            var targetTemplate = templates.FirstOrDefault(t => t.Id == selectedTemplateIdForAdd) ?? templates.FirstOrDefault();
            if (targetTemplate != null)
            {
                var (pos, rot) = GetPlayerTransform();
                var placement = sceneManager.AddPlacement(scene, targetTemplate, pos, rot);
                selectedPlacement = placement;
            }
        }
    }

    private void DrawPlacementTable(SceneData scene)
    {
        ImGui.TextUnformatted($"配置アクター一覧 ({scene.Placements.Count}体)");

        if (scene.Placements.Count == 0)
        {
            ImGui.TextDisabled("このシーンには配置アクターが登録されていません。");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (ImGui.BeginTable("PlacementTable", 6, flags, new Vector2(-1, 160)))
        {
            ImGui.TableSetupColumn("状態", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("表示名", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("テンプレート", ImGuiTableColumnFlags.WidthStretch, 2);
            ImGui.TableSetupColumn("座標 (X, Y, Z)", ImGuiTableColumnFlags.WidthStretch, 3);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("削除", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableHeadersRow();

            for (int i = 0; i < scene.Placements.Count; i++)
            {
                var placement = scene.Placements[i];
                bool isSpawned = sceneManager.IsPlacementSpawned(placement.PlacementId);
                var template = configuration.Templates.FirstOrDefault(t => t.Id == placement.CharacterTemplateId);

                ImGui.TableNextRow();
                ImGui.PushID($"Placement_{placement.PlacementId}");

                // 1. 状態
                ImGui.TableNextColumn();
                if (isSpawned)
                {
                    ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.3f, 1.0f), "スポーン中");
                }
                else
                {
                    ImGui.TextDisabled("停止");
                }

                // 2. 表示名
                ImGui.TableNextColumn();
                bool isSelected = selectedPlacement?.PlacementId == placement.PlacementId;
                string dName = !string.IsNullOrWhiteSpace(placement.CustomDisplayName) ? placement.CustomDisplayName : (template?.Name ?? "不明");
                if (ImGui.Selectable($"{dName}##Select_{placement.PlacementId}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    selectedPlacement = placement;
                }

                // 3. テンプレート名
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(template?.Name ?? "削除されたテンプレート");

                // 4. 座標
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"({placement.Position.X:F1}, {placement.Position.Y:F1}, {placement.Position.Z:F1})");

                // 5. 個別スポーン／デスポーン操作
                ImGui.TableNextColumn();
                if (isSpawned)
                {
                    if (ImGui.SmallButton("デスポーン##PDespawn"))
                    {
                        sceneManager.DespawnPlacement(placement);
                    }
                }
                else
                {
                    if (ImGui.SmallButton("スポーン##PSpawn"))
                    {
                        sceneManager.SpawnPlacement(scene, placement);
                    }
                }

                // 6. 削除
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("削除##PDel"))
                {
                    sceneManager.RemovePlacement(scene, placement);
                    if (selectedPlacement?.PlacementId == placement.PlacementId)
                    {
                        selectedPlacement = scene.Placements.FirstOrDefault();
                    }
                    ImGui.PopID();
                    break;
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void DrawSelectedPlacementInspector(SceneData scene)
    {
        if (selectedPlacement == null)
        {
            ImGui.TextDisabled("上の表から編集する配置アクターを選択してください。");
            return;
        }

        ImGui.TextUnformatted($"配置アクター詳細設定: {selectedPlacement.CustomDisplayName}");

        // 表示名
        string cName = selectedPlacement.CustomDisplayName;
        if (ImGui.InputText("個別表示名", ref cName, 64))
        {
            selectedPlacement.CustomDisplayName = cName;
            sceneManager.SaveScenes();
        }

        // 座標編集
        var pos = selectedPlacement.Position;
        if (ImGui.DragFloat3("座標 (X, Y, Z)", ref pos, 0.05f))
        {
            selectedPlacement.Position = pos;
            sceneManager.SaveScenes();

            // スポーン中の場合は即座にゲーム内アクターの位置も同期
            var spawned = sceneManager.GetSpawnedActor(selectedPlacement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, pos, selectedPlacement.Rotation);
            }
        }

        // 回転編集
        float rot = selectedPlacement.Rotation;
        if (ImGui.SliderAngle("向き (Rotation)", ref rot, -180f, 180f))
        {
            selectedPlacement.Rotation = rot;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(selectedPlacement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, selectedPlacement.Position, rot);
            }
        }

        // 自キャラ現在位置を再取得
        if (ImGui.Button("自キャラの現在座標・向きを適用##ApplyMyPos"))
        {
            var (myPos, myRot) = GetPlayerTransform();
            selectedPlacement.Position = myPos;
            selectedPlacement.Rotation = myRot;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(selectedPlacement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, myPos, myRot);
            }
        }
    }

    #endregion

    private (Vector3 Position, float Rotation) GetPlayerTransform()
    {
        var localPlayer = objectTable.Length > 0 ? objectTable[0] : null;
        if (localPlayer != null)
        {
            return (localPlayer.Position, localPlayer.Rotation);
        }
        return (Vector3.Zero, 0f);
    }
}
