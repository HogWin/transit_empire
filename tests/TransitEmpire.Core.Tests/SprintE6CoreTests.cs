using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintE6CoreTests
{
    private static GeneratedWorld CreateSimpleWorld(int w = 16, int h = 16)
    {
        var world = new GeneratedWorld(w, h);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);
            }
        }
        return world;
    }

    private static (RoadEdge forward, RoadEdge reverse) LinkBidirectional(GeneratedWorld world, int startEdgeId, RoadNode a, RoadNode b, RoadTier tier, float distanceKm = 10f)
    {
        var f = new RoadEdge(startEdgeId, a, b, tier, distanceKm);
        var r = new RoadEdge(startEdgeId + 1, b, a, tier, distanceKm);
        a.OutgoingEdges.Add(f);
        b.OutgoingEdges.Add(r);
        world.RoadEdges.Add(f);
        world.RoadEdges.Add(r);
        return (f, r);
    }

    [Fact]
    public void OperationalDiagnostics_DetectsBlockedDamagedCongestedStalledQueuedAndUnsupplied()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 6, 2);
        var n3 = new RoadNode(3, 10, 2);
        var n4 = new RoadNode(4, 14, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3, n4 });

        var (eBlocked, _) = LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 5f);
        eBlocked.IsBlocked = true;

        var (eDamaged, _) = LinkBidirectional(world, 3, n2, n3, RoadTier.Asphalt, 5f);
        eDamaged.PavementCondition = 32f;
        eDamaged.ActiveTrucksCount = 4;

        var city = new City(1, "Cidade Seca", 2, 2) { Population = 1500 };
        city.Warehouse.StoredStock[ResourceType.Food] = 0;
        city.Warehouse.StoredStock[ResourceType.Goods] = 0;
        world.Cities.Add(city);

        var indGrain = new Industry(100, "Fazenda Teste", 6, 3, ResourceType.Grain, ResourceType.None, 20);
        indGrain.OutputStock = 0;
        var indFood = new Industry(101, "Moinho Parado", 10, 3, ResourceType.Food, ResourceType.Grain, 20);
        indFood.InputStock = 0;
        indFood.OutputStock = 0;
        world.Industries.AddRange(new[] { indGrain, indFood });

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;
        var garage = engine.BuildGarage(2, 3, "Garagem")!;
        var t1 = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        var t2 = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;

        t1.Condition = 18f; // Desgaste crítico
        t2.SetSchedule(new[] { new ScheduleStop(ScheduleStopKind.Industry, indGrain.Id, ScheduleAction.Load, DispatchCondition.WaitFullLoad) });
        t2.Status = TruckStatus.WaitingForCargo;

        var report = engine.DiagnoseBottlenecks();

        Assert.Contains(report, b => b.Kind == BottleneckKind.BlockedRoad && b.GridX == 4 && b.GridY == 2);
        Assert.Contains(report, b => b.Kind == BottleneckKind.DamagedRoad && b.GridX == 8 && b.GridY == 2);
        Assert.Contains(report, b => b.Kind == BottleneckKind.CongestedCorridor && b.GridX == 8 && b.GridY == 2);
        Assert.Contains(report, b => b.Kind == BottleneckKind.StalledIndustry && b.GridX == 10 && b.GridY == 3);
        Assert.Contains(report, b => b.Kind == BottleneckKind.QueuedTruck && b.Severity == BottleneckSeverity.Critical);
        Assert.Contains(report, b => b.Kind == BottleneckKind.UnsuppliedCity && b.GridX == 2 && b.GridY == 2);
    }

    [Fact]
    public void MilestoneTracker_UnlocksAchievementsAndAppliesPermanentBonuses()
    {
        var world = CreateSimpleWorld(20, 20);
        var city1 = new City(1, "Alpha", 2, 2) { Population = 1000 };
        var city2 = new City(2, "Beta", 10, 2) { Population = 1000, ExpansionLevel = 4 }; // +3 expansões -> Império Metropolitano
        world.Cities.AddRange(new[] { city1, city2 });

        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 6, 2);
        var n3 = new RoadNode(3, 10, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[6, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[10, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 5f);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Asphalt, 5f);

        // 10 células de túnel -> Rei da Serra
        for (int x = 2; x < 12; x++)
        {
            world.Grid[x, 8].Terrain = TerrainType.Mountain;
            world.Grid[x, 8].RoadLevel = RoadTier.Dirt;
        }

        // Porto com 500 un. exportadas (em dois embarques ou navio ampliado) -> Magnata dos Mares
        world.Grid[11, 12].Terrain = TerrainType.Water;
        var seaport = new SeaportTerminal(1, "Porto E6", 10, 12, 11, 12) { MaxShipCapacity = 600 };
        seaport.LoadExportCargo(ResourceType.Steel, 250, out _);
        seaport.LoadExportCargo(ResourceType.Grain, 250, out _);
        world.Seaports.Add(seaport);

        // Mega-obra concluída -> Arquiteto Regional
        var landmark = new RegionalLandmark(1, "Obra E6", LandmarkKind.HydroelectricDam, 14, 14);
        landmark.ContributeCargo(ResourceType.Steel, 30, out _, out _);
        landmark.ContributeCargo(ResourceType.Lumber, 30, out _, out _);
        landmark.ContributeCargo(ResourceType.Steel, 45, out _, out _);
        landmark.ContributeCargo(ResourceType.Goods, 35, out _, out _);
        landmark.ContributeCargo(ResourceType.Steel, 45, out _, out _);
        landmark.ContributeCargo(ResourceType.Lumber, 40, out _, out _);
        landmark.ContributeCargo(ResourceType.Goods, 45, out _, out _);
        world.Landmarks.Add(landmark);

        var engine = new SimulationEngine(world);
        engine.Milestones.RetiredFleetDistanceKm = 100_050f; // -> Frota Centenária

        int unlockEvents = 0;
        engine.OnMilestoneUnlocked += _ => unlockEvents++;

        long balanceBefore = engine.Player.Balance;
        var newlyUnlocked = engine.EvaluateMilestones();

        Assert.Equal(6, newlyUnlocked.Count);
        Assert.Equal(6, unlockEvents);
        Assert.True(engine.Player.Balance > balanceBefore);

        Assert.Equal(1.12f, engine.Milestones.HillClimbBonusMultiplier);
        Assert.Equal(0.92f, engine.Milestones.FuelCostBonusMultiplier);
        Assert.Equal(1.10f, engine.Milestones.FreightAndTollBonusMultiplier);
        Assert.Equal(1.10f, engine.Milestones.SeaportExportBonusMultiplier);
        Assert.Equal(1.15f, engine.Milestones.ContractBonusMultiplier);
        Assert.Equal(1.10f, engine.Milestones.XpBonusMultiplier);

        // Reavaliar não deve duplicar desbloqueios nem prêmios
        long balanceAfter = engine.Player.Balance;
        Assert.Empty(engine.EvaluateMilestones());
        Assert.Equal(balanceAfter, engine.Player.Balance);
    }

    [Fact]
    public void SaveGameSerializer_RoundTripsMilestonesAndRetiredDistance()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 5f);

        for (int x = 1; x <= 10; x++)
        {
            world.Grid[x, 6].Terrain = TerrainType.Mountain;
            world.Grid[x, 6].RoadLevel = RoadTier.Dirt;
        }

        var engine = new SimulationEngine(world);
        engine.Milestones.RetiredFleetDistanceKm = 42_500f;
        engine.EvaluateMilestones();
        Assert.True(engine.Milestones.IsUnlocked(MilestoneTracker.IdKingOfMountains));

        string json = SaveGameSerializer.SerializeToJson(engine, 99);
        var (reloaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.True(reloaded.Milestones.IsUnlocked(MilestoneTracker.IdKingOfMountains));
        Assert.Equal(42_500f, reloaded.Milestones.RetiredFleetDistanceKm);
        Assert.Equal(1.12f, reloaded.Milestones.HillClimbBonusMultiplier);
    }
}
