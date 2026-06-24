using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 2;
}

var builder = new NpgsqlConnectionStringBuilder(connectionString);
var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
if (!string.IsNullOrWhiteSpace(hostOverride))
{
    builder.Host = hostOverride;
}

var batchSize = ReadPositiveInt("PAPER_HISTORY_RESET_BATCH_SIZE", 50_000);
var commandTimeoutSeconds = ReadPositiveInt("PAPER_HISTORY_RESET_COMMAND_TIMEOUT_SECONDS", 120);
var lockTimeout = Environment.GetEnvironmentVariable("PAPER_HISTORY_RESET_LOCK_TIMEOUT") ?? "2s";
var statementTimeout = Environment.GetEnvironmentVariable("PAPER_HISTORY_RESET_STATEMENT_TIMEOUT") ?? "120s";
var cutoffUtc = DateTimeOffset.UtcNow;

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();

Console.WriteLine($"target_database={builder.Database}");
Console.WriteLine($"target_host={builder.Host}");
Console.WriteLine($"target_port={builder.Port}");
Console.WriteLine($"cutoff_utc={cutoffUtc:O}");
Console.WriteLine($"batch_size={batchSize}");

await ExecuteNonQueryAsync(
    "SELECT set_config('lock_timeout', @LockTimeout, false); SELECT set_config('statement_timeout', @StatementTimeout, false);",
    ("LockTimeout", lockTimeout),
    ("StatementTimeout", statementTimeout));

Console.WriteLine("before_counts");
await PrintCountsAsync();

await ExecuteNonQueryAsync("DROP TABLE IF EXISTS tmp_paper_history_reset_orders;");
await ExecuteNonQueryAsync(
    """
    CREATE TEMP TABLE tmp_paper_history_reset_orders (
        id uuid PRIMARY KEY
    ) ON COMMIT PRESERVE ROWS;
    """);
var capturedPaperOrders = await ExecuteScalarLongAsync(
    """
    WITH inserted AS (
        INSERT INTO tmp_paper_history_reset_orders (id)
        SELECT id
        FROM paper_orders
        WHERE created_at_utc <= @CutoffUtc
        ON CONFLICT DO NOTHING
        RETURNING 1
    )
    SELECT count(*)::bigint
    FROM inserted;
    """,
    ("CutoffUtc", cutoffUtc.UtcDateTime));
Console.WriteLine($"captured_paper_orders={capturedPaperOrders}");

var unlinkedLiveOrders = await ExecuteScalarLongAsync(
    """
    WITH updated AS (
        UPDATE live_orders live_order
        SET paper_order_id = NULL
        WHERE live_order.paper_order_id IS NOT NULL
          AND EXISTS (
              SELECT 1
              FROM tmp_paper_history_reset_orders target
              WHERE target.id = live_order.paper_order_id
          )
        RETURNING 1
    )
    SELECT count(*)::bigint
    FROM updated;
    """);
Console.WriteLine($"live_orders_unlinked_from_paper_orders={unlinkedLiveOrders}");

var deletedShadowDiscrepancies = await DeleteByCtidBatchesAsync(
    "paper_live_shadow_discrepancies",
    "paper_live_shadow_discrepancy",
    "paper_live_shadow_discrepancy.created_at_utc <= @CutoffUtc");
Console.WriteLine($"deleted_paper_live_shadow_discrepancies={deletedShadowDiscrepancies}");

var deletedShadowDecisions = await DeleteByCtidBatchesAsync(
    "paper_live_shadow_decisions",
    "paper_live_shadow_decision",
    """
    paper_live_shadow_decision.decision_created_at_utc <= @CutoffUtc
    OR EXISTS (
        SELECT 1
        FROM tmp_paper_history_reset_orders target
        WHERE target.id = paper_live_shadow_decision.paper_order_id
    )
    """);
Console.WriteLine($"deleted_paper_live_shadow_decisions={deletedShadowDecisions}");

var deletedPaperFills = await DeleteByCtidBatchesAsync(
    "paper_fills",
    "paper_fill",
    """
    paper_fill.filled_at_utc <= @CutoffUtc
    OR EXISTS (
        SELECT 1
        FROM tmp_paper_history_reset_orders target
        WHERE target.id = paper_fill.paper_order_id
    )
    """);
Console.WriteLine($"deleted_paper_fills={deletedPaperFills}");

var deletedStrategyRuns = await DeleteByCtidBatchesAsync(
    "strategy_market_paper_runs",
    "strategy_run",
    """
    strategy_run.created_at_utc <= @CutoffUtc
    OR EXISTS (
        SELECT 1
        FROM tmp_paper_history_reset_orders target
        WHERE target.id = strategy_run.paper_order_id
    )
    """);
Console.WriteLine($"deleted_strategy_market_paper_runs={deletedStrategyRuns}");

var deletedPositions = await DeleteByCtidBatchesAsync(
    "paper_positions",
    "paper_position",
    "paper_position.updated_at_utc <= @CutoffUtc");
Console.WriteLine($"deleted_paper_positions={deletedPositions}");

var deletedSettlements = await DeleteByCtidBatchesAsync(
    "paper_position_settlements",
    "paper_settlement",
    "paper_settlement.created_at_utc <= @CutoffUtc OR paper_settlement.settled_at_utc <= @CutoffUtc");
Console.WriteLine($"deleted_paper_position_settlements={deletedSettlements}");

var deletedCopiedTraderPerformance = await DeleteByCtidBatchesAsync(
    "paper_copied_trader_performance",
    "paper_performance",
    "true");
Console.WriteLine($"deleted_paper_copied_trader_performance={deletedCopiedTraderPerformance}");

var deletedCopiedLeaderPositions = await DeleteByCtidBatchesAsync(
    "paper_copied_leader_positions",
    "paper_copied_leader_position",
    "true");
Console.WriteLine($"deleted_paper_copied_leader_positions={deletedCopiedLeaderPositions}");

var deletedCopiedLeaderActivityEvents = await DeleteByCtidBatchesAsync(
    "paper_copied_leader_activity_events",
    "paper_copied_leader_activity_event",
    "true");
Console.WriteLine($"deleted_paper_copied_leader_activity_events={deletedCopiedLeaderActivityEvents}");

var deletedOnchainPaperSignalResults = await DeleteByCtidBatchesAsync(
    "polymarket_onchain_paper_signal_results",
    "onchain_paper_signal_result",
    "true");
Console.WriteLine($"deleted_polymarket_onchain_paper_signal_results={deletedOnchainPaperSignalResults}");

var deletedPaperOrders = await DeletePaperOrdersAsync();
Console.WriteLine($"deleted_paper_orders={deletedPaperOrders}");

var resetPaperLostCounters = await ExecuteScalarLongAsync(
    """
    WITH updated AS (
        UPDATE strategies
        SET paper_lost_counter = 0,
            updated_at_utc = now()
        WHERE paper_lost_counter <> 0
        RETURNING 1
    )
    SELECT count(*)::bigint
    FROM updated;
    """);
Console.WriteLine($"reset_strategy_paper_lost_counters={resetPaperLostCounters}");

await ExecuteNonQueryAsync(
    """
    ANALYZE paper_orders;
    ANALYZE paper_fills;
    ANALYZE strategy_market_paper_runs;
    ANALYZE paper_positions;
    ANALYZE paper_position_settlements;
    ANALYZE paper_copied_trader_performance;
    ANALYZE paper_live_shadow_decisions;
    ANALYZE live_orders;
    ANALYZE strategies;
    """);

Console.WriteLine("after_counts");
await PrintCountsAsync();

return 0;

async Task<long> DeletePaperOrdersAsync()
{
    long total = 0;
    var batchNumber = 0;

    while (true)
    {
        var deleted = await ExecuteScalarLongAsync(
            """
            WITH target AS (
                SELECT id
                FROM tmp_paper_history_reset_orders
                LIMIT @BatchSize
            ),
            deleted_orders AS (
                DELETE FROM paper_orders paper_order
                USING target
                WHERE paper_order.id = target.id
                RETURNING paper_order.id
            ),
            deleted_targets AS (
                DELETE FROM tmp_paper_history_reset_orders target_store
                USING target
                WHERE target_store.id = target.id
                RETURNING 1
            )
            SELECT count(*)::bigint
            FROM deleted_orders;
            """,
            ("BatchSize", batchSize));

        if (deleted == 0)
        {
            return total;
        }

        total += deleted;
        batchNumber++;
        Console.WriteLine($"paper_orders_batch={batchNumber};deleted={deleted};total={total}");
        await Task.Delay(TimeSpan.FromMilliseconds(150));
    }
}

async Task<long> DeleteByCtidBatchesAsync(string tableName, string alias, string whereSql)
{
    long total = 0;
    var batchNumber = 0;
    var retryCount = 0;
    var sql =
        $$"""
        WITH target AS (
            SELECT {{alias}}.ctid
            FROM {{tableName}} {{alias}}
            WHERE {{whereSql}}
            LIMIT @BatchSize
        ),
        deleted_rows AS (
            DELETE FROM {{tableName}} {{alias}}
            USING target
            WHERE {{alias}}.ctid = target.ctid
            RETURNING 1
        )
        SELECT count(*)::bigint
        FROM deleted_rows;
        """;

    while (true)
    {
        long deleted;
        try
        {
            deleted = await ExecuteScalarLongAsync(
                sql,
                ("BatchSize", batchSize),
                ("CutoffUtc", cutoffUtc.UtcDateTime));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            retryCount++;
            Console.WriteLine($"{tableName}_lock_retry={retryCount}");
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(5_000, 500 * retryCount)));
            continue;
        }

        if (deleted == 0)
        {
            return total;
        }

        retryCount = 0;
        total += deleted;
        batchNumber++;
        Console.WriteLine($"{tableName}_batch={batchNumber};deleted={deleted};total={total}");
        await Task.Delay(TimeSpan.FromMilliseconds(150));
    }
}

async Task PrintCountsAsync()
{
    await using var command = CreateCommand(
        """
        SELECT 'live_orders' AS table_name, count(*)::bigint AS rows FROM live_orders
        UNION ALL SELECT 'live_orders_with_paper_order_id', count(*)::bigint FROM live_orders WHERE paper_order_id IS NOT NULL
        UNION ALL SELECT 'paper_orders', count(*)::bigint FROM paper_orders
        UNION ALL SELECT 'paper_fills', count(*)::bigint FROM paper_fills
        UNION ALL SELECT 'strategy_market_paper_runs', count(*)::bigint FROM strategy_market_paper_runs
        UNION ALL SELECT 'paper_positions', count(*)::bigint FROM paper_positions
        UNION ALL SELECT 'paper_position_settlements', count(*)::bigint FROM paper_position_settlements
        UNION ALL SELECT 'paper_copied_trader_performance', count(*)::bigint FROM paper_copied_trader_performance
        UNION ALL SELECT 'paper_copied_leader_positions', count(*)::bigint FROM paper_copied_leader_positions
        UNION ALL SELECT 'paper_copied_leader_activity_events', count(*)::bigint FROM paper_copied_leader_activity_events
        UNION ALL SELECT 'polymarket_onchain_paper_signal_results', count(*)::bigint FROM polymarket_onchain_paper_signal_results
        UNION ALL SELECT 'paper_live_shadow_decisions', count(*)::bigint FROM paper_live_shadow_decisions
        UNION ALL SELECT 'paper_live_shadow_discrepancies', count(*)::bigint FROM paper_live_shadow_discrepancies
        UNION ALL SELECT 'strategies_with_paper_lost_counter', count(*)::bigint FROM strategies WHERE paper_lost_counter <> 0
        ORDER BY table_name;
        """);

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader.GetString(0)}={reader.GetInt64(1)}");
    }
}

async Task ExecuteNonQueryAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = CreateCommand(sql, parameters);
    await command.ExecuteNonQueryAsync();
}

async Task<long> ExecuteScalarLongAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = CreateCommand(sql, parameters);
    var value = await command.ExecuteScalarAsync();
    return Convert.ToInt64(value);
}

NpgsqlCommand CreateCommand(string sql, params (string Name, object Value)[] parameters)
{
    var command = connection.CreateCommand();
    command.CommandText = sql;
    command.CommandTimeout = commandTimeoutSeconds;

    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value);
    }

    return command;
}

static int ReadPositiveInt(string variableName, int defaultValue)
{
    var raw = Environment.GetEnvironmentVariable(variableName);
    if (string.IsNullOrWhiteSpace(raw))
    {
        return defaultValue;
    }

    return int.TryParse(raw, out var parsed) && parsed > 0
        ? parsed
        : defaultValue;
}
