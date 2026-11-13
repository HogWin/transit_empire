using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

/// <summary>
/// Ponte de alto nível entre o núcleo de simulação headless (TransitEmpire.Core)
/// e o motor gráfico de renderização 3D (Godot Engine).
/// </summary>
public class SimulationBridge
{
    public SimulationEngine Engine { get; private set; }
    public GeneratedWorld World => Engine.World;
    public int Seed { get; private set; }
    public WorldScenario Scenario { get; private set; }
    public float TimeScale { get; set; } = 0.0f; // Inicia pausado por padrão

    private readonly Dictionary<int, (int GridX, int GridY)> _truckLastGridPos = new();
    private readonly Dictionary<int, (int OriginId, int DestId)> _busAutoRouteTargets = new();
    private int _routeAssignmentCounter = 0;

    public SimulationBridge(int width = 100, int height = 100, int? seed = null, WorldScenario scenario = WorldScenario.Balanced)
    {
        Seed = seed ?? Random.Shared.Next(1, 1_000_000);
        Scenario = scenario;
        int maxDim = Math.Max(width, height);
        int cityCount = maxDim <= 55 ? 4 : (maxDim <= 85 ? 5 : 6);
        int industryCount = maxDim <= 55 ? 7 : (maxDim <= 85 ? 9 : 10);

        var config = new WorldGenerationConfig(
            Width: width,
            Height: height,
            CityCount: cityCount,
            IndustryCount: industryCount,
            Seed: Seed,
            Scenario: scenario);
        var generator = new WorldGenerator(config);
        var world = generator.Generate();
        Engine = new SimulationEngine(world);
    }

    /// <summary>
    /// Inicializa uma garagem matriz e os primeiros caminhões operando nas rotas procedurais
    /// para que o mundo já inicie vivo no Godot.
    /// </summary>
    public void SeedInitialFleetAndInfrastructure()
    {
        if (Engine.Garages.Count > 0 || World.Cities.Count == 0)
            return;

        // Encontra um terreno plano adjacente a uma estrada próximo à primeira cidade
        var mainCity = World.Cities[0];
        Garage? initialGarage = null;

        for (int radius = 2; radius <= 10 && initialGarage == null; radius++)
        {
            for (int dx = -radius; dx <= radius && initialGarage == null; dx++)
            {
                for (int dy = -radius; dy <= radius && initialGarage == null; dy++)
                {
                    int gx = mainCity.CenterX + dx;
                    int gy = mainCity.CenterY + dy;
                    initialGarage = Engine.BuildGarage(gx, gy, $"Garagem Matriz {mainCity.Name}");
                }
            }
        }

        if (initialGarage != null)
        {
            // Compensa o saldo inicial para manter o capital de giro do jogador saudável
            Engine.Player.Balance += 8000;

            var model0 = ProgressionManager.AvailableTruckModels[0];
            var model1 = ProgressionManager.AvailableTruckModels[1];

            Engine.Player.Balance += model0.PurchasePrice + model1.PurchasePrice;
            var t1 = Engine.BuyTruck(initialGarage, model0);
            var t2 = Engine.BuyTruck(initialGarage, model1);

            if (t1 != null) AssignAndDispatchTruck(t1, initialGarage.AccessNode);
            if (t2 != null) AssignAndDispatchTruck(t2, initialGarage.AccessNode);
        }
    }

    public void UpdateSimulation(float deltaSeconds)
    {
        if (TimeScale <= 0.001f)
            return;

        // Converte o delta de tempo do jogo para horas de simulação
        float deltaHours = deltaSeconds * 0.65f * TimeScale;
        Engine.Tick(deltaHours);
        DispatchPendingTrucks();
    }

    /// <summary>
    /// Mantém os caminhões em ciclo contínuo nas cadeias produtivas de 2 estágios:
    /// Estágio 1: Matéria-Prima -> Indústria Processadora
    /// Estágio 2: Produto Processado/Primário -> Cidade
    /// </summary>
    public void DispatchPendingTrucks()
    {
        if (World.Industries.Count == 0 || World.Cities.Count == 0 || World.RoadNodes.Count == 0)
            return;

        foreach (var truck in Engine.ActiveTrucks)
        {
            if (truck.CurrentEdge != null)
            {
                _truckLastGridPos[truck.Id] = (truck.CurrentEdge.ToNode.GridX, truck.CurrentEdge.ToNode.GridY);
                continue;
            }

            // Caminhões com agenda multi-paradas, aguardando carga ou em pit-stop já são gerenciados pelo SimulationEngine
            if ((truck.HasManualSchedule && truck.Schedule.Count > 0) ||
                truck.IsDivertedForPitStop ||
                truck.Status == TruckStatus.WaitingForCargo ||
                truck.Status == TruckStatus.TravelingToPitStop)
            {
                continue;
            }

            if (truck.Spec.IsPassengerBus)
            {
                ProcessAutoBus(truck);
                continue;
            }

            // Se o caminhão está vazio e sem rota (ou em modo automático após concluir entrega), avalia a próxima etapa
            bool needsRoute = truck.AssignedOriginIndustry == null || (truck.AssignedTargetCity == null && truck.AssignedTargetIndustry == null);
            if (truck.LoadedCargoAmount <= 0 && (needsRoute || (!truck.HasManualSchedule && truck.Status == TruckStatus.Idle)))
            {
                AssignNextChainRoute(truck);
            }

            RoadNode currentNode = GetClosestRoadNodeForTruck(truck);

            if (truck.LoadedCargoAmount > 0)
            {
                // Segue para o destino (Indústria Processadora no Estágio 1, ou Cidade no Estágio 2)
                (int destX, int destY) = truck.AssignedTargetIndustry != null
                    ? (truck.AssignedTargetIndustry.GridX, truck.AssignedTargetIndustry.GridY)
                    : (truck.AssignedTargetCity!.CenterX, truck.AssignedTargetCity.CenterY);

                RoadNode destNode = FindNearestRoadNode(destX, destY);
                if (destNode != currentNode && Engine.DispatchTruck(truck, destNode))
                {
                    truck.Status = TruckStatus.TravelingToDelivery;
                }
                else
                {
                    var fallbackEdge = currentNode.OutgoingEdges.FirstOrDefault();
                    if (fallbackEdge != null)
                    {
                        truck.AssignPath(new[] { fallbackEdge });
                        truck.Status = TruckStatus.TravelingToDelivery;
                    }
                }
            }
            else if (truck.AssignedOriginIndustry != null)
            {
                // Se estiver em modo automático e a indústria secundária estiver sem insumo, reatribui para Estágio 1
                if (!truck.HasManualSchedule &&
                    truck.AssignedOriginIndustry.OutputStock <= 0 &&
                    truck.AssignedOriginIndustry.RequiredInputResource != ResourceType.None &&
                    truck.AssignedOriginIndustry.InputStock < 5)
                {
                    AssignNextChainRoute(truck);
                }

                if (truck.AssignedOriginIndustry.OutputStock < 5 &&
                    truck.AssignedOriginIndustry.RequiredInputResource == ResourceType.None)
                {
                    truck.AssignedOriginIndustry.Produce();
                }

                RoadNode indNode = FindNearestRoadNode(truck.AssignedOriginIndustry.GridX, truck.AssignedOriginIndustry.GridY);
                if (indNode != currentNode && Engine.DispatchTruck(truck, indNode))
                {
                    truck.Status = TruckStatus.TravelingToPickup;
                }
                else
                {
                    int loaded = truck.AssignedOriginIndustry.DispatchCargo(truck.Spec.Capacity);
                    if (loaded <= 0 && truck.AssignedOriginIndustry.RequiredInputResource == ResourceType.None)
                    {
                        truck.AssignedOriginIndustry.Produce();
                        loaded = truck.AssignedOriginIndustry.DispatchCargo(truck.Spec.Capacity);
                    }

                    if (loaded > 0)
                    {
                        truck.LoadedCargoType = truck.AssignedOriginIndustry.OutputResource;
                        truck.LoadedCargoAmount = loaded;

                        (int destX, int destY) = truck.AssignedTargetIndustry != null
                            ? (truck.AssignedTargetIndustry.GridX, truck.AssignedTargetIndustry.GridY)
                            : (truck.AssignedTargetCity!.CenterX, truck.AssignedTargetCity.CenterY);

                        RoadNode destNode = FindNearestRoadNode(destX, destY);
                        if (Engine.DispatchTruck(truck, destNode))
                        {
                            truck.Status = TruckStatus.TravelingToDelivery;
                        }
                    }
                }
            }
        }
    }

    private void AssignNextChainRoute(Truck truck)
    {
        int idx = _routeAssignmentCounter++;
        RoadNode cur = GetClosestRoadNodeForTruck(truck);
        int tx = cur.GridX;
        int ty = cur.GridY;

        var localIndustries = World.Industries
            .Where(i => World.AreSameContinent(tx, ty, i.GridX, i.GridY))
            .ToList();
        var localCities = World.Cities
            .Where(c => World.AreSameContinent(tx, ty, c.CenterX, c.CenterY))
            .ToList();

        if (localIndustries.Count == 0) localIndustries = World.Industries;
        if (localCities.Count == 0) localCities = World.Cities;

        // Verifica se alguma indústria processadora ou multi-insumo do mesmo continente precisa de matéria-prima (Estágio 1)
        var needyPrimary = localIndustries
            .Where(proc => proc.RequiredInputResource != ResourceType.None && proc.InputStock < 80)
            .ToList();

        if (needyPrimary.Count > 0 && idx % 4 == 0)
        {
            var proc = needyPrimary[(idx / 4) % needyPrimary.Count];
            var supplier = localIndustries.FirstOrDefault(i => i.OutputResource == proc.RequiredInputResource);
            if (supplier != null)
            {
                truck.AssignedOriginIndustry = supplier;
                truck.AssignedTargetIndustry = proc;
                truck.AssignedTargetCity = null;
                return;
            }
        }

        var needySecondary = localIndustries
            .Where(proc => proc.SecondaryInputResource != ResourceType.None && proc.SecondaryInputStock < 40)
            .ToList();

        if (needySecondary.Count > 0 && idx % 4 == 1)
        {
            var proc = needySecondary[(idx / 4) % needySecondary.Count];
            var supplier = localIndustries.FirstOrDefault(i => i.OutputResource == proc.SecondaryInputResource);
            if (supplier != null)
            {
                truck.AssignedOriginIndustry = supplier;
                truck.AssignedTargetIndustry = proc;
                truck.AssignedTargetCity = null;
                return;
            }
        }

        var needyTertiary = localIndustries
            .Where(proc => proc.TertiaryInputResource != ResourceType.None && proc.TertiaryInputStock < 35)
            .ToList();

        if (needyTertiary.Count > 0 && idx % 4 == 2)
        {
            var proc = needyTertiary[(idx / 4) % needyTertiary.Count];
            var supplier = localIndustries.FirstOrDefault(i => i.OutputResource == proc.TertiaryInputResource);
            if (supplier != null)
            {
                truck.AssignedOriginIndustry = supplier;
                truck.AssignedTargetIndustry = proc;
                truck.AssignedTargetCity = null;
                return;
            }
        }

        // Caso contrário, atribui rota de entrega para Cidade no mesmo continente (Estágio 2)
        var stockedIndustries = localIndustries
            .Where(i => i.OutputStock > 0 || i.RequiredInputResource == ResourceType.None)
            .ToList();

        var sourcePool = stockedIndustries.Count > 0 ? stockedIndustries : localIndustries;
        truck.AssignedOriginIndustry = sourcePool[idx % sourcePool.Count];
        truck.AssignedTargetIndustry = null;
        truck.AssignedTargetCity = localCities[idx % localCities.Count];
    }

    public void AssignAndDispatchTruck(Truck truck, RoadNode startNode)
    {
        _truckLastGridPos[truck.Id] = (startNode.GridX, startNode.GridY);
        if (truck.Spec.IsPassengerBus)
        {
            ProcessAutoBus(truck);
            return;
        }

        AssignNextChainRoute(truck);

        if (truck.AssignedOriginIndustry != null)
        {
            RoadNode indNode = FindNearestRoadNode(truck.AssignedOriginIndustry.GridX, truck.AssignedOriginIndustry.GridY);
            if (!Engine.DispatchTruck(truck, indNode))
            {
                var firstEdge = startNode.OutgoingEdges.FirstOrDefault();
                if (firstEdge != null)
                {
                    truck.AssignPath(new[] { firstEdge });
                    truck.Status = TruckStatus.TravelingToPickup;
                }
            }
        }
    }

    private void ProcessAutoBus(Truck bus)
    {
        var terminals = Engine.BusTerminals;
        if (terminals.Count < 2)
            return;

        RoadNode currentNode = GetClosestRoadNodeForTruck(bus);
        if (!_busAutoRouteTargets.TryGetValue(bus.Id, out var pair) ||
            terminals.All(t => t.Id != pair.OriginId) ||
            terminals.All(t => t.Id != pair.DestId))
        {
            var origin = terminals.FirstOrDefault(t => t.AccessNode == currentNode && t.WaitingPassengers > 0)
                ?? terminals.OrderByDescending(t => t.WaitingPassengers).First();
            var candidates = terminals
                .Where(t => t.Id != origin.Id && World.AreSameContinent(origin.GridX, origin.GridY, t.GridX, t.GridY))
                .ToList();
            if (candidates.Count == 0)
                candidates = terminals.Where(t => t.Id != origin.Id).ToList();
            if (candidates.Count == 0)
                return;

            var dest = candidates[_routeAssignmentCounter++ % candidates.Count];
            pair = (origin.Id, dest.Id);
            _busAutoRouteTargets[bus.Id] = pair;
        }

        var origTerm = terminals.First(t => t.Id == pair.OriginId);
        var destTerm = terminals.First(t => t.Id == pair.DestId);

        if (bus.LoadedCargoAmount > 0)
        {
            if (destTerm.AccessNode == currentNode)
            {
                Engine.DeliverPassengersToBusTerminal(bus, destTerm);
                _busAutoRouteTargets.Remove(bus.Id);
                bus.Status = TruckStatus.Idle;
            }
            else if (Engine.DispatchTruck(bus, destTerm.AccessNode))
            {
                bus.Status = TruckStatus.TravelingToDelivery;
            }
            return;
        }

        if (origTerm.AccessNode == currentNode)
        {
            int boarded = origTerm.BoardPassengers(bus.Spec.Capacity);
            if (boarded > 0)
            {
                bus.LoadedCargoType = ResourceType.None;
                bus.LoadedCargoAmount = boarded;
                if (Engine.DispatchTruck(bus, destTerm.AccessNode))
                    bus.Status = TruckStatus.TravelingToDelivery;
            }
            else
            {
                _busAutoRouteTargets.Remove(bus.Id);
            }
        }
        else if (Engine.DispatchTruck(bus, origTerm.AccessNode))
        {
            bus.Status = TruckStatus.TravelingToPickup;
        }
    }

    private RoadNode GetClosestRoadNodeForTruck(Truck truck)
    {
        if (_truckLastGridPos.TryGetValue(truck.Id, out var pos))
        {
            return FindNearestRoadNode(pos.GridX, pos.GridY);
        }

        var garage = Engine.Garages.FirstOrDefault(g => g.StationedTrucks.Contains(truck)) ?? Engine.Garages.FirstOrDefault();
        if (garage != null)
            return garage.AccessNode;

        return World.RoadNodes[0];
    }

    public RoadNode FindNearestRoadNode(int x, int y)
    {
        RoadNode best = World.RoadNodes[0];
        float minDist = float.MaxValue;
        foreach (var node in World.RoadNodes)
        {
            float d = MathF.Abs(node.GridX - x) + MathF.Abs(node.GridY - y);
            if (d < minDist)
            {
                minDist = d;
                best = node;
            }
        }
        return best;
    }

    public bool TryBuildRoadTile(int gridX, int gridY, out RoadStructureType builtStructure)
    {
        return Engine.BuildRoadTile(gridX, gridY, out builtStructure);
    }

    public bool TryUpgradeRoad(int gridX, int gridY, RoadTier targetTier)
    {
        return Engine.UpgradeRoadTile(gridX, gridY, targetTier);
    }

    public int TryUpgradeRoadCorridor(int startX, int startY, RoadTier targetTier, int maxTiles = 30)
    {
        return Engine.UpgradeRoadCorridor(startX, startY, targetTier, maxTiles);
    }

    public Garage? TryBuildGarage(int gridX, int gridY, string name)
    {
        return Engine.BuildGarage(gridX, gridY, name);
    }

    public LogisticsHub? TryBuildLogisticsHub(int gridX, int gridY, string name)
    {
        return Engine.BuildLogisticsHub(gridX, gridY, name);
    }

    public BusTerminal? TryBuildBusTerminal(int gridX, int gridY, out string statusMessage, string? customName = null)
    {
        return Engine.BuildBusTerminal(gridX, gridY, out statusMessage, customName);
    }

    public Truck? TryBuyAndDispatchTruck(int modelIndex, out string statusMessage)
    {
        if (modelIndex < 0 || modelIndex >= ProgressionManager.AvailableTruckModels.Count)
        {
            statusMessage = "Modelo de caminhão inválido.";
            return null;
        }

        var spec = ProgressionManager.AvailableTruckModels[modelIndex];
        if (!Engine.Research.IsTruckUnlocked(spec, Engine.Player.Level))
        {
            statusMessage = $"Requer Nível {spec.RequiredPlayerLevel} ou Pesquisa correspondente para comprar {spec.Name}.";
            return null;
        }

        if (Engine.Player.Balance < spec.PurchasePrice)
        {
            statusMessage = $"Saldo insuficiente! {spec.Name} custa R$ {spec.PurchasePrice:N0}.";
            return null;
        }

        var availableGarage = Engine.Garages.FirstOrDefault(g => !g.IsFull);
        if (availableGarage == null)
        {
            statusMessage = "Todas as garagens estão cheias! Construa uma nova Garagem próxima a uma estrada.";
            return null;
        }

        var truck = Engine.BuyTruck(availableGarage, spec);
        if (truck == null)
        {
            statusMessage = "Não foi possível adquirir o caminhão.";
            return null;
        }

        AssignAndDispatchTruck(truck, availableGarage.AccessNode);
        statusMessage = spec.IsPassengerBus
            ? (Engine.BusTerminals.Count >= 2
                ? $"🚌 {truck.Name} adquirido na {availableGarage.Name} e em linha intermunicipal!"
                : $"🚌 {truck.Name} adquirido na {availableGarage.Name}! Construa ao menos 2 Rodoviárias para iniciar rotas de passageiros.")
            : $"{truck.Name} adquirido na {availableGarage.Name} e despachado para {truck.AssignedOriginIndustry?.Name}!";
        return truck;
    }

    public string SetTruckManualSchedule(Truck truck, Industry origin, Industry? targetIndustry, City? targetCity)
    {
        var stops = new List<ScheduleStop>
        {
            new(ScheduleStopKind.Industry, origin.Id, ScheduleAction.Load, DispatchCondition.Immediate)
        };

        if (targetIndustry != null)
            stops.Add(new ScheduleStop(ScheduleStopKind.Industry, targetIndustry.Id, ScheduleAction.Unload));
        else if (targetCity != null)
            stops.Add(new ScheduleStop(ScheduleStopKind.City, targetCity.Id, ScheduleAction.Unload));

        return SetTruckMultiStopSchedule(truck, stops);
    }

    public string SetTruckMultiStopSchedule(Truck truck, IReadOnlyList<ScheduleStop> stops)
    {
        if (stops.Count == 0)
            return SetTruckAutoSchedule(truck);

        truck.SetSchedule(stops);

        var firstLoad = stops.FirstOrDefault(s => s.Action == ScheduleAction.Load && s.Kind == ScheduleStopKind.Industry);
        var firstUnloadInd = stops.FirstOrDefault(s => s.Action == ScheduleAction.Unload && s.Kind == ScheduleStopKind.Industry);
        var firstUnloadCity = stops.FirstOrDefault(s => s.Action == ScheduleAction.Unload && s.Kind == ScheduleStopKind.City);

        truck.AssignedOriginIndustry = firstLoad != null ? World.Industries.FirstOrDefault(i => i.Id == firstLoad.TargetId) : null;
        truck.AssignedTargetIndustry = firstUnloadInd != null ? World.Industries.FirstOrDefault(i => i.Id == firstUnloadInd.TargetId) : null;
        truck.AssignedTargetCity = firstUnloadCity != null ? World.Cities.FirstOrDefault(c => c.Id == firstUnloadCity.TargetId) : null;

        if (truck.CurrentEdge == null && (truck.Status == TruckStatus.Idle || truck.Status == TruckStatus.ParkedInGarage || truck.Status == TruckStatus.WaitingForCargo))
        {
            truck.Status = TruckStatus.Idle;
            Engine.ProcessScheduledTrucks();
        }

        string firstStopName = Engine.GetStopDisplayName(stops[0]);
        return $"📌 Agenda de {stops.Count} paradas definida p/ {truck.Name} (iniciando em {firstStopName})";
    }

    public bool TryCloneTruck(Truck source, out Truck? clonedTruck, out string statusMessage, Garage? preferredGarage = null)
    {
        clonedTruck = null;
        if (!Engine.Research.IsTruckUnlocked(source.Spec, Engine.Player.Level))
        {
            statusMessage = $"Requer Nível {source.Spec.RequiredPlayerLevel} para clonar {source.Spec.Name}.";
            return false;
        }

        if (Engine.Player.Balance < source.Spec.PurchasePrice)
        {
            statusMessage = $"Saldo insuficiente para clonar {source.Spec.Name} (R$ {source.Spec.PurchasePrice:N0}).";
            return false;
        }

        if (preferredGarage != null && preferredGarage.IsFull)
        {
            statusMessage = $"A garagem {preferredGarage.Name} está cheia ({preferredGarage.StationedTrucks.Count}/{preferredGarage.MaxCapacity})!";
            return false;
        }

        if (Engine.Garages.All(g => g.IsFull))
        {
            statusMessage = "Todas as garagens estão cheias! Construa uma nova Garagem para clonar veículos.";
            return false;
        }

        clonedTruck = Engine.CloneTruck(source, preferredGarage);
        if (clonedTruck == null)
        {
            statusMessage = "Não foi possível clonar o veículo.";
            return false;
        }

        if (!clonedTruck.HasManualSchedule)
        {
            var startNode = clonedTruck.LastNode ?? Engine.Garages.First().AccessNode;
            AssignAndDispatchTruck(clonedTruck, startNode);
        }
        else
        {
            Engine.ProcessScheduledTrucks();
        }

        statusMessage = $"📋 {clonedTruck.Name} clonado de {source.Name} ({(clonedTruck.HasManualSchedule ? $"{clonedTruck.Schedule.Count} paradas" : "Modo Auto")})!";
        return true;
    }

    public bool TryStartResearch(ResearchTechId techId, out string statusMessage)
    {
        return Engine.TryStartResearch(techId, out statusMessage);
    }

    public void PauseResearch()
    {
        Engine.Research.PauseResearch();
    }

    public bool TryExploreNewRegion(out RegionUnlockInfo? info, out string statusMessage)
    {
        return Engine.TryFundRegionalExpansion(out info, out statusMessage);
    }

    public bool TryUpgradeIndustry(Industry industry, out string statusMessage)
    {
        return Engine.TryUpgradeIndustry(industry, out statusMessage);
    }

    public bool TryBuildTrafficControl(int x, int y, out string statusMessage)
    {
        return Engine.TryBuildTrafficControl(x, y, out statusMessage);
    }

    public bool TryDemolishAt(int x, int y, out DemolishResult result, out string statusMessage)
    {
        return Engine.TryDemolishAt(x, y, out result, out statusMessage);
    }

    public bool TryTerraformTile(int x, int y, out SimulationEngine.TerraformResult result, out string statusMessage)
    {
        return Engine.TryTerraformTile(x, y, out result, out statusMessage);
    }

    public bool TryModernizeTruck(Truck oldTruck, TruckSpecification newSpec, out Truck? newTruck, out string statusMessage)
    {
        bool ok = Engine.TryUpgradeTruckModel(oldTruck, newSpec, out newTruck, out statusMessage);
        if (ok && newTruck != null)
        {
            _truckLastGridPos.Remove(oldTruck.Id);
            if (!newTruck.HasManualSchedule)
            {
                var startNode = newTruck.LastNode ?? Engine.Garages.FirstOrDefault()?.AccessNode;
                if (startNode != null)
                    AssignAndDispatchTruck(newTruck, startNode);
            }
        }
        return ok;
    }

    public LogisticsLine CreateAndAssignLogisticsLine(string name, IReadOnlyList<ScheduleStop> stops, Truck? truckToBind)
    {
        bool autoPit = truckToBind?.AutoPitStopEnabled ?? true;
        var line = Engine.CreateLogisticsLine(name, stops, autoPit);
        if (truckToBind != null)
        {
            Engine.AssignTruckToLogisticsLine(truckToBind, line);
        }
        return line;
    }

    public bool UpdateLogisticsLine(int lineId, string? newName, IReadOnlyList<ScheduleStop> stops, bool? autoPitStopEnabled = null)
    {
        return Engine.UpdateLogisticsLine(lineId, newName, stops, autoPitStopEnabled);
    }

    public void AssignTruckToLogisticsLine(Truck truck, LogisticsLine line)
    {
        Engine.AssignTruckToLogisticsLine(truck, line);
    }

    public void DetachTruckFromLogisticsLine(Truck truck)
    {
        Engine.DetachTruckFromLogisticsLine(truck);
    }

    public FleetLivery GetEffectiveLivery(Truck truck) => Engine.GetEffectiveLivery(truck);

    public void UpdateCompanyIdentityAndLivery(string? companyName, FleetLivery livery)
    {
        Engine.UpdateCompanyLivery(companyName, livery);
    }

    public bool SetLineLivery(int lineId, FleetLivery? customLivery)
    {
        return Engine.UpdateLineLivery(lineId, customLivery);
    }

    public bool UpdateLineAutomation(
        int lineId,
        bool headwayEnabled,
        float minHeadwayHours,
        bool autoReplaceEnabled,
        float minCondition = 25f,
        float maxMileageKm = 0f,
        string? targetSpecName = null)
    {
        return Engine.UpdateLineAutomation(lineId, headwayEnabled, minHeadwayHours, autoReplaceEnabled, minCondition, maxMileageKm, targetSpecName);
    }

    public bool UpdateGarageAutoReplace(
        int garageId,
        bool enabled,
        float minCondition = 25f,
        float maxMileageKm = 0f,
        string? targetSpecName = null)
    {
        return Engine.UpdateGarageAutoReplace(garageId, enabled, minCondition, maxMileageKm, targetSpecName);
    }

    public TollStation? TryBuildTollStation(int x, int y, out string statusMessage)
    {
        return Engine.BuildTollStation(x, y, out statusMessage);
    }

    public bool PlacePlayerAuctionBid(int contractId, float bidFreightMultiplier, float bidDurationHours, out string statusMessage)
    {
        return Engine.PlacePlayerAuctionBid(contractId, bidFreightMultiplier, bidDurationHours, out statusMessage);
    }

    public void SaveToFile(string absolutePath)
    {
        SaveGameSerializer.SaveToFile(absolutePath, Engine, Seed);
    }

    public void LoadFromFile(string absolutePath)
    {
        var (loadedEngine, loadedSeed) = SaveGameSerializer.LoadFromFile(absolutePath);
        Engine = loadedEngine;
        Seed = loadedSeed;
        _truckLastGridPos.Clear();
        _busAutoRouteTargets.Clear();
        TimeScale = 0.0f;
    }

    public string SetTruckAutoSchedule(Truck truck)
    {
        truck.ClearSchedule();
        if (truck.LoadedCargoAmount <= 0 && truck.CurrentEdge == null)
        {
            AssignNextChainRoute(truck);
        }
        return $"🤖 {truck.Name} voltou para o despacho automático inteligente.";
    }
}
