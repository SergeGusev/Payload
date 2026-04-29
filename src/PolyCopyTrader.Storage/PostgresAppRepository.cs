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

    public async Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT trader_wallet, trader_name, condition_id, asset_id, market_slug, market_title, outcome, side,
       price, size, cash_value_usd, timestamp_utc, transaction_hash
FROM leader_trades
ORDER BY timestamp_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
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
    accepted, proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json
) VALUES (
    @Id, @LeaderTradeId, @TraderWallet, @ConditionId, @AssetId, @Outcome, @LeaderPrice,
    @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, @LagSeconds, @Score, @Decision,
    @Accepted, @ProposedPaperPrice, @ProposedSizeShares, @ProposedNotionalUsd, @CreatedAtUtc, CAST(@RawContextJson AS jsonb)
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
        command.Parameters.AddWithValue("Accepted", signal.Accepted);
        command.Parameters.AddWithValue("ProposedPaperPrice", (object?)signal.ProposedPaperPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("ProposedSizeShares", (object?)signal.ProposedSizeShares ?? DBNull.Value);
        command.Parameters.AddWithValue("ProposedNotionalUsd", (object?)signal.ProposedNotionalUsd ?? DBNull.Value);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(signal.CreatedAtUtc));
        command.Parameters.AddWithValue("RawContextJson", JsonSerializer.Serialize(signal));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT s.id, s.trader_wallet, s.condition_id, s.asset_id, s.outcome, s.leader_price,
       s.best_bid, s.best_ask, s.spread_abs, s.spread_pct, s.lag_seconds, s.score,
       s.accepted, s.decision, s.proposed_paper_price, s.proposed_size_shares,
       s.proposed_notional_usd, s.created_at_utc,
       COALESCE(string_agg(sr.reason_code, ',' ORDER BY sr.created_at_utc), '') AS reason_codes
FROM signals s
LEFT JOIN signal_rejections sr ON sr.signal_id = s.id
GROUP BY s.id
ORDER BY s.created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<SignalSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SignalSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetBoolean(12),
                reader.GetString(13),
                SplitReasonCodes(reader.GetString(18)),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                DateTimeOffsetFromUtc(reader.GetDateTime(17))));
        }

        return results;
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

    public async Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, reason_code, reason_details, created_at_utc
FROM signal_rejections
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<SignalRejection>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SignalRejection(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffsetFromUtc(reader.GetDateTime(4))));
        }

        return results;
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
            results.Add(ReadPaperOrder(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,
       created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc
FROM paper_orders
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PaperOrder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPaperOrder(reader));
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

    public async Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence
FROM paper_fills
ORDER BY filled_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PaperFill>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PaperFill(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                DateTimeOffsetFromUtc(reader.GetDateTime(4)),
                reader.GetString(5)));
        }

        return results;
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

    public async Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, component, operation, message, created_at_utc
FROM api_errors
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ApiError>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ApiError(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffsetFromUtc(reader.GetDateTime(4))));
        }

        return results;
    }

    public async Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, reason_code, details, created_at_utc
FROM risk_events
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<RiskEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RiskEvent(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffsetFromUtc(reader.GetDateTime(3))));
        }

        return results;
    }

    public async Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO order_book_snapshots (
    id, asset_id, condition_id, best_bid, best_ask, spread_abs, spread_pct, raw_json, snapshot_at_utc
) VALUES (
    @Id, @AssetId, @ConditionId, @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, CAST(@RawJson AS jsonb), @SnapshotAtUtc
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("AssetId", snapshot.AssetId);
        command.Parameters.AddWithValue("ConditionId", (object?)snapshot.ConditionId ?? DBNull.Value);
        command.Parameters.AddWithValue("BestBid", (object?)snapshot.BestBid ?? DBNull.Value);
        command.Parameters.AddWithValue("BestAsk", (object?)snapshot.BestAsk ?? DBNull.Value);
        command.Parameters.AddWithValue("SpreadAbs", (object?)snapshot.SpreadAbs ?? DBNull.Value);
        command.Parameters.AddWithValue("SpreadPct", (object?)snapshot.SpreadPct ?? DBNull.Value);
        command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("SnapshotAtUtc", UtcDateTime(snapshot.SnapshotAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT asset_id, condition_id, best_bid, best_ask, snapshot_at_utc
FROM order_book_snapshots
WHERE asset_id = @AssetId
ORDER BY snapshot_at_utc DESC
LIMIT 1;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("AssetId", assetId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadOrderBookSnapshot(reader)
            : null;
    }

    public async Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT asset_id, condition_id, best_bid, best_ask, snapshot_at_utc
FROM (
    SELECT DISTINCT ON (asset_id)
        asset_id, condition_id, best_bid, best_ask, snapshot_at_utc
    FROM order_book_snapshots
    ORDER BY asset_id, snapshot_at_utc DESC
) latest
ORDER BY snapshot_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<OrderBookSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadOrderBookSnapshot(reader));
        }

        return results;
    }

    public async Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO market_data_events (id, event_type, asset_id, condition_id, message, received_at_utc)
VALUES (@Id, @EventType, @AssetId, @ConditionId, @Message, @ReceivedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", marketDataEvent.Id);
        command.Parameters.AddWithValue("EventType", marketDataEvent.EventType.ToString());
        command.Parameters.AddWithValue("AssetId", (object?)marketDataEvent.AssetId ?? DBNull.Value);
        command.Parameters.AddWithValue("ConditionId", (object?)marketDataEvent.ConditionId ?? DBNull.Value);
        command.Parameters.AddWithValue("Message", marketDataEvent.Message);
        command.Parameters.AddWithValue("ReceivedAtUtc", UtcDateTime(marketDataEvent.ReceivedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, event_type, asset_id, condition_id, message, received_at_utc
FROM market_data_events
ORDER BY received_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<MarketDataEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarketDataEvent(
                reader.GetGuid(0),
                Enum.Parse<MarketDataEventType>(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                DateTimeOffsetFromUtc(reader.GetDateTime(5))));
        }

        return results;
    }

    public async Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO market_data_status (
    component, connection_state, endpoint, subscribed_assets_count, last_message_utc,
    last_connected_utc, last_disconnected_utc, reconnect_count, stale, last_error, updated_at_utc
) VALUES (
    @Component, @ConnectionState, @Endpoint, @SubscribedAssetsCount, @LastMessageUtc,
    @LastConnectedUtc, @LastDisconnectedUtc, @ReconnectCount, @Stale, @LastError, @UpdatedAtUtc
)
ON CONFLICT(component) DO UPDATE SET
    connection_state = excluded.connection_state,
    endpoint = excluded.endpoint,
    subscribed_assets_count = excluded.subscribed_assets_count,
    last_message_utc = excluded.last_message_utc,
    last_connected_utc = excluded.last_connected_utc,
    last_disconnected_utc = excluded.last_disconnected_utc,
    reconnect_count = excluded.reconnect_count,
    stale = excluded.stale,
    last_error = excluded.last_error,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Component", status.Component);
        command.Parameters.AddWithValue("ConnectionState", status.ConnectionState.ToString());
        command.Parameters.AddWithValue("Endpoint", status.Endpoint);
        command.Parameters.AddWithValue("SubscribedAssetsCount", status.SubscribedAssetsCount);
        command.Parameters.AddWithValue("LastMessageUtc", status.LastMessageUtc is { } lastMessage ? UtcDateTime(lastMessage) : DBNull.Value);
        command.Parameters.AddWithValue("LastConnectedUtc", status.LastConnectedUtc is { } connected ? UtcDateTime(connected) : DBNull.Value);
        command.Parameters.AddWithValue("LastDisconnectedUtc", status.LastDisconnectedUtc is { } disconnected ? UtcDateTime(disconnected) : DBNull.Value);
        command.Parameters.AddWithValue("ReconnectCount", status.ReconnectCount);
        command.Parameters.AddWithValue("Stale", status.Stale);
        command.Parameters.AddWithValue("LastError", (object?)status.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(status.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT component, connection_state, endpoint, subscribed_assets_count, last_message_utc,
       last_connected_utc, last_disconnected_utc, reconnect_count, stale, last_error, updated_at_utc
FROM market_data_status
ORDER BY component;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<MarketDataStatusSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MarketDataStatusSnapshot(
                reader.GetString(0),
                Enum.Parse<MarketDataConnectionState>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
                reader.GetInt32(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                DateTimeOffsetFromUtc(reader.GetDateTime(10))));
        }

        return results;
    }

    public async Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO pinned_market_assets (asset_id, note, created_at_utc)
VALUES (@AssetId, @Note, @CreatedAtUtc)
ON CONFLICT(asset_id) DO UPDATE SET
    note = excluded.note;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("AssetId", asset.AssetId);
        command.Parameters.AddWithValue("Note", (object?)asset.Note ?? DBNull.Value);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(asset.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        const string sql = """
DELETE FROM pinned_market_assets
WHERE asset_id = @AssetId;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("AssetId", assetId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT asset_id, note, created_at_utc
FROM pinned_market_assets
ORDER BY created_at_utc DESC;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PinnedMarketAsset>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new PinnedMarketAsset(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                DateTimeOffsetFromUtc(reader.GetDateTime(2))));
        }

        return results;
    }

    public async Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH bounds AS (
    SELECT @StartUtc::timestamptz AS start_utc, @EndUtc::timestamptz AS end_utc
),
top_rejections AS (
    SELECT string_agg(reason_code || ':' || reason_count, '; ' ORDER BY reason_count DESC, reason_code) AS reasons
    FROM (
        SELECT sr.reason_code, count(*) AS reason_count
        FROM signal_rejections sr, bounds b
        WHERE sr.created_at_utc >= b.start_utc AND sr.created_at_utc < b.end_utc
        GROUP BY sr.reason_code
        ORDER BY reason_count DESC, sr.reason_code
        LIMIT 5
    ) ranked
)
SELECT
    (SELECT count(*)::integer FROM signals s, bounds b WHERE s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_observed,
    (SELECT count(*)::integer FROM signals s, bounds b WHERE s.accepted AND s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_accepted,
    (SELECT count(*)::integer FROM signals s, bounds b WHERE NOT s.accepted AND s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_rejected,
    (SELECT count(*)::integer FROM paper_orders po, bounds b WHERE po.created_at_utc >= b.start_utc AND po.created_at_utc < b.end_utc) AS paper_orders_created,
    (SELECT count(*)::integer FROM paper_fills pf, bounds b WHERE pf.filled_at_utc >= b.start_utc AND pf.filled_at_utc < b.end_utc) AS paper_fills,
    (SELECT count(*)::integer FROM paper_orders po, bounds b WHERE po.status = 'Expired' AND po.expires_at_utc >= b.start_utc AND po.expires_at_utc < b.end_utc) AS paper_expired_orders,
    COALESCE((SELECT sum(pp.unrealized_pnl_usd) FROM paper_positions pp), 0) AS paper_pnl,
    COALESCE((SELECT sum(po.notional_usd) FROM paper_orders po WHERE po.status IN ('Pending', 'PartiallyFilled')), 0)
        + COALESCE((SELECT sum(pp.estimated_value_usd) FROM paper_positions pp), 0) AS open_paper_exposure,
    COALESCE((SELECT reasons FROM top_rejections), '') AS top_rejection_reasons,
    (SELECT count(*)::integer FROM api_errors ae, bounds b WHERE ae.created_at_utc >= b.start_utc AND ae.created_at_utc < b.end_utc) AS api_errors;
""";

        var startUtc = reportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = reportDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("StartUtc", startUtc);
        command.Parameters.AddWithValue("EndUtc", endUtc);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DailyReport(reportDate, 0, 0, 0, 0, 0, 0, 0m, 0m, string.Empty, 0, DateTimeOffset.UtcNow);
        }

        return new DailyReport(
            reportDate,
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetString(8),
            reader.GetInt32(9),
            DateTimeOffset.UtcNow);
    }

    public async Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO daily_reports (
    report_date, signals_observed, signals_accepted, signals_rejected, paper_orders_created,
    paper_fills, paper_expired_orders, paper_pnl, open_paper_exposure, top_rejection_reasons,
    api_errors, generated_at_utc
) VALUES (
    @ReportDate, @SignalsObserved, @SignalsAccepted, @SignalsRejected, @PaperOrdersCreated,
    @PaperFills, @PaperExpiredOrders, @PaperPnl, @OpenPaperExposure, @TopRejectionReasons,
    @ApiErrors, @GeneratedAtUtc
)
ON CONFLICT(report_date) DO UPDATE SET
    signals_observed = excluded.signals_observed,
    signals_accepted = excluded.signals_accepted,
    signals_rejected = excluded.signals_rejected,
    paper_orders_created = excluded.paper_orders_created,
    paper_fills = excluded.paper_fills,
    paper_expired_orders = excluded.paper_expired_orders,
    paper_pnl = excluded.paper_pnl,
    open_paper_exposure = excluded.open_paper_exposure,
    top_rejection_reasons = excluded.top_rejection_reasons,
    api_errors = excluded.api_errors,
    generated_at_utc = excluded.generated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ReportDate", report.ReportDate);
        command.Parameters.AddWithValue("SignalsObserved", report.SignalsObserved);
        command.Parameters.AddWithValue("SignalsAccepted", report.SignalsAccepted);
        command.Parameters.AddWithValue("SignalsRejected", report.SignalsRejected);
        command.Parameters.AddWithValue("PaperOrdersCreated", report.PaperOrdersCreated);
        command.Parameters.AddWithValue("PaperFills", report.PaperFills);
        command.Parameters.AddWithValue("PaperExpiredOrders", report.PaperExpiredOrders);
        command.Parameters.AddWithValue("PaperPnl", report.PaperPnl);
        command.Parameters.AddWithValue("OpenPaperExposure", report.OpenPaperExposure);
        command.Parameters.AddWithValue("TopRejectionReasons", report.TopRejectionReasons);
        command.Parameters.AddWithValue("ApiErrors", report.ApiErrors);
        command.Parameters.AddWithValue("GeneratedAtUtc", UtcDateTime(report.GeneratedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT report_date, signals_observed, signals_accepted, signals_rejected, paper_orders_created,
       paper_fills, paper_expired_orders, paper_pnl, open_paper_exposure, top_rejection_reasons,
       api_errors, generated_at_utc
FROM daily_reports
ORDER BY report_date DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<DailyReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadDailyReport(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH signal_stats AS (
    SELECT
        s.trader_wallet,
        count(*) AS signals,
        count(*) FILTER (WHERE s.accepted) AS accepted,
        avg(s.lag_seconds)::numeric AS avg_lag,
        avg(s.leader_price)::numeric AS avg_leader_price,
        avg(s.proposed_paper_price)::numeric AS avg_proposed_price,
        avg(s.proposed_paper_price - s.leader_price)::numeric AS avg_price_difference
    FROM signals s
    GROUP BY s.trader_wallet
),
fill_stats AS (
    SELECT
        s.trader_wallet,
        count(DISTINCT po.id) AS orders,
        count(DISTINCT po.id) FILTER (WHERE pf.id IS NOT NULL) AS filled_orders,
        COALESCE(sum((COALESCE(pp.estimated_value_usd / NULLIF(pp.size_shares, 0), po.price) - po.price) * po.size_shares) FILTER (WHERE pf.id IS NOT NULL), 0) AS paper_pnl
    FROM signals s
    LEFT JOIN paper_orders po ON po.signal_id = s.id
    LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
    LEFT JOIN paper_positions pp ON pp.asset_id = po.asset_id
    GROUP BY s.trader_wallet
),
rejection_stats AS (
    SELECT trader_wallet, string_agg(reason_code || ':' || reason_count, '; ' ORDER BY reason_count DESC, reason_code) AS reasons
    FROM (
        SELECT s.trader_wallet, sr.reason_code, count(*) AS reason_count
        FROM signals s
        JOIN signal_rejections sr ON sr.signal_id = s.id
        GROUP BY s.trader_wallet, sr.reason_code
    ) ranked
    GROUP BY trader_wallet
),
category_pnl AS (
    SELECT trader_wallet, string_agg(category || ':' || pnl, '; ' ORDER BY category) AS pnl_by_category
    FROM (
        SELECT
            s.trader_wallet,
            COALESCE(m.category, 'unknown') AS category,
            COALESCE(sum((COALESCE(pp.estimated_value_usd / NULLIF(pp.size_shares, 0), po.price) - po.price) * po.size_shares) FILTER (WHERE pf.id IS NOT NULL), 0) AS pnl
        FROM signals s
        LEFT JOIN markets m ON m.condition_id = s.condition_id
        LEFT JOIN paper_orders po ON po.signal_id = s.id
        LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
        LEFT JOIN paper_positions pp ON pp.asset_id = po.asset_id
        GROUP BY s.trader_wallet, COALESCE(m.category, 'unknown')
    ) grouped
    GROUP BY trader_wallet
)
SELECT
    ss.trader_wallet,
    ss.signals::integer,
    CASE WHEN ss.signals = 0 THEN 0 ELSE round(ss.accepted::numeric / ss.signals * 100, 4) END AS acceptance_rate,
    CASE WHEN COALESCE(fs.orders, 0) = 0 THEN 0 ELSE round(fs.filled_orders::numeric / fs.orders * 100, 4) END AS fill_rate,
    ss.avg_lag,
    ss.avg_leader_price,
    ss.avg_proposed_price,
    ss.avg_price_difference,
    COALESCE(fs.paper_pnl, 0) AS paper_pnl,
    COALESCE(cp.pnl_by_category, '') AS paper_pnl_by_category,
    COALESCE(rs.reasons, '') AS rejection_reasons
FROM signal_stats ss
LEFT JOIN fill_stats fs ON fs.trader_wallet = ss.trader_wallet
LEFT JOIN rejection_stats rs ON rs.trader_wallet = ss.trader_wallet
LEFT JOIN category_pnl cp ON cp.trader_wallet = ss.trader_wallet
ORDER BY ss.signals DESC, ss.trader_wallet
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<TraderPerformanceReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TraderPerformanceReport(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return results;
    }

    public async Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
    COALESCE(m.category, 'unknown') AS category,
    count(DISTINCT s.id)::integer AS signals,
    count(DISTINCT s.id) FILTER (WHERE s.accepted)::integer AS accepted,
    count(DISTINCT po.id) FILTER (WHERE pf.id IS NOT NULL)::integer AS filled,
    COALESCE(sum((COALESCE(pp.estimated_value_usd / NULLIF(pp.size_shares, 0), po.price) - po.price) * po.size_shares) FILTER (WHERE pf.id IS NOT NULL), 0) AS paper_pnl,
    avg(s.spread_abs)::numeric AS avg_spread,
    avg(s.lag_seconds)::numeric AS avg_lag
FROM signals s
LEFT JOIN markets m ON m.condition_id = s.condition_id
LEFT JOIN paper_orders po ON po.signal_id = s.id
LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
LEFT JOIN paper_positions pp ON pp.asset_id = po.asset_id
GROUP BY COALESCE(m.category, 'unknown')
ORDER BY signals DESC, category
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<CategoryPerformanceReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CategoryPerformanceReport(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
        }

        return results;
    }

    public async Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT
    s.id, s.trader_wallet, s.asset_id, s.condition_id, s.created_at_utc,
    s.leader_price, s.proposed_paper_price, pf.price AS fill_price,
    s.proposed_paper_price - s.leader_price AS proposed_minus_leader,
    pf.price - s.proposed_paper_price AS fill_minus_proposed,
    s.lag_seconds, s.spread_abs,
    ob1.best_bid AS bid_1m, ob1.best_ask AS ask_1m,
    CASE WHEN ob1.best_bid IS NULL OR ob1.best_ask IS NULL THEN NULL ELSE (ob1.best_bid + ob1.best_ask) / 2 END AS mid_1m,
    ob5.best_bid AS bid_5m, ob5.best_ask AS ask_5m,
    CASE WHEN ob5.best_bid IS NULL OR ob5.best_ask IS NULL THEN NULL ELSE (ob5.best_bid + ob5.best_ask) / 2 END AS mid_5m,
    ob30.best_bid AS bid_30m, ob30.best_ask AS ask_30m,
    CASE WHEN ob30.best_bid IS NULL OR ob30.best_ask IS NULL THEN NULL ELSE (ob30.best_bid + ob30.best_ask) / 2 END AS mid_30m
FROM signals s
LEFT JOIN paper_orders po ON po.signal_id = s.id
LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
LEFT JOIN LATERAL (
    SELECT best_bid, best_ask FROM order_book_snapshots obs
    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '1 minute'
    ORDER BY obs.snapshot_at_utc
    LIMIT 1
) ob1 ON true
LEFT JOIN LATERAL (
    SELECT best_bid, best_ask FROM order_book_snapshots obs
    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '5 minutes'
    ORDER BY obs.snapshot_at_utc
    LIMIT 1
) ob5 ON true
LEFT JOIN LATERAL (
    SELECT best_bid, best_ask FROM order_book_snapshots obs
    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '30 minutes'
    ORDER BY obs.snapshot_at_utc
    LIMIT 1
) ob30 ON true
ORDER BY s.created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ExecutionQualityReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ExecutionQualityReport(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffsetFromUtc(reader.GetDateTime(4)),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20)));
        }

        return results;
    }

    public async Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH rejected AS (
    SELECT count(*) AS total_rejected FROM signals WHERE NOT accepted
),
reason_counts AS (
    SELECT sr.reason_code, count(*) AS reason_count, max(sr.created_at_utc) AS last_rejected_at
    FROM signal_rejections sr
    GROUP BY sr.reason_code
)
SELECT
    rc.reason_code,
    rc.reason_count::integer,
    CASE WHEN r.total_rejected = 0 THEN 0 ELSE round(rc.reason_count::numeric / r.total_rejected * 100, 4) END AS rejected_pct,
    rc.last_rejected_at
FROM reason_counts rc
CROSS JOIN rejected r
ORDER BY rc.reason_count DESC, rc.reason_code
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<RejectionAnalysisReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RejectionAnalysisReport(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(3))));
        }

        return results;
    }

    public async Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO service_command_audit (id, command, source, accepted, message, created_at_utc)
VALUES (@Id, @Command, @Source, @Accepted, @Message, @CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", audit.Id);
        command.Parameters.AddWithValue("Command", audit.Command);
        command.Parameters.AddWithValue("Source", audit.Source);
        command.Parameters.AddWithValue("Accepted", audit.Accepted);
        command.Parameters.AddWithValue("Message", audit.Message);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(audit.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, command, source, accepted, message, created_at_utc
FROM service_command_audit
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<ServiceCommandAudit>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ServiceCommandAudit(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4),
                DateTimeOffsetFromUtc(reader.GetDateTime(5))));
        }

        return results;
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

    private static PaperOrder ReadPaperOrder(NpgsqlDataReader reader)
    {
        return new PaperOrder(
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
            reader.IsDBNull(13) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(13)));
    }

    private static OrderBookSnapshot ReadOrderBookSnapshot(NpgsqlDataReader reader)
    {
        decimal? bestBid = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
        decimal? bestAsk = reader.IsDBNull(3) ? null : reader.GetDecimal(3);
        return new OrderBookSnapshot(
            reader.GetString(0),
            bestBid is { } bid ? [new OrderBookLevel(bid, 0m)] : [],
            bestAsk is { } ask ? [new OrderBookLevel(ask, 0m)] : [],
            DateTimeOffsetFromUtc(reader.GetDateTime(4)),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static DailyReport ReadDailyReport(NpgsqlDataReader reader)
    {
        return new DailyReport(
            reader.GetFieldValue<DateOnly>(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetString(9),
            reader.GetInt32(10),
            DateTimeOffsetFromUtc(reader.GetDateTime(11)));
    }

    private static IReadOnlyList<string> SplitReasonCodes(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
