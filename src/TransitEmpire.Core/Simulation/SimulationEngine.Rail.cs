using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Pathfinding;

namespace TransitEmpire.Core.Simulation;

public partial class SimulationEngine
{
    public const long RailSignalConstructionCost = 600;
    public const long WagonUnitCost = 2_500;

    public List<RailNode> RailNodes { get; } = new();
    public List<RailEdge> RailEdges { get; } = new();
    public List<IntermodalTerminal> IntermodalTerminals { get; } = new();
    public List<Train> ActiveTrains { get; } = new();
    public RailPathfinder RailPathfinder { get; } = new();

    private int _nextRailNodeId = 1;
    private int _nextRailEdgeId = 1;
    private int _nextIntermodalTerminalId = 900;
    private int _nextTrainId = 1;
    private readonly Dictionary<(int X, int Y), RailNode> _railNodeByGrid = new();
    private readonly Dictionary<int, int> _occupiedRailBlockOwner = new();
    private int _cachedRailNodeCount = -1;

    public event Action<int, int, RailStructureType>? OnRailBuilt;
    public event Action<RailNode>? OnRailSignalPlaced;
    public event Action<IntermodalTerminal>? OnIntermodalTerminalBuilt;
    public event Action<Train, ResourceType, int, long>? OnTrainDelivered;

    internal void RecomputeNextRailIds()
    {
        _nextRailNodeId = RailNodes.Count > 0 ? RailNodes.Max(n => n.Id) + 1 : 1;
        _nextRailEdgeId = RailEdges.Count > 0 ? RailEdges.Max(e => e.Id) + 1 : 1;
        _nextIntermodalTerminalId = IntermodalTerminals.Count > 0 ? IntermodalTerminals.Max(t => t.Id) + 1 : 900;
        _nextTrainId = ActiveTrains.Count > 0 ? ActiveTrains.Max(t => t.Id) + 1 : 1;
        RebuildRailSpatialCache();
        RebuildRailBlocks();
        RefreshIntermodalLinks();
    }

    private void RebuildRailSpatialCache()
    {
        _railNodeByGrid.Clear();
        for (int i = 0; i < RailNodes.Count; i++)
        {
            var n = RailNodes[i];
            _railNodeByGrid[(n.GridX, n.GridY)] = n;
        }
        _cachedRailNodeCount = RailNodes.Count;
    }

    public RailNode? GetRailNodeAt(int x, int y)
    {
        if (_cachedRailNodeCount != RailNodes.Count)
            RebuildRailSpatialCache();
        return _railNodeByGrid.TryGetValue((x, y), out var node) ? node : null;
    }

    public RailNode? FindNearestRailNode(int x, int y, float maxManhattanDist = 4.0f)
    {
        RailNode? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < RailNodes.Count; i++)
        {
            var node = RailNodes[i];
            float d = MathF.Abs(node.GridX - x) + MathF.Abs(node.GridY - y);
            if (d <= maxManhattanDist && d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }
        return best;
    }

    /// <summary>
    /// Constrói um trilho ferroviário no tile (Ground, Viaduto sobre água ou Túnel em serras/montanhas).
    /// Permite passagem em nível sobre rodovias existentes fora de edificações.
    /// </summary>
    public bool BuildRailTile(int x, int y, out RailStructureType builtStructure)
    {
        builtStructure = RailStructureType.Ground;
        if (!IsTileInBounds(x, y))
            return false;

        ref var cell = ref World.Grid[x, y];
        if (cell.HasRail || cell.OccupantId != 0 || World.OceanMask[x, y])
            return false;

        builtStructure = RailStructureExtensions.FromTerrain(cell.Terrain);
        int cost = builtStructure.GetRailConstructionCost();
        if (Player.Balance < cost)
            return false;

        Player.Balance -= cost;
        Financials.RecordCapEx(cost);
        cell.HasRail = true;

        var newNode = new RailNode(_nextRailNodeId++, x, y);
        RailNodes.Add(newNode);

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };
        for (int d = 0; d < 4; d++)
        {
            int nx = x + dx[d];
            int ny = y + dy[d];
            if (!IsTileInBounds(nx, ny) || !World.Grid[nx, ny].HasRail)
                continue;

            var adjNode = GetRailNodeAt(nx, ny);
            if (adjNode == null)
            {
                adjNode = new RailNode(_nextRailNodeId++, nx, ny);
                RailNodes.Add(adjNode);
            }

            RailStructureType edgeStructure = builtStructure == RailStructureType.Tunnel || World.Grid[nx, ny].RailStructure == RailStructureType.Tunnel
                ? RailStructureType.Tunnel
                : (builtStructure == RailStructureType.Viaduct || World.Grid[nx, ny].RailStructure == RailStructureType.Viaduct
                    ? RailStructureType.Viaduct
                    : RailStructureType.Ground);

            var forward = new RailEdge(_nextRailEdgeId++, newNode, adjNode, 1.0f, edgeStructure);
            var backward = new RailEdge(_nextRailEdgeId++, adjNode, newNode, 1.0f, edgeStructure);
            newNode.OutgoingEdges.Add(forward);
            adjNode.OutgoingEdges.Add(backward);
            RailEdges.Add(forward);
            RailEdges.Add(backward);
        }

        RebuildRailBlocks();
        RefreshIntermodalLinks();
        Player.AddXp(builtStructure == RailStructureType.Ground ? 12 : 35);
        OnRailBuilt?.Invoke(x, y, builtStructure);
        return true;
    }

    /// <summary>
    /// Planeja um corredor ferroviário A* contínuo entre dois tiles (viadutos e túneis automáticos).
    /// </summary>
    public RoadBuildPlan PlanRailPath(int startX, int startY, int endX, int endY)
    {
        int w = World.Grid.GetLength(0);
        int h = World.Grid.GetLength(1);
        var empty = Array.Empty<(int, int)>();

        if (!IsTileInBounds(startX, startY) || !IsTileInBounds(endX, endY))
            return new RoadBuildPlan(empty, empty, 0, 0, 0, false, false);

        if (World.Grid[endX, endY].OccupantId != 0 && !World.Grid[endX, endY].HasRail)
            return new RoadBuildPlan(empty, empty, 0, 0, 0, false, false);

        var open = new PriorityQueue<(int X, int Y), float>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var gScore = new Dictionary<(int X, int Y), float>();

        var start = (startX, startY);
        var goal = (endX, endY);
        gScore[start] = 0f;
        open.Enqueue(start, MathF.Abs(endX - startX) + MathF.Abs(endY - startY));

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };
        bool found = false;

        while (open.Count > 0)
        {
            var curr = open.Dequeue();
            if (curr == goal)
            {
                found = true;
                break;
            }

            float currG = gScore[curr];
            for (int d = 0; d < 4; d++)
            {
                int nx = curr.X + dx[d];
                int ny = curr.Y + dy[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                    continue;

                ref readonly var cell = ref World.Grid[nx, ny];
                if (cell.OccupantId != 0 || World.OceanMask[nx, ny])
                    continue;

                float stepCost = cell.HasRail
                    ? 0.2f
                    : cell.Terrain switch
                    {
                        TerrainType.Water => 4.2f,
                        TerrainType.Hills or TerrainType.Mountain => 3.8f,
                        _ => cell.HasRoad ? 1.3f : 1.0f
                    };

                float tentativeG = currG + stepCost;
                var next = (nx, ny);
                if (!gScore.TryGetValue(next, out float prevG) || tentativeG < prevG)
                {
                    cameFrom[next] = curr;
                    gScore[next] = tentativeG;
                    open.Enqueue(next, tentativeG + MathF.Abs(endX - nx) + MathF.Abs(endY - ny));
                }
            }
        }

        if (!found)
            return new RoadBuildPlan(empty, empty, 0, 0, 0, false, false);

        var path = new List<(int X, int Y)>();
        var trace = goal;
        path.Add(trace);
        while (trace != start && cameFrom.TryGetValue(trace, out var parent))
        {
            trace = parent;
            path.Add(trace);
        }
        path.Reverse();

        var toBuild = new List<(int X, int Y)>();
        int viaducts = 0;
        int tunnels = 0;
        long totalCost = 0;

        foreach (var (px, py) in path)
        {
            ref readonly var cell = ref World.Grid[px, py];
            if (cell.HasRail || cell.OccupantId != 0 || World.OceanMask[px, py])
                continue;

            var st = RailStructureExtensions.FromTerrain(cell.Terrain);
            if (st == RailStructureType.Viaduct) viaducts++;
            else if (st == RailStructureType.Tunnel) tunnels++;

            totalCost += st.GetRailConstructionCost();
            toBuild.Add((px, py));
        }

        return new RoadBuildPlan(path, toBuild, viaducts, tunnels, totalCost, Player.Balance >= totalCost, toBuild.Count > 0);
    }

    public bool BuildPlannedRail(RoadBuildPlan plan, out int builtCount, out int viaductCount, out int tunnelCount)
    {
        builtCount = 0;
        viaductCount = 0;
        tunnelCount = 0;
        if (!plan.IsValid || !plan.CanAfford)
            return false;

        foreach (var (x, y) in plan.TilesToBuild)
        {
            if (BuildRailTile(x, y, out var st))
            {
                builtCount++;
                if (st == RailStructureType.Viaduct) viaductCount++;
                else if (st == RailStructureType.Tunnel) tunnelCount++;
            }
        }
        return builtCount > 0;
    }

    /// <summary>
    /// Instala um Sinaleiro Ferroviário por Bloco no nó ferroviário de (x, y),
    /// dividindo a linha em blocos independentes para evitar colisões/travamentos.
    /// </summary>
    public bool PlaceRailSignal(int x, int y, out string message)
    {
        if (!IsTileInBounds(x, y) || !World.Grid[x, y].HasRail)
        {
            message = "⚠️ Instale o sinaleiro ferroviário diretamente sobre um trilho construído.";
            return false;
        }

        var node = GetRailNodeAt(x, y);
        if (node == null)
        {
            node = new RailNode(_nextRailNodeId++, x, y);
            RailNodes.Add(node);
        }

        if (node.HasBlockSignal)
        {
            message = "🚦 Este ponto da via já possui sinaleiro de bloco instalado.";
            return false;
        }

        if (Player.Balance < RailSignalConstructionCost)
        {
            message = $"⚠️ Saldo insuficiente para Sinaleiro de Bloco (R$ {RailSignalConstructionCost:N0}).";
            return false;
        }

        Player.Balance -= RailSignalConstructionCost;
        Financials.RecordCapEx(RailSignalConstructionCost);
        node.HasBlockSignal = true;
        World.Grid[x, y].HasRailSignal = true;

        RebuildRailBlocks();
        OnRailSignalPlaced?.Invoke(node);
        message = $"🚦 Sinaleiro de bloco instalado em ({x},{y}) — malha particionada em blocos seguros!";
        return true;
    }

    /// <summary>
    /// Particiona todas as arestas ferroviárias em BlockGroupIds: arestas conectadas por um nó SEM sinaleiro
    /// pertencem ao mesmo bloco; nós com sinaleiro isolam os blocos adjacentes.
    /// </summary>
    public void RebuildRailBlocks()
    {
        foreach (var edge in RailEdges)
            edge.BlockGroupId = 0;

        var reverseLookup = new Dictionary<(int From, int To), RailEdge>();
        foreach (var edge in RailEdges)
            reverseLookup[(edge.FromNode.Id, edge.ToNode.Id)] = edge;

        int nextBlockId = 1;
        var queue = new Queue<RailEdge>();

        foreach (var seed in RailEdges)
        {
            if (seed.BlockGroupId != 0)
                continue;

            int blockId = nextBlockId++;
            AssignBlockPair(seed, blockId, reverseLookup, queue);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                ExpandAcrossNode(current.FromNode, blockId, reverseLookup, queue);
                ExpandAcrossNode(current.ToNode, blockId, reverseLookup, queue);
            }
        }
    }

    private static void ExpandAcrossNode(
        RailNode node,
        int blockId,
        Dictionary<(int From, int To), RailEdge> reverseLookup,
        Queue<RailEdge> queue)
    {
        if (node.HasBlockSignal)
            return;

        foreach (var outEdge in node.OutgoingEdges)
        {
            if (outEdge.BlockGroupId == 0)
                AssignBlockPair(outEdge, blockId, reverseLookup, queue);
        }
    }

    private static void AssignBlockPair(
        RailEdge edge,
        int blockId,
        Dictionary<(int From, int To), RailEdge> reverseLookup,
        Queue<RailEdge> queue)
    {
        if (edge.BlockGroupId == 0)
        {
            edge.BlockGroupId = blockId;
            queue.Enqueue(edge);
        }

        if (reverseLookup.TryGetValue((edge.ToNode.Id, edge.FromNode.Id), out var rev) && rev.BlockGroupId == 0)
        {
            rev.BlockGroupId = blockId;
            queue.Enqueue(rev);
        }
    }

    private void RebuildBlockOccupancySnapshot()
    {
        _occupiedRailBlockOwner.Clear();
        for (int i = 0; i < ActiveTrains.Count; i++)
        {
            var t = ActiveTrains[i];
            if (t.CurrentEdge != null && t.CurrentEdge.BlockGroupId > 0)
                _occupiedRailBlockOwner[t.CurrentEdge.BlockGroupId] = t.Id;
        }
    }

    /// <summary>
    /// Retorna o aspecto atual do sinaleiro no tile (Red se algum trem ocupar um bloco conectado a jusante ou aguardar aqui).
    /// </summary>
    public RailSignalAspect GetRailSignalAspectAt(int x, int y)
    {
        var node = GetRailNodeAt(x, y);
        if (node == null || !node.HasBlockSignal)
            return RailSignalAspect.Green;

        RebuildBlockOccupancySnapshot();
        for (int i = 0; i < ActiveTrains.Count; i++)
        {
            if (ActiveTrains[i].Status == TrainStatus.WaitingAtSignal && ActiveTrains[i].BlockedBySignalAtNodeId == node.Id)
                return RailSignalAspect.Red;
        }

        for (int i = 0; i < node.OutgoingEdges.Count; i++)
        {
            if (_occupiedRailBlockOwner.ContainsKey(node.OutgoingEdges[i].BlockGroupId))
                return RailSignalAspect.Red;
        }

        return RailSignalAspect.Green;
    }

    private bool IsBlockOccupiedByOtherTrain(int blockGroupId, int excludeTrainId)
    {
        if (_occupiedRailBlockOwner.TryGetValue(blockGroupId, out int ownerId))
            return ownerId != excludeTrainId;
        return false;
    }

    private bool CanTrainEnterEdge(Train train, RailEdge nextEdge)
    {
        // Trem já está dentro do mesmo bloco: segue livremente até o próximo sinaleiro/parada
        if (train.CurrentEdge != null && train.CurrentEdge.BlockGroupId == nextEdge.BlockGroupId)
            return true;

        return !IsBlockOccupiedByOtherTrain(nextEdge.BlockGroupId, train.Id);
    }

    /// <summary>
    /// Constrói um Pátio Rodoferroviário (Intermodal Hub) que conecta caminhões, trens e portos marítimos.
    /// </summary>
    public IntermodalTerminal? BuildIntermodalTerminal(int x, int y, out string message, string? customName = null)
    {
        if (!IsTileInBounds(x, y))
        {
            message = "⚠️ Coordenada fora dos limites do mapa.";
            return null;
        }

        ref var cell = ref World.Grid[x, y];
        if (cell.OccupantId != 0 || cell.HasRoad || cell.Terrain != TerrainType.Plains || World.OceanMask[x, y])
        {
            message = "⚠️ O Pátio Intermodal precisa ser construído em planície livre (sem estrada ou prédio).";
            return null;
        }

        var nearestRail = FindNearestRailNode(x, y, maxManhattanDist: 3.0f);
        if (nearestRail == null)
        {
            message = "🚂 Construa trilhos a até 3 blocos deste local antes de instalar o Pátio Intermodal.";
            return null;
        }

        if (Player.Balance < IntermodalTerminal.ConstructionCost)
        {
            message = $"⚠️ Saldo insuficiente para Pátio Intermodal (R$ {IntermodalTerminal.ConstructionCost:N0}).";
            return null;
        }

        Player.Balance -= IntermodalTerminal.ConstructionCost;
        Financials.RecordCapEx(IntermodalTerminal.ConstructionCost);
        Player.AddXp(350);

        int id = _nextIntermodalTerminalId++;
        string name = string.IsNullOrWhiteSpace(customName) ? $"Pátio Intermodal #{id - 899}" : customName.Trim();
        var nearestRoad = FindNearestRoadNode(x, y, maxManhattanDist: 4.0f);

        var terminal = new IntermodalTerminal(id, name, x, y, nearestRail, nearestRoad);
        cell.OccupantId = terminal.Id;
        IntermodalTerminals.Add(terminal);
        RefreshIntermodalLinks();
        ProcessIntermodalTransshipment(terminal);

        OnIntermodalTerminalBuilt?.Invoke(terminal);
        message = $"🏗️ {terminal.Name} inaugurado!"
            + (terminal.LinkedSeaportId.HasValue ? " (Integrado ao Porto Marítimo!)" : "")
            + (terminal.RoadAccessNode == null ? " Conecte uma estrada em até 4 blocos para caminhões alimentadores." : " Integração Rodoferroviária pronta!");
        return terminal;
    }

    /// <summary>
    /// Atualiza automaticamente conexões ferroviárias, rodoviárias, hubs e portos adjacentes dos pátios intermodais.
    /// </summary>
    public void RefreshIntermodalLinks()
    {
        foreach (var yard in IntermodalTerminals)
        {
            yard.RailAccessNode ??= FindNearestRailNode(yard.GridX, yard.GridY, maxManhattanDist: 3.0f);
            yard.RoadAccessNode = FindNearestRoadNode(yard.GridX, yard.GridY, maxManhattanDist: 4.0f);

            var nearbyHub = LogisticsHubs
                .Select(h => (Hub: h, Dist: Math.Abs(h.GridX - yard.GridX) + Math.Abs(h.GridY - yard.GridY)))
                .Where(x => x.Dist <= 5)
                .OrderBy(x => x.Dist)
                .FirstOrDefault();
            yard.LinkedHubId = nearbyHub.Hub?.Id;

            var nearbyPort = World.Seaports
                .Select(s => (Port: s, Dist: Math.Abs(s.GridX - yard.GridX) + Math.Abs(s.GridY - yard.GridY)))
                .Where(x => x.Dist <= 6)
                .OrderBy(x => x.Dist)
                .FirstOrDefault();
            yard.LinkedSeaportId = nearbyPort.Port?.Id;
        }
    }

    public bool BuyTrain(int locomotiveIndex, int wagonCount, int spawnTerminalId, out Train? createdTrain, out string message)
    {
        createdTrain = null;
        if (locomotiveIndex < 0 || locomotiveIndex >= RailCatalog.AvailableLocomotives.Count)
        {
            message = "⚠️ Locomotiva selecionada inválida.";
            return false;
        }

        var spec = RailCatalog.AvailableLocomotives[locomotiveIndex];
        if (Player.Level < spec.RequiredLevel)
        {
            message = $"🔒 {spec.Name} requer Nível {spec.RequiredLevel} da empresa.";
            return false;
        }

        int clampedWagons = Math.Clamp(wagonCount, 1, spec.MaxWagons);
        long totalPrice = spec.PurchasePrice + clampedWagons * WagonUnitCost;
        if (Player.Balance < totalPrice)
        {
            message = $"⚠️ Saldo insuficiente para locomotiva + {clampedWagons} vagões (R$ {totalPrice:N0}).";
            return false;
        }

        var terminal = IntermodalTerminals.FirstOrDefault(t => t.Id == spawnTerminalId)
                       ?? IntermodalTerminals.FirstOrDefault(t => t.RailAccessNode != null);
        if (terminal == null || terminal.RailAccessNode == null)
        {
            message = "🏗️ Construa primeiro um Pátio Intermodal conectado aos trilhos para despachar trens.";
            return false;
        }

        Player.Balance -= totalPrice;
        Financials.RecordCapEx(totalPrice);
        Player.AddXp(280);

        int id = _nextTrainId++;
        var train = new Train(id, $"Trem #{id} ({spec.Name.Split(' ')[0]})", spec, clampedWagons)
        {
            LastNode = terminal.RailAccessNode,
            Status = TrainStatus.Idle
        };
        ActiveTrains.Add(train);
        createdTrain = train;
        message = $"🚂 {train.Name} adquirido com {clampedWagons} vagões (cap. {train.TotalCapacity}t) em {terminal.Name}!";
        return true;
    }

    /// <summary>
    /// Clona locomotiva, vagões e toda a rota intermodal de uma composição existente em 1 clique.
    /// </summary>
    public bool CloneTrain(int sourceTrainId, out Train? clonedTrain, out string message)
    {
        clonedTrain = null;
        Train? source = null;
        for (int i = 0; i < ActiveTrains.Count; i++)
        {
            if (ActiveTrains[i].Id == sourceTrainId)
            {
                source = ActiveTrains[i];
                break;
            }
        }

        if (source == null)
        {
            message = "⚠️ Selecione um trem válido para clonar.";
            return false;
        }

        int locoIdx = -1;
        for (int i = 0; i < RailCatalog.AvailableLocomotives.Count; i++)
        {
            if (RailCatalog.AvailableLocomotives[i].Name == source.Locomotive.Name)
            {
                locoIdx = i;
                break;
            }
        }
        if (locoIdx < 0) locoIdx = 0;

        int spawnYardId = source.Stops.Count > 0 ? source.Stops[0].TerminalId : (IntermodalTerminals.FirstOrDefault()?.Id ?? 0);
        if (!BuyTrain(locoIdx, source.WagonCount, spawnYardId, out clonedTrain, out message))
            return false;

        if (clonedTrain != null && source.Stops.Count >= 2)
        {
            SetTrainRoute(clonedTrain.Id, source.Stops, out _);
            message = $"♻️ {clonedTrain.Name} clonado de {source.Name} com {source.WagonCount} vagões e {source.Stops.Count} paradas!";
        }
        return true;
    }

    public bool SetTrainRoute(int trainId, IReadOnlyList<IntermodalStop> stops, out string message)
    {
        var train = ActiveTrains.FirstOrDefault(t => t.Id == trainId);
        if (train == null)
        {
            message = "⚠️ Composição ferroviária não encontrada.";
            return false;
        }

        if (stops.Count < 2)
        {
            message = "⚠️ Defina ao menos 2 Pátios Intermodais (Origem e Destino) na linha férrea.";
            return false;
        }

        for (int i = 0; i < stops.Count; i++)
        {
            var currYard = IntermodalTerminals.FirstOrDefault(t => t.Id == stops[i].TerminalId);
            var nextYard = IntermodalTerminals.FirstOrDefault(t => t.Id == stops[(i + 1) % stops.Count].TerminalId);
            if (currYard?.RailAccessNode == null || nextYard?.RailAccessNode == null)
            {
                message = "⚠️ Todos os pátios da rota precisam ter acesso válido aos trilhos.";
                return false;
            }

            if (currYard.Id != nextYard.Id)
            {
                var checkPath = RailPathfinder.FindFastestRoute(currYard.RailAccessNode, nextYard.RailAccessNode);
                if (checkPath == null || checkPath.Count == 0)
                {
                    message = $"🚧 Não há ligação férrea conectando {currYard.Name} → {nextYard.Name}!";
                    return false;
                }
            }
        }

        train.Stops.Clear();
        foreach (var s in stops)
            train.Stops.Add(s.Clone());

        train.CurrentStopIndex = 0;
        train.Status = TrainStatus.Idle;
        ProcessScheduledTrains();
        message = $"🛤️ Rota ferroviária configurada para {train.Name} ({stops.Count} pátios).";
        return true;
    }

    private void TickRailNetwork(float deltaHours)
    {
        if (ActiveTrains.Count == 0)
            return;

        RebuildBlockOccupancySnapshot();

        // 1. Movimenta composições em curso respeitando sinaleiros de bloco
        for (int i = 0; i < ActiveTrains.Count; i++)
        {
            var train = ActiveTrains[i];
            if (train.CurrentEdge == null)
                continue;

            int oldBlockId = train.CurrentEdge.BlockGroupId;
            float speed = MathF.Min(train.Locomotive.MaxSpeedKmh, train.CurrentEdge.SpeedLimitKmh);
            float stepKm = speed * deltaHours;
            train.DistanceAlongCurrentEdgeKm += stepKm;
            train.CurrentTripDistanceKm += stepKm;
            train.TotalDistanceTravelledKm += stepKm;

            while (train.CurrentEdge != null && train.DistanceAlongCurrentEdgeKm >= train.CurrentEdge.DistanceKm)
            {
                float overshootKm = train.DistanceAlongCurrentEdgeKm - train.CurrentEdge.DistanceKm;
                var arrivedNode = train.CurrentEdge.ToNode;
                train.LastNode = arrivedNode;

                if (train.RemainingPath.Count > 0)
                {
                    var nextEdge = train.RemainingPath.Peek();
                    if (!CanTrainEnterEdge(train, nextEdge))
                    {
                        // Segura no sinaleiro antes de invadir o bloco ocupado
                        if (_occupiedRailBlockOwner.TryGetValue(oldBlockId, out int owner) && owner == train.Id)
                            _occupiedRailBlockOwner.Remove(oldBlockId);
                        train.CurrentEdge = null;
                        train.DistanceAlongCurrentEdgeKm = 0f;
                        train.BlockedBySignalAtNodeId = arrivedNode.Id;
                        train.Status = TrainStatus.WaitingAtSignal;
                        break;
                    }

                    train.RemainingPath.Dequeue();
                    train.CurrentEdge = nextEdge;
                    train.DistanceAlongCurrentEdgeKm = overshootKm;
                    train.BlockedBySignalAtNodeId = 0;
                    if (nextEdge.BlockGroupId != oldBlockId)
                    {
                        if (_occupiedRailBlockOwner.TryGetValue(oldBlockId, out int owner) && owner == train.Id)
                            _occupiedRailBlockOwner.Remove(oldBlockId);
                        if (nextEdge.BlockGroupId > 0)
                            _occupiedRailBlockOwner[nextEdge.BlockGroupId] = train.Id;
                        oldBlockId = nextEdge.BlockGroupId;
                    }
                }
                else
                {
                    if (_occupiedRailBlockOwner.TryGetValue(oldBlockId, out int owner) && owner == train.Id)
                        _occupiedRailBlockOwner.Remove(oldBlockId);
                    train.CurrentEdge = null;
                    train.DistanceAlongCurrentEdgeKm = 0f;
                    train.BlockedBySignalAtNodeId = 0;
                    ExecuteTrainStopArrival(train);
                    break;
                }
            }
        }

        // 2. Despacha ou libera trens ociosos/retidos no sinal
        ProcessScheduledTrains();
    }

    private void ProcessScheduledTrains()
    {
        for (int i = 0; i < ActiveTrains.Count; i++)
        {
            var train = ActiveTrains[i];

            if (train.Status == TrainStatus.WaitingAtSignal && train.CurrentEdge == null)
            {
                if (train.RemainingPath.Count > 0)
                {
                    var nextEdge = train.RemainingPath.Peek();
                    if (!CanTrainEnterEdge(train, nextEdge))
                        continue;

                    train.RemainingPath.Dequeue();
                    train.CurrentEdge = nextEdge;
                    train.DistanceAlongCurrentEdgeKm = 0f;
                    train.BlockedBySignalAtNodeId = 0;
                    if (nextEdge.BlockGroupId > 0)
                        _occupiedRailBlockOwner[nextEdge.BlockGroupId] = train.Id;
                    train.Status = train.LoadedCargoAmount > 0
                        ? TrainStatus.TravelingToDelivery
                        : TrainStatus.TravelingToPickup;
                    continue;
                }
            }

            if (train.CurrentEdge != null || train.Status != TrainStatus.Idle || train.Stops.Count == 0)
                continue;

            var stop = train.GetCurrentStop();
            if (stop == null) continue;

            var yard = IntermodalTerminals.FirstOrDefault(t => t.Id == stop.TerminalId);
            if (yard?.RailAccessNode == null) continue;

            train.LastNode ??= yard.RailAccessNode;

            if (train.LastNode.Id == yard.RailAccessNode.Id)
            {
                ExecuteTrainStopArrival(train);
                continue;
            }

            var path = RailPathfinder.FindFastestRoute(train.LastNode, yard.RailAccessNode);
            if (path == null || path.Count == 0)
                continue;

            var firstEdge = path[0];
            train.AssignPath(path);
            train.CurrentTripDistanceKm = 0f;

            if (!CanTrainEnterEdge(train, firstEdge))
            {
                train.BlockedBySignalAtNodeId = train.LastNode.Id;
                train.Status = TrainStatus.WaitingAtSignal;
                continue;
            }

            train.RemainingPath.Dequeue();
            train.CurrentEdge = firstEdge;
            if (firstEdge.BlockGroupId > 0)
                _occupiedRailBlockOwner[firstEdge.BlockGroupId] = train.Id;
            train.Status = stop.Action == ScheduleAction.Load
                ? TrainStatus.TravelingToPickup
                : TrainStatus.TravelingToDelivery;
        }
    }

    private void ExecuteTrainStopArrival(Train train)
    {
        var stop = train.GetCurrentStop();
        if (stop == null)
        {
            train.Status = TrainStatus.Idle;
            return;
        }

        var yard = IntermodalTerminals.FirstOrDefault(t => t.Id == stop.TerminalId);
        if (yard == null)
        {
            train.Status = TrainStatus.Idle;
            return;
        }

        if (stop.Action == ScheduleAction.Load)
        {
            PullNearbySupplyIntoYard(yard);

            ResourceType cargoType = train.LoadedCargoAmount > 0 ? train.LoadedCargoType : stop.CargoFilter;
            if (cargoType == ResourceType.None)
            {
                int bestAmt = 0;
                foreach (var kv in yard.StoredCargo)
                {
                    if (kv.Value > bestAmt)
                    {
                        bestAmt = kv.Value;
                        cargoType = kv.Key;
                    }
                }
            }

            if (cargoType != ResourceType.None)
            {
                int space = train.TotalCapacity - train.LoadedCargoAmount;
                int loaded = yard.WithdrawCargo(cargoType, space);
                if (loaded > 0)
                {
                    train.LoadedCargoType = cargoType;
                    train.LoadedCargoAmount += loaded;
                    train.WagonCategory = RailWagonHelper.FromResource(cargoType);
                }
            }

            if (train.LoadedCargoAmount <= 0)
            {
                // Mantém em espera no pátio até chegar carga rodoviária/industrial
                train.Status = TrainStatus.Idle;
                return;
            }

            train.AdvanceStop();
            train.Status = TrainStatus.Idle;
            return;
        }

        if (stop.Action == ScheduleAction.Unload)
        {
            if (train.LoadedCargoAmount > 0 && train.LoadedCargoType != ResourceType.None)
            {
                ResourceType cargoType = train.LoadedCargoType;
                int accepted = yard.StoreCargo(cargoType, train.LoadedCargoAmount);
                if (accepted > 0)
                {
                    float distFactor = 1f + Math.Clamp(train.CurrentTripDistanceKm / 35f, 0f, 1.5f);
                    float wagonBonus = RailWagonHelper.GetRevenueMultiplier(train.WagonCategory, cargoType);
                    long grossRevenue = (long)Math.Round(
                        accepted * 18f * distFactor * wagonBonus * LandmarkFreightBonusMultiplier * Milestones.FreightAndTollBonusMultiplier);
                    long runningCost = Math.Min(grossRevenue / 2, (long)Math.Round(train.CurrentTripDistanceKm * train.Locomotive.RunningCostPerKm));
                    long netPayout = Math.Max(20L, grossRevenue - runningCost);

                    Player.Balance += netPayout;
                    Financials.RecordFreightRevenue(netPayout);
                    if (runningCost > 0)
                        Financials.RecordFleetMaintenance(runningCost);

                    PlayerDeliveredVolume += accepted;
                    Player.AddXp((int)Math.Round(accepted * 8 * LandmarkXpMultiplier * Milestones.XpBonusMultiplier));
                    train.TotalTonnageDelivered += accepted;
                    train.TotalRevenueEarned += netPayout;
                    train.LastYieldRevenue = netPayout;

                    train.LoadedCargoAmount -= accepted;
                    if (train.LoadedCargoAmount <= 0)
                    {
                        train.LoadedCargoAmount = 0;
                        train.LoadedCargoType = ResourceType.None;
                    }

                    ProcessIntermodalTransshipment(yard);
                    OnTrainDelivered?.Invoke(train, cargoType, accepted, netPayout);
                }
            }

            train.AdvanceStop();
            train.Status = TrainStatus.Idle;
        }
    }

    private void PullNearbySupplyIntoYard(IntermodalTerminal yard)
    {
        foreach (var ind in World.Industries)
        {
            if (ind.OutputResource == ResourceType.None || ind.OutputStock <= 0)
                continue;

            int dist = Math.Abs(ind.GridX - yard.GridX) + Math.Abs(ind.GridY - yard.GridY);
            if (dist > 4)
                continue;

            int free = Math.Max(0, yard.MaxStorageCapacity - yard.TotalStoredUnits);
            if (free <= 0) break;

            int dispatched = ind.DispatchCargo(Math.Min(180, free));
            if (dispatched > 0)
            {
                int stored = yard.StoreCargo(ind.OutputResource, dispatched);
                yard.TotalTransshippedUnits += stored;
            }
        }

        if (yard.LinkedHubId is int hubId)
        {
            var hub = LogisticsHubs.FirstOrDefault(h => h.Id == hubId);
            if (hub != null)
            {
                foreach (var kv in hub.StoredCargo.ToList())
                {
                    int free = Math.Max(0, yard.MaxStorageCapacity - yard.TotalStoredUnits);
                    if (free <= 0) break;

                    int pulled = hub.WithdrawCargo(kv.Key, Math.Min(150, free));
                    if (pulled > 0)
                    {
                        yard.StoreCargo(kv.Key, pulled);
                        yard.TotalTransshippedUnits += pulled;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Transbordo automático Caminhão <-> Trem <-> Porto Marítimo / Indústria / Cidade no Pátio Intermodal.
    /// </summary>
    public void ProcessIntermodalTransshipment(IntermodalTerminal yard)
    {
        RefreshIntermodalLinks();

        // 1. Porto Marítimo <-> Pátio Intermodal (Exportação e Importação)
        if (yard.LinkedSeaportId is int seaportId)
        {
            var seaport = World.Seaports.FirstOrDefault(s => s.Id == seaportId);
            if (seaport != null)
            {
                foreach (var kv in yard.StoredCargo.ToList())
                {
                    if (seaport.TransitCargo.GetValueOrDefault(kv.Key, 0) > 0 || !seaport.AcceptsCargo(kv.Key))
                        continue;

                    int accepted = seaport.LoadExportCargo(kv.Key, kv.Value, out bool wasShipDocked);
                    if (accepted > 0)
                    {
                        yard.WithdrawCargo(kv.Key, accepted);
                        yard.TotalTransshippedUnits += accepted;
                        long portSynergyBonus = (long)Math.Round(accepted * (wasShipDocked ? 16f : 11f) * Milestones.SeaportExportBonusMultiplier);
                        Player.Balance += portSynergyBonus;
                        Financials.RecordFreightRevenue(portSynergyBonus);
                        Player.AddXp(accepted * 6);
                    }
                }

                // Importações marítimas desembarcadas no porto ficam disponíveis no pátio para trens e caminhões
                foreach (var kv in seaport.TransitCargo.ToList())
                {
                    int free = Math.Max(0, yard.MaxStorageCapacity - yard.TotalStoredUnits);
                    if (free <= 0) break;

                    int imported = seaport.DispatchTransitCargo(kv.Key, Math.Min(160, free));
                    if (imported > 0)
                    {
                        yard.StoreCargo(kv.Key, imported);
                        yard.TotalTransshippedUnits += imported;
                    }
                }
            }
        }

        // 2. Indústrias consumidoras dentro do raio intermodal (4 tiles)
        foreach (var ind in World.Industries)
        {
            if (ind.RequiredInputResource == ResourceType.None)
                continue;
            if (Math.Abs(ind.GridX - yard.GridX) + Math.Abs(ind.GridY - yard.GridY) > 4)
                continue;

            int need = ind.MaxStock - ind.InputStock;
            if (need <= 0)
                continue;

            int delivered = yard.WithdrawCargo(ind.RequiredInputResource, need);
            if (delivered > 0)
            {
                ind.ReceiveInputCargo(ind.RequiredInputResource, delivered);
                yard.TotalTransshippedUnits += delivered;
            }
        }

        // 3. Cidades dentro do raio intermodal (5 tiles)
        foreach (var city in World.Cities)
        {
            if (Math.Abs(city.CenterX - yard.GridX) + Math.Abs(city.CenterY - yard.GridY) > 5)
                continue;

            foreach (var kv in yard.StoredCargo.ToList())
            {
                if (!city.Warehouse.DailyDemand.ContainsKey(kv.Key))
                    continue;

                int availableToDeliver = Math.Min(120, kv.Value);
                int accepted = city.Warehouse.ReceiveCargo(kv.Key, availableToDeliver);
                if (accepted > 0)
                {
                    yard.WithdrawCargo(kv.Key, accepted);
                    long urbanSale = accepted * 12L;
                    Player.Balance += urbanSale;
                    Financials.RecordFreightRevenue(urbanSale);
                    yard.TotalTransshippedUnits += accepted;
                    RecordCityContractProgress(city, kv.Key, accepted);
                }
            }
        }
    }

    private void ProcessIntermodalDailyCycle()
    {
        for (int i = 0; i < IntermodalTerminals.Count; i++)
        {
            var yard = IntermodalTerminals[i];
            PullNearbySupplyIntoYard(yard);
            ProcessIntermodalTransshipment(yard);
        }
    }

    private void DeliverCargoToIntermodalTerminal(Truck truck, IntermodalTerminal yard)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int accepted = yard.StoreCargo(deliveredType, truck.LoadedCargoAmount);
        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            long feederRevenue = (long)Math.Round(accepted * 11f * distanceFactor * Research.HeavyFreightBonusMultiplier);
            FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, feederRevenue, accepted * 5);
            yard.TotalTransshippedUnits += accepted;
            ProcessIntermodalTransshipment(yard);
        }

        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    private int TryLoadAtIntermodalTerminal(Truck truck, IntermodalTerminal yard, ScheduleStop stop)
    {
        int space = truck.Spec.Capacity - truck.LoadedCargoAmount;
        if (space <= 0)
            return 0;

        ResourceType targetType = truck.LoadedCargoAmount > 0 ? truck.LoadedCargoType : stop.CargoFilter;
        if (targetType == ResourceType.None)
        {
            int maxAmt = 0;
            foreach (var kv in yard.StoredCargo)
            {
                if (kv.Value > maxAmt)
                {
                    maxAmt = kv.Value;
                    targetType = kv.Key;
                }
            }
        }

        if (targetType == ResourceType.None)
            return 0;

        int withdrawn = yard.WithdrawCargo(targetType, space);
        if (withdrawn > 0)
        {
            truck.LoadedCargoType = targetType;
            truck.LoadedCargoAmount += withdrawn;
            truck.LoadedFromHub = true;
            yard.TotalTransshippedUnits += withdrawn;
            RefreshTruckSpecialConvoyState(truck);
        }
        return withdrawn;
    }

    private bool TryDemolishRailEntityAt(int x, int y, out DemolishResult result, out string message)
    {
        result = default;
        message = string.Empty;

        var yard = IntermodalTerminals.FirstOrDefault(t => t.GridX == x && t.GridY == y);
        if (yard != null)
        {
            if (ActiveTrains.Any(tr => tr.Stops.Any(s => s.TerminalId == yard.Id)))
            {
                message = $"⚠️ {yard.Name} pertence à rota de um trem ativo. Remova ou reatribua a rota antes.";
                return true;
            }

            long refund = (long)Math.Round(IntermodalTerminal.ConstructionCost * 0.40);
            World.Grid[x, y].OccupantId = 0;
            IntermodalTerminals.Remove(yard);
            Player.Balance += refund;
            result = new DemolishResult(DemolishTargetKind.IntermodalYard, x, y, yard.Id, RoadStructureType.Ground, refund);
            message = $"🚜 {yard.Name} demolido (+R$ {refund:N0} reembolso 40%).";
            return true;
        }

        var railNode = GetRailNodeAt(x, y);
        if (railNode != null && railNode.HasBlockSignal)
        {
            long sigRefund = (long)Math.Round(RailSignalConstructionCost * 0.40);
            railNode.HasBlockSignal = false;
            World.Grid[x, y].HasRailSignal = false;
            RebuildRailBlocks();
            Player.Balance += sigRefund;
            result = new DemolishResult(DemolishTargetKind.RailSignal, x, y, railNode.Id, RoadStructureType.Ground, sigRefund);
            message = $"🚜 Sinaleiro de bloco removido em ({x},{y}) (+R$ {sigRefund:N0}).";
            return true;
        }

        ref var cell = ref World.Grid[x, y];
        if (!cell.HasRail || cell.HasRoad)
            return false;

        if (ActiveTrains.Any(t => t.CurrentEdge != null && (t.CurrentEdge.FromNode.Id == railNode?.Id || t.CurrentEdge.ToNode.Id == railNode?.Id)))
        {
            message = "⚠️ Não é possível demolir um trilho enquanto um trem trafega por ele.";
            return true;
        }

        RailStructureType st = cell.RailStructure;
        long railRefund = (long)Math.Round(st.GetRailConstructionCost() * 0.40);
        cell.HasRail = false;
        cell.HasRailSignal = false;

        if (railNode != null)
        {
            for (int i = RailEdges.Count - 1; i >= 0; i--)
            {
                var edge = RailEdges[i];
                if (edge.FromNode.Id == railNode.Id || edge.ToNode.Id == railNode.Id)
                {
                    edge.FromNode.OutgoingEdges.Remove(edge);
                    RailEdges.RemoveAt(i);
                }
            }
            RailNodes.Remove(railNode);
            foreach (var yrd in IntermodalTerminals)
            {
                if (yrd.RailAccessNode?.Id == railNode.Id)
                    yrd.RailAccessNode = null;
            }
        }

        RebuildRailBlocks();
        RefreshIntermodalLinks();
        Player.Balance += railRefund;
        result = new DemolishResult(DemolishTargetKind.Rail, x, y, railNode?.Id ?? 0, RoadStructureType.Ground, railRefund);
        message = $"🚜 {st.GetDisplayName()} demolido (+R$ {railRefund:N0} reembolso 40%).";
        return true;
    }
}
