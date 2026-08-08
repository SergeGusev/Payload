using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketFeeAccountingServiceTests
{
    private const string ConditionId = "0xabc";
    private static readonly DateTimeOffset NowUtc = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("LegacyUnknown")]
    [InlineData("CalculationUnavailable")]
    [InlineData("PartiallyCalculated")]
    [InlineData("not-a-status")]
    public async Task ApplyToPaperFillAsync_RecalculatesEveryNonFinalStatus(string initialStatus)
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var order = CreatePaperOrder();
        var fill = CreatePaperFill(order.Id, status: initialStatus, role: "Taker");

        var result = await service.ApplyToPaperFillAsync(order, fill);

        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), result.FeeAccountingStatus);
        Assert.Equal(FeeLiquidityRole.Taker.ToString(), result.FeeLiquidityRole);
        Assert.Equal(1.75m, result.FeeUsd);
        Assert.Equal(1, client.CallCount);
    }

    [Theory]
    [InlineData("Calculated")]
    [InlineData("VenueReported")]
    public async Task ApplyToPaperFillAsync_PreservesFinalStatuses(string status)
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var order = CreatePaperOrder();
        var fill = CreatePaperFill(order.Id, status: status, role: "Taker") with
        {
            FeeUsd = 2m,
            FeeCalculationSource = "existing"
        };

        var result = await service.ApplyToPaperFillAsync(order, fill);

        Assert.Same(fill, result);
        Assert.Equal(0, client.CallCount);
    }

    [Theory]
    [InlineData("Taker", "Calculated", 1.75)]
    [InlineData("Maker", "Calculated", 0)]
    [InlineData("Unknown", "CalculationUnavailable", 0)]
    [InlineData("invalid-role", "CalculationUnavailable", 0)]
    public async Task ApplyToPaperFillAsync_HandlesEveryLiquidityRole(
        string role,
        string expectedStatus,
        decimal expectedFeeUsd)
    {
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var order = CreatePaperOrder();

        var result = await service.ApplyToPaperFillAsync(
            order,
            CreatePaperFill(order.Id, role: role));

        Assert.Equal(expectedStatus, result.FeeAccountingStatus);
        Assert.Equal(expectedFeeUsd, result.FeeUsd);
        Assert.Equal(
            role is "Taker" or "Maker" ? role : "Unknown",
            result.FeeLiquidityRole);
    }

    [Fact]
    public async Task Cache_CallerCancellationDoesNotCancelOrPoisonSharedLookup()
    {
        var lookupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubClobClient(async (_, cancellationToken) =>
        {
            lookupStarted.TrySetResult();
            await releaseLookup.Task;
            return FeeEnabledMarket();
        });
        var service = CreateService(client);
        var firstOrder = CreatePaperOrder(conditionId: "0xAbC");
        var secondOrder = CreatePaperOrder(conditionId: "0xaBc");
        using var firstCancellation = new CancellationTokenSource();

        var firstCall = service.ApplyToPaperFillAsync(
            firstOrder,
            CreatePaperFill(firstOrder.Id, role: "Taker"),
            firstCancellation.Token);
        await lookupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        firstCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCall);
        var secondCall = service.ApplyToPaperFillAsync(
            secondOrder,
            CreatePaperFill(secondOrder.Id, role: "Taker"));
        releaseLookup.TrySetResult();
        var secondResult = await secondCall;

        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), secondResult.FeeAccountingStatus);
        Assert.Equal(1, client.CallCount);
        Assert.Single(client.ObservedCancellationTokens);
        Assert.False(client.ObservedCancellationTokens[0].CanBeCanceled);
    }

    [Fact]
    public async Task Cancellation_IsObservedBeforeMissingConditionFallback()
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var order = CreatePaperOrder(conditionId: string.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ApplyToPaperFillAsync(
                order,
                CreatePaperFill(order.Id, role: "Taker"),
                cancellation.Token));

        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Cache_LookupFailureIsSharedAndProducesUnavailableStatus()
    {
        var client = new StubClobClient((_, _) =>
            Task.FromException<PolymarketClobMarketInfo>(new HttpRequestException("lookup failed")));
        var service = CreateService(client);
        var firstOrder = CreatePaperOrder();
        var secondOrder = CreatePaperOrder();

        var firstResult = await service.ApplyToPaperFillAsync(
            firstOrder,
            CreatePaperFill(firstOrder.Id, role: "Taker"));
        var secondResult = await service.ApplyToPaperFillAsync(
            secondOrder,
            CreatePaperFill(secondOrder.Id, role: "Taker"));

        Assert.Equal(FeeAccountingStatus.CalculationUnavailable.ToString(), firstResult.FeeAccountingStatus);
        Assert.Equal(FeeAccountingStatus.CalculationUnavailable.ToString(), secondResult.FeeAccountingStatus);
        Assert.Equal(0m, firstResult.FeeUsd);
        Assert.Equal(0m, secondResult.FeeUsd);
        Assert.Equal(
            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            firstResult.FeeCalculationSource);
        Assert.Equal(
            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            secondResult.FeeCalculationSource);
        Assert.Equal(1, client.CallCount);
    }

    [Theory]
    [InlineData("Unknown", null, "FAK", "Taker", "Calculated", "1.75", "8.25")]
    [InlineData("Unknown", false, "FOK", "Taker", "Calculated", "1.75", "8.25")]
    [InlineData("Unknown", true, "GTC", "Maker", "Calculated", "0", "10")]
    [InlineData("Unknown", false, "GTC", "Unknown", "CalculationUnavailable", "0", null)]
    [InlineData("Maker", false, "FAK", "Unknown", "CalculationUnavailable", "0", null)]
    [InlineData("Taker", true, "GTC", "Unknown", "CalculationUnavailable", "0", null)]
    [InlineData("Maker", true, "GTC", "Maker", "Calculated", "0", "10")]
    [InlineData("Taker", false, "GTC", "Taker", "Calculated", "1.75", "8.25")]
    [InlineData("Unknown", true, "FAK", "Unknown", "CalculationUnavailable", "0", null)]
    public async Task ApplyToLiveOrderAsync_ResolvesRoleAndFinancialFields(
        string persistedRole,
        bool? postOnly,
        string orderType,
        string expectedRole,
        string expectedStatus,
        string expectedFeeUsdText,
        string? expectedNetRealizedPnlUsdText)
    {
        var expectedFeeUsd = decimal.Parse(expectedFeeUsdText, CultureInfo.InvariantCulture);
        var expectedNetRealizedPnlUsd = expectedNetRealizedPnlUsdText is null
            ? (decimal?)null
            : decimal.Parse(expectedNetRealizedPnlUsdText, CultureInfo.InvariantCulture);
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var order = CreateLiveOrder(
            orderType: orderType,
            postOnly: postOnly,
            feeLiquidityRole: persistedRole,
            realizedPnlUsd: 10m);

        var result = await service.ApplyToLiveOrderAsync(order);

        Assert.Equal(expectedRole, result.FeeLiquidityRole);
        Assert.Equal(expectedStatus, result.FeeAccountingStatus);
        Assert.Equal(expectedFeeUsd, result.FeeUsd);
        Assert.Equal(50m, result.FilledNotionalUsd);
        Assert.Equal(50m + expectedFeeUsd, result.CostBasisUsd);
        Assert.Equal(expectedNetRealizedPnlUsd, result.NetRealizedPnlUsd);
    }

    [Fact]
    public async Task ApplyToLiveOrderAsync_FinalStatusStillUpdatesCostAndNetWithoutLookup()
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var order = CreateLiveOrder(
            status: FeeAccountingStatus.VenueReported.ToString(),
            feeUsd: 2m,
            realizedPnlUsd: 8m,
            settlementValueUsd: 60m,
            filledNotionalUsd: 0m,
            costBasisUsd: 0m);

        var result = await service.ApplyToLiveOrderAsync(order);

        Assert.Equal(50m, result.FilledNotionalUsd);
        Assert.Equal(52m, result.CostBasisUsd);
        Assert.Equal(8m, result.NetRealizedPnlUsd);
        Assert.Equal(FeeAccountingStatus.VenueReported.ToString(), result.FeeAccountingStatus);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task ApplyToLiveOrderAsync_RecalculatesModeledFeeWhenCumulativeFillGrows()
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var order = CreateLiveOrder(
            status: FeeAccountingStatus.Calculated.ToString(),
            feeLiquidityRole: FeeLiquidityRole.Taker.ToString(),
            feeUsd: 1.75m,
            filledNotionalUsd: 100m,
            costBasisUsd: 101.75m) with
        {
            SizeShares = 200m,
            NotionalUsd = 100m,
            FilledSize = 200m,
            FeeCalculationSource = PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            FeeRate = 0.07m,
            FeeExponent = 1,
            FeeTakerOnly = true
        };

        var result = await service.ApplyToLiveOrderAsync(order);

        Assert.Equal(3.50m, result.FeeUsd);
        Assert.Equal(103.50m, result.CostBasisUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), result.FeeAccountingStatus);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_PropagatesFillToRunAndMaterialization()
    {
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var order = CreatePaperOrder();
        var fill = CreatePaperFill(order.Id, role: "Taker");
        var run = CreateRun(order.Id, realizedPnlUsd: 10m);
        var materialization = new PaperPositionMaterialization(order, fill, 0.5m, NowUtc);
        var batch = CreateBatch([order], [fill], [run], [materialization]);

        var result = await service.ApplyToEntryBatchAsync(batch);

        var accountedFill = Assert.Single(result.PaperFills);
        Assert.Equal(1.75m, accountedFill.FeeUsd);
        Assert.Equal(accountedFill, Assert.Single(result.PaperPositionMaterializations).Fill);
        var accountedRun = Assert.Single(result.StrategyRuns);
        Assert.Equal(1.75m, accountedRun.FeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), accountedRun.FeeAccountingStatus);
        Assert.Equal(FeeLiquidityRole.Taker.ToString(), accountedRun.FeeLiquidityRole);
        Assert.Equal(8.25m, accountedRun.NetRealizedPnlUsd);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_DoesNotDowngradeFinalFillWhenOrderIsAbsent()
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var absentOrderId = Guid.NewGuid();
        var calculatedAtUtc = NowUtc.AddMinutes(-1);
        var fill = CreatePaperFill(absentOrderId, status: "VenueReported", role: "Taker") with
        {
            FeeUsd = 2m,
            FeeCalculationSource = "venue",
            FeeCalculatedAtUtc = calculatedAtUtc
        };
        var run = CreateRun(absentOrderId, realizedPnlUsd: 10m);
        var batch = CreateBatch([], [fill], [run], []);

        var result = await service.ApplyToEntryBatchAsync(batch);

        Assert.Same(fill, Assert.Single(result.PaperFills));
        var accountedRun = Assert.Single(result.StrategyRuns);
        Assert.Equal(FeeAccountingStatus.VenueReported.ToString(), accountedRun.FeeAccountingStatus);
        Assert.Equal(2m, accountedRun.FeeUsd);
        Assert.Equal(8m, accountedRun.NetRealizedPnlUsd);
        Assert.Equal(calculatedAtUtc, accountedRun.FeeCalculatedAtUtc);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_MissingOrderPropagatesUnavailableToRunAndMaterialization()
    {
        var client = StubClobClient.Returning(FeeEnabledMarket());
        var service = CreateService(client);
        var materializationOrder = CreatePaperOrder();
        var fill = CreatePaperFill(materializationOrder.Id, role: "Taker");
        var run = CreateRun(materializationOrder.Id, realizedPnlUsd: 10m);
        var materialization = new PaperPositionMaterialization(materializationOrder, fill, 0.5m, NowUtc);
        var batch = CreateBatch([], [fill], [run], [materialization]);

        var result = await service.ApplyToEntryBatchAsync(batch);

        var unavailableFill = Assert.Single(result.PaperFills);
        Assert.Equal(FeeAccountingStatus.CalculationUnavailable.ToString(), unavailableFill.FeeAccountingStatus);
        Assert.Equal(
            PolymarketFeeCalculationConstants.MarketInfoUnavailableCalculationSource,
            unavailableFill.FeeCalculationSource);
        Assert.Equal(unavailableFill, Assert.Single(result.PaperPositionMaterializations).Fill);
        var unavailableRun = Assert.Single(result.StrategyRuns);
        Assert.Equal(FeeAccountingStatus.CalculationUnavailable.ToString(), unavailableRun.FeeAccountingStatus);
        Assert.Null(unavailableRun.NetRealizedPnlUsd);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_PartialCoverageKeepsFeeButNotNetPnl()
    {
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var order = CreatePaperOrder();
        var takerFill = CreatePaperFill(order.Id, role: "Taker");
        var unknownFill = CreatePaperFill(order.Id, role: "Unknown") with { Id = Guid.NewGuid() };
        var run = CreateRun(order.Id, realizedPnlUsd: 10m);
        var batch = CreateBatch([order], [takerFill, unknownFill], [run], []);

        var result = await service.ApplyToEntryBatchAsync(batch);

        var partiallyAccountedRun = Assert.Single(result.StrategyRuns);
        Assert.Equal(FeeAccountingStatus.PartiallyCalculated.ToString(), partiallyAccountedRun.FeeAccountingStatus);
        Assert.Equal(1.75m, partiallyAccountedRun.FeeUsd);
        Assert.Equal(FeeLiquidityRole.Unknown.ToString(), partiallyAccountedRun.FeeLiquidityRole);
        Assert.Null(partiallyAccountedRun.NetRealizedPnlUsd);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_DoesNotHideBlankRoleOrSourceInAggregate()
    {
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var order = CreatePaperOrder();
        var venueFill = CreatePaperFill(order.Id, status: "VenueReported", role: string.Empty) with
        {
            FeeUsd = 0.25m,
            FeeCalculationSource = string.Empty
        };
        var calculatedFill = CreatePaperFill(order.Id, role: "Taker") with { Id = Guid.NewGuid() };
        var run = CreateRun(order.Id, realizedPnlUsd: 10m);
        var batch = CreateBatch([order], [venueFill, calculatedFill], [run], []);

        var result = await service.ApplyToEntryBatchAsync(batch);

        var accountedRun = Assert.Single(result.StrategyRuns);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), accountedRun.FeeAccountingStatus);
        Assert.Equal(2m, accountedRun.FeeUsd);
        Assert.Equal("Unknown", accountedRun.FeeLiquidityRole);
        Assert.Equal("mixed", accountedRun.FeeCalculationSource);
        Assert.Null(accountedRun.FeeRate);
        Assert.Equal(8m, accountedRun.NetRealizedPnlUsd);
    }

    [Fact]
    public async Task ApplyToEntryBatchAsync_NoFillsReturnsSameBatch()
    {
        var service = CreateService(StubClobClient.Returning(FeeEnabledMarket()));
        var batch = PaperEntryPersistenceBatch.Empty;

        var result = await service.ApplyToEntryBatchAsync(batch);

        Assert.Same(batch, result);
    }

    private static PolymarketFeeAccountingService CreateService(StubClobClient client)
    {
        return new PolymarketFeeAccountingService(
            NullLogger<PolymarketFeeAccountingService>.Instance,
            client);
    }

    private static PolymarketClobMarketInfo FeeEnabledMarket()
    {
        return new PolymarketClobMarketInfo(
            ConditionId,
            1000,
            1000,
            new PolymarketClobFeeSchedule(0.07m, 1, true),
            "{}");
    }

    private static PaperOrder CreatePaperOrder(
        Guid? id = null,
        string conditionId = ConditionId)
    {
        return new PaperOrder(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "0xwallet",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset",
            conditionId,
            "Up",
            0.5m,
            100m,
            50m,
            NowUtc,
            NowUtc.AddMinutes(5),
            FilledAtUtc: NowUtc);
    }

    private static PaperFill CreatePaperFill(
        Guid paperOrderId,
        string status = "LegacyUnknown",
        string role = "Unknown")
    {
        return new PaperFill(
            Guid.NewGuid(),
            paperOrderId,
            0.5m,
            100m,
            NowUtc,
            "test",
            FeeAccountingStatus: status,
            FeeLiquidityRole: role);
    }

    private static LiveOrder CreateLiveOrder(
        string orderType = "FAK",
        bool? postOnly = false,
        string status = "LegacyUnknown",
        string feeLiquidityRole = "Unknown",
        decimal feeUsd = 0m,
        decimal? realizedPnlUsd = null,
        decimal? settlementValueUsd = null,
        decimal filledNotionalUsd = 50m,
        decimal costBasisUsd = 50m)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "order-id",
            TradeSide.Buy,
            "asset",
            ConditionId,
            "Up",
            0.5m,
            100m,
            50m,
            orderType,
            NowUtc,
            NowUtc.AddMinutes(5),
            NowUtc,
            "matched",
            100m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            NowUtc,
            SettlementValueUsd: settlementValueUsd,
            RealizedPnlUsd: realizedPnlUsd,
            AverageFillPrice: 0.5m,
            FilledNotionalUsd: filledNotionalUsd,
            CostBasisUsd: costBasisUsd,
            FeeUsd: feeUsd,
            PostOnly: postOnly,
            FeeAccountingStatus: status,
            FeeLiquidityRole: feeLiquidityRole);
    }

    private static StrategyMarketPaperRun CreateRun(
        Guid paperOrderId,
        decimal? realizedPnlUsd = null)
    {
        return new StrategyMarketPaperRun(
            Id: Guid.NewGuid(),
            StrategyId: Guid.NewGuid(),
            MarketId: "market",
            ConditionId: ConditionId,
            MarketSlug: "market-slug",
            MarketTitle: "Market",
            Category: "Crypto",
            MarketStartUtc: NowUtc,
            MarketEndUtc: NowUtc.AddMinutes(5),
            DetectedAtUtc: NowUtc,
            EntryDueAtUtc: NowUtc,
            Status: StrategyMarketPaperRunStatuses.Settled,
            SelectedAssetId: "asset",
            SelectedOutcome: "Up",
            EntryPrice: 0.5m,
            StakeUsd: 50m,
            SizeShares: 100m,
            SignalId: Guid.NewGuid(),
            PaperOrderId: paperOrderId,
            EnteredAtUtc: NowUtc,
            SettlementPrice: 0.6m,
            SettlementValueUsd: 60m,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: NowUtc.AddMinutes(5),
            SkipReason: null,
            CreatedAtUtc: NowUtc,
            UpdatedAtUtc: NowUtc);
    }

    private static PaperEntryPersistenceBatch CreateBatch(
        IReadOnlyList<PaperOrder> orders,
        IReadOnlyList<PaperFill> fills,
        IReadOnlyList<StrategyMarketPaperRun> runs,
        IReadOnlyList<PaperPositionMaterialization> materializations)
    {
        return new PaperEntryPersistenceBatch([], orders, fills, [], [], runs)
        {
            PaperPositionMaterializations = materializations
        };
    }

    private sealed class StubClobClient(
        Func<string, CancellationToken, Task<PolymarketClobMarketInfo>> getMarketInfo)
        : IPolymarketClobPublicClient
    {
        private int callCount;

        public int CallCount => Volatile.Read(ref callCount);

        public List<CancellationToken> ObservedCancellationTokens { get; } = [];

        public static StubClobClient Returning(PolymarketClobMarketInfo marketInfo)
        {
            return new StubClobClient((_, _) => Task.FromResult(marketInfo));
        }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<decimal?> GetMidpointAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<decimal?> GetSpreadAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PolymarketClobMarketInfo> GetClobMarketInfoAsync(
            string conditionId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            lock (ObservedCancellationTokens)
            {
                ObservedCancellationTokens.Add(cancellationToken);
            }

            return getMarketInfo(conditionId, cancellationToken);
        }
    }
}
