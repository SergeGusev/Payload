using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Strategies;

public sealed class StrategyRunRetentionWorker(
    ILogger<StrategyRunRetentionWorker> logger,
    StrategyRunRetentionOptions options,
    IAppRepository repository) : BackgroundService
{
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
        var updatedBeforeUtc = nowUtc.ToUniversalTime().AddHours(-options.RawRetentionHours);
        var previewedRows = 0;
        var transferredRows = 0;
        var rollupRowsChanged = 0;
        var tombstonesChanged = 0;
        var strategiesQueued = 0;

        if (!options.ApplyEnabled)
        {
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(
                updatedBeforeUtc,
                options.CleanupBatchSize,
                cancellationToken);
            logger.LogWarning(
                "Strategy run retention exact preview completed in read-only mode. UpdatedBeforeUtc={UpdatedBeforeUtc:O} TotalCandidateRows={TotalCandidateRows} DistinctStrategies={DistinctStrategies} OldestUpdatedAtUtc={OldestUpdatedAtUtc:O} NewestUpdatedAtUtc={NewestUpdatedAtUtc:O} SampleRunIds={SampleRunIds}. No rows were transferred or deleted.",
                updatedBeforeUtc,
                summary.TotalCandidateRows,
                summary.DistinctStrategies,
                summary.OldestUpdatedAtUtc,
                summary.NewestUpdatedAtUtc,
                string.Join(',', summary.SampleRunIds));
            return new StrategyRunRetentionCycleResult(
                summary.TotalCandidateRows,
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
                cancellationToken);
            if (preview.CandidateRunIds.Count == 0)
            {
                break;
            }

            previewedRows += preview.CandidateRunIds.Count;
            logger.LogInformation(
                "Strategy run retention batch preview. ApplyEnabled={ApplyEnabled} UpdatedBeforeUtc={UpdatedBeforeUtc:O} CandidateRows={CandidateRows} DistinctStrategies={DistinctStrategies} OldestUpdatedAtUtc={OldestUpdatedAtUtc:O} NewestUpdatedAtUtc={NewestUpdatedAtUtc:O} CandidateRunIds={CandidateRunIds}",
                options.ApplyEnabled,
                updatedBeforeUtc,
                preview.CandidateRunIds.Count,
                preview.DistinctStrategies,
                preview.OldestUpdatedAtUtc,
                preview.NewestUpdatedAtUtc,
                string.Join(',', preview.CandidateRunIds));

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

            if (preview.CandidateRunIds.Count < options.CleanupBatchSize)
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
}

internal sealed record StrategyRunRetentionCycleResult(
    long PreviewedRows,
    int TransferredRows,
    int RollupRowsChanged,
    int TombstonesChanged,
    int StrategiesQueuedForReconciliation,
    bool ApplyEnabled);
