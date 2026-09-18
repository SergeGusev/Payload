using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Strategies;

/// <summary>A frozen GTD BUY request, including its original local and venue expiration.</summary>
internal sealed record LimitBuyExecutionIntent(
    Guid StrategyId,
    Guid DecisionId,
    string ConditionId,
    string AssetId,
    TradeSide Side,
    decimal RequestedNotionalUsd,
    decimal RequestedSizeShares,
    decimal TargetNotionalUsd,
    decimal TargetSizeShares,
    decimal LimitPrice,
    decimal TickSize,
    decimal MinOrderSize,
    bool NegativeRisk,
    bool PostOnly,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset EffectiveExpiresAtUtc,
    DateTimeOffset ClobGtdExpirationUtc)
{
    public const string TimeInForce = "GTD";

    public static LimitBuyExecutionIntent FromMakerGtd(MakerGtdBuyExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new LimitBuyExecutionIntent(
            intent.StrategyId,
            intent.DecisionId,
            intent.ConditionId,
            intent.AssetId,
            intent.Side,
            intent.RequestedNotionalUsd,
            intent.RequestedSizeShares,
            intent.TargetNotionalUsd,
            intent.TargetSizeShares,
            intent.LimitPrice,
            intent.TickSize,
            intent.MinOrderSize,
            intent.NegativeRisk,
            intent.PostOnly,
            intent.FrozenAtUtc,
            intent.EffectiveExpiresAtUtc,
            intent.ClobGtdExpirationUtc);
    }
}

internal static class LimitBuyExecutionParity
{
    public static LimitBuyExecutionValidationResult Validate(LimitBuyExecutionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var calculator = new OrderAmountCalculator();
        var errors = calculator.ValidateLimitOrder(
            intent.Side,
            intent.LimitPrice,
            intent.TargetSizeShares,
            intent.TickSize,
            intent.MinOrderSize).ToList();
        if (intent.Side != TradeSide.Buy)
        {
            errors.Add("Limit BUY intent must use Buy side.");
        }
        if (string.IsNullOrWhiteSpace(intent.ConditionId))
        {
            errors.Add("Condition id is required.");
        }
        if (string.IsNullOrWhiteSpace(intent.AssetId))
        {
            errors.Add("Asset id is required.");
        }
        if (intent.EffectiveExpiresAtUtc <= intent.CreatedAtUtc)
        {
            errors.Add("Effective expiration must be after intent freeze.");
        }
        if (intent.ClobGtdExpirationUtc < intent.CreatedAtUtc.AddSeconds(MakerGtdBuyExecutionIntent.MinimumWireLifetimeSeconds))
        {
            errors.Add("CLOB GTD expiration must be at least 180 seconds after intent freeze.");
        }
        if (intent.ClobGtdExpirationUtc < intent.EffectiveExpiresAtUtc.AddSeconds(60))
        {
            errors.Add("CLOB GTD expiration must preserve at least the 60-second security buffer.");
        }
        if (errors.Count == 0)
        {
            try
            {
                var amounts = calculator.Calculate(
                    intent.Side, intent.LimitPrice, intent.TargetSizeShares, intent.TickSize);
                if ((decimal)amounts.MakerAmount / 1_000_000m != intent.TargetNotionalUsd ||
                    (decimal)amounts.TakerAmount / 1_000_000m != intent.TargetSizeShares)
                {
                    errors.Add("Frozen GTD amounts do not match the normalized limit order request.");
                }
            }
            catch (ArgumentException ex)
            {
                errors.Add(ex.Message);
            }
        }
        return new LimitBuyExecutionValidationResult(errors);
    }

    public static ClobV2OrderRequest CreateLiveRequest(
        LimitBuyExecutionIntent intent,
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
            intent.CreatedAtUtc,
            GtdExpirationUtc: intent.ClobGtdExpirationUtc,
            NegativeRisk: intent.NegativeRisk,
            PostOnly: intent.PostOnly);
    }
}

internal sealed record LimitBuyExecutionValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public string? RejectionReason => IsValid ? null : string.Join("; ", Errors);
}
