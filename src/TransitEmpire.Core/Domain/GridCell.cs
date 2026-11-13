namespace TransitEmpire.Core.Domain;

/// <summary>
/// Estrutura de dados leve representando uma única célula no mapa em grade.
/// </summary>
public struct GridCell
{
    public int X;
    public int Y;
    public TerrainType Terrain;
    public BiomeType Biome;
    public RoadTier RoadLevel;
    public int OccupantId; // ID da Cidade, Indústria, Armazém ou Garagem (0 = Vazio)
    public int RivalOwnerId; // > 0 quando o trecho viário pertence a uma transportadora rival

    public bool HasRoad => RoadLevel != RoadTier.None;
    public bool IsBuildable => Terrain == TerrainType.Plains && OccupantId == 0 && !HasRoad;

    public RoadStructureType StructureType => !HasRoad
        ? RoadStructureType.Ground
        : Terrain == TerrainType.Water
            ? RoadStructureType.Bridge
            : (Terrain is TerrainType.Hills or TerrainType.Mountain ? RoadStructureType.Tunnel : RoadStructureType.Ground);

    public GridCell(int x, int y, TerrainType terrain, BiomeType biome = BiomeType.Temperate)
    {
        X = x;
        Y = y;
        Terrain = terrain;
        Biome = biome;
        RoadLevel = RoadTier.None;
        OccupantId = 0;
    }
}

