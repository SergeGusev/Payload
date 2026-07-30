using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    internal async Task<ProjectionBuildResult> BuildProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? strategyId,
        DateTimeOffset nowUtc,
        Func<DashboardRecentProjectionFact, CancellationToken, ValueTask> factSink,
        Func<PaperPositionProjectionPayload, CancellationToken, ValueTask>? positionFactSink,
        bool includePaperPositions,
        CancellationToken cancellationToken)
    {
        var descriptors = (await ReadStrategyDescriptorsAsync(
            connection,
            transaction,
            strategyId,
            cancellationToken)).ToDictionary(strategy => strategy.StrategyId);
        var lifetimeStates = descriptors.Keys.ToDictionary(
            id => id,
            _ => new DashboardLifetimeProjectionState());
        var recentStates = new Dictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState>();
        foreach (var id in descriptors.Keys)
        {
            foreach (var windowHours in WindowHours)
            {
                recentStates[(id, windowHours)] = new DashboardRecentProjectionState();
            }
        }

        var recentFactCount = 0;
        await AccumulatePaperOrdersAsync();
        await AccumulatePaperFillsAsync();
        if (includePaperPositions)
        {
            await AccumulatePaperPositionsAsync(
                connection,
                transaction,
                strategyId,
                descriptors,
                lifetimeStates,
                positionFactSink,
                cancellationToken);
        }

        await AccumulatePaperSettlementsAsync();
        await AccumulateStrategyRunsAsync();
        await AccumulateStrategyPaperSkipRollupsAsync();
        await AccumulateLiveOrdersAsync();

        return new ProjectionBuildResult(descriptors, lifetimeStates, recentStates, recentFactCount);

        async ValueTask AddFactsAsync(IReadOnlyList<DashboardRecentProjectionFact> facts)
        {
            foreach (var sourceFact in facts)
            {
                if (sourceFact.OccurredAtUtc < nowUtc.AddHours(-24))
                {
                    continue;
                }

                var fact = PrepareFact(sourceFact, nowUtc);
                ApplyFact(recentStates, fact, 1);
                await factSink(fact, cancellationToken);
                recentFactCount++;
            }
        }

        async Task AccumulatePaperOrdersAsync()
        {
            var filter = strategyId is null ? string.Empty : "WHERE paper_order.strategy_id = @StrategyId";
            await using var command = CreateSourceCommand(
                $$"""
SELECT paper_order.id,
       paper_order.strategy_id,
       paper_order.status,
       paper_order.side,
       paper_order.notional_usd,
       paper_order.created_at_utc,
       CASE
           WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score_bps') = 'number'
           THEN round((paper_order.raw_decision_json ->> 'previous_score_bps')::numeric, 8)
           ELSE NULL
       END AS previous_score_bps,
       CASE
           WHEN jsonb_typeof(paper_order.raw_decision_json -> 'previous_score') = 'number'
           THEN round((paper_order.raw_decision_json ->> 'previous_score')::numeric, 12)
           ELSE NULL
       END AS previous_score,
       CASE
           WHEN jsonb_typeof(paper_order.raw_decision_json -> 'selected_signal_bps') = 'number'
           THEN round((paper_order.raw_decision_json ->> 'selected_signal_bps')::numeric, 8)
           ELSE NULL
       END AS selected_signal_bps
FROM paper_orders paper_order
{{filter}}
ORDER BY paper_order.strategy_id, paper_order.created_at_utc, paper_order.id;
""",
                connection,
                transaction,
                strategyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new PaperOrderProjectionPayload(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDecimal(4),
                    UtcNow(reader.GetDateTime(5)),
                    reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    reader.IsDBNull(8) ? null : reader.GetDecimal(8));
                if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    continue;
                }

                DashboardProjectionCalculator.Apply(
                    state,
                    DashboardProjectionCalculator.GetLifetimeContribution(payload),
                    1);
                await AddFactsAsync(DashboardProjectionCalculator.GetRecentFacts(payload));
            }
        }

        async Task AccumulatePaperFillsAsync()
        {
            var filter = strategyId is null ? string.Empty : "WHERE paper_order.strategy_id = @StrategyId";
            await using var command = CreateSourceCommand(
                $$"""
SELECT fill_row.id,
       paper_order.strategy_id,
       paper_order.side,
       fill_row.price,
       fill_row.size_shares,
       fill_row.realized_pnl_usd,
       fill_row.filled_at_utc
FROM paper_fills fill_row
INNER JOIN paper_orders paper_order ON paper_order.id = fill_row.paper_order_id
{{filter}}
ORDER BY paper_order.strategy_id, fill_row.filled_at_utc, fill_row.id;
""",
                connection,
                transaction,
                strategyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new PaperFillProjectionPayload(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    UtcNow(reader.GetDateTime(6)));
                if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    continue;
                }

                DashboardProjectionCalculator.Apply(
                    state,
                    DashboardProjectionCalculator.GetLifetimeContribution(payload),
                    1);
                await AddFactsAsync(DashboardProjectionCalculator.GetRecentFacts(payload));
            }
        }

        async Task AccumulatePaperSettlementsAsync()
        {
            await using var command = strategyId is null
                ? CreateSourceCommand(
                    """
WITH mapped AS (
    SELECT settlement.id,
           CASE
               WHEN lower(settlement.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE follow_leader.id
           END AS strategy_id,
           settlement.cost_basis_usd,
           settlement.realized_pnl_usd,
           settlement.won
    FROM paper_position_settlements settlement
    LEFT JOIN strategies strategy_by_wallet
        ON lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy_by_wallet.code)
    LEFT JOIN strategies follow_leader
        ON follow_leader.id = @FollowLeaderStrategyId
)
SELECT mapped.id, mapped.strategy_id, mapped.cost_basis_usd, mapped.realized_pnl_usd, mapped.won
FROM mapped
WHERE mapped.strategy_id IS NOT NULL
ORDER BY mapped.strategy_id, mapped.id;
""",
                    connection,
                    transaction,
                    strategyId,
                    includeFollowLeader: true)
                : CreateStrategyWalletSourceCommand(
                    "paper_position_settlements",
                    descriptors[strategyId.Value],
                    connection,
                    transaction);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new PaperSettlementProjectionPayload(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetBoolean(4));
                if (lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    DashboardProjectionCalculator.Apply(
                        state,
                        DashboardProjectionCalculator.GetLifetimeContribution(payload),
                        1);
                }
            }
        }

        async Task AccumulateStrategyRunsAsync()
        {
            var filter = strategyId is null ? string.Empty : "WHERE run.strategy_id = @StrategyId";
            await using var command = CreateSourceCommand(
                $$"""
SELECT run.id,
       run.strategy_id,
       run.status,
       run.stake_usd,
       run.paper_order_id,
       run.entry_due_at_utc,
       run.entered_at_utc,
       run.realized_pnl_usd,
       run.settled_at_utc,
       run.skip_reason,
       run.updated_at_utc,
       strategy.live_enabled_at_utc
FROM strategy_market_paper_runs run
INNER JOIN strategies strategy ON strategy.id = run.strategy_id
{{filter}}
ORDER BY run.strategy_id, run.updated_at_utc, run.id;
""",
                connection,
                transaction,
                strategyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new StrategyRunProjectionPayload(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    UtcNow(reader.GetDateTime(5)),
                    ReadNullableUtc(reader, 6),
                    reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                    ReadNullableUtc(reader, 8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    UtcNow(reader.GetDateTime(10)),
                    ReadNullableUtc(reader, 11));
                if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    continue;
                }

                DashboardProjectionCalculator.Apply(
                    state,
                    DashboardProjectionCalculator.GetLifetimeContribution(payload),
                    1);
                await AddFactsAsync(DashboardProjectionCalculator.GetRecentFacts(payload));
            }
        }

        async Task AccumulateStrategyPaperSkipRollupsAsync()
        {
            var filter = strategyId is null ? string.Empty : "WHERE rollup.strategy_id = @StrategyId";
            await using var command = CreateSourceCommand(
                $$"""
SELECT rollup.strategy_id,
       sum(rollup.run_count)::bigint AS run_count,
       max(rollup.last_updated_at_utc) AS last_run_utc
FROM strategy_paper_skip_rollups rollup
{{filter}}
GROUP BY rollup.strategy_id
ORDER BY rollup.strategy_id;
""",
                connection,
                transaction,
                strategyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new StrategyPaperSkipRollupProjectionPayload(
                    reader.GetGuid(0),
                    checked((int)reader.GetInt64(1)),
                    UtcNow(reader.GetDateTime(2)));
                if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    continue;
                }

                DashboardProjectionCalculator.Apply(
                    state,
                    DashboardProjectionCalculator.GetLifetimeContribution(payload),
                    1);
            }
        }

        async Task AccumulateLiveOrdersAsync()
        {
            var filter = strategyId is null ? string.Empty : "WHERE live_order.strategy_id = @StrategyId";
            await using var command = CreateSourceCommand(
                $$"""
SELECT live_order.id,
       live_order.strategy_id,
       live_order.status,
       live_order.price,
       live_order.filled_size,
       live_order.remaining_size,
       live_order.filled_notional_usd,
       live_order.cost_basis_usd,
       live_order.fee_usd,
       live_order.settlement_value_usd,
       live_order.realized_pnl_usd,
       live_order.settled_at_utc,
       live_order.won,
       live_order.created_at_utc,
       live_order.updated_at_utc
FROM live_orders live_order
{{filter}}
ORDER BY live_order.strategy_id, live_order.updated_at_utc, live_order.id;
""",
                connection,
                transaction,
                strategyId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var payload = new LiveOrderProjectionPayload(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                    ReadNullableUtc(reader, 11),
                    reader.IsDBNull(12) ? null : reader.GetBoolean(12),
                    UtcNow(reader.GetDateTime(13)),
                    UtcNow(reader.GetDateTime(14)));
                if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
                {
                    continue;
                }

                DashboardProjectionCalculator.Apply(
                    state,
                    DashboardProjectionCalculator.GetLifetimeContribution(payload),
                    1);
                await AddFactsAsync(DashboardProjectionCalculator.GetRecentFacts(payload));
            }
        }
    }

    internal static async Task AccumulatePaperPositionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? strategyId,
        IReadOnlyDictionary<Guid, DashboardStrategyDescriptor> descriptors,
        IReadOnlyDictionary<Guid, DashboardLifetimeProjectionState> lifetimeStates,
        Func<PaperPositionProjectionPayload, CancellationToken, ValueTask>? factSink,
        CancellationToken cancellationToken)
    {
        await using var command = strategyId is null
            ? CreateSourceCommand(
                """
WITH mapped AS (
    SELECT position_row.id,
           CASE
               WHEN lower(position_row.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE follow_leader.id
           END AS strategy_id,
           position_row.size_shares,
           position_row.unrealized_pnl_usd
    FROM paper_positions position_row
    LEFT JOIN strategies strategy_by_wallet
        ON strategy_by_wallet.code = lower(substring(position_row.copied_trader_wallet from 10))
       AND lower(position_row.copied_trader_wallet) LIKE 'strategy:%'
    LEFT JOIN strategies follow_leader
        ON follow_leader.id = @FollowLeaderStrategyId
)
SELECT mapped.id, mapped.strategy_id, mapped.size_shares, mapped.unrealized_pnl_usd
FROM mapped
WHERE mapped.strategy_id IS NOT NULL;
""",
                connection,
                transaction,
                strategyId,
                includeFollowLeader: true)
            : CreateStrategyWalletSourceCommand(
                "paper_positions",
                descriptors[strategyId.Value],
                connection,
                transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = new PaperPositionProjectionPayload(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3));
            if (!lifetimeStates.TryGetValue(payload.StrategyId, out var state))
            {
                continue;
            }

            DashboardProjectionCalculator.Apply(
                state,
                DashboardProjectionCalculator.GetLifetimeContribution(payload),
                1);
            if (factSink is not null)
            {
                await factSink(payload, cancellationToken);
            }
        }
    }

    private static NpgsqlCommand CreateSourceCommand(
        string sql,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? strategyId,
        bool includeFollowLeader = false)
    {
        var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = strategyId is null ? 0 : 15
        };
        if (strategyId is not null)
        {
            command.Parameters.AddWithValue("StrategyId", strategyId.Value);
        }

        if (includeFollowLeader)
        {
            command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        }

        return command;
    }

    private static NpgsqlCommand CreateStrategyWalletSourceCommand(
        string tableName,
        DashboardStrategyDescriptor strategy,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        var selectedColumns = tableName switch
        {
            "paper_positions" => "size_shares, unrealized_pnl_usd",
            "paper_position_settlements" => "cost_basis_usd, realized_pnl_usd, won",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName))
        };

        var walletFilter = strategy.StrategyId == StrategyIds.FollowLeader
            ? "lower(source_row.copied_trader_wallet) NOT LIKE 'strategy:%'"
            : "source_row.copied_trader_wallet = @StrategyWallet";
        var command = new NpgsqlCommand(
            $"""
SELECT source_row.id, @StrategyId, {selectedColumns}
FROM {tableName} source_row
WHERE {walletFilter}
ORDER BY source_row.id;
""",
            connection,
            transaction)
        {
            CommandTimeout = 15
        };
        command.Parameters.AddWithValue("StrategyId", strategy.StrategyId);
        if (strategy.StrategyId != StrategyIds.FollowLeader)
        {
            command.Parameters.AddWithValue(
                "StrategyWallet",
                $"strategy:{strategy.Code.ToLowerInvariant()}");
        }

        return command;
    }
}
