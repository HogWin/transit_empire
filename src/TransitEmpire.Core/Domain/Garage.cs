using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

internal static class FuelReserveMath
{
    public const float DefaultKmPerFuelUnit = 8.0f;

    public static int ReceiveFuel(ref int stock, int maxCapacity, int amount)
    {
        if (amount <= 0) return 0;
        int free = System.Math.Max(0, maxCapacity - stock);
        int accepted = System.Math.Min(amount, free);
        stock += accepted;
        return accepted;
    }

    public static bool TryConsumeFuelForTravel(ref int stock, ref float pendingBurnKm, float distanceKm, float kmPerFuelUnit = DefaultKmPerFuelUnit)
    {
        if (stock <= 0 || distanceKm <= 0f)
            return false;

        pendingBurnKm += distanceKm;
        while (pendingBurnKm >= kmPerFuelUnit && stock > 0)
        {
            pendingBurnKm -= kmPerFuelUnit;
            stock--;
        }

        if (stock == 0)
            pendingBurnKm = 0f;

        return true;
    }
}

/// <summary>
/// Garagem construída pelo jogador no mapa em grade adjacente a uma via.
/// Responsável por abrigar veículos, comprar novos caminhões e gerenciar linhas de transporte.
/// </summary>
public class Garage
{
    public int Id { get; }
    public string Name { get; }
    public int GridX { get; }
    public int GridY { get; }
    public RoadNode AccessNode { get; }
    public int MaxCapacity { get; set; } = 4;
    public int FuelStock
    {
        get => _fuelStock;
        set => _fuelStock = value;
    }
    private int _fuelStock = 0;
    public int MaxFuelCapacity { get; set; } = 300;
    public List<Truck> StationedTrucks { get; } = new();

    // Sprint F2: Regra de Auto-Substituição por Desgaste/Quilometragem na Garagem
    public bool AutoReplaceEnabled { get; set; } = false;
    public float AutoReplaceMinCondition { get; set; } = 25f;
    public float AutoReplaceMaxMileageKm { get; set; } = 0f;
    public string? AutoReplaceTargetSpecName { get; set; }

    private float _pendingFuelBurnKm = 0f;

    public bool IsFull => StationedTrucks.Count >= MaxCapacity;

    public Garage(int id, string name, int gridX, int gridY, RoadNode accessNode, int maxCapacity = 4)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        AccessNode = accessNode;
        MaxCapacity = maxCapacity;
    }

    public int ReceiveFuel(int amount) => FuelReserveMath.ReceiveFuel(ref _fuelStock, MaxFuelCapacity, amount);

    /// <summary>
    /// Consome diesel da reserva da garagem proporcionalmente à quilometragem; retorna true se havia combustível (desconto de 35% no custo/km).
    /// </summary>
    public bool TryConsumeFuelForTravel(float distanceKm) =>
        FuelReserveMath.TryConsumeFuelForTravel(ref _fuelStock, ref _pendingFuelBurnKm, distanceKm);

    public bool AddTruck(Truck truck)
    {
        if (IsFull) return false;
        StationedTrucks.Add(truck);
        truck.Status = TruckStatus.ParkedInGarage;
        return true;
    }

    public bool RemoveTruck(Truck truck)
    {
        return StationedTrucks.Remove(truck);
    }
}

