using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CharacterSpawn.Managers;
using CharacterSpawn.Models;
using CharacterSpawn.Services;
using CharacterSpawn.UI;

namespace CharacterSpawn;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Character Spawn";
    private const string CommandName = "/charaspawn";
    private const string ShortCommandName = "/cspawn";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem { get; init; } = new("CharacterSpawnPlugin");

    private readonly GameDataService gameDataService;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser mcdfParser;

    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly NamePlateController namePlateController;
    private readonly ActorManager actorManager;

    private readonly GizmoRenderer gizmoRenderer;
    private readonly CharacterLibraryTab libraryTab;
    private readonly StageSceneTab stageTab;
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Services
        gameDataService = new GameDataService(DataManager);
        glamourerIpc = new GlamourerIpc(PluginInterface, Log);
        penumbraIpc = new PenumbraIpc(PluginInterface, Log);
        mcdfParser = new McdfParser(Log);

        // Managers
        timelineManager = new TimelineManager(Log);
        headTrackingManager = new HeadTrackingManager(ClientState, Log);
        actorManager = new ActorManager(ClientState, GameInteropProvider, Log, timelineManager, headTrackingManager, glamourerIpc, penumbraIpc);
        namePlateController = new NamePlateController(NamePlateGui, Log, () => actorManager.ActiveActors);

        // UI
        gizmoRenderer = new GizmoRenderer(GameGui, Configuration);
        libraryTab = new CharacterLibraryTab(Configuration, gameDataService, glamourerIpc, penumbraIpc, mcdfParser, ClientState, TargetManager, Log);
        stageTab = new StageSceneTab(Configuration, actorManager, gameDataService, ClientState, Log);
        mainWindow = new MainWindow(Configuration, libraryTab, stageTab, gizmoRenderer, actorManager, Log);

        WindowSystem.AddWindow(mainWindow);

        // Commands
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Character Spawn main window"
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Character Spawn main window"
        });

        // Event hooks
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleUI;

        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    private void OnCommand(string command, string args)
    {
        ToggleUI();
    }

    private void ToggleUI()
    {
        mainWindow.IsOpen = !mainWindow.IsOpen;
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        actorManager.UpdateFrame();
    }

    private void OnTerritoryChanged(ushort territoryType)
    {
        // ゾーン移動時は安全に現在のアクターを破棄
        actorManager.DespawnAll();

        if (!Configuration.AutoRestoreScenesOnZoneChange)
            return;

        // 新ゾーンにAuto-Spawn設定されたシーンがあれば自動呼び出し
        var autoScenes = Configuration.Scenes.Where(s => s.TerritoryTypeId == territoryType && s.AutoSpawnOnZone).ToList();
        foreach (var scene in autoScenes)
        {
            Log.Information($"Auto-spawning scene '{scene.Name}' for territory {territoryType}");
            foreach (var actorData in scene.Actors)
            {
                var template = Configuration.Templates.FirstOrDefault(t => t.Id == actorData.TemplateId);
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

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ClientState.TerritoryChanged -= OnTerritoryChanged;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleUI;

        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        namePlateController.Dispose();
        actorManager.Dispose();
    }
}
