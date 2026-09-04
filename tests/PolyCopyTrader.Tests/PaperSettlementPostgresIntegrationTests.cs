using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;
using Xunit.Abstractions;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperSettlementPostgresIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ConditionalMarkUpdates_DoNotRestoreSettledPositions()
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
        var wallet = $"conditional-mark-{suffix}";
        var wallets = new[] { wallet };
        var initialUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        try
        {
            var initialPositions = new[]
            {
                Position(wallet, $"asset-{suffix}-single", $"condition-{suffix}", "Yes", 4m, 0.25m, initialUtc),
                Position(wallet, $"asset-{suffix}-batch", $"condition-{suffix}", "No", 3m, 0.40m, initialUtc)
            };
            await repository.UpsertPaperPositionsAsync(initialPositions);
            var expectedSingle = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPositions[0].AssetId));
            var expectedBatch = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPositions[1].AssetId));

            Assert.True(await repository.TryUpdatePaperPositionMarkAsync(
                expectedSingle,
                estimatedValueUsd: 3m,
                unrealizedPnlUsd: 2m,
                netUnrealizedPnlUsd: null,
                updatedAtUtc: initialUtc.AddMilliseconds(100)));
            expectedSingle = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPositions[0].AssetId));
            var successfulBatch = await repository.TryUpdatePaperPositionMarksAsync(
            [
                new PaperPositionMarkUpdate(
                    expectedBatch,
                    EstimatedValueUsd: 2m,
                    UnrealizedPnlUsd: 0.8m,
                    UpdatedAtUtc: initialUtc.AddMilliseconds(100))
            ]);
            Assert.Single(successfulBatch);
            expectedBatch = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, initialPositions[1].AssetId));

            var settledUtc = initialUtc.AddSeconds(1);
            var writes = new[]
            {
                SettlementWrite(expectedSingle, won: true, settledUtc),
                SettlementWrite(expectedBatch, won: false, settledUtc)
            };
            Assert.Equal(2, await repository.PersistPaperPositionSettlementBatchAsync(writes));

            var singleUpdated = await repository.TryUpdatePaperPositionMarkAsync(
                expectedSingle,
                estimatedValueUsd: 3m,
                unrealizedPnlUsd: 2m,
                netUnrealizedPnlUsd: null,
                updatedAtUtc: settledUtc.AddSeconds(1));
            var batchUpdated = await repository.TryUpdatePaperPositionMarksAsync(
            [
                new PaperPositionMarkUpdate(
                    expectedBatch,
                    EstimatedValueUsd: 2m,
                    UnrealizedPnlUsd: 0.8m,
                    UpdatedAtUtc: settledUtc.AddSeconds(1))
            ]);

            Assert.False(singleUpdated);
            Assert.Empty(batchUpdated);
            Assert.Equal(0m, (await repository.GetPaperPositionAsync(wallet, expectedSingle.AssetId))?.SizeShares);
            Assert.Equal(0m, (await repository.GetPaperPositionAsync(wallet, expectedBatch.AssetId))?.SizeShares);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task BatchMarkUpdates_SkipLockedPositionAndUpdateFreePosition()
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
        var lockedWallet = $"mark-locked-{suffix}";
        var freeWallet = $"mark-free-{suffix}";
        var wallets = new[] { lockedWallet, freeWallet };
        var initialUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var lockedPosition = Position(
            lockedWallet,
            $"asset-{suffix}-locked",
            $"condition-{suffix}",
            "Yes",
            4m,
            0.25m,
            initialUtc);
        var freePosition = Position(
            freeWallet,
            $"asset-{suffix}-free",
            $"condition-{suffix}",
            "No",
            3m,
            0.40m,
            initialUtc);

        try
        {
            await repository.UpsertPaperPositionsAsync([lockedPosition, freePosition]);
            var expectedLocked = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(lockedWallet, lockedPosition.AssetId));
            var expectedFree = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(freeWallet, freePosition.AssetId));
            var markUtc = initialUtc.AddSeconds(1);

            await using (var blockerConnection = factory.CreateConnection())
            {
                await blockerConnection.OpenAsync();
                await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
                await using (var lockCommand = new NpgsqlCommand(
                    """
SELECT id
FROM paper_positions
WHERE copied_trader_wallet = @Wallet
  AND asset_id = @AssetId
FOR UPDATE;
""",
                    blockerConnection,
                    blockerTransaction))
                {
                    lockCommand.Parameters.AddWithValue("Wallet", lockedWallet);
                    lockCommand.Parameters.AddWithValue("AssetId", lockedPosition.AssetId);
                    Assert.IsType<Guid>(await lockCommand.ExecuteScalarAsync());
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var updated = await repository.TryUpdatePaperPositionMarksAsync(
                [
                    new PaperPositionMarkUpdate(
                        expectedLocked,
                        EstimatedValueUsd: 3m,
                        UnrealizedPnlUsd: 2m,
                        UpdatedAtUtc: markUtc),
                    new PaperPositionMarkUpdate(
                        expectedFree,
                        EstimatedValueUsd: 2m,
                        UnrealizedPnlUsd: 0.8m,
                        UpdatedAtUtc: markUtc)
                ],
                    timeout.Token);

                var persistedFree = Assert.Single(updated);
                Assert.Equal(freeWallet, persistedFree.CopiedTraderWallet);
                Assert.Equal(2m, persistedFree.EstimatedValueUsd);
                Assert.Equal(0.8m, persistedFree.UnrealizedPnlUsd);

                var unchangedLocked = Assert.IsType<PaperPosition>(
                    await repository.GetPaperPositionAsync(lockedWallet, lockedPosition.AssetId));
                Assert.Equal(expectedLocked, unchangedLocked);

                await blockerTransaction.CommitAsync();
            }

            var retried = await repository.TryUpdatePaperPositionMarksAsync(
            [
                new PaperPositionMarkUpdate(
                    expectedLocked,
                    EstimatedValueUsd: 3m,
                    UnrealizedPnlUsd: 2m,
                    UpdatedAtUtc: markUtc)
            ]);

            var persistedLocked = Assert.Single(retried);
            Assert.Equal(lockedWallet, persistedLocked.CopiedTraderWallet);
            Assert.Equal(3m, persistedLocked.EstimatedValueUsd);
            Assert.Equal(2m, persistedLocked.UnrealizedPnlUsd);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task StageAwareBatchMarkUpdate_ReportsExactStagesWithoutChangingPersistedResult()
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
        var wallet = $"mark-stage-{suffix}";
        var wallets = new[] { wallet };
        var initialUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var position = Position(
            wallet,
            $"asset-{suffix}-stage",
            $"condition-{suffix}",
            "Yes",
            4m,
            0.25m,
            initialUtc);

        try
        {
            await repository.UpsertPaperPositionAsync(position);
            var expected = Assert.IsType<PaperPosition>(
                await repository.GetPaperPositionAsync(wallet, position.AssetId));
            var stages = new List<string>();

            var persisted = Assert.Single(await repository.TryUpdatePaperPositionMarksAsync(
                [
                    new PaperPositionMarkUpdate(
                        expected,
                        EstimatedValueUsd: 3m,
                        UnrealizedPnlUsd: 2m,
                        UpdatedAtUtc: initialUtc.AddSeconds(1))
                ],
                stages.Add));

            Assert.Equal(
                [
                    PaperPositionMarkPersistenceStages.OpenConnection,
                    PaperPositionMarkPersistenceStages.SerializeUpdates,
                    PaperPositionMarkPersistenceStages.ExecuteCommand,
                    PaperPositionMarkPersistenceStages.ReadResults
                ],
                stages);
            Assert.Equal(3m, persisted.EstimatedValueUsd);
            Assert.Equal(2m, persisted.UnrealizedPnlUsd);
            Assert.Equal(
                persisted,
                await repository.GetPaperPositionAsync(wallet, position.AssetId));
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SettlementBatch_FiltersMarketAndRollsBackBothTablesOnFailure()
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
        var conditionId = $"Condition-{suffix}";
        var wallets = new[]
        {
            $"settlement-batch-{suffix}-one",
            $"settlement-batch-{suffix}-two",
            $"settlement-batch-{suffix}-unrelated",
            $"settlement-batch-{suffix}-rollback"
        };
        var nowUtc = DateTimeOffset.UtcNow;

        try
        {
            var positions = new[]
            {
                Position(wallets[0], $"asset-{suffix}-yes", conditionId, "Yes", 4m, 0.25m, nowUtc),
                Position(wallets[1], $"asset-{suffix}-no", conditionId, "No", 3m, 0.40m, nowUtc),
                Position(wallets[2], $"asset-{suffix}-other", $"other-{suffix}", "Yes", 2m, 0.50m, nowUtc)
            };
            await repository.UpsertPaperPositionsAsync(positions);

            var matching = await repository.GetOpenPaperPositionsForMarketAsync(
                conditionId.ToLowerInvariant(),
                null);
            Assert.Equal(2, matching.Count);
            Assert.DoesNotContain(matching, position => position.CopiedTraderWallet == wallets[2]);

            var writes = matching
                .Select(position => SettlementWrite(position, position.Outcome == "Yes", nowUtc))
                .ToArray();
            var inserted = await repository.PersistPaperPositionSettlementBatchAsync(writes);

            Assert.Equal(2, inserted);
            Assert.All(
                await Task.WhenAll(
                    repository.GetPaperPositionAsync(wallets[0], positions[0].AssetId),
                    repository.GetPaperPositionAsync(wallets[1], positions[1].AssetId)),
                position => Assert.Equal(0m, Assert.IsType<PaperPosition>(position).SizeShares));
            Assert.Equal(
                2m,
                (await repository.GetPaperPositionAsync(wallets[2], positions[2].AssetId))?.SizeShares);
            Assert.Equal(2, await CountSettlementsAsync(factory, wallets));

            var rollbackPosition = Position(
                wallets[3],
                $"asset-{suffix}-rollback",
                conditionId,
                "Yes",
                2m,
                0.50m,
                nowUtc);
            await repository.UpsertPaperPositionAsync(rollbackPosition);
            var rollbackWrite = SettlementWrite(rollbackPosition, won: true, nowUtc) with
            {
                SettledPosition = rollbackPosition with
                {
                    ConditionId = null!,
                    SizeShares = 0m,
                    AveragePrice = 0m,
                    EstimatedValueUsd = 0m,
                    UnrealizedPnlUsd = 0m
                }
            };

            await Assert.ThrowsAsync<PostgresException>(() =>
                repository.PersistPaperPositionSettlementBatchAsync([rollbackWrite]));

            Assert.Equal(2, await CountSettlementsAsync(factory, wallets));
            Assert.Equal(
                2m,
                (await repository.GetPaperPositionAsync(wallets[3], rollbackPosition.AssetId))?.SizeShares);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallets);
        }
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [Trait("Category", "PostgresIntegration")]
    public async Task SingletonSettlement_AndBatch_UseCommonWalletSerialization(
        bool singletonFirst,
        bool rollbackBlocker,
        int settlementCount)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var factory = await CreateWalletTestFactoryAsync();
        var wallet = $"settlement-serialized-{suffix}";
        var freeWallet = $"settlement-free-{suffix}";
        var wallets = new[] { wallet, freeWallet };
        var singletonName = $"singleton-{suffix}";
        var batchName = $"batch-{suffix}";
        var singleton = WalletTestRepository(factory, singletonName);
        var batch = WalletTestRepository(factory, batchName);
        var repository = new PostgresAppRepository(factory);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var positions = Enumerable.Range(0, settlementCount)
            .Select(index => Position(wallet, $"asset-{suffix}-{index}", $"condition-{suffix}",
                index == 0 ? "Yes" : "No", 4m, 0.25m, now)).ToArray();
        var writes = positions.Select((position, index) =>
        {
            var write = SettlementWrite(position, index == 0, now.AddSeconds(1));
            return write with { Settlement = WithWalletTestFees(write.Settlement, index == 0) };
        }).ToArray();
        var singleSettlement = writes[0].Settlement with { Id = Guid.NewGuid() };
        var freePosition = Position(freeWallet, $"free-asset-{suffix}", $"free-condition-{suffix}",
            "Yes", 2m, 0.50m, now);
        var freeSettlement = WithWalletTestFees(
            SettlementWrite(freePosition, true, now.AddSeconds(1)).Settlement, true);
        Task<bool>? singletonTask = null;
        Task<int>? batchTask = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var blocker = factory.CreateConnection();
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        var released = false;
        try
        {
            await repository.UpsertPaperPositionsAsync([.. positions, freePosition]);
            await LockWalletQueueAsync(blocker, transaction, wallet);
            var blockerPid = blocker.ProcessID;
            SettlementSession firstWait;
            SettlementSession secondWait;
            if (singletonFirst)
            {
                singletonTask = singleton.TryAddPaperPositionSettlementAsync(singleSettlement, timeout.Token);
                firstWait = await WaitForSettlementSessionAsync(factory, singletonName,
                    session => session.Blockers.Contains(blockerPid));
                batchTask = batch.PersistPaperPositionSettlementBatchAsync(writes, timeout.Token);
                secondWait = await WaitForSettlementSessionAsync(factory, batchName,
                    session => session.Blockers.Length > 0);
            }
            else
            {
                batchTask = batch.PersistPaperPositionSettlementBatchAsync(writes, timeout.Token);
                firstWait = await WaitForSettlementSessionAsync(factory, batchName,
                    session => session.Blockers.Contains(blockerPid));
                singletonTask = singleton.TryAddPaperPositionSettlementAsync(singleSettlement, timeout.Token);
                secondWait = await WaitForSettlementSessionAsync(factory, singletonName,
                    session => session.Blockers.Length > 0);
            }
            output.WriteLine($"Before release: first={firstWait}; second={secondWait}; blocker={blockerPid}");

            // A different wallet must still make progress while both competing paths are held.
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(freeSettlement, timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(singletonTask.IsCompleted);
            Assert.False(batchTask.IsCompleted);
            if (rollbackBlocker)
                await transaction.RollbackAsync();
            else
                await transaction.CommitAsync();
            released = true;

            var failure = await Record.ExceptionAsync(async () =>
                await Task.WhenAll(singletonTask, batchTask).WaitAsync(TimeSpan.FromSeconds(10)));
            output.WriteLine($"Singleton outcome: {singletonTask.Status} {singletonTask.Exception}");
            output.WriteLine($"Batch outcome: {batchTask.Status} {batchTask.Exception}");
            Assert.Null(failure);
            Assert.Contains("pg_advisory_xact_lock", secondWait.Query, StringComparison.Ordinal);
            Assert.Contains(firstWait.Pid, secondWait.Blockers);
            Assert.Equal("advisory", secondWait.WaitEvent);
            Assert.Equal(singletonFirst, await singletonTask);
            Assert.Equal(settlementCount - (singletonFirst ? 1 : 0), await batchTask);

            var expected = writes.Select((write, index) =>
                index == 0 && singletonFirst ? singleSettlement : write.Settlement).ToArray();
            await AssertWalletSettlementAccountingAsync(factory, repository, wallet, expected);
            foreach (var position in positions)
                Assert.Equal(0m, (await repository.GetPaperPositionAsync(wallet, position.AssetId))?.SizeShares);
            // A standalone settlement must not acquire new position-closing responsibilities.
            Assert.Equal(freePosition.SizeShares,
                (await repository.GetPaperPositionAsync(freeWallet, freePosition.AssetId))?.SizeShares);
            Assert.False(await singleton.TryAddPaperPositionSettlementAsync(singleSettlement with
            {
                Id = Guid.NewGuid(), FeeUsd = 99m, NetRealizedPnlUsd = -99m
            }, timeout.Token));
            Assert.Equal(0, await batch.PersistPaperPositionSettlementBatchAsync(writes, timeout.Token));
            await AssertWalletSettlementAccountingAsync(factory, repository, wallet, expected);
            await AssertNoWalletAdvisoryLockAsync(factory, wallet);
        }
        finally
        {
            if (!released)
                await transaction.RollbackAsync();
            timeout.Cancel();
            await DrainSettlementTaskAsync(singletonTask);
            await DrainSettlementTaskAsync(batchTask);
            await DeleteTestRowsAsync(factory, wallets);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgresIntegration")]
    public async Task SingletonSettlement_CancellationReleasesTransactionAndAllowsRetry(bool afterInsert)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var factory = await CreateWalletTestFactoryAsync();
        var wallet = $"settlement-cancel-{suffix}";
        var appName = $"cancel-{suffix}";
        var repository = WalletTestRepository(factory, appName);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var position = Position(wallet, $"asset-{suffix}", $"condition-{suffix}", "Yes", 4m, 0.25m, now);
        var settlement = WithWalletTestFees(SettlementWrite(position, true, now).Settlement, true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var blocker = factory.CreateConnection();
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        Task<bool>? task = null;
        var released = false;
        try
        {
            await repository.UpsertPaperPositionAsync(position);
            if (afterInsert)
                await LockWalletQueueAsync(blocker, transaction, wallet);
            else
            {
                await using var command = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@Wallet, 4937427318840178337));",
                    blocker, transaction);
                command.Parameters.AddWithValue("Wallet", wallet);
                await command.ExecuteNonQueryAsync();
            }
            task = repository.TryAddPaperPositionSettlementAsync(settlement, cancellation.Token);
            var wait = await WaitForSettlementSessionAsync(factory, appName,
                session => session.Blockers.Contains(blocker.ProcessID));
            output.WriteLine($"Cancellation afterInsert={afterInsert}: {wait}");
            Assert.Contains(afterInsert ? "INSERT INTO paper_position_settlements" : "pg_advisory_xact_lock",
                wait.Query, StringComparison.Ordinal);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            Assert.Equal(0, await CountSettlementsAsync(factory, [wallet]));
            Assert.Equal(0, await CountSettlementEventsAsync(factory, settlement.Id));
            await transaction.RollbackAsync();
            released = true;
            await AssertNoWalletAdvisoryLockAsync(factory, wallet);
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(settlement));
            await AssertWalletSettlementAccountingAsync(factory, repository, wallet, [settlement]);
            Assert.False(await repository.TryAddPaperPositionSettlementAsync(settlement));
            await AssertNoWalletAdvisoryLockAsync(factory, wallet);
        }
        finally
        {
            if (!released)
                await transaction.RollbackAsync();
            cancellation.Cancel();
            await DrainSettlementTaskAsync(task);
            await DeleteTestRowsAsync(factory, [wallet]);
        }
    }

    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SingletonSettlement_TriggerFailureRollsBackRowAndQueueAndAllowsRetry()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var factory = await CreateWalletTestFactoryAsync();
        var wallet = $"settlement-error-{suffix}";
        var repository = new PostgresAppRepository(factory);
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var position = Position(wallet, $"asset-{suffix}", $"condition-{suffix}", "Yes", 4m, 0.25m, now);
        var settlement = WithWalletTestFees(SettlementWrite(position, true, now).Settlement, false);
        var function = $"wallet_settlement_failure_{suffix}";
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        var installed = false;
        try
        {
            await using (var command = new NpgsqlCommand($$"""
CREATE FUNCTION public.{{function}}() RETURNS trigger LANGUAGE plpgsql AS $body$
BEGIN
    IF NEW.copied_trader_wallet = '{{wallet}}' THEN
        IF NOT EXISTS (SELECT 1 FROM paper_copied_trader_performance_refresh_queue
                       WHERE copied_trader_wallet = NEW.copied_trader_wallet) THEN
            RAISE EXCEPTION 'test queue trigger did not execute';
        END IF;
        RAISE EXCEPTION 'expected singleton after-trigger failure' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$body$;
CREATE TRIGGER zz_{{function}} AFTER INSERT ON paper_position_settlements
FOR EACH ROW EXECUTE FUNCTION public.{{function}}();
""", connection))
            {
                await command.ExecuteNonQueryAsync();
                installed = true;
            }
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                repository.TryAddPaperPositionSettlementAsync(settlement));
            Assert.Equal("P0001", failure.SqlState);
            Assert.Equal("expected singleton after-trigger failure", failure.MessageText);
            Assert.Equal(0, await CountSettlementsAsync(factory, [wallet]));
            await using (var command = new NpgsqlCommand(
                "SELECT count(*)::integer FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet = @Wallet;",
                connection))
            {
                command.Parameters.AddWithValue("Wallet", wallet);
                Assert.Equal(0, await command.ExecuteScalarAsync());
            }
            Assert.Equal(0, await CountSettlementEventsAsync(factory, settlement.Id));
            await AssertNoWalletAdvisoryLockAsync(factory, wallet);
            await using (var command = new NpgsqlCommand(
                $"DROP TRIGGER zz_{function} ON paper_position_settlements; DROP FUNCTION public.{function}();", connection))
                await command.ExecuteNonQueryAsync();
            installed = false;
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(settlement));
            await AssertWalletSettlementAccountingAsync(factory, repository, wallet, [settlement]);
            await AssertNoWalletAdvisoryLockAsync(factory, wallet);
        }
        finally
        {
            if (installed)
            {
                await using var command = new NpgsqlCommand(
                    $"DROP TRIGGER IF EXISTS zz_{function} ON paper_position_settlements; DROP FUNCTION IF EXISTS public.{function}();",
                    connection);
                await command.ExecuteNonQueryAsync();
            }
            await DeleteTestRowsAsync(factory, [wallet]);
        }
    }

    private static async Task<PostgresConnectionFactory> CreateWalletTestFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString), "An isolated PostgreSQL fixture is required.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Contains(settings.Host, new[] { "127.0.0.1", "localhost", "::1" });
        Assert.StartsWith("pct_codex_", settings.Database);
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await using (var connection = factory.CreateConnection())
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT current_database(), current_setting('data_directory');", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(settings.Database, reader.GetString(0));
            Assert.StartsWith("D:/CodexTemp/runs/", reader.GetString(1).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static PostgresAppRepository WalletTestRepository(PostgresConnectionFactory factory, string applicationName) =>
        new(new PostgresConnectionFactory(new StorageOptions { ConnectionString = factory.ConnectionString }, applicationName));

    private static PaperPositionSettlement WithWalletTestFees(PaperPositionSettlement settlement, bool known) =>
        settlement with
        {
            FeeUsd = known ? 0.17m : 0m,
            FeeAccountingStatus = known ? "Calculated" : "LegacyUnknown",
            FeeLiquidityRole = known ? "Taker" : "Unknown",
            FeeCalculationSource = known ? "wallet_serialization_test" : "",
            FeeRate = known ? 0.25m : null,
            FeeExponent = known ? 2 : null,
            FeeTakerOnly = known ? true : null,
            FeeCalculatedAtUtc = known ? settlement.CreatedAtUtc : null,
            NetRealizedPnlUsd = known ? settlement.SettlementValueUsd - settlement.CostBasisUsd - 0.17m : null
        };

    private static async Task LockWalletQueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string wallet)
    {
        await using var command = new NpgsqlCommand(
            "SELECT copied_trader_wallet FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet = @Wallet FOR UPDATE;",
            connection, transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        Assert.Equal(wallet, await command.ExecuteScalarAsync());
    }

    private static async Task<SettlementSession> WaitForSettlementSessionAsync(
        PostgresConnectionFactory factory, string applicationName, Func<SettlementSession, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
        SettlementSession? last = null;
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using (var command = new NpgsqlCommand("""
SELECT pid, COALESCE(wait_event, ''), query, pg_blocking_pids(pid)
FROM pg_stat_activity WHERE datname = current_database() AND application_name = @ApplicationName AND state = 'active';
""", connection))
            {
                command.Parameters.AddWithValue("ApplicationName", applicationName);
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    last = new SettlementSession(reader.GetInt32(0), reader.GetString(1),
                        reader.GetString(2), reader.GetFieldValue<int[]>(3));
                    if (predicate(last))
                        return last;
                }
            }
            await Task.Delay(20);
        }
        Assert.Fail($"Expected blocking session not observed: {applicationName}; last={last}");
        throw new InvalidOperationException();
    }

    private static async Task AssertNoWalletAdvisoryLockAsync(PostgresConnectionFactory factory, string wallet)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(hashtextextended(@Wallet, 4937427318840178337));", connection, transaction);
        command.Parameters.AddWithValue("Wallet", wallet);
        Assert.Equal(true, await command.ExecuteScalarAsync());
        await transaction.RollbackAsync();
    }

    private static async Task AssertWalletSettlementAccountingAsync(PostgresConnectionFactory factory,
        PostgresAppRepository repository, string wallet, IReadOnlyList<PaperPositionSettlement> expected)
    {
        var rows = (await repository.GetRecentPaperPositionSettlementsAsync(1000))
            .Where(row => row.CopiedTraderWallet == wallet).ToArray();
        Assert.Equal(expected.Count, rows.Length);
        foreach (var row in expected)
            Assert.Equal(row, Assert.Single(rows, actual => actual.AssetId == row.AssetId));
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
SELECT count(*)::integer, count(DISTINCT asset_id)::integer,
       COALESCE(sum(cost_basis_usd),0), COALESCE(sum(settlement_value_usd),0),
       COALESCE(sum(realized_pnl_usd),0), COALESCE(sum(fee_usd),0),
       COALESCE(sum(net_realized_pnl_usd),0),
       count(*) FILTER (WHERE net_realized_pnl_usd IS NULL)::integer
FROM paper_position_settlements WHERE copied_trader_wallet = @Wallet;
""", connection);
        command.Parameters.AddWithValue("Wallet", wallet);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expected.Count, reader.GetInt32(0));
        Assert.Equal(expected.Count, reader.GetInt32(1));
        Assert.Equal(expected.Sum(row => row.SettledSizeShares * row.AveragePrice), reader.GetDecimal(2));
        Assert.Equal(expected.Sum(row => row.Won ? row.SettledSizeShares : 0m), reader.GetDecimal(3));
        Assert.Equal(expected.Sum(row => (row.Won ? row.SettledSizeShares : 0m) -
            row.SettledSizeShares * row.AveragePrice), reader.GetDecimal(4));
        Assert.Equal(expected.Sum(row => row.FeeUsd), reader.GetDecimal(5));
        Assert.Equal(expected.Where(row => row.NetRealizedPnlUsd.HasValue).Sum(row =>
            (row.Won ? row.SettledSizeShares : 0m) - row.SettledSizeShares * row.AveragePrice - row.FeeUsd),
            reader.GetDecimal(6));
        Assert.Equal(expected.Count(row => !row.NetRealizedPnlUsd.HasValue), reader.GetInt32(7));
    }

    private static async Task<int> CountSettlementEventsAsync(PostgresConnectionFactory factory, Guid settlementId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM dashboard_projection_events WHERE source_kind = 'PaperSettlement' AND source_id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", settlementId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task DrainSettlementTaskAsync(Task? task)
    {
        if (task is null)
            return;
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception) when (task.IsCompleted) { }
    }

    private sealed record SettlementSession(int Pid, string WaitEvent, string Query, int[] Blockers)
    {
        public override string ToString() => $"pid={Pid}; wait={WaitEvent}; blockers={string.Join(',', Blockers)}; sql={Query}";
    }

    private static PaperPosition Position(
        string wallet,
        string assetId,
        string conditionId,
        string outcome,
        decimal sizeShares,
        decimal averagePrice,
        DateTimeOffset nowUtc)
    {
        return new PaperPosition(
            assetId,
            conditionId,
            outcome,
            sizeShares,
            averagePrice,
            sizeShares * averagePrice,
            0m,
            nowUtc,
            wallet);
    }

    private static PaperPositionSettlementWrite SettlementWrite(
        PaperPosition position,
        bool won,
        DateTimeOffset nowUtc)
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
                won ? position.Outcome : "Yes",
                "IntegrationTest",
                position.SizeShares,
                position.AveragePrice,
                costBasis,
                settlementValue,
                settlementValue - costBasis,
                won,
                "IntegrationTest",
                nowUtc,
                nowUtc),
            position with
            {
                SizeShares = 0m,
                AveragePrice = 0m,
                EstimatedValueUsd = 0m,
                UnrealizedPnlUsd = 0m,
                UpdatedAtUtc = nowUtc
            });
    }

    private static async Task<int> CountSettlementsAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*)::integer FROM paper_position_settlements WHERE copied_trader_wallet = ANY(@Wallets);",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task DeleteTestRowsAsync(
        PostgresConnectionFactory factory,
        string[] wallets)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM paper_position_settlements WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_positions WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance WHERE copied_trader_wallet = ANY(@Wallets);
DELETE FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet = ANY(@Wallets);
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        await command.ExecuteNonQueryAsync();
    }
}
