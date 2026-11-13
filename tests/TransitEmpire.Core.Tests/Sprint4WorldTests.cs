using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Pathfinding;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class Sprint4WorldTests
{
    private static GeneratedWorld CreateSimpleGridWorld(int width = 12, int height = 12)
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

    private static RoadEdge LinkNodes(GeneratedWorld world, int id, RoadNode a, RoadNode b, RoadTier tier, float distKm)
    {
        world.Grid[a.GridX, a.GridY].RoadLevel = tier;
        world.Grid[b.GridX, b.GridY].RoadLevel = tier;
        var edge = new RoadEdge(id, a, b, tier, distKm);
        a.OutgoingEdges.Add(edge);
        world.RoadEdges.Add(edge);
        return edge;
    }

    [Fact]
    public void RoadPavement_WearsUnderTraffic_SlowsSpeed_AndRepavesCleanly()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 1, 1);
        var n2 = new RoadNode(2, 2, 1);
        world.RoadNodes.Add(n1);
        world.RoadNodes.Add(n2);
        var edge = LinkNodes(world, 1, n1, n2, RoadTier.Dirt, 250f);
        LinkNodes(world, 2, n2, n1, RoadTier.Dirt, 250f);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 10_000;

        float freshSpeed = edge.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear);

        var heavySpec = new TruckSpecification("Bitrem Teste", 220, 90f, 10000, 10, 1);
        var truck = new Truck(1, "Caminhão Teste", heavySpec);
        truck.AssignPath(new[] { edge });
        truck.Status = TruckStatus.TravelingToDelivery;
        engine.ActiveTrucks.Add(truck);

        // Simula 30 ticks de viagem pesada sobre estrada de terra para ultrapassar a zona morta de desgaste (< 85%)
        for (int i = 0; i < 30; i++)
        {
            truck.Advance(0.5f, WeatherCondition.Clear);
        }

        Assert.True(edge.PavementCondition < 85f, $"Pavimento deveria cair abaixo de 85%, mas ficou em {edge.PavementCondition:F1}%.");
        float wornSpeed = edge.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear);
        Assert.True(wornSpeed < freshSpeed, "Via desgastada deve reduzir velocidade efetiva.");

        long balBefore = engine.Player.Balance;
        bool repaved = engine.TryRepaveRoadTile(1, 1, out string msg);
        Assert.True(repaved, msg);
        Assert.Equal(100f, edge.PavementCondition);
        Assert.True(engine.Player.Balance < balBefore);
        Assert.True(engine.Financials.CurrentMonth.RoadMaintenanceExpense > 0);
    }

    [Fact]
    public void AutoRoadMaintenance_RepairsWornEdgesAndLogsExpense()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 1, 1);
        var n2 = new RoadNode(2, 2, 1);
        var n3 = new RoadNode(3, 3, 1);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });

        var wornEdge = LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, 5f);
        wornEdge.PavementCondition = 40f;

        var goodEdge = LinkNodes(world, 2, n2, n3, RoadTier.Asphalt, 5f);
        goodEdge.PavementCondition = 88f;

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 10_000;

        int repaired = engine.RunAutoRoadMaintenance(out long totalCost);

        Assert.Equal(1, repaired);
        Assert.True(totalCost > 0);
        Assert.Equal(100f, wornEdge.PavementCondition);
        Assert.Equal(88f, goodEdge.PavementCondition);
        Assert.Equal(totalCost, engine.Financials.CurrentMonth.RoadMaintenanceExpense);
    }

    [Fact]
    public void BumperHarvest_DoublesGrainFarmDailyProduction()
    {
        var worldA = CreateSimpleGridWorld();
        var farmA = new Industry(1, "Fazenda Normal", 3, 3, ResourceType.Grain, ResourceType.None, 20) { OutputStock = 0 };
        worldA.Industries.Add(farmA);
        var engineNormal = new SimulationEngine(worldA);

        var worldB = CreateSimpleGridWorld();
        var farmB = new Industry(1, "Fazenda Safra", 3, 3, ResourceType.Grain, ResourceType.None, 20) { OutputStock = 0 };
        worldB.Industries.Add(farmB);
        var engineHarvest = new SimulationEngine(worldB);
        Assert.True(engineHarvest.TriggerSeasonalEvent(SeasonalEventType.BumperHarvest, 72f));

        for (int t = 0; t < 24; t++)
        {
            engineNormal.Tick(0.25f);
            engineHarvest.Tick(0.25f);
        }

        Assert.Equal(20, farmA.OutputStock);
        Assert.Equal(40, farmB.OutputStock);
    }

    [Fact]
    public void WinterBlizzard_SeverelySlowsUnpavedAlpineRoadsOnly()
    {
        var world = CreateSimpleGridWorld();
        world.Grid[2, 2] = new GridCell(2, 2, TerrainType.Hills, BiomeType.Alpine);

        var dirtAlpine = new RoadEdge(1, new RoadNode(1, 2, 2), new RoadNode(2, 3, 2), RoadTier.Dirt, 2f)
        {
            IsAlpineBiome = true
        };
        var asphaltAlpine = new RoadEdge(2, new RoadNode(3, 4, 2), new RoadNode(4, 5, 2), RoadTier.Asphalt, 2f)
        {
            IsAlpineBiome = true
        };

        float dirtClear = dirtAlpine.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear, winterBlizzard: false);
        float dirtBlizzard = dirtAlpine.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear, winterBlizzard: true);

        float asphaltClear = asphaltAlpine.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear, winterBlizzard: false);
        float asphaltBlizzard = asphaltAlpine.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear, winterBlizzard: true);

        Assert.Equal(dirtClear * 0.45f, dirtBlizzard, precision: 2);
        Assert.Equal(asphaltClear * 0.85f, asphaltBlizzard, precision: 2);
    }

    [Fact]
    public void MountainLandslide_BlocksMountainEdge_AndReroutesPathfinderUntilCleared()
    {
        var world = CreateSimpleGridWorld();
        // Rota direta passa por montanha em (3,2); rota alternativa desvia por planície (3,5)
        world.Grid[3, 2] = new GridCell(3, 2, TerrainType.Mountain, BiomeType.Alpine);

        var start = new RoadNode(1, 2, 2);
        var mountainPass = new RoadNode(2, 3, 2);
        var detour = new RoadNode(3, 3, 5);
        var end = new RoadNode(4, 4, 2);
        world.RoadNodes.AddRange(new[] { start, mountainPass, detour, end });

        // Direta curta (2 km total)
        LinkNodes(world, 1, start, mountainPass, RoadTier.Asphalt, 1.0f);
        LinkNodes(world, 2, mountainPass, start, RoadTier.Asphalt, 1.0f);
        LinkNodes(world, 3, mountainPass, end, RoadTier.Asphalt, 1.0f);
        LinkNodes(world, 4, end, mountainPass, RoadTier.Asphalt, 1.0f);

        // Desvio mais longo (8 km total)
        LinkNodes(world, 5, start, detour, RoadTier.Asphalt, 4.0f);
        LinkNodes(world, 6, detour, start, RoadTier.Asphalt, 4.0f);
        LinkNodes(world, 7, detour, end, RoadTier.Asphalt, 4.0f);
        LinkNodes(world, 8, end, detour, RoadTier.Asphalt, 4.0f);

        var engine = new SimulationEngine(world);
        var normalPath = engine.Pathfinder.FindFastestRoute(start, end);
        Assert.NotNull(normalPath);
        Assert.Contains(normalPath!, e => e.ToNode.Id == mountainPass.Id);

        Assert.True(engine.TriggerSeasonalEvent(SeasonalEventType.MountainLandslide, 36f));
        Assert.Equal(SeasonalEventType.MountainLandslide, engine.ActiveSeasonalEvent);
        Assert.True(engine.BlockedLandslideEdgeId > 0);

        var reroutedPath = engine.Pathfinder.FindFastestRoute(start, end);
        Assert.NotNull(reroutedPath);
        Assert.DoesNotContain(reroutedPath!, e => e.IsBlocked);
        Assert.Contains(reroutedPath!, e => e.ToNode.Id == detour.Id);

        // Avança 37 horas (148 ticks de 0.25h) até o fim automático do deslizamento
        for (int t = 0; t < 148; t++)
        {
            engine.Tick(0.25f);
        }

        Assert.Equal(SeasonalEventType.None, engine.ActiveSeasonalEvent);
        Assert.Equal(0, engine.BlockedLandslideEdgeId);
        Assert.All(world.RoadEdges, e => Assert.False(e.IsBlocked));

        var restoredPath = engine.Pathfinder.FindFastestRoute(start, end);
        Assert.NotNull(restoredPath);
        Assert.Contains(restoredPath!, e => e.ToNode.Id == mountainPass.Id);
    }

    [Fact]
    public void CityHighSatisfaction_GrantsMonthlySubsidies_AndAcceleratesExpansion()
    {
        var world = CreateSimpleGridWorld(20, 20);
        var happyCity = new City(1, "Vila Feliz", 5, 5) { Population = 500 };
        happyCity.Warehouse.DailyDemand.Clear();
        happyCity.Warehouse.DailyDemand[ResourceType.Grain] = 10;
        happyCity.Warehouse.StoredStock[ResourceType.Grain] = 500;

        var unhappyCity = new City(2, "Vila Seca", 15, 15) { Population = 500 };
        unhappyCity.Warehouse.DailyDemand.Clear();
        unhappyCity.Warehouse.DailyDemand[ResourceType.Grain] = 10;
        unhappyCity.Warehouse.StoredStock[ResourceType.Grain] = 0;

        world.Cities.Add(happyCity);
        world.Cities.Add(unhappyCity);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 10_000;
        engine.AutoRoadMaintenanceEnabled = false;

        // 2 ciclos diários (48 ticks) com 100% de satisfação devem expandir Vila Feliz (acelerada para >= 92%)
        for (int t = 0; t < 48; t++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(happyCity.ExpansionLevel >= 2, "Satisfação >= 92% deve acelerar expansão para 2 ciclos.");
        Assert.Equal(1, unhappyCity.ExpansionLevel);

        // Avança até virar o mês mantendo todos os recursos demandados abastecidos em Vila Feliz
        int startMonth = engine.Calendar.Month;
        while (engine.Calendar.Month == startMonth)
        {
            foreach (var res in happyCity.Warehouse.DailyDemand.Keys)
            {
                happyCity.Warehouse.StoredStock[res] = 500;
            }
            engine.Tick(1.0f);
        }

        Assert.True(happyCity.TotalSubsidiesReceived > 0);
        Assert.Equal(0, unhappyCity.TotalSubsidiesReceived);
        Assert.True(engine.Financials.MonthlyHistory[^1].MunicipalSubsidyRevenue > 0);
    }

    [Fact]
    public void SaveLoad_PreservesSprint4WorldState()
    {
        var world = CreateSimpleGridWorld();
        world.Grid[3, 3] = new GridCell(3, 3, TerrainType.Mountain, BiomeType.Alpine);

        var n1 = new RoadNode(1, 2, 3);
        var n2 = new RoadNode(2, 3, 3);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        var edge = LinkNodes(world, 1, n1, n2, RoadTier.Gravel, 3f);
        edge.PavementCondition = 54.5f;

        var city = new City(1, "Serra Alta", 7, 7)
        {
            Population = 420,
            LastSatisfactionRate = 0.94f,
            TotalSubsidiesReceived = 1280
        };
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.AutoRoadMaintenanceEnabled = false;
        Assert.True(engine.TriggerSeasonalEvent(SeasonalEventType.MountainLandslide, 28f));

        string json = SaveGameSerializer.SerializeToJson(engine, seed: 42);
        var (loadedEngine, loadedSeed) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Equal(42, loadedSeed);
        Assert.False(loadedEngine.AutoRoadMaintenanceEnabled);
        Assert.Equal(SeasonalEventType.MountainLandslide, loadedEngine.ActiveSeasonalEvent);
        Assert.Equal(engine.BlockedLandslideEdgeId, loadedEngine.BlockedLandslideEdgeId);
        Assert.InRange(loadedEngine.SeasonalEventRemainingHours, 27.5f, 28.5f);

        var loadedEdge = Assert.Single(loadedEngine.World.RoadEdges);
        Assert.Equal(54.5f, loadedEdge.PavementCondition, precision: 2);
        Assert.True(loadedEdge.IsBlocked);
        Assert.True(loadedEdge.IsAlpineBiome);

        var loadedCity = Assert.Single(loadedEngine.World.Cities);
        Assert.Equal(0.94f, loadedCity.LastSatisfactionRate, precision: 2);
        Assert.Equal(1280, loadedCity.TotalSubsidiesReceived);
    }

    [Fact]
    public void WorldGenerator_DoesNotCreateSenseless1x1RoadLoops_AndPreservesConnectivity()
    {
        int[] seeds = { 1, 23, 30, 37, 44, 109, 191, 1337 };
        foreach (int seed in seeds)
        {
            var world = new WorldGenerator(new WorldGenerationConfig(100, 100, 6, 10, seed)).Generate();
            int w = world.Grid.GetLength(0);
            int h = world.Grid.GetLength(1);

            for (int x = 1; x < w - 1; x++)
            {
                for (int y = 1; y < h - 1; y++)
                {
                    bool isSenseless1x1Loop = !world.Grid[x, y].HasRoad
                        && world.Grid[x, y].OccupantId == 0
                        && world.Grid[x - 1, y].HasRoad
                        && world.Grid[x + 1, y].HasRoad
                        && world.Grid[x, y - 1].HasRoad
                        && world.Grid[x, y + 1].HasRoad;

                    Assert.False(isSenseless1x1Loop, $"Seed {seed} gerou quadrado 1x1 sem sentido em ({x},{y}).");
                }
            }

            // Cada cidade deve possuir ao menos 1 ponto de acesso rodoviário válido no grid
            foreach (var city in world.Cities)
            {
                var access = WorldGenerator.GetCityAccessPoint(world, city);
                Assert.True(world.Grid[access.X, access.Y].HasRoad, $"Cidade {city.Name} (seed {seed}) ficou sem acesso viário.");
            }
        }
    }

    [Fact]
    public void TryTerraformTile_LevelsMountainHillsForest_AndPreservesObstacles()
    {
        var world = new GeneratedWorld(10, 10);
        world.Grid[2, 2].Terrain = TerrainType.Mountain;
        world.Grid[3, 3].Terrain = TerrainType.Forest;
        world.Grid[4, 4].Terrain = TerrainType.Water;
        world.Grid[5, 5].Terrain = TerrainType.Hills;
        world.Grid[5, 5].RoadLevel = RoadTier.Dirt;

        var engine = new SimulationEngine(world);
        long startBalance = engine.Player.Balance;

        // Montanha -> Colina -> Planície
        Assert.True(engine.TryTerraformTile(2, 2, out var step1, out _));
        Assert.Equal(TerrainType.Mountain, step1.PreviousTerrain);
        Assert.Equal(TerrainType.Hills, step1.NewTerrain);
        Assert.Equal(TerrainType.Hills, world.Grid[2, 2].Terrain);

        Assert.True(engine.TryTerraformTile(2, 2, out var step2, out _));
        Assert.Equal(TerrainType.Plains, step2.NewTerrain);
        Assert.Equal(TerrainType.Plains, world.Grid[2, 2].Terrain);
        Assert.Equal(startBalance - 220L - 120L, engine.Player.Balance);

        // Floresta -> Planície
        Assert.True(engine.TryTerraformTile(3, 3, out var forestStep, out _));
        Assert.Equal(TerrainType.Plains, forestStep.NewTerrain);

        // Água e célula com estrada não podem ser terraplenadas
        Assert.False(engine.TryTerraformTile(4, 4, out _, out _));
        Assert.False(engine.TryTerraformTile(5, 5, out _, out _));
    }

    [Fact]
    public void WorldGenerator_SupportsConfiguredSizes_AndDistinctThematicScenarios()
    {
        var compact = new WorldGenerator(new WorldGenerationConfig(50, 50, 4, 7, 2026, WorldScenario.Balanced)).Generate();
        Assert.Equal(50, compact.Grid.GetLength(0));
        Assert.Equal(50, compact.Grid.GetLength(1));
        Assert.True(compact.Cities.Count >= 2);

        var archipelago = new WorldGenerator(new WorldGenerationConfig(80, 80, 5, 9, 4242, WorldScenario.CoastalArchipelago)).Generate();
        var alpine = new WorldGenerator(new WorldGenerationConfig(80, 80, 5, 9, 4242, WorldScenario.AlpineRange)).Generate();

        int waterArch = 0, alpineCountArch = 0;
        int waterAlp = 0, alpineCountAlp = 0;
        for (int x = 0; x < 80; x++)
        {
            for (int y = 0; y < 80; y++)
            {
                if (archipelago.Grid[x, y].Terrain == TerrainType.Water) waterArch++;
                if (archipelago.Grid[x, y].Biome == BiomeType.Alpine) alpineCountArch++;
                if (alpine.Grid[x, y].Terrain == TerrainType.Water) waterAlp++;
                if (alpine.Grid[x, y].Biome == BiomeType.Alpine) alpineCountAlp++;
            }
        }

        Assert.True(waterArch > waterAlp, $"Arquipélago deveria ter mais água ({waterArch}) que Cordilheira Alpina ({waterAlp}).");
        Assert.True(alpineCountAlp > alpineCountArch, $"Cordilheira Alpina deveria ter mais bioma alpino ({alpineCountAlp}) que Arquipélago ({alpineCountArch}).");
    }

    [Fact]
    public void ScheduleStop_CargoFilterAndMaxWaitHours_EnforcedDuringDispatch()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, 2f);
        LinkNodes(world, 2, n2, n1, RoadTier.Asphalt, 2f);

        var farm = new Industry(1, "Fazenda Sul", 2, 1, ResourceType.Grain, ResourceType.None, 0)
        {
            OutputStock = 10
        };
        var city = new City(1, "Cidade Nova", 4, 3);
        city.Warehouse.DailyDemand[ResourceType.Grain] = 100;
        world.Industries.Add(farm);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;
        var garage = engine.BuildGarage(2, 3, "Garagem Sul")!;
        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;

        // Filtro incompatível (pede Timber na Fazenda de Grain) com WaitFullLoad + MaxWaitHours = 12h:
        // fica retido até atingir 12h mesmo com Grain disponível, então parte após MaxWaitHours.
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, farm.Id, ScheduleAction.Load, DispatchCondition.WaitFullLoad, ResourceType.Timber, maxWaitHours: 12f),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });

        engine.ProcessScheduledTrucks();
        Assert.Equal(TruckStatus.WaitingForCargo, truck.Status);
        Assert.Equal(0, truck.LoadedCargoAmount);

        // Avança 13 horas -> MaxWaitHours vencido -> despacha para parada 1 (sem carregar Grain proibido pelo filtro)
        for (int i = 0; i < 13; i++)
            engine.Tick(1.0f);

        Assert.NotEqual(TruckStatus.WaitingForCargo, truck.Status);
        Assert.Equal(0, truck.LoadedCargoAmount);
    }

    [Fact]
    public void LogisticsLine_HeadwayControl_HoldsSecondTruckUntilIntervalElapses()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 8, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, 250f);
        LinkNodes(world, 2, n2, n1, RoadTier.Asphalt, 250f);

        var farm = new Industry(1, "Fazenda Grãos", 2, 1, ResourceType.Grain, ResourceType.None, 0)
        {
            OutputStock = 200
        };
        var city = new City(1, "Metrópole", 8, 3);
        city.Warehouse.DailyDemand[ResourceType.Grain] = 200;
        world.Industries.Add(farm);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;
        var garage = engine.BuildGarage(2, 3, "Garagem Express")!;
        var t1 = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        var t2 = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;

        var line = engine.CreateLogisticsLine("Linha Express", new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, farm.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });
        engine.UpdateLineAutomation(line.Id, headwayEnabled: true, minHeadwayHours: 4.0f, autoReplaceEnabled: false);
        engine.AssignTruckToLogisticsLine(t1, line);
        engine.AssignTruckToLogisticsLine(t2, line);

        engine.ProcessScheduledTrucks();

        // Primeiro caminhão parte imediatamente; segundo é segurado por Headway na parada inicial
        Assert.Equal(TruckStatus.TravelingToDelivery, t1.Status);
        Assert.False(t1.IsHeldForHeadway);
        Assert.Equal(TruckStatus.WaitingForCargo, t2.Status);
        Assert.True(t2.IsHeldForHeadway);

        // Após 4.5 horas simuladas, o Headway libera o segundo caminhão
        for (int i = 0; i < 9; i++)
            engine.Tick(0.5f);

        Assert.False(t2.IsHeldForHeadway);
        Assert.Equal(TruckStatus.TravelingToDelivery, t2.Status);
    }

    [Fact]
    public void AutoReplace_GarageRule_ReplacesWornIdleTruckAndPreservesSchedule()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, 10f);
        LinkNodes(world, 2, n2, n1, RoadTier.Asphalt, 10f);

        var farm = new Industry(1, "Fazenda", 2, 1, ResourceType.Grain);
        var city = new City(1, "Destino", 4, 3);
        world.Industries.Add(farm);
        world.Cities.Add(city);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;

        var garage = engine.BuildGarage(2, 3, "Garagem Principal")!;
        Assert.True(engine.UpdateGarageAutoReplace(garage.Id, enabled: true, minCondition: 25f));

        var baseSpec = ProgressionManager.AvailableTruckModels[0];
        var oldTruck = engine.BuyTruck(garage, baseSpec)!;
        oldTruck.Condition = 18f;
        oldTruck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, farm.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });
        engine.Player.Balance = 100_000;

        Truck? replacedOld = null;
        Truck? replacedNew = null;
        engine.OnTruckAutoReplaced += (o, n, _) => { replacedOld = o; replacedNew = n; };

        engine.Tick(0.5f);

        Assert.Same(oldTruck, replacedOld);
        Assert.NotNull(replacedNew);
        Assert.NotEqual(oldTruck.Id, replacedNew!.Id);
        Assert.Equal(baseSpec.Name, replacedNew.Spec.Name);
        Assert.Equal(100f, replacedNew.Condition);
        Assert.Equal(2, replacedNew.Schedule.Count);
        Assert.Contains(replacedNew, engine.ActiveTrucks);
        Assert.DoesNotContain(oldTruck, engine.ActiveTrucks);
        Assert.Contains(replacedNew, garage.StationedTrucks);
        Assert.DoesNotContain(oldTruck, garage.StationedTrucks);

        long expectedNetCost = baseSpec.PurchasePrice - (long)System.Math.Round(baseSpec.PurchasePrice * 0.60);
        Assert.Equal(100_000 - expectedNetCost, engine.Player.Balance);
    }

    [Fact]
    public void SaveLoad_PreservesSprintF2AutomationAndStopRules()
    {
        var world = CreateSimpleGridWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, 1f);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;
        var garage = engine.BuildGarage(2, 3, "Garagem F2")!;
        garage.AutoReplaceEnabled = true;
        garage.AutoReplaceMinCondition = 30f;
        garage.AutoReplaceMaxMileageKm = 12_500f;
        garage.AutoReplaceTargetSpecName = ProgressionManager.AvailableTruckModels[0].Name;

        var line = engine.CreateLogisticsLine("Linha F2", new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, 1, ScheduleAction.Load, DispatchCondition.WaitFullLoad, ResourceType.Grain, maxWaitHours: 24f),
            new ScheduleStop(ScheduleStopKind.City, 1, ScheduleAction.Unload, DispatchCondition.Immediate, ResourceType.Grain, maxWaitHours: 0f)
        });
        engine.UpdateLineAutomation(line.Id, headwayEnabled: true, minHeadwayHours: 6.0f, autoReplaceEnabled: true, minCondition: 22f, maxMileageKm: 20_000f, targetSpecName: ProgressionManager.AvailableTruckModels[0].Name);

        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.AssignedLineId = line.Id;
        truck.CurrentStopWaitHours = 7.5f;
        truck.IsHeldForHeadway = true;

        string json = SaveGameSerializer.SerializeToJson(engine, seed: 77);
        var (loaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        var loadedGarage = Assert.Single(loaded.Garages);
        Assert.True(loadedGarage.AutoReplaceEnabled);
        Assert.Equal(30f, loadedGarage.AutoReplaceMinCondition, precision: 2);
        Assert.Equal(12_500f, loadedGarage.AutoReplaceMaxMileageKm, precision: 1);
        Assert.Equal(ProgressionManager.AvailableTruckModels[0].Name, loadedGarage.AutoReplaceTargetSpecName);

        var loadedLine = Assert.Single(loaded.LogisticsLines);
        Assert.True(loadedLine.HeadwayControlEnabled);
        Assert.Equal(6.0f, loadedLine.MinHeadwayHours, precision: 2);
        Assert.True(loadedLine.AutoReplaceEnabled);
        Assert.Equal(22f, loadedLine.AutoReplaceMinCondition, precision: 2);
        Assert.Equal(20_000f, loadedLine.AutoReplaceMaxMileageKm, precision: 1);
        Assert.Equal(ResourceType.Grain, loadedLine.Stops[0].CargoFilter);
        Assert.Equal(24f, loadedLine.Stops[0].MaxWaitHours, precision: 2);

        var loadedTruck = Assert.Single(loaded.ActiveTrucks);
        Assert.Equal(7.5f, loadedTruck.CurrentStopWaitHours, precision: 2);
        Assert.True(loadedTruck.IsHeldForHeadway);
    }

    [Fact]
    public void BusTerminal_PassengerLines_EarnTicketRevenueAndAccelerateCityGrowth()
    {
        var world = CreateSimpleGridWorld(width: 24, height: 12);
        var cityA = new City(1, "Porto Alegre", 3, 5);
        var cityB = new City(2, "Caxias do Sul", 18, 5);
        world.Cities.Add(cityA);
        world.Cities.Add(cityB);

        var nA = new RoadNode(1, 5, 5);
        var nB = new RoadNode(2, 16, 5);
        var nG = new RoadNode(3, 5, 6);
        world.RoadNodes.AddRange(new[] { nA, nB, nG });
        LinkNodes(world, 1, nA, nB, RoadTier.Asphalt, 12f);
        LinkNodes(world, 2, nB, nA, RoadTier.Asphalt, 12f);
        LinkNodes(world, 3, nG, nA, RoadTier.Asphalt, 1f);
        LinkNodes(world, 4, nA, nG, RoadTier.Asphalt, 1f);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 150_000;

        var garage = engine.BuildGarage(5, 7, "Garagem Rodoviária")!;
        var termA = engine.BuildBusTerminal(5, 4, out _, "Rodoviária Porto Alegre")!;
        var termB = engine.BuildBusTerminal(16, 4, out _, "Rodoviária Caxias")!;
        Assert.NotNull(garage);
        Assert.NotNull(termA);
        Assert.NotNull(termB);

        termA.WaitingPassengers = 40;
        var busSpec = ProgressionManager.AvailableTruckModels.First(s => s.IsPassengerBus);
        var bus = engine.BuyTruck(garage, busSpec)!;
        Assert.NotNull(bus);
        Assert.True(bus.Spec.IsPassengerBus);

        bus.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.BusTerminal, termA.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.BusTerminal, termB.Id, ScheduleAction.Unload)
        });

        int initialPopB = cityB.Population;
        for (int i = 0; i < 60; i++)
        {
            engine.Tick(0.25f);
            if (engine.Financials.Lifetime.TicketRevenue > 0)
                break;
        }

        Assert.True(engine.Financials.Lifetime.TicketRevenue > 0);
        Assert.True(termA.WaitingPassengers < 40);
        Assert.True(termB.TotalPassengersServed > 0);
        Assert.True(termB.TotalTicketRevenue > 0);
        Assert.True(cityB.ActiveBusServiceHours > 0);
        Assert.True(cityB.Population > initialPopB);

        // Crescimento urbano acelerado quando cidade possui serviço rodoviário ativo (2 ciclos em vez de 3 e +10 pop)
        cityB.Warehouse.DailyDemand.Clear();
        cityB.Warehouse.DailyDemand[ResourceType.Grain] = 1;
        cityB.Warehouse.StoredStock[ResourceType.Grain] = 10;
        cityB.ConsecutiveCyclesWellSupplied = 1;
        int popBeforeDaily = cityB.Population;
        int expBefore = cityB.ExpansionLevel;

        for (int h = 0; h < 24; h++)
            engine.Tick(1.0f);

        Assert.True(cityB.ExpansionLevel > expBefore);
        Assert.True(cityB.Population >= popBeforeDaily + 10);
    }

    [Fact]
    public void Tier3Chain_CoalCokesSteelAndMachineryCombinesSteelLumberFuel_UnlockedInLargeCityAndLandmark()
    {
        var steelMill = new Industry(
            id: 1,
            name: "Siderúrgica Teste",
            gridX: 2,
            gridY: 2,
            output: ResourceType.Steel,
            input: ResourceType.IronOre,
            productionRate: 10,
            secondaryInput: ResourceType.Coal);

        steelMill.ReceiveInputCargo(ResourceType.IronOre, 40);
        steelMill.Produce();
        int baseSteelProduced = steelMill.OutputStock - 20;

        steelMill.ReceiveInputCargo(ResourceType.Coal, 10);
        steelMill.Produce();
        int boostedSteelProduced = steelMill.OutputStock - (20 + baseSteelProduced);
        Assert.True(boostedSteelProduced > baseSteelProduced);

        var machineryFactory = new Industry(
            id: 2,
            name: "Fábrica de Maquinário",
            gridX: 5,
            gridY: 2,
            output: ResourceType.Machinery,
            input: ResourceType.Steel,
            productionRate: 8,
            secondaryInput: ResourceType.Lumber,
            tertiaryInput: ResourceType.Fuel);

        machineryFactory.OutputStock = 0;
        machineryFactory.ReceiveInputCargo(ResourceType.Steel, 20);
        machineryFactory.ReceiveInputCargo(ResourceType.Lumber, 20);
        machineryFactory.ReceiveInputCargo(ResourceType.Fuel, 20);
        machineryFactory.Produce();
        Assert.True(machineryFactory.OutputStock > 8);
        Assert.True(machineryFactory.TertiaryInputStock < 20);

        // Cidade expandida para nível 5 (já atendida em Goods e Fuel) passa a demandar Machinery
        var city = new City(1, "Metrópole", 6, 6);
        city.Population = 480;
        city.Warehouse.DailyDemand[ResourceType.Goods] = 4;
        city.Warehouse.DailyDemand[ResourceType.Fuel] = 4;
        var (addedRes, qty, unlockedNew) = city.Warehouse.ExpandDemandForGrowth(expansionLevel: 5, population: city.Population);
        Assert.Equal(ResourceType.Machinery, addedRes);
        Assert.True(unlockedNew);
        Assert.True(qty >= 3);
        Assert.True(city.Warehouse.DailyDemand.ContainsKey(ResourceType.Machinery));

        // Mega-obra aceita Machinery e Stage 2 exige Machinery
        Assert.True(RegionalLandmark.AcceptsResource(ResourceType.Machinery));
        var landmark = new RegionalLandmark(1, "Usina Teste", LandmarkKind.HydroelectricDam, 8, 8);
        landmark.ContributeCargo(ResourceType.Steel, 30, out _, out _);
        landmark.ContributeCargo(ResourceType.Lumber, 30, out _, out _);
        Assert.Equal(2, landmark.CurrentStage);
        Assert.True(landmark.StageRequired.ContainsKey(ResourceType.Machinery));
    }

    [Fact]
    public void RealFuelSupply_HubReserveGrantsDiscount_AndUnfueledLongHaulPaysScarcitySurcharge_WithSaveRoundtrip()
    {
        static long MeasureMaintenanceOnLongRoute(bool supplyHubFuel)
        {
            var world = CreateSimpleGridWorld(width: 40, height: 12);
            var n1 = new RoadNode(1, 2, 5);
            var n2 = new RoadNode(2, 30, 5);
            world.RoadNodes.AddRange(new[] { n1, n2 });
            var edge = LinkNodes(world, 1, n1, n2, RoadTier.Asphalt, distKm: 50f);
            LinkNodes(world, 2, n2, n1, RoadTier.Asphalt, distKm: 50f);

            var engine = new SimulationEngine(world);
            engine.Player.Balance = 100_000;

            var hub = engine.BuildLogisticsHub(2, 4, "Hub Combustível")!;
            if (supplyHubFuel)
                hub.ReceiveFuel(100);

            var spec = new TruckSpecification("Bitrem Diesel", 120, 80f, 30_000, 10, 1);
            var truck = new Truck(1, "Frota Diesel", spec);
            truck.AssignPath(new[] { edge });
            truck.Status = TruckStatus.TravelingToDelivery;
            engine.ActiveTrucks.Add(truck);

            engine.Tick(1.0f);
            return engine.Financials.Lifetime.FleetMaintenanceExpense;
        }

        long costWithRegionalFuel = MeasureMaintenanceOnLongRoute(supplyHubFuel: true);
        long costWithoutFuelLongHaul = MeasureMaintenanceOnLongRoute(supplyHubFuel: false);

        Assert.True(costWithRegionalFuel > 0);
        Assert.True(costWithoutFuelLongHaul > costWithRegionalFuel * 1.5);

        // Save/Load preserva Hub.FuelStock, BusTerminal, City.ActiveBusServiceHours, Industry.TertiaryInput e TicketRevenue
        var saveWorld = CreateSimpleGridWorld(width: 18, height: 12);
        var c = new City(1, "Joinville", 4, 4) { ActiveBusServiceHours = 48 };
        saveWorld.Cities.Add(c);
        var ind = new Industry(1, "Fábrica Maq", 10, 8, ResourceType.Machinery, ResourceType.Steel, 8, ResourceType.Lumber, ResourceType.Fuel);
        ind.ReceiveInputCargo(ResourceType.Fuel, 25);
        saveWorld.Industries.Add(ind);
        var rn = new RoadNode(1, 6, 4);
        saveWorld.RoadNodes.Add(rn);

        var saveEngine = new SimulationEngine(saveWorld);
        saveEngine.Player.Balance = 150_000;
        var hubSave = saveEngine.BuildLogisticsHub(6, 5, "Hub Sul")!;
        hubSave.ReceiveFuel(180);
        var btSave = saveEngine.BuildBusTerminal(6, 3, out _, "Rodoviária Joinville")!;
        btSave.RecordArrival(22, 950);
        saveEngine.Financials.RecordTicketRevenue(950);

        string json = SaveGameSerializer.SerializeToJson(saveEngine, seed: 303);
        var (loaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Equal(950, loaded.Financials.Lifetime.TicketRevenue);
        Assert.Equal(48, Assert.Single(loaded.World.Cities).ActiveBusServiceHours);
        var loadedInd = Assert.Single(loaded.World.Industries);
        Assert.Equal(ResourceType.Fuel, loadedInd.TertiaryInputResource);
        Assert.Equal(25, loadedInd.TertiaryInputStock);
        var loadedHub = Assert.Single(loaded.LogisticsHubs);
        Assert.Equal(180, loadedHub.FuelStock);
        var loadedBt = Assert.Single(loaded.BusTerminals);
        Assert.Equal("Rodoviária Joinville", loadedBt.Name);
        Assert.Equal(22, loadedBt.TotalPassengersServed);
        Assert.Equal(950, loadedBt.TotalTicketRevenue);
    }
}
