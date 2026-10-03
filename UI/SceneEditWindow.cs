using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using CharacterSpawn.Managers;
using CharacterSpawn.Models;

namespace CharacterSpawn.UI;

/// <summary>
/// シーン編集用独立ウィンドウ (Scene Edit Window)
/// 上段に全タブ共通のスポーンアクター一覧、下段に各設定タブ(Spawn, Scene, Animation)を展開
/// </summary>
public class SceneEditWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly SceneManager sceneManager;
    private readonly ActorManager actorManager;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly GizmoRenderer gizmoRenderer;
    private readonly GameDataService? gameDataService;

    private string selectedTemplateIdForAdd = string.Empty;
    private int currentTabIndex = 0; // 0: Spawn, 1: Scene, 2: Animation

    private string motionSearchQuery = string.Empty;
    private string facialSearchQuery = string.Empty;

    public SceneEditWindow(
        Configuration configuration,
        SceneManager sceneManager,
        ActorManager actorManager,
        IClientState clientState,
        IObjectTable objectTable,
        GizmoRenderer gizmoRenderer,
        GameDataService? gameDataService = null)
        : base("Scene Edit###SceneEditWindow", ImGuiWindowFlags.None)
    {
        this.configuration = configuration;
        this.sceneManager = sceneManager;
        this.actorManager = actorManager;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.gizmoRenderer = gizmoRenderer;
        this.gameDataService = gameDataService;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 520),
            MaximumSize = new Vector2(1000, 1200)
        };
        Size = new Vector2(500, 620);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw()
    {
        var scene = sceneManager.SelectedScene;
        WindowName = scene != null ? $"Scene Edit  ({scene.Name})###SceneEditWindow" : "Scene Edit###SceneEditWindow";
    }

    public override void Draw()
    {
        var scene = sceneManager.SelectedScene;
        if (scene == null)
        {
            ImGui.TextDisabled("編集対象のシーンが選択されていません。メインウィンドウの「Scene」タブからシーンを選択してください。");
            return;
        }

        // 1. 上部タブバー (Spawn / Scene / Animation)
        DrawTopTabs();

        ImGui.Spacing();

        // 2. 常時表示の共通上段: ツールバー + アクター一覧リスト + 削除ボタン
        DrawCommonTopSection(scene);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 3. タブごとに切り替わる下段
        switch (currentTabIndex)
        {
            case 0:
                DrawSpawnTab(scene);
                break;
            case 1:
                DrawSceneTabPlaceholder(scene);
                break;
            case 2:
                DrawAnimationTab(scene);
                break;
        }
    }

    #region 1. Top Tabs

    private void DrawTopTabs()
    {
        var tabActiveCol = new Vector4(0.7f, 0.15f, 0.15f, 1.0f); // Stagehand 風の赤アクセント
        var tabInactiveCol = new Vector4(0.22f, 0.22f, 0.24f, 1.0f);
        var tabBtnSize = new Vector2((ImGui.GetContentRegionAvail().X - 16) / 3f, 26);

        string[] tabNames = { "Spawn", "Scene", "Animation" };
        for (int i = 0; i < tabNames.Length; i++)
        {
            bool isActive = currentTabIndex == i;
            if (isActive)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, tabActiveCol);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, tabActiveCol);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, tabInactiveCol);
            }

            if (ImGui.Button($"{tabNames[i]}##SceneEditTab_{i}", tabBtnSize))
            {
                currentTabIndex = i;
            }

            ImGui.PopStyleColor(isActive ? 2 : 1);

            if (i < tabNames.Length - 1)
            {
                ImGui.SameLine();
            }
        }
    }

    #endregion

    #region 2. Common Top Section (Toolbar + Actor List + Delete)

    private void DrawCommonTopSection(SceneData scene)
    {
        // ツールバー (左: 4つのギズモアイコン, 右: テンプレート選択 + Add)
        gizmoRenderer.DrawToolbar();

        ImGui.SameLine();
        float availX = ImGui.GetContentRegionAvail().X;
        float addBtnWidth = 55;
        float comboWidth = MathF.Max(120, availX - addBtnWidth - 8);

        var templates = configuration.Templates;
        var curTemplate = templates.FirstOrDefault(t => t.Id == selectedTemplateIdForAdd);
        string previewName = curTemplate != null ? curTemplate.Name : (templates.Count > 0 ? "テンプレートを選択..." : "なし");

        ImGui.SetNextItemWidth(comboWidth);
        if (ImGui.BeginCombo("##AddTemplateCombo", previewName))
        {
            foreach (var t in templates)
            {
                bool isSelected = selectedTemplateIdForAdd == t.Id;
                if (ImGui.Selectable($"{t.Name}##T_{t.Id}", isSelected))
                {
                    selectedTemplateIdForAdd = t.Id;
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("+Add", new Vector2(addBtnWidth, 0)))
        {
            var targetTemplate = templates.FirstOrDefault(t => t.Id == selectedTemplateIdForAdd) ?? templates.FirstOrDefault();
            if (targetTemplate != null)
            {
                var (pos, rot) = GetPlayerTransform();
                var placement = sceneManager.AddPlacement(scene, targetTemplate, pos, rot);
                sceneManager.SelectedPlacement = placement;

                // シーンがすでにスポーン中なら新規アクターも即座にスポーン
                if (sceneManager.IsSceneSpawned(scene))
                {
                    sceneManager.SpawnPlacement(scene, placement);
                }
            }
        }

        ImGui.Spacing();

        // アクター一覧リスト (スクロール領域)
        var listHeight = 150f;
        if (ImGui.BeginChild("##ActorListBox", new Vector2(-1, listHeight), true, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (scene.Placements.Count == 0)
            {
                ImGui.TextDisabled("アクターが登録されていません。右上の「+Add」から追加してください。");
            }
            else
            {
                for (int i = 0; i < scene.Placements.Count; i++)
                {
                    var placement = scene.Placements[i];
                    var template = templates.FirstOrDefault(t => t.Id == placement.CharacterTemplateId);
                    bool isSelected = sceneManager.SelectedPlacement?.PlacementId == placement.PlacementId;

                    ImGui.PushID($"ActorRow_{placement.PlacementId}");

                    // 行全体の選択判定 (Selectable)
                    string dName = !string.IsNullOrWhiteSpace(placement.CustomDisplayName)
                        ? placement.CustomDisplayName
                        : (template?.Name ?? "不明なキャラクター");

                    // 左側: 紫色の人アイコン
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.45f, 0.95f, 1.0f));
                    ImGui.TextUnformatted(FontAwesomeIcon.User.ToIconString());
                    ImGui.PopStyleColor();
                    ImGui.PopFont();
                    ImGui.SameLine();

                    // 表示名
                    float rightButtonWidth = 32f;
                    float nameWidth = MathF.Max(50, ImGui.GetContentRegionAvail().X - rightButtonWidth);
                    if (ImGui.Selectable($"{dName}##Select_{placement.PlacementId}", isSelected, ImGuiSelectableFlags.None, new Vector2(nameWidth, 0)))
                    {
                        sceneManager.SelectedPlacement = placement;
                    }

                    // 右側: 目のアイコン (表示/非表示トグル)
                    ImGui.SameLine();
                    var eyeIcon = placement.IsVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.EyeSlash;
                    if (placement.IsVisible)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.45f, 0.45f, 1.0f));
                    }

                    if (ImGuiComponents.IconButton("##EyeBtn", eyeIcon))
                    {
                        sceneManager.TogglePlacementVisibility(scene, placement);
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(placement.IsVisible ? "クリックで非表示 (デスポーン)" : "クリックで表示 (スポーン)");
                    }

                    ImGui.PopID();
                }
            }
        }
        ImGui.EndChild();

        // リスト右下の [Delete] ボタン
        float delWidth = 75f;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - delWidth - ImGui.GetStyle().WindowPadding.X);
        var curPlacement = sceneManager.SelectedPlacement;
        if (curPlacement == null || scene.Placements.Count == 0)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Delete", new Vector2(delWidth, 0));
            ImGui.EndDisabled();
        }
        else
        {
            if (ImGui.Button("Delete", new Vector2(delWidth, 0)))
            {
                sceneManager.RemovePlacement(scene, curPlacement);
                sceneManager.SelectedPlacement = scene.Placements.FirstOrDefault();
            }
        }
    }

    #endregion

    #region 3. Tab Contents

    private void DrawSpawnTab(SceneData scene)
    {
        var placement = sceneManager.SelectedPlacement;
        if (placement == null)
        {
            ImGui.TextDisabled("上段の一覧から編集するキャラクターを選択してください。");
            return;
        }

        var template = configuration.Templates.FirstOrDefault(t => t.Id == placement.CharacterTemplateId);

        // 1. Character Name (編集不可・表示だけ)
        string charaName = template?.Name ?? "不明";
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        ImGui.BeginDisabled();
        ImGui.InputText("##CharaName", ref charaName, 64);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted("Character Name");

        // 2. Custom Name (カスタムネームプレート)
        string customName = placement.CustomDisplayName;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.InputText("##CustomName", ref customName, 64))
        {
            placement.CustomDisplayName = customName;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                spawned.DisplayName = !string.IsNullOrWhiteSpace(customName) ? customName : (template?.Name ?? "");
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Custom Name");

        // 3. Custom Title (カスタム称号)
        string customTitle = placement.NamePlate.CustomTitle;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.InputText("##CustomTitle", ref customTitle, 64))
        {
            placement.NamePlate.CustomTitle = customTitle;
            sceneManager.SaveScenes();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Custom Title");

        // 4. 表示・非表示チェックボックス
        bool showCustomName = placement.NamePlate.ShowCustomName;
        if (ImGui.Checkbox("Custom Name##ShowCustomName", ref showCustomName))
        {
            placement.NamePlate.ShowCustomName = showCustomName;
            sceneManager.SaveScenes();
        }

        ImGui.SameLine(180);
        bool showCustomTitle = placement.NamePlate.ShowCustomTitle;
        if (ImGui.Checkbox("Custom Title##ShowCustomTitle", ref showCustomTitle))
        {
            placement.NamePlate.ShowCustomTitle = showCustomTitle;
            sceneManager.SaveScenes();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 5. Translation (X, Y, Z)
        var pos = placement.Position;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.DragFloat3("##Translation", ref pos, 0.05f, 0, 0, "%.3f"))
        {
            placement.Position = pos;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, pos, placement.Rotation);
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Translation");

        // 6. Rotation (度数法表示)
        float rotDeg = placement.Rotation * (180f / MathF.PI);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.DragFloat("##Rotation", ref rotDeg, 0.5f, -180f, 180f, "%.3f"))
        {
            float rotRad = rotDeg * (MathF.PI / 180f);
            placement.Rotation = rotRad;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, placement.Position, rotRad);
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Rotation");

        // 7. Scale
        float scale = placement.Scale;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 130);
        if (ImGui.DragFloat("##Scale", ref scale, 0.01f, 0.01f, 10.0f, "%.3f"))
        {
            placement.Scale = scale;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                spawned.Transform.Scale = scale;
                actorManager.UpdateActorTransform(spawned, placement.Position, placement.Rotation, scale);
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Scale");

        ImGui.Spacing();

        // 8. Apply Own Transform (自キャラの現在座標・向きを適用)
        if (ImGui.Button("Apply Own Transform", new Vector2(170, 26)))
        {
            var (myPos, myRot) = GetPlayerTransform();
            placement.Position = myPos;
            placement.Rotation = myRot;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                actorManager.UpdateActorTransform(spawned, myPos, myRot);
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Copies the local player's current world position and facing angle to this placement.");
        }

        ImGui.SameLine();

        // 9. Default Scale (モンスターの原寸サイズまたは標準サイズ 1.0 に復元)
        if (ImGui.Button("Default Scale", new Vector2(120, 26)))
        {
            float defaultScale = (template != null && template.Scale > 0.001f) ? template.Scale : 1.0f;
            placement.Scale = defaultScale;
            sceneManager.SaveScenes();

            var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
            if (spawned != null)
            {
                spawned.Transform.Scale = defaultScale;
                actorManager.UpdateActorTransform(spawned, placement.Position, placement.Rotation, defaultScale);
            }
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Resets the scale to the template's default value (e.g. original monster size or 1.0).");
        }
    }

    private void DrawSceneTabPlaceholder(SceneData scene)
    {
        ImGui.TextUnformatted("マップアセット消去・空間環境設定 (Scene)");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped("マップ上の不要な家具や小道具（椅子やオブジェクトなど）を選択してピンポイントで消去できる機能（スポイト消去）を実装予定です（Phase 5）。");
        ImGui.Spacing();
        ImGui.TextDisabled($"登録済み消去アセット数: {scene.HiddenAssets.Count} 件");
    }

    private void DrawAnimationTab(SceneData scene)
    {
        var placement = sceneManager.SelectedPlacement;
        if (placement == null)
        {
            ImGui.TextDisabled("上段の一覧から編集するキャラクターを選択してください。");
            return;
        }

        var template = configuration.Templates.FirstOrDefault(t => t.Id == placement.CharacterTemplateId);
        string dName = !string.IsNullOrWhiteSpace(placement.CustomDisplayName) ? placement.CustomDisplayName : (template?.Name ?? "キャラクター");

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Editing Animation: {dName}");
        ImGui.Separator();
        ImGui.Spacing();

        // 1. Loop & LookAt & Speed controls
        bool isLoop = placement.Motion.IsLoop;
        if (ImGui.Checkbox("Loop Motion##AnimLoop", ref isLoop))
        {
            placement.Motion.IsLoop = isLoop;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Enables seamless infinite playback of this motion.");
        }

        ImGui.SameLine(160);
        bool lookAt = placement.Motion.LookAtPlayer;
        if (ImGui.Checkbox("LookAt Player##AnimLookAt", ref lookAt))
        {
            placement.Motion.LookAtPlayer = lookAt;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Actor's head and body will naturally turn toward the local player when nearby.");
        }

        float speed = placement.Motion.Speed > 0.01f ? placement.Motion.Speed : 1.0f;
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("Speed##AnimSpeed", ref speed, 0.1f, 3.0f, "%.2fx"))
        {
            placement.Motion.Speed = speed;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 2. Motion / ActionTimeline Selector
        ImGui.TextUnformatted("Motion / ActionTimeline:");
        ImGui.SameLine();
        if (placement.Motion.TimelineId > 0)
        {
            string keyDesc = !string.IsNullOrWhiteSpace(placement.Motion.TimelineKey) ? placement.Motion.TimelineKey : $"ID {placement.Motion.TimelineId}";
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.3f, 1.0f), $"[{placement.Motion.TimelineId}] {keyDesc}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##ClearMotion"))
            {
                placement.Motion.TimelineId = 0;
                placement.Motion.TimelineKey = string.Empty;
                sceneManager.SaveScenes();
                ApplyCurrentMotion(placement);
            }
        }
        else
        {
            ImGui.TextDisabled("(None / Default Idle)");
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchMotion", "Search Motion / Emote (e.g. wave, sit, cheer, dance)...", ref motionSearchQuery, 64);

        if (gameDataService != null)
        {
            var timelines = gameDataService.SearchTimelines(motionSearchQuery, 100);
            if (ImGui.BeginListBox("##MotionList", new Vector2(-1, 140)))
            {
                foreach (var t in timelines)
                {
                    bool isSelected = placement.Motion.TimelineId == t.Id;
                    string label = $"[{t.Id}] {t.Description} ({t.Key})";
                    if (ImGui.Selectable(label, isSelected))
                    {
                        placement.Motion.TimelineId = t.Id;
                        placement.Motion.TimelineKey = t.Key;
                        sceneManager.SaveScenes();
                        ApplyCurrentMotion(placement);
                    }
                }
                ImGui.EndListBox();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 3. Facial Expression
        ImGui.TextUnformatted("Facial Expression:");
        ImGui.SameLine();
        if (placement.Motion.FacialTimelineId > 0)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), $"[ID: {placement.Motion.FacialTimelineId}]");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##ClearFacial"))
            {
                placement.Motion.FacialTimelineId = 0;
                sceneManager.SaveScenes();
                ApplyCurrentMotion(placement);
            }
        }
        else
        {
            ImGui.TextDisabled("(Default / None)");
        }

        if (gameDataService != null)
        {
            var facials = gameDataService.GetFacialExpressions();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FacialCombo", placement.Motion.FacialTimelineId > 0 ? $"Facial ID: {placement.Motion.FacialTimelineId}" : "Select Facial Expression..."))
            {
                ImGui.InputTextWithHint("##SearchFacial", "Filter facial...", ref facialSearchQuery, 32);
                ImGui.Separator();

                if (ImGui.Selectable("(Default / None)", placement.Motion.FacialTimelineId == 0))
                {
                    placement.Motion.FacialTimelineId = 0;
                    sceneManager.SaveScenes();
                    ApplyCurrentMotion(placement);
                }

                foreach (var f in facials)
                {
                    if (!string.IsNullOrWhiteSpace(facialSearchQuery) && !f.Key.Contains(facialSearchQuery, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelected = placement.Motion.FacialTimelineId == f.Id;
                    if (ImGui.Selectable($"[{f.Id}] {f.Key}", isSelected))
                    {
                        placement.Motion.FacialTimelineId = f.Id;
                        sceneManager.SaveScenes();
                        ApplyCurrentMotion(placement);
                    }
                }
                ImGui.EndCombo();
            }
        }
    }

    private void ApplyCurrentMotion(SceneActorPlacement placement)
    {
        var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
        if (spawned != null && sceneManager.Animation != null)
        {
            sceneManager.Animation.ApplyMotion(spawned, placement.Motion, placement.Rotation);
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

    public void Dispose()
    {
    }
}
