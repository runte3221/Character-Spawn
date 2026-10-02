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

        float axisLength = 75.0f * configuration.GizmoScale;
        float handleRadius = 9.0f;
        float hitThreshold = 14.0f; // 当たり判定の広さ

        // X, Y, Zの終点スクリーン座標を計算
        var posX = origin + new Vector3(1.0f, 0, 0);
        var posY = origin + new Vector3(0, 1.0f, 0);
        var posZ = origin + new Vector3(0, 0, 1.0f);

        gameGui.WorldToScreen(posX, out var screenX);
        gameGui.WorldToScreen(posY, out var screenY);
        gameGui.WorldToScreen(posZ, out var screenZ);

        // 軸方向ベクトル
        var deltaX = screenX - screenOrigin;
        var deltaY = screenY - screenOrigin;
        var deltaZ = screenZ - screenOrigin;

        var dirX = deltaX.LengthSquared() > 0.001f ? Vector2.Normalize(deltaX) : new Vector2(1, 0);
        var dirY = deltaY.LengthSquared() > 0.001f ? Vector2.Normalize(deltaY) : new Vector2(0, -1);
        var dirZ = deltaZ.LengthSquared() > 0.001f ? Vector2.Normalize(deltaZ) : new Vector2(-0.7f, 0.7f);

        var endX = screenOrigin + (dirX * axisLength);
        var endY = screenOrigin + (dirY * axisLength);
        var endZ = screenOrigin + (dirZ * axisLength);

        // マウスと先端ハンドルの当たり判定
        bool hoverHandleX = Vector2.Distance(mousePos, endX) <= hitThreshold;
        bool hoverHandleY = Vector2.Distance(mousePos, endY) <= hitThreshold;
        bool hoverHandleZ = Vector2.Distance(mousePos, endZ) <= hitThreshold;

        // マウスと軸線分全体の当たり判定（線分に沿った掴み判定）
        bool hoverLineX = DistanceToLineSegment(mousePos, screenOrigin, endX) <= 8.0f;
        bool hoverLineY = DistanceToLineSegment(mousePos, screenOrigin, endY) <= 8.0f;
        bool hoverLineZ = DistanceToLineSegment(mousePos, screenOrigin, endZ) <= 8.0f;

        bool hoverX = hoverHandleX || hoverLineX;
        bool hoverY = hoverHandleY || hoverLineY;
        bool hoverZ = hoverHandleZ || hoverLineZ;

        // Yaw回転リングの半径
        float rotRadius = axisLength * 0.75f;
        float distToOrigin = Vector2.Distance(mousePos, screenOrigin);
        bool hoverRot = Math.Abs(distToOrigin - rotRadius) <= 9.0f;

        // マウスクリック検出
        bool isMouseClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool isMouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // ドラッグ開始
        if (isMouseClicked && activeAxis == GizmoAxis.None)
        {
            if (hoverHandleY || hoverLineY) activeAxis = GizmoAxis.Y;
            else if (hoverHandleX || hoverLineX) activeAxis = GizmoAxis.X;
            else if (hoverHandleZ || hoverLineZ) activeAxis = GizmoAxis.Z;
            else if (hoverRot) activeAxis = GizmoAxis.RotationYaw;

            if (activeAxis != GizmoAxis.None)
            {
                dragStartMousePos = mousePos;
                initialActorPosition = selectedActor.Transform.Position;
                initialActorRotation = selectedActor.Transform.Rotation;
            }
        }

        // ドラッグ中処理
        if (activeAxis != GizmoAxis.None)
        {
            if (isMouseDown)
            {
                var totalDelta = mousePos - dragStartMousePos;

                if (activeAxis == GizmoAxis.X)
                {
                    float proj = Vector2.Dot(totalDelta, dirX) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(proj, 0, 0);
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.Y)
                {
                    // スクリーン上向きが -Y 方向のため符号を調整
                    float proj = Vector2.Dot(totalDelta, dirY) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(0, -proj, 0);
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.Z)
                {
                    float proj = Vector2.Dot(totalDelta, dirZ) * 0.02f;
                    var newPos = initialActorPosition + new Vector3(0, 0, proj);
                    onTransformChanged(newPos, selectedActor.Transform.Rotation);
                }
                else if (activeAxis == GizmoAxis.RotationYaw)
                {
                    float rotDelta = totalDelta.X * 0.025f;
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
        uint colX = (hoverX || activeAxis == GizmoAxis.X) ? 0xFF3333FF : 0xDD2222DD; // 赤 (X)
        uint colY = (hoverY || activeAxis == GizmoAxis.Y) ? 0xFF33FF33 : 0xDD22DD22; // 緑 (Y)
        uint colZ = (hoverZ || activeAxis == GizmoAxis.Z) ? 0xFFFF6633 : 0xDDDD4422; // 青 (Z)
        uint colRot = (hoverRot || activeAxis == GizmoAxis.RotationYaw) ? 0xFF44FFFF : 0xAA22CCCC; // 黄 (Yaw)

        float lineThick = 3.5f;

        // X軸（赤）
        drawList.AddLine(screenOrigin, endX, colX, (hoverX || activeAxis == GizmoAxis.X) ? 5.0f : lineThick);
        drawList.AddCircleFilled(endX, handleRadius, colX);

        // Y軸（緑）
        drawList.AddLine(screenOrigin, endY, colY, (hoverY || activeAxis == GizmoAxis.Y) ? 5.0f : lineThick);
        drawList.AddCircleFilled(endY, handleRadius, colY);

        // Z軸（青）
        drawList.AddLine(screenOrigin, endZ, colZ, (hoverZ || activeAxis == GizmoAxis.Z) ? 5.0f : lineThick);
        drawList.AddCircleFilled(endZ, handleRadius, colZ);

        // Yaw回転リング
        drawList.AddCircle(screenOrigin, rotRadius, colRot, 48, (hoverRot || activeAxis == GizmoAxis.RotationYaw) ? 4.0f : 2.5f);

        // 中心原点
        drawList.AddCircleFilled(screenOrigin, 5.0f, 0xFFFFFFFF);
    }

    private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var line = lineEnd - lineStart;
        float lineLenSq = line.LengthSquared();
        if (lineLenSq < 0.001f) return Vector2.Distance(point, lineStart);

        float t = Math.Clamp(Vector2.Dot(point - lineStart, line) / lineLenSq, 0.0f, 1.0f);
        var proj = lineStart + (line * t);
        return Vector2.Distance(point, proj);
    }
}
