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
    public async Task SetBasedEligibility_NonZeroBatchReturnsExactCountsAndReportsDuration()
    {
        var factory = await CreateFactoryAsync();

        const int eligibleCount = 500;
        const int blockedCount = 500;
        var repository = new PostgresAppRepository(factory);
        var strategyId = Guid.NewGuid();
        var strategyCode = $"retention_{Guid.NewGuid():N}";
        var prefix = $"retention-benchmark-{Guid.NewGuid():N}";
        var oldUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var cutoffUtc = DateTimeOffset.UtcNow.AddHours(-48);
        var baseline = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 0);
        var runs = Enumerable.Range(0, eligibleCount + blockedCount)
            .Select(index => WithRetentionKey(
                CreateSkippedRun(strategyId, oldUtc.AddSeconds(index)),
                $"{prefix}-{index:D4}"))
            .ToArray();
        var blockedRuns = runs.Skip(eligibleCount).ToArray();

        await InsertStrategyAsync(factory, strategyId, strategyCode, liveStakes: false);
        try
        {
            await InsertPaperOrderDependenciesAsync(
                factory,
                strategyId,
                oldUtc,
                blockedRuns.Select(run => run.ConditionId).ToArray());
            Assert.Equal(
                runs.Length,
                (await repository.TryAddStrategyMarketPaperRunsAsync(runs)).Count);
            await DeleteProjectionBlockersAsync(factory, strategyId);

            var summaryTimer = Stopwatch.StartNew();
            var summary = await repository.GetPaperOnlySkippedRunRetentionSummaryAsync(cutoffUtc, 10);
            summaryTimer.Stop();
            var previewTimer = Stopwatch.StartNew();
            var preview = await repository.PreviewPaperOnlySkippedRunRetentionAsync(
                cutoffUtc,
                eligibleCount + blockedCount);
            previewTimer.Stop();

            var runIds = runs.Select(run => run.Id).ToHashSet();
            var fixturePreviewIds = preview.CandidateRunIds
                .Where(runIds.Contains)
                .ToArray();
            Assert.Equal(eligibleCount, fixturePreviewIds.Length);
            Assert.Equal(
                runs.Take(eligibleCount).Select(run => run.Id),
                fixturePreviewIds);
            Assert.Equal(baseline.TotalCandidateRows + eligibleCount, summary.TotalCandidateRows);
            Console.WriteLine(
                $"Set-based retention benchmark: rows={runs.Length}, eligible={eligibleCount}, " +
                $"paperOrderBlocked={blockedCount}, summaryMs={summaryTimer.Elapsed.TotalMilliseconds:F2}, " +
                $"previewMs={previewTimer.Elapsed.TotalMilliseconds:F2}");
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
}
