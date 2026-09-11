using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperSettlementProcessorTests
{
    [Fact]
    public async Task SettleMarketResolutionAsync_SettlesWinningAndLosingCopiedPositions()
    {
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(new PaperPosition(
            "asset-yes",
            "condition-1",
            "Yes",
            5m,
            0.40m,
            2m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader",
            FeeUsd: 0.10m,
            FeeAccountingStatus: FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Taker.ToString(),
            NetUnrealizedPnlUsd: -0.10m));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-no",
            "condition-1",
            "No",
            5m,
            0.30m,
            1.5m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader"));
        var processor = new PaperSettlementProcessor(
            NullLogger<PaperSettlementProcessor>.Instance,
            new FakeGammaClient([]),
            new ExposureSnapshotCache(repository),
            repository);

        var result = await processor.SettleMarketResolutionAsync(
            "condition-1",
            null,
            "asset-yes",
            "Yes",
            "Politics",
            "UnitTest",
            DateTimeOffset.UtcNow);

        Assert.Equal(2, result.PositionsSettled);
        Assert.Equal(2, result.SettlementsInserted);
        Assert.Equal(1, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(0, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Equal(0, repository.RefreshPaperCopiedTraderPerformanceProjectionCalls);
        Assert.Equal(0, result.PerformanceRowsRefreshed);
        Assert.All(repository.PaperPositions, position =>
        {
            Assert.Equal(0m, position.SizeShares);
            Assert.Equal(0m, position.AveragePrice);
            Assert.Equal(0m, position.EstimatedValueUsd);
            Assert.Equal(0m, position.UnrealizedPnlUsd);
            Assert.Equal(0m, position.FeeUsd);
            Assert.Equal(0m, position.NetUnrealizedPnlUsd);
        });
        var yes = Assert.Single(repository.PaperPositionSettlements, item => item.AssetId == "asset-yes");
        Assert.True(yes.Won);
        Assert.Equal(5m, yes.SettlementValueUsd);
        Assert.Equal(3m, yes.RealizedPnlUsd);
        Assert.Equal(0.10m, yes.FeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), yes.FeeAccountingStatus);
        Assert.Equal(2.90m, yes.NetRealizedPnlUsd);
        var no = Assert.Single(repository.PaperPositionSettlements, item => item.AssetId == "asset-no");
        Assert.False(no.Won);
        Assert.Equal(0m, no.SettlementValueUsd);
        Assert.Equal(-1.5m, no.RealizedPnlUsd);
        Assert.Equal(FeeAccountingStatus.LegacyUnknown.ToString(), no.FeeAccountingStatus);
        Assert.Null(no.NetRealizedPnlUsd);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_DeadlockReloadsCurrentPositionsBeforeRetry()
    {
        var (repository, processor) = CreateSettlementProcessor();
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        repository.PaperPositionSettlementBatchFailureHook = _ =>
        {
            repository.PaperPositions[0] = repository.PaperPositions[0] with
            {
                SizeShares = 7m,
                EstimatedValueUsd = 2.8m,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        };

        var result = await SettleAsync(processor);

        Assert.Equal(1, result.PositionsSettled);
        Assert.Equal(2, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(2, repository.PaperPositionSettlementBatchCalls);
        Assert.Equal(7m, Assert.Single(repository.PaperPositionSettlements).SettledSizeShares);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_TwoDeadlocksSucceedOnThirdCompleteAttempt()
    {
        var (repository, processor) = CreateSettlementProcessor();
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());

        var result = await SettleAsync(processor);

        Assert.Equal(1, result.SettlementsInserted);
        Assert.Equal(3, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(3, repository.PaperPositionSettlementBatchCalls);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_ThirdDeadlockIsRethrown()
    {
        var (repository, processor) = CreateSettlementProcessor();
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());

        var exception = await Assert.ThrowsAsync<PostgresException>(() => SettleAsync(processor));

        Assert.Equal(PostgresErrorCodes.DeadlockDetected, exception.SqlState);
        Assert.Equal(3, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(3, repository.PaperPositionSettlementBatchCalls);
        Assert.Empty(repository.PaperPositionSettlements);
        Assert.Equal(5m, Assert.Single(repository.PaperPositions).SizeShares);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_NonDeadlockFailureIsNotRetried()
    {
        var (repository, processor) = CreateSettlementProcessor();
        repository.PaperPositionSettlementBatchFailures.Enqueue(
            new InvalidOperationException("non-deadlock persistence failure"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SettleAsync(processor));

        Assert.Equal("non-deadlock persistence failure", exception.Message);
        Assert.Equal(1, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_CancellationStopsDeadlockRetryDelay()
    {
        var (repository, processor) = CreateSettlementProcessor();
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        using var cancellation = new CancellationTokenSource();
        repository.PaperPositionSettlementBatchFailureHook = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SettleAsync(processor, cancellation.Token));

        Assert.Equal(1, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Empty(repository.PaperPositionSettlements);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_ReportsTypedStagesWithoutChangingResultOrWarningThreshold()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);
        repository.PaperPositionSettlementStageHook = async (writes, observer, token) =>
        {
            CompleteStage(observer, PaperSettlementPersistenceStages.OpenConnection, 2);
            CompleteStage(observer, PaperSettlementPersistenceStages.AcquireWalletLocks, 2300);
            observer(new(PaperSettlementPersistenceStages.InsertSettlements, PaperSettlementPersistenceStageStatus.Started));
            var inserted = await repository.PersistPaperPositionSettlementBatchAsync(writes, token);
            observer(new(PaperSettlementPersistenceStages.InsertSettlements, PaperSettlementPersistenceStageStatus.Completed, 5));
            CompleteStage(observer, PaperSettlementPersistenceStages.Commit, 3);
            CompleteStage(observer, PaperSettlementPersistenceStages.DisposeConnection, 1);
            return inserted;
        };

        var result = await SettleAsync(processor);

        Assert.Equal(1, result.SettlementsInserted);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Available", entry.Properties["PersistenceStagesAvailability"]);
        Assert.Equal(PaperSettlementPersistenceStages.DisposeConnection, entry.Properties["PersistenceLastStage"]);
        Assert.Null(entry.Properties["PersistenceFailedStage"]);
        Assert.Equal(PaperSettlementPersistenceStages.AcquireWalletLocks, entry.Properties["PersistenceSlowestStage"]);
        Assert.Equal(2300d, entry.Properties["PersistenceSlowestDurationMs"]);
        var stages = ReadStages(entry);
        Assert.Equal(5, stages.Length);
        Assert.All(stages, stage => Assert.Equal(PaperSettlementPersistenceStageStatus.Completed, stage.Status));
        Assert.Equal(
            Assert.IsType<double>(entry.Properties["TotalDurationMs"]) >= 1000 ? LogLevel.Warning : LogLevel.Debug,
            entry.Level);
        Assert.Equal(1, entry.Properties["Attempt"]);
        Assert.Equal(1, entry.Properties["Positions"]);
        Assert.Equal("condition-1", entry.Properties["ConditionId"]);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_OldRepositoryPathReportsUnavailableInsteadOfZeroTimings()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);

        var result = await SettleAsync(processor);

        Assert.Equal(1, result.SettlementsInserted);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("NotAvailable", entry.Properties["PersistenceStagesAvailability"]);
        Assert.Null(entry.Properties["PersistenceLastStage"]);
        Assert.Null(entry.Properties["PersistenceFailedStage"]);
        Assert.Null(entry.Properties["PersistenceSlowestStage"]);
        Assert.Null(entry.Properties["PersistenceSlowestDurationMs"]);
        Assert.Empty(ReadStages(entry));
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_FailureRetainsOriginalExceptionAndStageAfterCleanup()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);
        var failure = new InvalidOperationException("original settlement failure");
        repository.PaperPositionSettlementBatchFailures.Enqueue(failure);
        repository.PaperPositionSettlementStageHook = async (writes, observer, token) =>
        {
            CompleteStage(observer, PaperSettlementPersistenceStages.OpenConnection, 1);
            observer(new(PaperSettlementPersistenceStages.UpsertPositions, PaperSettlementPersistenceStageStatus.Started));
            try
            {
                return await repository.PersistPaperPositionSettlementBatchAsync(writes, token);
            }
            catch
            {
                observer(new(PaperSettlementPersistenceStages.UpsertPositions, PaperSettlementPersistenceStageStatus.Failed, 9));
                CompleteStage(observer, PaperSettlementPersistenceStages.DisposeTransaction, 2);
                observer(new(PaperSettlementPersistenceStages.DisposeConnection, PaperSettlementPersistenceStageStatus.Started));
                throw;
            }
        };

        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => SettleAsync(processor)));

        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Empty(repository.PaperPositionSettlements);
        Assert.Equal(5m, Assert.Single(repository.PaperPositions).SizeShares);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Equal("PersistSettlementBatch", entry.Properties["Phase"]);
        Assert.Equal(PaperSettlementPersistenceStages.UpsertPositions, entry.Properties["PersistenceFailedStage"]);
        Assert.Equal(PaperSettlementPersistenceStages.DisposeConnection, entry.Properties["PersistenceLastStage"]);
        Assert.Equal(9d, entry.Properties["PersistenceSlowestDurationMs"]);
        var unfinished = Assert.Single(ReadStages(entry), stage => stage.Stage == PaperSettlementPersistenceStages.DisposeConnection);
        Assert.Equal(PaperSettlementPersistenceStageStatus.Started, unfinished.Status);
        Assert.Null(unfinished.DurationMilliseconds);
        Assert.DoesNotContain(ReadStages(entry), stage => stage.Stage == PaperSettlementPersistenceStages.Commit);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_DeadlockAttemptDoesNotLeakStagesIntoSuccessfulRetry()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);
        repository.PaperPositionSettlementBatchFailures.Enqueue(CreateDeadlockException());
        repository.PaperPositionSettlementStageHook = async (writes, observer, token) =>
        {
            var first = repository.PaperPositionSettlementBatchCalls == 0;
            var stage = first ? PaperSettlementPersistenceStages.AcquireWalletLocks : PaperSettlementPersistenceStages.InsertSettlements;
            observer(new(stage, PaperSettlementPersistenceStageStatus.Started));
            try
            {
                var inserted = await repository.PersistPaperPositionSettlementBatchAsync(writes, token);
                observer(new(stage, PaperSettlementPersistenceStageStatus.Completed, 3));
                return inserted;
            }
            catch
            {
                observer(new(stage, PaperSettlementPersistenceStageStatus.Failed, 40));
                throw;
            }
        };

        Assert.Equal(1, (await SettleAsync(processor)).SettlementsInserted);

        Assert.Equal(2, repository.GetOpenPaperPositionsForMarketCalls);
        Assert.Equal(2, repository.PaperPositionSettlementBatchCalls);
        var entries = logger.Entries.ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(LogLevel.Warning, entries[0].Level);
        Assert.Equal(1, entries[0].Properties["Attempt"]);
        Assert.Equal(PaperSettlementPersistenceStages.AcquireWalletLocks, entries[0].Properties["PersistenceFailedStage"]);
        Assert.Equal(40d, entries[0].Properties["PersistenceSlowestDurationMs"]);
        Assert.Equal(2, entries[1].Properties["Attempt"]);
        Assert.Null(entries[1].Properties["PersistenceFailedStage"]);
        Assert.Equal(3d, entries[1].Properties["PersistenceSlowestDurationMs"]);
        Assert.Equal(PaperSettlementPersistenceStages.InsertSettlements, Assert.Single(ReadStages(entries[1])).Stage);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_SimultaneousBatchesKeepIndependentDiagnostics()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);
        repository.PaperPositions.Add(repository.PaperPositions[0] with
        {
            AssetId = "asset-other", ConditionId = "condition-other", CopiedTraderWallet = "other-wallet"
        });
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.PaperPositionSettlementStageHook = async (writes, observer, token) =>
        {
            if (writes[0].Settlement.ConditionId == "condition-1")
            {
                observer(new(PaperSettlementPersistenceStages.AcquireWalletLocks, PaperSettlementPersistenceStageStatus.Started));
                firstEntered.TrySetResult(true);
                await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                CompleteStage(observer, PaperSettlementPersistenceStages.AcquireWalletLocks, 11);
            }
            else
            {
                CompleteStage(observer, PaperSettlementPersistenceStages.OpenConnection, 2);
            }
            return await repository.PersistPaperPositionSettlementBatchAsync(writes, token);
        };

        var first = SettleAsync(processor);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await processor.SettleMarketResolutionAsync("condition-other", null, "asset-other", "Yes", "Politics", "UnitTest", DateTimeOffset.UtcNow);
        }
        finally
        {
            releaseFirst.TrySetResult(true);
        }
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, repository.PaperPositionSettlements.Count);
        var firstEntry = Assert.Single(logger.Entries, entry => Equals(entry.Properties["ConditionId"], "condition-1"));
        var secondEntry = Assert.Single(logger.Entries, entry => Equals(entry.Properties["ConditionId"], "condition-other"));
        Assert.Equal(PaperSettlementPersistenceStages.AcquireWalletLocks, Assert.Single(ReadStages(firstEntry)).Stage);
        Assert.Equal(PaperSettlementPersistenceStages.OpenConnection, Assert.Single(ReadStages(secondEntry)).Stage);
        Assert.Equal(11d, firstEntry.Properties["PersistenceSlowestDurationMs"]);
        Assert.Equal(2d, secondEntry.Properties["PersistenceSlowestDurationMs"]);
    }

    [Fact]
    public async Task SettleMarketResolutionAsync_InvalidDiagnosticsCannotChangeCommittedSuccess()
    {
        var logger = new RecordingLogger();
        var (repository, processor) = CreateSettlementProcessor(logger);
        repository.PaperPositionSettlementStageHook = async (writes, observer, token) =>
        {
            var inserted = await repository.PersistPaperPositionSettlementBatchAsync(writes, token);
            CompleteStage(observer, PaperSettlementPersistenceStages.Commit, 1);
            observer(null!);
            observer(new("not-a-stage:wallet-secret", PaperSettlementPersistenceStageStatus.Completed, 3));
            observer(new(PaperSettlementPersistenceStages.OpenConnection, PaperSettlementPersistenceStageStatus.Completed, double.NaN));
            return inserted;
        };

        Assert.Equal(1, (await SettleAsync(processor)).SettlementsInserted);

        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("Incomplete", entry.Properties["PersistenceStagesAvailability"]);
        Assert.Equal(PaperSettlementPersistenceStages.Commit, Assert.Single(ReadStages(entry)).Stage);
        Assert.DoesNotContain("wallet-secret", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessOpenPositionsAsync_UsesClosedGammaMetadataWithoutInlinePerformanceRefresh()
    {
        var repository = new TestAppRepository();
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-yes",
            "condition-1",
            "Yes",
            0.40m,
            5m,
            2m,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5),
            DateTimeOffset.UtcNow.AddMinutes(-4)));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            repository.PaperOrders[0].Id,
            0.40m,
            5m,
            DateTimeOffset.UtcNow.AddMinutes(-4),
            "test"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-yes",
            "condition-1",
            "Yes",
            5m,
            0.40m,
            2m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader"));
        repository.PolymarketGammaMarkets.Add(GammaMarket("condition-1", "Politics"));
        var metadata = new[]
        {
            TokenMetadata("asset-yes", "condition-1", "Yes", "Yes", "Politics"),
            TokenMetadata("asset-no", "condition-1", "No", "Yes", "Politics")
        };
        var processor = new PaperSettlementProcessor(
            NullLogger<PaperSettlementProcessor>.Instance,
            new FakeGammaClient(metadata),
            new ExposureSnapshotCache(repository),
            repository);

        var result = await processor.ProcessOpenPositionsAsync();

        Assert.Equal(1, result.SettlementsInserted);
        Assert.Equal(1, repository.PaperPositionSettlementBatchCalls);
        Assert.Equal(0, repository.RefreshPaperCopiedTraderPerformanceProjectionCalls);
        Assert.Equal(0, result.PerformanceRowsRefreshed);
        Assert.Empty(repository.PaperCopiedTraderPerformances);
    }

    private static PolymarketOnChainTokenMetadata TokenMetadata(
        string tokenId,
        string conditionId,
        string outcome,
        string winningOutcome,
        string category)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            conditionId,
            "market-1",
            "market-slug",
            "Market title",
            outcome,
            outcome == "Yes" ? 0 : 1,
            category,
            DateTimeOffset.UtcNow,
            Active: false,
            Closed: true,
            Archived: false,
            Resolved: true,
            winningOutcome,
            ["asset-yes", "asset-no"],
            ["Yes", "No"],
            LookupSucceeded: true,
            LookupError: null,
            RawJson: "{}",
            LastRefreshedUtc: DateTimeOffset.UtcNow);
    }

    private static (TestAppRepository Repository, PaperSettlementProcessor Processor)
        CreateSettlementProcessor(ILogger<PaperSettlementProcessor>? logger = null)
    {
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(new PaperPosition(
            "asset-yes",
            "condition-1",
            "Yes",
            5m,
            0.40m,
            2m,
            0m,
            DateTimeOffset.UtcNow,
            "0xleader",
            FeeUsd: 0.10m,
            FeeAccountingStatus: FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Taker.ToString(),
            NetUnrealizedPnlUsd: -0.10m));
        var processor = new PaperSettlementProcessor(
            logger ?? NullLogger<PaperSettlementProcessor>.Instance,
            new FakeGammaClient([]),
            new ExposureSnapshotCache(repository),
            repository);
        return (repository, processor);
    }

    private static Task<PaperSettlementProcessingResult> SettleAsync(
        PaperSettlementProcessor processor,
        CancellationToken cancellationToken = default) =>
        processor.SettleMarketResolutionAsync(
            "condition-1",
            null,
            "asset-yes",
            "Yes",
            "Politics",
            "UnitTest",
            DateTimeOffset.UtcNow,
            cancellationToken);

    private static PostgresException CreateDeadlockException() =>
        new(
            "deadlock detected",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.DeadlockDetected);

    private static void CompleteStage(
        Action<PaperSettlementPersistenceStageEvent> observer,
        string stage,
        double duration)
    {
        observer(new(stage, PaperSettlementPersistenceStageStatus.Started));
        observer(new(stage, PaperSettlementPersistenceStageStatus.Completed, duration));
    }

    private static PaperSettlementPersistenceStageEvent[] ReadStages(LogEntry entry) =>
        Assert.IsType<PaperSettlementPersistenceStageEvent[]>(entry.Properties["@PersistenceStages"]);

    private sealed class RecordingLogger : ILogger<PaperSettlementProcessor>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(item => item.Key, item => item.Value)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception, properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);

    private static PolymarketGammaMarket GammaMarket(string conditionId, string category)
    {
        return new PolymarketGammaMarket(
            "market-1",
            conditionId,
            "question-1",
            "market-slug",
            "Market title",
            null,
            null,
            null,
            null,
            category,
            Active: false,
            Closed: true,
            Archived: false,
            Restricted: false,
            AcceptingOrders: false,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: null,
            BestAsk: null,
            Spread: null,
            CreatedAtUtc: null,
            UpdatedAtUtc: null,
            StartDateUtc: null,
            EndDateUtc: null,
            EventStartTimeUtc: null,
            Outcomes: ["Yes", "No"],
            ClobTokenIds: ["asset-yes", "asset-no"],
            RawJson: "{}",
            FetchedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata) : IPolymarketGammaClient
    {
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<string?> GetEventCategoryAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
