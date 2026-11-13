using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

public partial class MainView : Node3D
{
    private enum ToolMode
    {
        Inspect,
        BuildRoad,
        Terraform,
        UpgradeGravel,
        UpgradeAsphalt,
        UpgradeHighway,
        UpgradeDualCarriage,
        BuildGarage,
        BuildLogisticsHub,
        BuildBusTerminal,
        BuildTollStation,
        BuildRail,
        BuildRailSignal,
        BuildIntermodalYard,
        Demolish
    }

    private enum WindowTab
    {
        Inspector,
        Schedule,
        Research,
        Fleet,
        Rail,
        Contracts,
        Operations,
        Bank,
        Log
    }

    private SimulationBridge _bridge = null!;
    private Node3D _terrainRoot = null!;
    private Node3D _roadsRoot = null!;
    private Node3D _structuresRoot = null!;
    private Node3D _trucksRoot = null!;

    private const int RoadChunkSize = 10;
    private const float DetailLodMaxDistance = 45f;
    private const float DetailLodMaxDistSq = DetailLodMaxDistance * DetailLodMaxDistance;
    private const float IndustryAnimLodMaxDistSq = 55f * 55f;

    private readonly HashSet<(int X, int Y)> _roadTileCoords = new();
    private readonly Dictionary<(int Cx, int Cy), Node3D> _roadChunkNodes = new();
    private readonly Dictionary<(int X, int Y), Node3D> _urbanTileNodes = new();
    private readonly Dictionary<int, Label3D> _cityLabels = new();
    private readonly Dictionary<int, Label3D> _industryLabels = new();
    private readonly Dictionary<int, Node3D> _industryNodes = new();
    private readonly Dictionary<int, Node3D> _garageNodes = new();
    private readonly Dictionary<int, Node3D> _hubNodes = new();
    private readonly Dictionary<int, Node3D> _busTerminalNodes = new();
    private readonly Dictionary<int, Node3D> _tollNodes = new();
    private readonly Dictionary<(int X, int Y), Node3D> _trafficControlNodes = new();
    private readonly Dictionary<int, TruckVisualHandle> _truckVisuals = new();
    private readonly Dictionary<int, (Node3D Root, Node3D Headlights, Vector3 LastForward)> _trafficVisuals = new();
    private readonly Dictionary<int, Label3D> _tollWeighLabels = new();
    private readonly List<Node3D> _animatedWindmills = new();
    private readonly List<Node3D> _animatedPumpjacks = new();
    private readonly List<Node3D> _animatedRoofFans = new();
    private readonly List<(Node3D LeftGate, Node3D RightGate, Vector3 WorldPos)> _animatedTollGates = new();
    private readonly Dictionary<int, (Node3D Root, List<Node3D> Trolleys, Node3D ShipNode, Label3D StatusLabel)> _seaportVisuals = new();
    private readonly Dictionary<int, List<Vector3>> _seaportSeaRoutes = new();
    private readonly Dictionary<int, (Node3D Root, List<Node3D> Rotors)> _landmarkVisuals = new();
    private readonly List<(GpuParticles3D Particles, float Age)> _transientParticles = new();
    private readonly List<IndustrialConveyorItem> _conveyorItems = new();
    private readonly Dictionary<int, List<IndustrialConveyorItem>> _industryConveyorMap = new();
    private float _mechanicalAnimTime = 0f;

    private enum GraphicsQualityPreset
    {
        Performance,
        Balanced,
        DioramaUltra
    }

    // Iluminação Calibrada & Pós-Processamento Diorama (Sprint G2 & Sprint G4)
    private DirectionalLight3D _sunLight = null!;
    private Godot.Environment _worldEnv = null!;
    private ProceduralSkyMaterial _skyMat = null!;
    private CameraAttributesPractical _cameraAttributes = null!;
    private GpuParticles3D _rainParticles = null!;
    private GpuParticles3D _snowParticles = null!;
    private MeshInstance3D _hoverCursor = null!;
    private MeshInstance3D _selectedTruckRing = null!;
    private MultiMeshInstance3D? _selectedRouteMultiMesh;
    private float _currentRoadWetness = 0.0f;
    private float _currentRoadSnowCover = 0.0f;
    private GraphicsQualityPreset _graphicsPreset = GraphicsQualityPreset.Balanced;
    private bool _tiltShiftEnabled = true;
    private bool _dynamicSolarCycleEnabled = true;
    private bool _userSsaoEnabled = true;
    private bool _userSsrEnabled = false;
    private bool _userShadows4Splits = false;
    private float _userShadowMaxDistance = 92.0f;
    private int _lastZoomLodTier = -1;
    private bool? _lastVehicleLightsOn;
    private float _minimapUpdateTimer = 0f;
    private readonly List<MultiMeshInstance3D> _vegShadowCasters = new();
    private readonly HashSet<int> _activeTruckIdsScratch = new();
    private readonly HashSet<int> _activeNpcIdsScratch = new();
    private readonly List<int> _staleIdsScratch = new();
    private readonly List<Vector2> _minimapTrainScratch = new();
    private (int X, int Y) _lastHoverTipCell = (-1, -1);
    private ToolMode _lastHoverTipTool = ToolMode.Inspect;
    private PanelContainer? _graphicsModal;
    private CheckBox? _chkSsao;
    private CheckBox? _chkGlow;
    private CheckBox? _chkSsr;
    private CheckBox? _chkTiltShift;
    private CheckBox? _chkShadows4Splits;
    private CheckBox? _chkDynamicSun;
    private CheckBox? _chkVsync;
    private HSlider? _hudScaleSlider;
    private Label? _hudScaleValLbl;
    private HSlider? _masterVolSlider;
    private Label? _masterVolValLbl;
    private HSlider? _ambientVolSlider;
    private Label? _ambientVolValLbl;
    private Button? _presetPerfBtn;
    private Button? _presetBalBtn;
    private Button? _presetUltraBtn;
    private Label? _graphicsStatusSummaryLbl;
    private ClientSettingsData _clientSettings = new();
    private bool _userVsyncEnabled = true;
    private float _hudScaleFactor = 1.0f;
    private int _selectedMapSize = 100;
    private WorldScenario _selectedScenario = WorldScenario.Balanced;
    private bool _selectedHasRivers = true;
    private MountainDensity _selectedMountains = MountainDensity.Normal;
    private OptionButton? _mapSizeSelect;
    private OptionButton? _scenarioSelect;
    private PanelContainer? _tutorialPanel;
    private Label? _tutorialHeaderLabel;
    private Label[] _tutorialStepLabels = Array.Empty<Label>();
    private Button? _tutorialToggleBtn;
    private bool _tutorialRoadBuilt = false;
    private bool _tutorialPavedAsphalt = false;

    // Câmera Orbital & Chase Cam
    private Node3D _cameraPivot = null!;
    private Camera3D _camera = null!;
    private float _cameraYaw = 0.65f;
    private float _cameraPitch = 0.62f;
    private float _cameraDistance = 32.0f;
    private bool _isOrbiting = false;
    private bool _isPanning = false;
    private int _focusCityIndex = 0;
    private int _followedTruckIndex = -1;

    // Interação no Grid
    private ToolMode _activeTool = ToolMode.UpgradeGravel;
    private WindowTab _activeTab = WindowTab.Inspector;
    private bool _corridorBatchMode = false;
    private (int X, int Y) _hoveredCell = (-1, -1);
    private (int X, int Y) _selectedCell = (-1, -1);
    private readonly List<(Label3D Label, float Age, float Duration)> _floatingPopups = new();

    // HUD Flutuante estilo Voxel Tycoon (Pills escuros nos cantos + Card branco no topo esquerdo)
    private PanelContainer _topLeftWindow = null!;
    private Label _windowTitleLabel = null!;
    private Label _topAlertLabel = null!;
    private Label _bottomBalanceLabel = null!;
    private Label _bottomLevelLabel = null!;
    private Label _bottomDateLabel = null!;
    private Label _bottomWeatherLabel = null!;

    private Control _tabBodyInspector = null!;
    private Control _tabBodySchedule = null!;
    private Control _tabBodyResearch = null!;
    private Control _tabBodyFleet = null!;
    private Control _tabBodyContracts = null!;
    private Control _tabBodyOperations = null!;
    private Control _tabBodyBank = null!;
    private Control _tabBodyLog = null!;

    // Sprint E6 — Inteligência Operacional (Gargalos & Conquistas/Milestones)
    private int _opsSubTabIndex = 0;
    private Button _opsSubTabDiagBtn = null!;
    private Button _opsSubTabMilestoneBtn = null!;
    private Control _opsSubBodyDiag = null!;
    private Control _opsSubBodyMilestones = null!;
    private Label _bottleneckSummaryLabel = null!;
    private VBoxContainer _bottleneckListBox = null!;
    private Label _milestoneSummaryLabel = null!;
    private VBoxContainer _milestoneListBox = null!;

    private RichTextLabel _inspectorText = null!;
    private Button _inspectorUpgradeIndustryBtn = null!;
    private Button _inspectorRepaveBtn = null!;
    private Button _inspectorTrafficCtrlBtn = null!;
    private Button _inspectorGarageAutoReplaceBtn = null!;
    private Button _inspectorGarageCloneBtn = null!;
    private RichTextLabel _contractsText = null!;
    private HBoxContainer _auctionBidRow = null!;
    private Button _auctionTariffBidBtn = null!;
    private Button _auctionExpressBidBtn = null!;
    private RichTextLabel _eventLogText = null!;

    // Aba Agenda (Schedule) + Frota Ativa estilo Voxel Tycoon
    private int _selectedTruckIndex = 0;
    private OptionButton _scheduleTruckSelect = null!;
    private OptionButton _scheduleLineSelect = null!;
    private LineEdit _scheduleLineNameEdit = null!;
    private Button _scheduleUpdateLineBtn = null!;
    private RichTextLabel _scheduleTruckInfoText = null!;
    private CheckBox _schedulePitStopCheck = null!;
    private OptionButton _scheduleActionSelect = null!;
    private OptionButton _scheduleTargetSelect = null!;
    private OptionButton _scheduleConditionSelect = null!;
    private OptionButton _scheduleCargoFilterSelect = null!;
    private OptionButton _scheduleMaxWaitSelect = null!;
    private CheckBox _lineHeadwayCheck = null!;
    private CheckBox _lineAutoReplaceCheck = null!;
    private VBoxContainer _scheduleStopsListBox = null!;
    private readonly List<(ScheduleStopKind Kind, int Id, string Label)> _scheduleTargetPool = new();
    private readonly List<ScheduleStop> _draftSchedule = new();
    private VBoxContainer _fleetActiveListBox = null!;
    private Label _modernizerSourceLabel = null!;
    private OptionButton _modernizerTargetSelect = null!;
    private Button _modernizerApplyBtn = null!;
    private readonly List<(int ModelIdx, Button Btn)> _fleetBuyButtons = new();

    // Sprint E3 — Showroom 3D, Oficina de Pintura (Livery) & Sub-abas da Concessionária
    private int _fleetSubTabIndex = 0;
    private Button _fleetSubTabBuyBtn = null!;
    private Button _fleetSubTabShowcaseBtn = null!;
    private Button _fleetSubTabActiveBtn = null!;
    private Control _fleetSubBodyBuy = null!;
    private Control _fleetSubBodyShowcase = null!;
    private Control _fleetSubBodyActive = null!;
    private SubViewportContainer? _showcaseViewportContainer;
    private Node3D? _showcaseTurntable;
    private Camera3D? _showcaseCamera;
    private OptionButton _showcaseModelSelect = null!;
    private OptionButton _showcaseCargoSelect = null!;
    private OptionButton _showcaseRankSelect = null!;
    private Button _showcaseHeadlightsBtn = null!;
    private bool _showcaseHeadlightsOn = true;
    private float _showcaseYaw = -0.58f;
    private float _showcasePitch = -0.30f;
    private float _showcaseZoom = 2.65f;
    private bool _showcaseDragging = false;
    private bool _showcaseAutoSpin = true;
    private LineEdit _liveryCompanyNameEdit = null!;
    private OptionButton _liveryScopeSelect = null!;
    private ColorPickerButton _liveryPrimaryColorBtn = null!;
    private ColorPickerButton _liveryStripeColorBtn = null!;
    private OptionButton _liveryFinishSelect = null!;
    private Button _liveryApplyBtn = null!;
    private Button _liveryResetLineBtn = null!;
    private bool _liveryDirtyFromUser = false;
    private string _showcaseRenderedKey = string.Empty;
    private TruckVisualHandle? _showcaseHandle;
    private bool _showcaseBuilt = false;
    private float _routeMarkerUpdateTimer = 0f;
    private int _lastRouteMarkerTruckId = -1;
    private int _lastRouteMarkerEdgeCount = -1;

    // Aba Pesquisa & Tecnologia (Research Management)
    private Label _researchActiveLabel = null!;
    private ProgressBar _researchActiveProgressBar = null!;
    private Button _researchPauseBtn = null!;
    private readonly Dictionary<ResearchTechId, (Label StatusLbl, ProgressBar Bar, Button ActionBtn)> _researchRows = new();

    // Expansão Regional (Aba Contratos & Regiões)
    private Label _regionalStatusLabel = null!;
    private Button _exploreRegionBtn = null!;

    // Banco Central & DRE Financeira
    private bool _dreShowLifetime = false;
    private Label _bankSummaryLabel = null!;
    private Button _drePeriodToggleBtn = null!;
    private Button _autoRoadMaintToggleBtn = null!;
    private DreChartControl _dreChart = null!;
    private RichTextLabel _dreReportText = null!;

    // Gerenciador de Múltiplos Saves & Seeds
    private PanelContainer? _savesModal;
    private VBoxContainer _savesListBox = null!;
    private LineEdit _seedInputEdit = null!;

    // Sprint E7 — Áudio Espacial 3D & Modo Foto / Câmera Drone Automática
    private enum PhotoFilterMode
    {
        Default = 0,
        Retro90s = 1,
        ToyModelTiltShift = 2,
        WarmSunset = 3
    }

    private Control _hudRegularLayer = null!;
    private PanelContainer _photoModeBar = null!;
    private bool _photoModeActive = false;
    private bool _photoLockHour = false;
    private float _photoHourValue = 17.5f;
    private int _photoWeatherOverride = 0; // 0=Auto, 1=Clear, 2=Rain, 3=Storm, 4=Blizzard
    private PhotoFilterMode _photoFilterMode = PhotoFilterMode.Default;
    private bool _droneTourActive = false;
    private float _droneTourTimer = 0f;
    private int _droneStopIndex = 0;
    private Button? _photoDroneBtn;
    private CheckBox? _photoHourCheck;
    private HSlider? _photoHourSlider;
    private Label? _photoHourLabel;
    private Label? _photoStatusLabel;
    private readonly Dictionary<int, double> _truckHornCooldowns = new();
    private Label _savesCurrentSeedLabel = null!;

    // Câmera Picture-in-Picture (PiP) do Caminhão Selecionado
    private PanelContainer _pipPanel = null!;
    private Camera3D _pipCamera = null!;
    private Label _pipTitleLabel = null!;
    private Button _pipToggleBtn = null!;

    // Minimapa Estratégico & Camadas de Dados (Heatmaps)
    private MinimapOverlay _minimapOverlay = null!;
    private bool _minimapStaticDirty = true;
    private readonly List<(Vector2 GridPos, bool Manual)> _minimapTruckScratch = new();
    private MapOverlayMode _overlayMode = MapOverlayMode.Standard;
    private MultiMeshInstance3D? _heatmapMultiMesh;
    private int[,]? _heatmapTrafficBuf;
    private Button _overlayStandardBtn = null!;
    private Button _overlayTrafficBtn = null!;
    private Button _overlayPavementBtn = null!;
    private Button _overlayProfitBtn = null!;

    // Construtor de Estradas por Arrasto (Drag-and-Drop)
    private bool _isDraggingRoad = false;
    private (int X, int Y) _roadDragStart = (-1, -1);
    private RoadBuildPlan? _roadDragPlan;
    private MultiMeshInstance3D? _roadPreviewMultiMesh;

    // Auto-Save Mensal
    private bool _autoSaveEnabled = true;
    private Button _autoSaveToggleBtn = null!;

    // Áudio Sintetizado Procedural (AudioStreamGenerator)
    private ProceduralAudioSynth _audio = null!;
    private Button _muteAudioBtn = null!;

    private readonly Dictionary<ToolMode, Button> _toolButtons = new();
    private readonly Dictionary<float, Button> _speedButtons = new();
    private readonly Dictionary<WindowTab, Button> _tabButtons = new();
    private Button _corridorBatchBtn = null!;

    private StyleBoxFlat _pillBtnNormal = null!;
    private StyleBoxFlat _pillBtnActive = null!;
    private StyleBoxFlat _tabBtnNormal = null!;
    private StyleBoxFlat _tabBtnActive = null!;

    private readonly Queue<string> _recentEvents = new();
    private float _uiRefreshTimer = 0f;
    private float _spatialAudioTimer = 0f;
    private float _opsDiagTimer = 1.5f;
    private int _heatmapLastRoadCount = -1;
    private bool _heatmapTransformsDirty = true;
    private ToolMode? _lastRenderedTool;
    private bool? _lastRenderedCorridorBatch;
    private float _lastRenderedTimeScale = -1f;
    private long _lastHudBalance = long.MinValue;
    private long _lastHudDebt = long.MinValue;
    private int _lastHudLevel = -1;
    private int _lastHudXp = -1;
    private int _lastHudSeed = -1;
    private int _lastHudDay = -1;
    private int _lastHudMonth = -1;
    private int _lastHudHour = -1;
    private int _lastHudMinute = -1;
    private WeatherCondition? _lastHudWeather;
    private SeasonalEventType? _lastHudSeason;
    private string _lastContractsBbcode = string.Empty;
    private int _lastOpsCount = -1;
    private int _lastOpsCriticalCount = -1;

    public override void _Ready()
    {
        ProceduralModels3D.InitializeSharedMaterials();
        _clientSettings = ClientSettingsStore.Load();
        _selectedMapSize = _clientSettings.PreferredMapSize;
        _selectedScenario = Enum.TryParse<WorldScenario>(_clientSettings.PreferredScenario, out var parsedScen)
            ? parsedScen
            : WorldScenario.Balanced;
        _selectedHasRivers = _clientSettings.PreferredHasRivers;
        _selectedMountains = Enum.TryParse<MountainDensity>(_clientSettings.PreferredMountains, out var parsedMtns)
            ? parsedMtns
            : MountainDensity.Normal;
        _autoSaveEnabled = _clientSettings.AutoSaveEnabled;
        _userVsyncEnabled = _clientSettings.VSyncEnabled;
        _hudScaleFactor = Math.Clamp(_clientSettings.HudScale, 0.85f, 1.25f);

        _audio = new ProceduralAudioSynth { Name = "AudioSynth" };
        AddChild(_audio);
        _audio.MasterVolume = Math.Clamp(_clientSettings.MasterVolume, 0f, 1f);
        _audio.AmbientVolume = Math.Clamp(_clientSettings.AmbientVolume, 0f, 1f);
        _audio.SetMuted(_clientSettings.AudioMuted);

        // 1. Inicializa a simulação com Seed RNG aleatória para compor o diorama de fundo do menu
        InitializeSimulation(seed: null);

        // 2. Configura ambiente Voxel Tycoon, mundo, HUD e menu prévio
        SetupEnvironmentAndCamera();
        ApplySavedSettingsOnStartup();
        BuildInitialWorld3D();
        BuildVoxelTycoonHud();
        _audio.UpdateEnvironmentState(_bridge.Engine.CurrentWeather, _bridge.TimeScale);

        FocusOnFirstCity();
        SyncFleet3DTransforms(0.01f);

        LogEvent($"🎲 Mundo {_selectedMapSize}x{_selectedMapSize} gerado (Seed #{_bridge.Seed})! Pressione [ESPAÇO] ou ▶ para iniciar.");
        RefreshHud();
        EnterMainMenu();
    }

    private void InitializeSimulation(int? seed, GeneratedWorld? prebuiltWorld = null)
    {
        _bridge = new SimulationBridge(
            width: _selectedMapSize,
            height: _selectedMapSize,
            seed: seed,
            scenario: _selectedScenario,
            hasRivers: _selectedHasRivers,
            mountains: _selectedMountains,
            prebuiltWorld: prebuiltWorld);
        _bridge.TimeScale = 0.0f;
        _bridge.SeedInitialFleetAndInfrastructure();
        _tutorialRoadBuilt = false;
        _tutorialPavedAsphalt = false;
        SubscribeEngineEvents();
    }

    private void ApplySavedSettingsOnStartup()
    {
        var preset = Enum.TryParse<GraphicsQualityPreset>(_clientSettings.Preset, out var parsedPreset)
            ? parsedPreset
            : GraphicsQualityPreset.Balanced;
        ApplyGraphicsPreset(preset, logMessage: false, persist: false);

        _userSsaoEnabled = _clientSettings.SsaoEnabled;
        _worldEnv.SsaoEnabled = _userSsaoEnabled;
        _worldEnv.GlowEnabled = _clientSettings.GlowEnabled;
        _userSsrEnabled = _clientSettings.SsrEnabled;
        _worldEnv.SsrEnabled = _userSsrEnabled;
        _tiltShiftEnabled = _clientSettings.TiltShiftEnabled;
        _userShadows4Splits = _clientSettings.Shadows4Splits;
        _userShadowMaxDistance = _userShadows4Splits ? 125.0f : 85.0f;
        _sunLight.DirectionalShadowMode = _userShadows4Splits
            ? DirectionalLight3D.ShadowMode.Parallel4Splits
            : DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sunLight.DirectionalShadowMaxDistance = _userShadowMaxDistance;
        _dynamicSolarCycleEnabled = _clientSettings.DynamicSolarCycle;

        if (_userSsaoEnabled)
        {
            GetViewport().Msaa3D = Viewport.Msaa.Disabled;
            GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        }

        ApplyVsyncSetting(_userVsyncEnabled);
        ApplyHudScaleSetting(_hudScaleFactor);
        ApplyZoomPerformanceLod(force: true);
        UpdateCameraTransform();
    }

    private void ApplyVsyncSetting(bool enabled)
    {
        _userVsyncEnabled = enabled;
        DisplayServer.WindowSetVsyncMode(enabled ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }

    private void ApplyHudScaleSetting(float scale)
    {
        _hudScaleFactor = Math.Clamp(scale, 0.85f, 1.25f);
        var win = GetWindow();
        if (win != null)
            win.ContentScaleFactor = _hudScaleFactor;
    }

    private void PersistClientSettings()
    {
        _clientSettings.Preset = _graphicsPreset.ToString();
        _clientSettings.SsaoEnabled = _userSsaoEnabled;
        _clientSettings.GlowEnabled = _worldEnv.GlowEnabled;
        _clientSettings.SsrEnabled = _userSsrEnabled;
        _clientSettings.TiltShiftEnabled = _tiltShiftEnabled;
        _clientSettings.Shadows4Splits = _userShadows4Splits;
        _clientSettings.DynamicSolarCycle = _dynamicSolarCycleEnabled;
        _clientSettings.VSyncEnabled = _userVsyncEnabled;
        _clientSettings.HudScale = _hudScaleFactor;
        if (_audio != null)
        {
            _clientSettings.MasterVolume = _audio.MasterVolume;
            _clientSettings.AmbientVolume = _audio.AmbientVolume;
            _clientSettings.AudioMuted = _audio.IsMuted;
        }
        _clientSettings.AutoSaveEnabled = _autoSaveEnabled;
        _clientSettings.PreferredMapSize = _selectedMapSize;
        _clientSettings.PreferredScenario = _selectedScenario.ToString();
        _clientSettings.PreferredHasRivers = _selectedHasRivers;
        _clientSettings.PreferredMountains = _selectedMountains.ToString();
        ClientSettingsStore.Save(_clientSettings);
    }

    private void SubscribeEngineEvents()
    {
        _bridge.Engine.OnCityExpanded += OnCityExpanded;
        _bridge.Engine.OnDemandIncreased += OnDemandIncreased;
        _bridge.Engine.OnRegionUnlocked += OnRegionUnlocked;
        _bridge.Engine.OnRoadUpgraded += OnRoadUpgraded;
        _bridge.Engine.OnDeliveryCompleted += OnDeliveryCompleted;
        _bridge.Engine.OnGarageBuilt += OnGarageBuilt;
        _bridge.Engine.OnLogisticsHubBuilt += OnLogisticsHubBuilt;
        _bridge.Engine.OnBusTerminalBuilt += OnBusTerminalBuilt;
        _bridge.Engine.OnPassengersDelivered += OnPassengersDelivered;
        _bridge.Engine.OnContractCompleted += OnContractCompleted;
        _bridge.Engine.OnWeatherChanged += OnWeatherChanged;
        _bridge.Engine.OnResearchCompleted += OnResearchCompleted;
        _bridge.Engine.OnMonthlyFinancialClosed += OnMonthlyFinancialClosed;
        _bridge.Engine.OnTruckPitStopCompleted += OnTruckPitStopCompleted;
        _bridge.Engine.OnIndustryUpgraded += OnIndustryUpgraded;
        _bridge.Engine.OnSeasonalEventChanged += OnSeasonalEventChanged;
        _bridge.Engine.OnMunicipalSubsidyPaid += OnMunicipalSubsidyPaid;
        _bridge.Engine.OnRoadMaintained += OnRoadMaintained;
        _bridge.Engine.OnTrafficControlBuilt += OnTrafficControlBuilt;
        _bridge.Engine.OnTollStationBuilt += OnTollStationBuilt;
        _bridge.Engine.OnTollCollected += OnTollCollected;
        _bridge.Engine.OnTollWeighUpdated += OnTollWeighUpdated;
        _bridge.Engine.OnContractLostToRival += OnContractLostToRival;
        _bridge.Engine.OnContractAuctionWon += OnContractAuctionWon;
        _bridge.Engine.OnRivalRoadBuilt += OnRivalRoadBuilt;
        _bridge.Engine.OnRivalTollPaid += OnRivalTollPaid;
        _bridge.Engine.OnTruckRankUp += OnTruckRankUp;
        _bridge.Engine.OnSeaportShipStatusChanged += OnSeaportShipStatusChanged;
        _bridge.Engine.OnLandmarkProgress += OnLandmarkProgress;
        _bridge.Engine.OnMilestoneUnlocked += OnMilestoneUnlocked;
        _bridge.Engine.OnTruckAutoReplaced += OnTruckAutoReplaced;
    }

    private void ClearWorldVisuals()
    {
        foreach (Node child in _terrainRoot.GetChildren()) child.QueueFree();
        foreach (Node child in _roadsRoot.GetChildren()) child.QueueFree();
        foreach (Node child in _structuresRoot.GetChildren()) child.QueueFree();
        foreach (Node child in _trucksRoot.GetChildren()) child.QueueFree();

        _roadTileCoords.Clear();
        _roadChunkNodes.Clear();
        _urbanTileNodes.Clear();
        _cityLabels.Clear();
        _industryLabels.Clear();
        _industryNodes.Clear();
        _garageNodes.Clear();
        _liveryDirtyFromUser = false;
        _showcaseRenderedKey = string.Empty;
        _hubNodes.Clear();
        _busTerminalNodes.Clear();
        _tollNodes.Clear();
        _tollWeighLabels.Clear();
        _trafficControlNodes.Clear();
        _truckVisuals.Clear();
        _trafficVisuals.Clear();
        _vegShadowCasters.Clear();
        _lastZoomLodTier = -1;
        _lastVehicleLightsOn = null;
        _animatedWindmills.Clear();
        _animatedPumpjacks.Clear();
        _animatedRoofFans.Clear();
        _animatedTollGates.Clear();
        _seaportVisuals.Clear();
        _seaportSeaRoutes.Clear();
        _landmarkVisuals.Clear();
        _truckHornCooldowns.Clear();
        ClearRailVisuals();
        foreach (var (part, _) in _transientParticles)
        {
            if (IsInstanceValid(part))
                part.QueueFree();
        }
        _transientParticles.Clear();
        _conveyorItems.Clear();
        _industryConveyorMap.Clear();
        foreach (var popup in _floatingPopups)
        {
            if (IsInstanceValid(popup.Label))
                popup.Label.QueueFree();
        }
        _floatingPopups.Clear();
        _recentEvents.Clear();
        _heatmapMultiMesh = null;
        _roadPreviewMultiMesh = null;
        _selectedRouteMultiMesh = null;
        _isDraggingRoad = false;
        _roadDragPlan = null;
    }

    private void SaveGameToUserDir()
    {
        string path = ProjectSettings.GlobalizePath("user://savegame.json");
        _bridge.SaveToFile(path);
        _audio?.PlaySaveLoad(isLoad: false);
        LogEvent($"💾 Jogo salvo com sucesso (Seed #{_bridge.Seed})!");
    }

    private void LoadAutoSaveFromUserDir()
    {
        LoadGameFromPath("user://autosave.json", "autosave.json");
    }

    private void LoadGameFromUserDir()
    {
        LoadGameFromPath("user://savegame.json", "savegame.json");
    }

    private void LoadGameFromPath(string userPath, string displayName)
    {
        string path = ProjectSettings.GlobalizePath(userPath);
        if (!System.IO.File.Exists(path))
        {
            _audio?.PlayError();
            LogEvent($"⚠️ Nenhum save encontrado em {userPath}.");
            return;
        }

        ClearWorldVisuals();
        _bridge.LoadFromFile(path);
        SubscribeEngineEvents();
        BuildInitialWorld3D();
        FocusOnFirstCity();
        SyncFleet3DTransforms(0.01f);
        _selectedTruckIndex = 0;
        RebuildSchedulePools();

        _audio?.PlaySaveLoad(isLoad: true);
        LogEvent($"📂 Jogo carregado de {displayName} (Seed #{_bridge.Seed} | Dia {_bridge.Engine.Calendar.Day:D2}/{_bridge.Engine.Calendar.Month:D2})!");
        RefreshHud();
    }

    private void FocusOnFirstCity()
    {
        _followedTruckIndex = -1;
        _focusCityIndex = 0;
        if (_bridge.World.Cities.Count > 0)
        {
            var c = _bridge.World.Cities[0];
            _cameraPivot.Position = GridToWorld(c.CenterX, c.CenterY);
            _selectedCell = (c.CenterX, c.CenterY);
        }
        else
        {
            int mx = _bridge.World.Grid.GetLength(0) / 2;
            int my = _bridge.World.Grid.GetLength(1) / 2;
            _cameraPivot.Position = GridToWorld(mx, my);
            _selectedCell = (mx, my);
        }
    }

    private void RegenerateRandomWorld()
    {
        ClearWorldVisuals();

        // Gera um novo mundo completamente aleatório com nova Seed RNG
        InitializeSimulation(seed: null);
        BuildInitialWorld3D();
        FocusOnFirstCity();
        SyncFleet3DTransforms(0.01f);
        _selectedTruckIndex = 0;
        RebuildSchedulePools();

        LogEvent($"🎲 Novo mundo aleatório gerado (Seed #{_bridge.Seed})!");
        RefreshHud();
    }

    private void SetupEnvironmentAndCamera()
    {
        _skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("7ab3dc"),
            SkyHorizonColor = new Color("d9e6f2"),
            GroundBottomColor = new Color("8ba388"),
            GroundHorizonColor = new Color("d9e6f2"),
            SkyEnergyMultiplier = 0.72f
        };

        var sky = new Sky
        {
            SkyMaterial = _skyMat,
            ProcessMode = Sky.ProcessModeEnum.Incremental
        };

        _worldEnv = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = new Color("dce6f2"),
            AmbientLightSkyContribution = 0.35f,
            AmbientLightEnergy = 0.58f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,

            // Névoa suave de distância (calibrada para mapa amplo 100x100 sem ofuscar zoom alto)
            FogEnabled = true,
            FogLightColor = new Color("d9e6f2"),
            FogLightEnergy = 0.75f,
            FogDensity = 0.0011f,
            FogSkyAffect = 0.35f,

            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 0.85f,
            TonemapWhite = 5.0f,

            SdfgiEnabled = false,
            SsilEnabled = false,
            VolumetricFogEnabled = false,

            // Oclusão de Ambiente (SSAO — sombras de contato nos degraus de voxel, prédios e pneus)
            SsaoEnabled = true,
            SsaoRadius = 0.72f,
            SsaoIntensity = 1.35f,
            SsaoPower = 1.45f,
            SsaoDetail = 0.55f,
            SsaoHorizon = 0.08f,
            SsaoSharpness = 0.96f,
            SsaoLightAffect = 0.25f,

            // Reflexos de Espaço de Tela (SSR — reflexos em poças de chuva e água)
            SsrEnabled = false,
            SsrMaxSteps = 32,
            SsrFadeIn = 0.15f,
            SsrFadeOut = 2.0f,
            SsrDepthTolerance = 0.25f,

            // Bloom Cinematográfico Sutil (Glow — faróis, lanternas triplas, semáforos e janelas noturnas)
            GlowEnabled = true,
            GlowNormalized = true,
            GlowIntensity = 0.48f,
            GlowStrength = 0.92f,
            GlowBloom = 0.08f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            GlowHdrThreshold = 1.02f,
            GlowHdrScale = 1.8f
        };

        var envNode = new WorldEnvironment { Environment = _worldEnv };
        AddChild(envNode);

        _sunLight = new DirectionalLight3D
        {
            LightColor = new Color("fff8eb"),
            LightEnergy = 0.82f,
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowMaxDistance = 120.0f,
            ShadowBias = 0.03f,
            ShadowNormalBias = 1.4f,
            ShadowBlur = 1.15f,
            RotationDegrees = new Vector3(-50f, 38f, 0f)
        };
        AddChild(_sunLight);

        _terrainRoot = new Node3D { Name = "TerrainRoot" };
        _roadsRoot = new Node3D { Name = "RoadsRoot" };
        _structuresRoot = new Node3D { Name = "StructuresRoot" };
        _trucksRoot = new Node3D { Name = "TrucksRoot" };

        AddChild(_terrainRoot);
        AddChild(_roadsRoot);
        AddChild(_structuresRoot);
        AddChild(_trucksRoot);

        var cursorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.98f, 0.80f, 0.08f, 0.42f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _hoverCursor = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(ProceduralModels3D.TileSize * 0.96f, 0.12f, ProceduralModels3D.TileSize * 0.96f) },
            MaterialOverride = cursorMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(_hoverCursor);

        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color("38bdf8"),
            EmissionEnabled = true,
            Emission = new Color("38bdf8"),
            EmissionEnergyMultiplier = 1.65f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _selectedTruckRing = new MeshInstance3D
        {
            Name = "SelectedTruckRing3D",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.72f,
                BottomRadius = 0.78f,
                Height = 0.04f,
                RadialSegments = 24
            },
            MaterialOverride = ringMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(_selectedTruckRing);

        int midX = _bridge.World.Grid.GetLength(0) / 2;
        int midY = _bridge.World.Grid.GetLength(1) / 2;
        _cameraPivot = new Node3D { Position = GridToWorld(midX, midY) };
        AddChild(_cameraPivot);

        _rainParticles = ProceduralModels3D.CreateWeatherRainSystem();
        _cameraPivot.AddChild(_rainParticles);

        _snowParticles = ProceduralModels3D.CreateWeatherSnowSystem();
        _cameraPivot.AddChild(_snowParticles);

        _cameraAttributes = new CameraAttributesPractical
        {
            DofBlurFarEnabled = true,
            DofBlurFarDistance = 46.0f,
            DofBlurFarTransition = 26.0f,
            DofBlurNearEnabled = false,
            DofBlurAmount = 0.042f
        };

        _camera = new Camera3D
        {
            Fov = 38f, // FOV mais fechado para efeito isométrico/diorama das capturas
            Near = 0.5f,
            Far = 480f,
            Attributes = _cameraAttributes,
            Current = true
        };
        _cameraPivot.AddChild(_camera);
        ApplyGraphicsPreset(GraphicsQualityPreset.Balanced, logMessage: false);
        UpdateCameraTransform();
    }

    private void BuildInitialWorld3D()
    {
        var grid = _bridge.World.Grid;
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        var terrainMesh = ProceduralModels3D.BuildContinuousTerrainMesh(grid);
        terrainMesh.Name = "VoxelTerrainMesh";
        _terrainRoot.AddChild(terrainMesh);

        _vegShadowCasters.Clear();
        var biomeVegGroup = ProceduralModels3D.BuildBiomeVegetationGroup(grid, _bridge.Seed, _vegShadowCasters);
        _terrainRoot.AddChild(biomeVegGroup);
        ApplyZoomPerformanceLod(force: true);

        var waterMesh = ProceduralModels3D.BuildContinuousWaterMesh(grid);
        _terrainRoot.AddChild(waterMesh);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y].HasRoad)
                {
                    _roadTileCoords.Add((x, y));
                }
            }
        }
        RebuildAllRoadChunks();

        foreach (var city in _bridge.World.Cities)
            SpawnCityVisuals(city);

        foreach (var ind in _bridge.World.Industries)
            SpawnIndustryVisuals(ind);

        foreach (var garage in _bridge.Engine.Garages)
        {
            SpawnGarageMesh(garage);
        }

        foreach (var hub in _bridge.Engine.LogisticsHubs)
        {
            SpawnLogisticsHubMesh(hub);
        }

        foreach (var terminal in _bridge.Engine.BusTerminals)
        {
            SpawnBusTerminalMesh(terminal);
        }

        foreach (var node in _bridge.World.RoadNodes)
        {
            if (node.HasTrafficControl)
                SpawnTrafficControlMesh(node);
        }

        foreach (var toll in _bridge.Engine.TollStations)
        {
            SpawnTollStationMesh(toll);
        }

        foreach (var seaport in _bridge.World.Seaports)
        {
            SpawnSeaportMesh(seaport);
        }

        foreach (var landmark in _bridge.World.Landmarks)
        {
            SpawnLandmarkMesh(landmark);
        }

        RebuildAllRailAndStructures3D();
        InitializeOverlayMultiMeshes();
        _minimapStaticDirty = true;
    }

    private void InitializeOverlayMultiMeshes()
    {
        float ts = ProceduralModels3D.TileSize;
        var overlayMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = false
        };

        var heatMesh = new BoxMesh { Size = new Vector3(ts * 0.86f, 0.08f, ts * 0.86f) };
        var heatMm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = heatMesh,
            InstanceCount = 0
        };
        _heatmapMultiMesh = new MultiMeshInstance3D
        {
            Name = "HeatmapOverlay3D",
            Multimesh = heatMm,
            MaterialOverride = overlayMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        _roadsRoot.AddChild(_heatmapMultiMesh);

        var previewMesh = new BoxMesh { Size = new Vector3(ts * 0.84f, 0.12f, ts * 0.84f) };
        var previewMm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = previewMesh,
            InstanceCount = 0
        };
        _roadPreviewMultiMesh = new MultiMeshInstance3D
        {
            Name = "RoadDragPreview3D",
            Multimesh = previewMm,
            MaterialOverride = overlayMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        _roadsRoot.AddChild(_roadPreviewMultiMesh);

        var routeMesh = new BoxMesh { Size = new Vector3(0.32f, 0.05f, 0.72f) };
        var routeMm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = routeMesh,
            InstanceCount = 0
        };
        _selectedRouteMultiMesh = new MultiMeshInstance3D
        {
            Name = "SelectedTruckRoute3D",
            Multimesh = routeMm,
            MaterialOverride = overlayMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        _roadsRoot.AddChild(_selectedRouteMultiMesh);

        RefreshHeatmapOverlay();
    }

    private void SpawnCityVisuals(City city)
    {
        foreach (var (ux, uy) in city.UrbanTiles)
        {
            SpawnUrbanTileIfNeeded(city, ux, uy);
        }

        if (!_cityLabels.ContainsKey(city.Id))
        {
            var label = ProceduralModels3D.CreateFloatingLabel(GetCityBannerText(city), new Color("fde047"), 3.2f, 38);
            label.Position = GridToWorld(city.CenterX, city.CenterY) + new Vector3(0, 3.2f, 0);
            _structuresRoot.AddChild(label);
            _cityLabels[city.Id] = label;
        }
    }

    private void SpawnIndustryVisuals(Industry ind)
    {
        if (_industryNodes.TryGetValue(ind.Id, out var oldIndNode) && IsInstanceValid(oldIndNode))
        {
            oldIndNode.QueueFree();
        }
        if (_industryConveyorMap.TryGetValue(ind.Id, out var oldItems))
        {
            foreach (var item in oldItems)
                _conveyorItems.Remove(item);
            oldItems.Clear();
        }

        var localItems = new List<IndustrialConveyorItem>();
        var connections = _bridge.Engine.GetIndustryConnections(ind);
        var indNode = ProceduralModels3D.CreateIndustryComplexNode(ind, connections, out var animPart, localItems);
        indNode.Position = GridToWorld(ind.GridX, ind.GridY);
        _structuresRoot.AddChild(indNode);
        _industryNodes[ind.Id] = indNode;
        _industryConveyorMap[ind.Id] = localItems;
        _conveyorItems.AddRange(localItems);

        if (animPart != null)
        {
            if (animPart.Name == "PumpjackBeam")
                _animatedPumpjacks.Add(animPart);
            else if (animPart.Name == "RoofExhaustFan")
                _animatedRoofFans.Add(animPart);
            else
                _animatedWindmills.Add(animPart);
        }

        if (!_industryLabels.ContainsKey(ind.Id))
        {
            var label = ProceduralModels3D.CreateFloatingLabel(GetIndustryBannerText(ind), new Color("fca5a5"), 2.5f, 32);
            label.Position = GridToWorld(ind.GridX, ind.GridY) + new Vector3(0, 2.5f, 0);
            _structuresRoot.AddChild(label);
            _industryLabels[ind.Id] = label;
        }
    }

    private void RefreshIndustryVisualsNear(int gx, int gy)
    {
        foreach (var ind in _bridge.World.Industries)
        {
            int dist = Math.Abs(ind.GridX - gx) + Math.Abs(ind.GridY - gy);
            if (dist <= 2)
                SpawnIndustryVisuals(ind);
        }
    }

    private static (int Cx, int Cy) GetRoadChunkCoord(int x, int y)
        => (x / RoadChunkSize, y / RoadChunkSize);

    private HashSet<(int X, int Y)> BuildFacilityDockTileSet()
    {
        var docks = new HashSet<(int X, int Y)>();
        foreach (var ind in _bridge.World.Industries)
            docks.Add((ind.GridX, ind.GridY));
        foreach (var sp in _bridge.World.Seaports)
            docks.Add((sp.GridX, sp.GridY));
        foreach (var lm in _bridge.World.Landmarks)
            docks.Add((lm.GridX, lm.GridY));
        foreach (var city in _bridge.World.Cities)
            docks.Add((city.CenterX, city.CenterY));
        foreach (var g in _bridge.Engine.Garages)
            docks.Add((g.GridX, g.GridY));
        foreach (var h in _bridge.Engine.LogisticsHubs)
            docks.Add((h.GridX, h.GridY));
        foreach (var bt in _bridge.Engine.BusTerminals)
            docks.Add((bt.GridX, bt.GridY));
        foreach (var it in _bridge.Engine.IntermodalTerminals)
            docks.Add((it.GridX, it.GridY));
        return docks;
    }

    private void RebuildRoadChunk(int cx, int cy)
    {
        var chunkKey = (cx, cy);
        if (_roadChunkNodes.TryGetValue(chunkKey, out var oldChunk) && IsInstanceValid(oldChunk))
        {
            oldChunk.QueueFree();
        }
        _roadChunkNodes.Remove(chunkKey);

        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        int startX = cx * RoadChunkSize;
        int startY = cy * RoadChunkSize;
        if (startX < 0 || startY < 0 || startX >= w || startY >= h)
            return;

        int endX = Math.Min(w, startX + RoadChunkSize);
        int endY = Math.Min(h, startY + RoadChunkSize);

        var facilityDocks = BuildFacilityDockTileSet();
        var tiles = new List<ProceduralModels3D.RoadTileRenderInput>();
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                var cell = grid[x, y];
                if (cell.RoadLevel == RoadTier.None)
                {
                    _roadTileCoords.Remove((x, y));
                    continue;
                }

                _roadTileCoords.Add((x, y));
                bool connectN = y > 0 && grid[x, y - 1].HasRoad;
                bool connectS = y < h - 1 && grid[x, y + 1].HasRoad;
                bool connectE = x < w - 1 && grid[x + 1, y].HasRoad;
                bool connectW = x > 0 && grid[x - 1, y].HasRoad;
                bool dockN = y > 0 && facilityDocks.Contains((x, y - 1));
                bool dockS = y < h - 1 && facilityDocks.Contains((x, y + 1));
                bool dockE = x < w - 1 && facilityDocks.Contains((x + 1, y));
                bool dockW = x > 0 && facilityDocks.Contains((x - 1, y));

                float pavCond = _bridge.Engine.GetCellPavementCondition(x, y);
                bool blockedByLandslide = _bridge.Engine.IsCellBlockedByLandslide(x, y);
                string? rivalColor = null;
                if (cell.RivalOwnerId > 0)
                {
                    rivalColor = _bridge.Engine.Rivals.FirstOrDefault(r => r.Id == cell.RivalOwnerId)?.ColorHex;
                }
                bool downtownCongested = cell.RivalOwnerId == 0 && _bridge.Engine.IsTileInCongestedDowntown(x, y, out _, out _);

                tiles.Add(new ProceduralModels3D.RoadTileRenderInput(
                    x,
                    y,
                    GridToWorld(x, y),
                    cell.RoadLevel,
                    connectN,
                    connectS,
                    connectE,
                    connectW,
                    cell.StructureType,
                    cell.Biome,
                    pavCond,
                    cell.Terrain,
                    blockedByLandslide,
                    cell.RivalOwnerId,
                    rivalColor,
                    downtownCongested,
                    dockN,
                    dockS,
                    dockE,
                    dockW));
            }
        }

        if (tiles.Count == 0)
            return;

        var chunkNode = ProceduralModels3D.CreateRoadChunkNode(tiles);
        chunkNode.Name = $"RoadChunk_{cx}_{cy}";
        _roadsRoot.AddChild(chunkNode);
        _roadChunkNodes[chunkKey] = chunkNode;
    }

    private void RebuildRoadTileMesh(int x, int y)
    {
        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        if (x < 0 || x >= w || y < 0 || y >= h) return;

        var (cx, cy) = GetRoadChunkCoord(x, y);
        RebuildRoadChunk(cx, cy);
        _minimapStaticDirty = true;
    }

    private void RebuildAllRoadChunks()
    {
        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        var chunksToBuild = new HashSet<(int Cx, int Cy)>(_roadChunkNodes.Keys);
        _roadTileCoords.Clear();

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (grid[x, y].HasRoad)
                {
                    _roadTileCoords.Add((x, y));
                    chunksToBuild.Add(GetRoadChunkCoord(x, y));
                }
            }
        }

        foreach (var (cx, cy) in chunksToBuild)
        {
            RebuildRoadChunk(cx, cy);
        }

        _minimapStaticDirty = true;
    }

    private void RebuildAllRoadTileMeshes()
    {
        RebuildAllRoadChunks();
    }

    private void SpawnUrbanTileIfNeeded(City city, int ux, int uy)
    {
        var grid = _bridge.World.Grid;
        if (ux < 0 || ux >= grid.GetLength(0) || uy < 0 || uy >= grid.GetLength(1))
            return;

        // Regra estrita: prédios NUNCA são construídos sobre rodovias ou água
        if (grid[ux, uy].HasRoad || grid[ux, uy].Terrain != TerrainType.Plains)
            return;

        if (_urbanTileNodes.ContainsKey((ux, uy)))
            return;

        bool isCenter = (ux == city.CenterX && uy == city.CenterY);
        var bNode = ProceduralModels3D.CreateCityBuildingNode(isCenter, ux * 37 + uy * 101 + city.Id, city.GetDistrictType(ux, uy));
        bNode.Position = GridToWorld(ux, uy);
        _structuresRoot.AddChild(bNode);
        _urbanTileNodes[(ux, uy)] = bNode;
    }

    private void SpawnGarageMesh(Garage garage)
    {
        var gNode = ProceduralModels3D.CreateGarageNode(garage);
        gNode.Position = GridToWorld(garage.GridX, garage.GridY);
        _structuresRoot.AddChild(gNode);
        _garageNodes[garage.Id] = gNode;
    }

    private void SpawnLogisticsHubMesh(LogisticsHub hub)
    {
        var hNode = ProceduralModels3D.CreateLogisticsHubNode(hub, out var roofFan);
        hNode.Position = GridToWorld(hub.GridX, hub.GridY);
        _structuresRoot.AddChild(hNode);
        _hubNodes[hub.Id] = hNode;
        if (roofFan != null)
            _animatedRoofFans.Add(roofFan);
    }

    private void SpawnBusTerminalMesh(BusTerminal terminal)
    {
        var btNode = ProceduralModels3D.CreateBusTerminalNode(terminal);
        btNode.Position = GridToWorld(terminal.GridX, terminal.GridY);
        _structuresRoot.AddChild(btNode);
        _busTerminalNodes[terminal.Id] = btNode;
    }

    private void SpawnTrafficControlMesh(RoadNode node)
    {
        var tcNode = ProceduralModels3D.CreateTrafficControlNode(node);
        tcNode.Position = GridToWorld(node.GridX, node.GridY);
        _structuresRoot.AddChild(tcNode);
        _trafficControlNodes[(node.GridX, node.GridY)] = tcNode;
    }

    private void SpawnTollStationMesh(TollStation station)
    {
        var tNode = ProceduralModels3D.CreateTollStationNode(station, out var leftGate, out var rightGate, out var weighBoard);
        Vector3 worldPos = GridToWorld(station.GridX, station.GridY);
        tNode.Position = worldPos;
        _structuresRoot.AddChild(tNode);
        _tollNodes[station.Id] = tNode;
        _tollWeighLabels[station.Id] = weighBoard;
        _animatedTollGates.Add((leftGate, rightGate, worldPos));
    }

    private void SpawnSeaportMesh(SeaportTerminal seaport)
    {
        if (_seaportVisuals.TryGetValue(seaport.Id, out var oldVis) && IsInstanceValid(oldVis.Root))
            oldVis.Root.QueueFree();

        var sNode = ProceduralModels3D.CreateSeaportNode(seaport, out var trolleys, out var shipNode, out var statusLabel);
        sNode.Position = GridToWorld(seaport.GridX, seaport.GridY);
        _structuresRoot.AddChild(sNode);
        _seaportVisuals[seaport.Id] = (sNode, trolleys, shipNode, statusLabel);

        _seaportSeaRoutes.Remove(seaport.Id);
        if (seaport.PartnerSeaportId.HasValue)
        {
            var partner = _bridge.World.Seaports.FirstOrDefault(p => p.Id == seaport.PartnerSeaportId.Value);
            if (partner != null)
            {
                var route = ComputeOceanSeaRoute(seaport.WaterDockX, seaport.WaterDockY, partner.WaterDockX, partner.WaterDockY);
                if (route.Count >= 2)
                    _seaportSeaRoutes[seaport.Id] = route;
            }
        }
    }

    private List<Vector3> ComputeOceanSeaRoute(int startWx, int startWy, int goalWx, int goalWy)
    {
        var result = new List<Vector3>();
        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        if (startWx < 0 || startWx >= w || startWy < 0 || startWy >= h) return result;
        if (goalWx < 0 || goalWx >= w || goalWy < 0 || goalWy >= h) return result;
        if (grid[startWx, startWy].Terrain != TerrainType.Water || grid[goalWx, goalWy].Terrain != TerrainType.Water)
            return result;

        var start = (startWx, startWy);
        var goal = (goalWx, goalWy);
        var queue = new Queue<(int X, int Y)>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var visited = new HashSet<(int X, int Y)> { start };
        queue.Enqueue(start);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        bool found = false;

        while (queue.Count > 0)
        {
            var curr = queue.Dequeue();
            if (curr == goal)
            {
                found = true;
                break;
            }

            for (int d = 0; d < 4; d++)
            {
                int nx = curr.X + dx[d];
                int ny = curr.Y + dy[d];
                var next = (nx, ny);
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || visited.Contains(next))
                    continue;
                if (grid[nx, ny].Terrain != TerrainType.Water)
                    continue;

                visited.Add(next);
                cameFrom[next] = curr;
                queue.Enqueue(next);
            }
        }

        if (!found)
            return result;

        var cells = new List<(int X, int Y)>();
        var trace = goal;
        cells.Add(trace);
        while (trace != start && cameFrom.TryGetValue(trace, out var parent))
        {
            trace = parent;
            cells.Add(trace);
        }
        cells.Reverse();

        foreach (var (cx, cy) in cells)
        {
            Vector3 wp = GridToWorld(cx, cy);
            wp.Y = -0.02f;
            result.Add(wp);
        }
        return result;
    }

    private void SpawnLandmarkMesh(RegionalLandmark landmark)
    {
        if (_landmarkVisuals.TryGetValue(landmark.Id, out var oldVis) && IsInstanceValid(oldVis.Root))
            oldVis.Root.QueueFree();

        var lNode = ProceduralModels3D.CreateLandmarkNode(landmark, out var rotors);
        lNode.Position = GridToWorld(landmark.GridX, landmark.GridY);
        _structuresRoot.AddChild(lNode);
        _landmarkVisuals[landmark.Id] = (lNode, rotors);
    }

    private void SpawnConstructionBurst(Vector3 worldPos)
    {
        var burst = ProceduralModels3D.CreateConstructionBurstParticles(worldPos);
        _structuresRoot.AddChild(burst);
        _transientParticles.Add((burst, 0f));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_inMainMenu)
        {
            _cameraYaw += dt * 0.08f;
            UpdateCameraTransform();
            UpdateDayNightAndWeatherLighting(dt);
            return;
        }

        HandleCameraKeyboardPan(dt);
        UpdateFloatingPopups(dt);

        if (_bridge.TimeScale > 0.001f)
        {
            _bridge.UpdateSimulation(dt);
            _mechanicalAnimTime += dt * _bridge.TimeScale;

            Vector3 camPos = _camera.GlobalPosition;

            foreach (var rotor in _animatedWindmills)
            {
                if (IsInstanceValid(rotor) && rotor.GlobalPosition.DistanceSquaredTo(camPos) <= IndustryAnimLodMaxDistSq)
                    rotor.RotateZ(dt * 1.4f * _bridge.TimeScale);
            }

            foreach (var pump in _animatedPumpjacks)
            {
                if (IsInstanceValid(pump) && pump.GlobalPosition.DistanceSquaredTo(camPos) <= IndustryAnimLodMaxDistSq)
                {
                    float pitch = MathF.Sin(_mechanicalAnimTime * 2.8f + pump.GlobalPosition.X) * 0.28f;
                    pump.Rotation = new Vector3(pitch, 0f, 0f);
                }
            }

            foreach (var fan in _animatedRoofFans)
            {
                if (IsInstanceValid(fan) && fan.GlobalPosition.DistanceSquaredTo(camPos) <= IndustryAnimLodMaxDistSq)
                    fan.RotateY(dt * 4.2f * _bridge.TimeScale);
            }

            foreach (var seaport in _bridge.World.Seaports)
            {
                if (!_seaportVisuals.TryGetValue(seaport.Id, out var sVis) || !IsInstanceValid(sVis.Root))
                    continue;

                float bobY = -0.02f + MathF.Sin(_mechanicalAnimTime * 1.6f + seaport.Id) * 0.025f;
                if (IsInstanceValid(sVis.ShipNode))
                {
                    if (seaport.IsShipDocked)
                    {
                        var shipPos = sVis.ShipNode.Position;
                        shipPos = shipPos.Lerp(new Vector3(0f, bobY, 1.48f), Mathf.Clamp(dt * 3.0f, 0f, 1f));
                        shipPos.Y = bobY;
                        sVis.ShipNode.Position = shipPos;
                        sVis.ShipNode.Rotation = Vector3.Zero;
                        sVis.ShipNode.Visible = true;
                    }
                    else if (_seaportSeaRoutes.TryGetValue(seaport.Id, out var seaRoute) && seaRoute.Count >= 2 && sVis.ShipNode.GetParent() is Node3D shipParent)
                    {
                        // Navega fisicamente pelo oceano entre este porto e o porto do continente parceiro
                        float u = 1f - Math.Clamp(seaport.CycleHoursRemaining / SeaportTerminal.SeaVoyageDurationHours, 0f, 1f);
                        bool returning = u > 0.5f;
                        float legT = returning ? (1f - u) * 2f : u * 2f;
                        float fIdx = legT * (seaRoute.Count - 1);
                        int i0 = Math.Clamp((int)MathF.Floor(fIdx), 0, seaRoute.Count - 2);
                        int i1 = i0 + 1;
                        float segT = fIdx - i0;

                        Vector3 worldPos = seaRoute[i0].Lerp(seaRoute[i1], segT);
                        worldPos.Y = bobY;
                        Vector3 dir = returning ? (seaRoute[i0] - seaRoute[i1]) : (seaRoute[i1] - seaRoute[i0]);
                        dir.Y = 0f;

                        sVis.ShipNode.Position = shipParent.ToLocal(worldPos);
                        if (dir.LengthSquared() > 0.0001f)
                        {
                            // Proa do modelo está em +X local; converte direção mundo -> yaw local
                            Vector3 localDir = shipParent.GlobalBasis.Inverse() * dir.Normalized();
                            sVis.ShipNode.Rotation = new Vector3(0f, MathF.Atan2(-localDir.Z, localDir.X), 0f);
                        }
                        sVis.ShipNode.Visible = true;
                    }
                    else
                    {
                        var shipPos = sVis.ShipNode.Position;
                        shipPos.Z = Mathf.Lerp(shipPos.Z, 5.40f, Mathf.Clamp(dt * 2.4f, 0f, 1f));
                        shipPos.Y = bobY;
                        sVis.ShipNode.Position = shipPos;
                        sVis.ShipNode.Visible = shipPos.Z < 4.95f;
                    }
                }

                if (seaport.IsShipDocked && sVis.Root.GlobalPosition.DistanceSquaredTo(camPos) <= IndustryAnimLodMaxDistSq)
                {
                    for (int i = 0; i < sVis.Trolleys.Count; i++)
                    {
                        var tr = sVis.Trolleys[i];
                        if (!IsInstanceValid(tr)) continue;
                        var tp = tr.Position;
                        tp.Z = 0.85f + MathF.Sin(_mechanicalAnimTime * 1.4f + i * 2.1f) * 0.42f;
                        tr.Position = tp;
                    }
                }
            }

            foreach (var lVis in _landmarkVisuals.Values)
            {
                if (!IsInstanceValid(lVis.Root) || lVis.Root.GlobalPosition.DistanceSquaredTo(camPos) > IndustryAnimLodMaxDistSq)
                    continue;
                foreach (var rotor in lVis.Rotors)
                {
                    if (IsInstanceValid(rotor))
                        rotor.RotateZ(dt * 1.9f * _bridge.TimeScale);
                }
            }

            foreach (var cItem in _conveyorItems)
            {
                if (!IsInstanceValid(cItem.CargoNode)) continue;
                if (cItem.CargoNode.GlobalPosition.DistanceSquaredTo(camPos) > DetailLodMaxDistSq) continue;
                cItem.Phase += dt * cItem.Speed * _bridge.TimeScale;
                if (cItem.Phase >= 1.0f) cItem.Phase -= MathF.Floor(cItem.Phase);
                cItem.CargoNode.Position = cItem.StartLocal.Lerp(cItem.EndLocal, cItem.Phase);
            }

            UpdateDayNightAndWeatherLighting(dt);
            SyncFleet3DTransforms(dt);
            SyncRailAndTrains3D(dt);
            SyncTraffic3DTransforms(dt);
            UpdateTollGatesAnimation(dt);
        }
        else if (_photoModeActive)
        {
            UpdateDayNightAndWeatherLighting(dt);
        }

        UpdateDroneTour(dt);
        UpdateSpatialAudioAndHorns(dt);
        UpdateTransientParticles(dt);
        UpdateShowcaseTurntable(dt);
        UpdateSelectedTruckRouteAndRing(dt);

        if (_followedTruckIndex >= 0 && _followedTruckIndex < _bridge.Engine.ActiveTrucks.Count)
        {
            var followedTruck = _bridge.Engine.ActiveTrucks[_followedTruckIndex];
            if (_truckVisuals.TryGetValue(followedTruck.Id, out var handle) && handle.Root.Visible)
            {
                _cameraPivot.Position = _cameraPivot.Position.Lerp(handle.Root.GlobalPosition, dt * 8.0f);
            }
        }

        _uiRefreshTimer += dt;
        if (_uiRefreshTimer >= 0.30f)
        {
            _uiRefreshTimer = 0f;
            RefreshHud();
            if (_overlayMode is MapOverlayMode.Traffic or MapOverlayMode.Profitability)
            {
                RefreshHeatmapOverlay();
                _minimapStaticDirty = true;
            }
        }

        UpdatePipCamera(dt);
        _audio?.UpdateEnvironmentState(_bridge.Engine.CurrentWeather, _bridge.TimeScale);
        _minimapUpdateTimer += dt;
        if (_minimapUpdateTimer >= 0.10f)
        {
            _minimapUpdateTimer = 0f;
            RefreshMinimapOverlay();
        }
    }

    private void RefreshMinimapOverlay()
    {
        if (_minimapOverlay == null || !_minimapOverlay.Visible) return;

        float ts = ProceduralModels3D.TileSize;
        Vector2 camGrid = new(_cameraPivot.Position.X / ts, _cameraPivot.Position.Z / ts);

        _minimapTruckScratch.Clear();
        for (int i = 0; i < _bridge.Engine.ActiveTrucks.Count; i++)
        {
            var truck = _bridge.Engine.ActiveTrucks[i];
            if (_truckVisuals.TryGetValue(truck.Id, out var vis))
            {
                Vector3 p = vis.Root.Position;
                _minimapTruckScratch.Add((new Vector2(p.X / ts, p.Z / ts), truck.HasManualSchedule));
            }
        }

        _minimapTrainScratch.Clear();
        for (int i = 0; i < _bridge.Engine.ActiveTrains.Count; i++)
        {
            var train = _bridge.Engine.ActiveTrains[i];
            if (_trainVisuals.TryGetValue(train.Id, out var tVis))
            {
                Vector3 tp = tVis.Root.Position;
                _minimapTrainScratch.Add(new Vector2(tp.X / ts, tp.Z / ts));
            }
        }

        _minimapOverlay.UpdateDynamicMarkers(camGrid, _cameraDistance, _minimapTruckScratch, _minimapTrainScratch);
    }

    private void HandleCameraKeyboardPan(float dt)
    {
        Vector3 inputDir = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) inputDir.Z -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) inputDir.Z += 1f;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) inputDir.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) inputDir.X += 1f;

        if (inputDir.LengthSquared() > 0.001f)
        {
            _followedTruckIndex = -1;
            if (_droneTourActive) SetDroneTourActive(false);
            inputDir = inputDir.Normalized();
            Vector3 forward = new Vector3(-Mathf.Sin(_cameraYaw), 0, -Mathf.Cos(_cameraYaw));
            Vector3 right = new Vector3(Mathf.Cos(_cameraYaw), 0, -Mathf.Sin(_cameraYaw));
            float panSpeed = _cameraDistance * 0.85f;
            _cameraPivot.Position += (right * inputDir.X - forward * inputDir.Z) * panSpeed * dt;
            ClampCameraPivot();
        }
    }

    private void ClampCameraPivot()
    {
        float maxX = _bridge.World.Grid.GetLength(0) * ProceduralModels3D.TileSize;
        float maxZ = _bridge.World.Grid.GetLength(1) * ProceduralModels3D.TileSize;
        _cameraPivot.Position = new Vector3(
            Mathf.Clamp(_cameraPivot.Position.X, 0f, maxX),
            0f,
            Mathf.Clamp(_cameraPivot.Position.Z, 0f, maxZ)
        );
    }

    private void UpdateCameraTransform()
    {
        _cameraPitch = Mathf.Clamp(_cameraPitch, 0.25f, 1.40f);
        _cameraDistance = Mathf.Clamp(_cameraDistance, 7.0f, 175.0f);

        float horiz = _cameraDistance * Mathf.Cos(_cameraPitch);
        float vert = _cameraDistance * Mathf.Sin(_cameraPitch);
        Vector3 offset = new Vector3(
            horiz * Mathf.Sin(_cameraYaw),
            vert,
            horiz * Mathf.Cos(_cameraYaw)
        );

        _camera.Position = offset;
        _camera.LookAt(_cameraPivot.GlobalPosition, Vector3.Up);

        ApplyZoomPerformanceLod();
    }

    private void ApplyZoomPerformanceLod(bool force = false)
    {
        if (_worldEnv == null || _sunLight == null)
            return;

        int tier;
        if (_photoModeActive)
        {
            tier = 0;
        }
        else if (_cameraDistance <= 56f)
        {
            tier = 0; // Visão aproximada: qualidade total escolhida pelo usuário
        }
        else if (_cameraDistance <= 92f)
        {
            tier = 1; // Zoom médio-alto: remove sombras da vegetação, reduz cascatas e desliga DOF acima de 72m
        }
        else if (_cameraDistance <= 122f)
        {
            tier = 2; // Zoom distante: desliga SSAO/SSR/DOF e reduz distância de sombra
        }
        else
        {
            tier = 3; // Visão estratégica total do mapa: desliga sombras direcionais e rótulos industriais secundários
        }

        bool suppressDof = !_photoModeActive && _cameraDistance > 72f;
        if (_cameraAttributes != null)
        {
            bool toyTilt = _photoModeActive && _photoFilterMode == PhotoFilterMode.ToyModelTiltShift;
            _cameraAttributes.DofBlurFarEnabled = (!suppressDof && _tiltShiftEnabled) || toyTilt;
            _cameraAttributes.DofBlurFarDistance = toyTilt ? _cameraDistance * 1.18f : _cameraDistance * 1.42f;
            _cameraAttributes.DofBlurFarTransition = toyTilt ? _cameraDistance * 0.42f : _cameraDistance * 0.85f;
            if (toyTilt)
            {
                _cameraAttributes.DofBlurAmount = 0.12f;
            }
        }

        if (!force && tier == _lastZoomLodTier)
            return;
        _lastZoomLodTier = tier;

        bool vegShadows = tier == 0 && _graphicsPreset != GraphicsQualityPreset.Performance;
        var vegShadowMode = vegShadows
            ? GeometryInstance3D.ShadowCastingSetting.On
            : GeometryInstance3D.ShadowCastingSetting.Off;
        for (int i = 0; i < _vegShadowCasters.Count; i++)
        {
            var mm = _vegShadowCasters[i];
            if (IsInstanceValid(mm))
                mm.CastShadow = vegShadowMode;
        }

        bool showIndLabels = tier < 3;
        foreach (var lbl in _industryLabels.Values)
        {
            if (IsInstanceValid(lbl))
                lbl.Visible = showIndLabels;
        }

        if (tier == 0)
        {
            _worldEnv.SsaoEnabled = _userSsaoEnabled;
            _worldEnv.SsrEnabled = _userSsrEnabled;
            _sunLight.ShadowEnabled = true;
            _sunLight.DirectionalShadowMode = _userShadows4Splits
                ? DirectionalLight3D.ShadowMode.Parallel4Splits
                : DirectionalLight3D.ShadowMode.Parallel2Splits;
            _sunLight.DirectionalShadowMaxDistance = _userShadowMaxDistance;
        }
        else if (tier == 1)
        {
            _worldEnv.SsaoEnabled = _userSsaoEnabled;
            _worldEnv.SsrEnabled = false;
            _sunLight.ShadowEnabled = true;
            _sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
            _sunLight.DirectionalShadowMaxDistance = Mathf.Min(_userShadowMaxDistance, 95.0f);
        }
        else if (tier == 2)
        {
            _worldEnv.SsaoEnabled = false;
            _worldEnv.SsrEnabled = false;
            _sunLight.ShadowEnabled = true;
            _sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
            _sunLight.DirectionalShadowMaxDistance = Mathf.Min(_userShadowMaxDistance, 85.0f);
        }
        else
        {
            _worldEnv.SsaoEnabled = false;
            _worldEnv.SsrEnabled = false;
            _sunLight.ShadowEnabled = false;
        }
    }

    private void ApplyGraphicsPreset(GraphicsQualityPreset preset, bool logMessage = true, bool persist = true)
    {
        _graphicsPreset = preset;

        switch (preset)
        {
            case GraphicsQualityPreset.Performance:
                _userSsaoEnabled = false;
                _worldEnv.GlowEnabled = false;
                _userSsrEnabled = false;
                _tiltShiftEnabled = false;
                _userShadows4Splits = false;
                _userShadowMaxDistance = 80.0f;
                _sunLight.ShadowBlur = 0.8f;
                GetViewport().Msaa3D = Viewport.Msaa.Msaa2X;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;

            case GraphicsQualityPreset.Balanced:
                _userSsaoEnabled = true;
                _worldEnv.SsaoRadius = 0.64f;
                _worldEnv.SsaoIntensity = 1.25f;
                _worldEnv.SsaoDetail = 0.45f;
                _worldEnv.GlowEnabled = true;
                _worldEnv.GlowIntensity = 0.48f;
                _userSsrEnabled = false;
                _tiltShiftEnabled = true;
                _cameraAttributes.DofBlurAmount = 0.040f;
                _userShadows4Splits = false;
                _userShadowMaxDistance = 92.0f;
                _sunLight.ShadowBlur = 0.95f;
                // Evita conflito MSAA + SSAO no Forward+ (que zera a iluminação do terreno em GPUs AMD/Vulkan)
                GetViewport().Msaa3D = Viewport.Msaa.Disabled;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;

            case GraphicsQualityPreset.DioramaUltra:
                _userSsaoEnabled = true;
                _worldEnv.SsaoRadius = 0.72f;
                _worldEnv.SsaoIntensity = 1.65f;
                _worldEnv.SsaoDetail = 0.55f;
                _worldEnv.GlowEnabled = true;
                _worldEnv.GlowIntensity = 0.62f;
                _userSsrEnabled = true;
                _tiltShiftEnabled = true;
                _cameraAttributes.DofBlurAmount = 0.055f;
                _userShadows4Splits = true;
                _userShadowMaxDistance = 145.0f;
                _sunLight.ShadowBlur = 1.35f;
                // Evita conflito MSAA + SSAO no Forward+ (que zera a iluminação do terreno em GPUs AMD/Vulkan)
                GetViewport().Msaa3D = Viewport.Msaa.Disabled;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;
        }

        _worldEnv.SsaoEnabled = _userSsaoEnabled;
        _worldEnv.SsrEnabled = _userSsrEnabled;
        _sunLight.DirectionalShadowMode = _userShadows4Splits
            ? DirectionalLight3D.ShadowMode.Parallel4Splits
            : DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sunLight.DirectionalShadowMaxDistance = _userShadowMaxDistance;

        ApplyZoomPerformanceLod(force: true);
        UpdateCameraTransform();
        RefreshGraphicsModalUi();
        if (persist)
            PersistClientSettings();

        if (logMessage)
        {
            string label = preset switch
            {
                GraphicsQualityPreset.Performance => "⚡ Desempenho",
                GraphicsQualityPreset.Balanced => "⚖️ Equilibrado",
                _ => "✨ Diorama Ultra"
            };
            LogEvent($"🎨 Preset gráfico aplicado: {label}!");
        }
    }

    private void UpdateDayNightAndWeatherLighting(float dt)
    {
        float hour = (_photoModeActive && _photoLockHour)
            ? _photoHourValue
            : (_bridge.Engine.Calendar.Hour + (_bridge.Engine.Calendar.Minute / 60f));
        bool isNight = hour < 6.0f || hour >= 19.0f;
        var weather = (_photoModeActive && _photoWeatherOverride > 0)
            ? _photoWeatherOverride switch
            {
                1 => WeatherCondition.Clear,
                2 => WeatherCondition.Rain,
                3 => WeatherCondition.HeavyStorm,
                _ => WeatherCondition.Clear
            }
            : _bridge.Engine.CurrentWeather;
        bool isRaining = weather != WeatherCondition.Clear;
        bool isBlizzard = (_photoModeActive && _photoWeatherOverride == 4) ||
            (!(_photoModeActive && _photoWeatherOverride > 0) && _bridge.Engine.ActiveSeasonalEvent == SeasonalEventType.WinterBlizzard);

        // 1. Ciclo Solar Dinâmico 24h (Elevação, Azimute e Paletas de Cor do Diorama)
        Color targetSunColor;
        Color targetSkyTop;
        Color targetSkyHorizon;
        Color targetFogColor;
        float baseSunEnergy;
        float baseSkyEnergy;
        Vector3 targetSunRot;

        if (!_dynamicSolarCycleEnabled)
        {
            targetSunColor = isNight ? new Color("93c5fd") : new Color("fff8eb");
            targetSkyTop = isNight ? new Color("1e293b") : new Color("7ab3dc");
            targetSkyHorizon = isNight ? new Color("334155") : new Color("d9e6f2");
            targetFogColor = targetSkyHorizon;
            baseSunEnergy = isNight ? 0.20f : 0.84f;
            baseSkyEnergy = isNight ? 0.16f : 0.72f;
            targetSunRot = new Vector3(-50f, 38f, 0f);
        }
        else if (hour >= 5.5f && hour < 8.0f)
        {
            // 🌅 Amanhecer Dourado (05:30 -> 08:00)
            float t = (hour - 5.5f) / 2.5f;
            targetSunColor = new Color("ffb36b").Lerp(new Color("fff8eb"), t);
            targetSkyTop = new Color("3b6998").Lerp(new Color("7ab3dc"), t);
            targetSkyHorizon = new Color("f5c09c").Lerp(new Color("d9e6f2"), t);
            targetFogColor = new Color("ebd2be").Lerp(new Color("d9e6f2"), t);
            baseSunEnergy = Mathf.Lerp(0.45f, 0.84f, t);
            baseSkyEnergy = Mathf.Lerp(0.38f, 0.72f, t);
            targetSunRot = new Vector3(Mathf.Lerp(-26f, -52f, t), Mathf.Lerp(62f, 42f, t), 0f);
        }
        else if (hour >= 8.0f && hour < 16.5f)
        {
            // ☀️ Meio-Dia Solar Voxel Tycoon (08:00 -> 16:30)
            float t = (hour - 8.0f) / 8.5f;
            float zenith = 1.0f - MathF.Abs(t * 2f - 1f);
            targetSunColor = new Color("fff8eb");
            targetSkyTop = new Color("7ab3dc");
            targetSkyHorizon = new Color("d9e6f2");
            targetFogColor = new Color("d9e6f2");
            baseSunEnergy = 0.84f + zenith * 0.06f;
            baseSkyEnergy = 0.72f + zenith * 0.04f;
            targetSunRot = new Vector3(Mathf.Lerp(-52f, -58f, zenith), Mathf.Lerp(42f, 16f, t), 0f);
        }
        else if (hour >= 16.5f && hour < 19.25f)
        {
            // 🌇 Pôr do Sol Âmbar (16:30 -> 19:15)
            float t = (hour - 16.5f) / 2.75f;
            targetSunColor = new Color("fff8eb").Lerp(new Color("ff8c42"), t);
            targetSkyTop = new Color("7ab3dc").Lerp(new Color("284366"), t);
            targetSkyHorizon = new Color("d9e6f2").Lerp(new Color("eb946b"), t);
            targetFogColor = new Color("d9e6f2").Lerp(new Color("d9a796"), t);
            baseSunEnergy = Mathf.Lerp(0.82f, 0.32f, t);
            baseSkyEnergy = Mathf.Lerp(0.70f, 0.25f, t);
            targetSunRot = new Vector3(Mathf.Lerp(-48f, -24f, t), Mathf.Lerp(16f, -38f, t), 0f);
        }
        else
        {
            // 🌙 Noite Lunar (19:15 -> 05:30)
            targetSunColor = new Color("93c5fd");
            targetSkyTop = new Color("0f172a");
            targetSkyHorizon = new Color("1e293b");
            targetFogColor = new Color("172236");
            baseSunEnergy = 0.22f;
            baseSkyEnergy = 0.16f;
            targetSunRot = new Vector3(-42f, -28f, 0f);
        }

        // 2. Modulação Atmosférica por Clima (Chuva, Tempestade ou Nevasca)
        if (isRaining || isBlizzard)
        {
            float stormFactor = weather == WeatherCondition.HeavyStorm ? 0.65f : 0.42f;
            Color overcastHorizon = isBlizzard ? new Color("cbd5e1") : new Color("8496a8");
            Color overcastTop = isBlizzard ? new Color("94a3b8") : new Color("526375");
            targetSkyTop = targetSkyTop.Lerp(overcastTop, stormFactor);
            targetSkyHorizon = targetSkyHorizon.Lerp(overcastHorizon, stormFactor);
            targetFogColor = targetFogColor.Lerp(overcastHorizon, stormFactor);
            baseSunEnergy *= weather == WeatherCondition.HeavyStorm ? 0.56f : 0.76f;
            baseSkyEnergy *= weather == WeatherCondition.HeavyStorm ? 0.62f : 0.80f;
        }

        if (_photoModeActive && _photoFilterMode == PhotoFilterMode.Retro90s)
        {
            targetSunColor = targetSunColor.Lerp(new Color("fde68a"), 0.35f);
            targetFogColor = targetFogColor.Lerp(new Color("d6c7b2"), 0.42f);
        }
        else if (_photoModeActive && _photoFilterMode == PhotoFilterMode.WarmSunset)
        {
            targetSunColor = targetSunColor.Lerp(new Color("fb923c"), 0.52f);
            targetSkyHorizon = targetSkyHorizon.Lerp(new Color("fdba74"), 0.45f);
            targetFogColor = targetFogColor.Lerp(new Color("fDBA74"), 0.48f);
        }

        float lerpRate = Mathf.Clamp(dt * 3.2f, 0f, 1f);
        _sunLight.LightColor = _sunLight.LightColor.Lerp(targetSunColor, lerpRate);
        _sunLight.LightEnergy = Mathf.Lerp(_sunLight.LightEnergy, baseSunEnergy, lerpRate);
        _sunLight.RotationDegrees = _sunLight.RotationDegrees.Lerp(targetSunRot, lerpRate);

        _skyMat.SkyTopColor = _skyMat.SkyTopColor.Lerp(targetSkyTop, lerpRate);
        _skyMat.SkyHorizonColor = _skyMat.SkyHorizonColor.Lerp(targetSkyHorizon, lerpRate);
        _skyMat.GroundHorizonColor = _skyMat.GroundHorizonColor.Lerp(targetSkyHorizon, lerpRate);
        _skyMat.SkyEnergyMultiplier = Mathf.Lerp(_skyMat.SkyEnergyMultiplier, baseSkyEnergy, lerpRate);

        _worldEnv.AmbientLightColor = _worldEnv.AmbientLightColor.Lerp(targetSkyHorizon, lerpRate);
        _worldEnv.FogLightColor = _worldEnv.FogLightColor.Lerp(targetFogColor, lerpRate);
        float targetFogDensity = weather == WeatherCondition.HeavyStorm ? 0.0085f : (isRaining || isBlizzard ? 0.0062f : 0.0044f);
        _worldEnv.FogDensity = Mathf.Lerp(_worldEnv.FogDensity, targetFogDensity, lerpRate);

        _rainParticles.Emitting = isRaining && !isBlizzard;
        _snowParticles.Emitting = isBlizzard;

        float targetWetness = weather == WeatherCondition.HeavyStorm ? 1.0f : (isRaining ? 0.75f : 0.0f);
        _currentRoadWetness = Mathf.MoveToward(_currentRoadWetness, targetWetness, dt * 0.25f);
        ProceduralModels3D.UpdateGlobalRoadWetness(_currentRoadWetness);

        float targetSnowCover = isBlizzard ? 1.0f : 0.0f;
        _currentRoadSnowCover = Mathf.MoveToward(_currentRoadSnowCover, targetSnowCover, dt * 0.35f);
        ProceduralModels3D.UpdateGlobalRoadSnowCover(_currentRoadSnowCover);

        bool lightsOn = isNight || isRaining || isBlizzard || (hour >= 18.0f || hour < 6.5f);
        if (_lastVehicleLightsOn != lightsOn)
        {
            _lastVehicleLightsOn = lightsOn;
            ProceduralModels3D.UpdateNightBuildingLights(lightsOn);
            foreach (var kv in _truckVisuals)
            {
                kv.Value.Headlights.Visible = lightsOn;
            }
            foreach (var kv in _trafficVisuals)
            {
                kv.Value.Headlights.Visible = lightsOn;
            }
        }
    }

    private void SyncFleet3DTransforms(float dt)
    {
        Vector3 camPos = _camera.GlobalPosition;
        var activeIds = _activeTruckIdsScratch;
        activeIds.Clear();

        foreach (var truck in _bridge.Engine.ActiveTrucks)
        {
            activeIds.Add(truck.Id);
            FleetLivery effectiveLivery = _bridge.GetEffectiveLivery(truck);

            if (!_truckVisuals.TryGetValue(truck.Id, out var handle))
            {
                handle = ProceduralModels3D.CreateTruckMeshNode(truck, effectiveLivery);
                handle.Headlights.Visible = _lastVehicleLightsOn ?? false;
                _trucksRoot.AddChild(handle.Root);
                _truckVisuals[truck.Id] = handle;
            }
            else if (handle.DisplayedRank != truck.Rank
                || handle.DisplayedFinish != effectiveLivery.Finish
                || !string.Equals(handle.DisplayedPrimaryHex, effectiveLivery.PrimaryColorHex, StringComparison.Ordinal)
                || !string.Equals(handle.DisplayedStripeHex, effectiveLivery.StripeColorHex, StringComparison.Ordinal))
            {
                Vector3 prevPos = handle.Root.GlobalPosition;
                Vector3 prevDir = handle.LastForwardDir;
                float prevSteer = handle.CurrentSteerYaw;
                float prevTrailer = handle.CurrentTrailerYaw;
                handle.Root.QueueFree();

                handle = ProceduralModels3D.CreateTruckMeshNode(truck, effectiveLivery);
                handle.Headlights.Visible = _lastVehicleLightsOn ?? false;
                handle.LastForwardDir = prevDir;
                handle.CurrentSteerYaw = prevSteer;
                handle.CurrentTrailerYaw = prevTrailer;
                _trucksRoot.AddChild(handle.Root);
                handle.Root.GlobalPosition = prevPos;
                _truckVisuals[truck.Id] = handle;
            }

            if (truck.CurrentEdge == null)
            {
                handle.Root.Visible = false;
                if (handle.MaintenanceSmoke != null && handle.MaintenanceSmoke.Emitting)
                    handle.MaintenanceSmoke.Emitting = false;
                if (handle.TireDust != null && handle.TireDust.Emitting)
                    handle.TireDust.Emitting = false;
                if (handle.RainSpray != null && handle.RainSpray.Emitting)
                    handle.RainSpray.Emitting = false;
                continue;
            }

            handle.Root.Visible = true;
            ProceduralModels3D.UpdateTruckCargoVisuals(handle, truck);

            if (handle.EscortLead != null)
            {
                handle.EscortLead.Visible = truck.IsSpecialConvoyActive;
                if (truck.IsSpecialConvoyActive)
                {
                    bool flashA = ((int)(_mechanicalAnimTime * 9f) % 2) == 0;
                    if (handle.EscortRedBeacon != null) handle.EscortRedBeacon.Visible = flashA;
                    if (handle.EscortAmberBeacon != null) handle.EscortAmberBeacon.Visible = !flashA;
                }
            }

            var edge = truck.CurrentEdge;
            float progressRatio = Mathf.Clamp(truck.DistanceAlongCurrentEdgeKm / Mathf.Max(0.01f, edge.DistanceKm), 0f, 1f);

            (Vector3 pos, Vector3 lookTarget) = EvaluateOrthogonalRoadPath(
                edge.FromNode.GridX, edge.FromNode.GridY,
                edge.ToNode.GridX, edge.ToNode.GridY,
                progressRatio
            );

            handle.Root.GlobalPosition = pos + new Vector3(0, 0.06f, 0);
            float targetSteerYaw = 0f;
            if (pos.DistanceSquaredTo(lookTarget) > 0.0001f)
            {
                Vector3 nextForward = (lookTarget - pos).Normalized();
                float crossY = handle.LastForwardDir.Cross(nextForward).Y;
                targetSteerYaw = Mathf.Clamp(crossY * 1.8f, -0.34f, 0.34f);
                handle.LastForwardDir = handle.LastForwardDir.Lerp(nextForward, Mathf.Clamp(dt * 8.0f, 0f, 1f)).Normalized();
                handle.Root.LookAt(lookTarget + new Vector3(0, 0.06f, 0), Vector3.Up);
            }

            bool nearDetailLod = camPos.DistanceSquaredTo(handle.Root.GlobalPosition) <= DetailLodMaxDistSq
                && _camera.IsPositionInFrustum(handle.Root.GlobalPosition);

            if (nearDetailLod)
            {
                handle.CurrentSteerYaw = Mathf.Lerp(handle.CurrentSteerYaw, targetSteerYaw, Mathf.Clamp(dt * 10.0f, 0f, 1f));
                foreach (var steerPivot in handle.SteerPivots)
                {
                    steerPivot.Rotation = new Vector3(0f, handle.CurrentSteerYaw, 0f);
                }

                // Articulação independente do semirreboque traseiro em curvas (Bi-Trem e Rodotrem 9 Eixos)
                if (handle.RearTrailerPivot != null)
                {
                    float targetTrailerYaw = -handle.CurrentSteerYaw * 0.90f;
                    handle.CurrentTrailerYaw = Mathf.Lerp(handle.CurrentTrailerYaw, targetTrailerYaw, Mathf.Clamp(dt * 6.5f, 0f, 1f));
                    handle.RearTrailerPivot.Rotation = new Vector3(0f, handle.CurrentTrailerYaw, 0f);
                }

                bool isMoving = _bridge.TimeScale > 0.01f && edge.CurrentSpeedLimitKmh > 1f;
                bool isRaining = _bridge.Engine.CurrentWeather != WeatherCondition.Clear;

                // Fumaça escura de alerta mecânico quando Condition < 35%
                if (handle.MaintenanceSmoke != null)
                {
                    bool wantSmoke = truck.Condition < 35f && isMoving;
                    if (handle.MaintenanceSmoke.Emitting != wantSmoke)
                        handle.MaintenanceSmoke.Emitting = wantSmoke;
                }

                // Poeira nos pneus em estradas de terra/cascalho (Dirt/Gravel) em tempo seco
                if (handle.TireDust != null)
                {
                    bool wantDust = isMoving && !isRaining && edge.Tier <= RoadTier.Gravel;
                    if (handle.TireDust.Emitting != wantDust)
                        handle.TireDust.Emitting = wantDust;
                }

                // Respingos d'água nos pneus sob chuva/tempestade
                if (handle.RainSpray != null)
                {
                    bool wantSpray = isMoving && isRaining;
                    if (handle.RainSpray.Emitting != wantSpray)
                        handle.RainSpray.Emitting = wantSpray;
                }

                if (isMoving)
                {
                    float wheelSpinSpeed = (edge.CurrentSpeedLimitKmh / 60f) * 8.0f * _bridge.TimeScale;
                    foreach (var wheel in handle.Wheels)
                    {
                        wheel.RotateX(-wheelSpinSpeed * dt);
                    }
                }
            }
            else
            {
                // LOD distante / fora do frustum: suspende partículas secundárias e rotação individual de rodas
                if (handle.MaintenanceSmoke != null && handle.MaintenanceSmoke.Emitting)
                    handle.MaintenanceSmoke.Emitting = false;
                if (handle.TireDust != null && handle.TireDust.Emitting)
                    handle.TireDust.Emitting = false;
                if (handle.RainSpray != null && handle.RainSpray.Emitting)
                    handle.RainSpray.Emitting = false;
            }
        }

        if (_truckVisuals.Count > activeIds.Count)
        {
            _staleIdsScratch.Clear();
            foreach (int existingId in _truckVisuals.Keys)
            {
                if (!activeIds.Contains(existingId))
                    _staleIdsScratch.Add(existingId);
            }
            for (int i = 0; i < _staleIdsScratch.Count; i++)
            {
                int staleId = _staleIdsScratch[i];
                if (_truckVisuals.TryGetValue(staleId, out var staleHandle) && IsInstanceValid(staleHandle.Root))
                    staleHandle.Root.QueueFree();
                _truckVisuals.Remove(staleId);
            }
        }
    }

    private void SyncTraffic3DTransforms(float dt)
    {
        var activeNpcIds = _activeNpcIdsScratch;
        activeNpcIds.Clear();
        bool hideTinyCars = _cameraDistance > 140f;

        foreach (var npc in _bridge.Engine.TrafficVehicles)
        {
            if (npc.CurrentEdge == null)
                continue;

            activeNpcIds.Add(npc.Id);
            if (!_trafficVisuals.TryGetValue(npc.Id, out var visual))
            {
                var root = ProceduralModels3D.CreateTrafficVehicleNode(npc, out var headlights);
                headlights.Visible = _lastVehicleLightsOn ?? false;
                _trucksRoot.AddChild(root);
                visual = (root, headlights, Vector3.Forward);
                _trafficVisuals[npc.Id] = visual;
            }

            if (hideTinyCars && npc.Kind == TrafficVehicleKind.CivilCar)
            {
                if (visual.Root.Visible) visual.Root.Visible = false;
                continue;
            }

            var edge = npc.CurrentEdge;
            float progress = Mathf.Clamp(npc.DistanceAlongCurrentEdgeKm / Mathf.Max(0.01f, edge.DistanceKm), 0f, 1f);
            (Vector3 pos, Vector3 lookTarget) = EvaluateOrthogonalRoadPath(
                edge.FromNode.GridX, edge.FromNode.GridY,
                edge.ToNode.GridX, edge.ToNode.GridY,
                progress);

            Vector3 worldPos = pos + new Vector3(0f, 0.06f, 0f);
            if (!_camera.IsPositionInFrustum(worldPos))
            {
                if (visual.Root.Visible) visual.Root.Visible = false;
                continue;
            }

            visual.Root.Visible = true;
            visual.Root.GlobalPosition = worldPos;
            if (pos.DistanceSquaredTo(lookTarget) > 0.0001f)
            {
                Vector3 nextForward = (lookTarget - pos).Normalized();
                visual.LastForward = visual.LastForward.Lerp(nextForward, Mathf.Clamp(dt * 8f, 0f, 1f)).Normalized();
                visual.Root.LookAt(lookTarget + new Vector3(0f, 0.06f, 0f), Vector3.Up);
                _trafficVisuals[npc.Id] = visual;
            }
        }

        if (_trafficVisuals.Count > activeNpcIds.Count)
        {
            _staleIdsScratch.Clear();
            foreach (int existingId in _trafficVisuals.Keys)
            {
                if (!activeNpcIds.Contains(existingId))
                    _staleIdsScratch.Add(existingId);
            }
            for (int i = 0; i < _staleIdsScratch.Count; i++)
            {
                int staleId = _staleIdsScratch[i];
                if (_trafficVisuals.TryGetValue(staleId, out var stale) && IsInstanceValid(stale.Root))
                    stale.Root.QueueFree();
                _trafficVisuals.Remove(staleId);
            }
        }
    }

    private void UpdateTollGatesAnimation(float dt)
    {
        for (int i = _animatedTollGates.Count - 1; i >= 0; i--)
        {
            var (leftGate, rightGate, worldPos) = _animatedTollGates[i];
            if (!IsInstanceValid(leftGate) || !IsInstanceValid(rightGate))
            {
                _animatedTollGates.RemoveAt(i);
                continue;
            }

            bool truckNear = false;
            foreach (var kv in _truckVisuals)
            {
                if (kv.Value.Root.Visible && kv.Value.Root.GlobalPosition.DistanceSquaredTo(worldPos) <= 7.5f)
                {
                    truckNear = true;
                    break;
                }
            }
            if (!truckNear)
            {
                foreach (var kv in _trafficVisuals)
                {
                    if (kv.Value.Root.Visible && kv.Value.Root.GlobalPosition.DistanceSquaredTo(worldPos) <= 7.5f)
                    {
                        truckNear = true;
                        break;
                    }
                }
            }

            float targetAngle = truckNear ? 1.32f : 0f;
            float speed = Mathf.Clamp(dt * 8.5f, 0f, 1f);
            leftGate.Rotation = new Vector3(0f, 0f, Mathf.Lerp(leftGate.Rotation.Z, targetAngle, speed));
            rightGate.Rotation = new Vector3(0f, 0f, Mathf.Lerp(rightGate.Rotation.Z, -targetAngle, speed));
        }
    }

    private void UpdateTransientParticles(float dt)
    {
        for (int i = _transientParticles.Count - 1; i >= 0; i--)
        {
            var (p, age) = _transientParticles[i];
            float nextAge = age + dt;
            if (!IsInstanceValid(p) || nextAge > 1.2f)
            {
                if (IsInstanceValid(p))
                    p.QueueFree();
                _transientParticles.RemoveAt(i);
            }
            else
            {
                _transientParticles[i] = (p, nextAge);
            }
        }
    }

    private void UpdateSelectedTruckRouteAndRing(float dt)
    {
        var trucks = _bridge.Engine.ActiveTrucks;
        bool shouldShow = (_activeTab == WindowTab.Schedule || _activeTab == WindowTab.Fleet || _followedTruckIndex >= 0 || (_pipPanel != null && _pipPanel.Visible))
                          && trucks.Count > 0;

        if (!shouldShow)
        {
            if (_selectedTruckRing != null) _selectedTruckRing.Visible = false;
            if (_selectedRouteMultiMesh != null) _selectedRouteMultiMesh.Visible = false;
            return;
        }

        int idx = Mathf.Clamp(_followedTruckIndex >= 0 ? _followedTruckIndex : _selectedTruckIndex, 0, trucks.Count - 1);
        var truck = trucks[idx];

        if (_selectedTruckRing != null)
        {
            if (_truckVisuals.TryGetValue(truck.Id, out var vis) && vis.Root.Visible)
            {
                _selectedTruckRing.Visible = true;
                _selectedTruckRing.GlobalPosition = vis.Root.GlobalPosition + new Vector3(0f, 0.03f, 0f);
                _selectedTruckRing.RotateY(dt * 2.2f);
            }
            else
            {
                _selectedTruckRing.Visible = false;
            }
        }

        if (_selectedRouteMultiMesh?.Multimesh == null)
            return;

        var mm = _selectedRouteMultiMesh.Multimesh;
        var tripEdges = truck.CurrentTripEdges;
        if (tripEdges.Count == 0 && truck.CurrentEdge == null)
        {
            mm.InstanceCount = 0;
            _selectedRouteMultiMesh.Visible = false;
            return;
        }

        const int markersPerEdge = 3;
        int totalMarkers = Math.Max(1, tripEdges.Count) * markersPerEdge;
        _routeMarkerUpdateTimer -= dt;
        bool targetChanged = _lastRouteMarkerTruckId != truck.Id || _lastRouteMarkerEdgeCount != tripEdges.Count || !_selectedRouteMultiMesh.Visible;
        if (!targetChanged && _routeMarkerUpdateTimer > 0f)
            return;

        _routeMarkerUpdateTimer = 0.08f;
        _lastRouteMarkerTruckId = truck.Id;
        _lastRouteMarkerEdgeCount = tripEdges.Count;
        mm.InstanceCount = totalMarkers;

        float pulse = 0.65f + 0.35f * MathF.Sin((float)Time.GetTicksMsec() * 0.006f);
        Color activeCol = new Color(0.14f, 0.82f, 0.98f, 0.85f * pulse);
        int mIdx = 0;

        IEnumerable<RoadEdge> edgesToDraw = tripEdges.Count > 0
            ? tripEdges
            : (truck.CurrentEdge != null ? new[] { truck.CurrentEdge } : Array.Empty<RoadEdge>());

        foreach (var edge in edgesToDraw)
        {
            for (int s = 0; s < markersPerEdge; s++)
            {
                float t = (s + 0.5f) / markersPerEdge;
                var (pos, lookTarget) = EvaluateOrthogonalRoadPath(
                    edge.FromNode.GridX, edge.FromNode.GridY,
                    edge.ToNode.GridX, edge.ToNode.GridY,
                    t
                );
                Vector3 dir = (lookTarget - pos).Normalized();
                if (dir.LengthSquared() < 0.001f) dir = Vector3.Forward;
                Vector3 right = dir.Cross(Vector3.Up).Normalized();
                if (right.LengthSquared() < 0.001f) right = Vector3.Right;
                Basis basis = new Basis(right, Vector3.Up, -dir);

                mm.SetInstanceTransform(mIdx, new Transform3D(basis, pos + new Vector3(0f, 0.11f, 0f)));
                mm.SetInstanceColor(mIdx, activeCol);
                mIdx++;
            }
        }

        _selectedRouteMultiMesh.Visible = mIdx > 0;
    }

    private (Vector3 Position, Vector3 LookTarget) EvaluateOrthogonalRoadPath(int x1, int y1, int x2, int y2, float t)
    {
        Vector3 start = GridToWorld(x1, y1);
        Vector3 end = GridToWorld(x2, y2);

        if (x1 == x2 || y1 == y2)
        {
            Vector3 p = start.Lerp(end, t);
            Vector3 ahead = start.Lerp(end, Mathf.Min(1.0f, t + 0.05f));
            if (p.IsEqualApprox(ahead)) ahead = end + (end - start).Normalized();
            return (p, ahead);
        }

        var grid = _bridge.World.Grid;
        bool corner1HasRoad = grid[x2, y1].HasRoad;
        int cx = corner1HasRoad ? x2 : x1;
        int cy = corner1HasRoad ? y1 : y2;

        Vector3 corner = GridToWorld(cx, cy);
        float len1 = start.DistanceTo(corner);
        float len2 = corner.DistanceTo(end);
        float totalLen = Mathf.Max(0.01f, len1 + len2);
        float splitT = len1 / totalLen;

        if (t <= splitT)
        {
            float localT = splitT > 0.0001f ? t / splitT : 0f;
            Vector3 p = start.Lerp(corner, localT);
            return (p, corner);
        }
        else
        {
            float localT = (1f - splitT) > 0.0001f ? (t - splitT) / (1f - splitT) : 1f;
            Vector3 p = corner.Lerp(end, localT);
            return (p, end);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_inMainMenu)
            return;

        // Garante que soltar o botão direito ou do meio sobre qualquer painel do HUD sempre encerra o movimento
        if (@event is InputEventMouseButton mb && !mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _isOrbiting = false;
            else if (mb.ButtonIndex == MouseButton.Middle)
                _isPanning = false;
        }

        // Durante o arrasto ativo, processa o movimento mesmo que o cursor passe sobre o HUD
        if (@event is InputEventMouseMotion mm)
        {
            if (_isOrbiting)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Right))
                {
                    _isOrbiting = false;
                    return;
                }

                _cameraYaw -= mm.Relative.X * 0.0065f;
                _cameraPitch += mm.Relative.Y * 0.0055f;
                UpdateCameraTransform();
                GetViewport().SetInputAsHandled();
            }
            else if (_isPanning)
            {
                if (!Input.IsMouseButtonPressed(MouseButton.Middle))
                {
                    _isPanning = false;
                    return;
                }

                _followedTruckIndex = -1;
                Vector3 forward = new Vector3(-Mathf.Sin(_cameraYaw), 0, -Mathf.Cos(_cameraYaw));
                Vector3 right = new Vector3(Mathf.Cos(_cameraYaw), 0, -Mathf.Sin(_cameraYaw));
                float factor = _cameraDistance * 0.0024f;
                // Arrasto natural estilo mapa: puxar o mouse move o terreno na mesma direção
                _cameraPivot.Position += (-right * mm.Relative.X + forward * mm.Relative.Y) * factor;
                ClampCameraPivot();
                UpdateCameraTransform();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_inMainMenu)
        {
            if (@event is InputEventKey menuKey && menuKey.Pressed && !menuKey.Echo && menuKey.Keycode == Key.Escape)
            {
                if (_graphicsModal != null && _graphicsModal.Visible)
                {
                    _graphicsModal.Visible = false;
                }
                else if (_newGameCreatorCard != null && _newGameCreatorCard.Visible)
                {
                    _newGameCreatorCard.Visible = false;
                    if (_mainMenuHomeCard != null) _mainMenuHomeCard.Visible = true;
                }
            }
            return;
        }

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _isOrbiting = mb.Pressed;
            }
            else if (mb.ButtonIndex == MouseButton.Middle)
            {
                _isPanning = mb.Pressed;
            }
            else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                _cameraDistance -= 2.6f;
                UpdateCameraTransform();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                _cameraDistance += 2.6f;
                UpdateCameraTransform();
            }
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && _isDraggingRoad)
            {
                CancelRoadDrag();
            }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                if (_activeTool == ToolMode.Inspect && TryPickTruckAtScreenPos(mb.Position, out int pickedTruckIdx))
                {
                    SelectAndFocusTruck(pickedTruckIdx);
                    return;
                }

                UpdateHoveredGridCell(mb.Position);
                if (_hoveredCell.X >= 0)
                {
                    if (_activeTool == ToolMode.BuildRoad || _activeTool == ToolMode.BuildRail)
                    {
                        _isDraggingRoad = true;
                        _roadDragStart = _hoveredCell;
                        if (_activeTool == ToolMode.BuildRail)
                            UpdateRailDragPreview();
                        else
                            UpdateRoadDragPreview();
                    }
                    else
                    {
                        bool shiftPressed = mb.ShiftPressed || _corridorBatchMode;
                        HandleGridCellClick(_hoveredCell.X, _hoveredCell.Y, shiftPressed);
                    }
                }
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed && _isDraggingRoad)
            {
                if (_activeTool == ToolMode.BuildRail)
                    CommitRailDrag();
                else
                    CommitRoadDrag();
            }
        }
        else if (@event is InputEventMouseMotion mm && !_isOrbiting && !_isPanning)
        {
            UpdateHoveredGridCell(mm.Position);
            if (_isDraggingRoad)
            {
                if (_activeTool == ToolMode.BuildRail)
                    UpdateRailDragPreview();
                else if (_activeTool == ToolMode.BuildRoad)
                    UpdateRoadDragPreview();
            }
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (GetViewport().GuiGetFocusOwner() is LineEdit)
                return;

            if (key.Keycode == Key.Escape)
            {
                if (_photoModeActive) TogglePhotoMode();
                else if (_isDraggingRoad) CancelRoadDrag();
                else if (_graphicsModal != null && _graphicsModal.Visible) _graphicsModal.Visible = false;
                else if (_savesModal != null && _savesModal.Visible) _savesModal.Visible = false;
                else SetActiveTool(ToolMode.Inspect);
            }
            else if (key.Keycode == Key.F12)
                TogglePhotoMode();
            else if (key.Keycode == Key.H)
                _audio?.PlayAirHorn(0f, 1.0f);
            else if (key.Keycode == Key.G)
                ToggleGraphicsModal();
            else if (key.Keycode == Key.C)
                CycleCameraFocus();
            else if (key.Keycode == Key.V)
                CycleFollowTruckCamera();
            else if (key.Keycode == Key.P)
                TogglePipCamera();
            else if (key.Keycode == Key.Q)
                SetActiveTool(ToolMode.Inspect);
            else if (key.Keycode == Key.B)
                SetActiveTool(ToolMode.BuildRoad);
            else if (key.Keycode == Key.R)
                SetActiveTool(ToolMode.BuildRail);
            else if (key.Keycode == Key.X)
                SetActiveTool(ToolMode.Demolish);
            else if (key.Keycode == Key.Tab)
            {
                int nextMode = ((int)_overlayMode + 1) % 4;
                SetMapOverlayMode((MapOverlayMode)nextMode);
            }
            else if (key.Keycode == Key.Space)
                SetSimulationSpeed(_bridge.TimeScale > 0.01f ? 0f : 1f);
            else if (key.Keycode == Key.Key1)
                SetSimulationSpeed(0f);
            else if (key.Keycode == Key.Key2)
                SetSimulationSpeed(1f);
            else if (key.Keycode == Key.Key3)
                SetSimulationSpeed(2.5f);
            else if (key.Keycode == Key.Key4)
                SetSimulationSpeed(6f);
        }
    }

    private void UpdateRoadDragPreview()
    {
        if (_roadDragStart.X < 0 || _hoveredCell.X < 0)
        {
            _roadDragPlan = null;
            RefreshRoadPreviewMesh();
            return;
        }

        _roadDragPlan = _bridge.Engine.PlanRoadPath(_roadDragStart.X, _roadDragStart.Y, _hoveredCell.X, _hoveredCell.Y);
        RefreshRoadPreviewMesh();

        if (_roadDragPlan.IsValid)
        {
            string affordTag = _roadDragPlan.CanAfford ? "" : " ⚠️ SALDO INSUFICIENTE";
            _topAlertLabel.Text =
                $"📐 Via: {_roadDragPlan.TilesToBuild.Count} trechos | 🌉 {_roadDragPlan.BridgesCount} Pontes | ⛰️ {_roadDragPlan.TunnelsCount} Túneis | R$ {_roadDragPlan.TotalCost:N0}{affordTag}";
        }
    }

    private void CommitRoadDrag()
    {
        _isDraggingRoad = false;
        var plan = _roadDragPlan;
        var start = _roadDragStart;
        _roadDragPlan = null;
        RefreshRoadPreviewMesh();

        if (plan != null && plan.TilesToBuild.Count > 1)
        {
            if (_bridge.Engine.BuildPlannedRoad(plan, out int built, out int bridges, out int tunnels))
            {
                if (tunnels > 0)
                {
                    var oldTerrain = _terrainRoot.GetNodeOrNull("VoxelTerrainMesh");
                    oldTerrain?.QueueFree();
                    var newTerrain = ProceduralModels3D.BuildContinuousTerrainMesh(_bridge.World.Grid);
                    newTerrain.Name = "VoxelTerrainMesh";
                    _terrainRoot.AddChild(newTerrain);
                }

                foreach (var (tx, ty) in plan.TilesToBuild)
                {
                    RebuildRoadAndNeighbors(tx, ty);
                    SpawnConstructionBurst(GridToWorld(tx, ty));
                }

                _audio?.PlayRoadBuild(bridges > 0 ? RoadStructureType.Bridge : (tunnels > 0 ? RoadStructureType.Tunnel : RoadStructureType.Ground));
                _tutorialRoadBuilt = true;
                LogEvent($"📐 Corredor construído: {built} trechos (🌉 {bridges} pontes, ⛰️ {tunnels} túneis) por R$ {plan.TotalCost:N0}!");
                _minimapStaticDirty = true;
                _heatmapTransformsDirty = true;
                RefreshHeatmapOverlay();
                RefreshHud();
            }
            else
            {
                _audio?.PlayError();
                LogEvent($"⚠️ Não foi possível construir o corredor (Custo: R$ {plan.TotalCost:N0}).");
            }
            return;
        }

        if (start.X >= 0)
        {
            HandleGridCellClick(start.X, start.Y, _corridorBatchMode);
        }
    }

    private void CancelRoadDrag()
    {
        _isDraggingRoad = false;
        _roadDragPlan = null;
        RefreshRoadPreviewMesh();
        _topAlertLabel.Text = "Construção de via cancelada.";
    }

    private void RefreshRoadPreviewMesh()
    {
        if (_roadPreviewMultiMesh?.Multimesh == null) return;
        var mm = _roadPreviewMultiMesh.Multimesh;

        if (_roadDragPlan == null || _roadDragPlan.PathTiles.Count == 0)
        {
            mm.InstanceCount = 0;
            _roadPreviewMultiMesh.Visible = false;
            return;
        }

        mm.InstanceCount = _roadDragPlan.PathTiles.Count;
        var grid = _bridge.World.Grid;

        for (int i = 0; i < _roadDragPlan.PathTiles.Count; i++)
        {
            var (px, py) = _roadDragPlan.PathTiles[i];
            var cell = grid[px, py];
            float yOff = cell.Terrain == TerrainType.Water ? 0.34f : (cell.Terrain is TerrainType.Hills or TerrainType.Mountain ? 0.24f : 0.14f);
            Vector3 pos = GridToWorld(px, py) + new Vector3(0f, yOff, 0f);
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, pos));

            Color col = !_roadDragPlan.CanAfford
                ? new Color(0.94f, 0.27f, 0.27f, 0.78f)
                : (cell.HasRoad ? new Color(0.22f, 0.74f, 0.97f, 0.55f) : new Color(0.29f, 0.87f, 0.50f, 0.78f));
            mm.SetInstanceColor(i, col);
        }

        _roadPreviewMultiMesh.Visible = true;
    }

    private void SetMapOverlayMode(MapOverlayMode mode)
    {
        _overlayMode = mode;
        if (_minimapOverlay != null)
            _minimapOverlay.OverlayMode = mode;

        if (_overlayStandardBtn != null)
        {
            _overlayStandardBtn.AddThemeStyleboxOverride("normal", mode == MapOverlayMode.Standard ? _pillBtnActive : _pillBtnNormal);
            _overlayTrafficBtn.AddThemeStyleboxOverride("normal", mode == MapOverlayMode.Traffic ? _pillBtnActive : _pillBtnNormal);
            _overlayPavementBtn.AddThemeStyleboxOverride("normal", mode == MapOverlayMode.Pavement ? _pillBtnActive : _pillBtnNormal);
            _overlayProfitBtn?.AddThemeStyleboxOverride("normal", mode == MapOverlayMode.Profitability ? _pillBtnActive : _pillBtnNormal);
        }

        RefreshHeatmapOverlay();
        _minimapStaticDirty = true;
    }

    private void RefreshHeatmapOverlay()
    {
        if (_heatmapMultiMesh?.Multimesh == null) return;
        var mm = _heatmapMultiMesh.Multimesh;

        if (_overlayMode == MapOverlayMode.Standard)
        {
            mm.InstanceCount = 0;
            _heatmapMultiMesh.Visible = false;
            _heatmapLastRoadCount = 0;
            return;
        }

        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);

        if (_heatmapTrafficBuf == null || _heatmapTrafficBuf.GetLength(0) != w || _heatmapTrafficBuf.GetLength(1) != h)
            _heatmapTrafficBuf = new int[w, h];

        if (_overlayMode == MapOverlayMode.Traffic)
            _bridge.Engine.FillCellTrafficMap(_heatmapTrafficBuf);

        int count = _roadTileCoords.Count;
        bool needTransforms = _heatmapTransformsDirty
            || mm.InstanceCount != count
            || _heatmapLastRoadCount != count;

        if (mm.InstanceCount != count)
            mm.InstanceCount = count;

        _heatmapLastRoadCount = count;
        _heatmapTransformsDirty = false;

        int idx = 0;

        foreach (var (x, y) in _roadTileCoords)
        {
            var cell = grid[x, y];
            if (needTransforms)
            {
                float yOff = cell.Terrain == TerrainType.Water ? 0.30f : 0.14f;
                mm.SetInstanceTransform(idx, new Transform3D(Basis.Identity, GridToWorld(x, y) + new Vector3(0f, yOff, 0f)));
            }

            Color col;
            if (_overlayMode == MapOverlayMode.Traffic)
            {
                int tCount = _heatmapTrafficBuf[x, y];
                col = tCount switch
                {
                    0 => new Color(0.13f, 0.77f, 0.37f, 0.55f),
                    1 or 2 => new Color(0.98f, 0.80f, 0.08f, 0.75f),
                    _ => new Color(0.94f, 0.27f, 0.27f, 0.85f)
                };
            }
            else if (_overlayMode == MapOverlayMode.Profitability)
            {
                long rev = _bridge.Engine.GetCellCorridorRevenue(x, y);
                if (rev <= 0)
                    col = new Color(0.39f, 0.45f, 0.55f, 0.50f);
                else if (rev < 800)
                    col = new Color(0.92f, 0.70f, 0.03f, 0.78f);
                else
                    col = new Color(0.06f, 0.73f, 0.51f, 0.86f);
            }
            else
            {
                if (_bridge.Engine.IsCellBlockedByLandslide(x, y))
                {
                    col = new Color(0.94f, 0.15f, 0.15f, 0.92f);
                }
                else
                {
                    float pav = _bridge.Engine.GetCellPavementCondition(x, y);
                    col = cell.RoadLevel switch
                    {
                        RoadTier.Dirt => new Color(0.63f, 0.38f, 0.03f, 0.65f),
                        RoadTier.Gravel => new Color(0.89f, 0.91f, 0.94f, 0.70f),
                        RoadTier.Asphalt => new Color(0.23f, 0.51f, 0.96f, 0.72f),
                        RoadTier.Highway => new Color(0.96f, 0.62f, 0.04f, 0.78f),
                        RoadTier.DualCarriage => new Color(0.02f, 0.71f, 0.83f, 0.82f),
                        _ => new Color(0.5f, 0.5f, 0.5f, 0.5f)
                    };
                    if (pav < 65f)
                    {
                        col = col.Lerp(new Color(0.95f, 0.35f, 0.10f, 0.88f), Mathf.Clamp((75f - pav) / 60f, 0.25f, 0.85f));
                    }
                }
            }

            mm.SetInstanceColor(idx, col);
            idx++;
        }

        _heatmapMultiMesh.Visible = count > 0;
    }

    private bool TryPickTruckAtScreenPos(Vector2 screenPos, out int truckIndex)
    {
        truckIndex = -1;
        if (_camera == null || _bridge.Engine.ActiveTrucks.Count == 0)
            return false;

        Vector3 rayOrigin = _camera.ProjectRayOrigin(screenPos);
        Vector3 rayDir = _camera.ProjectRayNormal(screenPos);
        float bestAlongRay = float.MaxValue;

        var trucks = _bridge.Engine.ActiveTrucks;
        for (int i = 0; i < trucks.Count; i++)
        {
            var truck = trucks[i];
            if (!_truckVisuals.TryGetValue(truck.Id, out var handle) || !handle.Root.Visible)
                continue;

            Vector3 center = handle.Root.GlobalPosition + new Vector3(0f, 0.34f, 0f);
            Vector3 oc = center - rayOrigin;
            float along = oc.Dot(rayDir);
            if (along <= 0f || along >= bestAlongRay)
                continue;

            Vector3 closest = rayOrigin + rayDir * along;
            float hitRadius = (truck.Spec.Capacity >= 30 || truck.Spec.RequiresHighwayOrDualCarriage) ? 1.15f : 0.90f;
            if (center.DistanceSquaredTo(closest) <= hitRadius * hitRadius)
            {
                bestAlongRay = along;
                truckIndex = i;
            }
        }

        return truckIndex >= 0;
    }

    private void SelectAndFocusTruck(int truckIndex)
    {
        var trucks = _bridge.Engine.ActiveTrucks;
        if (truckIndex < 0 || truckIndex >= trucks.Count) return;

        _selectedTruckIndex = truckIndex;
        var truck = trucks[truckIndex];
        _audio?.PlayUiClick();

        if (_pipPanel != null && !_pipPanel.Visible)
        {
            _pipPanel.Visible = true;
            AnimateControlPopup(_pipPanel);
            _pipToggleBtn?.AddThemeStyleboxOverride("normal", _pillBtnActive);
        }

        OpenWindowOnTab(WindowTab.Schedule);
        SyncScheduleTruckDropdown(forceRebuild: true);
        SyncScheduleSelectorsToCurrentTruck();
        RefreshHud();
        LogEvent($"🚛 Selecionado no mapa 3D: [b]{truck.Name}[/b] ({truck.Spec.Name}).");
    }

    private void UpdateHoveredGridCell(Vector2 screenPos)
    {
        Vector3 rayOrigin = _camera.ProjectRayOrigin(screenPos);
        Vector3 rayDir = _camera.ProjectRayNormal(screenPos);

        if (Mathf.Abs(rayDir.Y) < 0.0001f)
        {
            _hoverCursor.Visible = false;
            _hoveredCell = (-1, -1);
            return;
        }

        float t = -rayOrigin.Y / rayDir.Y;
        if (t <= 0f)
        {
            _hoverCursor.Visible = false;
            _hoveredCell = (-1, -1);
            return;
        }

        Vector3 hitPos = rayOrigin + rayDir * t;
        int gx = Mathf.RoundToInt(hitPos.X / ProceduralModels3D.TileSize);
        int gy = Mathf.RoundToInt(hitPos.Z / ProceduralModels3D.TileSize);

        int w = _bridge.World.Grid.GetLength(0);
        int h = _bridge.World.Grid.GetLength(1);

        if (gx >= 0 && gx < w && gy >= 0 && gy < h)
        {
            _hoveredCell = (gx, gy);
            _hoverCursor.Position = GridToWorld(gx, gy) + new Vector3(0, 0.08f, 0);
            _hoverCursor.Visible = true;
            if (!_isDraggingRoad && (_lastHoverTipCell != _hoveredCell || _lastHoverTipTool != _activeTool))
            {
                _lastHoverTipCell = _hoveredCell;
                _lastHoverTipTool = _activeTool;
                UpdateHoverCostPreview(gx, gy);
            }
        }
        else
        {
            _hoveredCell = (-1, -1);
            _hoverCursor.Visible = false;
        }
    }

    private void UpdateHoverCostPreview(int gx, int gy)
    {
        if (_topAlertLabel == null)
            return;

        var cell = _bridge.World.Grid[gx, gy];
        string preview = _activeTool switch
        {
            ToolMode.BuildRoad => cell.HasRoad
                ? $"({gx},{gy}) Via existente ({cell.RoadLevel})"
                : (cell.OccupantId != 0 || _bridge.World.OceanMask[gx, gy]
                    ? $"({gx},{gy}) Local bloqueado"
                    : $"({gx},{gy}) {(cell.Terrain == TerrainType.Water ? "🌉 Ponte" : (cell.Terrain is TerrainType.Hills or TerrainType.Mountain ? "🚇 Túnel" : "🛤️ Estrada"))}: R$ {(cell.Terrain == TerrainType.Water ? RoadStructureType.Bridge : (cell.Terrain is TerrainType.Hills or TerrainType.Mountain ? RoadStructureType.Tunnel : RoadStructureType.Ground)).GetBaseConstructionCost():N0} (arraste para traçar)"),
            ToolMode.BuildRail => cell.HasRail
                ? $"({gx},{gy}) Trilho existente ({cell.RailStructure})"
                : (cell.OccupantId != 0 || _bridge.World.OceanMask[gx, gy]
                    ? $"({gx},{gy}) Local bloqueado"
                    : $"({gx},{gy}) {RailStructureExtensions.FromTerrain(cell.Terrain).GetDisplayName()}: R$ {RailStructureExtensions.FromTerrain(cell.Terrain).GetRailConstructionCost():N0} (arraste para traçar)"),
            ToolMode.BuildRailSignal => $"({gx},{gy}) Sinaleiro de Bloco: R$ {SimulationEngine.RailSignalConstructionCost:N0}",
            ToolMode.BuildIntermodalYard => $"({gx},{gy}) Pátio Rodoferroviário Intermodal: R$ {IntermodalTerminal.ConstructionCost:N0}",
            ToolMode.BuildGarage => $"({gx},{gy}) Garagem Regional: R$ 8.000",
            ToolMode.BuildLogisticsHub => $"({gx},{gy}) Hub Logístico (+20% Frete): R$ 12.000",
            ToolMode.BuildBusTerminal => $"({gx},{gy}) Rodoviária Urbana: R$ {BusTerminal.ConstructionCost:N0}",
            ToolMode.BuildTollStation => $"({gx},{gy}) Praça de Pedágio: R$ {TollStation.ConstructionCost:N0}",
            ToolMode.Demolish => $"({gx},{gy}) Demolir estrutura/via (+40% reembolso)",
            _ => string.Empty
        };

        if (!string.IsNullOrEmpty(preview))
            _topAlertLabel.Text = preview;
    }

    private void HandleGridCellClick(int gx, int gy, bool batchCorridor)
    {
        _selectedCell = (gx, gy);
        long balanceBefore = _bridge.Engine.Player.Balance;

        switch (_activeTool)
        {
            case ToolMode.Inspect:
                _audio?.PlayUiClick();
                OpenWindowOnTab(WindowTab.Inspector);
                break;

            case ToolMode.BuildRoad:
            {
                if (_bridge.TryBuildRoadTile(gx, gy, out var structure))
                {
                    if (structure == RoadStructureType.Tunnel)
                    {
                        var oldTerrain = _terrainRoot.GetNodeOrNull("VoxelTerrainMesh");
                        oldTerrain?.QueueFree();
                        var newTerrain = ProceduralModels3D.BuildContinuousTerrainMesh(_bridge.World.Grid);
                        newTerrain.Name = "VoxelTerrainMesh";
                        _terrainRoot.AddChild(newTerrain);
                    }
                    RebuildRoadAndNeighbors(gx, gy);
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _tutorialRoadBuilt = true;
                    _audio?.PlayRoadBuild(structure);
                    string sLabel = structure switch
                    {
                        RoadStructureType.Bridge => $"🌉 Ponte voxel construída em ({gx}, {gy}) (-R$ {structure.GetBaseConstructionCost()})!",
                        RoadStructureType.Tunnel => $"🚇 Túnel em encosta escavado em ({gx}, {gy}) (-R$ {structure.GetBaseConstructionCost()})!",
                        _ => $"🛤️ Estrada construída em ({gx}, {gy}) (-R$ {structure.GetBaseConstructionCost()})!"
                    };
                    LogEvent(sLabel);
                }
                else
                {
                    _audio?.PlayError();
                    LogEvent("⚠️ Clique em célula livre (Plains, Water ou Hills) com saldo suficiente.");
                }
                break;
            }

            case ToolMode.Terraform:
            {
                if (_bridge.TryTerraformTile(gx, gy, out _, out string tfMsg))
                {
                    var oldTerrain = _terrainRoot.GetNodeOrNull("VoxelTerrainMesh");
                    oldTerrain?.QueueFree();
                    var newTerrain = ProceduralModels3D.BuildContinuousTerrainMesh(_bridge.World.Grid);
                    newTerrain.Name = "VoxelTerrainMesh";
                    _terrainRoot.AddChild(newTerrain);

                    var oldVeg = _terrainRoot.GetNodeOrNull("BiomeVegetationGroup");
                    oldVeg?.QueueFree();
                    _vegShadowCasters.Clear();
                    var newVeg = ProceduralModels3D.BuildBiomeVegetationGroup(_bridge.World.Grid, _bridge.Seed, _vegShadowCasters);
                    _terrainRoot.AddChild(newVeg);
                    ApplyZoomPerformanceLod(force: true);

                    _minimapStaticDirty = true;
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                }
                else
                {
                    _audio?.PlayError();
                }
                LogEvent(tfMsg);
                break;
            }

            case ToolMode.UpgradeGravel:
            case ToolMode.UpgradeAsphalt:
            case ToolMode.UpgradeHighway:
            case ToolMode.UpgradeDualCarriage:
            {
                RoadTier targetTier = _activeTool switch
                {
                    ToolMode.UpgradeGravel => RoadTier.Gravel,
                    ToolMode.UpgradeAsphalt => RoadTier.Asphalt,
                    ToolMode.UpgradeHighway => RoadTier.Highway,
                    ToolMode.UpgradeDualCarriage => RoadTier.DualCarriage,
                    _ => RoadTier.Gravel
                };

                RoadTier maxAllowed = _bridge.Engine.Research.GetMaxUnlockedRoadTier(_bridge.Engine.Player.Level);
                if (targetTier > maxAllowed)
                {
                    _audio?.PlayError();
                    LogEvent($"⚠️ {targetTier} bloqueado! Seu nível ({_bridge.Engine.Player.Level}) permite até {maxAllowed} (ou desbloqueie na aba Pesquisa).");
                    break;
                }

                if (batchCorridor)
                {
                    int upgraded = _bridge.TryUpgradeRoadCorridor(gx, gy, targetTier, maxTiles: 30);
                    if (upgraded > 0)
                    {
                        RebuildAllRoadMeshes();
                        _audio?.PlayPaveUpgrade(targetTier);
                        LogEvent($"🛣️ Corredor pavimentado: {upgraded} trechos em {targetTier} (10% OFF).");
                    }
                    else
                    {
                        _audio?.PlayError();
                        LogEvent("⚠️ Clique em uma estrada de nível inferior com saldo suficiente.");
                    }
                }
                else
                {
                    if (_bridge.TryUpgradeRoad(gx, gy, targetTier))
                    {
                        RebuildRoadAndNeighbors(gx, gy);
                        SpawnConstructionBurst(GridToWorld(gx, gy));
                        _audio?.PlayPaveUpgrade(targetTier);
                        LogEvent($"🛣️ Trecho ({gx}, {gy}) pavimentado para {targetTier}!");
                    }
                    else
                    {
                        _audio?.PlayError();
                    }
                }
                break;
            }

            case ToolMode.BuildGarage:
            {
                var garage = _bridge.TryBuildGarage(gx, gy, $"Garagem #{_bridge.Engine.Garages.Count + 1}");
                if (garage != null)
                {
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                    LogEvent($"🔧 {garage.Name} construída em ({gx}, {gy})!");
                }
                else
                {
                    _audio?.PlayError();
                    LogEvent("⚠️ Clique em planície vazia até 3 células da estrada (R$ 8.000).");
                }
                break;
            }

            case ToolMode.BuildLogisticsHub:
            {
                var hub = _bridge.TryBuildLogisticsHub(gx, gy, $"Hub #{_bridge.Engine.LogisticsHubs.Count + 1}");
                if (hub != null)
                {
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                    LogEvent($"📦 {hub.Name} inaugurado em ({gx}, {gy})! (+20% frete).");
                }
                else
                {
                    _audio?.PlayError();
                    LogEvent("⚠️ Clique em planície vazia próxima à estrada (R$ 12.000).");
                }
                break;
            }

            case ToolMode.BuildTollStation:
            {
                var toll = _bridge.TryBuildTollStation(gx, gy, out string tollMsg);
                if (toll != null)
                {
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                }
                else
                    _audio?.PlayError();
                LogEvent(tollMsg);
                break;
            }

            case ToolMode.BuildBusTerminal:
            {
                var terminal = _bridge.TryBuildBusTerminal(gx, gy, out string btMsg);
                if (terminal != null)
                {
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                }
                else
                    _audio?.PlayError();
                LogEvent(btMsg);
                break;
            }

            case ToolMode.BuildRail:
            case ToolMode.BuildRailSignal:
            case ToolMode.BuildIntermodalYard:
                HandleRailToolClick(gx, gy);
                break;

            case ToolMode.Demolish:
            {
                if (_bridge.TryDemolishAt(gx, gy, out var demResult, out string demMsg))
                {
                    Vector3 wPos = GridToWorld(gx, gy);
                    if (demResult.Kind == DemolishTargetKind.Garage && _garageNodes.TryGetValue(demResult.RemovedEntityId, out var gNode))
                    {
                        if (IsInstanceValid(gNode)) gNode.QueueFree();
                        _garageNodes.Remove(demResult.RemovedEntityId);
                        RebuildRoadAndNeighbors(gx, gy);
                    }
                    else if (demResult.Kind == DemolishTargetKind.LogisticsHub && _hubNodes.TryGetValue(demResult.RemovedEntityId, out var hNode))
                    {
                        if (IsInstanceValid(hNode)) hNode.QueueFree();
                        _hubNodes.Remove(demResult.RemovedEntityId);
                        RebuildRoadAndNeighbors(gx, gy);
                    }
                    else if (demResult.Kind == DemolishTargetKind.BusTerminal && _busTerminalNodes.TryGetValue(demResult.RemovedEntityId, out var btNode))
                    {
                        if (IsInstanceValid(btNode)) btNode.QueueFree();
                        _busTerminalNodes.Remove(demResult.RemovedEntityId);
                        RebuildRoadAndNeighbors(gx, gy);
                    }
                    else if (demResult.Kind == DemolishTargetKind.TollStation && _tollNodes.TryGetValue(demResult.RemovedEntityId, out var tNode))
                    {
                        if (IsInstanceValid(tNode)) tNode.QueueFree();
                        _tollNodes.Remove(demResult.RemovedEntityId);
                    }
                    else if (demResult.Kind == DemolishTargetKind.Road)
                    {
                        if (_trafficControlNodes.TryGetValue((gx, gy), out var tcNode))
                        {
                            if (IsInstanceValid(tcNode)) tcNode.QueueFree();
                            _trafficControlNodes.Remove((gx, gy));
                        }
                        if (demResult.RemovedStructureType == RoadStructureType.Tunnel)
                        {
                            var oldTerrain = _terrainRoot.GetNodeOrNull("VoxelTerrainMesh");
                            oldTerrain?.QueueFree();
                            var newTerrain = ProceduralModels3D.BuildContinuousTerrainMesh(_bridge.World.Grid);
                            newTerrain.Name = "VoxelTerrainMesh";
                            _terrainRoot.AddChild(newTerrain);
                        }
                        RebuildRoadAndNeighbors(gx, gy);
                    }
                    else
                    {
                        HandleDemolishRailVisuals(demResult, gx, gy);
                    }

                    SpawnConstructionBurst(wPos);
                    if (demResult.RefundAmount > 0)
                        SpawnFloatingRevenuePopup(wPos, $"+R$ {demResult.RefundAmount:N0}", new Color("4ade80"), fontSize: 44, duration: 1.6f);
                    _audio?.PlayStructureBuilt();
                    LogEvent(demMsg);
                }
                else
                {
                    _audio?.PlayError();
                    LogEvent(demMsg);
                }
                break;
            }
        }

        long spent = balanceBefore - _bridge.Engine.Player.Balance;
        if (spent > 0)
        {
            SpawnFloatingRevenuePopup(GridToWorld(gx, gy), $"-R$ {spent:N0}", new Color("f87171"), fontSize: 44, duration: 1.6f);
        }

        RefreshHud();
    }

    private void RebuildRoadAndNeighbors(int gx, int gy)
    {
        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var dirtyChunks = new HashSet<(int Cx, int Cy)>();

        void TouchTile(int tx, int ty)
        {
            if (tx < 0 || tx >= w || ty < 0 || ty >= h) return;
            if (grid[tx, ty].HasRoad && _urbanTileNodes.TryGetValue((tx, ty), out var overlapBuilding))
            {
                _urbanTileNodes.Remove((tx, ty));
                if (IsInstanceValid(overlapBuilding))
                    overlapBuilding.QueueFree();
            }
            dirtyChunks.Add(GetRoadChunkCoord(tx, ty));
        }

        TouchTile(gx, gy);
        TouchTile(gx + 1, gy);
        TouchTile(gx - 1, gy);
        TouchTile(gx, gy + 1);
        TouchTile(gx, gy - 1);

        foreach (var (cx, cy) in dirtyChunks)
        {
            RebuildRoadChunk(cx, cy);
        }

        _minimapStaticDirty = true;
        RefreshIndustryVisualsNear(gx, gy);
    }

    private void RebuildAllRoadMeshes()
    {
        RebuildAllRoadChunks();
    }

    private void CycleCameraFocus()
    {
        _followedTruckIndex = -1;
        if (_bridge.World.Cities.Count == 0) return;
        _focusCityIndex = (_focusCityIndex + 1) % _bridge.World.Cities.Count;
        var city = _bridge.World.Cities[_focusCityIndex];
        SmoothMoveCameraPivotTo(GridToWorld(city.CenterX, city.CenterY));
        _selectedCell = (city.CenterX, city.CenterY);
        LogEvent($"📍 Foco em {city.Name} ({city.Population} hab).");
        RefreshHud();
    }

    private void SmoothMoveCameraPivotTo(Vector3 targetWorldPos)
    {
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_cameraPivot, "position", targetWorldPos, 0.38);
    }

    private void AnimateControlPopup(Control panel)
    {
        panel.Modulate = new Color(1f, 1f, 1f, 0f);
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(panel, "modulate:a", 1.0f, 0.18);
    }

    private void CycleFollowTruckCamera()
    {
        if (_bridge.Engine.ActiveTrucks.Count == 0) return;
        _followedTruckIndex = (_followedTruckIndex + 1) % _bridge.Engine.ActiveTrucks.Count;
        _selectedTruckIndex = _followedTruckIndex;
        var truck = _bridge.Engine.ActiveTrucks[_followedTruckIndex];
        _cameraDistance = 13.0f;
        _cameraPitch = 0.45f;
        UpdateCameraTransform();
        SyncScheduleSelectorsToCurrentTruck();
        RefreshScheduleTab();
        LogEvent($"🎥 Seguindo {truck.Name} (use WASD para soltar).");
    }

    private void SetSimulationSpeed(float scale)
    {
        _bridge.TimeScale = scale;
        _audio?.PlaySpeedChange(scale);
        _audio?.UpdateEnvironmentState(_bridge.Engine.CurrentWeather, scale);
        string label = scale <= 0.01f ? "⏸ PAUSADO" : $"▶ {scale:0.#}x";
        LogEvent($"⏱️ Velocidade: {label}");
        RefreshHud();
    }

    // =========================================================================
    // Eventos da Simulação
    // =========================================================================

    private void OnCityExpanded(City city, int x, int y)
    {
        SpawnUrbanTileIfNeeded(city, x, y);
        RebuildRoadAndNeighbors(city.CenterX, city.CenterY);
        RebuildRoadAndNeighbors(x, y);
        _minimapStaticDirty = true;
        _audio?.PlayNotification(celebratory: false);
        string districtPt = city.GetDistrictType(x, y) == UrbanDistrictType.Commercial ? "Comercial" : "Residencial";
        LogEvent($"🏙️ {city.Name} expandiu bairro {districtPt}! População: {city.Population} hab.");
    }

    private void OnDemandIncreased(City city, ResourceType resource, int delta, bool isNewResource)
    {
        string msg = isNewResource
            ? $"📈 Demand increased! {city.Name} passou a exigir {resource} ({delta}/dia)!"
            : $"📈 Demand increased! {city.Name} aumentou consumo de {resource} (+{delta}/dia)!";
        _audio?.PlayNotification(celebratory: false);
        LogEvent(msg);
    }

    private void OnRegionUnlocked(RegionUnlockInfo info)
    {
        if (info.NewCity != null)
            SpawnCityVisuals(info.NewCity);
        if (info.NewIndustry != null)
        {
            SpawnIndustryVisuals(info.NewIndustry);
            RefreshIndustryVisualsNear(info.NewIndustry.GridX, info.NewIndustry.GridY);
        }

        RebuildAllRoadMeshes();
        RebuildSchedulePools();

        string cityNote = info.NewCity != null ? info.NewCity.Name : "Novo Setor";
        string indNote = info.NewIndustry != null ? $" + {info.NewIndustry.Name}" : "";
        _audio?.PlayNotification(celebratory: true);
        LogEvent($"🗺️ EXPANSÃO REGIONAL ({info.RegionName}): {cityNote}{indNote} conectado(a) à rede!");
        RefreshHud();
    }

    private void OnRoadUpgraded(RoadEdge edge, RoadTier tier)
    {
        if (tier >= RoadTier.Asphalt)
            _tutorialPavedAsphalt = true;
    }

    private void OnDeliveryCompleted(Truck truck, ResourceType resource, int amount, long reward)
    {
        var currentStop = truck.HasManualSchedule ? truck.GetCurrentStop() : null;
        string destName = currentStop != null
            ? _bridge.Engine.GetStopDisplayName(currentStop)
            : (truck.AssignedTargetIndustry?.Name ?? truck.AssignedTargetCity?.Name ?? "Destino");
        if (reward > 0)
        {
            Vector3 pos = _truckVisuals.TryGetValue(truck.Id, out var vis)
                ? vis.Root.GlobalPosition
                : _cameraPivot.Position;
            if (currentStop?.Kind == ScheduleStopKind.Seaport)
            {
                var sp = _bridge.World.Seaports.FirstOrDefault(s => s.Id == currentStop.TargetId);
                if (sp != null) pos = GridToWorld(sp.GridX, sp.GridY);
            }
            else if (currentStop?.Kind == ScheduleStopKind.RegionalLandmark)
            {
                var lm = _bridge.World.Landmarks.FirstOrDefault(l => l.Id == currentStop.TargetId);
                if (lm != null) pos = GridToWorld(lm.GridX, lm.GridY);
            }
            else if (truck.AssignedTargetIndustry != null)
            {
                pos = GridToWorld(truck.AssignedTargetIndustry.GridX, truck.AssignedTargetIndustry.GridY);
            }
            else if (truck.AssignedTargetCity != null)
            {
                pos = GridToWorld(truck.AssignedTargetCity.CenterX, truck.AssignedTargetCity.CenterY);
            }

            SpawnFloatingRevenuePopup(pos, $"+R$ {reward:N0}", new Color("4ade80"), fontSize: 52, duration: 2.1f);
            _audio?.PlayCashRegister(reward);
        }
        LogEvent($"💰 {truck.Name} entregou {amount}x {resource} em {destName} (+R$ {reward:N0})");
    }

    private void OnGarageBuilt(Garage garage)
    {
        SpawnGarageMesh(garage);
        RefreshIndustryVisualsNear(garage.GridX, garage.GridY);
        RebuildRoadAndNeighbors(garage.GridX, garage.GridY);
        RebuildSchedulePools();
        _minimapStaticDirty = true;
    }

    private void OnLogisticsHubBuilt(LogisticsHub hub)
    {
        SpawnLogisticsHubMesh(hub);
        RefreshIndustryVisualsNear(hub.GridX, hub.GridY);
        RebuildRoadAndNeighbors(hub.GridX, hub.GridY);
        RebuildSchedulePools();
        _minimapStaticDirty = true;
    }

    private void OnBusTerminalBuilt(BusTerminal terminal)
    {
        SpawnBusTerminalMesh(terminal);
        RebuildRoadAndNeighbors(terminal.GridX, terminal.GridY);
        RebuildSchedulePools();
        _minimapStaticDirty = true;
        RefreshHud();
    }

    private void OnPassengersDelivered(Truck bus, BusTerminal terminal, int count, long revenue)
    {
        SpawnFloatingRevenuePopup(GridToWorld(terminal.GridX, terminal.GridY), $"+R$ {revenue:N0} ({count} pass.)", new Color("38bdf8"), fontSize: 50, duration: 2.1f);
        _audio?.PlayCashRegister(revenue);
        LogEvent($"🚌 {bus.Name} desembarcou {count} passageiros em {terminal.Name} (+R$ {revenue:N0} em passagens).");
    }

    private void OnContractAuctionWon(LogisticsContract contract, string winnerName)
    {
        bool playerWon = winnerName == _bridge.Engine.Player.CompanyName;
        _audio?.PlayNotification(celebratory: playerWon);
        LogEvent(playerWon
            ? $"🏆 Leilão vencido! Exclusividade garantida em {contract.TargetCity.Name} (Tarifa {contract.FreightMultiplier * 100f:F0}%)."
            : $"🏢 Leilão perdido para {winnerName} ({contract.TargetCity.Name}).");
        RefreshHud();
    }

    private void OnRivalRoadBuilt(RivalCompany rival, List<RoadEdge> createdEdges, long tollFee)
    {
        RebuildAllRoadChunks();
        _minimapStaticDirty = true;
        LogEvent($"🚧 {rival.Name} inaugurou trecho próprio pedagiado (R$ {tollFee:N0}/viagem). Construa um desvio competitivo!");
        RefreshHud();
    }

    private void OnRivalTollPaid(Truck truck, RivalCompany rival, long fee)
    {
        LogEvent($"💸 {truck.Name} pagou R$ {fee:N0} de pedágio ao trecho rival de {rival.Name}.");
    }

    private void OnTruckPitStopCompleted(Truck truck, string facilityName, long cost)
    {
        string costText = cost > 0 ? $" (R$ {cost:N0})" : " (Gratuito)";
        LogEvent($"🔧 {truck.Name} realizou Pit-Stop preventivo em {facilityName}{costText} — Condição 100%!");
    }

    private void OnIndustryUpgraded(Industry ind)
    {
        if (_industryLabels.TryGetValue(ind.Id, out var lbl))
            lbl.Text = GetIndustryBannerText(ind);
        RefreshIndustryVisualsNear(ind.GridX, ind.GridY);
    }

    private void OnContractCompleted(LogisticsContract contract)
    {
        Vector3 cityPos = GridToWorld(contract.TargetCity.CenterX, contract.TargetCity.CenterY) + new Vector3(0, 1.1f, 0);
        SpawnFloatingRevenuePopup(cityPos, $"🏆 +R$ {contract.BonusRewardMoney:N0}", new Color("facc15"), fontSize: 58, duration: 2.6f);
        _audio?.PlayCashRegister(contract.BonusRewardMoney);
        _audio?.PlayNotification(celebratory: true);
        LogEvent($"🏆 Contrato concluído: {contract.TargetCity.Name} (+R$ {contract.BonusRewardMoney:N0})!");
    }

    private void SpawnFloatingRevenuePopup(Vector3 worldPos, string text, Color color, int fontSize = 52, float duration = 2.1f)
    {
        if (_structuresRoot == null) return;
        var label = ProceduralModels3D.CreateFloatingLabel(text, color, heightY: 0f, fontSize: fontSize);
        label.Position = worldPos + new Vector3(0, 2.2f, 0);
        _structuresRoot.AddChild(label);
        _floatingPopups.Add((label, 0f, duration));
    }

    private void UpdateFloatingPopups(float dt)
    {
        for (int i = _floatingPopups.Count - 1; i >= 0; i--)
        {
            var item = _floatingPopups[i];
            if (!IsInstanceValid(item.Label))
            {
                _floatingPopups.RemoveAt(i);
                continue;
            }

            float nextAge = item.Age + dt;
            if (nextAge >= item.Duration)
            {
                item.Label.QueueFree();
                _floatingPopups.RemoveAt(i);
                continue;
            }

            item.Label.Position += new Vector3(0f, dt * 1.45f, 0f);
            float t = nextAge / item.Duration;
            float alpha = t > 0.65f ? Mathf.Clamp((1f - t) / 0.35f, 0f, 1f) : 1f;

            var mod = item.Label.Modulate;
            mod.A = alpha;
            item.Label.Modulate = mod;

            var outMod = item.Label.OutlineModulate;
            outMod.A = alpha * 0.98f;
            item.Label.OutlineModulate = outMod;

            _floatingPopups[i] = (item.Label, nextAge, item.Duration);
        }
    }

    private void OnWeatherChanged(WeatherCondition weather)
    {
        string desc = weather switch
        {
            WeatherCondition.Clear => "☀️ Tempo Limpo",
            WeatherCondition.Rain => "🌧️ Chuva (-40% vel. em Terra)",
            WeatherCondition.HeavyStorm => "⛈️ Tempestade (-60% vel. em Terra)",
            _ => weather.ToString()
        };
        LogEvent($"🌦️ Clima: {desc}");
    }

    private void OnSeasonalEventChanged(SeasonalEventType evt, float durationHours)
    {
        switch (evt)
        {
            case SeasonalEventType.BumperHarvest:
                _audio?.PlayNotification(celebratory: true);
                LogEvent($"🌾 SAFRA RECORDE ({durationHours:F0}h): Fazendas de Grãos estão produzindo em DOBRO!");
                break;
            case SeasonalEventType.WinterBlizzard:
                _audio?.PlayNotification(celebratory: false);
                LogEvent($"❄️ NEVASCA DE INVERNO ({durationHours:F0}h): Vias não pavimentadas em região Alpina sofrem severa lentidão!");
                break;
            case SeasonalEventType.MountainLandslide:
                _audio?.PlayError();
                LogEvent($"🚧 DESLIZAMENTO EM ENCOSTA ({durationHours:F0}h): Trecho de montanha interditado — frota desviando rotas!");
                RebuildAllRoadTileMeshes();
                break;
            case SeasonalEventType.None:
                LogEvent("🌤️ Evento sazonal encerrado; condições de tráfego e safra normalizadas.");
                RebuildAllRoadTileMeshes();
                break;
        }
        RefreshHud();
    }

    private void OnMunicipalSubsidyPaid(City city, long amount)
    {
        Vector3 cityPos = GridToWorld(city.CenterX, city.CenterY) + new Vector3(0, 1.3f, 0);
        SpawnFloatingRevenuePopup(cityPos, $"🏛️ +R$ {amount:N0}", new Color("38bdf8"), fontSize: 50, duration: 2.2f);
        LogEvent($"🏛️ Subsídio Municipal: [b]{city.Name}[/b] concedeu R$ {amount:N0} (satisfação {city.LastSatisfactionRate * 100f:F0}%).");
    }

    private void OnRoadMaintained(int repairedCount, long totalCost)
    {
        RebuildAllRoadTileMeshes();
        if (repairedCount > 0 && totalCost > 0)
        {
            LogEvent($"🛠️ Conservação Viária: {repairedCount} segmento(s) recapeado(s) (-R$ {totalCost:N0}).");
        }
    }

    private void OnTrafficControlBuilt(RoadNode node)
    {
        SpawnTrafficControlMesh(node);
    }

    private void OnTollStationBuilt(TollStation station)
    {
        SpawnTollStationMesh(station);
    }

    private void OnTollCollected(TollStation station, long amount)
    {
        // Evita excesso de popups no tráfego diário passivo; apenas atualiza quando visível
        if (station.TrucksInspected > 0 && _bridge.TimeScale > 0.01f && _bridge.TimeScale <= 3f)
        {
            SpawnFloatingRevenuePopup(GridToWorld(station.GridX, station.GridY), $"🚧 +R$ {amount:N0}", new Color("38bdf8"), fontSize: 40, duration: 1.5f);
        }
    }

    private void OnTollWeighUpdated(TollStation station, string displayText, long rivalPaid)
    {
        if (_tollWeighLabels.TryGetValue(station.Id, out var lbl) && IsInstanceValid(lbl))
        {
            lbl.Text = $"⚖️ {displayText}";
            lbl.Modulate = displayText.Contains("RODOTREM") || displayText.Contains("ESCOLTA")
                ? new Color("facc15")
                : (rivalPaid > 0 ? new Color("38bdf8") : new Color("4ade80"));
        }
    }

    private void OnContractLostToRival(LogisticsContract contract, RivalCompany rival)
    {
        _audio?.PlayError();
        LogEvent($"🏢 CONCORRÊNCIA: [b]{rival.Name}[/b] venceu o contrato de {contract.Resource} para {contract.TargetCity.Name}!");
        RefreshHud();
    }

    private void OnSeaportShipStatusChanged(SeaportTerminal seaport, bool docked, int sailedUnits)
    {
        if (_seaportVisuals.TryGetValue(seaport.Id, out var vis) && IsInstanceValid(vis.StatusLabel))
        {
            string shipTag = docked ? "⚓ Navio Atracado (+45%)" : "🌊 Navio em Alto-Mar";
            vis.StatusLabel.Text = $"⚓ {seaport.Name}\n[{shipTag}]";
        }

        Vector3 pos = GridToWorld(seaport.GridX, seaport.GridY);
        if (docked)
        {
            _audio?.PlayNotification(celebratory: true);
            SpawnFloatingRevenuePopup(pos, "⚓ Navio Atracado (+45%)", new Color("38bdf8"), fontSize: 44, duration: 2.3f);
            LogEvent($"⚓ PORTO: Novo navio cargueiro atracou em [b]{seaport.Name}[/b]! Bônus de +45% de exportação ativo.");
        }
        else
        {
            _audio?.PlayNotification(celebratory: false);
            SpawnFloatingRevenuePopup(pos, $"🚢 Zarpou ({sailedUnits} un.)", new Color("94a3b8"), fontSize: 42, duration: 2.2f);
            LogEvent($"🚢 PORTO: Navio zarpou de [b]{seaport.Name}[/b] levando {sailedUnits} un. exportadas.");
        }
        RefreshHud();
    }

    private void OnLandmarkProgress(RegionalLandmark landmark, bool stageCompleted, bool projectCompleted)
    {
        SpawnLandmarkMesh(landmark);
        Vector3 pos = GridToWorld(landmark.GridX, landmark.GridY);
        SpawnConstructionBurst(pos);

        if (projectCompleted)
        {
            _audio?.PlayNotification(celebratory: true);
            _audio?.PlayCashRegister(15000);
            SpawnFloatingRevenuePopup(pos, $"🏆 {landmark.Name} CONCLUÍDA!", new Color("facc15"), fontSize: 48, duration: 2.8f);
            LogEvent($"🏆 MEGA-OBRA CONCLUÍDA: [b]{landmark.Name}[/b] finalizada (+R$ 15.000)! Bônus permanente: {landmark.BonusSummary}.");
        }
        else if (stageCompleted)
        {
            _audio?.PlayStructureBuilt();
            _audio?.PlayCashRegister(5000);
            SpawnFloatingRevenuePopup(pos, $"🏗️ Etapa {landmark.CurrentStage - 1}/3 concluída!", new Color("fbbf24"), fontSize: 44, duration: 2.4f);
            LogEvent($"🏗️ MEGA-OBRA: [b]{landmark.Name}[/b] avançou para a Etapa {landmark.CurrentStage}/{RegionalLandmark.MaxStages} (+R$ 5.000)!");
        }
        RefreshHud();
    }

    private void OnTruckRankUp(Truck truck, TruckRank newRank)
    {
        _audio?.PlayNotification(celebratory: true);
        LogEvent($"🏅 VETERANIA: [b]{truck.Name}[/b] alcançou [b]{truck.RankDisplayLabel}[/b] ({truck.TotalDistanceTravelledKm:F0} km • combustível {(1f - truck.FuelCostMultiplier) * 100f:F0}% OFF)!");
        if (truck.CurrentEdge != null)
        {
            float t = Mathf.Clamp(truck.DistanceAlongCurrentEdgeKm / Mathf.Max(0.01f, truck.CurrentEdge.DistanceKm), 0f, 1f);
            (Vector3 worldPos, _) = EvaluateOrthogonalRoadPath(
                truck.CurrentEdge.FromNode.GridX, truck.CurrentEdge.FromNode.GridY,
                truck.CurrentEdge.ToNode.GridX, truck.CurrentEdge.ToNode.GridY,
                t);
            SpawnFloatingRevenuePopup(worldPos, $"🏅 {truck.RankDisplayLabel}!", new Color("facc15"), fontSize: 44, duration: 2.3f);
        }
    }

    // =========================================================================
    // HUD Voxel Tycoon: Pills flutuantes nos cantos + Card branco com abas
    // =========================================================================

    private void BuildVoxelTycoonHud()
    {
        var canvas = new CanvasLayer { Layer = 10 };
        AddChild(canvas);

        _hudRegularLayer = new Control
        {
            Name = "RegularHudLayer",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _hudRegularLayer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(_hudRegularLayer);

        var darkPillStyle = new StyleBoxFlat
        {
            BgColor = new Color("22252b"),
            BorderColor = new Color("363b45"),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };

        var whiteToastStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.97f, 0.98f, 0.99f, 0.96f),
            BorderColor = new Color("d1d5db"),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };

        _pillBtnNormal = new StyleBoxFlat
        {
            BgColor = new Color("2d3139"),
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };

        _pillBtnActive = new StyleBoxFlat
        {
            BgColor = new Color("16a34a"),
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            ContentMarginLeft = 8,
            ContentMarginRight = 8,
            ContentMarginTop = 4,
            ContentMarginBottom = 4
        };

        _tabBtnNormal = new StyleBoxFlat
        {
            BgColor = new Color("2c3038"),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };

        _tabBtnActive = new StyleBoxFlat
        {
            BgColor = new Color("f4f5f7"),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        };

        // ---------------------------------------------------------------------
        // 1. TOP-CENTER NOTIFICATION PILL (como em images (8).jpg)
        // ---------------------------------------------------------------------
        var topCenterPill = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -585,
            OffsetRight = 585,
            OffsetTop = 10,
            OffsetBottom = 44
        };
        topCenterPill.AddThemeStyleboxOverride("panel", whiteToastStyle);
        _hudRegularLayer.AddChild(topCenterPill);

        var topCenterHBox = new HBoxContainer();
        topCenterHBox.AddThemeConstantOverride("separation", 8);
        topCenterPill.AddChild(topCenterHBox);

        _topAlertLabel = CreateLabel("🔔 Pronto para operar", new Color("1f2937"), 12);
        _topAlertLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _topAlertLabel.ClipText = true;
        topCenterHBox.AddChild(_topAlertLabel);

        AddPillButton(topCenterHBox, "💾 Salvar", SaveGameToUserDir);
        AddPillButton(topCenterHBox, "🗂️ Saves/Seed", ToggleSavesModal);
        AddPillButton(topCenterHBox, "🎨 Gráficos", ToggleGraphicsModal);
        AddPillButton(topCenterHBox, "📂 Auto", LoadAutoSaveFromUserDir);
        _autoSaveToggleBtn = AddPillButton(topCenterHBox, _autoSaveEnabled ? "💾 Auto: ON" : "💾 Auto: OFF", () =>
        {
            _autoSaveEnabled = !_autoSaveEnabled;
            _autoSaveToggleBtn.Text = _autoSaveEnabled ? "💾 Auto: ON" : "💾 Auto: OFF";
            _autoSaveToggleBtn.AddThemeStyleboxOverride("normal", _autoSaveEnabled ? _pillBtnActive : _pillBtnNormal);
            PersistClientSettings();
            LogEvent($"💾 Auto-Save mensal {(_autoSaveEnabled ? "ativado" : "desativado")}.");
        });
        _autoSaveToggleBtn.AddThemeStyleboxOverride("normal", _autoSaveEnabled ? _pillBtnActive : _pillBtnNormal);
        _tutorialToggleBtn = AddPillButton(topCenterHBox, "🎓 Tutorial", ToggleTutorialPanel);
        _tutorialToggleBtn.TooltipText = "Exibir ou ocultar o roteiro inicial de 5 passos";
        AddPillButton(topCenterHBox, "🎲 Novo Mapa", RegenerateRandomWorld);
        AddPillButton(topCenterHBox, "📍 Cidade", CycleCameraFocus);
        AddPillButton(topCenterHBox, "🎥 Caminhão", CycleFollowTruckCamera);
        var photoBtn = AddPillButton(topCenterHBox, "📸 Foto", TogglePhotoMode);
        photoBtn.TooltipText = "Modo Foto & Câmera Drone Cinematográfica (Atalho: F12)";
        AddPillButton(topCenterHBox, "🏠 Menu", EnterMainMenu);

        // ---------------------------------------------------------------------
        // 2. TOP-LEFT FLOATING WINDOW CARD (Header escuro + Abas + Corpo branco)
        //    Idêntico ao modelo de Research Management / Car #58 das capturas!
        // ---------------------------------------------------------------------
        _topLeftWindow = new PanelContainer
        {
            AnchorLeft = 0f,
            AnchorTop = 0f,
            OffsetLeft = 14,
            OffsetTop = 14,
            OffsetRight = 524,
            OffsetBottom = 478
        };
        var windowOuterStyle = new StyleBoxFlat
        {
            BgColor = new Color("f4f5f7"),
            BorderColor = new Color("22252b"),
            BorderWidthBottom = 2,
            BorderWidthTop = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6
        };
        _topLeftWindow.AddThemeStyleboxOverride("panel", windowOuterStyle);
        _hudRegularLayer.AddChild(_topLeftWindow);

        var winVBox = new VBoxContainer();
        winVBox.AddThemeConstantOverride("separation", 0);
        _topLeftWindow.AddChild(winVBox);

        // Header escuro da janela
        var headerPanel = new PanelContainer();
        headerPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("22252b"),
            ContentMarginLeft = 12,
            ContentMarginRight = 8,
            ContentMarginTop = 7,
            ContentMarginBottom = 7
        });
        winVBox.AddChild(headerPanel);

        var headerHBox = new HBoxContainer();
        headerPanel.AddChild(headerHBox);

        _windowTitleLabel = CreateLabel("Painel de Gestão", new Color("f8fafc"), 13);
        _windowTitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerHBox.AddChild(_windowTitleLabel);

        var closeBtn = new Button { Text = "✕" };
        closeBtn.AddThemeFontSizeOverride("font_size", 12);
        closeBtn.Pressed += () => _topLeftWindow.Visible = false;
        headerHBox.AddChild(closeBtn);

        // Barra de Abas (Inspetor | Frota | Contratos | Banco | Log)
        var tabBarPanel = new PanelContainer();
        tabBarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("22252b") });
        winVBox.AddChild(tabBarPanel);

        var tabHBox = new HBoxContainer();
        tabHBox.AddThemeConstantOverride("separation", 2);
        tabBarPanel.AddChild(tabHBox);

        _tabButtons[WindowTab.Inspector] = AddTabButton(tabHBox, "Inspetor", WindowTab.Inspector);
        _tabButtons[WindowTab.Schedule] = AddTabButton(tabHBox, "Agenda", WindowTab.Schedule);
        _tabButtons[WindowTab.Research] = AddTabButton(tabHBox, "Pesquisa", WindowTab.Research);
        _tabButtons[WindowTab.Fleet] = AddTabButton(tabHBox, "Frota", WindowTab.Fleet);
        _tabButtons[WindowTab.Rail] = AddTabButton(tabHBox, "Ferrovia", WindowTab.Rail);
        _tabButtons[WindowTab.Contracts] = AddTabButton(tabHBox, "Contratos", WindowTab.Contracts);
        _tabButtons[WindowTab.Operations] = AddTabButton(tabHBox, "Operações", WindowTab.Operations);
        _tabButtons[WindowTab.Bank] = AddTabButton(tabHBox, "Banco", WindowTab.Bank);
        _tabButtons[WindowTab.Log] = AddTabButton(tabHBox, "Log", WindowTab.Log);

        // Área de Conteúdo Branca
        var bodyMargin = new MarginContainer();
        bodyMargin.AddThemeConstantOverride("margin_left", 12);
        bodyMargin.AddThemeConstantOverride("margin_right", 12);
        bodyMargin.AddThemeConstantOverride("margin_top", 10);
        bodyMargin.AddThemeConstantOverride("margin_bottom", 10);
        bodyMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        winVBox.AddChild(bodyMargin);

        // Aba 1: Inspetor
        var inspBox = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _tabBodyInspector = inspBox;
        _inspectorText = new RichTextLabel
        {
            BbcodeEnabled = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _inspectorText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _inspectorText.AddThemeFontSizeOverride("normal_font_size", 12);
        _inspectorText.AddThemeFontSizeOverride("bold_font_size", 12);
        inspBox.AddChild(_inspectorText);

        _inspectorUpgradeIndustryBtn = new Button
        {
            Text = "⬆️ Expandir Planta Industrial",
            Visible = false
        };
        _inspectorUpgradeIndustryBtn.AddThemeFontSizeOverride("font_size", 11);
        _inspectorUpgradeIndustryBtn.Pressed += () =>
        {
            var ind = _bridge.World.Industries.FirstOrDefault(i => i.GridX == _selectedCell.X && i.GridY == _selectedCell.Y);
            if (ind == null) return;
            if (_bridge.TryUpgradeIndustry(ind, out string msg))
                _audio?.PlayStructureBuilt();
            else
                _audio?.PlayError();
            LogEvent(msg);
            RefreshHud();
        };
        inspBox.AddChild(_inspectorUpgradeIndustryBtn);

        _inspectorRepaveBtn = new Button
        {
            Text = "🛠️ Recapear Trecho",
            Visible = false
        };
        _inspectorRepaveBtn.AddThemeFontSizeOverride("font_size", 11);
        _inspectorRepaveBtn.Pressed += () =>
        {
            if (_bridge.Engine.TryRepaveRoadTile(_selectedCell.X, _selectedCell.Y, out string msg))
            {
                RebuildRoadTileMesh(_selectedCell.X, _selectedCell.Y);
                _audio?.PlayStructureBuilt();
            }
            else
                _audio?.PlayError();
            LogEvent(msg);
            RefreshHud();
        };
        inspBox.AddChild(_inspectorRepaveBtn);

        _inspectorTrafficCtrlBtn = new Button
        {
            Text = $"🚦 Instalar Rotatória/Semáforo 3D (R$ {SimulationEngine.TrafficControlUpgradeCost:N0})",
            Visible = false
        };
        _inspectorTrafficCtrlBtn.AddThemeFontSizeOverride("font_size", 11);
        _inspectorTrafficCtrlBtn.Pressed += () =>
        {
            if (_bridge.TryBuildTrafficControl(_selectedCell.X, _selectedCell.Y, out string msg))
                _audio?.PlayStructureBuilt();
            else
                _audio?.PlayError();
            LogEvent(msg);
            RefreshHud();
        };
        inspBox.AddChild(_inspectorTrafficCtrlBtn);

        _inspectorGarageAutoReplaceBtn = new Button
        {
            Text = "♻️ Auto-Substituir Frota (Garagem): OFF",
            Visible = false
        };
        _inspectorGarageAutoReplaceBtn.AddThemeFontSizeOverride("font_size", 11);
        _inspectorGarageAutoReplaceBtn.Pressed += () =>
        {
            var garage = _bridge.Engine.Garages.FirstOrDefault(g => g.GridX == _selectedCell.X && g.GridY == _selectedCell.Y);
            if (garage == null) return;
            bool next = !garage.AutoReplaceEnabled;
            _bridge.UpdateGarageAutoReplace(garage.Id, next, minCondition: 25f, maxMileageKm: 0f);
            _audio?.PlayStructureBuilt();
            LogEvent($"♻️ Auto-substituição em [b]{garage.Name}[/b]: {(next ? "ATIVADA (Condição <= 25%)" : "DESATIVADA")}.");
            RefreshHud();
        };
        inspBox.AddChild(_inspectorGarageAutoReplaceBtn);

        _inspectorGarageCloneBtn = new Button
        {
            Text = "📋 Clonar Veículo para esta Garagem",
            Visible = false
        };
        _inspectorGarageCloneBtn.AddThemeFontSizeOverride("font_size", 11);
        _inspectorGarageCloneBtn.Pressed += () =>
        {
            var garage = _bridge.Engine.Garages.FirstOrDefault(g => g.GridX == _selectedCell.X && g.GridY == _selectedCell.Y);
            if (garage == null) return;
            var trucks = _bridge.Engine.ActiveTrucks;
            if (trucks.Count == 0)
            {
                _audio?.PlayError();
                LogEvent("⚠️ Nenhum veículo ativo para clonar.");
                return;
            }
            var source = trucks[Mathf.Clamp(_selectedTruckIndex, 0, trucks.Count - 1)];
            if (_bridge.TryCloneTruck(source, out _, out string msg, garage))
                _audio?.PlayStructureBuilt();
            else
                _audio?.PlayError();
            LogEvent(msg);
            RefreshHud();
        };
        inspBox.AddChild(_inspectorGarageCloneBtn);

        bodyMargin.AddChild(inspBox);

        // Aba 2: Agenda de Rotas por Veículo (estilo Voxel Tycoon)
        var schedBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        schedBox.AddThemeConstantOverride("separation", 6);
        _tabBodySchedule = schedBox;

        schedBox.AddChild(CreateLabel("VEÍCULO & STATUS EM TEMPO REAL", new Color("4b5563"), 11));
        var truckPickRow = new HBoxContainer();
        truckPickRow.AddThemeConstantOverride("separation", 6);
        _scheduleTruckSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleTruckSelect.AddThemeFontSizeOverride("font_size", 12);
        _scheduleTruckSelect.ItemSelected += idx =>
        {
            _selectedTruckIndex = (int)idx;
            SyncScheduleSelectorsToCurrentTruck();
            RefreshScheduleTab();
        };
        truckPickRow.AddChild(_scheduleTruckSelect);

        var followSelectedBtn = new Button { Text = "🎥" };
        followSelectedBtn.TooltipText = "Seguir veículo com a câmera";
        followSelectedBtn.AddThemeFontSizeOverride("font_size", 11);
        followSelectedBtn.Pressed += () =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            _followedTruckIndex = _selectedTruckIndex;
            _cameraDistance = 13.0f;
            _cameraPitch = 0.45f;
            UpdateCameraTransform();
            LogEvent($"🎥 Seguindo {_bridge.Engine.ActiveTrucks[_followedTruckIndex].Name} (use WASD para soltar).");
        };
        truckPickRow.AddChild(followSelectedBtn);

        var cloneSelectedBtn = new Button { Text = "📋 Clonar" };
        cloneSelectedBtn.TooltipText = "Comprar caminhão idêntico copiando esta agenda";
        cloneSelectedBtn.AddThemeFontSizeOverride("font_size", 11);
        cloneSelectedBtn.Pressed += () =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            var source = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];
            if (_bridge.TryCloneTruck(source, out var cloned, out string msg) && cloned != null)
            {
                _selectedTruckIndex = _bridge.Engine.ActiveTrucks.Count - 1;
                SyncScheduleTruckDropdown(forceRebuild: true);
                SyncScheduleSelectorsToCurrentTruck();
                _audio?.PlayStructureBuilt();
            }
            else
            {
                _audio?.PlayError();
            }
            LogEvent(msg);
            RefreshHud();
        };
        truckPickRow.AddChild(cloneSelectedBtn);
        schedBox.AddChild(truckPickRow);

        _scheduleTruckInfoText = new RichTextLabel
        {
            BbcodeEnabled = true,
            CustomMinimumSize = new Vector2(0, 74),
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        _scheduleTruckInfoText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _scheduleTruckInfoText.AddThemeFontSizeOverride("normal_font_size", 11);
        _scheduleTruckInfoText.AddThemeFontSizeOverride("bold_font_size", 11);
        schedBox.AddChild(_scheduleTruckInfoText);

        _schedulePitStopCheck = new CheckBox
        {
            Text = "🔧 Pit-Stop Automático quando Condição < 35%",
            ButtonPressed = true
        };
        _schedulePitStopCheck.AddThemeColorOverride("font_color", new Color("1f2937"));
        _schedulePitStopCheck.AddThemeColorOverride("font_pressed_color", new Color("111827"));
        _schedulePitStopCheck.AddThemeFontSizeOverride("font_size", 11);
        _schedulePitStopCheck.Toggled += pressed =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            var truck = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];
            truck.AutoPitStopEnabled = pressed;
            LogEvent($"🔧 Pit-Stop automático {(pressed ? "ativado" : "desativado")} para {truck.Name}.");
            RefreshScheduleTab();
        };
        schedBox.AddChild(_schedulePitStopCheck);

        schedBox.AddChild(CreateLabel("PARADAS DA AGENDA (MULTI-PARADAS)", new Color("4b5563"), 11));
        var stopsScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 108),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _scheduleStopsListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleStopsListBox.AddThemeConstantOverride("separation", 3);
        stopsScroll.AddChild(_scheduleStopsListBox);
        schedBox.AddChild(stopsScroll);

        schedBox.AddChild(CreateLabel("ADICIONAR PARADA", new Color("4b5563"), 10));
        var addStopTopRow = new HBoxContainer();
        addStopTopRow.AddThemeConstantOverride("separation", 4);
        _scheduleActionSelect = new OptionButton();
        _scheduleActionSelect.AddThemeFontSizeOverride("font_size", 11);
        _scheduleActionSelect.AddItem("Carregar");
        _scheduleActionSelect.AddItem("Descarregar");
        _scheduleActionSelect.AddItem("Pit-Stop");
        addStopTopRow.AddChild(_scheduleActionSelect);

        _scheduleTargetSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleTargetSelect.AddThemeFontSizeOverride("font_size", 11);
        addStopTopRow.AddChild(_scheduleTargetSelect);
        schedBox.AddChild(addStopTopRow);

        var addStopFilterRow = new HBoxContainer();
        addStopFilterRow.AddThemeConstantOverride("separation", 4);
        _scheduleCargoFilterSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleCargoFilterSelect.AddThemeFontSizeOverride("font_size", 11);
        _scheduleCargoFilterSelect.AddItem("Qualquer Carga");
        _scheduleCargoFilterSelect.AddItem("Grain");
        _scheduleCargoFilterSelect.AddItem("Timber");
        _scheduleCargoFilterSelect.AddItem("IronOre");
        _scheduleCargoFilterSelect.AddItem("Coal");
        _scheduleCargoFilterSelect.AddItem("CrudeOil");
        _scheduleCargoFilterSelect.AddItem("Food");
        _scheduleCargoFilterSelect.AddItem("Lumber");
        _scheduleCargoFilterSelect.AddItem("Steel");
        _scheduleCargoFilterSelect.AddItem("Fuel");
        _scheduleCargoFilterSelect.AddItem("Goods");
        _scheduleCargoFilterSelect.AddItem("Machinery");
        addStopFilterRow.AddChild(_scheduleCargoFilterSelect);

        _scheduleMaxWaitSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleMaxWaitSelect.AddThemeFontSizeOverride("font_size", 11);
        _scheduleMaxWaitSelect.AddItem("Sem Máx. Espera");
        _scheduleMaxWaitSelect.AddItem("Máx. 12h");
        _scheduleMaxWaitSelect.AddItem("Máx. 24h");
        _scheduleMaxWaitSelect.AddItem("Máx. 48h");
        addStopFilterRow.AddChild(_scheduleMaxWaitSelect);
        schedBox.AddChild(addStopFilterRow);

        var addStopBotRow = new HBoxContainer();
        addStopBotRow.AddThemeConstantOverride("separation", 4);
        _scheduleConditionSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleConditionSelect.AddThemeFontSizeOverride("font_size", 11);
        _scheduleConditionSelect.AddItem("Partida Imediata");
        _scheduleConditionSelect.AddItem("Aguardar 50% Carga");
        _scheduleConditionSelect.AddItem("Aguardar 100% Carga");
        addStopBotRow.AddChild(_scheduleConditionSelect);

        var addStopBtn = new Button { Text = "➕ Adicionar" };
        addStopBtn.AddThemeFontSizeOverride("font_size", 11);
        addStopBtn.Pressed += () =>
        {
            int targetIdx = _scheduleTargetSelect.Selected;
            if (targetIdx < 0 || targetIdx >= _scheduleTargetPool.Count) return;

            var target = _scheduleTargetPool[targetIdx];
            var action = _scheduleActionSelect.Selected switch
            {
                0 => ScheduleAction.Load,
                1 => ScheduleAction.Unload,
                _ => ScheduleAction.PitStop
            };
            var condition = _scheduleConditionSelect.Selected switch
            {
                1 => DispatchCondition.WaitHalfLoad,
                2 => DispatchCondition.WaitFullLoad,
                _ => DispatchCondition.Immediate
            };
            var cargoFilter = _scheduleCargoFilterSelect.Selected switch
            {
                1 => ResourceType.Grain,
                2 => ResourceType.Timber,
                3 => ResourceType.IronOre,
                4 => ResourceType.Coal,
                5 => ResourceType.CrudeOil,
                6 => ResourceType.Food,
                7 => ResourceType.Lumber,
                8 => ResourceType.Steel,
                9 => ResourceType.Fuel,
                10 => ResourceType.Goods,
                11 => ResourceType.Machinery,
                _ => ResourceType.None
            };
            float maxWaitHours = _scheduleMaxWaitSelect.Selected switch
            {
                1 => 12f,
                2 => 24f,
                3 => 48f,
                _ => 0f
            };

            _draftSchedule.Add(new ScheduleStop(target.Kind, target.Id, action, condition, cargoFilter, maxWaitHours));
            RefreshDraftScheduleList();
        };
        addStopBotRow.AddChild(addStopBtn);
        schedBox.AddChild(addStopBotRow);

        var schedActionRow = new HBoxContainer();
        schedActionRow.AddThemeConstantOverride("separation", 6);
        var applySchedBtn = new Button
        {
            Text = "✅ Aplicar Agenda",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        applySchedBtn.AddThemeFontSizeOverride("font_size", 12);
        applySchedBtn.Pressed += () =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            var truck = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];
            _bridge.DetachTruckFromLogisticsLine(truck);
            string msg = _bridge.SetTruckMultiStopSchedule(truck, _draftSchedule);
            LogEvent(msg);
            RefreshHud();
        };
        schedActionRow.AddChild(applySchedBtn);

        var autoSchedBtn = new Button { Text = "🤖 Automático" };
        autoSchedBtn.AddThemeFontSizeOverride("font_size", 12);
        autoSchedBtn.Pressed += () =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            var truck = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];
            _bridge.DetachTruckFromLogisticsLine(truck);
            string msg = _bridge.SetTruckAutoSchedule(truck);
            LogEvent(msg);
            SyncScheduleSelectorsToCurrentTruck();
            RefreshHud();
        };
        schedActionRow.AddChild(autoSchedBtn);
        schedBox.AddChild(schedActionRow);

        // Linhas Logísticas Compartilhadas (Shared Route Templates)
        schedBox.AddChild(CreateLabel("LINHA LOGÍSTICA COMPARTILHADA", new Color("4b5563"), 10));
        var linePickRow = new HBoxContainer();
        linePickRow.AddThemeConstantOverride("separation", 4);
        _scheduleLineSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scheduleLineSelect.AddThemeFontSizeOverride("font_size", 11);
        _scheduleLineSelect.ItemSelected += idx =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
            var truck = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];
            int selIdx = (int)idx;
            if (selIdx <= 0)
            {
                _bridge.DetachTruckFromLogisticsLine(truck);
                LogEvent($"⛓️‍💥 {truck.Name} desvinculado de linha compartilhada.");
            }
            else if (selIdx - 1 < _bridge.Engine.LogisticsLines.Count)
            {
                var chosenLine = _bridge.Engine.LogisticsLines[selIdx - 1];
                _bridge.AssignTruckToLogisticsLine(truck, chosenLine);
                SyncScheduleSelectorsToCurrentTruck();
                LogEvent($"🔗 {truck.Name} vinculado à linha [b]{chosenLine.Name}[/b].");
            }
            RefreshHud();
        };
        linePickRow.AddChild(_scheduleLineSelect);

        _scheduleUpdateLineBtn = new Button { Text = "🔄 Atualizar Linha" };
        _scheduleUpdateLineBtn.TooltipText = "Salva alterações nesta Linha e atualiza TODOS os caminhões vinculados";
        _scheduleUpdateLineBtn.AddThemeFontSizeOverride("font_size", 11);
        _scheduleUpdateLineBtn.Pressed += () =>
        {
            if (_bridge.Engine.ActiveTrucks.Count == 0) return;
            int selLineIdx = _scheduleLineSelect.Selected - 1;
            if (selLineIdx < 0 || selLineIdx >= _bridge.Engine.LogisticsLines.Count)
            {
                _audio?.PlayError();
                LogEvent("⚠️ Selecione uma Linha compartilhada existente ou clique em '💾 Nova Linha'.");
                return;
            }
            var targetLine = _bridge.Engine.LogisticsLines[selLineIdx];
            string newName = _scheduleLineNameEdit?.Text?.Trim() ?? string.Empty;
            if (_bridge.UpdateLogisticsLine(targetLine.Id, string.IsNullOrWhiteSpace(newName) ? null : newName, _draftSchedule, _schedulePitStopCheck?.ButtonPressed))
            {
                int boundCount = _bridge.Engine.ActiveTrucks.Count(t => t.AssignedLineId == targetLine.Id);
                SyncScheduleSelectorsToCurrentTruck();
                _audio?.PlayStructureBuilt();
                LogEvent($"🔄 Linha [b]{targetLine.Name}[/b] atualizada! ({boundCount} veículo(s) sincronizados)");
            }
            RefreshHud();
        };
        linePickRow.AddChild(_scheduleUpdateLineBtn);
        schedBox.AddChild(linePickRow);

        var lineCreateRow = new HBoxContainer();
        lineCreateRow.AddThemeConstantOverride("separation", 4);
        _scheduleLineNameEdit = new LineEdit
        {
            PlaceholderText = "Nome da nova Linha (ex: Linha 01 — Minério Sul)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _scheduleLineNameEdit.AddThemeFontSizeOverride("font_size", 11);
        lineCreateRow.AddChild(_scheduleLineNameEdit);

        var createLineBtn = new Button { Text = "💾 Nova Linha" };
        createLineBtn.AddThemeFontSizeOverride("font_size", 11);
        createLineBtn.Pressed += () =>
        {
            if (_draftSchedule.Count == 0)
            {
                _audio?.PlayError();
                LogEvent("⚠️ Adicione ao menos 1 parada na agenda antes de salvar uma Linha.");
                return;
            }
            Truck? truck = _bridge.Engine.ActiveTrucks.Count > 0
                ? _bridge.Engine.ActiveTrucks[Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1)]
                : null;
            string lineName = _scheduleLineNameEdit?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(lineName))
                lineName = $"Linha {_bridge.Engine.LogisticsLines.Count + 1:D2}";
            var created = _bridge.CreateAndAssignLogisticsLine(lineName, _draftSchedule, truck);
            if (_scheduleLineNameEdit != null) _scheduleLineNameEdit.Text = string.Empty;
            SyncScheduleSelectorsToCurrentTruck();
            _audio?.PlayStructureBuilt();
            LogEvent($"💾 Linha compartilhada [b]{created.Name}[/b] criada ({created.Stops.Count} paradas)!");
            RefreshHud();
        };
        lineCreateRow.AddChild(createLineBtn);
        schedBox.AddChild(lineCreateRow);

        var lineAutoRow = new HBoxContainer();
        lineAutoRow.AddThemeConstantOverride("separation", 6);
        void ApplyLineAutomationFromChecks()
        {
            int selLineIdx = _scheduleLineSelect.Selected - 1;
            if (selLineIdx < 0 || selLineIdx >= _bridge.Engine.LogisticsLines.Count)
                return;
            var line = _bridge.Engine.LogisticsLines[selLineIdx];
            _bridge.UpdateLineAutomation(
                line.Id,
                _lineHeadwayCheck.ButtonPressed,
                line.MinHeadwayHours > 0f ? line.MinHeadwayHours : 4.0f,
                _lineAutoReplaceCheck.ButtonPressed,
                line.AutoReplaceMinCondition > 0f ? line.AutoReplaceMinCondition : 25f,
                line.AutoReplaceMaxMileageKm,
                line.AutoReplaceTargetSpecName);
            RefreshHud();
        }

        _lineHeadwayCheck = new CheckBox
        {
            Text = "⏱️ Headway (4h)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Mantém espaçamento mínimo de 4h entre saídas da parada inicial desta Linha"
        };
        _lineHeadwayCheck.AddThemeFontSizeOverride("font_size", 10);
        _lineHeadwayCheck.Toggled += _ => ApplyLineAutomationFromChecks();
        lineAutoRow.AddChild(_lineHeadwayCheck);

        _lineAutoReplaceCheck = new CheckBox
        {
            Text = "♻️ Auto-Trocar ≤25%",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Substitui automaticamente caminhões da Linha quando vazios e com condição <= 25%"
        };
        _lineAutoReplaceCheck.AddThemeFontSizeOverride("font_size", 10);
        _lineAutoReplaceCheck.Toggled += _ => ApplyLineAutomationFromChecks();
        lineAutoRow.AddChild(_lineAutoReplaceCheck);
        schedBox.AddChild(lineAutoRow);

        bodyMargin.AddChild(schedBox);

        // Aba 3: Pesquisa & Tecnologia (Research Management estilo Voxel Tycoon)
        var researchBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        researchBox.AddThemeConstantOverride("separation", 6);
        _tabBodyResearch = researchBox;

        researchBox.AddChild(CreateLabel("LABORATÓRIO DE P&D EM TEMPO REAL", new Color("4b5563"), 11));
        var activeRow = new HBoxContainer();
        activeRow.AddThemeConstantOverride("separation", 6);
        _researchActiveLabel = CreateLabel("Nenhuma pesquisa ativa.", new Color("1f2937"), 11);
        _researchActiveLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        activeRow.AddChild(_researchActiveLabel);

        _researchPauseBtn = new Button { Text = "⏸ Pausar" };
        _researchPauseBtn.AddThemeFontSizeOverride("font_size", 11);
        _researchPauseBtn.Pressed += () =>
        {
            _bridge.PauseResearch();
            LogEvent("⏸️ Pesquisa pausada (progresso preservado).");
            RefreshHud();
        };
        activeRow.AddChild(_researchPauseBtn);
        researchBox.AddChild(activeRow);

        _researchActiveProgressBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 100,
            Value = 0,
            CustomMinimumSize = new Vector2(0, 16)
        };
        researchBox.AddChild(_researchActiveProgressBar);

        var researchScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var researchList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        researchList.AddThemeConstantOverride("separation", 6);
        researchScroll.AddChild(researchList);
        researchBox.AddChild(researchScroll);

        _researchRows.Clear();
        foreach (var node in _bridge.Engine.Research.Nodes)
        {
            var card = new PanelContainer();
            var cardStyle = new StyleBoxFlat
            {
                BgColor = new Color("ffffff"),
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                BorderColor = new Color("cbd5e1"),
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            };
            card.AddThemeStyleboxOverride("panel", cardStyle);

            var cardCol = new VBoxContainer();
            cardCol.AddThemeConstantOverride("separation", 3);

            var topRow = new HBoxContainer();
            var titleLbl = CreateLabel(node.Name, new Color("111827"), 12);
            titleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var statusLbl = CreateLabel("Disponível", new Color("475569"), 10);
            topRow.AddChild(titleLbl);
            topRow.AddChild(statusLbl);
            cardCol.AddChild(topRow);

            string prereqNote = node.PrerequisiteId.HasValue
                ? $" (Req: {_bridge.Engine.Research.GetNode(node.PrerequisiteId.Value).Name})"
                : string.Empty;
            var descLbl = new Label
            {
                Text = $"{node.Description}{prereqNote}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            descLbl.AddThemeColorOverride("font_color", new Color("4b5563"));
            descLbl.AddThemeFontSizeOverride("font_size", 10);
            cardCol.AddChild(descLbl);

            var botRow = new HBoxContainer();
            botRow.AddThemeConstantOverride("separation", 6);
            var bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 0,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 14)
            };
            botRow.AddChild(bar);

            var techId = node.Id;
            var actionBtn = new Button { Text = $"🔬 R$ {node.FundingCost:N0}" };
            actionBtn.AddThemeFontSizeOverride("font_size", 11);
            actionBtn.Pressed += () =>
            {
                if (_bridge.TryStartResearch(techId, out string msg))
                    _audio?.PlayUiClick();
                else
                    _audio?.PlayError();
                LogEvent(msg);
                RefreshHud();
            };
            botRow.AddChild(actionBtn);
            cardCol.AddChild(botRow);

            card.AddChild(cardCol);
            researchList.AddChild(card);
            _researchRows[techId] = (statusLbl, bar, actionBtn);
        }

        bodyMargin.AddChild(researchBox);

        // Aba 4: Concessionária de Frota (Compra + Showroom 3D & Pintura + Frota Ativa)
        var fleetBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        fleetBox.AddThemeConstantOverride("separation", 5);
        _tabBodyFleet = fleetBox;

        var fleetSubTabRow = new HBoxContainer();
        fleetSubTabRow.AddThemeConstantOverride("separation", 4);
        _fleetSubTabBuyBtn = new Button { Text = "🛒 Comprar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fleetSubTabBuyBtn.AddThemeFontSizeOverride("font_size", 11);
        _fleetSubTabBuyBtn.Pressed += () => SwitchFleetSubTab(0);
        _fleetSubTabShowcaseBtn = new Button { Text = "🎨 Showroom 3D & Pintura", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fleetSubTabShowcaseBtn.AddThemeFontSizeOverride("font_size", 11);
        _fleetSubTabShowcaseBtn.Pressed += () => SwitchFleetSubTab(1);
        _fleetSubTabActiveBtn = new Button { Text = "🚛 Frota Ativa", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fleetSubTabActiveBtn.AddThemeFontSizeOverride("font_size", 11);
        _fleetSubTabActiveBtn.Pressed += () => SwitchFleetSubTab(2);
        fleetSubTabRow.AddChild(_fleetSubTabBuyBtn);
        fleetSubTabRow.AddChild(_fleetSubTabShowcaseBtn);
        fleetSubTabRow.AddChild(_fleetSubTabActiveBtn);
        fleetBox.AddChild(fleetSubTabRow);

        // Sub-aba 0: Concessionária & Modernização
        var fleetBuyBody = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        fleetBuyBody.AddThemeConstantOverride("separation", 5);
        _fleetSubBodyBuy = fleetBuyBody;
        fleetBuyBody.AddChild(CreateLabel("COMPRAR NOVO VEÍCULO", new Color("4b5563"), 11));

        _fleetBuyButtons.Clear();
        for (int i = 0; i < ProgressionManager.AvailableTruckModels.Count; i++)
        {
            int modelIdx = i;
            var spec = ProgressionManager.AvailableTruckModels[i];
            var buyRow = new HBoxContainer();
            buyRow.AddThemeConstantOverride("separation", 4);

            var btn = new Button
            {
                Text = $"{spec.Name}  •  R$ {spec.PurchasePrice:N0}  (Cap {spec.Capacity} | Nv.{spec.RequiredPlayerLevel})",
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += () =>
            {
                if (_bridge.TryBuyAndDispatchTruck(modelIdx, out string msg) != null && _bridge.Engine.ActiveTrucks.Count > 0)
                {
                    _selectedTruckIndex = _bridge.Engine.ActiveTrucks.Count - 1;
                    SyncScheduleTruckDropdown(forceRebuild: true);
                    SyncScheduleSelectorsToCurrentTruck();
                    _audio?.PlayStructureBuilt();
                }
                else
                {
                    _audio?.PlayError();
                }
                LogEvent(msg);
                RefreshHud();
            };
            _fleetBuyButtons.Add((modelIdx, btn));
            buyRow.AddChild(btn);

            var previewBtn = new Button { Text = "🔍 3D", TooltipText = $"Inspecionar {spec.Name} no Showroom 3D" };
            previewBtn.AddThemeFontSizeOverride("font_size", 10);
            previewBtn.Pressed += () =>
            {
                EnsureShowcaseBuilt();
                _showcaseModelSelect.Select(modelIdx);
                _showcaseRenderedKey = string.Empty;
                SwitchFleetSubTab(1);
            };
            buyRow.AddChild(previewBtn);
            fleetBuyBody.AddChild(buyRow);
        }

        fleetBuyBody.AddChild(CreateLabel("MODERNIZAR FROTA SELECIONADA (TROCA +60%)", new Color("4b5563"), 11));
        _modernizerSourceLabel = CreateLabel("Selecione um veículo na frota ativa.", new Color("1f2937"), 11);
        fleetBuyBody.AddChild(_modernizerSourceLabel);

        var modRow = new HBoxContainer();
        modRow.AddThemeConstantOverride("separation", 6);
        _modernizerTargetSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _modernizerTargetSelect.AddThemeFontSizeOverride("font_size", 11);
        modRow.AddChild(_modernizerTargetSelect);

        _modernizerApplyBtn = new Button { Text = "🔄 Modernizar" };
        _modernizerApplyBtn.AddThemeFontSizeOverride("font_size", 11);
        _modernizerApplyBtn.Pressed += () =>
        {
            var trucks = _bridge.Engine.ActiveTrucks;
            if (trucks.Count == 0) return;
            _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, trucks.Count - 1);
            var oldTruck = trucks[_selectedTruckIndex];
            int modelIdx = _modernizerTargetSelect.Selected;
            if (modelIdx < 0 || modelIdx >= ProgressionManager.AvailableTruckModels.Count) return;

            var targetSpec = ProgressionManager.AvailableTruckModels[modelIdx];
            if (_bridge.TryModernizeTruck(oldTruck, targetSpec, out var upgraded, out string msg) && upgraded != null)
            {
                SyncScheduleTruckDropdown(forceRebuild: true);
                SyncScheduleSelectorsToCurrentTruck();
                _audio?.PlayStructureBuilt();
            }
            else
            {
                _audio?.PlayError();
            }
            LogEvent(msg);
            RefreshHud();
        };
        modRow.AddChild(_modernizerApplyBtn);
        fleetBuyBody.AddChild(modRow);
        fleetBox.AddChild(fleetBuyBody);

        // Sub-aba 1: Showroom 3D Interativo & Oficina de Pintura (construído sob demanda para abrir rápido)
        var showcaseBody = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        showcaseBody.AddThemeConstantOverride("separation", 4);
        _fleetSubBodyShowcase = showcaseBody;
        fleetBox.AddChild(showcaseBody);

        // Sub-aba 2: Frota Ativa & Veterania
        var activeBody = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        activeBody.AddThemeConstantOverride("separation", 4);
        _fleetSubBodyActive = activeBody;
        activeBody.AddChild(CreateLabel("FROTA ATIVA & VETERANIA (CLIQUE P/ AGENDA)", new Color("4b5563"), 11));
        var fleetScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _fleetActiveListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fleetActiveListBox.AddThemeConstantOverride("separation", 3);
        fleetScroll.AddChild(_fleetActiveListBox);
        activeBody.AddChild(fleetScroll);
        fleetBox.AddChild(activeBody);

        SwitchFleetSubTab(0);
        bodyMargin.AddChild(fleetBox);

        // Aba 5: Contratos & Expansão Regional
        var contractsBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        contractsBox.AddThemeConstantOverride("separation", 6);
        _tabBodyContracts = contractsBox;

        var regRow = new HBoxContainer();
        regRow.AddThemeConstantOverride("separation", 6);
        _regionalStatusLabel = CreateLabel("🗺️ Regiões: 1/5", new Color("1f2937"), 11);
        _regionalStatusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        regRow.AddChild(_regionalStatusLabel);

        _exploreRegionBtn = new Button { Text = "🧭 Explorar Região" };
        _exploreRegionBtn.AddThemeFontSizeOverride("font_size", 11);
        _exploreRegionBtn.Pressed += () =>
        {
            if (_bridge.TryExploreNewRegion(out _, out string msg))
                _audio?.PlayStructureBuilt();
            else
                _audio?.PlayError();
            LogEvent(msg);
            RefreshHud();
        };
        regRow.AddChild(_exploreRegionBtn);
        contractsBox.AddChild(regRow);

        _auctionBidRow = new HBoxContainer { Visible = false };
        _auctionBidRow.AddThemeConstantOverride("separation", 6);
        _auctionTariffBidBtn = new Button { Text = "🏷️ Lance Tarifa (85%)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _auctionTariffBidBtn.AddThemeFontSizeOverride("font_size", 11);
        _auctionTariffBidBtn.Pressed += () =>
        {
            var openAuction = _bridge.Engine.ActiveContracts.FirstOrDefault(c => c.IsAuctionOpen);
            if (openAuction == null) return;
            float nextMult = openAuction.HasPlayerBid
                ? MathF.Max(0.65f, openAuction.PlayerBidFreightMultiplier - 0.05f)
                : MathF.Max(0.75f, openAuction.RivalBidFreightMultiplier - 0.06f);
            float nextDur = MathF.Min(openAuction.BaseDurationHours, openAuction.RivalBidDurationHours);
            if (_bridge.PlacePlayerAuctionBid(openAuction.Id, nextMult, nextDur, out string bidMsg))
                _audio?.PlayNotification(celebratory: true);
            else
                _audio?.PlayError();
            LogEvent(bidMsg);
            RefreshHud();
        };
        _auctionBidRow.AddChild(_auctionTariffBidBtn);

        _auctionExpressBidBtn = new Button { Text = "⚡ Lance Expresso (70% Prazo)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _auctionExpressBidBtn.AddThemeFontSizeOverride("font_size", 11);
        _auctionExpressBidBtn.Pressed += () =>
        {
            var openAuction = _bridge.Engine.ActiveContracts.FirstOrDefault(c => c.IsAuctionOpen);
            if (openAuction == null) return;
            float nextMult = MathF.Min(0.92f, openAuction.RivalBidFreightMultiplier);
            float nextDur = MathF.Max(48f, MathF.Round(openAuction.BaseDurationHours * 0.70f));
            if (_bridge.PlacePlayerAuctionBid(openAuction.Id, nextMult, nextDur, out string bidMsg))
                _audio?.PlayNotification(celebratory: true);
            else
                _audio?.PlayError();
            LogEvent(bidMsg);
            RefreshHud();
        };
        _auctionBidRow.AddChild(_auctionExpressBidBtn);
        contractsBox.AddChild(_auctionBidRow);

        _contractsText = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _contractsText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _contractsText.AddThemeFontSizeOverride("normal_font_size", 12);
        contractsBox.AddChild(_contractsText);
        bodyMargin.AddChild(contractsBox);

        // Aba 6: Banco Central & Fechamento Financeiro (DRE)
        var bankBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        bankBox.AddThemeConstantOverride("separation", 6);
        _tabBodyBank = bankBox;

        _bankSummaryLabel = CreateLabel("Crédito: R$ 50.000 | Dívida: R$ 0 (Juros: 2% a.m.)", new Color("1f2937"), 11);
        bankBox.AddChild(_bankSummaryLabel);

        var loanRow = new HBoxContainer();
        loanRow.AddThemeConstantOverride("separation", 6);
        var loan15Btn = new Button { Text = "💵 +R$ 15.000", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        loan15Btn.AddThemeFontSizeOverride("font_size", 11);
        loan15Btn.Pressed += () =>
        {
            if (_bridge.Engine.Player.TakeLoan(15000))
            {
                _audio?.PlayCashRegister(15000);
                LogEvent("🏦 Empréstimo de R$ 15.000 creditado!");
            }
            else
            {
                _audio?.PlayError();
                LogEvent("⚠️ Limite de crédito atingido para seu Nível.");
            }
            RefreshHud();
        };
        loanRow.AddChild(loan15Btn);

        var loan50Btn = new Button { Text = "💰 +R$ 50.000", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        loan50Btn.AddThemeFontSizeOverride("font_size", 11);
        loan50Btn.Pressed += () =>
        {
            if (_bridge.Engine.Player.TakeLoan(50000))
            {
                _audio?.PlayCashRegister(50000);
                LogEvent("🏦 Empréstimo de expansão (R$ 50.000) creditado!");
            }
            else
            {
                _audio?.PlayError();
                LogEvent("⚠️ Limite de crédito insuficiente (evolua seu Nível).");
            }
            RefreshHud();
        };
        loanRow.AddChild(loan50Btn);
        bankBox.AddChild(loanRow);

        var repayRow = new HBoxContainer();
        repayRow.AddThemeConstantOverride("separation", 6);
        var repay10Btn = new Button { Text = "💳 Amortizar -R$ 10.000", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        repay10Btn.AddThemeFontSizeOverride("font_size", 11);
        repay10Btn.Pressed += () =>
        {
            if (_bridge.Engine.Player.RepayLoan(10000))
            {
                _audio?.PlayUiClick();
                LogEvent("💳 Dívida amortizada (-R$ 10.000)!");
            }
            else
            {
                _audio?.PlayError();
                LogEvent("⚠️ Sem dívida ativa ou saldo insuficiente.");
            }
            RefreshHud();
        };
        repayRow.AddChild(repay10Btn);

        var repayAllBtn = new Button { Text = "✅ Quitar Tudo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        repayAllBtn.AddThemeFontSizeOverride("font_size", 11);
        repayAllBtn.Pressed += () =>
        {
            long debt = _bridge.Engine.Player.LoanDebt;
            if (debt > 0 && _bridge.Engine.Player.RepayLoan(debt))
            {
                _audio?.PlayNotification(celebratory: true);
                LogEvent($"✅ Operação bancária: Dívida amortizada em até R$ {debt:N0}!");
            }
            else
            {
                _audio?.PlayError();
                LogEvent("⚠️ Sem dívida ativa ou saldo insuficiente.");
            }
            RefreshHud();
        };
        repayRow.AddChild(repayAllBtn);
        bankBox.AddChild(repayRow);

        var maintRow = new HBoxContainer();
        maintRow.AddThemeConstantOverride("separation", 6);
        _autoRoadMaintToggleBtn = new Button { Text = "🛠️ Conservação Auto: ON", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _autoRoadMaintToggleBtn.AddThemeFontSizeOverride("font_size", 11);
        _autoRoadMaintToggleBtn.Pressed += () =>
        {
            _bridge.Engine.AutoRoadMaintenanceEnabled = !_bridge.Engine.AutoRoadMaintenanceEnabled;
            string st = _bridge.Engine.AutoRoadMaintenanceEnabled ? "ATIVADA" : "DESATIVADA";
            LogEvent($"🛠️ Conservação Viária Automática mensal {st}.");
            RefreshHud();
        };
        maintRow.AddChild(_autoRoadMaintToggleBtn);

        var repaveNowBtn = new Button { Text = "🛣️ Conservar Agora", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        repaveNowBtn.AddThemeFontSizeOverride("font_size", 11);
        repaveNowBtn.Pressed += () =>
        {
            int fixedCount = _bridge.Engine.RunAutoRoadMaintenance(out long totalCost);
            if (fixedCount > 0)
            {
                _audio?.PlayStructureBuilt();
                LogEvent($"🛠️ Conservação preventiva aplicada em {fixedCount} segmento(s) (-R$ {totalCost:N0}).");
            }
            else
            {
                LogEvent("🛣️ Toda a malha viária já está acima de 65% de integridade.");
            }
            RefreshHud();
        };
        maintRow.AddChild(repaveNowBtn);
        bankBox.AddChild(maintRow);

        var dreHeaderRow = new HBoxContainer();
        dreHeaderRow.AddThemeConstantOverride("separation", 6);
        var dreTitleLbl = CreateLabel("FECHAMENTO FINANCEIRO (DRE)", new Color("4b5563"), 11);
        dreTitleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        dreHeaderRow.AddChild(dreTitleLbl);

        _drePeriodToggleBtn = new Button { Text = "📅 Mês Atual" };
        _drePeriodToggleBtn.AddThemeFontSizeOverride("font_size", 11);
        _drePeriodToggleBtn.Pressed += () =>
        {
            _dreShowLifetime = !_dreShowLifetime;
            _drePeriodToggleBtn.Text = _dreShowLifetime ? "📊 Acumulado" : "📅 Mês Atual";
            RefreshBankAndDreTab();
        };
        dreHeaderRow.AddChild(_drePeriodToggleBtn);
        bankBox.AddChild(dreHeaderRow);

        _dreChart = new DreChartControl();
        bankBox.AddChild(_dreChart);

        _dreReportText = new RichTextLabel
        {
            BbcodeEnabled = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _dreReportText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _dreReportText.AddThemeFontSizeOverride("normal_font_size", 11);
        _dreReportText.AddThemeFontSizeOverride("bold_font_size", 11);
        bankBox.AddChild(_dreReportText);
        bodyMargin.AddChild(bankBox);

        // Aba 6b: Operações & Inteligência (Diagnóstico de Gargalos + Conquistas & Marcos)
        BuildOperationsTab(bodyMargin);

        // Aba 7: Log
        var logBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _tabBodyLog = logBox;
        _eventLogText = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _eventLogText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _eventLogText.AddThemeFontSizeOverride("normal_font_size", 11);
        logBox.AddChild(_eventLogText);
        bodyMargin.AddChild(logBox);
        BuildRailTabBody(bodyMargin);

        // ---------------------------------------------------------------------
        // 3. BOTTOM-LEFT CORNER PILL (Saldo, Nível, Botão Abrir Janela)
        // ---------------------------------------------------------------------
        var bottomLeftPill = new PanelContainer
        {
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 14,
            OffsetRight = 250,
            OffsetTop = -56,
            OffsetBottom = -12
        };
        bottomLeftPill.AddThemeStyleboxOverride("panel", darkPillStyle);
        _hudRegularLayer.AddChild(bottomLeftPill);

        var blHBox = new HBoxContainer();
        blHBox.AddThemeConstantOverride("separation", 10);
        bottomLeftPill.AddChild(blHBox);

        var blTextCol = new VBoxContainer();
        blTextCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bottomBalanceLabel = CreateLabel("R$ 30.000", new Color("4ade80"), 14);
        _bottomLevelLabel = CreateLabel("Nv.1 • 0/1000 XP", new Color("cbd5e1"), 11);
        blTextCol.AddChild(_bottomBalanceLabel);
        blTextCol.AddChild(_bottomLevelLabel);
        blHBox.AddChild(blTextCol);

        AddPillButton(blHBox, "☰ Painel", () =>
        {
            if (_topLeftWindow.Visible)
                _topLeftWindow.Visible = false;
            else
                OpenWindowOnTab(_activeTab);
        });

        // ---------------------------------------------------------------------
        // 4. BOTTOM-CENTER FLOATING TOOLBAR PILL (2 Linhas Compactas Responsivas)
        // ---------------------------------------------------------------------
        var bottomCenterPill = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -335,
            OffsetRight = 335,
            OffsetTop = -84,
            OffsetBottom = -10
        };
        bottomCenterPill.AddThemeStyleboxOverride("panel", darkPillStyle);
        _hudRegularLayer.AddChild(bottomCenterPill);

        var bcCol = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bcCol.AddThemeConstantOverride("separation", 4);
        bottomCenterPill.AddChild(bcCol);

        var bcRow1 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bcRow1.AddThemeConstantOverride("separation", 5);
        var bcRow2 = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bcRow2.AddThemeConstantOverride("separation", 5);
        bcCol.AddChild(bcRow1);
        bcCol.AddChild(bcRow2);

        _toolButtons[ToolMode.Inspect] = AddPillButton(bcRow1, "🔍 [Q]", () => SetActiveTool(ToolMode.Inspect));
        _toolButtons[ToolMode.BuildRoad] = AddPillButton(bcRow1, "🌉 Via [B]", () => SetActiveTool(ToolMode.BuildRoad));
        _toolButtons[ToolMode.Terraform] = AddPillButton(bcRow1, "⛏️ Terrapl.", () => SetActiveTool(ToolMode.Terraform));
        _toolButtons[ToolMode.Terraform].TooltipText = "Terraplenagem: nivela Florestas (R$ 75), Colinas (R$ 120) e Montanhas (R$ 220)";
        _toolButtons[ToolMode.UpgradeGravel] = AddPillButton(bcRow1, "🪨 Cascalho", () => SetActiveTool(ToolMode.UpgradeGravel));
        _toolButtons[ToolMode.UpgradeAsphalt] = AddPillButton(bcRow1, "🛣️ Asfalto", () => SetActiveTool(ToolMode.UpgradeAsphalt));
        _toolButtons[ToolMode.UpgradeHighway] = AddPillButton(bcRow1, "⚡ Rodovia", () => SetActiveTool(ToolMode.UpgradeHighway));
        _toolButtons[ToolMode.UpgradeDualCarriage] = AddPillButton(bcRow1, "🚧 Dupla", () => SetActiveTool(ToolMode.UpgradeDualCarriage));
        _corridorBatchBtn = AddPillButton(bcRow1, "Lote -10%", () =>
        {
            _corridorBatchMode = !_corridorBatchMode;
            RefreshHud();
        });

        _toolButtons[ToolMode.BuildGarage] = AddPillButton(bcRow2, "🔧 Garag.", () => SetActiveTool(ToolMode.BuildGarage));
        _toolButtons[ToolMode.BuildLogisticsHub] = AddPillButton(bcRow2, "📦 Hub", () => SetActiveTool(ToolMode.BuildLogisticsHub));
        _toolButtons[ToolMode.BuildBusTerminal] = AddPillButton(bcRow2, "🚌 Rodov.", () => SetActiveTool(ToolMode.BuildBusTerminal));
        _toolButtons[ToolMode.BuildTollStation] = AddPillButton(bcRow2, "🚧 Pedágio", () => SetActiveTool(ToolMode.BuildTollStation));
        _toolButtons[ToolMode.BuildRail] = AddPillButton(bcRow2, "🚂 Trilho [R]", () => SetActiveTool(ToolMode.BuildRail));
        _toolButtons[ToolMode.BuildRailSignal] = AddPillButton(bcRow2, "🚦 Sinal", () => SetActiveTool(ToolMode.BuildRailSignal));
        _toolButtons[ToolMode.BuildIntermodalYard] = AddPillButton(bcRow2, "🏗️ Pátio", () => SetActiveTool(ToolMode.BuildIntermodalYard));
        _toolButtons[ToolMode.Demolish] = AddPillButton(bcRow2, "🚜 Dem. [X]", () => SetActiveTool(ToolMode.Demolish));
        _toolButtons[ToolMode.Demolish].TooltipText = "Bulldozer [X]: demole estradas, trilhos, sinaleiros, garagens, hubs ou pátios (+40% reembolso)";

        AddPillButton(bcRow2, "🚛 Frota", () => OpenWindowOnTab(WindowTab.Fleet));
        AddPillButton(bcRow2, "🚆 Trens", () => OpenWindowOnTab(WindowTab.Rail));
        AddPillButton(bcRow2, "🏦 Banco", () => OpenWindowOnTab(WindowTab.Bank));

        // ---------------------------------------------------------------------
        // 5. BOTTOM-RIGHT CORNER PILL (Velocidade, Áudio, Data e Clima)
        // ---------------------------------------------------------------------
        var bottomRightPill = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -395,
            OffsetRight = -14,
            OffsetTop = -52,
            OffsetBottom = -12
        };
        bottomRightPill.AddThemeStyleboxOverride("panel", darkPillStyle);
        _hudRegularLayer.AddChild(bottomRightPill);

        var brHBox = new HBoxContainer();
        brHBox.AddThemeConstantOverride("separation", 6);
        bottomRightPill.AddChild(brHBox);

        _speedButtons[0f] = AddPillButton(brHBox, "⏸", () => SetSimulationSpeed(0f));
        _speedButtons[1f] = AddPillButton(brHBox, "▶", () => SetSimulationSpeed(1f));
        _speedButtons[2.5f] = AddPillButton(brHBox, "⏩", () => SetSimulationSpeed(2.5f));
        _speedButtons[6f] = AddPillButton(brHBox, "🚀", () => SetSimulationSpeed(6f));

        _muteAudioBtn = AddPillButton(brHBox, _audio != null && _audio.IsMuted ? "🔇" : "🔊", ToggleAudioMute);
        _pipToggleBtn = AddPillButton(brHBox, "📺 PiP", TogglePipCamera);
        _pipToggleBtn.TooltipText = "Câmera Picture-in-Picture do Caminhão (Tecla P)";

        _bottomWeatherLabel = CreateLabel("☀️", new Color("facc15"), 13);
        brHBox.AddChild(_bottomWeatherLabel);

        _bottomDateLabel = CreateLabel("05/11/2026 08:00", new Color("f8fafc"), 12);
        brHBox.AddChild(_bottomDateLabel);

        // ---------------------------------------------------------------------
        // 6. TOP-RIGHT MINIMAP PILL (Minimapa Estratégico Clicável)
        // ---------------------------------------------------------------------
        var minimapPill = new PanelContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            GrowHorizontal = Control.GrowDirection.Begin,
            OffsetLeft = -236,
            OffsetRight = -14,
            OffsetTop = 54,
            OffsetBottom = 282
        };
        minimapPill.AddThemeStyleboxOverride("panel", darkPillStyle);
        _hudRegularLayer.AddChild(minimapPill);

        var mmVBox = new VBoxContainer();
        mmVBox.AddThemeConstantOverride("separation", 4);
        minimapPill.AddChild(mmVBox);

        var mmHeader = new HBoxContainer();
        var mmTitle = CreateLabel("🗺️ MINIMAPA", new Color("cbd5e1"), 11);
        mmTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mmHeader.AddChild(mmTitle);

        var mmToggleBtn = new Button { Text = "–" };
        mmToggleBtn.AddThemeFontSizeOverride("font_size", 11);
        mmHeader.AddChild(mmToggleBtn);
        mmVBox.AddChild(mmHeader);

        var mmLayerRow = new HBoxContainer();
        mmLayerRow.AddThemeConstantOverride("separation", 3);
        _overlayStandardBtn = AddPillButton(mmLayerRow, "🗺️", () => SetMapOverlayMode(MapOverlayMode.Standard));
        _overlayStandardBtn.TooltipText = "Mapa Padrão";
        _overlayStandardBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _overlayTrafficBtn = AddPillButton(mmLayerRow, "🚦 Tráfego", () => SetMapOverlayMode(MapOverlayMode.Traffic));
        _overlayTrafficBtn.TooltipText = "Heatmap de Congestionamento";
        _overlayTrafficBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _overlayPavementBtn = AddPillButton(mmLayerRow, "🛣️ Via", () => SetMapOverlayMode(MapOverlayMode.Pavement));
        _overlayPavementBtn.TooltipText = "Camada de Pavimentação";
        _overlayPavementBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _overlayProfitBtn = AddPillButton(mmLayerRow, "💹", () => SetMapOverlayMode(MapOverlayMode.Profitability));
        _overlayProfitBtn.TooltipText = "Rentabilidade por Corredor (30 dias)";
        _overlayProfitBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _overlayStandardBtn.AddThemeStyleboxOverride("normal", _pillBtnActive);
        mmVBox.AddChild(mmLayerRow);

        _minimapOverlay = new MinimapOverlay();
        _minimapOverlay.OnNavigateToCell += (gx, gy) =>
        {
            _followedTruckIndex = -1;
            SmoothMoveCameraPivotTo(GridToWorld(gx, gy));
            _selectedCell = (gx, gy);
            RefreshMinimapOverlay();
            UpdateInspectorPanel();
        };
        mmVBox.AddChild(_minimapOverlay);

        mmToggleBtn.Pressed += () =>
        {
            _minimapOverlay.Visible = !_minimapOverlay.Visible;
            mmToggleBtn.Text = _minimapOverlay.Visible ? "–" : "+";
            minimapPill.OffsetBottom = _minimapOverlay.Visible ? 282 : 116;
        };

        _minimapOverlay.RebuildStaticMap(_bridge);
        _minimapStaticDirty = false;

        BuildPipWindow(_hudRegularLayer, darkPillStyle);
        BuildTutorialPanel(_hudRegularLayer, whiteToastStyle);
        BuildSavesAndSeedModal(_hudRegularLayer, windowOuterStyle);
        BuildMainMenuOverlay(canvas);
        BuildGraphicsSettingsModal(canvas, windowOuterStyle);
        BuildPhotoModeBar(canvas, darkPillStyle);

        RebuildSchedulePools();
        OpenWindowOnTab(WindowTab.Inspector);
    }

    private void BuildGraphicsSettingsModal(Node canvas, StyleBoxFlat windowOuterStyle)
    {
        _graphicsModal = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -285,
            OffsetRight = 285,
            OffsetTop = -265,
            OffsetBottom = 265
        };
        _graphicsModal.AddThemeStyleboxOverride("panel", windowOuterStyle);
        canvas.AddChild(_graphicsModal);

        var rootCol = new VBoxContainer();
        rootCol.AddThemeConstantOverride("separation", 0);
        _graphicsModal.AddChild(rootCol);

        var hdrPanel = new PanelContainer();
        hdrPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("22252b"),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        });
        var hdrRow = new HBoxContainer();
        var titleLbl = CreateLabel("🎨 Qualidade Gráfica & Atmosfera Diorama (Atalho: G)", new Color("f8fafc"), 13);
        titleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hdrRow.AddChild(titleLbl);
        AddPillButton(hdrRow, "✕", ToggleGraphicsModal);
        hdrPanel.AddChild(hdrRow);
        rootCol.AddChild(hdrPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rootCol.AddChild(margin);

        var bodyCol = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        bodyCol.AddThemeConstantOverride("separation", 8);
        margin.AddChild(bodyCol);

        bodyCol.AddChild(CreateLabel("PRESETS DE QUALIDADE (1 CLIQUE)", new Color("4b5563"), 11));
        var presetRow = new HBoxContainer();
        presetRow.AddThemeConstantOverride("separation", 6);
        _presetPerfBtn = AddPillButton(presetRow, "⚡ Desempenho", () => ApplyGraphicsPreset(GraphicsQualityPreset.Performance));
        _presetBalBtn = AddPillButton(presetRow, "⚖️ Equilibrado", () => ApplyGraphicsPreset(GraphicsQualityPreset.Balanced));
        _presetUltraBtn = AddPillButton(presetRow, "✨ Diorama Ultra", () => ApplyGraphicsPreset(GraphicsQualityPreset.DioramaUltra));
        _presetPerfBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _presetBalBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _presetUltraBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bodyCol.AddChild(presetRow);

        bodyCol.AddChild(CreateLabel("PÓS-PROCESSAMENTO & SOMBRAS CUSTOMIZÁVEIS", new Color("4b5563"), 11));

        CheckBox CreateGraphicToggle(string text, bool initial, Action<bool> onToggled)
        {
            var cb = new CheckBox
            {
                Text = text,
                ButtonPressed = initial
            };
            cb.AddThemeFontSizeOverride("font_size", 11);
            cb.AddThemeColorOverride("font_color", new Color("1f2937"));
            cb.AddThemeColorOverride("font_pressed_color", new Color("111827"));
            cb.AddThemeColorOverride("font_hover_color", new Color("111827"));
            cb.Toggled += val =>
            {
                onToggled(val);
                PersistClientSettings();
                RefreshGraphicsModalUi();
            };
            bodyCol.AddChild(cb);
            return cb;
        }

        _chkSsao = CreateGraphicToggle("Oclusão de Ambiente (SSAO — Sombras de contato nos voxels e pneus)", _userSsaoEnabled, v =>
        {
            _userSsaoEnabled = v;
            _worldEnv.SsaoEnabled = v;
            if (v)
            {
                GetViewport().Msaa3D = Viewport.Msaa.Disabled;
                GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
            }
            else if (_graphicsPreset == GraphicsQualityPreset.Performance)
            {
                GetViewport().Msaa3D = Viewport.Msaa.Msaa2X;
            }
            ApplyZoomPerformanceLod(force: true);
            RefreshGraphicsModalUi();
        });
        _chkGlow = CreateGraphicToggle("Bloom Cinematográfico (Glow — Faróis, lanternas e janelas noturnas)", _worldEnv.GlowEnabled, v => _worldEnv.GlowEnabled = v);
        _chkSsr = CreateGraphicToggle("Reflexos em Espaço de Tela (SSR — Poças de chuva e água)", _userSsrEnabled, v =>
        {
            _userSsrEnabled = v;
            _worldEnv.SsrEnabled = v;
            ApplyZoomPerformanceLod(force: true);
        });
        _chkTiltShift = CreateGraphicToggle("Efeito Maquete Tilt-Shift (Depth of Field dinâmico com o zoom)", _tiltShiftEnabled, v =>
        {
            _tiltShiftEnabled = v;
            UpdateCameraTransform();
        });
        _chkShadows4Splits = CreateGraphicToggle("Sombras Solares Alta Definição (Parallel 4-Splits 120m+)", _userShadows4Splits, v =>
        {
            _userShadows4Splits = v;
            _userShadowMaxDistance = v ? 125.0f : 85.0f;
            _sunLight.DirectionalShadowMode = v ? DirectionalLight3D.ShadowMode.Parallel4Splits : DirectionalLight3D.ShadowMode.Parallel2Splits;
            _sunLight.DirectionalShadowMaxDistance = _userShadowMaxDistance;
            ApplyZoomPerformanceLod(force: true);
        });
        _chkDynamicSun = CreateGraphicToggle("Ciclo Solar 24h Dinâmico (Amanhecer Dourado / Pôr do Sol / Noite)", _dynamicSolarCycleEnabled, v => _dynamicSolarCycleEnabled = v);
        _chkVsync = CreateGraphicToggle("Sincronização Vertical (VSync — Estabiliza FPS no monitor)", _userVsyncEnabled, ApplyVsyncSetting);

        (HSlider Slider, Label ValueLbl) CreateSliderRow(string labelText, float minVal, float maxVal, float step, float initial, Func<float, string> fmt, Action<float> onChanged)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lbl = CreateLabel(labelText, new Color("1f2937"), 11);
            lbl.CustomMinimumSize = new Vector2(160, 0);
            var slider = new HSlider
            {
                MinValue = minVal,
                MaxValue = maxVal,
                Step = step,
                Value = initial,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var valLbl = CreateLabel(fmt(initial), new Color("0369a1"), 11);
            valLbl.CustomMinimumSize = new Vector2(54, 0);
            valLbl.HorizontalAlignment = HorizontalAlignment.Right;

            slider.ValueChanged += raw =>
            {
                float v = (float)raw;
                onChanged(v);
                valLbl.Text = fmt(v);
            };
            slider.DragEnded += _ => PersistClientSettings();

            row.AddChild(lbl);
            row.AddChild(slider);
            row.AddChild(valLbl);
            bodyCol.AddChild(row);
            return (slider, valLbl);
        }

        bodyCol.AddChild(CreateLabel("ESCALA DA INTERFACE & ÁUDIO (SALVO EM settings.json)", new Color("4b5563"), 11));
        (_hudScaleSlider, _hudScaleValLbl) = CreateSliderRow(
            "Escala do HUD:", 0.85f, 1.25f, 0.05f, _hudScaleFactor,
            v => $"{v * 100f:F0}%", ApplyHudScaleSetting);
        (_masterVolSlider, _masterVolValLbl) = CreateSliderRow(
            "Volume Principal:", 0f, 1f, 0.05f, _audio != null ? _audio.MasterVolume : 0.85f,
            v => $"{v * 100f:F0}%",
            v => { if (_audio != null) _audio.MasterVolume = v; });
        (_ambientVolSlider, _ambientVolValLbl) = CreateSliderRow(
            "Volume Ambiente:", 0f, 1f, 0.05f, _audio != null ? _audio.AmbientVolume : 0.28f,
            v => $"{v * 100f:F0}%",
            v => { if (_audio != null) _audio.AmbientVolume = v; });

        _graphicsStatusSummaryLbl = CreateLabel("", new Color("047857"), 11);
        bodyCol.AddChild(_graphicsStatusSummaryLbl);

        RefreshGraphicsModalUi();
    }

    private void ToggleGraphicsModal()
    {
        if (_graphicsModal == null) return;
        _graphicsModal.Visible = !_graphicsModal.Visible;
        if (_graphicsModal.Visible)
        {
            if (_savesModal != null) _savesModal.Visible = false;
            RefreshGraphicsModalUi();
            AnimateControlPopup(_graphicsModal);
        }
        _audio?.PlayUiClick();
    }

    private void RefreshGraphicsModalUi()
    {
        if (_graphicsModal == null) return;

        _presetPerfBtn?.AddThemeStyleboxOverride("normal", _graphicsPreset == GraphicsQualityPreset.Performance ? _pillBtnActive : _pillBtnNormal);
        _presetBalBtn?.AddThemeStyleboxOverride("normal", _graphicsPreset == GraphicsQualityPreset.Balanced ? _pillBtnActive : _pillBtnNormal);
        _presetUltraBtn?.AddThemeStyleboxOverride("normal", _graphicsPreset == GraphicsQualityPreset.DioramaUltra ? _pillBtnActive : _pillBtnNormal);

        if (_chkSsao != null) _chkSsao.SetPressedNoSignal(_userSsaoEnabled);
        if (_chkGlow != null) _chkGlow.SetPressedNoSignal(_worldEnv.GlowEnabled);
        if (_chkSsr != null) _chkSsr.SetPressedNoSignal(_userSsrEnabled);
        if (_chkTiltShift != null) _chkTiltShift.SetPressedNoSignal(_tiltShiftEnabled);
        if (_chkShadows4Splits != null) _chkShadows4Splits.SetPressedNoSignal(_userShadows4Splits);
        if (_chkDynamicSun != null) _chkDynamicSun.SetPressedNoSignal(_dynamicSolarCycleEnabled);
        if (_chkVsync != null) _chkVsync.SetPressedNoSignal(_userVsyncEnabled);
        if (_hudScaleSlider != null) _hudScaleSlider.SetValueNoSignal(_hudScaleFactor);
        if (_hudScaleValLbl != null) _hudScaleValLbl.Text = $"{_hudScaleFactor * 100f:F0}%";
        if (_masterVolSlider != null && _audio != null) _masterVolSlider.SetValueNoSignal(_audio.MasterVolume);
        if (_masterVolValLbl != null && _audio != null) _masterVolValLbl.Text = $"{_audio.MasterVolume * 100f:F0}%";
        if (_ambientVolSlider != null && _audio != null) _ambientVolSlider.SetValueNoSignal(_audio.AmbientVolume);
        if (_ambientVolValLbl != null && _audio != null) _ambientVolValLbl.Text = $"{_audio.AmbientVolume * 100f:F0}%";

        if (_graphicsStatusSummaryLbl != null)
        {
            string msaa = GetViewport()?.Msaa3D switch
            {
                Viewport.Msaa.Msaa4X => "MSAA 4x",
                Viewport.Msaa.Msaa2X => "MSAA 2x",
                _ => GetViewport()?.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa ? "FXAA" : "AA OFF"
            };
            _graphicsStatusSummaryLbl.Text =
                $"Pipeline Forward+ Ativo • {msaa} • Sombras {(int)_userShadowMaxDistance}m • " +
                $"SSAO {(_userSsaoEnabled ? "ON" : "OFF")} • Glow {(_worldEnv.GlowEnabled ? "ON" : "OFF")} • " +
                $"SSR {(_userSsrEnabled ? "ON" : "OFF")} • Tilt-Shift {(_tiltShiftEnabled ? "ON" : "OFF")}";
        }
    }

    private void BuildPipWindow(Control canvas, StyleBoxFlat darkPillStyle)
    {
        _pipPanel = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -248,
            OffsetRight = -14,
            OffsetTop = -226,
            OffsetBottom = -60
        };
        _pipPanel.AddThemeStyleboxOverride("panel", darkPillStyle);
        canvas.AddChild(_pipPanel);

        var pipVBox = new VBoxContainer();
        pipVBox.AddThemeConstantOverride("separation", 4);
        _pipPanel.AddChild(pipVBox);

        var pipHeader = new HBoxContainer();
        _pipTitleLabel = CreateLabel("📺 PiP • Caminhão", new Color("f8fafc"), 11);
        _pipTitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pipTitleLabel.ClipText = true;
        pipHeader.AddChild(_pipTitleLabel);
        var closeBtn = AddPillButton(pipHeader, "✕", TogglePipCamera);
        pipVBox.AddChild(pipHeader);

        var vpContainer = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(218, 126),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var subVp = new SubViewport
        {
            Size = new Vector2I(218, 126),
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible
        };
        _pipCamera = new Camera3D
        {
            Fov = 48f,
            Current = true
        };
        subVp.AddChild(_pipCamera);
        vpContainer.AddChild(subVp);
        pipVBox.AddChild(vpContainer);
    }

    private void TogglePipCamera()
    {
        if (_pipPanel == null) return;
        _pipPanel.Visible = !_pipPanel.Visible;
        if (_pipPanel.Visible)
            AnimateControlPopup(_pipPanel);
        _pipToggleBtn?.AddThemeStyleboxOverride("normal", _pipPanel.Visible ? _pillBtnActive : _pillBtnNormal);
        _audio?.PlayUiClick();
    }

    private void UpdatePipCamera(float dt)
    {
        if (_pipPanel == null || !_pipPanel.Visible || _pipCamera == null)
            return;

        var trucks = _bridge.Engine.ActiveTrucks;
        if (trucks.Count == 0)
        {
            _pipTitleLabel.Text = "📺 PiP • Sem frota ativa";
            return;
        }

        int idx = Math.Clamp(_selectedTruckIndex, 0, trucks.Count - 1);
        var truck = trucks[idx];
        _pipTitleLabel.Text = $"📺 {truck.Name} ({truck.Status})";

        if (_truckVisuals.TryGetValue(truck.Id, out var handle))
        {
            Vector3 targetPos = handle.Root.GlobalPosition;
            Vector3 desiredCam = targetPos + new Vector3(4.5f, 3.8f, 4.5f);
            _pipCamera.GlobalPosition = _pipCamera.GlobalPosition.DistanceTo(desiredCam) > 30f
                ? desiredCam
                : _pipCamera.GlobalPosition.Lerp(desiredCam, MathF.Min(1f, dt * 7.5f));
            _pipCamera.LookAt(targetPos + new Vector3(0f, 0.35f, 0f), Vector3.Up);
        }
    }

    private void BuildTutorialPanel(Control canvas, StyleBoxFlat whiteToastStyle)
    {
        _tutorialPanel = new PanelContainer
        {
            Visible = !_clientSettings.TutorialHidden && !_clientSettings.TutorialCompleted,
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            GrowHorizontal = Control.GrowDirection.Begin,
            OffsetLeft = -272,
            OffsetRight = -14,
            OffsetTop = 290,
            OffsetBottom = 448
        };
        _tutorialPanel.AddThemeStyleboxOverride("panel", whiteToastStyle);
        canvas.AddChild(_tutorialPanel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        _tutorialPanel.AddChild(col);

        var hdr = new HBoxContainer();
        _tutorialHeaderLabel = CreateLabel("🎓 Roteiro Inicial (0/5)", new Color("0f172a"), 12);
        _tutorialHeaderLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hdr.AddChild(_tutorialHeaderLabel);

        var hideBtn = new Button { Text = "✕" };
        hideBtn.AddThemeFontSizeOverride("font_size", 10);
        hideBtn.Pressed += ToggleTutorialPanel;
        hdr.AddChild(hideBtn);
        col.AddChild(hdr);

        string[] stepTexts =
        {
            "⬜ 1. Construa 1 trecho de estrada (🌉 Via/Ponte)",
            "⬜ 2. Compre 1 caminhão novo (🚛 Frota)",
            "⬜ 3. Crie 1 Linha Logística (Agenda → Linha)",
            "⬜ 4. Pavimente via p/ Asfalto+ (🛣️ Asfalto)",
            "⬜ 5. Feche 1 mês com Lucro Operacional"
        };

        _tutorialStepLabels = new Label[stepTexts.Length];
        for (int i = 0; i < stepTexts.Length; i++)
        {
            var lbl = CreateLabel(stepTexts[i], new Color("334155"), 10);
            lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(lbl);
            _tutorialStepLabels[i] = lbl;
        }
    }

    private void ToggleTutorialPanel()
    {
        if (_tutorialPanel == null) return;
        _tutorialPanel.Visible = !_tutorialPanel.Visible;
        _clientSettings.TutorialHidden = !_tutorialPanel.Visible;
        PersistClientSettings();
        if (_tutorialPanel.Visible)
            RefreshTutorialPanel();
        _audio?.PlayUiClick();
    }

    private void RefreshTutorialPanel()
    {
        if (_tutorialPanel == null || !_tutorialPanel.Visible || _tutorialStepLabels.Length < 5)
            return;

        bool step1 = _tutorialRoadBuilt;
        bool step2 = _bridge.Engine.ActiveTrucks.Count >= 3;
        bool step3 = _bridge.Engine.LogisticsLines.Count >= 1;
        bool step4 = _tutorialPavedAsphalt;
        bool step5 = _bridge.Engine.Financials.MonthlyHistory.Any(m => m.OperatingProfit > 0);

        bool[] done = { step1, step2, step3, step4, step5 };
        string[] labels =
        {
            "1. Construa 1 trecho de estrada (🌉 Via/Ponte)",
            "2. Compre 1 caminhão novo (🚛 Frota)",
            "3. Crie 1 Linha Logística (Agenda → Linha)",
            "4. Pavimente via p/ Asfalto+ (🛣️ Asfalto)",
            "5. Feche 1 mês com Lucro Operacional"
        };

        int completedCount = 0;
        for (int i = 0; i < 5; i++)
        {
            if (done[i]) completedCount++;
            string prefix = done[i] ? "✅ " : "⬜ ";
            string next = prefix + labels[i];
            if (_tutorialStepLabels[i].Text != next)
            {
                _tutorialStepLabels[i].Text = next;
                _tutorialStepLabels[i].AddThemeColorOverride("font_color", done[i] ? new Color("15803d") : new Color("334155"));
            }
        }

        string hdrText = $"🎓 Roteiro Inicial ({completedCount}/5)";
        if (_tutorialHeaderLabel != null && _tutorialHeaderLabel.Text != hdrText)
            _tutorialHeaderLabel.Text = hdrText;

        if (completedCount == 5 && !_clientSettings.TutorialCompleted)
        {
            _clientSettings.TutorialCompleted = true;
            _bridge.Engine.GrantTutorialReward(2500L, 250);
            PersistClientSettings();
            _audio?.PlayNotification(celebratory: true);
            LogEvent("🎓 Tutorial Inicial concluído (5/5)! Bônus de boas-vindas: +R$ 2.500 e +250 XP!");
        }
    }

    private void BuildSavesAndSeedModal(Control canvas, StyleBoxFlat windowOuterStyle)
    {
        _savesModal = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -285,
            OffsetRight = 285,
            OffsetTop = -250,
            OffsetBottom = 250
        };
        _savesModal.AddThemeStyleboxOverride("panel", windowOuterStyle);
        canvas.AddChild(_savesModal);

        var rootCol = new VBoxContainer();
        rootCol.AddThemeConstantOverride("separation", 0);
        _savesModal.AddChild(rootCol);

        var hdrPanel = new PanelContainer();
        hdrPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("22252b"),
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        });
        var hdrRow = new HBoxContainer();
        var titleLbl = CreateLabel("🗂️ Gerenciador de Saves & Seeds", new Color("f8fafc"), 13);
        titleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hdrRow.AddChild(titleLbl);
        AddPillButton(hdrRow, "✕", ToggleSavesModal);
        hdrPanel.AddChild(hdrRow);
        rootCol.AddChild(hdrPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rootCol.AddChild(margin);

        var bodyCol = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        bodyCol.AddThemeConstantOverride("separation", 8);
        margin.AddChild(bodyCol);

        // Seed sharing & custom generation row
        var seedInfoRow = new HBoxContainer();
        seedInfoRow.AddThemeConstantOverride("separation", 6);
        _savesCurrentSeedLabel = CreateLabel($"🎲 Seed Atual: #{_bridge.Seed}", new Color("1f2937"), 12);
        _savesCurrentSeedLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        seedInfoRow.AddChild(_savesCurrentSeedLabel);
        var copySeedBtn = new Button { Text = "📋 Copiar Seed" };
        copySeedBtn.AddThemeFontSizeOverride("font_size", 11);
        copySeedBtn.Pressed += () =>
        {
            DisplayServer.ClipboardSet(_bridge.Seed.ToString());
            LogEvent($"📋 Seed #{_bridge.Seed} copiada para a área de transferência!");
        };
        seedInfoRow.AddChild(copySeedBtn);
        bodyCol.AddChild(seedInfoRow);

        var seedGenRow = new HBoxContainer();
        seedGenRow.AddThemeConstantOverride("separation", 6);
        _seedInputEdit = new LineEdit
        {
            PlaceholderText = "Digite um código de Seed (ex: 123456)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _seedInputEdit.AddThemeFontSizeOverride("font_size", 11);
        seedGenRow.AddChild(_seedInputEdit);

        var genSeedBtn = new Button { Text = "🌍 Gerar c/ Seed" };
        genSeedBtn.AddThemeFontSizeOverride("font_size", 11);
        genSeedBtn.Pressed += () =>
        {
            if (int.TryParse(_seedInputEdit.Text.Trim(), out int customSeed) && customSeed > 0)
            {
                RegenerateWorldWithSeed(customSeed);
                _savesModal.Visible = false;
            }
            else
            {
                _audio?.PlayError();
                LogEvent("⚠️ Informe um número inteiro positivo para a Seed.");
            }
        };
        seedGenRow.AddChild(genSeedBtn);
        bodyCol.AddChild(seedGenRow);

        bodyCol.AddChild(CreateLabel("TAMANHO DO MAPA & CENÁRIO TEMÁTICO", new Color("4b5563"), 11));
        var mapCfgRow = new HBoxContainer();
        mapCfgRow.AddThemeConstantOverride("separation", 6);

        _mapSizeSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mapSizeSelect.AddThemeFontSizeOverride("font_size", 11);
        _mapSizeSelect.AddItem("50x50 (Compacto)", 50);
        _mapSizeSelect.AddItem("80x80 (Médio)", 80);
        _mapSizeSelect.AddItem("100x100 (Grande)", 100);
        int sizeIdx = _selectedMapSize == 50 ? 0 : (_selectedMapSize == 80 ? 1 : 2);
        _mapSizeSelect.Select(sizeIdx);
        _mapSizeSelect.ItemSelected += idx =>
        {
            _selectedMapSize = (int)_mapSizeSelect.GetItemId((int)idx);
            PersistClientSettings();
        };
        mapCfgRow.AddChild(_mapSizeSelect);

        _scenarioSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scenarioSelect.AddThemeFontSizeOverride("font_size", 11);
        _scenarioSelect.AddItem("Equilibrado", (int)WorldScenario.Balanced);
        _scenarioSelect.AddItem("🏝️ Arquipélago Costeiro", (int)WorldScenario.CoastalArchipelago);
        _scenarioSelect.AddItem("🏔️ Cordilheira Alpina", (int)WorldScenario.AlpineRange);
        _scenarioSelect.Select((int)_selectedScenario);
        _scenarioSelect.ItemSelected += idx =>
        {
            _selectedScenario = (WorldScenario)_scenarioSelect.GetItemId((int)idx);
            PersistClientSettings();
        };
        mapCfgRow.AddChild(_scenarioSelect);

        var applyMapBtn = new Button { Text = "🆕 Novo Mapa" };
        applyMapBtn.AddThemeFontSizeOverride("font_size", 11);
        applyMapBtn.Pressed += () =>
        {
            RegenerateRandomWorld();
            _savesModal.Visible = false;
        };
        mapCfgRow.AddChild(applyMapBtn);
        bodyCol.AddChild(mapCfgRow);

        bodyCol.AddChild(CreateLabel("SLOTS DE SALVAMENTO", new Color("4b5563"), 11));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _savesListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _savesListBox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_savesListBox);
        bodyCol.AddChild(scroll);
    }

    private void ToggleSavesModal()
    {
        if (_savesModal == null) return;
        _savesModal.Visible = !_savesModal.Visible;
        if (_savesModal.Visible)
        {
            if (_graphicsModal != null) _graphicsModal.Visible = false;
            RefreshSavesModalList();
            AnimateControlPopup(_savesModal);
        }
        _audio?.PlayUiClick();
    }

    private void RefreshSavesModalList()
    {
        if (_savesModal == null || _savesListBox == null) return;
        _savesCurrentSeedLabel.Text = $"🎲 Seed Atual: #{_bridge.Seed}";
        foreach (Node child in _savesListBox.GetChildren())
            child.QueueFree();

        var slots = new (string Label, string UserPath, bool CanOverwrite)[]
        {
            ("Slot 1 (Principal)", "user://save_slot_1.json", true),
            ("Slot 2 (Secundário)", "user://save_slot_2.json", true),
            ("Slot 3 (Reserva)", "user://save_slot_3.json", true),
            ("Save Rápido", "user://savegame.json", true),
            ("Auto-Save Mensal", "user://autosave.json", false)
        };

        foreach (var (label, userPath, canOverwrite) in slots)
        {
            string absPath = ProjectSettings.GlobalizePath(userPath);
            var summary = SaveGameSerializer.ReadSaveSummary(absPath, label);

            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color("ffffff"),
                BorderColor = new Color("cbd5e1"),
                BorderWidthBottom = 1,
                BorderWidthTop = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            });

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var infoCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            infoCol.AddChild(CreateLabel(summary.Exists ? $"📁 {label}  •  Seed #{summary.Seed} ({summary.SavedAtText})" : $"📁 {label}  •  [Vazio]", new Color("111827"), 11));
            string sub = summary.Exists
                ? $"{summary.CalendarText} | R$ {summary.Balance:N0} | {summary.TruckCount} caminhões | {summary.CityCount} cidades"
                : "Nenhum dado gravado neste slot.";
            infoCol.AddChild(CreateLabel(sub, new Color("64748b"), 10));
            row.AddChild(infoCol);

            if (canOverwrite)
            {
                var saveBtn = new Button { Text = "💾 Salvar" };
                saveBtn.AddThemeFontSizeOverride("font_size", 11);
                string uPath = userPath;
                string sLabel = label;
                saveBtn.Pressed += () =>
                {
                    _bridge.SaveToFile(ProjectSettings.GlobalizePath(uPath));
                    _audio?.PlaySaveLoad(isLoad: false);
                    LogEvent($"💾 Jogo salvo em {sLabel} (Seed #{_bridge.Seed})!");
                    RefreshSavesModalList();
                };
                row.AddChild(saveBtn);
            }

            if (summary.Exists)
            {
                var loadBtn = new Button { Text = "📂 Carregar" };
                loadBtn.AddThemeFontSizeOverride("font_size", 11);
                string uPath = userPath;
                string sLabel = label;
                loadBtn.Pressed += () =>
                {
                    _savesModal.Visible = false;
                    LoadGameFromPath(uPath, sLabel);
                };
                row.AddChild(loadBtn);

                var delBtn = new Button { Text = "🗑" };
                delBtn.AddThemeFontSizeOverride("font_size", 11);
                string dPath = absPath;
                delBtn.Pressed += () =>
                {
                    if (System.IO.File.Exists(dPath))
                        System.IO.File.Delete(dPath);
                    RefreshSavesModalList();
                };
                row.AddChild(delBtn);
            }

            card.AddChild(row);
            _savesListBox.AddChild(card);
        }
    }

    private void RegenerateWorldWithSeed(int customSeed)
    {
        ClearWorldVisuals();
        InitializeSimulation(seed: customSeed);
        BuildInitialWorld3D();
        FocusOnFirstCity();
        SyncFleet3DTransforms(0.01f);
        _selectedTruckIndex = 0;
        RebuildSchedulePools();
        LogEvent($"🌍 Mapa gerado pela Seed #{_bridge.Seed}!");
        RefreshHud();
    }

    private void OpenWindowOnTab(WindowTab tab)
    {
        _activeTab = tab;
        _topLeftWindow.Visible = true;

        _tabBodyInspector.Visible = tab == WindowTab.Inspector;
        _tabBodySchedule.Visible = tab == WindowTab.Schedule;
        _tabBodyResearch.Visible = tab == WindowTab.Research;
        _tabBodyFleet.Visible = tab == WindowTab.Fleet;
        _tabBodyRail.Visible = tab == WindowTab.Rail;
        _tabBodyContracts.Visible = tab == WindowTab.Contracts;
        _tabBodyOperations.Visible = tab == WindowTab.Operations;
        _tabBodyBank.Visible = tab == WindowTab.Bank;
        _tabBodyLog.Visible = tab == WindowTab.Log;

        _windowTitleLabel.Text = tab switch
        {
            WindowTab.Inspector => "Inspetor Regional",
            WindowTab.Schedule => "Agenda do Veículo",
            WindowTab.Research => "Pesquisa & Tecnologia",
            WindowTab.Fleet => $"{_bridge.Engine.Player.CompanyName} (Frota: {_bridge.Engine.ActiveTrucks.Count})",
            WindowTab.Rail => $"Ferrovia & Pátios Intermodais ({_bridge.Engine.ActiveTrains.Count} trens)",
            WindowTab.Contracts => "Contratos & Regiões",
            WindowTab.Operations => "Inteligência Operacional & Conquistas",
            WindowTab.Bank => "Banco Central & DRE",
            WindowTab.Log => "Registro de Operações",
            _ => "Painel"
        };

        if (tab == WindowTab.Fleet)
        {
            SyncLiveryEditorScopeDropdown();
            if (!_liveryDirtyFromUser)
                LoadSelectedScopeLiveryIntoEditor();
        }

        foreach (var (t, btn) in _tabButtons)
        {
            bool active = t == tab;
            btn.AddThemeStyleboxOverride("normal", active ? _tabBtnActive : _tabBtnNormal);
            btn.AddThemeColorOverride("font_color", active ? new Color("111827") : new Color("cbd5e1"));
        }

        switch (tab)
        {
            case WindowTab.Inspector:
                UpdateInspectorPanel();
                break;
            case WindowTab.Schedule:
                SyncScheduleSelectorsToCurrentTruck();
                RefreshScheduleTab();
                break;
            case WindowTab.Research:
                RefreshResearchTab();
                break;
            case WindowTab.Fleet:
                RefreshFleetModernizerPanel();
                RefreshFleetActiveList();
                break;
            case WindowTab.Rail:
                RefreshRailTab();
                break;
            case WindowTab.Contracts:
                RefreshContractsTab();
                break;
            case WindowTab.Operations:
                RefreshOperationsTab();
                break;
            case WindowTab.Bank:
                RefreshBankAndDreTab();
                break;
        }
    }

    private void SetActiveTool(ToolMode mode)
    {
        if (_isDraggingRoad && mode != ToolMode.BuildRoad && mode != ToolMode.BuildRail)
            CancelRoadDrag();

        _activeTool = mode;
        _audio?.PlayUiClick();
        RefreshHud();
    }

    private void ToggleAudioMute()
    {
        if (_audio == null) return;
        bool nextMuted = !_audio.IsMuted;
        _audio.SetMuted(nextMuted);
        if (_muteAudioBtn != null)
            _muteAudioBtn.Text = nextMuted ? "🔇" : "🔊";
        if (!nextMuted)
            _audio.PlayUiClick();
        PersistClientSettings();
        LogEvent(nextMuted ? "🔇 Áudio sintetizado silenciado." : "🔊 Áudio sintetizado ativado.");
    }

    private Label CreateLabel(string text, Color color, int fontSize)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeColorOverride("font_color", color);
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        return lbl;
    }

    private Button AddPillButton(Container parent, string text, Action onClick)
    {
        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", 12);
        btn.AddThemeStyleboxOverride("normal", _pillBtnNormal);
        btn.AddThemeStyleboxOverride("hover", _pillBtnActive);
        btn.AddThemeStyleboxOverride("pressed", _pillBtnActive);
        btn.AddThemeColorOverride("font_color", new Color("f8fafc"));
        btn.Pressed += onClick;
        parent.AddChild(btn);
        return btn;
    }

    private Button AddTabButton(Container parent, string text, WindowTab tab)
    {
        var btn = new Button
        {
            Text = text,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        btn.AddThemeFontSizeOverride("font_size", 11);
        btn.Pressed += () =>
        {
            _audio?.PlayUiClick();
            OpenWindowOnTab(tab);
        };
        parent.AddChild(btn);
        return btn;
    }

    private void LogEvent(string message)
    {
        string timestamp = $"{_bridge.Engine.Calendar.Hour:D2}:{_bridge.Engine.Calendar.Minute:D2}";
        _recentEvents.Enqueue($"[{timestamp}] {message}");
        while (_recentEvents.Count > 15)
            _recentEvents.Dequeue();

        if (_topAlertLabel != null)
            _topAlertLabel.Text = message;

        if (_eventLogText != null)
            _eventLogText.Text = string.Join("\n", _recentEvents.Reverse());
    }

    private void RefreshHud()
    {
        var p = _bridge.Engine.Player;
        if (p.Balance != _lastHudBalance || p.LoanDebt != _lastHudDebt)
        {
            _lastHudBalance = p.Balance;
            _lastHudDebt = p.LoanDebt;
            _bottomBalanceLabel.Text = p.LoanDebt > 0
                ? $"R$ {p.Balance:N0} (-R$ {p.LoanDebt:N0})"
                : $"R$ {p.Balance:N0}";
        }

        if (p.Level != _lastHudLevel || p.CurrentXp != _lastHudXp || _bridge.Seed != _lastHudSeed)
        {
            _lastHudLevel = p.Level;
            _lastHudXp = p.CurrentXp;
            _lastHudSeed = _bridge.Seed;
            _bottomLevelLabel.Text = $"Nv.{p.Level} • {p.CurrentXp}/{p.NextLevelXp} XP • Seed #{_bridge.Seed}";
        }

        var cal = _bridge.Engine.Calendar;
        if (cal.Day != _lastHudDay || cal.Month != _lastHudMonth || cal.Hour != _lastHudHour || cal.Minute != _lastHudMinute)
        {
            _lastHudDay = cal.Day;
            _lastHudMonth = cal.Month;
            _lastHudHour = cal.Hour;
            _lastHudMinute = cal.Minute;
            _bottomDateLabel.Text = $"{cal.Day:D2}/{cal.Month:D2} {cal.Hour:D2}:{cal.Minute:D2}";
        }

        if (_bridge.Engine.CurrentWeather != _lastHudWeather || _bridge.Engine.ActiveSeasonalEvent != _lastHudSeason)
        {
            _lastHudWeather = _bridge.Engine.CurrentWeather;
            _lastHudSeason = _bridge.Engine.ActiveSeasonalEvent;
            string weatherIcon = _lastHudWeather switch
            {
                WeatherCondition.Clear => "☀️",
                WeatherCondition.Rain => "🌧️",
                WeatherCondition.HeavyStorm => "⛈️",
                _ => "☀️"
            };
            string seasonTag = _lastHudSeason switch
            {
                SeasonalEventType.BumperHarvest => " 🌾Safra",
                SeasonalEventType.WinterBlizzard => " ❄️Nevasca",
                SeasonalEventType.MountainLandslide => " 🚧Desliz.",
                _ => string.Empty
            };
            _bottomWeatherLabel.Text = weatherIcon + seasonTag;
        }

        if (_lastRenderedTool != _activeTool)
        {
            _lastRenderedTool = _activeTool;
            foreach (var (mode, btn) in _toolButtons)
                btn.AddThemeStyleboxOverride("normal", mode == _activeTool ? _pillBtnActive : _pillBtnNormal);
        }

        if (_lastRenderedCorridorBatch != _corridorBatchMode)
        {
            _lastRenderedCorridorBatch = _corridorBatchMode;
            _corridorBatchBtn.AddThemeStyleboxOverride("normal", _corridorBatchMode ? _pillBtnActive : _pillBtnNormal);
        }

        if (!Mathf.IsEqualApprox(_lastRenderedTimeScale, _bridge.TimeScale))
        {
            _lastRenderedTimeScale = _bridge.TimeScale;
            foreach (var (spd, btn) in _speedButtons)
            {
                bool active = Mathf.IsEqualApprox(spd, _bridge.TimeScale);
                btn.AddThemeStyleboxOverride("normal", active ? _pillBtnActive : _pillBtnNormal);
            }
        }

        foreach (var city in _bridge.World.Cities)
        {
            if (_cityLabels.TryGetValue(city.Id, out var lbl))
            {
                string nextText = GetCityBannerText(city);
                if (lbl.Text != nextText)
                    lbl.Text = nextText;
            }
        }
        foreach (var ind in _bridge.World.Industries)
        {
            if (_industryLabels.TryGetValue(ind.Id, out var lbl))
            {
                string nextText = GetIndustryBannerText(ind);
                if (lbl.Text != nextText)
                    lbl.Text = nextText;
            }
        }

        bool windowOpen = _topLeftWindow.Visible;
        if (windowOpen)
        {
            switch (_activeTab)
            {
                case WindowTab.Inspector:
                    UpdateInspectorPanel();
                    break;
                case WindowTab.Schedule:
                    RefreshScheduleTab();
                    break;
                case WindowTab.Fleet:
                    string fleetTitle = $"{_bridge.Engine.Player.CompanyName} (Frota: {_bridge.Engine.ActiveTrucks.Count})";
                    if (_windowTitleLabel.Text != fleetTitle)
                        _windowTitleLabel.Text = fleetTitle;
                    SyncLiveryEditorScopeDropdown();
                    RefreshFleetModernizerPanel();
                    RefreshFleetActiveList();
                    break;
                case WindowTab.Rail:
                    RefreshRailTab();
                    break;
                case WindowTab.Contracts:
                    RefreshContractsTab();
                    break;
                case WindowTab.Research:
                    RefreshResearchTab();
                    break;
                case WindowTab.Bank:
                    RefreshBankAndDreTab();
                    break;
            }
        }

        RefreshOperationsBadgeAndTab(forceDiagnose: windowOpen && _activeTab == WindowTab.Operations);
        RefreshTutorialPanel();

        if (_minimapStaticDirty && _minimapOverlay != null)
        {
            _minimapOverlay.RebuildStaticMap(_bridge);
            _minimapStaticDirty = false;
            RefreshMinimapOverlay();
        }
    }

    private void RefreshContractsTab()
    {
        if (_contractsText == null) return;

        var contractLines = new List<string>();
        bool hasAnyOpenAuction = false;
        foreach (var c in _bridge.Engine.ActiveContracts)
        {
            string statusColor = c.IsCompleted ? "#16a34a" : ((c.IsExpired || c.IsLostToRival) ? "#dc2626" : (c.IsAuctionOpen ? "#0284c7" : (c.IsSpecialConvoy ? "#7c3aed" : "#b45309")));
            string convoyTag = c.IsSpecialConvoy ? "🚨 [COMBOIO 3x] " : (c.IsExclusiveAuction ? "🏷️ [LEILÃO] " : string.Empty);
            string statusTag;
            if (c.IsCompleted)
            {
                statusTag = "CONCLUÍDO";
            }
            else if (c.IsLostToRival)
            {
                statusTag = $"RIVAL ({c.ClaimedByRivalName})";
            }
            else if (c.IsAuctionOpen)
            {
                hasAnyOpenAuction = true;
                string pBid = c.HasPlayerBid
                    ? $"Você: {c.PlayerBidFreightMultiplier * 100f:F0}%/{c.PlayerBidDurationHours:F0}h"
                    : "Sem lance seu";
                statusTag = $"Aberto {c.AuctionRemainingHours:F0}h | {c.RivalBidCompanyName}: {c.RivalBidFreightMultiplier * 100f:F0}%/{c.RivalBidDurationHours:F0}h | {pBid}";
            }
            else
            {
                statusTag = $"{c.DeliveredAmount}/{c.RequiredAmount} un. ({c.RemainingHours:F0}h)";
            }
            contractLines.Add($"[color={statusColor}]{convoyTag}• [b]{c.TargetCity.Name}[/b]: {c.Resource} — {statusTag} (+R$ {c.BonusRewardMoney:N0})[/color]");
        }
        if (_auctionBidRow != null)
        {
            _auctionBidRow.Visible = hasAnyOpenAuction;
        }
        if (_regionalStatusLabel != null && _exploreRegionBtn != null)
        {
            int unlocked = _bridge.World.UnlockedRegionsCount;
            int maxReg = TransitEmpire.Core.Generation.GeneratedWorld.MaxRegionsCount;
            _regionalStatusLabel.Text = $"🗺️ Regiões Ativas: {unlocked}/{maxReg} ({_bridge.World.Cities.Count} cidades)";
            if (unlocked >= maxReg)
            {
                _exploreRegionBtn.Text = "✅ Mapa Completo";
                _exploreRegionBtn.Disabled = true;
            }
            else
            {
                _exploreRegionBtn.Text = $"🧭 Explorar (R$ {_bridge.Engine.GetNextRegionUnlockCost():N0})";
                _exploreRegionBtn.Disabled = false;
            }
        }

        var standings = _bridge.Engine.GetMarketShareStandings();
        contractLines.Add("[b]📊 RANKING DE MERCADO (MARKET SHARE)[/b]");
        foreach (var st in standings)
        {
            string tag = st.IsPlayer ? "⭐ " : "🏢 ";
            contractLines.Add($"[color={st.ColorHex}]{tag}[b]{st.Name}[/b]: {st.SharePercent:F1}% ({st.DeliveredVolume:N0} un. | {st.CompletedContracts} contr.)[/color]");
        }

        string nextContractsBbcode = contractLines.Count > 0 ? string.Join("\n", contractLines) : "Nenhum contrato ativo.";
        if (_lastContractsBbcode != nextContractsBbcode)
        {
            _lastContractsBbcode = nextContractsBbcode;
            _contractsText.Text = nextContractsBbcode;
        }
    }

    private void RefreshBankAndDreTab()
    {
        if (_dreReportText == null) return;
        if (_autoRoadMaintToggleBtn != null)
        {
            _autoRoadMaintToggleBtn.Text = _bridge.Engine.AutoRoadMaintenanceEnabled
                ? "🛠️ Conservação Auto: ON"
                : "🛠️ Conservação Auto: OFF";
        }
        var p = _bridge.Engine.Player;
        long availableCredit = Math.Max(0, p.MaxCreditLimit - p.LoanDebt);
        long estInterest = p.LoanDebt > 0 ? Math.Max(10, (long)(p.LoanDebt * 0.02f)) : 0;
        _bankSummaryLabel.Text =
            $"Dívida: R$ {p.LoanDebt:N0} / Limite R$ {p.MaxCreditLimit:N0} (Disp.: R$ {availableCredit:N0})\n" +
            $"Juros mensais estimados (2% a.m.): R$ {estInterest:N0}";

        var fin = _bridge.Engine.Financials;
        _dreChart?.UpdateSeries(fin.MonthlyHistory, fin.CurrentMonth, _bridge.Engine.Calendar.Month, _bridge.Engine.Calendar.Year);

        var stmt = _dreShowLifetime ? fin.Lifetime : fin.CurrentMonth;
        string periodTitle = _dreShowLifetime
            ? "Acumulado Geral da Empresa"
            : $"Exercício Corrente ({_bridge.Engine.Calendar.Month:D2}/{_bridge.Engine.Calendar.Year})";

        string opColor = stmt.OperatingProfit >= 0 ? "#15803d" : "#dc2626";
        string cashColor = stmt.NetCashFlow >= 0 ? "#15803d" : "#dc2626";

        var lines = new List<string>
        {
            $"[b]{periodTitle}[/b]",
            $"[color=#15803d](+) Receita de Fretes:[/color] R$ {stmt.FreightRevenue:N0}",
            $"[color=#0284c7](+) Passagens Rodoviárias:[/color] R$ {stmt.TicketRevenue:N0}",
            $"[color=#15803d](+) Bônus de Contratos:[/color] R$ {stmt.ContractBonusRevenue:N0}",
            $"[color=#0284c7](+) Subsídios Municipais:[/color] R$ {stmt.MunicipalSubsidyRevenue:N0}",
            $"[color=#0d9488](+) Pedágios & Balanças:[/color] R$ {stmt.TollRevenue:N0}",
            $"[b]= Receita Bruta Total: R$ {stmt.TotalRevenue:N0}[/b]",
            $"[color=#b91c1c](-) Manutenção & Frota:[/color] R$ {stmt.FleetMaintenanceExpense:N0}",
            $"[color=#b91c1c](-) Conservação Viária:[/color] R$ {stmt.RoadMaintenanceExpense:N0}",
            $"[color=#b91c1c](-) Obras Viárias:[/color] R$ {stmt.RoadConstructionExpense:N0}",
            $"[color=#b91c1c](-) Pedágios Rivais:[/color] R$ {stmt.RivalTollExpense:N0}",
            $"[color=#b91c1c](-) Juros Bancários:[/color] R$ {stmt.LoanInterestExpense:N0}",
            $"[color={opColor}][b]= Resultado Operacional: R$ {stmt.OperatingProfit:N0}[/b][/color]",
            $"[color=#475569](-) Investimentos (Frota/Obras/P&D/Regiões): R$ {stmt.InfrastructureAndFleetExpense:N0}[/color]",
            $"[color={cashColor}][b]= Fluxo de Caixa Líquido: R$ {stmt.NetCashFlow:N0}[/b][/color]"
        };

        if (fin.MonthlyHistory.Count > 0)
        {
            var last = fin.MonthlyHistory[^1];
            lines.Add($"\n[color=#475569]Último Fechamento ({last.Month:D2}/{last.Year}): Receita R$ {last.TotalRevenue:N0} | Lucro Op. R$ {last.OperatingProfit:N0}[/color]");
        }

        _dreReportText.Text = string.Join("\n", lines);
    }

    private void OnMonthlyFinancialClosed(FinancialStatement closed)
    {
        string resultTag = closed.OperatingProfit >= 0
            ? $"Lucro Op. +R$ {closed.OperatingProfit:N0}"
            : $"Prejuízo Op. -R$ {Math.Abs(closed.OperatingProfit):N0}";
        _audio?.PlayNotification(celebratory: false);
        LogEvent($"📑 Fechamento DRE ({closed.Month:D2}/{closed.Year}): Receita R$ {closed.TotalRevenue:N0} | {resultTag}");

        if (_autoSaveEnabled)
        {
            string autoPath = ProjectSettings.GlobalizePath("user://autosave.json");
            _bridge.SaveToFile(autoPath);
            LogEvent($"💾 Auto-Save mensal realizado ({closed.Month:D2}/{closed.Year}).");
        }

        RefreshHud();
    }

    private void RefreshResearchTab()
    {
        if (_researchActiveLabel == null) return;
        var research = _bridge.Engine.Research;
        var active = research.ActiveResearch;

        if (active != null)
        {
            float pct = active.ProgressRatio * 100f;
            float remHours = MathF.Max(0f, active.RequiredHours - active.ProgressHours);
            _researchActiveLabel.Text = $"🔬 Em Pesquisa: {active.Name} ({pct:F0}% • {remHours:F0}h restantes)";
            _researchActiveProgressBar.Value = pct;
            _researchPauseBtn.Disabled = false;
        }
        else
        {
            _researchActiveLabel.Text = "🧪 Selecione uma tecnologia abaixo para iniciar P&D.";
            _researchActiveProgressBar.Value = 0;
            _researchPauseBtn.Disabled = true;
        }

        foreach (var node in research.Nodes)
        {
            if (!_researchRows.TryGetValue(node.Id, out var row)) continue;

            float pct = node.ProgressRatio * 100f;
            row.Bar.Value = pct;

            bool isActive = active == node;
            bool prereqMissing = node.PrerequisiteId.HasValue && !research.IsCompleted(node.PrerequisiteId.Value);

            if (node.IsCompleted)
            {
                row.StatusLbl.Text = "✅ Concluída";
                row.StatusLbl.AddThemeColorOverride("font_color", new Color("15803d"));
                row.ActionBtn.Text = "✅ Concluída";
                row.ActionBtn.Disabled = true;
            }
            else if (isActive)
            {
                row.StatusLbl.Text = $"⏳ {node.ProgressHours:F0}/{node.RequiredHours:F0}h ({pct:F0}%)";
                row.StatusLbl.AddThemeColorOverride("font_color", new Color("0369a1"));
                row.ActionBtn.Text = "Ativa";
                row.ActionBtn.Disabled = true;
            }
            else if (prereqMissing)
            {
                row.StatusLbl.Text = "🔒 Bloqueada";
                row.StatusLbl.AddThemeColorOverride("font_color", new Color("9ca3af"));
                row.ActionBtn.Text = "🔒 Pré-req.";
                row.ActionBtn.Disabled = true;
            }
            else
            {
                row.StatusLbl.Text = node.IsFunded
                    ? $"⏸️ {node.ProgressHours:F0}/{node.RequiredHours:F0}h ({pct:F0}%)"
                    : $"{node.RequiredHours:F0}h";
                row.StatusLbl.AddThemeColorOverride("font_color", new Color("475569"));
                row.ActionBtn.Text = node.IsFunded ? "▶ Retomar" : $"🔬 R$ {node.FundingCost:N0}";
                row.ActionBtn.Disabled = false;
            }
        }

        int playerLvl = _bridge.Engine.Player.Level;
        foreach (var (modelIdx, btn) in _fleetBuyButtons)
        {
            var spec = ProgressionManager.AvailableTruckModels[modelIdx];
            bool unlocked = research.IsTruckUnlocked(spec, playerLvl);
            string lockTag = unlocked ? "✅" : $"🔒 Nv.{spec.RequiredPlayerLevel}/P&D";
            string hwTag = spec.RequiresHighwayOrDualCarriage ? " [Rodovia+]" : string.Empty;
            string capUnit = spec.IsPassengerBus ? "pass." : "un.";
            btn.Text = $"{lockTag} {spec.Name}  •  R$ {spec.PurchasePrice:N0}  (Cap {spec.Capacity} {capUnit}{hwTag})";
        }
    }

    private void OnResearchCompleted(ResearchNode node)
    {
        _audio?.PlayNotification(celebratory: true);
        LogEvent($"🔬 PESQUISA CONCLUÍDA: {node.Name} — {node.Description}");
        RefreshHud();
    }

    private void RebuildSchedulePools()
    {
        if (_scheduleTargetSelect == null) return;

        _scheduleTargetPool.Clear();
        _scheduleTargetSelect.Clear();

        foreach (var ind in _bridge.World.Industries)
        {
            string tag = ind.RequiredInputResource != ResourceType.None
                ? $"[Indústria] {ind.Name} ({ind.RequiredInputResource} → {ind.OutputResource})"
                : $"[Indústria] {ind.Name} (Extrai {ind.OutputResource})";
            _scheduleTargetPool.Add((ScheduleStopKind.Industry, ind.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var hub in _bridge.Engine.LogisticsHubs)
        {
            string tag = $"[Hub Cross-Dock] {hub.Name} ({hub.CurrentTotalStored}/{hub.MaxStorageCapacity} un.)";
            _scheduleTargetPool.Add((ScheduleStopKind.LogisticsHub, hub.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var city in _bridge.World.Cities)
        {
            string tag = $"[Cidade] {city.Name} (Pop. {city.Population:N0})";
            _scheduleTargetPool.Add((ScheduleStopKind.City, city.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var garage in _bridge.Engine.Garages)
        {
            string tag = $"[Garagem] {garage.Name}";
            _scheduleTargetPool.Add((ScheduleStopKind.Garage, garage.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var bt in _bridge.Engine.BusTerminals)
        {
            string tag = $"[Rodoviária] {bt.Name} ({bt.WaitingPassengers} pass.)";
            _scheduleTargetPool.Add((ScheduleStopKind.BusTerminal, bt.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var yard in _bridge.Engine.IntermodalTerminals)
        {
            string portTag = yard.LinkedSeaportId.HasValue ? " • 🚢 Porto" : "";
            string tag = $"[Pátio Intermodal] {yard.Name} ({yard.TotalStoredUnits}t{portTag})";
            _scheduleTargetPool.Add((ScheduleStopKind.IntermodalYard, yard.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var seaport in _bridge.World.Seaports)
        {
            string shipTag = seaport.IsShipDocked ? "Atracado +45%" : "Em Alto-Mar";
            string tag = $"[Porto] {seaport.Name} ({shipTag})";
            _scheduleTargetPool.Add((ScheduleStopKind.Seaport, seaport.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        foreach (var landmark in _bridge.World.Landmarks)
        {
            string stageTag = landmark.IsCompleted
                ? "Concluída"
                : $"Etapa {landmark.CurrentStage}/{RegionalLandmark.MaxStages}";
            string tag = $"[Mega-Obra] {landmark.Name} ({stageTag})";
            _scheduleTargetPool.Add((ScheduleStopKind.RegionalLandmark, landmark.Id, tag));
            _scheduleTargetSelect.AddItem(tag);
        }

        SyncScheduleTruckDropdown(forceRebuild: true);
        SyncScheduleSelectorsToCurrentTruck();
        RefreshScheduleTab();
    }

    private void SyncScheduleLinesDropdown()
    {
        if (_scheduleLineSelect == null) return;
        var lines = _bridge.Engine.LogisticsLines;
        var trucks = _bridge.Engine.ActiveTrucks;
        int? assignedLineId = trucks.Count > 0
            ? trucks[Mathf.Clamp(_selectedTruckIndex, 0, trucks.Count - 1)].AssignedLineId
            : null;

        _scheduleLineSelect.Clear();
        _scheduleLineSelect.AddItem("— Sem Linha Compartilhada (Individual) —");

        int selectedOptionIdx = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int count = trucks.Count(t => t.AssignedLineId == line.Id);
            _scheduleLineSelect.AddItem($"📋 {line.Name} ({line.Stops.Count}P • {count} cam.)");
            if (assignedLineId == line.Id)
                selectedOptionIdx = i + 1;
        }
        _scheduleLineSelect.Select(selectedOptionIdx);

        if (_scheduleUpdateLineBtn != null)
            _scheduleUpdateLineBtn.Disabled = selectedOptionIdx <= 0;

        var activeLine = selectedOptionIdx > 0 && selectedOptionIdx - 1 < lines.Count
            ? lines[selectedOptionIdx - 1]
            : null;
        if (_lineHeadwayCheck != null)
        {
            _lineHeadwayCheck.Disabled = activeLine == null;
            _lineHeadwayCheck.SetPressedNoSignal(activeLine?.HeadwayControlEnabled ?? false);
        }
        if (_lineAutoReplaceCheck != null)
        {
            _lineAutoReplaceCheck.Disabled = activeLine == null;
            _lineAutoReplaceCheck.SetPressedNoSignal(activeLine?.AutoReplaceEnabled ?? false);
        }
    }

    private void SyncScheduleTruckDropdown(bool forceRebuild = false)
    {
        if (_scheduleTruckSelect == null) return;
        var trucks = _bridge.Engine.ActiveTrucks;
        if (trucks.Count == 0)
        {
            _scheduleTruckSelect.Clear();
            return;
        }

        _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, trucks.Count - 1);

        string FormatTruckDropdownItem(int index, Truck truck)
        {
            var sharedLine = _bridge.Engine.GetLogisticsLine(truck.AssignedLineId);
            string badge = sharedLine != null
                ? $" [{sharedLine.Name}]"
                : (truck.HasManualSchedule ? $" [{truck.Schedule.Count} Paradas]" : " [Auto]");
            return $"#{index + 1} {truck.Name}{badge}";
        }

        if (forceRebuild || _scheduleTruckSelect.ItemCount != trucks.Count)
        {
            _scheduleTruckSelect.Clear();
            for (int i = 0; i < trucks.Count; i++)
            {
                _scheduleTruckSelect.AddItem(FormatTruckDropdownItem(i, trucks[i]));
            }
            _scheduleTruckSelect.Select(_selectedTruckIndex);
        }
        else
        {
            for (int i = 0; i < trucks.Count; i++)
            {
                string desired = FormatTruckDropdownItem(i, trucks[i]);
                if (_scheduleTruckSelect.GetItemText(i) != desired)
                    _scheduleTruckSelect.SetItemText(i, desired);
            }
            if (_scheduleTruckSelect.Selected != _selectedTruckIndex)
                _scheduleTruckSelect.Select(_selectedTruckIndex);
        }
    }

    private void SyncScheduleSelectorsToCurrentTruck()
    {
        _draftSchedule.Clear();
        if (_bridge.Engine.ActiveTrucks.Count == 0)
        {
            RefreshDraftScheduleList();
            return;
        }

        _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1);
        var truck = _bridge.Engine.ActiveTrucks[_selectedTruckIndex];

        if (_schedulePitStopCheck != null)
            _schedulePitStopCheck.SetPressedNoSignal(truck.AutoPitStopEnabled);

        if (truck.Schedule.Count > 0)
        {
            foreach (var stop in truck.Schedule)
                _draftSchedule.Add(stop.Clone());
        }
        else if (truck.AssignedOriginIndustry != null)
        {
            _draftSchedule.Add(new ScheduleStop(ScheduleStopKind.Industry, truck.AssignedOriginIndustry.Id, ScheduleAction.Load));
            if (truck.AssignedTargetIndustry != null)
                _draftSchedule.Add(new ScheduleStop(ScheduleStopKind.Industry, truck.AssignedTargetIndustry.Id, ScheduleAction.Unload));
            else if (truck.AssignedTargetCity != null)
                _draftSchedule.Add(new ScheduleStop(ScheduleStopKind.City, truck.AssignedTargetCity.Id, ScheduleAction.Unload));
        }

        RefreshDraftScheduleList();
        SyncScheduleLinesDropdown();
    }

    private void RefreshDraftScheduleList()
    {
        if (_scheduleStopsListBox == null) return;
        foreach (Node child in _scheduleStopsListBox.GetChildren())
            child.QueueFree();

        if (_draftSchedule.Count == 0)
        {
            _scheduleStopsListBox.AddChild(CreateLabel("Nenhuma parada configurada (use '➕ Adicionar' abaixo).", new Color("6b7280"), 10));
            return;
        }

        Truck? activeTruck = _bridge.Engine.ActiveTrucks.Count > 0
            ? _bridge.Engine.ActiveTrucks[Mathf.Clamp(_selectedTruckIndex, 0, _bridge.Engine.ActiveTrucks.Count - 1)]
            : null;

        for (int i = 0; i < _draftSchedule.Count; i++)
        {
            int idx = i;
            var stop = _draftSchedule[i];
            bool isCurrent = activeTruck != null && activeTruck.HasManualSchedule && activeTruck.CurrentScheduleIndex == i;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            string actionLabel = stop.Action switch
            {
                ScheduleAction.Load => "Carregar",
                ScheduleAction.Unload => "Descarregar",
                ScheduleAction.PitStop => "Pit-Stop",
                _ => stop.Action.ToString()
            };
            string condLabel = stop.Action == ScheduleAction.Load && stop.Condition != DispatchCondition.Immediate
                ? (stop.Condition == DispatchCondition.WaitFullLoad ? " [100%]" : " [50%]")
                : string.Empty;
            string cargoLabel = stop.CargoFilter != ResourceType.None ? $" [{stop.CargoFilter}]" : string.Empty;
            string waitLabel = stop.MaxWaitHours > 0f ? $" [≤{stop.MaxWaitHours:F0}h]" : string.Empty;
            string prefix = isCurrent ? "▶ " : "";
            string targetName = _bridge.Engine.GetStopDisplayName(stop);

            var lbl = CreateLabel($"{prefix}{i + 1}. [{actionLabel}] {targetName}{condLabel}{cargoLabel}{waitLabel}",
                isCurrent ? new Color("0369a1") : new Color("1f2937"), 11);
            lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(lbl);

            var removeBtn = new Button { Text = "✕" };
            removeBtn.AddThemeFontSizeOverride("font_size", 10);
            removeBtn.Pressed += () =>
            {
                _draftSchedule.RemoveAt(idx);
                RefreshDraftScheduleList();
            };
            row.AddChild(removeBtn);

            _scheduleStopsListBox.AddChild(row);
        }
    }

    private void RefreshScheduleTab()
    {
        if (_scheduleTruckInfoText == null) return;
        var trucks = _bridge.Engine.ActiveTrucks;
        if (trucks.Count == 0)
        {
            SyncScheduleTruckDropdown(forceRebuild: true);
            _scheduleTruckInfoText.Text = "[color=#6b7280]Nenhum veículo ativo. Compre um caminhão na aba [b]Frota[/b].[/color]";
            return;
        }

        SyncScheduleTruckDropdown();
        SyncScheduleLinesDropdown();
        var t = trucks[_selectedTruckIndex];

        if (_schedulePitStopCheck != null)
            _schedulePitStopCheck.SetPressedNoSignal(t.AutoPitStopEnabled);

        var sharedLine = _bridge.Engine.GetLogisticsLine(t.AssignedLineId);
        string modeLabel = sharedLine != null
            ? $"[color=#7c3aed][b]Linha: {sharedLine.Name} ({t.Schedule.Count}P)[/b][/color]"
            : (t.HasManualSchedule
                ? $"[color=#0369a1][b]Agenda Multi-Paradas ({t.Schedule.Count})[/b][/color]"
                : "[color=#15803d][b]Despacho Automático (2-Estágios)[/b][/color]");
        string cargoUnit = t.Spec.IsPassengerBus ? "pass." : "un.";
        string cargoLabel = t.Spec.IsPassengerBus ? "Passageiros" : $"{t.LoadedCargoType}";
        string cargoText = t.LoadedCargoAmount > 0
            ? $"{t.LoadedCargoAmount}/{t.Spec.Capacity} {cargoUnit} {cargoLabel}"
            : $"Vazio (0/{t.Spec.Capacity} {cargoUnit})";

        string nextStopText = t.HasManualSchedule && t.Schedule.Count > 0
            ? _bridge.Engine.GetStopDisplayName(t.GetCurrentStop()!)
            : (t.AssignedTargetIndustry?.Name ?? t.AssignedTargetCity?.Name ?? "Livre");

        string statusPt = t.Status switch
        {
            TruckStatus.ParkedInGarage => "Na Garagem",
            TruckStatus.Idle => "Ocioso",
            TruckStatus.TravelingToPickup => "Indo Carregar",
            TruckStatus.Loading => "Carregando",
            TruckStatus.WaitingForCargo => t.IsHeldForHeadway ? "Aguardando Headway" : "Aguardando Carga",
            TruckStatus.TravelingToDelivery => "Em Entrega",
            TruckStatus.Unloading => "Descarregando",
            TruckStatus.TravelingToPitStop => "Indo p/ Pit-Stop",
            _ => t.Status.ToString()
        };

        float fuelSavePct = (1.0f - t.FuelCostMultiplier) * 100f;
        float hillPct = (t.HillClimbSpeedMultiplier - 1.0f) * 100f;
        string vetBonus = t.Rank > TruckRank.Rookie
            ? $" [color=#b45309](-{fuelSavePct:F0}% diesel | +{hillPct:F0}% serra)[/color]"
            : string.Empty;

        _scheduleTruckInfoText.Text =
            $"[b]{t.Name}[/b] ({t.Spec.Name} • {t.Spec.MaxTopSpeedKmh:F0} km/h)\n" +
            $"Veterania: [b]{t.RankDisplayLabel}[/b] ({t.TotalDistanceTravelledKm:F0} km){vetBonus}\n" +
            $"Modo: {modeLabel} | Estado: [b]{statusPt}[/b]\n" +
            $"Carga: [b]{cargoText}[/b] | Condição: [b]{t.Condition:F0}%[/b] | Próx.: [b]{nextStopText}[/b]";
    }

    private void RefreshFleetModernizerPanel()
    {
        if (_modernizerSourceLabel == null || _modernizerTargetSelect == null || _modernizerApplyBtn == null)
            return;

        var trucks = _bridge.Engine.ActiveTrucks;
        if (trucks.Count == 0)
        {
            _modernizerSourceLabel.Text = "Nenhum veículo ativo para modernizar.";
            _modernizerTargetSelect.Clear();
            _modernizerApplyBtn.Disabled = true;
            return;
        }

        _selectedTruckIndex = Mathf.Clamp(_selectedTruckIndex, 0, trucks.Count - 1);
        var current = trucks[_selectedTruckIndex];
        long tradeIn = (long)Math.Round(current.Spec.PurchasePrice * 0.60);
        _modernizerSourceLabel.Text = $"Atual: {current.Name} (Crédito troca 60%: R$ {tradeIn:N0})";

        var models = ProgressionManager.AvailableTruckModels;
        int prevSelected = _modernizerTargetSelect.Selected;
        _modernizerTargetSelect.Clear();
        int defaultTargetIdx = 0;
        for (int i = 0; i < models.Count; i++)
        {
            var m = models[i];
            long netCost = m.PurchasePrice - tradeIn;
            string deltaStr = netCost >= 0 ? $"Líq. R$ {netCost:N0}" : $"+R$ {-netCost:N0}";
            bool unlocked = _bridge.Engine.Research.IsTruckUnlocked(m, _bridge.Engine.Player.Level);
            string lockTag = unlocked ? "" : $" [Nv.{m.RequiredPlayerLevel}]";
            _modernizerTargetSelect.AddItem($"{m.Name} (Cap {m.Capacity} • {deltaStr}){lockTag}");
            _modernizerTargetSelect.SetItemDisabled(i, !unlocked || m.Name == current.Spec.Name);
            if (unlocked && m.PurchasePrice > current.Spec.PurchasePrice && defaultTargetIdx == 0)
                defaultTargetIdx = i;
        }

        int finalIdx = (prevSelected >= 0 && prevSelected < models.Count && !_modernizerTargetSelect.IsItemDisabled(prevSelected))
            ? prevSelected
            : defaultTargetIdx;
        if (models.Count > 0)
            _modernizerTargetSelect.Select(finalIdx);

        _modernizerApplyBtn.Disabled = models.Count == 0 || _modernizerTargetSelect.IsItemDisabled(finalIdx);
    }

    private void RefreshFleetActiveList()
    {
        if (_fleetActiveListBox == null) return;
        foreach (Node child in _fleetActiveListBox.GetChildren())
            child.QueueFree();

        var trucks = _bridge.Engine.ActiveTrucks;
        if (trucks.Count == 0)
        {
            _fleetActiveListBox.AddChild(CreateLabel("Nenhum veículo em operação.", new Color("6b7280"), 11));
            return;
        }

        for (int i = 0; i < trucks.Count; i++)
        {
            int idx = i;
            var t = trucks[i];
            var sharedLine = _bridge.Engine.GetLogisticsLine(t.AssignedLineId);
            string modeTag = sharedLine != null ? sharedLine.Name : (t.HasManualSchedule ? $"{t.Schedule.Count}P" : "Auto");
            string destName = t.HasManualSchedule && t.Schedule.Count > 0
                ? _bridge.Engine.GetStopDisplayName(t.GetCurrentStop()!)
                : (t.AssignedTargetIndustry?.Name ?? t.AssignedTargetCity?.Name ?? "Livre");

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            string rankShort = t.Rank switch
            {
                TruckRank.RoadLegend => "★★★",
                TruckRank.Veteran => "★★",
                _ => "★"
            };
            var btn = new Button
            {
                Text = $"#{i + 1} {rankShort} {t.Name} ({t.TotalDistanceTravelledKm:F0}km) [{modeTag}] • {t.LoadedCargoAmount}/{t.Spec.Capacity} → {destName}",
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += () =>
            {
                _selectedTruckIndex = idx;
                OpenWindowOnTab(WindowTab.Schedule);
            };
            row.AddChild(btn);

            TruckSpecification? nextSpec = null;
            var allModels = ProgressionManager.AvailableTruckModels;
            for (int mIdx = 0; mIdx < allModels.Count; mIdx++)
            {
                var candidate = allModels[mIdx];
                if (candidate.PurchasePrice > t.Spec.PurchasePrice &&
                    _bridge.Engine.Research.IsTruckUnlocked(candidate, _bridge.Engine.Player.Level))
                {
                    nextSpec = candidate;
                    break;
                }
            }
            if (nextSpec != null)
            {
                long tradeIn = (long)Math.Round(t.Spec.PurchasePrice * 0.60);
                long netUpgrade = nextSpec.PurchasePrice - tradeIn;
                var upgBtn = new Button { Text = "⬆️" };
                upgBtn.TooltipText = $"Modernizar {t.Name} → {nextSpec.Name} (Líquido R$ {netUpgrade:N0} c/ 60% troca)";
                upgBtn.AddThemeFontSizeOverride("font_size", 11);
                upgBtn.Pressed += () =>
                {
                    if (_bridge.TryModernizeTruck(t, nextSpec, out _, out string msg))
                    {
                        _selectedTruckIndex = idx;
                        SyncScheduleTruckDropdown(forceRebuild: true);
                        SyncScheduleSelectorsToCurrentTruck();
                        _audio?.PlayStructureBuilt();
                    }
                    else
                    {
                        _audio?.PlayError();
                    }
                    LogEvent(msg);
                    RefreshHud();
                };
                row.AddChild(upgBtn);
            }

            var cloneBtn = new Button { Text = "📋" };
            cloneBtn.TooltipText = $"Clonar {t.Name} + agenda (R$ {t.Spec.PurchasePrice:N0})";
            cloneBtn.AddThemeFontSizeOverride("font_size", 11);
            cloneBtn.Pressed += () =>
            {
                if (_bridge.TryCloneTruck(t, out _, out string msg))
                {
                    _selectedTruckIndex = _bridge.Engine.ActiveTrucks.Count - 1;
                    _audio?.PlayStructureBuilt();
                }
                else
                {
                    _audio?.PlayError();
                }
                LogEvent(msg);
                RefreshHud();
            };
            row.AddChild(cloneBtn);

            _fleetActiveListBox.AddChild(row);
        }
    }

    private void UpdateInspectorPanel()
    {
        int sx = _selectedCell.X;
        int sy = _selectedCell.Y;
        int w = _bridge.World.Grid.GetLength(0);
        int h = _bridge.World.Grid.GetLength(1);

        if (sx < 0 || sx >= w || sy < 0 || sy >= h)
        {
            _inspectorText.Text = "Clique em qualquer célula do mapa 3D para inspecionar.";
            if (_inspectorUpgradeIndustryBtn != null) _inspectorUpgradeIndustryBtn.Visible = false;
            if (_inspectorRepaveBtn != null) _inspectorRepaveBtn.Visible = false;
            if (_inspectorTrafficCtrlBtn != null) _inspectorTrafficCtrlBtn.Visible = false;
            if (_inspectorGarageAutoReplaceBtn != null) _inspectorGarageAutoReplaceBtn.Visible = false;
            if (_inspectorGarageCloneBtn != null) _inspectorGarageCloneBtn.Visible = false;
            return;
        }

        var cell = _bridge.World.Grid[sx, sy];
        var lines = new List<string>
        {
            $"[b]Célula ({sx}, {sy})[/b] — Bioma: {cell.Biome} | Terreno: {cell.Terrain}"
        };

        if (cell.HasRoad)
        {
            float pav = _bridge.Engine.GetCellPavementCondition(sx, sy);
            bool blocked = _bridge.Engine.IsCellBlockedByLandslide(sx, sy);
            long rev30 = _bridge.Engine.GetCellCorridorRevenue(sx, sy);
            bool isIntersection = _bridge.Engine.IsIntersectionCell(sx, sy);
            bool hasCtrl = _bridge.Engine.HasTrafficControlAt(sx, sy);
            float baseSpeed = cell.RoadLevel.GetSpeedLimitKmh();
            float pavMult = Math.Clamp(0.45f + (pav / 100f) * 0.55f, 0.45f, 1.0f);
            float blizzardMult = (_bridge.Engine.IsWinterBlizzardActive && cell.Biome == BiomeType.Alpine && cell.RoadLevel <= RoadTier.Gravel) ? 0.45f : 1.0f;
            float effSpeed = blocked ? 0f : baseSpeed * cell.RoadLevel.GetWeatherSpeedMultiplier(_bridge.Engine.CurrentWeather) * pavMult * blizzardMult;
            string stTag = cell.StructureType switch
            {
                RoadStructureType.Bridge => "🌉 Ponte",
                RoadStructureType.Tunnel => "🚇 Túnel em Encosta",
                _ => "🛣️ Via"
            };
            string pavColor = blocked || pav < 45f ? "#dc2626" : (pav < 75f ? "#d97706" : "#15803d");
            lines.Add($"{stTag}: [b]{cell.RoadLevel}[/b] ({effSpeed:F0} km/h)");
            lines.Add(blocked
                ? "[color=#dc2626][b]🚧 INTERDITADA POR DESLIZAMENTO[/b][/color]"
                : $"🧱 Pavimento: [color={pavColor}][b]{pav:F0}%[/b][/color] • 💹 Lucro 30d: [b]R$ {rev30:N0}[/b]");
            if (isIntersection)
            {
                lines.Add(hasCtrl
                    ? "🚦 Cruzamento Inteligente: [color=#15803d][b]ATIVO (+4 fluxo)[/b][/color]"
                    : "🔀 Entroncamento (3+ vias): [color=#b45309]Sem sinalização inteligente[/color]");
            }
            if (cell.RivalOwnerId > 0)
            {
                long rToll = _bridge.Engine.GetRivalTollAtTile(sx, sy, out var rivalOwner);
                lines.Add($"[color=#ea580c]🏢 Rodovia Rival ({rivalOwner?.Name ?? "Concorrente"}): Pedágio R$ {rToll:N0}/viagem — construa desvio![/color]");
            }
            if (_bridge.Engine.IsTileInCongestedDowntown(sx, sy, out var congCity, out float dtFactor))
            {
                int slowPct = Math.Clamp((int)Math.Round((1f - dtFactor) * 100f), 1, 80);
                lines.Add($"[color=#d97706]🚦 Trânsito Urbano Central ({congCity?.Name}): -{slowPct}% vel. — construa Anel Viário periférico![/color]");
            }

            if (_inspectorRepaveBtn != null)
            {
                long repaveCost = _bridge.Engine.EstimateRepaveTileCost(sx, sy);
                _inspectorRepaveBtn.Visible = repaveCost > 0;
                _inspectorRepaveBtn.Text = blocked
                    ? $"🚧 Desobstruir & Recapear (R$ {repaveCost:N0})"
                    : $"🛠️ Recapear Trecho ({pav:F0}% → 100% | R$ {repaveCost:N0})";
            }

            if (_inspectorTrafficCtrlBtn != null)
            {
                _inspectorTrafficCtrlBtn.Visible = isIntersection && !hasCtrl;
            }
        }
        else
        {
            if (_inspectorRepaveBtn != null) _inspectorRepaveBtn.Visible = false;
            if (_inspectorTrafficCtrlBtn != null) _inspectorTrafficCtrlBtn.Visible = false;
        }

        var toll = _bridge.Engine.TollStations.FirstOrDefault(t => t.GridX == sx && t.GridY == sy);
        if (toll != null)
        {
            lines.Add($"\n🚧 [b]{toll.Name}[/b]");
            lines.Add($"⚖️ Balança: [b]{toll.LastWeighDisplay}[/b]");
            lines.Add($"💰 Arrecadado: [b]R$ {toll.TotalTollCollected:N0}[/b] • Inspeções: [b]{toll.TrucksInspected}[/b]");
        }

        var city = _bridge.World.Cities.FirstOrDefault(c => c.UrbanTiles.Contains((sx, sy)));
        if (city != null)
        {
            float sat = city.Warehouse.CalculateSatisfactionRate() * 100f;
            string subTag = sat >= 80f ? " [color=#0284c7](🏛️ Subsídio Ativo)[/color]" : "";
            lines.Add($"\n🏙️ [b]{city.Name}[/b] (Pop: {city.Population} hab • Exp. Nv.{city.ExpansionLevel})");
            lines.Add($"🏘️ Bairros: [b]{city.ResidentialTileCount}[/b] Resid. • [b]{city.CommercialTileCount}[/b] Comerc.");
            float dtMult = city.GetDowntownTrafficSpeedMultiplier();
            if (dtMult < 0.999f)
            {
                lines.Add($"[color=#d97706]⚠️ Centro Congestionado: -{(1f - dtMult) * 100f:F0}% vel. (Anel Viário alivia rota)[/color]");
            }
            lines.Add($"📊 Satisfação: [b]{sat:F0}%[/b]{subTag}");
            if (city.TotalSubsidiesReceived > 0)
            {
                lines.Add($"🏛️ Subsídios Recebidos: [b]R$ {city.TotalSubsidiesReceived:N0}[/b]");
            }
            var demandSummary = city.Warehouse.DailyDemand
                .Select(kvp => $"{kvp.Key}: {city.Warehouse.StoredStock.GetValueOrDefault(kvp.Key, 0)}/{kvp.Value} dia");
            lines.Add($"📦 Demanda: {string.Join(" | ", demandSummary)}");
        }

        var ind = _bridge.World.Industries.FirstOrDefault(i => i.GridX == sx && i.GridY == sy);
        if (ind != null)
        {
            var conn = _bridge.Engine.GetIndustryConnections(ind);
            lines.Add($"\n🏭 [b]{ind.Name}[/b] (Planta Nv.{ind.UpgradeLevel}/{ind.MaxUpgradeLevel})");
            if (ind.RequiredInputResource != ResourceType.None)
            {
                lines.Add($"📥 Consome: [b]{ind.RequiredInputResource}[/b] (Estoque: {ind.InputStock}/{ind.MaxStock})");
            }
            if (ind.SecondaryInputResource != ResourceType.None)
            {
                lines.Add($"⚡ Catalisador (+50% Prod.): [b]{ind.SecondaryInputResource}[/b] ({ind.SecondaryInputStock}/{ind.MaxStock})");
            }
            if (ind.TertiaryInputResource != ResourceType.None)
            {
                lines.Add($"🛢️ Insumo Especial (+25% Prod.): [b]{ind.TertiaryInputResource}[/b] ({ind.TertiaryInputStock}/{ind.MaxStock})");
            }
            lines.Add($"📤 Produz: [b]{ind.OutputResource}[/b] (+{ind.ProductionRatePerDay}/dia{(conn.DailyProductionBonus > 0 ? $" [color=#15803d]+{conn.DailyProductionBonus} bônus[/color]" : "")})");
            lines.Add($"🔌 Módulos Conectados: [b]{conn.RoadDockCount} doca(s)[/b] • [b]{conn.FacilityBridgeCount} ponte(s)[/b] • [b]{conn.NearbyHubCount} hub(s)[/b]");
            lines.Add($"📊 Saída: [b]{ind.OutputStock}/{ind.MaxStock} un.[/b]");

            if (_inspectorUpgradeIndustryBtn != null)
            {
                _inspectorUpgradeIndustryBtn.Visible = true;
                _inspectorUpgradeIndustryBtn.Disabled = !ind.CanUpgrade;
                _inspectorUpgradeIndustryBtn.Text = ind.CanUpgrade
                    ? $"⬆️ Expandir Planta p/ Nv.{ind.UpgradeLevel + 1} (R$ {ind.GetNextUpgradeCost():N0})"
                    : $"✅ Planta no Nível Máximo (Nv.{ind.MaxUpgradeLevel})";
            }
        }
        else if (_inspectorUpgradeIndustryBtn != null)
        {
            _inspectorUpgradeIndustryBtn.Visible = false;
        }

        var garage = _bridge.Engine.Garages.FirstOrDefault(g => g.GridX == sx && g.GridY == sy);
        if (garage != null)
        {
            lines.Add($"\n🔧 [b]{garage.Name}[/b]");
            lines.Add($"🚛 Frota: {garage.StationedTrucks.Count}/{garage.MaxCapacity} caminhões");
            string fuelBonusTag = garage.FuelStock > 0 ? " [color=#15803d](-35% Custo/km ATIVO)[/color]" : " (Abasteça c/ Fuel)";
            lines.Add($"⛽ Diesel: [b]{garage.FuelStock}/{garage.MaxFuelCapacity} un.[/b]{fuelBonusTag}");
            if (garage.AutoReplaceEnabled)
                lines.Add($"♻️ Auto-Substituição: [color=#0369a1][b]ATIVA[/b][/color] (Condição ≤ {garage.AutoReplaceMinCondition:F0}%)");

            if (_inspectorGarageAutoReplaceBtn != null)
            {
                _inspectorGarageAutoReplaceBtn.Visible = true;
                _inspectorGarageAutoReplaceBtn.Text = garage.AutoReplaceEnabled
                    ? "♻️ Auto-Substituir (Garagem): ON (≤25%)"
                    : "♻️ Auto-Substituir (Garagem): OFF";
            }
            if (_inspectorGarageCloneBtn != null)
            {
                _inspectorGarageCloneBtn.Visible = true;
                _inspectorGarageCloneBtn.Disabled = garage.IsFull || _bridge.Engine.ActiveTrucks.Count == 0;
            }
        }
        else
        {
            if (_inspectorGarageAutoReplaceBtn != null) _inspectorGarageAutoReplaceBtn.Visible = false;
            if (_inspectorGarageCloneBtn != null) _inspectorGarageCloneBtn.Visible = false;
        }

        var hub = _bridge.Engine.LogisticsHubs.FirstOrDefault(l => l.GridX == sx && l.GridY == sy);
        if (hub != null)
        {
            lines.Add($"\n📦 [b]{hub.Name}[/b]");
            lines.Add("✨ +20% Frete & Pit-Stop Gratuito");
            string hubFuelTag = hub.FuelStock > 0 ? " [color=#15803d](-35% Custo/km num raio 22q ATIVO)[/color]" : " (Abasteça c/ Fuel)";
            lines.Add($"⛽ Diesel Regional: [b]{hub.FuelStock}/{hub.MaxFuelCapacity} un.[/b]{hubFuelTag}");
            lines.Add($"🏭 Armazém: {hub.CurrentTotalStored}/{hub.MaxStorageCapacity} un.");
            foreach (var kv in hub.StoredCargo.Where(k => k.Value > 0))
            {
                lines.Add($"  • {kv.Key}: [b]{kv.Value} un.[/b]");
            }
        }

        var busTerminal = _bridge.Engine.BusTerminals.FirstOrDefault(b => b.GridX == sx && b.GridY == sy);
        if (busTerminal != null)
        {
            lines.Add($"\n🚌 [b]{busTerminal.Name}[/b] (Terminal Rodoviário)");
            lines.Add($"👥 Passageiros Aguardando: [b]{busTerminal.WaitingPassengers}/{busTerminal.MaxPassengers}[/b]");
            lines.Add($"🎫 Atendidos Total: [b]{busTerminal.TotalPassengersServed:N0} pass.[/b]");
            lines.Add($"💰 Receita de Bilhetes: [b]R$ {busTerminal.TotalTicketRevenue:N0}[/b]");
        }

        var seaport = _bridge.World.Seaports.FirstOrDefault(s => s.GridX == sx && s.GridY == sy);
        if (seaport != null)
        {
            string shipStatus = seaport.IsShipDocked
                ? $"[color=#0284c7][b]⚓ Navio Atracado (+45% Exportação)[/b][/color] • {seaport.CycleHoursRemaining:F0}h p/ zarpar"
                : $"[color=#64748b]🌊 Navio em Alto-Mar[/color] • retorna em {seaport.CycleHoursRemaining:F0}h";
            lines.Add($"\n⚓ [b]{seaport.Name}[/b] (Terminal Portuário Oceânico)");
            if (seaport.PartnerSeaportId.HasValue)
            {
                var partner = _bridge.World.Seaports.FirstOrDefault(p => p.Id == seaport.PartnerSeaportId.Value);
                if (partner != null)
                    lines.Add($"🧭 Rota Marítima Exclusiva: [b]↔ {partner.Name}[/b] (outro continente)");
            }
            lines.Add(shipStatus);
            lines.Add($"🚢 Porão do Navio: [b]{seaport.CurrentShipCargo}/{seaport.MaxShipCapacity} un.[/b]");
            if (seaport.TransitCargo.Values.Any(v => v > 0))
            {
                string stockTxt = string.Join(", ", seaport.TransitCargo.Where(k => k.Value > 0).Select(k => $"{k.Key}: {k.Value}"));
                lines.Add($"📦 Carga Importada no Cais (Carregar): [b]{stockTxt}[/b]");
            }
            lines.Add("📥 Aceita no Cais: Grain, Steel, Lumber, Goods, Timber (e frete marítimo)");
            lines.Add($"💰 Exportado Total: [b]{seaport.TotalExportedUnits:N0} un.[/b] (R$ {seaport.TotalExportRevenue:N0})");
        }

        var landmark = _bridge.World.Landmarks.FirstOrDefault(l => l.GridX == sx && l.GridY == sy);
        if (landmark != null)
        {
            lines.Add($"\n🏗️ [b]{landmark.Name}[/b]");
            lines.Add($"📐 Progresso Global: [b]{landmark.CompletionPercentage * 100f:F0}%[/b] ({(landmark.IsCompleted ? "✅ CONCLUÍDA" : $"Etapa {landmark.CurrentStage}/{RegionalLandmark.MaxStages}")})");
            lines.Add($"✨ Bônus Permanente: [color=#15803d]{landmark.BonusSummary}[/color]");
            if (!landmark.IsCompleted)
            {
                lines.Add("📦 Insumos p/ Etapa Atual:");
                foreach (var kv in landmark.StageRequired)
                {
                    int deliv = landmark.StageDelivered.GetValueOrDefault(kv.Key, 0);
                    lines.Add($"  • {kv.Key}: [b]{deliv}/{kv.Value} un.[/b]");
                }
            }
        }

        AppendRailInspectorDetails(lines, sx, sy);

        _inspectorText.Text = string.Join("\n", lines);
    }

    private static string GetCityBannerText(City city)
    {
        int sat = Mathf.RoundToInt(city.Warehouse.CalculateSatisfactionRate() * 100f);
        return $"🏙️ {city.Name}\n👥 {city.Population} | 📦 {sat}%";
    }

    private static string GetIndustryBannerText(Industry ind)
    {
        if (ind.RequiredInputResource != ResourceType.None)
        {
            string extra = ind.TertiaryInputResource != ResourceType.None
                ? $" +{ind.TertiaryInputResource} {ind.TertiaryInputStock}"
                : string.Empty;
            return $"🏭 {ind.Name}\n[In: {ind.RequiredInputResource} {ind.InputStock}{extra} → Out: {ind.OutputResource} {ind.OutputStock}]";
        }
        return $"🏭 {ind.Name}\n[{ind.OutputResource}: {ind.OutputStock} un.]";
    }

    public static Vector3 GridToWorld(int gridX, int gridY)
    {
        return new Vector3(gridX * ProceduralModels3D.TileSize, 0f, gridY * ProceduralModels3D.TileSize);
    }

    // =========================================================================
    // Sprint E3 — Showroom 3D Interativo & Oficina de Pintura / Identidade
    // =========================================================================

    private void EnsureShowcaseBuilt()
    {
        if (_showcaseBuilt || _fleetSubBodyShowcase == null)
            return;
        _showcaseBuilt = true;
        BuildShowcaseAndLiverySection(_fleetSubBodyShowcase);
    }

    private void SwitchFleetSubTab(int index)
    {
        if (index == 1)
            EnsureShowcaseBuilt();

        _fleetSubTabIndex = index;
        _fleetSubBodyBuy.Visible = index == 0;
        _fleetSubBodyShowcase.Visible = index == 1;
        _fleetSubBodyActive.Visible = index == 2;

        Button[] tabs = { _fleetSubTabBuyBtn, _fleetSubTabShowcaseBtn, _fleetSubTabActiveBtn };
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].Disabled = i == index;
        }

        if (index == 1)
        {
            SyncLiveryEditorScopeDropdown();
            if (!_liveryDirtyFromUser)
                LoadSelectedScopeLiveryIntoEditor();
            _showcaseRenderedKey = string.Empty;
        }
    }

    private void BuildShowcaseAndLiverySection(Control parent)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(content);
        parent.AddChild(scroll);

        // Viewport 3D isolado com plataforma giratória (arraste com o mouse + zoom)
        _showcaseViewportContainer = new SubViewportContainer
        {
            Stretch = true,
            CustomMinimumSize = new Vector2(0, 145),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Arraste com o botão esquerdo para girar 360° • Roda do mouse para Zoom"
        };
        _showcaseViewportContainer.GuiInput += OnShowcaseViewportGuiInput;

        var subVp = new SubViewport
        {
            Size = new Vector2I(404, 145),
            OwnWorld3D = true,
            World3D = new World3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible
        };

        var worldEnv = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("161e2e"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("dbeafe"),
                AmbientLightEnergy = 1.05f
            }
        };
        subVp.AddChild(worldEnv);

        var sun = new DirectionalLight3D
        {
            LightColor = new Color("fff7ed"),
            LightEnergy = 1.65f,
            RotationDegrees = new Vector3(-42f, 38f, 0f)
        };
        subVp.AddChild(sun);

        var fillLight = new DirectionalLight3D
        {
            LightColor = new Color("93c5fd"),
            LightEnergy = 0.55f,
            RotationDegrees = new Vector3(-25f, -140f, 0f)
        };
        subVp.AddChild(fillLight);

        _showcaseTurntable = new Node3D { Name = "ShowcaseTurntable" };
        var stageMesh = new CylinderMesh { TopRadius = 1.78f, BottomRadius = 1.88f, Height = 0.08f, RadialSegments = 32 };
        var stageMat = new StandardMaterial3D { AlbedoColor = new Color("1e293b"), Metallic = 0.55f, Roughness = 0.35f };
        var stageRim = new MeshInstance3D { Mesh = stageMesh, MaterialOverride = stageMat, Position = new Vector3(0f, -0.04f, 0f) };
        _showcaseTurntable.AddChild(stageRim);
        subVp.AddChild(_showcaseTurntable);

        _showcaseCamera = new Camera3D { Fov = 38f, Current = true };
        subVp.AddChild(_showcaseCamera);

        _showcaseViewportContainer.AddChild(subVp);
        content.AddChild(_showcaseViewportContainer);

        // Barra de controles 3D (Modelo, Faróis, Carga 3D, Veterania)
        var ctrlRow = new HBoxContainer();
        ctrlRow.AddThemeConstantOverride("separation", 4);
        _showcaseModelSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _showcaseModelSelect.AddThemeFontSizeOverride("font_size", 10);
        foreach (var m in ProgressionManager.AvailableTruckModels)
            _showcaseModelSelect.AddItem(m.Name);
        _showcaseModelSelect.ItemSelected += _ => _showcaseRenderedKey = string.Empty;
        ctrlRow.AddChild(_showcaseModelSelect);

        _showcaseHeadlightsBtn = new Button { Text = "💡 Faróis" };
        _showcaseHeadlightsBtn.AddThemeFontSizeOverride("font_size", 10);
        _showcaseHeadlightsBtn.Pressed += () =>
        {
            _showcaseHeadlightsOn = !_showcaseHeadlightsOn;
            if (_showcaseHandle != null)
                _showcaseHandle.Headlights.Visible = _showcaseHeadlightsOn;
            _showcaseHeadlightsBtn.Text = _showcaseHeadlightsOn ? "💡 ON" : "💡 OFF";
        };
        _showcaseHeadlightsBtn.Text = "💡 ON";
        ctrlRow.AddChild(_showcaseHeadlightsBtn);

        _showcaseCargoSelect = new OptionButton();
        _showcaseCargoSelect.AddThemeFontSizeOverride("font_size", 10);
        foreach (ResourceType rt in Enum.GetValues(typeof(ResourceType)))
            _showcaseCargoSelect.AddItem(rt == ResourceType.None ? "📦 Vazio" : $"📦 {rt}");
        _showcaseCargoSelect.ItemSelected += _ => _showcaseRenderedKey = string.Empty;
        ctrlRow.AddChild(_showcaseCargoSelect);

        _showcaseRankSelect = new OptionButton();
        _showcaseRankSelect.AddThemeFontSizeOverride("font_size", 10);
        _showcaseRankSelect.AddItem("★ Novato");
        _showcaseRankSelect.AddItem("★★ Veterano");
        _showcaseRankSelect.AddItem("★★★ Lenda");
        _showcaseRankSelect.ItemSelected += _ => _showcaseRenderedKey = string.Empty;
        ctrlRow.AddChild(_showcaseRankSelect);
        content.AddChild(ctrlRow);

        content.AddChild(CreateLabel("OFICINA DE PINTURA & IDENTIDADE DA TRANSPORTADORA", new Color("4b5563"), 10));

        var companyRow = new HBoxContainer();
        companyRow.AddThemeConstantOverride("separation", 4);
        _liveryCompanyNameEdit = new LineEdit
        {
            PlaceholderText = "Nome da Transportadora",
            Text = _bridge.Engine.Player.CompanyName,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _liveryCompanyNameEdit.AddThemeFontSizeOverride("font_size", 11);
        _liveryCompanyNameEdit.TextChanged += _ => _liveryDirtyFromUser = true;
        companyRow.AddChild(_liveryCompanyNameEdit);

        _liveryScopeSelect = new OptionButton { CustomMinimumSize = new Vector2(150, 0) };
        _liveryScopeSelect.AddThemeFontSizeOverride("font_size", 10);
        _liveryScopeSelect.ItemSelected += _ =>
        {
            _liveryDirtyFromUser = false;
            LoadSelectedScopeLiveryIntoEditor();
        };
        companyRow.AddChild(_liveryScopeSelect);
        content.AddChild(companyRow);

        var colorRow = new HBoxContainer();
        colorRow.AddThemeConstantOverride("separation", 4);
        colorRow.AddChild(CreateLabel("Cabine:", new Color("1f2937"), 10));
        _liveryPrimaryColorBtn = new ColorPickerButton
        {
            Color = new Color("#1d4ed8"),
            EditAlpha = false,
            CustomMinimumSize = new Vector2(34, 24)
        };
        _liveryPrimaryColorBtn.ColorChanged += _ =>
        {
            _liveryDirtyFromUser = true;
            _showcaseRenderedKey = string.Empty;
        };
        colorRow.AddChild(_liveryPrimaryColorBtn);

        string[] paletteHexes = { "#1d4ed8", "#dc2626", "#15803d", "#ea580c", "#18181b", "#7c3aed" };
        foreach (string hex in paletteHexes)
        {
            var swatchBtn = new Button { Text = " ", CustomMinimumSize = new Vector2(18, 22), TooltipText = hex };
            var swStyle = new StyleBoxFlat { BgColor = new Color(hex), CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3 };
            swatchBtn.AddThemeStyleboxOverride("normal", swStyle);
            swatchBtn.AddThemeStyleboxOverride("hover", swStyle);
            string h = hex;
            swatchBtn.Pressed += () =>
            {
                _liveryPrimaryColorBtn.Color = new Color(h);
                _liveryDirtyFromUser = true;
                _showcaseRenderedKey = string.Empty;
            };
            colorRow.AddChild(swatchBtn);
        }

        colorRow.AddChild(CreateLabel("Faixa:", new Color("1f2937"), 10));
        _liveryStripeColorBtn = new ColorPickerButton
        {
            Color = new Color("#facc15"),
            EditAlpha = false,
            CustomMinimumSize = new Vector2(34, 24)
        };
        _liveryStripeColorBtn.ColorChanged += _ =>
        {
            _liveryDirtyFromUser = true;
            _showcaseRenderedKey = string.Empty;
        };
        colorRow.AddChild(_liveryStripeColorBtn);

        _liveryFinishSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _liveryFinishSelect.AddThemeFontSizeOverride("font_size", 10);
        _liveryFinishSelect.AddItem("Metálico");
        _liveryFinishSelect.AddItem("Fosco");
        _liveryFinishSelect.AddItem("Cromado");
        _liveryFinishSelect.ItemSelected += _ =>
        {
            _liveryDirtyFromUser = true;
            _showcaseRenderedKey = string.Empty;
        };
        colorRow.AddChild(_liveryFinishSelect);
        content.AddChild(colorRow);

        var applyRow = new HBoxContainer();
        applyRow.AddThemeConstantOverride("separation", 6);
        _liveryApplyBtn = new Button
        {
            Text = "💾 Aplicar Pintura & Identidade",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _liveryApplyBtn.AddThemeFontSizeOverride("font_size", 11);
        _liveryApplyBtn.Pressed += ApplyLiveryFromEditor;
        applyRow.AddChild(_liveryApplyBtn);

        _liveryResetLineBtn = new Button { Text = "↩️ Padrão", Visible = false };
        _liveryResetLineBtn.AddThemeFontSizeOverride("font_size", 10);
        _liveryResetLineBtn.Pressed += ResetLineLiveryFromEditor;
        applyRow.AddChild(_liveryResetLineBtn);
        content.AddChild(applyRow);
    }

    private void OnShowcaseViewportGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _showcaseDragging = mb.Pressed;
                if (mb.Pressed)
                    _showcaseAutoSpin = false;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _showcaseZoom = Mathf.Clamp(_showcaseZoom - 0.18f, 1.45f, 4.40f);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _showcaseZoom = Mathf.Clamp(_showcaseZoom + 0.18f, 1.45f, 4.40f);
            }
        }
        else if (@event is InputEventMouseMotion mm && _showcaseDragging)
        {
            _showcaseYaw -= mm.Relative.X * 0.012f;
            _showcasePitch = Mathf.Clamp(_showcasePitch - mm.Relative.Y * 0.010f, -0.82f, -0.10f);
        }
    }

    private void UpdateShowcaseTurntable(float dt)
    {
        if (_showcaseTurntable == null || _showcaseCamera == null || !_topLeftWindow.Visible || _activeTab != WindowTab.Fleet || _fleetSubTabIndex != 1)
            return;

        if (_showcaseAutoSpin && !_showcaseDragging)
            _showcaseYaw += dt * 0.65f;

        RebuildShowcaseTruckMesh();

        Vector3 offset = new Vector3(
            Mathf.Sin(_showcaseYaw) * Mathf.Cos(_showcasePitch) * _showcaseZoom,
            -Mathf.Sin(_showcasePitch) * _showcaseZoom + 0.25f,
            Mathf.Cos(_showcaseYaw) * Mathf.Cos(_showcasePitch) * _showcaseZoom
        );
        _showcaseCamera.Position = offset;
        _showcaseCamera.LookAt(new Vector3(0f, 0.26f, 0f), Vector3.Up);
    }

    private void RebuildShowcaseTruckMesh()
    {
        if (_showcaseTurntable == null || _showcaseModelSelect == null)
            return;

        int modelIdx = Math.Clamp(_showcaseModelSelect.Selected, 0, ProgressionManager.AvailableTruckModels.Count - 1);
        var spec = ProgressionManager.AvailableTruckModels[modelIdx];
        var cargoValues = (ResourceType[])Enum.GetValues(typeof(ResourceType));
        int cargoIdx = Math.Clamp(_showcaseCargoSelect.Selected, 0, cargoValues.Length - 1);
        ResourceType cargoType = cargoValues[cargoIdx];
        int rankIdx = Math.Clamp(_showcaseRankSelect.Selected, 0, 2);
        FleetLivery draft = GetDraftLiveryFromEditor(customized: true);

        string renderKey = $"{modelIdx}|{cargoType}|{rankIdx}|{draft.GetRenderKey()}";
        if (_showcaseRenderedKey == renderKey && _showcaseHandle != null)
            return;

        _showcaseRenderedKey = renderKey;
        if (_showcaseHandle != null && IsInstanceValid(_showcaseHandle.Root))
            _showcaseHandle.Root.QueueFree();

        var previewTruck = new Truck(0, spec.Name, spec)
        {
            LoadedCargoType = cargoType,
            LoadedCargoAmount = cargoType == ResourceType.None ? 0 : spec.Capacity
        };
        float previewDist = rankIdx switch
        {
            1 => Truck.VeteranKmThreshold,
            2 => Truck.RoadLegendKmThreshold,
            _ => 0f
        };
        if (previewDist > 0f)
        {
            var pEdge = new RoadEdge(0, new RoadNode(0, 0, 0), new RoadNode(1, 1, 0), RoadTier.Asphalt, 500f);
            previewTruck.AssignPath(new[] { pEdge });
            previewTruck.Advance(previewDist / Mathf.Max(1f, spec.MaxTopSpeedKmh), WeatherCondition.Clear, 1.0f);
        }

        _showcaseHandle = ProceduralModels3D.CreateTruckMeshNode(previewTruck, draft);
        _showcaseHandle.Headlights.Visible = _showcaseHeadlightsOn;
        _showcaseTurntable.AddChild(_showcaseHandle.Root);
    }

    private FleetLivery GetDraftLiveryFromEditor(bool customized)
    {
        PaintFinish finish = _liveryFinishSelect.Selected switch
        {
            1 => PaintFinish.Matte,
            2 => PaintFinish.Chrome,
            _ => PaintFinish.Metallic
        };
        return new FleetLivery
        {
            IsCustomized = customized,
            PrimaryColorHex = $"#{_liveryPrimaryColorBtn.Color.ToHtml(false)}",
            StripeColorHex = $"#{_liveryStripeColorBtn.Color.ToHtml(false)}",
            Finish = finish
        };
    }

    private void SyncLiveryEditorScopeDropdown()
    {
        if (_liveryScopeSelect == null) return;
        int prevSel = _liveryScopeSelect.Selected;
        _liveryScopeSelect.Clear();
        _liveryScopeSelect.AddItem("🌐 Toda a Frota (Empresa)");
        foreach (var line in _bridge.Engine.LogisticsLines)
            _liveryScopeSelect.AddItem($"📋 Linha: {line.Name}");

        if (prevSel >= 0 && prevSel < _liveryScopeSelect.ItemCount)
            _liveryScopeSelect.Select(prevSel);
        else
            _liveryScopeSelect.Select(0);

        if (_liveryResetLineBtn != null)
            _liveryResetLineBtn.Visible = _liveryScopeSelect.Selected > 0;
    }

    private void LoadSelectedScopeLiveryIntoEditor()
    {
        if (_liveryScopeSelect == null) return;
        _liveryCompanyNameEdit.Text = _bridge.Engine.Player.CompanyName;

        int scopeIdx = _liveryScopeSelect.Selected;
        FleetLivery source = _bridge.Engine.Player.CompanyLivery;
        if (scopeIdx > 0 && scopeIdx - 1 < _bridge.Engine.LogisticsLines.Count)
        {
            var line = _bridge.Engine.LogisticsLines[scopeIdx - 1];
            if (line.CustomLivery != null)
                source = line.CustomLivery;
        }

        _liveryPrimaryColorBtn.Color = new Color(source.PrimaryColorHex);
        _liveryStripeColorBtn.Color = new Color(source.StripeColorHex);
        _liveryFinishSelect.Select(source.Finish switch
        {
            PaintFinish.Matte => 1,
            PaintFinish.Chrome => 2,
            _ => 0
        });
        if (_liveryResetLineBtn != null)
            _liveryResetLineBtn.Visible = scopeIdx > 0;
        _showcaseRenderedKey = string.Empty;
    }

    private void ApplyLiveryFromEditor()
    {
        int scopeIdx = _liveryScopeSelect.Selected;
        FleetLivery livery = GetDraftLiveryFromEditor(customized: true);

        if (scopeIdx <= 0)
        {
            _bridge.UpdateCompanyIdentityAndLivery(_liveryCompanyNameEdit.Text, livery);
            LogEvent($"🎨 Identidade atualizada: [b]{_bridge.Engine.Player.CompanyName}[/b] ({livery.PrimaryColorHex} / {livery.StripeColorHex}, {livery.Finish}).");
        }
        else if (scopeIdx - 1 < _bridge.Engine.LogisticsLines.Count)
        {
            var line = _bridge.Engine.LogisticsLines[scopeIdx - 1];
            _bridge.SetLineLivery(line.Id, livery);
            LogEvent($"🎨 Pintura exclusiva aplicada na linha [b]{line.Name}[/b]!");
        }

        _liveryDirtyFromUser = false;
        _audio?.PlayStructureBuilt();
        SyncFleet3DTransforms(0.01f);
        RefreshHud();
    }

    private void ResetLineLiveryFromEditor()
    {
        int scopeIdx = _liveryScopeSelect.Selected;
        if (scopeIdx > 0 && scopeIdx - 1 < _bridge.Engine.LogisticsLines.Count)
        {
            var line = _bridge.Engine.LogisticsLines[scopeIdx - 1];
            _bridge.SetLineLivery(line.Id, null);
            LogEvent($"↩️ Linha [b]{line.Name}[/b] voltou a herdar a pintura padrão da transportadora.");
        }
        _liveryDirtyFromUser = false;
        LoadSelectedScopeLiveryIntoEditor();
        _audio?.PlayUiClick();
        SyncFleet3DTransforms(0.01f);
        RefreshHud();
    }

    // =========================================================================
    // Sprint E6 — Diagnóstico de Gargalos & Conquistas/Milestones
    // =========================================================================

    private void BuildOperationsTab(MarginContainer bodyMargin)
    {
        var opsBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        opsBox.AddThemeConstantOverride("separation", 6);
        _tabBodyOperations = opsBox;

        var subTabRow = new HBoxContainer();
        subTabRow.AddThemeConstantOverride("separation", 4);
        _opsSubTabDiagBtn = new Button { Text = "🩺 Gargalos", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _opsSubTabDiagBtn.AddThemeFontSizeOverride("font_size", 11);
        _opsSubTabDiagBtn.Pressed += () => SwitchOperationsSubTab(0);

        _opsSubTabMilestoneBtn = new Button { Text = "🏆 Conquistas", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _opsSubTabMilestoneBtn.AddThemeFontSizeOverride("font_size", 11);
        _opsSubTabMilestoneBtn.Pressed += () => SwitchOperationsSubTab(1);

        subTabRow.AddChild(_opsSubTabDiagBtn);
        subTabRow.AddChild(_opsSubTabMilestoneBtn);
        opsBox.AddChild(subTabRow);

        // Sub-aba 0: Bottleneck Analyzer
        var diagBody = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        diagBody.AddThemeConstantOverride("separation", 5);
        _opsSubBodyDiag = diagBody;

        var diagTop = new HBoxContainer();
        diagTop.AddThemeConstantOverride("separation", 6);
        _bottleneckSummaryLabel = CreateLabel("Analisando rede...", new Color("1f2937"), 11);
        _bottleneckSummaryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        diagTop.AddChild(_bottleneckSummaryLabel);

        var reanalyzeBtn = new Button { Text = "🔄 Reanalisar" };
        reanalyzeBtn.AddThemeFontSizeOverride("font_size", 11);
        reanalyzeBtn.Pressed += () =>
        {
            _audio?.PlayUiClick();
            RefreshOperationsTab();
        };
        diagTop.AddChild(reanalyzeBtn);
        diagBody.AddChild(diagTop);

        var diagScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _bottleneckListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bottleneckListBox.AddThemeConstantOverride("separation", 5);
        diagScroll.AddChild(_bottleneckListBox);
        diagBody.AddChild(diagScroll);
        opsBox.AddChild(diagBody);

        // Sub-aba 1: Milestones & Troféus
        var mileBody = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        mileBody.AddThemeConstantOverride("separation", 5);
        _opsSubBodyMilestones = mileBody;

        _milestoneSummaryLabel = CreateLabel("🏆 Marcos Históricos: 0/6", new Color("1f2937"), 11);
        mileBody.AddChild(_milestoneSummaryLabel);

        var mileScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _milestoneListBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _milestoneListBox.AddThemeConstantOverride("separation", 5);
        mileScroll.AddChild(_milestoneListBox);
        mileBody.AddChild(mileScroll);
        opsBox.AddChild(mileBody);

        SwitchOperationsSubTab(0);
        bodyMargin.AddChild(opsBox);
    }

    private void SwitchOperationsSubTab(int index)
    {
        _opsSubTabIndex = index;
        if (_opsSubBodyDiag != null) _opsSubBodyDiag.Visible = index == 0;
        if (_opsSubBodyMilestones != null) _opsSubBodyMilestones.Visible = index == 1;
        if (_opsSubTabDiagBtn != null)
            _opsSubTabDiagBtn.Modulate = index == 0 ? new Color("ffffff") : new Color("cbd5e1");
        if (_opsSubTabMilestoneBtn != null)
            _opsSubTabMilestoneBtn.Modulate = index == 1 ? new Color("ffffff") : new Color("cbd5e1");
        RefreshOperationsTab();
    }

    private void RefreshOperationsBadgeAndTab(bool forceDiagnose = false)
    {
        if (!forceDiagnose)
        {
            _opsDiagTimer += 0.30f;
            if (_opsDiagTimer < 1.50f)
                return;
        }
        _opsDiagTimer = 0f;

        var bottlenecks = _bridge.Engine.DiagnoseBottlenecks();
        int criticalCount = 0;
        for (int i = 0; i < bottlenecks.Count; i++)
        {
            if (bottlenecks[i].Severity == BottleneckSeverity.Critical)
                criticalCount++;
        }

        if ((bottlenecks.Count != _lastOpsCount || criticalCount != _lastOpsCriticalCount) &&
            _tabButtons.TryGetValue(WindowTab.Operations, out var opBtn) && IsInstanceValid(opBtn))
        {
            _lastOpsCount = bottlenecks.Count;
            _lastOpsCriticalCount = criticalCount;
            opBtn.Text = bottlenecks.Count > 0 ? $"Oper. ({bottlenecks.Count})" : "Operações";
            opBtn.Modulate = criticalCount > 0 ? new Color("fca5a5") : new Color("ffffff");
        }

        if (_topLeftWindow.Visible && _activeTab == WindowTab.Operations)
        {
            RenderOperationsTabContent(bottlenecks);
        }
    }

    private void RefreshOperationsTab()
    {
        if (_bottleneckListBox == null || _milestoneListBox == null) return;
        RenderOperationsTabContent(_bridge.Engine.DiagnoseBottlenecks());
    }

    private void RenderOperationsTabContent(List<OperationalBottleneck> bottlenecks)
    {
        if (_opsSubTabIndex == 0)
        {
            foreach (Node child in _bottleneckListBox.GetChildren())
                child.QueueFree();

            int crit = bottlenecks.Count(b => b.Severity == BottleneckSeverity.Critical);
            int warn = bottlenecks.Count - crit;
            _bottleneckSummaryLabel.Text = bottlenecks.Count == 0
                ? "✅ Rede fluida — nenhum gargalo crítico!"
                : $"🩺 {crit} crítico(s) • {warn} alerta(s) detectado(s)";
            _bottleneckSummaryLabel.AddThemeColorOverride(
                "font_color",
                crit > 0 ? new Color("b91c1c") : (warn > 0 ? new Color("b45309") : new Color("15803d")));

            if (bottlenecks.Count == 0)
            {
                var okLbl = CreateLabel("Sua malha viária, fábricas e cidades estão operando sem gargalos no momento.", new Color("475569"), 11);
                okLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                _bottleneckListBox.AddChild(okLbl);
                return;
            }

            foreach (var b in bottlenecks.Take(16))
            {
                var card = new PanelContainer();
                card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = b.Severity == BottleneckSeverity.Critical ? new Color("fef2f2") : new Color("fffbeb"),
                    BorderColor = b.Severity == BottleneckSeverity.Critical ? new Color("ef4444") : new Color("f59e0b"),
                    BorderWidthLeft = 3,
                    CornerRadiusTopRight = 5,
                    CornerRadiusBottomRight = 5,
                    ContentMarginLeft = 8,
                    ContentMarginRight = 8,
                    ContentMarginTop = 5,
                    ContentMarginBottom = 5
                });

                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);

                var infoCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                infoCol.AddThemeConstantOverride("separation", 2);

                string sevTag = b.Severity == BottleneckSeverity.Critical ? "🔴 CRÍTICO" : "🟡 ALERTA";
                var title = CreateLabel($"{sevTag} — {b.Title}", new Color("111827"), 11);
                var desc = new Label
                {
                    Text = $"{b.Description}\n💡 {b.Suggestion}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                desc.AddThemeColorOverride("font_color", new Color("374151"));
                desc.AddThemeFontSizeOverride("font_size", 10);
                infoCol.AddChild(title);
                infoCol.AddChild(desc);
                row.AddChild(infoCol);

                int gx = b.GridX;
                int gy = b.GridY;
                var jumpBtn = new Button
                {
                    Text = "🔍 Ir",
                    CustomMinimumSize = new Vector2(58, 0),
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
                };
                jumpBtn.AddThemeFontSizeOverride("font_size", 11);
                jumpBtn.Pressed += () =>
                {
                    _audio?.PlayUiClick();
                    _selectedCell = (gx, gy);
                    SmoothMoveCameraPivotTo(GridToWorld(gx, gy));
                    UpdateInspectorPanel();
                };
                row.AddChild(jumpBtn);

                card.AddChild(row);
                _bottleneckListBox.AddChild(card);
            }
        }
        else
        {
            foreach (Node child in _milestoneListBox.GetChildren())
                child.QueueFree();

            var statuses = _bridge.Engine.Milestones.GetStatuses(_bridge.Engine);
            int unlockedCount = statuses.Count(s => s.IsUnlocked);
            _milestoneSummaryLabel.Text = $"🏆 Troféus Desbloqueados: {unlockedCount}/{statuses.Count}";
            _milestoneSummaryLabel.AddThemeColorOverride("font_color", unlockedCount > 0 ? new Color("15803d") : new Color("1f2937"));

            foreach (var st in statuses)
            {
                var card = new PanelContainer();
                card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = st.IsUnlocked ? new Color("ecfdf5") : new Color("f8fafc"),
                    BorderColor = st.IsUnlocked ? new Color("10b981") : new Color("cbd5e1"),
                    BorderWidthLeft = 3,
                    BorderWidthTop = 1,
                    BorderWidthRight = 1,
                    BorderWidthBottom = 1,
                    CornerRadiusTopRight = 5,
                    CornerRadiusBottomRight = 5,
                    ContentMarginLeft = 8,
                    ContentMarginRight = 8,
                    ContentMarginTop = 5,
                    ContentMarginBottom = 5
                });

                var col = new VBoxContainer();
                col.AddThemeConstantOverride("separation", 3);

                var topRow = new HBoxContainer();
                var titleLbl = CreateLabel($"{st.Definition.Icon} {st.Definition.Title}", new Color("111827"), 11);
                titleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var badgeLbl = CreateLabel(
                    st.IsUnlocked ? "✅ CONQUISTADO" : st.ProgressText,
                    st.IsUnlocked ? new Color("15803d") : new Color("475569"),
                    10);
                topRow.AddChild(titleLbl);
                topRow.AddChild(badgeLbl);
                col.AddChild(topRow);

                var desc = new Label
                {
                    Text = $"{st.Definition.Description}\n🎁 +R$ {st.Definition.CashReward:N0} | ✨ {st.Definition.BonusDescription}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                desc.AddThemeColorOverride("font_color", new Color("374151"));
                desc.AddThemeFontSizeOverride("font_size", 10);
                col.AddChild(desc);

                var bar = new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = 100,
                    Value = st.NormalizedProgress * 100f,
                    CustomMinimumSize = new Vector2(0, 12)
                };
                col.AddChild(bar);

                card.AddChild(col);
                _milestoneListBox.AddChild(card);
            }
        }
    }

    private void OnMilestoneUnlocked(MilestoneDefinition milestone)
    {
        _audio?.PlayNotification(celebratory: true);
        _audio?.PlayCashRegister(milestone.CashReward);
        SpawnFloatingRevenuePopup(
            _cameraPivot.Position + new Vector3(0f, 1.5f, 0f),
            $"🏆 {milestone.Title}! (+R$ {milestone.CashReward:N0})",
            new Color("facc15"),
            fontSize: 48,
            duration: 3.0f);
        LogEvent($"🏆 CONQUISTA HISTÓRICA: [b]{milestone.Icon} {milestone.Title}[/b] desbloqueada (+R$ {milestone.CashReward:N0})! Bônus permanente: {milestone.BonusDescription}.");
        RefreshHud();
    }

    private void OnTruckAutoReplaced(Truck oldTruck, Truck newTruck, string reason)
    {
        _audio?.PlayNotification(celebratory: false);
        LogEvent($"♻️ Auto-substituição: [b]{oldTruck.Name}[/b] ({oldTruck.Spec.Name}) → [b]{newTruck.Name}[/b] ({newTruck.Spec.Name}) • {reason}.");
        RefreshHud();
    }

    // =========================================================================
    // Sprint E7 — Áudio Espacial 3D Dinâmico & Modo Foto / Câmera Drone
    // =========================================================================

    private void UpdateSpatialAudioAndHorns(float dt)
    {
        if (_audio == null || _bridge.Engine.ActiveTrucks.Count == 0 || _bridge.TimeScale <= 0.01f)
        {
            _audio?.UpdateSpatialTruckAudio(0f, 0f, false, 0f);
            return;
        }

        // Em zoom afastado sem caminhão seguido, o áudio espacial é sempre nulo: evita varrer toda a frota por frame.
        if (_followedTruckIndex < 0 && _cameraDistance >= 28f)
        {
            _audio.UpdateSpatialTruckAudio(0f, 0f, false, 0f);
            return;
        }

        _spatialAudioTimer += dt;
        if (_spatialAudioTimer < 0.05f)
            return;
        _spatialAudioTimer = 0f;

        Truck? focusTruck = null;
        float bestDistSq = float.MaxValue;
        Vector3 pivotPos = _cameraPivot.GlobalPosition;

        if (_followedTruckIndex >= 0 && _followedTruckIndex < _bridge.Engine.ActiveTrucks.Count)
        {
            focusTruck = _bridge.Engine.ActiveTrucks[_followedTruckIndex];
        }
        else
        {
            var trucks = _bridge.Engine.ActiveTrucks;
            for (int i = 0; i < trucks.Count; i++)
            {
                var truck = trucks[i];
                if (_truckVisuals.TryGetValue(truck.Id, out var vis) && IsInstanceValid(vis.Root))
                {
                    float d2 = vis.Root.GlobalPosition.DistanceSquaredTo(pivotPos);
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        focusTruck = truck;
                    }
                }
            }
        }

        if (focusTruck == null || !_truckVisuals.TryGetValue(focusTruck.Id, out var handle) || !IsInstanceValid(handle.Root))
        {
            _audio.UpdateSpatialTruckAudio(0f, 0f, false, 0f);
            return;
        }

        float distToPivot = handle.Root.GlobalPosition.DistanceTo(pivotPos);
        float zoomProximity = Mathf.Clamp((28f - _cameraDistance) / 20f, 0f, 1f);
        float targetProximity = _followedTruckIndex >= 0
            ? Mathf.Max(0.75f, zoomProximity)
            : zoomProximity * Mathf.Clamp(1f - distToPivot / 26f, 0f, 1f);

        bool moving = focusTruck.CurrentEdge != null &&
            (focusTruck.Status == TruckStatus.TravelingToDelivery ||
             focusTruck.Status == TruckStatus.TravelingToPickup ||
             focusTruck.Status == TruckStatus.TravelingToPitStop);

        float speed01 = 0f;
        bool onHill = false;
        if (moving && focusTruck.CurrentEdge != null)
        {
            float effSpeed = MathF.Min(
                focusTruck.Spec.MaxTopSpeedKmh,
                focusTruck.CurrentEdge.GetEffectiveSpeedLimitKmh(_bridge.Engine.CurrentWeather, _bridge.Engine.IsWinterBlizzardActive));
            speed01 = Mathf.Clamp(effSpeed / 110f, 0.10f, 1f);
            onHill = focusTruck.CurrentEdge.IsHillOrMountain || focusTruck.CurrentEdge.IsAlpineBiome;
        }

        Vector3 camRight = _camera.GlobalTransform.Basis.X;
        Vector3 dir = (handle.Root.GlobalPosition - _camera.GlobalPosition).Normalized();
        float stereoPan = Mathf.Clamp(camRight.Dot(dir), -1f, 1f);

        _audio.UpdateSpatialTruckAudio(targetProximity, speed01, onHill, stereoPan);

        // Buzina a ar automática ao cruzar áreas urbanas quando a câmera está próxima ou em Chase Cam
        if (moving && targetProximity > 0.30f)
        {
            float ts = ProceduralModels3D.TileSize;
            int gx = Mathf.RoundToInt(handle.Root.Position.X / ts);
            int gy = Mathf.RoundToInt(handle.Root.Position.Z / ts);

            bool insideCity = false;
            var cities = _bridge.World.Cities;
            for (int i = 0; i < cities.Count; i++)
            {
                var c = cities[i];
                if (c.UrbanTiles.Contains((gx, gy)) ||
                    (Math.Abs(c.CenterX - gx) <= 1 && Math.Abs(c.CenterY - gy) <= 1))
                {
                    insideCity = true;
                    break;
                }
            }

            if (insideCity)
            {
                double now = Time.GetTicksMsec() * 0.001;
                if (!_truckHornCooldowns.TryGetValue(focusTruck.Id, out double lastHorn) || now - lastHorn >= 9.0)
                {
                    _truckHornCooldowns[focusTruck.Id] = now;
                    _audio.PlayAirHorn(stereoPan, Mathf.Clamp(targetProximity + 0.25f, 0.3f, 1f));
                }
            }
        }
    }

    private void BuildPhotoModeBar(CanvasLayer canvas, StyleBoxFlat darkPillStyle)
    {
        _photoModeBar = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = -465,
            OffsetRight = 465,
            OffsetTop = -62,
            OffsetBottom = -14
        };
        _photoModeBar.AddThemeStyleboxOverride("panel", darkPillStyle);
        canvas.AddChild(_photoModeBar);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        _photoModeBar.AddChild(row);

        row.AddChild(CreateLabel("📸 FOTO", new Color("facc15"), 12));

        _photoHourCheck = new CheckBox { Text = "Hora", ButtonPressed = false };
        _photoHourCheck.AddThemeFontSizeOverride("font_size", 11);
        _photoHourCheck.AddThemeColorOverride("font_color", new Color("f8fafc"));
        _photoHourCheck.Toggled += pressed =>
        {
            _photoLockHour = pressed;
            if (_photoHourSlider != null) _photoHourSlider.Editable = pressed;
        };
        row.AddChild(_photoHourCheck);

        _photoHourSlider = new HSlider
        {
            MinValue = 0.0,
            MaxValue = 23.5,
            Step = 0.5,
            Value = 17.5,
            CustomMinimumSize = new Vector2(88, 0),
            Editable = false
        };
        _photoHourLabel = CreateLabel("17:30", new Color("e2e8f0"), 11);
        _photoHourSlider.ValueChanged += val =>
        {
            _photoHourValue = (float)val;
            _photoLockHour = true;
            if (_photoHourCheck != null) _photoHourCheck.ButtonPressed = true;
            _photoHourSlider.Editable = true;
            int hh = (int)_photoHourValue;
            int mm = (_photoHourValue - hh) >= 0.4f ? 30 : 0;
            _photoHourLabel.Text = $"{hh:D2}:{mm:D2}";
        };
        row.AddChild(_photoHourSlider);
        row.AddChild(_photoHourLabel);

        var weatherOpt = new OptionButton();
        weatherOpt.AddThemeFontSizeOverride("font_size", 11);
        weatherOpt.AddItem("Clima: Auto");
        weatherOpt.AddItem("☀️ Ensolarado");
        weatherOpt.AddItem("🌧️ Chuva");
        weatherOpt.AddItem("⛈️ Tempestade");
        weatherOpt.AddItem("❄️ Nevasca");
        weatherOpt.ItemSelected += idx => _photoWeatherOverride = (int)idx;
        row.AddChild(weatherOpt);

        var filterOpt = new OptionButton();
        filterOpt.AddThemeFontSizeOverride("font_size", 11);
        filterOpt.AddItem("🎞️ Padrão");
        filterOpt.AddItem("📼 Retrô Anos 90");
        filterOpt.AddItem("🧸 Maquete Forte");
        filterOpt.AddItem("🌅 Pôr do Sol Quente");
        filterOpt.ItemSelected += idx => ApplyPhotoFilterVisuals((PhotoFilterMode)idx);
        row.AddChild(filterOpt);

        _photoDroneBtn = AddPillButton(row, "🚁 Drone: OFF", () => SetDroneTourActive(!_droneTourActive));
        AddPillButton(row, "⏭️", AdvanceDroneTourTarget);
        AddPillButton(row, "📷 PNG", CapturePhotoScreenshot);
        AddPillButton(row, "✕ Sair (F12)", TogglePhotoMode);

        _photoStatusLabel = CreateLabel(string.Empty, new Color("4ade80"), 11);
        row.AddChild(_photoStatusLabel);
    }

    private void TogglePhotoMode()
    {
        _photoModeActive = !_photoModeActive;
        if (_hudRegularLayer != null) _hudRegularLayer.Visible = !_photoModeActive;
        if (_photoModeBar != null)
        {
            _photoModeBar.Visible = _photoModeActive;
            if (_photoModeActive) AnimateControlPopup(_photoModeBar);
        }

        if (_photoModeActive)
        {
            float currentHour = _bridge.Engine.Calendar.Hour + (_bridge.Engine.Calendar.Minute / 60f);
            _photoHourValue = MathF.Round(currentHour * 2f) * 0.5f;
            if (_photoHourSlider != null) _photoHourSlider.Value = _photoHourValue;
            if (_photoStatusLabel != null) _photoStatusLabel.Text = string.Empty;
            ApplyZoomPerformanceLod(force: true);
        }
        else
        {
            SetDroneTourActive(false);
            _photoLockHour = false;
            _photoWeatherOverride = 0;
            ApplyPhotoFilterVisuals(PhotoFilterMode.Default);
            ApplyGraphicsPreset(_graphicsPreset, logMessage: false);
        }

        _audio?.PlayUiClick();
    }

    private void ApplyPhotoFilterVisuals(PhotoFilterMode mode)
    {
        _photoFilterMode = mode;
        if (_worldEnv == null) return;

        switch (mode)
        {
            case PhotoFilterMode.Retro90s:
                _worldEnv.AdjustmentEnabled = true;
                _worldEnv.AdjustmentBrightness = 0.96f;
                _worldEnv.AdjustmentContrast = 1.18f;
                _worldEnv.AdjustmentSaturation = 0.70f;
                break;

            case PhotoFilterMode.ToyModelTiltShift:
                _worldEnv.AdjustmentEnabled = true;
                _worldEnv.AdjustmentBrightness = 1.05f;
                _worldEnv.AdjustmentContrast = 1.20f;
                _worldEnv.AdjustmentSaturation = 1.38f;
                break;

            case PhotoFilterMode.WarmSunset:
                _worldEnv.AdjustmentEnabled = true;
                _worldEnv.AdjustmentBrightness = 1.02f;
                _worldEnv.AdjustmentContrast = 1.16f;
                _worldEnv.AdjustmentSaturation = 1.24f;
                break;

            default:
                _worldEnv.AdjustmentEnabled = false;
                _worldEnv.AdjustmentBrightness = 1.0f;
                _worldEnv.AdjustmentContrast = 1.0f;
                _worldEnv.AdjustmentSaturation = 1.0f;
                break;
        }
        UpdateCameraTransform();
    }

    private void SetDroneTourActive(bool active)
    {
        _droneTourActive = active;
        _droneTourTimer = 0f;
        if (active) _followedTruckIndex = -1;

        if (_photoDroneBtn != null)
        {
            _photoDroneBtn.Text = active ? "🚁 Drone: ON" : "🚁 Drone: OFF";
            _photoDroneBtn.AddThemeStyleboxOverride("normal", active ? _pillBtnActive : _pillBtnNormal);
        }
    }

    private List<(string Label, Vector3 WorldPos, float Distance)> BuildDroneTourStops()
    {
        var stops = new List<(string, Vector3, float)>();

        foreach (var truck in _bridge.Engine.ActiveTrucks.Take(3))
        {
            if (_truckVisuals.TryGetValue(truck.Id, out var vis) && IsInstanceValid(vis.Root))
                stops.Add((truck.Name, vis.Root.GlobalPosition, 13.5f));
        }

        foreach (var sp in _bridge.World.Seaports)
            stops.Add((sp.Name, GridToWorld(sp.GridX, sp.GridY), 16.5f));

        foreach (var lm in _bridge.World.Landmarks)
            stops.Add((lm.Name, GridToWorld(lm.GridX, lm.GridY), 16.0f));

        foreach (var city in _bridge.World.Cities)
            stops.Add((city.Name, GridToWorld(city.CenterX, city.CenterY), 21.0f));

        return stops;
    }

    private void AdvanceDroneTourTarget()
    {
        var stops = BuildDroneTourStops();
        if (stops.Count == 0) return;
        _droneStopIndex = (_droneStopIndex + 1) % stops.Count;
        _droneTourTimer = 0f;
        if (!_droneTourActive) SetDroneTourActive(true);
        if (_photoStatusLabel != null)
            _photoStatusLabel.Text = $"🚁 {stops[_droneStopIndex].Label}";
    }

    private void UpdateDroneTour(float dt)
    {
        if (!_droneTourActive) return;

        var stops = BuildDroneTourStops();
        if (stops.Count == 0) return;

        _droneTourTimer += dt;
        if (_droneTourTimer >= 7.5f)
        {
            _droneTourTimer = 0f;
            _droneStopIndex = (_droneStopIndex + 1) % stops.Count;
        }

        int idx = _droneStopIndex % stops.Count;
        var target = stops[idx];

        _cameraPivot.Position = _cameraPivot.Position.Lerp(target.WorldPos, Mathf.Clamp(dt * 1.4f, 0f, 1f));
        _cameraYaw += dt * 0.16f;
        float desiredPitch = 0.44f + MathF.Sin(_mechanicalAnimTime * 0.35f) * 0.06f;
        _cameraPitch = Mathf.Lerp(_cameraPitch, desiredPitch, Mathf.Clamp(dt * 1.5f, 0f, 1f));
        _cameraDistance = Mathf.Lerp(_cameraDistance, target.Distance, Mathf.Clamp(dt * 1.5f, 0f, 1f));
        UpdateCameraTransform();
    }

    private void CapturePhotoScreenshot()
    {
        string dir = ProjectSettings.GlobalizePath("user://screenshots");
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, $"transit_empire_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
        {
            img.SavePng(file);
            _audio?.PlayUiClick();
            if (_photoStatusLabel != null)
                _photoStatusLabel.Text = "✅ PNG Salvo!";
            LogEvent($"📷 Foto salva em {file}");
        }
    }
}
