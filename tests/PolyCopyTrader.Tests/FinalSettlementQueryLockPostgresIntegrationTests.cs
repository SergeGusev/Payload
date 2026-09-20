using System.Diagnostics;
using System.Reflection;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;
using Xunit.Abstractions;
using static PolyCopyTrader.Tests.PaperAlgorithmOutcomePostgresIntegrationTests;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class FinalSettlementQueryLockPostgresIntegrationTests(ITestOutputHelper output)
{
    private static PostgresAppRepository NamedRepository(string name) => new(new PostgresConnectionFactory(
        new StorageOptions { ConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        { ApplicationName = name, CommandTimeout = 5 }.ConnectionString }));

    [Fact]
    public async Task RunAndGenericSettlementOverlapWithoutHoldingRunWhileWaitingForWallet()
    {
        var repository = await RepositoryAsync();
        var seed = await ActiveAsync(repository);
        var write = await FinalWriteAsync(repository, seed, false);
        var name = "final-generic-" + Guid.NewGuid().ToString("N");
        var runner = NamedRepository(name);
        using var release = new ManualResetEventSlim();
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generic = Task.Run(() => runner.PersistFinalPaperPositionsAsync(
            [new(write.Settlement!, write.Position!)], write.Evidence, stage =>
            {
                if (stage.Stage == PaperSettlementPersistenceStages.AcquirePositionLocks &&
                    stage.Status == PaperSettlementPersistenceStageStatus.Completed)
                {
                    acquired.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test barrier not released.");
                }
            }));
        Task<StrategyLostCounterUpdateResult>? final = null;
        try
        {
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            final = runner.PersistFinalPaperRunAsync(write);
            await WaitForBlockedAsync(name, 1);
            await using var probe = new NpgsqlConnection(ConnectionString); await probe.OpenAsync();
            await using var transaction = await probe.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT id FROM strategies WHERE id=@Strategy FOR UPDATE NOWAIT;
                SELECT id FROM strategy_market_paper_runs WHERE id=@Run FOR UPDATE NOWAIT;
                """, probe, transaction);
            command.Parameters.AddWithValue("Strategy", seed.Order.StrategyId);
            command.Parameters.AddWithValue("Run", seed.RunId);
            await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }
        finally { release.Set(); }
        Assert.Equal(1, await generic.WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.True((await final!.WaitAsync(TimeSpan.FromSeconds(8))).Applied);
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.Equal(1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)", seed.Order.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinkedLiveOrMarkAndRunWaitForPositionBeforeFinancialWrites(bool linkedLive)
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        var write = await FinalWriteAsync(repository, seed, false);
        var order = seed.Order;
        var live = new LiveOrder(Guid.NewGuid(), order.SignalId, LiveOrderStatus.Matched, "test", TradeSide.Buy,
            order.AssetId, order.ConditionId, order.Outcome, .5m, 12m, 6m, "FAK", order.CreatedAtUtc, order.ExpiresAtUtc,
            order.CreatedAtUtc, "matched", 12m, 0m, "", "{}", "actual fill", seed.Settled,
            StrategyId: order.StrategyId, AverageFillPrice: .5m, FilledNotionalUsd: 6m, CostBasisUsd: 6m,
            FeeUsd: .2m, PaperOrderId: order.Id, FeeAccountingStatus: "VenueReported");
        if (linkedLive) await repository.AddLiveOrderAsync(live);
        var expected = (await repository.GetPaperPositionAsync(order.CopiedTraderWallet, order.AssetId))!;
        var name = "final-position-" + Guid.NewGuid().ToString("N"); var runner = NamedRepository(name);
        await using var gate = new NpgsqlConnection(ConnectionString); await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT id FROM paper_positions WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset FOR UPDATE", gate, transaction))
        {
            command.Parameters.AddWithValue("Wallet", order.CopiedTraderWallet);
            command.Parameters.AddWithValue("Asset", order.AssetId); await command.ExecuteNonQueryAsync();
        }
        Task first = linkedLive
            ? runner.ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(live.Id, live.StrategyId, 0, -6, -6.2m,
                write.Evidence.WinningAssetId, write.Evidence.WinningOutcome, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, live.RowVersion, write.Evidence)
            : runner.TryUpdatePaperPositionMarkAsync(expected, 7, 1, .8m, DateTimeOffset.UtcNow);
        Task<StrategyLostCounterUpdateResult>? final = null;
        try
        {
            await WaitForBlockedAsync(name, 1);
            final = runner.PersistFinalPaperRunAsync(write);
            await WaitForBlockedAsync(name, 2);
            if (linkedLive)
            {
                await using var probe = new NpgsqlConnection(ConnectionString); await probe.OpenAsync();
                await using var checkTransaction = await probe.BeginTransactionAsync();
                await using var command = new NpgsqlCommand("SELECT id FROM live_orders WHERE id=@Id FOR UPDATE NOWAIT", probe, checkTransaction);
                command.Parameters.AddWithValue("Id", live.Id); await command.ExecuteNonQueryAsync();
                await checkTransaction.RollbackAsync();
            }
            Assert.False(first.IsCompleted); Assert.False(final.IsCompleted);
        }
        finally { await transaction.RollbackAsync(); }
        await first.WaitAsync(TimeSpan.FromSeconds(8));
        if (!linkedLive) Assert.True(await (Task<bool>)first);
        Assert.True((await final!.WaitAsync(TimeSpan.FromSeconds(8))).Applied);
        Assert.True((await repository.GetPaperOrderAsync(order.Id))!.Confirmed);
        Assert.Equal(0m, (await repository.GetPaperPositionAsync(order.CopiedTraderWallet, order.AssetId))!.SizeShares);
        Assert.Equal(1, await repository.GetEffectivePaperLostCounterAsync(order.StrategyId));
    }

    [Fact]
    public async Task BatchMarksSkipLockedPositionWhileFinalSettlementWaits()
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        var write = await FinalWriteAsync(repository, seed, false);
        var expected = (await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet, seed.Order.AssetId))!;
        var name = "final-batch-mark-" + Guid.NewGuid().ToString("N"); var runner = NamedRepository(name);
        await using var gate = new NpgsqlConnection(ConnectionString); await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT id FROM paper_positions WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset FOR UPDATE", gate, transaction))
        {
            command.Parameters.AddWithValue("Wallet", seed.Order.CopiedTraderWallet);
            command.Parameters.AddWithValue("Asset", seed.Order.AssetId); await command.ExecuteNonQueryAsync();
        }
        var final = runner.PersistFinalPaperRunAsync(write);
        try
        {
            await WaitForBlockedAsync(name, 1);
            Assert.Empty(await runner.TryUpdatePaperPositionMarksAsync(
                [new PaperPositionMarkUpdate(expected, 7, 1, DateTimeOffset.UtcNow, .8m)]));
            Assert.False(final.IsCompleted);
        }
        finally { await transaction.RollbackAsync(); }
        Assert.True((await final.WaitAsync(TimeSpan.FromSeconds(8))).Applied);
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.Equal(0m, (await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet, seed.Order.AssetId))!.SizeShares);
    }

    [Fact]
    public async Task CancelledWalletWaitLeavesEverythingRetryable()
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        await repository.RecordPaperAlgorithmOutcomeAsync(Preliminary(seed, true));
        var write = await FinalWriteAsync(repository, seed, false);
        var name = "final-cancel-" + Guid.NewGuid().ToString("N"); var runner = NamedRepository(name);
        await using var gate = new NpgsqlConnection(ConnectionString); await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@Wallet,4937427318840178337))", gate, transaction))
        { command.Parameters.AddWithValue("Wallet", seed.Order.CopiedTraderWallet); await command.ExecuteNonQueryAsync(); }
        using var cancellation = new CancellationTokenSource();
        var final = runner.PersistFinalPaperRunAsync(write, cancellation.Token);
        try
        {
            await WaitForBlockedAsync(name, 1); await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => final);
            Assert.Equal("Entered", await ScalarAsync<string>("SELECT status FROM strategy_market_paper_runs WHERE id=(SELECT id FROM strategy_market_paper_runs WHERE paper_order_id=@Id)", seed.Order.Id));
            Assert.Equal(-1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
            Assert.Equal(12m, (await repository.GetPaperPositionAsync(seed.Order.CopiedTraderWallet, seed.Order.AssetId))!.SizeShares);
            Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        }
        finally { await transaction.RollbackAsync(); }
        Assert.True((await runner.PersistFinalPaperRunAsync(write)).Applied);
        Assert.Equal(1, await repository.GetEffectivePaperLostCounterAsync(seed.Order.StrategyId));
    }
    private static async Task WaitForBlockedAsync(string application, int expected)
    {
        await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(4))
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name=@Name AND wait_event_type='Lock'", connection);
            command.Parameters.AddWithValue("Name", application);
            if ((long)(await command.ExecuteScalarAsync())! >= expected) return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {expected} blocked real PostgreSQL transactions for {application}.");
    }

    [Fact]
    public async Task ActualReadinessUsesKeysAcrossLargeUnrelatedHistory()
    {
        var repository = await RepositoryAsync(); var seed = await ActiveAsync(repository);
        var unrelated = await SeedAsync(repository, false, 0);
        await repository.UpsertPaperPositionAsync(new PaperPosition(unrelated.Order.AssetId, unrelated.Order.ConditionId,
            "Up", 12, .5m, 6, 0, DateTimeOffset.UtcNow, unrelated.Order.CopiedTraderWallet));
        var prefix = "scale-" + Guid.NewGuid().ToString("N") + "-";
        await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
        await using (var fill = new NpgsqlCommand("""
            INSERT INTO paper_positions(id,copied_trader_wallet,asset_id,condition_id,outcome,size_shares,average_price,
                estimated_value_usd,unrealized_pnl_usd,updated_at_utc)
            SELECT md5(@Prefix||'p'||n)::uuid,@Prefix,@Prefix||n,@Prefix||n,'Up',12,.5,6,0,now()
            FROM generate_series(1,100000) n;
            INSERT INTO paper_position_settlements(id,copied_trader_wallet,asset_id,condition_id,outcome,
                winning_asset_id,winning_outcome,settled_size_shares,average_price,cost_basis_usd,
                settlement_value_usd,realized_pnl_usd,won,settlement_source,settled_at_utc,created_at_utc)
            SELECT md5(@Prefix||'s'||n)::uuid,@Prefix,@Prefix||n,@Prefix||n,'Up',@Prefix||n,'Up',12,.5,6,12,6,
                true,'test',now(),now() FROM generate_series(1,100000) n;
            INSERT INTO strategy_market_paper_runs(id,strategy_id,market_id,condition_id,market_slug,market_title,
                detected_at_utc,entry_due_at_utc,status,stake_usd,created_at_utc,updated_at_utc)
            SELECT md5(@Prefix||'r'||n)::uuid,r.strategy_id,@Prefix||n,@Prefix||n,@Prefix||n,'test',now(),now(),
                'Skipped',6,now(),now() FROM strategy_market_paper_runs r CROSS JOIN generate_series(1,100000) n WHERE r.id=@Run;
            ANALYZE paper_positions; ANALYZE paper_position_settlements; ANALYZE strategy_market_paper_runs;
            """, connection) { CommandTimeout = 120 })
        {
            fill.Parameters.AddWithValue("Prefix", prefix); fill.Parameters.AddWithValue("Wallet", unrelated.Order.CopiedTraderWallet);
            fill.Parameters.AddWithValue("Asset", unrelated.Order.AssetId); fill.Parameters.AddWithValue("Run", unrelated.RunId);
            await fill.ExecuteNonQueryAsync();
        }
        async Task<string> Snapshot()
        {
            await using var command = new NpgsqlCommand("""
                SELECT jsonb_build_array(
                    (SELECT jsonb_build_array(count(*),md5(string_agg(md5(p::text),'' ORDER BY id))) FROM paper_positions p WHERE copied_trader_wallet=@Prefix),
                    (SELECT jsonb_build_array(count(*),md5(string_agg(md5(s::text),'' ORDER BY id))) FROM paper_position_settlements s WHERE copied_trader_wallet=@Prefix),
                    (SELECT jsonb_build_array(count(*),md5(string_agg(md5(r::text),'' ORDER BY id))) FROM strategy_market_paper_runs r WHERE strategy_id=@Strategy AND market_id LIKE @Pattern))::text;
                """, connection) { CommandTimeout = 30 };
            command.Parameters.AddWithValue("Prefix", prefix); command.Parameters.AddWithValue("Pattern", prefix+"%");
            command.Parameters.AddWithValue("Strategy", unrelated.Order.StrategyId);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        var before = await Snapshot(); output.WriteLine("Unrelated fixture before: " + before);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(before, "100000").Count);
        var write = await FinalWriteAsync(repository, seed, false);
        var sql = (string)typeof(PostgresAppRepository).GetField("FinalPaperReadinessSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        await using (var plan = new NpgsqlCommand("EXPLAIN (FORMAT TEXT) " + sql, connection))
        {
            plan.Parameters.AddWithValue("Id", seed.Order.Id); plan.Parameters.AddWithValue("Wallet", seed.Order.CopiedTraderWallet);
            plan.Parameters.AddWithValue("Asset", seed.Order.AssetId); plan.Parameters.AddWithValue("Condition", seed.Order.ConditionId);
            plan.Parameters.AddWithValue("Outcome", seed.Order.Outcome); plan.Parameters.AddWithValue("RelatedIds", new[] { seed.Order.Id });
            plan.Parameters.AddWithValue("Price", 0m); plan.Parameters.AddWithValue("Winner", write.Evidence.WinningAssetId);
            plan.Parameters.AddWithValue("WinningOutcome", write.Evidence.WinningOutcome);
            await using var reader = await plan.ExecuteReaderAsync(); var lines = new List<string>();
            while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
            var text = string.Join('\n', lines); output.WriteLine(text);
            Assert.DoesNotContain("Seq Scan on paper_positions", text);
            Assert.DoesNotContain("Seq Scan on paper_position_settlements", text);
            Assert.DoesNotContain("Seq Scan on strategy_market_paper_runs", text);
            Assert.Contains("ix_strategy_market_paper_runs_order", text);
        }
        var timed = NamedRepository("final-scale-" + Guid.NewGuid().ToString("N"));
        var stopwatch = Stopwatch.StartNew();
        Assert.True((await timed.PersistFinalPaperRunAsync(write)).Applied);
        stopwatch.Stop(); output.WriteLine($"Actual final transaction: {stopwatch.Elapsed.TotalMilliseconds:F2}ms; command timeout5s.");
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.Equal(before, await Snapshot());
    }
}
