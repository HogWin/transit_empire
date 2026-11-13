using System;

namespace TransitEmpire.Core.Domain;

/// <summary>
/// Contrato Expresso / Licitatório emitido por uma cidade solicitando um volume de recursos dentro de um prazo.
/// Concede bônus financeiro e de XP ao ser concluído.
/// </summary>
public class LogisticsContract
{
    public int Id { get; }
    public string Title { get; }
    public City TargetCity { get; }
    public ResourceType Resource { get; }
    public int RequiredAmount { get; }
    public int DeliveredAmount { get; private set; }
    public long BonusRewardMoney { get; }
    public int BonusRewardXp { get; }
    public float RemainingHours { get; private set; }
    public string? ClaimedByRivalName { get; private set; }
    public bool IsSpecialConvoy { get; }
    public float FreightMultiplier { get; }

    public bool IsCompleted => DeliveredAmount >= RequiredAmount;
    public bool IsLostToRival => ClaimedByRivalName != null;
    public bool IsExpired => !IsCompleted && !IsLostToRival && RemainingHours <= 0f;
    public float CompletionPercentage => RequiredAmount > 0 ? Math.Clamp((float)DeliveredAmount / RequiredAmount, 0f, 1f) : 1f;

    public LogisticsContract(
        int id,
        string title,
        City targetCity,
        ResourceType resource,
        int requiredAmount,
        long bonusRewardMoney,
        int bonusRewardXp,
        float durationHours = 240f,
        bool isSpecialConvoy = false,
        float freightMultiplier = 1.0f)
    {
        Id = id;
        Title = title;
        TargetCity = targetCity;
        Resource = resource;
        RequiredAmount = requiredAmount;
        BonusRewardMoney = bonusRewardMoney;
        BonusRewardXp = bonusRewardXp;
        RemainingHours = durationHours;
        IsSpecialConvoy = isSpecialConvoy;
        FreightMultiplier = freightMultiplier > 0f ? freightMultiplier : 1.0f;
    }

    /// <summary>
    /// Registra uma entrega parcial ou total para este contrato.
    /// Retorna true no exato momento em que o contrato acaba de ser concluído.
    /// </summary>
    public bool RecordDelivery(ResourceType type, int amount)
    {
        if (IsCompleted || IsExpired || IsLostToRival || type != Resource || amount <= 0)
            return false;

        DeliveredAmount = Math.Min(RequiredAmount, DeliveredAmount + amount);
        return IsCompleted;
    }

    public void AdvanceTime(float deltaHours)
    {
        if (!IsCompleted && !IsLostToRival && RemainingHours > 0f)
        {
            RemainingHours = MathF.Max(0f, RemainingHours - deltaHours);
        }
    }

    internal void ClaimByRival(string rivalName)
    {
        if (!IsCompleted && ClaimedByRivalName == null)
        {
            ClaimedByRivalName = rivalName;
        }
    }

    internal void RestoreProgress(int deliveredAmount, float remainingHours, string? claimedByRivalName = null)
    {
        DeliveredAmount = deliveredAmount;
        RemainingHours = remainingHours;
        ClaimedByRivalName = claimedByRivalName;
    }
}

