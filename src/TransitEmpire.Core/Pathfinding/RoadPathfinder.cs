using System;
using System.Collections.Generic;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Pathfinding;

/// <summary>
/// Algoritmo A* de alta performance operando diretamente sobre o grafo da malha viária (RoadGraph).
/// Otimiza pelo menor tempo de viagem (horas), considerando velocidade do nível da estrada e densidade de tráfego.
/// </summary>
public class RoadPathfinder
{
    private const int MaxCacheEntries = 4096;

    private readonly record struct RouteCacheKey(
        int OriginId,
        int DestinationId,
        WeatherCondition Weather,
        bool PreferHighways,
        bool WinterBlizzard);

    private readonly Dictionary<RouteCacheKey, List<RoadEdge>?> _routeCache = new();

    public long CacheHits { get; private set; }
    public long CacheMisses { get; private set; }
    public int CachedRouteCount => _routeCache.Count;
    public int CacheGeneration { get; private set; }

    /// <summary>
    /// Limpa o cache de rotas após alterações topológicas, upgrades, interdições ou manutenção viária.
    /// </summary>
    public void InvalidateCache()
    {
        _routeCache.Clear();
        CacheGeneration++;
    }

    /// <summary>
    /// Reseta contadores de telemetria e limpa rotas em cache.
    /// </summary>
    public void ResetCacheStats()
    {
        _routeCache.Clear();
        CacheHits = 0;
        CacheMisses = 0;
        CacheGeneration = 0;
    }

    /// <summary>
    /// Encontra o trajeto mais rápido entre dois nós da malha viária.
    /// Retorna a lista sequencial de RoadEdge que o caminhão deve percorrer.
    /// </summary>
    public List<RoadEdge>? FindFastestRoute(
        RoadNode origin,
        RoadNode destination,
        WeatherCondition weather = WeatherCondition.Clear,
        bool preferHighways = false,
        bool winterBlizzard = false,
        bool bypassCache = false)
    {
        if (origin == null || destination == null) return null;
        if (origin.Id == destination.Id) return new List<RoadEdge>();

        var cacheKey = new RouteCacheKey(origin.Id, destination.Id, weather, preferHighways, winterBlizzard);
        if (!bypassCache && _routeCache.TryGetValue(cacheKey, out var cachedRoute))
        {
            CacheHits++;
            return cachedRoute == null ? null : new List<RoadEdge>(cachedRoute);
        }

        if (!bypassCache)
        {
            CacheMisses++;
        }

        // Fila de prioridade: menor tempo estimado (G + H) tem precedência
        var openSet = new PriorityQueue<RoadNode, float>();
        var gScore = new Dictionary<int, float>(); // Tempo real gasto do início até o nó
        var cameFrom = new Dictionary<int, (RoadNode PreviousNode, RoadEdge ConnectingEdge)>();

        gScore[origin.Id] = 0f;
        openSet.Enqueue(origin, EstimateHeuristicHours(origin, destination));

        var visited = new HashSet<int>();
        List<RoadEdge>? result = null;

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();

            if (current.Id == destination.Id)
            {
                // Reconstrói o caminho de arestas
                result = ReconstructPath(cameFrom, current.Id);
                break;
            }

            if (!visited.Add(current.Id))
                continue;

            foreach (var edge in current.OutgoingEdges)
            {
                if (edge.IsBlocked)
                    continue;

                var neighbor = edge.ToNode;
                if (visited.Contains(neighbor.Id))
                    continue;

                // Tempo para atravessar esta aresta considerando tráfego, desgaste e clima
                float edgeTravelTime = edge.EstimateTravelTimeHours(weather, winterBlizzard);
                if (preferHighways && edge.Tier < RoadTier.Highway)
                {
                    // Penalidade para manter Comboios Rodotrem em Rodovias e Pistas Duplas sempre que possível
                    edgeTravelTime *= 6.0f;
                }
                float tentativeG = gScore[current.Id] + edgeTravelTime;

                if (!gScore.TryGetValue(neighbor.Id, out float existingG) || tentativeG < existingG)
                {
                    gScore[neighbor.Id] = tentativeG;
                    cameFrom[neighbor.Id] = (current, edge);

                    float fScore = tentativeG + EstimateHeuristicHours(neighbor, destination);
                    openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        if (!bypassCache)
        {
            if (_routeCache.Count >= MaxCacheEntries)
            {
                _routeCache.Clear();
            }
            _routeCache[cacheKey] = result == null ? null : new List<RoadEdge>(result);
        }

        return result;
    }

    public static float EstimatePathTravelTimeHours(
        IEnumerable<RoadEdge> path,
        WeatherCondition weather = WeatherCondition.Clear,
        bool winterBlizzard = false)
    {
        float total = 0f;
        foreach (var edge in path)
            total += edge.EstimateTravelTimeHours(weather, winterBlizzard);
        return total;
    }

    /// <summary>
    /// Heurística admissível baseada na distância euclidiana dividida pela velocidade máxima teórica do jogo (110 km/h).
    /// </summary>
    private static float EstimateHeuristicHours(RoadNode from, RoadNode to)
    {
        float dx = to.GridX - from.GridX;
        float dy = to.GridY - from.GridY;
        float distanceKm = MathF.Sqrt(dx * dx + dy * dy);
        return distanceKm / 110f; // Velocidade máxima teórica para não superestimar (admissível)
    }

    private static List<RoadEdge> ReconstructPath(
        Dictionary<int, (RoadNode PreviousNode, RoadEdge ConnectingEdge)> cameFrom,
        int currentId)
    {
        var path = new List<RoadEdge>();
        while (cameFrom.TryGetValue(currentId, out var entry))
        {
            path.Add(entry.ConnectingEdge);
            currentId = entry.PreviousNode.Id;
        }

        path.Reverse();
        return path;
    }
}

