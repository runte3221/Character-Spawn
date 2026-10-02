using Dalamud.Configuration;
using Dalamud.Plugin;
using CharacterSpawn.Models;

namespace CharacterSpawn;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Library templates
    public List<CharacterTemplate> Templates { get; set; } = new();

    // Stored scene presets
    public List<ScenePreset> Scenes { get; set; } = new();

    // Settings
    public bool ShowGizmo { get; set; } = true;
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
