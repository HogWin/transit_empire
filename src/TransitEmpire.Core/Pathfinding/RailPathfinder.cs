using System;
using System.Collections.Generic;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Pathfinding;

/// <summary>
/// Calculador A* para rotas ferroviárias respeitando velocidade de via, viadutos e túneis.
/// </summary>
public class RailPathfinder
{
    public List<RailEdge>? FindFastestRoute(RailNode origin, RailNode destination)
    {
        if (origin == null || destination == null)
            return null;

        if (origin.Id == destination.Id)
            return new List<RailEdge>();

        var openSet = new PriorityQueue<RailNode, float>();
        var cameFrom = new Dictionary<int, (RailNode Parent, RailEdge Edge)>();
        var gScore = new Dictionary<int, float> { [origin.Id] = 0f };
        var closed = new HashSet<int>();

        openSet.Enqueue(origin, EstimateHeuristicHours(origin, destination));

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            if (current.Id == destination.Id)
                return ReconstructPath(cameFrom, destination);

            if (!closed.Add(current.Id))
                continue;

            float currentG = gScore[current.Id];
            foreach (var edge in current.OutgoingEdges)
            {
                var neighbor = edge.ToNode;
                if (closed.Contains(neighbor.Id))
                    continue;

                float tentativeG = currentG + edge.EstimateTravelTimeHours(115f);
                if (!gScore.TryGetValue(neighbor.Id, out float existingG) || tentativeG < existingG)
                {
                    gScore[neighbor.Id] = tentativeG;
                    cameFrom[neighbor.Id] = (current, edge);
                    float f = tentativeG + EstimateHeuristicHours(neighbor, destination);
                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        return null;
    }

    private static float EstimateHeuristicHours(RailNode a, RailNode b)
    {
        float manhattanKm = MathF.Abs(a.GridX - b.GridX) + MathF.Abs(a.GridY - b.GridY);
        return manhattanKm / 115f;
    }

    private static List<RailEdge> ReconstructPath(
        Dictionary<int, (RailNode Parent, RailEdge Edge)> cameFrom,
        RailNode destination)
    {
        var path = new List<RailEdge>();
        int cursor = destination.Id;
        while (cameFrom.TryGetValue(cursor, out var step))
        {
            path.Add(step.Edge);
            cursor = step.Parent.Id;
        }
        path.Reverse();
        return path;
    }
}

