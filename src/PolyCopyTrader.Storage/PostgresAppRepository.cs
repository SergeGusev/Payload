using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class PostgresAppRepository(PostgresConnectionFactory connectionFactory) : IAppRepository
{
	private const int OnChainDerivedRefreshLockKey1 = 1348686930;

	private const int OnChainDerivedRefreshLockKey2 = 1329812038;

	private const int PaperCopiedTraderPerformanceRefreshLockKey1 = 1348686931;

	private const int PaperCopiedTraderPerformanceRefreshLockKey2 = 1329812039;

	private const string PolymarketGammaMarketSelectColumns = "market_id, condition_id, question_id, slug, question, event_id, event_slug, event_title,\n       series_slug, category, active, closed, archived, restricted, accepting_orders, enable_order_book,\n       negative_risk, liquidity, liquidity_clob, volume, volume_24hr, best_bid, best_ask, spread,\n       created_at_utc, updated_at_utc, start_date_utc, end_date_utc, event_start_time_utc,\n       outcomes_json, clob_token_ids_json, raw_json, fetched_at_utc, last_trade_price, order_min_size,\n       order_price_min_tick_size";

	private const string PaperOrderSelectColumns = "id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n       created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json::text, correlation_id, execution_source";

	private const string LiveOrderSelectColumns = "id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,\n       notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,\n       filled_size, remaining_size, average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,\n       cancel_status, raw_response_json::text, validation_summary, updated_at_utc,\n       balance_effect_applied, settlement_value_usd, realized_pnl_usd, settled_at_utc, winning_asset_id, winning_outcome,\n       won, settlement_source, correlation_id, execution_source, post_only, paper_order_id";

	public async Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default(CancellationToken))
	{
		await TryAddLeaderTradeAsync(trade, cancellationToken);
	}

	public async Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default(CancellationToken))
	{
		bool result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			bool flag;
			await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO leader_trades (\n    id, trader_wallet, trader_name, condition_id, asset_id, market_slug, market_title, outcome,\n    side, price, size, cash_value_usd, timestamp_utc, transaction_hash, dedup_key, raw_json, created_at_utc\n) VALUES (\n    @Id, @TraderWallet, @TraderName, @ConditionId, @AssetId, @MarketSlug, @MarketTitle, @Outcome,\n    @Side, @Price, @Size, @CashValueUsd, @TimestampUtc, @TransactionHash, @DedupKey, CAST(@RawJson AS jsonb), @CreatedAtUtc\n)\nON CONFLICT (dedup_key) DO NOTHING;"))
			{
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
				command.Parameters.AddWithValue("TransactionHash", ((object)trade.TransactionHash) ?? ((object)DBNull.Value));
				command.Parameters.AddWithValue("DedupKey", LeaderTradeDeduplication.BuildKey(trade));
				command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(trade));
				command.Parameters.AddWithValue("CreatedAtUtc", DateTime.UtcNow);
				flag = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
			}
			result = flag;
		}
		return result;
	}

	public async Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LeaderTrade> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LeaderTrade> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT trader_wallet, trader_name, condition_id, asset_id, market_slug, market_title, outcome, side,\n       price, size, cash_value_usd, timestamp_utc, transaction_hash\nFROM leader_trades\nORDER BY timestamp_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<LeaderTrade> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<LeaderTrade> results = new List<LeaderTrade>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new LeaderTrade(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), Enum.Parse<TradeSide>(reader.GetString(7)), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), DateTimeOffsetFromUtc(reader.GetDateTime(11)), reader.IsDBNull(12) ? null : reader.GetString(12)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO leader_positions (\n    id, trader_wallet, condition_id, asset_id, outcome, size, avg_price, initial_value, current_value,\n    cash_pnl, percent_pnl, total_bought, realized_pnl, cur_price, title, market_slug, opposite_asset,\n    end_date_utc, negative_risk, snapshot_at_utc, raw_json\n) VALUES (\n    @Id, @TraderWallet, @ConditionId, @AssetId, @Outcome, @Size, @AvgPrice, @InitialValue, @CurrentValue,\n    @CashPnl, @PercentPnl, @TotalBought, @RealizedPnl, @CurPrice, @Title, @MarketSlug, @OppositeAsset,\n    @EndDateUtc, @NegativeRisk, @SnapshotAtUtc, CAST(@RawJson AS jsonb)\n);");
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
		command.Parameters.AddWithValue("Title", ((object)position.Title) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("MarketSlug", ((object)position.MarketSlug) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("OppositeAsset", ((object)position.OppositeAsset) ?? ((object)DBNull.Value));
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? endDateUtc = position.EndDateUtc;
		object value;
		if (endDateUtc.HasValue)
		{
			DateTimeOffset endDate = endDateUtc.GetValueOrDefault();
			value = UtcDateTime(endDate);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("EndDateUtc", value);
		command.Parameters.AddWithValue("NegativeRisk", position.NegativeRisk);
		command.Parameters.AddWithValue("SnapshotAtUtc", UtcDateTime(position.SnapshotAtUtc));
		command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(position));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddTraderLeaderboardSnapshotsAsync(IReadOnlyList<TraderLeaderboardSnapshot> snapshots, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (snapshots.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (TraderLeaderboardSnapshot snapshot in snapshots)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO trader_leaderboard_snapshots (\n    id, discovery_run_id, category, time_period, wallet, user_name, x_username, verified_badge,\n    pnl_rank, pnl_page_offset, pnl_leaderboard_pnl, pnl_leaderboard_volume, pnl_snapshot_at_utc,\n    volume_rank, volume_page_offset, volume_leaderboard_pnl, volume_leaderboard_volume, volume_snapshot_at_utc,\n    updated_at_utc\n) VALUES (\n    @Id, @DiscoveryRunId, @Category, @TimePeriod, @Wallet, @UserName, @XUsername, @VerifiedBadge,\n    @PnlRank, @PnlPageOffset, @PnlLeaderboardPnl, @PnlLeaderboardVolume, @PnlSnapshotAtUtc,\n    @VolumeRank, @VolumePageOffset, @VolumeLeaderboardPnl, @VolumeLeaderboardVolume, @VolumeSnapshotAtUtc,\n    @UpdatedAtUtc\n)\nON CONFLICT (category, time_period, wallet) DO UPDATE SET\n    discovery_run_id = excluded.discovery_run_id,\n    user_name = excluded.user_name,\n    x_username = excluded.x_username,\n    verified_badge = excluded.verified_badge,\n    pnl_rank = excluded.pnl_rank,\n    pnl_page_offset = excluded.pnl_page_offset,\n    pnl_leaderboard_pnl = excluded.pnl_leaderboard_pnl,\n    pnl_leaderboard_volume = excluded.pnl_leaderboard_volume,\n    pnl_snapshot_at_utc = excluded.pnl_snapshot_at_utc,\n    volume_rank = excluded.volume_rank,\n    volume_page_offset = excluded.volume_page_offset,\n    volume_leaderboard_pnl = excluded.volume_leaderboard_pnl,\n    volume_leaderboard_volume = excluded.volume_leaderboard_volume,\n    volume_snapshot_at_utc = excluded.volume_snapshot_at_utc,\n    updated_at_utc = excluded.updated_at_utc;");
			command.Transaction = transaction;
			AddTraderLeaderboardSnapshotParameters(command, snapshot);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task UpsertTraderDiscoveryCandidatesAsync(IReadOnlyList<TraderDiscoveryCandidate> candidates, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (candidates.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (TraderDiscoveryCandidate candidate in candidates)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO trader_discovery_candidates (\n    id, discovery_type, category, time_period, rank, wallet, user_name, x_username,\n    leaderboard_pnl, leaderboard_volume, all_time_pnl, all_time_volume, verified_badge, trades_fetched, buy_trades,\n    sell_trades, recent_trade_volume_usd, average_trade_usd, last_trade_utc,\n    positions_fetched, open_position_value_usd, open_position_cash_pnl_usd,\n    open_position_realized_pnl_usd, notes, snapshot_at_utc, updated_at_utc\n) VALUES (\n    @Id, @DiscoveryType, @Category, @TimePeriod, @Rank, @Wallet, @UserName, @XUsername,\n    @LeaderboardPnl, @LeaderboardVolume, @AllTimePnl, @AllTimeVolume, @VerifiedBadge, @TradesFetched, @BuyTrades,\n    @SellTrades, @RecentTradeVolumeUsd, @AverageTradeUsd, @LastTradeUtc,\n    @PositionsFetched, @OpenPositionValueUsd, @OpenPositionCashPnlUsd,\n    @OpenPositionRealizedPnlUsd, @Notes, @SnapshotAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (discovery_type, category, time_period, wallet) DO UPDATE SET\n    id = excluded.id,\n    rank = excluded.rank,\n    user_name = excluded.user_name,\n    x_username = excluded.x_username,\n    leaderboard_pnl = excluded.leaderboard_pnl,\n    leaderboard_volume = excluded.leaderboard_volume,\n    all_time_pnl = excluded.all_time_pnl,\n    all_time_volume = excluded.all_time_volume,\n    verified_badge = excluded.verified_badge,\n    trades_fetched = excluded.trades_fetched,\n    buy_trades = excluded.buy_trades,\n    sell_trades = excluded.sell_trades,\n    recent_trade_volume_usd = excluded.recent_trade_volume_usd,\n    average_trade_usd = excluded.average_trade_usd,\n    last_trade_utc = excluded.last_trade_utc,\n    positions_fetched = excluded.positions_fetched,\n    open_position_value_usd = excluded.open_position_value_usd,\n    open_position_cash_pnl_usd = excluded.open_position_cash_pnl_usd,\n    open_position_realized_pnl_usd = excluded.open_position_realized_pnl_usd,\n    notes = excluded.notes,\n    snapshot_at_utc = excluded.snapshot_at_utc,\n    updated_at_utc = excluded.updated_at_utc;");
			command.Transaction = transaction;
			AddTraderDiscoveryParameters(command, candidate);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<TraderDiscoveryCandidate> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<TraderDiscoveryCandidate> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, discovery_type, category, time_period, rank, wallet, user_name, x_username,\n       leaderboard_pnl, leaderboard_volume, all_time_pnl, all_time_volume, verified_badge, trades_fetched, buy_trades,\n       sell_trades, recent_trade_volume_usd, average_trade_usd, last_trade_utc,\n       positions_fetched, open_position_value_usd, open_position_cash_pnl_usd,\n       open_position_realized_pnl_usd, notes, snapshot_at_utc\nFROM trader_discovery_candidates\nORDER BY snapshot_at_utc DESC,\n         discovery_type,\n         CASE WHEN discovery_type = 'WorstPnl' THEN leaderboard_pnl END ASC,\n         leaderboard_pnl DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<TraderDiscoveryCandidate> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<TraderDiscoveryCandidate> results = new List<TraderDiscoveryCandidate>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new TraderDiscoveryCandidate(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? ((int?)null) : new int?(reader.GetInt32(4)), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.IsDBNull(10) ? ((decimal?)null) : new decimal?(reader.GetDecimal(10)), reader.IsDBNull(11) ? ((decimal?)null) : new decimal?(reader.GetDecimal(11)), reader.GetBoolean(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.IsDBNull(18) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(18))), reader.GetInt32(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetString(23), DateTimeOffsetFromUtc(reader.GetDateTime(24))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<PolymarketDataApiTrader?> GetPolymarketDataApiTraderAsync(string wallet, CancellationToken cancellationToken = default(CancellationToken))
	{
		PolymarketDataApiTrader result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			PolymarketDataApiTrader polymarketDataApiTrader2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, name, pseudonym, bio, profile_image, profile_image_optimized,\n       first_seen_at_utc, last_seen_at_utc, last_global_seen_at_utc,\n       last_full_sync_at_utc, last_incremental_sync_at_utc, last_trade_timestamp_utc,\n       full_sync_completed, full_sync_trades_fetched, full_sync_trades_inserted,\n       incremental_sync_count, updated_at_utc\nFROM polymarket_data_api_traders\nWHERE wallet = @Wallet;"))
			{
				command.Parameters.AddWithValue("Wallet", wallet);
				PolymarketDataApiTrader polymarketDataApiTrader;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					polymarketDataApiTrader = ((await reader.ReadAsync(cancellationToken)) ? ReadPolymarketDataApiTrader(reader) : null);
				}
				polymarketDataApiTrader2 = polymarketDataApiTrader;
			}
			result = polymarketDataApiTrader2;
		}
		return result;
	}

	public async Task UpsertPolymarketDataApiTraderAsync(PolymarketDataApiTrader trader, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_data_api_traders (\n    wallet, name, pseudonym, bio, profile_image, profile_image_optimized,\n    first_seen_at_utc, last_seen_at_utc, last_global_seen_at_utc,\n    last_full_sync_at_utc, last_incremental_sync_at_utc, last_trade_timestamp_utc,\n    full_sync_completed, full_sync_trades_fetched, full_sync_trades_inserted,\n    incremental_sync_count, updated_at_utc\n) VALUES (\n    @Wallet, @Name, @Pseudonym, @Bio, @ProfileImage, @ProfileImageOptimized,\n    @FirstSeenAtUtc, @LastSeenAtUtc, @LastGlobalSeenAtUtc,\n    @LastFullSyncAtUtc, @LastIncrementalSyncAtUtc, @LastTradeTimestampUtc,\n    @FullSyncCompleted, @FullSyncTradesFetched, @FullSyncTradesInserted,\n    @IncrementalSyncCount, @UpdatedAtUtc\n)\nON CONFLICT (wallet) DO UPDATE SET\n    name = CASE WHEN excluded.name <> '' THEN excluded.name ELSE polymarket_data_api_traders.name END,\n    pseudonym = COALESCE(excluded.pseudonym, polymarket_data_api_traders.pseudonym),\n    bio = COALESCE(excluded.bio, polymarket_data_api_traders.bio),\n    profile_image = COALESCE(excluded.profile_image, polymarket_data_api_traders.profile_image),\n    profile_image_optimized = COALESCE(excluded.profile_image_optimized, polymarket_data_api_traders.profile_image_optimized),\n    last_seen_at_utc = excluded.last_seen_at_utc,\n    last_global_seen_at_utc = COALESCE(excluded.last_global_seen_at_utc, polymarket_data_api_traders.last_global_seen_at_utc),\n    last_trade_timestamp_utc =\n        CASE\n            WHEN excluded.last_trade_timestamp_utc IS NULL THEN polymarket_data_api_traders.last_trade_timestamp_utc\n            WHEN polymarket_data_api_traders.last_trade_timestamp_utc IS NULL THEN excluded.last_trade_timestamp_utc\n            ELSE GREATEST(polymarket_data_api_traders.last_trade_timestamp_utc, excluded.last_trade_timestamp_utc)\n        END,\n    updated_at_utc = excluded.updated_at_utc\nWHERE\n    (excluded.name <> '' AND polymarket_data_api_traders.name IS DISTINCT FROM excluded.name)\n    OR (excluded.pseudonym IS NOT NULL AND polymarket_data_api_traders.pseudonym IS DISTINCT FROM excluded.pseudonym)\n    OR (excluded.bio IS NOT NULL AND polymarket_data_api_traders.bio IS DISTINCT FROM excluded.bio)\n    OR (excluded.profile_image IS NOT NULL AND polymarket_data_api_traders.profile_image IS DISTINCT FROM excluded.profile_image)\n    OR (excluded.profile_image_optimized IS NOT NULL AND polymarket_data_api_traders.profile_image_optimized IS DISTINCT FROM excluded.profile_image_optimized)\n    OR (\n        excluded.last_trade_timestamp_utc IS NOT NULL\n        AND (\n            polymarket_data_api_traders.last_trade_timestamp_utc IS NULL\n            OR excluded.last_trade_timestamp_utc > polymarket_data_api_traders.last_trade_timestamp_utc\n        )\n    )\n    OR polymarket_data_api_traders.last_seen_at_utc <= excluded.last_seen_at_utc - interval '5 minutes'\n    OR (\n        excluded.last_global_seen_at_utc IS NOT NULL\n        AND (\n            polymarket_data_api_traders.last_global_seen_at_utc IS NULL\n            OR polymarket_data_api_traders.last_global_seen_at_utc <= excluded.last_global_seen_at_utc - interval '5 minutes'\n        )\n    );");
		AddPolymarketDataApiTraderParameters(command, trader);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<int> UpsertPolymarketDataApiTradersAsync(IReadOnlyList<PolymarketDataApiTrader> traders, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (traders.Count == 0)
		{
			return 0;
		}
		int result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			int num2;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				int rows = 0;
				foreach (PolymarketDataApiTrader trader in traders)
				{
					await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_data_api_traders (\n    wallet, name, pseudonym, bio, profile_image, profile_image_optimized,\n    first_seen_at_utc, last_seen_at_utc, last_global_seen_at_utc,\n    last_full_sync_at_utc, last_incremental_sync_at_utc, last_trade_timestamp_utc,\n    full_sync_completed, full_sync_trades_fetched, full_sync_trades_inserted,\n    incremental_sync_count, updated_at_utc\n) VALUES (\n    @Wallet, @Name, @Pseudonym, @Bio, @ProfileImage, @ProfileImageOptimized,\n    @FirstSeenAtUtc, @LastSeenAtUtc, @LastGlobalSeenAtUtc,\n    @LastFullSyncAtUtc, @LastIncrementalSyncAtUtc, @LastTradeTimestampUtc,\n    @FullSyncCompleted, @FullSyncTradesFetched, @FullSyncTradesInserted,\n    @IncrementalSyncCount, @UpdatedAtUtc\n)\nON CONFLICT (wallet) DO UPDATE SET\n    name = CASE WHEN excluded.name <> '' THEN excluded.name ELSE polymarket_data_api_traders.name END,\n    pseudonym = COALESCE(excluded.pseudonym, polymarket_data_api_traders.pseudonym),\n    bio = COALESCE(excluded.bio, polymarket_data_api_traders.bio),\n    profile_image = COALESCE(excluded.profile_image, polymarket_data_api_traders.profile_image),\n    profile_image_optimized = COALESCE(excluded.profile_image_optimized, polymarket_data_api_traders.profile_image_optimized),\n    last_seen_at_utc = excluded.last_seen_at_utc,\n    last_global_seen_at_utc = COALESCE(excluded.last_global_seen_at_utc, polymarket_data_api_traders.last_global_seen_at_utc),\n    last_trade_timestamp_utc =\n        CASE\n            WHEN excluded.last_trade_timestamp_utc IS NULL THEN polymarket_data_api_traders.last_trade_timestamp_utc\n            WHEN polymarket_data_api_traders.last_trade_timestamp_utc IS NULL THEN excluded.last_trade_timestamp_utc\n            ELSE GREATEST(polymarket_data_api_traders.last_trade_timestamp_utc, excluded.last_trade_timestamp_utc)\n        END,\n    updated_at_utc = excluded.updated_at_utc\nWHERE\n    (excluded.name <> '' AND polymarket_data_api_traders.name IS DISTINCT FROM excluded.name)\n    OR (excluded.pseudonym IS NOT NULL AND polymarket_data_api_traders.pseudonym IS DISTINCT FROM excluded.pseudonym)\n    OR (excluded.bio IS NOT NULL AND polymarket_data_api_traders.bio IS DISTINCT FROM excluded.bio)\n    OR (excluded.profile_image IS NOT NULL AND polymarket_data_api_traders.profile_image IS DISTINCT FROM excluded.profile_image)\n    OR (excluded.profile_image_optimized IS NOT NULL AND polymarket_data_api_traders.profile_image_optimized IS DISTINCT FROM excluded.profile_image_optimized)\n    OR (\n        excluded.last_trade_timestamp_utc IS NOT NULL\n        AND (\n            polymarket_data_api_traders.last_trade_timestamp_utc IS NULL\n            OR excluded.last_trade_timestamp_utc > polymarket_data_api_traders.last_trade_timestamp_utc\n        )\n    )\n    OR polymarket_data_api_traders.last_seen_at_utc <= excluded.last_seen_at_utc - interval '5 minutes'\n    OR (\n        excluded.last_global_seen_at_utc IS NOT NULL\n        AND (\n            polymarket_data_api_traders.last_global_seen_at_utc IS NULL\n            OR polymarket_data_api_traders.last_global_seen_at_utc <= excluded.last_global_seen_at_utc - interval '5 minutes'\n        )\n    );");
					command.Transaction = transaction;
					AddPolymarketDataApiTraderParameters(command, trader);
					int num = rows;
					rows = num + await command.ExecuteNonQueryAsync(cancellationToken);
				}
				await transaction.CommitAsync(cancellationToken);
				num2 = rows;
			}
			result = num2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForSyncAsync(int limit, DateTimeOffset incrementalSyncBeforeUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketDataApiTrader> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketDataApiTrader> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, name, pseudonym, bio, profile_image, profile_image_optimized,\n       first_seen_at_utc, last_seen_at_utc, last_global_seen_at_utc,\n       last_full_sync_at_utc, last_incremental_sync_at_utc, last_trade_timestamp_utc,\n       full_sync_completed, full_sync_trades_fetched, full_sync_trades_inserted,\n       incremental_sync_count, updated_at_utc\nFROM polymarket_data_api_traders\nWHERE full_sync_completed = false\n   OR last_incremental_sync_at_utc IS NULL\n   OR last_incremental_sync_at_utc <= @IncrementalSyncBeforeUtc\nORDER BY\n    CASE WHEN full_sync_completed THEN 1 ELSE 0 END,\n    COALESCE(last_full_sync_at_utc, first_seen_at_utc),\n    COALESCE(last_incremental_sync_at_utc, first_seen_at_utc),\n    last_seen_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				command.Parameters.Add("IncrementalSyncBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(incrementalSyncBeforeUtc);
				IReadOnlyList<PolymarketDataApiTrader> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketDataApiTrader> traders = new List<PolymarketDataApiTrader>();
					while (await reader.ReadAsync(cancellationToken))
					{
						traders.Add(ReadPolymarketDataApiTrader(reader));
					}
					readOnlyList = traders;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForRatingRefreshAsync(int limit, DateTimeOffset dueBeforeUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketDataApiTrader> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketDataApiTrader> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, name, pseudonym, bio, profile_image, profile_image_optimized,\n       first_seen_at_utc, last_seen_at_utc, last_global_seen_at_utc,\n       last_full_sync_at_utc, last_incremental_sync_at_utc, last_trade_timestamp_utc,\n       full_sync_completed, full_sync_trades_fetched, full_sync_trades_inserted,\n       incremental_sync_count, updated_at_utc,\n       polymarket_rating_refreshed_at_utc, polymarket_rating_next_refresh_at_utc,\n       polymarket_rating_refresh_attempts, polymarket_rating_last_error\nFROM polymarket_data_api_traders\nWHERE polymarket_rating_next_refresh_at_utc <= @DueBeforeUtc\nORDER BY polymarket_rating_next_refresh_at_utc, last_seen_at_utc DESC, wallet\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				command.Parameters.Add("DueBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(dueBeforeUtc);
				IReadOnlyList<PolymarketDataApiTrader> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketDataApiTrader> traders = new List<PolymarketDataApiTrader>();
					while (await reader.ReadAsync(cancellationToken))
					{
						traders.Add(ReadPolymarketDataApiTrader(reader));
					}
					readOnlyList = traders;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task MarkPolymarketDataApiTraderSyncedAsync(string wallet, bool fullSync, int tradesFetched, int tradesInserted, DateTimeOffset? latestTradeTimestampUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_data_api_traders\nSET last_full_sync_at_utc = CASE WHEN @FullSync THEN @NowUtc ELSE last_full_sync_at_utc END,\n    last_incremental_sync_at_utc = CASE WHEN @FullSync THEN last_incremental_sync_at_utc ELSE @NowUtc END,\n    full_sync_completed = CASE WHEN @FullSync THEN true ELSE full_sync_completed END,\n    full_sync_trades_fetched = CASE WHEN @FullSync THEN full_sync_trades_fetched + @TradesFetched ELSE full_sync_trades_fetched END,\n    full_sync_trades_inserted = CASE WHEN @FullSync THEN full_sync_trades_inserted + @TradesInserted ELSE full_sync_trades_inserted END,\n    incremental_sync_count = CASE WHEN @FullSync THEN incremental_sync_count ELSE incremental_sync_count + 1 END,\n    last_trade_timestamp_utc =\n        CASE\n            WHEN @LatestTradeTimestampUtc IS NULL THEN last_trade_timestamp_utc\n            WHEN last_trade_timestamp_utc IS NULL THEN @LatestTradeTimestampUtc\n            ELSE GREATEST(last_trade_timestamp_utc, @LatestTradeTimestampUtc)\n        END,\n    updated_at_utc = @NowUtc\nWHERE wallet = @Wallet;");
		command.Parameters.AddWithValue("Wallet", wallet);
		command.Parameters.AddWithValue("FullSync", fullSync);
		command.Parameters.AddWithValue("TradesFetched", tradesFetched);
		command.Parameters.AddWithValue("TradesInserted", tradesInserted);
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("LatestTradeTimestampUtc", NpgsqlDbType.TimestampTz);
		object value;
		if (latestTradeTimestampUtc.HasValue)
		{
			DateTimeOffset latest = latestTradeTimestampUtc.GetValueOrDefault();
			value = UtcDateTime(latest);
		}
		else
		{
			value = DBNull.Value;
		}
		npgsqlParameter.Value = value;
		command.Parameters.AddWithValue("NowUtc", DateTime.UtcNow);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<PolymarketDataApiPerformanceRefreshResult> RefreshPolymarketDataApiPositionsAndPerformanceAsync(string wallet, IReadOnlyList<PolymarketDataApiPosition> currentPositions, IReadOnlyList<PolymarketDataApiPosition> closedPositions, CancellationToken cancellationToken = default(CancellationToken))
	{
		PolymarketDataApiPerformanceRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			PolymarketDataApiPerformanceRefreshResult polymarketDataApiPerformanceRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				await using (NpgsqlCommand deleteOpenCommand = CreateCommand(connection, "DELETE FROM polymarket_data_api_positions WHERE wallet = @Wallet AND position_status = 'Open';"))
				{
					deleteOpenCommand.Transaction = transaction;
					deleteOpenCommand.Parameters.AddWithValue("Wallet", wallet);
					await deleteOpenCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				int positionsUpserted = 0;
				foreach (PolymarketDataApiPosition position in currentPositions.Concat(closedPositions))
				{
					await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_data_api_positions (\n    id, wallet, position_status, asset_id, condition_id, size, avg_price,\n    initial_value_usd, current_value_usd, cash_pnl_usd, percent_pnl,\n    total_bought, realized_pnl_usd, percent_realized_pnl, cur_price,\n    timestamp_utc, market_title, market_slug, icon, event_id, event_slug,\n    category, outcome, outcome_index, opposite_outcome, opposite_asset, end_date_utc,\n    redeemable, mergeable, negative_risk, raw_json, fetched_at_utc, updated_at_utc\n) VALUES (\n    @Id, @Wallet, @PositionStatus, @AssetId, @ConditionId, @Size, @AvgPrice,\n    @InitialValueUsd, @CurrentValueUsd, @CashPnlUsd, @PercentPnl,\n    @TotalBought, @RealizedPnlUsd, @PercentRealizedPnl, @CurPrice,\n    @TimestampUtc, @MarketTitle, @MarketSlug, @Icon, @EventId, @EventSlug,\n    @Category, @Outcome, @OutcomeIndex, @OppositeOutcome, @OppositeAsset, @EndDateUtc,\n    @Redeemable, @Mergeable, @NegativeRisk, CAST(@RawJson AS jsonb), @FetchedAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (wallet, position_status, asset_id) DO UPDATE SET\n    condition_id = excluded.condition_id,\n    size = excluded.size,\n    avg_price = excluded.avg_price,\n    initial_value_usd = excluded.initial_value_usd,\n    current_value_usd = excluded.current_value_usd,\n    cash_pnl_usd = excluded.cash_pnl_usd,\n    percent_pnl = excluded.percent_pnl,\n    total_bought = excluded.total_bought,\n    realized_pnl_usd = excluded.realized_pnl_usd,\n    percent_realized_pnl = excluded.percent_realized_pnl,\n    cur_price = excluded.cur_price,\n    timestamp_utc = COALESCE(excluded.timestamp_utc, polymarket_data_api_positions.timestamp_utc),\n    market_title = excluded.market_title,\n    market_slug = excluded.market_slug,\n    icon = excluded.icon,\n    event_id = excluded.event_id,\n    event_slug = excluded.event_slug,\n    category = COALESCE(NULLIF(excluded.category, ''), polymarket_data_api_positions.category),\n    outcome = excluded.outcome,\n    outcome_index = excluded.outcome_index,\n    opposite_outcome = excluded.opposite_outcome,\n    opposite_asset = excluded.opposite_asset,\n    end_date_utc = excluded.end_date_utc,\n    redeemable = excluded.redeemable,\n    mergeable = excluded.mergeable,\n    negative_risk = excluded.negative_risk,\n    raw_json = excluded.raw_json,\n    fetched_at_utc = excluded.fetched_at_utc,\n    updated_at_utc = excluded.updated_at_utc;");
					command.Transaction = transaction;
					AddPolymarketDataApiPositionParameters(command, position with
					{
						Wallet = wallet
					});
					int num = positionsUpserted;
					positionsUpserted = num + await command.ExecuteNonQueryAsync(cancellationToken);
				}
				await using (NpgsqlCommand deleteWalletPerformance = CreateCommand(connection, "DELETE FROM polymarket_data_api_wallet_performance WHERE wallet = @Wallet;"))
				{
					deleteWalletPerformance.Transaction = transaction;
					deleteWalletPerformance.Parameters.AddWithValue("Wallet", wallet);
					await deleteWalletPerformance.ExecuteNonQueryAsync(cancellationToken);
				}
				int walletPerformanceRowsUpserted;
				await using (NpgsqlCommand command2 = CreateCommand(connection, "INSERT INTO polymarket_data_api_wallet_performance (\n    wallet, positions_count, open_positions, closed_positions, profitable_positions,\n    losing_positions, markets_traded, outcomes_traded, volume_usd,\n    open_initial_value_usd, open_current_value_usd, open_cash_pnl_usd, open_realized_pnl_usd,\n    closed_cost_basis_usd, closed_realized_pnl_usd, total_cost_basis_usd, total_current_value_usd,\n    total_pnl_usd, realized_pnl_usd, roi_pct, win_rate_pct, average_position_size_usd,\n    score, sample_quality, last_position_timestamp_utc, refreshed_at_utc\n)\nWITH base AS (\n    SELECT\n        wallet,\n        position_status,\n        condition_id,\n        asset_id,\n        CASE\n            WHEN position_status = 'Open' THEN COALESCE(initial_value_usd, total_bought * avg_price)\n            ELSE total_bought * avg_price\n        END AS cost_basis_usd,\n        CASE WHEN position_status = 'Open' THEN COALESCE(current_value_usd, 0) ELSE 0 END AS current_value_usd,\n        CASE\n            WHEN position_status = 'Open' THEN COALESCE(cash_pnl_usd, 0) + realized_pnl_usd\n            ELSE realized_pnl_usd\n        END AS position_pnl_usd,\n        realized_pnl_usd,\n        COALESCE(timestamp_utc, end_date_utc, updated_at_utc) AS activity_utc\n    FROM polymarket_data_api_positions\n    WHERE wallet = @Wallet\n),\nmetrics AS (\n    SELECT\n        wallet,\n        COUNT(*)::integer AS positions_count,\n        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Closed')::integer AS closed_positions,\n        COUNT(*) FILTER (WHERE position_pnl_usd > 0)::integer AS profitable_positions,\n        COUNT(*) FILTER (WHERE position_pnl_usd < 0)::integer AS losing_positions,\n        COUNT(DISTINCT condition_id)::integer AS markets_traded,\n        COUNT(DISTINCT asset_id)::integer AS outcomes_traded,\n        COALESCE(SUM(cost_basis_usd), 0)::numeric AS volume_usd,\n        COALESCE(SUM(cost_basis_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_initial_value_usd,\n        COALESCE(SUM(current_value_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_current_value_usd,\n        COALESCE(SUM(position_pnl_usd - realized_pnl_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_cash_pnl_usd,\n        COALESCE(SUM(realized_pnl_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_realized_pnl_usd,\n        COALESCE(SUM(cost_basis_usd) FILTER (WHERE position_status = 'Closed'), 0)::numeric AS closed_cost_basis_usd,\n        COALESCE(SUM(realized_pnl_usd) FILTER (WHERE position_status = 'Closed'), 0)::numeric AS closed_realized_pnl_usd,\n        COALESCE(SUM(cost_basis_usd), 0)::numeric AS total_cost_basis_usd,\n        COALESCE(SUM(current_value_usd), 0)::numeric AS total_current_value_usd,\n        COALESCE(SUM(position_pnl_usd), 0)::numeric AS total_pnl_usd,\n        COALESCE(SUM(realized_pnl_usd), 0)::numeric AS realized_pnl_usd,\n        COALESCE(AVG(cost_basis_usd), 0)::numeric AS average_position_size_usd,\n        MAX(activity_utc) AS last_position_timestamp_utc\n    FROM base\n    GROUP BY wallet\n),\nscored AS (\n    SELECT\n        metrics.*,\n        CASE WHEN total_cost_basis_usd = 0 THEN 0 ELSE total_pnl_usd / total_cost_basis_usd * 100 END AS roi_pct,\n        CASE\n            WHEN profitable_positions + losing_positions = 0 THEN 0\n            ELSE profitable_positions::numeric / (profitable_positions + losing_positions) * 100\n        END AS win_rate_pct\n    FROM metrics\n)\nSELECT\n    wallet,\n    positions_count,\n    open_positions,\n    closed_positions,\n    profitable_positions,\n    losing_positions,\n    markets_traded,\n    outcomes_traded,\n    volume_usd,\n    open_initial_value_usd,\n    open_current_value_usd,\n    open_cash_pnl_usd,\n    open_realized_pnl_usd,\n    closed_cost_basis_usd,\n    closed_realized_pnl_usd,\n    total_cost_basis_usd,\n    total_current_value_usd,\n    total_pnl_usd,\n    realized_pnl_usd,\n    roi_pct,\n    win_rate_pct,\n    average_position_size_usd,\n    (\n        total_pnl_usd +\n        roi_pct * 2 +\n        profitable_positions * 5 +\n        ln(volume_usd + 1) +\n        LEAST(positions_count, 50) * 2 -\n        open_current_value_usd * 0.02 -\n        CASE WHEN positions_count < 5 THEN (5 - positions_count) * 10 ELSE 0 END\n    )::numeric AS score,\n    CASE\n        WHEN positions_count >= 50 AND volume_usd >= 1000 THEN 'High'\n        WHEN positions_count >= 20 THEN 'Medium'\n        WHEN positions_count >= 5 THEN 'Low'\n        ELSE 'Thin'\n    END AS sample_quality,\n    last_position_timestamp_utc,\n    now()\nFROM scored;"))
				{
					command2.Transaction = transaction;
					command2.Parameters.AddWithValue("Wallet", wallet);
					walletPerformanceRowsUpserted = await command2.ExecuteNonQueryAsync(cancellationToken);
				}
				await using (NpgsqlCommand updateWalletPolymarketPnlCommand = CreateCommand(connection, """
UPDATE polymarket_data_api_wallet_performance
SET polymarket_positions_open_cash_pnl_usd = open_cash_pnl_usd,
    polymarket_positions_open_realized_pnl_usd = open_realized_pnl_usd,
    polymarket_positions_open_current_value_usd = open_current_value_usd,
    polymarket_positions_closed_realized_pnl_usd = closed_realized_pnl_usd,
    polymarket_positions_total_pnl_usd = total_pnl_usd,
    polymarket_positions_refreshed_at_utc = refreshed_at_utc
WHERE wallet = @Wallet;
"""))
				{
					updateWalletPolymarketPnlCommand.Transaction = transaction;
					updateWalletPolymarketPnlCommand.Parameters.AddWithValue("Wallet", wallet);
					await updateWalletPolymarketPnlCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				await using (NpgsqlCommand deleteCategoryPerformance = CreateCommand(connection, "DELETE FROM polymarket_data_api_wallet_category_performance WHERE wallet = @Wallet;"))
				{
					deleteCategoryPerformance.Transaction = transaction;
					deleteCategoryPerformance.Parameters.AddWithValue("Wallet", wallet);
					await deleteCategoryPerformance.ExecuteNonQueryAsync(cancellationToken);
				}
				int categoryPerformanceRowsUpserted;
				await using (NpgsqlCommand command3 = CreateCommand(connection, "INSERT INTO polymarket_data_api_wallet_category_performance (\n    wallet, category, positions_count, open_positions, closed_positions, profitable_positions,\n    losing_positions, markets_traded, outcomes_traded, volume_usd,\n    open_initial_value_usd, open_current_value_usd, open_cash_pnl_usd, open_realized_pnl_usd,\n    closed_cost_basis_usd, closed_realized_pnl_usd, total_cost_basis_usd, total_current_value_usd,\n    total_pnl_usd, realized_pnl_usd, roi_pct, win_rate_pct, average_position_size_usd,\n    score, sample_quality, last_position_timestamp_utc, refreshed_at_utc\n)\nWITH categorized AS (\n    SELECT\n        position.*,\n        COALESCE(NULLIF(position.category, ''), NULLIF(market.category, ''), 'unknown') AS resolved_category\n    FROM polymarket_data_api_positions position\n    LEFT JOIN LATERAL (\n        SELECT gamma.category\n        FROM polymarket_gamma_markets gamma\n        WHERE gamma.condition_id = position.condition_id\n          AND NULLIF(gamma.category, '') IS NOT NULL\n        ORDER BY gamma.fetched_at_utc DESC\n        LIMIT 1\n    ) market ON true\n    WHERE position.wallet = @Wallet\n),\nbase AS (\n    SELECT\n        wallet,\n        resolved_category AS category,\n        position_status,\n        condition_id,\n        asset_id,\n        CASE\n            WHEN position_status = 'Open' THEN COALESCE(initial_value_usd, total_bought * avg_price)\n            ELSE total_bought * avg_price\n        END AS cost_basis_usd,\n        CASE WHEN position_status = 'Open' THEN COALESCE(current_value_usd, 0) ELSE 0 END AS current_value_usd,\n        CASE\n            WHEN position_status = 'Open' THEN COALESCE(cash_pnl_usd, 0) + realized_pnl_usd\n            ELSE realized_pnl_usd\n        END AS position_pnl_usd,\n        realized_pnl_usd,\n        COALESCE(timestamp_utc, end_date_utc, updated_at_utc) AS activity_utc\n    FROM categorized\n),\nmetrics AS (\n    SELECT\n        wallet,\n        category,\n        COUNT(*)::integer AS positions_count,\n        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Closed')::integer AS closed_positions,\n        COUNT(*) FILTER (WHERE position_pnl_usd > 0)::integer AS profitable_positions,\n        COUNT(*) FILTER (WHERE position_pnl_usd < 0)::integer AS losing_positions,\n        COUNT(DISTINCT condition_id)::integer AS markets_traded,\n        COUNT(DISTINCT asset_id)::integer AS outcomes_traded,\n        COALESCE(SUM(cost_basis_usd), 0)::numeric AS volume_usd,\n        COALESCE(SUM(cost_basis_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_initial_value_usd,\n        COALESCE(SUM(current_value_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_current_value_usd,\n        COALESCE(SUM(position_pnl_usd - realized_pnl_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_cash_pnl_usd,\n        COALESCE(SUM(realized_pnl_usd) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_realized_pnl_usd,\n        COALESCE(SUM(cost_basis_usd) FILTER (WHERE position_status = 'Closed'), 0)::numeric AS closed_cost_basis_usd,\n        COALESCE(SUM(realized_pnl_usd) FILTER (WHERE position_status = 'Closed'), 0)::numeric AS closed_realized_pnl_usd,\n        COALESCE(SUM(cost_basis_usd), 0)::numeric AS total_cost_basis_usd,\n        COALESCE(SUM(current_value_usd), 0)::numeric AS total_current_value_usd,\n        COALESCE(SUM(position_pnl_usd), 0)::numeric AS total_pnl_usd,\n        COALESCE(SUM(realized_pnl_usd), 0)::numeric AS realized_pnl_usd,\n        COALESCE(AVG(cost_basis_usd), 0)::numeric AS average_position_size_usd,\n        MAX(activity_utc) AS last_position_timestamp_utc\n    FROM base\n    GROUP BY wallet, category\n),\nscored AS (\n    SELECT\n        metrics.*,\n        CASE WHEN total_cost_basis_usd = 0 THEN 0 ELSE total_pnl_usd / total_cost_basis_usd * 100 END AS roi_pct,\n        CASE\n            WHEN profitable_positions + losing_positions = 0 THEN 0\n            ELSE profitable_positions::numeric / (profitable_positions + losing_positions) * 100\n        END AS win_rate_pct\n    FROM metrics\n)\nSELECT\n    wallet,\n    category,\n    positions_count,\n    open_positions,\n    closed_positions,\n    profitable_positions,\n    losing_positions,\n    markets_traded,\n    outcomes_traded,\n    volume_usd,\n    open_initial_value_usd,\n    open_current_value_usd,\n    open_cash_pnl_usd,\n    open_realized_pnl_usd,\n    closed_cost_basis_usd,\n    closed_realized_pnl_usd,\n    total_cost_basis_usd,\n    total_current_value_usd,\n    total_pnl_usd,\n    realized_pnl_usd,\n    roi_pct,\n    win_rate_pct,\n    average_position_size_usd,\n    (\n        total_pnl_usd +\n        roi_pct * 2 +\n        profitable_positions * 5 +\n        ln(volume_usd + 1) +\n        LEAST(positions_count, 50) * 2 -\n        open_current_value_usd * 0.02 -\n        CASE WHEN positions_count < 5 THEN (5 - positions_count) * 10 ELSE 0 END\n    )::numeric AS score,\n    CASE\n        WHEN positions_count >= 50 AND volume_usd >= 1000 THEN 'High'\n        WHEN positions_count >= 20 THEN 'Medium'\n        WHEN positions_count >= 5 THEN 'Low'\n        ELSE 'Thin'\n    END AS sample_quality,\n    last_position_timestamp_utc,\n    now()\nFROM scored;"))
				{
					command3.Transaction = transaction;
					command3.Parameters.AddWithValue("Wallet", wallet);
					categoryPerformanceRowsUpserted = await command3.ExecuteNonQueryAsync(cancellationToken);
				}
				await using (NpgsqlCommand updateCategoryPolymarketPnlCommand = CreateCommand(connection, """
UPDATE polymarket_data_api_wallet_category_performance
SET polymarket_positions_open_cash_pnl_usd = open_cash_pnl_usd,
    polymarket_positions_open_realized_pnl_usd = open_realized_pnl_usd,
    polymarket_positions_open_current_value_usd = open_current_value_usd,
    polymarket_positions_closed_realized_pnl_usd = closed_realized_pnl_usd,
    polymarket_positions_total_pnl_usd = total_pnl_usd,
    polymarket_positions_refreshed_at_utc = refreshed_at_utc
WHERE wallet = @Wallet;
"""))
				{
					updateCategoryPolymarketPnlCommand.Transaction = transaction;
					updateCategoryPolymarketPnlCommand.Parameters.AddWithValue("Wallet", wallet);
					await updateCategoryPolymarketPnlCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				await transaction.CommitAsync(cancellationToken);
				polymarketDataApiPerformanceRefreshResult = new PolymarketDataApiPerformanceRefreshResult(currentPositions.Count, closedPositions.Count, positionsUpserted, walletPerformanceRowsUpserted, categoryPerformanceRowsUpserted);
			}
			result = polymarketDataApiPerformanceRefreshResult;
		}
		return result;
	}

	public async Task<IReadOnlyList<string>> GetMissingPolymarketLeaderboardCategoryMappingsAsync(string wallet, int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<string> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<string> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT DISTINCT performance.category\nFROM polymarket_data_api_wallet_category_performance performance\nLEFT JOIN polymarket_category_mappings mapping\n  ON lower(mapping.local_category) = lower(performance.category)\n AND mapping.enabled\nWHERE performance.wallet = @Wallet\n  AND NULLIF(performance.category, '') IS NOT NULL\n  AND lower(performance.category) <> 'unknown'\n  AND mapping.local_category IS NULL\nORDER BY performance.category\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Wallet", wallet);
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<string> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<string> categories = new List<string>();
					while (await reader.ReadAsync(cancellationToken))
					{
						categories.Add(reader.GetString(0));
					}
					readOnlyList = categories;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketCategoryMapping>> GetEnabledPolymarketCategoryMappingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketCategoryMapping> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketCategoryMapping> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT local_category, polymarket_leaderboard_category\nFROM polymarket_category_mappings\nWHERE enabled\nORDER BY local_category;"))
			{
				IReadOnlyList<PolymarketCategoryMapping> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketCategoryMapping> mappings = new List<PolymarketCategoryMapping>();
					while (await reader.ReadAsync(cancellationToken))
					{
						mappings.Add(new PolymarketCategoryMapping(reader.GetString(0), reader.GetString(1)));
					}
					readOnlyList = mappings;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<int> UpsertPolymarketDataApiWalletCategoryRatingsAsync(IReadOnlyList<PolymarketDataApiWalletCategoryRating> ratings, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (ratings.Count == 0)
		{
			return 0;
		}
		int result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
			int rows = 0;
			foreach (PolymarketDataApiWalletCategoryRating rating in ratings)
			{
				await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_data_api_wallet_category_ratings (\n    wallet, local_category, polymarket_category, time_period, order_by, found,\n    leaderboard_rank, user_name, x_username, profile_image, verified_badge,\n    leaderboard_pnl_usd, leaderboard_volume_usd, leaderboard_pnl_to_volume_pct,\n    current_positions_count, current_positions_initial_value_usd,\n    current_positions_current_value_usd, current_positions_cash_pnl_usd,\n    current_positions_realized_pnl_usd, current_positions_total_pnl_usd,\n    current_positions_percent_pnl, current_positions_percent_realized_pnl,\n    closed_positions_count, closed_positions_cost_basis_usd,\n    closed_positions_realized_pnl_usd, closed_positions_percent_realized_pnl,\n    positions_total_cost_basis_usd, positions_total_pnl_usd,\n    positions_total_percent_pnl, positions_refreshed_at_utc,\n    raw_json, refreshed_at_utc, updated_at_utc\n) VALUES (\n    @Wallet, @LocalCategory, @PolymarketCategory, @TimePeriod, @OrderBy, @Found,\n    @LeaderboardRank, @UserName, @XUsername, @ProfileImage, @VerifiedBadge,\n    @LeaderboardPnlUsd, @LeaderboardVolumeUsd, @LeaderboardPnlToVolumePct,\n    @CurrentPositionsCount, @CurrentPositionsInitialValueUsd,\n    @CurrentPositionsCurrentValueUsd, @CurrentPositionsCashPnlUsd,\n    @CurrentPositionsRealizedPnlUsd, @CurrentPositionsTotalPnlUsd,\n    @CurrentPositionsPercentPnl, @CurrentPositionsPercentRealizedPnl,\n    @ClosedPositionsCount, @ClosedPositionsCostBasisUsd,\n    @ClosedPositionsRealizedPnlUsd, @ClosedPositionsPercentRealizedPnl,\n    @PositionsTotalCostBasisUsd, @PositionsTotalPnlUsd,\n    @PositionsTotalPercentPnl, @PositionsRefreshedAtUtc,\n    CAST(@RawJson AS jsonb), @RefreshedAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (wallet, local_category, polymarket_category, time_period, order_by) DO UPDATE SET\n    found = excluded.found,\n    leaderboard_rank = excluded.leaderboard_rank,\n    user_name = excluded.user_name,\n    x_username = excluded.x_username,\n    profile_image = excluded.profile_image,\n    verified_badge = excluded.verified_badge,\n    leaderboard_pnl_usd = excluded.leaderboard_pnl_usd,\n    leaderboard_volume_usd = excluded.leaderboard_volume_usd,\n    leaderboard_pnl_to_volume_pct = excluded.leaderboard_pnl_to_volume_pct,\n    current_positions_count = excluded.current_positions_count,\n    current_positions_initial_value_usd = excluded.current_positions_initial_value_usd,\n    current_positions_current_value_usd = excluded.current_positions_current_value_usd,\n    current_positions_cash_pnl_usd = excluded.current_positions_cash_pnl_usd,\n    current_positions_realized_pnl_usd = excluded.current_positions_realized_pnl_usd,\n    current_positions_total_pnl_usd = excluded.current_positions_total_pnl_usd,\n    current_positions_percent_pnl = excluded.current_positions_percent_pnl,\n    current_positions_percent_realized_pnl = excluded.current_positions_percent_realized_pnl,\n    closed_positions_count = excluded.closed_positions_count,\n    closed_positions_cost_basis_usd = excluded.closed_positions_cost_basis_usd,\n    closed_positions_realized_pnl_usd = excluded.closed_positions_realized_pnl_usd,\n    closed_positions_percent_realized_pnl = excluded.closed_positions_percent_realized_pnl,\n    positions_total_cost_basis_usd = excluded.positions_total_cost_basis_usd,\n    positions_total_pnl_usd = excluded.positions_total_pnl_usd,\n    positions_total_percent_pnl = excluded.positions_total_percent_pnl,\n    positions_refreshed_at_utc = excluded.positions_refreshed_at_utc,\n    raw_json = excluded.raw_json,\n    refreshed_at_utc = excluded.refreshed_at_utc,\n    updated_at_utc = excluded.updated_at_utc;");
				command.Transaction = transaction;
				AddPolymarketDataApiWalletCategoryRatingParameters(command, rating);
				rows += await command.ExecuteNonQueryAsync(cancellationToken);
			}
			await transaction.CommitAsync(cancellationToken);
			result = rows;
		}
		return result;
	}

	public async Task MarkPolymarketDataApiTraderRatingRefreshedAsync(string wallet, DateTimeOffset refreshedAtUtc, DateTimeOffset nextRefreshAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_data_api_traders\nSET polymarket_rating_refreshed_at_utc = @RefreshedAtUtc,\n    polymarket_rating_next_refresh_at_utc = @NextRefreshAtUtc,\n    polymarket_rating_refresh_attempts = 0,\n    polymarket_rating_last_error = NULL,\n    updated_at_utc = @RefreshedAtUtc\nWHERE wallet = @Wallet;");
		command.Parameters.AddWithValue("Wallet", wallet);
		command.Parameters.Add("RefreshedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(refreshedAtUtc);
		command.Parameters.Add("NextRefreshAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(nextRefreshAtUtc);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task MarkPolymarketDataApiTraderRatingRefreshFailedAsync(string wallet, string errorMessage, DateTimeOffset nextRefreshAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_data_api_traders\nSET polymarket_rating_next_refresh_at_utc = @NextRefreshAtUtc,\n    polymarket_rating_refresh_attempts = polymarket_rating_refresh_attempts + 1,\n    polymarket_rating_last_error = @ErrorMessage,\n    updated_at_utc = @UpdatedAtUtc\nWHERE wallet = @Wallet;");
		command.Parameters.AddWithValue("Wallet", wallet);
		command.Parameters.Add("NextRefreshAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(nextRefreshAtUtc);
		command.Parameters.AddWithValue("ErrorMessage", errorMessage.Length > 2_000 ? errorMessage[..2_000] : errorMessage);
		command.Parameters.AddWithValue("UpdatedAtUtc", DateTime.UtcNow);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpsertPolymarketGammaMarketAsync(PolymarketGammaMarket market, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_gamma_markets (\n    market_id, condition_id, question_id, slug, question, event_id, event_slug, event_title,\n    series_slug, category, active, closed, archived, restricted, accepting_orders, enable_order_book,\n    negative_risk, liquidity, liquidity_clob, volume, volume_24hr, best_bid, best_ask, spread,\n    last_trade_price, order_min_size, order_price_min_tick_size,\n    created_at_utc, updated_at_utc, start_date_utc, end_date_utc, event_start_time_utc,\n    outcomes_json, clob_token_ids_json, raw_json, fetched_at_utc\n) VALUES (\n    @MarketId, @ConditionId, @QuestionId, @Slug, @Question, @EventId, @EventSlug, @EventTitle,\n    @SeriesSlug, @Category, @Active, @Closed, @Archived, @Restricted, @AcceptingOrders, @EnableOrderBook,\n    @NegativeRisk, @Liquidity, @LiquidityClob, @Volume, @Volume24Hr, @BestBid, @BestAsk, @Spread,\n    @LastTradePrice, @OrderMinSize, @OrderPriceMinTickSize,\n    @CreatedAtUtc, @UpdatedAtUtc, @StartDateUtc, @EndDateUtc, @EventStartTimeUtc,\n    CAST(@OutcomesJson AS jsonb), CAST(@ClobTokenIdsJson AS jsonb), CAST(@RawJson AS jsonb), @FetchedAtUtc\n)\nON CONFLICT (market_id) DO UPDATE SET\n    condition_id = excluded.condition_id,\n    question_id = excluded.question_id,\n    slug = excluded.slug,\n    question = excluded.question,\n    event_id = excluded.event_id,\n    event_slug = excluded.event_slug,\n    event_title = excluded.event_title,\n    series_slug = excluded.series_slug,\n    category = excluded.category,\n    active = excluded.active,\n    closed = excluded.closed,\n    archived = excluded.archived,\n    restricted = excluded.restricted,\n    accepting_orders = excluded.accepting_orders,\n    enable_order_book = excluded.enable_order_book,\n    negative_risk = excluded.negative_risk,\n    liquidity = excluded.liquidity,\n    liquidity_clob = excluded.liquidity_clob,\n    volume = excluded.volume,\n    volume_24hr = excluded.volume_24hr,\n    best_bid = excluded.best_bid,\n    best_ask = excluded.best_ask,\n    spread = excluded.spread,\n    last_trade_price = excluded.last_trade_price,\n    order_min_size = excluded.order_min_size,\n    order_price_min_tick_size = excluded.order_price_min_tick_size,\n    created_at_utc = excluded.created_at_utc,\n    updated_at_utc = excluded.updated_at_utc,\n    start_date_utc = excluded.start_date_utc,\n    end_date_utc = excluded.end_date_utc,\n    event_start_time_utc = excluded.event_start_time_utc,\n    outcomes_json = excluded.outcomes_json,\n    clob_token_ids_json = excluded.clob_token_ids_json,\n    raw_json = excluded.raw_json,\n    fetched_at_utc = excluded.fetched_at_utc\nWHERE\n    polymarket_gamma_markets.condition_id IS DISTINCT FROM excluded.condition_id\n    OR polymarket_gamma_markets.question_id IS DISTINCT FROM excluded.question_id\n    OR polymarket_gamma_markets.slug IS DISTINCT FROM excluded.slug\n    OR polymarket_gamma_markets.question IS DISTINCT FROM excluded.question\n    OR polymarket_gamma_markets.event_id IS DISTINCT FROM excluded.event_id\n    OR polymarket_gamma_markets.event_slug IS DISTINCT FROM excluded.event_slug\n    OR polymarket_gamma_markets.event_title IS DISTINCT FROM excluded.event_title\n    OR polymarket_gamma_markets.series_slug IS DISTINCT FROM excluded.series_slug\n    OR polymarket_gamma_markets.category IS DISTINCT FROM excluded.category\n    OR polymarket_gamma_markets.active IS DISTINCT FROM excluded.active\n    OR polymarket_gamma_markets.closed IS DISTINCT FROM excluded.closed\n    OR polymarket_gamma_markets.archived IS DISTINCT FROM excluded.archived\n    OR polymarket_gamma_markets.restricted IS DISTINCT FROM excluded.restricted\n    OR polymarket_gamma_markets.accepting_orders IS DISTINCT FROM excluded.accepting_orders\n    OR polymarket_gamma_markets.enable_order_book IS DISTINCT FROM excluded.enable_order_book\n    OR polymarket_gamma_markets.negative_risk IS DISTINCT FROM excluded.negative_risk\n    OR polymarket_gamma_markets.liquidity IS DISTINCT FROM excluded.liquidity\n    OR polymarket_gamma_markets.liquidity_clob IS DISTINCT FROM excluded.liquidity_clob\n    OR polymarket_gamma_markets.volume IS DISTINCT FROM excluded.volume\n    OR polymarket_gamma_markets.volume_24hr IS DISTINCT FROM excluded.volume_24hr\n    OR polymarket_gamma_markets.best_bid IS DISTINCT FROM excluded.best_bid\n    OR polymarket_gamma_markets.best_ask IS DISTINCT FROM excluded.best_ask\n    OR polymarket_gamma_markets.spread IS DISTINCT FROM excluded.spread\n    OR polymarket_gamma_markets.last_trade_price IS DISTINCT FROM excluded.last_trade_price\n    OR polymarket_gamma_markets.order_min_size IS DISTINCT FROM excluded.order_min_size\n    OR polymarket_gamma_markets.order_price_min_tick_size IS DISTINCT FROM excluded.order_price_min_tick_size\n    OR polymarket_gamma_markets.created_at_utc IS DISTINCT FROM excluded.created_at_utc\n    OR polymarket_gamma_markets.updated_at_utc IS DISTINCT FROM excluded.updated_at_utc\n    OR polymarket_gamma_markets.start_date_utc IS DISTINCT FROM excluded.start_date_utc\n    OR polymarket_gamma_markets.end_date_utc IS DISTINCT FROM excluded.end_date_utc\n    OR polymarket_gamma_markets.event_start_time_utc IS DISTINCT FROM excluded.event_start_time_utc\n    OR polymarket_gamma_markets.outcomes_json IS DISTINCT FROM excluded.outcomes_json\n    OR polymarket_gamma_markets.clob_token_ids_json IS DISTINCT FROM excluded.clob_token_ids_json\n    OR polymarket_gamma_markets.raw_json IS DISTINCT FROM excluded.raw_json;");
		AddPolymarketGammaMarketParameters(command, market);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDown5mGammaMarketsAsync(int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND (\n      lower(slug) ~ '^btc-updown-5m-[0-9]+$'\n      OR lower(COALESCE(event_slug, '')) ~ '^btc-updown-5m-[0-9]+$'\n      OR lower(COALESCE(series_slug, '')) = 'btc-up-or-down-5m'\n  )\n  AND (end_date_utc IS NULL OR end_date_utc >= now() - interval '1 hour')\nORDER BY COALESCE(event_start_time_utc, end_date_utc, created_at_utc) ASC NULLS LAST,\n         market_id ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("Limit", limit);
		List<PolymarketGammaMarket> results = new List<PolymarketGammaMarket>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPolymarketGammaMarket(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDownStrategyGammaMarketsAsync(int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND (\n      lower(slug) ~ '^btc-updown-(5m|15m|4h)-[0-9]+$'\n      OR lower(COALESCE(event_slug, '')) ~ '^btc-updown-(5m|15m|4h)-[0-9]+$'\n      OR lower(COALESCE(series_slug, '')) IN ('btc-up-or-down-5m', 'btc-up-or-down-15m', 'btc-up-or-down-hourly', 'btc-up-or-down-4h')\n  )\n  AND (end_date_utc IS NULL OR end_date_utc >= now() - interval '4 hours')\nORDER BY COALESCE(event_start_time_utc, end_date_utc, created_at_utc) ASC NULLS LAST,\n         market_id ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("Limit", limit);
		List<PolymarketGammaMarket> results = new List<PolymarketGammaMarket>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPolymarketGammaMarket(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<PolymarketGammaMarket>> GetCryptoUpDown5mGammaMarketsAsync(IReadOnlyCollection<string> assetSymbols, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToLowerInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedSymbols.Length == 0)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "WITH requested_assets AS (\n    SELECT unnest(@AssetSymbols::text[]) AS asset_symbol\n)\nSELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND EXISTS (\n      SELECT 1\n      FROM requested_assets asset\n      WHERE lower(slug) ~ ('^' || asset.asset_symbol || '-updown-5m-[0-9]+$')\n         OR lower(COALESCE(event_slug, '')) ~ ('^' || asset.asset_symbol || '-updown-5m-[0-9]+$')\n         OR lower(COALESCE(series_slug, '')) = asset.asset_symbol || '-up-or-down-5m'\n  )\n  AND (end_date_utc IS NULL OR end_date_utc >= now() - interval '1 hour')\nORDER BY COALESCE(event_start_time_utc, end_date_utc, created_at_utc) ASC NULLS LAST,\n         market_id ASC\nLIMIT @Limit;");
		command.Parameters.Add("AssetSymbols", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = normalizedSymbols;
		command.Parameters.AddWithValue("Limit", limit);
		List<PolymarketGammaMarket> results = [];
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPolymarketGammaMarket(reader));
		}

		return results;
	}

	public async Task<PolymarketGammaMarket?> GetPolymarketGammaMarketAsync(string marketId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE market_id = @MarketId\nLIMIT 1;");
		command.Parameters.AddWithValue("MarketId", marketId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPolymarketGammaMarket(reader) : null;
	}

	public async Task<bool> TryAddStrategyMarketPaperRunAsync(StrategyMarketPaperRun run, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO strategy_market_paper_runs (\n    id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc\n) VALUES (\n    @Id, @StrategyId, @MarketId, @ConditionId, @MarketSlug, @MarketTitle, @Category,\n    @MarketStartUtc, @MarketEndUtc, @DetectedAtUtc, @EntryDueAtUtc, @Status,\n    @SelectedAssetId, @SelectedOutcome, @EntryPrice, @StakeUsd, @SizeShares,\n    @SignalId, @PaperOrderId, @EnteredAtUtc, @SettlementPrice, @SettlementValueUsd,\n    @RealizedPnlUsd, @SettledAtUtc, @SkipReason, CAST(@SkipDiagnosticsJson AS jsonb), @CreatedAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (strategy_id, market_id) DO NOTHING;");
		AddStrategyMarketPaperRunParameters(command, run);
		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(Guid strategyId, string status, DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\n  AND entry_due_at_utc <= @DueBeforeUtc\nORDER BY entry_due_at_utc ASC, detected_at_utc ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyId", strategyId);
		command.Parameters.AddWithValue("Status", status);
		command.Parameters.Add("DueBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(dueBeforeUtc);
		command.Parameters.AddWithValue("Limit", limit);
		List<StrategyMarketPaperRun> results = new List<StrategyMarketPaperRun>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadStrategyMarketPaperRun(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(Guid strategyId, DateTimeOffset marketEndedBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\n  AND market_end_utc IS NOT NULL\n  AND market_end_utc <= @MarketEndedBeforeUtc\nORDER BY market_end_utc ASC, entered_at_utc ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyId", strategyId);
		command.Parameters.AddWithValue("Status", StrategyMarketPaperRunStatuses.Entered);
		command.Parameters.Add("MarketEndedBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(marketEndedBeforeUtc);
		command.Parameters.AddWithValue("Limit", limit);
		List<StrategyMarketPaperRun> results = new List<StrategyMarketPaperRun>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadStrategyMarketPaperRun(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetRecentStrategyMarketPaperRunsAsync(Guid strategyId, string status, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\nORDER BY COALESCE(settled_at_utc, entered_at_utc, updated_at_utc) DESC,\n         COALESCE(market_start_utc, entry_due_at_utc, detected_at_utc) DESC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyId", strategyId);
		command.Parameters.AddWithValue("Status", status);
		command.Parameters.AddWithValue("Limit", limit);
		List<StrategyMarketPaperRun> results = new List<StrategyMarketPaperRun>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadStrategyMarketPaperRun(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<BtcUpDown5mMarketResult>> GetRecentBtcUpDown5mMarketResultsAsync(int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (limit <= 0)
		{
			return Array.Empty<BtcUpDown5mMarketResult>();
		}

		var rowLimit = Math.Max(limit * Math.Max(StrategyIds.BtcUpDown5mVariants.Count, 1) * 2, limit);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT market_id, condition_id, market_slug, market_start_utc, market_end_utc,\n       selected_outcome, realized_pnl_usd, settled_at_utc\nFROM strategy_market_paper_runs\nWHERE status = @Status\n  AND selected_outcome IS NOT NULL\n  AND realized_pnl_usd IS NOT NULL\n  AND settled_at_utc IS NOT NULL\n  AND lower(market_slug) ~ '^btc-updown-5m-[0-9]+$'\nORDER BY COALESCE(market_start_utc, market_end_utc, settled_at_utc) DESC,\n         settled_at_utc DESC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("Status", StrategyMarketPaperRunStatuses.Settled);
		command.Parameters.AddWithValue("Limit", rowLimit);

		var rows = new List<BtcUpDown5mSettledRunRow>();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			rows.Add(new BtcUpDown5mSettledRunRow(
				reader.GetString(0),
				reader.GetString(1),
				reader.GetString(2),
				reader.IsDBNull(3) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(3)),
				reader.IsDBNull(4) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(4)),
				reader.GetString(5),
				reader.GetDecimal(6),
				DateTimeOffsetFromUtc(reader.GetDateTime(7))));
		}

		return rows
			.GroupBy(row => string.IsNullOrWhiteSpace(row.ConditionId) ? row.MarketId : row.ConditionId, StringComparer.OrdinalIgnoreCase)
			.Select(TryCreateBtcUpDown5mMarketResult)
			.Where(result => result is not null)
			.Select(result => result!)
			.OrderByDescending(result => result.MarketStartUtc ?? result.MarketEndUtc ?? result.SettledAtUtc)
			.ThenByDescending(result => result.SettledAtUtc)
			.Take(limit)
			.ToArray();
	}

	public async Task UpdateStrategyMarketPaperRunAsync(StrategyMarketPaperRun run, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE strategy_market_paper_runs\nSET strategy_id = @StrategyId,\n    market_id = @MarketId,\n    condition_id = @ConditionId,\n    market_slug = @MarketSlug,\n    market_title = @MarketTitle,\n    category = @Category,\n    market_start_utc = @MarketStartUtc,\n    market_end_utc = @MarketEndUtc,\n    detected_at_utc = @DetectedAtUtc,\n    entry_due_at_utc = @EntryDueAtUtc,\n    status = @Status,\n    selected_asset_id = @SelectedAssetId,\n    selected_outcome = @SelectedOutcome,\n    entry_price = @EntryPrice,\n    stake_usd = @StakeUsd,\n    size_shares = @SizeShares,\n    signal_id = @SignalId,\n    paper_order_id = @PaperOrderId,\n    entered_at_utc = @EnteredAtUtc,\n    settlement_price = @SettlementPrice,\n    settlement_value_usd = @SettlementValueUsd,\n    realized_pnl_usd = @RealizedPnlUsd,\n    settled_at_utc = @SettledAtUtc,\n    skip_reason = @SkipReason,\n    skip_diagnostics_json = CAST(@SkipDiagnosticsJson AS jsonb),\n    created_at_utc = @CreatedAtUtc,\n    updated_at_utc = @UpdatedAtUtc\nWHERE id = @Id;");
		AddStrategyMarketPaperRunParameters(command, run);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO signals (\n    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,\n    best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, decision,\n    accepted, proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json\n) VALUES (\n    @Id, @LeaderTradeId, @TraderWallet, @ConditionId, @AssetId, @Outcome, @LeaderPrice,\n    @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, @LagSeconds, @Score, @Decision,\n    @Accepted, @ProposedPaperPrice, @ProposedSizeShares, @ProposedNotionalUsd, @CreatedAtUtc, CAST(@RawContextJson AS jsonb)\n);");
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
		command.Parameters.AddWithValue("ProposedPaperPrice", ((object)signal.ProposedPaperPrice) ?? DBNull.Value);
		command.Parameters.AddWithValue("ProposedSizeShares", ((object)signal.ProposedSizeShares) ?? DBNull.Value);
		command.Parameters.AddWithValue("ProposedNotionalUsd", ((object)signal.ProposedNotionalUsd) ?? DBNull.Value);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(signal.CreatedAtUtc));
		command.Parameters.AddWithValue("RawContextJson", JsonSerializer.Serialize(signal));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<SignalSummary> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<SignalSummary> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT s.id, s.trader_wallet, s.condition_id, s.asset_id, s.outcome, s.leader_price,\n       s.best_bid, s.best_ask, s.spread_abs, s.spread_pct, s.lag_seconds, s.score,\n       s.accepted, s.decision, s.proposed_paper_price, s.proposed_size_shares,\n       s.proposed_notional_usd, s.created_at_utc,\n       COALESCE(string_agg(sr.reason_code, ',' ORDER BY sr.created_at_utc), '') AS reason_codes\nFROM signals s\nLEFT JOIN signal_rejections sr ON sr.signal_id = s.id\nGROUP BY s.id\nORDER BY s.created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<SignalSummary> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<SignalSummary> results = new List<SignalSummary>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new SignalSummary(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetDecimal(5), reader.IsDBNull(6) ? ((decimal?)null) : new decimal?(reader.GetDecimal(6)), reader.IsDBNull(7) ? ((decimal?)null) : new decimal?(reader.GetDecimal(7)), reader.IsDBNull(8) ? ((decimal?)null) : new decimal?(reader.GetDecimal(8)), reader.IsDBNull(9) ? ((decimal?)null) : new decimal?(reader.GetDecimal(9)), reader.IsDBNull(10) ? ((int?)null) : new int?(reader.GetInt32(10)), reader.GetInt32(11), reader.GetBoolean(12), reader.GetString(13), SplitReasonCodes(reader.GetString(18)), reader.IsDBNull(14) ? ((decimal?)null) : new decimal?(reader.GetDecimal(14)), reader.IsDBNull(15) ? ((decimal?)null) : new decimal?(reader.GetDecimal(15)), reader.IsDBNull(16) ? ((decimal?)null) : new decimal?(reader.GetDecimal(16)), DateTimeOffsetFromUtc(reader.GetDateTime(17))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO signal_rejections (id, signal_id, reason_code, reason_details, created_at_utc)\nVALUES (@Id, @SignalId, @ReasonCode, @ReasonDetails, @CreatedAtUtc);");
		command.Parameters.AddWithValue("Id", rejection.Id);
		command.Parameters.AddWithValue("SignalId", rejection.SignalId);
		command.Parameters.AddWithValue("ReasonCode", rejection.ReasonCode);
		command.Parameters.AddWithValue("ReasonDetails", rejection.ReasonDetails);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(rejection.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<SignalRejection> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<SignalRejection> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, signal_id, reason_code, reason_details, created_at_utc\nFROM signal_rejections\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<SignalRejection> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<SignalRejection> results = new List<SignalRejection>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new SignalRejection(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), DateTimeOffsetFromUtc(reader.GetDateTime(4))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO paper_orders (\n    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json, correlation_id, execution_source\n) VALUES (\n    @Id, @SignalId, @StrategyId, @CopiedTraderWallet, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,\n    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc, CAST(@RawDecisionJson AS jsonb), @CorrelationId, @ExecutionSource\n);");
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("SignalId", order.SignalId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("CopiedTraderWallet", order.CopiedTraderWallet);
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
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? filledAtUtc = order.FilledAtUtc;
		object value;
		if (filledAtUtc.HasValue)
		{
			DateTimeOffset filledAt = filledAtUtc.GetValueOrDefault();
			value = UtcDateTime(filledAt);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("FilledAtUtc", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		filledAtUtc = order.CancelledAtUtc;
		object value2;
		if (filledAtUtc.HasValue)
		{
			DateTimeOffset cancelledAt = filledAtUtc.GetValueOrDefault();
			value2 = UtcDateTime(cancelledAt);
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("CancelledAtUtc", value2);
		command.Parameters.AddWithValue("RawDecisionJson", BuildPaperOrderRawDecisionJson(order));
		command.Parameters.AddWithValue("CorrelationId", ((object)order.CorrelationId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource ?? string.Empty);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE paper_orders\nSET status = @Status,\n    strategy_id = @StrategyId,\n    filled_at_utc = @FilledAtUtc,\n    cancelled_at_utc = @CancelledAtUtc,\n    raw_decision_json = CAST(@RawDecisionJson AS jsonb),\n    correlation_id = @CorrelationId,\n    execution_source = @ExecutionSource\nWHERE id = @Id;");
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("Status", order.Status.ToString());
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? filledAtUtc = order.FilledAtUtc;
		object value;
		if (filledAtUtc.HasValue)
		{
			DateTimeOffset filledAt = filledAtUtc.GetValueOrDefault();
			value = UtcDateTime(filledAt);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("FilledAtUtc", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		filledAtUtc = order.CancelledAtUtc;
		object value2;
		if (filledAtUtc.HasValue)
		{
			DateTimeOffset cancelledAt = filledAtUtc.GetValueOrDefault();
			value2 = UtcDateTime(cancelledAt);
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("CancelledAtUtc", value2);
		command.Parameters.AddWithValue("RawDecisionJson", BuildPaperOrderRawDecisionJson(order));
		command.Parameters.AddWithValue("CorrelationId", ((object)order.CorrelationId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource ?? string.Empty);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nWHERE status IN ('Pending', 'PartiallyFilled')\nORDER BY created_at_utc DESC;"))
			{
				IReadOnlyList<PaperOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperOrder> results = new List<PaperOrder>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPaperOrder(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<PaperOrder?> GetPaperOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nWHERE id = @Id\nLIMIT 1;");
		command.Parameters.AddWithValue("Id", paperOrderId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperOrder(reader) : null;
	}

	public async Task<PaperOrder?> GetPaperOrderByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nWHERE correlation_id = @CorrelationId\nORDER BY created_at_utc DESC\nLIMIT 1;");
		command.Parameters.AddWithValue("CorrelationId", correlationId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperOrder(reader) : null;
	}

	public async Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PaperOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperOrder> results = new List<PaperOrder>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPaperOrder(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO paper_fills (id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)\nVALUES (@Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, @Evidence, @RealizedPnlUsd);");
		command.Parameters.AddWithValue("Id", fill.Id);
		command.Parameters.AddWithValue("PaperOrderId", fill.PaperOrderId);
		command.Parameters.AddWithValue("Price", fill.Price);
		command.Parameters.AddWithValue("SizeShares", fill.SizeShares);
		command.Parameters.AddWithValue("FilledAtUtc", UtcDateTime(fill.FilledAtUtc));
		command.Parameters.AddWithValue("Evidence", fill.Evidence);
		command.Parameters.AddWithValue("RealizedPnlUsd", fill.RealizedPnlUsd);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperFill> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperFill> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd\nFROM paper_fills\nORDER BY filled_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PaperFill> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperFill> results = new List<PaperFill>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPaperFill(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PaperFill>> GetPaperFillsForOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperFill> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperFill> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd\nFROM paper_fills\nWHERE paper_order_id = @PaperOrderId\nORDER BY filled_at_utc ASC, id ASC;"))
			{
				command.Parameters.AddWithValue("PaperOrderId", paperOrderId);
				IReadOnlyList<PaperFill> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperFill> results = new List<PaperFill>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPaperFill(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO paper_positions (\n    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,\n    estimated_value_usd, unrealized_pnl_usd, updated_at_utc\n) VALUES (\n    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome, @SizeShares, @AveragePrice,\n    @EstimatedValueUsd, @UnrealizedPnlUsd, @UpdatedAtUtc\n)\nON CONFLICT (copied_trader_wallet, asset_id) DO UPDATE SET\n    condition_id = excluded.condition_id,\n    outcome = excluded.outcome,\n    size_shares = excluded.size_shares,\n    average_price = excluded.average_price,\n    estimated_value_usd = excluded.estimated_value_usd,\n    unrealized_pnl_usd = excluded.unrealized_pnl_usd,\n    updated_at_utc = excluded.updated_at_utc;");
		command.Parameters.AddWithValue("Id", Guid.NewGuid());
		command.Parameters.AddWithValue("CopiedTraderWallet", position.CopiedTraderWallet);
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

	public async Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperPosition> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperPosition> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc, copied_trader_wallet\nFROM paper_positions\nORDER BY updated_at_utc DESC;"))
			{
				IReadOnlyList<PaperPosition> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperPosition> results = new List<PaperPosition>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new PaperPosition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), DateTimeOffsetFromUtc(reader.GetDateTime(7)), reader.GetString(8)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<bool> TryAddPaperPositionSettlementAsync(PaperPositionSettlement settlement, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
    category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc
) VALUES (
    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome, @WinningAssetId, @WinningOutcome,
    @Category, @SettledSizeShares, @AveragePrice, @CostBasisUsd, @SettlementValueUsd,
    @RealizedPnlUsd, @Won, @SettlementSource, @SettledAtUtc, @CreatedAtUtc
)
ON CONFLICT (copied_trader_wallet, asset_id) DO NOTHING
RETURNING 1;
""");
		command.Parameters.AddWithValue("Id", settlement.Id);
		command.Parameters.AddWithValue("CopiedTraderWallet", settlement.CopiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", settlement.AssetId);
		command.Parameters.AddWithValue("ConditionId", settlement.ConditionId);
		command.Parameters.AddWithValue("Outcome", settlement.Outcome);
		command.Parameters.AddWithValue("WinningAssetId", ((object?)settlement.WinningAssetId) ?? DBNull.Value);
		command.Parameters.AddWithValue("WinningOutcome", settlement.WinningOutcome);
		command.Parameters.AddWithValue("Category", ((object?)settlement.Category) ?? DBNull.Value);
		command.Parameters.AddWithValue("SettledSizeShares", settlement.SettledSizeShares);
		command.Parameters.AddWithValue("AveragePrice", settlement.AveragePrice);
		command.Parameters.AddWithValue("CostBasisUsd", settlement.CostBasisUsd);
		command.Parameters.AddWithValue("SettlementValueUsd", settlement.SettlementValueUsd);
		command.Parameters.AddWithValue("RealizedPnlUsd", settlement.RealizedPnlUsd);
		command.Parameters.AddWithValue("Won", settlement.Won);
		command.Parameters.AddWithValue("SettlementSource", settlement.SettlementSource);
		command.Parameters.AddWithValue("SettledAtUtc", UtcDateTime(settlement.SettledAtUtc));
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(settlement.CreatedAtUtc));
		return await command.ExecuteScalarAsync(cancellationToken) is not null;
	}

	public async Task<IReadOnlyList<PaperPositionSettlement>> GetRecentPaperPositionSettlementsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
       category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
       realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc
FROM paper_position_settlements
ORDER BY settled_at_utc DESC, created_at_utc DESC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperPositionSettlement> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new PaperPositionSettlement(
				reader.GetGuid(0),
				reader.GetString(1),
				reader.GetString(2),
				reader.GetString(3),
				reader.GetString(4),
				reader.IsDBNull(5) ? null : reader.GetString(5),
				reader.GetString(6),
				reader.IsDBNull(7) ? null : reader.GetString(7),
				reader.GetDecimal(8),
				reader.GetDecimal(9),
				reader.GetDecimal(10),
				reader.GetDecimal(11),
				reader.GetDecimal(12),
				reader.GetBoolean(13),
				reader.GetString(14),
				DateTimeOffsetFromUtc(reader.GetDateTime(15)),
				DateTimeOffsetFromUtc(reader.GetDateTime(16))));
		}

		return results;
	}

	public async Task<int> RefreshPaperCopiedTraderPerformanceAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await AcquirePaperCopiedTraderPerformanceRefreshLockAsync(connection, transaction, cancellationToken);
		await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM paper_copied_trader_performance;"))
		{
			deleteCommand.Transaction = transaction;
			await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
		}
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH event_rows AS (
    SELECT
        po.copied_trader_wallet,
        COALESCE(NULLIF(gm.category, ''), 'unknown') AS category,
        1::integer AS orders_count,
        CASE WHEN po.status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired') THEN 1 ELSE 0 END::integer AS filled_orders_count,
        0::integer AS buy_fills_count,
        0::integer AS sell_fills_count,
        0::integer AS open_positions_count,
        0::integer AS settled_positions_count,
        0::integer AS won_positions_count,
        0::integer AS lost_positions_count,
        0::numeric AS buy_cost_usd,
        0::numeric AS sell_proceeds_usd,
        0::numeric AS settlement_value_usd,
        0::numeric AS realized_pnl_usd,
        0::numeric AS unrealized_pnl_usd,
        po.created_at_utc AS first_order_utc,
        po.created_at_utc AS last_order_utc
    FROM paper_orders po
    LEFT JOIN polymarket_gamma_markets gm ON gm.condition_id = po.condition_id
    WHERE po.copied_trader_wallet <> ''

    UNION ALL

    SELECT
        po.copied_trader_wallet,
        COALESCE(NULLIF(gm.category, ''), 'unknown') AS category,
        0, 0,
        CASE WHEN po.side = 'Buy' THEN 1 ELSE 0 END,
        CASE WHEN po.side = 'Sell' THEN 1 ELSE 0 END,
        0, 0, 0, 0,
        CASE WHEN po.side = 'Buy' THEN pf.price * pf.size_shares ELSE 0 END,
        CASE WHEN po.side = 'Sell' THEN pf.price * pf.size_shares ELSE 0 END,
        0,
        pf.realized_pnl_usd,
        0,
        po.created_at_utc,
        po.created_at_utc
    FROM paper_fills pf
    JOIN paper_orders po ON po.id = pf.paper_order_id
    LEFT JOIN polymarket_gamma_markets gm ON gm.condition_id = po.condition_id
    WHERE po.copied_trader_wallet <> ''

    UNION ALL

    SELECT
        pp.copied_trader_wallet,
        COALESCE(NULLIF(gm.category, ''), 'unknown') AS category,
        0, 0, 0, 0,
        CASE WHEN pp.size_shares > 0 THEN 1 ELSE 0 END,
        0, 0, 0,
        0, 0, 0, 0,
        pp.unrealized_pnl_usd,
        NULL::timestamptz,
        NULL::timestamptz
    FROM paper_positions pp
    LEFT JOIN polymarket_gamma_markets gm ON gm.condition_id = pp.condition_id
    WHERE pp.copied_trader_wallet <> ''

    UNION ALL

    SELECT
        ps.copied_trader_wallet,
        COALESCE(NULLIF(ps.category, ''), NULLIF(gm.category, ''), 'unknown') AS category,
        0, 0, 0, 0, 0,
        1,
        CASE WHEN ps.won THEN 1 ELSE 0 END,
        CASE WHEN ps.won THEN 0 ELSE 1 END,
        0, 0,
        ps.settlement_value_usd,
        ps.realized_pnl_usd,
        0,
        NULL::timestamptz,
        NULL::timestamptz
    FROM paper_position_settlements ps
    LEFT JOIN polymarket_gamma_markets gm ON gm.condition_id = ps.condition_id
    WHERE ps.copied_trader_wallet <> ''
),
grouped AS (
    SELECT copied_trader_wallet, category,
           SUM(orders_count)::integer AS orders_count,
           SUM(filled_orders_count)::integer AS filled_orders_count,
           SUM(buy_fills_count)::integer AS buy_fills_count,
           SUM(sell_fills_count)::integer AS sell_fills_count,
           SUM(open_positions_count)::integer AS open_positions_count,
           SUM(settled_positions_count)::integer AS settled_positions_count,
           SUM(won_positions_count)::integer AS won_positions_count,
           SUM(lost_positions_count)::integer AS lost_positions_count,
           SUM(buy_cost_usd) AS buy_cost_usd,
           SUM(sell_proceeds_usd) AS sell_proceeds_usd,
           SUM(settlement_value_usd) AS settlement_value_usd,
           SUM(realized_pnl_usd) AS realized_pnl_usd,
           SUM(unrealized_pnl_usd) AS unrealized_pnl_usd,
           MIN(first_order_utc) AS first_order_utc,
           MAX(last_order_utc) AS last_order_utc
    FROM event_rows
    GROUP BY copied_trader_wallet, category

    UNION ALL

    SELECT copied_trader_wallet, 'OVERALL',
           SUM(orders_count)::integer,
           SUM(filled_orders_count)::integer,
           SUM(buy_fills_count)::integer,
           SUM(sell_fills_count)::integer,
           SUM(open_positions_count)::integer,
           SUM(settled_positions_count)::integer,
           SUM(won_positions_count)::integer,
           SUM(lost_positions_count)::integer,
           SUM(buy_cost_usd),
           SUM(sell_proceeds_usd),
           SUM(settlement_value_usd),
           SUM(realized_pnl_usd),
           SUM(unrealized_pnl_usd),
           MIN(first_order_utc),
           MAX(last_order_utc)
    FROM event_rows
    GROUP BY copied_trader_wallet
),
scored AS (
    SELECT *,
           realized_pnl_usd + unrealized_pnl_usd AS total_pnl_usd,
           CASE WHEN buy_cost_usd = 0 THEN 0 ELSE (realized_pnl_usd + unrealized_pnl_usd) / buy_cost_usd * 100 END AS roi_pct,
           CASE WHEN settled_positions_count = 0 THEN 0 ELSE won_positions_count::numeric / settled_positions_count * 100 END AS win_rate_pct
    FROM grouped
),
inserted AS (
    INSERT INTO paper_copied_trader_performance (
        copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
        sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
        lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
        realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
        score, first_order_utc, last_order_utc, refreshed_at_utc
    )
    SELECT
        copied_trader_wallet,
        category,
        orders_count,
        filled_orders_count,
        buy_fills_count,
        sell_fills_count,
        open_positions_count,
        settled_positions_count,
        won_positions_count,
        lost_positions_count,
        buy_cost_usd,
        sell_proceeds_usd,
        settlement_value_usd,
        realized_pnl_usd,
        unrealized_pnl_usd,
        total_pnl_usd,
        roi_pct,
        win_rate_pct,
        greatest(0, least(100,
            50
            + greatest(-50, least(50, roi_pct)) * 0.35
            + (win_rate_pct - 50) * 0.25
            + greatest(-20, least(20, total_pnl_usd)) * 1.25
            + least(settled_positions_count, 20) * 0.5
            - lost_positions_count * 1.25
            - open_positions_count * 0.1
        )) AS score,
        first_order_utc,
        last_order_utc,
        now()
    FROM scored
    RETURNING 1
)
SELECT count(*)::integer FROM inserted;
""");
		command.Transaction = transaction;
		int rows = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
		await transaction.CommitAsync(cancellationToken);
		return rows;
	}

	public async Task<IReadOnlyList<PaperCopiedTraderPerformance>> GetPaperCopiedTraderPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
       sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
       lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
       realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
       score, first_order_utc, last_order_utc, refreshed_at_utc
FROM paper_copied_trader_performance
ORDER BY category = 'OVERALL' DESC, score DESC, total_pnl_usd DESC, copied_trader_wallet
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperCopiedTraderPerformance> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperCopiedTraderPerformance(reader));
		}

		return results;
	}

	public async Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync(
		string copiedTraderWallet,
		string category,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT copied_trader_wallet, category, orders_count, filled_orders_count, buy_fills_count,
       sell_fills_count, open_positions_count, settled_positions_count, won_positions_count,
       lost_positions_count, buy_cost_usd, sell_proceeds_usd, settlement_value_usd,
       realized_pnl_usd, unrealized_pnl_usd, total_pnl_usd, roi_pct, win_rate_pct,
       score, first_order_utc, last_order_utc, refreshed_at_utc
FROM paper_copied_trader_performance
WHERE lower(copied_trader_wallet) = lower(@CopiedTraderWallet)
  AND lower(category) = lower(@Category)
LIMIT 1;
""");
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
		command.Parameters.AddWithValue("Category", category);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperCopiedTraderPerformance(reader) : null;
	}

	public async Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(int limit = 1000, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH order_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS orders_count,
        (count(*) FILTER (WHERE status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired')))::integer AS filled_orders_count,
        (count(*) FILTER (WHERE status IN ('Pending', 'PartiallyFilled')))::integer AS open_orders_count,
        COALESCE(sum(notional_usd) FILTER (WHERE side = 'Buy' AND status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired')), 0) AS buy_notional_usd,
        max(created_at_utc) AS last_order_utc
    FROM paper_orders
    GROUP BY strategy_id
),
fill_agg AS (
    SELECT
        paper_order.strategy_id,
        COALESCE(sum(fill_row.realized_pnl_usd), 0) AS realized_fill_pnl_usd,
        COALESCE(sum((fill_row.price * fill_row.size_shares) - fill_row.realized_pnl_usd) FILTER (WHERE paper_order.side = 'Sell'), 0) AS closed_fill_cost_basis_usd
    FROM paper_fills fill_row
    INNER JOIN paper_orders paper_order ON paper_order.id = fill_row.paper_order_id
    GROUP BY paper_order.strategy_id
),
position_mapped AS (
    SELECT
        CASE
            WHEN lower(position_row.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
            ELSE @FollowLeaderStrategyId
        END AS strategy_id,
        position_row.unrealized_pnl_usd
    FROM paper_positions position_row
    LEFT JOIN strategies strategy_by_wallet
        ON lower(position_row.copied_trader_wallet) = lower('strategy:' || strategy_by_wallet.code)
    WHERE position_row.size_shares > 0
),
position_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS open_positions_count,
        COALESCE(sum(unrealized_pnl_usd), 0) AS unrealized_pnl_usd
    FROM position_mapped
    WHERE strategy_id IS NOT NULL
    GROUP BY strategy_id
),
settlement_mapped AS (
    SELECT
        CASE
            WHEN lower(settlement_row.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
            ELSE @FollowLeaderStrategyId
        END AS strategy_id,
        settlement_row.cost_basis_usd,
        settlement_row.realized_pnl_usd,
        settlement_row.won
    FROM paper_position_settlements settlement_row
    LEFT JOIN strategies strategy_by_wallet
        ON lower(settlement_row.copied_trader_wallet) = lower('strategy:' || strategy_by_wallet.code)
),
settlement_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS settled_positions_count,
        (count(*) FILTER (WHERE won))::integer AS won_positions_count,
        (count(*) FILTER (WHERE NOT won))::integer AS lost_positions_count,
        COALESCE(sum(cost_basis_usd), 0) AS cost_basis_usd,
        COALESCE(sum(realized_pnl_usd), 0) AS realized_pnl_usd,
        COALESCE(avg(realized_pnl_usd) FILTER (WHERE won), 0) AS avg_win_pnl_usd,
        COALESCE(avg(realized_pnl_usd) FILTER (WHERE NOT won), 0) AS avg_loss_pnl_usd,
        COALESCE(sum(realized_pnl_usd) FILTER (WHERE realized_pnl_usd > 0), 0) AS positive_pnl_usd,
        COALESCE(sum(-realized_pnl_usd) FILTER (WHERE realized_pnl_usd < 0), 0) AS loss_abs_pnl_usd,
        COALESCE(avg(realized_pnl_usd), 0) AS expectancy_pnl_usd
    FROM settlement_mapped
    WHERE strategy_id IS NOT NULL
    GROUP BY strategy_id
),
run_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS runs_count,
        (count(*) FILTER (WHERE status = 'Observed'))::integer AS observed_runs_count,
        (count(*) FILTER (WHERE status = 'Entered'))::integer AS entered_runs_count,
        (count(*) FILTER (WHERE status = 'Skipped'))::integer AS skipped_runs_count,
        (count(*) FILTER (WHERE status = 'Settled'))::integer AS settled_runs_count,
        (count(*) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) > 0))::integer AS won_runs_count,
        (count(*) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) < 0))::integer AS lost_runs_count,
        COALESCE(sum(stake_usd) FILTER (WHERE status = 'Settled'), 0) AS settled_stake_usd,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled'), 0) AS realized_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) > 0), 0) AS avg_win_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) < 0), 0) AS avg_loss_pnl_usd,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) > 0), 0) AS positive_pnl_usd,
        COALESCE(sum(-COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled' AND COALESCE(realized_pnl_usd, 0) < 0), 0) AS loss_abs_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled'), 0) AS expectancy_pnl_usd,
        COALESCE(avg(GREATEST(0, EXTRACT(EPOCH FROM (entered_at_utc - entry_due_at_utc)))) FILTER (WHERE entered_at_utc IS NOT NULL), 0)::numeric AS avg_entry_delay_seconds,
        COALESCE(max(GREATEST(0, EXTRACT(EPOCH FROM (entered_at_utc - entry_due_at_utc)))) FILTER (WHERE entered_at_utc IS NOT NULL), 0)::numeric AS max_entry_delay_seconds,
        max(updated_at_utc) AS last_run_utc
    FROM strategy_market_paper_runs
    GROUP BY strategy_id
),
live_order_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS live_orders_count,
        (count(*) FILTER (WHERE filled_size > 0))::integer AS live_filled_orders_count,
        (count(*) FILTER (WHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested') AND remaining_size > 0))::integer AS live_open_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND realized_pnl_usd IS NOT NULL))::integer AS live_settled_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)))::integer AS live_won_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)))::integer AS live_lost_orders_count,
        COALESCE(sum(CASE
            WHEN cost_basis_usd > 0 THEN cost_basis_usd
            WHEN filled_notional_usd > 0 THEN filled_notional_usd + fee_usd
            WHEN filled_size > 0 THEN price * filled_size + fee_usd
            ELSE 0
        END) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_stake_usd,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_realized_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (
            WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)
        ), 0) AS live_avg_win_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (
            WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)
        ), 0) AS live_avg_loss_pnl_usd,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) > 0), 0) AS live_positive_pnl_usd,
        COALESCE(sum(-COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(realized_pnl_usd, 0) < 0), 0) AS live_loss_abs_pnl_usd,
        COALESCE(avg(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE settled_at_utc IS NOT NULL), 0) AS live_expectancy_pnl_usd,
        max(created_at_utc) AS live_last_order_utc,
        max(settled_at_utc) AS live_last_settlement_utc
    FROM live_orders
    GROUP BY strategy_id
),
combined AS (
    SELECT
        strategy.id AS strategy_id,
        strategy.code,
        strategy.name,
        strategy.enabled,
        strategy.live_stakes,
        strategy.paper_stake_amount,
        strategy.live_stake_amount,
        strategy.live_available_balance,
        COALESCE(order_agg.orders_count, 0) AS orders_count,
        COALESCE(order_agg.filled_orders_count, 0) AS filled_orders_count,
        COALESCE(order_agg.open_orders_count, 0) AS open_orders_count,
        COALESCE(position_agg.open_positions_count, 0) AS open_positions_count,
        COALESCE(run_agg.observed_runs_count, 0) AS observed_runs_count,
        COALESCE(run_agg.entered_runs_count, 0) AS entered_runs_count,
        COALESCE(run_agg.skipped_runs_count, 0) AS skipped_runs_count,
        COALESCE(run_agg.settled_runs_count, 0) AS settled_runs_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.settled_runs_count, 0)
            ELSE COALESCE(settlement_agg.settled_positions_count, 0)
        END AS settled_positions_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.won_runs_count, 0)
            ELSE COALESCE(settlement_agg.won_positions_count, 0)
        END AS won_positions_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.lost_runs_count, 0)
            ELSE COALESCE(settlement_agg.lost_positions_count, 0)
        END AS lost_positions_count,
        CASE
            WHEN COALESCE(order_agg.buy_notional_usd, 0) > 0 THEN COALESCE(order_agg.buy_notional_usd, 0)
            WHEN COALESCE(run_agg.settled_stake_usd, 0) > 0 THEN COALESCE(run_agg.settled_stake_usd, 0)
            ELSE COALESCE(settlement_agg.cost_basis_usd, 0)
        END AS stake_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.settled_stake_usd, 0)
            ELSE COALESCE(settlement_agg.cost_basis_usd, 0)
        END + COALESCE(fill_agg.closed_fill_cost_basis_usd, 0) AS closed_stake_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.realized_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.realized_pnl_usd, 0)
        END + COALESCE(fill_agg.realized_fill_pnl_usd, 0) AS realized_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.avg_win_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.avg_win_pnl_usd, 0)
        END AS avg_win_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.avg_loss_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.avg_loss_pnl_usd, 0)
        END AS avg_loss_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.positive_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.positive_pnl_usd, 0)
        END AS closed_positive_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.loss_abs_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.loss_abs_pnl_usd, 0)
        END AS closed_loss_abs_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.expectancy_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.expectancy_pnl_usd, 0)
        END AS expectancy_pnl_usd,
        COALESCE(position_agg.unrealized_pnl_usd, 0) AS unrealized_pnl_usd,
        COALESCE(run_agg.avg_entry_delay_seconds, 0) AS avg_entry_delay_seconds,
        COALESCE(run_agg.max_entry_delay_seconds, 0) AS max_entry_delay_seconds,
        COALESCE(live_order_agg.live_orders_count, 0) AS live_orders_count,
        COALESCE(live_order_agg.live_filled_orders_count, 0) AS live_filled_orders_count,
        COALESCE(live_order_agg.live_open_orders_count, 0) AS live_open_orders_count,
        COALESCE(live_order_agg.live_settled_orders_count, 0) AS live_settled_orders_count,
        COALESCE(live_order_agg.live_won_orders_count, 0) AS live_won_orders_count,
        COALESCE(live_order_agg.live_lost_orders_count, 0) AS live_lost_orders_count,
        COALESCE(live_order_agg.live_stake_usd, 0) AS live_stake_usd,
        COALESCE(live_order_agg.live_realized_pnl_usd, 0) AS live_realized_pnl_usd,
        COALESCE(live_order_agg.live_avg_win_pnl_usd, 0) AS live_avg_win_pnl_usd,
        COALESCE(live_order_agg.live_avg_loss_pnl_usd, 0) AS live_avg_loss_pnl_usd,
        COALESCE(live_order_agg.live_positive_pnl_usd, 0) AS live_positive_pnl_usd,
        COALESCE(live_order_agg.live_loss_abs_pnl_usd, 0) AS live_loss_abs_pnl_usd,
        COALESCE(live_order_agg.live_expectancy_pnl_usd, 0) AS live_expectancy_pnl_usd,
        live_order_agg.live_last_order_utc,
        live_order_agg.live_last_settlement_utc,
        order_agg.last_order_utc,
        run_agg.last_run_utc
    FROM strategies strategy
    LEFT JOIN order_agg ON order_agg.strategy_id = strategy.id
    LEFT JOIN fill_agg ON fill_agg.strategy_id = strategy.id
    LEFT JOIN position_agg ON position_agg.strategy_id = strategy.id
    LEFT JOIN settlement_agg ON settlement_agg.strategy_id = strategy.id
    LEFT JOIN run_agg ON run_agg.strategy_id = strategy.id
    LEFT JOIN live_order_agg ON live_order_agg.strategy_id = strategy.id
)
SELECT
    strategy_id,
    code,
    name,
    enabled,
    live_stakes,
    paper_stake_amount,
    live_stake_amount,
    live_available_balance,
    orders_count,
    filled_orders_count,
    open_orders_count,
    open_positions_count,
    observed_runs_count,
    entered_runs_count,
    skipped_runs_count,
    settled_runs_count,
    settled_positions_count,
    won_positions_count,
    lost_positions_count,
    stake_usd,
    realized_pnl_usd,
    unrealized_pnl_usd,
    realized_pnl_usd + unrealized_pnl_usd AS total_pnl_usd,
    CASE WHEN settled_positions_count = 0 THEN 0 ELSE won_positions_count * 100.0 / settled_positions_count END AS win_rate_pct,
    CASE WHEN settled_positions_count = 0 THEN 0 ELSE lost_positions_count * 100.0 / settled_positions_count END AS loss_rate_pct,
    avg_win_pnl_usd,
    avg_loss_pnl_usd,
    CASE WHEN closed_loss_abs_pnl_usd = 0 THEN NULL ELSE closed_positive_pnl_usd / closed_loss_abs_pnl_usd END AS profit_factor,
    expectancy_pnl_usd,
    CASE WHEN stake_usd = 0 THEN 0 ELSE (realized_pnl_usd + unrealized_pnl_usd) * 100.0 / stake_usd END AS roi_pct,
    CASE WHEN closed_stake_usd = 0 THEN 0 ELSE realized_pnl_usd * 100.0 / closed_stake_usd END AS closed_roi_pct,
    avg_entry_delay_seconds,
    max_entry_delay_seconds,
    live_orders_count,
    live_filled_orders_count,
    live_open_orders_count,
    live_settled_orders_count,
    live_won_orders_count,
    live_lost_orders_count,
    live_stake_usd,
    live_realized_pnl_usd,
    CASE WHEN live_settled_orders_count = 0 THEN 0 ELSE live_won_orders_count * 100.0 / live_settled_orders_count END AS live_win_rate_pct,
    CASE WHEN live_settled_orders_count = 0 THEN 0 ELSE live_lost_orders_count * 100.0 / live_settled_orders_count END AS live_loss_rate_pct,
    live_avg_win_pnl_usd,
    live_avg_loss_pnl_usd,
    CASE WHEN live_loss_abs_pnl_usd = 0 THEN NULL ELSE live_positive_pnl_usd / live_loss_abs_pnl_usd END AS live_profit_factor,
    live_expectancy_pnl_usd,
    CASE WHEN live_stake_usd = 0 THEN 0 ELSE live_realized_pnl_usd * 100.0 / live_stake_usd END AS live_roi_pct,
    live_last_order_utc,
    live_last_settlement_utc,
    last_order_utc,
    last_run_utc
FROM combined
ORDER BY
    CASE WHEN code = 'follow_leader' THEN 0 ELSE 1 END,
    code
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<StrategyPerformance> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new StrategyPerformance(
				reader.GetGuid(0),
				reader.GetString(1),
				reader.GetString(2),
				reader.GetBoolean(3),
				reader.GetBoolean(4),
				reader.GetDecimal(5),
				reader.GetDecimal(6),
				reader.GetDecimal(7),
				reader.GetInt32(8),
				reader.GetInt32(9),
				reader.GetInt32(10),
				reader.GetInt32(11),
				reader.GetInt32(12),
				reader.GetInt32(13),
				reader.GetInt32(14),
				reader.GetInt32(15),
				reader.GetInt32(16),
				reader.GetInt32(17),
				reader.GetInt32(18),
				reader.GetDecimal(19),
				reader.GetDecimal(20),
				reader.GetDecimal(21),
				reader.GetDecimal(22),
				reader.GetDecimal(23),
				reader.GetDecimal(24),
				reader.GetDecimal(25),
				reader.GetDecimal(26),
				reader.IsDBNull(27) ? null : reader.GetDecimal(27),
				reader.GetDecimal(28),
				reader.GetDecimal(29),
				reader.GetDecimal(30),
				reader.GetDecimal(31),
				reader.GetDecimal(32),
				reader.GetInt32(33),
				reader.GetInt32(34),
				reader.GetInt32(35),
				reader.GetInt32(36),
				reader.GetInt32(37),
				reader.GetInt32(38),
				reader.GetDecimal(39),
				reader.GetDecimal(40),
				reader.GetDecimal(41),
				reader.GetDecimal(42),
				reader.GetDecimal(43),
				reader.GetDecimal(44),
				reader.IsDBNull(45) ? null : reader.GetDecimal(45),
				reader.GetDecimal(46),
				reader.GetDecimal(47),
				reader.IsDBNull(48) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(48)),
				reader.IsDBNull(49) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(49)),
				reader.IsDBNull(50) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(50)),
				reader.IsDBNull(51) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(51))));
		}

		return results;
	}

	public async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(int limit = 3000, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH selected_strategies AS (
    SELECT id, code, name
    FROM strategies
    ORDER BY
        CASE WHEN code = 'follow_leader' THEN 0 ELSE 1 END,
        code
    LIMIT @Limit
),
windows AS (
    SELECT *
    FROM (VALUES
        ('1h'::text, 1::integer, CAST(@NowUtc AS timestamptz) - interval '1 hour', CAST(@NowUtc AS timestamptz)),
        ('6h'::text, 6::integer, CAST(@NowUtc AS timestamptz) - interval '6 hours', CAST(@NowUtc AS timestamptz)),
        ('24h'::text, 24::integer, CAST(@NowUtc AS timestamptz) - interval '24 hours', CAST(@NowUtc AS timestamptz))
    ) AS window_row(window_label, window_hours, window_start_utc, window_end_utc)
),
strategy_windows AS (
    SELECT
        strategy.id AS strategy_id,
        strategy.code,
        strategy.name,
        window_row.window_label,
        window_row.window_hours,
        window_row.window_start_utc,
        window_row.window_end_utc
    FROM selected_strategies strategy
    CROSS JOIN windows window_row
)
SELECT
    sw.strategy_id,
    sw.code,
    sw.name,
    sw.window_label,
    sw.window_hours,
    sw.window_start_utc,
    sw.window_end_utc,
    COALESCE(order_agg.orders_count, 0) AS orders_count,
    COALESCE(order_agg.filled_orders_count, 0) AS filled_orders_count,
    COALESCE(order_agg.expired_orders_count, 0) AS expired_orders_count,
    COALESCE(order_agg.open_orders_count, 0) AS open_orders_count,
    COALESCE(run_agg.entered_runs_count, 0) AS entered_runs_count,
    COALESCE(run_agg.skipped_runs_count, 0) AS skipped_runs_count,
    COALESCE(run_agg.settled_runs_count, 0) AS settled_runs_count,
    COALESCE(run_agg.won_runs_count, 0) AS won_runs_count,
    COALESCE(run_agg.lost_runs_count, 0) AS lost_runs_count,
    COALESCE(fill_agg.filled_cost_usd, 0) AS filled_cost_usd,
    COALESCE(run_agg.realized_pnl_usd, 0) AS realized_pnl_usd,
    COALESCE(fill_agg.avg_fill_price, 0) AS avg_fill_price,
    COALESCE(run_agg.avg_entry_delay_seconds, 0) AS avg_entry_delay_seconds,
    COALESCE(run_agg.max_entry_delay_seconds, 0) AS max_entry_delay_seconds,
    CASE
        WHEN COALESCE(run_agg.settled_runs_count, 0) = 0 THEN 0
        ELSE COALESCE(run_agg.won_runs_count, 0) * 100.0 / run_agg.settled_runs_count
    END AS win_rate_pct,
    CASE
        WHEN COALESCE(run_agg.settled_stake_usd, 0) > 0 THEN COALESCE(run_agg.realized_pnl_usd, 0) * 100.0 / run_agg.settled_stake_usd
        WHEN COALESCE(fill_agg.filled_cost_usd, 0) > 0 THEN COALESCE(run_agg.realized_pnl_usd, 0) * 100.0 / fill_agg.filled_cost_usd
        ELSE 0
    END AS roi_pct,
    COALESCE(top_skip.top_skip_reason, '') AS top_skip_reason,
    order_agg.last_order_utc,
    run_agg.last_run_utc
FROM strategy_windows sw
LEFT JOIN LATERAL (
    SELECT
        count(*)::integer AS orders_count,
        (count(*) FILTER (WHERE status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired')))::integer AS filled_orders_count,
        (count(*) FILTER (WHERE status IN ('Expired', 'PartiallyFilledExpired')))::integer AS expired_orders_count,
        (count(*) FILTER (WHERE status IN ('Pending', 'PartiallyFilled')))::integer AS open_orders_count,
        max(created_at_utc) AS last_order_utc
    FROM paper_orders paper_order
    WHERE paper_order.strategy_id = sw.strategy_id
      AND paper_order.created_at_utc >= sw.window_start_utc
      AND paper_order.created_at_utc <= sw.window_end_utc
) order_agg ON true
LEFT JOIN LATERAL (
    SELECT
        COALESCE(sum(fill_row.price * fill_row.size_shares), 0) AS filled_cost_usd,
        CASE
            WHEN COALESCE(sum(fill_row.size_shares), 0) = 0 THEN 0
            ELSE COALESCE(sum(fill_row.price * fill_row.size_shares), 0) / sum(fill_row.size_shares)
        END AS avg_fill_price
    FROM paper_fills fill_row
    INNER JOIN paper_orders paper_order ON paper_order.id = fill_row.paper_order_id
    WHERE paper_order.strategy_id = sw.strategy_id
      AND fill_row.filled_at_utc >= sw.window_start_utc
      AND fill_row.filled_at_utc <= sw.window_end_utc
) fill_agg ON true
LEFT JOIN LATERAL (
    SELECT
        (count(*) FILTER (WHERE entered_at_utc >= sw.window_start_utc AND entered_at_utc <= sw.window_end_utc))::integer AS entered_runs_count,
        (count(*) FILTER (WHERE status = 'Skipped' AND updated_at_utc >= sw.window_start_utc AND updated_at_utc <= sw.window_end_utc))::integer AS skipped_runs_count,
        (count(*) FILTER (WHERE status = 'Settled' AND settled_at_utc >= sw.window_start_utc AND settled_at_utc <= sw.window_end_utc))::integer AS settled_runs_count,
        (count(*) FILTER (WHERE status = 'Settled' AND settled_at_utc >= sw.window_start_utc AND settled_at_utc <= sw.window_end_utc AND COALESCE(realized_pnl_usd, 0) > 0))::integer AS won_runs_count,
        (count(*) FILTER (WHERE status = 'Settled' AND settled_at_utc >= sw.window_start_utc AND settled_at_utc <= sw.window_end_utc AND COALESCE(realized_pnl_usd, 0) < 0))::integer AS lost_runs_count,
        COALESCE(sum(stake_usd) FILTER (WHERE status = 'Settled' AND settled_at_utc >= sw.window_start_utc AND settled_at_utc <= sw.window_end_utc), 0) AS settled_stake_usd,
        COALESCE(sum(COALESCE(realized_pnl_usd, 0)) FILTER (WHERE status = 'Settled' AND settled_at_utc >= sw.window_start_utc AND settled_at_utc <= sw.window_end_utc), 0) AS realized_pnl_usd,
        COALESCE(avg(GREATEST(0, EXTRACT(EPOCH FROM (entered_at_utc - entry_due_at_utc)))) FILTER (WHERE entered_at_utc >= sw.window_start_utc AND entered_at_utc <= sw.window_end_utc), 0)::numeric AS avg_entry_delay_seconds,
        COALESCE(max(GREATEST(0, EXTRACT(EPOCH FROM (entered_at_utc - entry_due_at_utc)))) FILTER (WHERE entered_at_utc >= sw.window_start_utc AND entered_at_utc <= sw.window_end_utc), 0)::numeric AS max_entry_delay_seconds,
        max(updated_at_utc) FILTER (WHERE updated_at_utc >= sw.window_start_utc AND updated_at_utc <= sw.window_end_utc) AS last_run_utc
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id = sw.strategy_id
) run_agg ON true
LEFT JOIN LATERAL (
    SELECT concat(skip_reason, ':', count(*)) AS top_skip_reason
    FROM strategy_market_paper_runs skipped_run
    WHERE skipped_run.strategy_id = sw.strategy_id
      AND skipped_run.status = 'Skipped'
      AND skipped_run.updated_at_utc >= sw.window_start_utc
      AND skipped_run.updated_at_utc <= sw.window_end_utc
      AND skipped_run.skip_reason IS NOT NULL
      AND skipped_run.skip_reason <> ''
    GROUP BY skip_reason
    ORDER BY count(*) DESC, skip_reason
    LIMIT 1
) top_skip ON true
ORDER BY
    CASE WHEN sw.code = 'follow_leader' THEN 0 ELSE 1 END,
    sw.code,
    sw.window_hours;
""");
		command.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<StrategyRecentPerformance> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new StrategyRecentPerformance(
				reader.GetGuid(0),
				reader.GetString(1),
				reader.GetString(2),
				reader.GetString(3),
				reader.GetInt32(4),
				DateTimeOffsetFromUtc(reader.GetDateTime(5)),
				DateTimeOffsetFromUtc(reader.GetDateTime(6)),
				reader.GetInt32(7),
				reader.GetInt32(8),
				reader.GetInt32(9),
				reader.GetInt32(10),
				reader.GetInt32(11),
				reader.GetInt32(12),
				reader.GetInt32(13),
				reader.GetInt32(14),
				reader.GetInt32(15),
				reader.GetDecimal(16),
				reader.GetDecimal(17),
				reader.GetDecimal(18),
				reader.GetDecimal(19),
				reader.GetDecimal(20),
				reader.GetDecimal(21),
				reader.GetDecimal(22),
				reader.GetString(23),
				reader.IsDBNull(24) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(24)),
				reader.IsDBNull(25) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(25))));
		}

		return results;
	}

	public async Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategyRuntimeSettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, enabled, live_stakes, paper_stake_amount, live_stake_amount, live_available_balance
FROM strategies;
""");
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		Dictionary<Guid, StrategyRuntimeSettings> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			var strategyId = StrategyIds.Normalize(reader.GetGuid(0));
			results[strategyId] = new StrategyRuntimeSettings(
				strategyId,
				reader.GetBoolean(1),
				reader.GetBoolean(2),
				reader.GetDecimal(3),
				reader.GetDecimal(4),
				reader.GetDecimal(5));
		}

		return results;
	}

	public async Task<IReadOnlyDictionary<Guid, bool>> GetStrategyEnabledStatesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, enabled
FROM strategies;
""");
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		Dictionary<Guid, bool> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results[reader.GetGuid(0)] = reader.GetBoolean(1);
		}

		return results;
	}

	public async Task<bool> SetStrategyEnabledAsync(
		Guid strategyId,
		bool enabled,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET enabled = @Enabled,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("Enabled", enabled);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> SetStrategyLiveStakesAsync(
		Guid strategyId,
		bool liveStakes,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET live_stakes = @LiveStakes,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("LiveStakes", liveStakes);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> SetStrategyStakeAmountsAsync(
		Guid strategyId,
		decimal paperStakeAmount,
		decimal liveStakeAmount,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET paper_stake_amount = @PaperStakeAmount,
    live_stake_amount = @LiveStakeAmount,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
  AND @PaperStakeAmount > 0
  AND @LiveStakeAmount > 0;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("PaperStakeAmount", paperStakeAmount);
		command.Parameters.AddWithValue("LiveStakeAmount", liveStakeAmount);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> SetStrategyLiveAvailableBalanceAsync(
		Guid strategyId,
		decimal liveAvailableBalance,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET live_available_balance = @LiveAvailableBalance,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
  AND @LiveAvailableBalance >= 0;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("LiveAvailableBalance", liveAvailableBalance);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> TryAddPaperCopiedLeaderPositionAsync(PaperCopiedLeaderPosition position, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_copied_leader_positions (
    id, entry_signal_id, entry_paper_order_id, copied_trader_wallet, asset_id,
    condition_id, outcome, entry_transaction_hash, entry_timestamp_utc,
    leader_entry_price, leader_initial_size_shares, copied_initial_size_shares,
    leader_sold_size_shares, copied_exit_requested_size_shares, status,
    last_activity_timestamp_utc, last_activity_transaction_hash,
    last_activity_sync_at_utc, next_activity_sync_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @Id, @EntrySignalId, @EntryPaperOrderId, @CopiedTraderWallet, @AssetId,
    @ConditionId, @Outcome, @EntryTransactionHash, @EntryTimestampUtc,
    @LeaderEntryPrice, @LeaderInitialSizeShares, @CopiedInitialSizeShares,
    @LeaderSoldSizeShares, @CopiedExitRequestedSizeShares, @Status,
    @LastActivityTimestampUtc, @LastActivityTransactionHash,
    @LastActivitySyncAtUtc, @NextActivitySyncAtUtc, @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (entry_paper_order_id) DO NOTHING
RETURNING 1;
""");
		AddPaperCopiedLeaderPositionParameters(command, position);
		return await command.ExecuteScalarAsync(cancellationToken) is not null;
	}

	public async Task ActivatePaperCopiedLeaderPositionAsync(Guid entryPaperOrderId, decimal copiedInitialSizeShares, DateTimeOffset filledAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_copied_leader_positions
SET status = 'Active',
    copied_initial_size_shares = CASE
        WHEN status = 'Active' THEN copied_initial_size_shares + @CopiedInitialSizeShares
        ELSE @CopiedInitialSizeShares
    END,
    next_activity_sync_at_utc = LEAST(next_activity_sync_at_utc, @FilledAtUtc),
    updated_at_utc = @FilledAtUtc
WHERE entry_paper_order_id = @EntryPaperOrderId
  AND status IN ('PendingEntry', 'Active');
""");
		command.Parameters.AddWithValue("EntryPaperOrderId", entryPaperOrderId);
		command.Parameters.AddWithValue("CopiedInitialSizeShares", copiedInitialSizeShares);
		command.Parameters.AddWithValue("FilledAtUtc", UtcDateTime(filledAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PaperCopiedLeaderPosition>> GetPaperCopiedLeaderPositionsForExitTrackingAsync(int limit, DateTimeOffset dueBeforeUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, entry_signal_id, entry_paper_order_id, copied_trader_wallet, asset_id,
       condition_id, outcome, entry_transaction_hash, entry_timestamp_utc,
       leader_entry_price, leader_initial_size_shares, copied_initial_size_shares,
       leader_sold_size_shares, copied_exit_requested_size_shares, status,
       last_activity_timestamp_utc, last_activity_transaction_hash,
       last_activity_sync_at_utc, next_activity_sync_at_utc, created_at_utc, updated_at_utc
FROM paper_copied_leader_positions
WHERE status = 'Active'
  AND next_activity_sync_at_utc <= @DueBeforeUtc
  AND leader_initial_size_shares > leader_sold_size_shares
  AND copied_initial_size_shares > copied_exit_requested_size_shares
ORDER BY next_activity_sync_at_utc, updated_at_utc, copied_trader_wallet, asset_id
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("DueBeforeUtc", UtcDateTime(dueBeforeUtc));
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperCopiedLeaderPosition> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperCopiedLeaderPosition(reader));
		}

		return results;
	}

	public async Task MarkPaperCopiedLeaderPositionsActivitySyncedAsync(string copiedTraderWallet, DateTimeOffset syncedAtUtc, DateTimeOffset nextSyncAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_copied_leader_positions
SET last_activity_sync_at_utc = @SyncedAtUtc,
    next_activity_sync_at_utc = @NextSyncAtUtc,
    updated_at_utc = @SyncedAtUtc
WHERE status = 'Active'
  AND lower(copied_trader_wallet) = lower(@CopiedTraderWallet);
""");
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
		command.Parameters.AddWithValue("SyncedAtUtc", UtcDateTime(syncedAtUtc));
		command.Parameters.AddWithValue("NextSyncAtUtc", UtcDateTime(nextSyncAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<bool> ApplyPaperCopiedLeaderExitAsync(PaperCopiedLeaderActivityEvent activityEvent, IReadOnlyList<PaperCopiedLeaderPositionExitUpdate> positionUpdates, IReadOnlyList<Signal> signals, IReadOnlyList<PaperOrder> paperOrders, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using (NpgsqlCommand eventCommand = CreateCommand(connection, """
INSERT INTO paper_copied_leader_activity_events (
    id, dedup_key, copied_trader_wallet, asset_id, condition_id, side, price,
    size_shares, usdc_size, transaction_hash, activity_timestamp_utc,
    raw_json, observed_at_utc
) VALUES (
    @Id, @DedupKey, @CopiedTraderWallet, @AssetId, @ConditionId, @Side, @Price,
    @SizeShares, @UsdcSize, @TransactionHash, @ActivityTimestampUtc,
    CAST(@RawJson AS jsonb), @ObservedAtUtc
)
ON CONFLICT (dedup_key) DO NOTHING
RETURNING 1;
"""))
		{
			eventCommand.Transaction = transaction;
			eventCommand.Parameters.AddWithValue("Id", activityEvent.Id);
			eventCommand.Parameters.AddWithValue("DedupKey", activityEvent.DedupKey);
			eventCommand.Parameters.AddWithValue("CopiedTraderWallet", activityEvent.CopiedTraderWallet);
			eventCommand.Parameters.AddWithValue("AssetId", activityEvent.AssetId);
			eventCommand.Parameters.AddWithValue("ConditionId", activityEvent.ConditionId);
			eventCommand.Parameters.AddWithValue("Side", activityEvent.Side.ToString());
			eventCommand.Parameters.AddWithValue("Price", activityEvent.Price);
			eventCommand.Parameters.AddWithValue("SizeShares", activityEvent.SizeShares);
			eventCommand.Parameters.AddWithValue("UsdcSize", activityEvent.UsdcSize);
			eventCommand.Parameters.AddWithValue("TransactionHash", ((object?)activityEvent.TransactionHash) ?? DBNull.Value);
			eventCommand.Parameters.AddWithValue("ActivityTimestampUtc", UtcDateTime(activityEvent.ActivityTimestampUtc));
			eventCommand.Parameters.AddWithValue("RawJson", activityEvent.RawJson);
			eventCommand.Parameters.AddWithValue("ObservedAtUtc", UtcDateTime(activityEvent.ObservedAtUtc));
			if (await eventCommand.ExecuteScalarAsync(cancellationToken) is null)
			{
				return false;
			}
		}

		foreach (PaperCopiedLeaderPositionExitUpdate update in positionUpdates)
		{
			await using NpgsqlCommand updateCommand = CreateCommand(connection, """
UPDATE paper_copied_leader_positions
SET leader_sold_size_shares = @LeaderSoldSizeShares,
    copied_exit_requested_size_shares = @CopiedExitRequestedSizeShares,
    status = @Status,
    last_activity_timestamp_utc = @LastActivityTimestampUtc,
    last_activity_transaction_hash = @LastActivityTransactionHash,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @PositionId;
""");
			updateCommand.Transaction = transaction;
			updateCommand.Parameters.AddWithValue("PositionId", update.PositionId);
			updateCommand.Parameters.AddWithValue("LeaderSoldSizeShares", update.LeaderSoldSizeShares);
			updateCommand.Parameters.AddWithValue("CopiedExitRequestedSizeShares", update.CopiedExitRequestedSizeShares);
			updateCommand.Parameters.AddWithValue("Status", update.Status.ToString());
			updateCommand.Parameters.AddWithValue("LastActivityTimestampUtc", UtcDateTime(update.LastActivityTimestampUtc));
			updateCommand.Parameters.AddWithValue("LastActivityTransactionHash", ((object?)update.LastActivityTransactionHash) ?? DBNull.Value);
			updateCommand.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(update.UpdatedAtUtc));
			await updateCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		foreach (Signal signal in signals)
		{
			await using NpgsqlCommand signalCommand = CreateCommand(connection, "INSERT INTO signals (\n    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,\n    best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, decision,\n    accepted, proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json\n) VALUES (\n    @Id, @LeaderTradeId, @TraderWallet, @ConditionId, @AssetId, @Outcome, @LeaderPrice,\n    @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, @LagSeconds, @Score, @Decision,\n    @Accepted, @ProposedPaperPrice, @ProposedSizeShares, @ProposedNotionalUsd, @CreatedAtUtc, CAST(@RawContextJson AS jsonb)\n);");
			signalCommand.Transaction = transaction;
			AddSignalParameters(signalCommand, signal);
			await signalCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		foreach (PaperOrder order in paperOrders)
		{
			await using NpgsqlCommand orderCommand = CreateCommand(connection, "INSERT INTO paper_orders (\n    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json, correlation_id, execution_source\n) VALUES (\n    @Id, @SignalId, @StrategyId, @CopiedTraderWallet, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,\n    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc, CAST(@RawDecisionJson AS jsonb), @CorrelationId, @ExecutionSource\n);");
			orderCommand.Transaction = transaction;
			AddPaperOrderParameters(orderCommand, order);
			await orderCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
		return true;
	}

	public async Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO dry_run_orders (\n    id, signal_id, strategy_id, status, side, asset_id, condition_id, outcome, price, size_shares,\n    notional_usd, order_type, payload_json, validation_summary, created_at_utc\n) VALUES (\n    @Id, @SignalId, @StrategyId, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares,\n    @NotionalUsd, @OrderType, CAST(@PayloadJson AS jsonb), @ValidationSummary, @CreatedAtUtc\n);");
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("SignalId", order.SignalId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
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

	public async Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<DryRunOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<DryRunOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, signal_id, strategy_id, status, side, asset_id, condition_id, outcome, price, size_shares,\n       notional_usd, order_type, payload_json::text, validation_summary, created_at_utc\nFROM dry_run_orders\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<DryRunOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<DryRunOrder> results = new List<DryRunOrder>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new DryRunOrder(reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<DryRunOrderStatus>(reader.GetString(3)), Enum.Parse<TradeSide>(reader.GetString(4)), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetString(11), reader.GetString(12), reader.GetString(13), DateTimeOffsetFromUtc(reader.GetDateTime(14)), reader.GetGuid(2)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO live_orders (
    id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,
    notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,
    filled_size, remaining_size, average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,
    cancel_status, raw_response_json, validation_summary,
    balance_effect_applied, settlement_value_usd, realized_pnl_usd, settled_at_utc, winning_asset_id, winning_outcome,
    won, settlement_source, correlation_id, execution_source, post_only, paper_order_id,
    updated_at_utc
) VALUES (
    @Id, @SignalId, @StrategyId, @Status, @OrderId, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares,
    @NotionalUsd, @OrderType, @CreatedAtUtc, @ExpiresAtUtc, @SubmittedAtUtc, @ResponseStatus,
    @FilledSize, @RemainingSize, @AverageFillPrice, @FilledNotionalUsd, @CostBasisUsd, @FeeUsd,
    @CancelStatus, CAST(@RawResponseJson AS jsonb), @ValidationSummary,
    @BalanceEffectApplied, @SettlementValueUsd, @RealizedPnlUsd, @SettledAtUtc, @WinningAssetId, @WinningOutcome,
    @Won, @SettlementSource, @CorrelationId, @ExecutionSource, @PostOnly, @PaperOrderId,
    @UpdatedAtUtc
);
""");
		AddLiveOrderParameters(command, order);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE live_orders
SET status = @Status,
    strategy_id = @StrategyId,
    order_id = @OrderId,
    submitted_at_utc = @SubmittedAtUtc,
    response_status = @ResponseStatus,
    filled_size = @FilledSize,
    remaining_size = @RemainingSize,
    average_fill_price = @AverageFillPrice,
    filled_notional_usd = @FilledNotionalUsd,
    cost_basis_usd = @CostBasisUsd,
    fee_usd = @FeeUsd,
    cancel_status = @CancelStatus,
    raw_response_json = CAST(@RawResponseJson AS jsonb),
    validation_summary = @ValidationSummary,
    balance_effect_applied = @BalanceEffectApplied,
    settlement_value_usd = @SettlementValueUsd,
    realized_pnl_usd = @RealizedPnlUsd,
    settled_at_utc = @SettledAtUtc,
    winning_asset_id = @WinningAssetId,
    winning_outcome = @WinningOutcome,
    won = @Won,
    settlement_source = @SettlementSource,
    correlation_id = @CorrelationId,
    execution_source = @ExecutionSource,
    post_only = @PostOnly,
    paper_order_id = @PaperOrderId,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @Id;
""");
		AddLiveOrderParameters(command, order);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')\nORDER BY created_at_utc DESC;"))
			{
				IReadOnlyList<LiveOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					readOnlyList = await ReadLiveOrdersAsync(reader, cancellationToken);
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersForStrategyOrCorrelationAsync(
		Guid strategyId,
		Guid? correlationId = null,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')\n  AND (strategy_id = @StrategyId OR (@CorrelationId IS NOT NULL AND correlation_id = @CorrelationId))\nORDER BY created_at_utc DESC;");
			command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
			command.Parameters.AddWithValue("CorrelationId", ((object)correlationId) ?? ((object)DBNull.Value));
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			result = await ReadLiveOrdersAsync(reader, cancellationToken);
		}

		return result;
	}

	public async Task<IReadOnlyList<LiveOrder>> GetMatchedLiveOrdersPendingBalanceSettlementAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status = 'Matched'\n  AND balance_effect_applied = false\n  AND filled_size > 0\nORDER BY updated_at_utc ASC, created_at_utc ASC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<LiveOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					readOnlyList = await ReadLiveOrdersAsync(reader, cancellationToken);
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceAsync(
		Guid liveOrderId,
		Guid strategyId,
		decimal settlementValueUsd,
		decimal realizedPnlUsd,
		string? winningAssetId,
		string winningOutcome,
		DateTimeOffset settledAtUtc,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedStrategyId = StrategyIds.Normalize(strategyId);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

		await using (NpgsqlCommand command = CreateCommand(connection, """
UPDATE live_orders
SET balance_effect_applied = true,
    settlement_value_usd = @SettlementValueUsd,
    realized_pnl_usd = @RealizedPnlUsd,
    settled_at_utc = @SettledAtUtc,
    winning_asset_id = @WinningAssetId,
    winning_outcome = @WinningOutcome,
    won = @Won,
    settlement_source = 'gamma_resolved_metadata',
    updated_at_utc = @UpdatedAtUtc
WHERE id = @LiveOrderId
  AND strategy_id = @StrategyId
  AND balance_effect_applied = false
RETURNING id;
"""))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("LiveOrderId", liveOrderId);
			command.Parameters.AddWithValue("StrategyId", normalizedStrategyId);
			command.Parameters.AddWithValue("SettlementValueUsd", settlementValueUsd);
			command.Parameters.AddWithValue("RealizedPnlUsd", realizedPnlUsd);
			command.Parameters.AddWithValue("SettledAtUtc", UtcDateTime(settledAtUtc));
			command.Parameters.AddWithValue("WinningAssetId", ((object)winningAssetId) ?? ((object)DBNull.Value));
			command.Parameters.AddWithValue("WinningOutcome", winningOutcome);
			command.Parameters.AddWithValue("Won", settlementValueUsd > 0m);
			command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
			var appliedOrderId = await command.ExecuteScalarAsync(cancellationToken);
			if (appliedOrderId is null or DBNull)
			{
				await transaction.RollbackAsync(cancellationToken);
				return new StrategyLiveBalanceAdjustmentResult(false, 0m, false);
			}
		}

		await using (NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET live_available_balance = GREATEST(0, live_available_balance + @RealizedPnlUsd),
    live_stakes = CASE
        WHEN GREATEST(0, live_available_balance + @RealizedPnlUsd) < live_stake_amount THEN false
        ELSE live_stakes
    END,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
RETURNING live_available_balance, live_stakes, live_stake_amount;
"""))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("StrategyId", normalizedStrategyId);
			command.Parameters.AddWithValue("RealizedPnlUsd", realizedPnlUsd);
			command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				await transaction.RollbackAsync(cancellationToken);
				return new StrategyLiveBalanceAdjustmentResult(false, 0m, false);
			}

			var availableBalance = reader.GetDecimal(0);
			var liveStakes = reader.GetBoolean(1);
			var liveStakeAmount = reader.GetDecimal(2);
			await reader.CloseAsync();
			await transaction.CommitAsync(cancellationToken);
			return new StrategyLiveBalanceAdjustmentResult(
				true,
				availableBalance,
				!liveStakes && availableBalance < liveStakeAmount);
		}
	}

	public async Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<LiveOrder> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					readOnlyList = await ReadLiveOrdersAsync(reader, cancellationToken);
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO live_trading_events (id, action, status, details, created_at_utc)\nVALUES (@Id, @Action, @Status, @Details, @CreatedAtUtc);");
		command.Parameters.AddWithValue("Id", liveEvent.Id);
		command.Parameters.AddWithValue("Action", liveEvent.Action);
		command.Parameters.AddWithValue("Status", liveEvent.Status);
		command.Parameters.AddWithValue("Details", liveEvent.Details);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(liveEvent.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveTradingEvent> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveTradingEvent> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, action, status, details, created_at_utc\nFROM live_trading_events\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<LiveTradingEvent> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<LiveTradingEvent> results = new List<LiveTradingEvent>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new LiveTradingEvent(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffsetFromUtc(reader.GetDateTime(4))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddPaperLiveShadowDecisionAsync(PaperLiveShadowDecision decision, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_live_shadow_decisions (
    correlation_id, strategy_id, market_id, condition_id, asset_id, outcome, side,
    limit_price, target_notional_usd, requested_size_shares, max_reserved_notional_usd,
    order_type, post_only, order_book_snapshot_json, quote_age_ms, source,
    quote_received_at_utc, decision_created_at_utc, market_start_utc, market_close_utc,
    submit_deadline_utc, cancel_deadline_utc, signal_id, paper_order_id, live_order_id,
    status, updated_at_utc
) VALUES (
    @CorrelationId, @StrategyId, @MarketId, @ConditionId, @AssetId, @Outcome, @Side,
    @LimitPrice, @TargetNotionalUsd, @RequestedSizeShares, @MaxReservedNotionalUsd,
    @OrderType, @PostOnly, CAST(@OrderBookSnapshotJson AS jsonb), @QuoteAgeMs, @Source,
    @QuoteReceivedAtUtc, @DecisionCreatedAtUtc, @MarketStartUtc, @MarketCloseUtc,
    @SubmitDeadlineUtc, @CancelDeadlineUtc, @SignalId, @PaperOrderId, @LiveOrderId,
    @Status, @UpdatedAtUtc
);
""");
		AddPaperLiveShadowDecisionParameters(command, decision);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpdatePaperLiveShadowDecisionLinksAsync(
		Guid correlationId,
		Guid? signalId,
		Guid? paperOrderId,
		Guid? liveOrderId,
		string status,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_live_shadow_decisions
SET signal_id = COALESCE(@SignalId, signal_id),
    paper_order_id = COALESCE(@PaperOrderId, paper_order_id),
    live_order_id = COALESCE(@LiveOrderId, live_order_id),
    status = @Status,
    updated_at_utc = @UpdatedAtUtc
WHERE correlation_id = @CorrelationId;
""");
		command.Parameters.AddWithValue("CorrelationId", correlationId);
		command.Parameters.AddWithValue("SignalId", ((object)signalId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("PaperOrderId", ((object)paperOrderId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("LiveOrderId", ((object)liveOrderId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Status", status);
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddPaperLiveShadowDiscrepancyAsync(PaperLiveShadowDiscrepancy discrepancy, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_live_shadow_discrepancies (
    id, correlation_id, strategy_id, classification, severity, details, raw_json, created_at_utc
) VALUES (
    @Id, @CorrelationId, @StrategyId, @Classification, @Severity, @Details, CAST(@RawJson AS jsonb), @CreatedAtUtc
);
""");
		command.Parameters.AddWithValue("Id", discrepancy.Id);
		command.Parameters.AddWithValue("CorrelationId", discrepancy.CorrelationId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(discrepancy.StrategyId));
		command.Parameters.AddWithValue("Classification", discrepancy.Classification);
		command.Parameters.AddWithValue("Severity", discrepancy.Severity);
		command.Parameters.AddWithValue("Details", discrepancy.Details);
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(discrepancy.RawJson) ? "{}" : discrepancy.RawJson);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(discrepancy.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddBtcUsdReferenceCorrelationSampleAsync(BtcUsdReferenceCorrelationSample sample, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_usd_reference_correlation_samples (
    id, binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
    chainlink_price_usd, chainlink_valid_after_utc, time_delta_seconds,
    price_diff_usd, price_diff_bps, chainlink_feed_id, chainlink_query_window,
    raw_json, created_at_utc
) VALUES (
    @Id, @BinancePriceUsd, @BinanceSourceUpdatedAtUtc, @BinanceFetchedAtUtc,
    @ChainlinkPriceUsd, @ChainlinkValidAfterUtc, @TimeDeltaSeconds,
    @PriceDiffUsd, @PriceDiffBps, @ChainlinkFeedId, @ChainlinkQueryWindow,
    CAST(@RawJson AS jsonb), @CreatedAtUtc
)
ON CONFLICT (binance_source_updated_at_utc, chainlink_valid_after_utc) DO NOTHING;
""");
		AddBtcUsdReferenceCorrelationSampleParameters(command, sample);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<BtcUsdReferenceCorrelationSample>> GetRecentBtcUsdReferenceCorrelationSamplesAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<BtcUsdReferenceCorrelationSample> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
       chainlink_price_usd, chainlink_valid_after_utc, time_delta_seconds,
       price_diff_usd, price_diff_bps, chainlink_feed_id, chainlink_query_window,
       raw_json::text, created_at_utc
FROM btc_usd_reference_correlation_samples
ORDER BY created_at_utc DESC
LIMIT @Limit;
""");
			command.Parameters.AddWithValue("Limit", limit);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			List<BtcUsdReferenceCorrelationSample> results = [];
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(ReadBtcUsdReferenceCorrelationSample(reader));
			}

			result = results;
		}

		return result;
	}

	public async Task AddBtcOrderBookLagDiagnosticEventsAsync(IReadOnlyList<BtcOrderBookLagDiagnosticEvent> events, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (events.Count == 0)
		{
			return;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (BtcOrderBookLagDiagnosticEvent diagnosticEvent in events)
		{
			await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_order_book_lag_diagnostic_events (
    id, source, event_type, asset_id, condition_id, binance_symbol, binance_price_usd,
    best_bid, best_bid_size, best_ask, best_ask_size, mid, trade_price, trade_size, source_timestamp_utc,
    received_at_utc, local_lag_ms, raw_event_type, created_at_utc
) VALUES (
    @Id, @Source, @EventType, @AssetId, @ConditionId, @BinanceSymbol, @BinancePriceUsd,
    @BestBid, @BestBidSize, @BestAsk, @BestAskSize, @Mid, @TradePrice, @TradeSize, @SourceTimestampUtc,
    @ReceivedAtUtc, @LocalLagMs, @RawEventType, @CreatedAtUtc
);
""");
			command.Transaction = transaction;
			AddBtcOrderBookLagDiagnosticEventParameters(command, diagnosticEvent);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<int> CleanupBtcOrderBookLagDiagnosticEventsAsync(
		DateTimeOffset receivedBeforeUtc,
		int batchSize,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (batchSize <= 0)
		{
			return 0;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
DELETE FROM btc_order_book_lag_diagnostic_events events
WHERE events.ctid IN (
    SELECT ctid
    FROM btc_order_book_lag_diagnostic_events
    WHERE received_at_utc < @ReceivedBeforeUtc
    ORDER BY received_at_utc ASC
    LIMIT @BatchSize
);
""");
		command.Parameters.AddWithValue("ReceivedBeforeUtc", UtcDateTime(receivedBeforeUtc));
		command.Parameters.AddWithValue("BatchSize", batchSize);
		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddBtcUpDown5mOddsTickAsync(BtcUpDown5mOddsTick tick, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_up_down_5m_odds_ticks (
    id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    sampled_at_utc, seconds_after_start, seconds_to_close,
    binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
    binance_start_price_usd, btc_move_from_start_usd, btc_move_from_start_bps,
    up_asset_id, up_best_bid, up_best_ask, up_mid, up_price_proxy,
    up_price_proxy_kind, up_last_trade_price, up_book_source, up_book_age_ms,
    down_asset_id, down_best_bid, down_best_ask, down_mid, down_price_proxy,
    down_price_proxy_kind, down_last_trade_price, down_book_source, down_book_age_ms,
    diagnostics_json, created_at_utc
) VALUES (
    @Id, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @SampledAtUtc, @SecondsAfterStart, @SecondsToClose,
    @BinancePriceUsd, @BinanceSourceUpdatedAtUtc, @BinanceFetchedAtUtc,
    @BinanceStartPriceUsd, @BtcMoveFromStartUsd, @BtcMoveFromStartBps,
    @UpAssetId, @UpBestBid, @UpBestAsk, @UpMid, @UpPriceProxy,
    @UpPriceProxyKind, @UpLastTradePrice, @UpBookSource, @UpBookAgeMs,
    @DownAssetId, @DownBestBid, @DownBestAsk, @DownMid, @DownPriceProxy,
    @DownPriceProxyKind, @DownLastTradePrice, @DownBookSource, @DownBookAgeMs,
    CAST(@DiagnosticsJson AS jsonb), @CreatedAtUtc
);
""");
		AddBtcUpDown5mOddsTickParameters(command, tick);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<decimal?> GetBtcUpDown5mOddsStartPriceAsync(string marketId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT binance_start_price_usd
FROM btc_up_down_5m_odds_ticks
WHERE market_id = @MarketId
ORDER BY sampled_at_utc ASC
LIMIT 1;
""");
		command.Parameters.AddWithValue("MarketId", marketId);
		var result = await command.ExecuteScalarAsync(cancellationToken);
		return result is decimal value ? value : null;
	}

	public async Task<BtcUpDown5mOddsTick?> GetLatestBtcUpDown5mOddsTickAsync(string marketId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       sampled_at_utc, seconds_after_start, seconds_to_close,
       binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
       binance_start_price_usd, btc_move_from_start_usd, btc_move_from_start_bps,
       up_asset_id, up_best_bid, up_best_ask, up_mid, up_price_proxy,
       up_price_proxy_kind, up_last_trade_price, up_book_source, up_book_age_ms,
       down_asset_id, down_best_bid, down_best_ask, down_mid, down_price_proxy,
       down_price_proxy_kind, down_last_trade_price, down_book_source, down_book_age_ms,
       diagnostics_json::text, created_at_utc
FROM btc_up_down_5m_odds_ticks
WHERE market_id = @MarketId
ORDER BY sampled_at_utc DESC, created_at_utc DESC
LIMIT 1;
""");
		command.Parameters.AddWithValue("MarketId", marketId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadBtcUpDown5mOddsTick(reader) : null;
	}

	public async Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetRecentBtcUpDown5mOddsTicksAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       sampled_at_utc, seconds_after_start, seconds_to_close,
       binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
       binance_start_price_usd, btc_move_from_start_usd, btc_move_from_start_bps,
       up_asset_id, up_best_bid, up_best_ask, up_mid, up_price_proxy,
       up_price_proxy_kind, up_last_trade_price, up_book_source, up_book_age_ms,
       down_asset_id, down_best_bid, down_best_ask, down_mid, down_price_proxy,
       down_price_proxy_kind, down_last_trade_price, down_book_source, down_book_age_ms,
       diagnostics_json::text, created_at_utc
FROM btc_up_down_5m_odds_ticks
ORDER BY sampled_at_utc DESC, created_at_utc DESC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<BtcUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadBtcUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task AddCryptoUpDown5mOddsTickAsync(CryptoUpDown5mOddsTick tick, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_up_down_5m_odds_ticks (
    id, asset_symbol, binance_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    sampled_at_utc, seconds_after_start, seconds_to_close,
    binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
    binance_start_price_usd, asset_move_from_start_usd, asset_move_from_start_bps,
    up_asset_id, up_best_bid, up_best_ask, up_mid, up_price_proxy,
    up_price_proxy_kind, up_last_trade_price, up_book_source, up_book_age_ms,
    down_asset_id, down_best_bid, down_best_ask, down_mid, down_price_proxy,
    down_price_proxy_kind, down_last_trade_price, down_book_source, down_book_age_ms,
    diagnostics_json, created_at_utc
) VALUES (
    @Id, @AssetSymbol, @BinanceSymbol, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @SampledAtUtc, @SecondsAfterStart, @SecondsToClose,
    @BinancePriceUsd, @BinanceSourceUpdatedAtUtc, @BinanceFetchedAtUtc,
    @BinanceStartPriceUsd, @AssetMoveFromStartUsd, @AssetMoveFromStartBps,
    @UpAssetId, @UpBestBid, @UpBestAsk, @UpMid, @UpPriceProxy,
    @UpPriceProxyKind, @UpLastTradePrice, @UpBookSource, @UpBookAgeMs,
    @DownAssetId, @DownBestBid, @DownBestAsk, @DownMid, @DownPriceProxy,
    @DownPriceProxyKind, @DownLastTradePrice, @DownBookSource, @DownBookAgeMs,
    CAST(@DiagnosticsJson AS jsonb), @CreatedAtUtc
);
""");
		AddCryptoUpDown5mOddsTickParameters(command, tick);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<decimal?> GetCryptoUpDown5mOddsStartPriceAsync(string assetSymbol, string marketId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT binance_start_price_usd
FROM crypto_up_down_5m_odds_ticks
WHERE lower(asset_symbol) = lower(@AssetSymbol)
  AND market_id = @MarketId
ORDER BY sampled_at_utc ASC
LIMIT 1;
""");
		command.Parameters.AddWithValue("AssetSymbol", assetSymbol);
		command.Parameters.AddWithValue("MarketId", marketId);
		var result = await command.ExecuteScalarAsync(cancellationToken);
		return result is decimal value ? value : null;
	}

	public async Task<IReadOnlyList<CryptoUpDown5mOddsTick>> GetRecentCryptoUpDown5mOddsTicksAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, asset_symbol, binance_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       sampled_at_utc, seconds_after_start, seconds_to_close,
       binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
       binance_start_price_usd, asset_move_from_start_usd, asset_move_from_start_bps,
       up_asset_id, up_best_bid, up_best_ask, up_mid, up_price_proxy,
       up_price_proxy_kind, up_last_trade_price, up_book_source, up_book_age_ms,
       down_asset_id, down_best_bid, down_best_ask, down_mid, down_price_proxy,
       down_price_proxy_kind, down_last_trade_price, down_book_source, down_book_age_ms,
       diagnostics_json::text, created_at_utc
FROM crypto_up_down_5m_odds_ticks
ORDER BY sampled_at_utc DESC, created_at_utc DESC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO api_errors (id, component, operation, message, created_at_utc)\nVALUES (@Id, @Component, @Operation, @Message, @CreatedAtUtc);");
		command.Parameters.AddWithValue("Id", error.Id);
		command.Parameters.AddWithValue("Component", error.Component);
		command.Parameters.AddWithValue("Operation", error.Operation);
		command.Parameters.AddWithValue("Message", error.Message);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(error.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ApiError> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<ApiError> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, component, operation, message, created_at_utc\nFROM api_errors\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<ApiError> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<ApiError> results = new List<ApiError>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new ApiError(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffsetFromUtc(reader.GetDateTime(4))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_http_logs (\n    id, component, operation, http_method, request_url, requested_at_utc, response_at_utc,\n    duration_ms, attempt, status_code, succeeded, response_body, error_message\n) VALUES (\n    @Id, @Component, @Operation, @HttpMethod, @RequestUrl, @RequestedAtUtc, @ResponseAtUtc,\n    @DurationMs, @Attempt, @StatusCode, @Succeeded, @ResponseBody, @ErrorMessage\n);");
		AddPolymarketHttpLogParameters(command, entry);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketHttpLogEntry> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketHttpLogEntry> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, component, operation, http_method, request_url, requested_at_utc, response_at_utc,\n       duration_ms, attempt, status_code, succeeded, response_body, error_message\nFROM polymarket_http_logs\nORDER BY requested_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketHttpLogEntry> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketHttpLogEntry> results = new List<PolymarketHttpLogEntry>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketHttpLogEntry(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<PolymarketHttpLogCleanupResult> CleanupPolymarketHttpLogsAsync(DateTimeOffset successfulBeforeUtc, DateTimeOffset failedBeforeUtc, int batchSize, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (batchSize <= 0)
		{
			return new PolymarketHttpLogCleanupResult(0, 0, 0);
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH selected AS (
    SELECT id
    FROM polymarket_http_logs
    WHERE (succeeded = true AND requested_at_utc < @SuccessfulBeforeUtc)
       OR (succeeded = false AND requested_at_utc < @FailedBeforeUtc)
    ORDER BY requested_at_utc ASC
    LIMIT @BatchSize
),
deleted AS (
    DELETE FROM polymarket_http_logs logs
    USING selected
    WHERE logs.id = selected.id
    RETURNING logs.succeeded
)
SELECT
    count(*)::integer AS deleted_rows,
    count(*) FILTER (WHERE succeeded = true)::integer AS deleted_successful_rows,
    count(*) FILTER (WHERE succeeded = false)::integer AS deleted_failed_rows
FROM deleted;
""");
		command.Parameters.AddWithValue("SuccessfulBeforeUtc", UtcDateTime(successfulBeforeUtc));
		command.Parameters.AddWithValue("FailedBeforeUtc", UtcDateTime(failedBeforeUtc));
		command.Parameters.AddWithValue("BatchSize", batchSize);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
		{
			return new PolymarketHttpLogCleanupResult(0, 0, 0);
		}

		return new PolymarketHttpLogCleanupResult(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
	}

	public async Task AddPolymarketOnChainLogsAsync(IReadOnlyList<PolymarketOnChainLog> logs, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (logs.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (PolymarketOnChainLog log in logs)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_logs (\n    id, contract_name, contract_address, exchange_version, block_number, block_hash,\n    transaction_hash, transaction_index, log_index, topic0, topics_json, data, removed, observed_at_utc\n) VALUES (\n    @Id, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber, @BlockHash,\n    @TransactionHash, @TransactionIndex, @LogIndex, @Topic0, CAST(@TopicsJson AS jsonb), @Data, @Removed, @ObservedAtUtc\n)\nON CONFLICT (transaction_hash, log_index) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_hash = excluded.block_hash,\n    transaction_index = excluded.transaction_index,\n    topic0 = excluded.topic0,\n    topics_json = excluded.topics_json,\n    data = excluded.data,\n    removed = excluded.removed,\n    observed_at_utc = excluded.observed_at_utc;");
			command.Transaction = transaction;
			AddPolymarketOnChainLogParameters(command, log);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task AddPolymarketOnChainFillsAsync(IReadOnlyList<PolymarketOnChainFill> fills, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (fills.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (PolymarketOnChainFill fill in fills)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_fills (\n    id, contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n    transaction_hash, log_index, order_hash, maker, taker, wallet, side, token_id,\n    maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw, maker_amount, taker_amount,\n    price, size_shares, notional_usd, fee_raw, fee_amount, fee_asset_id, builder, metadata, imported_at_utc\n) VALUES (\n    @Id, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber, @BlockTimestampUtc,\n    @TransactionHash, @LogIndex, @OrderHash, @Maker, @Taker, @Wallet, @Side, @TokenId,\n    @MakerAssetId, @TakerAssetId, @MakerAmountRaw, @TakerAmountRaw, @MakerAmount, @TakerAmount,\n    @Price, @SizeShares, @NotionalUsd, @FeeRaw, @FeeAmount, @FeeAssetId, @Builder, @Metadata, @ImportedAtUtc\n)\nON CONFLICT (transaction_hash, log_index) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    order_hash = excluded.order_hash,\n    maker = excluded.maker,\n    taker = excluded.taker,\n    wallet = excluded.wallet,\n    side = excluded.side,\n    token_id = excluded.token_id,\n    maker_asset_id = excluded.maker_asset_id,\n    taker_asset_id = excluded.taker_asset_id,\n    maker_amount_raw = excluded.maker_amount_raw,\n    taker_amount_raw = excluded.taker_amount_raw,\n    maker_amount = excluded.maker_amount,\n    taker_amount = excluded.taker_amount,\n    price = excluded.price,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    fee_raw = excluded.fee_raw,\n    fee_amount = excluded.fee_amount,\n    fee_asset_id = excluded.fee_asset_id,\n    builder = excluded.builder,\n    metadata = excluded.metadata,\n    imported_at_utc = excluded.imported_at_utc;");
			command.Transaction = transaction;
			AddPolymarketOnChainFillParameters(command, fill);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		foreach (var range in from @group in fills.GroupBy<PolymarketOnChainFill, string>((PolymarketOnChainFill polymarketOnChainFill) => polymarketOnChainFill.ContractAddress, StringComparer.OrdinalIgnoreCase)
			select new
			{
				ContractAddress = @group.Key,
				FromBlock = @group.Min((PolymarketOnChainFill polymarketOnChainFill) => polymarketOnChainFill.BlockNumber),
				ToBlock = @group.Max((PolymarketOnChainFill polymarketOnChainFill) => polymarketOnChainFill.BlockNumber)
			})
		{
			await UpsertPolymarketOnChainWalletFillsAsync(connection, transaction, range.ContractAddress, range.FromBlock, range.ToBlock, cancellationToken);
			await UpsertPolymarketOnChainWalletExecutionsAsync(connection, transaction, range.ContractAddress, range.FromBlock, range.ToBlock, cancellationToken);
			await UpsertPolymarketOnChainTradeDetailsAsync(connection, transaction, range.ContractAddress, range.FromBlock, range.ToBlock, cancellationToken);
			await QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(connection, transaction, range.ContractAddress, range.FromBlock, range.ToBlock, "execution", cancellationToken);
			await DeleteProcessedPolymarketOnChainRawLogsAsync(connection, transaction, range.ContractAddress, range.FromBlock, range.ToBlock, cancellationToken);
		}
		await QueuePolymarketOnChainPositionRefreshTokensAsync(connection, transaction, fills.Select((PolymarketOnChainFill polymarketOnChainFill) => polymarketOnChainFill.TokenId).Distinct<string>(StringComparer.OrdinalIgnoreCase), "execution", cancellationToken);
		await QueuePolymarketOnChainTokenMetadataRefreshTokensAsync(connection, transaction, fills.Select((PolymarketOnChainFill polymarketOnChainFill) => polymarketOnChainFill.TokenId).Distinct<string>(StringComparer.OrdinalIgnoreCase), "execution", cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<int> AddPolymarketOnChainTradeCapturesAsync(IReadOnlyList<PolymarketOnChainTradeCapture> captures, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (captures.Count == 0)
		{
			return 0;
		}
		int rowsAffected = 0;
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (PolymarketOnChainTradeCapture capture in captures)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_trade_captures (\n    id, contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n    block_hash, transaction_hash, transaction_index, log_index, order_hash, maker, taker, wallet, side, token_id,\n    maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw, maker_amount, taker_amount,\n    price, size_shares, notional_usd, fee_raw, fee_amount, fee_asset_id, builder, metadata,\n    raw_topics_json, raw_data, removed, observed_at_utc, imported_at_utc\n) VALUES (\n    @Id, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber, @BlockTimestampUtc,\n    @BlockHash, @TransactionHash, @TransactionIndex, @LogIndex, @OrderHash, @Maker, @Taker, @Wallet, @Side, @TokenId,\n    @MakerAssetId, @TakerAssetId, @MakerAmountRaw, @TakerAmountRaw, @MakerAmount, @TakerAmount,\n    @Price, @SizeShares, @NotionalUsd, @FeeRaw, @FeeAmount, @FeeAssetId, @Builder, @Metadata,\n    CAST(@RawTopicsJson AS jsonb), @RawData, @Removed, @ObservedAtUtc, @ImportedAtUtc\n)\nON CONFLICT (transaction_hash, log_index) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    block_hash = excluded.block_hash,\n    transaction_index = excluded.transaction_index,\n    order_hash = excluded.order_hash,\n    maker = excluded.maker,\n    taker = excluded.taker,\n    wallet = excluded.wallet,\n    side = excluded.side,\n    token_id = excluded.token_id,\n    maker_asset_id = excluded.maker_asset_id,\n    taker_asset_id = excluded.taker_asset_id,\n    maker_amount_raw = excluded.maker_amount_raw,\n    taker_amount_raw = excluded.taker_amount_raw,\n    maker_amount = excluded.maker_amount,\n    taker_amount = excluded.taker_amount,\n    price = excluded.price,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    fee_raw = excluded.fee_raw,\n    fee_amount = excluded.fee_amount,\n    fee_asset_id = excluded.fee_asset_id,\n    builder = excluded.builder,\n    metadata = excluded.metadata,\n    raw_topics_json = excluded.raw_topics_json,\n    raw_data = excluded.raw_data,\n    removed = excluded.removed,\n    observed_at_utc = excluded.observed_at_utc,\n    imported_at_utc = excluded.imported_at_utc;");
			command.Transaction = transaction;
			AddPolymarketOnChainTradeCaptureParameters(command, capture);
			rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
		return rowsAffected;
	}

	private static async Task UpsertPolymarketOnChainWalletFillsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_fills (\n    source_fill_id, contract_name, contract_address, exchange_version, block_number,\n    block_timestamp_utc, transaction_hash, log_index, order_hash, role, wallet, counterparty,\n    side, token_id, price, size_shares, notional_usd, fee_amount, fee_asset_id, imported_at_utc\n)\nSELECT id, contract_name, contract_address, exchange_version, block_number,\n       block_timestamp_utc, transaction_hash, log_index, order_hash, 'Maker',\n       maker, taker, side, token_id, price, size_shares, notional_usd,\n       fee_amount, fee_asset_id, imported_at_utc\nFROM polymarket_onchain_fills\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock\nUNION ALL\nSELECT id, contract_name, contract_address, exchange_version, block_number,\n       block_timestamp_utc, transaction_hash, log_index, order_hash, 'Taker',\n       taker, maker,\n       CASE side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE side END,\n       token_id, price, size_shares, notional_usd, 0, '0', imported_at_utc\nFROM polymarket_onchain_fills\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock\nON CONFLICT (transaction_hash, log_index, role) DO UPDATE SET\n    source_fill_id = excluded.source_fill_id,\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    order_hash = excluded.order_hash,\n    wallet = excluded.wallet,\n    counterparty = excluded.counterparty,\n    side = excluded.side,\n    token_id = excluded.token_id,\n    price = excluded.price,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    fee_amount = excluded.fee_amount,\n    fee_asset_id = excluded.fee_asset_id,\n    imported_at_utc = excluded.imported_at_utc;\n\nINSERT INTO polymarket_onchain_signal_candidate_refresh_queue (\n    source_fill_id, participant_role, block_timestamp_utc, block_number,\n    log_index, queued_at_utc, next_attempt_at_utc\n)\nSELECT wallet_fill.source_fill_id, wallet_fill.role, wallet_fill.block_timestamp_utc,\n       wallet_fill.block_number, wallet_fill.log_index, now(), now()\nFROM polymarket_onchain_wallet_fills wallet_fill\nLEFT JOIN polymarket_onchain_signal_candidates candidate\n  ON candidate.source_fill_id = wallet_fill.source_fill_id\n AND candidate.participant_role = wallet_fill.role\nWHERE wallet_fill.contract_address = @ContractAddress\n  AND wallet_fill.block_number BETWEEN @FromBlock AND @ToBlock\n  AND candidate.id IS NULL\nON CONFLICT (source_fill_id, participant_role) DO NOTHING;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task UpsertPolymarketOnChainWalletExecutionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_executions\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock;\n\nINSERT INTO polymarket_onchain_wallet_executions (\n    contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n    transaction_hash, first_log_index, last_log_index, wallet, side, token_id, fill_count,\n    maker_fill_count, taker_fill_count, size_shares, notional_usd, average_price,\n    fees_usd, imported_at_utc\n)\nSELECT contract_name,\n       contract_address,\n       exchange_version,\n       MIN(block_number),\n       MIN(block_timestamp_utc),\n       transaction_hash,\n       MIN(log_index),\n       MAX(log_index),\n       wallet,\n       side,\n       token_id,\n       COUNT(*)::integer,\n       COUNT(*) FILTER (WHERE role = 'Maker')::integer,\n       COUNT(*) FILTER (WHERE role = 'Taker')::integer,\n       SUM(size_shares),\n       SUM(notional_usd),\n       CASE WHEN SUM(size_shares) = 0 THEN 0 ELSE SUM(notional_usd) / SUM(size_shares) END,\n       SUM(CASE WHEN fee_asset_id = '0' THEN fee_amount ELSE 0 END),\n       MAX(imported_at_utc)\nFROM polymarket_onchain_wallet_fills\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock\nGROUP BY contract_name, contract_address, exchange_version, transaction_hash, wallet, side, token_id\nON CONFLICT (contract_address, transaction_hash, wallet, side, token_id) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    first_log_index = excluded.first_log_index,\n    last_log_index = excluded.last_log_index,\n    fill_count = excluded.fill_count,\n    maker_fill_count = excluded.maker_fill_count,\n    taker_fill_count = excluded.taker_fill_count,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    average_price = excluded.average_price,\n    fees_usd = excluded.fees_usd,\n    imported_at_utc = excluded.imported_at_utc;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task UpsertPolymarketOnChainTradeDetailsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_trade_details (\n    contract_name,\n    contract_address,\n    exchange_version,\n    block_number,\n    block_timestamp_utc,\n    transaction_hash,\n    log_index,\n    order_hash,\n    maker,\n    taker,\n    maker_side,\n    taker_side,\n    token_id,\n    maker_asset_id,\n    taker_asset_id,\n    maker_amount_raw,\n    taker_amount_raw,\n    maker_amount,\n    taker_amount,\n    price,\n    size_shares,\n    notional_usd,\n    fee_amount,\n    fee_asset_id,\n    builder,\n    order_metadata,\n    condition_id,\n    market_id,\n    market_slug,\n    market_title,\n    outcome,\n    category,\n    lookup_succeeded,\n    market_active,\n    market_closed,\n    market_archived,\n    market_resolved,\n    winning_outcome,\n    imported_at_utc,\n    refreshed_at_utc\n)\nSELECT\n    raw_fill.contract_name,\n    raw_fill.contract_address,\n    raw_fill.exchange_version,\n    raw_fill.block_number,\n    raw_fill.block_timestamp_utc,\n    raw_fill.transaction_hash,\n    raw_fill.log_index,\n    raw_fill.order_hash,\n    raw_fill.maker,\n    raw_fill.taker,\n    raw_fill.side,\n    CASE raw_fill.side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE raw_fill.side END,\n    raw_fill.token_id,\n    raw_fill.maker_asset_id,\n    raw_fill.taker_asset_id,\n    raw_fill.maker_amount_raw,\n    raw_fill.taker_amount_raw,\n    raw_fill.maker_amount,\n    raw_fill.taker_amount,\n    raw_fill.price,\n    raw_fill.size_shares,\n    raw_fill.notional_usd,\n    raw_fill.fee_amount,\n    raw_fill.fee_asset_id,\n    raw_fill.builder,\n    raw_fill.metadata,\n    COALESCE(token_metadata.condition_id, ''),\n    COALESCE(token_metadata.market_id, ''),\n    COALESCE(token_metadata.market_slug, ''),\n    COALESCE(token_metadata.market_title, 'Unenriched token ' || left(raw_fill.token_id, 16)),\n    COALESCE(token_metadata.outcome, 'Unknown'),\n    token_metadata.category,\n    COALESCE(token_metadata.lookup_succeeded, false),\n    COALESCE(token_metadata.active, false),\n    COALESCE(token_metadata.closed, false),\n    COALESCE(token_metadata.archived, false),\n    COALESCE(token_metadata.resolved, false),\n    token_metadata.winning_outcome,\n    raw_fill.imported_at_utc,\n    now()\nFROM polymarket_onchain_fills raw_fill\nLEFT JOIN polymarket_onchain_token_metadata token_metadata\n       ON token_metadata.token_id = raw_fill.token_id\nWHERE raw_fill.contract_address = @ContractAddress\n  AND raw_fill.block_number BETWEEN @FromBlock AND @ToBlock\nON CONFLICT (transaction_hash, log_index) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    order_hash = excluded.order_hash,\n    maker = excluded.maker,\n    taker = excluded.taker,\n    maker_side = excluded.maker_side,\n    taker_side = excluded.taker_side,\n    token_id = excluded.token_id,\n    maker_asset_id = excluded.maker_asset_id,\n    taker_asset_id = excluded.taker_asset_id,\n    maker_amount_raw = excluded.maker_amount_raw,\n    taker_amount_raw = excluded.taker_amount_raw,\n    maker_amount = excluded.maker_amount,\n    taker_amount = excluded.taker_amount,\n    price = excluded.price,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    fee_amount = excluded.fee_amount,\n    fee_asset_id = excluded.fee_asset_id,\n    builder = excluded.builder,\n    order_metadata = excluded.order_metadata,\n    condition_id = excluded.condition_id,\n    market_id = excluded.market_id,\n    market_slug = excluded.market_slug,\n    market_title = excluded.market_title,\n    outcome = excluded.outcome,\n    category = excluded.category,\n    lookup_succeeded = excluded.lookup_succeeded,\n    market_active = excluded.market_active,\n    market_closed = excluded.market_closed,\n    market_archived = excluded.market_archived,\n    market_resolved = excluded.market_resolved,\n    winning_outcome = excluded.winning_outcome,\n    imported_at_utc = excluded.imported_at_utc,\n    refreshed_at_utc = excluded.refreshed_at_utc;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task RefreshPolymarketOnChainTradeDetailsMetadataAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<string> tokenIds, CancellationToken cancellationToken)
	{
		string[] distinctTokenIds = tokenIds.Where((string tokenId) => !string.IsNullOrWhiteSpace(tokenId)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (distinctTokenIds.Length == 0)
		{
			return;
		}
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_onchain_trade_details trade_detail\nSET\n    condition_id = COALESCE(token_metadata.condition_id, ''),\n    market_id = COALESCE(token_metadata.market_id, ''),\n    market_slug = COALESCE(token_metadata.market_slug, ''),\n    market_title = COALESCE(token_metadata.market_title, 'Unenriched token ' || left(trade_detail.token_id, 16)),\n    outcome = COALESCE(token_metadata.outcome, 'Unknown'),\n    category = token_metadata.category,\n    lookup_succeeded = COALESCE(token_metadata.lookup_succeeded, false),\n    market_active = COALESCE(token_metadata.active, false),\n    market_closed = COALESCE(token_metadata.closed, false),\n    market_archived = COALESCE(token_metadata.archived, false),\n    market_resolved = COALESCE(token_metadata.resolved, false),\n    winning_outcome = token_metadata.winning_outcome,\n    refreshed_at_utc = now()\nFROM polymarket_onchain_token_metadata token_metadata\nWHERE token_metadata.token_id = trade_detail.token_id\n  AND trade_detail.token_id = ANY(@TokenIds);");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task DeleteProcessedPolymarketOnChainRawLogsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "DELETE FROM polymarket_onchain_logs raw_log\nWHERE raw_log.contract_address = @ContractAddress\n  AND raw_log.block_number BETWEEN @FromBlock AND @ToBlock\n  AND EXISTS (\n      SELECT 1\n      FROM polymarket_onchain_trade_details trade_detail\n      WHERE trade_detail.transaction_hash = raw_log.transaction_hash\n        AND trade_detail.log_index = raw_log.log_index\n  );");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpsertOnChainIngestionCursorAsync(OnChainIngestionCursor cursor, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_ingest_cursors (\n    contract_address, contract_name, exchange_version, from_block, to_block,\n    logs_fetched, fills_stored, started_at_utc, completed_at_utc\n) VALUES (\n    @ContractAddress, @ContractName, @ExchangeVersion, @FromBlock, @ToBlock,\n    @LogsFetched, @FillsStored, @StartedAtUtc, @CompletedAtUtc\n)\nON CONFLICT (contract_address) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    exchange_version = excluded.exchange_version,\n    from_block = excluded.from_block,\n    to_block = excluded.to_block,\n    logs_fetched = excluded.logs_fetched,\n    fills_stored = excluded.fills_stored,\n    started_at_utc = excluded.started_at_utc,\n    completed_at_utc = excluded.completed_at_utc;");
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

	public async Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainIngestionCursor result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainIngestionCursor onChainIngestionCursor2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT contract_address, contract_name, exchange_version, from_block, to_block,\n       logs_fetched, fills_stored, started_at_utc, completed_at_utc\nFROM polymarket_onchain_ingest_cursors\nWHERE contract_address = @ContractAddress\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
				OnChainIngestionCursor onChainIngestionCursor;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					onChainIngestionCursor = ((await reader.ReadAsync(cancellationToken)) ? new OnChainIngestionCursor(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6), DateTimeOffsetFromUtc(reader.GetDateTime(7)), DateTimeOffsetFromUtc(reader.GetDateTime(8))) : null);
				}
				onChainIngestionCursor2 = onChainIngestionCursor;
			}
			result = onChainIngestionCursor2;
		}
		return result;
	}

	public async Task UpsertOnChainTradeCaptureCursorAsync(OnChainTradeCaptureCursor cursor, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_trade_capture_cursors (\n    contract_address, contract_name, exchange_version, next_block, last_scanned_block,\n    last_target_block, logs_fetched, captures_stored, started_at_utc, updated_at_utc\n) VALUES (\n    @ContractAddress, @ContractName, @ExchangeVersion, @NextBlock, @LastScannedBlock,\n    @LastTargetBlock, @LogsFetched, @CapturesStored, @StartedAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (contract_address) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    exchange_version = excluded.exchange_version,\n    next_block = excluded.next_block,\n    last_scanned_block = excluded.last_scanned_block,\n    last_target_block = excluded.last_target_block,\n    logs_fetched = excluded.logs_fetched,\n    captures_stored = excluded.captures_stored,\n    started_at_utc = excluded.started_at_utc,\n    updated_at_utc = excluded.updated_at_utc;");
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(cursor.ContractAddress));
		command.Parameters.AddWithValue("ContractName", cursor.ContractName);
		command.Parameters.AddWithValue("ExchangeVersion", cursor.ExchangeVersion);
		command.Parameters.AddWithValue("NextBlock", cursor.NextBlock);
		command.Parameters.AddWithValue("LastScannedBlock", cursor.LastScannedBlock);
		command.Parameters.AddWithValue("LastTargetBlock", cursor.LastTargetBlock);
		command.Parameters.AddWithValue("LogsFetched", cursor.LogsFetched);
		command.Parameters.AddWithValue("CapturesStored", cursor.CapturesStored);
		command.Parameters.AddWithValue("StartedAtUtc", UtcDateTime(cursor.StartedAtUtc));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(cursor.UpdatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<OnChainTradeCaptureCursor?> GetOnChainTradeCaptureCursorAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainTradeCaptureCursor result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainTradeCaptureCursor cursor2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT contract_address, contract_name, exchange_version, next_block,\n       last_scanned_block, last_target_block, logs_fetched, captures_stored,\n       started_at_utc, updated_at_utc\nFROM polymarket_onchain_trade_capture_cursors\nWHERE contract_address = @ContractAddress\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
				OnChainTradeCaptureCursor cursor;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					cursor = ((await reader.ReadAsync(cancellationToken)) ? new OnChainTradeCaptureCursor(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7), DateTimeOffsetFromUtc(reader.GetDateTime(8)), DateTimeOffsetFromUtc(reader.GetDateTime(9))) : null);
				}
				cursor2 = cursor;
			}
			result = cursor2;
		}
		return result;
	}

	public async Task<long?> GetLatestPolymarketOnChainFillBlockAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		long? result2;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			long? num;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT block_number\nFROM polymarket_onchain_fills\nWHERE contract_address = @ContractAddress\nORDER BY block_number DESC\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
				object result = await command.ExecuteScalarAsync(cancellationToken);
				bool flag = ((result is DBNull || result == null) ? true : false);
				num = (flag ? ((long?)null) : new long?((long)result));
			}
			result2 = num;
		}
		return result2;
	}

	public async Task<OnChainBlockRange?> GetPolymarketOnChainFillBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainBlockRange result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainBlockRange onChainBlockRange2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH first_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_fills\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number ASC\n    LIMIT 1\n),\nlast_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_fills\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number DESC\n    LIMIT 1\n)\nSELECT first_block.block_number, last_block.block_number\nFROM first_block\nCROSS JOIN last_block;"))
			{
				command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
				OnChainBlockRange onChainBlockRange;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					onChainBlockRange = ((await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) && !reader.IsDBNull(1)) ? new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1)) : null);
				}
				onChainBlockRange2 = onChainBlockRange;
			}
			result = onChainBlockRange2;
		}
		return result;
	}

	public async Task<OnChainBlockRange?> GetPolymarketOnChainWalletExecutionBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainBlockRange result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainBlockRange onChainBlockRange2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH first_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_wallet_executions\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number ASC\n    LIMIT 1\n),\nlast_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_wallet_executions\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number DESC\n    LIMIT 1\n)\nSELECT first_block.block_number, last_block.block_number\nFROM first_block\nCROSS JOIN last_block;"))
			{
				command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
				OnChainBlockRange onChainBlockRange;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					onChainBlockRange = ((await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) && !reader.IsDBNull(1)) ? new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1)) : null);
				}
				onChainBlockRange2 = onChainBlockRange;
			}
			result = onChainBlockRange2;
		}
		return result;
	}

	public async Task<OnChainBlockRange?> GetPolymarketOnChainTradeDetailsBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			OnChainBlockRange result;
			await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
			{
				OnChainBlockRange onChainBlockRange2;
				await using (NpgsqlCommand command = CreateCommand(connection, "WITH first_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_trade_details\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number ASC\n    LIMIT 1\n),\nlast_block AS (\n    SELECT block_number\n    FROM polymarket_onchain_trade_details\n    WHERE contract_address = @ContractAddress\n    ORDER BY block_number DESC\n    LIMIT 1\n)\nSELECT first_block.block_number, last_block.block_number\nFROM first_block\nCROSS JOIN last_block;"))
				{
					command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
					OnChainBlockRange onChainBlockRange;
					await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
					{
						onChainBlockRange = ((await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) && !reader.IsDBNull(1)) ? new OnChainBlockRange(reader.GetInt64(0), reader.GetInt64(1)) : null);
					}
					onChainBlockRange2 = onChainBlockRange;
				}
				result = onChainBlockRange2;
			}
			return result;
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01")
		{
			return null;
		}
	}

	public async Task RefreshPolymarketOnChainWalletDerivedDataAsync(string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (fromBlock > toBlock)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await UpsertPolymarketOnChainWalletFillsAsync(connection, transaction, contractAddress, fromBlock, toBlock, cancellationToken);
		await UpsertPolymarketOnChainWalletExecutionsAsync(connection, transaction, contractAddress, fromBlock, toBlock, cancellationToken);
		await UpsertPolymarketOnChainTradeDetailsAsync(connection, transaction, contractAddress, fromBlock, toBlock, cancellationToken);
		await QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(connection, transaction, contractAddress, fromBlock, toBlock, "derived_refresh", cancellationToken);
		await QueuePolymarketOnChainPositionRefreshTokensForRangeAsync(connection, transaction, contractAddress, fromBlock, toBlock, "derived_refresh", cancellationToken);
		await QueuePolymarketOnChainTokenMetadataRefreshTokensForRangeAsync(connection, transaction, contractAddress, fromBlock, toBlock, "derived_refresh", cancellationToken);
		await DeleteProcessedPolymarketOnChainRawLogsAsync(connection, transaction, contractAddress, fromBlock, toBlock, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketOnChainWalletExecution>> GetRecentPolymarketOnChainWalletExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainWalletExecution> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainWalletExecution> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n       transaction_hash, first_log_index, last_log_index, wallet, side, token_id,\n       fill_count, maker_fill_count, taker_fill_count, size_shares, notional_usd,\n       average_price, fees_usd, imported_at_utc\nFROM polymarket_onchain_wallet_executions\nORDER BY block_timestamp_utc DESC, block_number DESC, first_log_index DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainWalletExecution> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainWalletExecution> results = new List<PolymarketOnChainWalletExecution>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainWalletExecution(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<string>> GetOnChainTokenIdsMissingMetadataAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<string> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<string> readOnlyList2;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				await DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(connection, transaction, cancellationToken);
				IReadOnlyList<string> readOnlyList;
				await using (NpgsqlCommand command = CreateCommand(connection, "SELECT refresh_queue.token_id\nFROM polymarket_onchain_token_metadata_refresh_queue refresh_queue\nLEFT JOIN polymarket_onchain_token_metadata metadata\n  ON metadata.token_id = refresh_queue.token_id\nWHERE refresh_queue.next_attempt_at_utc <= now()\n  AND (\n      metadata.token_id IS NULL\n      OR NOT metadata.lookup_succeeded\n      OR NULLIF(metadata.category, '') IS NULL\n  )\nORDER BY refresh_queue.next_attempt_at_utc, refresh_queue.queued_at_utc, refresh_queue.token_id\nLIMIT @Limit;"))
				{
					command.Transaction = transaction;
					command.Parameters.AddWithValue("Limit", limit);
					List<string> results = new List<string>();
					await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
					{
						while (await reader.ReadAsync(cancellationToken))
						{
							results.Add(reader.GetString(0));
						}
					}
					await transaction.CommitAsync(cancellationToken);
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<PolymarketOnChainTokenMetadata?> GetPolymarketOnChainTokenMetadataAsync(string tokenId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(tokenId))
		{
			return null;
		}
		PolymarketOnChainTokenMetadata result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			PolymarketOnChainTokenMetadata polymarketOnChainTokenMetadata2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT token_id, condition_id, market_id, market_slug, market_title, outcome, outcome_index,\n       category, end_date_utc, active, closed, archived, resolved, winning_outcome,\n       clob_token_ids_json, outcomes_json, lookup_succeeded, lookup_error, raw_json,\n       last_refreshed_utc\nFROM polymarket_onchain_token_metadata\nWHERE token_id = @TokenId\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("TokenId", tokenId);
				PolymarketOnChainTokenMetadata polymarketOnChainTokenMetadata;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					polymarketOnChainTokenMetadata = ((await reader.ReadAsync(cancellationToken)) ? ReadPolymarketOnChainTokenMetadata(reader) : null);
				}
				polymarketOnChainTokenMetadata2 = polymarketOnChainTokenMetadata;
			}
			result = polymarketOnChainTokenMetadata2;
		}
		return result;
	}

	public async Task UpsertPolymarketOnChainTokenMetadataAsync(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (metadata.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (PolymarketOnChainTokenMetadata item in metadata)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_token_metadata (\n    token_id, condition_id, market_id, market_slug, market_title, outcome, outcome_index,\n    category, end_date_utc, active, closed, archived, resolved, winning_outcome,\n    clob_token_ids_json, outcomes_json, lookup_succeeded, lookup_error, raw_json,\n    last_refreshed_utc\n) VALUES (\n    @TokenId, @ConditionId, @MarketId, @MarketSlug, @MarketTitle, @Outcome, @OutcomeIndex,\n    @Category, @EndDateUtc, @Active, @Closed, @Archived, @Resolved, @WinningOutcome,\n    CAST(@ClobTokenIdsJson AS jsonb), CAST(@OutcomesJson AS jsonb), @LookupSucceeded,\n    @LookupError, CAST(@RawJson AS jsonb), @LastRefreshedUtc\n)\nON CONFLICT (token_id) DO UPDATE SET\n    condition_id = excluded.condition_id,\n    market_id = excluded.market_id,\n    market_slug = excluded.market_slug,\n    market_title = excluded.market_title,\n    outcome = excluded.outcome,\n    outcome_index = excluded.outcome_index,\n    category = excluded.category,\n    end_date_utc = excluded.end_date_utc,\n    active = excluded.active,\n    closed = excluded.closed,\n    archived = excluded.archived,\n    resolved = excluded.resolved,\n    winning_outcome = excluded.winning_outcome,\n    clob_token_ids_json = excluded.clob_token_ids_json,\n    outcomes_json = excluded.outcomes_json,\n    lookup_succeeded = excluded.lookup_succeeded,\n    lookup_error = excluded.lookup_error,\n    raw_json = excluded.raw_json,\n    last_refreshed_utc = excluded.last_refreshed_utc;");
			command.Transaction = transaction;
			AddPolymarketOnChainTokenMetadataParameters(command, item);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await QueuePolymarketOnChainPositionRefreshTokensAsync(connection, transaction, metadata.Select((PolymarketOnChainTokenMetadata polymarketOnChainTokenMetadata) => polymarketOnChainTokenMetadata.TokenId).Distinct<string>(StringComparer.OrdinalIgnoreCase), "metadata", cancellationToken);
		await RefreshPolymarketOnChainTradeDetailsMetadataAsync(connection, transaction, metadata.Select((PolymarketOnChainTokenMetadata polymarketOnChainTokenMetadata) => polymarketOnChainTokenMetadata.TokenId), cancellationToken);
		await DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(connection, transaction, cancellationToken);
		await RescheduleIncompletePolymarketOnChainTokenMetadataRefreshQueueAsync(connection, transaction, metadata.Select((PolymarketOnChainTokenMetadata polymarketOnChainTokenMetadata) => polymarketOnChainTokenMetadata.TokenId), cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketOnChainFill>> GetRecentPolymarketOnChainFillsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainFill> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainFill> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n       transaction_hash, log_index, order_hash, maker, taker, wallet, side, token_id,\n       maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw, maker_amount, taker_amount,\n       price, size_shares, notional_usd, fee_raw, fee_amount, fee_asset_id, builder, metadata, imported_at_utc\nFROM polymarket_onchain_fills\nORDER BY block_timestamp_utc DESC, block_number DESC, log_index DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainFill> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainFill> results = new List<PolymarketOnChainFill>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainFill(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<TraderOnChainStats>> GetTraderOnChainStatsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<TraderOnChainStats> results = new List<TraderOnChainStats>();
		try
		{
			await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, executions, buy_executions, sell_executions, markets_traded,\n       volume_usd, average_trade_usd, fees_usd, activity_score,\n       first_trade_utc, last_trade_utc\nFROM polymarket_onchain_wallet_activity\nORDER BY activity_score DESC, volume_usd DESC\nLIMIT @Limit;");
			command.Parameters.AddWithValue("Limit", limit);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(new TraderOnChainStats(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), DateTimeOffsetFromUtc(reader.GetDateTime(9)), DateTimeOffsetFromUtc(reader.GetDateTime(10))));
			}
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01")
		{
			return results;
		}
		return results;
	}

	public async Task<OnChainActivityRefreshResult> RefreshPolymarketOnChainWalletActivityAsync(int walletLimit = 100, int queueSeedWalletLimit = 500, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainActivityRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainActivityRefreshResult onChainActivityRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				if (!(await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken)))
				{
					int remaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					onChainActivityRefreshResult = new OnChainActivityRefreshResult(0, 0, 0, remaining);
				}
				else
				{
					int walletsQueued = await SeedMissingPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, queueSeedWalletLimit, cancellationToken) + await SeedMissingPolymarketOnChainParticipantDetailsRefreshQueueAsync(connection, transaction, queueSeedWalletLimit, cancellationToken);
					await using (NpgsqlCommand createTempCommand = CreateCommand(connection, "CREATE TEMP TABLE temp_wallet_activity_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
					{
						createTempCommand.Transaction = transaction;
						await createTempCommand.ExecuteNonQueryAsync(cancellationToken);
					}
					await using (NpgsqlCommand selectWalletsCommand = CreateCommand(connection, "WITH queued AS (\n    SELECT wallet\n    FROM polymarket_onchain_wallet_activity_refresh_queue\n    ORDER BY queued_at_utc, wallet\n    LIMIT @WalletLimit\n    FOR UPDATE SKIP LOCKED\n)\nINSERT INTO temp_wallet_activity_refresh_wallets (wallet)\nSELECT wallet\nFROM queued\nON CONFLICT (wallet) DO NOTHING;"))
					{
						selectWalletsCommand.Transaction = transaction;
						selectWalletsCommand.Parameters.AddWithValue("WalletLimit", walletLimit);
						await selectWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
					}
					int walletsProcessed = await CountTempWalletActivityRefreshWalletsAsync(connection, transaction, cancellationToken);
					if (walletsProcessed == 0)
					{
						int remaining2 = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainActivityRefreshResult = new OnChainActivityRefreshResult(walletsQueued, 0, 0, remaining2);
					}
					else
					{
						await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_activity\nWHERE wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets);"))
						{
							deleteCommand.Transaction = transaction;
							await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int walletsUpserted;
						await using (NpgsqlCommand upsertCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_activity (\n    wallet,\n    executions,\n    buy_executions,\n    sell_executions,\n    markets_traded,\n    volume_usd,\n    average_trade_usd,\n    fees_usd,\n    activity_score,\n    first_trade_utc,\n    last_trade_utc,\n    refreshed_at_utc\n)\nSELECT wallet,\n       executions,\n       buy_executions,\n       sell_executions,\n       markets_traded,\n       volume_usd,\n       average_trade_usd,\n       fees_usd,\n       volume_usd + executions + markets_traded * 5,\n       first_trade_utc,\n       last_trade_utc,\n       now()\nFROM (\n    SELECT execution.wallet,\n           COUNT(*)::integer AS executions,\n           COUNT(*) FILTER (WHERE execution.side = 'Buy')::integer AS buy_executions,\n           COUNT(*) FILTER (WHERE execution.side = 'Sell')::integer AS sell_executions,\n           COUNT(DISTINCT execution.token_id)::integer AS markets_traded,\n           COALESCE(SUM(execution.notional_usd), 0) AS volume_usd,\n           COALESCE(AVG(execution.notional_usd), 0) AS average_trade_usd,\n           COALESCE(SUM(execution.fees_usd), 0) AS fees_usd,\n           MIN(execution.block_timestamp_utc) AS first_trade_utc,\n           MAX(execution.block_timestamp_utc) AS last_trade_utc\n    FROM polymarket_onchain_wallet_executions execution\n    WHERE execution.wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets)\n    GROUP BY execution.wallet\n) activity_aggregate\nON CONFLICT (wallet) DO UPDATE SET\n    executions = excluded.executions,\n    buy_executions = excluded.buy_executions,\n    sell_executions = excluded.sell_executions,\n    markets_traded = excluded.markets_traded,\n    volume_usd = excluded.volume_usd,\n    average_trade_usd = excluded.average_trade_usd,\n    fees_usd = excluded.fees_usd,\n    activity_score = excluded.activity_score,\n    first_trade_utc = excluded.first_trade_utc,\n    last_trade_utc = excluded.last_trade_utc,\n    refreshed_at_utc = excluded.refreshed_at_utc;"))
						{
							upsertCommand.Transaction = transaction;
							upsertCommand.CommandTimeout = 300;
							walletsUpserted = await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(connection, transaction, "temp_wallet_activity_refresh_wallets", cancellationToken);
						await using (NpgsqlCommand clearQueueCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_activity_refresh_queue\nWHERE wallet IN (SELECT wallet FROM temp_wallet_activity_refresh_wallets);"))
						{
							clearQueueCommand.Transaction = transaction;
							await clearQueueCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainActivityRefreshResult = new OnChainActivityRefreshResult(walletsQueued, walletsProcessed, walletsUpserted, queueRemaining);
					}
				}
			}
			result = onChainActivityRefreshResult;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketOnChainWalletPosition>> GetPolymarketOnChainWalletPositionsAsync(int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainWalletPosition> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainWalletPosition> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, token_id, condition_id, market_id, market_slug, market_title, outcome,\n       category, lookup_succeeded, market_resolved, winning_outcome,\n       executions, buy_executions, sell_executions, buy_shares, sell_shares, net_shares,\n       buy_notional_usd, sell_notional_usd, net_cost_usd, fees_usd, average_buy_price,\n       average_sell_price, volume_usd, resolved_pnl_usd, position_status,\n       first_trade_utc, last_trade_utc\nFROM polymarket_onchain_wallet_positions\nORDER BY absolute_net_cost_usd DESC, volume_usd DESC, last_trade_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainWalletPosition> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainWalletPosition> results = new List<PolymarketOnChainWalletPosition>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainWalletPosition(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<OnChainPositionRefreshResult> RefreshPolymarketOnChainWalletPositionsAsync(int tokenLimit = 50, int queueSeedTokenLimit = 500, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainPositionRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainPositionRefreshResult onChainPositionRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				if (!(await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken)))
				{
					int remaining = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					onChainPositionRefreshResult = new OnChainPositionRefreshResult(0, 0, 0, remaining);
				}
				else
				{
					int tokensQueued = await SeedMissingPolymarketOnChainPositionRefreshTokensAsync(connection, transaction, queueSeedTokenLimit, cancellationToken);
					await using (NpgsqlCommand createTemp = CreateCommand(connection, "CREATE TEMP TABLE temp_position_refresh_tokens (token_id text PRIMARY KEY) ON COMMIT DROP;"))
					{
						createTemp.Transaction = transaction;
						await createTemp.ExecuteNonQueryAsync(cancellationToken);
					}
					await using (NpgsqlCommand pickCommand = CreateCommand(connection, "WITH picked AS (\n    SELECT token_id\n    FROM polymarket_onchain_position_refresh_queue\n    ORDER BY queued_at_utc\n    LIMIT @TokenLimit\n    FOR UPDATE SKIP LOCKED\n)\nINSERT INTO temp_position_refresh_tokens (token_id)\nSELECT token_id\nFROM picked;"))
					{
						pickCommand.Transaction = transaction;
						pickCommand.Parameters.AddWithValue("TokenLimit", tokenLimit);
						await pickCommand.ExecuteNonQueryAsync(cancellationToken);
					}
					int tokensProcessed = await CountTempPositionRefreshTokensAsync(connection, transaction, cancellationToken);
					if (tokensProcessed == 0)
					{
						int remaining2 = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainPositionRefreshResult = new OnChainPositionRefreshResult(tokensQueued, 0, 0, remaining2);
					}
					else
					{
						await using (NpgsqlCommand createWalletsCommand = CreateCommand(connection, "CREATE TEMP TABLE temp_position_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
						{
							createWalletsCommand.Transaction = transaction;
							await createWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand createCategoryPairsCommand = CreateCommand(connection, "CREATE TEMP TABLE temp_wallet_category_performance_refresh_pairs (wallet text NOT NULL, category text NOT NULL, PRIMARY KEY (wallet, category)) ON COMMIT DROP;"))
						{
							createCategoryPairsCommand.Transaction = transaction;
							await createCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand captureWalletsCommand = CreateCommand(connection, "INSERT INTO temp_position_refresh_wallets (wallet)\nSELECT DISTINCT wallet\nFROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\nON CONFLICT (wallet) DO NOTHING;"))
						{
							captureWalletsCommand.Transaction = transaction;
							captureWalletsCommand.CommandTimeout = 300;
							await captureWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand captureCategoryPairsCommand = CreateCommand(connection, "INSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)\nSELECT DISTINCT wallet, COALESCE(NULLIF(category, ''), 'unknown')\nFROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\nON CONFLICT (wallet, category) DO NOTHING;"))
						{
							captureCategoryPairsCommand.Transaction = transaction;
							captureCategoryPairsCommand.CommandTimeout = 300;
							await captureCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens);"))
						{
							deleteCommand.Transaction = transaction;
							deleteCommand.CommandTimeout = 300;
							await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int positionsUpserted;
						await using (NpgsqlCommand insertCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_positions (\n    wallet, token_id, condition_id, market_id, market_slug, market_title, outcome,\n    category, lookup_succeeded, market_resolved, winning_outcome,\n    executions, buy_executions, sell_executions, buy_shares, sell_shares, net_shares,\n    buy_notional_usd, sell_notional_usd, net_cost_usd, absolute_net_cost_usd,\n    fees_usd, average_buy_price, average_sell_price, volume_usd, resolved_pnl_usd,\n    position_status, first_trade_utc, last_trade_utc, latest_execution_imported_at_utc,\n    metadata_refreshed_at_utc, refreshed_at_utc\n)\nWITH grouped AS (\n    SELECT\n        execution.wallet,\n        execution.token_id,\n        COALESCE(NULLIF(metadata.condition_id, ''), execution.token_id) AS condition_id,\n        COALESCE(NULLIF(metadata.market_id, ''), '') AS market_id,\n        COALESCE(NULLIF(metadata.market_slug, ''), '') AS market_slug,\n        COALESCE(NULLIF(metadata.market_title, ''), 'Unenriched token ' || left(execution.token_id, 16)) AS market_title,\n        COALESCE(NULLIF(metadata.outcome, ''), 'Unknown') AS outcome,\n        metadata.category,\n        COALESCE(metadata.lookup_succeeded, false) AS lookup_succeeded,\n        COALESCE(metadata.resolved, false) AS market_resolved,\n        metadata.winning_outcome,\n        metadata.last_refreshed_utc AS metadata_refreshed_at_utc,\n        COUNT(*)::integer AS executions,\n        COUNT(*) FILTER (WHERE execution.side = 'Buy')::integer AS buy_executions,\n        COUNT(*) FILTER (WHERE execution.side = 'Sell')::integer AS sell_executions,\n        COALESCE(SUM(execution.size_shares) FILTER (WHERE execution.side = 'Buy'), 0)::numeric AS buy_shares,\n        COALESCE(SUM(execution.size_shares) FILTER (WHERE execution.side = 'Sell'), 0)::numeric AS sell_shares,\n        COALESCE(SUM(execution.notional_usd) FILTER (WHERE execution.side = 'Buy'), 0)::numeric AS buy_notional_usd,\n        COALESCE(SUM(execution.notional_usd) FILTER (WHERE execution.side = 'Sell'), 0)::numeric AS sell_notional_usd,\n        COALESCE(SUM(execution.fees_usd), 0)::numeric AS fees_usd,\n        COALESCE(SUM(execution.notional_usd), 0)::numeric AS volume_usd,\n        MIN(execution.block_timestamp_utc) AS first_trade_utc,\n        MAX(execution.block_timestamp_utc) AS last_trade_utc,\n        MAX(execution.imported_at_utc) AS latest_execution_imported_at_utc\n    FROM polymarket_onchain_wallet_executions execution\n    LEFT JOIN polymarket_onchain_token_metadata metadata\n      ON metadata.token_id = execution.token_id\n    WHERE execution.token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\n    GROUP BY\n        execution.wallet,\n        execution.token_id,\n        metadata.condition_id,\n        metadata.market_id,\n        metadata.market_slug,\n        metadata.market_title,\n        metadata.outcome,\n        metadata.category,\n        metadata.lookup_succeeded,\n        metadata.resolved,\n        metadata.winning_outcome,\n        metadata.last_refreshed_utc\n),\npositions AS (\n    SELECT\n        grouped.*,\n        (buy_shares - sell_shares)::numeric AS net_shares,\n        (buy_notional_usd - sell_notional_usd + fees_usd)::numeric AS net_cost_usd,\n        CASE WHEN buy_shares = 0 THEN 0 ELSE buy_notional_usd / buy_shares END AS average_buy_price,\n        CASE WHEN sell_shares = 0 THEN 0 ELSE sell_notional_usd / sell_shares END AS average_sell_price\n    FROM grouped\n)\nSELECT\n    wallet,\n    token_id,\n    condition_id,\n    market_id,\n    market_slug,\n    market_title,\n    outcome,\n    category,\n    lookup_succeeded,\n    market_resolved,\n    winning_outcome,\n    executions,\n    buy_executions,\n    sell_executions,\n    buy_shares,\n    sell_shares,\n    net_shares,\n    buy_notional_usd,\n    sell_notional_usd,\n    net_cost_usd,\n    abs(net_cost_usd),\n    fees_usd,\n    average_buy_price,\n    average_sell_price,\n    volume_usd,\n    CASE\n        WHEN market_resolved AND winning_outcome IS NOT NULL\n        THEN (CASE WHEN lower(outcome) = lower(winning_outcome) THEN net_shares ELSE 0 END) - net_cost_usd\n        ELSE NULL::numeric\n    END,\n    CASE\n        WHEN market_resolved THEN 'Resolved'\n        WHEN abs(net_shares) < 0.00000001 THEN 'Flat'\n        ELSE 'Open'\n    END,\n    first_trade_utc,\n    last_trade_utc,\n    latest_execution_imported_at_utc,\n    metadata_refreshed_at_utc,\n    now()\nFROM positions;"))
						{
							insertCommand.Transaction = transaction;
							insertCommand.CommandTimeout = 300;
							positionsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand pickWalletsCommand = CreateCommand(connection, "INSERT INTO temp_position_refresh_wallets (wallet)\nSELECT DISTINCT wallet\nFROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\nON CONFLICT (wallet) DO NOTHING;"))
						{
							pickWalletsCommand.Transaction = transaction;
							pickWalletsCommand.CommandTimeout = 300;
							await pickWalletsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand pickCategoryPairsCommand = CreateCommand(connection, "INSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)\nSELECT DISTINCT wallet, COALESCE(NULLIF(category, ''), 'unknown')\nFROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\nON CONFLICT (wallet, category) DO NOTHING;"))
						{
							pickCategoryPairsCommand.Transaction = transaction;
							pickCategoryPairsCommand.CommandTimeout = 300;
							await pickCategoryPairsCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(connection, transaction, "temp_position_refresh_wallets", cancellationToken);
						await QueuePolymarketOnChainWalletPerformanceRefreshForPositionTokensAsync(connection, transaction, "position_refresh", cancellationToken);
						await QueuePolymarketOnChainWalletCategoryPerformanceRefreshForPositionPairsAsync(connection, transaction, "position_refresh", cancellationToken);
						await using (NpgsqlCommand clearCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_position_refresh_queue\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens);"))
						{
							clearCommand.Transaction = transaction;
							await clearCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int queueRemaining = await CountPolymarketOnChainPositionRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainPositionRefreshResult = new OnChainPositionRefreshResult(tokensQueued, tokensProcessed, positionsUpserted, queueRemaining);
					}
				}
			}
			result = onChainPositionRefreshResult;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketOnChainWalletPerformance>> GetPolymarketOnChainWalletPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainWalletPerformance> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainWalletPerformance> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, positions_count, open_positions, flat_positions, resolved_positions,\n       profitable_resolved_positions, losing_resolved_positions, markets_traded,\n       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,\n       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,\n       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc\nFROM polymarket_onchain_wallet_performance\nORDER BY score DESC, resolved_pnl_usd DESC, volume_usd DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainWalletPerformance> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainWalletPerformance> results = new List<PolymarketOnChainWalletPerformance>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainWalletPerformance(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<OnChainPerformanceRefreshResult> RefreshPolymarketOnChainWalletPerformanceAsync(int walletLimit = 100, int queueSeedWalletLimit = 500, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainPerformanceRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainPerformanceRefreshResult onChainPerformanceRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				if (!(await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken)))
				{
					int remaining = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					onChainPerformanceRefreshResult = new OnChainPerformanceRefreshResult(0, 0, 0, remaining);
				}
				else
				{
					int walletsQueued = await SeedMissingPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, queueSeedWalletLimit, cancellationToken);
					await using (NpgsqlCommand createTemp = CreateCommand(connection, "CREATE TEMP TABLE temp_wallet_performance_refresh_wallets (wallet text PRIMARY KEY) ON COMMIT DROP;"))
					{
						createTemp.Transaction = transaction;
						await createTemp.ExecuteNonQueryAsync(cancellationToken);
					}
					await using (NpgsqlCommand pickCommand = CreateCommand(connection, "WITH picked AS (\n    SELECT wallet\n    FROM polymarket_onchain_wallet_performance_refresh_queue\n    ORDER BY queued_at_utc\n    LIMIT @WalletLimit\n    FOR UPDATE SKIP LOCKED\n)\nINSERT INTO temp_wallet_performance_refresh_wallets (wallet)\nSELECT wallet\nFROM picked;"))
					{
						pickCommand.Transaction = transaction;
						pickCommand.Parameters.AddWithValue("WalletLimit", walletLimit);
						await pickCommand.ExecuteNonQueryAsync(cancellationToken);
					}
					int walletsProcessed = await CountTempWalletPerformanceRefreshWalletsAsync(connection, transaction, cancellationToken);
					if (walletsProcessed == 0)
					{
						int remaining2 = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainPerformanceRefreshResult = new OnChainPerformanceRefreshResult(walletsQueued, 0, 0, remaining2);
					}
					else
					{
						await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_performance\nWHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets);"))
						{
							deleteCommand.Transaction = transaction;
							deleteCommand.CommandTimeout = 300;
							await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int walletsUpserted;
						await using (NpgsqlCommand insertCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_performance (\n    wallet, positions_count, open_positions, flat_positions, resolved_positions,\n    profitable_resolved_positions, losing_resolved_positions, markets_traded,\n    volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,\n    resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,\n    score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc\n)\nWITH metrics AS (\n    SELECT\n        wallet,\n        COUNT(*)::integer AS positions_count,\n        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Flat')::integer AS flat_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved')::integer AS resolved_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,\n        COUNT(DISTINCT condition_id)::integer AS markets_traded,\n        COALESCE(SUM(volume_usd), 0)::numeric AS volume_usd,\n        COALESCE(SUM(volume_usd) FILTER (WHERE position_status = 'Resolved'), 0)::numeric AS resolved_volume_usd,\n        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_exposure_usd,\n        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Resolved' AND resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,\n        COALESCE(SUM(resolved_pnl_usd), 0)::numeric AS resolved_pnl_usd,\n        COALESCE(AVG(abs(net_cost_usd)), 0)::numeric AS average_position_size_usd,\n        MIN(first_trade_utc) AS first_active_utc,\n        MAX(last_trade_utc) AS last_active_utc\n    FROM polymarket_onchain_wallet_positions\n    WHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets)\n    GROUP BY wallet\n),\nscored AS (\n    SELECT\n        metrics.*,\n        CASE WHEN resolved_cost_usd = 0 THEN 0 ELSE resolved_pnl_usd / resolved_cost_usd * 100 END AS resolved_roi_pct,\n        CASE WHEN resolved_positions = 0 THEN 0 ELSE profitable_resolved_positions::numeric / resolved_positions * 100 END AS win_rate_pct\n    FROM metrics\n)\nSELECT\n    wallet,\n    positions_count,\n    open_positions,\n    flat_positions,\n    resolved_positions,\n    profitable_resolved_positions,\n    losing_resolved_positions,\n    markets_traded,\n    volume_usd,\n    resolved_volume_usd,\n    open_exposure_usd,\n    resolved_cost_usd,\n    resolved_pnl_usd,\n    resolved_roi_pct,\n    win_rate_pct,\n    average_position_size_usd,\n    (\n        resolved_pnl_usd +\n        resolved_roi_pct * 2 +\n        profitable_resolved_positions * 5 +\n        ln(volume_usd + 1) +\n        LEAST(resolved_positions, 50) * 2 -\n        open_exposure_usd * 0.02 -\n        CASE WHEN resolved_positions < 5 THEN (5 - resolved_positions) * 10 ELSE 0 END\n    )::numeric AS score,\n    CASE\n        WHEN resolved_positions >= 25 AND volume_usd >= 1000 THEN 'High'\n        WHEN resolved_positions >= 10 THEN 'Medium'\n        WHEN resolved_positions >= 3 THEN 'Low'\n        ELSE 'Thin'\n    END AS sample_quality,\n    first_active_utc,\n    last_active_utc,\n    now()\nFROM scored;"))
						{
							insertCommand.Transaction = transaction;
							insertCommand.CommandTimeout = 300;
							walletsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(connection, transaction, "temp_wallet_performance_refresh_wallets", cancellationToken);
						await using (NpgsqlCommand clearCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_performance_refresh_queue\nWHERE wallet IN (SELECT wallet FROM temp_wallet_performance_refresh_wallets);"))
						{
							clearCommand.Transaction = transaction;
							await clearCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int queueRemaining = await CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainPerformanceRefreshResult = new OnChainPerformanceRefreshResult(walletsQueued, walletsProcessed, walletsUpserted, queueRemaining);
					}
				}
			}
			result = onChainPerformanceRefreshResult;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>> GetPolymarketOnChainWalletCategoryPerformanceAsync(string? category = null, int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainWalletCategoryPerformance> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainWalletCategoryPerformance> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, category, positions_count, open_positions, flat_positions, resolved_positions,\n       profitable_resolved_positions, losing_resolved_positions, markets_traded,\n       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,\n       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,\n       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc\nFROM polymarket_onchain_wallet_category_performance\nWHERE @Category IS NULL OR category = @Category\nORDER BY score DESC, resolved_pnl_usd DESC, volume_usd DESC\nLIMIT @Limit;"))
			{
				command.Parameters.Add("Category", NpgsqlDbType.Text).Value = ((object)category) ?? ((object)DBNull.Value);
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainWalletCategoryPerformance> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainWalletCategoryPerformance> results = new List<PolymarketOnChainWalletCategoryPerformance>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainWalletCategoryPerformance(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<PolymarketOnChainWalletCategoryPerformance?> GetPolymarketOnChainWalletCategoryPerformanceAsync(string wallet, string category, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(wallet) || string.IsNullOrWhiteSpace(category))
		{
			return null;
		}
		PolymarketOnChainWalletCategoryPerformance result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			PolymarketOnChainWalletCategoryPerformance polymarketOnChainWalletCategoryPerformance2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, category, positions_count, open_positions, flat_positions, resolved_positions,\n       profitable_resolved_positions, losing_resolved_positions, markets_traded,\n       volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,\n       resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,\n       score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc\nFROM polymarket_onchain_wallet_category_performance\nWHERE wallet = @Wallet\n  AND category = @Category\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("Wallet", wallet);
				command.Parameters.AddWithValue("Category", category);
				PolymarketOnChainWalletCategoryPerformance polymarketOnChainWalletCategoryPerformance;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					polymarketOnChainWalletCategoryPerformance = ((await reader.ReadAsync(cancellationToken)) ? ReadPolymarketOnChainWalletCategoryPerformance(reader) : null);
				}
				polymarketOnChainWalletCategoryPerformance2 = polymarketOnChainWalletCategoryPerformance;
			}
			result = polymarketOnChainWalletCategoryPerformance2;
		}
		return result;
	}

	public async Task<OnChainCategoryPerformanceRefreshResult> RefreshPolymarketOnChainWalletCategoryPerformanceAsync(int pairLimit = 500, int queueSeedPairLimit = 1000, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainCategoryPerformanceRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainCategoryPerformanceRefreshResult onChainCategoryPerformanceRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				if (!(await TryAcquireOnChainDerivedRefreshLockAsync(connection, transaction, cancellationToken)))
				{
					int remaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					onChainCategoryPerformanceRefreshResult = new OnChainCategoryPerformanceRefreshResult(0, 0, 0, remaining);
				}
				else
				{
					int pairsQueued = await SeedMissingPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, queueSeedPairLimit, cancellationToken);
					await using (NpgsqlCommand createTemp = CreateCommand(connection, "CREATE TEMP TABLE temp_wallet_category_performance_refresh_pairs (wallet text NOT NULL, category text NOT NULL, PRIMARY KEY (wallet, category)) ON COMMIT DROP;"))
					{
						createTemp.Transaction = transaction;
						await createTemp.ExecuteNonQueryAsync(cancellationToken);
					}
					await using (NpgsqlCommand pickCommand = CreateCommand(connection, "WITH picked AS (\n    SELECT wallet, category\n    FROM polymarket_onchain_wallet_category_performance_refresh_queue\n    ORDER BY queued_at_utc, category, wallet\n    LIMIT @PairLimit\n    FOR UPDATE SKIP LOCKED\n)\nINSERT INTO temp_wallet_category_performance_refresh_pairs (wallet, category)\nSELECT wallet, category\nFROM picked\nON CONFLICT (wallet, category) DO NOTHING;"))
					{
						pickCommand.Transaction = transaction;
						pickCommand.Parameters.AddWithValue("PairLimit", pairLimit);
						await pickCommand.ExecuteNonQueryAsync(cancellationToken);
					}
					int pairsProcessed = await CountTempWalletCategoryPerformanceRefreshPairsAsync(connection, transaction, cancellationToken);
					if (pairsProcessed == 0)
					{
						int remaining2 = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainCategoryPerformanceRefreshResult = new OnChainCategoryPerformanceRefreshResult(pairsQueued, 0, 0, remaining2);
					}
					else
					{
						await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_category_performance performance\nUSING temp_wallet_category_performance_refresh_pairs pair\nWHERE performance.wallet = pair.wallet\n  AND performance.category = pair.category;"))
						{
							deleteCommand.Transaction = transaction;
							deleteCommand.CommandTimeout = 300;
							await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int pairsUpserted;
						await using (NpgsqlCommand insertCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_category_performance (\n    wallet, category, positions_count, open_positions, flat_positions, resolved_positions,\n    profitable_resolved_positions, losing_resolved_positions, markets_traded,\n    volume_usd, resolved_volume_usd, open_exposure_usd, resolved_cost_usd,\n    resolved_pnl_usd, resolved_roi_pct, win_rate_pct, average_position_size_usd,\n    score, sample_quality, first_active_utc, last_active_utc, refreshed_at_utc\n)\nWITH metrics AS (\n    SELECT\n        position.wallet,\n        COALESCE(NULLIF(position.category, ''), 'unknown') AS category,\n        COUNT(*)::integer AS positions_count,\n        COUNT(*) FILTER (WHERE position_status = 'Open')::integer AS open_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Flat')::integer AS flat_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved')::integer AS resolved_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,\n        COUNT(*) FILTER (WHERE position_status = 'Resolved' AND COALESCE(resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,\n        COUNT(DISTINCT condition_id)::integer AS markets_traded,\n        COALESCE(SUM(volume_usd), 0)::numeric AS volume_usd,\n        COALESCE(SUM(volume_usd) FILTER (WHERE position_status = 'Resolved'), 0)::numeric AS resolved_volume_usd,\n        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Open'), 0)::numeric AS open_exposure_usd,\n        COALESCE(SUM(abs(net_cost_usd)) FILTER (WHERE position_status = 'Resolved' AND resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,\n        COALESCE(SUM(resolved_pnl_usd), 0)::numeric AS resolved_pnl_usd,\n        COALESCE(AVG(abs(net_cost_usd)), 0)::numeric AS average_position_size_usd,\n        MIN(first_trade_utc) AS first_active_utc,\n        MAX(last_trade_utc) AS last_active_utc\n    FROM polymarket_onchain_wallet_positions position\n    WHERE EXISTS (\n        SELECT 1\n        FROM temp_wallet_category_performance_refresh_pairs pair\n        WHERE pair.wallet = position.wallet\n          AND pair.category = COALESCE(NULLIF(position.category, ''), 'unknown')\n    )\n    GROUP BY position.wallet, COALESCE(NULLIF(position.category, ''), 'unknown')\n),\nscored AS (\n    SELECT\n        metrics.*,\n        CASE WHEN resolved_cost_usd = 0 THEN 0 ELSE resolved_pnl_usd / resolved_cost_usd * 100 END AS resolved_roi_pct,\n        CASE WHEN resolved_positions = 0 THEN 0 ELSE profitable_resolved_positions::numeric / resolved_positions * 100 END AS win_rate_pct\n    FROM metrics\n)\nSELECT\n    wallet,\n    category,\n    positions_count,\n    open_positions,\n    flat_positions,\n    resolved_positions,\n    profitable_resolved_positions,\n    losing_resolved_positions,\n    markets_traded,\n    volume_usd,\n    resolved_volume_usd,\n    open_exposure_usd,\n    resolved_cost_usd,\n    resolved_pnl_usd,\n    resolved_roi_pct,\n    win_rate_pct,\n    average_position_size_usd,\n    (\n        resolved_pnl_usd +\n        resolved_roi_pct * 2 +\n        profitable_resolved_positions * 5 +\n        ln(volume_usd + 1) +\n        LEAST(resolved_positions, 50) * 2 -\n        open_exposure_usd * 0.02 -\n        CASE WHEN resolved_positions < 5 THEN (5 - resolved_positions) * 10 ELSE 0 END\n    )::numeric AS score,\n    CASE\n        WHEN resolved_positions >= 25 AND volume_usd >= 1000 THEN 'High'\n        WHEN resolved_positions >= 10 THEN 'Medium'\n        WHEN resolved_positions >= 3 THEN 'Low'\n        ELSE 'Thin'\n    END AS sample_quality,\n    first_active_utc,\n    last_active_utc,\n    now()\nFROM scored;"))
						{
							insertCommand.Transaction = transaction;
							insertCommand.CommandTimeout = 300;
							pairsUpserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						await using (NpgsqlCommand clearCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_wallet_category_performance_refresh_queue queue\nUSING temp_wallet_category_performance_refresh_pairs pair\nWHERE queue.wallet = pair.wallet\n  AND queue.category = pair.category;"))
						{
							clearCommand.Transaction = transaction;
							await clearCommand.ExecuteNonQueryAsync(cancellationToken);
						}
						int queueRemaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
						await transaction.CommitAsync(cancellationToken);
						onChainCategoryPerformanceRefreshResult = new OnChainCategoryPerformanceRefreshResult(pairsQueued, pairsProcessed, pairsUpserted, queueRemaining);
					}
				}
			}
			result = onChainCategoryPerformanceRefreshResult;
		}
		return result;
	}

	public async Task<OnChainSignalCandidateQueueRefreshResult> RefreshPolymarketOnChainSignalCandidateQueueAsync(int queueSeedLimit = 1000, int retryLimit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		OnChainSignalCandidateQueueRefreshResult result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OnChainSignalCandidateQueueRefreshResult onChainSignalCandidateQueueRefreshResult;
			await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				await using (NpgsqlCommand ensureCursorCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_signal_candidate_backfill_cursors (\n    cursor_name, last_block_timestamp_utc, last_block_number, last_log_index,\n    last_participant_role, completed, updated_at_utc\n) VALUES (\n    'default', NULL, NULL, NULL, NULL, false, now()\n)\nON CONFLICT (cursor_name) DO NOTHING;"))
				{
					ensureCursorCommand.Transaction = transaction;
					await ensureCursorCommand.ExecuteNonQueryAsync(cancellationToken);
				}
				int sourcesQueued = 0;
				await using (NpgsqlCommand seedBackfillCommand = CreateCommand(connection, "WITH cursor_row AS (\n    SELECT cursor_name, last_block_timestamp_utc, last_block_number, last_log_index,\n           last_participant_role, completed\n    FROM polymarket_onchain_signal_candidate_backfill_cursors\n    WHERE cursor_name = 'default'\n    FOR UPDATE\n),\nselected AS (\n    SELECT wallet_fill.source_fill_id, wallet_fill.role, wallet_fill.block_timestamp_utc,\n           wallet_fill.block_number, wallet_fill.log_index\n    FROM polymarket_onchain_wallet_fills wallet_fill\n    CROSS JOIN cursor_row cursor\n    WHERE NOT cursor.completed\n      AND (\n          cursor.last_block_timestamp_utc IS NULL\n          OR (\n              wallet_fill.block_timestamp_utc,\n              wallet_fill.block_number,\n              wallet_fill.log_index,\n              wallet_fill.role\n          ) > (\n              cursor.last_block_timestamp_utc,\n              cursor.last_block_number,\n              cursor.last_log_index,\n              cursor.last_participant_role\n          )\n      )\n    ORDER BY wallet_fill.block_timestamp_utc,\n             wallet_fill.block_number,\n             wallet_fill.log_index,\n             wallet_fill.role\n    LIMIT @QueueSeedLimit\n),\ninserted AS (\n    INSERT INTO polymarket_onchain_signal_candidate_refresh_queue (\n        source_fill_id, participant_role, block_timestamp_utc, block_number,\n        log_index, queued_at_utc, next_attempt_at_utc\n    )\n    SELECT selected.source_fill_id, selected.role, selected.block_timestamp_utc,\n           selected.block_number, selected.log_index, now(), now()\n    FROM selected\n    LEFT JOIN polymarket_onchain_signal_candidates candidate\n      ON candidate.source_fill_id = selected.source_fill_id\n     AND candidate.participant_role = selected.role\n    WHERE candidate.id IS NULL\n    ON CONFLICT (source_fill_id, participant_role) DO NOTHING\n    RETURNING 1\n),\nlast_selected AS (\n    SELECT block_timestamp_utc, block_number, log_index, role\n    FROM selected\n    ORDER BY block_timestamp_utc DESC, block_number DESC, log_index DESC, role DESC\n    LIMIT 1\n),\nadvanced AS (\n    UPDATE polymarket_onchain_signal_candidate_backfill_cursors cursor\n    SET last_block_timestamp_utc = COALESCE((SELECT block_timestamp_utc FROM last_selected), cursor.last_block_timestamp_utc),\n        last_block_number = COALESCE((SELECT block_number FROM last_selected), cursor.last_block_number),\n        last_log_index = COALESCE((SELECT log_index FROM last_selected), cursor.last_log_index),\n        last_participant_role = COALESCE((SELECT role FROM last_selected), cursor.last_participant_role),\n        completed = NOT EXISTS (SELECT 1 FROM selected),\n        updated_at_utc = now()\n    WHERE cursor.cursor_name = 'default'\n    RETURNING completed\n)\nSELECT count(*)::integer AS sources_queued\nFROM inserted;"))
				{
					seedBackfillCommand.Transaction = transaction;
					seedBackfillCommand.CommandTimeout = 300;
					seedBackfillCommand.Parameters.AddWithValue("QueueSeedLimit", queueSeedLimit);
					sourcesQueued = Convert.ToInt32(await seedBackfillCommand.ExecuteScalarAsync(cancellationToken));
				}
				int retriesQueued = 0;
				await using (NpgsqlCommand seedRetriesCommand = CreateCommand(connection, "WITH selected AS (\n    SELECT source_fill_id, participant_role, block_timestamp_utc, block_number, log_index\n    FROM polymarket_onchain_signal_candidates\n    WHERE decision_status = 'Rejected'\n      AND updated_at_utc <= now() - interval '10 minutes'\n      AND decision_code IN (\n          'missing_market_metadata',\n          'missing_market_category',\n          'missing_leader_category_performance',\n          'leader_category_performance_stale',\n          'leader_trade_too_small',\n          'unsupported_side',\n          'market_inactive',\n          'market_resolved'\n      )\n    ORDER BY updated_at_utc, block_timestamp_utc, block_number, log_index, participant_role\n    LIMIT @RetryLimit\n),\ninserted AS (\n    INSERT INTO polymarket_onchain_signal_candidate_refresh_queue (\n        source_fill_id, participant_role, block_timestamp_utc, block_number,\n        log_index, queued_at_utc, next_attempt_at_utc\n    )\n    SELECT source_fill_id, participant_role, block_timestamp_utc, block_number,\n           log_index, now(), now()\n    FROM selected\n    ON CONFLICT (source_fill_id, participant_role) DO NOTHING\n    RETURNING 1\n)\nSELECT count(*)::integer AS retries_queued\nFROM inserted;"))
				{
					seedRetriesCommand.Transaction = transaction;
					seedRetriesCommand.CommandTimeout = 300;
					seedRetriesCommand.Parameters.AddWithValue("RetryLimit", retryLimit);
					retriesQueued = Convert.ToInt32(await seedRetriesCommand.ExecuteScalarAsync(cancellationToken));
				}
				int queueRemaining = await CountPolymarketOnChainSignalCandidateRefreshQueueAsync(connection, transaction, cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				onChainSignalCandidateQueueRefreshResult = new OnChainSignalCandidateQueueRefreshResult(sourcesQueued, retriesQueued, queueRemaining);
			}
			result = onChainSignalCandidateQueueRefreshResult;
		}
		return result;
	}

	public async Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetPendingOnChainPaperSignalCandidatesAsync(string ratingTimePeriod, string ratingOrderBy, int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<OnChainPaperSignalCandidate> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<OnChainPaperSignalCandidate> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH pending_captures AS MATERIALIZED (\n    SELECT candidate.*\n    FROM (\n        SELECT capture.id AS capture_id, capture.contract_name, capture.contract_address,\n               capture.exchange_version, capture.block_number, capture.block_timestamp_utc,\n               capture.transaction_hash, capture.log_index, capture.order_hash,\n               capture.maker, capture.taker, capture.side, capture.token_id,\n               capture.price, capture.size_shares, capture.notional_usd,\n               maker_processed.id IS NULL AS maker_pending,\n               taker_processed.id IS NULL AS taker_pending\n        FROM polymarket_onchain_trade_captures capture\n        LEFT JOIN polymarket_onchain_paper_signal_results maker_processed\n          ON maker_processed.transaction_hash = capture.transaction_hash\n         AND maker_processed.log_index = capture.log_index\n         AND maker_processed.participant_role = 'Maker'\n        LEFT JOIN polymarket_onchain_paper_signal_results taker_processed\n          ON taker_processed.transaction_hash = capture.transaction_hash\n         AND taker_processed.log_index = capture.log_index\n         AND taker_processed.participant_role = 'Taker'\n        WHERE NOT capture.removed\n    ) candidate\n    WHERE candidate.maker_pending OR candidate.taker_pending\n    ORDER BY candidate.block_timestamp_utc, candidate.block_number, candidate.log_index\n    LIMIT @Limit\n),\nparticipants AS MATERIALIZED (\n    SELECT capture.capture_id, capture.contract_name, capture.contract_address,\n           capture.exchange_version, capture.block_number, capture.block_timestamp_utc,\n           capture.transaction_hash, capture.log_index, capture.order_hash,\n           'Maker'::text AS participant_role, lower(capture.maker) AS wallet,\n           lower(capture.taker) AS counterparty_wallet, capture.side AS participant_side,\n           capture.token_id, capture.price, capture.size_shares, capture.notional_usd\n    FROM pending_captures capture\n    WHERE capture.maker_pending\n    UNION ALL\n    SELECT capture.capture_id, capture.contract_name, capture.contract_address,\n           capture.exchange_version, capture.block_number, capture.block_timestamp_utc,\n           capture.transaction_hash, capture.log_index, capture.order_hash,\n           'Taker'::text AS participant_role, lower(capture.taker) AS wallet,\n           lower(capture.maker) AS counterparty_wallet,\n           CASE capture.side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE 'Unknown' END AS participant_side,\n           capture.token_id, capture.price, capture.size_shares, capture.notional_usd\n    FROM pending_captures capture\n    WHERE capture.taker_pending\n)\nSELECT participant.capture_id, participant.contract_name, participant.contract_address,\n       participant.exchange_version, participant.block_number, participant.block_timestamp_utc,\n       participant.transaction_hash, participant.log_index, participant.order_hash,\n       participant.participant_role, participant.wallet, participant.counterparty_wallet,\n       participant.participant_side, participant.token_id, participant.price,\n       participant.size_shares, participant.notional_usd,\n       COALESCE(gamma.condition_id, '') AS condition_id,\n       COALESCE(gamma.market_id, '') AS market_id,\n       COALESCE(gamma.slug, '') AS market_slug,\n       COALESCE(gamma.question, '') AS market_title,\n       COALESCE(gamma.outcome, '') AS outcome,\n       gamma.category,\n       gamma.market_id IS NOT NULL AS market_found,\n       COALESCE(gamma.active, false) AS market_active,\n       COALESCE(gamma.closed, false) AS market_closed,\n       COALESCE(gamma.archived, false) AS market_archived,\n       COALESCE(gamma.restricted, false) AS market_restricted,\n       COALESCE(gamma.accepting_orders, false) AS market_accepting_orders,\n       COALESCE(gamma.enable_order_book, false) AS market_enable_order_book,\n       gamma.end_date_utc,\n       mapping.polymarket_leaderboard_category,\n       rating.found,\n       rating.leaderboard_rank,\n       rating.user_name,\n       rating.leaderboard_pnl_usd,\n       rating.leaderboard_volume_usd,\n       rating.leaderboard_pnl_to_volume_pct,\n       COALESCE(rating.current_positions_count, 0) AS current_positions_count,\n       COALESCE(rating.closed_positions_count, 0) AS closed_positions_count,\n       COALESCE(rating.positions_total_pnl_usd, 0) AS positions_total_pnl_usd,\n       rating.positions_total_percent_pnl,\n       rating.refreshed_at_utc\nFROM participants participant\nLEFT JOIN LATERAL (\n    SELECT market.market_id, market.condition_id, market.slug, market.question,\n           market.category, market.active, market.closed, market.archived, market.restricted,\n           market.accepting_orders, market.enable_order_book, market.end_date_utc,\n           COALESCE(outcome.outcome, '') AS outcome\n    FROM polymarket_gamma_markets market\n    CROSS JOIN LATERAL jsonb_array_elements_text(market.clob_token_ids_json) WITH ORDINALITY AS token(token_id, token_ordinality)\n    LEFT JOIN LATERAL jsonb_array_elements_text(market.outcomes_json) WITH ORDINALITY AS outcome(outcome, outcome_ordinality)\n      ON outcome.outcome_ordinality = token.token_ordinality\n    WHERE market.clob_token_ids_json ? participant.token_id\n      AND token.token_id = participant.token_id\n    ORDER BY market.active DESC, market.closed ASC, market.fetched_at_utc DESC\n    LIMIT 1\n) gamma ON true\nLEFT JOIN polymarket_category_mappings mapping\n  ON mapping.enabled\n AND lower(mapping.local_category) = lower(COALESCE(NULLIF(gamma.category, ''), 'unknown'))\nLEFT JOIN polymarket_data_api_wallet_category_ratings rating\n  ON lower(rating.wallet) = participant.wallet\n AND lower(rating.local_category) = lower(COALESCE(NULLIF(gamma.category, ''), 'unknown'))\n AND lower(rating.polymarket_category) = lower(mapping.polymarket_leaderboard_category)\n AND rating.time_period = @RatingTimePeriod\n AND rating.order_by = @RatingOrderBy\nORDER BY participant.block_timestamp_utc, participant.block_number, participant.log_index, participant.participant_role\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("RatingTimePeriod", ratingTimePeriod);
				command.Parameters.AddWithValue("RatingOrderBy", ratingOrderBy);
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<OnChainPaperSignalCandidate> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<OnChainPaperSignalCandidate> results = new List<OnChainPaperSignalCandidate>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadOnChainPaperSignalCandidate(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddOnChainPaperSignalResultAsync(OnChainPaperSignalResult result, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_paper_signal_results (\n    id, capture_id, transaction_hash, log_index, participant_role, copied_trader_wallet,\n    counterparty_wallet, side, token_id, condition_id, market_slug, outcome,\n    local_category, polymarket_category, rating_found, leaderboard_rank,\n    leaderboard_pnl_usd, leaderboard_volume_usd, leaderboard_pnl_to_volume_pct,\n    signal_id, paper_order_id, status, decision_code, reason_details, processed_at_utc\n) VALUES (\n    @Id, @CaptureId, @TransactionHash, @LogIndex, @ParticipantRole, @CopiedTraderWallet,\n    @CounterpartyWallet, @Side, @TokenId, @ConditionId, @MarketSlug, @Outcome,\n    @LocalCategory, @PolymarketCategory, @RatingFound, @LeaderboardRank,\n    @LeaderboardPnlUsd, @LeaderboardVolumeUsd, @LeaderboardPnlToVolumePct,\n    @SignalId, @PaperOrderId, @Status, @DecisionCode, @ReasonDetails, @ProcessedAtUtc\n)\nON CONFLICT (transaction_hash, log_index, participant_role) DO NOTHING;")
		;
		AddOnChainPaperSignalResultParameters(command, result);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddAcceptedOnChainPaperOrderAsync(Signal signal, PaperOrder paperOrder, PaperCopiedLeaderPosition? copiedLeaderPosition, OnChainPaperSignalResult result, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using (NpgsqlCommand signalCommand = CreateCommand(connection, "INSERT INTO signals (\n    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,\n    best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, decision,\n    accepted, proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json\n) VALUES (\n    @Id, @LeaderTradeId, @TraderWallet, @ConditionId, @AssetId, @Outcome, @LeaderPrice,\n    @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, @LagSeconds, @Score, @Decision,\n    @Accepted, @ProposedPaperPrice, @ProposedSizeShares, @ProposedNotionalUsd, @CreatedAtUtc, CAST(@RawContextJson AS jsonb)\n);"))
		{
			signalCommand.Transaction = transaction;
			AddSignalParameters(signalCommand, signal);
			await signalCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (NpgsqlCommand orderCommand = CreateCommand(connection, "INSERT INTO paper_orders (\n    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json, correlation_id, execution_source\n) VALUES (\n    @Id, @SignalId, @StrategyId, @CopiedTraderWallet, @Status, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,\n    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc, CAST(@RawDecisionJson AS jsonb), @CorrelationId, @ExecutionSource\n);"))
		{
			orderCommand.Transaction = transaction;
			AddPaperOrderParameters(orderCommand, paperOrder);
			await orderCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		if (copiedLeaderPosition is not null)
		{
			await using NpgsqlCommand copiedCommand = CreateCommand(connection, """
INSERT INTO paper_copied_leader_positions (
    id, entry_signal_id, entry_paper_order_id, copied_trader_wallet, asset_id,
    condition_id, outcome, entry_transaction_hash, entry_timestamp_utc,
    leader_entry_price, leader_initial_size_shares, copied_initial_size_shares,
    leader_sold_size_shares, copied_exit_requested_size_shares, status,
    last_activity_timestamp_utc, last_activity_transaction_hash,
    last_activity_sync_at_utc, next_activity_sync_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @Id, @EntrySignalId, @EntryPaperOrderId, @CopiedTraderWallet, @AssetId,
    @ConditionId, @Outcome, @EntryTransactionHash, @EntryTimestampUtc,
    @LeaderEntryPrice, @LeaderInitialSizeShares, @CopiedInitialSizeShares,
    @LeaderSoldSizeShares, @CopiedExitRequestedSizeShares, @Status,
    @LastActivityTimestampUtc, @LastActivityTransactionHash,
    @LastActivitySyncAtUtc, @NextActivitySyncAtUtc, @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (entry_paper_order_id) DO NOTHING;
""");
			copiedCommand.Transaction = transaction;
			AddPaperCopiedLeaderPositionParameters(copiedCommand, copiedLeaderPosition);
			await copiedCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (NpgsqlCommand resultCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_paper_signal_results (\n    id, capture_id, transaction_hash, log_index, participant_role, copied_trader_wallet,\n    counterparty_wallet, side, token_id, condition_id, market_slug, outcome,\n    local_category, polymarket_category, rating_found, leaderboard_rank,\n    leaderboard_pnl_usd, leaderboard_volume_usd, leaderboard_pnl_to_volume_pct,\n    signal_id, paper_order_id, status, decision_code, reason_details, processed_at_utc\n) VALUES (\n    @Id, @CaptureId, @TransactionHash, @LogIndex, @ParticipantRole, @CopiedTraderWallet,\n    @CounterpartyWallet, @Side, @TokenId, @ConditionId, @MarketSlug, @Outcome,\n    @LocalCategory, @PolymarketCategory, @RatingFound, @LeaderboardRank,\n    @LeaderboardPnlUsd, @LeaderboardVolumeUsd, @LeaderboardPnlToVolumePct,\n    @SignalId, @PaperOrderId, @Status, @DecisionCode, @ReasonDetails, @ProcessedAtUtc\n)\nON CONFLICT (transaction_hash, log_index, participant_role) DO NOTHING;"))
		{
			resultCommand.Transaction = transaction;
			AddOnChainPaperSignalResultParameters(resultCommand, result);
			await resultCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetOnChainPaperSignalCandidatesForCapturesAsync(IReadOnlyList<PolymarketOnChainTradeCapture> captures, string ratingTimePeriod, string ratingOrderBy, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (captures.Count == 0)
		{
			return [];
		}
		string capturesJson = JsonSerializer.Serialize(captures.Select(capture => new
		{
			id = capture.Id,
			contract_name = capture.ContractName,
			contract_address = capture.ContractAddress,
			exchange_version = capture.ExchangeVersion,
			block_number = capture.BlockNumber,
			block_timestamp_utc = UtcDateTime(capture.BlockTimestampUtc),
			transaction_hash = capture.TransactionHash,
			log_index = capture.LogIndex,
			order_hash = capture.OrderHash,
			maker = capture.Maker,
			taker = capture.Taker,
			side = capture.Side.ToString(),
			token_id = capture.TokenId,
			price = capture.Price,
			size_shares = capture.SizeShares,
			notional_usd = capture.NotionalUsd,
			removed = capture.Removed
		}));
		IReadOnlyList<OnChainPaperSignalCandidate> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			await using NpgsqlCommand command = CreateCommand(connection, """
WITH hot_captures AS MATERIALIZED (
    SELECT capture.id AS capture_id, capture.contract_name, capture.contract_address,
           capture.exchange_version, capture.block_number, capture.block_timestamp_utc,
           capture.transaction_hash, capture.log_index, capture.order_hash,
           capture.maker, capture.taker, capture.side, capture.token_id,
           capture.price, capture.size_shares, capture.notional_usd
    FROM jsonb_to_recordset(CAST(@CapturesJson AS jsonb)) AS capture(
        id uuid,
        contract_name text,
        contract_address text,
        exchange_version text,
        block_number bigint,
        block_timestamp_utc timestamptz,
        transaction_hash text,
        log_index bigint,
        order_hash text,
        maker text,
        taker text,
        side text,
        token_id text,
        price numeric,
        size_shares numeric,
        notional_usd numeric,
        removed boolean
    )
    WHERE NOT capture.removed
),
participants AS MATERIALIZED (
    SELECT capture.capture_id, capture.contract_name, capture.contract_address,
           capture.exchange_version, capture.block_number, capture.block_timestamp_utc,
           capture.transaction_hash, capture.log_index, capture.order_hash,
           'Maker'::text AS participant_role, lower(capture.maker) AS wallet,
           lower(capture.taker) AS counterparty_wallet, capture.side AS participant_side,
           capture.token_id, capture.price, capture.size_shares, capture.notional_usd
    FROM hot_captures capture
    LEFT JOIN polymarket_onchain_paper_signal_results maker_processed
      ON maker_processed.transaction_hash = capture.transaction_hash
     AND maker_processed.log_index = capture.log_index
     AND maker_processed.participant_role = 'Maker'
    WHERE maker_processed.id IS NULL
    UNION ALL
    SELECT capture.capture_id, capture.contract_name, capture.contract_address,
           capture.exchange_version, capture.block_number, capture.block_timestamp_utc,
           capture.transaction_hash, capture.log_index, capture.order_hash,
           'Taker'::text AS participant_role, lower(capture.taker) AS wallet,
           lower(capture.maker) AS counterparty_wallet,
           CASE capture.side WHEN 'Buy' THEN 'Sell' WHEN 'Sell' THEN 'Buy' ELSE 'Unknown' END AS participant_side,
           capture.token_id, capture.price, capture.size_shares, capture.notional_usd
    FROM hot_captures capture
    LEFT JOIN polymarket_onchain_paper_signal_results taker_processed
      ON taker_processed.transaction_hash = capture.transaction_hash
     AND taker_processed.log_index = capture.log_index
     AND taker_processed.participant_role = 'Taker'
    WHERE taker_processed.id IS NULL
)
SELECT participant.capture_id, participant.contract_name, participant.contract_address,
       participant.exchange_version, participant.block_number, participant.block_timestamp_utc,
       participant.transaction_hash, participant.log_index, participant.order_hash,
       participant.participant_role, participant.wallet, participant.counterparty_wallet,
       participant.participant_side, participant.token_id, participant.price,
       participant.size_shares, participant.notional_usd,
       COALESCE(gamma.condition_id, '') AS condition_id,
       COALESCE(gamma.market_id, '') AS market_id,
       COALESCE(gamma.slug, '') AS market_slug,
       COALESCE(gamma.question, '') AS market_title,
       COALESCE(gamma.outcome, '') AS outcome,
       gamma.category,
       gamma.market_id IS NOT NULL AS market_found,
       COALESCE(gamma.active, false) AS market_active,
       COALESCE(gamma.closed, false) AS market_closed,
       COALESCE(gamma.archived, false) AS market_archived,
       COALESCE(gamma.restricted, false) AS market_restricted,
       COALESCE(gamma.accepting_orders, false) AS market_accepting_orders,
       COALESCE(gamma.enable_order_book, false) AS market_enable_order_book,
       gamma.end_date_utc,
       mapping.polymarket_leaderboard_category,
       rating.found,
       rating.leaderboard_rank,
       rating.user_name,
       rating.leaderboard_pnl_usd,
       rating.leaderboard_volume_usd,
       rating.leaderboard_pnl_to_volume_pct,
       COALESCE(rating.current_positions_count, 0) AS current_positions_count,
       COALESCE(rating.closed_positions_count, 0) AS closed_positions_count,
       COALESCE(rating.positions_total_pnl_usd, 0) AS positions_total_pnl_usd,
       rating.positions_total_percent_pnl,
       rating.refreshed_at_utc
FROM participants participant
LEFT JOIN LATERAL (
    SELECT market.market_id, market.condition_id, market.slug, market.question,
           market.category, market.active, market.closed, market.archived, market.restricted,
           market.accepting_orders, market.enable_order_book, market.end_date_utc,
           COALESCE(outcome.outcome, '') AS outcome
    FROM polymarket_gamma_markets market
    CROSS JOIN LATERAL jsonb_array_elements_text(market.clob_token_ids_json) WITH ORDINALITY AS token(token_id, token_ordinality)
    LEFT JOIN LATERAL jsonb_array_elements_text(market.outcomes_json) WITH ORDINALITY AS outcome(outcome, outcome_ordinality)
      ON outcome.outcome_ordinality = token.token_ordinality
    WHERE market.clob_token_ids_json ? participant.token_id
      AND token.token_id = participant.token_id
    ORDER BY market.active DESC, market.closed ASC, market.fetched_at_utc DESC
    LIMIT 1
) gamma ON true
LEFT JOIN polymarket_category_mappings mapping
  ON mapping.enabled
 AND lower(mapping.local_category) = lower(COALESCE(NULLIF(gamma.category, ''), 'unknown'))
LEFT JOIN polymarket_data_api_wallet_category_ratings rating
  ON lower(rating.wallet) = participant.wallet
 AND lower(rating.local_category) = lower(COALESCE(NULLIF(gamma.category, ''), 'unknown'))
 AND lower(rating.polymarket_category) = lower(mapping.polymarket_leaderboard_category)
 AND rating.time_period = @RatingTimePeriod
 AND rating.order_by = @RatingOrderBy
ORDER BY participant.block_timestamp_utc, participant.block_number, participant.log_index, participant.participant_role;
""");
			command.Parameters.AddWithValue("CapturesJson", capturesJson);
			command.Parameters.AddWithValue("RatingTimePeriod", ratingTimePeriod);
			command.Parameters.AddWithValue("RatingOrderBy", ratingOrderBy);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			List<OnChainPaperSignalCandidate> results = new List<OnChainPaperSignalCandidate>();
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(ReadOnChainPaperSignalCandidate(reader));
			}
			result = results;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketOnChainSignalCandidateSource>> GetPolymarketOnChainSignalCandidateSourcesAsync(int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainSignalCandidateSource> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainSignalCandidateSource> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH due_queue AS (\n    SELECT queue.source_fill_id, queue.participant_role\n    FROM polymarket_onchain_signal_candidate_refresh_queue queue\n    WHERE queue.next_attempt_at_utc <= now()\n    ORDER BY queue.block_timestamp_utc, queue.block_number, queue.log_index, queue.participant_role\n    LIMIT @Limit\n    FOR UPDATE SKIP LOCKED\n),\ntouched_queue AS (\n    UPDATE polymarket_onchain_signal_candidate_refresh_queue queue\n    SET attempt_count = queue.attempt_count + 1\n    FROM due_queue\n    WHERE queue.source_fill_id = due_queue.source_fill_id\n      AND queue.participant_role = due_queue.participant_role\n    RETURNING queue.source_fill_id, queue.participant_role, queue.block_timestamp_utc,\n              queue.block_number, queue.log_index\n)\nSELECT wallet_fill.source_fill_id, wallet_fill.contract_name, wallet_fill.contract_address,\n       wallet_fill.exchange_version, wallet_fill.block_number, wallet_fill.block_timestamp_utc,\n       wallet_fill.transaction_hash, wallet_fill.log_index, wallet_fill.order_hash,\n       wallet_fill.role, wallet_fill.wallet, wallet_fill.counterparty, wallet_fill.side,\n       wallet_fill.token_id, wallet_fill.price, wallet_fill.size_shares, wallet_fill.notional_usd,\n       wallet_fill.fee_amount, wallet_fill.fee_asset_id, wallet_fill.imported_at_utc,\n       metadata.token_id, metadata.condition_id, metadata.market_id, metadata.market_slug,\n       metadata.market_title, metadata.outcome, metadata.outcome_index, metadata.category,\n       metadata.end_date_utc, metadata.active, metadata.closed, metadata.archived,\n       metadata.resolved, metadata.winning_outcome, metadata.clob_token_ids_json,\n       metadata.outcomes_json, metadata.lookup_succeeded, metadata.lookup_error,\n       metadata.raw_json, metadata.last_refreshed_utc,\n       performance.wallet, performance.category, performance.positions_count,\n       performance.open_positions, performance.flat_positions, performance.resolved_positions,\n       performance.profitable_resolved_positions, performance.losing_resolved_positions,\n       performance.markets_traded, performance.volume_usd, performance.resolved_volume_usd,\n       performance.open_exposure_usd, performance.resolved_cost_usd,\n       performance.resolved_pnl_usd, performance.resolved_roi_pct,\n       performance.win_rate_pct, performance.average_position_size_usd,\n       performance.score, performance.sample_quality, performance.first_active_utc,\n       performance.last_active_utc, performance.refreshed_at_utc\nFROM touched_queue queue\nJOIN polymarket_onchain_wallet_fills wallet_fill\n  ON wallet_fill.source_fill_id = queue.source_fill_id\n AND wallet_fill.role = queue.participant_role\nLEFT JOIN polymarket_onchain_token_metadata metadata\n  ON metadata.token_id = wallet_fill.token_id\nLEFT JOIN polymarket_onchain_wallet_category_performance performance\n  ON performance.wallet = wallet_fill.wallet\n AND performance.category = COALESCE(NULLIF(metadata.category, ''), 'unknown')\nORDER BY queue.block_timestamp_utc, queue.block_number, queue.log_index, queue.participant_role;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainSignalCandidateSource> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainSignalCandidateSource> results = new List<PolymarketOnChainSignalCandidateSource>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainSignalCandidateSource(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task UpsertPolymarketOnChainSignalCandidateDecisionsAsync(IReadOnlyList<PolymarketOnChainSignalCandidateDecision> decisions, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (decisions.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (PolymarketOnChainSignalCandidateDecision decision in decisions)
		{
			await using NpgsqlCommand upsertCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_signal_candidates (\n    id, source_fill_id, contract_name, contract_address, exchange_version, block_number,\n    block_timestamp_utc, transaction_hash, log_index, order_hash, participant_role,\n    wallet, counterparty, side, token_id, condition_id, market_id, market_slug,\n    market_title, outcome, category, lookup_succeeded, market_active, market_closed,\n    market_archived, market_resolved, winning_outcome, price, size_shares, notional_usd,\n    fee_amount, fee_asset_id, leader_positions_count, leader_resolved_positions,\n    leader_markets_traded, leader_volume_usd, leader_resolved_pnl_usd,\n    leader_resolved_roi_pct, leader_win_rate_pct, leader_category_score,\n    leader_sample_quality, leader_performance_refreshed_at_utc, decision_status,\n    decision_code, candidate_score, created_at_utc, updated_at_utc\n) VALUES (\n    @Id, @SourceFillId, @ContractName, @ContractAddress, @ExchangeVersion, @BlockNumber,\n    @BlockTimestampUtc, @TransactionHash, @LogIndex, @OrderHash, @ParticipantRole,\n    @Wallet, @Counterparty, @Side, @TokenId, @ConditionId, @MarketId, @MarketSlug,\n    @MarketTitle, @Outcome, @Category, @LookupSucceeded, @MarketActive, @MarketClosed,\n    @MarketArchived, @MarketResolved, @WinningOutcome, @Price, @SizeShares, @NotionalUsd,\n    @FeeAmount, @FeeAssetId, @LeaderPositionsCount, @LeaderResolvedPositions,\n    @LeaderMarketsTraded, @LeaderVolumeUsd, @LeaderResolvedPnlUsd,\n    @LeaderResolvedRoiPct, @LeaderWinRatePct, @LeaderCategoryScore,\n    @LeaderSampleQuality, @LeaderPerformanceRefreshedAtUtc, @DecisionStatus,\n    @DecisionCode, @CandidateScore, @CreatedAtUtc, @UpdatedAtUtc\n)\nON CONFLICT (source_fill_id, participant_role) DO UPDATE SET\n    contract_name = excluded.contract_name,\n    contract_address = excluded.contract_address,\n    exchange_version = excluded.exchange_version,\n    block_number = excluded.block_number,\n    block_timestamp_utc = excluded.block_timestamp_utc,\n    transaction_hash = excluded.transaction_hash,\n    log_index = excluded.log_index,\n    order_hash = excluded.order_hash,\n    wallet = excluded.wallet,\n    counterparty = excluded.counterparty,\n    side = excluded.side,\n    token_id = excluded.token_id,\n    condition_id = excluded.condition_id,\n    market_id = excluded.market_id,\n    market_slug = excluded.market_slug,\n    market_title = excluded.market_title,\n    outcome = excluded.outcome,\n    category = excluded.category,\n    lookup_succeeded = excluded.lookup_succeeded,\n    market_active = excluded.market_active,\n    market_closed = excluded.market_closed,\n    market_archived = excluded.market_archived,\n    market_resolved = excluded.market_resolved,\n    winning_outcome = excluded.winning_outcome,\n    price = excluded.price,\n    size_shares = excluded.size_shares,\n    notional_usd = excluded.notional_usd,\n    fee_amount = excluded.fee_amount,\n    fee_asset_id = excluded.fee_asset_id,\n    leader_positions_count = excluded.leader_positions_count,\n    leader_resolved_positions = excluded.leader_resolved_positions,\n    leader_markets_traded = excluded.leader_markets_traded,\n    leader_volume_usd = excluded.leader_volume_usd,\n    leader_resolved_pnl_usd = excluded.leader_resolved_pnl_usd,\n    leader_resolved_roi_pct = excluded.leader_resolved_roi_pct,\n    leader_win_rate_pct = excluded.leader_win_rate_pct,\n    leader_category_score = excluded.leader_category_score,\n    leader_sample_quality = excluded.leader_sample_quality,\n    leader_performance_refreshed_at_utc = excluded.leader_performance_refreshed_at_utc,\n    decision_status = excluded.decision_status,\n    decision_code = excluded.decision_code,\n    candidate_score = excluded.candidate_score,\n    updated_at_utc = excluded.updated_at_utc\nRETURNING id;");
			upsertCommand.Transaction = transaction;
			AddPolymarketOnChainSignalCandidateParameters(upsertCommand, decision.Candidate);
			Guid persistedId = (Guid)((await upsertCommand.ExecuteScalarAsync(cancellationToken)) ?? throw new InvalidOperationException("Failed to upsert on-chain signal candidate."));
			await using (NpgsqlCommand deleteCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_signal_candidate_reasons\nWHERE candidate_id = @CandidateId;"))
			{
				deleteCommand.Transaction = transaction;
				deleteCommand.Parameters.AddWithValue("CandidateId", persistedId);
				await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
			}
			foreach (PolymarketOnChainSignalCandidateReason reason in decision.Reasons)
			{
				await using NpgsqlCommand reasonCommand = CreateCommand(connection, "INSERT INTO polymarket_onchain_signal_candidate_reasons (\n    id, candidate_id, reason_code, reason_details, created_at_utc\n) VALUES (\n    @Id, @CandidateId, @ReasonCode, @ReasonDetails, @CreatedAtUtc\n);");
				reasonCommand.Transaction = transaction;
				reasonCommand.Parameters.AddWithValue("Id", reason.Id);
				reasonCommand.Parameters.AddWithValue("CandidateId", persistedId);
				reasonCommand.Parameters.AddWithValue("ReasonCode", reason.ReasonCode);
				reasonCommand.Parameters.AddWithValue("ReasonDetails", reason.ReasonDetails);
				reasonCommand.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(reason.CreatedAtUtc));
				await reasonCommand.ExecuteNonQueryAsync(cancellationToken);
			}
			await using NpgsqlCommand deleteQueueCommand = CreateCommand(connection, "DELETE FROM polymarket_onchain_signal_candidate_refresh_queue\nWHERE source_fill_id = @SourceFillId\n  AND participant_role = @ParticipantRole;");
			deleteQueueCommand.Transaction = transaction;
			deleteQueueCommand.Parameters.AddWithValue("SourceFillId", decision.Candidate.SourceFillId);
			deleteQueueCommand.Parameters.AddWithValue("ParticipantRole", decision.Candidate.ParticipantRole.ToString());
			await deleteQueueCommand.ExecuteNonQueryAsync(cancellationToken);
		}
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketOnChainSignalCandidate>> GetRecentPolymarketOnChainSignalCandidatesAsync(int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketOnChainSignalCandidate> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketOnChainSignalCandidate> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, source_fill_id, contract_name, contract_address, exchange_version, block_number,\n       block_timestamp_utc, transaction_hash, log_index, order_hash, participant_role,\n       wallet, counterparty, side, token_id, condition_id, market_id, market_slug,\n       market_title, outcome, category, lookup_succeeded, market_active, market_closed,\n       market_archived, market_resolved, winning_outcome, price, size_shares, notional_usd,\n       fee_amount, fee_asset_id, leader_positions_count, leader_resolved_positions,\n       leader_markets_traded, leader_volume_usd, leader_resolved_pnl_usd,\n       leader_resolved_roi_pct, leader_win_rate_pct, leader_category_score,\n       leader_sample_quality, leader_performance_refreshed_at_utc, decision_status,\n       decision_code, candidate_score, created_at_utc, updated_at_utc\nFROM polymarket_onchain_signal_candidates\nORDER BY updated_at_utc DESC, block_timestamp_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketOnChainSignalCandidate> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketOnChainSignalCandidate> results = new List<PolymarketOnChainSignalCandidate>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketOnChainSignalCandidate(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketOnChainTradeDetails>> GetRecentPolymarketOnChainTradeDetailsAsync(int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<PolymarketOnChainTradeDetails> results = new List<PolymarketOnChainTradeDetails>();
		try
		{
			await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT contract_name, contract_address, exchange_version, block_number, block_timestamp_utc,\n       transaction_hash, log_index, order_hash, maker, taker, maker_side, taker_side,\n       token_id, maker_asset_id, taker_asset_id, maker_amount_raw, taker_amount_raw,\n       maker_amount, taker_amount, price, size_shares, notional_usd, fee_amount,\n       fee_asset_id, builder, order_metadata, condition_id, market_id, market_slug,\n       market_title, outcome, category, lookup_succeeded, market_active, market_closed,\n       market_archived, market_resolved, winning_outcome, imported_at_utc\nFROM polymarket_onchain_trade_details\nORDER BY block_timestamp_utc DESC, block_number DESC, log_index DESC\nLIMIT @Limit;");
			command.Parameters.AddWithValue("Limit", limit);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(ReadPolymarketOnChainTradeDetails(reader));
			}
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01")
		{
			return results;
		}
		return results;
	}

	public async Task<IReadOnlyList<PolymarketOnChainParticipantDetails>> GetPolymarketOnChainParticipantDetailsAsync(int limit = 250, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<PolymarketOnChainParticipantDetails> results = new List<PolymarketOnChainParticipantDetails>();
		try
		{
			await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT wallet, executions, buy_executions, sell_executions, markets_traded,\n       volume_usd, average_trade_usd, fees_usd, activity_score,\n       positions_count, open_positions, flat_positions, resolved_positions,\n       profitable_resolved_positions, losing_resolved_positions, open_exposure_usd,\n       resolved_cost_usd, resolved_pnl_usd, resolved_roi_pct, win_rate_pct,\n       average_position_size_usd, score, sample_quality, first_trade_utc,\n       last_trade_utc, activity_refreshed_at_utc, performance_refreshed_at_utc\nFROM polymarket_onchain_participant_details\nORDER BY score DESC, volume_usd DESC, last_trade_utc DESC\nLIMIT @Limit;");
			command.Parameters.AddWithValue("Limit", limit);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				results.Add(ReadPolymarketOnChainParticipantDetails(reader));
			}
		}
		catch (PostgresException ex) when (ex.SqlState == "42P01")
		{
			return results;
		}
		return results;
	}

	public async Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<RiskEvent> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<RiskEvent> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, reason_code, details, created_at_utc\nFROM risk_events\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<RiskEvent> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<RiskEvent> results = new List<RiskEvent>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new RiskEvent(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), DateTimeOffsetFromUtc(reader.GetDateTime(3))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO order_book_snapshots (\n    id, asset_id, condition_id, best_bid, best_ask, spread_abs, spread_pct, raw_json, snapshot_at_utc\n) VALUES (\n    @Id, @AssetId, @ConditionId, @BestBid, @BestAsk, @SpreadAbs, @SpreadPct, CAST(@RawJson AS jsonb), @SnapshotAtUtc\n);");
		command.Parameters.AddWithValue("Id", Guid.NewGuid());
		command.Parameters.AddWithValue("AssetId", snapshot.AssetId);
		command.Parameters.AddWithValue("ConditionId", ((object)snapshot.ConditionId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("BestBid", ((object)snapshot.BestBid) ?? DBNull.Value);
		command.Parameters.AddWithValue("BestAsk", ((object)snapshot.BestAsk) ?? DBNull.Value);
		command.Parameters.AddWithValue("SpreadAbs", ((object)snapshot.SpreadAbs) ?? DBNull.Value);
		command.Parameters.AddWithValue("SpreadPct", ((object)snapshot.SpreadPct) ?? DBNull.Value);
		command.Parameters.AddWithValue("RawJson", JsonSerializer.Serialize(snapshot));
		command.Parameters.AddWithValue("SnapshotAtUtc", UtcDateTime(snapshot.SnapshotAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		OrderBookSnapshot result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			OrderBookSnapshot orderBookSnapshot2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, best_bid, best_ask, snapshot_at_utc\nFROM order_book_snapshots\nWHERE asset_id = @AssetId\nORDER BY snapshot_at_utc DESC\nLIMIT 1;"))
			{
				command.Parameters.AddWithValue("AssetId", assetId);
				OrderBookSnapshot orderBookSnapshot;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					orderBookSnapshot = ((await reader.ReadAsync(cancellationToken)) ? ReadOrderBookSnapshot(reader) : null);
				}
				orderBookSnapshot2 = orderBookSnapshot;
			}
			result = orderBookSnapshot2;
		}
		return result;
	}

	public async Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<OrderBookSnapshot> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<OrderBookSnapshot> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, best_bid, best_ask, snapshot_at_utc\nFROM (\n    SELECT DISTINCT ON (asset_id)\n        asset_id, condition_id, best_bid, best_ask, snapshot_at_utc\n    FROM order_book_snapshots\n    ORDER BY asset_id, snapshot_at_utc DESC\n) latest\nORDER BY snapshot_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<OrderBookSnapshot> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<OrderBookSnapshot> results = new List<OrderBookSnapshot>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadOrderBookSnapshot(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO market_data_events (id, event_type, asset_id, condition_id, message, received_at_utc)\nVALUES (@Id, @EventType, @AssetId, @ConditionId, @Message, @ReceivedAtUtc);");
		command.Parameters.AddWithValue("Id", marketDataEvent.Id);
		command.Parameters.AddWithValue("EventType", marketDataEvent.EventType.ToString());
		command.Parameters.AddWithValue("AssetId", ((object)marketDataEvent.AssetId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ConditionId", ((object)marketDataEvent.ConditionId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Message", marketDataEvent.Message);
		command.Parameters.AddWithValue("ReceivedAtUtc", UtcDateTime(marketDataEvent.ReceivedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<MarketDataEvent> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<MarketDataEvent> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, event_type, asset_id, condition_id, message, received_at_utc\nFROM market_data_events\nORDER BY received_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<MarketDataEvent> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<MarketDataEvent> results = new List<MarketDataEvent>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new MarketDataEvent(reader.GetGuid(0), Enum.Parse<MarketDataEventType>(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), DateTimeOffsetFromUtc(reader.GetDateTime(5))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<bool> TryAddPolymarketWebSocketTradeTickAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default(CancellationToken))
	{
		bool result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			bool flag;
			await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_websocket_trade_ticks (\n    id, dedup_key, asset_id, condition_id, side, price, size, trade_timestamp_utc,\n    transaction_hash, transaction_hash_present, trader_match_status, trader_wallet,\n    received_at_utc, matched_at_utc, match_attempts, last_match_attempt_utc,\n    last_match_error, matched_transaction_hash, match_details, raw_json, updated_at_utc\n) VALUES (\n    @Id, @DedupKey, @AssetId, @ConditionId, @Side, @Price, @Size, @TradeTimestampUtc,\n    @TransactionHash, @TransactionHashPresent, @TraderMatchStatus, @TraderWallet,\n    @ReceivedAtUtc, @MatchedAtUtc, @MatchAttempts, @LastMatchAttemptUtc,\n    @LastMatchError, @MatchedTransactionHash, @MatchDetails, CAST(@RawJson AS jsonb), @UpdatedAtUtc\n)\nON CONFLICT (dedup_key) DO NOTHING;"))
			{
				AddPolymarketWebSocketTradeTickParameters(command, tradeTick);
				flag = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
			}
			result = flag;
		}
		return result;
	}

	public async Task UpdatePolymarketWebSocketTradeTickMatchAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_websocket_trade_ticks\nSET trader_match_status = @TraderMatchStatus,\n    trader_wallet = @TraderWallet,\n    matched_at_utc = @MatchedAtUtc,\n    match_attempts = @MatchAttempts,\n    last_match_attempt_utc = @LastMatchAttemptUtc,\n    last_match_error = @LastMatchError,\n    matched_transaction_hash = @MatchedTransactionHash,\n    match_details = @MatchDetails,\n    updated_at_utc = @UpdatedAtUtc\nWHERE dedup_key = @DedupKey;");
		AddPolymarketWebSocketTradeTickParameters(command, tradeTick);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetPendingPolymarketWebSocketTradeTickMatchesAsync(DateTimeOffset dueBeforeUtc, int maxAttempts, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketWebSocketTradeTick> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketWebSocketTradeTick> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, dedup_key, asset_id, condition_id, side, price, size, trade_timestamp_utc,\n       transaction_hash, transaction_hash_present, trader_match_status, trader_wallet,\n       received_at_utc, matched_at_utc, match_attempts, last_match_attempt_utc,\n       last_match_error, matched_transaction_hash, match_details, raw_json::text, updated_at_utc\nFROM polymarket_websocket_trade_ticks\nWHERE trader_match_status = @NotFoundStatus\n  AND match_attempts < @MaxAttempts\n  AND condition_id IS NOT NULL\n  AND btrim(condition_id) <> ''\n  AND (last_match_attempt_utc IS NULL OR last_match_attempt_utc <= @DueBeforeUtc)\nORDER BY COALESCE(last_match_attempt_utc, received_at_utc), received_at_utc\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("NotFoundStatus", 1);
				command.Parameters.AddWithValue("MaxAttempts", maxAttempts);
				command.Parameters.AddWithValue("DueBeforeUtc", UtcDateTime(dueBeforeUtc));
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketWebSocketTradeTick> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketWebSocketTradeTick> results = new List<PolymarketWebSocketTradeTick>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketWebSocketTradeTick(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetRecentPolymarketWebSocketTradeTicksAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PolymarketWebSocketTradeTick> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PolymarketWebSocketTradeTick> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, dedup_key, asset_id, condition_id, side, price, size, trade_timestamp_utc,\n       transaction_hash, transaction_hash_present, trader_match_status, trader_wallet,\n       received_at_utc, matched_at_utc, match_attempts, last_match_attempt_utc,\n       last_match_error, matched_transaction_hash, match_details, raw_json::text, updated_at_utc\nFROM polymarket_websocket_trade_ticks\nORDER BY received_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<PolymarketWebSocketTradeTick> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PolymarketWebSocketTradeTick> results = new List<PolymarketWebSocketTradeTick>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPolymarketWebSocketTradeTick(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO market_data_status (\n    component, connection_state, endpoint, subscribed_assets_count, last_message_utc,\n    last_connected_utc, last_disconnected_utc, reconnect_count, stale, last_error, updated_at_utc\n) VALUES (\n    @Component, @ConnectionState, @Endpoint, @SubscribedAssetsCount, @LastMessageUtc,\n    @LastConnectedUtc, @LastDisconnectedUtc, @ReconnectCount, @Stale, @LastError, @UpdatedAtUtc\n)\nON CONFLICT(component) DO UPDATE SET\n    connection_state = excluded.connection_state,\n    endpoint = excluded.endpoint,\n    subscribed_assets_count = excluded.subscribed_assets_count,\n    last_message_utc = excluded.last_message_utc,\n    last_connected_utc = excluded.last_connected_utc,\n    last_disconnected_utc = excluded.last_disconnected_utc,\n    reconnect_count = excluded.reconnect_count,\n    stale = excluded.stale,\n    last_error = excluded.last_error,\n    updated_at_utc = excluded.updated_at_utc\nWHERE\n    market_data_status.connection_state IS DISTINCT FROM excluded.connection_state\n    OR market_data_status.endpoint IS DISTINCT FROM excluded.endpoint\n    OR market_data_status.subscribed_assets_count IS DISTINCT FROM excluded.subscribed_assets_count\n    OR market_data_status.last_connected_utc IS DISTINCT FROM excluded.last_connected_utc\n    OR market_data_status.last_disconnected_utc IS DISTINCT FROM excluded.last_disconnected_utc\n    OR market_data_status.reconnect_count IS DISTINCT FROM excluded.reconnect_count\n    OR market_data_status.stale IS DISTINCT FROM excluded.stale\n    OR market_data_status.last_error IS DISTINCT FROM excluded.last_error\n    OR market_data_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds';");
		command.Parameters.AddWithValue("Component", status.Component);
		command.Parameters.AddWithValue("ConnectionState", status.ConnectionState.ToString());
		command.Parameters.AddWithValue("Endpoint", status.Endpoint);
		command.Parameters.AddWithValue("SubscribedAssetsCount", status.SubscribedAssetsCount);
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? lastMessageUtc = status.LastMessageUtc;
		object value;
		if (lastMessageUtc.HasValue)
		{
			DateTimeOffset lastMessage = lastMessageUtc.GetValueOrDefault();
			value = UtcDateTime(lastMessage);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("LastMessageUtc", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		lastMessageUtc = status.LastConnectedUtc;
		object value2;
		if (lastMessageUtc.HasValue)
		{
			DateTimeOffset connected = lastMessageUtc.GetValueOrDefault();
			value2 = UtcDateTime(connected);
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("LastConnectedUtc", value2);
		NpgsqlParameterCollection parameters3 = command.Parameters;
		lastMessageUtc = status.LastDisconnectedUtc;
		object value3;
		if (lastMessageUtc.HasValue)
		{
			DateTimeOffset disconnected = lastMessageUtc.GetValueOrDefault();
			value3 = UtcDateTime(disconnected);
		}
		else
		{
			value3 = DBNull.Value;
		}
		parameters3.AddWithValue("LastDisconnectedUtc", value3);
		command.Parameters.AddWithValue("ReconnectCount", status.ReconnectCount);
		command.Parameters.AddWithValue("Stale", status.Stale);
		command.Parameters.AddWithValue("LastError", ((object)status.LastError) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(status.UpdatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<MarketDataStatusSnapshot> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<MarketDataStatusSnapshot> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT component, connection_state, endpoint, subscribed_assets_count, last_message_utc,\n       last_connected_utc, last_disconnected_utc, reconnect_count, stale, last_error, updated_at_utc\nFROM market_data_status\nORDER BY component;"))
			{
				IReadOnlyList<MarketDataStatusSnapshot> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<MarketDataStatusSnapshot> results = new List<MarketDataStatusSnapshot>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new MarketDataStatusSnapshot(reader.GetString(0), Enum.Parse<MarketDataConnectionState>(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.IsDBNull(4) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(4))), reader.IsDBNull(5) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(5))), reader.IsDBNull(6) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(6))), reader.GetInt32(7), reader.GetBoolean(8), reader.IsDBNull(9) ? null : reader.GetString(9), DateTimeOffsetFromUtc(reader.GetDateTime(10))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO pinned_market_assets (asset_id, note, created_at_utc)\nVALUES (@AssetId, @Note, @CreatedAtUtc)\nON CONFLICT(asset_id) DO UPDATE SET\n    note = excluded.note;");
		command.Parameters.AddWithValue("AssetId", asset.AssetId);
		command.Parameters.AddWithValue("Note", ((object)asset.Note) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(asset.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "DELETE FROM pinned_market_assets\nWHERE asset_id = @AssetId;");
		command.Parameters.AddWithValue("AssetId", assetId);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PinnedMarketAsset> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PinnedMarketAsset> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, note, created_at_utc\nFROM pinned_market_assets\nORDER BY created_at_utc DESC;"))
			{
				IReadOnlyList<PinnedMarketAsset> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PinnedMarketAsset> results = new List<PinnedMarketAsset>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new PinnedMarketAsset(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), DateTimeOffsetFromUtc(reader.GetDateTime(2))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateTime startUtc = reportDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		DateTime endUtc = reportDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
		DailyReport result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			DailyReport dailyReport2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH bounds AS (\n    SELECT @StartUtc::timestamptz AS start_utc, @EndUtc::timestamptz AS end_utc\n),\ntop_rejections AS (\n    SELECT string_agg(reason_code || ':' || reason_count, '; ' ORDER BY reason_count DESC, reason_code) AS reasons\n    FROM (\n        SELECT sr.reason_code, count(*) AS reason_count\n        FROM signal_rejections sr, bounds b\n        WHERE sr.created_at_utc >= b.start_utc AND sr.created_at_utc < b.end_utc\n        GROUP BY sr.reason_code\n        ORDER BY reason_count DESC, sr.reason_code\n        LIMIT 5\n    ) ranked\n)\nSELECT\n    (SELECT count(*)::integer FROM signals s, bounds b WHERE s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_observed,\n    (SELECT count(*)::integer FROM signals s, bounds b WHERE s.accepted AND s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_accepted,\n    (SELECT count(*)::integer FROM signals s, bounds b WHERE NOT s.accepted AND s.created_at_utc >= b.start_utc AND s.created_at_utc < b.end_utc) AS signals_rejected,\n    (SELECT count(*)::integer FROM paper_orders po, bounds b WHERE po.created_at_utc >= b.start_utc AND po.created_at_utc < b.end_utc) AS paper_orders_created,\n    (SELECT count(*)::integer FROM paper_fills pf, bounds b WHERE pf.filled_at_utc >= b.start_utc AND pf.filled_at_utc < b.end_utc) AS paper_fills,\n    (SELECT count(*)::integer FROM paper_orders po, bounds b WHERE po.status IN ('Expired', 'PartiallyFilledExpired') AND po.expires_at_utc >= b.start_utc AND po.expires_at_utc < b.end_utc) AS paper_expired_orders,\n    COALESCE((SELECT sum(pp.unrealized_pnl_usd) FROM paper_positions pp), 0)\n        + COALESCE((SELECT sum(pf.realized_pnl_usd) FROM paper_fills pf), 0)\n        + COALESCE((SELECT sum(ps.realized_pnl_usd) FROM paper_position_settlements ps), 0) AS paper_pnl,\n    COALESCE((SELECT sum(po.notional_usd) FROM paper_orders po WHERE po.status IN ('Pending', 'PartiallyFilled')), 0)\n        + COALESCE((SELECT sum(pp.estimated_value_usd) FROM paper_positions pp), 0) AS open_paper_exposure,\n    COALESCE((SELECT reasons FROM top_rejections), '') AS top_rejection_reasons,\n    (SELECT count(*)::integer FROM api_errors ae, bounds b WHERE ae.created_at_utc >= b.start_utc AND ae.created_at_utc < b.end_utc) AS api_errors;"))
			{
				command.Parameters.AddWithValue("StartUtc", startUtc);
				command.Parameters.AddWithValue("EndUtc", endUtc);
				DailyReport dailyReport;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					dailyReport = ((await reader.ReadAsync(cancellationToken)) ? new DailyReport(reportDate, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetString(8), reader.GetInt32(9), DateTimeOffset.UtcNow) : new DailyReport(reportDate, 0, 0, 0, 0, 0, 0, 0m, 0m, string.Empty, 0, DateTimeOffset.UtcNow));
				}
				dailyReport2 = dailyReport;
			}
			result = dailyReport2;
		}
		return result;
	}

	public async Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO daily_reports (\n    report_date, signals_observed, signals_accepted, signals_rejected, paper_orders_created,\n    paper_fills, paper_expired_orders, paper_pnl, open_paper_exposure, top_rejection_reasons,\n    api_errors, generated_at_utc\n) VALUES (\n    @ReportDate, @SignalsObserved, @SignalsAccepted, @SignalsRejected, @PaperOrdersCreated,\n    @PaperFills, @PaperExpiredOrders, @PaperPnl, @OpenPaperExposure, @TopRejectionReasons,\n    @ApiErrors, @GeneratedAtUtc\n)\nON CONFLICT(report_date) DO UPDATE SET\n    signals_observed = excluded.signals_observed,\n    signals_accepted = excluded.signals_accepted,\n    signals_rejected = excluded.signals_rejected,\n    paper_orders_created = excluded.paper_orders_created,\n    paper_fills = excluded.paper_fills,\n    paper_expired_orders = excluded.paper_expired_orders,\n    paper_pnl = excluded.paper_pnl,\n    open_paper_exposure = excluded.open_paper_exposure,\n    top_rejection_reasons = excluded.top_rejection_reasons,\n    api_errors = excluded.api_errors,\n    generated_at_utc = excluded.generated_at_utc;");
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

	public async Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<DailyReport> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<DailyReport> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT report_date, signals_observed, signals_accepted, signals_rejected, paper_orders_created,\n       paper_fills, paper_expired_orders, paper_pnl, open_paper_exposure, top_rejection_reasons,\n       api_errors, generated_at_utc\nFROM daily_reports\nORDER BY report_date DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<DailyReport> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<DailyReport> results = new List<DailyReport>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadDailyReport(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH signal_stats AS (
    SELECT
        s.trader_wallet,
        count(*) AS signals,
        count(*) FILTER (WHERE s.accepted) AS accepted,
        round(avg(s.lag_seconds)::numeric, 4)::numeric(18,4) AS avg_lag,
        round(avg(s.leader_price)::numeric, 8)::numeric(18,8) AS avg_leader_price,
        round(avg(s.proposed_paper_price)::numeric, 8)::numeric(18,8) AS avg_proposed_price,
        round(avg(s.proposed_paper_price - s.leader_price)::numeric, 8)::numeric(18,8) AS avg_price_difference
    FROM signals s
    GROUP BY s.trader_wallet
),
fill_stats AS (
    SELECT
        s.trader_wallet,
        count(DISTINCT po.id) AS orders,
        count(DISTINCT po.id) FILTER (WHERE pf.id IS NOT NULL) AS filled_orders,
        round(
            LEAST(
                GREATEST(
                    COALESCE(sum(
                        CASE
                            WHEN pp.size_shares > 0 THEN pp.estimated_value_usd - po.price * po.size_shares
                            ELSE 0
                        END
                    ) FILTER (WHERE pf.id IS NOT NULL), 0),
                    -999999999999999999.99999999
                ),
                999999999999999999.99999999
            ),
            8
        )::numeric(28,8) AS paper_pnl
    FROM signals s
    LEFT JOIN paper_orders po ON po.signal_id = s.id
    LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
    LEFT JOIN paper_positions pp
      ON pp.asset_id = po.asset_id
     AND lower(pp.copied_trader_wallet) = lower(po.copied_trader_wallet)
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
            round(
                LEAST(
                    GREATEST(
                        COALESCE(sum(
                            CASE
                                WHEN pp.size_shares > 0 THEN pp.estimated_value_usd - po.price * po.size_shares
                                ELSE 0
                            END
                        ) FILTER (WHERE pf.id IS NOT NULL), 0),
                        -999999999999999999.99999999
                    ),
                    999999999999999999.99999999
                ),
                8
            )::numeric(28,8) AS pnl
        FROM signals s
        LEFT JOIN markets m ON m.condition_id = s.condition_id
        LEFT JOIN paper_orders po ON po.signal_id = s.id
        LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
        LEFT JOIN paper_positions pp
          ON pp.asset_id = po.asset_id
         AND lower(pp.copied_trader_wallet) = lower(po.copied_trader_wallet)
        GROUP BY s.trader_wallet, COALESCE(m.category, 'unknown')
    ) grouped
    GROUP BY trader_wallet
)
SELECT
    ss.trader_wallet,
    ss.signals::integer,
    CASE WHEN ss.signals = 0 THEN 0 ELSE round(ss.accepted::numeric / ss.signals * 100, 4)::numeric(18,4) END AS acceptance_rate,
    CASE WHEN COALESCE(fs.orders, 0) = 0 THEN 0 ELSE round(fs.filled_orders::numeric / fs.orders * 100, 4)::numeric(18,4) END AS fill_rate,
    ss.avg_lag,
    ss.avg_leader_price,
    ss.avg_proposed_price,
    ss.avg_price_difference,
    COALESCE(fs.paper_pnl, 0)::numeric(28,8) AS paper_pnl,
    COALESCE(cp.pnl_by_category, '') AS paper_pnl_by_category,
    COALESCE(rs.reasons, '') AS rejection_reasons
FROM signal_stats ss
LEFT JOIN fill_stats fs ON fs.trader_wallet = ss.trader_wallet
LEFT JOIN rejection_stats rs ON rs.trader_wallet = ss.trader_wallet
LEFT JOIN category_pnl cp ON cp.trader_wallet = ss.trader_wallet
ORDER BY ss.signals DESC, ss.trader_wallet
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<TraderPerformanceReport> results = [];
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

	public async Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT
    COALESCE(m.category, 'unknown') AS category,
    count(DISTINCT s.id)::integer AS signals,
    count(DISTINCT s.id) FILTER (WHERE s.accepted)::integer AS accepted,
    count(DISTINCT po.id) FILTER (WHERE pf.id IS NOT NULL)::integer AS filled,
    round(
        LEAST(
            GREATEST(
                COALESCE(sum(
                    CASE
                        WHEN pp.size_shares > 0 THEN pp.estimated_value_usd - po.price * po.size_shares
                        ELSE 0
                    END
                ) FILTER (WHERE pf.id IS NOT NULL), 0),
                -999999999999999999.99999999
            ),
            999999999999999999.99999999
        ),
        8
    )::numeric(28,8) AS paper_pnl,
    round(avg(s.spread_abs)::numeric, 8)::numeric(18,8) AS avg_spread,
    round(avg(s.lag_seconds)::numeric, 4)::numeric(18,4) AS avg_lag
FROM signals s
LEFT JOIN markets m ON m.condition_id = s.condition_id
LEFT JOIN paper_orders po ON po.signal_id = s.id
LEFT JOIN paper_fills pf ON pf.paper_order_id = po.id
LEFT JOIN paper_positions pp
  ON pp.asset_id = po.asset_id
 AND lower(pp.copied_trader_wallet) = lower(po.copied_trader_wallet)
GROUP BY COALESCE(m.category, 'unknown')
ORDER BY signals DESC, category
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CategoryPerformanceReport> results = [];
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

	public async Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ExecutionQualityReport> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<ExecutionQualityReport> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT\n    s.id, s.trader_wallet, s.asset_id, s.condition_id, s.created_at_utc,\n    s.leader_price, s.proposed_paper_price, pf.price AS fill_price,\n    s.proposed_paper_price - s.leader_price AS proposed_minus_leader,\n    pf.price - s.proposed_paper_price AS fill_minus_proposed,\n    s.lag_seconds, s.spread_abs,\n    ob1.best_bid AS bid_1m, ob1.best_ask AS ask_1m,\n    CASE WHEN ob1.best_bid IS NULL OR ob1.best_ask IS NULL THEN NULL ELSE (ob1.best_bid + ob1.best_ask) / 2 END AS mid_1m,\n    ob5.best_bid AS bid_5m, ob5.best_ask AS ask_5m,\n    CASE WHEN ob5.best_bid IS NULL OR ob5.best_ask IS NULL THEN NULL ELSE (ob5.best_bid + ob5.best_ask) / 2 END AS mid_5m,\n    ob30.best_bid AS bid_30m, ob30.best_ask AS ask_30m,\n    CASE WHEN ob30.best_bid IS NULL OR ob30.best_ask IS NULL THEN NULL ELSE (ob30.best_bid + ob30.best_ask) / 2 END AS mid_30m\nFROM signals s\nLEFT JOIN paper_orders po ON po.signal_id = s.id\nLEFT JOIN paper_fills pf ON pf.paper_order_id = po.id\nLEFT JOIN LATERAL (\n    SELECT best_bid, best_ask FROM order_book_snapshots obs\n    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '1 minute'\n    ORDER BY obs.snapshot_at_utc\n    LIMIT 1\n) ob1 ON true\nLEFT JOIN LATERAL (\n    SELECT best_bid, best_ask FROM order_book_snapshots obs\n    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '5 minutes'\n    ORDER BY obs.snapshot_at_utc\n    LIMIT 1\n) ob5 ON true\nLEFT JOIN LATERAL (\n    SELECT best_bid, best_ask FROM order_book_snapshots obs\n    WHERE obs.asset_id = s.asset_id AND obs.snapshot_at_utc >= s.created_at_utc + interval '30 minutes'\n    ORDER BY obs.snapshot_at_utc\n    LIMIT 1\n) ob30 ON true\nORDER BY s.created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<ExecutionQualityReport> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<ExecutionQualityReport> results = new List<ExecutionQualityReport>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new ExecutionQualityReport(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffsetFromUtc(reader.GetDateTime(4)), reader.GetDecimal(5), reader.IsDBNull(6) ? ((decimal?)null) : new decimal?(reader.GetDecimal(6)), reader.IsDBNull(7) ? ((decimal?)null) : new decimal?(reader.GetDecimal(7)), reader.IsDBNull(8) ? ((decimal?)null) : new decimal?(reader.GetDecimal(8)), reader.IsDBNull(9) ? ((decimal?)null) : new decimal?(reader.GetDecimal(9)), reader.IsDBNull(10) ? ((int?)null) : new int?(reader.GetInt32(10)), reader.IsDBNull(11) ? ((decimal?)null) : new decimal?(reader.GetDecimal(11)), reader.IsDBNull(12) ? ((decimal?)null) : new decimal?(reader.GetDecimal(12)), reader.IsDBNull(13) ? ((decimal?)null) : new decimal?(reader.GetDecimal(13)), reader.IsDBNull(14) ? ((decimal?)null) : new decimal?(reader.GetDecimal(14)), reader.IsDBNull(15) ? ((decimal?)null) : new decimal?(reader.GetDecimal(15)), reader.IsDBNull(16) ? ((decimal?)null) : new decimal?(reader.GetDecimal(16)), reader.IsDBNull(17) ? ((decimal?)null) : new decimal?(reader.GetDecimal(17)), reader.IsDBNull(18) ? ((decimal?)null) : new decimal?(reader.GetDecimal(18)), reader.IsDBNull(19) ? ((decimal?)null) : new decimal?(reader.GetDecimal(19)), reader.IsDBNull(20) ? ((decimal?)null) : new decimal?(reader.GetDecimal(20))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<RejectionAnalysisReport> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<RejectionAnalysisReport> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH rejected AS (\n    SELECT count(*) AS total_rejected FROM signals WHERE NOT accepted\n),\nreason_counts AS (\n    SELECT sr.reason_code, count(*) AS reason_count, max(sr.created_at_utc) AS last_rejected_at\n    FROM signal_rejections sr\n    GROUP BY sr.reason_code\n)\nSELECT\n    rc.reason_code,\n    rc.reason_count::integer,\n    CASE WHEN r.total_rejected = 0 THEN 0 ELSE round(rc.reason_count::numeric / r.total_rejected * 100, 4) END AS rejected_pct,\n    rc.last_rejected_at\nFROM reason_counts rc\nCROSS JOIN rejected r\nORDER BY rc.reason_count DESC, rc.reason_code\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<RejectionAnalysisReport> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<RejectionAnalysisReport> results = new List<RejectionAnalysisReport>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new RejectionAnalysisReport(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2), reader.IsDBNull(3) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(3)))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO service_command_audit (id, command, source, accepted, message, created_at_utc)\nVALUES (@Id, @Command, @Source, @Accepted, @Message, @CreatedAtUtc);");
		command.Parameters.AddWithValue("Id", audit.Id);
		command.Parameters.AddWithValue("Command", audit.Command);
		command.Parameters.AddWithValue("Source", audit.Source);
		command.Parameters.AddWithValue("Accepted", audit.Accepted);
		command.Parameters.AddWithValue("Message", audit.Message);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(audit.CreatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ServiceCommandAudit> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<ServiceCommandAudit> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, command, source, accepted, message, created_at_utc\nFROM service_command_audit\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				command.Parameters.AddWithValue("Limit", limit);
				IReadOnlyList<ServiceCommandAudit> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<ServiceCommandAudit> results = new List<ServiceCommandAudit>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new ServiceCommandAudit(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4), DateTimeOffsetFromUtc(reader.GetDateTime(5))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO scanner_status (\n    scanner_name, status, last_successful_scan_utc, last_error_utc, last_error_message,\n    trades_fetched, new_trades_stored, positions_fetched, updated_at_utc\n) VALUES (\n    @ScannerName, @Status, @LastSuccessfulScanUtc, @LastErrorUtc, @LastErrorMessage,\n    @TradesFetched, @NewTradesStored, @PositionsFetched, @UpdatedAtUtc\n)\nON CONFLICT(scanner_name) DO UPDATE SET\n    status = excluded.status,\n    last_successful_scan_utc = excluded.last_successful_scan_utc,\n    last_error_utc = excluded.last_error_utc,\n    last_error_message = excluded.last_error_message,\n    trades_fetched = excluded.trades_fetched,\n    new_trades_stored = excluded.new_trades_stored,\n    positions_fetched = excluded.positions_fetched,\n    updated_at_utc = excluded.updated_at_utc\nWHERE\n    scanner_status.status IS DISTINCT FROM excluded.status\n    OR scanner_status.last_error_utc IS DISTINCT FROM excluded.last_error_utc\n    OR scanner_status.last_error_message IS DISTINCT FROM excluded.last_error_message\n    OR scanner_status.trades_fetched IS DISTINCT FROM excluded.trades_fetched\n    OR scanner_status.new_trades_stored IS DISTINCT FROM excluded.new_trades_stored\n    OR scanner_status.positions_fetched IS DISTINCT FROM excluded.positions_fetched\n    OR scanner_status.updated_at_utc <= excluded.updated_at_utc - interval '60 seconds';");
		command.Parameters.AddWithValue("ScannerName", status.ScannerName);
		command.Parameters.AddWithValue("Status", status.ScannerStatus);
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? lastSuccessfulScanUtc = status.LastSuccessfulScanUtc;
		object value;
		if (lastSuccessfulScanUtc.HasValue)
		{
			DateTimeOffset successfulScan = lastSuccessfulScanUtc.GetValueOrDefault();
			value = UtcDateTime(successfulScan);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("LastSuccessfulScanUtc", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		lastSuccessfulScanUtc = status.LastErrorUtc;
		object value2;
		if (lastSuccessfulScanUtc.HasValue)
		{
			DateTimeOffset errorUtc = lastSuccessfulScanUtc.GetValueOrDefault();
			value2 = UtcDateTime(errorUtc);
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("LastErrorUtc", value2);
		command.Parameters.AddWithValue("LastErrorMessage", ((object)status.LastErrorMessage) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("TradesFetched", status.TradesFetched);
		command.Parameters.AddWithValue("NewTradesStored", status.NewTradesStored);
		command.Parameters.AddWithValue("PositionsFetched", status.PositionsFetched);
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(status.UpdatedAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ScannerStatusSnapshot> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<ScannerStatusSnapshot> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT scanner_name, status, last_successful_scan_utc, last_error_utc, last_error_message,\n       trades_fetched, new_trades_stored, positions_fetched, updated_at_utc\nFROM scanner_status\nORDER BY scanner_name;"))
			{
				IReadOnlyList<ScannerStatusSnapshot> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<ScannerStatusSnapshot> results = new List<ScannerStatusSnapshot>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new ScannerStatusSnapshot(reader.GetString(0), reader.IsDBNull(2) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(2))), reader.IsDBNull(3) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(3))), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(1), DateTimeOffsetFromUtc(reader.GetDateTime(8))));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO service_heartbeats (\n    service_name, status, started_at_utc, last_heartbeat_utc, version, mode, current_loop, last_error\n) VALUES (\n    @ServiceName, @Status, @StartedAtUtc, @LastHeartbeatUtc, @Version, @Mode, @CurrentLoop, @LastError\n)\nON CONFLICT(service_name) DO UPDATE SET\n    status = excluded.status,\n    started_at_utc = excluded.started_at_utc,\n    last_heartbeat_utc = excluded.last_heartbeat_utc,\n    version = excluded.version,\n    mode = excluded.mode,\n    current_loop = excluded.current_loop,\n    last_error = excluded.last_error\nWHERE\n    service_heartbeats.status IS DISTINCT FROM excluded.status\n    OR service_heartbeats.started_at_utc IS DISTINCT FROM excluded.started_at_utc\n    OR service_heartbeats.version IS DISTINCT FROM excluded.version\n    OR service_heartbeats.mode IS DISTINCT FROM excluded.mode\n    OR service_heartbeats.current_loop IS DISTINCT FROM excluded.current_loop\n    OR service_heartbeats.last_error IS DISTINCT FROM excluded.last_error\n    OR service_heartbeats.last_heartbeat_utc <= excluded.last_heartbeat_utc - interval '60 seconds';");
		command.Parameters.AddWithValue("ServiceName", heartbeat.ServiceName);
		command.Parameters.AddWithValue("Status", heartbeat.Status);
		command.Parameters.AddWithValue("StartedAtUtc", UtcDateTime(heartbeat.StartedAtUtc));
		command.Parameters.AddWithValue("LastHeartbeatUtc", UtcDateTime(heartbeat.LastHeartbeatUtc));
		command.Parameters.AddWithValue("Version", heartbeat.Version);
		command.Parameters.AddWithValue("Mode", heartbeat.Mode.ToString());
		command.Parameters.AddWithValue("CurrentLoop", heartbeat.CurrentLoop);
		command.Parameters.AddWithValue("LastError", ((object)heartbeat.LastError) ?? ((object)DBNull.Value));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<ServiceHeartbeat> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<ServiceHeartbeat> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT service_name, status, started_at_utc, last_heartbeat_utc, version, mode, current_loop, last_error\nFROM service_heartbeats\nORDER BY service_name;"))
			{
				IReadOnlyList<ServiceHeartbeat> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<ServiceHeartbeat> results = new List<ServiceHeartbeat>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(new ServiceHeartbeat(reader.GetString(0), reader.GetString(1), DateTimeOffsetFromUtc(reader.GetDateTime(2)), DateTimeOffsetFromUtc(reader.GetDateTime(3)), reader.GetString(4), Enum.Parse<BotMode>(reader.GetString(5)), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
	{
		NpgsqlConnection connection = connectionFactory.CreateConnection();
		await connection.OpenAsync(cancellationToken);
		return connection;
	}

	private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, string sql)
	{
		return new NpgsqlCommand(sql, connection);
	}

	private static async Task<bool> TryAcquireOnChainDerivedRefreshLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT pg_try_advisory_xact_lock(@LockKey1, @LockKey2);"))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("LockKey1", 1348686930);
			command.Parameters.AddWithValue("LockKey2", 1329812038);
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool acquired = default(bool);
			int num;
			if (result is bool)
			{
				acquired = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (acquired ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task AcquirePaperCopiedTraderPerformanceRefreshLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT pg_advisory_xact_lock(@LockKey1, @LockKey2);");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("LockKey1", PaperCopiedTraderPerformanceRefreshLockKey1);
		command.Parameters.AddWithValue("LockKey2", PaperCopiedTraderPerformanceRefreshLockKey2);
		await command.ExecuteNonQueryAsync(cancellationToken);
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

	private static object NullableDateTime(DateTimeOffset? timestamp)
	{
		return timestamp.HasValue ? UtcDateTime(timestamp.Value) : DBNull.Value;
	}

	private static object NullableDecimal(decimal? value)
	{
		return value.HasValue ? value.Value : DBNull.Value;
	}

	private static object NullableGuid(Guid? value)
	{
		return value.HasValue ? value.Value : DBNull.Value;
	}

	private static PolymarketGammaMarket ReadPolymarketGammaMarket(NpgsqlDataReader reader)
	{
		return new PolymarketGammaMarket(
			reader.GetString(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.IsDBNull(5) ? null : reader.GetString(5),
			reader.IsDBNull(6) ? null : reader.GetString(6),
			reader.IsDBNull(7) ? null : reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetString(8),
			reader.IsDBNull(9) ? null : reader.GetString(9),
			reader.GetBoolean(10),
			reader.GetBoolean(11),
			reader.GetBoolean(12),
			reader.GetBoolean(13),
			reader.GetBoolean(14),
			reader.GetBoolean(15),
			reader.GetBoolean(16),
			reader.IsDBNull(17) ? null : reader.GetDecimal(17),
			reader.IsDBNull(18) ? null : reader.GetDecimal(18),
			reader.IsDBNull(19) ? null : reader.GetDecimal(19),
			reader.IsDBNull(20) ? null : reader.GetDecimal(20),
			reader.IsDBNull(21) ? null : reader.GetDecimal(21),
			reader.IsDBNull(22) ? null : reader.GetDecimal(22),
			reader.IsDBNull(23) ? null : reader.GetDecimal(23),
			reader.IsDBNull(24) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(24)),
			reader.IsDBNull(25) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(25)),
			reader.IsDBNull(26) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(26)),
			reader.IsDBNull(27) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(27)),
			reader.IsDBNull(28) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(28)),
			ReadJsonStringArray(reader, 29),
			ReadJsonStringArray(reader, 30),
			reader.GetString(31),
			DateTimeOffsetFromUtc(reader.GetDateTime(32)),
			reader.IsDBNull(33) ? null : reader.GetDecimal(33),
			reader.IsDBNull(34) ? null : reader.GetDecimal(34),
			reader.IsDBNull(35) ? null : reader.GetDecimal(35));
	}

	private static StrategyMarketPaperRun ReadStrategyMarketPaperRun(NpgsqlDataReader reader)
	{
		return new StrategyMarketPaperRun(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetString(5),
			reader.IsDBNull(6) ? null : reader.GetString(6),
			reader.IsDBNull(7) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(7)),
			reader.IsDBNull(8) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			DateTimeOffsetFromUtc(reader.GetDateTime(9)),
			DateTimeOffsetFromUtc(reader.GetDateTime(10)),
			reader.GetString(11),
			reader.IsDBNull(12) ? null : reader.GetString(12),
			reader.IsDBNull(13) ? null : reader.GetString(13),
			reader.IsDBNull(14) ? null : reader.GetDecimal(14),
			reader.GetDecimal(15),
			reader.IsDBNull(16) ? null : reader.GetDecimal(16),
			reader.IsDBNull(17) ? null : reader.GetGuid(17),
			reader.IsDBNull(18) ? null : reader.GetGuid(18),
			reader.IsDBNull(19) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(19)),
			reader.IsDBNull(20) ? null : reader.GetDecimal(20),
			reader.IsDBNull(21) ? null : reader.GetDecimal(21),
			reader.IsDBNull(22) ? null : reader.GetDecimal(22),
			reader.IsDBNull(23) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(23)),
			reader.IsDBNull(24) ? null : reader.GetString(24),
			DateTimeOffsetFromUtc(reader.GetDateTime(25)),
			DateTimeOffsetFromUtc(reader.GetDateTime(26)),
			reader.IsDBNull(27) ? null : reader.GetString(27));
	}

	private static void AddStrategyMarketPaperRunParameters(NpgsqlCommand command, StrategyMarketPaperRun run)
	{
		command.Parameters.AddWithValue("Id", run.Id);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(run.StrategyId));
		command.Parameters.AddWithValue("MarketId", run.MarketId);
		command.Parameters.AddWithValue("ConditionId", run.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", run.MarketSlug);
		command.Parameters.AddWithValue("MarketTitle", run.MarketTitle);
		command.Parameters.AddWithValue("Category", ((object?)run.Category) ?? DBNull.Value);
		command.Parameters.AddWithValue("MarketStartUtc", NullableDateTime(run.MarketStartUtc));
		command.Parameters.AddWithValue("MarketEndUtc", NullableDateTime(run.MarketEndUtc));
		command.Parameters.AddWithValue("DetectedAtUtc", UtcDateTime(run.DetectedAtUtc));
		command.Parameters.AddWithValue("EntryDueAtUtc", UtcDateTime(run.EntryDueAtUtc));
		command.Parameters.AddWithValue("Status", run.Status);
		command.Parameters.AddWithValue("SelectedAssetId", ((object?)run.SelectedAssetId) ?? DBNull.Value);
		command.Parameters.AddWithValue("SelectedOutcome", ((object?)run.SelectedOutcome) ?? DBNull.Value);
		command.Parameters.AddWithValue("EntryPrice", NullableDecimal(run.EntryPrice));
		command.Parameters.AddWithValue("StakeUsd", run.StakeUsd);
		command.Parameters.AddWithValue("SizeShares", NullableDecimal(run.SizeShares));
		command.Parameters.AddWithValue("SignalId", NullableGuid(run.SignalId));
		command.Parameters.AddWithValue("PaperOrderId", NullableGuid(run.PaperOrderId));
		command.Parameters.AddWithValue("EnteredAtUtc", NullableDateTime(run.EnteredAtUtc));
		command.Parameters.AddWithValue("SettlementPrice", NullableDecimal(run.SettlementPrice));
		command.Parameters.AddWithValue("SettlementValueUsd", NullableDecimal(run.SettlementValueUsd));
		command.Parameters.AddWithValue("RealizedPnlUsd", NullableDecimal(run.RealizedPnlUsd));
		command.Parameters.AddWithValue("SettledAtUtc", NullableDateTime(run.SettledAtUtc));
		command.Parameters.AddWithValue("SkipReason", ((object?)run.SkipReason) ?? DBNull.Value);
		command.Parameters.AddWithValue("SkipDiagnosticsJson", ((object?)run.SkipDiagnosticsJson) ?? DBNull.Value);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(run.CreatedAtUtc));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(run.UpdatedAtUtc));
	}

	private static PaperCopiedLeaderPosition ReadPaperCopiedLeaderPosition(NpgsqlDataReader reader)
	{
		return new PaperCopiedLeaderPosition(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetGuid(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetString(5),
			reader.GetString(6),
			reader.IsDBNull(7) ? null : reader.GetString(7),
			DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			reader.GetDecimal(9),
			reader.GetDecimal(10),
			reader.GetDecimal(11),
			reader.GetDecimal(12),
			reader.GetDecimal(13),
			Enum.Parse<PaperCopiedLeaderPositionStatus>(reader.GetString(14)),
			reader.IsDBNull(15) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(15)),
			reader.IsDBNull(16) ? null : reader.GetString(16),
			reader.IsDBNull(17) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(17)),
			DateTimeOffsetFromUtc(reader.GetDateTime(18)),
			DateTimeOffsetFromUtc(reader.GetDateTime(19)),
			DateTimeOffsetFromUtc(reader.GetDateTime(20)));
	}

	private static void AddPaperCopiedLeaderPositionParameters(NpgsqlCommand command, PaperCopiedLeaderPosition position)
	{
		command.Parameters.AddWithValue("Id", position.Id);
		command.Parameters.AddWithValue("EntrySignalId", position.EntrySignalId);
		command.Parameters.AddWithValue("EntryPaperOrderId", position.EntryPaperOrderId);
		command.Parameters.AddWithValue("CopiedTraderWallet", position.CopiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", position.AssetId);
		command.Parameters.AddWithValue("ConditionId", position.ConditionId);
		command.Parameters.AddWithValue("Outcome", position.Outcome);
		command.Parameters.AddWithValue("EntryTransactionHash", ((object?)position.EntryTransactionHash) ?? DBNull.Value);
		command.Parameters.AddWithValue("EntryTimestampUtc", UtcDateTime(position.EntryTimestampUtc));
		command.Parameters.AddWithValue("LeaderEntryPrice", position.LeaderEntryPrice);
		command.Parameters.AddWithValue("LeaderInitialSizeShares", position.LeaderInitialSizeShares);
		command.Parameters.AddWithValue("CopiedInitialSizeShares", position.CopiedInitialSizeShares);
		command.Parameters.AddWithValue("LeaderSoldSizeShares", position.LeaderSoldSizeShares);
		command.Parameters.AddWithValue("CopiedExitRequestedSizeShares", position.CopiedExitRequestedSizeShares);
		command.Parameters.AddWithValue("Status", position.Status.ToString());
		command.Parameters.AddWithValue("LastActivityTimestampUtc", position.LastActivityTimestampUtc.HasValue ? UtcDateTime(position.LastActivityTimestampUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("LastActivityTransactionHash", ((object?)position.LastActivityTransactionHash) ?? DBNull.Value);
		command.Parameters.AddWithValue("LastActivitySyncAtUtc", position.LastActivitySyncAtUtc.HasValue ? UtcDateTime(position.LastActivitySyncAtUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("NextActivitySyncAtUtc", UtcDateTime(position.NextActivitySyncAtUtc));
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(position.CreatedAtUtc));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(position.UpdatedAtUtc));
	}

	private static void AddSignalParameters(NpgsqlCommand command, Signal signal)
	{
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
		command.Parameters.AddWithValue("ProposedPaperPrice", ((object?)signal.ProposedPaperPrice) ?? DBNull.Value);
		command.Parameters.AddWithValue("ProposedSizeShares", ((object?)signal.ProposedSizeShares) ?? DBNull.Value);
		command.Parameters.AddWithValue("ProposedNotionalUsd", ((object?)signal.ProposedNotionalUsd) ?? DBNull.Value);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(signal.CreatedAtUtc));
		command.Parameters.AddWithValue("RawContextJson", JsonSerializer.Serialize(signal));
	}

	private static void AddPaperOrderParameters(NpgsqlCommand command, PaperOrder order)
	{
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("SignalId", order.SignalId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("CopiedTraderWallet", order.CopiedTraderWallet);
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
		command.Parameters.AddWithValue("FilledAtUtc", order.FilledAtUtc.HasValue ? UtcDateTime(order.FilledAtUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("CancelledAtUtc", order.CancelledAtUtc.HasValue ? UtcDateTime(order.CancelledAtUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("RawDecisionJson", BuildPaperOrderRawDecisionJson(order));
		command.Parameters.AddWithValue("CorrelationId", ((object)order.CorrelationId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource ?? string.Empty);
	}

	private static PaperOrder NormalizePaperOrderStrategy(PaperOrder order)
	{
		return order.StrategyId == Guid.Empty
			? order with { StrategyId = StrategyIds.FollowLeader }
			: order;
	}

	private static string BuildPaperOrderRawDecisionJson(PaperOrder order)
	{
		return string.IsNullOrWhiteSpace(order.RawDecisionJson)
			? JsonSerializer.Serialize(NormalizePaperOrderStrategy(order) with { RawDecisionJson = null })
			: order.RawDecisionJson;
	}

	private static BtcUpDown5mMarketResult? TryCreateBtcUpDown5mMarketResult(
		IGrouping<string, BtcUpDown5mSettledRunRow> group)
	{
		var rows = group
			.OrderByDescending(row => row.MarketStartUtc ?? row.MarketEndUtc ?? row.SettledAtUtc)
			.ThenByDescending(row => row.SettledAtUtc)
			.ToArray();
		var winners = rows
			.Select(row => TryInferBtcWinningOutcome(row.SelectedOutcome, row.RealizedPnlUsd))
			.Where(outcome => !string.IsNullOrWhiteSpace(outcome))
			.Select(outcome => outcome!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (winners.Length != 1)
		{
			return null;
		}

		var latest = rows[0];
		return new BtcUpDown5mMarketResult(
			latest.MarketId,
			latest.ConditionId,
			latest.MarketSlug,
			latest.MarketStartUtc,
			latest.MarketEndUtc,
			winners[0],
			rows.Max(row => row.SettledAtUtc));
	}

	private static string? TryInferBtcWinningOutcome(string selectedOutcome, decimal realizedPnlUsd)
	{
		var normalized = NormalizeBtcOutcome(selectedOutcome);
		if (normalized is null || realizedPnlUsd == 0m)
		{
			return null;
		}

		return realizedPnlUsd > 0m ? normalized : OppositeBtcOutcome(normalized);
	}

	private static string? NormalizeBtcOutcome(string? outcome)
	{
		if (string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase))
		{
			return "Up";
		}

		return string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase) ? "Down" : null;
	}

	private static string OppositeBtcOutcome(string outcome)
	{
		return string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Down" : "Up";
	}

	private sealed record BtcUpDown5mSettledRunRow(
		string MarketId,
		string ConditionId,
		string MarketSlug,
		DateTimeOffset? MarketStartUtc,
		DateTimeOffset? MarketEndUtc,
		string SelectedOutcome,
		decimal RealizedPnlUsd,
		DateTimeOffset SettledAtUtc);

	private static void AddOnChainPaperSignalResultParameters(NpgsqlCommand command, OnChainPaperSignalResult result)
	{
		command.Parameters.AddWithValue("Id", result.Id);
		command.Parameters.AddWithValue("CaptureId", result.CaptureId);
		command.Parameters.AddWithValue("TransactionHash", result.TransactionHash);
		command.Parameters.AddWithValue("LogIndex", result.LogIndex);
		command.Parameters.AddWithValue("ParticipantRole", result.ParticipantRole.ToString());
		command.Parameters.AddWithValue("CopiedTraderWallet", result.CopiedTraderWallet);
		command.Parameters.AddWithValue("CounterpartyWallet", result.CounterpartyWallet);
		command.Parameters.AddWithValue("Side", result.Side.ToString());
		command.Parameters.AddWithValue("TokenId", result.TokenId);
		command.Parameters.AddWithValue("ConditionId", result.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", result.MarketSlug);
		command.Parameters.AddWithValue("Outcome", result.Outcome);
		command.Parameters.AddWithValue("LocalCategory", ((object?)result.LocalCategory) ?? DBNull.Value);
		command.Parameters.AddWithValue("PolymarketCategory", ((object?)result.PolymarketCategory) ?? DBNull.Value);
		command.Parameters.AddWithValue("RatingFound", result.RatingFound.HasValue ? result.RatingFound.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("LeaderboardRank", result.LeaderboardRank.HasValue ? result.LeaderboardRank.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("LeaderboardPnlUsd", result.LeaderboardPnlUsd.HasValue ? result.LeaderboardPnlUsd.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("LeaderboardVolumeUsd", result.LeaderboardVolumeUsd.HasValue ? result.LeaderboardVolumeUsd.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("LeaderboardPnlToVolumePct", result.LeaderboardPnlToVolumePct.HasValue ? result.LeaderboardPnlToVolumePct.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("SignalId", result.SignalId.HasValue ? result.SignalId.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("PaperOrderId", result.PaperOrderId.HasValue ? result.PaperOrderId.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("Status", result.Status);
		command.Parameters.AddWithValue("DecisionCode", result.DecisionCode);
		command.Parameters.AddWithValue("ReasonDetails", result.ReasonDetails);
		command.Parameters.AddWithValue("ProcessedAtUtc", UtcDateTime(result.ProcessedAtUtc));
	}

	private static PaperOrder ReadPaperOrder(NpgsqlDataReader reader)
	{
		return new PaperOrder(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetString(3),
			Enum.Parse<PaperOrderStatus>(reader.GetString(4)),
			Enum.Parse<TradeSide>(reader.GetString(5)),
			reader.GetString(6),
			reader.GetString(7),
			reader.GetString(8),
			reader.GetDecimal(9),
			reader.GetDecimal(10),
			reader.GetDecimal(11),
			DateTimeOffsetFromUtc(reader.GetDateTime(12)),
			DateTimeOffsetFromUtc(reader.GetDateTime(13)),
			reader.IsDBNull(14) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(14))),
			reader.IsDBNull(15) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(15))),
			reader.GetGuid(2),
			reader.IsDBNull(16) ? null : reader.GetString(16),
			reader.IsDBNull(17) ? null : reader.GetGuid(17),
			reader.IsDBNull(18) ? string.Empty : reader.GetString(18));
	}

	private static PaperFill ReadPaperFill(NpgsqlDataReader reader)
	{
		return new PaperFill(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetDecimal(2),
			reader.GetDecimal(3),
			DateTimeOffsetFromUtc(reader.GetDateTime(4)),
			reader.GetString(5),
			reader.GetDecimal(6));
	}

	private static PaperCopiedTraderPerformance ReadPaperCopiedTraderPerformance(NpgsqlDataReader reader)
	{
		return new PaperCopiedTraderPerformance(
			reader.GetString(0),
			reader.GetString(1),
			reader.GetInt32(2),
			reader.GetInt32(3),
			reader.GetInt32(4),
			reader.GetInt32(5),
			reader.GetInt32(6),
			reader.GetInt32(7),
			reader.GetInt32(8),
			reader.GetInt32(9),
			reader.GetDecimal(10),
			reader.GetDecimal(11),
			reader.GetDecimal(12),
			reader.GetDecimal(13),
			reader.GetDecimal(14),
			reader.GetDecimal(15),
			reader.GetDecimal(16),
			reader.GetDecimal(17),
			reader.GetDecimal(18),
			reader.IsDBNull(19) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(19)),
			reader.IsDBNull(20) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(20)),
			DateTimeOffsetFromUtc(reader.GetDateTime(21)));
	}

	private static OrderBookSnapshot ReadOrderBookSnapshot(NpgsqlDataReader reader)
	{
		decimal? bestBid = (reader.IsDBNull(2) ? ((decimal?)null) : new decimal?(reader.GetDecimal(2)));
		decimal? bestAsk = (reader.IsDBNull(3) ? ((decimal?)null) : new decimal?(reader.GetDecimal(3)));
		string assetId = reader.GetString(0);
		IReadOnlyList<OrderBookLevel> bids;
		if (bestBid.HasValue)
		{
			decimal bid = bestBid.GetValueOrDefault();
			IReadOnlyList<OrderBookLevel> readOnlyList = new[] { new OrderBookLevel(bid, 0m) };
			bids = readOnlyList;
		}
		else
		{
			IReadOnlyList<OrderBookLevel> readOnlyList = Array.Empty<OrderBookLevel>();
			bids = readOnlyList;
		}
		IReadOnlyList<OrderBookLevel> asks;
		if (bestAsk.HasValue)
		{
			decimal ask = bestAsk.GetValueOrDefault();
			IReadOnlyList<OrderBookLevel> readOnlyList = new[] { new OrderBookLevel(ask, 0m) };
			asks = readOnlyList;
		}
		else
		{
			IReadOnlyList<OrderBookLevel> readOnlyList = Array.Empty<OrderBookLevel>();
			asks = readOnlyList;
		}
		return new OrderBookSnapshot(assetId, bids, asks, DateTimeOffsetFromUtc(reader.GetDateTime(4)), reader.IsDBNull(1) ? null : reader.GetString(1));
	}

	private static DailyReport ReadDailyReport(NpgsqlDataReader reader)
	{
		return new DailyReport(reader.GetFieldValue<DateOnly>(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetString(9), reader.GetInt32(10), DateTimeOffsetFromUtc(reader.GetDateTime(11)));
	}

	private static void AddPolymarketHttpLogParameters(NpgsqlCommand command, PolymarketHttpLogEntry entry)
	{
		command.Parameters.AddWithValue("Id", entry.Id);
		command.Parameters.AddWithValue("Component", entry.Component);
		command.Parameters.AddWithValue("Operation", entry.Operation);
		command.Parameters.AddWithValue("HttpMethod", entry.HttpMethod);
		command.Parameters.AddWithValue("RequestUrl", entry.RequestUrl);
		command.Parameters.AddWithValue("RequestedAtUtc", UtcDateTime(entry.RequestedAtUtc));
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? responseAtUtc = entry.ResponseAtUtc;
		object value;
		if (responseAtUtc.HasValue)
		{
			DateTimeOffset responseAt = responseAtUtc.GetValueOrDefault();
			value = UtcDateTime(responseAt);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("ResponseAtUtc", value);
		command.Parameters.AddWithValue("DurationMs", entry.DurationMilliseconds);
		command.Parameters.AddWithValue("Attempt", entry.Attempt);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		int? statusCode = entry.StatusCode;
		object value2;
		if (statusCode.HasValue)
		{
			int statusCode2 = statusCode.GetValueOrDefault();
			value2 = statusCode2;
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("StatusCode", value2);
		command.Parameters.AddWithValue("Succeeded", entry.Succeeded);
		command.Parameters.AddWithValue("ResponseBody", entry.ResponseBody);
		command.Parameters.AddWithValue("ErrorMessage", ((object)entry.ErrorMessage) ?? ((object)DBNull.Value));
	}

	private static void AddPolymarketGammaMarketParameters(NpgsqlCommand command, PolymarketGammaMarket market)
	{
		command.Parameters.AddWithValue("MarketId", market.MarketId);
		command.Parameters.AddWithValue("ConditionId", market.ConditionId);
		command.Parameters.AddWithValue("QuestionId", market.QuestionId);
		command.Parameters.AddWithValue("Slug", market.Slug);
		command.Parameters.AddWithValue("Question", market.Question);
		command.Parameters.AddWithValue("EventId", ((object)market.EventId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("EventSlug", ((object)market.EventSlug) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("EventTitle", ((object)market.EventTitle) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("SeriesSlug", ((object)market.SeriesSlug) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Category", ((object)market.Category) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Active", market.Active);
		command.Parameters.AddWithValue("Closed", market.Closed);
		command.Parameters.AddWithValue("Archived", market.Archived);
		command.Parameters.AddWithValue("Restricted", market.Restricted);
		command.Parameters.AddWithValue("AcceptingOrders", market.AcceptingOrders);
		command.Parameters.AddWithValue("EnableOrderBook", market.EnableOrderBook);
		command.Parameters.AddWithValue("NegativeRisk", market.NegativeRisk);
		NpgsqlParameterCollection parameters = command.Parameters;
		decimal? liquidity = market.Liquidity;
		object value;
		if (liquidity.HasValue)
		{
			decimal liquidity2 = liquidity.GetValueOrDefault();
			value = liquidity2;
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("Liquidity", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		liquidity = market.LiquidityClob;
		object value2;
		if (liquidity.HasValue)
		{
			decimal liquidityClob = liquidity.GetValueOrDefault();
			value2 = liquidityClob;
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("LiquidityClob", value2);
		NpgsqlParameterCollection parameters3 = command.Parameters;
		liquidity = market.Volume;
		object value3;
		if (liquidity.HasValue)
		{
			decimal volume = liquidity.GetValueOrDefault();
			value3 = volume;
		}
		else
		{
			value3 = DBNull.Value;
		}
		parameters3.AddWithValue("Volume", value3);
		NpgsqlParameterCollection parameters4 = command.Parameters;
		liquidity = market.Volume24Hr;
		object value4;
		if (liquidity.HasValue)
		{
			decimal volume24Hr = liquidity.GetValueOrDefault();
			value4 = volume24Hr;
		}
		else
		{
			value4 = DBNull.Value;
		}
		parameters4.AddWithValue("Volume24Hr", value4);
		NpgsqlParameterCollection parameters5 = command.Parameters;
		liquidity = market.BestBid;
		object value5;
		if (liquidity.HasValue)
		{
			decimal bestBid = liquidity.GetValueOrDefault();
			value5 = bestBid;
		}
		else
		{
			value5 = DBNull.Value;
		}
		parameters5.AddWithValue("BestBid", value5);
		NpgsqlParameterCollection parameters6 = command.Parameters;
		liquidity = market.BestAsk;
		object value6;
		if (liquidity.HasValue)
		{
			decimal bestAsk = liquidity.GetValueOrDefault();
			value6 = bestAsk;
		}
		else
		{
			value6 = DBNull.Value;
		}
		parameters6.AddWithValue("BestAsk", value6);
		NpgsqlParameterCollection parameters7 = command.Parameters;
		liquidity = market.Spread;
		object value7;
		if (liquidity.HasValue)
		{
			decimal spread = liquidity.GetValueOrDefault();
			value7 = spread;
		}
		else
		{
			value7 = DBNull.Value;
		}
		parameters7.AddWithValue("Spread", value7);
		NpgsqlParameterCollection parameters8 = command.Parameters;
		liquidity = market.LastTradePrice;
		object value8;
		if (liquidity.HasValue)
		{
			decimal lastTradePrice = liquidity.GetValueOrDefault();
			value8 = lastTradePrice;
		}
		else
		{
			value8 = DBNull.Value;
		}
		parameters8.AddWithValue("LastTradePrice", value8);
		NpgsqlParameterCollection parameters9 = command.Parameters;
		liquidity = market.OrderMinSize;
		object value9;
		if (liquidity.HasValue)
		{
			decimal orderMinSize = liquidity.GetValueOrDefault();
			value9 = orderMinSize;
		}
		else
		{
			value9 = DBNull.Value;
		}
		parameters9.AddWithValue("OrderMinSize", value9);
		NpgsqlParameterCollection parameters10 = command.Parameters;
		liquidity = market.OrderPriceMinTickSize;
		object value10;
		if (liquidity.HasValue)
		{
			decimal orderPriceMinTickSize = liquidity.GetValueOrDefault();
			value10 = orderPriceMinTickSize;
		}
		else
		{
			value10 = DBNull.Value;
		}
		parameters10.AddWithValue("OrderPriceMinTickSize", value10);
		NpgsqlParameterCollection parameters11 = command.Parameters;
		DateTimeOffset? createdAtUtc = market.CreatedAtUtc;
		object value11;
		if (createdAtUtc.HasValue)
		{
			DateTimeOffset createdAt = createdAtUtc.GetValueOrDefault();
			value11 = UtcDateTime(createdAt);
		}
		else
		{
			value11 = DBNull.Value;
		}
		parameters11.AddWithValue("CreatedAtUtc", value11);
		NpgsqlParameterCollection parameters12 = command.Parameters;
		createdAtUtc = market.UpdatedAtUtc;
		object value12;
		if (createdAtUtc.HasValue)
		{
			DateTimeOffset updatedAt = createdAtUtc.GetValueOrDefault();
			value12 = UtcDateTime(updatedAt);
		}
		else
		{
			value12 = DBNull.Value;
		}
		parameters12.AddWithValue("UpdatedAtUtc", value12);
		NpgsqlParameterCollection parameters13 = command.Parameters;
		createdAtUtc = market.StartDateUtc;
		object value13;
		if (createdAtUtc.HasValue)
		{
			DateTimeOffset startDate = createdAtUtc.GetValueOrDefault();
			value13 = UtcDateTime(startDate);
		}
		else
		{
			value13 = DBNull.Value;
		}
		parameters13.AddWithValue("StartDateUtc", value13);
		NpgsqlParameterCollection parameters14 = command.Parameters;
		createdAtUtc = market.EndDateUtc;
		object value14;
		if (createdAtUtc.HasValue)
		{
			DateTimeOffset endDate = createdAtUtc.GetValueOrDefault();
			value14 = UtcDateTime(endDate);
		}
		else
		{
			value14 = DBNull.Value;
		}
		parameters14.AddWithValue("EndDateUtc", value14);
		NpgsqlParameterCollection parameters15 = command.Parameters;
		createdAtUtc = market.EventStartTimeUtc;
		object value15;
		if (createdAtUtc.HasValue)
		{
			DateTimeOffset eventStartTime = createdAtUtc.GetValueOrDefault();
			value15 = UtcDateTime(eventStartTime);
		}
		else
		{
			value15 = DBNull.Value;
		}
		parameters15.AddWithValue("EventStartTimeUtc", value15);
		command.Parameters.AddWithValue("OutcomesJson", JsonSerializer.Serialize(market.Outcomes));
		command.Parameters.AddWithValue("ClobTokenIdsJson", JsonSerializer.Serialize(market.ClobTokenIds));
		command.Parameters.AddWithValue("RawJson", market.RawJson);
		command.Parameters.AddWithValue("FetchedAtUtc", UtcDateTime(market.FetchedAtUtc));
	}

	private static PolymarketHttpLogEntry ReadPolymarketHttpLogEntry(NpgsqlDataReader reader)
	{
		return new PolymarketHttpLogEntry(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), DateTimeOffsetFromUtc(reader.GetDateTime(5)), reader.IsDBNull(6) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(6))), reader.GetInt64(7), reader.GetInt32(8), reader.IsDBNull(9) ? ((int?)null) : new int?(reader.GetInt32(9)), reader.GetBoolean(10), reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12));
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
		command.Parameters.AddWithValue("Builder", ((object)fill.Builder) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Metadata", ((object)fill.Metadata) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ImportedAtUtc", UtcDateTime(fill.ImportedAtUtc));
	}

	private static void AddPolymarketOnChainTradeCaptureParameters(NpgsqlCommand command, PolymarketOnChainTradeCapture capture)
	{
		command.Parameters.AddWithValue("Id", capture.Id);
		command.Parameters.AddWithValue("ContractName", capture.ContractName);
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(capture.ContractAddress));
		command.Parameters.AddWithValue("ExchangeVersion", capture.ExchangeVersion);
		command.Parameters.AddWithValue("BlockNumber", capture.BlockNumber);
		command.Parameters.AddWithValue("BlockTimestampUtc", UtcDateTime(capture.BlockTimestampUtc));
		command.Parameters.AddWithValue("BlockHash", capture.BlockHash);
		command.Parameters.AddWithValue("TransactionHash", capture.TransactionHash);
		command.Parameters.AddWithValue("TransactionIndex", capture.TransactionIndex);
		command.Parameters.AddWithValue("LogIndex", capture.LogIndex);
		command.Parameters.AddWithValue("OrderHash", capture.OrderHash);
		command.Parameters.AddWithValue("Maker", capture.Maker);
		command.Parameters.AddWithValue("Taker", capture.Taker);
		command.Parameters.AddWithValue("Wallet", capture.Wallet);
		command.Parameters.AddWithValue("Side", capture.Side.ToString());
		command.Parameters.AddWithValue("TokenId", capture.TokenId);
		command.Parameters.AddWithValue("MakerAssetId", capture.MakerAssetId);
		command.Parameters.AddWithValue("TakerAssetId", capture.TakerAssetId);
		command.Parameters.AddWithValue("MakerAmountRaw", capture.MakerAmountRaw);
		command.Parameters.AddWithValue("TakerAmountRaw", capture.TakerAmountRaw);
		command.Parameters.AddWithValue("MakerAmount", capture.MakerAmount);
		command.Parameters.AddWithValue("TakerAmount", capture.TakerAmount);
		command.Parameters.AddWithValue("Price", capture.Price);
		command.Parameters.AddWithValue("SizeShares", capture.SizeShares);
		command.Parameters.AddWithValue("NotionalUsd", capture.NotionalUsd);
		command.Parameters.AddWithValue("FeeRaw", capture.FeeRaw);
		command.Parameters.AddWithValue("FeeAmount", capture.FeeAmount);
		command.Parameters.AddWithValue("FeeAssetId", capture.FeeAssetId);
		command.Parameters.AddWithValue("Builder", ((object)capture.Builder) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Metadata", ((object)capture.Metadata) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("RawTopicsJson", JsonSerializer.Serialize(capture.RawTopics));
		command.Parameters.AddWithValue("RawData", capture.RawData);
		command.Parameters.AddWithValue("Removed", capture.Removed);
		command.Parameters.AddWithValue("ObservedAtUtc", UtcDateTime(capture.ObservedAtUtc));
		command.Parameters.AddWithValue("ImportedAtUtc", UtcDateTime(capture.ImportedAtUtc));
	}

	private static async Task QueuePolymarketOnChainPositionRefreshTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<string> tokenIds, string reason, CancellationToken cancellationToken)
	{
		string[] distinctTokenIds = tokenIds.Where((string tokenId) => !string.IsNullOrWhiteSpace(tokenId)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (distinctTokenIds.Length == 0)
		{
			return;
		}
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)\nSELECT input.token_id, @Reason, now()\nFROM unnest(@TokenIds) AS input(token_id)\nWHERE EXISTS (\n    SELECT 1\n    FROM polymarket_onchain_wallet_executions execution\n    WHERE execution.token_id = input.token_id\n    LIMIT 1\n)\nON CONFLICT (token_id) DO NOTHING;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainTokenMetadataRefreshTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<string> tokenIds, string reason, CancellationToken cancellationToken)
	{
		string[] distinctTokenIds = tokenIds.Where((string tokenId) => !string.IsNullOrWhiteSpace(tokenId)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (distinctTokenIds.Length == 0)
		{
			return;
		}
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_token_metadata_refresh_queue (\n    token_id, reason, attempts, queued_at_utc, next_attempt_at_utc\n)\nSELECT unnest(@TokenIds), @Reason, 0, now(), now()\nON CONFLICT (token_id) DO NOTHING;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainTokenMetadataRefreshTokensForRangeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, string reason, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_token_metadata_refresh_queue (\n    token_id, reason, attempts, queued_at_utc, next_attempt_at_utc\n)\nSELECT DISTINCT execution.token_id, @Reason, 0, now(), now()\nFROM polymarket_onchain_wallet_fills execution\nLEFT JOIN polymarket_onchain_token_metadata metadata\n  ON metadata.token_id = execution.token_id\nWHERE execution.contract_address = @ContractAddress\n  AND execution.block_number BETWEEN @FromBlock AND @ToBlock\n  AND (\n      metadata.token_id IS NULL\n      OR NOT metadata.lookup_succeeded\n      OR NULLIF(metadata.category, '') IS NULL\n  )\nON CONFLICT (token_id) DO NOTHING;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task DeleteCompletedPolymarketOnChainTokenMetadataRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "DELETE FROM polymarket_onchain_token_metadata_refresh_queue refresh_queue\nUSING polymarket_onchain_token_metadata metadata\nWHERE metadata.token_id = refresh_queue.token_id\n  AND metadata.lookup_succeeded\n  AND NULLIF(metadata.category, '') IS NOT NULL;");
		command.Transaction = transaction;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task RescheduleIncompletePolymarketOnChainTokenMetadataRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<string> tokenIds, CancellationToken cancellationToken)
	{
		string[] distinctTokenIds = tokenIds.Where((string tokenId) => !string.IsNullOrWhiteSpace(tokenId)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		if (distinctTokenIds.Length == 0)
		{
			return;
		}
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE polymarket_onchain_token_metadata_refresh_queue refresh_queue\nSET\n    reason = 'metadata_retry',\n    attempts = refresh_queue.attempts + 1,\n    last_attempted_at_utc = now(),\n    next_attempt_at_utc = now() + (LEAST((refresh_queue.attempts + 1) * 5, 60)::text || ' minutes')::interval,\n    last_error = COALESCE(\n        metadata.lookup_error,\n        CASE\n            WHEN NULLIF(metadata.category, '') IS NULL THEN 'Metadata category is missing.'\n            ELSE NULL\n        END)\nFROM polymarket_onchain_token_metadata metadata\nWHERE metadata.token_id = refresh_queue.token_id\n  AND refresh_queue.token_id = ANY(@TokenIds)\n  AND (\n      NOT metadata.lookup_succeeded\n      OR NULLIF(metadata.category, '') IS NULL\n  );");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("TokenIds", distinctTokenIds);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainPositionRefreshTokensForRangeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, string reason, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)\nSELECT DISTINCT token_id, @Reason, now()\nFROM polymarket_onchain_wallet_fills\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock\nON CONFLICT (token_id) DO NOTHING;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainWalletActivityRefreshForRangeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string contractAddress, long fromBlock, long toBlock, string reason, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)\nSELECT DISTINCT wallet, @Reason, now()\nFROM polymarket_onchain_wallet_fills\nWHERE contract_address = @ContractAddress\n  AND block_number BETWEEN @FromBlock AND @ToBlock\nON CONFLICT (wallet) DO NOTHING;");
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		command.Parameters.AddWithValue("ContractAddress", NormalizeContractAddress(contractAddress));
		command.Parameters.AddWithValue("FromBlock", fromBlock);
		command.Parameters.AddWithValue("ToBlock", toBlock);
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<int> SeedMissingPolymarketOnChainPositionRefreshTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int tokenLimit, CancellationToken cancellationToken)
	{
		string initialBackfillComplete = await GetBotSettingAsync(connection, transaction, "onchain_positions_initial_backfill_complete", cancellationToken);
		bool positionsEmpty = await IsPolymarketOnChainPositionsEmptyAsync(connection, transaction, cancellationToken);
		if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !positionsEmpty)
		{
			return 0;
		}
		int queued;
		await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_position_refresh_queue (token_id, reason, queued_at_utc)\nSELECT missing.token_id, 'missing_position', now()\nFROM (\n    SELECT DISTINCT execution.token_id\n    FROM polymarket_onchain_wallet_executions execution\n    WHERE NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_positions position\n        WHERE position.token_id = execution.token_id\n    )\n    ORDER BY execution.token_id\n    LIMIT @TokenLimit\n) missing\nON CONFLICT (token_id) DO NOTHING;"))
		{
			command.Transaction = transaction;
			command.CommandTimeout = 300;
			command.Parameters.AddWithValue("TokenLimit", tokenLimit);
			queued = await command.ExecuteNonQueryAsync(cancellationToken);
		}
		if (queued != 0)
		{
			await UpsertBotSettingAsync(connection, transaction, "onchain_positions_initial_backfill_complete", "false", cancellationToken);
		}
		else
		{
			await UpsertBotSettingAsync(connection, transaction, "onchain_positions_initial_backfill_complete", "true", cancellationToken);
		}
		return queued;
	}

	private static async Task<bool> IsPolymarketOnChainPositionsEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_positions LIMIT 1);"))
		{
			command.Transaction = transaction;
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool empty = default(bool);
			int num;
			if (result is bool)
			{
				empty = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (empty ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task<int> CountTempPositionRefreshTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM temp_position_refresh_tokens;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountPolymarketOnChainPositionRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_position_refresh_queue;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> SeedMissingPolymarketOnChainWalletActivityRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int walletLimit, CancellationToken cancellationToken)
	{
		string initialBackfillComplete = await GetBotSettingAsync(connection, transaction, "onchain_wallet_activity_initial_backfill_complete", cancellationToken);
		bool activityEmpty = await IsPolymarketOnChainWalletActivityEmptyAsync(connection, transaction, cancellationToken);
		if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !activityEmpty)
		{
			return 0;
		}
		int queued;
		await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)\nSELECT missing.wallet, 'missing_activity', now()\nFROM (\n    SELECT DISTINCT fills.wallet\n    FROM polymarket_onchain_wallet_fills fills\n    WHERE NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_activity activity\n        WHERE activity.wallet = fills.wallet\n    )\n      AND NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_activity_refresh_queue queued_wallet\n        WHERE queued_wallet.wallet = fills.wallet\n    )\n    ORDER BY fills.wallet\n    LIMIT @WalletLimit\n) missing\nON CONFLICT (wallet) DO NOTHING;"))
		{
			command.Transaction = transaction;
			command.CommandTimeout = 300;
			command.Parameters.AddWithValue("WalletLimit", walletLimit);
			queued = await command.ExecuteNonQueryAsync(cancellationToken);
		}
		int queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
		await UpsertBotSettingAsync(connection, transaction, "onchain_wallet_activity_initial_backfill_complete", (queued == 0 && queueRemaining == 0) ? "true" : "false", cancellationToken);
		return queued;
	}

	private static async Task<bool> IsPolymarketOnChainWalletActivityEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_activity LIMIT 1);"))
		{
			command.Transaction = transaction;
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool empty = default(bool);
			int num;
			if (result is bool)
			{
				empty = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (empty ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task<int> CountTempWalletActivityRefreshWalletsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_activity_refresh_wallets;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountPolymarketOnChainWalletActivityRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_activity_refresh_queue;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> SeedMissingPolymarketOnChainParticipantDetailsRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int walletLimit, CancellationToken cancellationToken)
	{
		string initialBackfillComplete = await GetBotSettingAsync(connection, transaction, "onchain_participant_details_initial_backfill_complete", cancellationToken);
		bool participantDetailsEmpty = await IsPolymarketOnChainParticipantDetailsEmptyAsync(connection, transaction, cancellationToken);
		if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !participantDetailsEmpty)
		{
			return 0;
		}
		int queued;
		await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_activity_refresh_queue (wallet, reason, queued_at_utc)\nSELECT missing.wallet, 'missing_participant_details', now()\nFROM (\n    SELECT activity.wallet\n    FROM polymarket_onchain_wallet_activity activity\n    WHERE NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_participant_details participant\n        WHERE lower(participant.wallet) = lower(activity.wallet)\n    )\n      AND NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_activity_refresh_queue queued_wallet\n        WHERE lower(queued_wallet.wallet) = lower(activity.wallet)\n    )\n    ORDER BY activity.wallet\n    LIMIT @WalletLimit\n) missing\nON CONFLICT (wallet) DO NOTHING;"))
		{
			command.Transaction = transaction;
			command.CommandTimeout = 300;
			command.Parameters.AddWithValue("WalletLimit", walletLimit);
			queued = await command.ExecuteNonQueryAsync(cancellationToken);
		}
		int queueRemaining = await CountPolymarketOnChainWalletActivityRefreshQueueAsync(connection, transaction, cancellationToken);
		await UpsertBotSettingAsync(connection, transaction, "onchain_participant_details_initial_backfill_complete", (queued == 0 && queueRemaining == 0) ? "true" : "false", cancellationToken);
		return queued;
	}

	private static async Task<bool> IsPolymarketOnChainParticipantDetailsEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_participant_details LIMIT 1);"))
		{
			command.Transaction = transaction;
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool empty = default(bool);
			int num;
			if (result is bool)
			{
				empty = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (empty ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task UpsertPolymarketOnChainParticipantDetailsForWalletsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string walletSourceTable, CancellationToken cancellationToken)
	{
		if (1 == 0)
		{
		}
		string text = walletSourceTable switch
		{
			"temp_wallet_activity_refresh_wallets" => "SELECT wallet FROM temp_wallet_activity_refresh_wallets",
			"temp_position_refresh_wallets" => "SELECT wallet FROM temp_position_refresh_wallets",
			"temp_wallet_performance_refresh_wallets" => "SELECT wallet FROM temp_wallet_performance_refresh_wallets",
			_ => throw new ArgumentOutOfRangeException("walletSourceTable", walletSourceTable, "Unsupported wallet source table."),
		};
		if (1 == 0)
		{
		}
		string walletSourceSql = text;
		string sql = $"DELETE FROM polymarket_onchain_participant_details\nWHERE wallet IN ({walletSourceSql});\n\nINSERT INTO polymarket_onchain_participant_details (\n    wallet,\n    executions,\n    buy_executions,\n    sell_executions,\n    markets_traded,\n    volume_usd,\n    average_trade_usd,\n    fees_usd,\n    activity_score,\n    positions_count,\n    open_positions,\n    flat_positions,\n    resolved_positions,\n    profitable_resolved_positions,\n    losing_resolved_positions,\n    open_exposure_usd,\n    resolved_cost_usd,\n    resolved_pnl_usd,\n    resolved_roi_pct,\n    win_rate_pct,\n    average_position_size_usd,\n    score,\n    sample_quality,\n    first_trade_utc,\n    last_trade_utc,\n    activity_refreshed_at_utc,\n    performance_refreshed_at_utc,\n    refreshed_at_utc\n)\nWITH position_stats AS (\n    SELECT\n        position.wallet,\n        COUNT(*)::integer AS positions_count,\n        COUNT(*) FILTER (WHERE position.position_status = 'Open')::integer AS open_positions,\n        COUNT(*) FILTER (WHERE position.position_status = 'Flat')::integer AS flat_positions,\n        COUNT(*) FILTER (WHERE position.position_status = 'Resolved')::integer AS resolved_positions,\n        COUNT(*) FILTER (WHERE position.position_status = 'Resolved' AND COALESCE(position.resolved_pnl_usd, 0) > 0)::integer AS profitable_resolved_positions,\n        COUNT(*) FILTER (WHERE position.position_status = 'Resolved' AND COALESCE(position.resolved_pnl_usd, 0) < 0)::integer AS losing_resolved_positions,\n        COALESCE(SUM(abs(position.net_cost_usd)) FILTER (WHERE position.position_status = 'Open'), 0)::numeric AS open_exposure_usd,\n        COALESCE(SUM(abs(position.net_cost_usd)) FILTER (WHERE position.position_status = 'Resolved' AND position.resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_cost_usd,\n        COALESCE(SUM(position.resolved_pnl_usd) FILTER (WHERE position.resolved_pnl_usd IS NOT NULL), 0)::numeric AS resolved_pnl_usd\n    FROM polymarket_onchain_wallet_positions position\n    WHERE position.wallet IN ({walletSourceSql})\n    GROUP BY position.wallet\n)\nSELECT\n    activity.wallet,\n    activity.executions,\n    activity.buy_executions,\n    activity.sell_executions,\n    activity.markets_traded,\n    activity.volume_usd,\n    activity.average_trade_usd,\n    activity.fees_usd,\n    activity.activity_score,\n    COALESCE(performance.positions_count, position_stats.positions_count, 0),\n    COALESCE(performance.open_positions, position_stats.open_positions, 0),\n    COALESCE(performance.flat_positions, position_stats.flat_positions, 0),\n    COALESCE(performance.resolved_positions, position_stats.resolved_positions, 0),\n    COALESCE(performance.profitable_resolved_positions, position_stats.profitable_resolved_positions, 0),\n    COALESCE(performance.losing_resolved_positions, position_stats.losing_resolved_positions, 0),\n    COALESCE(performance.open_exposure_usd, position_stats.open_exposure_usd, 0),\n    COALESCE(performance.resolved_cost_usd, position_stats.resolved_cost_usd, 0),\n    COALESCE(performance.resolved_pnl_usd, position_stats.resolved_pnl_usd, 0),\n    COALESCE(performance.resolved_roi_pct, 0),\n    COALESCE(performance.win_rate_pct, 0),\n    COALESCE(performance.average_position_size_usd, 0),\n    COALESCE(performance.score, activity.activity_score),\n    COALESCE(performance.sample_quality, 'ActivityOnly'),\n    activity.first_trade_utc,\n    activity.last_trade_utc,\n    activity.refreshed_at_utc,\n    performance.refreshed_at_utc,\n    now()\nFROM polymarket_onchain_wallet_activity activity\nLEFT JOIN polymarket_onchain_wallet_performance performance\n       ON lower(performance.wallet) = lower(activity.wallet)\nLEFT JOIN position_stats\n       ON lower(position_stats.wallet) = lower(activity.wallet)\nWHERE activity.wallet IN ({walletSourceSql})\nON CONFLICT (wallet) DO UPDATE SET\n    executions = excluded.executions,\n    buy_executions = excluded.buy_executions,\n    sell_executions = excluded.sell_executions,\n    markets_traded = excluded.markets_traded,\n    volume_usd = excluded.volume_usd,\n    average_trade_usd = excluded.average_trade_usd,\n    fees_usd = excluded.fees_usd,\n    activity_score = excluded.activity_score,\n    positions_count = excluded.positions_count,\n    open_positions = excluded.open_positions,\n    flat_positions = excluded.flat_positions,\n    resolved_positions = excluded.resolved_positions,\n    profitable_resolved_positions = excluded.profitable_resolved_positions,\n    losing_resolved_positions = excluded.losing_resolved_positions,\n    open_exposure_usd = excluded.open_exposure_usd,\n    resolved_cost_usd = excluded.resolved_cost_usd,\n    resolved_pnl_usd = excluded.resolved_pnl_usd,\n    resolved_roi_pct = excluded.resolved_roi_pct,\n    win_rate_pct = excluded.win_rate_pct,\n    average_position_size_usd = excluded.average_position_size_usd,\n    score = excluded.score,\n    sample_quality = excluded.sample_quality,\n    first_trade_utc = excluded.first_trade_utc,\n    last_trade_utc = excluded.last_trade_utc,\n    activity_refreshed_at_utc = excluded.activity_refreshed_at_utc,\n    performance_refreshed_at_utc = excluded.performance_refreshed_at_utc,\n    refreshed_at_utc = excluded.refreshed_at_utc;";
		await using NpgsqlCommand command = CreateCommand(connection, sql);
		command.Transaction = transaction;
		command.CommandTimeout = 300;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainWalletPerformanceRefreshForPositionTokensAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string reason, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_performance_refresh_queue (wallet, reason, queued_at_utc)\nSELECT DISTINCT wallet, @Reason, now()\nFROM polymarket_onchain_wallet_positions\nWHERE token_id IN (SELECT token_id FROM temp_position_refresh_tokens)\nON CONFLICT (wallet) DO NOTHING;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task QueuePolymarketOnChainWalletCategoryPerformanceRefreshForPositionPairsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string reason, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_category_performance_refresh_queue (wallet, category, reason, queued_at_utc)\nSELECT wallet, category, @Reason, now()\nFROM temp_wallet_category_performance_refresh_pairs\nON CONFLICT (wallet, category) DO NOTHING;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Reason", reason);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<int> SeedMissingPolymarketOnChainWalletPerformanceRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int walletLimit, CancellationToken cancellationToken)
	{
		string initialBackfillComplete = await GetBotSettingAsync(connection, transaction, "onchain_wallet_performance_initial_backfill_complete", cancellationToken);
		bool performanceEmpty = await IsPolymarketOnChainWalletPerformanceEmptyAsync(connection, transaction, cancellationToken);
		if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !performanceEmpty)
		{
			return 0;
		}
		int queued;
		await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_performance_refresh_queue (wallet, reason, queued_at_utc)\nSELECT missing.wallet, 'missing_performance', now()\nFROM (\n    SELECT DISTINCT position.wallet\n    FROM polymarket_onchain_wallet_positions position\n    WHERE NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_performance performance\n        WHERE performance.wallet = position.wallet\n    )\n    ORDER BY position.wallet\n    LIMIT @WalletLimit\n) missing\nON CONFLICT (wallet) DO NOTHING;"))
		{
			command.Transaction = transaction;
			command.CommandTimeout = 300;
			command.Parameters.AddWithValue("WalletLimit", walletLimit);
			queued = await command.ExecuteNonQueryAsync(cancellationToken);
		}
		await UpsertBotSettingAsync(connection, transaction, "onchain_wallet_performance_initial_backfill_complete", (queued == 0) ? "true" : "false", cancellationToken);
		return queued;
	}

	private static async Task<int> SeedMissingPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int pairLimit, CancellationToken cancellationToken)
	{
		string initialBackfillComplete = await GetBotSettingAsync(connection, transaction, "onchain_wallet_category_performance_initial_backfill_complete", cancellationToken);
		bool categoryPerformanceEmpty = await IsPolymarketOnChainWalletCategoryPerformanceEmptyAsync(connection, transaction, cancellationToken);
		if (string.Equals(initialBackfillComplete, "true", StringComparison.OrdinalIgnoreCase) && !categoryPerformanceEmpty)
		{
			return 0;
		}
		int queued;
		await using (NpgsqlCommand command = CreateCommand(connection, "INSERT INTO polymarket_onchain_wallet_category_performance_refresh_queue (wallet, category, reason, queued_at_utc)\nSELECT missing.wallet, missing.category, 'missing_category_performance', now()\nFROM (\n    SELECT DISTINCT position.wallet, COALESCE(NULLIF(position.category, ''), 'unknown') AS category\n    FROM polymarket_onchain_wallet_positions position\n    WHERE NOT EXISTS (\n        SELECT 1\n        FROM polymarket_onchain_wallet_category_performance performance\n        WHERE performance.wallet = position.wallet\n          AND performance.category = COALESCE(NULLIF(position.category, ''), 'unknown')\n    )\n    ORDER BY category, position.wallet\n    LIMIT @PairLimit\n) missing\nON CONFLICT (wallet, category) DO NOTHING;"))
		{
			command.Transaction = transaction;
			command.CommandTimeout = 300;
			command.Parameters.AddWithValue("PairLimit", pairLimit);
			queued = await command.ExecuteNonQueryAsync(cancellationToken);
		}
		int queueRemaining = await CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(connection, transaction, cancellationToken);
		await UpsertBotSettingAsync(connection, transaction, "onchain_wallet_category_performance_initial_backfill_complete", (queued == 0 && queueRemaining == 0) ? "true" : "false", cancellationToken);
		return queued;
	}

	private static async Task<bool> IsPolymarketOnChainWalletPerformanceEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_performance LIMIT 1);"))
		{
			command.Transaction = transaction;
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool empty = default(bool);
			int num;
			if (result is bool)
			{
				empty = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (empty ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task<bool> IsPolymarketOnChainWalletCategoryPerformanceEmptyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		bool result2;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT NOT EXISTS (SELECT 1 FROM polymarket_onchain_wallet_category_performance LIMIT 1);"))
		{
			command.Transaction = transaction;
			object result = await command.ExecuteScalarAsync(cancellationToken);
			bool empty = default(bool);
			int num;
			if (result is bool)
			{
				empty = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			result2 = (byte)((uint)num & (empty ? 1u : 0u)) != 0;
		}
		return result2;
	}

	private static async Task<int> CountTempWalletPerformanceRefreshWalletsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_performance_refresh_wallets;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountTempWalletCategoryPerformanceRefreshPairsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM temp_wallet_category_performance_refresh_pairs;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountPolymarketOnChainWalletPerformanceRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_performance_refresh_queue;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountPolymarketOnChainWalletCategoryPerformanceRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_wallet_category_performance_refresh_queue;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<int> CountPolymarketOnChainSignalCandidateRefreshQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
	{
		int result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT count(*) FROM polymarket_onchain_signal_candidate_refresh_queue;"))
		{
			command.Transaction = transaction;
			result = ((await command.ExecuteScalarAsync(cancellationToken) is long count) ? checked((int)count) : 0);
		}
		return result;
	}

	private static async Task<string?> GetBotSettingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
	{
		string result;
		await using (NpgsqlCommand command = CreateCommand(connection, "SELECT value FROM bot_settings WHERE key = @Key;"))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("Key", key);
			result = ((await command.ExecuteScalarAsync(cancellationToken) is string value) ? value : null);
		}
		return result;
	}

	private static async Task UpsertBotSettingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key, string value, CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO bot_settings (key, value, updated_at_utc)\nVALUES (@Key, @Value, now())\nON CONFLICT (key) DO UPDATE SET\n    value = excluded.value,\n    updated_at_utc = excluded.updated_at_utc;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Key", key);
		command.Parameters.AddWithValue("Value", value);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static void AddPolymarketOnChainSignalCandidateParameters(NpgsqlCommand command, PolymarketOnChainSignalCandidate candidate)
	{
		command.Parameters.AddWithValue("Id", candidate.Id);
		command.Parameters.AddWithValue("SourceFillId", candidate.SourceFillId);
		command.Parameters.AddWithValue("ContractName", candidate.ContractName);
		command.Parameters.AddWithValue("ContractAddress", candidate.ContractAddress);
		command.Parameters.AddWithValue("ExchangeVersion", candidate.ExchangeVersion);
		command.Parameters.AddWithValue("BlockNumber", candidate.BlockNumber);
		command.Parameters.AddWithValue("BlockTimestampUtc", UtcDateTime(candidate.BlockTimestampUtc));
		command.Parameters.AddWithValue("TransactionHash", candidate.TransactionHash);
		command.Parameters.AddWithValue("LogIndex", candidate.LogIndex);
		command.Parameters.AddWithValue("OrderHash", candidate.OrderHash);
		command.Parameters.AddWithValue("ParticipantRole", candidate.ParticipantRole.ToString());
		command.Parameters.AddWithValue("Wallet", candidate.Wallet);
		command.Parameters.AddWithValue("Counterparty", candidate.Counterparty);
		command.Parameters.AddWithValue("Side", candidate.Side.ToString());
		command.Parameters.AddWithValue("TokenId", candidate.TokenId);
		command.Parameters.AddWithValue("ConditionId", candidate.ConditionId);
		command.Parameters.AddWithValue("MarketId", candidate.MarketId);
		command.Parameters.AddWithValue("MarketSlug", candidate.MarketSlug);
		command.Parameters.AddWithValue("MarketTitle", candidate.MarketTitle);
		command.Parameters.AddWithValue("Outcome", candidate.Outcome);
		command.Parameters.AddWithValue("Category", ((object)candidate.Category) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("LookupSucceeded", candidate.LookupSucceeded);
		command.Parameters.AddWithValue("MarketActive", candidate.MarketActive);
		command.Parameters.AddWithValue("MarketClosed", candidate.MarketClosed);
		command.Parameters.AddWithValue("MarketArchived", candidate.MarketArchived);
		command.Parameters.AddWithValue("MarketResolved", candidate.MarketResolved);
		command.Parameters.AddWithValue("WinningOutcome", ((object)candidate.WinningOutcome) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Price", candidate.Price);
		command.Parameters.AddWithValue("SizeShares", candidate.SizeShares);
		command.Parameters.AddWithValue("NotionalUsd", candidate.NotionalUsd);
		command.Parameters.AddWithValue("FeeAmount", candidate.FeeAmount);
		command.Parameters.AddWithValue("FeeAssetId", candidate.FeeAssetId);
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("LeaderPositionsCount", NpgsqlDbType.Integer);
		int? leaderPositionsCount = candidate.LeaderPositionsCount;
		object value;
		if (leaderPositionsCount.HasValue)
		{
			int leaderPositionsCount2 = leaderPositionsCount.GetValueOrDefault();
			value = leaderPositionsCount2;
		}
		else
		{
			value = DBNull.Value;
		}
		npgsqlParameter.Value = value;
		NpgsqlParameter npgsqlParameter2 = command.Parameters.Add("LeaderResolvedPositions", NpgsqlDbType.Integer);
		leaderPositionsCount = candidate.LeaderResolvedPositions;
		object value2;
		if (leaderPositionsCount.HasValue)
		{
			int leaderResolvedPositions = leaderPositionsCount.GetValueOrDefault();
			value2 = leaderResolvedPositions;
		}
		else
		{
			value2 = DBNull.Value;
		}
		npgsqlParameter2.Value = value2;
		NpgsqlParameter npgsqlParameter3 = command.Parameters.Add("LeaderMarketsTraded", NpgsqlDbType.Integer);
		leaderPositionsCount = candidate.LeaderMarketsTraded;
		object value3;
		if (leaderPositionsCount.HasValue)
		{
			int leaderMarketsTraded = leaderPositionsCount.GetValueOrDefault();
			value3 = leaderMarketsTraded;
		}
		else
		{
			value3 = DBNull.Value;
		}
		npgsqlParameter3.Value = value3;
		NpgsqlParameter npgsqlParameter4 = command.Parameters.Add("LeaderVolumeUsd", NpgsqlDbType.Numeric);
		decimal? leaderVolumeUsd = candidate.LeaderVolumeUsd;
		object value4;
		if (leaderVolumeUsd.HasValue)
		{
			decimal leaderVolumeUsd2 = leaderVolumeUsd.GetValueOrDefault();
			value4 = leaderVolumeUsd2;
		}
		else
		{
			value4 = DBNull.Value;
		}
		npgsqlParameter4.Value = value4;
		NpgsqlParameter npgsqlParameter5 = command.Parameters.Add("LeaderResolvedPnlUsd", NpgsqlDbType.Numeric);
		leaderVolumeUsd = candidate.LeaderResolvedPnlUsd;
		object value5;
		if (leaderVolumeUsd.HasValue)
		{
			decimal leaderResolvedPnlUsd = leaderVolumeUsd.GetValueOrDefault();
			value5 = leaderResolvedPnlUsd;
		}
		else
		{
			value5 = DBNull.Value;
		}
		npgsqlParameter5.Value = value5;
		NpgsqlParameter npgsqlParameter6 = command.Parameters.Add("LeaderResolvedRoiPct", NpgsqlDbType.Numeric);
		leaderVolumeUsd = candidate.LeaderResolvedRoiPct;
		object value6;
		if (leaderVolumeUsd.HasValue)
		{
			decimal leaderResolvedRoiPct = leaderVolumeUsd.GetValueOrDefault();
			value6 = leaderResolvedRoiPct;
		}
		else
		{
			value6 = DBNull.Value;
		}
		npgsqlParameter6.Value = value6;
		NpgsqlParameter npgsqlParameter7 = command.Parameters.Add("LeaderWinRatePct", NpgsqlDbType.Numeric);
		leaderVolumeUsd = candidate.LeaderWinRatePct;
		object value7;
		if (leaderVolumeUsd.HasValue)
		{
			decimal leaderWinRatePct = leaderVolumeUsd.GetValueOrDefault();
			value7 = leaderWinRatePct;
		}
		else
		{
			value7 = DBNull.Value;
		}
		npgsqlParameter7.Value = value7;
		NpgsqlParameter npgsqlParameter8 = command.Parameters.Add("LeaderCategoryScore", NpgsqlDbType.Numeric);
		leaderVolumeUsd = candidate.LeaderCategoryScore;
		object value8;
		if (leaderVolumeUsd.HasValue)
		{
			decimal leaderCategoryScore = leaderVolumeUsd.GetValueOrDefault();
			value8 = leaderCategoryScore;
		}
		else
		{
			value8 = DBNull.Value;
		}
		npgsqlParameter8.Value = value8;
		command.Parameters.AddWithValue("LeaderSampleQuality", ((object)candidate.LeaderSampleQuality) ?? ((object)DBNull.Value));
		NpgsqlParameter npgsqlParameter9 = command.Parameters.Add("LeaderPerformanceRefreshedAtUtc", NpgsqlDbType.TimestampTz);
		DateTimeOffset? leaderPerformanceRefreshedAtUtc = candidate.LeaderPerformanceRefreshedAtUtc;
		object value9;
		if (leaderPerformanceRefreshedAtUtc.HasValue)
		{
			DateTimeOffset refreshedAt = leaderPerformanceRefreshedAtUtc.GetValueOrDefault();
			value9 = UtcDateTime(refreshedAt);
		}
		else
		{
			value9 = DBNull.Value;
		}
		npgsqlParameter9.Value = value9;
		command.Parameters.AddWithValue("DecisionStatus", candidate.DecisionStatus);
		command.Parameters.AddWithValue("DecisionCode", candidate.DecisionCode);
		command.Parameters.AddWithValue("CandidateScore", candidate.CandidateScore);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(candidate.CreatedAtUtc));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(candidate.UpdatedAtUtc));
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
		command.Parameters.AddWithValue("Category", ((object)metadata.Category) ?? ((object)DBNull.Value));
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? endDateUtc = metadata.EndDateUtc;
		object value;
		if (endDateUtc.HasValue)
		{
			DateTimeOffset endDate = endDateUtc.GetValueOrDefault();
			value = UtcDateTime(endDate);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("EndDateUtc", value);
		command.Parameters.AddWithValue("Active", metadata.Active);
		command.Parameters.AddWithValue("Closed", metadata.Closed);
		command.Parameters.AddWithValue("Archived", metadata.Archived);
		command.Parameters.AddWithValue("Resolved", metadata.Resolved);
		command.Parameters.AddWithValue("WinningOutcome", ((object)metadata.WinningOutcome) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ClobTokenIdsJson", JsonSerializer.Serialize(metadata.ClobTokenIds));
		command.Parameters.AddWithValue("OutcomesJson", JsonSerializer.Serialize(metadata.Outcomes));
		command.Parameters.AddWithValue("LookupSucceeded", metadata.LookupSucceeded);
		command.Parameters.AddWithValue("LookupError", ((object)metadata.LookupError) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(metadata.RawJson) ? "{}" : metadata.RawJson);
		command.Parameters.AddWithValue("LastRefreshedUtc", UtcDateTime(metadata.LastRefreshedUtc));
	}

	private static PolymarketOnChainTokenMetadata ReadPolymarketOnChainTokenMetadata(NpgsqlDataReader reader, int offset = 0)
	{
		return new PolymarketOnChainTokenMetadata(reader.GetString(offset), reader.GetString(offset + 1), reader.GetString(offset + 2), reader.GetString(offset + 3), reader.GetString(offset + 4), reader.GetString(offset + 5), reader.GetInt32(offset + 6), reader.IsDBNull(offset + 7) ? null : reader.GetString(offset + 7), reader.IsDBNull(offset + 8) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(offset + 8))), reader.GetBoolean(offset + 9), reader.GetBoolean(offset + 10), reader.GetBoolean(offset + 11), reader.GetBoolean(offset + 12), reader.IsDBNull(offset + 13) ? null : reader.GetString(offset + 13), ReadJsonStringArray(reader, offset + 14), ReadJsonStringArray(reader, offset + 15), reader.GetBoolean(offset + 16), reader.IsDBNull(offset + 17) ? null : reader.GetString(offset + 17), reader.GetString(offset + 18), DateTimeOffsetFromUtc(reader.GetDateTime(offset + 19)));
	}

	private static PolymarketOnChainTokenMetadata? ReadNullablePolymarketOnChainTokenMetadata(NpgsqlDataReader reader, int offset)
	{
		return reader.IsDBNull(offset) ? null : ReadPolymarketOnChainTokenMetadata(reader, offset);
	}

	private static IReadOnlyList<string> ReadJsonStringArray(NpgsqlDataReader reader, int ordinal)
	{
		if (reader.IsDBNull(ordinal))
		{
			return Array.Empty<string>();
		}
		return JsonSerializer.Deserialize<string[]>(reader.GetString(ordinal)) ?? Array.Empty<string>();
	}

	private static PolymarketOnChainFill ReadPolymarketOnChainFill(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainFill(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), DateTimeOffsetFromUtc(reader.GetDateTime(5)), reader.GetString(6), reader.GetInt64(7), reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11), Enum.Parse<TradeSide>(reader.GetString(12)), reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetString(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetString(23), reader.GetDecimal(24), reader.GetString(25), reader.IsDBNull(26) ? null : reader.GetString(26), reader.IsDBNull(27) ? null : reader.GetString(27), DateTimeOffsetFromUtc(reader.GetDateTime(28)));
	}

	private static PolymarketOnChainSignalCandidateSource ReadPolymarketOnChainSignalCandidateSource(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainSignalCandidateSource(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), DateTimeOffsetFromUtc(reader.GetDateTime(5)), reader.GetString(6), reader.GetInt64(7), reader.GetString(8), Enum.Parse<OnChainParticipantRole>(reader.GetString(9)), reader.GetString(10), reader.GetString(11), Enum.Parse<TradeSide>(reader.GetString(12)), reader.GetString(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.GetString(18), DateTimeOffsetFromUtc(reader.GetDateTime(19)), ReadNullablePolymarketOnChainTokenMetadata(reader, 20), ReadNullablePolymarketOnChainWalletCategoryPerformance(reader, 40));
	}

	private static PolymarketOnChainSignalCandidate ReadPolymarketOnChainSignalCandidate(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainSignalCandidate(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), DateTimeOffsetFromUtc(reader.GetDateTime(6)), reader.GetString(7), reader.GetInt64(8), reader.GetString(9), Enum.Parse<OnChainParticipantRole>(reader.GetString(10)), reader.GetString(11), reader.GetString(12), Enum.Parse<TradeSide>(reader.GetString(13)), reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetString(17), reader.GetString(18), reader.GetString(19), reader.IsDBNull(20) ? null : reader.GetString(20), reader.GetBoolean(21), reader.GetBoolean(22), reader.GetBoolean(23), reader.GetBoolean(24), reader.GetBoolean(25), reader.IsDBNull(26) ? null : reader.GetString(26), reader.GetDecimal(27), reader.GetDecimal(28), reader.GetDecimal(29), reader.GetDecimal(30), reader.GetString(31), reader.IsDBNull(32) ? ((int?)null) : new int?(reader.GetInt32(32)), reader.IsDBNull(33) ? ((int?)null) : new int?(reader.GetInt32(33)), reader.IsDBNull(34) ? ((int?)null) : new int?(reader.GetInt32(34)), reader.IsDBNull(35) ? ((decimal?)null) : new decimal?(reader.GetDecimal(35)), reader.IsDBNull(36) ? ((decimal?)null) : new decimal?(reader.GetDecimal(36)), reader.IsDBNull(37) ? ((decimal?)null) : new decimal?(reader.GetDecimal(37)), reader.IsDBNull(38) ? ((decimal?)null) : new decimal?(reader.GetDecimal(38)), reader.IsDBNull(39) ? ((decimal?)null) : new decimal?(reader.GetDecimal(39)), reader.IsDBNull(40) ? null : reader.GetString(40), reader.IsDBNull(41) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(41))), reader.GetString(42), reader.GetString(43), reader.GetDecimal(44), DateTimeOffsetFromUtc(reader.GetDateTime(45)), DateTimeOffsetFromUtc(reader.GetDateTime(46)));
	}

	private static OnChainPaperSignalCandidate ReadOnChainPaperSignalCandidate(NpgsqlDataReader reader)
	{
		return new OnChainPaperSignalCandidate(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetInt64(4),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			reader.GetString(6),
			reader.GetInt64(7),
			reader.GetString(8),
			Enum.Parse<OnChainParticipantRole>(reader.GetString(9)),
			reader.GetString(10),
			reader.GetString(11),
			Enum.Parse<TradeSide>(reader.GetString(12)),
			reader.GetString(13),
			reader.GetDecimal(14),
			reader.GetDecimal(15),
			reader.GetDecimal(16),
			reader.GetString(17),
			reader.GetString(18),
			reader.GetString(19),
			reader.GetString(20),
			reader.GetString(21),
			reader.IsDBNull(22) ? null : reader.GetString(22),
			reader.GetBoolean(23),
			reader.GetBoolean(24),
			reader.GetBoolean(25),
			reader.GetBoolean(26),
			reader.GetBoolean(27),
			reader.GetBoolean(28),
			reader.GetBoolean(29),
			reader.IsDBNull(30) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(30))),
			reader.IsDBNull(31) ? null : reader.GetString(31),
			reader.IsDBNull(32) ? ((bool?)null) : new bool?(reader.GetBoolean(32)),
			reader.IsDBNull(33) ? ((int?)null) : new int?(reader.GetInt32(33)),
			reader.IsDBNull(34) ? null : reader.GetString(34),
			reader.IsDBNull(35) ? ((decimal?)null) : new decimal?(reader.GetDecimal(35)),
			reader.IsDBNull(36) ? ((decimal?)null) : new decimal?(reader.GetDecimal(36)),
			reader.IsDBNull(37) ? ((decimal?)null) : new decimal?(reader.GetDecimal(37)),
			reader.GetInt32(38),
			reader.GetInt32(39),
			reader.GetDecimal(40),
			reader.IsDBNull(41) ? ((decimal?)null) : new decimal?(reader.GetDecimal(41)),
			reader.IsDBNull(42) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(42))));
	}

	private static PolymarketOnChainWalletExecution ReadPolymarketOnChainWalletExecution(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainWalletExecution(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), DateTimeOffsetFromUtc(reader.GetDateTime(4)), reader.GetString(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetString(8), Enum.Parse<TradeSide>(reader.GetString(9)), reader.GetString(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17), DateTimeOffsetFromUtc(reader.GetDateTime(18)));
	}

	private static PolymarketOnChainWalletPosition ReadPolymarketOnChainWalletPosition(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainWalletPosition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetBoolean(8), reader.GetBoolean(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetDecimal(23), reader.IsDBNull(24) ? ((decimal?)null) : new decimal?(reader.GetDecimal(24)), reader.GetString(25), DateTimeOffsetFromUtc(reader.GetDateTime(26)), DateTimeOffsetFromUtc(reader.GetDateTime(27)));
	}

	private static PolymarketOnChainWalletPerformance ReadPolymarketOnChainWalletPerformance(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainWalletPerformance(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetString(17), DateTimeOffsetFromUtc(reader.GetDateTime(18)), DateTimeOffsetFromUtc(reader.GetDateTime(19)), DateTimeOffsetFromUtc(reader.GetDateTime(20)));
	}

	private static PolymarketOnChainWalletCategoryPerformance ReadPolymarketOnChainWalletCategoryPerformance(NpgsqlDataReader reader, int offset = 0)
	{
		return new PolymarketOnChainWalletCategoryPerformance(reader.GetString(offset), reader.GetString(offset + 1), reader.GetInt32(offset + 2), reader.GetInt32(offset + 3), reader.GetInt32(offset + 4), reader.GetInt32(offset + 5), reader.GetInt32(offset + 6), reader.GetInt32(offset + 7), reader.GetInt32(offset + 8), reader.GetDecimal(offset + 9), reader.GetDecimal(offset + 10), reader.GetDecimal(offset + 11), reader.GetDecimal(offset + 12), reader.GetDecimal(offset + 13), reader.GetDecimal(offset + 14), reader.GetDecimal(offset + 15), reader.GetDecimal(offset + 16), reader.GetDecimal(offset + 17), reader.GetString(offset + 18), DateTimeOffsetFromUtc(reader.GetDateTime(offset + 19)), DateTimeOffsetFromUtc(reader.GetDateTime(offset + 20)), DateTimeOffsetFromUtc(reader.GetDateTime(offset + 21)));
	}

	private static PolymarketOnChainWalletCategoryPerformance? ReadNullablePolymarketOnChainWalletCategoryPerformance(NpgsqlDataReader reader, int offset)
	{
		return reader.IsDBNull(offset) ? null : ReadPolymarketOnChainWalletCategoryPerformance(reader, offset);
	}

	private static PolymarketOnChainTradeDetails ReadPolymarketOnChainTradeDetails(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainTradeDetails(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), DateTimeOffsetFromUtc(reader.GetDateTime(4)), reader.GetString(5), reader.GetInt64(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), Enum.Parse<TradeSide>(reader.GetString(10)), Enum.Parse<TradeSide>(reader.GetString(11)), reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.GetString(15), reader.GetString(16), reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetDecimal(22), reader.GetString(23), reader.IsDBNull(24) ? null : reader.GetString(24), reader.IsDBNull(25) ? null : reader.GetString(25), reader.GetString(26), reader.GetString(27), reader.GetString(28), reader.GetString(29), reader.GetString(30), reader.IsDBNull(31) ? null : reader.GetString(31), reader.GetBoolean(32), reader.GetBoolean(33), reader.GetBoolean(34), reader.GetBoolean(35), reader.GetBoolean(36), reader.IsDBNull(37) ? null : reader.GetString(37), DateTimeOffsetFromUtc(reader.GetDateTime(38)));
	}

	private static PolymarketOnChainParticipantDetails ReadPolymarketOnChainParticipantDetails(NpgsqlDataReader reader)
	{
		return new PolymarketOnChainParticipantDetails(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetDecimal(15), reader.GetDecimal(16), reader.GetDecimal(17), reader.GetDecimal(18), reader.GetDecimal(19), reader.GetDecimal(20), reader.GetDecimal(21), reader.GetString(22), DateTimeOffsetFromUtc(reader.GetDateTime(23)), DateTimeOffsetFromUtc(reader.GetDateTime(24)), DateTimeOffsetFromUtc(reader.GetDateTime(25)), reader.IsDBNull(26) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(26))));
	}

	private static PolymarketWebSocketTradeTick ReadPolymarketWebSocketTradeTick(NpgsqlDataReader reader)
	{
		return new PolymarketWebSocketTradeTick(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), Enum.Parse<TradeSide>(reader.GetString(4)), reader.IsDBNull(5) ? ((decimal?)null) : new decimal?(reader.GetDecimal(5)), reader.IsDBNull(6) ? ((decimal?)null) : new decimal?(reader.GetDecimal(6)), DateTimeOffsetFromUtc(reader.GetDateTime(7)), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetBoolean(9), (TradeTickTraderMatchStatus)reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetString(11), DateTimeOffsetFromUtc(reader.GetDateTime(12)), reader.IsDBNull(13) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(13))), reader.GetInt32(14), reader.IsDBNull(15) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(15))), reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17), reader.IsDBNull(18) ? null : reader.GetString(18), reader.GetString(19), DateTimeOffsetFromUtc(reader.GetDateTime(20)));
	}

	private static void AddPolymarketWebSocketTradeTickParameters(NpgsqlCommand command, PolymarketWebSocketTradeTick tradeTick)
	{
		command.Parameters.AddWithValue("Id", tradeTick.Id);
		command.Parameters.AddWithValue("DedupKey", tradeTick.DedupKey);
		command.Parameters.AddWithValue("AssetId", tradeTick.AssetId);
		command.Parameters.AddWithValue("ConditionId", ((object)tradeTick.ConditionId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Side", tradeTick.Side.ToString());
		NpgsqlParameterCollection parameters = command.Parameters;
		decimal? price = tradeTick.Price;
		object value;
		if (price.HasValue)
		{
			decimal price2 = price.GetValueOrDefault();
			value = price2;
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("Price", value);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		price = tradeTick.Size;
		object value2;
		if (price.HasValue)
		{
			decimal size = price.GetValueOrDefault();
			value2 = size;
		}
		else
		{
			value2 = DBNull.Value;
		}
		parameters2.AddWithValue("Size", value2);
		command.Parameters.AddWithValue("TradeTimestampUtc", UtcDateTime(tradeTick.TradeTimestampUtc));
		command.Parameters.AddWithValue("TransactionHash", ((object)tradeTick.TransactionHash) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("TransactionHashPresent", tradeTick.TransactionHashPresent);
		command.Parameters.AddWithValue("TraderMatchStatus", (int)tradeTick.TraderMatchStatus);
		command.Parameters.AddWithValue("TraderWallet", ((object)tradeTick.TraderWallet) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ReceivedAtUtc", UtcDateTime(tradeTick.ReceivedAtUtc));
		NpgsqlParameterCollection parameters3 = command.Parameters;
		DateTimeOffset? matchedAtUtc = tradeTick.MatchedAtUtc;
		object value3;
		if (matchedAtUtc.HasValue)
		{
			DateTimeOffset matchedAt = matchedAtUtc.GetValueOrDefault();
			value3 = UtcDateTime(matchedAt);
		}
		else
		{
			value3 = DBNull.Value;
		}
		parameters3.AddWithValue("MatchedAtUtc", value3);
		command.Parameters.AddWithValue("MatchAttempts", tradeTick.MatchAttempts);
		NpgsqlParameterCollection parameters4 = command.Parameters;
		matchedAtUtc = tradeTick.LastMatchAttemptUtc;
		object value4;
		if (matchedAtUtc.HasValue)
		{
			DateTimeOffset attemptAt = matchedAtUtc.GetValueOrDefault();
			value4 = UtcDateTime(attemptAt);
		}
		else
		{
			value4 = DBNull.Value;
		}
		parameters4.AddWithValue("LastMatchAttemptUtc", value4);
		command.Parameters.AddWithValue("LastMatchError", ((object)tradeTick.LastMatchError) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("MatchedTransactionHash", ((object)tradeTick.MatchedTransactionHash) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("MatchDetails", ((object)tradeTick.MatchDetails) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(tradeTick.RawJson) ? "{}" : tradeTick.RawJson);
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(tradeTick.UpdatedAtUtc));
	}

	private static void AddLiveOrderParameters(NpgsqlCommand command, LiveOrder order)
	{
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("SignalId", order.SignalId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("Status", order.Status.ToString());
		command.Parameters.AddWithValue("OrderId", ((object)order.OrderId) ?? ((object)DBNull.Value));
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
		NpgsqlParameterCollection parameters = command.Parameters;
		DateTimeOffset? submittedAtUtc = order.SubmittedAtUtc;
		object value;
		if (submittedAtUtc.HasValue)
		{
			DateTimeOffset submittedAt = submittedAtUtc.GetValueOrDefault();
			value = UtcDateTime(submittedAt);
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("SubmittedAtUtc", value);
		command.Parameters.AddWithValue("ResponseStatus", order.ResponseStatus);
		command.Parameters.AddWithValue("FilledSize", order.FilledSize);
		command.Parameters.AddWithValue("RemainingSize", order.RemainingSize);
		command.Parameters.AddWithValue("AverageFillPrice", ((object)order.AverageFillPrice) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("FilledNotionalUsd", order.FilledNotionalUsd);
		command.Parameters.AddWithValue("CostBasisUsd", order.CostBasisUsd);
		command.Parameters.AddWithValue("FeeUsd", order.FeeUsd);
		command.Parameters.AddWithValue("CancelStatus", order.CancelStatus);
		command.Parameters.AddWithValue("RawResponseJson", string.IsNullOrWhiteSpace(order.RawResponseJson) ? "{}" : order.RawResponseJson);
		command.Parameters.AddWithValue("ValidationSummary", order.ValidationSummary);
		command.Parameters.AddWithValue("BalanceEffectApplied", order.BalanceEffectApplied);
		command.Parameters.AddWithValue("SettlementValueUsd", ((object)order.SettlementValueUsd) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("RealizedPnlUsd", ((object)order.RealizedPnlUsd) ?? ((object)DBNull.Value));
		NpgsqlParameterCollection settlementParameters = command.Parameters;
		DateTimeOffset? settledAtUtc = order.SettledAtUtc;
		object settlementValue;
		if (settledAtUtc.HasValue)
		{
			DateTimeOffset settledAt = settledAtUtc.GetValueOrDefault();
			settlementValue = UtcDateTime(settledAt);
		}
		else
		{
			settlementValue = DBNull.Value;
		}
		settlementParameters.AddWithValue("SettledAtUtc", settlementValue);
		command.Parameters.AddWithValue("WinningAssetId", ((object)order.WinningAssetId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("WinningOutcome", ((object)order.WinningOutcome) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Won", ((object)order.Won) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("SettlementSource", order.SettlementSource);
		command.Parameters.AddWithValue("CorrelationId", ((object)order.CorrelationId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource ?? string.Empty);
		command.Parameters.AddWithValue("PostOnly", ((object)order.PostOnly) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("PaperOrderId", ((object)order.PaperOrderId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(order.UpdatedAtUtc));
	}

	private static void AddPaperLiveShadowDecisionParameters(NpgsqlCommand command, PaperLiveShadowDecision decision)
	{
		command.Parameters.AddWithValue("CorrelationId", decision.CorrelationId);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(decision.StrategyId));
		command.Parameters.AddWithValue("MarketId", decision.MarketId);
		command.Parameters.AddWithValue("ConditionId", decision.ConditionId);
		command.Parameters.AddWithValue("AssetId", decision.AssetId);
		command.Parameters.AddWithValue("Outcome", decision.Outcome);
		command.Parameters.AddWithValue("Side", decision.Side.ToString());
		command.Parameters.AddWithValue("LimitPrice", decision.LimitPrice);
		command.Parameters.AddWithValue("TargetNotionalUsd", decision.TargetNotionalUsd);
		command.Parameters.AddWithValue("RequestedSizeShares", decision.RequestedSizeShares);
		command.Parameters.AddWithValue("MaxReservedNotionalUsd", decision.MaxReservedNotionalUsd);
		command.Parameters.AddWithValue("OrderType", decision.OrderType);
		command.Parameters.AddWithValue("PostOnly", decision.PostOnly);
		command.Parameters.AddWithValue("OrderBookSnapshotJson", string.IsNullOrWhiteSpace(decision.OrderBookSnapshotJson) ? "{}" : decision.OrderBookSnapshotJson);
		command.Parameters.AddWithValue("QuoteAgeMs", ((object)decision.QuoteAgeMs) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Source", decision.Source);
		command.Parameters.AddWithValue("QuoteReceivedAtUtc", UtcDateTime(decision.QuoteReceivedAtUtc));
		command.Parameters.AddWithValue("DecisionCreatedAtUtc", UtcDateTime(decision.DecisionCreatedAtUtc));
		command.Parameters.AddWithValue("MarketStartUtc", decision.MarketStartUtc.HasValue ? UtcDateTime(decision.MarketStartUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("MarketCloseUtc", decision.MarketCloseUtc.HasValue ? UtcDateTime(decision.MarketCloseUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("SubmitDeadlineUtc", UtcDateTime(decision.SubmitDeadlineUtc));
		command.Parameters.AddWithValue("CancelDeadlineUtc", UtcDateTime(decision.CancelDeadlineUtc));
		command.Parameters.AddWithValue("SignalId", ((object)decision.SignalId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("PaperOrderId", ((object)decision.PaperOrderId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("LiveOrderId", ((object)decision.LiveOrderId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Status", decision.Status);
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(decision.UpdatedAtUtc ?? decision.DecisionCreatedAtUtc));
	}

	private static void AddBtcUsdReferenceCorrelationSampleParameters(NpgsqlCommand command, BtcUsdReferenceCorrelationSample sample)
	{
		command.Parameters.AddWithValue("Id", sample.Id);
		command.Parameters.AddWithValue("BinancePriceUsd", sample.BinancePriceUsd);
		command.Parameters.AddWithValue("BinanceSourceUpdatedAtUtc", UtcDateTime(sample.BinanceSourceUpdatedAtUtc));
		command.Parameters.AddWithValue("BinanceFetchedAtUtc", UtcDateTime(sample.BinanceFetchedAtUtc));
		command.Parameters.AddWithValue("ChainlinkPriceUsd", sample.ChainlinkPriceUsd);
		command.Parameters.AddWithValue("ChainlinkValidAfterUtc", UtcDateTime(sample.ChainlinkValidAfterUtc));
		command.Parameters.AddWithValue("TimeDeltaSeconds", sample.TimeDeltaSeconds);
		command.Parameters.AddWithValue("PriceDiffUsd", sample.PriceDiffUsd);
		command.Parameters.AddWithValue("PriceDiffBps", sample.PriceDiffBps);
		command.Parameters.AddWithValue("ChainlinkFeedId", sample.ChainlinkFeedId);
		command.Parameters.AddWithValue("ChainlinkQueryWindow", sample.ChainlinkQueryWindow);
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(sample.RawJson) ? "{}" : sample.RawJson);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(sample.CreatedAtUtc));
	}

	private static BtcUsdReferenceCorrelationSample ReadBtcUsdReferenceCorrelationSample(NpgsqlDataReader reader)
	{
		return new BtcUsdReferenceCorrelationSample(
			reader.GetGuid(0),
			reader.GetDecimal(1),
			DateTimeOffsetFromUtc(reader.GetDateTime(2)),
			DateTimeOffsetFromUtc(reader.GetDateTime(3)),
			reader.GetDecimal(4),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			reader.GetDecimal(6),
			reader.GetDecimal(7),
			reader.GetDecimal(8),
			reader.GetString(9),
			reader.GetString(10),
			reader.GetString(11),
			DateTimeOffsetFromUtc(reader.GetDateTime(12)));
	}

	private static void AddBtcOrderBookLagDiagnosticEventParameters(NpgsqlCommand command, BtcOrderBookLagDiagnosticEvent diagnosticEvent)
	{
		command.Parameters.AddWithValue("Id", diagnosticEvent.Id);
		command.Parameters.AddWithValue("Source", diagnosticEvent.Source);
		command.Parameters.AddWithValue("EventType", diagnosticEvent.EventType);
		command.Parameters.AddWithValue("AssetId", ((object?)diagnosticEvent.AssetId) ?? DBNull.Value);
		command.Parameters.AddWithValue("ConditionId", ((object?)diagnosticEvent.ConditionId) ?? DBNull.Value);
		command.Parameters.AddWithValue("BinanceSymbol", ((object?)diagnosticEvent.BinanceSymbol) ?? DBNull.Value);
		command.Parameters.AddWithValue("BinancePriceUsd", NullableDecimal(diagnosticEvent.BinancePriceUsd));
		command.Parameters.AddWithValue("BestBid", NullableDecimal(diagnosticEvent.BestBid));
		command.Parameters.AddWithValue("BestBidSize", NullableDecimal(diagnosticEvent.BestBidSize));
		command.Parameters.AddWithValue("BestAsk", NullableDecimal(diagnosticEvent.BestAsk));
		command.Parameters.AddWithValue("BestAskSize", NullableDecimal(diagnosticEvent.BestAskSize));
		command.Parameters.AddWithValue("Mid", NullableDecimal(diagnosticEvent.Mid));
		command.Parameters.AddWithValue("TradePrice", NullableDecimal(diagnosticEvent.TradePrice));
		command.Parameters.AddWithValue("TradeSize", NullableDecimal(diagnosticEvent.TradeSize));
		command.Parameters.AddWithValue("SourceTimestampUtc", NullableDateTime(diagnosticEvent.SourceTimestampUtc));
		command.Parameters.AddWithValue("ReceivedAtUtc", UtcDateTime(diagnosticEvent.ReceivedAtUtc));
		command.Parameters.AddWithValue("LocalLagMs", NullableDecimal(diagnosticEvent.LocalLagMilliseconds));
		command.Parameters.AddWithValue("RawEventType", diagnosticEvent.RawEventType);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(diagnosticEvent.CreatedAtUtc));
	}

	private static void AddBtcUpDown5mOddsTickParameters(NpgsqlCommand command, BtcUpDown5mOddsTick tick)
	{
		command.Parameters.AddWithValue("Id", tick.Id);
		command.Parameters.AddWithValue("MarketId", tick.MarketId);
		command.Parameters.AddWithValue("ConditionId", tick.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", tick.MarketSlug);
		command.Parameters.AddWithValue("MarketStartUtc", UtcDateTime(tick.MarketStartUtc));
		command.Parameters.AddWithValue("MarketEndUtc", UtcDateTime(tick.MarketEndUtc));
		command.Parameters.AddWithValue("SampledAtUtc", UtcDateTime(tick.SampledAtUtc));
		command.Parameters.AddWithValue("SecondsAfterStart", tick.SecondsAfterStart);
		command.Parameters.AddWithValue("SecondsToClose", tick.SecondsToClose);
		command.Parameters.AddWithValue("BinancePriceUsd", tick.BinancePriceUsd);
		command.Parameters.AddWithValue("BinanceSourceUpdatedAtUtc", UtcDateTime(tick.BinanceSourceUpdatedAtUtc));
		command.Parameters.AddWithValue("BinanceFetchedAtUtc", UtcDateTime(tick.BinanceFetchedAtUtc));
		command.Parameters.AddWithValue("BinanceStartPriceUsd", tick.BinanceStartPriceUsd);
		command.Parameters.AddWithValue("BtcMoveFromStartUsd", tick.BtcMoveFromStartUsd);
		command.Parameters.AddWithValue("BtcMoveFromStartBps", tick.BtcMoveFromStartBps);
		command.Parameters.AddWithValue("UpAssetId", tick.UpAssetId);
		command.Parameters.AddWithValue("UpBestBid", NullableDecimal(tick.UpBestBid));
		command.Parameters.AddWithValue("UpBestAsk", NullableDecimal(tick.UpBestAsk));
		command.Parameters.AddWithValue("UpMid", NullableDecimal(tick.UpMid));
		command.Parameters.AddWithValue("UpPriceProxy", NullableDecimal(tick.UpPriceProxy));
		command.Parameters.AddWithValue("UpPriceProxyKind", tick.UpPriceProxyKind);
		command.Parameters.AddWithValue("UpLastTradePrice", NullableDecimal(tick.UpLastTradePrice));
		command.Parameters.AddWithValue("UpBookSource", tick.UpBookSource);
		command.Parameters.AddWithValue("UpBookAgeMs", NullableDecimal(tick.UpBookAgeMs));
		command.Parameters.AddWithValue("DownAssetId", tick.DownAssetId);
		command.Parameters.AddWithValue("DownBestBid", NullableDecimal(tick.DownBestBid));
		command.Parameters.AddWithValue("DownBestAsk", NullableDecimal(tick.DownBestAsk));
		command.Parameters.AddWithValue("DownMid", NullableDecimal(tick.DownMid));
		command.Parameters.AddWithValue("DownPriceProxy", NullableDecimal(tick.DownPriceProxy));
		command.Parameters.AddWithValue("DownPriceProxyKind", tick.DownPriceProxyKind);
		command.Parameters.AddWithValue("DownLastTradePrice", NullableDecimal(tick.DownLastTradePrice));
		command.Parameters.AddWithValue("DownBookSource", tick.DownBookSource);
		command.Parameters.AddWithValue("DownBookAgeMs", NullableDecimal(tick.DownBookAgeMs));
		command.Parameters.AddWithValue("DiagnosticsJson", string.IsNullOrWhiteSpace(tick.DiagnosticsJson) ? "{}" : tick.DiagnosticsJson);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(tick.CreatedAtUtc));
	}

	private static BtcUpDown5mOddsTick ReadBtcUpDown5mOddsTick(NpgsqlDataReader reader)
	{
		return new BtcUpDown5mOddsTick(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			DateTimeOffsetFromUtc(reader.GetDateTime(4)),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			reader.GetDecimal(7),
			reader.GetDecimal(8),
			reader.GetDecimal(9),
			DateTimeOffsetFromUtc(reader.GetDateTime(10)),
			DateTimeOffsetFromUtc(reader.GetDateTime(11)),
			reader.GetDecimal(12),
			reader.GetDecimal(13),
			reader.GetDecimal(14),
			reader.GetString(15),
			reader.IsDBNull(16) ? null : reader.GetDecimal(16),
			reader.IsDBNull(17) ? null : reader.GetDecimal(17),
			reader.IsDBNull(18) ? null : reader.GetDecimal(18),
			reader.IsDBNull(19) ? null : reader.GetDecimal(19),
			reader.GetString(20),
			reader.IsDBNull(21) ? null : reader.GetDecimal(21),
			reader.GetString(22),
			reader.IsDBNull(23) ? null : reader.GetDecimal(23),
			reader.GetString(24),
			reader.IsDBNull(25) ? null : reader.GetDecimal(25),
			reader.IsDBNull(26) ? null : reader.GetDecimal(26),
			reader.IsDBNull(27) ? null : reader.GetDecimal(27),
			reader.IsDBNull(28) ? null : reader.GetDecimal(28),
			reader.GetString(29),
			reader.IsDBNull(30) ? null : reader.GetDecimal(30),
			reader.GetString(31),
			reader.IsDBNull(32) ? null : reader.GetDecimal(32),
			reader.GetString(33),
			DateTimeOffsetFromUtc(reader.GetDateTime(34)));
	}

	private static void AddCryptoUpDown5mOddsTickParameters(NpgsqlCommand command, CryptoUpDown5mOddsTick tick)
	{
		command.Parameters.AddWithValue("Id", tick.Id);
		command.Parameters.AddWithValue("AssetSymbol", tick.AssetSymbol);
		command.Parameters.AddWithValue("BinanceSymbol", tick.BinanceSymbol);
		command.Parameters.AddWithValue("MarketId", tick.MarketId);
		command.Parameters.AddWithValue("ConditionId", tick.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", tick.MarketSlug);
		command.Parameters.AddWithValue("MarketStartUtc", UtcDateTime(tick.MarketStartUtc));
		command.Parameters.AddWithValue("MarketEndUtc", UtcDateTime(tick.MarketEndUtc));
		command.Parameters.AddWithValue("SampledAtUtc", UtcDateTime(tick.SampledAtUtc));
		command.Parameters.AddWithValue("SecondsAfterStart", tick.SecondsAfterStart);
		command.Parameters.AddWithValue("SecondsToClose", tick.SecondsToClose);
		command.Parameters.AddWithValue("BinancePriceUsd", tick.BinancePriceUsd);
		command.Parameters.AddWithValue("BinanceSourceUpdatedAtUtc", UtcDateTime(tick.BinanceSourceUpdatedAtUtc));
		command.Parameters.AddWithValue("BinanceFetchedAtUtc", UtcDateTime(tick.BinanceFetchedAtUtc));
		command.Parameters.AddWithValue("BinanceStartPriceUsd", tick.BinanceStartPriceUsd);
		command.Parameters.AddWithValue("AssetMoveFromStartUsd", tick.AssetMoveFromStartUsd);
		command.Parameters.AddWithValue("AssetMoveFromStartBps", tick.AssetMoveFromStartBps);
		command.Parameters.AddWithValue("UpAssetId", tick.UpAssetId);
		command.Parameters.AddWithValue("UpBestBid", NullableDecimal(tick.UpBestBid));
		command.Parameters.AddWithValue("UpBestAsk", NullableDecimal(tick.UpBestAsk));
		command.Parameters.AddWithValue("UpMid", NullableDecimal(tick.UpMid));
		command.Parameters.AddWithValue("UpPriceProxy", NullableDecimal(tick.UpPriceProxy));
		command.Parameters.AddWithValue("UpPriceProxyKind", tick.UpPriceProxyKind);
		command.Parameters.AddWithValue("UpLastTradePrice", NullableDecimal(tick.UpLastTradePrice));
		command.Parameters.AddWithValue("UpBookSource", tick.UpBookSource);
		command.Parameters.AddWithValue("UpBookAgeMs", NullableDecimal(tick.UpBookAgeMs));
		command.Parameters.AddWithValue("DownAssetId", tick.DownAssetId);
		command.Parameters.AddWithValue("DownBestBid", NullableDecimal(tick.DownBestBid));
		command.Parameters.AddWithValue("DownBestAsk", NullableDecimal(tick.DownBestAsk));
		command.Parameters.AddWithValue("DownMid", NullableDecimal(tick.DownMid));
		command.Parameters.AddWithValue("DownPriceProxy", NullableDecimal(tick.DownPriceProxy));
		command.Parameters.AddWithValue("DownPriceProxyKind", tick.DownPriceProxyKind);
		command.Parameters.AddWithValue("DownLastTradePrice", NullableDecimal(tick.DownLastTradePrice));
		command.Parameters.AddWithValue("DownBookSource", tick.DownBookSource);
		command.Parameters.AddWithValue("DownBookAgeMs", NullableDecimal(tick.DownBookAgeMs));
		command.Parameters.AddWithValue("DiagnosticsJson", string.IsNullOrWhiteSpace(tick.DiagnosticsJson) ? "{}" : tick.DiagnosticsJson);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(tick.CreatedAtUtc));
	}

	private static CryptoUpDown5mOddsTick ReadCryptoUpDown5mOddsTick(NpgsqlDataReader reader)
	{
		return new CryptoUpDown5mOddsTick(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetString(5),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			DateTimeOffsetFromUtc(reader.GetDateTime(7)),
			DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			reader.GetDecimal(9),
			reader.GetDecimal(10),
			reader.GetDecimal(11),
			DateTimeOffsetFromUtc(reader.GetDateTime(12)),
			DateTimeOffsetFromUtc(reader.GetDateTime(13)),
			reader.GetDecimal(14),
			reader.GetDecimal(15),
			reader.GetDecimal(16),
			reader.GetString(17),
			reader.IsDBNull(18) ? null : reader.GetDecimal(18),
			reader.IsDBNull(19) ? null : reader.GetDecimal(19),
			reader.IsDBNull(20) ? null : reader.GetDecimal(20),
			reader.IsDBNull(21) ? null : reader.GetDecimal(21),
			reader.GetString(22),
			reader.IsDBNull(23) ? null : reader.GetDecimal(23),
			reader.GetString(24),
			reader.IsDBNull(25) ? null : reader.GetDecimal(25),
			reader.GetString(26),
			reader.IsDBNull(27) ? null : reader.GetDecimal(27),
			reader.IsDBNull(28) ? null : reader.GetDecimal(28),
			reader.IsDBNull(29) ? null : reader.GetDecimal(29),
			reader.IsDBNull(30) ? null : reader.GetDecimal(30),
			reader.GetString(31),
			reader.IsDBNull(32) ? null : reader.GetDecimal(32),
			reader.GetString(33),
			reader.IsDBNull(34) ? null : reader.GetDecimal(34),
			reader.GetString(35),
			DateTimeOffsetFromUtc(reader.GetDateTime(36)));
	}

	private static void AddPolymarketDataApiTraderParameters(NpgsqlCommand command, PolymarketDataApiTrader trader)
	{
		command.Parameters.AddWithValue("Wallet", trader.Wallet);
		command.Parameters.AddWithValue("Name", trader.Name);
		command.Parameters.AddWithValue("Pseudonym", ((object)trader.Pseudonym) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Bio", ((object)trader.Bio) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ProfileImage", ((object)trader.ProfileImage) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ProfileImageOptimized", ((object)trader.ProfileImageOptimized) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("FirstSeenAtUtc", UtcDateTime(trader.FirstSeenAtUtc));
		command.Parameters.AddWithValue("LastSeenAtUtc", UtcDateTime(trader.LastSeenAtUtc));
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("LastGlobalSeenAtUtc", NpgsqlDbType.TimestampTz);
		DateTimeOffset? lastGlobalSeenAtUtc = trader.LastGlobalSeenAtUtc;
		object value;
		if (lastGlobalSeenAtUtc.HasValue)
		{
			DateTimeOffset lastGlobalSeen = lastGlobalSeenAtUtc.GetValueOrDefault();
			value = UtcDateTime(lastGlobalSeen);
		}
		else
		{
			value = DBNull.Value;
		}
		npgsqlParameter.Value = value;
		NpgsqlParameter npgsqlParameter2 = command.Parameters.Add("LastFullSyncAtUtc", NpgsqlDbType.TimestampTz);
		lastGlobalSeenAtUtc = trader.LastFullSyncAtUtc;
		object value2;
		if (lastGlobalSeenAtUtc.HasValue)
		{
			DateTimeOffset lastFullSync = lastGlobalSeenAtUtc.GetValueOrDefault();
			value2 = UtcDateTime(lastFullSync);
		}
		else
		{
			value2 = DBNull.Value;
		}
		npgsqlParameter2.Value = value2;
		NpgsqlParameter npgsqlParameter3 = command.Parameters.Add("LastIncrementalSyncAtUtc", NpgsqlDbType.TimestampTz);
		lastGlobalSeenAtUtc = trader.LastIncrementalSyncAtUtc;
		object value3;
		if (lastGlobalSeenAtUtc.HasValue)
		{
			DateTimeOffset lastIncrementalSync = lastGlobalSeenAtUtc.GetValueOrDefault();
			value3 = UtcDateTime(lastIncrementalSync);
		}
		else
		{
			value3 = DBNull.Value;
		}
		npgsqlParameter3.Value = value3;
		NpgsqlParameter npgsqlParameter4 = command.Parameters.Add("LastTradeTimestampUtc", NpgsqlDbType.TimestampTz);
		lastGlobalSeenAtUtc = trader.LastTradeTimestampUtc;
		object value4;
		if (lastGlobalSeenAtUtc.HasValue)
		{
			DateTimeOffset lastTrade = lastGlobalSeenAtUtc.GetValueOrDefault();
			value4 = UtcDateTime(lastTrade);
		}
		else
		{
			value4 = DBNull.Value;
		}
		npgsqlParameter4.Value = value4;
		command.Parameters.AddWithValue("FullSyncCompleted", trader.FullSyncCompleted);
		command.Parameters.AddWithValue("FullSyncTradesFetched", trader.FullSyncTradesFetched);
		command.Parameters.AddWithValue("FullSyncTradesInserted", trader.FullSyncTradesInserted);
		command.Parameters.AddWithValue("IncrementalSyncCount", trader.IncrementalSyncCount);
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(trader.UpdatedAtUtc));
	}

	private static void AddPolymarketDataApiPositionParameters(NpgsqlCommand command, PolymarketDataApiPosition position)
	{
		DateTime now = DateTime.UtcNow;
		command.Parameters.AddWithValue("Id", Guid.NewGuid());
		command.Parameters.AddWithValue("Wallet", position.Wallet);
		command.Parameters.AddWithValue("PositionStatus", position.Status.ToString());
		command.Parameters.AddWithValue("AssetId", position.AssetId);
		command.Parameters.AddWithValue("ConditionId", position.ConditionId);
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("Size", NpgsqlDbType.Numeric);
		decimal? size = position.Size;
		object value;
		if (size.HasValue)
		{
			decimal size2 = size.GetValueOrDefault();
			value = size2;
		}
		else
		{
			value = DBNull.Value;
		}
		npgsqlParameter.Value = value;
		command.Parameters.AddWithValue("AvgPrice", position.AvgPrice);
		NpgsqlParameter npgsqlParameter2 = command.Parameters.Add("InitialValueUsd", NpgsqlDbType.Numeric);
		size = position.InitialValue;
		object value2;
		if (size.HasValue)
		{
			decimal initialValue = size.GetValueOrDefault();
			value2 = initialValue;
		}
		else
		{
			value2 = DBNull.Value;
		}
		npgsqlParameter2.Value = value2;
		NpgsqlParameter npgsqlParameter3 = command.Parameters.Add("CurrentValueUsd", NpgsqlDbType.Numeric);
		size = position.CurrentValue;
		object value3;
		if (size.HasValue)
		{
			decimal currentValue = size.GetValueOrDefault();
			value3 = currentValue;
		}
		else
		{
			value3 = DBNull.Value;
		}
		npgsqlParameter3.Value = value3;
		NpgsqlParameter npgsqlParameter4 = command.Parameters.Add("CashPnlUsd", NpgsqlDbType.Numeric);
		size = position.CashPnl;
		object value4;
		if (size.HasValue)
		{
			decimal cashPnl = size.GetValueOrDefault();
			value4 = cashPnl;
		}
		else
		{
			value4 = DBNull.Value;
		}
		npgsqlParameter4.Value = value4;
		NpgsqlParameter npgsqlParameter5 = command.Parameters.Add("PercentPnl", NpgsqlDbType.Numeric);
		size = position.PercentPnl;
		object value5;
		if (size.HasValue)
		{
			decimal percentPnl = size.GetValueOrDefault();
			value5 = percentPnl;
		}
		else
		{
			value5 = DBNull.Value;
		}
		npgsqlParameter5.Value = value5;
		command.Parameters.AddWithValue("TotalBought", position.TotalBought);
		command.Parameters.AddWithValue("RealizedPnlUsd", position.RealizedPnl);
		NpgsqlParameter npgsqlParameter6 = command.Parameters.Add("PercentRealizedPnl", NpgsqlDbType.Numeric);
		size = position.PercentRealizedPnl;
		object value6;
		if (size.HasValue)
		{
			decimal percentRealizedPnl = size.GetValueOrDefault();
			value6 = percentRealizedPnl;
		}
		else
		{
			value6 = DBNull.Value;
		}
		npgsqlParameter6.Value = value6;
		command.Parameters.AddWithValue("CurPrice", position.CurPrice);
		NpgsqlParameter npgsqlParameter7 = command.Parameters.Add("TimestampUtc", NpgsqlDbType.TimestampTz);
		DateTimeOffset? timestampUtc = position.TimestampUtc;
		object value7;
		if (timestampUtc.HasValue)
		{
			DateTimeOffset timestamp = timestampUtc.GetValueOrDefault();
			value7 = UtcDateTime(timestamp);
		}
		else
		{
			value7 = DBNull.Value;
		}
		npgsqlParameter7.Value = value7;
		command.Parameters.AddWithValue("MarketTitle", position.MarketTitle);
		command.Parameters.AddWithValue("MarketSlug", position.MarketSlug);
		command.Parameters.AddWithValue("Icon", ((object)position.Icon) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("EventId", ((object)position.EventId) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("EventSlug", ((object)position.EventSlug) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Category", ((object)position.Category) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("Outcome", position.Outcome);
		NpgsqlParameter npgsqlParameter8 = command.Parameters.Add("OutcomeIndex", NpgsqlDbType.Integer);
		int? outcomeIndex = position.OutcomeIndex;
		object value8;
		if (outcomeIndex.HasValue)
		{
			int outcomeIndex2 = outcomeIndex.GetValueOrDefault();
			value8 = outcomeIndex2;
		}
		else
		{
			value8 = DBNull.Value;
		}
		npgsqlParameter8.Value = value8;
		command.Parameters.AddWithValue("OppositeOutcome", ((object)position.OppositeOutcome) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("OppositeAsset", ((object)position.OppositeAsset) ?? ((object)DBNull.Value));
		NpgsqlParameter npgsqlParameter9 = command.Parameters.Add("EndDateUtc", NpgsqlDbType.TimestampTz);
		timestampUtc = position.EndDateUtc;
		object value9;
		if (timestampUtc.HasValue)
		{
			DateTimeOffset endDate = timestampUtc.GetValueOrDefault();
			value9 = UtcDateTime(endDate);
		}
		else
		{
			value9 = DBNull.Value;
		}
		npgsqlParameter9.Value = value9;
		NpgsqlParameter npgsqlParameter10 = command.Parameters.Add("Redeemable", NpgsqlDbType.Boolean);
		bool? redeemable = position.Redeemable;
		object value10;
		if (redeemable.HasValue)
		{
			bool redeemable2 = redeemable == true;
			value10 = redeemable2;
		}
		else
		{
			value10 = DBNull.Value;
		}
		npgsqlParameter10.Value = value10;
		NpgsqlParameter npgsqlParameter11 = command.Parameters.Add("Mergeable", NpgsqlDbType.Boolean);
		redeemable = position.Mergeable;
		object value11;
		if (redeemable.HasValue)
		{
			bool mergeable = redeemable == true;
			value11 = mergeable;
		}
		else
		{
			value11 = DBNull.Value;
		}
		npgsqlParameter11.Value = value11;
		NpgsqlParameter npgsqlParameter12 = command.Parameters.Add("NegativeRisk", NpgsqlDbType.Boolean);
		redeemable = position.NegativeRisk;
		object value12;
		if (redeemable.HasValue)
		{
			bool negativeRisk = redeemable == true;
			value12 = negativeRisk;
		}
		else
		{
			value12 = DBNull.Value;
		}
		npgsqlParameter12.Value = value12;
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(position.RawJson) ? "{}" : position.RawJson);
		command.Parameters.AddWithValue("FetchedAtUtc", now);
		command.Parameters.AddWithValue("UpdatedAtUtc", now);
	}

	private static void AddPolymarketDataApiWalletCategoryRatingParameters(NpgsqlCommand command, PolymarketDataApiWalletCategoryRating rating)
	{
		DateTime refreshedAtUtc = UtcDateTime(rating.RefreshedAtUtc);
		command.Parameters.AddWithValue("Wallet", rating.Wallet);
		command.Parameters.AddWithValue("LocalCategory", rating.LocalCategory);
		command.Parameters.AddWithValue("PolymarketCategory", rating.PolymarketCategory);
		command.Parameters.AddWithValue("TimePeriod", rating.TimePeriod);
		command.Parameters.AddWithValue("OrderBy", rating.OrderBy);
		command.Parameters.AddWithValue("Found", rating.Found);
		NpgsqlParameter rankParameter = command.Parameters.Add("LeaderboardRank", NpgsqlDbType.Integer);
		rankParameter.Value = rating.Rank.HasValue ? rating.Rank.Value : (object)DBNull.Value;
		command.Parameters.AddWithValue("UserName", ((object)rating.UserName) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("XUsername", ((object)rating.XUsername) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("ProfileImage", ((object)rating.ProfileImage) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("VerifiedBadge", rating.VerifiedBadge);
		NpgsqlParameter pnlParameter = command.Parameters.Add("LeaderboardPnlUsd", NpgsqlDbType.Numeric);
		pnlParameter.Value = rating.LeaderboardPnlUsd.HasValue ? rating.LeaderboardPnlUsd.Value : (object)DBNull.Value;
		NpgsqlParameter volumeParameter = command.Parameters.Add("LeaderboardVolumeUsd", NpgsqlDbType.Numeric);
		volumeParameter.Value = rating.LeaderboardVolumeUsd.HasValue ? rating.LeaderboardVolumeUsd.Value : (object)DBNull.Value;
		NpgsqlParameter ratioParameter = command.Parameters.Add("LeaderboardPnlToVolumePct", NpgsqlDbType.Numeric);
		ratioParameter.Value = rating.LeaderboardPnlToVolumePct.HasValue ? rating.LeaderboardPnlToVolumePct.Value : (object)DBNull.Value;
		command.Parameters.AddWithValue("CurrentPositionsCount", rating.CurrentPositionsCount);
		command.Parameters.AddWithValue("CurrentPositionsInitialValueUsd", rating.CurrentPositionsInitialValueUsd);
		command.Parameters.AddWithValue("CurrentPositionsCurrentValueUsd", rating.CurrentPositionsCurrentValueUsd);
		command.Parameters.AddWithValue("CurrentPositionsCashPnlUsd", rating.CurrentPositionsCashPnlUsd);
		command.Parameters.AddWithValue("CurrentPositionsRealizedPnlUsd", rating.CurrentPositionsRealizedPnlUsd);
		command.Parameters.AddWithValue("CurrentPositionsTotalPnlUsd", rating.CurrentPositionsTotalPnlUsd);
		NpgsqlParameter currentPercentPnlParameter = command.Parameters.Add("CurrentPositionsPercentPnl", NpgsqlDbType.Numeric);
		currentPercentPnlParameter.Value = rating.CurrentPositionsPercentPnl.HasValue ? rating.CurrentPositionsPercentPnl.Value : (object)DBNull.Value;
		NpgsqlParameter currentPercentRealizedPnlParameter = command.Parameters.Add("CurrentPositionsPercentRealizedPnl", NpgsqlDbType.Numeric);
		currentPercentRealizedPnlParameter.Value = rating.CurrentPositionsPercentRealizedPnl.HasValue ? rating.CurrentPositionsPercentRealizedPnl.Value : (object)DBNull.Value;
		command.Parameters.AddWithValue("ClosedPositionsCount", rating.ClosedPositionsCount);
		command.Parameters.AddWithValue("ClosedPositionsCostBasisUsd", rating.ClosedPositionsCostBasisUsd);
		command.Parameters.AddWithValue("ClosedPositionsRealizedPnlUsd", rating.ClosedPositionsRealizedPnlUsd);
		NpgsqlParameter closedPercentRealizedPnlParameter = command.Parameters.Add("ClosedPositionsPercentRealizedPnl", NpgsqlDbType.Numeric);
		closedPercentRealizedPnlParameter.Value = rating.ClosedPositionsPercentRealizedPnl.HasValue ? rating.ClosedPositionsPercentRealizedPnl.Value : (object)DBNull.Value;
		command.Parameters.AddWithValue("PositionsTotalCostBasisUsd", rating.PositionsTotalCostBasisUsd);
		command.Parameters.AddWithValue("PositionsTotalPnlUsd", rating.PositionsTotalPnlUsd);
		NpgsqlParameter totalPercentPnlParameter = command.Parameters.Add("PositionsTotalPercentPnl", NpgsqlDbType.Numeric);
		totalPercentPnlParameter.Value = rating.PositionsTotalPercentPnl.HasValue ? rating.PositionsTotalPercentPnl.Value : (object)DBNull.Value;
		NpgsqlParameter positionsRefreshedAtParameter = command.Parameters.Add("PositionsRefreshedAtUtc", NpgsqlDbType.TimestampTz);
		positionsRefreshedAtParameter.Value = rating.PositionsRefreshedAtUtc.HasValue ? UtcDateTime(rating.PositionsRefreshedAtUtc.Value) : (object)DBNull.Value;
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(rating.RawJson) ? "{}" : rating.RawJson);
		command.Parameters.Add("RefreshedAtUtc", NpgsqlDbType.TimestampTz).Value = refreshedAtUtc;
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = refreshedAtUtc;
	}

	private static PolymarketDataApiTrader ReadPolymarketDataApiTrader(NpgsqlDataReader reader)
	{
		return new PolymarketDataApiTrader(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), DateTimeOffsetFromUtc(reader.GetDateTime(6)), DateTimeOffsetFromUtc(reader.GetDateTime(7)), reader.IsDBNull(8) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(8))), reader.IsDBNull(9) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(9))), reader.IsDBNull(10) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(10))), reader.IsDBNull(11) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(11))), reader.GetBoolean(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), DateTimeOffsetFromUtc(reader.GetDateTime(16)), reader.FieldCount > 17 && !reader.IsDBNull(17) ? DateTimeOffsetFromUtc(reader.GetDateTime(17)) : null, reader.FieldCount > 18 && !reader.IsDBNull(18) ? DateTimeOffsetFromUtc(reader.GetDateTime(18)) : null, reader.FieldCount > 19 ? reader.GetInt32(19) : 0, reader.FieldCount > 20 && !reader.IsDBNull(20) ? reader.GetString(20) : null);
	}

	private static void AddTraderDiscoveryParameters(NpgsqlCommand command, TraderDiscoveryCandidate candidate)
	{
		command.Parameters.AddWithValue("Id", candidate.Id);
		command.Parameters.AddWithValue("DiscoveryType", candidate.DiscoveryType);
		command.Parameters.AddWithValue("Category", candidate.Category);
		command.Parameters.AddWithValue("TimePeriod", candidate.TimePeriod);
		NpgsqlParameterCollection parameters = command.Parameters;
		int? rank = candidate.Rank;
		object value;
		if (rank.HasValue)
		{
			int rank2 = rank.GetValueOrDefault();
			value = rank2;
		}
		else
		{
			value = DBNull.Value;
		}
		parameters.AddWithValue("Rank", value);
		command.Parameters.AddWithValue("Wallet", candidate.Wallet);
		command.Parameters.AddWithValue("UserName", candidate.UserName);
		command.Parameters.AddWithValue("XUsername", ((object)candidate.XUsername) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("LeaderboardPnl", candidate.LeaderboardPnl);
		command.Parameters.AddWithValue("LeaderboardVolume", candidate.LeaderboardVolume);
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("AllTimePnl", NpgsqlDbType.Numeric);
		decimal? allTimePnl = candidate.AllTimePnl;
		object value2;
		if (allTimePnl.HasValue)
		{
			decimal allTimePnl2 = allTimePnl.GetValueOrDefault();
			value2 = allTimePnl2;
		}
		else
		{
			value2 = DBNull.Value;
		}
		npgsqlParameter.Value = value2;
		NpgsqlParameter npgsqlParameter2 = command.Parameters.Add("AllTimeVolume", NpgsqlDbType.Numeric);
		allTimePnl = candidate.AllTimeVolume;
		object value3;
		if (allTimePnl.HasValue)
		{
			decimal allTimeVolume = allTimePnl.GetValueOrDefault();
			value3 = allTimeVolume;
		}
		else
		{
			value3 = DBNull.Value;
		}
		npgsqlParameter2.Value = value3;
		command.Parameters.AddWithValue("VerifiedBadge", candidate.VerifiedBadge);
		command.Parameters.AddWithValue("TradesFetched", candidate.TradesFetched);
		command.Parameters.AddWithValue("BuyTrades", candidate.BuyTrades);
		command.Parameters.AddWithValue("SellTrades", candidate.SellTrades);
		command.Parameters.AddWithValue("RecentTradeVolumeUsd", candidate.RecentTradeVolumeUsd);
		command.Parameters.AddWithValue("AverageTradeUsd", candidate.AverageTradeUsd);
		NpgsqlParameterCollection parameters2 = command.Parameters;
		DateTimeOffset? lastTradeUtc = candidate.LastTradeUtc;
		object value4;
		if (lastTradeUtc.HasValue)
		{
			DateTimeOffset lastTrade = lastTradeUtc.GetValueOrDefault();
			value4 = UtcDateTime(lastTrade);
		}
		else
		{
			value4 = DBNull.Value;
		}
		parameters2.AddWithValue("LastTradeUtc", value4);
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
		command.Parameters.AddWithValue("XUsername", ((object)snapshot.XUsername) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("VerifiedBadge", snapshot.VerifiedBadge);
		NpgsqlParameter npgsqlParameter = command.Parameters.Add("PnlRank", NpgsqlDbType.Integer);
		int? pnlRank = snapshot.PnlRank;
		object value;
		if (pnlRank.HasValue)
		{
			int pnlRank2 = pnlRank.GetValueOrDefault();
			value = pnlRank2;
		}
		else
		{
			value = DBNull.Value;
		}
		npgsqlParameter.Value = value;
		NpgsqlParameter npgsqlParameter2 = command.Parameters.Add("PnlPageOffset", NpgsqlDbType.Integer);
		pnlRank = snapshot.PnlPageOffset;
		object value2;
		if (pnlRank.HasValue)
		{
			int pnlPageOffset = pnlRank.GetValueOrDefault();
			value2 = pnlPageOffset;
		}
		else
		{
			value2 = DBNull.Value;
		}
		npgsqlParameter2.Value = value2;
		NpgsqlParameter npgsqlParameter3 = command.Parameters.Add("PnlLeaderboardPnl", NpgsqlDbType.Numeric);
		decimal? pnlLeaderboardPnl = snapshot.PnlLeaderboardPnl;
		object value3;
		if (pnlLeaderboardPnl.HasValue)
		{
			decimal pnlLeaderboardPnl2 = pnlLeaderboardPnl.GetValueOrDefault();
			value3 = pnlLeaderboardPnl2;
		}
		else
		{
			value3 = DBNull.Value;
		}
		npgsqlParameter3.Value = value3;
		NpgsqlParameter npgsqlParameter4 = command.Parameters.Add("PnlLeaderboardVolume", NpgsqlDbType.Numeric);
		pnlLeaderboardPnl = snapshot.PnlLeaderboardVolume;
		object value4;
		if (pnlLeaderboardPnl.HasValue)
		{
			decimal pnlLeaderboardVolume = pnlLeaderboardPnl.GetValueOrDefault();
			value4 = pnlLeaderboardVolume;
		}
		else
		{
			value4 = DBNull.Value;
		}
		npgsqlParameter4.Value = value4;
		NpgsqlParameter npgsqlParameter5 = command.Parameters.Add("PnlSnapshotAtUtc", NpgsqlDbType.TimestampTz);
		DateTimeOffset? pnlSnapshotAtUtc = snapshot.PnlSnapshotAtUtc;
		object value5;
		if (pnlSnapshotAtUtc.HasValue)
		{
			DateTimeOffset pnlSnapshotAt = pnlSnapshotAtUtc.GetValueOrDefault();
			value5 = UtcDateTime(pnlSnapshotAt);
		}
		else
		{
			value5 = DBNull.Value;
		}
		npgsqlParameter5.Value = value5;
		NpgsqlParameter npgsqlParameter6 = command.Parameters.Add("VolumeRank", NpgsqlDbType.Integer);
		pnlRank = snapshot.VolumeRank;
		object value6;
		if (pnlRank.HasValue)
		{
			int volumeRank = pnlRank.GetValueOrDefault();
			value6 = volumeRank;
		}
		else
		{
			value6 = DBNull.Value;
		}
		npgsqlParameter6.Value = value6;
		NpgsqlParameter npgsqlParameter7 = command.Parameters.Add("VolumePageOffset", NpgsqlDbType.Integer);
		pnlRank = snapshot.VolumePageOffset;
		object value7;
		if (pnlRank.HasValue)
		{
			int volumePageOffset = pnlRank.GetValueOrDefault();
			value7 = volumePageOffset;
		}
		else
		{
			value7 = DBNull.Value;
		}
		npgsqlParameter7.Value = value7;
		NpgsqlParameter npgsqlParameter8 = command.Parameters.Add("VolumeLeaderboardPnl", NpgsqlDbType.Numeric);
		pnlLeaderboardPnl = snapshot.VolumeLeaderboardPnl;
		object value8;
		if (pnlLeaderboardPnl.HasValue)
		{
			decimal volumeLeaderboardPnl = pnlLeaderboardPnl.GetValueOrDefault();
			value8 = volumeLeaderboardPnl;
		}
		else
		{
			value8 = DBNull.Value;
		}
		npgsqlParameter8.Value = value8;
		NpgsqlParameter npgsqlParameter9 = command.Parameters.Add("VolumeLeaderboardVolume", NpgsqlDbType.Numeric);
		pnlLeaderboardPnl = snapshot.VolumeLeaderboardVolume;
		object value9;
		if (pnlLeaderboardPnl.HasValue)
		{
			decimal volumeLeaderboardVolume = pnlLeaderboardPnl.GetValueOrDefault();
			value9 = volumeLeaderboardVolume;
		}
		else
		{
			value9 = DBNull.Value;
		}
		npgsqlParameter9.Value = value9;
		NpgsqlParameter npgsqlParameter10 = command.Parameters.Add("VolumeSnapshotAtUtc", NpgsqlDbType.TimestampTz);
		pnlSnapshotAtUtc = snapshot.VolumeSnapshotAtUtc;
		object value10;
		if (pnlSnapshotAtUtc.HasValue)
		{
			DateTimeOffset volumeSnapshotAt = pnlSnapshotAtUtc.GetValueOrDefault();
			value10 = UtcDateTime(volumeSnapshotAt);
		}
		else
		{
			value10 = DBNull.Value;
		}
		npgsqlParameter10.Value = value10;
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(snapshot.UpdatedAtUtc));
	}

	private static async Task<IReadOnlyList<LiveOrder>> ReadLiveOrdersAsync(NpgsqlDataReader reader, CancellationToken cancellationToken)
	{
		List<LiveOrder> results = new List<LiveOrder>();
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new LiveOrder(
				reader.GetGuid(0),
				reader.GetGuid(1),
				Enum.Parse<LiveOrderStatus>(reader.GetString(3)),
				reader.IsDBNull(4) ? null : reader.GetString(4),
				Enum.Parse<TradeSide>(reader.GetString(5)),
				reader.GetString(6),
				reader.GetString(7),
				reader.GetString(8),
				reader.GetDecimal(9),
				reader.GetDecimal(10),
				reader.GetDecimal(11),
				reader.GetString(12),
				DateTimeOffsetFromUtc(reader.GetDateTime(13)),
				DateTimeOffsetFromUtc(reader.GetDateTime(14)),
				reader.IsDBNull(15) ? ((DateTimeOffset?)null) : new DateTimeOffset?(DateTimeOffsetFromUtc(reader.GetDateTime(15))),
				reader.GetString(16),
				reader.GetDecimal(17),
				reader.GetDecimal(18),
				reader.GetString(23),
				reader.GetString(24),
				reader.GetString(25),
				DateTimeOffsetFromUtc(reader.GetDateTime(26)),
				reader.GetGuid(2),
				reader.GetBoolean(27),
				reader.IsDBNull(28) ? null : reader.GetDecimal(28),
				reader.IsDBNull(29) ? null : reader.GetDecimal(29),
				reader.IsDBNull(30) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(30)),
				reader.IsDBNull(31) ? null : reader.GetString(31),
				reader.IsDBNull(32) ? null : reader.GetString(32),
				reader.IsDBNull(19) ? null : reader.GetDecimal(19),
				reader.GetDecimal(20),
				reader.GetDecimal(21),
				reader.GetDecimal(22),
				reader.IsDBNull(33) ? null : reader.GetBoolean(33),
				reader.GetString(34),
				reader.IsDBNull(35) ? null : reader.GetGuid(35),
				reader.IsDBNull(36) ? string.Empty : reader.GetString(36),
				reader.IsDBNull(37) ? null : reader.GetBoolean(37),
				reader.IsDBNull(38) ? null : reader.GetGuid(38)));
		}
		return results;
	}

	private static IReadOnlyList<string> SplitReasonCodes(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? ((IReadOnlyList<string>)Array.Empty<string>()) : ((IReadOnlyList<string>)value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
	}
}
