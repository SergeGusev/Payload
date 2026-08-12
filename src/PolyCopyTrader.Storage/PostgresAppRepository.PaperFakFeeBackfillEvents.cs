using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public async Task AddPaperFakFeeBackfillEventAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, """
INSERT INTO paper_fak_fee_backfill_events (
    id, worker_instance_id, sweep_id, cycle_id, sequence, occurred_at_utc,
    level, event_type, message, build_version, host_name, process_id,
    backfill_enabled, apply_enabled, cutoff_utc, batch_size,
    pending_paper_entry_batches, pending_market_data_updates, delay_seconds,
    strategy_id, strategy_code, strategy_rank, strategy_count, gross_realized_pnl_usd,
    candidates, evaluated_for_apply, transient_lookup_unavailable,
    requested, eligible, full_chain_eligible, run_only_legacy_eligible,
    fills_updated, runs_updated, positions_updated, settlements_updated,
    full_chain_already_applied, run_only_legacy_already_applied, already_applied,
    structural_conflicts, accounting_conflicts,
    deferred_by_lock_timeout, deferred_by_query_cancel,
    reached_strategy_end, reached_sweep_end, duration_milliseconds,
    exception_type, exception_message
) VALUES (
    @Id, @WorkerInstanceId, @SweepId, @CycleId, @Sequence, @OccurredAtUtc,
    @Level, @EventType, @Message, @BuildVersion, @HostName, @ProcessId,
    @BackfillEnabled, @ApplyEnabled, @CutoffUtc, @BatchSize,
    @PendingPaperEntryBatches, @PendingMarketDataUpdates, @DelaySeconds,
    @StrategyId, @StrategyCode, @StrategyRank, @StrategyCount, @GrossRealizedPnlUsd,
    @Candidates, @EvaluatedForApply, @TransientLookupUnavailable,
    @Requested, @Eligible, @FullChainEligible, @RunOnlyLegacyEligible,
    @FillsUpdated, @RunsUpdated, @PositionsUpdated, @SettlementsUpdated,
    @FullChainAlreadyApplied, @RunOnlyLegacyAlreadyApplied, @AlreadyApplied,
    @StructuralConflicts, @AccountingConflicts,
    @DeferredByLockTimeout, @DeferredByQueryCancel,
    @ReachedStrategyEnd, @ReachedSweepEnd, @DurationMilliseconds,
    @ExceptionType, @ExceptionMessage
);
""");

        AddPaperFakFeeBackfillEventParameter(command, "Id", NpgsqlDbType.Uuid, entry.Id);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "WorkerInstanceId",
            NpgsqlDbType.Uuid,
            entry.WorkerInstanceId);
        AddPaperFakFeeBackfillEventParameter(command, "SweepId", NpgsqlDbType.Uuid, entry.SweepId);
        AddPaperFakFeeBackfillEventParameter(command, "CycleId", NpgsqlDbType.Uuid, entry.CycleId);
        AddPaperFakFeeBackfillEventParameter(command, "Sequence", NpgsqlDbType.Bigint, entry.Sequence);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "OccurredAtUtc",
            NpgsqlDbType.TimestampTz,
            entry.OccurredAtUtc);
        AddPaperFakFeeBackfillEventParameter(command, "Level", NpgsqlDbType.Text, entry.Level);
        AddPaperFakFeeBackfillEventParameter(command, "EventType", NpgsqlDbType.Text, entry.EventType);
        AddPaperFakFeeBackfillEventParameter(command, "Message", NpgsqlDbType.Text, entry.Message);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "BuildVersion",
            NpgsqlDbType.Text,
            entry.BuildVersion);
        AddPaperFakFeeBackfillEventParameter(command, "HostName", NpgsqlDbType.Text, entry.HostName);
        AddPaperFakFeeBackfillEventParameter(command, "ProcessId", NpgsqlDbType.Integer, entry.ProcessId);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "BackfillEnabled",
            NpgsqlDbType.Boolean,
            entry.BackfillEnabled);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "ApplyEnabled",
            NpgsqlDbType.Boolean,
            entry.ApplyEnabled);
        AddPaperFakFeeBackfillEventParameter(command, "CutoffUtc", NpgsqlDbType.TimestampTz, entry.CutoffUtc);
        AddPaperFakFeeBackfillEventParameter(command, "BatchSize", NpgsqlDbType.Integer, entry.BatchSize);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "PendingPaperEntryBatches",
            NpgsqlDbType.Integer,
            entry.PendingPaperEntryBatches);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "PendingMarketDataUpdates",
            NpgsqlDbType.Integer,
            entry.PendingMarketDataUpdates);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "DelaySeconds",
            NpgsqlDbType.Integer,
            entry.DelaySeconds);
        AddPaperFakFeeBackfillEventParameter(command, "StrategyId", NpgsqlDbType.Uuid, entry.StrategyId);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "StrategyCode",
            NpgsqlDbType.Text,
            entry.StrategyCode);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "StrategyRank",
            NpgsqlDbType.Integer,
            entry.StrategyRank);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "StrategyCount",
            NpgsqlDbType.Integer,
            entry.StrategyCount);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "GrossRealizedPnlUsd",
            NpgsqlDbType.Numeric,
            entry.GrossRealizedPnlUsd);
        AddPaperFakFeeBackfillEventParameter(command, "Candidates", NpgsqlDbType.Integer, entry.Candidates);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "EvaluatedForApply",
            NpgsqlDbType.Integer,
            entry.EvaluatedForApply);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "TransientLookupUnavailable",
            NpgsqlDbType.Integer,
            entry.TransientLookupUnavailable);
        AddPaperFakFeeBackfillEventParameter(command, "Requested", NpgsqlDbType.Integer, entry.Requested);
        AddPaperFakFeeBackfillEventParameter(command, "Eligible", NpgsqlDbType.Integer, entry.Eligible);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "FullChainEligible",
            NpgsqlDbType.Integer,
            entry.FullChainEligible);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "RunOnlyLegacyEligible",
            NpgsqlDbType.Integer,
            entry.RunOnlyLegacyEligible);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "FillsUpdated",
            NpgsqlDbType.Integer,
            entry.FillsUpdated);
        AddPaperFakFeeBackfillEventParameter(command, "RunsUpdated", NpgsqlDbType.Integer, entry.RunsUpdated);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "PositionsUpdated",
            NpgsqlDbType.Integer,
            entry.PositionsUpdated);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "SettlementsUpdated",
            NpgsqlDbType.Integer,
            entry.SettlementsUpdated);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "FullChainAlreadyApplied",
            NpgsqlDbType.Integer,
            entry.FullChainAlreadyApplied);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "RunOnlyLegacyAlreadyApplied",
            NpgsqlDbType.Integer,
            entry.RunOnlyLegacyAlreadyApplied);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "AlreadyApplied",
            NpgsqlDbType.Integer,
            entry.AlreadyApplied);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "StructuralConflicts",
            NpgsqlDbType.Integer,
            entry.StructuralConflicts);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "AccountingConflicts",
            NpgsqlDbType.Integer,
            entry.AccountingConflicts);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "DeferredByLockTimeout",
            NpgsqlDbType.Integer,
            entry.DeferredByLockTimeout);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "DeferredByQueryCancel",
            NpgsqlDbType.Integer,
            entry.DeferredByQueryCancel);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "ReachedStrategyEnd",
            NpgsqlDbType.Boolean,
            entry.ReachedStrategyEnd);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "ReachedSweepEnd",
            NpgsqlDbType.Boolean,
            entry.ReachedSweepEnd);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "DurationMilliseconds",
            NpgsqlDbType.Bigint,
            entry.DurationMilliseconds);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "ExceptionType",
            NpgsqlDbType.Text,
            entry.ExceptionType);
        AddPaperFakFeeBackfillEventParameter(
            command,
            "ExceptionMessage",
            NpgsqlDbType.Text,
            entry.ExceptionMessage);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CleanupPaperFakFeeBackfillEventsAsync(
        DateTimeOffset occurredBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return 0;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, """
WITH selected AS (
    SELECT id
    FROM paper_fak_fee_backfill_events
    WHERE occurred_at_utc < @OccurredBeforeUtc
    ORDER BY occurred_at_utc ASC, id ASC
    LIMIT @BatchSize
    FOR UPDATE SKIP LOCKED
)
DELETE FROM paper_fak_fee_backfill_events events
USING selected
WHERE events.id = selected.id;
""");
        command.Parameters.Add("OccurredBeforeUtc", NpgsqlDbType.TimestampTz).Value =
            UtcDateTime(occurredBeforeUtc);
        command.Parameters.Add("BatchSize", NpgsqlDbType.Integer).Value = batchSize;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddPaperFakFeeBackfillEventParameter(
        NpgsqlCommand command,
        string parameterName,
        NpgsqlDbType parameterType,
        object? value)
    {
        command.Parameters.Add(parameterName, parameterType).Value = value switch
        {
            DateTimeOffset timestamp => UtcDateTime(timestamp),
            null => DBNull.Value,
            _ => value
        };
    }
}
