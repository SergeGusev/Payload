using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperConfirmationProjectionTests
{
    private static PostgresConnectionFactory Factory() => new(new StorageOptions { ConnectionString = ConnectionString });
    internal static async Task DrainAsync(PostgresDashboardProjectionRepository projection, DateTimeOffset? at = null)
    {
        for (var i=0;i<1000;i++)
        {
            await projection.ApplyPaperConfirmationProjectionAsync(250,at);
            var captured=(at??DateTimeOffset.UtcNow).UtcDateTime.ToString("O");
            if (await ScalarAsync<bool>($"SELECT NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_cursor WHERE NOT completed) AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_queue) AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_members WHERE expires_at<='{captured}'::timestamptz AND (pending OR expires_at<'{captured}'::timestamptz))",Guid.Empty)) return;
        }
        throw new InvalidOperationException("Bounded fixture projection failed to drain.");
    }

    [Fact]
    public async Task RecentWindowsIncludeBothBoundaries_AndFutureRowsActivateWithoutSourceWrites()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,true));
        var projection=new PostgresDashboardProjectionRepository(Factory());
        var now=new DateTimeOffset(2026,9,20,12,0,0,TimeSpan.Zero);
        async Task<long> Closed() => await ScalarAsync<long>("SELECT COALESCE(sum(closed),0)::bigint FROM paper_confirmation_projection_totals WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND kind='R' AND hours=1",seed.Order.Id);
        await SqlAsync("UPDATE strategy_market_paper_runs SET settled_at_utc='2026-09-20T11:00:00Z' WHERE paper_order_id=@Id",seed.Order.Id);
        await DrainAsync(projection,now);Assert.Equal(1,await Closed());
        await DrainAsync(projection,now.AddTicks(10));Assert.Equal(0,await Closed());
        await SqlAsync("UPDATE strategy_market_paper_runs SET settled_at_utc='2026-09-20T12:00:01Z' WHERE paper_order_id=@Id",seed.Order.Id);
        await DrainAsync(projection,now);Assert.Equal(0,await Closed());
        await DrainAsync(projection,now.AddSeconds(1));Assert.Equal(1,await Closed());
        await DrainAsync(projection,now.AddHours(1).AddSeconds(1));Assert.Equal(1,await Closed());
        await DrainAsync(projection,now.AddHours(1).AddSeconds(1).AddTicks(10));Assert.Equal(0,await Closed());
    }

    [Fact]
    public async Task DurableTransitionsSurviveDeletionBeforeConsumption_AndReplayDoesNotDoubleCount()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        var projection=new PostgresDashboardProjectionRepository(Factory());await DrainAsync(projection);
        var snapshots=new PostgresDashboardSnapshotRepository(Factory());
        var before=(await snapshots.GetPaperConfirmationProgressAsync())!;
        var order=seed.Order with { Id=Guid.NewGuid(), SignalId=Guid.NewGuid(), Status=PaperOrderStatus.Cancelled };
        await repository.AddPaperOrderAsync(order);
        await SqlAsync("UPDATE paper_orders SET confirmed=true WHERE id=@Id; DELETE FROM paper_orders WHERE id=@Id;",order.Id);
        await DrainAsync(projection);
        var after=(await snapshots.GetPaperConfirmationProgressAsync())!;
        Assert.Equal(before.Orders,after.Orders);Assert.Equal(before.Confirmed,after.Confirmed);
        Assert.Equal(before.Arrivals+1,after.Arrivals);Assert.Equal(before.UniqueConfirmations+1,after.UniqueConfirmations);
        await SqlAsync("INSERT INTO paper_confirmation_projection_queue(kind,id,observed_confirmed) VALUES('O',@Id,true)",order.Id);
        await DrainAsync(projection);
        Assert.Equal(after.UniqueConfirmations,(await snapshots.GetPaperConfirmationProgressAsync())!.UniqueConfirmations);
    }

    [Fact]
    public async Task StoredNonRunSnapshotCombinesSettlementsAndSales_WithEntryAndExitFees_AndUnknownFallback()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,4);
        var exitId=await ScalarAsync<Guid>("SELECT id FROM paper_orders WHERE side='Sell' AND asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)",seed.Order.Id);
        var exit=(await repository.GetPaperOrderAsync(exitId))!;
        await repository.ConfirmPaperOutcomeGroupAsync([Confirmation(seed.Order,true),Confirmation(exit,true)]);
        await SqlAsync("""
            DELETE FROM strategy_market_paper_runs WHERE paper_order_id=@Id;
            UPDATE paper_fills SET fee_usd=.1,fee_accounting_status='Calculated',net_realized_pnl_usd=.6
                WHERE paper_order_id IN (SELECT id FROM paper_orders WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id) AND side='Sell');
            """,seed.Order.Id);
        var projection=new PostgresDashboardProjectionRepository(Factory());
        await projection.BootstrapAsync();await projection.ApplyPendingEventsAsync(10000);await DrainAsync(projection);
        var snapshots=new PostgresDashboardSnapshotRepository(Factory());
        async Task<StrategyPerformance> Read() => (await snapshots.GetStrategyPerformanceSnapshotAsync()).Single(x=>x.StrategyId==seed.Order.StrategyId);
        var actual=await Read();var coverage=actual.Confirmation!;
        Assert.True(coverage.Initialized);Assert.Equal(2,coverage.Closed);Assert.Equal(2,coverage.Confirmed);
        Assert.Equal(4.4m,coverage.NetRealizedPnlUsd);Assert.Equal(4.4m*100/6.4m,coverage.NetClosedRoiPct);
        Assert.Equal(actual.NetRealizedPnlUsd,coverage.NetRealizedPnlUsd);
        await SqlAsync("UPDATE paper_fills SET fee_accounting_status='LegacyUnknown',net_realized_pnl_usd=NULL WHERE paper_order_id=@Id",exitId);
        await projection.ApplyPendingEventsAsync(10000);await DrainAsync(projection);
        Assert.Null((await Read()).Confirmation!.NetRealizedPnlUsd);
        await SqlAsync("ALTER TABLE paper_confirmation_projection_totals RENAME TO confirmation_test_hidden_totals",Guid.Empty);
        try
        {
            var unavailable=await Read();Assert.False(unavailable.Confirmation!.Initialized);
            Assert.Contains("42P01",unavailable.Confirmation.Error);
            Assert.Equal(actual.StrategyId,unavailable.StrategyId);
        }
        finally { await SqlAsync("ALTER TABLE confirmation_test_hidden_totals RENAME TO paper_confirmation_projection_totals",Guid.Empty); }
    }

    [Fact]
    public async Task MultiOrderFailureAfterFinancialCorrectionRollsBackWholeGroup_AndRetrySucceeds()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        var second=seed.Order with { Id=Guid.NewGuid(),SignalId=Guid.NewGuid() };await repository.AddPaperOrderAsync(second);
        var before=await SnapshotAsync(seed.Order.Id);var suffix=Guid.NewGuid().ToString("N");
        await SqlAsync($"""
            CREATE FUNCTION confirmation_group_test_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW.id='{second.Id}'::uuid AND NEW.confirmed THEN RAISE EXCEPTION 'group crash'; END IF; RETURN NEW; END;$body$;
            CREATE TRIGGER confirmation_group_test_{suffix} BEFORE UPDATE ON paper_orders FOR EACH ROW EXECUTE FUNCTION confirmation_group_test_{suffix}();
            """,seed.Order.Id);
        var group=new[] { Confirmation(seed.Order,false),Confirmation(second,false) };
        try
        {
            await Assert.ThrowsAsync<PostgresException>(()=>repository.ConfirmPaperOutcomeGroupAsync(group));
            Assert.Equal(before,await SnapshotAsync(seed.Order.Id));
            Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
            Assert.False((await repository.GetPaperOrderAsync(second.Id))!.Confirmed);
        }
        finally { await SqlAsync($"DROP TRIGGER confirmation_group_test_{suffix} ON paper_orders; DROP FUNCTION confirmation_group_test_{suffix}();",seed.Order.Id); }
        Assert.True((await repository.ConfirmPaperOutcomeGroupAsync(group)).Confirmed);
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        Assert.True((await repository.GetPaperOrderAsync(second.Id))!.Confirmed);
        Assert.Equal(-6.2m,await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
    }

    [Fact]
    public async Task StoredSnapshotPath_ConfirmedOnlyCorrectionUnknownFeesAndRecentExpiry()
    {
        var repository = await RepositoryAsync(); var seed = await SeedAsync(repository,true,0);
        var projection = new PostgresDashboardProjectionRepository(Factory());
        var snapshots = new PostgresDashboardSnapshotRepository(Factory());
        await projection.BootstrapAsync(); await DrainAsync(projection);
        async Task<PaperConfirmationCoverage> Read() => (await snapshots.GetStrategyPerformanceSnapshotAsync())
            .Single(x=>x.StrategyId==seed.Order.StrategyId).Confirmation!;
        var before=await Read(); Assert.True(before.Initialized); Assert.Equal(1,before.Closed);
        Assert.Equal(0,before.Confirmed); Assert.Null(before.NetClosedRoiPct);
        Assert.False((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,true))).Corrected);
        // Restart reconstructs from persisted cursor/totals, including a Confirmed-only event.
        projection = new(Factory()); await DrainAsync(projection);
        var matched=await Read(); Assert.Equal(100m,matched.Percent); Assert.Equal(5.8m,matched.NetRealizedPnlUsd);
        Assert.Equal(5.8m*100/6.2m,matched.NetClosedRoiPct);
        var recent=(await snapshots.GetStrategyRecentPerformanceSnapshotAsync()).Single(x=>x.StrategyId==seed.Order.StrategyId&&x.WindowHours==6);
        Assert.Equal(5.8m,recent.Confirmation!.NetRealizedPnlUsd);
        await SqlAsync("UPDATE paper_orders SET confirmed=false WHERE id=@Id",seed.Order.Id);
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Corrected);
        await projection.ApplyPendingEventsAsync(10000); await DrainAsync(projection);
        var corrected=await Read(); Assert.Equal(-6.2m,corrected.NetRealizedPnlUsd); Assert.Equal(-100m,corrected.NetClosedRoiPct);
        var global=await snapshots.GetPaperConfirmationProgressAsync(); Assert.True(global!.Initialized); Assert.True(global.Corrected>0);
        var uniqueBefore=global.UniqueConfirmations;
        await SqlAsync("UPDATE strategy_market_paper_runs SET fee_accounting_status='LegacyUnknown',net_realized_pnl_usd=NULL WHERE paper_order_id=@Id",seed.Order.Id);
        await DrainAsync(projection); var unknown=await Read(); Assert.Equal(1,unknown.Confirmed); Assert.Null(unknown.NetRealizedPnlUsd);
        Assert.Equal(uniqueBefore,(await snapshots.GetPaperConfirmationProgressAsync())!.UniqueConfirmations);
        await DrainAsync(projection,DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal(0L,await ScalarAsync<long>("SELECT COALESCE(sum(confirmed),0)::bigint FROM paper_confirmation_projection_totals WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND hours>0",seed.Order.Id));
        Assert.Equal(1,(await Read()).Confirmed);
    }

    [Fact]
    public async Task AmbiguousAndRelinkedRunsInvalidateBothSides_AndFillChangesRefreshSettlement()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,true));
        var projection=new PostgresDashboardProjectionRepository(Factory());await DrainAsync(projection);
        async Task<long> Count(string kind) => await ScalarAsync<long>($"SELECT confirmed FROM paper_confirmation_projection_totals WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND kind='{kind}' AND hours=0",seed.Order.Id);
        Assert.Equal(1,await Count("R"));Assert.Equal(1,await Count("S"));
        await SqlAsync("""
            INSERT INTO strategy_market_paper_runs(id,strategy_id,market_id,condition_id,market_slug,market_title,category,
                detected_at_utc,entry_due_at_utc,status,paper_order_id,created_at_utc,updated_at_utc,stake_usd)
            SELECT gen_random_uuid(),strategy_id,market_id||'-ambiguous',condition_id||'-ambiguous',market_slug,market_title,category,
                detected_at_utc,entry_due_at_utc,'Observed',paper_order_id,created_at_utc,updated_at_utc,0
                FROM strategy_market_paper_runs WHERE paper_order_id=@Id;
            """,seed.Order.Id);
        await DrainAsync(projection);Assert.Equal(0,await Count("R"));
        await SqlAsync("DELETE FROM strategy_market_paper_runs WHERE paper_order_id=@Id AND status='Observed'",seed.Order.Id);
        await DrainAsync(projection);Assert.Equal(1,await Count("R"));
        await SqlAsync("DELETE FROM paper_fills WHERE paper_order_id=@Id",seed.Order.Id);
        await DrainAsync(projection);Assert.Equal(0,await Count("S"));
        await repository.AddPaperFillAsync(new(Guid.NewGuid(),seed.Order.Id,.5m,12,seed.Order.CreatedAtUtc,"fixture"));
        await DrainAsync(projection);Assert.Equal(1,await Count("S"));
    }

    [Fact]
    public async Task CachePersistsExactFinalEvidence_AndGroupDoesNotMultiplyPositionPayout()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,false,0);
        var second=seed.Order with {Id=Guid.NewGuid(),SignalId=Guid.NewGuid()};await repository.AddPaperOrderAsync(second);
        var token=PaperOutcomeConfirmationProcessorTests.Metadata() with
        {
            ConditionId=seed.Order.ConditionId,TokenId=seed.Order.AssetId,
            ClobTokenIds=[seed.Order.AssetId,seed.Order.AssetId+"-other"],WinningOutcome="Up",
            RawJson="""{"umaResolutionStatus":"resolved","outcomePrices":"[\"1\",\"0\"]"}"""
        };
        var evidence=new PaperConfirmationMarketEvidence(seed.Order.ConditionId,[token],DateTimeOffset.UtcNow);
        await repository.SavePaperConfirmationMarketAsync(evidence);
        var restarted=await RepositoryAsync();var cached=await restarted.GetPaperConfirmationMarketAsync(seed.Order.ConditionId);
        Assert.NotNull(cached);
        Assert.Equal(seed.Order.AssetId,PolyCopyTrader.Service.PaperTrading.PaperOutcomeConfirmationProcessor.Resolve(second,cached.Tokens,DateTimeOffset.UtcNow)!.WinningAssetId);
        var immutable=await restarted.SavePaperConfirmationMarketAsync(evidence with {Tokens=[]});
        Assert.Single(immutable.Tokens);
        var result=await restarted.ConfirmPaperOutcomeGroupAsync([Confirmation(seed.Order,true),Confirmation(second,true)]);
        Assert.True(result.Confirmed);Assert.True(result.Corrected);
        Assert.Equal(12m,await ScalarAsync<decimal>("SELECT settlement_value_usd FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)",seed.Order.Id));
        Assert.True((await restarted.GetPaperOrderAsync(second.Id))!.Confirmed);
        Assert.False((await restarted.ConfirmPaperOutcomeGroupAsync([Confirmation(seed.Order,true),Confirmation(second,true)])).Corrected);
    }

    [Fact]
    public async Task ProjectionQueueConsumerDoesNotBlockProducer_AndReplayReadsLatestState()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var locked=new NpgsqlCommand("SELECT sequence_id FROM paper_confirmation_projection_queue WHERE kind='O' AND id=@Id ORDER BY sequence_id LIMIT 1 FOR UPDATE",connection,transaction))
        { locked.Parameters.AddWithValue("Id",seed.Order.Id); Assert.NotNull(await locked.ExecuteScalarAsync()); }
        await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,true)).WaitAsync(TimeSpan.FromSeconds(2));
        var projection=new PostgresDashboardProjectionRepository(Factory());
        await projection.ApplyPaperConfirmationProjectionAsync();
        await transaction.CommitAsync();await DrainAsync(projection);
        Assert.Equal(1L,await ScalarAsync<long>("SELECT confirmed FROM paper_confirmation_projection_totals WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND kind='R' AND hours=0",seed.Order.Id));
    }

    [Fact]
    public async Task DueOrderPreventsOldUnresolvedStarvation_AndLanesUseCreatedUtcBoundary()
    {
        var repository=await RepositoryAsync();var seed=await SeedAsync(repository,true,0);
        var now=DateTimeOffset.UtcNow;
        await SqlAsync("UPDATE paper_orders SET confirmation_next_attempt_at_utc='infinity' WHERE id<>@Id; UPDATE paper_orders SET created_at_utc=now()-interval '2 days' WHERE id=@Id",seed.Order.Id);
        var later=seed.Order with {Id=Guid.NewGuid(),SignalId=Guid.NewGuid(),CreatedAtUtc=now.AddHours(-25)};
        await repository.AddPaperOrderAsync(later);
        var first=Assert.Single(await repository.ClaimPaperConfirmationBatchAsync(PaperConfirmationLane.Archive,now,24,1));
        Assert.Equal(seed.Order.Id,first.Id);
        await repository.DeferPaperOutcomeConfirmationAsync(first.Id,now.AddMinutes(1),"unresolved");
        // Even after the oldest retry is due again, never-attempted work is first.
        Assert.Equal(later.Id,Assert.Single(await repository.ClaimPaperConfirmationBatchAsync(PaperConfirmationLane.Archive,now.AddMinutes(2),24,1)).Id);
        var recent=seed.Order with {Id=Guid.NewGuid(),SignalId=Guid.NewGuid(),CreatedAtUtc=now.AddHours(-23)};
        await repository.AddPaperOrderAsync(recent);
        Assert.Equal(recent.Id,Assert.Single(await repository.ClaimPaperConfirmationBatchAsync(PaperConfirmationLane.Recent,now,24,1)).Id);
    }
}
