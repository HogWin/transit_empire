using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class Phase4LogisticsTests
{
    private static (SimulationEngine Engine, Garage Garage, LogisticsHub Hub) CreateWorldWithFacilities(int seed = 101)
    {
        var config = new WorldGenerationConfig(Width: 40, Height: 40, CityCount: 2, IndustryCount: 3, Seed: seed);
        var world = new WorldGenerator(config).Generate();
        var engine = new SimulationEngine(world);
        engine.Player.Balance = 200_000;

        Garage? garage = null;
        LogisticsHub? hub = null;

        // Encontra duas células planas adjacentes a estradas para Garagem e Hub
        for (int x = 2; x < 38; x++)
        {
            for (int y = 2; y < 38; y++)
            {
                if (garage == null)
                    garage = engine.BuildGarage(x, y, "Garagem Central");
                else if (hub == null)
                    hub = engine.BuildLogisticsHub(x, y, "Hub Cross-Dock");

                if (garage != null && hub != null) break;
            }
            if (garage != null && hub != null) break;
        }

        Assert.NotNull(garage);
        Assert.NotNull(hub);
        return (engine, garage!, hub!);
    }

    [Fact]
    public void MultiStopSchedule_AndBackhaul_ExecutesInOrder()
    {
        var (engine, garage, _) = CreateWorldWithFacilities(202);
        var ind1 = engine.World.Industries[0];
        var ind2 = engine.World.Industries[1];
        var city1 = engine.World.Cities[0];
        var city2 = engine.World.Cities[1];

        ind1.OutputStock = 100;
        ind2.OutputStock = 100;

        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind1.Id, ScheduleAction.Load, DispatchCondition.Immediate),
            new ScheduleStop(ScheduleStopKind.City, city1.Id, ScheduleAction.Unload),
            new ScheduleStop(ScheduleStopKind.Industry, ind2.Id, ScheduleAction.Load, DispatchCondition.Immediate),
            new ScheduleStop(ScheduleStopKind.City, city2.Id, ScheduleAction.Unload),
        });

        int deliveries = 0;
        engine.OnDeliveryCompleted += (_, _, _, _) => deliveries++;

        for (int i = 0; i < 600 && deliveries < 2; i++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(deliveries >= 2, $"Esperava ao menos 2 entregas no ciclo de backhaul, obteve {deliveries}");
    }

    [Fact]
    public void ConditionalDispatch_WaitFullLoad_WaitsUntilCapacityReached()
    {
        var (engine, garage, _) = CreateWorldWithFacilities(303);
        var ind = engine.World.Industries[0];
        var city = engine.World.Cities[0];
        ind.OutputStock = 4; // Menor que capacidade de 15 do VUC

        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.WaitFullLoad),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });

        // Avança ticks até o caminhão chegar na indústria e entrar em espera
        for (int i = 0; i < 200; i++)
        {
            engine.Tick(0.25f);
            if (truck.Status == TruckStatus.WaitingForCargo)
                break;
        }

        Assert.Equal(TruckStatus.WaitingForCargo, truck.Status);
        Assert.Equal(4, truck.LoadedCargoAmount);
        Assert.Equal(0, truck.CurrentScheduleIndex);

        // Abastece estoque da indústria e avança 1 tick: deve completar carga e partir para entrega
        ind.OutputStock += 30;
        engine.Tick(0.25f);

        Assert.Equal(truck.Spec.Capacity, truck.LoadedCargoAmount);
        Assert.Equal(1, truck.CurrentScheduleIndex);
        Assert.NotEqual(TruckStatus.WaitingForCargo, truck.Status);
    }

    [Fact]
    public void LogisticsHub_RealCrossDocking_StoresAndTransfersCargoBetweenTrucks()
    {
        var (engine, garage, hub) = CreateWorldWithFacilities(404);
        var ind = engine.World.Industries[0];
        var city = engine.World.Cities[0];
        ind.OutputStock = 100;

        // Caminhão 1 (Coletor): Indústria -> Hub
        var feeder = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        feeder.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.Immediate),
            new ScheduleStop(ScheduleStopKind.LogisticsHub, hub.Id, ScheduleAction.Unload)
        });

        for (int i = 0; i < 300 && hub.CurrentTotalStored == 0; i++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(hub.CurrentTotalStored > 0, "Hub deveria armazenar carga depositada pelo caminhão coletor");
        int storedAmount = hub.StoredCargo[ind.OutputResource];
        Assert.True(storedAmount > 0);

        // Caminhão 2 (Tronco): Hub -> Cidade
        var hauler = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        hauler.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.LogisticsHub, hub.Id, ScheduleAction.Load, DispatchCondition.Immediate, ind.OutputResource),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });

        bool cityReceivedFromHub = false;
        engine.OnDeliveryCompleted += (t, res, amt, _) =>
        {
            if (t.Id == hauler.Id && res == ind.OutputResource && amt > 0)
                cityReceivedFromHub = true;
        };

        for (int i = 0; i < 400 && !cityReceivedFromHub; i++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(cityReceivedFromHub, "Caminhão tronco deveria retirar carga do Hub e entregar na Cidade");
    }

    [Fact]
    public void CloneTruck_CopiesSpecAndMultiStopSchedule()
    {
        var (engine, garage, hub) = CreateWorldWithFacilities(505);
        var ind = engine.World.Industries[0];
        var city = engine.World.Cities[0];

        var original = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        original.AutoPitStopEnabled = false;
        original.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.WaitHalfLoad),
            new ScheduleStop(ScheduleStopKind.LogisticsHub, hub.Id, ScheduleAction.Unload),
            new ScheduleStop(ScheduleStopKind.LogisticsHub, hub.Id, ScheduleAction.Load, DispatchCondition.Immediate, ind.OutputResource),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });

        var clone = engine.CloneTruck(original);
        Assert.NotNull(clone);
        Assert.NotEqual(original.Id, clone!.Id);
        Assert.Equal(original.Spec.Name, clone.Spec.Name);
        Assert.Equal(original.AutoPitStopEnabled, clone.AutoPitStopEnabled);
        Assert.True(clone.HasManualSchedule);
        Assert.Equal(4, clone.Schedule.Count);
        Assert.Equal(DispatchCondition.WaitHalfLoad, clone.Schedule[0].Condition);
        Assert.Equal(ScheduleStopKind.LogisticsHub, clone.Schedule[1].Kind);
    }

    [Fact]
    public void AutoPitStop_DivertsLowConditionTruck_RepairsAndResumes()
    {
        var (engine, garage, _) = CreateWorldWithFacilities(606);
        var ind = engine.World.Industries[0];
        var city = engine.World.Cities[0];

        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.AutoPitStopEnabled = true;
        truck.Condition = 25f; // Abaixo do limiar de 35%
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.Immediate),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });

        bool pitStopDone = false;
        engine.OnTruckPitStopCompleted += (t, _, _) =>
        {
            if (t.Id == truck.Id) pitStopDone = true;
        };

        for (int i = 0; i < 300 && !pitStopDone; i++)
        {
            engine.Tick(0.25f);
        }

        Assert.True(pitStopDone, "Caminhão com condição < 35% deveria fazer pit-stop automático");
        Assert.Equal(100f, truck.Condition);
        Assert.False(truck.IsDivertedForPitStop);
    }

    [Fact]
    public void SaveAndLoad_PreservesMultiStopScheduleAndPitStopFlags()
    {
        var (engine, garage, hub) = CreateWorldWithFacilities(707);
        var ind = engine.World.Industries[0];
        var city = engine.World.Cities[0];

        var truck = engine.BuyTruck(garage, ProgressionManager.AvailableTruckModels[0])!;
        truck.AutoPitStopEnabled = false;
        truck.SetSchedule(new[]
        {
            new ScheduleStop(ScheduleStopKind.Industry, ind.Id, ScheduleAction.Load, DispatchCondition.WaitFullLoad),
            new ScheduleStop(ScheduleStopKind.LogisticsHub, hub.Id, ScheduleAction.Unload),
            new ScheduleStop(ScheduleStopKind.City, city.Id, ScheduleAction.Unload)
        });
        truck.CurrentScheduleIndex = 1;

        string json = SaveGameSerializer.SerializeToJson(engine, 707);
        var (loaded, _) = SaveGameSerializer.DeserializeFromJson(json);

        var loadedTruck = loaded.ActiveTrucks.Single();
        Assert.False(loadedTruck.AutoPitStopEnabled);
        Assert.Equal(3, loadedTruck.Schedule.Count);
        Assert.Equal(1, loadedTruck.CurrentScheduleIndex);
        Assert.Equal(DispatchCondition.WaitFullLoad, loadedTruck.Schedule[0].Condition);
        Assert.Equal(ScheduleStopKind.LogisticsHub, loadedTruck.Schedule[1].Kind);
    }
}

