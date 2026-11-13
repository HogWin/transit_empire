using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintE5CoreTests
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
    public void SeaportTerminal_DockedBonus_Applies45PercentAndRotatesVoyageCycle()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 6, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[6, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 10f);

        var ind = new Industry(100, "Serraria Export", 2, 1, ResourceType.Lumber, ResourceType.None, 40);
        ind.OutputStock = 200;
        world.Industries.Add(ind);

        world.Grid[7, 2].Terrain = TerrainType.Water;
        var seaport = new SeaportTerminal(1, "Porto Atlântico", 6, 1, 7, 2);
        world.Seaports.Add(seaport);

        var engine = new SimulationEngine(world) { CurrentWeather = WeatherCondition.Clear };
        engine.Player.Balance = 100_000;
        var garage = engine.BuildGarage(2, 3, "Garagem Base");
        Assert.NotNull(garage);

        var spec = ProgressionManager.AvailableTruckModels[0];
        var truck = engine.BuyTruck(garage!, spec)!;
        truck.AutoPitStopEnabled = false;
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.Seaport, seaport.Id, ScheduleAction.Unload)
        });

        int shipTransitions = 0;
        engine.OnSeaportShipStatusChanged += (_, _, _) => shipTransitions++;

        long lastPayment = 0;
        engine.OnDeliveryCompleted += (_, _, _, paid) => lastPayment = paid;

        // Entrega 1 com navio atracado (+45% bônus de exportação)
        for (int i = 0; i < 15 && lastPayment == 0; i++)
            engine.Tick(0.25f);

        long dockedPayment = lastPayment;
        Assert.True(dockedPayment > 0);
        Assert.Equal(spec.Capacity, seaport.CurrentShipCargo);
        Assert.Equal(dockedPayment, seaport.TotalExportRevenue);

        // Avança até o navio zarpar (120h) enquanto mantém caminhão parado para não interferir na contagem
        truck.HasManualSchedule = false;
        for (int i = 0; i < 130 && seaport.IsShipDocked; i++)
            engine.Tick(1f);

        Assert.False(seaport.IsShipDocked);
        Assert.Equal(1, shipTransitions);
        Assert.Equal(0, seaport.CurrentShipCargo);

        // Realiza nova entrega completa via agenda (n2 -> n1 coleta -> n2 porto) com navio em alto-mar
        lastPayment = 0;
        ind.OutputStock = 200;
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load),
            new ScheduleStop(ScheduleStopKind.Seaport, seaport.Id, ScheduleAction.Unload)
        });
        truck.Status = TruckStatus.Idle;

        for (int i = 0; i < 30 && lastPayment == 0; i++)
            engine.Tick(0.25f);

        long undockedPayment = lastPayment;
        Assert.True(undockedPayment > 0);
        Assert.True(dockedPayment > undockedPayment);
        float ratio = (float)dockedPayment / undockedPayment;
        Assert.InRange(ratio, 1.40f, 1.50f);

        // Avança mais 60h para novo navio atracar
        truck.HasManualSchedule = false;
        for (int i = 0; i < 65 && !seaport.IsShipDocked; i++)
            engine.Tick(1f);

        Assert.True(seaport.IsShipDocked);
        Assert.Equal(2, shipTransitions);
    }

    [Fact]
    public void RegionalLandmark_MultiStageConstruction_UnlocksPermanentBonuses()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 5f);

        var dam = new RegionalLandmark(1, "Usina Rio Claro", LandmarkKind.HydroelectricDam, 4, 1);
        var stadium = new RegionalLandmark(2, "Estádio Horizonte", LandmarkKind.MunicipalStadium, 4, 3);
        var wind = new RegionalLandmark(3, "Parque Eólico", LandmarkKind.WindFarmPark, 5, 2);
        world.Landmarks.AddRange(new[] { dam, stadium, wind });

        var engine = new SimulationEngine(world) { CurrentWeather = WeatherCondition.Clear };
        engine.Player.Balance = 100_000;
        Assert.Equal(1.0f, engine.LandmarkFreightBonusMultiplier);
        Assert.Equal(1.0f, engine.LandmarkMaintenanceMultiplier);
        Assert.Equal(1.0f, engine.LandmarkXpMultiplier);
        Assert.Equal(1.0f, engine.LandmarkFuelMultiplier);
        Assert.Equal(1.0f, engine.LandmarkProductionMultiplier);

        var garage = engine.BuildGarage(2, 3, "Garagem")!;
        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.AutoPitStopEnabled = false;

        int completedStages = 0;
        bool projectDone = false;
        engine.OnLandmarkProgress += (_, stageDone, allDone) =>
        {
            if (stageDone) completedStages++;
            if (allDone) projectDone = true;
        };

        void DeliverDirectViaSchedule(RegionalLandmark target, ResourceType res, int amt)
        {
            truck.SetSchedule(new[]
            {
                new ScheduleStop(ScheduleStopKind.RegionalLandmark, target.Id, ScheduleAction.Unload)
            });
            truck.LoadedCargoType = res;
            truck.LoadedCargoAmount = amt;
            truck.Status = TruckStatus.Idle;
            for (int i = 0; i < 15 && truck.LoadedCargoAmount > 0; i++)
                engine.Tick(0.25f);
            truck.HasManualSchedule = false;
        }

        // Etapa 1: 30 Steel + 30 Lumber
        DeliverDirectViaSchedule(dam, ResourceType.Steel, 30);
        DeliverDirectViaSchedule(dam, ResourceType.Lumber, 30);
        Assert.Equal(2, dam.CurrentStage);

        // Etapa 2: 45 Steel + 35 Goods
        DeliverDirectViaSchedule(dam, ResourceType.Steel, 45);
        DeliverDirectViaSchedule(dam, ResourceType.Goods, 35);
        Assert.Equal(3, dam.CurrentStage);

        // Etapa 3: 45 Steel + 40 Lumber + 45 Goods
        DeliverDirectViaSchedule(dam, ResourceType.Steel, 45);
        DeliverDirectViaSchedule(dam, ResourceType.Lumber, 40);
        DeliverDirectViaSchedule(dam, ResourceType.Goods, 45);

        Assert.True(dam.IsCompleted);
        Assert.True(projectDone);
        Assert.Equal(3, completedStages);
        Assert.True(dam.TotalInvestedBonusPaid >= 25000L);
        Assert.Equal(1.15f, engine.LandmarkFreightBonusMultiplier);
        Assert.Equal(0.90f, engine.LandmarkMaintenanceMultiplier);

        // Completa Estádio e Parque Eólico para validar bônus globais de XP, Diesel e Produção
        static void CompleteAllStages(RegionalLandmark lm)
        {
            lm.ContributeCargo(ResourceType.Steel, 30, out _, out _);
            lm.ContributeCargo(ResourceType.Lumber, 30, out _, out _);
            lm.ContributeCargo(ResourceType.Steel, 45, out _, out _);
            lm.ContributeCargo(ResourceType.Goods, 35, out _, out _);
            lm.ContributeCargo(ResourceType.Steel, 45, out _, out _);
            lm.ContributeCargo(ResourceType.Lumber, 40, out _, out _);
            lm.ContributeCargo(ResourceType.Goods, 45, out _, out _);
        }

        CompleteAllStages(stadium);
        CompleteAllStages(wind);
        Assert.Equal(1.25f, engine.LandmarkXpMultiplier);
        Assert.Equal(0.85f, engine.LandmarkFuelMultiplier);
        Assert.Equal(1.10f, engine.LandmarkProductionMultiplier);
    }

    [Fact]
    public void WorldGenerator_GeneratesSeaportAndRegionalLandmarksConnectedToRoads()
    {
        var world = new WorldGenerator(new WorldGenerationConfig
        {
            Width = 35,
            Height = 35,
            Seed = 42,
            CityCount = 3,
            IndustryCount = 4
        }).Generate();

        Assert.Single(world.Seaports);
        Assert.Equal(2, world.Landmarks.Count);

        var seaport = world.Seaports[0];
        Assert.Equal(TerrainType.Water, world.Grid[seaport.WaterDockX, seaport.WaterDockY].Terrain);
        Assert.Equal(RoadTier.None, world.Grid[seaport.GridX, seaport.GridY].RoadLevel);

        var engine = new SimulationEngine(world);
        Assert.NotNull(engine.GetStopAccessNode(new ScheduleStop(ScheduleStopKind.Seaport, seaport.Id, ScheduleAction.Unload)));
        foreach (var lm in world.Landmarks)
        {
            Assert.NotNull(engine.GetStopAccessNode(new ScheduleStop(ScheduleStopKind.RegionalLandmark, lm.Id, ScheduleAction.Unload)));
        }
    }

    [Fact]
    public void SaveGameSerializer_RoundTripsSeaportsAndLandmarksState()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 3, 3);
        var n2 = new RoadNode(2, 4, 3);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[3, 3].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 3].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 5f);

        var seaport = new SeaportTerminal(1, "Porto Save", 4, 3, 5, 3);
        seaport.LoadExportCargo(ResourceType.Steel, 80, out _);
        seaport.TotalExportRevenue = 4200;
        seaport.AdvanceTime(125f, out _); // Coloca navio em alto-mar
        world.Seaports.Add(seaport);

        var landmark = new RegionalLandmark(1, "Usina Save", LandmarkKind.HydroelectricDam, 8, 8);
        landmark.ContributeCargo(ResourceType.Steel, 30, out _, out _);
        landmark.ContributeCargo(ResourceType.Lumber, 30, out _, out _); // Avança para Etapa 2
        landmark.ContributeCargo(ResourceType.Goods, 12, out _, out _);
        landmark.TotalInvestedBonusPaid = 5000;
        world.Landmarks.Add(landmark);

        var engine = new SimulationEngine(world);
        string json = SaveGameSerializer.SerializeToJson(engine, 123);
        var (restored, seed) = SaveGameSerializer.DeserializeFromJson(json);
        Assert.Equal(123, seed);

        Assert.Single(restored.World.Seaports);
        var rs = restored.World.Seaports[0];
        Assert.Equal("Porto Save", rs.Name);
        Assert.False(rs.IsShipDocked);
        Assert.Equal(80, rs.TotalExportedUnits);
        Assert.Equal(4200, rs.TotalExportRevenue);

        Assert.Single(restored.World.Landmarks);
        var rl = restored.World.Landmarks[0];
        Assert.Equal("Usina Save", rl.Name);
        Assert.Equal(LandmarkKind.HydroelectricDam, rl.Kind);
        Assert.Equal(2, rl.CurrentStage);
        Assert.Equal(12, rl.StageDelivered[ResourceType.Goods]);
        Assert.Equal(5000, rl.TotalInvestedBonusPaid);
    }

    [Fact]
    public void WorldGenerator_LargeMap_SeparatesContinentsWithOcean_LinksSeaportsByWater_AndAvoidsBuildingsOnRoads()
    {
        var world = new WorldGenerator(new WorldGenerationConfig(
            Width: 100,
            Height: 100,
            CityCount: 6,
            IndustryCount: 10,
            Seed: 2026)).Generate();

        Assert.Equal(100, world.Grid.GetLength(0));
        Assert.Equal(100, world.Grid.GetLength(1));
        Assert.True(world.OverseasContinentId > 0, "Mapa 100x100 deve gerar segundo continente separado pelo oceano");
        Assert.Equal(2, world.Seaports.Count);

        // Nenhuma estrutura central (prédio sede de cidade, indústria, porto ou obra) pode ter estrada por baixo
        foreach (var city in world.Cities)
            Assert.False(world.Grid[city.CenterX, city.CenterY].HasRoad, $"Centro de {city.Name} não pode ter rodovia embaixo");
        foreach (var ind in world.Industries)
            Assert.False(world.Grid[ind.GridX, ind.GridY].HasRoad);
        foreach (var sp in world.Seaports)
            Assert.False(world.Grid[sp.GridX, sp.GridY].HasRoad);
        foreach (var lm in world.Landmarks)
            Assert.False(world.Grid[lm.GridX, lm.GridY].HasRoad);

        // Portos em continentes diferentes, pareados entre si e ligados pelo oceano aberto
        var p1 = world.Seaports[0];
        var p2 = world.Seaports[1];
        Assert.Equal(p2.Id, p1.PartnerSeaportId);
        Assert.Equal(p1.Id, p2.PartnerSeaportId);
        Assert.NotEqual(world.ContinentMap[p1.GridX, p1.GridY], world.ContinentMap[p2.GridX, p2.GridY]);
        Assert.True(world.OceanMask[p1.WaterDockX, p1.WaterDockY]);
        Assert.True(world.OceanMask[p2.WaterDockX, p2.WaterDockY]);

        // Verifica por BFS na água que os dois portos estão conectados pelo oceano
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var q = new Queue<(int X, int Y)>();
        var vis = new HashSet<(int X, int Y)>();
        q.Enqueue((p1.WaterDockX, p1.WaterDockY));
        vis.Add((p1.WaterDockX, p1.WaterDockY));
        bool waterConnected = false;
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == (p2.WaterDockX, p2.WaterDockY)) { waterConnected = true; break; }
            for (int d = 0; d < 4; d++)
            {
                int nx = cur.X + dx[d];
                int ny = cur.Y + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].Terrain == TerrainType.Water && vis.Add((nx, ny)))
                    q.Enqueue((nx, ny));
            }
        }
        Assert.True(waterConnected, "Os dois portos precisam estar ligados por oceano navegável");

        // Verifica que NÃO existe rota rodoviária entre os dois portos/continentes (acesso apenas por barco)
        var engine = new SimulationEngine(world);
        var n1 = engine.GetStopAccessNode(new ScheduleStop(ScheduleStopKind.Seaport, p1.Id, ScheduleAction.Unload));
        var n2 = engine.GetStopAccessNode(new ScheduleStop(ScheduleStopKind.Seaport, p2.Id, ScheduleAction.Load));
        Assert.NotNull(n1);
        Assert.NotNull(n2);
        Assert.Null(engine.Pathfinder.FindFastestRoute(n1!, n2!));

        // Ponte em cima do oceano aberto é bloqueada
        Assert.False(engine.BuildRoadTile(p1.WaterDockX, p1.WaterDockY, out _));
    }

    [Fact]
    public void SeaportTerminal_MaritimeTransit_DeliversCargoToPartnerContinent()
    {
        var world = new WorldGenerator(new WorldGenerationConfig(
            Width: 100,
            Height: 100,
            CityCount: 6,
            IndustryCount: 10,
            Seed: 4242)).Generate();
        var engine = new SimulationEngine(world);
        var p1 = world.Seaports[0];
        var p2 = world.Seaports[1];

        p1.LoadExportCargo(ResourceType.Steel, 40, out bool docked);
        Assert.True(docked);
        Assert.Equal(40, p1.OutboundCargo[ResourceType.Steel]);

        // Avança até o navio zarpar de p1 (envia manifesto ao porto parceiro p2)
        for (int i = 0; i < 130 && p1.IsShipDocked; i++)
            engine.Tick(1f);
        Assert.False(p1.IsShipDocked);

        // Avança até p2 atracar e descarregar o manifesto marítimo no pátio de trânsito
        for (int i = 0; i < 180 && !p2.TransitCargo.ContainsKey(ResourceType.Steel); i++)
            engine.Tick(1f);

        Assert.True(p2.TransitCargo.TryGetValue(ResourceType.Steel, out int arrived) && arrived == 40);
    }
}
