using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;
using Xunit.Abstractions;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperConfirmationBenchmarkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SyntheticTenThousandOrders_WithForegroundWrites()
    {
        // Explicit opt-in: normal focused tests do not spend two observation windows.
        if (Environment.GetEnvironmentVariable("POLYCOPYTRADER_CONFIRMATION_BENCHMARK") != "1") return;
        var repository=await RepositoryAsync();
        var prefix="pcbench-"+Guid.NewGuid().ToString("N");
        await Execute("""
            UPDATE paper_orders SET confirmation_next_attempt_at_utc='infinity';
            INSERT INTO strategies(id,code,name,enabled,paper_lost_coeff,paper_lost_counter,created_at_utc,updated_at_utc)
            SELECT md5(@Prefix||'-strategy-'||s)::uuid,@Prefix||'-strategy-'||s,'Synthetic '||@Prefix||' '||s,false,2,-100,now(),now()
                FROM generate_series(0,19) s;
            INSERT INTO paper_orders(id,signal_id,strategy_id,copied_trader_wallet,status,side,asset_id,condition_id,outcome,
                price,size_shares,notional_usd,created_at_utc,expires_at_utc,filled_at_utc)
            SELECT md5(@Prefix||'-order-'||i)::uuid,md5(@Prefix||'-signal-'||i)::uuid,
                md5(@Prefix||'-strategy-'||(i/500))::uuid,'strategy:'||@Prefix||'-strategy-'||(i/500),
                CASE WHEN i%5=0 THEN 'Filled' ELSE 'Cancelled' END,'Buy',@Prefix||'-token-'||((i/5)%100),
                @Prefix||'-condition-'||((i/5)%100),'Up',.5,12,6,
                now()-CASE WHEN ((i/5)%100)<50 THEN interval '2 hours' ELSE interval '2 days' END+make_interval(secs=>i/100.0),
                now()-interval '1 hour',CASE WHEN i%5=0 THEN now()-interval '2 hours' ELSE NULL END
            FROM generate_series(0,9999) i;
            INSERT INTO paper_fills(id,paper_order_id,price,size_shares,filled_at_utc,evidence,fee_usd,fee_accounting_status)
            SELECT md5(o.id::text||'-fill')::uuid,o.id,.5,12,o.created_at_utc,'{}',.2,'Calculated'
                FROM paper_orders o WHERE o.condition_id LIKE @Prefix||'%' AND o.status='Filled';
            INSERT INTO strategy_market_paper_runs(id,strategy_id,market_id,condition_id,market_slug,market_title,category,
                detected_at_utc,entry_due_at_utc,status,selected_asset_id,selected_outcome,entry_price,stake_usd,size_shares,
                paper_order_id,entered_at_utc,settlement_price,settlement_value_usd,realized_pnl_usd,fee_usd,fee_accounting_status,
                net_realized_pnl_usd,settled_at_utc,created_at_utc,updated_at_utc)
            SELECT md5(o.id::text||'-run')::uuid,strategy_id,condition_id,condition_id,condition_id,condition_id,'crypto',
                created_at_utc,created_at_utc,'Settled',asset_id,outcome,.5,6,12,id,created_at_utc,1,12,6,.2,'Calculated',5.8,
                created_at_utc+interval '5 minutes',created_at_utc,now() FROM paper_orders o
                WHERE o.condition_id LIKE @Prefix||'%' AND o.status='Filled';
            INSERT INTO paper_position_settlements(id,copied_trader_wallet,asset_id,condition_id,outcome,winning_asset_id,
                winning_outcome,category,settled_size_shares,average_price,cost_basis_usd,settlement_value_usd,realized_pnl_usd,
                won,settlement_source,settled_at_utc,created_at_utc,fee_usd,fee_accounting_status,net_realized_pnl_usd)
            SELECT md5(o.id::text||'-settlement')::uuid,copied_trader_wallet,asset_id,condition_id,'Up',asset_id,'Up','crypto',
                12,.5,6,12,6,true,'BinanceTimedClose',created_at_utc+interval '5 minutes',created_at_utc,.2,'Calculated',5.8
                FROM paper_orders o WHERE condition_id LIKE @Prefix||'%' AND status='Filled';
            ANALYZE paper_orders; ANALYZE paper_fills; ANALYZE strategy_market_paper_runs; ANALYZE paper_position_settlements;
            """,prefix);
        Assert.Equal(10000L,await Count("SELECT count(*) FROM paper_orders WHERE condition_id LIKE @Prefix||'%'",prefix));
        Assert.Equal(100L,await Count("SELECT count(DISTINCT condition_id) FROM paper_orders WHERE condition_id LIKE @Prefix||'%'",prefix));
        Assert.Equal(20L,await Count("SELECT count(DISTINCT strategy_id) FROM paper_orders WHERE condition_id LIKE @Prefix||'%'",prefix));
        var baseline=await Observe(false);await Reset();var current=await Observe(true);
        output.WriteLine(JsonSerializer.Serialize(new { Fixture="Synthetic only",Orders=10000,Markets=100,Strategies=20,WindowSeconds=60,Baseline=baseline,Current=current }));
        foreach(var lane in new[]{">=","<"}) await Explain($"""
            SELECT id FROM paper_orders WHERE NOT confirmed AND confirmation_next_attempt_at_utc<=now()
                AND created_at_utc {lane} now()-interval '24 hours'
            ORDER BY confirmation_next_attempt_at_utc,created_at_utc,id LIMIT 32 FOR UPDATE SKIP LOCKED
            """, "Claim "+lane);
        await Explain("""
            SELECT r.id,r.realized_pnl_usd,r.net_realized_pnl_usd FROM strategy_market_paper_runs r
            WHERE paper_order_id IN (SELECT id FROM paper_orders WHERE copied_trader_wallet='strategy:'||@Prefix||'-strategy-0'
                AND asset_id=@Prefix||'-token-0' AND condition_id=@Prefix||'-condition-0'
                AND strategy_id=md5(@Prefix||'-strategy-0')::uuid)
            ""","Matched related runs");
        await Explain("""
            SELECT s.id,s.won,s.net_realized_pnl_usd FROM paper_position_settlements s
            WHERE copied_trader_wallet='strategy:'||@Prefix||'-strategy-0' AND asset_id=@Prefix||'-token-0'
                AND condition_id=@Prefix||'-condition-0'
            ""","Matched settlement");
        Assert.True(current.Confirmed>=100);Assert.True(current.Corrected>0);Assert.True(current.Archive>0);Assert.True(current.Recent>0);
        Assert.True(current.ConfirmedPerMinute>34);Assert.True(current.ForegroundP95Ms-baseline.ForegroundP95Ms<=Math.Max(50,baseline.ForegroundP95Ms*.1));
        Assert.Equal(0,current.InvalidFinancialRows);Assert.Equal(0,current.ForegroundErrors);

        async Task Explain(string sql,string label)
        {
            await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();
            await using var cmd=new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) "+sql,c);
            cmd.Parameters.AddWithValue("Prefix",prefix);
            var json=(string)(await cmd.ExecuteScalarAsync())!;output.WriteLine(label+": "+json);
            using var plan=JsonDocument.Parse(json);
            Visit(plan.RootElement[0].GetProperty("Plan"));
            void Visit(JsonElement node)
            {
                if(node.TryGetProperty("Relation Name",out var relation) &&
                    relation.GetString() is "paper_orders" or "strategy_market_paper_runs" or "paper_position_settlements")
                    Assert.NotEqual("Seq Scan",node.GetProperty("Node Type").GetString());
                if(node.TryGetProperty("Plans",out var children)) foreach(var child in children.EnumerateArray())Visit(child);
            }
        }

        async Task Reset()
        {
            await Execute("""
                UPDATE paper_orders SET confirmed=false,confirmation_evidence=NULL,confirmation_next_attempt_at_utc='-infinity' WHERE condition_id LIKE @Prefix||'%';
                UPDATE strategy_market_paper_runs SET settlement_price=1,settlement_value_usd=12,realized_pnl_usd=6,net_realized_pnl_usd=5.8 WHERE condition_id LIKE @Prefix||'%';
                UPDATE paper_position_settlements SET winning_asset_id=asset_id,winning_outcome='Up',won=true,settlement_value_usd=12,
                    realized_pnl_usd=6,net_realized_pnl_usd=5.8,settlement_source='BinanceTimedClose' WHERE condition_id LIKE @Prefix||'%';
                UPDATE strategies SET paper_lost_counter=-100 WHERE code LIKE @Prefix||'%';
                DELETE FROM paper_confirmation_markets WHERE condition_id LIKE @Prefix||'%';
                """,prefix);
        }
        async Task<Measurement> Observe(bool modern)
        {
            var activity=new ServiceActivityState();var gamma=new SyntheticGamma(prefix);
            var processor=new PaperOutcomeConfirmationProcessor(NullLogger<PaperOutcomeConfirmationProcessor>.Instance,repository,gamma,
                new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance,repository));
            using BackgroundService worker=modern
                ? new PaperOutcomeConfirmationWorker(NullLogger<PaperOutcomeConfirmationWorker>.Instance,activity,new PaperOutcomeConfirmationWorkerTests.EntryQueue(),new PaperOutcomeConfirmationWorkerTests.MarketQueue(),processor)
                : new PaperConfirmationBaselineWorker(NullLogger<PaperConfirmationBaselineWorker>.Instance,activity,new PaperOutcomeConfirmationWorkerTests.EntryQueue(),new PaperOutcomeConfirmationWorkerTests.MarketQueue(),new BaselineProcessor(gamma,processor));
            var latencies=new ConcurrentBag<double>();var errors=0;var waits=0;using var lifetime=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var timer=Stopwatch.StartNew();
            var foreground=Task.Run(async()=>
            {
                await using var connection=new NpgsqlConnection(ConnectionString);await connection.OpenAsync();var i=0;
                while(!lifetime.IsCancellationRequested)
                {
                    var watch=Stopwatch.StartNew();
                    try
                    {
                        using var busy=activity.EnterTradingCycle("SyntheticForeground");
                        await using var cmd=new NpgsqlCommand("UPDATE strategies SET updated_at_utc=clock_timestamp() WHERE id=md5(@Code)::uuid",connection);
                        cmd.Parameters.AddWithValue("Code",prefix+"-strategy-"+(i++%20));await cmd.ExecuteNonQueryAsync(lifetime.Token);
                        latencies.Add(watch.Elapsed.TotalMilliseconds);
                        await using var locks=new NpgsqlCommand("SELECT count(*)::integer FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'",connection);
                        waits+=(int)(await locks.ExecuteScalarAsync(lifetime.Token))!;
                    }
                    catch(OperationCanceledException) when(lifetime.IsCancellationRequested){break;}
                    catch(NpgsqlException){errors++;}
                    try { await Task.Delay(20,lifetime.Token); } catch(OperationCanceledException){break;}
                }
            });
            await worker.StartAsync(default);
            try { await Task.Delay(Timeout.InfiniteTimeSpan,lifetime.Token); } catch(OperationCanceledException) { }
            await worker.StopAsync(default);await foreground;timer.Stop();
            var confirmed=await Count("SELECT count(*) FROM paper_orders WHERE condition_id LIKE @Prefix||'%' AND confirmed",prefix);
            var corrected=await Count("SELECT count(*) FROM paper_orders WHERE condition_id LIKE @Prefix||'%' AND confirmed AND confirmation_evidence->>'corrected'='true'",prefix);
            var archive=await Count("SELECT count(*) FROM paper_orders WHERE condition_id LIKE @Prefix||'%' AND confirmed AND created_at_utc<now()-interval '24 hours'",prefix);
            var invalid=await Count("""
                SELECT count(*) FROM strategy_market_paper_runs r WHERE r.condition_id LIKE @Prefix||'%'
                    AND EXISTS(SELECT 1 FROM paper_orders o WHERE o.strategy_id=r.strategy_id AND o.condition_id=r.condition_id AND o.confirmed)
                    AND (r.realized_pnl_usd<>CASE WHEN split_part(r.condition_id,'-condition-',2)::integer%2=0 THEN 6 ELSE -6 END
                        OR r.net_realized_pnl_usd<>r.realized_pnl_usd-.2);
                """,prefix);
            var sorted=latencies.Order().ToArray();Assert.NotEmpty(sorted);
            return new(confirmed,corrected,archive,confirmed-archive,confirmed*60/timer.Elapsed.TotalSeconds,
                sorted[(int)Math.Ceiling(sorted.Length*.95)-1],sorted.Length,errors,waits,gamma.Calls,invalid,timer.Elapsed.TotalSeconds);
        }
    }
    private sealed record Measurement(long Confirmed,long Corrected,long Archive,long Recent,double ConfirmedPerMinute,
        double ForegroundP95Ms,int ForegroundWrites,int ForegroundErrors,int WaitingLockSamples,int HttpCalls,long InvalidFinancialRows,double DurationSeconds);
    private static async Task Execute(string sql,string prefix)
    { await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();await using var cmd=new NpgsqlCommand(sql,c){CommandTimeout=120};cmd.Parameters.AddWithValue("Prefix",prefix);await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> Count(string sql,string prefix)
    { await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync();await using var cmd=new NpgsqlCommand(sql,c);cmd.Parameters.AddWithValue("Prefix",prefix);return (long)(await cmd.ExecuteScalarAsync())!; }
    private sealed class SyntheticGamma(string prefix) : IPolymarketGammaClient
    {
        public int Calls;
        public async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(string tokenId,bool closed,CancellationToken cancellationToken=default)
        {
            Interlocked.Increment(ref Calls);await Task.Delay(5,cancellationToken);
            var index=int.Parse(tokenId[(prefix.Length+7)..]);var win=index%2==0;
            return [PaperOutcomeConfirmationProcessorTests.Metadata() with {TokenId=tokenId,ConditionId=prefix+"-condition-"+index,
                MarketId=prefix+"-market-"+index,ClobTokenIds=[tokenId,tokenId+"-down"],WinningOutcome=win?"Up":"Down",
                RawJson=JsonSerializer.Serialize(new {umaResolutionStatus="resolved",outcomePrices=win?"[\"1\",\"0\"]":"[\"0\",\"1\"]"})}];
        }
        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(string conditionId,string requestedTokenId,bool closed,CancellationToken cancellationToken=default)=>GetTokenMetadataAsync(requestedTokenId,closed,cancellationToken);
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(int limit=500,int offset=0,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<string?> GetEventCategoryAsync(string eventId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class BaselineProcessor(SyntheticGamma gamma,PaperOutcomeConfirmationProcessor inner) : IPaperOutcomeConfirmationProcessor
    {
        public Task<PaperOrder?> ClaimAsync(CancellationToken token)=>inner.ClaimAsync(token);
        public Task<IReadOnlyList<PaperOrder>> ClaimBatchAsync(PaperConfirmationLane lane,Guid strategyId,int limit,CancellationToken token)=>throw new NotSupportedException();
        public async Task<PaperOutcomeConfirmation?> LookupAsync(PaperOrder order,PaperOutcomeConfirmationTrace trace,CancellationToken token)
            => PaperOutcomeConfirmationProcessor.Resolve(order,await gamma.GetTokenMetadataAsync(order.AssetId,true,token),DateTimeOffset.UtcNow);
        public Task<PaperOutcomeConfirmationResult> ApplyAsync(PaperOutcomeConfirmation value,PaperOutcomeConfirmationTrace trace,CancellationToken token)=>inner.ApplyAsync(value,trace,token);
        public Task<PaperOutcomeConfirmationResult> ApplyGroupAsync(IReadOnlyList<PaperOutcomeConfirmation> values,PaperOutcomeConfirmationTrace trace,CancellationToken token)=>throw new NotSupportedException();
        public Task DeferAsync(Guid id,string reason,CancellationToken token)=>inner.DeferAsync(id,reason,token);
    }
}
