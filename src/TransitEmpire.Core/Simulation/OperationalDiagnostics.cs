using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Simulation;

public enum BottleneckKind
{
    BlockedRoad,
    DamagedRoad,
    CongestedCorridor,
    StalledIndustry,
    QueuedTruck,
    UnsuppliedCity
}

public enum BottleneckSeverity
{
    Warning,
    Critical
}

public sealed record OperationalBottleneck(
    BottleneckKind Kind,
    BottleneckSeverity Severity,
    string Title,
    string Description,
    int GridX,
    int GridY,
    string Suggestion);

/// <summary>
/// Assistente de diagnóstico operacional em tempo real: detecta gargalos viários,
/// fábricas paradas por falta de insumo, filas de caminhões e cidades desabastecidas.
/// </summary>
public static class OperationalDiagnostics
{
    public static List<OperationalBottleneck> Analyze(SimulationEngine engine)
    {
        var results = new List<OperationalBottleneck>();
        var seenCorridors = new HashSet<(int A, int B)>();

        // 1. Vias bloqueadas, trechos esburacados e corredores congestionados (deduplicados por par de nós)
        foreach (var edge in engine.World.RoadEdges)
        {
            int a = Math.Min(edge.FromNode.Id, edge.ToNode.Id);
            int b = Math.Max(edge.FromNode.Id, edge.ToNode.Id);
            if (!seenCorridors.Add((a, b)))
                continue;

            int midX = (edge.FromNode.GridX + edge.ToNode.GridX) / 2;
            int midY = (edge.FromNode.GridY + edge.ToNode.GridY) / 2;

            // Verifica desgaste e congestionamento somando os dois sentidos do corredor quando houver via dupla lógica
            RoadEdge? reverse = null;
            var toOutgoing = edge.ToNode.OutgoingEdges;
            for (int i = 0; i < toOutgoing.Count; i++)
            {
                if (toOutgoing[i].ToNode.Id == edge.FromNode.Id)
                {
                    reverse = toOutgoing[i];
                    break;
                }
            }
            bool blocked = edge.IsBlocked || (reverse?.IsBlocked ?? false);
            float minPavement = Math.Min(edge.PavementCondition, reverse?.PavementCondition ?? edge.PavementCondition);
            int totalTraffic = edge.ActiveTrucksCount + (reverse?.ActiveTrucksCount ?? 0);

            if (blocked)
            {
                results.Add(new OperationalBottleneck(
                    BottleneckKind.BlockedRoad,
                    BottleneckSeverity.Critical,
                    $"🚧 Trecho bloqueado ({midX}, {midY})",
                    "Via interrompida por deslizamento ou bloqueio de pista.",
                    midX,
                    midY,
                    "Inspecione o trecho e clique em Desobstruir & Recapear."));
                continue;
            }

            if (minPavement < 55f)
            {
                var sev = minPavement < 38f ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                results.Add(new OperationalBottleneck(
                    BottleneckKind.DamagedRoad,
                    sev,
                    $"🕳️ Asfalto deteriorado ({minPavement:F0}%)",
                    $"Trecho em ({midX}, {midY}) reduz a velocidade da frota por buracos.",
                    midX,
                    midY,
                    "Recapeie o segmento ou ative a Conservação Auto no Banco."));
            }

            int congestionThreshold = edge.Tier >= RoadTier.Highway ? 5 : 3;
            if (totalTraffic >= congestionThreshold)
            {
                bool hasCtrl = edge.FromNode.HasTrafficControl || edge.ToNode.HasTrafficControl;
                var sev = totalTraffic >= congestionThreshold + 2 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                results.Add(new OperationalBottleneck(
                    BottleneckKind.CongestedCorridor,
                    sev,
                    $"🚦 Corredor congestionado ({totalTraffic} veículos)",
                    $"Tráfego intenso em {edge.Tier} próximo a ({midX}, {midY}).",
                    midX,
                    midY,
                    hasCtrl
                        ? "Evolua para Rodovia / Pista Dupla para elevar a vazão."
                        : "Instale Semáforo Inteligente ou amplie a via."));
            }
        }

        // 2. Indústrias processadoras paradas por falta de matéria-prima
        foreach (var ind in engine.World.Industries)
        {
            if (ind.RequiredInputResource == ResourceType.None)
                continue;

            if (ind.InputStock < 5)
            {
                var sev = ind.OutputStock == 0 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                results.Add(new OperationalBottleneck(
                    BottleneckKind.StalledIndustry,
                    sev,
                    $"🏭 {ind.Name} sem insumo ({ind.RequiredInputResource})",
                    $"Produção parada: estoque de {ind.RequiredInputResource} em {ind.InputStock}/{ind.MaxStock} un.",
                    ind.GridX,
                    ind.GridY,
                    $"Atribua caminhões trazendo {ind.RequiredInputResource} para esta planta."));
            }
        }

        // 3. Filas de caminhões esperando carga, quebrados ou aglomerados em nós
        var waitingAtIndustry = new Dictionary<int, (Industry Ind, int Count)>();
        foreach (var truck in engine.ActiveTrucks)
        {
            if (truck.Condition < 25f && !truck.IsDivertedForPitStop)
            {
                var pos = ResolveTruckGridPos(engine, truck);
                results.Add(new OperationalBottleneck(
                    BottleneckKind.QueuedTruck,
                    BottleneckSeverity.Critical,
                    $"🔧 {truck.Name} com desgaste crítico ({truck.Condition:F0}%)",
                    "Veículo sofrendo penalidade severa de velocidade por falta de revisão.",
                    pos.X,
                    pos.Y,
                    "Ative Pit-Stop automático ou envie o caminhão à Garagem/Hub."));
                continue;
            }

            if (truck.Status == TruckStatus.WaitingForCargo && truck.HasManualSchedule)
            {
                var stop = truck.GetCurrentStop();
                if (stop?.Kind == ScheduleStopKind.Industry)
                {
                    Industry? ind = null;
                    var industries = engine.World.Industries;
                    for (int i = 0; i < industries.Count; i++)
                    {
                        if (industries[i].Id == stop.TargetId)
                        {
                            ind = industries[i];
                            break;
                        }
                    }
                    if (ind != null)
                    {
                        waitingAtIndustry[ind.Id] = waitingAtIndustry.TryGetValue(ind.Id, out var prev)
                            ? (ind, prev.Count + 1)
                            : (ind, 1);
                    }
                }
            }
        }

        foreach (var entry in waitingAtIndustry.Values)
        {
            var sev = entry.Count >= 2 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
            results.Add(new OperationalBottleneck(
                BottleneckKind.QueuedTruck,
                sev,
                $"⏳ Fila de caminhões em {entry.Ind.Name} ({entry.Count})",
                $"{entry.Count} caminhão(ões) aguardando estoque de {entry.Ind.OutputResource} ({entry.Ind.OutputStock} un.).",
                entry.Ind.GridX,
                entry.Ind.GridY,
                "Expanda a planta industrial ou mude a condição de espera para 50%/Imediato."));
        }

        // 4. Cidades com abastecimento baixo (< 55%)
        foreach (var city in engine.World.Cities)
        {
            float sat = city.Warehouse.CalculateSatisfactionRate();
            if (sat < 0.55f)
            {
                ResourceType worstResource = ResourceType.None;
                int worstStock = 0;
                float worstRatio = float.MaxValue;
                foreach (var kv in city.Warehouse.DailyDemand)
                {
                    int stock = city.Warehouse.StoredStock.GetValueOrDefault(kv.Key, 0);
                    float ratio = (float)stock / Math.Max(1, kv.Value);
                    if (ratio < worstRatio)
                    {
                        worstRatio = ratio;
                        worstResource = kv.Key;
                        worstStock = stock;
                    }
                }

                var sev = sat < 0.30f ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                results.Add(new OperationalBottleneck(
                    BottleneckKind.UnsuppliedCity,
                    sev,
                    $"🏙️ {city.Name} desabastecida ({sat * 100f:F0}%)",
                    worstResource != ResourceType.None
                        ? $"Escassez crítica de {worstResource} (estoque {worstStock})."
                        : "Estoque urbano abaixo do nível saudável.",
                    city.CenterX,
                    city.CenterY,
                    worstResource != ResourceType.None
                        ? $"Crie uma linha entregando {worstResource} em {city.Name}."
                        : $"Reforce as entregas urbanas para {city.Name}."));
            }
        }

        results.Sort(static (a, b) =>
        {
            int sevCmp = b.Severity.CompareTo(a.Severity);
            return sevCmp != 0 ? sevCmp : a.Kind.CompareTo(b.Kind);
        });
        return results;
    }

    private static (int X, int Y) ResolveTruckGridPos(SimulationEngine engine, Truck truck)
    {
        if (truck.CurrentEdge != null)
            return (truck.CurrentEdge.FromNode.GridX, truck.CurrentEdge.FromNode.GridY);

        var stop = truck.HasManualSchedule ? truck.GetCurrentStop() : null;
        if (stop != null)
        {
            var node = engine.GetStopAccessNode(stop);
            if (node != null)
                return (node.GridX, node.GridY);
        }

        var garage = engine.Garages.FirstOrDefault(g => g.StationedTrucks.Contains(truck));
        if (garage != null)
            return (garage.GridX, garage.GridY);

        return engine.World.Cities.Count > 0
            ? (engine.World.Cities[0].CenterX, engine.World.Cities[0].CenterY)
            : (0, 0);
    }
}
