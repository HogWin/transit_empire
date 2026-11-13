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
    public float FreightMultiplier { get; private set; }

    // Sprint F4: Leilão de Tarifa/Prazo para Contratos Exclusivos
    public bool IsExclusiveAuction { get; set; }
    public float AuctionRemainingHours { get; set; }
    public float BaseDurationHours { get; }
    public float PlayerBidFreightMultiplier { get; set; } = 1.0f;
    public float PlayerBidDurationHours { get; set; }
    public bool HasPlayerBid { get; set; }
    public string? RivalBidCompanyName { get; set; }
    public float RivalBidFreightMultiplier { get; set; } = 1.0f;
    public float RivalBidDurationHours { get; set; }
    public bool IsWonByPlayerAuction { get; private set; }

    public bool IsAuctionOpen => IsExclusiveAuction && !IsWonByPlayerAuction && !IsLostToRival && !IsCompleted && AuctionRemainingHours > 0f;
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
        BaseDurationHours = durationHours;
        RemainingHours = durationHours;
        PlayerBidDurationHours = durationHours;
        RivalBidDurationHours = durationHours;
        IsSpecialConvoy = isSpecialConvoy;
        FreightMultiplier = freightMultiplier > 0f ? freightMultiplier : 1.0f;
    }

    public static float ComputeBidScore(float freightMultiplier, float durationHours, float baseDurationHours)
    {
        float tariffScore = (2.0f - Math.Clamp(freightMultiplier, 0.50f, 1.50f)) * 100f;
        float speedScore = (MathF.Max(24f, baseDurationHours) / MathF.Max(24f, durationHours)) * 35f;
        return tariffScore + speedScore;
    }

    public void SubmitPlayerBid(float freightMultiplier, float durationHours)
    {
        PlayerBidFreightMultiplier = Math.Clamp(freightMultiplier, 0.55f, 1.25f);
        PlayerBidDurationHours = Math.Clamp(durationHours, 48f, MathF.Max(48f, BaseDurationHours));
        HasPlayerBid = true;
    }

    public void SubmitRivalBid(string rivalName, float freightMultiplier, float durationHours)
    {
        RivalBidCompanyName = rivalName;
        RivalBidFreightMultiplier = Math.Clamp(freightMultiplier, 0.55f, 1.25f);
        RivalBidDurationHours = Math.Clamp(durationHours, 48f, MathF.Max(48f, BaseDurationHours));
    }

    internal void ApplyWonPlayerAuctionTerms()
    {
        IsWonByPlayerAuction = true;
        AuctionRemainingHours = 0f;
        FreightMultiplier = PlayerBidFreightMultiplier;
        RemainingHours = Math.Min(RemainingHours, PlayerBidDurationHours);
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
        if (IsExclusiveAuction && AuctionRemainingHours > 0f && !IsWonByPlayerAuction && !IsLostToRival)
        {
            AuctionRemainingHours = MathF.Max(0f, AuctionRemainingHours - deltaHours);
        }

        if (!IsCompleted && !IsLostToRival && RemainingHours > 0f && !IsAuctionOpen)
        {
            RemainingHours = MathF.Max(0f, RemainingHours - deltaHours);
        }
    }

    internal void ClaimByRival(string rivalName)
    {
        if (!IsCompleted && ClaimedByRivalName == null)
        {
            ClaimedByRivalName = rivalName;
            AuctionRemainingHours = 0f;
        }
    }

    internal void RestoreProgress(
        int deliveredAmount,
        float remainingHours,
        string? claimedByRivalName = null,
        bool isExclusiveAuction = false,
        float auctionRemainingHours = 0f,
        bool hasPlayerBid = false,
        float playerBidFreightMultiplier = 1.0f,
        float playerBidDurationHours = 0f,
        string? rivalBidCompanyName = null,
        float rivalBidFreightMultiplier = 1.0f,
        float rivalBidDurationHours = 0f,
        bool isWonByPlayerAuction = false)
    {
        DeliveredAmount = deliveredAmount;
        RemainingHours = remainingHours;
        ClaimedByRivalName = claimedByRivalName;
        IsExclusiveAuction = isExclusiveAuction;
        AuctionRemainingHours = auctionRemainingHours;
        HasPlayerBid = hasPlayerBid;
        PlayerBidFreightMultiplier = playerBidFreightMultiplier > 0f ? playerBidFreightMultiplier : 1.0f;
        if (playerBidDurationHours > 0f)
            PlayerBidDurationHours = playerBidDurationHours;
        RivalBidCompanyName = rivalBidCompanyName;
        RivalBidFreightMultiplier = rivalBidFreightMultiplier > 0f ? rivalBidFreightMultiplier : 1.0f;
        if (rivalBidDurationHours > 0f)
            RivalBidDurationHours = rivalBidDurationHours;
        IsWonByPlayerAuction = isWonByPlayerAuction;
        if (isWonByPlayerAuction)
            FreightMultiplier = PlayerBidFreightMultiplier;
    }
}

