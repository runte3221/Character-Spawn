using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using CharacterSpawn.Models;
using CharacterSpawn.Managers;
using CharacterSpawn.Services;

namespace CharacterSpawn.UI;

public class StageSceneTab
{
    private readonly Configuration configuration;
    private readonly ActorManager actorManager;
    private readonly GameDataService gameDataService;
    private readonly IClientState clientState;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly GizmoRenderer? gizmoRenderer;

    private SpawnedActorData? selectedActor;
    private string animSearchQuery = string.Empty;
    private string facialSearchQuery = string.Empty;
    private string newSceneName = "New Scene";

    public SpawnedActorData? SelectedActor => selectedActor;

    public StageSceneTab(
        Configuration configuration,
        ActorManager actorManager,
        GameDataService gameDataService,
        IClientState clientState,
        IObjectTable objectTable,
        IPluginLog log,
        GizmoRenderer? gizmoRenderer = null)
    {
        this.configuration = configuration;
        this.actorManager = actorManager;
        this.gameDataService = gameDataService;
        this.clientState = clientState;
        this.objectTable = objectTable;
        this.log = log;
        this.gizmoRenderer = gizmoRenderer;
    }

    public void SelectActor(SpawnedActorData? actor)
    {
        selectedActor = actor;
    }

    public void Draw()
    {
        ImGui.Columns(2, "StageColumns", true);

        // Left Column: Active Spawned Actors & Scene Presets
        DrawLeftPanel();

        ImGui.NextColumn();

        // Right Column: Transform, Animation, Expression, and NamePlate Controls
        DrawRightPanel();

        ImGui.Columns(1);
    }

    private void DrawLeftPanel()
    {
        ImGui.TextUnformatted("Active Characters on Stage");
        ImGui.SameLine();
        if (ImGui.SmallButton("Despawn All"))
        {
            actorManager.DespawnAll();
            selectedActor = null;
        }

        ImGui.Separator();

        var activeActors = actorManager.ActiveActors;
        if (activeActors.Count == 0)
        {
            ImGui.TextDisabled("No active characters spawned.");
            ImGui.TextWrapped("Spawn a character from the 'Character Library' tab.");
        }
        else
        {
            if (ImGui.BeginListBox("##ActiveActorsList", new Vector2(-1, 140)))
            {
                for (int i = 0; i < activeActors.Count; i++)
                {
                    var actor = activeActors[i];
                    bool isSelected = selectedActor == actor;

                    if (ImGui.Selectable($"{actor.DisplayName}##Actor_{actor.InstanceId}", isSelected))
                    {
                        selectedActor = actor;
                    }
                }
                ImGui.EndListBox();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Scene Presets (Stagehand-like)");
        ImGui.Separator();

        ImGui.InputText("Scene Name", ref newSceneName, 64);

        if (ImGui.Button("Save Current Stage as Scene", new Vector2(-1, 26)))
        {
            SaveCurrentStageAsScene();
        }

        ImGui.Spacing();

        // Scene preset list
        for (int i = 0; i < configuration.Scenes.Count; i++)
        {
            var scene = configuration.Scenes[i];
            ImGui.PushID($"Scene_{scene.Id}");

            ImGui.TextUnformatted($"[{scene.TerritoryName}] {scene.Name} ({scene.Actors.Count} actors)");

            bool autoSpawn = scene.AutoSpawnOnZone;
            if (ImGui.Checkbox("Auto-Spawn on Zone", ref autoSpawn))
            {
                scene.AutoSpawnOnZone = autoSpawn;
                configuration.Save();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Show"))
            {
                LoadScene(scene);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Hide"))
            {
                actorManager.DespawnAll();
                selectedActor = null;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
            {
                configuration.Scenes.RemoveAt(i);
                configuration.Save();
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    private void DrawRightPanel()
    {
        if (selectedActor == null || !selectedActor.IsSpawned)
        {
            ImGui.TextDisabled("No active character selected.");
            ImGui.TextWrapped("Select a character from the active list to edit transform, animations, expressions, and settings.");
            return;
        }

        ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), $"Editing: {selectedActor.DisplayName}");
        ImGui.Separator();

        // 1. Transform Section
        DrawTransformSection();

        ImGui.Spacing();
        ImGui.Separator();

        // 2. Animation & Expressions Section
        DrawAnimationSection();

        ImGui.Spacing();
        ImGui.Separator();

        // 3. NamePlate & Targetability Section
        DrawNamePlateSection();
    }

    private void DrawTransformSection()
    {
        if (selectedActor == null) return;

        ImGui.TextUnformatted("Transform (Position & Rotation)");

        if (gizmoRenderer != null)
        {
            gizmoRenderer.DrawToolbar();
            ImGui.Spacing();
        }

        var pos = selectedActor.Transform.Position;
        var rot = selectedActor.Transform.Rotation;

        bool changed = false;

        float posX = pos.X;
        float posY = pos.Y;
        float posZ = pos.Z;
        float rotDeg = rot * (180.0f / (float)Math.PI);

        if (ImGui.DragFloat("X", ref posX, 0.05f)) changed = true;
        ImGui.SameLine();
        DrawStepButtons(ref posX, ref changed);

        if (ImGui.DragFloat("Y", ref posY, 0.05f)) changed = true;
        ImGui.SameLine();
        DrawStepButtons(ref posY, ref changed);

        if (ImGui.DragFloat("Z", ref posZ, 0.05f)) changed = true;
        ImGui.SameLine();
        DrawStepButtons(ref posZ, ref changed);

        if (ImGui.DragFloat("Yaw Rotation", ref rotDeg, 1.0f, -180.0f, 180.0f))
        {
            rot = rotDeg * ((float)Math.PI / 180.0f);
            changed = true;
        }

        if (changed)
        {
            actorManager.UpdateActorTransform(selectedActor, new Vector3(posX, posY, posZ), rot);
        }

        ImGui.Spacing();

        if (ImGui.Button("Snap to Local Player"))
        {
            var p = objectTable.Length > 0 ? objectTable[0] : null;
            if (p != null)
            {
                actorManager.UpdateActorTransform(selectedActor, p.Position, p.Rotation);
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Place 1.5m in Front"))
        {
            var p = objectTable.Length > 0 ? objectTable[0] : null;
            if (p != null)
            {
                var forward = new Vector3((float)Math.Sin(p.Rotation), 0, (float)Math.Cos(p.Rotation));
                var targetPos = p.Position + (forward * 1.5f);
                actorManager.UpdateActorTransform(selectedActor, targetPos, p.Rotation + (float)Math.PI);
            }
        }
    }

    private void DrawStepButtons(ref float val, ref bool changed)
    {
        if (ImGui.SmallButton("-0.1")) { val -= 0.1f; changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("+0.1")) { val += 0.1f; changed = true; }
    }

    private void DrawAnimationSection()
    {
        if (selectedActor == null) return;

        ImGui.TextUnformatted("Animation & Motion (ActionTimeline)");

        var anim = selectedActor.Animation;

        ImGui.InputText("Search Motions", ref animSearchQuery, 64);
        var searchResults = gameDataService.SearchTimelines(animSearchQuery, 8);

        if (ImGui.BeginListBox("##AnimTimelineList", new Vector2(-1, 90)))
        {
            foreach (var entry in searchResults)
            {
                bool isSelected = anim.TimelineId == entry.Id;
                if (ImGui.Selectable($"[{entry.Id}] {entry.Key} - {entry.Description}", isSelected))
                {
                    anim.TimelineId = entry.Id;
                    anim.TimelineName = entry.Key;
                    actorManager.ApplyActorAnimation(selectedActor);
                }
            }
            ImGui.EndListBox();
        }

        bool isLoop = anim.IsLoop;
        if (ImGui.Checkbox("Seamless Loop Animation", ref isLoop))
        {
            anim.IsLoop = isLoop;
            actorManager.ApplyActorAnimation(selectedActor);
        }

        bool lookAt = anim.LookAtPlayer;
        if (ImGui.Checkbox("Track Player Head / Eyes (LookAt)", ref lookAt))
        {
            anim.LookAtPlayer = lookAt;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Facial Expression:");

        var facials = gameDataService.GetFacialExpressions();
        if (ImGui.BeginListBox("##FacialList", new Vector2(-1, 80)))
        {
            foreach (var f in facials.Take(20))
            {
                bool isSelected = anim.FacialExpressionId == f.Id;
                if (ImGui.Selectable($"[{f.Id}] {f.Key}", isSelected))
                {
                    anim.FacialExpressionId = f.Id;
                    anim.FacialExpressionName = f.Key;
                    actorManager.ApplyActorAnimation(selectedActor);
                }
            }
            ImGui.EndListBox();
        }
    }

    private void DrawNamePlateSection()
    {
        if (selectedActor == null) return;

        ImGui.TextUnformatted("NamePlate & Targetability");

        var np = selectedActor.NamePlate;
        bool showPlate = np.Show;
        if (ImGui.Checkbox("Show NamePlate", ref showPlate))
        {
            np.Show = showPlate;
        }

        string customName = np.CustomName;
        if (ImGui.InputText("Display Name", ref customName, 64))
        {
            np.CustomName = customName;
        }

        bool targetable = selectedActor.IsTargetable;
        if (ImGui.Checkbox("Targetable (Click to Select)", ref targetable))
        {
            selectedActor.IsTargetable = targetable;
            actorManager.ApplyTargetable(selectedActor);
        }

        ImGui.Spacing();
        if (ImGui.Button("Delete this Character", new Vector2(-1, 24)))
        {
            actorManager.DespawnCharacter(selectedActor);
            selectedActor = null;
        }
    }

    private void SaveCurrentStageAsScene()
    {
        var active = actorManager.ActiveActors;
        if (active.Count == 0) return;

        var scene = new ScenePreset
        {
            Name = string.IsNullOrWhiteSpace(newSceneName) ? "Scene" : newSceneName,
            TerritoryTypeId = clientState.TerritoryType,
            TerritoryName = $"Zone {clientState.TerritoryType}",
            AutoSpawnOnZone = false,
            Actors = active.Select(a => new SpawnedActorData
            {
                TemplateId = a.TemplateId,
                DisplayName = a.DisplayName,
                Transform = new TransformData
                {
                    Position = a.Transform.Position,
                    Rotation = a.Transform.Rotation,
                    Scale = a.Transform.Scale
                },
                Animation = new AnimationSettings
                {
                    TimelineId = a.Animation.TimelineId,
                    TimelineName = a.Animation.TimelineName,
                    IsLoop = a.Animation.IsLoop,
                    FacialExpressionId = a.Animation.FacialExpressionId,
                    FacialExpressionName = a.Animation.FacialExpressionName,
                    LookAtPlayer = a.Animation.LookAtPlayer
                },
                NamePlate = new NamePlateSettings
                {
                    Show = a.NamePlate.Show,
                    CustomName = a.NamePlate.CustomName
                },
                IsTargetable = a.IsTargetable
            }).ToList()
        };

        configuration.Scenes.Add(scene);
        configuration.Save();
        log.Information($"Saved scene preset '{scene.Name}' with {scene.Actors.Count} actors.");
    }

    private void LoadScene(ScenePreset scene)
    {
        actorManager.DespawnAll();

        foreach (var actorData in scene.Actors)
        {
            var template = configuration.Templates.FirstOrDefault(t => t.Id == actorData.TemplateId);
            if (template != null)
            {
                var spawned = actorManager.SpawnCharacter(template, actorData.Transform.Position, actorData.Transform.Rotation);
                if (spawned != null)
                {
                    spawned.Animation = actorData.Animation;
                    spawned.NamePlate = actorData.NamePlate;
                    spawned.IsTargetable = actorData.IsTargetable;

                    actorManager.ApplyActorAnimation(spawned);
                    actorManager.ApplyTargetable(spawned);
                }
            }
        }
    }
}
