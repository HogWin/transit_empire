namespace TransitEmpire.Core.Domain;

/// <summary>
/// Tipos de terreno do mapa.
/// </summary>
public enum TerrainType : byte
{
    Plains = 0,
    Forest = 1,
    Hills = 2,
    Mountain = 3, // Bloqueia estradas convencionais
    Water = 4     // Requer pontes
}

/// <summary>
/// Biomas regionais para variação visual (planície verde, deserto dourado e picos nevados).
/// </summary>
public enum BiomeType : byte
{
    Temperate = 0,
    Desert = 1,
    Alpine = 2
}

