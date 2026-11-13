using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Pathfinding;

namespace TransitEmpire.Core.Simulation;

public enum SeasonalEventType
{
    None = 0,
    BumperHarvest = 1,
    WinterBlizzard = 2,
    MountainLandslide = 3
}

public record RegionUnlockInfo(int RegionNumber, string RegionName, City? NewCity, Industry? NewIndustry);

public class GameCalendar
{
    public int Year { get; set; } = 2026;
    public int Month { get; set; } = 11;
    public int Day { get; set; } = 5;
    public int Hour { get; set; } = 8;
    public int Minute { get; set; } = 0;

    private static readonly int[] DaysInMonth = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    public void AdvanceHours(float deltaHours)
    {
        int totalMinutesToAdd = (int)(deltaHours * 60f);
        Minute += totalMinutesToAdd;

        while (Minute >= 60)
        {
            Minute -= 60;
            Hour++;
            if (Hour >= 24)
            {
                Hour = 0;
                Day++;
                int maxDays = (Month >= 1 && Month <= 12) ? DaysInMonth[Month] : 30;
                if (Day > maxDays)
                {
                    Day = 1;
                    Month++;
                    if (Month > 12)
                    {
                        Month = 1;
                        Year++;
                    }
                }
            }
        }
    }

    public override string ToString() => $"{Day:D2}/{Month:D2}/{Year} {Hour:D2}:{Minute:D2}";
}

public class PlayerState
{
    public string CompanyName { get; set; } = "Transit Empire Logística";
    public FleetLivery CompanyLivery { get; set; } = new();
    public int Level { get; set; } = 1;
    public int CurrentXp { get; set; } = 0;
    public int NextLevelXp => Level * 1000;
    public long Balance { get; set; } = 30000; // Saldo inicial
    public long LoanDebt { get; set; } = 0;    // Saldo devedor de empréstimos bancários
    public long MaxCreditLimit => Level * 50000L;

    public void AddXp(int amount)
    {
        CurrentXp += amount;
        while (CurrentXp >= NextLevelXp)
        {
            CurrentXp -= NextLevelXp;
            Level++;
        }
    }

    /// <summary>
    /// Contrai um empréstimo bancário dentro do limite de crédito da empresa.
    /// </summary>
    public bool TakeLoan(long amount)
    {
        if (amount <= 0 || LoanDebt + amount > MaxCreditLimit)
            return false;

        LoanDebt += amount;
        Balance += amount;
        return true;
    }

    /// <summary>
    /// Amortiza parcial ou totalmente a dívida bancária usando o saldo em caixa.
    /// </summary>
    public bool RepayLoan(long amount)
    {
        if (amount <= 0 || LoanDebt <= 0 || Balance <= 0)
            return false;

        long payment = Math.Min(amount, Math.Min(LoanDebt, Balance));
        Balance -= payment;
        LoanDebt -= payment;
        return true;
    }
}

/// <summary>
/// Motor central da simulação determinística do Transit Empire.
/// </summary>
public class SimulationEngine
{
    public GeneratedWorld World { get; }
    public GameCalendar Calendar { get; } = new();
    public PlayerState Player { get; } = new();
    public ResearchManager Research { get; } = new();
    public FinancialLedger Financials { get; } = new();
    public MilestoneTracker Milestones { get; } = new();
    public List<Truck> ActiveTrucks { get; } = new();
    public List<Garage> Garages { get; } = new();
    public List<LogisticsHub> LogisticsHubs { get; } = new();
    public List<BusTerminal> BusTerminals { get; } = new();
    public List<LogisticsLine> LogisticsLines { get; } = new();
    public List<TollStation> TollStations { get; } = new();
        public List<LogisticsContract> ActiveContracts { get; } = new();
    public List<RivalCompany> Rivals { get; } = new();
    public List<TrafficVehicle> TrafficVehicles { get; } = new();
    public RoadPathfinder Pathfinder { get; } = new();
    public WeatherCondition CurrentWeather { get; set; } = WeatherCondition.Clear;
    public SeasonalEventType ActiveSeasonalEvent { get; private set; } = SeasonalEventType.None;
    public float SeasonalEventRemainingHours { get; private set; } = 0f;
    public int BlockedLandslideEdgeId { get; private set; } = 0;
    public bool AutoRoadMaintenanceEnabled { get; set; } = true;
    public bool IsWinterBlizzardActive => ActiveSeasonalEvent == SeasonalEventType.WinterBlizzard;
    public int PlayerDeliveredVolume { get; private set; } = 0;
    public int PlayerContractsCompleted { get; private set; } = 0;
    public int CurrentTick { get; private set; } = 0;
    
    private readonly Random _rng = new();
    private readonly Dictionary<(int X, int Y), RoadNode> _nearestNodeCache = new();
    private readonly Dictionary<(int X, int Y), RoadNode> _roadNodeByGrid = new();
    private readonly Dictionary<(int X, int Y), List<RoadEdge>> _edgesByTile = new();
    private int _cachedRoadNodeCount = -1;
    private int _cachedRoadEdgeCount = -1;
    private readonly Dictionary<int, TollStation> _tollByNodeId = new();
    private int _nextTruckId = 1;
    private int _nextGarageId = 1;
    private int _nextHubId = 500;
    private int _nextBusTerminalId = 700;
    private int _nextLineId = 1;
    private int _nextTollId = 1;
    private int _nextContractId = 1;
    private int _nextCityId;
    private int _nextIndustryId;
    private int _nextRoadNodeId;
    private int _nextRoadEdgeId;
    private int _nextTrafficVehicleId = 1;
    private int _totalUrbanExpansions = 0;
    private float _trafficSpawnAccumulator = 0f;

    public event Action<City, int, int>? OnCityExpanded;
    public event Action<City, ResourceType, int, bool>? OnDemandIncreased;
    public event Action<RegionUnlockInfo>? OnRegionUnlocked;
    public event Action<RoadEdge, RoadTier>? OnRoadUpgraded;
    public event Action<Truck, ResourceType, int, long>? OnDeliveryCompleted;
    public event Action<Garage>? OnGarageBuilt;
    public event Action<LogisticsHub>? OnLogisticsHubBuilt;
    public event Action<LogisticsContract>? OnContractCompleted;
    public event Action<WeatherCondition>? OnWeatherChanged;
    public event Action<ResearchNode>? OnResearchCompleted;
    public event Action<FinancialStatement>? OnMonthlyFinancialClosed;
    public event Action<Truck, string, long>? OnTruckPitStopCompleted;
    public event Action<Industry>? OnIndustryUpgraded;
    public event Action<SeasonalEventType, float>? OnSeasonalEventChanged;
    public event Action<City, long>? OnMunicipalSubsidyPaid;
    public event Action<int, long>? OnRoadMaintained;
    public event Action<RoadNode>? OnTrafficControlBuilt;
    public event Action<TollStation>? OnTollStationBuilt;
    public event Action<TollStation, long>? OnTollCollected;
    public event Action<TollStation, string, long>? OnTollWeighUpdated;
    public event Action<LogisticsContract, RivalCompany>? OnContractLostToRival;
    public event Action<LogisticsLine>? OnLogisticsLineUpdated;
    public event Action<Truck, TruckRank>? OnTruckRankUp;
    public event Action<SeaportTerminal, bool, int>? OnSeaportShipStatusChanged;
    public event Action<RegionalLandmark, bool, bool>? OnLandmarkProgress;
    public event Action<MilestoneDefinition>? OnMilestoneUnlocked;
    public event Action<int, int, TerrainType, TerrainType>? OnTileTerraformed;
    public event Action<Truck, Truck, string>? OnTruckAutoReplaced;
    public event Action<BusTerminal>? OnBusTerminalBuilt;
    public event Action<Truck, BusTerminal, int, long>? OnPassengersDelivered;

    public List<OperationalBottleneck> DiagnoseBottlenecks() => OperationalDiagnostics.Analyze(this);

    public List<MilestoneDefinition> EvaluateMilestones()
    {
        var unlocked = Milestones.EvaluateAndUnlock(this);
        foreach (var m in unlocked)
        {
            Player.Balance += m.CashReward;
            Player.AddXp(m.XpReward);
            Financials.RecordContractBonus(m.CashReward);
            OnMilestoneUnlocked?.Invoke(m);
        }
        return unlocked;
    }

    public bool HasCompletedLandmark(LandmarkKind kind) =>
        World.Landmarks.Any(l => l.Kind == kind && l.IsCompleted);

    public float LandmarkFreightBonusMultiplier => HasCompletedLandmark(LandmarkKind.HydroelectricDam) ? 1.15f : 1.0f;
    public float LandmarkMaintenanceMultiplier => HasCompletedLandmark(LandmarkKind.HydroelectricDam) ? 0.90f : 1.0f;
    public float LandmarkXpMultiplier => HasCompletedLandmark(LandmarkKind.MunicipalStadium) ? 1.25f : 1.0f;
    public float LandmarkFuelMultiplier => HasCompletedLandmark(LandmarkKind.WindFarmPark) ? 0.85f : 1.0f;
    public float LandmarkProductionMultiplier => HasCompletedLandmark(LandmarkKind.WindFarmPark) ? 1.10f : 1.0f;

    public SimulationEngine(GeneratedWorld world)
        : this(world, generateInitialContracts: true)
    {
    }

    internal SimulationEngine(GeneratedWorld world, bool generateInitialContracts)
    {
        World = world;
        SyncAllEdgeBiomes();
        RecomputeNextIds();
        if (generateInitialContracts)
        {
            EnsureRivalsInitialized();
            GenerateInitialContracts();
        }
    }

    internal void RecomputeNextIds()
    {
        _nextCityId = World.Cities.Count > 0 ? World.Cities.Max(c => c.Id) + 1 : 1;
        _nextIndustryId = World.Industries.Count > 0 ? World.Industries.Max(i => i.Id) + 1 : 100;
        _nextRoadNodeId = World.RoadNodes.Count > 0 ? World.RoadNodes.Max(n => n.Id) + 1 : 1;
        _nextRoadEdgeId = World.RoadEdges.Count > 0 ? World.RoadEdges.Max(e => e.Id) + 1 : 1;
        _nextTruckId = ActiveTrucks.Count > 0 ? ActiveTrucks.Max(t => t.Id) + 1 : 1;
        _nextGarageId = Garages.Count > 0 ? Garages.Max(g => g.Id) + 1 : 1;
        _nextHubId = LogisticsHubs.Count > 0 ? LogisticsHubs.Max(h => h.Id) + 1 : 500;
        _nextBusTerminalId = BusTerminals.Count > 0 ? BusTerminals.Max(b => b.Id) + 1 : 700;
        _nextLineId = LogisticsLines.Count > 0 ? LogisticsLines.Max(l => l.Id) + 1 : 1;
        _nextTollId = TollStations.Count > 0 ? TollStations.Max(t => t.Id) + 1 : 1;
        _nextContractId = ActiveContracts.Count > 0 ? ActiveContracts.Max(c => c.Id) + 1 : 1;
        RebuildTollLookupCache();
        RebuildRoadSpatialCache();
    }

    internal void RestoreSimulationState(
        int currentTick,
        WeatherCondition weather,
        SeasonalEventType seasonalEvent = SeasonalEventType.None,
        float seasonalRemainingHours = 0f,
        int blockedLandslideEdgeId = 0,
        bool autoRoadMaintenance = true,
        int playerDeliveredVolume = 0,
        int playerContractsCompleted = 0)
    {
        CurrentTick = currentTick;
        CurrentWeather = weather;
        ActiveSeasonalEvent = seasonalEvent;
        SeasonalEventRemainingHours = seasonalRemainingHours;
        BlockedLandslideEdgeId = blockedLandslideEdgeId;
        AutoRoadMaintenanceEnabled = autoRoadMaintenance;
        PlayerDeliveredVolume = playerDeliveredVolume;
        PlayerContractsCompleted = playerContractsCompleted;
        EnsureRivalsInitialized();
        SyncAllEdgeBiomes();
        RecomputeNextIds();
    }

    private void GenerateInitialContracts()
    {
        if (World.Cities.Count == 0) return;

        foreach (var city in World.Cities.Take(2))
        {
            CreateContractForCity(city);
        }
    }

    /// <summary>
    /// Emite um novo Contrato Expresso Licitatório para uma cidade específica.
    /// </summary>
    public LogisticsContract CreateContractForCity(City city, ResourceType resource = ResourceType.Food, int amount = 45)
    {
        long bonusMoney = amount * 35L;
        int bonusXp = amount * 12;
        var contract = new LogisticsContract(
            _nextContractId++,
            $"Contrato Expresso: {resource} para {city.Name}",
            city,
            resource,
            amount,
            bonusMoney,
            bonusXp,
            durationHours: 240f
        );
        ActiveContracts.Add(contract);
        return contract;
    }

    /// <summary>
    /// Emite um Contrato Especial de Carga Indivisível / Comboio Escoltado com recompensa tripla (3x).
    /// </summary>
    public LogisticsContract CreateSpecialConvoyContract(City city, ResourceType resource = ResourceType.Steel, int amount = 60)
    {
        const float convoyMultiplier = 3.0f;
        long bonusMoney = (long)(amount * 35L * convoyMultiplier);
        int bonusXp = (int)(amount * 12 * convoyMultiplier);
        var contract = new LogisticsContract(
            _nextContractId++,
            $"🚨 Comboio Especial (3x): {resource} Indivisível → {city.Name}",
            city,
            resource,
            amount,
            bonusMoney,
            bonusXp,
            durationHours: 240f,
            isSpecialConvoy: true,
            freightMultiplier: convoyMultiplier
        );
        ActiveContracts.Add(contract);
        return contract;
    }

    /// <summary>
    /// Constrói uma garagem na grade adjacente a um nó viário existente.
    /// </summary>
    public Garage? BuildGarage(int x, int y, string name)
    {
        if (x < 0 || x >= World.Grid.GetLength(0) || y < 0 || y >= World.Grid.GetLength(1))
            return null;

        ref var cell = ref World.Grid[x, y];
        if (cell.OccupantId != 0 || cell.HasRoad || cell.Terrain != TerrainType.Plains)
            return null;

        const int garageCost = 8000;
        if (Player.Balance < garageCost)
            return null;

        RoadNode? nearestNode = FindNearestRoadNode(x, y, maxManhattanDist: 3.0f);
        if (nearestNode == null) return null; // Sem estrada próxima

        Player.Balance -= garageCost;
        Financials.RecordCapEx(garageCost);
        var garage = new Garage(_nextGarageId++, name, x, y, nearestNode);
        cell.OccupantId = garage.Id;
        Garages.Add(garage);

        OnGarageBuilt?.Invoke(garage);
        return garage;
    }

    /// <summary>
    /// Constrói um Centro de Transbordo / Hub Logístico adjacente à malha viária (Regra #4 do README).
    /// </summary>
    public LogisticsHub? BuildLogisticsHub(int x, int y, string name)
    {
        if (x < 0 || x >= World.Grid.GetLength(0) || y < 0 || y >= World.Grid.GetLength(1))
            return null;

        ref var cell = ref World.Grid[x, y];
        if (cell.OccupantId != 0 || cell.HasRoad || cell.Terrain != TerrainType.Plains)
            return null;

        const int hubCost = 12000;
        if (Player.Balance < hubCost)
            return null;

        RoadNode? nearestNode = FindNearestRoadNode(x, y, maxManhattanDist: 3.0f);
        if (nearestNode == null) return null;

        Player.Balance -= hubCost;
        Financials.RecordCapEx(hubCost);
        Player.AddXp(250);

        var hub = new LogisticsHub(_nextHubId++, name, x, y, nearestNode);
        cell.OccupantId = hub.Id;
        LogisticsHubs.Add(hub);

        OnLogisticsHubBuilt?.Invoke(hub);
        return hub;
    }

    /// <summary>
    /// Constrói um Terminal Rodoviário Urbano próximo a uma cidade e a uma via existente (Sprint F3).
    /// </summary>
    public BusTerminal? BuildBusTerminal(int x, int y, out string statusMessage, string? customName = null)
    {
        if (!IsTileInBounds(x, y))
        {
            statusMessage = "⚠️ Coordenada fora dos limites do mapa.";
            return null;
        }

        ref var cell = ref World.Grid[x, y];
        if (cell.OccupantId != 0 || cell.HasRoad || cell.Terrain != TerrainType.Plains || World.OceanMask[x, y])
        {
            statusMessage = "⚠️ A Rodoviária deve ser construída em planície vazia fora da estrada.";
            return null;
        }

        if (Player.Balance < BusTerminal.ConstructionCost)
        {
            statusMessage = $"⚠️ Saldo insuficiente para Rodoviária (R$ {BusTerminal.ConstructionCost:N0}).";
            return null;
        }

        RoadNode? nearestNode = FindNearestRoadNode(x, y, maxManhattanDist: 3.0f);
        if (nearestNode == null)
        {
            statusMessage = "⚠️ A Rodoviária precisa estar até 3 células de uma via existente.";
            return null;
        }

        City? targetCity = World.Cities
            .Where(c => c.UrbanTiles.Any(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y) <= 5))
            .OrderBy(c => c.UrbanTiles.Min(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y)))
            .FirstOrDefault();

        if (targetCity == null)
        {
            statusMessage = "⚠️ A Rodoviária deve ficar próxima à área urbana de uma cidade (até 5 células).";
            return null;
        }

        if (BusTerminals.Any(b => b.CityId == targetCity.Id))
        {
            statusMessage = $"⚠️ {targetCity.Name} já possui um Terminal Rodoviário ativo.";
            return null;
        }

        Player.Balance -= BusTerminal.ConstructionCost;
        Financials.RecordCapEx(BusTerminal.ConstructionCost);
        Player.AddXp(180);

        string name = string.IsNullOrWhiteSpace(customName)
            ? $"Rodoviária de {targetCity.Name}"
            : customName.Trim();

        var terminal = new BusTerminal(_nextBusTerminalId++, name, targetCity.Id, x, y, nearestNode);
        cell.OccupantId = terminal.Id;
        BusTerminals.Add(terminal);
        OnBusTerminalBuilt?.Invoke(terminal);

        statusMessage = $"🚌 {terminal.Name} inaugurada em ({x}, {y})!";
        return terminal;
    }

    private RoadNode? FindNearestRoadNode(int x, int y, float maxManhattanDist = float.MaxValue)
    {
        RoadNode? nearestNode = null;
        float minDistance = float.MaxValue;
        foreach (var node in World.RoadNodes)
        {
            float dist = MathF.Abs(node.GridX - x) + MathF.Abs(node.GridY - y);
            if (dist < minDistance && dist <= maxManhattanDist)
            {
                minDistance = dist;
                nearestNode = node;
            }
        }
        return nearestNode;
    }

    /// <summary>
    /// Retorna o nó viário mais próximo de (x, y) usando cache espacial O(1).
    /// </summary>
    public RoadNode? FindNearestRoadNodeCached(int x, int y)
    {
        if (World.RoadNodes.Count == 0)
            return null;

        if (_nearestNodeCache.TryGetValue((x, y), out var cached))
            return cached;

        var nearest = FindNearestRoadNode(x, y);
        if (nearest != null)
            _nearestNodeCache[(x, y)] = nearest;
        return nearest;
    }

    public RoadNode? GetTruckCurrentNode(Truck truck)
    {
        if (truck.CurrentEdge != null)
            return truck.CurrentEdge.ToNode;
        if (truck.LastNode != null)
            return truck.LastNode;

        var garage = Garages.FirstOrDefault(g => g.StationedTrucks.Contains(truck)) ?? Garages.FirstOrDefault();
        if (garage != null)
            return garage.AccessNode;

        return World.RoadNodes.FirstOrDefault();
    }

    public RoadNode? GetStopAccessNode(ScheduleStop stop)
    {
        switch (stop.Kind)
        {
            case ScheduleStopKind.Industry:
                var ind = World.Industries.FirstOrDefault(i => i.Id == stop.TargetId);
                return ind != null ? FindNearestRoadNodeCached(ind.GridX, ind.GridY) : null;
            case ScheduleStopKind.City:
                var city = World.Cities.FirstOrDefault(c => c.Id == stop.TargetId);
                return city != null ? FindNearestRoadNodeCached(city.CenterX, city.CenterY) : null;
            case ScheduleStopKind.LogisticsHub:
                return LogisticsHubs.FirstOrDefault(h => h.Id == stop.TargetId)?.AccessNode;
            case ScheduleStopKind.Garage:
                return Garages.FirstOrDefault(g => g.Id == stop.TargetId)?.AccessNode;
            case ScheduleStopKind.Seaport:
                var seaport = World.Seaports.FirstOrDefault(s => s.Id == stop.TargetId);
                return seaport != null ? FindNearestRoadNodeCached(seaport.GridX, seaport.GridY) : null;
            case ScheduleStopKind.RegionalLandmark:
                var landmark = World.Landmarks.FirstOrDefault(l => l.Id == stop.TargetId);
                return landmark != null ? FindNearestRoadNodeCached(landmark.GridX, landmark.GridY) : null;
            case ScheduleStopKind.BusTerminal:
                return BusTerminals.FirstOrDefault(b => b.Id == stop.TargetId)?.AccessNode;
            default:
                return null;
        }
    }

    public string GetStopDisplayName(ScheduleStop stop)
    {
        return stop.Kind switch
        {
            ScheduleStopKind.Industry => World.Industries.FirstOrDefault(i => i.Id == stop.TargetId)?.Name ?? $"Indústria #{stop.TargetId}",
            ScheduleStopKind.City => World.Cities.FirstOrDefault(c => c.Id == stop.TargetId)?.Name ?? $"Cidade #{stop.TargetId}",
            ScheduleStopKind.LogisticsHub => LogisticsHubs.FirstOrDefault(h => h.Id == stop.TargetId)?.Name ?? $"Hub #{stop.TargetId}",
            ScheduleStopKind.Garage => Garages.FirstOrDefault(g => g.Id == stop.TargetId)?.Name ?? $"Garagem #{stop.TargetId}",
            ScheduleStopKind.Seaport => World.Seaports.FirstOrDefault(s => s.Id == stop.TargetId)?.Name ?? $"Porto #{stop.TargetId}",
            ScheduleStopKind.RegionalLandmark => World.Landmarks.FirstOrDefault(l => l.Id == stop.TargetId)?.Name ?? $"Mega-Obra #{stop.TargetId}",
            ScheduleStopKind.BusTerminal => BusTerminals.FirstOrDefault(b => b.Id == stop.TargetId)?.Name ?? $"Rodoviária #{stop.TargetId}",
            _ => $"Parada #{stop.TargetId}"
        };
    }

    /// <summary>
    /// Compra um caminhão novo e o posiciona na garagem selecionada.
    /// </summary>
    public Truck? BuyTruck(Garage garage, TruckSpecification spec)
    {
        if (!Research.IsTruckUnlocked(spec, Player.Level))
            return null;

        if (Player.Balance < spec.PurchasePrice)
            return null;

        if (garage.IsFull)
            return null;

        Player.Balance -= spec.PurchasePrice;
        Financials.RecordCapEx(spec.PurchasePrice);
        var truck = new Truck(_nextTruckId++, $"{spec.Name} #{_nextTruckId}", spec)
        {
            LastNode = garage.AccessNode
        };
        garage.AddTruck(truck);
        ActiveTrucks.Add(truck);
        return truck;
    }

    /// <summary>
    /// Copia a agenda (linha compartilhada, multi-paradas e configuração de pit-stop) de um caminhão para outro.
    /// </summary>
    public void CopyTruckSchedule(Truck source, Truck target)
    {
        target.AssignedLineId = source.AssignedLineId;
        target.AutoPitStopEnabled = source.AutoPitStopEnabled;
        target.AssignedOriginIndustry = source.AssignedOriginIndustry;
        target.AssignedTargetIndustry = source.AssignedTargetIndustry;
        target.AssignedTargetCity = source.AssignedTargetCity;

        if (source.Schedule.Count > 0)
        {
            target.SetSchedule(source.Schedule);
            target.HasManualSchedule = source.HasManualSchedule;
        }
        else
        {
            target.ClearSchedule();
            target.HasManualSchedule = source.HasManualSchedule;
        }
    }

    public LogisticsLine? GetLogisticsLine(int? lineId)
        => lineId.HasValue ? LogisticsLines.FirstOrDefault(l => l.Id == lineId.Value) : null;

    /// <summary>
    /// Cria uma nova Linha Logística Compartilhada nomeada com a sequência de paradas fornecida.
    /// </summary>
    public LogisticsLine CreateLogisticsLine(string name, IEnumerable<ScheduleStop> stops, bool autoPitStopEnabled = true)
    {
        var line = new LogisticsLine(_nextLineId++, name, autoPitStopEnabled);
        line.SetStops(stops);
        LogisticsLines.Add(line);
        OnLogisticsLineUpdated?.Invoke(line);
        return line;
    }

    /// <summary>
    /// Atualiza nome/paradas de uma Linha Logística e propaga automaticamente para todos os caminhões vinculados.
    /// </summary>
    public bool UpdateLogisticsLine(int lineId, string? newName, IEnumerable<ScheduleStop> stops, bool? autoPitStopEnabled = null)
    {
        var line = GetLogisticsLine(lineId);
        if (line == null) return false;

        if (!string.IsNullOrWhiteSpace(newName))
            line.Name = newName.Trim();
        if (autoPitStopEnabled.HasValue)
            line.AutoPitStopEnabled = autoPitStopEnabled.Value;
        line.SetStops(stops);

        foreach (var truck in ActiveTrucks)
        {
            if (truck.AssignedLineId == line.Id)
            {
                ApplyLogisticsLineToTruck(truck, line);
            }
        }

        OnLogisticsLineUpdated?.Invoke(line);
        return true;
    }

    /// <summary>
    /// Vincula um caminhão a uma Linha Logística Compartilhada e sincroniza sua agenda imediatamente.
    /// </summary>
    public void AssignTruckToLogisticsLine(Truck truck, LogisticsLine line)
    {
        truck.AssignedLineId = line.Id;
        ApplyLogisticsLineToTruck(truck, line);
    }

    /// <summary>
    /// Desvincula um caminhão de sua Linha Logística Compartilhada mantendo a agenda atual ou livre.
    /// </summary>
    public void DetachTruckFromLogisticsLine(Truck truck)
    {
        truck.AssignedLineId = null;
    }

    /// <summary>
    /// Remove uma Linha Logística Compartilhada e desvincula seus veículos.
    /// </summary>
    public bool DeleteLogisticsLine(int lineId)
    {
        var line = GetLogisticsLine(lineId);
        if (line == null) return false;

        foreach (var truck in ActiveTrucks)
        {
            if (truck.AssignedLineId == lineId)
                truck.AssignedLineId = null;
        }
        LogisticsLines.Remove(line);
        return true;
    }

    /// <summary>
    /// Retorna a pintura efetiva de um caminhão (priorizando a pintura personalizada da sua Linha Logística, se houver).
    /// </summary>
    public FleetLivery GetEffectiveLivery(Truck truck)
    {
        if (truck.AssignedLineId is int lineId)
        {
            var line = GetLogisticsLine(lineId);
            if (line?.CustomLivery is { IsCustomized: true } lineLivery)
                return lineLivery;
        }
        return Player.CompanyLivery;
    }

    /// <summary>
    /// Atualiza o nome da transportadora e a identidade visual padrão da frota.
    /// </summary>
    public void UpdateCompanyLivery(string? companyName, FleetLivery livery)
    {
        if (!string.IsNullOrWhiteSpace(companyName))
            Player.CompanyName = companyName.Trim();
        Player.CompanyLivery = livery.Clone();
    }

    /// <summary>
    /// Define ou remove uma pintura exclusiva para uma Linha Logística Compartilhada.
    /// </summary>
    public bool UpdateLineLivery(int lineId, FleetLivery? customLivery)
    {
        var line = GetLogisticsLine(lineId);
        if (line == null) return false;
        line.CustomLivery = customLivery?.Clone();
        OnLogisticsLineUpdated?.Invoke(line);
        return true;
    }

    public bool UpdateLineAutomation(
        int lineId,
        bool headwayEnabled,
        float minHeadwayHours,
        bool autoReplaceEnabled,
        float minCondition = 25f,
        float maxMileageKm = 0f,
        string? targetSpecName = null)
    {
        var line = GetLogisticsLine(lineId);
        if (line == null) return false;
        line.HeadwayControlEnabled = headwayEnabled;
        line.MinHeadwayHours = Math.Clamp(minHeadwayHours, 1f, 24f);
        line.AutoReplaceEnabled = autoReplaceEnabled;
        line.AutoReplaceMinCondition = Math.Clamp(minCondition, 0f, 95f);
        line.AutoReplaceMaxMileageKm = Math.Max(0f, maxMileageKm);
        line.AutoReplaceTargetSpecName = string.IsNullOrWhiteSpace(targetSpecName) ? null : targetSpecName.Trim();
        OnLogisticsLineUpdated?.Invoke(line);
        return true;
    }

    public bool UpdateGarageAutoReplace(
        int garageId,
        bool enabled,
        float minCondition = 25f,
        float maxMileageKm = 0f,
        string? targetSpecName = null)
    {
        var garage = Garages.FirstOrDefault(g => g.Id == garageId);
        if (garage == null) return false;
        garage.AutoReplaceEnabled = enabled;
        garage.AutoReplaceMinCondition = Math.Clamp(minCondition, 0f, 95f);
        garage.AutoReplaceMaxMileageKm = Math.Max(0f, maxMileageKm);
        garage.AutoReplaceTargetSpecName = string.IsNullOrWhiteSpace(targetSpecName) ? null : targetSpecName.Trim();
        return true;
    }

    private void ApplyLogisticsLineToTruck(Truck truck, LogisticsLine line)
    {
        truck.AutoPitStopEnabled = line.AutoPitStopEnabled;
        if (line.Stops.Count > 0)
        {
            truck.SetSchedule(line.Stops);
            var firstLoad = line.Stops.FirstOrDefault(s => s.Action == ScheduleAction.Load && s.Kind == ScheduleStopKind.Industry);
            var firstUnloadInd = line.Stops.FirstOrDefault(s => s.Action == ScheduleAction.Unload && s.Kind == ScheduleStopKind.Industry);
            var firstUnloadCity = line.Stops.FirstOrDefault(s => s.Action == ScheduleAction.Unload && s.Kind == ScheduleStopKind.City);

            truck.AssignedOriginIndustry = firstLoad != null ? World.Industries.FirstOrDefault(i => i.Id == firstLoad.TargetId) : null;
            truck.AssignedTargetIndustry = firstUnloadInd != null ? World.Industries.FirstOrDefault(i => i.Id == firstUnloadInd.TargetId) : null;
            truck.AssignedTargetCity = firstUnloadCity != null ? World.Cities.FirstOrDefault(c => c.Id == firstUnloadCity.TargetId) : null;

            if (truck.CurrentEdge == null && (truck.Status == TruckStatus.Idle || truck.Status == TruckStatus.ParkedInGarage || truck.Status == TruckStatus.WaitingForCargo))
            {
                truck.Status = TruckStatus.Idle;
                ProcessScheduledTrucks();
            }
        }
        else
        {
            truck.ClearSchedule();
        }
    }

    /// <summary>
    /// Moderniza/substitui um caminhão existente por outro modelo: vende o antigo por 60% do valor original
    /// e transfere automaticamente sua Linha Logística e agenda para o novo veículo.
    /// </summary>
    public bool TryUpgradeTruckModel(Truck oldTruck, TruckSpecification newSpec, out Truck? newTruck, out string message)
    {
        newTruck = null;
        int truckIndex = ActiveTrucks.IndexOf(oldTruck);
        if (truckIndex < 0)
        {
            message = "⚠️ Veículo selecionado não encontrado na frota ativa.";
            return false;
        }

        if (oldTruck.Spec.Name == newSpec.Name)
        {
            message = $"ℹ️ {oldTruck.Name} já utiliza o modelo {newSpec.Name}.";
            return false;
        }

        if (!Research.IsTruckUnlocked(newSpec, Player.Level))
        {
            message = $"🔒 Modelo {newSpec.Name} requer Nível {newSpec.RequiredPlayerLevel} (ou pesquisa desbloqueada).";
            return false;
        }

        long tradeInValue = (long)Math.Round(oldTruck.Spec.PurchasePrice * 0.60);
        long netCost = newSpec.PurchasePrice - tradeInValue;
        if (netCost > 0 && Player.Balance < netCost)
        {
            message = $"❌ Saldo insuficiente para modernizar (custo líquido R$ {netCost:N0} após troca de R$ {tradeInValue:N0}).";
            return false;
        }

        Player.Balance += tradeInValue - newSpec.PurchasePrice;
        if (netCost > 0)
            Financials.RecordCapEx(netCost);

        newTruck = ReplaceTruckInFleet(oldTruck, newSpec, truckIndex);

        if (newTruck.HasManualSchedule && newTruck.Schedule.Count > 0)
        {
            ProcessScheduledTrucks();
        }

        string costSummary = netCost >= 0
            ? $"-R$ {netCost:N0} líquido (revenda R$ {tradeInValue:N0})"
            : $"+R$ {-netCost:N0} troco (revenda R$ {tradeInValue:N0})";
        message = $"🔄 Frota modernizada: {oldTruck.Name} → {newTruck.Name} ({costSummary})!";
        return true;
    }

    private Truck ReplaceTruckInFleet(Truck oldTruck, TruckSpecification newSpec, int truckIndex)
    {
        var homeGarage = Garages.FirstOrDefault(g => g.StationedTrucks.Contains(oldTruck));
        if (homeGarage != null)
            homeGarage.StationedTrucks.Remove(oldTruck);

        if (oldTruck.CurrentEdge != null)
        {
            oldTruck.CurrentEdge.ActiveTrucksCount = Math.Max(0, oldTruck.CurrentEdge.ActiveTrucksCount - 1);
            oldTruck.CurrentEdge = null;
        }
        oldTruck.RemainingPath.Clear();

        RoadNode? spawnNode = oldTruck.LastNode
            ?? homeGarage?.AccessNode
            ?? Garages.FirstOrDefault()?.AccessNode
            ?? World.RoadNodes.FirstOrDefault();

        var newTruck = new Truck(_nextTruckId++, $"{newSpec.Name} #{_nextTruckId}", newSpec)
        {
            LastNode = spawnNode,
            Status = TruckStatus.Idle
        };

        CopyTruckSchedule(oldTruck, newTruck);
        if (newTruck.Schedule.Count > 0)
            newTruck.CurrentScheduleIndex = Math.Clamp(oldTruck.CurrentScheduleIndex, 0, newTruck.Schedule.Count - 1);

        if (oldTruck.LoadedCargoAmount > 0 && oldTruck.LoadedCargoType != ResourceType.None)
        {
            newTruck.LoadedCargoType = oldTruck.LoadedCargoType;
            newTruck.LoadedCargoAmount = Math.Min(oldTruck.LoadedCargoAmount, newTruck.Spec.Capacity);
            newTruck.LoadedFromHub = oldTruck.LoadedFromHub;
        }

        if (homeGarage != null)
            homeGarage.AddTruck(newTruck);
        else
            Garages.FirstOrDefault(g => !g.IsFull)?.AddTruck(newTruck);

        ActiveTrucks[truckIndex] = newTruck;
        Milestones.RetiredFleetDistanceKm += oldTruck.TotalDistanceTravelledKm;
        return newTruck;
    }

    /// <summary>
    /// Demole estrada, garagem, hub logístico ou pedágio em (x, y), reembolsando 40% do valor investido
    /// e invalidando imediatamente o cache de rotas A*.
    /// </summary>
    public bool TryDemolishAt(int x, int y, out DemolishResult result, out string message)
    {
        result = default;
        if (!IsTileInBounds(x, y))
        {
            message = "⚠️ Coordenada fora dos limites do mapa.";
            return false;
        }

        var toll = TollStations.FirstOrDefault(t => t.GridX == x && t.GridY == y);
        if (toll != null)
        {
            long refund = (long)Math.Round(TollStation.ConstructionCost * 0.40);
            TollStations.Remove(toll);
            RebuildTollLookupCache();
            Player.Balance += refund;
            result = new DemolishResult(DemolishTargetKind.TollStation, x, y, toll.Id, RoadStructureType.Ground, refund);
            message = $"🚜 {toll.Name} demolido (+R$ {refund:N0} reembolso 40%).";
            return true;
        }

        var garage = Garages.FirstOrDefault(g => g.GridX == x && g.GridY == y);
        if (garage != null)
        {
            if (garage.StationedTrucks.Count > 0)
            {
                var altGarages = Garages.Where(g => g.Id != garage.Id).ToList();
                int freeSlots = altGarages.Sum(g => Math.Max(0, g.MaxCapacity - g.StationedTrucks.Count));
                if (freeSlots < garage.StationedTrucks.Count)
                {
                    message = $"⚠️ {garage.Name} abriga {garage.StationedTrucks.Count} veículo(s) e não há vagas em outras garagens.";
                    return false;
                }

                foreach (var stationed in garage.StationedTrucks.ToList())
                {
                    var destGarage = altGarages.First(g => !g.IsFull);
                    destGarage.AddTruck(stationed);
                }
                garage.StationedTrucks.Clear();
            }

            long refund = (long)Math.Round(8000 * 0.40);
            World.Grid[x, y].OccupantId = 0;
            Garages.Remove(garage);
            Player.Balance += refund;
            result = new DemolishResult(DemolishTargetKind.Garage, x, y, garage.Id, RoadStructureType.Ground, refund);
            message = $"🚜 {garage.Name} demolida (+R$ {refund:N0} reembolso 40%).";
            return true;
        }

        var hub = LogisticsHubs.FirstOrDefault(h => h.GridX == x && h.GridY == y);
        if (hub != null)
        {
            long refund = (long)Math.Round(12000 * 0.40);
            World.Grid[x, y].OccupantId = 0;
            LogisticsHubs.Remove(hub);
            Player.Balance += refund;
            result = new DemolishResult(DemolishTargetKind.LogisticsHub, x, y, hub.Id, RoadStructureType.Ground, refund);
            message = $"🚜 {hub.Name} demolido (+R$ {refund:N0} reembolso 40%).";
            return true;
        }

        var terminal = BusTerminals.FirstOrDefault(b => b.GridX == x && b.GridY == y);
        if (terminal != null)
        {
            long refund = (long)Math.Round(BusTerminal.ConstructionCost * 0.40);
            World.Grid[x, y].OccupantId = 0;
            BusTerminals.Remove(terminal);
            Player.Balance += refund;
            result = new DemolishResult(DemolishTargetKind.BusTerminal, x, y, terminal.Id, RoadStructureType.Ground, refund);
            message = $"🚜 {terminal.Name} demolida (+R$ {refund:N0} reembolso 40%).";
            return true;
        }

        ref var cell = ref World.Grid[x, y];
        if (!cell.HasRoad)
        {
            message = "⚠️ Selecione uma estrada, garagem, hub ou pedágio para demolir (cidades e indústrias são protegidas).";
            return false;
        }

        RoadStructureType removedStructure = cell.StructureType;
        long invested = removedStructure.GetBaseConstructionCost();
        if (cell.RoadLevel > RoadTier.Dirt)
            invested += RoadTier.Dirt.GetUpgradeCost(cell.RoadLevel);

        var nodeAtTile = GetRoadNodeAt(x, y);
        if (nodeAtTile != null && nodeAtTile.HasTrafficControl)
            invested += TrafficControlUpgradeCost;

        long roadRefund = Math.Max(16L, (long)Math.Round(invested * 0.40));
        cell.RoadLevel = RoadTier.None;

        var removedEdges = GetEdgesTouchingTile(x, y).ToList();
        var removedEdgeIds = new HashSet<int>();

        foreach (var edge in removedEdges)
        {
            removedEdgeIds.Add(edge.Id);
            edge.FromNode.OutgoingEdges.Remove(edge);
            World.RoadEdges.Remove(edge);
            if (edge.Id == BlockedLandslideEdgeId)
                BlockedLandslideEdgeId = 0;
        }

        if (nodeAtTile != null)
            World.RoadNodes.Remove(nodeAtTile);

        _cachedRoadNodeCount = -1;
        _cachedRoadEdgeCount = -1;
        _nearestNodeCache.Clear();
        Pathfinder.InvalidateCache();

        for (int i = TrafficVehicles.Count - 1; i >= 0; i--)
        {
            var npc = TrafficVehicles[i];
            if ((npc.CurrentEdge != null && removedEdgeIds.Contains(npc.CurrentEdge.Id)) ||
                npc.RemainingPath.Any(e => removedEdgeIds.Contains(e.Id)))
            {
                npc.ReleaseCurrentEdgeOccupancy();
                TrafficVehicles.RemoveAt(i);
            }
        }

        foreach (var truck in ActiveTrucks)
        {
            if (truck.LastNode == nodeAtTile)
                truck.LastNode = FindNearestRoadNode(x, y) ?? Garages.FirstOrDefault()?.AccessNode;

            if (truck.CurrentEdge != null && removedEdgeIds.Contains(truck.CurrentEdge.Id))
            {
                truck.CurrentEdge.ActiveTrucksCount = Math.Max(0, truck.CurrentEdge.ActiveTrucksCount - 1);
                truck.CurrentEdge = null;
                truck.RemainingPath.Clear();
                truck.ClearCurrentTripEdges();
                truck.Status = TruckStatus.Idle;
                continue;
            }

            if (truck.RemainingPath.Count > 0 && truck.RemainingPath.Any(e => removedEdgeIds.Contains(e.Id)))
            {
                RoadNode? rerouteFrom = truck.CurrentEdge?.ToNode ?? truck.LastNode;
                List<RoadEdge>? altPath = (rerouteFrom != null && truck.CurrentDestinationNode != null && truck.CurrentDestinationNode != nodeAtTile)
                    ? Pathfinder.FindFastestRoute(rerouteFrom, truck.CurrentDestinationNode, CurrentWeather, truck.Spec.RequiresHighwayOrDualCarriage, IsWinterBlizzardActive)
                    : null;

                truck.RemainingPath.Clear();
                if (altPath != null && altPath.Count > 0)
                {
                    foreach (var altEdge in altPath)
                        truck.RemainingPath.Enqueue(altEdge);
                }
                else
                {
                    truck.CurrentDestinationNode = null;
                    truck.Status = TruckStatus.Idle;
                }
            }
        }

        Player.Balance += roadRefund;
        result = new DemolishResult(DemolishTargetKind.Road, x, y, nodeAtTile?.Id ?? 0, removedStructure, roadRefund);
        message = $"🚜 Trecho ({x}, {y}) demolido (+R$ {roadRefund:N0} reembolso 40%).";
        return true;
    }

    /// <summary>
    /// Clona um caminhão existente (mesmo modelo e mesma agenda/linha) em uma garagem disponível.
    /// </summary>
    public Truck? CloneTruck(Truck source, Garage? preferredGarage = null)
    {
        var sourceGarage = preferredGarage ?? Garages.FirstOrDefault(g => g.StationedTrucks.Contains(source));
        Garage? targetGarage = (sourceGarage != null && !sourceGarage.IsFull)
            ? sourceGarage
            : Garages.FirstOrDefault(g => !g.IsFull);

        if (targetGarage == null)
            return null;

        var clone = BuyTruck(targetGarage, source.Spec);
        if (clone == null)
            return null;

        CopyTruckSchedule(source, clone);
        return clone;
    }

    public bool TryUpgradeIndustry(Industry ind, out string message)
    {
        if (!ind.CanUpgrade)
        {
            message = $"🏭 {ind.Name} já atingiu o nível máximo de expansão (Nv.{ind.MaxUpgradeLevel}).";
            return false;
        }

        int cost = ind.GetNextUpgradeCost();
        if (Player.Balance < cost)
        {
            message = $"⚠️ Saldo insuficiente para expandir {ind.Name} (R$ {cost:N0}).";
            return false;
        }

        Player.Balance -= cost;
        Financials.RecordCapEx(cost);
        ind.ApplyPlantUpgrade();
        OnIndustryUpgraded?.Invoke(ind);

        message = $"🏭 {ind.Name} expandida para Nv.{ind.UpgradeLevel}! (Prod: {ind.ProductionRatePerDay}/dia | Estoque: {ind.MaxStock} un.)";
        return true;
    }

    /// <summary>
    /// Despacha um caminhão até um nó de destino usando o A* no grafo de estradas.
    /// </summary>
    public bool DispatchTruck(Truck truck, RoadNode destinationNode)
    {
        RoadNode? originNode = GetTruckCurrentNode(truck);
        if (originNode == null)
            return false;

        truck.LastNode = originNode;
        var path = Pathfinder.FindFastestRoute(
            originNode,
            destinationNode,
            CurrentWeather,
            preferHighways: truck.Spec.RequiresHighwayOrDualCarriage,
            winterBlizzard: IsWinterBlizzardActive);

        if (path == null || path.Count == 0)
            return false;

        truck.CurrentDestinationNode = destinationNode;
        truck.AssignPath(path);
        truck.Status = truck.LoadedCargoAmount > 0 
            ? TruckStatus.TravelingToDelivery 
            : TruckStatus.TravelingToPickup;

        return true;
    }

    /// <summary>
    /// Executa um passo de tempo na simulação (ex: 1 tick = 0.25h a 1h de simulação).
    /// </summary>
    public void Tick(float deltaHours = 0.25f)
    {
        int prevMonth = Calendar.Month;
        int prevYear = Calendar.Year;
        CurrentTick++;
        Calendar.AdvanceHours(deltaHours);

        // Cobrança de juros bancários, subsídios municipais, conservação automática e DRE na virada de mês
        if (Calendar.Month != prevMonth)
        {
            if (Player.LoanDebt > 0)
            {
                long monthlyInterest = Math.Max(10, (long)(Player.LoanDebt * 0.02f));
                Player.Balance -= monthlyInterest;
                Financials.RecordLoanInterest(monthlyInterest);
            }

            PayMunicipalSubsidies();
            if (AutoRoadMaintenanceEnabled)
            {
                RunAutoRoadMaintenance(out _);
            }
            DecayCorridorRevenues();

            var closed = Financials.CloseMonth(prevMonth, prevYear, Player.Balance, Player.LoanDebt);
            OnMonthlyFinancialClosed?.Invoke(closed);
        }

        // Atualiza cronômetro de evento sazonal / climático extremo ativo
        bool seasonalEventExpiredThisTick = false;
        if (ActiveSeasonalEvent != SeasonalEventType.None)
        {
            SeasonalEventRemainingHours -= deltaHours;
            if (SeasonalEventRemainingHours <= 0f)
            {
                ClearActiveSeasonalEvent();
                seasonalEventExpiredThisTick = true;
            }
        }

        // Atualiza cronômetros de contratos expressos, linhas (Headway) e ciclos de navio nos Portos
        foreach (var line in LogisticsLines)
        {
            line.HoursSinceLastDispatch = Math.Min(10_000f, line.HoursSinceLastDispatch + deltaHours);
        }
        foreach (var contract in ActiveContracts)
        {
            contract.AdvanceTime(deltaHours);
        }
        foreach (var seaport in World.Seaports)
        {
            if (seaport.AdvanceTime(deltaHours, out int sailedUnits))
            {
                if (!seaport.IsShipDocked && seaport.PartnerSeaportId.HasValue)
                {
                    var partner = World.Seaports.FirstOrDefault(s => s.Id == seaport.PartnerSeaportId.Value);
                    var manifest = seaport.ConsumeOutboundManifest();
                    if (partner != null && manifest.Count > 0)
                        partner.QueueIncomingSeaCargo(manifest);
                }
                else if (seaport.IsShipDocked)
                {
                    seaport.UnloadArrivedSeaCargoToYard();
                }

                OnSeaportShipStatusChanged?.Invoke(seaport, seaport.IsShipDocked, sailedUnits);
            }
        }

        // Atualiza Pesquisa & Tecnologia em andamento
        var completedTech = Research.Advance(deltaHours);
        if (completedTech != null)
        {
            OnResearchCompleted?.Invoke(completedTech);
        }

        // 1. Ciclo Diário: Indústrias conectadas transferem insumos via esteira, produzem, cidades consomem e clima alterna
        if (CurrentTick % 24 == 0)
        {
            TransferConnectedIndustrialConveyors();

            foreach (var ind in World.Industries)
            {
                int bonus = GetIndustryConnections(ind).DailyProductionBonus;
                if (LandmarkProductionMultiplier > 1.01f)
                {
                    bonus += Math.Max(1, (int)Math.Round(ind.ProductionRatePerDay * (LandmarkProductionMultiplier - 1f)));
                }
                if (ActiveSeasonalEvent == SeasonalEventType.BumperHarvest && ind.OutputResource == ResourceType.Grain)
                {
                    bonus += ind.ProductionRatePerDay;
                }
                ind.Produce(bonus);
            }

            ProcessCitiesDailyCycle();
            CollectDailyPassiveTolls();
            ProcessRivalsDailyCycle();
            UpdateDynamicWeather();
            if (!seasonalEventExpiredThisTick)
            {
                UpdateSeasonalEvents();
            }
            EvaluateMilestones();
        }

        // 2. Simula movimento dos caminhões do jogador
        ProcessTrucksMovement(deltaHours);

        // 2b. Simula tráfego NPC vivo (frota rival pagando pedágio real + carros/ônibus entre cidades)
        ProcessNpcTraffic(deltaHours);

        // 2c. Substituição automática de veículos desgastados ou antigos (Garagem / Linha)
        ProcessFleetAutoReplace();

        // 3. Executa agendas multi-paradas, espera condicional e pit-stops automáticos
        ProcessScheduledTrucks();
    }

    private void UpdateDynamicWeather()
    {
        double roll = _rng.NextDouble();
        WeatherCondition nextWeather = roll < 0.72
            ? WeatherCondition.Clear
            : (roll < 0.93 ? WeatherCondition.Rain : WeatherCondition.HeavyStorm);

        if (nextWeather != CurrentWeather)
        {
            CurrentWeather = nextWeather;
            OnWeatherChanged?.Invoke(CurrentWeather);
        }
    }

    private void ProcessTrucksMovement(float deltaHours)
    {
        foreach (var truck in ActiveTrucks)
        {
            if (truck.Status == TruckStatus.WaitingForCargo)
            {
                truck.CurrentStopWaitHours += deltaHours;
                continue;
            }

            if (truck.Status == TruckStatus.TravelingToPickup ||
                truck.Status == TruckStatus.TravelingToDelivery ||
                truck.Status == TruckStatus.TravelingToPitStop)
            {
                RefreshTruckSpecialConvoyState(truck);
                RoadEdge? prevEdge = truck.CurrentEdge;
                float prevDist = truck.TotalDistanceTravelledKm;
                TruckRank prevRank = truck.Rank;
                bool arrivedAtDestination = truck.Advance(
                    deltaHours,
                    CurrentWeather,
                    Research.FleetSpeedMultiplier,
                    IsWinterBlizzardActive,
                    Milestones.HillClimbBonusMultiplier);
                float deltaKm = truck.TotalDistanceTravelledKm - prevDist;
                if (truck.Rank > prevRank)
                    OnTruckRankUp?.Invoke(truck, truck.Rank);

                // Atualiza letreiro luminoso da balança 3D ao cruzar uma praça de pedágio
                if (prevEdge != null && truck.CurrentEdge != prevEdge && _tollByNodeId.TryGetValue(prevEdge.ToNode.Id, out var crossedStation))
                {
                    UpdatePlayerTruckWeighboard(crossedStation, truck);
                }

                // Desconta custo operacional proporcional à quilometragem percorrida (com -35% se houver Diesel regional/garagem,
                // ou sobretaxa de diesel escasso em viagens longas > 35km sem reserva próxima)
                if (deltaKm > 0f && Player.Balance > 1000)
                {
                    bool hasRegionalFuel = ConsumeFleetFuel(truck, deltaKm);
                    float regionFactor = hasRegionalFuel
                        ? 0.65f
                        : (truck.CurrentTripDistanceKm > 70f ? 1.50f : (truck.CurrentTripDistanceKm > 35f ? 1.35f : 1.0f));
                    float fuelCostMultiplier = regionFactor * truck.FuelCostMultiplier * LandmarkFuelMultiplier * Milestones.FuelCostBonusMultiplier;
                    long opCost = (long)(deltaKm * truck.Spec.RunningCostPerKm * 0.2f * fuelCostMultiplier);
                    long paid = Math.Min(Player.Balance, opCost);
                    Player.Balance -= paid;
                    Financials.RecordFleetMaintenance(paid);
                }

                if (arrivedAtDestination)
                {
                    HandleTruckArrival(truck);
                }
                else if (BlockedLandslideEdgeId != 0 && HasBlockedEdgeInRemainingPath(truck))
                {
                    TryRerouteAroundBlockedRoad(truck);
                }
                else if (Research.IsSmartRoutingEnabled && CurrentTick % 12 == 0)
                {
                    TrySmartRerouteTruck(truck);
                }
            }
        }
    }

    public const int RegionalFuelRadiusTiles = 22;

    public bool HasRegionalFuelCoverage(int gridX, int gridY)
    {
        return Garages.Any(g => g.FuelStock > 0 && Math.Abs(g.GridX - gridX) + Math.Abs(g.GridY - gridY) <= RegionalFuelRadiusTiles)
            || LogisticsHubs.Any(h => h.FuelStock > 0 && Math.Abs(h.GridX - gridX) + Math.Abs(h.GridY - gridY) <= RegionalFuelRadiusTiles);
    }

    private bool ConsumeFleetFuel(Truck truck, float deltaKm)
    {
        foreach (var garage in Garages)
        {
            if (garage.FuelStock > 0 && garage.StationedTrucks.Contains(truck))
                return garage.TryConsumeFuelForTravel(deltaKm);
        }

        RoadNode? refNode = truck.CurrentEdge?.ToNode ?? truck.LastNode ?? Garages.FirstOrDefault()?.AccessNode;
        if (refNode != null)
        {
            foreach (var garage in Garages)
            {
                if (garage.FuelStock > 0 && Math.Abs(garage.GridX - refNode.GridX) + Math.Abs(garage.GridY - refNode.GridY) <= RegionalFuelRadiusTiles)
                    return garage.TryConsumeFuelForTravel(deltaKm);
            }

            foreach (var hub in LogisticsHubs)
            {
                if (hub.FuelStock > 0 && Math.Abs(hub.GridX - refNode.GridX) + Math.Abs(hub.GridY - refNode.GridY) <= RegionalFuelRadiusTiles)
                    return hub.TryConsumeFuelForTravel(deltaKm);
            }
        }

        return false;
    }

    private void TrySmartRerouteTruck(Truck truck)
    {
        if (truck.CurrentEdge == null || truck.CurrentDestinationNode == null || truck.RemainingPath.Count <= 2)
            return;

        if (truck.CurrentEdge.ActiveTrucksCount < 3 && CurrentWeather == WeatherCondition.Clear)
            return;

        float currentRemainingTime = RoadPathfinder.EstimatePathTravelTimeHours(truck.RemainingPath, CurrentWeather, IsWinterBlizzardActive);
        var altPath = Pathfinder.FindFastestRoute(
            truck.CurrentEdge.ToNode,
            truck.CurrentDestinationNode,
            CurrentWeather,
            preferHighways: truck.Spec.RequiresHighwayOrDualCarriage,
            winterBlizzard: IsWinterBlizzardActive,
            bypassCache: truck.CurrentEdge.ActiveTrucksCount >= 3);

        if (altPath == null || altPath.Count == 0)
            return;

        float altTime = RoadPathfinder.EstimatePathTravelTimeHours(altPath, CurrentWeather, IsWinterBlizzardActive);
        if (altTime < currentRemainingTime * 0.85f)
        {
            truck.RemainingPath.Clear();
            foreach (var edge in altPath)
                truck.RemainingPath.Enqueue(edge);
        }
    }

    private void HandleTruckArrival(Truck truck)
    {
        if (truck.Status == TruckStatus.TravelingToPitStop)
        {
            CompletePitStop(truck);
            return;
        }

        if (truck.HasManualSchedule && truck.Schedule.Count > 0)
        {
            ExecuteScheduleStopArrival(truck);
            return;
        }

        if (truck.Status == TruckStatus.TravelingToDelivery && truck.LoadedCargoAmount > 0)
        {
            // Estágio 1: Entrega de matéria-prima em Indústria Processadora
            if (truck.AssignedTargetIndustry != null)
            {
                var targetInd = truck.AssignedTargetIndustry;
                ResourceType deliveredType = truck.LoadedCargoType;
                int accepted = targetInd.ReceiveInputCargo(deliveredType, truck.LoadedCargoAmount);

                if (accepted > 0)
                {
                    float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
                    bool hasNearbyHub = LogisticsHubs.Any(h =>
                        Math.Abs(h.GridX - targetInd.GridX) + Math.Abs(h.GridY - targetInd.GridY) <= 15);
                    float hubMultiplier = hasNearbyHub ? 1.20f : 1.0f;

                    long freightReward = (long)(accepted * 15f * distanceFactor * hubMultiplier * Research.HeavyFreightBonusMultiplier);
                    int xpReward = accepted * 7;

                    if (hasNearbyHub)
                        truck.Repair();

                    FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, freightReward, xpReward);
                }

                truck.LoadedCargoAmount = 0;
                truck.LoadedCargoType = ResourceType.None;
                truck.Status = TruckStatus.Idle;
                return;
            }

            // Estágio 2: Entrega de produto final no Armazém da Cidade
            City? targetCity = truck.AssignedTargetCity ?? World.Cities.FirstOrDefault();
            if (targetCity != null)
            {
                ResourceType deliveredType = truck.LoadedCargoType;
                int accepted = targetCity.Warehouse.ReceiveCargo(deliveredType, truck.LoadedCargoAmount);

                // Tarifa dinâmica: valor base + bônus de distância + bônus de escassez + bônus de Hub Logístico + bônus manufaturado
                float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
                int demand = targetCity.Warehouse.DailyDemand.GetValueOrDefault(deliveredType, 10);
                int stockBefore = Math.Max(0, targetCity.Warehouse.StoredStock.GetValueOrDefault(deliveredType, 0) - accepted);
                float scarcityMultiplier = stockBefore < demand ? 1.25f : 1.0f;

        bool isManufactured2 = deliveredType is ResourceType.Food or ResourceType.Goods or ResourceType.Steel or ResourceType.Fuel or ResourceType.Machinery;
        float manufacturedMultiplier = deliveredType == ResourceType.Machinery ? 1.60f : (isManufactured2 ? 1.35f : 1.0f);

                bool hasNearbyHub = LogisticsHubs.Any(h =>
                    Math.Abs(h.GridX - targetCity.CenterX) + Math.Abs(h.GridY - targetCity.CenterY) <= 15);
                float hubMultiplier = hasNearbyHub ? 1.20f : 1.0f;
                float reeferMultiplier = deliveredType == ResourceType.Food ? Research.FoodFreightBonusMultiplier : 1.0f;

                long freightReward = (long)(accepted * 18f * distanceFactor * scarcityMultiplier * hubMultiplier * manufacturedMultiplier * reeferMultiplier * Research.HeavyFreightBonusMultiplier);
                int xpReward = accepted * 8;

                // Se houver Hub Logístico próximo, realiza manutenção preventiva gratuita no caminhão
                if (hasNearbyHub)
                {
                    truck.Repair();
                }

                FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, freightReward, xpReward);
                RecordCityContractProgress(targetCity, deliveredType, accepted);

                truck.LoadedCargoAmount = 0;
                truck.LoadedCargoType = ResourceType.None;
                truck.Status = TruckStatus.Idle;
            }
        }
        else if (truck.Status == TruckStatus.TravelingToPickup && truck.AssignedOriginIndustry != null)
        {
            // Coleta automática na indústria de origem e parte para entrega
            var ind = truck.AssignedOriginIndustry;
            int loaded = ind.DispatchCargo(truck.Spec.Capacity);
            if (loaded > 0)
            {
                truck.LoadedCargoType = ind.OutputResource;
                truck.LoadedCargoAmount = loaded;
                truck.Status = TruckStatus.TravelingToDelivery;
            }
            else
            {
                truck.Status = TruckStatus.Idle;
            }
        }
        else
        {
            truck.Status = TruckStatus.Idle;
        }
    }

    /// <summary>
    /// Processa caminhões com agenda multi-paradas, espera condicional e desvios de pit-stop automático.
    /// </summary>
    public void ProcessScheduledTrucks()
    {
        foreach (var truck in ActiveTrucks)
        {
            if (truck.CurrentEdge != null)
                continue;

            // 1. Espera condicional de carga (50% ou 100%), tempo máximo de espera e controle de Headway
            if (truck.Status == TruckStatus.WaitingForCargo && truck.HasManualSchedule && truck.Schedule.Count > 0)
            {
                var waitStop = truck.GetCurrentStop();
                if (waitStop != null)
                {
                    if (waitStop.Action == ScheduleAction.Load)
                        TryLoadAtStop(truck, waitStop);

                    if (CanDepartScheduleStop(truck, waitStop))
                    {
                        truck.CurrentStopWaitHours = 0f;
                        truck.IsHeldForHeadway = false;
                        truck.AdvanceScheduleStop();
                        truck.Status = TruckStatus.Idle;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                {
                    truck.IsHeldForHeadway = false;
                    truck.CurrentStopWaitHours = 0f;
                    truck.Status = TruckStatus.Idle;
                }
            }

            if (truck.Status != TruckStatus.Idle && truck.Status != TruckStatus.ParkedInGarage)
                continue;

            // 2. Manutenção preditiva: desvia para Garagem ou Hub quando Condição < 35%
            bool isManaged = (truck.HasManualSchedule && truck.Schedule.Count > 0) ||
                             truck.AssignedOriginIndustry != null ||
                             truck.AssignedTargetCity != null ||
                             truck.AssignedTargetIndustry != null;

            if (isManaged && TryTriggerAutoPitStop(truck))
                continue;

            if (!truck.HasManualSchedule || truck.Schedule.Count == 0)
                continue;

            // 3. Despacha para a próxima parada da agenda
            var stop = truck.GetCurrentStop();
            if (stop == null) continue;

            RoadNode? currentNode = GetTruckCurrentNode(truck);
            RoadNode? targetNode = GetStopAccessNode(stop);
            if (currentNode == null || targetNode == null) continue;

            if (targetNode == currentNode)
            {
                ExecuteScheduleStopArrival(truck);
            }
            else if (DispatchTruck(truck, targetNode))
            {
                truck.Status = stop.Action switch
                {
                    ScheduleAction.Load => TruckStatus.TravelingToPickup,
                    ScheduleAction.Unload => TruckStatus.TravelingToDelivery,
                    ScheduleAction.PitStop => TruckStatus.TravelingToPitStop,
                    _ => TruckStatus.TravelingToPickup
                };
            }
        }
    }

    public bool TryTriggerAutoPitStop(Truck truck)
    {
        if (!truck.AutoPitStopEnabled || truck.IsDivertedForPitStop || truck.Condition >= 35f || truck.LoadedCargoAmount > 0)
            return false;

        RoadNode? currentNode = GetTruckCurrentNode(truck);
        if (currentNode == null)
            return false;

        (ScheduleStopKind Kind, int Id, RoadNode AccessNode, float Dist)? best = null;

        foreach (var hub in LogisticsHubs)
        {
            float d = MathF.Abs(hub.AccessNode.GridX - currentNode.GridX) + MathF.Abs(hub.AccessNode.GridY - currentNode.GridY);
            // Hubs fazem manutenção gratuita, então têm leve prioridade (-3 tiles)
            float score = d - 3f;
            if (best == null || score < best.Value.Dist)
                best = (ScheduleStopKind.LogisticsHub, hub.Id, hub.AccessNode, score);
        }

        foreach (var garage in Garages)
        {
            float d = MathF.Abs(garage.AccessNode.GridX - currentNode.GridX) + MathF.Abs(garage.AccessNode.GridY - currentNode.GridY);
            if (best == null || d < best.Value.Dist)
                best = (ScheduleStopKind.Garage, garage.Id, garage.AccessNode, d);
        }

        if (best == null)
            return false;

        truck.IsDivertedForPitStop = true;
        truck.PitStopTargetKind = best.Value.Kind;
        truck.PitStopTargetId = best.Value.Id;

        if (best.Value.AccessNode == currentNode)
        {
            CompletePitStop(truck);
            return true;
        }

        if (DispatchTruck(truck, best.Value.AccessNode))
        {
            truck.Status = TruckStatus.TravelingToPitStop;
            return true;
        }

        truck.IsDivertedForPitStop = false;
        truck.PitStopTargetKind = null;
        truck.PitStopTargetId = null;
        return false;
    }

    private void CompletePitStop(Truck truck)
    {
        string facilityName = "Oficina";
        long cost = 0;

        if (truck.PitStopTargetKind == ScheduleStopKind.LogisticsHub && truck.PitStopTargetId.HasValue)
        {
            var hub = LogisticsHubs.FirstOrDefault(h => h.Id == truck.PitStopTargetId.Value);
            facilityName = hub?.Name ?? "Hub Logístico";
            cost = 0; // Manutenção preventiva gratuita em Hubs
        }
        else
        {
            var garage = truck.PitStopTargetId.HasValue
                ? Garages.FirstOrDefault(g => g.Id == truck.PitStopTargetId.Value)
                : Garages.FirstOrDefault();
            facilityName = garage?.Name ?? "Garagem";
            long pitCost = (long)Math.Round(150 * LandmarkMaintenanceMultiplier);
            cost = Math.Min(Player.Balance, pitCost);
            if (cost > 0)
            {
                Player.Balance -= cost;
                Financials.RecordFleetMaintenance(cost);
            }
        }

        truck.Repair();
        truck.IsDivertedForPitStop = false;
        truck.PitStopTargetKind = null;
        truck.PitStopTargetId = null;
        truck.Status = TruckStatus.Idle;

        OnTruckPitStopCompleted?.Invoke(truck, facilityName, cost);
    }

    private void ExecuteScheduleStopArrival(Truck truck)
    {
        var stop = truck.GetCurrentStop();
        if (stop == null)
        {
            truck.Status = TruckStatus.Idle;
            return;
        }

        if (stop.Action == ScheduleAction.Load)
        {
            TryLoadAtStop(truck, stop);
            if (CanDepartScheduleStop(truck, stop))
            {
                truck.CurrentStopWaitHours = 0f;
                truck.IsHeldForHeadway = false;
                truck.AdvanceScheduleStop();
                truck.Status = TruckStatus.Idle;
            }
            else
            {
                truck.Status = TruckStatus.WaitingForCargo;
            }
            return;
        }

        if (stop.Action == ScheduleAction.Unload)
        {
            if (stop.Kind == ScheduleStopKind.BusTerminal && truck.Spec.IsPassengerBus)
            {
                var terminal = BusTerminals.FirstOrDefault(b => b.Id == stop.TargetId);
                if (terminal != null && truck.LoadedCargoAmount > 0)
                    DeliverPassengersToBusTerminal(truck, terminal);
            }
            else if (!truck.Spec.IsPassengerBus)
            {
                bool filterMatches = stop.CargoFilter == ResourceType.None || truck.LoadedCargoType == stop.CargoFilter;
                if (truck.LoadedCargoAmount > 0 && filterMatches)
                {
                    switch (stop.Kind)
                    {
                        case ScheduleStopKind.Industry:
                            var ind = World.Industries.FirstOrDefault(i => i.Id == stop.TargetId);
                            if (ind != null) DeliverCargoToIndustry(truck, ind);
                            break;
                        case ScheduleStopKind.City:
                            var city = World.Cities.FirstOrDefault(c => c.Id == stop.TargetId);
                            if (city != null) DeliverCargoToCity(truck, city);
                            break;
                        case ScheduleStopKind.LogisticsHub:
                            var hub = LogisticsHubs.FirstOrDefault(h => h.Id == stop.TargetId);
                            if (hub != null) DeliverCargoToHub(truck, hub);
                            break;
                        case ScheduleStopKind.Garage:
                            var garage = Garages.FirstOrDefault(g => g.Id == stop.TargetId);
                            if (garage != null) DeliverFuelToGarage(truck, garage);
                            break;
                        case ScheduleStopKind.Seaport:
                            var seaport = World.Seaports.FirstOrDefault(s => s.Id == stop.TargetId);
                            if (seaport != null) DeliverCargoToSeaport(truck, seaport);
                            break;
                        case ScheduleStopKind.RegionalLandmark:
                            var landmark = World.Landmarks.FirstOrDefault(l => l.Id == stop.TargetId);
                            if (landmark != null) DeliverCargoToLandmark(truck, landmark);
                            break;
                    }
                }
            }

            if (!CanDepartScheduleStop(truck, stop))
            {
                truck.Status = TruckStatus.WaitingForCargo;
                return;
            }

            truck.CurrentStopWaitHours = 0f;
            truck.IsHeldForHeadway = false;
            truck.AdvanceScheduleStop();
            truck.Status = TruckStatus.Idle;
            return;
        }

        if (stop.Action == ScheduleAction.PitStop)
        {
            truck.PitStopTargetKind = stop.Kind;
            truck.PitStopTargetId = stop.TargetId;
            CompletePitStop(truck);
            if (!CanDepartScheduleStop(truck, stop))
            {
                truck.Status = TruckStatus.WaitingForCargo;
                return;
            }
            truck.CurrentStopWaitHours = 0f;
            truck.IsHeldForHeadway = false;
            truck.AdvanceScheduleStop();
        }
    }

    private bool CanDepartScheduleStop(Truck truck, ScheduleStop stop)
    {
        if (stop.Action == ScheduleAction.Load)
        {
            int required = truck.GetRequiredCargoThreshold(stop.Condition);
            bool timedOut = stop.MaxWaitHours > 0f && truck.CurrentStopWaitHours >= stop.MaxWaitHours;
            bool cargoReady = stop.Condition == DispatchCondition.Immediate
                              || truck.LoadedCargoAmount >= required
                              || timedOut;
            if (!cargoReady)
            {
                truck.IsHeldForHeadway = false;
                return false;
            }
        }

        if (truck.CurrentScheduleIndex == 0 && truck.AssignedLineId is int lineId)
        {
            var line = GetLogisticsLine(lineId);
            if (line != null && line.HeadwayControlEnabled)
            {
                if (line.HoursSinceLastDispatch < line.MinHeadwayHours)
                {
                    truck.IsHeldForHeadway = true;
                    return false;
                }
                line.HoursSinceLastDispatch = 0f;
            }
        }

        truck.IsHeldForHeadway = false;
        return true;
    }

    private void ProcessFleetAutoReplace()
    {
        for (int i = 0; i < ActiveTrucks.Count; i++)
        {
            var truck = ActiveTrucks[i];
            if (truck.CurrentEdge != null || truck.LoadedCargoAmount > 0)
                continue;
            if (truck.Status != TruckStatus.Idle && truck.Status != TruckStatus.ParkedInGarage)
                continue;

            var line = GetLogisticsLine(truck.AssignedLineId);
            var garage = Garages.FirstOrDefault(g => g.StationedTrucks.Contains(truck));

            bool enabled = false;
            float minCond = 25f;
            float maxKm = 0f;
            string? targetSpecName = null;

            if (line != null && line.AutoReplaceEnabled)
            {
                enabled = true;
                minCond = line.AutoReplaceMinCondition;
                maxKm = line.AutoReplaceMaxMileageKm;
                targetSpecName = line.AutoReplaceTargetSpecName;
            }
            else if (garage != null && garage.AutoReplaceEnabled)
            {
                enabled = true;
                minCond = garage.AutoReplaceMinCondition;
                maxKm = garage.AutoReplaceMaxMileageKm;
                targetSpecName = garage.AutoReplaceTargetSpecName;
            }

            if (!enabled)
                continue;

            bool wearHit = minCond > 0f && truck.Condition <= minCond;
            bool mileageHit = maxKm > 0f && truck.TotalDistanceTravelledKm >= maxKm;
            if (!wearHit && !mileageHit)
                continue;

            var targetSpec = !string.IsNullOrWhiteSpace(targetSpecName)
                ? ProgressionManager.ResolveSpecByName(targetSpecName)
                : truck.Spec;

            if (!Research.IsTruckUnlocked(targetSpec, Player.Level))
                continue;

            long tradeIn = (long)Math.Round(truck.Spec.PurchasePrice * 0.60);
            long netCost = targetSpec.PurchasePrice - tradeIn;
            if (netCost > 0 && Player.Balance < netCost)
                continue;

            Player.Balance += tradeIn - targetSpec.PurchasePrice;
            if (netCost > 0)
                Financials.RecordCapEx(netCost);

            var newTruck = ReplaceTruckInFleet(truck, targetSpec, i);
            string reason = wearHit
                ? $"desgaste ({truck.Condition:F0}% <= {minCond:F0}%)"
                : $"quilometragem ({truck.TotalDistanceTravelledKm:F0} km >= {maxKm:F0} km)";
            OnTruckAutoReplaced?.Invoke(truck, newTruck, reason);
        }
    }

    private int TryLoadAtStop(Truck truck, ScheduleStop stop)
    {
        int space = truck.Spec.Capacity - truck.LoadedCargoAmount;
        if (space <= 0) return 0;

        if (stop.Kind == ScheduleStopKind.BusTerminal)
        {
            if (!truck.Spec.IsPassengerBus) return 0;
            var terminal = BusTerminals.FirstOrDefault(b => b.Id == stop.TargetId);
            if (terminal == null) return 0;

            int boarded = terminal.BoardPassengers(space);
            if (boarded > 0)
            {
                truck.LoadedCargoType = ResourceType.None;
                truck.LoadedCargoAmount += boarded;
                truck.LoadedFromHub = false;
            }
            return boarded;
        }

        if (truck.Spec.IsPassengerBus)
            return 0;

        if (stop.Kind == ScheduleStopKind.Industry)
        {
            var ind = World.Industries.FirstOrDefault(i => i.Id == stop.TargetId);
            if (ind == null) return 0;
            if (stop.CargoFilter != ResourceType.None && ind.OutputResource != stop.CargoFilter)
                return 0;
            if (truck.LoadedCargoAmount > 0 && truck.LoadedCargoType != ind.OutputResource)
                return 0;

            int loaded = ind.DispatchCargo(space);
            if (loaded > 0)
            {
                truck.LoadedCargoType = ind.OutputResource;
                truck.LoadedCargoAmount += loaded;
                truck.LoadedFromHub = false;
                RefreshTruckSpecialConvoyState(truck);
            }
            return loaded;
        }

        if (stop.Kind == ScheduleStopKind.LogisticsHub)
        {
            var hub = LogisticsHubs.FirstOrDefault(h => h.Id == stop.TargetId);
            if (hub == null) return 0;

            ResourceType targetType = truck.LoadedCargoAmount > 0
                ? truck.LoadedCargoType
                : stop.CargoFilter;

            if (targetType == ResourceType.None)
            {
                int maxAmt = 0;
                foreach (var kv in hub.StoredCargo)
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

            int withdrawn = hub.WithdrawCargo(targetType, space);
            if (withdrawn > 0)
            {
                truck.LoadedCargoType = targetType;
                truck.LoadedCargoAmount += withdrawn;
                truck.LoadedFromHub = true;
                RefreshTruckSpecialConvoyState(truck);
            }
            return withdrawn;
        }

        if (stop.Kind == ScheduleStopKind.Seaport)
        {
            var seaport = World.Seaports.FirstOrDefault(s => s.Id == stop.TargetId);
            if (seaport == null) return 0;

            ResourceType targetType = truck.LoadedCargoAmount > 0
                ? truck.LoadedCargoType
                : stop.CargoFilter;

            if (targetType == ResourceType.None)
            {
                int maxAmt = 0;
                foreach (var kv in seaport.TransitCargo)
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

            int withdrawn = seaport.DispatchTransitCargo(targetType, space);
            if (withdrawn > 0)
            {
                truck.LoadedCargoType = targetType;
                truck.LoadedCargoAmount += withdrawn;
                truck.LoadedFromHub = false;
                RefreshTruckSpecialConvoyState(truck);
            }
            return withdrawn;
        }

        return 0;
    }

    private void DeliverCargoToIndustry(Truck truck, Industry targetInd)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int accepted = targetInd.ReceiveInputCargo(deliveredType, truck.LoadedCargoAmount);

        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            bool hasNearbyHub = LogisticsHubs.Any(h =>
                Math.Abs(h.GridX - targetInd.GridX) + Math.Abs(h.GridY - targetInd.GridY) <= 15);
            float hubMultiplier = (hasNearbyHub || truck.LoadedFromHub) ? 1.20f : 1.0f;

            long freightReward = (long)(accepted * 15f * distanceFactor * hubMultiplier * Research.HeavyFreightBonusMultiplier);
            int xpReward = accepted * 7;

            if (hasNearbyHub)
                truck.Repair();

            FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, freightReward, xpReward);
        }

        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    private void DeliverCargoToCity(Truck truck, City targetCity)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int accepted = targetCity.Warehouse.ReceiveCargo(deliveredType, truck.LoadedCargoAmount);

        float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
        int demand = targetCity.Warehouse.DailyDemand.GetValueOrDefault(deliveredType, 10);
        int stockBefore = Math.Max(0, targetCity.Warehouse.StoredStock.GetValueOrDefault(deliveredType, 0) - accepted);
        float scarcityMultiplier = stockBefore < demand ? 1.25f : 1.0f;

        bool isManufactured = deliveredType is ResourceType.Food or ResourceType.Goods or ResourceType.Steel or ResourceType.Fuel or ResourceType.Machinery;
        float manufacturedMultiplier = deliveredType == ResourceType.Machinery ? 1.60f : (isManufactured ? 1.35f : 1.0f);

        bool hasNearbyHub = LogisticsHubs.Any(h =>
            Math.Abs(h.GridX - targetCity.CenterX) + Math.Abs(h.GridY - targetCity.CenterY) <= 15);
        float hubMultiplier = (hasNearbyHub || truck.LoadedFromHub) ? 1.20f : 1.0f;
        float reeferMultiplier = deliveredType == ResourceType.Food ? Research.FoodFreightBonusMultiplier : 1.0f;

        var convoyContract = ActiveContracts.FirstOrDefault(c =>
            c.IsSpecialConvoy &&
            !c.IsCompleted &&
            !c.IsExpired &&
            !c.IsLostToRival &&
            c.TargetCity.Id == targetCity.Id &&
            c.Resource == deliveredType);
        float convoyMultiplier = convoyContract != null ? convoyContract.FreightMultiplier : 1.0f;

        long baseFreightReward = (long)(accepted * 18f * distanceFactor * scarcityMultiplier * hubMultiplier * manufacturedMultiplier * reeferMultiplier * Research.HeavyFreightBonusMultiplier);
        long freightReward = (long)Math.Round(baseFreightReward * convoyMultiplier);
        int xpReward = accepted * (convoyContract != null ? 16 : 8);

        if (hasNearbyHub)
            truck.Repair();

        FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, freightReward, xpReward);
        RecordCityContractProgress(targetCity, deliveredType, accepted);

        truck.LoadedCargoAmount = 0;
        truck.LoadedCargoType = ResourceType.None;
        truck.LoadedFromHub = false;
        truck.IsSpecialConvoyActive = false;
    }

    private void DeliverCargoToHub(Truck truck, LogisticsHub hub)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int totalAmount = truck.LoadedCargoAmount;
        int fueled = deliveredType == ResourceType.Fuel ? hub.ReceiveFuel(totalAmount) : 0;
        int stored = hub.StoreCargo(deliveredType, Math.Max(0, totalAmount - fueled));
        int accepted = fueled + stored;

        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            long transferReward = (long)(accepted * (fueled > 0 ? 10f : 8f) * distanceFactor * Research.HeavyFreightBonusMultiplier);
            int xpReward = accepted * 4;

            FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, transferReward, xpReward);
        }

        hub.RepairTruckIfNeeded(truck);
        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    public long DeliverPassengersToBusTerminal(Truck bus, BusTerminal terminal)
    {
        int passengers = bus.LoadedCargoAmount;
        if (!bus.Spec.IsPassengerBus || passengers <= 0)
            return 0;

        float distanceFactor = 1f + Math.Clamp(bus.CurrentTripDistanceKm / 40f, 0f, 0.9f);
        long ticketRevenue = Math.Max(20L, (long)Math.Round(passengers * 16f * distanceFactor * LandmarkFreightMultiplier));

        Player.Balance += ticketRevenue;
        Player.AddXp(passengers * 6);
        Financials.RecordTicketRevenue(ticketRevenue);
        terminal.RecordArrival(passengers, ticketRevenue);

        var targetCity = World.Cities.FirstOrDefault(c => c.Id == terminal.CityId);
        if (targetCity != null)
        {
            targetCity.Population += Math.Max(2, passengers / 10);
            targetCity.ActiveBusServiceHours = Math.Max(targetCity.ActiveBusServiceHours, 72);
        }

        bus.LoadedCargoAmount = 0;
        bus.LoadedCargoType = ResourceType.None;
        OnPassengersDelivered?.Invoke(bus, terminal, passengers, ticketRevenue);
        return ticketRevenue;
    }

    private void DeliverFuelToGarage(Truck truck, Garage garage)
    {
        if (truck.LoadedCargoType != ResourceType.Fuel || truck.LoadedCargoAmount <= 0)
            return;

        int accepted = garage.ReceiveFuel(truck.LoadedCargoAmount);
        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            long supplyReward = (long)(accepted * 10f * distanceFactor * Research.HeavyFreightBonusMultiplier);
            FinalizeFreightPaymentAndEffects(truck, ResourceType.Fuel, accepted, supplyReward, accepted * 4);
        }

        truck.Repair();
        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    private void DeliverCargoToSeaport(Truck truck, SeaportTerminal seaport)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int accepted = seaport.LoadExportCargo(deliveredType, truck.LoadedCargoAmount, out bool wasShipDocked);

        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            bool hasNearbyHub = LogisticsHubs.Any(h =>
                Math.Abs(h.GridX - seaport.GridX) + Math.Abs(h.GridY - seaport.GridY) <= 15);
            float hubMultiplier = (hasNearbyHub || truck.LoadedFromHub) ? 1.20f : 1.0f;
            float exportMultiplier = wasShipDocked ? SeaportTerminal.ExportBonusMultiplier : 1.0f;

            long baseFreight = (long)Math.Round(accepted * 20f * distanceFactor * hubMultiplier * exportMultiplier * Research.HeavyFreightBonusMultiplier * Milestones.SeaportExportBonusMultiplier);
            int xpReward = accepted * (wasShipDocked ? 12 : 7);

            seaport.TotalExportRevenue += (long)Math.Round(baseFreight * LandmarkFreightBonusMultiplier * Milestones.FreightAndTollBonusMultiplier);

            if (hasNearbyHub)
                truck.Repair();

            FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, baseFreight, xpReward);
            EvaluateMilestones();
        }

        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    private void DeliverCargoToLandmark(Truck truck, RegionalLandmark landmark)
    {
        ResourceType deliveredType = truck.LoadedCargoType;
        int accepted = landmark.ContributeCargo(deliveredType, truck.LoadedCargoAmount, out bool stageCompleted, out bool projectCompleted);

        if (accepted > 0)
        {
            float distanceFactor = 1f + Math.Clamp(truck.CurrentTripDistanceKm / 40f, 0f, 0.8f);
            long freightReward = (long)Math.Round(accepted * 18f * distanceFactor * Research.HeavyFreightBonusMultiplier);
            int xpReward = accepted * 9;

            FinalizeFreightPaymentAndEffects(truck, deliveredType, accepted, freightReward, xpReward);

            if (stageCompleted)
            {
                long stageBonus = projectCompleted ? 15000L : 5000L;
                int stageXp = (int)Math.Round((projectCompleted ? 500 : 180) * LandmarkXpMultiplier);
                landmark.TotalInvestedBonusPaid += stageBonus;
                Player.Balance += stageBonus;
                Player.AddXp(stageXp);
                Financials.RecordContractBonus(stageBonus);
                OnLandmarkProgress?.Invoke(landmark, stageCompleted, projectCompleted);
                EvaluateMilestones();
            }
        }

        truck.LoadedCargoAmount = Math.Max(0, truck.LoadedCargoAmount - accepted);
        if (truck.LoadedCargoAmount == 0)
        {
            truck.LoadedCargoType = ResourceType.None;
            truck.LoadedFromHub = false;
        }
    }

    private void ProcessCitiesDailyCycle()
    {
        foreach (var terminal in BusTerminals)
        {
            var servedCity = World.Cities.FirstOrDefault(c => c.Id == terminal.CityId);
            if (servedCity != null)
                terminal.GenerateDailyPassengers(servedCity.Population);
        }

        int cityCount = World.Cities.Count;
        for (int i = 0; i < cityCount; i++)
        {
            var city = World.Cities[i];
            city.Warehouse.ConsumeDailyDemand();
            float satisfaction = city.Warehouse.CalculateSatisfactionRate();
            city.LastSatisfactionRate = satisfaction;

            bool hasActiveBus = city.ActiveBusServiceHours > 0;
            if (hasActiveBus)
            {
                city.ActiveBusServiceHours = Math.Max(0, city.ActiveBusServiceHours - 24);
                city.Population += 10; // Crescimento populacional acelerado por transporte intermunicipal
            }

            if (satisfaction >= 0.80f)
            {
                city.ConsecutiveCyclesWellSupplied++;
                int cyclesRequired = satisfaction >= 0.92f ? 2 : 3;
                if (hasActiveBus)
                    cyclesRequired = Math.Max(1, cyclesRequired - 1);

                if (city.ConsecutiveCyclesWellSupplied >= cyclesRequired)
                {
                    ExpandCityRandomly(city);
                    city.ConsecutiveCyclesWellSupplied = 0;
                }
            }
            else
            {
                city.ConsecutiveCyclesWellSupplied = 0;
            }
        }
    }

    private void ExpandCityRandomly(City city)
    {
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };

        var candidates = new List<(int X, int Y)>();
        foreach (var (ux, uy) in city.UrbanTiles)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = ux + dx[dir];
                int ny = uy + dy[dir];

                if (nx >= 0 && nx < World.Grid.GetLength(0) && ny >= 0 && ny < World.Grid.GetLength(1))
                {
                    ref readonly var nCell = ref World.Grid[nx, ny];
                    if (nCell.Terrain == TerrainType.Plains && nCell.OccupantId == 0 && !nCell.HasRoad && !World.OceanMask[nx, ny] && !city.UrbanTiles.Contains((nx, ny)))
                    {
                        candidates.Add((nx, ny));
                    }
                }
            }
        }

        if (candidates.Count > 0)
        {
            var choice = candidates[_rng.Next(candidates.Count)];
            city.UrbanTiles.Add(choice);
            World.Grid[choice.X, choice.Y].OccupantId = city.Id;
            World.Grid[choice.X, choice.Y].RoadLevel = RoadTier.None; // Novo quarteirão urbano NUNCA nasce sobre rodovia

            city.Population += _rng.Next(30, 60);
            city.ExpansionLevel++;

            var (res, delta, isNewRes) = city.Warehouse.ExpandDemandForGrowth(city.ExpansionLevel, city.Population);

            OnCityExpanded?.Invoke(city, choice.X, choice.Y);
            OnDemandIncreased?.Invoke(city, res, delta, isNewRes);

            _totalUrbanExpansions++;
            if (_totalUrbanExpansions % 2 == 0 && World.UnlockedRegionsCount < GeneratedWorld.MaxRegionsCount)
            {
                TryUnlockNewRegion(out _);
            }
        }
    }

    public int GetNextRegionUnlockCost() => 12000 + Math.Max(0, World.UnlockedRegionsCount - 1) * 6000;

    /// <summary>
    /// Permite ao jogador financiar manualmente a abertura de uma nova fronteira regional.
    /// </summary>
    public bool TryFundRegionalExpansion(out RegionUnlockInfo? info, out string message)
    {
        info = null;
        if (World.UnlockedRegionsCount >= GeneratedWorld.MaxRegionsCount)
        {
            message = "🗺️ Todas as regiões do mapa já foram desbloqueadas!";
            return false;
        }

        int cost = GetNextRegionUnlockCost();
        if (Player.Balance < cost)
        {
            message = $"⚠️ Saldo insuficiente para expedição regional (R$ {cost:N0}).";
            return false;
        }

        if (!TryUnlockNewRegion(out info) || info == null)
        {
            message = "⚠️ Não há terreno disponível para abrir uma nova região.";
            return false;
        }

        Player.Balance -= cost;
        Financials.RecordCapEx(cost);
        message = $"🗺️ Região '{info.RegionName}' desbloqueada por R$ {cost:N0}!";
        return true;
    }

    public bool TryStartResearch(ResearchTechId techId, out string message)
    {
        long before = Player.Balance;
        bool ok = Research.StartOrSwitchResearch(techId, Player, out message);
        long spent = before - Player.Balance;
        if (spent > 0)
            Financials.RecordCapEx(spent);
        return ok;
    }

    /// <summary>
    /// Desbloqueia uma nova região do mapa com assentamento e indústria interligados à malha viária.
    /// </summary>
    public bool TryUnlockNewRegion(out RegionUnlockInfo? info)
    {
        info = null;
        if (World.UnlockedRegionsCount >= GeneratedWorld.MaxRegionsCount)
            return false;

        int w = World.Grid.GetLength(0);
        int h = World.Grid.GetLength(1);

        (int X, int Y)? citySpot = FindOpenPlainSpot(w, h, minCityDist: 14, minIndDist: 8)
            ?? FindOpenPlainSpot(w, h, minCityDist: 7, minIndDist: 5);

        if (citySpot == null)
            return false;

        World.UnlockedRegionsCount++;
        string[] regionNames = { "Central", "Serra Leste", "Vale do Ouro", "Planalto Norte", "Costa Sul" };
        string regionName = regionNames[Math.Clamp(World.UnlockedRegionsCount - 1, 0, regionNames.Length - 1)];

        var usedCityNames = new HashSet<string>(World.Cities.Select(c => c.Name));
        string newCityName = WorldGenerator.GenerateUniqueCityName(usedCityNames, _rng);
        var newCity = new City(_nextCityId++, newCityName, citySpot.Value.X, citySpot.Value.Y);
        World.Cities.Add(newCity);

        foreach (var (ux, uy) in newCity.UrbanTiles)
        {
            if (ux >= 0 && ux < w && uy >= 0 && uy < h && World.Grid[ux, uy].OccupantId == 0)
            {
                World.Grid[ux, uy].OccupantId = newCity.Id;
            }
        }

        // Conecta a nova cidade ao nó viário mais próximo DO MESMO CONTINENTE (sem cruzar o oceano)
        var cityAccess = WorldGenerator.GetCityAccessPoint(World, newCity);
        if (World.RoadNodes.Count > 0)
        {
            var anchor = World.RoadNodes
                .Where(n => World.AreSameContinent(n.GridX, n.GridY, cityAccess.X, cityAccess.Y))
                .OrderBy(n => Math.Abs(n.GridX - cityAccess.X) + Math.Abs(n.GridY - cityAccess.Y))
                .FirstOrDefault();
            if (anchor != null)
            {
                WorldGenerator.CarveDirtRoadAndLinkNodes(
                    World,
                    cityAccess.X,
                    cityAccess.Y,
                    anchor.GridX,
                    anchor.GridY,
                    ref _nextRoadNodeId,
                    ref _nextRoadEdgeId);
                World.Grid[newCity.CenterX, newCity.CenterY].RoadLevel = RoadTier.None;
            }
        }

        // Tenta posicionar também uma nova indústria regional conectada no mesmo continente
        Industry? newIndustry = null;
        (int X, int Y)? indSpot = FindOpenPlainSpot(w, h, minCityDist: 8, minIndDist: 8, requiredContinent: World.ContinentMap[newCity.CenterX, newCity.CenterY]);
        if (indSpot != null)
        {
            var templates = new (ResourceType Out, ResourceType In)[]
            {
                (ResourceType.Goods, ResourceType.Lumber),
                (ResourceType.Fuel, ResourceType.None),
                (ResourceType.Food, ResourceType.Grain),
                (ResourceType.Steel, ResourceType.IronOre)
            };
            var tmpl = templates[(World.UnlockedRegionsCount - 2 + templates.Length) % templates.Length];
            var usedIndNames = new HashSet<string>(World.Industries.Select(i => i.Name));
            string indName = WorldGenerator.GenerateUniqueEstablishmentName(tmpl.Out, usedIndNames, _rng);

            newIndustry = new Industry(_nextIndustryId++, indName, indSpot.Value.X, indSpot.Value.Y, tmpl.Out, tmpl.In);
            World.Industries.Add(newIndustry);
            World.Grid[newIndustry.GridX, newIndustry.GridY].OccupantId = newIndustry.Id;
            World.Grid[newIndustry.GridX, newIndustry.GridY].RoadLevel = RoadTier.None;

            var accessPt = WorldGenerator.GetAccessPointForIndustry(World, newIndustry.GridX, newIndustry.GridY);
            WorldGenerator.CarveDirtRoadAndLinkNodes(
                World,
                accessPt.X,
                accessPt.Y,
                cityAccess.X,
                cityAccess.Y,
                ref _nextRoadNodeId,
                ref _nextRoadEdgeId);
            World.Grid[newIndustry.GridX, newIndustry.GridY].RoadLevel = RoadTier.None;
            World.Grid[newCity.CenterX, newCity.CenterY].RoadLevel = RoadTier.None;
        }

        CreateContractForCity(newCity);
        SyncAllEdgeBiomes();
        _nearestNodeCache.Clear();
        Pathfinder.InvalidateCache();

        info = new RegionUnlockInfo(World.UnlockedRegionsCount, regionName, newCity, newIndustry);
        OnRegionUnlocked?.Invoke(info);
        return true;
    }

    private (int X, int Y)? FindOpenPlainSpot(int w, int h, int minCityDist, int minIndDist, int requiredContinent = 0)
    {
        int margin = Math.Min(4, Math.Max(2, Math.Min(w, h) / 8));
        for (int attempt = 0; attempt < 240; attempt++)
        {
            int x = _rng.Next(margin, w - margin);
            int y = _rng.Next(margin, h - margin);

            if (World.OceanMask[x, y] || World.Grid[x, y].Terrain != TerrainType.Plains || World.Grid[x, y].OccupantId != 0)
                continue;
            if (requiredContinent > 0 && World.ContinentMap[x, y] != requiredContinent)
                continue;

            bool tooClose = false;
            foreach (var c in World.Cities)
            {
                if (Math.Abs(c.CenterX - x) + Math.Abs(c.CenterY - y) < minCityDist)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            foreach (var ind in World.Industries)
            {
                if (Math.Abs(ind.GridX - x) + Math.Abs(ind.GridY - y) < minIndDist)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose)
                return (x, y);
        }
        return null;
    }

    /// <summary>
    /// Constrói uma nova estrada em planície ou cria automaticamente Ponte (água) / Túnel (morro/montanha).
    /// </summary>
    public bool BuildRoadTile(int x, int y, out RoadStructureType builtStructure, RoadTier initialTier = RoadTier.Dirt)
    {
        builtStructure = RoadStructureType.Ground;
        if (x < 0 || x >= World.Grid.GetLength(0) || y < 0 || y >= World.Grid.GetLength(1))
            return false;

        ref var cell = ref World.Grid[x, y];
        if (cell.HasRoad || cell.OccupantId != 0 || World.OceanMask[x, y])
            return false;

        builtStructure = cell.Terrain == TerrainType.Water
            ? RoadStructureType.Bridge
            : (cell.Terrain is TerrainType.Hills or TerrainType.Mountain ? RoadStructureType.Tunnel : RoadStructureType.Ground);

        int cost = builtStructure.GetBaseConstructionCost();
        if (Player.Balance < cost)
            return false;

        Player.Balance -= cost;
        Financials.RecordRoadConstruction(cost);
        cell.RoadLevel = initialTier;

        var newNode = new RoadNode(_nextRoadNodeId++, x, y);
        World.RoadNodes.Add(newNode);

        // Conecta ao nó viário vizinho mais próximo (ortogonal ou até distância 2) para habilitar rotas pela nova ponte/túnel
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };
        for (int d = 0; d < 4; d++)
        {
            int nx = x + dx[d];
            int ny = y + dy[d];
            if (nx >= 0 && nx < World.Grid.GetLength(0) && ny >= 0 && ny < World.Grid.GetLength(1) && World.Grid[nx, ny].HasRoad)
            {
                var adjNode = World.RoadNodes.FirstOrDefault(n => n.GridX == nx && n.GridY == ny);
                if (adjNode == null)
                {
                    adjNode = new RoadNode(_nextRoadNodeId++, nx, ny);
                    World.RoadNodes.Add(adjNode);
                }

                int w = World.Grid.GetLength(0);
                int h = World.Grid.GetLength(1);
                bool alpine = IsNodeAlpine(newNode, w, h) || IsNodeAlpine(adjNode, w, h);
                bool hillOrMountain = IsNodeHillOrMountain(newNode, w, h) || IsNodeHillOrMountain(adjNode, w, h);
                var f = new RoadEdge(_nextRoadEdgeId++, newNode, adjNode, initialTier, 1.0f) { IsAlpineBiome = alpine, IsHillOrMountain = hillOrMountain };
                var b = new RoadEdge(_nextRoadEdgeId++, adjNode, newNode, initialTier, 1.0f) { IsAlpineBiome = alpine, IsHillOrMountain = hillOrMountain };
                newNode.OutgoingEdges.Add(f);
                adjNode.OutgoingEdges.Add(b);
                World.RoadEdges.Add(f);
                World.RoadEdges.Add(b);
            }
        }

        _nearestNodeCache.Clear();
        Pathfinder.InvalidateCache();
        EvaluateMilestones();
        return true;
    }

    /// <summary>
    /// Aplica upgrade em um trecho de estrada existente, validando o nível do jogador e saldo.
    /// </summary>
    public bool UpgradeRoadTile(int x, int y, RoadTier targetTier)
    {
        if (x < 0 || x >= World.Grid.GetLength(0) || y < 0 || y >= World.Grid.GetLength(1))
            return false;

        ref var cell = ref World.Grid[x, y];
        if (cell.RoadLevel == RoadTier.None || cell.RoadLevel >= targetTier)
            return false;

        // Validação de nível do jogador e árvore de pesquisa
        if (targetTier > Research.GetMaxUnlockedRoadTier(Player.Level))
            return false;

        int cost = cell.RoadLevel.GetUpgradeCost(targetTier);
        if (Player.Balance < cost)
            return false;

        Player.Balance -= cost;
        Financials.RecordRoadConstruction(cost);
        cell.RoadLevel = targetTier;

        foreach (var edge in GetEdgesTouchingTile(x, y))
        {
            edge.Tier = targetTier;
            edge.Repave();
            OnRoadUpgraded?.Invoke(edge, targetTier);
        }

        Pathfinder.InvalidateCache();
        EvaluateMilestones();
        return true;
    }

    /// <summary>
    /// Aplica upgrade em lote em um corredor viário conectado a partir de (startX, startY),
    /// concedendo 10% de desconto de empreiteira para obras em escala.
    /// Retorna o número de células modernizadas.
    /// </summary>
    public int UpgradeRoadCorridor(int startX, int startY, RoadTier targetTier, int maxTiles = 30)
    {
        if (startX < 0 || startX >= World.Grid.GetLength(0) || startY < 0 || startY >= World.Grid.GetLength(1))
            return 0;

        if (targetTier > Research.GetMaxUnlockedRoadTier(Player.Level))
            return 0;

        if (World.Grid[startX, startY].RoadLevel == RoadTier.None)
            return 0;

        // Busca em largura (BFS) pelas células de estrada conectadas que ainda estão abaixo do targetTier
        var queue = new Queue<(int X, int Y)>();
        var visited = new HashSet<(int X, int Y)>();
        var toUpgrade = new List<(int X, int Y)>();

        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };

        while (queue.Count > 0 && toUpgrade.Count < maxTiles)
        {
            var (cx, cy) = queue.Dequeue();
            var currentTier = World.Grid[cx, cy].RoadLevel;
            if (currentTier != RoadTier.None && currentTier < targetTier)
            {
                toUpgrade.Add((cx, cy));
            }

            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if (nx >= 0 && nx < World.Grid.GetLength(0) && ny >= 0 && ny < World.Grid.GetLength(1))
                {
                    if (!visited.Contains((nx, ny)) && World.Grid[nx, ny].RoadLevel != RoadTier.None)
                    {
                        visited.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
                }
            }
        }

        int upgradedCount = 0;
        foreach (var (ux, uy) in toUpgrade)
        {
            ref var cell = ref World.Grid[ux, uy];
            int baseCost = cell.RoadLevel.GetUpgradeCost(targetTier);
            int discountedCost = Math.Max(1, (int)(baseCost * 0.90f)); // 10% de desconto em lote

            if (Player.Balance < discountedCost)
                break;

            Player.Balance -= discountedCost;
            Financials.RecordRoadConstruction(discountedCost);
            cell.RoadLevel = targetTier;
            upgradedCount++;

            foreach (var edge in World.RoadEdges)
            {
                if ((edge.FromNode.GridX == ux && edge.FromNode.GridY == uy) ||
                    (edge.ToNode.GridX == ux && edge.ToNode.GridY == uy))
                {
                    edge.Tier = targetTier;
                    edge.Repave();
                    OnRoadUpgraded?.Invoke(edge, targetTier);
                }
            }
        }

        if (upgradedCount > 0)
        {
            Pathfinder.InvalidateCache();
        }

        return upgradedCount;
    }

    /// <summary>
    /// Analisa esteiras, tubos, docas viárias e pórticos conectados ao redor de uma indústria.
    /// </summary>
    public IndustrialConnections GetIndustryConnections(Industry ind)
    {
        var grid = World.Grid;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        int x = ind.GridX;
        int y = ind.GridY;

        bool IsRoadAt(int nx, int ny) => nx >= 0 && nx < w && ny >= 0 && ny < h && grid[nx, ny].HasRoad;
        bool IsFacilityAt(int nx, int ny)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
            return LogisticsHubs.Any(hub => hub.GridX == nx && hub.GridY == ny)
                || Garages.Any(g => g.GridX == nx && g.GridY == ny)
                || World.Industries.Any(other => other.Id != ind.Id && other.GridX == nx && other.GridY == ny);
        }

        bool linkN = IsRoadAt(x, y - 1);
        bool linkS = IsRoadAt(x, y + 1);
        bool linkE = IsRoadAt(x + 1, y);
        bool linkW = IsRoadAt(x - 1, y);

        bool facN = IsFacilityAt(x, y - 1);
        bool facS = IsFacilityAt(x, y + 1);
        bool facE = IsFacilityAt(x + 1, y);
        bool facW = IsFacilityAt(x - 1, y);

        int nearbyHubs = LogisticsHubs.Count(hub =>
            Math.Abs(hub.GridX - x) + Math.Abs(hub.GridY - y) <= 2);

        int roadDocks = (linkN ? 1 : 0) + (linkS ? 1 : 0) + (linkE ? 1 : 0) + (linkW ? 1 : 0) + Math.Max(0, ind.UpgradeLevel - 1);
        int facilityLinks = (facN ? 1 : 0) + (facS ? 1 : 0) + (facE ? 1 : 0) + (facW ? 1 : 0);

        int roadBonus = roadDocks >= 2 ? Math.Min(5, roadDocks - 1) : 0;
        int hubBonus = nearbyHubs * 3;

        return new IndustrialConnections(
            LinkN: linkN || facN,
            LinkS: linkS || facS,
            LinkE: linkE || facE,
            LinkW: linkW || facW,
            HeavyBridgeN: facN,
            HeavyBridgeS: facS,
            HeavyBridgeE: facE,
            HeavyBridgeW: facW,
            RoadDockCount: roadDocks,
            FacilityBridgeCount: facilityLinks,
            NearbyHubCount: nearbyHubs,
            DailyProductionBonus: roadBonus + hubBonus);
    }

    private void TransferConnectedIndustrialConveyors()
    {
        foreach (var consumer in World.Industries)
        {
            bool needsPrimary = consumer.RequiredInputResource != ResourceType.None && consumer.InputStock < consumer.MaxStock;
            bool needsSecondary = consumer.SecondaryInputResource != ResourceType.None && consumer.SecondaryInputStock < consumer.MaxStock;
            bool needsTertiary = consumer.TertiaryInputResource != ResourceType.None && consumer.TertiaryInputStock < consumer.MaxStock;
            if (!needsPrimary && !needsSecondary && !needsTertiary)
                continue;

            foreach (var supplier in World.Industries)
            {
                if (supplier.Id == consumer.Id || supplier.OutputStock <= 0)
                    continue;

                bool matchesPrimary = needsPrimary && supplier.OutputResource == consumer.RequiredInputResource;
                bool matchesSecondary = needsSecondary && supplier.OutputResource == consumer.SecondaryInputResource;
                bool matchesTertiary = needsTertiary && supplier.OutputResource == consumer.TertiaryInputResource;
                if (!matchesPrimary && !matchesSecondary && !matchesTertiary)
                    continue;

                int dist = Math.Abs(supplier.GridX - consumer.GridX) + Math.Abs(supplier.GridY - consumer.GridY);
                if (dist <= 2)
                {
                    int dispatch = supplier.DispatchCargo(5);
                    if (dispatch > 0)
                        consumer.ReceiveInputCargo(supplier.OutputResource, dispatch);
                }
            }
        }
    }

    /// <summary>
    /// Preenche um buffer (W x H) com a contagem máxima de tráfego ativo em cada célula viária.
    /// </summary>
    public void FillCellTrafficMap(int[,] buffer)
    {
        int w = buffer.GetLength(0);
        int h = buffer.GetLength(1);
        Array.Clear(buffer, 0, buffer.Length);

        foreach (var edge in World.RoadEdges)
        {
            if (edge.ActiveTrucksCount <= 0)
                continue;

            int fx = edge.FromNode.GridX;
            int fy = edge.FromNode.GridY;
            if (fx >= 0 && fx < w && fy >= 0 && fy < h && edge.ActiveTrucksCount > buffer[fx, fy])
                buffer[fx, fy] = edge.ActiveTrucksCount;

            int tx = edge.ToNode.GridX;
            int ty = edge.ToNode.GridY;
            if (tx >= 0 && tx < w && ty >= 0 && ty < h && edge.ActiveTrucksCount > buffer[tx, ty])
                buffer[tx, ty] = edge.ActiveTrucksCount;
        }
    }

    /// <summary>
    /// Planeja um traçado viário A* entre duas células do grid, calculando custo, pontes e túneis antes de construir.
    /// </summary>
    public RoadBuildPlan PlanRoadPath(int startX, int startY, int endX, int endY)
    {
        int w = World.Grid.GetLength(0);
        int h = World.Grid.GetLength(1);
        var empty = Array.Empty<(int, int)>();

        if (startX < 0 || startX >= w || startY < 0 || startY >= h ||
            endX < 0 || endX >= w || endY < 0 || endY >= h)
        {
            return new RoadBuildPlan(empty, empty, 0, 0, 0, false, false);
        }

        if (World.Grid[endX, endY].OccupantId != 0 && !World.Grid[endX, endY].HasRoad)
        {
            return new RoadBuildPlan(empty, empty, 0, 0, 0, false, false);
        }

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
                if ((!cell.HasRoad && cell.OccupantId != 0) || World.OceanMask[nx, ny])
                    continue;

                float stepCost = cell.HasRoad
                    ? 0.2f
                    : cell.Terrain switch
                    {
                        TerrainType.Water => 4.5f,
                        TerrainType.Hills or TerrainType.Mountain => 3.5f,
                        _ => 1.0f
                    };

                float tentativeG = currG + stepCost;
                var next = (nx, ny);
                if (!gScore.TryGetValue(next, out float prevG) || tentativeG < prevG)
                {
                    cameFrom[next] = curr;
                    gScore[next] = tentativeG;
                    float f = tentativeG + MathF.Abs(endX - nx) + MathF.Abs(endY - ny);
                    open.Enqueue(next, f);
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
        int bridges = 0;
        int tunnels = 0;
        long totalCost = 0;

        foreach (var (px, py) in path)
        {
            ref readonly var cell = ref World.Grid[px, py];
            if (cell.HasRoad || cell.OccupantId != 0)
                continue;

            var structure = cell.Terrain == TerrainType.Water
                ? RoadStructureType.Bridge
                : (cell.Terrain is TerrainType.Hills or TerrainType.Mountain ? RoadStructureType.Tunnel : RoadStructureType.Ground);

            if (structure == RoadStructureType.Bridge) bridges++;
            else if (structure == RoadStructureType.Tunnel) tunnels++;

            totalCost += structure.GetBaseConstructionCost();
            toBuild.Add((px, py));
        }

        return new RoadBuildPlan(
            PathTiles: path,
            TilesToBuild: toBuild,
            BridgesCount: bridges,
            TunnelsCount: tunnels,
            TotalCost: totalCost,
            CanAfford: Player.Balance >= totalCost,
            IsValid: toBuild.Count > 0);
    }

    /// <summary>
    /// Executa a construção em lote de um plano viário gerado por PlanRoadPath.
    /// </summary>
    public bool BuildPlannedRoad(RoadBuildPlan plan, out int builtCount, out int bridgesCount, out int tunnelsCount)
    {
        builtCount = 0;
        bridgesCount = 0;
        tunnelsCount = 0;

        if (!plan.IsValid || !plan.CanAfford || plan.TilesToBuild.Count == 0)
            return false;

        foreach (var (x, y) in plan.TilesToBuild)
        {
            if (BuildRoadTile(x, y, out var structure))
            {
                builtCount++;
                if (structure == RoadStructureType.Bridge) bridgesCount++;
                else if (structure == RoadStructureType.Tunnel) tunnelsCount++;
            }
        }

        return builtCount > 0;
    }

    public readonly record struct TerraformResult(
        int GridX,
        int GridY,
        TerrainType PreviousTerrain,
        TerrainType NewTerrain,
        long Cost);

    public static long GetTerraformCost(TerrainType terrain) => terrain switch
    {
        TerrainType.Mountain => 220L,
        TerrainType.Hills => 120L,
        TerrainType.Forest => 75L,
        _ => 0L
    };

    /// <summary>
    /// Executa terraplenagem/corte de terreno em uma célula livre:
    /// Mountain -> Hills (R$ 220), Hills -> Plains (R$ 120) ou Forest -> Plains (R$ 75).
    /// </summary>
    public bool TryTerraformTile(int x, int y, out TerraformResult result, out string message)
    {
        result = default;
        if (!IsTileInBounds(x, y))
        {
            message = "⚠️ Coordenada fora dos limites do mapa.";
            return false;
        }

        ref var cell = ref World.Grid[x, y];
        if (cell.HasRoad)
        {
            message = "⚠️ Remova a via antes de terraplenar esta célula.";
            return false;
        }

        if (cell.OccupantId != 0 ||
            TollStations.Any(t => t.GridX == x && t.GridY == y) ||
            LogisticsHubs.Any(h => h.GridX == x && h.GridY == y) ||
            Garages.Any(g => g.GridX == x && g.GridY == y))
        {
            message = "⚠️ Há uma estrutura ocupando esta célula.";
            return false;
        }

        if (cell.Terrain is not (TerrainType.Mountain or TerrainType.Hills or TerrainType.Forest))
        {
            message = cell.Terrain == TerrainType.Water
                ? "⚠️ Terraplenagem não aterra água — use Pontes para cruzar rios e mar."
                : "ℹ️ Esta célula já está nivelada (Planície).";
            return false;
        }

        TerrainType prev = cell.Terrain;
        TerrainType next = prev == TerrainType.Mountain ? TerrainType.Hills : TerrainType.Plains;
        long cost = GetTerraformCost(prev);

        if (Player.Balance < cost)
        {
            message = $"❌ Saldo insuficiente para terraplenagem (Custa R$ {cost:N0}).";
            return false;
        }

        Player.Balance -= cost;
        Financials.RecordCapEx(cost);
        cell.Terrain = next;

        result = new TerraformResult(x, y, prev, next, cost);
        OnTileTerraformed?.Invoke(x, y, prev, next);
        message = prev == TerrainType.Mountain
            ? $"⛏️ Pico escavado para Colina em ({x}, {y}) (-R$ {cost:N0})!"
            : $"⛏️ Terreno nivelado para Planície em ({x}, {y}) (-R$ {cost:N0})!";
        return true;
    }

    public void GrantTutorialReward(long bonusMoney = 2500L, int bonusXp = 250)
    {
        if (bonusMoney > 0)
        {
            Player.Balance += bonusMoney;
            Financials.RecordContractBonus(bonusMoney);
        }
        if (bonusXp > 0)
        {
            Player.AddXp(bonusXp);
        }
    }

    // =========================================================================
    // Sprint 4 — Mundo Vivo: Pavimento, Eventos Sazonais e Subsídios Municipais
    // =========================================================================

    private void SyncAllEdgeBiomes()
    {
        int w = World.Grid.GetLength(0);
        int h = World.Grid.GetLength(1);
        foreach (var edge in World.RoadEdges)
        {
            edge.IsAlpineBiome = IsNodeAlpine(edge.FromNode, w, h) || IsNodeAlpine(edge.ToNode, w, h);
            edge.IsHillOrMountain = IsNodeHillOrMountain(edge.FromNode, w, h) || IsNodeHillOrMountain(edge.ToNode, w, h);
        }
    }

    private bool IsNodeAlpine(RoadNode node, int w, int h)
    {
        return node.GridX >= 0 && node.GridX < w && node.GridY >= 0 && node.GridY < h &&
               World.Grid[node.GridX, node.GridY].Biome == BiomeType.Alpine;
    }

    private bool IsNodeHillOrMountain(RoadNode node, int w, int h)
    {
        if (node.GridX < 0 || node.GridX >= w || node.GridY < 0 || node.GridY >= h)
            return false;
        var cell = World.Grid[node.GridX, node.GridY];
        return cell.Terrain is TerrainType.Hills or TerrainType.Mountain || cell.Biome == BiomeType.Alpine;
    }

    private bool IsTileInBounds(int x, int y)
    {
        return x >= 0 && x < World.Grid.GetLength(0) && y >= 0 && y < World.Grid.GetLength(1);
    }

    private IReadOnlyList<RoadEdge> GetEdgesTouchingTile(int x, int y)
    {
        EnsureRoadSpatialCache();
        return _edgesByTile.TryGetValue((x, y), out var list) ? list : Array.Empty<RoadEdge>();
    }

    /// <summary>
    /// Retorna o nó rodoviário exatamente na célula (x, y), em O(1).
    /// </summary>
    public RoadNode? GetRoadNodeAt(int x, int y)
    {
        EnsureRoadSpatialCache();
        return _roadNodeByGrid.TryGetValue((x, y), out var node) ? node : null;
    }

    private void EnsureRoadSpatialCache()
    {
        if (_cachedRoadNodeCount != World.RoadNodes.Count || _cachedRoadEdgeCount != World.RoadEdges.Count)
            RebuildRoadSpatialCache();
    }

    private void RebuildRoadSpatialCache()
    {
        _roadNodeByGrid.Clear();
        foreach (var node in World.RoadNodes)
            _roadNodeByGrid[(node.GridX, node.GridY)] = node;

        _edgesByTile.Clear();
        foreach (var edge in World.RoadEdges)
        {
            var fromKey = (edge.FromNode.GridX, edge.FromNode.GridY);
            if (!_edgesByTile.TryGetValue(fromKey, out var fromList))
            {
                fromList = new List<RoadEdge>(4);
                _edgesByTile[fromKey] = fromList;
            }
            fromList.Add(edge);

            var toKey = (edge.ToNode.GridX, edge.ToNode.GridY);
            if (toKey != fromKey)
            {
                if (!_edgesByTile.TryGetValue(toKey, out var toList))
                {
                    toList = new List<RoadEdge>(4);
                    _edgesByTile[toKey] = toList;
                }
                toList.Add(edge);
            }
        }

        _cachedRoadNodeCount = World.RoadNodes.Count;
        _cachedRoadEdgeCount = World.RoadEdges.Count;
    }

    private static long GetEdgeMaintenanceBaseCost(RoadTier tier) => tier switch
    {
        RoadTier.Dirt => 60L,
        RoadTier.Gravel => 110L,
        RoadTier.Asphalt => 200L,
        RoadTier.Highway => 320L,
        RoadTier.DualCarriage => 450L,
        _ => 50L
    };

    private static bool HasBlockedEdgeInRemainingPath(Truck truck)
    {
        foreach (var edge in truck.RemainingPath)
        {
            if (edge.IsBlocked)
                return true;
        }
        return false;
    }

    private void TryRerouteAroundBlockedRoad(Truck truck)
    {
        if (truck.CurrentEdge == null || truck.CurrentDestinationNode == null)
            return;

        var altPath = Pathfinder.FindFastestRoute(
            truck.CurrentEdge.ToNode,
            truck.CurrentDestinationNode,
            CurrentWeather,
            preferHighways: truck.Spec.RequiresHighwayOrDualCarriage,
            winterBlizzard: IsWinterBlizzardActive);

        if (altPath != null && altPath.Count > 0)
        {
            truck.RemainingPath.Clear();
            foreach (var edge in altPath)
                truck.RemainingPath.Enqueue(edge);
        }
    }

    private void UpdateSeasonalEvents()
    {
        if (ActiveSeasonalEvent != SeasonalEventType.None || CurrentTick < 72)
            return;

        double roll = _rng.NextDouble();
        int month = Calendar.Month;

        // Inverno (Jun-Ago): Nevasca em biomas alpinos ou deslizamento em encostas
        if (month is >= 6 and <= 8)
        {
            if (roll < 0.18)
            {
                TriggerSeasonalEvent(SeasonalEventType.WinterBlizzard, 48f);
            }
            else if (roll < 0.24)
            {
                TriggerSeasonalEvent(SeasonalEventType.MountainLandslide, 36f);
            }
            return;
        }

        // Primavera/Verão (Set-Fev): Safra Recorde de Grãos ou deslizamento sazonal
        if (month >= 9 || month <= 2)
        {
            if (roll < 0.16)
            {
                TriggerSeasonalEvent(SeasonalEventType.BumperHarvest, 72f);
            }
            else if (roll < 0.22)
            {
                TriggerSeasonalEvent(SeasonalEventType.MountainLandslide, 36f);
            }
            return;
        }

        // Outono (Mar-Mai): eventos moderados
        if (roll < 0.08)
        {
            TriggerSeasonalEvent(SeasonalEventType.MountainLandslide, 36f);
        }
        else if (roll < 0.16)
        {
            TriggerSeasonalEvent(SeasonalEventType.BumperHarvest, 48f);
        }
    }

    /// <summary>
    /// Ativa manualmente ou por sorteio um evento sazonal/climático extremo.
    /// </summary>
    public bool TriggerSeasonalEvent(SeasonalEventType evt, float durationHours = 48f)
    {
        if (ActiveSeasonalEvent != SeasonalEventType.None)
        {
            ClearActiveSeasonalEvent(notify: false);
        }

        if (evt == SeasonalEventType.None)
        {
            OnSeasonalEventChanged?.Invoke(SeasonalEventType.None, 0f);
            return true;
        }

        if (evt == SeasonalEventType.MountainLandslide)
        {
            var targetEdge = PickLandslideCandidateEdge();
            if (targetEdge == null)
                return false;

            targetEdge.IsBlocked = true;
            foreach (var rev in targetEdge.ToNode.OutgoingEdges)
            {
                if (rev.ToNode.Id == targetEdge.FromNode.Id)
                {
                    rev.IsBlocked = true;
                }
            }
            BlockedLandslideEdgeId = targetEdge.Id;
            Pathfinder.InvalidateCache();
        }

        ActiveSeasonalEvent = evt;
        SeasonalEventRemainingHours = Math.Max(1f, durationHours);
        OnSeasonalEventChanged?.Invoke(ActiveSeasonalEvent, SeasonalEventRemainingHours);
        return true;
    }

    /// <summary>
    /// Encerra o evento sazonal ativo e libera eventuais vias bloqueadas por deslizamento.
    /// </summary>
    public void ClearActiveSeasonalEvent(bool notify = true)
    {
        foreach (var edge in World.RoadEdges)
        {
            edge.IsBlocked = false;
        }

        BlockedLandslideEdgeId = 0;
        ActiveSeasonalEvent = SeasonalEventType.None;
        SeasonalEventRemainingHours = 0f;
        Pathfinder.InvalidateCache();

        if (notify)
        {
            OnSeasonalEventChanged?.Invoke(SeasonalEventType.None, 0f);
        }
    }

    private RoadEdge? PickLandslideCandidateEdge()
    {
        var mountainEdges = new List<RoadEdge>();
        foreach (var edge in World.RoadEdges)
        {
            var tFrom = World.Grid[edge.FromNode.GridX, edge.FromNode.GridY].Terrain;
            var tTo = World.Grid[edge.ToNode.GridX, edge.ToNode.GridY].Terrain;
            if (tFrom is TerrainType.Hills or TerrainType.Mountain ||
                tTo is TerrainType.Hills or TerrainType.Mountain)
            {
                mountainEdges.Add(edge);
            }
        }

        if (mountainEdges.Count == 0)
            return null;

        return mountainEdges[_rng.Next(mountainEdges.Count)];
    }

    private void PayMunicipalSubsidies()
    {
        foreach (var city in World.Cities)
        {
            if (city.LastSatisfactionRate < 0.80f)
                continue;

            float bonusMult = city.LastSatisfactionRate >= 0.92f ? 1.35f : 1.0f;
            long subsidy = Math.Max(150L, (long)Math.Round(city.Population * 0.45f * bonusMult));

            Player.Balance += subsidy;
            city.TotalSubsidiesReceived += subsidy;
            Financials.RecordMunicipalSubsidy(subsidy);
            OnMunicipalSubsidyPaid?.Invoke(city, subsidy);
        }
    }

    /// <summary>
    /// Retorna a menor integridade de pavimento (0..100%) das arestas conectadas à célula.
    /// </summary>
    public float GetCellPavementCondition(int x, int y)
    {
        if (!IsTileInBounds(x, y))
            return 100f;

        float minCondition = 100f;
        var edges = GetEdgesTouchingTile(x, y);
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].PavementCondition < minCondition)
                minCondition = edges[i].PavementCondition;
        }
        return minCondition;
    }

    /// <summary>
    /// Indica se algum trecho conectado à célula está interditado por deslizamento.
    /// </summary>
    public bool IsCellBlockedByLandslide(int x, int y)
    {
        if (!IsTileInBounds(x, y))
            return false;

        var edges = GetEdgesTouchingTile(x, y);
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].IsBlocked)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Calcula o custo estimado de recapeamento/desobstrução manual para a célula viária.
    /// </summary>
    public long EstimateRepaveTileCost(int x, int y)
    {
        if (!IsTileInBounds(x, y) || !World.Grid[x, y].HasRoad)
            return 0L;

        var edges = GetEdgesTouchingTile(x, y);
        if (edges.Count == 0)
            return 0L;

        float minCond = 100f;
        bool hasBlocked = false;
        foreach (var edge in edges)
        {
            if (edge.PavementCondition < minCond) minCond = edge.PavementCondition;
            if (edge.IsBlocked) hasBlocked = true;
        }

        if (minCond >= 99.5f && !hasBlocked)
            return 0L;

        long baseTierCost = GetEdgeMaintenanceBaseCost(World.Grid[x, y].RoadLevel);
        long wearCost = Math.Max(20L, (long)Math.Round(baseTierCost * Math.Max(0.15f, (100f - minCond) / 100f) * LandmarkMaintenanceMultiplier));
        return hasBlocked ? wearCost + 250L : wearCost;
    }

    /// <summary>
    /// Recapeia manualmente um trecho viário (e libera eventual bloqueio de deslizamento no local).
    /// </summary>
    public bool TryRepaveRoadTile(int x, int y, out string message)
    {
        if (!IsTileInBounds(x, y) || !World.Grid[x, y].HasRoad)
        {
            message = "⚠️ Selecione uma célula com estrada válida.";
            return false;
        }

        var edges = GetEdgesTouchingTile(x, y);
        if (edges.Count == 0)
        {
            message = "⚠️ Nenhuma conexão viária encontrada nesta célula.";
            return false;
        }

        long cost = EstimateRepaveTileCost(x, y);
        if (cost <= 0)
        {
            message = "🛣️ Este trecho já está com pavimento 100% íntegro.";
            return false;
        }

        if (Player.Balance < cost)
        {
            message = $"❌ Saldo insuficiente para recapear (necessário R$ {cost:N0}).";
            return false;
        }

        Player.Balance -= cost;
        Financials.RecordRoadMaintenance(cost);

        bool clearedBlocked = false;
        foreach (var edge in edges)
        {
            edge.Repave();
            if (edge.IsBlocked)
            {
                edge.IsBlocked = false;
                clearedBlocked = true;
                if (edge.Id == BlockedLandslideEdgeId)
                {
                    BlockedLandslideEdgeId = 0;
                }
            }
        }

        if (clearedBlocked && ActiveSeasonalEvent == SeasonalEventType.MountainLandslide &&
            World.RoadEdges.All(e => !e.IsBlocked))
        {
            ClearActiveSeasonalEvent();
        }

        Pathfinder.InvalidateCache();
        OnRoadMaintained?.Invoke(1, cost);
        message = clearedBlocked
            ? $"🚧 Deslizamento liberado e trecho recapeado! (-R$ {cost:N0})"
            : $"🛠️ Trecho recapeado para 100%! (-R$ {cost:N0})";
        return true;
    }

    /// <summary>
    /// Executa conservação preventiva em trechos com pavimento abaixo de 65%.
    /// Retorna a quantidade de arestas restauradas.
    /// </summary>
    public int RunAutoRoadMaintenance(out long totalCost)
    {
        totalCost = 0L;
        int repairedEdges = 0;

        foreach (var edge in World.RoadEdges)
        {
            if (edge.PavementCondition >= 65f)
                continue;

            long baseCost = GetEdgeMaintenanceBaseCost(edge.Tier);
            long edgeCost = Math.Max(15L, (long)Math.Round(baseCost * ((100f - edge.PavementCondition) * 0.01f) * 0.45f * LandmarkMaintenanceMultiplier));
            if (Player.Balance < edgeCost)
                break;

            Player.Balance -= edgeCost;
            totalCost += edgeCost;
            edge.Repave();
            repairedEdges++;
        }

        if (repairedEdges > 0)
        {
            Pathfinder.InvalidateCache();
        }

        if (totalCost > 0)
        {
            Financials.RecordRoadMaintenance(totalCost);
            OnRoadMaintained?.Invoke(repairedEdges, totalCost);
        }

        return repairedEdges;
    }

    // =========================================================================
    // Fases 6, 7 e 8 — Interseções 3D, Pedágios, Concorrência IA e Lucro por Corredor
    // =========================================================================

    private void FinalizeFreightPaymentAndEffects(Truck truck, ResourceType deliveredType, int accepted, long freightReward, int xpReward)
    {
        if (accepted <= 0 || freightReward <= 0)
            return;

        long finalFreightReward = (long)Math.Round(freightReward * LandmarkFreightBonusMultiplier * Milestones.FreightAndTollBonusMultiplier);
        int finalXpReward = (int)Math.Round(xpReward * LandmarkXpMultiplier * Milestones.XpBonusMultiplier);

        Player.Balance += finalFreightReward;
        Player.AddXp(finalXpReward);
        Financials.RecordFreightRevenue(finalFreightReward);
        PlayerDeliveredVolume += accepted;

        if (truck.CurrentTripEdges.Count > 0)
        {
            long sharePerEdge = Math.Max(1L, finalFreightReward / truck.CurrentTripEdges.Count);
            foreach (var edge in truck.CurrentTripEdges)
            {
                edge.RecentRevenue30d += sharePerEdge;
            }
            CollectTollAndWeighBonusForTrip(truck);
            truck.ClearCurrentTripEdges();
        }

        OnDeliveryCompleted?.Invoke(truck, deliveredType, accepted, finalFreightReward);
    }

    private void RecordCityContractProgress(City targetCity, ResourceType deliveredType, int accepted)
    {
        foreach (var contract in ActiveContracts)
        {
            if (contract.TargetCity.Id == targetCity.Id && contract.RecordDelivery(deliveredType, accepted))
            {
                PlayerContractsCompleted++;
                long contractBonus = (long)Math.Round(contract.BonusRewardMoney * Milestones.ContractBonusMultiplier);
                Player.Balance += contractBonus;
                Player.AddXp(contract.BonusRewardXp);
                Financials.RecordContractBonus(contractBonus);
                OnContractCompleted?.Invoke(contract);
            }
        }
    }

    public const int TrafficControlUpgradeCost = 1500;

    private int CountAdjacentRoadTiles(int x, int y)
    {
        int w = World.Grid.GetLength(0);
        int h = World.Grid.GetLength(1);
        int count = 0;
        if (x + 1 < w && World.Grid[x + 1, y].HasRoad) count++;
        if (x - 1 >= 0 && World.Grid[x - 1, y].HasRoad) count++;
        if (y + 1 < h && World.Grid[x, y + 1].HasRoad) count++;
        if (y - 1 >= 0 && World.Grid[x, y - 1].HasRoad) count++;
        return count;
    }

    /// <summary>
    /// Retorna true se a célula for um entroncamento de 3 ou mais vias.
    /// </summary>
    public bool IsIntersectionCell(int x, int y)
    {
        if (!IsTileInBounds(x, y) || !World.Grid[x, y].HasRoad)
            return false;

        var exactNode = GetRoadNodeAt(x, y);
        if (exactNode != null && exactNode.IsIntersection)
            return true;

        return CountAdjacentRoadTiles(x, y) >= 3;
    }

    /// <summary>
    /// Retorna true se houver rotatória/semáforo inteligente instalado neste entroncamento.
    /// </summary>
    public bool HasTrafficControlAt(int x, int y)
    {
        var exactNode = GetRoadNodeAt(x, y);
        return exactNode != null && exactNode.HasTrafficControl;
    }

    /// <summary>
    /// Instala controle inteligente de tráfego (semáforo/rotatória 3D) em um cruzamento de 3+ vias.
    /// </summary>
    public bool TryBuildTrafficControl(int x, int y, out string message)
    {
        if (!IsIntersectionCell(x, y))
        {
            message = "⚠️ Selecione um cruzamento com 3 ou mais vias conectadas.";
            return false;
        }

        var node = GetRoadNodeAt(x, y)
                   ?? FindNearestRoadNode(x, y, maxManhattanDist: 1.5f);
        if (node == null)
        {
            message = "⚠️ Nó viário não localizado neste cruzamento.";
            return false;
        }

        if (node.HasTrafficControl)
        {
            message = "🚦 Este cruzamento já possui rotatória/semáforo inteligente.";
            return false;
        }

        if (Player.Balance < TrafficControlUpgradeCost)
        {
            message = $"❌ Saldo insuficiente para sinalizar cruzamento (R$ {TrafficControlUpgradeCost:N0}).";
            return false;
        }

        Player.Balance -= TrafficControlUpgradeCost;
        Financials.RecordRoadConstruction(TrafficControlUpgradeCost);
        node.HasTrafficControl = true;
        Pathfinder.InvalidateCache();
        OnTrafficControlBuilt?.Invoke(node);
        message = $"🚦 Rotatória & Semáforo 3D instalados em ({node.GridX}, {node.GridY})! (+4 fluxo sem engarrafamento)";
        return true;
    }

    private void RebuildTollLookupCache()
    {
        _tollByNodeId.Clear();
        foreach (var station in TollStations)
        {
            _tollByNodeId[station.AccessNode.Id] = station;
        }
    }

    /// <summary>
    /// Constrói uma Praça de Pedágio & Balança de Pesagem sobre uma rodovia asfaltada ou superior.
    /// </summary>
    public TollStation? BuildTollStation(int x, int y, out string message, string? customName = null)
    {
        if (!IsTileInBounds(x, y))
        {
            message = "⚠️ Coordenada fora dos limites do mapa.";
            return null;
        }

        ref var cell = ref World.Grid[x, y];
        if (!cell.HasRoad || cell.RoadLevel < RoadTier.Asphalt)
        {
            message = "⚠️ Pedágios exigem estrada pavimentada (Asfalto, Rodovia ou Pista Dupla).";
            return null;
        }

        if (cell.OccupantId != 0 || TollStations.Any(t => t.GridX == x && t.GridY == y))
        {
            message = "⚠️ Já existe uma estrutura ou pedágio nesta célula.";
            return null;
        }

        RoadNode? accessNode = GetRoadNodeAt(x, y)
                               ?? FindNearestRoadNode(x, y, maxManhattanDist: 8.0f);
        if (accessNode == null)
        {
            message = "⚠️ Nenhum nó rodoviário próximo encontrado.";
            return null;
        }

        if (Player.Balance < TollStation.ConstructionCost)
        {
            message = $"❌ Saldo insuficiente (Pedágio custa R$ {TollStation.ConstructionCost:N0}).";
            return null;
        }

        Player.Balance -= TollStation.ConstructionCost;
        Financials.RecordCapEx(TollStation.ConstructionCost);

        var station = new TollStation(
            _nextTollId++,
            customName ?? $"Pedágio & Balança #{TollStations.Count + 1}",
            x,
            y,
            accessNode);

        TollStations.Add(station);
        _tollByNodeId[accessNode.Id] = station;
        OnTollStationBuilt?.Invoke(station);

        message = $"🚧 {station.Name} inaugurado em ({x}, {y})! Receita regional + bônus de balança ativos.";
        return station;
    }

    private void CollectTollAndWeighBonusForTrip(Truck truck)
    {
        if (TollStations.Count == 0 || truck.CurrentTripEdges.Count == 0)
            return;

        int passedStationNodeId = -1;
        foreach (var edge in truck.CurrentTripEdges)
        {
            TollStation? station = null;
            if (_tollByNodeId.TryGetValue(edge.ToNode.Id, out var stTo))
                station = stTo;
            else if (_tollByNodeId.TryGetValue(edge.FromNode.Id, out var stFrom))
                station = stFrom;

            if (station == null || station.AccessNode.Id == passedStationNodeId)
                continue;

            passedStationNodeId = station.AccessNode.Id;
            long payout = station.TollFee;
            if (truck.Condition >= 80f)
            {
                payout += TollStation.WeighStationComplianceBonus;
                truck.Condition = MathF.Min(100f, truck.Condition + 0.5f);
            }
            payout = (long)Math.Round(payout * Milestones.FreightAndTollBonusMultiplier);

            station.TotalTollCollected += payout;
            station.TrucksInspected++;
            Player.Balance += payout;
            Financials.RecordTollRevenue(payout);
            OnTollCollected?.Invoke(station, payout);
            break; // 1 cobrança/bônus por viagem para manter economia equilibrada
        }
    }

    private void CollectDailyPassiveTolls()
    {
        if (TollStations.Count == 0)
            return;

        foreach (var station in TollStations)
        {
            RoadTier tier = IsTileInBounds(station.GridX, station.GridY)
                ? World.Grid[station.GridX, station.GridY].RoadLevel
                : RoadTier.Asphalt;
            long regionalTrafficToll = tier switch
            {
                RoadTier.DualCarriage => 320L,
                RoadTier.Highway => 240L,
                _ => 160L
            };

            station.TotalTollCollected += regionalTrafficToll;
            Player.Balance += regionalTrafficToll;
            Financials.RecordTollRevenue(regionalTrafficToll);
            OnTollCollected?.Invoke(station, regionalTrafficToll);
        }
    }

    private void DecayCorridorRevenues()
    {
        foreach (var edge in World.RoadEdges)
        {
            if (edge.RecentRevenue30d > 0)
            {
                edge.RecentRevenue30d = (long)(edge.RecentRevenue30d * 0.35);
            }
        }
    }

    /// <summary>
    /// Retorna a receita gerada nos últimos ~30 dias pelos corredores conectados a esta célula.
    /// </summary>
    public long GetCellCorridorRevenue(int x, int y)
    {
        if (!IsTileInBounds(x, y) || !World.Grid[x, y].HasRoad)
            return 0L;

        long maxEdgeRev = 0L;
        var edges = GetEdgesTouchingTile(x, y);
        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i].RecentRevenue30d > maxEdgeRev)
                maxEdgeRev = edges[i].RecentRevenue30d;
        }
        return maxEdgeRev;
    }

    internal void EnsureRivalsInitialized()
    {
        if (Rivals.Count > 0)
            return;

        Rivals.Add(new RivalCompany(1, "TransBrasil Express", "#f97316"));
        Rivals.Add(new RivalCompany(2, "SulCargo Logística", "#8b5cf6"));
    }

    private void ProcessRivalsDailyCycle()
    {
        if (Rivals.Count == 0)
            return;

        int scale = Math.Max(1, World.Cities.Count / 2);
        foreach (var rival in Rivals)
        {
            int dailyUnits = _rng.Next(6, 16) * scale;
            rival.DeliveredVolume += dailyUnits;
            rival.EstimatedRevenue += dailyUnits * 20L;
        }

        // Rivais disputam contratos expressos que estão expirando com pouco progresso do jogador
        for (int i = 0; i < ActiveContracts.Count; i++)
        {
            var contract = ActiveContracts[i];
            if (contract.IsCompleted || contract.IsExpired || contract.IsLostToRival)
                continue;

            if (contract.RemainingHours <= 72f && contract.CompletionPercentage < 0.25f && _rng.NextDouble() < 0.22)
            {
                var winner = Rivals[_rng.Next(Rivals.Count)];
                ClaimContractForRival(contract, winner);
            }
        }

        // Emite periodicamente contrato de Comboio Especial / Carga Indivisível (3x recompensa)
        bool hasActiveConvoy = ActiveContracts.Any(c => c.IsSpecialConvoy && !c.IsCompleted && !c.IsExpired && !c.IsLostToRival);
        if (!hasActiveConvoy && World.Cities.Count > 0 && _rng.NextDouble() < 0.45)
        {
            var convoyCity = World.Cities[_rng.Next(World.Cities.Count)];
            ResourceType[] heavyCargos = { ResourceType.Steel, ResourceType.Lumber, ResourceType.Goods, ResourceType.Food };
            ResourceType chosen = heavyCargos[_rng.Next(heavyCargos.Length)];
            CreateSpecialConvoyContract(convoyCity, chosen, 55);
        }
    }

    /// <summary>
    /// Permite à IA concorrente assumir um contrato expresso em aberto e emitir novo contrato no mercado.
    /// </summary>
    public bool ClaimContractForRival(LogisticsContract contract, RivalCompany rival)
    {
        if (contract.IsCompleted || contract.IsLostToRival)
            return false;

        contract.ClaimByRival(rival.Name);
        rival.CompletedContracts++;
        rival.DeliveredVolume += contract.RequiredAmount;
        rival.EstimatedRevenue += contract.BonusRewardMoney;
        OnContractLostToRival?.Invoke(contract, rival);

        if (World.Cities.Count > 0)
        {
            var nextCity = World.Cities[_rng.Next(World.Cities.Count)];
            CreateContractForCity(nextCity);
        }
        return true;
    }

    /// <summary>
    /// Calcula o ranking de participação de mercado (Market Share) entre a empresa do jogador e rivais IA.
    /// </summary>
    public List<MarketShareStanding> GetMarketShareStandings()
    {
        EnsureRivalsInitialized();
        long totalVolume = Math.Max(1L, PlayerDeliveredVolume + Rivals.Sum(r => (long)r.DeliveredVolume));

        var list = new List<MarketShareStanding>
        {
            new MarketShareStanding(
                "Sua Transportadora",
                IsPlayer: true,
                "#22c55e",
                PlayerDeliveredVolume,
                PlayerContractsCompleted,
                (PlayerDeliveredVolume * 100f) / totalVolume)
        };

        foreach (var rival in Rivals)
        {
            list.Add(new MarketShareStanding(
                rival.Name,
                IsPlayer: false,
                rival.ColorHex,
                rival.DeliveredVolume,
                rival.CompletedContracts,
                (rival.DeliveredVolume * 100f) / totalVolume));
        }

        list.Sort((a, b) => b.DeliveredVolume.CompareTo(a.DeliveredVolume));
        return list;
    }

    // =========================================================================
    // Sprint E4 — Frota Rival Visível, Tráfego Civil & Balança/Comboio 3D
    // =========================================================================

    private void RefreshTruckSpecialConvoyState(Truck truck)
    {
        if (truck.LoadedCargoAmount <= 0 || truck.LoadedCargoType == ResourceType.None)
        {
            truck.IsSpecialConvoyActive = false;
            return;
        }

        truck.IsSpecialConvoyActive = ActiveContracts.Any(c =>
            c.IsSpecialConvoy &&
            !c.IsCompleted &&
            !c.IsExpired &&
            !c.IsLostToRival &&
            c.Resource == truck.LoadedCargoType);
    }

    private void UpdatePlayerTruckWeighboard(TollStation station, Truck truck)
    {
        int baseTareTons = truck.Spec.Capacity switch
        {
            <= 15 => 6,
            <= 30 => 14,
            <= 55 => 18,
            <= 95 => 26,
            <= 165 => 34,
            _ => 42
        };
        int grossTons = baseTareTons + Math.Max(0, truck.LoadedCargoAmount / 3);
        string tag = truck.Spec.Capacity > 165
            ? "RODOTREM"
            : (truck.IsSpecialConvoyActive ? "ESCOLTA 3X" : (truck.Condition >= 80f ? "OK" : "ALERTA"));
        string msg = $"{grossTons}t {tag}";
        station.LastWeighDisplay = msg;
        OnTollWeighUpdated?.Invoke(station, msg, 0L);
    }

    private void ProcessNpcTraffic(float deltaHours)
    {
        EnsureRivalsInitialized();

        // 1. Avança veículos NPC ativos e processa cobrança de pedágio real de rivais + balança
        for (int i = TrafficVehicles.Count - 1; i >= 0; i--)
        {
            var npc = TrafficVehicles[i];
            bool finished = npc.Advance(deltaHours, CurrentWeather, IsWinterBlizzardActive, out RoadNode? crossedNode);

            if (crossedNode != null && _tollByNodeId.TryGetValue(crossedNode.Id, out var station))
            {
                HandleNpcVehicleTollAndWeigh(station, npc, crossedNode.Id);
            }

            if (finished || npc.IsFinished)
            {
                npc.ReleaseCurrentEdgeOccupancy();
                TrafficVehicles.RemoveAt(i);
            }
        }

        // 2. Gera tráfego NPC continuamente proporcional à população das cidades e malha conectada
        if (World.RoadEdges.Count < 4 || World.Cities.Count < 2)
            return;

        _trafficSpawnAccumulator += deltaHours;
        if (_trafficSpawnAccumulator < 0.08f)
            return;
        _trafficSpawnAccumulator = 0f;

        int totalPop = World.Cities.Sum(c => c.Population);
        int civilBudget = Math.Clamp(3 + totalPop / 110, 4, 18);
        int rivalBudget = Math.Min(4, Rivals.Count * 2);

        int currentCivil = TrafficVehicles.Count(v => v.Kind != TrafficVehicleKind.RivalTruck);
        int currentRival = TrafficVehicles.Count(v => v.Kind == TrafficVehicleKind.RivalTruck);

        if (currentCivil < civilBudget)
        {
            var kind = _rng.NextDouble() < 0.28 ? TrafficVehicleKind.IntercityBus : TrafficVehicleKind.CivilCar;
            SpawnCivilVehicle(kind);
        }
        if (currentRival < rivalBudget && Rivals.Count > 0)
        {
            var rival = Rivals[_rng.Next(Rivals.Count)];
            SpawnRivalTruck(rival);
        }
    }

    private void HandleNpcVehicleTollAndWeigh(TollStation station, TrafficVehicle npc, int crossedNodeId)
    {
        if (npc.Kind == TrafficVehicleKind.RivalTruck && npc.LastPaidTollNodeId != crossedNodeId)
        {
            npc.LastPaidTollNodeId = crossedNodeId;
            long payout = station.TollFee;
            station.TotalTollCollected += payout;
            station.TrucksInspected++;
            Player.Balance += payout;
            Financials.RecordTollRevenue(payout);
            station.LastWeighDisplay = $"{(int)npc.EstimatedWeightTons}t RIVAL OK";
            OnTollCollected?.Invoke(station, payout);
            OnTollWeighUpdated?.Invoke(station, station.LastWeighDisplay, payout);
            return;
        }

        if (npc.Kind == TrafficVehicleKind.IntercityBus)
        {
            station.LastWeighDisplay = $"{(int)npc.EstimatedWeightTons}t ÔNIBUS OK";
            OnTollWeighUpdated?.Invoke(station, station.LastWeighDisplay, 0L);
        }
        else if (npc.Kind == TrafficVehicleKind.CivilCar)
        {
            station.LastWeighDisplay = $"{(int)npc.EstimatedWeightTons}t PASSEIO";
            OnTollWeighUpdated?.Invoke(station, station.LastWeighDisplay, 0L);
        }
    }

    /// <summary>
    /// Coloca um caminhão físico de uma empresa rival circulando entre dois nós/cidades conectados.
    /// </summary>
    public TrafficVehicle? SpawnRivalTruck(RivalCompany rival, RoadNode? origin = null, RoadNode? destination = null)
    {
        if (!TryResolveNpcRoute(origin, destination, out var startNode, out var endNode, out var path))
            return null;

        float weightTons = _rng.Next(32, 49);
        var npc = new TrafficVehicle(
            _nextTrafficVehicleId++,
            TrafficVehicleKind.RivalTruck,
            rival.Name,
            rival.ColorHex,
            "#f8fafc",
            cruiseSpeedKmh: 76f,
            estimatedWeightTons: weightTons,
            rivalCompanyId: rival.Id)
        {
            OriginNode = startNode,
            DestinationNode = endNode
        };
        npc.AssignPath(path);
        TrafficVehicles.Add(npc);
        return npc;
    }

    private static readonly string[] CivilCarColors =
    {
        "#ef4444", "#3b82f6", "#eab308", "#f8fafc", "#10b981", "#64748b", "#ec4899"
    };

    /// <summary>
    /// Gera um veículo civil (carro de passeio ou ônibus intermunicipal) em rota entre cidades conectadas.
    /// </summary>
    public TrafficVehicle? SpawnCivilVehicle(TrafficVehicleKind kind, RoadNode? origin = null, RoadNode? destination = null)
    {
        TrafficVehicleKind actualKind = kind == TrafficVehicleKind.RivalTruck ? TrafficVehicleKind.CivilCar : kind;
        if (!TryResolveNpcRoute(origin, destination, out var startNode, out var endNode, out var path))
            return null;

        bool isBus = actualKind == TrafficVehicleKind.IntercityBus;
        string primary = isBus ? "#0284c7" : CivilCarColors[_rng.Next(CivilCarColors.Length)];
        string stripe = isBus ? "#facc15" : "#e2e8f0";
        float speed = isBus ? 72f : 86f;
        float tons = isBus ? 16f : 2f;

        var npc = new TrafficVehicle(
            _nextTrafficVehicleId++,
            actualKind,
            isBus ? "Expresso Intermunicipal" : "Veículo Civil",
            primary,
            stripe,
            speed,
            tons)
        {
            OriginNode = startNode,
            DestinationNode = endNode
        };
        npc.AssignPath(path);
        TrafficVehicles.Add(npc);
        return npc;
    }

    private bool TryResolveNpcRoute(
        RoadNode? origin,
        RoadNode? destination,
        out RoadNode startNode,
        out RoadNode endNode,
        out List<RoadEdge> path)
    {
        startNode = origin!;
        endNode = destination!;
        path = new List<RoadEdge>();

        if (origin == null || destination == null)
        {
            if (World.Cities.Count < 2 || World.RoadNodes.Count < 2)
                return false;

            int idxA = _rng.Next(World.Cities.Count);
            int idxB = (idxA + 1 + _rng.Next(World.Cities.Count - 1)) % World.Cities.Count;
            var cA = World.Cities[idxA];
            var cB = World.Cities[idxB];
            origin ??= FindNearestRoadNode(cA.CenterX, cA.CenterY, maxManhattanDist: 5f);
            destination ??= FindNearestRoadNode(cB.CenterX, cB.CenterY, maxManhattanDist: 5f);
        }

        if (origin == null || destination == null || origin.Id == destination.Id)
            return false;

        var found = Pathfinder.FindFastestRoute(origin, destination, CurrentWeather, false, IsWinterBlizzardActive);
        if (found == null || found.Count == 0)
            return false;

        startNode = origin;
        endNode = destination;
        path = found;
        return true;
    }
}

public enum MapOverlayMode
{
    Standard,
    Traffic,
    Pavement,
    Profitability
}

public enum DemolishTargetKind
{
    None,
    Road,
    Garage,
    LogisticsHub,
    TollStation,
    BusTerminal
}

public readonly record struct DemolishResult(
    DemolishTargetKind Kind,
    int X,
    int Y,
    int RemovedEntityId,
    RoadStructureType RemovedStructureType,
    long RefundAmount);

public sealed record RoadBuildPlan(
    IReadOnlyList<(int X, int Y)> PathTiles,
    IReadOnlyList<(int X, int Y)> TilesToBuild,
    int BridgesCount,
    int TunnelsCount,
    long TotalCost,
    bool CanAfford,
    bool IsValid);

public readonly record struct IndustrialConnections(
    bool LinkN,
    bool LinkS,
    bool LinkE,
    bool LinkW,
    bool HeavyBridgeN,
    bool HeavyBridgeS,
    bool HeavyBridgeE,
    bool HeavyBridgeW,
    int RoadDockCount,
    int FacilityBridgeCount,
    int NearbyHubCount,
    int DailyProductionBonus);
