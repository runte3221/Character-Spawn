using Dalamud.Configuration;
using Dalamud.Plugin;
using CharacterSpawn.Models;

namespace CharacterSpawn;

public enum GizmoMode
{
    Select = 0,
    Translate = 1,
    Rotate = 2,
    Scale = 3
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Library templates
    public List<CharacterTemplate> Templates { get; set; } = new();

    // Folders for template organization
    public List<string> Folders { get; set; } = new();

    // Stored scene presets
    public List<ScenePreset> Scenes { get; set; } = new();

    // Settings
    public bool ShowGizmo
    {
        get => CurrentGizmoMode != GizmoMode.Select;
        set
        {
            if (!value) CurrentGizmoMode = GizmoMode.Select;
            else if (CurrentGizmoMode == GizmoMode.Select) CurrentGizmoMode = GizmoMode.Translate;
        }
    }
    public GizmoMode CurrentGizmoMode { get; set; } = GizmoMode.Translate;
    public float GizmoScale { get; set; } = 1.0f;
    public bool AutoRestoreScenesOnZoneChange { get; set; } = true;
    public float GizmoSnapDistance { get; set; } = 0.1f;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi)
    {
        pluginInterface = pi;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
