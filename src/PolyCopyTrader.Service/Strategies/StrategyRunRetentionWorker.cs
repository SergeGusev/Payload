using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class StrategyRunRetentionWorker(
    ILogger<StrategyRunRetentionWorker> logger,
    StrategyRunRetentionOptions options,
    IAppRepository repository) : BackgroundService
{
    private StrategyRunRetentionCursor? continuationCursor;
    private DateTimeOffset? sweepUpdatedBeforeUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Strategy run retention is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(options.CleanupIntervalMinutes);
        logger.LogInformation(
            "Strategy run retention worker started. ApplyEnabled={ApplyEnabled} RawRetentionHours={RawRetentionHours} CleanupIntervalMinutes={CleanupIntervalMinutes} CleanupBatchSize={CleanupBatchSize} MaxBatchesPerCycle={MaxBatchesPerCycle}",
            options.ApplyEnabled,
            options.RawRetentionHours,
            options.CleanupIntervalMinutes,
            options.CleanupBatchSize,
            options.CleanupMaxBatchesPerCycle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Strategy run retention cycle failed; its current transaction was rolled back.");
            }

            await Task.Delay(interval, stoppingToken);
        }

        logger.LogInformation("Strategy run retention worker stopped.");
    }

    internal async Task<StrategyRunRetentionCycleResult> RunCycleAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var updatedBeforeUtc = sweepUpdatedBeforeUtc ??
            nowUtc.ToUniversalTime().AddHours(-options.RawRetentionHours);
        sweepUpdatedBeforeUtc ??= updatedBeforeUtc;
        var previewedRows = 0;
        var transferredRows = 0;
        var rollupRowsChanged = 0;
        var tombstonesChanged = 0;
        var strategiesQueued = 0;

        if (!options.ApplyEnabled)
        {
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                updatedBeforeUtc,
                options.CleanupBatchSize,
                continuationCursor,
                cancellationToken);
            logger.LogWarning(
                "Strategy run retention bounded preview completed in read-only mode. UpdatedBeforeUtc={UpdatedBeforeUtc:O} IntrinsicRowsScanned={IntrinsicRowsScanned} CandidateRows={CandidateRows} DistinctStrategies={DistinctStrategies} OldestUpdatedAtUtc={OldestUpdatedAtUtc:O} NewestUpdatedAtUtc={NewestUpdatedAtUtc:O} CandidateRunIds={CandidateRunIds} ReachedIntrinsicEnd={ReachedIntrinsicEnd}. No rows were transferred or deleted.",
                updatedBeforeUtc,
                preview.IntrinsicRowsScanned,
                preview.CandidateRunIds.Count,
                preview.DistinctStrategies,
                preview.OldestUpdatedAtUtc,
                preview.NewestUpdatedAtUtc,
                string.Join(',', preview.CandidateRunIds),
                preview.ReachedIntrinsicEnd);
            AdvanceContinuationCursor(preview);
            return new StrategyRunRetentionCycleResult(
                preview.CandidateRunIds.Count,
                0,
                0,
                0,
                0,
                false);
        }

        for (var batch = 0; batch < options.CleanupMaxBatchesPerCycle; batch++)
        {
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                updatedBeforeUtc,
                options.CleanupBatchSize,
                continuationCursor,
                cancellationToken);

            previewedRows += preview.CandidateRunIds.Count;
            logger.LogInformation(
                "Strategy run retention batch preview. ApplyEnabled={ApplyEnabled} UpdatedBeforeUtc={UpdatedBeforeUtc:O} IntrinsicRowsScanned={IntrinsicRowsScanned} CandidateRows={CandidateRows} DistinctStrategies={DistinctStrategies} OldestUpdatedAtUtc={OldestUpdatedAtUtc:O} NewestUpdatedAtUtc={NewestUpdatedAtUtc:O} CandidateRunIds={CandidateRunIds} ReachedIntrinsicEnd={ReachedIntrinsicEnd}",
                options.ApplyEnabled,
                updatedBeforeUtc,
                preview.IntrinsicRowsScanned,
                preview.CandidateRunIds.Count,
                preview.DistinctStrategies,
                preview.OldestUpdatedAtUtc,
                preview.NewestUpdatedAtUtc,
                string.Join(',', preview.CandidateRunIds),
                preview.ReachedIntrinsicEnd);

            if (preview.CandidateRunIds.Count > 0)
            {
                var result = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    preview.CandidateRunIds,
                    updatedBeforeUtc,
                    cancellationToken);
                transferredRows += result.DeletedRows;
                rollupRowsChanged += result.RollupRowsChanged;
                tombstonesChanged += result.TombstonesChanged;
                strategiesQueued += result.StrategiesQueuedForReconciliation;

                logger.LogInformation(
                    "Strategy run retention batch transferred atomically. SelectedRows={SelectedRows} DeletedRows={DeletedRows} RollupRowsChanged={RollupRowsChanged} TombstonesChanged={TombstonesChanged} StrategiesQueuedForReconciliation={StrategiesQueuedForReconciliation}",
                    result.SelectedRows,
                    result.DeletedRows,
                    result.RollupRowsChanged,
                    result.TombstonesChanged,
                    result.StrategiesQueuedForReconciliation);
            }

            AdvanceContinuationCursor(preview);
            if (preview.ReachedIntrinsicEnd)
            {
                break;
            }
        }

        return new StrategyRunRetentionCycleResult(
            previewedRows,
            transferredRows,
            rollupRowsChanged,
            tombstonesChanged,
            strategiesQueued,
            options.ApplyEnabled);
    }

    private void AdvanceContinuationCursor(StrategyRunRetentionPreview preview)
    {
        if (preview.IntrinsicRowsScanned < 0 ||
            preview.CandidateRunIds.Count > preview.IntrinsicRowsScanned)
        {
            throw new InvalidOperationException(
                "Strategy-run retention preview returned inconsistent intrinsic row counts.");
        }

        if (preview.ReachedIntrinsicEnd)
        {
            continuationCursor = null;
            sweepUpdatedBeforeUtc = null;
            return;
        }

        if (preview.IntrinsicRowsScanned == 0 || preview.ContinuationCursor is null)
        {
            throw new InvalidOperationException(
                "Strategy-run retention preview did not provide a continuation cursor before intrinsic end.");
        }

        continuationCursor = preview.ContinuationCursor;
    }
}

internal sealed record StrategyRunRetentionCycleResult(
    long PreviewedRows,
    int TransferredRows,
    int RollupRowsChanged,
    int TombstonesChanged,
    int StrategiesQueuedForReconciliation,
    bool ApplyEnabled);
