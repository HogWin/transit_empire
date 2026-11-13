using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

public enum TrafficVehicleKind
{
    RivalTruck = 0,
    CivilCar = 1,
    IntercityBus = 2
}

/// <summary>
/// Veículo NPC ativo na malha viária (caminhões de concorrentes IA, carros de passeio e ônibus intermunicipais).
/// Ocupa faixa na via (ActiveTrucksCount), responde ao clima e pode acionar cancelas/pedágios reais.
/// </summary>
public class TrafficVehicle
{
    public int Id { get; }
    public TrafficVehicleKind Kind { get; }
    public string Label { get; }
    public string PrimaryColorHex { get; }
    public string StripeColorHex { get; }
    public int? RivalCompanyId { get; }
    public float CruiseSpeedKmh { get; }
    public float EstimatedWeightTons { get; }

    public RoadNode? OriginNode { get; set; }
    public RoadNode? DestinationNode { get; set; }
    public RoadEdge? CurrentEdge { get; private set; }
    public float DistanceAlongCurrentEdgeKm { get; private set; }
    public Queue<RoadEdge> RemainingPath { get; } = new();
    public int LastPaidTollNodeId { get; set; } = -1;

    public bool IsFinished => CurrentEdge == null && RemainingPath.Count == 0;

    public TrafficVehicle(
        int id,
        TrafficVehicleKind kind,
        string label,
        string primaryColorHex,
        string stripeColorHex,
        float cruiseSpeedKmh,
        float estimatedWeightTons,
        int? rivalCompanyId = null)
    {
        Id = id;
        Kind = kind;
        Label = label;
        PrimaryColorHex = primaryColorHex;
        StripeColorHex = stripeColorHex;
        CruiseSpeedKmh = cruiseSpeedKmh;
        EstimatedWeightTons = estimatedWeightTons;
        RivalCompanyId = rivalCompanyId;
    }

    public void AssignPath(IEnumerable<RoadEdge> path)
    {
        ReleaseCurrentEdgeOccupancy();
        RemainingPath.Clear();
        foreach (var edge in path)
            RemainingPath.Enqueue(edge);

        if (RemainingPath.Count > 0)
        {
            CurrentEdge = RemainingPath.Dequeue();
            DistanceAlongCurrentEdgeKm = 0f;
            CurrentEdge.ActiveTrucksCount++;
        }
    }

    public void ReleaseCurrentEdgeOccupancy()
    {
        if (CurrentEdge != null)
        {
            CurrentEdge.ActiveTrucksCount = System.Math.Max(0, CurrentEdge.ActiveTrucksCount - 1);
            CurrentEdge = null;
        }
        RemainingPath.Clear();
    }

    /// <summary>
    /// Avança o veículo NPC ao longo da estrada e informa caso tenha cruzado um nó viário nesta etapa.
    /// </summary>
    public bool Advance(
        float deltaTimeHours,
        WeatherCondition weather,
        bool winterBlizzard,
        out RoadNode? crossedNode)
    {
        crossedNode = null;
        if (CurrentEdge == null)
            return false;

        float effectiveLimit = CurrentEdge.GetEffectiveSpeedLimitKmh(weather, winterBlizzard);
        float speed = System.MathF.Min(CruiseSpeedKmh, effectiveLimit);
        DistanceAlongCurrentEdgeKm += speed * deltaTimeHours;

        if (DistanceAlongCurrentEdgeKm >= CurrentEdge.DistanceKm)
        {
            crossedNode = CurrentEdge.ToNode;
            CurrentEdge.ActiveTrucksCount = System.Math.Max(0, CurrentEdge.ActiveTrucksCount - 1);

            if (RemainingPath.Count > 0)
            {
                CurrentEdge = RemainingPath.Dequeue();
                DistanceAlongCurrentEdgeKm = 0f;
                CurrentEdge.ActiveTrucksCount++;
                return false;
            }

            CurrentEdge = null;
            return true;
        }

        return false;
    }
}
