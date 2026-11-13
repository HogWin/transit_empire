using System;
using System.IO;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class RemainingRoadmapTests
{
    private static GeneratedWorld CreateSimpleGridWorld(int width = 14, int height = 14)
    {
        var world = new GeneratedWorld(width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);
            }
        }
        return world;
    }

    private static RoadEdge LinkBiDirectional(GeneratedWorld world, ref int nextEdgeId, RoadNode a, RoadNode b, RoadTier tier, float distKm)
    {
        world.Grid[a.GridX, a.GridY].RoadLevel = tier;
        world.Grid[b.GridX, b.GridY].RoadLevel = tier;
        var f = new RoadEdge(nextEdgeId++, a, b, tier, distKm);
        var r = new RoadEdge(nextEdgeId++, b, a, tier, distKm);
        a.OutgoingEdges.Add(f);
        b.OutgoingEdges.Add(r);
        world.RoadEdges.Add(f);
        world.RoadEdges.Add(r);
        return f;
    }

    [Fact]
    public void SmartIntersection_TrafficControl_ImprovesFlowUnderCongestion()
    {
        var world = CreateSimpleGridWorld();
        var center = new RoadNode(1, 5, 5);
        var north = new RoadNode(2, 5, 4);
        var east = new RoadNode(3, 6, 5);
        var south = new RoadNode(4, 5, 6);
        world.RoadNodes.AddRange(new[] { center, north, east, south });

        int eid = 1;
        var edge = LinkBiDirectional(world, ref eid, center, north, RoadTier.Dirt, 2f);
        LinkBiDirectional(world, ref eid, center, east, RoadTier.Dirt, 2f);
        LinkBiDirectional(world, ref eid, center, south, RoadTier.Dirt, 2f);

        edge.ActiveTrucksCount = 5;
        float congestedBefore = edge.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 10_000;

        Assert.True(engine.IsIntersectionCell(5, 5));
        Assert.False(engine.HasTrafficControlAt(5, 5));

        bool built = engine.TryBuildTrafficControl(5, 5, out string msg);
        Assert.True(built, msg);
        Assert.True(engine.HasTrafficControlAt(5, 5));
        Assert.Equal(10_000 - SimulationEngine.TrafficControlUpgradeCost, engine.Player.Balance);

        float flowAfter = edge.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear);
        Assert.True(flowAfter > congestedBefore, "Cruzamento inteligente deve aliviar congestionamento.");
    }

    [Fact]
    public void TollAndWeighStation_CollectsDailyAndTripTollsWithComplianceBonus()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        int eid = 1;
        var edge = LinkBiDirectional(world, ref eid, n1, n2, RoadTier.Asphalt, 1.0f);

        var ind = new Industry(1, "Fazenda Teste", 2, 2, ResourceType.Grain, ResourceType.None, 20);
        var city = new City(1, "Cidade Destino", 3, 2);
        world.Industries.Add(ind);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 20_000;

        var station = engine.BuildTollStation(2, 2, out string msg);
        Assert.NotNull(station);
        Assert.Equal(20_000 - TollStation.ConstructionCost, engine.Player.Balance);

        var truck = new Truck(1, "VUC Pedágio", ProgressionManager.AvailableTruckModels[0])
        {
            Condition = 95f,
            LoadedCargoType = ResourceType.Grain,
            LoadedCargoAmount = 10,
            AssignedTargetCity = city,
            Status = TruckStatus.TravelingToDelivery
        };
        truck.AssignPath(new[] { edge });
        engine.ActiveTrucks.Add(truck);

        // Avança 24 ticks (1 dia -> inclui entrega + pedágio de viagem + pedágio passivo diário)
        for (int t = 0; t < 24; t++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(station!.TrucksInspected >= 1);
        Assert.True(station.TotalTollCollected >= TollStation.DefaultTollFee + TollStation.WeighStationComplianceBonus);
        Assert.True(engine.Financials.CurrentMonth.TollRevenue >= station.TotalTollCollected);
    }

    [Fact]
    public void CorridorProfitability_TracksRevenueAcrossTraversedEdgesAndDecaysMonthly()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 5);
        var n2 = new RoadNode(2, 3, 5);
        var n3 = new RoadNode(3, 4, 5);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });
        int eid = 1;
        var e1 = LinkBiDirectional(world, ref eid, n1, n2, RoadTier.Asphalt, 1.0f);
        var e2 = LinkBiDirectional(world, ref eid, n2, n3, RoadTier.Asphalt, 1.0f);

        var city = new City(1, "Porto Verde", 4, 5);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        var truck = new Truck(1, "Carreta Lucro", ProgressionManager.AvailableTruckModels[0])
        {
            LoadedCargoType = ResourceType.Food,
            LoadedCargoAmount = 12,
            AssignedTargetCity = city,
            Status = TruckStatus.TravelingToDelivery
        };
        truck.AssignPath(new[] { e1, e2 });
        engine.ActiveTrucks.Add(truck);

        for (int t = 0; t < 16; t++)
        {
            engine.Tick(0.25f);
        }

        long revAtTile = engine.GetCellCorridorRevenue(3, 5);
        Assert.True(revAtTile > 0, "Receita de entrega deve ser atribuída ao corredor percorrido.");

        // Virada de mês decai receita de janela de 30 dias para ~35%
        int curMonth = engine.Calendar.Month;
        while (engine.Calendar.Month == curMonth)
        {
            engine.Tick(1.0f);
        }

        long decayedRev = engine.GetCellCorridorRevenue(3, 5);
        Assert.True(decayedRev < revAtTile, "Receita acumulada do corredor deve decair na virada mensal.");
    }

    [Fact]
    public void RivalLogistics_CompetesForMarketShareAndClaimsExpiringContract()
    {
        var world = CreateSimpleGridWorld();
        var city = new City(1, "Metrópole Teste", 6, 6);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        Assert.True(engine.Rivals.Count >= 2);

        // Avança 1 dia para IA acumular volume regional
        for (int t = 0; t < 24; t++)
        {
            engine.Tick(0.25f);
        }

        var standings = engine.GetMarketShareStandings();
        Assert.Contains(standings, s => s.IsPlayer);
        Assert.Contains(standings, s => !s.IsPlayer && s.DeliveredVolume > 0);
        float totalShare = standings.Sum(s => s.SharePercent);
        Assert.InRange(totalShare, 99.5f, 100.5f);

        var rival = engine.Rivals[0];
        var contract = engine.ActiveContracts[0];
        RivalCompany? eventRival = null;
        engine.OnContractLostToRival += (_, r) => eventRival = r;

        bool claimed = engine.ClaimContractForRival(contract, rival);
        Assert.True(claimed);
        Assert.True(contract.IsLostToRival);
        Assert.Equal(rival.Name, contract.ClaimedByRivalName);
        Assert.Same(rival, eventRival);
        Assert.Equal(1, rival.CompletedContracts);
    }

    [Fact]
    public void SaveLoad_And_SaveSlotSummary_PreserveAllRemainingFeatures()
    {
        var world = CreateSimpleGridWorld();
        var cNode = new RoadNode(1, 5, 5);
        var eNode = new RoadNode(2, 6, 5);
        var nNode = new RoadNode(3, 5, 4);
        var sNode = new RoadNode(4, 5, 6);
        world.RoadNodes.AddRange(new[] { cNode, eNode, nNode, sNode });

        int eid = 1;
        var edge = LinkBiDirectional(world, ref eid, cNode, eNode, RoadTier.Asphalt, 2.0f);
        LinkBiDirectional(world, ref eid, cNode, nNode, RoadTier.Asphalt, 2.0f);
        LinkBiDirectional(world, ref eid, cNode, sNode, RoadTier.Asphalt, 2.0f);

        var city = new City(1, "Centro Norte", 9, 9);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 40_000;
        Assert.True(engine.TryBuildTrafficControl(5, 5, out _));
        var toll = engine.BuildTollStation(6, 5, out _);
        Assert.NotNull(toll);

        edge.RecentRevenue30d = 2450;
        engine.Rivals[0].DeliveredVolume = 330;
        engine.Financials.RecordTollRevenue(500);

        string tempPath = Path.Combine(Path.GetTempPath(), $"te_slot_test_{Guid.NewGuid():N}.json");
        try
        {
            SaveGameSerializer.SaveToFile(tempPath, engine, seed: 98765);

            var summary = SaveGameSerializer.ReadSaveSummary(tempPath, "Slot Teste");
            Assert.True(summary.Exists);
            Assert.Equal(98765, summary.Seed);
            Assert.Equal(1, summary.CityCount);
            Assert.Equal(engine.Player.Balance, summary.Balance);

            var missingSummary = SaveGameSerializer.ReadSaveSummary(tempPath + ".missing", "Vazio");
            Assert.False(missingSummary.Exists);

            var (loaded, seed) = SaveGameSerializer.LoadFromFile(tempPath);
            Assert.Equal(98765, seed);
            Assert.True(loaded.HasTrafficControlAt(5, 5));
            Assert.Single(loaded.TollStations);
            Assert.Equal(toll!.Name, loaded.TollStations[0].Name);
            Assert.Equal(2450, loaded.GetCellCorridorRevenue(6, 5));
            Assert.Equal(330, loaded.Rivals[0].DeliveredVolume);
            Assert.Equal(500, loaded.Financials.CurrentMonth.TollRevenue);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void IconicTruckCatalog_MatchesShowcaseAndResolvesLegacySaveNames()
    {
        var models = ProgressionManager.AvailableTruckModels;
        Assert.Equal(8, models.Count);
        Assert.Equal("Utilitário F-4000 Carga Seca", models[0].Name);
        Assert.Equal("Scania 113H TopLine 360", models[1].Name);
        Assert.Equal(28, models[1].Capacity);
        Assert.Equal(75f, models[1].MaxTopSpeedKmh);
        Assert.Equal("Mercedes-Benz L-1620 Bicudo", models[2].Name);
        Assert.Equal("Volvo FH 540 Globetrotter 6x2", models[3].Name);
        Assert.Equal("Peterbilt 389 Bi-Trem King", models[4].Name);
        Assert.Equal("Scania R730 V8 Rodotrem 9 Eixos", models[5].Name);
        Assert.True(models[5].RequiresHighwayOrDualCarriage);
        Assert.True(models[6].IsPassengerBus);
        Assert.True(models[7].IsPassengerBus);

        Assert.Equal(models[1], ProgressionManager.ResolveSpecByName("Caminhão Leve 3/4"));
        Assert.Equal(models[4], ProgressionManager.ResolveSpecByName("Carreta Bi-Trem Estradeira"));
        Assert.Equal(models[5], ProgressionManager.ResolveSpecByName("Comboio Rodotrem 9 Eixos"));
    }
}

