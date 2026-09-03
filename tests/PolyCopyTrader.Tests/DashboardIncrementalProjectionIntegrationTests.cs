using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class DashboardIncrementalProjectionIntegrationTests
{
    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_EventAckCompletesWhileRealBatchMarksHoldQueueLocks()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        foreach (var rollbackProducer in new[] { false, true })
        foreach (var unknownNet in new[] { false, true })
        {
            var strategyId = Guid.NewGuid();
            var code = $"projection_test_{strategyId:N}";
            await InsertStrategyAsync(factory, strategyId);
            var positionIds = new List<Guid>();
            for (var index = 0; index < 4; index++)
                positionIds.Add(await InsertPaperPositionAsync(factory, code, DateTimeOffset.UtcNow));
            var runId = Guid.NewGuid();
            var settledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await InsertRunAsync(factory, runId, strategyId, $"ack-run-{runId:N}",
                StrategyMarketPaperRunStatuses.Settled, settledAt, settledAt, settledAt, 2m, null);
            await using (var setup = factory.CreateConnection())
            {
                await setup.OpenAsync();
                await using var fees = new NpgsqlCommand("""
UPDATE paper_positions SET fee_usd = 0.02, fee_accounting_status = 'Calculated',
    net_unrealized_pnl_usd = 0.73 WHERE id = ANY(@Ids);
""", setup);
                fees.Parameters.AddWithValue("Ids", positionIds.ToArray());
                Assert.Equal(4, await fees.ExecuteNonQueryAsync());
                await using var runFees = new NpgsqlCommand("""
UPDATE strategy_market_paper_runs
SET fee_usd = 0.10, fee_accounting_status = @Status, net_realized_pnl_usd = @Net
WHERE id = @Id;
""", setup);
                runFees.Parameters.AddWithValue("Id", runId);
                runFees.Parameters.AddWithValue("Status", unknownNet ? "LegacyUnknown" : "Calculated");
                runFees.Parameters.AddWithValue("Net", NpgsqlDbType.Numeric, unknownNet ? DBNull.Value : 1.90m);
                Assert.Equal(1, await runFees.ExecuteNonQueryAsync());
                if (unknownNet)
                {
                    await using var unknown = new NpgsqlCommand("""
UPDATE paper_positions SET fee_usd = 0, fee_accounting_status = 'LegacyUnknown',
    net_unrealized_pnl_usd = NULL WHERE id = @Id;
""", setup);
                    unknown.Parameters.AddWithValue("Id", positionIds[0]);
                    Assert.Equal(1, await unknown.ExecuteNonQueryAsync());
                }
            }
            var projection = new PostgresDashboardProjectionRepository(factory);
            await projection.BootstrapAsync();
            // Three selected position events: two busy, one free. The fourth
            // position gets its event only after the consumer has read its batch.
            foreach (var id in positionIds.Take(3))
                await UpdateAckFixturePositionAsync(factory, id, 1m);
            var orderId = Guid.NewGuid();
            await InsertPaperOrderAsync(factory, orderId, strategyId, DateTimeOffset.UtcNow);
            var positions = (await new PostgresAppRepository(factory).GetPaperPositionsAsync())
                .Where(position => position.CopiedTraderWallet == $"strategy:{code}").ToArray();
            var marks = positions.Where(position => position.UnrealizedPnlUsd == 1m).Take(2)
                .Select(position => new PaperPositionMarkUpdate(position, 3.5m, 2m,
                    DateTimeOffset.UtcNow, position.NetUnrealizedPnlUsd.HasValue ? 1.98m : null)).ToArray();
            Assert.Equal(2, marks.Length);
            var markedAssets = marks.Select(mark => mark.ExpectedPosition.AssetId).ToArray();
            var suffix = Guid.NewGuid().ToString("N");
            var producerName = $"batch_marks_{suffix}";
            var functionName = $"pct_marks_barrier_{suffix}";
            var lockKey = BitConverter.ToInt32(Guid.NewGuid().ToByteArray());
            await using var barrier = factory.CreateConnection();
            await barrier.OpenAsync();
            await using (var setup = new NpgsqlCommand($$"""
CREATE FUNCTION public.{{functionName}}() RETURNS trigger LANGUAGE plpgsql AS $test$
BEGIN
    IF current_setting('application_name') = '{{producerName}}' THEN
        PERFORM pg_advisory_xact_lock(1809202604, {{lockKey}});
        {{(rollbackProducer ? "RAISE EXCEPTION 'Injected mark batch rollback';" : string.Empty)}}
    END IF;
    RETURN NULL;
END;
$test$;
CREATE TRIGGER {{functionName}} AFTER UPDATE ON public.paper_positions
FOR EACH STATEMENT EXECUTE FUNCTION public.{{functionName}}();
SELECT pg_advisory_lock(1809202604, {{lockKey}});
""", barrier))
                await setup.ExecuteNonQueryAsync();

            Task<IReadOnlyList<PaperPosition>>? producer = null;
            try
            {
                await RunWhileProjectionPausedAsync(factory, strategyId,
                    async consumer => Assert.Equal(4, (await consumer.ApplyPendingEventsAsync(4)).EventsApplied),
                    async () =>
                    {
                        producer = new PostgresAppRepository(WithApplicationName(factory, producerName))
                            .TryUpdatePaperPositionMarksAsync(marks);
                        // AFTER STATEMENT is reached only after the real row triggers
                        // have coalesced both events; the statement is not committed.
                        await WaitForTestBlockerAsync(factory, producerName, barrier.ProcessID, producer);
                        await UpdateAckFixturePositionAsync(factory, positionIds[3], 4m);
                    },
                    afterRelease: async (consumer, consumerName) =>
                    {
                        try
                        {
                            await AssertConsumerCompletesWithoutProducerWaitAsync(factory, consumerName, producerName, consumer);
                            Assert.False(producer!.IsCompleted);
                            await using var check = factory.CreateConnection();
                            await check.OpenAsync();
                            await using var remaining = new NpgsqlCommand("""
SELECT count(*)::integer,
       count(*) FILTER (WHERE pending.source_kind = 'PaperOrder')::integer,
       count(*) FILTER (WHERE pending.source_id = @OutsideId)::integer,
       count(*) FILTER (WHERE position.asset_id = ANY(@Assets))::integer
FROM dashboard_projection_events pending
LEFT JOIN paper_positions position ON position.id = pending.source_id
WHERE pending.strategy_id = @StrategyId;
""", check);
                            remaining.Parameters.AddWithValue("StrategyId", strategyId);
                            remaining.Parameters.AddWithValue("OutsideId", positionIds[3]);
                            remaining.Parameters.AddWithValue("Assets", markedAssets);
                            await using var reader = await remaining.ExecuteReaderAsync();
                            Assert.True(await reader.ReadAsync());
                            Assert.Equal(3, reader.GetInt32(0)); // two busy plus the unselected event
                            Assert.Equal(0, reader.GetInt32(1)); // non-position acknowledged once
                            Assert.Equal(1, reader.GetInt32(2));
                            Assert.Equal(2, reader.GetInt32(3));
                        }
                        finally
                        {
                            // Also release on the old-code regression failure so
                            // the real consumer can exit before fixture cleanup.
                            await using var release = new NpgsqlCommand(
                                $"SELECT pg_advisory_unlock(1809202604, {lockKey});", barrier);
                            await release.ExecuteScalarAsync();
                            await consumer.WaitAsync(TimeSpan.FromSeconds(10));
                        }
                    });
            }
            finally
            {
                await using (var release = new NpgsqlCommand(
                    $"SELECT pg_advisory_unlock(1809202604, {lockKey});", barrier))
                    await release.ExecuteScalarAsync();
                try
                {
                    if (producer is not null)
                    {
                        if (rollbackProducer)
                        {
                            var error = await Assert.ThrowsAsync<PostgresException>(async () => await producer);
                            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
                        }
                        else
                            Assert.Equal(2, (await producer.WaitAsync(TimeSpan.FromSeconds(10))).Count);
                    }
                }
                finally
                {
                    await using var cleanup = new NpgsqlCommand($"""
DROP TRIGGER {functionName} ON public.paper_positions;
DROP FUNCTION public.{functionName}();
""", barrier);
                    await cleanup.ExecuteNonQueryAsync();
                }
            }
            Assert.Equal(3, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
            Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
            await AssertRawAndProjectedAccountingAsync(factory, strategyId);
            var actual = await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId);
            Assert.Equal(rollbackProducer ? 7m : 9m, actual.UnrealizedPnlUsd);
            Assert.Equal(4, actual.FeeRequiredOpenPositionCount);
            Assert.Equal(unknownNet ? 3 : 4, actual.FeeAccountedOpenPositionCount);
            Assert.Equal(unknownNet, actual.NetUnrealizedPnlUsd is null);
            Assert.Equal(unknownNet, actual.NetRealizedPnlUsd is null);
            Assert.Equal(1, actual.FeeRequiredSettledCount);
            Assert.Equal(unknownNet ? 0 : 1, actual.FeeAccountedSettledCount);
        }
    }

    private static async Task WaitForTestBlockerAsync(
        PostgresConnectionFactory factory, string applicationName, int blockerPid, Task task)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
SELECT EXISTS (SELECT 1 FROM pg_stat_activity
WHERE application_name = @Name AND @Blocker = ANY(pg_blocking_pids(pid)));
""", connection);
            command.Parameters.AddWithValue("Name", applicationName);
            command.Parameters.AddWithValue("Blocker", blockerPid);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) return;
            if (task.IsCompleted)
            {
                await task;
                Assert.Fail("Task completed without reaching its test-only database barrier.");
            }
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task AssertConsumerCompletesWithoutProducerWaitAsync(
        PostgresConnectionFactory factory, string consumerName, string producerName, Task consumer)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!consumer.IsCompleted)
        {
            await using var command = new NpgsqlCommand("""
SELECT consumer.query FROM pg_stat_activity consumer
JOIN pg_stat_activity producer ON producer.pid = ANY(pg_blocking_pids(consumer.pid))
WHERE consumer.application_name = @Consumer AND producer.application_name = @Producer
  AND consumer.wait_event_type = 'Lock';
""", connection);
            command.Parameters.AddWithValue("Consumer", consumerName);
            command.Parameters.AddWithValue("Producer", producerName);
            var waitingSql = await command.ExecuteScalarAsync(timeout.Token) as string;
            Assert.True(waitingSql is null,
                $"Consumer waits on the still-uncommitted producer at final acknowledgement: {waitingSql}");
            await Task.Delay(10, timeout.Token);
        }
        await consumer;
    }

    private static async Task UpdateAckFixturePositionAsync(
        PostgresConnectionFactory factory, Guid id, decimal gross)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
UPDATE paper_positions SET unrealized_pnl_usd = @Gross,
    estimated_value_usd = average_price * size_shares + @Gross,
    net_unrealized_pnl_usd = CASE WHEN fee_accounting_status = 'Calculated' THEN @Gross - fee_usd END,
    updated_at_utc = clock_timestamp() WHERE id = @Id;
""", connection);
        command.Parameters.AddWithValue("Id", id);
        command.Parameters.AddWithValue("Gross", gross);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task AssertRawAndProjectedAccountingAsync(PostgresConnectionFactory factory, Guid strategyId)
    {
        var raw = new PostgresAppRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var expected = (await raw.GetStrategyPerformanceAsync()).Single(row => row.StrategyId == strategyId);
        var actual = await ReadSnapshotAsync(snapshots, strategyId);
        AssertStrategyMetricsEqual(expected, actual);
        // The legacy raw repository returns gross metrics only. Calculate Net
        // independently from this fixture's raw positions and settled run, not
        // from projection state or DashboardProjectionCalculator.
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
WITH positions AS (
    SELECT *, fee_accounting_status = 'Calculated' AND fee_usd >= 0
        AND net_unrealized_pnl_usd = unrealized_pnl_usd - fee_usd AS accounted
    FROM paper_positions WHERE copied_trader_wallet = @Wallet AND size_shares > 0
), runs AS (
    SELECT *, fee_accounting_status = 'Calculated' AND fee_usd >= 0
        AND net_realized_pnl_usd = realized_pnl_usd - fee_usd AS accounted
    FROM strategy_market_paper_runs WHERE strategy_id = @Id AND status = 'Settled'
)
SELECT p.*, r.* FROM (
    SELECT count(*)::integer AS required, count(*) FILTER (WHERE accounted)::integer AS known,
           COALESCE(sum(fee_usd) FILTER (WHERE accounted), 0) AS fees,
           CASE WHEN count(*) = count(*) FILTER (WHERE accounted)
                THEN sum(net_unrealized_pnl_usd) END AS net,
           sum(size_shares * average_price) AS cost
    FROM positions
) p CROSS JOIN (
    SELECT count(*)::integer AS required, count(*) FILTER (WHERE accounted)::integer AS known,
           COALESCE(sum(fee_usd) FILTER (WHERE accounted), 0) AS fees,
           CASE WHEN count(*) = count(*) FILTER (WHERE accounted)
                THEN sum(net_realized_pnl_usd) END AS net,
           sum(stake_usd) AS cost,
           count(*) FILTER (WHERE settled_at_utc >= clock_timestamp() - interval '1 hour')::integer AS recent
    FROM runs
) r;
""", connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Wallet", $"strategy:projection_test_{strategyId:N}");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var openRequired = reader.GetInt32(0);
        var openKnown = reader.GetInt32(1);
        var openFees = reader.GetDecimal(2);
        decimal? openNet = reader.IsDBNull(3) ? null : reader.GetDecimal(3);
        var openCost = reader.GetDecimal(4);
        var closedRequired = reader.GetInt32(5);
        var closedKnown = reader.GetInt32(6);
        var closedFees = reader.GetDecimal(7);
        decimal? closedNet = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
        var closedCost = reader.GetDecimal(9);
        Assert.Equal(4, openRequired);
        Assert.Equal(1, closedRequired);
        Assert.Equal(closedRequired, reader.GetInt32(10)); // same run lies in all three recent windows
        Assert.Equal(closedNet, actual.NetRealizedPnlUsd);
        Assert.Equal(openNet, actual.NetUnrealizedPnlUsd);
        Assert.Equal(closedNet + openNet, actual.NetTotalPnlUsd);
        AssertAckNullableDecimalEqual((closedNet + openNet) * 100m / (closedCost + openCost + openFees + closedFees), actual.NetRoiPct);
        AssertAckNullableDecimalEqual(closedNet * 100m / (closedCost + closedFees), actual.NetClosedRoiPct);
        Assert.Equal(openFees + closedFees, actual.AccountedFeeUsd);
        Assert.Equal(openRequired, actual.FeeRequiredOpenPositionCount);
        Assert.Equal(openKnown, actual.FeeAccountedOpenPositionCount);
        Assert.Equal(closedRequired, actual.FeeRequiredSettledCount);
        Assert.Equal(closedKnown, actual.FeeAccountedSettledCount);
        var rawRecent = (await raw.GetStrategyRecentPerformanceAsync())
            .Where(row => row.StrategyId == strategyId).OrderBy(row => row.WindowHours).ToArray();
        var recent = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
            .Where(row => row.StrategyId == strategyId).OrderBy(row => row.WindowHours).ToArray();
        Assert.Equal(3, recent.Length);
        Assert.Equal(3, rawRecent.Length);
        for (var index = 0; index < recent.Length; index++)
        {
            AssertRecentMetricsEqual(rawRecent[index], recent[index]);
            Assert.Equal(closedNet, recent[index].NetRealizedPnlUsd);
            AssertAckNullableDecimalEqual(closedNet * 100m / (closedCost + closedFees), recent[index].NetRoiPct);
            Assert.Equal(closedFees, recent[index].AccountedFeeUsd);
            Assert.Equal(closedRequired, recent[index].FeeRequiredSettledCount);
            Assert.Equal(closedKnown, recent[index].FeeAccountedSettledCount);
        }
    }

    private static void AssertAckNullableDecimalEqual(decimal? expected, decimal? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
            AssertSnapshotDecimalEqual(expected.Value, actual!.Value);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_PositionProducerCommitsDuringProjectionAndNewVersionSurvives()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, strategyId);
        var positionId = await InsertPaperPositionAsync(
            factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
        await projection.BootstrapAsync();
        await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);

        await RunWhileProjectionPausedAsync(
            factory,
            strategyId,
            async consumer => Assert.Equal(1, (await consumer.ApplyPendingEventsAsync(1000)).EventsApplied),
            () => UpdatePaperPositionAsync(
                WithApplicationName(factory, "position_producer", "-c lock_timeout=250ms -c statement_timeout=2000"),
                positionId, 2m, DateTimeOffset.UtcNow));

        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, positionId));
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
        Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, positionId));
        Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
        var actual = await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId);
        var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync())
            .Single(row => row.StrategyId == strategyId);
        AssertStrategyMetricsEqual(expected, actual);
        Assert.Equal(2m, actual.UnrealizedPnlUsd);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_ReconciliationProducerCommitsDuringRebuildAndNewRequestSurvives()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, strategyId);
        await projection.BootstrapAsync();
        await QueueReconciliationAsync(factory, strategyId);

        await RunWhileProjectionPausedAsync(
            factory,
            strategyId,
            async consumer =>
            {
                var result = await consumer.ReconcileNextStrategyAsync();
                Assert.Null(result.Error);
                Assert.True(result.Reconciled);
                Assert.Equal(strategyId, result.StrategyId);
            },
            () => QueueReconciliationAsync(
                WithApplicationName(factory, "reconciliation_producer", "-c lock_timeout=250ms -c statement_timeout=2000"),
                strategyId));

        Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, strategyId));
        var next = await projection.ReconcileNextStrategyAsync();
        Assert.Null(next.Error);
        Assert.Equal(strategyId, next.StrategyId);
        Assert.Equal(0, await ReadReconciliationRequestCountAsync(factory, strategyId));
        var actual = await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId);
        var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync())
            .Single(row => row.StrategyId == strategyId);
        AssertStrategyMetricsEqual(expected, actual);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_PositionDeleteReinsertAndConsumerRollbackPreserveLatestState()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        foreach (var scenario in new[] { "delete", "reinsert", "rollback" })
        {
            var projection = new PostgresDashboardProjectionRepository(factory);
            var strategyId = Guid.NewGuid();
            await InsertStrategyAsync(factory, strategyId);
            var positionId = await InsertPaperPositionAsync(
                factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
            await projection.BootstrapAsync();
            await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);

            await RunWhileProjectionPausedAsync(factory, strategyId,
                async consumer =>
                {
                    if (scenario == "rollback")
                    {
                        var error = await Assert.ThrowsAsync<PostgresException>(
                            () => consumer.ApplyPendingEventsAsync(1000));
                        Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
                    }
                    else
                    {
                        Assert.Equal(1, (await consumer.ApplyPendingEventsAsync(1000)).EventsApplied);
                    }
                },
                async () =>
                {
                    var producer = WithApplicationName(factory, "position_edge_producer",
                        "-c lock_timeout=250ms -c statement_timeout=2000");
                    if (scenario == "rollback")
                    {
                        await UpdatePaperPositionAsync(producer, positionId, 3m, DateTimeOffset.UtcNow);
                        return;
                    }
                    await using var connection = producer.CreateConnection();
                    await connection.OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();
                    await using (var delete = new NpgsqlCommand(
                        "DELETE FROM paper_positions WHERE id = @Id;", connection, transaction))
                    {
                        delete.Parameters.AddWithValue("Id", positionId);
                        Assert.Equal(1, await delete.ExecuteNonQueryAsync());
                    }
                    if (scenario == "reinsert")
                    {
                        await using var insert = new NpgsqlCommand("""
INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares,
    average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc)
VALUES (@Id, @Wallet, @Asset, @Condition, 'Up', 3, 0.5, 4.5, 3, clock_timestamp());
""", connection, transaction);
                        insert.Parameters.AddWithValue("Id", positionId);
                        insert.Parameters.AddWithValue("Wallet", $"strategy:projection_test_{strategyId:N}");
                        insert.Parameters.AddWithValue("Asset", $"reinsert-{positionId:N}");
                        insert.Parameters.AddWithValue("Condition", $"reinsert-{positionId:N}");
                        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
                    }
                    await transaction.CommitAsync();
                },
                failConsumer: scenario == "rollback");

            Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, positionId));
            await projection.ApplyPendingEventsAsync(1000);
            Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, positionId));
            Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
            var actual = await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId);
            var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            AssertStrategyMetricsEqual(expected, actual);
            Assert.Equal(scenario == "delete" ? 0m : 3m, actual.UnrealizedPnlUsd);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_CursorReconciliationDoesNotDeleteRequestCreatedDuringRebuild()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        await projection.BootstrapAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        // Reset only this isolated integration fixture's queue/cursor.
        await using var setup = new NpgsqlCommand("""
DELETE FROM dashboard_projection_reconciliation_queue;
UPDATE dashboard_projection_control SET reconciliation_cursor_strategy_id = NULL WHERE singleton_id = 1;
SELECT id FROM strategies ORDER BY id LIMIT 1;
""", connection);
        var strategyId = (Guid)(await setup.ExecuteScalarAsync())!;
        await RunWhileProjectionPausedAsync(factory, strategyId,
            async consumer =>
            {
                var result = await consumer.ReconcileNextStrategyAsync();
                Assert.Null(result.Error);
                Assert.Equal(strategyId, result.StrategyId);
            },
            () => QueueReconciliationAsync(
                WithApplicationName(factory, "cursor_request_producer", "-c lock_timeout=250ms -c statement_timeout=2000"),
                strategyId));
        Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, strategyId));
        Assert.Equal(strategyId, (await projection.ReconcileNextStrategyAsync()).StrategyId);
        Assert.Equal(0, await ReadReconciliationRequestCountAsync(factory, strategyId));
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_ReconciliationPreservesPositionEventUpdatedDuringRebuild()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, strategyId);
        var positionId = await InsertPaperPositionAsync(
            factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
        await projection.BootstrapAsync();
        // This append-only delta is already included in the rebuilt snapshot;
        // retaining it when the position acknowledgement conflicts doubles it.
        var orderId = Guid.NewGuid();
        await InsertPaperOrderAsync(factory, orderId, strategyId, DateTimeOffset.UtcNow);
        await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);
        await QueueReconciliationAsync(factory, strategyId);
        await RunWhileProjectionPausedAsync(factory, strategyId,
            async consumer =>
            {
                var result = await consumer.ReconcileNextStrategyAsync();
                Assert.Null(result.Error);
                Assert.Equal(strategyId, result.StrategyId);
            },
            () => UpdatePaperPositionAsync(
                WithApplicationName(factory, "rebuild_position_producer", "-c lock_timeout=250ms -c statement_timeout=2000"),
                positionId, 2m, DateTimeOffset.UtcNow));
        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, positionId));
        await projection.ApplyPendingEventsAsync(1000);
        Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, positionId));
        var actual = await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId);
        var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync())
            .Single(row => row.StrategyId == strategyId);
        AssertStrategyMetricsEqual(expected, actual);
        Assert.Equal(1, actual.OrdersCount);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_MissingPositionFactAndConcurrentBindingChangeAreReplayedOnce()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var originalStrategy = Guid.NewGuid();
        var nextStrategy = Guid.NewGuid();
        await InsertStrategyAsync(factory, originalStrategy);
        await InsertStrategyAsync(factory, nextStrategy);
        await projection.BootstrapAsync();
        // The insert has no stored fact when the consumer reads it.
        var positionId = await InsertPaperPositionAsync(
            factory, $"projection_test_{originalStrategy:N}", DateTimeOffset.UtcNow);
        await RunWhileProjectionPausedAsync(factory, originalStrategy,
            async consumer => Assert.Equal(1, (await consumer.ApplyPendingEventsAsync(1000)).EventsApplied),
            async () =>
            {
                var producer = WithApplicationName(factory, "binding_producer",
                    "-c lock_timeout=250ms -c statement_timeout=2000");
                await UpdatePaperPositionAsync(producer, positionId, 2m, DateTimeOffset.UtcNow);
                await using var connection = producer.CreateConnection();
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
UPDATE paper_positions SET copied_trader_wallet = @Wallet, unrealized_pnl_usd = 3,
    updated_at_utc = clock_timestamp() WHERE id = @Id;
""", connection);
                command.Parameters.AddWithValue("Wallet", $"strategy:projection_test_{nextStrategy:N}");
                command.Parameters.AddWithValue("Id", positionId);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            });
        Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, positionId));
        Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
        Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, originalStrategy));
        Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, nextStrategy));
        var reconciled = new HashSet<Guid>();
        for (var i = 0; i < 2; i++)
        {
            var result = await projection.ReconcileNextStrategyAsync();
            Assert.Null(result.Error);
            reconciled.Add(result.StrategyId!.Value);
        }
        Assert.True(reconciled.SetEquals([originalStrategy, nextStrategy]));
        Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
        var raw = await new PostgresAppRepository(factory).GetStrategyPerformanceAsync();
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        foreach (var strategyId in new[] { originalStrategy, nextStrategy })
        {
            AssertStrategyMetricsEqual(raw.Single(row => row.StrategyId == strategyId),
                await ReadSnapshotAsync(snapshots, strategyId));
        }
        Assert.Equal(0m, (await ReadSnapshotAsync(snapshots, originalStrategy)).UnrealizedPnlUsd);
        Assert.Equal(3m, (await ReadSnapshotAsync(snapshots, nextStrategy)).UnrealizedPnlUsd);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_ReconciliationAcknowledgementsRespectLockTimeoutWithoutLosingWork()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        foreach (var positionConflict in new[] { false, true })
        {
            var projection = new PostgresDashboardProjectionRepository(factory);
            var strategyId = Guid.NewGuid();
            await InsertStrategyAsync(factory, strategyId);
            var positionId = await InsertPaperPositionAsync(
                factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
            await projection.BootstrapAsync();
            await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);
            await QueueReconciliationAsync(factory, strategyId);
            await using var producer = factory.CreateConnection();
            await producer.OpenAsync();
            await using var producerTransaction = await producer.BeginTransactionAsync();
            await RunWhileProjectionPausedAsync(factory, strategyId,
                async consumer =>
                {
                    var result = await consumer.ReconcileNextStrategyAsync();
                    Assert.Null(result.Error);
                    Assert.True(result.Reconciled);
                    Assert.Equal(strategyId, result.StrategyId);
                },
                async () =>
                {
                    await using var command = new NpgsqlCommand(positionConflict
                        ? "UPDATE paper_positions SET unrealized_pnl_usd = 2, updated_at_utc = clock_timestamp() WHERE id = @Id;"
                        : "UPDATE dashboard_projection_reconciliation_queue SET reason = 'pending_producer' WHERE strategy_id = @Id;",
                        producer, producerTransaction) { CommandTimeout = 2 };
                    command.Parameters.AddWithValue("Id", positionConflict ? positionId : strategyId);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync());
                    // Keep this producer's tuple lock until AFTER the consumer
                    // commits. Its real 250ms acknowledgement timeout must apply.
                });
            Assert.Equal(1m, (await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId)).UnrealizedPnlUsd);
            await producerTransaction.CommitAsync();
            if (positionConflict)
            {
                Assert.Equal(1, await ReadPaperPositionEventCountAsync(factory, positionId));
                await projection.ApplyPendingEventsAsync(1000);
                Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, positionId));
            }
            else
            {
                Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, strategyId));
                Assert.Null((await projection.ReconcileNextStrategyAsync()).Error);
                Assert.Equal(0, await ReadReconciliationRequestCountAsync(factory, strategyId));
            }
            var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            AssertStrategyMetricsEqual(expected,
                await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId));
        }
    }

    // A test-only trigger pauses the real consumer after it has read its queue,
    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_EventLimitDoesNotLockUnselectedRowsAndBackfillsLockedRows()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, strategyId);
        var positionId = await InsertPaperPositionAsync(factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
        await projection.BootstrapAsync();
        await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);
        var orderId = Guid.NewGuid();
        await InsertPaperOrderAsync(factory, orderId, strategyId, DateTimeOffset.UtcNow);
        await using var observer = factory.CreateConnection();
        await observer.OpenAsync();
        await RunWhileProjectionPausedAsync(factory, strategyId,
            async consumer => Assert.Equal(1, (await consumer.ApplyPendingEventsAsync(1)).EventsApplied),
            async () =>
            {
                await using var command = new NpgsqlCommand("""
SELECT id FROM dashboard_projection_events
WHERE source_kind = 'PaperOrder' AND source_id = @Id FOR UPDATE NOWAIT;
""", observer);
                command.Parameters.AddWithValue("Id", orderId);
                Assert.IsType<long>(await command.ExecuteScalarAsync());
            });
        await UpdatePaperPositionAsync(factory, positionId, 2m, DateTimeOffset.UtcNow);
        await using (var transaction = await observer.BeginTransactionAsync())
        {
            await using var command = new NpgsqlCommand("""
SELECT id FROM dashboard_projection_events
WHERE source_kind = 'PaperOrder' AND source_id = @Id FOR UPDATE;
""", observer, transaction);
            command.Parameters.AddWithValue("Id", orderId);
            Assert.IsType<long>(await command.ExecuteScalarAsync());
            var result = await projection.ApplyPendingEventsAsync(1).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, result.EventsApplied);
            Assert.Equal(0, await ReadPaperPositionEventCountAsync(factory, positionId));
            await transaction.CommitAsync();
        }
        Assert.Equal(1, (await projection.ApplyPendingEventsAsync(1)).EventsApplied);
        Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1)).EventsApplied);
        var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync()).Single(row => row.StrategyId == strategyId);
        AssertStrategyMetricsEqual(expected, await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId));
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LockContention_MissingExistingPositionFactReconcilesWithConcurrentUpdate()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var strategyId = Guid.NewGuid();
        await InsertStrategyAsync(factory, strategyId);
        var positionId = await InsertPaperPositionAsync(factory, $"projection_test_{strategyId:N}", DateTimeOffset.UtcNow);
        await projection.BootstrapAsync();
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "DELETE FROM dashboard_strategy_position_projection_facts WHERE source_id = @Id;", connection);
            command.Parameters.AddWithValue("Id", positionId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await UpdatePaperPositionAsync(factory, positionId, 1m, DateTimeOffset.UtcNow);
        Assert.Equal(0, (await projection.ApplyPendingEventsAsync(1000)).EventsApplied);
        Assert.Equal(1, await ReadReconciliationRequestCountAsync(factory, strategyId));
        await RunWhileProjectionPausedAsync(factory, strategyId,
            async consumer => Assert.Null((await consumer.ReconcileNextStrategyAsync()).Error),
            () => UpdatePaperPositionAsync(factory, positionId, 2m, DateTimeOffset.UtcNow));
        await projection.ApplyPendingEventsAsync(1000);
        var expected = (await new PostgresAppRepository(factory).GetStrategyPerformanceAsync()).Single(row => row.StrategyId == strategyId);
        AssertStrategyMetricsEqual(expected, await ReadSnapshotAsync(new PostgresDashboardSnapshotRepository(factory), strategyId));
    }

    // A test-only trigger pauses the real consumer after it has read its queue,
    // during snapshot persistence. The optional afterRelease callback can check
    // consumer completion while a separately paused producer still holds locks.
    // No product test hook or timing-only synchronization is used.
    internal static async Task RunWhileProjectionPausedAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Func<PostgresDashboardProjectionRepository, Task> consume,
        Func<Task> produce,
        bool failConsumer = false,
        Func<Task, string, Task>? afterRelease = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var functionName = $"pct_lock_test_{suffix}";
        var consumerName = $"projection_consumer_{suffix}";
        var lockKey = BitConverter.ToInt32(Guid.NewGuid().ToByteArray());
        await using var barrier = factory.CreateConnection();
        await barrier.OpenAsync();
        await using (var setup = new NpgsqlCommand($$"""
CREATE FUNCTION public.{{functionName}}() RETURNS trigger LANGUAGE plpgsql AS $test$
DECLARE previous_lock_timeout text := current_setting('lock_timeout');
BEGIN
    PERFORM set_config('lock_timeout', '0', true);
    PERFORM pg_advisory_xact_lock(1809202603, {{lockKey}});
    PERFORM set_config('lock_timeout', previous_lock_timeout, true);
    {{(failConsumer ? "RAISE EXCEPTION 'Injected snapshot rollback';" : string.Empty)}}
    RETURN NEW;
END;
$test$;
CREATE TRIGGER {{functionName}}
BEFORE INSERT OR UPDATE ON public.dashboard_strategy_performance_snapshots
FOR EACH ROW WHEN (NEW.strategy_id = '{{strategyId}}'::uuid)
EXECUTE FUNCTION public.{{functionName}}();
SELECT pg_advisory_lock(1809202603, {{lockKey}});
""", barrier))
        {
            await setup.ExecuteNonQueryAsync();
        }

        Task? consumerTask = null;
        try
        {
            consumerTask = consume(new PostgresDashboardProjectionRepository(
                WithApplicationName(factory, consumerName)));
            await using var observer = factory.CreateConnection();
            await observer.OpenAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var check = new NpgsqlCommand("""
SELECT EXISTS (
    SELECT 1 FROM pg_stat_activity
    WHERE application_name = @Name AND @Blocker = ANY(pg_blocking_pids(pid)));
""", observer);
                check.Parameters.AddWithValue("Name", consumerName);
                check.Parameters.AddWithValue("Blocker", barrier.ProcessID);
                if ((bool)(await check.ExecuteScalarAsync(timeout.Token))!)
                {
                    break;
                }
                if (consumerTask.IsCompleted)
                {
                    await consumerTask;
                    Assert.Fail("Consumer completed without reaching the snapshot barrier.");
                }
                await Task.Delay(10, timeout.Token);
            }
            await produce().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(consumerTask.IsCompleted);
        }
        finally
        {
            await using (var release = new NpgsqlCommand(
                $"SELECT pg_advisory_unlock(1809202603, {lockKey});", barrier))
            {
                await release.ExecuteScalarAsync();
            }
            try
            {
                if (consumerTask is not null)
                {
                    if (afterRelease is not null)
                        await afterRelease(consumerTask, consumerName);
                    await consumerTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            finally
            {
                await using var cleanup = new NpgsqlCommand($"""
DROP TRIGGER {functionName} ON public.dashboard_strategy_performance_snapshots;
DROP FUNCTION public.{functionName}();
""", barrier);
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    private static PostgresConnectionFactory WithApplicationName(
        PostgresConnectionFactory factory, string applicationName, string? options = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            ApplicationName = applicationName
        };
        if (options is not null)
        {
            builder.Options = options;
        }
        return new PostgresConnectionFactory(new StorageOptions { ConnectionString = builder.ConnectionString });
    }

    private static async Task<int> ReadReconciliationRequestCountAsync(
        PostgresConnectionFactory factory, Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_BootstrapDeltaAndReconciliation_MatchRawAggregates()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var rawRepository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"projection_test_{strategyId:N}";
        await InsertStrategyAsync(factory, strategyId);
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
        Assert.True(reconciliation.PaperPositionsBuildSequentialScans is >= 0);
        Assert.True(reconciliation.PaperPositionsBuildSequentialTuplesRead is >= 0);
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
        Assert.Equal((0, 0, 0, 0, 0, 0, 0), deletedCounts);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ApplyPendingEvents_MultipleEventsForSameRun_PersistsOnlyFinalFacts()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
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

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ExpireRecentFacts_SkipsLockedOldestFactAndBackfillsBatch()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
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

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Bootstrap_V1OnlyV2OnlyAndMixedArchivesHaveIdenticalLifetimeAndRecentWindows()
    {
        var factory = await DisposablePostgresIntegrationGuard.CreateInitializedFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var capturedNowUtc = DateTimeOffset.UtcNow;
        var alignedNowUtc = capturedNowUtc.AddTicks(
            -(capturedNowUtc.Ticks % TimeSpan.TicksPerSecond));
        var updatedAtUtc = new[]
        {
            alignedNowUtc.AddMinutes(-30),
            alignedNowUtc.AddHours(-3),
            alignedNowUtc.AddHours(-12),
            alignedNowUtc.AddHours(-25)
        };
        const string skipReason = "dashboard_archive_window_skip";
        var marketFixtureKey = Guid.NewGuid().ToString("N");
        var cohorts = new[]
        {
            (
                StrategyId: Guid.NewGuid(),
                ArchiveVersions: new short[] { 1, 1, 1, 1 }),
            (
                StrategyId: Guid.NewGuid(),
                ArchiveVersions: new short[] { 2, 2, 2, 2 }),
            (
                StrategyId: Guid.NewGuid(),
                ArchiveVersions: new short[] { 2, 1, 2, 1 })
        };
        var runsByStrategy = new Dictionary<Guid, StrategyMarketPaperRun[]>();
        var insertedStrategyIds = new List<Guid>();

        try
        {
            foreach (var cohort in cohorts)
            {
                await InsertStrategyAsync(factory, cohort.StrategyId);
                insertedStrategyIds.Add(cohort.StrategyId);
                var runs = updatedAtUtc
                    .Select((timestamp, index) => CreateArchivedSkipRun(
                        cohort.StrategyId,
                        $"{marketFixtureKey}-{index}",
                        timestamp,
                        skipReason))
                    .ToArray();
                runsByStrategy.Add(cohort.StrategyId, runs);

                var v1Runs = runs
                    .Where((_, index) => cohort.ArchiveVersions[index] == 1)
                    .ToArray();
                if (v1Runs.Length > 0)
                {
                    var insertedV1 = await repository.TryAddStrategyMarketPaperRunsAsync(
                        v1Runs,
                        directPaperSkipCompactionEnabled: true);
                    Assert.Equal(
                        v1Runs.Select(run => run.Id).OrderBy(id => id).ToArray(),
                        insertedV1.OrderBy(id => id).ToArray());
                }

                var v2Runs = runs
                    .Where((_, index) => cohort.ArchiveVersions[index] == 2)
                    .ToArray();
                if (v2Runs.Length > 0)
                {
                    var insertedV2 = await repository
                        .TryAddStrategyMarketPaperRunsWithCompactSkipArchiveV2ForTestsAsync(v2Runs);
                    Assert.Equal(
                        v2Runs.Select(run => run.Id).OrderBy(id => id).ToArray(),
                        insertedV2.OrderBy(id => id).ToArray());
                }
            }

            await projection.BootstrapAsync();

            var cohortIds = cohorts.Select(cohort => cohort.StrategyId).ToHashSet();
            var dashboardLifetime = (await snapshots.GetStrategyPerformanceSnapshotAsync())
                .Where(row => cohortIds.Contains(row.StrategyId))
                .ToDictionary(row => row.StrategyId);
            var directLifetime = (await repository.GetStrategyPerformanceAsync())
                .Where(row => cohortIds.Contains(row.StrategyId))
                .ToDictionary(row => row.StrategyId);
            var dashboardRecent = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
                .Where(row => cohortIds.Contains(row.StrategyId))
                .GroupBy(row => row.StrategyId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(row => row.WindowHours));
            var directRecent = (await repository.GetStrategyRecentPerformanceAsync())
                .Where(row => cohortIds.Contains(row.StrategyId))
                .GroupBy(row => row.StrategyId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToDictionary(row => row.WindowHours));
            var baselineLifetime = dashboardLifetime[cohorts[0].StrategyId];
            var baselineRecent = dashboardRecent[cohorts[0].StrategyId];
            var expectedWindowCounts = new Dictionary<int, int>
            {
                [1] = 1,
                [6] = 2,
                [24] = 3
            };

            foreach (var cohort in cohorts)
            {
                var lifetime = dashboardLifetime[cohort.StrategyId];
                var directLifetimeRow = directLifetime[cohort.StrategyId];
                Assert.Equal(0, lifetime.ObservedRunsCount);
                Assert.Equal(0, lifetime.EnteredRunsCount);
                Assert.Equal(4, lifetime.SkippedRunsCount);
                Assert.Equal(4, lifetime.PaperConditionSkippedRunsCount);
                Assert.Equal(0, lifetime.PaperNotAcceptedRunsCount);
                Assert.Equal(updatedAtUtc[0], lifetime.LastRunUtc);
                AssertStrategyMetricsEqual(baselineLifetime, lifetime);
                AssertStrategyMetricsEqual(lifetime, directLifetimeRow);

                Assert.Equal(
                    [1, 6, 24],
                    dashboardRecent[cohort.StrategyId].Keys.OrderBy(value => value).ToArray());
                Assert.Equal(
                    [1, 6, 24],
                    directRecent[cohort.StrategyId].Keys.OrderBy(value => value).ToArray());
                foreach (var (windowHours, expectedCount) in expectedWindowCounts)
                {
                    var dashboardRow = dashboardRecent[cohort.StrategyId][windowHours];
                    var directRow = directRecent[cohort.StrategyId][windowHours];
                    Assert.Equal(expectedCount, dashboardRow.SkippedRunsCount);
                    Assert.Equal(expectedCount, dashboardRow.PaperConditionSkippedRunsCount);
                    Assert.Equal(0, dashboardRow.PaperNotAcceptedRunsCount);
                    Assert.Equal($"{skipReason}:{expectedCount}", dashboardRow.TopSkipReason);
                    Assert.Equal(updatedAtUtc[0], dashboardRow.LastRunUtc);
                    AssertRecentMetricsEqual(baselineRecent[windowHours], dashboardRow);
                    AssertRecentMetricsEqual(dashboardRow, directRow);
                    Assert.Equal(expectedCount, directRow.PaperConditionSkippedRunsCount);
                    Assert.Equal(0, directRow.PaperNotAcceptedRunsCount);
                    Assert.Equal(updatedAtUtc[0], directRow.LastRunUtc);
                }

                var counts = await ReadArchiveDashboardCountsAsync(factory, cohort.StrategyId);
                Assert.Equal(0, counts.RawRuns);
                Assert.Equal(
                    cohort.ArchiveVersions.LongCount(version => version == 1),
                    counts.V1Archives);
                Assert.Equal(
                    cohort.ArchiveVersions.LongCount(version => version == 2),
                    counts.V2Archives);
                Assert.Equal(4, counts.CanonicalArchives);
                Assert.Equal(4, counts.DistinctArchivedRunIds);
                Assert.Equal(4, counts.RollupRuns);
                Assert.Equal(6, counts.RecentFacts);
                Assert.Equal(3, counts.RunActivityFacts);
                Assert.Equal(3, counts.RunSkippedFacts);

                var sourceFactCounts = await ReadRecentStrategyRunFactCountsAsync(
                    factory,
                    cohort.StrategyId);
                Assert.Equal(3, sourceFactCounts.Count);
                foreach (var recentRun in runsByStrategy[cohort.StrategyId].Take(3))
                {
                    Assert.Equal(2, sourceFactCounts[recentRun.Id]);
                }

                Assert.DoesNotContain(
                    runsByStrategy[cohort.StrategyId][3].Id,
                    sourceFactCounts.Keys);
            }
        }
        finally
        {
            foreach (var strategyId in insertedStrategyIds)
            {
                await DeleteStrategyProjectionArtifactsAsync(factory, strategyId);
                await DeleteStrategyAsync(factory, strategyId);
                await DeleteStrategyProjectionArtifactsAsync(factory, strategyId);
                Assert.Equal(
                    (0, 0, 0, 0, 0, 0, 0),
                    await ReadDeletedStrategyProjectionCountsAsync(factory, strategyId));
            }
        }
    }

    private static StrategyMarketPaperRun CreateArchivedSkipRun(
        Guid strategyId,
        string marketFixtureKey,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        var detectedAtUtc = updatedAtUtc.AddMinutes(-10);
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            $"dashboard-archive-market-{marketFixtureKey}",
            $"dashboard-archive-condition-{marketFixtureKey}",
            $"dashboard-archive-market-{marketFixtureKey}",
            "Dashboard archive window integration test",
            "Test",
            updatedAtUtc.AddMinutes(-5),
            updatedAtUtc,
            detectedAtUtc,
            updatedAtUtc.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Skipped,
            null,
            null,
            null,
            6m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            skipReason,
            detectedAtUtc,
            updatedAtUtc);
    }

    private static async Task<ArchiveDashboardCounts> ReadArchiveDashboardCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_tombstones_v2 WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_archive_rows WHERE strategy_id = @StrategyId),
    (SELECT count(DISTINCT archived_run_id) FROM strategy_market_paper_skip_archive_rows
        WHERE strategy_id = @StrategyId),
    (SELECT COALESCE(sum(run_count), 0)::bigint FROM strategy_paper_skip_rollups
        WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_strategy_recent_projection_facts
        WHERE strategy_id = @StrategyId AND source_kind = @SourceKind),
    (SELECT count(*) FROM dashboard_strategy_recent_projection_facts
        WHERE strategy_id = @StrategyId AND source_kind = @SourceKind AND fact_kind = @RunActivity),
    (SELECT count(*) FROM dashboard_strategy_recent_projection_facts
        WHERE strategy_id = @StrategyId AND source_kind = @SourceKind AND fact_kind = @RunSkipped);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.StrategyRun);
        command.Parameters.AddWithValue("RunActivity", DashboardProjectionFactKinds.RunActivity);
        command.Parameters.AddWithValue("RunSkipped", DashboardProjectionFactKinds.RunSkipped);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ArchiveDashboardCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetInt64(8));
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> ReadRecentStrategyRunFactCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT source_id, count(*)::integer
FROM dashboard_strategy_recent_projection_facts
WHERE strategy_id = @StrategyId
  AND source_kind = @SourceKind
GROUP BY source_id
ORDER BY source_id;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.StrategyRun);
        var counts = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts.Add(reader.GetGuid(0), reader.GetInt32(1));
        }

        return counts;
    }

    private sealed record ArchiveDashboardCounts(
        long RawRuns,
        long V1Archives,
        long V2Archives,
        long CanonicalArchives,
        long DistinctArchivedRunIds,
        long RollupRuns,
        long RecentFacts,
        long RunActivityFacts,
        long RunSkippedFacts);

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

    private static async Task DeleteStrategyProjectionArtifactsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM dashboard_projection_events WHERE strategy_id = @Id;
DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @Id;
DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @Id;
DELETE FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @Id;
DELETE FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @Id;
DELETE FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @Id;
DELETE FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(
        int LifetimeSnapshots,
        int RecentSnapshots,
        int LifetimeStates,
        int RecentStates,
        int RecentFacts,
        int Events,
        int Queue)> ReadDeletedStrategyProjectionCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*)::integer FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_projection_events WHERE strategy_id = @Id),
    (SELECT count(*)::integer FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @Id);
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6));
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
