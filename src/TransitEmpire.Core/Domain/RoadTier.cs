namespace TransitEmpire.Core.Domain;

/// <summary>
/// Níveis de estrada. Apenas o jogo gera estradas inicialmente (Tier 1 - Dirt).
/// O jogador atua exclusivamente aplicando melhorias (upgrades).
/// </summary>
public enum RoadTier : byte
{
    None = 0,
    Dirt = 1,          // 30 km/h - Gerada inicialmente pelo jogo
    Gravel = 2,        // 45 km/h - Cascalho compactado
    Asphalt = 3,       // 70 km/h - Asfalto convencional
    Highway = 4,       // 90 km/h - Rodovia expressa
    DualCarriage = 5   // 110 km/h - Pista dupla com canteiro central
}

/// <summary>
/// Tipo de estrutura da via de acordo com o relevo atravessado.
/// </summary>
public enum RoadStructureType : byte
{
    Ground = 0,
    Bridge = 1,
    Tunnel = 2
}

public static class RoadTierExtensions
{
    public static int GetBaseConstructionCost(this RoadStructureType structure) => structure switch
    {
        RoadStructureType.Bridge => 150,
        RoadStructureType.Tunnel => 220,
        _ => 40
    };
    public static float GetSpeedLimitKmh(this RoadTier tier) => tier switch
    {
        RoadTier.Dirt => 30f,
        RoadTier.Gravel => 45f,
        RoadTier.Asphalt => 70f,
        RoadTier.Highway => 90f,
        RoadTier.DualCarriage => 110f,
        _ => 0f
    };

    public static int GetUpgradeCost(this RoadTier fromTier, RoadTier toTier)
    {
        if (toTier <= fromTier) return 0;
        
        int total = 0;
        for (int t = (int)fromTier + 1; t <= (int)toTier; t++)
        {
            total += (RoadTier)t switch
            {
                RoadTier.Gravel => 50,
                RoadTier.Asphalt => 150,
                RoadTier.Highway => 350,
                RoadTier.DualCarriage => 800,
                _ => 0
            };
        }
        return total;
    }
}

