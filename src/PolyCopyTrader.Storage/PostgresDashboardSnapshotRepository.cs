using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class PostgresDashboardSnapshotRepository(PostgresConnectionFactory connectionFactory) : IDashboardSnapshotRepository
{
    private const string SelectStrategyPerformanceSnapshotSql = """
SELECT
    strategy_id,
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
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
    total_pnl_usd,
    win_rate_pct,
    loss_rate_pct,
    avg_win_pnl_usd,
    avg_loss_pnl_usd,
    profit_factor,
    expectancy_pnl_usd,
    roi_pct,
    closed_roi_pct,
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
    live_win_rate_pct,
    live_loss_rate_pct,
    live_avg_win_pnl_usd,
    live_avg_loss_pnl_usd,
    live_profit_factor,
    live_expectancy_pnl_usd,
    live_roi_pct,
    live_last_order_utc,
    live_last_settlement_utc,
    last_order_utc,
    last_run_utc
FROM dashboard_strategy_performance_snapshots
ORDER BY
    CASE WHEN code = 'follow_leader' THEN 0 ELSE 1 END,
    code
LIMIT @Limit;
""";

    private const string UpsertStrategyPerformanceSnapshotSql = """
INSERT INTO dashboard_strategy_performance_snapshots (
    strategy_id,
    code,
    name,
    enabled,
    live_stakes,
    auto_live_paused,
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
    total_pnl_usd,
    win_rate_pct,
    loss_rate_pct,
    avg_win_pnl_usd,
    avg_loss_pnl_usd,
    profit_factor,
    expectancy_pnl_usd,
    roi_pct,
    closed_roi_pct,
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
    live_win_rate_pct,
    live_loss_rate_pct,
    live_avg_win_pnl_usd,
    live_avg_loss_pnl_usd,
    live_profit_factor,
    live_expectancy_pnl_usd,
    live_roi_pct,
    live_last_order_utc,
    live_last_settlement_utc,
    last_order_utc,
    last_run_utc,
    refreshed_at_utc)
VALUES (
    @StrategyId,
    @Code,
    @Name,
    @Enabled,
    @LiveStakes,
    @AutoLivePaused,
    @Paused,
    @PausedUntilUtc,
    @PaperStakeAmount,
    @LiveStakeAmount,
    @PaperLostCoeff,
    @LiveLostCoeff,
    @PaperLostCounter,
    @LiveLostCounter,
    @LiveAvailableBalance,
    @OrdersCount,
    @FilledOrdersCount,
    @OpenOrdersCount,
    @OpenPositionsCount,
    @ObservedRunsCount,
    @EnteredRunsCount,
    @SkippedRunsCount,
    @PaperConditionSkippedRunsCount,
    @PaperNotAcceptedRunsCount,
    @SettledRunsCount,
    @SettledPositionsCount,
    @WonPositionsCount,
    @LostPositionsCount,
    @StakeUsd,
    @RealizedPnlUsd,
    @UnrealizedPnlUsd,
    @TotalPnlUsd,
    @WinRatePct,
    @LossRatePct,
    @AvgWinPnlUsd,
    @AvgLossPnlUsd,
    @ProfitFactor,
    @ExpectancyPnlUsd,
    @RoiPct,
    @ClosedRoiPct,
    @AvgEntryDelaySeconds,
    @MaxEntryDelaySeconds,
    @AvgCountertrendScoreBps,
    @AvgCountertrendSignalBps,
    @LastCountertrendSignalBps,
    @LiveOrdersCount,
    @LiveFilledOrdersCount,
    @LiveOpenOrdersCount,
    @LiveSettledOrdersCount,
    @LiveSkippedOrdersCount,
    @LiveConditionSkippedOrdersCount,
    @LiveTechnicalSkippedOrdersCount,
    @LiveIgnoredOrdersCount,
    @LiveIgnoredGtdUnfilledCount,
    @LiveIgnoredCancelledOrdersCount,
    @LiveIgnoredRejectedOrdersCount,
    @LiveWonOrdersCount,
    @LiveLostOrdersCount,
    @LiveStakeUsd,
    @LiveRealizedPnlUsd,
    @LiveWinRatePct,
    @LiveLossRatePct,
    @LiveAvgWinPnlUsd,
    @LiveAvgLossPnlUsd,
    @LiveProfitFactor,
    @LiveExpectancyPnlUsd,
    @LiveRoiPct,
    @LiveLastOrderUtc,
    @LiveLastSettlementUtc,
    @LastOrderUtc,
    @LastRunUtc,
    @RefreshedAtUtc)
ON CONFLICT (strategy_id) DO UPDATE SET
    code = EXCLUDED.code,
    name = EXCLUDED.name,
    enabled = EXCLUDED.enabled,
    live_stakes = EXCLUDED.live_stakes,
    auto_live_paused = EXCLUDED.auto_live_paused,
    paused = EXCLUDED.paused,
    paused_until_utc = EXCLUDED.paused_until_utc,
    paper_stake_amount = EXCLUDED.paper_stake_amount,
    live_stake_amount = EXCLUDED.live_stake_amount,
    paper_lost_coeff = EXCLUDED.paper_lost_coeff,
    live_lost_coeff = EXCLUDED.live_lost_coeff,
    paper_lost_counter = EXCLUDED.paper_lost_counter,
    live_lost_counter = EXCLUDED.live_lost_counter,
    live_available_balance = EXCLUDED.live_available_balance,
    orders_count = EXCLUDED.orders_count,
    filled_orders_count = EXCLUDED.filled_orders_count,
    open_orders_count = EXCLUDED.open_orders_count,
    open_positions_count = EXCLUDED.open_positions_count,
    observed_runs_count = EXCLUDED.observed_runs_count,
    entered_runs_count = EXCLUDED.entered_runs_count,
    skipped_runs_count = EXCLUDED.skipped_runs_count,
    paper_condition_skipped_runs_count = EXCLUDED.paper_condition_skipped_runs_count,
    paper_not_accepted_runs_count = EXCLUDED.paper_not_accepted_runs_count,
    settled_runs_count = EXCLUDED.settled_runs_count,
    settled_positions_count = EXCLUDED.settled_positions_count,
    won_positions_count = EXCLUDED.won_positions_count,
    lost_positions_count = EXCLUDED.lost_positions_count,
    stake_usd = EXCLUDED.stake_usd,
    realized_pnl_usd = EXCLUDED.realized_pnl_usd,
    unrealized_pnl_usd = EXCLUDED.unrealized_pnl_usd,
    total_pnl_usd = EXCLUDED.total_pnl_usd,
    win_rate_pct = EXCLUDED.win_rate_pct,
    loss_rate_pct = EXCLUDED.loss_rate_pct,
    avg_win_pnl_usd = EXCLUDED.avg_win_pnl_usd,
    avg_loss_pnl_usd = EXCLUDED.avg_loss_pnl_usd,
    profit_factor = EXCLUDED.profit_factor,
    expectancy_pnl_usd = EXCLUDED.expectancy_pnl_usd,
    roi_pct = EXCLUDED.roi_pct,
    closed_roi_pct = EXCLUDED.closed_roi_pct,
    avg_entry_delay_seconds = EXCLUDED.avg_entry_delay_seconds,
    max_entry_delay_seconds = EXCLUDED.max_entry_delay_seconds,
    avg_countertrend_score_bps = EXCLUDED.avg_countertrend_score_bps,
    avg_countertrend_signal_bps = EXCLUDED.avg_countertrend_signal_bps,
    last_countertrend_signal_bps = EXCLUDED.last_countertrend_signal_bps,
    live_orders_count = EXCLUDED.live_orders_count,
    live_filled_orders_count = EXCLUDED.live_filled_orders_count,
    live_open_orders_count = EXCLUDED.live_open_orders_count,
    live_settled_orders_count = EXCLUDED.live_settled_orders_count,
    live_skipped_orders_count = EXCLUDED.live_skipped_orders_count,
    live_condition_skipped_orders_count = EXCLUDED.live_condition_skipped_orders_count,
    live_technical_skipped_orders_count = EXCLUDED.live_technical_skipped_orders_count,
    live_ignored_orders_count = EXCLUDED.live_ignored_orders_count,
    live_ignored_gtd_unfilled_count = EXCLUDED.live_ignored_gtd_unfilled_count,
    live_ignored_cancelled_orders_count = EXCLUDED.live_ignored_cancelled_orders_count,
    live_ignored_rejected_orders_count = EXCLUDED.live_ignored_rejected_orders_count,
    live_won_orders_count = EXCLUDED.live_won_orders_count,
    live_lost_orders_count = EXCLUDED.live_lost_orders_count,
    live_stake_usd = EXCLUDED.live_stake_usd,
    live_realized_pnl_usd = EXCLUDED.live_realized_pnl_usd,
    live_win_rate_pct = EXCLUDED.live_win_rate_pct,
    live_loss_rate_pct = EXCLUDED.live_loss_rate_pct,
    live_avg_win_pnl_usd = EXCLUDED.live_avg_win_pnl_usd,
    live_avg_loss_pnl_usd = EXCLUDED.live_avg_loss_pnl_usd,
    live_profit_factor = EXCLUDED.live_profit_factor,
    live_expectancy_pnl_usd = EXCLUDED.live_expectancy_pnl_usd,
    live_roi_pct = EXCLUDED.live_roi_pct,
    live_last_order_utc = EXCLUDED.live_last_order_utc,
    live_last_settlement_utc = EXCLUDED.live_last_settlement_utc,
    last_order_utc = EXCLUDED.last_order_utc,
    last_run_utc = EXCLUDED.last_run_utc,
    refreshed_at_utc = EXCLUDED.refreshed_at_utc;
""";

    private const string SelectStrategyRecentPerformanceSnapshotSql = """
SELECT
    strategy_id,
    code,
    name,
    live_stakes,
    window_label,
    window_hours,
    window_start_utc,
    window_end_utc,
    orders_count,
    filled_orders_count,
    expired_orders_count,
    open_orders_count,
    entered_runs_count,
    skipped_runs_count,
    paper_condition_skipped_runs_count,
    paper_not_accepted_runs_count,
    settled_runs_count,
    won_runs_count,
    lost_runs_count,
    filled_cost_usd,
    realized_pnl_usd,
    avg_fill_price,
    avg_entry_delay_seconds,
    max_entry_delay_seconds,
    win_rate_pct,
    roi_pct,
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
    live_realized_pnl_usd,
    live_roi_pct,
    top_skip_reason,
    last_order_utc,
    last_run_utc
FROM dashboard_strategy_recent_performance_snapshots
ORDER BY
    CASE WHEN code = 'follow_leader' THEN 0 ELSE 1 END,
    code,
    window_hours
LIMIT (@Limit * 3);
""";

    private const string UpsertStrategyRecentPerformanceSnapshotSql = """
INSERT INTO dashboard_strategy_recent_performance_snapshots (
    strategy_id,
    code,
    name,
    live_stakes,
    window_label,
    window_hours,
    window_start_utc,
    window_end_utc,
    orders_count,
    filled_orders_count,
    expired_orders_count,
    open_orders_count,
    entered_runs_count,
    skipped_runs_count,
    paper_condition_skipped_runs_count,
    paper_not_accepted_runs_count,
    settled_runs_count,
    won_runs_count,
    lost_runs_count,
    filled_cost_usd,
    realized_pnl_usd,
    avg_fill_price,
    avg_entry_delay_seconds,
    max_entry_delay_seconds,
    win_rate_pct,
    roi_pct,
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
    live_realized_pnl_usd,
    live_roi_pct,
    top_skip_reason,
    last_order_utc,
    last_run_utc,
    refreshed_at_utc)
VALUES (
    @StrategyId,
    @Code,
    @Name,
    @LiveStakes,
    @Window,
    @WindowHours,
    @WindowStartUtc,
    @WindowEndUtc,
    @OrdersCount,
    @FilledOrdersCount,
    @ExpiredOrdersCount,
    @OpenOrdersCount,
    @EnteredRunsCount,
    @SkippedRunsCount,
    @PaperConditionSkippedRunsCount,
    @PaperNotAcceptedRunsCount,
    @SettledRunsCount,
    @WonRunsCount,
    @LostRunsCount,
    @FilledCostUsd,
    @RealizedPnlUsd,
    @AvgFillPrice,
    @AvgEntryDelaySeconds,
    @MaxEntryDelaySeconds,
    @WinRatePct,
    @RoiPct,
    @LiveSettledOrdersCount,
    @LiveSkippedOrdersCount,
    @LiveConditionSkippedOrdersCount,
    @LiveTechnicalSkippedOrdersCount,
    @LiveIgnoredOrdersCount,
    @LiveIgnoredGtdUnfilledCount,
    @LiveIgnoredCancelledOrdersCount,
    @LiveIgnoredRejectedOrdersCount,
    @LiveWonOrdersCount,
    @LiveLostOrdersCount,
    @LiveRealizedPnlUsd,
    @LiveRoiPct,
    @TopSkipReason,
    @LastOrderUtc,
    @LastRunUtc,
    @RefreshedAtUtc)
ON CONFLICT (strategy_id, window_label) DO UPDATE SET
    code = EXCLUDED.code,
    name = EXCLUDED.name,
    live_stakes = EXCLUDED.live_stakes,
    window_hours = EXCLUDED.window_hours,
    window_start_utc = EXCLUDED.window_start_utc,
    window_end_utc = EXCLUDED.window_end_utc,
    orders_count = EXCLUDED.orders_count,
    filled_orders_count = EXCLUDED.filled_orders_count,
    expired_orders_count = EXCLUDED.expired_orders_count,
    open_orders_count = EXCLUDED.open_orders_count,
    entered_runs_count = EXCLUDED.entered_runs_count,
    skipped_runs_count = EXCLUDED.skipped_runs_count,
    paper_condition_skipped_runs_count = EXCLUDED.paper_condition_skipped_runs_count,
    paper_not_accepted_runs_count = EXCLUDED.paper_not_accepted_runs_count,
    settled_runs_count = EXCLUDED.settled_runs_count,
    won_runs_count = EXCLUDED.won_runs_count,
    lost_runs_count = EXCLUDED.lost_runs_count,
    filled_cost_usd = EXCLUDED.filled_cost_usd,
    realized_pnl_usd = EXCLUDED.realized_pnl_usd,
    avg_fill_price = EXCLUDED.avg_fill_price,
    avg_entry_delay_seconds = EXCLUDED.avg_entry_delay_seconds,
    max_entry_delay_seconds = EXCLUDED.max_entry_delay_seconds,
    win_rate_pct = EXCLUDED.win_rate_pct,
    roi_pct = EXCLUDED.roi_pct,
    live_settled_orders_count = EXCLUDED.live_settled_orders_count,
    live_skipped_orders_count = EXCLUDED.live_skipped_orders_count,
    live_condition_skipped_orders_count = EXCLUDED.live_condition_skipped_orders_count,
    live_technical_skipped_orders_count = EXCLUDED.live_technical_skipped_orders_count,
    live_ignored_orders_count = EXCLUDED.live_ignored_orders_count,
    live_ignored_gtd_unfilled_count = EXCLUDED.live_ignored_gtd_unfilled_count,
    live_ignored_cancelled_orders_count = EXCLUDED.live_ignored_cancelled_orders_count,
    live_ignored_rejected_orders_count = EXCLUDED.live_ignored_rejected_orders_count,
    live_won_orders_count = EXCLUDED.live_won_orders_count,
    live_lost_orders_count = EXCLUDED.live_lost_orders_count,
    live_realized_pnl_usd = EXCLUDED.live_realized_pnl_usd,
    live_roi_pct = EXCLUDED.live_roi_pct,
    top_skip_reason = EXCLUDED.top_skip_reason,
    last_order_utc = EXCLUDED.last_order_utc,
    last_run_utc = EXCLUDED.last_run_utc,
    refreshed_at_utc = EXCLUDED.refreshed_at_utc;
""";

    public async Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectStrategyPerformanceSnapshotSql, connection);
        command.Parameters.AddWithValue("Limit", limit);

        var results = new List<StrategyPerformance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStrategyPerformance(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceSnapshotAsync(
        int limit = 25_000,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SelectStrategyRecentPerformanceSnapshotSql, connection);
        command.Parameters.AddWithValue("Limit", limit);

        var results = new List<StrategyRecentPerformance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStrategyRecentPerformance(reader));
        }

        return results;
    }

    public async Task<int> UpsertStrategyPerformanceSnapshotAsync(
        IReadOnlyList<StrategyPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (strategies.Count == 0)
        {
            return 0;
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection)
        {
            Transaction = transaction
        };

        foreach (var strategy in strategies)
        {
            batch.BatchCommands.Add(CreateUpsertCommand(strategy, refreshedAtUtc));
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
        await using var deleteStaleCommand = new NpgsqlCommand(
            """
DELETE FROM dashboard_strategy_performance_snapshots
WHERE refreshed_at_utc < @RefreshedAtUtc;
""",
            connection,
            transaction);
        deleteStaleCommand.Parameters.AddWithValue("RefreshedAtUtc", UtcDateTime(refreshedAtUtc));
        await deleteStaleCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return strategies.Count;
    }

    public async Task<int> UpsertStrategyRecentPerformanceSnapshotAsync(
        IReadOnlyList<StrategyRecentPerformance> strategies,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (strategies.Count == 0)
        {
            return 0;
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var batch = new NpgsqlBatch(connection)
        {
            Transaction = transaction
        };

        foreach (var strategy in strategies)
        {
            batch.BatchCommands.Add(CreateUpsertCommand(strategy, refreshedAtUtc));
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
        await using var deleteStaleCommand = new NpgsqlCommand(
            """
DELETE FROM dashboard_strategy_recent_performance_snapshots
WHERE refreshed_at_utc < @RefreshedAtUtc;
""",
            connection,
            transaction);
        deleteStaleCommand.Parameters.AddWithValue("RefreshedAtUtc", UtcDateTime(refreshedAtUtc));
        await deleteStaleCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return strategies.Count;
    }

    private static NpgsqlBatchCommand CreateUpsertCommand(
        StrategyPerformance strategy,
        DateTimeOffset refreshedAtUtc)
    {
        var command = new NpgsqlBatchCommand(UpsertStrategyPerformanceSnapshotSql);
        Add(command, "StrategyId", strategy.StrategyId);
        Add(command, "Code", strategy.Code);
        Add(command, "Name", strategy.Name);
        Add(command, "Enabled", strategy.Enabled);
        Add(command, "LiveStakes", strategy.LiveStakes);
        Add(command, "AutoLivePaused", strategy.AutoLivePaused);
        Add(command, "Paused", strategy.Paused);
        Add(command, "PausedUntilUtc", NullableDateTime(strategy.PausedUntilUtc));
        Add(command, "PaperStakeAmount", strategy.PaperStakeAmount);
        Add(command, "LiveStakeAmount", strategy.LiveStakeAmount);
        Add(command, "PaperLostCoeff", strategy.PaperLostCoeff);
        Add(command, "LiveLostCoeff", strategy.LiveLostCoeff);
        Add(command, "PaperLostCounter", strategy.PaperLostCounter);
        Add(command, "LiveLostCounter", strategy.LiveLostCounter);
        Add(command, "LiveAvailableBalance", strategy.LiveAvailableBalance);
        Add(command, "OrdersCount", strategy.OrdersCount);
        Add(command, "FilledOrdersCount", strategy.FilledOrdersCount);
        Add(command, "OpenOrdersCount", strategy.OpenOrdersCount);
        Add(command, "OpenPositionsCount", strategy.OpenPositionsCount);
        Add(command, "ObservedRunsCount", strategy.ObservedRunsCount);
        Add(command, "EnteredRunsCount", strategy.EnteredRunsCount);
        Add(command, "SkippedRunsCount", strategy.SkippedRunsCount);
        Add(command, "PaperConditionSkippedRunsCount", strategy.PaperConditionSkippedRunsCount);
        Add(command, "PaperNotAcceptedRunsCount", strategy.PaperNotAcceptedRunsCount);
        Add(command, "SettledRunsCount", strategy.SettledRunsCount);
        Add(command, "SettledPositionsCount", strategy.SettledPositionsCount);
        Add(command, "WonPositionsCount", strategy.WonPositionsCount);
        Add(command, "LostPositionsCount", strategy.LostPositionsCount);
        Add(command, "StakeUsd", strategy.StakeUsd);
        Add(command, "RealizedPnlUsd", strategy.RealizedPnlUsd);
        Add(command, "UnrealizedPnlUsd", strategy.UnrealizedPnlUsd);
        Add(command, "TotalPnlUsd", strategy.TotalPnlUsd);
        Add(command, "WinRatePct", strategy.WinRatePct);
        Add(command, "LossRatePct", strategy.LossRatePct);
        Add(command, "AvgWinPnlUsd", strategy.AvgWinPnlUsd);
        Add(command, "AvgLossPnlUsd", strategy.AvgLossPnlUsd);
        Add(command, "ProfitFactor", NullableDecimal(strategy.ProfitFactor));
        Add(command, "ExpectancyPnlUsd", strategy.ExpectancyPnlUsd);
        Add(command, "RoiPct", strategy.RoiPct);
        Add(command, "ClosedRoiPct", strategy.ClosedRoiPct);
        Add(command, "AvgEntryDelaySeconds", strategy.AvgEntryDelaySeconds);
        Add(command, "MaxEntryDelaySeconds", strategy.MaxEntryDelaySeconds);
        Add(command, "AvgCountertrendScoreBps", strategy.AvgCountertrendScoreBps);
        Add(command, "AvgCountertrendSignalBps", strategy.AvgCountertrendSignalBps);
        Add(command, "LastCountertrendSignalBps", NullableDecimal(strategy.LastCountertrendSignalBps));
        Add(command, "LiveOrdersCount", strategy.LiveOrdersCount);
        Add(command, "LiveFilledOrdersCount", strategy.LiveFilledOrdersCount);
        Add(command, "LiveOpenOrdersCount", strategy.LiveOpenOrdersCount);
        Add(command, "LiveSettledOrdersCount", strategy.LiveSettledOrdersCount);
        Add(command, "LiveSkippedOrdersCount", strategy.LiveSkippedOrdersCount);
        Add(command, "LiveConditionSkippedOrdersCount", strategy.LiveConditionSkippedOrdersCount);
        Add(command, "LiveTechnicalSkippedOrdersCount", strategy.LiveTechnicalSkippedOrdersCount);
        Add(command, "LiveIgnoredOrdersCount", strategy.LiveIgnoredOrdersCount);
        Add(command, "LiveIgnoredGtdUnfilledCount", strategy.LiveIgnoredGtdUnfilledCount);
        Add(command, "LiveIgnoredCancelledOrdersCount", strategy.LiveIgnoredCancelledOrdersCount);
        Add(command, "LiveIgnoredRejectedOrdersCount", strategy.LiveIgnoredRejectedOrdersCount);
        Add(command, "LiveWonOrdersCount", strategy.LiveWonOrdersCount);
        Add(command, "LiveLostOrdersCount", strategy.LiveLostOrdersCount);
        Add(command, "LiveStakeUsd", strategy.LiveStakeUsd);
        Add(command, "LiveRealizedPnlUsd", strategy.LiveRealizedPnlUsd);
        Add(command, "LiveWinRatePct", strategy.LiveWinRatePct);
        Add(command, "LiveLossRatePct", strategy.LiveLossRatePct);
        Add(command, "LiveAvgWinPnlUsd", strategy.LiveAvgWinPnlUsd);
        Add(command, "LiveAvgLossPnlUsd", strategy.LiveAvgLossPnlUsd);
        Add(command, "LiveProfitFactor", NullableDecimal(strategy.LiveProfitFactor));
        Add(command, "LiveExpectancyPnlUsd", strategy.LiveExpectancyPnlUsd);
        Add(command, "LiveRoiPct", strategy.LiveRoiPct);
        Add(command, "LiveLastOrderUtc", NullableDateTime(strategy.LiveLastOrderUtc));
        Add(command, "LiveLastSettlementUtc", NullableDateTime(strategy.LiveLastSettlementUtc));
        Add(command, "LastOrderUtc", NullableDateTime(strategy.LastOrderUtc));
        Add(command, "LastRunUtc", NullableDateTime(strategy.LastRunUtc));
        Add(command, "RefreshedAtUtc", UtcDateTime(refreshedAtUtc));
        return command;
    }

    private static NpgsqlBatchCommand CreateUpsertCommand(
        StrategyRecentPerformance strategy,
        DateTimeOffset refreshedAtUtc)
    {
        var command = new NpgsqlBatchCommand(UpsertStrategyRecentPerformanceSnapshotSql);
        Add(command, "StrategyId", strategy.StrategyId);
        Add(command, "Code", strategy.Code);
        Add(command, "Name", strategy.Name);
        Add(command, "LiveStakes", strategy.LiveStakes);
        Add(command, "Window", strategy.Window);
        Add(command, "WindowHours", strategy.WindowHours);
        Add(command, "WindowStartUtc", UtcDateTime(strategy.WindowStartUtc));
        Add(command, "WindowEndUtc", UtcDateTime(strategy.WindowEndUtc));
        Add(command, "OrdersCount", strategy.OrdersCount);
        Add(command, "FilledOrdersCount", strategy.FilledOrdersCount);
        Add(command, "ExpiredOrdersCount", strategy.ExpiredOrdersCount);
        Add(command, "OpenOrdersCount", strategy.OpenOrdersCount);
        Add(command, "EnteredRunsCount", strategy.EnteredRunsCount);
        Add(command, "SkippedRunsCount", strategy.SkippedRunsCount);
        Add(command, "PaperConditionSkippedRunsCount", strategy.PaperConditionSkippedRunsCount);
        Add(command, "PaperNotAcceptedRunsCount", strategy.PaperNotAcceptedRunsCount);
        Add(command, "SettledRunsCount", strategy.SettledRunsCount);
        Add(command, "WonRunsCount", strategy.WonRunsCount);
        Add(command, "LostRunsCount", strategy.LostRunsCount);
        Add(command, "FilledCostUsd", strategy.FilledCostUsd);
        Add(command, "RealizedPnlUsd", strategy.RealizedPnlUsd);
        Add(command, "AvgFillPrice", strategy.AvgFillPrice);
        Add(command, "AvgEntryDelaySeconds", strategy.AvgEntryDelaySeconds);
        Add(command, "MaxEntryDelaySeconds", strategy.MaxEntryDelaySeconds);
        Add(command, "WinRatePct", strategy.WinRatePct);
        Add(command, "RoiPct", strategy.RoiPct);
        Add(command, "LiveSettledOrdersCount", strategy.LiveSettledOrdersCount);
        Add(command, "LiveSkippedOrdersCount", strategy.LiveSkippedOrdersCount);
        Add(command, "LiveConditionSkippedOrdersCount", strategy.LiveConditionSkippedOrdersCount);
        Add(command, "LiveTechnicalSkippedOrdersCount", strategy.LiveTechnicalSkippedOrdersCount);
        Add(command, "LiveIgnoredOrdersCount", strategy.LiveIgnoredOrdersCount);
        Add(command, "LiveIgnoredGtdUnfilledCount", strategy.LiveIgnoredGtdUnfilledCount);
        Add(command, "LiveIgnoredCancelledOrdersCount", strategy.LiveIgnoredCancelledOrdersCount);
        Add(command, "LiveIgnoredRejectedOrdersCount", strategy.LiveIgnoredRejectedOrdersCount);
        Add(command, "LiveWonOrdersCount", strategy.LiveWonOrdersCount);
        Add(command, "LiveLostOrdersCount", strategy.LiveLostOrdersCount);
        Add(command, "LiveRealizedPnlUsd", strategy.LiveRealizedPnlUsd);
        Add(command, "LiveRoiPct", strategy.LiveRoiPct);
        Add(command, "TopSkipReason", strategy.TopSkipReason);
        Add(command, "LastOrderUtc", NullableDateTime(strategy.LastOrderUtc));
        Add(command, "LastRunUtc", NullableDateTime(strategy.LastRunUtc));
        Add(command, "RefreshedAtUtc", UtcDateTime(refreshedAtUtc));
        return command;
    }

    private static StrategyPerformance ReadStrategyPerformance(NpgsqlDataReader reader)
    {
        return new StrategyPerformance(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(7)),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetDecimal(14),
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
            reader.GetInt32(27),
            reader.GetDecimal(28),
            reader.GetDecimal(29),
            reader.GetDecimal(30),
            reader.GetDecimal(31),
            reader.GetDecimal(32),
            reader.GetDecimal(33),
            reader.GetDecimal(34),
            reader.GetDecimal(35),
            reader.IsDBNull(36) ? null : reader.GetDecimal(36),
            reader.GetDecimal(37),
            reader.GetDecimal(38),
            reader.GetDecimal(39),
            reader.GetDecimal(40),
            reader.GetDecimal(41),
            reader.GetDecimal(42),
            reader.GetDecimal(43),
            reader.IsDBNull(44) ? null : reader.GetDecimal(44),
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
            reader.GetInt32(57),
            reader.GetDecimal(58),
            reader.GetDecimal(59),
            reader.GetDecimal(60),
            reader.GetDecimal(61),
            reader.GetDecimal(62),
            reader.GetDecimal(63),
            reader.IsDBNull(64) ? null : reader.GetDecimal(64),
            reader.GetDecimal(65),
            reader.GetDecimal(66),
            reader.IsDBNull(67) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(67)),
            reader.IsDBNull(68) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(68)),
            reader.IsDBNull(69) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(69)),
            reader.IsDBNull(70) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(70)));
    }

    private static StrategyRecentPerformance ReadStrategyRecentPerformance(NpgsqlDataReader reader)
    {
        return new StrategyRecentPerformance(
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
            reader.IsDBNull(40) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(40)));
    }

    private static void Add(NpgsqlBatchCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
    }

    private static DateTime UtcDateTime(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime;
    }

    private static object NullableDateTime(DateTimeOffset? timestamp)
    {
        return timestamp.HasValue ? UtcDateTime(timestamp.Value) : DBNull.Value;
    }

    private static object NullableDecimal(decimal? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static DateTimeOffset DateTimeOffsetFromUtc(DateTime timestamp)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
    }
}
