using System;
using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Centro de Transbordo / Hub Logístico construído pelo jogador adjacente à malha viária (Regra #4 do README).
/// Permite estocar cargas intermediárias, reparar caminhões em trânsito e bonificar entregas regionais.
/// </summary>
public class LogisticsHub
{
    public int Id { get; }
    public string Name { get; }
    public int GridX { get; }
    public int GridY { get; }
    public RoadNode AccessNode { get; }
    public int MaxStorageCapacity { get; set; } = 800;
    public Dictionary<ResourceType, int> StoredCargo { get; } = new();
    public float RegionalFreightBonusMultiplier { get; } = 1.20f; // +20% de bônus logístico

    private int _fuelStock = 0;
    private float _pendingFuelBurnKm = 0f;

    /// <summary>Reserva regional de diesel do Hub (Sprint F3) para abastecer corredores próximos.</summary>
    public int FuelStock
    {
        get => _fuelStock;
        set => _fuelStock = value;
    }

    public int MaxFuelCapacity { get; set; } = 500;

    public int CurrentTotalStored
    {
        get
        {
            int sum = 0;
            foreach (var val in StoredCargo.Values) sum += val;
            return sum;
        }
    }

    public LogisticsHub(int id, string name, int gridX, int gridY, RoadNode accessNode)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        AccessNode = accessNode;
    }

    public int StoreCargo(ResourceType type, int amount)
    {
        if (amount <= 0 || type == ResourceType.None) return 0;
        int freeSpace = Math.Max(0, MaxStorageCapacity - CurrentTotalStored);
        int accepted = Math.Min(amount, freeSpace);

        if (!StoredCargo.ContainsKey(type))
            StoredCargo[type] = 0;

        StoredCargo[type] += accepted;
        return accepted;
    }

    public int WithdrawCargo(ResourceType type, int requestedAmount)
    {
        if (requestedAmount <= 0 || !StoredCargo.TryGetValue(type, out int current) || current <= 0)
            return 0;

        int withdrawn = Math.Min(current, requestedAmount);
        StoredCargo[type] = current - withdrawn;
        return withdrawn;
    }

    public int ReceiveFuel(int amount) => FuelReserveMath.ReceiveFuel(ref _fuelStock, MaxFuelCapacity, amount);

    public bool TryConsumeFuelForTravel(float distanceKm) =>
        FuelReserveMath.TryConsumeFuelForTravel(ref _fuelStock, ref _pendingFuelBurnKm, distanceKm);

    public void RepairTruckIfNeeded(Truck truck)
    {
        if (truck.Condition < 100f)
            truck.Repair();
    }
}

