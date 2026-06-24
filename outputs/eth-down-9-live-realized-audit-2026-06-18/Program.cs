using System.Data;
using System.Globalization;
using Npgsql;

const string StrategyCode = "eth_up_down_5m_down_bps_9_instant";
const string DefaultOutputDirectory = "outputs/eth-down-9-live-realized-audit-2026-06-18";

var outputDirectory = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.OrdinalIgnoreCase)) ?? DefaultOutputDirectory;
Directory.CreateDirectory(outputDirectory);

var summaryPath = Path.Combine(outputDirectory, "summary.tsv");
var sourceBreakdownPath = Path.Combine(outputDirectory, "settlement-source-breakdown.tsv");
var statusBreakdownPath = Path.Combine(outputDirectory, "status-breakdown.tsv");
var recentUpdatesPath = Path.Combine(outputDirectory, "recent-settlement-updates.tsv");
var settledOrdersPath = Path.Combine(outputDirectory, "settled-live-orders.tsv");

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
}

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    ApplicationName = "EthDown9LiveRealizedAudit",
    Timeout = 10,
    CommandTimeout = 60
};

var hostOverride = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_HOST_OVERRIDE");
builder.Host = string.IsNullOrWhiteSpace(hostOverride) ? "192.168.0.101" : hostOverride;

await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await ExecuteNonQueryAsync("""
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '45s';
SET LOCAL lock_timeout = '500ms';
""");

var capturedAtUtc = DateTimeOffset.UtcNow;
var strategy = await ReadStrategyAsync();
var metrics = await ReadMetricsAsync(strategy.Id);
var sourceBreakdown = await ReadBreakdownAsync(strategy.Id, "COALESCE(NULLIF(settlement_source, ''), '<null>')");
var statusBreakdown = await ReadBreakdownAsync(strategy.Id, "status");
var recentUpdates = await ReadRecentUpdatesAsync(strategy.Id);
var settledOrders = await ReadSettledOrdersAsync(strategy.Id);

await using (var writer = new StreamWriter(summaryPath, append: false))
{
    await writer.WriteLineAsync("key\tvalue");
    await WritePairAsync(writer, "captured_at_utc", capturedAtUtc.ToString("O", CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "database", (await ScalarAsync("SELECT current_database();"))?.ToString() ?? string.Empty);
    await WritePairAsync(writer, "server_address", (await ScalarAsync("SELECT inet_server_addr()::text;"))?.ToString() ?? string.Empty);
    await WritePairAsync(writer, "server_time_utc", (await ScalarAsync("SELECT to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"');"))?.ToString() ?? string.Empty);
    await WritePairAsync(writer, "strategy_id", strategy.Id.ToString());
    await WritePairAsync(writer, "strategy_code", strategy.Code);
    await WritePairAsync(writer, "strategy_name", strategy.Name);
    await WritePairAsync(writer, "enabled", strategy.Enabled.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_stakes", strategy.LiveStakes.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "auto_live_paused", strategy.AutoLivePaused.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_enabled_at_utc", FormatDate(strategy.LiveEnabledAtUtc));
    await WritePairAsync(writer, "live_available_balance", FormatDecimal(strategy.LiveAvailableBalance));
    await WritePairAsync(writer, "live_orders_count", metrics.LiveOrdersCount.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_filled_orders_count", metrics.LiveFilledOrdersCount.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_settled_orders_count", metrics.LiveSettledOrdersCount.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_won_orders_count", metrics.LiveWonOrdersCount.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_lost_orders_count", metrics.LiveLostOrdersCount.ToString(CultureInfo.InvariantCulture));
    await WritePairAsync(writer, "live_stake_usd", FormatDecimal(metrics.LiveStakeUsd));
    await WritePairAsync(writer, "live_settlement_value_usd", FormatDecimal(metrics.LiveSettlementValueUsd));
    await WritePairAsync(writer, "live_positive_pnl_usd", FormatDecimal(metrics.LivePositivePnlUsd));
    await WritePairAsync(writer, "live_loss_abs_pnl_usd", FormatDecimal(metrics.LiveLossAbsPnlUsd));
    await WritePairAsync(writer, "live_realized_pnl_usd", FormatDecimal(metrics.LiveRealizedPnlUsd));
    await WritePairAsync(writer, "live_avg_win_pnl_usd", FormatDecimal(metrics.LiveAvgWinPnlUsd));
    await WritePairAsync(writer, "live_avg_loss_pnl_usd", FormatDecimal(metrics.LiveAvgLossPnlUsd));
    await WritePairAsync(writer, "live_profit_factor", metrics.LiveLossAbsPnlUsd == 0m ? string.Empty : FormatDecimal(metrics.LivePositivePnlUsd / metrics.LiveLossAbsPnlUsd));
    await WritePairAsync(writer, "live_last_order_utc", FormatDate(metrics.LiveLastOrderUtc));
    await WritePairAsync(writer, "live_last_settlement_utc", FormatDate(metrics.LiveLastSettlementUtc));
    await WritePairAsync(writer, "max_live_order_updated_at_utc", FormatDate(metrics.MaxUpdatedAtUtc));
    await WritePairAsync(writer, "source_breakdown_tsv", Path.GetFullPath(sourceBreakdownPath));
    await WritePairAsync(writer, "status_breakdown_tsv", Path.GetFullPath(statusBreakdownPath));
    await WritePairAsync(writer, "recent_updates_tsv", Path.GetFullPath(recentUpdatesPath));
    await WritePairAsync(writer, "settled_orders_tsv", Path.GetFullPath(settledOrdersPath));
}

await WriteBreakdownAsync(sourceBreakdownPath, sourceBreakdown);
await WriteBreakdownAsync(statusBreakdownPath, statusBreakdown);

await using (var writer = new StreamWriter(recentUpdatesPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', RecentSettlementUpdate.Columns));
    foreach (var row in recentUpdates)
    {
        await writer.WriteLineAsync(row.ToTsv());
    }
}

await using (var writer = new StreamWriter(settledOrdersPath, append: false))
{
    await writer.WriteLineAsync(string.Join('\t', SettledLiveOrder.Columns));
    foreach (var row in settledOrders)
    {
        await writer.WriteLineAsync(row.ToTsv());
    }
}

await tx.CommitAsync();

Console.WriteLine($"summary={Path.GetFullPath(summaryPath)}");
Console.WriteLine($"source_breakdown={Path.GetFullPath(sourceBreakdownPath)}");
Console.WriteLine($"status_breakdown={Path.GetFullPath(statusBreakdownPath)}");
Console.WriteLine($"recent_updates={Path.GetFullPath(recentUpdatesPath)}");
Console.WriteLine($"settled_orders={Path.GetFullPath(settledOrdersPath)}");
Console.WriteLine($"strategy={strategy.Code}");
Console.WriteLine($"live_settled={metrics.LiveSettledOrdersCount}");
Console.WriteLine($"live_won={metrics.LiveWonOrdersCount}");
Console.WriteLine($"live_lost={metrics.LiveLostOrdersCount}");
Console.WriteLine($"live_positive_pnl={FormatDecimal(metrics.LivePositivePnlUsd)}");
Console.WriteLine($"live_loss_abs={FormatDecimal(metrics.LiveLossAbsPnlUsd)}");
Console.WriteLine($"live_realized={FormatDecimal(metrics.LiveRealizedPnlUsd)}");
Console.WriteLine($"live_available_balance={FormatDecimal(strategy.LiveAvailableBalance)}");

async Task<StrategySnapshot> ReadStrategyAsync()
{
    await using var command = new NpgsqlCommand("""
SELECT id, code, name, enabled, live_stakes, auto_live_paused, live_enabled_at_utc, live_available_balance
FROM strategies
WHERE code = @StrategyCode
LIMIT 1;
""", connection, tx);
    command.Parameters.AddWithValue("StrategyCode", StrategyCode);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException($"Strategy not found: {StrategyCode}");
    }

    return new StrategySnapshot(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetBoolean(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
        reader.GetDecimal(7));
}

async Task<LiveMetrics> ReadMetricsAsync(Guid strategyId)
{
    await using var command = new NpgsqlCommand("""
SELECT
    count(*)::integer AS live_orders_count,
    count(*) FILTER (WHERE filled_size > 0)::integer AS live_filled_orders_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND realized_pnl_usd IS NOT NULL)::integer AS live_settled_orders_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS live_won_orders_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS live_lost_orders_count,
    COALESCE(sum(CASE
        WHEN cost_basis_usd > 0 THEN cost_basis_usd
        WHEN filled_notional_usd > 0 THEN filled_notional_usd + fee_usd
        WHEN filled_size > 0 THEN price * filled_size + fee_usd
        ELSE 0
    END) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_stake_usd,
    COALESCE(sum(COALESCE(settlement_value_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_settlement_value_usd,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) > 0), 0) AS live_positive_pnl_usd,
    COALESCE(sum(-COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) < 0), 0) AS live_loss_abs_pnl_usd,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_realized_pnl_usd,
    COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)), 0) AS live_avg_win_pnl_usd,
    COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)), 0) AS live_avg_loss_pnl_usd,
    max(created_at_utc) AS live_last_order_utc,
    max(settled_at_utc) AS live_last_settlement_utc,
    max(updated_at_utc) AS max_updated_at_utc
FROM live_orders
WHERE strategy_id = @StrategyId;
""", connection, tx);
    command.Parameters.AddWithValue("StrategyId", strategyId);

    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        throw new InvalidOperationException("Metric query returned no rows.");
    }

    return new LiveMetrics(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt32(4),
        reader.GetDecimal(5),
        reader.GetDecimal(6),
        reader.GetDecimal(7),
        reader.GetDecimal(8),
        reader.GetDecimal(9),
        reader.GetDecimal(10),
        reader.GetDecimal(11),
        reader.IsDBNull(12) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(12)),
        reader.IsDBNull(13) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(13)),
        reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)));
}

async Task<IReadOnlyList<BreakdownRow>> ReadBreakdownAsync(Guid strategyId, string groupExpression)
{
    await using var command = new NpgsqlCommand($"""
SELECT
    {groupExpression} AS bucket,
    count(*)::integer AS orders_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND realized_pnl_usd IS NOT NULL)::integer AS settled_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS won_count,
    count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS lost_count,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) > 0), 0) AS positive_pnl_usd,
    COALESCE(sum(-COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) < 0), 0) AS loss_abs_pnl_usd,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS realized_pnl_usd,
    min(settled_at_utc) AS first_settlement_utc,
    max(settled_at_utc) AS last_settlement_utc,
    max(updated_at_utc) AS last_updated_utc
FROM live_orders
WHERE strategy_id = @StrategyId
GROUP BY bucket
ORDER BY realized_pnl_usd DESC, bucket;
""", connection, tx);
    command.Parameters.AddWithValue("StrategyId", strategyId);

    var rows = new List<BreakdownRow>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new BreakdownRow(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.IsDBNull(8) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(8)),
            reader.IsDBNull(9) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(9)),
            reader.IsDBNull(10) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(10))));
    }

    return rows;
}

async Task<IReadOnlyList<RecentSettlementUpdate>> ReadRecentUpdatesAsync(Guid strategyId)
{
    await using var command = new NpgsqlCommand("""
SELECT
    updated_at_utc,
    settled_at_utc,
    status,
    COALESCE(NULLIF(settlement_source, ''), '<null>') AS settlement_source,
    count(*)::integer AS rows_count,
    count(*) FILTER (WHERE COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS won_count,
    count(*) FILTER (WHERE NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0))::integer AS lost_count,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE COALESCE(realized_pnl_usd, 0) > 0), 0) AS positive_pnl_usd,
    COALESCE(sum(-COALESCE(realized_pnl_usd, 0)) FILTER (WHERE COALESCE(realized_pnl_usd, 0) < 0), 0) AS loss_abs_pnl_usd,
    COALESCE(sum(COALESCE(realized_pnl_usd, 0)), 0) AS realized_pnl_usd
FROM live_orders
WHERE strategy_id = @StrategyId
  AND settled_at_utc IS NOT NULL
  AND realized_pnl_usd IS NOT NULL
GROUP BY updated_at_utc, settled_at_utc, status, settlement_source
ORDER BY updated_at_utc DESC
LIMIT 80;
""", connection, tx);
    command.Parameters.AddWithValue("StrategyId", strategyId);

    var rows = new List<RecentSettlementUpdate>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new RecentSettlementUpdate(
            DateTimeOffsetFromUtc(reader.GetDateTime(0)),
            DateTimeOffsetFromUtc(reader.GetDateTime(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9)));
    }

    return rows;
}

async Task<IReadOnlyList<SettledLiveOrder>> ReadSettledOrdersAsync(Guid strategyId)
{
    await using var command = new NpgsqlCommand("""
SELECT
    id,
    created_at_utc,
    submitted_at_utc,
    updated_at_utc,
    settled_at_utc,
    status,
    response_status,
    COALESCE(NULLIF(settlement_source, ''), '<null>') AS settlement_source,
    condition_id,
    outcome,
    price,
    filled_size,
    COALESCE(cost_basis_usd, 0) AS cost_basis_usd,
    COALESCE(settlement_value_usd, 0) AS settlement_value_usd,
    COALESCE(realized_pnl_usd, 0) AS realized_pnl_usd,
    COALESCE(won, COALESCE(settlement_value_usd, 0) > 0) AS won,
    winning_outcome,
    order_type,
    post_only
FROM live_orders
WHERE strategy_id = @StrategyId
  AND settled_at_utc IS NOT NULL
  AND realized_pnl_usd IS NOT NULL
ORDER BY realized_pnl_usd DESC, settled_at_utc DESC;
""", connection, tx);
    command.Parameters.AddWithValue("StrategyId", strategyId);

    var rows = new List<SettledLiveOrder>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new SettledLiveOrder(
            reader.GetGuid(0),
            DateTimeOffsetFromUtc(reader.GetDateTime(1)),
            reader.IsDBNull(2) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(2)),
            DateTimeOffsetFromUtc(reader.GetDateTime(3)),
            DateTimeOffsetFromUtc(reader.GetDateTime(4)),
            reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetBoolean(15),
            reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
            reader.GetString(17),
            !reader.IsDBNull(18) && reader.GetBoolean(18)));
    }

    return rows;
}

async Task ExecuteNonQueryAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    await command.ExecuteNonQueryAsync();
}

async Task<object?> ScalarAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, tx);
    return await command.ExecuteScalarAsync();
}

static async Task WritePairAsync(StreamWriter writer, string key, string value)
{
    await writer.WriteLineAsync($"{key}\t{value}");
}

static async Task WriteBreakdownAsync(string path, IReadOnlyList<BreakdownRow> rows)
{
    await using var writer = new StreamWriter(path, append: false);
    await writer.WriteLineAsync(string.Join('\t', BreakdownRow.Columns));
    foreach (var row in rows)
    {
        await writer.WriteLineAsync(row.ToTsv());
    }
}

static DateTimeOffset DateTimeOffsetFromUtc(DateTime dateTime)
{
    return dateTime.Kind == DateTimeKind.Unspecified
        ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc))
        : new DateTimeOffset(dateTime.ToUniversalTime());
}

static string FormatDecimal(decimal value)
{
    return ReportFormat.Decimal(value);
}

static string FormatDate(DateTimeOffset? value)
{
    return ReportFormat.Date(value);
}

sealed record StrategySnapshot(
    Guid Id,
    string Code,
    string Name,
    bool Enabled,
    bool LiveStakes,
    bool AutoLivePaused,
    DateTimeOffset? LiveEnabledAtUtc,
    decimal LiveAvailableBalance);

sealed record LiveMetrics(
    int LiveOrdersCount,
    int LiveFilledOrdersCount,
    int LiveSettledOrdersCount,
    int LiveWonOrdersCount,
    int LiveLostOrdersCount,
    decimal LiveStakeUsd,
    decimal LiveSettlementValueUsd,
    decimal LivePositivePnlUsd,
    decimal LiveLossAbsPnlUsd,
    decimal LiveRealizedPnlUsd,
    decimal LiveAvgWinPnlUsd,
    decimal LiveAvgLossPnlUsd,
    DateTimeOffset? LiveLastOrderUtc,
    DateTimeOffset? LiveLastSettlementUtc,
    DateTimeOffset? MaxUpdatedAtUtc);

sealed record BreakdownRow(
    string Bucket,
    int OrdersCount,
    int SettledCount,
    int WonCount,
    int LostCount,
    decimal PositivePnlUsd,
    decimal LossAbsPnlUsd,
    decimal RealizedPnlUsd,
    DateTimeOffset? FirstSettlementUtc,
    DateTimeOffset? LastSettlementUtc,
    DateTimeOffset? LastUpdatedUtc)
{
    public static readonly string[] Columns =
    [
        "bucket",
        "orders_count",
        "settled_count",
        "won_count",
        "lost_count",
        "positive_pnl_usd",
        "loss_abs_pnl_usd",
        "realized_pnl_usd",
        "first_settlement_utc",
        "last_settlement_utc",
        "last_updated_utc"
    ];

    public string ToTsv()
    {
        return string.Join('\t',
            ReportFormat.Tsv(Bucket),
            OrdersCount.ToString(CultureInfo.InvariantCulture),
            SettledCount.ToString(CultureInfo.InvariantCulture),
            WonCount.ToString(CultureInfo.InvariantCulture),
            LostCount.ToString(CultureInfo.InvariantCulture),
            ReportFormat.Decimal(PositivePnlUsd),
            ReportFormat.Decimal(LossAbsPnlUsd),
            ReportFormat.Decimal(RealizedPnlUsd),
            ReportFormat.Date(FirstSettlementUtc),
            ReportFormat.Date(LastSettlementUtc),
            ReportFormat.Date(LastUpdatedUtc));
    }
}

sealed record RecentSettlementUpdate(
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset SettledAtUtc,
    string Status,
    string SettlementSource,
    int RowsCount,
    int WonCount,
    int LostCount,
    decimal PositivePnlUsd,
    decimal LossAbsPnlUsd,
    decimal RealizedPnlUsd)
{
    public static readonly string[] Columns =
    [
        "updated_at_utc",
        "settled_at_utc",
        "status",
        "settlement_source",
        "rows_count",
        "won_count",
        "lost_count",
        "positive_pnl_usd",
        "loss_abs_pnl_usd",
        "realized_pnl_usd"
    ];

    public string ToTsv()
    {
        return string.Join('\t',
            ReportFormat.Date(UpdatedAtUtc),
            ReportFormat.Date(SettledAtUtc),
            ReportFormat.Tsv(Status),
            ReportFormat.Tsv(SettlementSource),
            RowsCount.ToString(CultureInfo.InvariantCulture),
            WonCount.ToString(CultureInfo.InvariantCulture),
            LostCount.ToString(CultureInfo.InvariantCulture),
            ReportFormat.Decimal(PositivePnlUsd),
            ReportFormat.Decimal(LossAbsPnlUsd),
            ReportFormat.Decimal(RealizedPnlUsd));
    }
}

sealed record SettledLiveOrder(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset SettledAtUtc,
    string Status,
    string ResponseStatus,
    string SettlementSource,
    string ConditionId,
    string Outcome,
    decimal Price,
    decimal FilledSize,
    decimal CostBasisUsd,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    bool Won,
    string WinningOutcome,
    string OrderType,
    bool PostOnly)
{
    public static readonly string[] Columns =
    [
        "id",
        "created_at_utc",
        "submitted_at_utc",
        "updated_at_utc",
        "settled_at_utc",
        "status",
        "response_status",
        "settlement_source",
        "condition_id",
        "outcome",
        "price",
        "filled_size",
        "cost_basis_usd",
        "settlement_value_usd",
        "realized_pnl_usd",
        "won",
        "winning_outcome",
        "order_type",
        "post_only"
    ];

    public string ToTsv()
    {
        return string.Join('\t',
            Id,
            ReportFormat.Date(CreatedAtUtc),
            ReportFormat.Date(SubmittedAtUtc),
            ReportFormat.Date(UpdatedAtUtc),
            ReportFormat.Date(SettledAtUtc),
            ReportFormat.Tsv(Status),
            ReportFormat.Tsv(ResponseStatus),
            ReportFormat.Tsv(SettlementSource),
            ReportFormat.Tsv(ConditionId),
            ReportFormat.Tsv(Outcome),
            ReportFormat.Decimal(Price),
            ReportFormat.Decimal(FilledSize),
            ReportFormat.Decimal(CostBasisUsd),
            ReportFormat.Decimal(SettlementValueUsd),
            ReportFormat.Decimal(RealizedPnlUsd),
            Won.ToString(CultureInfo.InvariantCulture),
            ReportFormat.Tsv(WinningOutcome),
            ReportFormat.Tsv(OrderType),
            PostOnly.ToString(CultureInfo.InvariantCulture));
    }
}

static class ReportFormat
{
    public static string Decimal(decimal value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    public static string Date(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : string.Empty;
    }

    public static string Tsv(string? value)
    {
        return (value ?? string.Empty)
            .Replace('\t', ' ')
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
