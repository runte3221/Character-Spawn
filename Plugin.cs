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
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] public static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem { get; init; } = new("CharacterSpawnPlugin");

    private readonly LogManager logManager;
    private readonly GameDataService gameDataService;
    private readonly GlamourerIpc glamourerIpc;
    private readonly PenumbraIpc penumbraIpc;
    private readonly McdfParser mcdfParser;
    private readonly CustomizePlusIpc customizePlusIpc;

    private readonly TimelineManager timelineManager;
    private readonly HeadTrackingManager headTrackingManager;
    private readonly NamePlateController namePlateController;
    private readonly ActorManager actorManager;
    private readonly AnimationService animationService;
    private readonly MovementService movementService;
    private readonly SceneManager sceneManager;

    private readonly GizmoRenderer gizmoRenderer;
    private readonly CharacterLibraryTab libraryTab;
    private readonly StageSceneTab stageTab;
    private readonly LogTab logTab;
    private readonly MainWindow mainWindow;
    private readonly SceneEditWindow sceneEditWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Logging
        logManager = new LogManager(Log);
        logManager.Info($"Character Spawn plugin initializing (v{GetType().Assembly.GetName().Version})...");

        // Services
        gameDataService = new GameDataService(DataManager, logManager);
        glamourerIpc = new GlamourerIpc(PluginInterface, Log);
        penumbraIpc = new PenumbraIpc(PluginInterface, Log);
        mcdfParser = new McdfParser(Log);
        customizePlusIpc = new CustomizePlusIpc(PluginInterface, Log);

        // Animation & Managers
        animationService = new AnimationService(Framework, ObjectTable, gameDataService, Log, logManager);
        timelineManager = new TimelineManager(Log);
        headTrackingManager = new HeadTrackingManager(ObjectTable, Log);
        actorManager = new ActorManager(ClientState, ObjectTable, SigScanner, Log, timelineManager, headTrackingManager, glamourerIpc, penumbraIpc, logManager, mcdfParser, PluginInterface, customizePlusIpc, gameDataService, Framework);
        movementService = new MovementService(Framework, ObjectTable, actorManager, animationService, Log, logManager);
        sceneManager = new SceneManager(PluginInterface, ClientState, logManager, actorManager, Configuration, animationService, movementService);
        namePlateController = new NamePlateController(NamePlateGui, Log, () => actorManager.ActiveActors);

        // UI
        gizmoRenderer = new GizmoRenderer(GameGui, Configuration);
        logTab = new LogTab(logManager);
        sceneEditWindow = new SceneEditWindow(Configuration, sceneManager, actorManager, ClientState, ObjectTable, gizmoRenderer, gameDataService);
        libraryTab = new CharacterLibraryTab(Configuration, gameDataService, glamourerIpc, penumbraIpc, mcdfParser, actorManager, ObjectTable, TargetManager, Log, logManager, customizePlusIpc, gizmoRenderer);
        stageTab = new StageSceneTab(Configuration, actorManager, sceneManager, gameDataService, ClientState, ObjectTable, Log, () =>
        {
            sceneEditWindow.IsOpen = true;
        });
        mainWindow = new MainWindow(Configuration, libraryTab, stageTab, logTab, gizmoRenderer, actorManager, Log);

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(sceneEditWindow);

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

        // 過去のセッションで自キャラがGlamourerによって誤変身させられていた場合の自動復元 (メインスレッド上で安全に実行)
        Framework.RunOnFrameworkThread(() =>
        {
            try { actorManager.RevertLocalPlayer(); } catch { }
        });

        logManager.Info("Character Spawn initialized successfully.");
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
        try
        {
            WindowSystem.Draw();

            // 3D Gizmo & Waypoints Overlay 描画 (SceneEditWindow または MainWindow プレビュー中のみアクティブ化)
            bool shouldDrawGizmo = sceneEditWindow.IsOpen || (mainWindow.IsOpen && actorManager.CurrentPreviewActor != null);
            if (shouldDrawGizmo)
            {
                var targetActor = stageTab.SelectedActor;
                if (targetActor == null || !targetActor.IsSpawned || !targetActor.IsReady)
                {
                    targetActor = actorManager.CurrentPreviewActor;
                }

                var curPlacement = sceneManager.SelectedPlacement;
                var waypoints = curPlacement?.Movement?.Waypoints;
                var loopType = curPlacement?.Movement?.LoopType ?? PatrolLoopType.Loop;
                var homePos = curPlacement?.Position;

                bool hasWaypoints = waypoints != null && waypoints.Count > 0;
                bool canDrawGizmo = targetActor != null && targetActor.IsSpawned && targetActor.IsReady && Configuration.CurrentGizmoMode != GizmoMode.Select;
                bool canDrawOverlays = Configuration.ShowVisualOverlays && (hasWaypoints || curPlacement != null);

                if (canDrawGizmo || canDrawOverlays)
                {
                    gizmoRenderer.Render(
                        canDrawGizmo ? targetActor : null,
                        (newPos, newRot, newScale) =>
                        {
                            if (targetActor != null)
                            {
                                actorManager.UpdateActorTransform(targetActor, newPos, newRot, newScale);
                                stageTab.SyncPlacementTransformFromGizmo(newPos, newRot, newScale);
                            }
                        },
                        hasWaypoints ? waypoints : null,
                        loopType,
                        homePos,
                        curPlacement);
                }
            }

            // 3D 空間モデル直接クリック判定（UI表示中かつスポーン中のアクターが存在する場合にクリックで選択切り替え）
            if (mainWindow.IsOpen || sceneEditWindow.IsOpen)
            {
                var activePlacements = sceneManager.GetActiveSpawnedPlacements();
                if (activePlacements.Count > 0)
                {
                    gizmoRenderer.CheckActorClickSelection(
                        activePlacements,
                        sceneManager.SelectedPlacement,
                        selected =>
                        {
                            sceneManager.SelectedPlacement = selected;
                        });
                }
            }
        }
        catch (Exception ex)
        {
            logManager.Error($"DrawUI exception: {ex}");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            actorManager.UpdateFrame();
            sceneManager.UpdateFrame();

            // ゲーム内ターゲット連動: ゲーム画面上でアクターをクリック/Tab選択した場合、選択Placementを自動同期
            var currentTarget = TargetManager.Target;
            if (currentTarget != null)
            {
                var matchedPlacement = sceneManager.FindPlacementByGameObject(currentTarget);
                if (matchedPlacement != null && sceneManager.SelectedPlacement != matchedPlacement)
                {
                    sceneManager.SelectedPlacement = matchedPlacement;
                }
            }
        }
        catch (Exception ex)
        {
            logManager.Error($"OnFrameworkUpdate exception: {ex}");
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleUI;

        WindowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        sceneEditWindow.Dispose();
        namePlateController.Dispose();
        movementService.Dispose();
        sceneManager.Dispose();
        actorManager.Dispose();
        animationService.Dispose();
    }
}
