using System.Net;
using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;
using Xunit.Abstractions;
using Cmd = PolyCopyTrader.Service.Startup.EthLossDiffPositiveProgressHistoryBackfillCommand;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class EthLossDiffPositiveProgressHistoryBackfillPostgresTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NativeSixChain_AtomicRollbackExactResumeProvenanceAndCollisionGuards()
    {
        var factory = await GuardedFactoryAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var sources = new[] { EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8101, 10, 11, false),
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8102, 20, 21, false),
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8103, 30, 31, true, 11.3m) };
        var allIds = new List<Guid>();
        foreach (var source in sources)
        {
            foreach (var role in new[] { "signal", "order", "fill", "run", "position", "settlement" })
                allIds.Add(role == "run" ? source.RunId : Cmd.DeterministicId(source.ParentId, source.RunId, role));
            foreach (var child in Cmd.Children)
                foreach (var role in new[] { "signal", "order", "fill", "run", "position", "settlement" })
                    allIds.Add(Cmd.DeterministicId(child.Id, source.RunId, role));
        }
        try
        {
            foreach (var source in sources) await InsertParentAsync(connection, source);
            var actual = await Cmd.ReadSourcesAsync(connection, null, CancellationToken.None);
            Assert.Equal(3, actual.Count);
            Assert.All(actual, s => Assert.True(s.ChainConsistent));
            var plan = Cmd.BuildPlan(actual);
            Assert.Equal(32, plan.Entries.Count);
            var reconstructed = DateTimeOffset.Parse("2026-09-03T10:00:00.123456Z");
            var chains = plan.Entries.Select(e => Cmd.CreateChain(e, plan, reconstructed)).ToArray();
            var batch = chains.Take(16).ToArray();
            Assert.Equal(0, await Cmd.CheckChainsAsync(connection, null, batch, CancellationToken.None));
            var protectedBefore = await StateDigestAsync(connection);
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await Cmd.InsertChainsAsync(connection, transaction, batch, CancellationToken.None);
                Assert.Equal(16, await Cmd.CheckChainsAsync(connection, transaction, batch, CancellationToken.None));
                Assert.Equal(96L, await CountRowsAsync(connection, transaction, batch));
                await transaction.RollbackAsync();
            }
            Assert.Equal(0, await Cmd.CheckChainsAsync(connection, null, batch, CancellationToken.None));
            Assert.Equal(protectedBefore, await StateDigestAsync(connection));
            Assert.Equal(0L, await ScalarAsync<long>(connection,
                "SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await Cmd.InsertChainsAsync(connection, transaction, batch, CancellationToken.None);
                Assert.Equal(16, await Cmd.CheckChainsAsync(connection, transaction, batch, CancellationToken.None));
                await transaction.CommitAsync();
            }
            var beforeRetry = await CountRowsAsync(connection, null, batch);
            var recreated = plan.Entries.Take(16).Select(e => Cmd.CreateChain(e, plan, reconstructed)).ToArray();
            Assert.Equal(16, await Cmd.CheckChainsAsync(connection, null, recreated, CancellationToken.None));
            Assert.Equal(beforeRetry, await CountRowsAsync(connection, null, batch));
            Assert.Equal(protectedBefore, await StateDigestAsync(connection));
            Assert.True(await ScalarAsync<bool>(connection, """
SELECT EXISTS(SELECT 1 FROM dashboard_projection_events WHERE strategy_id='b7c50005-0000-4000-8236-000000000001');
"""));
            Assert.True(await ScalarAsync<bool>(connection, """
SELECT EXISTS(SELECT 1 FROM paper_copied_trader_performance_refresh_queue
 WHERE copied_trader_wallet='strategy:eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_1');
"""));

            // A changed expected fee cannot be passed off as an equivalent prior batch.
            recreated[0].Run["fee_usd"] = 999m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.CheckChainsAsync(connection, null, recreated, CancellationToken.None));

            var other = chains.Skip(16).ToArray();
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                // Different UUID on the same child/market must not be overwritten.
                var collision = other[0] with { Run = other[0].Run.DeepClone().AsObject() };
                collision.Run["id"] = Guid.NewGuid();
                await Cmd.InsertChainsAsync(connection, transaction, [collision], CancellationToken.None);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.CheckChainsAsync(connection, transaction, [other[0]], CancellationToken.None));
                await transaction.RollbackAsync();
            }
            Assert.Equal(0, await Cmd.CheckChainsAsync(connection, null, other, CancellationToken.None));

            foreach (var v2 in new[] { false, true })
            foreach (var sameConditionOnly in new[] {false,true})
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var archive = new NpgsqlCommand(v2 ? """
WITH m AS (INSERT INTO strategy_skip_archive_market_identities(market_id) VALUES(@Market) RETURNING market_identity_id),
 v AS (INSERT INTO strategy_skip_archive_market_metadata_versions(market_identity_id,condition_id,market_slug,market_title)
 SELECT market_identity_id,@Condition,'fixture','fixture' FROM m RETURNING metadata_version_id,market_identity_id),
 reason AS (INSERT INTO strategy_skip_archive_reasons(skip_reason) VALUES('progress34-fixture') RETURNING skip_reason_id)
INSERT INTO strategy_market_paper_skip_tombstones_v2(strategy_id,market_identity_id,metadata_version_id,archived_run_id,
 detected_at_utc,entry_due_at_utc,stake_usd,skip_reason_id,run_updated_at_utc)
SELECT @Child,v.market_identity_id,v.metadata_version_id,@Run,now(),now(),1,reason.skip_reason_id,now() FROM v,reason;
""" : """
INSERT INTO strategy_market_paper_skip_tombstones(strategy_id,market_id,condition_id,archived_run_id,archived_at_utc,archive_format_version,
 market_slug,market_title,detected_at_utc,entry_due_at_utc,stake_usd,skip_reason,run_created_at_utc,run_updated_at_utc,rollup_bucket_start_utc)
VALUES(@Child,@Market,@Condition,@Run,now(),1,'fixture','fixture',now(),now(),1,'fixture',now(),now(),date_trunc('day',now() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC');
""", connection, transaction);
                archive.Parameters.AddWithValue("Child", other[0].ChildId);
                archive.Parameters.AddWithValue("Market", sameConditionOnly ? "different-market" : other[0].MarketId);
                archive.Parameters.AddWithValue("Condition",other[0].ConditionId);
                archive.Parameters.AddWithValue("Run", sameConditionOnly ? Guid.NewGuid() : other[0].Run["id"]!.GetValue<Guid>());
                await archive.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.CheckChainsAsync(connection, transaction, [other[0]], CancellationToken.None));
                await transaction.RollbackAsync();
            }

            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await Cmd.InsertChainsAsync(connection, transaction, other, CancellationToken.None);
                Assert.Equal(16, await Cmd.CheckChainsAsync(connection, transaction, other, CancellationToken.None));
                await transaction.CommitAsync();
            }
            await using var amounts = new NpgsqlCommand("""
SELECT r.stake_usd,r.size_shares,r.settlement_value_usd,r.fee_usd,r.net_realized_pnl_usd,
 f.fee_usd,ps.fee_usd,ps.net_realized_pnl_usd,p.size_shares,
 r.skip_diagnostics_json->'history_model'->>'classification',
 (r.skip_diagnostics_json->'history_model'->>'ordinary_paper_metrics_included')::boolean
FROM strategy_market_paper_runs r JOIN paper_fills f ON f.paper_order_id=r.paper_order_id
JOIN paper_orders o ON o.id=r.paper_order_id
JOIN paper_position_settlements ps ON ps.copied_trader_wallet=o.copied_trader_wallet AND ps.asset_id=o.asset_id
JOIN paper_positions p ON p.copied_trader_wallet=o.copied_trader_wallet AND p.asset_id=o.asset_id
WHERE r.id=@Id;
""", connection);
            var entry = Assert.Single(plan.Entries, e => e.Source.RunId == sources[2].RunId && e.Child.Cap == 2);
            amounts.Parameters.AddWithValue("Id", entry.Id("run"));
            await using var amountsReader = await amounts.ExecuteReaderAsync();
            Assert.True(await amountsReader.ReadAsync());
            Assert.Equal(12m, amountsReader.GetDecimal(0));
            Assert.Equal(22.6m, amountsReader.GetDecimal(1));
            Assert.Equal(22.6m, amountsReader.GetDecimal(2));
            var fee = Math.Round(22.6m * .07m * (6m / 11.3m) * (1m - 6m / 11.3m), 5, MidpointRounding.AwayFromZero);
            Assert.Equal(fee, amountsReader.GetDecimal(3));
            Assert.Equal(10.6m - fee, amountsReader.GetDecimal(4));
            Assert.Equal(fee, amountsReader.GetDecimal(5));
            Assert.Equal(fee, amountsReader.GetDecimal(6));
            Assert.Equal(10.6m - fee, amountsReader.GetDecimal(7));
            Assert.Equal(0m, amountsReader.GetDecimal(8));
            Assert.Equal("ResearchOnly", amountsReader.GetString(9));
            Assert.True(amountsReader.GetBoolean(10));
        }
        finally
        {
            await using var clean = new NpgsqlCommand("""
DELETE FROM paper_position_settlements WHERE id=ANY(@Ids);
DELETE FROM paper_positions WHERE id=ANY(@Ids);
DELETE FROM strategy_market_paper_runs WHERE id=ANY(@Ids);
DELETE FROM paper_fills WHERE id=ANY(@Ids);
DELETE FROM paper_orders WHERE id=ANY(@Ids);
DELETE FROM signals WHERE id=ANY(@Ids);
""", connection);
            clean.Parameters.AddWithValue("Ids", allIds.ToArray());
            await clean.ExecuteNonQueryAsync();
            await CleanCatalogAsync(connection);
        }
    }

    [Fact]
    public async Task Lifecycle_ReadOnlyPreviewHealthStopInterruptedResumeQueuesAndMarkerLast()
    {
        var factory = await GuardedFactoryAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var sources = new[]
        {
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8201,10,11,false),
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8202,20,21,true),
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8203,10,11,false,parent:Cmd.Up8),
            EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(8204,20,21,true,parent:Cmd.Up8)
        };
        var organicId=Guid.Parse("a0000000-0000-0000-0000-000000008205");
        var ids = sources.SelectMany(s => new[] { "signal", "order", "fill", "run", "position", "settlement" }
            .SelectMany(role => Cmd.Children.Select(c => Cmd.DeterministicId(c.Id,s.RunId,role))
                .Append(role == "run" ? s.RunId : Cmd.DeterministicId(s.ParentId,s.RunId,role)))).Append(organicId).ToArray();
        using var consumerStop = new CancellationTokenSource();
        Task? consumer = null;
        await CleanTradeRowsAsync(connection, ids);
        try
        {
            foreach (var source in sources) await InsertParentAsync(connection, source);
            await using (var setup = new NpgsqlCommand("""
UPDATE strategy_loss_diff_states SET started_at_utc=@Cutoff WHERE child_strategy_id=ANY(@Children);
UPDATE strategy_child_parent_assignments SET assigned_at_utc=@Cutoff WHERE child_strategy_id=ANY(@Children);
INSERT INTO service_heartbeats(service_name,status,started_at_utc,last_heartbeat_utc,version,mode,current_loop,last_error)
VALUES('PolyCopyTrader.Service','Running',now(),now(),@Version,'Live','isolated fixture',NULL)
ON CONFLICT(service_name) DO UPDATE SET status='Running',last_heartbeat_utc=now(),version=@Version,mode='Live',last_error=NULL;
""", connection))
            {
                setup.Parameters.AddWithValue("Cutoff", Cmd.CutoffUtc);
                setup.Parameters.AddWithValue("Children", Cmd.Children.Select(c=>c.Id).ToArray());
                setup.Parameters.AddWithValue("Version", Cmd.ServiceVersion);
                await setup.ExecuteNonQueryAsync();
            }
            var projections = new PostgresDashboardProjectionRepository(factory);
            await projections.BootstrapAsync();
            var plan = Cmd.BuildPlan(await Cmd.ReadSourcesAsync(connection, null, CancellationToken.None));
            Assert.Equal(34, plan.Entries.Count);
            var baseline = await Cmd.ReadHealthAsync(connection, null, CancellationToken.None);
            Assert.Equal("info=1.0.0+eab41015744d4d2fcc04b042d946529efeb13084; assembly=1.0.0.0; mvid=0b7cc2c4a796", Cmd.ServiceVersion);
            var protectedBefore = await StateDigestAsync(connection);
            await ScalarAsync<string>(connection,"SET default_transaction_read_only=on; SELECT current_setting('transaction_read_only');");
            var preview = new StringWriter();
            Assert.Equal(0,await Cmd.RunPlanAsync(connection,plan,baseline,false,preview,CancellationToken.None));
            Assert.Contains("PREVIEW_OK",preview.ToString());
            Assert.Equal(0L,await ScalarAsync<long>(connection,"SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id IN ('b7c50005-0000-4000-8236-000000000001','b7c50005-0000-4000-8237-000000000001');"));

            // A redeployment is not an implicit authorization to accept any build.
            foreach (var wrongVersion in new[]
            {
                "info=1.0.0+3aa0dc5c62ed84377158d822eb6c720ffbd1aca9; assembly=1.0.0.0; mvid=2b5992c622c9",
                "info=1.0.0+9aeb7447318ea244028fbc9d8b05c1c3529006af; assembly=1.0.0.0; mvid=d5191846ec8f",
                "info=1.0.0+3548a5736cba95661b0284613ac228c600d0a5b1; assembly=1.0.0.0; mvid=d42f178b96b1",
                Cmd.ServiceVersion + "-unexpected"
            })
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var changedBuild = new NpgsqlCommand("SET TRANSACTION READ WRITE; UPDATE service_heartbeats SET version=@Version WHERE service_name='PolyCopyTrader.Service';", connection, transaction);
                changedBuild.Parameters.AddWithValue("Version", wrongVersion);
                await changedBuild.ExecuteNonQueryAsync();
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.ReadHealthAsync(connection, transaction, CancellationToken.None));
                Assert.Equal("Exact service build/heartbeat/error guard failed.", failure.Message);
                await transaction.RollbackAsync();
            }
            Assert.Equal(baseline, await Cmd.ReadHealthAsync(connection, null, CancellationToken.None));
            Assert.Equal(protectedBefore, await StateDigestAsync(connection));
            await using (var untouchedHistory = new NpgsqlCommand("SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id=ANY(@Children);", connection))
            {
                untouchedHistory.Parameters.AddWithValue("Children", Cmd.Children.Select(c => c.Id).ToArray());
                Assert.Equal(0L, (long)(await untouchedHistory.ExecuteScalarAsync())!);
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.RunPlanAsync(connection,plan,
                baseline with { SchemaFingerprint="changed-schema" },true,TextWriter.Null,CancellationToken.None));
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                await using var stale = new NpgsqlCommand("SET TRANSACTION READ WRITE; UPDATE service_heartbeats SET last_heartbeat_utc=now()-interval '121 seconds' WHERE service_name='PolyCopyTrader.Service';",connection,transaction);
                await stale.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(()=>Cmd.ReadHealthAsync(connection,transaction,CancellationToken.None));
                await transaction.RollbackAsync();
            }

            foreach (var sql in new[]
            {
                "INSERT INTO strategies(id,code,name,description,enabled,created_at_utc,updated_at_utc) VALUES(gen_random_uuid(),upper('eth_up_down_5m_up_bps_4_fak_premarket_lossdiff_positive_progress_cap_1'),'alias fixture','fixture',false,now(),now());",
                "INSERT INTO strategy_market_paper_skip_tombstones(strategy_id,market_id,archived_run_id,archived_at_utc) VALUES('b7c50005-0000-4000-8236-000000000001','unrelated-legacy-fixture',gen_random_uuid(),now());"
            })
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var guardFixture = new NpgsqlCommand("SET TRANSACTION READ WRITE; "+sql,connection,transaction);
                await guardFixture.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(()=>Cmd.ReadHealthAsync(connection,transaction,CancellationToken.None));
                await transaction.RollbackAsync();
            }

            // Normal queues are deliberately not consumed yet. The command must
            // commit the two-parent partial window, wait, then preserve both on cancellation.
            using var interrupted = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var waiting = new CallbackWriter(line => { if (line?.StartsWith("WAITING_PROJECTIONS",StringComparison.Ordinal)==true) interrupted.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Cmd.RunPlanAsync(connection,plan,baseline,true,waiting,interrupted.Token));
            Assert.Contains("WAITING_PROJECTIONS",waiting.ToString());
            Assert.Contains("stage=window_queues", waiting.ToString());
            Assert.Equal(16L,await ScalarAsync<long>(connection,"SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id IN (SELECT child_strategy_id FROM strategy_loss_diff_states WHERE parent_strategy_id='b7c50005-0000-4000-8137-000000000104');"));
            Assert.Equal(18L,await ScalarAsync<long>(connection,"SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id IN (SELECT child_strategy_id FROM strategy_loss_diff_states WHERE parent_strategy_id='b7c50005-0000-4000-8137-000000000108');"));
            Assert.Equal(0L,await ScalarAsync<long>(connection,"SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));
            Assert.Equal(protectedBefore,await StateDigestAsync(connection));

            var repository = new PostgresAppRepository(factory);
            consumer = Task.Run(async () =>
            {
                while (!consumerStop.IsCancellationRequested)
                {
                    await projections.ApplyPendingEventsAsync(500,consumerStop.Token);
                    await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(250,1,1,consumerStop.Token);
                    await projections.ReconcileNextStrategyAsync(consumerStop.Token);
                    await Task.Delay(50,consumerStop.Token);
                }
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var resumed = new StringWriter();
            Assert.Equal(0,await Cmd.RunPlanAsync(connection,plan,baseline,true,resumed,deadline.Token));
            Assert.Contains("verified_existing=34",resumed.ToString());
            Assert.DoesNotContain("BATCH_COMMITTED",resumed.ToString());
            Assert.Contains("COMPLETE",resumed.ToString());
            Assert.Equal(1L,await ScalarAsync<long>(connection,"SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));
            Assert.Equal(protectedBefore,await StateDigestAsync(connection));
            var repeated = new StringWriter();
            Assert.Equal(0,await Cmd.RunPlanAsync(connection,plan,baseline,true,repeated,deadline.Token));
            Assert.Contains("IDEMPOTENT_OK",repeated.ToString());
            Assert.DoesNotContain("BATCH_COMMITTED",repeated.ToString());

            // A new ordinary post-T0 settlement belongs to neither imported batch.
            // Even repeat-marker verification must wait for its projection event.
            consumerStop.Cancel();
            try { await consumer; } catch(OperationCanceledException) { }
            consumer=null;
            await using (var transaction=await connection.BeginTransactionAsync())
            {
                await using var organic=new NpgsqlCommand("""
SET TRANSACTION READ WRITE;
INSERT INTO strategy_market_paper_runs
SELECT x.* FROM strategy_market_paper_runs r CROSS JOIN LATERAL jsonb_populate_record(NULL::strategy_market_paper_runs,
 to_jsonb(r)||jsonb_build_object('id',@Organic::uuid,'market_id','organic-fixture','paper_order_id',NULL,'signal_id',NULL,
 'created_at_utc',clock_timestamp(),'entered_at_utc',clock_timestamp(),'settled_at_utc',clock_timestamp(),
 'updated_at_utc',clock_timestamp())) x WHERE r.id=@Source;
""",connection,transaction);
                organic.Parameters.AddWithValue("Organic",organicId);
                organic.Parameters.AddWithValue("Source",plan.Entries[0].Id("run"));
                await organic.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            using var finalWait=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var organicWait=new CallbackWriter(line=>{ if(line?.StartsWith("WAITING_PROJECTIONS",StringComparison.Ordinal)==true && line.Contains("stage=final_reconciliation",StringComparison.Ordinal)) finalWait.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Cmd.RunPlanAsync(connection,plan,baseline,true,organicWait,finalWait.Token));
            Assert.Contains("stage=final_reconciliation",organicWait.ToString());
            Assert.DoesNotContain("BATCH_COMMITTED",organicWait.ToString());
            Assert.Equal(1L,await ScalarAsync<long>(connection,"SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));
        }
        finally
        {
            consumerStop.Cancel();
            if (consumer is not null) { try { await consumer; } catch(OperationCanceledException) { } }
            await using (var writable = new NpgsqlCommand("SET default_transaction_read_only=off;",connection))
                await writable.ExecuteNonQueryAsync();
            await CleanTradeRowsAsync(connection,ids);
            await using (var heartbeat = new NpgsqlCommand("DELETE FROM service_heartbeats WHERE service_name='PolyCopyTrader.Service';",connection))
                await heartbeat.ExecuteNonQueryAsync();
            await CleanCatalogAsync(connection);
        }
    }

    [Fact]
    public async Task ResilientLifecycle_64BatchesRealCadenceLocksBackpressureCancellationAndResume()
    {
        var factory = await GuardedFactoryAsync();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        var sources = new[] { Cmd.Up4, Cmd.Up8 }.SelectMany((parent, family) =>
            Enumerable.Range(0, 33).Select(i => EthLossDiffPositiveProgressHistoryBackfillCommandTests.Row(
                9000 + family * 100 + i, 10 + i * 10, 11 + i * 10, i % 4 == 3, parent: parent))).ToArray();
        var ids = sources.SelectMany(s => new[] { "signal", "order", "fill", "run", "position", "settlement" }
            .SelectMany(role => Cmd.Children.Select(c => Cmd.DeterministicId(c.Id, s.RunId, role))
                .Append(role == "run" ? s.RunId : Cmd.DeterministicId(s.ParentId, s.RunId, role)))).ToArray();
        using var backgroundStop = new CancellationTokenSource();
        using var firstStop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        Task? heartbeat = null, dashboard = null, wallet = null, firstRun = null;
        await using var blocker = factory.CreateConnection();
        await using var waiter = factory.CreateConnection();
        await blocker.OpenAsync();
        await waiter.OpenAsync();
        NpgsqlTransaction? fillBlock = null;
        Task<int>? foreignWait = null;
        var stopwatch = Stopwatch.StartNew();
        var commits = 0;
        var waitReports = 0;
        var maximumWindow = 0;
        var retryObserved = false;
        Exception? testFailure = null;
        try
        {
            foreach (var source in sources) await InsertParentAsync(connection, source);
            await using (var setup = new NpgsqlCommand("""
UPDATE strategy_loss_diff_states SET started_at_utc=@Cutoff WHERE child_strategy_id=ANY(@Children);
UPDATE strategy_child_parent_assignments SET assigned_at_utc=@Cutoff WHERE child_strategy_id=ANY(@Children);
INSERT INTO service_heartbeats(service_name,status,started_at_utc,last_heartbeat_utc,version,mode,current_loop,last_error)
VALUES('PolyCopyTrader.Service','Running',now(),now(),@Version,'Live','isolated sustained fixture',NULL)
ON CONFLICT(service_name) DO UPDATE SET status='Running',last_heartbeat_utc=now(),version=@Version,mode='Live',last_error=NULL;
""", connection))
            {
                setup.Parameters.AddWithValue("Cutoff", Cmd.CutoffUtc);
                setup.Parameters.AddWithValue("Children", Cmd.Children.Select(c => c.Id).ToArray());
                setup.Parameters.AddWithValue("Version", Cmd.ServiceVersion);
                await setup.ExecuteNonQueryAsync();
            }
            var projections = new PostgresDashboardProjectionRepository(factory);
            var repository = new PostgresAppRepository(factory);
            await projections.BootstrapAsync();
            var plan = Cmd.BuildPlan(await Cmd.ReadSourcesAsync(connection, null, deadline.Token));
            Assert.Equal(64, plan.Entries.Select(e => e.Source.RunId).Distinct().Count());
            Assert.Equal(1088, plan.Entries.Count);
            var protectedBefore = await StateDigestAsync(connection);
            var baseline = await Cmd.ReadHealthAsync(connection, null, deadline.Token);
            await ScalarAsync<string>(connection, "SET default_transaction_read_only=on; SET statement_timeout='15s'; SET lock_timeout='2s'; SELECT current_setting('transaction_read_only');");
            var importerPid = await ScalarAsync<int>(connection, "SELECT pg_backend_pid();");
            heartbeat = Task.Run(async () =>
            {
                await using var healthConnection = factory.CreateConnection();
                await healthConnection.OpenAsync(backgroundStop.Token);
                while (!backgroundStop.IsCancellationRequested)
                {
                    await using var update = new NpgsqlCommand("UPDATE service_heartbeats SET last_heartbeat_utc=clock_timestamp() WHERE service_name='PolyCopyTrader.Service';", healthConnection);
                    await update.ExecuteNonQueryAsync(backgroundStop.Token);
                    await Task.Delay(TimeSpan.FromSeconds(10), backgroundStop.Token);
                }
            });

            // A real unrelated waiting backend, with no writes to the tested family.
            await ScalarAsync<object>(blocker, "SELECT pg_advisory_lock(9000034);");
            await using var waitCommand = new NpgsqlCommand("SELECT pg_advisory_lock(9000034);", waiter) { CommandTimeout = 120 };
            foreignWait = waitCommand.ExecuteNonQueryAsync(deadline.Token);
            for (var i = 0; i < 100 && (await Cmd.ReadLocksAsync(connection, deadline.Token)).Waiting == 0; i++)
                await Task.Delay(20, deadline.Token);
            Assert.True((await Cmd.ReadLocksAsync(connection, deadline.Token)).Waiting > 0);
            Assert.Equal(baseline, await Cmd.ReadHealthAsync(connection, null, deadline.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Cmd.WaitForReadyAsync(connection,
                baseline with { SchemaFingerprint = "fatal-even-with-locks" }, new Cmd.Progress(), TextWriter.Null, deadline.Token));
            using (var cancelLockWait = new CancellationTokenSource())
            {
                var canceledLockOutput = new CallbackWriter(line =>
                {
                    if (line?.StartsWith("WAITING_LOCKS", StringComparison.Ordinal) == true) cancelLockWait.Cancel();
                });
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cmd.WaitForReadyAsync(connection,
                    baseline, new Cmd.Progress(), canceledLockOutput, cancelLockWait.Token));
                Assert.Contains("WAITING_LOCKS", canceledLockOutput.ToString());
                Assert.True(await ScalarAsync<bool>(blocker, $"SELECT xact_start IS NULL FROM pg_stat_activity WHERE pid={importerPid};"));
            }

            var firstProgress = new Cmd.Progress();
            var atEight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releasedForeignLock = false;
            var firstOutput = new AsyncCallbackWriter(async line =>
            {
                output.WriteLine(line);
                if (line.StartsWith("WAITING_", StringComparison.Ordinal)) waitReports++;
                if (line.StartsWith("WAITING_LOCKS", StringComparison.Ordinal) && !releasedForeignLock)
                {
                    releasedForeignLock = true;
                    Assert.True(await ScalarAsync<bool>(blocker, $"SELECT xact_start IS NULL FROM pg_stat_activity WHERE pid={importerPid};"));
                    await ScalarAsync<object>(blocker, "SELECT pg_advisory_unlock(9000034);");
                    await foreignWait;
                    await ScalarAsync<object>(waiter, "SELECT pg_advisory_unlock(9000034);");
                }
                if (line.StartsWith("BATCH_COMMITTED", StringComparison.Ordinal))
                {
                    commits++;
                    maximumWindow = Math.Max(maximumWindow, firstProgress.WindowBatches);
                    Assert.InRange(firstProgress.WindowBatches, 1, 8);
                    Assert.True(commits <= 8, "A ninth batch was committed while the consumer was stopped.");
                }
                if (line.StartsWith("WAITING_PROJECTIONS", StringComparison.Ordinal) && firstProgress.WindowBatches == 8)
                    atEight.TrySetResult();
            });
            firstRun = Cmd.RunPlanAsync(connection, plan, baseline, true, firstOutput, firstStop.Token, firstProgress);
            await atEight.Task.WaitAsync(firstStop.Token);
            var delayedSince = stopwatch.Elapsed;
            // Two entire production-cadence periods with consumer deliberately stopped.
            await Task.Delay(TimeSpan.FromSeconds(61), firstStop.Token);
            Assert.False(firstRun.IsCompleted);
            Assert.Equal(8, commits);
            Assert.True(await ScalarAsync<bool>(blocker, $"SELECT xact_start IS NULL FROM pg_stat_activity WHERE pid={importerPid};"));
            Assert.Equal(128L, await ScalarAsync<long>(blocker, "SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id IN (SELECT child_strategy_id FROM strategy_loss_diff_states);"));
            Assert.Equal(0L, await ScalarAsync<long>(blocker, "SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));
            firstStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRun);
            firstRun = null;
            Assert.Equal(protectedBefore, await StateDigestAsync(connection));

            // Normal repository methods on separate connections, as in separate workers.
            dashboard = Task.Run(async () =>
            {
                while (!backgroundStop.IsCancellationRequested)
                {
                    await projections.ApplyPendingEventsAsync(500, backgroundStop.Token);
                    await projections.ReconcileNextStrategyAsync(backgroundStop.Token);
                    await Task.Delay(TimeSpan.FromSeconds(1), backgroundStop.Token);
                }
            });
            wallet = Task.Run(async () =>
            {
                using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(30));
                do
                {
                    await repository.RefreshPaperCopiedTraderPerformanceProjectionAsync(25, 5, 100, backgroundStop.Token);
                } while (await cadence.WaitForNextTickAsync(backgroundStop.Token));
            });

            var resumedProgress = new Cmd.Progress();
            var installedFillLock = false;
            var resumedOutput = new AsyncCallbackWriter(async line =>
            {
                output.WriteLine(line);
                if (line.StartsWith("WAITING_", StringComparison.Ordinal)) waitReports++;
                if (line.StartsWith("BATCH_ATTEMPT", StringComparison.Ordinal) && !installedFillLock)
                {
                    // SHARE permits preview SELECTs but blocks INSERT after signal/order writes.
                    installedFillLock = true;
                    fillBlock = await blocker.BeginTransactionAsync();
                    await using var hold = new NpgsqlCommand("LOCK TABLE paper_fills IN SHARE MODE;", blocker, fillBlock);
                    await hold.ExecuteNonQueryAsync();
                }
                if (line.StartsWith("WAITING_LOCKS", StringComparison.Ordinal) && line.Contains("sqlstate=55P03", StringComparison.Ordinal))
                {
                    retryObserved = true;
                    Assert.Equal(Cmd.WriteOutcome.RolledBack, resumedProgress.Outcome);
                    Assert.True(await ScalarAsync<bool>(waiter, $"SELECT xact_start IS NULL FROM pg_stat_activity WHERE pid={importerPid};"));
                    Assert.Equal(128L, await ScalarAsync<long>(waiter, "SELECT count(*) FROM paper_orders WHERE execution_source='eth_lossdiff_positive_progress_history_research_paper';"));
                    await fillBlock!.RollbackAsync();
                    await fillBlock.DisposeAsync();
                    fillBlock = null;
                }
                if (line.StartsWith("BATCH_COMMITTED", StringComparison.Ordinal))
                {
                    commits++;
                    maximumWindow = Math.Max(maximumWindow, resumedProgress.WindowBatches);
                    Assert.InRange(resumedProgress.WindowBatches, 1, 8);
                }
            });
            Assert.Equal(0, await Cmd.RunPlanAsync(connection, plan, baseline, true, resumedOutput, deadline.Token, resumedProgress));
            Assert.Contains("verified_existing=128", resumedOutput.ToString());
            Assert.True(retryObserved);
            Assert.Equal(64, commits);
            Assert.Equal(8, maximumWindow);
            Assert.Equal(protectedBefore, await StateDigestAsync(connection));
            Assert.Equal(1088L, await ScalarAsync<long>(connection, "SELECT count(*) FROM paper_orders WHERE execution_source='eth_lossdiff_positive_progress_history_research_paper';"));
            Assert.Equal(1088L, await ScalarAsync<long>(connection, """
SELECT count(*) FROM strategy_market_paper_runs r
JOIN paper_orders o ON o.id=r.paper_order_id JOIN paper_fills f ON f.paper_order_id=o.id
JOIN paper_position_settlements s ON s.copied_trader_wallet=o.copied_trader_wallet AND s.asset_id=o.asset_id
WHERE o.execution_source='eth_lossdiff_positive_progress_history_research_paper'
 AND r.net_realized_pnl_usd=r.settlement_value_usd-r.stake_usd-r.fee_usd
 AND f.fee_usd=r.fee_usd AND s.fee_usd=r.fee_usd AND s.net_realized_pnl_usd=r.net_realized_pnl_usd;
"""));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';"));
            var repeat = new StringWriter();
            Assert.Equal(0, await Cmd.RunPlanAsync(connection, plan, baseline, true, repeat, deadline.Token));
            Assert.Contains("IDEMPOTENT_OK", repeat.ToString());
            Assert.DoesNotContain("BATCH_COMMITTED", repeat.ToString());
            output.WriteLine($"SUSTAINED_VERIFIED parent_batches={commits}; child_chains=1088; max_window={maximumWindow}; wait_reports={waitReports}; deliberately_delayed_seconds=61; delay_started_seconds={delayedSince.TotalSeconds:F1}; elapsed_seconds={stopwatch.Elapsed.TotalSeconds:F1}; cadence_seconds=30; high_priority=25; reconciliation=5; seed=100; repeat_writes=0");
        }
        catch (Exception ex)
        {
            testFailure = ex;
            throw;
        }
        finally
        {
            firstStop.Cancel();
            backgroundStop.Cancel();
            var cleanupErrors = new List<Exception>();
            async Task CleanupAsync(Func<Task> action)
            {
                try { await action(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { cleanupErrors.Add(ex); output.WriteLine("Cleanup: " + ex); }
            }
            // Always release locks and observe every task even if the importer failed.
            if (fillBlock is not null)
            {
                await CleanupAsync(() => fillBlock.RollbackAsync());
                await CleanupAsync(() => fillBlock.DisposeAsync().AsTask());
            }
            await CleanupAsync(() => ScalarAsync<object>(blocker, "SELECT pg_advisory_unlock_all();"));
            if (foreignWait is not null) await CleanupAsync(() => foreignWait);
            await CleanupAsync(() => ScalarAsync<object>(waiter, "SELECT pg_advisory_unlock_all();"));
            if (firstRun is not null) await CleanupAsync(() => firstRun);
            foreach (var task in new[] { heartbeat, dashboard, wallet })
                if (task is not null) await CleanupAsync(() => task);
            await CleanupAsync(() => ScalarAsync<string>(connection, "SET default_transaction_read_only=off; SELECT current_setting('transaction_read_only');"));
            await CleanupAsync(() => CleanTradeRowsAsync(connection, ids));
            await CleanupAsync(async () =>
            {
                await using var clearHealth = new NpgsqlCommand("DELETE FROM service_heartbeats WHERE service_name='PolyCopyTrader.Service';", connection);
                await clearHealth.ExecuteNonQueryAsync();
            });
            await CleanupAsync(() => CleanCatalogAsync(connection));
            if (testFailure is null && cleanupErrors.Count > 0) throw new AggregateException(cleanupErrors);
        }
    }

    private sealed class AsyncCallbackWriter(Func<string, Task> callback) : StringWriter
    {
        public override async Task WriteLineAsync(string? value)
        {
            await base.WriteLineAsync(value);
            if (value is not null) await callback(value);
        }
    }

    private static async Task CleanTradeRowsAsync(NpgsqlConnection connection, Guid[] ids)
    {
        await using var clean = new NpgsqlCommand("""
DELETE FROM paper_position_settlements WHERE id=ANY(@Ids);
DELETE FROM paper_positions WHERE id=ANY(@Ids);
DELETE FROM strategy_market_paper_runs WHERE id=ANY(@Ids);
DELETE FROM paper_fills WHERE id=ANY(@Ids);
DELETE FROM paper_orders WHERE id=ANY(@Ids);
DELETE FROM signals WHERE id=ANY(@Ids);
""",connection);
        clean.Parameters.AddWithValue("Ids",ids);
        await clean.ExecuteNonQueryAsync();
    }

    private sealed class CallbackWriter(Action<string?> callback) : StringWriter
    {
        public override Task WriteLineAsync(string? value)
        {
            callback(value);
            return base.WriteLineAsync(value);
        }
    }

    private static async Task<PostgresConnectionFactory> GuardedFactoryAsync()
    {
        Assert.Null(DisposablePostgresIntegrationGuard.GetConfiguredConnectionValidationError());
        var factory = new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")!
        }, "PolyCopyTrader.Tests.ProgressHistory");
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using (var identity = new NpgsqlCommand("SELECT inet_server_addr(),current_setting('data_directory');", connection))
        await using (var reader = await identity.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(IPAddress.IsLoopback(reader.GetFieldValue<IPAddress>(0)));
            var path = Path.GetFullPath(reader.GetString(1));
            Assert.StartsWith(Path.GetFullPath(@"D:\CodexTemp\runs\"), path, StringComparison.OrdinalIgnoreCase);
            var root = Directory.GetParent(path)!.Parent!.FullName;
            using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, ".codex-ephemeral.json")));
            Assert.Equal("OpenAI Codex", marker.RootElement.GetProperty("owner").GetString());
            Assert.Equal("ephemeral-session", marker.RootElement.GetProperty("kind").GetString());
            Assert.Equal(root, Path.GetFullPath(marker.RootElement.GetProperty("runPath").GetString()!));
        }
        return factory;
    }

    private static async Task CleanCatalogAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
DELETE FROM strategy_loss_diff_parent_events WHERE child_strategy_id=ANY(@Children);
DELETE FROM strategy_loss_diff_states WHERE child_strategy_id=ANY(@Children);
DELETE FROM strategy_child_parent_assignments WHERE child_strategy_id=ANY(@Children);
DELETE FROM strategies WHERE id=ANY(@Children);
-- The removed fixture wallets have no remaining histories. Their refresh queues
-- have no strategy FK and must not leak into another test's initial drain.
DELETE FROM paper_copied_trader_performance_refresh_queue WHERE copied_trader_wallet=ANY(@Wallets);
DELETE FROM paper_copied_trader_performance_refresh_inflight WHERE copied_trader_wallet=ANY(@Wallets);
DELETE FROM schema_migration_history WHERE migration_id='0008-eth-lossdiff-positive-progress-34';
DELETE FROM schema_data_migrations WHERE migration_key='20260903_eth_progress34_native_history_v1';
""", connection);
        command.Parameters.AddWithValue("Children", Cmd.Children.Select(c => c.Id).ToArray());
        command.Parameters.AddWithValue("Wallets", Cmd.Children.Select(c => c.Wallet).ToArray());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertParentAsync(NpgsqlConnection connection, Cmd.Source row)
    {
        const string sql = """
INSERT INTO signals(id,trader_wallet,condition_id,asset_id,outcome,leader_price,score,accepted,decision,
 proposed_paper_price,proposed_size_shares,proposed_notional_usd,created_at_utc,raw_context_json)
VALUES(@Signal,@Wallet,@Condition,@Asset,'Up',@Price,1,true,'test',@Price,@Shares,@Spent,@Entered,'{}');
INSERT INTO paper_orders(id,signal_id,strategy_id,copied_trader_wallet,status,side,asset_id,condition_id,outcome,
 price,size_shares,notional_usd,created_at_utc,expires_at_utc,filled_at_utc,execution_source)
VALUES(@Order,@Signal,@Parent,@Wallet,'Filled','Buy',@Asset,@Condition,'Up',@Price,@Shares,@Spent,@Entered,@Settled,@Entered,'btc_updown5m_fak_taker_paper');
INSERT INTO paper_fills(id,paper_order_id,price,size_shares,filled_at_utc,evidence,fee_usd,fee_accounting_status,
 fee_liquidity_role,fee_rate,fee_exponent,fee_taker_only,fee_calculation_source)
VALUES(@Fill,@Order,@Price,@Shares,@Entered,'no historical book',@Fee,'Calculated','Taker',.07,1,true,@FeeSource);
INSERT INTO strategy_market_paper_runs(id,strategy_id,market_id,condition_id,market_slug,market_title,category,
 detected_at_utc,entry_due_at_utc,status,selected_asset_id,selected_outcome,entry_price,stake_usd,size_shares,
 signal_id,paper_order_id,entered_at_utc,settlement_price,settlement_value_usd,realized_pnl_usd,
 fee_usd,fee_accounting_status,fee_liquidity_role,fee_rate,fee_exponent,fee_taker_only,fee_calculation_source,
 net_realized_pnl_usd,settled_at_utc,created_at_utc,updated_at_utc)
VALUES(@Run,@Parent,@Market,@Condition,@Market,'ETH Up or Down 5m fixture','Crypto',@Entered,@Entered,
 'Settled',@Asset,'Up',@Price,@Spent,@Shares,@Signal,@Order,@Entered,@SettlementPrice,@Payout,@Gross,
 @Fee,'Calculated','Taker',.07,1,true,@FeeSource,@Net,@Settled,@Entered,@Settled);
INSERT INTO paper_positions(id,copied_trader_wallet,asset_id,condition_id,outcome,size_shares,average_price,
 estimated_value_usd,unrealized_pnl_usd,updated_at_utc,fee_accounting_status,net_unrealized_pnl_usd)
VALUES(@Position,@Wallet,@Asset,@Condition,'Up',0,0,0,0,@Settled,'Calculated',0);
INSERT INTO paper_position_settlements(id,copied_trader_wallet,asset_id,condition_id,outcome,winning_asset_id,
 winning_outcome,category,settled_size_shares,average_price,cost_basis_usd,settlement_value_usd,realized_pnl_usd,
 fee_usd,fee_accounting_status,fee_liquidity_role,fee_rate,fee_exponent,fee_taker_only,fee_calculation_source,
 net_realized_pnl_usd,won,settlement_source,settled_at_utc,created_at_utc)
VALUES(@Settlement,@Wallet,@Asset,@Condition,'Up',@WinningAsset,@WinningOutcome,'Crypto',@Shares,@Price,
 @Spent,@Payout,@Gross,@Fee,'Calculated','Taker',.07,1,true,@FeeSource,@Net,@Won,'fixture',@Settled,@Settled);
""";
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var role in new[] { "signal", "order", "fill", "position", "settlement" })
            command.Parameters.AddWithValue(role, Cmd.DeterministicId(row.ParentId, row.RunId, role));
        command.Parameters.AddWithValue("Run", row.RunId);
        command.Parameters.AddWithValue("Parent", row.ParentId);
        command.Parameters.AddWithValue("Wallet", row.ParentId == Cmd.Up4
            ? "strategy:eth_up_down_5m_up_bps_4_fak_premarket" : "strategy:eth_up_down_5m_up_bps_8_fak_premarket");
        command.Parameters.AddWithValue("Condition", row.ConditionId);
        command.Parameters.AddWithValue("Asset", row.AssetId);
        command.Parameters.AddWithValue("Market", row.MarketId);
        command.Parameters.AddWithValue("Price", row.Spent / row.Shares);
        command.Parameters.AddWithValue("Shares", row.Shares);
        command.Parameters.AddWithValue("Spent", row.Spent);
        command.Parameters.AddWithValue("Entered", row.EnteredAt);
        command.Parameters.AddWithValue("Settled", row.SettledAt);
        command.Parameters.AddWithValue("SettlementPrice", row.SettlementPrice);
        command.Parameters.AddWithValue("Payout", row.Payout);
        command.Parameters.AddWithValue("Gross", row.Gross);
        command.Parameters.AddWithValue("Net", row.Net);
        command.Parameters.AddWithValue("Fee", row.ParentFee);
        command.Parameters.AddWithValue("FeeSource", row.FeeSource);
        command.Parameters.AddWithValue("Won", row.Gross > 0);
        command.Parameters.AddWithValue("WinningAsset", row.Gross > 0 ? row.AssetId : "other-" + row.AssetId);
        command.Parameters.AddWithValue("WinningOutcome", row.Gross > 0 ? "Up" : "Down");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountRowsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, IReadOnlyList<Cmd.NativeChain> chains)
    {
        var ids = chains.SelectMany(c => new[] { c.Signal, c.Order, c.Fill, c.Run, c.Position, c.Settlement })
            .Select(j => j["id"]!.GetValue<Guid>()).ToArray();
        await using var command = new NpgsqlCommand("""
SELECT (SELECT count(*) FROM signals WHERE id=ANY(@Ids))+(SELECT count(*) FROM paper_orders WHERE id=ANY(@Ids))
 +(SELECT count(*) FROM paper_fills WHERE id=ANY(@Ids))+(SELECT count(*) FROM strategy_market_paper_runs WHERE id=ANY(@Ids))
 +(SELECT count(*) FROM paper_positions WHERE id=ANY(@Ids))+(SELECT count(*) FROM paper_position_settlements WHERE id=ANY(@Ids));
""", connection, transaction);
        command.Parameters.AddWithValue("Ids", ids);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> StateDigestAsync(NpgsqlConnection connection) => await ScalarAsync<string>(connection, """
SELECT jsonb_build_object('states',(SELECT jsonb_agg(to_jsonb(s) ORDER BY child_strategy_id) FROM strategy_loss_diff_states s),
 'strategies',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM strategies s),
 'events',(SELECT jsonb_agg(to_jsonb(e)) FROM strategy_loss_diff_parent_events e),
 'live',(SELECT count(*) FROM live_orders),'shadow',(SELECT count(*) FROM paper_live_shadow_decisions))::text;
""");
    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
