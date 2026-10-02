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
    }

    private bool isHoveredOrUsing = false;

    public void Render(SpawnedActorData? selectedActor, Action<Vector3, float> onTransformChanged)
    {
        if (selectedActor == null || !selectedActor.IsSpawned)
            return;

        // Select モードの場合はギズモ非表示
        if (configuration.CurrentGizmoMode == GizmoMode.Select)
            return;

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
        // ギズモにカーソルが乗っている/操作中の時だけ NoInputs を解除してゲーム側へのクリック透過(NPC会話暴発)を防ぎ、
        // それ以外の時は NoInputs を有効にしてゲームのカメラ視点移動を自由に許可する
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

            // ターゲットアクターの Transform 行列を構築 (Quaternion を用いて 180 度境界でのフリップ・ジンバルロックを完全に防止)
            var pos = selectedActor.Transform.Position;
            var rotQuat = Quaternion.CreateFromAxisAngle(Vector3.UnitY, selectedActor.Transform.Rotation);
            var scaleVec = Vector3.One * (selectedActor.Transform.Scale > 0 ? selectedActor.Transform.Scale : 1.0f);

            var matrix = Matrix4x4.CreateScale(scaleVec) *
                         Matrix4x4.CreateFromQuaternion(rotQuat) *
                         Matrix4x4.CreateTranslation(pos);

            // 操作モード: Translate (軸矢印 + XY/XZ/YZ平面Quad) / RotateY (水平回転リング: キャラクターの向き変更に最適化)
            var op = configuration.CurrentGizmoMode == GizmoMode.Rotate
                ? ImGuizmoOperation.RotateY
                : ImGuizmoOperation.Translate;

            var mode = ImGuizmoMode.World;

            // ImGuizmo によるマニピュレート
            if (ImGuizmo.Manipulate(ref viewMatrix.M11, ref projMatrix.M11, op, mode, ref matrix.M11))
            {
                if (Matrix4x4.Decompose(matrix, out var newScale, out var newRot, out var newPos))
                {
                    // クォータニオンから前方ベクトルを算出し、Atan2 で 360 度シームレスに水平回転角（Yaw）を導出
                    var forward = Vector3.Transform(Vector3.UnitZ, newRot);
                    float newYawRad = MathF.Atan2(forward.X, forward.Z);

                    onTransformChanged(newPos, newYawRad);
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
