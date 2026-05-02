using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class PostgresAppRepository(PostgresConnectionFactory connectionFactory) : IAppRepository
{
    private const int OnChainDerivedRefreshLockKey1 = 0x50635452;
    private const int OnChainDerivedRefreshLockKey2 = 0x4F435246;

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

    public async Task AddTraderLeaderboardSnapshotsAsync(
        IReadOnlyList<TraderLeaderboardSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO trader_leaderboard_snapshots (
    id, discovery_run_id, category, time_period, wallet, user_name, x_username, verified_badge,
    pnl_rank, pnl_page_offset, pnl_leaderboard_pnl, pnl_leaderboard_volume, pnl_snapshot_at_utc,
    volume_rank, volume_page_offset, volume_leaderboard_pnl, volume_leaderboard_volume, volume_snapshot_at_utc,
    updated_at_utc
) VALUES (
    @Id, @DiscoveryRunId, @Category, @TimePeriod, @Wallet, @UserName, @XUsername, @VerifiedBadge,
    @PnlRank, @PnlPageOffset, @PnlLeaderboardPnl, @PnlLeaderboardVolume, @PnlSnapshotAtUtc,
    @VolumeRank, @VolumePageOffset, @VolumeLeaderboardPnl, @VolumeLeaderboardVolume, @VolumeSnapshotAtUtc,
    @UpdatedAtUtc
)
ON CONFLICT (category, time_period, wallet) DO UPDATE SET
    discovery_run_id = excluded.discovery_run_id,
    user_name = excluded.user_name,
    x_username = excluded.x_username,
    verified_badge = excluded.verified_badge,
    pnl_rank = excluded.pnl_rank,
    pnl_page_offset = excluded.pnl_page_offset,
    pnl_leaderboard_pnl = excluded.pnl_leaderboard_pnl,
    pnl_leaderboard_volume = excluded.pnl_leaderboard_volume,
    pnl_snapshot_at_utc = excluded.pnl_snapshot_at_utc,
    volume_rank = excluded.volume_rank,
    volume_page_offset = excluded.volume_page_offset,
    volume_leaderboard_pnl = excluded.volume_leaderboard_pnl,
    volume_leaderboard_volume = excluded.volume_leaderboard_volume,
    volume_snapshot_at_utc = excluded.volume_snapshot_at_utc,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var snapshot in snapshots)
        {
            await using var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            AddTraderLeaderboardSnapshotParameters(command, snapshot);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpsertTraderDiscoveryCandidatesAsync(
        IReadOnlyList<TraderDiscoveryCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO trader_discovery_candidates (
    id, discovery_type, category, time_period, rank, wallet, user_name, x_username,
    leaderboard_pnl, leaderboard_volume, all_time_pnl, all_time_volume, verified_badge, trades_fetched, buy_trades,
    sell_trades, recent_trade_volume_usd, average_trade_usd, last_trade_utc,
    positions_fetched, open_position_value_usd, open_position_cash_pnl_usd,
    open_position_realized_pnl_usd, notes, snapshot_at_utc, updated_at_utc
) VALUES (
    @Id, @DiscoveryType, @Category, @TimePeriod, @Rank, @Wallet, @UserName, @XUsername,
    @LeaderboardPnl, @LeaderboardVolume, @AllTimePnl, @AllTimeVolume, @VerifiedBadge, @TradesFetched, @BuyTrades,
    @SellTrades, @RecentTradeVolumeUsd, @AverageTradeUsd, @LastTradeUtc,
    @PositionsFetched, @OpenPositionValueUsd, @OpenPositionCashPnlUsd,
    @OpenPositionRealizedPnlUsd, @Notes, @SnapshotAtUtc, @UpdatedAtUtc
)
ON CONFLICT (discovery_type, category, time_period, wallet) DO UPDATE SET
    id = excluded.id,
    rank = excluded.rank,
    user_name = excluded.user_name,
    x_username = excluded.x_username,
    leaderboard_pnl = excluded.leaderboard_pnl,
    leaderboard_volume = excluded.leaderboard_volume,
    all_time_pnl = excluded.all_time_pnl,
    all_time_volume = excluded.all_time_volume,
    verified_badge = excluded.verified_badge,
    trades_fetched = excluded.trades_fetched,
    buy_trades = excluded.buy_trades,
    sell_trades = excluded.sell_trades,
    recent_trade_volume_usd = excluded.recent_trade_volume_usd,
    average_trade_usd = excluded.average_trade_usd,
    last_trade_utc = excluded.last_trade_utc,
    positions_fetched = excluded.positions_fetched,
    open_position_value_usd = excluded.open_position_value_usd,
    open_position_cash_pnl_usd = excluded.open_position_cash_pnl_usd,
    open_position_realized_pnl_usd = excluded.open_position_realized_pnl_usd,
    notes = excluded.notes,
    snapshot_at_utc = excluded.snapshot_at_utc,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            await using var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            AddTraderDiscoveryParameters(command, candidate);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, discovery_type, category, time_period, rank, wallet, user_name, x_username,
       leaderboard_pnl, leaderboard_volume, all_time_pnl, all_time_volume, verified_badge, trades_fetched, buy_trades,
       sell_trades, recent_trade_volume_usd, average_trade_usd, last_trade_utc,
       positions_fetched, open_position_value_usd, open_position_cash_pnl_usd,
       open_position_realized_pnl_usd, notes, snapshot_at_utc
FROM trader_discovery_candidates
ORDER BY snapshot_at_utc DESC,
         discovery_type,
         CASE WHEN discovery_type = 'WorstPnl' THEN leaderboard_pnl END ASC,
         leaderboard_pnl DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<TraderDiscoveryCandidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TraderDiscoveryCandidate(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                reader.GetBoolean(12),
                reader.GetInt32(13),
                reader.GetInt32(14),
                reader.GetInt32(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.IsDBNull(18) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(18)),
                reader.GetInt32(19),
                reader.GetDecimal(20),
                reader.GetDecimal(21),
                reader.GetDecimal(22),
                reader.GetString(23),
                DateTimeOffsetFromUtc(reader.GetDateTime(24))));
        }

        return results;
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

    public async Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO dry_run_orders (
    id, signal_id, status, side, asset_id, condition_id, outcome, price, size_shares,
    notional_usd, order_type, payload_json, validation_summary, created_at_utc
) VALUES (
    @Id, @SignalId, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares,
    @NotionalUsd, @OrderType, CAST(@PayloadJson AS jsonb), @ValidationSummary, @CreatedAtUtc
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
        command.Parameters.AddWithValue("OrderType", order.OrderType);
        command.Parameters.AddWithValue("PayloadJson", order.PayloadJson);
        command.Parameters.AddWithValue("ValidationSummary", order.ValidationSummary);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(order.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, status, side, asset_id, condition_id, outcome, price, size_shares,
       notional_usd, order_type, payload_json::text, validation_summary, created_at_utc
FROM dry_run_orders
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<DryRunOrder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DryRunOrder(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<DryRunOrderStatus>(reader.GetString(2)),
                Enum.Parse<TradeSide>(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                DateTimeOffsetFromUtc(reader.GetDateTime(13))));
        }

        return results;
    }

    public async Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO live_orders (
    id, signal_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,
    notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,
    filled_size, remaining_size, cancel_status, raw_response_json, validation_summary, updated_at_utc
) VALUES (
    @Id, @SignalId, @Status, @OrderId, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares,
    @NotionalUsd, @OrderType, @CreatedAtUtc, @ExpiresAtUtc, @SubmittedAtUtc, @ResponseStatus,
    @FilledSize, @RemainingSize, @CancelStatus, CAST(@RawResponseJson AS jsonb), @ValidationSummary, @UpdatedAtUtc
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddLiveOrderParameters(command, order);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
UPDATE live_orders
SET status = @Status,
    order_id = @OrderId,
    submitted_at_utc = @SubmittedAtUtc,
    response_status = @ResponseStatus,
    filled_size = @FilledSize,
    remaining_size = @RemainingSize,
    cancel_status = @CancelStatus,
    raw_response_json = CAST(@RawResponseJson AS jsonb),
    validation_summary = @ValidationSummary,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @Id;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddLiveOrderParameters(command, order);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,
       notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,
       filled_size, remaining_size, cancel_status, raw_response_json::text, validation_summary, updated_at_utc
FROM live_orders
WHERE status IN ('Submitted', 'Live', 'Delayed', 'CancelRequested')
ORDER BY created_at_utc DESC;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadLiveOrdersAsync(reader, cancellationToken);
    }

    public async Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, signal_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,
       notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,
       filled_size, remaining_size, cancel_status, raw_response_json::text, validation_summary, updated_at_utc
FROM live_orders
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await ReadLiveOrdersAsync(reader, cancellationToken);
    }

    public async Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO live_trading_events (id, action, status, details, created_at_utc)
VALUES (@Id, @Action, @Status, @Details, @CreatedAtUtc);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Id", liveEvent.Id);
        command.Parameters.AddWithValue("Action", liveEvent.Action);
        command.Parameters.AddWithValue("Status", liveEvent.Status);
        command.Parameters.AddWithValue("Details", liveEvent.Details);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(liveEvent.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, action, status, details, created_at_utc
FROM live_trading_events
ORDER BY created_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<LiveTradingEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LiveTradingEvent(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffsetFromUtc(reader.GetDateTime(4))));
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

    public async Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO polymarket_http_logs (
    id, component, operation, http_method, request_url, requested_at_utc, response_at_utc,
    duration_ms, attempt, status_code, succeeded, response_body, error_message
) VALUES (
    @Id, @Component, @Operation, @HttpMethod, @RequestUrl, @RequestedAtUtc, @ResponseAtUtc,
    @DurationMs, @Attempt, @StatusCode, @Succeeded, @ResponseBody, @ErrorMessage
);
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        AddPolymarketHttpLogParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, component, operation, http_method, request_url, requested_at_utc, response_at_utc,
       duration_ms, attempt, status_code, succeeded, response_body, error_message
FROM polymarket_http_logs
ORDER BY requested_at_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketHttpLogEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketHttpLogEntry(reader));
        }

        return results;
    }

    public async Task AddPolymarketOnChainLogsAsync(
        IReadOnlyList<PolymarketOnChainLog> logs,
        CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO polymarket_onchain_logs (
    id, contract_name, contract_address, exchange_version, block_number, block_hash,
    transaction_hash, transaction_index, log_index, topic0, topics_json, data, removed, observed_at_utc
) VALUES (
    @Id, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber, @BlockHash,
    @TransactionHash, @TransactionIndex, @LogIndex, @Topic0, CAST(@TopicsJson AS jsonb), @Data, @Removed, @ObservedAtUtc
)
ON CONFLICT (transaction_hash, log_index) DO UPDATE SET
    contract_name = excluded.contract_name,
    contract_address = excluded.contract_address,
    exchange_version = excluded.exchange_version,
    block_number = excluded.block_number,
    block_hash = excluded.block_hash,
    transaction_index = excluded.transaction_index,
    topic0 = excluded.topic0,
    topics_json = excluded.topics_json,
    data = excluded.data,
    removed = excluded.removed,
    observed_at_utc = excluded.observed_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var log in logs)
        {
            await using var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            AddPolymarketOnChainLogParameters(command, log);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddPolymarketOnChainFillsAsync(
        IReadOnlyList<PolymarketOnChainFill> fills,
        CancellationToken cancellationToken = default)
    {
        if (fills.Count == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO polymarket_onchain_fills (
    id, contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,
    transaction_hash, log_index, order_hash, maker, taker, wallet, side, token_id,
    maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw, maker_amount, taker_amount,
    price, size_shares, notional_usd, fee_raw, fee_amount, fee_asset_id, builder, metadata, imported_at_utc
) VALUES (
    @Id, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber, @BlockTimestampUtc,
    @TransactionHash, @LogIndex, @OrderHash, @Maker, @Taker, @Wallet, @Side, @TokenId,
    @MakerAssetId, @TakerAssetId, @MakerAmountRaw, @TakerAmountRaw, @MakerAmount, @TakerAmount,
    @Price, @SizeShares, @NotionalUsd, @FeeRaw, @FeeAmount, @FeeAssetId, @Builder, @Metadata, @ImportedAtUtc
)
ON CONFLICT (transaction_hash, log_index) DO UPDATE SET
    contract_name = excluded.contract_name,
    contract_address = excluded.contract_address,
    exchange_version = excluded.exchange_version,
    block_number = excluded.block_number,
    block_timestamp_utc = excluded.block_timestamp_utc,
    order_hash = excluded.order_hash,
    maker = excluded.maker,
    taker = excluded.taker,
    wallet = excluded.wallet,
    side = excluded.side,
    token_id = excluded.token_id,
    maker_asset_id = excluded.maker_asset_id,
    taker_asset_id = excluded.taker_asset_id,
    maker_amount_raw = excluded.maker_amount_raw,
    taker_amount_raw = excluded.taker_amount_raw,
    maker_amount = excluded.maker_amount,
    taker_amount = excluded.taker_amount,
    price = excluded.price,
    size_shares = excluded.size_shares,
    notional_usd = excluded.notional_usd,
    fee_raw = excluded.fee_raw,
    fee_amount = excluded.fee_amount,
    fee_asset_id = excluded.fee_asset_id,
    builder = excluded.builder,
    metadata = excluded.metadata,
    imported_at_utc = excluded.imported_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var fill in fills)
        {
            await using var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            AddPolymarketOnChainFillParameters(command, fill);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var range in fills
            .GroupBy(fill => fill.ContractAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ContractAddress = group.Key,
                FromBlock = group.Min(fill => fill.BlockNumber),
                ToBlock = group.Max(fill => fill.BlockNumber)
            }))
        {
            await UpsertPolymarketOnChainWalletFillsAsync(
                connection,
                transaction,
                range.ContractAddress,
                range.FromBlock,
                range.ToBlock,
                cancellationToken);
            await UpsertPolymarketOnChainWalletExecutionsAsync(
                connection,
                transaction,
                range.ContractAddress,
                range.FromBlock,
                range.ToBlock,
                cancellationToken);
            await UpsertPolymarketOnChainTradeDetailsAsync(
                connection,
                transaction,
                range.ContractAddress,
                range.FromBlock,
                range.ToBlock,
                cancellationToken);
            await QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(
                connection,
                transaction,
                range.ContractAddress,
                range.FromBlock,
                range.ToBlock,
                "execution",
                cancellationToken);
            await DeleteProcessedPolymarketOnChainRawLogsAsync(
                connection,
                transaction,
                range.ContractAddress,
                range.FromBlock,
                range.ToBlock,
                cancellationToken);
        }

        await QueuePolymarketOnChainPositionRefreshTokensAsync(
            connection,
            transaction,
            fills.Select(fill => fill.TokenId).Distinct(StringComparer.OrdinalIgnoreCase),
            "execution",
            cancellationToken);
        await QueuePolymarketOnChainTokenMetadataRefreshTokensAsync(
            connection,
            transaction,
            fills.Select(fill => fill.TokenId).Distinct(StringComparer.OrdinalIgnoreCase),
            "execution",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertPolymarketOnChainWalletFillsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_wallet_fills (
    source_fill_id, contract_name, contract_address, exchange_version, block_number,
    block_timestamp_utc, transaction_hash, log_index, order_hash, role, wallet, counterparty,
    side, token_id, price, size_shares, notional_usd, fee_amount, fee_asset_id, imported_at_utc
)
SELECT id, contract_name, contract_address, exchange_version, block_number,
       block_timestamp_utc, transaction_hash, log_index, order_hash, 'Maker',
       maker, taker, side, token_id, price, size_shares, notional_usd,
       fee_amount, fee_asset_id, imported_at_utc
FROM polymarket_onchain_fills
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock
UNION ALL
SELECT id, contract_name, contract_address, exchange_version, block_number,
       block_timestamp_utc, transaction_hash, log_index, order_hash, 'Taker',
       taker, maker,
       CASE side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE side END,
       token_id, price, size_shares, notional_usd, 0, '0', imported_at_utc
FROM polymarket_onchain_fills
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock
ON CONFLICT (transaction_hash, log_index, role) DO UPDATE SET
    source_fill_id = excluded.source_fill_id,
    contract_name = excluded.contract_name,
    contract_address = excluded.contract_address,
    exchange_version = excluded.exchange_version,
    block_number = excluded.block_number,
    block_timestamp_utc = excluded.block_timestamp_utc,
    order_hash = excluded.order_hash,
    wallet = excluded.wallet,
    counterparty = excluded.counterparty,
    side = excluded.side,
    token_id = excluded.token_id,
    price = excluded.price,
    size_shares = excluded.size_shares,
    notional_usd = excluded.notional_usd,
    fee_amount = excluded.fee_amount,
    fee_asset_id = excluded.fee_asset_id,
    imported_at_utc = excluded.imported_at_utc;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertPolymarketOnChainWalletExecutionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        const string sql = """
DELETE FROM polymarket_onchain_wallet_executions
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock;

INSERT INTO polymarket_onchain_wallet_executions (
    contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,
    transaction_hash, first_log_index, last_log_index, wallet, side, token_id, fill_count,
    maker_fill_count, taker_fill_count, size_shares, notional_usd, average_price,
    fees_usd, imported_at_utc
)
SELECT contract_name,
       contract_address,
       exchange_version,
       MIN(block_number),
       MIN(block_timestamp_utc),
       transaction_hash,
       MIN(log_index),
       MAX(log_index),
       wallet,
       side,
       token_id,
       COUNT(*)::integer,
       COUNT(*) FILTER (WHERE role = 'Maker')::integer,
       COUNT(*) FILTER (WHERE role = 'Taker')::integer,
       SUM(size_shares),
       SUM(notional_usd),
       CASE WHEN SUM(size_shares) = 0 THEN 0 ELSE SUM(notional_usd) / SUM(size_shares) END,
       SUM(CASE WHEN fee_asset_id = '0' THEN fee_amount ELSE 0 END),
       MAX(imported_at_utc)
FROM polymarket_onchain_wallet_fills
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock
GROUP BY contract_name, contract_address, exchange_version, transaction_hash, wallet, side, token_id
ON CONFLICT (contract_address, transaction_hash, wallet, side, token_id) DO UPDATE SET
    contract_name = excluded.contract_name,
    exchange_version = excluded.exchange_version,
    block_number = excluded.block_number,
    block_timestamp_utc = excluded.block_timestamp_utc,
    first_log_index = excluded.first_log_index,
    last_log_index = excluded.last_log_index,
    fill_count = excluded.fill_count,
    maker_fill_count = excluded.maker_fill_count,
    taker_fill_count = excluded.taker_fill_count,
    size_shares = excluded.size_shares,
    notional_usd = excluded.notional_usd,
    average_price = excluded.average_price,
    fees_usd = excluded.fees_usd,
    imported_at_utc = excluded.imported_at_utc;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertPolymarketOnChainTradeDetailsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_trade_details (
    contract_name,
    contract_address,
    exchange_version,
    block_number,
    block_timestamp_utc,
    transaction_hash,
    log_index,
    order_hash,
    maker,
    taker,
    maker_side,
    taker_side,
    token_id,
    maker_asset_id,
    taker_asset_id,
    maker_amount_raw,
    taker_amount_raw,
    maker_amount,
    taker_amount,
    price,
    size_shares,
    notional_usd,
    fee_amount,
    fee_asset_id,
    builder,
    order_metadata,
    condition_id,
    market_id,
    market_slug,
    market_title,
    outcome,
    category,
    lookup_succeeded,
    market_active,
    market_closed,
    market_archived,
    market_resolved,
    winning_outcome,
    imported_at_utc,
    refreshed_at_utc
)
SELECT
    raw_fill.contract_name,
    raw_fill.contract_address,
    raw_fill.exchange_version,
    raw_fill.block_number,
    raw_fill.block_timestamp_utc,
    raw_fill.transaction_hash,
    raw_fill.log_index,
    raw_fill.order_hash,
    raw_fill.maker,
    raw_fill.taker,
    raw_fill.side,
    CASE raw_fill.side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE raw_fill.side END,
    raw_fill.token_id,
    raw_fill.maker_asset_id,
    raw_fill.taker_asset_id,
    raw_fill.maker_amount_raw,
    raw_fill.taker_amount_raw,
    raw_fill.maker_amount,
    raw_fill.taker_amount,
    raw_fill.price,
    raw_fill.size_shares,
    raw_fill.notional_usd,
    raw_fill.fee_amount,
    raw_fill.fee_asset_id,
    raw_fill.builder,
    raw_fill.metadata,
    COALESCE(token_metadata.condition_id, ''),
    COALESCE(token_metadata.market_id, ''),
    COALESCE(token_metadata.market_slug, ''),
    COALESCE(token_metadata.market_title, 'Unenriched token ' || left(raw_fill.token_id, 16)),
    COALESCE(token_metadata.outcome, 'Unknown'),
    token_metadata.category,
    COALESCE(token_metadata.lookup_succeeded, false),
    COALESCE(token_metadata.active, false),
    COALESCE(token_metadata.closed, false),
    COALESCE(token_metadata.archived, false),
    COALESCE(token_metadata.resolved, false),
    token_metadata.winning_outcome,
    raw_fill.imported_at_utc,
    now()
FROM polymarket_onchain_fills raw_fill
LEFT JOIN polymarket_onchain_token_metadata token_metadata
       ON token_metadata.token_id = raw_fill.token_id
WHERE raw_fill.contract_address = @ContractAddress
  AND raw_fill.block_number BETWEEN @FromBlock AND @ToBlock
ON CONFLICT (transaction_hash, log_index) DO UPDATE SET
    contract_name = excluded.contract_name,
    contract_address = excluded.contract_address,
    exchange_version = excluded.exchange_version,
    block_number = excluded.block_number,
    block_timestamp_utc = excluded.block_timestamp_utc,
    order_hash = excluded.order_hash,
    maker = excluded.maker,
    taker = excluded.taker,
    maker_side = excluded.maker_side,
    taker_side = excluded.taker_side,
    token_id = excluded.token_id,
    maker_asset_id = excluded.maker_asset_id,
    taker_asset_id = excluded.taker_asset_id,
    maker_amount_raw = excluded.maker_amount_raw,
    taker_amount_raw = excluded.taker_amount_raw,
    maker_amount = excluded.maker_amount,
    taker_amount = excluded.taker_amount,
    price = excluded.price,
    size_shares = excluded.size_shares,
    notional_usd = excluded.notional_usd,
    fee_amount = excluded.fee_amount,
    fee_asset_id = excluded.fee_asset_id,
    builder = excluded.builder,
    order_metadata = excluded.order_metadata,
    condition_id = excluded.condition_id,
    market_id = excluded.market_id,
    market_slug = excluded.market_slug,
    market_title = excluded.market_title,
    outcome = excluded.outcome,
    category = excluded.category,
    lookup_succeeded = excluded.lookup_succeeded,
    market_active = excluded.market_active,
    market_closed = excluded.market_closed,
    market_archived = excluded.market_archived,
    market_resolved = excluded.market_resolved,
    winning_outcome = excluded.winning_outcome,
    imported_at_utc = excluded.imported_at_utc,
    refreshed_at_utc = excluded.refreshed_at_utc;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RefreshPolymarketOnChainTradeDetailsMetadataAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<string> tokenIds,
        CancellationToken cancellationToken)
    {
        var distinctTokenIds = tokenIds
            .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctTokenIds.Length == 0)
        {
            return;
        }

        const string sql = """
UPDATE polymarket_onchain_trade_details trade_detail
SET
    condition_id = COALESCE(token_metadata.condition_id, ''),
    market_id = COALESCE(token_metadata.market_id, ''),
    market_slug = COALESCE(token_metadata.market_slug, ''),
    market_title = COALESCE(token_metadata.market_title, 'Unenriched token ' || left(trade_detail.token_id, 16)),
    outcome = COALESCE(token_metadata.outcome, 'Unknown'),
    category = token_metadata.category,
    lookup_succeeded = COALESCE(token_metadata.lookup_succeeded, false),
    market_active = COALESCE(token_metadata.active, false),
    market_closed = COALESCE(token_metadata.closed, false),
    market_archived = COALESCE(token_metadata.archived, false),
    market_resolved = COALESCE(token_metadata.resolved, false),
    winning_outcome = token_metadata.winning_outcome,
    refreshed_at_utc = now()
FROM polymarket_onchain_token_metadata token_metadata
WHERE token_metadata.token_id = trade_detail.token_id
  AND trade_detail.token_id = ANY(@TokenIds);
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteProcessedPolymarketOnChainRawLogsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        const string sql = """
DELETE FROM polymarket_onchain_logs raw_log
WHERE raw_log.contract_address = @ContractAddress
  AND raw_log.block_number BETWEEN @FromBlock AND @ToBlock
  AND EXISTS (
      SELECT 1
      FROM polymarket_onchain_trade_details trade_detail
      WHERE trade_detail.transaction_hash = raw_log.transaction_hash
        AND trade_detail.log_index = raw_log.log_index
  );
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertOnChainIngestionCursorAsync(
        OnChainIngestionCursor cursor,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
INSERT INTO polymarket_onchain_ingest_cursors (
    contract_address, contract_name, exchange_version, from_block, to_block,
    logs_fetched, fills_stored, started_at_utc, completed_at_utc
) VALUES (
    @ContractAddress, @ContractName, @ExchangeVersion, @FromBlock, @ToBlock,
    @LogsFetched, @FillsStored, @StartedAtUtc, @CompletedAtUtc
)
ON CONFLICT (contract_address) DO UPDATE SET
    contract_name = excluded.contract_name,
    exchange_version = excluded.exchange_version,
    from_block = excluded.from_block,
    to_block = excluded.to_block,
    logs_fetched = excluded.logs_fetched,
    fills_stored = excluded.fills_stored,
    started_at_utc = excluded.started_at_utc,
    completed_at_utc = excluded.completed_at_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(cursor.ContractAddress));
        command.Parameters.AddWithValue("ContractName", cursor.ContractName);
        command.Parameters.AddWithValue("ExchangeVersion", cursor.ExchangeVersion);
        command.Parameters.AddWithValue("FromBlock", cursor.FromBlock);
        command.Parameters.AddWithValue("ToBlock", cursor.ToBlock);
        command.Parameters.AddWithValue("LogsFetched", cursor.LogsFetched);
        command.Parameters.AddWithValue("FillsStored", cursor.FillsStored);
        command.Parameters.AddWithValue("StartedAtUtc", UtcDateTime(cursor.StartedAtUtc));
        command.Parameters.AddWithValue("CompletedAtUtc", UtcDateTime(cursor.CompletedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(
        string contractAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT contract_address, contract_name, exchange_version, from_block, to_block,
       logs_fetched, fills_stored, started_at_utc, completed_at_utc
FROM polymarket_onchain_ingest_cursors
WHERE contract_address = @ContractAddress
LIMIT 1;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OnChainIngestionCursor(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            DateTimeOffsetFromUtc(reader.GetDateTime(7)),
            DateTimeOffsetFromUtc(reader.GetDateTime(8)));
    }

    public async Task<long?> GetLatestPolymarketOnChainFillBlockAsync(
        string contractAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT block_number
FROM polymarket_onchain_fills
WHERE contract_address = @ContractAddress
ORDER BY block_number DESC
LIMIT 1;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is DBNull or null ? null : (long)result;
    }

    public async Task<OnChainBlockRange?> GetPolymarketOnChainFillBlockRangeAsync(
        string contractAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH first_block AS (
    SELECT block_number
    FROM polymarket_onchain_fills
    WHERE contract_address = @ContractAddress
    ORDER BY block_number ASC
    LIMIT 1
),
last_block AS (
    SELECT block_number
    FROM polymarket_onchain_fills
    WHERE contract_address = @ContractAddress
    ORDER BY block_number DESC
    LIMIT 1
)
SELECT first_block.block_number, last_block.block_number
FROM first_block
CROSS JOIN last_block;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1));
    }

    public async Task<OnChainBlockRange?> GetPolymarketOnChainWalletExecutionBlockRangeAsync(
        string contractAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH first_block AS (
    SELECT block_number
    FROM polymarket_onchain_wallet_executions
    WHERE contract_address = @ContractAddress
    ORDER BY block_number ASC
    LIMIT 1
),
last_block AS (
    SELECT block_number
    FROM polymarket_onchain_wallet_executions
    WHERE contract_address = @ContractAddress
    ORDER BY block_number DESC
    LIMIT 1
)
SELECT first_block.block_number, last_block.block_number
FROM first_block
CROSS JOIN last_block;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1));
    }

    public async Task<OnChainBlockRange?> GetPolymarketOnChainTradeDetailsBlockRangeAsync(
        string contractAddress,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
WITH first_block AS (
    SELECT block_number
    FROM polymarket_onchain_trade_details
    WHERE contract_address = @ContractAddress
    ORDER BY block_number ASC
    LIMIT 1
),
last_block AS (
    SELECT block_number
    FROM polymarket_onchain_trade_details
    WHERE contract_address = @ContractAddress
    ORDER BY block_number DESC
    LIMIT 1
)
SELECT first_block.block_number, last_block.block_number
FROM first_block
CROSS JOIN last_block;
""";

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                return null;
            }

            return new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    public async Task RefreshPolymarketOnChainWalletDerivedDataAsync(
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken = default)
    {
        if (fromBlock > toBlock)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await UpsertPolymarketOnChainWalletFillsAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            cancellationToken);
        await UpsertPolymarketOnChainWalletExecutionsAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            cancellationToken);
        await UpsertPolymarketOnChainTradeDetailsAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            cancellationToken);
        await QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            "derived_refresh",
            cancellationToken);
        await QueuePolymarketOnChainPositionRefreshTokensForRangeAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            "derived_refresh",
            cancellationToken);
        await QueuePolymarketOnChainTokenMetadataRefreshTokensForRangeAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            "derived_refresh",
            cancellationToken);
        await DeleteProcessedPolymarketOnChainRawLogsAsync(
            connection,
            transaction,
            contractAddress,
            fromBlock,
            toBlock,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PolymarketOnChainWalletExecution>> GetRecentPolymarketOnChainWalletExecutionsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,
       transaction_hash, first_log_index, last_log_index, wallet, side, token_id,
       fill_count, maker_fill_count, taker_fill_count, size_shares, notional_usd,
       average_price, fees_usd, imported_at_utc
FROM polymarket_onchain_wallet_executions
ORDER BY block_timestamp_utc DESC, block_number DESC, first_log_index DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketOnChainWalletExecution>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketOnChainWalletExecution(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetOnChainTokenIdsMissingMetadataAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT refresh_queue.token_id
FROM polymarket_onchain_token_metadata_refresh_queue refresh_queue
LEFT JOIN polymarket_onchain_token_metadata metadata
  ON metadata.token_id = refresh_queue.token_id
WHERE refresh_queue.next_attempt_at_utc <= now()
  AND (
      metadata.token_id IS NULL
      OR NOT metadata.lookup_succeeded
      OR NULLIF(metadata.category, '') IS NULL
  )
ORDER BY refresh_queue.next_attempt_at_utc, refresh_queue.queued_at_utc, refresh_queue.token_id
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(connection, transaction, cancellationToken);

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Limit", limit);
        var results = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(reader.GetString(0));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }

    public async Task<PolymarketOnChainTokenMetadata?> GetPolymarketOnChainTokenMetadataAsync(
        string tokenId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return null;
        }

        const string sql = """
SELECT token_id, condition_id, market_id, market_slug, market_title, outcome, outcome_index,
       category, end_date_utc, active, closed, archived, resolved, winning_outcome,
       clob_token_ids_json, outcomes_json, lookup_succeeded, lookup_error, raw_json,
       last_refreshed_utc
FROM polymarket_onchain_token_metadata
WHERE token_id = @TokenId
LIMIT 1;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("TokenId", tokenId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadPolymarketOnChainTokenMetadata(reader)
            : null;
    }

    public async Task UpsertPolymarketOnChainTokenMetadataAsync(
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        CancellationToken cancellationToken = default)
    {
        if (metadata.Count == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO polymarket_onchain_token_metadata (
    token_id, condition_id, market_id, market_slug, market_title, outcome, outcome_index,
    category, end_date_utc, active, closed, archived, resolved, winning_outcome,
    clob_token_ids_json, outcomes_json, lookup_succeeded, lookup_error, raw_json,
    last_refreshed_utc
) VALUES (
    @TokenId, @ConditionId, @MarketId, @MarketSlug, @MarketTitle, @Outcome, @OutcomeIndex,
    @Category, @EndDateUtc, @Active, @Closed, @Archived, @Resolved, @WinningOutcome,
    CAST(@ClobTokenIdsJson AS jsonb), CAST(@OutcomesJson AS jsonb), @LookupSucceeded,
    @LookupError, CAST(@RawJson AS jsonb), @LastRefreshedUtc
)
ON CONFLICT (token_id) DO UPDATE SET
    condition_id = excluded.condition_id,
    market_id = excluded.market_id,
    market_slug = excluded.market_slug,
    market_title = excluded.market_title,
    outcome = excluded.outcome,
    outcome_index = excluded.outcome_index,
    category = excluded.category,
    end_date_utc = excluded.end_date_utc,
    active = excluded.active,
    closed = excluded.closed,
    archived = excluded.archived,
    resolved = excluded.resolved,
    winning_outcome = excluded.winning_outcome,
    clob_token_ids_json = excluded.clob_token_ids_json,
    outcomes_json = excluded.outcomes_json,
    lookup_succeeded = excluded.lookup_succeeded,
    lookup_error = excluded.lookup_error,
    raw_json = excluded.raw_json,
    last_refreshed_utc = excluded.last_refreshed_utc;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var item in metadata)
        {
            await using var command = CreateCommand(connection, sql);
            command.Transaction = transaction;
            AddPolymarketOnChainTokenMetadataParameters(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await QueuePolymarketOnChainPositionRefreshTokensAsync(
            connection,
            transaction,
            metadata.Select(item => item.TokenId).Distinct(StringComparer.OrdinalIgnoreCase),
            "metadata",
            cancellationToken);
        await RefreshPolymarketOnChainTradeDetailsMetadataAsync(
            connection,
            transaction,
            metadata.Select(item => item.TokenId),
            cancellationToken);
        await DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(
            connection,
            transaction,
            cancellationToken);
        await RescheduleIncompletePolymarketOnChainTokenMetadataRefreshQueueAsync(
            connection,
            transaction,
            metadata.Select(item => item.TokenId),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PolymarketOnChainFill>> GetRecentPolymarketOnChainFillsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT id, contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,
       transaction_hash, log_index, order_hash, maker, taker, wallet, side, token_id,
       maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw, maker_amount, taker_amount,
       price, size_shares, notional_usd, fee_raw, fee_amount, fee_asset_id, builder, metadata, imported_at_utc
FROM polymarket_onchain_fills
ORDER BY block_timestamp_utc DESC, block_number DESC, log_index DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketOnChainFill>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketOnChainFill(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<TraderOnChainStats>> GetTraderOnChainStatsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT wallet, executions, buy_executions, sell_executions, markets_traded,
       volume_usd, average_trade_usd, fees_usd, activity_score,
       first_trade_utc, last_trade_utc
FROM polymarket_onchain_wallet_activity
ORDER BY activity_score DESC, volume_usd DESC
LIMIT @Limit;
""";

        var results = new List<TraderOnChainStats>();
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("Limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new TraderOnChainStats(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    DateTimeOffsetFromUtc(reader.GetDateTime(9)),
                    DateTimeOffsetFromUtc(reader.GetDateTime(10))));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return results;
        }

        return results;
    }

    public async Task<OnChainActivityRefreshResult> RefreshPolymarketOnChainWalletActivityAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken))
        {
            var remaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainActivityRefreshResult(0, 0, 0, remaining);
        }

        var walletsQueued = await SeedMissingPolymarketOnChainWalletActivityRefreshQueueAsync(
            connection,
            transaction,
            queueSeedWalletLimit,
            cancellationToken);
        walletsQueued += await SeedMissingPolymarketOnChainParticipantDetailsRefreshQueueAsync(
            connection,
            transaction,
            queueSeedWalletLimit,
            cancellationToken);

        await using (var createTempCommand = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_wallet_activity_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
        {
            createTempCommand.Transaction = transaction;
            await createTempCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string selectWalletsSql = """
WITH queued AS (
    SELECT wallet
    FROM polymarket_onchain_wallet_activity_refresh_queue
    ORDER BY queued_at_utc, wallet
    LIMIT @WalletLimit
    FOR UPDATE SKIP LOCKED
)
INSERT INTO temp_wallet_activity_refresh_wallets (wallet)
SELECT wallet
FROM queued
ON CONFLICT (wallet) DO NOTHING;
""";

        await using (var selectWalletsCommand = CreateCommand(connection, selectWalletsSql))
        {
            selectWalletsCommand.Transaction = transaction;
            selectWalletsCommand.Parameters.AddWithValue("WalletLimit", walletLimit);
            await selectWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var walletsProcessed = await CountTempWalletActivityRefreshWalletsAsync(connection, transaction, cancellationToken);
        if (walletsProcessed == 0)
        {
            var remaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainActivityRefreshResult(walletsQueued, 0, 0, remaining);
        }

        await using (var deleteCommand = CreateCommand(
            connection,
            """
DELETE FROM polymarket_onchain_wallet_activity
WHERE wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets);
"""))
        {
            deleteCommand.Transaction = transaction;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string upsertSql = """
INSERT INTO polymarket_onchain_wallet_activity (
    wallet,
    executions,
    buy_executions,
    sell_executions,
    markets_traded,
    volume_usd,
    average_trade_usd,
    fees_usd,
    activity_score,
    first_trade_utc,
    last_trade_utc,
    refreshed_at_utc
)
SELECT wallet,
       executions,
       buy_executions,
       sell_executions,
       markets_traded,
       volume_usd,
       average_trade_usd,
       fees_usd,
       volume_usd + executions + markets_traded * 5,
       first_trade_utc,
       last_trade_utc,
       now()
FROM (
    SELECT execution.wallet,
           COUNT(*)::integer AS executions,
           COUNT(*) FILTER (WHERE execution.side = 'Buy')::integer AS buy_executions,
           COUNT(*) FILTER (WHERE execution.side = 'Sell')::integer AS sell_executions,
           COUNT(DISTINCT execution.token_id)::integer AS markets_traded,
           COALESCE(SUM(execution.notional_usd), 0) AS volume_usd,
           COALESCE(AVG(execution.notional_usd), 0) AS average_trade_usd,
           COALESCE(SUM(execution.fees_usd), 0) AS fees_usd,
           MIN(execution.block_timestamp_utc) AS first_trade_utc,
           MAX(execution.block_timestamp_utc) AS last_trade_utc
    FROM polymarket_onchain_wallet_executions execution
    WHERE execution.wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets)
    GROUP BY execution.wallet
) activity_aggregate
ON CONFLICT (wallet) DO UPDATE SET
    executions = excluded.executions,
    buy_executions = excluded.buy_executions,
    sell_executions = excluded.sell_executions,
    markets_traded = excluded.markets_traded,
    volume_usd = excluded.volume_usd,
    average_trade_usd = excluded.average_trade_usd,
    fees_usd = excluded.fees_usd,
    activity_score = excluded.activity_score,
    first_trade_utc = excluded.first_trade_utc,
    last_trade_utc = excluded.last_trade_utc,
    refreshed_at_utc = excluded.refreshed_at_utc;
""";

        int walletsUpserted;
        await using (var upsertCommand = CreateCommand(connection, upsertSql))
        {
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandTimeout = 300;
            walletsUpserted = await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(
            connection,
            transaction,
            "temp_wallet_activity_refresh_wallets",
            cancellationToken);

        await using (var clearQueueCommand = CreateCommand(
            connection,
            """
DELETE FROM polymarket_onchain_wallet_activity_refresh_queue
WHERE wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets);
"""))
        {
            clearQueueCommand.Transaction = transaction;
            await clearQueueCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new OnChainActivityRefreshResult(walletsQueued, walletsProcessed, walletsUpserted, queueRemaining);
    }

    public async Task<IReadOnlyList<PolymarketOnChainWalletPosition>> GetPolymarketOnChainWalletPositionsAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT wallet, token_id, condition_id, market_id, market_slug, market_title, outcome,
       category, lookup_succeeded, market_resolved, winning_outcome,
       executions, buy_executions, sell_executions, buy_shares, sell_shares, net_shares,
       buy_notional_usd, sell_notional_usd, net_cost_usd, fees_usd, average_buy_price,
       average_sell_price, volume_usd, resolved_pnl_usd, position_status,
       first_trade_utc, last_trade_utc
FROM polymarket_onchain_wallet_positions
ORDER BY absolute_net_cost_usd DESC, volume_usd DESC, last_trade_utc DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketOnChainWalletPosition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketOnChainWalletPosition(reader));
        }

        return results;
    }

    public async Task<OnChainPositionRefreshResult> RefreshPolymarketOnChainWalletPositionsAsync(
        int tokenLimit = 50,
        int queueSeedTokenLimit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken))
        {
            var remaining = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainPositionRefreshResult(0, 0, 0, remaining);
        }

        var tokensQueued = await SeedMissingPolymarketOnChainPositionRefreshTokensAsync(
            connection,
            transaction,
            queueSeedTokenLimit,
            cancellationToken);

        await using (var createTemp = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_position_refresh_tokens (token_id text PRIMARY KEY) ON COMMIT DROP;"))
        {
            createTemp.Transaction = transaction;
            await createTemp.ExecuteNonQueryAsync(cancellationToken);
        }

        const string pickSql = """
WITH picked AS (
    SELECT token_id
    FROM polymarket_onchain_position_refresh_queue
    ORDER BY queued_at_utc
    LIMIT @TokenLimit
    FOR UPDATE SKIP LOCKED
)
INSERT INTO temp_position_refresh_tokens (token_id)
SELECT token_id
FROM picked;
""";

        await using (var pickCommand = CreateCommand(connection, pickSql))
        {
            pickCommand.Transaction = transaction;
            pickCommand.Parameters.AddWithValue("TokenLimit", tokenLimit);
            await pickCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var tokensProcessed = await CountTempPositionRefreshTokensAsync(connection, transaction, cancellationToken);
        if (tokensProcessed == 0)
        {
            var remaining = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainPositionRefreshResult(tokensQueued, 0, 0, remaining);
        }

        await using (var createWalletsCommand = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_position_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
        {
            createWalletsCommand.Transaction = transaction;
            await createWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createCategoryPairsCommand = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_wallet_category_performance_refresh_pairs (wallet text NOT NULL, category text NOT NULL, PRIMARY KEY (wallet, category)) ON COMMIT DROP;"))
        {
            createCategoryPairsCommand.Transaction = transaction;
            await createCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string captureExistingWalletsSql = """
INSERT INTO temp_position_refresh_wallets (wallet)
SELECT DISTINCT wallet
FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
ON CONFLICT (wallet) DO NOTHING;
""";

        await using (var captureWalletsCommand = CreateCommand(connection, captureExistingWalletsSql))
        {
            captureWalletsCommand.Transaction = transaction;
            captureWalletsCommand.CommandTimeout = 300;
            await captureWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string captureExistingCategoryPairsSql = """
INSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)
SELECT DISTINCT wallet, COALESCE(NULLIF(category, ''), 'unknown')
FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
ON CONFLICT (wallet, category) DO NOTHING;
""";

        await using (var captureCategoryPairsCommand = CreateCommand(connection, captureExistingCategoryPairsSql))
        {
            captureCategoryPairsCommand.Transaction = transaction;
            captureCategoryPairsCommand.CommandTimeout = 300;
            await captureCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string deleteSql = """
DELETE FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens);
""";

        await using (var deleteCommand = CreateCommand(connection, deleteSql))
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandTimeout = 300;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
INSERT INTO polymarket_onchain_wallet_positions (
    wallet, token_id, condition_id, market_id, market_slug, market_title, outcome,
    category, lookup_succeeded, market_resolved, winning_outcome,
    executions, buy_executions, sell_executions, buy_shares, sell_shares, net_shares,
    buy_notional_usd, sell_notional_usd, net_cost_usd, absolute_net_cost_usd,
    fees_usd, average_buy_price, average_sell_price, volume_usd, resolved_pnl_usd,
    position_status, first_trade_utc, last_trade_utc, latest_execution_imported_at_utc,
    metadata_refreshed_at_utc, refreshed_at_utc
)
WITH grouped AS (
    SELECT
        execution.wallet,
        execution.token_id,
        COALESCE(NULLIF(metadata.condition_id, ''), execution.token_id) AS condition_id,
        COALESCE(NULLIF(metadata.market_id, ''), '') AS market_id,
        COALESCE(NULLIF(metadata.market_slug, ''), '') AS market_slug,
        COALESCE(NULLIF(metadata.market_title, ''), 'Unenriched token ' || left(execution.token_id, 16)) AS market_title,
        COALESCE(NULLIF(metadata.outcome, ''), 'Unknown') AS outcome,
        metadata.category,
        COALESCE(metadata.lookup_succeeded, false) AS lookup_succeeded,
        COALESCE(metadata.resolved, false) AS market_resolved,
        metadata.winning_outcome,
        metadata.last_refreshed_utc AS metadata_refreshed_at_utc,
        COUNT(*)::integer AS executions,
        COUNT(*) FILTER (WHERE execution.side = 'Buy')::integer AS buy_executions,
        COUNT(*) FILTER (WHERE execution.side = 'Sell')::integer AS sell_executions,
        COALESCE(SUM(execution.size_shares) FILTER (WHERE execution.side = 'Buy'), 0)::numeric AS buy_shares,
        COALESCE(SUM(execution.size_shares) FILTER (WHERE execution.side = 'Sell'), 0)::numeric AS sell_shares,
        COALESCE(SUM(execution.notional_usd) FILTER (WHERE execution.side = 'Buy'), 0)::numeric AS buy_notional_usd,
        COALESCE(SUM(execution.notional_usd) FILTER (WHERE execution.side = 'Sell'), 0)::numeric AS sell_notional_usd,
        COALESCE(SUM(execution.fees_usd), 0)::numeric AS fees_usd,
        COALESCE(SUM(execution.notional_usd), 0)::numeric AS volume_usd,
        MIN(execution.block_timestamp_utc) AS first_trade_utc,
        MAX(execution.block_timestamp_utc) AS last_trade_utc,
        MAX(execution.imported_at_utc) AS latest_execution_imported_at_utc
    FROM polymarket_onchain_wallet_executions execution
    LEFT JOIN polymarket_onchain_token_metadata metadata
      ON metadata.token_id = execution.token_id
    WHERE execution.token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
    GROUP BY
        execution.wallet,
        execution.token_id,
        metadata.condition_id,
        metadata.market_id,
        metadata.market_slug,
        metadata.market_title,
        metadata.outcome,
        metadata.category,
        metadata.lookup_succeeded,
        metadata.resolved,
        metadata.winning_outcome,
        metadata.last_refreshed_utc
),
positions AS (
    SELECT
        grouped.*,
        (buy_shares - sell_shares)::numeric AS net_shares,
        (buy_notional_usd - sell_notional_usd + fees_usd)::numeric AS net_cost_usd,
        CASE WHEN buy_shares = 0 THEN 0 ELSE buy_notional_usd / buy_shares END AS average_buy_price,
        CASE WHEN sell_shares = 0 THEN 0 ELSE sell_notional_usd / sell_shares END AS average_sell_price
    FROM grouped
)
SELECT
    wallet,
    token_id,
    condition_id,
    market_id,
    market_slug,
    market_title,
    outcome,
    category,
    lookup_succeeded,
    market_resolved,
    winning_outcome,
    executions,
    buy_executions,
    sell_executions,
    buy_shares,
    sell_shares,
    net_shares,
    buy_notional_usd,
    sell_notional_usd,
    net_cost_usd,
    abs(net_cost_usd),
    fees_usd,
    average_buy_price,
    average_sell_price,
    volume_usd,
    CASE
        WHEN market_resolved AND winning_outcome IS NOT NULL
        THEN (CASE WHEN lower(outcome) = lower(winning_outcome) THEN net_shares ELSE 0 END) - net_cost_usd
        ELSE NULL::numeric
    END,
    CASE
        WHEN market_resolved THEN 'Resolved'
        WHEN abs(net_shares) < 0.00000001 THEN 'Flat'
        ELSE 'Open'
    END,
    first_trade_utc,
    last_trade_utc,
    latest_execution_imported_at_utc,
    metadata_refreshed_at_utc,
    now()
FROM positions;
""";

        int positionsUpserted;
        await using (var insertCommand = CreateCommand(connection, insertSql))
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandTimeout = 300;
            positionsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string pickWalletsSql = """
INSERT INTO temp_position_refresh_wallets (wallet)
SELECT DISTINCT wallet
FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
ON CONFLICT (wallet) DO NOTHING;
""";

        await using (var pickWalletsCommand = CreateCommand(connection, pickWalletsSql))
        {
            pickWalletsCommand.Transaction = transaction;
            pickWalletsCommand.CommandTimeout = 300;
            await pickWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string pickCategoryPairsSql = """
INSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)
SELECT DISTINCT wallet, COALESCE(NULLIF(category, ''), 'unknown')
FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
ON CONFLICT (wallet, category) DO NOTHING;
""";

        await using (var pickCategoryPairsCommand = CreateCommand(connection, pickCategoryPairsSql))
        {
            pickCategoryPairsCommand.Transaction = transaction;
            pickCategoryPairsCommand.CommandTimeout = 300;
            await pickCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(
            connection,
            transaction,
            "temp_position_refresh_wallets",
            cancellationToken);

        await QueuePolymarketOnChainWalletPerformanceRefreshForPositionTokensAsync(
            connection,
            transaction,
            "position_refresh",
            cancellationToken);

        await QueuePolymarketOnChainWalletCategoryPerformanceRefreshForPositionPairsAsync(
            connection,
            transaction,
            "position_refresh",
            cancellationToken);

        const string clearQueueSql = """
DELETE FROM polymarket_onchain_position_refresh_queue
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens);
""";

        await using (var clearCommand = CreateCommand(connection, clearQueueSql))
        {
            clearCommand.Transaction = transaction;
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OnChainPositionRefreshResult(tokensQueued, tokensProcessed, positionsUpserted, queueRemaining);
    }

    public async Task<IReadOnlyList<PolymarketOnChainWalletPerformance>> GetPolymarketOnChainWalletPerformanceAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT wallet, positions_count, open_positions, flat_positions, resolved_positions,
       profitable_resolved_positions, losing_resolved_positions, markets_traded,
       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,
       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,
       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc
FROM polymarket_onchain_wallet_performance
ORDER BY score DESC, resolved_pnl_usd DESC, volume_usd DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketOnChainWalletPerformance>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketOnChainWalletPerformance(reader));
        }

        return results;
    }

    public async Task<OnChainPerformanceRefreshResult> RefreshPolymarketOnChainWalletPerformanceAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken))
        {
            var remaining = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainPerformanceRefreshResult(0, 0, 0, remaining);
        }

        var walletsQueued = await SeedMissingPolymarketOnChainWalletPerformanceRefreshQueueAsync(
            connection,
            transaction,
            queueSeedWalletLimit,
            cancellationToken);

        await using (var createTemp = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_wallet_performance_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
        {
            createTemp.Transaction = transaction;
            await createTemp.ExecuteNonQueryAsync(cancellationToken);
        }

        const string pickSql = """
WITH picked AS (
    SELECT wallet
    FROM polymarket_onchain_wallet_performance_refresh_queue
    ORDER BY queued_at_utc
    LIMIT @WalletLimit
    FOR UPDATE SKIP LOCKED
)
INSERT INTO temp_wallet_performance_refresh_wallets (wallet)
SELECT wallet
FROM picked;
""";

        await using (var pickCommand = CreateCommand(connection, pickSql))
        {
            pickCommand.Transaction = transaction;
            pickCommand.Parameters.AddWithValue("WalletLimit", walletLimit);
            await pickCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var walletsProcessed = await CountTempWalletPerformanceRefreshWalletsAsync(connection, transaction, cancellationToken);
        if (walletsProcessed == 0)
        {
            var remaining = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainPerformanceRefreshResult(walletsQueued, 0, 0, remaining);
        }

        const string deleteSql = """
DELETE FROM polymarket_onchain_wallet_performance
WHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets);
""";

        await using (var deleteCommand = CreateCommand(connection, deleteSql))
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandTimeout = 300;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
INSERT INTO polymarket_onchain_wallet_performance (
    wallet, positions_count, open_positions, flat_positions, resolved_positions,
    profitable_resolved_positions, losing_resolved_positions, markets_traded,
    volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,
    resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,
    score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc
)
WITH metrics AS (
    SELECT
        wallet,
        COUNT(*)::integer AS positions_count,
        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,
        COUNT(*) FILTER (WHERE position_status = 'Flat')::integer AS flat_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved')::integer AS resolved_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,
        COUNT(DISTINCT condition_id)::integer AS markets_traded,
        COALESCE(SUM(volume_usd), 0)::numeric AS volume_usd,
        COALESCE(SUM(volume_usd) FILTER (WHERE position_status = 'Resolved'), 0)::numeric AS resolved_volume_usd,
        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_exposure_usd,
        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Resolved' AND resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,
        COALESCE(SUM(resolved_pnl_usd), 0)::numeric AS resolved_pnl_usd,
        COALESCE(AVG(abs(net_cost_usd)), 0)::numeric AS average_position_size_usd,
        MIN(first_trade_utc) AS first_active_utc,
        MAX(last_trade_utc) AS last_active_utc
    FROM polymarket_onchain_wallet_positions
    WHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets)
    GROUP BY wallet
),
scored AS (
    SELECT
        metrics.*,
        CASE WHEN resolved_cost_usd = 0 THEN 0 ELSE resolved_pnl_usd / resolved_cost_usd * 100 END AS resolved_roi_pct,
        CASE WHEN resolved_positions = 0 THEN 0 ELSE profitable_resolved_positions::numeric / resolved_positions * 100 END AS win_rate_pct
    FROM metrics
)
SELECT
    wallet,
    positions_count,
    open_positions,
    flat_positions,
    resolved_positions,
    profitable_resolved_positions,
    losing_resolved_positions,
    markets_traded,
    volume_usd,
    resolved_volume_usd,
    open_exposure_usd,
    resolved_cost_usd,
    resolved_pnl_usd,
    resolved_roi_pct,
    win_rate_pct,
    average_position_size_usd,
    (
        resolved_pnl_usd +
        resolved_roi_pct * 2 +
        profitable_resolved_positions * 5 +
        ln(volume_usd + 1) +
        LEAST(resolved_positions, 50) * 2 -
        open_exposure_usd * 0.02 -
        CASE WHEN resolved_positions < 5 THEN (5 - resolved_positions) * 10 ELSE 0 END
    )::numeric AS score,
    CASE
        WHEN resolved_positions >= 25 AND volume_usd >= 1000 THEN 'High'
        WHEN resolved_positions >= 10 THEN 'Medium'
        WHEN resolved_positions >= 3 THEN 'Low'
        ELSE 'Thin'
    END AS sample_quality,
    first_active_utc,
    last_active_utc,
    now()
FROM scored;
""";

        int walletsUpserted;
        await using (var insertCommand = CreateCommand(connection, insertSql))
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandTimeout = 300;
            walletsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(
            connection,
            transaction,
            "temp_wallet_performance_refresh_wallets",
            cancellationToken);

        const string clearQueueSql = """
DELETE FROM polymarket_onchain_wallet_performance_refresh_queue
WHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets);
""";

        await using (var clearCommand = CreateCommand(connection, clearQueueSql))
        {
            clearCommand.Transaction = transaction;
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OnChainPerformanceRefreshResult(walletsQueued, walletsProcessed, walletsUpserted, queueRemaining);
    }

    public async Task<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT wallet, category, positions_count, open_positions, flat_positions, resolved_positions,
       profitable_resolved_positions, losing_resolved_positions, markets_traded,
       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,
       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,
       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc
FROM polymarket_onchain_wallet_category_performance
WHERE @Category IS NULL OR category = @Category
ORDER BY score DESC, resolved_pnl_usd DESC, volume_usd DESC
LIMIT @Limit;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.Add("Category", NpgsqlDbType.Text).Value = (object?)category ?? DBNull.Value;
        command.Parameters.AddWithValue("Limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<PolymarketOnChainWalletCategoryPerformance>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadPolymarketOnChainWalletCategoryPerformance(reader));
        }

        return results;
    }

    public async Task<PolymarketOnChainWalletCategoryPerformance?> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string wallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(wallet) || string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        const string sql = """
SELECT wallet, category, positions_count, open_positions, flat_positions, resolved_positions,
       profitable_resolved_positions, losing_resolved_positions, markets_traded,
       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,
       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,
       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc
FROM polymarket_onchain_wallet_category_performance
WHERE wallet = @Wallet
  AND category = @Category
LIMIT 1;
""";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("Category", category);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadPolymarketOnChainWalletCategoryPerformance(reader)
            : null;
    }

    public async Task<OnChainCategoryPerformanceRefreshResult> RefreshPolymarketOnChainWalletCategoryPerformanceAsync(
        int pairLimit = 500,
        int queueSeedPairLimit = 1_000,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken))
        {
            var remaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainCategoryPerformanceRefreshResult(0, 0, 0, remaining);
        }

        var pairsQueued = await SeedMissingPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(
            connection,
            transaction,
            queueSeedPairLimit,
            cancellationToken);

        await using (var createTemp = CreateCommand(
            connection,
            "CREATE TEMP TABLE temp_wallet_category_performance_refresh_pairs (wallet text NOT NULL, category text NOT NULL, PRIMARY KEY (wallet, category)) ON COMMIT DROP;"))
        {
            createTemp.Transaction = transaction;
            await createTemp.ExecuteNonQueryAsync(cancellationToken);
        }

        const string pickSql = """
WITH picked AS (
    SELECT wallet, category
    FROM polymarket_onchain_wallet_category_performance_refresh_queue
    ORDER BY queued_at_utc, category, wallet
    LIMIT @PairLimit
    FOR UPDATE SKIP LOCKED
)
INSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)
SELECT wallet, category
FROM picked
ON CONFLICT (wallet, category) DO NOTHING;
""";

        await using (var pickCommand = CreateCommand(connection, pickSql))
        {
            pickCommand.Transaction = transaction;
            pickCommand.Parameters.AddWithValue("PairLimit", pairLimit);
            await pickCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var pairsProcessed = await CountTempWalletCategoryPerformanceRefreshPairsAsync(connection, transaction, cancellationToken);
        if (pairsProcessed == 0)
        {
            var remaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OnChainCategoryPerformanceRefreshResult(pairsQueued, 0, 0, remaining);
        }

        const string deleteSql = """
DELETE FROM polymarket_onchain_wallet_category_performance performance
USING temp_wallet_category_performance_refresh_pairs pair
WHERE performance.wallet = pair.wallet
  AND performance.category = pair.category;
""";

        await using (var deleteCommand = CreateCommand(connection, deleteSql))
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandTimeout = 300;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
INSERT INTO polymarket_onchain_wallet_category_performance (
    wallet, category, positions_count, open_positions, flat_positions, resolved_positions,
    profitable_resolved_positions, losing_resolved_positions, markets_traded,
    volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,
    resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,
    score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc
)
WITH metrics AS (
    SELECT
        position.wallet,
        COALESCE(NULLIF(position.category, ''), 'unknown') AS category,
        COUNT(*)::integer AS positions_count,
        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,
        COUNT(*) FILTER (WHERE position_status = 'Flat')::integer AS flat_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved')::integer AS resolved_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,
        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,
        COUNT(DISTINCT condition_id)::integer AS markets_traded,
        COALESCE(SUM(volume_usd), 0)::numeric AS volume_usd,
        COALESCE(SUM(volume_usd) FILTER (WHERE position_status = 'Resolved'), 0)::numeric AS resolved_volume_usd,
        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_exposure_usd,
        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Resolved' AND resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,
        COALESCE(SUM(resolved_pnl_usd), 0)::numeric AS resolved_pnl_usd,
        COALESCE(AVG(abs(net_cost_usd)), 0)::numeric AS average_position_size_usd,
        MIN(first_trade_utc) AS first_active_utc,
        MAX(last_trade_utc) AS last_active_utc
    FROM polymarket_onchain_wallet_positions position
    WHERE EXISTS (
        SELECT 1
        FROM temp_wallet_category_performance_refresh_pairs pair
        WHERE pair.wallet = position.wallet
          AND pair.category = COALESCE(NULLIF(position.category, ''), 'unknown')
    )
    GROUP BY position.wallet, COALESCE(NULLIF(position.category, ''), 'unknown')
),
scored AS (
    SELECT
        metrics.*,
        CASE WHEN resolved_cost_usd = 0 THEN 0 ELSE resolved_pnl_usd / resolved_cost_usd * 100 END AS resolved_roi_pct,
        CASE WHEN resolved_positions = 0 THEN 0 ELSE profitable_resolved_positions::numeric / resolved_positions * 100 END AS win_rate_pct
    FROM metrics
)
SELECT
    wallet,
    category,
    positions_count,
    open_positions,
    flat_positions,
    resolved_positions,
    profitable_resolved_positions,
    losing_resolved_positions,
    markets_traded,
    volume_usd,
    resolved_volume_usd,
    open_exposure_usd,
    resolved_cost_usd,
    resolved_pnl_usd,
    resolved_roi_pct,
    win_rate_pct,
    average_position_size_usd,
    (
        resolved_pnl_usd +
        resolved_roi_pct * 2 +
        profitable_resolved_positions * 5 +
        ln(volume_usd + 1) +
        LEAST(resolved_positions, 50) * 2 -
        open_exposure_usd * 0.02 -
        CASE WHEN resolved_positions < 5 THEN (5 - resolved_positions) * 10 ELSE 0 END
    )::numeric AS score,
    CASE
        WHEN resolved_positions >= 25 AND volume_usd >= 1000 THEN 'High'
        WHEN resolved_positions >= 10 THEN 'Medium'
        WHEN resolved_positions >= 3 THEN 'Low'
        ELSE 'Thin'
    END AS sample_quality,
    first_active_utc,
    last_active_utc,
    now()
FROM scored;
""";

        int pairsUpserted;
        await using (var insertCommand = CreateCommand(connection, insertSql))
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandTimeout = 300;
            pairsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        const string clearQueueSql = """
DELETE FROM polymarket_onchain_wallet_category_performance_refresh_queue queue
USING temp_wallet_category_performance_refresh_pairs pair
WHERE queue.wallet = pair.wallet
  AND queue.category = pair.category;
""";

        await using (var clearCommand = CreateCommand(connection, clearQueueSql))
        {
            clearCommand.Transaction = transaction;
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new OnChainCategoryPerformanceRefreshResult(pairsQueued, pairsProcessed, pairsUpserted, queueRemaining);
    }

    public async Task<IReadOnlyList<PolymarketOnChainTradeDetails>> GetRecentPolymarketOnChainTradeDetailsAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,
       transaction_hash, log_index, order_hash, maker, taker, maker_side, taker_side,
       token_id, maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw,
       maker_amount, taker_amount, price, size_shares, notional_usd, fee_amount,
       fee_asset_id, builder, order_metadata, condition_id, market_id, market_slug,
       market_title, outcome, category, lookup_succeeded, market_active, market_closed,
       market_archived, market_resolved, winning_outcome, imported_at_utc
FROM polymarket_onchain_trade_details
ORDER BY block_timestamp_utc DESC, block_number DESC, log_index DESC
LIMIT @Limit;
""";

        var results = new List<PolymarketOnChainTradeDetails>();
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("Limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadPolymarketOnChainTradeDetails(reader));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return results;
        }

        return results;
    }

    public async Task<IReadOnlyList<PolymarketOnChainParticipantDetails>> GetPolymarketOnChainParticipantDetailsAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
SELECT wallet, executions, buy_executions, sell_executions, markets_traded,
       volume_usd, average_trade_usd, fees_usd, activity_score,
       positions_count, open_positions, flat_positions, resolved_positions,
       profitable_resolved_positions, losing_resolved_positions, open_exposure_usd,
       resolved_cost_usd, resolved_pnl_usd, resolved_roi_pct, win_rate_pct,
       average_position_size_usd, score, sample_quality, first_trade_utc,
       last_trade_utc, activity_refreshed_at_utc, performance_refreshed_at_utc
FROM polymarket_onchain_participant_details
ORDER BY score DESC, volume_usd DESC, last_trade_utc DESC
LIMIT @Limit;
""";

        var results = new List<PolymarketOnChainParticipantDetails>();
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("Limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(ReadPolymarketOnChainParticipantDetails(reader));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return results;
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

    private static async Task<bool> TryAcquireOnChainDerivedRefreshLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT pg_try_advisory_xact_lock(@LockKey1, @LockKey2);");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("LockKey1", OnChainDerivedRefreshLockKey1);
        command.Parameters.AddWithValue("LockKey2", OnChainDerivedRefreshLockKey2);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool acquired && acquired;
    }

    private static DateTime UtcDateTime(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime;
    }

    private static string NormalizeContractAddress(string contractAddress)
    {
        return contractAddress.Trim().ToLowerInvariant();
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

    private static void AddPolymarketHttpLogParameters(NpgsqlCommand command, PolymarketHttpLogEntry entry)
    {
        command.Parameters.AddWithValue("Id", entry.Id);
        command.Parameters.AddWithValue("Component", entry.Component);
        command.Parameters.AddWithValue("Operation", entry.Operation);
        command.Parameters.AddWithValue("HttpMethod", entry.HttpMethod);
        command.Parameters.AddWithValue("RequestUrl", entry.RequestUrl);
        command.Parameters.AddWithValue("RequestedAtUtc", UtcDateTime(entry.RequestedAtUtc));
        command.Parameters.AddWithValue("ResponseAtUtc", entry.ResponseAtUtc is { } responseAt ? UtcDateTime(responseAt) : DBNull.Value);
        command.Parameters.AddWithValue("DurationMs", entry.DurationMilliseconds);
        command.Parameters.AddWithValue("Attempt", entry.Attempt);
        command.Parameters.AddWithValue("StatusCode", entry.StatusCode is { } statusCode ? statusCode : DBNull.Value);
        command.Parameters.AddWithValue("Succeeded", entry.Succeeded);
        command.Parameters.AddWithValue("ResponseBody", entry.ResponseBody);
        command.Parameters.AddWithValue("ErrorMessage", (object?)entry.ErrorMessage ?? DBNull.Value);
    }

    private static PolymarketHttpLogEntry ReadPolymarketHttpLogEntry(NpgsqlDataReader reader)
    {
        return new PolymarketHttpLogEntry(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffsetFromUtc(reader.GetDateTime(5)),
            reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
            reader.GetInt64(7),
            reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.GetBoolean(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static void AddPolymarketOnChainLogParameters(NpgsqlCommand command, PolymarketOnChainLog log)
    {
        command.Parameters.AddWithValue("Id", log.Id);
        command.Parameters.AddWithValue("ContractName", log.ContractName);
        command.Parameters.AddWithValue("ContractAddress", log.ContractAddress);
        command.Parameters.AddWithValue("ExchangeVersion", log.ExchangeVersion);
        command.Parameters.AddWithValue("BlockNumber", log.BlockNumber);
        command.Parameters.AddWithValue("BlockHash", log.BlockHash);
        command.Parameters.AddWithValue("TransactionHash", log.TransactionHash);
        command.Parameters.AddWithValue("TransactionIndex", log.TransactionIndex);
        command.Parameters.AddWithValue("LogIndex", log.LogIndex);
        command.Parameters.AddWithValue("Topic0", log.Topic0);
        command.Parameters.AddWithValue("TopicsJson", JsonSerializer.Serialize(log.Topics));
        command.Parameters.AddWithValue("Data", log.Data);
        command.Parameters.AddWithValue("Removed", log.Removed);
        command.Parameters.AddWithValue("ObservedAtUtc", UtcDateTime(log.ObservedAtUtc));
    }

    private static void AddPolymarketOnChainFillParameters(NpgsqlCommand command, PolymarketOnChainFill fill)
    {
        command.Parameters.AddWithValue("Id", fill.Id);
        command.Parameters.AddWithValue("ContractName", fill.ContractName);
        command.Parameters.AddWithValue("ContractAddress", fill.ContractAddress);
        command.Parameters.AddWithValue("ExchangeVersion", fill.ExchangeVersion);
        command.Parameters.AddWithValue("BlockNumber", fill.BlockNumber);
        command.Parameters.AddWithValue("BlockTimestampUtc", UtcDateTime(fill.BlockTimestampUtc));
        command.Parameters.AddWithValue("TransactionHash", fill.TransactionHash);
        command.Parameters.AddWithValue("LogIndex", fill.LogIndex);
        command.Parameters.AddWithValue("OrderHash", fill.OrderHash);
        command.Parameters.AddWithValue("Maker", fill.Maker);
        command.Parameters.AddWithValue("Taker", fill.Taker);
        command.Parameters.AddWithValue("Wallet", fill.Wallet);
        command.Parameters.AddWithValue("Side", fill.Side.ToString());
        command.Parameters.AddWithValue("TokenId", fill.TokenId);
        command.Parameters.AddWithValue("MakerAssetId", fill.MakerAssetId);
        command.Parameters.AddWithValue("TakerAssetId", fill.TakerAssetId);
        command.Parameters.AddWithValue("MakerAmountRaw", fill.MakerAmountRaw);
        command.Parameters.AddWithValue("TakerAmountRaw", fill.TakerAmountRaw);
        command.Parameters.AddWithValue("MakerAmount", fill.MakerAmount);
        command.Parameters.AddWithValue("TakerAmount", fill.TakerAmount);
        command.Parameters.AddWithValue("Price", fill.Price);
        command.Parameters.AddWithValue("SizeShares", fill.SizeShares);
        command.Parameters.AddWithValue("NotionalUsd", fill.NotionalUsd);
        command.Parameters.AddWithValue("FeeRaw", fill.FeeRaw);
        command.Parameters.AddWithValue("FeeAmount", fill.FeeAmount);
        command.Parameters.AddWithValue("FeeAssetId", fill.FeeAssetId);
        command.Parameters.AddWithValue("Builder", (object?)fill.Builder ?? DBNull.Value);
        command.Parameters.AddWithValue("Metadata", (object?)fill.Metadata ?? DBNull.Value);
        command.Parameters.AddWithValue("ImportedAtUtc", UtcDateTime(fill.ImportedAtUtc));
    }

    private static async Task QueuePolymarketOnChainPositionRefreshTokensAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<string> tokenIds,
        string reason,
        CancellationToken cancellationToken)
    {
        var distinctTokenIds = tokenIds
            .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctTokenIds.Length == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)
SELECT input.token_id, @Reason, now()
FROM unnest(@TokenIds) AS input(token_id)
WHERE EXISTS (
    SELECT 1
    FROM polymarket_onchain_wallet_executions execution
    WHERE execution.token_id = input.token_id
    LIMIT 1
)
ON CONFLICT (token_id) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainTokenMetadataRefreshTokensAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<string> tokenIds,
        string reason,
        CancellationToken cancellationToken)
    {
        var distinctTokenIds = tokenIds
            .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctTokenIds.Length == 0)
        {
            return;
        }

        const string sql = """
INSERT INTO polymarket_onchain_token_metadata_refresh_queue (
    token_id, reason, attempts, queued_at_utc, next_attempt_at_utc
)
SELECT unnest(@TokenIds), @Reason, 0, now(), now()
ON CONFLICT (token_id) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainTokenMetadataRefreshTokensForRangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_token_metadata_refresh_queue (
    token_id, reason, attempts, queued_at_utc, next_attempt_at_utc
)
SELECT DISTINCT execution.token_id, @Reason, 0, now(), now()
FROM polymarket_onchain_wallet_fills execution
LEFT JOIN polymarket_onchain_token_metadata metadata
  ON metadata.token_id = execution.token_id
WHERE execution.contract_address = @ContractAddress
  AND execution.block_number BETWEEN @FromBlock AND @ToBlock
  AND (
      metadata.token_id IS NULL
      OR NOT metadata.lookup_succeeded
      OR NULLIF(metadata.category, '') IS NULL
  )
ON CONFLICT (token_id) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
DELETE FROM polymarket_onchain_token_metadata_refresh_queue refresh_queue
USING polymarket_onchain_token_metadata metadata
WHERE metadata.token_id = refresh_queue.token_id
  AND metadata.lookup_succeeded
  AND NULLIF(metadata.category, '') IS NOT NULL;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RescheduleIncompletePolymarketOnChainTokenMetadataRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<string> tokenIds,
        CancellationToken cancellationToken)
    {
        var distinctTokenIds = tokenIds
            .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctTokenIds.Length == 0)
        {
            return;
        }

        const string sql = """
UPDATE polymarket_onchain_token_metadata_refresh_queue refresh_queue
SET
    reason = 'metadata_retry',
    attempts = refresh_queue.attempts + 1,
    last_attempted_at_utc = now(),
    next_attempt_at_utc = now() + (LEAST((refresh_queue.attempts + 1) * 5, 60)::text || ' minutes')::interval,
    last_error = COALESCE(
        metadata.lookup_error,
        CASE
            WHEN NULLIF(metadata.category, '') IS NULL THEN 'Metadata category is missing.'
            ELSE NULL
        END)
FROM polymarket_onchain_token_metadata metadata
WHERE metadata.token_id = refresh_queue.token_id
  AND refresh_queue.token_id = ANY(@TokenIds)
  AND (
      NOT metadata.lookup_succeeded
      OR NULLIF(metadata.category, '') IS NULL
  );
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainPositionRefreshTokensForRangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)
SELECT DISTINCT token_id, @Reason, now()
FROM polymarket_onchain_wallet_fills
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock
ON CONFLICT (token_id) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string contractAddress,
        long fromBlock,
        long toBlock,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)
SELECT DISTINCT wallet, @Reason, now()
FROM polymarket_onchain_wallet_fills
WHERE contract_address = @ContractAddress
  AND block_number BETWEEN @FromBlock AND @ToBlock
ON CONFLICT (wallet) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
        command.Parameters.AddWithValue("FromBlock", fromBlock);
        command.Parameters.AddWithValue("ToBlock", toBlock);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> SeedMissingPolymarketOnChainPositionRefreshTokensAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int tokenLimit,
        CancellationToken cancellationToken)
    {
        var initialBackfillComplete = await GetBotSettingAsync(
            connection,
            transaction,
            "onchain_positions_initial_backfill_complete",
            cancellationToken);
        var positionsEmpty = await IsPolymarketOnChainPositionsEmptyAsync(connection, transaction, cancellationToken);
        if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !positionsEmpty)
        {
            return 0;
        }

        const string sql = """
INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)
SELECT missing.token_id, 'missing_position', now()
FROM (
    SELECT DISTINCT execution.token_id
    FROM polymarket_onchain_wallet_executions execution
    WHERE NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_positions position
        WHERE position.token_id = execution.token_id
    )
    ORDER BY execution.token_id
    LIMIT @TokenLimit
) missing
ON CONFLICT (token_id) DO NOTHING;
""";

        int queued;
        await using (var command = CreateCommand(connection, sql))
        {
            command.Transaction = transaction;
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("TokenLimit", tokenLimit);
            queued = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (queued == 0)
        {
            await UpsertBotSettingAsync(
                connection,
                transaction,
                "onchain_positions_initial_backfill_complete",
                "true",
                cancellationToken);
        }
        else
        {
            await UpsertBotSettingAsync(
                connection,
                transaction,
                "onchain_positions_initial_backfill_complete",
                "false",
                cancellationToken);
        }

        return queued;
    }

    private static async Task<bool> IsPolymarketOnChainPositionsEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_positions LIMIT 1);");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool empty && empty;
    }

    private static async Task<int> CountTempPositionRefreshTokensAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM temp_position_refresh_tokens;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> CountPolymarketOnChainPositionRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_position_refresh_queue;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> SeedMissingPolymarketOnChainWalletActivityRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int walletLimit,
        CancellationToken cancellationToken)
    {
        var initialBackfillComplete = await GetBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_activity_initial_backfill_complete",
            cancellationToken);
        var activityEmpty = await IsPolymarketOnChainWalletActivityEmptyAsync(connection, transaction, cancellationToken);
        if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !activityEmpty)
        {
            return 0;
        }

        const string sql = """
INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)
SELECT missing.wallet, 'missing_activity', now()
FROM (
    SELECT DISTINCT fills.wallet
    FROM polymarket_onchain_wallet_fills fills
    WHERE NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_activity activity
        WHERE activity.wallet = fills.wallet
    )
      AND NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_activity_refresh_queue queued_wallet
        WHERE queued_wallet.wallet = fills.wallet
    )
    ORDER BY fills.wallet
    LIMIT @WalletLimit
) missing
ON CONFLICT (wallet) DO NOTHING;
""";

        int queued;
        await using (var command = CreateCommand(connection, sql))
        {
            command.Transaction = transaction;
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("WalletLimit", walletLimit);
            queued = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
        await UpsertBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_activity_initial_backfill_complete",
            queued == 0 && queueRemaining == 0 ? "true" : "false",
            cancellationToken);

        return queued;
    }

    private static async Task<bool> IsPolymarketOnChainWalletActivityEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_activity LIMIT 1);");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool empty && empty;
    }

    private static async Task<int> CountTempWalletActivityRefreshWalletsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_activity_refresh_wallets;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> CountPolymarketOnChainWalletActivityRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_activity_refresh_queue;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> SeedMissingPolymarketOnChainParticipantDetailsRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int walletLimit,
        CancellationToken cancellationToken)
    {
        var initialBackfillComplete = await GetBotSettingAsync(
            connection,
            transaction,
            "onchain_participant_details_initial_backfill_complete",
            cancellationToken);
        var participantDetailsEmpty = await IsPolymarketOnChainParticipantDetailsEmptyAsync(
            connection,
            transaction,
            cancellationToken);
        if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !participantDetailsEmpty)
        {
            return 0;
        }

        const string sql = """
INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)
SELECT missing.wallet, 'missing_participant_details', now()
FROM (
    SELECT activity.wallet
    FROM polymarket_onchain_wallet_activity activity
    WHERE NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_participant_details participant
        WHERE lower(participant.wallet) = lower(activity.wallet)
    )
      AND NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_activity_refresh_queue queued_wallet
        WHERE lower(queued_wallet.wallet) = lower(activity.wallet)
    )
    ORDER BY activity.wallet
    LIMIT @WalletLimit
) missing
ON CONFLICT (wallet) DO NOTHING;
""";

        int queued;
        await using (var command = CreateCommand(connection, sql))
        {
            command.Transaction = transaction;
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("WalletLimit", walletLimit);
            queued = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
        await UpsertBotSettingAsync(
            connection,
            transaction,
            "onchain_participant_details_initial_backfill_complete",
            queued == 0 && queueRemaining == 0 ? "true" : "false",
            cancellationToken);

        return queued;
    }

    private static async Task<bool> IsPolymarketOnChainParticipantDetailsEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_participant_details LIMIT 1);");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool empty && empty;
    }

    private static async Task UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string walletSourceTable,
        CancellationToken cancellationToken)
    {
        var walletSourceSql = walletSourceTable switch
        {
            "temp_wallet_activity_refresh_wallets" => "SELECT wallet FROM temp_wallet_activity_refresh_wallets",
            "temp_position_refresh_wallets" => "SELECT wallet FROM temp_position_refresh_wallets",
            "temp_wallet_performance_refresh_wallets" => "SELECT wallet FROM temp_wallet_performance_refresh_wallets",
            _ => throw new ArgumentOutOfRangeException(nameof(walletSourceTable), walletSourceTable, "Unsupported wallet source table.")
        };

        var sql = $"""
DELETE FROM polymarket_onchain_participant_details
WHERE wallet IN ({walletSourceSql});

INSERT INTO polymarket_onchain_participant_details (
    wallet,
    executions,
    buy_executions,
    sell_executions,
    markets_traded,
    volume_usd,
    average_trade_usd,
    fees_usd,
    activity_score,
    positions_count,
    open_positions,
    flat_positions,
    resolved_positions,
    profitable_resolved_positions,
    losing_resolved_positions,
    open_exposure_usd,
    resolved_cost_usd,
    resolved_pnl_usd,
    resolved_roi_pct,
    win_rate_pct,
    average_position_size_usd,
    score,
    sample_quality,
    first_trade_utc,
    last_trade_utc,
    activity_refreshed_at_utc,
    performance_refreshed_at_utc,
    refreshed_at_utc
)
WITH position_stats AS (
    SELECT
        position.wallet,
        COUNT(*)::integer AS positions_count,
        COUNT(*) FILTER (WHERE position.position_status = 'Open')::integer AS open_positions,
        COUNT(*) FILTER (WHERE position.position_status = 'Flat')::integer AS flat_positions,
        COUNT(*) FILTER (WHERE position.position_status = 'Resolved')::integer AS resolved_positions,
        COUNT(*) FILTER (WHERE position.position_status = 'Resolved' AND COALESCE(position.resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,
        COUNT(*) FILTER (WHERE position.position_status = 'Resolved' AND COALESCE(position.resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,
        COALESCE(SUM(abs(position.net_cost_usd)) FILTER (WHERE position.position_status = 'Open'), 0)::numeric AS open_exposure_usd,
        COALESCE(SUM(abs(position.net_cost_usd)) FILTER (WHERE position.position_status = 'Resolved' AND position.resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,
        COALESCE(SUM(position.resolved_pnl_usd) FILTER (WHERE position.resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_pnl_usd
    FROM polymarket_onchain_wallet_positions position
    WHERE position.wallet IN ({walletSourceSql})
    GROUP BY position.wallet
)
SELECT
    activity.wallet,
    activity.executions,
    activity.buy_executions,
    activity.sell_executions,
    activity.markets_traded,
    activity.volume_usd,
    activity.average_trade_usd,
    activity.fees_usd,
    activity.activity_score,
    COALESCE(performance.positions_count, position_stats.positions_count, 0),
    COALESCE(performance.open_positions, position_stats.open_positions, 0),
    COALESCE(performance.flat_positions, position_stats.flat_positions, 0),
    COALESCE(performance.resolved_positions, position_stats.resolved_positions, 0),
    COALESCE(performance.profitable_resolved_positions, position_stats.profitable_resolved_positions, 0),
    COALESCE(performance.losing_resolved_positions, position_stats.losing_resolved_positions, 0),
    COALESCE(performance.open_exposure_usd, position_stats.open_exposure_usd, 0),
    COALESCE(performance.resolved_cost_usd, position_stats.resolved_cost_usd, 0),
    COALESCE(performance.resolved_pnl_usd, position_stats.resolved_pnl_usd, 0),
    COALESCE(performance.resolved_roi_pct, 0),
    COALESCE(performance.win_rate_pct, 0),
    COALESCE(performance.average_position_size_usd, 0),
    COALESCE(performance.score, activity.activity_score),
    COALESCE(performance.sample_quality, 'ActivityOnly'),
    activity.first_trade_utc,
    activity.last_trade_utc,
    activity.refreshed_at_utc,
    performance.refreshed_at_utc,
    now()
FROM polymarket_onchain_wallet_activity activity
LEFT JOIN polymarket_onchain_wallet_performance performance
       ON lower(performance.wallet) = lower(activity.wallet)
LEFT JOIN position_stats
       ON lower(position_stats.wallet) = lower(activity.wallet)
WHERE activity.wallet IN ({walletSourceSql})
ON CONFLICT (wallet) DO UPDATE SET
    executions = excluded.executions,
    buy_executions = excluded.buy_executions,
    sell_executions = excluded.sell_executions,
    markets_traded = excluded.markets_traded,
    volume_usd = excluded.volume_usd,
    average_trade_usd = excluded.average_trade_usd,
    fees_usd = excluded.fees_usd,
    activity_score = excluded.activity_score,
    positions_count = excluded.positions_count,
    open_positions = excluded.open_positions,
    flat_positions = excluded.flat_positions,
    resolved_positions = excluded.resolved_positions,
    profitable_resolved_positions = excluded.profitable_resolved_positions,
    losing_resolved_positions = excluded.losing_resolved_positions,
    open_exposure_usd = excluded.open_exposure_usd,
    resolved_cost_usd = excluded.resolved_cost_usd,
    resolved_pnl_usd = excluded.resolved_pnl_usd,
    resolved_roi_pct = excluded.resolved_roi_pct,
    win_rate_pct = excluded.win_rate_pct,
    average_position_size_usd = excluded.average_position_size_usd,
    score = excluded.score,
    sample_quality = excluded.sample_quality,
    first_trade_utc = excluded.first_trade_utc,
    last_trade_utc = excluded.last_trade_utc,
    activity_refreshed_at_utc = excluded.activity_refreshed_at_utc,
    performance_refreshed_at_utc = excluded.performance_refreshed_at_utc,
    refreshed_at_utc = excluded.refreshed_at_utc;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainWalletPerformanceRefreshForPositionTokensAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_wallet_performance_refresh_queue (wallet, reason, queued_at_utc)
SELECT DISTINCT wallet, @Reason, now()
FROM polymarket_onchain_wallet_positions
WHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)
ON CONFLICT (wallet) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task QueuePolymarketOnChainWalletCategoryPerformanceRefreshForPositionPairsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string reason,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO polymarket_onchain_wallet_category_performance_refresh_queue (wallet, category, reason, queued_at_utc)
SELECT wallet, category, @Reason, now()
FROM temp_wallet_category_performance_refresh_pairs
ON CONFLICT (wallet, category) DO NOTHING;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> SeedMissingPolymarketOnChainWalletPerformanceRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int walletLimit,
        CancellationToken cancellationToken)
    {
        var initialBackfillComplete = await GetBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_performance_initial_backfill_complete",
            cancellationToken);
        var performanceEmpty = await IsPolymarketOnChainWalletPerformanceEmptyAsync(connection, transaction, cancellationToken);
        if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !performanceEmpty)
        {
            return 0;
        }

        const string sql = """
INSERT INTO polymarket_onchain_wallet_performance_refresh_queue (wallet, reason, queued_at_utc)
SELECT missing.wallet, 'missing_performance', now()
FROM (
    SELECT DISTINCT position.wallet
    FROM polymarket_onchain_wallet_positions position
    WHERE NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_performance performance
        WHERE performance.wallet = position.wallet
    )
    ORDER BY position.wallet
    LIMIT @WalletLimit
) missing
ON CONFLICT (wallet) DO NOTHING;
""";

        int queued;
        await using (var command = CreateCommand(connection, sql))
        {
            command.Transaction = transaction;
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("WalletLimit", walletLimit);
            queued = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await UpsertBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_performance_initial_backfill_complete",
            queued == 0 ? "true" : "false",
            cancellationToken);

        return queued;
    }

    private static async Task<int> SeedMissingPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int pairLimit,
        CancellationToken cancellationToken)
    {
        var initialBackfillComplete = await GetBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_category_performance_initial_backfill_complete",
            cancellationToken);
        var categoryPerformanceEmpty = await IsPolymarketOnChainWalletCategoryPerformanceEmptyAsync(connection, transaction, cancellationToken);
        if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !categoryPerformanceEmpty)
        {
            return 0;
        }

        const string sql = """
INSERT INTO polymarket_onchain_wallet_category_performance_refresh_queue (wallet, category, reason, queued_at_utc)
SELECT missing.wallet, missing.category, 'missing_category_performance', now()
FROM (
    SELECT DISTINCT position.wallet, COALESCE(NULLIF(position.category, ''), 'unknown') AS category
    FROM polymarket_onchain_wallet_positions position
    WHERE NOT EXISTS (
        SELECT 1
        FROM polymarket_onchain_wallet_category_performance performance
        WHERE performance.wallet = position.wallet
          AND performance.category = COALESCE(NULLIF(position.category, ''), 'unknown')
    )
    ORDER BY category, position.wallet
    LIMIT @PairLimit
) missing
ON CONFLICT (wallet, category) DO NOTHING;
""";

        int queued;
        await using (var command = CreateCommand(connection, sql))
        {
            command.Transaction = transaction;
            command.CommandTimeout = 300;
            command.Parameters.AddWithValue("PairLimit", pairLimit);
            queued = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueRemaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
        await UpsertBotSettingAsync(
            connection,
            transaction,
            "onchain_wallet_category_performance_initial_backfill_complete",
            queued == 0 && queueRemaining == 0 ? "true" : "false",
            cancellationToken);

        return queued;
    }

    private static async Task<bool> IsPolymarketOnChainWalletPerformanceEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_performance LIMIT 1);");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool empty && empty;
    }

    private static async Task<bool> IsPolymarketOnChainWalletCategoryPerformanceEmptyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_category_performance LIMIT 1);");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool empty && empty;
    }

    private static async Task<int> CountTempWalletPerformanceRefreshWalletsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_performance_refresh_wallets;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> CountTempWalletCategoryPerformanceRefreshPairsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_category_performance_refresh_pairs;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_performance_refresh_queue;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<int> CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_category_performance_refresh_queue;");
        command.Transaction = transaction;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? checked((int)count) : 0;
    }

    private static async Task<string?> GetBotSettingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT value FROM bot_settings WHERE key = @Key;");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Key", key);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string value ? value : null;
    }

    private static async Task UpsertBotSettingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        const string sql = """
INSERT INTO bot_settings (key, value, updated_at_utc)
VALUES (@Key, @Value, now())
ON CONFLICT (key) DO UPDATE SET
    value = excluded.value,
    updated_at_utc = excluded.updated_at_utc;
""";

        await using var command = CreateCommand(connection, sql);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Key", key);
        command.Parameters.AddWithValue("Value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddPolymarketOnChainTokenMetadataParameters(NpgsqlCommand command, PolymarketOnChainTokenMetadata metadata)
    {
        command.Parameters.AddWithValue("TokenId", metadata.TokenId);
        command.Parameters.AddWithValue("ConditionId", metadata.ConditionId);
        command.Parameters.AddWithValue("MarketId", metadata.MarketId);
        command.Parameters.AddWithValue("MarketSlug", metadata.MarketSlug);
        command.Parameters.AddWithValue("MarketTitle", metadata.MarketTitle);
        command.Parameters.AddWithValue("Outcome", metadata.Outcome);
        command.Parameters.AddWithValue("OutcomeIndex", metadata.OutcomeIndex);
        command.Parameters.AddWithValue("Category", (object?)metadata.Category ?? DBNull.Value);
        command.Parameters.AddWithValue("EndDateUtc", metadata.EndDateUtc is { } endDate ? UtcDateTime(endDate) : DBNull.Value);
        command.Parameters.AddWithValue("Active", metadata.Active);
        command.Parameters.AddWithValue("Closed", metadata.Closed);
        command.Parameters.AddWithValue("Archived", metadata.Archived);
        command.Parameters.AddWithValue("Resolved", metadata.Resolved);
        command.Parameters.AddWithValue("WinningOutcome", (object?)metadata.WinningOutcome ?? DBNull.Value);
        command.Parameters.AddWithValue("ClobTokenIdsJson", JsonSerializer.Serialize(metadata.ClobTokenIds));
        command.Parameters.AddWithValue("OutcomesJson", JsonSerializer.Serialize(metadata.Outcomes));
        command.Parameters.AddWithValue("LookupSucceeded", metadata.LookupSucceeded);
        command.Parameters.AddWithValue("LookupError", (object?)metadata.LookupError ?? DBNull.Value);
        command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(metadata.RawJson) ? "{}" : metadata.RawJson);
        command.Parameters.AddWithValue("LastRefreshedUtc", UtcDateTime(metadata.LastRefreshedUtc));
    }

    private static PolymarketOnChainTokenMetadata ReadPolymarketOnChainTokenMetadata(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainTokenMetadata(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(8)),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetBoolean(11),
            reader.GetBoolean(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            ReadJsonStringArray(reader, 14),
            ReadJsonStringArray(reader, 15),
            reader.GetBoolean(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetString(18),
            DateTimeOffsetFromUtc(reader.GetDateTime(19)));
    }

    private static IReadOnlyList<string> ReadJsonStringArray(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return [];
        }

        return JsonSerializer.Deserialize<string[]>(reader.GetString(ordinal)) ?? [];
    }

    private static PolymarketOnChainFill ReadPolymarketOnChainFill(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainFill(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            DateTimeOffsetFromUtc(reader.GetDateTime(5)),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            Enum.Parse<TradeSide>(reader.GetString(12)),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetDecimal(18),
            reader.GetDecimal(19),
            reader.GetDecimal(20),
            reader.GetDecimal(21),
            reader.GetDecimal(22),
            reader.GetString(23),
            reader.GetDecimal(24),
            reader.GetString(25),
            reader.IsDBNull(26) ? null : reader.GetString(26),
            reader.IsDBNull(27) ? null : reader.GetString(27),
            DateTimeOffsetFromUtc(reader.GetDateTime(28)));
    }

    private static PolymarketOnChainWalletExecution ReadPolymarketOnChainWalletExecution(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainWalletExecution(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            DateTimeOffsetFromUtc(reader.GetDateTime(4)),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetString(8),
            Enum.Parse<TradeSide>(reader.GetString(9)),
            reader.GetString(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetDecimal(17),
            DateTimeOffsetFromUtc(reader.GetDateTime(18)));
    }

    private static PolymarketOnChainWalletPosition ReadPolymarketOnChainWalletPosition(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainWalletPosition(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetBoolean(8),
            reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetDecimal(17),
            reader.GetDecimal(18),
            reader.GetDecimal(19),
            reader.GetDecimal(20),
            reader.GetDecimal(21),
            reader.GetDecimal(22),
            reader.GetDecimal(23),
            reader.IsDBNull(24) ? null : reader.GetDecimal(24),
            reader.GetString(25),
            DateTimeOffsetFromUtc(reader.GetDateTime(26)),
            DateTimeOffsetFromUtc(reader.GetDateTime(27)));
    }

    private static PolymarketOnChainWalletPerformance ReadPolymarketOnChainWalletPerformance(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainWalletPerformance(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetString(17),
            DateTimeOffsetFromUtc(reader.GetDateTime(18)),
            DateTimeOffsetFromUtc(reader.GetDateTime(19)),
            DateTimeOffsetFromUtc(reader.GetDateTime(20)));
    }

    private static PolymarketOnChainWalletCategoryPerformance ReadPolymarketOnChainWalletCategoryPerformance(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainWalletCategoryPerformance(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetDecimal(17),
            reader.GetString(18),
            DateTimeOffsetFromUtc(reader.GetDateTime(19)),
            DateTimeOffsetFromUtc(reader.GetDateTime(20)),
            DateTimeOffsetFromUtc(reader.GetDateTime(21)));
    }

    private static PolymarketOnChainTradeDetails ReadPolymarketOnChainTradeDetails(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainTradeDetails(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            DateTimeOffsetFromUtc(reader.GetDateTime(4)),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            Enum.Parse<TradeSide>(reader.GetString(10)),
            Enum.Parse<TradeSide>(reader.GetString(11)),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetString(16),
            reader.GetDecimal(17),
            reader.GetDecimal(18),
            reader.GetDecimal(19),
            reader.GetDecimal(20),
            reader.GetDecimal(21),
            reader.GetDecimal(22),
            reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetString(24),
            reader.IsDBNull(25) ? null : reader.GetString(25),
            reader.GetString(26),
            reader.GetString(27),
            reader.GetString(28),
            reader.GetString(29),
            reader.GetString(30),
            reader.IsDBNull(31) ? null : reader.GetString(31),
            reader.GetBoolean(32),
            reader.GetBoolean(33),
            reader.GetBoolean(34),
            reader.GetBoolean(35),
            reader.GetBoolean(36),
            reader.IsDBNull(37) ? null : reader.GetString(37),
            DateTimeOffsetFromUtc(reader.GetDateTime(38)));
    }

    private static PolymarketOnChainParticipantDetails ReadPolymarketOnChainParticipantDetails(NpgsqlDataReader reader)
    {
        return new PolymarketOnChainParticipantDetails(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetDecimal(5),
            reader.GetDecimal(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetDecimal(17),
            reader.GetDecimal(18),
            reader.GetDecimal(19),
            reader.GetDecimal(20),
            reader.GetDecimal(21),
            reader.GetString(22),
            DateTimeOffsetFromUtc(reader.GetDateTime(23)),
            DateTimeOffsetFromUtc(reader.GetDateTime(24)),
            DateTimeOffsetFromUtc(reader.GetDateTime(25)),
            reader.IsDBNull(26) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(26)));
    }

    private static void AddLiveOrderParameters(NpgsqlCommand command, LiveOrder order)
    {
        command.Parameters.AddWithValue("Id", order.Id);
        command.Parameters.AddWithValue("SignalId", order.SignalId);
        command.Parameters.AddWithValue("Status", order.Status.ToString());
        command.Parameters.AddWithValue("OrderId", (object?)order.OrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("Side", order.Side.ToString());
        command.Parameters.AddWithValue("AssetId", order.AssetId);
        command.Parameters.AddWithValue("ConditionId", order.ConditionId);
        command.Parameters.AddWithValue("Outcome", order.Outcome);
        command.Parameters.AddWithValue("Price", order.Price);
        command.Parameters.AddWithValue("SizeShares", order.SizeShares);
        command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
        command.Parameters.AddWithValue("OrderType", order.OrderType);
        command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(order.CreatedAtUtc));
        command.Parameters.AddWithValue("ExpiresAtUtc", UtcDateTime(order.ExpiresAtUtc));
        command.Parameters.AddWithValue("SubmittedAtUtc", order.SubmittedAtUtc is { } submittedAt ? UtcDateTime(submittedAt) : DBNull.Value);
        command.Parameters.AddWithValue("ResponseStatus", order.ResponseStatus);
        command.Parameters.AddWithValue("FilledSize", order.FilledSize);
        command.Parameters.AddWithValue("RemainingSize", order.RemainingSize);
        command.Parameters.AddWithValue("CancelStatus", order.CancelStatus);
        command.Parameters.AddWithValue("RawResponseJson", string.IsNullOrWhiteSpace(order.RawResponseJson) ? "{}" : order.RawResponseJson);
        command.Parameters.AddWithValue("ValidationSummary", order.ValidationSummary);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(order.UpdatedAtUtc));
    }

    private static void AddTraderDiscoveryParameters(NpgsqlCommand command, TraderDiscoveryCandidate candidate)
    {
        command.Parameters.AddWithValue("Id", candidate.Id);
        command.Parameters.AddWithValue("DiscoveryType", candidate.DiscoveryType);
        command.Parameters.AddWithValue("Category", candidate.Category);
        command.Parameters.AddWithValue("TimePeriod", candidate.TimePeriod);
        command.Parameters.AddWithValue("Rank", candidate.Rank is { } rank ? rank : DBNull.Value);
        command.Parameters.AddWithValue("Wallet", candidate.Wallet);
        command.Parameters.AddWithValue("UserName", candidate.UserName);
        command.Parameters.AddWithValue("XUsername", (object?)candidate.XUsername ?? DBNull.Value);
        command.Parameters.AddWithValue("LeaderboardPnl", candidate.LeaderboardPnl);
        command.Parameters.AddWithValue("LeaderboardVolume", candidate.LeaderboardVolume);
        command.Parameters.Add("AllTimePnl", NpgsqlDbType.Numeric).Value =
            candidate.AllTimePnl is { } allTimePnl ? allTimePnl : DBNull.Value;
        command.Parameters.Add("AllTimeVolume", NpgsqlDbType.Numeric).Value =
            candidate.AllTimeVolume is { } allTimeVolume ? allTimeVolume : DBNull.Value;
        command.Parameters.AddWithValue("VerifiedBadge", candidate.VerifiedBadge);
        command.Parameters.AddWithValue("TradesFetched", candidate.TradesFetched);
        command.Parameters.AddWithValue("BuyTrades", candidate.BuyTrades);
        command.Parameters.AddWithValue("SellTrades", candidate.SellTrades);
        command.Parameters.AddWithValue("RecentTradeVolumeUsd", candidate.RecentTradeVolumeUsd);
        command.Parameters.AddWithValue("AverageTradeUsd", candidate.AverageTradeUsd);
        command.Parameters.AddWithValue("LastTradeUtc", candidate.LastTradeUtc is { } lastTrade ? UtcDateTime(lastTrade) : DBNull.Value);
        command.Parameters.AddWithValue("PositionsFetched", candidate.PositionsFetched);
        command.Parameters.AddWithValue("OpenPositionValueUsd", candidate.OpenPositionValueUsd);
        command.Parameters.AddWithValue("OpenPositionCashPnlUsd", candidate.OpenPositionCashPnlUsd);
        command.Parameters.AddWithValue("OpenPositionRealizedPnlUsd", candidate.OpenPositionRealizedPnlUsd);
        command.Parameters.AddWithValue("Notes", candidate.Notes);
        command.Parameters.AddWithValue("SnapshotAtUtc", UtcDateTime(candidate.SnapshotAtUtc));
        command.Parameters.AddWithValue("UpdatedAtUtc", DateTime.UtcNow);
    }

    private static void AddTraderLeaderboardSnapshotParameters(NpgsqlCommand command, TraderLeaderboardSnapshot snapshot)
    {
        command.Parameters.AddWithValue("Id", snapshot.Id);
        command.Parameters.AddWithValue("DiscoveryRunId", snapshot.DiscoveryRunId);
        command.Parameters.AddWithValue("Category", snapshot.Category);
        command.Parameters.AddWithValue("TimePeriod", snapshot.TimePeriod);
        command.Parameters.AddWithValue("Wallet", snapshot.Wallet);
        command.Parameters.AddWithValue("UserName", snapshot.UserName);
        command.Parameters.AddWithValue("XUsername", (object?)snapshot.XUsername ?? DBNull.Value);
        command.Parameters.AddWithValue("VerifiedBadge", snapshot.VerifiedBadge);
        command.Parameters.Add("PnlRank", NpgsqlDbType.Integer).Value =
            snapshot.PnlRank is { } pnlRank ? pnlRank : DBNull.Value;
        command.Parameters.Add("PnlPageOffset", NpgsqlDbType.Integer).Value =
            snapshot.PnlPageOffset is { } pnlPageOffset ? pnlPageOffset : DBNull.Value;
        command.Parameters.Add("PnlLeaderboardPnl", NpgsqlDbType.Numeric).Value =
            snapshot.PnlLeaderboardPnl is { } pnlLeaderboardPnl ? pnlLeaderboardPnl : DBNull.Value;
        command.Parameters.Add("PnlLeaderboardVolume", NpgsqlDbType.Numeric).Value =
            snapshot.PnlLeaderboardVolume is { } pnlLeaderboardVolume ? pnlLeaderboardVolume : DBNull.Value;
        command.Parameters.Add("PnlSnapshotAtUtc", NpgsqlDbType.TimestampTz).Value =
            snapshot.PnlSnapshotAtUtc is { } pnlSnapshotAt ? UtcDateTime(pnlSnapshotAt) : DBNull.Value;
        command.Parameters.Add("VolumeRank", NpgsqlDbType.Integer).Value =
            snapshot.VolumeRank is { } volumeRank ? volumeRank : DBNull.Value;
        command.Parameters.Add("VolumePageOffset", NpgsqlDbType.Integer).Value =
            snapshot.VolumePageOffset is { } volumePageOffset ? volumePageOffset : DBNull.Value;
        command.Parameters.Add("VolumeLeaderboardPnl", NpgsqlDbType.Numeric).Value =
            snapshot.VolumeLeaderboardPnl is { } volumeLeaderboardPnl ? volumeLeaderboardPnl : DBNull.Value;
        command.Parameters.Add("VolumeLeaderboardVolume", NpgsqlDbType.Numeric).Value =
            snapshot.VolumeLeaderboardVolume is { } volumeLeaderboardVolume ? volumeLeaderboardVolume : DBNull.Value;
        command.Parameters.Add("VolumeSnapshotAtUtc", NpgsqlDbType.TimestampTz).Value =
            snapshot.VolumeSnapshotAtUtc is { } volumeSnapshotAt ? UtcDateTime(volumeSnapshotAt) : DBNull.Value;
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(snapshot.UpdatedAtUtc));
    }

    private static async Task<IReadOnlyList<LiveOrder>> ReadLiveOrdersAsync(
        NpgsqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var results = new List<LiveOrder>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LiveOrder(
                reader.GetGuid(0),
                reader.GetGuid(1),
                Enum.Parse<LiveOrderStatus>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Enum.Parse<TradeSide>(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetString(11),
                DateTimeOffsetFromUtc(reader.GetDateTime(12)),
                DateTimeOffsetFromUtc(reader.GetDateTime(13)),
                reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)),
                reader.GetString(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.GetString(18),
                reader.GetString(19),
                reader.GetString(20),
                DateTimeOffsetFromUtc(reader.GetDateTime(21))));
        }

        return results;
    }

    private static IReadOnlyList<string> SplitReasonCodes(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
