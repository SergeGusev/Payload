using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository(PostgresConnectionFactory connectionFactory) : IAppRepository
{
	private const int OnChainDerivedRefreshLockKey1 = 1348686930;

	private const int OnChainDerivedRefreshLockKey2 = 1329812038;

	private const int PaperCopiedTraderPerformanceRefreshLockKey1 = 1348686931;

	private const int PaperCopiedTraderPerformanceRefreshLockKey2 = 1329812039;

	private const int PaperCopiedTraderPerformanceCommandTimeoutSeconds = 180;

	private const int StrategyPerformanceCommandTimeoutSeconds = 180;

	private const int StrategyMarketPaperRunInsertBatchSize = 2_000;

	private static readonly JsonSerializerOptions BulkInsertJsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	private const string PolymarketGammaMarketSelectColumns = "market_id, condition_id, question_id, slug, question, event_id, event_slug, event_title,\n       series_slug, category, active, closed, archived, restricted, accepting_orders, enable_order_book,\n       negative_risk, liquidity, liquidity_clob, volume, volume_24hr, best_bid, best_ask, spread,\n       created_at_utc, updated_at_utc, start_date_utc, end_date_utc, event_start_time_utc,\n       outcomes_json, clob_token_ids_json, raw_json, fetched_at_utc, last_trade_price, order_min_size,\n       order_price_min_tick_size";

	private const string PaperOrderSelectColumns = "id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n       created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, raw_decision_json::text, correlation_id, execution_source";

	private const string RecentPaperOrderSelectColumns = "id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price, size_shares, notional_usd,\n       created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc, NULL::text, correlation_id, execution_source";

	private const string LiveOrderSelectColumns = "id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,\n       notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,\n       filled_size, remaining_size, average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,\n       cancel_status, raw_response_json::text, validation_summary, updated_at_utc,\n       balance_effect_applied, settlement_value_usd, realized_pnl_usd, settled_at_utc, winning_asset_id, winning_outcome,\n       won, settlement_source, correlation_id, execution_source, post_only, paper_order_id,\n       fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,\n       fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd,\n       historical_gross_net_parity_ownership, row_version";

	public async Task<DateTimeOffset> GetDatabaseNowUtcAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT clock_timestamp();");
		object? value = await command.ExecuteScalarAsync(cancellationToken);
		if (value is DateTimeOffset timestamp)
		{
			return timestamp.ToUniversalTime();
		}

		if (value is DateTime dateTime)
		{
			return DateTimeOffsetFromUtc(dateTime);
		}

		return DateTimeOffset.UtcNow;
	}

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

	public async Task<PolymarketAutoRedeemAttempt?> GetPolymarketAutoRedeemAttemptAsync(string wallet, string conditionId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, wallet, proxy_wallet, condition_id, asset_id, market_slug, market_title, outcome,
       outcome_index, redeemable_value_usd, size, status, dry_run, auto_submit_enabled,
       target_contract, calldata, collateral_token, parent_collection_id, index_sets_json::text,
       relayer_transaction_id, transaction_hash, last_error, detected_at_utc, last_seen_at_utc,
       submitted_at_utc, confirmed_at_utc, updated_at_utc, raw_position_json::text
FROM polymarket_auto_redeem_attempts
WHERE wallet = @Wallet
  AND condition_id = @ConditionId;
""");
		command.Parameters.AddWithValue("Wallet", wallet);
		command.Parameters.AddWithValue("ConditionId", conditionId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken)
			? ReadPolymarketAutoRedeemAttempt(reader)
			: null;
	}

	public async Task UpsertPolymarketAutoRedeemAttemptAsync(PolymarketAutoRedeemAttempt attempt, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO polymarket_auto_redeem_attempts (
    id, wallet, proxy_wallet, condition_id, asset_id, market_slug, market_title, outcome,
    outcome_index, redeemable_value_usd, size, status, dry_run, auto_submit_enabled,
    target_contract, calldata, collateral_token, parent_collection_id, index_sets_json,
    relayer_transaction_id, transaction_hash, last_error, detected_at_utc, last_seen_at_utc,
    submitted_at_utc, confirmed_at_utc, updated_at_utc, raw_position_json
) VALUES (
    @Id, @Wallet, @ProxyWallet, @ConditionId, @AssetId, @MarketSlug, @MarketTitle, @Outcome,
    @OutcomeIndex, @RedeemableValueUsd, @Size, @Status, @DryRun, @AutoSubmitEnabled,
    @TargetContract, @Calldata, @CollateralToken, @ParentCollectionId, CAST(@IndexSetsJson AS jsonb),
    @RelayerTransactionId, @TransactionHash, @LastError, @DetectedAtUtc, @LastSeenAtUtc,
    @SubmittedAtUtc, @ConfirmedAtUtc, @UpdatedAtUtc, CAST(@RawPositionJson AS jsonb)
)
ON CONFLICT (wallet, condition_id) DO UPDATE SET
    proxy_wallet = excluded.proxy_wallet,
    asset_id = excluded.asset_id,
    market_slug = excluded.market_slug,
    market_title = excluded.market_title,
    outcome = excluded.outcome,
    outcome_index = excluded.outcome_index,
    redeemable_value_usd = excluded.redeemable_value_usd,
    size = excluded.size,
    status = CASE
        WHEN polymarket_auto_redeem_attempts.status IN ('Submitted', 'Confirmed')
            THEN polymarket_auto_redeem_attempts.status
        ELSE excluded.status
    END,
    dry_run = excluded.dry_run,
    auto_submit_enabled = excluded.auto_submit_enabled,
    target_contract = excluded.target_contract,
    calldata = excluded.calldata,
    collateral_token = excluded.collateral_token,
    parent_collection_id = excluded.parent_collection_id,
    index_sets_json = excluded.index_sets_json,
    relayer_transaction_id = COALESCE(polymarket_auto_redeem_attempts.relayer_transaction_id, excluded.relayer_transaction_id),
    transaction_hash = COALESCE(polymarket_auto_redeem_attempts.transaction_hash, excluded.transaction_hash),
    last_error = excluded.last_error,
    detected_at_utc = LEAST(polymarket_auto_redeem_attempts.detected_at_utc, excluded.detected_at_utc),
    last_seen_at_utc = GREATEST(polymarket_auto_redeem_attempts.last_seen_at_utc, excluded.last_seen_at_utc),
    submitted_at_utc = COALESCE(polymarket_auto_redeem_attempts.submitted_at_utc, excluded.submitted_at_utc),
    confirmed_at_utc = COALESCE(polymarket_auto_redeem_attempts.confirmed_at_utc, excluded.confirmed_at_utc),
    updated_at_utc = excluded.updated_at_utc,
    raw_position_json = excluded.raw_position_json;
""");
		AddPolymarketAutoRedeemAttemptParameters(command, attempt);
		await command.ExecuteNonQueryAsync(cancellationToken);
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
		await using NpgsqlCommand command = CreateCommand(connection, "WITH requested_assets AS (\n    SELECT unnest(@AssetSymbols::text[]) AS asset_symbol\n)\nSELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND EXISTS (\n      SELECT 1\n      FROM requested_assets asset\n      WHERE lower(slug) ~ ('^' || asset.asset_symbol || '-updown-(5m|15m)-[0-9]+$')\n         OR lower(COALESCE(event_slug, '')) ~ ('^' || asset.asset_symbol || '-updown-(5m|15m)-[0-9]+$')\n         OR lower(COALESCE(series_slug, '')) IN (asset.asset_symbol || '-up-or-down-5m', asset.asset_symbol || '-up-or-down-15m')\n  )\n  AND (end_date_utc IS NULL OR end_date_utc >= now() - interval '4 hours')\nORDER BY COALESCE(event_start_time_utc, end_date_utc, created_at_utc) ASC NULLS LAST,\n         market_id ASC\nLIMIT @Limit;");
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

	public async Task<IReadOnlyList<PolymarketGammaMarket>> GetUpDownStrategyGammaMarketsForObservationAsync(IReadOnlyCollection<string> assetSymbols, DateTimeOffset marketEndAtOrAfterUtc, DateTimeOffset marketStartAtOrBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToLowerInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedSymbols.Length == 0 || marketStartAtOrBeforeUtc < marketEndAtOrAfterUtc)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "WITH requested_assets AS (\n    SELECT unnest(@AssetSymbols::text[]) AS asset_symbol\n)\nSELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND (event_start_time_utc IS NULL OR event_start_time_utc <= @MarketStartAtOrBeforeUtc)\n  AND (\n      end_date_utc >= @MarketEndAtOrAfterUtc\n      OR (\n          end_date_utc IS NULL\n          AND (event_start_time_utc IS NULL OR event_start_time_utc >= @MarketEndAtOrAfterUtc)\n      )\n  )\n  AND EXISTS (\n      SELECT 1\n      FROM requested_assets asset\n      WHERE (\n          asset.asset_symbol = 'btc'\n          AND (\n              lower(slug) ~ '^btc-updown-(5m|15m|4h)-[0-9]+$'\n              OR lower(COALESCE(event_slug, '')) ~ '^btc-updown-(5m|15m|4h)-[0-9]+$'\n              OR lower(COALESCE(series_slug, '')) IN ('btc-up-or-down-5m', 'btc-up-or-down-15m', 'btc-up-or-down-hourly', 'btc-up-or-down-4h')\n          )\n      )\n      OR (\n          asset.asset_symbol <> 'btc'\n          AND (\n              lower(slug) ~ ('^' || asset.asset_symbol || '-updown-(5m|15m)-[0-9]+$')\n              OR lower(COALESCE(event_slug, '')) ~ ('^' || asset.asset_symbol || '-updown-(5m|15m)-[0-9]+$')\n              OR lower(COALESCE(series_slug, '')) IN (asset.asset_symbol || '-up-or-down-5m', asset.asset_symbol || '-up-or-down-15m')\n          )\n      )\n  )\nORDER BY COALESCE(event_start_time_utc, end_date_utc, created_at_utc) ASC NULLS LAST,\n         market_id ASC\nLIMIT @Limit;");
		command.Parameters.Add("AssetSymbols", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = normalizedSymbols;
		command.Parameters.Add("MarketEndAtOrAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(marketEndAtOrAfterUtc);
		command.Parameters.Add("MarketStartAtOrBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(marketStartAtOrBeforeUtc);
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
		List<PolymarketGammaMarket> results = [];
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPolymarketGammaMarket(reader));
		}

		return results;
	}

	public async Task<IReadOnlyList<PolymarketGammaMarket>> GetCryptoUpDown5mGammaMarketsEndingBetweenAsync(IReadOnlyCollection<string> assetSymbols, DateTimeOffset endAfterUtc, DateTimeOffset endBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToLowerInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedSymbols.Length == 0 || endBeforeUtc < endAfterUtc)
		{
			return [];
		}

		var marketStartAfterUtc = endAfterUtc.Subtract(TimeSpan.FromMinutes(5));
		var marketStartBeforeUtc = endBeforeUtc.Subtract(TimeSpan.FromMinutes(5));
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "WITH requested_assets AS (\n    SELECT unnest(@AssetSymbols::text[]) AS asset_symbol\n)\nSELECT " + PolymarketGammaMarketSelectColumns + "\nFROM polymarket_gamma_markets\nWHERE active\n  AND NOT archived\n  AND EXISTS (\n      SELECT 1\n      FROM requested_assets asset\n      WHERE lower(slug) ~ ('^' || asset.asset_symbol || '-updown-5m-[0-9]+$')\n         OR lower(COALESCE(event_slug, '')) ~ ('^' || asset.asset_symbol || '-updown-5m-[0-9]+$')\n         OR lower(COALESCE(series_slug, '')) = asset.asset_symbol || '-up-or-down-5m'\n  )\n  AND (\n      (\n          event_start_time_utc IS NOT NULL\n          AND event_start_time_utc >= @MarketStartAfterUtc\n          AND event_start_time_utc <= @MarketStartBeforeUtc\n      )\n      OR (\n          event_start_time_utc IS NULL\n          AND end_date_utc >= @EndAfterUtc\n          AND end_date_utc <= @EndBeforeUtc\n      )\n  )\nORDER BY COALESCE(event_start_time_utc + interval '5 minutes', end_date_utc) ASC,\n         market_id ASC\nLIMIT @Limit;");
		command.Parameters.Add("AssetSymbols", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = normalizedSymbols;
		command.Parameters.Add("EndAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endAfterUtc);
		command.Parameters.Add("EndBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endBeforeUtc);
		command.Parameters.Add("MarketStartAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(marketStartAfterUtc);
		command.Parameters.Add("MarketStartBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(marketStartBeforeUtc);
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
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
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO strategy_market_paper_runs (\n    id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc,\n    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\n)\nSELECT\n    @Id, @StrategyId, @MarketId, @ConditionId, @MarketSlug, @MarketTitle, @Category,\n    @MarketStartUtc, @MarketEndUtc, @DetectedAtUtc, @EntryDueAtUtc, @Status,\n    @SelectedAssetId, @SelectedOutcome, @EntryPrice, @StakeUsd, @SizeShares,\n    @SignalId, @PaperOrderId, @EnteredAtUtc, @SettlementPrice, @SettlementValueUsd,\n    @RealizedPnlUsd, @SettledAtUtc, @SkipReason, CAST(@SkipDiagnosticsJson AS jsonb), @CreatedAtUtc, @UpdatedAtUtc,\n    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,\n    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd\nWHERE NOT EXISTS (SELECT 1 FROM strategy_market_paper_skip_tombstones WHERE archived_run_id = @Id)\n  AND NOT EXISTS (SELECT 1 FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId AND market_id = @MarketId)\n  AND NOT EXISTS (SELECT 1 FROM strategy_market_paper_skip_tombstones_v2 WHERE archived_run_id = @Id)\n  AND NOT EXISTS (\n      SELECT 1\n      FROM strategy_skip_archive_market_identities market_identity\n      INNER JOIN strategy_market_paper_skip_tombstones_v2 tombstone\n          ON tombstone.strategy_id = @StrategyId\n         AND tombstone.market_identity_id = market_identity.market_identity_id\n      WHERE market_identity.market_id = @MarketId COLLATE \"C\")\nON CONFLICT (strategy_id, market_id) DO NOTHING;");
		AddStrategyMarketPaperRunParameters(command, run);
		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	public async Task<IReadOnlySet<Guid>> TryAddStrategyMarketPaperRunsAsync(
		IReadOnlyList<StrategyMarketPaperRun> runs,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (runs.Count == 0)
		{
			return new HashSet<Guid>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		var insertedIds = new HashSet<Guid>();
		for (var offset = 0; offset < runs.Count; offset += StrategyMarketPaperRunInsertBatchSize)
		{
			var count = Math.Min(StrategyMarketPaperRunInsertBatchSize, runs.Count - offset);
			var batch = new StrategyMarketPaperRun[count];
			for (var index = 0; index < count; index++)
			{
				var run = runs[offset + index];
				batch[index] = run with
				{
					StrategyId = StrategyIds.Normalize(run.StrategyId),
					SkipDiagnosticsJson = GetPersistedSkipDiagnosticsJson(run)
				};
			}

			await using NpgsqlCommand command = CreateCommand(connection, """
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
        skip_diagnostics_json text,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
)
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
)
SELECT
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, CAST(skip_diagnostics_json AS jsonb), created_at_utc, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
FROM run_rows
WHERE NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.archived_run_id = run_rows.id)
AND NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.strategy_id = run_rows.strategy_id
      AND tombstone.market_id = run_rows.market_id)
AND NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones_v2 tombstone
    WHERE tombstone.archived_run_id = run_rows.id)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_skip_archive_market_identities market_identity
    INNER JOIN strategy_market_paper_skip_tombstones_v2 tombstone
        ON tombstone.strategy_id = run_rows.strategy_id
       AND tombstone.market_identity_id = market_identity.market_identity_id
    WHERE market_identity.market_id = run_rows.market_id COLLATE "C")
ON CONFLICT (strategy_id, market_id) DO NOTHING
RETURNING id;
""");
			command.Parameters.Add("RunsJson", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(batch, BulkInsertJsonOptions);
			await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				insertedIds.Add(reader.GetGuid(0));
			}
		}

		return insertedIds;
	}

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(Guid strategyId, string status, DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\n  AND entry_due_at_utc <= @DueBeforeUtc\nORDER BY entry_due_at_utc ASC, detected_at_utc ASC\nLIMIT @Limit;");
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(IReadOnlyCollection<Guid> strategyIds, string status, DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (strategyIds.Count == 0 || limit <= 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		Guid[] normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT run.id, run.strategy_id, run.market_id, run.condition_id, run.market_slug, run.market_title, run.category,\n       run.market_start_utc, run.market_end_utc, run.detected_at_utc, run.entry_due_at_utc, run.status,\n       run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd, run.size_shares,\n       run.signal_id, run.paper_order_id, run.entered_at_utc, run.settlement_price, run.settlement_value_usd,\n       run.realized_pnl_usd, run.settled_at_utc, run.skip_reason, run.created_at_utc, run.updated_at_utc,\n       run.skip_diagnostics_json::text,\n       run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source, run.fee_rate,\n       run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd\nFROM strategy_market_paper_runs run\nINNER JOIN strategies strategy ON strategy.id = run.strategy_id\nWHERE run.strategy_id = ANY(@StrategyIds)\n  AND run.status = @Status\n  AND run.entry_due_at_utc <= @DueBeforeUtc\nORDER BY run.entry_due_at_utc ASC, strategy.live_stakes DESC, run.detected_at_utc ASC, run.strategy_id ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsWithExpandedLastDueAsync(IReadOnlyCollection<Guid> strategyIds, string status, DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (strategyIds.Count == 0 || limit <= 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		Guid[] normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "WITH ordered_runs AS (\n    SELECT run.id, run.strategy_id, run.market_id, run.condition_id, run.market_slug, run.market_title, run.category,\n           run.market_start_utc, run.market_end_utc, run.detected_at_utc, run.entry_due_at_utc, run.status,\n           run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd, run.size_shares,\n           run.signal_id, run.paper_order_id, run.entered_at_utc, run.settlement_price, run.settlement_value_usd,\n           run.realized_pnl_usd, run.settled_at_utc, run.skip_reason, run.created_at_utc, run.updated_at_utc,\n           run.skip_diagnostics_json::text AS skip_diagnostics_json,\n           run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source, run.fee_rate,\n           run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd,\n           row_number() OVER (\n               ORDER BY run.entry_due_at_utc ASC,\n                        strategy.live_stakes DESC,\n                        run.detected_at_utc ASC,\n                        run.strategy_id ASC\n           ) AS due_row_number\n    FROM strategy_market_paper_runs run\n    INNER JOIN strategies strategy ON strategy.id = run.strategy_id\n    WHERE run.strategy_id = ANY(@StrategyIds)\n      AND run.status = @Status\n      AND run.entry_due_at_utc <= @DueBeforeUtc\n), cutoff AS (\n    SELECT entry_due_at_utc\n    FROM ordered_runs\n    WHERE due_row_number = @Limit\n)\nSELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM ordered_runs\nWHERE due_row_number <= @Limit\n   OR ((SELECT entry_due_at_utc FROM cutoff) IS NOT NULL\n       AND entry_due_at_utc = (SELECT entry_due_at_utc FROM cutoff))\nORDER BY due_row_number ASC;");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAtEarliestDueAsync(IReadOnlyCollection<Guid> strategyIds, string status, DateTimeOffset dueBeforeUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (strategyIds.Count == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		Guid[] normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "WITH earliest_due AS (\n    SELECT min(entry_due_at_utc) AS entry_due_at_utc\n    FROM strategy_market_paper_runs\n    WHERE strategy_id = ANY(@StrategyIds)\n      AND status = @Status\n      AND entry_due_at_utc <= @DueBeforeUtc\n)\nSELECT run.id, run.strategy_id, run.market_id, run.condition_id, run.market_slug, run.market_title, run.category,\n       run.market_start_utc, run.market_end_utc, run.detected_at_utc, run.entry_due_at_utc, run.status,\n       run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd, run.size_shares,\n       run.signal_id, run.paper_order_id, run.entered_at_utc, run.settlement_price, run.settlement_value_usd,\n       run.realized_pnl_usd, run.settled_at_utc, run.skip_reason, run.created_at_utc, run.updated_at_utc,\n       run.skip_diagnostics_json::text,\n       run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source, run.fee_rate,\n       run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd\nFROM strategy_market_paper_runs run\nINNER JOIN strategies strategy ON strategy.id = run.strategy_id\nCROSS JOIN earliest_due due\nWHERE run.strategy_id = ANY(@StrategyIds)\n  AND run.status = @Status\n  AND due.entry_due_at_utc IS NOT NULL\n  AND run.entry_due_at_utc = due.entry_due_at_utc\nORDER BY run.entry_due_at_utc ASC, strategy.live_stakes DESC, run.detected_at_utc ASC, run.strategy_id ASC;");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
		command.Parameters.AddWithValue("Status", status);
		command.Parameters.Add("DueBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(dueBeforeUtc);
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
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\n  AND market_end_utc IS NOT NULL\n  AND market_end_utc <= @MarketEndedBeforeUtc\nORDER BY market_end_utc ASC, entered_at_utc ASC\nLIMIT @Limit;");
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(IReadOnlyCollection<Guid> strategyIds, DateTimeOffset marketEndedBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT run.id, run.strategy_id, run.market_id, run.condition_id, run.market_slug, run.market_title, run.category,\n       run.market_start_utc, run.market_end_utc, run.detected_at_utc, run.entry_due_at_utc, run.status,\n       run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd, run.size_shares,\n       run.signal_id, run.paper_order_id, run.entered_at_utc, run.settlement_price, run.settlement_value_usd,\n       run.realized_pnl_usd, run.settled_at_utc, run.skip_reason, run.created_at_utc, run.updated_at_utc,\n       run.skip_diagnostics_json::text,\n       run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source, run.fee_rate,\n       run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd\nFROM strategy_market_paper_runs run\nINNER JOIN strategies strategy ON strategy.id = run.strategy_id\nLEFT JOIN paper_orders paper_order ON paper_order.id = run.paper_order_id\nLEFT JOIN LATERAL (\n    SELECT 1 AS has_fill\n    FROM paper_fills fill_row\n    WHERE fill_row.paper_order_id = run.paper_order_id\n    LIMIT 1\n) fill_row ON true\nWHERE run.strategy_id = ANY(@StrategyIds)\n  AND run.status = @Status\n  AND run.market_end_utc IS NOT NULL\n  AND run.market_end_utc <= @MarketEndedBeforeUtc\nORDER BY\n  CASE\n    WHEN fill_row.has_fill IS NOT NULL THEN 0\n    WHEN paper_order.status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired') THEN 1\n    WHEN paper_order.status = 'Expired' THEN 2\n    WHEN paper_order.id IS NULL THEN 3\n    ELSE 4\n  END ASC,\n  run.market_end_utc ASC,\n  run.entered_at_utc ASC,\n  strategy.live_stakes DESC,\n  run.detected_at_utc ASC,\n  run.strategy_id ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetPreOpenSellExitDueRunsAsync(IReadOnlyCollection<Guid> strategyIds, DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0 || limit <= 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT run.id, run.strategy_id, run.market_id, run.condition_id, run.market_slug, run.market_title, run.category,\n       run.market_start_utc, run.market_end_utc, run.detected_at_utc, run.entry_due_at_utc, run.status,\n       run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd, run.size_shares,\n       run.signal_id, run.paper_order_id, run.entered_at_utc, run.settlement_price, run.settlement_value_usd,\n       run.realized_pnl_usd, run.settled_at_utc, run.skip_reason, run.created_at_utc, run.updated_at_utc,\n       run.skip_diagnostics_json::text,\n       run.fee_usd, run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source, run.fee_rate,\n       run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc, run.net_realized_pnl_usd\nFROM strategy_market_paper_runs run\nINNER JOIN strategies strategy ON strategy.id = run.strategy_id\nWHERE run.strategy_id = ANY(@StrategyIds)\n  AND run.status = @Status\n  AND run.market_start_utc IS NOT NULL\n  AND run.market_end_utc IS NOT NULL\n  AND run.market_start_utc < run.market_end_utc\n  AND run.market_end_utc > @DueBeforeUtc\n  AND run.market_start_utc + ((run.market_end_utc - run.market_start_utc) * 0.75) <= @DueBeforeUtc\nORDER BY run.market_end_utc ASC, run.entered_at_utc ASC, strategy.live_stakes DESC, run.detected_at_utc ASC, run.strategy_id ASC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
		command.Parameters.AddWithValue("Status", StrategyMarketPaperRunStatuses.Entered);
		command.Parameters.Add("DueBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(dueBeforeUtc);
		command.Parameters.AddWithValue("Limit", limit);
		List<StrategyMarketPaperRun> results = [];
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
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM strategy_market_paper_runs\nWHERE strategy_id = @StrategyId\n  AND status = @Status\nORDER BY COALESCE(settled_at_utc, entered_at_utc, updated_at_utc) DESC,\n         COALESCE(market_start_utc, entry_due_at_utc, detected_at_utc) DESC\nLIMIT @Limit;");
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
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE strategy_market_paper_runs\nSET strategy_id = @StrategyId,\n    market_id = @MarketId,\n    condition_id = @ConditionId,\n    market_slug = @MarketSlug,\n    market_title = @MarketTitle,\n    category = @Category,\n    market_start_utc = @MarketStartUtc,\n    market_end_utc = @MarketEndUtc,\n    detected_at_utc = @DetectedAtUtc,\n    entry_due_at_utc = @EntryDueAtUtc,\n    status = @Status,\n    selected_asset_id = @SelectedAssetId,\n    selected_outcome = @SelectedOutcome,\n    entry_price = @EntryPrice,\n    stake_usd = @StakeUsd,\n    size_shares = @SizeShares,\n    signal_id = @SignalId,\n    paper_order_id = @PaperOrderId,\n    entered_at_utc = @EnteredAtUtc,\n    settlement_price = @SettlementPrice,\n    settlement_value_usd = @SettlementValueUsd,\n    realized_pnl_usd = @RealizedPnlUsd,\n    settled_at_utc = @SettledAtUtc,\n    skip_reason = @SkipReason,\n    skip_diagnostics_json = CAST(@SkipDiagnosticsJson AS jsonb),\n    created_at_utc = @CreatedAtUtc,\n    updated_at_utc = @UpdatedAtUtc,\n    fee_usd = @FeeUsd,\n    fee_accounting_status = @FeeAccountingStatus,\n    fee_liquidity_role = @FeeLiquidityRole,\n    fee_calculation_source = @FeeCalculationSource,\n    fee_rate = @FeeRate,\n    fee_exponent = @FeeExponent,\n    fee_taker_only = @FeeTakerOnly,\n    fee_calculated_at_utc = @FeeCalculatedAtUtc,\n    net_realized_pnl_usd = @NetRealizedPnlUsd\nWHERE id = @Id;");
		AddStrategyMarketPaperRunParameters(command, run);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpdateStrategyMarketPaperRunsAsync(IReadOnlyList<StrategyMarketPaperRun> runs, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (runs.Count == 0)
		{
			return;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		foreach (StrategyMarketPaperRun run in runs)
		{
			await using NpgsqlCommand command = CreateCommand(connection, "UPDATE strategy_market_paper_runs\nSET strategy_id = @StrategyId,\n    market_id = @MarketId,\n    condition_id = @ConditionId,\n    market_slug = @MarketSlug,\n    market_title = @MarketTitle,\n    category = @Category,\n    market_start_utc = @MarketStartUtc,\n    market_end_utc = @MarketEndUtc,\n    detected_at_utc = @DetectedAtUtc,\n    entry_due_at_utc = @EntryDueAtUtc,\n    status = @Status,\n    selected_asset_id = @SelectedAssetId,\n    selected_outcome = @SelectedOutcome,\n    entry_price = @EntryPrice,\n    stake_usd = @StakeUsd,\n    size_shares = @SizeShares,\n    signal_id = @SignalId,\n    paper_order_id = @PaperOrderId,\n    entered_at_utc = @EnteredAtUtc,\n    settlement_price = @SettlementPrice,\n    settlement_value_usd = @SettlementValueUsd,\n    realized_pnl_usd = @RealizedPnlUsd,\n    settled_at_utc = @SettledAtUtc,\n    skip_reason = @SkipReason,\n    skip_diagnostics_json = CAST(@SkipDiagnosticsJson AS jsonb),\n    created_at_utc = @CreatedAtUtc,\n    updated_at_utc = @UpdatedAtUtc,\n    fee_usd = @FeeUsd,\n    fee_accounting_status = @FeeAccountingStatus,\n    fee_liquidity_role = @FeeLiquidityRole,\n    fee_calculation_source = @FeeCalculationSource,\n    fee_rate = @FeeRate,\n    fee_exponent = @FeeExponent,\n    fee_taker_only = @FeeTakerOnly,\n    fee_calculated_at_utc = @FeeCalculatedAtUtc,\n    net_realized_pnl_usd = @NetRealizedPnlUsd\nWHERE id = @Id;");
			command.Transaction = transaction;
			AddStrategyMarketPaperRunParameters(command, run);
			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
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
		command.Parameters.Add("RawContextJson", NpgsqlDbType.Text).Value = DBNull.Value;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<SignalSummary> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<SignalSummary> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "WITH recent_signals AS MATERIALIZED (\n    SELECT id, trader_wallet, condition_id, asset_id, outcome, leader_price,\n           best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score,\n           accepted, decision, proposed_paper_price, proposed_size_shares,\n           proposed_notional_usd, created_at_utc\n    FROM signals\n    ORDER BY created_at_utc DESC, id DESC\n    LIMIT @Limit\n), rejection_codes AS (\n    SELECT sr.signal_id, string_agg(sr.reason_code, ',' ORDER BY sr.created_at_utc) AS reason_codes\n    FROM signal_rejections sr\n    INNER JOIN recent_signals recent ON recent.id = sr.signal_id\n    GROUP BY sr.signal_id\n)\nSELECT s.id, s.trader_wallet, s.condition_id, s.asset_id, s.outcome, s.leader_price,\n       s.best_bid, s.best_ask, s.spread_abs, s.spread_pct, s.lag_seconds, s.score,\n       s.accepted, s.decision, s.proposed_paper_price, s.proposed_size_shares,\n       s.proposed_notional_usd, s.created_at_utc,\n       COALESCE(rejection.reason_codes, '') AS reason_codes\nFROM recent_signals s\nLEFT JOIN rejection_codes rejection ON rejection.signal_id = s.id\nORDER BY s.created_at_utc DESC, s.id DESC;"))
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

	public async Task AddSignalAndPaperOrderAsync(Signal signal, PaperOrder paperOrder, CancellationToken cancellationToken = default(CancellationToken))
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

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task AddPaperEntryPersistenceBatchAsync(PaperEntryPersistenceBatch batch, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (batch.IsEmpty)
		{
			return;
		}
		var paperOrderIds = batch.PaperOrders.Select(order => order.Id).ToHashSet();
		if (batch.PaperFills.Any(fill => !paperOrderIds.Contains(fill.PaperOrderId)))
		{
			throw new ArgumentException(
				"Every paper fill in an entry persistence batch must reference an order from the same batch.",
				nameof(batch));
		}
		if (batch.CopiedLeaderPositionActivations.Any(activation =>
			!paperOrderIds.Contains(activation.EntryPaperOrderId)))
		{
			throw new ArgumentException(
				"Every copied-leader activation in an entry persistence batch must reference an order from the same batch.",
				nameof(batch));
		}
		if (batch.DirectPaperSkipCompactionEnabled && ContainsSkippedRun(batch.StrategyRuns))
		{
			await AddPaperEntryPersistenceBatchWithDirectCompactionAsync(batch, cancellationToken);
			return;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await LockPaperPositionKeysAsync(
			connection,
			transaction,
			batch.PaperPositions,
			batch.PaperOrders.Select(order => order.CopiedTraderWallet).ToArray(),
			cancellationToken);
		await AddSignalsBatchAsync(connection, transaction, batch.Signals, cancellationToken);
		await UpsertPaperPositionsBatchAsync(connection, transaction, batch.PaperPositions, cancellationToken);
		await AddPaperOrdersBatchAsync(connection, transaction, batch.PaperOrders, cancellationToken);
		await AddPaperFillsBatchAsync(connection, transaction, batch.PaperFills, cancellationToken);
		await ActivatePaperCopiedLeaderPositionsBatchAsync(connection, transaction, batch.CopiedLeaderPositionActivations, cancellationToken);
		await UpdateStrategyMarketPaperRunsBatchAsync(connection, transaction, batch.StrategyRuns, cancellationToken);

		await transaction.CommitAsync(cancellationToken);
	}

	private static string PrepareSignalsJson(IReadOnlyList<Signal> signals)
	{
		var rows = signals.Select(signal => new
		{
			id = signal.Id,
			trader_wallet = signal.LeaderTrade.TraderWallet,
			condition_id = signal.LeaderTrade.ConditionId,
			asset_id = signal.LeaderTrade.AssetId,
			outcome = signal.LeaderTrade.Outcome,
			leader_price = signal.LeaderTrade.Price,
			score = signal.Score,
			decision = signal.DecisionCode,
			accepted = signal.Accepted,
			proposed_paper_price = signal.ProposedPaperPrice,
			proposed_size_shares = signal.ProposedSizeShares,
			proposed_notional_usd = signal.ProposedNotionalUsd,
			created_at_utc = UtcDateTime(signal.CreatedAtUtc),
			raw_context_json = (string?)null
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task AddSignalsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<Signal> signals,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (signals.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO signals (
    id, leader_trade_id, trader_wallet, condition_id, asset_id, outcome, leader_price,
    best_bid, best_ask, spread_abs, spread_pct, lag_seconds, score, decision,
    accepted, proposed_paper_price, proposed_size_shares, proposed_notional_usd, created_at_utc, raw_context_json
)
SELECT
    signal.id, NULL, signal.trader_wallet, signal.condition_id, signal.asset_id, signal.outcome, signal.leader_price,
    NULL, NULL, NULL, NULL, NULL, signal.score, signal.decision,
    signal.accepted, signal.proposed_paper_price, signal.proposed_size_shares, signal.proposed_notional_usd,
    signal.created_at_utc, CAST(signal.raw_context_json AS jsonb)
FROM jsonb_to_recordset(CAST(@SignalsJson AS jsonb)) AS signal(
    id uuid,
    trader_wallet text,
    condition_id text,
    asset_id text,
    outcome text,
    leader_price numeric,
    score integer,
    decision text,
    accepted boolean,
    proposed_paper_price numeric,
    proposed_size_shares numeric,
    proposed_notional_usd numeric,
    created_at_utc timestamptz,
    raw_context_json text
);
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "SignalsJson", preparedJson ?? PrepareSignalsJson(signals));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string PreparePaperOrdersJson(IReadOnlyList<PaperOrder> orders)
	{
		var rows = orders.Select(order => new
		{
			id = order.Id,
			signal_id = order.SignalId,
			strategy_id = StrategyIds.Normalize(order.StrategyId),
			copied_trader_wallet = order.CopiedTraderWallet,
			status = order.Status.ToString(),
			side = order.Side.ToString(),
			asset_id = order.AssetId,
			condition_id = order.ConditionId,
			outcome = order.Outcome,
			price = order.Price,
			size_shares = order.SizeShares,
			notional_usd = order.NotionalUsd,
			created_at_utc = UtcDateTime(order.CreatedAtUtc),
			expires_at_utc = UtcDateTime(order.ExpiresAtUtc),
			filled_at_utc = order.FilledAtUtc.HasValue ? UtcDateTime(order.FilledAtUtc.Value) : (DateTime?)null,
			cancelled_at_utc = order.CancelledAtUtc.HasValue ? UtcDateTime(order.CancelledAtUtc.Value) : (DateTime?)null,
			raw_decision_json = BuildPaperOrderRawDecisionJson(order),
			correlation_id = order.CorrelationId,
			execution_source = order.ExecutionSource ?? string.Empty
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task AddPaperOrdersBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperOrder> orders,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (orders.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id, condition_id, outcome, price,
    size_shares, notional_usd, created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc,
    raw_decision_json, correlation_id, execution_source
)
SELECT
    paper_order.id, paper_order.signal_id, paper_order.strategy_id, paper_order.copied_trader_wallet,
    paper_order.status, paper_order.side, paper_order.asset_id, paper_order.condition_id, paper_order.outcome,
    paper_order.price, paper_order.size_shares, paper_order.notional_usd, paper_order.created_at_utc,
    paper_order.expires_at_utc, paper_order.filled_at_utc, paper_order.cancelled_at_utc,
    CAST(paper_order.raw_decision_json AS jsonb), paper_order.correlation_id, paper_order.execution_source
FROM jsonb_to_recordset(CAST(@PaperOrdersJson AS jsonb)) AS paper_order(
    id uuid,
    signal_id uuid,
    strategy_id uuid,
    copied_trader_wallet text,
    status text,
    side text,
    asset_id text,
    condition_id text,
    outcome text,
    price numeric,
    size_shares numeric,
    notional_usd numeric,
    created_at_utc timestamptz,
    expires_at_utc timestamptz,
    filled_at_utc timestamptz,
    cancelled_at_utc timestamptz,
    raw_decision_json text,
    correlation_id uuid,
    execution_source text
)
ORDER BY
    paper_order.copied_trader_wallet COLLATE "C",
    paper_order.asset_id COLLATE "C",
    paper_order.id;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "PaperOrdersJson", preparedJson ?? PreparePaperOrdersJson(orders));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string PreparePaperFillsJson(IReadOnlyList<PaperFill> fills)
	{
		var rows = fills.Select(fill => new
		{
			id = fill.Id,
			paper_order_id = fill.PaperOrderId,
			price = fill.Price,
			size_shares = fill.SizeShares,
			filled_at_utc = UtcDateTime(fill.FilledAtUtc),
			evidence = fill.Evidence,
			realized_pnl_usd = fill.RealizedPnlUsd,
			fee_usd = fill.FeeUsd,
			fee_accounting_status = fill.FeeAccountingStatus,
			fee_liquidity_role = fill.FeeLiquidityRole,
			fee_calculation_source = fill.FeeCalculationSource,
			fee_rate = fill.FeeRate,
			fee_exponent = fill.FeeExponent,
			fee_taker_only = fill.FeeTakerOnly,
			fee_calculated_at_utc = fill.FeeCalculatedAtUtc.HasValue ? UtcDateTime(fill.FeeCalculatedAtUtc.Value) : (DateTime?)null,
			net_realized_pnl_usd = fill.NetRealizedPnlUsd
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task AddPaperFillsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperFill> fills,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (fills.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
)
SELECT
    fill.id, fill.paper_order_id, fill.price, fill.size_shares, fill.filled_at_utc, fill.evidence, fill.realized_pnl_usd,
    fill.fee_usd, fill.fee_accounting_status, fill.fee_liquidity_role, fill.fee_calculation_source, fill.fee_rate,
    fill.fee_exponent, fill.fee_taker_only, fill.fee_calculated_at_utc, fill.net_realized_pnl_usd
FROM jsonb_to_recordset(CAST(@PaperFillsJson AS jsonb)) AS fill(
    id uuid,
    paper_order_id uuid,
    price numeric,
    size_shares numeric,
    filled_at_utc timestamptz,
    evidence text,
    realized_pnl_usd numeric,
    fee_usd numeric,
    fee_accounting_status text,
    fee_liquidity_role text,
    fee_calculation_source text,
    fee_rate numeric,
    fee_exponent integer,
    fee_taker_only boolean,
    fee_calculated_at_utc timestamptz,
    net_realized_pnl_usd numeric
)
ORDER BY
    (
        SELECT paper_order.copied_trader_wallet COLLATE "C"
        FROM paper_orders paper_order
        WHERE paper_order.id = fill.paper_order_id
    ),
    fill.paper_order_id,
    fill.id;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "PaperFillsJson", preparedJson ?? PreparePaperFillsJson(fills));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string PreparePaperPositionsJson(IReadOnlyList<PaperPosition> positions)
	{
		var rows = positions.Select(position => new
		{
			id = Guid.NewGuid(),
			copied_trader_wallet = position.CopiedTraderWallet,
			asset_id = position.AssetId,
			condition_id = position.ConditionId,
			outcome = position.Outcome,
			size_shares = position.SizeShares,
			average_price = position.AveragePrice,
			estimated_value_usd = position.EstimatedValueUsd,
			unrealized_pnl_usd = position.UnrealizedPnlUsd,
			fee_usd = position.FeeUsd,
			fee_accounting_status = position.FeeAccountingStatus,
			fee_liquidity_role = position.FeeLiquidityRole,
			fee_calculation_source = position.FeeCalculationSource,
			fee_rate = position.FeeRate,
			fee_exponent = position.FeeExponent,
			fee_taker_only = position.FeeTakerOnly,
			fee_calculated_at_utc = position.FeeCalculatedAtUtc.HasValue ? UtcDateTime(position.FeeCalculatedAtUtc.Value) : (DateTime?)null,
			net_unrealized_pnl_usd = position.NetUnrealizedPnlUsd,
			updated_at_utc = UtcDateTime(position.UpdatedAtUtc)
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task UpsertPaperPositionsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperPosition> positions,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (positions.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,
    estimated_value_usd, unrealized_pnl_usd, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd
)
SELECT
    position.id, position.copied_trader_wallet, position.asset_id, position.condition_id, position.outcome,
    position.size_shares, position.average_price, position.estimated_value_usd, position.unrealized_pnl_usd,
    position.updated_at_utc,
    position.fee_usd, position.fee_accounting_status, position.fee_liquidity_role,
    position.fee_calculation_source, position.fee_rate, position.fee_exponent,
    position.fee_taker_only, position.fee_calculated_at_utc, position.net_unrealized_pnl_usd
FROM jsonb_to_recordset(CAST(@PaperPositionsJson AS jsonb)) AS position(
    id uuid,
    copied_trader_wallet text,
    asset_id text,
    condition_id text,
    outcome text,
    size_shares numeric,
    average_price numeric,
    estimated_value_usd numeric,
    unrealized_pnl_usd numeric,
    updated_at_utc timestamptz,
    fee_usd numeric,
    fee_accounting_status text,
    fee_liquidity_role text,
    fee_calculation_source text,
    fee_rate numeric,
    fee_exponent integer,
    fee_taker_only boolean,
    fee_calculated_at_utc timestamptz,
    net_unrealized_pnl_usd numeric
)
ORDER BY
    position.copied_trader_wallet COLLATE "C",
    position.asset_id COLLATE "C",
    position.id
ON CONFLICT (copied_trader_wallet, asset_id) DO UPDATE SET
    condition_id = excluded.condition_id,
    outcome = excluded.outcome,
    size_shares = excluded.size_shares,
    average_price = excluded.average_price,
    estimated_value_usd = excluded.estimated_value_usd,
    unrealized_pnl_usd = excluded.unrealized_pnl_usd,
    fee_usd = excluded.fee_usd,
    fee_accounting_status = excluded.fee_accounting_status,
    fee_liquidity_role = excluded.fee_liquidity_role,
    fee_calculation_source = excluded.fee_calculation_source,
    fee_rate = excluded.fee_rate,
    fee_exponent = excluded.fee_exponent,
    fee_taker_only = excluded.fee_taker_only,
    fee_calculated_at_utc = excluded.fee_calculated_at_utc,
    net_unrealized_pnl_usd = excluded.net_unrealized_pnl_usd,
    updated_at_utc = excluded.updated_at_utc;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "PaperPositionsJson", preparedJson ?? PreparePaperPositionsJson(positions));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task LockPaperPositionKeysAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperPosition> positions,
		IReadOnlyCollection<string> additionalWallets,
		CancellationToken cancellationToken)
	{
		if (positions.Count == 0 && additionalWallets.Count == 0)
		{
			return;
		}

		var keys = positions
			.Select(position => new
			{
				copied_trader_wallet = position.CopiedTraderWallet,
				asset_id = position.AssetId
			})
			.Distinct()
			.ToArray();
		var wallets = positions
			.Select(position => position.CopiedTraderWallet)
			.Concat(additionalWallets)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		var keysJson = JsonSerializer.Serialize(keys);
		await LockPaperWalletsAsync(connection, transaction, wallets, cancellationToken);
		if (positions.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
WITH requested_position_keys AS (
    SELECT position_key.copied_trader_wallet, position_key.asset_id
    FROM jsonb_to_recordset(CAST(@PaperPositionKeysJson AS jsonb)) AS position_key(
        copied_trader_wallet text,
        asset_id text
    )
)
SELECT target_position.id
FROM paper_positions target_position
INNER JOIN requested_position_keys requested_key
    ON requested_key.copied_trader_wallet = target_position.copied_trader_wallet
   AND requested_key.asset_id = target_position.asset_id
ORDER BY
    target_position.copied_trader_wallet COLLATE "C",
    target_position.asset_id COLLATE "C"
FOR UPDATE OF target_position;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "PaperPositionKeysJson", keysJson);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
		}
	}

	private static async Task LockPaperWalletsAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyCollection<string> wallets,
		CancellationToken cancellationToken)
	{
		if (wallets.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand walletLockCommand = CreateCommand(connection, """
WITH wallet_lock_keys AS (
    SELECT DISTINCT hashtextextended(copied_trader_wallet, 4937427318840178337) AS lock_key
    FROM jsonb_array_elements_text(CAST(@PaperWalletsJson AS jsonb)) AS wallet(copied_trader_wallet)
)
SELECT lock_key, pg_advisory_xact_lock(lock_key)
FROM wallet_lock_keys
ORDER BY lock_key;
""");
		walletLockCommand.Transaction = transaction;
		AddJsonbParameter(
			walletLockCommand,
			"PaperWalletsJson",
			JsonSerializer.Serialize(wallets.Distinct(StringComparer.Ordinal)));
		await using NpgsqlDataReader walletLockReader =
			await walletLockCommand.ExecuteReaderAsync(cancellationToken);
		while (await walletLockReader.ReadAsync(cancellationToken))
		{
		}
	}

	private static string PrepareActivationsJson(IReadOnlyList<PaperCopiedLeaderPositionActivation> activations)
	{
		var rows = activations.Select(activation => new
		{
			entry_paper_order_id = activation.EntryPaperOrderId,
			copied_initial_size_shares = activation.CopiedInitialSizeShares,
			filled_at_utc = UtcDateTime(activation.FilledAtUtc)
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task ActivatePaperCopiedLeaderPositionsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperCopiedLeaderPositionActivation> activations,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (activations.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
WITH activation_rows AS (
    SELECT
        activation.entry_paper_order_id,
        SUM(activation.copied_initial_size_shares) AS copied_initial_size_shares,
        MIN(activation.filled_at_utc) AS first_filled_at_utc,
        MAX(activation.filled_at_utc) AS last_filled_at_utc
    FROM jsonb_to_recordset(CAST(@ActivationsJson AS jsonb)) AS activation(
        entry_paper_order_id uuid,
        copied_initial_size_shares numeric,
        filled_at_utc timestamptz
    )
    GROUP BY activation.entry_paper_order_id
)
UPDATE paper_copied_leader_positions position
SET status = 'Active',
    copied_initial_size_shares = CASE
        WHEN position.status = 'Active' THEN position.copied_initial_size_shares + activation_rows.copied_initial_size_shares
        ELSE activation_rows.copied_initial_size_shares
    END,
    next_activity_sync_at_utc = LEAST(position.next_activity_sync_at_utc, activation_rows.first_filled_at_utc),
    updated_at_utc = activation_rows.last_filled_at_utc
FROM activation_rows
WHERE position.entry_paper_order_id = activation_rows.entry_paper_order_id
  AND position.status IN ('PendingEntry', 'Active');
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "ActivationsJson", preparedJson ?? PrepareActivationsJson(activations));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static string PrepareStrategyRunsJson(IReadOnlyList<StrategyMarketPaperRun> runs)
	{
		var rows = runs.Select(run => new
		{
			id = run.Id,
			strategy_id = StrategyIds.Normalize(run.StrategyId),
			market_id = run.MarketId,
			condition_id = run.ConditionId,
			market_slug = run.MarketSlug,
			market_title = run.MarketTitle,
			category = run.Category,
			market_start_utc = run.MarketStartUtc.HasValue ? UtcDateTime(run.MarketStartUtc.Value) : (DateTime?)null,
			market_end_utc = run.MarketEndUtc.HasValue ? UtcDateTime(run.MarketEndUtc.Value) : (DateTime?)null,
			detected_at_utc = UtcDateTime(run.DetectedAtUtc),
			entry_due_at_utc = UtcDateTime(run.EntryDueAtUtc),
			status = run.Status,
			selected_asset_id = run.SelectedAssetId,
			selected_outcome = run.SelectedOutcome,
			entry_price = run.EntryPrice,
			stake_usd = run.StakeUsd,
			size_shares = run.SizeShares,
			signal_id = run.SignalId,
			paper_order_id = run.PaperOrderId,
			entered_at_utc = run.EnteredAtUtc.HasValue ? UtcDateTime(run.EnteredAtUtc.Value) : (DateTime?)null,
			settlement_price = run.SettlementPrice,
			settlement_value_usd = run.SettlementValueUsd,
			realized_pnl_usd = run.RealizedPnlUsd,
			settled_at_utc = run.SettledAtUtc.HasValue ? UtcDateTime(run.SettledAtUtc.Value) : (DateTime?)null,
			skip_reason = run.SkipReason,
			skip_diagnostics_json = GetPersistedSkipDiagnosticsJson(run),
			created_at_utc = UtcDateTime(run.CreatedAtUtc),
			updated_at_utc = UtcDateTime(run.UpdatedAtUtc),
			fee_usd = run.FeeUsd,
			fee_accounting_status = run.FeeAccountingStatus,
			fee_liquidity_role = run.FeeLiquidityRole,
			fee_calculation_source = run.FeeCalculationSource,
			fee_rate = run.FeeRate,
			fee_exponent = run.FeeExponent,
			fee_taker_only = run.FeeTakerOnly,
			fee_calculated_at_utc = run.FeeCalculatedAtUtc.HasValue ? UtcDateTime(run.FeeCalculatedAtUtc.Value) : (DateTime?)null,
			net_realized_pnl_usd = run.NetRealizedPnlUsd
		});
		return JsonSerializer.Serialize(rows);
	}

	private static async Task UpdateStrategyMarketPaperRunsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<StrategyMarketPaperRun> runs,
		CancellationToken cancellationToken,
		string? preparedJson = null)
	{
		if (runs.Count == 0)
		{
			return;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
WITH run_rows AS (
    SELECT *
    FROM jsonb_to_recordset(CAST(@StrategyRunsJson AS jsonb)) AS run(
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
        skip_diagnostics_json text,
        created_at_utc timestamptz,
        updated_at_utc timestamptz,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
),
updated_rows AS (
    UPDATE strategy_market_paper_runs target
    SET strategy_id = run_rows.strategy_id,
        market_id = run_rows.market_id,
        condition_id = run_rows.condition_id,
        market_slug = run_rows.market_slug,
        market_title = run_rows.market_title,
        category = run_rows.category,
        market_start_utc = run_rows.market_start_utc,
        market_end_utc = run_rows.market_end_utc,
        detected_at_utc = run_rows.detected_at_utc,
        entry_due_at_utc = run_rows.entry_due_at_utc,
        status = run_rows.status,
        selected_asset_id = run_rows.selected_asset_id,
        selected_outcome = run_rows.selected_outcome,
        entry_price = run_rows.entry_price,
        stake_usd = run_rows.stake_usd,
        size_shares = run_rows.size_shares,
        signal_id = run_rows.signal_id,
        paper_order_id = run_rows.paper_order_id,
        entered_at_utc = run_rows.entered_at_utc,
        settlement_price = run_rows.settlement_price,
        settlement_value_usd = run_rows.settlement_value_usd,
        realized_pnl_usd = run_rows.realized_pnl_usd,
        settled_at_utc = run_rows.settled_at_utc,
        skip_reason = run_rows.skip_reason,
        skip_diagnostics_json = CAST(run_rows.skip_diagnostics_json AS jsonb),
        created_at_utc = run_rows.created_at_utc,
        updated_at_utc = run_rows.updated_at_utc,
        fee_usd = run_rows.fee_usd,
        fee_accounting_status = run_rows.fee_accounting_status,
        fee_liquidity_role = run_rows.fee_liquidity_role,
        fee_calculation_source = run_rows.fee_calculation_source,
        fee_rate = run_rows.fee_rate,
        fee_exponent = run_rows.fee_exponent,
        fee_taker_only = run_rows.fee_taker_only,
        fee_calculated_at_utc = run_rows.fee_calculated_at_utc,
        net_realized_pnl_usd = run_rows.net_realized_pnl_usd
    FROM run_rows
    WHERE target.id = run_rows.id
    RETURNING target.id
)
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, skip_diagnostics_json, created_at_utc, updated_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
)
SELECT
    run_rows.id, run_rows.strategy_id, run_rows.market_id, run_rows.condition_id, run_rows.market_slug, run_rows.market_title, run_rows.category,
    run_rows.market_start_utc, run_rows.market_end_utc, run_rows.detected_at_utc, run_rows.entry_due_at_utc, run_rows.status,
    run_rows.selected_asset_id, run_rows.selected_outcome, run_rows.entry_price, run_rows.stake_usd, run_rows.size_shares,
    run_rows.signal_id, run_rows.paper_order_id, run_rows.entered_at_utc, run_rows.settlement_price, run_rows.settlement_value_usd,
    run_rows.realized_pnl_usd, run_rows.settled_at_utc, run_rows.skip_reason, CAST(run_rows.skip_diagnostics_json AS jsonb), run_rows.created_at_utc, run_rows.updated_at_utc,
    run_rows.fee_usd, run_rows.fee_accounting_status, run_rows.fee_liquidity_role, run_rows.fee_calculation_source, run_rows.fee_rate,
    run_rows.fee_exponent, run_rows.fee_taker_only, run_rows.fee_calculated_at_utc, run_rows.net_realized_pnl_usd
FROM run_rows
WHERE NOT EXISTS (
    SELECT 1
    FROM updated_rows
    WHERE updated_rows.id = run_rows.id
)
AND NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.archived_run_id = run_rows.id)
AND NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones tombstone
    WHERE tombstone.strategy_id = run_rows.strategy_id
      AND tombstone.market_id = run_rows.market_id)
AND NOT EXISTS (
    SELECT 1 FROM strategy_market_paper_skip_tombstones_v2 tombstone
    WHERE tombstone.archived_run_id = run_rows.id)
AND NOT EXISTS (
    SELECT 1
    FROM strategy_skip_archive_market_identities market_identity
    INNER JOIN strategy_market_paper_skip_tombstones_v2 tombstone
        ON tombstone.strategy_id = run_rows.strategy_id
       AND tombstone.market_identity_id = market_identity.market_identity_id
    WHERE market_identity.market_id = run_rows.market_id COLLATE "C")
ON CONFLICT (strategy_id, market_id) DO UPDATE SET
    condition_id = excluded.condition_id,
    market_slug = excluded.market_slug,
    market_title = excluded.market_title,
    category = excluded.category,
    market_start_utc = excluded.market_start_utc,
    market_end_utc = excluded.market_end_utc,
    detected_at_utc = excluded.detected_at_utc,
    entry_due_at_utc = excluded.entry_due_at_utc,
    status = excluded.status,
    selected_asset_id = excluded.selected_asset_id,
    selected_outcome = excluded.selected_outcome,
    entry_price = excluded.entry_price,
    stake_usd = excluded.stake_usd,
    size_shares = excluded.size_shares,
    signal_id = excluded.signal_id,
    paper_order_id = excluded.paper_order_id,
    entered_at_utc = excluded.entered_at_utc,
    settlement_price = excluded.settlement_price,
    settlement_value_usd = excluded.settlement_value_usd,
    realized_pnl_usd = excluded.realized_pnl_usd,
    settled_at_utc = excluded.settled_at_utc,
    skip_reason = excluded.skip_reason,
    skip_diagnostics_json = excluded.skip_diagnostics_json,
    created_at_utc = excluded.created_at_utc,
    updated_at_utc = excluded.updated_at_utc,
    fee_usd = excluded.fee_usd,
    fee_accounting_status = excluded.fee_accounting_status,
    fee_liquidity_role = excluded.fee_liquidity_role,
    fee_calculation_source = excluded.fee_calculation_source,
    fee_rate = excluded.fee_rate,
    fee_exponent = excluded.fee_exponent,
    fee_taker_only = excluded.fee_taker_only,
    fee_calculated_at_utc = excluded.fee_calculated_at_utc,
    net_realized_pnl_usd = excluded.net_realized_pnl_usd;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "StrategyRunsJson", preparedJson ?? PrepareStrategyRunsJson(runs));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static void AddJsonbParameter(NpgsqlCommand command, string name, string json)
	{
		command.Parameters.Add(name, NpgsqlDbType.Jsonb).Value = json;
	}

	public async Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "UPDATE paper_orders\nSET status = @Status,\n    strategy_id = @StrategyId,\n    filled_at_utc = @FilledAtUtc,\n    cancelled_at_utc = @CancelledAtUtc,\n    price = @Price,\n    size_shares = @SizeShares,\n    notional_usd = @NotionalUsd,\n    raw_decision_json = CAST(@RawDecisionJson AS jsonb),\n    correlation_id = @CorrelationId,\n    execution_source = @ExecutionSource\nWHERE id = @Id;");
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("Status", order.Status.ToString());
		command.Parameters.AddWithValue("Price", order.Price);
		command.Parameters.AddWithValue("SizeShares", order.SizeShares);
		command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
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

	public async Task<IReadOnlyList<PaperOrder>> GetPaperOrdersForStrategyAssetAsync(Guid strategyId, string copiedTraderWallet, string assetId, DateTimeOffset createdAfterUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (limit <= 0 || string.IsNullOrWhiteSpace(assetId))
		{
			return Array.Empty<PaperOrder>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nWHERE strategy_id = @StrategyId\n  AND copied_trader_wallet = @CopiedTraderWallet\n  AND asset_id = @AssetId\n  AND created_at_utc >= @CreatedAfterUtc\nORDER BY created_at_utc DESC\nLIMIT @Limit;");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet ?? string.Empty);
		command.Parameters.AddWithValue("AssetId", assetId);
		command.Parameters.Add("CreatedAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(createdAfterUtc);
		command.Parameters.AddWithValue("Limit", limit);
		List<PaperOrder> results = [];
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperOrder(reader));
		}
		return results;
	}

	public async Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken), Guid? strategyId = null, DateTimeOffset? createdAfterUtc = null)
	{
		if (limit <= 0)
		{
			return Array.Empty<PaperOrder>();
		}

		Guid? normalizedStrategyId = strategyId.HasValue ? StrategyIds.Normalize(strategyId.GetValueOrDefault()) : null;
		List<string> filters = new List<string>();
		if (normalizedStrategyId.HasValue)
		{
			filters.Add("strategy_id = @StrategyId");
		}
		if (createdAfterUtc.HasValue)
		{
			filters.Add("created_at_utc >= @CreatedAfterUtc");
		}
		string filterSql = filters.Count > 0 ? "\nWHERE " + string.Join("\n  AND ", filters) : string.Empty;
		IReadOnlyList<PaperOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + RecentPaperOrderSelectColumns + "\nFROM paper_orders" + filterSql + "\nORDER BY created_at_utc DESC\nLIMIT @Limit;"))
			{
				if (normalizedStrategyId.HasValue)
				{
					command.Parameters.AddWithValue("StrategyId", normalizedStrategyId.GetValueOrDefault());
				}
				if (createdAfterUtc.HasValue)
				{
					command.Parameters.Add("CreatedAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(createdAfterUtc.GetValueOrDefault());
				}
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

	public async Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsByPaperOrderIdsAsync(IReadOnlyCollection<Guid> paperOrderIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid[] normalizedPaperOrderIds = paperOrderIds
			.Where(id => id != Guid.Empty)
			.Distinct()
			.ToArray();
		if (normalizedPaperOrderIds.Length == 0)
		{
			return Array.Empty<StrategyMarketPaperRun>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM strategy_market_paper_runs\nWHERE paper_order_id = ANY(@PaperOrderIds)\nORDER BY COALESCE(settled_at_utc, entered_at_utc, updated_at_utc) DESC,\n         updated_at_utc DESC;");
		command.Parameters.AddWithValue("PaperOrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid, normalizedPaperOrderIds);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<StrategyMarketPaperRun> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadStrategyMarketPaperRun(reader));
		}

		return results;
	}

	public async Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO paper_fills (\n    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,\n    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\n) VALUES (\n    @Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, @Evidence, @RealizedPnlUsd,\n    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,\n    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd\n);");
		AddPaperFillParameters(command, fill);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperFill> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperFill> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM paper_fills\nORDER BY filled_at_utc DESC\nLIMIT @Limit;"))
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
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM paper_fills\nWHERE paper_order_id = @PaperOrderId\nORDER BY filled_at_utc ASC, id ASC;"))
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

	public async Task<IReadOnlyList<PaperFill>> GetPaperFillsForOrdersAsync(IReadOnlyCollection<Guid> paperOrderIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid[] normalizedPaperOrderIds = paperOrderIds
			.Where(id => id != Guid.Empty)
			.Distinct()
			.ToArray();
		if (normalizedPaperOrderIds.Length == 0)
		{
			return Array.Empty<PaperFill>();
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd\nFROM paper_fills\nWHERE paper_order_id = ANY(@PaperOrderIds)\nORDER BY paper_order_id, filled_at_utc ASC, id ASC;");
		command.Parameters.AddWithValue("PaperOrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid, normalizedPaperOrderIds);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperFill> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperFill(reader));
		}

		return results;
	}

	public async Task<PaperLiveShadowFillReconciliationResult> ReconcilePaperLiveShadowFillAsync(
		PaperLiveShadowFillReconciliationRequest request,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (request.PaperOrderId == Guid.Empty || request.LiveOrderId == Guid.Empty)
		{
			throw new ArgumentException("Paper and Live order identifiers are required for shadow reconciliation.", nameof(request));
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		var initialOrder = await ReadPaperOrderForReconciliationAsync(
			connection,
			transaction: null,
			request.PaperOrderId,
			forUpdate: false,
			cancellationToken);
		if (initialOrder is null)
		{
			throw new InvalidOperationException($"Paper shadow order {request.PaperOrderId:D} was not found.");
		}

		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await LockPaperWalletsAsync(connection, transaction, [initialOrder.CopiedTraderWallet], cancellationToken);
		var currentPosition = await ReadPaperPositionForReconciliationAsync(
			connection,
			transaction,
			initialOrder.CopiedTraderWallet,
			initialOrder.AssetId,
			cancellationToken);
		var currentOrder = await ReadPaperOrderForReconciliationAsync(
			connection,
			transaction,
			request.PaperOrderId,
			forUpdate: true,
			cancellationToken);
		if (currentOrder is null)
		{
			throw new InvalidOperationException($"Paper shadow order {request.PaperOrderId:D} disappeared during reconciliation.");
		}

		if (!string.Equals(currentOrder.CopiedTraderWallet, initialOrder.CopiedTraderWallet, StringComparison.Ordinal) ||
			!string.Equals(currentOrder.AssetId, initialOrder.AssetId, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Paper shadow wallet or asset changed while reconciliation locks were being acquired.");
		}

		var liveOrder = await ReadLiveOrderForReconciliationAsync(
			connection,
			transaction,
			request.LiveOrderId,
			cancellationToken);
		if (liveOrder is null)
		{
			throw new InvalidOperationException($"Live shadow order {request.LiveOrderId:D} was not found.");
		}

		var existingFills = await ReadPaperFillsForReconciliationAsync(
			connection,
			transaction,
			request.PaperOrderId,
			cancellationToken);
		await EnsurePaperShadowNotSettledAsync(
			connection,
			transaction,
			currentOrder.CopiedTraderWallet,
			currentOrder.AssetId,
			cancellationToken);
		var copiedLeaderPosition = await ReadPaperCopiedLeaderPositionForReconciliationAsync(
			connection,
			transaction,
			currentOrder.Id,
			cancellationToken);
		if (copiedLeaderPosition is not null &&
			(copiedLeaderPosition.Status == PaperCopiedLeaderPositionStatus.Closed ||
			 copiedLeaderPosition.LeaderSoldSizeShares > 0m ||
			 copiedLeaderPosition.CopiedExitRequestedSizeShares > 0m))
		{
			throw new InvalidOperationException("Paper copied-leader exits already started; shadow fill reconciliation was refused.");
		}

		var canonical = PaperLiveShadowFillAccounting.CreateCanonicalState(
			currentOrder,
			liveOrder,
			existingFills,
			currentPosition,
			request.ReconciledAtUtc);

		await UpsertPaperPositionsBatchAsync(connection, transaction, [canonical.PaperPosition], cancellationToken);
		await UpdatePaperOrderForReconciliationAsync(connection, transaction, canonical.PaperOrder, cancellationToken);
		await ReplacePaperFillsForReconciliationAsync(
			connection,
			transaction,
			canonical.PaperFill,
			existingFills,
			cancellationToken);
		await SetPaperCopiedLeaderPositionFillForReconciliationAsync(
			connection,
			transaction,
			canonical.PaperFill,
			cancellationToken);

		await transaction.CommitAsync(cancellationToken);
		return new PaperLiveShadowFillReconciliationResult(
			canonical.PaperOrder,
			canonical.PaperFill,
			canonical.PaperPosition);
	}

	private static async Task<PaperOrder?> ReadPaperOrderForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction? transaction,
		Guid paperOrderId,
		bool forUpdate,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(
			connection,
			"SELECT " + PaperOrderSelectColumns + "\nFROM paper_orders\nWHERE id = @Id\nLIMIT 1" + (forUpdate ? "\nFOR UPDATE" : string.Empty) + ";");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Id", paperOrderId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperOrder(reader) : null;
	}

	private static async Task<PaperPosition?> ReadPaperPositionForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string copiedTraderWallet,
		string assetId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd,
       unrealized_pnl_usd, updated_at_utc, copied_trader_wallet,
       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd
FROM paper_positions
WHERE copied_trader_wallet = @CopiedTraderWallet
  AND asset_id = @AssetId
LIMIT 1
FOR UPDATE;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", assetId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperPosition(reader) : null;
	}

	private static async Task<LiveOrder?> ReadLiveOrderForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid liveOrderId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(
			connection,
			"SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE id = @Id\nLIMIT 1\nFOR UPDATE;");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Id", liveOrderId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		var orders = await ReadLiveOrdersAsync(reader, cancellationToken);
		return orders.SingleOrDefault();
	}

	private static async Task<IReadOnlyList<PaperFill>> ReadPaperFillsForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid paperOrderId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,
       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
FROM paper_fills
WHERE paper_order_id = @PaperOrderId
ORDER BY filled_at_utc, id
FOR UPDATE;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("PaperOrderId", paperOrderId);
		List<PaperFill> fills = [];
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			fills.Add(ReadPaperFill(reader));
		}

		return fills;
	}

	private static async Task EnsurePaperShadowNotSettledAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string copiedTraderWallet,
		string assetId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT EXISTS (
    SELECT 1
    FROM paper_position_settlements
    WHERE copied_trader_wallet = @CopiedTraderWallet
      AND asset_id = @AssetId
);
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", assetId);
		if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
		{
			throw new InvalidOperationException("Paper shadow position was already settled; reconciliation was refused.");
		}
	}

	private static async Task<PaperCopiedLeaderPosition?> ReadPaperCopiedLeaderPositionForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		Guid paperOrderId,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, entry_signal_id, entry_paper_order_id, copied_trader_wallet, asset_id,
       condition_id, outcome, entry_transaction_hash, entry_timestamp_utc,
       leader_entry_price, leader_initial_size_shares, copied_initial_size_shares,
       leader_sold_size_shares, copied_exit_requested_size_shares, status,
       last_activity_timestamp_utc, last_activity_transaction_hash,
       last_activity_sync_at_utc, next_activity_sync_at_utc, created_at_utc, updated_at_utc
FROM paper_copied_leader_positions
WHERE entry_paper_order_id = @PaperOrderId
LIMIT 1
FOR UPDATE;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("PaperOrderId", paperOrderId);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperCopiedLeaderPosition(reader) : null;
	}

	private static async Task UpdatePaperOrderForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		PaperOrder order,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_orders
SET status = @Status,
    strategy_id = @StrategyId,
    filled_at_utc = @FilledAtUtc,
    cancelled_at_utc = @CancelledAtUtc,
    price = @Price,
    size_shares = @SizeShares,
    notional_usd = @NotionalUsd,
    raw_decision_json = CAST(@RawDecisionJson AS jsonb),
    correlation_id = @CorrelationId,
    execution_source = @ExecutionSource
WHERE id = @Id;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("Id", order.Id);
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(order.StrategyId));
		command.Parameters.AddWithValue("Status", order.Status.ToString());
		command.Parameters.AddWithValue("Price", order.Price);
		command.Parameters.AddWithValue("SizeShares", order.SizeShares);
		command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
		command.Parameters.AddWithValue("FilledAtUtc", order.FilledAtUtc.HasValue ? UtcDateTime(order.FilledAtUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("CancelledAtUtc", order.CancelledAtUtc.HasValue ? UtcDateTime(order.CancelledAtUtc.Value) : (object)DBNull.Value);
		command.Parameters.AddWithValue("RawDecisionJson", BuildPaperOrderRawDecisionJson(order));
		command.Parameters.AddWithValue("CorrelationId", order.CorrelationId.HasValue ? order.CorrelationId.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource ?? string.Empty);
		if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
		{
			throw new InvalidOperationException("Paper shadow order update did not affect exactly one row.");
		}
	}

	private static async Task ReplacePaperFillsForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		PaperFill canonicalFill,
		IReadOnlyList<PaperFill> existingFills,
		CancellationToken cancellationToken)
	{
		if (existingFills.Count == 0)
		{
			await using NpgsqlCommand insertCommand = CreateCommand(connection, """
INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
)
VALUES (
    @Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, @Evidence, @RealizedPnlUsd,
    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,
    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd
);
""");
			insertCommand.Transaction = transaction;
			AddPaperFillParameters(insertCommand, canonicalFill);
			await insertCommand.ExecuteNonQueryAsync(cancellationToken);
			return;
		}

		await using (NpgsqlCommand updateCommand = CreateCommand(connection, """
UPDATE paper_fills
SET price = @Price,
    size_shares = @SizeShares,
    filled_at_utc = @FilledAtUtc,
    evidence = @Evidence,
    realized_pnl_usd = @RealizedPnlUsd,
    fee_usd = @FeeUsd,
    fee_accounting_status = @FeeAccountingStatus,
    fee_liquidity_role = @FeeLiquidityRole,
    fee_calculation_source = @FeeCalculationSource,
    fee_rate = @FeeRate,
    fee_exponent = @FeeExponent,
    fee_taker_only = @FeeTakerOnly,
    fee_calculated_at_utc = @FeeCalculatedAtUtc,
    net_realized_pnl_usd = @NetRealizedPnlUsd
WHERE id = @Id
  AND paper_order_id = @PaperOrderId;
"""))
		{
			updateCommand.Transaction = transaction;
			AddPaperFillParameters(updateCommand, canonicalFill);
			if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
			{
				throw new InvalidOperationException("Canonical Paper shadow fill update did not affect exactly one row.");
			}
		}

		await using NpgsqlCommand deleteCommand = CreateCommand(connection, """
DELETE FROM paper_fills
WHERE paper_order_id = @PaperOrderId
  AND id <> @CanonicalFillId;
""");
		deleteCommand.Transaction = transaction;
		deleteCommand.Parameters.AddWithValue("PaperOrderId", canonicalFill.PaperOrderId);
		deleteCommand.Parameters.AddWithValue("CanonicalFillId", canonicalFill.Id);
		await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task SetPaperCopiedLeaderPositionFillForReconciliationAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		PaperFill canonicalFill,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_copied_leader_positions
SET status = 'Active',
    copied_initial_size_shares = @CopiedInitialSizeShares,
    next_activity_sync_at_utc = LEAST(next_activity_sync_at_utc, @FilledAtUtc),
    updated_at_utc = @FilledAtUtc
WHERE entry_paper_order_id = @EntryPaperOrderId
  AND status IN ('PendingEntry', 'Active');
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("EntryPaperOrderId", canonicalFill.PaperOrderId);
		command.Parameters.AddWithValue("CopiedInitialSizeShares", canonicalFill.SizeShares);
		command.Parameters.AddWithValue("FilledAtUtc", UtcDateTime(canonicalFill.FilledAtUtc));
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await LockPaperPositionKeysAsync(connection, transaction, [position], [], cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "INSERT INTO paper_positions (\n    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares, average_price,\n    estimated_value_usd, unrealized_pnl_usd, updated_at_utc,\n    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd\n) VALUES (\n    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome, @SizeShares, @AveragePrice,\n    @EstimatedValueUsd, @UnrealizedPnlUsd, @UpdatedAtUtc,\n    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,\n    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetUnrealizedPnlUsd\n)\nON CONFLICT (copied_trader_wallet, asset_id) DO UPDATE SET\n    condition_id = excluded.condition_id,\n    outcome = excluded.outcome,\n    size_shares = excluded.size_shares,\n    average_price = excluded.average_price,\n    estimated_value_usd = excluded.estimated_value_usd,\n    unrealized_pnl_usd = excluded.unrealized_pnl_usd,\n    fee_usd = excluded.fee_usd,\n    fee_accounting_status = excluded.fee_accounting_status,\n    fee_liquidity_role = excluded.fee_liquidity_role,\n    fee_calculation_source = excluded.fee_calculation_source,\n    fee_rate = excluded.fee_rate,\n    fee_exponent = excluded.fee_exponent,\n    fee_taker_only = excluded.fee_taker_only,\n    fee_calculated_at_utc = excluded.fee_calculated_at_utc,\n    net_unrealized_pnl_usd = excluded.net_unrealized_pnl_usd,\n    updated_at_utc = excluded.updated_at_utc;");
		command.Transaction = transaction;
		AddPaperPositionParameters(command, position);
		await command.ExecuteNonQueryAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<bool> TryUpdatePaperPositionMarkAsync(
		PaperPosition expectedPosition,
		decimal estimatedValueUsd,
		decimal unrealizedPnlUsd,
		decimal? netUnrealizedPnlUsd,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await LockPaperPositionKeysAsync(connection, transaction, [expectedPosition], [], cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE paper_positions
SET estimated_value_usd = @EstimatedValueUsd,
    unrealized_pnl_usd = @UnrealizedPnlUsd,
    net_unrealized_pnl_usd = @NetUnrealizedPnlUsd,
    updated_at_utc = @UpdatedAtUtc
WHERE copied_trader_wallet = @CopiedTraderWallet
  AND asset_id = @AssetId
  AND condition_id = @ExpectedConditionId
  AND outcome = @ExpectedOutcome
  AND size_shares = @ExpectedSizeShares
  AND average_price = @ExpectedAveragePrice
  AND estimated_value_usd = @ExpectedEstimatedValueUsd
  AND unrealized_pnl_usd = @ExpectedUnrealizedPnlUsd
  AND net_unrealized_pnl_usd IS NOT DISTINCT FROM @ExpectedNetUnrealizedPnlUsd
  AND updated_at_utc = @ExpectedUpdatedAtUtc;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("CopiedTraderWallet", expectedPosition.CopiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", expectedPosition.AssetId);
		command.Parameters.AddWithValue("ExpectedConditionId", expectedPosition.ConditionId);
		command.Parameters.AddWithValue("ExpectedOutcome", expectedPosition.Outcome);
		command.Parameters.AddWithValue("ExpectedSizeShares", expectedPosition.SizeShares);
		command.Parameters.AddWithValue("ExpectedAveragePrice", expectedPosition.AveragePrice);
		command.Parameters.AddWithValue("ExpectedEstimatedValueUsd", expectedPosition.EstimatedValueUsd);
		command.Parameters.AddWithValue("ExpectedUnrealizedPnlUsd", expectedPosition.UnrealizedPnlUsd);
		command.Parameters.AddWithValue("ExpectedNetUnrealizedPnlUsd", NullableDecimal(expectedPosition.NetUnrealizedPnlUsd));
		command.Parameters.AddWithValue("ExpectedUpdatedAtUtc", UtcDateTime(expectedPosition.UpdatedAtUtc));
		command.Parameters.AddWithValue("EstimatedValueUsd", estimatedValueUsd);
		command.Parameters.AddWithValue("UnrealizedPnlUsd", unrealizedPnlUsd);
		command.Parameters.AddWithValue("NetUnrealizedPnlUsd", NullableDecimal(netUnrealizedPnlUsd));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
		var updated = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
		await transaction.CommitAsync(cancellationToken);
		return updated;
	}

	public async Task<IReadOnlyList<PaperPosition>> TryUpdatePaperPositionMarksAsync(
		IReadOnlyList<PaperPositionMarkUpdate> updates,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		return await TryUpdatePaperPositionMarksCoreAsync(
			updates,
			stageObserver: null,
			cancellationToken);
	}

	public async Task<IReadOnlyList<PaperPosition>> TryUpdatePaperPositionMarksAsync(
		IReadOnlyList<PaperPositionMarkUpdate> updates,
		Action<string> stageObserver,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(stageObserver);
		return await TryUpdatePaperPositionMarksCoreAsync(
			updates,
			stageObserver,
			cancellationToken);
	}

	private async Task<IReadOnlyList<PaperPosition>> TryUpdatePaperPositionMarksCoreAsync(
		IReadOnlyList<PaperPositionMarkUpdate> updates,
		Action<string>? stageObserver,
		CancellationToken cancellationToken)
	{
		if (updates.Count == 0)
		{
			return [];
		}

		var rows = updates.Select(update => new
		{
			copied_trader_wallet = update.ExpectedPosition.CopiedTraderWallet,
			asset_id = update.ExpectedPosition.AssetId,
			expected_condition_id = update.ExpectedPosition.ConditionId,
			expected_outcome = update.ExpectedPosition.Outcome,
			expected_size_shares = update.ExpectedPosition.SizeShares,
			expected_average_price = update.ExpectedPosition.AveragePrice,
			expected_estimated_value_usd = update.ExpectedPosition.EstimatedValueUsd,
			expected_unrealized_pnl_usd = update.ExpectedPosition.UnrealizedPnlUsd,
			expected_net_unrealized_pnl_usd = update.ExpectedPosition.NetUnrealizedPnlUsd,
			expected_updated_at_utc = UtcDateTime(update.ExpectedPosition.UpdatedAtUtc),
			estimated_value_usd = update.EstimatedValueUsd,
			unrealized_pnl_usd = update.UnrealizedPnlUsd,
			net_unrealized_pnl_usd = update.NetUnrealizedPnlUsd,
			updated_at_utc = UtcDateTime(update.UpdatedAtUtc)
		}).ToArray();
		stageObserver?.Invoke(PaperPositionMarkPersistenceStages.OpenConnection);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH mark_updates AS (
    SELECT *
    FROM jsonb_to_recordset(CAST(@PaperPositionMarkUpdatesJson AS jsonb)) AS mark_update(
        copied_trader_wallet text,
        asset_id text,
        expected_condition_id text,
        expected_outcome text,
        expected_size_shares numeric,
        expected_average_price numeric,
        expected_estimated_value_usd numeric,
        expected_unrealized_pnl_usd numeric,
        expected_net_unrealized_pnl_usd numeric,
        expected_updated_at_utc timestamptz,
        estimated_value_usd numeric,
        unrealized_pnl_usd numeric,
        net_unrealized_pnl_usd numeric,
        updated_at_utc timestamptz
    )
), eligible_positions AS MATERIALIZED (
    SELECT
        target_position.id,
        mark_update.estimated_value_usd,
        mark_update.unrealized_pnl_usd,
        mark_update.net_unrealized_pnl_usd,
        mark_update.updated_at_utc
    FROM paper_positions AS target_position
    INNER JOIN mark_updates AS mark_update
        ON target_position.copied_trader_wallet = mark_update.copied_trader_wallet
       AND target_position.asset_id = mark_update.asset_id
    WHERE target_position.condition_id = mark_update.expected_condition_id
      AND target_position.outcome = mark_update.expected_outcome
      AND target_position.size_shares = mark_update.expected_size_shares
      AND target_position.average_price = mark_update.expected_average_price
      AND target_position.estimated_value_usd = mark_update.expected_estimated_value_usd
      AND target_position.unrealized_pnl_usd = mark_update.expected_unrealized_pnl_usd
      AND target_position.net_unrealized_pnl_usd IS NOT DISTINCT FROM mark_update.expected_net_unrealized_pnl_usd
      AND target_position.updated_at_utc = mark_update.expected_updated_at_utc
    ORDER BY
        target_position.copied_trader_wallet COLLATE "C",
        target_position.asset_id COLLATE "C"
    FOR UPDATE OF target_position SKIP LOCKED
), updated_positions AS (
    UPDATE paper_positions AS target_position
    SET estimated_value_usd = eligible_position.estimated_value_usd,
        unrealized_pnl_usd = eligible_position.unrealized_pnl_usd,
        net_unrealized_pnl_usd = eligible_position.net_unrealized_pnl_usd,
        updated_at_utc = eligible_position.updated_at_utc
    FROM eligible_positions AS eligible_position
    WHERE target_position.id = eligible_position.id
    RETURNING
        target_position.asset_id,
        target_position.condition_id,
        target_position.outcome,
        target_position.size_shares,
        target_position.average_price,
        target_position.estimated_value_usd,
        target_position.unrealized_pnl_usd,
        target_position.updated_at_utc,
        target_position.copied_trader_wallet,
        target_position.fee_usd,
        target_position.fee_accounting_status,
        target_position.fee_liquidity_role,
        target_position.fee_calculation_source,
        target_position.fee_rate,
        target_position.fee_exponent,
        target_position.fee_taker_only,
        target_position.fee_calculated_at_utc,
        target_position.net_unrealized_pnl_usd
)
SELECT
    asset_id,
    condition_id,
    outcome,
    size_shares,
    average_price,
    estimated_value_usd,
    unrealized_pnl_usd,
    updated_at_utc,
    copied_trader_wallet,
    fee_usd,
    fee_accounting_status,
    fee_liquidity_role,
    fee_calculation_source,
    fee_rate,
    fee_exponent,
    fee_taker_only,
    fee_calculated_at_utc,
    net_unrealized_pnl_usd
FROM updated_positions
ORDER BY copied_trader_wallet, asset_id;
""");
		stageObserver?.Invoke(PaperPositionMarkPersistenceStages.SerializeUpdates);
		AddJsonbParameter(command, "PaperPositionMarkUpdatesJson", JsonSerializer.Serialize(rows));
		List<PaperPosition> updatedPositions = [];
		stageObserver?.Invoke(PaperPositionMarkPersistenceStages.ExecuteCommand);
		await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
		{
			stageObserver?.Invoke(PaperPositionMarkPersistenceStages.ReadResults);
			while (await reader.ReadAsync(cancellationToken))
			{
				updatedPositions.Add(ReadPaperPosition(reader));
			}
		}

		return updatedPositions;
	}

	public async Task UpsertPaperPositionsAsync(
		IReadOnlyList<PaperPosition> positions,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (positions.Count == 0)
		{
			return;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await LockPaperPositionKeysAsync(connection, transaction, positions, [], cancellationToken);
		await UpsertPaperPositionsBatchAsync(connection, transaction, positions, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<PaperPosition> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<PaperPosition> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc, copied_trader_wallet,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd\nFROM paper_positions\nORDER BY updated_at_utc DESC, copied_trader_wallet ASC, asset_id ASC;"))
			{
				IReadOnlyList<PaperPosition> readOnlyList;
				await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					List<PaperPosition> results = new List<PaperPosition>();
					while (await reader.ReadAsync(cancellationToken))
					{
						results.Add(ReadPaperPosition(reader));
					}
					readOnlyList = results;
				}
				readOnlyList2 = readOnlyList;
			}
			result = readOnlyList2;
		}
		return result;
	}

	public async Task<IReadOnlyList<PaperPosition>> GetOpenPaperPositionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc, copied_trader_wallet,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd\nFROM paper_positions\nWHERE size_shares > 0\nORDER BY updated_at_utc DESC, copied_trader_wallet ASC, asset_id ASC;");
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperPosition> results = new List<PaperPosition>();
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperPosition(reader));
		}

		return results;
	}

	public async Task<IReadOnlyList<PaperPosition>> GetOpenPaperPositionsForMarketAsync(
		string? conditionId,
		string? assetId,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedConditionId = string.IsNullOrWhiteSpace(conditionId) ? null : conditionId;
		var normalizedAssetId = string.IsNullOrWhiteSpace(assetId) ? null : assetId;
		if (normalizedConditionId is null && normalizedAssetId is null)
		{
			return [];
		}

		var marketPredicate = normalizedConditionId is not null && normalizedAssetId is not null
			? "(lower(condition_id) = lower(@ConditionId) OR lower(asset_id) = lower(@AssetId))"
			: normalizedConditionId is not null
				? "lower(condition_id) = lower(@ConditionId)"
				: "lower(asset_id) = lower(@AssetId)";
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, $"""
SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc, copied_trader_wallet,
       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd
FROM paper_positions
WHERE size_shares > 0
  AND {marketPredicate}
ORDER BY updated_at_utc DESC, copied_trader_wallet ASC, asset_id ASC;
""");
		if (normalizedConditionId is not null)
		{
			command.Parameters.AddWithValue("ConditionId", normalizedConditionId);
		}

		if (normalizedAssetId is not null)
		{
			command.Parameters.AddWithValue("AssetId", normalizedAssetId);
		}

		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<PaperPosition> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadPaperPosition(reader));
		}

		return results;
	}

	public async Task<PaperPosition?> GetPaperPositionAsync(string copiedTraderWallet, string assetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(assetId))
		{
			return null;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT asset_id, condition_id, outcome, size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc, copied_trader_wallet,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd\nFROM paper_positions\nWHERE copied_trader_wallet = @CopiedTraderWallet\n  AND asset_id = @AssetId\nLIMIT 1;");
		command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet ?? string.Empty);
		command.Parameters.AddWithValue("AssetId", assetId.Trim());
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken) ? ReadPaperPosition(reader) : null;
	}

	public async Task<bool> TryAddPaperPositionSettlementAsync(PaperPositionSettlement settlement, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
    category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
) VALUES (
    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome, @WinningAssetId, @WinningOutcome,
    @Category, @SettledSizeShares, @AveragePrice, @CostBasisUsd, @SettlementValueUsd,
    @RealizedPnlUsd, @Won, @SettlementSource, @SettledAtUtc, @CreatedAtUtc,
    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,
    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd
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
		command.Parameters.AddWithValue("FeeUsd", settlement.FeeUsd);
		command.Parameters.AddWithValue("FeeAccountingStatus", settlement.FeeAccountingStatus);
		command.Parameters.AddWithValue("FeeLiquidityRole", settlement.FeeLiquidityRole);
		command.Parameters.AddWithValue("FeeCalculationSource", settlement.FeeCalculationSource);
		command.Parameters.AddWithValue("FeeRate", NullableDecimal(settlement.FeeRate));
		command.Parameters.AddWithValue("FeeExponent", settlement.FeeExponent.HasValue ? settlement.FeeExponent.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeTakerOnly", settlement.FeeTakerOnly.HasValue ? settlement.FeeTakerOnly.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeCalculatedAtUtc", NullableDateTime(settlement.FeeCalculatedAtUtc));
		command.Parameters.AddWithValue("NetRealizedPnlUsd", NullableDecimal(settlement.NetRealizedPnlUsd));
		return await command.ExecuteScalarAsync(cancellationToken) is not null;
	}

	public async Task<int> PersistPaperPositionSettlementBatchAsync(
		IReadOnlyList<PaperPositionSettlementWrite> writes,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (writes.Count == 0)
		{
			return 0;
		}
		if (writes.Any(write =>
			!string.Equals(
				write.Settlement.CopiedTraderWallet,
				write.SettledPosition.CopiedTraderWallet,
				StringComparison.Ordinal)
			|| !string.Equals(
				write.Settlement.AssetId,
				write.SettledPosition.AssetId,
				StringComparison.Ordinal)))
		{
			throw new ArgumentException(
				"Each settlement and settled position must have the same exact wallet and asset key.",
				nameof(writes));
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		var settledPositions = writes.Select(write => write.SettledPosition).ToArray();
		await LockPaperPositionKeysAsync(connection, transaction, settledPositions, [], cancellationToken);
		await UpsertPaperPositionsBatchAsync(
			connection,
			transaction,
			settledPositions,
			cancellationToken);
		var inserted = await AddPaperPositionSettlementsBatchAsync(
			connection,
			transaction,
			writes.Select(write => write.Settlement).ToArray(),
			cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		return inserted;
	}

	private static async Task<int> AddPaperPositionSettlementsBatchAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IReadOnlyList<PaperPositionSettlement> settlements,
		CancellationToken cancellationToken)
	{
		var rows = settlements.Select(settlement => new
		{
			id = settlement.Id,
			copied_trader_wallet = settlement.CopiedTraderWallet,
			asset_id = settlement.AssetId,
			condition_id = settlement.ConditionId,
			outcome = settlement.Outcome,
			winning_asset_id = settlement.WinningAssetId,
			winning_outcome = settlement.WinningOutcome,
			category = settlement.Category,
			settled_size_shares = settlement.SettledSizeShares,
			average_price = settlement.AveragePrice,
			cost_basis_usd = settlement.CostBasisUsd,
			settlement_value_usd = settlement.SettlementValueUsd,
			realized_pnl_usd = settlement.RealizedPnlUsd,
			fee_usd = settlement.FeeUsd,
			fee_accounting_status = settlement.FeeAccountingStatus,
			fee_liquidity_role = settlement.FeeLiquidityRole,
			fee_calculation_source = settlement.FeeCalculationSource,
			fee_rate = settlement.FeeRate,
			fee_exponent = settlement.FeeExponent,
			fee_taker_only = settlement.FeeTakerOnly,
			fee_calculated_at_utc = settlement.FeeCalculatedAtUtc.HasValue ? UtcDateTime(settlement.FeeCalculatedAtUtc.Value) : (DateTime?)null,
			net_realized_pnl_usd = settlement.NetRealizedPnlUsd,
			won = settlement.Won,
			settlement_source = settlement.SettlementSource,
			settled_at_utc = UtcDateTime(settlement.SettledAtUtc),
			created_at_utc = UtcDateTime(settlement.CreatedAtUtc)
		});
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH inserted AS (
    INSERT INTO paper_position_settlements (
        id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
        category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
        realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc,
        fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
        fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
    )
    SELECT
        settlement.id, settlement.copied_trader_wallet, settlement.asset_id, settlement.condition_id,
        settlement.outcome, settlement.winning_asset_id, settlement.winning_outcome, settlement.category,
        settlement.settled_size_shares, settlement.average_price, settlement.cost_basis_usd,
        settlement.settlement_value_usd, settlement.realized_pnl_usd, settlement.won,
        settlement.settlement_source, settlement.settled_at_utc, settlement.created_at_utc,
        settlement.fee_usd, settlement.fee_accounting_status, settlement.fee_liquidity_role,
        settlement.fee_calculation_source, settlement.fee_rate, settlement.fee_exponent,
        settlement.fee_taker_only, settlement.fee_calculated_at_utc, settlement.net_realized_pnl_usd
    FROM jsonb_to_recordset(CAST(@SettlementsJson AS jsonb)) AS settlement(
        id uuid,
        copied_trader_wallet text,
        asset_id text,
        condition_id text,
        outcome text,
        winning_asset_id text,
        winning_outcome text,
        category text,
        settled_size_shares numeric,
        average_price numeric,
        cost_basis_usd numeric,
        settlement_value_usd numeric,
        realized_pnl_usd numeric,
        won boolean,
        settlement_source text,
        settled_at_utc timestamptz,
        created_at_utc timestamptz,
        fee_usd numeric,
        fee_accounting_status text,
        fee_liquidity_role text,
        fee_calculation_source text,
        fee_rate numeric,
        fee_exponent integer,
        fee_taker_only boolean,
        fee_calculated_at_utc timestamptz,
        net_realized_pnl_usd numeric
    )
    ORDER BY
        settlement.copied_trader_wallet COLLATE "C",
        settlement.asset_id COLLATE "C",
        settlement.id
    ON CONFLICT (copied_trader_wallet, asset_id) DO NOTHING
    RETURNING 1
)
SELECT count(*)::integer FROM inserted;
""");
		command.Transaction = transaction;
		AddJsonbParameter(command, "SettlementsJson", JsonSerializer.Serialize(rows));
		return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
	}

	public async Task<IReadOnlyList<PaperPositionSettlement>> GetRecentPaperPositionSettlementsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id, winning_outcome,
       category, settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
       realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc,
       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
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
				DateTimeOffsetFromUtc(reader.GetDateTime(16)),
				reader.GetDecimal(17),
				reader.GetString(18),
				reader.GetString(19),
				reader.GetString(20),
				reader.IsDBNull(21) ? null : reader.GetDecimal(21),
				reader.IsDBNull(22) ? null : reader.GetInt32(22),
				reader.IsDBNull(23) ? null : reader.GetBoolean(23),
				reader.IsDBNull(24) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(24)),
				reader.IsDBNull(25) ? null : reader.GetDecimal(25)));
		}

		return results;
	}

	public async Task<PaperCopiedTraderPerformanceRefreshResult> RefreshPaperCopiedTraderPerformanceProjectionAsync(
		int highPriorityWalletBatchSize,
		int reconciliationWalletBatchSize,
		int reconciliationSeedWalletBatchSize,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(highPriorityWalletBatchSize, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(highPriorityWalletBatchSize, 250);
		ArgumentOutOfRangeException.ThrowIfLessThan(reconciliationWalletBatchSize, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(reconciliationWalletBatchSize, 250);
		ArgumentOutOfRangeException.ThrowIfLessThan(reconciliationSeedWalletBatchSize, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(reconciliationSeedWalletBatchSize, 1_000);

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		bool refreshLockAcquired;
		try
		{
			refreshLockAcquired = await TryAcquirePaperCopiedTraderPerformanceRefreshLockAsync(
				connection,
				cancellationToken);
		}
		catch
		{
			NpgsqlConnection.ClearPool(connection);
			throw;
		}

		if (!refreshLockAcquired)
		{
			return new PaperCopiedTraderPerformanceRefreshResult(false, 0, 0, 0, 0, false);
		}

		Exception? operationException = null;
		try
		{
			int highPriorityWalletsProcessed;
			int reconciliationWalletsProcessed;
			await using (NpgsqlTransaction claimTransaction = await connection.BeginTransactionAsync(
				IsolationLevel.ReadCommitted,
				cancellationToken))
			{
				await using (NpgsqlCommand createTempCommand = CreateCommand(connection, """
DROP TABLE IF EXISTS pg_temp.temp_paper_copied_trader_performance_wallets;
CREATE TEMP TABLE temp_paper_copied_trader_performance_wallets (
    copied_trader_wallet text PRIMARY KEY,
    work_kind text NOT NULL CHECK (work_kind IN ('high_priority', 'reconciliation'))
) ON COMMIT PRESERVE ROWS;
"""))
				{
					createTempCommand.Transaction = claimTransaction;
					await createTempCommand.ExecuteNonQueryAsync(cancellationToken);
				}

				highPriorityWalletsProcessed = await RecoverPaperCopiedTraderPerformanceInflightAsync(
					connection,
					claimTransaction,
					"high_priority",
					highPriorityWalletBatchSize,
					cancellationToken);
				highPriorityWalletsProcessed += await ClaimPaperCopiedTraderPerformanceQueueAsync(
					connection,
					claimTransaction,
					"high_priority",
					highPriorityWalletBatchSize - highPriorityWalletsProcessed,
					cancellationToken);

				reconciliationWalletsProcessed = await RecoverPaperCopiedTraderPerformanceInflightAsync(
					connection,
					claimTransaction,
					"reconciliation",
					reconciliationWalletBatchSize,
					cancellationToken);
				reconciliationWalletsProcessed += await ClaimPaperCopiedTraderPerformanceQueueAsync(
					connection,
					claimTransaction,
					"reconciliation",
					reconciliationWalletBatchSize - reconciliationWalletsProcessed,
					cancellationToken);

				await claimTransaction.CommitAsync(cancellationToken);
			}

			await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
				IsolationLevel.ReadCommitted,
				cancellationToken);
			string? reconciliationCursor;
			await using (NpgsqlCommand cursorCommand = CreateCommand(connection, """
SELECT reconciliation_cursor_wallet
FROM paper_copied_trader_performance_projection_control
WHERE singleton_id = 1
FOR UPDATE;
"""))
			{
				cursorCommand.Transaction = transaction;
				reconciliationCursor = await cursorCommand.ExecuteScalarAsync(cancellationToken) as string;
			}
			var paperPositionsStatsBeforeSeed = await PostgresPaperPositionsScanTelemetry.ReadAsync(
				connection,
				transaction,
				cancellationToken);

			var walletsSeeded = 0;
			var reconciliationCycleCompleted = false;
			var reconciliationCapacity = reconciliationWalletBatchSize - reconciliationWalletsProcessed;
			if (reconciliationCapacity > 0)
			{
				var seedCandidates = 0;
				string? lastSeededWallet = null;
				var effectiveSeedLimit = Math.Min(reconciliationSeedWalletBatchSize, reconciliationCapacity);
				await using (NpgsqlCommand seedCommand = CreateCommand(connection, """
WITH source_wallets AS (
    (
        SELECT paper_order.copied_trader_wallet AS wallet
        FROM paper_orders paper_order
        WHERE btrim(paper_order.copied_trader_wallet) <> ''
          AND (@Cursor IS NULL OR paper_order.copied_trader_wallet > @Cursor)
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_queue queued_wallet
              WHERE queued_wallet.copied_trader_wallet = paper_order.copied_trader_wallet
          )
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_inflight inflight_wallet
              WHERE inflight_wallet.copied_trader_wallet = paper_order.copied_trader_wallet
          )
        GROUP BY paper_order.copied_trader_wallet
        ORDER BY paper_order.copied_trader_wallet
        LIMIT @SeedLimit
    )
    UNION
    (
        SELECT paper_position.copied_trader_wallet AS wallet
        FROM paper_positions paper_position
        WHERE btrim(paper_position.copied_trader_wallet) <> ''
          AND (@Cursor IS NULL OR paper_position.copied_trader_wallet > @Cursor)
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_queue queued_wallet
              WHERE queued_wallet.copied_trader_wallet = paper_position.copied_trader_wallet
          )
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_inflight inflight_wallet
              WHERE inflight_wallet.copied_trader_wallet = paper_position.copied_trader_wallet
          )
        GROUP BY paper_position.copied_trader_wallet
        ORDER BY paper_position.copied_trader_wallet
        LIMIT @SeedLimit
    )
    UNION
    (
        SELECT paper_settlement.copied_trader_wallet AS wallet
        FROM paper_position_settlements paper_settlement
        WHERE btrim(paper_settlement.copied_trader_wallet) <> ''
          AND (@Cursor IS NULL OR paper_settlement.copied_trader_wallet > @Cursor)
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_queue queued_wallet
              WHERE queued_wallet.copied_trader_wallet = paper_settlement.copied_trader_wallet
          )
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_inflight inflight_wallet
              WHERE inflight_wallet.copied_trader_wallet = paper_settlement.copied_trader_wallet
          )
        GROUP BY paper_settlement.copied_trader_wallet
        ORDER BY paper_settlement.copied_trader_wallet
        LIMIT @SeedLimit
    )
    UNION
    (
        SELECT performance.copied_trader_wallet AS wallet
        FROM paper_copied_trader_performance performance
        WHERE btrim(performance.copied_trader_wallet) <> ''
          AND (@Cursor IS NULL OR performance.copied_trader_wallet > @Cursor)
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_queue queued_wallet
              WHERE queued_wallet.copied_trader_wallet = performance.copied_trader_wallet
          )
          AND NOT EXISTS (
              SELECT 1
              FROM paper_copied_trader_performance_refresh_inflight inflight_wallet
              WHERE inflight_wallet.copied_trader_wallet = performance.copied_trader_wallet
          )
        GROUP BY performance.copied_trader_wallet
        ORDER BY performance.copied_trader_wallet
        LIMIT @SeedLimit
    )
), candidates AS (
    SELECT wallet
    FROM source_wallets
    ORDER BY wallet
    LIMIT @SeedLimit
), claimed AS (
    INSERT INTO paper_copied_trader_performance_refresh_inflight (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind,
        work_kind,
        claimed_at_utc)
    SELECT wallet, 0, now(), 'reconciliation', 'reconciliation', clock_timestamp()
    FROM candidates
    RETURNING copied_trader_wallet
), selected AS (
    INSERT INTO temp_paper_copied_trader_performance_wallets (
        copied_trader_wallet,
        work_kind)
    SELECT copied_trader_wallet, 'reconciliation'
    FROM claimed
    RETURNING copied_trader_wallet
)
SELECT
    (SELECT count(*)::integer FROM claimed) AS wallets_seeded,
    (SELECT count(*)::integer FROM candidates) AS seed_candidates,
    (SELECT max(wallet) FROM candidates) AS last_seeded_wallet,
    (SELECT count(*)::integer FROM selected) AS wallets_selected;
"""))
				{
					seedCommand.Transaction = transaction;
					seedCommand.Parameters.Add("Cursor", NpgsqlDbType.Text).Value = (object?)reconciliationCursor ?? DBNull.Value;
					seedCommand.Parameters.AddWithValue("SeedLimit", effectiveSeedLimit);
					await using NpgsqlDataReader reader = await seedCommand.ExecuteReaderAsync(cancellationToken);
					if (await reader.ReadAsync(cancellationToken))
					{
						walletsSeeded = reader.GetInt32(0);
						seedCandidates = reader.GetInt32(1);
						lastSeededWallet = reader.IsDBNull(2) ? null : reader.GetString(2);
						var seededWalletsSelected = reader.GetInt32(3);
						if (seededWalletsSelected != walletsSeeded)
						{
							throw new InvalidOperationException(
								"Every seeded paper copied-trader reconciliation wallet must be selected in the same transaction.");
						}

						reconciliationWalletsProcessed += seededWalletsSelected;
					}
				}

				reconciliationCycleCompleted = seedCandidates == 0;
				await using (NpgsqlCommand updateCursorCommand = CreateCommand(connection, """
UPDATE paper_copied_trader_performance_projection_control
SET reconciliation_cursor_wallet = @NextCursor,
    reconciliation_cycle = reconciliation_cycle + CASE WHEN @CycleCompleted THEN 1 ELSE 0 END,
    last_cycle_completed_at_utc = CASE WHEN @CycleCompleted THEN now() ELSE last_cycle_completed_at_utc END,
    updated_at_utc = now()
WHERE singleton_id = 1;
"""))
				{
					updateCursorCommand.Transaction = transaction;
					updateCursorCommand.Parameters.Add("NextCursor", NpgsqlDbType.Text).Value =
						reconciliationCycleCompleted ? DBNull.Value : lastSeededWallet!;
					updateCursorCommand.Parameters.AddWithValue("CycleCompleted", reconciliationCycleCompleted);
					await updateCursorCommand.ExecuteNonQueryAsync(cancellationToken);
				}
			}
			var paperPositionsStatsAfterSeed = await PostgresPaperPositionsScanTelemetry.ReadAsync(
				connection,
				transaction,
				cancellationToken);
			var paperPositionsSeedScanDelta = PostgresPaperPositionsScanStats.Delta(
				paperPositionsStatsBeforeSeed,
				paperPositionsStatsAfterSeed);

			var walletsProcessed = highPriorityWalletsProcessed + reconciliationWalletsProcessed;
			var performanceRowsWritten = 0;
			if (walletsProcessed > 0)
			{
				// PostgreSQL otherwise applies its default cardinality estimate to this
				// session-local selector and may scan every Paper source table.
				await using (NpgsqlCommand analyzeWalletSelectionCommand = CreateCommand(connection, """
ANALYZE pg_temp.temp_paper_copied_trader_performance_wallets;
"""))
				{
					analyzeWalletSelectionCommand.Transaction = transaction;
					await analyzeWalletSelectionCommand.ExecuteNonQueryAsync(cancellationToken);
				}

				await using (NpgsqlCommand deleteCommand = CreateCommand(connection, """
DELETE FROM paper_copied_trader_performance performance
USING temp_paper_copied_trader_performance_wallets selected
WHERE performance.copied_trader_wallet = selected.copied_trader_wallet;
"""))
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
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = po.copied_trader_wallet
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE market.condition_id = po.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) gm ON true
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
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = po.copied_trader_wallet
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE market.condition_id = po.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) gm ON true
    WHERE po.copied_trader_wallet <> ''

    UNION ALL

    SELECT
        pp.copied_trader_wallet,
        COALESCE(NULLIF(gm.category, ''), 'unknown') AS category,
        0, 0, 0, 0,
        1,
        0, 0, 0,
        0, 0, 0, 0,
        pp.unrealized_pnl_usd,
        NULL::timestamptz,
        NULL::timestamptz
    FROM paper_positions pp
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = pp.copied_trader_wallet
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE market.condition_id = pp.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) gm ON true
    WHERE pp.copied_trader_wallet <> ''
      AND pp.size_shares > 0

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
    JOIN temp_paper_copied_trader_performance_wallets selected
      ON selected.copied_trader_wallet = ps.copied_trader_wallet
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE NULLIF(ps.category, '') IS NULL
          AND market.condition_id = ps.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) gm ON true
    WHERE ps.copied_trader_wallet <> ''
),
grouped AS (
    SELECT
           copied_trader_wallet,
           CASE WHEN GROUPING(category) = 1 THEN 'OVERALL' ELSE category END AS category,
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
    GROUP BY GROUPING SETS (
        (copied_trader_wallet, category),
        (copied_trader_wallet)
    )
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
				command.CommandTimeout = PaperCopiedTraderPerformanceCommandTimeoutSeconds;
				performanceRowsWritten = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);

				await using NpgsqlCommand clearInflightCommand = CreateCommand(connection, """
DELETE FROM paper_copied_trader_performance_refresh_inflight inflight
USING temp_paper_copied_trader_performance_wallets selected
WHERE inflight.copied_trader_wallet = selected.copied_trader_wallet;
""");
				clearInflightCommand.Transaction = transaction;
				await clearInflightCommand.ExecuteNonQueryAsync(cancellationToken);
			}
			var paperPositionsStatsAfterAggregation = await PostgresPaperPositionsScanTelemetry.ReadAsync(
				connection,
				transaction,
				cancellationToken);
			var paperPositionsAggregationScanDelta = PostgresPaperPositionsScanStats.Delta(
				paperPositionsStatsAfterSeed,
				paperPositionsStatsAfterAggregation);

			var highPriorityQueueRemaining = 0;
			var reconciliationQueueRemaining = 0;
			await using (NpgsqlCommand remainingCommand = CreateCommand(connection, """
SELECT
    (
        (SELECT count(*) FROM paper_copied_trader_performance_refresh_queue WHERE priority > 0)
        +
        (SELECT count(*) FROM paper_copied_trader_performance_refresh_inflight WHERE work_kind = 'high_priority')
    )::integer AS high_priority_remaining,
    (
        (SELECT count(*) FROM paper_copied_trader_performance_refresh_queue WHERE priority <= 0)
        +
        (SELECT count(*) FROM paper_copied_trader_performance_refresh_inflight WHERE work_kind = 'reconciliation')
    )::integer AS reconciliation_remaining;
"""))
			{
				remainingCommand.Transaction = transaction;
				await using NpgsqlDataReader reader = await remainingCommand.ExecuteReaderAsync(cancellationToken);
				if (await reader.ReadAsync(cancellationToken))
				{
					highPriorityQueueRemaining = reader.GetInt32(0);
					reconciliationQueueRemaining = reader.GetInt32(1);
				}
			}

			await transaction.CommitAsync(cancellationToken);
			var queueRemaining = highPriorityQueueRemaining + reconciliationQueueRemaining;
			return new PaperCopiedTraderPerformanceRefreshResult(
				LockAcquired: true,
				WalletsSeeded: walletsSeeded,
				WalletsProcessed: walletsProcessed,
				PerformanceRowsWritten: performanceRowsWritten,
				QueueRemaining: queueRemaining,
				ReconciliationCycleCompleted: reconciliationCycleCompleted,
				HighPriorityWalletsProcessed: highPriorityWalletsProcessed,
				ReconciliationWalletsProcessed: reconciliationWalletsProcessed,
				HighPriorityQueueRemaining: highPriorityQueueRemaining,
				ReconciliationQueueRemaining: reconciliationQueueRemaining,
				PaperPositionsSeedSequentialScans: paperPositionsSeedScanDelta?.SequentialScans,
				PaperPositionsSeedSequentialTuplesRead: paperPositionsSeedScanDelta?.SequentialTuplesRead,
				PaperPositionsAggregationSequentialScans: paperPositionsAggregationScanDelta?.SequentialScans,
				PaperPositionsAggregationSequentialTuplesRead: paperPositionsAggregationScanDelta?.SequentialTuplesRead);
		}
		catch (Exception ex)
		{
			operationException = ex;
			throw;
		}
		finally
		{
			try
			{
				await ReleasePaperCopiedTraderPerformanceRefreshLockAsync(connection, CancellationToken.None);
			}
			catch (Exception unlockException) when (operationException is not null)
			{
				operationException.Data["PaperCopiedTraderPerformanceRefreshUnlockError"] = unlockException.Message;
			}
		}
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

	public async Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(int limit = 25_000, CancellationToken cancellationToken = default(CancellationToken))
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
countertrend_score_rows AS (
    SELECT
        strategy_id,
        created_at_utc,
        COALESCE(
            CASE
                WHEN jsonb_typeof(raw_decision_json -> 'previous_score_bps') = 'number'
                THEN round((raw_decision_json ->> 'previous_score_bps')::numeric, 8)::numeric(28,8)
                ELSE NULL
            END,
            CASE
                WHEN jsonb_typeof(raw_decision_json -> 'previous_score') = 'number'
                THEN round((raw_decision_json ->> 'previous_score')::numeric * 10000, 8)::numeric(28,8)
                ELSE NULL
            END
        ) AS previous_score_bps,
        CASE
            WHEN jsonb_typeof(raw_decision_json -> 'selected_signal_bps') = 'number'
            THEN round((raw_decision_json ->> 'selected_signal_bps')::numeric, 8)::numeric(28,8)
            ELSE NULL
        END AS selected_signal_bps
    FROM paper_orders
    WHERE raw_decision_json IS NOT NULL
      AND (raw_decision_json ? 'previous_score' OR raw_decision_json ? 'previous_score_bps')
),
countertrend_signal_rows AS (
    SELECT
        strategy_id,
        created_at_utc,
        previous_score_bps,
        round(COALESCE(selected_signal_bps, abs(previous_score_bps)), 8)::numeric(28,8) AS signal_bps
    FROM countertrend_score_rows
    WHERE previous_score_bps IS NOT NULL
),
countertrend_signal_agg AS (
    SELECT
        strategy_id,
        COALESCE(round(avg(previous_score_bps), 8), 0)::numeric(28,8) AS avg_countertrend_score_bps,
        COALESCE(round(avg(signal_bps), 8), 0)::numeric(28,8) AS avg_countertrend_signal_bps,
        ((array_agg(signal_bps ORDER BY created_at_utc DESC) FILTER (WHERE signal_bps IS NOT NULL))[1])::numeric(28,8) AS last_countertrend_signal_bps
    FROM countertrend_signal_rows
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
run_rows AS (
    SELECT
        run.*,
        strategy.live_enabled_at_utc
    FROM strategy_market_paper_runs run
    INNER JOIN strategies strategy ON strategy.id = run.strategy_id
),
run_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS runs_count,
        (count(*) FILTER (WHERE status = 'Observed'))::integer AS observed_runs_count,
        (count(*) FILTER (WHERE status = 'Entered'))::integer AS entered_runs_count,
        (count(*) FILTER (WHERE status = 'Skipped'))::integer AS skipped_runs_count,
        (count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NULL))::integer AS paper_condition_skipped_runs_count,
        (count(*) FILTER (WHERE status = 'Skipped' AND paper_order_id IS NOT NULL))::integer AS paper_not_accepted_runs_count,
        (count(*) FILTER (
            WHERE status = 'Skipped'
              AND live_enabled_at_utc IS NOT NULL
              AND updated_at_utc >= live_enabled_at_utc
              AND (
                  lower(COALESCE(skip_reason, '')) IN (
                      'btc_reference_move_below_bps_threshold',
                      'btc_reference_equal_market_start',
                      'btc_reference_equal_mean',
                      'btc_reference_mixed_around_mean',
                      'btc_market_results_not_consecutive',
                      'btc_previous_score_countertrend_rejected',
                      'btc_previous_score_neutral',
                      'btc_previous_score_down_time_share_below_threshold',
                      'btc_previous_score_up_time_share_below_threshold',
                      'btc_clever_fair_value_below_margin',
                      'btc_clever_fair_value_rejected',
                      'markov_edge_below_threshold',
                      'martin_not_triggered',
                      'strategy_selector_no_candidate_current_entry',
                      'gtd_limit_decision_rejected'
                  )
                  OR lower(COALESCE(skip_reason, '')) LIKE '%threshold%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%edge%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%countertrend%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%neutral%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%not_triggered%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%no_candidate%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%spread_too_wide%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%price_cap%'
              )
        ))::integer AS live_condition_skipped_orders_count,
        (count(*) FILTER (
            WHERE status = 'Skipped'
              AND live_enabled_at_utc IS NOT NULL
              AND updated_at_utc >= live_enabled_at_utc
              AND lower(COALESCE(skip_reason, '')) <> 'gtd_limit_not_filled'
              AND NOT (
                  lower(COALESCE(skip_reason, '')) IN (
                      'btc_reference_move_below_bps_threshold',
                      'btc_reference_equal_market_start',
                      'btc_reference_equal_mean',
                      'btc_reference_mixed_around_mean',
                      'btc_market_results_not_consecutive',
                      'btc_previous_score_countertrend_rejected',
                      'btc_previous_score_neutral',
                      'btc_previous_score_down_time_share_below_threshold',
                      'btc_previous_score_up_time_share_below_threshold',
                      'btc_clever_fair_value_below_margin',
                      'btc_clever_fair_value_rejected',
                      'markov_edge_below_threshold',
                      'martin_not_triggered',
                      'strategy_selector_no_candidate_current_entry',
                      'gtd_limit_decision_rejected'
                  )
                  OR lower(COALESCE(skip_reason, '')) LIKE '%threshold%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%edge%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%countertrend%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%neutral%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%not_triggered%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%no_candidate%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%spread_too_wide%'
                  OR lower(COALESCE(skip_reason, '')) LIKE '%price_cap%'
              )
        ))::integer AS live_technical_skipped_orders_count,
        (count(*) FILTER (
            WHERE status = 'Skipped'
              AND live_enabled_at_utc IS NOT NULL
              AND updated_at_utc >= live_enabled_at_utc
              AND lower(COALESCE(skip_reason, '')) = 'gtd_limit_not_filled'
        ))::integer AS live_ignored_gtd_unfilled_count,
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
    FROM run_rows
    GROUP BY strategy_id
),
paper_skip_rollup_agg AS (
    SELECT
        strategy_id,
        sum(run_count)::integer AS runs_count,
        max(last_updated_at_utc) AS last_run_utc
    FROM strategy_paper_skip_rollups
    GROUP BY strategy_id
),
live_order_agg AS (
    SELECT
        strategy_id,
        count(*)::integer AS live_orders_count,
        (count(*) FILTER (WHERE filled_size > 0))::integer AS live_filled_orders_count,
        (count(*) FILTER (WHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested') AND remaining_size > 0))::integer AS live_open_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND realized_pnl_usd IS NOT NULL))::integer AS live_settled_orders_count,
        (count(*) FILTER (WHERE status = 'PreflightRejected'))::integer AS live_technical_skipped_orders_count,
        (count(*) FILTER (
            WHERE status IN ('Rejected', 'Error')
        ))::integer AS live_ignored_rejected_orders_count,
        (count(*) FILTER (
            WHERE status IN ('Cancelled', 'CancelFailed')
              AND filled_size <= 0
        ))::integer AS live_ignored_cancelled_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)))::integer AS live_won_orders_count,
        (count(*) FILTER (WHERE settled_at_utc IS NOT NULL AND NOT COALESCE(won, COALESCE(settlement_value_usd, 0) > 0)))::integer AS live_lost_orders_count,
        COALESCE(sum(CASE
            WHEN filled_notional_usd > 0 THEN filled_notional_usd
            WHEN filled_size > 0 THEN price * filled_size
            WHEN cost_basis_usd > 0 THEN GREATEST(0, cost_basis_usd - fee_usd)
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
        strategy.paused AND (strategy.paused_until_utc IS NULL OR strategy.paused_until_utc > @NowUtc) AS paused,
        CASE
            WHEN strategy.paused AND (strategy.paused_until_utc IS NULL OR strategy.paused_until_utc > @NowUtc) THEN strategy.paused_until_utc
            ELSE NULL
        END AS paused_until_utc,
        strategy.paper_stake_amount,
        strategy.live_stake_amount,
        strategy.paper_lost_coeff,
        strategy.live_lost_coeff,
        strategy.paper_lost_counter,
        strategy.live_lost_counter,
        strategy.live_available_balance,
        COALESCE(order_agg.orders_count, 0) AS orders_count,
        COALESCE(order_agg.filled_orders_count, 0) AS filled_orders_count,
        COALESCE(order_agg.open_orders_count, 0) AS open_orders_count,
        COALESCE(position_agg.open_positions_count, 0) AS open_positions_count,
        COALESCE(run_agg.observed_runs_count, 0) AS observed_runs_count,
        COALESCE(run_agg.entered_runs_count, 0) AS entered_runs_count,
        COALESCE(run_agg.skipped_runs_count, 0)
            + COALESCE(paper_skip_rollup_agg.runs_count, 0) AS skipped_runs_count,
        COALESCE(run_agg.paper_condition_skipped_runs_count, 0)
            + COALESCE(paper_skip_rollup_agg.runs_count, 0) AS paper_condition_skipped_runs_count,
        COALESCE(run_agg.paper_not_accepted_runs_count, 0) AS paper_not_accepted_runs_count,
        COALESCE(run_agg.settled_runs_count, 0) AS settled_runs_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.settled_runs_count, 0)
            ELSE COALESCE(settlement_agg.settled_positions_count, 0)
        END AS settled_positions_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.won_runs_count, 0)
            ELSE COALESCE(settlement_agg.won_positions_count, 0)
        END AS won_positions_count,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.lost_runs_count, 0)
            ELSE COALESCE(settlement_agg.lost_positions_count, 0)
        END AS lost_positions_count,
        CASE
            WHEN COALESCE(order_agg.buy_notional_usd, 0) > 0 THEN COALESCE(order_agg.buy_notional_usd, 0)
            WHEN COALESCE(run_agg.settled_stake_usd, 0) > 0 THEN COALESCE(run_agg.settled_stake_usd, 0)
            ELSE COALESCE(settlement_agg.cost_basis_usd, 0)
        END AS stake_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.settled_stake_usd, 0)
            ELSE COALESCE(settlement_agg.cost_basis_usd, 0) + COALESCE(fill_agg.closed_fill_cost_basis_usd, 0)
        END AS closed_stake_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.realized_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.realized_pnl_usd, 0) + COALESCE(fill_agg.realized_fill_pnl_usd, 0)
        END AS realized_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.avg_win_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.avg_win_pnl_usd, 0)
        END AS avg_win_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.avg_loss_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.avg_loss_pnl_usd, 0)
        END AS avg_loss_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.positive_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.positive_pnl_usd, 0)
        END AS closed_positive_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.loss_abs_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.loss_abs_pnl_usd, 0)
        END AS closed_loss_abs_pnl_usd,
        CASE
            WHEN COALESCE(run_agg.runs_count, 0) + COALESCE(paper_skip_rollup_agg.runs_count, 0) > 0 THEN COALESCE(run_agg.expectancy_pnl_usd, 0)
            ELSE COALESCE(settlement_agg.expectancy_pnl_usd, 0)
        END AS expectancy_pnl_usd,
        COALESCE(position_agg.unrealized_pnl_usd, 0) AS unrealized_pnl_usd,
        COALESCE(run_agg.avg_entry_delay_seconds, 0) AS avg_entry_delay_seconds,
        COALESCE(run_agg.max_entry_delay_seconds, 0) AS max_entry_delay_seconds,
        COALESCE(countertrend_signal_agg.avg_countertrend_score_bps, 0) AS avg_countertrend_score_bps,
        COALESCE(countertrend_signal_agg.avg_countertrend_signal_bps, 0) AS avg_countertrend_signal_bps,
        countertrend_signal_agg.last_countertrend_signal_bps,
        COALESCE(live_order_agg.live_orders_count, 0) AS live_orders_count,
        COALESCE(live_order_agg.live_filled_orders_count, 0) AS live_filled_orders_count,
        COALESCE(live_order_agg.live_open_orders_count, 0) AS live_open_orders_count,
        COALESCE(live_order_agg.live_settled_orders_count, 0) AS live_settled_orders_count,
        CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_condition_skipped_orders_count, 0) ELSE 0 END AS live_condition_skipped_orders_count,
        CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_technical_skipped_orders_count, 0) ELSE 0 END
            + COALESCE(live_order_agg.live_technical_skipped_orders_count, 0) AS live_technical_skipped_orders_count,
        CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END
            + COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0)
            + COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_ignored_orders_count,
        CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END AS live_ignored_gtd_unfilled_count,
        COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0) AS live_ignored_cancelled_orders_count,
        COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_ignored_rejected_orders_count,
        CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_condition_skipped_orders_count, 0) ELSE 0 END
            + CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_technical_skipped_orders_count, 0) ELSE 0 END
            + CASE WHEN strategy.live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END
            + COALESCE(live_order_agg.live_technical_skipped_orders_count, 0)
            + COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0)
            + COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_skipped_orders_count,
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
        GREATEST(run_agg.last_run_utc, paper_skip_rollup_agg.last_run_utc) AS last_run_utc
    FROM strategies strategy
    LEFT JOIN order_agg ON order_agg.strategy_id = strategy.id
    LEFT JOIN fill_agg ON fill_agg.strategy_id = strategy.id
    LEFT JOIN position_agg ON position_agg.strategy_id = strategy.id
    LEFT JOIN settlement_agg ON settlement_agg.strategy_id = strategy.id
    LEFT JOIN run_agg ON run_agg.strategy_id = strategy.id
    LEFT JOIN paper_skip_rollup_agg ON paper_skip_rollup_agg.strategy_id = strategy.id
    LEFT JOIN live_order_agg ON live_order_agg.strategy_id = strategy.id
    LEFT JOIN countertrend_signal_agg ON countertrend_signal_agg.strategy_id = strategy.id
)
SELECT
    strategy_id,
    code,
    name,
    enabled,
    live_stakes,
    paused,
    paused_until_utc,
    paper_stake_amount,
    live_stake_amount,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    live_available_balance,
    orders_count,
    filled_orders_count,
    open_orders_count,
    open_positions_count,
    observed_runs_count,
    entered_runs_count,
    skipped_runs_count,
    paper_condition_skipped_runs_count,
    paper_not_accepted_runs_count,
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
    avg_countertrend_score_bps,
    avg_countertrend_signal_bps,
    last_countertrend_signal_bps,
    live_orders_count,
    live_filled_orders_count,
    live_open_orders_count,
    live_settled_orders_count,
    live_skipped_orders_count,
    live_condition_skipped_orders_count,
    live_technical_skipped_orders_count,
    live_ignored_orders_count,
    live_ignored_gtd_unfilled_count,
    live_ignored_cancelled_orders_count,
    live_ignored_rejected_orders_count,
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
		command.CommandTimeout = StrategyPerformanceCommandTimeoutSeconds;
		command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
		command.Parameters.AddWithValue("NowUtc", UtcDateTime(DateTimeOffset.UtcNow));
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
				reader.GetBoolean(5),
				reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
				reader.GetDecimal(7),
				reader.GetDecimal(8),
				reader.GetDecimal(9),
				reader.GetDecimal(10),
				reader.GetInt32(11),
				reader.GetInt32(12),
				reader.GetDecimal(13),
				reader.GetInt32(14),
				reader.GetInt32(15),
				reader.GetInt32(16),
				reader.GetInt32(17),
				reader.GetInt32(18),
				reader.GetInt32(19),
				reader.GetInt32(20),
				reader.GetInt32(21),
				reader.GetInt32(22),
				reader.GetInt32(23),
				reader.GetInt32(24),
				reader.GetInt32(25),
				reader.GetInt32(26),
				reader.GetDecimal(27),
				reader.GetDecimal(28),
				reader.GetDecimal(29),
				reader.GetDecimal(30),
				reader.GetDecimal(31),
				reader.GetDecimal(32),
				reader.GetDecimal(33),
				reader.GetDecimal(34),
				reader.IsDBNull(35) ? null : reader.GetDecimal(35),
				reader.GetDecimal(36),
				reader.GetDecimal(37),
				reader.GetDecimal(38),
				reader.GetDecimal(39),
				reader.GetDecimal(40),
				reader.GetDecimal(41),
				reader.GetDecimal(42),
				reader.IsDBNull(43) ? null : reader.GetDecimal(43),
				reader.GetInt32(44),
				reader.GetInt32(45),
				reader.GetInt32(46),
				reader.GetInt32(47),
				reader.GetInt32(48),
				reader.GetInt32(49),
				reader.GetInt32(50),
				reader.GetInt32(51),
				reader.GetInt32(52),
				reader.GetInt32(53),
				reader.GetInt32(54),
				reader.GetInt32(55),
				reader.GetInt32(56),
				reader.GetDecimal(57),
				reader.GetDecimal(58),
				reader.GetDecimal(59),
				reader.GetDecimal(60),
				reader.GetDecimal(61),
				reader.GetDecimal(62),
				reader.IsDBNull(63) ? null : reader.GetDecimal(63),
				reader.GetDecimal(64),
				reader.GetDecimal(65),
				reader.IsDBNull(66) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(66)),
				reader.IsDBNull(67) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(67)),
				reader.IsDBNull(68) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(68)),
				reader.IsDBNull(69) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(69))));
		}

		return results;
	}

	public async Task<IReadOnlyDictionary<Guid, decimal>> GetLiveRealizedPnlByStrategyAsync(IReadOnlyCollection<Guid> strategyIds, CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid[] normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		Dictionary<Guid, decimal> results = normalizedStrategyIds.ToDictionary(strategyId => strategyId, _ => 0m);
		if (normalizedStrategyIds.Length == 0)
		{
			return results;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT strategy_id,
       COALESCE(sum(realized_pnl_usd), 0) AS live_realized_pnl_usd
FROM live_orders
WHERE strategy_id = ANY(@StrategyIds)
  AND settled_at_utc IS NOT NULL
  AND realized_pnl_usd IS NOT NULL
GROUP BY strategy_id;
""");
		command.Parameters.AddWithValue("StrategyIds", normalizedStrategyIds);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			results[StrategyIds.Normalize(reader.GetGuid(0))] = reader.GetDecimal(1);
		}

		return results;
	}

	public async Task<int> RefreshDateDependentStrategyHourlyPaperPnlAsync(
		IReadOnlyCollection<Guid> strategyIds,
		DateTimeOffset refreshedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid[] normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0)
		{
			return 0;
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH selected_strategy_ids AS (
    SELECT unnest(@StrategyIds::uuid[]) AS strategy_id
),
selected_strategies AS (
    SELECT strategy.id, strategy.code, strategy.name
    FROM strategies strategy
    INNER JOIN selected_strategy_ids selected ON selected.strategy_id = strategy.id
),
hours AS (
    SELECT generate_series(0, 23)::integer AS hour_utc
),
run_agg AS (
    SELECT
        run.strategy_id,
        EXTRACT(HOUR FROM run.entered_at_utc AT TIME ZONE 'UTC')::integer AS hour_utc,
        count(*)::integer AS settled_runs_count,
        (count(*) FILTER (WHERE COALESCE(run.realized_pnl_usd, 0) > 0))::integer AS won_runs_count,
        (count(*) FILTER (WHERE COALESCE(run.realized_pnl_usd, 0) < 0))::integer AS lost_runs_count,
        COALESCE(sum(run.stake_usd), 0)::numeric(28,8) AS stake_usd,
        COALESCE(sum(run.realized_pnl_usd), 0)::numeric(28,8) AS realized_pnl_usd,
        COALESCE(avg(run.realized_pnl_usd), 0)::numeric(28,8) AS avg_pnl_usd,
        min(run.entered_at_utc) AS first_entered_at_utc,
        max(run.entered_at_utc) AS last_entered_at_utc
    FROM strategy_market_paper_runs run
    INNER JOIN selected_strategies strategy ON strategy.id = run.strategy_id
    WHERE run.status = 'Settled'
      AND run.entered_at_utc IS NOT NULL
      AND run.realized_pnl_usd IS NOT NULL
    GROUP BY
        run.strategy_id,
        EXTRACT(HOUR FROM run.entered_at_utc AT TIME ZONE 'UTC')::integer
),
upserted AS (
    INSERT INTO date_dependent_strategy_hourly_paper_pnl (
        strategy_id,
        code,
        name,
        hour_utc,
        settled_runs_count,
        won_runs_count,
        lost_runs_count,
        stake_usd,
        realized_pnl_usd,
        avg_pnl_usd,
        first_entered_at_utc,
        last_entered_at_utc,
        refreshed_at_utc
    )
    SELECT
        strategy.id,
        strategy.code,
        strategy.name,
        hour_row.hour_utc,
        COALESCE(run_agg.settled_runs_count, 0),
        COALESCE(run_agg.won_runs_count, 0),
        COALESCE(run_agg.lost_runs_count, 0),
        COALESCE(run_agg.stake_usd, 0),
        COALESCE(run_agg.realized_pnl_usd, 0),
        COALESCE(run_agg.avg_pnl_usd, 0),
        run_agg.first_entered_at_utc,
        run_agg.last_entered_at_utc,
        CAST(@RefreshedAtUtc AS timestamptz)
    FROM selected_strategies strategy
    CROSS JOIN hours hour_row
    LEFT JOIN run_agg
        ON run_agg.strategy_id = strategy.id
       AND run_agg.hour_utc = hour_row.hour_utc
    ON CONFLICT (strategy_id, hour_utc) DO UPDATE SET
        code = EXCLUDED.code,
        name = EXCLUDED.name,
        settled_runs_count = EXCLUDED.settled_runs_count,
        won_runs_count = EXCLUDED.won_runs_count,
        lost_runs_count = EXCLUDED.lost_runs_count,
        stake_usd = EXCLUDED.stake_usd,
        realized_pnl_usd = EXCLUDED.realized_pnl_usd,
        avg_pnl_usd = EXCLUDED.avg_pnl_usd,
        first_entered_at_utc = EXCLUDED.first_entered_at_utc,
        last_entered_at_utc = EXCLUDED.last_entered_at_utc,
        refreshed_at_utc = EXCLUDED.refreshed_at_utc
    RETURNING 1
),
deleted AS (
    DELETE FROM date_dependent_strategy_hourly_paper_pnl snapshot
    WHERE NOT EXISTS (
        SELECT 1
        FROM selected_strategy_ids selected
        WHERE selected.strategy_id = snapshot.strategy_id
    )
    RETURNING 1
)
SELECT (SELECT count(*) FROM upserted)::integer AS upserted_rows,
       (SELECT count(*) FROM deleted)::integer AS deleted_rows;
""");
		command.CommandTimeout = StrategyPerformanceCommandTimeoutSeconds;
		command.Parameters.Add("StrategyIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = normalizedStrategyIds;
		command.Parameters.AddWithValue("RefreshedAtUtc", UtcDateTime(refreshedAtUtc));
		object? value = await command.ExecuteScalarAsync(cancellationToken);
		return value is null or DBNull ? 0 : Convert.ToInt32(value);
	}

	public async Task<decimal?> GetDateDependentStrategyHourlyPaperPnlAsync(
		Guid strategyId,
		int hourUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (hourUtc is < 0 or > 23)
		{
			throw new ArgumentOutOfRangeException(nameof(hourUtc), hourUtc, "UTC hour must be in the range 0..23.");
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT realized_pnl_usd
FROM date_dependent_strategy_hourly_paper_pnl
WHERE strategy_id = @StrategyId
  AND hour_utc = @HourUtc;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("HourUtc", hourUtc);
		object? value = await command.ExecuteScalarAsync(cancellationToken);
		return value is null or DBNull ? null : (decimal)value;
	}

	public async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(int limit = 25_000, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH selected_strategies AS (
    SELECT id, code, name, live_stakes, live_enabled_at_utc, live_stakes AS effective_live_stakes
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
        strategy.live_stakes,
        strategy.effective_live_stakes,
        window_row.window_label,
        window_row.window_hours,
        window_row.window_start_utc,
        window_row.window_end_utc
    FROM selected_strategies strategy
    CROSS JOIN windows window_row
),
order_agg AS (
    SELECT
        paper_order.strategy_id,
        window_row.window_label,
        count(*)::integer AS orders_count,
        (count(*) FILTER (WHERE paper_order.status IN ('Filled', 'PartiallyFilled', 'PartiallyFilledExpired')))::integer AS filled_orders_count,
        (count(*) FILTER (WHERE paper_order.status IN ('Expired', 'PartiallyFilledExpired')))::integer AS expired_orders_count,
        (count(*) FILTER (WHERE paper_order.status IN ('Pending', 'PartiallyFilled')))::integer AS open_orders_count,
        max(paper_order.created_at_utc) AS last_order_utc
    FROM paper_orders paper_order
    INNER JOIN selected_strategies strategy ON strategy.id = paper_order.strategy_id
    INNER JOIN windows window_row
        ON paper_order.created_at_utc >= window_row.window_start_utc
        AND paper_order.created_at_utc <= window_row.window_end_utc
    GROUP BY paper_order.strategy_id, window_row.window_label
),
fill_agg AS (
    SELECT
        paper_order.strategy_id,
        window_row.window_label,
        COALESCE(sum(fill_row.price * fill_row.size_shares), 0) AS filled_cost_usd,
        CASE
            WHEN COALESCE(sum(fill_row.size_shares), 0) = 0 THEN 0
            ELSE COALESCE(sum(fill_row.price * fill_row.size_shares), 0) / sum(fill_row.size_shares)
        END AS avg_fill_price
    FROM paper_fills fill_row
    INNER JOIN paper_orders paper_order ON paper_order.id = fill_row.paper_order_id
    INNER JOIN selected_strategies strategy ON strategy.id = paper_order.strategy_id
    INNER JOIN windows window_row
        ON fill_row.filled_at_utc >= window_row.window_start_utc
        AND fill_row.filled_at_utc <= window_row.window_end_utc
    GROUP BY paper_order.strategy_id, window_row.window_label
),
raw_run_window_rows AS (
    SELECT
        run.strategy_id,
        run.status,
        run.stake_usd,
        run.realized_pnl_usd,
        run.entered_at_utc,
        run.entry_due_at_utc,
        run.settled_at_utc,
        run.updated_at_utc,
        run.skip_reason,
        run.paper_order_id,
        strategy.live_enabled_at_utc,
        window_row.window_label,
        window_row.window_start_utc,
        window_row.window_end_utc
    FROM strategy_market_paper_runs run
    INNER JOIN selected_strategies strategy ON strategy.id = run.strategy_id
    INNER JOIN windows window_row
        ON (
            run.entered_at_utc >= window_row.window_start_utc
            AND run.entered_at_utc <= window_row.window_end_utc
        )
        OR (
            run.updated_at_utc >= window_row.window_start_utc
            AND run.updated_at_utc <= window_row.window_end_utc
        )
        OR (
            run.settled_at_utc >= window_row.window_start_utc
            AND run.settled_at_utc <= window_row.window_end_utc
        )
),
archived_skip_window_rows AS (
    SELECT
        tombstone.strategy_id,
        'Skipped'::text AS status,
        tombstone.stake_usd,
        NULL::numeric AS realized_pnl_usd,
        NULL::timestamptz AS entered_at_utc,
        tombstone.entry_due_at_utc,
        NULL::timestamptz AS settled_at_utc,
        tombstone.run_updated_at_utc AS updated_at_utc,
        tombstone.skip_reason,
        NULL::uuid AS paper_order_id,
        NULL::timestamptz AS live_enabled_at_utc,
        window_row.window_label,
        window_row.window_start_utc,
        window_row.window_end_utc
    FROM strategy_market_paper_skip_archive_rows tombstone
    INNER JOIN selected_strategies strategy ON strategy.id = tombstone.strategy_id
    INNER JOIN windows window_row
        ON tombstone.run_updated_at_utc >= window_row.window_start_utc
       AND tombstone.run_updated_at_utc <= window_row.window_end_utc
    WHERE tombstone.run_updated_at_utc >= CAST(@NowUtc AS timestamptz) - interval '24 hours'
      AND tombstone.run_updated_at_utc <= CAST(@NowUtc AS timestamptz)
),
run_window_rows AS (
    SELECT
        strategy_id,
        status,
        stake_usd,
        realized_pnl_usd,
        entered_at_utc,
        entry_due_at_utc,
        settled_at_utc,
        updated_at_utc,
        skip_reason,
        paper_order_id,
        live_enabled_at_utc,
        window_label,
        window_start_utc,
        window_end_utc
    FROM raw_run_window_rows
    UNION ALL
    SELECT
        strategy_id,
        status,
        stake_usd,
        realized_pnl_usd,
        entered_at_utc,
        entry_due_at_utc,
        settled_at_utc,
        updated_at_utc,
        skip_reason,
        paper_order_id,
        live_enabled_at_utc,
        window_label,
        window_start_utc,
        window_end_utc
    FROM archived_skip_window_rows
),
run_agg AS (
    SELECT
        run.strategy_id,
        run.window_label,
        (count(*) FILTER (WHERE run.entered_at_utc >= run.window_start_utc AND run.entered_at_utc <= run.window_end_utc))::integer AS entered_runs_count,
        (count(*) FILTER (WHERE run.status = 'Skipped' AND run.updated_at_utc >= run.window_start_utc AND run.updated_at_utc <= run.window_end_utc))::integer AS skipped_runs_count,
        (count(*) FILTER (
            WHERE run.status = 'Skipped'
              AND run.updated_at_utc >= run.window_start_utc
              AND run.updated_at_utc <= run.window_end_utc
              AND run.paper_order_id IS NULL
        ))::integer AS paper_condition_skipped_runs_count,
        (count(*) FILTER (
            WHERE run.status = 'Skipped'
              AND run.updated_at_utc >= run.window_start_utc
              AND run.updated_at_utc <= run.window_end_utc
              AND run.paper_order_id IS NOT NULL
        ))::integer AS paper_not_accepted_runs_count,
        (count(*) FILTER (
            WHERE run.status = 'Skipped'
              AND run.updated_at_utc >= run.window_start_utc
              AND run.updated_at_utc <= run.window_end_utc
              AND run.live_enabled_at_utc IS NOT NULL
              AND run.updated_at_utc >= run.live_enabled_at_utc
              AND (
                  lower(COALESCE(run.skip_reason, '')) IN (
                      'btc_reference_move_below_bps_threshold',
                      'btc_reference_equal_market_start',
                      'btc_reference_equal_mean',
                      'btc_reference_mixed_around_mean',
                      'btc_market_results_not_consecutive',
                      'btc_previous_score_countertrend_rejected',
                      'btc_previous_score_neutral',
                      'btc_previous_score_down_time_share_below_threshold',
                      'btc_previous_score_up_time_share_below_threshold',
                      'btc_clever_fair_value_below_margin',
                      'btc_clever_fair_value_rejected',
                      'markov_edge_below_threshold',
                      'martin_not_triggered',
                      'strategy_selector_no_candidate_current_entry',
                      'gtd_limit_decision_rejected'
                  )
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%threshold%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%edge%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%countertrend%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%neutral%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%not_triggered%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%no_candidate%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%spread_too_wide%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%price_cap%'
              )
        ))::integer AS live_condition_skipped_orders_count,
        (count(*) FILTER (
            WHERE run.status = 'Skipped'
              AND run.updated_at_utc >= run.window_start_utc
              AND run.updated_at_utc <= run.window_end_utc
              AND run.live_enabled_at_utc IS NOT NULL
              AND run.updated_at_utc >= run.live_enabled_at_utc
              AND lower(COALESCE(run.skip_reason, '')) <> 'gtd_limit_not_filled'
              AND NOT (
                  lower(COALESCE(run.skip_reason, '')) IN (
                      'btc_reference_move_below_bps_threshold',
                      'btc_reference_equal_market_start',
                      'btc_reference_equal_mean',
                      'btc_reference_mixed_around_mean',
                      'btc_market_results_not_consecutive',
                      'btc_previous_score_countertrend_rejected',
                      'btc_previous_score_neutral',
                      'btc_previous_score_down_time_share_below_threshold',
                      'btc_previous_score_up_time_share_below_threshold',
                      'btc_clever_fair_value_below_margin',
                      'btc_clever_fair_value_rejected',
                      'markov_edge_below_threshold',
                      'martin_not_triggered',
                      'strategy_selector_no_candidate_current_entry',
                      'gtd_limit_decision_rejected'
                  )
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%threshold%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%edge%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%countertrend%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%neutral%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%not_triggered%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%no_candidate%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%spread_too_wide%'
                  OR lower(COALESCE(run.skip_reason, '')) LIKE '%price_cap%'
              )
        ))::integer AS live_technical_skipped_orders_count,
        (count(*) FILTER (
            WHERE run.status = 'Skipped'
              AND run.updated_at_utc >= run.window_start_utc
              AND run.updated_at_utc <= run.window_end_utc
              AND run.live_enabled_at_utc IS NOT NULL
              AND run.updated_at_utc >= run.live_enabled_at_utc
              AND lower(COALESCE(run.skip_reason, '')) = 'gtd_limit_not_filled'
        ))::integer AS live_ignored_gtd_unfilled_count,
        (count(*) FILTER (WHERE run.status = 'Settled' AND run.settled_at_utc >= run.window_start_utc AND run.settled_at_utc <= run.window_end_utc))::integer AS settled_runs_count,
        (count(*) FILTER (WHERE run.status = 'Settled' AND run.settled_at_utc >= run.window_start_utc AND run.settled_at_utc <= run.window_end_utc AND COALESCE(run.realized_pnl_usd, 0) > 0))::integer AS won_runs_count,
        (count(*) FILTER (WHERE run.status = 'Settled' AND run.settled_at_utc >= run.window_start_utc AND run.settled_at_utc <= run.window_end_utc AND COALESCE(run.realized_pnl_usd, 0) < 0))::integer AS lost_runs_count,
        COALESCE(sum(run.stake_usd) FILTER (WHERE run.status = 'Settled' AND run.settled_at_utc >= run.window_start_utc AND run.settled_at_utc <= run.window_end_utc), 0) AS settled_stake_usd,
        COALESCE(sum(COALESCE(run.realized_pnl_usd, 0)) FILTER (WHERE run.status = 'Settled' AND run.settled_at_utc >= run.window_start_utc AND run.settled_at_utc <= run.window_end_utc), 0) AS realized_pnl_usd,
        COALESCE(avg(GREATEST(0, EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc)))) FILTER (WHERE run.entered_at_utc >= run.window_start_utc AND run.entered_at_utc <= run.window_end_utc), 0)::numeric AS avg_entry_delay_seconds,
        COALESCE(max(GREATEST(0, EXTRACT(EPOCH FROM (run.entered_at_utc - run.entry_due_at_utc)))) FILTER (WHERE run.entered_at_utc >= run.window_start_utc AND run.entered_at_utc <= run.window_end_utc), 0)::numeric AS max_entry_delay_seconds,
        max(run.updated_at_utc) FILTER (WHERE run.updated_at_utc >= run.window_start_utc AND run.updated_at_utc <= run.window_end_utc) AS last_run_utc
    FROM run_window_rows run
    GROUP BY run.strategy_id, run.window_label
),
top_skip_ranked AS (
    SELECT
        run.strategy_id,
        run.window_label,
        concat(run.skip_reason, ':', count(*)) AS top_skip_reason,
        row_number() OVER (
            PARTITION BY run.strategy_id, run.window_label
            ORDER BY count(*) DESC, run.skip_reason
        ) AS skip_rank
    FROM run_window_rows run
    WHERE run.status = 'Skipped'
      AND run.updated_at_utc >= run.window_start_utc
      AND run.updated_at_utc <= run.window_end_utc
      AND run.skip_reason IS NOT NULL
      AND run.skip_reason <> ''
    GROUP BY run.strategy_id, run.window_label, run.skip_reason
),
top_skip AS (
    SELECT strategy_id, window_label, top_skip_reason
    FROM top_skip_ranked
    WHERE skip_rank = 1
),
live_order_window_rows AS (
    SELECT
        live_order.strategy_id,
        live_order.status,
        live_order.created_at_utc,
        live_order.updated_at_utc,
        live_order.settled_at_utc,
        live_order.cost_basis_usd,
        live_order.filled_notional_usd,
        live_order.fee_usd,
        live_order.filled_size,
        live_order.price,
        live_order.settlement_value_usd,
        live_order.realized_pnl_usd,
        live_order.won,
        window_row.window_label,
        window_row.window_start_utc,
        window_row.window_end_utc
    FROM live_orders live_order
    INNER JOIN selected_strategies strategy ON strategy.id = live_order.strategy_id
    INNER JOIN windows window_row
        ON (
            live_order.created_at_utc >= window_row.window_start_utc
            AND live_order.created_at_utc <= window_row.window_end_utc
        )
        OR (
            live_order.updated_at_utc >= window_row.window_start_utc
            AND live_order.updated_at_utc <= window_row.window_end_utc
        )
        OR (
            live_order.settled_at_utc >= window_row.window_start_utc
            AND live_order.settled_at_utc <= window_row.window_end_utc
        )
),
live_order_agg AS (
    SELECT
        live_order.strategy_id,
        live_order.window_label,
        (count(*) FILTER (
            WHERE live_order.settled_at_utc >= live_order.window_start_utc
              AND live_order.settled_at_utc <= live_order.window_end_utc
              AND live_order.realized_pnl_usd IS NOT NULL
        ))::integer AS live_settled_orders_count,
        (count(*) FILTER (
            WHERE live_order.status = 'PreflightRejected'
              AND live_order.created_at_utc >= live_order.window_start_utc
              AND live_order.created_at_utc <= live_order.window_end_utc
        ))::integer AS live_technical_skipped_orders_count,
        (count(*) FILTER (
            WHERE live_order.status IN ('Rejected', 'Error')
              AND live_order.created_at_utc >= live_order.window_start_utc
              AND live_order.created_at_utc <= live_order.window_end_utc
        ))::integer AS live_ignored_rejected_orders_count,
        (count(*) FILTER (
            WHERE live_order.status IN ('Cancelled', 'CancelFailed')
              AND live_order.filled_size <= 0
              AND live_order.created_at_utc >= live_order.window_start_utc
              AND live_order.created_at_utc <= live_order.window_end_utc
        ))::integer AS live_ignored_cancelled_orders_count,
        (count(*) FILTER (
            WHERE live_order.settled_at_utc >= live_order.window_start_utc
              AND live_order.settled_at_utc <= live_order.window_end_utc
              AND COALESCE(live_order.won, COALESCE(live_order.settlement_value_usd, 0) > 0)
        ))::integer AS live_won_orders_count,
        (count(*) FILTER (
            WHERE live_order.settled_at_utc >= live_order.window_start_utc
              AND live_order.settled_at_utc <= live_order.window_end_utc
              AND NOT COALESCE(live_order.won, COALESCE(live_order.settlement_value_usd, 0) > 0)
        ))::integer AS live_lost_orders_count,
        COALESCE(sum(CASE
            WHEN live_order.filled_notional_usd > 0 THEN live_order.filled_notional_usd
            WHEN live_order.filled_size > 0 THEN live_order.price * live_order.filled_size
            WHEN live_order.cost_basis_usd > 0 THEN GREATEST(0, live_order.cost_basis_usd - live_order.fee_usd)
            ELSE 0
        END) FILTER (
            WHERE live_order.settled_at_utc >= live_order.window_start_utc
              AND live_order.settled_at_utc <= live_order.window_end_utc
        ), 0) AS live_stake_usd,
        COALESCE(sum(COALESCE(live_order.realized_pnl_usd, 0)) FILTER (
            WHERE live_order.settled_at_utc >= live_order.window_start_utc
              AND live_order.settled_at_utc <= live_order.window_end_utc
        ), 0) AS live_realized_pnl_usd
    FROM live_order_window_rows live_order
    GROUP BY live_order.strategy_id, live_order.window_label
)
SELECT
    sw.strategy_id,
    sw.code,
    sw.name,
    sw.live_stakes,
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
    COALESCE(run_agg.paper_condition_skipped_runs_count, 0) AS paper_condition_skipped_runs_count,
    COALESCE(run_agg.paper_not_accepted_runs_count, 0) AS paper_not_accepted_runs_count,
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
    COALESCE(live_order_agg.live_settled_orders_count, 0) AS live_settled_orders_count,
    CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_condition_skipped_orders_count, 0) ELSE 0 END
        + CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_technical_skipped_orders_count, 0) ELSE 0 END
        + CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END
        + COALESCE(live_order_agg.live_technical_skipped_orders_count, 0)
        + COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0)
        + COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_skipped_orders_count,
    CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_condition_skipped_orders_count, 0) ELSE 0 END AS live_condition_skipped_orders_count,
    CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_technical_skipped_orders_count, 0) ELSE 0 END
        + COALESCE(live_order_agg.live_technical_skipped_orders_count, 0) AS live_technical_skipped_orders_count,
    CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END
        + COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0)
        + COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_ignored_orders_count,
    CASE WHEN sw.effective_live_stakes THEN COALESCE(run_agg.live_ignored_gtd_unfilled_count, 0) ELSE 0 END AS live_ignored_gtd_unfilled_count,
    COALESCE(live_order_agg.live_ignored_cancelled_orders_count, 0) AS live_ignored_cancelled_orders_count,
    COALESCE(live_order_agg.live_ignored_rejected_orders_count, 0) AS live_ignored_rejected_orders_count,
    COALESCE(live_order_agg.live_won_orders_count, 0) AS live_won_orders_count,
    COALESCE(live_order_agg.live_lost_orders_count, 0) AS live_lost_orders_count,
    COALESCE(live_order_agg.live_realized_pnl_usd, 0) AS live_realized_pnl_usd,
    CASE WHEN COALESCE(live_order_agg.live_stake_usd, 0) = 0 THEN 0 ELSE COALESCE(live_order_agg.live_realized_pnl_usd, 0) * 100.0 / live_order_agg.live_stake_usd END AS live_roi_pct,
    COALESCE(top_skip.top_skip_reason, '') AS top_skip_reason,
    order_agg.last_order_utc,
    run_agg.last_run_utc
FROM strategy_windows sw
LEFT JOIN order_agg
    ON order_agg.strategy_id = sw.strategy_id
    AND order_agg.window_label = sw.window_label
LEFT JOIN fill_agg
    ON fill_agg.strategy_id = sw.strategy_id
    AND fill_agg.window_label = sw.window_label
LEFT JOIN run_agg
    ON run_agg.strategy_id = sw.strategy_id
    AND run_agg.window_label = sw.window_label
LEFT JOIN top_skip
    ON top_skip.strategy_id = sw.strategy_id
    AND top_skip.window_label = sw.window_label
LEFT JOIN live_order_agg
    ON live_order_agg.strategy_id = sw.strategy_id
    AND live_order_agg.window_label = sw.window_label
ORDER BY
    CASE WHEN sw.code = 'follow_leader' THEN 0 ELSE 1 END,
    sw.code,
    sw.window_hours;
""");
		command.CommandTimeout = StrategyPerformanceCommandTimeoutSeconds;
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
				reader.GetBoolean(3),
				reader.GetString(4),
				reader.GetInt32(5),
				DateTimeOffsetFromUtc(reader.GetDateTime(6)),
				DateTimeOffsetFromUtc(reader.GetDateTime(7)),
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
				reader.GetInt32(26),
				reader.GetInt32(27),
				reader.GetInt32(28),
				reader.GetInt32(29),
				reader.GetInt32(30),
				reader.GetInt32(31),
				reader.GetInt32(32),
				reader.GetInt32(33),
				reader.GetInt32(34),
				reader.GetInt32(35),
				reader.GetDecimal(36),
				reader.GetDecimal(37),
				reader.GetString(38),
				reader.IsDBNull(39) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(39)),
				reader.IsDBNull(40) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(40))));
		}

		return results;
	}

	public async Task<IReadOnlyList<StrategyLookbackPnl>> GetStrategySettledPnlByLookbackHoursAsync(
		IReadOnlyCollection<Guid> strategyIds,
		DateTimeOffset nowUtc,
		int maxLookbackHours,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedStrategyIds = strategyIds
			.Select(StrategyIds.Normalize)
			.Distinct()
			.ToArray();
		if (normalizedStrategyIds.Length == 0 || maxLookbackHours <= 0)
		{
			return Array.Empty<StrategyLookbackPnl>();
		}

		var normalizedMaxLookbackHours = Math.Min(maxLookbackHours, 24);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH windows AS (
    SELECT generate_series(1, @MaxLookbackHours)::integer AS lookback_hours
),
requested_strategies AS (
    SELECT unnest(CAST(@StrategyIds AS uuid[])) AS strategy_id
),
filtered_runs AS (
    SELECT
        run.strategy_id,
        LEAST(
            @MaxLookbackHours,
            GREATEST(
                1,
                CEIL(EXTRACT(EPOCH FROM (CAST(@NowUtc AS timestamptz) - run.settled_at_utc)) / 3600.0)::integer
            )
        ) AS first_lookback_hours,
        run.realized_pnl_usd,
        run.stake_usd
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id = ANY(@StrategyIds)
      AND run.status = @Status
      AND run.realized_pnl_usd IS NOT NULL
      AND run.settled_at_utc IS NOT NULL
      AND run.settled_at_utc >= CAST(@NowUtc AS timestamptz) - make_interval(hours => @MaxLookbackHours)
      AND run.settled_at_utc <= CAST(@NowUtc AS timestamptz)
),
hour_buckets AS (
    SELECT
        strategy_id,
        first_lookback_hours,
        sum(realized_pnl_usd) AS realized_pnl_usd,
        sum(stake_usd) AS stake_usd,
        count(*)::integer AS settled_runs_count
    FROM filtered_runs
    GROUP BY strategy_id, first_lookback_hours
),
lookback_pnl AS (
    SELECT
        requested_strategies.strategy_id,
        windows.lookback_hours,
        sum(COALESCE(hour_buckets.realized_pnl_usd, 0)) OVER cumulative_window AS realized_pnl_usd,
        sum(COALESCE(hour_buckets.stake_usd, 0)) OVER cumulative_window AS stake_usd,
        (sum(COALESCE(hour_buckets.settled_runs_count, 0)) OVER cumulative_window)::integer AS settled_runs_count
    FROM requested_strategies
    CROSS JOIN windows
    LEFT JOIN hour_buckets
        ON hour_buckets.strategy_id = requested_strategies.strategy_id
       AND hour_buckets.first_lookback_hours = windows.lookback_hours
    WINDOW cumulative_window AS (
        PARTITION BY requested_strategies.strategy_id
        ORDER BY windows.lookback_hours
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    )
)
SELECT
    strategy_id,
    lookback_hours,
    realized_pnl_usd,
    stake_usd,
    CASE
        WHEN stake_usd > 0 THEN realized_pnl_usd * 100 / stake_usd
        ELSE 0
    END AS roi_pct,
    settled_runs_count
FROM lookback_pnl
WHERE realized_pnl_usd > 0
  AND stake_usd > 0
ORDER BY lookback_hours ASC, realized_pnl_usd DESC, strategy_id ASC;
""");
		command.Parameters.Add("StrategyIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = normalizedStrategyIds;
		command.Parameters.AddWithValue("Status", StrategyMarketPaperRunStatuses.Settled);
		command.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
		command.Parameters.AddWithValue("MaxLookbackHours", normalizedMaxLookbackHours);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<StrategyLookbackPnl> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new StrategyLookbackPnl(
				reader.GetGuid(0),
				reader.GetInt32(1),
				reader.GetDecimal(2),
				reader.GetDecimal(3),
				reader.GetDecimal(4),
				reader.GetInt32(5)));
		}

		return results;
	}

	public async Task<IReadOnlyList<StrategyChildParentAssignment>> GetActiveStrategyChildParentAssignmentsAsync(
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, child_strategy_id, parent_strategy_id, asset_symbol, lookback_hours, child_mode,
       parent_pnl_usd, parent_roi_pct, assigned_at_utc, ended_at_utc, updated_at_utc
FROM strategy_child_parent_assignments
WHERE ended_at_utc IS NULL
ORDER BY parent_strategy_id ASC, child_strategy_id ASC;
""");
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<StrategyChildParentAssignment> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadStrategyChildParentAssignment(reader));
		}

		return results;
	}

	public async Task<IReadOnlyDictionary<Guid, StrategyLossDiffState>> ReconcileStrategyLossDiffStatesAsync(
		Guid parentStrategyId,
		DateTimeOffset settledBeforeUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedParentStrategyId = StrategyIds.Normalize(parentStrategyId);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		var states = new List<StrategyLossDiffState>();
		await using (NpgsqlCommand lockCommand = CreateCommand(connection, """
SELECT child_strategy_id, parent_strategy_id, mode, threshold, current_value,
       started_at_utc, last_parent_entered_at_utc, last_parent_run_id,
       last_reconciled_at_utc, updated_at_utc
FROM strategy_loss_diff_states
WHERE parent_strategy_id = @ParentStrategyId
ORDER BY child_strategy_id ASC
FOR UPDATE;
"""))
		{
			lockCommand.Transaction = transaction;
			lockCommand.Parameters.AddWithValue("ParentStrategyId", normalizedParentStrategyId);
			await using NpgsqlDataReader reader = await lockCommand.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				states.Add(ReadStrategyLossDiffState(reader));
			}
		}

		if (states.Count == 0)
		{
			await transaction.CommitAsync(cancellationToken);
			return new Dictionary<Guid, StrategyLossDiffState>();
		}

		await using (NpgsqlCommand insertEventsCommand = CreateCommand(connection, """
WITH parent_runs AS MATERIALIZED (
    SELECT id, strategy_id, entered_at_utc, settled_at_utc, realized_pnl_usd
    FROM strategy_market_paper_runs
    WHERE strategy_id = @ParentStrategyId AND status = @SettledStatus
      AND entered_at_utc >= (SELECT min(started_at_utc) FROM strategy_loss_diff_states WHERE parent_strategy_id = @ParentStrategyId)
      AND settled_at_utc < @SettledBeforeUtc
      AND realized_pnl_usd IS NOT NULL AND realized_pnl_usd <> 0
)
INSERT INTO strategy_loss_diff_parent_events (
    child_strategy_id,
    parent_run_id,
    parent_entered_at_utc,
    parent_settled_at_utc,
    won,
    created_at_utc
)
SELECT
    state.child_strategy_id,
    run.id,
    run.entered_at_utc,
    run.settled_at_utc,
    run.realized_pnl_usd > 0,
    clock_timestamp()
FROM strategy_loss_diff_states state
INNER JOIN parent_runs run
    ON run.strategy_id = state.parent_strategy_id
WHERE state.parent_strategy_id = @ParentStrategyId
  AND run.entered_at_utc IS NOT NULL
  AND run.entered_at_utc >= state.started_at_utc
  AND run.settled_at_utc IS NOT NULL
  AND run.settled_at_utc < @SettledBeforeUtc
  AND run.realized_pnl_usd IS NOT NULL
  AND run.realized_pnl_usd <> 0
ON CONFLICT (child_strategy_id, parent_run_id) DO NOTHING;
"""))
		{
			insertEventsCommand.Transaction = transaction;
			insertEventsCommand.Parameters.AddWithValue("ParentStrategyId", normalizedParentStrategyId);
			insertEventsCommand.Parameters.AddWithValue("SettledStatus", StrategyMarketPaperRunStatuses.Settled);
			insertEventsCommand.Parameters.AddWithValue("SettledBeforeUtc", UtcDateTime(settledBeforeUtc));
			await insertEventsCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		var eventsByChild = states.ToDictionary(
			state => state.ChildStrategyId,
			_ => new List<(Guid ParentRunId, DateTimeOffset ParentEnteredAtUtc, bool Won)>());
		var progressIds = StrategyIds.UpDown5mStrategyVariants
			.Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.LossDiffPositiveProgressMirror)
			.Select(variant => variant.Id).ToArray();
		await using (NpgsqlCommand readEventsCommand = CreateCommand(connection, """
SELECT child_strategy_id, parent_run_id, parent_entered_at_utc, won
FROM strategy_loss_diff_parent_events
WHERE child_strategy_id = ANY(@ChildStrategyIds)
  AND (NOT (child_strategy_id = ANY(@ProgressIds)) OR parent_settled_at_utc < @SettledBeforeUtc)
ORDER BY child_strategy_id ASC,
         CASE WHEN child_strategy_id = ANY(@ProgressIds) THEN parent_settled_at_utc ELSE parent_entered_at_utc END ASC,
         parent_entered_at_utc ASC, parent_run_id ASC;
"""))
		{
			readEventsCommand.Transaction = transaction;
			readEventsCommand.Parameters.AddWithValue("ProgressIds", progressIds);
			readEventsCommand.Parameters.AddWithValue("SettledBeforeUtc", UtcDateTime(settledBeforeUtc));
			readEventsCommand.Parameters.Add("ChildStrategyIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
				states.Select(state => state.ChildStrategyId).ToArray();
			await using NpgsqlDataReader reader = await readEventsCommand.ExecuteReaderAsync(cancellationToken);
			while (await reader.ReadAsync(cancellationToken))
			{
				eventsByChild[reader.GetGuid(0)].Add((
					reader.GetGuid(1),
					DateTimeOffsetFromUtc(reader.GetDateTime(2)),
					reader.GetBoolean(3)));
			}
		}

		var reconciledStates = new Dictionary<Guid, StrategyLossDiffState>(states.Count);
		foreach (var state in states)
		{
			var currentValue = 0;
			foreach (var parentEvent in eventsByChild[state.ChildStrategyId])
			{
				currentValue = state.Mode switch
				{
					StrategyChildParentAssignmentModes.LossDiffReset => parentEvent.Won
						? 0
						: checked(currentValue + 1),
					StrategyChildParentAssignmentModes.LossDiffPositive => parentEvent.Won
						? Math.Max(0, currentValue - 1)
						: checked(currentValue + 1),
					_ => throw new InvalidOperationException(
						$"Unsupported LossDiff state mode '{state.Mode}' for child {state.ChildStrategyId:D}.")
				};
			}

			var lastEvent = eventsByChild[state.ChildStrategyId].LastOrDefault();
			var hasEvents = eventsByChild[state.ChildStrategyId].Count > 0;
			await using NpgsqlCommand updateCommand = CreateCommand(connection, """
UPDATE strategy_loss_diff_states
SET current_value = @CurrentValue,
    last_parent_entered_at_utc = @LastParentEnteredAtUtc,
    last_parent_run_id = @LastParentRunId,
    last_reconciled_at_utc = @LastReconciledAtUtc,
    updated_at_utc = clock_timestamp()
WHERE child_strategy_id = @ChildStrategyId
RETURNING child_strategy_id, parent_strategy_id, mode, threshold, current_value,
          started_at_utc, last_parent_entered_at_utc, last_parent_run_id,
          last_reconciled_at_utc, updated_at_utc;
""");
			updateCommand.Transaction = transaction;
			updateCommand.Parameters.AddWithValue("CurrentValue", currentValue);
			updateCommand.Parameters.AddWithValue(
				"LastParentEnteredAtUtc",
				hasEvents ? UtcDateTime(lastEvent.ParentEnteredAtUtc) : DBNull.Value);
			updateCommand.Parameters.AddWithValue(
				"LastParentRunId",
				hasEvents ? lastEvent.ParentRunId : DBNull.Value);
			updateCommand.Parameters.AddWithValue("LastReconciledAtUtc", UtcDateTime(settledBeforeUtc));
			updateCommand.Parameters.AddWithValue("ChildStrategyId", state.ChildStrategyId);
			await using NpgsqlDataReader reader = await updateCommand.ExecuteReaderAsync(cancellationToken);
			if (!await reader.ReadAsync(cancellationToken))
			{
				throw new InvalidOperationException(
					$"LossDiff state disappeared while reconciling child {state.ChildStrategyId:D}.");
			}

			var reconciledState = ReadStrategyLossDiffState(reader);
			reconciledStates.Add(reconciledState.ChildStrategyId, reconciledState);
		}

		await transaction.CommitAsync(cancellationToken);
		return reconciledStates;
	}

	public async Task UpsertStrategyChildParentSelectionsAsync(
		IReadOnlyList<StrategyChildParentSelection> selections,
		DateTimeOffset nowUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (selections.Count == 0)
		{
			return;
		}

		var rows = selections.Select(selection => new
		{
			id = Guid.NewGuid(),
			child_strategy_id = StrategyIds.Normalize(selection.ChildStrategyId),
			parent_strategy_id = selection.ParentStrategyId.HasValue
				? StrategyIds.Normalize(selection.ParentStrategyId.Value)
				: (Guid?)null,
			asset_symbol = selection.AssetSymbol.Trim().ToUpperInvariant(),
			lookback_hours = selection.LookbackHours,
			child_mode = selection.ChildMode,
			parent_pnl_usd = selection.ParentPnlUsd ?? 0m,
			parent_roi_pct = selection.ParentRoiPct ?? 0m
		});
		var selectionsJson = JsonSerializer.Serialize(rows);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		await using (NpgsqlCommand closeCommand = CreateCommand(connection, """
WITH selection_rows AS (
    SELECT *
    FROM jsonb_to_recordset(CAST(@SelectionsJson AS jsonb)) AS selection(
        id uuid,
        child_strategy_id uuid,
        parent_strategy_id uuid,
        asset_symbol text,
        lookback_hours integer,
        child_mode text,
        parent_pnl_usd numeric,
        parent_roi_pct numeric
    )
)
UPDATE strategy_child_parent_assignments active
SET ended_at_utc = @NowUtc,
    updated_at_utc = @NowUtc
FROM selection_rows
WHERE active.child_strategy_id = selection_rows.child_strategy_id
  AND active.ended_at_utc IS NULL
  AND (
      selection_rows.parent_strategy_id IS NULL
      OR active.parent_strategy_id <> selection_rows.parent_strategy_id
      OR active.asset_symbol <> selection_rows.asset_symbol
      OR active.lookback_hours <> selection_rows.lookback_hours
      OR active.child_mode <> selection_rows.child_mode
  );
"""))
		{
			closeCommand.Transaction = transaction;
			AddJsonbParameter(closeCommand, "SelectionsJson", selectionsJson);
			closeCommand.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
			await closeCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (NpgsqlCommand updateCommand = CreateCommand(connection, """
WITH selection_rows AS (
    SELECT *
    FROM jsonb_to_recordset(CAST(@SelectionsJson AS jsonb)) AS selection(
        id uuid,
        child_strategy_id uuid,
        parent_strategy_id uuid,
        asset_symbol text,
        lookback_hours integer,
        child_mode text,
        parent_pnl_usd numeric,
        parent_roi_pct numeric
    )
)
UPDATE strategy_child_parent_assignments active
SET parent_pnl_usd = selection_rows.parent_pnl_usd,
    parent_roi_pct = selection_rows.parent_roi_pct,
    updated_at_utc = @NowUtc
FROM selection_rows
WHERE active.child_strategy_id = selection_rows.child_strategy_id
  AND active.parent_strategy_id = selection_rows.parent_strategy_id
  AND active.asset_symbol = selection_rows.asset_symbol
  AND active.lookback_hours = selection_rows.lookback_hours
  AND active.child_mode = selection_rows.child_mode
  AND active.ended_at_utc IS NULL
  AND selection_rows.parent_strategy_id IS NOT NULL;
"""))
		{
			updateCommand.Transaction = transaction;
			AddJsonbParameter(updateCommand, "SelectionsJson", selectionsJson);
			updateCommand.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
			await updateCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (NpgsqlCommand insertCommand = CreateCommand(connection, """
WITH selection_rows AS (
    SELECT *
    FROM jsonb_to_recordset(CAST(@SelectionsJson AS jsonb)) AS selection(
        id uuid,
        child_strategy_id uuid,
        parent_strategy_id uuid,
        asset_symbol text,
        lookback_hours integer,
        child_mode text,
        parent_pnl_usd numeric,
        parent_roi_pct numeric
    )
)
INSERT INTO strategy_child_parent_assignments (
    id, child_strategy_id, parent_strategy_id, asset_symbol, lookback_hours, child_mode,
    parent_pnl_usd, parent_roi_pct, assigned_at_utc, ended_at_utc, updated_at_utc
)
SELECT
    selection_rows.id,
    selection_rows.child_strategy_id,
    selection_rows.parent_strategy_id,
    selection_rows.asset_symbol,
    selection_rows.lookback_hours,
    selection_rows.child_mode,
    selection_rows.parent_pnl_usd,
    selection_rows.parent_roi_pct,
    @NowUtc,
    NULL,
    @NowUtc
FROM selection_rows
WHERE selection_rows.parent_strategy_id IS NOT NULL
  AND NOT EXISTS (
      SELECT 1
      FROM strategy_child_parent_assignments active
      WHERE active.child_strategy_id = selection_rows.child_strategy_id
        AND active.ended_at_utc IS NULL
  );
"""))
		{
			insertCommand.Transaction = transaction;
			AddJsonbParameter(insertCommand, "SelectionsJson", selectionsJson);
			insertCommand.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
			await insertCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategyRuntimeSettingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id,
       enabled,
       live_stakes,
       paused AND (paused_until_utc IS NULL OR paused_until_utc > @NowUtc) AS paused,
       CASE
           WHEN paused AND (paused_until_utc IS NULL OR paused_until_utc > @NowUtc) THEN paused_until_utc
           ELSE NULL
       END AS paused_until_utc,
       paper_stake_amount,
       live_stake_amount,
       paper_lost_coeff,
       live_lost_coeff,
       paper_lost_counter,
       live_lost_counter,
       live_available_balance,
       live_enabled_at_utc
FROM strategies;
""");
		command.Parameters.AddWithValue("NowUtc", UtcDateTime(DateTimeOffset.UtcNow));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		Dictionary<Guid, StrategyRuntimeSettings> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			var strategyId = StrategyIds.Normalize(reader.GetGuid(0));
			results[strategyId] = new StrategyRuntimeSettings(
				strategyId,
				reader.GetBoolean(1),
				reader.GetBoolean(2),
				reader.GetBoolean(3),
				reader.IsDBNull(4) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(4)),
				reader.GetDecimal(5),
				reader.GetDecimal(6),
				reader.GetDecimal(7),
				reader.GetDecimal(8),
				reader.GetInt32(9),
				reader.GetInt32(10),
				reader.GetDecimal(11),
				reader.IsDBNull(12) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(12)));
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
    live_enabled_at_utc = CASE
        WHEN @LiveStakes AND NOT live_stakes THEN @UpdatedAtUtc
        WHEN @LiveStakes AND live_enabled_at_utc IS NULL THEN @UpdatedAtUtc
        WHEN NOT @LiveStakes THEN NULL
        ELSE live_enabled_at_utc
    END,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("LiveStakes", liveStakes);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> SetStrategyPausedAsync(
		Guid strategyId,
		bool paused,
		DateTimeOffset? pausedUntilUtc,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET paused = @Paused,
    paused_until_utc = CASE WHEN @Paused THEN @PausedUntilUtc ELSE NULL END,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("Paused", paused);
		command.Parameters.Add("PausedUntilUtc", NpgsqlDbType.TimestampTz).Value =
			paused && pausedUntilUtc.HasValue ? UtcDateTime(pausedUntilUtc.Value) : DBNull.Value;
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
		var rows = await command.ExecuteNonQueryAsync(cancellationToken);
		return rows > 0;
	}

	public async Task<bool> SetStrategyStakeAmountsAsync(
		Guid strategyId,
		decimal paperStakeAmount,
		decimal liveStakeAmount,
		decimal paperLostCoeff,
		decimal liveLostCoeff,
		int paperLostCounter,
		int liveLostCounter,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET paper_stake_amount = @PaperStakeAmount,
    live_stake_amount = @LiveStakeAmount,
    paper_lost_coeff = @PaperLostCoeff,
    live_lost_coeff = @LiveLostCoeff,
    paper_lost_counter = @PaperLostCounter,
    live_lost_counter = @LiveLostCounter,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
  AND @PaperStakeAmount > 0
  AND @LiveStakeAmount > 0
  AND @PaperLostCoeff >= 1
  AND @LiveLostCoeff >= 1;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("PaperStakeAmount", paperStakeAmount);
		command.Parameters.AddWithValue("LiveStakeAmount", liveStakeAmount);
		command.Parameters.AddWithValue("PaperLostCoeff", paperLostCoeff);
		command.Parameters.AddWithValue("LiveLostCoeff", liveLostCoeff);
		command.Parameters.AddWithValue("PaperLostCounter", paperLostCounter);
		command.Parameters.AddWithValue("LiveLostCounter", liveLostCounter);
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
SET live_available_balance = LEAST(100.00, @LiveAvailableBalance),
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

	public async Task<StrategyLostCounterUpdateResult> UpdateStrategyLostCounterAfterSettlementAsync(
		Guid strategyId,
		bool isLive,
		bool won,
		bool counterEnabled,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET paper_lost_counter = CASE
        WHEN @IsLive THEN paper_lost_counter
        WHEN NOT @CounterEnabled THEN 0
        WHEN @Won THEN paper_lost_counter - 1
        ELSE paper_lost_counter + 1
    END,
    live_lost_counter = CASE
        WHEN NOT @IsLive THEN live_lost_counter
        WHEN NOT @CounterEnabled THEN 0
        WHEN @Won THEN live_lost_counter - 1
        ELSE live_lost_counter + 1
    END,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
RETURNING paper_lost_counter, live_lost_counter;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		command.Parameters.AddWithValue("IsLive", isLive);
		command.Parameters.AddWithValue("Won", won);
		command.Parameters.AddWithValue("CounterEnabled", counterEnabled);
		command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		if (!await reader.ReadAsync(cancellationToken))
		{
			return new StrategyLostCounterUpdateResult(false, 0, 0);
		}

		return new StrategyLostCounterUpdateResult(
			true,
			reader.GetInt32(0),
			reader.GetInt32(1));
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
		await LockPaperWalletsAsync(
			connection,
			transaction,
			paperOrders
				.Select(order => order.CopiedTraderWallet)
				.Append(activityEvent.CopiedTraderWallet)
				.Distinct(StringComparer.Ordinal)
				.ToArray(),
			cancellationToken);
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
		if (positionUpdates.Count > 0)
		{
			var positionIds = positionUpdates
				.Select(update => update.PositionId)
				.Distinct()
				.ToArray();
			await using NpgsqlCommand validatePositionsCommand = CreateCommand(connection, """
SELECT count(*) = @PositionCount
   AND bool_and(lower(copied_trader_wallet) = lower(@CopiedTraderWallet))
FROM paper_copied_leader_positions
WHERE id = ANY(@PositionIds);
""");
			validatePositionsCommand.Transaction = transaction;
			validatePositionsCommand.Parameters.AddWithValue("PositionCount", positionIds.Length);
			validatePositionsCommand.Parameters.AddWithValue(
				"CopiedTraderWallet",
				activityEvent.CopiedTraderWallet);
			validatePositionsCommand.Parameters.Add("PositionIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value =
				positionIds;
			if (await validatePositionsCommand.ExecuteScalarAsync(cancellationToken) is not true)
			{
				throw new ArgumentException(
					"Every copied-leader position update must target the activity event wallet.",
					nameof(positionUpdates));
			}
		}

		foreach (PaperCopiedLeaderPositionExitUpdate update in positionUpdates.OrderBy(update => update.PositionId))
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

		foreach (PaperOrder order in paperOrders
			.OrderBy(order => order.CopiedTraderWallet, StringComparer.Ordinal)
			.ThenBy(order => order.AssetId, StringComparer.Ordinal)
			.ThenBy(order => order.Id))
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
		if (order.HistoricalGrossNetParityOwnership != HistoricalGrossNetParityOwnership.None ||
			order.RowVersion != 0)
		{
			throw new ArgumentException(
				"A new Live order must start with parity ownership None and row version zero.",
				nameof(order));
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO live_orders (
    id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id, outcome, price, size_shares,
    notional_usd, order_type, created_at_utc, expires_at_utc, submitted_at_utc, response_status,
    filled_size, remaining_size, average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd,
    cancel_status, raw_response_json, validation_summary,
    balance_effect_applied, settlement_value_usd, realized_pnl_usd, settled_at_utc, winning_asset_id, winning_outcome,
    won, settlement_source, correlation_id, execution_source, post_only, paper_order_id,
    fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate, fee_exponent,
    fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd,
    historical_gross_net_parity_ownership, row_version,
    updated_at_utc
) VALUES (
    @Id, @SignalId, @StrategyId, @Status, @OrderId, @Side, @AssetId, @ConditionId, @Outcome, @Price, @SizeShares,
    @NotionalUsd, @OrderType, @CreatedAtUtc, @ExpiresAtUtc, @SubmittedAtUtc, @ResponseStatus,
    @FilledSize, @RemainingSize, @AverageFillPrice, @FilledNotionalUsd, @CostBasisUsd, @FeeUsd,
    @CancelStatus, CAST(@RawResponseJson AS jsonb), @ValidationSummary,
    @BalanceEffectApplied, @SettlementValueUsd, @RealizedPnlUsd, @SettledAtUtc, @WinningAssetId, @WinningOutcome,
    @Won, @SettlementSource, @CorrelationId, @ExecutionSource, @PostOnly, @PaperOrderId,
    @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate, @FeeExponent,
    @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd,
    @HistoricalGrossNetParityOwnership, @RowVersion,
    @UpdatedAtUtc
);
""");
		AddLiveOrderParameters(command, order);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = await UpdateLiveOrderWithConcurrencyAsync(order, cancellationToken);
	}

	public async Task<LiveOrder> UpdateLiveOrderWithConcurrencyAsync(
		LiveOrder order,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		if (order.HistoricalGrossNetParityOwnership != HistoricalGrossNetParityOwnership.None ||
			order.RowVersion < 0)
		{
			throw new DBConcurrencyException(
				"Ordinary Live-order updates require parity ownership None and a valid expected row version.");
		}

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
    fee_accounting_status = @FeeAccountingStatus,
    fee_liquidity_role = @FeeLiquidityRole,
    fee_calculation_source = @FeeCalculationSource,
    fee_rate = @FeeRate,
    fee_exponent = @FeeExponent,
    fee_taker_only = @FeeTakerOnly,
    fee_calculated_at_utc = @FeeCalculatedAtUtc,
    cancel_status = @CancelStatus,
    raw_response_json = CAST(@RawResponseJson AS jsonb),
    validation_summary = @ValidationSummary,
    balance_effect_applied = @BalanceEffectApplied,
    settlement_value_usd = @SettlementValueUsd,
    realized_pnl_usd = @RealizedPnlUsd,
    net_realized_pnl_usd = @NetRealizedPnlUsd,
    settled_at_utc = @SettledAtUtc,
    winning_asset_id = @WinningAssetId,
    winning_outcome = @WinningOutcome,
    won = @Won,
    settlement_source = @SettlementSource,
    correlation_id = @CorrelationId,
    execution_source = @ExecutionSource,
    post_only = @PostOnly,
    paper_order_id = @PaperOrderId,
    updated_at_utc = @UpdatedAtUtc,
    row_version = row_version + 1
WHERE id = @Id
  AND historical_gross_net_parity_ownership = 'None'
  AND row_version = @RowVersion
RETURNING row_version;
""");
		AddLiveOrderParameters(command, order);
		var resultingRowVersion = await command.ExecuteScalarAsync(cancellationToken);
		if (resultingRowVersion is null or DBNull)
		{
			throw new DBConcurrencyException(
				$"Live order {order.Id:D} changed or is owned by Historical Gross/Net parity.");
		}

		return order with { RowVersion = (long)resultingRowVersion };
	}

	public async Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')\n  AND historical_gross_net_parity_ownership = 'None'\nORDER BY created_at_utc DESC;"))
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
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status IN ('Submitted', 'Live', 'Delayed', 'Unmatched', 'CancelRequested')\n  AND historical_gross_net_parity_ownership = 'None'\n  AND (strategy_id = @StrategyId OR (@CorrelationId IS NOT NULL AND correlation_id = @CorrelationId))\nORDER BY created_at_utc DESC;");
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
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders\nWHERE status = 'Matched'\n  AND balance_effect_applied = false\n  AND filled_size > 0\n  AND historical_gross_net_parity_ownership = 'None'\nORDER BY updated_at_utc ASC, created_at_utc ASC\nLIMIT @Limit;"))
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
		decimal grossRealizedPnlUsd,
		decimal? netRealizedPnlUsd,
		string? winningAssetId,
		string winningOutcome,
		DateTimeOffset settledAtUtc,
		DateTimeOffset updatedAtUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		return await ApplyLiveOrderSettlementToStrategyBalanceCoreAsync(
			liveOrderId,
			strategyId,
			settlementValueUsd,
			grossRealizedPnlUsd,
			netRealizedPnlUsd,
			winningAssetId,
			winningOutcome,
			settledAtUtc,
			updatedAtUtc,
			null,
			cancellationToken);
	}

	public async Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(
		Guid liveOrderId,
		Guid strategyId,
		decimal settlementValueUsd,
		decimal grossRealizedPnlUsd,
		decimal? netRealizedPnlUsd,
		string? winningAssetId,
		string winningOutcome,
		DateTimeOffset settledAtUtc,
		DateTimeOffset updatedAtUtc,
		long expectedRowVersion,
		CancellationToken cancellationToken = default)
	{
		return await ApplyLiveOrderSettlementToStrategyBalanceCoreAsync(
			liveOrderId,
			strategyId,
			settlementValueUsd,
			grossRealizedPnlUsd,
			netRealizedPnlUsd,
			winningAssetId,
			winningOutcome,
			settledAtUtc,
			updatedAtUtc,
			expectedRowVersion,
			cancellationToken);
	}

	private async Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceCoreAsync(
		Guid liveOrderId,
		Guid strategyId,
		decimal settlementValueUsd,
		decimal grossRealizedPnlUsd,
		decimal? netRealizedPnlUsd,
		string? winningAssetId,
		string winningOutcome,
		DateTimeOffset settledAtUtc,
		DateTimeOffset updatedAtUtc,
		long? expectedRowVersion,
		CancellationToken cancellationToken)
	{
		var normalizedStrategyId = StrategyIds.Normalize(strategyId);
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		if (expectedRowVersion is null)
		{
			await using NpgsqlCommand versionCommand = CreateCommand(connection, """
SELECT row_version
FROM live_orders
WHERE id = @LiveOrderId
  AND strategy_id = @StrategyId
  AND balance_effect_applied = false
  AND historical_gross_net_parity_ownership = 'None'
FOR UPDATE;
""");
			versionCommand.Transaction = transaction;
			versionCommand.Parameters.AddWithValue("LiveOrderId", liveOrderId);
			versionCommand.Parameters.AddWithValue("StrategyId", normalizedStrategyId);
			var currentVersion = await versionCommand.ExecuteScalarAsync(cancellationToken);
			if (currentVersion is null or DBNull)
			{
				await transaction.RollbackAsync(cancellationToken);
				return new StrategyLiveBalanceAdjustmentResult(false, 0m, false);
			}
			expectedRowVersion = Convert.ToInt64(currentVersion);
		}

		await using (NpgsqlCommand command = CreateCommand(connection, """
UPDATE live_orders
SET balance_effect_applied = @NetRealizedPnlUsd IS NOT NULL,
    settlement_value_usd = @SettlementValueUsd,
    realized_pnl_usd = @GrossRealizedPnlUsd,
    net_realized_pnl_usd = @NetRealizedPnlUsd,
    settled_at_utc = @SettledAtUtc,
    winning_asset_id = @WinningAssetId,
    winning_outcome = @WinningOutcome,
    won = @Won,
    settlement_source = 'gamma_resolved_metadata',
    updated_at_utc = @UpdatedAtUtc,
    row_version = row_version + 1
WHERE id = @LiveOrderId
  AND strategy_id = @StrategyId
  AND balance_effect_applied = false
  AND historical_gross_net_parity_ownership = 'None'
  AND row_version = @ExpectedRowVersion
RETURNING balance_effect_applied;
"""))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("LiveOrderId", liveOrderId);
			command.Parameters.AddWithValue("StrategyId", normalizedStrategyId);
			command.Parameters.AddWithValue("SettlementValueUsd", settlementValueUsd);
			command.Parameters.AddWithValue("GrossRealizedPnlUsd", grossRealizedPnlUsd);
			command.Parameters.AddWithValue("NetRealizedPnlUsd", NullableDecimal(netRealizedPnlUsd));
			command.Parameters.AddWithValue("SettledAtUtc", UtcDateTime(settledAtUtc));
			command.Parameters.AddWithValue("WinningAssetId", ((object)winningAssetId) ?? ((object)DBNull.Value));
			command.Parameters.AddWithValue("WinningOutcome", winningOutcome);
			command.Parameters.AddWithValue("Won", settlementValueUsd > 0m);
			command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
			command.Parameters.AddWithValue("ExpectedRowVersion", expectedRowVersion.Value);
			var balanceEffectApplied = await command.ExecuteScalarAsync(cancellationToken);
			if (balanceEffectApplied is null or DBNull)
			{
				await transaction.RollbackAsync(cancellationToken);
				return new StrategyLiveBalanceAdjustmentResult(false, 0m, false);
			}

			if (!(bool)balanceEffectApplied)
			{
				await transaction.CommitAsync(cancellationToken);
				return new StrategyLiveBalanceAdjustmentResult(false, 0m, false);
			}
		}

		await using (NpgsqlCommand command = CreateCommand(connection, """
UPDATE strategies
SET live_available_balance = LEAST(100.00, GREATEST(0, live_available_balance + @BalanceRealizedPnlUsd)),
    live_stakes = CASE
        WHEN LEAST(100.00, GREATEST(0, live_available_balance + @BalanceRealizedPnlUsd)) < live_stake_amount THEN false
        ELSE live_stakes
    END,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @StrategyId
RETURNING live_available_balance, live_stakes, live_stake_amount;
"""))
		{
			command.Transaction = transaction;
			command.Parameters.AddWithValue("StrategyId", normalizedStrategyId);
			command.Parameters.AddWithValue("BalanceRealizedPnlUsd", netRealizedPnlUsd!.Value);
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

	public async Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken), Guid? strategyId = null, int offset = 0, DateTimeOffset? createdAfterUtc = null)
	{
		if (limit <= 0)
		{
			return Array.Empty<LiveOrder>();
		}

		Guid? normalizedStrategyId = strategyId.HasValue ? StrategyIds.Normalize(strategyId.GetValueOrDefault()) : null;
		int normalizedOffset = Math.Max(0, offset);
		List<string> filters = new List<string>
		{
			"historical_gross_net_parity_ownership = 'None'"
		};
		if (normalizedStrategyId.HasValue)
		{
			filters.Add("strategy_id = @StrategyId");
		}
		if (createdAfterUtc.HasValue)
		{
			filters.Add("created_at_utc >= @CreatedAfterUtc");
		}
		string filterSql = filters.Count > 0 ? "\nWHERE " + string.Join("\n  AND ", filters) : string.Empty;
		IReadOnlyList<LiveOrder> result;
		await using (NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken))
		{
			IReadOnlyList<LiveOrder> readOnlyList2;
			await using (NpgsqlCommand command = CreateCommand(connection, "SELECT " + LiveOrderSelectColumns + "\nFROM live_orders" + filterSql + "\nORDER BY created_at_utc DESC\nLIMIT @Limit OFFSET @Offset;"))
			{
				if (normalizedStrategyId.HasValue)
				{
					command.Parameters.AddWithValue("StrategyId", normalizedStrategyId.GetValueOrDefault());
				}
				if (createdAfterUtc.HasValue)
				{
					command.Parameters.Add("CreatedAfterUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(createdAfterUtc.GetValueOrDefault());
				}
				command.Parameters.AddWithValue("Limit", limit);
				command.Parameters.AddWithValue("Offset", normalizedOffset);
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

	public async Task UpsertCryptoReferencePriceTickAsync(CryptoReferencePriceTick tick, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_reference_price_ticks (
    id, asset_symbol, binance_symbol, sampled_at_utc, bucket_start_utc,
    price_usd, source_updated_at_utc, fetched_at_utc, source, created_at_utc
) VALUES (
    @Id, @AssetSymbol, @BinanceSymbol, @SampledAtUtc, @BucketStartUtc,
    @PriceUsd, @SourceUpdatedAtUtc, @FetchedAtUtc, @Source, @CreatedAtUtc
)
ON CONFLICT (asset_symbol, bucket_start_utc) DO UPDATE SET
    id = excluded.id,
    binance_symbol = excluded.binance_symbol,
    sampled_at_utc = excluded.sampled_at_utc,
    price_usd = excluded.price_usd,
    source_updated_at_utc = excluded.source_updated_at_utc,
    fetched_at_utc = excluded.fetched_at_utc,
    source = excluded.source,
    created_at_utc = excluded.created_at_utc;
""");
		AddCryptoReferencePriceTickParameters(command, tick);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<CryptoReferencePriceTick>> GetCryptoReferencePriceTicksAsync(
		IReadOnlyCollection<string> assetSymbols,
		DateTimeOffset startUtc,
		DateTimeOffset endUtc,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedAssetSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToUpperInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedAssetSymbols.Length == 0)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, asset_symbol, binance_symbol, sampled_at_utc, bucket_start_utc,
       price_usd, source_updated_at_utc, fetched_at_utc, source, created_at_utc
FROM crypto_reference_price_ticks
WHERE asset_symbol = ANY(@AssetSymbols)
  AND sampled_at_utc >= @StartUtc
  AND sampled_at_utc <= @EndUtc
ORDER BY sampled_at_utc ASC, asset_symbol ASC;
""");
		command.Parameters.Add("AssetSymbols", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = normalizedAssetSymbols;
		command.Parameters.Add("StartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(startUtc);
		command.Parameters.Add("EndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endUtc);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoReferencePriceTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoReferencePriceTick(reader));
		}

		return results;
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

	public async Task AddBtcUpDown5mStrategyStageTimingAsync(
		BtcUpDown5mStrategyStageTiming timing,
		CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_up_down_5m_strategy_stage_timings (
    id, cycle_id, cycle_kind, flow_name, stage_name, detail,
    started_at_utc, completed_at_utc, duration_ms,
    variant_count, run_count, entries_placed, runs_skipped, runs_settled, markets_observed,
    earliest_entry_due_at_utc, latest_entry_due_at_utc,
    succeeded, error_message, created_at_utc
) VALUES (
    @Id, @CycleId, @CycleKind, @FlowName, @StageName, @Detail,
    @StartedAtUtc, @CompletedAtUtc, @DurationMs,
    @VariantCount, @RunCount, @EntriesPlaced, @RunsSkipped, @RunsSettled, @MarketsObserved,
    @EarliestEntryDueAtUtc, @LatestEntryDueAtUtc,
    @Succeeded, @ErrorMessage, @CreatedAtUtc
);
""");
		AddBtcUpDown5mStrategyStageTimingParameters(command, timing);
		await command.ExecuteNonQueryAsync(cancellationToken);
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

	public async Task<IReadOnlyList<BtcUsdReferencePricePoint>> GetRecentBtcUsdReferencePricePointsAsync(int limit = 100, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH source_ticks AS (
    SELECT
        date_trunc('minute', sampled_at_utc AT TIME ZONE 'UTC') AS sample_minute,
        sampled_at_utc,
        binance_price_usd,
        binance_source_updated_at_utc,
        binance_fetched_at_utc,
        created_at_utc
    FROM btc_up_down_5m_odds_ticks
    WHERE binance_price_usd > 0
),
recent_minute_samples AS (
    SELECT DISTINCT ON (sample_minute)
        sample_minute,
        sampled_at_utc,
        binance_price_usd,
        binance_source_updated_at_utc,
        binance_fetched_at_utc,
        created_at_utc
    FROM source_ticks
    ORDER BY sample_minute DESC, sampled_at_utc DESC, created_at_utc DESC
    LIMIT @Limit
)
SELECT binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc
FROM recent_minute_samples
ORDER BY sample_minute ASC, sampled_at_utc ASC, created_at_utc ASC;
""");
		command.Parameters.Add("Limit", NpgsqlDbType.Integer).Value = Math.Max(1, limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<BtcUsdReferencePricePoint> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new BtcUsdReferencePricePoint(
				reader.GetDecimal(0),
				DateTimeOffsetFromUtc(reader.GetDateTime(1)),
				DateTimeOffsetFromUtc(reader.GetDateTime(2)),
				"BinanceTradeWebSocketOddsArchive"));
		}

		return results;
	}

	public async Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetBtcUpDown5mOddsTicksForMarketStartAsync(DateTimeOffset marketStartUtc, int limit = 500, CancellationToken cancellationToken = default(CancellationToken))
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
WHERE market_start_utc = @MarketStartUtc
ORDER BY sampled_at_utc ASC, created_at_utc ASC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("MarketStartUtc", UtcDateTime(marketStartUtc));
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<BtcUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadBtcUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetBtcUpDown5mOddsTicksForMarketAsync(string marketId, int limit = 500, CancellationToken cancellationToken = default(CancellationToken))
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
ORDER BY sampled_at_utc ASC, created_at_utc ASC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("MarketId", marketId);
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<BtcUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadBtcUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task<IReadOnlyList<Btc5mHistoryRow>> GetBtc5mHistoryRowsAsync(IReadOnlyCollection<Btc5mHistoryKey> keys, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (keys.Count == 0)
		{
			return [];
		}

		var distinctKeys = keys.Distinct().ToArray();
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
WITH requested(seconds, cents) AS (
    SELECT * FROM unnest(@Seconds::integer[], @Cents::integer[])
)
SELECT history.seconds, history.cents, history.count, history.up_count, history.down_count
FROM requested
JOIN btc_5m_history history
  ON history.seconds = requested.seconds
 AND history.cents = requested.cents;
""");
		command.Parameters.Add("Seconds", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = distinctKeys.Select(key => key.Seconds).ToArray();
		command.Parameters.Add("Cents", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = distinctKeys.Select(key => key.Cents).ToArray();
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<Btc5mHistoryRow> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(new Btc5mHistoryRow(
				reader.GetInt32(0),
				reader.GetInt32(1),
				reader.GetInt32(2),
				reader.GetInt32(3),
				reader.GetInt32(4)));
		}

		return results;
	}

	public async Task AddBtcUpDown5mStatisticsTickAsync(BtcUpDown5mStatisticsTick tick, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_up_down_5m_statistics_ticks (
    id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    sampled_at_utc, seconds_after_start, seconds_to_close,
    binance_price_usd, binance_source_updated_at_utc, binance_fetched_at_utc,
    binance_start_price_usd, btc_move_from_start_usd, btc_move_from_start_cents,
    seconds_lower, seconds_upper, cents_lower, cents_upper,
    effective_count, up_probability, down_probability, support_threshold,
    history_rows_found, missing_history_corners, interpolation_method,
    up_asset_id, up_market_price, up_market_price_kind,
    down_asset_id, down_market_price, down_market_price_kind,
    up_edge, down_edge, decision_code, recommended_outcome, would_bet,
    diagnostics_json, created_at_utc
) VALUES (
    @Id, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @SampledAtUtc, @SecondsAfterStart, @SecondsToClose,
    @BinancePriceUsd, @BinanceSourceUpdatedAtUtc, @BinanceFetchedAtUtc,
    @BinanceStartPriceUsd, @BtcMoveFromStartUsd, @BtcMoveFromStartCents,
    @SecondsLower, @SecondsUpper, @CentsLower, @CentsUpper,
    @EffectiveCount, @UpProbability, @DownProbability, @SupportThreshold,
    @HistoryRowsFound, @MissingHistoryCorners, @InterpolationMethod,
    @UpAssetId, @UpMarketPrice, @UpMarketPriceKind,
    @DownAssetId, @DownMarketPrice, @DownMarketPriceKind,
    @UpEdge, @DownEdge, @DecisionCode, @RecommendedOutcome, @WouldBet,
    CAST(@DiagnosticsJson AS jsonb), @CreatedAtUtc
);
""");
		AddBtcUpDown5mStatisticsTickParameters(command, tick);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddBtcUpDown5mArbitrageScanAsync(BtcUpDown5mArbitrageScan scan, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_up_down_5m_arbitrage_scans (
    id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    sampled_at_utc, seconds_after_start, seconds_to_close,
    up_asset_id, up_best_bid, up_best_ask, up_ask_depth_shares, up_book_source, up_book_age_ms,
    down_asset_id, down_best_bid, down_best_ask, down_ask_depth_shares, down_book_source, down_book_age_ms,
    required_min_shares, max_common_executable_shares, best_executable_shares,
    up_cost_usd, down_cost_usd, total_cost_usd, guaranteed_payout_usd,
    gross_profit_usd, safety_buffer_usd, net_profit_usd, average_cost_per_share, edge_per_share,
    safety_buffer_per_share, min_net_profit_usd, decision_code, would_arbitrage,
    diagnostics_json, created_at_utc
) VALUES (
    @Id, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @SampledAtUtc, @SecondsAfterStart, @SecondsToClose,
    @UpAssetId, @UpBestBid, @UpBestAsk, @UpAskDepthShares, @UpBookSource, @UpBookAgeMs,
    @DownAssetId, @DownBestBid, @DownBestAsk, @DownAskDepthShares, @DownBookSource, @DownBookAgeMs,
    @RequiredMinShares, @MaxCommonExecutableShares, @BestExecutableShares,
    @UpCostUsd, @DownCostUsd, @TotalCostUsd, @GuaranteedPayoutUsd,
    @GrossProfitUsd, @SafetyBufferUsd, @NetProfitUsd, @AverageCostPerShare, @EdgePerShare,
    @SafetyBufferPerShare, @MinNetProfitUsd, @DecisionCode, @WouldArbitrage,
    CAST(@DiagnosticsJson AS jsonb), @CreatedAtUtc
);
""");
		AddBtcUpDown5mArbitrageScanParameters(command, scan);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpsertBtcUpDown5mResultStreakDiagnosticAsync(BtcUpDown5mResultStreakDiagnostic diagnostic, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_up_down_5m_result_streak_diagnostics (
    id, market_id, condition_id, market_slug, market_start_utc, market_end_utc, sampled_at_utc,
    latest_previous_market_id, latest_previous_market_slug, latest_previous_market_start_utc, latest_previous_market_end_utc,
    streak_winning_outcome, base_selected_direction, selected_outcome,
    close_book_streak_result_count, cumulative_move_market_count,
    latest_move_bps, latest_abs_move_bps, cumulative_move_bps, cumulative_abs_move_bps,
    rejection_reason, streak_truncated_reason, diagnostics_json, created_at_utc, updated_at_utc
) VALUES (
    @Id, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc, @SampledAtUtc,
    @LatestPreviousMarketId, @LatestPreviousMarketSlug, @LatestPreviousMarketStartUtc, @LatestPreviousMarketEndUtc,
    @StreakWinningOutcome, @BaseSelectedDirection, @SelectedOutcome,
    @CloseBookStreakResultCount, @CumulativeMoveMarketCount,
    @LatestMoveBps, @LatestAbsMoveBps, @CumulativeMoveBps, @CumulativeAbsMoveBps,
    @RejectionReason, @StreakTruncatedReason, CAST(@DiagnosticsJson AS jsonb), @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (market_id) DO UPDATE SET
    condition_id = excluded.condition_id,
    market_slug = excluded.market_slug,
    market_start_utc = excluded.market_start_utc,
    market_end_utc = excluded.market_end_utc,
    sampled_at_utc = excluded.sampled_at_utc,
    latest_previous_market_id = excluded.latest_previous_market_id,
    latest_previous_market_slug = excluded.latest_previous_market_slug,
    latest_previous_market_start_utc = excluded.latest_previous_market_start_utc,
    latest_previous_market_end_utc = excluded.latest_previous_market_end_utc,
    streak_winning_outcome = excluded.streak_winning_outcome,
    base_selected_direction = excluded.base_selected_direction,
    selected_outcome = excluded.selected_outcome,
    close_book_streak_result_count = excluded.close_book_streak_result_count,
    cumulative_move_market_count = excluded.cumulative_move_market_count,
    latest_move_bps = excluded.latest_move_bps,
    latest_abs_move_bps = excluded.latest_abs_move_bps,
    cumulative_move_bps = excluded.cumulative_move_bps,
    cumulative_abs_move_bps = excluded.cumulative_abs_move_bps,
    rejection_reason = excluded.rejection_reason,
    streak_truncated_reason = excluded.streak_truncated_reason,
    diagnostics_json = excluded.diagnostics_json,
    updated_at_utc = excluded.updated_at_utc;
""");
		AddBtcUpDown5mResultStreakDiagnosticParameters(command, diagnostic);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<bool> TryAddBtc5mHistoryLiveObservationAsync(Btc5mHistoryLiveObservation observation, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO btc_5m_history_live_observations (
    id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    sampled_at_utc, seconds, cents, binance_price_usd, binance_start_price_usd,
    btc_move_from_start_usd, result, applied_to_history, applied_at_utc,
    result_check_attempts, next_result_check_utc, last_result_error,
    created_at_utc, updated_at_utc
) VALUES (
    @Id, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @SampledAtUtc, @Seconds, @Cents, @BinancePriceUsd, @BinanceStartPriceUsd,
    @BtcMoveFromStartUsd, @Result, @AppliedToHistory, @AppliedAtUtc,
    @ResultCheckAttempts, @NextResultCheckUtc, @LastResultError,
    @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (market_id, seconds) DO NOTHING;
""");
		AddBtc5mHistoryLiveObservationParameters(command, observation);
		return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
	}

	public async Task<IReadOnlyList<Btc5mHistoryLiveObservation>> GetDueBtc5mHistoryLiveObservationsAsync(DateTimeOffset dueBeforeUtc, int limit, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       sampled_at_utc, seconds, cents, binance_price_usd, binance_start_price_usd,
       btc_move_from_start_usd, result, applied_to_history, applied_at_utc,
       result_check_attempts, next_result_check_utc, last_result_error,
       created_at_utc, updated_at_utc
FROM btc_5m_history_live_observations
WHERE NOT applied_to_history
  AND market_end_utc <= @DueBeforeUtc
  AND next_result_check_utc <= @DueBeforeUtc
ORDER BY market_end_utc ASC, sampled_at_utc ASC
LIMIT @Limit;
""");
		command.Parameters.Add("DueBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(dueBeforeUtc);
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<Btc5mHistoryLiveObservation> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadBtc5mHistoryLiveObservation(reader));
		}

		return results;
	}

	public async Task ApplyBtc5mHistoryLiveObservationResultAsync(Guid observationId, string result, DateTimeOffset appliedAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedResult = string.Equals(result, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down";
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
		Btc5mHistoryLiveObservation? observation;
		await using (NpgsqlCommand loadCommand = CreateCommand(connection, """
SELECT id, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       sampled_at_utc, seconds, cents, binance_price_usd, binance_start_price_usd,
       btc_move_from_start_usd, result, applied_to_history, applied_at_utc,
       result_check_attempts, next_result_check_utc, last_result_error,
       created_at_utc, updated_at_utc
FROM btc_5m_history_live_observations
WHERE id = @Id
FOR UPDATE;
"""))
		{
			loadCommand.Transaction = transaction;
			loadCommand.Parameters.AddWithValue("Id", observationId);
			await using NpgsqlDataReader reader = await loadCommand.ExecuteReaderAsync(cancellationToken);
			observation = await reader.ReadAsync(cancellationToken) ? ReadBtc5mHistoryLiveObservation(reader) : null;
		}

		if (observation is null || observation.AppliedToHistory)
		{
			await transaction.CommitAsync(cancellationToken);
			return;
		}

		await using (NpgsqlCommand historyCommand = CreateCommand(connection, """
INSERT INTO btc_5m_history (seconds, cents, count, up_count, down_count)
VALUES (@Seconds, @Cents, 1, @UpDelta, @DownDelta)
ON CONFLICT (seconds, cents) DO UPDATE SET
    count = btc_5m_history.count + 1,
    up_count = btc_5m_history.up_count + excluded.up_count,
    down_count = btc_5m_history.down_count + excluded.down_count;
"""))
		{
			historyCommand.Transaction = transaction;
			historyCommand.Parameters.AddWithValue("Seconds", observation.Seconds);
			historyCommand.Parameters.AddWithValue("Cents", observation.Cents);
			historyCommand.Parameters.AddWithValue("UpDelta", normalizedResult == "Up" ? 1 : 0);
			historyCommand.Parameters.AddWithValue("DownDelta", normalizedResult == "Down" ? 1 : 0);
			await historyCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await using (NpgsqlCommand markCommand = CreateCommand(connection, """
UPDATE btc_5m_history_live_observations
SET result = @Result,
    applied_to_history = true,
    applied_at_utc = @AppliedAtUtc,
    last_result_error = NULL,
    updated_at_utc = @AppliedAtUtc
WHERE id = @Id;
"""))
		{
			markCommand.Transaction = transaction;
			markCommand.Parameters.AddWithValue("Id", observationId);
			markCommand.Parameters.AddWithValue("Result", normalizedResult);
			markCommand.Parameters.Add("AppliedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(appliedAtUtc);
			await markCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		await transaction.CommitAsync(cancellationToken);
	}

	public async Task MarkBtc5mHistoryLiveObservationResultPendingAsync(Guid observationId, DateTimeOffset nextResultCheckUtc, string? errorMessage, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
UPDATE btc_5m_history_live_observations
SET result_check_attempts = result_check_attempts + 1,
    next_result_check_utc = @NextResultCheckUtc,
    last_result_error = @LastResultError,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @Id
  AND NOT applied_to_history;
""");
		command.Parameters.AddWithValue("Id", observationId);
		command.Parameters.Add("NextResultCheckUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(nextResultCheckUtc);
		command.Parameters.AddWithValue("LastResultError", string.IsNullOrWhiteSpace(errorMessage) ? DBNull.Value : errorMessage.Length > 2_000 ? errorMessage[..2_000] : errorMessage);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(updatedAtUtc);
		await command.ExecuteNonQueryAsync(cancellationToken);
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

	public async Task<IReadOnlyList<CryptoUpDown5mOddsTick>> GetCryptoUpDown5mOddsTicksForMarketStartAsync(string assetSymbol, DateTimeOffset marketStartUtc, int limit = 500, CancellationToken cancellationToken = default(CancellationToken))
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
WHERE lower(asset_symbol) = lower(@AssetSymbol)
  AND market_start_utc >= @MarketStartMinUtc
  AND market_start_utc <= @MarketStartMaxUtc
ORDER BY sampled_at_utc ASC, created_at_utc ASC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("AssetSymbol", assetSymbol);
		command.Parameters.AddWithValue("MarketStartMinUtc", UtcDateTime(marketStartUtc.AddSeconds(-2)));
		command.Parameters.AddWithValue("MarketStartMaxUtc", UtcDateTime(marketStartUtc.AddSeconds(2)));
		command.Parameters.AddWithValue("Limit", limit);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task<IReadOnlyList<CryptoUpDown5mOddsTick>> GetCryptoUpDown5mOddsTicksForMarketAsync(string assetSymbol, string marketId, int limit = 500, CancellationToken cancellationToken = default(CancellationToken))
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
WHERE lower(asset_symbol) = lower(@AssetSymbol)
  AND market_id = @MarketId
ORDER BY sampled_at_utc ASC, created_at_utc ASC
LIMIT @Limit;
""");
		command.Parameters.AddWithValue("AssetSymbol", assetSymbol);
		command.Parameters.AddWithValue("MarketId", marketId);
		command.Parameters.AddWithValue("Limit", Math.Max(1, limit));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mOddsTick> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mOddsTick(reader));
		}

		return results;
	}

	public async Task UpsertCryptoUpDown5mDiffSnapshotAsync(CryptoUpDown5mDiffSnapshot snapshot, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_up_down_5m_diff_snapshots (
    id, asset_symbol, market_start_utc, sampled_at_utc,
    counter_start_market_start_utc, last_included_market_start_utc, high_water_market_start_utc,
    counter_initialized, up_count, down_count, diff_count, diff, processed_market_count,
    history_fetch_failed_at_utc, history_fetch_retry_after_utc, history_fetch_error,
    created_at_utc, updated_at_utc
) VALUES (
    @Id, @AssetSymbol, @MarketStartUtc, @SampledAtUtc,
    @CounterStartMarketStartUtc, @LastIncludedMarketStartUtc, @HighWaterMarketStartUtc,
    @CounterInitialized, @UpCount, @DownCount, @DiffCount, @Diff, @ProcessedMarketCount,
    @HistoryFetchFailedAtUtc, @HistoryFetchRetryAfterUtc, @HistoryFetchError,
    @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (asset_symbol, market_start_utc) DO UPDATE SET
    sampled_at_utc = excluded.sampled_at_utc,
    counter_start_market_start_utc = excluded.counter_start_market_start_utc,
    last_included_market_start_utc = excluded.last_included_market_start_utc,
    high_water_market_start_utc = excluded.high_water_market_start_utc,
    counter_initialized = excluded.counter_initialized,
    up_count = excluded.up_count,
    down_count = excluded.down_count,
    diff_count = excluded.diff_count,
    diff = excluded.diff,
    processed_market_count = excluded.processed_market_count,
    history_fetch_failed_at_utc = excluded.history_fetch_failed_at_utc,
    history_fetch_retry_after_utc = excluded.history_fetch_retry_after_utc,
    history_fetch_error = excluded.history_fetch_error,
    updated_at_utc = excluded.updated_at_utc;
""");
		AddCryptoUpDown5mDiffSnapshotParameters(command, snapshot);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<CryptoUpDown5mDiffSnapshot>> GetCryptoUpDown5mDiffSnapshotsAsync(IReadOnlyCollection<string> assetSymbols, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedAssetSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToUpperInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedAssetSymbols.Length == 0 || endUtc <= startUtc)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, asset_symbol, market_start_utc, sampled_at_utc,
       counter_start_market_start_utc, last_included_market_start_utc, high_water_market_start_utc,
       counter_initialized, up_count, down_count, diff_count, diff, processed_market_count,
       history_fetch_failed_at_utc, history_fetch_retry_after_utc, history_fetch_error,
       created_at_utc, updated_at_utc
FROM crypto_up_down_5m_diff_snapshots
WHERE upper(asset_symbol) = ANY(@AssetSymbols)
  AND market_start_utc >= @StartUtc
  AND market_start_utc < @EndUtc
ORDER BY asset_symbol, market_start_utc;
""");
		command.Parameters.AddWithValue("AssetSymbols", normalizedAssetSymbols);
		command.Parameters.Add("StartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(startUtc);
		command.Parameters.Add("EndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endUtc);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mDiffSnapshot> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mDiffSnapshot(reader));
		}

		return results;
	}

	public async Task<CryptoUpDown5mDiffShiftProgressState?> GetCryptoUpDown5mDiffShiftProgressStateAsync(Guid strategyId, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT strategy_id, asset_symbol, trigger_outcome, up_count, down_count, sum_amount,
       damping_active, damping_direction,
       last_processed_market_start_utc, pending_market_start_utc, pending_target_outcome,
       pending_stake_usd, pending_created_at_utc, created_at_utc, updated_at_utc
FROM crypto_up_down_5m_diff_shift_progress_states
WHERE strategy_id = @StrategyId;
""");
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(strategyId));
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		return await reader.ReadAsync(cancellationToken)
			? ReadCryptoUpDown5mDiffShiftProgressState(reader)
			: null;
	}

	public async Task UpsertCryptoUpDown5mDiffShiftProgressStateAsync(CryptoUpDown5mDiffShiftProgressState state, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_up_down_5m_diff_shift_progress_states (
    strategy_id, asset_symbol, trigger_outcome, up_count, down_count, sum_amount,
    damping_active, damping_direction,
    last_processed_market_start_utc, pending_market_start_utc, pending_target_outcome,
    pending_stake_usd, pending_created_at_utc, created_at_utc, updated_at_utc
) VALUES (
    @StrategyId, @AssetSymbol, @TriggerOutcome, @UpCount, @DownCount, @SumAmount,
    @DampingActive, @DampingDirection,
    @LastProcessedMarketStartUtc, @PendingMarketStartUtc, @PendingTargetOutcome,
    @PendingStakeUsd, @PendingCreatedAtUtc, @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (strategy_id) DO UPDATE SET
    asset_symbol = excluded.asset_symbol,
    trigger_outcome = excluded.trigger_outcome,
    up_count = excluded.up_count,
    down_count = excluded.down_count,
    sum_amount = excluded.sum_amount,
    damping_active = excluded.damping_active,
    damping_direction = excluded.damping_direction,
    last_processed_market_start_utc = excluded.last_processed_market_start_utc,
    pending_market_start_utc = excluded.pending_market_start_utc,
    pending_target_outcome = excluded.pending_target_outcome,
    pending_stake_usd = excluded.pending_stake_usd,
    pending_created_at_utc = excluded.pending_created_at_utc,
    updated_at_utc = excluded.updated_at_utc;
""");
		AddCryptoUpDown5mDiffShiftProgressStateParameters(command, state);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task UpsertCryptoUpDown5mResultPollingObservationAsync(CryptoUpDown5mResultPollingObservation observation, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_up_down_5m_result_polling_observations (
    id, asset_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    first_observed_ended_at_utc, polling_started_at_utc, last_poll_at_utc, poll_attempts,
    first_closed_at_utc, first_winner_at_utc, winning_outcome,
    closed_delay_seconds, result_delay_seconds, status, last_response_status, last_error,
    created_at_utc, updated_at_utc
) VALUES (
    @Id, @AssetSymbol, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @FirstObservedEndedAtUtc, @PollingStartedAtUtc, @LastPollAtUtc, @PollAttempts,
    @FirstClosedAtUtc, @FirstWinnerAtUtc, @WinningOutcome,
    @ClosedDelaySeconds, @ResultDelaySeconds, @Status, @LastResponseStatus, @LastError,
    @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (market_id) DO UPDATE SET
    asset_symbol = excluded.asset_symbol,
    condition_id = excluded.condition_id,
    market_slug = excluded.market_slug,
    market_start_utc = excluded.market_start_utc,
    market_end_utc = excluded.market_end_utc,
    first_observed_ended_at_utc = LEAST(crypto_up_down_5m_result_polling_observations.first_observed_ended_at_utc, excluded.first_observed_ended_at_utc),
    polling_started_at_utc = LEAST(crypto_up_down_5m_result_polling_observations.polling_started_at_utc, excluded.polling_started_at_utc),
    last_poll_at_utc = excluded.last_poll_at_utc,
    poll_attempts = excluded.poll_attempts,
    first_closed_at_utc = COALESCE(crypto_up_down_5m_result_polling_observations.first_closed_at_utc, excluded.first_closed_at_utc),
    first_winner_at_utc = COALESCE(crypto_up_down_5m_result_polling_observations.first_winner_at_utc, excluded.first_winner_at_utc),
    winning_outcome = COALESCE(crypto_up_down_5m_result_polling_observations.winning_outcome, excluded.winning_outcome),
    closed_delay_seconds = COALESCE(crypto_up_down_5m_result_polling_observations.closed_delay_seconds, excluded.closed_delay_seconds),
    result_delay_seconds = COALESCE(crypto_up_down_5m_result_polling_observations.result_delay_seconds, excluded.result_delay_seconds),
    status = excluded.status,
    last_response_status = excluded.last_response_status,
    last_error = excluded.last_error,
    updated_at_utc = excluded.updated_at_utc;
""");
		AddCryptoUpDown5mResultPollingObservationParameters(command, observation);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<CryptoUpDown5mResultPollingObservation>> GetCryptoUpDown5mResultPollingObservationsAsync(IReadOnlyCollection<string> assetSymbols, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedAssetSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToUpperInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedAssetSymbols.Length == 0 || endUtc <= startUtc)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, asset_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       first_observed_ended_at_utc, polling_started_at_utc, last_poll_at_utc, poll_attempts,
       first_closed_at_utc, first_winner_at_utc, winning_outcome,
       closed_delay_seconds, result_delay_seconds, status, last_response_status, last_error,
       created_at_utc, updated_at_utc
FROM crypto_up_down_5m_result_polling_observations
WHERE upper(asset_symbol) = ANY(@AssetSymbols)
  AND market_start_utc >= @StartUtc
  AND market_start_utc < @EndUtc
ORDER BY asset_symbol, market_start_utc;
""");
		command.Parameters.AddWithValue("AssetSymbols", normalizedAssetSymbols);
		command.Parameters.Add("StartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(startUtc);
		command.Parameters.Add("EndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endUtc);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mResultPollingObservation> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mResultPollingObservation(reader));
		}

		return results;
	}

	public async Task UpsertCryptoUpDown5mWebSocketResolvedMarketAsync(CryptoUpDown5mWebSocketResolvedMarket resolvedMarket, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO crypto_up_down_5m_websocket_resolved_markets (
    id, asset_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
    winning_outcome, winning_asset_id, event_timestamp_utc,
    first_received_at_utc, last_received_at_utc, event_count, result_delay_seconds,
    source, raw_event_type, raw_json, created_at_utc, updated_at_utc
) VALUES (
    @Id, @AssetSymbol, @MarketId, @ConditionId, @MarketSlug, @MarketStartUtc, @MarketEndUtc,
    @WinningOutcome, @WinningAssetId, @EventTimestampUtc,
    @FirstReceivedAtUtc, @LastReceivedAtUtc, @EventCount, @ResultDelaySeconds,
    @Source, @RawEventType, CAST(@RawJson AS jsonb), @CreatedAtUtc, @UpdatedAtUtc
)
ON CONFLICT (asset_symbol, market_start_utc) DO UPDATE SET
    market_id = excluded.market_id,
    condition_id = excluded.condition_id,
    market_slug = excluded.market_slug,
    market_end_utc = excluded.market_end_utc,
    winning_outcome = excluded.winning_outcome,
    winning_asset_id = COALESCE(crypto_up_down_5m_websocket_resolved_markets.winning_asset_id, excluded.winning_asset_id),
    event_timestamp_utc = LEAST(crypto_up_down_5m_websocket_resolved_markets.event_timestamp_utc, excluded.event_timestamp_utc),
    first_received_at_utc = LEAST(crypto_up_down_5m_websocket_resolved_markets.first_received_at_utc, excluded.first_received_at_utc),
    last_received_at_utc = GREATEST(crypto_up_down_5m_websocket_resolved_markets.last_received_at_utc, excluded.last_received_at_utc),
    event_count = crypto_up_down_5m_websocket_resolved_markets.event_count + GREATEST(1, excluded.event_count),
    result_delay_seconds = LEAST(crypto_up_down_5m_websocket_resolved_markets.result_delay_seconds, excluded.result_delay_seconds),
    source = excluded.source,
    raw_event_type = excluded.raw_event_type,
    raw_json = excluded.raw_json,
    updated_at_utc = excluded.updated_at_utc;
""");
		AddCryptoUpDown5mWebSocketResolvedMarketParameters(command, resolvedMarket);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<CryptoUpDown5mWebSocketResolvedMarket>> GetCryptoUpDown5mWebSocketResolvedMarketsAsync(IReadOnlyCollection<string> assetSymbols, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		var normalizedAssetSymbols = assetSymbols
			.Select(symbol => symbol.Trim().ToUpperInvariant())
			.Where(symbol => !string.IsNullOrWhiteSpace(symbol))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalizedAssetSymbols.Length == 0 || endUtc < startUtc)
		{
			return [];
		}

		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
SELECT id, asset_symbol, market_id, condition_id, market_slug, market_start_utc, market_end_utc,
       winning_outcome, winning_asset_id, event_timestamp_utc,
       first_received_at_utc, last_received_at_utc, event_count, result_delay_seconds,
       source, raw_event_type, raw_json::text, created_at_utc, updated_at_utc
FROM crypto_up_down_5m_websocket_resolved_markets
WHERE upper(asset_symbol) = ANY(@AssetSymbols)
  AND market_start_utc >= @StartUtc
  AND market_start_utc <= @EndUtc
ORDER BY asset_symbol, market_start_utc;
""");
		command.Parameters.AddWithValue("AssetSymbols", normalizedAssetSymbols);
		command.Parameters.Add("StartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(startUtc);
		command.Parameters.Add("EndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(endUtc);
		await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		List<CryptoUpDown5mWebSocketResolvedMarket> results = [];
		while (await reader.ReadAsync(cancellationToken))
		{
			results.Add(ReadCryptoUpDown5mWebSocketResolvedMarket(reader));
		}

		return results;
	}

	public async Task AddMarketResolvedEventDiagnosticAsync(MarketResolvedEventDiagnostic diagnostic, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO market_resolved_event_diagnostics (
    id, component, raw_event_type, asset_id, condition_id,
    winning_asset_id, winning_outcome, event_timestamp_utc, received_at_utc,
    active_snapshot_found, snapshot_market_id, snapshot_condition_id, snapshot_market_slug,
    snapshot_asset_symbol, snapshot_market_start_utc, snapshot_is_crypto_up_down_5m,
    recorder_action, raw_json, created_at_utc
) VALUES (
    @Id, @Component, @RawEventType, @AssetId, @ConditionId,
    @WinningAssetId, @WinningOutcome, @EventTimestampUtc, @ReceivedAtUtc,
    @ActiveSnapshotFound, @SnapshotMarketId, @SnapshotConditionId, @SnapshotMarketSlug,
    @SnapshotAssetSymbol, @SnapshotMarketStartUtc, @SnapshotIsCryptoUpDown5m,
    @RecorderAction, CAST(@RawJson AS jsonb), @CreatedAtUtc
);
""");
		AddMarketResolvedEventDiagnosticParameters(command, diagnostic);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	public async Task AddMarketWebSocketFrameDiagnosticAsync(MarketWebSocketFrameDiagnostic diagnostic, CancellationToken cancellationToken = default(CancellationToken))
	{
		await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO market_websocket_frame_diagnostics (
    id, component, received_at_utc, frame_kind, payload_length_chars, payload_sha256,
    event_count, event_types_json, asset_ids_json, market_ids_json,
    contains_market_resolved_text, contains_resolved_text, parse_succeeded,
    parsed_update_count, parse_error, raw_payload, raw_payload_truncated, created_at_utc
) VALUES (
    @Id, @Component, @ReceivedAtUtc, @FrameKind, @PayloadLengthChars, @PayloadSha256,
    @EventCount, CAST(@EventTypesJson AS jsonb), CAST(@AssetIdsJson AS jsonb), CAST(@MarketIdsJson AS jsonb),
    @ContainsMarketResolvedText, @ContainsResolvedText, @ParseSucceeded,
    @ParsedUpdateCount, @ParseError, @RawPayload, @RawPayloadTruncated, @CreatedAtUtc
);
""");
		AddMarketWebSocketFrameDiagnosticParameters(command, diagnostic);
		await command.ExecuteNonQueryAsync(cancellationToken);
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
		command.Parameters.Add("RawJson", NpgsqlDbType.Text).Value = DBNull.Value;
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

	private static async Task<int> RecoverPaperCopiedTraderPerformanceInflightAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string workKind,
		int walletLimit,
		CancellationToken cancellationToken)
	{
		if (walletLimit <= 0)
		{
			return 0;
		}

		await using NpgsqlCommand command = CreateCommand(connection, """
INSERT INTO temp_paper_copied_trader_performance_wallets (
    copied_trader_wallet,
    work_kind)
SELECT
    inflight.copied_trader_wallet,
    inflight.work_kind
FROM paper_copied_trader_performance_refresh_inflight inflight
WHERE inflight.work_kind = @WorkKind
ORDER BY inflight.priority DESC, inflight.requested_at_utc, inflight.copied_trader_wallet
LIMIT @WalletLimit
ON CONFLICT (copied_trader_wallet) DO NOTHING;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("WorkKind", workKind);
		command.Parameters.AddWithValue("WalletLimit", walletLimit);
		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<int> ClaimPaperCopiedTraderPerformanceQueueAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string workKind,
		int walletLimit,
		CancellationToken cancellationToken)
	{
		if (walletLimit <= 0)
		{
			return 0;
		}

		var priorityPredicate = workKind switch
		{
			"high_priority" => "queue.priority > 0",
			"reconciliation" => "queue.priority <= 0",
			_ => throw new ArgumentOutOfRangeException(nameof(workKind), workKind, "Unsupported projection work kind.")
		};
		await using NpgsqlCommand command = CreateCommand(connection, $"""
WITH picked AS (
    SELECT queue.copied_trader_wallet
    FROM paper_copied_trader_performance_refresh_queue queue
    WHERE {priorityPredicate}
      AND NOT EXISTS (
          SELECT 1
          FROM paper_copied_trader_performance_refresh_inflight inflight
          WHERE inflight.copied_trader_wallet = queue.copied_trader_wallet
      )
    ORDER BY queue.priority DESC, queue.requested_at_utc, queue.copied_trader_wallet
    LIMIT @WalletLimit
    FOR UPDATE OF queue SKIP LOCKED
), removed AS (
    DELETE FROM paper_copied_trader_performance_refresh_queue queue
    USING picked
    WHERE queue.copied_trader_wallet = picked.copied_trader_wallet
    RETURNING
        queue.copied_trader_wallet,
        queue.priority,
        queue.requested_at_utc,
        queue.source_kind
), claimed AS (
    INSERT INTO paper_copied_trader_performance_refresh_inflight (
        copied_trader_wallet,
        priority,
        requested_at_utc,
        source_kind,
        work_kind,
        claimed_at_utc)
    SELECT
        removed.copied_trader_wallet,
        removed.priority,
        removed.requested_at_utc,
        removed.source_kind,
        @WorkKind,
        clock_timestamp()
    FROM removed
    RETURNING copied_trader_wallet
)
INSERT INTO temp_paper_copied_trader_performance_wallets (
    copied_trader_wallet,
    work_kind)
SELECT claimed.copied_trader_wallet, @WorkKind
FROM claimed;
""");
		command.Transaction = transaction;
		command.Parameters.AddWithValue("WorkKind", workKind);
		command.Parameters.AddWithValue("WalletLimit", walletLimit);
		return await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task<bool> TryAcquirePaperCopiedTraderPerformanceRefreshLockAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		await using NpgsqlCommand command = CreateCommand(connection, "SELECT pg_try_advisory_lock(@LockKey1, @LockKey2);");
		command.Parameters.AddWithValue("LockKey1", PaperCopiedTraderPerformanceRefreshLockKey1);
		command.Parameters.AddWithValue("LockKey2", PaperCopiedTraderPerformanceRefreshLockKey2);
		return await command.ExecuteScalarAsync(cancellationToken) is true;
	}

	private static async Task ReleasePaperCopiedTraderPerformanceRefreshLockAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		try
		{
			await using NpgsqlCommand command = CreateCommand(connection, "SELECT pg_advisory_unlock(@LockKey1, @LockKey2);");
			command.Parameters.AddWithValue("LockKey1", PaperCopiedTraderPerformanceRefreshLockKey1);
			command.Parameters.AddWithValue("LockKey2", PaperCopiedTraderPerformanceRefreshLockKey2);
			if (await command.ExecuteScalarAsync(cancellationToken) is not true)
			{
				throw new InvalidOperationException("The paper copied-trader performance refresh advisory lock was not held by this session.");
			}
		}
		catch
		{
			NpgsqlConnection.ClearPool(connection);
			throw;
		}
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

	private static object NullableInt32(int? value)
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
			reader.IsDBNull(27) ? null : reader.GetString(27),
			reader.GetDecimal(28),
			reader.GetString(29),
			reader.GetString(30),
			reader.GetString(31),
			reader.IsDBNull(32) ? null : reader.GetDecimal(32),
			reader.IsDBNull(33) ? null : reader.GetInt32(33),
			reader.IsDBNull(34) ? null : reader.GetBoolean(34),
			reader.IsDBNull(35) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(35)),
			reader.IsDBNull(36) ? null : reader.GetDecimal(36));
	}

	private static StrategyChildParentAssignment ReadStrategyChildParentAssignment(NpgsqlDataReader reader)
	{
		return new StrategyChildParentAssignment(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetGuid(2),
			reader.GetString(3),
			reader.GetInt32(4),
			reader.GetString(5),
			reader.GetDecimal(6),
			reader.GetDecimal(7),
			DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			reader.IsDBNull(9) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(9)),
			DateTimeOffsetFromUtc(reader.GetDateTime(10)));
	}

	private static StrategyLossDiffState ReadStrategyLossDiffState(NpgsqlDataReader reader)
	{
		return new StrategyLossDiffState(
			reader.GetGuid(0),
			reader.GetGuid(1),
			reader.GetString(2),
			reader.GetInt32(3),
			reader.GetInt32(4),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			reader.IsDBNull(7) ? null : reader.GetGuid(7),
			reader.IsDBNull(8) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			DateTimeOffsetFromUtc(reader.GetDateTime(9)));
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
		command.Parameters.Add("SkipDiagnosticsJson", NpgsqlDbType.Text).Value =
			((object?)GetPersistedSkipDiagnosticsJson(run)) ?? DBNull.Value;
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(run.CreatedAtUtc));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(run.UpdatedAtUtc));
		command.Parameters.AddWithValue("FeeUsd", run.FeeUsd);
		command.Parameters.AddWithValue("FeeAccountingStatus", run.FeeAccountingStatus);
		command.Parameters.AddWithValue("FeeLiquidityRole", run.FeeLiquidityRole);
		command.Parameters.AddWithValue("FeeCalculationSource", run.FeeCalculationSource);
		command.Parameters.AddWithValue("FeeRate", NullableDecimal(run.FeeRate));
		command.Parameters.AddWithValue("FeeExponent", run.FeeExponent.HasValue ? run.FeeExponent.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeTakerOnly", run.FeeTakerOnly.HasValue ? run.FeeTakerOnly.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeCalculatedAtUtc", NullableDateTime(run.FeeCalculatedAtUtc));
		command.Parameters.AddWithValue("NetRealizedPnlUsd", NullableDecimal(run.NetRealizedPnlUsd));
	}

	internal static string? GetPersistedSkipDiagnosticsJson(StrategyMarketPaperRun run)
	{
		if (!string.Equals(run.Status, StrategyMarketPaperRunStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
		{
			return run.SkipDiagnosticsJson;
		}

		return IsMakerGtdPlacementSkipDiagnostics(run) || IsLossDiffPlacementSkipDiagnostics(run) || IsPositiveProgressSkipDiagnostics(run)
			? run.SkipDiagnosticsJson
			: null;
	}

	private static bool IsLossDiffPlacementSkipDiagnostics(StrategyMarketPaperRun run)
	{
		var normalizedStrategyId = StrategyIds.Normalize(run.StrategyId);
		var expectedParentStrategyId = normalizedStrategyId switch
		{
			var id when id == StrategyIds.EthLossDiff4Plus ||
				id == StrategyIds.EthLossDiff13PlusPositive =>
				StrategyIds.EthDiffConfirmedAveragePremarketParent,
			var id when id == StrategyIds.EthUp8BpsLossDiff3Plus ||
				id == StrategyIds.EthUp8BpsLossDiff16PlusPositive =>
				StrategyIds.EthUp8BpsReferenceAveragePremarketParent,
			_ => Guid.Empty
		};
		if (expectedParentStrategyId == Guid.Empty ||
			!string.Equals(run.SkipReason, "parent_lossdiff_below_threshold", StringComparison.Ordinal) ||
			string.IsNullOrWhiteSpace(run.SkipDiagnosticsJson))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(run.SkipDiagnosticsJson);
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object &&
				root.TryGetProperty("pricing_mode", out var pricingMode) &&
				string.Equals(pricingMode.GetString(), "child_parent_mirror", StringComparison.Ordinal) &&
				root.TryGetProperty("child_strategy_id", out var childStrategyId) &&
				Guid.TryParse(childStrategyId.GetString(), out var parsedChildStrategyId) &&
				StrategyIds.Normalize(parsedChildStrategyId) == normalizedStrategyId &&
				root.TryGetProperty("parent_strategy_id", out var parentStrategyId) &&
				Guid.TryParse(parentStrategyId.GetString(), out var parsedParentStrategyId) &&
				StrategyIds.Normalize(parsedParentStrategyId) == expectedParentStrategyId &&
				root.TryGetProperty("parent_run_id", out var parentRunId) &&
				Guid.TryParse(parentRunId.GetString(), out _) &&
				root.TryGetProperty("loss_diff", out var lossDiff) &&
				lossDiff.ValueKind == JsonValueKind.Object &&
				lossDiff.TryGetProperty("gate_passed", out var gatePassed) &&
				gatePassed.ValueKind == JsonValueKind.False &&
				lossDiff.TryGetProperty("pre_entry_value", out var currentValue) &&
				currentValue.TryGetInt32(out var parsedCurrentValue) &&
				lossDiff.TryGetProperty("threshold", out var threshold) &&
				threshold.TryGetInt32(out var parsedThreshold) &&
				parsedCurrentValue >= 0 &&
				parsedThreshold > parsedCurrentValue;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool IsPositiveProgressSkipDiagnostics(StrategyMarketPaperRun run)
	{
		var variant = StrategyIds.UpDown5mStrategyVariants.FirstOrDefault(v =>
			v.Id == run.StrategyId && v.Behavior == BtcUpDown5mStrategyBehavior.LossDiffPositiveProgressMirror);
		if (variant is null || run.SkipReason is not ("parent_lossdiff_below_threshold" or "positive_progress_fak_no_fill") ||
			string.IsNullOrWhiteSpace(run.SkipDiagnosticsJson)) return false;
		try
		{
			using var document = JsonDocument.Parse(run.SkipDiagnosticsJson);
			var root = document.RootElement;
			return root.TryGetProperty("child_strategy_id", out var child) && child.TryGetGuid(out var childId) && childId == variant.Id &&
				root.TryGetProperty("parent_strategy_id", out var parent) && parent.TryGetGuid(out var parentId) && parentId == variant.ParentStrategyId &&
				root.TryGetProperty("parent_run_id", out var parentRun) && parentRun.TryGetGuid(out _) &&
				root.TryGetProperty("loss_diff", out var state) && state.ValueKind == JsonValueKind.Object &&
				state.TryGetProperty("pre_entry_value", out var value) && value.TryGetInt32(out var current) && current >= 0 &&
				(run.SkipReason == "parent_lossdiff_below_threshold"
					? current == 0 && state.TryGetProperty("gate_passed", out var gate) && gate.ValueKind == JsonValueKind.False
					: current > 0 && root.TryGetProperty("positive_progress", out var progress) && progress.ValueKind == JsonValueKind.Object);
		}
		catch (JsonException) { return false; }
	}

	private static bool IsMakerGtdPlacementSkipDiagnostics(StrategyMarketPaperRun run)
	{
		const string executionSource = "eth_reference_average_maker_gtd_paper";
		if (string.IsNullOrWhiteSpace(run.SkipDiagnosticsJson) ||
			run.SkipReason is not
				("maker_gtd_variant_not_paper_only" or
				 "maker_gtd_variant_outside_closed_exception" or
				 "maker_gtd_market_start_unknown" or
				 "maker_gtd_market_end_unknown" or
				 "maker_gtd_effective_expiration_elapsed" or
				 "maker_gtd_maximum_order_price_invalid" or
				 "maker_gtd_premarket_entry_window_elapsed" or
				 "maker_gtd_post_only_attempts_exhausted"))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(run.SkipDiagnosticsJson);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("execution_source", out var rootExecutionSource) ||
				!string.Equals(rootExecutionSource.GetString(), executionSource, StringComparison.Ordinal) ||
				!root.TryGetProperty("skip_reason", out var rootSkipReason) ||
				!string.Equals(rootSkipReason.GetString(), run.SkipReason, StringComparison.Ordinal) ||
				!root.TryGetProperty("maker_gtd", out var makerGtd) ||
				makerGtd.ValueKind != JsonValueKind.Object ||
				!makerGtd.TryGetProperty("execution_source", out var makerExecutionSource) ||
				!string.Equals(makerExecutionSource.GetString(), executionSource, StringComparison.Ordinal) ||
				!makerGtd.TryGetProperty("terminal_outcome", out var terminalOutcome) ||
				!string.Equals(terminalOutcome.GetString(), "skipped", StringComparison.Ordinal) ||
				!makerGtd.TryGetProperty("terminal_reason", out var terminalReason) ||
				!string.Equals(terminalReason.GetString(), run.SkipReason, StringComparison.Ordinal))
			{
				return false;
			}

			return true;
		}
		catch (JsonException)
		{
			return false;
		}
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
		command.Parameters.Add("RawContextJson", NpgsqlDbType.Text).Value = DBNull.Value;
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

	private static void AddPaperFillParameters(NpgsqlCommand command, PaperFill fill)
	{
		command.Parameters.AddWithValue("Id", fill.Id);
		command.Parameters.AddWithValue("PaperOrderId", fill.PaperOrderId);
		command.Parameters.AddWithValue("Price", fill.Price);
		command.Parameters.AddWithValue("SizeShares", fill.SizeShares);
		command.Parameters.AddWithValue("FilledAtUtc", UtcDateTime(fill.FilledAtUtc));
		command.Parameters.AddWithValue("Evidence", fill.Evidence);
		command.Parameters.AddWithValue("RealizedPnlUsd", fill.RealizedPnlUsd);
		command.Parameters.AddWithValue("FeeUsd", fill.FeeUsd);
		command.Parameters.AddWithValue("FeeAccountingStatus", fill.FeeAccountingStatus);
		command.Parameters.AddWithValue("FeeLiquidityRole", fill.FeeLiquidityRole);
		command.Parameters.AddWithValue("FeeCalculationSource", fill.FeeCalculationSource);
		command.Parameters.AddWithValue("FeeRate", NullableDecimal(fill.FeeRate));
		command.Parameters.AddWithValue("FeeExponent", fill.FeeExponent.HasValue ? fill.FeeExponent.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeTakerOnly", fill.FeeTakerOnly.HasValue ? fill.FeeTakerOnly.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeCalculatedAtUtc", NullableDateTime(fill.FeeCalculatedAtUtc));
		command.Parameters.AddWithValue("NetRealizedPnlUsd", NullableDecimal(fill.NetRealizedPnlUsd));
	}

	private static void AddPaperPositionParameters(NpgsqlCommand command, PaperPosition position)
	{
		command.Parameters.AddWithValue("Id", Guid.NewGuid());
		command.Parameters.AddWithValue("CopiedTraderWallet", position.CopiedTraderWallet);
		command.Parameters.AddWithValue("AssetId", position.AssetId);
		command.Parameters.AddWithValue("ConditionId", position.ConditionId);
		command.Parameters.AddWithValue("Outcome", position.Outcome);
		command.Parameters.AddWithValue("SizeShares", position.SizeShares);
		command.Parameters.AddWithValue("AveragePrice", position.AveragePrice);
		command.Parameters.AddWithValue("EstimatedValueUsd", position.EstimatedValueUsd);
		command.Parameters.AddWithValue("UnrealizedPnlUsd", position.UnrealizedPnlUsd);
		command.Parameters.AddWithValue("FeeUsd", position.FeeUsd);
		command.Parameters.AddWithValue("FeeAccountingStatus", position.FeeAccountingStatus);
		command.Parameters.AddWithValue("FeeLiquidityRole", position.FeeLiquidityRole);
		command.Parameters.AddWithValue("FeeCalculationSource", position.FeeCalculationSource);
		command.Parameters.AddWithValue("FeeRate", NullableDecimal(position.FeeRate));
		command.Parameters.AddWithValue("FeeExponent", position.FeeExponent.HasValue ? position.FeeExponent.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeTakerOnly", position.FeeTakerOnly.HasValue ? position.FeeTakerOnly.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeCalculatedAtUtc", NullableDateTime(position.FeeCalculatedAtUtc));
		command.Parameters.AddWithValue("NetUnrealizedPnlUsd", NullableDecimal(position.NetUnrealizedPnlUsd));
		command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(position.UpdatedAtUtc));
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
			reader.GetDecimal(6),
			reader.GetDecimal(7),
			reader.GetString(8),
			reader.GetString(9),
			reader.GetString(10),
			reader.IsDBNull(11) ? null : reader.GetDecimal(11),
			reader.IsDBNull(12) ? null : reader.GetInt32(12),
			reader.IsDBNull(13) ? null : reader.GetBoolean(13),
			reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)),
			reader.IsDBNull(15) ? null : reader.GetDecimal(15));
	}

	private static PaperPosition ReadPaperPosition(NpgsqlDataReader reader)
	{
		return new PaperPosition(
			reader.GetString(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetDecimal(3),
			reader.GetDecimal(4),
			reader.GetDecimal(5),
			reader.GetDecimal(6),
			DateTimeOffsetFromUtc(reader.GetDateTime(7)),
			reader.GetString(8),
			reader.GetDecimal(9),
			reader.GetString(10),
			reader.GetString(11),
			reader.GetString(12),
			reader.IsDBNull(13) ? null : reader.GetDecimal(13),
			reader.IsDBNull(14) ? null : reader.GetInt32(14),
			reader.IsDBNull(15) ? null : reader.GetBoolean(15),
			reader.IsDBNull(16) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(16)),
			reader.IsDBNull(17) ? null : reader.GetDecimal(17));
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

	internal static string NormalizeLiveOrderRawResponseJson(string? rawResponseJson)
	{
		if (string.IsNullOrWhiteSpace(rawResponseJson))
		{
			return "{}";
		}

		var trimmed = rawResponseJson.Trim();
		try
		{
			using var document = JsonDocument.Parse(trimmed);
			return trimmed;
		}
		catch (JsonException)
		{
			return JsonSerializer.Serialize(new { raw = rawResponseJson });
		}
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
		command.Parameters.AddWithValue("FeeAccountingStatus", order.FeeAccountingStatus);
		command.Parameters.AddWithValue("FeeLiquidityRole", order.FeeLiquidityRole);
		command.Parameters.AddWithValue("FeeCalculationSource", order.FeeCalculationSource);
		command.Parameters.AddWithValue("FeeRate", NullableDecimal(order.FeeRate));
		command.Parameters.AddWithValue("FeeExponent", order.FeeExponent.HasValue ? order.FeeExponent.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeTakerOnly", order.FeeTakerOnly.HasValue ? order.FeeTakerOnly.Value : (object)DBNull.Value);
		command.Parameters.AddWithValue("FeeCalculatedAtUtc", NullableDateTime(order.FeeCalculatedAtUtc));
		command.Parameters.AddWithValue("CancelStatus", order.CancelStatus);
		command.Parameters.AddWithValue("RawResponseJson", NormalizeLiveOrderRawResponseJson(order.RawResponseJson));
		command.Parameters.AddWithValue("ValidationSummary", order.ValidationSummary);
		command.Parameters.AddWithValue("BalanceEffectApplied", order.BalanceEffectApplied);
		command.Parameters.AddWithValue("SettlementValueUsd", ((object)order.SettlementValueUsd) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("RealizedPnlUsd", ((object)order.RealizedPnlUsd) ?? ((object)DBNull.Value));
		command.Parameters.AddWithValue("NetRealizedPnlUsd", ((object)order.NetRealizedPnlUsd) ?? ((object)DBNull.Value));
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
		command.Parameters.AddWithValue(
			"HistoricalGrossNetParityOwnership",
			order.HistoricalGrossNetParityOwnership.ToString());
		command.Parameters.AddWithValue("RowVersion", order.RowVersion);
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
		command.Parameters.AddWithValue("OrderBookSnapshotJson", "{}");
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

	private static void AddCryptoReferencePriceTickParameters(NpgsqlCommand command, CryptoReferencePriceTick tick)
	{
		command.Parameters.AddWithValue("Id", tick.Id);
		command.Parameters.AddWithValue("AssetSymbol", tick.AssetSymbol.Trim().ToUpperInvariant());
		command.Parameters.AddWithValue("BinanceSymbol", tick.BinanceSymbol.Trim().ToUpperInvariant());
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.SampledAtUtc);
		command.Parameters.Add("BucketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.BucketStartUtc);
		command.Parameters.AddWithValue("PriceUsd", tick.PriceUsd);
		command.Parameters.Add("SourceUpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.SourceUpdatedAtUtc);
		command.Parameters.Add("FetchedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.FetchedAtUtc);
		command.Parameters.AddWithValue("Source", tick.Source);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.CreatedAtUtc);
	}

	private static CryptoReferencePriceTick ReadCryptoReferencePriceTick(NpgsqlDataReader reader)
	{
		return new CryptoReferencePriceTick(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			DateTimeOffsetFromUtc(reader.GetDateTime(3)),
			DateTimeOffsetFromUtc(reader.GetDateTime(4)),
			reader.GetDecimal(5),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			DateTimeOffsetFromUtc(reader.GetDateTime(7)),
			reader.GetString(8),
			DateTimeOffsetFromUtc(reader.GetDateTime(9)));
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

	private static void AddBtcUpDown5mStrategyStageTimingParameters(
		NpgsqlCommand command,
		BtcUpDown5mStrategyStageTiming timing)
	{
		command.Parameters.AddWithValue("Id", timing.Id);
		command.Parameters.AddWithValue("CycleId", timing.CycleId);
		command.Parameters.AddWithValue("CycleKind", timing.CycleKind);
		command.Parameters.AddWithValue("FlowName", ((object?)timing.FlowName) ?? DBNull.Value);
		command.Parameters.AddWithValue("StageName", timing.StageName);
		command.Parameters.AddWithValue("Detail", ((object?)timing.Detail) ?? DBNull.Value);
		command.Parameters.AddWithValue("StartedAtUtc", UtcDateTime(timing.StartedAtUtc));
		command.Parameters.AddWithValue("CompletedAtUtc", UtcDateTime(timing.CompletedAtUtc));
		command.Parameters.AddWithValue("DurationMs", timing.DurationMilliseconds);
		command.Parameters.AddWithValue("VariantCount", ((object?)timing.VariantCount) ?? DBNull.Value);
		command.Parameters.AddWithValue("RunCount", ((object?)timing.RunCount) ?? DBNull.Value);
		command.Parameters.AddWithValue("EntriesPlaced", ((object?)timing.EntriesPlaced) ?? DBNull.Value);
		command.Parameters.AddWithValue("RunsSkipped", ((object?)timing.RunsSkipped) ?? DBNull.Value);
		command.Parameters.AddWithValue("RunsSettled", ((object?)timing.RunsSettled) ?? DBNull.Value);
		command.Parameters.AddWithValue("MarketsObserved", ((object?)timing.MarketsObserved) ?? DBNull.Value);
		command.Parameters.AddWithValue("EarliestEntryDueAtUtc", NullableDateTime(timing.EarliestEntryDueAtUtc));
		command.Parameters.AddWithValue("LatestEntryDueAtUtc", NullableDateTime(timing.LatestEntryDueAtUtc));
		command.Parameters.AddWithValue("Succeeded", timing.Succeeded);
		command.Parameters.AddWithValue("ErrorMessage", ((object?)timing.ErrorMessage) ?? DBNull.Value);
		command.Parameters.AddWithValue("CreatedAtUtc", UtcDateTime(timing.CreatedAtUtc));
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
		command.Parameters.AddWithValue("DiagnosticsJson", "{}");
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

	private static void AddBtcUpDown5mStatisticsTickParameters(NpgsqlCommand command, BtcUpDown5mStatisticsTick tick)
	{
		command.Parameters.AddWithValue("Id", tick.Id);
		command.Parameters.AddWithValue("MarketId", tick.MarketId);
		command.Parameters.AddWithValue("ConditionId", tick.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", tick.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.MarketEndUtc);
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.SampledAtUtc);
		command.Parameters.AddWithValue("SecondsAfterStart", tick.SecondsAfterStart);
		command.Parameters.AddWithValue("SecondsToClose", tick.SecondsToClose);
		command.Parameters.AddWithValue("BinancePriceUsd", tick.BinancePriceUsd);
		command.Parameters.Add("BinanceSourceUpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.BinanceSourceUpdatedAtUtc);
		command.Parameters.Add("BinanceFetchedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.BinanceFetchedAtUtc);
		command.Parameters.AddWithValue("BinanceStartPriceUsd", NullableDecimal(tick.BinanceStartPriceUsd));
		command.Parameters.AddWithValue("BtcMoveFromStartUsd", NullableDecimal(tick.BtcMoveFromStartUsd));
		command.Parameters.AddWithValue("BtcMoveFromStartCents", NullableDecimal(tick.BtcMoveFromStartCents));
		command.Parameters.AddWithValue("SecondsLower", NullableInt32(tick.SecondsLower));
		command.Parameters.AddWithValue("SecondsUpper", NullableInt32(tick.SecondsUpper));
		command.Parameters.AddWithValue("CentsLower", NullableInt32(tick.CentsLower));
		command.Parameters.AddWithValue("CentsUpper", NullableInt32(tick.CentsUpper));
		command.Parameters.AddWithValue("EffectiveCount", NullableDecimal(tick.EffectiveCount));
		command.Parameters.AddWithValue("UpProbability", NullableDecimal(tick.UpProbability));
		command.Parameters.AddWithValue("DownProbability", NullableDecimal(tick.DownProbability));
		command.Parameters.AddWithValue("SupportThreshold", tick.SupportThreshold);
		command.Parameters.AddWithValue("HistoryRowsFound", tick.HistoryRowsFound);
		command.Parameters.AddWithValue("MissingHistoryCorners", tick.MissingHistoryCorners);
		command.Parameters.AddWithValue("InterpolationMethod", tick.InterpolationMethod);
		command.Parameters.AddWithValue("UpAssetId", tick.UpAssetId);
		command.Parameters.AddWithValue("UpMarketPrice", NullableDecimal(tick.UpMarketPrice));
		command.Parameters.AddWithValue("UpMarketPriceKind", tick.UpMarketPriceKind);
		command.Parameters.AddWithValue("DownAssetId", tick.DownAssetId);
		command.Parameters.AddWithValue("DownMarketPrice", NullableDecimal(tick.DownMarketPrice));
		command.Parameters.AddWithValue("DownMarketPriceKind", tick.DownMarketPriceKind);
		command.Parameters.AddWithValue("UpEdge", NullableDecimal(tick.UpEdge));
		command.Parameters.AddWithValue("DownEdge", NullableDecimal(tick.DownEdge));
		command.Parameters.AddWithValue("DecisionCode", tick.DecisionCode);
		command.Parameters.AddWithValue("RecommendedOutcome", string.IsNullOrWhiteSpace(tick.RecommendedOutcome) ? DBNull.Value : tick.RecommendedOutcome);
		command.Parameters.AddWithValue("WouldBet", tick.WouldBet);
		command.Parameters.AddWithValue("DiagnosticsJson", "{}");
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(tick.CreatedAtUtc);
	}

	private static void AddBtcUpDown5mArbitrageScanParameters(NpgsqlCommand command, BtcUpDown5mArbitrageScan scan)
	{
		command.Parameters.AddWithValue("Id", scan.Id);
		command.Parameters.AddWithValue("MarketId", scan.MarketId);
		command.Parameters.AddWithValue("ConditionId", scan.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", scan.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(scan.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(scan.MarketEndUtc);
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(scan.SampledAtUtc);
		command.Parameters.AddWithValue("SecondsAfterStart", scan.SecondsAfterStart);
		command.Parameters.AddWithValue("SecondsToClose", scan.SecondsToClose);
		command.Parameters.AddWithValue("UpAssetId", scan.UpAssetId);
		command.Parameters.AddWithValue("UpBestBid", NullableDecimal(scan.UpBestBid));
		command.Parameters.AddWithValue("UpBestAsk", NullableDecimal(scan.UpBestAsk));
		command.Parameters.AddWithValue("UpAskDepthShares", NullableDecimal(scan.UpAskDepthShares));
		command.Parameters.AddWithValue("UpBookSource", scan.UpBookSource);
		command.Parameters.AddWithValue("UpBookAgeMs", NullableDecimal(scan.UpBookAgeMs));
		command.Parameters.AddWithValue("DownAssetId", scan.DownAssetId);
		command.Parameters.AddWithValue("DownBestBid", NullableDecimal(scan.DownBestBid));
		command.Parameters.AddWithValue("DownBestAsk", NullableDecimal(scan.DownBestAsk));
		command.Parameters.AddWithValue("DownAskDepthShares", NullableDecimal(scan.DownAskDepthShares));
		command.Parameters.AddWithValue("DownBookSource", scan.DownBookSource);
		command.Parameters.AddWithValue("DownBookAgeMs", NullableDecimal(scan.DownBookAgeMs));
		command.Parameters.AddWithValue("RequiredMinShares", scan.RequiredMinShares);
		command.Parameters.AddWithValue("MaxCommonExecutableShares", scan.MaxCommonExecutableShares);
		command.Parameters.AddWithValue("BestExecutableShares", NullableDecimal(scan.BestExecutableShares));
		command.Parameters.AddWithValue("UpCostUsd", NullableDecimal(scan.UpCostUsd));
		command.Parameters.AddWithValue("DownCostUsd", NullableDecimal(scan.DownCostUsd));
		command.Parameters.AddWithValue("TotalCostUsd", NullableDecimal(scan.TotalCostUsd));
		command.Parameters.AddWithValue("GuaranteedPayoutUsd", NullableDecimal(scan.GuaranteedPayoutUsd));
		command.Parameters.AddWithValue("GrossProfitUsd", NullableDecimal(scan.GrossProfitUsd));
		command.Parameters.AddWithValue("SafetyBufferUsd", NullableDecimal(scan.SafetyBufferUsd));
		command.Parameters.AddWithValue("NetProfitUsd", NullableDecimal(scan.NetProfitUsd));
		command.Parameters.AddWithValue("AverageCostPerShare", NullableDecimal(scan.AverageCostPerShare));
		command.Parameters.AddWithValue("EdgePerShare", NullableDecimal(scan.EdgePerShare));
		command.Parameters.AddWithValue("SafetyBufferPerShare", scan.SafetyBufferPerShare);
		command.Parameters.AddWithValue("MinNetProfitUsd", scan.MinNetProfitUsd);
		command.Parameters.AddWithValue("DecisionCode", scan.DecisionCode);
		command.Parameters.AddWithValue("WouldArbitrage", scan.WouldArbitrage);
		command.Parameters.AddWithValue("DiagnosticsJson", string.IsNullOrWhiteSpace(scan.DiagnosticsJson) ? "{}" : scan.DiagnosticsJson);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(scan.CreatedAtUtc);
	}

	private static void AddBtcUpDown5mResultStreakDiagnosticParameters(NpgsqlCommand command, BtcUpDown5mResultStreakDiagnostic diagnostic)
	{
		command.Parameters.AddWithValue("Id", diagnostic.Id);
		command.Parameters.AddWithValue("MarketId", diagnostic.MarketId);
		command.Parameters.AddWithValue("ConditionId", diagnostic.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", diagnostic.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(diagnostic.MarketEndUtc);
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.SampledAtUtc);
		command.Parameters.AddWithValue("LatestPreviousMarketId", string.IsNullOrWhiteSpace(diagnostic.LatestPreviousMarketId) ? DBNull.Value : diagnostic.LatestPreviousMarketId);
		command.Parameters.AddWithValue("LatestPreviousMarketSlug", string.IsNullOrWhiteSpace(diagnostic.LatestPreviousMarketSlug) ? DBNull.Value : diagnostic.LatestPreviousMarketSlug);
		command.Parameters.Add("LatestPreviousMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(diagnostic.LatestPreviousMarketStartUtc);
		command.Parameters.Add("LatestPreviousMarketEndUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(diagnostic.LatestPreviousMarketEndUtc);
		command.Parameters.AddWithValue("StreakWinningOutcome", string.IsNullOrWhiteSpace(diagnostic.StreakWinningOutcome) ? DBNull.Value : diagnostic.StreakWinningOutcome);
		command.Parameters.AddWithValue("BaseSelectedDirection", string.IsNullOrWhiteSpace(diagnostic.BaseSelectedDirection) ? DBNull.Value : diagnostic.BaseSelectedDirection);
		command.Parameters.AddWithValue("SelectedOutcome", string.IsNullOrWhiteSpace(diagnostic.SelectedOutcome) ? DBNull.Value : diagnostic.SelectedOutcome);
		command.Parameters.AddWithValue("CloseBookStreakResultCount", diagnostic.CloseBookStreakResultCount);
		command.Parameters.AddWithValue("CumulativeMoveMarketCount", diagnostic.CumulativeMoveMarketCount);
		command.Parameters.AddWithValue("LatestMoveBps", NullableDecimal(diagnostic.LatestMoveBps));
		command.Parameters.AddWithValue("LatestAbsMoveBps", NullableDecimal(diagnostic.LatestAbsMoveBps));
		command.Parameters.AddWithValue("CumulativeMoveBps", NullableDecimal(diagnostic.CumulativeMoveBps));
		command.Parameters.AddWithValue("CumulativeAbsMoveBps", NullableDecimal(diagnostic.CumulativeAbsMoveBps));
		command.Parameters.AddWithValue("RejectionReason", string.IsNullOrWhiteSpace(diagnostic.RejectionReason) ? DBNull.Value : diagnostic.RejectionReason);
		command.Parameters.AddWithValue("StreakTruncatedReason", string.IsNullOrWhiteSpace(diagnostic.StreakTruncatedReason) ? DBNull.Value : diagnostic.StreakTruncatedReason);
		command.Parameters.AddWithValue("DiagnosticsJson", string.IsNullOrWhiteSpace(diagnostic.DiagnosticsJson) ? "{}" : diagnostic.DiagnosticsJson);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.UpdatedAtUtc);
	}

	private static void AddBtc5mHistoryLiveObservationParameters(NpgsqlCommand command, Btc5mHistoryLiveObservation observation)
	{
		command.Parameters.AddWithValue("Id", observation.Id);
		command.Parameters.AddWithValue("MarketId", observation.MarketId);
		command.Parameters.AddWithValue("ConditionId", observation.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", observation.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.MarketEndUtc);
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.SampledAtUtc);
		command.Parameters.AddWithValue("Seconds", observation.Seconds);
		command.Parameters.AddWithValue("Cents", observation.Cents);
		command.Parameters.AddWithValue("BinancePriceUsd", observation.BinancePriceUsd);
		command.Parameters.AddWithValue("BinanceStartPriceUsd", observation.BinanceStartPriceUsd);
		command.Parameters.AddWithValue("BtcMoveFromStartUsd", observation.BtcMoveFromStartUsd);
		command.Parameters.AddWithValue("Result", string.IsNullOrWhiteSpace(observation.Result) ? DBNull.Value : observation.Result);
		command.Parameters.AddWithValue("AppliedToHistory", observation.AppliedToHistory);
		command.Parameters.AddWithValue("AppliedAtUtc", NullableDateTime(observation.AppliedAtUtc));
		command.Parameters.AddWithValue("ResultCheckAttempts", observation.ResultCheckAttempts);
		command.Parameters.Add("NextResultCheckUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.NextResultCheckUtc);
		command.Parameters.AddWithValue("LastResultError", string.IsNullOrWhiteSpace(observation.LastResultError) ? DBNull.Value : observation.LastResultError);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.UpdatedAtUtc);
	}

	private static Btc5mHistoryLiveObservation ReadBtc5mHistoryLiveObservation(NpgsqlDataReader reader)
	{
		return new Btc5mHistoryLiveObservation(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			DateTimeOffsetFromUtc(reader.GetDateTime(4)),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			reader.GetInt32(7),
			reader.GetInt32(8),
			reader.GetDecimal(9),
			reader.GetDecimal(10),
			reader.GetDecimal(11),
			reader.IsDBNull(12) ? null : reader.GetString(12),
			reader.GetBoolean(13),
			reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)),
			reader.GetInt32(15),
			DateTimeOffsetFromUtc(reader.GetDateTime(16)),
			reader.IsDBNull(17) ? null : reader.GetString(17),
			DateTimeOffsetFromUtc(reader.GetDateTime(18)),
			DateTimeOffsetFromUtc(reader.GetDateTime(19)));
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
		command.Parameters.AddWithValue("DiagnosticsJson", "{}");
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

	private static void AddCryptoUpDown5mDiffSnapshotParameters(NpgsqlCommand command, CryptoUpDown5mDiffSnapshot snapshot)
	{
		command.Parameters.AddWithValue("Id", snapshot.Id);
		command.Parameters.AddWithValue("AssetSymbol", snapshot.AssetSymbol.Trim().ToUpperInvariant());
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(snapshot.MarketStartUtc);
		command.Parameters.Add("SampledAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(snapshot.SampledAtUtc);
		command.Parameters.Add("CounterStartMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(snapshot.CounterStartMarketStartUtc);
		command.Parameters.Add("LastIncludedMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(snapshot.LastIncludedMarketStartUtc);
		command.Parameters.Add("HighWaterMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(snapshot.HighWaterMarketStartUtc);
		command.Parameters.AddWithValue("CounterInitialized", snapshot.CounterInitialized);
		command.Parameters.AddWithValue("UpCount", snapshot.UpCount);
		command.Parameters.AddWithValue("DownCount", snapshot.DownCount);
		command.Parameters.AddWithValue("DiffCount", snapshot.DiffCount);
		command.Parameters.AddWithValue("Diff", snapshot.Diff);
		command.Parameters.AddWithValue("ProcessedMarketCount", snapshot.ProcessedMarketCount);
		command.Parameters.Add("HistoryFetchFailedAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(snapshot.HistoryFetchFailedAtUtc);
		command.Parameters.Add("HistoryFetchRetryAfterUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(snapshot.HistoryFetchRetryAfterUtc);
		command.Parameters.AddWithValue("HistoryFetchError", string.IsNullOrWhiteSpace(snapshot.HistoryFetchError) ? DBNull.Value : snapshot.HistoryFetchError);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(snapshot.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(snapshot.UpdatedAtUtc);
	}

	private static CryptoUpDown5mDiffSnapshot ReadCryptoUpDown5mDiffSnapshot(NpgsqlDataReader reader)
	{
		return new CryptoUpDown5mDiffSnapshot(
			reader.GetGuid(0),
			reader.GetString(1),
			DateTimeOffsetFromUtc(reader.GetDateTime(2)),
			DateTimeOffsetFromUtc(reader.GetDateTime(3)),
			reader.IsDBNull(4) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(4)),
			reader.IsDBNull(5) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			reader.IsDBNull(6) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			reader.GetBoolean(7),
			reader.GetInt32(8),
			reader.GetInt32(9),
			reader.GetInt32(10),
			reader.GetInt32(11),
			reader.GetInt32(12),
			reader.IsDBNull(13) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(13)),
			reader.IsDBNull(14) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(14)),
			reader.IsDBNull(15) ? null : reader.GetString(15),
			DateTimeOffsetFromUtc(reader.GetDateTime(16)),
			DateTimeOffsetFromUtc(reader.GetDateTime(17)));
	}

	private static void AddCryptoUpDown5mDiffShiftProgressStateParameters(NpgsqlCommand command, CryptoUpDown5mDiffShiftProgressState state)
	{
		command.Parameters.AddWithValue("StrategyId", StrategyIds.Normalize(state.StrategyId));
		command.Parameters.AddWithValue("AssetSymbol", state.AssetSymbol.Trim().ToUpperInvariant());
		command.Parameters.AddWithValue("TriggerOutcome", NormalizeBtcOutcome(state.TriggerOutcome) ?? state.TriggerOutcome.Trim());
		command.Parameters.AddWithValue("UpCount", state.UpCount);
		command.Parameters.AddWithValue("DownCount", state.DownCount);
		command.Parameters.AddWithValue("SumAmount", state.SumAmount);
		command.Parameters.AddWithValue("DampingActive", state.DampingActive);
		command.Parameters.AddWithValue("DampingDirection", string.IsNullOrWhiteSpace(state.DampingDirection)
			? DBNull.Value
			: NormalizeBtcOutcome(state.DampingDirection) ?? state.DampingDirection.Trim());
		command.Parameters.Add("LastProcessedMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(state.LastProcessedMarketStartUtc);
		command.Parameters.Add("PendingMarketStartUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(state.PendingMarketStartUtc);
		command.Parameters.AddWithValue("PendingTargetOutcome", string.IsNullOrWhiteSpace(state.PendingTargetOutcome)
			? DBNull.Value
			: NormalizeBtcOutcome(state.PendingTargetOutcome) ?? state.PendingTargetOutcome.Trim());
		command.Parameters.AddWithValue("PendingStakeUsd", state.PendingStakeUsd.HasValue ? state.PendingStakeUsd.Value : DBNull.Value);
		command.Parameters.Add("PendingCreatedAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(state.PendingCreatedAtUtc);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(state.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(state.UpdatedAtUtc);
	}

	private static CryptoUpDown5mDiffShiftProgressState ReadCryptoUpDown5mDiffShiftProgressState(NpgsqlDataReader reader)
	{
		return new CryptoUpDown5mDiffShiftProgressState(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetInt32(3),
			reader.GetInt32(4),
			reader.GetDecimal(5),
			reader.GetBoolean(6),
			reader.IsDBNull(7) ? null : reader.GetString(7),
			reader.IsDBNull(8) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			reader.IsDBNull(9) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(9)),
			reader.IsDBNull(10) ? null : reader.GetString(10),
			reader.IsDBNull(11) ? null : reader.GetDecimal(11),
			reader.IsDBNull(12) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(12)),
			DateTimeOffsetFromUtc(reader.GetDateTime(13)),
			DateTimeOffsetFromUtc(reader.GetDateTime(14)));
	}

	private static void AddCryptoUpDown5mResultPollingObservationParameters(NpgsqlCommand command, CryptoUpDown5mResultPollingObservation observation)
	{
		command.Parameters.AddWithValue("Id", observation.Id);
		command.Parameters.AddWithValue("AssetSymbol", observation.AssetSymbol.Trim().ToUpperInvariant());
		command.Parameters.AddWithValue("MarketId", observation.MarketId);
		command.Parameters.AddWithValue("ConditionId", observation.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", observation.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.MarketEndUtc);
		command.Parameters.Add("FirstObservedEndedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.FirstObservedEndedAtUtc);
		command.Parameters.Add("PollingStartedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.PollingStartedAtUtc);
		command.Parameters.Add("LastPollAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(observation.LastPollAtUtc);
		command.Parameters.AddWithValue("PollAttempts", observation.PollAttempts);
		command.Parameters.Add("FirstClosedAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(observation.FirstClosedAtUtc);
		command.Parameters.Add("FirstWinnerAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(observation.FirstWinnerAtUtc);
		command.Parameters.AddWithValue("WinningOutcome", string.IsNullOrWhiteSpace(observation.WinningOutcome) ? DBNull.Value : observation.WinningOutcome);
		command.Parameters.AddWithValue("ClosedDelaySeconds", observation.ClosedDelaySeconds.HasValue ? observation.ClosedDelaySeconds.Value : DBNull.Value);
		command.Parameters.AddWithValue("ResultDelaySeconds", observation.ResultDelaySeconds.HasValue ? observation.ResultDelaySeconds.Value : DBNull.Value);
		command.Parameters.AddWithValue("Status", observation.Status);
		command.Parameters.AddWithValue("LastResponseStatus", observation.LastResponseStatus);
		command.Parameters.AddWithValue("LastError", string.IsNullOrWhiteSpace(observation.LastError) ? DBNull.Value : observation.LastError);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(observation.UpdatedAtUtc);
	}

	private static CryptoUpDown5mResultPollingObservation ReadCryptoUpDown5mResultPollingObservation(NpgsqlDataReader reader)
	{
		return new CryptoUpDown5mResultPollingObservation(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			DateTimeOffsetFromUtc(reader.GetDateTime(7)),
			DateTimeOffsetFromUtc(reader.GetDateTime(8)),
			reader.IsDBNull(9) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(9)),
			reader.GetInt32(10),
			reader.IsDBNull(11) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(11)),
			reader.IsDBNull(12) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(12)),
			reader.IsDBNull(13) ? null : reader.GetString(13),
			reader.IsDBNull(14) ? null : reader.GetDecimal(14),
			reader.IsDBNull(15) ? null : reader.GetDecimal(15),
			reader.GetString(16),
			reader.GetString(17),
			reader.IsDBNull(18) ? null : reader.GetString(18),
			DateTimeOffsetFromUtc(reader.GetDateTime(19)),
			DateTimeOffsetFromUtc(reader.GetDateTime(20)));
	}

	private static void AddCryptoUpDown5mWebSocketResolvedMarketParameters(NpgsqlCommand command, CryptoUpDown5mWebSocketResolvedMarket resolvedMarket)
	{
		command.Parameters.AddWithValue("Id", resolvedMarket.Id);
		command.Parameters.AddWithValue("AssetSymbol", resolvedMarket.AssetSymbol.Trim().ToUpperInvariant());
		command.Parameters.AddWithValue("MarketId", resolvedMarket.MarketId);
		command.Parameters.AddWithValue("ConditionId", resolvedMarket.ConditionId);
		command.Parameters.AddWithValue("MarketSlug", resolvedMarket.MarketSlug);
		command.Parameters.Add("MarketStartUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.MarketStartUtc);
		command.Parameters.Add("MarketEndUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.MarketEndUtc);
		command.Parameters.AddWithValue("WinningOutcome", resolvedMarket.WinningOutcome);
		command.Parameters.AddWithValue("WinningAssetId", string.IsNullOrWhiteSpace(resolvedMarket.WinningAssetId) ? DBNull.Value : resolvedMarket.WinningAssetId);
		command.Parameters.Add("EventTimestampUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.EventTimestampUtc);
		command.Parameters.Add("FirstReceivedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.FirstReceivedAtUtc);
		command.Parameters.Add("LastReceivedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.LastReceivedAtUtc);
		command.Parameters.AddWithValue("EventCount", Math.Max(1, resolvedMarket.EventCount));
		command.Parameters.AddWithValue("ResultDelaySeconds", resolvedMarket.ResultDelaySeconds);
		command.Parameters.AddWithValue("Source", resolvedMarket.Source);
		command.Parameters.AddWithValue("RawEventType", resolvedMarket.RawEventType);
		command.Parameters.AddWithValue("RawJson", "{}");
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.CreatedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(resolvedMarket.UpdatedAtUtc);
	}

	private static CryptoUpDown5mWebSocketResolvedMarket ReadCryptoUpDown5mWebSocketResolvedMarket(NpgsqlDataReader reader)
	{
		return new CryptoUpDown5mWebSocketResolvedMarket(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			DateTimeOffsetFromUtc(reader.GetDateTime(5)),
			DateTimeOffsetFromUtc(reader.GetDateTime(6)),
			reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetString(8),
			DateTimeOffsetFromUtc(reader.GetDateTime(9)),
			DateTimeOffsetFromUtc(reader.GetDateTime(10)),
			DateTimeOffsetFromUtc(reader.GetDateTime(11)),
			reader.GetInt32(12),
			reader.GetDecimal(13),
			reader.GetString(14),
			reader.GetString(15),
			reader.GetString(16),
			DateTimeOffsetFromUtc(reader.GetDateTime(17)),
			DateTimeOffsetFromUtc(reader.GetDateTime(18)));
	}

	private static void AddMarketResolvedEventDiagnosticParameters(NpgsqlCommand command, MarketResolvedEventDiagnostic diagnostic)
	{
		command.Parameters.AddWithValue("Id", diagnostic.Id);
		command.Parameters.AddWithValue("Component", diagnostic.Component);
		command.Parameters.AddWithValue("RawEventType", diagnostic.RawEventType);
		command.Parameters.AddWithValue("AssetId", string.IsNullOrWhiteSpace(diagnostic.AssetId) ? DBNull.Value : diagnostic.AssetId);
		command.Parameters.AddWithValue("ConditionId", string.IsNullOrWhiteSpace(diagnostic.ConditionId) ? DBNull.Value : diagnostic.ConditionId);
		command.Parameters.AddWithValue("WinningAssetId", string.IsNullOrWhiteSpace(diagnostic.WinningAssetId) ? DBNull.Value : diagnostic.WinningAssetId);
		command.Parameters.AddWithValue("WinningOutcome", string.IsNullOrWhiteSpace(diagnostic.WinningOutcome) ? DBNull.Value : diagnostic.WinningOutcome);
		command.Parameters.Add("EventTimestampUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.EventTimestampUtc);
		command.Parameters.Add("ReceivedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.ReceivedAtUtc);
		command.Parameters.AddWithValue("ActiveSnapshotFound", diagnostic.ActiveSnapshotFound);
		command.Parameters.AddWithValue("SnapshotMarketId", string.IsNullOrWhiteSpace(diagnostic.SnapshotMarketId) ? DBNull.Value : diagnostic.SnapshotMarketId);
		command.Parameters.AddWithValue("SnapshotConditionId", string.IsNullOrWhiteSpace(diagnostic.SnapshotConditionId) ? DBNull.Value : diagnostic.SnapshotConditionId);
		command.Parameters.AddWithValue("SnapshotMarketSlug", string.IsNullOrWhiteSpace(diagnostic.SnapshotMarketSlug) ? DBNull.Value : diagnostic.SnapshotMarketSlug);
		command.Parameters.AddWithValue("SnapshotAssetSymbol", string.IsNullOrWhiteSpace(diagnostic.SnapshotAssetSymbol) ? DBNull.Value : diagnostic.SnapshotAssetSymbol);
		command.Parameters.Add("SnapshotMarketStartUtc", NpgsqlDbType.TimestampTz).Value = diagnostic.SnapshotMarketStartUtc is { } snapshotMarketStartUtc
			? UtcDateTime(snapshotMarketStartUtc)
			: DBNull.Value;
		command.Parameters.AddWithValue("SnapshotIsCryptoUpDown5m", diagnostic.SnapshotIsCryptoUpDown5m);
		command.Parameters.AddWithValue("RecorderAction", diagnostic.RecorderAction);
		command.Parameters.AddWithValue("RawJson", string.IsNullOrWhiteSpace(diagnostic.RawJson) ? "{}" : diagnostic.RawJson);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.CreatedAtUtc);
	}

	private static void AddMarketWebSocketFrameDiagnosticParameters(NpgsqlCommand command, MarketWebSocketFrameDiagnostic diagnostic)
	{
		command.Parameters.AddWithValue("Id", diagnostic.Id);
		command.Parameters.AddWithValue("Component", diagnostic.Component);
		command.Parameters.Add("ReceivedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.ReceivedAtUtc);
		command.Parameters.AddWithValue("FrameKind", diagnostic.FrameKind);
		command.Parameters.AddWithValue("PayloadLengthChars", diagnostic.PayloadLengthChars);
		command.Parameters.AddWithValue("PayloadSha256", diagnostic.PayloadSha256);
		command.Parameters.AddWithValue("EventCount", diagnostic.EventCount);
		command.Parameters.AddWithValue("EventTypesJson", string.IsNullOrWhiteSpace(diagnostic.EventTypesJson) ? "[]" : diagnostic.EventTypesJson);
		command.Parameters.AddWithValue("AssetIdsJson", string.IsNullOrWhiteSpace(diagnostic.AssetIdsJson) ? "[]" : diagnostic.AssetIdsJson);
		command.Parameters.AddWithValue("MarketIdsJson", string.IsNullOrWhiteSpace(diagnostic.MarketIdsJson) ? "[]" : diagnostic.MarketIdsJson);
		command.Parameters.AddWithValue("ContainsMarketResolvedText", diagnostic.ContainsMarketResolvedText);
		command.Parameters.AddWithValue("ContainsResolvedText", diagnostic.ContainsResolvedText);
		command.Parameters.AddWithValue("ParseSucceeded", diagnostic.ParseSucceeded);
		command.Parameters.AddWithValue("ParsedUpdateCount", Math.Max(0, diagnostic.ParsedUpdateCount));
		command.Parameters.AddWithValue("ParseError", string.IsNullOrWhiteSpace(diagnostic.ParseError) ? DBNull.Value : diagnostic.ParseError);
		command.Parameters.AddWithValue("RawPayload", diagnostic.RawPayload);
		command.Parameters.AddWithValue("RawPayloadTruncated", diagnostic.RawPayloadTruncated);
		command.Parameters.Add("CreatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(diagnostic.CreatedAtUtc);
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

	private static void AddPolymarketAutoRedeemAttemptParameters(NpgsqlCommand command, PolymarketAutoRedeemAttempt attempt)
	{
		command.Parameters.AddWithValue("Id", attempt.Id);
		command.Parameters.AddWithValue("Wallet", attempt.Wallet);
		command.Parameters.AddWithValue("ProxyWallet", (object?)attempt.ProxyWallet ?? DBNull.Value);
		command.Parameters.AddWithValue("ConditionId", attempt.ConditionId);
		command.Parameters.AddWithValue("AssetId", attempt.AssetId);
		command.Parameters.AddWithValue("MarketSlug", attempt.MarketSlug);
		command.Parameters.AddWithValue("MarketTitle", attempt.MarketTitle);
		command.Parameters.AddWithValue("Outcome", attempt.Outcome);
		command.Parameters.Add("OutcomeIndex", NpgsqlDbType.Integer).Value = attempt.OutcomeIndex.HasValue ? attempt.OutcomeIndex.Value : (object)DBNull.Value;
		command.Parameters.Add("RedeemableValueUsd", NpgsqlDbType.Numeric).Value = attempt.RedeemableValueUsd.HasValue ? attempt.RedeemableValueUsd.Value : (object)DBNull.Value;
		command.Parameters.Add("Size", NpgsqlDbType.Numeric).Value = attempt.Size.HasValue ? attempt.Size.Value : (object)DBNull.Value;
		command.Parameters.AddWithValue("Status", attempt.Status);
		command.Parameters.AddWithValue("DryRun", attempt.DryRun);
		command.Parameters.AddWithValue("AutoSubmitEnabled", attempt.AutoSubmitEnabled);
		command.Parameters.AddWithValue("TargetContract", attempt.TargetContract);
		command.Parameters.AddWithValue("Calldata", attempt.Calldata);
		command.Parameters.AddWithValue("CollateralToken", attempt.CollateralToken);
		command.Parameters.AddWithValue("ParentCollectionId", attempt.ParentCollectionId);
		command.Parameters.AddWithValue("IndexSetsJson", JsonSerializer.Serialize(attempt.IndexSets));
		command.Parameters.AddWithValue("RelayerTransactionId", (object?)attempt.RelayerTransactionId ?? DBNull.Value);
		command.Parameters.AddWithValue("TransactionHash", (object?)attempt.TransactionHash ?? DBNull.Value);
		command.Parameters.AddWithValue("LastError", (object?)attempt.LastError ?? DBNull.Value);
		command.Parameters.Add("DetectedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(attempt.DetectedAtUtc);
		command.Parameters.Add("LastSeenAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(attempt.LastSeenAtUtc);
		command.Parameters.Add("SubmittedAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(attempt.SubmittedAtUtc);
		command.Parameters.Add("ConfirmedAtUtc", NpgsqlDbType.TimestampTz).Value = NullableDateTime(attempt.ConfirmedAtUtc);
		command.Parameters.Add("UpdatedAtUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(attempt.UpdatedAtUtc);
		command.Parameters.AddWithValue("RawPositionJson", string.IsNullOrWhiteSpace(attempt.RawPositionJson) ? "{}" : attempt.RawPositionJson);
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

	private static PolymarketAutoRedeemAttempt ReadPolymarketAutoRedeemAttempt(NpgsqlDataReader reader)
	{
		int[]? indexSets = null;
		if (!reader.IsDBNull(18))
		{
			try
			{
				indexSets = JsonSerializer.Deserialize<int[]>(reader.GetString(18));
			}
			catch (JsonException)
			{
				indexSets = [];
			}
		}

		return new PolymarketAutoRedeemAttempt(
			reader.GetGuid(0),
			reader.GetString(1),
			reader.IsDBNull(2) ? null : reader.GetString(2),
			reader.GetString(3),
			reader.GetString(4),
			reader.GetString(5),
			reader.GetString(6),
			reader.GetString(7),
			reader.IsDBNull(8) ? null : reader.GetInt32(8),
			reader.IsDBNull(9) ? null : reader.GetDecimal(9),
			reader.IsDBNull(10) ? null : reader.GetDecimal(10),
			reader.GetString(11),
			reader.GetBoolean(12),
			reader.GetBoolean(13),
			reader.GetString(14),
			reader.GetString(15),
			reader.GetString(16),
			reader.GetString(17),
			indexSets ?? [],
			reader.IsDBNull(19) ? null : reader.GetString(19),
			reader.IsDBNull(20) ? null : reader.GetString(20),
			reader.IsDBNull(21) ? null : reader.GetString(21),
			DateTimeOffsetFromUtc(reader.GetDateTime(22)),
			DateTimeOffsetFromUtc(reader.GetDateTime(23)),
			reader.IsDBNull(24) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(24)),
			reader.IsDBNull(25) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(25)),
			DateTimeOffsetFromUtc(reader.GetDateTime(26)),
			reader.GetString(27));
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
				reader.IsDBNull(38) ? null : reader.GetGuid(38),
				reader.GetString(39),
				reader.GetString(40),
				reader.GetString(41),
				reader.IsDBNull(42) ? null : reader.GetDecimal(42),
				reader.IsDBNull(43) ? null : reader.GetInt32(43),
				reader.IsDBNull(44) ? null : reader.GetBoolean(44),
				reader.IsDBNull(45) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(45)),
				reader.IsDBNull(46) ? null : reader.GetDecimal(46),
				Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(47), ignoreCase: false),
				reader.GetInt64(48)));
		}
		return results;
	}

	private static IReadOnlyList<string> SplitReasonCodes(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? ((IReadOnlyList<string>)Array.Empty<string>()) : ((IReadOnlyList<string>)value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
	}
}
