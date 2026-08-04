using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int DirectPaperSkipCompactionBatchSize = 25_000;

    private const string DirectPaperSkipBusinessBlockerCtes = """
candidate_strategy_keys AS MATERIALIZED (
    SELECT
        candidate.id,
        candidate.condition_id,
        candidate.updated_at_utc,
        strategy.live_enabled_at_utc,
        lower('strategy:' || strategy.code) AS copied_trader_wallet_key
    FROM candidate_batch candidate
    INNER JOIN public.strategies strategy ON strategy.id = candidate.strategy_id
),
blocker_hits AS (
    SELECT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    WHERE candidate_key.live_enabled_at_utc IS NOT NULL
      AND candidate_key.updated_at_utc >= candidate_key.live_enabled_at_utc

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.dry_run_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.live_orders dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_live_shadow_decisions dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.market_id = candidate.market_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_live_shadow_decisions dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    INNER JOIN public.paper_positions dependency
        ON lower(dependency.copied_trader_wallet) = candidate_key.copied_trader_wallet_key
       AND dependency.condition_id = candidate_key.condition_id

    UNION ALL

    SELECT DISTINCT candidate_key.id
    FROM candidate_strategy_keys candidate_key
    INNER JOIN public.paper_position_settlements dependency
        ON lower(dependency.copied_trader_wallet) = candidate_key.copied_trader_wallet_key
       AND dependency.condition_id = candidate_key.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_copied_leader_positions dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.paper_copied_leader_activity_events dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.polymarket_onchain_paper_signal_results dependency
        ON dependency.condition_id = candidate.condition_id

    UNION ALL

    SELECT DISTINCT candidate.id
    FROM candidate_batch candidate
    INNER JOIN public.strategy_market_paper_skip_tombstones dependency
        ON dependency.strategy_id = candidate.strategy_id
       AND dependency.market_id = candidate.market_id
),
blocked_candidate_ids AS MATERIALIZED (
    SELECT DISTINCT blocker_hit.id
    FROM blocker_hits blocker_hit
)
""";

    private const string DirectStrategyRunInsertSql = """
WITH run_rows AS (
    SELECT *
    FROM jsonb_to_recordset(@RunsJson) AS run_row(
        id uuid,
        strategy_id uuid,
        market_id text,
        condition_id text,
        market_slug text,
        market_title text,
        category text,
        market_start_utc timestamptz,
        market_end_utc timestamptz,
        detected_at_utc timestamptz,
        entry_due_at_utc timestamptz,
        status text,
        selected_asset_id text,
        selected_outcome text,
        entry_price numeric,
        stake_usd numeric,
        size_shares numeric,
        signal_id uuid,
        paper_order_id uuid,
        entered_at_utc timestamptz,
        settlement_price numeric,
        settlement_value_usd numeric,
        realized_pnl_usd numeric,
        settled_at_utc timestamptz,
        skip_reason text,
        created_at_utc timestamptz,
        updated_at_utc timestamptz,
        skip_diagnostics_json text
    )
)
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc
)
SELECT
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, CAST(skip_diagnostics_json AS jsonb), created_at_utc, updated_at_utc
FROM run_rows
WHERE NOT EXISTS (
    SELECT 1
    FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.strategy_id = run_rows.strategy_id
      AND tombstone.market_id = run_rows.market_id
)
ON CONFLICT (strategy_id, market_id) DO NOTHING
RETURNING id;
""";

    public Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !ContainsSkippedRun(runs))
        {
            return TryAddStrategyMarketPaperRunsAsync(runs, cancellationToken);
        }

        return TryAddStrategyMarketPaperRunsWithDirectCompactionAsync(runs, cancellationToken);
    }

    public Task FinalizeStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !IsSkippedRun(run))
        {
            return UpdateStrategyMarketPaperRunAsync(run, cancellationToken);
        }

        return FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync([run], cancellationToken);
    }

    public Task FinalizeStrategyMarketPaperRunsAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        bool directPaperSkipCompactionEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!directPaperSkipCompactionEnabled || !ContainsSkippedRun(runs))
        {
            return UpdateStrategyMarketPaperRunsAsync(runs, cancellationToken);
        }

        return FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync(runs, cancellationToken);
    }

    private async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsWithDirectCompactionAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return new HashSet<Guid>();
        }

        return await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                var insertedIds = new HashSet<Guid>();
                for (var offset = 0; offset < runs.Count; offset += StrategyMarketPaperRunInsertBatchSize)
                {
                    var count = Math.Min(StrategyMarketPaperRunInsertBatchSize, runs.Count - offset);
                    var batch = new StrategyMarketPaperRun[count];
                    var skippedIds = new HashSet<Guid>();
                    for (var index = 0; index < count; index++)
                    {
                        var run = runs[offset + index];
                        batch[index] = run with
                        {
                            StrategyId = StrategyIds.Normalize(run.StrategyId),
                            SkipDiagnosticsJson = GetPersistedSkipDiagnosticsJson(run)
                        };
                        if (IsSkippedRun(run))
                        {
                            skippedIds.Add(run.Id);
                        }
                    }

                    await using var command = CreateCommand(connection, DirectStrategyRunInsertSql);
                    command.Transaction = transaction;
                    command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value =
                        JsonSerializer.Serialize(batch, BulkInsertJsonOptions);
                    var insertedSkippedIds = new List<Guid>();
                    await using (var reader = await command.ExecuteReaderAsync(token))
                    {
                        while (await reader.ReadAsync(token))
                        {
                            var insertedId = reader.GetGuid(0);
                            insertedIds.Add(insertedId);
                            if (skippedIds.Contains(insertedId))
                            {
                                insertedSkippedIds.Add(insertedId);
                            }
                        }
                    }

                    await CompactDirectPaperSkippedRunsAsync(
                        connection,
                        transaction,
                        insertedSkippedIds,
                        token);
                }

                return (IReadOnlySet<Guid>)insertedIds;
            },
            cancellationToken);
    }

    private async Task FinalizeStrategyMarketPaperRunsWithDirectCompactionAsync(
        IReadOnlyList<StrategyMarketPaperRun> runs,
        CancellationToken cancellationToken)
    {
        if (runs.Count == 0)
        {
            return;
        }

        await ExecuteWithExclusiveStrategyRunRetentionGateAsync(
            async (connection, transaction, token) =>
            {
                await UpdateStrategyMarketPaperRunsBatchAsync(connection, transaction, runs, token);
                await CompactDirectPaperSkippedRunsAsync(
                    connection,
                    transaction,
                    runs.Where(IsSkippedRun).Select(run => run.Id).Distinct().ToArray(),
                    token);
                return true;
            },
            cancellationToken);
    }

    private async Task AddPaperEntryPersistenceBatchWithDirectCompactionAsync(
        PaperEntryPersistenceBatch batch,
        CancellationToken cancellationToken)
    {
        await ExecuteWithPaperPositionLocksAndExclusiveStrategyRunRetentionGateAsync(
            batch.PaperPositions,
            batch.PaperOrders.Select(order => order.CopiedTraderWallet).ToArray(),
            async (connection, transaction, token) =>
            {
                await AddSignalsBatchAsync(connection, transaction, batch.Signals, token);
                await UpsertPaperPositionsBatchAsync(connection, transaction, batch.PaperPositions, token);
                await AddPaperOrdersBatchAsync(connection, transaction, batch.PaperOrders, token);
                await AddPaperFillsBatchAsync(connection, transaction, batch.PaperFills, token);
                await ActivatePaperCopiedLeaderPositionsBatchAsync(
                    connection,
                    transaction,
                    batch.CopiedLeaderPositionActivations,
                    token);
                await UpdateStrategyMarketPaperRunsBatchAsync(
                    connection,
                    transaction,
                    batch.StrategyRuns,
                    token);
                await CompactDirectPaperSkippedRunsAsync(
                    connection,
                    transaction,
                    batch.StrategyRuns
                        .Where(IsSkippedRun)
                        .Select(run => run.Id)
                        .Distinct()
                        .ToArray(),
                    token);
                return true;
            },
            cancellationToken);
    }

    private async Task<T> ExecuteWithPaperPositionLocksAndExclusiveStrategyRunRetentionGateAsync<T>(
        IReadOnlyList<PaperPosition> positions,
        IReadOnlyCollection<string> additionalWallets,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var gateHeld = false;
        try
        {
            await using (var transaction = await connection.BeginTransactionAsync(
                             IsolationLevel.ReadCommitted,
                             cancellationToken))
            {
                // Normal Paper position/settlement writers take these locks before
                // the retention-shared trigger. Keep the same global lock order here
                // so direct compaction cannot deadlock with them under load.
                await LockPaperPositionKeysAsync(
                    connection,
                    transaction,
                    positions,
                    additionalWallets,
                    cancellationToken);

                await using (var gateCommand = CreateCommand(
                                 connection,
                                 "SELECT public.lock_strategy_run_retention_transfer();"))
                {
                    gateCommand.Transaction = transaction;
                    gateCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
                    await gateCommand.ExecuteNonQueryAsync(cancellationToken);
                    gateHeld = true;
                }

                await using (var settingsCommand = CreateCommand(
                                 connection,
                                 "SET LOCAL polycopytrader.skip_run_retention_transfer = 'on';"))
                {
                    settingsCommand.Transaction = transaction;
                    await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                var result = await action(connection, transaction, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
        }
        finally
        {
            await ReleaseExclusiveStrategyRunRetentionGateAsync(connection, gateHeld);
        }
    }

    private static async Task CompactDirectPaperSkippedRunsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<Guid> candidateRunIds,
        CancellationToken cancellationToken)
    {
        if (candidateRunIds.Count == 0)
        {
            return;
        }

        foreach (var runIdBatch in candidateRunIds
                     .Distinct()
                     .Chunk(DirectPaperSkipCompactionBatchSize))
        {
            await using var command = CreateCommand(
                connection,
                $$"""
WITH candidate_batch AS MATERIALIZED (
    SELECT
        run.id,
        run.strategy_id,
        run.market_id,
        run.condition_id,
        run.market_slug,
        run.market_title,
        run.category,
        run.market_start_utc,
        run.market_end_utc,
        run.detected_at_utc,
        run.entry_due_at_utc,
        run.selected_asset_id,
        run.selected_outcome,
        run.stake_usd,
        run.skip_reason,
        run.created_at_utc,
        run.updated_at_utc,
        date_trunc('day', run.updated_at_utc AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS rollup_bucket_start_utc
    FROM public.strategy_market_paper_runs run
    WHERE run.id = ANY(@RunIds)
      AND run.status = 'Skipped'
      AND run.retention_scope = 'PaperOnly'
      AND NULLIF(btrim(COALESCE(run.skip_reason, '')), '') IS NOT NULL
      AND run.signal_id IS NULL
      AND run.paper_order_id IS NULL
      AND run.entered_at_utc IS NULL
      AND run.entry_price IS NULL
      AND run.size_shares IS NULL
      AND run.settlement_price IS NULL
      AND run.settlement_value_usd IS NULL
      AND run.realized_pnl_usd IS NULL
      AND run.settled_at_utc IS NULL
      AND run.skip_diagnostics_json IS NULL
    ORDER BY run.strategy_id, run.market_id, run.id
    FOR UPDATE OF run
),
{{DirectPaperSkipBusinessBlockerCtes}},
candidates AS MATERIALIZED (
    SELECT candidate.*
    FROM candidate_batch candidate
    LEFT JOIN blocked_candidate_ids blocked ON blocked.id = candidate.id
    WHERE blocked.id IS NULL
    ORDER BY candidate.strategy_id, candidate.market_id, candidate.id
),
tombstones AS (
    INSERT INTO public.strategy_market_paper_skip_tombstones (
        strategy_id,
        market_id,
        archived_run_id,
        archived_at_utc,
        archive_format_version,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc,
        detected_at_utc,
        entry_due_at_utc,
        selected_asset_id,
        selected_outcome,
        stake_usd,
        skip_reason,
        run_created_at_utc,
        run_updated_at_utc,
        rollup_bucket_start_utc)
    SELECT
        candidate.strategy_id,
        candidate.market_id,
        candidate.id,
        clock_timestamp(),
        1,
        candidate.condition_id,
        candidate.market_slug,
        candidate.market_title,
        candidate.category,
        candidate.market_start_utc,
        candidate.market_end_utc,
        candidate.detected_at_utc,
        candidate.entry_due_at_utc,
        candidate.selected_asset_id,
        candidate.selected_outcome,
        candidate.stake_usd,
        candidate.skip_reason,
        candidate.created_at_utc,
        candidate.updated_at_utc,
        candidate.rollup_bucket_start_utc
    FROM candidates candidate
    ON CONFLICT (strategy_id, market_id) DO NOTHING
    RETURNING
        strategy_id,
        archived_run_id,
        rollup_bucket_start_utc,
        skip_reason,
        run_updated_at_utc
),
rollups AS (
    INSERT INTO public.strategy_paper_skip_rollups AS existing_rollup (
        strategy_id,
        bucket_start_utc,
        skip_reason,
        run_count,
        first_updated_at_utc,
        last_updated_at_utc,
        created_at_utc,
        updated_at_utc)
    SELECT
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason,
        count(*)::integer,
        min(tombstone.run_updated_at_utc),
        max(tombstone.run_updated_at_utc),
        clock_timestamp(),
        clock_timestamp()
    FROM tombstones tombstone
    GROUP BY
        tombstone.strategy_id,
        tombstone.rollup_bucket_start_utc,
        tombstone.skip_reason
    ON CONFLICT (strategy_id, bucket_start_utc, skip_reason) DO UPDATE SET
        run_count = existing_rollup.run_count + EXCLUDED.run_count,
        first_updated_at_utc = LEAST(
            existing_rollup.first_updated_at_utc,
            EXCLUDED.first_updated_at_utc),
        last_updated_at_utc = GREATEST(
            existing_rollup.last_updated_at_utc,
            EXCLUDED.last_updated_at_utc),
        updated_at_utc = clock_timestamp()
    RETURNING 1
),
deleted AS (
    DELETE FROM public.strategy_market_paper_runs run
    USING tombstones tombstone
    WHERE run.id = tombstone.archived_run_id
    RETURNING run.id
),
queued AS (
    INSERT INTO public.dashboard_projection_reconciliation_queue AS existing_queue (
        strategy_id,
        priority,
        reason,
        requested_at_utc,
        attempt_count,
        next_attempt_at_utc,
        last_error)
    SELECT
        distinct_tombstone.strategy_id,
        50,
        'direct_paper_skip_compaction',
        clock_timestamp(),
        0,
        clock_timestamp(),
        NULL
    FROM (
        SELECT DISTINCT tombstone.strategy_id
        FROM tombstones tombstone
    ) distinct_tombstone
    ON CONFLICT (strategy_id) DO UPDATE SET
        priority = GREATEST(existing_queue.priority, EXCLUDED.priority),
        reason = EXCLUDED.reason,
        requested_at_utc = LEAST(
            existing_queue.requested_at_utc,
            EXCLUDED.requested_at_utc),
        next_attempt_at_utc = LEAST(
            existing_queue.next_attempt_at_utc,
            EXCLUDED.next_attempt_at_utc),
        last_error = NULL
    RETURNING 1
)
SELECT
    (SELECT count(*)::integer FROM candidates),
    (SELECT count(*)::integer FROM deleted),
    (SELECT count(*)::integer FROM rollups),
    (SELECT count(*)::integer FROM tombstones),
    (SELECT count(*)::integer FROM queued);
""");
            command.Transaction = transaction;
            command.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
            command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIdBatch;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Direct Paper skipped-run compaction returned no batch result.");
            }

            var result = new StrategyRunRetentionBatchResult(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4));
            await reader.DisposeAsync();

            if (result.SelectedRows != result.DeletedRows ||
                result.SelectedRows != result.TombstonesChanged)
            {
                throw new InvalidOperationException(
                    $"Direct Paper skipped-run compaction invariant failed: " +
                    $"selected={result.SelectedRows}, deleted={result.DeletedRows}, " +
                    $"tombstones={result.TombstonesChanged}.");
            }
        }
    }

    private async Task<T> ExecuteWithExclusiveStrategyRunRetentionGateAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var gateHeld = false;
        try
        {
            await using (var gateCommand = CreateCommand(
                connection,
                "SELECT public.lock_strategy_run_retention_transfer();"))
            {
                gateCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
                await gateCommand.ExecuteNonQueryAsync(cancellationToken);
                gateHeld = true;
            }

            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await using (var settingsCommand = CreateCommand(
                connection,
                "SET LOCAL polycopytrader.skip_run_retention_transfer = 'on';"))
            {
                settingsCommand.Transaction = transaction;
                await settingsCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var result = await action(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            await ReleaseExclusiveStrategyRunRetentionGateAsync(connection, gateHeld);
        }
    }

    private static async Task ReleaseExclusiveStrategyRunRetentionGateAsync(
        NpgsqlConnection connection,
        bool gateHeld)
    {
        if (!gateHeld)
        {
            NpgsqlConnection.ClearPool(connection);
            return;
        }

        try
        {
            await using var unlockCommand = CreateCommand(
                connection,
                "SELECT public.unlock_strategy_run_retention_transfer();");
            unlockCommand.CommandTimeout = StrategyRunRetentionCommandTimeoutSeconds;
            var unlocked = await unlockCommand.ExecuteScalarAsync(CancellationToken.None);
            if (unlocked is not true)
            {
                NpgsqlConnection.ClearPool(connection);
                throw new InvalidOperationException(
                    "Strategy-run retention session gate was not held during direct-compaction release.");
            }
        }
        catch
        {
            NpgsqlConnection.ClearPool(connection);
            throw;
        }
    }

    private static bool ContainsSkippedRun(IReadOnlyList<StrategyMarketPaperRun> runs)
    {
        return runs.Any(IsSkippedRun);
    }

    private static bool IsSkippedRun(StrategyMarketPaperRun run)
    {
        return string.Equals(
            run.Status,
            StrategyMarketPaperRunStatuses.Skipped,
            StringComparison.OrdinalIgnoreCase);
    }
}
