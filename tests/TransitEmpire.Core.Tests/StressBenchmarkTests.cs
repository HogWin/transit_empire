using System.Diagnostics;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;
using Xunit;

namespace TransitEmpire.Core.Tests;

public class StressBenchmarkTests
{
    [Fact]
    public void Simulation_LargeScaleStressTest_500Trucks_RunsUnder1Second()
    {
        // 1. Gera um mapa grande (100x100) com 8 cidades e 12 indústrias
        var config = new WorldGenerationConfig(Width: 100, Height: 100, CityCount: 8, IndustryCount: 12, Seed: 2026);
        var generator = new WorldGenerator(config);
        var world = generator.Generate();
        var engine = new SimulationEngine(world);

        // 2. Cria 500 caminhões com rotas ativas
        var spec = ProgressionManager.AvailableTruckModels.First();
        var allEdges = world.RoadEdges;

        for (int i = 0; i < 500; i++)
        {
            var truck = new Truck(i + 1, $"Caminhão #{i + 1}", spec)
            {
                Status = TruckStatus.TravelingToDelivery,
                LoadedCargoType = ResourceType.Food,
                LoadedCargoAmount = 15
            };

            // Atribui uma rota cíclica de arestas da malha
            var sampleRoute = allEdges.Skip(i % System.Math.Max(1, allEdges.Count - 5)).Take(4).ToList();
            if (sampleRoute.Count > 0)
            {
                truck.AssignPath(sampleRoute);
                engine.ActiveTrucks.Add(truck);
            }
        }

        // 3. Roda 500 ticks completos de simulação (equivalente a ~20 dias no jogo) e mede o tempo
        var stopwatch = Stopwatch.StartNew();

        for (int tick = 0; tick < 500; tick++)
        {
            engine.Tick(0.25f);
        }

        stopwatch.Stop();

        // 500 caminhões rodando 500 ticks com física cinemática e despacho
        // Deve rodar em menos de 1.000 ms (1 segundo), garantindo mais de 60 FPS estáveis na engine
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"A simulação em larga escala deve ser ultrarrápida. Tempo gasto: {stopwatch.ElapsedMilliseconds}ms");
    }
}

