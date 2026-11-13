using System;
using System.Collections.Generic;
using System.Linq;

namespace TransitEmpire.Core.Domain;

public enum LandmarkKind
{
    HydroelectricDam = 0,
    MunicipalStadium = 1,
    WindFarmPark = 2
}

/// <summary>
/// Mega-Obra Regional em múltiplas etapas que consome grandes volumes de Steel, Lumber e Goods
/// e desbloqueia bônus permanentes para toda a operação.
/// </summary>
public class RegionalLandmark
{
    public const int MaxStages = 3;

    public int Id { get; }
    public string Name { get; }
    public LandmarkKind Kind { get; }
    public int GridX { get; }
    public int GridY { get; }

    public int CurrentStage { get; private set; } = 1; // 1..3; > MaxStages significa concluída
    public bool IsCompleted => CurrentStage > MaxStages;
    public long TotalInvestedBonusPaid { get; set; }

    public Dictionary<ResourceType, int> StageRequired { get; } = new();
    public Dictionary<ResourceType, int> StageDelivered { get; } = new();

    public RegionalLandmark(int id, string name, LandmarkKind kind, int gridX, int gridY)
    {
        Id = id;
        Name = name;
        Kind = kind;
        GridX = gridX;
        GridY = gridY;
        ConfigureStageRequirements(CurrentStage);
    }

    public static bool AcceptsResource(ResourceType res)
        => res is ResourceType.Steel or ResourceType.Lumber or ResourceType.Goods or ResourceType.Machinery;

    private void ConfigureStageRequirements(int stage)
    {
        StageRequired.Clear();
        StageDelivered.Clear();

        if (stage == 1)
        {
            StageRequired[ResourceType.Steel] = 30;
            StageRequired[ResourceType.Lumber] = 30;
        }
        else if (stage == 2)
        {
            StageRequired[ResourceType.Steel] = 45;
            StageRequired[ResourceType.Goods] = 35;
            StageRequired[ResourceType.Machinery] = 20;
        }
        else if (stage == 3)
        {
            StageRequired[ResourceType.Steel] = 45;
            StageRequired[ResourceType.Lumber] = 40;
            StageRequired[ResourceType.Goods] = 45;
            StageRequired[ResourceType.Machinery] = 30;
        }

        foreach (var key in StageRequired.Keys)
            StageDelivered[key] = 0;
    }

    public int GetRemainingForResource(ResourceType res)
    {
        if (IsCompleted || !StageRequired.TryGetValue(res, out int req))
            return 0;
        int deliv = StageDelivered.GetValueOrDefault(res, 0);
        return Math.Max(0, req - deliv);
    }

    public float CompletionPercentage
    {
        get
        {
            if (IsCompleted) return 1.0f;
            int totalReq = Math.Max(1, StageRequired.Values.Sum());
            int totalDeliv = StageRequired.Sum(kv => Math.Min(kv.Value, StageDelivered.GetValueOrDefault(kv.Key, 0)));
            float stageFrac = (float)totalDeliv / totalReq;
            return Math.Clamp(((CurrentStage - 1) + stageFrac) / MaxStages, 0f, 1f);
        }
    }

    public string BonusSummary => Kind switch
    {
        LandmarkKind.HydroelectricDam => "+15% valor de frete global e -10% manutenção",
        LandmarkKind.MunicipalStadium => "+25% XP nas entregas e atratividade urbana",
        LandmarkKind.WindFarmPark => "+15% economia de diesel e +10% produção industrial",
        _ => "Bônus regional"
    };

    /// <summary>
    /// Entrega insumos para a etapa atual da Mega-Obra. Avança automaticamente de etapa ao completar os requisitos.
    /// </summary>
    public int ContributeCargo(ResourceType type, int amount, out bool stageCompleted, out bool projectCompleted)
    {
        stageCompleted = false;
        projectCompleted = false;
        if (IsCompleted || amount <= 0)
            return 0;

        int remaining = GetRemainingForResource(type);
        if (remaining <= 0)
            return 0;

        int accepted = Math.Min(amount, remaining);
        StageDelivered[type] = StageDelivered.GetValueOrDefault(type, 0) + accepted;

        bool allMet = StageRequired.All(kv => StageDelivered.GetValueOrDefault(kv.Key, 0) >= kv.Value);
        if (allMet)
        {
            stageCompleted = true;
            CurrentStage++;
            if (IsCompleted)
            {
                projectCompleted = true;
                StageRequired.Clear();
                StageDelivered.Clear();
            }
            else
            {
                ConfigureStageRequirements(CurrentStage);
            }
        }
        return accepted;
    }

    internal void RestoreState(int currentStage, Dictionary<string, int> deliveredMap, long totalBonusPaid)
    {
        CurrentStage = Math.Clamp(currentStage, 1, MaxStages + 1);
        TotalInvestedBonusPaid = totalBonusPaid;
        if (IsCompleted)
        {
            StageRequired.Clear();
            StageDelivered.Clear();
            return;
        }

        ConfigureStageRequirements(CurrentStage);
        foreach (var kv in deliveredMap)
        {
            if (Enum.TryParse<ResourceType>(kv.Key, out var rt) && StageRequired.ContainsKey(rt))
            {
                StageDelivered[rt] = Math.Clamp(kv.Value, 0, StageRequired[rt]);
            }
        }
    }
}
