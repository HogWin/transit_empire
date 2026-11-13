using System;
using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Terminal Portuário de Contêineres &amp; Graneleiro para exportação e navegação marítima entre continentes.
/// Quando o navio cargueiro está atracado, concede +45% de bônus de exportação por tonelada.
/// </summary>
public class SeaportTerminal
{
    public const float ExportBonusMultiplier = 1.45f;
    public const float DockedDurationHours = 120f;
    public const float SeaVoyageDurationHours = 60f;

    public int Id { get; }
    public string Name { get; }
    public int GridX { get; }
    public int GridY { get; }
    public int WaterDockX { get; }
    public int WaterDockY { get; }
    public int? PartnerSeaportId { get; set; }

    public bool IsShipDocked { get; private set; } = true;
    public float CycleHoursRemaining { get; private set; } = DockedDurationHours;
    public int CurrentShipCargo { get; private set; } = 0;
    public int MaxShipCapacity { get; set; } = 320;
    public int TotalExportedUnits { get; set; } = 0;
    public long TotalExportRevenue { get; set; } = 0;

    /// <summary>Carga embarcada aguardando travessia marítima para o porto parceiro.</summary>
    public Dictionary<ResourceType, int> OutboundCargo { get; } = new();

    /// <summary>Carga recebida de outro continente por navio, disponível para retirada por caminhões.</summary>
    public Dictionary<ResourceType, int> TransitCargo { get; } = new();

    /// <summary>Carga atualmente em trânsito no mar rumo a este terminal.</summary>
    public Dictionary<ResourceType, int> InTransitFromSea { get; } = new();

    public SeaportTerminal(int id, string name, int gridX, int gridY, int waterDockX, int waterDockY, int? partnerSeaportId = null)
    {
        Id = id;
        Name = name;
        GridX = gridX;
        GridY = gridY;
        WaterDockX = waterDockX;
        WaterDockY = waterDockY;
        PartnerSeaportId = partnerSeaportId;
    }

    public static bool AcceptsExportResource(ResourceType res)
        => res is ResourceType.Grain or ResourceType.Steel or ResourceType.Lumber or ResourceType.Goods or ResourceType.Timber;

    public bool AcceptsCargo(ResourceType res)
        => PartnerSeaportId.HasValue ? res != ResourceType.None : AcceptsExportResource(res);

    /// <summary>
    /// Embarca carregamento no terminal portuário. Retorna quantidade aceita e informa se o navio estava atracado (+45% bônus).
    /// </summary>
    public int LoadExportCargo(ResourceType type, int amount, out bool wasShipDocked)
    {
        wasShipDocked = IsShipDocked;
        if (!AcceptsCargo(type) || amount <= 0)
            return 0;

        int freeSpace = Math.Max(0, MaxShipCapacity - CurrentShipCargo);
        int accepted = IsShipDocked ? Math.Min(amount, freeSpace) : amount;
        if (accepted <= 0 && freeSpace == 0 && IsShipDocked)
        {
            // Aceita no pátio mesmo com navio lotado para não travar linhas, porém sem bônus extra de embarque direto
            wasShipDocked = false;
            accepted = amount;
        }
        else if (IsShipDocked)
        {
            CurrentShipCargo += accepted;
        }

        if (PartnerSeaportId.HasValue && accepted > 0)
        {
            OutboundCargo[type] = OutboundCargo.GetValueOrDefault(type, 0) + accepted;
        }

        TotalExportedUnits += accepted;
        return accepted;
    }

    /// <summary>
    /// Retira carga importada/marítima do pátio do porto para distribuição em terra.
    /// </summary>
    public int DispatchTransitCargo(ResourceType type, int maxAmount)
    {
        if (maxAmount <= 0 || type == ResourceType.None)
            return 0;

        int available = TransitCargo.GetValueOrDefault(type, 0);
        int taken = Math.Min(available, maxAmount);
        if (taken > 0)
        {
            int left = available - taken;
            if (left > 0)
                TransitCargo[type] = left;
            else
                TransitCargo.Remove(type);
        }
        return taken;
    }

    public void QueueIncomingSeaCargo(Dictionary<ResourceType, int> shipment)
    {
        foreach (var (res, amt) in shipment)
        {
            if (amt > 0 && res != ResourceType.None)
                InTransitFromSea[res] = InTransitFromSea.GetValueOrDefault(res, 0) + amt;
        }
    }

    public int UnloadArrivedSeaCargoToYard()
    {
        int total = 0;
        foreach (var (res, amt) in InTransitFromSea)
        {
            if (amt > 0 && res != ResourceType.None)
            {
                TransitCargo[res] = TransitCargo.GetValueOrDefault(res, 0) + amt;
                total += amt;
            }
        }
        InTransitFromSea.Clear();
        return total;
    }

    public Dictionary<ResourceType, int> ConsumeOutboundManifest()
    {
        if (OutboundCargo.Count == 0)
            return new Dictionary<ResourceType, int>();

        var copy = new Dictionary<ResourceType, int>(OutboundCargo);
        OutboundCargo.Clear();
        return copy;
    }

    /// <summary>
    /// Avança o ciclo logístico do navio cargueiro (Atracado &lt;-&gt; Em Alto-Mar).
    /// Retorna true no instante em que o navio zarpa ou atraca novamente.
    /// </summary>
    public bool AdvanceTime(float deltaHours, out int sailedUnits)
    {
        sailedUnits = 0;
        if (deltaHours <= 0f)
            return false;

        CycleHoursRemaining -= deltaHours;
        if (CycleHoursRemaining > 0f)
            return false;

        if (IsShipDocked)
        {
            // Navio zarpa levando os carregamentos exportados
            sailedUnits = CurrentShipCargo;
            CurrentShipCargo = 0;
            IsShipDocked = false;
            CycleHoursRemaining = SeaVoyageDurationHours;
        }
        else
        {
            // Navio cargueiro atraca trazendo cargas vindas do mar
            IsShipDocked = true;
            CycleHoursRemaining = DockedDurationHours;
        }
        return true;
    }

    internal void RestoreState(
        bool isShipDocked,
        float cycleHoursRemaining,
        int currentShipCargo,
        int maxShipCapacity,
        int totalExportedUnits,
        long totalExportRevenue,
        int? partnerSeaportId = null,
        Dictionary<string, int>? outboundCargo = null,
        Dictionary<string, int>? transitCargo = null,
        Dictionary<string, int>? inTransitFromSea = null)
    {
        IsShipDocked = isShipDocked;
        CycleHoursRemaining = cycleHoursRemaining;
        CurrentShipCargo = currentShipCargo;
        MaxShipCapacity = maxShipCapacity > 0 ? maxShipCapacity : 320;
        TotalExportedUnits = totalExportedUnits;
        TotalExportRevenue = totalExportRevenue;
        PartnerSeaportId = partnerSeaportId;

        RestoreCargoDict(outboundCargo, OutboundCargo);
        RestoreCargoDict(transitCargo, TransitCargo);
        RestoreCargoDict(inTransitFromSea, InTransitFromSea);
    }

    private static void RestoreCargoDict(Dictionary<string, int>? src, Dictionary<ResourceType, int> dst)
    {
        dst.Clear();
        if (src == null) return;
        foreach (var (key, amt) in src)
        {
            if (amt > 0 && Enum.TryParse<ResourceType>(key, out var res) && res != ResourceType.None)
                dst[res] = amt;
        }
    }
}
