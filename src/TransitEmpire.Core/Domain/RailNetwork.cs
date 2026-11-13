using System;
using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Tipo de obra de engenharia de um trilho ferroviário em grade.
/// </summary>
public enum RailStructureType
{
    Ground = 0,
    Viaduct = 1,
    Tunnel = 2
}

public static class RailStructureExtensions
{
    public static int GetRailConstructionCost(this RailStructureType structure) => structure switch
    {
        RailStructureType.Viaduct => 950,
        RailStructureType.Tunnel => 1350,
        _ => 180
    };

    public static string GetDisplayName(this RailStructureType structure) => structure switch
    {
        RailStructureType.Viaduct => "Viaduto Ferroviário",
        RailStructureType.Tunnel => "Túnel Ferroviário",
        _ => "Via Férrea em Solo"
    };

    public static RailStructureType FromTerrain(TerrainType terrain) => terrain switch
    {
        TerrainType.Water => RailStructureType.Viaduct,
        TerrainType.Hills or TerrainType.Mountain => RailStructureType.Tunnel,
        _ => RailStructureType.Ground
    };
}

/// <summary>
/// Especialização automática do vagão de carga conforme a commodity transportada.
/// </summary>
public enum RailWagonCategory
{
    GeneralFreight = 0,
    MineralHopper = 1,
    GrainHopper = 2,
    TimberFlatbed = 3,
    ContainerIntermodal = 4
}

public static class RailWagonHelper
{
    public static RailWagonCategory FromResource(ResourceType cargo) => cargo switch
    {
        ResourceType.IronOre or ResourceType.Coal => RailWagonCategory.MineralHopper,
        ResourceType.Grain or ResourceType.Food => RailWagonCategory.GrainHopper,
        ResourceType.Timber or ResourceType.Lumber => RailWagonCategory.TimberFlatbed,
        ResourceType.Goods or ResourceType.Steel or ResourceType.Fuel or ResourceType.Machinery
            => RailWagonCategory.ContainerIntermodal,
        _ => RailWagonCategory.GeneralFreight
    };

    public static float GetRevenueMultiplier(RailWagonCategory category, ResourceType cargo)
    {
        if (cargo == ResourceType.None)
            return 1.0f;

        return FromResource(cargo) == category ? 1.22f : 1.0f;
    }

    public static string GetDisplayName(RailWagonCategory category) => category switch
    {
        RailWagonCategory.MineralHopper => "Gôndola/Hopper Mineral",
        RailWagonCategory.GrainHopper => "Graneleiro de Safra",
        RailWagonCategory.TimberFlatbed => "Prancha Florestal",
        RailWagonCategory.ContainerIntermodal => "Plataforma Contêiner",
        _ => "Vagão Carga Geral"
    };
}

/// <summary>
/// Aspecto operacional do sinaleiro de bloco ferroviário.
/// </summary>
public enum RailSignalAspect
{
    Green = 0,
    Red = 1
}

/// <summary>
/// Nó da topologia ferroviária representando cruzamentos, desvios (passing sidings) e sinaleiros.
/// </summary>
public class RailNode
{
    public int Id { get; }
    public int GridX { get; }
    public int GridY { get; }
    public bool HasBlockSignal { get; set; }
    public bool IsSidingSwitch => OutgoingEdges.Count >= 3;
    public List<RailEdge> OutgoingEdges { get; } = new();

    public RailNode(int id, int gridX, int gridY, bool hasBlockSignal = false)
    {
        Id = id;
        GridX = gridX;
        GridY = gridY;
        HasBlockSignal = hasBlockSignal;
    }
}

/// <summary>
/// Segmento direcional entre dois nós ferroviários com grupo de bloco para sinalização segura.
/// </summary>
public class RailEdge
{
    public int Id { get; }
    public RailNode FromNode { get; }
    public RailNode ToNode { get; }
    public float DistanceKm { get; }
    public RailStructureType Structure { get; }
    public int BlockGroupId { get; set; } = 1;

    public float SpeedLimitKmh => Structure switch
    {
        RailStructureType.Tunnel => 85f,
        RailStructureType.Viaduct => 95f,
        _ => 112f
    };

    public RailEdge(
        int id,
        RailNode fromNode,
        RailNode toNode,
        float distanceKm,
        RailStructureType structure = RailStructureType.Ground)
    {
        Id = id;
        FromNode = fromNode;
        ToNode = toNode;
        DistanceKm = distanceKm;
        Structure = structure;
    }

    public float EstimateTravelTimeHours(float locomotiveMaxSpeedKmh)
    {
        float effectiveSpeed = MathF.Max(20f, MathF.Min(locomotiveMaxSpeedKmh, SpeedLimitKmh));
        return DistanceKm / effectiveSpeed;
    }
}

/// <summary>
/// Especificação técnica de locomotivas disponíveis no catálogo ferroviário.
/// </summary>
public record LocomotiveSpec(
    string Name,
    int MaxWagons,
    int CapacityPerWagon,
    float MaxSpeedKmh,
    long PurchasePrice,
    long RunningCostPerKm,
    int RequiredLevel
);

/// <summary>
/// Catálogo oficial de locomotivas de carga pesada do Sprint F5.
/// </summary>
public static class RailCatalog
{
    public static readonly IReadOnlyList<LocomotiveSpec> AvailableLocomotives = new List<LocomotiveSpec>
    {
        new(
            Name: "EMD SD40-2 Ferrovia",
            MaxWagons: 4,
            CapacityPerWagon: 35,
            MaxSpeedKmh: 88f,
            PurchasePrice: 22_000,
            RunningCostPerKm: 18,
            RequiredLevel: 1
        ),
        new(
            Name: "GE AC44i Minério Express",
            MaxWagons: 6,
            CapacityPerWagon: 50,
            MaxSpeedKmh: 100f,
            PurchasePrice: 48_000,
            RunningCostPerKm: 26,
            RequiredLevel: 2
        ),
        new(
            Name: "VLI Multimodal Heavy",
            MaxWagons: 8,
            CapacityPerWagon: 65,
            MaxSpeedKmh: 115f,
            PurchasePrice: 82_000,
            RunningCostPerKm: 34,
            RequiredLevel: 3
        )
    };
}

/// <summary>
/// Parada de uma composição ferroviária em um Pátio Rodoferroviário (Intermodal).
/// </summary>
public class IntermodalStop
{
    public int TerminalId { get; set; }
    public ScheduleAction Action { get; set; }
    public ResourceType CargoFilter { get; set; } = ResourceType.None;

    public IntermodalStop()
    {
    }

    public IntermodalStop(int terminalId, ScheduleAction action, ResourceType cargoFilter = ResourceType.None)
    {
        TerminalId = terminalId;
        Action = action;
        CargoFilter = cargoFilter;
    }

    public IntermodalStop Clone() => new(TerminalId, Action, CargoFilter);
}

public enum TrainStatus
{
    Idle = 0,
    TravelingToPickup = 1,
    Loading = 2,
    TravelingToDelivery = 3,
    Unloading = 4,
    WaitingAtSignal = 5
}

/// <summary>
/// Composição ferroviária composta por locomotiva e vagões especializados de carga massiva.
/// </summary>
public class Train
{
    public int Id { get; }
    public string Name { get; set; }
    public LocomotiveSpec Locomotive { get; }
    public int WagonCount { get; set; }
    public int TotalCapacity => Math.Max(1, WagonCount) * Locomotive.CapacityPerWagon;
    public TrainStatus Status { get; set; } = TrainStatus.Idle;

    public ResourceType LoadedCargoType { get; set; } = ResourceType.None;
    public int LoadedCargoAmount { get; set; }
    public RailWagonCategory WagonCategory { get; set; } = RailWagonCategory.GeneralFreight;

    public RailNode? LastNode { get; set; }
    public RailEdge? CurrentEdge { get; set; }
    public float DistanceAlongCurrentEdgeKm { get; set; }
    public Queue<RailEdge> RemainingPath { get; } = new();
    public int BlockedBySignalAtNodeId { get; set; }

    public List<IntermodalStop> Stops { get; } = new();
    public int CurrentStopIndex { get; set; }

    public float CurrentTripDistanceKm { get; set; }
    public float TotalDistanceTravelledKm { get; set; }
    public int TotalTonnageDelivered { get; set; }
    public long TotalRevenueEarned { get; set; }
    public long LastYieldRevenue { get; set; }

    public Train(int id, string name, LocomotiveSpec locomotive, int wagonCount)
    {
        Id = id;
        Name = name;
        Locomotive = locomotive;
        WagonCount = Math.Clamp(wagonCount, 1, locomotive.MaxWagons);
    }

    public IntermodalStop? GetCurrentStop()
    {
        if (Stops.Count == 0)
            return null;

        int idx = ((CurrentStopIndex % Stops.Count) + Stops.Count) % Stops.Count;
        return Stops[idx];
    }

    public void AdvanceStop()
    {
        if (Stops.Count == 0)
            return;

        CurrentStopIndex = (CurrentStopIndex + 1) % Stops.Count;
    }

    public void AssignPath(IEnumerable<RailEdge> edges)
    {
        RemainingPath.Clear();
        foreach (var edge in edges)
        {
            RemainingPath.Enqueue(edge);
        }

        CurrentEdge = null;
        DistanceAlongCurrentEdgeKm = 0f;
    }
}

/// <summary>
/// Pátio Rodoferroviário (Intermodal Hub) que conecta caminhões, composições ferroviárias e portos marítimos.
/// </summary>
public class IntermodalTerminal
{
    public const long ConstructionCost = 18_000;
    public const int DefaultStorageCapacity = 1_500;

    public int Id { get; }
    public string Name { get; set; }
    public int GridX { get; }
    public int GridY { get; }
    public RailNode? RailAccessNode { get; set; }
    public RoadNode? RoadAccessNode { get; set; }
    public int? LinkedHubId { get; set; }
    public int? LinkedSeaportId { get; set; }
    public int MaxStorageCapacity { get; } = DefaultStorageCapacity;
    public Dictionary<ResourceType, int> StoredCargo { get; } = new();
    public int TotalTransshippedUnits { get; set; }

    public int TotalStoredUnits
    {
        get
        {
            int sum = 0;
            foreach (var v in StoredCargo.Values)
                sum += v;
            return sum;
        }
    }

    public IntermodalTerminal(
        int id,
        string name,
        int gridX,
        int gridY,
        RailNode? railAccessNode = null,
        RoadNode? roadAccessNode = null)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        RailAccessNode = railAccessNode;
        RoadAccessNode = roadAccessNode;
    }

    public int StoreCargo(ResourceType type, int amount)
    {
        if (type == ResourceType.None || amount <= 0)
            return 0;

        int space = MaxStorageCapacity - TotalStoredUnits;
        int accepted = Math.Min(amount, Math.Max(0, space));
        if (accepted <= 0)
            return 0;

        StoredCargo.TryGetValue(type, out int current);
        StoredCargo[type] = current + accepted;
        return accepted;
    }

    public int WithdrawCargo(ResourceType type, int amount)
    {
        if (type == ResourceType.None || amount <= 0)
            return 0;

        if (!StoredCargo.TryGetValue(type, out int current) || current <= 0)
            return 0;

        int taken = Math.Min(amount, current);
        int remaining = current - taken;
        if (remaining > 0)
            StoredCargo[type] = remaining;
        else
            StoredCargo.Remove(type);
        return taken;
    }
}

