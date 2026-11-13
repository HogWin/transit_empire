using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class Sprint2VisualUxTests
{
    [Fact]
    public void PlanRoadPath_And_BuildPlannedRoad_BuildsConnectedRoadAndDeductsExactCost()
    {
        var world = new GeneratedWorld(20, 20);
        for (int x = 0; x < 20; x++)
            for (int y = 0; y < 20; y++)
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);

        // Coloca um rio no meio (x = 8) para testar contagem de ponte
        world.Grid[8, 5] = new GridCell(8, 5, TerrainType.Water, BiomeType.Temperate);
        world.Grid[8, 4] = new GridCell(8, 4, TerrainType.Water, BiomeType.Temperate);
        world.Grid[8, 6] = new GridCell(8, 6, TerrainType.Water, BiomeType.Temperate);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 50_000;
        long initialBalance = engine.Player.Balance;

        var plan = engine.PlanRoadPath(5, 5, 11, 5);

        Assert.True(plan.IsValid);
        Assert.True(plan.CanAfford);
        Assert.Equal(7, plan.TilesToBuild.Count);
        Assert.True(plan.TotalCost > 0);

        bool built = engine.BuildPlannedRoad(plan, out int builtCount, out int bridges, out int tunnels);
        Assert.True(built);
        Assert.Equal(plan.TilesToBuild.Count, builtCount);
        Assert.Equal(plan.BridgesCount, bridges);
        Assert.Equal(plan.TunnelsCount, tunnels);
        Assert.Equal(initialBalance - plan.TotalCost, engine.Player.Balance);

        foreach (var (tx, ty) in plan.TilesToBuild)
            Assert.True(world.Grid[tx, ty].HasRoad);
    }

    [Fact]
    public void PlanRoadPath_MarksCannotAfford_WhenBalanceInsufficient()
    {
        var world = new GeneratedWorld(15, 15);
        for (int x = 0; x < 15; x++)
            for (int y = 0; y < 15; y++)
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 10; // Saldo insuficiente

        var plan = engine.PlanRoadPath(2, 2, 10, 2);

        Assert.True(plan.IsValid);
        Assert.False(plan.CanAfford);
        Assert.False(engine.BuildPlannedRoad(plan, out _, out _, out _));
    }

    [Fact]
    public void FillCellTrafficMap_CapturesActiveTruckDensityPerRoadCell()
    {
        var config = new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 2, Seed: 808);
        var world = new WorldGenerator(config).Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;

        var edge = world.RoadEdges.First();
        var spec = ProgressionManager.AvailableTruckModels[0];
        var t1 = new Truck(1, "T1", spec);
        var t2 = new Truck(2, "T2", spec);
        t1.AssignPath(new[] { edge });
        t2.AssignPath(new[] { edge });

        var trafficBuf = new int[40, 40];
        engine.FillCellTrafficMap(trafficBuf);

        Assert.Equal(2, trafficBuf[edge.FromNode.GridX, edge.FromNode.GridY]);
        Assert.Equal(2, trafficBuf[edge.ToNode.GridX, edge.ToNode.GridY]);
    }
}

