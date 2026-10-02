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
    }

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted("Plugin Settings");
        ImGui.Separator();

        ImGui.TextUnformatted("Gizmo Mode:");
        int modeInt = (int)configuration.CurrentGizmoMode;
        if (ImGui.RadioButton("Select (Hide Gizmo)", ref modeInt, (int)GizmoMode.Select) ||
            ImGui.RadioButton("Translate (Move Axis + Quad Planes)", ref modeInt, (int)GizmoMode.Translate) ||
            ImGui.RadioButton("Rotate (Rings)", ref modeInt, (int)GizmoMode.Rotate))
        {
            configuration.CurrentGizmoMode = (GizmoMode)modeInt;
            configuration.Save();
        }

        bool autoRestore = configuration.AutoRestoreScenesOnZoneChange;
        if (ImGui.Checkbox("Auto-Restore Marked Scenes on Zone Change", ref autoRestore))
        {
            configuration.AutoRestoreScenesOnZoneChange = autoRestore;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Character Recovery");
        if (ImGui.Button("Revert Local Player (Glamourer)", new Vector2(250, 0)))
        {
            actorManager.RevertLocalPlayer();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Restores the local player character to their original appearance by reverting Glamourer state.");
        }
    }

    public void Dispose()
    {
    }
}
