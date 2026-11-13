using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Simulation;

/// <summary>
/// Gerencia os níveis do jogador, desbloqueios de veículos e permissões de upgrades viários.
/// </summary>
public class ProgressionManager
{
    public static readonly List<TruckSpecification> AvailableTruckModels = new()
    {
        new TruckSpecification(
            Name: "Utilitário F-4000 Carga Seca",
            Capacity: 12,
            MaxTopSpeedKmh: 65f,
            PurchasePrice: 5000,
            RunningCostPerKm: 1,
            RequiredPlayerLevel: 1
        ),
        new TruckSpecification(
            Name: "Scania 113H TopLine 360",
            Capacity: 28,
            MaxTopSpeedKmh: 75f,
            PurchasePrice: 12000,
            RunningCostPerKm: 2,
            RequiredPlayerLevel: 1
        ),
        new TruckSpecification(
            Name: "Mercedes-Benz L-1620 Bicudo",
            Capacity: 45,
            MaxTopSpeedKmh: 80f,
            PurchasePrice: 24000,
            RunningCostPerKm: 3,
            RequiredPlayerLevel: 2
        ),
        new TruckSpecification(
            Name: "Volvo FH 540 Globetrotter 6x2",
            Capacity: 80,
            MaxTopSpeedKmh: 85f,
            PurchasePrice: 48000,
            RunningCostPerKm: 5,
            RequiredPlayerLevel: 3
        ),
        new TruckSpecification(
            Name: "Peterbilt 389 Bi-Trem King",
            Capacity: 150,
            MaxTopSpeedKmh: 95f,
            PurchasePrice: 95000,
            RunningCostPerKm: 8,
            RequiredPlayerLevel: 4
        ),
        new TruckSpecification(
            Name: "Scania R730 V8 Rodotrem 9 Eixos",
            Capacity: 220,
            MaxTopSpeedKmh: 90f,
            PurchasePrice: 155000,
            RunningCostPerKm: 11,
            RequiredPlayerLevel: 5,
            RequiresHighwayOrDualCarriage: true
        ),
        new TruckSpecification(
            Name: "Ônibus Rodoviário Marcobus G7",
            Capacity: 44,
            MaxTopSpeedKmh: 85f,
            PurchasePrice: 18000,
            RunningCostPerKm: 2,
            RequiredPlayerLevel: 1,
            IsPassengerBus: true
        ),
        new TruckSpecification(
            Name: "Ônibus Double-Decker Executivo",
            Capacity: 76,
            MaxTopSpeedKmh: 95f,
            PurchasePrice: 42000,
            RunningCostPerKm: 3,
            RequiredPlayerLevel: 3,
            IsPassengerBus: true
        )
    };

    /// <summary>
    /// Retorna o nível de estrada mais alto que o jogador tem permissão de construir com base em seu nível atual.
    /// </summary>
    public static RoadTier GetMaxUnlockedRoadTier(int playerLevel) => playerLevel switch
    {
        <= 1 => RoadTier.Gravel,
        2 => RoadTier.Asphalt,
        3 => RoadTier.Highway,
        _ => RoadTier.DualCarriage
    };

    /// <summary>
    /// Retorna todos os modelos de caminhão disponíveis para o nível atual do jogador.
    /// </summary>
    public static IEnumerable<TruckSpecification> GetUnlockedTrucks(int playerLevel)
    {
        return AvailableTruckModels.Where(t => t.RequiredPlayerLevel <= playerLevel);
    }

    public static TruckSpecification ResolveSpecByName(string? specName)
    {
        if (string.IsNullOrWhiteSpace(specName))
            return AvailableTruckModels[0];

        var direct = AvailableTruckModels.FirstOrDefault(s => s.Name == specName);
        if (direct != null)
            return direct;

        return specName switch
        {
            "Furgão Utilitário 1500" => AvailableTruckModels[0],
            "Caminhão Leve 3/4" => AvailableTruckModels[1],
            "Caminhão Toco Médio" => AvailableTruckModels[2],
            "Caminhão Pesado Trucado" => AvailableTruckModels[3],
            "Carreta Bi-Trem Estradeira" => AvailableTruckModels[4],
            "Comboio Rodotrem 9 Eixos" => AvailableTruckModels[5],
            _ => AvailableTruckModels[0]
        };
    }
}

