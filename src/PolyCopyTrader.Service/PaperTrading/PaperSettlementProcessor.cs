using System.Diagnostics;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperSettlementProcessor(
    ILogger<PaperSettlementProcessor> logger,
    IPolymarketGammaClient gammaClient,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : IPaperSettlementProcessor
{
    private const int SettlementDeadlockMaximumAttempts = 3;
    private const int SettlementDeadlockInitialRetryDelayMilliseconds = 50;

    public async Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = (await repository.GetOpenPaperPositionsAsync(cancellationToken)).ToArray();
        if (positions.Length == 0)
        {
            return new PaperSettlementProcessingResult(0, 0, 0, 0);
        }

        var checkedPositions = 0;
        var settledPositions = 0;
        var insertedSettlements = 0;
        var processedConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var position in positions)
        {
            if (!processedConditions.Add(position.ConditionId))
            {
                continue;
            }

            checkedPositions += positions.Count(item =>
                string.Equals(item.ConditionId, position.ConditionId, StringComparison.OrdinalIgnoreCase));

            try
            {
                var metadata = await GetResolvedMetadataAsync(position, cancellationToken);
                if (metadata.Count == 0)
                {
                    continue;
                }

                var winningOutcome = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.WinningOutcome))?.WinningOutcome;
                if (string.IsNullOrWhiteSpace(winningOutcome))
                {
                    continue;
                }

                var winningAssetId = metadata.FirstOrDefault(item =>
                    string.Equals(item.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase))?.TokenId;
                var category = metadata.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Category))?.Category;
                var result = await SettleMarketResolutionAsync(
                    position.ConditionId,
                    null,
                    winningAssetId,
                    winningOutcome,
                    category,
                    "GammaClosedMarket",
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                settledPositions += result.PositionsSettled;
                insertedSettlements += result.SettlementsInserted;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Paper settlement lookup failed for condition {ConditionId} asset {AssetId}.", position.ConditionId, position.AssetId);
                await TryRecordApiErrorAsync("ProcessOpenPosition", ex.Message, cancellationToken);
            }
        }

        return new PaperSettlementProcessingResult(checkedPositions, settledPositions, insertedSettlements, 0);
    }

    public async Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
        string? conditionId,
        string? assetId,
        string? winningAssetId,
        string? winningOutcome,
        string? category,
        string settlementSource,
        DateTimeOffset settledAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(winningAssetId) && string.IsNullOrWhiteSpace(winningOutcome))
        {
            return new PaperSettlementProcessingResult(0, 0, 0, 0);
        }

        for (var attempt = 1; attempt <= SettlementDeadlockMaximumAttempts; attempt++)
        {
            var operationStarted = Stopwatch.GetTimestamp();
            var phase = "LoadOpenPositions";
            var loadDuration = TimeSpan.Zero;
            var prepareDuration = TimeSpan.Zero;
            var persistenceDuration = TimeSpan.Zero;
            var cacheDuration = TimeSpan.Zero;
            var persistenceDiagnostics = new SettlementPersistenceDiagnostics();
            try
            {
                var phaseStarted = Stopwatch.GetTimestamp();
                var positions = (await repository.GetOpenPaperPositionsForMarketAsync(
                        conditionId,
                        assetId,
                        cancellationToken))
                    .ToArray();
                loadDuration = Stopwatch.GetElapsedTime(phaseStarted);
                if (positions.Length == 0)
                {
                    return new PaperSettlementProcessingResult(0, 0, 0, 0);
                }

                phase = "PrepareSettlementBatch";
                phaseStarted = Stopwatch.GetTimestamp();
                var writes = new List<PaperPositionSettlementWrite>(positions.Length);
                foreach (var position in positions)
                {
                    var won = IsWinningPosition(position, winningAssetId, winningOutcome);
                    var costBasis = position.AveragePrice * position.SizeShares;
                    var settlementValue = won ? position.SizeShares : 0m;
                    var grossRealizedPnl = settlementValue - costBasis;
                    var netRealizedPnl = FeeAccountingRules.IsAccounted(position.FeeAccountingStatus)
                        ? grossRealizedPnl - position.FeeUsd
                        : (decimal?)null;
                    var now = DateTimeOffset.UtcNow;
                    var settlement = new PaperPositionSettlement(
                        Guid.NewGuid(),
                        position.CopiedTraderWallet,
                        position.AssetId,
                        position.ConditionId,
                        position.Outcome,
                        winningAssetId,
                        winningOutcome ?? string.Empty,
                        category,
                        position.SizeShares,
                        position.AveragePrice,
                        costBasis,
                        settlementValue,
                        grossRealizedPnl,
                        won,
                        settlementSource,
                        settledAtUtc,
                        now,
                        position.FeeUsd,
                        position.FeeAccountingStatus,
                        position.FeeLiquidityRole,
                        position.FeeCalculationSource,
                        position.FeeRate,
                        position.FeeExponent,
                        position.FeeTakerOnly,
                        position.FeeCalculatedAtUtc,
                        netRealizedPnl);
                    var settledPosition = position with
                    {
                        SizeShares = 0m,
                        AveragePrice = 0m,
                        EstimatedValueUsd = 0m,
                        UnrealizedPnlUsd = 0m,
                        FeeUsd = 0m,
                        FeeAccountingStatus = FeeAccountingStatus.Calculated.ToString(),
                        FeeLiquidityRole = FeeLiquidityRole.Unknown.ToString(),
                        FeeCalculationSource = "settled",
                        FeeRate = null,
                        FeeExponent = null,
                        FeeTakerOnly = null,
                        FeeCalculatedAtUtc = now,
                        NetUnrealizedPnlUsd = 0m,
                        UpdatedAtUtc = now
                    };
                    writes.Add(new PaperPositionSettlementWrite(settlement, settledPosition));
                }
                prepareDuration = Stopwatch.GetElapsedTime(phaseStarted);

                phase = "PersistSettlementBatch";
                phaseStarted = Stopwatch.GetTimestamp();
                var inserted = await repository.PersistPaperPositionSettlementBatchAsync(
                    writes,
                    persistenceDiagnostics.Observe,
                    cancellationToken);
                persistenceDuration = Stopwatch.GetElapsedTime(phaseStarted);

                phase = "ApplyExposureCache";
                phaseStarted = Stopwatch.GetTimestamp();
                foreach (var write in writes)
                {
                    exposureCache.ApplyPaperPosition(write.SettledPosition);
                }
                cacheDuration = Stopwatch.GetElapsedTime(phaseStarted);

                var totalDuration = Stopwatch.GetElapsedTime(operationStarted);
                var logLevel = totalDuration >= TimeSpan.FromSeconds(1) ? LogLevel.Warning : LogLevel.Debug;
                var persistenceSnapshot = persistenceDiagnostics.Capture();
                logger.Log(
                    logLevel,
                    "Paper resolution settlement completed. ConditionId={ConditionId} AssetId={AssetId} Positions={Positions} SettlementsInserted={SettlementsInserted} Attempt={Attempt} LoadDurationMs={LoadDurationMs} PrepareDurationMs={PrepareDurationMs} PersistenceDurationMs={PersistenceDurationMs} CacheDurationMs={CacheDurationMs} TotalDurationMs={TotalDurationMs} PersistenceStagesAvailability={PersistenceStagesAvailability} PersistenceLastStage={PersistenceLastStage} PersistenceFailedStage={PersistenceFailedStage} PersistenceSlowestStage={PersistenceSlowestStage} PersistenceSlowestDurationMs={PersistenceSlowestDurationMs} PersistenceStages={@PersistenceStages}",
                    conditionId,
                    assetId,
                    positions.Length,
                    inserted,
                    attempt,
                    loadDuration.TotalMilliseconds,
                    prepareDuration.TotalMilliseconds,
                    persistenceDuration.TotalMilliseconds,
                    cacheDuration.TotalMilliseconds,
                    totalDuration.TotalMilliseconds,
                    persistenceSnapshot.Availability,
                    persistenceSnapshot.LastStage,
                    persistenceSnapshot.FailedStage,
                    persistenceSnapshot.SlowestStage,
                    persistenceSnapshot.SlowestDurationMilliseconds,
                    persistenceSnapshot.Stages);
                return new PaperSettlementProcessingResult(positions.Length, positions.Length, inserted, 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.DeadlockDetected &&
                attempt < SettlementDeadlockMaximumAttempts)
            {
                var retryDelay = TimeSpan.FromMilliseconds(
                    SettlementDeadlockInitialRetryDelayMilliseconds * attempt);
                var persistenceSnapshot = persistenceDiagnostics.Capture();
                logger.LogWarning(
                    ex,
                    "Paper resolution settlement deadlocked. Reloading current positions before retry. ConditionId={ConditionId} AssetId={AssetId} Phase={Phase} Attempt={Attempt} NextAttempt={NextAttempt} RetryDelayMs={RetryDelayMs} DurationMs={DurationMs} PersistenceStagesAvailability={PersistenceStagesAvailability} PersistenceLastStage={PersistenceLastStage} PersistenceFailedStage={PersistenceFailedStage} PersistenceSlowestStage={PersistenceSlowestStage} PersistenceSlowestDurationMs={PersistenceSlowestDurationMs} PersistenceStages={@PersistenceStages}",
                    conditionId,
                    assetId,
                    phase,
                    attempt,
                    attempt + 1,
                    retryDelay.TotalMilliseconds,
                    Stopwatch.GetElapsedTime(operationStarted).TotalMilliseconds,
                    persistenceSnapshot.Availability,
                    persistenceSnapshot.LastStage,
                    persistenceSnapshot.FailedStage,
                    persistenceSnapshot.SlowestStage,
                    persistenceSnapshot.SlowestDurationMilliseconds,
                    persistenceSnapshot.Stages);
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                var persistenceSnapshot = persistenceDiagnostics.Capture();
                logger.LogError(
                    ex,
                    "Paper resolution settlement failed. ConditionId={ConditionId} AssetId={AssetId} Phase={Phase} Attempt={Attempt} DurationMs={DurationMs} PersistenceStagesAvailability={PersistenceStagesAvailability} PersistenceLastStage={PersistenceLastStage} PersistenceFailedStage={PersistenceFailedStage} PersistenceSlowestStage={PersistenceSlowestStage} PersistenceSlowestDurationMs={PersistenceSlowestDurationMs} PersistenceStages={@PersistenceStages}",
                    conditionId,
                    assetId,
                    phase,
                    attempt,
                    Stopwatch.GetElapsedTime(operationStarted).TotalMilliseconds,
                    persistenceSnapshot.Availability,
                    persistenceSnapshot.LastStage,
                    persistenceSnapshot.FailedStage,
                    persistenceSnapshot.SlowestStage,
                    persistenceSnapshot.SlowestDurationMilliseconds,
                    persistenceSnapshot.Stages);
                throw;
            }
        }

        throw new InvalidOperationException("Paper settlement retry loop completed without a result.");
    }

    private sealed record SettlementPersistenceSnapshot(
        string Availability,
        string? LastStage,
        string? FailedStage,
        string? SlowestStage,
        double? SlowestDurationMilliseconds,
        PaperSettlementPersistenceStageEvent[] Stages)
    {
        public static readonly SettlementPersistenceSnapshot NotAvailable =
            new("NotAvailable", null, null, null, null, []);
    }

    private sealed class SettlementPersistenceDiagnostics
    {
        private readonly List<PaperSettlementPersistenceStageEvent> stages = [];
        private string? lastStage;
        private string? failedStage;
        private bool collectionIncomplete;

        public void Observe(PaperSettlementPersistenceStageEvent observation)
        {
            try
            {
                if (observation is null || !IsKnownStage(observation.Stage) ||
                    !Enum.IsDefined(observation.Status))
                {
                    collectionIncomplete = true;
                    return;
                }

                if (observation.Status == PaperSettlementPersistenceStageStatus.Started)
                {
                    observation = observation with { DurationMilliseconds = null };
                }
                else if (observation.DurationMilliseconds is not double duration ||
                    !double.IsFinite(duration) || duration < 0)
                {
                    collectionIncomplete = true;
                    return;
                }

                lastStage = observation.Stage;
                var index = stages.FindIndex(stage => stage.Stage == observation.Stage);
                if (observation.Status == PaperSettlementPersistenceStageStatus.Failed)
                {
                    // Cleanup can be the last observed stage without replacing the
                    // first persistence failure that caused the attempt to unwind.
                    failedStage ??= observation.Stage;
                }

                if (index >= 0)
                    stages[index] = observation;
                else
                    stages.Add(observation);
            }
            catch (Exception)
            {
                // Diagnostics must never change the persistence result or retry path.
                collectionIncomplete = true;
            }
        }

        public SettlementPersistenceSnapshot Capture()
        {
            try
            {
                if (stages.Count == 0 && !collectionIncomplete)
                    return SettlementPersistenceSnapshot.NotAvailable;

                var slowest = stages
                    .Where(stage => stage.DurationMilliseconds.HasValue)
                    .OrderByDescending(stage => stage.DurationMilliseconds)
                    .FirstOrDefault();
                return new SettlementPersistenceSnapshot(
                    collectionIncomplete ? "Incomplete" : "Available",
                    lastStage,
                    failedStage,
                    slowest?.Stage,
                    slowest?.DurationMilliseconds,
                    stages.ToArray());
            }
            catch (Exception)
            {
                return SettlementPersistenceSnapshot.NotAvailable;
            }
        }

        private static bool IsKnownStage(string stage) => stage is
            PaperSettlementPersistenceStages.PrepareBatch or
            PaperSettlementPersistenceStages.OpenConnection or
            PaperSettlementPersistenceStages.BeginTransaction or
            PaperSettlementPersistenceStages.PreparePositions or
            PaperSettlementPersistenceStages.PreparePositionKeys or
            PaperSettlementPersistenceStages.SerializeWallets or
            PaperSettlementPersistenceStages.AcquireWalletLocks or
            PaperSettlementPersistenceStages.AcquirePositionLocks or
            PaperSettlementPersistenceStages.SerializePositions or
            PaperSettlementPersistenceStages.UpsertPositions or
            PaperSettlementPersistenceStages.PrepareSettlements or
            PaperSettlementPersistenceStages.SerializeSettlements or
            PaperSettlementPersistenceStages.InsertSettlements or
            PaperSettlementPersistenceStages.Commit or
            PaperSettlementPersistenceStages.DisposeTransaction or
            PaperSettlementPersistenceStages.DisposeConnection;
    }

    private async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetResolvedMetadataAsync(
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        var byToken = await gammaClient.GetTokenMetadataAsync(position.AssetId, closed: true, cancellationToken);
        var metadata = byToken.Count > 0
            ? byToken
            : await gammaClient.GetTokenMetadataByConditionIdAsync(position.ConditionId, position.AssetId, closed: true, cancellationToken);

        return metadata
            .Where(item => item.Resolved && !string.IsNullOrWhiteSpace(item.WinningOutcome))
            .ToArray();
    }

    private static bool IsWinningPosition(PaperPosition position, string? winningAssetId, string? winningOutcome)
    {
        return (!string.IsNullOrWhiteSpace(winningAssetId) &&
                string.Equals(position.AssetId, winningAssetId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(winningOutcome) &&
                string.Equals(position.Outcome, winningOutcome, StringComparison.OrdinalIgnoreCase));
    }

    private static Guid? ResolveStrategyId(string copiedTraderWallet)
    {
        const string strategyPrefix = "strategy:";
        if (copiedTraderWallet.StartsWith(strategyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return StrategyIds.TryGetStrategyIdByCode(copiedTraderWallet[strategyPrefix.Length..]);
        }

        return StrategyIds.FollowLeader;
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "PaperSettlementProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist paper settlement API error for {Operation}.", operation);
        }
    }
}
