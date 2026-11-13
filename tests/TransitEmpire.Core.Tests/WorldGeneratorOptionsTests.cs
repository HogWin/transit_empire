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

        Assert.True(CountInlandRiverTiles(withRivers) > 0, "Mundo com rios deve conter leitos fluviais no continente.");
        Assert.Equal(0, CountInlandRiverTiles(withoutRivers));
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
}

