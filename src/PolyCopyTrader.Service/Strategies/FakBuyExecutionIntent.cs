using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.Strategies;

/// <summary>
/// Immutable economic intent shared by Paper simulation and Live submission for
/// a Polymarket FAK BUY. Fill data is deliberately absent because it is an
/// execution outcome, not a pre-submit constraint.
/// </summary>
internal sealed record FakBuyExecutionIntent(
    Guid StrategyId,
    Guid DecisionId,
    string ConditionId,
    string AssetId,
    TradeSide Side,
    decimal MaximumOrderPrice,
    decimal RequestedNotionalUsd,
    decimal RequestedSizeShares,
    decimal TargetNotionalUsd,
    decimal TargetSizeShares,
    decimal TickSize,
    decimal MinOrderSize,
    bool NegativeRisk,
    DateTimeOffset CreatedAtUtc)
{
    public const string TimeInForce = "FAK";

    public bool PostOnly => false;

    public static FakBuyExecutionIntent Create(
        Guid strategyId,
        Guid decisionId,
        string conditionId,
        string assetId,
        decimal maximumOrderPrice,
        decimal targetNotionalUsd,
        decimal targetSizeShares,
        OrderBookSnapshot? orderBook,
        DateTimeOffset createdAtUtc)
    {
        var tickSize = orderBook?.TickSize ?? 0.01m;
        var effectiveAmounts = new OrderAmountCalculator().CalculateMarketBuy(
            maximumOrderPrice,
            targetNotionalUsd,
            tickSize);
        var effectiveNotionalUsd = (decimal)effectiveAmounts.MakerAmount / 1_000_000m;
        var effectiveSizeShares = (decimal)effectiveAmounts.TakerAmount / 1_000_000m;
        return new FakBuyExecutionIntent(
            strategyId,
            decisionId,
            conditionId,
            assetId,
            TradeSide.Buy,
            maximumOrderPrice,
            targetNotionalUsd,
            targetSizeShares,
            effectiveNotionalUsd,
            effectiveSizeShares,
            tickSize,
            orderBook?.MinOrderSize ?? 1m,
            orderBook?.NegativeRisk ?? false,
            createdAtUtc);
    }
}

internal static class FakBuyExecutionParity
{
    public static FakBuyExecutionValidationResult Validate(FakBuyExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var errors = new OrderAmountCalculator().ValidateMarketBuyOrder(
            intent.Side,
            intent.MaximumOrderPrice,
            intent.TargetNotionalUsd,
            intent.TickSize,
            intent.MinOrderSize);
        return new FakBuyExecutionValidationResult(errors.ToArray());
    }

    public static TakerBuyFillEstimate SimulatePaper(
        FakBuyExecutionIntent intent,
        OrderBookSnapshot orderBook,
        decimal? maximumSpreadAbsolute)
    {
        if (!string.Equals(intent.AssetId, orderBook.AssetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("FAK execution intent asset does not match the Paper order book.");
        }

        var validation = Validate(intent);
        if (!validation.IsValid)
        {
            return TakerBuyFillEstimate.Reject(
                validation.RejectionReason!,
                intent.MaximumOrderPrice,
                orderBook,
                intent.TargetSizeShares);
        }

        return TakerBuyFillEstimator.Estimate(
            orderBook,
            intent.TargetNotionalUsd,
            intent.MaximumOrderPrice,
            intent.MinOrderSize,
            maximumSpreadAbsolute);
    }

    public static ClobV2OrderRequest CreateLiveRequest(
        FakBuyExecutionIntent intent,
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
            intent.MaximumOrderPrice,
            intent.TargetSizeShares,
            intent.TickSize,
            intent.MinOrderSize,
            makerAddress,
            signerAddress,
            signatureType,
            ClobV2OrderType.FAK,
            intent.CreatedAtUtc,
            NegativeRisk: intent.NegativeRisk,
            PostOnly: intent.PostOnly,
            MarketBuyAmountUsd: intent.TargetNotionalUsd);
    }
}

internal sealed record FakBuyExecutionValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string? RejectionReason => IsValid ? null : string.Join("; ", Errors);
}
