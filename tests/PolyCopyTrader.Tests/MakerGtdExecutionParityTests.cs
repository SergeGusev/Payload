using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdExecutionParityTests
{
    private static readonly DateTimeOffset FrozenAtUtc =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NormalizesSizeAndNotionalToLimitOrderWireAmounts()
    {
        var intent = CreateIntent(targetSizeShares: 12.34567m);

        Assert.Equal(12.34m, intent.TargetSizeShares);
        Assert.Equal(6.17m, intent.TargetNotionalUsd);
        Assert.Equal(12.34567m, intent.RequestedSizeShares);
        Assert.Equal(6.172835m, intent.RequestedNotionalUsd);
    }

    [Fact]
    public void Validate_AcceptsFrozenPostOnlyGtdIntent()
    {
        var result = MakerGtdExecutionParity.Validate(CreateIntent());

        Assert.True(result.IsValid);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void CreateLiveRequest_PreservesFrozenExecutionShape()
    {
        var intent = CreateIntent();

        var request = MakerGtdExecutionParity.CreateLiveRequest(
            intent,
            "0x1111111111111111111111111111111111111111",
            "0x2222222222222222222222222222222222222222",
            ClobV2SignatureType.EOA);

        Assert.Equal(intent.AssetId, request.TokenId);
        Assert.Equal(TradeSide.Buy, request.Side);
        Assert.Equal(intent.LimitPrice, request.Price);
        Assert.Equal(intent.TargetSizeShares, request.SizeShares);
        Assert.Equal(intent.TickSize, request.TickSize);
        Assert.Equal(intent.MinOrderSize, request.MinOrderSize);
        Assert.Equal(ClobV2OrderType.GTD, request.OrderType);
        Assert.Equal(intent.ClobGtdExpirationUtc, request.GtdExpirationUtc);
        Assert.True(request.PostOnly);
        Assert.Null(request.MarketBuyAmountUsd);
    }

    [Fact]
    public void Validate_RejectsLimitAboveMakerCap()
    {
        var result = MakerGtdExecutionParity.Validate(
            CreateIntent() with { MaximumOrderPrice = 0.49m });

        Assert.False(result.IsValid);
        Assert.Contains("maximum order price", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsWireExpirationNotExactlyOneMinuteAfterEffectiveExpiry()
    {
        var intent = CreateIntent();

        var result = MakerGtdExecutionParity.Validate(
            intent with { ClobGtdExpirationUtc = intent.EffectiveExpiresAtUtc.AddSeconds(61) });

        Assert.False(result.IsValid);
        Assert.Contains("exactly 60 seconds", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsWireLifetimeBelowOfficialMinimum()
    {
        var effectiveExpiry = FrozenAtUtc.AddSeconds(119);

        var result = MakerGtdExecutionParity.Validate(
            CreateIntent(
                effectiveExpiresAtUtc: effectiveExpiry,
                clobGtdExpirationUtc: effectiveExpiry.AddSeconds(60)));

        Assert.False(result.IsValid);
        Assert.Contains("at least 180 seconds", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsSnapshotNewerThanIntentFreeze()
    {
        var intent = CreateIntent();

        var result = MakerGtdExecutionParity.Validate(
            intent with { DecisionSnapshotAtUtc = FrozenAtUtc.AddTicks(1) });

        Assert.False(result.IsValid);
        Assert.Contains("snapshot", result.RejectionReason, StringComparison.OrdinalIgnoreCase);
    }

    private static MakerGtdBuyExecutionIntent CreateIntent(
        decimal targetSizeShares = 12m,
        DateTimeOffset? effectiveExpiresAtUtc = null,
        DateTimeOffset? clobGtdExpirationUtc = null)
    {
        var effectiveExpiry = effectiveExpiresAtUtc ?? FrozenAtUtc.AddMinutes(4);
        var wireExpiry = clobGtdExpirationUtc ?? effectiveExpiry.AddMinutes(1);
        var orderBook = new OrderBookSnapshot(
            "123456",
            [new OrderBookLevel(0.48m, 100m)],
            [new OrderBookLevel(0.52m, 100m)],
            FrozenAtUtc.AddMilliseconds(-10),
            "condition-1",
            MinOrderSize: 5m,
            TickSize: 0.01m,
            NegativeRisk: false);

        return MakerGtdBuyExecutionIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "condition-1",
            orderBook.AssetId,
            maximumOrderPrice: 0.99m,
            limitPrice: 0.50m,
            targetNotionalUsd: targetSizeShares * 0.50m,
            targetSizeShares,
            orderBook,
            FrozenAtUtc,
            effectiveExpiry,
            wireExpiry);
    }
}
