using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Nó no grafo da malha viária (cruzamentos, entroncamentos ou finais de linha).
/// </summary>
public class RoadNode
{
    public int Id { get; }
    public int GridX { get; }
    public int GridY { get; }
    public bool HasTrafficControl { get; set; } = false;
    public List<RoadEdge> OutgoingEdges { get; } = new();
    public bool IsIntersection => OutgoingEdges.Count >= 3;

    public RoadNode(int id, int gridX, int gridY)
    {
        Id = id;
        GridX = gridX;
        GridY = gridY;
    }
}

/// <summary>
/// Aresta no grafo da malha viária conectando dois nós.
/// </summary>
public class RoadEdge
{
    public int Id { get; }
    public RoadNode FromNode { get; }
    public RoadNode ToNode { get; }
    public RoadTier Tier { get; set; }
    public float DistanceKm { get; }
    public int ActiveTrucksCount { get; set; }
    public float PavementCondition { get; set; } = 100f; // 25% a 100%
    public bool IsBlocked { get; set; } = false;
    public bool IsAlpineBiome { get; set; } = false;
    public bool IsHillOrMountain { get; set; } = false;
    public float UrbanTrafficSpeedMultiplier { get; set; } = 1.0f; // Penalidade de trânsito pesado no centro urbano
    public int OwnerRivalId { get; set; } = 0; // > 0 para trecho privado construído por rival
    public long RivalTollFee { get; set; } = 0;
    public long RecentRevenue30d { get; set; } = 0;

    public float CurrentSpeedLimitKmh => Tier.GetSpeedLimitKmh();

    public RoadEdge(int id, RoadNode fromNode, RoadNode toNode, RoadTier tier, float distanceKm)
    {
        Id = id;
        FromNode = fromNode;
        ToNode = toNode;
        Tier = tier;
        DistanceKm = distanceKm;
        ActiveTrucksCount = 0;
    }

    public void ApplyTrafficWear(float distanceKm, int truckCapacity)
    {
        if (distanceKm <= 0f) return;
        float weightFactor = 0.50f + (truckCapacity / 100f);
        float tierResilience = Tier switch
        {
            RoadTier.Dirt => 1.40f,
            RoadTier.Gravel => 1.10f,
            RoadTier.Asphalt => 0.70f,
            RoadTier.Highway => 0.45f,
            RoadTier.DualCarriage => 0.35f,
            _ => 1.0f
        };
        PavementCondition = System.MathF.Max(25f, PavementCondition - (distanceKm * 0.03f * weightFactor * tierResilience));
    }

    public void Repave()
    {
        PavementCondition = 100f;
    }

    /// <summary>
    /// Velocidade efetiva da via considerando congestionamento, condições climáticas, desgaste asfáltico e nevasca alpina.
    /// </summary>
    public float GetEffectiveSpeedLimitKmh(WeatherCondition weather = WeatherCondition.Clear, bool winterBlizzard = false)
    {
        float speed = CurrentSpeedLimitKmh * Tier.GetWeatherSpeedMultiplier(weather);

        if (PavementCondition < 85f)
        {
            float conditionFactor = System.Math.Clamp(0.45f + (PavementCondition / 100f) * 0.55f, 0.45f, 1.0f);
            speed *= conditionFactor;
        }

        if (winterBlizzard && IsAlpineBiome)
        {
            speed *= Tier < RoadTier.Asphalt ? 0.45f : 0.85f;
        }

        if (UrbanTrafficSpeedMultiplier < 0.999f)
        {
            speed *= System.Math.Clamp(UrbanTrafficSpeedMultiplier, 0.30f, 1.0f);
        }

        // Capacidade de fluxo varia conforme o nível da via (Pista Dupla e Rodovia suportam muito mais caminhões sem lentidão)
        bool hasSmartControl = FromNode.HasTrafficControl || ToNode.HasTrafficControl;
        if (hasSmartControl && ActiveTrucksCount > 1)
        {
            speed *= 1.12f;
        }

        int congestionThreshold = Tier switch
        {
            RoadTier.Dirt => 2,
            RoadTier.Gravel => 3,
            RoadTier.Asphalt => 5,
            RoadTier.Highway => 8,
            RoadTier.DualCarriage => 12,
            _ => 5
        };

        if (hasSmartControl)
        {
            congestionThreshold += 4;
        }

        if (ActiveTrucksCount > congestionThreshold)
        {
            float congestionFactor = System.MathF.Max(0.30f, 1f - (ActiveTrucksCount - congestionThreshold) * 0.06f);
            speed *= congestionFactor;
        }

        return System.MathF.Max(5f, speed);
    }

    /// <summary>
    /// Tempo estimado em horas para percorrer este trecho (considerando velocidade, densidade, desgaste e clima).
    /// </summary>
    public float EstimateTravelTimeHours(WeatherCondition weather = WeatherCondition.Clear, bool winterBlizzard = false)
    {
        float speed = GetEffectiveSpeedLimitKmh(weather, winterBlizzard);
        return DistanceKm / System.MathF.Max(1f, speed);
    }
}

