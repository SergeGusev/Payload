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
        var orderIds = new[] { firstOrderId, secondOrderId };
        var fillIds = new[] { fillId };
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

            await UpdatePaperFillAsync(factory, fillId, 0.50m, 6m, 3.50m);
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterUpdate = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterUpdate.WalletsProcessed);
            Assert.Equal(2, afterUpdate.PerformanceRowsWritten);
            AssertProjectionRows(
                await ReadProjectionRowsAsync(factory, firstWallet),
                ordersCount: 1,
                filledOrdersCount: 1,
                buyFillsCount: 1,
                buyCostUsd: 3m,
                realizedPnlUsd: 3.50m);

            await DeletePaperFillAsync(factory, fillId);
            Assert.Equal("paper_fill", await ReadQueuedWalletSourceKindAsync(factory, firstWallet));

            var afterFillDelete = await RefreshExactWalletAsync(factory, repository, firstWallet);
            Assert.Equal(1, afterFillDelete.WalletsProcessed);
            Assert.Equal(2, afterFillDelete.PerformanceRowsWritten);
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

    private sealed record ProjectionControlState(
        string? CursorWallet,
        long Cycle,
        DateTime? LastCycleCompletedAtUtc,
        DateTime UpdatedAtUtc);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaperCopiedTraderPerformancePostgresIntegrationCollection
{
    public const string Name = "Paper copied-trader performance PostgreSQL integration";
}
