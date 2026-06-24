using System.Diagnostics;
using Npgsql;
using PolyCopyTrader.Domain;

const int DefaultBatchSize = 250;
const int DefaultPauseMs = 100;
const int DefaultLockTimeoutMs = 300;
const int DefaultStatementTimeoutMs = 8_000;
const int MaxConsecutiveTimeouts = 20;
const string ReportFileName = "result.txt";
const string TargetPattern = "^(btc|eth|sol)_up_down_5m_middle_[0-9]+(_revert)?(_bps_[0-9]+)?(_instant)?$";

var startedAt = DateTimeOffset.UtcNow;
var stopwatch = Stopwatch.StartNew();
var outputDirectory = AppContext.BaseDirectory;
var reportPath = Path.GetFullPath(Path.Combine(outputDirectory, "..", "..", "..", ReportFileName));
var errorPath = Path.GetFullPath(Path.Combine(outputDirectory, "..", "..", "..", "error.txt"));
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.Delete(errorPath);
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    try
    {
        File.AppendAllText(errorPath, $"{DateTimeOffset.UtcNow:O} {eventArgs.ExceptionObject}{Environment.NewLine}");
    }
    catch
    {
        // Best-effort crash diagnostics only.
    }
};

await using var report = new StreamWriter(reportPath, append: false);

var batchSize = GetIntArg(args, "--batch-size", DefaultBatchSize);
var pauseMs = GetIntArg(args, "--pause-ms", DefaultPauseMs);
var lockTimeoutMs = GetIntArg(args, "--lock-timeout-ms", DefaultLockTimeoutMs);
var statementTimeoutMs = GetIntArg(args, "--statement-timeout-ms", DefaultStatementTimeoutMs);
var execute = args.Contains("--execute", StringComparer.OrdinalIgnoreCase);
var dryRun = !execute || args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
var verifyOnly = args.Contains("--verify-only", StringComparer.OrdinalIgnoreCase);

var targetVariants = StrategyIds.UpDown5mStrategyVariants
    .Where(IsMiddleReferenceVariant)
    .OrderBy(variant => variant.Code, StringComparer.OrdinalIgnoreCase)
    .ToArray();

await WriteLineAsync($"Middle N strategy update started at {startedAt:O}");
await WriteLineAsync($"Target strategy code regex: {TargetPattern}");
await WriteLineAsync($"Catalog target Middle strategy rows: {targetVariants.Length}");
await WriteLineAsync($"Dry run: {dryRun}");
await WriteLineAsync($"Execute: {execute && !dryRun}");
await WriteLineAsync($"Verify only: {verifyOnly}");
await WriteLineAsync($"Batch size: {batchSize}");
await WriteLineAsync($"Batch pause ms: {pauseMs}");
await WriteLineAsync($"Batch lock timeout ms: {lockTimeoutMs}");
await WriteLineAsync($"Batch statement timeout ms: {statementTimeoutMs}");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "UpdateMiddleNStrategies"
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
if (string.IsNullOrWhiteSpace(hostOverride))
{
    hostOverride = "192.168.0.101";
}

connectionBuilder.Host = hostOverride;

await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
await connection.OpenAsync();

await ExecuteNonQueryAsync(connection, """
SET statement_timeout = '10min';
SET lock_timeout = '3s';
SET idle_in_transaction_session_timeout = '60s';
""");

await WriteScalarAsync(connection, "Database", "SELECT current_database();");
await WriteScalarAsync(connection, "Server address", "SELECT inet_server_addr()::text;");
await WriteScalarAsync(connection, "Server time UTC", "SELECT now() AT TIME ZONE 'UTC';");
await WriteScalarAsync(connection, "Last heartbeat UTC", """
SELECT COALESCE(to_char(max(last_heartbeat_utc AT TIME ZONE 'UTC'), 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), '<none>')
FROM service_heartbeats;
""");

await CreateCatalogTableAsync(connection, targetVariants);
await CreateTargetTablesAsync(connection);
await WriteTargetSummaryAsync(connection, dryRun ? "dry-run before" : "before");

if (verifyOnly)
{
    await WriteLineAsync($"Middle N strategy verification finished at {DateTimeOffset.UtcNow:O}");
    return;
}

if (dryRun)
{
    await WriteLineAsync("Dry run requested; no rows were changed.");
    return;
}

await AbortIfOpenLiveOrdersAsync(connection);

await DeleteInBatchesAsync(connection, "paper_live_shadow_discrepancies", """
WITH victim AS (
    SELECT d.id
    FROM paper_live_shadow_discrepancies d
    WHERE d.strategy_id IN (SELECT id FROM cleanup_middle_strategy_ids)
       OR d.correlation_id IN (SELECT correlation_id FROM cleanup_middle_shadow_correlation_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM paper_live_shadow_discrepancies d
    USING victim
    WHERE d.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_live_shadow_decisions", """
WITH victim AS (
    SELECT d.correlation_id
    FROM paper_live_shadow_decisions d
    WHERE d.correlation_id IN (SELECT correlation_id FROM cleanup_middle_shadow_correlation_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM paper_live_shadow_decisions d
    USING victim
    WHERE d.correlation_id = victim.correlation_id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteStrategyMarketPaperRunsByStrategyAsync(connection);

await DeleteInBatchesAsync(connection, "dry_run_orders", """
WITH victim AS (
    SELECT d.id
    FROM dry_run_orders d
    WHERE d.strategy_id IN (SELECT id FROM cleanup_middle_strategy_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM dry_run_orders d
    USING victim
    WHERE d.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "live_orders", """
WITH victim AS (
    SELECT l.id
    FROM live_orders l
    WHERE l.id IN (SELECT id FROM cleanup_middle_live_order_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM live_orders l
    USING victim
    WHERE l.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_fills", """
WITH victim AS (
    DELETE FROM cleanup_middle_paper_fill_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_middle_paper_fill_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM paper_fills f
    USING victim
    WHERE f.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_position_settlements", """
WITH victim AS (
    DELETE FROM cleanup_middle_settlement_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_middle_settlement_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM paper_position_settlements s
    USING victim
    WHERE s.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_positions", """
WITH victim AS (
    DELETE FROM cleanup_middle_position_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_middle_position_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM paper_positions p
    USING victim
    WHERE p.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_copied_trader_performance", """
WITH victim AS (
    DELETE FROM cleanup_middle_performance_keys target
    WHERE (target.copied_trader_wallet, target.category) IN (
        SELECT copied_trader_wallet, category
        FROM cleanup_middle_performance_keys
        LIMIT @batch_size
    )
    RETURNING target.copied_trader_wallet, target.category
),
deleted AS (
    DELETE FROM paper_copied_trader_performance p
    USING victim
    WHERE p.copied_trader_wallet = victim.copied_trader_wallet
      AND p.category = victim.category
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await RefreshSignalIdsFromCurrentTargetRowsAsync(connection);

await DeleteInBatchesAsync(connection, "paper_orders", """
WITH victim AS (
    DELETE FROM cleanup_middle_paper_order_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_middle_paper_order_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM paper_orders o
    USING victim
    WHERE o.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await RebuildDeletableSignalTableAsync(connection);
await WriteTargetSummaryAsync(connection, "after dependent deletes before signal delete");

await DeleteInBatchesAsync(connection, "signal_rejections", """
WITH victim AS (
    SELECT r.id
    FROM signal_rejections r
    WHERE r.signal_id IN (SELECT id FROM cleanup_middle_signal_delete_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM signal_rejections r
    USING victim
    WHERE r.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "signals", """
WITH victim AS (
    DELETE FROM cleanup_middle_signal_delete_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_middle_signal_delete_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM signals s
    USING victim
    WHERE s.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "strategies", """
WITH victim AS (
    SELECT s.id
    FROM strategies s
    JOIN cleanup_middle_strategy_ids target
        ON target.id = s.id
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM strategies s
    USING victim
    WHERE s.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await UpsertTargetStrategiesAsync(connection, targetVariants);

await CreateTargetTablesAsync(connection, rebuildStrategyTargets: true);
await WriteTargetSummaryAsync(connection, "after");
await WriteLineAsync($"Middle N strategy update finished at {DateTimeOffset.UtcNow:O}");

async Task CreateCatalogTableAsync(NpgsqlConnection db, IReadOnlyList<BtcUpDown5mStrategyVariant> variants)
{
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_middle_strategy_catalog;
CREATE TEMP TABLE cleanup_middle_strategy_catalog (
    id uuid PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    description text NOT NULL
) ON COMMIT PRESERVE ROWS;
""");

    await using var importer = await db.BeginBinaryImportAsync(
        "COPY cleanup_middle_strategy_catalog (id, code, name, description) FROM STDIN (FORMAT BINARY)");
    foreach (var variant in variants)
    {
        await importer.StartRowAsync();
        await importer.WriteAsync(variant.Id, NpgsqlTypes.NpgsqlDbType.Uuid);
        await importer.WriteAsync(variant.Code, NpgsqlTypes.NpgsqlDbType.Text);
        await importer.WriteAsync(variant.Name, NpgsqlTypes.NpgsqlDbType.Text);
        await importer.WriteAsync(variant.Description, NpgsqlTypes.NpgsqlDbType.Text);
    }

    await importer.CompleteAsync();
}

async Task UpsertTargetStrategiesAsync(NpgsqlConnection db, IReadOnlyList<BtcUpDown5mStrategyVariant> _)
{
    await WriteLineAsync("Upserting Middle N catalog rows while preserving enabled flags...");
    var upserted = await ExecuteScalarIntAsync(db, """
WITH upserted AS (
INSERT INTO strategies (id, code, name, description, enabled, paper_stake_amount, created_at_utc, updated_at_utc)
SELECT id, code, name, description, false, 1.00, now(), now()
FROM cleanup_middle_strategy_catalog
ON CONFLICT (id) DO UPDATE SET
    code = excluded.code,
    name = excluded.name,
    description = excluded.description,
    updated_at_utc = excluded.updated_at_utc
RETURNING 1
)
SELECT count(*)::int FROM upserted;
""");
    await WriteLineAsync($"Upserted/refreshed Middle N catalog rows: {upserted}");
}

async Task CreateTargetTablesAsync(NpgsqlConnection db, bool rebuildStrategyTargets = true)
{
    if (rebuildStrategyTargets)
    {
        await WriteLineAsync("Preparing off-catalog target strategy ids and synthetic wallets...");
        await ExecuteNonQueryAsync(db, $"""
DROP TABLE IF EXISTS cleanup_middle_strategy_ids;
CREATE TEMP TABLE cleanup_middle_strategy_ids (
    id uuid PRIMARY KEY,
    code text NOT NULL
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_strategy_ids (id, code)
SELECT strategy.id, strategy.code
FROM strategies strategy
WHERE strategy.code ~ '{TargetPattern}'
  AND NOT EXISTS (
      SELECT 1
      FROM cleanup_middle_strategy_catalog catalog
      WHERE catalog.id = strategy.id
  )
ON CONFLICT (id) DO UPDATE SET code = excluded.code;

DROP TABLE IF EXISTS cleanup_middle_wallets;
CREATE TEMP TABLE cleanup_middle_wallets (
    copied_trader_wallet text PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_wallets (copied_trader_wallet)
SELECT 'strategy:' || code
FROM cleanup_middle_strategy_ids
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_strategy_ids;
ANALYZE cleanup_middle_wallets;
""");
    }
    else
    {
        await WriteLineAsync("Reusing target strategy ids and synthetic wallets...");
    }

    await PrepareTargetStepAsync(db, "paper order ids", """
DROP TABLE IF EXISTS cleanup_middle_paper_order_ids;
CREATE TEMP TABLE cleanup_middle_paper_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
JOIN cleanup_middle_strategy_ids target
    ON target.id = paper_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
JOIN cleanup_middle_wallets target
    ON target.copied_trader_wallet = paper_order.copied_trader_wallet
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_paper_order_ids;
""");

    await PrepareTargetStepAsync(db, "paper fill ids", """
DROP TABLE IF EXISTS cleanup_middle_paper_fill_ids;
CREATE TEMP TABLE cleanup_middle_paper_fill_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_paper_fill_ids (id)
SELECT fill.id
FROM paper_fills fill
JOIN cleanup_middle_paper_order_ids target
    ON target.id = fill.paper_order_id
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_paper_fill_ids;
""");

    await PrepareTargetStepAsync(db, "paper run ids table", """
DROP TABLE IF EXISTS cleanup_middle_run_ids;
CREATE TEMP TABLE cleanup_middle_run_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;
""");

    await PrepareTargetStepAsync(db, "signal ids", """
DROP TABLE IF EXISTS cleanup_middle_signal_ids;
CREATE TEMP TABLE cleanup_middle_signal_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
JOIN cleanup_middle_paper_order_ids target
    ON target.id = paper_order.id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_signal_ids (id)
SELECT decision.signal_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_middle_strategy_ids target
    ON target.id = decision.strategy_id
WHERE decision.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_signal_ids (id)
SELECT live_order.signal_id
FROM live_orders live_order
JOIN cleanup_middle_strategy_ids target
    ON target.id = live_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_signal_ids (id)
SELECT dry_order.signal_id
FROM dry_run_orders dry_order
JOIN cleanup_middle_strategy_ids target
    ON target.id = dry_order.strategy_id
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_signal_ids;
""");

    await PrepareTargetStepAsync(db, "live order ids", """
DROP TABLE IF EXISTS cleanup_middle_live_order_ids;
CREATE TEMP TABLE cleanup_middle_live_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_live_order_ids (id)
SELECT live_order.id
FROM live_orders live_order
JOIN cleanup_middle_strategy_ids target
    ON target.id = live_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_live_order_ids (id)
SELECT live_order.id
FROM live_orders live_order
JOIN cleanup_middle_paper_order_ids target
    ON target.id = live_order.paper_order_id
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_live_order_ids;
""");

    await PrepareTargetStepAsync(db, "shadow correlation ids", """
DROP TABLE IF EXISTS cleanup_middle_shadow_correlation_ids;
CREATE TEMP TABLE cleanup_middle_shadow_correlation_ids (
    correlation_id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_middle_strategy_ids target
    ON target.id = decision.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_middle_paper_order_ids target
    ON target.id = decision.paper_order_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_middle_live_order_ids target
    ON target.id = decision.live_order_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_shadow_correlation_ids (correlation_id)
SELECT live_order.correlation_id
FROM live_orders live_order
JOIN cleanup_middle_live_order_ids target
    ON target.id = live_order.id
WHERE live_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_middle_shadow_correlation_ids (correlation_id)
SELECT paper_order.correlation_id
FROM paper_orders paper_order
JOIN cleanup_middle_paper_order_ids target
    ON target.id = paper_order.id
WHERE paper_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

ANALYZE cleanup_middle_shadow_correlation_ids;
""");

    await PrepareTargetStepAsync(db, "settlement ids", """
DROP TABLE IF EXISTS cleanup_middle_settlement_ids;
CREATE TEMP TABLE cleanup_middle_settlement_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_settlement_ids (id)
SELECT settlement.id
FROM paper_position_settlements settlement
JOIN cleanup_middle_wallets target
    ON target.copied_trader_wallet = settlement.copied_trader_wallet;

ANALYZE cleanup_middle_settlement_ids;
""");

    await PrepareTargetStepAsync(db, "position ids", """
DROP TABLE IF EXISTS cleanup_middle_position_ids;
CREATE TEMP TABLE cleanup_middle_position_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_position_ids (id)
SELECT position.id
FROM paper_positions position
JOIN cleanup_middle_wallets target
    ON target.copied_trader_wallet = position.copied_trader_wallet;

ANALYZE cleanup_middle_position_ids;
""");

    await PrepareTargetStepAsync(db, "performance keys", """
DROP TABLE IF EXISTS cleanup_middle_performance_keys;
CREATE TEMP TABLE cleanup_middle_performance_keys (
    copied_trader_wallet text NOT NULL,
    category text NOT NULL,
    PRIMARY KEY (copied_trader_wallet, category)
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_performance_keys (copied_trader_wallet, category)
SELECT performance.copied_trader_wallet, performance.category
FROM paper_copied_trader_performance performance
JOIN cleanup_middle_wallets target
    ON target.copied_trader_wallet = performance.copied_trader_wallet;

ANALYZE cleanup_middle_performance_keys;
""");
}

async Task PrepareTargetStepAsync(NpgsqlConnection db, string label, string sql)
{
    var stepStopwatch = Stopwatch.StartNew();
    await WriteLineAsync($"Preparing {label}...");
    await ExecuteNonQueryAsync(db, sql);
    await WriteLineAsync($"Prepared {label} in {stepStopwatch.Elapsed.TotalSeconds:N1}s.");
}

async Task RefreshSignalIdsFromCurrentTargetRowsAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Refreshing target signal ids from current target rows before deleting paper orders...");
    await ExecuteNonQueryAsync(db, """
INSERT INTO cleanup_middle_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
JOIN cleanup_middle_paper_order_ids target
    ON target.id = paper_order.id
ON CONFLICT DO NOTHING;
""");
}

async Task RebuildDeletableSignalTableAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Preparing deletable target signal ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_middle_signal_delete_ids;
CREATE TEMP TABLE cleanup_middle_signal_delete_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_middle_signal_delete_ids (id)
SELECT signal.id
FROM cleanup_middle_signal_ids signal;

ANALYZE cleanup_middle_signal_delete_ids;
""");

    await RemoveStillReferencedSignalIdsAsync(db, "paper_orders", "paper_orders", "signal_id");
    await RemoveStillReferencedSignalIdsAsync(db, "strategy_market_paper_runs", "strategy_market_paper_runs", "signal_id");
    await RemoveStillReferencedSignalIdsAsync(db, "paper_live_shadow_decisions", "paper_live_shadow_decisions", "signal_id");
    await RemoveStillReferencedSignalIdsAsync(db, "live_orders", "live_orders", "signal_id");
    await RemoveStillReferencedSignalIdsAsync(db, "dry_run_orders", "dry_run_orders", "signal_id");
}

async Task RemoveStillReferencedSignalIdsAsync(NpgsqlConnection db, string label, string tableName, string signalColumnName)
{
    var removed = await ExecuteScalarIntAsync(db, $"""
WITH removed AS (
    DELETE FROM cleanup_middle_signal_delete_ids target
    USING {tableName} ref
    WHERE ref.{signalColumnName} = target.id
    RETURNING 1
)
SELECT count(*)::int
FROM removed;
""");

    await WriteLineAsync($"Preparing deletable target signal ids: kept referenced by {label}: {removed}.");
}

async Task AbortIfOpenLiveOrdersAsync(NpgsqlConnection db)
{
    const string sql = """
SELECT count(*)::int
FROM live_orders
WHERE id IN (SELECT id FROM cleanup_middle_live_order_ids)
  AND (
      lower(status) IN ('created', 'queued', 'validated', 'submitted', 'open', 'live', 'unmatched', 'partiallymatched', 'pending', 'cancelrequested')
      OR lower(cancel_status) IN ('requested', 'pending')
      OR (
          remaining_size > 0
          AND lower(status) NOT IN ('matched', 'rejected', 'preflightrejected', 'cancelled', 'cancelfailed')
      )
  )
  AND settled_at_utc IS NULL;
""";
    var openLiveOrders = await ExecuteScalarIntAsync(db, sql);
    await WriteLineAsync($"Open/unsettled target live_orders guard count: {openLiveOrders}");
    if (openLiveOrders > 0)
    {
        throw new InvalidOperationException("Refusing cleanup because open/unsettled Middle live orders exist. Cancel/settle them before deleting history.");
    }
}

async Task WriteTargetSummaryAsync(NpgsqlConnection db, string stage)
{
    await WriteLineAsync($"Target summary ({stage}):");
    var summaries = new (string Label, string Sql)[]
    {
        ("target catalog strategies", "SELECT count(*)::int FROM cleanup_middle_strategy_catalog;"),
        ("off-catalog middle strategy ids", "SELECT count(*)::int FROM cleanup_middle_strategy_ids;"),
        ("all database middle strategy rows", $"SELECT count(*)::int FROM strategies WHERE code ~ '{TargetPattern}';"),
        ("enabled catalog strategies", "SELECT count(*)::int FROM strategies WHERE id IN (SELECT id FROM cleanup_middle_strategy_catalog) AND enabled;"),
        ("old middle_1 strategy codes", "SELECT count(*)::int FROM strategies WHERE code ~ '^(btc|eth|sol)_up_down_5m_middle_1(_revert)?(_bps_[0-9]+)?(_instant)?$';"),
        ("paper_orders", "SELECT count(*)::int FROM paper_orders WHERE id IN (SELECT id FROM cleanup_middle_paper_order_ids);"),
        ("paper_fills", "SELECT count(*)::int FROM paper_fills WHERE id IN (SELECT id FROM cleanup_middle_paper_fill_ids);"),
        ("live_orders", "SELECT count(*)::int FROM live_orders WHERE id IN (SELECT id FROM cleanup_middle_live_order_ids);"),
        ("dry_run_orders", "SELECT count(*)::int FROM dry_run_orders WHERE strategy_id IN (SELECT id FROM cleanup_middle_strategy_ids);"),
        ("paper_live_shadow_decisions", "SELECT count(*)::int FROM paper_live_shadow_decisions WHERE correlation_id IN (SELECT correlation_id FROM cleanup_middle_shadow_correlation_ids);"),
        ("paper_live_shadow_discrepancies", "SELECT count(*)::int FROM paper_live_shadow_discrepancies WHERE strategy_id IN (SELECT id FROM cleanup_middle_strategy_ids) OR correlation_id IN (SELECT correlation_id FROM cleanup_middle_shadow_correlation_ids);"),
        ("paper_positions", "SELECT count(*)::int FROM paper_positions WHERE id IN (SELECT id FROM cleanup_middle_position_ids);"),
        ("paper_position_settlements", "SELECT count(*)::int FROM paper_position_settlements WHERE id IN (SELECT id FROM cleanup_middle_settlement_ids);"),
        ("paper_copied_trader_performance keys", "SELECT count(*)::int FROM cleanup_middle_performance_keys;"),
        ("signals target ids", "SELECT count(*)::int FROM cleanup_middle_signal_ids;")
    };

    foreach (var summary in summaries)
    {
        await WriteScalarAsync(db, $"  {summary.Label}", summary.Sql);
    }

    await WriteLineAsync("  strategy_market_paper_runs: counted during direct batched delete.");
}

async Task DeleteInBatchesAsync(NpgsqlConnection db, string label, string sql)
{
    var total = 0;
    var batchNumber = 0;
    var consecutiveTimeouts = 0;
    while (true)
    {
        await using var transaction = await db.BeginTransactionAsync();
        try
        {
            await using (var timeoutCommand = new NpgsqlCommand($"""
SET LOCAL lock_timeout = '{lockTimeoutMs}ms';
SET LOCAL statement_timeout = '{statementTimeoutMs}ms';
""", db, transaction))
            {
                await timeoutCommand.ExecuteNonQueryAsync();
            }

            await using var command = new NpgsqlCommand(sql, db, transaction);
            command.Parameters.AddWithValue("batch_size", batchSize);
            command.CommandTimeout = Math.Max(1, (statementTimeoutMs / 1000) + 5);
            var deleted = Convert.ToInt32(await command.ExecuteScalarAsync());
            await transaction.CommitAsync();

            if (deleted == 0)
            {
                await WriteLineAsync($"{label}: deleted {total} row(s) total.");
                return;
            }

            batchNumber++;
            total += deleted;
            consecutiveTimeouts = 0;
            await WriteLineAsync($"{label}: batch {batchNumber} deleted {deleted}, total {total}.");
            if (pauseMs > 0)
            {
                await Task.Delay(pauseMs);
            }
        }
        catch (PostgresException ex) when (ex.SqlState is "55P03" or "57014")
        {
            await transaction.RollbackAsync();
            consecutiveTimeouts++;
            await WriteLineAsync($"{label}: timeout/lock batch retry {consecutiveTimeouts}/{MaxConsecutiveTimeouts}. SqlState={ex.SqlState}");
            if (consecutiveTimeouts >= MaxConsecutiveTimeouts)
            {
                throw;
            }

            await Task.Delay(Math.Max(pauseMs, 500));
        }
    }
}

async Task DeleteStrategyMarketPaperRunsByStrategyAsync(NpgsqlConnection db)
{
    var strategyIds = await ReadGuidListAsync(db, "SELECT id FROM cleanup_middle_strategy_ids ORDER BY code;");
    await WriteLineAsync($"strategy_market_paper_runs: deleting by {strategyIds.Count} strategy id(s) in batches of {batchSize}.");

    var total = 0;
    var processedStrategies = 0;
    foreach (var strategyId in strategyIds)
    {
        processedStrategies++;
        var strategyTotal = 0;
        var strategyBatchNumber = 0;
        var consecutiveTimeouts = 0;

        while (true)
        {
            await using var transaction = await db.BeginTransactionAsync();
            try
            {
                await using (var timeoutCommand = new NpgsqlCommand($"""
SET LOCAL lock_timeout = '{lockTimeoutMs}ms';
SET LOCAL statement_timeout = '{statementTimeoutMs}ms';
""", db, transaction))
                {
                    await timeoutCommand.ExecuteNonQueryAsync();
                }

                await using var command = new NpgsqlCommand("""
WITH victim AS (
    SELECT id
    FROM strategy_market_paper_runs
    WHERE strategy_id = @strategy_id
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM strategy_market_paper_runs run
    USING victim
    WHERE run.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""", db, transaction);
                command.Parameters.AddWithValue("strategy_id", NpgsqlTypes.NpgsqlDbType.Uuid, strategyId);
                command.Parameters.AddWithValue("batch_size", batchSize);
                command.CommandTimeout = Math.Max(1, (statementTimeoutMs / 1000) + 5);
                var deleted = Convert.ToInt32(await command.ExecuteScalarAsync());
                await transaction.CommitAsync();

                if (deleted == 0)
                {
                    if (strategyTotal > 0)
                    {
                        await WriteLineAsync($"strategy_market_paper_runs: strategy {processedStrategies}/{strategyIds.Count} deleted {strategyTotal}, total {total}.");
                    }
                    else if (processedStrategies == 1 || processedStrategies % 100 == 0 || processedStrategies == strategyIds.Count)
                    {
                        await WriteLineAsync($"strategy_market_paper_runs: strategy {processedStrategies}/{strategyIds.Count}, total {total}.");
                    }

                    break;
                }

                strategyBatchNumber++;
                strategyTotal += deleted;
                total += deleted;
                consecutiveTimeouts = 0;

                if (strategyBatchNumber == 1 || strategyBatchNumber % 25 == 0)
                {
                    await WriteLineAsync($"strategy_market_paper_runs: strategy {processedStrategies}/{strategyIds.Count} batch {strategyBatchNumber} deleted {deleted}, strategy total {strategyTotal}, total {total}.");
                }

                if (pauseMs > 0)
                {
                    await Task.Delay(pauseMs);
                }
            }
            catch (PostgresException ex) when (ex.SqlState is "55P03" or "57014")
            {
                await transaction.RollbackAsync();
                consecutiveTimeouts++;
                await WriteLineAsync($"strategy_market_paper_runs: strategy {processedStrategies}/{strategyIds.Count} timeout/lock retry {consecutiveTimeouts}/{MaxConsecutiveTimeouts}. SqlState={ex.SqlState}");
                if (consecutiveTimeouts >= MaxConsecutiveTimeouts)
                {
                    throw;
                }

                await Task.Delay(Math.Max(pauseMs, 500));
            }
        }
    }

    await WriteLineAsync($"strategy_market_paper_runs: deleted {total} row(s) total.");
}

async Task<List<Guid>> ReadGuidListAsync(NpgsqlConnection db, string sql)
{
    var ids = new List<Guid>();
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        ids.Add(reader.GetGuid(0));
    }

    return ids;
}

async Task WriteScalarAsync(NpgsqlConnection db, string label, string sql)
{
    var value = await ExecuteScalarAsync(db, sql);
    await WriteLineAsync($"{label}: {value}");
}

async Task<object?> ExecuteScalarAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    return await command.ExecuteScalarAsync();
}

async Task<int> ExecuteScalarIntAsync(NpgsqlConnection db, string sql)
{
    var value = await ExecuteScalarAsync(db, sql);
    return Convert.ToInt32(value);
}

async Task ExecuteNonQueryAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    await command.ExecuteNonQueryAsync();
}

async Task WriteLineAsync(string line)
{
    var text = $"{DateTimeOffset.UtcNow:O} {line}";
    Console.WriteLine(text);
    await report.WriteLineAsync(text);
    await report.FlushAsync();
}

static bool IsMiddleReferenceVariant(BtcUpDown5mStrategyVariant variant)
{
    return variant.Behavior is BtcUpDown5mStrategyBehavior.MiddleReference or
        BtcUpDown5mStrategyBehavior.MiddleReferenceRevert or
        BtcUpDown5mStrategyBehavior.MiddleReferenceInstant or
        BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant;
}

static int GetIntArg(string[] args, string name, int fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[i + 1], out var value) &&
            value > 0)
        {
            return value;
        }
    }

    return fallback;
}
