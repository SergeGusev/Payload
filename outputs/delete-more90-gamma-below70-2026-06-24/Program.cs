using System.Diagnostics;
using Npgsql;

const string TargetIdValue = "b7c50005-0000-4000-8022-000000090070";
const string TargetCode = "btc_up_down_5m_more_90_gamma_below_70";
const string TargetName = "BTC Up or Down 5m More 90 Gamma Below 70";
const string TargetWallet = "strategy:btc_up_down_5m_more_90_gamma_below_70";
const int DefaultBatchSize = 1000;
const int DefaultPauseMs = 100;
const int DefaultLockTimeoutMs = 500;
const int DefaultStatementTimeoutMs = 20_000;
const int MaxConsecutiveTimeouts = 20;

var targetId = Guid.Parse(TargetIdValue);
var startedAt = DateTimeOffset.UtcNow;
var stopwatch = Stopwatch.StartNew();
var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var reportPath = Path.Combine(outputDirectory, "result.txt");
var errorPath = Path.Combine(outputDirectory, "error.txt");
Directory.CreateDirectory(outputDirectory);
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
var verifyOnly = args.Contains("--verify-only", StringComparer.OrdinalIgnoreCase);
var dryRun = !execute && !verifyOnly;

await WriteLineAsync($"Delete target strategy cleanup started at {startedAt:O}");
await WriteLineAsync($"Target id: {TargetIdValue}");
await WriteLineAsync($"Target code: {TargetCode}");
await WriteLineAsync($"Target name: {TargetName}");
await WriteLineAsync($"Target wallet: {TargetWallet}");
await WriteLineAsync($"Dry run: {dryRun}");
await WriteLineAsync($"Execute: {execute}");
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
    ApplicationName = "DeleteMore90GammaBelow70"
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

await CreateTargetTablesAsync(connection);
await ValidateTargetStrategyMatchAsync(connection);
await WriteForeignKeysAsync(connection);
await WriteTargetStrategyMatchesAsync(connection);
await WriteTargetSummaryAsync(connection, "before");

if (verifyOnly)
{
    await WriteLineAsync($"Verification finished at {DateTimeOffset.UtcNow:O}");
    return;
}

await AbortIfOpenLiveOrdersAsync(connection);

if (dryRun)
{
    await WriteLineAsync("Dry run requested; no rows were deleted.");
    return;
}

var disabledStrategies = await ExecuteParameterizedScalarIntAsync(connection, """
WITH updated AS (
    UPDATE strategies
    SET enabled = false,
        live_stakes = false,
        auto_live_paused = false,
        auto_live_paused_at_utc = NULL,
        auto_live_pause_window_start_utc = NULL,
        live_enabled_at_utc = NULL,
        updated_at_utc = now()
    WHERE id = @target_id
      AND code = @target_code
      AND name = @target_name
      AND (
          enabled OR live_stakes OR auto_live_paused
          OR auto_live_paused_at_utc IS NOT NULL
          OR auto_live_pause_window_start_utc IS NOT NULL
          OR live_enabled_at_utc IS NOT NULL
      )
    RETURNING 1
)
SELECT count(*)::int FROM updated;
""");
await WriteLineAsync($"Disabled/de-live-updated target strategy rows: {disabledStrategies}");

if (disabledStrategies > 0)
{
    await WriteLineAsync("Waiting 10 seconds for running service strategy-state caches to expire before rechecking live-order guard...");
    await Task.Delay(TimeSpan.FromSeconds(10));
    await AbortIfOpenLiveOrdersAsync(connection);
}

await DeleteInBatchesAsync(connection, "paper_live_shadow_discrepancies", """
WITH victim AS (
    SELECT d.id
    FROM paper_live_shadow_discrepancies d
    WHERE d.strategy_id = @target_id
       OR d.correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids)
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
    WHERE d.strategy_id = @target_id
       OR d.correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids)
       OR d.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
       OR d.live_order_id IN (SELECT id FROM cleanup_target_live_order_ids)
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

await DeleteInBatchesAsync(connection, "polymarket_onchain_paper_signal_results", """
WITH victim AS (
    SELECT r.id
    FROM polymarket_onchain_paper_signal_results r
    WHERE r.id IN (SELECT id FROM cleanup_target_onchain_result_ids)
       OR r.copied_trader_wallet = @target_wallet
       OR r.signal_id IN (SELECT id FROM cleanup_target_signal_ids)
       OR r.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM polymarket_onchain_paper_signal_results r
    USING victim
    WHERE r.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_copied_leader_activity_events", """
WITH victim AS (
    SELECT e.id
    FROM paper_copied_leader_activity_events e
    WHERE e.copied_trader_wallet = @target_wallet
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM paper_copied_leader_activity_events e
    USING victim
    WHERE e.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_copied_leader_positions", """
WITH victim AS (
    SELECT p.id
    FROM paper_copied_leader_positions p
    WHERE p.id IN (SELECT id FROM cleanup_target_copied_leader_position_ids)
       OR p.copied_trader_wallet = @target_wallet
       OR p.entry_paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM paper_copied_leader_positions p
    USING victim
    WHERE p.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "strategy_market_paper_runs", """
WITH victim AS (
    DELETE FROM cleanup_target_run_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_run_ids
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
    WHERE d.strategy_id = @target_id
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
    DELETE FROM cleanup_target_live_order_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_live_order_ids
        LIMIT @batch_size
    )
    RETURNING target.id
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
    DELETE FROM cleanup_target_paper_fill_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_paper_fill_ids
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
    DELETE FROM cleanup_target_settlement_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_settlement_ids
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
    DELETE FROM cleanup_target_position_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_position_ids
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
    DELETE FROM cleanup_target_performance_keys target
    WHERE (target.copied_trader_wallet, target.category) IN (
        SELECT copied_trader_wallet, category
        FROM cleanup_target_performance_keys
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

await RefreshPaperOrderIdsFromCurrentTargetRowsAsync(connection);
await RefreshSignalIdsFromCurrentTargetRowsAsync(connection);

await DeleteInBatchesAsync(connection, "strategy_market_paper_runs late strategy", """
WITH victim AS (
    SELECT r.id
    FROM strategy_market_paper_runs r
    WHERE r.strategy_id = @target_id
    FOR UPDATE SKIP LOCKED
    LIMIT @batch_size
),
deleted AS (
    DELETE FROM strategy_market_paper_runs r
    USING victim
    WHERE r.id = victim.id
    RETURNING 1
)
SELECT count(*)::int FROM deleted;
""");

await DeleteInBatchesAsync(connection, "paper_fills late", """
WITH victim AS (
    SELECT f.id
    FROM paper_fills f
    WHERE f.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
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

await DeleteInBatchesAsync(connection, "paper_orders", """
WITH victim AS (
    DELETE FROM cleanup_target_paper_order_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_paper_order_ids
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

await RefreshPaperOrderIdsFromCurrentTargetRowsAsync(connection);
await RefreshSignalIdsFromCurrentTargetRowsAsync(connection);

await DeleteInBatchesAsync(connection, "paper_orders late", """
WITH victim AS (
    DELETE FROM cleanup_target_paper_order_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_paper_order_ids
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
    WHERE r.signal_id IN (SELECT id FROM cleanup_target_signal_delete_ids)
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
    DELETE FROM cleanup_target_signal_delete_ids target
    WHERE target.id IN (
        SELECT id
        FROM cleanup_target_signal_delete_ids
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
    WHERE s.id = @target_id
      AND s.code = @target_code
      AND s.name = @target_name
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
await WriteLineAsync($"Delete target strategy cleanup finished at {DateTimeOffset.UtcNow:O}");

async Task CreateTargetTablesAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Preparing target temp tables...");
    await ExecuteParameterizedNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_target_strategy_matches;
CREATE TEMP TABLE cleanup_target_strategy_matches AS
SELECT id, code, name, enabled, live_stakes
FROM strategies
WHERE id = @target_id
   OR code = @target_code
   OR name = @target_name;

DROP TABLE IF EXISTS cleanup_target_strategy_ids;
CREATE TEMP TABLE cleanup_target_strategy_ids (
    id uuid PRIMARY KEY,
    code text NOT NULL,
    name text NOT NULL,
    copied_trader_wallet text NOT NULL
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_strategy_ids (id, code, name, copied_trader_wallet)
VALUES (@target_id, @target_code, @target_name, @target_wallet);

DROP TABLE IF EXISTS cleanup_target_paper_order_ids;
CREATE TEMP TABLE cleanup_target_paper_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
WHERE paper_order.strategy_id = @target_id
   OR paper_order.copied_trader_wallet = @target_wallet
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_run_ids;
CREATE TEMP TABLE cleanup_target_run_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_run_ids (id)
SELECT run.id
FROM strategy_market_paper_runs run
WHERE run.strategy_id = @target_id
   OR run.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_paper_fill_ids;
CREATE TEMP TABLE cleanup_target_paper_fill_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_paper_fill_ids (id)
SELECT fill.id
FROM paper_fills fill
WHERE fill.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_live_order_ids;
CREATE TEMP TABLE cleanup_target_live_order_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_live_order_ids (id)
SELECT live_order.id
FROM live_orders live_order
WHERE live_order.strategy_id = @target_id
   OR live_order.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_correlation_ids;
CREATE TEMP TABLE cleanup_target_correlation_ids (
    correlation_id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
WHERE decision.strategy_id = @target_id
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_correlation_ids (correlation_id)
SELECT decision.correlation_id
FROM paper_live_shadow_decisions decision
WHERE decision.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
   OR decision.live_order_id IN (SELECT id FROM cleanup_target_live_order_ids)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_correlation_ids (correlation_id)
SELECT paper_order.correlation_id
FROM paper_orders paper_order
WHERE paper_order.id IN (SELECT id FROM cleanup_target_paper_order_ids)
  AND paper_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_correlation_ids (correlation_id)
SELECT live_order.correlation_id
FROM live_orders live_order
WHERE live_order.id IN (SELECT id FROM cleanup_target_live_order_ids)
  AND live_order.correlation_id IS NOT NULL
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_signal_ids;
CREATE TEMP TABLE cleanup_target_signal_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
WHERE paper_order.id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT run.signal_id
FROM strategy_market_paper_runs run
WHERE run.id IN (SELECT id FROM cleanup_target_run_ids)
  AND run.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT decision.signal_id
FROM paper_live_shadow_decisions decision
WHERE (decision.strategy_id = @target_id
       OR decision.correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids))
  AND decision.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT live_order.signal_id
FROM live_orders live_order
WHERE live_order.id IN (SELECT id FROM cleanup_target_live_order_ids)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT dry_order.signal_id
FROM dry_run_orders dry_order
WHERE dry_order.strategy_id = @target_id
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_settlement_ids;
CREATE TEMP TABLE cleanup_target_settlement_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_settlement_ids (id)
SELECT settlement.id
FROM paper_position_settlements settlement
WHERE settlement.copied_trader_wallet = @target_wallet
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_position_ids;
CREATE TEMP TABLE cleanup_target_position_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_position_ids (id)
SELECT position.id
FROM paper_positions position
WHERE position.copied_trader_wallet = @target_wallet
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_performance_keys;
CREATE TEMP TABLE cleanup_target_performance_keys (
    copied_trader_wallet text NOT NULL,
    category text NOT NULL,
    PRIMARY KEY (copied_trader_wallet, category)
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_performance_keys (copied_trader_wallet, category)
SELECT performance.copied_trader_wallet, performance.category
FROM paper_copied_trader_performance performance
WHERE performance.copied_trader_wallet = @target_wallet
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_copied_leader_position_ids;
CREATE TEMP TABLE cleanup_target_copied_leader_position_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_copied_leader_position_ids (id)
SELECT copied_position.id
FROM paper_copied_leader_positions copied_position
WHERE copied_position.copied_trader_wallet = @target_wallet
   OR copied_position.entry_paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

DROP TABLE IF EXISTS cleanup_target_onchain_result_ids;
CREATE TEMP TABLE cleanup_target_onchain_result_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_onchain_result_ids (id)
SELECT result.id
FROM polymarket_onchain_paper_signal_results result
WHERE result.copied_trader_wallet = @target_wallet
   OR result.signal_id IN (SELECT id FROM cleanup_target_signal_ids)
   OR result.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

ANALYZE cleanup_target_strategy_matches;
ANALYZE cleanup_target_strategy_ids;
ANALYZE cleanup_target_paper_order_ids;
ANALYZE cleanup_target_run_ids;
ANALYZE cleanup_target_paper_fill_ids;
ANALYZE cleanup_target_live_order_ids;
ANALYZE cleanup_target_correlation_ids;
ANALYZE cleanup_target_signal_ids;
ANALYZE cleanup_target_settlement_ids;
ANALYZE cleanup_target_position_ids;
ANALYZE cleanup_target_performance_keys;
ANALYZE cleanup_target_copied_leader_position_ids;
ANALYZE cleanup_target_onchain_result_ids;
""");
}

async Task ValidateTargetStrategyMatchAsync(NpgsqlConnection db)
{
    var matchCount = await ExecuteScalarIntAsync(db, "SELECT count(*)::int FROM cleanup_target_strategy_matches;");
    var exactMatchCount = await ExecuteParameterizedScalarIntAsync(db, """
SELECT count(*)::int
FROM cleanup_target_strategy_matches
WHERE id = @target_id
  AND code = @target_code
  AND name = @target_name;
""");

    await WriteLineAsync($"Strategy rows matching target id/code/name: {matchCount}");
    await WriteLineAsync($"Exact target strategy rows: {exactMatchCount}");

    if (matchCount > 1)
    {
        throw new InvalidOperationException("Refusing cleanup because multiple strategy rows match the target id/code/name selectors.");
    }

    if (matchCount == 1 && exactMatchCount != 1)
    {
        throw new InvalidOperationException("Refusing cleanup because a strategy row matched by id/code/name does not match all target fields exactly.");
    }
}

async Task RefreshPaperOrderIdsFromCurrentTargetRowsAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Refreshing paper order ids from current target rows...");
    await ExecuteParameterizedNonQueryAsync(db, """
INSERT INTO cleanup_target_paper_order_ids (id)
SELECT paper_order.id
FROM paper_orders paper_order
WHERE paper_order.strategy_id = @target_id
   OR paper_order.copied_trader_wallet = @target_wallet
ON CONFLICT DO NOTHING;

ANALYZE cleanup_target_paper_order_ids;
""");
}

async Task RefreshSignalIdsFromCurrentTargetRowsAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Refreshing signal ids from current target rows before deleting paper orders...");
    await ExecuteParameterizedNonQueryAsync(db, """
INSERT INTO cleanup_target_signal_ids (id)
SELECT paper_order.signal_id
FROM paper_orders paper_order
WHERE paper_order.id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT run.signal_id
FROM strategy_market_paper_runs run
WHERE (run.strategy_id = @target_id
       OR run.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids))
  AND run.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT decision.signal_id
FROM paper_live_shadow_decisions decision
WHERE (decision.strategy_id = @target_id
       OR decision.correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids))
  AND decision.signal_id IS NOT NULL
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT live_order.signal_id
FROM live_orders live_order
WHERE live_order.strategy_id = @target_id
   OR live_order.paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
ON CONFLICT DO NOTHING;

INSERT INTO cleanup_target_signal_ids (id)
SELECT dry_order.signal_id
FROM dry_run_orders dry_order
WHERE dry_order.strategy_id = @target_id
ON CONFLICT DO NOTHING;

ANALYZE cleanup_target_signal_ids;
""");
}

async Task RebuildDeletableSignalTableAsync(NpgsqlConnection db)
{
    await ExecuteNonQueryAsync(db, """
DROP TABLE IF EXISTS cleanup_target_signal_delete_ids;
CREATE TEMP TABLE cleanup_target_signal_delete_ids (
    id uuid PRIMARY KEY
) ON COMMIT PRESERVE ROWS;

INSERT INTO cleanup_target_signal_delete_ids (id)
SELECT target.id
FROM cleanup_target_signal_ids target
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
)
AND NOT EXISTS (
    SELECT 1
    FROM polymarket_onchain_paper_signal_results result
    WHERE result.signal_id = target.id
);

ANALYZE cleanup_target_signal_delete_ids;
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

async Task WriteTargetStrategyMatchesAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Target strategy row matches:");
    await using var command = new NpgsqlCommand("""
SELECT id::text, code, name, enabled, live_stakes
FROM cleanup_target_strategy_matches
ORDER BY code;
""", db);
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    var count = 0;
    while (await reader.ReadAsync())
    {
        count++;
        await WriteLineAsync($"  id={reader.GetString(0)} code={reader.GetString(1)} enabled={reader.GetBoolean(3)} live_stakes={reader.GetBoolean(4)} name={reader.GetString(2)}");
    }

    if (count == 0)
    {
        await WriteLineAsync("  <none>");
    }
}

async Task WriteTargetSummaryAsync(NpgsqlConnection db, string stage)
{
    await WriteLineAsync($"Target summary ({stage}):");
    var summaries = new (string Label, string Sql, bool Parameterized)[]
    {
        ("strategy matches", "SELECT count(*)::int FROM cleanup_target_strategy_matches;", false),
        ("exact strategy row still in strategies", "SELECT count(*)::int FROM strategies WHERE id = @target_id AND code = @target_code AND name = @target_name;", true),
        ("paper_orders temp ids", "SELECT count(*)::int FROM cleanup_target_paper_order_ids;", false),
        ("remaining paper_orders by temp", "SELECT count(*)::int FROM paper_orders WHERE id IN (SELECT id FROM cleanup_target_paper_order_ids);", false),
        ("remaining paper_orders by strategy/wallet", "SELECT count(*)::int FROM paper_orders WHERE strategy_id = @target_id OR copied_trader_wallet = @target_wallet;", true),
        ("paper_fills temp ids", "SELECT count(*)::int FROM cleanup_target_paper_fill_ids;", false),
        ("remaining paper_fills by target orders", "SELECT count(*)::int FROM paper_fills WHERE paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids);", false),
        ("strategy runs temp ids", "SELECT count(*)::int FROM cleanup_target_run_ids;", false),
        ("remaining strategy runs by strategy", "SELECT count(*)::int FROM strategy_market_paper_runs WHERE strategy_id = @target_id;", true),
        ("dry_run_orders", "SELECT count(*)::int FROM dry_run_orders WHERE strategy_id = @target_id;", true),
        ("live_orders temp ids", "SELECT count(*)::int FROM cleanup_target_live_order_ids;", false),
        ("remaining live_orders", "SELECT count(*)::int FROM live_orders WHERE strategy_id = @target_id OR paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids);", true),
        ("open/unsettled live_orders", BuildOpenLiveOrderCountSql(), true),
        ("shadow correlation ids", "SELECT count(*)::int FROM cleanup_target_correlation_ids;", false),
        ("paper_live_shadow_decisions", "SELECT count(*)::int FROM paper_live_shadow_decisions WHERE strategy_id = @target_id OR correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids) OR paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids) OR live_order_id IN (SELECT id FROM cleanup_target_live_order_ids);", true),
        ("paper_live_shadow_discrepancies", "SELECT count(*)::int FROM paper_live_shadow_discrepancies WHERE strategy_id = @target_id OR correlation_id IN (SELECT correlation_id FROM cleanup_target_correlation_ids);", true),
        ("paper_positions", "SELECT count(*)::int FROM paper_positions WHERE copied_trader_wallet = @target_wallet;", true),
        ("paper_position_settlements", "SELECT count(*)::int FROM paper_position_settlements WHERE copied_trader_wallet = @target_wallet;", true),
        ("paper_copied_trader_performance", "SELECT count(*)::int FROM paper_copied_trader_performance WHERE copied_trader_wallet = @target_wallet;", true),
        ("paper_copied_leader_positions", "SELECT count(*)::int FROM paper_copied_leader_positions WHERE copied_trader_wallet = @target_wallet OR entry_paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids);", true),
        ("paper_copied_leader_activity_events", "SELECT count(*)::int FROM paper_copied_leader_activity_events WHERE copied_trader_wallet = @target_wallet;", true),
        ("polymarket_onchain_paper_signal_results", "SELECT count(*)::int FROM polymarket_onchain_paper_signal_results WHERE copied_trader_wallet = @target_wallet OR signal_id IN (SELECT id FROM cleanup_target_signal_ids) OR paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids);", true),
        ("signal ids temp", "SELECT count(*)::int FROM cleanup_target_signal_ids;", false),
        ("remaining signals by temp", "SELECT count(*)::int FROM signals WHERE id IN (SELECT id FROM cleanup_target_signal_ids);", false),
        ("remaining signal_rejections by temp", "SELECT count(*)::int FROM signal_rejections WHERE signal_id IN (SELECT id FROM cleanup_target_signal_ids);", false)
    };

    foreach (var summary in summaries)
    {
        var value = summary.Parameterized
            ? await ExecuteParameterizedScalarAsync(db, summary.Sql)
            : await ExecuteScalarAsync(db, summary.Sql);
        await WriteLineAsync($"  {summary.Label}: {value}");
    }

    if (await TempTableExistsAsync(db, "cleanup_target_signal_delete_ids"))
    {
        await WriteScalarAsync(db, "  deletable signal ids", "SELECT count(*)::int FROM cleanup_target_signal_delete_ids;");
    }
}

async Task AbortIfOpenLiveOrdersAsync(NpgsqlConnection db)
{
    var openLiveOrders = await ExecuteParameterizedScalarIntAsync(db, BuildOpenLiveOrderCountSql());
    await WriteLineAsync($"Open/unsettled target live_orders guard count: {openLiveOrders}");
    if (openLiveOrders == 0)
    {
        return;
    }

    await WriteOpenLiveOrdersAsync(db);
    throw new InvalidOperationException("Refusing cleanup because open/unsettled target live orders exist. Cancel/settle them before deleting history.");
}

async Task WriteOpenLiveOrdersAsync(NpgsqlConnection db)
{
    await WriteLineAsync("Open target live orders:");
    await using var command = CreateParameterizedCommand(db, """
SELECT id::text,
       status,
       cancel_status,
       remaining_size,
       filled_size,
       price,
       COALESCE(left(order_id, 10), '<none>') AS order_id_prefix,
       created_at_utc AT TIME ZONE 'UTC',
       updated_at_utc AT TIME ZONE 'UTC'
FROM live_orders
WHERE (
      strategy_id = @target_id
      OR id IN (SELECT id FROM cleanup_target_live_order_ids)
      OR paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
  )
  AND (
      lower(status) IN ('created', 'queued', 'validated', 'submitted', 'open', 'live', 'unmatched', 'partiallymatched', 'pending', 'cancelrequested')
      OR lower(cancel_status) IN ('requested', 'pending')
      OR (
          remaining_size > 0
          AND lower(status) NOT IN ('matched', 'rejected', 'preflightrejected', 'cancelled', 'cancelfailed')
      )
  )
  AND settled_at_utc IS NULL
ORDER BY updated_at_utc DESC
LIMIT 20;
""");
    command.CommandTimeout = 180;
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        await WriteLineAsync(
            $"  id={reader.GetString(0)} status={reader.GetString(1)} cancel_status={reader.GetString(2)} remaining={reader.GetDecimal(3):0.########} filled={reader.GetDecimal(4):0.########} price={reader.GetDecimal(5):0.########} order_id_prefix={reader.GetString(6)} created_utc={reader.GetValue(7)} updated_utc={reader.GetValue(8)}");
    }
}

string BuildOpenLiveOrderCountSql()
{
    return """
SELECT count(*)::int
FROM live_orders
WHERE (
      strategy_id = @target_id
      OR id IN (SELECT id FROM cleanup_target_live_order_ids)
      OR paper_order_id IN (SELECT id FROM cleanup_target_paper_order_ids)
  )
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

            await using var command = CreateParameterizedCommand(db, sql, transaction);
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

async Task<object?> ExecuteScalarAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 180;
    return await command.ExecuteScalarAsync();
}

async Task<object?> ExecuteParameterizedScalarAsync(NpgsqlConnection db, string sql)
{
    await using var command = CreateParameterizedCommand(db, sql);
    command.CommandTimeout = 180;
    return await command.ExecuteScalarAsync();
}

async Task<int> ExecuteScalarIntAsync(NpgsqlConnection db, string sql)
{
    var value = await ExecuteScalarAsync(db, sql);
    return Convert.ToInt32(value);
}

async Task<int> ExecuteParameterizedScalarIntAsync(NpgsqlConnection db, string sql)
{
    var value = await ExecuteParameterizedScalarAsync(db, sql);
    return Convert.ToInt32(value);
}

async Task ExecuteNonQueryAsync(NpgsqlConnection db, string sql)
{
    await using var command = new NpgsqlCommand(sql, db);
    command.CommandTimeout = 600;
    await command.ExecuteNonQueryAsync();
}

async Task ExecuteParameterizedNonQueryAsync(NpgsqlConnection db, string sql)
{
    await using var command = CreateParameterizedCommand(db, sql);
    command.CommandTimeout = 600;
    await command.ExecuteNonQueryAsync();
}

NpgsqlCommand CreateParameterizedCommand(NpgsqlConnection db, string sql, NpgsqlTransaction? transaction = null)
{
    var command = new NpgsqlCommand(sql, db, transaction);
    command.Parameters.AddWithValue("target_id", targetId);
    command.Parameters.AddWithValue("target_code", TargetCode);
    command.Parameters.AddWithValue("target_name", TargetName);
    command.Parameters.AddWithValue("target_wallet", TargetWallet);
    return command;
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
