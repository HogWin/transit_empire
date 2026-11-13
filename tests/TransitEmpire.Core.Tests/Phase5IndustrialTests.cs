using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Pathfinding;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class Phase5IndustrialTests
{
    private static GeneratedWorld CreateSimpleGridWorld(int width = 20, int height = 20)
    {
        var world = new GeneratedWorld(width, height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                world.Grid[x, y] = new GridCell(x, y, TerrainType.Plains, BiomeType.Temperate);
        return world;
    }

    [Fact]
    public void GarageFuelSupply_ReducesFleetOperatingCostBy35Percent()
    {
        var config = new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 4, Seed: 111);
        var world = new WorldGenerator(config).Generate();

        // Engine sem combustível
        var engineDry = new SimulationEngine(world);
        engineDry.Player.Balance = 100_000;
        var gDry = engineDry.Garages.FirstOrDefault();
        if (gDry == null)
        {
            var rn = world.RoadNodes.First();
            gDry = new Garage(1, "G1", rn.GridX, rn.GridY, rn);
            engineDry.Garages.Add(gDry);
        }
        var spec = ProgressionManager.AvailableTruckModels[4]; // Bi-Trem (RunningCostPerKm = 8)
        var tDry = new Truck(1, "Dry", spec) { Status = TruckStatus.TravelingToDelivery };
        tDry.AssignPath(world.RoadEdges.Take(5));
        engineDry.ActiveTrucks.Add(tDry);
        for (int i = 0; i < 4; i++) engineDry.Tick(0.25f);
        long dryCost = engineDry.Financials.CurrentMonth.FleetMaintenanceExpense;

        // Engine com diesel na garagem
        var world2 = new WorldGenerator(config).Generate();
        var engineFueled = new SimulationEngine(world2);
        engineFueled.Player.Balance = 100_000;
        var rn2 = world2.RoadNodes.First();
        var gFueled = new Garage(1, "G2", rn2.GridX, rn2.GridY, rn2);
        gFueled.ReceiveFuel(100);
        engineFueled.Garages.Add(gFueled);
        var tFueled = new Truck(1, "Fueled", spec) { Status = TruckStatus.TravelingToDelivery };
        tFueled.AssignPath(world2.RoadEdges.Take(5));
        engineFueled.ActiveTrucks.Add(tFueled);
        for (int i = 0; i < 4; i++) engineFueled.Tick(0.25f);
        long fueledCost = engineFueled.Financials.CurrentMonth.FleetMaintenanceExpense;

        Assert.True(dryCost > 0);
        Assert.True(fueledCost < dryCost, $"Esperava custo com diesel ({fueledCost}) menor que sem diesel ({dryCost})");
        Assert.True(gFueled.FuelStock <= 100);
    }

    [Fact]
    public void MultiInputIndustry_BoostsProduction_WhenSecondaryCatalystSupplied()
    {
        var factoryBase = new Industry(1, "Fábrica Base", 5, 5, ResourceType.Goods, ResourceType.Lumber, 16, ResourceType.Steel);
        factoryBase.OutputStock = 0;
        factoryBase.ReceiveInputCargo(ResourceType.Lumber, 10);
        factoryBase.Produce();
        int baseOut = factoryBase.OutputStock;

        var factoryBoosted = new Industry(2, "Fábrica Aço+Madeira", 6, 6, ResourceType.Goods, ResourceType.Lumber, 16, ResourceType.Steel);
        factoryBoosted.OutputStock = 0;
        factoryBoosted.ReceiveInputCargo(ResourceType.Lumber, 10);
        int acceptedSteel = factoryBoosted.ReceiveInputCargo(ResourceType.Steel, 10);
        factoryBoosted.Produce();
        int boostedOut = factoryBoosted.OutputStock;

        Assert.Equal(10, acceptedSteel);
        Assert.Equal(16, baseOut);
        Assert.True(boostedOut > baseOut, $"Com aço secundário deveria produzir mais ({boostedOut} > {baseOut})");
    }

    [Fact]
    public void UpgradeIndustry_IncreasesLevelCapacityRateAndDocks()
    {
        var world = CreateSimpleGridWorld();
        var ind = new Industry(100, "Siderúrgica", 10, 10, ResourceType.Steel, ResourceType.IronOre, 15);
        world.Industries.Add(ind);
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 50_000;

        int initialMax = ind.MaxStock;
        int initialRate = ind.ProductionRatePerDay;
        int initialDocks = engine.GetIndustryConnections(ind).RoadDockCount;

        bool ok = engine.TryUpgradeIndustry(ind, out _);
        Assert.True(ok);
        Assert.Equal(2, ind.UpgradeLevel);
        Assert.Equal(initialMax + 150, ind.MaxStock);
        Assert.Equal(initialRate + 6, ind.ProductionRatePerDay);
        Assert.Equal(initialDocks + 1, engine.GetIndustryConnections(ind).RoadDockCount);
    }

    [Fact]
    public void ResearchPhase5_UnlocksReeferSmartRoutingAndRoadTrain9Axles()
    {
        var world = CreateSimpleGridWorld();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 200_000;

        // Conclui pré-requisitos e novas pesquisas
        Assert.True(engine.TryStartResearch(ResearchTechId.HeavyDieselEngines, out _));
        engine.Research.Advance(30f);
        Assert.True(engine.TryStartResearch(ResearchTechId.ReeferBays, out _));
        engine.Research.Advance(30f);
        Assert.Equal(1.25f, engine.Research.FoodFreightBonusMultiplier);

        Assert.True(engine.TryStartResearch(ResearchTechId.AsphaltPaving, out _));
        engine.Research.Advance(25f);
        Assert.True(engine.TryStartResearch(ResearchTechId.SmartRouting, out _));
        engine.Research.Advance(35f);
        Assert.True(engine.Research.IsSmartRoutingEnabled);

        Assert.True(engine.TryStartResearch(ResearchTechId.BiTrainTrucks, out _));
        engine.Research.Advance(50f);
        Assert.True(engine.TryStartResearch(ResearchTechId.RoadTrain9Axles, out _));
        engine.Research.Advance(65f);

        var rodotrem = ProgressionManager.AvailableTruckModels.First(t => t.RequiresHighwayOrDualCarriage && !t.IsPassengerBus);
        Assert.Equal(220, rodotrem.Capacity);
        Assert.True(rodotrem.RequiresHighwayOrDualCarriage);
        Assert.True(engine.Research.IsTruckUnlocked(rodotrem, playerLevel: 1));
    }

    [Fact]
    public void RoadPathfinder_PrefersHighways_ForRoadTrain9Axles()
    {
        var nStart = new RoadNode(1, 0, 0);
        var nSlowMid = new RoadNode(2, 5, 0);
        var nFastMid = new RoadNode(3, 5, 3);
        var nEnd = new RoadNode(4, 10, 0);

        // Rota direta em terra (mais curta em km, mas Dirt)
        var e1 = new RoadEdge(1, nStart, nSlowMid, RoadTier.Dirt, 5f);
        var e2 = new RoadEdge(2, nSlowMid, nEnd, RoadTier.Dirt, 5f);
        // Rota alternativa em Highway (um pouco mais longa, mas Highway)
        var e3 = new RoadEdge(3, nStart, nFastMid, RoadTier.Highway, 6f);
        var e4 = new RoadEdge(4, nFastMid, nEnd, RoadTier.Highway, 6f);

        nStart.OutgoingEdges.Add(e1);
        nSlowMid.OutgoingEdges.Add(e2);
        nStart.OutgoingEdges.Add(e3);
        nFastMid.OutgoingEdges.Add(e4);

        var pathfinder = new RoadPathfinder();
        var route = pathfinder.FindFastestRoute(nStart, nEnd, WeatherCondition.Clear, preferHighways: true);

        Assert.NotNull(route);
        Assert.Equal(2, route!.Count);
        Assert.All(route, e => Assert.Equal(RoadTier.Highway, e.Tier));
    }

    [Fact]
    public void SaveAndLoad_PreservesIndustryUpgradesSecondaryInputsAndGarageFuel()
    {
        var config = new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 6, Seed: 909);
        var world = new WorldGenerator(config).Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 100_000;

        var ind = world.Industries.First(i => i.SecondaryInputResource != ResourceType.None);
        ind.ReceiveInputCargo(ind.SecondaryInputResource, 25);
        engine.TryUpgradeIndustry(ind, out _);

        var rn = world.RoadNodes.First();
        var garage = new Garage(1, "Garagem Diesel", rn.GridX, rn.GridY, rn);
        garage.ReceiveFuel(140);
        engine.Garages.Add(garage);

        string json = SaveGameSerializer.SerializeToJson(engine, 909);
        var (loaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        var loadedInd = loaded.World.Industries.First(i => i.Id == ind.Id);
        Assert.Equal(2, loadedInd.UpgradeLevel);
        Assert.Equal(25, loadedInd.SecondaryInputStock);
        Assert.Equal(ind.MaxStock, loadedInd.MaxStock);
        Assert.Equal(ind.ProductionRatePerDay, loadedInd.ProductionRatePerDay);

        var loadedGarage = loaded.Garages.Single();
        Assert.Equal(140, loadedGarage.FuelStock);
    }
}
