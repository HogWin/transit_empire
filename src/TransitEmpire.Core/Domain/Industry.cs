using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Indústrias produtoras ou processadoras de recursos.
/// </summary>
public class Industry
{
    public int Id { get; }
    public string Name { get; }
    public int GridX { get; }
    public int GridY { get; }
    public ResourceType OutputResource { get; }
    public ResourceType RequiredInputResource { get; } // None se for extrativa primária
    public ResourceType SecondaryInputResource { get; set; } = ResourceType.None; // Insumo catalítico/avançado (Fase 5 / Coal)
    public ResourceType TertiaryInputResource { get; set; } = ResourceType.None; // Terceiro insumo (Sprint F3: Fuel em Maquinário)
    
    public int OutputStock { get; set; }
    public int InputStock { get; set; }
    public int SecondaryInputStock { get; set; }
    public int TertiaryInputStock { get; set; }
    public int MaxStock { get; set; } = 300;
    public int ProductionRatePerDay { get; set; }
    public int UpgradeLevel { get; set; } = 1;

    public int MaxUpgradeLevel => 5;
    public bool CanUpgrade => UpgradeLevel < MaxUpgradeLevel;
    public int GetNextUpgradeCost() => 10_000 * UpgradeLevel;

    public Industry(
        int id,
        string name,
        int gridX,
        int gridY,
        ResourceType output,
        ResourceType input = ResourceType.None,
        int productionRate = 15,
        ResourceType secondaryInput = ResourceType.None,
        ResourceType tertiaryInput = ResourceType.None)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        OutputResource = output;
        RequiredInputResource = input;
        SecondaryInputResource = secondaryInput;
        TertiaryInputResource = tertiaryInput;
        ProductionRatePerDay = productionRate;
        OutputStock = 20; // Estoque inicial para primeiras viagens
        InputStock = 0;
        SecondaryInputStock = 0;
        TertiaryInputStock = 0;
    }

    public void ApplyPlantUpgrade()
    {
        if (!CanUpgrade) return;
        UpgradeLevel++;
        MaxStock += 150;
        ProductionRatePerDay += 6;
    }

    public void Produce(int bonusUnits = 0)
    {
        if (RequiredInputResource != ResourceType.None)
        {
            if (InputStock < 5) return; // Sem matéria-prima suficiente
            InputStock -= 5;
        }

        int secondaryBonus = 0;
        if (SecondaryInputResource != ResourceType.None && SecondaryInputStock >= 3)
        {
            SecondaryInputStock -= 3;
            secondaryBonus = SecondaryInputResource == ResourceType.Coal
                ? System.Math.Max(6, ProductionRatePerDay)
                : System.Math.Max(4, ProductionRatePerDay / 2);
        }

        int tertiaryBonus = 0;
        if (TertiaryInputResource != ResourceType.None && TertiaryInputStock >= 2)
        {
            TertiaryInputStock -= 2;
            tertiaryBonus = System.Math.Max(5, ProductionRatePerDay / 2);
        }

        int total = ProductionRatePerDay + System.Math.Max(0, bonusUnits) + secondaryBonus + tertiaryBonus;
        OutputStock = System.Math.Min(MaxStock, OutputStock + total);
    }

    public int DispatchCargo(int requestedAmount)
    {
        int available = System.Math.Min(OutputStock, requestedAmount);
        OutputStock -= available;
        return available;
    }

    /// <summary>
    /// Recebe matéria-prima (primária, secundária ou terciária) entregue por caminhões para processamento nesta indústria.
    /// </summary>
    public int ReceiveInputCargo(ResourceType type, int amount)
    {
        if (amount <= 0 || type == ResourceType.None)
            return 0;

        if (RequiredInputResource != ResourceType.None && type == RequiredInputResource)
        {
            int freeSpace = System.Math.Max(0, MaxStock - InputStock);
            int accepted = System.Math.Min(amount, freeSpace);
            InputStock += accepted;
            return accepted;
        }

        if (SecondaryInputResource != ResourceType.None && type == SecondaryInputResource)
        {
            int freeSpace = System.Math.Max(0, MaxStock - SecondaryInputStock);
            int accepted = System.Math.Min(amount, freeSpace);
            SecondaryInputStock += accepted;
            return accepted;
        }

        if (TertiaryInputResource != ResourceType.None && type == TertiaryInputResource)
        {
            int freeSpace = System.Math.Max(0, MaxStock - TertiaryInputStock);
            int accepted = System.Math.Min(amount, freeSpace);
            TertiaryInputStock += accepted;
            return accepted;
        }

        return 0;
    }
}

