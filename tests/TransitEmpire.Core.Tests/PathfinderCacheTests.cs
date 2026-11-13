using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Pathfinding;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class PathfinderCacheTests
{
    [Fact]
    public void RoadPathfinder_CachesRoutesPerConstraintKey_AndReturnsDefensiveCopy()
    {
        var start = new RoadNode(1, 0, 0);
        var mid = new RoadNode(2, 5, 0);
        var end = new RoadNode(3, 10, 0);

        var e1 = new RoadEdge(1, start, mid, RoadTier.Asphalt, 5f);
        var e2 = new RoadEdge(2, mid, end, RoadTier.Asphalt, 5f);
        start.OutgoingEdges.Add(e1);
        mid.OutgoingEdges.Add(e2);

        var pathfinder = new RoadPathfinder();

        var first = pathfinder.FindFastestRoute(start, end, WeatherCondition.Clear);
        Assert.NotNull(first);
        Assert.Equal(2, first!.Count);
        Assert.Equal(0, pathfinder.CacheHits);
        Assert.Equal(1, pathfinder.CacheMisses);

        // Mutar a lista retornada não pode corromper o cache interno
        first.Clear();

        var second = pathfinder.FindFastestRoute(start, end, WeatherCondition.Clear);
        Assert.NotNull(second);
        Assert.Equal(2, second!.Count);
        Assert.Equal(1, pathfinder.CacheHits);
        Assert.Equal(1, pathfinder.CacheMisses);

        // Outro clima / restrição deve usar chave distinta sem colidir
        var rainy = pathfinder.FindFastestRoute(start, end, WeatherCondition.Rain, preferHighways: true);
        Assert.NotNull(rainy);
        Assert.Equal(2, rainy!.Count);
        Assert.Equal(1, pathfinder.CacheHits);
        Assert.Equal(2, pathfinder.CacheMisses);
    }

    [Fact]
    public void SimulationEngine_InvalidatesPathfinderCache_OnRoadBuildUpgradeAndRepave()
    {
        var world = new GeneratedWorld(12, 12);
        for (int x = 0; x < 12; x++)
        {
            for (int y = 0; y < 12; y++)
            {
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);
            }
        }

        // Rota inicial A(1,1) -> B(2,1) -> C(3,1) -> D(4,1) em terra batida
        var n1 = new RoadNode(1, 1, 1);
        var n2 = new RoadNode(2, 2, 1);
        var n3 = new RoadNode(3, 3, 1);
        var n4 = new RoadNode(4, 4, 1);
        world.RoadNodes.AddRange(new[] { n1, n2, n3, n4 });
        world.Grid[1, 1].RoadLevel = RoadTier.Dirt;
        world.Grid[2, 1].RoadLevel = RoadTier.Dirt;
        world.Grid[3, 1].RoadLevel = RoadTier.Dirt;
        world.Grid[4, 1].RoadLevel = RoadTier.Dirt;

        LinkBidirectional(world, 1, n1, n2, RoadTier.Dirt);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Dirt);
        LinkBidirectional(world, 5, n3, n4, RoadTier.Dirt);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 1_000_000;
        engine.Player.Level = 10;

        var route1 = engine.Pathfinder.FindFastestRoute(n1, n4);
        Assert.NotNull(route1);
        Assert.Equal(1, engine.Pathfinder.CacheMisses);

        _ = engine.Pathfinder.FindFastestRoute(n1, n4);
        Assert.Equal(1, engine.Pathfinder.CacheHits);

        // Upgrade de trecho invalida o cache
        int prevGen = engine.Pathfinder.CacheGeneration;
        Assert.True(engine.UpgradeRoadTile(2, 1, RoadTier.Asphalt));
        Assert.True(engine.Pathfinder.CacheGeneration > prevGen);
        Assert.Equal(0, engine.Pathfinder.CachedRouteCount);

        _ = engine.Pathfinder.FindFastestRoute(n1, n4);
        Assert.Equal(2, engine.Pathfinder.CacheMisses);

        // Construção de novo tile viário conectado invalida o cache
        prevGen = engine.Pathfinder.CacheGeneration;
        Assert.True(engine.BuildRoadTile(4, 2, out _));
        Assert.True(engine.Pathfinder.CacheGeneration > prevGen);
        Assert.Equal(0, engine.Pathfinder.CachedRouteCount);

        // Recapear estrada desgastada também invalida o cache
        _ = engine.Pathfinder.FindFastestRoute(n1, n4);
        world.RoadEdges[0].PavementCondition = 40f;
        prevGen = engine.Pathfinder.CacheGeneration;
        Assert.True(engine.TryRepaveRoadTile(1, 1, out _));
        Assert.True(engine.Pathfinder.CacheGeneration > prevGen);
        Assert.Equal(0, engine.Pathfinder.CachedRouteCount);
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
