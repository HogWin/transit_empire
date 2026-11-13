using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintE2CoreTests
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

    [Fact]
    public void SharedLogisticsLine_UpdatesAllLinkedTrucks_AndPersistsAcrossSaveLoad()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        var n3 = new RoadNode(3, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[3, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Asphalt);

        var indA = new Industry(100, "Mina Sul", 2, 1, ResourceType.IronOre, ResourceType.None);
        var cityA = new City(1, "Porto Sul", 4, 3);
        var cityB = new City(2, "Serra Norte", 3, 3);
        world.Industries.Add(indA);
        world.Cities.AddRange(new[] { cityA, cityB });

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 200_000;
        var garage = engine.BuildGarage(2, 3, "Garagem Base")!;
        var spec = ProgressionManager.AvailableTruckModels[0];
        var t1 = engine.BuyTruck(garage, spec)!;
        var t2 = engine.BuyTruck(garage, spec)!;

        var initialStops = new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, indA.Id, ScheduleAction.Load, DispatchCondition.WaitHalfLoad),
            new ScheduleStop(ScheduleStopKind.City, cityA.Id, ScheduleAction.Unload)
        };
        var line = engine.CreateLogisticsLine("Linha 01 — Minério Sul", initialStops);

        engine.AssignTruckToLogisticsLine(t1, line);
        engine.AssignTruckToLogisticsLine(t2, line);

        Assert.Equal(line.Id, t1.AssignedLineId);
        Assert.Equal(line.Id, t2.AssignedLineId);
        Assert.Equal(2, t1.Schedule.Count);
        Assert.Equal(2, t2.Schedule.Count);

        // Atualiza a linha compartilhada com nova parada e valida propagação automática
        var updatedStops = new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, indA.Id, ScheduleAction.Load, DispatchCondition.WaitFullLoad),
            new ScheduleStop(ScheduleStopKind.City, cityA.Id, ScheduleAction.Unload),
            new ScheduleStop(ScheduleStopKind.City, cityB.Id, ScheduleAction.Unload)
        };
        Assert.True(engine.UpdateLogisticsLine(line.Id, "Linha 01 — Expressa", updatedStops));

        Assert.Equal(3, t1.Schedule.Count);
        Assert.Equal(3, t2.Schedule.Count);
        Assert.Equal(cityB.Id, t1.Schedule[2].TargetId);
        Assert.Equal(DispatchCondition.WaitFullLoad, t2.Schedule[0].Condition);

        // Serializa e desserializa garantindo persistência da linha e vínculo
        string json = SaveGameSerializer.SerializeToJson(engine, 12345);
        var (loadedEngine, _) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Single(loadedEngine.LogisticsLines);
        Assert.Equal("Linha 01 — Expressa", loadedEngine.LogisticsLines[0].Name);
        Assert.Equal(3, loadedEngine.LogisticsLines[0].Stops.Count);
        Assert.All(loadedEngine.ActiveTrucks, t => Assert.Equal(line.Id, t.AssignedLineId));
    }

    [Fact]
    public void Bulldozer_DemolishesStructuresAndRoads_With40PercentRefund_AndCacheInvalidation()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        var n3 = new RoadNode(3, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[3, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Asphalt);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;

        // Constrói Hub (R$ 12.000), Pedágio (R$ 3.500) e 2 Garagens (R$ 8.000 cada)
        var hub = engine.BuildLogisticsHub(2, 4, "Hub Leste")!;
        Assert.NotNull(hub);
        var toll = engine.BuildTollStation(3, 2, out _)!;
        Assert.NotNull(toll);
        var g1 = engine.BuildGarage(2, 3, "Garagem 1")!;
        var g2 = engine.BuildGarage(4, 3, "Garagem 2")!;
        var spec = ProgressionManager.AvailableTruckModels[0];
        var truck = engine.BuyTruck(g1, spec)!;

        // 1. Demolir pedágio devolve 40% de 3.500 = 1.400
        long balBeforeToll = engine.Player.Balance;
        Assert.True(engine.TryDemolishAt(3, 2, out var tollRes, out _));
        Assert.Equal(DemolishTargetKind.TollStation, tollRes.Kind);
        Assert.Equal(1400, tollRes.RefundAmount);
        Assert.Equal(balBeforeToll + 1400, engine.Player.Balance);
        Assert.Empty(engine.TollStations);

        // 2. Demolir hub devolve 40% de 12.000 = 4.800
        long balBeforeHub = engine.Player.Balance;
        Assert.True(engine.TryDemolishAt(2, 4, out var hubRes, out _));
        Assert.Equal(DemolishTargetKind.LogisticsHub, hubRes.Kind);
        Assert.Equal(4800, hubRes.RefundAmount);
        Assert.Equal(balBeforeHub + 4800, engine.Player.Balance);
        Assert.Empty(engine.LogisticsHubs);

        // 3. Demolir Garagem 1 realoca o caminhão para Garagem 2 e devolve 40% de 8.000 = 3.200
        long balBeforeGarage = engine.Player.Balance;
        Assert.True(engine.TryDemolishAt(2, 3, out var gRes, out _));
        Assert.Equal(DemolishTargetKind.Garage, gRes.Kind);
        Assert.Equal(3200, gRes.RefundAmount);
        Assert.Equal(balBeforeGarage + 3200, engine.Player.Balance);
        Assert.Contains(truck, g2.StationedTrucks);

        // 4. Aquecendo cache de rotas e demolindo trecho (3,2) Asfalto:
        // Investido: Base Ground (40) + Dirt->Asphalt (50 + 150 = 200) = 240 => 40% = 96
        var pathBefore = engine.Pathfinder.FindFastestRoute(n1, n3);
        Assert.NotNull(pathBefore);
        int genBefore = engine.Pathfinder.CacheGeneration;

        long balBeforeRoad = engine.Player.Balance;
        Assert.True(engine.TryDemolishAt(3, 2, out var roadRes, out _));
        Assert.Equal(DemolishTargetKind.Road, roadRes.Kind);
        Assert.Equal(96, roadRes.RefundAmount);
        Assert.Equal(balBeforeRoad + 96, engine.Player.Balance);
        Assert.Equal(RoadTier.None, world.Grid[3, 2].RoadLevel);
        Assert.True(engine.Pathfinder.CacheGeneration > genBefore);
        Assert.Null(engine.Pathfinder.FindFastestRoute(n1, n3));
    }

    [Fact]
    public void FleetModernizer_UpgradesTruckModel_SellsOldFor60Percent_AndTransfersLineAndSchedule()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[3, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt);

        var ind = new Industry(100, "Madeireira", 2, 1, ResourceType.Lumber, ResourceType.None);
        var city = new City(1, "Vila Nova", 3, 3);
        world.Industries.Add(ind);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Level = 5;
        engine.Player.Balance = 100_000;

        var garage = engine.BuildGarage(2, 3, "Matriz")!;
        var oldSpec = ProgressionManager.AvailableTruckModels[0]; // 2.500 -> trade-in 60% = 1.500
        var newSpec = ProgressionManager.AvailableTruckModels[2]; // 9.500 -> líquido = 8.000
        var oldTruck = engine.BuyTruck(garage, oldSpec)!;

        var line = engine.CreateLogisticsLine("Linha Madeira", new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });
        engine.AssignTruckToLogisticsLine(oldTruck, line);
        oldTruck.LoadedCargoType = ResourceType.Lumber;
        oldTruck.LoadedCargoAmount = oldSpec.Capacity;

        long balanceBefore = engine.Player.Balance;
        long expectedTradeIn = (long)System.Math.Round(oldSpec.PurchasePrice * 0.60);
        long expectedNetCost = newSpec.PurchasePrice - expectedTradeIn;

        Assert.True(engine.TryUpgradeTruckModel(oldTruck, newSpec, out var upgraded, out _));
        Assert.NotNull(upgraded);
        Assert.Equal(newSpec.Name, upgraded!.Spec.Name);
        Assert.Equal(balanceBefore - expectedNetCost, engine.Player.Balance);
        Assert.Single(engine.ActiveTrucks);
        Assert.Same(upgraded, engine.ActiveTrucks[0]);
        Assert.Equal(line.Id, upgraded.AssignedLineId);
        Assert.True(upgraded.HasManualSchedule);
        Assert.Equal(2, upgraded.Schedule.Count);
        Assert.Equal(oldSpec.Capacity, upgraded.LoadedCargoAmount);
        Assert.Equal(ResourceType.Lumber, upgraded.LoadedCargoType);
        Assert.DoesNotContain(oldTruck, garage.StationedTrucks);
        Assert.Contains(upgraded, garage.StationedTrucks);
    }

    private static void LinkBidirectional(GeneratedWorld world, int startEdgeId, RoadNode a, RoadNode b, RoadTier tier)
    {
        var fwd = new RoadEdge(startEdgeId, a, b, tier, 1f);
        var rev = new RoadEdge(startEdgeId + 1, b, a, tier, 1f);
        a.OutgoingEdges.Add(fwd);
        b.OutgoingEdges.Add(rev);
        world.RoadEdges.Add(fwd);
        world.RoadEdges.Add(rev);
    }
}

