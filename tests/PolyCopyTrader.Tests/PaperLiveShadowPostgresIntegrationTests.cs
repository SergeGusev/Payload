using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperLiveShadowPostgresIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ConcurrentReconciliationReplacesMixedFillsAndPreservesUnrelatedPositionBasis()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var firstRepository = new PostgresAppRepository(factory);
        var secondRepository = new PostgresAppRepository(factory);
        var strategyId = await ReadFirstStrategyIdAsync(factory);
        var suffix = Guid.NewGuid().ToString("N");
        var wallet = $"paper-live-shadow-{suffix}";
        var assetId = $"asset-{suffix}";
        var conditionId = $"condition-{suffix}";
        var signalId = Guid.NewGuid();
        var paperOrderId = Guid.NewGuid();
        var liveOrderId = Guid.NewGuid();
        var firstFillId = Guid.NewGuid();
        var secondFillId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        try
        {
            await firstRepository.AddPaperOrderAsync(new PaperOrder(
                paperOrderId,
                signalId,
                wallet,
                PaperOrderStatus.Filled,
                TradeSide.Buy,
                assetId,
                conditionId,
                "Yes",
                0.50m,
                4m,
                2m,
                now.AddMinutes(-1),
                now.AddMinutes(4),
                FilledAtUtc: now.AddSeconds(-10),
                StrategyId: strategyId,
                RawDecisionJson: "{\"paper_live_shadow_test\":true}",
                CorrelationId: correlationId,
                ExecutionSource: "paper_live_shadow_actual_fill"));
            await firstRepository.AddPaperFillAsync(new PaperFill(
                firstFillId,
                paperOrderId,
                0.99m,
                2m,
                now.AddSeconds(-20),
                "BalancedGtcDepth"));
            await firstRepository.AddPaperFillAsync(new PaperFill(
                secondFillId,
                paperOrderId,
                0.50m,
                2m,
                now.AddSeconds(-10),
                "live delta"));
            await firstRepository.UpsertPaperPositionAsync(new PaperPosition(
                assetId,
                conditionId,
                "Yes",
                5m,
                0.656m,
                2.50m,
                -0.78m,
                now.AddSeconds(-10),
                wallet));
            await firstRepository.AddLiveOrderAsync(new LiveOrder(
                liveOrderId,
                signalId,
                LiveOrderStatus.Matched,
                "0xpostgres-shadow",
                TradeSide.Buy,
                assetId,
                conditionId,
                "Yes",
                0.99m,
                4m,
                3.96m,
                "FAK",
                now.AddMinutes(-1),
                now.AddMinutes(4),
                now.AddSeconds(-15),
                "matched",
                4m,
                0m,
                string.Empty,
                "{}",
                string.Empty,
                now.AddSeconds(-10),
                StrategyId: strategyId,
                AverageFillPrice: 0.50m,
                FilledNotionalUsd: 2m,
                CostBasisUsd: 2m,
                CorrelationId: correlationId,
                ExecutionSource: "paper_live_shadow_test",
                PostOnly: false,
                PaperOrderId: paperOrderId));

            var request = new PaperLiveShadowFillReconciliationRequest(paperOrderId, liveOrderId, now);
            await Task.WhenAll(
                firstRepository.ReconcilePaperLiveShadowFillAsync(request),
                secondRepository.ReconcilePaperLiveShadowFillAsync(request)).WaitAsync(TimeSpan.FromSeconds(15));

            var order = await firstRepository.GetPaperOrderAsync(paperOrderId);
            Assert.NotNull(order);
            Assert.Equal(PaperOrderStatus.Filled, order.Status);
            Assert.Equal("paper_live_shadow_actual_fill", order.ExecutionSource);
            Assert.Equal(0.50m, order.Price);
            Assert.Equal(4m, order.SizeShares);
            Assert.Equal(2m, order.NotionalUsd);
            var fill = Assert.Single(await firstRepository.GetPaperFillsForOrderAsync(paperOrderId));
            Assert.Equal(0.50m, fill.Price);
            Assert.Equal(4m, fill.SizeShares);
            Assert.Equal(2m, fill.Price * fill.SizeShares);
            var position = await firstRepository.GetPaperPositionAsync(wallet, assetId);
            Assert.NotNull(position);
            Assert.Equal(5m, position.SizeShares);
            Assert.Equal(0.46m, position.AveragePrice);
            Assert.Equal(2.30m, position.SizeShares * position.AveragePrice);
        }
        finally
        {
            await DeleteTestRowsAsync(factory, wallet, assetId, liveOrderId, paperOrderId);
        }
    }

    private static async Task<Guid> ReadFirstStrategyIdAsync(PostgresConnectionFactory factory)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM strategies ORDER BY id LIMIT 1;", connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? StrategyIds.FollowLeader);
    }

    private static async Task DeleteTestRowsAsync(
        PostgresConnectionFactory factory,
        string wallet,
        string assetId,
        Guid liveOrderId,
        Guid paperOrderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
DELETE FROM live_orders WHERE id = @LiveOrderId;
DELETE FROM paper_fills WHERE paper_order_id = @PaperOrderId;
DELETE FROM paper_positions WHERE copied_trader_wallet = @Wallet AND asset_id = @AssetId;
DELETE FROM paper_orders WHERE id = @PaperOrderId;
""", connection, transaction);
        command.Parameters.AddWithValue("LiveOrderId", liveOrderId);
        command.Parameters.AddWithValue("PaperOrderId", paperOrderId);
        command.Parameters.AddWithValue("Wallet", wallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
