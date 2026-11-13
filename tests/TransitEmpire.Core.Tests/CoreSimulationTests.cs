using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Pathfinding;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class CoreSimulationTests
{
    [Fact]
    public void WorldGenerator_ShouldGenerateCitiesAndInitialDirtRoads()
    {
        var config = new WorldGenerationConfig(Width: 50, Height: 50, CityCount: 3, IndustryCount: 3, Seed: 1234);
        var generator = new WorldGenerator(config);
        var world = generator.Generate();

        Assert.Equal(3, world.Cities.Count);
        Assert.Equal(3, world.Industries.Count);
        Assert.NotEmpty(world.RoadNodes);
        Assert.NotEmpty(world.RoadEdges);

        Assert.All(world.RoadEdges, edge => Assert.Equal(RoadTier.Dirt, edge.Tier));
    }

    [Fact]
    public void Player_CanUpgradeRoad_IncreasingSpeedAndDeductingBalance()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 1, Seed: 99));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        // Jogador sobe para nível 2 para poder aplicar asfalto
        engine.Player.Level = 2;
        long initialBalance = engine.Player.Balance;

        int roadX = -1, roadY = -1;
        for (int x = 0; x < 40; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                if (world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                {
                    roadX = x;
                    roadY = y;
                    break;
                }
            }
            if (roadX != -1) break;
        }

        Assert.True(roadX != -1, "Deve existir ao menos um bloco de estrada de terra");

        bool success = engine.UpgradeRoadTile(roadX, roadY, RoadTier.Asphalt);

        Assert.True(success);
        Assert.Equal(RoadTier.Asphalt, world.Grid[roadX, roadY].RoadLevel);
        Assert.True(engine.Player.Balance < initialBalance);
    }

    [Fact]
    public void PlayerLevel_ShouldRestrict_HighTierRoadUpgrades()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 1, Seed: 55));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        engine.Player.Level = 1; // Nível 1 não pode asfaltar para Rodovia (Tier 4)

        int roadX = -1, roadY = -1;
        for (int x = 0; x < 40; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                if (world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                {
                    roadX = x;
                    roadY = y;
                    break;
                }
            }
            if (roadX != -1) break;
        }

        bool upgradeAttempt = engine.UpgradeRoadTile(roadX, roadY, RoadTier.Highway);
        Assert.False(upgradeAttempt, "Jogador nível 1 não deve conseguir fazer upgrade para Rodovia sem antes progredir de nível");
    }

    [Fact]
    public void Calendar_AdvancesCorrectly_WithSimulationTicks()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 30, Height: 30, CityCount: 1, IndustryCount: 1, Seed: 10));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        Assert.Equal(5, engine.Calendar.Day);
        Assert.Equal(11, engine.Calendar.Month);
        Assert.Equal(2026, engine.Calendar.Year);

        // Avança 24 horas (96 ticks de 0.25h)
        for (int i = 0; i < 96; i++)
        {
            engine.Tick(0.25f);
        }

        // Deve ter virado para o dia 6
        Assert.Equal(6, engine.Calendar.Day);
        Assert.Equal(11, engine.Calendar.Month);
        Assert.Equal(2026, engine.Calendar.Year);
    }

    [Fact]
    public void City_ExpandsRandomly_WhenFullySuppliedForConsecutiveCycles()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 1, IndustryCount: 1, Seed: 42));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        var city = world.Cities[0];
        int initialTilesCount = city.UrbanTiles.Count;

        city.Warehouse.ReceiveCargo(ResourceType.Food, 100);
        city.Warehouse.ReceiveCargo(ResourceType.Lumber, 100);

        for (int i = 0; i < 96; i++)
        {
            engine.Tick(1.0f);
        }

        Assert.True(city.UrbanTiles.Count > initialTilesCount);
        Assert.True(city.ExpansionLevel > 1);
    }

    [Fact]
    public void RoadPathfinder_FindsOptimalRouteOnGraph()
    {
        var nodeA = new RoadNode(1, 0, 0);
        var nodeB = new RoadNode(2, 10, 0);
        var nodeC = new RoadNode(3, 20, 0);

        // Rota A -> B -> C
        var edgeAB = new RoadEdge(101, nodeA, nodeB, RoadTier.Dirt, 10f);
        var edgeBC = new RoadEdge(102, nodeB, nodeC, RoadTier.Asphalt, 10f);
        nodeA.OutgoingEdges.Add(edgeAB);
        nodeB.OutgoingEdges.Add(edgeBC);

        var pathfinder = new RoadPathfinder();
        var route = pathfinder.FindFastestRoute(nodeA, nodeC);

        Assert.NotNull(route);
        Assert.Equal(2, route.Count);
        Assert.Equal(101, route[0].Id);
        Assert.Equal(102, route[1].Id);
    }

    [Fact]
    public void Player_CanBuildGarage_AndBuyTruck()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 1, IndustryCount: 1, Seed: 88));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        // Procura uma célula livre (Plains, sem estrada e sem ocupante) que esteja a até 2 tiles de um nó viário
        int targetX = -1, targetY = -1;
        foreach (var node in world.RoadNodes)
        {
            int[] dx = { 0, 1, 0, -1, 1, -1, 1, -1 };
            int[] dy = { 1, 0, -1, 0, 1, 1, -1, -1 };
            for (int i = 0; i < 8; i++)
            {
                int testX = node.GridX + dx[i];
                int testY = node.GridY + dy[i];
                if (testX >= 0 && testX < 40 && testY >= 0 && testY < 40)
                {
                    ref var cell = ref world.Grid[testX, testY];
                    if (cell.Terrain == TerrainType.Plains && !cell.HasRoad && cell.OccupantId == 0)
                    {
                        targetX = testX;
                        targetY = testY;
                        break;
                    }
                }
            }
            if (targetX != -1) break;
        }

        Assert.True(targetX != -1, "Deve existir ao menos um local livre adjacente a uma via para construir garagem");

        var garage = engine.BuildGarage(targetX, targetY, "Garagem Central 01");
        Assert.NotNull(garage);
        Assert.Contains(garage, engine.Garages);

        var truckSpec = ProgressionManager.AvailableTruckModels.First();
        var truck = engine.BuyTruck(garage, truckSpec);

        Assert.NotNull(truck);
        Assert.Contains(truck, garage.StationedTrucks);
        Assert.Contains(truck, engine.ActiveTrucks);
    }

    [Fact]
    public void Player_CanUpgradeRoadCorridor_WithBulkDiscount()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 1, Seed: 77));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Level = 2;

        int startX = -1, startY = -1;
        for (int x = 0; x < 40; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                if (world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                {
                    startX = x;
                    startY = y;
                    break;
                }
            }
            if (startX != -1) break;
        }

        Assert.True(startX != -1);

        int upgradedCount = engine.UpgradeRoadCorridor(startX, startY, RoadTier.Asphalt, maxTiles: 10);
        Assert.True(upgradedCount > 1, "Deve atualizar múltiplos blocos conectados do corredor viário");
        Assert.Equal(RoadTier.Asphalt, world.Grid[startX, startY].RoadLevel);
    }

    [Fact]
    public void Player_CanBuildLogisticsHub_AndStoreTransshipmentCargo()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 1, IndustryCount: 1, Seed: 101));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        int targetX = -1, targetY = -1;
        foreach (var node in world.RoadNodes)
        {
            int[] dx = { 0, 1, 0, -1 };
            int[] dy = { 1, 0, -1, 0 };
            for (int i = 0; i < 4; i++)
            {
                int tx = node.GridX + dx[i];
                int ty = node.GridY + dy[i];
                if (tx >= 0 && tx < 40 && ty >= 0 && ty < 40)
                {
                    ref var cell = ref world.Grid[tx, ty];
                    if (cell.Terrain == TerrainType.Plains && !cell.HasRoad && cell.OccupantId == 0)
                    {
                        targetX = tx;
                        targetY = ty;
                        break;
                    }
                }
            }
            if (targetX != -1) break;
        }

        Assert.True(targetX != -1);

        var hub = engine.BuildLogisticsHub(targetX, targetY, "Hub Logístico Central");
        Assert.NotNull(hub);
        Assert.Contains(hub, engine.LogisticsHubs);

        int stored = hub!.StoreCargo(ResourceType.Grain, 120);
        Assert.Equal(120, stored);

        int withdrawn = hub.WithdrawCargo(ResourceType.Grain, 50);
        Assert.Equal(50, withdrawn);
        Assert.Equal(70, hub.CurrentTotalStored);
    }

    [Fact]
    public void LogisticsContract_RecordsDeliveries_AndPaysBonusOnCompletion()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 1, IndustryCount: 1, Seed: 202));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        var city = world.Cities[0];
        var contract = engine.CreateContractForCity(city, ResourceType.Food, amount: 30);

        Assert.False(contract.IsCompleted);
        Assert.True(contract.RecordDelivery(ResourceType.Food, 30));
        Assert.True(contract.IsCompleted);
    }

    [Fact]
    public void Weather_RainPenalizesDirtRoads_MoreThanAsphalt()
    {
        var nodeA = new RoadNode(1, 0, 0);
        var nodeB = new RoadNode(2, 10, 0);

        var dirtEdge = new RoadEdge(1, nodeA, nodeB, RoadTier.Dirt, 30f);
        var asphaltEdge = new RoadEdge(2, nodeA, nodeB, RoadTier.Asphalt, 30f);

        float dirtSpeedClear = dirtEdge.GetEffectiveSpeedLimitKmh(WeatherCondition.Clear);
        float dirtSpeedRain = dirtEdge.GetEffectiveSpeedLimitKmh(WeatherCondition.Rain);
        float asphaltSpeedRain = asphaltEdge.GetEffectiveSpeedLimitKmh(WeatherCondition.Rain);

        Assert.True(dirtSpeedRain < dirtSpeedClear * 0.7f, "Estrada de terra deve perder bastante velocidade na chuva");
        Assert.True(asphaltSpeedRain > dirtSpeedRain * 3.0f, "Estrada asfaltada mantém alta velocidade mesmo na chuva");
    }

    [Fact]
    public void Player_CanTakeAndRepayBankLoan()
    {
        var state = new PlayerState { Balance = 10000, Level = 1 };

        bool tookLoan = state.TakeLoan(25000);
        Assert.True(tookLoan);
        Assert.Equal(35000, state.Balance);
        Assert.Equal(25000, state.LoanDebt);

        bool repaid = state.RepayLoan(15000);
        Assert.True(repaid);
        Assert.Equal(20000, state.Balance);
        Assert.Equal(10000, state.LoanDebt);
    }

    [Fact]
    public void Research_UnlocksAsphaltAndExpressHighwaysWithPrerequisites()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 1, Seed: 77));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Level = 1;
        engine.Player.Balance = 50000;

        int roadX = -1, roadY = -1;
        for (int x = 0; x < 40 && roadX == -1; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                if (world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                {
                    roadX = x;
                    roadY = y;
                    break;
                }
            }
        }
        Assert.True(roadX != -1);

        // Pré-requisito bloqueia Rodovias Expressas antes de Pavimentação Asfáltica
        Assert.False(engine.Research.StartOrSwitchResearch(ResearchTechId.ExpressHighways, engine.Player, out _));
        Assert.False(engine.UpgradeRoadTile(roadX, roadY, RoadTier.Asphalt));

        // Inicia e conclui Pavimentação Asfáltica (20h) em ticks em tempo real
        Assert.True(engine.Research.StartOrSwitchResearch(ResearchTechId.AsphaltPaving, engine.Player, out _));
        for (int i = 0; i < 80; i++) // 80 * 0.25h = 20h
            engine.Tick(0.25f);

        Assert.True(engine.Research.IsCompleted(ResearchTechId.AsphaltPaving));
        Assert.True(engine.UpgradeRoadTile(roadX, roadY, RoadTier.Asphalt));

        // Agora Rodovias Expressas pode ser pesquisada
        Assert.True(engine.Research.StartOrSwitchResearch(ResearchTechId.ExpressHighways, engine.Player, out _));
        for (int i = 0; i < 144; i++) // 144 * 0.25h = 36h
            engine.Tick(0.25f);

        Assert.True(engine.Research.IsCompleted(ResearchTechId.ExpressHighways));
        Assert.True(engine.UpgradeRoadTile(roadX, roadY, RoadTier.Highway));
    }

    [Fact]
    public void Research_HeavyDieselEngines_BoostsSpeedAndUnlocksHeavyTrucks()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 30, Height: 30, CityCount: 1, IndustryCount: 1, Seed: 33));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Level = 1;
        engine.Player.Balance = 80000;

        var heavySpec = ProgressionManager.AvailableTruckModels[3]; // Caminhão Pesado Trucado (Nv.3)
        Assert.False(engine.Research.IsTruckUnlocked(heavySpec, engine.Player.Level));
        Assert.Equal(1.0f, engine.Research.FleetSpeedMultiplier);

        Assert.True(engine.Research.StartOrSwitchResearch(ResearchTechId.HeavyDieselEngines, engine.Player, out _));
        for (int i = 0; i < 96; i++) // 96 * 0.25h = 24h
            engine.Tick(0.25f);

        Assert.True(engine.Research.IsCompleted(ResearchTechId.HeavyDieselEngines));
        Assert.Equal(1.20f, engine.Research.FleetSpeedMultiplier);
        Assert.True(engine.Research.IsTruckUnlocked(heavySpec, engine.Player.Level));
    }

    [Fact]
    public void CityGrowth_TriggersDemandIncreased_AndUnlocksNewResourceDemand()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 45, Height: 45, CityCount: 1, IndustryCount: 1, Seed: 42));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        var city = world.Cities[0];

        bool demandIncreasedFired = false;
        engine.OnDemandIncreased += (c, res, delta, isNew) => demandIncreasedFired = true;

        // Mantém a cidade bem abastecida por 3 ciclos diários (3 * 24 ticks = 72 ticks de 0.25h)
        for (int day = 0; day < 3; day++)
        {
            city.Warehouse.ReceiveCargo(ResourceType.Food, 100);
            city.Warehouse.ReceiveCargo(ResourceType.Lumber, 100);
            for (int t = 0; t < 24; t++)
                engine.Tick(0.25f);
        }

        Assert.True(demandIncreasedFired, "Crescimento urbano deve disparar evento OnDemandIncreased");
        Assert.True(city.ExpansionLevel >= 2);
        Assert.True(city.Warehouse.DailyDemand.ContainsKey(ResourceType.Goods), "Cidade em crescimento deve desbloquear demanda por Goods");
    }

    [Fact]
    public void RegionalExpansion_UnlocksNewConnectedRegion()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 50, Height: 50, CityCount: 2, IndustryCount: 2, Seed: 88));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 50000;

        int initialCities = world.Cities.Count;
        int initialIndustries = world.Industries.Count;
        RegionUnlockInfo? unlockedInfo = null;
        engine.OnRegionUnlocked += info => unlockedInfo = info;

        bool ok = engine.TryFundRegionalExpansion(out var info, out _);

        Assert.True(ok);
        Assert.NotNull(info);
        Assert.NotNull(unlockedInfo);
        Assert.Equal(2, world.UnlockedRegionsCount);
        Assert.Equal(initialCities + 1, world.Cities.Count);
        Assert.True(world.Industries.Count >= initialIndustries);
        Assert.True(world.RoadEdges.Count > 0);
    }

    [Fact]
    public void FinancialLedger_TracksDreAndClosesMonthlyStatementWithInterest()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 1, Seed: 15));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Level = 2;
        engine.Player.Balance = 40000;
        engine.Player.TakeLoan(20000); // Juros esperados na virada de mês: 2% de 20.000 = 400

        // Obra viária registra despesa em RoadConstructionExpense
        int rx = -1, ry = -1;
        for (int x = 0; x < 40 && rx == -1; x++)
        {
            for (int y = 0; y < 40; y++)
            {
                if (world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                {
                    rx = x;
                    ry = y;
                    break;
                }
            }
        }
        Assert.True(engine.UpgradeRoadTile(rx, ry, RoadTier.Asphalt));
        Assert.True(engine.Financials.CurrentMonth.RoadConstructionExpense > 0);

        FinancialStatement? closedStatement = null;
        engine.OnMonthlyFinancialClosed += stmt => closedStatement = stmt;

        // Data inicial é 05/11/2026: avança 26 dias (26 * 24h = 624h -> 2496 ticks de 0.25h, ou diretamente 26 * 24 ticks de 1.0h)
        for (int h = 0; h < 26 * 24; h++)
        {
            engine.Tick(1.0f);
        }

        Assert.NotNull(closedStatement);
        Assert.Equal(11, closedStatement!.Month);
        Assert.Equal(400, closedStatement.LoanInterestExpense);
        Assert.Equal(400, engine.Financials.Lifetime.LoanInterestExpense);
        Assert.Equal(0, engine.Financials.CurrentMonth.RoadConstructionExpense);
        Assert.True(engine.Financials.Lifetime.RoadConstructionExpense > 0);
    }

    [Fact]
    public void WorldGenerator_ShouldGenerateTemperateDesertAndAlpineBiomes()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 50, Height: 50, CityCount: 3, IndustryCount: 3, Seed: 2026));
        var world = generator.Generate();

        int temperate = 0, desert = 0, alpine = 0;
        foreach (var cell in world.Grid)
        {
            switch (cell.Biome)
            {
                case BiomeType.Temperate: temperate++; break;
                case BiomeType.Desert: desert++; break;
                case BiomeType.Alpine: alpine++; break;
            }
        }

        Assert.True(temperate > 0, "Deve haver células de planície verde (Temperate)");
        Assert.True(desert > 0, "Deve haver células de deserto dourado (Desert)");
        Assert.True(alpine > 0, "Deve haver células de picos nevados (Alpine)");
    }

    [Fact]
    public void Player_CanBuildBridgeOverWater_AndTunnelThroughHills()
    {
        var world = new GeneratedWorld(20, 20);
        world.Grid[5, 5].Terrain = TerrainType.Plains;
        world.Grid[5, 5].RoadLevel = RoadTier.Dirt;
        world.RoadNodes.Add(new RoadNode(1, 5, 5));

        world.Grid[6, 5].Terrain = TerrainType.Water;
        world.Grid[7, 5].Terrain = TerrainType.Hills;

        var engine = new SimulationEngine(world);
        engine.Player.Level = 2;
        engine.Player.Balance = 10000;

        bool builtBridge = engine.BuildRoadTile(6, 5, out var bridgeType);
        bool builtTunnel = engine.BuildRoadTile(7, 5, out var tunnelType);

        Assert.True(builtBridge);
        Assert.Equal(RoadStructureType.Bridge, bridgeType);
        Assert.Equal(RoadStructureType.Bridge, world.Grid[6, 5].StructureType);

        Assert.True(builtTunnel);
        Assert.Equal(RoadStructureType.Tunnel, tunnelType);
        Assert.Equal(RoadStructureType.Tunnel, world.Grid[7, 5].StructureType);

        // Pontes e túneis também podem ser asfaltados mantendo seu tipo estrutural
        Assert.True(engine.UpgradeRoadTile(6, 5, RoadTier.Asphalt));
        Assert.Equal(RoadStructureType.Bridge, world.Grid[6, 5].StructureType);
    }

    [Fact]
    public void SaveGameSerializer_RoundTripsFullState_WorldRoadsFleetResearchAndDre()
    {
        var generator = new WorldGenerator(new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 2, Seed: 777));
        var world = generator.Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 90000;
        engine.Player.TakeLoan(15000);

        // Constroi garagem, compra caminhão, ativa agenda manual e pesquisa Asfalto
        var city = world.Cities[0];
        Garage? garage = null;
        for (int dx = -4; dx <= 4 && garage == null; dx++)
            for (int dy = -4; dy <= 4 && garage == null; dy++)
                garage = engine.BuildGarage(city.CenterX + dx, city.CenterY + dy, "Garagem Matriz");

        Assert.NotNull(garage);
        var truck = engine.BuyTruck(garage!, ProgressionManager.AvailableTruckModels[0]);
        Assert.NotNull(truck);
        truck!.HasManualSchedule = true;
        truck.AssignedOriginIndustry = world.Industries[0];
        truck.AssignedTargetCity = city;

        Assert.True(engine.TryStartResearch(ResearchTechId.AsphaltPaving, out _));
        for (int t = 0; t < 80; t++)
            engine.Tick(0.25f);

        Assert.True(engine.Research.IsCompleted(ResearchTechId.AsphaltPaving));

        string json = SaveGameSerializer.SerializeToJson(engine, seed: 777);
        var (loadedEngine, loadedSeed) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Equal(777, loadedSeed);
        Assert.Equal(engine.Player.Balance, loadedEngine.Player.Balance);
        Assert.Equal(15000, loadedEngine.Player.LoanDebt);
        Assert.Equal(engine.CurrentTick, loadedEngine.CurrentTick);
        Assert.True(loadedEngine.Research.IsCompleted(ResearchTechId.AsphaltPaving));
        Assert.Equal(engine.ActiveTrucks.Count, loadedEngine.ActiveTrucks.Count);
        Assert.True(loadedEngine.ActiveTrucks[0].HasManualSchedule);
        Assert.Equal(world.Industries[0].Id, loadedEngine.ActiveTrucks[0].AssignedOriginIndustry?.Id);
        Assert.Equal(engine.Financials.Lifetime.InfrastructureAndFleetExpense, loadedEngine.Financials.Lifetime.InfrastructureAndFleetExpense);
    }

    [Fact]
    public void ConnectedIndustrialModules_DetectLinksAndTransferConveyorCargo()
    {
        var world = new GeneratedWorld(20, 20);
        var farm = new Industry(1, "Fazenda Modelo", 8, 10, ResourceType.Grain, ResourceType.None, productionRate: 10);
        var mill = new Industry(2, "Moinho Anexo", 8, 11, ResourceType.Food, ResourceType.Grain, productionRate: 12);
        world.Industries.Add(farm);
        world.Industries.Add(mill);
        world.Grid[8, 10].OccupantId = farm.Id;
        world.Grid[8, 11].OccupantId = mill.Id;

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 50000;

        // Estrada conectando a indústria ao Norte (8, 9) e a Leste (9, 10)
        Assert.True(engine.BuildRoadTile(8, 9, out _));
        Assert.True(engine.BuildRoadTile(9, 10, out _));

        var farmConn = engine.GetIndustryConnections(farm);
        Assert.True(farmConn.LinkN);
        Assert.True(farmConn.LinkE);
        Assert.True(farmConn.LinkS);
        Assert.True(farmConn.HeavyBridgeS);
        Assert.Equal(2, farmConn.RoadDockCount);
        Assert.Equal(1, farmConn.FacilityBridgeCount);
        Assert.Equal(1, farmConn.DailyProductionBonus);

        // Construir Hub Logístico próximo aumenta conexões e bônus de produção
        var hub = engine.BuildLogisticsHub(7, 10, "Hub Industrial");
        Assert.NotNull(hub);
        var farmConnWithHub = engine.GetIndustryConnections(farm);
        Assert.Equal(1, farmConnWithHub.NearbyHubCount);
        Assert.Equal(4, farmConnWithHub.DailyProductionBonus);

        int millFoodBefore = mill.OutputStock;
        for (int t = 0; t < 24; t++) // 1 dia completo (dispara transferência via esteira + produção)
            engine.Tick(0.25f);

        Assert.True(mill.OutputStock > millFoodBefore, "Moinho adjacente à fazenda deve receber grãos pela esteira conectada e produzir alimentos.");
    }
}
