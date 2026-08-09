using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.GammaMarkets;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class PairedMakerGtdFirstAcceptingProcessorTests
{
    [Fact]
    public async Task FirstAccepting_BothLegsAccepted_PersistsIndependentEqualShareOrders()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(1, result.MarketsProcessed);
        Assert.True(
            result.LegsAccepted == 2,
            "Expected both legs accepted. Runs=" + JsonSerializer.Serialize(
                fixture.Repository.StrategyMarketPaperRuns.Select(run => new
                {
                    run.SelectedOutcome,
                    run.Status,
                    run.SkipReason,
                    run.SkipDiagnosticsJson
                })));
        Assert.Equal(0, result.LegsSkipped);
        Assert.Equal(2, fixture.Repository.PaperEntryPersistenceBatchCalls);
        var orders = fixture.Repository.PaperOrders.OrderBy(order => order.Outcome).ToArray();
        Assert.Equal(2, orders.Length);
        Assert.Equal(orders[0].SizeShares, orders[1].SizeShares);
        Assert.All(orders, order => Assert.Equal(6.13m, order.SizeShares));
        Assert.All(orders, order => Assert.Equal(
            fixture.Candidate.Market.EndDateUtc!.Value.AddMinutes(-1),
            order.ExpiresAtUtc));
        Assert.Equal(0.49m, Assert.Single(orders, order => order.Outcome == "Down").Price);
        Assert.Equal(0.50m, Assert.Single(orders, order => order.Outcome == "Up").Price);
        Assert.All(orders, order =>
        {
            Assert.Equal(PaperOrderStatus.Pending, order.Status);
            Assert.Equal(PairedMakerGtdPaperExecutionContract.ExecutionSource, order.ExecutionSource);
            using var document = JsonDocument.Parse(Assert.IsType<string>(order.RawDecisionJson));
            var root = document.RootElement;
            Assert.Equal(StrategyIds.OptimisticTouchNoDepthPaperLabel, root.GetProperty("paper_model_label").GetString());
            Assert.False(root.GetProperty("maker_rebate_modeled").GetBoolean());
            Assert.Equal(
                PairedMakerGtdPaperExecutionContract.ContractVersion,
                root.GetProperty("maker_gtd").GetProperty("contract_version").GetString());
            Assert.True(root.GetProperty("market_data_status_at_acceptance").TryGetProperty(
                "continuity_generation",
                out _));
            Assert.True(root.GetProperty("market_data_status_at_acceptance").TryGetProperty(
                "asset_subscription_generation",
                out _));
            Assert.False(string.IsNullOrWhiteSpace(
                root.GetProperty("market_data_status_at_acceptance")
                    .GetProperty("asset_subscription_session_id")
                    .GetString()));
            Assert.True(root.GetProperty("market_data_status_at_acceptance")
                .GetProperty("asset_confirmed_live")
                .GetBoolean());
            Assert.Equal(
                "paired-test-shard",
                root.GetProperty("market_data_status_at_acceptance")
                    .GetProperty("asset_subscription_component")
                    .GetString());
            Assert.True(
                MakerGtdPaperOrderEvidenceParser.TryParse(
                    order,
                    out var evidence,
                    out var failureDetail),
                failureDetail);
            Assert.NotNull(evidence);
        });
        Assert.Equal(2, fixture.Repository.StrategyMarketPaperRuns.Count(run =>
            run.Status == StrategyMarketPaperRunStatuses.Resting));
    }

    [Fact]
    public async Task FirstAccepting_PublishesOnlyAfterOrderIsVisibleInExposureCache()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var innerHandoff = new MakerGtdPaperPlacementHandoff();
        var orderingHandoff = new PublicationOrderingHandoff(innerHandoff);
        var exposureCache = new ExposureSnapshotCache(fixture.Repository, orderingHandoff);
        orderingHandoff.IsOrderVisible = orderId =>
            fixture.Candidate.Market.ClobTokenIds.Any(assetId =>
                exposureCache.TryGetOpenPaperOrderIds(assetId, out var orderIds) &&
                orderIds.Contains(orderId));
        var processor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions(),
            fixture.ClobClient,
            fixture.Cache,
            fixture.Registry,
            exposureCache,
            orderingHandoff,
            new StaticStrategyStateProvider(),
            fixture.Repository,
            fixture.TimeProvider);

        var result = await processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(2, result.LegsAccepted);
        Assert.Equal(2, orderingHandoff.PublishedOrderIds.Count);
        Assert.All(fixture.Repository.PaperOrders, order =>
            Assert.Contains(order.Id, orderingHandoff.PublishedOrderIds));
    }

    [Fact]
    public async Task FirstAccepting_DownS1AlwaysCrosses_KeepsAcceptedUpAndSkipsOnlyDown()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: true);

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(1, result.LegsAccepted);
        Assert.Equal(1, result.LegsSkipped);
        var order = Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(fixture.Repository.StrategyMarketPaperRuns, run => run.SelectedOutcome == "Up").Status);
        var downRun = Assert.Single(
            fixture.Repository.StrategyMarketPaperRuns,
            run => run.SelectedOutcome == "Down");
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, downRun.Status);
        Assert.Equal("paired_maker_gtd_post_only_attempts_exhausted", downRun.SkipReason);
        Assert.Equal(10, fixture.ClobClient.DownS1Calls);
    }

    [Fact]
    public async Task DueRecovery_ReprotectsStoredMarketAssetsBeforeWaitingForSubscription()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false, marketDataReady: false);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        foreach (var variant in StrategyIds.PairedMakerGtdFirstAcceptingVariants.Where(variant =>
                     variant.ReferenceAssetSymbol == "BTC"))
        {
            fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
                fixture.Candidate.Market,
                variant,
                fixture.NowUtc));
        }

        var result = await fixture.Processor.ProcessDueAsync();
        var retained = fixture.Registry.RetainAssets([]);

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, retained.Removed);
        Assert.Contains("token-up", fixture.Registry.GetAssetIds(), StringComparer.Ordinal);
        Assert.Contains("token-down", fixture.Registry.GetAssetIds(), StringComparer.Ordinal);
        Assert.Empty(fixture.Repository.PaperOrders);
    }

    [Fact]
    public async Task FirstAccepting_SequentialS0ReadMakesPeerStale_RevalidatesBeforeCommonFreeze()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            advanceBeforeFirstDownRead: TimeSpan.FromSeconds(2),
            maxQuoteAgeMilliseconds: 1_000);

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(2, result.LegsAccepted);
        var upOrder = Assert.Single(fixture.Repository.PaperOrders, order => order.Outcome == "Up");
        using var document = JsonDocument.Parse(Assert.IsType<string>(upOrder.RawDecisionJson));
        var attempts = document.RootElement.GetProperty("maker_gtd").GetProperty("attempts");
        Assert.True(attempts.GetArrayLength() >= 2);
        var firstAttempt = attempts[0];
        Assert.Equal("s0_rejected_at_common_freeze", firstAttempt.GetProperty("outcome").GetString());
        Assert.Equal("paired_maker_gtd_s0_book_not_current", firstAttempt.GetProperty("reason_code").GetString());
    }

    [Fact]
    public async Task DueRecovery_AfterFirstLegPersistenceAndMissingOpenPeer_ReusesFrozenCommonShares()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            cancelDownOnFirstS1: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate));

        var acceptedUp = Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal("Up", acceptedUp.Outcome);
        var downRun = Assert.Single(
            fixture.Repository.StrategyMarketPaperRuns,
            run => run.SelectedOutcome == "Down");
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, downRun.Status);
        string frozenAtUtc;
        using (var continuation = JsonDocument.Parse(Assert.IsType<string>(downRun.SkipDiagnosticsJson)))
        {
            var pair = continuation.RootElement.GetProperty("pair");
            Assert.Equal(
                acceptedUp.SizeShares,
                pair.GetProperty("common_requested_size_shares").GetDecimal());
            frozenAtUtc = Assert.IsType<string>(
                pair.GetProperty("common_size_frozen_at_utc").GetString());
            Assert.False(string.IsNullOrWhiteSpace(frozenAtUtc));
        }

        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        fixture.Repository.PaperOrders.Clear();
        var recoveryHandoff = new MakerGtdPaperPlacementHandoff();
        var recoveryClobClient = new PairedBookClobClient(
            fixture.TimeProvider,
            fixture.Candidate.Market.ConditionId,
            downWouldCrossOnS1: false,
            advanceBeforeFirstDownRead: null,
            cancelDownOnFirstS1: false);
        var recoveryProcessor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions(),
            recoveryClobClient,
            fixture.Cache,
            fixture.Registry,
            new ExposureSnapshotCache(fixture.Repository, recoveryHandoff),
            recoveryHandoff,
            new StaticStrategyStateProvider(),
            fixture.Repository,
            fixture.TimeProvider);

        var result = await recoveryProcessor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(1, result.LegsAccepted);
        var recoveredDown = Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal("Down", recoveredDown.Outcome);
        Assert.Equal(acceptedUp.SizeShares, recoveredDown.SizeShares);
        using (var recoveredDecision = JsonDocument.Parse(
                   Assert.IsType<string>(recoveredDown.RawDecisionJson)))
        {
            Assert.Equal(
                frozenAtUtc,
                recoveredDecision.RootElement
                    .GetProperty("pair")
                    .GetProperty("common_size_frozen_at_utc")
                    .GetString());
        }
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Resting,
            Assert.Single(
                fixture.Repository.StrategyMarketPaperRuns,
                run => run.SelectedOutcome == "Down").Status);
    }

    [Fact]
    public async Task DueRecovery_ExistingPeerSizeDisagreesWithFrozenCommonSize_FailsClosed()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            cancelDownOnFirstS1: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate));

        var acceptedUp = Assert.Single(fixture.Repository.PaperOrders);
        fixture.Repository.PaperOrders[0] = acceptedUp with
        {
            SizeShares = acceptedUp.SizeShares + 1m
        };
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        var recoveryHandoff = new MakerGtdPaperPlacementHandoff();
        var recoveryClobClient = new PairedBookClobClient(
            fixture.TimeProvider,
            fixture.Candidate.Market.ConditionId,
            downWouldCrossOnS1: false,
            advanceBeforeFirstDownRead: null,
            cancelDownOnFirstS1: false);
        var recoveryProcessor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions(),
            recoveryClobClient,
            fixture.Cache,
            fixture.Registry,
            new ExposureSnapshotCache(fixture.Repository, recoveryHandoff),
            recoveryHandoff,
            new StaticStrategyStateProvider(),
            fixture.Repository,
            fixture.TimeProvider);

        var result = await recoveryProcessor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, recoveryClobClient.TotalOrderBookCalls);
        Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Observed,
            Assert.Single(
                fixture.Repository.StrategyMarketPaperRuns,
                run => run.SelectedOutcome == "Down").Status);
    }

    [Fact]
    public async Task DueRecovery_OneObservedLegWithOpenPeerButNoFrozenCommonSize_FailsClosed()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var initial = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);
        Assert.Equal(2, initial.LegsAccepted);
        var acceptedUp = Assert.Single(fixture.Repository.PaperOrders, order => order.Outcome == "Up");

        fixture.Repository.PaperOrders.Clear();
        fixture.Repository.PaperOrders.Add(acceptedUp);
        fixture.Repository.StrategyMarketPaperRuns.Clear();
        var downVariant = Assert.Single(
            StrategyIds.PairedMakerGtdFirstAcceptingVariants,
            variant => variant.ReferenceAssetSymbol == "BTC" &&
                variant.FixedOutcome == BtcUpDownFixedOutcome.Down);
        fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            fixture.Candidate.Market,
            downVariant,
            fixture.NowUtc));
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);

        var recoveryHandoff = new MakerGtdPaperPlacementHandoff();
        var recoveryClobClient = new PairedBookClobClient(
            fixture.TimeProvider,
            fixture.Candidate.Market.ConditionId,
            downWouldCrossOnS1: false,
            advanceBeforeFirstDownRead: null);
        var recoveryProcessor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions(),
            recoveryClobClient,
            fixture.Cache,
            fixture.Registry,
            new ExposureSnapshotCache(fixture.Repository, recoveryHandoff),
            recoveryHandoff,
            new StaticStrategyStateProvider(),
            fixture.Repository,
            fixture.TimeProvider);

        var result = await recoveryProcessor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, recoveryClobClient.TotalOrderBookCalls);
        Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal(
            StrategyMarketPaperRunStatuses.Observed,
            Assert.Single(fixture.Repository.StrategyMarketPaperRuns).Status);
    }

    [Fact]
    public async Task DueRecovery_FutureFrozenCommonSizeTimestamp_FailsClosedWithoutClobRead()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        foreach (var variant in StrategyIds.PairedMakerGtdFirstAcceptingVariants.Where(variant =>
                     variant.ReferenceAssetSymbol == "BTC"))
        {
            fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
                fixture.Candidate.Market,
                variant,
                fixture.NowUtc,
                CreateContinuationEvidence(
                    fixture.Candidate.Market,
                    variant,
                    fixture.NowUtc,
                    attemptsCompleted: 1,
                    frozenAtUtc: fixture.NowUtc.AddMinutes(1))));
        }

        var result = await fixture.Processor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
    }

    [Fact]
    public async Task DueRecovery_PersistedAttemptsAreRetainedAndGloballyCappedAtTen()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: true);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        foreach (var variant in StrategyIds.PairedMakerGtdFirstAcceptingVariants.Where(variant =>
                     variant.ReferenceAssetSymbol == "BTC"))
        {
            fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
                fixture.Candidate.Market,
                variant,
                fixture.NowUtc,
                CreateContinuationEvidence(
                    fixture.Candidate.Market,
                    variant,
                    fixture.NowUtc,
                    attemptsCompleted: 8)));
        }

        var result = await fixture.Processor.ProcessDueAsync();

        Assert.Equal(1, result.LegsAccepted);
        Assert.Equal(1, result.LegsSkipped);
        Assert.Equal(2, fixture.ClobClient.DownS1Calls);
        var upOrder = Assert.Single(fixture.Repository.PaperOrders);
        Assert.Equal("Up", upOrder.Outcome);
        using (var accepted = JsonDocument.Parse(Assert.IsType<string>(upOrder.RawDecisionJson)))
        {
            var makerGtd = accepted.RootElement.GetProperty("maker_gtd");
            Assert.Equal(9, makerGtd.GetProperty("attempts_completed").GetInt32());
            var attempts = makerGtd.GetProperty("attempts");
            Assert.Equal(9, attempts.GetArrayLength());
            Assert.Equal("persisted-Up-1", attempts[0].GetProperty("persisted_marker").GetString());
            Assert.Equal(
                "original-observation",
                accepted.RootElement.GetProperty("first_accepting_observation")
                    .GetProperty("observation_marker")
                    .GetString());
        }

        var downRun = Assert.Single(
            fixture.Repository.StrategyMarketPaperRuns,
            run => run.SelectedOutcome == "Down");
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, downRun.Status);
        using var skipped = JsonDocument.Parse(Assert.IsType<string>(downRun.SkipDiagnosticsJson));
        var skippedMakerGtd = skipped.RootElement.GetProperty("maker_gtd");
        Assert.Equal(10, skippedMakerGtd.GetProperty("attempts_completed").GetInt32());
        var skippedAttempts = skippedMakerGtd.GetProperty("attempts");
        Assert.Equal(10, skippedAttempts.GetArrayLength());
        Assert.Equal("persisted-Down-1", skippedAttempts[0].GetProperty("persisted_marker").GetString());
        Assert.Equal(10, skippedAttempts[9].GetProperty("attempt_number").GetInt32());
        Assert.Equal(
            "original-observation",
            skipped.RootElement.GetProperty("first_accepting_observation")
                .GetProperty("observation_marker")
                .GetString());
    }

    [Fact]
    public async Task DueRecovery_MalformedContinuationFailsClosedWithoutClobRead()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        var variants = StrategyIds.PairedMakerGtdFirstAcceptingVariants
            .Where(variant => variant.ReferenceAssetSymbol == "BTC")
            .ToArray();
        fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            fixture.Candidate.Market,
            variants[0],
            fixture.NowUtc,
            "{not-json"));
        fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            fixture.Candidate.Market,
            variants[1],
            fixture.NowUtc,
            CreateContinuationEvidence(
                fixture.Candidate.Market,
                variants[1],
                fixture.NowUtc,
                attemptsCompleted: 1)));

        var result = await fixture.Processor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.All(fixture.Repository.StrategyMarketPaperRuns, run =>
            Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status));
    }

    [Fact]
    public async Task DueRecovery_MismatchedContinuationFailsClosedWithoutClobRead()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        var variants = StrategyIds.PairedMakerGtdFirstAcceptingVariants
            .Where(variant => variant.ReferenceAssetSymbol == "BTC")
            .ToArray();
        fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            fixture.Candidate.Market,
            variants[0],
            fixture.NowUtc,
            CreateContinuationEvidence(
                fixture.Candidate.Market,
                variants[0],
                fixture.NowUtc,
                attemptsCompleted: 1,
                persistedStrategyId: Guid.Parse("ffffffff-ffff-4fff-8fff-ffffffffffff"))));
        fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            fixture.Candidate.Market,
            variants[1],
            fixture.NowUtc,
            CreateContinuationEvidence(
                fixture.Candidate.Market,
                variants[1],
                fixture.NowUtc,
                attemptsCompleted: 1)));

        var result = await fixture.Processor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.All(fixture.Repository.StrategyMarketPaperRuns, run =>
            Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status));
    }

    [Fact]
    public async Task DueRecovery_IncompleteFirstAcceptingObservationFailsClosedWithoutClobRead()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        fixture.Repository.PolymarketGammaMarkets.Add(fixture.Candidate.Market);
        var incompleteObservation = CreateInitialObservationEvidence(
            fixture.Candidate.Market,
            fixture.NowUtc);
        incompleteObservation.Remove("condition_id");
        foreach (var variant in StrategyIds.PairedMakerGtdFirstAcceptingVariants.Where(variant =>
                     variant.ReferenceAssetSymbol == "BTC"))
        {
            fixture.Repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
                fixture.Candidate.Market,
                variant,
                fixture.NowUtc,
                incompleteObservation.ToJsonString()));
        }

        var result = await fixture.Processor.ProcessDueAsync();

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.All(fixture.Repository.StrategyMarketPaperRuns, run =>
            Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status));
    }

    [Fact]
    public async Task FirstAccepting_ReadOnlyRuntimeFailsClosedBeforePersistenceOrClobRead()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            botMode: BotMode.ReadOnly);

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(0, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.StrategyMarketPaperRuns);
        Assert.Empty(fixture.Repository.PaperOrders);
    }

    [Theory]
    [InlineData("malformed_slug")]
    [InlineData("event_start_mismatch")]
    public async Task FirstAccepting_MarketIdentityMutationFailsClosedBeforePersistenceOrClobRead(
        string mutation)
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var market = mutation switch
        {
            "malformed_slug" => fixture.Candidate.Market with
            {
                Slug = "btc-updown-5m-garbage"
            },
            "event_start_mismatch" => fixture.Candidate.Market with
            {
                EventStartTimeUtc = fixture.Candidate.Market.EventStartTimeUtc!.Value.AddMinutes(5)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(
            fixture.Candidate with { Market = market });

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.StrategyMarketPaperRuns);
        Assert.Empty(fixture.Repository.PaperOrders);
    }

    [Fact]
    public async Task FirstAccepting_PlannedButUnconfirmedSubscriptionsFailClosedBeforeClobRead()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            confirmLiveSubscriptions: false);

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.Equal(2, fixture.Repository.StrategyMarketPaperRuns.Count(run =>
            run.Status == StrategyMarketPaperRunStatuses.Observed));
    }

    [Fact]
    public async Task FirstAccepting_S1ReceiptBeforeLocalAcceptance_PreservesBothOrderedTimestamps()
    {
        var fixture = CreateFixture(
            downWouldCrossOnS1: false,
            advanceAfterS1Snapshot: TimeSpan.FromMilliseconds(250));

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(2, result.LegsAccepted);
        var upOrder = Assert.Single(fixture.Repository.PaperOrders, order => order.Outcome == "Up");
        using var document = JsonDocument.Parse(Assert.IsType<string>(upOrder.RawDecisionJson));
        var root = document.RootElement;
        var attempts = root.GetProperty("maker_gtd").GetProperty("attempts");
        var acceptedAttempt = attempts[attempts.GetArrayLength() - 1];
        var s1ReceivedAtUtc = acceptedAttempt.GetProperty("s1_received_at_utc").GetDateTimeOffset();
        var acceptedAtUtc = acceptedAttempt.GetProperty("accepted_at_utc").GetDateTimeOffset();
        Assert.True(s1ReceivedAtUtc < acceptedAtUtc);
        Assert.Equal(upOrder.CreatedAtUtc, acceptedAtUtc);
        Assert.Equal(
            acceptedAtUtc,
            root.GetProperty("market_data_status_at_acceptance")
                .GetProperty("accepted_at_utc")
                .GetDateTimeOffset());
        Assert.True(
            MakerGtdPaperOrderEvidenceParser.TryParse(
                upOrder,
                out _,
                out var failureDetail),
            failureDetail);
    }

    [Fact]
    public async Task FirstAccepting_ConfirmedGenerationChangesBetweenAcceptanceReads_FailsClosed()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var racingCache = new RacingConfirmedMarketDataCache(
            fixture.Cache,
            "paired-test-shard",
            fixture.Candidate.Market.ClobTokenIds,
            triggerConfirmedRead: 15);
        var handoff = new MakerGtdPaperPlacementHandoff();
        var processor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions(),
            fixture.ClobClient,
            racingCache,
            fixture.Registry,
            new ExposureSnapshotCache(fixture.Repository, handoff),
            handoff,
            new StaticStrategyStateProvider(),
            fixture.Repository,
            fixture.TimeProvider);

        var result = await processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.True(racingCache.RaceTriggered);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.True(fixture.ClobClient.TotalOrderBookCalls > 0);
        Assert.Equal(2, fixture.Repository.StrategyMarketPaperRuns.Count(run =>
            run.Status == StrategyMarketPaperRunStatuses.Observed));
    }

    [Fact]
    public async Task FirstAccepting_UnrelatedAggregateShardFailureDoesNotBlockConfirmedPairAssets()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var status = fixture.Cache.Status;
        fixture.Cache.UpdateStatus(status with
        {
            ConnectionState = MarketDataConnectionState.Reconnecting,
            Stale = true,
            ReconnectCount = status.ReconnectCount + 1,
            LastDisconnectedUtc = fixture.NowUtc.AddMilliseconds(1),
            UpdatedAtUtc = fixture.NowUtc.AddMilliseconds(1)
        });

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(fixture.Candidate);

        Assert.Equal(2, result.LegsAccepted);
        Assert.Equal(2, fixture.Repository.PaperOrders.Count);
        Assert.All(fixture.Repository.PaperOrders, order =>
            Assert.True(MakerGtdPaperOrderEvidenceParser.TryParse(
                order,
                out _,
                out var failureDetail), failureDetail));
    }

    [Fact]
    public async Task FirstAccepting_OutcomeCasingMismatch_FailsClosedBeforeClobRead()
    {
        var fixture = CreateFixture(downWouldCrossOnS1: false);
        var candidate = fixture.Candidate with
        {
            Market = fixture.Candidate.Market with { Outcomes = ["UP", "Down"] }
        };

        var result = await fixture.Processor.ProcessFirstAcceptingMarketAsync(candidate);

        Assert.Equal(1, result.MarketsProcessed);
        Assert.Equal(0, result.LegsAccepted);
        Assert.Equal(0, fixture.ClobClient.TotalOrderBookCalls);
        Assert.Empty(fixture.Repository.PaperOrders);
        Assert.Empty(fixture.Repository.StrategyMarketPaperRuns);
    }

    private static Fixture CreateFixture(
        bool downWouldCrossOnS1,
        bool marketDataReady = true,
        TimeSpan? advanceBeforeFirstDownRead = null,
        int maxQuoteAgeMilliseconds = 30_000,
        bool cancelDownOnFirstS1 = false,
        bool confirmLiveSubscriptions = true,
        TimeSpan? advanceAfterS1Snapshot = null,
        BotMode botMode = BotMode.Paper)
    {
        var marketStartUtc = new DateTimeOffset(2026, 8, 10, 15, 30, 0, TimeSpan.Zero);
        var nowUtc = marketStartUtc.AddHours(-23).AddMinutes(-50);
        var market = CreateMarket(nowUtc, marketStartUtc);
        var repository = new TestAppRepository();
        var handoff = new MakerGtdPaperPlacementHandoff();
        var cache = new MarketDataCache(new MarketDataWebSocketOptions());
        if (marketDataReady)
        {
            cache.AssignAssetSubscriptions("paired-test-shard", market.ClobTokenIds);
            cache.ReplaceSubscribedAssets(market.ClobTokenIds);
            if (confirmLiveSubscriptions)
            {
                foreach (var assetId in market.ClobTokenIds)
                {
                    Assert.True(cache.ConfirmAssetSubscription("paired-test-shard", assetId));
                }
            }

            cache.UpdateStatus(new MarketDataStatusSnapshot(
                "PolymarketMarketWebSocket",
                MarketDataConnectionState.Connected,
                "wss://example.test",
                market.ClobTokenIds.Count,
                nowUtc,
                nowUtc.AddMinutes(-1),
                null,
                0,
                false,
                null,
                nowUtc));
        }

        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var timeProvider = new MutableTimeProvider(nowUtc);
        var clobClient = new PairedBookClobClient(
            timeProvider,
            market.ConditionId,
            downWouldCrossOnS1,
            advanceBeforeFirstDownRead,
            cancelDownOnFirstS1,
            advanceAfterS1Snapshot);
        var processor = new PairedMakerGtdFirstAcceptingProcessor(
            NullLogger<PairedMakerGtdFirstAcceptingProcessor>.Instance,
            new BotOptions { Mode = botMode },
            new PaperTradingOptions(),
            new BtcUpDown5mStrategyOptions
            {
                PaperTakerMaxQuoteAgeMilliseconds = maxQuoteAgeMilliseconds
            },
            clobClient,
            cache,
            registry,
            new ExposureSnapshotCache(repository, handoff),
            handoff,
            new StaticStrategyStateProvider(),
            repository,
            timeProvider);
        var candidate = new PairedMakerGtdFirstAcceptingCandidate(
            market,
            nowUtc.AddMilliseconds(-100),
            nowUtc,
            nowUtc);
        return new Fixture(
            nowUtc,
            candidate,
            processor,
            repository,
            registry,
            cache,
            timeProvider,
            clobClient);
    }

    private static PolymarketGammaMarket CreateMarket(
        DateTimeOffset observedAtUtc,
        DateTimeOffset marketStartUtc)
    {
        var unix = marketStartUtc.ToUnixTimeSeconds();
        return GammaMarketIngestionTests.CreateMarketForTests("paired-btc") with
        {
            Slug = $"btc-updown-5m-{unix}",
            EventSlug = $"btc-updown-5m-{unix}",
            SeriesSlug = "btc-up-or-down-5m",
            Category = "Crypto",
            Active = true,
            Closed = false,
            Archived = false,
            AcceptingOrders = true,
            EnableOrderBook = true,
            CreatedAtUtc = observedAtUtc.AddMinutes(-1),
            UpdatedAtUtc = observedAtUtc,
            EventStartTimeUtc = marketStartUtc,
            EndDateUtc = marketStartUtc.AddMinutes(5),
            Outcomes = ["Up", "Down"],
            ClobTokenIds = ["token-up", "token-down"],
            FetchedAtUtc = observedAtUtc
        };
    }

    private static StrategyMarketPaperRun CreateObservedRun(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc,
        string? diagnosticsJson = null)
    {
        var marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market);
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            variant.Category,
            marketStartUtc,
            market.EndDateUtc,
            nowUtc,
            nowUtc,
            StrategyMarketPaperRunStatuses.Observed,
            null,
            variant.FixedOutcome?.ToString(),
            null,
            0m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            diagnosticsJson ?? CreateInitialObservationEvidence(market, nowUtc).ToJsonString());
    }

    private static string CreateContinuationEvidence(
        PolymarketGammaMarket market,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset nowUtc,
        int attemptsCompleted,
        Guid? persistedStrategyId = null,
        DateTimeOffset? frozenAtUtc = null)
    {
        var attempts = new JsonArray();
        for (var attemptNumber = 1; attemptNumber <= attemptsCompleted; attemptNumber++)
        {
            attempts.Add(new JsonObject
            {
                ["attempt_number"] = attemptNumber,
                ["started_at_utc"] = nowUtc.AddSeconds(-attemptsCompleted + attemptNumber).ToString("O"),
                ["persisted_marker"] = $"persisted-{variant.FixedOutcome}-{attemptNumber}"
            });
        }

        return new JsonObject
        {
            ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
            ["paper_model_label"] = PairedMakerGtdPaperExecutionContract.MandatoryLabel,
            ["skip_reason"] = null,
            ["updated_at_utc"] = nowUtc.ToString("O"),
            ["first_accepting_observation"] = CreateInitialObservationEvidence(
                market,
                nowUtc,
                "original-observation"),
            ["pair"] = new JsonObject
            {
                ["strategy_id"] = (persistedStrategyId ?? variant.Id).ToString("D"),
                ["paired_strategy_id"] = variant.PairedStrategyId?.ToString("D"),
                ["common_requested_size_shares"] = 10m,
                ["common_size_frozen_at_utc"] = (frozenAtUtc ?? nowUtc).ToString("O"),
                ["atomic"] = false,
                ["rollback"] = false
            },
            ["maker_gtd"] = new JsonObject
            {
                ["execution_source"] = PairedMakerGtdPaperExecutionContract.ExecutionSource,
                ["contract_version"] = PairedMakerGtdPaperExecutionContract.ContractVersion,
                ["terminal_outcome"] = "observed",
                ["terminal_reason"] = null,
                ["attempts_completed"] = attemptsCompleted,
                ["attempts"] = attempts
            }
        }.ToJsonString();
    }

    private static JsonObject CreateInitialObservationEvidence(
        PolymarketGammaMarket market,
        DateTimeOffset firstObservedAcceptingAtUtc,
        string? observationMarker = null)
    {
        return new JsonObject
        {
            ["phase"] = "first_accepting_observed",
            ["request_started_at_utc"] = firstObservedAcceptingAtUtc.AddMilliseconds(-100).ToString("O"),
            ["response_completed_at_utc"] = firstObservedAcceptingAtUtc.ToString("O"),
            ["first_observed_accepting_at_utc"] = firstObservedAcceptingAtUtc.ToString("O"),
            ["market_id"] = market.MarketId,
            ["condition_id"] = market.ConditionId,
            ["market_slug"] = market.Slug,
            ["accepting_orders"] = true,
            ["clob_token_ids"] = JsonSerializer.SerializeToNode(market.ClobTokenIds),
            ["observation_marker"] = observationMarker
        };
    }

    private sealed class StaticStrategyStateProvider : IStrategyStateProvider
    {
        private static readonly IReadOnlyDictionary<Guid, StrategyRuntimeSettings> Settings =
            StrategyIds.PairedMakerGtdFirstAcceptingVariants.ToDictionary(
                variant => variant.Id,
                variant => StrategyRuntimeSettings.Default(variant.Id));

        public Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategySettingsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Settings);

        public Task<IReadOnlySet<Guid>> GetEnabledStrategyIdsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<Guid>>(
                Settings.Keys.ToHashSet());
    }

    private sealed class PairedBookClobClient(
        MutableTimeProvider timeProvider,
        string conditionId,
        bool downWouldCrossOnS1,
        TimeSpan? advanceBeforeFirstDownRead,
        bool cancelDownOnFirstS1 = false,
        TimeSpan? advanceAfterS1Snapshot = null) : IPolymarketClobPublicClient
    {
        private int upCalls;
        private int downCalls;
        private int downS1CancellationInjected;

        public int DownS1Calls { get; private set; }

        public int TotalOrderBookCalls { get; private set; }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(
            string assetId,
            CancellationToken cancellationToken = default)
        {
            TotalOrderBookCalls++;
            decimal bestAsk;
            bool isS1;
            if (assetId == "token-up")
            {
                isS1 = Interlocked.Increment(ref upCalls) % 2 == 0;
                bestAsk = 0.51m;
            }
            else
            {
                var call = Interlocked.Increment(ref downCalls);
                if (call == 1 && advanceBeforeFirstDownRead is { } advance)
                {
                    timeProvider.Advance(advance);
                }

                isS1 = call % 2 == 0;
                if (isS1)
                {
                    DownS1Calls++;
                    if (cancelDownOnFirstS1 &&
                        Interlocked.Exchange(ref downS1CancellationInjected, 1) == 0)
                    {
                        throw new OperationCanceledException("Injected interruption after Up persistence.");
                    }
                }

                bestAsk = downWouldCrossOnS1 && isS1 ? 0.49m : 0.50m;
            }

            timeProvider.Advance(TimeSpan.FromMilliseconds(1));
            var nowUtc = timeProvider.GetUtcNow();

            OrderBookSnapshot snapshot = new(
                assetId,
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(bestAsk, 100m)],
                nowUtc,
                conditionId,
                MinOrderSize: 5m,
                TickSize: 0.01m,
                SourceTimestampUtc: nowUtc,
                TimestampQuality: MarketDataTimestampQuality.VenueProvided,
                ReceivedAtUtc: nowUtc,
                SourceEventId: assetId + "-book");
            if (isS1 && advanceAfterS1Snapshot is { } advanceAfterSnapshot)
            {
                timeProvider.Advance(advanceAfterSnapshot);
            }

            return Task.FromResult<OrderBookSnapshot?>(snapshot);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(timeProvider.GetUtcNow());

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<decimal?>(0.50m);

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<decimal?>(0.01m);

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(
            string tokenId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PolymarketClobMarketByToken?>(null);
    }

    private sealed class RacingConfirmedMarketDataCache(
        IMarketDataCache inner,
        string component,
        IReadOnlyCollection<string> assetIds,
        int triggerConfirmedRead) : IMarketDataCache
    {
        private int confirmedReads;

        public bool RaceTriggered { get; private set; }

        public IReadOnlyCollection<string> SubscribedAssetIds => inner.SubscribedAssetIds;

        public MarketDataStatusSnapshot Status => inner.Status;

        public long GetAssetSubscriptionGeneration(string assetId) =>
            inner.GetAssetSubscriptionGeneration(assetId);

        public ConfirmedAssetSubscriptionSnapshot GetConfirmedAssetSubscription(string assetId)
        {
            var snapshot = inner.GetConfirmedAssetSubscription(assetId);
            if (Interlocked.Increment(ref confirmedReads) == triggerConfirmedRead)
            {
                inner.InvalidateAssetSubscriptions(component);
                foreach (var requiredAssetId in assetIds)
                {
                    Assert.True(inner.ConfirmAssetSubscription(component, requiredAssetId));
                }

                RaceTriggered = true;
            }

            return snapshot;
        }

        public void ReplaceSubscribedAssets(IReadOnlyCollection<string> nextAssetIds) =>
            inner.ReplaceSubscribedAssets(nextAssetIds);

        public void AssignAssetSubscriptions(string nextComponent, IReadOnlyCollection<string> nextAssetIds) =>
            inner.AssignAssetSubscriptions(nextComponent, nextAssetIds);

        public void InvalidateAssetSubscriptions(string invalidatedComponent) =>
            inner.InvalidateAssetSubscriptions(invalidatedComponent);

        public void RemoveAssetSubscriptionComponent(string removedComponent) =>
            inner.RemoveAssetSubscriptionComponent(removedComponent);

        public bool ConfirmAssetSubscription(string confirmedComponent, string assetId) =>
            inner.ConfirmAssetSubscription(confirmedComponent, assetId);

        public void ApplyUpdate(MarketDataUpdate update) => inner.ApplyUpdate(update);

        public OrderBookCacheLookup GetOrderBook(string assetId, TimeSpan maxAge) =>
            inner.GetOrderBook(assetId, maxAge);

        public bool TryGetFreshOrderBook(
            string assetId,
            TimeSpan maxAge,
            out OrderBookSnapshot snapshot) =>
            inner.TryGetFreshOrderBook(assetId, maxAge, out snapshot);

        public void UpdateStatus(MarketDataStatusSnapshot status) => inner.UpdateStatus(status);
    }

    private sealed class PublicationOrderingHandoff(
        IMakerGtdPaperPlacementHandoff inner) : IMakerGtdPaperPlacementHandoff
    {
        public Func<Guid, bool> IsOrderVisible { private get; set; } = _ => false;

        public HashSet<Guid> PublishedOrderIds { get; } = [];

        public ValueTask<IMakerGtdPaperPlacementAdmission> EnterPlacementAdmissionAsync(
            string assetId,
            CancellationToken cancellationToken = default) =>
            inner.EnterPlacementAdmissionAsync(assetId, cancellationToken);

        public ValueTask<IAsyncDisposable> EnterMarketDataAdmissionAsync(
            string assetId,
            CancellationToken cancellationToken = default) =>
            inner.EnterMarketDataAdmissionAsync(assetId, cancellationToken);

        public ValueTask<IAsyncDisposable> EnterMarketDataReceiptAsync(
            CancellationToken cancellationToken = default) =>
            inner.EnterMarketDataReceiptAsync(cancellationToken);

        public ValueTask<IAsyncDisposable?> TryEnterExpiryAdmissionAsync(
            CancellationToken cancellationToken = default) =>
            inner.TryEnterExpiryAdmissionAsync(cancellationToken);

        public IReadOnlySet<Guid> GetPendingOrderIds(string assetId) =>
            inner.GetPendingOrderIds(assetId);

        public Task WaitForPublicationAsync(
            IReadOnlySet<Guid>? eligiblePaperOrderIds,
            CancellationToken cancellationToken = default) =>
            inner.WaitForPublicationAsync(eligiblePaperOrderIds, cancellationToken);

        public void MarkPublished(Guid paperOrderId)
        {
            Assert.True(
                IsOrderVisible(paperOrderId),
                $"Paper order {paperOrderId:D} was published before exposure-cache visibility.");
            PublishedOrderIds.Add(paperOrderId);
            inner.MarkPublished(paperOrderId);
        }

        public void MarkFailed(Guid paperOrderId) => inner.MarkFailed(paperOrderId);

        public void TrackMakerGtdPaperOrder(Guid paperOrderId, string executionSource) =>
            inner.TrackMakerGtdPaperOrder(paperOrderId, executionSource);

        public void RecordMarketDataFailure(
            string? assetId,
            string? conditionId,
            DateTimeOffset receivedAtUtc,
            IReadOnlySet<Guid>? affectedPaperOrderIds,
            string failureCode) =>
            inner.RecordMarketDataFailure(
                assetId,
                conditionId,
                receivedAtUtc,
                affectedPaperOrderIds,
                failureCode);

        public bool TryGetMarketDataFailure(
            Guid paperOrderId,
            string assetId,
            string conditionId,
            DateTimeOffset acceptedAfterUtc,
            DateTimeOffset expiresBeforeUtc,
            out MakerGtdPaperMarketDataFailure? failure) =>
            inner.TryGetMarketDataFailure(
                paperOrderId,
                assetId,
                conditionId,
                acceptedAfterUtc,
                expiresBeforeUtc,
                out failure);

        public void ClearMarketDataFailures(Guid paperOrderId) =>
            inner.ClearMarketDataFailures(paperOrderId);
    }

    private sealed class MutableTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        private DateTimeOffset now = nowUtc;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan value) => now = now.Add(value);
    }

    private sealed record Fixture(
        DateTimeOffset NowUtc,
        PairedMakerGtdFirstAcceptingCandidate Candidate,
        PairedMakerGtdFirstAcceptingProcessor Processor,
        TestAppRepository Repository,
        ActiveMarketAssetSubscriptionRegistry Registry,
        MarketDataCache Cache,
        MutableTimeProvider TimeProvider,
        PairedBookClobClient ClobClient);
}
