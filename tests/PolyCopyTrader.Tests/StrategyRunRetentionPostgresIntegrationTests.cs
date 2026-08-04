using System.Data;
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

internal sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION is not configured.";
        }
    }
}

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class StrategyRunRetentionPostgresIntegrationTests
{
    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_BulkInsertReturnsLogicalIdsAndRetryIsIdempotent()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_skip_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc) with
        {
            SkipDiagnosticsJson = "{\"discarded\":true}"
        };
        var observed = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1)) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [skipped, observed],
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(
                new[] { skipped.Id, observed.Id }.OrderBy(id => id),
                inserted.OrderBy(id => id));
            Assert.Equal(
                new RetentionCounts(1, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Observed,
                await ReadRunStatusAsync(factory, observed.Id));
            Assert.Null(await ReadRunStatusAsync(factory, skipped.Id));

            var retry = await repository.TryAddStrategyMarketPaperRunsAsync(
                [skipped, observed],
                directPaperSkipCompactionEnabled: true);

            Assert.Empty(retry);
            Assert.Equal(
                new RetentionCounts(1, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_FinalizeObservedToPureSkippedCompactsAtomically()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_finalize_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var observed = CreateSkippedRun(strategyId, nowUtc) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed,
            SkipReason = null
        };
        var skipped = observed with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "direct_finalize_skip",
            SkipDiagnosticsJson = "{\"discarded\":true}",
            UpdatedAtUtc = nowUtc.AddSeconds(1)
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(observed));

            await repository.FinalizeStrategyMarketPaperRunAsync(
                skipped,
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(
                new RetentionCounts(0, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Null(await ReadRunStatusAsync(factory, observed.Id));

            await repository.FinalizeStrategyMarketPaperRunAsync(
                skipped,
                directPaperSkipCompactionEnabled: true);
            Assert.Equal(
                new RetentionCounts(0, 1, 1, 2, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_PaperDependencyAndLiveScopeRemainRaw()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var paperStrategyId = Guid.NewGuid();
        var liveStrategyId = Guid.NewGuid();
        var nowUtc = DateTimeOffset.UtcNow;
        var paperRun = CreateSkippedRun(paperStrategyId, nowUtc);
        var liveRun = CreateSkippedRun(liveStrategyId, nowUtc.AddSeconds(1));

        await InsertStrategyAsync(
            factory,
            paperStrategyId,
            $"direct_paper_guard_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            liveStrategyId,
            $"direct_live_guard_{Guid.NewGuid():N}",
            liveStakes: true);
        try
        {
            await DeleteProjectionBlockersAsync(factory, paperStrategyId);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                paperStrategyId,
                paperRun.ConditionId,
                nowUtc));

            var inserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [paperRun, liveRun],
                directPaperSkipCompactionEnabled: true);

            Assert.Equal(2, inserted.Count);
            Assert.Equal(
                new RetentionCounts(1, 0, 0, 2, 0),
                await ReadRetentionCountsAsync(factory, paperStrategyId));
            Assert.Equal(
                new RetentionCounts(1, 0, 0, 1, 0),
                await ReadRetentionCountsAsync(factory, liveStrategyId));
            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, paperRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, paperStrategyId);
            await DeleteTestStrategyAsync(factory, liveStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_EntryBatchCompactsPureSkipAndPreservesActualPaperRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_batch_{Guid.NewGuid():N}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc);
        var enteredBase = CreateSkippedRun(strategyId, nowUtc.AddSeconds(1));
        var order = CreatePaperOrder(strategyId, enteredBase.ConditionId, nowUtc);
        var leaderTrade = new LeaderTrade(
            $"strategy:{strategyCode}",
            strategyCode,
            order.ConditionId,
            order.AssetId,
            enteredBase.MarketSlug,
            enteredBase.MarketTitle,
            order.Outcome,
            order.Side,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            nowUtc);
        var signal = new Signal(
            order.SignalId,
            leaderTrade,
            100,
            true,
            "direct_batch_entry",
            [],
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            nowUtc);
        var entered = enteredBase with
        {
            Status = StrategyMarketPaperRunStatuses.Entered,
            SelectedAssetId = order.AssetId,
            SelectedOutcome = order.Outcome,
            EntryPrice = order.Price,
            StakeUsd = order.NotionalUsd,
            SizeShares = order.SizeShares,
            SignalId = signal.Id,
            PaperOrderId = order.Id,
            EnteredAtUtc = nowUtc,
            SkipReason = null,
            UpdatedAtUtc = nowUtc
        };
        var batch = new PaperEntryPersistenceBatch(
            [signal],
            [order],
            [],
            [],
            [],
            [skipped, entered])
        {
            DirectPaperSkipCompactionEnabled = true
        };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await repository.AddPaperEntryPersistenceBatchAsync(batch, timeout.Token);

            Assert.Equal(
                new RetentionCounts(1, 1, 1, 3, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Entered,
                await ReadRunStatusAsync(factory, entered.Id));
            Assert.Null(await ReadRunStatusAsync(factory, skipped.Id));
            Assert.Equal(1, await ReadPaperOrderCountAsync(factory, order.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task DirectCompaction_EntryBatchLocksWalletBeforeExclusiveRetentionGate()
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"direct_lock_order_{Guid.NewGuid():N}";
        var wallet = $"strategy:{strategyCode}";
        var nowUtc = DateTimeOffset.UtcNow;
        var skipped = CreateSkippedRun(strategyId, nowUtc);
        var position = new PaperPosition(
            $"asset-{Guid.NewGuid():N}",
            skipped.ConditionId,
            "Yes",
            2m,
            0.50m,
            1m,
            0m,
            nowUtc,
            wallet);
        var batch = new PaperEntryPersistenceBatch(
            [],
            [],
            [],
            [position],
            [],
            [skipped])
        {
            DirectPaperSkipCompactionEnabled = true
        };
        var directApplicationName = $"direct_lock_order_{Guid.NewGuid():N}";
        var directFactory = WithApplicationName(factory, directApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? directTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await using (var blockerConnection = factory.CreateConnection())
            {
                await blockerConnection.OpenAsync();
                await using (var blockerTransaction = await blockerConnection.BeginTransactionAsync())
                {
                    await using (var walletLockCommand = new NpgsqlCommand(
                                     "SELECT pg_advisory_xact_lock(" +
                                     "hashtextextended(@Wallet, 4937427318840178337));",
                                     blockerConnection,
                                     blockerTransaction))
                    {
                        walletLockCommand.Parameters.AddWithValue("Wallet", wallet);
                        await walletLockCommand.ExecuteNonQueryAsync();
                    }

                    directTask = new PostgresAppRepository(directFactory)
                        .AddPaperEntryPersistenceBatchAsync(batch, raceCancellation.Token);
                    var directPid = await WaitForBlockedApplicationAsync(
                        factory,
                        directApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, directPid, blockerConnection.ProcessID);
                    Assert.False(await HoldsExclusiveRetentionGateAsync(factory, directPid));

                    await using var sharedGateCommand = new NpgsqlCommand(
                        "SELECT public.lock_strategy_run_retention_dependency();",
                        blockerConnection,
                        blockerTransaction)
                    {
                        CommandTimeout = 5
                    };
                    await sharedGateCommand.ExecuteNonQueryAsync();
                    await blockerTransaction.CommitAsync();
                }
            }

            await directTask.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(
                StrategyMarketPaperRunStatuses.Skipped,
                await ReadRunStatusAsync(factory, skipped.Id));
        }
        finally
        {
            await DrainRaceTaskAsync(directTask);
            await DeletePaperPositionAsync(factory, position.CopiedTraderWallet, position.AssetId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_CompactsOnlyPreviewedPaperSkipAndPreservesLifetimeTotals()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var run = CreateSkippedRun(strategyId, DateTimeOffset.UtcNow.AddDays(-4));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly, await ReadRetentionScopeAsync(factory, run.Id));
            await projection.BootstrapAsync();

            var before = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardBefore = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(run.Id, preview.CandidateRunIds);

            var result = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                cutoffUtc);

            Assert.Equal(1, result.SelectedRows);
            Assert.Equal(1, result.DeletedRows);
            Assert.Equal(1, result.RollupRowsChanged);
            Assert.Equal(1, result.TombstonesChanged);
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(0, counts.RawRuns);
            Assert.Equal(1, counts.RollupRuns);
            Assert.Equal(1, counts.Tombstones);
            Assert.Equal(0, counts.ProjectionEvents);

            var reconciliation = await projection.ReconcileNextStrategyAsync();
            Assert.True(reconciliation.Reconciled, reconciliation.Error);
            Assert.Equal(strategyId, reconciliation.StrategyId);

            var after = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboardAfter = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            Assert.Equal(before.SkippedRunsCount, after.SkippedRunsCount);
            Assert.Equal(before.PaperConditionSkippedRunsCount, after.PaperConditionSkippedRunsCount);
            Assert.Equal(before.LastRunUtc, after.LastRunUtc);
            Assert.Equal(dashboardBefore, dashboardAfter);

            Assert.False(await repository.TryAddStrategyMarketPaperRunAsync(run with { Id = Guid.NewGuid() }));
            var bulkInserted = await repository.TryAddStrategyMarketPaperRunsAsync(
                [run with { Id = Guid.NewGuid() }]);
            Assert.Empty(bulkInserted);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Bootstrap_FreshV1TombstoneFeedsRecentWhileRollupAloneFeedsLifetime()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var capturedNowUtc = DateTimeOffset.UtcNow;
        var secondAlignedNowUtc = capturedNowUtc.AddTicks(
            -(capturedNowUtc.Ticks % TimeSpan.TicksPerSecond));
        var recentUpdatedAtUtc = secondAlignedNowUtc.AddMinutes(-30);
        var expiredUpdatedAtUtc = secondAlignedNowUtc.AddHours(-25);
        var recentRunId = Guid.NewGuid();
        var expiredRunId = Guid.NewGuid();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertArchivedPaperSkipAsync(
                factory,
                strategyId,
                recentRunId,
                recentUpdatedAtUtc,
                "recent_compact_skip");
            await InsertArchivedPaperSkipAsync(
                factory,
                strategyId,
                expiredRunId,
                expiredUpdatedAtUtc,
                "expired_compact_skip");

            await projection.BootstrapAsync();

            var lifetime = (await snapshots.GetStrategyPerformanceSnapshotAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(0, lifetime.ObservedRunsCount);
            Assert.Equal(2, lifetime.SkippedRunsCount);
            Assert.Equal(2, lifetime.PaperConditionSkippedRunsCount);
            Assert.Equal(0, lifetime.PaperNotAcceptedRunsCount);
            Assert.Equal(recentUpdatedAtUtc, lifetime.LastRunUtc);

            var recentRows = (await snapshots.GetStrategyRecentPerformanceSnapshotAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            Assert.Equal([1, 6, 24], recentRows.Keys.OrderBy(value => value).ToArray());
            foreach (var windowHours in new[] { 1, 6, 24 })
            {
                var row = recentRows[windowHours];
                Assert.Equal(1, row.SkippedRunsCount);
                Assert.Equal(1, row.PaperConditionSkippedRunsCount);
                Assert.Equal(0, row.PaperNotAcceptedRunsCount);
                Assert.Equal("recent_compact_skip:1", row.TopSkipReason);
                Assert.Equal(recentUpdatedAtUtc, row.LastRunUtc);
            }

            var directRecentRows = (await repository.GetStrategyRecentPerformanceAsync())
                .Where(row => row.StrategyId == strategyId)
                .ToDictionary(row => row.WindowHours);
            foreach (var windowHours in new[] { 1, 6, 24 })
            {
                var row = directRecentRows[windowHours];
                Assert.Equal(1, row.SkippedRunsCount);
                Assert.Equal(1, row.PaperConditionSkippedRunsCount);
                Assert.Equal(0, row.PaperNotAcceptedRunsCount);
                Assert.Equal(0, row.LiveSkippedOrdersCount);
                Assert.Equal("recent_compact_skip:1", row.TopSkipReason);
                Assert.Equal(recentUpdatedAtUtc, row.LastRunUtc);
            }

            Assert.Equal(2, await ReadRecentStrategyRunFactCountAsync(factory, recentRunId));
            Assert.Equal(0, await ReadRecentStrategyRunFactCountAsync(factory, expiredRunId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_WhenPaperOrderMakesAllowlistStale_RollsBackEntireBatch()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var firstRun = CreateSkippedRun(strategyId, oldUtc);
        var secondRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(firstRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(secondRun));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.Contains(firstRun.Id, preview.CandidateRunIds);
            Assert.Contains(secondRun.Id, preview.CandidateRunIds);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                secondRun.ConditionId,
                oldUtc.AddMinutes(2)));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [firstRun.Id, secondRun.Id],
                    cutoffUtc));

            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(2, counts.RawRuns);
            Assert.Equal(0, counts.RollupRuns);
            Assert.Equal(0, counts.Tombstones);
            Assert.Equal(0, counts.ReconciliationQueueRows);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveGuard_MakesCurrentAndFutureRunsPermanentlyIneligible()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var liveRun = CreateSkippedRun(strategyId, oldUtc);
        var laterPaperRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: true);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveRun));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));

            await SetStrategyLiveStakesAsync(factory, strategyId, liveStakes: false);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(laterPaperRun));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, laterPaperRun.Id));

            await TryDemoteRetentionScopeAsync(factory, liveRun.Id);
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow, await ReadRetentionScopeAsync(factory, liveRun.Id));
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                DateTimeOffset.UtcNow.AddHours(-48),
                10);
            Assert.DoesNotContain(liveRun.Id, preview.CandidateRunIds);
            Assert.DoesNotContain(laterPaperRun.Id, preview.CandidateRunIds);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveSkipProjectionBoundary_CompactsOnlyPreLiveRunAndKeepsLiveSkipRowsRaw()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var preLiveRun = CreateSkippedRun(strategyId, oldUtc);
        var boundaryRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(1));
        var postLiveRun = CreateSkippedRun(strategyId, oldUtc.AddMinutes(2));
        var allRuns = new[] { preLiveRun, boundaryRun, postLiveRun };

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            Assert.True(await repository.SetStrategyLiveStakesAsync(
                strategyId,
                liveStakes: true,
                updatedAtUtc: boundaryRun.UpdatedAtUtc));
            foreach (var run in allRuns)
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }

            await DeleteProjectionBlockersAsync(factory, strategyId);

            var blockers = await ReadLegacyBlockersAsync(
                factory,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Empty(blockers[preLiveRun.Id]);
            Assert.Equal(["live_skip_projection_dependency"], blockers[boundaryRun.Id]);
            Assert.Equal(["live_skip_projection_dependency"], blockers[postLiveRun.Id]);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixtureIds = allRuns.Select(run => run.Id).ToHashSet();
            Assert.Equal(
                [preLiveRun.Id],
                preview.CandidateRunIds.Where(fixtureIds.Contains).ToArray());
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
            Assert.Equal(baseline.TotalCandidateRows + 1, summary.TotalCandidateRows);

            var performanceBefore = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(3, performanceBefore.SkippedRunsCount);
            Assert.Equal(3, performanceBefore.PaperConditionSkippedRunsCount);
            Assert.Equal(2, performanceBefore.LiveSkippedOrdersCount);
            Assert.Equal(0, performanceBefore.LiveConditionSkippedOrdersCount);
            Assert.Equal(2, performanceBefore.LiveTechnicalSkippedOrdersCount);
            Assert.Equal(0, performanceBefore.LiveIgnoredOrdersCount);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [preLiveRun.Id, boundaryRun.Id],
                    cutoffUtc));
            Assert.Equal(
                new RetentionCounts(3, 0, 0, 0, 0),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));

            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [preLiveRun.Id],
                cutoffUtc);
            Assert.Equal(1, transfer.SelectedRows);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(1, transfer.RollupRowsChanged);
            Assert.Equal(1, transfer.TombstonesChanged);
            Assert.Equal(
                new RetentionCounts(2, 1, 1, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                new[] { boundaryRun.Id, postLiveRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));

            var performanceAfter = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            Assert.Equal(performanceBefore.SkippedRunsCount, performanceAfter.SkippedRunsCount);
            Assert.Equal(
                performanceBefore.PaperConditionSkippedRunsCount,
                performanceAfter.PaperConditionSkippedRunsCount);
            Assert.Equal(performanceBefore.LiveSkippedOrdersCount, performanceAfter.LiveSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveConditionSkippedOrdersCount,
                performanceAfter.LiveConditionSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveTechnicalSkippedOrdersCount,
                performanceAfter.LiveTechnicalSkippedOrdersCount);
            Assert.Equal(
                performanceBefore.LiveIgnoredOrdersCount,
                performanceAfter.LiveIgnoredOrdersCount);
            Assert.Equal(performanceBefore.LastRunUtc, performanceAfter.LastRunUtc);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task SetBasedEligibility_MatchesLegacyBlockersAndPreservesPaperAndLiveHistory()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var paperStrategyId = Guid.NewGuid();
        var liveStrategyId = Guid.NewGuid();
        var paperStrategyCode = $"retention_{Guid.NewGuid():N}";
        var liveStrategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-eligibility-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var controlRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc),
            $"{prefix}-control");
        var dependencyOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-paper-dependency",
            oldUtc);
        var paperDependencyRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(1)),
            dependencyOrder.ConditionId);
        var enteredOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-entered",
            oldUtc.AddMinutes(2));
        var enteredRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(2)) with
            {
                Status = StrategyMarketPaperRunStatuses.Entered,
                SelectedAssetId = enteredOrder.AssetId,
                SelectedOutcome = enteredOrder.Outcome,
                EntryPrice = enteredOrder.Price,
                SizeShares = enteredOrder.SizeShares,
                PaperOrderId = enteredOrder.Id,
                EnteredAtUtc = oldUtc.AddMinutes(2),
                SkipReason = null
            },
            enteredOrder.ConditionId);
        var settledOrder = CreatePaperOrder(
            paperStrategyId,
            $"{prefix}-settled",
            oldUtc.AddMinutes(3));
        var settledRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(3)) with
            {
                Status = StrategyMarketPaperRunStatuses.Settled,
                SelectedAssetId = settledOrder.AssetId,
                SelectedOutcome = settledOrder.Outcome,
                EntryPrice = settledOrder.Price,
                SizeShares = settledOrder.SizeShares,
                PaperOrderId = settledOrder.Id,
                EnteredAtUtc = oldUtc.AddMinutes(3),
                SettlementPrice = 1m,
                SettlementValueUsd = settledOrder.SizeShares,
                RealizedPnlUsd = settledOrder.SizeShares - settledOrder.NotionalUsd,
                SettledAtUtc = oldUtc.AddHours(1),
                SkipReason = null
            },
            settledOrder.ConditionId);
        var diagnosticRun = WithRetentionKey(
            CreateSkippedRun(paperStrategyId, oldUtc.AddMinutes(4)) with
            {
                SkipDiagnosticsJson = "{}"
            },
            $"{prefix}-diagnostic");
        var liveRun = WithRetentionKey(
            CreateSkippedRun(liveStrategyId, oldUtc.AddMinutes(5)),
            $"{prefix}-live");
        var allRuns = new[]
        {
            controlRun,
            paperDependencyRun,
            enteredRun,
            settledRun,
            diagnosticRun,
            liveRun
        };

        try
        {
            await InsertStrategyAsync(factory, paperStrategyId, paperStrategyCode, liveStakes: false);
            await InsertStrategyAsync(factory, liveStrategyId, liveStrategyCode, liveStakes: true);
            await repository.AddPaperOrderAsync(dependencyOrder);
            await repository.AddPaperOrderAsync(enteredOrder);
            await repository.AddPaperOrderAsync(settledOrder);
            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            await AddSkipDiagnosticsAsync(factory, diagnosticRun.Id);

            await repository.AddPaperFillAsync(new PaperFill(
                Guid.NewGuid(),
                enteredOrder.Id,
                enteredOrder.Price,
                enteredOrder.SizeShares,
                oldUtc.AddMinutes(3),
                "retention integration test"));
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                enteredOrder.AssetId,
                enteredOrder.ConditionId,
                enteredOrder.Outcome,
                enteredOrder.SizeShares,
                enteredOrder.Price,
                enteredOrder.NotionalUsd,
                0m,
                oldUtc.AddMinutes(3),
                $"strategy:{paperStrategyCode}"));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(new PaperPositionSettlement(
                Guid.NewGuid(),
                $"strategy:{paperStrategyCode}",
                settledOrder.AssetId,
                settledOrder.ConditionId,
                settledOrder.Outcome,
                settledOrder.AssetId,
                settledOrder.Outcome,
                "IntegrationTest",
                settledOrder.SizeShares,
                settledOrder.Price,
                settledOrder.NotionalUsd,
                settledOrder.SizeShares,
                settledOrder.SizeShares - settledOrder.NotionalUsd,
                true,
                "IntegrationTest",
                oldUtc.AddHours(1),
                oldUtc.AddHours(1))));

            await DeleteProjectionBlockersAsync(factory, paperStrategyId);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);

            foreach (var run in allRuns.Where(run => run.StrategyId == paperStrategyId))
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));

            var legacyEligibleIds = await ReadLegacyEligibleRunIdsAsync(
                factory,
                cutoffUtc,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Equal([controlRun.Id], legacyEligibleIds);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixturePreviewIds = preview.CandidateRunIds
                .Where(id => allRuns.Any(run => run.Id == id))
                .ToArray();
            Assert.Equal(legacyEligibleIds, fixturePreviewIds);

            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 25_000);
            Assert.Equal(baseline.TotalCandidateRows + 1, summary.TotalCandidateRows);
            Assert.Contains(controlRun.Id, summary.SampleRunIds);

            var historyCountsBefore = await ReadPaperHistoryCountsAsync(factory, paperStrategyId);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [controlRun.Id, enteredRun.Id, settledRun.Id, liveRun.Id],
                    cutoffUtc));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));
            Assert.Equal(historyCountsBefore, await ReadPaperHistoryCountsAsync(factory, paperStrategyId));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, paperStrategyId);
            await DeleteTestStrategyAsync(factory, liveStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task EveryExternalBlocker_MatchesLegacyEligibilityAndKeepsRunsRaw()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var queueStrategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var queueStrategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-blockers-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var runIndex = 0;

        StrategyMarketPaperRun CreateFixtureRun(Guid targetStrategyId, string blockerName)
        {
            var run = CreateSkippedRun(targetStrategyId, oldUtc.AddMinutes(runIndex++));
            return WithRetentionKey(run, $"{prefix}-{blockerName}");
        }

        var controlRun = CreateFixtureRun(strategyId, "control");
        var paperOrderRun = CreateFixtureRun(strategyId, "paper-order");
        var dryRun = CreateFixtureRun(strategyId, "dry-run");
        var liveOrderRun = CreateFixtureRun(strategyId, "live-order");
        var shadowMarketRun = CreateFixtureRun(strategyId, "shadow-market");
        var shadowConditionRun = CreateFixtureRun(strategyId, "shadow-condition");
        var paperPositionRun = CreateFixtureRun(strategyId, "paper-position");
        var paperSettlementRun = CreateFixtureRun(strategyId, "paper-settlement");
        var copiedLeaderPositionRun = CreateFixtureRun(strategyId, "copied-position");
        var copiedLeaderActivityRun = CreateFixtureRun(strategyId, "copied-activity");
        var onchainRun = CreateFixtureRun(strategyId, "onchain");
        var tombstoneRun = CreateFixtureRun(strategyId, "tombstone");
        var projectionEventRun = CreateFixtureRun(strategyId, "projection-event");
        var recentFactRun = CreateFixtureRun(strategyId, "recent-fact");
        var reconciliationRun = CreateFixtureRun(queueStrategyId, "reconciliation");
        var allRuns = new[]
        {
            controlRun,
            paperOrderRun,
            dryRun,
            liveOrderRun,
            shadowMarketRun,
            shadowConditionRun,
            paperPositionRun,
            paperSettlementRun,
            copiedLeaderPositionRun,
            copiedLeaderActivityRun,
            onchainRun,
            tombstoneRun,
            projectionEventRun,
            recentFactRun,
            reconciliationRun
        };
        var expectedBlockers = new Dictionary<Guid, string>
        {
            [paperOrderRun.Id] = "paper_order_dependency",
            [dryRun.Id] = "dry_run_dependency",
            [liveOrderRun.Id] = "live_order_dependency",
            [shadowMarketRun.Id] = "live_shadow_dependency",
            [shadowConditionRun.Id] = "live_shadow_dependency",
            [paperPositionRun.Id] = "paper_position_dependency",
            [paperSettlementRun.Id] = "paper_settlement_dependency",
            [copiedLeaderPositionRun.Id] = "copied_leader_position_dependency",
            [copiedLeaderActivityRun.Id] = "copied_leader_activity_dependency",
            [onchainRun.Id] = "onchain_paper_dependency",
            [tombstoneRun.Id] = "existing_tombstone",
            [projectionEventRun.Id] = "pending_projection_event",
            [recentFactRun.Id] = "recent_projection_fact",
            [reconciliationRun.Id] = "pending_projection_reconciliation"
        };

        try
        {
            await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
            await InsertStrategyAsync(factory, queueStrategyId, queueStrategyCode, liveStakes: false);

            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                paperOrderRun.ConditionId,
                oldUtc));
            await repository.AddLiveOrderAsync(CreateLiveOrder(
                strategyId,
                liveOrderRun.ConditionId,
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                shadowMarketRun.MarketId,
                $"{prefix}-unrelated-shadow-condition",
                oldUtc));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                strategyId,
                $"{prefix}-unrelated-shadow-market",
                shadowConditionRun.ConditionId,
                oldUtc));
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                paperPositionRun.ConditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{strategyCode}"));
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(new PaperPositionSettlement(
                Guid.NewGuid(),
                $"strategy:{strategyCode}",
                $"asset-{Guid.NewGuid():N}",
                paperSettlementRun.ConditionId,
                "Yes",
                null,
                "No",
                "IntegrationTest",
                2m,
                0.50m,
                1m,
                0m,
                -1m,
                false,
                "IntegrationTest",
                oldUtc,
                oldUtc)));
            await InsertDirectExternalDependenciesAsync(
                factory,
                strategyId,
                oldUtc,
                dryRun,
                copiedLeaderPositionRun,
                copiedLeaderActivityRun,
                onchainRun);

            Assert.Equal(
                allRuns.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(allRuns)).Count);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await DeleteProjectionBlockersAsync(factory, queueStrategyId);
            await InsertPostRunExternalBlockersAsync(
                factory,
                strategyId,
                queueStrategyId,
                oldUtc,
                tombstoneRun,
                projectionEventRun,
                recentFactRun);

            foreach (var run in allRuns)
            {
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, run.Id));
            }

            var legacyBlockers = await ReadLegacyBlockersAsync(
                factory,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Empty(legacyBlockers[controlRun.Id]);
            foreach (var (runId, expectedBlocker) in expectedBlockers)
            {
                Assert.Equal([expectedBlocker], legacyBlockers[runId]);
            }

            var legacyEligibleIds = await ReadLegacyEligibleRunIdsAsync(
                factory,
                cutoffUtc,
                allRuns.Select(run => run.Id).ToArray());
            Assert.Equal([controlRun.Id], legacyEligibleIds);

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            var fixtureIds = allRuns.Select(run => run.Id).ToHashSet();
            Assert.Equal(
                legacyEligibleIds,
                preview.CandidateRunIds.Where(fixtureIds.Contains).ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    allRuns.Select(run => run.Id).ToArray(),
                    cutoffUtc));
            Assert.Equal(
                allRuns.Select(run => run.Id).OrderBy(id => id),
                (await ReadRunIdsAsync(factory, allRuns.Select(run => run.Id).ToArray())).OrderBy(id => id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, strategyId);
            await DeleteTestStrategyAsync(factory, queueStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Eligibility_UsesPublicDependenciesWhenSearchPathIsShadowed()
    {
        var factory = await CreateFactoryAsync();
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var shadowSchema = $"retention_shadow_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-search-path-{Guid.NewGuid():N}");

        try
        {
            await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
            var repository = new PostgresAppRepository(factory);
            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                run.ConditionId,
                oldUtc));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            await CreateShadowPaperOrdersSchemaAsync(factory, shadowSchema);

            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
            {
                SearchPath = $"{shadowSchema},public"
            };
            var shadowFactory = new PostgresConnectionFactory(new StorageOptions
            {
                ConnectionString = connectionStringBuilder.ConnectionString
            });
            await using (var connection = shadowFactory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SHOW search_path;", connection);
                Assert.Equal($"{shadowSchema},public", await command.ExecuteScalarAsync());
            }

            var preview = await new PostgresAppRepository(shadowFactory)
                .PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.DoesNotContain(run.Id, preview.CandidateRunIds);
            Assert.Equal(
                [run.Id],
                await ReadRunIdsAsync(factory, [run.Id]));
        }
        finally
        {
            await DropShadowSchemaAsync(factory, shadowSchema);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task LiveOrderAndShadowDecision_PromoteRunsAndKeepThemRaw()
    {
        var factory = await CreateFactoryAsync();

        var repository = new PostgresAppRepository(factory);
        var liveOrderStrategyId = Guid.NewGuid();
        var shadowStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var prefix = $"retention-promotion-{Guid.NewGuid():N}";
        var liveOrderRun = WithRetentionKey(
            CreateSkippedRun(liveOrderStrategyId, oldUtc),
            $"{prefix}-live-order");
        var shadowRun = WithRetentionKey(
            CreateSkippedRun(shadowStrategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-shadow");

        try
        {
            await InsertStrategyAsync(
                factory,
                liveOrderStrategyId,
                $"retention_{Guid.NewGuid():N}",
                liveStakes: false);
            await InsertStrategyAsync(
                factory,
                shadowStrategyId,
                $"retention_{Guid.NewGuid():N}",
                liveStakes: false);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveOrderRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(shadowRun));
            await DeleteProjectionBlockersAsync(factory, liveOrderStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            var before = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.Contains(liveOrderRun.Id, before.CandidateRunIds);
            Assert.Contains(shadowRun.Id, before.CandidateRunIds);

            await repository.AddLiveOrderAsync(CreateLiveOrder(
                liveOrderStrategyId,
                liveOrderRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                shadowStrategyId,
                shadowRun.MarketId,
                shadowRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await DeleteProjectionBlockersAsync(factory, liveOrderStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveOrderRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowRun.Id));

            var after = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25_000);
            Assert.DoesNotContain(liveOrderRun.Id, after.CandidateRunIds);
            Assert.DoesNotContain(shadowRun.Id, after.CandidateRunIds);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                    [liveOrderRun.Id, shadowRun.Id],
                    cutoffUtc));
            Assert.Equal(
                new[] { liveOrderRun.Id, shadowRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, [liveOrderRun.Id, shadowRun.Id])).OrderBy(id => id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, liveOrderStrategyId);
            await DeleteTestStrategyAsync(factory, shadowStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRun_PaperOrderRestoresExactRawRunAndReversesRollup()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var projection = new PostgresDashboardProjectionRepository(factory);
        var snapshots = new PostgresDashboardSnapshotRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-paper-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await projection.BootstrapAsync();
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, run.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                cutoffUtc);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(new RetentionCounts(0, 1, 1, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));

            await repository.AddPaperOrderAsync(CreatePaperOrder(
                strategyId,
                run.ConditionId,
                oldUtc.AddMinutes(1)));

            Assert.Equal(originalPayload, await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, run.Id));
            var restored = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(1, restored.RawRuns);
            Assert.Equal(0, restored.RollupRuns);
            Assert.Equal(0, restored.Tombstones);
            Assert.Equal(1, restored.ReconciliationQueueRows);
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));

            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10);
            Assert.DoesNotContain(run.Id, preview.CandidateRunIds);
            Assert.Contains(
                "paper_order_dependency",
                await ReadRunBlockersAsync(factory, run.Id));

            var reconciliation = await projection.ReconcileNextStrategyAsync();
            Assert.True(reconciliation.Reconciled, reconciliation.Error);
            Assert.Equal(strategyId, reconciliation.StrategyId);
            var authoritative = (await repository.GetStrategyPerformanceAsync())
                .Single(row => row.StrategyId == strategyId);
            var dashboard = await ReadDashboardSnapshotAsync(snapshots, strategyId);
            AssertStrategyLifetimeMetricsEqual(authoritative, dashboard);
            var reconciledCounts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(0, reconciledCounts.ProjectionEvents);
            Assert.Equal(0, reconciledCounts.ReconciliationQueueRows);
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_RacingPaperOrderWaitsAndRestoresRawRunAfterCommit()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-paper-race-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var anchor = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"{prefix}-anchor");
        var candidate = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-candidate");
        var retentionApplicationName = $"retention_race_{Guid.NewGuid():N}";
        var dependencyApplicationName = $"paper_race_{Guid.NewGuid():N}";
        var retentionFactory = WithApplicationName(factory, retentionApplicationName);
        var dependencyFactory = WithApplicationName(factory, dependencyApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<StrategyRunRetentionBatchResult>? retentionTask = null;
        Task? dependencyTask = null;

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(anchor));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [anchor.Id],
                cutoffUtc)).DeletedRows);
            var anchorRollup = await ReadRollupGroupAsync(
                factory,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(candidate));
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, candidate.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Contains(
                candidate.Id,
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10))
                    .CandidateRunIds);

            await using var blockerConnection = factory.CreateConnection();
            await blockerConnection.OpenAsync();
            await using var blockerTransaction = await blockerConnection.BeginTransactionAsync();
            await LockRollupGroupAsync(
                blockerConnection,
                blockerTransaction,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);

            retentionTask = new PostgresAppRepository(retentionFactory)
                .TransferPaperOnlySkippedRunsToRollupsAsync(
                    [candidate.Id],
                    cutoffUtc,
                    raceCancellation.Token);
            var retentionPid = await WaitForBlockedApplicationAsync(
                factory,
                retentionApplicationName,
                "transactionid");
            await AssertBlockedByAsync(factory, retentionPid, blockerConnection.ProcessID);
            Assert.True(await HoldsExclusiveRetentionGateAsync(factory, retentionPid));
            await AssertRunRowIsLockedAsync(factory, candidate.Id);

            var order = CreatePaperOrder(
                strategyId,
                candidate.ConditionId,
                oldUtc.AddMinutes(2));
            dependencyTask = new PostgresAppRepository(dependencyFactory)
                .AddPaperOrderAsync(order, raceCancellation.Token);
            var dependencyPid = await WaitForBlockedApplicationAsync(
                factory,
                dependencyApplicationName,
                "advisory");
            await AssertBlockedByAsync(factory, dependencyPid, retentionPid);
            Assert.False(dependencyTask.IsCompleted);
            Assert.False(retentionTask.IsCompleted);

            await blockerTransaction.CommitAsync();
            var transfer = await retentionTask.WaitAsync(TimeSpan.FromSeconds(15));
            await dependencyTask.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, transfer.SelectedRows);
            Assert.Equal(1, transfer.DeletedRows);
            Assert.Equal(1, transfer.TombstonesChanged);

            Assert.Equal(originalPayload,
                await ReadRunPayloadWithoutScopeAsync(factory, candidate.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, candidate.Id));
            Assert.Equal(new RetentionCounts(1, 1, 1, 1, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            var rollup = await ReadRollupGroupAsync(
                factory,
                strategyId,
                anchor.UpdatedAtUtc,
                anchor.SkipReason!);
            Assert.Equal(anchorRollup, rollup);
            Assert.Equal([anchor.Id], await ReadArchivedRunIdsAsync(factory, strategyId));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, candidate.Id));
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(dependencyTask);
            await DrainRaceTaskAsync(retentionTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Transfer_RacingCommittedPaperOrderFirstRejectsStaleAllowlist()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-paper-first-race-{Guid.NewGuid():N}");
        var retentionApplicationName = $"retention_dep_first_{Guid.NewGuid():N}";
        var retentionFactory = WithApplicationName(factory, retentionApplicationName);
        var order = CreatePaperOrder(strategyId, run.ConditionId, oldUtc.AddMinutes(1));
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<StrategyRunRetentionBatchResult>? retentionTask = null;

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Contains(
                run.Id,
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 10))
                    .CandidateRunIds);

            await using var dependencyConnection = factory.CreateConnection();
            await dependencyConnection.OpenAsync();
            await using var dependencyTransaction =
                await dependencyConnection.BeginTransactionAsync();
            Assert.Equal(1, await InsertPaperOrderAsync(
                dependencyConnection,
                dependencyTransaction,
                order));

            retentionTask = new PostgresAppRepository(retentionFactory)
                .TransferPaperOnlySkippedRunsToRollupsAsync(
                    [run.Id],
                    cutoffUtc,
                    raceCancellation.Token);
            var retentionPid = await WaitForBlockedApplicationAsync(
                factory,
                retentionApplicationName,
                "advisory");
            await AssertBlockedByAsync(factory, retentionPid, dependencyConnection.ProcessID);
            Assert.False(retentionTask.IsCompleted);

            await dependencyTransaction.CommitAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await retentionTask.WaitAsync(TimeSpan.FromSeconds(15)));

            Assert.Equal([run.Id], await ReadRunIdsAsync(factory, [run.Id]));
            Assert.Equal(1, await ReadPaperOrderCountAsync(factory, order.Id));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal(1, counts.RawRuns);
            Assert.Equal(0, counts.RollupRuns);
            Assert.Equal(0, counts.Tombstones);
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(retentionTask);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRuns_LiveOrderAndShadowDecisionRestoreExactPromotedRawRuns()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var liveStrategyId = Guid.NewGuid();
        var shadowStrategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var prefix = $"retention-live-restore-{Guid.NewGuid():N}";
        var liveRun = WithRetentionKey(
            CreateSkippedRun(liveStrategyId, oldUtc),
            $"{prefix}-live");
        var shadowRun = WithRetentionKey(
            CreateSkippedRun(shadowStrategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-shadow");

        await InsertStrategyAsync(
            factory,
            liveStrategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        await InsertStrategyAsync(
            factory,
            shadowStrategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(shadowRun));
            var livePayload = await ReadRunPayloadWithoutScopeAsync(factory, liveRun.Id);
            var shadowPayload = await ReadRunPayloadWithoutScopeAsync(factory, shadowRun.Id);
            await DeleteProjectionBlockersAsync(factory, liveStrategyId);
            await DeleteProjectionBlockersAsync(factory, shadowStrategyId);

            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [liveRun.Id],
                cutoffUtc)).DeletedRows);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [shadowRun.Id],
                cutoffUtc)).DeletedRows);

            await repository.AddLiveOrderAsync(CreateLiveOrder(
                liveStrategyId,
                liveRun.ConditionId,
                oldUtc.AddMinutes(2)));
            await repository.AddPaperLiveShadowDecisionAsync(CreateShadowDecision(
                shadowStrategyId,
                shadowRun.MarketId,
                shadowRun.ConditionId,
                oldUtc.AddMinutes(2)));

            Assert.Equal(livePayload, await ReadRunPayloadWithoutScopeAsync(factory, liveRun.Id));
            Assert.Equal(shadowPayload, await ReadRunPayloadWithoutScopeAsync(factory, shadowRun.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, liveRun.Id));
            Assert.Equal(StrategyRunRetentionScopes.LiveOrShadow,
                await ReadRetentionScopeAsync(factory, shadowRun.Id));
            Assert.Equal(0, (await ReadRetentionCountsAsync(factory, liveStrategyId)).RollupRuns);
            Assert.Equal(0, (await ReadRetentionCountsAsync(factory, shadowStrategyId)).RollupRuns);
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, liveRun.Id));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, shadowRun.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, liveStrategyId);
            await DeleteTestStrategyAsync(factory, shadowStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_RolledBackPaperOrderRollsBackRawRunAndRollupCompensation()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-rollback-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var before = await ReadRetentionCountsAsync(factory, strategyId);
            var order = CreatePaperOrder(strategyId, run.ConditionId, oldUtc.AddMinutes(1));

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                Assert.Equal(1, await InsertPaperOrderAsync(connection, transaction, order));
                Assert.Equal(1, await ReadRunCountAsync(connection, transaction, run.Id));
                Assert.Equal(0, await ReadTombstoneCountAsync(connection, transaction, run.Id));
                await transaction.RollbackAsync();
            }

            Assert.Equal(before, await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
            Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_ConflictDoNothingDoesNotRestoreArchivedRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-conflict-restore-{Guid.NewGuid():N}");
        var existingOrder = CreatePaperOrder(
            strategyId,
            $"retention-unrelated-{Guid.NewGuid():N}",
            oldUtc);

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await repository.AddPaperOrderAsync(existingOrder);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            var before = await ReadRetentionCountsAsync(factory, strategyId);

            Assert.Equal(0, await InsertConflictingPaperOrderAsync(
                factory,
                existingOrder,
                run.ConditionId));

            Assert.Equal(before, await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_LegacyTombstoneRejectsDependencyWriteFailClosed()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var archivedRunId = Guid.NewGuid();
        var order = CreatePaperOrder(
            strategyId,
            $"retention-legacy-condition-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow.AddDays(-4));

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "INSERT INTO public.strategy_market_paper_skip_tombstones " +
                    "(strategy_id, market_id, archived_run_id, archived_at_utc) " +
                    "VALUES (@StrategyId, @MarketId, @ArchivedRunId, @ArchivedAtUtc);",
                    connection);
                command.Parameters.AddWithValue("StrategyId", strategyId);
                command.Parameters.AddWithValue("MarketId", $"retention-legacy-market-{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("ArchivedRunId", archivedRunId);
                command.Parameters.AddWithValue("ArchivedAtUtc", DateTime.UtcNow.AddDays(-3));
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => repository.AddPaperOrderAsync(order));
            Assert.Equal("55000", exception.SqlState);
            Assert.Contains("legacy/incomplete tombstone", exception.MessageText);
            Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
            Assert.Empty(await ReadRunIdsAsync(factory, [archivedRunId]));
            Assert.Equal([archivedRunId], await ReadArchivedRunIdsAsync(factory, strategyId));
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_PositionWalletMatchingCaseVariantCodesRestoresEveryRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var firstStrategyId = Guid.NewGuid();
        var secondStrategyId = Guid.NewGuid();
        var codeSuffix = Guid.NewGuid().ToString("N");
        var firstStrategyCode = $"RetentionCase_{codeSuffix}";
        var secondStrategyCode = $"retentioncase_{codeSuffix}";
        var conditionId = $"retention-case-wallet-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var firstRun = WithRetentionKey(
            CreateSkippedRun(firstStrategyId, oldUtc),
            conditionId);
        var secondRun = WithRetentionKey(
            CreateSkippedRun(secondStrategyId, oldUtc.AddMinutes(1)),
            conditionId);

        await InsertStrategyAsync(factory, firstStrategyId, firstStrategyCode, liveStakes: false);
        await InsertStrategyAsync(factory, secondStrategyId, secondStrategyCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(firstRun));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(secondRun));
            await DeleteProjectionBlockersAsync(factory, firstStrategyId);
            await DeleteProjectionBlockersAsync(factory, secondStrategyId);
            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [firstRun.Id, secondRun.Id],
                DateTimeOffset.UtcNow.AddHours(-48));
            Assert.Equal(2, transfer.DeletedRows);

            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                conditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{firstStrategyCode.ToUpperInvariant()}"));

            Assert.Equal(
                new[] { firstRun.Id, secondRun.Id }.OrderBy(id => id),
                (await ReadRunIdsAsync(factory, [firstRun.Id, secondRun.Id])).OrderBy(id => id));
            var firstCounts = await ReadRetentionCountsAsync(factory, firstStrategyId);
            var secondCounts = await ReadRetentionCountsAsync(factory, secondStrategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (firstCounts.RawRuns, firstCounts.RollupRuns,
                    firstCounts.Tombstones, firstCounts.ReconciliationQueueRows));
            Assert.Equal((1L, 0L, 0L, 1L),
                (secondCounts.RawRuns, secondCounts.RollupRuns,
                    secondCounts.Tombstones, secondCounts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, firstRun.Id));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, secondRun.Id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, firstStrategyId);
            await DeleteTestStrategyAsync(factory, secondStrategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_StrategyCodeUpdateThatCreatesPositionMatchRestoresRun()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldCode = $"retention_old_{Guid.NewGuid():N}";
        var newCode = $"retention_new_{Guid.NewGuid():N}";
        var conditionId = $"retention-code-update-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(CreateSkippedRun(strategyId, oldUtc), conditionId);

        await InsertStrategyAsync(factory, strategyId, oldCode, liveStakes: false);
        try
        {
            await repository.UpsertPaperPositionAsync(new PaperPosition(
                $"asset-{Guid.NewGuid():N}",
                conditionId,
                "Yes",
                2m,
                0.50m,
                1m,
                0m,
                oldUtc,
                $"strategy:{newCode}"));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);

            await using (var connection = factory.CreateConnection())
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "UPDATE public.strategies SET code = @Code, updated_at_utc = clock_timestamp() " +
                    "WHERE id = @StrategyId;",
                    connection);
                command.Parameters.AddWithValue("Code", newCode);
                command.Parameters.AddWithValue("StrategyId", strategyId);
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
            }

            Assert.Equal([run.Id], await ReadRunIdsAsync(factory, [run.Id]));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (counts.RawRuns, counts.RollupRuns,
                    counts.Tombstones, counts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));
            Assert.Contains("paper_position_dependency", await ReadRunBlockersAsync(factory, run.Id));
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_RacingStrategyCodeAndPositionWritesPreserveRunInBothOrders()
    {
        await AssertStrategyCodePositionRaceAsync(codeUpdateFirst: true);
        await AssertStrategyCodePositionRaceAsync(codeUpdateFirst: false);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task ArchivedRuns_RemainingPaperDependenciesRestoreExactRawRunsAndReverseRollup()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-remaining-restore-{Guid.NewGuid():N}";
        var oldUtc = new DateTimeOffset(
            DateTime.UtcNow.Date.AddDays(-4).AddHours(12),
            TimeSpan.Zero);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var dryRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"{prefix}-dry-run");
        var copiedPositionRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(1)),
            $"{prefix}-copied-position");
        var copiedActivityRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(2)),
            $"{prefix}-copied-activity");
        var onchainRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(3)),
            $"{prefix}-onchain");
        var settlementRun = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc.AddMinutes(4)),
            $"{prefix}-settlement");
        var runs = new[]
        {
            dryRun,
            copiedPositionRun,
            copiedActivityRun,
            onchainRun,
            settlementRun
        };
        var settlement = new PaperPositionSettlement(
            Guid.NewGuid(),
            $"strategy:{strategyCode}",
            $"asset-{Guid.NewGuid():N}",
            settlementRun.ConditionId,
            "Yes",
            $"asset-{Guid.NewGuid():N}",
            "Yes",
            "IntegrationTest",
            2m,
            0.50m,
            1m,
            2m,
            1m,
            true,
            "IntegrationTest",
            oldUtc.AddHours(1),
            oldUtc.AddHours(1));

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            Assert.Equal(runs.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(runs)).Count);
            var originalPayloads = new Dictionary<Guid, string>();
            foreach (var run in runs)
            {
                originalPayloads.Add(
                    run.Id,
                    await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            }

            await DeleteProjectionBlockersAsync(factory, strategyId);
            var transfer = await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                runs.Select(run => run.Id).ToArray(),
                cutoffUtc);
            Assert.Equal(runs.Length, transfer.SelectedRows);
            Assert.Equal(runs.Length, transfer.DeletedRows);
            Assert.Equal(1, transfer.RollupRowsChanged);
            Assert.Equal(runs.Length, transfer.TombstonesChanged);
            Assert.Equal(new RetentionCounts(0, runs.Length, runs.Length, 0, 1),
                await ReadRetentionCountsAsync(factory, strategyId));
            Assert.Equal(
                runs.Select(run => run.Id).OrderBy(id => id),
                await ReadArchivedRunIdsAsync(factory, strategyId));

            await DeleteProjectionBlockersAsync(factory, strategyId);
            await InsertDirectExternalDependenciesAsync(
                factory,
                strategyId,
                oldUtc.AddHours(2),
                dryRun,
                copiedPositionRun,
                copiedActivityRun,
                onchainRun);

            var afterFourRestores = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((4L, 1L, 1L, 1L),
                (afterFourRestores.RawRuns,
                    afterFourRestores.RollupRuns,
                    afterFourRestores.Tombstones,
                    afterFourRestores.ReconciliationQueueRows));
            Assert.Equal([settlementRun.Id],
                await ReadArchivedRunIdsAsync(factory, strategyId));
            Assert.Equal(
                new RollupGroup(
                    1,
                    settlementRun.UpdatedAtUtc,
                    settlementRun.UpdatedAtUtc),
                await ReadRollupGroupAsync(
                    factory,
                    strategyId,
                    settlementRun.UpdatedAtUtc,
                    settlementRun.SkipReason!));

            var firstRestoreExpectations = new[]
            {
                (Run: dryRun, Blocker: "dry_run_dependency"),
                (Run: copiedPositionRun, Blocker: "copied_leader_position_dependency"),
                (Run: copiedActivityRun, Blocker: "copied_leader_activity_dependency"),
                (Run: onchainRun, Blocker: "onchain_paper_dependency")
            };
            foreach (var expectation in firstRestoreExpectations)
            {
                Assert.Equal(
                    originalPayloads[expectation.Run.Id],
                    await ReadRunPayloadWithoutScopeAsync(factory, expectation.Run.Id));
                Assert.Equal(
                    StrategyRunRetentionScopes.PaperOnly,
                    await ReadRetentionScopeAsync(factory, expectation.Run.Id));
                Assert.Contains(
                    expectation.Blocker,
                    await ReadRunBlockersAsync(factory, expectation.Run.Id));
                Assert.Equal(
                    0,
                    await ReadStrategyRunProjectionEventCountAsync(factory, expectation.Run.Id));
            }

            Assert.True(await repository.TryAddPaperPositionSettlementAsync(settlement));

            Assert.Equal(
                originalPayloads[settlementRun.Id],
                await ReadRunPayloadWithoutScopeAsync(factory, settlementRun.Id));
            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, settlementRun.Id));
            Assert.Contains(
                "paper_settlement_dependency",
                await ReadRunBlockersAsync(factory, settlementRun.Id));
            Assert.Equal(
                0,
                await ReadStrategyRunProjectionEventCountAsync(factory, settlementRun.Id));
            var restored = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((5L, 0L, 0L, 1L),
                (restored.RawRuns,
                    restored.RollupRuns,
                    restored.Tombstones,
                    restored.ReconciliationQueueRows));
            Assert.Empty(await ReadArchivedRunIdsAsync(factory, strategyId));
            var restoredRunIds = runs.Select(run => run.Id).ToHashSet();
            Assert.DoesNotContain(
                (await repository.PreviewPaperOnlySkippedRunRetentionAsync(cutoffUtc, 25))
                    .CandidateRunIds,
                restoredRunIds.Contains);
        }
        finally
        {
            await DeleteConditionDependenciesAsync(factory, prefix);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Restore_HigherIsolationDependencyWriteFailsClosedWithoutChangingArchive()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(
            CreateSkippedRun(strategyId, oldUtc),
            $"retention-isolation-restore-{Guid.NewGuid():N}");

        await InsertStrategyAsync(
            factory,
            strategyId,
            $"retention_{Guid.NewGuid():N}",
            liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            var archived = await ReadRetentionCountsAsync(factory, strategyId);

            foreach (var isolationLevel in new[]
                     {
                         IsolationLevel.RepeatableRead,
                         IsolationLevel.Serializable
                     })
            {
                var order = CreatePaperOrder(
                    strategyId,
                    run.ConditionId,
                    oldUtc.AddMinutes(1));
                await using var connection = factory.CreateConnection();
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync(isolationLevel);
                var exception = await Assert.ThrowsAsync<PostgresException>(
                    async () => await InsertPaperOrderAsync(connection, transaction, order));
                Assert.Equal("0A000", exception.SqlState);
                Assert.Contains("requires READ COMMITTED", exception.MessageText);
                await transaction.RollbackAsync();

                Assert.Equal(0, await ReadPaperOrderCountAsync(factory, order.Id));
                Assert.Equal(archived, await ReadRetentionCountsAsync(factory, strategyId));
                Assert.Empty(await ReadRunIdsAsync(factory, [run.Id]));
                Assert.Equal([run.Id], await ReadArchivedRunIdsAsync(factory, strategyId));
            }
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task CandidateFirstEligibility_FullyBlockedFirstPageAdvancesToEligibleTailAndReportsDuration()
    {
        var factory = await CreateFactoryAsync();

        const int pageSize = 500;
        const int eligibleCount = 500;
        const int blockedCount = 500;
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-benchmark-{Guid.NewGuid():N}";
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var priorIntrinsicCursor = await ReadNewestIntrinsicRunCursorAsync(factory, cutoffUtc);
        var firstUpdatedAtUtc = priorIntrinsicCursor?.UpdatedAtUtc.AddMilliseconds(1)
            ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var lastUpdatedAtUtc = firstUpdatedAtUtc.AddTicks(
            (eligibleCount + blockedCount - 1L) * TimeSpan.TicksPerMicrosecond);
        Assert.True(
            lastUpdatedAtUtc < cutoffUtc,
            $"The retention paging fixture needs an intrinsic timestamp gap before {cutoffUtc:O}, " +
            $"but the newest existing cursor is {priorIntrinsicCursor?.UpdatedAtUtc:O}.");
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var runs = Enumerable.Range(0, eligibleCount + blockedCount)
            .Select(index => WithRetentionKey(
                CreateSkippedRun(
                    strategyId,
                    firstUpdatedAtUtc.AddTicks(index * TimeSpan.TicksPerMicrosecond)),
                $"{prefix}-{index:D4}"))
            .ToArray();
        var blockedRuns = runs.Take(blockedCount).ToArray();
        var eligibleRuns = runs.Skip(blockedCount).ToArray();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertPaperOrderDependenciesAsync(
                factory,
                strategyId,
                firstUpdatedAtUtc,
                blockedRuns.Select(run => run.ConditionId).ToArray());
            Assert.Equal(
                runs.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(runs)).Count);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var summaryTimer = Stopwatch.StartNew();
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 10);
            summaryTimer.Stop();
            var previewTimer = Stopwatch.StartNew();
            var firstPage = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                cutoffUtc,
                pageSize,
                priorIntrinsicCursor);
            var firstPageCursor = Assert.IsType<StrategyRunRetentionCursor>(
                firstPage.ContinuationCursor);
            var secondPage = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                cutoffUtc,
                pageSize,
                firstPageCursor);
            previewTimer.Stop();

            Assert.Empty(firstPage.CandidateRunIds);
            Assert.Equal(0, firstPage.DistinctStrategies);
            Assert.Null(firstPage.OldestUpdatedAtUtc);
            Assert.Null(firstPage.NewestUpdatedAtUtc);
            Assert.Equal(pageSize, firstPage.IntrinsicRowsScanned);
            Assert.False(firstPage.ReachedIntrinsicEnd);
            Assert.Equal(
                new StrategyRunRetentionCursor(
                    blockedRuns[^1].UpdatedAtUtc,
                    blockedRuns[^1].Id),
                firstPageCursor);

            Assert.Equal(
                eligibleRuns.Select(run => run.Id),
                secondPage.CandidateRunIds);
            Assert.Equal(1, secondPage.DistinctStrategies);
            Assert.Equal(eligibleRuns[0].UpdatedAtUtc, secondPage.OldestUpdatedAtUtc);
            Assert.Equal(eligibleRuns[^1].UpdatedAtUtc, secondPage.NewestUpdatedAtUtc);
            Assert.Equal(pageSize, secondPage.IntrinsicRowsScanned);
            Assert.True(secondPage.ReachedIntrinsicEnd);
            Assert.Equal(
                new StrategyRunRetentionCursor(
                    eligibleRuns[^1].UpdatedAtUtc,
                    eligibleRuns[^1].Id),
                secondPage.ContinuationCursor);
            Assert.Equal(baseline.TotalCandidateRows + eligibleCount, summary.TotalCandidateRows);
            Console.WriteLine(
                $"Set-based retention benchmark: rows={runs.Length}, eligible={eligibleCount}, " +
                $"paperOrderBlocked={blockedCount}, summaryMs={summaryTimer.Elapsed.TotalMilliseconds:F2}, " +
                $"pagedPreviewMs={previewTimer.Elapsed.TotalMilliseconds:F2}");
        }
        finally
        {
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static async Task<PostgresConnectionFactory> CreateFactoryAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION disappeared after test discovery.");
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static async Task InsertArchivedPaperSkipAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Guid runId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        var marketId = $"retention-archive-{runId:N}";
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
WITH archive_values AS (
    SELECT
        date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
            AS bucket_start_utc
), rollup AS (
    INSERT INTO strategy_paper_skip_rollups (
        strategy_id, bucket_start_utc, skip_reason, run_count,
        first_updated_at_utc, last_updated_at_utc, created_at_utc, updated_at_utc)
    SELECT
        @StrategyId, archive_values.bucket_start_utc, @SkipReason, 1,
        @UpdatedAtUtc, @UpdatedAtUtc, clock_timestamp(), clock_timestamp()
    FROM archive_values
    RETURNING bucket_start_utc
)
INSERT INTO strategy_market_paper_skip_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc, archive_format_version,
    condition_id, market_slug, market_title, category, market_start_utc, market_end_utc,
    detected_at_utc, entry_due_at_utc, selected_asset_id, selected_outcome, stake_usd,
    skip_reason, run_created_at_utc, run_updated_at_utc, rollup_bucket_start_utc)
SELECT
    @StrategyId, @MarketId, @RunId, clock_timestamp(), 1,
    @ConditionId, @MarketId, 'Dashboard compact skip integration test', 'Test',
    @UpdatedAtUtc - interval '5 minutes', @UpdatedAtUtc,
    @UpdatedAtUtc - interval '10 minutes', @UpdatedAtUtc - interval '5 minutes',
    NULL, NULL, 6.00,
    @SkipReason, @UpdatedAtUtc - interval '10 minutes', @UpdatedAtUtc,
    rollup.bucket_start_utc
FROM rollup;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("MarketId", marketId);
        command.Parameters.AddWithValue("ConditionId", $"condition-{marketId}");
        command.Parameters.AddWithValue("SkipReason", skipReason);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<int> ReadRecentStrategyRunFactCountAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT count(*)::integer
FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind
  AND source_id = @RunId;
""",
            connection);
        command.Parameters.AddWithValue("SourceKind", DashboardProjectionSourceKinds.StrategyRun);
        command.Parameters.AddWithValue("RunId", runId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static StrategyMarketPaperRun CreateSkippedRun(Guid strategyId, DateTimeOffset updatedAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            $"retention-market-{suffix}",
            $"retention-condition-{suffix}",
            $"retention-market-{suffix}",
            "Strategy run retention integration test",
            "Test",
            updatedAtUtc.AddMinutes(-5),
            updatedAtUtc,
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Skipped,
            null,
            null,
            null,
            1m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "retention_test_skip",
            updatedAtUtc.AddMinutes(-10),
            updatedAtUtc);
    }

    private static StrategyMarketPaperRun WithRetentionKey(
        StrategyMarketPaperRun run,
        string key)
    {
        return run with
        {
            MarketId = key,
            ConditionId = key,
            MarketSlug = key,
            MarketTitle = key
        };
    }

    private static LiveOrder CreateLiveOrder(
        Guid strategyId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Submitted,
            null,
            TradeSide.Buy,
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            0.50m,
            2m,
            1m,
            "FAK",
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            createdAtUtc.AddSeconds(1),
            "submitted",
            0m,
            2m,
            string.Empty,
            "{}",
            "retention integration test",
            createdAtUtc.AddSeconds(1),
            StrategyId: strategyId,
            ExecutionSource: "retention_integration_test",
            PostOnly: false);
    }

    private static PaperLiveShadowDecision CreateShadowDecision(
        Guid strategyId,
        string marketId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new PaperLiveShadowDecision(
            Guid.NewGuid(),
            strategyId,
            marketId,
            conditionId,
            $"asset-{Guid.NewGuid():N}",
            "Yes",
            TradeSide.Buy,
            0.50m,
            1m,
            2m,
            1m,
            "FAK",
            false,
            "{}",
            0,
            "retention_integration_test",
            createdAtUtc,
            createdAtUtc,
            createdAtUtc.AddMinutes(-5),
            createdAtUtc.AddMinutes(5),
            createdAtUtc.AddSeconds(10),
            createdAtUtc.AddMinutes(5),
            Status: "decision_created",
            UpdatedAtUtc: createdAtUtc);
    }

    private static async Task<Guid[]> ReadLegacyEligibleRunIdsAsync(
        PostgresConnectionFactory factory,
        DateTimeOffset updatedBeforeUtc,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.id
FROM strategy_market_paper_runs run
WHERE run.id = ANY(@RunIds)
  AND run.status = 'Skipped'
  AND run.retention_scope = 'PaperOnly'
  AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND run.market_end_utc IS NOT NULL
  AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND cardinality(public.strategy_market_paper_run_retention_blockers(run)) = 0
ORDER BY run.updated_at_utc, run.id;
""",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<StrategyRunRetentionCursor?> ReadNewestIntrinsicRunCursorAsync(
        PostgresConnectionFactory factory,
        DateTimeOffset updatedBeforeUtc)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.updated_at_utc, run.id
FROM public.strategy_market_paper_runs run
WHERE run.status = 'Skipped'
  AND run.retention_scope = 'PaperOnly'
  AND run.updated_at_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND run.market_end_utc IS NOT NULL
  AND run.market_end_utc < LEAST(@UpdatedBeforeUtc, clock_timestamp() - interval '48 hours')
  AND NULLIF(btrim(COALESCE(run.skip_reason, '')), '') IS NOT NULL
  AND run.signal_id IS NULL
  AND run.paper_order_id IS NULL
  AND run.entered_at_utc IS NULL
  AND run.entry_price IS NULL
  AND run.size_shares IS NULL
  AND run.settlement_price IS NULL
  AND run.settlement_value_usd IS NULL
  AND run.realized_pnl_usd IS NULL
  AND run.settled_at_utc IS NULL
  AND run.skip_diagnostics_json IS NULL
ORDER BY run.updated_at_utc DESC, run.id DESC
LIMIT 1;
""",
            connection);
        command.Parameters.AddWithValue("UpdatedBeforeUtc", updatedBeforeUtc.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StrategyRunRetentionCursor(
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)),
            reader.GetGuid(1));
    }

    private static async Task<IReadOnlyDictionary<Guid, string[]>> ReadLegacyBlockersAsync(
        PostgresConnectionFactory factory,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run.id, public.strategy_market_paper_run_retention_blockers(run)
FROM public.strategy_market_paper_runs run
WHERE run.id = ANY(@RunIds);
""",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        var results = new Dictionary<Guid, string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0), reader.GetFieldValue<string[]>(1));
        }

        return results;
    }

    private static async Task InsertDirectExternalDependenciesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset oldUtc,
        StrategyMarketPaperRun dryRun,
        StrategyMarketPaperRun copiedLeaderPositionRun,
        StrategyMarketPaperRun copiedLeaderActivityRun,
        StrategyMarketPaperRun onchainRun)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.dry_run_orders (
    id, signal_id, strategy_id, status, side, asset_id, condition_id,
    outcome, price, size_shares, notional_usd, order_type,
    payload_json, validation_summary, created_at_utc)
VALUES (
    @DryOrderId, @DrySignalId, @StrategyId, 'Validated', 'Buy', @DryAssetId, @DryConditionId,
    'Yes', 0.50, 2, 1, 'FAK',
    '{}'::jsonb, 'retention fixture', @OldUtc);

INSERT INTO public.paper_copied_leader_positions (
    id, entry_signal_id, entry_paper_order_id,
    copied_trader_wallet, asset_id, condition_id, outcome,
    entry_timestamp_utc, leader_entry_price,
    leader_initial_size_shares, status,
    next_activity_sync_at_utc, created_at_utc, updated_at_utc)
VALUES (
    @CopiedPositionId, @CopiedPositionSignalId, @CopiedPositionOrderId,
    @CopiedPositionWallet, @CopiedPositionAssetId, @CopiedPositionConditionId, 'Yes',
    @OldUtc, 0.50,
    2, 'Active',
    @OldUtc, @OldUtc, @OldUtc);

INSERT INTO public.paper_copied_leader_activity_events (
    id, dedup_key, copied_trader_wallet, asset_id, condition_id,
    side, price, size_shares, usdc_size,
    activity_timestamp_utc, raw_json, observed_at_utc)
VALUES (
    @ActivityId, @ActivityDedupKey, @ActivityWallet, @ActivityAssetId, @ActivityConditionId,
    'Buy', 0.50, 2, 1,
    @OldUtc, '{}'::jsonb, @OldUtc);

INSERT INTO public.polymarket_onchain_paper_signal_results (
    id, capture_id, transaction_hash, log_index, participant_role,
    copied_trader_wallet, counterparty_wallet, side, token_id,
    condition_id, market_slug, outcome,
    status, decision_code, reason_details, processed_at_utc)
VALUES (
    @OnchainId, @OnchainCaptureId, @OnchainTransactionHash, 0, 'maker',
    @OnchainWallet, @OnchainCounterparty, 'Buy', @OnchainAssetId,
    @OnchainConditionId, @OnchainMarketSlug, 'Yes',
    'Skipped', 'retention_fixture', 'retention fixture', @OldUtc);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("OldUtc", oldUtc.UtcDateTime);
        command.Parameters.AddWithValue("DryOrderId", Guid.NewGuid());
        command.Parameters.AddWithValue("DrySignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("DryAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("DryConditionId", dryRun.ConditionId);
        command.Parameters.AddWithValue("CopiedPositionId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionSignalId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionOrderId", Guid.NewGuid());
        command.Parameters.AddWithValue("CopiedPositionWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CopiedPositionAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("CopiedPositionConditionId", copiedLeaderPositionRun.ConditionId);
        command.Parameters.AddWithValue("ActivityId", Guid.NewGuid());
        command.Parameters.AddWithValue("ActivityDedupKey", $"retention-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("ActivityConditionId", copiedLeaderActivityRun.ConditionId);
        command.Parameters.AddWithValue("OnchainId", Guid.NewGuid());
        command.Parameters.AddWithValue("OnchainCaptureId", Guid.NewGuid());
        command.Parameters.AddWithValue("OnchainTransactionHash", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainWallet", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainCounterparty", $"0x{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainAssetId", $"asset-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("OnchainConditionId", onchainRun.ConditionId);
        command.Parameters.AddWithValue("OnchainMarketSlug", onchainRun.MarketSlug);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertPostRunExternalBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        Guid queueStrategyId,
        DateTimeOffset oldUtc,
        StrategyMarketPaperRun tombstoneRun,
        StrategyMarketPaperRun projectionEventRun,
        StrategyMarketPaperRun recentFactRun)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategy_market_paper_skip_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc)
VALUES (@StrategyId, @TombstoneMarketId, @TombstoneRunId, @OldUtc);

INSERT INTO public.dashboard_projection_events (
    source_kind, source_id, strategy_id, operation,
    old_payload, new_payload, transaction_id)
VALUES (
    'StrategyRun', @ProjectionEventRunId, @StrategyId, 'Update',
    NULL, NULL, pg_current_xact_id());

INSERT INTO public.dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id,
    occurred_at_utc, contribution_json,
    applied_1h, applied_6h, applied_24h, updated_at_utc)
VALUES (
    'StrategyRun', @RecentFactRunId, 'RetentionFixture', @StrategyId,
    @OldUtc, '{}'::jsonb,
    false, false, false, @OldUtc);

INSERT INTO public.dashboard_projection_reconciliation_queue (
    strategy_id, priority, reason, requested_at_utc,
    attempt_count, next_attempt_at_utc, last_error)
VALUES (
    @QueueStrategyId, 0, 'retention_fixture', @OldUtc,
    0, @OldUtc, NULL);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("QueueStrategyId", queueStrategyId);
        command.Parameters.AddWithValue("OldUtc", oldUtc.UtcDateTime);
        command.Parameters.AddWithValue("TombstoneMarketId", tombstoneRun.MarketId);
        command.Parameters.AddWithValue("TombstoneRunId", tombstoneRun.Id);
        command.Parameters.AddWithValue("ProjectionEventRunId", projectionEventRun.Id);
        command.Parameters.AddWithValue("RecentFactRunId", recentFactRun.Id);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task CreateShadowPaperOrdersSchemaAsync(
        PostgresConnectionFactory factory,
        string schemaName)
    {
        var quotedSchemaName = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
CREATE SCHEMA {quotedSchemaName};
CREATE TABLE {quotedSchemaName}.paper_orders (
    strategy_id uuid NOT NULL,
    condition_id text NOT NULL);
""",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropShadowSchemaAsync(
        PostgresConnectionFactory factory,
        string schemaName)
    {
        var quotedSchemaName = new NpgsqlCommandBuilder().QuoteIdentifier(schemaName);
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS {quotedSchemaName} CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid[]> ReadRunIdsAsync(
        PostgresConnectionFactory factory,
        Guid[] runIds)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM strategy_market_paper_runs WHERE id = ANY(@RunIds);",
            connection);
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<string> ReadRunPayloadWithoutScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (to_jsonb(run) - 'retention_scope')::text " +
            "FROM public.strategy_market_paper_runs run WHERE run.id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run payload was not found."));
    }

    private static PostgresConnectionFactory WithApplicationName(
        PostgresConnectionFactory factory,
        string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString)
        {
            ApplicationName = applicationName
        };
        return new PostgresConnectionFactory(new StorageOptions
        {
            ConnectionString = builder.ConnectionString
        });
    }

    private static async Task AssertStrategyCodePositionRaceAsync(bool codeUpdateFirst)
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var oldCode = $"retention_mapping_old_{Guid.NewGuid():N}";
        var newCode = $"retention_mapping_new_{Guid.NewGuid():N}";
        var conditionId = $"retention-mapping-race-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var run = WithRetentionKey(CreateSkippedRun(strategyId, oldUtc), conditionId);
        var positionId = Guid.NewGuid();
        var position = new PaperPosition(
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            2m,
            0.50m,
            1m,
            0m,
            oldUtc,
            $"strategy:{newCode}");
        var ownerApplicationName = $"mapping_owner_{Guid.NewGuid():N}";
        var blockedApplicationName = $"mapping_blocked_{Guid.NewGuid():N}";
        var ownerFactory = WithApplicationName(factory, ownerApplicationName);
        var blockedFactory = WithApplicationName(factory, blockedApplicationName);
        using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<int>? blockedTask = null;

        await InsertStrategyAsync(factory, strategyId, oldCode, liveStakes: false);
        try
        {
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            var originalPayload = await ReadRunPayloadWithoutScopeAsync(factory, run.Id);
            await DeleteProjectionBlockersAsync(factory, strategyId);
            Assert.Equal(1, (await repository.TransferPaperOnlySkippedRunsToRollupsAsync(
                [run.Id],
                DateTimeOffset.UtcNow.AddHours(-48))).DeletedRows);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            if (codeUpdateFirst)
            {
                await using var ownerConnection = ownerFactory.CreateConnection();
                await ownerConnection.OpenAsync();
                await using var ownerTransaction = await ownerConnection.BeginTransactionAsync();
                var ownerCommitted = false;
                try
                {
                    Assert.Equal(1, await UpdateStrategyCodeAsync(
                        ownerConnection,
                        ownerTransaction,
                        strategyId,
                        newCode));
                    blockedTask = InsertPaperPositionAsync(
                        blockedFactory,
                        positionId,
                        position,
                        raceCancellation.Token);
                    var blockedPid = await WaitForBlockedApplicationAsync(
                        factory,
                        blockedApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, blockedPid, ownerConnection.ProcessID);

                    await ownerTransaction.CommitAsync();
                    ownerCommitted = true;
                    Assert.Equal(1, await blockedTask.WaitAsync(TimeSpan.FromSeconds(15)));
                }
                finally
                {
                    if (!ownerCommitted)
                    {
                        await ownerTransaction.RollbackAsync(CancellationToken.None);
                    }

                    raceCancellation.Cancel();
                    await DrainRaceTaskAsync(blockedTask);
                }
            }
            else
            {
                await using var ownerConnection = ownerFactory.CreateConnection();
                await ownerConnection.OpenAsync();
                await using var ownerTransaction = await ownerConnection.BeginTransactionAsync();
                var ownerCommitted = false;
                try
                {
                    Assert.Equal(1, await InsertPaperPositionAsync(
                        ownerConnection,
                        ownerTransaction,
                        positionId,
                        position,
                        CancellationToken.None));
                    blockedTask = UpdateStrategyCodeAsync(
                        blockedFactory,
                        strategyId,
                        newCode,
                        raceCancellation.Token);
                    var blockedPid = await WaitForBlockedApplicationAsync(
                        factory,
                        blockedApplicationName,
                        "advisory");
                    await AssertBlockedByAsync(factory, blockedPid, ownerConnection.ProcessID);

                    await ownerTransaction.CommitAsync();
                    ownerCommitted = true;
                    Assert.Equal(1, await blockedTask.WaitAsync(TimeSpan.FromSeconds(15)));
                }
                finally
                {
                    if (!ownerCommitted)
                    {
                        await ownerTransaction.RollbackAsync(CancellationToken.None);
                    }

                    raceCancellation.Cancel();
                    await DrainRaceTaskAsync(blockedTask);
                }
            }

            Assert.Equal(originalPayload, await ReadRunPayloadWithoutScopeAsync(factory, run.Id));
            Assert.Equal(StrategyRunRetentionScopes.PaperOnly,
                await ReadRetentionScopeAsync(factory, run.Id));
            var counts = await ReadRetentionCountsAsync(factory, strategyId);
            Assert.Equal((1L, 0L, 0L, 1L),
                (counts.RawRuns, counts.RollupRuns,
                    counts.Tombstones, counts.ReconciliationQueueRows));
            Assert.Equal(0, await ReadStrategyRunProjectionEventCountAsync(factory, run.Id));
            Assert.Contains("paper_position_dependency", await ReadRunBlockersAsync(factory, run.Id));
        }
        finally
        {
            raceCancellation.Cancel();
            await DrainRaceTaskAsync(blockedTask);
            await DeleteConditionDependenciesAsync(factory, conditionId);
            await DeleteTestStrategyAsync(factory, strategyId);
        }
    }

    private static async Task<int> UpdateStrategyCodeAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await UpdateStrategyCodeAsync(
            connection,
            null,
            strategyId,
            code,
            cancellationToken);
    }

    private static async Task<int> UpdateStrategyCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid strategyId,
        string code,
        CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE public.strategies SET code = @Code, updated_at_utc = clock_timestamp() " +
            "WHERE id = @StrategyId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("Code", code);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertPaperPositionAsync(
        PostgresConnectionFactory factory,
        Guid positionId,
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await InsertPaperPositionAsync(
            connection,
            null,
            positionId,
            position,
            cancellationToken);
    }

    private static async Task<int> InsertPaperPositionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid positionId,
        PaperPosition position,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome,
    size_shares, average_price, estimated_value_usd,
    unrealized_pnl_usd, updated_at_utc)
VALUES (
    @Id, @CopiedTraderWallet, @AssetId, @ConditionId, @Outcome,
    @SizeShares, @AveragePrice, @EstimatedValueUsd,
    @UnrealizedPnlUsd, @UpdatedAtUtc);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Id", positionId);
        command.Parameters.AddWithValue("CopiedTraderWallet", position.CopiedTraderWallet);
        command.Parameters.AddWithValue("AssetId", position.AssetId);
        command.Parameters.AddWithValue("ConditionId", position.ConditionId);
        command.Parameters.AddWithValue("Outcome", position.Outcome);
        command.Parameters.AddWithValue("SizeShares", position.SizeShares);
        command.Parameters.AddWithValue("AveragePrice", position.AveragePrice);
        command.Parameters.AddWithValue("EstimatedValueUsd", position.EstimatedValueUsd);
        command.Parameters.AddWithValue("UnrealizedPnlUsd", position.UnrealizedPnlUsd);
        command.Parameters.AddWithValue("UpdatedAtUtc", position.UpdatedAtUtc.UtcDateTime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockRollupGroupAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT run_count
FROM public.strategy_paper_skip_rollups
WHERE strategy_id = @StrategyId
  AND bucket_start_utc =
      date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND skip_reason = @SkipReason
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SkipReason", skipReason);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static async Task<int> WaitForBlockedApplicationAsync(
        PostgresConnectionFactory factory,
        string applicationName,
        string waitEvent)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            await using var connection = factory.CreateConnection();
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
SELECT pid
FROM pg_stat_activity
WHERE application_name = @ApplicationName
  AND state = 'active'
  AND wait_event_type = 'Lock'
  AND lower(COALESCE(wait_event, '')) = lower(@WaitEvent)
LIMIT 1;
""",
                connection);
            command.Parameters.AddWithValue("ApplicationName", applicationName);
            command.Parameters.AddWithValue("WaitEvent", waitEvent);
            var result = await command.ExecuteScalarAsync();
            if (result is int pid)
            {
                return pid;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException(
            $"PostgreSQL application {applicationName} did not wait on {waitEvent} within 10 seconds.");
    }

    private static async Task AssertBlockedByAsync(
        PostgresConnectionFactory factory,
        int blockedPid,
        int blockerPid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT @BlockerPid = ANY(pg_blocking_pids(@BlockedPid));",
            connection);
        command.Parameters.AddWithValue("BlockedPid", blockedPid);
        command.Parameters.AddWithValue("BlockerPid", blockerPid);
        Assert.True((bool)(await command.ExecuteScalarAsync() ?? false));
    }

    private static async Task DrainRaceTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException)
        {
        }
        catch (PostgresException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private static async Task<bool> HoldsExclusiveRetentionGateAsync(
        PostgresConnectionFactory factory,
        int pid)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1
    FROM pg_locks
    WHERE pid = @Pid
      AND locktype = 'advisory'
      AND classid = 1346589778
      AND objid = 1
      AND mode = 'ExclusiveLock'
      AND granted);
""",
            connection);
        command.Parameters.AddWithValue("Pid", pid);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task AssertRunRowIsLockedAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT id FROM public.strategy_market_paper_runs " +
            "WHERE id = @RunId FOR UPDATE NOWAIT;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        await transaction.RollbackAsync();
    }

    private static async Task<RollupGroup> ReadRollupGroupAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset updatedAtUtc,
        string skipReason)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT run_count, first_updated_at_utc, last_updated_at_utc
FROM public.strategy_paper_skip_rollups
WHERE strategy_id = @StrategyId
  AND bucket_start_utc =
      date_trunc('day', @UpdatedAtUtc::timestamptz AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND skip_reason = @SkipReason;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("UpdatedAtUtc", updatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("SkipReason", skipReason);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RollupGroup(
            reader.GetInt32(0),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)));
    }

    private static async Task<Guid[]> ReadArchivedRunIdsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT archived_run_id FROM public.strategy_market_paper_skip_tombstones " +
            "WHERE strategy_id = @StrategyId ORDER BY archived_run_id;",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var results = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetGuid(0));
        }

        return results.ToArray();
    }

    private static async Task<string[]> ReadRunBlockersAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT public.strategy_market_paper_run_retention_blockers(run) " +
            "FROM public.strategy_market_paper_runs run WHERE run.id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (string[])(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run blockers were not found."));
    }

    private static async Task<long> ReadStrategyRunProjectionEventCountAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.dashboard_projection_events " +
            "WHERE source_kind = 'StrategyRun' AND source_id = @RunId;",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<int> InsertPaperOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        PaperOrder order)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, cancelled_at_utc,
    raw_decision_json, correlation_id, execution_source)
VALUES (
    @Id, @SignalId, @StrategyId, @CopiedTraderWallet, @Status, @Side,
    @AssetId, @ConditionId, @Outcome, @Price, @SizeShares, @NotionalUsd,
    @CreatedAtUtc, @ExpiresAtUtc, @FilledAtUtc, @CancelledAtUtc,
    CAST(@RawDecisionJson AS jsonb), @CorrelationId, @ExecutionSource)
ON CONFLICT (id) DO NOTHING;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("Id", order.Id);
        command.Parameters.AddWithValue("SignalId", order.SignalId);
        command.Parameters.AddWithValue("StrategyId", order.StrategyId);
        command.Parameters.AddWithValue("CopiedTraderWallet", order.CopiedTraderWallet);
        command.Parameters.AddWithValue("Status", order.Status.ToString());
        command.Parameters.AddWithValue("Side", order.Side.ToString());
        command.Parameters.AddWithValue("AssetId", order.AssetId);
        command.Parameters.AddWithValue("ConditionId", order.ConditionId);
        command.Parameters.AddWithValue("Outcome", order.Outcome);
        command.Parameters.AddWithValue("Price", order.Price);
        command.Parameters.AddWithValue("SizeShares", order.SizeShares);
        command.Parameters.AddWithValue("NotionalUsd", order.NotionalUsd);
        command.Parameters.AddWithValue("CreatedAtUtc", order.CreatedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("ExpiresAtUtc", order.ExpiresAtUtc.UtcDateTime);
        command.Parameters.Add("FilledAtUtc", NpgsqlDbType.TimestampTz).Value =
            order.FilledAtUtc is null ? DBNull.Value : order.FilledAtUtc.Value.UtcDateTime;
        command.Parameters.Add("CancelledAtUtc", NpgsqlDbType.TimestampTz).Value =
            order.CancelledAtUtc is null ? DBNull.Value : order.CancelledAtUtc.Value.UtcDateTime;
        command.Parameters.AddWithValue("RawDecisionJson", order.RawDecisionJson ?? "{}");
        command.Parameters.Add("CorrelationId", NpgsqlDbType.Uuid).Value =
            order.CorrelationId is null ? DBNull.Value : order.CorrelationId.Value;
        command.Parameters.AddWithValue("ExecutionSource", order.ExecutionSource);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertConflictingPaperOrderAsync(
        PostgresConnectionFactory factory,
        PaperOrder existingOrder,
        string conflictingConditionId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        return await InsertPaperOrderAsync(
            connection,
            null,
            existingOrder with { ConditionId = conflictingConditionId });
    }

    private static async Task<long> ReadRunCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.strategy_market_paper_runs WHERE id = @RunId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> ReadTombstoneCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.strategy_market_paper_skip_tombstones " +
            "WHERE archived_run_id = @RunId;",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<long> ReadPaperOrderCountAsync(
        PostgresConnectionFactory factory,
        Guid orderId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM public.paper_orders WHERE id = @OrderId;",
            connection);
        command.Parameters.AddWithValue("OrderId", orderId);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<PaperHistoryCounts> ReadPaperHistoryCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM paper_orders WHERE strategy_id = @StrategyId),
    (SELECT count(*)
     FROM paper_fills fill_row
     INNER JOIN paper_orders order_row ON order_row.id = fill_row.paper_order_id
     WHERE order_row.strategy_id = @StrategyId),
    (SELECT count(*)
     FROM paper_positions position_row
     INNER JOIN strategies strategy
         ON lower(position_row.copied_trader_wallet) = lower('strategy:' || strategy.code)
     WHERE strategy.id = @StrategyId),
    (SELECT count(*)
     FROM paper_position_settlements settlement
     INNER JOIN strategies strategy
         ON lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy.code)
     WHERE strategy.id = @StrategyId);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PaperHistoryCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3));
    }

    private static async Task InsertPaperOrderDependenciesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        DateTimeOffset createdAtUtc,
        string[] conditionIds)
    {
        if (conditionIds.Length == 0)
        {
            return;
        }

        var orderIds = conditionIds.Select(_ => Guid.NewGuid()).ToArray();
        var signalIds = conditionIds.Select(_ => Guid.NewGuid()).ToArray();
        var assetIds = conditionIds.Select(_ => $"asset-{Guid.NewGuid():N}").ToArray();
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, execution_source)
SELECT
    dependency.id,
    dependency.signal_id,
    @StrategyId,
    @Wallet,
    'Filled',
    'Buy',
    dependency.asset_id,
    dependency.condition_id,
    'Yes',
    0.50,
    2,
    1,
    @CreatedAtUtc,
    @CreatedAtUtc + interval '5 minutes',
    @CreatedAtUtc + interval '1 minute',
    'retention_integration_benchmark'
FROM unnest(@OrderIds, @SignalIds, @AssetIds, @ConditionIds)
    AS dependency(id, signal_id, asset_id, condition_id);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Wallet", $"strategy-retention-{strategyId:N}");
        command.Parameters.AddWithValue("CreatedAtUtc", createdAtUtc.UtcDateTime);
        command.Parameters.Add("OrderIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = orderIds;
        command.Parameters.Add("SignalIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = signalIds;
        command.Parameters.Add("AssetIds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = assetIds;
        command.Parameters.Add("ConditionIds", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = conditionIds;
        Assert.Equal(conditionIds.Length, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteConditionDependenciesAsync(
        PostgresConnectionFactory factory,
        string conditionPrefix)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM public.paper_copied_leader_activity_events WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_copied_leader_positions WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.polymarket_onchain_paper_signal_results WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_positions WHERE condition_id LIKE @ConditionPattern;
DELETE FROM public.paper_position_settlements WHERE condition_id LIKE @ConditionPattern;
""",
            connection);
        command.Parameters.AddWithValue("ConditionPattern", $"{conditionPrefix}%");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        string strategyCode,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes,
    live_enabled_at_utc, created_at_utc, updated_at_utc)
VALUES (
    @Id, @Code, @Name, 'retention integration test', true, @LiveStakes,
    CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    clock_timestamp(), clock_timestamp());
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Code", strategyCode);
        command.Parameters.AddWithValue("Name", strategyCode);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetStrategyLiveStakesAsync(
        PostgresConnectionFactory factory,
        Guid strategyId,
        bool liveStakes)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE strategies
SET live_stakes = @LiveStakes,
    live_enabled_at_utc = CASE WHEN @LiveStakes THEN clock_timestamp() ELSE NULL END,
    updated_at_utc = clock_timestamp()
WHERE id = @Id;
""",
            connection);
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("LiveStakes", liveStakes);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task TryDemoteRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET retention_scope = 'PaperOnly' WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ReadRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT retention_scope FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Strategy run was not found."));
    }

    private static async Task<string?> ReadRunStatusAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT status FROM strategy_market_paper_runs WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task AddSkipDiagnosticsAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE strategy_market_paper_runs SET skip_diagnostics_json = '{}'::jsonb WHERE id = @Id;",
            connection);
        command.Parameters.AddWithValue("Id", runId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeletePaperPositionAsync(
        PostgresConnectionFactory factory,
        string copiedTraderWallet,
        string assetId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM paper_positions " +
            "WHERE copied_trader_wallet = @CopiedTraderWallet AND asset_id = @AssetId;",
            connection);
        command.Parameters.AddWithValue("CopiedTraderWallet", copiedTraderWallet);
        command.Parameters.AddWithValue("AssetId", assetId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteProjectionBlockersAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_events WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;",
            strategyId);
    }

    private static async Task<RetentionCounts> ReadRetentionCountsAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT
    (SELECT count(*) FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId),
    (SELECT COALESCE(sum(run_count), 0) FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_events WHERE strategy_id = @StrategyId),
    (SELECT count(*) FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId);
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RetentionCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static async Task<StrategyPerformance> ReadDashboardSnapshotAsync(
        PostgresDashboardSnapshotRepository snapshots,
        Guid strategyId)
    {
        return (await snapshots.GetStrategyPerformanceSnapshotAsync())
            .Single(row => row.StrategyId == strategyId);
    }

    private static void AssertStrategyLifetimeMetricsEqual(
        StrategyPerformance expected,
        StrategyPerformance actual)
    {
        Assert.Equal(expected.OrdersCount, actual.OrdersCount);
        Assert.Equal(expected.FilledOrdersCount, actual.FilledOrdersCount);
        Assert.Equal(expected.OpenOrdersCount, actual.OpenOrdersCount);
        Assert.Equal(expected.OpenPositionsCount, actual.OpenPositionsCount);
        Assert.Equal(expected.ObservedRunsCount, actual.ObservedRunsCount);
        Assert.Equal(expected.EnteredRunsCount, actual.EnteredRunsCount);
        Assert.Equal(expected.SkippedRunsCount, actual.SkippedRunsCount);
        Assert.Equal(expected.PaperConditionSkippedRunsCount, actual.PaperConditionSkippedRunsCount);
        Assert.Equal(expected.PaperNotAcceptedRunsCount, actual.PaperNotAcceptedRunsCount);
        Assert.Equal(expected.SettledRunsCount, actual.SettledRunsCount);
        Assert.Equal(expected.StakeUsd, actual.StakeUsd);
        Assert.Equal(expected.RealizedPnlUsd, actual.RealizedPnlUsd);
        Assert.Equal(expected.UnrealizedPnlUsd, actual.UnrealizedPnlUsd);
        Assert.Equal(expected.TotalPnlUsd, actual.TotalPnlUsd);
        Assert.Equal(expected.LastOrderUtc, actual.LastOrderUtc);
        Assert.Equal(expected.LastRunUtc, actual.LastRunUtc);
    }

    private static async Task DeleteTestStrategyAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM paper_live_shadow_decisions WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM live_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dry_run_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_fills fill_row
USING paper_orders order_row
WHERE fill_row.paper_order_id = order_row.id
  AND order_row.strategy_id = @StrategyId;
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_runs WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM paper_orders WHERE strategy_id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_paper_skip_rollups WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_market_paper_skip_tombstones WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategy_live_retention_guards WHERE strategy_id = @StrategyId;",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance_refresh_queue
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance_refresh_inflight
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            """
DELETE FROM paper_copied_trader_performance
WHERE lower(copied_trader_wallet) IN (
    lower('strategy-retention-' || replace(@StrategyId::text, '-', '')),
    lower(COALESCE((SELECT 'strategy:' || code FROM strategies WHERE id = @StrategyId), '')));
""",
            strategyId);
        await ExecuteForStrategyAsync(
            factory,
            "DELETE FROM strategies WHERE id = @StrategyId;",
            strategyId);
        await DeleteProjectionBlockersAsync(factory, strategyId);
    }

    private static PaperOrder CreatePaperOrder(
        Guid strategyId,
        string conditionId,
        DateTimeOffset createdAtUtc)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"strategy-retention-{strategyId:N}",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            $"asset-{Guid.NewGuid():N}",
            conditionId,
            "Yes",
            0.50m,
            2m,
            1m,
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            FilledAtUtc: createdAtUtc.AddMinutes(1),
            StrategyId: strategyId,
            ExecutionSource: "retention_integration_test");
    }

    private static async Task ExecuteForStrategyAsync(
        PostgresConnectionFactory factory,
        string sql,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record RetentionCounts(
        long RawRuns,
        long RollupRuns,
        long Tombstones,
        long ProjectionEvents,
        long ReconciliationQueueRows);

    private sealed record PaperHistoryCounts(
        long Orders,
        long Fills,
        long Positions,
        long Settlements);

    private sealed record RollupGroup(
        int RunCount,
        DateTimeOffset FirstUpdatedAtUtc,
        DateTimeOffset LastUpdatedAtUtc);
}
