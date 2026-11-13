using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class SprintE4CoreTests
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
    public void RivalTruck_CrossesTollStation_PaysRealTollAndUpdatesWeighboard()
    {
        var world = CreateSimpleWorld();
        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        var n3 = new RoadNode(3, 4, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3 });
        world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[3, 2].RoadLevel = RoadTier.Asphalt;
        world.Grid[4, 2].RoadLevel = RoadTier.Asphalt;
        LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 10f);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Asphalt, 10f);

        var engine = new SimulationEngine(world) { CurrentWeather = WeatherCondition.Clear };
        engine.Player.Balance = 50_000;
        var station = engine.BuildTollStation(3, 2, out _) !;
        Assert.NotNull(station);

        long balanceBeforeToll = engine.Player.Balance;
        long? collectedAmount = null;
        string? weighMsg = null;
        engine.OnTollCollected += (_, amt) => collectedAmount = amt;
        engine.OnTollWeighUpdated += (_, msg, _) => weighMsg = msg;

        var rival = engine.Rivals[0];
        var npc = engine.SpawnRivalTruck(rival, n1, n3);
        Assert.NotNull(npc);

        // Avança até cruzar o nó central (3,2) onde está a praça de pedágio
        for (int tick = 0; tick < 10; tick++)
            engine.Tick(0.05f);

        Assert.Equal(balanceBeforeToll + station.TollFee, engine.Player.Balance);
        Assert.Equal(station.TollFee, collectedAmount);
        Assert.Equal(1, station.TrucksInspected);
        Assert.Contains("RIVAL OK", station.LastWeighDisplay);
        Assert.Contains("RIVAL OK", weighMsg);
    }

    [Fact]
    public void AmbientCivilTraffic_OccupiesRoadCapacity_AndCleansUpOnDemolish()
    {
        var world = CreateSimpleWorld();
        var c1 = new City(1, "Vila Norte", 2, 2) { Population = 450 };
        var c2 = new City(2, "Porto Sul", 6, 2) { Population = 450 };
        world.Cities.AddRange(new[] { c1, c2 });

        var n1 = new RoadNode(1, 2, 2);
        var n2 = new RoadNode(2, 3, 2);
        var n3 = new RoadNode(3, 4, 2);
        var n4 = new RoadNode(4, 5, 2);
        var n5 = new RoadNode(5, 6, 2);
        world.RoadNodes.AddRange(new[] { n1, n2, n3, n4, n5 });
        for (int x = 2; x <= 6; x++)
            world.Grid[x, 2].RoadLevel = RoadTier.Dirt;

        LinkBidirectional(world, 1, n1, n2, RoadTier.Dirt, 20f);
        LinkBidirectional(world, 3, n2, n3, RoadTier.Dirt, 20f);
        LinkBidirectional(world, 5, n3, n4, RoadTier.Dirt, 20f);
        LinkBidirectional(world, 7, n4, n5, RoadTier.Dirt, 20f);

        var engine = new SimulationEngine(world) { CurrentWeather = WeatherCondition.Clear };

        // Dispara ciclos de geração automática de tráfego civil + rival entre as duas cidades
        for (int i = 0; i < 6; i++)
            engine.Tick(0.10f);

        Assert.NotEmpty(engine.TrafficVehicles);
        Assert.Contains(engine.TrafficVehicles, v => v.Kind is TrafficVehicleKind.CivilCar or TrafficVehicleKind.IntercityBus);
        Assert.True(world.RoadEdges.Sum(e => e.ActiveTrucksCount) > 0);

        // Demolir a estrada central remove veículos NPC afetados e limpa ocupação
        Assert.True(engine.TryDemolishAt(4, 2, out _, out _));
        Assert.DoesNotContain(engine.TrafficVehicles, v => v.CurrentEdge == null);
    }

    [Fact]
    public void SpecialConvoyContract_GrantsTripleFreight_ActivatesEscort_AndPersists()
    {
        static (SimulationEngine engine, City city, Truck truck) BuildDeliveryScenario(bool withSpecialConvoy)
        {
            var world = CreateSimpleWorld();
            var n1 = new RoadNode(1, 2, 2);
            var n2 = new RoadNode(2, 5, 2);
            world.RoadNodes.AddRange(new[] { n1, n2 });
            world.Grid[2, 2].RoadLevel = RoadTier.Asphalt;
            world.Grid[5, 2].RoadLevel = RoadTier.Asphalt;
            var (edgeFwd, _) = LinkBidirectional(world, 1, n1, n2, RoadTier.Asphalt, 20f);

            var indSteel = new Industry(100, "Siderúrgica Alfa", 2, 1, ResourceType.Steel, ResourceType.None);
            indSteel.Produce(100);
            var city = new City(1, "Metrópole", 5, 3);
            world.Industries.Add(indSteel);
            world.Cities.Add(city);

            var engine = new SimulationEngine(world)
            {
                CurrentWeather = WeatherCondition.Clear
            };
            engine.ActiveContracts.Clear();
            engine.Player.Balance = 100_000;
            var garage = engine.BuildGarage(2, 3, "Garagem Central")!;
            var spec = ProgressionManager.AvailableTruckModels[1];
            var truck = engine.BuyTruck(garage, spec)!;

            if (withSpecialConvoy)
            {
                engine.CreateSpecialConvoyContract(city, ResourceType.Steel, 60);
            }

            truck.AutoPitStopEnabled = false;
            truck.SetSchedule(new[]
            {
                new ScheduleStop(ScheduleStopKind.Industry, indSteel.Id, ScheduleAction.Load),
                new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
            });

            // Carrega aço na indústria e coloca em trânsito para entrega na cidade
            engine.Tick(0.05f);
            return (engine, city, truck);
        }

        var (engNormal, _, _) = BuildDeliveryScenario(withSpecialConvoy: false);
        long normalDeliveryReward = 0;
        engNormal.OnDeliveryCompleted += (_, _, _, rev) => normalDeliveryReward = rev;

        var (engConvoy, _, truckConvoy) = BuildDeliveryScenario(withSpecialConvoy: true);
        long convoyDeliveryReward = 0;
        engConvoy.OnDeliveryCompleted += (_, _, _, rev) => convoyDeliveryReward = rev;

        // Tick parcial confirma escolta ativa em trânsito carregando carga do Comboio Especial
        engConvoy.Tick(0.05f);
        engNormal.Tick(0.05f);
        Assert.True(truckConvoy.IsSpecialConvoyActive);

        // Testa persistência de Comboio Especial e Balança no SaveGameSerializer antes da entrega
        var station = engConvoy.BuildTollStation(2, 2, out _);
        if (station != null) station.LastWeighDisplay = "74t RODOTREM";
        string json = SaveGameSerializer.SerializeToJson(engConvoy, 777);
        var (reloaded, _) = SaveGameSerializer.DeserializeFromJson(json);
        var reloadedConvoy = Assert.Single(reloaded.ActiveContracts);
        Assert.True(reloadedConvoy.IsSpecialConvoy);
        Assert.Equal(3.0f, reloadedConvoy.FreightMultiplier);
        Assert.True(Assert.Single(reloaded.ActiveTrucks).IsSpecialConvoyActive);
        if (station != null)
            Assert.Equal("74t RODOTREM", Assert.Single(reloaded.TollStations).LastWeighDisplay);

        // Conclui a viagem nos dois cenários para comparar pagamento normal vs Comboio Especial 3x
        for (int i = 0; i < 20 && normalDeliveryReward == 0; i++)
            engNormal.Tick(0.25f);
        for (int i = 0; i < 20 && convoyDeliveryReward == 0; i++)
            engConvoy.Tick(0.25f);

        Assert.True(normalDeliveryReward > 0);
        Assert.Equal(normalDeliveryReward * 3, convoyDeliveryReward);
        Assert.False(truckConvoy.IsSpecialConvoyActive);
    }
}
