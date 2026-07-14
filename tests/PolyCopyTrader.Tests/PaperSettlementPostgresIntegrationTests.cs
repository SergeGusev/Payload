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
    public async Task SettlementBatch_FiltersMarketAndRollsBackBothTablesOnFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await EnsurePaperTablesAsync(factory);
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

    private static async Task EnsurePaperTablesAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
CREATE TABLE IF NOT EXISTS paper_positions (
    id uuid PRIMARY KEY,
    copied_trader_wallet text NOT NULL DEFAULT '',
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    size_shares numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    estimated_value_usd numeric(28,8) NOT NULL,
    unrealized_pnl_usd numeric(28,8) NOT NULL,
    updated_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_paper_positions_wallet_asset
ON paper_positions(copied_trader_wallet, asset_id);

CREATE TABLE IF NOT EXISTS paper_position_settlements (
    id uuid PRIMARY KEY,
    copied_trader_wallet text NOT NULL,
    asset_id text NOT NULL,
    condition_id text NOT NULL,
    outcome text NOT NULL,
    winning_asset_id text NULL,
    winning_outcome text NOT NULL,
    category text NULL,
    settled_size_shares numeric(28,8) NOT NULL,
    average_price numeric(18,8) NOT NULL,
    cost_basis_usd numeric(28,8) NOT NULL,
    settlement_value_usd numeric(28,8) NOT NULL,
    realized_pnl_usd numeric(28,8) NOT NULL,
    won boolean NOT NULL,
    settlement_source text NOT NULL,
    settled_at_utc timestamptz NOT NULL,
    created_at_utc timestamptz NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_paper_position_settlements_wallet_asset
ON paper_position_settlements(copied_trader_wallet, asset_id);
""",
            connection);
        await command.ExecuteNonQueryAsync();
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
""",
            connection);
        command.Parameters.Add("Wallets", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = wallets;
        await command.ExecuteNonQueryAsync();
    }
}
