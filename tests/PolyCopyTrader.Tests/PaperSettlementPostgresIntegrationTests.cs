using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperSettlementPostgresIntegrationTests
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
