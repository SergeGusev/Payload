using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Strategies;

/// <summary>
/// Immutable economic intent shared by the conservative Paper model and a
/// possible Live Polymarket post-only GTD submission. The current strategy is
/// hard PaperOnly; the Live request projection exists solely to prove that the
/// frozen order shape is supported by the venue without changing it.
/// </summary>
internal sealed record MakerGtdBuyExecutionIntent(
    Guid StrategyId,
    Guid DecisionId,
    string ConditionId,
    string AssetId,
    TradeSide Side,
    decimal MaximumOrderPrice,
    decimal LimitPrice,
    decimal RequestedNotionalUsd,
    decimal RequestedSizeShares,
    decimal TargetNotionalUsd,
    decimal TargetSizeShares,
    decimal TickSize,
    decimal MinOrderSize,
    bool NegativeRisk,
    DateTimeOffset DecisionSnapshotAtUtc,
    DateTimeOffset FrozenAtUtc,
    DateTimeOffset EffectiveExpiresAtUtc,
    DateTimeOffset ClobGtdExpirationUtc)
{
    public const string TimeInForce = "GTD";
    public const int VenueEarlyExpirationSeconds = 60;
    public const int MinimumWireLifetimeSeconds = 180;

    public bool PostOnly => true;

    public static MakerGtdBuyExecutionIntent Create(
        Guid strategyId,
        Guid decisionId,
        string conditionId,
        string assetId,
        decimal maximumOrderPrice,
        decimal limitPrice,
        decimal targetNotionalUsd,
        decimal targetSizeShares,
        OrderBookSnapshot orderBook,
        DateTimeOffset frozenAtUtc,
        DateTimeOffset effectiveExpiresAtUtc,
        DateTimeOffset clobGtdExpirationUtc)
    {
        ArgumentNullException.ThrowIfNull(orderBook);

        var tickSize = orderBook.TickSize ?? 0m;
        var minOrderSize = orderBook.MinOrderSize ?? 0m;
        var amounts = new OrderAmountCalculator().Calculate(
            TradeSide.Buy,
            limitPrice,
            targetSizeShares,
            tickSize);
        var effectiveNotionalUsd = (decimal)amounts.MakerAmount / 1_000_000m;
        var effectiveSizeShares = (decimal)amounts.TakerAmount / 1_000_000m;

        return new MakerGtdBuyExecutionIntent(
            strategyId,
            decisionId,
            conditionId,
            assetId,
            TradeSide.Buy,
            maximumOrderPrice,
            limitPrice,
            targetNotionalUsd,
            targetSizeShares,
            effectiveNotionalUsd,
            effectiveSizeShares,
            tickSize,
            minOrderSize,
            orderBook.NegativeRisk,
            orderBook.SnapshotAtUtc,
            frozenAtUtc,
            effectiveExpiresAtUtc,
            clobGtdExpirationUtc);
    }
}

internal static class MakerGtdExecutionParity
{
    public static MakerGtdExecutionValidationResult Validate(MakerGtdBuyExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var errors = new OrderAmountCalculator().ValidateLimitOrder(
            intent.Side,
            intent.LimitPrice,
            intent.TargetSizeShares,
            intent.TickSize,
            intent.MinOrderSize).ToList();

        if (string.IsNullOrWhiteSpace(intent.ConditionId))
        {
            errors.Add("Condition id is required.");
        }

        if (string.IsNullOrWhiteSpace(intent.AssetId))
        {
            errors.Add("Asset id is required.");
        }

        if (intent.MaximumOrderPrice <= 0m || intent.MaximumOrderPrice >= 1m)
        {
            errors.Add("Maximum order price must be greater than 0 and less than 1.");
        }
        else if (intent.LimitPrice > intent.MaximumOrderPrice)
        {
            errors.Add("Limit price must not exceed the frozen maximum order price.");
        }

        if (!intent.PostOnly)
        {
            errors.Add("Maker GTD intent must be post-only.");
        }

        if (intent.EffectiveExpiresAtUtc <= intent.FrozenAtUtc)
        {
            errors.Add("Effective expiration must be after intent freeze.");
        }

        if (intent.ClobGtdExpirationUtc !=
            intent.EffectiveExpiresAtUtc.AddSeconds(MakerGtdBuyExecutionIntent.VenueEarlyExpirationSeconds))
        {
            errors.Add("CLOB GTD expiration must be exactly 60 seconds after effective Paper expiration.");
        }

        if (intent.ClobGtdExpirationUtc <
            intent.FrozenAtUtc.AddSeconds(MakerGtdBuyExecutionIntent.MinimumWireLifetimeSeconds))
        {
            errors.Add("CLOB GTD expiration must be at least 180 seconds after intent freeze.");
        }

        if (intent.DecisionSnapshotAtUtc > intent.FrozenAtUtc)
        {
            errors.Add("Decision snapshot must not be newer than intent freeze.");
        }

        return new MakerGtdExecutionValidationResult(errors);
    }

    public static ClobV2OrderRequest CreateLiveRequest(
        MakerGtdBuyExecutionIntent intent,
        string makerAddress,
        string signerAddress,
        ClobV2SignatureType signatureType)
    {
        var validation = Validate(intent);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.RejectionReason, nameof(intent));
        }

        return new ClobV2OrderRequest(
            intent.AssetId,
            intent.Side,
            intent.LimitPrice,
            intent.TargetSizeShares,
            intent.TickSize,
            intent.MinOrderSize,
            makerAddress,
            signerAddress,
            signatureType,
            ClobV2OrderType.GTD,
            intent.FrozenAtUtc,
            GtdExpirationUtc: intent.ClobGtdExpirationUtc,
            NegativeRisk: intent.NegativeRisk,
            PostOnly: intent.PostOnly);
    }
}

internal sealed record MakerGtdExecutionValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string? RejectionReason => IsValid ? null : string.Join("; ", Errors);
}
