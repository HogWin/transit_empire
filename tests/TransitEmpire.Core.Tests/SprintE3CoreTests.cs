using System;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintE3CoreTests
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

    private static RoadEdge LinkBidirectional(GeneratedWorld world, int startEdgeId, RoadNode a, RoadNode b, RoadTier tier, float distanceKm = 100f, bool isHill = false)
    {
        var f = new RoadEdge(startEdgeId, a, b, tier, distanceKm) { IsHillOrMountain = isHill };
        var r = new RoadEdge(startEdgeId + 1, b, a, tier, distanceKm) { IsHillOrMountain = isHill };
        a.OutgoingEdges.Add(f);
        b.OutgoingEdges.Add(r);
        world.RoadEdges.Add(f);
        world.RoadEdges.Add(r);
        return f;
    }

    [Fact]
    public void TruckRank_Transitions_And_GrantsHillClimbAndFuelBonuses()
    {
        var spec = new TruckSpecification("F-4000", 12, 60f, 5000, 10, 1);

        var rookie = new Truck(1, "Caminhão Novato", spec);
        Assert.Equal(TruckRank.Rookie, rookie.Rank);
        Assert.Equal(1.00f, rookie.FuelCostMultiplier);
        Assert.Equal(1.00f, rookie.HillClimbSpeedMultiplier);

        var warmupEdge = new RoadEdge(99, new RoadNode(10, 1, 1), new RoadNode(11, 2, 1), RoadTier.Asphalt, 500f);

        var veteran = new Truck(2, "Caminhão Veterano", spec);
        veteran.AssignPath(new[] { warmupEdge });
        veteran.Advance(1.0f, WeatherCondition.Clear, 1.0f); // +60 km >= 50 km
        Assert.Equal(TruckRank.Veteran, veteran.Rank);
        Assert.Equal(0.95f, veteran.FuelCostMultiplier);
        Assert.Equal(1.06f, veteran.HillClimbSpeedMultiplier);

        var legend = new Truck(3, "Caminhão Lenda", spec);
        legend.AssignPath(new[] { warmupEdge });
        legend.Advance(3.0f, WeatherCondition.Clear, 1.0f); // +180 km >= 150 km
        Assert.Equal(TruckRank.RoadLegend, legend.Rank);
        Assert.Equal(0.90f, legend.FuelCostMultiplier);
        Assert.Equal(1.12f, legend.HillClimbSpeedMultiplier);

        // Compara distância percorrida em trecho de serra (IsHillOrMountain = true)
        var n1 = new RoadNode(1, 1, 1);
        var n2 = new RoadNode(2, 2, 1);
        var hillEdge = new RoadEdge(1, n1, n2, RoadTier.Asphalt, 100f) { IsHillOrMountain = true };

        rookie.AssignPath(new[] { hillEdge });
        rookie.Status = TruckStatus.TravelingToPickup;
        legend.AssignPath(new[] { hillEdge });
        legend.Status = TruckStatus.TravelingToPickup;

        rookie.Advance(0.5f, WeatherCondition.Clear, 1.0f);
        legend.Advance(0.5f, WeatherCondition.Clear, 1.0f);

        Assert.True(legend.DistanceAlongCurrentEdgeKm > rookie.DistanceAlongCurrentEdgeKm);
        float ratio = legend.DistanceAlongCurrentEdgeKm / rookie.DistanceAlongCurrentEdgeKm;
        Assert.InRange(ratio, 1.11f, 1.13f);
    }

    [Fact]
    public void OnTruckRankUp_FiresWhenCrossingVeteranThreshold_AndReducesOperatingCost()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[3, 2].RoadLevel = RoadTier.Asphalt;
        var edge = LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 500f);

        var spec = new TruckSpecification("Scania 113H", 24, 80f, 12000, 10, 1);

        var engineRookie = new SimulationEngine(world)
        {
            CurrentWeather = WeatherCondition.Clear
        };
        engineRookie.Player.Balance = 100000;

        TruckRank? promotedRank = null;
        engineRookie.OnTruckRankUp += (_, r) => promotedRank = r;

        var rookieTruck = new Truck(1, "Caminhão #1", spec)
        {
            AutoPitStopEnabled = false,
            Status = TruckStatus.TravelingToPickup
        };
        rookieTruck.AssignPath(new[] { edge });
        engineRookie.ActiveTrucks.Add(rookieTruck);

        // Avança 0.8h (~64km) para cruzar 50km e disparar promoção para Veterano
        engineRookie.Tick(0.8f);
        Assert.Equal(TruckRank.Veteran, rookieTruck.Rank);
        Assert.Equal(TruckRank.Veteran, promotedRank);

        // Compara gasto operacional de um Novato vs Lenda da Estrada na mesma viagem
        var engineA = new SimulationEngine(CreateSimpleWorld()) { CurrentWeather = WeatherCondition.Clear };
        var engineB = new SimulationEngine(CreateSimpleWorld()) { CurrentWeather = WeatherCondition.Clear };
        engineA.Player.Balance = 100000;
        engineB.Player.Balance = 100000;

        var warmupEdge = new RoadEdge(99, new RoadNode(10, 1, 1), new RoadNode(11, 2, 1), RoadTier.Asphalt, 500f);
        var eA = new RoadEdge(1, new RoadNode(1, 1, 1), new RoadNode(2, 2, 1), RoadTier.Asphalt, 200f);
        var eB = new RoadEdge(1, new RoadNode(1, 1, 1), new RoadNode(2, 2, 1), RoadTier.Asphalt, 200f);

        var tRookie = new Truck(1, "Rookie", spec) { AutoPitStopEnabled = false, Status = TruckStatus.TravelingToPickup };
        tRookie.AssignPath(new[] { eA });
        engineA.ActiveTrucks.Add(tRookie);

        var tLegend = new Truck(2, "Legend", spec) { AutoPitStopEnabled = false, Status = TruckStatus.TravelingToPickup };
        tLegend.AssignPath(new[] { warmupEdge });
        tLegend.Advance(2.0f, WeatherCondition.Clear, 1.0f); // +160 km -> Lenda da Estrada
        tLegend.AssignPath(new[] { eB });
        engineB.ActiveTrucks.Add(tLegend);

        engineA.Tick(0.5f);
        engineB.Tick(0.5f);

        long costRookie = 100000 - engineA.Player.Balance;
        long costLegend = 100000 - engineB.Player.Balance;
        Assert.True(costRookie > 0);
        Assert.True(costLegend < costRookie);
    }

    [Fact]
    public void CompanyAndLineLivery_ResolvesOverride_AndPersistsAcrossSaveLoad()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        var edge = LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 500f);

        var engine = new SimulationEngine(world);
        engine.Player.Balance = 200_000;
        engine.UpdateCompanyLivery("Expresso Serra Sul", new FleetLivery
        {
            IsCustomized = true,
            PrimaryColorHex = "#dc2626",
            StripeColorHex = "#facc15",
            Finish = PaintFinish.Chrome
        });

        var garage = engine.BuildGarage(2, 3, "Garagem Central")!;
        var spec = ProgressionManager.AvailableTruckModels[0];
        var truck = engine.BuyTruck(garage, spec)!;
        truck.AssignPath(new[] { edge });
        truck.Advance(3.0f, WeatherCondition.Clear, 1.0f); // >= 150 km -> Lenda da Estrada

        var line = engine.CreateLogisticsLine("Linha Expressa", Array.Empty<ScheduleStop>());
        engine.AssignTruckToLogisticsLine(truck, line);

        // Sem pintura na linha, herda a da empresa
        Assert.Equal("#dc2626", engine.GetEffectiveLivery(truck).PrimaryColorHex);
        Assert.Equal(PaintFinish.Chrome, engine.GetEffectiveLivery(truck).Finish);

        // Com pintura exclusiva da linha, sobrescreve a da empresa
        engine.UpdateLineLivery(line.Id, new FleetLivery
        {
            IsCustomized = true,
            PrimaryColorHex = "#16a34a",
            StripeColorHex = "#f8fafc",
            Finish = PaintFinish.Matte
        });
        Assert.Equal("#16a34a", engine.GetEffectiveLivery(truck).PrimaryColorHex);
        Assert.Equal(PaintFinish.Matte, engine.GetEffectiveLivery(truck).Finish);

        string json = SaveGameSerializer.SerializeToJson(engine, 42);
        var (loaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        Assert.Equal("Expresso Serra Sul", loaded.Player.CompanyName);
        Assert.True(loaded.Player.CompanyLivery.IsCustomized);
        Assert.Equal("#dc2626", loaded.Player.CompanyLivery.PrimaryColorHex);
        Assert.Equal(PaintFinish.Chrome, loaded.Player.CompanyLivery.Finish);

        var loadedTruck = Assert.Single(loaded.ActiveTrucks);
        Assert.Equal(TruckRank.RoadLegend, loadedTruck.Rank);

        var effectiveLoaded = loaded.GetEffectiveLivery(loadedTruck);
        Assert.Equal("#16a34a", effectiveLoaded.PrimaryColorHex);
        Assert.Equal("#f8fafc", effectiveLoaded.StripeColorHex);
        Assert.Equal(PaintFinish.Matte, effectiveLoaded.Finish);

        // Removendo pintura da linha volta para a pintura da empresa
        loaded.UpdateLineLivery(line.Id, null);
        Assert.Equal("#dc2626", loaded.GetEffectiveLivery(loadedTruck).PrimaryColorHex);
    }
}
