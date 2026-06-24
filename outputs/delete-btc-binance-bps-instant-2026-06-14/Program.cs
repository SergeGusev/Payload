using System.Diagnostics;
using Npgsql;

const int DefaultBatchSize = 100;
const int DefaultPauseMs = 150;
const int DefaultLockTimeoutMs = 300;
const int DefaultStatementTimeoutMs = 8_000;
const int MaxConsecutiveTimeouts = 20;
const string ReportFileName = "result.txt";
const string TargetPattern = "^btc_up_down_5m_binance_bps_[0-9]+_instant$";

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

await WriteLineAsync($"BTC Binance bps Instant strategy cleanup started at {startedAt:O}");
await WriteLineAsync($"Target strategy code regex: {TargetPattern}");
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
    ApplicationName = "DeleteBtcBinanceBpsInstantStrategies"
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
SET statement_timeout = '120s';
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

if (verifyOnly)
{
    await CreateTargetTablesAsync(connection);
    await WriteTargetSummaryAsync(connection, "verify");
    await WriteResidualPatternScanAsync(connection);
    await WriteLineAsync($"BTC Binance bps Instant residual verification finished at {DateTimeOffset.UtcNow:O}");
    return;
}

await CreateTargetTablesAsync(connection);
await WriteForeignKeysAsync(connection);
await WriteTargetStrategiesAsync(connection);
await WriteTargetSummaryAsync(connection, "before");
await AbortIfOpenLiveOrdersAsync(connection);

if (dryRun)
{
    await WriteLineAsync("Dry run requested; no rows were deleted.");
    return;
}

var disabledStrategies = await ExecuteScalarIntAsync(connection, """
WITH updated AS (
UPDATE strategies
SET enabled = false,
    live_stakes = false,
    auto_live_paused = false,
    auto_live_paused_at_utc = NULL,
    auto_live_pause_window_start_utc = NULL,
    live_enabled_at_utc = NULL,
    updated_at_utc = now()
WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
  AND (enabled OR live_stakes OR auto_live_paused OR auto_live_paused_at_utc IS NOT NULL OR auto_live_pause_window_start_utc IS NOT NULL OR live_enabled_at_utc IS NOT NULL)
RETURNING 1
)
SELECT count(*)::int FROM updated;
""");
await WriteLineAsync($"Disabled/de-live-updated target strategies: {disabledStrategies}");
if (disabledStrategies > 0)
{
    await WriteLineAsync("Waiting 10 seconds for running service strategy-state caches to expire before refreshing cleanup targets...");
    await Task.Delay(TimeSpan.FromSeconds(10));
    await CreateTargetTablesAsync(connection);
    await WriteTargetSummaryAsync(connection, "after disable refresh");
    await AbortIfOpenLiveOrdersAsync(connection);
}

await DeleteInBatchesAsync(connection, "paper_live_shadow_discrepancies", """
WITH victim AS (
    SELECT d.id
    FROM paper_live_shadow_discrepancies d
    WHERE d.strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
       OR d.correlation_id IN (SELECT correlation_id FROM cleanup_btc_binance_bps_instant_shadow_correlation_ids)
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
    WHERE d.strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
       OR d.paper_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
       OR d.live_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_live_order_ids)
       OR d.signal_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_ids)
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

await DeleteInBatchesAsync(connection, "strategy_market_paper_runs", """
WITH victim AS (
    DELETE FROM cleanup_btc_binance_bps_instant_run_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_btc_binance_bps_instant_run_ids
        LIMIT @batch_size
    )
    RETURNING target.id
),
deleted AS (
    DELETE FROM strategy_market_paper_runs r
    USING victim
    WHERE r.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "dry_run_orders", """
WITH victim AS (
    SELECT d.id
    FROM dry_run_orders d
    WHERE d.strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
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
    WHERE l.id IN (SELECT id FROM cleanup_btc_binance_bps_instant_live_order_ids)
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
    SELECT f.id
    FROM paper_fills f
    WHERE f.paper_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
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
    DELETE FROM cleanup_btc_binance_bps_instant_settlement_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_btc_binance_bps_instant_settlement_ids
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
    DELETE FROM cleanup_btc_binance_bps_instant_position_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_btc_binance_bps_instant_position_ids
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
    DELETE FROM cleanup_btc_binance_bps_instant_performance_keys target
    WHERE (target.copied_trader_wallet, target.category) IN (
        SELECT copied_trader_wallet, category
        FROM cleanup_btc_binance_bps_instant_performance_keys
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
    SELECT o.id
    FROM paper_orders o
    WHERE o.id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
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
    WHERE r.signal_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_delete_ids)
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
    SELECT s.id
    FROM signals s
    WHERE s.id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_delete_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
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
    WHERE s.id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
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

await WriteTargetSummaryAsync(connection, "after");
await WriteResidualPatternScanAsync(connection);
await WriteLineAsync($"BTC Binance bps Instant strategy cleanup finished at {DateTimeOffset.UtcNow:O}");

async Task CreateTargetTablesAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Preparing target strategy ids...");
    await ExecuteNonQueryAsync(db, $"""
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_strategy_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_strategy_ids (
    id uuid PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    enabled boolean NOT NULL,
    live_stakes boolean NOT NULL
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_strategy_ids (id, code, name, enabled, live_stakes)
SELECT id, code, name, enabled, live_stakes
FROM strategies
WHERE code ~ '{TargetPattern}';

DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_wallets;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_wallets (
    copied_trader_wallet text PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_wallets (copied_trader_wallet)
SELECT 'strategy:' || code
FROM cleanup_btc_binance_bps_instant_strategy_ids
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_wallets (copied_trader_wallet)
SELECT 'strategy:btc_up_down_5m_binance_bps_' || threshold_bps::text || '_instant'
FROM generate_series(1, 50) threshold_bps
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing target paper order ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_paper_order_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_paper_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = paper_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
JOIN cleanup_btc_binance_bps_instant_wallets target
    ON target.copied_trader_wallet = paper_order.copied_trader_wallet
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing target run ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_run_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_run_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_run_ids (id)
SELECT run.id
FROM strategy_market_paper_runs run
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = run.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_run_ids (id)
SELECT run.id
FROM strategy_market_paper_runs run
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = run.paper_order_id
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing target signal ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_signal_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_signal_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = paper_order.id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT run.signal_id
FROM strategy_market_paper_runs run
JOIN cleanup_btc_binance_bps_instant_run_ids target
    ON target.id = run.id
WHERE run.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT decision.signal_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = decision.strategy_id
WHERE decision.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT live_order.signal_id
FROM live_orders live_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = live_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT dry_order.signal_id
FROM dry_run_orders dry_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = dry_order.strategy_id
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing target live order ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_live_order_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_live_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_live_order_ids (id)
SELECT live_order.id
FROM live_orders live_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = live_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_live_order_ids (id)
SELECT live_order.id
FROM live_orders live_order
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = live_order.paper_order_id
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing target correlation ids...");
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_shadow_correlation_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_shadow_correlation_ids (
    correlation_id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = decision.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = decision.paper_order_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_shadow_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_btc_binance_bps_instant_live_order_ids target
    ON target.id = decision.live_order_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_shadow_correlation_ids (correlation_id)
SELECT live_order.correlation_id
FROM live_orders live_order
JOIN cleanup_btc_binance_bps_instant_live_order_ids target
    ON target.id = live_order.id
WHERE live_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_shadow_correlation_ids (correlation_id)
SELECT paper_order.correlation_id
FROM paper_orders paper_order
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = paper_order.id
WHERE paper_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;
""");

    await WriteLineAsync("Preparing safe synthetic wallet ids...");
    await ExecuteNonQueryAsync(db, $"""
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_wallet_candidates;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_wallet_candidates (
    copied_trader_wallet text PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_wallet_candidates (copied_trader_wallet)
SELECT copied_trader_wallet
FROM cleanup_btc_binance_bps_instant_wallets
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_wallet_candidates (copied_trader_wallet)
SELECT DISTINCT copied_trader_wallet
FROM paper_orders
WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_wallet_candidates (copied_trader_wallet)
SELECT DISTINCT copied_trader_wallet
FROM paper_positions
WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_wallet_candidates (copied_trader_wallet)
SELECT DISTINCT copied_trader_wallet
FROM paper_position_settlements
WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_wallet_candidates (copied_trader_wallet)
SELECT DISTINCT copied_trader_wallet
FROM paper_copied_trader_performance
WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets)
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_safe_wallets;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_safe_wallets (
    copied_trader_wallet text PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_safe_wallets (copied_trader_wallet)
SELECT copied_trader_wallet
FROM cleanup_btc_binance_bps_instant_wallet_candidates candidate
WHERE NOT EXISTS (
    SELECT 1
    FROM paper_orders orders
    WHERE orders.copied_trader_wallet = candidate.copied_trader_wallet
      AND orders.id NOT IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
);

DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_settlement_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_settlement_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_settlement_ids (id)
SELECT settlement.id
FROM paper_position_settlements settlement
JOIN cleanup_btc_binance_bps_instant_safe_wallets target
    ON target.copied_trader_wallet = settlement.copied_trader_wallet;

DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_position_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_position_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_position_ids (id)
SELECT position.id
FROM paper_positions position
JOIN cleanup_btc_binance_bps_instant_safe_wallets target
    ON target.copied_trader_wallet = position.copied_trader_wallet;

DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_performance_keys;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_performance_keys (
    copied_trader_wallet text NOT NULL,
    category text NOT NULL,
    PRIMARY KEY (copied_trader_wallet, category)
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_performance_keys (copied_trader_wallet, category)
SELECT performance.copied_trader_wallet, performance.category
FROM paper_copied_trader_performance performance
JOIN cleanup_btc_binance_bps_instant_safe_wallets target
    ON target.copied_trader_wallet = performance.copied_trader_wallet;
""");
}

async Task RefreshSignalIdsFromCurrentTargetRowsAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Refreshing target signal ids from current target rows before deleting paper orders...");
    await ExecuteNonQueryAsync(db, """
INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
JOIN cleanup_btc_binance_bps_instant_paper_order_ids target
    ON target.id = paper_order.id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT run.signal_id
FROM strategy_market_paper_runs run
JOIN cleanup_btc_binance_bps_instant_run_ids target
    ON target.id = run.id
WHERE run.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT decision.signal_id
FROM paper_live_shadow_decisions decision
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = decision.strategy_id
WHERE decision.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT live_order.signal_id
FROM live_orders live_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = live_order.strategy_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_btc_binance_bps_instant_signal_ids (id)
SELECT dry_order.signal_id
FROM dry_run_orders dry_order
JOIN cleanup_btc_binance_bps_instant_strategy_ids target
    ON target.id = dry_order.strategy_id
ON CONFLICT DO NOTHING;
""");
}

async Task RebuildDeletableSignalTableAsync(NpgsqlConnection db)
{
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_btc_binance_bps_instant_signal_delete_ids;
CREATE TEMP TABLE cleanup_btc_binance_bps_instant_signal_delete_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_btc_binance_bps_instant_signal_delete_ids (id)
SELECT target.id
FROM cleanup_btc_binance_bps_instant_signal_ids target
WHERE EXISTS (
    SELECT 1
    FROM signals signal
    WHERE signal.id = target.id
)
AND NOT EXISTS (
    SELECT 1
    FROM paper_orders orders
    WHERE orders.signal_id = target.id
)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_market_paper_runs runs
    WHERE runs.signal_id = target.id
)
AND NOT EXISTS (
    SELECT 1
    FROM paper_live_shadow_decisions decisions
    WHERE decisions.signal_id = target.id
)
AND NOT EXISTS (
    SELECT 1
    FROM live_orders live
    WHERE live.signal_id = target.id
)
AND NOT EXISTS (
    SELECT 1
    FROM dry_run_orders dry
    WHERE dry.signal_id = target.id
);
""");
}

async Task WriteForeignKeysAsync(NpgsqlConnection db)
{
    const string sql = """
SELECT conrelid::regclass::text AS child_table,
       confrelid::regclass::text AS parent_table,
       conname
FROM pg_constraint
WHERE contype = 'f'
  AND confrelid IN ('strategies'::regclass, 'signals'::regclass, 'paper_orders'::regclass, 'live_orders'::regclass)
ORDER BY parent_table, child_table, conname;
""";

    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    await WriteLineAsync("Foreign keys touching cleanup parents:");
    while (await reader.ReadAsync())
    {
        await WriteLineAsync($"  {reader.GetString(0)} -> {reader.GetString(1)} ({reader.GetString(2)})");
    }
}

async Task WriteTargetStrategiesAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Target strategy rows:");
    await using var command = new NpgsqlCommand("""
SELECT code, name, enabled, live_stakes
FROM cleanup_btc_binance_bps_instant_strategy_ids
ORDER BY (regexp_match(code, '^btc_up_down_5m_binance_bps_([0-9]+)_instant$'))[1]::integer;
""", db);
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    var count = 0;
    while (await reader.ReadAsync())
    {
        count++;
        await WriteLineAsync($"  {reader.GetString(0)} | enabled={reader.GetBoolean(2)} | live_stakes={reader.GetBoolean(3)} | {reader.GetString(1)}");
    }

    if (count == 0)
    {
        await WriteLineAsync("  <none>");
    }
}

async Task WriteTargetSummaryAsync(NpgsqlConnection db, string stage)
{
    await WriteLineAsync($"Target summary ({stage}):");
    var summaries = new (string Label, string Sql)[]
    {
        ("target strategies in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_strategy_ids"),
        ("remaining strategies by exact code regex", $"SELECT count(*)::int FROM strategies WHERE code ~ '{TargetPattern}';"),
        ("target wallet candidates", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_wallet_candidates"),
        ("safe wallets", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_safe_wallets"),
        ("target paper_orders in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_paper_order_ids"),
        ("remaining target paper_orders", "SELECT count(*)::int FROM paper_orders WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)"),
        ("remaining target paper_fills", "SELECT count(*)::int FROM paper_fills WHERE paper_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)"),
        ("target run ids in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_run_ids"),
        ("remaining target runs", "SELECT count(*)::int FROM strategy_market_paper_runs WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_run_ids)"),
        ("target signal ids in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_signal_ids"),
        ("remaining target signals", "SELECT count(*)::int FROM signals WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_ids)"),
        ("remaining target signal_rejections", "SELECT count(*)::int FROM signal_rejections WHERE signal_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_ids)"),
        ("remaining target dry_run_orders", "SELECT count(*)::int FROM dry_run_orders WHERE strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)"),
        ("target live_orders in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_live_order_ids"),
        ("remaining target live_orders", "SELECT count(*)::int FROM live_orders WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_live_order_ids)"),
        ("remaining target shadow decisions", """
SELECT count(*)::int
FROM paper_live_shadow_decisions
WHERE strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
   OR paper_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
   OR live_order_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_live_order_ids)
   OR signal_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_signal_ids);
"""),
        ("remaining target shadow discrepancies", """
SELECT count(*)::int
FROM paper_live_shadow_discrepancies
WHERE strategy_id IN (SELECT id FROM cleanup_btc_binance_bps_instant_strategy_ids)
   OR correlation_id IN (SELECT correlation_id FROM cleanup_btc_binance_bps_instant_shadow_correlation_ids);
"""),
        ("remaining safe-wallet paper_positions", "SELECT count(*)::int FROM paper_positions WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_safe_wallets)"),
        ("remaining safe-wallet settlements", "SELECT count(*)::int FROM paper_position_settlements WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_safe_wallets)"),
        ("remaining safe-wallet performance rows", "SELECT count(*)::int FROM paper_copied_trader_performance WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_safe_wallets)"),
        ("target settlement ids in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_settlement_ids"),
        ("target position ids in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_position_ids"),
        ("target performance keys in temp", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_performance_keys")
    };

    foreach (var summary in summaries)
    {
        await WriteScalarAsync(db, $"  {summary.Label}", summary.Sql);
    }

    if (await TempTableExistsAsync(db, "cleanup_btc_binance_bps_instant_signal_delete_ids"))
    {
        await WriteScalarAsync(db, "  deletable signals", "SELECT count(*)::int FROM cleanup_btc_binance_bps_instant_signal_delete_ids");
    }

    await WriteLineAsync("Wallet candidate safety sample:");
    await using var command = new NpgsqlCommand("""
SELECT candidate.copied_trader_wallet,
       COALESCE(target_orders.count, 0) AS target_order_count,
       COALESCE(other_orders.count, 0) AS other_order_count,
       CASE WHEN safe.copied_trader_wallet IS NULL THEN false ELSE true END AS safe_to_delete
FROM cleanup_btc_binance_bps_instant_wallet_candidates candidate
LEFT JOIN LATERAL (
    SELECT count(*)::int
    FROM paper_orders orders
    WHERE orders.copied_trader_wallet = candidate.copied_trader_wallet
      AND orders.id IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
) target_orders(count) ON true
LEFT JOIN LATERAL (
    SELECT count(*)::int
    FROM paper_orders orders
    WHERE orders.copied_trader_wallet = candidate.copied_trader_wallet
      AND orders.id NOT IN (SELECT id FROM cleanup_btc_binance_bps_instant_paper_order_ids)
) other_orders(count) ON true
LEFT JOIN cleanup_btc_binance_bps_instant_safe_wallets safe
    ON safe.copied_trader_wallet = candidate.copied_trader_wallet
ORDER BY other_order_count DESC, target_order_count DESC, candidate.copied_trader_wallet
LIMIT 20;
""", db);
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        await WriteLineAsync($"  wallet={reader.GetString(0)}, target_orders={reader.GetInt32(1)}, other_orders={reader.GetInt32(2)}, safe={reader.GetBoolean(3)}");
    }
}

async Task AbortIfOpenLiveOrdersAsync(NpgsqlConnection db)
{
    const string sql = """
SELECT count(*)::int
FROM live_orders
WHERE id IN (SELECT id FROM cleanup_btc_binance_bps_instant_live_order_ids)
  AND (
      lower(status) IN ('created', 'queued', 'validated', 'submitted', 'open', 'live', 'unmatched', 'partiallymatched', 'pending', 'cancelrequested')
      OR lower(cancel_status) IN ('requested', 'pending')
      OR remaining_size > 0
  )
  AND settled_at_utc IS NULL;
""";
    var openLiveOrders = await ExecuteScalarIntAsync(db, sql);
    await WriteLineAsync($"Open/unsettled target live_orders guard count: {openLiveOrders}");
    if (openLiveOrders > 0)
    {
        throw new InvalidOperationException("Refusing cleanup because open/unsettled BTC Binance bps Instant live orders exist. Cancel/settle them before deleting history.");
    }
}

async Task WriteResidualPatternScanAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Residual BTC Binance bps Instant pattern scan:");
    var summaries = new (string Label, string Sql)[]
    {
        ("strategies by exact code regex", $"SELECT count(*)::int FROM strategies WHERE code ~ '{TargetPattern}';"),
        ("paper_orders synthetic wallets", "SELECT count(*)::int FROM paper_orders WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets);"),
        ("paper_positions synthetic wallets", "SELECT count(*)::int FROM paper_positions WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets);"),
        ("paper_position_settlements synthetic wallets", "SELECT count(*)::int FROM paper_position_settlements WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets);"),
        ("paper_copied_trader_performance synthetic wallets", "SELECT count(*)::int FROM paper_copied_trader_performance WHERE copied_trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets);")
    };

    foreach (var summary in summaries)
    {
        await WriteScalarAsync(db, $"  {summary.Label}", summary.Sql);
    }

    await WriteScalarBestEffortAsync(
        db,
        "  signals synthetic wallets (best-effort; no trader_wallet index)",
        "SELECT count(*)::int FROM signals WHERE trader_wallet IN (SELECT copied_trader_wallet FROM cleanup_btc_binance_bps_instant_wallets);",
        5_000);
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
            command.CommandTimeout = Math.Max(5, statementTimeoutMs / 1000 + 5);
            command.Parameters.AddWithValue("batch_size", batchSize);
            var deleted = (int)(await command.ExecuteScalarAsync() ?? 0);
            await transaction.CommitAsync();
            consecutiveTimeouts = 0;

            if (deleted == 0)
            {
                await WriteLineAsync($"{label}: complete, deleted {total}");
                break;
            }

            batchNumber++;
            total += deleted;
            await WriteLineAsync($"{label}: batch {batchNumber} deleted {deleted}, total {total}");

            if (pauseMs > 0)
            {
                await Task.Delay(pauseMs);
            }
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            catch
            {
                // The server may already have aborted the transaction after a timeout.
            }

            consecutiveTimeouts++;
            await WriteLineAsync($"{label}: lock/statement timeout {consecutiveTimeouts}/{MaxConsecutiveTimeouts}; retrying after pause. SqlState={exception.SqlState}");
            if (consecutiveTimeouts >= MaxConsecutiveTimeouts)
            {
                throw;
            }

            await Task.Delay(Math.Max(pauseMs, 500));
        }
    }
}

async Task<bool> TempTableExistsAsync(NpgsqlConnection db, string tableName)
{
    await using var command = new NpgsqlCommand("SELECT to_regclass('pg_temp.' || @table_name) IS NOT NULL;", db);
    command.CommandTimeout = 180;
    command.Parameters.AddWithValue("table_name", tableName);
    return (bool)(await command.ExecuteScalarAsync() ?? false);
}

async Task WriteScalarAsync(NpgsqlConnection db, string label, string sql)
{
    var value = await ExecuteScalarAsync(db, sql);
    await WriteLineAsync($"{label}: {value}");
}

async Task WriteScalarBestEffortAsync(NpgsqlConnection db, string label, string sql, int timeoutMilliseconds)
{
    await using var transaction = await db.BeginTransactionAsync();
    try
    {
        await using (var timeoutCommand = new NpgsqlCommand($"SET LOCAL statement_timeout = '{timeoutMilliseconds}ms';", db, transaction))
        {
            await timeoutCommand.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(sql, db, transaction);
        command.CommandTimeout = Math.Max(5, timeoutMilliseconds / 1000 + 2);
        var value = await command.ExecuteScalarAsync();
        await transaction.CommitAsync();
        await WriteLineAsync($"{label}: {value}");
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.QueryCanceled)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // The server may already have aborted the transaction after statement_timeout.
        }

        await WriteLineAsync($"{label}: <timed out after {timeoutMilliseconds}ms>");
    }
}

async Task<object?> ExecuteScalarAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    return await command.ExecuteScalarAsync();
}

async Task<int> ExecuteScalarIntAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    var value = await command.ExecuteScalarAsync();
    if (value is int intValue)
    {
        return intValue;
    }

    if (value is long longValue)
    {
        return checked((int)longValue);
    }

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
    var elapsed = stopwatch.Elapsed.TotalSeconds;
    var stamped = $"[{elapsed,8:0.0}s] {line}";
    Console.WriteLine(stamped);
    await report.WriteLineAsync(stamped);
    await report.FlushAsync();
}

static int GetIntArg(string[] args, string name, int defaultValue)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i + 1], out var parsed)
            && parsed > 0)
        {
            return parsed;
        }
    }

    return defaultValue;
}
