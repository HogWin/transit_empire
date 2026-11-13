using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Simulation;

/// <summary>
/// Tecnologias pesquisáveis na Árvore de Pesquisa (estilo Voxel Tycoon).
/// </summary>
public enum ResearchTechId
{
    HeavyDieselEngines,
    AsphaltPaving,
    ExpressHighways,
    BiTrainTrucks,
    ReeferBays,
    SmartRouting,
    RoadTrain9Axles
}

/// <summary>
/// Nó individual de tecnologia na Árvore de Pesquisa.
/// </summary>
public class ResearchNode
{
    public ResearchTechId Id { get; }
    public string Name { get; }
    public string Description { get; }
    public int FundingCost { get; }
    public float RequiredHours { get; }
    public ResearchTechId? PrerequisiteId { get; }

    public float ProgressHours { get; internal set; }
    public bool IsFunded { get; internal set; }

    public bool IsCompleted => ProgressHours >= RequiredHours;
    public float ProgressRatio => RequiredHours <= 0f ? 1f : Math.Clamp(ProgressHours / RequiredHours, 0f, 1f);

    public ResearchNode(
        ResearchTechId id,
        string name,
        string description,
        int fundingCost,
        float requiredHours,
        ResearchTechId? prerequisiteId = null)
    {
        Id = id;
        Name = name;
        Description = description;
        FundingCost = fundingCost;
        RequiredHours = requiredHours;
        PrerequisiteId = prerequisiteId;
    }
}

/// <summary>
/// Gerencia a Árvore de Pesquisa & Tecnologia em tempo real da empresa.
/// </summary>
public class ResearchManager
{
    private readonly List<ResearchNode> _nodes;

    public IReadOnlyList<ResearchNode> Nodes => _nodes;
    public ResearchNode? ActiveResearch { get; private set; }

    public ResearchManager()
    {
        _nodes = new List<ResearchNode>
        {
            new ResearchNode(
                id: ResearchTechId.HeavyDieselEngines,
                name: "Motores Diesel Pesados",
                description: "+20% velocidade da frota e libera MB L-1620 Bicudo & Volvo FH 540 Globetrotter.",
                fundingCost: 4000,
                requiredHours: 24f
            ),
            new ResearchNode(
                id: ResearchTechId.AsphaltPaving,
                name: "Pavimentação Asfáltica",
                description: "Libera pavimentação em Asfalto (90 km/h) sem depender de nível.",
                fundingCost: 3500,
                requiredHours: 20f
            ),
            new ResearchNode(
                id: ResearchTechId.ExpressHighways,
                name: "Rodovias Expressas",
                description: "Libera Rodovias e Pistas Duplas (120+ km/h) para corredores pesados.",
                fundingCost: 9000,
                requiredHours: 36f,
                prerequisiteId: ResearchTechId.AsphaltPaving
            ),
            new ResearchNode(
                id: ResearchTechId.BiTrainTrucks,
                name: "Caminhões Bi-Trem",
                description: "Libera Peterbilt 389 Bi-Trem King (150 un.) e +15% na tarifa de frete.",
                fundingCost: 14000,
                requiredHours: 48f,
                prerequisiteId: ResearchTechId.HeavyDieselEngines
            ),
            new ResearchNode(
                id: ResearchTechId.ReeferBays,
                name: "Logística Refrigerada",
                description: "+25% no valor das entregas de Alimentos (Food) com câmaras frigoríficas.",
                fundingCost: 6500,
                requiredHours: 28f,
                prerequisiteId: ResearchTechId.HeavyDieselEngines
            ),
            new ResearchNode(
                id: ResearchTechId.SmartRouting,
                name: "Telemetria & Roteamento Dinâmico",
                description: "Caminhões recalculam rotas em tempo real para desviar de congestionamentos.",
                fundingCost: 11000,
                requiredHours: 32f,
                prerequisiteId: ResearchTechId.AsphaltPaving
            ),
            new ResearchNode(
                id: ResearchTechId.RoadTrain9Axles,
                name: "Comboios Rodotrem (9 Eixos)",
                description: "Libera o Scania R730 V8 Rodotrem 9 Eixos (220 un.), ideal para Rodovias e Pistas Duplas.",
                fundingCost: 22000,
                requiredHours: 60f,
                prerequisiteId: ResearchTechId.BiTrainTrucks
            )
        };
    }

    public ResearchNode GetNode(ResearchTechId id) => _nodes.First(n => n.Id == id);

    public bool IsCompleted(ResearchTechId id) => GetNode(id).IsCompleted;

    /// <summary>
    /// Inicia ou retoma uma pesquisa tecnológica, cobrando o financiamento na primeira ativação.
    /// </summary>
    public bool StartOrSwitchResearch(ResearchTechId id, PlayerState player, out string message)
    {
        var node = GetNode(id);

        if (node.IsCompleted)
        {
            message = $"✅ Pesquisa '{node.Name}' já foi concluída.";
            return false;
        }

        if (node.PrerequisiteId.HasValue && !IsCompleted(node.PrerequisiteId.Value))
        {
            var prereq = GetNode(node.PrerequisiteId.Value);
            message = $"🔒 '{node.Name}' requer a pesquisa prévia: {prereq.Name}.";
            return false;
        }

        if (ActiveResearch == node)
        {
            message = $"🔬 '{node.Name}' já está em andamento ({node.ProgressRatio * 100f:F0}%).";
            return false;
        }

        if (!node.IsFunded)
        {
            if (player.Balance < node.FundingCost)
            {
                message = $"⚠️ Saldo insuficiente para financiar '{node.Name}' (R$ {node.FundingCost:N0}).";
                return false;
            }

            player.Balance -= node.FundingCost;
            node.IsFunded = true;
        }

        ActiveResearch = node;
        message = $"🔬 Pesquisa ativa: {node.Name} ({node.ProgressHours:F0}/{node.RequiredHours:F0}h).";
        return true;
    }

    public void PauseResearch()
    {
        ActiveResearch = null;
    }

    internal void RestoreState(ResearchTechId? activeTechId, IEnumerable<(ResearchTechId Id, float ProgressHours, bool IsFunded)> nodeStates)
    {
        foreach (var state in nodeStates)
        {
            var node = GetNode(state.Id);
            node.ProgressHours = state.ProgressHours;
            node.IsFunded = state.IsFunded;
        }
        ActiveResearch = activeTechId.HasValue ? GetNode(activeTechId.Value) : null;
    }

    /// <summary>
    /// Avança o progresso em horas da pesquisa ativa; retorna o nó se concluir neste tick.
    /// </summary>
    public ResearchNode? Advance(float deltaHours)
    {
        if (ActiveResearch == null || deltaHours <= 0f)
            return null;

        ActiveResearch.ProgressHours += deltaHours;
        if (!ActiveResearch.IsCompleted)
            return null;

        ActiveResearch.ProgressHours = ActiveResearch.RequiredHours;
        var completed = ActiveResearch;
        ActiveResearch = null;
        return completed;
    }

    public float FleetSpeedMultiplier => IsCompleted(ResearchTechId.HeavyDieselEngines) ? 1.20f : 1.0f;

    public float HeavyFreightBonusMultiplier => IsCompleted(ResearchTechId.BiTrainTrucks) ? 1.15f : 1.0f;

    public float FoodFreightBonusMultiplier => IsCompleted(ResearchTechId.ReeferBays) ? 1.25f : 1.0f;

    public bool IsSmartRoutingEnabled => IsCompleted(ResearchTechId.SmartRouting);

    /// <summary>
    /// Retorna o maior nível de estrada liberado pela combinação de nível do jogador e pesquisas concluídas.
    /// </summary>
    public RoadTier GetMaxUnlockedRoadTier(int playerLevel)
    {
        RoadTier byLevel = ProgressionManager.GetMaxUnlockedRoadTier(playerLevel);
        RoadTier byResearch = RoadTier.Gravel;

        if (IsCompleted(ResearchTechId.ExpressHighways))
            byResearch = RoadTier.DualCarriage;
        else if (IsCompleted(ResearchTechId.AsphaltPaving))
            byResearch = RoadTier.Asphalt;

        return (RoadTier)Math.Max((int)byLevel, (int)byResearch);
    }

    /// <summary>
    /// Verifica se um modelo de caminhão está liberado pelo nível do jogador ou por tecnologia pesquisada.
    /// </summary>
    public bool IsTruckUnlocked(TruckSpecification spec, int playerLevel)
    {
        if (playerLevel >= spec.RequiredPlayerLevel)
            return true;

        if (spec.RequiredPlayerLevel <= 3 && IsCompleted(ResearchTechId.HeavyDieselEngines))
            return true;

        if (spec.RequiredPlayerLevel <= 4 && IsCompleted(ResearchTechId.BiTrainTrucks))
            return true;

        if (spec.RequiredPlayerLevel <= 5 && IsCompleted(ResearchTechId.RoadTrain9Axles))
            return true;

        return false;
    }
}

