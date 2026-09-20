using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperOutcomeConfirmationPostgresIntegrationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException("An isolated test PostgreSQL connection is required; this test must not silently skip.");

    private static async Task<PostgresAppRepository> RepositoryAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        Assert.Equal("127.0.0.1", builder.Host);
        Assert.Equal("pct_codex_paper_confirmation_test", builder.Database);
        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = ConnectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return new PostgresAppRepository(factory);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 4)]
    [InlineData(false, true, 4)]
    [InlineData(true, true, 0)]
    public async Task CorrectsAtomicAccounting_PreservesFillsAndFees_AndIsIdempotent(bool oldWin, bool finalWin, int sold)
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository, oldWin, sold);
        var oldFinancial = await SnapshotAsync(seed.Order.Id);
        var fills = await ScalarAsync<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY id)::text FROM paper_fills f WHERE paper_order_id=@Id", seed.Order.Id);
        var cache = new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository);
        await cache.GetStrategySettingsAsync();
        PaperOutcomeConfirmationResult result;
        var trace = new PaperOutcomeConfirmationTrace();
        using (cache.BeginPaperOutcomeUpdate()) result = await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, finalWin), diagnostics: trace);
        var diagnostic = trace.Snapshot();
        Assert.True(diagnostic.CommitStarted);
        Assert.True(diagnostic.CommitAcknowledged);
        Assert.Equal(PaperConfirmationStage.Commit, diagnostic.Stage);
        Assert.Contains(diagnostic.Stages, x => x.Stage == PaperConfirmationStage.DatabaseConnection);
        Assert.Contains(diagnostic.Stages, x => x.Stage == PaperConfirmationStage.OrderLock);
        Assert.Contains(diagnostic.Stages, x => x.Stage == PaperConfirmationStage.MarkConfirmed);
        if (oldWin != finalWin) Assert.Contains(diagnostic.Stages, x => x.Stage == PaperConfirmationStage.ReconcileLossDiff);
        Assert.True(result.Confirmed);
        Assert.Equal(oldWin != finalWin, result.Corrected);
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        var expectedPayout = sold * .7m + (finalWin ? 12m - sold : 0m);
        Assert.Equal(expectedPayout - 6m, await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id", seed.Order.Id));
        Assert.Equal(expectedPayout - 6.2m, await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id", seed.Order.Id));
        Assert.Equal(finalWin ? 12m - sold : 0m, await ScalarAsync<decimal>("SELECT settlement_value_usd FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)", seed.Order.Id));
        Assert.Equal(fills, await ScalarAsync<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY id)::text FROM paper_fills f WHERE paper_order_id=@Id", seed.Order.Id));
        Assert.Equal(seed.Settled.UtcDateTime, await ScalarAsync<DateTime>("SELECT settled_at_utc FROM strategy_market_paper_runs WHERE paper_order_id=@Id", seed.Order.Id));
        if (oldWin == finalWin) Assert.Equal(oldFinancial, await SnapshotAsync(seed.Order.Id));
        else
        {
            Assert.Equal(finalWin ? -1 : 1, (await cache.GetStrategySettingsAsync())[seed.Order.StrategyId].PaperLostCounter);
            Assert.Equal(expectedPayout - 6m, await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM date_dependent_strategy_hourly_paper_pnl WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)", seed.Order.Id));
            Assert.Equal(expectedPayout - 6m, await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM paper_copied_trader_performance WHERE copied_trader_wallet=(SELECT copied_trader_wallet FROM paper_orders WHERE id=@Id) AND category='OVERALL'", seed.Order.Id));
        }
        var correctedSnapshot = await SnapshotAsync(seed.Order.Id);
        var repeat = await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, finalWin));
        Assert.True(repeat.Confirmed);
        Assert.False(repeat.Corrected);
        Assert.Equal(correctedSnapshot, await SnapshotAsync(seed.Order.Id));
        await repository.UpdatePaperOrderAsync(seed.Order); // stale false object must not erase confirmation
        Assert.True((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
    }

    [Fact]
    public async Task OneSharedPositionPayoutIsNotRepeatedForAnotherOrder()
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository, true, 4);
        var exitId = await ScalarAsync<Guid>("SELECT id FROM paper_orders WHERE side='Sell' AND asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)",seed.Order.Id);
        var exit = (await repository.GetPaperOrderAsync(exitId))!;
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(exit, false))).Corrected);
        Assert.Equal(-3.2m,await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
        var correctedSnapshot = await SnapshotAsync(seed.Order.Id);
        var second = await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, false));
        Assert.True(second.Confirmed);
        Assert.False(second.Corrected);
        Assert.Equal(correctedSnapshot, await SnapshotAsync(seed.Order.Id));
    }

    [Fact]
    public async Task MissingWinnerTokenDoesNotRewriteMatchingFinancialValues()
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository, true, 0);
        await SqlAsync("UPDATE paper_position_settlements SET winning_asset_id=NULL, realized_pnl_usd=5.99999999 WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)", seed.Order.Id);
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, true))).Confirmed);
        Assert.Equal(5.99999999m, await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)", seed.Order.Id));
    }

    [Fact]
    public async Task ActiveCycleAndCancellationCannotPartiallyConfirm()
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository, true, 0);
        await SqlAsync("UPDATE strategy_market_paper_runs SET status='Entered' WHERE paper_order_id=@Id", seed.Order.Id);
        var before = await SnapshotAsync(seed.Order.Id);
        var result = await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, false));
        Assert.False(result.Confirmed);
        Assert.Equal(before, await SnapshotAsync(seed.Order.Id));
        await SqlAsync("UPDATE strategy_market_paper_runs SET status='Settled' WHERE paper_order_id=@Id", seed.Order.Id);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order, false), cancelled.Token));
        Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
    }

    [Fact]
    public async Task MigrationDefaultAndRetrySelectionSurviveRestart()
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository, true, 0);
        Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        await repository.DeferPaperOutcomeConfirmationAsync(seed.Order.Id, DateTimeOffset.UtcNow.AddDays(2), "waiting_final");
        var next = await repository.TryClaimPaperOutcomeConfirmationAsync(DateTimeOffset.UtcNow);
        Assert.NotEqual(seed.Order.Id, next?.Id);
        var restarted = await RepositoryAsync();
        Assert.False((await restarted.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SET enable_seqscan=off; EXPLAIN SELECT id FROM paper_orders WHERE NOT confirmed AND confirmation_next_attempt_at_utc<=now() ORDER BY confirmation_next_attempt_at_utc,created_at_utc,id LIMIT 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync()) plan.Add(reader.GetString(0));
        Assert.Contains("ix_paper_orders_unconfirmed", string.Join("\n", plan));
    }

    [Fact]
    public async Task ExistingRowsReceiveFalseWithoutChangingMoney_AndMigrationRepeatsSafely()
    {
        await RepositoryAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var schema = "confirmation_migration_" + Guid.NewGuid().ToString("N");
        await using var prepare = new NpgsqlCommand($"""
            CREATE SCHEMA {schema}; SET LOCAL search_path TO {schema};
            CREATE TABLE paper_orders(id uuid PRIMARY KEY,created_at_utc timestamptz,notional_usd numeric);
            INSERT INTO paper_orders VALUES(gen_random_uuid(),now(),6.125);
            """,connection,transaction);
        await prepare.ExecuteNonQueryAsync();
        for (var i=0;i<2;i++)
        {
            await using var migrate = new NpgsqlCommand(PostgresPaperOutcomeConfirmationSchemaMigration.Sql,connection,transaction);
            await migrate.ExecuteNonQueryAsync();
        }
        await using var verify = new NpgsqlCommand("SELECT NOT confirmed AND notional_usd=6.125 FROM paper_orders",connection,transaction);
        Assert.True((bool)(await verify.ExecuteScalarAsync())!);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task LiveShadowMatchesFinalActualFill_WithoutMutatingLiveOrBalance_AndDashboardConverges()
    {
        var repository = await RepositoryAsync();
        var seed = await SeedAsync(repository,true,0);
        var order=seed.Order;
        var live=new LiveOrder(Guid.NewGuid(),order.SignalId,LiveOrderStatus.Matched,"venue-id",TradeSide.Buy,
            order.AssetId,order.ConditionId,order.Outcome,.6m,20m,12m,"FAK",order.CreatedAtUtc,order.ExpiresAtUtc,
            order.CreatedAtUtc,"matched",12m,0m,"","{}","actual partial fill",seed.Settled,
            StrategyId:order.StrategyId,BalanceEffectApplied:true,SettlementValueUsd:0,RealizedPnlUsd:-6,
            SettledAtUtc:seed.Settled,WinningAssetId:order.AssetId+"-other",WinningOutcome:"Down",
            AverageFillPrice:.5m,FilledNotionalUsd:6m,CostBasisUsd:6m,FeeUsd:.2m,Won:false,
            SettlementSource:"gamma_resolved_metadata",PaperOrderId:order.Id,FeeAccountingStatus:"VenueReported",NetRealizedPnlUsd:-6.2m);
        await repository.AddLiveOrderAsync(live);
        var before=await ScalarAsync<string>("SELECT to_jsonb(l)::text FROM live_orders l WHERE paper_order_id=@Id",order.Id);
        var balance=await ScalarAsync<decimal>("SELECT live_available_balance FROM strategies WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",order.Id);
        var projection=new PostgresDashboardProjectionRepository(new PostgresConnectionFactory(new StorageOptions { ConnectionString=ConnectionString }));
        await projection.BootstrapAsync();
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(order,false))).Corrected);
        for(var i=0;i<100;i++)
        {
            var applied=await projection.ApplyPendingEventsAsync(500);
            if(applied.EventsRead==0) break;
        }
        Assert.Equal(before,await ScalarAsync<string>("SELECT to_jsonb(l)::text FROM live_orders l WHERE paper_order_id=@Id",order.Id));
        Assert.Equal(balance,await ScalarAsync<decimal>("SELECT live_available_balance FROM strategies WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",order.Id));
        Assert.Equal(-6.2m,await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM dashboard_strategy_performance_snapshots WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",order.Id));
        Assert.Equal(-6.2m,await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND window_hours=24",order.Id));
    }

    [Theory]
    [InlineData("LossDiffReset")]
    [InlineData("LossDiffPositive")]
    public async Task CorrectedParentEventRebuildsLossDiff_AndHonorsHistoryBoundary(string mode)
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        var child=Guid.NewGuid();
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using(var prepare=new NpgsqlCommand("""
            INSERT INTO strategies(id,code,name,created_at_utc,updated_at_utc) VALUES(@Child,@Code,@Code,now(),now());
            INSERT INTO strategy_loss_diff_states(child_strategy_id,parent_strategy_id,mode,threshold,current_value,started_at_utc,updated_at_utc)
                VALUES(@Child,@Parent,@Mode,1,0,@Start,now());
            INSERT INTO strategy_loss_diff_parent_events(child_strategy_id,parent_run_id,parent_entered_at_utc,parent_settled_at_utc,won,created_at_utc)
                SELECT @Child,id,entered_at_utc,settled_at_utc,true,now() FROM strategy_market_paper_runs WHERE paper_order_id=@Id;
            """,connection))
        {
            prepare.Parameters.AddWithValue("Child",child);prepare.Parameters.AddWithValue("Code",child.ToString());
            prepare.Parameters.AddWithValue("Parent",seed.Order.StrategyId);prepare.Parameters.AddWithValue("Mode",mode);
            prepare.Parameters.AddWithValue("Start",seed.Order.CreatedAtUtc.AddMinutes(-1).UtcDateTime);
            prepare.Parameters.AddWithValue("Id",seed.Order.Id);await prepare.ExecuteNonQueryAsync();
        }
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Corrected);
        Assert.Equal(1,await ScalarAsync<int>("SELECT current_value FROM strategy_loss_diff_states WHERE parent_strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",seed.Order.Id));
        Assert.False(await ScalarAsync<bool>("SELECT won FROM strategy_loss_diff_parent_events WHERE parent_run_id=(SELECT id FROM strategy_market_paper_runs WHERE paper_order_id=@Id)",seed.Order.Id));
        var replay=await repository.ReconcileStrategyLossDiffStatesAsync(seed.Order.StrategyId,DateTimeOffset.UtcNow);
        Assert.Equal(1,replay[child].CurrentValue);
        Assert.Equal(seed.Order.CreatedAtUtc.AddMinutes(-1).ToUnixTimeSeconds(),replay[child].StartedAtUtc.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task FailureAfterFinancialWritesRollsBackEverything_AndRetrySucceeds()
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        var before=await SnapshotAsync(seed.Order.Id);
        var suffix=Guid.NewGuid().ToString("N");
        await SqlAsync($"""
            CREATE FUNCTION confirmation_test_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN IF NEW.id='{seed.Order.Id}'::uuid AND NEW.confirmed THEN RAISE EXCEPTION 'test crash after financial correction'; END IF; RETURN NEW; END;$body$;
            CREATE TRIGGER confirmation_test_{suffix} BEFORE UPDATE ON paper_orders FOR EACH ROW EXECUTE FUNCTION confirmation_test_{suffix}();
            """,seed.Order.Id);
        try
        {
            var exception=await Assert.ThrowsAsync<PostgresException>(()=>repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false)));
            Assert.Contains("test crash",exception.MessageText);
            Assert.Equal(before,await SnapshotAsync(seed.Order.Id));
            Assert.False((await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
            Assert.Equal(-1,await ScalarAsync<int>("SELECT paper_lost_counter FROM strategies WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",seed.Order.Id));
        }
        finally { await SqlAsync($"DROP TRIGGER confirmation_test_{suffix} ON paper_orders; DROP FUNCTION confirmation_test_{suffix}();",seed.Order.Id); }
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Confirmed);
    }

    [Fact]
    public async Task FiveObservedPayoutDeltasAreRemovedWithoutChangingFillAmounts()
    {
        var repository=await RepositoryAsync();
        // The five observed ETH22 Paper-minus-Live payouts, with anonymous identities.
        var observedPayouts=new[] { 12.765958m,13.95348901m,10.34482797m,11.53846199m,13.04347905m };
        decimal removed=0;
        foreach(var payout in observedPayouts)
        {
            var seed=await SeedAsync(repository,true,0);
            await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
            await using(var data=new NpgsqlCommand("""
                UPDATE paper_orders SET size_shares=@Size,price=6/@Size WHERE id=@Id;
                UPDATE paper_fills SET size_shares=@Size,price=6/@Size WHERE paper_order_id=@Id;
                UPDATE strategy_market_paper_runs SET size_shares=@Size,entry_price=6/@Size,settlement_value_usd=@Size,
                    realized_pnl_usd=@Size-6,net_realized_pnl_usd=@Size-6.2 WHERE paper_order_id=@Id;
                UPDATE paper_position_settlements SET settled_size_shares=@Size,average_price=6/@Size,
                    settlement_value_usd=@Size,realized_pnl_usd=@Size-6,net_realized_pnl_usd=@Size-6.2
                    WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id);
                """,connection))
            {
                data.Parameters.AddWithValue("Id",seed.Order.Id);data.Parameters.AddWithValue("Size",payout);
                await data.ExecuteNonQueryAsync();
            }
            var before=await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id);
            Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Corrected);
            var after=await ScalarAsync<decimal>("SELECT net_realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id);
            removed+=before-after;
            Assert.Equal(payout,(await repository.GetPaperOrderAsync(seed.Order.Id))!.SizeShares);
        }
        Assert.Equal(61.64621602m,removed);
    }

    [Fact]
    public async Task CounterDisabledAndUnknownFeesRemainTheirExistingSemantics()
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        await SqlAsync("""
            UPDATE strategies SET paper_lost_coeff=1 WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id);
            UPDATE strategy_market_paper_runs SET fee_accounting_status='LegacyUnknown',net_realized_pnl_usd=NULL WHERE paper_order_id=@Id;
            UPDATE paper_position_settlements SET fee_accounting_status='LegacyUnknown',net_realized_pnl_usd=NULL
                WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id);
            """,seed.Order.Id);
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Confirmed);
        Assert.Equal(0,await ScalarAsync<int>("SELECT paper_lost_counter FROM strategies WHERE id=(SELECT strategy_id FROM paper_orders WHERE id=@Id)",seed.Order.Id));
        Assert.True(await ScalarAsync<bool>("SELECT net_realized_pnl_usd IS NULL FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
    }

    [Fact]
    public async Task CacheCannotKeepPreCommitCountersAfterAnUncertainOrInterruptedUpdate()
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        var cache=new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance,repository);
        Assert.Equal(-1,(await cache.GetStrategySettingsAsync())[seed.Order.StrategyId].PaperLostCounter);
        using(cache.BeginPaperOutcomeUpdate())
        {
            Assert.Equal(-1,(await cache.GetStrategySettingsAsync())[seed.Order.StrategyId].PaperLostCounter);
            await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false));
            // Foreground resumes before update scope disposal, i.e. before the old
            // post-commit-only invalidation point. It must already see persisted +1.
            Assert.Equal(1,(await cache.GetStrategySettingsAsync())[seed.Order.StrategyId].PaperLostCounter);
        }
        Assert.Equal(1,(await cache.GetStrategySettingsAsync())[seed.Order.StrategyId].PaperLostCounter);
    }

    [Fact]
    public async Task EmptyCancelledOrderGetsNoPhantomPayout()
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        var cancelled=seed.Order with { Id=Guid.NewGuid(),SignalId=Guid.NewGuid(),AssetId=seed.Order.AssetId+"-empty",
            Status=PaperOrderStatus.Cancelled,FilledAtUtc=null };
        await repository.AddPaperOrderAsync(cancelled);
        var result=await repository.ConfirmPaperOutcomeAsync(Confirmation(cancelled,false));
        Assert.True(result.Confirmed);
        Assert.False(result.Corrected);
        Assert.Equal(0L,await ScalarAsync<long>("SELECT count(*) FROM paper_fills WHERE paper_order_id=@Id",cancelled.Id));
        Assert.Equal(0L,await ScalarAsync<long>("SELECT count(*) FROM paper_position_settlements WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)",cancelled.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessorDispatchConfirmsOne_OrPersistsApiFailureWithoutBlockingOtherCandidates(bool apiFailure)
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        await SqlAsync("UPDATE paper_orders SET created_at_utc='1900-01-01',confirmation_next_attempt_at_utc='-infinity' WHERE id=@Id",seed.Order.Id);
        var gamma=new ConfirmationGamma(seed.Order,apiFailure);
        var strategies=new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance,repository);
        var processor=new PaperOutcomeConfirmationProcessor(NullLogger<PaperOutcomeConfirmationProcessor>.Instance,repository,gamma,strategies);
        var activity=new ServiceActivityState();
        using(var idle=activity.TryEnterIdle(()=>true,default)!) await processor.ProcessOneAsync(idle,idle.Token);
        Assert.Equal(1,gamma.Lookups);
        Assert.Equal(!apiFailure,(await repository.GetPaperOrderAsync(seed.Order.Id))!.Confirmed);
        if(apiFailure)
        {
            Assert.Contains("HttpRequestException",await ScalarAsync<string>("SELECT confirmation_evidence::text FROM paper_orders WHERE id=@Id",seed.Order.Id));
            Assert.Equal(6m,await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM strategy_market_paper_runs WHERE paper_order_id=@Id",seed.Order.Id));
        }
        Assert.NotEqual(seed.Order.Id,(await repository.TryClaimPaperOutcomeConfirmationAsync(DateTimeOffset.UtcNow))?.Id);
    }

    [Fact]
    public async Task HourlyRefreshWaitsBeforeReadingSource_AndCannotRestoreOldOutcome()
    {
        var repository=await RepositoryAsync();
        var seed=await SeedAsync(repository,true,0);
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using(var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('paper-outcome-hourly',0))",connection,transaction))
            await gate.ExecuteNonQueryAsync();
        var deferred=await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false));
        Assert.False(deferred.Confirmed);
        Assert.Equal("hourly_refresh_active",deferred.Reason);
        var refresh=repository.RefreshDateDependentStrategyHourlyPaperPnlAsync([seed.Order.StrategyId],DateTimeOffset.UtcNow);
        var waiting=false;
        for(var i=0;i<100;i++)
        {
            waiting=await ScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory' AND query LIKE '%paper-outcome-hourly%')",seed.Order.Id);
            if(waiting) break;
            await Task.Delay(20);
        }
        Assert.True(waiting);
        // Commit the corrected source under the exact lock used by confirmation.
        await using(var correct=new NpgsqlCommand("UPDATE strategy_market_paper_runs SET settlement_price=0,settlement_value_usd=0,realized_pnl_usd=-6,net_realized_pnl_usd=-6.2 WHERE paper_order_id=@Id",connection,transaction))
        { correct.Parameters.AddWithValue("Id",seed.Order.Id);await correct.ExecuteNonQueryAsync(); }
        await transaction.CommitAsync();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(-6m,await ScalarAsync<decimal>("SELECT realized_pnl_usd FROM date_dependent_strategy_hourly_paper_pnl WHERE strategy_id=(SELECT strategy_id FROM paper_orders WHERE id=@Id) AND settled_runs_count>0",seed.Order.Id));
        Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(seed.Order,false))).Confirmed);
    }

    [Fact]
    public async Task PositiveProgressUsesSettlementOrder_AndExcludesBeforeStartedHistory()
    {
        var repository=await RepositoryAsync();
        var first=await SeedAsync(repository,false,0);
        var second=await SeedAsync(repository,false,0);
        var excluded=await SeedAsync(repository,false,0);
        var child=StrategyIds.UpDown5mStrategyVariants.First(x=>x.Behavior==BtcUpDown5mStrategyBehavior.LossDiffPositiveProgressMirror).Id;
        var original=await ScalarAsync<string>("SELECT to_jsonb(s)::text FROM strategy_loss_diff_states s WHERE child_strategy_id=@Id",child);
        Assert.Equal(0L,await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_parent_events WHERE child_strategy_id=@Id",child));
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        try
        {
            await using(var arrange=new NpgsqlCommand("""
                UPDATE strategy_loss_diff_states SET parent_strategy_id=@Parent,started_at_utc=@Start,current_value=0 WHERE child_strategy_id=@Child;
                UPDATE strategy_market_paper_runs SET settled_at_utc=@Late WHERE paper_order_id=@First;
                UPDATE strategy_market_paper_runs SET strategy_id=@Parent,entered_at_utc=@MiddleEntry,settled_at_utc=@MiddleClose WHERE paper_order_id=@Second;
                UPDATE strategy_market_paper_runs SET strategy_id=@Parent,entered_at_utc=@OldEntry,settled_at_utc=@OldClose WHERE paper_order_id=@Excluded;
                """,connection))
            {
                arrange.Parameters.AddWithValue("Parent",first.Order.StrategyId);arrange.Parameters.AddWithValue("Child",child);
                arrange.Parameters.AddWithValue("First",first.Order.Id);arrange.Parameters.AddWithValue("Second",second.Order.Id);
                arrange.Parameters.AddWithValue("Excluded",excluded.Order.Id);
                arrange.Parameters.AddWithValue("Start",first.Order.CreatedAtUtc.AddMinutes(-1).UtcDateTime);
                arrange.Parameters.AddWithValue("Late",first.Order.CreatedAtUtc.AddMinutes(20).UtcDateTime);
                arrange.Parameters.AddWithValue("MiddleEntry",first.Order.CreatedAtUtc.AddMinutes(5).UtcDateTime);
                arrange.Parameters.AddWithValue("MiddleClose",first.Order.CreatedAtUtc.AddMinutes(10).UtcDateTime);
                arrange.Parameters.AddWithValue("OldEntry",first.Order.CreatedAtUtc.AddMinutes(-20).UtcDateTime);
                arrange.Parameters.AddWithValue("OldClose",first.Order.CreatedAtUtc.AddMinutes(-15).UtcDateTime);
                await arrange.ExecuteNonQueryAsync();
            }
            Assert.Equal(2,(await repository.ReconcileStrategyLossDiffStatesAsync(first.Order.StrategyId,DateTimeOffset.UtcNow))[child].CurrentValue);
            Assert.True((await repository.ConfirmPaperOutcomeAsync(Confirmation(first.Order,true))).Corrected);
            var replay=await repository.ReconcileStrategyLossDiffStatesAsync(first.Order.StrategyId,DateTimeOffset.UtcNow);
            Assert.Equal(0,replay[child].CurrentValue); // settlement order Loss, Win; entry order would incorrectly yield 1
            Assert.Equal(2L,await ScalarAsync<long>("SELECT count(*) FROM strategy_loss_diff_parent_events WHERE child_strategy_id=@Id",child));
        }
        finally
        {
            await using var restore=new NpgsqlCommand("""
                DELETE FROM strategy_loss_diff_parent_events WHERE child_strategy_id=@Child;
                UPDATE strategy_loss_diff_states s SET parent_strategy_id=o.parent_strategy_id,mode=o.mode,threshold=o.threshold,
                    current_value=o.current_value,started_at_utc=o.started_at_utc,last_parent_entered_at_utc=o.last_parent_entered_at_utc,
                    last_parent_run_id=o.last_parent_run_id,last_reconciled_at_utc=o.last_reconciled_at_utc,updated_at_utc=o.updated_at_utc
                FROM jsonb_populate_record(NULL::strategy_loss_diff_states,@Original::jsonb) o WHERE s.child_strategy_id=@Child;
                """,connection);
            restore.Parameters.AddWithValue("Child",child);restore.Parameters.AddWithValue("Original",original);await restore.ExecuteNonQueryAsync();
        }
    }

    private sealed class ConfirmationGamma(PaperOrder order,bool fail) : IPolymarketGammaClient
    {
        public int Lookups;
        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(string tokenId,bool closed,CancellationToken cancellationToken=default)
        {
            Lookups++;
            Assert.Equal(order.AssetId,tokenId);
            Assert.True(closed);
            if(fail) throw new HttpRequestException("test API outage");
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([PaperOutcomeConfirmationProcessorTests.Metadata() with
            { TokenId=order.AssetId,ConditionId=order.ConditionId,ClobTokenIds=[order.AssetId,order.AssetId+"-other"] }]);
        }
        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(string conditionId,string requestedTokenId,bool closed,CancellationToken cancellationToken=default)
            =>GetTokenMetadataAsync(requestedTokenId,closed,cancellationToken);
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(int limit=500,int offset=0,CancellationToken cancellationToken=default)
            =>Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        public Task<string?> GetEventCategoryAsync(string eventId,CancellationToken cancellationToken=default)=>Task.FromResult<string?>(null);
    }

    private static PaperOutcomeConfirmation Confirmation(PaperOrder order, bool won) => new(order.Id,order.ConditionId,
        order.AssetId,order.Outcome,won ? order.AssetId : order.AssetId+"-other",won ? "Up" : "Down",DateTimeOffset.UtcNow,
        """{"source":"GammaClosedMarket","umaResolutionStatus":"resolved"}""");

    private sealed record Seed(PaperOrder Order, Guid RunId, DateTimeOffset Settled);
    private static async Task<Seed> SeedAsync(PostgresAppRepository repository, bool oldWin, int sold)
    {
        var strategy = Guid.NewGuid();
        var code = "confirmation-"+strategy.ToString("N");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using (var strategyCommand = new NpgsqlCommand("INSERT INTO strategies(id,code,name,enabled,paper_lost_coeff,paper_lost_counter,created_at_utc,updated_at_utc) VALUES(@Id,@Code,@Code,false,2,@Counter,now(),now())",connection))
        {
            strategyCommand.Parameters.AddWithValue("Id",strategy);
            strategyCommand.Parameters.AddWithValue("Code",code);
            strategyCommand.Parameters.AddWithValue("Counter",oldWin ? -1 : 1);
            await strategyCommand.ExecuteNonQueryAsync();
        }
        var entered = DateTimeOffset.UtcNow.AddHours(-2);
        var settled = entered.AddMinutes(5);
        // PostgreSQL timestamp precision is microseconds.
        settled = new DateTimeOffset(settled.Ticks-settled.Ticks%10,TimeSpan.Zero);
        var order = new PaperOrder(Guid.NewGuid(),Guid.NewGuid(),"strategy:"+code,PaperOrderStatus.Filled,TradeSide.Buy,
            "token-"+code,"condition-"+code,"Up",.5m,12m,6m,entered,settled,entered,StrategyId:strategy,
            ExecutionSource:"paper_live_shadow_actual_fill");
        await repository.AddPaperOrderAsync(order);
        await repository.AddPaperFillAsync(new PaperFill(Guid.NewGuid(),order.Id,.5m,12m,entered,"actual fill",FeeUsd:.2m,FeeAccountingStatus:"Calculated"));
        if(sold>0)
        {
            var exit=order with { Id=Guid.NewGuid(),SignalId=Guid.NewGuid(),Side=TradeSide.Sell,Price=.7m,SizeShares=sold,NotionalUsd=sold*.7m,CreatedAtUtc=entered.AddMinutes(1) };
            await repository.AddPaperOrderAsync(exit);
            await repository.AddPaperFillAsync(new PaperFill(Guid.NewGuid(),exit.Id,.7m,sold,exit.CreatedAtUtc,"actual sale",RealizedPnlUsd:sold*.2m));
        }
        var runId=Guid.NewGuid();
        await using(var run=new NpgsqlCommand("""
            INSERT INTO strategy_market_paper_runs(id,strategy_id,market_id,condition_id,market_slug,market_title,category,
                detected_at_utc,entry_due_at_utc,status,selected_asset_id,selected_outcome,entry_price,stake_usd,size_shares,
                paper_order_id,entered_at_utc,settlement_price,settlement_value_usd,realized_pnl_usd,fee_usd,fee_accounting_status,
                net_realized_pnl_usd,settled_at_utc,created_at_utc,updated_at_utc)
            SELECT @RunId,strategy_id,condition_id,condition_id,condition_id,condition_id,'crypto',created_at_utc,created_at_utc,
                'Settled',asset_id,outcome,price,6,12,id,created_at_utc,@Price,@Payout,@Payout-6,.2,'Calculated',@Payout-6.2,
                @Settled,created_at_utc,@Settled FROM paper_orders WHERE id=@Id;
            INSERT INTO date_dependent_strategy_hourly_paper_pnl(strategy_id,code,name,hour_utc,settled_runs_count,won_runs_count,
                lost_runs_count,stake_usd,realized_pnl_usd,avg_pnl_usd,refreshed_at_utc)
            SELECT s.id,s.code,s.name,extract(hour FROM o.created_at_utc AT TIME ZONE 'UTC')::integer,1,1,0,6,@Payout-6,@Payout-6,now()
                FROM paper_orders o JOIN strategies s ON s.id=o.strategy_id WHERE o.id=@Id;
            """,connection))
        {
            run.Parameters.AddWithValue("Id",order.Id);run.Parameters.AddWithValue("RunId",runId);
            run.Parameters.AddWithValue("Price",oldWin?1m:0m);run.Parameters.AddWithValue("Payout",sold*.7m+(oldWin?12-sold:0));
            run.Parameters.AddWithValue("Settled",settled.UtcDateTime);await run.ExecuteNonQueryAsync();
        }
        await repository.TryAddPaperPositionSettlementAsync(new PaperPositionSettlement(Guid.NewGuid(),order.CopiedTraderWallet,
            order.AssetId,order.ConditionId,"Up",oldWin?order.AssetId:order.AssetId+"-other",oldWin?"Up":"Down","crypto",
            12-sold,.5m,(12-sold)*.5m,oldWin?12-sold:0,(oldWin?12-sold:0)-(12-sold)*.5m,oldWin,"BinanceTimedClose",settled,settled,
            FeeUsd:.2m,FeeAccountingStatus:"Calculated",NetRealizedPnlUsd:(oldWin?12-sold:0)-(12-sold)*.5m-.2m));
        return new(order,runId,settled);
    }

    private static Task<string> SnapshotAsync(Guid id)=>ScalarAsync<string>("""
        SELECT jsonb_build_object('run',(SELECT to_jsonb(r) FROM strategy_market_paper_runs r WHERE paper_order_id=@Id),
            'settlement',(SELECT to_jsonb(s) FROM paper_position_settlements s WHERE asset_id=(SELECT asset_id FROM paper_orders WHERE id=@Id)))::text
        """,id);
    private static async Task<T> ScalarAsync<T>(string sql,Guid id)
    {
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using var command=new NpgsqlCommand(sql,connection);command.Parameters.AddWithValue("Id",id);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing scalar result"));
    }
    private static async Task SqlAsync(string sql,Guid id)
    {
        await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();
        await using var command=new NpgsqlCommand(sql,connection);command.Parameters.AddWithValue("Id",id);await command.ExecuteNonQueryAsync();
    }
}
