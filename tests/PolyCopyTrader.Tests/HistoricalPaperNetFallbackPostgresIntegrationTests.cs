using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

[Collection(PaperCopiedTraderPerformancePostgresIntegrationCollection.Name)]
public sealed class HistoricalPaperNetFallbackPostgresIntegrationTests
{
    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task AuthoritativeRepair_ChangesOnlyNet_AndIsTerminalWithinPaperScope()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = Fixture.Create();
        var calculatedAtUtc = fixture.BaseUtc.AddMinutes(-2);
        var target = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 25m,
            grossPnlUsd: 5m,
            feeUsd: 0.75m,
            feeStatus: FeeAccountingStatus.VenueReported.ToString(),
            feeRole: FeeLiquidityRole.Maker.ToString(),
            feeSource: "venue-authoritative-integration-v1",
            feeRate: 0.03m,
            feeExponent: 2,
            feeTakerOnly: false,
            feeCalculatedAtUtc: calculatedAtUtc,
            netPnlUsd: null);
        var inconsistentNet = target with
        {
            Id = Guid.NewGuid(),
            MarketId = $"inconsistent-{fixture.Suffix}",
            ConditionId = $"inconsistent-condition-{fixture.Suffix}",
            MarketSlug = $"inconsistent-{fixture.Suffix}",
            NetRealizedPnlUsd = 999m
        };
        var priorFallback = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 10m,
            grossPnlUsd: 2m,
            feeUsd: 0.2m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: HistoricalPaperNetFallbackConstants.CalculationSource,
            netPnlUsd: null);
        var completeMixed = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 25m,
            grossPnlUsd: 5m,
            feeUsd: 0.75m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "mixed",
            netPnlUsd: 4.25m);
        var liveScope = target with
        {
            Id = Guid.NewGuid(),
            MarketId = $"live-{fixture.Suffix}",
            ConditionId = $"live-condition-{fixture.Suffix}",
            MarketSlug = $"live-{fixture.Suffix}"
        };

        try
        {
            await SeedStrategyAsync(factory, fixture);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(target));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(inconsistentNet));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(priorFallback));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(completeMixed));
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(liveScope));
            await SetRetentionScopeAsync(factory, liveScope.Id, StrategyRunRetentionScopes.LiveOrShadow);

            Assert.Equal(
                StrategyRunRetentionScopes.PaperOnly,
                (await ReadRunAccountingAsync(factory, target.Id)).RetentionScope);
            var targetWithoutNet = await ReadRunPayloadWithoutNetAsync(factory, target.Id);
            var inconsistentWithoutNet = await ReadRunPayloadWithoutNetAsync(
                factory,
                inconsistentNet.Id);
            var completeMixedBefore = await ReadRunPayloadAsync(factory, completeMixed.Id);
            Assert.Null(await ReadDashboardReconciliationAsync(factory, fixture.StrategyId));
            var preview = await repository.ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: false);

            Assert.Equal(2, preview.Candidates);
            Assert.Equal(0, preview.RunsUpdated);
            Assert.Equal(0, preview.CompareAndSetConflicts);
            Assert.True(preview.ReachedEnd);
            Assert.Equal(
                new[] { target.Id, inconsistentNet.Id }.Order().Last(),
                preview.ContinuationCursor?.RunId);
            Assert.Null((await ReadRunAccountingAsync(factory, target.Id)).NetPnlUsd);
            Assert.Equal(999m, (await ReadRunAccountingAsync(factory, inconsistentNet.Id)).NetPnlUsd);

            var applied = await repository.ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true);

            Assert.Equal(2, applied.Candidates);
            Assert.Equal(2, applied.RunsUpdated);
            Assert.Equal(0, applied.CompareAndSetConflicts);
            Assert.True(applied.ReachedEnd);
            Assert.Equal(
                new[] { target.Id, inconsistentNet.Id }.Order().Last(),
                applied.ContinuationCursor?.RunId);
            Assert.Equal(targetWithoutNet, await ReadRunPayloadWithoutNetAsync(factory, target.Id));
            Assert.Equal(
                inconsistentWithoutNet,
                await ReadRunPayloadWithoutNetAsync(factory, inconsistentNet.Id));
            var repaired = await ReadRunAccountingAsync(factory, target.Id);
            Assert.Equal(0.75m, repaired.FeeUsd);
            Assert.Equal(FeeAccountingStatus.VenueReported.ToString(), repaired.FeeStatus);
            Assert.Equal(FeeLiquidityRole.Maker.ToString(), repaired.FeeRole);
            Assert.Equal("venue-authoritative-integration-v1", repaired.FeeSource);
            Assert.Equal(0.03m, repaired.FeeRate);
            Assert.Equal(2, repaired.FeeExponent);
            Assert.False(repaired.FeeTakerOnly);
            Assert.Equal(calculatedAtUtc, repaired.FeeCalculatedAtUtc);
            Assert.Equal(4.25m, repaired.NetPnlUsd);
            var repairedInconsistent = await ReadRunAccountingAsync(factory, inconsistentNet.Id);
            Assert.Equal(0.75m, repairedInconsistent.FeeUsd);
            Assert.Equal(FeeAccountingStatus.VenueReported.ToString(), repairedInconsistent.FeeStatus);
            Assert.Equal("venue-authoritative-integration-v1", repairedInconsistent.FeeSource);
            Assert.Equal(4.25m, repairedInconsistent.NetPnlUsd);
            Assert.Equal(
                new DashboardReconciliationSnapshot(
                    50,
                    "strategy_run_fee_accounting_changed",
                    0,
                    null),
                await ReadDashboardReconciliationAsync(factory, fixture.StrategyId));

            var retry = await repository.ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true);

            Assert.Equal(0, retry.Candidates);
            Assert.Equal(0, retry.RunsUpdated);
            Assert.Equal(0, retry.CompareAndSetConflicts);
            Assert.True(retry.ReachedEnd);
            Assert.Null(retry.ContinuationCursor);
            Assert.Null((await ReadRunAccountingAsync(factory, priorFallback.Id)).NetPnlUsd);
            Assert.Equal(completeMixedBefore, await ReadRunPayloadAsync(factory, completeMixed.Id));
            Assert.Null((await ReadRunAccountingAsync(factory, liveScope.Id)).NetPnlUsd);
        }
        finally
        {
            await CleanupFixtureAsync(factory, fixture);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Fallback_UsesOnlyExactPaperDonors_RoundsAwayFromZero_AndLeavesDependentsUntouched()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = Fixture.Create();
        var positiveOrder = CreateOrder(fixture, Guid.NewGuid(), $"positive-asset-{fixture.Suffix}");
        var excludedOrder = CreateOrder(fixture, Guid.NewGuid(), $"excluded-asset-{fixture.Suffix}");
        var positiveTarget = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: 10m,
            feeUsd: 7m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "legacy-positive",
            feeRate: 0.7m,
            feeExponent: 7,
            feeTakerOnly: true,
            feeCalculatedAtUtc: fixture.BaseUtc.AddHours(-1),
            netPnlUsd: 3m,
            paperOrder: positiveOrder);
        var negativeTarget = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: -10m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.CalculationUnavailable.ToString(),
            feeRole: FeeLiquidityRole.Maker.ToString(),
            feeSource: "legacy-negative",
            feeRate: 0.4m,
            feeExponent: 4,
            feeTakerOnly: false,
            feeCalculatedAtUtc: fixture.BaseUtc.AddHours(-2),
            netPnlUsd: null);
        var midpointTarget = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 0.00000025m,
            grossPnlUsd: 1m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.PartiallyCalculated.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "legacy-midpoint",
            netPnlUsd: null);
        var excludedTarget = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: 10m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "transient-exact-lookup",
            netPnlUsd: null,
            paperOrder: excludedOrder);
        var liveTarget = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: 10m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "live-target",
            netPnlUsd: null);
        var firstDonor = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 100m,
            grossPnlUsd: 10m,
            feeUsd: 2m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "exact-donor-calculated",
            netPnlUsd: 8m);
        var secondDonor = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 50m,
            grossPnlUsd: -10m,
            feeUsd: 1m,
            feeStatus: FeeAccountingStatus.VenueReported.ToString(),
            feeRole: FeeLiquidityRole.Maker.ToString(),
            feeSource: "exact-donor-venue",
            netPnlUsd: -11m);
        var priorFallback = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 100m,
            grossPnlUsd: 60m,
            feeUsd: 50m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: HistoricalPaperNetFallbackConstants.CalculationSource,
            netPnlUsd: 10m);
        var liveDonor = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 100m,
            grossPnlUsd: 60m,
            feeUsd: 50m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "live-exact-donor",
            netPnlUsd: 10m);
        var fill = CreateDependentFill(fixture, positiveOrder);
        var position = CreateDependentPosition(fixture, positiveOrder);
        var settlement = CreateDependentSettlement(fixture, positiveOrder);

        try
        {
            await SeedStrategyAsync(factory, fixture);
            await repository.AddPaperOrderAsync(positiveOrder);
            await repository.AddPaperOrderAsync(excludedOrder);
            await repository.AddPaperFillAsync(fill);
            await repository.UpsertPaperPositionAsync(position);
            Assert.True(await repository.TryAddPaperPositionSettlementAsync(settlement));
            foreach (var run in new[]
                     {
                         positiveTarget,
                         negativeTarget,
                         midpointTarget,
                         excludedTarget,
                         liveTarget,
                         firstDonor,
                         secondDonor,
                         priorFallback,
                         liveDonor
                     })
            {
                Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            }

            await SetRetentionScopeAsync(factory, liveTarget.Id, StrategyRunRetentionScopes.LiveOrShadow);
            await SetRetentionScopeAsync(factory, liveDonor.Id, StrategyRunRetentionScopes.LiveOrShadow);
            var dependentRowsBefore = await ReadDependentRowsAsync(
                factory,
                positiveTarget.Id,
                fill.Id,
                settlement.Id);
            var excludedBefore = await ReadRunPayloadAsync(factory, excludedTarget.Id);
            var liveTargetBefore = await ReadRunPayloadAsync(factory, liveTarget.Id);
            var positiveNonAccountingBefore = await ReadRunPayloadWithoutAccountingAsync(
                factory,
                positiveTarget.Id);
            var negativeNonAccountingBefore = await ReadRunPayloadWithoutAccountingAsync(
                factory,
                negativeTarget.Id);
            var midpointNonAccountingBefore = await ReadRunPayloadWithoutAccountingAsync(
                factory,
                midpointTarget.Id);

            var expectedTargetIds = new[]
            {
                positiveTarget.Id,
                negativeTarget.Id,
                midpointTarget.Id
            }.Order().ToArray();
            var firstPage = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 2,
                applyEnabled: true,
                excludedPaperOrderIds: [excludedOrder.Id]);

            Assert.Equal(2, firstPage.Candidates);
            Assert.Equal(2, firstPage.ExactDonorCount);
            Assert.Equal(3m, firstPage.ExactDonorFeeUsd);
            Assert.Equal(150m, firstPage.ExactDonorStakeUsd);
            Assert.Equal(0.02m, firstPage.FeeToStakeRatio);
            Assert.True(firstPage.DonorAvailable);
            Assert.Equal(2, firstPage.RunsUpdated);
            Assert.Equal(0, firstPage.CompareAndSetConflicts);
            Assert.False(firstPage.ReachedEnd);
            Assert.Equal(expectedTargetIds[1], firstPage.ContinuationCursor?.RunId);

            var lastPage = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 2,
                applyEnabled: true,
                excludedPaperOrderIds: [excludedOrder.Id],
                afterCursor: firstPage.ContinuationCursor);

            Assert.Equal(1, lastPage.Candidates);
            Assert.Equal(2, lastPage.ExactDonorCount);
            Assert.Equal(3m, lastPage.ExactDonorFeeUsd);
            Assert.Equal(150m, lastPage.ExactDonorStakeUsd);
            Assert.Equal(0.02m, lastPage.FeeToStakeRatio);
            Assert.True(lastPage.DonorAvailable);
            Assert.Equal(1, lastPage.RunsUpdated);
            Assert.Equal(0, lastPage.CompareAndSetConflicts);
            Assert.True(lastPage.ReachedEnd);
            Assert.Equal(expectedTargetIds[2], lastPage.ContinuationCursor?.RunId);

            AssertFallbackAccounting(
                await ReadRunAccountingAsync(factory, positiveTarget.Id),
                expectedFeeUsd: 0.4m,
                expectedNetPnlUsd: 9.6m);
            AssertFallbackAccounting(
                await ReadRunAccountingAsync(factory, negativeTarget.Id),
                expectedFeeUsd: 0.4m,
                expectedNetPnlUsd: -10.4m);
            AssertFallbackAccounting(
                await ReadRunAccountingAsync(factory, midpointTarget.Id),
                expectedFeeUsd: 0.00000001m,
                expectedNetPnlUsd: 0.99999999m);
            Assert.Equal(
                positiveNonAccountingBefore,
                await ReadRunPayloadWithoutAccountingAsync(factory, positiveTarget.Id));
            Assert.Equal(
                negativeNonAccountingBefore,
                await ReadRunPayloadWithoutAccountingAsync(factory, negativeTarget.Id));
            Assert.Equal(
                midpointNonAccountingBefore,
                await ReadRunPayloadWithoutAccountingAsync(factory, midpointTarget.Id));
            Assert.Equal(
                dependentRowsBefore,
                await ReadDependentRowsAsync(factory, positiveTarget.Id, fill.Id, settlement.Id));
            Assert.Equal(excludedBefore, await ReadRunPayloadAsync(factory, excludedTarget.Id));
            Assert.Equal(liveTargetBefore, await ReadRunPayloadAsync(factory, liveTarget.Id));

            var retry = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true,
                excludedPaperOrderIds: [excludedOrder.Id]);

            Assert.Equal(0, retry.Candidates);
            Assert.Equal(2, retry.ExactDonorCount);
            Assert.Equal(3m, retry.ExactDonorFeeUsd);
            Assert.Equal(150m, retry.ExactDonorStakeUsd);
            Assert.Equal(0.02m, retry.FeeToStakeRatio);
            Assert.Equal(0, retry.RunsUpdated);
            Assert.Equal(0, retry.CompareAndSetConflicts);
            Assert.True(retry.ReachedEnd);
            Assert.Null(retry.ContinuationCursor);

            var exactRetry = await repository.ApplyHistoricalPaperAuthoritativeNetRepairBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true);
            Assert.Equal(0, exactRetry.Candidates);
            Assert.Equal(0, exactRetry.RunsUpdated);
        }
        finally
        {
            await CleanupFixtureAsync(factory, fixture);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Fallback_WithoutExactDonor_ReportsUnavailableAndDoesNotMutateCandidate()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = Fixture.Create();
        var target = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: -4m,
            feeUsd: 9m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "no-donor-target",
            feeRate: 0.45m,
            feeExponent: 5,
            feeTakerOnly: true,
            feeCalculatedAtUtc: fixture.BaseUtc.AddMinutes(-1),
            netPnlUsd: -13m);

        try
        {
            await SeedStrategyAsync(factory, fixture);
            Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(target));
            var before = await ReadRunPayloadAsync(factory, target.Id);

            var preview = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: false,
                excludedPaperOrderIds: []);
            var applied = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true,
                excludedPaperOrderIds: []);

            foreach (var result in new[] { preview, applied })
            {
                Assert.Equal(1, result.Candidates);
                Assert.Equal(0, result.ExactDonorCount);
                Assert.Equal(0m, result.ExactDonorFeeUsd);
                Assert.Equal(0m, result.ExactDonorStakeUsd);
                Assert.Null(result.FeeToStakeRatio);
                Assert.False(result.DonorAvailable);
                Assert.Equal(0, result.RunsUpdated);
                Assert.Equal(0, result.CompareAndSetConflicts);
                Assert.True(result.ReachedEnd);
                Assert.Equal(target.Id, result.ContinuationCursor?.RunId);
            }

            Assert.Equal(before, await ReadRunPayloadAsync(factory, target.Id));
        }
        finally
        {
            await CleanupFixtureAsync(factory, fixture);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Fallback_ZeroFeeDonorIsValid_AndInvalidTargetsRemainUnchanged()
    {
        var factory = await CreateFactoryAsync();
        var repository = new PostgresAppRepository(factory);
        var fixture = Fixture.Create();
        var donor = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 100m,
            grossPnlUsd: 10m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Maker.ToString(),
            feeSource: "zero-fee-exact-donor",
            netPnlUsd: 10m);
        var eligible = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: -4m,
            feeUsd: 7m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "zero-ratio-target",
            netPnlUsd: null);
        var zeroStake = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 0m,
            grossPnlUsd: 1m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "invalid-zero-stake",
            netPnlUsd: null);
        var missingGross = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: 1m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "invalid-missing-gross",
            netPnlUsd: null) with
        {
            SettlementValueUsd = null,
            RealizedPnlUsd = null
        };

        try
        {
            await SeedStrategyAsync(factory, fixture);
            foreach (var run in new[] { donor, eligible, zeroStake, missingGross })
            {
                Assert.True(await repository.TryAddStrategyMarketPaperRunAsync(run));
            }

            var zeroStakeBefore = await ReadRunPayloadAsync(factory, zeroStake.Id);
            var missingGrossBefore = await ReadRunPayloadAsync(factory, missingGross.Id);

            var result = await repository.ApplyHistoricalPaperNetFallbackBatchAsync(
                fixture.StrategyId,
                limit: 20,
                applyEnabled: true,
                excludedPaperOrderIds: []);

            Assert.Equal(1, result.Candidates);
            Assert.Equal(1, result.ExactDonorCount);
            Assert.Equal(0m, result.ExactDonorFeeUsd);
            Assert.Equal(100m, result.ExactDonorStakeUsd);
            Assert.Equal(0m, result.FeeToStakeRatio);
            Assert.True(result.DonorAvailable);
            Assert.Equal(1, result.RunsUpdated);
            Assert.Equal(0, result.CompareAndSetConflicts);
            AssertFallbackAccounting(
                await ReadRunAccountingAsync(factory, eligible.Id),
                expectedFeeUsd: 0m,
                expectedNetPnlUsd: -4m);
            Assert.Equal(zeroStakeBefore, await ReadRunPayloadAsync(factory, zeroStake.Id));
            Assert.Equal(missingGrossBefore, await ReadRunPayloadAsync(factory, missingGross.Id));
        }
        finally
        {
            await CleanupFixtureAsync(factory, fixture);
        }
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task Fallback_WhenConcurrentExactAccountingCommits_ReportsCasConflictAndPreservesExactWinner()
    {
        var factory = await CreateFactoryAsync();
        var fixture = Fixture.Create();
        var donor = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 100m,
            grossPnlUsd: 10m,
            feeUsd: 2m,
            feeStatus: FeeAccountingStatus.Calculated.ToString(),
            feeRole: FeeLiquidityRole.Taker.ToString(),
            feeSource: "race-donor",
            netPnlUsd: 8m);
        var target = CreateRun(
            fixture,
            Guid.NewGuid(),
            stakeUsd: 20m,
            grossPnlUsd: 10m,
            feeUsd: 0m,
            feeStatus: FeeAccountingStatus.LegacyUnknown.ToString(),
            feeRole: FeeLiquidityRole.Unknown.ToString(),
            feeSource: "race-target",
            netPnlUsd: null);
        Task<HistoricalPaperNetFallbackBatchResult>? fallbackTask = null;

        try
        {
            await SeedStrategyAsync(factory, fixture);
            var seedRepository = new PostgresAppRepository(factory);
            Assert.True(await seedRepository.TryAddStrategyMarketPaperRunAsync(donor));
            Assert.True(await seedRepository.TryAddStrategyMarketPaperRunAsync(target));

            var applicationName = $"net-fallback-cas-{fixture.Suffix}";
            var fallbackFactory = CreateUninitializedFactory(applicationName);
            var fallbackRepository = new PostgresAppRepository(fallbackFactory);
            using var raceCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var exactConnection = factory.CreateConnection();
            await exactConnection.OpenAsync();
            await using var exactTransaction = await exactConnection.BeginTransactionAsync();
            var exactCommitted = false;
            try
            {
                await CompleteExactAccountingAsync(
                    exactConnection,
                    exactTransaction,
                    target.Id,
                    fixture.BaseUtc.AddMinutes(3));
                fallbackTask = fallbackRepository.ApplyHistoricalPaperNetFallbackBatchAsync(
                    fixture.StrategyId,
                    limit: 20,
                    applyEnabled: true,
                    excludedPaperOrderIds: [],
                    cancellationToken: raceCancellation.Token);

                Assert.True(
                    await WaitUntilBlockedByAsync(
                        exactConnection,
                        exactTransaction,
                        applicationName,
                        exactConnection.ProcessID,
                        fallbackTask),
                    "Fallback UPDATE did not block behind the uncommitted exact-accounting row.");
                await exactTransaction.CommitAsync();
                exactCommitted = true;

                var result = await fallbackTask.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, result.Candidates);
                Assert.Equal(1, result.ExactDonorCount);
                Assert.Equal(2m, result.ExactDonorFeeUsd);
                Assert.Equal(100m, result.ExactDonorStakeUsd);
                Assert.Equal(0.02m, result.FeeToStakeRatio);
                Assert.Equal(0, result.RunsUpdated);
                Assert.Equal(1, result.CompareAndSetConflicts);
                Assert.Equal(0, result.Deferred);
            }
            finally
            {
                if (!exactCommitted)
                {
                    await exactTransaction.RollbackAsync(CancellationToken.None);
                }

                raceCancellation.Cancel();
                await DrainRaceTaskAsync(fallbackTask);
            }

            var exactWinner = await ReadRunAccountingAsync(factory, target.Id);
            Assert.Equal(1m, exactWinner.FeeUsd);
            Assert.Equal(FeeAccountingStatus.Calculated.ToString(), exactWinner.FeeStatus);
            Assert.Equal(FeeLiquidityRole.Maker.ToString(), exactWinner.FeeRole);
            Assert.Equal("concurrent-exact-integration-v1", exactWinner.FeeSource);
            Assert.Equal(0.05m, exactWinner.FeeRate);
            Assert.Equal(2, exactWinner.FeeExponent);
            Assert.False(exactWinner.FeeTakerOnly);
            Assert.Equal(9m, exactWinner.NetPnlUsd);
        }
        finally
        {
            await CleanupFixtureAsync(factory, fixture);
        }
    }

    private static async Task<PostgresConnectionFactory> CreateFactoryAsync()
    {
        var factory = CreateUninitializedFactory();
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        return factory;
    }

    private static PostgresConnectionFactory CreateUninitializedFactory(string? applicationName = null)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION disappeared after test discovery.");
        }

        if (!string.IsNullOrWhiteSpace(applicationName))
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                ApplicationName = applicationName
            };
            connectionString = builder.ConnectionString;
        }

        return new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString });
    }

    private static async Task SeedStrategyAsync(
        PostgresConnectionFactory factory,
        Fixture fixture)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO public.strategies (
    id, code, name, description, enabled, live_stakes, created_at_utc, updated_at_utc)
VALUES (
    @Id, @Code, @Code, 'historical Paper Net fallback integration test', true, false,
    @CreatedAtUtc, @CreatedAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", fixture.StrategyId);
        command.Parameters.AddWithValue("Code", fixture.StrategyCode);
        command.Parameters.AddWithValue("CreatedAtUtc", fixture.BaseUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static StrategyMarketPaperRun CreateRun(
        Fixture fixture,
        Guid runId,
        decimal stakeUsd,
        decimal grossPnlUsd,
        decimal feeUsd,
        string feeStatus,
        string feeRole,
        string feeSource,
        decimal? netPnlUsd,
        decimal? feeRate = null,
        int? feeExponent = null,
        bool? feeTakerOnly = null,
        DateTimeOffset? feeCalculatedAtUtc = null,
        PaperOrder? paperOrder = null)
    {
        var marketId = $"market-{runId:N}";
        var assetId = paperOrder?.AssetId ?? $"asset-{runId:N}";
        var conditionId = paperOrder?.ConditionId ?? $"condition-{runId:N}";
        return new StrategyMarketPaperRun(
            runId,
            fixture.StrategyId,
            marketId,
            conditionId,
            marketId,
            "Historical Paper Net fallback integration market",
            "Integration",
            fixture.BaseUtc.AddMinutes(-10),
            fixture.BaseUtc,
            fixture.BaseUtc.AddMinutes(-12),
            fixture.BaseUtc.AddMinutes(-2),
            StrategyMarketPaperRunStatuses.Settled,
            assetId,
            "Up",
            0.5m,
            stakeUsd,
            stakeUsd * 2m,
            null,
            paperOrder?.Id,
            fixture.BaseUtc.AddMinutes(-1),
            0.75m,
            stakeUsd + grossPnlUsd,
            grossPnlUsd,
            fixture.BaseUtc.AddMinutes(1),
            null,
            fixture.BaseUtc.AddMinutes(-12),
            fixture.BaseUtc.AddMinutes(1),
            "{\"fixture\":\"historical-paper-net-fallback\"}",
            feeUsd,
            feeStatus,
            feeRole,
            feeSource,
            feeRate,
            feeExponent,
            feeTakerOnly,
            feeCalculatedAtUtc,
            netPnlUsd);
    }

    private static PaperOrder CreateOrder(Fixture fixture, Guid orderId, string assetId)
    {
        return new PaperOrder(
            orderId,
            Guid.NewGuid(),
            fixture.Wallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            assetId,
            $"condition-{assetId}",
            "Up",
            0.5m,
            40m,
            20m,
            fixture.BaseUtc.AddMinutes(-3),
            fixture.BaseUtc.AddMinutes(2),
            FilledAtUtc: fixture.BaseUtc.AddMinutes(-1),
            StrategyId: fixture.StrategyId,
            RawDecisionJson: "{\"fixture\":true}",
            CorrelationId: Guid.NewGuid(),
            ExecutionSource: "historical-paper-net-fallback-integration");
    }

    private static PaperFill CreateDependentFill(Fixture fixture, PaperOrder order)
    {
        return new PaperFill(
            Guid.NewGuid(),
            order.Id,
            0.5m,
            40m,
            fixture.BaseUtc.AddMinutes(-1),
            "dependent-fill-sentinel",
            RealizedPnlUsd: 12m,
            FeeUsd: 1.2m,
            FeeAccountingStatus: FeeAccountingStatus.VenueReported.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Maker.ToString(),
            FeeCalculationSource: "dependent-fill-source",
            FeeRate: 0.1m,
            FeeExponent: 3,
            FeeTakerOnly: false,
            FeeCalculatedAtUtc: fixture.BaseUtc.AddMinutes(-1),
            NetRealizedPnlUsd: 10.8m);
    }

    private static PaperPosition CreateDependentPosition(Fixture fixture, PaperOrder order)
    {
        return new PaperPosition(
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            4m,
            0.5m,
            3m,
            1m,
            fixture.BaseUtc,
            fixture.Wallet,
            FeeUsd: 0.3m,
            FeeAccountingStatus: FeeAccountingStatus.Calculated.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Taker.ToString(),
            FeeCalculationSource: "dependent-position-source",
            FeeRate: 0.2m,
            FeeExponent: 2,
            FeeTakerOnly: true,
            FeeCalculatedAtUtc: fixture.BaseUtc,
            NetUnrealizedPnlUsd: 0.7m);
    }

    private static PaperPositionSettlement CreateDependentSettlement(
        Fixture fixture,
        PaperOrder order)
    {
        return new PaperPositionSettlement(
            Guid.NewGuid(),
            fixture.Wallet,
            order.AssetId,
            order.ConditionId,
            order.Outcome,
            order.AssetId,
            order.Outcome,
            "Integration",
            40m,
            0.5m,
            20m,
            30m,
            10m,
            true,
            "dependent-settlement-source",
            fixture.BaseUtc.AddMinutes(1),
            fixture.BaseUtc.AddMinutes(2),
            FeeUsd: 0.8m,
            FeeAccountingStatus: FeeAccountingStatus.VenueReported.ToString(),
            FeeLiquidityRole: FeeLiquidityRole.Maker.ToString(),
            FeeCalculationSource: "dependent-settlement-fee-source",
            FeeRate: 0.08m,
            FeeExponent: 4,
            FeeTakerOnly: false,
            FeeCalculatedAtUtc: fixture.BaseUtc.AddMinutes(2),
            NetRealizedPnlUsd: 9.2m);
    }

    private static async Task SetRetentionScopeAsync(
        PostgresConnectionFactory factory,
        Guid runId,
        string retentionScope)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
UPDATE public.strategy_market_paper_runs
SET retention_scope = @RetentionScope
WHERE id = @RunId;
""",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("RetentionScope", retentionScope);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task CompleteExactAccountingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid runId,
        DateTimeOffset calculatedAtUtc)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE public.strategy_market_paper_runs
SET fee_usd = 1,
    fee_accounting_status = 'Calculated',
    fee_liquidity_role = 'Maker',
    fee_calculation_source = 'concurrent-exact-integration-v1',
    fee_rate = 0.05,
    fee_exponent = 2,
    fee_taker_only = false,
    fee_calculated_at_utc = @CalculatedAtUtc,
    net_realized_pnl_usd = realized_pnl_usd - 1
WHERE id = @RunId;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("CalculatedAtUtc", calculatedAtUtc.UtcDateTime);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<bool> WaitUntilBlockedByAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string applicationName,
        int blockerProcessId,
        Task fallbackTask)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1
    FROM pg_stat_activity activity
    WHERE activity.datname = current_database()
      AND activity.application_name = @ApplicationName
      AND activity.state = 'active'
      AND activity.wait_event_type = 'Lock'
      AND @BlockerProcessId = ANY(pg_blocking_pids(activity.pid)));
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("ApplicationName", applicationName);
        command.Parameters.AddWithValue("BlockerProcessId", blockerProcessId);
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!fallbackTask.IsCompleted && DateTimeOffset.UtcNow < timeoutAt)
        {
            if (await command.ExecuteScalarAsync() is true)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    private static async Task DrainRaceTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (PostgresException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    private static void AssertFallbackAccounting(
        RunAccountingSnapshot actual,
        decimal expectedFeeUsd,
        decimal expectedNetPnlUsd)
    {
        Assert.Equal(expectedFeeUsd, actual.FeeUsd);
        Assert.Equal(FeeAccountingStatus.Calculated.ToString(), actual.FeeStatus);
        Assert.Equal(FeeLiquidityRole.Unknown.ToString(), actual.FeeRole);
        Assert.Equal(HistoricalPaperNetFallbackConstants.CalculationSource, actual.FeeSource);
        Assert.Null(actual.FeeRate);
        Assert.Null(actual.FeeExponent);
        Assert.Null(actual.FeeTakerOnly);
        Assert.NotNull(actual.FeeCalculatedAtUtc);
        Assert.Equal(expectedNetPnlUsd, actual.NetPnlUsd);
        Assert.Equal(StrategyRunRetentionScopes.PaperOnly, actual.RetentionScope);
    }

    private static async Task<RunAccountingSnapshot> ReadRunAccountingAsync(
        PostgresConnectionFactory factory,
        Guid runId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source,
       fee_rate, fee_exponent, fee_taker_only, fee_calculated_at_utc,
       net_realized_pnl_usd, realized_pnl_usd, stake_usd, retention_scope
FROM public.strategy_market_paper_runs
WHERE id = @RunId;
""",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var result = new RunAccountingSnapshot(
            reader.GetDecimal(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NullableDecimal(reader, 4),
            NullableInt32(reader, 5),
            NullableBoolean(reader, 6),
            NullableDateTimeOffset(reader, 7),
            NullableDecimal(reader, 8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetString(11));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static Task<string> ReadRunPayloadAsync(
        PostgresConnectionFactory factory,
        Guid runId) => ReadJsonScalarAsync(
            factory,
            "SELECT to_jsonb(run)::text FROM public.strategy_market_paper_runs run WHERE id = @Id;",
            runId);

    private static Task<string> ReadRunPayloadWithoutNetAsync(
        PostgresConnectionFactory factory,
        Guid runId) => ReadJsonScalarAsync(
            factory,
            "SELECT (to_jsonb(run) - 'net_realized_pnl_usd')::text " +
            "FROM public.strategy_market_paper_runs run WHERE id = @Id;",
            runId);

    private static Task<string> ReadRunPayloadWithoutAccountingAsync(
        PostgresConnectionFactory factory,
        Guid runId) => ReadJsonScalarAsync(
            factory,
            "SELECT (to_jsonb(run) - " +
            "ARRAY['fee_usd', 'fee_accounting_status', 'fee_liquidity_role', " +
            "'fee_calculation_source', 'fee_rate', 'fee_exponent', 'fee_taker_only', " +
            "'fee_calculated_at_utc', 'net_realized_pnl_usd'])::text " +
            "FROM public.strategy_market_paper_runs run WHERE id = @Id;",
            runId);

    private static async Task<string> ReadJsonScalarAsync(
        PostgresConnectionFactory factory,
        string sql,
        Guid id)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("Id", id);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadDependentRowsAsync(
        PostgresConnectionFactory factory,
        Guid runId,
        Guid fillId,
        Guid settlementId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT jsonb_build_object(
    'order', to_jsonb(paper_order),
    'fill', to_jsonb(fill),
    'position', to_jsonb(position),
    'settlement', to_jsonb(settlement))::text
FROM public.strategy_market_paper_runs run
INNER JOIN public.paper_orders paper_order ON paper_order.id = run.paper_order_id
INNER JOIN public.paper_fills fill ON fill.paper_order_id = paper_order.id
INNER JOIN public.paper_positions position
    ON position.copied_trader_wallet = paper_order.copied_trader_wallet
   AND position.asset_id = paper_order.asset_id
INNER JOIN public.paper_position_settlements settlement
    ON settlement.copied_trader_wallet = paper_order.copied_trader_wallet
   AND settlement.asset_id = paper_order.asset_id
WHERE run.id = @RunId
  AND fill.id = @FillId
  AND settlement.id = @SettlementId;
""",
            connection);
        command.Parameters.AddWithValue("RunId", runId);
        command.Parameters.AddWithValue("FillId", fillId);
        command.Parameters.AddWithValue("SettlementId", settlementId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<DashboardReconciliationSnapshot?> ReadDashboardReconciliationAsync(
        PostgresConnectionFactory factory,
        Guid strategyId)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
SELECT priority, reason, attempt_count, last_error
FROM public.dashboard_projection_reconciliation_queue
WHERE strategy_id = @StrategyId;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var result = new DashboardReconciliationSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
        Assert.False(await reader.ReadAsync());
        return result;
    }

    private static async Task CleanupFixtureAsync(
        PostgresConnectionFactory factory,
        Fixture fixture)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
DELETE FROM public.paper_position_settlements WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_positions WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.strategy_market_paper_runs WHERE strategy_id = @StrategyId;
DELETE FROM public.paper_fills fill
USING public.paper_orders paper_order
WHERE fill.paper_order_id = paper_order.id
  AND paper_order.strategy_id = @StrategyId;
DELETE FROM public.paper_orders WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM public.dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId;
DELETE FROM public.strategies WHERE id = @StrategyId;
DELETE FROM public.dashboard_projection_events WHERE strategy_id = @StrategyId;
DELETE FROM public.paper_copied_trader_performance_refresh_inflight
WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_copied_trader_performance_refresh_queue
WHERE copied_trader_wallet = @Wallet;
DELETE FROM public.paper_copied_trader_performance
WHERE copied_trader_wallet = @Wallet;
""",
            connection);
        command.Parameters.AddWithValue("StrategyId", fixture.StrategyId);
        command.Parameters.AddWithValue("Wallet", fixture.Wallet);
        await command.ExecuteNonQueryAsync();
    }

    private static decimal? NullableDecimal(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static int? NullableInt32(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static bool? NullableBoolean(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);

    private static DateTimeOffset? NullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    }

    private sealed record RunAccountingSnapshot(
        decimal FeeUsd,
        string FeeStatus,
        string FeeRole,
        string FeeSource,
        decimal? FeeRate,
        int? FeeExponent,
        bool? FeeTakerOnly,
        DateTimeOffset? FeeCalculatedAtUtc,
        decimal? NetPnlUsd,
        decimal GrossPnlUsd,
        decimal StakeUsd,
        string RetentionScope);

    private sealed record DashboardReconciliationSnapshot(
        int Priority,
        string Reason,
        int AttemptCount,
        string? LastError);

    private sealed record Fixture(
        Guid StrategyId,
        string StrategyCode,
        string Wallet,
        string Suffix,
        DateTimeOffset BaseUtc)
    {
        public static Fixture Create()
        {
            var suffix = Guid.NewGuid().ToString("N");
            return new Fixture(
                Guid.NewGuid(),
                $"net-fallback-it-{suffix}",
                $"0xnetfallback{suffix}",
                suffix,
                new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        }
    }
}
