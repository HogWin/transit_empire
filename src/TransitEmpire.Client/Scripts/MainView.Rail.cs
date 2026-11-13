using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Client.Scripts;

public partial class MainView
{
    private readonly Dictionary<(int Cx, int Cy), Node3D> _railChunkNodes = new();
    private readonly Dictionary<(int X, int Y), (Node3D Root, MeshInstance3D Lamp)> _railSignalVisuals = new();
    private readonly Dictionary<(int X, int Y), RailSignalAspect> _lastSignalAspects = new();
    private readonly Dictionary<int, Node3D> _intermodalYardNodes = new();
    private readonly Dictionary<int, TrainVisualHandle> _trainVisuals = new();

    private Control _tabBodyRail = null!;
    private OptionButton _railLocoSelect = null!;
    private SpinBox _railWagonSpin = null!;
    private OptionButton _railSpawnYardSelect = null!;
    private OptionButton _railTrainSelect = null!;
    private OptionButton _railOriginSelect = null!;
    private OptionButton _railDestSelect = null!;
    private OptionButton _railCargoSelect = null!;
    private Label _railSummaryLabel = null!;
    private Label _railStagedStopsLabel = null!;
    private RichTextLabel _railStatusText = null!;

    private readonly List<int> _railYardPoolIds = new();
    private readonly List<IntermodalStop> _pendingRailStops = new();
    private readonly List<ResourceType> _railCargoPool = new()
    {
        ResourceType.None,
        ResourceType.IronOre,
        ResourceType.Coal,
        ResourceType.Steel,
        ResourceType.Grain,
        ResourceType.Food,
        ResourceType.Timber,
        ResourceType.Lumber,
        ResourceType.Goods,
        ResourceType.Fuel,
        ResourceType.Machinery
    };

    private void ClearRailVisuals()
    {
        foreach (var node in _railChunkNodes.Values)
            if (IsInstanceValid(node)) node.QueueFree();
        _railChunkNodes.Clear();

        foreach (var (root, _) in _railSignalVisuals.Values)
            if (IsInstanceValid(root)) root.QueueFree();
        _railSignalVisuals.Clear();
        _lastSignalAspects.Clear();
        _pendingRailStops.Clear();

        foreach (var node in _intermodalYardNodes.Values)
            if (IsInstanceValid(node)) node.QueueFree();
        _intermodalYardNodes.Clear();

        foreach (var handle in _trainVisuals.Values)
            if (IsInstanceValid(handle.Root)) handle.Root.QueueFree();
        _trainVisuals.Clear();
    }

    private void RebuildAllRailAndStructures3D()
    {
        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        var dirtyChunks = new HashSet<(int Cx, int Cy)>();

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (grid[x, y].HasRail)
                    dirtyChunks.Add(GetRoadChunkCoord(x, y));
                if (grid[x, y].HasRailSignal)
                    SpawnRailSignalVisual(x, y);
            }
        }

        foreach (var (cx, cy) in dirtyChunks)
            RebuildRailChunk(cx, cy);

        foreach (var yard in _bridge.Engine.IntermodalTerminals)
            SpawnIntermodalYardVisual(yard);

        SyncRailAndTrains3D(0.01f);
    }

    private void RebuildRailAndNeighbors(int gx, int gy)
    {
        var dirty = new HashSet<(int Cx, int Cy)>
        {
            GetRoadChunkCoord(gx, gy),
            GetRoadChunkCoord(Math.Max(0, gx - 1), gy),
            GetRoadChunkCoord(gx + 1, gy),
            GetRoadChunkCoord(gx, Math.Max(0, gy - 1)),
            GetRoadChunkCoord(gx, gy + 1)
        };
        foreach (var (cx, cy) in dirty)
            RebuildRailChunk(cx, cy);

        _minimapStaticDirty = true;
    }

    private void RebuildRailChunk(int cx, int cy)
    {
        var key = (cx, cy);
        if (_railChunkNodes.TryGetValue(key, out var old) && IsInstanceValid(old))
            old.QueueFree();
        _railChunkNodes.Remove(key);

        var grid = _bridge.World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        int startX = cx * RoadChunkSize;
        int startY = cy * RoadChunkSize;
        if (startX < 0 || startY < 0 || startX >= w || startY >= h)
            return;

        int endX = Math.Min(w, startX + RoadChunkSize);
        int endY = Math.Min(h, startY + RoadChunkSize);

        var inputs = new List<ProceduralModels3D.RailTileRenderInput>();
        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                if (!grid[x, y].HasRail)
                    continue;

                bool cN = y > 0 && grid[x, y - 1].HasRail;
                bool cS = y < h - 1 && grid[x, y + 1].HasRail;
                bool cE = x < w - 1 && grid[x + 1, y].HasRail;
                bool cW = x > 0 && grid[x - 1, y].HasRail;

                inputs.Add(new ProceduralModels3D.RailTileRenderInput(
                    x, y, GridToWorld(x, y), cN, cS, cE, cW, grid[x, y].RailStructure));
            }
        }

        if (inputs.Count == 0)
            return;

        var chunkNode = ProceduralModels3D.CreateRailChunkNode(inputs);
        chunkNode.Name = $"RailChunk_{cx}_{cy}";
        _roadsRoot.AddChild(chunkNode);
        _railChunkNodes[key] = chunkNode;
    }

    private void SpawnRailSignalVisual(int x, int y)
    {
        if (_railSignalVisuals.ContainsKey((x, y)))
            return;

        var aspect = _bridge.Engine.GetRailSignalAspectAt(x, y);
        var (root, lamp) = ProceduralModels3D.CreateRailSignalNode(aspect);
        root.Position = GridToWorld(x, y);
        _structuresRoot.AddChild(root);
        _railSignalVisuals[(x, y)] = (root, lamp);
    }

    private void SpawnIntermodalYardVisual(IntermodalTerminal yard)
    {
        if (_intermodalYardNodes.TryGetValue(yard.Id, out var existing) && IsInstanceValid(existing))
            existing.QueueFree();

        var node = ProceduralModels3D.CreateIntermodalYardNode(yard);
        node.Position = GridToWorld(yard.GridX, yard.GridY);
        _structuresRoot.AddChild(node);
        _intermodalYardNodes[yard.Id] = node;
    }

    private void UpdateRailDragPreview()
    {
        if (_roadDragStart.X < 0 || _hoveredCell.X < 0)
        {
            _roadDragPlan = null;
            RefreshRoadPreviewMesh();
            return;
        }

        _roadDragPlan = _bridge.Engine.PlanRailPath(_roadDragStart.X, _roadDragStart.Y, _hoveredCell.X, _hoveredCell.Y);
        RefreshRoadPreviewMesh();

        if (_roadDragPlan.IsValid)
        {
            string affordTag = _roadDragPlan.CanAfford ? "" : " ⚠️ SALDO INSUFICIENTE";
            _topAlertLabel.Text =
                $"🚂 Ferrovia: {_roadDragPlan.TilesToBuild.Count} trilhos | 🌉 {_roadDragPlan.BridgesCount} Viadutos | 🚇 {_roadDragPlan.TunnelsCount} Túneis | R$ {_roadDragPlan.TotalCost:N0}{affordTag}";
        }
    }

    private void CommitRailDrag()
    {
        _isDraggingRoad = false;
        var plan = _roadDragPlan;
        var start = _roadDragStart;
        _roadDragPlan = null;
        RefreshRoadPreviewMesh();

        if (plan != null && plan.TilesToBuild.Count > 1)
        {
            if (_bridge.Engine.BuildPlannedRail(plan, out int built, out int viaducts, out int tunnels))
            {
                foreach (var (tx, ty) in plan.TilesToBuild)
                {
                    RebuildRailAndNeighbors(tx, ty);
                    SpawnConstructionBurst(GridToWorld(tx, ty));
                }

                _audio?.PlayRoadBuild(viaducts > 0 ? RoadStructureType.Bridge : (tunnels > 0 ? RoadStructureType.Tunnel : RoadStructureType.Ground));
                LogEvent($"🚂 Corredor férreo construído: {built} trilhos (🌉 {viaducts} viadutos, 🚇 {tunnels} túneis) por R$ {plan.TotalCost:N0}!");
                _minimapStaticDirty = true;
                RefreshHud();
            }
            else
            {
                _audio?.PlayError();
                LogEvent($"⚠️ Não foi possível construir a linha férrea (Custo: R$ {plan.TotalCost:N0}).");
            }
            return;
        }

        if (start.X >= 0)
            HandleGridCellClick(start.X, start.Y, _corridorBatchMode);
    }

    private bool HandleRailToolClick(int gx, int gy)
    {
        switch (_activeTool)
        {
            case ToolMode.BuildRail:
            {
                if (_bridge.TryBuildRailTile(gx, gy, out var st))
                {
                    RebuildRailAndNeighbors(gx, gy);
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayRoadBuild(st == RailStructureType.Viaduct ? RoadStructureType.Bridge : (st == RailStructureType.Tunnel ? RoadStructureType.Tunnel : RoadStructureType.Ground));
                    LogEvent($"🚂 {st.GetDisplayName()} construído em ({gx},{gy}) (-R$ {st.GetRailConstructionCost():N0}).");
                }
                else
                {
                    _audio?.PlayError();
                    LogEvent("⚠️ Selecione um tile livre de construções/oceano e com saldo para construir trilhos.");
                }
                return true;
            }

            case ToolMode.BuildRailSignal:
            {
                if (_bridge.TryPlaceRailSignal(gx, gy, out string sigMsg))
                {
                    SpawnRailSignalVisual(gx, gy);
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    _audio?.PlayStructureBuilt();
                    _minimapStaticDirty = true;
                }
                else
                {
                    _audio?.PlayError();
                }
                LogEvent(sigMsg);
                return true;
            }

            case ToolMode.BuildIntermodalYard:
            {
                var created = _bridge.TryBuildIntermodalTerminal(gx, gy, out string yardMsg);
                if (created != null)
                {
                    SpawnIntermodalYardVisual(created);
                    SpawnConstructionBurst(GridToWorld(gx, gy));
                    RebuildSchedulePools();
                    RefreshRailTabSelectors();
                    _audio?.PlayStructureBuilt();
                    _minimapStaticDirty = true;
                }
                else
                {
                    _audio?.PlayError();
                }
                LogEvent(yardMsg);
                return true;
            }

            default:
                return false;
        }
    }

    private void HandleDemolishRailVisuals(Core.Simulation.DemolishResult demResult, int gx, int gy)
    {
        if (demResult.Kind == Core.Simulation.DemolishTargetKind.IntermodalYard &&
            _intermodalYardNodes.TryGetValue(demResult.RemovedEntityId, out var yNode))
        {
            if (IsInstanceValid(yNode)) yNode.QueueFree();
            _intermodalYardNodes.Remove(demResult.RemovedEntityId);
            RebuildSchedulePools();
            RefreshRailTabSelectors();
        }
        else if (demResult.Kind == Core.Simulation.DemolishTargetKind.RailSignal &&
                 _railSignalVisuals.TryGetValue((gx, gy), out var sig))
        {
            if (IsInstanceValid(sig.Root)) sig.Root.QueueFree();
            _railSignalVisuals.Remove((gx, gy));
            _lastSignalAspects.Remove((gx, gy));
        }
        else if (demResult.Kind == Core.Simulation.DemolishTargetKind.Rail)
        {
            if (_railSignalVisuals.TryGetValue((gx, gy), out var sigRail))
            {
                if (IsInstanceValid(sigRail.Root)) sigRail.Root.QueueFree();
                _railSignalVisuals.Remove((gx, gy));
                _lastSignalAspects.Remove((gx, gy));
            }
            RebuildRailAndNeighbors(gx, gy);
        }
    }

    private void SyncRailAndTrains3D(float dt)
    {
        foreach (var (key, (_, lamp)) in _railSignalVisuals)
        {
            if (!IsInstanceValid(lamp)) continue;
            var aspect = _bridge.Engine.GetRailSignalAspectAt(key.X, key.Y);
            if (!_lastSignalAspects.TryGetValue(key, out var prevAspect) || prevAspect != aspect)
            {
                ProceduralModels3D.UpdateRailSignalLamp(lamp, aspect);
                _lastSignalAspects[key] = aspect;
            }
        }

        for (int i = 0; i < _bridge.Engine.ActiveTrains.Count; i++)
        {
            var train = _bridge.Engine.ActiveTrains[i];
            if (!_trainVisuals.TryGetValue(train.Id, out var handle) || !IsInstanceValid(handle.Root))
            {
                handle = ProceduralModels3D.CreateTrainNode(train);
                _trucksRoot.AddChild(handle.Root);
                _trainVisuals[train.Id] = handle;
            }

            Vector3 targetPos;
            Vector3 forwardDir = Vector3.Forward;

            if (train.CurrentEdge != null)
            {
                var p0 = GridToWorld(train.CurrentEdge.FromNode.GridX, train.CurrentEdge.FromNode.GridY);
                var p1 = GridToWorld(train.CurrentEdge.ToNode.GridX, train.CurrentEdge.ToNode.GridY);
                float t = Math.Clamp(train.DistanceAlongCurrentEdgeKm / MathF.Max(0.01f, train.CurrentEdge.DistanceKm), 0f, 1f);
                float yElev = train.CurrentEdge.Structure == RailStructureType.Viaduct ? 0.56f : p0.Y + 0.14f;
                targetPos = p0.Lerp(p1, t);
                targetPos.Y = yElev;

                Vector3 delta = p1 - p0;
                delta.Y = 0;
                if (delta.LengthSquared() > 0.001f)
                    forwardDir = delta.Normalized();
            }
            else if (train.LastNode != null)
            {
                targetPos = GridToWorld(train.LastNode.GridX, train.LastNode.GridY) + new Vector3(0f, 0.14f, 0f);
                if (train.LastNode.OutgoingEdges.Count > 0)
                {
                    var sample = train.LastNode.OutgoingEdges[0].ToNode;
                    var d = GridToWorld(sample.GridX, sample.GridY) - GridToWorld(train.LastNode.GridX, train.LastNode.GridY);
                    d.Y = 0;
                    if (d.LengthSquared() > 0.001f) forwardDir = d.Normalized();
                }
            }
            else
            {
                continue;
            }

            bool inFrustum = _camera == null || _camera.IsPositionInFrustum(targetPos);
            if (!inFrustum && handle.Root.Position != Vector3.Zero)
            {
                handle.Root.Position = targetPos;
                continue;
            }

            if (train.Status == TrainStatus.Idle && handle.Root.Position.DistanceSquaredTo(targetPos) < 0.0004f)
                continue;

            handle.Root.Position = handle.Root.Position == Vector3.Zero
                ? targetPos
                : handle.Root.Position.Lerp(targetPos, Math.Clamp(dt * 14f, 0f, 1f));

            float yaw = MathF.Atan2(-forwardDir.X, -forwardDir.Z);
            handle.Root.Rotation = new Vector3(0f, yaw, 0f);

            bool loadedNow = train.LoadedCargoAmount > 0;
            bool wasLoaded = handle.DisplayedCargoAmount > 0;
            if (loadedNow != wasLoaded || handle.DisplayedCategory != train.WagonCategory)
            {
                var mat = ProceduralModels3D.GetWagonVisualMaterial(train.WagonCategory, loadedNow);
                foreach (var cargoMesh in handle.CargoMeshes)
                {
                    if (IsInstanceValid(cargoMesh))
                    {
                        cargoMesh.MaterialOverride = mat;
                        cargoMesh.Scale = loadedNow ? Vector3.One : new Vector3(1f, 0.28f, 1f);
                    }
                }
                handle.DisplayedCategory = train.WagonCategory;
                handle.DisplayedCargoAmount = train.LoadedCargoAmount;
            }
        }
    }

    private void BuildRailTabBody(Container bodyMargin)
    {
        var railBox = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        railBox.AddThemeConstantOverride("separation", 6);
        _tabBodyRail = railBox;

        _railSummaryLabel = CreateLabel("Malha Férrea: 0 trilhos • 0 sinaleiros • 0 pátios • 0 trens", new Color("1e3a8a"), 12);
        railBox.AddChild(_railSummaryLabel);

        railBox.AddChild(CreateLabel("COMPRAR LOCOMOTIVA & COMPOSIÇÃO", new Color("4b5563"), 11));
        var buyRow = new HBoxContainer();
        buyRow.AddThemeConstantOverride("separation", 4);

        _railLocoSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railLocoSelect.AddThemeFontSizeOverride("font_size", 11);
        foreach (var loco in RailCatalog.AvailableLocomotives)
        {
            _railLocoSelect.AddItem($"{loco.Name} (Máx {loco.MaxWagons}v • R$ {loco.PurchasePrice:N0} | Nv.{loco.RequiredLevel})");
        }
        buyRow.AddChild(_railLocoSelect);

        _railWagonSpin = new SpinBox { MinValue = 1, MaxValue = 8, Value = 4, Prefix = "Vagões: " };
        _railWagonSpin.CustomMinimumSize = new Vector2(108, 0);
        buyRow.AddChild(_railWagonSpin);
        railBox.AddChild(buyRow);

        var spawnRow = new HBoxContainer();
        spawnRow.AddThemeConstantOverride("separation", 4);
        _railSpawnYardSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railSpawnYardSelect.AddThemeFontSizeOverride("font_size", 11);
        spawnRow.AddChild(_railSpawnYardSelect);

        var buyTrainBtn = new Button { Text = "🚂 Comprar Trem" };
        buyTrainBtn.AddThemeFontSizeOverride("font_size", 11);
        buyTrainBtn.Pressed += () =>
        {
            int yardId = (_railSpawnYardSelect.Selected >= 0 && _railSpawnYardSelect.Selected < _railYardPoolIds.Count)
                ? _railYardPoolIds[_railSpawnYardSelect.Selected]
                : 0;
            var created = _bridge.TryBuyAndDispatchTrain(_railLocoSelect.Selected, (int)_railWagonSpin.Value, yardId, out string msg);
            if (created != null)
            {
                _audio?.PlayStructureBuilt();
                RefreshRailTabSelectors();
            }
            else
            {
                _audio?.PlayError();
            }
            LogEvent(msg);
            RefreshRailTab();
            RefreshHud();
        };
        spawnRow.AddChild(buyTrainBtn);
        railBox.AddChild(spawnRow);

        railBox.AddChild(CreateLabel("ROTA INTERMODAL DO TREM", new Color("4b5563"), 11));
        var routeGrid = new GridContainer { Columns = 2 };
        routeGrid.AddThemeConstantOverride("h_separation", 4);
        routeGrid.AddThemeConstantOverride("v_separation", 4);

        _railTrainSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railTrainSelect.AddThemeFontSizeOverride("font_size", 11);
        _railCargoSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railCargoSelect.AddThemeFontSizeOverride("font_size", 11);
        foreach (var res in _railCargoPool)
            _railCargoSelect.AddItem(res == ResourceType.None ? "Qualquer Carga (Auto)" : res.ToString());

        _railOriginSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railOriginSelect.AddThemeFontSizeOverride("font_size", 11);
        _railDestSelect = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _railDestSelect.AddThemeFontSizeOverride("font_size", 11);

        routeGrid.AddChild(_railTrainSelect);
        routeGrid.AddChild(_railCargoSelect);
        routeGrid.AddChild(_railOriginSelect);
        routeGrid.AddChild(_railDestSelect);
        railBox.AddChild(routeGrid);

        var stageRow = new HBoxContainer();
        stageRow.AddThemeConstantOverride("separation", 4);

        var addLoadStopBtn = new Button { Text = "➕ Origem (Carga)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        addLoadStopBtn.AddThemeFontSizeOverride("font_size", 10);
        addLoadStopBtn.Pressed += () =>
        {
            if (_railOriginSelect.Selected < 0 || _railOriginSelect.Selected >= _railYardPoolIds.Count)
            {
                _audio?.PlayError();
                return;
            }
            var filter = _railCargoPool[Math.Clamp(_railCargoSelect.Selected, 0, _railCargoPool.Count - 1)];
            _pendingRailStops.Add(new IntermodalStop(_railYardPoolIds[_railOriginSelect.Selected], ScheduleAction.Load, filter));
            UpdateStagedRailStopsPreview();
        };

        var addUnloadStopBtn = new Button { Text = "➕ Destino (Descarga)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        addUnloadStopBtn.AddThemeFontSizeOverride("font_size", 10);
        addUnloadStopBtn.Pressed += () =>
        {
            if (_railDestSelect.Selected < 0 || _railDestSelect.Selected >= _railYardPoolIds.Count)
            {
                _audio?.PlayError();
                return;
            }
            _pendingRailStops.Add(new IntermodalStop(_railYardPoolIds[_railDestSelect.Selected], ScheduleAction.Unload));
            UpdateStagedRailStopsPreview();
        };

        var clearStopsBtn = new Button { Text = "🧹 Limpar" };
        clearStopsBtn.AddThemeFontSizeOverride("font_size", 10);
        clearStopsBtn.Pressed += () =>
        {
            _pendingRailStops.Clear();
            UpdateStagedRailStopsPreview();
        };

        stageRow.AddChild(addLoadStopBtn);
        stageRow.AddChild(addUnloadStopBtn);
        stageRow.AddChild(clearStopsBtn);
        railBox.AddChild(stageRow);

        _railStagedStopsLabel = CreateLabel("Rota rápida 2 paradas (ou adicione múltiplas paradas acima).", new Color("6b7280"), 10);
        railBox.AddChild(_railStagedStopsLabel);

        var routeActionRow = new HBoxContainer();
        routeActionRow.AddThemeConstantOverride("separation", 4);

        var assignRouteBtn = new Button { Text = "🛤️ Definir Rota & Despachar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        assignRouteBtn.AddThemeFontSizeOverride("font_size", 11);
        assignRouteBtn.Pressed += () =>
        {
            if (_railTrainSelect.Selected < 0 || _railTrainSelect.Selected >= _bridge.Engine.ActiveTrains.Count)
            {
                _audio?.PlayError();
                LogEvent("⚠️ Nenhum trem selecionado.");
                return;
            }

            var train = _bridge.Engine.ActiveTrains[_railTrainSelect.Selected];
            bool ok;
            string msg;

            if (_pendingRailStops.Count >= 2)
            {
                ok = _bridge.TryAssignMultiStopTrainRoute(train.Id, _pendingRailStops.ToList(), out msg);
                if (ok)
                {
                    _pendingRailStops.Clear();
                    UpdateStagedRailStopsPreview();
                }
            }
            else
            {
                if (_railOriginSelect.Selected < 0 || _railOriginSelect.Selected >= _railYardPoolIds.Count ||
                    _railDestSelect.Selected < 0 || _railDestSelect.Selected >= _railYardPoolIds.Count)
                {
                    _audio?.PlayError();
                    LogEvent("⚠️ Selecione Pátio de Origem e Destino.");
                    return;
                }

                int origId = _railYardPoolIds[_railOriginSelect.Selected];
                int destId = _railYardPoolIds[_railDestSelect.Selected];
                var filter = _railCargoPool[Math.Clamp(_railCargoSelect.Selected, 0, _railCargoPool.Count - 1)];
                ok = _bridge.TryAssignTrainRoute(train.Id, origId, destId, filter, out msg);
            }

            if (ok) _audio?.PlayStructureBuilt();
            else _audio?.PlayError();

            LogEvent(msg);
            RefreshRailTab();
        };

        var cloneTrainBtn = new Button { Text = "♻️ Clonar Trem" };
        cloneTrainBtn.AddThemeFontSizeOverride("font_size", 11);
        cloneTrainBtn.TooltipText = "Compra e despacha uma composição idêntica herdando vagões e rota.";
        cloneTrainBtn.Pressed += () =>
        {
            if (_railTrainSelect.Selected < 0 || _railTrainSelect.Selected >= _bridge.Engine.ActiveTrains.Count)
            {
                _audio?.PlayError();
                LogEvent("⚠️ Selecione um trem existente para clonar.");
                return;
            }

            var srcTrain = _bridge.Engine.ActiveTrains[_railTrainSelect.Selected];
            var cloned = _bridge.TryCloneTrain(srcTrain.Id, out string cloneMsg);
            if (cloned != null)
            {
                _audio?.PlayStructureBuilt();
                RefreshRailTabSelectors();
            }
            else
            {
                _audio?.PlayError();
            }
            LogEvent(cloneMsg);
            RefreshRailTab();
            RefreshHud();
        };

        routeActionRow.AddChild(assignRouteBtn);
        routeActionRow.AddChild(cloneTrainBtn);
        railBox.AddChild(routeActionRow);

        _railStatusText = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _railStatusText.AddThemeColorOverride("default_color", new Color("1f2937"));
        _railStatusText.AddThemeFontSizeOverride("normal_font_size", 11);
        _railStatusText.AddThemeFontSizeOverride("bold_font_size", 11);
        railBox.AddChild(_railStatusText);

        bodyMargin.AddChild(railBox);
    }

    private void RefreshRailTabSelectors()
    {
        if (_railSpawnYardSelect == null)
            return;

        int prevSpawn = _railSpawnYardSelect.Selected;
        int prevOrig = _railOriginSelect.Selected;
        int prevDest = _railDestSelect.Selected;
        int prevTrain = _railTrainSelect.Selected;

        _railYardPoolIds.Clear();
        _railSpawnYardSelect.Clear();
        _railOriginSelect.Clear();
        _railDestSelect.Clear();

        foreach (var yard in _bridge.Engine.IntermodalTerminals)
        {
            string tag = yard.LinkedSeaportId.HasValue ? $"{yard.Name} [Porto]" : yard.Name;
            _railYardPoolIds.Add(yard.Id);
            _railSpawnYardSelect.AddItem(tag);
            _railOriginSelect.AddItem($"Origem: {tag}");
            _railDestSelect.AddItem($"Destino: {tag}");
        }

        if (_railYardPoolIds.Count > 0)
        {
            _railSpawnYardSelect.Select(Math.Clamp(prevSpawn >= 0 ? prevSpawn : 0, 0, _railYardPoolIds.Count - 1));
            _railOriginSelect.Select(Math.Clamp(prevOrig >= 0 ? prevOrig : 0, 0, _railYardPoolIds.Count - 1));
            _railDestSelect.Select(prevDest >= 0 && prevDest < _railYardPoolIds.Count
                ? prevDest
                : (_railYardPoolIds.Count > 1 ? 1 : 0));
        }

        _railTrainSelect.Clear();
        foreach (var tr in _bridge.Engine.ActiveTrains)
        {
            _railTrainSelect.AddItem($"{tr.Name} ({tr.WagonCount}v • {tr.TotalCapacity}t)");
        }
        if (prevTrain >= 0 && prevTrain < _railTrainSelect.ItemCount)
            _railTrainSelect.Select(prevTrain);
        else if (_railTrainSelect.ItemCount > 0)
            _railTrainSelect.Select(0);

        UpdateStagedRailStopsPreview();
    }

    private void UpdateStagedRailStopsPreview()
    {
        if (_railStagedStopsLabel == null)
            return;

        if (_pendingRailStops.Count == 0)
        {
            _railStagedStopsLabel.Text = "Rota rápida 2 paradas (ou adicione múltiplas paradas acima).";
            return;
        }

        var parts = new List<string>(_pendingRailStops.Count);
        foreach (var stop in _pendingRailStops)
        {
            var yard = _bridge.Engine.IntermodalTerminals.FirstOrDefault(y => y.Id == stop.TerminalId);
            string yardName = yard?.Name ?? $"Pátio #{stop.TerminalId}";
            parts.Add(stop.Action == ScheduleAction.Load
                ? $"📥 {yardName}"
                : $"📤 {yardName}");
        }

        _railStagedStopsLabel.Text = $"Paradas ({_pendingRailStops.Count}): " + string.Join(" → ", parts);
    }

    private void RefreshRailTab()
    {
        if (_tabBodyRail == null)
            return;

        RefreshRailTabSelectors();
        int signalCount = _bridge.Engine.RailNodes.Count(n => n.HasBlockSignal);
        _railSummaryLabel.Text =
            $"Malha: {_bridge.Engine.RailNodes.Count} trilhos • 🚦 {signalCount} sinaleiros • 🏗️ {_bridge.Engine.IntermodalTerminals.Count} pátios • 🚂 {_bridge.Engine.ActiveTrains.Count} trens";

        var lines = new List<string>();
        lines.Add("[b]🚂 COMPOSIÇÕES ATIVAS:[/b]");
        if (_bridge.Engine.ActiveTrains.Count == 0)
        {
            lines.Add("Nenhuma composição adquirida ainda. Construa trilhos + 2 Pátios Intermodais e compre uma locomotiva!");
        }
        else
        {
            foreach (var tr in _bridge.Engine.ActiveTrains)
            {
                string statusText = tr.Status switch
                {
                    TrainStatus.WaitingAtSignal => "[color=#dc2626][b]🔴 RETIDO NO SINAL DE BLOCO[/b][/color]",
                    TrainStatus.TravelingToPickup => "🛤️ Indo carregar",
                    TrainStatus.TravelingToDelivery => "🚄 Transportando",
                    TrainStatus.Loading => "📥 Carregando",
                    TrainStatus.Unloading => "📤 Descarregando",
                    _ => "⏸️ Ocioso no pátio"
                };
                string wagonTag = RailWagonHelper.GetDisplayName(tr.WagonCategory);
                string cargoTag = tr.LoadedCargoAmount > 0 ? $"{tr.LoadedCargoType} ({tr.LoadedCargoAmount}/{tr.TotalCapacity}t)" : $"Vazio (0/{tr.TotalCapacity}t)";
                lines.Add($"• [b]{tr.Name}[/b] [{wagonTag}] — {statusText} | Carga: {cargoTag} | Receita: R$ {tr.TotalRevenueEarned:N0}");
            }
        }

        lines.Add("\n[b]🏗️ PÁTIOS RODOFERROVIÁRIOS (INTERMODAIS):[/b]");
        if (_bridge.Engine.IntermodalTerminals.Count == 0)
        {
            lines.Add("Nenhum Pátio Intermodal construído. Use a ferramenta 🏗️ Pátio próximo aos trilhos!");
        }
        else
        {
            foreach (var y in _bridge.Engine.IntermodalTerminals)
            {
                string links = (y.LinkedSeaportId.HasValue ? "🚢 Porto " : "")
                             + (y.LinkedHubId.HasValue ? "📦 Hub " : "")
                             + (y.RoadAccessNode != null ? "🚚 Estrada OK" : "⚠️ Sem estrada");
                string stored = y.StoredCargo.Count > 0
                    ? string.Join(", ", y.StoredCargo.Select(kv => $"{kv.Key}:{kv.Value}"))
                    : "Estoque vazio";
                lines.Add($"• [b]{y.Name}[/b] ({links}) | Transbordo: {y.TotalTransshippedUnits}t | [{stored}]");
            }
        }

        _railStatusText.Text = string.Join("\n", lines);
    }

    private void AppendRailInspectorDetails(List<string> lines, int sx, int sy)
    {
        var cell = _bridge.World.Grid[sx, sy];
        if (cell.HasRail)
        {
            var rNode = _bridge.Engine.GetRailNodeAt(sx, sy);
            int blockId = rNode?.OutgoingEdges.FirstOrDefault()?.BlockGroupId ?? 0;
            lines.Add($"\n🚂 [b]{cell.RailStructure.GetDisplayName()}[/b] (Bloco #{blockId})");
            if (cell.HasRailSignal)
            {
                var asp = _bridge.Engine.GetRailSignalAspectAt(sx, sy);
                lines.Add(asp == RailSignalAspect.Red
                    ? "🚦 Sinaleiro de Bloco: [color=#dc2626][b]🔴 VERMELHO (Bloco Ocupado)[/b][/color]"
                    : "🚦 Sinaleiro de Bloco: [color=#16a34a][b]🟢 VERDE (Via Livre)[/b][/color]");
            }
        }

        var yard = _bridge.Engine.IntermodalTerminals.FirstOrDefault(y => y.GridX == sx && y.GridY == sy);
        if (yard != null)
        {
            lines.Add($"\n🏗️ [b]{yard.Name}[/b] (Pátio Intermodal)");
            lines.Add($"🔗 Conexões: {(yard.RailAccessNode != null ? "🚂 Trilhos OK" : "Sem trilho")} | {(yard.RoadAccessNode != null ? "🚚 Estrada OK" : "Sem estrada")}{(yard.LinkedSeaportId.HasValue ? " | 🚢 Porto Integrado" : "")}");
            lines.Add($"📦 Estoque: {yard.TotalStoredUnits}/{yard.MaxStorageCapacity}t | Transbordo total: {yard.TotalTransshippedUnits}t");
            if (yard.StoredCargo.Count > 0)
            {
                lines.Add($"🧱 Cargas: {string.Join(" | ", yard.StoredCargo.Select(kv => $"{kv.Key}: {kv.Value}t"))}");
            }
        }
    }
}
