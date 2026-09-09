using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperCopiedTraderPerformancePostgresIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task PaperPositionsScanTelemetry_DetectsParallelAndCountsSerialSequentialScans()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        await using (var disableTriggers = new NpgsqlCommand(
            "ALTER TABLE public.paper_positions DISABLE TRIGGER USER;",
            connection,
            transaction))
        {
            await disableTriggers.ExecuteNonQueryAsync();
        }
        await using (var insert = new NpgsqlCommand(
            """
INSERT INTO public.paper_positions (
    id,
    copied_trader_wallet,
    asset_id,
    condition_id,
    outcome,
    size_shares,
    average_price,
    estimated_value_usd,
    unrealized_pnl_usd,
    updated_at_utc)
SELECT
    gen_random_uuid(),
    @Wallet,
    @AssetPrefix || value,
    @ConditionId,
    'YES',
    1,
    0.5,
    0.5,
    0,
    clock_timestamp()
FROM generate_series(1, 100000) value;
""",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("Wallet", $"scan-telemetry-{suffix}");
            insert.Parameters.AddWithValue("AssetPrefix", $"asset-{suffix}-");
            insert.Parameters.AddWithValue("ConditionId", $"condition-{suffix}");
            Assert.Equal(100_000, await insert.ExecuteNonQueryAsync());
        }

        await using (var analyze = new NpgsqlCommand(
            "ANALYZE public.paper_positions;",
            connection,
            transaction))
        {
            await analyze.ExecuteNonQueryAsync();
        }

        await using (var configure = new NpgsqlCommand(
            """
SET LOCAL max_parallel_workers_per_gather = 4;
SET LOCAL min_parallel_table_scan_size = 0;
SET LOCAL parallel_setup_cost = 0;
SET LOCAL parallel_tuple_cost = 0;
SET LOCAL parallel_leader_participation = off;
SET LOCAL enable_indexscan = off;
SET LOCAL enable_indexonlyscan = off;
SET LOCAL enable_bitmapscan = off;
""",
            connection,
            transaction))
        {
            await configure.ExecuteNonQueryAsync();
        }

        string scanPlan;
        await using (var explain = new NpgsqlCommand(
            "EXPLAIN (ANALYZE, COSTS OFF, SUMMARY OFF, TIMING OFF) SELECT count(*) FROM public.paper_positions;",
            connection,
            transaction))
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            var planLines = new List<string>();
            while (await reader.ReadAsync())
            {
                planLines.Add(reader.GetString(0));
            }

            scanPlan = string.Join(Environment.NewLine, planLines);
        }
        Assert.Contains("Parallel Seq Scan on paper_positions", scanPlan, StringComparison.Ordinal);
        Assert.Matches(@"Workers Launched: [1-9]\d*", scanPlan);

        var before = await PostgresPaperPositionsScanTelemetry.ReadAsync(
            connection,
            transaction,
            CancellationToken.None);
        long visibleRows;
        await using (var scan = new NpgsqlCommand(
            "SELECT count(*)::bigint FROM public.paper_positions;",
            connection,
            transaction))
        {
            visibleRows = Convert.ToInt64(await scan.ExecuteScalarAsync());
        }

        var after = await PostgresPaperPositionsScanTelemetry.ReadAsync(
            connection,
            transaction,
            CancellationToken.None);
        var parallelScanDelta = PostgresPaperPositionsScanStats.Delta(before, after);

        await using (var forceSerial = new NpgsqlCommand(
            "SET LOCAL max_parallel_workers_per_gather = 0;",
            connection,
            transaction))
        {
            await forceSerial.ExecuteNonQueryAsync();
        }
        var beforeSerialScan = await PostgresPaperPositionsScanTelemetry.ReadAsync(
            connection,
            transaction,
            CancellationToken.None);
        await using (var serialScan = new NpgsqlCommand(
            "SELECT count(*)::bigint FROM public.paper_positions;",
            connection,
            transaction))
        {
            Assert.Equal(visibleRows, Convert.ToInt64(await serialScan.ExecuteScalarAsync()));
        }
        var afterSerialScan = await PostgresPaperPositionsScanTelemetry.ReadAsync(
            connection,
            transaction,
            CancellationToken.None);
        var afterStatsRead = await PostgresPaperPositionsScanTelemetry.ReadAsync(
            connection,
            transaction,
            CancellationToken.None);
        var serialScanDelta = PostgresPaperPositionsScanStats.Delta(
            beforeSerialScan,
            afterSerialScan);
        var statsReadDelta = PostgresPaperPositionsScanStats.Delta(
            afterSerialScan,
            afterStatsRead);

        Assert.True(visibleRows >= 100_000);
        Assert.True(parallelScanDelta.HasValue);
        Assert.True(parallelScanDelta.Value.SequentialScans >= 1);
        Assert.Equal(0, parallelScanDelta.Value.SequentialTuplesRead);
        Assert.True(serialScanDelta.HasValue);
        Assert.True(serialScanDelta.Value.SequentialScans >= 1);
        Assert.True(serialScanDelta.Value.SequentialTuplesRead >= visibleRows);
        Assert.True(statsReadDelta.HasValue);
        Assert.Equal(new PostgresPaperPositionsScanStats(0, 0), statsReadDelta.Value);

        await transaction.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_OpenOnlyAggregate_PreservesClosedHistoryAndUsesPartialWalletIndex()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallet = $"paper-performance-open-only-{suffix}";
        var closedOnlyWallet = $"paper-performance-closed-only-{suffix}";
        var wallets = new[] { wallet, closedOnlyWallet };
        var adversarialClosedAssetId = $"closed-adversarial-{suffix}";
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            await InsertOpenOnlyAggregateFixtureAsync(
                factory,
                wallet,
                closedOnlyWallet,
                adversarialClosedAssetId,
                suffix);
            await AnalyzePaperPositionsAsync(factory);

            var before = await ReadOpenOnlyAggregateFixtureStateAsync(
                factory,
                wallet,
                adversarialClosedAssetId);
            Assert.Equal(100_003L, before.TotalPositions);
            Assert.Equal(100_001L, before.ClosedPositions);
            Assert.Equal(2L, before.OpenPositions);
            Assert.True(before.AdversarialClosedPositionPresent);

            var plan = await ExplainOpenWalletPositionLookupAsync(factory, wallets);
            Assert.Contains("ix_paper_positions_open_wallet", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan on paper_positions", plan, StringComparison.Ordinal);

            await QueueWalletsAsync(factory, wallets, int.MaxValue, "open_only_aggregate_test");
            await SetControlCursorToMaximumSourceWalletAsync(factory);

            var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 2,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);

            Assert.True(result.LockAcquired);
            Assert.Equal(2, result.HighPriorityWalletsProcessed);
            Assert.Equal(2, result.WalletsProcessed);
            Assert.Equal(2, result.PerformanceRowsWritten);
            Assert.True(result.PaperPositionsAggregationSequentialScans.HasValue);
            Assert.Equal(0L, result.PaperPositionsAggregationSequentialScans.Value);
            Assert.True(result.PaperPositionsAggregationSequentialTuplesRead.HasValue);
            Assert.Equal(0L, result.PaperPositionsAggregationSequentialTuplesRead.Value);

            var overall = Assert.IsType<PaperCopiedTraderPerformance>(
                await repository.GetPaperCopiedTraderPerformanceAsync(wallet, "OVERALL"));
            var unknown = Assert.IsType<PaperCopiedTraderPerformance>(
                await repository.GetPaperCopiedTraderPerformanceAsync(wallet, "unknown"));
            AssertOpenOnlyAggregatePerformance(overall, wallet, "OVERALL");
            AssertOpenOnlyAggregatePerformance(unknown, wallet, "unknown");

            Assert.Null(await repository.GetPaperCopiedTraderPerformanceAsync(closedOnlyWallet, "OVERALL"));
            Assert.Null(await repository.GetPaperCopiedTraderPerformanceAsync(closedOnlyWallet, "unknown"));

            var after = await ReadOpenOnlyAggregateFixtureStateAsync(
                factory,
                wallet,
                adversarialClosedAssetId);
            Assert.Equal(before, after);

            var closedOnlyState = await ReadOpenOnlyAggregateFixtureStateAsync(
                factory,
                closedOnlyWallet,
                $"closed-only-{suffix}");
            Assert.Equal(1L, closedOnlyState.TotalPositions);
            Assert.Equal(1L, closedOnlyState.ClosedPositions);
            Assert.Equal(0L, closedOnlyState.OpenPositions);
            Assert.True(closedOnlyState.AdversarialClosedPositionPresent);
        }
        finally
        {
            try
            {
                await DeleteOpenOnlyAggregateFixtureAsync(factory, wallets);
                await AnalyzePaperPositionsAsync(factory);
            }
            finally
            {
                await RestoreControlStateAsync(factory, controlState);
            }
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_BoundedBatchAndFillChanges_RecomputeExactWalletState()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var firstWallet = $"paper-performance-{suffix}-one";
        var secondWallet = $"paper-performance-{suffix}-two";
        var wallets = new[] { firstWallet, secondWallet };
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();
        var fillId = Guid.NewGuid();
        var secondFillId = Guid.NewGuid();
        var orderIds = new[] { firstOrderId, secondOrderId };
        var fillIds = new[] { fillId, secondFillId };
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            var createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            await InsertPaperOrderAsync(
                factory,
                firstOrderId,
                strategyId,
                firstWallet,
                $"asset-{suffix}-one",
                $"condition-{suffix}-one",
                createdAtUtc);
            await InsertPaperOrderAsync(
                factory,
                secondOrderId,
                strategyId,
                secondWallet,
                $"asset-{suffix}-two",
                $"condition-{suffix}-two",
                createdAtUtc.AddSeconds(1));

            Assert.Equal(2, await CountQueuedWalletsAsync(factory, wallets));
            await PromoteQueuedWalletsAsync(factory, firstWallet, secondWallet);
            await SetControlCursorToMaximumSourceWalletAsync(factory);
            var queueBefore = await CountAllQueuedWalletsAsync(factory);

            var bounded = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);

            Assert.True(bounded.LockAcquired);
            Assert.Equal(0, bounded.WalletsSeeded);
            Assert.Equal(1, bounded.WalletsProcessed);
            Assert.Equal(2, bounded.PerformanceRowsWritten);
            Assert.Equal(queueBefore - 1, bounded.QueueRemaining);
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, [secondWallet]));
            Assert.Empty(await ReadProjectionRowsAsync(factory, secondWallet));
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 0,
                buyCostUsd: 0m,
                realizedPnlUsd: 0m);

            await DeletePaperOrderAsync(factory, secondOrderId);
            await DeleteQueuedWalletAsync(factory, secondWallet);

            Assert.Equal(0, await CountQueuedWalletsAsync(factory, [firstWallet]));
            await InsertPaperFillAsync(factory, fillId, firstOrderId, 0.40m, 5m, 1.25m, createdAtUtc.AddSeconds(2));
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterInsert = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterInsert.WalletsProcessed);
            Assert.Equal(2, afterInsert.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 1,
                buyCostUsd: 2m,
                realizedPnlUsd: 1.25m);

            await InsertPaperFillAsync(factory, secondFillId, firstOrderId, 0.25m, 4m, -0.50m, createdAtUtc.AddSeconds(3));
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterSecondInsert = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterSecondInsert.WalletsProcessed);
            Assert.Equal(2, afterSecondInsert.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 2,
                buyCostUsd: 3m,
                realizedPnlUsd: 0.75m);

            await UpdatePaperFillAsync(factory, fillId, 0.50m, 6m, 3.50m);
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterUpdate = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterUpdate.WalletsProcessed);
            Assert.Equal(2, afterUpdate.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 2,
                buyCostUsd: 4m,
                realizedPnlUsd: 3m);

            await DeletePaperFillAsync(factory, fillId);
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterFillDelete = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterFillDelete.WalletsProcessed);
            Assert.Equal(2, afterFillDelete.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 1,
                buyCostUsd: 1m,
                realizedPnlUsd: -0.50m);

            await DeletePaperFillAsync(factory, secondFillId);
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterAllFillsDeleted = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterAllFillsDeleted.WalletsProcessed);
            Assert.Equal(2, afterAllFillsDeleted.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 0,
                buyCostUsd: 0m,
                realizedPnlUsd: 0m);

            await DeletePaperOrderAsync(factory, firstOrderId);
            var afterOrderDelete = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterOrderDelete.WalletsProcessed);
            Assert.Equal(0, afterOrderDelete.PerformanceRowsWritten);
            Assert.Empty(await ReadProjectionRowsAsync(factory, firstWallet));
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, orderIds, fillIds);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_OptimizedAggregate_MatchesLegacyMetricsAndUsesIndexedFillLookup()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallets = new[]
        {
            $"paper-performance-equivalence-{suffix}-one",
            $"paper-performance-equivalence-{suffix}-two"
        };
        var orderIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var fillIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var gammaMarketIds = new[]
        {
            $"paper-performance-equivalence-{suffix}-gamma-old",
            $"paper-performance-equivalence-{suffix}-gamma-latest",
            $"paper-performance-equivalence-{suffix}-gamma-second"
        };
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            await InsertAggregateEquivalenceFixtureAsync(
                factory,
                strategyId,
                wallets,
                orderIds,
                fillIds,
                gammaMarketIds,
                suffix);
            await PromoteQueuedWalletsAsync(factory, wallets);
            await SetControlCursorToMaximumSourceWalletAsync(factory);

            var plan = await ExplainOptimizedFillLookupAsync(factory, wallets);
            Assert.Contains("ix_paper_fills_order_time", plan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan on paper_fills", plan, StringComparison.Ordinal);

            var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 2,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);

            Assert.True(result.LockAcquired);
            Assert.Equal(2, result.HighPriorityWalletsProcessed);
            var differences = await CompareLegacyAndPersistedProjectionAsync(factory, wallets);
            Assert.Equal(0, differences.LegacyMinusPersisted);
            Assert.Equal(0, differences.PersistedMinusLegacy);

            Assert.Equal(
                new[] { "Crypto", "OVERALL", "StoredCategory", "unknown" },
                (await ReadProjectionRowsAsync(factory, wallets[0])).Select(row => row.Category).ToArray());
            Assert.Equal(
                new[] { "OVERALL", "Sports", "unknown" },
                (await ReadProjectionRowsAsync(factory, wallets[1])).Select(row => row.Category).ToArray());
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, orderIds, fillIds);
            await DeleteGammaMarketRowsAsync(factory, gammaMarketIds);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_ReconciliationSeed_BootstrapsPreexistingWallet()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var previousMaximumWallet = await ReadMaximumSourceWalletAsync(factory) ?? string.Empty;
        var suffix = Guid.NewGuid().ToString("N");
        var lexicalStem = $"{previousMaximumWallet}zzzz-paper-performance-seed-{suffix}-";
        var cursorWallet = $"{lexicalStem}0";
        var seededWallet = $"{lexicalStem}1";
        var wallets = new[] { seededWallet };
        var orderId = Guid.NewGuid();
        var orderIds = new[] { orderId };
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            await InsertPaperOrderAsync(
                factory,
                orderId,
                strategyId,
                seededWallet,
                $"seed-asset-{suffix}",
                $"seed-condition-{suffix}",
                DateTimeOffset.UtcNow.AddMinutes(-1));
            await DeleteQueuedWalletAsync(factory, seededWallet);
            Assert.Empty(await ReadProjectionRowsAsync(factory, seededWallet));
            Assert.Equal(
                seededWallet,
                await ReadMinimumSourceWalletAfterAsync(factory, cursorWallet));
            await SetControlCursorAsync(factory, cursorWallet);

            var seeded = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);

            Assert.True(seeded.LockAcquired);
            Assert.Equal(1, seeded.WalletsSeeded);

            var rows = await ReadProjectionRowsAsync(factory, seededWallet);
            if (rows.Count == 0)
            {
                Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
                await PromoteQueuedWalletsAsync(factory, seededWallet);
                await SetControlCursorToMaximumSourceWalletAsync(factory);

                var processed = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                    highPriorityWalletBatchSize: 1,
                    reconciliationWalletBatchSize: 1,
                    reconciliationSeedWalletBatchSize: 1);

                Assert.True(processed.LockAcquired);
                Assert.Equal(1, processed.WalletsProcessed);
                rows = await ReadProjectionRowsAsync(factory, seededWallet);
            }

            AssertProjectionRows(
                rows,
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 0,
                buyCostUsd: 0m,
                realizedPnlUsd: 0m);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, orderIds, []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_ReservesIndependentHighAndReconciliationBudgetsWithoutSpill()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var highPriorityWallets = Enumerable.Range(1, 4)
            .Select(index => $"paper-performance-budget-{suffix}-high-{index}")
            .ToArray();
        var reconciliationWallets = Enumerable.Range(1, 5)
            .Select(index => $"paper-performance-budget-{suffix}-reconciliation-{index}")
            .ToArray();
        var wallets = highPriorityWallets.Concat(reconciliationWallets).ToArray();
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            await QueueWalletsAsync(factory, highPriorityWallets, int.MaxValue, "budget_high");
            await QueueWalletsAsync(factory, reconciliationWallets, 0, "budget_reconciliation");

            var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 2,
                reconciliationWalletBatchSize: 2,
                reconciliationSeedWalletBatchSize: 100);

            Assert.True(result.LockAcquired);
            Assert.Equal(0, result.WalletsSeeded);
            Assert.Equal(2, result.HighPriorityWalletsProcessed);
            Assert.Equal(2, result.ReconciliationWalletsProcessed);
            Assert.Equal(4, result.WalletsProcessed);
            Assert.Equal(2, await CountQueuedWalletsAsync(factory, highPriorityWallets));
            Assert.Equal(3, await CountQueuedWalletsAsync(factory, reconciliationWallets));

            var controlAfter = await ReadControlStateAsync(factory);
            Assert.Equal(controlState.CursorWallet, controlAfter.CursorWallet);
            Assert.Equal(controlState.Cycle, controlAfter.Cycle);
            Assert.Equal(controlState.UpdatedAtUtc, controlAfter.UpdatedAtUtc);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, [], []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_SeedsOnlyUnusedReconciliationSlotsAndProcessesThemInSameTransaction()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var previousMaximumWallet = await ReadMaximumSourceWalletAsync(factory) ?? string.Empty;
        var suffix = Guid.NewGuid().ToString("N");
        var lexicalStem = $"{previousMaximumWallet}zzzz-paper-performance-capacity-{suffix}-";
        var cursorWallet = $"{lexicalStem}0";
        var sourceWallets = Enumerable.Range(1, 4)
            .Select(index => $"{lexicalStem}{index}")
            .ToArray();
        var backlogWallets = new[]
        {
            sourceWallets[0],
            $"paper-performance-capacity-{suffix}-backlog"
        };
        var wallets = sourceWallets.Concat(backlogWallets).Distinct(StringComparer.Ordinal).ToArray();
        var orderIds = Enumerable.Range(0, sourceWallets.Length).Select(_ => Guid.NewGuid()).ToArray();
        var controlState = await ReadControlStateAsync(factory);

        try
        {
            for (var index = 0; index < sourceWallets.Length; index++)
            {
                await InsertPaperOrderAsync(
                    factory,
                    orderIds[index],
                    strategyId,
                    sourceWallets[index],
                    $"capacity-asset-{suffix}-{index}",
                    $"capacity-condition-{suffix}-{index}",
                    DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(index));
                await DeleteQueuedWalletAsync(factory, sourceWallets[index]);
            }

            await QueueWalletsAsync(factory, backlogWallets, 0, "capacity_backlog");
            await SetControlCursorAsync(factory, cursorWallet);

            var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 2,
                reconciliationWalletBatchSize: 5,
                reconciliationSeedWalletBatchSize: 100);

            Assert.True(result.LockAcquired);
            Assert.Equal(0, result.HighPriorityWalletsProcessed);
            Assert.Equal(3, result.WalletsSeeded);
            Assert.Equal(5, result.ReconciliationWalletsProcessed);
            Assert.Equal(5, result.WalletsProcessed);
            Assert.Equal(8, result.PerformanceRowsWritten);
            Assert.True(result.PaperPositionsSeedSequentialScans is >= 0);
            Assert.True(result.PaperPositionsSeedSequentialTuplesRead is >= 0);
            Assert.True(result.PaperPositionsAggregationSequentialScans is >= 0);
            Assert.True(result.PaperPositionsAggregationSequentialTuplesRead is >= 0);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));
            Assert.NotEmpty(await ReadProjectionRowsAsync(factory, sourceWallets[0]));
            Assert.NotEmpty(await ReadProjectionRowsAsync(factory, sourceWallets[1]));
            Assert.NotEmpty(await ReadProjectionRowsAsync(factory, sourceWallets[2]));
            Assert.NotEmpty(await ReadProjectionRowsAsync(factory, sourceWallets[3]));

            var controlAfter = await ReadControlStateAsync(factory);
            Assert.Equal(sourceWallets[3], controlAfter.CursorWallet);
            Assert.Equal(controlState.Cycle, controlAfter.Cycle);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, orderIds, []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_ClaimedWallet_DoesNotBlockSettlementAndRetainsConcurrentDirtyEvent()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallet = $"paper-performance-race-{suffix}";
        var wallets = new[] { wallet };
        var nowUtc = DateTimeOffset.UtcNow;
        var position = CreatePosition(
            wallet,
            $"race-asset-{suffix}",
            $"race-condition-{suffix}",
            sizeShares: 4m,
            averagePrice: 0.25m,
            nowUtc);
        var controlState = await ReadControlStateAsync(factory);
        Task<PaperCopiedTraderPerformanceRefreshResult>? refreshTask = null;
        Task<int>? settlementTask = null;
        using var refreshCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blockerConnection = factory.CreateConnection();
        await blockerConnection.OpenAsync();
        var blockerBackendPid = await ReadBackendPidAsync(blockerConnection);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        var blockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionAsync(position);
            var initial = await RefreshExactWalletAsync(factory, repository, wallet);
            Assert.Equal(1, initial.HighPriorityWalletsProcessed);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(0, await CountInflightWalletsAsync(factory, wallets));

            await QueueWalletsAsync(factory, wallets, int.MaxValue, "race_refresh");
            await SetControlCursorToMaximumSourceWalletAsync(factory);
            await LockProjectionRowsAsync(blockerConnection, blockerTransaction, wallet);

            refreshTask = repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1,
                refreshCancellation.Token);

            await WaitForAsync(
                async () =>
                    await CountInflightWalletsAsync(factory, wallets) == 1
                    && await CountQueuedWalletsAsync(factory, wallets) == 0,
                TimeSpan.FromSeconds(5));
            await WaitForAsync(
                () => IsProjectionBlockedByAsync(factory, blockerBackendPid),
                TimeSpan.FromSeconds(5));
            Assert.False(refreshTask.IsCompleted);

            var overlapping = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);
            Assert.False(overlapping.LockAcquired);

            var settledAtUtc = nowUtc.AddSeconds(1);
            settlementTask = repository.PersistPaperPositionSettlementBatchAsync(
                [CreateSettlementWrite(position, won: true, settledAtUtc)],
                refreshCancellation.Token);
            Assert.Equal(1, await settlementTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(refreshTask.IsCompleted);
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(1, await CountInflightWalletsAsync(factory, wallets));

            await blockerTransaction.CommitAsync();
            blockerReleased = true;

            var raced = await refreshTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(raced.LockAcquired);
            Assert.Equal(1, raced.HighPriorityWalletsProcessed);
            Assert.Equal(1, raced.HighPriorityQueueRemaining);
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(0, await CountInflightWalletsAsync(factory, wallets));

            var afterRace = Assert.IsType<OverallProjection>(
                await ReadOverallProjectionAsync(factory, wallet));
            Assert.Equal(0, afterRace.OpenPositionsCount);
            Assert.Equal(1, afterRace.SettledPositionsCount);
            Assert.Equal(3m, afterRace.RealizedPnlUsd);

            var replayed = await RefreshExactWalletAsync(factory, repository, wallet);
            Assert.Equal(1, replayed.HighPriorityWalletsProcessed);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(0, await CountInflightWalletsAsync(factory, wallets));
        }
        finally
        {
            if (!blockerReleased)
            {
                try
                {
                    await blockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            refreshCancellation.Cancel();
            if (refreshTask is not null)
            {
                try
                {
                    await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
                {
                }
            }

            if (settlementTask is not null)
            {
                try
                {
                    await settlementTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task PaperEntryBatch_PositionFirstPersistsDeterministicallyOrderedRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallets = new[]
        {
            $"paper-entry-lock-{suffix}-a",
            $"paper-entry-lock-{suffix}-b"
        };
        var nowUtc = DateTimeOffset.UtcNow;
        var positions = new[]
        {
            CreatePosition(wallets[0], $"entry-asset-{suffix}-a", $"entry-condition-{suffix}-a", 2m, 0.40m, nowUtc),
            CreatePosition(wallets[1], $"entry-asset-{suffix}-b", $"entry-condition-{suffix}-b", 3m, 0.50m, nowUtc)
        };
        var orders = positions.Select(position => new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            position.AssetId,
            position.ConditionId,
            position.Outcome,
            position.AveragePrice,
            position.SizeShares,
            position.AveragePrice * position.SizeShares,
            nowUtc,
            nowUtc.AddMinutes(5),
            nowUtc,
            StrategyId: strategyId)).ToArray();
        var fills = orders.Select(order => new PaperFill(
            Guid.NewGuid(),
            order.Id,
            order.Price,
            order.SizeShares,
            nowUtc,
            "position-first integration test")).ToArray();
        var orderIds = orders.Select(order => order.Id).ToArray();
        var fillIds = fills.Select(fill => fill.Id).ToArray();

        try
        {
            await repository.AddPaperEntryPersistenceBatchAsync(new PaperEntryPersistenceBatch(
                [],
                orders.Reverse().ToArray(),
                fills.Reverse().ToArray(),
                positions.Reverse().ToArray(),
                [],
                []));

            Assert.Equal((2, 2), await CountPaperEntryRowsAsync(factory, orderIds, fillIds));
            Assert.Equal(2, await CountQueuedWalletsAsync(factory, wallets));
            Assert.All(
                await Task.WhenAll(positions.Select(position =>
                    repository.GetPaperPositionAsync(position.CopiedTraderWallet, position.AssetId))),
                persisted => Assert.True(Assert.IsType<PaperPosition>(persisted).SizeShares > 0m));
            Assert.All(
                await Task.WhenAll(wallets.Select(wallet => ReadQueuedWalletSourceKindAsync(factory, wallet))),
                sourceKind => Assert.Equal("paper_position", sourceKind));
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets, orderIds, fillIds);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SettlementBatch_PositionFirstLockOrder_AvoidsConcurrentMarkDeadlock()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        var settlementApplicationName = $"paper-lock-settlement-{suffix[..8]}";
        var markApplicationName = $"paper-lock-mark-{suffix[..8]}";
        var settlementRepository = new PostgresAppRepository(
            new PostgresConnectionFactory(
                new StorageOptions { ConnectionString = connectionString },
                settlementApplicationName));
        var markRepository = new PostgresAppRepository(
            new PostgresConnectionFactory(
                new StorageOptions { ConnectionString = connectionString },
                markApplicationName));
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var wallet = $"paper-position-lock-{suffix}";
        var wallets = new[] { wallet };
        var nowUtc = DateTimeOffset.UtcNow;
        var position = CreatePosition(
            wallet,
            $"position-lock-asset-{suffix}",
            $"position-lock-condition-{suffix}",
            sizeShares: 4m,
            averagePrice: 0.25m,
            nowUtc);
        var controlState = await ReadControlStateAsync(factory);
        Task<int>? settlementTask = null;
        Task<bool>? markTask = null;
        using var operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var queueBlockerConnection = factory.CreateConnection();
        await queueBlockerConnection.OpenAsync();
        var queueBlockerBackendPid = await ReadBackendPidAsync(queueBlockerConnection);
        await using var queueBlockerTransaction = await queueBlockerConnection.BeginTransactionAsync();
        var queueBlockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionAsync(position);
            await QueueWalletsAsync(factory, wallets, priority: 0, sourceKind: "position_lock_test");
            await LockQueuedWalletAsync(
                queueBlockerConnection,
                queueBlockerTransaction,
                wallet);

            settlementTask = settlementRepository.PersistPaperPositionSettlementBatchAsync(
                [CreateSettlementWrite(position, won: true, nowUtc.AddSeconds(1))],
                operationCancellation.Token);

            BlockingSession? settlementWait = null;
            await WaitForAsync(
                async () =>
                {
                    settlementWait = await ReadBlockingSessionAsync(factory, settlementApplicationName);
                    return settlementWait is not null
                        && settlementWait.BlockingBackendPids.Contains(queueBlockerBackendPid);
                },
                TimeSpan.FromSeconds(5));
            var settlementBackendPid = Assert.IsType<BlockingSession>(settlementWait).BackendPid;
            Assert.Contains("INSERT INTO paper_positions", settlementWait.Query, StringComparison.Ordinal);
            Assert.Equal(1, await CountGrantedAdvisoryLocksAsync(factory, settlementBackendPid));

            markTask = markRepository.TryUpdatePaperPositionMarkAsync(
                position,
                estimatedValueUsd: 2m,
                unrealizedPnlUsd: 1m,
                netUnrealizedPnlUsd: null,
                updatedAtUtc: nowUtc.AddMilliseconds(500),
                operationCancellation.Token);

            BlockingSession? markWait = null;
            await WaitForAsync(
                async () =>
                {
                    markWait = await ReadBlockingSessionAsync(factory, markApplicationName);
                    return markWait is not null
                        && markWait.BlockingBackendPids.Contains(settlementBackendPid);
                },
                TimeSpan.FromSeconds(5));
            Assert.Contains(
                "pg_advisory_xact_lock",
                Assert.IsType<BlockingSession>(markWait).Query,
                StringComparison.Ordinal);
            Assert.False(settlementTask.IsCompleted);
            Assert.False(markTask.IsCompleted);

            await queueBlockerTransaction.CommitAsync();
            queueBlockerReleased = true;

            Assert.Equal(1, await settlementTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(await markTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal("paper_position", await ReadQueuedWalletSourceKindAsync(factory, wallet));
            Assert.Equal(0m, (await repository.GetPaperPositionAsync(wallet, position.AssetId))?.SizeShares);

            var projected = await RefreshExactWalletAsync(factory, repository, wallet);
            Assert.Equal(1, projected.HighPriorityWalletsProcessed);
            var overall = Assert.IsType<OverallProjection>(await ReadOverallProjectionAsync(factory, wallet));
            Assert.Equal(0, overall.OpenPositionsCount);
            Assert.Equal(1, overall.SettledPositionsCount);
            Assert.Equal(3m, overall.RealizedPnlUsd);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(0, await CountInflightWalletsAsync(factory, wallets));
        }
        finally
        {
            if (!queueBlockerReleased)
            {
                try
                {
                    await queueBlockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            operationCancellation.Cancel();
            if (settlementTask is not null)
            {
                try
                {
                    await settlementTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            if (markTask is not null)
            {
                try
                {
                    await markTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SinglePositionUpsert_WaitsForWalletAdvisoryLockAndCompletesAfterRelease()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var applicationName = $"paper-single-upsert-{suffix[..8]}";
        var lockedRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            applicationName));
        var wallet = $"single-upsert-lock-{suffix}";
        var wallets = new[] { wallet };
        var nowUtc = DateTimeOffset.UtcNow;
        var initialPosition = CreatePosition(
            wallet,
            $"single-upsert-asset-{suffix}",
            $"single-upsert-condition-{suffix}",
            2m,
            0.25m,
            nowUtc);
        var updatedPosition = initialPosition with
        {
            SizeShares = 5m,
            EstimatedValueUsd = 1.25m,
            UpdatedAtUtc = nowUtc.AddSeconds(1)
        };
        Task? upsertTask = null;
        using var operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blockerConnection = factory.CreateConnection();
        await blockerConnection.OpenAsync();
        var blockerBackendPid = await ReadBackendPidAsync(blockerConnection);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        var blockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionAsync(initialPosition);
            await LockPaperWalletAdvisoryAsync(blockerConnection, blockerTransaction, wallet);

            upsertTask = lockedRepository.UpsertPaperPositionAsync(
                updatedPosition,
                operationCancellation.Token);

            BlockingSession? upsertWait = null;
            await WaitForAsync(
                async () =>
                {
                    upsertWait = await ReadBlockingSessionAsync(factory, applicationName);
                    return upsertWait is not null
                        && upsertWait.BlockingBackendPids.Contains(blockerBackendPid);
                },
                TimeSpan.FromSeconds(5));
            var upsertBackendPid = Assert.IsType<BlockingSession>(upsertWait).BackendPid;
            Assert.Contains("pg_advisory_xact_lock", upsertWait.Query, StringComparison.Ordinal);
            Assert.False(upsertTask.IsCompleted);
            Assert.Equal(
                initialPosition.SizeShares,
                (await repository.GetPaperPositionAsync(wallet, initialPosition.AssetId))?.SizeShares);

            await blockerTransaction.CommitAsync();
            blockerReleased = true;

            await upsertTask.WaitAsync(TimeSpan.FromSeconds(5));
            var persisted = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPosition.AssetId));
            Assert.Equal(updatedPosition.SizeShares, persisted.SizeShares);
            Assert.Equal(updatedPosition.EstimatedValueUsd, persisted.EstimatedValueUsd);
            Assert.Equal(0, await CountGrantedAdvisoryLocksAsync(factory, upsertBackendPid));
        }
        finally
        {
            if (!blockerReleased)
            {
                try
                {
                    await blockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            operationCancellation.Cancel();
            if (upsertTask is not null)
            {
                try
                {
                    await upsertTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SingleConditionalMark_WaitsForWalletAdvisoryLockAndPreservesStaleCasAfterRelease()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var applicationName = $"paper-single-mark-{suffix[..8]}";
        var lockedRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            applicationName));
        var wallet = $"single-mark-lock-{suffix}";
        var wallets = new[] { wallet };
        var nowUtc = DateTimeOffset.UtcNow;
        var initialPosition = CreatePosition(
            wallet,
            $"single-mark-asset-{suffix}",
            $"single-mark-condition-{suffix}",
            4m,
            0.25m,
            nowUtc);
        Task<bool>? markTask = null;
        using var operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blockerConnection = factory.CreateConnection();
        await blockerConnection.OpenAsync();
        var blockerBackendPid = await ReadBackendPidAsync(blockerConnection);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        var blockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionAsync(initialPosition);
            var expectedPosition = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPosition.AssetId));
            await LockPaperWalletAdvisoryAsync(blockerConnection, blockerTransaction, wallet);

            markTask = lockedRepository.TryUpdatePaperPositionMarkAsync(
                expectedPosition,
                estimatedValueUsd: 2m,
                unrealizedPnlUsd: 1m,
                netUnrealizedPnlUsd: null,
                updatedAtUtc: nowUtc.AddSeconds(1),
                operationCancellation.Token);

            BlockingSession? markWait = null;
            await WaitForAsync(
                async () =>
                {
                    markWait = await ReadBlockingSessionAsync(factory, applicationName);
                    return markWait is not null
                        && markWait.BlockingBackendPids.Contains(blockerBackendPid);
                },
                TimeSpan.FromSeconds(5));
            var markBackendPid = Assert.IsType<BlockingSession>(markWait).BackendPid;
            Assert.Contains("pg_advisory_xact_lock", markWait.Query, StringComparison.Ordinal);
            Assert.False(markTask.IsCompleted);

            await UpdatePaperPositionMarkWithoutWalletLockForCasTestAsync(
                factory,
                wallet,
                initialPosition.AssetId,
                estimatedValueUsd: 3m,
                unrealizedPnlUsd: 2m,
                updatedAtUtc: nowUtc.AddSeconds(2));
            Assert.False(markTask.IsCompleted);

            await blockerTransaction.CommitAsync();
            blockerReleased = true;

            Assert.False(await markTask.WaitAsync(TimeSpan.FromSeconds(5)));
            var persisted = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPosition.AssetId));
            Assert.Equal(3m, persisted.EstimatedValueUsd);
            Assert.Equal(2m, persisted.UnrealizedPnlUsd);
            Assert.Equal(0, await CountGrantedAdvisoryLocksAsync(factory, markBackendPid));
        }
        finally
        {
            if (!blockerReleased)
            {
                try
                {
                    await blockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            operationCancellation.Cancel();
            if (markTask is not null)
            {
                try
                {
                    await markTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task MixedPositionBatches_AcquireWalletAdvisoryLocksBeforeRowAndQueueLocks()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var firstApplicationName = $"paper-wallet-lock-first-{suffix[..8]}";
        var secondApplicationName = $"paper-wallet-lock-second-{suffix[..8]}";
        var firstRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            firstApplicationName));
        var secondRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            secondApplicationName));
        var wallets = new[]
        {
            $"mixed-wallet-lock-{suffix}-a",
            $"mixed-wallet-lock-{suffix}-b"
        };
        var nowUtc = DateTimeOffset.UtcNow;
        var firstExisting = CreatePosition(
            wallets[0],
            $"mixed-existing-{suffix}-a",
            $"mixed-condition-{suffix}-a",
            2m,
            0.25m,
            nowUtc);
        var secondExisting = CreatePosition(
            wallets[1],
            $"mixed-existing-{suffix}-b",
            $"mixed-condition-{suffix}-b",
            3m,
            0.30m,
            nowUtc);
        var firstNew = CreatePosition(
            wallets[0],
            $"mixed-new-{suffix}-a",
            $"mixed-new-condition-{suffix}-a",
            4m,
            0.35m,
            nowUtc.AddSeconds(1));
        var secondNew = CreatePosition(
            wallets[1],
            $"mixed-new-{suffix}-b",
            $"mixed-new-condition-{suffix}-b",
            5m,
            0.40m,
            nowUtc.AddSeconds(1));
        var firstExistingUpdate = secondExisting with
        {
            SizeShares = 13m,
            EstimatedValueUsd = 3.90m,
            UpdatedAtUtc = nowUtc.AddSeconds(1)
        };
        var secondExistingUpdate = firstExisting with
        {
            SizeShares = 12m,
            EstimatedValueUsd = 3m,
            UpdatedAtUtc = nowUtc.AddSeconds(2)
        };
        Task? firstTask = null;
        Task? secondTask = null;
        using var operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var rowBlockerConnection = factory.CreateConnection();
        await rowBlockerConnection.OpenAsync();
        var rowBlockerBackendPid = await ReadBackendPidAsync(rowBlockerConnection);
        await using var rowBlockerTransaction = await rowBlockerConnection.BeginTransactionAsync();
        var rowBlockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionsAsync([firstExisting, secondExisting]);
            await DeleteQueuedWalletAsync(factory, wallets[0]);
            await DeleteQueuedWalletAsync(factory, wallets[1]);
            await LockPaperPositionAsync(
                rowBlockerConnection,
                rowBlockerTransaction,
                secondExisting.CopiedTraderWallet,
                secondExisting.AssetId);

            firstTask = firstRepository.UpsertPaperPositionsAsync(
                [firstNew, firstExistingUpdate],
                operationCancellation.Token);

            BlockingSession? firstWait = null;
            await WaitForAsync(
                async () =>
                {
                    firstWait = await ReadBlockingSessionAsync(factory, firstApplicationName);
                    return firstWait is not null
                        && firstWait.BlockingBackendPids.Contains(rowBlockerBackendPid);
                },
                TimeSpan.FromSeconds(5));
            var firstBackendPid = Assert.IsType<BlockingSession>(firstWait).BackendPid;
            Assert.Contains("FOR UPDATE OF target_position", firstWait.Query, StringComparison.Ordinal);
            Assert.Equal(
                await CountExpectedWalletAdvisoryLockKeysAsync(factory, wallets),
                await CountGrantedAdvisoryLocksAsync(factory, firstBackendPid));

            secondTask = secondRepository.UpsertPaperPositionsAsync(
                [secondExistingUpdate, secondNew],
                operationCancellation.Token);

            BlockingSession? secondWait = null;
            await WaitForAsync(
                async () =>
                {
                    secondWait = await ReadBlockingSessionAsync(factory, secondApplicationName);
                    return secondWait is not null
                        && secondWait.BlockingBackendPids.Contains(firstBackendPid);
                },
                TimeSpan.FromSeconds(5));
            Assert.Contains(
                "pg_advisory_xact_lock",
                Assert.IsType<BlockingSession>(secondWait).Query,
                StringComparison.Ordinal);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));

            await rowBlockerTransaction.CommitAsync();
            rowBlockerReleased = true;

            await firstTask.WaitAsync(TimeSpan.FromSeconds(5));
            await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, await CountQueuedWalletsAsync(factory, wallets));
            Assert.Equal(
                firstNew.SizeShares,
                (await repository.GetPaperPositionAsync(wallets[0], firstNew.AssetId))?.SizeShares);
            Assert.Equal(
                firstExistingUpdate.SizeShares,
                (await repository.GetPaperPositionAsync(wallets[1], secondExisting.AssetId))?.SizeShares);
            Assert.Equal(
                secondExistingUpdate.SizeShares,
                (await repository.GetPaperPositionAsync(wallets[0], firstExisting.AssetId))?.SizeShares);
            Assert.Equal(
                secondNew.SizeShares,
                (await repository.GetPaperPositionAsync(wallets[1], secondNew.AssetId))?.SizeShares);
        }
        finally
        {
            if (!rowBlockerReleased)
            {
                try
                {
                    await rowBlockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            operationCancellation.Cancel();
            foreach (var task in new[] { firstTask, secondTask })
            {
                if (task is null)
                {
                    continue;
                }

                try
                {
                    await task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task EntryAndLeaderExit_SerializeWalletBeforeQueueAndLeaderRowLocks()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var exitApplicationName = $"paper-leader-exit-{suffix[..8]}";
        var entryApplicationName = $"paper-leader-entry-{suffix[..8]}";
        var exitRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            exitApplicationName));
        var entryRepository = new PostgresAppRepository(new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString },
            entryApplicationName));
        var wallet = $"leader-wallet-lock-{suffix}";
        var wallets = new[] { wallet };
        var assetId = $"leader-wallet-lock-asset-{suffix}";
        var conditionId = $"leader-wallet-lock-condition-{suffix}";
        var leaderPositionId = Guid.NewGuid();
        var entryOrderId = Guid.NewGuid();
        var exitOrderId = Guid.NewGuid();
        var activityEventId = Guid.NewGuid();
        var orderIds = new[] { entryOrderId, exitOrderId };
        var nowUtc = DateTimeOffset.UtcNow;
        var entryOrder = new PaperOrder(
            entryOrderId,
            Guid.NewGuid(),
            wallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            assetId,
            conditionId,
            "Yes",
            0.40m,
            2m,
            0.80m,
            nowUtc,
            nowUtc.AddMinutes(5),
            nowUtc,
            StrategyId: strategyId);
        var exitOrder = new PaperOrder(
            exitOrderId,
            Guid.NewGuid(),
            wallet,
            PaperOrderStatus.Pending,
            TradeSide.Sell,
            assetId,
            conditionId,
            "Yes",
            0.50m,
            1m,
            0.50m,
            nowUtc.AddSeconds(1),
            nowUtc.AddMinutes(5),
            StrategyId: strategyId);
        var activityEvent = new PaperCopiedLeaderActivityEvent(
            activityEventId,
            $"leader-wallet-lock-event-{suffix}",
            wallet.ToUpperInvariant(),
            assetId,
            conditionId,
            TradeSide.Sell,
            0.50m,
            1m,
            0.50m,
            $"leader-wallet-lock-tx-{suffix}",
            nowUtc.AddSeconds(1),
            "{}",
            nowUtc.AddSeconds(1));
        var exitUpdate = new PaperCopiedLeaderPositionExitUpdate(
            leaderPositionId,
            1m,
            1m,
            PaperCopiedLeaderPositionStatus.Active,
            nowUtc.AddSeconds(1),
            activityEvent.TransactionHash,
            nowUtc.AddSeconds(1));
        Task<bool>? exitTask = null;
        Task? entryTask = null;
        using var operationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var leaderRowBlockerConnection = factory.CreateConnection();
        await leaderRowBlockerConnection.OpenAsync();
        var leaderRowBlockerBackendPid = await ReadBackendPidAsync(leaderRowBlockerConnection);
        await using var leaderRowBlockerTransaction = await leaderRowBlockerConnection.BeginTransactionAsync();
        var leaderRowBlockerReleased = false;

        try
        {
            await InsertPaperCopiedLeaderPositionAsync(
                factory,
                leaderPositionId,
                entryOrderId,
                wallet,
                assetId,
                conditionId,
                nowUtc);
            await LockPaperCopiedLeaderPositionAsync(
                leaderRowBlockerConnection,
                leaderRowBlockerTransaction,
                leaderPositionId);

            exitTask = exitRepository.ApplyPaperCopiedLeaderExitAsync(
                activityEvent,
                [exitUpdate],
                [],
                [exitOrder],
                operationCancellation.Token);

            BlockingSession? exitWait = null;
            await WaitForAsync(
                async () =>
                {
                    exitWait = await ReadBlockingSessionAsync(factory, exitApplicationName);
                    return exitWait is not null
                        && exitWait.BlockingBackendPids.Contains(leaderRowBlockerBackendPid);
                },
                TimeSpan.FromSeconds(5));
            var exitBackendPid = Assert.IsType<BlockingSession>(exitWait).BackendPid;
            Assert.Contains("UPDATE paper_copied_leader_positions", exitWait.Query, StringComparison.Ordinal);
            Assert.Equal(
                await CountExpectedWalletAdvisoryLockKeysAsync(
                    factory,
                    [wallet, activityEvent.CopiedTraderWallet]),
                await CountGrantedAdvisoryLocksAsync(factory, exitBackendPid));

            entryTask = entryRepository.AddPaperEntryPersistenceBatchAsync(
                new PaperEntryPersistenceBatch(
                    [],
                    [entryOrder],
                    [],
                    [],
                    [new PaperCopiedLeaderPositionActivation(entryOrderId, 2m, nowUtc)],
                    []),
                operationCancellation.Token);

            BlockingSession? entryWait = null;
            await WaitForAsync(
                async () =>
                {
                    entryWait = await ReadBlockingSessionAsync(factory, entryApplicationName);
                    return entryWait is not null
                        && entryWait.BlockingBackendPids.Contains(exitBackendPid);
                },
                TimeSpan.FromSeconds(5));
            Assert.Contains(
                "pg_advisory_xact_lock",
                Assert.IsType<BlockingSession>(entryWait).Query,
                StringComparison.Ordinal);
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, wallets));

            await leaderRowBlockerTransaction.CommitAsync();
            leaderRowBlockerReleased = true;

            Assert.True(await exitTask.WaitAsync(TimeSpan.FromSeconds(5)));
            await entryTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((2, 0), await CountPaperEntryRowsAsync(factory, orderIds, []));
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, wallets));
            var leaderState = await ReadPaperCopiedLeaderPositionStateAsync(factory, leaderPositionId);
            Assert.Equal("Active", leaderState.Status);
            Assert.Equal(2m, leaderState.CopiedInitialSizeShares);
            Assert.Equal(1m, leaderState.LeaderSoldSizeShares);
            Assert.Equal(1m, leaderState.CopiedExitRequestedSizeShares);
        }
        finally
        {
            if (!leaderRowBlockerReleased)
            {
                try
                {
                    await leaderRowBlockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            operationCancellation.Cancel();
            foreach (var task in new Task?[] { exitTask, entryTask })
            {
                if (task is null)
                {
                    continue;
                }

                try
                {
                    await task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (Exception) when (operationCancellation.IsCancellationRequested)
                {
                }
            }

            await DeletePaperCopiedLeaderLockTestRowsAsync(
                factory,
                leaderPositionId,
                activityEventId,
                wallets,
                orderIds);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Projection_RecoversDurableInflightWalletAfterInterruptedCycle()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var repository = new PostgresAppRepository(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallet = $"paper-performance-recovery-{suffix}";
        var freshWallet = $"paper-performance-recovery-{suffix}-fresh";
        var wallets = new[] { wallet, freshWallet };
        var position = CreatePosition(
            wallet,
            $"recovery-asset-{suffix}",
            $"recovery-condition-{suffix}",
            sizeShares: 2m,
            averagePrice: 0.40m,
            DateTimeOffset.UtcNow);
        var controlState = await ReadControlStateAsync(factory);
        Task<PaperCopiedTraderPerformanceRefreshResult>? interruptedRefreshTask = null;
        using var refreshCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blockerConnection = factory.CreateConnection();
        await blockerConnection.OpenAsync();
        var blockerBackendPid = await ReadBackendPidAsync(blockerConnection);
        await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
        var blockerReleased = false;

        try
        {
            await repository.UpsertPaperPositionAsync(position);
            var initial = await RefreshExactWalletAsync(factory, repository, wallet);
            Assert.Equal(1, initial.HighPriorityWalletsProcessed);
            await QueueWalletsAsync(factory, [wallet], 100, "recovery_interrupted_claim");
            await SetControlCursorToMaximumSourceWalletAsync(factory);
            await LockProjectionRowsAsync(blockerConnection, blockerTransaction, wallet);

            interruptedRefreshTask = repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1,
                refreshCancellation.Token);

            await WaitForAsync(
                async () =>
                    await CountInflightWalletsAsync(factory, [wallet]) == 1
                    && await CountQueuedWalletsAsync(factory, [wallet]) == 0,
                TimeSpan.FromSeconds(5));
            await WaitForAsync(
                () => IsProjectionBlockedByAsync(factory, blockerBackendPid),
                TimeSpan.FromSeconds(5));

            refreshCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await interruptedRefreshTask.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, await CountInflightWalletsAsync(factory, [wallet]));
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, [wallet]));

            await blockerTransaction.CommitAsync();
            blockerReleased = true;
            await QueueWalletsAsync(factory, [freshWallet], int.MaxValue, "recovery_fresh_queue");
            await SetControlCursorToMaximumSourceWalletAsync(factory);

            var recovered = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
                highPriorityWalletBatchSize: 1,
                reconciliationWalletBatchSize: 1,
                reconciliationSeedWalletBatchSize: 1);

            Assert.True(recovered.LockAcquired);
            Assert.Equal(1, recovered.HighPriorityWalletsProcessed);
            Assert.Equal(0, await CountInflightWalletsAsync(factory, [wallet]));
            Assert.Equal(0, await CountQueuedWalletsAsync(factory, [wallet]));
            Assert.Equal(1, await CountQueuedWalletsAsync(factory, [freshWallet]));
            var overall = Assert.IsType<OverallProjection>(
                await ReadOverallProjectionAsync(factory, wallet));
            Assert.Equal(1, overall.OpenPositionsCount);
            Assert.Equal(0, overall.SettledPositionsCount);
        }
        finally
        {
            if (!blockerReleased)
            {
                try
                {
                    await blockerTransaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }

            refreshCancellation.Cancel();
            if (interruptedRefreshTask is not null)
            {
                try
                {
                    await interruptedRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
                {
                }
            }

            await DeleteTestRowsAsync(factory, wallets, [], []);
            await RestoreControlStateAsync(factory, controlState);
        }
    }

    private static async Task<PaperCopiedTraderPerformanceRefreshResult> RefreshExactWalletAsync(
        PostgresConnectionFactory factory,
        PostgresAppRepository repository,
        string wallet)
    {
        await PromoteQueuedWalletsAsync(factory, wallet);
        await SetControlCursorToMaximumSourceWalletAsync(factory);
        var result = await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(
            highPriorityWalletBatchSize: 1,
            reconciliationWalletBatchSize: 1,
            reconciliationSeedWalletBatchSize: 1);
        Assert.True(result.LockAcquired);
        return result;
    }

    private static void AssertProjectionRows(
        IReadOnlyList<ProjectionRow> rows,
        int ordersCount,
        int filledOrdersCount,
        int buyFillsCount,
        decimal buyCostUsd,
        decimal realizedPnlUsd)
    {
        Assert.Equal(2, rows.Count);
        var byCategory = rows.ToDictionary(row => row.Category, StringComparer.Ordinal);
        Assert.True(byCategory.ContainsKey("unknown"));
        Assert.True(byCategory.ContainsKey("OVERALL"));
        foreach (var row in rows)
        {
            Assert.Equal(ordersCount, row.OrdersCount);
            Assert.Equal(filledOrdersCount, row.FilledOrdersCount);
            Assert.Equal(buyFillsCount, row.BuyFillsCount);
            Assert.Equal(buyCostUsd, row.BuyCostUsd);
            Assert.Equal(realizedPnlUsd, row.RealizedPnlUsd);
        }
    }

    private static void AssertOpenOnlyAggregatePerformance(
        PaperCopiedTraderPerformance performance,
        string wallet,
        string category)
    {
        Assert.Equal(wallet, performance.CopiedTraderWallet);
        Assert.Equal(category, performance.Category);
        Assert.Equal(0, performance.OrdersCount);
        Assert.Equal(0, performance.FilledOrdersCount);
        Assert.Equal(0, performance.BuyFillsCount);
        Assert.Equal(0, performance.SellFillsCount);
        Assert.Equal(2, performance.OpenPositionsCount);
        Assert.Equal(0, performance.SettledPositionsCount);
        Assert.Equal(0, performance.WonPositionsCount);
        Assert.Equal(0, performance.LostPositionsCount);
        Assert.Equal(0m, performance.BuyCostUsd);
        Assert.Equal(0m, performance.SellProceedsUsd);
        Assert.Equal(0m, performance.SettlementValueUsd);
        Assert.Equal(0m, performance.RealizedPnlUsd);
        Assert.Equal(0.75m, performance.UnrealizedPnlUsd);
        Assert.Equal(0.75m, performance.TotalPnlUsd);
        Assert.Equal(0m, performance.RoiPct);
        Assert.Equal(0m, performance.WinRatePct);
        Assert.Equal(38.2375m, performance.Score);
        Assert.Null(performance.FirstOrderUtc);
        Assert.Null(performance.LastOrderUtc);
    }

    private static async Task InsertOpenOnlyAggregateFixtureAsync(
        PostgresConnectionFactory factory,
        string wallet,
        string closedOnlyWallet,
        string adversarialClosedAssetId,
        string suffix)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var disableTriggers = new NpgsqlCommand(
            "ALTER TABLE public.paper_positions DISABLE TRIGGER USER;",
            connection,
            transaction))
        {
            await disableTriggers.ExecuteNonQueryAsync();
        }

        await using (var insertClosedHistory = new NpgsqlCommand(
            """
INSERT INTO public.paper_positions (
    id,
    copied_trader_wallet,
    asset_id,
    condition_id,
    outcome,
    size_shares,
    average_price,
    estimated_value_usd,
    unrealized_pnl_usd,
    updated_at_utc)
SELECT
    gen_random_uuid(),
    @Wallet,
    @AssetPrefix || value::text,
    @ConditionId,
    'Yes',
    0,
    0,
    0,
    0,
    @UpdatedAtUtc
FROM generate_series(1, 100000) value;
""",
            connection,
            transaction))
        {
            insertClosedHistory.Parameters.AddWithValue("Wallet", wallet);
            insertClosedHistory.Parameters.AddWithValue("AssetPrefix", $"closed-history-{suffix}-");
            insertClosedHistory.Parameters.AddWithValue("ConditionId", $"closed-history-condition-{suffix}");
            insertClosedHistory.Parameters.AddWithValue("UpdatedAtUtc", DateTime.UtcNow);
            Assert.Equal(100_000, await insertClosedHistory.ExecuteNonQueryAsync());
        }

        await using (var insertFocusedRows = new NpgsqlCommand(
            """
INSERT INTO public.paper_positions (
    id,
    copied_trader_wallet,
    asset_id,
    condition_id,
    outcome,
    size_shares,
    average_price,
    estimated_value_usd,
    unrealized_pnl_usd,
    updated_at_utc)
VALUES
    (gen_random_uuid(), @Wallet, @AdversarialClosedAssetId, @AdversarialClosedConditionId,
     'Yes', 0, 0, 0, 123.45, @UpdatedAtUtc),
    (gen_random_uuid(), @Wallet, @OpenAssetIdOne, @OpenConditionIdOne,
     'Yes', 2, 0.40, 0.80, 1.25, @UpdatedAtUtc),
    (gen_random_uuid(), @Wallet, @OpenAssetIdTwo, @OpenConditionIdTwo,
     'No', 3, 0.50, 1.50, -0.50, @UpdatedAtUtc),
    (gen_random_uuid(), @ClosedOnlyWallet, @ClosedOnlyAssetId, @ClosedOnlyConditionId,
     'Yes', 0, 0, 0, 17.25, @UpdatedAtUtc);
""",
            connection,
            transaction))
        {
            insertFocusedRows.Parameters.AddWithValue("Wallet", wallet);
            insertFocusedRows.Parameters.AddWithValue("ClosedOnlyWallet", closedOnlyWallet);
            insertFocusedRows.Parameters.AddWithValue("AdversarialClosedAssetId", adversarialClosedAssetId);
            insertFocusedRows.Parameters.AddWithValue("AdversarialClosedConditionId", $"closed-adversarial-condition-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("OpenAssetIdOne", $"open-one-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("OpenConditionIdOne", $"open-condition-one-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("OpenAssetIdTwo", $"open-two-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("OpenConditionIdTwo", $"open-condition-two-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("ClosedOnlyAssetId", $"closed-only-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("ClosedOnlyConditionId", $"closed-only-condition-{suffix}");
            insertFocusedRows.Parameters.AddWithValue("UpdatedAtUtc", DateTime.UtcNow);
            Assert.Equal(4, await insertFocusedRows.ExecuteNonQueryAsync());
        }

        await using (var enableTriggers = new NpgsqlCommand(
            "ALTER TABLE public.paper_positions ENABLE TRIGGER USER;",
            connection,
            transaction))
        {
            await enableTriggers.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task AnalyzePaperPositionsAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("ANALYZE public.paper_positions;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<OpenOnlyAggregateFixtureState> ReadOpenOnlyAggregateFixtureStateAsync(
        PostgresConnectionFactory factory,
        string wallet,
        string adversarialClosedAssetId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    count(*)::bigint,
    count(*) FILTER (WHERE size_shares <= 0)::bigint,
    count(*) FILTER (WHERE size_shares > 0)::bigint,
    count(*) FILTER (
        WHERE asset_id = @AdversarialClosedAssetId
          AND size_shares = 0
          AND unrealized_pnl_usd <> 0) = 1
FROM public.paper_positions
WHERE copied_trader_wallet = @Wallet;
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AdversarialClosedAssetId", adversarialClosedAssetId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new OpenOnlyAggregateFixtureState(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetBoolean(3));
    }

    private static async Task<string> ExplainOpenWalletPositionLookupAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var selectorCommand = new NpgsqlCommand(
            """
DROP TABLE IF EXISTS pg_temp.temp_paper_copied_trader_performance_wallets;
CREATE TEMP TABLE temp_paper_copied_trader_performance_wallets (
    copied_trader_wallet text PRIMARY KEY,
    work_kind text NOT NULL CHECK (work_kind IN ('high_priority', 'reconciliation'))
) ON COMMIT PRESERVE ROWS;
INSERT INTO temp_paper_copied_trader_performance_wallets (copied_trader_wallet, work_kind)
SELECT wallet, 'high_priority'
FROM unnest(@Wallets::text[]) wallet;
ANALYZE pg_temp.temp_paper_copied_trader_performance_wallets;
""",
            connection,
            transaction))
        {
            selectorCommand.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
            await selectorCommand.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(
            """
EXPLAIN (COSTS OFF)
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
FROM public.paper_positions pp
JOIN temp_paper_copied_trader_performance_wallets selected
  ON selected.copied_trader_wallet = pp.copied_trader_wallet
LEFT JOIN LATERAL (
    SELECT market.category
    FROM public.polymarket_gamma_markets market
    WHERE market.condition_id = pp.condition_id
    ORDER BY market.fetched_at_utc DESC, market.market_id
    LIMIT 1
) gm ON true
WHERE pp.copied_trader_wallet <> ''
  AND pp.size_shares > 0;
""",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var planLines = new List<string>();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        await reader.DisposeAsync();
        await transaction.RollbackAsync();
        return string.Join(Environment.NewLine, planLines);
    }

    private static async Task DeleteOpenOnlyAggregateFixtureAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var disableTriggers = new NpgsqlCommand(
            "ALTER TABLE public.paper_positions DISABLE TRIGGER USER;",
            connection,
            transaction))
        {
            await disableTriggers.ExecuteNonQueryAsync();
        }

        await using (var deletePositions = new NpgsqlCommand(
            "DELETE FROM public.paper_positions WHERE copied_trader_wallet = ANY(@Wallets);",
            connection,
            transaction))
        {
            deletePositions.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
            await deletePositions.ExecuteNonQueryAsync();
        }

        await using (var enableTriggers = new NpgsqlCommand(
            "ALTER TABLE public.paper_positions ENABLE TRIGGER USER;",
            connection,
            transaction))
        {
            await enableTriggers.ExecuteNonQueryAsync();
        }

        await using (var deleteDerivedRows = new NpgsqlCommand(
            """
DELETE FROM public.paper_copied_trader_performance
WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM public.paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM public.paper_copied_trader_performance_refresh_inflight
WHERE copied_trader_wallet = ANY(@Wallets);
""",
            connection,
            transaction))
        {
            deleteDerivedRows.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
            await deleteDerivedRows.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static PaperPosition CreatePosition(
        string wallet,
        string assetId,
        string conditionId,
        decimal sizeShares,
        decimal averagePrice,
        DateTimeOffset nowUtc)
    {
        return new PaperPosition(
            assetId,
            conditionId,
            "Yes",
            sizeShares,
            averagePrice,
            sizeShares * averagePrice,
            0m,
            nowUtc,
            wallet);
    }

    private static PaperPositionSettlementWrite CreateSettlementWrite(
        PaperPosition position,
        bool won,
        DateTimeOffset settledAtUtc)
    {
        var costBasis = position.SizeShares * position.AveragePrice;
        var settlementValue = won ? position.SizeShares : 0m;
        return new PaperPositionSettlementWrite(
            new PaperPositionSettlement(
                Guid.NewGuid(),
                position.CopiedTraderWallet,
                position.AssetId,
                position.ConditionId,
                position.Outcome,
                won ? position.AssetId : null,
                won ? position.Outcome : "No",
                "IntegrationTest",
                position.SizeShares,
                position.AveragePrice,
                costBasis,
                settlementValue,
                settlementValue - costBasis,
                won,
                "IntegrationTest",
                settledAtUtc,
                settledAtUtc),
            position with
            {
                SizeShares = 0m,
                AveragePrice = 0m,
                EstimatedValueUsd = 0m,
                UnrealizedPnlUsd = 0m,
                UpdatedAtUtc = settledAtUtc
            });
    }

    private static async Task<Guid> ReadFirstStrategyIdAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM strategies ORDER BY id LIMIT 1;", connection);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("PostgreSQL integration database has no strategy row."));
    }

    private static async Task InsertAggregateEquivalenceFixtureAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string[] wallets,
        Guid[] orderIds,
        Guid[] fillIds,
        string[] gammaMarketIds,
        string suffix)
    {
        var conditionA = $"condition-{suffix}-a";
        var conditionB = $"condition-{suffix}-b";
        var missingCondition = $"condition-{suffix}-missing";
        var nowUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO polymarket_gamma_markets (
    market_id, condition_id, question_id, slug, question, category,
    active, closed, archived, restricted, accepting_orders, enable_order_book,
    negative_risk, outcomes_json, clob_token_ids_json, raw_json, fetched_at_utc)
VALUES
    (@Gamma0, @ConditionA, @Question0, @Slug0, 'Old category fixture', 'OldCategory',
     true, false, false, false, true, true, false, '[]'::jsonb, '[]'::jsonb, '{}'::jsonb, @GammaOldUtc),
    (@Gamma1, @ConditionA, @Question1, @Slug1, 'Latest category fixture', 'Crypto',
     true, false, false, false, true, true, false, '[]'::jsonb, '[]'::jsonb, '{}'::jsonb, @GammaLatestUtc),
    (@Gamma2, @ConditionB, @Question2, @Slug2, 'Second category fixture', 'Sports',
     true, false, false, false, true, true, false, '[]'::jsonb, '[]'::jsonb, '{}'::jsonb, @GammaLatestUtc);

INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id,
    condition_id, outcome, price, size_shares, notional_usd, created_at_utc,
    expires_at_utc, filled_at_utc, raw_decision_json)
VALUES
    (@Order0, @Signal0, @StrategyId, @Wallet0, 'Filled', 'Buy', @Asset0,
     @ConditionA, 'Yes', 0.40, 5, 2, @Created0, @ExpiresUtc, @FilledUtc, '{}'::jsonb),
    (@Order1, @Signal1, @StrategyId, @Wallet0, 'PartiallyFilled', 'Sell', @Asset1,
     @ConditionA, 'Yes', 0.60, 3, 1.8, @Created1, @ExpiresUtc, @FilledUtc, '{}'::jsonb),
    (@Order2, @Signal2, @StrategyId, @Wallet0, 'Pending', 'Buy', @Asset2,
     @MissingCondition, 'Yes', 0.30, 2, 0.6, @Created2, @ExpiresUtc, NULL, '{}'::jsonb),
    (@Order3, @Signal3, @StrategyId, @Wallet1, 'Filled', 'Buy', @Asset3,
     @ConditionB, 'Yes', 0.50, 4, 2, @Created3, @ExpiresUtc, @FilledUtc, '{}'::jsonb);

INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)
VALUES
    (@Fill0, @Order0, 0.40, 2, @FilledUtc, 'aggregate equivalence fixture', 0.25),
    (@Fill1, @Order0, 0.45, 3, @FilledUtc, 'aggregate equivalence fixture', -0.10),
    (@Fill2, @Order1, 0.65, 1.5, @FilledUtc, 'aggregate equivalence fixture', 0.40),
    (@Fill3, @Order3, 0.50, 4, @FilledUtc, 'aggregate equivalence fixture', 1.20);

INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares,
    average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc)
VALUES
    (@Position0, @Wallet0, @OpenAsset0, @ConditionA, 'Yes', 1, 0.40, 1.15, 0.75, @Created3),
    (@Position1, @Wallet1, @OpenAsset1, @MissingCondition, 'Yes', 2, 0.50, 0.75, -0.25, @Created3);

INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_asset_id,
    winning_outcome, category, settled_size_shares, average_price, cost_basis_usd,
    settlement_value_usd, realized_pnl_usd, won, settlement_source,
    settled_at_utc, created_at_utc)
VALUES
    (@Settlement0, @Wallet0, @SettledAsset0, @MissingCondition, 'Yes', @SettledAsset0,
     'Yes', 'StoredCategory', 3, 0.30, 0.90, 3, 2.10, true, 'aggregate equivalence fixture', @Created3, @Created3),
    (@Settlement1, @Wallet0, @SettledAsset1, @ConditionA, 'Yes', NULL,
     'No', NULL, 2, 0.60, 1.20, 0, -1.20, false, 'aggregate equivalence fixture', @Created3, @Created3),
    (@Settlement2, @Wallet1, @SettledAsset2, @MissingCondition, 'Yes', NULL,
     'No', NULL, 1, 0.50, 0.50, 0, -0.50, false, 'aggregate equivalence fixture', @Created3, @Created3);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        for (var index = 0; index < wallets.Length; index++)
        {
            command.Parameters.AddWithValue($"Wallet{index}", wallets[index]);
        }

        for (var index = 0; index < orderIds.Length; index++)
        {
            command.Parameters.AddWithValue($"Order{index}", orderIds[index]);
            command.Parameters.AddWithValue($"Signal{index}", Guid.NewGuid());
            command.Parameters.AddWithValue($"Asset{index}", $"asset-{suffix}-order-{index}");
        }

        for (var index = 0; index < fillIds.Length; index++)
        {
            command.Parameters.AddWithValue($"Fill{index}", fillIds[index]);
        }

        for (var index = 0; index < gammaMarketIds.Length; index++)
        {
            command.Parameters.AddWithValue($"Gamma{index}", gammaMarketIds[index]);
            command.Parameters.AddWithValue($"Question{index}", $"question-{suffix}-{index}");
            command.Parameters.AddWithValue($"Slug{index}", $"slug-{suffix}-{index}");
        }

        command.Parameters.AddWithValue("ConditionA", conditionA);
        command.Parameters.AddWithValue("ConditionB", conditionB);
        command.Parameters.AddWithValue("MissingCondition", missingCondition);
        command.Parameters.AddWithValue("GammaOldUtc", nowUtc.AddMinutes(-2).UtcDateTime);
        command.Parameters.AddWithValue("GammaLatestUtc", nowUtc.AddMinutes(-1).UtcDateTime);
        command.Parameters.AddWithValue("Created0", nowUtc.UtcDateTime);
        command.Parameters.AddWithValue("Created1", nowUtc.AddSeconds(1).UtcDateTime);
        command.Parameters.AddWithValue("Created2", nowUtc.AddSeconds(2).UtcDateTime);
        command.Parameters.AddWithValue("Created3", nowUtc.AddSeconds(3).UtcDateTime);
        command.Parameters.AddWithValue("ExpiresUtc", nowUtc.AddMinutes(5).UtcDateTime);
        command.Parameters.AddWithValue("FilledUtc", nowUtc.AddSeconds(4).UtcDateTime);
        command.Parameters.AddWithValue("Position0", Guid.NewGuid());
        command.Parameters.AddWithValue("Position1", Guid.NewGuid());
        command.Parameters.AddWithValue("OpenAsset0", $"asset-{suffix}-open-0");
        command.Parameters.AddWithValue("OpenAsset1", $"asset-{suffix}-open-1");
        command.Parameters.AddWithValue("Settlement0", Guid.NewGuid());
        command.Parameters.AddWithValue("Settlement1", Guid.NewGuid());
        command.Parameters.AddWithValue("Settlement2", Guid.NewGuid());
        command.Parameters.AddWithValue("SettledAsset0", $"asset-{suffix}-settled-0");
        command.Parameters.AddWithValue("SettledAsset1", $"asset-{suffix}-settled-1");
        command.Parameters.AddWithValue("SettledAsset2", $"asset-{suffix}-settled-2");
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task<string> ExplainOptimizedFillLookupAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
SET LOCAL enable_seqscan = off;
EXPLAIN (COSTS OFF)
WITH selected_orders AS MATERIALIZED (
    SELECT po.id, po.copied_trader_wallet, po.side
    FROM paper_orders po
    WHERE po.copied_trader_wallet = ANY(@Wallets)
)
SELECT
    orders.copied_trader_wallet,
    SUM(CASE WHEN orders.side = 'Buy' THEN fills.fill_count ELSE 0 END),
    SUM(CASE WHEN orders.side = 'Buy' THEN fills.notional_usd ELSE 0 END),
    SUM(fills.realized_pnl_usd)
FROM selected_orders orders
LEFT JOIN LATERAL (
    SELECT
        COUNT(*) AS fill_count,
        COALESCE(SUM(fill.price * fill.size_shares), 0) AS notional_usd,
        COALESCE(SUM(fill.realized_pnl_usd), 0) AS realized_pnl_usd
    FROM paper_fills fill
    WHERE fill.paper_order_id = orders.id
    OFFSET 0
) fills ON true
GROUP BY orders.copied_trader_wallet;
""",
            connection,
            transaction);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        await using var reader = await command.ExecuteReaderAsync();
        var planLines = new List<string>();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        await reader.DisposeAsync();
        await transaction.RollbackAsync();
        return string.Join(Environment.NewLine, planLines);
    }

    private static async Task<ProjectionDifferenceCounts> CompareLegacyAndPersistedProjectionAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
WITH selected AS MATERIALIZED (
    SELECT unnest(@Wallets::text[]) AS copied_trader_wallet
), event_rows AS (
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
    JOIN selected ON selected.copied_trader_wallet = po.copied_trader_wallet
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
        COALESCE(NULLIF(gm.category, ''), 'unknown'),
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
    JOIN selected ON selected.copied_trader_wallet = po.copied_trader_wallet
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
        COALESCE(NULLIF(gm.category, ''), 'unknown'),
        0, 0, 0, 0,
        1,
        0, 0, 0,
        0, 0, 0, 0,
        pp.unrealized_pnl_usd,
        NULL::timestamptz,
        NULL::timestamptz
    FROM paper_positions pp
    JOIN selected ON selected.copied_trader_wallet = pp.copied_trader_wallet
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
        COALESCE(NULLIF(ps.category, ''), NULLIF(gm.category, ''), 'unknown'),
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
    JOIN selected ON selected.copied_trader_wallet = ps.copied_trader_wallet
    LEFT JOIN LATERAL (
        SELECT market.category
        FROM polymarket_gamma_markets market
        WHERE NULLIF(ps.category, '') IS NULL
          AND market.condition_id = ps.condition_id
        ORDER BY market.fetched_at_utc DESC, market.market_id
        LIMIT 1
    ) gm ON true
    WHERE ps.copied_trader_wallet <> ''
), grouped AS (
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
), scored AS (
    SELECT *,
        realized_pnl_usd + unrealized_pnl_usd AS total_pnl_usd,
        CASE WHEN buy_cost_usd = 0 THEN 0 ELSE (realized_pnl_usd + unrealized_pnl_usd) / buy_cost_usd * 100 END AS roi_pct,
        CASE WHEN settled_positions_count = 0 THEN 0 ELSE won_positions_count::numeric / settled_positions_count * 100 END AS win_rate_pct
    FROM grouped
), legacy_projection AS (
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
        buy_cost_usd::numeric(28,8),
        sell_proceeds_usd::numeric(28,8),
        settlement_value_usd::numeric(28,8),
        realized_pnl_usd::numeric(28,8),
        unrealized_pnl_usd::numeric(28,8),
        total_pnl_usd::numeric(28,8),
        roi_pct::numeric(18,8),
        win_rate_pct::numeric(18,8),
        greatest(0, least(100,
            50
            + greatest(-50, least(50, roi_pct)) * 0.35
            + (win_rate_pct - 50) * 0.25
            + greatest(-20, least(20, total_pnl_usd)) * 1.25
            + least(settled_positions_count, 20) * 0.5
            - lost_positions_count * 1.25
            - open_positions_count * 0.1
        ))::numeric(28,8) AS score,
        first_order_utc,
        last_order_utc
    FROM scored
), persisted_projection AS (
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
        score,
        first_order_utc,
        last_order_utc
    FROM paper_copied_trader_performance
    WHERE copied_trader_wallet = ANY(@Wallets)
), legacy_minus_persisted AS (
    SELECT * FROM legacy_projection
    EXCEPT ALL
    SELECT * FROM persisted_projection
), persisted_minus_legacy AS (
    SELECT * FROM persisted_projection
    EXCEPT ALL
    SELECT * FROM legacy_projection
)
SELECT
    (SELECT count(*)::integer FROM legacy_minus_persisted),
    (SELECT count(*)::integer FROM persisted_minus_legacy);
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ProjectionDifferenceCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task DeleteGammaMarketRowsAsync(
        PostgresConnectionFactory factory,
        string[] marketIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM polymarket_gamma_markets WHERE market_id = ANY(@MarketIds);",
            connection);
        command.Parameters.Add("MarketIds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = marketIds;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(int Orders, int Fills)> CountPaperEntryRowsAsync(
        PostgresConnectionFactory factory,
        Guid[] orderIds,
        Guid[] fillIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*)::integer FROM paper_orders WHERE id = ANY(@OrderIds)),
    (SELECT count(*)::integer FROM paper_fills WHERE id = ANY(@FillIds));
""",
            connection);
        command.Parameters.Add("OrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = orderIds;
        command.Parameters.Add("FillIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = fillIds;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task InsertPaperCopiedLeaderPositionAsync(
        PostgresConnectionFactory factory,
        Guid positionId,
        Guid entryOrderId,
        string wallet,
        string assetId,
        string conditionId,
        DateTimeOffset nowUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_copied_leader_positions (
    id, entry_signal_id, entry_paper_order_id, copied_trader_wallet, asset_id,
    condition_id, outcome, entry_timestamp_utc, leader_entry_price,
    leader_initial_size_shares, copied_initial_size_shares, leader_sold_size_shares,
    copied_exit_requested_size_shares, status, next_activity_sync_at_utc,
    created_at_utc, updated_at_utc)
VALUES (
    @Id, @EntrySignalId, @EntryOrderId, @Wallet, @AssetId,
    @ConditionId, 'Yes', @NowUtc, 0.40,
    4, 0, 0,
    0, 'PendingEntry', @NowUtc,
    @NowUtc, @NowUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        command.Parameters.AddWithValue("EntrySignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("EntryOrderId", entryOrderId);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        command.Parameters.AddWithValue("ConditionId", conditionId);
        command.Parameters.AddWithValue("NowUtc", nowUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<PaperCopiedLeaderPositionState> ReadPaperCopiedLeaderPositionStateAsync(
        PostgresConnectionFactory factory,
        Guid positionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT status, copied_initial_size_shares, leader_sold_size_shares,
       copied_exit_requested_size_shares
FROM paper_copied_leader_positions
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", positionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaperCopiedLeaderPositionState(
            reader.GetString(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3));
    }

    private static async Task InsertPaperOrderAsync(
        PostgresConnectionFactory factory,
        Guid orderId,
        Guid strategyId,
        string wallet,
        string assetId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side, asset_id,
    condition_id, outcome, price, size_shares, notional_usd, created_at_utc,
    expires_at_utc, filled_at_utc, raw_decision_json)
VALUES (
    @Id, @SignalId, @StrategyId, @Wallet, 'Filled', 'Buy', @AssetId,
    @ConditionId, 'Yes', 0.40, 5, 2, @CreatedAtUtc,
    @ExpiresAtUtc, @FilledAtUtc, '{}'::jsonb);
""",
            connection);
        command.Parameters.AddWithValue("Id", orderId);
        command.Parameters.AddWithValue("SignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        command.Parameters.AddWithValue("ConditionId", conditionId);
        command.Parameters.AddWithValue("CreatedAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("ExpiresAtUtc", createdAtUtc.AddMinutes(5).UtcDateTime);
        command.Parameters.AddWithValue("FilledAtUtc", createdAtUtc.AddSeconds(1).UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertPaperFillAsync(
        PostgresConnectionFactory factory,
        Guid fillId,
        Guid orderId,
        decimal price,
        decimal sizeShares,
        decimal realizedPnlUsd,
        DateTimeOffset filledAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)
VALUES (
    @Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, 'projection integration test', @RealizedPnlUsd);
""",
            connection);
        command.Parameters.AddWithValue("Id", fillId);
        command.Parameters.AddWithValue("PaperOrderId", orderId);
        command.Parameters.AddWithValue("Price", price);
        command.Parameters.AddWithValue("SizeShares", sizeShares);
        command.Parameters.AddWithValue("FilledAtUtc", filledAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("RealizedPnlUsd", realizedPnlUsd);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdatePaperFillAsync(
        PostgresConnectionFactory factory,
        Guid fillId,
        decimal price,
        decimal sizeShares,
        decimal realizedPnlUsd)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_fills
SET price = @Price,
    size_shares = @SizeShares,
    realized_pnl_usd = @RealizedPnlUsd
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", fillId);
        command.Parameters.AddWithValue("Price", price);
        command.Parameters.AddWithValue("SizeShares", sizeShares);
        command.Parameters.AddWithValue("RealizedPnlUsd", realizedPnlUsd);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeletePaperFillAsync(PostgresConnectionFactory factory, Guid fillId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM paper_fills WHERE id = @Id;", connection);
        command.Parameters.AddWithValue("Id", fillId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeletePaperOrderAsync(PostgresConnectionFactory factory, Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM paper_orders WHERE id = @Id;", connection);
        command.Parameters.AddWithValue("Id", orderId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<IReadOnlyList<ProjectionRow>> ReadProjectionRowsAsync(
        PostgresConnectionFactory factory,
        string wallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT category, orders_count, filled_orders_count, buy_fills_count,
       buy_cost_usd, realized_pnl_usd
FROM paper_copied_trader_performance
WHERE copied_trader_wallet = @Wallet
ORDER BY category;
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ProjectionRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new ProjectionRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5)));
        }

        return rows;
    }

    private static async Task<OverallProjection?> ReadOverallProjectionAsync(
        PostgresConnectionFactory factory,
        string wallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT open_positions_count, settled_positions_count, realized_pnl_usd
FROM paper_copied_trader_performance
WHERE copied_trader_wallet = @Wallet
  AND category = 'OVERALL';
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new OverallProjection(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2))
            : null;
    }

    private static async Task LockProjectionRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string wallet)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT category
FROM paper_copied_trader_performance
WHERE copied_trader_wallet = @Wallet
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        await using var reader = await command.ExecuteReaderAsync();
        var rowsLocked = 0;
        while (await reader.ReadAsync())
        {
            rowsLocked++;
        }

        Assert.True(rowsLocked > 0);
    }

    private static async Task<int> ReadBackendPidAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid();", connection);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task LockQueuedWalletAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string wallet)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT copied_trader_wallet
FROM paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = @Wallet
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        Assert.Equal(wallet, Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }

    private static async Task LockPaperPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string wallet,
        string assetId)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT id
FROM paper_positions
WHERE copied_trader_wallet = @Wallet
  AND asset_id = @AssetId
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        Assert.IsType<Guid>(await command.ExecuteScalarAsync());
    }

    private static async Task LockPaperWalletAdvisoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string wallet)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@Wallet, 4937427318840178337));",
            connection,
            transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpdatePaperPositionMarkWithoutWalletLockForCasTestAsync(
        PostgresConnectionFactory factory,
        string wallet,
        string assetId,
        decimal estimatedValueUsd,
        decimal unrealizedPnlUsd,
        DateTimeOffset updatedAtUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_positions
SET estimated_value_usd = @EstimatedValueUsd,
    unrealized_pnl_usd = @UnrealizedPnlUsd,
    updated_at_utc = @UpdatedAtUtc
WHERE copied_trader_wallet = @Wallet
  AND asset_id = @AssetId;
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        command.Parameters.AddWithValue("EstimatedValueUsd", estimatedValueUsd);
        command.Parameters.AddWithValue("UnrealizedPnlUsd", unrealizedPnlUsd);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task LockPaperCopiedLeaderPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid positionId)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT id
FROM paper_copied_leader_positions
WHERE id = @Id
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Id", positionId);
        Assert.Equal(positionId, Assert.IsType<Guid>(await command.ExecuteScalarAsync()));
    }

    private static async Task<int> CountExpectedWalletAdvisoryLockKeysAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(DISTINCT hashtextextended(wallet, 4937427318840178337))::integer
FROM unnest(@Wallets) wallet;
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountGrantedAdvisoryLocksAsync(
        PostgresConnectionFactory factory,
        int backendPid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM pg_locks
WHERE pid = @BackendPid
  AND locktype = 'advisory'
  AND granted
  -- Wallet counts exclude only the existing shared retention gate.
  AND NOT (classid = 1346589778 AND objid = 1 AND objsubid = 2 AND mode = 'ShareLock');
""",
            connection);
        command.Parameters.AddWithValue("BackendPid", backendPid);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<BlockingSession?> ReadBlockingSessionAsync(
        PostgresConnectionFactory factory,
        string applicationName)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT activity.pid, activity.query, pg_blocking_pids(activity.pid)
FROM pg_stat_activity activity
WHERE activity.datname = current_database()
  AND activity.application_name = @ApplicationName
  AND activity.state = 'active'
  AND activity.wait_event_type = 'Lock'
ORDER BY activity.backend_start DESC
LIMIT 1;
""",
            connection);
        command.Parameters.AddWithValue("ApplicationName", applicationName);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new BlockingSession(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetFieldValue<int[]>(2))
            : null;
    }

    private static async Task<bool> IsProjectionBlockedByAsync(
        PostgresConnectionFactory factory,
        int blockerBackendPid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1
    FROM pg_stat_activity activity
    WHERE activity.datname = current_database()
      AND activity.state = 'active'
      AND activity.wait_event_type = 'Lock'
      AND @BlockerBackendPid = ANY(pg_blocking_pids(activity.pid))
      AND activity.query LIKE '%DELETE FROM paper_copied_trader_performance performance%'
);
""",
            connection);
        command.Parameters.AddWithValue("BlockerBackendPid", blockerBackendPid);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task PromoteQueuedWalletsAsync(
        PostgresConnectionFactory factory,
        params string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_copied_trader_performance_refresh_queue
SET priority = @Priority - array_position(@Wallets, copied_trader_wallet) + 1,
    requested_at_utc = @RequestedAtUtc
WHERE copied_trader_wallet = ANY(@Wallets);
""",
            connection);
        command.Parameters.AddWithValue("Priority", int.MaxValue);
        command.Parameters.AddWithValue("RequestedAtUtc", DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc));
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        Assert.Equal(wallets.Length, await command.ExecuteNonQueryAsync());
    }

    private static async Task QueueWalletsAsync(
        PostgresConnectionFactory factory,
        string[] wallets,
        int priority,
        string sourceKind)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_copied_trader_performance_refresh_queue (
    copied_trader_wallet, priority, requested_at_utc, source_kind)
SELECT wallet, @Priority, '-infinity'::timestamptz, @SourceKind
FROM unnest(@Wallets) wallet
ON CONFLICT (copied_trader_wallet) DO UPDATE SET
    priority = EXCLUDED.priority,
    requested_at_utc = EXCLUDED.requested_at_utc,
    source_kind = EXCLUDED.source_kind;
""",
            connection);
        command.Parameters.AddWithValue("Priority", priority);
        command.Parameters.AddWithValue("SourceKind", sourceKind);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        Assert.Equal(wallets.Length, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountQueuedWalletsAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = ANY(@Wallets);
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountAllQueuedWalletsAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM paper_copied_trader_performance_refresh_queue;",
            connection);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> CountInflightWalletsAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM paper_copied_trader_performance_refresh_inflight
WHERE copied_trader_wallet = ANY(@Wallets);
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<string?> ReadQueuedWalletSourceKindAsync(
        PostgresConnectionFactory factory,
        string wallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT source_kind
FROM paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = @Wallet;
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task DeleteQueuedWalletAsync(
        PostgresConnectionFactory factory,
        string wallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = @Wallet;
""",
            connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadMaximumSourceWalletAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(SourceWalletMaximumSql, connection);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<string?> ReadMinimumSourceWalletAfterAsync(
        PostgresConnectionFactory factory,
        string cursorWallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
WITH source_wallets AS (
    SELECT copied_trader_wallet AS wallet FROM paper_orders WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_positions WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_position_settlements WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_copied_trader_performance WHERE copied_trader_wallet <> ''
)
SELECT min(wallet)
FROM source_wallets
WHERE wallet > @Cursor;
""",
            connection);
        command.Parameters.AddWithValue("Cursor", cursorWallet);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task SetControlCursorToMaximumSourceWalletAsync(PostgresConnectionFactory factory)
    {
        var maximumWallet = await ReadMaximumSourceWalletAsync(factory);
        await SetControlCursorAsync(factory, maximumWallet);
    }

    private static async Task SetControlCursorAsync(
        PostgresConnectionFactory factory,
        string? cursorWallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_copied_trader_performance_projection_control
SET reconciliation_cursor_wallet = @Cursor,
    updated_at_utc = clock_timestamp()
WHERE singleton_id = 1;
""",
            connection);
        command.Parameters.Add("Cursor", NpgsqlDbType.Text).Value = (object?)cursorWallet ?? DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<ProjectionControlState> ReadControlStateAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT reconciliation_cursor_wallet, reconciliation_cycle,
       last_cycle_completed_at_utc, updated_at_utc
FROM paper_copied_trader_performance_projection_control
WHERE singleton_id = 1;
""",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Paper copied-trader projection control row is missing.");
        }

        return new ProjectionControlState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.GetDateTime(3));
    }

    private static async Task RestoreControlStateAsync(
        PostgresConnectionFactory factory,
        ProjectionControlState state)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE paper_copied_trader_performance_projection_control
SET reconciliation_cursor_wallet = @Cursor,
    reconciliation_cycle = @Cycle,
    last_cycle_completed_at_utc = @LastCycleCompletedAtUtc,
    updated_at_utc = @UpdatedAtUtc
WHERE singleton_id = 1;
""",
            connection);
        command.Parameters.Add("Cursor", NpgsqlDbType.Text).Value = (object?)state.CursorWallet ?? DBNull.Value;
        command.Parameters.AddWithValue("Cycle", state.Cycle);
        command.Parameters.Add("LastCycleCompletedAtUtc", NpgsqlDbType.TimestampTz).Value =
            (object?)state.LastCycleCompletedAtUtc ?? DBNull.Value;
        command.Parameters.AddWithValue(
            "UpdatedAtUtc",
            NpgsqlDbType.TimestampTz,
            DateTime.SpecifyKind(state.UpdatedAtUtc, DateTimeKind.Utc));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteTestRowsAsync(
        PostgresConnectionFactory factory,
        string[] wallets,
        Guid[] orderIds,
        Guid[] fillIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
CREATE TEMP TABLE temp_paper_performance_test_dashboard_sources
ON COMMIT DROP
AS
SELECT 'PaperPosition'::text AS source_kind, id AS source_id
FROM paper_positions
WHERE copied_trader_wallet = ANY(@Wallets)
UNION ALL
SELECT 'PaperSettlement'::text AS source_kind, id AS source_id
FROM paper_position_settlements
WHERE copied_trader_wallet = ANY(@Wallets);

DELETE FROM paper_fills WHERE id = ANY(@FillIds);
DELETE FROM paper_orders WHERE id = ANY(@OrderIds);
DELETE FROM paper_position_settlements WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_positions WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance_refresh_inflight WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM dashboard_projection_events
WHERE (source_kind = 'PaperOrder' AND source_id = ANY(@OrderIds))
   OR (source_kind = 'PaperFill' AND source_id = ANY(@FillIds))
   OR EXISTS (
       SELECT 1
       FROM temp_paper_performance_test_dashboard_sources source
       WHERE source.source_kind = dashboard_projection_events.source_kind
         AND source.source_id = dashboard_projection_events.source_id
   );
""",
            connection,
            transaction);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        command.Parameters.Add("OrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = orderIds;
        command.Parameters.Add("FillIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = fillIds;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task DeletePaperCopiedLeaderLockTestRowsAsync(
        PostgresConnectionFactory factory,
        Guid leaderPositionId,
        Guid activityEventId,
        string[] wallets,
        Guid[] orderIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM paper_copied_leader_activity_events WHERE id = @ActivityEventId;
DELETE FROM paper_copied_leader_positions WHERE id = @LeaderPositionId;
DELETE FROM paper_orders WHERE id = ANY(@OrderIds);
DELETE FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance_refresh_inflight WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM dashboard_projection_events
WHERE source_kind = 'PaperOrder'
  AND source_id = ANY(@OrderIds);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("ActivityEventId", activityEventId);
        command.Parameters.AddWithValue("LeaderPositionId", leaderPositionId);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        command.Parameters.Add("OrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = orderIds;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static async Task WaitForAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected PostgreSQL projection state was not observed in time.");
            }

            await Task.Delay(25);
        }
    }

    private const string SourceWalletMaximumSql = """
WITH source_wallets AS (
    SELECT copied_trader_wallet AS wallet FROM paper_orders WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_positions WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_position_settlements WHERE copied_trader_wallet <> ''
    UNION
    SELECT copied_trader_wallet AS wallet FROM paper_copied_trader_performance WHERE copied_trader_wallet <> ''
)
SELECT max(wallet) FROM source_wallets;
""";

    private sealed record ProjectionRow(
        string Category,
        int OrdersCount,
        int FilledOrdersCount,
        int BuyFillsCount,
        decimal BuyCostUsd,
        decimal RealizedPnlUsd);

    private sealed record OverallProjection(
        int OpenPositionsCount,
        int SettledPositionsCount,
        decimal RealizedPnlUsd);

    private sealed record OpenOnlyAggregateFixtureState(
        long TotalPositions,
        long ClosedPositions,
        long OpenPositions,
        bool AdversarialClosedPositionPresent);

    private sealed record ProjectionControlState(
        string? CursorWallet,
        long Cycle,
        DateTime? LastCycleCompletedAtUtc,
        DateTime UpdatedAtUtc);

    private sealed record ProjectionDifferenceCounts(
        int LegacyMinusPersisted,
        int PersistedMinusLegacy);

    private sealed record BlockingSession(
        int BackendPid,
        string Query,
        int[] BlockingBackendPids);

    private sealed record PaperCopiedLeaderPositionState(
        string Status,
        decimal CopiedInitialSizeShares,
        decimal LeaderSoldSizeShares,
        decimal CopiedExitRequestedSizeShares);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaperCopiedTraderPerformancePostgresIntegrationCollection
{
    public const string Name = "Paper copied-trader performance PostgreSQL integration";
}
