using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class PostgresAppRepository(PostgresConnectionFactory connectionFactory) : IAppRepository
{
    public async Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        await TryAddLeaderTradeAsync(trade, cancellationToken);
    }

    public async Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO leader_trades (
    id, trader_wallet, trader_name, condition_id, asset_id, market_slug, market_title, outcome,
    side, price, size, cash_value_usd, timestamp_utc, transaction_hash, dedup_key, raw_json, created_at_utc
) VALUES (
    @Id, @TraderWallet, @TraderName, @ConditionId, @AssetId, @MarketSlug, @MarketTitle, @Outcome,
    @Side, @Price, @Size, @CashValueUsd, @TimestampUtc, @TransactionHash, @DedupKey, CAST(@RawJson AS jsonb), @CreatedAtUtc
)
ON CONFLICT (dedup_key) DO NOTHING;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("TraderWallet", trade.TraderWallet);
        command.Parameters.AddWithValue("TraderName", trade.TraderName);
        command.Parameters.AddWithValue("ConditionId", trade.ConditionId);
        command.Parameters.AddWithValue("AssetId", trade.AssetId);
        command.Parameters.AddWithValue("MarketSlug", trade.MarketSlug);
        command.Parameters.AddWithValue("MarketTitle", trade.MarketTitle);
        command.Parameters.AddWithValue("Outcome", trade.Outcome);
        command.Parameters.AddWithValue("Side", trade.Side.ToString());
        command.Parameters.AddWithValue("Price", trade.Price);
        command.Parameters.AddWithValue("Size", trade.Size);
        command.Parameters.AddWithValue("CashValueUsd", trade.CashValueUsd);
        command.Parameters.AddWithValue("TimestampUtc", UtcDateTime(trade.TimestampUtc));
        command.Parameters.AddWithValue("TransactionHash", (object?)trade.TransactionHash ?? DBNull.Value);
        command.Parameters.AddWithValue("DedupKey", LeaderTradeDeduplication.BuildKey(trade));
        command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(trade));
        command.Parameters.AddWithValue("CreatedAtUtc", DateTime.UtcNow);
        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows > 0;
    }

    public async Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT trader_wallet, trader_name, condition_id, asset_id, market_slug, market_title, outcome, side,
       price, size, cash_value_usd, timestamp_utc, transaction_hash
FROM leader_trades
ORDER BY timestamp_utc DESC
LIMIT 100;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<LeaderTrade>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LeaderTrade(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<TradeSide>(reader.GetString(7)),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                DateTimeOffsetFromUtc(reader.GetDateTime(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return results;
    }

    public async Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO leader_positions (
    id, trader_wallet, condition_id, asset_id, outcome, size, avg_price, initial_value, current_value,
    cash_pnl, percent_pnl, total_bought, realized_pnl, cur_price, title, market_slug, opposite_asset,
    end_date_utc, negative_risk, snapshot_at_utc, raw_json
) VALUES (
    @Id, @TraderWallet, @ConditionId, @AssetId, @Outcome, @Size, @AvgPrice, @InitialValue, @CurrentValue,
    @CashPnl, @PercentPnl, @TotalBought, @RealizedPnl, @CurPrice, @Title, @MarketSlug, @OppositeAsset,
    @EndDateUtc, @NegativeRisk, @SnapshotAtUtc, CAST(@RawJson AS jsonb)
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("TraderWallet", position.TraderWallet);
        command.Parameters.AddWithValue("ConditionId", position.ConditionId);
        command.Parameters.AddWithValue("AssetId", position.AssetId);
        command.Parameters.AddWithValue("Outcome", position.Outcome);
        command.Parameters.AddWithValue("Size", position.Size);
        command.Parameters.AddWithValue("AvgPrice", position.AvgPrice);
        command.Parameters.AddWithValue("InitialValue", position.InitialValue);
        command.Parameters.AddWithValue("CurrentValue", position.CurrentValue);
        command.Parameters.AddWithValue("CashPnl", position.CashPnl);
        command.Parameters.AddWithValue("PercentPnl", position.PercentPnl);
        command.Parameters.AddWithValue("TotalBought", position.TotalBought);
        command.Parameters.AddWithValue("RealizedPnl", position.RealizedPnl);
        command.Parameters.AddWithValue("CurPrice", position.CurPrice);
        command.Parameters.AddWithValue("Title", (object?)position.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("MarketSlug", (object?)position.MarketSlug ?? DBNull.Value);
        command.Parameters.AddWithValue("OppositeAsset", (object?)position.OppositeAsset ?? DBNull.Value);
        command.Parameters.AddWithValue("EndDateUtc", position.EndDateUtc is { } endDate ? UtcDateTime(endDate) : DBNull.Value);
        command.Parameters.AddWithValue("NegativeRisk", position.NegativeRisk);
        command.Parameters.AddWithValue("SnapshotAtUtc", UtcDateTime(position.SnapshotAtUtc));
        command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(position));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO signals (
    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,
    best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, decision,
    proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json
) VALUES (
    @Id, @LeaderTradeId, @TraderWallet, @ConditionId, @AssetId, @Outcome, @LeaderPrice,
    @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, @LagSeconds, @Score, @Decision,
    @ProposedPaperPrice, @ProposedSizeShares, @ProposedNotionalUsd, @CreatedAtUtc, CAST(@RawContextJson AS jsonb)
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", signal.Id);
        command.Parameters.AddWithValue("LeaderTradeId", DBNull.Value);
        command.Parameters.AddWithValue("TraderWallet", signal.LeaderTrade.TraderWallet);
        command.Parameters.AddWithValue("ConditionId", signal.LeaderTrade.ConditionId);
        command.Parameters.AddWithValue("AssetId", signal.LeaderTrade.AssetId);
        command.Parameters.AddWithValue("Outcome", signal.LeaderTrade.Outcome);
        command.Parameters.AddWithValue("LeaderPrice", signal.LeaderTrade.Price);
        command.Parameters.AddWithValue("BestBid", DBNull.Value);
        command.Parameters.AddWithValue("BestAsk", DBNull.Value);
        command.Parameters.AddWithValue("SpreadAbs", DBNull.Value);
        command.Parameters.AddWithValue("SpreadPct", DBNull.Value);
        command.Parameters.AddWithValue("LagSeconds", DBNull.Value);
        command.Parameters.AddWithValue("Score", signal.Score);
        command.Parameters.AddWithValue("Decision", signal.DecisionCode);
        command.Parameters.AddWithValue("ProposedPaperPrice", (object?)signal.ProposedPaperPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("ProposedSizeShares", (object?)signal.ProposedSizeShares ?? DBNull.Value);
        command.Parameters.AddWithValue("ProposedNotionalUsd", (object?)signal.ProposedNotionalUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(signal.CreatedAtUtc));
        command.Parameters.AddWithValue("RawContextJson", JsonSerializer.Serialize(signal));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO signal_rejections (id, signal_id, reason_code, reason_details, created_at_utc)
VALUES (@Id, @SignalId, @ReasonCode, @ReasonDetails, @CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", rejection.Id);
        command.Parameters.AddWithValue("SignalId", rejection.SignalId);
        command.Parameters.AddWithValue("ReasonCode", rejection.ReasonCode);
        command.Parameters.AddWithValue("ReasonDetails", rejection.ReasonDetails);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(rejection.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO paper_orders (
    id, signal_id, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json
) VALUES (
    @Id, @SignalId, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,
    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc, CAST(@RawDecisionJson AS jsonb)
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", order.Id);
        command.Parameters.AddWithValue("SignalId", order.SignalId);
        command.Parameters.AddWithValue("Status", order.Status.ToString());
        command.Parameters.AddWithValue("Side", order.Side.ToString());
        command.Parameters.AddWithValue("AssetId", order.AssetId);
        command.Parameters.AddWithValue("ConditionId", order.ConditionId);
        command.Parameters.AddWithValue("Outcome", order.Outcome);
        command.Parameters.AddWithValue("Price", order.Price);
        command.Parameters.AddWithValue("SizeShares", order.SizeShares);
        command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(order.CreatedAtUtc));
        command.Parameters.AddWithValue("ExpiresAtUtc", UtcDateTime(order.ExpiresAtUtc));
        command.Parameters.AddWithValue("FilledAtUtc", order.FilledAtUtc is { } filledAt ? UtcDateTime(filledAt) : DBNull.Value);
        command.Parameters.AddWithValue("CancelledAtUtc", order.CancelledAtUtc is { } cancelledAt ? UtcDateTime(cancelledAt) : DBNull.Value);
        command.Parameters.AddWithValue("RawDecisionJson", JsonSerializer.Serialize(order));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE paper_orders
SET status = @Status,
    filled_at_utc = @FilledAtUtc,
    cancelled_at_utc = @CancelledAtUtc,
    raw_decision_json = CAST(@RawDecisionJson AS jsonb)
WHERE id = @Id;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", order.Id);
        command.Parameters.AddWithValue("Status", order.Status.ToString());
        command.Parameters.AddWithValue("FilledAtUtc", order.FilledAtUtc is { } filledAt ? UtcDateTime(filledAt) : DBNull.Value);
        command.Parameters.AddWithValue("CancelledAtUtc", order.CancelledAtUtc is { } cancelledAt ? UtcDateTime(cancelledAt) : DBNull.Value);
        command.Parameters.AddWithValue("RawDecisionJson", JsonSerializer.Serialize(order));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,
       created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc
FROM paper_orders
WHERE status IN ('Pending', 'PartiallyFilled')
ORDER BY created_at_utc DESC;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PaperOrder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PaperOrder(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<PaperOrderStatus>(reader.GetString(2)),
                Enum.Parse<TradeSide>(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                DateTimeOffsetFromUtc(reader.GetDateTime(10)),
                DateTimeOffsetFromUtc(reader.GetDateTime(11)),
                reader.IsDBNull(12) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(12)),
                reader.IsDBNull(13) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(13))));
        }

        return results;
    }

    public async Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO paper_fills (id, paper_order_id, price, size_shares, filled_at_utc, evidence)
VALUES (@Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, @Evidence);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", fill.Id);
        command.Parameters.AddWithValue("PaperOrderId", fill.PaperOrderId);
        command.Parameters.AddWithValue("Price", fill.Price);
        command.Parameters.AddWithValue("SizeShares", fill.SizeShares);
        command.Parameters.AddWithValue("FilledAtUtc", UtcDateTime(fill.FilledAtUtc));
        command.Parameters.AddWithValue("Evidence", fill.Evidence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO paper_positions (
    id, asset_id, condition_id, outcome, size_shares, average_price,
    estimated_value_usd, unrealized_pnl_usd, updated_at_utc
) VALUES (
    @Id, @AssetId, @ConditionId, @Outcome, @SizeShares, @AveragePrice,
    @EstimatedValueUsd, @UnrealizedPnlUsd, @UpdatedAtUtc
)
ON CONFLICT (asset_id) DO UPDATE SET
    condition_id = excluded.condition_id,
    outcome = excluded.outcome,
    size_shares = excluded.size_shares,
    average_price = excluded.average_price,
    estimated_value_usd = excluded.estimated_value_usd,
    unrealized_pnl_usd = excluded.unrealized_pnl_usd,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("AssetId", position.AssetId);
        command.Parameters.AddWithValue("ConditionId", position.ConditionId);
        command.Parameters.AddWithValue("Outcome", position.Outcome);
        command.Parameters.AddWithValue("SizeShares", position.SizeShares);
        command.Parameters.AddWithValue("AveragePrice", position.AveragePrice);
        command.Parameters.AddWithValue("EstimatedValueUsd", position.EstimatedValueUsd);
        command.Parameters.AddWithValue("UnrealizedPnlUsd", position.UnrealizedPnlUsd);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(position.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc
FROM paper_positions
ORDER BY updated_at_utc DESC;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PaperPosition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PaperPosition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                DateTimeOffsetFromUtc(reader.GetDateTime(7))));
        }

        return results;
    }

    public async Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO api_errors (id, component, operation, message, created_at_utc)
VALUES (@Id, @Component, @Operation, @Message, @CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", error.Id);
        command.Parameters.AddWithValue("Component", error.Component);
        command.Parameters.AddWithValue("Operation", error.Operation);
        command.Parameters.AddWithValue("Message", error.Message);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(error.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO scanner_status (
    scanner_name, status, last_successful_scan_utc, last_error_utc, last_error_message,
    trades_fetched, new_trades_stored, positions_fetched, updated_at_utc
) VALUES (
    @ScannerName, @Status, @LastSuccessfulScanUtc, @LastErrorUtc, @LastErrorMessage,
    @TradesFetched, @NewTradesStored, @PositionsFetched, @UpdatedAtUtc
)
ON CONFLICT(scanner_name) DO UPDATE SET
    status = excluded.status,
    last_successful_scan_utc = excluded.last_successful_scan_utc,
    last_error_utc = excluded.last_error_utc,
    last_error_message = excluded.last_error_message,
    trades_fetched = excluded.trades_fetched,
    new_trades_stored = excluded.new_trades_stored,
    positions_fetched = excluded.positions_fetched,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ScannerName", status.ScannerName);
        command.Parameters.AddWithValue("Status", status.ScannerStatus);
        command.Parameters.AddWithValue(
            "LastSuccessfulScanUtc",
            status.LastSuccessfulScanUtc is { } successfulScan ? UtcDateTime(successfulScan) : DBNull.Value);
        command.Parameters.AddWithValue(
            "LastErrorUtc",
            status.LastErrorUtc is { } errorUtc ? UtcDateTime(errorUtc) : DBNull.Value);
        command.Parameters.AddWithValue("LastErrorMessage", (object?)status.LastErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("TradesFetched", status.TradesFetched);
        command.Parameters.AddWithValue("NewTradesStored", status.NewTradesStored);
        command.Parameters.AddWithValue("PositionsFetched", status.PositionsFetched);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(status.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT scanner_name, status, last_successful_scan_utc, last_error_utc, last_error_message,
       trades_fetched, new_trades_stored, positions_fetched, updated_at_utc
FROM scanner_status
ORDER BY scanner_name;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ScannerStatusSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ScannerStatusSnapshot(
                reader.GetString(0),
                reader.IsDBNull(2) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(2)),
                reader.IsDBNull(3) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(1),
                DateTimeOffsetFromUtc(reader.GetDateTime(8))));
        }

        return results;
    }

    public async Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO service_heartbeats (
    service_name, status, started_at_utc, last_heartbeat_utc, version, mode, current_loop, last_error
) VALUES (
    @ServiceName, @Status, @StartedAtUtc, @LastHeartbeatUtc, @Version, @Mode, @CurrentLoop, @LastError
)
ON CONFLICT(service_name) DO UPDATE SET
    status = excluded.status,
    started_at_utc = excluded.started_at_utc,
    last_heartbeat_utc = excluded.last_heartbeat_utc,
    version = excluded.version,
    mode = excluded.mode,
    current_loop = excluded.current_loop,
    last_error = excluded.last_error;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ServiceName", heartbeat.ServiceName);
        command.Parameters.AddWithValue("Status", heartbeat.Status);
        command.Parameters.AddWithValue("StartedAtUtc", UtcDateTime(heartbeat.StartedAtUtc));
        command.Parameters.AddWithValue("LastHeartbeatUtc", UtcDateTime(heartbeat.LastHeartbeatUtc));
        command.Parameters.AddWithValue("Version", heartbeat.Version);
        command.Parameters.AddWithValue("Mode", heartbeat.Mode.ToString());
        command.Parameters.AddWithValue("CurrentLoop", heartbeat.CurrentLoop);
        command.Parameters.AddWithValue("LastError", (object?)heartbeat.LastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT service_name, status, started_at_utc, last_heartbeat_utc, version, mode, current_loop, last_error
FROM service_heartbeats
ORDER BY service_name;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ServiceHeartbeat>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ServiceHeartbeat(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffsetFromUtc(reader.GetDateTime(2)),
                DateTimeOffsetFromUtc(reader.GetDateTime(3)),
                reader.GetString(4),
                Enum.Parse<BotMode>(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql)
    {
        return new NpgsqlCommand(sql, connection);
    }

    private static DateTime UtcDateTime(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime;
    }

    private static DateTimeOffset DateTimeOffsetFromUtc(DateTime timestamp)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
    }
}
