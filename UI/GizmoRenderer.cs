using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;

namespace CharacterSpawn.UI;

public class GizmoRenderer
{
    private readonly IGameGui gameGui;
    private readonly Configuration configuration;

    public enum GizmoAxis
    {
        None,
        X,
        Y,
        Z,
        RotationYaw
    }

    private GizmoAxis activeAxis = GizmoAxis.None;
    private Vector2 dragStartMousePos;
    private Vector3 initialActorPosition;
    private float initialActorRotation;

    public GizmoRenderer(IGameGui gameGui, Configuration configuration)
    {
        this.gameGui = gameGui;
        this.configuration = configuration;
    }

    public void Render(SpawnedActorData? selectedActor, Action<Vector3, float> onTransformChanged)
    {
        if (selectedActor == null || !selectedActor.IsSpawned || !configuration.ShowGizmo)
            return;

        var origin = selectedActor.Transform.Position;
        if (!gameGui.WorldToScreen(origin, out var screenOrigin))
            return;

        var drawList = ImGui.GetForegroundDrawList();
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;

        float axisLength = 70.0f * configuration.GizmoScale;
        float handleRadius = 8.0f;

        // X, Y, Zの終点スクリーン座標を計算
        var posX = origin + new Vector3(1.0f, 0, 0);
        var posY = origin + new Vector3(0, 1.0f, 0);
        var posZ = origin + new Vector3(0, 0, 1.0f);

        gameGui.WorldToScreen(posX, out var screenX);
        gameGui.WorldToScreen(posY, out var screenY);
        gameGui.WorldToScreen(posZ, out var screenZ);

        // 軸方向ベクトル
        var dirX = Vector2.Normalize(screenX - screenOrigin);
        var dirY = Vector2.Normalize(screenY - screenOrigin);
        var dirZ = Vector2.Normalize(screenZ - screenOrigin);

        var endX = screenOrigin + (dirX * axisLength);
        var endY = screenOrigin + (dirY * axisLength);
        var endZ = screenOrigin + (dirZ * axisLength);

        // マウスとハンドルの当たり判定
        bool hoverX = Vector2.Distance(mousePos, endX) <= handleRadius + 2.0f;
        bool hoverY = Vector2.Distance(mousePos, endY) <= handleRadius + 2.0f;
        bool hoverZ = Vector2.Distance(mousePos, endZ) <= handleRadius + 2.0f;

        // Yaw回転リングの半径
        float rotRadius = axisLength * 0.7f;
        bool hoverRot = Math.Abs(Vector2.Distance(mousePos, screenOrigin) - rotRadius) <= 5.0f;

        // ドラッグ開始
        if (io.MouseClicked[0])
        {
            if (hoverX) activeAxis = GizmoAxis.X;
            else if (hoverY) activeAxis = GizmoAxis.Y;
            else if (hoverZ) activeAxis = GizmoAxis.Z;
            else if (hoverRot) activeAxis = GizmoAxis.RotationYaw;

            if (activeAxis != GizmoAxis.None)
            {
                dragStartMousePos = mousePos;
                initialActorPosition = selectedActor.Transform.Position;
                initialActorRotation = selectedActor.Transform.Rotation;
            }
        }

        // ドラッグ中
        if (activeAxis != GizmoAxis.None)
        {
            if (io.MouseDown[0])
            {
                var delta = mousePos - dragStartMousePos;

                if (activeAxis == GizmoAxis.X)
                {
                    float proj = Vector2.Dot(delta, dirX) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(proj, 0, 0);
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.Y)
                {
                    float proj = Vector2.Dot(delta, dirY) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(0, -proj, 0); // スクリーンY反転
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.Z)
                {
                    float proj = Vector2.Dot(delta, dirZ) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(0, 0, proj);
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.RotationYaw)
                {
                    float rotDelta = delta.X * 0.02f;
                    var newRot = initialActorRotation + rotDelta;
                    onTransformChanged(selectedActor.Transform.Position, newRot);
                }
            }
            else
            {
                activeAxis = GizmoAxis.None;
            }
        }

        // 描画色
        uint colX = (hoverX || activeAxis == GizmoAxis.X) ? 0xFF5555FF : 0xFF2222EE; // 赤
        uint colY = (hoverY || activeAxis == GizmoAxis.Y) ? 0xFF55FF55 : 0xFF22EE22; // 緑
        uint colZ = (hoverZ || activeAxis == GizmoAxis.Z) ? 0xFFFF7755 : 0xFFEE4422; // 青
        uint colRot = (hoverRot || activeAxis == GizmoAxis.RotationYaw) ? 0xFFFFFF55 : 0xAAFFFF22; // 黄

        // X軸（赤）
        drawList.AddLine(screenOrigin, endX, colX, 3.0f);
        drawList.AddCircleFilled(endX, handleRadius, colX);

        // Y軸（緑）
        drawList.AddLine(screenOrigin, endY, colY, 3.0f);
        drawList.AddCircleFilled(endY, handleRadius, colY);

        // Z軸（青）
        drawList.AddLine(screenOrigin, endZ, colZ, 3.0f);
        drawList.AddCircleFilled(endZ, handleRadius, colZ);

        // Yaw回転リング
        drawList.AddCircle(screenOrigin, rotRadius, colRot, 32, 2.0f);

        // 中心点
        drawList.AddCircleFilled(screenOrigin, 4.0f, 0xFFFFFFFF);
    }
}
