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
    private readonly GizmoRenderer gizmoRenderer;
    private readonly ActorManager actorManager;
    private readonly IPluginLog log;

    private int activeTab = 0;

    public MainWindow(
        Configuration configuration,
        CharacterLibraryTab libraryTab,
        StageSceneTab stageTab,
        GizmoRenderer gizmoRenderer,
        ActorManager actorManager,
        IPluginLog log)
        : base("Character Spawn###CharacterSpawnMainWindow", ImGuiWindowFlags.None)
    {
        this.configuration = configuration;
        this.libraryTab = libraryTab;
        this.stageTab = stageTab;
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
            if (ImGui.BeginTabItem("Character Library"))
            {
                activeTab = 0;
                libraryTab.Draw(OnSpawnRequested);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Stage & Scene"))
            {
                activeTab = 1;
                stageTab.Draw();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                activeTab = 2;
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        // Render 3D Gizmo for selected actor
        var selectedActor = stageTab.SelectedActor;
        if (selectedActor != null && selectedActor.IsSpawned)
        {
            gizmoRenderer.Render(selectedActor, (newPos, newRot) =>
            {
                actorManager.UpdateActorTransform(selectedActor, newPos, newRot);
            });
        }
    }

    private void OnSpawnRequested(CharacterTemplate template)
    {
        var spawned = actorManager.SpawnCharacter(template);
        if (spawned != null)
        {
            stageTab.SelectActor(spawned);
            // Switch view to Stage tab
            activeTab = 1;
        }
    }

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted("Plugin Settings");
        ImGui.Separator();

        bool showGizmo = configuration.ShowGizmo;
        if (ImGui.Checkbox("Enable 3D Gizmo on Stage", ref showGizmo))
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
