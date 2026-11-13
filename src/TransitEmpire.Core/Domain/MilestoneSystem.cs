using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Core.Domain;

public sealed record MilestoneDefinition(
    string Id,
    string Icon,
    string Title,
    string Description,
    string BonusDescription,
    long CashReward,
    int XpReward);

public sealed record MilestoneStatus(
    MilestoneDefinition Definition,
    bool IsUnlocked,
    float CurrentValue,
    float TargetValue,
    string ProgressText)
{
    public float NormalizedProgress => IsUnlocked
        ? 1.0f
        : (TargetValue > 0f ? Math.Clamp(CurrentValue / TargetValue, 0f, 1.0f) : 0f);
}

/// <summary>
/// Sistema de Conquistas &amp; Marcos Históricos (Milestones) com bônus permanentes para a transportadora.
/// </summary>
public class MilestoneTracker
{
    public const string IdKingOfMountains = "king_of_mountains";
    public const string IdCentenaryFleet = "centenary_fleet";
    public const string IdAsphaltBaron = "asphalt_baron";
    public const string IdMasterExporter = "master_exporter";
    public const string IdMonumentBuilder = "monument_builder";
    public const string IdMetroTycoon = "metro_tycoon";

    public static readonly IReadOnlyList<MilestoneDefinition> Catalog = new List<MilestoneDefinition>
    {
        new(
            IdKingOfMountains,
            "⛰️",
            "Rei da Serra",
            "Possua pelo menos 10 segmentos de túnel cruzando serras e montanhas.",
            "+12% velocidade em subidas e biomas alpinos",
            15000L,
            350),
        new(
            IdCentenaryFleet,
            "🚛",
            "Frota Centenária",
            "Acumule 100.000 km percorridos pela sua frota ao longo da história.",
            "-8% consumo/custo de combustível da frota",
            20000L,
            450),
        new(
            IdAsphaltBaron,
            "🛣️",
            "Barão do Asfalto",
            "Pavimente para Asfalto ou superior todas as ligações viárias urbanas.",
            "+10% valor de fretes e arrecadação de pedágios",
            18000L,
            400),
        new(
            IdMasterExporter,
            "⚓",
            "Magnata dos Mares",
            "Exporte pelo menos 500 toneladas/unidades através dos Portos Marítimos.",
            "+10% bônus adicional em exportações portuárias",
            25000L,
            500),
        new(
            IdMonumentBuilder,
            "🏆",
            "Arquiteto Regional",
            "Conclua todas as 3 etapas de pelo menos 1 Mega-Obra Regional.",
            "+15% bônus em Contratos Expressos",
            20000L,
            500),
        new(
            IdMetroTycoon,
            "🏙️",
            "Império Metropolitano",
            "Opere 15 caminhões simultâneos ou conquiste 3 expansões urbanas.",
            "+10% XP global em todas as entregas",
            15000L,
            350)
    };

    public HashSet<string> UnlockedIds { get; } = new();
    public float RetiredFleetDistanceKm { get; set; } = 0f;

    public bool IsUnlocked(string id) => UnlockedIds.Contains(id);

    public float HillClimbBonusMultiplier => IsUnlocked(IdKingOfMountains) ? 1.12f : 1.0f;
    public float FuelCostBonusMultiplier => IsUnlocked(IdCentenaryFleet) ? 0.92f : 1.0f;
    public float FreightAndTollBonusMultiplier => IsUnlocked(IdAsphaltBaron) ? 1.10f : 1.0f;
    public float SeaportExportBonusMultiplier => IsUnlocked(IdMasterExporter) ? 1.10f : 1.0f;
    public float ContractBonusMultiplier => IsUnlocked(IdMonumentBuilder) ? 1.15f : 1.0f;
    public float XpBonusMultiplier => IsUnlocked(IdMetroTycoon) ? 1.10f : 1.0f;

    public static int CountTunnelTiles(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int count = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (world.Grid[x, y].HasRoad && world.Grid[x, y].StructureType == RoadStructureType.Tunnel)
                    count++;
            }
        }
        return count;
    }

    public static (int PavedCount, int TotalCount, bool Qualified) EvaluateUrbanLinksPaved(GeneratedWorld world)
    {
        if (world.RoadEdges.Count == 0)
            return (0, 0, false);

        var urbanEdges = new List<RoadEdge>();
        foreach (var edge in world.RoadEdges)
        {
            bool touchesCity = world.Cities.Any(c =>
                (Math.Abs(edge.FromNode.GridX - c.CenterX) <= 2 && Math.Abs(edge.FromNode.GridY - c.CenterY) <= 2) ||
                (Math.Abs(edge.ToNode.GridX - c.CenterX) <= 2 && Math.Abs(edge.ToNode.GridY - c.CenterY) <= 2));
            if (touchesCity)
                urbanEdges.Add(edge);
        }

        int total = urbanEdges.Count;
        int paved = urbanEdges.Count(e => e.Tier >= RoadTier.Asphalt);
        bool qualified = total >= 2 && paved == total;
        return (paved, total, qualified);
    }

    public List<MilestoneStatus> GetStatuses(SimulationEngine engine)
    {
        int tunnels = CountTunnelTiles(engine.World);
        float totalKm = RetiredFleetDistanceKm + engine.ActiveTrucks.Sum(t => t.TotalDistanceTravelledKm);
        var (urbanPaved, urbanTotal, urbanQualified) = EvaluateUrbanLinksPaved(engine.World);
        int exportedUnits = engine.World.Seaports.Sum(s => s.TotalExportedUnits);
        int completedLandmarks = engine.World.Landmarks.Count(l => l.IsCompleted);
        int activeTrucks = engine.ActiveTrucks.Count;
        int urbanExpansions = engine.World.Cities.Sum(c => Math.Max(0, c.ExpansionLevel - 1));

        var result = new List<MilestoneStatus>(Catalog.Count);
        foreach (var def in Catalog)
        {
            bool unlocked = IsUnlocked(def.Id);
            (float cur, float target, string text) = def.Id switch
            {
                IdKingOfMountains => (tunnels, 10f, $"{tunnels}/10 túneis"),
                IdCentenaryFleet => (totalKm, 100_000f, $"{totalKm:N0}/100.000 km"),
                IdAsphaltBaron => (urbanPaved, Math.Max(2f, urbanTotal), urbanTotal > 0 ? $"{urbanPaved}/{urbanTotal} ligações" : "Sem ligações"),
                IdMasterExporter => (exportedUnits, 500f, $"{exportedUnits:N0}/500 un."),
                IdMonumentBuilder => (completedLandmarks, 1f, $"{completedLandmarks}/1 mega-obra"),
                IdMetroTycoon => (
                    Math.Max((float)activeTrucks / 15f, (float)urbanExpansions / 3f) * 100f,
                    100f,
                    $"{activeTrucks}/15 caminhões ou {urbanExpansions}/3 exp."),
                _ => (0f, 1f, "0/1")
            };

            if (def.Id == IdAsphaltBaron && urbanQualified)
                cur = Math.Max(cur, Math.Max(2f, urbanTotal));

            result.Add(new MilestoneStatus(def, unlocked, cur, target, text));
        }

        return result;
    }

    public List<MilestoneDefinition> EvaluateAndUnlock(SimulationEngine engine)
    {
        var newlyUnlocked = new List<MilestoneDefinition>();
        int tunnels = CountTunnelTiles(engine.World);
        float totalKm = RetiredFleetDistanceKm + engine.ActiveTrucks.Sum(t => t.TotalDistanceTravelledKm);
        var (_, _, urbanQualified) = EvaluateUrbanLinksPaved(engine.World);
        int exportedUnits = engine.World.Seaports.Sum(s => s.TotalExportedUnits);
        int completedLandmarks = engine.World.Landmarks.Count(l => l.IsCompleted);
        int activeTrucks = engine.ActiveTrucks.Count;
        int urbanExpansions = engine.World.Cities.Sum(c => Math.Max(0, c.ExpansionLevel - 1));

        foreach (var def in Catalog)
        {
            if (UnlockedIds.Contains(def.Id))
                continue;

            bool met = def.Id switch
            {
                IdKingOfMountains => tunnels >= 10,
                IdCentenaryFleet => totalKm >= 100_000f,
                IdAsphaltBaron => urbanQualified,
                IdMasterExporter => exportedUnits >= 500,
                IdMonumentBuilder => completedLandmarks >= 1,
                IdMetroTycoon => activeTrucks >= 15 || urbanExpansions >= 3,
                _ => false
            };

            if (met)
            {
                UnlockedIds.Add(def.Id);
                newlyUnlocked.Add(def);
            }
        }

        return newlyUnlocked;
    }

    internal void RestoreState(IEnumerable<string>? unlockedIds, float retiredFleetDistanceKm)
    {
        UnlockedIds.Clear();
        if (unlockedIds != null)
        {
            foreach (var id in unlockedIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    UnlockedIds.Add(id);
            }
        }
        RetiredFleetDistanceKm = Math.Max(0f, retiredFleetDistanceKm);
    }
}
