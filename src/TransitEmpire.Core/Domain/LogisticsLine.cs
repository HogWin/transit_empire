using System.Collections.Generic;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Modelo nomeado de Linha Logística Compartilhada (Voxel Tycoon / Transport Fever style).
/// Qualquer edição de paradas é propagada automaticamente para todos os caminhões vinculados à linha.
/// </summary>
public class LogisticsLine
{
    public int Id { get; }
    public string Name { get; set; }
    public List<ScheduleStop> Stops { get; } = new();
    public bool AutoPitStopEnabled { get; set; } = true;
    public FleetLivery? CustomLivery { get; set; }

    // Sprint F2: Controle de Espaçamento de Comboio (Headway) e Auto-Substituição
    public bool HeadwayControlEnabled { get; set; } = false;
    public float MinHeadwayHours { get; set; } = 4.0f;
    public float HoursSinceLastDispatch { get; set; } = 999f;

    public bool AutoReplaceEnabled { get; set; } = false;
    public float AutoReplaceMinCondition { get; set; } = 25f;
    public float AutoReplaceMaxMileageKm { get; set; } = 0f;
    public string? AutoReplaceTargetSpecName { get; set; }

    public LogisticsLine(int id, string name, bool autoPitStopEnabled = true)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? $"Linha #{id:D2}" : name.Trim();
        AutoPitStopEnabled = autoPitStopEnabled;
    }

    public void SetStops(IEnumerable<ScheduleStop> stops)
    {
        Stops.Clear();
        foreach (var stop in stops)
        {
            Stops.Add(stop.Clone());
        }
    }
}
