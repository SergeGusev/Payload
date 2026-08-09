using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperLifecycleIntegrationTests
{
    [Fact]
    public async Task MarketDataUpdater_AuthoritativeLastTradeAtLimit_AppliesAtomicFullMakerFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                price: scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(scenario.Order.Price, fill.Price);
        Assert.Equal(scenario.Order.SizeShares, fill.SizeShares);
        Assert.Equal(sourceTimestampUtc, fill.FilledAtUtc);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), fill.FeeLiquidityRole);
        Assert.Contains("\"source_timestamp_utc\"", fill.Evidence, StringComparison.Ordinal);
        Assert.Contains("\"received_at_utc\"", fill.Evidence, StringComparison.Ordinal);

        var filledOrder = Assert.Single(scenario.Repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, filledOrder.Status);
        Assert.Equal(sourceTimestampUtc, filledOrder.FilledAtUtc);
        var enteredRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, enteredRun.Status);
        Assert.Equal(sourceTimestampUtc, enteredRun.EnteredAtUtc);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), enteredRun.FeeLiquidityRole);
        var position = Assert.Single(scenario.Repository.PaperPositions);
        Assert.Equal(scenario.Order.SizeShares, position.SizeShares);
        Assert.Equal(scenario.Order.Price, position.AveragePrice);
    }

    [Fact]
    public async Task MarketDataUpdater_OnePositionMarkConflict_RecomputesAndFillsExactlyOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var initialPosition = new PaperPosition(
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.Outcome,
            SizeShares: 2m,
            AveragePrice: 0.40m,
            EstimatedValueUsd: 0.80m,
            UnrealizedPnlUsd: 0m,
            UpdatedAtUtc: scenario.Order.CreatedAtUtc.AddSeconds(-1),
            CopiedTraderWallet: scenario.Order.CopiedTraderWallet);
        scenario.Repository.PaperPositions.Add(initialPosition);
        var mutationRequests = new List<MakerGtdPaperFullFillRequest>();
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (request, attempt) =>
        {
            mutationRequests.Add(request);
            if (attempt != 1)
            {
                return;
            }

            var markedPosition = initialPosition with
            {
                EstimatedValueUsd = 1.20m,
                UnrealizedPnlUsd = 0.40m,
                UpdatedAtUtc = now
            };
            scenario.Repository.PaperPositions.Clear();
            scenario.Repository.PaperPositions.Add(markedPosition);
        };
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(2, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Equal(2, mutationRequests.Count);
        Assert.Equal(mutationRequests[0].Fill.Id, mutationRequests[1].Fill.Id);
        Assert.Equal(mutationRequests[0].Fill.Evidence, mutationRequests[1].Fill.Evidence);
        Assert.Equal(mutationRequests[0].FilledOrder, mutationRequests[1].FilledOrder);
        Assert.Equal(mutationRequests[0].EnteredRun, mutationRequests[1].EnteredRun);
        Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        var finalPosition = Assert.Single(scenario.Repository.PaperPositions);
        Assert.Equal(initialPosition.SizeShares + scenario.Order.SizeShares, finalPosition.SizeShares);
        Assert.Equal(7.20m, finalPosition.EstimatedValueUsd);
        Assert.Equal(1.40m, finalPosition.UnrealizedPnlUsd, 10);
        Assert.Equal(now, finalPosition.UpdatedAtUtc);
    }

    [Fact]
    public async Task MarketDataUpdater_NonPositionConflictReason_DoesNotRetry()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (_, attempt) =>
        {
            if (attempt != 1)
            {
                return;
            }

            scenario.Repository.PaperOrders.Clear();
            scenario.Repository.PaperOrders.Add(scenario.Order with
            {
                Status = PaperOrderStatus.Expired
            });
        };
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(1, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_AuthoritativeCurrentBestAskAtLimit_AppliesAtomicFullMakerFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var update = new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: scenario.Order.Price - 0.01m,
            BestAsk: scenario.Order.Price,
            Price: null,
            Size: null,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: sourceTimestampUtc,
            SourceTimestampUtc: sourceTimestampUtc,
            TimestampQuality: MarketDataTimestampQuality.VenueProvided,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: "best-ask-event-1",
            EventFingerprint: "best-ask-event-fingerprint-1");

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(scenario.Order.Price, fill.Price);
        Assert.Equal(scenario.Order.SizeShares, fill.SizeShares);
        Assert.Equal(FeeLiquidityRole.Maker.ToString(), fill.FeeLiquidityRole);
        Assert.Contains("BestAsk", fill.Evidence, StringComparison.Ordinal);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceWithContinuousSubscription_AppliesAtomicFullMakerFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Theory]
    [InlineData("foreign_strategy_id")]
    [InlineData("wrong_order_source")]
    [InlineData("wrong_contract_version")]
    [InlineData("wrong_price_formula")]
    [InlineData("wrong_root_source")]
    [InlineData("wrong_maker_source")]
    [InlineData("wrong_pair_linkage")]
    [InlineData("wrong_common_size")]
    [InlineData("wrong_cap")]
    [InlineData("wrong_price")]
    [InlineData("wrong_notional")]
    [InlineData("wrong_requested_notional")]
    [InlineData("wrong_market_interval")]
    [InlineData("wrong_outcome")]
    [InlineData("wrong_asset")]
    [InlineData("missing_continuity_generation")]
    [InlineData("missing_subscription_session")]
    [InlineData("asset_not_confirmed_live")]
    [InlineData("future_s1_receipt")]
    [InlineData("post_start_acceptance")]
    public void EvidenceParser_PairedContractMutation_FailsClosed(string mutation)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var order = scenario.Order;
        var root = JsonNode.Parse(Assert.IsType<string>(order.RawDecisionJson))!.AsObject();

        switch (mutation)
        {
            case "foreign_strategy_id":
                order = order with { StrategyId = Guid.NewGuid() };
                break;
            case "wrong_order_source":
                order = order with { ExecutionSource = MakerGtdPaperExecutionContract.ExecutionSource };
                break;
            case "wrong_contract_version":
                root["maker_gtd"]!["contract_version"] = "paired_maker_gtd_paper_v0";
                break;
            case "wrong_price_formula":
                root["maker_gtd"]!["price_formula"] = MakerGtdPaperExecutionContract.LegacyPriceFormula;
                break;
            case "wrong_root_source":
                root["execution_source"] = MakerGtdPaperExecutionContract.ExecutionSource;
                break;
            case "wrong_maker_source":
                root["maker_gtd"]!["execution_source"] = MakerGtdPaperExecutionContract.ExecutionSource;
                break;
            case "wrong_pair_linkage":
                root["pair"]!["paired_strategy_id"] = Guid.NewGuid().ToString("D");
                break;
            case "wrong_common_size":
                root["pair"]!["common_requested_size_shares"] = order.SizeShares + 1m;
                break;
            case "wrong_cap":
                root["maker_gtd"]!["maximum_order_price"] = 0.51m;
                break;
            case "wrong_price":
                order = order with { Price = 0.51m };
                break;
            case "wrong_notional":
                order = order with { NotionalUsd = order.NotionalUsd - 0.01m };
                break;
            case "wrong_requested_notional":
                root["maker_gtd"]!["frozen_intent"]!["requested_notional_usd"] =
                    order.NotionalUsd - 0.01m;
                break;
            case "wrong_market_interval":
                root["maker_gtd"]!["market_start_utc"] = order.ExpiresAtUtc.AddMinutes(-3);
                break;
            case "wrong_outcome":
                order = order with { Outcome = "Down" };
                break;
            case "wrong_asset":
                order = order with { AssetId = "foreign-token" };
                break;
            case "missing_continuity_generation":
                root["market_data_status_at_acceptance"]!.AsObject()
                    .Remove("continuity_generation");
                break;
            case "missing_subscription_session":
                root["market_data_status_at_acceptance"]!.AsObject()
                    .Remove("asset_subscription_session_id");
                break;
            case "asset_not_confirmed_live":
                root["market_data_status_at_acceptance"]!["asset_confirmed_live"] = false;
                break;
            case "future_s1_receipt":
                root["maker_gtd"]!["attempts"]![0]!["s1_received_at_utc"] =
                    order.CreatedAtUtc.AddMilliseconds(1);
                break;
            case "post_start_acceptance":
                var postStartOrder = order with
                {
                    CreatedAtUtc = order.ExpiresAtUtc.AddMinutes(-3)
                };
                order = postStartOrder;
                root = JsonNode.Parse(BuildRawDecisionJson(
                    postStartOrder,
                    postStartOrder.CreatedAtUtc))!.AsObject();
                break;
            default:
                throw new InvalidOperationException($"Unknown mutation {mutation}.");
        }

        order = order with { RawDecisionJson = root.ToJsonString() };

        var parsed = MakerGtdPaperOrderEvidenceParser.TryParse(
            order,
            out var evidence,
            out _);

        Assert.False(parsed);
        Assert.Null(evidence);
    }

    [Theory]
    [InlineData(
        MakerGtdPaperExecutionContract.LegacyContractVersion,
        MakerGtdPaperExecutionContract.LegacyPriceFormula)]
    [InlineData(
        MakerGtdPaperExecutionContract.CurrentContractVersion,
        MakerGtdPaperExecutionContract.CurrentPriceFormula)]
    public void EvidenceParser_ReferenceAverageV1AndV2RemainAccepted(
        string contractVersion,
        string priceFormula)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var root = JsonNode.Parse(Assert.IsType<string>(scenario.Order.RawDecisionJson))!.AsObject();
        root["maker_gtd"]!["contract_version"] = contractVersion;
        root["maker_gtd"]!["price_formula"] = priceFormula;
        var order = scenario.Order with { RawDecisionJson = root.ToJsonString() };

        var parsed = MakerGtdPaperOrderEvidenceParser.TryParse(
            order,
            out var evidence,
            out var failureDetail);

        Assert.True(parsed, failureDetail);
        Assert.NotNull(evidence);
    }

    [Fact]
    public void EvidenceParser_ReferenceAverageForeignStrategyId_FailsClosed()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var order = scenario.Order with { StrategyId = Guid.NewGuid() };

        var parsed = MakerGtdPaperOrderEvidenceParser.TryParse(
            order,
            out var evidence,
            out var failureDetail);

        Assert.False(parsed);
        Assert.Null(evidence);
        Assert.Equal("reference_average_strategy_not_approved", failureDetail);
    }

    [Theory]
    [InlineData("maker_gtd_paper_v0", MakerGtdPaperExecutionContract.CurrentPriceFormula)]
    [InlineData(
        MakerGtdPaperExecutionContract.LegacyContractVersion,
        MakerGtdPaperExecutionContract.CurrentPriceFormula)]
    [InlineData(
        MakerGtdPaperExecutionContract.CurrentContractVersion,
        MakerGtdPaperExecutionContract.LegacyPriceFormula)]
    public void EvidenceParser_ReferenceAverageUnknownOrCrossedContractFormula_FailsClosed(
        string contractVersion,
        string priceFormula)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var root = JsonNode.Parse(Assert.IsType<string>(scenario.Order.RawDecisionJson))!.AsObject();
        root["maker_gtd"]!["contract_version"] = contractVersion;
        root["maker_gtd"]!["price_formula"] = priceFormula;
        var order = scenario.Order with { RawDecisionJson = root.ToJsonString() };

        var parsed = MakerGtdPaperOrderEvidenceParser.TryParse(
            order,
            out var evidence,
            out _);

        Assert.False(parsed);
        Assert.Null(evidence);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceAfterReconnect_DoesNotInferFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 3);
        cache.InvalidateAssetSubscriptions("test-shard");
        Assert.True(cache.ConfirmAssetSubscription("test-shard", scenario.Order.AssetId));
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceAfterRecoveredStaleGap_DoesNotInferFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var acceptedStatus = cache.Status;
        cache.UpdateStatus(acceptedStatus with
        {
            Stale = true,
            UpdatedAtUtc = acceptedStatus.UpdatedAtUtc.AddMilliseconds(1)
        });
        cache.InvalidateAssetSubscriptions("test-shard");
        cache.UpdateStatus(acceptedStatus with
        {
            UpdatedAtUtc = acceptedStatus.UpdatedAtUtc.AddMilliseconds(2)
        });
        Assert.True(cache.ConfirmAssetSubscription("test-shard", scenario.Order.AssetId));
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(1, cache.GetConfirmedAssetSubscription(scenario.Order.AssetId).Generation);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceUnrelatedAggregateShardFailure_StillUsesOwningAssetContinuity()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var aggregate = cache.Status;
        cache.UpdateStatus(aggregate with
        {
            ConnectionState = MarketDataConnectionState.Reconnecting,
            Stale = true,
            ReconnectCount = aggregate.ReconnectCount + 1,
            LastDisconnectedUtc = now.AddMilliseconds(-30),
            UpdatedAtUtc = now.AddMilliseconds(-30)
        });
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceAfterConfirmedSubscriptionGenerationChange_DoesNotInferFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        cache.InvalidateAssetSubscriptions("test-shard");
        Assert.True(cache.ConfirmAssetSubscription("test-shard", scenario.Order.AssetId));
        var currentSubscription = cache.GetConfirmedAssetSubscription(scenario.Order.AssetId);
        Assert.True(currentSubscription.ConfirmedLive);
        Assert.Equal(1, currentSubscription.Generation);
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_PairedSourceAfterServiceRestart_DoesNotInferFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(
            now,
            expiresAtUtc: now.AddMinutes(1),
            executionSource: PairedMakerGtdPaperExecutionContract.ExecutionSource);
        var cache = CreateHealthyCache(
            scenario.Order,
            reconnectCount: 2,
            confirmedSubscriptionSessionId: "restarted-market-data-session");
        var currentSubscription = cache.GetConfirmedAssetSubscription(scenario.Order.AssetId);
        Assert.True(currentSubscription.ConfirmedLive);
        Assert.Equal(0, currentSubscription.Generation);
        var updater = CreateUpdater(scenario.Repository, marketDataCache: cache);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_NonCrossingTradeWithCrossedBook_NeverFallsThroughGenericFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = now.AddMilliseconds(-20);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var crossedBook = new OrderBookSnapshot(
            scenario.Order.AssetId,
            [new OrderBookLevel(0.39m, 100m)],
            [new OrderBookLevel(0.40m, 100m)],
            sourceTimestampUtc,
            scenario.Order.ConditionId);
        var update = LastTradeUpdate(
            scenario.Order,
            price: scenario.Order.Price + 0.01m,
            sourceTimestampUtc,
            receivedAtUtc) with
        {
            OrderBookSnapshot = crossedBook,
            BestBid = crossedBook.BestBid,
            BestAsk = crossedBook.BestAsk
        };

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Empty(scenario.Repository.PaperPositions);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_ReceiveTimeFallbackWithCrossedBook_FailsClosed()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var updater = CreateUpdater(scenario.Repository);
        var receivedAtUtc = now.AddMilliseconds(-10);
        var update = new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: 0.39m,
            BestAsk: scenario.Order.Price,
            Price: null,
            Size: null,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: receivedAtUtc,
            SourceTimestampUtc: null,
            TimestampQuality: MarketDataTimestampQuality.ReceiveTimeFallback,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: null,
            EventFingerprint: "fallback-event");

        await updater.ApplyUpdateAsync(update, receivedAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_EventReceivedAtEffectiveExpiry_DoesNotFill()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddMilliseconds(50);
        var scenario = CreateScenario(now, expiresAtUtc);
        var updater = CreateUpdater(scenario.Repository);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc: now.AddMilliseconds(10),
                receivedAtUtc: expiresAtUtc),
            expiresAtUtc);

        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task MarketDataUpdater_SourceAndReceiptBeforeExpiry_StillFillsWhenProcessedAfterExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddSeconds(-1);
        var scenario = CreateScenario(now, expiresAtUtc);
        var updater = CreateUpdater(scenario.Repository);
        var sourceTimestampUtc = expiresAtUtc.AddMilliseconds(-20);
        var receivedAtUtc = expiresAtUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        var fill = Assert.Single(scenario.Repository.PaperFills);
        Assert.Equal(sourceTimestampUtc, fill.FilledAtUtc);
        Assert.Equal(PaperOrderStatus.Filled, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Entered,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task Processor_ContinuousSubscribedWebSocket_ExpiresUnfilledWithoutRestLookup()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Equal(1, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, skippedRun.Status);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
        Assert.Contains("continuous_market_websocket_evidence", skippedRun.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_ReconnectAfterAcceptance_ExpiresAsEvidenceUnavailableWithoutRestLookup()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 3);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains("reconnect_count_changed", skippedRun.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Processor_MakerOrderBeforeExpiry_NeverUsesRestOrGenericFill()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddMinutes(1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var processor = CreateProcessor(scenario.Repository, cache, clobClient);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, result.OrdersFilled);
        Assert.Equal(0, result.OrdersExpired);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
    }

    [Fact]
    public async Task Processor_QueuedPreExpiryMakerUpdate_DefersExpiryUntilQueueDrains()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var queue = new OutstandingMarketDataSideEffectQueue
        {
            HasOutstandingUpdate = true
        };
        var processor = CreateProcessor(scenario.Repository, cache, clobClient, queue);

        var deferredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(0, deferredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        Assert.Equal(0, clobClient.OrderBookCalls);
        Assert.Equal(1, queue.OutstandingChecks);

        queue.HasOutstandingUpdate = false;
        var expiredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, expiredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(0, clobClient.OrderBookCalls);
    }

    [Fact]
    public async Task Processor_ActiveFrameReceipt_DefersExpiryUntilReceiptAdmissionCompletes()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var clobClient = new CountingClobClient();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var processor = CreateProcessor(
            scenario.Repository,
            cache,
            clobClient,
            makerGtdPaperPlacementHandoff: handoff);
        var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        try
        {
            var deferredResult = await processor.ProcessOpenOrdersAsync();

            Assert.Equal(0, deferredResult.OrdersExpired);
            Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Resting,
                Assert.Single(scenario.Repository.StrategyMarketPaperRuns).Status);
        }
        finally
        {
            await receiptAdmission.DisposeAsync();
        }

        var expiredResult = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, expiredResult.OrdersExpired);
        Assert.Equal(PaperOrderStatus.Expired, Assert.Single(scenario.Repository.PaperOrders).Status);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode, skippedRun.SkipReason);
        Assert.Equal(0, clobClient.OrderBookCalls);
    }

    [Fact]
    public async Task Processor_MatchingDeliveryFailure_ExpiresAsEvidenceUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var cache = CreateHealthyCache(scenario.Order, reconnectCount: 2);
        var handoff = new MakerGtdPaperPlacementHandoff();
        handoff.TrackMakerGtdPaperOrder(
            scenario.Order.Id,
            MakerGtdPaperExecutionContract.ExecutionSource);
        handoff.RecordMarketDataFailure(
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.ExpiresAtUtc.AddMilliseconds(-1),
            new HashSet<Guid> { scenario.Order.Id },
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
        var processor = CreateProcessor(
            scenario.Repository,
            cache,
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
        Assert.False(handoff.TryGetMarketDataFailure(
            scenario.Order.Id,
            scenario.Order.AssetId,
            scenario.Order.ConditionId,
            scenario.Order.CreatedAtUtc,
            scenario.Order.ExpiresAtUtc,
            out _));
    }

    [Fact]
    public async Task MarketDataUpdater_FinalPositionConflict_PoisonsLaterExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var eventTimeUtc = now.AddMinutes(-1);
        var scenario = CreateScenario(eventTimeUtc, expiresAtUtc: now.AddSeconds(-1));
        scenario.Repository.BeforeTryApplyMakerGtdPaperFullFill = (request, attempt) =>
        {
            var conflictingPosition = new PaperPosition(
                scenario.Order.AssetId,
                scenario.Order.ConditionId,
                scenario.Order.Outcome,
                SizeShares: attempt,
                AveragePrice: 0.40m,
                EstimatedValueUsd: attempt * 0.49m,
                UnrealizedPnlUsd: attempt * 0.09m,
                UpdatedAtUtc: request.Position.UpdatedAtUtc.AddTicks(attempt),
                CopiedTraderWallet: scenario.Order.CopiedTraderWallet);
            scenario.Repository.PaperPositions.Clear();
            scenario.Repository.PaperPositions.Add(conflictingPosition);
        };
        var handoff = new MakerGtdPaperPlacementHandoff();
        var updater = CreateUpdater(scenario.Repository, handoff);
        var sourceTimestampUtc = eventTimeUtc.AddMilliseconds(-20);
        var receivedAtUtc = eventTimeUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc);

        Assert.Equal(3, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        Assert.Equal(PaperOrderStatus.Pending, Assert.Single(scenario.Repository.PaperOrders).Status);

        var processor = CreateProcessor(
            scenario.Repository,
            CreateHealthyCache(scenario.Order, reconnectCount: 2),
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);
        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataApplyFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarketDataUpdater_LoadExposureFailure_PoisonsEligibleMakerExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var eventTimeUtc = now.AddMinutes(-1);
        var scenario = CreateScenario(eventTimeUtc, expiresAtUtc: now.AddSeconds(-1));
        var handoff = new MakerGtdPaperPlacementHandoff();
        handoff.TrackMakerGtdPaperOrder(
            scenario.Order.Id,
            MakerGtdPaperExecutionContract.ExecutionSource);
        var updater = CreateUpdater(
            scenario.Repository,
            handoff,
            new ThrowingExposureSnapshotCache());
        var sourceTimestampUtc = eventTimeUtc.AddMilliseconds(-20);
        var receivedAtUtc = eventTimeUtc.AddMilliseconds(-10);

        await updater.ApplyUpdateAsync(
            LastTradeUpdate(
                scenario.Order,
                scenario.Order.Price,
                sourceTimestampUtc,
                receivedAtUtc),
            receivedAtUtc,
            new HashSet<Guid> { scenario.Order.Id });

        Assert.Equal(0, scenario.Repository.MakerGtdPaperFullFillAttempts);
        Assert.Empty(scenario.Repository.PaperFills);
        var processor = CreateProcessor(
            scenario.Repository,
            CreateHealthyCache(scenario.Order, reconnectCount: 2),
            new CountingClobClient(),
            makerGtdPaperPlacementHandoff: handoff);

        var result = await processor.ProcessOpenOrdersAsync();

        Assert.Equal(1, result.OrdersExpired);
        var skippedRun = Assert.Single(scenario.Repository.StrategyMarketPaperRuns);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, skippedRun.SkipReason);
        Assert.Contains(
            MakerGtdPaperExecutionContract.MarketDataApplyFailureCode,
            skippedRun.SkipDiagnosticsJson,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing_status")]
    [InlineData("accepted_not_subscribed")]
    [InlineData("accepted_stale")]
    [InlineData("current_not_subscribed")]
    [InlineData("current_last_connected_after_order")]
    [InlineData("current_disconnect_after_order")]
    public void ContinuityEvaluator_MissingOrContradictoryEvidence_FailsClosed(string scenarioName)
    {
        var now = DateTimeOffset.UtcNow;
        var scenario = CreateScenario(now, expiresAtUtc: now.AddSeconds(-1));
        var order = scenario.Order;
        var cache = CreateHealthyCache(order, reconnectCount: 2);
        IReadOnlyCollection<string> subscribedAssetIds = cache.SubscribedAssetIds;
        var currentStatus = cache.Status;

        switch (scenarioName)
        {
            case "missing_status":
                order = order with { RawDecisionJson = "{\"maker_gtd\":{}}" };
                break;
            case "accepted_not_subscribed":
                order = order with
                {
                    RawDecisionJson = BuildRawDecisionJson(
                        order,
                        acceptedAtUtc: order.CreatedAtUtc.AddSeconds(1),
                        assetSubscribed: false)
                };
                break;
            case "accepted_stale":
                order = order with
                {
                    RawDecisionJson = BuildRawDecisionJson(
                        order,
                        acceptedAtUtc: order.CreatedAtUtc.AddSeconds(1),
                        acceptedStale: true)
                };
                break;
            case "current_not_subscribed":
                subscribedAssetIds = [];
                break;
            case "current_last_connected_after_order":
                currentStatus = currentStatus with
                {
                    LastConnectedUtc = order.CreatedAtUtc.AddTicks(1)
                };
                break;
            case "current_disconnect_after_order":
                currentStatus = currentStatus with
                {
                    LastDisconnectedUtc = order.CreatedAtUtc.AddTicks(1)
                };
                break;
        }

        var result = MakerGtdPaperContinuityEvaluator.Evaluate(
            order,
            currentStatus,
            subscribedAssetIds);

        Assert.False(result.Continuous);
        Assert.Equal(MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode, result.ReasonCode);
    }

    private static MakerScenario CreateScenario(
        DateTimeOffset now,
        DateTimeOffset expiresAtUtc,
        string executionSource = MakerGtdPaperExecutionContract.ExecutionSource)
    {
        var repository = new TestAppRepository();
        var pairedVariant = string.Equals(
                executionSource,
                PairedMakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal)
            ? StrategyIds.PairedMakerGtdFirstAcceptingVariants.Single(variant =>
                variant.ReferenceAssetSymbol == "BTC" &&
                variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
            : null;
        var createdAtUtc = pairedVariant is null
            ? now.AddMinutes(-2)
            : expiresAtUtc.AddMinutes(-5);
        var referenceAverageVariant = string.Equals(
                executionSource,
                MakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal)
            ? StrategyIds.UpDown5mStrategyVariants.Single(variant =>
                variant.DecisionThresholdBps == 1m &&
                MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(variant))
            : null;
        var acceptedAtUtc = createdAtUtc;
        var strategyId = pairedVariant?.Id ?? referenceAverageVariant?.Id ?? Guid.NewGuid();
        var order = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:maker-gtd",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-maker-gtd",
            "condition-maker-gtd",
            "Up",
            0.50m,
            10m,
            5m,
            createdAtUtc,
            expiresAtUtc,
            StrategyId: strategyId,
            ExecutionSource: executionSource);
        order = order with
        {
            RawDecisionJson = BuildRawDecisionJson(order, acceptedAtUtc)
        };
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            "market-maker-gtd",
            order.ConditionId,
            "market-maker-gtd",
            "Maker GTD market",
            "Crypto",
            MarketStartUtc: createdAtUtc.AddMinutes(1),
            MarketEndUtc: expiresAtUtc.AddMinutes(1),
            DetectedAtUtc: createdAtUtc.AddSeconds(-1),
            EntryDueAtUtc: createdAtUtc,
            Status: StrategyMarketPaperRunStatuses.Resting,
            SelectedAssetId: order.AssetId,
            SelectedOutcome: order.Outcome,
            EntryPrice: order.Price,
            StakeUsd: order.NotionalUsd,
            SizeShares: order.SizeShares,
            SignalId: order.SignalId,
            PaperOrderId: order.Id,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: acceptedAtUtc);
        repository.PaperOrders.Add(order);
        repository.StrategyMarketPaperRuns.Add(run);
        return new MakerScenario(repository, order, run);
    }

    private static string BuildRawDecisionJson(
        PaperOrder order,
        DateTimeOffset acceptedAtUtc,
        bool assetSubscribed = true,
        bool acceptedStale = false)
    {
        var pairedVariant = StrategyIds.PairedMakerGtdFirstAcceptingVariants
            .SingleOrDefault(variant => variant.Id == order.StrategyId);
        if (string.Equals(
                order.ExecutionSource,
                PairedMakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) &&
            pairedVariant is not null)
        {
            var pairedStrategyId = Assert.IsType<Guid>(pairedVariant.PairedStrategyId);
            var maximumOrderPrice = Assert.IsType<decimal>(pairedVariant.MakerMaximumOrderPrice);
            var commonSizeFrozenAtUtc = acceptedAtUtc.AddSeconds(-1);
            var marketEndUtc = order.ExpiresAtUtc.AddMinutes(1);
            var frozenAtUtc = acceptedAtUtc.AddMilliseconds(-500);
            var frozenIntent = new
            {
                strategy_id = order.StrategyId.ToString("D"),
                decision_id = Guid.NewGuid().ToString("D"),
                condition_id = order.ConditionId,
                asset_id = order.AssetId,
                side = TradeSide.Buy.ToString(),
                post_only = true,
                order_type = "GTD",
                maximum_order_price = maximumOrderPrice,
                limit_price = order.Price,
                requested_notional_usd = order.NotionalUsd,
                requested_size_shares = order.SizeShares,
                target_notional_usd = order.NotionalUsd,
                target_size_shares = order.SizeShares,
                tick_size = 0.01m,
                min_order_size = 1m,
                negative_risk = false,
                decision_snapshot_at_utc = acceptedAtUtc.AddSeconds(-1),
                frozen_at_utc = frozenAtUtc,
                effective_expires_at_utc = order.ExpiresAtUtc,
                clob_gtd_expiration_utc = marketEndUtc
            };
            var acceptedAttempt = new
            {
                attempt_number = 1,
                started_at_utc = acceptedAtUtc.AddSeconds(-2),
                s0 = new
                {
                    asset_id = order.AssetId,
                    condition_id = order.ConditionId,
                    is_current = true,
                    timestamp_is_authoritative = true,
                    best_ask = 0.51m,
                    tick_size = 0.01m
                },
                raw_limit_price = order.Price,
                limit_price = order.Price,
                tick_size = 0.01m,
                frozen_intent = frozenIntent,
                s1 = new
                {
                    asset_id = order.AssetId,
                    condition_id = order.ConditionId,
                    is_current = true,
                    timestamp_is_authoritative = true,
                    best_ask = 0.51m,
                    tick_size = 0.01m
                },
                acceptance_outcome = "AcceptedResting",
                acceptance_reason_code = "paper_post_only_accepted_resting",
                observed_best_ask = 0.51m,
                s1_received_at_utc = frozenAtUtc.AddMilliseconds(250),
                accepted_at_utc = acceptedAtUtc
            };
            return JsonSerializer.Serialize(new
            {
                paper_only = true,
                post_only = true,
                order_type = "GTD",
                execution_source = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                paper_model_label = PairedMakerGtdPaperExecutionContract.MandatoryLabel,
                maker_rebate_modeled = false,
                pair = new
                {
                    pair_id = $"{pairedVariant.ReferenceAssetSymbol}:{order.ConditionId}",
                    strategy_id = order.StrategyId.ToString("D"),
                    paired_strategy_id = pairedStrategyId.ToString("D"),
                    pair_strategy_ids = new[]
                    {
                        order.StrategyId.ToString("D"),
                        pairedStrategyId.ToString("D")
                    }.OrderBy(value => value).ToArray(),
                    common_requested_size_shares = order.SizeShares,
                    common_size_frozen_at_utc = commonSizeFrozenAtUtc,
                    atomic = false,
                    rollback = false
                },
                first_accepting_observation = new
                {
                    phase = "first_accepting_observed",
                    request_started_at_utc = acceptedAtUtc.AddSeconds(-3),
                    response_completed_at_utc = acceptedAtUtc.AddSeconds(-2),
                    first_observed_accepting_at_utc = acceptedAtUtc.AddSeconds(-2),
                    market_id = "market-maker-gtd",
                    condition_id = order.ConditionId,
                    market_slug = "market-maker-gtd",
                    accepting_orders = true,
                    clob_token_ids = new[] { order.AssetId, "paired-peer-token" }
                },
                maker_gtd = new
                {
                    contract_version = PairedMakerGtdPaperExecutionContract.ContractVersion,
                    execution_source = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                    strategy_run_id = Guid.NewGuid().ToString("D"),
                    paper_only = true,
                    post_only = true,
                    order_type = "GTD",
                    maximum_placement_attempts = 10,
                    price_formula = PairedMakerGtdPaperExecutionContract.PriceFormula,
                    maximum_order_price = maximumOrderPrice,
                    market_start_utc = order.ExpiresAtUtc.AddMinutes(-4),
                    market_end_utc = marketEndUtc,
                    effective_expires_at_utc = order.ExpiresAtUtc,
                    clob_gtd_expiration_utc = marketEndUtc,
                    accepted_at_utc = acceptedAtUtc,
                    frozen_intent = frozenIntent,
                    attempts_completed = 1,
                    attempts = new[] { acceptedAttempt }
                },
                market_data_status_at_acceptance = new
                {
                    connection_state = MarketDataConnectionState.Connected.ToString(),
                    stale = acceptedStale,
                    reconnect_count = 2,
                    last_connected_utc = (DateTimeOffset?)order.CreatedAtUtc.AddMinutes(-1),
                    last_disconnected_utc = (DateTimeOffset?)null,
                    asset_subscribed = assetSubscribed,
                    asset_confirmed_live = true,
                    asset_subscription_component = "test-shard",
                    subscribed_assets_count = 2,
                    continuity_generation = 0,
                    asset_subscription_generation = 0,
                    asset_subscription_session_id = "maker-gtd-test-session",
                    accepted_at_utc = acceptedAtUtc
                }
            });
        }

        var referenceAverageVariant = StrategyIds.UpDown5mStrategyVariants
            .SingleOrDefault(variant =>
                variant.Id == order.StrategyId &&
                MakerGtdPaperExecutionContract.IsApprovedCurrentStrategyVariant(variant));
        if (!string.Equals(
                order.ExecutionSource,
                MakerGtdPaperExecutionContract.ExecutionSource,
                StringComparison.Ordinal) ||
            referenceAverageVariant is null)
        {
            return "{}";
        }

        var referenceMarketEndUtc = order.ExpiresAtUtc.AddMinutes(1);
        var referenceFrozenAtUtc = acceptedAtUtc.AddMilliseconds(-500);
        var referenceFrozenIntent = new
        {
            strategy_id = order.StrategyId.ToString("D"),
            decision_id = Guid.NewGuid().ToString("D"),
            condition_id = order.ConditionId,
            asset_id = order.AssetId,
            side = TradeSide.Buy.ToString(),
            post_only = true,
            order_type = "GTD",
            maximum_order_price = StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice,
            limit_price = order.Price,
            requested_notional_usd = order.NotionalUsd,
            requested_size_shares = order.SizeShares,
            target_notional_usd = order.NotionalUsd,
            target_size_shares = order.SizeShares,
            tick_size = 0.01m,
            min_order_size = 1m,
            negative_risk = false,
            decision_snapshot_at_utc = acceptedAtUtc.AddSeconds(-1),
            frozen_at_utc = referenceFrozenAtUtc,
            effective_expires_at_utc = order.ExpiresAtUtc,
            clob_gtd_expiration_utc = referenceMarketEndUtc
        };
        var referenceAcceptedAttempt = new
        {
            attempt_number = 1,
            started_at_utc = acceptedAtUtc.AddSeconds(-2),
            s0 = new
            {
                asset_id = order.AssetId,
                condition_id = order.ConditionId,
                is_current = true,
                timestamp_is_authoritative = true,
                best_bid = 0.49m,
                best_ask = 0.51m,
                tick_size = 0.01m
            },
            raw_limit_price = order.Price,
            limit_price = order.Price,
            tick_size = 0.01m,
            frozen_intent = referenceFrozenIntent,
            s1 = new
            {
                asset_id = order.AssetId,
                condition_id = order.ConditionId,
                is_current = true,
                timestamp_is_authoritative = true,
                best_ask = 0.51m,
                tick_size = 0.01m
            },
            outcome = "accepted_resting",
            reason_code = "paper_post_only_accepted_resting",
            accepted_at_utc = acceptedAtUtc
        };
        return JsonSerializer.Serialize(new
        {
            paper_only = true,
            post_only = true,
            order_type = "GTD",
            order_execution_mode = "GTD",
            execution_source = MakerGtdPaperExecutionContract.ExecutionSource,
            maker_gtd = new
            {
                contract_version = MakerGtdPaperExecutionContract.CurrentContractVersion,
                execution_source = MakerGtdPaperExecutionContract.ExecutionSource,
                strategy_run_id = Guid.NewGuid().ToString("D"),
                paper_only = true,
                post_only = true,
                order_type = "GTD",
                maximum_placement_attempts = 10,
                price_formula = MakerGtdPaperExecutionContract.CurrentPriceFormula,
                maximum_order_price = StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice,
                market_start_utc = order.ExpiresAtUtc.AddMinutes(-4),
                market_end_utc = referenceMarketEndUtc,
                effective_expires_at_utc = order.ExpiresAtUtc,
                clob_gtd_expiration_utc = referenceMarketEndUtc,
                accepted_at_utc = acceptedAtUtc,
                frozen_intent = referenceFrozenIntent,
                attempts_completed = 1,
                attempts = new[] { referenceAcceptedAttempt }
            },
            market_data_status_at_acceptance = new
            {
                connection_state = MarketDataConnectionState.Connected.ToString(),
                stale = acceptedStale,
                reconnect_count = 2,
                last_connected_utc = (DateTimeOffset?)order.CreatedAtUtc.AddMinutes(-1),
                last_disconnected_utc = (DateTimeOffset?)null,
                asset_subscribed = assetSubscribed,
                subscribed_assets_count = 1,
                accepted_at_utc = acceptedAtUtc
            }
        });
    }

    private static PaperTradingMarketDataUpdater CreateUpdater(
        TestAppRepository repository,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null,
        IExposureSnapshotCache? exposureSnapshotCache = null,
        IMarketDataCache? marketDataCache = null)
    {
        return new PaperTradingMarketDataUpdater(
            NullLogger<PaperTradingMarketDataUpdater>.Instance,
            new DefaultPaperTradingEngine(),
            new NoOpPaperSettlementProcessor(),
            exposureSnapshotCache ?? new ExposureSnapshotCache(repository, makerGtdPaperPlacementHandoff),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService: null,
            marketDataWebSocketOptions: new MarketDataWebSocketOptions { StaleAfterSeconds = 30 },
            makerGtdPaperPlacementHandoff: makerGtdPaperPlacementHandoff,
            marketDataCache: marketDataCache);
    }

    private static PaperTradingProcessor CreateProcessor(
        TestAppRepository repository,
        IMarketDataCache marketDataCache,
        CountingClobClient clobClient,
        IMarketDataSideEffectQueue? marketDataSideEffectQueue = null,
        IMakerGtdPaperPlacementHandoff? makerGtdPaperPlacementHandoff = null)
    {
        var options = new MarketDataWebSocketOptions { StaleAfterSeconds = 30 };
        return new PaperTradingProcessor(
            NullLogger<PaperTradingProcessor>.Instance,
            new DefaultPaperTradingEngine(),
            clobClient,
            marketDataCache,
            options,
            new PaperTradingOptions(),
            new ExposureSnapshotCache(repository, makerGtdPaperPlacementHandoff),
            new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions()),
            repository,
            feeAccountingService: null,
            marketDataSideEffectQueue: marketDataSideEffectQueue,
            makerGtdPaperPlacementHandoff: makerGtdPaperPlacementHandoff);
    }

    private static MarketDataCache CreateHealthyCache(
        PaperOrder order,
        int reconnectCount,
        string confirmedSubscriptionSessionId = "maker-gtd-test-session")
    {
        var cache = new MarketDataCache(
            new MarketDataWebSocketOptions(),
            confirmedSubscriptionSessionId);
        cache.ReplaceSubscribedAssets([order.AssetId]);
        cache.AssignAssetSubscriptions("test-shard", [order.AssetId]);
        Assert.True(cache.ConfirmAssetSubscription("test-shard", order.AssetId));
        cache.UpdateStatus(new MarketDataStatusSnapshot(
            "PolymarketMarketWebSocket",
            MarketDataConnectionState.Connected,
            "wss://example.test",
            SubscribedAssetsCount: 1,
            LastMessageUtc: order.ExpiresAtUtc,
            LastConnectedUtc: order.CreatedAtUtc.AddMinutes(-1),
            LastDisconnectedUtc: null,
            reconnectCount,
            Stale: false,
            LastError: null,
            UpdatedAtUtc: order.ExpiresAtUtc));
        return cache;
    }

    private static MarketDataUpdate LastTradeUpdate(
        PaperOrder order,
        decimal price,
        DateTimeOffset sourceTimestampUtc,
        DateTimeOffset receivedAtUtc)
    {
        return new MarketDataUpdate(
            MarketDataEventType.LastTradePrice,
            "last_trade_price",
            order.AssetId,
            order.ConditionId,
            OrderBookSnapshot: null,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Price: price,
            Size: 0m,
            TradeSide.Unknown,
            MarketResolved: false,
            TimestampUtc: sourceTimestampUtc,
            SourceTimestampUtc: sourceTimestampUtc,
            TimestampQuality: MarketDataTimestampQuality.VenueProvided,
            ReceivedAtUtc: receivedAtUtc,
            SourceEventId: "trade-event-1",
            EventFingerprint: "trade-event-fingerprint-1");
    }

    private sealed record MakerScenario(
        TestAppRepository Repository,
        PaperOrder Order,
        StrategyMarketPaperRun Run);

    private sealed class ThrowingExposureSnapshotCache : IExposureSnapshotCache
    {
        public Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated exposure snapshot failure");
        }

        public PaperPosition? GetPaperPosition(string copiedTraderWallet, string assetId) => null;

        public bool TryGetOpenPaperOrderIds(string assetId, out IReadOnlySet<Guid> orderIds)
        {
            orderIds = new HashSet<Guid>();
            return false;
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ApplyPaperOrder(PaperOrder order)
        {
        }

        public void ApplyPaperOrders(IReadOnlyCollection<PaperOrder> orders)
        {
        }

        public void ApplyPaperPosition(PaperPosition position)
        {
        }

        public void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions)
        {
        }

        public void ApplyLiveOrder(LiveOrder order)
        {
        }
    }

    private sealed class CountingClobClient : IPolymarketClobPublicClient
    {
        public int OrderBookCalls { get; private set; }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            OrderBookCalls++;
            throw new InvalidOperationException("Maker-GTD lifecycle must not fetch a REST order book.");
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class OutstandingMarketDataSideEffectQueue : IMarketDataSideEffectQueue
    {
        public bool HasOutstandingUpdate { get; set; }

        public int OutstandingChecks { get; private set; }

        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(
            string component,
            MarketDataUpdate update,
            ActiveMarketAssetSnapshot? activeMarketSnapshot,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? eligiblePaperOrderIds)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(
            MarketWebSocketFrameDiagnostic diagnostic,
            bool important)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError apiError)
        {
            return MarketDataSideEffectEnqueueOutcome.Rejected;
        }

        public MarketDataSideEffectQueueMetrics GetMetrics()
        {
            return new MarketDataSideEffectQueueMetrics(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }

        public bool HasOutstandingPaperOrderUpdate(
            Guid paperOrderId,
            string assetId,
            string conditionId,
            DateTimeOffset acceptedAfterUtc,
            DateTimeOffset expiresBeforeUtc)
        {
            OutstandingChecks++;
            return HasOutstandingUpdate;
        }
    }

    private sealed class NoOpPaperSettlementProcessor : IPaperSettlementProcessor
    {
        public Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }

        public Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
            string? conditionId,
            string? assetId,
            string? winningAssetId,
            string? winningOutcome,
            string? category,
            string settlementSource,
            DateTimeOffset settledAtUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaperSettlementProcessingResult(0, 0, 0, 0));
        }
    }
}
