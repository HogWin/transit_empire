using Godot;
using System;
using System.IO;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

public partial class MainView
{
    private bool _inMainMenu = true;
    private Control? _mainMenuOverlay;
    private PanelContainer? _mainMenuHomeCard;
    private PanelContainer? _newGameCreatorCard;

    private Button? _menuContinueBtn;
    private Label? _menuContinueInfoLbl;
    private string? _latestSaveUserPath;

    // Estado do Criador de Mapa ("Novo Jogo")
    private int _ngSize = 100;
    private WorldScenario _ngScenario = WorldScenario.Balanced;
    private bool _ngHasRivers = true;
    private MountainDensity _ngMountains = MountainDensity.Normal;
    private int _ngSeed = 1337;
    private GeneratedWorld? _ngPreviewWorld;

    private LineEdit? _ngSeedEdit;
    private OptionButton? _ngScenarioSelect;
    private TextureRect? _ngPreviewTexRect;
    private Label? _ngPreviewStatsLbl;

    private Button? _ngSize50Btn;
    private Button? _ngSize80Btn;
    private Button? _ngSize100Btn;
    private Button? _ngRiversYesBtn;
    private Button? _ngRiversNoBtn;
    private Button? _ngMtnFewBtn;
    private Button? _ngMtnNormBtn;
    private Button? _ngMtnManyBtn;

    private StyleBoxFlat _menuCardStyle = null!;
    private StyleBoxFlat _menuBtnNormalStyle = null!;
    private StyleBoxFlat _menuBtnPrimaryStyle = null!;
    private StyleBoxFlat _menuBtnAccentStyle = null!;

    private void BuildMainMenuOverlay(Node canvas)
    {
        _menuCardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.11f, 0.14f, 0.19f, 0.94f),
            BorderColor = new Color("38bdf8"),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = 18,
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 20,
            ContentMarginBottom = 20
        };

        _menuBtnNormalStyle = new StyleBoxFlat
        {
            BgColor = new Color("1e293b"),
            BorderColor = new Color("475569"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };

        _menuBtnPrimaryStyle = new StyleBoxFlat
        {
            BgColor = new Color("0284c7"),
            BorderColor = new Color("38bdf8"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };

        _menuBtnAccentStyle = new StyleBoxFlat
        {
            BgColor = new Color("15803d"),
            BorderColor = new Color("4ade80"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };

        _mainMenuOverlay = new Control
        {
            Name = "MainMenuOverlay",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _mainMenuOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        var backdropTint = new ColorRect
        {
            Color = new Color(0.05f, 0.08f, 0.13f, 0.42f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        backdropTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _mainMenuOverlay.AddChild(backdropTint);

        BuildMainMenuHomeCard(_mainMenuOverlay);
        BuildNewGameCreatorCard(_mainMenuOverlay);

        canvas.AddChild(_mainMenuOverlay);
    }

    private void BuildMainMenuHomeCard(Control parent)
    {
        _mainMenuHomeCard = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -215,
            OffsetRight = 215,
            OffsetTop = -230,
            OffsetBottom = 230
        };
        _mainMenuHomeCard.AddThemeStyleboxOverride("panel", _menuCardStyle);
        parent.AddChild(_mainMenuHomeCard);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        _mainMenuHomeCard.AddChild(col);

        var badgeLbl = CreateLabel("🚂 DIORAMA VOXEL TYCOON", new Color("38bdf8"), 11);
        badgeLbl.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(badgeLbl);

        var titleLbl = CreateLabel("TRANSIT EMPIRE", new Color("f8fafc"), 26);
        titleLbl.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(titleLbl);

        var subLbl = CreateLabel("Logística Multimodal • Rodovias, Portos & Ferrovias", new Color("94a3b8"), 12);
        subLbl.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(subLbl);

        col.AddChild(new HSeparator());

        _menuContinueBtn = CreateMenuButton("▶ Continuar Partida", _menuBtnAccentStyle, OnMenuContinuePressed);
        col.AddChild(_menuContinueBtn);

        _menuContinueInfoLbl = CreateLabel("Verificando saves disponíveis...", new Color("cbd5e1"), 11);
        _menuContinueInfoLbl.HorizontalAlignment = HorizontalAlignment.Center;
        _menuContinueInfoLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_menuContinueInfoLbl);

        var newGameBtn = CreateMenuButton("🗺️ Novo Jogo (Configurar Mapa)", _menuBtnPrimaryStyle, ShowNewGameCreatorPanel);
        col.AddChild(newGameBtn);

        var settingsBtn = CreateMenuButton("⚙️ Configurações", _menuBtnNormalStyle, ToggleGraphicsModal);
        col.AddChild(settingsBtn);

        var quitBtn = CreateMenuButton("🚪 Sair do Jogo", _menuBtnNormalStyle, () => GetTree().Quit());
        col.AddChild(quitBtn);

        var footerLbl = CreateLabel("Dica: durante o jogo, use [Q/B/R/X] para alternar ferramentas e [Espaço] para pausar.", new Color("64748b"), 10);
        footerLbl.HorizontalAlignment = HorizontalAlignment.Center;
        footerLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(footerLbl);
    }

    private void BuildNewGameCreatorCard(Control parent)
    {
        _newGameCreatorCard = new PanelContainer
        {
            Visible = false,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -420,
            OffsetRight = 420,
            OffsetTop = -280,
            OffsetBottom = 280
        };
        _newGameCreatorCard.AddThemeStyleboxOverride("panel", _menuCardStyle);
        parent.AddChild(_newGameCreatorCard);

        var rootCol = new VBoxContainer();
        rootCol.AddThemeConstantOverride("separation", 12);
        _newGameCreatorCard.AddChild(rootCol);

        var hdrRow = new HBoxContainer();
        var hdrTitle = CreateLabel("🗺️ CRIAR NOVO MAPA & PRÉVIA EM TEMPO REAL", new Color("f8fafc"), 16);
        hdrTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hdrRow.AddChild(hdrTitle);

        var backBtn = CreateMenuButton("← Voltar", _menuBtnNormalStyle, () =>
        {
            if (_newGameCreatorCard != null) _newGameCreatorCard.Visible = false;
            if (_mainMenuHomeCard != null) _mainMenuHomeCard.Visible = true;
            _audio?.PlayUiClick();
        });
        hdrRow.AddChild(backBtn);
        rootCol.AddChild(hdrRow);

        var splitRow = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        splitRow.AddThemeConstantOverride("separation", 20);
        rootCol.AddChild(splitRow);

        // Coluna esquerda: Parâmetros do mundo
        var formCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        formCol.AddThemeConstantOverride("separation", 10);
        splitRow.AddChild(formCol);

        formCol.AddChild(CreateLabel("1. TAMANHO DO MAPA", new Color("38bdf8"), 11));
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 6);
        _ngSize50Btn = CreateToggleOptionButton(sizeRow, "50×50 Compacto", () => { _ngSize = 50; RefreshNewGameCreatorAndPreview(); });
        _ngSize80Btn = CreateToggleOptionButton(sizeRow, "80×80 Médio", () => { _ngSize = 80; RefreshNewGameCreatorAndPreview(); });
        _ngSize100Btn = CreateToggleOptionButton(sizeRow, "100×100 Grande", () => { _ngSize = 100; RefreshNewGameCreatorAndPreview(); });
        formCol.AddChild(sizeRow);

        formCol.AddChild(CreateLabel("2. RIOS & HIDROGRAFIA INTERIOR", new Color("38bdf8"), 11));
        var riverRow = new HBoxContainer();
        riverRow.AddThemeConstantOverride("separation", 6);
        _ngRiversYesBtn = CreateToggleOptionButton(riverRow, "🌊 Com Rios", () => { _ngHasRivers = true; RefreshNewGameCreatorAndPreview(); });
        _ngRiversNoBtn = CreateToggleOptionButton(riverRow, "🏜️ Sem Rios", () => { _ngHasRivers = false; RefreshNewGameCreatorAndPreview(); });
        formCol.AddChild(riverRow);

        formCol.AddChild(CreateLabel("3. RELEVO & MONTANHAS", new Color("38bdf8"), 11));
        var mtnRow = new HBoxContainer();
        mtnRow.AddThemeConstantOverride("separation", 6);
        _ngMtnFewBtn = CreateToggleOptionButton(mtnRow, "🌾 Poucas", () => { _ngMountains = MountainDensity.Few; RefreshNewGameCreatorAndPreview(); });
        _ngMtnNormBtn = CreateToggleOptionButton(mtnRow, "⛰️ Normais", () => { _ngMountains = MountainDensity.Normal; RefreshNewGameCreatorAndPreview(); });
        _ngMtnManyBtn = CreateToggleOptionButton(mtnRow, "🏔️ Muitas", () => { _ngMountains = MountainDensity.Many; RefreshNewGameCreatorAndPreview(); });
        formCol.AddChild(mtnRow);

        formCol.AddChild(CreateLabel("4. CENÁRIO GEOPOLÍTICO", new Color("38bdf8"), 11));
        _ngScenarioSelect = new OptionButton();
        _ngScenarioSelect.AddThemeFontSizeOverride("font_size", 12);
        _ngScenarioSelect.AddItem("⚖️ Continentes Equilibrados", (int)WorldScenario.Balanced);
        _ngScenarioSelect.AddItem("🏝️ Arquipélago Costeiro", (int)WorldScenario.CoastalArchipelago);
        _ngScenarioSelect.AddItem("🏔️ Cordilheira Alpina", (int)WorldScenario.AlpineRange);
        _ngScenarioSelect.ItemSelected += idx =>
        {
            _ngScenario = (WorldScenario)_ngScenarioSelect.GetItemId((int)idx);
            RefreshNewGameCreatorAndPreview();
        };
        formCol.AddChild(_ngScenarioSelect);

        formCol.AddChild(CreateLabel("5. SEED & CRIAÇÃO ALEATÓRIA", new Color("38bdf8"), 11));
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 6);
        _ngSeedEdit = new LineEdit
        {
            PlaceholderText = "Seed numérica",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _ngSeedEdit.AddThemeFontSizeOverride("font_size", 12);
        _ngSeedEdit.TextSubmitted += txt =>
        {
            if (int.TryParse(txt.Trim(), out int parsed) && parsed > 0)
            {
                _ngSeed = parsed;
                RefreshNewGameCreatorAndPreview();
            }
            else
            {
                _audio?.PlayError();
            }
        };
        seedRow.AddChild(_ngSeedEdit);

        var applySeedBtn = CreateMenuButton("Aplicar Seed", _menuBtnNormalStyle, () =>
        {
            if (_ngSeedEdit != null && int.TryParse(_ngSeedEdit.Text.Trim(), out int parsed) && parsed > 0)
            {
                _ngSeed = parsed;
                RefreshNewGameCreatorAndPreview();
            }
            else
            {
                _audio?.PlayError();
            }
        });
        seedRow.AddChild(applySeedBtn);

        var randomBtn = CreateMenuButton("🎲 Mapa Aleatório", _menuBtnPrimaryStyle, RandomizeNewGameMap);
        seedRow.AddChild(randomBtn);
        formCol.AddChild(seedRow);

        var startBtn = CreateMenuButton("🚀 Iniciar Partida neste Mapa", _menuBtnAccentStyle, StartConfiguredNewGame);
        startBtn.CustomMinimumSize = new Vector2(0, 44);
        formCol.AddChild(startBtn);

        // Coluna direita: Prévia do mapa gerado
        var previewCol = new VBoxContainer();
        previewCol.CustomMinimumSize = new Vector2(310, 0);
        previewCol.AddThemeConstantOverride("separation", 8);
        splitRow.AddChild(previewCol);

        previewCol.AddChild(CreateLabel("PRÉVIA DO TERRENO A SER INICIADO", new Color("38bdf8"), 11));

        var previewFrame = new PanelContainer();
        previewFrame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("0f172a"),
            BorderColor = new Color("475569"),
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        });

        _ngPreviewTexRect = new TextureRect
        {
            CustomMinimumSize = new Vector2(288, 288),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        };
        previewFrame.AddChild(_ngPreviewTexRect);
        previewCol.AddChild(previewFrame);

        var legendLbl = CreateLabel(
            "Legenda: 🟦 Oceano/Rios • 🟩 Planície • 🟨 Deserto/Cidades • 🟫 Colina/Montanha • 🟧 Indústrias • 🩵 Portos",
            new Color("94a3b8"),
            10);
        legendLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        previewCol.AddChild(legendLbl);

        _ngPreviewStatsLbl = CreateLabel("", new Color("e2e8f0"), 11);
        _ngPreviewStatsLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        previewCol.AddChild(_ngPreviewStatsLbl);
    }

    private Button CreateMenuButton(string text, StyleBoxFlat style, Action onClick)
    {
        var hoverStyle = (StyleBoxFlat)style.Duplicate();
        hoverStyle.BgColor = style.BgColor.Lightened(0.14f);

        var btn = new Button { Text = text };
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.AddThemeStyleboxOverride("normal", style);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", hoverStyle);
        btn.AddThemeColorOverride("font_color", new Color("f8fafc"));
        btn.AddThemeColorOverride("font_disabled_color", new Color("64748b"));
        btn.Pressed += () =>
        {
            _audio?.PlayUiClick();
            onClick();
        };
        return btn;
    }

    private Button CreateToggleOptionButton(Container parent, string text, Action onClick)
    {
        var btn = CreateMenuButton(text, _menuBtnNormalStyle, onClick);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.AddThemeFontSizeOverride("font_size", 11);
        parent.AddChild(btn);
        return btn;
    }

    private void EnterMainMenu()
    {
        _inMainMenu = true;
        _bridge.TimeScale = 0.0f;
        _isOrbiting = false;
        _isPanning = false;
        CancelRoadDrag();

        if (_hudRegularLayer != null) _hudRegularLayer.Visible = false;
        if (_graphicsModal != null) _graphicsModal.Visible = false;
        if (_savesModal != null) _savesModal.Visible = false;
        if (_photoModeActive) TogglePhotoMode();

        if (_mainMenuOverlay != null)
        {
            _mainMenuOverlay.Visible = true;
            if (_mainMenuHomeCard != null) _mainMenuHomeCard.Visible = true;
            if (_newGameCreatorCard != null) _newGameCreatorCard.Visible = false;
        }

        RefreshContinueButtonState();
        _audio?.UpdateEnvironmentState(_bridge.Engine.CurrentWeather, 0.0f);
    }

    private void ExitMainMenu()
    {
        _inMainMenu = false;
        if (_mainMenuOverlay != null) _mainMenuOverlay.Visible = false;
        if (_graphicsModal != null) _graphicsModal.Visible = false;
        if (_hudRegularLayer != null) _hudRegularLayer.Visible = true;
        RefreshHud();
    }

    private void RefreshContinueButtonState()
    {
        if (_menuContinueBtn == null || _menuContinueInfoLbl == null)
            return;

        string[] candidateUserPaths =
        {
            "user://autosave.json",
            "user://savegame.json",
            "user://save_slot_1.json",
            "user://save_slot_2.json",
            "user://save_slot_3.json"
        };

        string? bestPath = null;
        DateTime bestTime = DateTime.MinValue;
        SaveGameSerializer.SaveSlotSummary? bestSummary = null;

        foreach (string userPath in candidateUserPaths)
        {
            string absPath = ProjectSettings.GlobalizePath(userPath);
            if (!File.Exists(absPath)) continue;

            var summary = SaveGameSerializer.ReadSaveSummary(absPath, Path.GetFileNameWithoutExtension(userPath));
            if (!summary.Exists) continue;

            DateTime modTime = File.GetLastWriteTimeUtc(absPath);
            if (modTime > bestTime)
            {
                bestTime = modTime;
                bestPath = userPath;
                bestSummary = summary;
            }
        }

        _latestSaveUserPath = bestPath;
        if (bestSummary != null && bestPath != null)
        {
            _menuContinueBtn.Disabled = false;
            _menuContinueInfoLbl.Text = $"{bestSummary.SlotLabel} • {bestSummary.CalendarText} • R$ {bestSummary.Balance:N0} ({bestSummary.TruckCount} veíc.)";
        }
        else
        {
            _menuContinueBtn.Disabled = true;
            _menuContinueInfoLbl.Text = "Nenhum jogo salvo encontrado.";
        }
    }

    private void OnMenuContinuePressed()
    {
        if (string.IsNullOrEmpty(_latestSaveUserPath))
        {
            _audio?.PlayError();
            return;
        }

        LoadGameFromPath(_latestSaveUserPath, Path.GetFileName(_latestSaveUserPath));
        ExitMainMenu();
    }

    private void ShowNewGameCreatorPanel()
    {
        _ngSize = _selectedMapSize;
        _ngScenario = _selectedScenario;
        _ngHasRivers = _selectedHasRivers;
        _ngMountains = _selectedMountains;
        _ngSeed = Random.Shared.Next(1, 1_000_000);

        if (_ngScenarioSelect != null)
            _ngScenarioSelect.Select((int)_ngScenario);

        if (_mainMenuHomeCard != null) _mainMenuHomeCard.Visible = false;
        if (_newGameCreatorCard != null)
        {
            _newGameCreatorCard.Visible = true;
            AnimateControlPopup(_newGameCreatorCard);
        }

        RefreshNewGameCreatorAndPreview();
    }

    private void RandomizeNewGameMap()
    {
        _ngSeed = Random.Shared.Next(1, 1_000_000);
        RefreshNewGameCreatorAndPreview();
    }

    private void RefreshNewGameCreatorAndPreview()
    {
        if (_ngSeedEdit != null)
            _ngSeedEdit.Text = _ngSeed.ToString();

        void StyleToggle(Button? btn, bool active)
        {
            if (btn == null) return;
            var baseStyle = active ? _menuBtnPrimaryStyle : _menuBtnNormalStyle;
            var hover = (StyleBoxFlat)baseStyle.Duplicate();
            hover.BgColor = baseStyle.BgColor.Lightened(0.14f);
            btn.AddThemeStyleboxOverride("normal", baseStyle);
            btn.AddThemeStyleboxOverride("hover", hover);
            btn.AddThemeStyleboxOverride("pressed", hover);
        }

        StyleToggle(_ngSize50Btn, _ngSize == 50);
        StyleToggle(_ngSize80Btn, _ngSize == 80);
        StyleToggle(_ngSize100Btn, _ngSize == 100);

        StyleToggle(_ngRiversYesBtn, _ngHasRivers);
        StyleToggle(_ngRiversNoBtn, !_ngHasRivers);

        StyleToggle(_ngMtnFewBtn, _ngMountains == MountainDensity.Few);
        StyleToggle(_ngMtnNormBtn, _ngMountains == MountainDensity.Normal);
        StyleToggle(_ngMtnManyBtn, _ngMountains == MountainDensity.Many);

        var config = SimulationBridge.BuildWorldConfig(
            width: _ngSize,
            height: _ngSize,
            seed: _ngSeed,
            scenario: _ngScenario,
            hasRivers: _ngHasRivers,
            mountains: _ngMountains);
        _ngPreviewWorld = new WorldGenerator(config).Generate();
        RenderPreviewWorldTexture(_ngPreviewWorld);
    }

    private void RenderPreviewWorldTexture(GeneratedWorld world)
    {
        if (_ngPreviewTexRect == null || _ngPreviewStatsLbl == null)
            return;

        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        int mountainTiles = 0;
        int riverTiles = 0;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var cell = world.Grid[x, y];
                Color col;

                if (world.OceanMask[x, y])
                {
                    col = new Color("1d4ed8");
                }
                else if (cell.Terrain == TerrainType.Water)
                {
                    col = new Color("38bdf8");
                    riverTiles++;
                }
                else if (cell.Terrain == TerrainType.Mountain)
                {
                    col = cell.Biome == BiomeType.Alpine ? new Color("cbd5e1") : new Color("78716c");
                    mountainTiles++;
                }
                else if (cell.Terrain == TerrainType.Hills)
                {
                    col = cell.Biome switch
                    {
                        BiomeType.Desert => new Color("c29b61"),
                        BiomeType.Alpine => new Color("5a7d60"),
                        _ => new Color("4d7c3f")
                    };
                }
                else
                {
                    col = cell.Biome switch
                    {
                        BiomeType.Desert => new Color("e2c785"),
                        BiomeType.Alpine => new Color("7da174"),
                        _ => new Color("74ab57")
                    };
                }

                if (cell.HasRoad)
                    col = col.Lerp(new Color("334155"), 0.72f);

                img.SetPixel(x, y, col);
            }
        }

        void PaintMarker(int cx, int cy, int radius, Color fill, Color? border = null)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int px = cx + dx;
                    int py = cy + dy;
                    if (px < 0 || px >= w || py < 0 || py >= h) continue;
                    bool isEdge = Math.Abs(dx) == radius || Math.Abs(dy) == radius;
                    img.SetPixel(px, py, (isEdge && border.HasValue) ? border.Value : fill);
                }
            }
        }

        foreach (var ind in world.Industries)
            PaintMarker(ind.GridX, ind.GridY, 1, new Color("f97316"));

        foreach (var port in world.Seaports)
            PaintMarker(port.GridX, port.GridY, 1, new Color("06b6d4"), new Color("083344"));

        foreach (var city in world.Cities)
            PaintMarker(city.CenterX, city.CenterY, 2, new Color("facc15"), new Color("1e293b"));

        _ngPreviewTexRect.Texture = ImageTexture.CreateFromImage(img);

        float totalTiles = Math.Max(1f, w * h);
        float mtnPct = mountainTiles * 100f / totalTiles;
        string riverDesc = _ngHasRivers ? $"{riverTiles} tiles de rio" : "Sem rios";
        _ngPreviewStatsLbl.Text =
            $"🏙️ {world.Cities.Count} cidades  •  🏭 {world.Industries.Count} indústrias  •  ⚓ {world.Seaports.Count} portos\n" +
            $"🏔️ {mtnPct:F1}% montanhas  •  💧 {riverDesc}  •  🎲 Seed #{_ngSeed}";
    }

    private void StartConfiguredNewGame()
    {
        _selectedMapSize = _ngSize;
        _selectedScenario = _ngScenario;
        _selectedHasRivers = _ngHasRivers;
        _selectedMountains = _ngMountains;
        PersistClientSettings();

        ClearWorldVisuals();
        InitializeSimulation(seed: _ngSeed, prebuiltWorld: _ngPreviewWorld);
        _ngPreviewWorld = null;

        BuildInitialWorld3D();
        FocusOnFirstCity();
        SyncFleet3DTransforms(0.01f);
        _selectedTruckIndex = 0;
        RebuildSchedulePools();

        ExitMainMenu();

        string mtnTxt = _selectedMountains switch
        {
            MountainDensity.Few => "Poucas",
            MountainDensity.Many => "Muitas",
            _ => "Normais"
        };
        LogEvent($"🚀 Nova partida {_selectedMapSize}x{_selectedMapSize} iniciada! Rios: {(_selectedHasRivers ? "Sim" : "Não")} | Montanhas: {mtnTxt} | Seed #{_bridge.Seed}");
    }
}
