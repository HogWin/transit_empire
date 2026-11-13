using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class WorldGeneratorOptionsTests
{
    [Fact]
    public void WorldGenerator_RespectsHasRiversFlag()
    {
        var withRivers = new WorldGenerator(new WorldGenerationConfig(
            Width: 80,
            Height: 80,
            CityCount: 5,
            IndustryCount: 9,
            Seed: 4242,
            Scenario: WorldScenario.Balanced,
            HasRivers: true)).Generate();

        var withoutRivers = new WorldGenerator(new WorldGenerationConfig(
            Width: 80,
            Height: 80,
            CityCount: 5,
            IndustryCount: 9,
            Seed: 4242,
            Scenario: WorldScenario.Balanced,
            HasRivers: false)).Generate();

        static int CountInlandRiverTiles(GeneratedWorld world)
        {
            int count = 0;
            int w = world.Grid.GetLength(0);
            int h = world.Grid.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (world.Grid[x, y].Terrain == TerrainType.Water && !world.OceanMask[x, y])
                        count++;
                }
            }
            return count;
        }

        static int CountWaterTiles(GeneratedWorld world)
        {
            int count = 0;
            int w = world.Grid.GetLength(0);
            int h = world.Grid.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (world.Grid[x, y].Terrain == TerrainType.Water || world.OceanMask[x, y])
                        count++;
                }
            }
            return count;
        }

        Assert.True(CountInlandRiverTiles(withRivers) > 0, "Mundo com rios deve conter leitos fluviais no continente.");
        Assert.True(withRivers.Seaports.Count > 0, "Mundo com rios e canal oceânico deve possuir portos.");
        Assert.Equal(0, CountWaterTiles(withoutRivers));
        Assert.Equal(0, withoutRivers.OverseasContinentId);
        Assert.Empty(withoutRivers.Seaports);
    }

    [Fact]
    public void WorldGenerator_MountainDensityManyProducesMoreReliefThanFew()
    {
        var fewMountains = new WorldGenerator(new WorldGenerationConfig(
            Width: 80,
            Height: 80,
            CityCount: 5,
            IndustryCount: 9,
            Seed: 7777,
            Scenario: WorldScenario.Balanced,
            HasRivers: true,
            Mountains: MountainDensity.Few)).Generate();

        var manyMountains = new WorldGenerator(new WorldGenerationConfig(
            Width: 80,
            Height: 80,
            CityCount: 5,
            IndustryCount: 9,
            Seed: 7777,
            Scenario: WorldScenario.Balanced,
            HasRivers: true,
            Mountains: MountainDensity.Many)).Generate();

        static int CountElevatedTiles(GeneratedWorld world)
        {
            int count = 0;
            int w = world.Grid.GetLength(0);
            int h = world.Grid.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    var t = world.Grid[x, y].Terrain;
                    if (t == TerrainType.Mountain || t == TerrainType.Hills)
                        count++;
                }
            }
            return count;
        }

        Assert.True(CountElevatedTiles(manyMountains) > CountElevatedTiles(fewMountains));
    }

    [Fact]
    public void WorldGenerator_BiomesAreTrulyRandomizedWithoutFixedForcedCornerShapes()
    {
        var desertCornerSeeds = 0;
        var alpineBottomLeftSeeds = 0;
        var seeds = new[] { 101, 202, 303, 404, 505, 606 };

        foreach (int seed in seeds)
        {
            var world = new WorldGenerator(new WorldGenerationConfig(
                Width: 80,
                Height: 80,
                CityCount: 5,
                IndustryCount: 9,
                Seed: seed,
                Scenario: WorldScenario.Balanced,
                HasRivers: false)).Generate();

            // Antigo ponto fixo do círculo obrigatório de deserto: (0.16*w, 0.78*h)
            if (world.Grid[13, 62].Biome == BiomeType.Desert)
                desertCornerSeeds++;
            if (world.Grid[13, 62].Biome == BiomeType.Alpine)
                alpineBottomLeftSeeds++;
        }

        Assert.True(desertCornerSeeds < seeds.Length, "Deserto não pode ocupar sempre o mesmo canto fixo em todas as seeds.");
        Assert.True(desertCornerSeeds + alpineBottomLeftSeeds <= seeds.Length);
    }

    [Theory]
    [InlineData(true, 17)]
    [InlineData(false, 42)]
    [InlineData(false, 514859)]
    public void WorldGenerator_EveryIndustryHasOrthogonalRoadIntegration(bool hasRivers, int seed)
    {
        var world = new WorldGenerator(new WorldGenerationConfig(
            Width: 80,
            Height: 80,
            CityCount: 5,
            IndustryCount: 9,
            Seed: seed,
            Scenario: WorldScenario.Balanced,
            HasRivers: hasRivers)).Generate();

        var engine = new TransitEmpire.Core.Simulation.SimulationEngine(world);
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var offsets = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        Assert.NotEmpty(world.Industries);
        foreach (var ind in world.Industries)
        {
            Assert.Equal(RoadTier.None, world.Grid[ind.GridX, ind.GridY].RoadLevel);

            bool hasOrthogonalRoad = offsets.Any(o =>
            {
                int nx = ind.GridX + o.Item1;
                int ny = ind.GridY + o.Item2;
                return nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].HasRoad;
            });
            Assert.True(hasOrthogonalRoad, $"Indústria {ind.Name} em ({ind.GridX},{ind.GridY}) deve ter estrada ortogonalmente adjacente.");

            var conn = engine.GetIndustryConnections(ind);
            Assert.True(conn.LinkN || conn.LinkS || conn.LinkE || conn.LinkW);
            Assert.True(conn.RoadDockCount >= 1);
        }
    }

    [Theory]
    [InlineData(true, 722896)]
    [InlineData(false, 42)]
    [InlineData(true, 999)]
    public void WorldGenerator_InitialRoadsStayGuidedNearOptimalPath(bool hasRivers, int seed)
    {
        var world = new WorldGenerator(new WorldGenerationConfig(
            Width: 100,
            Height: 100,
            CityCount: 6,
            IndustryCount: 10,
            Seed: seed,
            Scenario: WorldScenario.Balanced,
            HasRivers: hasRivers,
            Mountains: MountainDensity.Many)).Generate();

        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int totalRoadTiles = 0;
        int borderRoadTiles = 0;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (!world.Grid[x, y].HasRoad) continue;
                totalRoadTiles++;
                if (x == 0 || x == w - 1 || y == 0 || y == h - 1)
                    borderRoadTiles++;
            }
        }

        Assert.True(totalRoadTiles > 0);
        Assert.True(borderRoadTiles <= Math.Max(4, totalRoadTiles / 10),
            $"Muitos tiles de estrada colados na borda externa do mapa ({borderRoadTiles}/{totalRoadTiles}).");

        foreach (var edge in world.RoadEdges)
        {
            int manhattan = Math.Abs(edge.ToNode.GridX - edge.FromNode.GridX) + Math.Abs(edge.ToNode.GridY - edge.FromNode.GridY);
            if (manhattan < 8) continue;

            float maxAllowed = manhattan * 1.75f + 16f;
            Assert.True(edge.DistanceKm <= maxAllowed,
                $"Estrada excessivamente longa entre ({edge.FromNode.GridX},{edge.FromNode.GridY}) e ({edge.ToNode.GridX},{edge.ToNode.GridY}): dist={edge.DistanceKm}, manhattan={manhattan}, maxAllowed={maxAllowed}");
        }
    }
}

