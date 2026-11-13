namespace TransitEmpire.Core.Domain;

/// <summary>
/// Tipo de localidade referenciada em uma parada da agenda do veículo.
/// </summary>
public enum ScheduleStopKind
{
    Industry,
    City,
    LogisticsHub,
    Garage,
    Seaport,
    RegionalLandmark,
    BusTerminal,
    IntermodalYard
}

/// <summary>
/// Ação executada pelo caminhão ao chegar em uma parada da agenda.
/// </summary>
public enum ScheduleAction
{
    Load,
    Unload,
    PitStop
}

/// <summary>
/// Regra condicional de despacho ao concluir a operação na parada.
/// </summary>
public enum DispatchCondition
{
    Immediate,
    WaitHalfLoad,
    WaitFullLoad
}

/// <summary>
/// Representa uma parada individual dentro de uma agenda multi-paradas (Voxel Tycoon style).
/// </summary>
public class ScheduleStop
{
    public ScheduleStopKind Kind { get; set; }
    public int TargetId { get; set; }
    public ScheduleAction Action { get; set; }
    public DispatchCondition Condition { get; set; } = DispatchCondition.Immediate;
    public ResourceType CargoFilter { get; set; } = ResourceType.None;
    public float MaxWaitHours { get; set; } = 0f; // 0 = sem limite de espera

    public ScheduleStop()
    {
    }

    public ScheduleStop(
        ScheduleStopKind kind,
        int targetId,
        ScheduleAction action,
        DispatchCondition condition = DispatchCondition.Immediate,
        ResourceType cargoFilter = ResourceType.None,
        float maxWaitHours = 0f)
    {
        Kind = kind;
        TargetId = targetId;
        Action = action;
        Condition = condition;
        CargoFilter = cargoFilter;
        MaxWaitHours = maxWaitHours;
    }

    public ScheduleStop Clone() => new(Kind, TargetId, Action, Condition, CargoFilter, MaxWaitHours);
}

