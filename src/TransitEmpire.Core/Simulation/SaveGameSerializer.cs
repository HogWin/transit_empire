using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Generation;

namespace TransitEmpire.Core.Simulation;

/// <summary>
/// Serializador JSON completo do estado do jogo (mundo, estradas, frota, pesquisas, contratos e DRE).
/// </summary>
public static class SaveGameSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SerializeToJson(SimulationEngine engine, int seed)
    {
        var world = engine.World;
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int total = w * h;

        var terrains = new TerrainType[total];
        var biomes = new BiomeType[total];
        var roadLevels = new RoadTier[total];
        var occupantIds = new int[total];
        var oceanMask = new bool[total];

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int idx = x * h + y;
                var cell = world.Grid[x, y];
                terrains[idx] = cell.Terrain;
                biomes[idx] = cell.Biome;
                roadLevels[idx] = cell.RoadLevel;
                occupantIds[idx] = cell.OccupantId;
                oceanMask[idx] = world.OceanMask[x, y];
            }
        }

        // Mapeia caminhão -> garagem estacionada
        var truckToGarage = new Dictionary<int, int>();
        foreach (var garage in engine.Garages)
        {
            foreach (var t in garage.StationedTrucks)
                truckToGarage[t.Id] = garage.Id;
        }

        var data = new SaveGameData
        {
            Version = 1,
            Seed = seed,
            MapWidth = w,
            MapHeight = h,
            UnlockedRegionsCount = world.UnlockedRegionsCount,
            CurrentTick = engine.CurrentTick,
            CurrentWeather = engine.CurrentWeather,
            ActiveSeasonalEvent = engine.ActiveSeasonalEvent,
            SeasonalEventRemainingHours = engine.SeasonalEventRemainingHours,
            BlockedLandslideEdgeId = engine.BlockedLandslideEdgeId,
            AutoRoadMaintenanceEnabled = engine.AutoRoadMaintenanceEnabled,
            PlayerDeliveredVolume = engine.PlayerDeliveredVolume,
            PlayerContractsCompleted = engine.PlayerContractsCompleted,
            Calendar = new GameCalendarDto
            {
                Year = engine.Calendar.Year,
                Month = engine.Calendar.Month,
                Day = engine.Calendar.Day,
                Hour = engine.Calendar.Hour,
                Minute = engine.Calendar.Minute
            },
            Player = new PlayerStateDto
            {
                CompanyName = engine.Player.CompanyName,
                CompanyLivery = ToLiveryDto(engine.Player.CompanyLivery),
                Level = engine.Player.Level,
                CurrentXp = engine.Player.CurrentXp,
                Balance = engine.Player.Balance,
                LoanDebt = engine.Player.LoanDebt
            },
            CellTerrains = terrains,
            CellBiomes = biomes,
            CellRoadLevels = roadLevels,
            CellOccupantIds = occupantIds,
            CellOceanMask = oceanMask,
            RoadNodes = world.RoadNodes.Select(n => new RoadNodeDto
            {
                Id = n.Id,
                GridX = n.GridX,
                GridY = n.GridY,
                HasTrafficControl = n.HasTrafficControl
            }).ToList(),
            RoadEdges = world.RoadEdges.Select(e => new RoadEdgeDto
            {
                Id = e.Id,
                FromNodeId = e.FromNode.Id,
                ToNodeId = e.ToNode.Id,
                Tier = e.Tier,
                DistanceKm = e.DistanceKm,
                PavementCondition = e.PavementCondition,
                IsBlocked = e.IsBlocked,
                IsAlpineBiome = e.IsAlpineBiome,
                RecentRevenue30d = e.RecentRevenue30d
            }).ToList(),
            Cities = world.Cities.Select(c => new CityDto
            {
                Id = c.Id,
                Name = c.Name,
                CenterX = c.CenterX,
                CenterY = c.CenterY,
                Population = c.Population,
                ExpansionLevel = c.ExpansionLevel,
                ConsecutiveCyclesWellSupplied = c.ConsecutiveCyclesWellSupplied,
                LastSatisfactionRate = c.LastSatisfactionRate,
                TotalSubsidiesReceived = c.TotalSubsidiesReceived,
                ActiveBusServiceHours = c.ActiveBusServiceHours,
                CapacityPerResource = c.Warehouse.CapacityPerResource,
                UrbanTiles = c.UrbanTiles.Select(p => new GridPointDto { X = p.X, Y = p.Y }).ToList(),
                StoredStock = c.Warehouse.StoredStock.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                DailyDemand = c.Warehouse.DailyDemand.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            }).ToList(),
            Industries = world.Industries.Select(i => new IndustryDto
            {
                Id = i.Id,
                Name = i.Name,
                GridX = i.GridX,
                GridY = i.GridY,
                OutputResource = i.OutputResource,
                RequiredInputResource = i.RequiredInputResource,
                SecondaryInputResource = i.SecondaryInputResource,
                TertiaryInputResource = i.TertiaryInputResource,
                OutputStock = i.OutputStock,
                InputStock = i.InputStock,
                SecondaryInputStock = i.SecondaryInputStock,
                TertiaryInputStock = i.TertiaryInputStock,
                MaxStock = i.MaxStock,
                ProductionRatePerDay = i.ProductionRatePerDay,
                UpgradeLevel = i.UpgradeLevel
            }).ToList(),
            Seaports = world.Seaports.Select(s => new SeaportDto
            {
                Id = s.Id,
                Name = s.Name,
                GridX = s.GridX,
                GridY = s.GridY,
                WaterDockX = s.WaterDockX,
                WaterDockY = s.WaterDockY,
                IsShipDocked = s.IsShipDocked,
                CycleHoursRemaining = s.CycleHoursRemaining,
                CurrentShipCargo = s.CurrentShipCargo,
                MaxShipCapacity = s.MaxShipCapacity,
                TotalExportedUnits = s.TotalExportedUnits,
                TotalExportRevenue = s.TotalExportRevenue,
                PartnerSeaportId = s.PartnerSeaportId,
                OutboundCargo = s.OutboundCargo.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                TransitCargo = s.TransitCargo.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                InTransitFromSea = s.InTransitFromSea.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            }).ToList(),
            Landmarks = world.Landmarks.Select(l => new RegionalLandmarkDto
            {
                Id = l.Id,
                Name = l.Name,
                Kind = l.Kind,
                GridX = l.GridX,
                GridY = l.GridY,
                CurrentStage = l.CurrentStage,
                TotalInvestedBonusPaid = l.TotalInvestedBonusPaid,
                StageDelivered = l.StageDelivered.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            }).ToList(),
            Garages = engine.Garages.Select(g => new GarageDto
            {
                Id = g.Id,
                Name = g.Name,
                GridX = g.GridX,
                GridY = g.GridY,
                AccessNodeId = g.AccessNode.Id,
                MaxCapacity = g.MaxCapacity,
                FuelStock = g.FuelStock,
                MaxFuelCapacity = g.MaxFuelCapacity,
                AutoReplaceEnabled = g.AutoReplaceEnabled,
                AutoReplaceMinCondition = g.AutoReplaceMinCondition,
                AutoReplaceMaxMileageKm = g.AutoReplaceMaxMileageKm,
                AutoReplaceTargetSpecName = g.AutoReplaceTargetSpecName
            }).ToList(),
            LogisticsHubs = engine.LogisticsHubs.Select(l => new LogisticsHubDto
            {
                Id = l.Id,
                Name = l.Name,
                GridX = l.GridX,
                GridY = l.GridY,
                AccessNodeId = l.AccessNode.Id,
                MaxStorageCapacity = l.MaxStorageCapacity,
                FuelStock = l.FuelStock,
                MaxFuelCapacity = l.MaxFuelCapacity,
                StoredCargo = l.StoredCargo.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value)
            }).ToList(),
            BusTerminals = engine.BusTerminals.Select(bt => new BusTerminalDto
            {
                Id = bt.Id,
                Name = bt.Name,
                CityId = bt.CityId,
                GridX = bt.GridX,
                GridY = bt.GridY,
                AccessNodeId = bt.AccessNode.Id,
                WaitingPassengers = bt.WaitingPassengers,
                MaxPassengers = bt.MaxPassengers,
                TotalPassengersServed = bt.TotalPassengersServed,
                TotalTicketRevenue = bt.TotalTicketRevenue
            }).ToList(),
            LogisticsLines = engine.LogisticsLines.Select(line => new LogisticsLineDto
            {
                Id = line.Id,
                Name = line.Name,
                AutoPitStopEnabled = line.AutoPitStopEnabled,
                HeadwayControlEnabled = line.HeadwayControlEnabled,
                MinHeadwayHours = line.MinHeadwayHours,
                HoursSinceLastDispatch = line.HoursSinceLastDispatch,
                AutoReplaceEnabled = line.AutoReplaceEnabled,
                AutoReplaceMinCondition = line.AutoReplaceMinCondition,
                AutoReplaceMaxMileageKm = line.AutoReplaceMaxMileageKm,
                AutoReplaceTargetSpecName = line.AutoReplaceTargetSpecName,
                CustomLivery = line.CustomLivery != null ? ToLiveryDto(line.CustomLivery) : null,
                Stops = line.Stops.Select(ToStopDto).ToList()
            }).ToList(),
            Trucks = engine.ActiveTrucks.Select(t => new TruckDto
            {
                Id = t.Id,
                Name = t.Name,
                SpecName = t.Spec.Name,
                AssignedLineId = t.AssignedLineId,
                LoadedCargoType = t.LoadedCargoType,
                LoadedCargoAmount = t.LoadedCargoAmount,
                IsSpecialConvoyActive = t.IsSpecialConvoyActive,
                Status = t.Status,
                Condition = t.Condition,
                TotalDistanceTravelledKm = t.TotalDistanceTravelledKm,
                CurrentTripDistanceKm = t.CurrentTripDistanceKm,
                HasManualSchedule = t.HasManualSchedule,
                CurrentScheduleIndex = t.CurrentScheduleIndex,
                AutoPitStopEnabled = t.AutoPitStopEnabled,
                IsDivertedForPitStop = t.IsDivertedForPitStop,
                PitStopTargetKind = t.PitStopTargetKind,
                PitStopTargetId = t.PitStopTargetId,
                LoadedFromHub = t.LoadedFromHub,
                CurrentStopWaitHours = t.CurrentStopWaitHours,
                IsHeldForHeadway = t.IsHeldForHeadway,
                LastNodeId = t.LastNode?.Id,
                Schedule = t.Schedule.Select(ToStopDto).ToList(),
                AssignedOriginIndustryId = t.AssignedOriginIndustry?.Id,
                AssignedTargetIndustryId = t.AssignedTargetIndustry?.Id,
                AssignedTargetCityId = t.AssignedTargetCity?.Id,
                CurrentEdgeId = t.CurrentEdge?.Id,
                DistanceAlongCurrentEdgeKm = t.DistanceAlongCurrentEdgeKm,
                RemainingEdgeIds = t.RemainingPath.Select(e => e.Id).ToList(),
                StationedGarageId = truckToGarage.TryGetValue(t.Id, out int gid) ? gid : null
            }).ToList(),
            Contracts = engine.ActiveContracts.Select(ct => new ContractDto
            {
                Id = ct.Id,
                Title = ct.Title,
                TargetCityId = ct.TargetCity.Id,
                Resource = ct.Resource,
                RequiredAmount = ct.RequiredAmount,
                DeliveredAmount = ct.DeliveredAmount,
                BonusRewardMoney = ct.BonusRewardMoney,
                BonusRewardXp = ct.BonusRewardXp,
                RemainingHours = ct.RemainingHours,
                ClaimedByRivalName = ct.ClaimedByRivalName,
                IsSpecialConvoy = ct.IsSpecialConvoy,
                FreightMultiplier = ct.FreightMultiplier
            }).ToList(),
            TollStations = engine.TollStations.Select(ts => new TollStationDto
            {
                Id = ts.Id,
                Name = ts.Name,
                GridX = ts.GridX,
                GridY = ts.GridY,
                AccessNodeId = ts.AccessNode.Id,
                TollFee = ts.TollFee,
                TotalTollCollected = ts.TotalTollCollected,
                TrucksInspected = ts.TrucksInspected,
                LastWeighDisplay = ts.LastWeighDisplay
            }).ToList(),
            Rivals = engine.Rivals.Select(rv => new RivalCompanyDto
            {
                Id = rv.Id,
                Name = rv.Name,
                ColorHex = rv.ColorHex,
                DeliveredVolume = rv.DeliveredVolume,
                CompletedContracts = rv.CompletedContracts,
                EstimatedRevenue = rv.EstimatedRevenue
            }).ToList(),
            Research = new ResearchSaveDto
            {
                ActiveTechId = engine.Research.ActiveResearch?.Id,
                Nodes = engine.Research.Nodes.Select(n => new ResearchNodeSaveDto
                {
                    Id = n.Id,
                    ProgressHours = n.ProgressHours,
                    IsFunded = n.IsFunded
                }).ToList()
            },
            Financials = new FinancialLedgerDto
            {
                CurrentMonth = engine.Financials.CurrentMonth,
                Lifetime = engine.Financials.Lifetime,
                MonthlyHistory = engine.Financials.MonthlyHistory
            },
            UnlockedMilestones = engine.Milestones.UnlockedIds.ToList(),
            RetiredFleetDistanceKm = engine.Milestones.RetiredFleetDistanceKm
        };

        return JsonSerializer.Serialize(data, JsonOptions);
    }

    public static (SimulationEngine Engine, int Seed) DeserializeFromJson(string json)
    {
        var data = JsonSerializer.Deserialize<SaveGameData>(json, JsonOptions)
            ?? throw new InvalidDataException("Arquivo de save inválido ou vazio.");

        var world = new GeneratedWorld(data.MapWidth, data.MapHeight)
        {
            UnlockedRegionsCount = Math.Max(1, data.UnlockedRegionsCount)
        };

        for (int x = 0; x < data.MapWidth; x++)
        {
            for (int y = 0; y < data.MapHeight; y++)
            {
                int idx = x * data.MapHeight + y;
                world.Grid[x, y] = new GridCell(x, y, data.CellTerrains[idx], data.CellBiomes[idx])
                {
                    RoadLevel = data.CellRoadLevels[idx],
                    OccupantId = data.CellOccupantIds[idx]
                };
                if (data.CellOceanMask != null && idx < data.CellOceanMask.Length)
                    world.OceanMask[x, y] = data.CellOceanMask[idx];
            }
        }
        WorldGenerator.LabelContinents(world);

        var nodeMap = new Dictionary<int, RoadNode>();
        foreach (var nd in data.RoadNodes)
        {
            var node = new RoadNode(nd.Id, nd.GridX, nd.GridY)
            {
                HasTrafficControl = nd.HasTrafficControl
            };
            world.RoadNodes.Add(node);
            nodeMap[node.Id] = node;
        }

        var edgeMap = new Dictionary<int, RoadEdge>();
        foreach (var ed in data.RoadEdges)
        {
            if (nodeMap.TryGetValue(ed.FromNodeId, out var from) && nodeMap.TryGetValue(ed.ToNodeId, out var to))
            {
                var edge = new RoadEdge(ed.Id, from, to, ed.Tier, ed.DistanceKm)
                {
                    PavementCondition = ed.PavementCondition ?? 100f,
                    IsBlocked = ed.IsBlocked,
                    IsAlpineBiome = ed.IsAlpineBiome,
                    RecentRevenue30d = ed.RecentRevenue30d
                };
                from.OutgoingEdges.Add(edge);
                world.RoadEdges.Add(edge);
                edgeMap[edge.Id] = edge;
            }
        }

        var cityMap = new Dictionary<int, City>();
        foreach (var cd in data.Cities)
        {
            var city = new City(cd.Id, cd.Name, cd.CenterX, cd.CenterY)
            {
                Population = cd.Population,
                ExpansionLevel = cd.ExpansionLevel,
                ConsecutiveCyclesWellSupplied = cd.ConsecutiveCyclesWellSupplied,
                LastSatisfactionRate = cd.LastSatisfactionRate ?? 1.0f,
                TotalSubsidiesReceived = cd.TotalSubsidiesReceived,
                ActiveBusServiceHours = cd.ActiveBusServiceHours
            };
            city.UrbanTiles.Clear();
            foreach (var pt in cd.UrbanTiles)
                city.UrbanTiles.Add((pt.X, pt.Y));

            city.Warehouse.CapacityPerResource = cd.CapacityPerResource;
            city.Warehouse.StoredStock.Clear();
            foreach (var kv in cd.StoredStock)
            {
                if (Enum.TryParse<ResourceType>(kv.Key, out var rt))
                    city.Warehouse.StoredStock[rt] = kv.Value;
            }
            city.Warehouse.DailyDemand.Clear();
            foreach (var kv in cd.DailyDemand)
            {
                if (Enum.TryParse<ResourceType>(kv.Key, out var rt))
                    city.Warehouse.DailyDemand[rt] = kv.Value;
            }

            world.Cities.Add(city);
            cityMap[city.Id] = city;
        }

        var indMap = new Dictionary<int, Industry>();
        foreach (var idto in data.Industries)
        {
            var ind = new Industry(idto.Id, idto.Name, idto.GridX, idto.GridY, idto.OutputResource, idto.RequiredInputResource, idto.ProductionRatePerDay, idto.SecondaryInputResource, idto.TertiaryInputResource)
            {
                OutputStock = idto.OutputStock,
                InputStock = idto.InputStock,
                SecondaryInputStock = idto.SecondaryInputStock,
                TertiaryInputStock = idto.TertiaryInputStock,
                MaxStock = idto.MaxStock > 0 ? idto.MaxStock : 300,
                UpgradeLevel = Math.Max(1, idto.UpgradeLevel)
            };
            world.Industries.Add(ind);
            indMap[ind.Id] = ind;
        }

        if (data.Seaports != null)
        {
            foreach (var sd in data.Seaports)
            {
                var seaport = new SeaportTerminal(sd.Id, sd.Name, sd.GridX, sd.GridY, sd.WaterDockX, sd.WaterDockY, sd.PartnerSeaportId);
                seaport.RestoreState(
                    sd.IsShipDocked,
                    sd.CycleHoursRemaining,
                    sd.CurrentShipCargo,
                    sd.MaxShipCapacity,
                    sd.TotalExportedUnits,
                    sd.TotalExportRevenue,
                    sd.PartnerSeaportId,
                    sd.OutboundCargo,
                    sd.TransitCargo,
                    sd.InTransitFromSea);
                world.Seaports.Add(seaport);
            }
        }

        if (data.Landmarks != null)
        {
            foreach (var ld in data.Landmarks)
            {
                var landmark = new RegionalLandmark(ld.Id, ld.Name, ld.Kind, ld.GridX, ld.GridY);
                landmark.RestoreState(ld.CurrentStage, ld.StageDelivered ?? new Dictionary<string, int>(), ld.TotalInvestedBonusPaid);
                world.Landmarks.Add(landmark);
            }
        }

        var engine = new SimulationEngine(world, generateInitialContracts: false);
        engine.Calendar.Year = data.Calendar.Year;
        engine.Calendar.Month = data.Calendar.Month;
        engine.Calendar.Day = data.Calendar.Day;
        engine.Calendar.Hour = data.Calendar.Hour;
        engine.Calendar.Minute = data.Calendar.Minute;

        engine.Player.CompanyName = string.IsNullOrWhiteSpace(data.Player.CompanyName)
            ? "Transit Empire Logística"
            : data.Player.CompanyName;
        engine.Player.CompanyLivery = FromLiveryDto(data.Player.CompanyLivery) ?? new FleetLivery();
        engine.Player.Level = data.Player.Level;
        engine.Player.CurrentXp = data.Player.CurrentXp;
        engine.Player.Balance = data.Player.Balance;
        engine.Player.LoanDebt = data.Player.LoanDebt;

        var garageMap = new Dictionary<int, Garage>();
        foreach (var gd in data.Garages)
        {
            var accessNode = nodeMap.TryGetValue(gd.AccessNodeId, out var an) ? an : world.RoadNodes.First();
            var garage = new Garage(gd.Id, gd.Name, gd.GridX, gd.GridY, accessNode, gd.MaxCapacity)
            {
                FuelStock = gd.FuelStock,
                MaxFuelCapacity = gd.MaxFuelCapacity > 0 ? gd.MaxFuelCapacity : 300,
                AutoReplaceEnabled = gd.AutoReplaceEnabled,
                AutoReplaceMinCondition = gd.AutoReplaceMinCondition > 0f ? gd.AutoReplaceMinCondition : 25f,
                AutoReplaceMaxMileageKm = gd.AutoReplaceMaxMileageKm,
                AutoReplaceTargetSpecName = gd.AutoReplaceTargetSpecName
            };
            engine.Garages.Add(garage);
            garageMap[garage.Id] = garage;
        }

        foreach (var hd in data.LogisticsHubs)
        {
            var accessNode = nodeMap.TryGetValue(hd.AccessNodeId, out var an) ? an : world.RoadNodes.First();
            var hub = new LogisticsHub(hd.Id, hd.Name, hd.GridX, hd.GridY, accessNode)
            {
                MaxStorageCapacity = hd.MaxStorageCapacity,
                FuelStock = hd.FuelStock,
                MaxFuelCapacity = hd.MaxFuelCapacity > 0 ? hd.MaxFuelCapacity : 500
            };
            foreach (var kv in hd.StoredCargo)
            {
                if (Enum.TryParse<ResourceType>(kv.Key, out var rt))
                    hub.StoredCargo[rt] = kv.Value;
            }
            engine.LogisticsHubs.Add(hub);
        }

        if (data.BusTerminals != null)
        {
            foreach (var btd in data.BusTerminals)
            {
                var accessNode = nodeMap.TryGetValue(btd.AccessNodeId, out var an) ? an : world.RoadNodes.First();
                engine.BusTerminals.Add(new BusTerminal(btd.Id, btd.Name, btd.CityId, btd.GridX, btd.GridY, accessNode)
                {
                    WaitingPassengers = btd.WaitingPassengers,
                    MaxPassengers = btd.MaxPassengers > 0 ? btd.MaxPassengers : 140,
                    TotalPassengersServed = btd.TotalPassengersServed,
                    TotalTicketRevenue = btd.TotalTicketRevenue
                });
            }
        }

        if (data.LogisticsLines != null)
        {
            foreach (var ld in data.LogisticsLines)
            {
                var line = new LogisticsLine(ld.Id, ld.Name, ld.AutoPitStopEnabled)
                {
                    HeadwayControlEnabled = ld.HeadwayControlEnabled,
                    MinHeadwayHours = ld.MinHeadwayHours > 0f ? ld.MinHeadwayHours : 4.0f,
                    HoursSinceLastDispatch = ld.HoursSinceLastDispatch,
                    AutoReplaceEnabled = ld.AutoReplaceEnabled,
                    AutoReplaceMinCondition = ld.AutoReplaceMinCondition > 0f ? ld.AutoReplaceMinCondition : 25f,
                    AutoReplaceMaxMileageKm = ld.AutoReplaceMaxMileageKm,
                    AutoReplaceTargetSpecName = ld.AutoReplaceTargetSpecName,
                    CustomLivery = FromLiveryDto(ld.CustomLivery)
                };
                if (ld.Stops != null && ld.Stops.Count > 0)
                {
                    line.SetStops(ld.Stops.Select(FromStopDto));
                }
                engine.LogisticsLines.Add(line);
            }
        }

        foreach (var td in data.Trucks)
        {
            var spec = ProgressionManager.ResolveSpecByName(td.SpecName);
            var truck = new Truck(td.Id, td.Name, spec)
            {
                AssignedLineId = td.AssignedLineId,
                LoadedCargoType = td.LoadedCargoType,
                LoadedCargoAmount = td.LoadedCargoAmount,
                IsSpecialConvoyActive = td.IsSpecialConvoyActive,
                Condition = td.Condition,
                HasManualSchedule = td.HasManualSchedule,
                CurrentScheduleIndex = td.CurrentScheduleIndex,
                AutoPitStopEnabled = td.AutoPitStopEnabled,
                IsDivertedForPitStop = td.IsDivertedForPitStop,
                PitStopTargetKind = td.PitStopTargetKind,
                PitStopTargetId = td.PitStopTargetId,
                LoadedFromHub = td.LoadedFromHub,
                CurrentStopWaitHours = td.CurrentStopWaitHours,
                IsHeldForHeadway = td.IsHeldForHeadway,
                LastNode = td.LastNodeId.HasValue && nodeMap.TryGetValue(td.LastNodeId.Value, out var ln) ? ln : null,
                AssignedOriginIndustry = td.AssignedOriginIndustryId.HasValue && indMap.TryGetValue(td.AssignedOriginIndustryId.Value, out var origInd) ? origInd : null,
                AssignedTargetIndustry = td.AssignedTargetIndustryId.HasValue && indMap.TryGetValue(td.AssignedTargetIndustryId.Value, out var tgtInd) ? tgtInd : null,
                AssignedTargetCity = td.AssignedTargetCityId.HasValue && cityMap.TryGetValue(td.AssignedTargetCityId.Value, out var tgtCity) ? tgtCity : null
            };
            if (td.Schedule != null && td.Schedule.Count > 0)
            {
                foreach (var sd in td.Schedule)
                {
                    truck.Schedule.Add(FromStopDto(sd));
                }
                truck.CurrentScheduleIndex = Math.Clamp(td.CurrentScheduleIndex, 0, truck.Schedule.Count - 1);
            }

            if (td.StationedGarageId.HasValue && garageMap.TryGetValue(td.StationedGarageId.Value, out var stGarage))
                stGarage.StationedTrucks.Add(truck);

            truck.Status = td.Status;

            RoadEdge? currEdge = td.CurrentEdgeId.HasValue && edgeMap.TryGetValue(td.CurrentEdgeId.Value, out var ce) ? ce : null;
            var remPath = new List<RoadEdge>();
            foreach (int eid in td.RemainingEdgeIds)
            {
                if (edgeMap.TryGetValue(eid, out var re))
                    remPath.Add(re);
            }
            truck.RestoreNavigationState(td.TotalDistanceTravelledKm, td.CurrentTripDistanceKm, currEdge, td.DistanceAlongCurrentEdgeKm, remPath);
            engine.ActiveTrucks.Add(truck);
        }

        foreach (var cd in data.Contracts)
        {
            if (cityMap.TryGetValue(cd.TargetCityId, out var c))
            {
                float convoyMult = cd.FreightMultiplier > 0f ? cd.FreightMultiplier : 1.0f;
                var contract = new LogisticsContract(cd.Id, cd.Title, c, cd.Resource, cd.RequiredAmount, cd.BonusRewardMoney, cd.BonusRewardXp, 240f, cd.IsSpecialConvoy, convoyMult);
                contract.RestoreProgress(cd.DeliveredAmount, cd.RemainingHours, cd.ClaimedByRivalName);
                engine.ActiveContracts.Add(contract);
            }
        }

        foreach (var td in data.TollStations)
        {
            var accessNode = nodeMap.TryGetValue(td.AccessNodeId, out var an) ? an : world.RoadNodes.FirstOrDefault();
            if (accessNode == null) continue;
            engine.TollStations.Add(new TollStation(td.Id, td.Name, td.GridX, td.GridY, accessNode)
            {
                TollFee = td.TollFee > 0 ? td.TollFee : TollStation.DefaultTollFee,
                TotalTollCollected = td.TotalTollCollected,
                TrucksInspected = td.TrucksInspected,
                LastWeighDisplay = string.IsNullOrWhiteSpace(td.LastWeighDisplay) ? "LIVRE • BALANÇA OK" : td.LastWeighDisplay
            });
        }

        if (data.Rivals != null && data.Rivals.Count > 0)
        {
            foreach (var rd in data.Rivals)
            {
                engine.Rivals.Add(new RivalCompany(rd.Id, rd.Name, rd.ColorHex)
                {
                    DeliveredVolume = rd.DeliveredVolume,
                    CompletedContracts = rd.CompletedContracts,
                    EstimatedRevenue = rd.EstimatedRevenue
                });
            }
        }

        if (data.Research != null)
        {
            engine.Research.RestoreState(
                data.Research.ActiveTechId,
                data.Research.Nodes.Select(n => (n.Id, n.ProgressHours, n.IsFunded)));
        }

        if (data.Financials != null)
        {
            engine.Financials.RestoreState(data.Financials.CurrentMonth, data.Financials.Lifetime, data.Financials.MonthlyHistory);
        }

        engine.Milestones.RestoreState(data.UnlockedMilestones, data.RetiredFleetDistanceKm);

        engine.RestoreSimulationState(
            data.CurrentTick,
            data.CurrentWeather,
            data.ActiveSeasonalEvent,
            data.SeasonalEventRemainingHours,
            data.BlockedLandslideEdgeId,
            data.AutoRoadMaintenanceEnabled ?? true,
            data.PlayerDeliveredVolume,
            data.PlayerContractsCompleted);
        return (engine, data.Seed);
    }

    public static void SaveToFile(string filePath, SimulationEngine engine, int seed)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string json = SerializeToJson(engine, seed);
        File.WriteAllText(filePath, json);
    }

    public static (SimulationEngine Engine, int Seed) LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Nenhum arquivo de save encontrado.", filePath);

        string json = File.ReadAllText(filePath);
        return DeserializeFromJson(json);
    }

    public static SaveSlotSummary ReadSaveSummary(string filePath, string slotLabel)
    {
        if (!File.Exists(filePath))
            return new SaveSlotSummary(slotLabel, filePath, Exists: false, Seed: 0, CalendarText: "-", Balance: 0, TruckCount: 0, CityCount: 0, SavedAtText: "Vazio");

        try
        {
            string json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<SaveGameData>(json, JsonOptions);
            if (data == null)
                return new SaveSlotSummary(slotLabel, filePath, Exists: false, Seed: 0, CalendarText: "-", Balance: 0, TruckCount: 0, CityCount: 0, SavedAtText: "Inválido");

            string cal = $"{data.Calendar.Day:D2}/{data.Calendar.Month:D2}/{data.Calendar.Year} {data.Calendar.Hour:D2}:{data.Calendar.Minute:D2}";
            string ts = File.GetLastWriteTime(filePath).ToString("dd/MM HH:mm");
            return new SaveSlotSummary(
                slotLabel,
                filePath,
                Exists: true,
                Seed: data.Seed,
                CalendarText: cal,
                Balance: data.Player.Balance,
                TruckCount: data.Trucks?.Count ?? 0,
                CityCount: data.Cities?.Count ?? 0,
                SavedAtText: ts);
        }
        catch
        {
            return new SaveSlotSummary(slotLabel, filePath, Exists: false, Seed: 0, CalendarText: "-", Balance: 0, TruckCount: 0, CityCount: 0, SavedAtText: "Erro ao ler");
        }
    }

    public sealed record SaveSlotSummary(
        string SlotLabel,
        string FilePath,
        bool Exists,
        int Seed,
        string CalendarText,
        long Balance,
        int TruckCount,
        int CityCount,
        string SavedAtText);

    public sealed class SaveGameData
    {
        public int Version { get; set; } = 1;
        public int Seed { get; set; }
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        public int UnlockedRegionsCount { get; set; }
        public int CurrentTick { get; set; }
        public WeatherCondition CurrentWeather { get; set; }
        public SeasonalEventType ActiveSeasonalEvent { get; set; }
        public float SeasonalEventRemainingHours { get; set; }
        public int BlockedLandslideEdgeId { get; set; }
        public bool? AutoRoadMaintenanceEnabled { get; set; }
        public int PlayerDeliveredVolume { get; set; }
        public int PlayerContractsCompleted { get; set; }
        public GameCalendarDto Calendar { get; set; } = new();
        public PlayerStateDto Player { get; set; } = new();
        public TerrainType[] CellTerrains { get; set; } = Array.Empty<TerrainType>();
        public BiomeType[] CellBiomes { get; set; } = Array.Empty<BiomeType>();
        public RoadTier[] CellRoadLevels { get; set; } = Array.Empty<RoadTier>();
        public int[] CellOccupantIds { get; set; } = Array.Empty<int>();
        public bool[]? CellOceanMask { get; set; }
        public List<RoadNodeDto> RoadNodes { get; set; } = new();
        public List<RoadEdgeDto> RoadEdges { get; set; } = new();
        public List<CityDto> Cities { get; set; } = new();
        public List<IndustryDto> Industries { get; set; } = new();
        public List<GarageDto> Garages { get; set; } = new();
        public List<LogisticsHubDto> LogisticsHubs { get; set; } = new();
        public List<BusTerminalDto>? BusTerminals { get; set; } = new();
        public List<LogisticsLineDto> LogisticsLines { get; set; } = new();
        public List<TruckDto> Trucks { get; set; } = new();
        public List<ContractDto> Contracts { get; set; } = new();
        public List<TollStationDto> TollStations { get; set; } = new();
        public List<SeaportDto>? Seaports { get; set; } = new();
        public List<RegionalLandmarkDto>? Landmarks { get; set; } = new();
        public List<RivalCompanyDto> Rivals { get; set; } = new();
        public ResearchSaveDto? Research { get; set; }
        public FinancialLedgerDto? Financials { get; set; }
        public List<string>? UnlockedMilestones { get; set; } = new();
        public float RetiredFleetDistanceKm { get; set; }
    }

    public sealed class GameCalendarDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }

    private static FleetLiveryDto ToLiveryDto(FleetLivery livery) => new()
    {
        IsCustomized = livery.IsCustomized,
        PrimaryColorHex = livery.PrimaryColorHex,
        StripeColorHex = livery.StripeColorHex,
        Finish = livery.Finish
    };

    private static FleetLivery? FromLiveryDto(FleetLiveryDto? dto)
    {
        if (dto == null) return null;
        return new FleetLivery
        {
            IsCustomized = dto.IsCustomized,
            PrimaryColorHex = string.IsNullOrWhiteSpace(dto.PrimaryColorHex) ? "#1d4ed8" : dto.PrimaryColorHex,
            StripeColorHex = string.IsNullOrWhiteSpace(dto.StripeColorHex) ? "#facc15" : dto.StripeColorHex,
            Finish = dto.Finish
        };
    }

    private static ScheduleStopDto ToStopDto(ScheduleStop stop) => new()
    {
        Kind = stop.Kind,
        TargetId = stop.TargetId,
        Action = stop.Action,
        Condition = stop.Condition,
        CargoFilter = stop.CargoFilter,
        MaxWaitHours = stop.MaxWaitHours
    };

    private static ScheduleStop FromStopDto(ScheduleStopDto dto) =>
        new(dto.Kind, dto.TargetId, dto.Action, dto.Condition, dto.CargoFilter, dto.MaxWaitHours);

    public sealed class PlayerStateDto
    {
        public string? CompanyName { get; set; }
        public FleetLiveryDto? CompanyLivery { get; set; }
        public int Level { get; set; }
        public int CurrentXp { get; set; }
        public long Balance { get; set; }
        public long LoanDebt { get; set; }
    }

    public sealed class FleetLiveryDto
    {
        public bool IsCustomized { get; set; }
        public string PrimaryColorHex { get; set; } = "#1d4ed8";
        public string StripeColorHex { get; set; } = "#facc15";
        public PaintFinish Finish { get; set; } = PaintFinish.Metallic;
    }

    public sealed class RoadNodeDto
    {
        public int Id { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public bool HasTrafficControl { get; set; }
    }

    public sealed class RoadEdgeDto
    {
        public int Id { get; set; }
        public int FromNodeId { get; set; }
        public int ToNodeId { get; set; }
        public RoadTier Tier { get; set; }
        public float DistanceKm { get; set; }
        public float? PavementCondition { get; set; }
        public bool IsBlocked { get; set; }
        public bool IsAlpineBiome { get; set; }
        public long RecentRevenue30d { get; set; }
    }

    public sealed class GridPointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class CityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int Population { get; set; }
        public int ExpansionLevel { get; set; }
        public int ConsecutiveCyclesWellSupplied { get; set; }
        public float? LastSatisfactionRate { get; set; }
        public long TotalSubsidiesReceived { get; set; }
        public int ActiveBusServiceHours { get; set; }
        public int CapacityPerResource { get; set; }
        public List<GridPointDto> UrbanTiles { get; set; } = new();
        public Dictionary<string, int> StoredStock { get; set; } = new();
        public Dictionary<string, int> DailyDemand { get; set; } = new();
    }

    public sealed class IndustryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GridX { get; set; }
        public int GridY { get; set; }
        public ResourceType OutputResource { get; set; }
        public ResourceType RequiredInputResource { get; set; }
        public ResourceType SecondaryInputResource { get; set; }
        public ResourceType TertiaryInputResource { get; set; }
        public int OutputStock { get; set; }
        public int InputStock { get; set; }
        public int SecondaryInputStock { get; set; }
        public int TertiaryInputStock { get; set; }
        public int MaxStock { get; set; }
        public int ProductionRatePerDay { get; set; }
        public int UpgradeLevel { get; set; } = 1;
    }

    public sealed class SeaportDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int WaterDockX { get; set; }
        public int WaterDockY { get; set; }
        public bool IsShipDocked { get; set; } = true;
        public float CycleHoursRemaining { get; set; } = SeaportTerminal.DockedDurationHours;
        public int CurrentShipCargo { get; set; }
        public int MaxShipCapacity { get; set; } = 320;
        public int TotalExportedUnits { get; set; }
        public long TotalExportRevenue { get; set; }
        public int? PartnerSeaportId { get; set; }
        public Dictionary<string, int>? OutboundCargo { get; set; }
        public Dictionary<string, int>? TransitCargo { get; set; }
        public Dictionary<string, int>? InTransitFromSea { get; set; }
    }

    public sealed class RegionalLandmarkDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public LandmarkKind Kind { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int CurrentStage { get; set; } = 1;
        public long TotalInvestedBonusPaid { get; set; }
        public Dictionary<string, int>? StageDelivered { get; set; } = new();
    }

    public sealed class GarageDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int AccessNodeId { get; set; }
        public int MaxCapacity { get; set; }
        public int FuelStock { get; set; }
        public int MaxFuelCapacity { get; set; } = 300;
        public bool AutoReplaceEnabled { get; set; }
        public float AutoReplaceMinCondition { get; set; } = 25f;
        public float AutoReplaceMaxMileageKm { get; set; }
        public string? AutoReplaceTargetSpecName { get; set; }
    }

    public sealed class LogisticsHubDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int AccessNodeId { get; set; }
        public int MaxStorageCapacity { get; set; }
        public int FuelStock { get; set; }
        public int MaxFuelCapacity { get; set; } = 500;
        public Dictionary<string, int> StoredCargo { get; set; } = new();
    }

    public sealed class BusTerminalDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CityId { get; set; }
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int AccessNodeId { get; set; }
        public int WaitingPassengers { get; set; }
        public int MaxPassengers { get; set; } = 140;
        public int TotalPassengersServed { get; set; }
        public long TotalTicketRevenue { get; set; }
    }

    public sealed class LogisticsLineDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool AutoPitStopEnabled { get; set; } = true;
        public bool HeadwayControlEnabled { get; set; }
        public float MinHeadwayHours { get; set; } = 4.0f;
        public float HoursSinceLastDispatch { get; set; } = 999f;
        public bool AutoReplaceEnabled { get; set; }
        public float AutoReplaceMinCondition { get; set; } = 25f;
        public float AutoReplaceMaxMileageKm { get; set; }
        public string? AutoReplaceTargetSpecName { get; set; }
        public FleetLiveryDto? CustomLivery { get; set; }
        public List<ScheduleStopDto> Stops { get; set; } = new();
    }

    public sealed class TruckDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SpecName { get; set; } = string.Empty;
        public int? AssignedLineId { get; set; }
        public ResourceType LoadedCargoType { get; set; }
        public int LoadedCargoAmount { get; set; }
        public bool IsSpecialConvoyActive { get; set; }
        public TruckStatus Status { get; set; }
        public float Condition { get; set; }
        public float TotalDistanceTravelledKm { get; set; }
        public float CurrentTripDistanceKm { get; set; }
        public bool HasManualSchedule { get; set; }
        public int CurrentScheduleIndex { get; set; }
        public bool AutoPitStopEnabled { get; set; } = true;
        public bool IsDivertedForPitStop { get; set; }
        public ScheduleStopKind? PitStopTargetKind { get; set; }
        public int? PitStopTargetId { get; set; }
        public bool LoadedFromHub { get; set; }
        public float CurrentStopWaitHours { get; set; }
        public bool IsHeldForHeadway { get; set; }
        public int? LastNodeId { get; set; }
        public List<ScheduleStopDto> Schedule { get; set; } = new();
        public int? AssignedOriginIndustryId { get; set; }
        public int? AssignedTargetIndustryId { get; set; }
        public int? AssignedTargetCityId { get; set; }
        public int? CurrentEdgeId { get; set; }
        public float DistanceAlongCurrentEdgeKm { get; set; }
        public List<int> RemainingEdgeIds { get; set; } = new();
        public int? StationedGarageId { get; set; }
    }

    public sealed class ScheduleStopDto
    {
        public ScheduleStopKind Kind { get; set; }
        public int TargetId { get; set; }
        public ScheduleAction Action { get; set; }
        public DispatchCondition Condition { get; set; } = DispatchCondition.Immediate;
        public ResourceType CargoFilter { get; set; } = ResourceType.None;
        public float MaxWaitHours { get; set; }
    }

    public sealed class ContractDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TargetCityId { get; set; }
        public ResourceType Resource { get; set; }
        public int RequiredAmount { get; set; }
        public int DeliveredAmount { get; set; }
        public long BonusRewardMoney { get; set; }
        public int BonusRewardXp { get; set; }
        public float RemainingHours { get; set; }
        public string? ClaimedByRivalName { get; set; }
        public bool IsSpecialConvoy { get; set; }
        public float FreightMultiplier { get; set; } = 1.0f;
    }

    public sealed class TollStationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int GridX { get; set; }
        public int GridY { get; set; }
        public int AccessNodeId { get; set; }
        public long TollFee { get; set; }
        public long TotalTollCollected { get; set; }
        public int TrucksInspected { get; set; }
        public string? LastWeighDisplay { get; set; }
    }

    public sealed class RivalCompanyDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ColorHex { get; set; } = string.Empty;
        public int DeliveredVolume { get; set; }
        public int CompletedContracts { get; set; }
        public long EstimatedRevenue { get; set; }
    }

    public sealed class ResearchSaveDto
    {
        public ResearchTechId? ActiveTechId { get; set; }
        public List<ResearchNodeSaveDto> Nodes { get; set; } = new();
    }

    public sealed class ResearchNodeSaveDto
    {
        public ResearchTechId Id { get; set; }
        public float ProgressHours { get; set; }
        public bool IsFunded { get; set; }
    }

    public sealed class FinancialLedgerDto
    {
        public FinancialStatement CurrentMonth { get; set; } = new();
        public FinancialStatement Lifetime { get; set; } = new();
        public List<FinancialStatement> MonthlyHistory { get; set; } = new();
    }
}
