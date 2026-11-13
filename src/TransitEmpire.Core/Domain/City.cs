using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Armazém urbano de uma cidade, responsável por receber recursos e suprir as demandas diárias.
/// </summary>
public class CityWarehouse
{
    public int CapacityPerResource { get; set; } = 500;
    public Dictionary<ResourceType, int> StoredStock { get; } = new();
    public Dictionary<ResourceType, int> DailyDemand { get; } = new();

    public CityWarehouse()
    {
        // Demanda inicial de qualquer cidade pequena: Comida (Food) e Madeira processada (Lumber)
        DailyDemand[ResourceType.Food] = 10;
        DailyDemand[ResourceType.Lumber] = 5;
        StoredStock[ResourceType.Food] = 0;
        StoredStock[ResourceType.Lumber] = 0;
    }

    public int ReceiveCargo(ResourceType type, int amount)
    {
        if (amount <= 0) return 0;
        if (!StoredStock.ContainsKey(type)) StoredStock[type] = 0;

        int current = StoredStock[type];
        int accepted = System.Math.Min(amount, CapacityPerResource - current);
        StoredStock[type] = current + accepted;
        return accepted;
    }

    public float CalculateSatisfactionRate()
    {
        if (DailyDemand.Count == 0) return 1.0f;

        float totalScore = 0f;
        foreach (var (resource, demand) in DailyDemand)
        {
            if (demand <= 0)
            {
                totalScore += 1f;
                continue;
            }
            int stock = StoredStock.GetValueOrDefault(resource, 0);
            totalScore += System.Math.Clamp((float)stock / demand, 0f, 1f);
        }
        return totalScore / DailyDemand.Count;
    }

    public void ConsumeDailyDemand()
    {
        foreach (var (resource, demand) in DailyDemand)
        {
            int current = StoredStock.GetValueOrDefault(resource, 0);
            StoredStock[resource] = System.Math.Max(0, current - demand);
        }
    }

    /// <summary>
    /// Aumenta a demanda diária e destrava novos bens de consumo conforme a cidade cresce.
    /// Retorna o recurso em destaque, o incremento de demanda e se foi recém-desbloqueado.
    /// </summary>
    public (ResourceType Resource, int Delta, bool IsNewResource) ExpandDemandForGrowth(int expansionLevel, int population)
    {
        DailyDemand[ResourceType.Food] = DailyDemand.GetValueOrDefault(ResourceType.Food, 10) + 2;
        DailyDemand[ResourceType.Lumber] = DailyDemand.GetValueOrDefault(ResourceType.Lumber, 5) + 1;

        if ((expansionLevel >= 2 || population >= 300) && !DailyDemand.ContainsKey(ResourceType.Goods))
        {
            DailyDemand[ResourceType.Goods] = 4;
            StoredStock[ResourceType.Goods] = StoredStock.GetValueOrDefault(ResourceType.Goods, 0);
            CapacityPerResource = System.Math.Max(CapacityPerResource, 650);
            return (ResourceType.Goods, 4, true);
        }

        if ((expansionLevel >= 4 || population >= 400) && !DailyDemand.ContainsKey(ResourceType.Fuel))
        {
            DailyDemand[ResourceType.Fuel] = 4;
            StoredStock[ResourceType.Fuel] = StoredStock.GetValueOrDefault(ResourceType.Fuel, 0);
            CapacityPerResource = System.Math.Max(CapacityPerResource, 800);
            return (ResourceType.Fuel, 4, true);
        }

        if ((expansionLevel >= 5 || population >= 460) && !DailyDemand.ContainsKey(ResourceType.Machinery))
        {
            DailyDemand[ResourceType.Machinery] = 3;
            StoredStock[ResourceType.Machinery] = StoredStock.GetValueOrDefault(ResourceType.Machinery, 0);
            CapacityPerResource = System.Math.Max(CapacityPerResource, 950);
            return (ResourceType.Machinery, 3, true);
        }

        if (DailyDemand.ContainsKey(ResourceType.Goods))
        {
            DailyDemand[ResourceType.Goods] += 2;
            return (ResourceType.Goods, 2, false);
        }

        return (ResourceType.Food, 2, false);
    }
}

/// <summary>
/// Representa uma cidade com seus quarteirões no grid e sua dinâmica de expansão randômica.
/// </summary>
public class City
{
    public int Id { get; }
    public string Name { get; }
    public int CenterX { get; }
    public int CenterY { get; }
    public HashSet<(int X, int Y)> UrbanTiles { get; } = new();
    public CityWarehouse Warehouse { get; } = new();
    
    public int Population { get; set; } = 250;
    public int ConsecutiveCyclesWellSupplied { get; set; } = 0;
    public int ExpansionLevel { get; set; } = 1;
    public float LastSatisfactionRate { get; set; } = 1.0f;
    public long TotalSubsidiesReceived { get; set; } = 0;
    public int ActiveBusServiceHours { get; set; } = 0;

    public City(int id, string name, int centerX, int centerY)
    {
        Id = id;
        Name = name;
        CenterX = centerX;
        CenterY = centerY;
        
        // Núcleo inicial da cidade (área 3x3)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                UrbanTiles.Add((centerX + dx, centerY + dy));
            }
        }
    }
}

