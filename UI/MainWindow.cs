using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;
using CharacterSpawn.Managers;

namespace CharacterSpawn.UI;

public class MainWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly CharacterLibraryTab libraryTab;
    private readonly StageSceneTab stageTab;
    private readonly LogTab logTab;
    private readonly GizmoRenderer gizmoRenderer;
    private readonly ActorManager actorManager;
    private readonly IPluginLog log;

    public MainWindow(
        Configuration configuration,
        CharacterLibraryTab libraryTab,
        StageSceneTab stageTab,
        LogTab logTab,
        GizmoRenderer gizmoRenderer,
        ActorManager actorManager,
        IPluginLog log)
        : base("Character Spawn###CharacterSpawnMainWindow", ImGuiWindowFlags.None)
    {
        this.configuration = configuration;
        this.libraryTab = libraryTab;
        this.stageTab = stageTab;
        this.logTab = logTab;
        this.gizmoRenderer = gizmoRenderer;
        this.actorManager = actorManager;
        this.log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(650, 480),
            MaximumSize = new Vector2(1600, 1200)
        };
        Size = new Vector2(750, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("CharacterSpawnTabs"))
        {
            if (ImGui.BeginTabItem("Character"))
            {
                libraryTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Scene"))
            {
                stageTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Log"))
            {
                logTab.Draw();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Render 3D Gizmo
        // 1. StageSceneTab で選択中のアクター
        // 2. または CharacterTab でプレビュー中のアクター
        var targetActor = stageTab.SelectedActor;
        if (targetActor == null || !targetActor.IsSpawned)
        {
            targetActor = actorManager.CurrentPreviewActor;
        }

        if (targetActor != null && targetActor.IsSpawned && configuration.ShowGizmo)
        {
            gizmoRenderer.Render(targetActor, (newPos, newRot) =>
            {
                actorManager.UpdateActorTransform(targetActor, newPos, newRot);
            });
        }
    }

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted("Plugin Settings");
        ImGui.Separator();

        bool showGizmo = configuration.ShowGizmo;
        if (ImGui.Checkbox("Enable 3D Gizmo on Stage & Preview", ref showGizmo))
        {
            configuration.ShowGizmo = showGizmo;
            configuration.Save();
        }

        float gizmoScale = configuration.GizmoScale;
        if (ImGui.SliderFloat("Gizmo Size Scale", ref gizmoScale, 0.5f, 2.5f))
        {
            configuration.GizmoScale = gizmoScale;
            configuration.Save();
        }

        bool autoRestore = configuration.AutoRestoreScenesOnZoneChange;
        if (ImGui.Checkbox("Auto-Restore Marked Scenes on Zone Change", ref autoRestore))
        {
            configuration.AutoRestoreScenesOnZoneChange = autoRestore;
            configuration.Save();
        }
    }

    public void Dispose()
    {
    }
}
