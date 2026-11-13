using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

public enum TruckStatus
{
    Idle,
    ParkedInGarage,
    TravelingToPickup,
    Loading,
    WaitingForCargo,
    TravelingToDelivery,
    Unloading,
    TravelingToPitStop
}

/// <summary>
/// Modelo/especificação de caminhão.
/// </summary>
public record TruckSpecification(
    string Name,
    int Capacity,
    float MaxTopSpeedKmh,
    int PurchasePrice,
    int RunningCostPerKm,
    int RequiredPlayerLevel,
    bool RequiresHighwayOrDualCarriage = false,
    bool IsPassengerBus = false
);

/// <summary>
/// Instância de um caminhão em operação.
/// </summary>
public class Truck
{
    public int Id { get; }
    public string Name { get; }
    public TruckSpecification Spec { get; }
    
    public ResourceType LoadedCargoType { get; set; } = ResourceType.None;
    public int LoadedCargoAmount { get; set; } = 0;
    public bool IsSpecialConvoyActive { get; set; } = false;
    
    public TruckStatus Status { get; set; } = TruckStatus.ParkedInGarage;

    // Estado Mecânico e Histórico de Operação
    public float Condition { get; set; } = 100f; // 0% a 100%
    public float TotalDistanceTravelledKm { get; private set; } = 0f;
    public float CurrentTripDistanceKm { get; private set; } = 0f;

    public const float VeteranKmThreshold = 50f;
    public const float RoadLegendKmThreshold = 150f;

    public TruckRank Rank => TotalDistanceTravelledKm >= RoadLegendKmThreshold
        ? TruckRank.RoadLegend
        : (TotalDistanceTravelledKm >= VeteranKmThreshold ? TruckRank.Veteran : TruckRank.Rookie);

    public float FuelCostMultiplier => Rank switch
    {
        TruckRank.RoadLegend => 0.90f, // +10% economia de combustível
        TruckRank.Veteran => 0.95f,    // +5% economia de combustível
        _ => 1.00f
    };

    public float HillClimbSpeedMultiplier => Rank switch
    {
        TruckRank.RoadLegend => 1.12f, // +12% velocidade em subida/serra
        TruckRank.Veteran => 1.06f,    // +6% velocidade em subida/serra
        _ => 1.00f
    };

    public string RankDisplayLabel => Rank switch
    {
        TruckRank.RoadLegend => "★★★ Lenda da Estrada",
        TruckRank.Veteran => "★★ Veterano",
        _ => "★ Novato"
    };

    // Destinos de Linha Logística (Automática, Agenda Multi-Paradas ou Linha Compartilhada)
    public int? AssignedLineId { get; set; }
    public bool HasManualSchedule { get; set; } = false;
    public List<ScheduleStop> Schedule { get; } = new();
    public int CurrentScheduleIndex { get; set; } = 0;
    public Industry? AssignedOriginIndustry { get; set; }
    public Industry? AssignedTargetIndustry { get; set; }
    public City? AssignedTargetCity { get; set; }

    // Manutenção Preditiva & Pit-Stop Automático
    public bool AutoPitStopEnabled { get; set; } = true;
    public bool IsDivertedForPitStop { get; set; } = false;
    public ScheduleStopKind? PitStopTargetKind { get; set; }
    public int? PitStopTargetId { get; set; }
    public bool LoadedFromHub { get; set; } = false;
    public float CurrentStopWaitHours { get; set; } = 0f;
    public bool IsHeldForHeadway { get; set; } = false;
    
    // Rota e Navegação
    public RoadNode? LastNode { get; internal set; }
    public RoadNode? CurrentDestinationNode { get; internal set; }
    public RoadEdge? CurrentEdge { get; set; }
    public float DistanceAlongCurrentEdgeKm { get; set; }
    public Queue<RoadEdge> RemainingPath { get; } = new();
    private readonly List<RoadEdge> _currentTripEdges = new();
    public IReadOnlyList<RoadEdge> CurrentTripEdges => _currentTripEdges;

    public Truck(int id, string name, TruckSpecification spec)
    {
        Id = id;
        Name = name;
        Spec = spec;
    }

    public void ClearCurrentTripEdges() => _currentTripEdges.Clear();

    public void AssignPath(IEnumerable<RoadEdge> path)
    {
        if (CurrentEdge != null)
        {
            CurrentEdge.ActiveTrucksCount = System.Math.Max(0, CurrentEdge.ActiveTrucksCount - 1);
        }

        RemainingPath.Clear();
        _currentTripEdges.Clear();
        CurrentTripDistanceKm = 0f;
        foreach (var edge in path)
        {
            RemainingPath.Enqueue(edge);
            _currentTripEdges.Add(edge);
            CurrentTripDistanceKm += edge.DistanceKm;
        }

        if (RemainingPath.Count > 0)
        {
            CurrentEdge = RemainingPath.Dequeue();
            DistanceAlongCurrentEdgeKm = 0f;
            LastNode = CurrentEdge.FromNode;
            CurrentEdge.ActiveTrucksCount++;
        }
    }

    /// <summary>
    /// Define uma agenda multi-paradas para este veículo.
    /// </summary>
    public void SetSchedule(IEnumerable<ScheduleStop> stops)
    {
        Schedule.Clear();
        foreach (var stop in stops)
        {
            Schedule.Add(stop.Clone());
        }
        CurrentScheduleIndex = 0;
        HasManualSchedule = Schedule.Count > 0;
    }

    public void ClearSchedule()
    {
        Schedule.Clear();
        CurrentScheduleIndex = 0;
        HasManualSchedule = false;
    }

    public ScheduleStop? GetCurrentStop()
    {
        if (Schedule.Count == 0) return null;
        if (CurrentScheduleIndex < 0 || CurrentScheduleIndex >= Schedule.Count)
            CurrentScheduleIndex = 0;
        return Schedule[CurrentScheduleIndex];
    }

    public void AdvanceScheduleStop()
    {
        if (Schedule.Count == 0) return;
        CurrentScheduleIndex = (CurrentScheduleIndex + 1) % Schedule.Count;
    }

    public int GetRequiredCargoThreshold(DispatchCondition condition) => condition switch
    {
        DispatchCondition.WaitFullLoad => Spec.Capacity,
        DispatchCondition.WaitHalfLoad => System.Math.Max(1, (Spec.Capacity + 1) / 2),
        _ => 0
    };

    /// <summary>
    /// Restaura a condição mecânica do caminhão a 100%.
    /// </summary>
    public void Repair()
    {
        Condition = 100f;
    }

    internal void RestoreNavigationState(
        float totalDistanceKm,
        float currentTripDistanceKm,
        RoadEdge? currentEdge,
        float distanceAlongEdgeKm,
        IEnumerable<RoadEdge> remainingPath)
    {
        TotalDistanceTravelledKm = totalDistanceKm;
        CurrentTripDistanceKm = currentTripDistanceKm;
        CurrentEdge = currentEdge;
        DistanceAlongCurrentEdgeKm = distanceAlongEdgeKm;
        RemainingPath.Clear();
        foreach (var edge in remainingPath)
            RemainingPath.Enqueue(edge);

        if (CurrentEdge != null)
        {
            CurrentEdge.ActiveTrucksCount++;
            LastNode = CurrentEdge.FromNode;
        }
    }

    /// <summary>
    /// Avança o caminhão ao longo da estrada com base no deltaTime (em horas), clima, nevasca e multiplicador de motor.
    /// </summary>
    public bool Advance(
        float deltaTimeHours,
        WeatherCondition weather = WeatherCondition.Clear,
        float fleetSpeedMultiplier = 1.0f,
        bool winterBlizzard = false,
        float hillClimbBonusMultiplier = 1.0f)
    {
        if (CurrentEdge == null) return false;

        float roadEffectiveSpeed = CurrentEdge.GetEffectiveSpeedLimitKmh(weather, winterBlizzard);
        if (Spec.RequiresHighwayOrDualCarriage && CurrentEdge.Tier < RoadTier.Highway)
        {
            // Comboio Rodotrem sofre restrição severa fora de Rodovias e Pistas Duplas
            roadEffectiveSpeed *= 0.55f;
        }
        float effectiveSpeed = System.MathF.Min(Spec.MaxTopSpeedKmh, roadEffectiveSpeed) * fleetSpeedMultiplier;
        if (CurrentEdge.IsHillOrMountain || CurrentEdge.IsAlpineBiome)
        {
            effectiveSpeed *= HillClimbSpeedMultiplier * hillClimbBonusMultiplier;
        }

        // Penalidade de desgaste mecânico caso a manutenção esteja crítica (< 30%)
        if (Condition < 30f)
        {
            float mechanicalFactor = System.MathF.Max(0.45f, Condition / 30f);
            effectiveSpeed *= mechanicalFactor;
        }

        float distanceTravelled = effectiveSpeed * deltaTimeHours;
        DistanceAlongCurrentEdgeKm += distanceTravelled;
        TotalDistanceTravelledKm += distanceTravelled;
        CurrentEdge.ApplyTrafficWear(distanceTravelled, Spec.Capacity);

        // Desgaste mecânico maior em estradas de terra
        float wearRatePerKm = CurrentEdge.Tier == RoadTier.Dirt ? 0.12f : (CurrentEdge.Tier == RoadTier.Gravel ? 0.08f : 0.04f);
        Condition = System.MathF.Max(10f, Condition - (distanceTravelled * wearRatePerKm));

        if (DistanceAlongCurrentEdgeKm >= CurrentEdge.DistanceKm)
        {
            // Chegou ao final da aresta atual
            LastNode = CurrentEdge.ToNode;
            CurrentEdge.ActiveTrucksCount = System.Math.Max(0, CurrentEdge.ActiveTrucksCount - 1);

            if (RemainingPath.Count > 0)
            {
                CurrentEdge = RemainingPath.Dequeue();
                DistanceAlongCurrentEdgeKm = 0f;
                CurrentEdge.ActiveTrucksCount++;
                return false; // Continua navegando
            }
            else
            {
                CurrentEdge = null;
                CurrentDestinationNode = null;
                return true; // Chegou ao destino final da rota
            }
        }

        return false;
    }
}

