namespace TransitEmpire.Core.Domain;

/// <summary>
/// Transportadora concorrente simulada por IA que disputa participação de mercado e contratos expressos.
/// </summary>
public class RivalCompany
{
    public int Id { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public int DeliveredVolume { get; set; }
    public int CompletedContracts { get; set; }
    public long EstimatedRevenue { get; set; }
    public int OwnedRoadCount { get; set; }
    public long TollRevenueCollected { get; set; }
    public int AuctionsWon { get; set; }

    public RivalCompany(int id, string name, string colorHex)
    {
        Id = id;
        Name = name;
        ColorHex = colorHex;
    }
}

public readonly record struct MarketShareStanding(
    string Name,
    bool IsPlayer,
    string ColorHex,
    int DeliveredVolume,
    int CompletedContracts,
    float SharePercent);

