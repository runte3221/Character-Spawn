using System;
using System.Collections.Generic;
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
#pragma warning disable CS0414
    private readonly IGameGui gameGui;
#pragma warning restore CS0414
    private readonly Configuration configuration;

    public bool IsManipulating => ImGuizmo.IsUsing();

    public GizmoRenderer(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    /// <summary>
    /// Stagehand スタイルのモード切り替えツールバー (Select / Translate / Rotate / Scale)
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

    /// <summary>
    /// 3D 空間上のモデルを直接クリックしてアクターを選択するヒットテスト。
    /// スクリーン空間投影とカメラ距離（最前面優先）を用いて高精度に判定し、ImGui/ImGuizmo 操作中は除外する。
    /// </summary>
    public void CheckActorClickSelection(
        IReadOnlyList<(SceneActorPlacement placement, SpawnedActorData actor)> spawnedActors,
        SceneActorPlacement? currentSelected,
        Action<SceneActorPlacement> onSelect)
    {
        if (spawnedActors == null || spawnedActors.Count == 0) return;

        // ImGui ウィンドウ操作中または ImGuizmo 操作中はクリック判定を行わない
        var io = ImGui.GetIO();
        if (io.WantCaptureMouse || ImGuizmo.IsUsing() || ImGuizmo.IsOver())
            return;

        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null || cameraManager->CurrentCamera->RenderCamera == null)
            return;

        var camera = cameraManager->CurrentCamera;
        var renderCamera = camera->RenderCamera;
        var viewMatrix = camera->ViewMatrix;
        var projMatrix = renderCamera->ProjectionMatrix;
        var viewport = ImGuiHelpers.MainViewport;
        var vpPos = viewport.Pos;
        var vpSize = viewport.Size;
        var viewProj = viewMatrix * projMatrix;

        if (!Matrix4x4.Invert(viewMatrix, out var invView))
            return;
        var cameraPos = invView.Translation;

        var mousePos = io.MousePos;
        bool isLeftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        SceneActorPlacement? bestPlacement = null;
        SpawnedActorData? bestActor = null;
        float closestDistSq = float.MaxValue;

        foreach (var (placement, actor) in spawnedActors)
        {
            if (actor == null || !actor.IsSpawned) continue;

            var feetPos = actor.Transform.Position;
            float scale = actor.Transform.Scale > 0.001f ? actor.Transform.Scale : 1.0f;
            float actorHeight = 1.85f * scale;
            var headPos = feetPos + new Vector3(0, actorHeight, 0);

            if (!ProjectWorldToScreen(feetPos, viewProj, vpPos, vpSize, out var feetScreen))
                continue;
            if (!ProjectWorldToScreen(headPos, viewProj, vpPos, vpSize, out var headScreen))
                continue;

            float screenHeight = MathF.Abs(feetScreen.Y - headScreen.Y);
            if (screenHeight < 10f) screenHeight = 10f;

            float screenWidth = MathF.Max(screenHeight * 0.45f, 24f);
            float centerX = (feetScreen.X + headScreen.X) * 0.5f;
            float minX = MathF.Min(centerX - screenWidth * 0.5f, MathF.Min(feetScreen.X, headScreen.X) - screenWidth * 0.2f);
            float maxX = MathF.Max(centerX + screenWidth * 0.5f, MathF.Max(feetScreen.X, headScreen.X) + screenWidth * 0.2f);
            float minY = MathF.Min(feetScreen.Y, headScreen.Y) - screenHeight * 0.08f;
            float maxY = MathF.Max(feetScreen.Y, headScreen.Y) + screenHeight * 0.05f;

            if (mousePos.X >= minX && mousePos.X <= maxX && mousePos.Y >= minY && mousePos.Y <= maxY)
            {
                float distSq = Vector3.DistanceSquared(cameraPos, feetPos);
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    bestPlacement = placement;
                    bestActor = actor;
                }
            }
        }

        if (bestPlacement != null && bestActor != null)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            if (bestPlacement != currentSelected)
            {
                var foregroundDrawList = ImGui.GetForegroundDrawList(viewport);
                var hoverCircleCol = ImGui.GetColorU32(new Vector4(0.35f, 0.75f, 1.0f, 0.65f));
                float circleRadius = MathF.Max(0.5f, 0.6f * (bestActor.Transform.Scale > 0.001f ? bestActor.Transform.Scale : 1.0f));
                DrawHorizontalCircle(foregroundDrawList, bestActor.Transform.Position, circleRadius, hoverCircleCol, 2.5f, viewProj, vpPos, vpSize, 36);
            }

            if (isLeftClicked)
            {
                onSelect(bestPlacement);
            }
        }
    }

    private bool isHoveredOrUsing = false;

    public void Render(
        SpawnedActorData? selectedActor,
        Action<Vector3, float, float?> onTransformChanged,
        IReadOnlyList<SceneActorWaypoint>? waypoints = null,
        PatrolLoopType loopType = PatrolLoopType.Loop,
        Vector3? homePosition = null,
        SceneActorPlacement? selectedPlacement = null)
    {
        // FFXIV ゲームカメラの取得 (Stagehand / BDTH 準拠)
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null || cameraManager->CurrentCamera->RenderCamera == null)
            return;

        var camera = cameraManager->CurrentCamera;
        var renderCamera = camera->RenderCamera;

        var viewMatrix = camera->ViewMatrix;
        var projMatrix = renderCamera->ProjectionMatrix;

        var viewport = ImGuiHelpers.MainViewport;
        var vpPos = viewport.Pos;
        var vpSize = viewport.Size;
        var viewProj = viewMatrix * projMatrix;

        // =========================================================================
        // 1. 3D 空間オーバーレイ描画 (パスライン・ピン・範囲円・扇形)
        // メインビューポートの ForegroundDrawList に直接描画し、ジッターやウィンドウ干渉を根絶
        // =========================================================================
        if (configuration.ShowVisualOverlays)
        {
            var foregroundDrawList = ImGui.GetForegroundDrawList(viewport);

            // ウェイポイント巡回ルート描画
            if (configuration.ShowWaypointPath && waypoints != null && waypoints.Count > 0)
            {
                RenderWaypointsPath(foregroundDrawList, waypoints, loopType, homePosition, viewProj, vpPos, vpSize);
            }

            // 範囲円・Body Turn 扇形描画
            if (selectedPlacement != null)
            {
                var actorPos = selectedActor != null && selectedActor.IsSpawned
                    ? selectedActor.Transform.Position
                    : selectedPlacement.Position;

                var actorRot = selectedActor != null && selectedActor.IsSpawned
                    ? selectedActor.Transform.Rotation
                    : selectedPlacement.Rotation;

                RenderRangeOverlays(foregroundDrawList, actorPos, actorRot, selectedPlacement, viewProj, vpPos, vpSize);
            }
        }

        // =========================================================================
        // 2. ImGuizmo 3D マニピュレータ (必要な時のみウィンドウを開いて操作)
        // =========================================================================
        if (selectedActor != null && selectedActor.IsSpawned && configuration.CurrentGizmoMode != GizmoMode.Select)
        {
            // FFXIV リバースZ深度プロジェクションの ImGuizmo 補正 (Stagehand & BDTH 準拠)
            var far = renderCamera->FarPlane;
            var near = renderCamera->NearPlane;
            var clip = far / (far - near);

            var imguizmoProj = projMatrix;
            imguizmoProj.M43 = -(clip * near);
            imguizmoProj.M33 = -((far + near) / (far - near));
            var imguizmoView = viewMatrix;
            imguizmoView.M44 = 1.0f;

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
                ImGuizmo.BeginFrame();
                ImGuizmo.SetDrawlist();
                ImGuizmo.Enable(true);
                ImGuizmo.SetID((int)ImGui.GetID("CharacterSpawnGizmo"));
                ImGuizmo.SetOrthographic(false);

                var vp = ImGui.GetWindowViewport();
                ImGuizmo.SetRect(vp.Pos.X, vp.Pos.Y, vp.Size.X, vp.Size.Y);

                // ターゲットアクターの Transform 行列を構築
                var pos = selectedActor.Transform.Position;
                var rotQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitY, selectedActor.Transform.Rotation);
                var scaleVec = Vector3.One * (selectedActor.Transform.Scale > 0 ? selectedActor.Transform.Scale : 1.0f);

                var matrix = Matrix4x4.CreateScale(scaleVec) *
                             Matrix4x4.CreateFromQuaternion(rotQuat) *
                             Matrix4x4.CreateTranslation(pos);

                var op = configuration.CurrentGizmoMode switch
                {
                    GizmoMode.Rotate => ImGuizmoOperation.RotateY,
                    GizmoMode.Scale => ImGuizmoOperation.Scale,
                    _ => ImGuizmoOperation.Translate
                };

                var mode = ImGuizmoMode.World;

                if (ImGuizmo.Manipulate(ref imguizmoView.M11, ref imguizmoProj.M11, op, mode, ref matrix.M11))
                {
                    if (Matrix4x4.Decompose(matrix, out var newScale, out var newRot, out var newPos))
                    {
                        var forward = Vector3.Transform(Vector3.UnitZ, newRot);
                        float newYawRad = MathF.Atan2(forward.X, forward.Z);

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

                ImGui.End();
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// 3D 空間上にウェイポイント巡回ルート（パスラインと番号ピン）を描画
    /// </summary>
    private void RenderWaypointsPath(
        ImDrawListPtr drawList,
        IReadOnlyList<SceneActorWaypoint> waypoints,
        PatrolLoopType loopType,
        Vector3? homePosition,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize)
    {
        if (waypoints == null || waypoints.Count == 0) return;

        var yellowLineCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 0.85f));
        var loopLineCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 0.45f));
        var homeLineCol = ImGui.GetColorU32(new Vector4(0.4f, 0.8f, 1.0f, 0.65f));
        var circleFillCol = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.18f, 0.9f));
        var circleBorderCol = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 1.0f));
        var textCol = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 1.0f));

        Vector2 prevScreenPos = Vector2.Zero;
        bool hasPrev = false;

        // ホーム位置から最初のウェイポイントへの接続線
        if (homePosition.HasValue && ProjectWorldToScreen(homePosition.Value, viewProj, vpPos, vpSize, out var homeScreenPos))
        {
            if (ProjectWorldToScreen(waypoints[0].Position, viewProj, vpPos, vpSize, out var firstWpScreenPos))
            {
                drawList.AddLine(homeScreenPos, firstWpScreenPos, homeLineCol, 1.5f);
            }
        }

        Vector2 firstScreenPos = Vector2.Zero;
        bool hasFirst = false;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            if (ProjectWorldToScreen(wp.Position, viewProj, vpPos, vpSize, out var screenPos))
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

    /// <summary>
    /// 各種距離（Trigger Dist, Stop Dist, LookAt Dist）および Body Turn の 3D 可視化を描画
    /// </summary>
    private void RenderRangeOverlays(
        ImDrawListPtr drawList,
        Vector3 center,
        float rotation,
        SceneActorPlacement placement,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize)
    {
        // 1. 移動・追従範囲 (Trigger Dist & Stop Dist)
        if (configuration.ShowMovementRanges && placement.Movement != null)
        {
            // FollowPlayer または PatrolAndFollow の場合に追従範囲を描画
            if (placement.Movement.Mode == MovementMode.FollowPlayer || placement.Movement.Mode == MovementMode.PatrolAndFollow)
            {
                float triggerDist = placement.Movement.FollowTriggerDistance;
                float stopDist = placement.Movement.FollowStopDistance;

                // Stop Dist: ライムグリーン (#33FF66)
                var stopColor = ImGui.GetColorU32(new Vector4(0.2f, 1.0f, 0.4f, 0.85f));
                DrawHorizontalCircle(drawList, center, stopDist, stopColor, 2.0f, viewProj, vpPos, vpSize);
                DrawRangeLabel(drawList, center, stopDist, $"Stop: {stopDist:F1}m", stopColor, viewProj, vpPos, vpSize, 0f);

                // Trigger Dist: シアン・水色 (#33CCFF)
                var trigColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 1.0f, 0.85f));
                DrawHorizontalCircle(drawList, center, triggerDist, trigColor, 2.0f, viewProj, vpPos, vpSize);
                DrawRangeLabel(drawList, center, triggerDist, $"Trigger: {triggerDist:F1}m", trigColor, viewProj, vpPos, vpSize, MathF.PI * 0.25f);
            }
        }

        // 2. アニメーション・視線追従範囲 (LookAt Distance & Body Turn)
        if (configuration.ShowAnimationRanges && placement.Motion != null)
        {
            // LookAt が有効な場合 (LookAtPlayer または LookAtCustomSpawn)
            if (placement.Motion.LookAtPlayer || placement.Motion.LookAtCustomSpawn)
            {
                float lookAtDist = placement.Motion.LookAtMaxDistance;
                float bodyTurnAngle = placement.Motion.BodyTurnAngleLimit;

                // LookAt Distance: オレンジ色 (#FF9933)
                var lookAtColor = ImGui.GetColorU32(new Vector4(1.0f, 0.6f, 0.2f, 0.85f));
                DrawHorizontalCircle(drawList, center, lookAtDist, lookAtColor, 1.8f, viewProj, vpPos, vpSize, 64);
                DrawRangeLabel(drawList, center, lookAtDist, $"LookAt: {lookAtDist:F1}m", lookAtColor, viewProj, vpPos, vpSize, MathF.PI * 0.5f);

                // Body Turn: 黄色扇形 (#FFEE33)
                DrawBodyTurnArc(drawList, center, rotation, bodyTurnAngle, viewProj, vpPos, vpSize);
            }
        }
    }

    /// <summary>
    /// 水平面（XZ平面）上に指定半径の円を描画
    /// </summary>
    private void DrawHorizontalCircle(
        ImDrawListPtr drawList,
        Vector3 center,
        float radius,
        uint color,
        float thickness,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize,
        int segments = 48)
    {
        if (radius <= 0.05f) return;

        Vector2 prevPt = Vector2.Zero;
        bool hasPrev = false;

        float step = MathF.PI * 2.0f / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * step;
            var worldPt = new Vector3(
                center.X + MathF.Sin(angle) * radius,
                center.Y,
                center.Z + MathF.Cos(angle) * radius
            );

            if (ProjectWorldToScreen(worldPt, viewProj, vpPos, vpSize, out var screenPt))
            {
                if (hasPrev)
                {
                    drawList.AddLine(prevPt, screenPt, color, thickness);
                }
                prevPt = screenPt;
                hasPrev = true;
            }
            else
            {
                hasPrev = false;
            }
        }
    }

    /// <summary>
    /// Body Turn 角度制限の扇形（アーク）を描画
    /// </summary>
    private void DrawBodyTurnArc(
        ImDrawListPtr drawList,
        Vector3 center,
        float actorRotation,
        float angleLimitDeg,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize)
    {
        var arcBorderCol = ImGui.GetColorU32(new Vector4(1.0f, 0.92f, 0.25f, 0.95f)); // 黄色
        var arcFillCol = ImGui.GetColorU32(new Vector4(1.0f, 0.92f, 0.25f, 0.12f));   // 半透明黄色
        float arcRadius = 2.8f; // 見やすい標準的な半径

        if (!ProjectWorldToScreen(center, viewProj, vpPos, vpSize, out var centerScreen))
            return;

        // 角度が 0 度（首・視線のみ、体は回転しない）
        if (angleLimitDeg <= 0.5f)
        {
            var forwardWorld = new Vector3(
                center.X + MathF.Sin(actorRotation) * arcRadius,
                center.Y,
                center.Z + MathF.Cos(actorRotation) * arcRadius
            );
            if (ProjectWorldToScreen(forwardWorld, viewProj, vpPos, vpSize, out var forwardScreen))
            {
                drawList.AddLine(centerScreen, forwardScreen, arcBorderCol, 2.5f);
                drawList.AddCircleFilled(forwardScreen, 4.0f, arcBorderCol);
                DrawLabel(drawList, forwardScreen, "Body Turn: 0° (Head only)", arcBorderCol);
            }
            return;
        }

        // 角度が 180 度以上（全方位体回転）
        if (angleLimitDeg >= 179.5f)
        {
            DrawHorizontalCircle(drawList, center, arcRadius, arcBorderCol, 2.0f, viewProj, vpPos, vpSize, 36);
            DrawRangeLabel(drawList, center, arcRadius, "Body Turn: ±180° (All directions)", arcBorderCol, viewProj, vpPos, vpSize, MathF.PI);
            return;
        }

        // 0 < angleLimitDeg < 180 (扇形アーク)
        float limitRad = angleLimitDeg * (MathF.PI / 180.0f);
        float startAngle = actorRotation - limitRad;
        float endAngle = actorRotation + limitRad;
        int segments = Math.Max(8, (int)(angleLimitDeg / 5.0f));
        float step = (endAngle - startAngle) / segments;

        Vector2 leftEdgeScreen = Vector2.Zero;
        Vector2 rightEdgeScreen = Vector2.Zero;
        bool hasLeftEdge = false;
        bool hasRightEdge = false;

        Vector2 prevPt = Vector2.Zero;
        bool hasPrev = false;

        for (int i = 0; i <= segments; i++)
        {
            float curAngle = startAngle + i * step;
            var ptWorld = new Vector3(
                center.X + MathF.Sin(curAngle) * arcRadius,
                center.Y,
                center.Z + MathF.Cos(curAngle) * arcRadius
            );

            if (ProjectWorldToScreen(ptWorld, viewProj, vpPos, vpSize, out var curPtScreen))
            {
                if (i == 0)
                {
                    leftEdgeScreen = curPtScreen;
                    hasLeftEdge = true;
                }
                if (i == segments)
                {
                    rightEdgeScreen = curPtScreen;
                    hasRightEdge = true;
                }

                if (hasPrev)
                {
                    drawList.AddLine(prevPt, curPtScreen, arcBorderCol, 2.0f);
                    drawList.AddTriangleFilled(centerScreen, prevPt, curPtScreen, arcFillCol);
                }

                prevPt = curPtScreen;
                hasPrev = true;
            }
            else
            {
                hasPrev = false;
            }
        }

        if (hasLeftEdge)
        {
            drawList.AddLine(centerScreen, leftEdgeScreen, arcBorderCol, 2.0f);
        }
        if (hasRightEdge)
        {
            drawList.AddLine(centerScreen, rightEdgeScreen, arcBorderCol, 2.0f);
        }

        // 正面ライン（中央ガイド線）
        var fwdWorld = new Vector3(
            center.X + MathF.Sin(actorRotation) * arcRadius,
            center.Y,
            center.Z + MathF.Cos(actorRotation) * arcRadius
        );
        if (ProjectWorldToScreen(fwdWorld, viewProj, vpPos, vpSize, out var fwdScreen))
        {
            var fwdLineCol = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.6f));
            drawList.AddLine(centerScreen, fwdScreen, fwdLineCol, 1.2f);
            DrawLabel(drawList, fwdScreen, $"Body Turn: ±{angleLimitDeg:F0}°", arcBorderCol);
        }
    }

    /// <summary>
    /// リング外周上の指定角度位置に距離ラベルを描画
    /// </summary>
    private void DrawRangeLabel(
        ImDrawListPtr drawList,
        Vector3 center,
        float radius,
        string text,
        uint color,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize,
        float angleOffset = 0f)
    {
        var pos = new Vector3(
            center.X + MathF.Sin(angleOffset) * radius,
            center.Y,
            center.Z + MathF.Cos(angleOffset) * radius
        );

        if (ProjectWorldToScreen(pos, viewProj, vpPos, vpSize, out var screenPos))
        {
            DrawLabel(drawList, screenPos, text, color);
        }
    }

    private void DrawLabel(ImDrawListPtr drawList, Vector2 pos, string text, uint color)
    {
        var textSize = ImGui.CalcTextSize(text);
        var bgMin = new Vector2(pos.X - textSize.X * 0.5f - 4, pos.Y - textSize.Y * 0.5f - 2);
        var bgMax = new Vector2(pos.X + textSize.X * 0.5f + 4, pos.Y + textSize.Y * 0.5f + 2);
        var bgCol = ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.14f, 0.85f));

        drawList.AddRectFilled(bgMin, bgMax, bgCol, 3.0f);
        drawList.AddText(new Vector2(pos.X - textSize.X * 0.5f, pos.Y - textSize.Y * 0.5f), color, text);
    }

    /// <summary>
    /// 3D ワールド座標をスクリーン座標へ高精度に投影。
    /// ゲームカメラの最新 ViewProjection 行列から直接計算し、TAAジッターやウィンドウ位置ずれに影響されない完全同期を実現。
    /// </summary>
    private bool ProjectWorldToScreen(
        Vector3 worldPos,
        Matrix4x4 viewProj,
        Vector2 vpPos,
        Vector2 vpSize,
        out Vector2 screenPos)
    {
        var clip = Vector4.Transform(new Vector4(worldPos, 1.0f), viewProj);
        if (clip.W <= 0.01f) // カメラ背面
        {
            screenPos = Vector2.Zero;
            return false;
        }

        float invW = 1.0f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;

        screenPos = new Vector2(
            vpPos.X + (ndcX + 1.0f) * 0.5f * vpSize.X,
            vpPos.Y + (1.0f - ndcY) * 0.5f * vpSize.Y
        );
        return true;
    }
}
