namespace TransitEmpire.Core.Domain;

/// <summary>
/// Praça de Pedágio & Balança de Pesagem instalada em rodovias para gerar receita auxiliar
/// e bonificar caminhões que operam dentro do padrão ideal de manutenção.
/// </summary>
public class TollStation
{
    public const int ConstructionCost = 3500;
    public const long DefaultTollFee = 120;
    public const long WeighStationComplianceBonus = 60;

    public int Id { get; }
    public string Name { get; }
    public int GridX { get; }
    public int GridY { get; }
    public RoadNode AccessNode { get; }
    public long TollFee { get; set; } = DefaultTollFee;
    public long TotalTollCollected { get; set; }
    public int TrucksInspected { get; set; }
    public string LastWeighDisplay { get; set; } = "LIVRE • BALANÇA OK";

    public TollStation(int id, string name, int gridX, int gridY, RoadNode accessNode)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        AccessNode = accessNode;
    }
}

