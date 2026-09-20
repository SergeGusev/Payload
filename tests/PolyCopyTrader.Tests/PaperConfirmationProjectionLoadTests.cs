using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Storage;
using Xunit.Abstractions;
using static PolyCopyTrader.Tests.PaperOutcomeConfirmationPostgresIntegrationTests;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class PaperConfirmationProjectionLoadTests(ITestOutputHelper output)
{
    private static PostgresConnectionFactory Factory() => new(new StorageOptions { ConnectionString = ConnectionString });

    [Fact]
    public async Task DenseWalletHistoryAndConcurrentArrivals_DrainActualProjectionWithExactTotals()
    {
        await RepositoryAsync();
        var strategy = Guid.NewGuid();
        var code = "coverage-load-" + strategy.ToString("N");
        var watch = Stopwatch.StartNew();
        await ExecuteAsync("""
            INSERT INTO strategies(id,code,name,enabled,created_at_utc,updated_at_utc)
                VALUES(@Strategy,@Code,@Code,false,now(),now());
            INSERT INTO paper_orders(id,signal_id,strategy_id,copied_trader_wallet,status,side,asset_id,condition_id,
                outcome,price,size_shares,notional_usd,created_at_utc,expires_at_utc,filled_at_utc,execution_source,confirmed)
            SELECT md5(@Code||'-o-'||i)::uuid,md5(@Code||'-signal-'||i)::uuid,@Strategy,'strategy:'||@Code,
                'Filled','Buy',@Code||'-a-'||((i-1)%10000+1),@Code||'-c-'||((i-1)%10000+1),'Up',.5,1,.5,
                now()-interval '2 days',now()-interval '2 days',now()-interval '2 days',
                CASE WHEN i%3=0 THEN 'paper_live_shadow_actual_fill' ELSE 'coverage_test_paper' END,i%2=0
            FROM generate_series(1,100000) i;
            INSERT INTO paper_fills(id,paper_order_id,price,size_shares,filled_at_utc,evidence,fee_usd,fee_accounting_status)
            SELECT md5(@Code||'-f-'||i)::uuid,md5(@Code||'-o-'||i)::uuid,.5,1,now()-interval '2 days','synthetic coverage test',.1,'Calculated'
            FROM generate_series(1,1000) i;
            INSERT INTO paper_position_settlements(id,copied_trader_wallet,asset_id,condition_id,outcome,winning_asset_id,
                winning_outcome,settled_size_shares,average_price,cost_basis_usd,settlement_value_usd,realized_pnl_usd,
                fee_usd,fee_accounting_status,net_realized_pnl_usd,won,settlement_source,settled_at_utc,created_at_utc)
            SELECT md5(@Code||'-s-'||i)::uuid,'strategy:'||@Code,@Code||'-a-'||i,@Code||'-c-'||i,'Up',@Code||'-a-'||i,
                'Up',1,.5,.5,1,.5,.1,'Calculated',.4,true,'coverage_test',now()-interval '2 days',now()-interval '2 days'
            FROM generate_series(1,1000) i;
            ANALYZE paper_orders; ANALYZE paper_fills; ANALYZE paper_position_settlements;
            UPDATE paper_confirmation_projection_cursor SET cursor_id='00000000-0000-0000-0000-000000000000',completed=false;
            UPDATE paper_confirmation_projection_state SET initialized=false;
            """, strategy, code);
        output.WriteLine($"Fixture: 100000 orders / 10000 assets / 1000 settlements / 1000 fills, one wallet; seedMs={watch.ElapsedMilliseconds}");
        Assert.True(await ScalarAsync<bool>(PostgresPaperConfirmationCoverageIndexMigration.CompletionCheckSql, Guid.Empty));
        await new PostgresSchemaInitializer(Factory()).InitializeAsync();

        // These are the two exact all-source order predicates from the S branch of migration0014.
        var plan = await TextAsync("""
            EXPLAIN (FORMAT JSON)
            SELECT EXISTS(SELECT 1 FROM paper_orders o WHERE o.copied_trader_wallet=s.copied_trader_wallet
                    AND o.asset_id=s.asset_id AND EXISTS(SELECT 1 FROM paper_fills f WHERE f.paper_order_id=o.id)),
                NOT EXISTS(SELECT 1 FROM paper_orders o WHERE o.copied_trader_wallet=s.copied_trader_wallet
                    AND o.asset_id=s.asset_id AND (NOT o.confirmed OR o.condition_id<>s.condition_id OR o.outcome<>s.outcome))
            FROM paper_position_settlements s WHERE s.id=md5(@Code||'-s-2')::uuid;
            """, strategy, code);
        using (var doc = JsonDocument.Parse(plan))
        {
            var lookups = Nodes(doc.RootElement).Where(e => e.TryGetProperty("Index Name", out var name)
                && name.GetString() == "ix_paper_orders_confirmation_wallet_asset").ToArray();
            Assert.Equal(2, lookups.Length);
            foreach (var node in lookups)
            {
                var condition = node.GetProperty("Index Cond").GetString()!;
                Assert.Contains("copied_trader_wallet", condition);
                Assert.Contains("asset_id", condition);
            }
        }
        output.WriteLine("Both settlement subplans use wallet AND asset Index Cond; migration valid/ready, repeated initializer passes.");
        var projection = new PostgresDashboardProjectionRepository(Factory());
        var policy = new PaperConfirmationProjectionBatchPolicy();
        var durations = new List<double>();
        var processed = 0;
        var errors = 0;
        async Task ConsumeAsync()
        {
            if (!policy.CanRun(DateTimeOffset.UtcNow)) { await Task.Delay(50); return; }
            var portion = Stopwatch.StartNew();
            try
            {
                var n = await projection.ApplyPaperConfirmationProjectionAsync(policy.Limit);
                processed += n;
                policy.Succeeded(n, portion.Elapsed);
                durations.Add(portion.Elapsed.TotalMilliseconds);
            }
            catch (PostgresException e) when (e.SqlState is "57014" or "55P03")
            {
                errors++;
                policy.Failed(DateTimeOffset.UtcNow, e.SqlState);
                await projection.MarkPaperConfirmationProjectionUnknownAsync(CancellationToken.None);
                output.WriteLine($"Deferred {e.SqlState}; limit={policy.Limit}; elapsedMs={portion.ElapsedMilliseconds}");
            }
        }
        async Task DrainAsync(string phase)
        {
            var drain = Stopwatch.StartNew();
            while (drain.Elapsed < TimeSpan.FromMinutes(10))
            {
                await ConsumeAsync();
                if (await ScalarAsync<bool>("SELECT initialized AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_queue) AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_cursor WHERE NOT completed) FROM paper_confirmation_projection_state", Guid.Empty))
                {
                    output.WriteLine($"{phase}: drainMs={drain.ElapsedMilliseconds}, processedCumulative={processed}, errors={errors}");
                    return;
                }
            }
            Assert.Fail($"{phase}: actual coverage consumer failed to initialize/drain within 10 minutes.");
        }
        await DrainAsync("history");
        await AssertTotalsAsync(strategy, code, 1000, 500);
        var beforeSequence = await ScalarAsync<long>("SELECT last_value FROM paper_confirmation_projection_queue_sequence_id_seq", Guid.Empty);
        var producer = ProduceAsync(strategy, code);
        while (!producer.IsCompleted) await ConsumeAsync();
        await producer;
        await DrainAsync("arrivals");
        await AssertTotalsAsync(strategy, code, 1600, 800);
        var afterSequence = await ScalarAsync<long>("SELECT last_value FROM paper_confirmation_projection_queue_sequence_id_seq", Guid.Empty);
        durations.Sort();
        output.WriteLine($"Arrivals: 600 orders, 600 fills, 600 settlements / 60 seconds; queuedEvents={afterSequence-beforeSequence}; consumerCalls={durations.Count}, processed={processed}, errors={errors}, p95Ms={durations[(int)((durations.Count-1)*.95)]:F3}, maxMs={durations[^1]:F3}; raw and projected totals match.");
    }

    private static async Task ProduceAsync(Guid strategy, string code)
    {
        var clock = Stopwatch.StartNew();
        for (var i = 10001; i <= 10600; i++)
        {
            var remaining = TimeSpan.FromMilliseconds((i-10001)*100) - clock.Elapsed;
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            await ExecuteAsync("""
                BEGIN;
                INSERT INTO paper_orders(id,signal_id,strategy_id,copied_trader_wallet,status,side,asset_id,condition_id,
                    outcome,price,size_shares,notional_usd,created_at_utc,expires_at_utc,filled_at_utc,execution_source,confirmed)
                VALUES(md5(@Code||'-new-o-'||@Number)::uuid,gen_random_uuid(),@Strategy,'strategy:'||@Code,
                    'Filled','Buy',@Code||'-a-'||@Number,@Code||'-c-'||@Number,'Up',.5,1,.5,now(),now(),now(),'coverage_test_paper',@Number%2=0);
                INSERT INTO paper_fills(id,paper_order_id,price,size_shares,filled_at_utc,evidence,fee_usd,fee_accounting_status)
                VALUES(gen_random_uuid(),md5(@Code||'-new-o-'||@Number)::uuid,.5,1,now(),'synthetic arrivals',.1,'Calculated');
                INSERT INTO paper_position_settlements(id,copied_trader_wallet,asset_id,condition_id,outcome,winning_asset_id,
                    winning_outcome,settled_size_shares,average_price,cost_basis_usd,settlement_value_usd,realized_pnl_usd,
                    fee_usd,fee_accounting_status,net_realized_pnl_usd,won,settlement_source,settled_at_utc,created_at_utc)
                VALUES(gen_random_uuid(),'strategy:'||@Code,@Code||'-a-'||@Number,@Code||'-c-'||@Number,'Up',@Code||'-a-'||@Number,
                    'Up',1,.5,.5,1,.5,.1,'Calculated',.4,true,'coverage_test',now(),now());
                COMMIT;
                """, strategy, code, i);
        }
        var finalDelay = TimeSpan.FromSeconds(60) - clock.Elapsed;
        if (finalDelay > TimeSpan.Zero) await Task.Delay(finalDelay);
    }

    private static async Task AssertTotalsAsync(Guid strategy, string code, int total, int confirmed)
    {
        // Independently aggregate eligible assets first, then join settlements (no refresh function or member sums).
        var raw = await TextAsync("""
            WITH eligible AS (
                SELECT o.asset_id FROM paper_orders o WHERE o.copied_trader_wallet='strategy:'||@Code
                GROUP BY o.asset_id HAVING bool_and(o.confirmed) AND count(DISTINCT o.condition_id)=1
                    AND bool_and(o.outcome='Up') AND bool_or(EXISTS(SELECT 1 FROM paper_fills f WHERE f.paper_order_id=o.id))
            ) SELECT jsonb_build_array(count(*),count(e.asset_id),COALESCE(sum(s.net_realized_pnl_usd) FILTER(WHERE e.asset_id IS NOT NULL),0),
                COALESCE(sum(s.cost_basis_usd+s.fee_usd) FILTER(WHERE e.asset_id IS NOT NULL),0))::text
            FROM paper_position_settlements s LEFT JOIN eligible e USING(asset_id) WHERE s.copied_trader_wallet='strategy:'||@Code;
            """, strategy, code);
        var projected = await TextAsync("SELECT jsonb_build_array(closed,confirmed,net,denominator)::text FROM paper_confirmation_projection_totals WHERE strategy_id=@Strategy AND kind='S' AND hours=0", strategy, code);
        using var a = JsonDocument.Parse(raw);
        using var b = JsonDocument.Parse(projected);
        var rawValues = a.RootElement.EnumerateArray().Select(x=>x.GetDecimal()).ToArray();
        Assert.Equal(new decimal[] {total, confirmed, confirmed*.4m, confirmed*.6m}, rawValues);
        Assert.Equal(rawValues, b.RootElement.EnumerateArray().Select(x=>x.GetDecimal()).ToArray());
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            yield return value;
            foreach (var property in value.EnumerateObject()) foreach (var child in Nodes(property.Value)) yield return child;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) foreach (var child in Nodes(item)) yield return child;
    }

    private static async Task ExecuteAsync(string sql, Guid strategy, string code, int number = 0)
    {
        await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command = Command(sql, connection, strategy, code, number);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<string> TextAsync(string sql, Guid strategy, string code)
    {
        await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command = Command(sql, connection, strategy, code, 0);
        return (string)(await command.ExecuteScalarAsync())!;
    }
    private static NpgsqlCommand Command(string sql, NpgsqlConnection connection, Guid strategy, string code, int number)
    {
        var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
        command.Parameters.AddWithValue("Strategy", strategy); command.Parameters.AddWithValue("Code", code);
        command.Parameters.AddWithValue("Number", number); return command;
    }
}
