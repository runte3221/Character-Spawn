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
using CharacterSpawn.Services;

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
    private int selectedMotionCategoryIndex = 0;
    private static readonly string[] MotionCategories = { "All", "Favorite", "Emotes", "NPC", "Monster", "Battle", "General" };
    private bool onlyModelSpecificMotions = false;

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
            case 3:
                DrawMovementTab(scene);
                break;
        }
    }

    #region 1. Top Tabs

    private void DrawTopTabs()
    {
        var tabActiveCol = new Vector4(0.7f, 0.15f, 0.15f, 1.0f); // Stagehand 風の赤アクセント
        var tabInactiveCol = new Vector4(0.22f, 0.22f, 0.24f, 1.0f);
        string[] tabNames = { "Spawn", "Scene", "Animation", "Movement" };
        var tabBtnSize = new Vector2((ImGui.GetContentRegionAvail().X - (tabNames.Length - 1) * 8) / tabNames.Length, 26);

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
        if (ImGui.BeginChild("##ActorListBox", new Vector2(-1, listHeight), true))
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

        // 1. Loop Motion
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

        ImGui.Spacing();

        // 2. LookAt Player & LookAt Custom Spawn (排他制御・画像3レイアウト)
        bool lookAtPlayer = placement.Motion.LookAtPlayer;
        if (ImGui.Checkbox("LookAt Player##AnimLookAt", ref lookAtPlayer))
        {
            placement.Motion.LookAtPlayer = lookAtPlayer;
            if (lookAtPlayer)
            {
                placement.Motion.LookAtCustomSpawn = false;
            }
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Actor's head and eyes naturally turn toward the local player when nearby.");
        }

        // 右列: LookAt Custom Spawn
        ImGui.SameLine(360);
        bool lookAtCustom = placement.Motion.LookAtCustomSpawn;
        if (ImGui.Checkbox("LookAt Custom Spawn##AnimLookAtCustom", ref lookAtCustom))
        {
            placement.Motion.LookAtCustomSpawn = lookAtCustom;
            if (lookAtCustom)
            {
                placement.Motion.LookAtPlayer = false;
                if (placement.Motion.LookAtTargetPlacementId == Guid.Empty)
                {
                    var other = scene.Placements.FirstOrDefault(p => p.PlacementId != placement.PlacementId);
                    if (other != null)
                    {
                        placement.Motion.LookAtTargetPlacementId = other.PlacementId;
                    }
                }
            }
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Actor's head, eyes, and body turn toward another custom spawn actor in the same scene.");
        }

        ImGui.Spacing();

        // 3. パラメータ行 (左列: Distance, Body Turn, Speed / 右列: Target)
        // 行 1: Distance (左) ＆ Target (右)
        float lookAtDist = placement.Motion.LookAtMaxDistance > 0.1f ? placement.Motion.LookAtMaxDistance : 15.0f;
        ImGui.TextUnformatted("Distance");
        ImGui.SameLine(85);
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat("##AnimLookAtDist", ref lookAtDist, 1.0f, 50.0f, "%.1fm"))
        {
            placement.Motion.LookAtMaxDistance = lookAtDist;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##ResetLookAtDist"))
        {
            placement.Motion.LookAtMaxDistance = 15.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Distance at which the actor begins and stops tracking the player or target (Default: 15.0m).\nClick Reset to return to 15.0m.");
        }

        // 右列: Target
        ImGui.SameLine(360);
        ImGui.TextUnformatted("Target");
        ImGui.SameLine(420);
        ImGui.SetNextItemWidth(180);

        var otherPlacements = scene.Placements.Where(p => p.PlacementId != placement.PlacementId).ToList();
        var currentTarget = otherPlacements.FirstOrDefault(p => p.PlacementId == placement.Motion.LookAtTargetPlacementId);
        string currentTargetName = currentTarget != null
            ? (!string.IsNullOrWhiteSpace(currentTarget.CustomDisplayName) ? currentTarget.CustomDisplayName : (configuration.Templates.FirstOrDefault(t => t.Id == currentTarget.CharacterTemplateId)?.Name ?? "アクター"))
            : "(None)";

        if (ImGui.BeginCombo("##LookAtTargetSelect", currentTargetName))
        {
            if (ImGui.Selectable("(None)", placement.Motion.LookAtTargetPlacementId == Guid.Empty))
            {
                placement.Motion.LookAtTargetPlacementId = Guid.Empty;
                sceneManager.SaveScenes();
                ApplyCurrentMotion(placement);
            }

            foreach (var other in otherPlacements)
            {
                var oTemplate = configuration.Templates.FirstOrDefault(t => t.Id == other.CharacterTemplateId);
                string oName = !string.IsNullOrWhiteSpace(other.CustomDisplayName) ? other.CustomDisplayName : (oTemplate?.Name ?? "アクター");
                float d = Vector3.Distance(placement.Position, other.Position);
                string itemLabel = $"{oName} ({d:F1}m)";
                bool isSelected = placement.Motion.LookAtTargetPlacementId == other.PlacementId;

                if (ImGui.Selectable($"{itemLabel}##{other.PlacementId}", isSelected))
                {
                    placement.Motion.LookAtTargetPlacementId = other.PlacementId;
                    sceneManager.SaveScenes();
                    ApplyCurrentMotion(placement);
                }
            }
            ImGui.EndCombo();
        }

        // ターゲットまでの実距離と範囲外警告 ＆ ワンクリック自動調整 (Fit) ボタン
        if (currentTarget != null)
        {
            float actualDist = Vector3.Distance(placement.Position, currentTarget.Position);
            ImGui.SameLine();
            if (actualDist > lookAtDist)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.15f, 1.0f), $"({actualDist:F1}m ⚠️超過)");
                ImGui.SameLine();
                if (ImGui.SmallButton("Fit##FitLookAtDist"))
                {
                    placement.Motion.LookAtMaxDistance = MathF.Ceiling(actualDist + 2.0f);
                    sceneManager.SaveScenes();
                    ApplyCurrentMotion(placement);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Distance ({lookAtDist:F1}m) is smaller than target distance ({actualDist:F1}m)!\nClick to set Distance to {MathF.Ceiling(actualDist + 2.0f):F0}m so the actor can look at the target.");
                }
            }
            else
            {
                ImGui.TextDisabled($"({actualDist:F1}m)");
            }
        }

        // 行 2: Body Turn
        float bodyTurn = placement.Motion.BodyTurnAngleLimit;
        ImGui.TextUnformatted("Body Turn");
        ImGui.SameLine(85);
        ImGui.SetNextItemWidth(120);
        string turnFmt = bodyTurn <= 0.01f ? "0°" : "%.0f°";
        if (ImGui.SliderFloat("##AnimBodyTurn", ref bodyTurn, 0f, 180f, turnFmt))
        {
            placement.Motion.BodyTurnAngleLimit = bodyTurn;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##ResetBodyTurn"))
        {
            placement.Motion.BodyTurnAngleLimit = 0.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Maximum body rotation angle toward player or target.\n0° = Body never rotates (Face & Eyes only)\n45° = Body turns up to ±45°\n180° = Full body rotation\nClick Reset to return to 0°.");
        }

        // 行 3: Speed
        float speed = placement.Motion.Speed > 0.01f ? placement.Motion.Speed : 1.0f;
        ImGui.TextUnformatted("Speed");
        ImGui.SameLine(85);
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat("##AnimSpeed", ref speed, 0.1f, 3.0f, "%.2fx"))
        {
            placement.Motion.Speed = speed;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##ResetSpeed"))
        {
            placement.Motion.Speed = 1.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMotion(placement);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset motion speed to 1.00x.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 3. Motion / ActionTimeline Selector
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

        // 対象アクターがモンスター・マウント・ミニオン・デミヒューマン等の場合、モデル固有モーション絞り込みトグルを表示（独立した行に配置）
        string? activeModelPrefix = null;
        if (template != null && template.ModelCharaId > 0 && gameDataService != null)
        {
            string? pfx = gameDataService.GetModelPrefix(template.ModelCharaId);
            if (!string.IsNullOrEmpty(pfx))
            {
                if (ImGui.Checkbox($"固有・共通アクションのみ ({template.Name})##ModelSpecificMotions", ref onlyModelSpecificMotions))
                {
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"Filters motions matching this actor's model ({pfx}) and common monster actions (walk, run, battle idle, attack, damage, death).");
                }
                if (onlyModelSpecificMotions)
                {
                    activeModelPrefix = pfx;
                }
            }
        }

        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("##MotionCategory", ref selectedMotionCategoryIndex, MotionCategories, MotionCategories.Length))
        {
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Filter motion category (All, Emotes, NPC Motions, Monsters, Battles, General)");
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##SearchMotion", "Search Motion / Emote (e.g. wave, talk, guard, dance, walk, attack)...", ref motionSearchQuery, 64);

        if (gameDataService != null)
        {
            var timelines = gameDataService.SearchTimelines(motionSearchQuery, MotionCategories[selectedMotionCategoryIndex], activeModelPrefix, 500, configuration.FavoriteTimelineIds);
            if (ImGui.BeginListBox("##MotionList", new Vector2(-1, 180)))
            {
                foreach (var t in timelines)
                {
                    bool isFav = configuration.FavoriteTimelineIds.Contains(t.Id);
                    if (isFav)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.85f, 0.2f, 1.0f));
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.45f, 0.45f, 0.5f));
                    }

                    if (ImGuiComponents.IconButton($"##Fav_{t.Id}", FontAwesomeIcon.Star))
                    {
                        if (isFav)
                            configuration.FavoriteTimelineIds.Remove(t.Id);
                        else
                            configuration.FavoriteTimelineIds.Add(t.Id);
                        configuration.Save();
                    }
                    ImGui.PopStyleColor();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(isFav ? "お気に入りを解除" : "お気に入りに追加");
                    }

                    ImGui.SameLine();

                    bool isSelected = placement.Motion.TimelineId == t.Id;
                    string label = $"[{t.Id}] {t.Description} ({t.Key})";
                    if (ImGui.Selectable($"{label}##Motion_{t.Id}", isSelected))
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
            string comboPreview = "(Default / None)";
            if (placement.Motion.FacialTimelineId > 0)
            {
                var matched = facials.FirstOrDefault(f => f.Id == placement.Motion.FacialTimelineId);
                comboPreview = matched != null ? $"[{matched.Id}] {matched.Description}" : $"Facial ID: {placement.Motion.FacialTimelineId}";
            }

            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##FacialCombo", comboPreview))
            {
                ImGui.InputTextWithHint("##SearchFacial", "Filter facial (e.g. smile, angry, wink, laugh)...", ref facialSearchQuery, 32);
                ImGui.Separator();

                if (ImGui.Selectable("(Default / None)", placement.Motion.FacialTimelineId == 0))
                {
                    placement.Motion.FacialTimelineId = 0;
                    sceneManager.SaveScenes();
                    ApplyCurrentMotion(placement);
                }

                foreach (var f in facials)
                {
                    if (!string.IsNullOrWhiteSpace(facialSearchQuery) &&
                        !f.Description.Contains(facialSearchQuery, StringComparison.OrdinalIgnoreCase) &&
                        !f.Key.Contains(facialSearchQuery, StringComparison.OrdinalIgnoreCase) &&
                        !f.Id.ToString().Contains(facialSearchQuery))
                        continue;

                    bool isSelected = placement.Motion.FacialTimelineId == f.Id;
                    if (ImGui.Selectable($"[{f.Id}] {f.Description}", isSelected))
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

    private void DrawMovementTab(SceneData scene)
    {
        var placement = sceneManager.SelectedPlacement;
        if (placement == null)
        {
            ImGui.TextDisabled("上段の一覧から編集するキャラクターを選択してください。");
            return;
        }

        placement.Movement ??= new();
        var move = placement.Movement;

        // 1. 移動モード選択 (Movement Mode)
        ImGui.TextUnformatted("Movement Mode");
        ImGui.SameLine(130);
        int modeIdx = (int)move.Mode;
        string[] modeLabels = { "None (静止)", "Patrol (巡回)", "Follow Player (追従)", "Patrol + Follow (巡回+追従)" };
        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("##MovementModeCombo", ref modeIdx, modeLabels, modeLabels.Length))
        {
            move.Mode = (MovementMode)modeIdx;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (move.Mode == MovementMode.None)
        {
            ImGui.TextDisabled("自律移動は無効です。このアクターはその場に留まります。");
            return;
        }

        // 2. 移動速度・旋回速度
        float speed = move.Speed;
        ImGui.TextUnformatted("Speed");
        ImGui.SameLine(130);
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderFloat("##MoveSpeedSlider", ref speed, 0.5f, 10.0f, "%.1f m/s"))
        {
            move.Speed = speed;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("歩き (2.0)##SetWalkSpeed"))
        {
            move.Speed = 2.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("駆け足 (4.0)##SetJogSpeed"))
        {
            move.Speed = 4.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("走り (6.0)##SetRunSpeed"))
        {
            move.Speed = 6.0f;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }

        float turnSpeed = move.TurnSpeed;
        ImGui.TextUnformatted("Turn Speed");
        ImGui.SameLine(130);
        ImGui.SetNextItemWidth(140);
        if (ImGui.SliderFloat("##TurnSpeedSlider", ref turnSpeed, 90f, 720f, "%.0f °/s"))
        {
            move.TurnSpeed = turnSpeed;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset##ResetTurnSpeed"))
        {
            move.TurnSpeed = 360f;
            sceneManager.SaveScenes();
            ApplyCurrentMovement(placement);
        }

        // 3. プレイヤー接近追従設定 (FollowPlayer または PatrolAndFollow の場合)
        if (move.Mode == MovementMode.FollowPlayer || move.Mode == MovementMode.PatrolAndFollow)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "Player Follow & Return (接近追従 ＆ 帰還設定)");

            float trigDist = move.FollowTriggerDistance;
            ImGui.TextUnformatted("Trigger Dist");
            ImGui.SameLine(130);
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("##FollowTrigDist", ref trigDist, 1.0f, 15.0f, "%.1f m"))
            {
                move.FollowTriggerDistance = trigDist;
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("プレイヤーがこの距離内に入ったら歩み寄りを開始します (デフォルト: 4.0m)");

            float stopDist = move.FollowStopDistance;
            ImGui.TextUnformatted("Stop Dist");
            ImGui.SameLine(130);
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("##FollowStopDist", ref stopDist, 0.5f, 5.0f, "%.1f m"))
            {
                move.FollowStopDistance = stopDist;
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("プレイヤーの手前この距離で立ち止まります (デフォルト: 1.8m)");

            float maxTerritory = move.MaxTerritoryDistance;
            ImGui.TextUnformatted("Territory Limit");
            ImGui.SameLine(130);
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("##MaxTerritoryDist", ref maxTerritory, 3.0f, 30.0f, "%.1f m"))
            {
                move.MaxTerritoryDistance = maxTerritory;
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("初期配置（ホーム）からこの距離以上離れたら追従を中断して戻ります (デフォルト: 15.0m)");

            bool retHome = move.ReturnToHome;
            ImGui.SetCursorPosX(130);
            if (ImGui.Checkbox("Return to Home upon loss (追従解除時にホームへ自律帰還)##RetHome", ref retHome))
            {
                move.ReturnToHome = retHome;
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
        }

        // 4. ウェイポイント巡回設定 (Patrol または PatrolAndFollow の場合)
        if (move.Mode == MovementMode.Patrol || move.Mode == MovementMode.PatrolAndFollow)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.5f, 0.95f, 0.5f, 1.0f), "Patrol Route & Waypoints (巡回ルート・通過地点)");

            int loopIdx = (int)move.LoopType;
            string[] loopLabels = { "Loop (循環: A->B->C->A...)", "PingPong (往復: A->B->C->B...)", "Once (片道: A->B->C 停止)" };
            ImGui.TextUnformatted("Loop Type");
            ImGui.SameLine(130);
            ImGui.SetNextItemWidth(220);
            if (ImGui.Combo("##PatrolLoopTypeCombo", ref loopIdx, loopLabels, loopLabels.Length))
            {
                move.LoopType = (PatrolLoopType)loopIdx;
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }

            ImGui.Spacing();

            // ウェイポイント追加ボタン
            if (ImGui.Button("📍 自キャラ位置を追加##AddPlayerPosWp"))
            {
                var (pPos, _) = GetPlayerTransform();
                move.Waypoints.Add(new SceneActorWaypoint
                {
                    Position = pPos,
                    Description = $"WP #{move.Waypoints.Count + 1}"
                });
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
            ImGui.SameLine();
            if (ImGui.Button("📍 アクター位置を追加##AddActorPosWp"))
            {
                var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
                Vector3 addPos = spawned != null ? spawned.Transform.Position : placement.Position;
                move.Waypoints.Add(new SceneActorWaypoint
                {
                    Position = addPos,
                    Description = $"WP #{move.Waypoints.Count + 1}"
                });
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }
            ImGui.SameLine();
            if (move.Waypoints.Count > 0 && ImGui.Button("全消去##ClearWps"))
            {
                move.Waypoints.Clear();
                sceneManager.SaveScenes();
                ApplyCurrentMovement(placement);
            }

            ImGui.Spacing();

            // ウェイポイント一覧
            if (move.Waypoints.Count == 0)
            {
                ImGui.TextDisabled("通過地点（ウェイポイント）が登録されていません。\n上の「📍 自キャラ位置を追加」ボタンを押して巡回ルートを作成してください。");
            }
            else
            {
                if (ImGui.BeginChild("##WaypointsListArea", new Vector2(0, 160), true))
                {
                    for (int i = 0; i < move.Waypoints.Count; i++)
                    {
                        var wp = move.Waypoints[i];
                        ImGui.PushID($"WP_Row_{i}");

                        ImGui.TextColored(new Vector4(0.9f, 0.75f, 0.2f, 1.0f), $"#{i + 1}");
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"<{wp.Position.X:F1}, {wp.Position.Y:F1}, {wp.Position.Z:F1}>");

                        ImGui.SameLine(180);
                        float wait = wp.WaitSeconds;
                        ImGui.SetNextItemWidth(60);
                        if (ImGui.DragFloat("##WaitSec", ref wait, 0.5f, 0f, 60f, "%.1fs"))
                        {
                            wp.WaitSeconds = wait;
                            sceneManager.SaveScenes();
                            ApplyCurrentMovement(placement);
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("到着後の待機秒数 (0=ノンストップ通過)");

                        // 上へ・下へボタン
                        ImGui.SameLine();
                        ImGui.BeginDisabled(i == 0);
                        if (ImGui.SmallButton("▲##MoveUpWp"))
                        {
                            (move.Waypoints[i], move.Waypoints[i - 1]) = (move.Waypoints[i - 1], move.Waypoints[i]);
                            sceneManager.SaveScenes();
                            ApplyCurrentMovement(placement);
                        }
                        ImGui.EndDisabled();

                        ImGui.SameLine();
                        ImGui.BeginDisabled(i == move.Waypoints.Count - 1);
                        if (ImGui.SmallButton("▼##MoveDownWp"))
                        {
                            (move.Waypoints[i], move.Waypoints[i + 1]) = (move.Waypoints[i + 1], move.Waypoints[i]);
                            sceneManager.SaveScenes();
                            ApplyCurrentMovement(placement);
                        }
                        ImGui.EndDisabled();

                        // 削除ボタン
                        ImGui.SameLine();
                        if (ImGui.SmallButton("✕##DelWp"))
                        {
                            move.Waypoints.RemoveAt(i);
                            sceneManager.SaveScenes();
                            ApplyCurrentMovement(placement);
                            ImGui.PopID();
                            break;
                        }

                        ImGui.PopID();
                    }
                }
                ImGui.EndChild();
            }
        }
    }

    private void ApplyCurrentMovement(SceneActorPlacement placement)
    {
        var spawned = sceneManager.GetSpawnedActor(placement.PlacementId);
        if (spawned != null && sceneManager.Movement != null)
        {
            if (placement.Movement.Mode == MovementMode.None)
            {
                sceneManager.Movement.StopMovement(placement.PlacementId);
            }
            else
            {
                sceneManager.Movement.StartMovement(placement, spawned);
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

    public void Dispose()
    {
    }
}
