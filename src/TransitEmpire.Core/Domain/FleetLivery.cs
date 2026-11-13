namespace TransitEmpire.Core.Domain;

/// <summary>
/// Acabamento PBR da pintura dos caminhões na oficina/concessionária.
/// </summary>
public enum PaintFinish
{
    Metallic = 0,
    Matte = 1,
    Chrome = 2
}

/// <summary>
/// Identidade visual (pintura + acabamento) da transportadora ou de uma Linha Logística.
/// </summary>
public sealed class FleetLivery
{
    public bool IsCustomized { get; set; } = false;
    public string PrimaryColorHex { get; set; } = "#1d4ed8";
    public string StripeColorHex { get; set; } = "#facc15";
    public PaintFinish Finish { get; set; } = PaintFinish.Metallic;

    public FleetLivery Clone() => new()
    {
        IsCustomized = IsCustomized,
        PrimaryColorHex = PrimaryColorHex,
        StripeColorHex = StripeColorHex,
        Finish = Finish
    };

    public string GetRenderKey()
        => IsCustomized ? $"{PrimaryColorHex.ToUpperInvariant()}|{StripeColorHex.ToUpperInvariant()}|{(int)Finish}" : "FACTORY";
}

/// <summary>
/// Patente de veterania por quilometragem acumulada do caminhão/motorista.
/// </summary>
public enum TruckRank
{
    Rookie = 0,     // ★ Novato (< 50 km)
    Veteran = 1,    // ★★ Veterano (>= 50 km): -5% diesel, +6% subida
    RoadLegend = 2  // ★★★ Lenda da Estrada (>= 150 km): -10% diesel, +12% subida + insígnia dourada
}

