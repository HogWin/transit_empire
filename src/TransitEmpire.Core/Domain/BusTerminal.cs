using System;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Terminal Rodoviário Urbano construído adjacente a uma cidade e à malha viária
/// para embarque e desembarque de passageiros em linhas intermunicipais.
/// </summary>
public class BusTerminal
{
    public const int ConstructionCost = 6000;

    public int Id { get; }
    public string Name { get; }
    public int CityId { get; }
    public int GridX { get; }
    public int GridY { get; }
    public RoadNode AccessNode { get; }

    public int WaitingPassengers { get; set; } = 15;
    public int MaxPassengers { get; set; } = 140;
    public int TotalPassengersServed { get; set; }
    public long TotalTicketRevenue { get; set; }

    public BusTerminal(int id, string name, int cityId, int gridX, int gridY, RoadNode accessNode)
    {
        Id = id;
        Name = name;
        CityId = cityId;
        GridX = gridX;
        GridY = gridY;
        AccessNode = accessNode;
    }

    public int GenerateDailyPassengers(int cityPopulation)
    {
        int generated = Math.Clamp(cityPopulation / 25, 8, 45);
        int free = Math.Max(0, MaxPassengers - WaitingPassengers);
        int added = Math.Min(generated, free);
        WaitingPassengers += added;
        return added;
    }

    public int BoardPassengers(int requested)
    {
        if (requested <= 0 || WaitingPassengers <= 0)
            return 0;

        int boarded = Math.Min(requested, WaitingPassengers);
        WaitingPassengers -= boarded;
        return boarded;
    }

    public void RecordArrival(int droppedOff, long revenue)
    {
        if (droppedOff > 0)
            TotalPassengersServed += droppedOff;
        if (revenue > 0)
            TotalTicketRevenue += revenue;
    }
}

