using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImGuizmo;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using CharacterSpawn.Models;

namespace CharacterSpawn.UI;

public unsafe class GizmoRenderer
{
    private readonly IGameGui gameGui;
    private readonly Configuration configuration;

    public bool IsManipulating => ImGuizmo.IsUsing();

    public GizmoRenderer(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    /// <summary>
    /// Stagehand スタイルのモード切り替えツールバー (Select / Translate / Rotate)
    /// </summary>
    public void DrawToolbar()
    {
        var activeCol = new Vector4(0.65f, 0.18f, 0.18f, 1.0f); // Stagehand 風の赤背景
        var btnSize = new Vector2(28, 28);

        // 1. Select (No Gizmo)
        bool isSelect = configuration.CurrentGizmoMode == GizmoMode.Select;
        if (isSelect) ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
        if (ImGuiComponents.IconButton("##GizmoSelectBtn", FontAwesomeIcon.MousePointer))
        {
            configuration.CurrentGizmoMode = GizmoMode.Select;
            configuration.Save();
        }
        if (isSelect) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Select (Hide Gizmo)");

        ImGui.SameLine();

        // 2. Translate (Move with Axis & Quad Planes)
        bool isTranslate = configuration.CurrentGizmoMode == GizmoMode.Translate;
        if (isTranslate) ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
        if (ImGuiComponents.IconButton("##GizmoTranslateBtn", FontAwesomeIcon.ArrowsUpDownLeftRight))
        {
            configuration.CurrentGizmoMode = GizmoMode.Translate;
            configuration.Save();
        }
        if (isTranslate) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Translate (Move along X, Y, Z axis or XY, XZ, YZ quad planes)");

        ImGui.SameLine();

        // 3. Rotate (Rotate with Rings)
        bool isRotate = configuration.CurrentGizmoMode == GizmoMode.Rotate;
        if (isRotate) ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
        if (ImGuiComponents.IconButton("##GizmoRotateBtn", FontAwesomeIcon.SyncAlt))
        {
            configuration.CurrentGizmoMode = GizmoMode.Rotate;
            configuration.Save();
        }
        if (isRotate) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate (Rotate along X, Y, Z rings)");

        ImGui.SameLine();

        // 4. Scale (Expand / Shrink)
        bool isScale = configuration.CurrentGizmoMode == GizmoMode.Scale;
        if (isScale) ImGui.PushStyleColor(ImGuiCol.Button, activeCol);
        if (ImGuiComponents.IconButton("##GizmoScaleBtn", FontAwesomeIcon.ExpandAlt))
        {
            configuration.CurrentGizmoMode = GizmoMode.Scale;
            configuration.Save();
        }
        if (isScale) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scale (Resize character)");
    }

    private bool isHoveredOrUsing = false;

    public void Render(
        SpawnedActorData? selectedActor,
        Action<Vector3, float, float?> onTransformChanged,
        IReadOnlyList<SceneActorWaypoint>? waypoints = null,
        PatrolLoopType loopType = PatrolLoopType.Loop,
        Vector3? homePosition = null)
    {
        // FFXIV ゲームカメラの取得 (Stagehand / BDTH 準拠)
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null || cameraManager->CurrentCamera->RenderCamera == null)
            return;

        var camera = cameraManager->CurrentCamera;
        var renderCamera = camera->RenderCamera;

        var viewMatrix = camera->ViewMatrix;
        var projMatrix = renderCamera->ProjectionMatrix;

        // FFXIV リバースZ深度プロジェクションの ImGuizmo 補正 (Stagehand & BDTH 準拠)
        var far = renderCamera->FarPlane;
        var near = renderCamera->NearPlane;
        var clip = far / (far - near);

        projMatrix.M43 = -(clip * near);
        projMatrix.M33 = -((far + near) / (far - near));
        viewMatrix.M44 = 1.0f;

        // フルスクリーン透明オーバーレイウィンドウ
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);

        var flags = ImGuiWindowFlags.NoDecoration |
                    ImGuiWindowFlags.NoSavedSettings |
                    ImGuiWindowFlags.NoFocusOnAppearing |
                    ImGuiWindowFlags.NoNav |
                    ImGuiWindowFlags.NoBackground |
                    ImGuiWindowFlags.NoBringToFrontOnFocus;

        if (!isHoveredOrUsing)
        {
            flags |= ImGuiWindowFlags.NoInputs;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);

        if (ImGui.Begin("##CharacterSpawnGizmoOverlay", flags))
        {
            // 3D 空間上のウェイポイント巡回ルートライン描画
            if (waypoints != null && waypoints.Count > 0)
            {
                RenderWaypointsPath(waypoints, loopType, homePosition);
            }

            if (selectedActor != null && selectedActor.IsSpawned && configuration.CurrentGizmoMode != GizmoMode.Select)
            {
                ImGuizmo.BeginFrame();

                ImGuizmo.SetDrawlist();
                ImGuizmo.Enable(true);
                ImGuizmo.SetID((int)ImGui.GetID("CharacterSpawnGizmo"));
                ImGuizmo.SetOrthographic(false);

                var vp = ImGui.GetWindowViewport();
                ImGuizmo.SetRect(vp.Pos.X, vp.Pos.Y, vp.Size.X, vp.Size.Y);

            // ターゲットアクターの Transform 行列を構築 (Quaternion を用いて 180 度境界でのフリップ・ジンバルロックを完全に防止)
            var pos = selectedActor.Transform.Position;
            var rotQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitY, selectedActor.Transform.Rotation);
            var scaleVec = Vector3.One * (selectedActor.Transform.Scale > 0 ? selectedActor.Transform.Scale : 1.0f);

            var matrix = Matrix4x4.CreateScale(scaleVec) *
                         Matrix4x4.CreateFromQuaternion(rotQuat) *
                         Matrix4x4.CreateTranslation(pos);

            // 操作モード: Translate / Rotate / Scale
            var op = configuration.CurrentGizmoMode switch
            {
                GizmoMode.Rotate => ImGuizmoOperation.RotateY,
                GizmoMode.Scale => ImGuizmoOperation.Scale,
                _ => ImGuizmoOperation.Translate
            };

            var mode = ImGuizmoMode.World;

            // ImGuizmo によるマニピュレート
            if (ImGuizmo.Manipulate(ref viewMatrix.M11, ref projMatrix.M11, op, mode, ref matrix.M11))
            {
                if (Matrix4x4.Decompose(matrix, out var newScale, out var newRot, out var newPos))
                {
                    // クォータニオンから前方ベクトルを算出し、Atan2 で 360 度シームレスに水平回転角（Yaw）を導出
                    var forward = Vector3.Transform(Vector3.UnitZ, newRot);
                    float newYawRad = MathF.Atan2(forward.X, forward.Z);

                    // Scale モード操作時のみ均等スケールを導出して通知 (移動・回転モードでの不要なスケール破壊を完全防止)
                    float? updatedScale = null;
                    if (configuration.CurrentGizmoMode == GizmoMode.Scale)
                    {
                        updatedScale = Math.Clamp((newScale.X + newScale.Y + newScale.Z) / 3.0f, 0.01f, 10.0f);
                    }

                    onTransformChanged(newPos, newYawRad, updatedScale);
                }
            }

                // 次フレームの NoInputs 判定用にホバー・使用状態を記録
                isHoveredOrUsing = ImGuizmo.IsOver() || ImGuizmo.IsUsing();

                ImGuizmo.SetID(-1);
            }

            ImGui.End();
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// 3D 空間上にウェイポイント巡回ルート（パスラインと番号ピン）を描画
    /// </summary>
    public void RenderWaypointsPath(IReadOnlyList<SceneActorWaypoint> waypoints, PatrolLoopType loopType, Vector3? homePosition = null)
    {
        if (waypoints == null || waypoints.Count == 0) return;

        var drawList = ImGui.GetWindowDrawList();
        var yellowLineCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 0.85f));
        var loopLineCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 0.45f));
        var homeLineCol = ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 1.0f, 0.65f));
        var circleFillCol = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.18f, 0.9f));
        var circleBorderCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 1.0f));
        var textCol = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

        Vector2 prevScreenPos = Vector2.Zero;
        bool hasPrev = false;

        // ホーム位置から最初のウェイポイントへの接続線
        if (homePosition.HasValue && gameGui.WorldToScreen(homePosition.Value, out var homeScreenPos))
        {
            if (gameGui.WorldToScreen(waypoints[0].Position, out var firstWpScreenPos))
            {
                drawList.AddLine(homeScreenPos, firstWpScreenPos, homeLineCol, 1.5f);
            }
        }

        Vector2 firstScreenPos = Vector2.Zero;
        bool hasFirst = false;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            if (gameGui.WorldToScreen(wp.Position, out var screenPos))
            {
                if (!hasFirst)
                {
                    firstScreenPos = screenPos;
                    hasFirst = true;
                }

                // 前の地点からの線
                if (hasPrev)
                {
                    drawList.AddLine(prevScreenPos, screenPos, yellowLineCol, 2.5f);
                }

                // 地点マーカー (ピン)
                float radius = 10f;
                drawList.AddCircleFilled(screenPos, radius, circleFillCol);
                drawList.AddCircle(screenPos, radius, circleBorderCol, 16, 2.0f);

                string numStr = $"{i + 1}";
                var textSize = ImGui.CalcTextSize(numStr);
                drawList.AddText(new Vector2(screenPos.X - textSize.X * 0.5f, screenPos.Y - textSize.Y * 0.5f), textCol, numStr);

                prevScreenPos = screenPos;
                hasPrev = true;
            }
            else
            {
                hasPrev = false;
            }
        }

        // Loop の場合、末尾から先頭へ線を結ぶ
        if (loopType == PatrolLoopType.Loop && hasPrev && hasFirst && waypoints.Count > 1)
        {
            drawList.AddLine(prevScreenPos, firstScreenPos, loopLineCol, 1.5f);
        }
    }
}
