using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DashboardIncrementalProjectionIntegrationTests
{
    [Fact]
    public async Task Projection_BootstrapDeltaAndReconciliation_MatchRawAggregates()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var rawRepository = new PostgresAppRepository(factory);
        var (strategyId, strategyCode) = await ReadFirstStrategyAsync(factory);
        var runId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var marketId = $"projection-test-{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        var bootstrap = await projection.BootstrapAsync();
        Assert.True(bootstrap.Strategies > 0);
        Assert.True((await projection.GetControlStateAsync()).Initialized);

        await InsertRunAsync(
            factory,
            runId,
            strategyId,
            marketId,
            StrategyMarketPaperRunStatuses.Observed,
            nowUtc,
            enteredAtUtc: null,
            settledAtUtc: null,
            realizedPnlUsd: null,
            skipReason: null);
        var observedBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(1, observedBatch.EventsApplied);
        var observed = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, observed.ObservedRunsCount);
        Assert.Equal(0, observed.SkippedRunsCount);

        await UpdateRunAsync(
            factory,
            runId,
            StrategyMarketPaperRunStatuses.Skipped,
            nowUtc.AddSeconds(2),
            enteredAtUtc: null,
            settledAtUtc: null,
            realizedPnlUsd: null,
            skipReason: "reference_threshold_not_met");
        var skippedBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(1, skippedBatch.EventsApplied);
        var skipped = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(0, skipped.ObservedRunsCount);
        Assert.Equal(1, skipped.SkippedRunsCount);
        Assert.Equal(1, skipped.PaperConditionSkippedRunsCount);

        await UpdateRunAsync(
            factory,
            runId,
            StrategyMarketPaperRunStatuses.Settled,
            nowUtc.AddSeconds(5),
            enteredAtUtc: nowUtc.AddSeconds(1),
            settledAtUtc: nowUtc.AddSeconds(5),
            realizedPnlUsd: 2m,
            skipReason: null);
        var settledBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(1, settledBatch.EventsApplied);
        var settled = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(0, settled.SkippedRunsCount);
        Assert.Equal(1, settled.SettledRunsCount);
        Assert.Equal(1, settled.WonPositionsCount);
        Assert.Equal(2m, settled.RealizedPnlUsd);

        await InsertPaperOrderAsync(factory, paperOrderId, strategyId, nowUtc.AddSeconds(6));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var pendingOrder = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, pendingOrder.OrdersCount);
        Assert.Equal(1, pendingOrder.OpenOrdersCount);

        await UpdatePaperOrderStatusAsync(factory, paperOrderId, nameof(PaperOrderStatus.Filled));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var filledOrder = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, filledOrder.FilledOrdersCount);
        Assert.Equal(0, filledOrder.OpenOrdersCount);
        Assert.Equal(6m, filledOrder.StakeUsd);

        await InsertPaperFillAsync(factory, paperOrderId, nowUtc.AddSeconds(7));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);

        var paperPositionId = await InsertPaperPositionAsync(factory, strategyCode, nowUtc.AddSeconds(8));
        await UpdatePaperPositionAsync(factory, paperPositionId, 0.9m, nowUtc.AddSeconds(9));
        await UpdatePaperPositionAsync(factory, paperPositionId, 1.25m, nowUtc.AddSeconds(10));
        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, paperPositionId));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var openPosition = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, openPosition.OpenPositionsCount);
        Assert.Equal(1.25m, openPosition.UnrealizedPnlUsd);
        Assert.Equal(1.25m, await ReadPositionFactUnrealizedPnlAsync(factory, paperPositionId));

        await UpdatePaperPositionEstimatedValueOnlyAsync(factory, paperPositionId);
        Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, paperPositionId));

        await UpdatePaperPositionAsync(factory, paperPositionId, 0.5m, nowUtc.AddSeconds(11));
        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, paperPositionId));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var repricedPosition = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, repricedPosition.OpenPositionsCount);
        Assert.Equal(0.5m, repricedPosition.UnrealizedPnlUsd);
        Assert.Equal(0.5m, await ReadPositionFactUnrealizedPnlAsync(factory, paperPositionId));

        await UpdatePaperPositionAsync(factory, paperPositionId, 0.4m, nowUtc.AddSeconds(12));
        await DeleteLockedPositionEventWhileUpdateWaitsAsync(
            factory,
            paperPositionId,
            0.3m,
            nowUtc.AddSeconds(13));
        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, paperPositionId));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var concurrentlyRepricedPosition = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(0.3m, concurrentlyRepricedPosition.UnrealizedPnlUsd);
        Assert.Equal(0.3m, await ReadPositionFactUnrealizedPnlAsync(factory, paperPositionId));

        await InsertPaperSettlementAsync(factory, strategyCode, nowUtc.AddSeconds(14));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);

        await InsertLiveOrderAsync(factory, strategyId, nowUtc.AddSeconds(15));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        var liveOrder = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(1, liveOrder.LiveOrdersCount);
        Assert.Equal(1, liveOrder.LiveFilledOrdersCount);
        Assert.Equal(1, liveOrder.LiveSettledOrdersCount);
        Assert.Equal(1, liveOrder.LiveWonOrdersCount);
        Assert.Equal(1.5m, liveOrder.LiveRealizedPnlUsd);

        var emptyBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(0, emptyBatch.EventsRead);
        var afterReplay = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(liveOrder, afterReplay);

        var rawBeforeReconciliation = (await rawRepository.GetStrategyPerformanceAsync())
            .Single(strategy => strategy.StrategyId == strategyId);
        AssertStrategyMetricsEqual(rawBeforeReconciliation, afterReplay);
        var incrementalStateJson = await ReadLifetimeStateJsonAsync(factory, strategyId);

        await QueueReconciliationAsync(factory, strategyId);
        var reconciliation = await projection.ReconcileNextStrategyAsync();
        Assert.True(reconciliation.Reconciled);
        Assert.Equal(strategyId, reconciliation.StrategyId);
        var reconciledStateJson = await ReadLifetimeStateJsonAsync(factory, strategyId);
        Assert.False(
            reconciliation.ValuesChanged,
            $"Incremental: {incrementalStateJson}{Environment.NewLine}Reconciled: {reconciledStateJson}");
        Assert.Null(reconciliation.Error);

        var projected = await ReadSnapshotAsync(snapshots, strategyId);
        var raw = (await rawRepository.GetStrategyPerformanceAsync())
            .Single(strategy => strategy.StrategyId == strategyId);
        AssertStrategyMetricsEqual(raw, projected);

        var projectedRecent = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
            .Where(row => row.StrategyId == strategyId)
            .OrderBy(row => row.WindowHours)
            .ToArray();
        var rawRecent = (await rawRepository.GetStrategyRecentPerformanceAsync())
            .Where(row => row.StrategyId == strategyId)
            .OrderBy(row => row.WindowHours)
            .ToArray();
        Assert.Equal(3, projectedRecent.Length);
        Assert.Equal(3, rawRecent.Length);
        for (var index = 0; index < rawRecent.Length; index++)
        {
            AssertRecentMetricsEqual(rawRecent[index], projectedRecent[index]);
        }

        await AgePaperOrderProjectionFactAsync(factory, paperOrderId, 2);
        var expiry = await projection.ExpireRecentFactsAsync(100);
        Assert.Equal(1, expiry.FactsExpired);
        var afterExpiry = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
            .Where(row => row.StrategyId == strategyId)
            .ToDictionary(row => row.WindowHours);
        Assert.Equal(0, afterExpiry[1].OrdersCount);
        Assert.Equal(1, afterExpiry[6].OrdersCount);
        Assert.Equal(1, afterExpiry[24].OrdersCount);

        await AgePaperOrderProjectionFactAsync(factory, paperOrderId, 7);
        var sixHourExpiry = await projection.ExpireRecentFactsAsync(100);
        Assert.Equal(1, sixHourExpiry.FactsExpired);
        var afterSixHourExpiry = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
            .Where(row => row.StrategyId == strategyId)
            .ToDictionary(row => row.WindowHours);
        Assert.Equal(0, afterSixHourExpiry[1].OrdersCount);
        Assert.Equal(0, afterSixHourExpiry[6].OrdersCount);
        Assert.Equal(1, afterSixHourExpiry[24].OrdersCount);

        await AgePaperOrderProjectionFactAsync(factory, paperOrderId, 25);
        var twentyFourHourExpiry = await projection.ExpireRecentFactsAsync(100);
        Assert.Equal(1, twentyFourHourExpiry.FactsExpired);
        var afterTwentyFourHourExpiry = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
            .Where(row => row.StrategyId == strategyId)
            .ToDictionary(row => row.WindowHours);
        Assert.Equal(0, afterTwentyFourHourExpiry[1].OrdersCount);
        Assert.Equal(0, afterTwentyFourHourExpiry[6].OrdersCount);
        Assert.Equal(0, afterTwentyFourHourExpiry[24].OrdersCount);
        Assert.Equal(0, await ReadRecentProjectionFactCountForSourceAsync(
            factory,
            DashboardProjectionSourceKinds.PaperOrder,
            paperOrderId));

        var disposableStrategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, disposableStrategyId);
        var createStrategyBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(1, createStrategyBatch.ReconciliationsQueued);
        var createStrategyReconciliation = await projection.ReconcileNextStrategyAsync();
        Assert.True(createStrategyReconciliation.Reconciled);
        Assert.Equal(disposableStrategyId, createStrategyReconciliation.StrategyId);
        Assert.Contains(
            await snapshots.GetStrategyPerformanceSnapshotAsync(),
            row => row.StrategyId == disposableStrategyId);

        await DeleteStrategyAsync(factory, disposableStrategyId);
        var deleteStrategyBatch = await projection.ApplyPendingEventsAsync(100);
        Assert.Equal(1, deleteStrategyBatch.EventsApplied);
        var deletedCounts = await ReadDeletedStrategyProjectionCountsAsync(factory, disposableStrategyId);
        Assert.Equal((0, 0, 0, 0), deletedCounts);
    }

    [Fact]
    public async Task ApplyPendingEvents_MultipleEventsForSameRun_PersistsOnlyFinalFacts()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        await InsertStrategyAsync(factory, strategyId);
        await projection.BootstrapAsync();
        await InsertRunAsync(
            factory,
            runId,
            strategyId,
            $"projection-multi-event-{Guid.NewGuid():N}",
            StrategyMarketPaperRunStatuses.Observed,
            nowUtc,
            enteredAtUtc: null,
            settledAtUtc: null,
            realizedPnlUsd: null,
            skipReason: null);
        await UpdateRunAsync(
            factory,
            runId,
            StrategyMarketPaperRunStatuses.Skipped,
            nowUtc.AddSeconds(2),
            enteredAtUtc: null,
            settledAtUtc: null,
            realizedPnlUsd: null,
            skipReason: "reference_threshold_not_met");
        await UpdateRunAsync(
            factory,
            runId,
            StrategyMarketPaperRunStatuses.Settled,
            nowUtc.AddSeconds(5),
            enteredAtUtc: nowUtc.AddSeconds(1),
            settledAtUtc: nowUtc.AddSeconds(5),
            realizedPnlUsd: 2m,
            skipReason: null);

        Assert.Equal(3, await ReadProjectionEventCountForSourceAsync(
            factory,
            DashboardProjectionSourceKinds.StrategyRun,
            runId));

        var batch = await projection.ApplyPendingEventsAsync(100);

        Assert.Equal(3, batch.EventsRead);
        Assert.Equal(3, batch.EventsApplied);
        Assert.Equal(3, await ReadRecentProjectionFactCountForSourceAsync(
            factory,
            DashboardProjectionSourceKinds.StrategyRun,
            runId));
        var snapshot = await ReadSnapshotAsync(snapshots, strategyId);
        Assert.Equal(0, snapshot.ObservedRunsCount);
        Assert.Equal(0, snapshot.SkippedRunsCount);
        Assert.Equal(1, snapshot.SettledRunsCount);
        Assert.Equal(2m, snapshot.RealizedPnlUsd);

        await DeleteRunAsync(factory, runId);
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
        await DeleteStrategyAsync(factory, strategyId);
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(100)).EventsApplied);
    }

    [Fact]
    public async Task ExpireRecentFacts_SkipsLockedOldestFactAndBackfillsBatch()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        var lockedOrderId = Guid.NewGuid();
        var nextOrderId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        await InsertStrategyAsync(factory, strategyId);
        await InsertPaperOrderAsync(factory, lockedOrderId, strategyId, nowUtc);
        await InsertPaperOrderAsync(factory, nextOrderId, strategyId, nowUtc.AddSeconds(1));
        await projection.BootstrapAsync();
        await SetPaperOrderProjectionFactOccurredAtAsync(
            factory,
            lockedOrderId,
            DateTimeOffset.UnixEpoch.AddDays(1));
        await SetPaperOrderProjectionFactOccurredAtAsync(
            factory,
            nextOrderId,
            DateTimeOffset.UnixEpoch.AddDays(2));

        await using var blockerConnection = factory.CreateConnection();
        await blockerConnection.OpenAsync();
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            """
SELECT source_id
FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind
  AND source_id = @SourceId
  AND fact_kind = @FactKind
FOR UPDATE;
""",
            blockerConnection,
            blockerTransaction))
        {
            lockCommand.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.PaperOrder);
            lockCommand.Parameters.AddWithValue("SourceId", lockedOrderId);
            lockCommand.Parameters.AddWithValue("FactKind", DashboardProjectionFactKinds.PaperOrderCreated);
            Assert.Equal(lockedOrderId, Assert.IsType<Guid>(await lockCommand.ExecuteScalarAsync()));
        }

        try
        {
            var expiry = await projection.ExpireRecentFactsAsync(1);

            Assert.Equal(1, expiry.FactsExpired);
            Assert.Equal(1, await ReadRecentProjectionFactCountForSourceAsync(
                factory,
                DashboardProjectionSourceKinds.PaperOrder,
                lockedOrderId));
            Assert.Equal(0, await ReadRecentProjectionFactCountForSourceAsync(
                factory,
                DashboardProjectionSourceKinds.PaperOrder,
                nextOrderId));
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        Assert.Equal(1, (await projection.ExpireRecentFactsAsync(1)).FactsExpired);
        Assert.Equal(0, await ReadRecentProjectionFactCountForSourceAsync(
            factory,
            DashboardProjectionSourceKinds.PaperOrder,
            lockedOrderId));
    }

    private static async Task<(Guid StrategyId, string StrategyCode)> ReadFirstStrategyAsync(
        PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id, code FROM strategies ORDER BY id LIMIT 1;",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("No strategy row.");
        }

        return (reader.GetGuid(0), reader.GetString(1));
    }

    private static async Task InsertPaperOrderAsync(
        PostgresConnectionFactory factory,
        Guid paperOrderId,
        Guid strategyId,
        DateTimeOffset createdAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id,
    condition_id, outcome, price, size_shares, notional_usd, created_at_utc,
    expires_at_utc, raw_decision_json)
VALUES (
    @Id, @SignalId, @StrategyId, '', @Status, 'Buy', @AssetId,
    @ConditionId, 'Up', 0.5, 12, 6, @CreatedAtUtc,
    @ExpiresAtUtc, CAST(@RawDecisionJson AS jsonb));
""",
            connection);
        command.Parameters.AddWithValue("Id", paperOrderId);
        command.Parameters.AddWithValue("SignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Status", nameof(PaperOrderStatus.Pending));
        command.Parameters.AddWithValue("AssetId", $"projection-asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ConditionId", $"projection-condition-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CreatedAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("ExpiresAtUtc", createdAtUtc.AddMinutes(5).UtcDateTime);
        command.Parameters.AddWithValue(
            "RawDecisionJson",
            "{\"previous_score_bps\":12.5,\"selected_signal_bps\":15.0}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (id, code, name, description, created_at_utc, updated_at_utc)
VALUES (@Id, @Code, @Name, 'projection integration test', clock_timestamp(), clock_timestamp());
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Code", $"projection_test_{strategyId:N}");
        command.Parameters.AddWithValue("Name", $"Projection Test {strategyId:N}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AgePaperOrderProjectionFactAsync(
        PostgresConnectionFactory factory,
        Guid paperOrderId,
        int ageHours)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ageHours);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_strategy_recent_projection_facts
SET occurred_at_utc = clock_timestamp() - make_interval(hours => @AgeHours)
WHERE source_kind = @SourceKind
  AND source_id = @SourceId
  AND fact_kind = @FactKind;
""",
            connection);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.PaperOrder);
        command.Parameters.AddWithValue("SourceId", paperOrderId);
        command.Parameters.AddWithValue("FactKind", DashboardProjectionFactKinds.PaperOrderCreated);
        command.Parameters.AddWithValue("AgeHours", ageHours);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetPaperOrderProjectionFactOccurredAtAsync(
        PostgresConnectionFactory factory,
        Guid paperOrderId,
        DateTimeOffset occurredAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_strategy_recent_projection_facts
SET occurred_at_utc = @OccurredAtUtc
WHERE source_kind = @SourceKind
  AND source_id = @SourceId
  AND fact_kind = @FactKind;
""",
            connection);
        command.Parameters.AddWithValue("OccurredAtUtc", NpgsqlDbType.TimestampTz, occurredAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.PaperOrder);
        command.Parameters.AddWithValue("SourceId", paperOrderId);
        command.Parameters.AddWithValue("FactKind", DashboardProjectionFactKinds.PaperOrderCreated);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteRunAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM strategies WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(int Snapshots, int States, int Events, int Queue)> ReadDeletedStrategyProjectionCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*)::integer FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_projection_events WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @Id);
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private static async Task UpdatePaperOrderStatusAsync(
        PostgresConnectionFactory factory,
        Guid paperOrderId,
        string status)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE paper_orders SET status = @Status WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", paperOrderId);
        command.Parameters.AddWithValue("Status", status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPaperFillAsync(
        PostgresConnectionFactory factory,
        Guid paperOrderId,
        DateTimeOffset filledAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)
VALUES (
    @Id, @PaperOrderId, 0.5, 12, @FilledAtUtc, 'projection_test', 0);
""",
            connection);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("PaperOrderId", paperOrderId);
        command.Parameters.AddWithValue("FilledAtUtc", filledAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> InsertPaperPositionAsync(
        PostgresConnectionFactory factory,
        string strategyCode,
        DateTimeOffset updatedAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares,
    average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc)
VALUES (
    @Id, @Wallet, @AssetId, @ConditionId, 'Up', 3,
    0.5, 2.25, 0.75, @UpdatedAtUtc);
""",
            connection);
        var positionId = Guid.NewGuid();
        command.Parameters.AddWithValue("Id", positionId);
        command.Parameters.AddWithValue("Wallet", $"strategy:{strategyCode}");
        command.Parameters.AddWithValue("AssetId", $"projection-position-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ConditionId", $"projection-condition-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
        return positionId;
    }

    private static async Task UpdatePaperPositionAsync(
        PostgresConnectionFactory factory,
        Guid positionId,
        decimal unrealizedPnlUsd,
        DateTimeOffset updatedAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_positions
SET unrealized_pnl_usd = @UnrealizedPnlUsd,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        command.Parameters.AddWithValue("UnrealizedPnlUsd", unrealizedPnlUsd);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdatePaperPositionEstimatedValueOnlyAsync(
        PostgresConnectionFactory factory,
        Guid positionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE paper_positions SET estimated_value_usd = estimated_value_usd + 0.01 WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteLockedPositionEventWhileUpdateWaitsAsync(
        PostgresConnectionFactory factory,
        Guid positionId,
        decimal finalUnrealizedPnlUsd,
        DateTimeOffset updatedAtUtc)
    {
        await using var lockConnection = factory.CreateConnection();
        await lockConnection.OpenAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(
            """
SELECT id
FROM dashboard_projection_events
WHERE source_kind = 'PaperPosition' AND source_id = @Id
FOR UPDATE;
""",
            lockConnection,
            transaction))
        {
            lockCommand.Parameters.AddWithValue("Id", positionId);
            Assert.NotNull(await lockCommand.ExecuteScalarAsync());
        }

        var waitingUpdate = UpdatePaperPositionAsync(
            factory,
            positionId,
            finalUnrealizedPnlUsd,
            updatedAtUtc);
        await Task.Delay(100);
        Assert.False(waitingUpdate.IsCompleted);

        await using (var deleteCommand = new NpgsqlCommand(
            """
DELETE FROM dashboard_projection_events
WHERE source_kind = 'PaperPosition' AND source_id = @Id;
""",
            lockConnection,
            transaction))
        {
            deleteCommand.Parameters.AddWithValue("Id", positionId);
            Assert.Equal(1, await deleteCommand.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        await waitingUpdate.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<int> ReadPaperPositionEventCountAsync(
        PostgresConnectionFactory factory,
        Guid positionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM dashboard_projection_events
WHERE source_kind = 'PaperPosition' AND source_id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> ReadProjectionEventCountForSourceAsync(
        PostgresConnectionFactory factory,
        string sourceKind,
        Guid sourceId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM dashboard_projection_events
WHERE source_kind = @SourceKind AND source_id = @SourceId;
""",
            connection);
        command.Parameters.AddWithValue("SourceKind", sourceKind);
        command.Parameters.AddWithValue("SourceId", sourceId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> ReadRecentProjectionFactCountForSourceAsync(
        PostgresConnectionFactory factory,
        string sourceKind,
        Guid sourceId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind AND source_id = @SourceId;
""",
            connection);
        command.Parameters.AddWithValue("SourceKind", sourceKind);
        command.Parameters.AddWithValue("SourceId", sourceId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<decimal> ReadPositionFactUnrealizedPnlAsync(
        PostgresConnectionFactory factory,
        Guid positionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT unrealized_pnl_usd
FROM dashboard_strategy_position_projection_facts
WHERE source_id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        return (decimal)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Position projection fact was not written."));
    }

    private static async Task InsertPaperSettlementAsync(
        PostgresConnectionFactory factory,
        string strategyCode,
        DateTimeOffset settledAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id,
    winning_outcome, category, settled_size_shares, average_price, cost_basis_usd,
    settlement_value_usd, realized_pnl_usd, won, settlement_source,
    settled_at_utc, created_at_utc)
VALUES (
    @Id, @Wallet, @AssetId, @ConditionId, 'Up', @WinningAssetId,
    'Up', 'projection_test', 12, 0.5, 6,
    8, 2, true, 'projection_test', @SettledAtUtc, @SettledAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("Wallet", $"strategy:{strategyCode}");
        var assetId = $"projection-settlement-{Guid.NewGuid():N}";
        command.Parameters.AddWithValue("AssetId", assetId);
        command.Parameters.AddWithValue("WinningAssetId", assetId);
        command.Parameters.AddWithValue("ConditionId", $"projection-condition-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("SettledAtUtc", settledAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertLiveOrderAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset settledAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO live_orders (
    id, signal_id, strategy_id, status, order_id, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, order_type, created_at_utc,
    expires_at_utc, submitted_at_utc, response_status, filled_size, remaining_size,
    average_fill_price, filled_notional_usd, cost_basis_usd, fee_usd, cancel_status,
    raw_response_json, validation_summary, settlement_value_usd, realized_pnl_usd,
    settled_at_utc, winning_asset_id, winning_outcome, won, settlement_source,
    updated_at_utc)
VALUES (
    @Id, @SignalId, @StrategyId, @Status, @OrderId, 'Buy', @AssetId, @ConditionId,
    'Up', 0.5, 12, 6, 'FAK', @CreatedAtUtc,
    @ExpiresAtUtc, @CreatedAtUtc, 'ok', 12, 0,
    0.5, 6, 6, 0, '',
    '{}'::jsonb, 'projection_test', 7.5, 1.5,
    @SettledAtUtc, @AssetId, 'Up', true, 'projection_test',
    @SettledAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", Guid.NewGuid());
        command.Parameters.AddWithValue("SignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Status", nameof(LiveOrderStatus.Matched));
        command.Parameters.AddWithValue("OrderId", $"projection-order-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("AssetId", $"projection-live-asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ConditionId", $"projection-condition-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CreatedAtUtc", settledAtUtc.AddSeconds(-2).UtcDateTime);
        command.Parameters.AddWithValue("ExpiresAtUtc", settledAtUtc.AddMinutes(5).UtcDateTime);
        command.Parameters.AddWithValue("SettledAtUtc", settledAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRunAsync(
        PostgresConnectionFactory factory,
        Guid runId,
        Guid strategyId,
        string marketId,
        string status,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? enteredAtUtc,
        DateTimeOffset? settledAtUtc,
        decimal? realizedPnlUsd,
        string? skipReason)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title, category,
    market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,
    selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,
    signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,
    realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc)
VALUES (
    @Id, @StrategyId, @MarketId, @ConditionId, @MarketSlug, 'Projection integration test', 'Test',
    @MarketStartUtc, @MarketEndUtc, @DetectedAtUtc, @EntryDueAtUtc, @Status,
    NULL, NULL, NULL, 6.00, NULL,
    NULL, NULL, @EnteredAtUtc, NULL, NULL,
    @RealizedPnlUsd, @SettledAtUtc, @SkipReason, @CreatedAtUtc, @UpdatedAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("MarketId", marketId);
        command.Parameters.AddWithValue("ConditionId", $"condition-{marketId}");
        command.Parameters.AddWithValue("MarketSlug", marketId);
        command.Parameters.AddWithValue("MarketStartUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("MarketEndUtc", updatedAtUtc.AddMinutes(5).UtcDateTime);
        command.Parameters.AddWithValue("DetectedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("EntryDueAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("Status", status);
        AddNullable(command, "EnteredAtUtc", enteredAtUtc, NpgsqlDbType.TimestampTz);
        AddNullable(command, "RealizedPnlUsd", realizedPnlUsd, NpgsqlDbType.Numeric);
        AddNullable(command, "SettledAtUtc", settledAtUtc, NpgsqlDbType.TimestampTz);
        AddNullable(command, "SkipReason", skipReason, NpgsqlDbType.Text);
        command.Parameters.AddWithValue("CreatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdateRunAsync(
        PostgresConnectionFactory factory,
        Guid runId,
        string status,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? enteredAtUtc,
        DateTimeOffset? settledAtUtc,
        decimal? realizedPnlUsd,
        string? skipReason)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE strategy_market_paper_runs
SET status = @Status,
    entered_at_utc = @EnteredAtUtc,
    settled_at_utc = @SettledAtUtc,
    realized_pnl_usd = @RealizedPnlUsd,
    skip_reason = @SkipReason,
    updated_at_utc = @UpdatedAtUtc
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        command.Parameters.AddWithValue("Status", status);
        AddNullable(command, "EnteredAtUtc", enteredAtUtc, NpgsqlDbType.TimestampTz);
        AddNullable(command, "SettledAtUtc", settledAtUtc, NpgsqlDbType.TimestampTz);
        AddNullable(command, "RealizedPnlUsd", realizedPnlUsd, NpgsqlDbType.Numeric);
        AddNullable(command, "SkipReason", skipReason, NpgsqlDbType.Text);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task QueueReconciliationAsync(PostgresConnectionFactory factory, Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO dashboard_projection_reconciliation_queue (strategy_id, priority, reason)
VALUES (@StrategyId, 1000, 'integration_test')
ON CONFLICT (strategy_id) DO UPDATE SET priority = EXCLUDED.priority, reason = EXCLUDED.reason;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<StrategyPerformance> ReadSnapshotAsync(
        PostgresDashboardSnapshotRepository snapshots,
        Guid strategyId)
    {
        return (await snapshots.GetStrategyPerformanceSnapshotAsync())
            .Single(strategy => strategy.StrategyId == strategyId);
    }

    private static async Task<string> ReadLifetimeStateJsonAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT state_json::text FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("State row missing."));
    }

    private static void AddNullable(
        NpgsqlCommand command,
        string name,
        object? value,
        NpgsqlDbType type)
    {
        command.Parameters.Add(new NpgsqlParameter(name, type)
        {
            Value = value switch
            {
                DateTimeOffset timestamp => timestamp.UtcDateTime,
                null => DBNull.Value,
                _ => value
            }
        });
    }

    private static void AssertStrategyMetricsEqual(StrategyPerformance expected, StrategyPerformance actual)
    {
        Assert.Equal(expected.OrdersCount, actual.OrdersCount);
        Assert.Equal(expected.FilledOrdersCount, actual.FilledOrdersCount);
        Assert.Equal(expected.OpenOrdersCount, actual.OpenOrdersCount);
        Assert.Equal(expected.OpenPositionsCount, actual.OpenPositionsCount);
        Assert.Equal(expected.ObservedRunsCount, actual.ObservedRunsCount);
        Assert.Equal(expected.EnteredRunsCount, actual.EnteredRunsCount);
        Assert.Equal(expected.SkippedRunsCount, actual.SkippedRunsCount);
        Assert.Equal(expected.PaperConditionSkippedRunsCount, actual.PaperConditionSkippedRunsCount);
        Assert.Equal(expected.PaperNotAcceptedRunsCount, actual.PaperNotAcceptedRunsCount);
        Assert.Equal(expected.SettledRunsCount, actual.SettledRunsCount);
        Assert.Equal(expected.WonPositionsCount, actual.WonPositionsCount);
        Assert.Equal(expected.LostPositionsCount, actual.LostPositionsCount);
        AssertSnapshotDecimalEqual(expected.StakeUsd, actual.StakeUsd);
        AssertSnapshotDecimalEqual(expected.RealizedPnlUsd, actual.RealizedPnlUsd);
        AssertSnapshotDecimalEqual(expected.UnrealizedPnlUsd, actual.UnrealizedPnlUsd);
        AssertSnapshotDecimalEqual(expected.TotalPnlUsd, actual.TotalPnlUsd);
        AssertSnapshotDecimalEqual(expected.WinRatePct, actual.WinRatePct);
        AssertSnapshotDecimalEqual(expected.RoiPct, actual.RoiPct);
        AssertSnapshotDecimalEqual(expected.ClosedRoiPct, actual.ClosedRoiPct);
        AssertSnapshotDecimalEqual(expected.AvgEntryDelaySeconds, actual.AvgEntryDelaySeconds);
        AssertSnapshotDecimalEqual(expected.MaxEntryDelaySeconds, actual.MaxEntryDelaySeconds);
        Assert.Equal(expected.LastRunUtc, actual.LastRunUtc);
    }

    private static void AssertRecentMetricsEqual(
        StrategyRecentPerformance expected,
        StrategyRecentPerformance actual)
    {
        Assert.Equal(expected.WindowHours, actual.WindowHours);
        Assert.Equal(expected.OrdersCount, actual.OrdersCount);
        Assert.Equal(expected.FilledOrdersCount, actual.FilledOrdersCount);
        Assert.Equal(expected.ExpiredOrdersCount, actual.ExpiredOrdersCount);
        Assert.Equal(expected.OpenOrdersCount, actual.OpenOrdersCount);
        Assert.Equal(expected.EnteredRunsCount, actual.EnteredRunsCount);
        Assert.Equal(expected.SkippedRunsCount, actual.SkippedRunsCount);
        Assert.Equal(expected.SettledRunsCount, actual.SettledRunsCount);
        Assert.Equal(expected.WonRunsCount, actual.WonRunsCount);
        Assert.Equal(expected.LostRunsCount, actual.LostRunsCount);
        AssertSnapshotDecimalEqual(expected.FilledCostUsd, actual.FilledCostUsd);
        AssertSnapshotDecimalEqual(expected.RealizedPnlUsd, actual.RealizedPnlUsd);
        AssertSnapshotDecimalEqual(expected.AvgEntryDelaySeconds, actual.AvgEntryDelaySeconds);
        AssertSnapshotDecimalEqual(expected.MaxEntryDelaySeconds, actual.MaxEntryDelaySeconds);
        AssertSnapshotDecimalEqual(expected.WinRatePct, actual.WinRatePct);
        AssertSnapshotDecimalEqual(expected.RoiPct, actual.RoiPct);
        Assert.Equal(expected.TopSkipReason, actual.TopSkipReason);
    }

    private static void AssertSnapshotDecimalEqual(decimal expected, decimal actual)
    {
        Assert.InRange(Math.Abs(expected - actual), 0m, 0.000000005m);
    }
}
