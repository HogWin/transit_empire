using System.Collections.Generic;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintF5RailTests
{
    private static GeneratedWorld CreateRailTestWorld(int width = 30, int height = 16)
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

    [Fact]
    public void RailNetwork_BuildsTrackViaductAndTunnel_WithBlockSignalHoldingSecondTrain()
    {
        var world = CreateRailTestWorld(24, 12);
        for (int y = 0; y < 12; y++)
        {
            world.Grid[10, y] = new GridCell(10, y, TerrainType.Water, BiomeType.Temperate);
            world.Grid[12, y] = new GridCell(12, y, TerrainType.Hills, BiomeType.Temperate);
        }

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 500_000;

        // Constrói linha férrea contínua x=4..16, y=5 atravessando planície, água (Viaduto) e colina (Túnel)
        var plan = engine.PlanRailPath(4, 5, 16, 5);
        Assert.True(plan.IsValid);
        Assert.True(engine.BuildPlannedRail(plan, out int builtCount, out int viaducts, out int tunnels));
        Assert.True(builtCount >= 13);
        Assert.Equal(1, viaducts);
        Assert.Equal(1, tunnels);
        Assert.Equal(RailStructureType.Viaduct, world.Grid[10, 5].RailStructure);
        Assert.Equal(RailStructureType.Tunnel, world.Grid[12, 5].RailStructure);

        // Sinaleiros de bloco dividem a linha em 3 blocos distintos (entrada | viaduto/túnel | destino)
        Assert.True(engine.PlaceRailSignal(7, 5, out _));
        Assert.True(engine.PlaceRailSignal(14, 5, out _));

        var leftEdge = engine.GetRailNodeAt(5, 5)!.OutgoingEdges[0];
        var midEdge = engine.GetRailNodeAt(10, 5)!.OutgoingEdges[0];
        var rightEdge = engine.GetRailNodeAt(15, 5)!.OutgoingEdges[0];
        Assert.NotEqual(leftEdge.BlockGroupId, midEdge.BlockGroupId);
        Assert.NotEqual(midEdge.BlockGroupId, rightEdge.BlockGroupId);

        // Dois Pátios Intermodais nas pontas da ferrovia
        var yardOrigin = engine.BuildIntermodalTerminal(4, 6, out _, "Pátio Oeste")!;
        var yardDest = engine.BuildIntermodalTerminal(16, 6, out _, "Pátio Leste")!;
        Assert.NotNull(yardOrigin);
        Assert.NotNull(yardDest);
        yardOrigin.StoreCargo(ResourceType.IronOre, 250);

        Assert.True(engine.BuyTrain(0, 3, yardOrigin.Id, out var train1, out _));
        Assert.True(engine.BuyTrain(0, 3, yardOrigin.Id, out var train2, out _));

        var route = new List<IntermodalStop>
        {
            new(yardOrigin.Id, ScheduleAction.Load, ResourceType.IronOre),
            new(yardDest.Id, ScheduleAction.Unload)
        };
        Assert.True(engine.SetTrainRoute(train1!.Id, route, out _));

        // Coloca trem #1 exatamente no bloco central ocupado por 1 tick e despacha trem #2 atrás
        while (train1.CurrentEdge?.BlockGroupId != midEdge.BlockGroupId)
        {
            engine.Tick(0.02f);
        }

        Assert.Equal(RailSignalAspect.Red, engine.GetRailSignalAspectAt(7, 5));
        Assert.True(engine.SetTrainRoute(train2!.Id, route, out _));

        bool secondHeldAtRedSignal = false;
        for (int tick = 0; tick < 80; tick++)
        {
            engine.Tick(0.02f);
            if (train2.Status == TrainStatus.WaitingAtSignal)
            {
                secondHeldAtRedSignal = true;
                break;
            }
        }
        Assert.True(secondHeldAtRedSignal, "Trem #2 deve aguardar no sinaleiro de bloco enquanto o bloco à frente estiver ocupado.");

        // Avança simulação para trem #1 liberar o bloco e trem #2 prosseguir até descarregar
        for (int tick = 0; tick < 250; tick++)
        {
            engine.Tick(0.05f);
        }

        Assert.True(train1.TotalTonnageDelivered > 0);
        Assert.True(train2.TotalTonnageDelivered > 0);
        Assert.Equal(RailWagonCategory.MineralHopper, train1.WagonCategory);
    }

    [Fact]
    public void IntermodalYard_TransshipsTruckToTrainToSeaport_AndSaveLoadRoundtrip()
    {
        var world = CreateRailTestWorld(28, 14);
        var ind = new Industry(101, "Fazenda Rio Grande", 3, 5, ResourceType.Grain, ResourceType.None, productionRate: 60)
        {
            OutputStock = 200
        };
        world.Industries.Add(ind);
        world.Grid[3, 5].OccupantId = ind.Id;

        var seaport = new SeaportTerminal(501, "Porto Atlântico", 23, 5, 24, 5)
        {
            PartnerSeaportId = 502
        };
        world.Seaports.Add(seaport);
        world.Grid[23, 5].OccupantId = seaport.Id;

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 600_000;

        // Rodovia curta entre Fazenda (3,5) -> Pátio Interior (8,5)
        for (int x = 4; x <= 7; x++)
            Assert.True(engine.BuildRoadTile(x, 5, out _));

        // Ferrovia do Pátio Interior (8,6) -> Pátio Portuário (20,6)
        var railPlan = engine.PlanRailPath(8, 6, 20, 6);
        Assert.True(engine.BuildPlannedRail(railPlan, out _, out _, out _));
        Assert.True(engine.PlaceRailSignal(14, 6, out _));

        var yardInland = engine.BuildIntermodalTerminal(8, 5, out _, "Pátio Interior")!;
        var yardPort = engine.BuildIntermodalTerminal(20, 5, out _, "Pátio Porto")!;
        Assert.NotNull(yardInland.RoadAccessNode);
        Assert.Equal(seaport.Id, yardPort.LinkedSeaportId);

        // Caminhão alimentador entrega grãos no Pátio Intermodal Interior
        var garage = engine.BuildGarage(4, 4, "Garagem Oeste")!;
        Assert.NotNull(garage);
        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        Assert.NotNull(truck);
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.Immediate, ResourceType.Grain),
            new ScheduleStop(ScheduleStopKind.IntermodalYard, yardInland.Id, ScheduleAction.Unload)
        });

        // Trem graneleiro leva do Pátio Interior -> Pátio Portuário -> Transbordo automático para o Navio
        Assert.True(engine.BuyTrain(0, 4, yardInland.Id, out var train, out _));
        Assert.True(engine.SetTrainRoute(train!.Id, new[]
        {
            new IntermodalStop(yardInland.Id, ScheduleAction.Load, ResourceType.Grain),
            new IntermodalStop(yardPort.Id, ScheduleAction.Unload)
        }, out _));

        for (int tick = 0; tick < 180; tick++)
        {
            engine.Tick(0.10f);
        }

        Assert.True(yardInland.TotalTransshippedUnits > 0, "Caminhão alimentador deve transbordar grãos no Pátio Interior.");
        Assert.True(train.TotalTonnageDelivered > 0, "Trem deve entregar carga ao Pátio Portuário.");
        Assert.True(seaport.CurrentShipCargo > 0, "Pátio Portuário deve transbordar automaticamente para o navio atracado.");
        Assert.Equal(RailWagonCategory.GrainHopper, train.WagonCategory);

        // Valida Save/Load completo do Sprint F5
        string json = SaveGameSerializer.SerializeToJson(engine, 9090);
        var (loadedEngine, loadedSeed) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Equal(9090, loadedSeed);
        Assert.Equal(engine.RailNodes.Count, loadedEngine.RailNodes.Count);
        Assert.Equal(engine.RailEdges.Count, loadedEngine.RailEdges.Count);
        Assert.Equal(2, loadedEngine.IntermodalTerminals.Count);
        Assert.Single(loadedEngine.ActiveTrains);
        Assert.True(loadedEngine.World.Grid[14, 6].HasRailSignal);
        Assert.Equal(RailWagonCategory.GrainHopper, loadedEngine.ActiveTrains[0].WagonCategory);
        Assert.Equal(train.TotalTonnageDelivered, loadedEngine.ActiveTrains[0].TotalTonnageDelivered);
    }

    [Fact]
    public void CloneTrain_CopiesLocomotiveWagonsAndMultiStopRoute_Safely()
    {
        var world = CreateRailTestWorld(24, 14);
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 1_500_000;
        engine.Player.Level = 6;

        var plan = engine.PlanRailPath(3, 6, 12, 6);
        Assert.True(plan.IsValid);
        Assert.True(engine.BuildPlannedRail(plan, out _, out _, out _));

        var yardA = engine.BuildIntermodalTerminal(3, 7, out _, "Pátio A");
        var yardB = engine.BuildIntermodalTerminal(12, 7, out _, "Pátio B");
        Assert.NotNull(yardA);
        Assert.NotNull(yardB);

        Assert.True(engine.BuyTrain(locomotiveIndex: 1, wagonCount: 5, yardA!.Id, out var sourceTrain, out _));
        Assert.True(engine.SetTrainRoute(sourceTrain!.Id, new List<IntermodalStop>
        {
            new IntermodalStop(yardA.Id, ScheduleAction.Load, ResourceType.Steel),
            new IntermodalStop(yardB!.Id, ScheduleAction.Unload)
        }, out _));

        bool cloned = engine.CloneTrain(sourceTrain.Id, out var clonedTrain, out string msg);

        Assert.True(cloned, msg);
        Assert.NotNull(clonedTrain);
        Assert.NotEqual(sourceTrain.Id, clonedTrain!.Id);
        Assert.Equal(sourceTrain.Locomotive.Name, clonedTrain.Locomotive.Name);
        Assert.Equal(sourceTrain.WagonCount, clonedTrain.WagonCount);
        Assert.Equal(sourceTrain.Stops.Count, clonedTrain.Stops.Count);
        Assert.Equal(2, engine.ActiveTrains.Count);
    }

    [Fact]
    public void OperationalDiagnostics_DetectsRailSignalHold_AndDisconnectedIntermodalYard()
    {
        var world = CreateRailTestWorld(20, 12);
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 1_200_000;
        engine.Player.Level = 5;

        var plan = engine.PlanRailPath(5, 5, 7, 5);
        Assert.True(plan.IsValid);
        Assert.True(engine.BuildPlannedRail(plan, out _, out _, out _));

        var isolatedYard = engine.BuildIntermodalTerminal(6, 6, out _, "Pátio Isolado");
        Assert.NotNull(isolatedYard);

        Assert.True(engine.BuyTrain(locomotiveIndex: 0, wagonCount: 2, isolatedYard!.Id, out var train, out _));
        train!.Status = TrainStatus.WaitingAtSignal;

        var bottlenecks = OperationalDiagnostics.Analyze(engine);

        Assert.Contains(bottlenecks, b => b.Kind == BottleneckKind.RailBottleneck && b.Title.Contains("sinaleiro"));
        Assert.Contains(bottlenecks, b => b.Kind == BottleneckKind.RailBottleneck && b.Title.Contains("sem acesso rodoviário"));
    }
}
