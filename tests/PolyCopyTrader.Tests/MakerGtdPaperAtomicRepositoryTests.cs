using System.Globalization;
using System.Runtime.CompilerServices;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperAtomicRepositoryTests
{
    private const string ExecutionSource = "eth_reference_average_maker_gtd_paper";

    [Fact]
    public async Task FullFill_PersistsExactCallerComputedModelsAtomically()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.Applied, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillApplied, result.ReasonCode);
        Assert.Equal(fixture.FullFillRequest.FilledOrder, repository.PaperOrders.Single());
        Assert.Equal(fixture.FullFillRequest.Fill, repository.PaperFills.Single());
        Assert.Equal(fixture.FullFillRequest.Position, repository.PaperPositions.Single());
        Assert.Equal(fixture.FullFillRequest.EnteredRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Equal(fixture.FullFillRequest.FilledOrder, result.PaperOrder);
        Assert.Equal(fixture.FullFillRequest.Fill, result.PaperFill);
        Assert.Equal(fixture.FullFillRequest.Position, result.PaperPosition);
        Assert.Equal(fixture.FullFillRequest.EnteredRun, result.StrategyRun);
    }

    [Fact]
    public async Task FullFill_RepeatedExactRequest_IsIdempotent()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);

        var first = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);
        var second = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.Applied, first.Outcome);
        Assert.Equal(MakerGtdPaperMutationOutcome.AlreadyApplied, second.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillAlreadyApplied, second.ReasonCode);
        Assert.Single(repository.PaperOrders);
        Assert.Single(repository.PaperFills);
        Assert.Single(repository.PaperPositions);
        Assert.Single(repository.StrategyMarketPaperRuns);
    }

    [PostgresIntegrationFact]
    [Trait("Category", "PostgresIntegration")]
    public async Task PostgresFullFill_JsonbRoundedInitialTimestampAndDirectTruncatedOutcome_RetryIsIdempotent()
    {
        var callerCreatedAtUtc = DateTimeOffset.Parse(
            "2026-08-27T05:59:33.1706318+00:00",
            CultureInfo.InvariantCulture);
        var fixture = CreateFixture(
            fillAtUtc: callerCreatedAtUtc.AddSeconds(30),
            createdAtUtc: callerCreatedAtUtc);
        var connectionString = Environment.GetEnvironmentVariable(
            "POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        var factory = new PostgresConnectionFactory(
            new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();
        await AddStrategyFixtureAsync(factory, fixture.PendingOrder);
        var repository = new PostgresAppRepository(factory);
        var signal = CreateSignal(fixture.PendingOrder);
        await repository.AddPaperEntryPersistenceBatchAsync(new PaperEntryPersistenceBatch(
            [signal],
            [fixture.PendingOrder],
            [],
            [],
            [],
            [fixture.RestingRun]));

        var persistedPendingOrder = await repository.GetPaperOrderAsync(fixture.PendingOrder.Id);
        Assert.NotNull(persistedPendingOrder);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-27T05:59:33.1706320+00:00", CultureInfo.InvariantCulture),
            persistedPendingOrder.CreatedAtUtc);
        var persistedRestingRun = Assert.Single(
            await repository.GetStrategyMarketPaperRunsByPaperOrderIdsAsync([fixture.PendingOrder.Id]));
        var requestedEnteredRun = persistedRestingRun with
        {
            Status = fixture.FullFillRequest.EnteredRun.Status,
            EntryPrice = fixture.FullFillRequest.EnteredRun.EntryPrice,
            StakeUsd = fixture.FullFillRequest.EnteredRun.StakeUsd,
            SizeShares = fixture.FullFillRequest.EnteredRun.SizeShares,
            EnteredAtUtc = fixture.FullFillRequest.EnteredRun.EnteredAtUtc,
            UpdatedAtUtc = fixture.FullFillRequest.EnteredRun.UpdatedAtUtc,
            FeeUsd = fixture.FullFillRequest.EnteredRun.FeeUsd,
            FeeAccountingStatus = fixture.FullFillRequest.EnteredRun.FeeAccountingStatus,
            FeeLiquidityRole = fixture.FullFillRequest.EnteredRun.FeeLiquidityRole,
            FeeCalculationSource = fixture.FullFillRequest.EnteredRun.FeeCalculationSource,
            FeeRate = fixture.FullFillRequest.EnteredRun.FeeRate,
            FeeExponent = fixture.FullFillRequest.EnteredRun.FeeExponent,
            FeeTakerOnly = fixture.FullFillRequest.EnteredRun.FeeTakerOnly,
            FeeCalculatedAtUtc = fixture.FullFillRequest.EnteredRun.FeeCalculatedAtUtc
        };
        var request = fixture.FullFillRequest with { EnteredRun = requestedEnteredRun };

        var first = await repository.TryApplyMakerGtdPaperFullFillAsync(request);
        var second = await repository.TryApplyMakerGtdPaperFullFillAsync(request);

        Assert.True(
            first.Outcome == MakerGtdPaperMutationOutcome.Applied,
            $"Expected Applied but received {first.Outcome}: {first.ReasonCode}");
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillApplied, first.ReasonCode);
        Assert.Equal(MakerGtdPaperMutationOutcome.AlreadyApplied, second.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillAlreadyApplied, second.ReasonCode);
        Assert.Single(await repository.GetPaperFillsForOrdersAsync([fixture.PendingOrder.Id]));
    }

    [Theory]
    [InlineData("2026-08-27T05:59:33.1706315+00:00", "2026-08-27T05:59:33.1706320+00:00")]
    [InlineData("2026-08-27T05:59:33.1706318+00:00", "2026-08-27T05:59:33.1706320+00:00")]
    [InlineData("2026-08-27T05:59:33.3761549+00:00", "2026-08-27T05:59:33.3761550+00:00")]
    public async Task FullFill_PostgresRoundedCreatedAtTimestamp_IsEligible(
        string callerCreatedAtText,
        string persistedCreatedAtText)
    {
        var callerCreatedAtUtc = DateTimeOffset.Parse(callerCreatedAtText, CultureInfo.InvariantCulture);
        var persistedCreatedAtUtc = DateTimeOffset.Parse(persistedCreatedAtText, CultureInfo.InvariantCulture);
        var fixture = CreateFixture(
            fillAtUtc: callerCreatedAtUtc.AddSeconds(30),
            createdAtUtc: callerCreatedAtUtc);
        var repository = Seed(fixture);
        repository.PaperOrders[0] = fixture.PendingOrder with { CreatedAtUtc = persistedCreatedAtUtc };

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.Applied, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillApplied, result.ReasonCode);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task FullFill_CreatedAtTimestampsThatRoundToDifferentPostgresMicroseconds_AreNotEligible()
    {
        var persistedCreatedAtUtc = DateTimeOffset.Parse(
            "2026-08-27T05:59:33.1706320+00:00",
            CultureInfo.InvariantCulture);
        var callerCreatedAtUtc = DateTimeOffset.Parse(
            "2026-08-27T05:59:33.1706326+00:00",
            CultureInfo.InvariantCulture);
        var fixture = CreateFixture(
            fillAtUtc: callerCreatedAtUtc.AddSeconds(30),
            createdAtUtc: callerCreatedAtUtc);
        var repository = Seed(fixture);
        repository.PaperOrders[0] = fixture.PendingOrder with { CreatedAtUtc = persistedCreatedAtUtc };

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FilledOrderShapeMismatch, result.ReasonCode);
        AssertRepositoryStillResting(repository, fixture, persistedCreatedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(240)]
    public async Task FullFill_AtEitherOrderLifetimeBoundary_IsNotEligible(int secondsAfterCreation)
    {
        var createdAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var fixture = CreateFixture(createdAtUtc.AddSeconds(secondsAfterCreation));
        var repository = Seed(fixture);

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.FillTimestampOutsideOrderLifetime, result.ReasonCode);
        AssertRepositoryStillResting(repository, fixture);
    }

    [Fact]
    public async Task FullFill_WithDifferentExpectedSource_IsNotEligible()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var request = fixture.FullFillRequest with
        {
            ExpectedExecutionSource = "another_execution_source"
        };

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(request);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExecutionSourceMismatch, result.ReasonCode);
        AssertRepositoryStillResting(repository, fixture);
    }

    [Fact]
    public async Task FullFill_WithExistingFill_IsNotEligibleAndDoesNotOverwriteState()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var existingFill = fixture.FullFillRequest.Fill with { Id = Guid.NewGuid(), Evidence = "pre-existing" };
        repository.PaperFills.Add(existingFill);

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExistingFillConflict, result.ReasonCode);
        Assert.Equal(existingFill, repository.PaperFills.Single());
        Assert.Equal(fixture.PendingOrder, repository.PaperOrders.Single());
        Assert.Equal(fixture.RestingRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task FullFill_WithUnlinkedRun_IsNotEligible()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var unlinkedRun = fixture.RestingRun with { PaperOrderId = Guid.NewGuid() };
        repository.StrategyMarketPaperRuns.Clear();
        repository.StrategyMarketPaperRuns.Add(unlinkedRun);
        var request = fixture.FullFillRequest with
        {
            EnteredRun = fixture.FullFillRequest.EnteredRun with { PaperOrderId = unlinkedRun.PaperOrderId }
        };

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(request);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.StrategyRunLinkMismatch, result.ReasonCode);
        Assert.Equal(fixture.PendingOrder, repository.PaperOrders.Single());
        Assert.Equal(unlinkedRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task FullFill_WithPositionCasMismatch_IsNotEligibleAndDoesNotPartiallyMutate()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var concurrentPosition = fixture.FullFillRequest.Position with
        {
            SizeShares = 3m,
            AveragePrice = 0.44m,
            EstimatedValueUsd = 1.5m,
            UnrealizedPnlUsd = 0.18m,
            FeeUsd = 0.01m,
            NetUnrealizedPnlUsd = 0.17m
        };
        repository.PaperPositions.Add(concurrentPosition);

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.PositionConcurrencyConflict, result.ReasonCode);
        Assert.Equal(fixture.PendingOrder, repository.PaperOrders.Single());
        Assert.Empty(repository.PaperFills);
        Assert.Equal(concurrentPosition, repository.PaperPositions.Single());
        Assert.Equal(fixture.RestingRun, repository.StrategyMarketPaperRuns.Single());
    }

    [Fact]
    public async Task FullFill_WithCallerRunFeeMismatch_IsNotEligible()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var request = fixture.FullFillRequest with
        {
            EnteredRun = fixture.FullFillRequest.EnteredRun with { FeeUsd = 999m }
        };

        var result = await repository.TryApplyMakerGtdPaperFullFillAsync(request);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.EnteredRunShapeMismatch, result.ReasonCode);
        AssertRepositoryStillResting(repository, fixture);
    }

    [Fact]
    public async Task FullFill_ConcurrentExactRequests_ApplyOnceAndThenReportAlreadyApplied()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        using var start = new ManualResetEventSlim(false);

        Task<MakerGtdPaperMutationResult> InvokeAsync() => Task.Run(async () =>
        {
            start.Wait();
            return await repository.TryApplyMakerGtdPaperFullFillAsync(fixture.FullFillRequest);
        });

        var firstTask = InvokeAsync();
        var secondTask = InvokeAsync();
        start.Set();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, result => result.Outcome == MakerGtdPaperMutationOutcome.Applied);
        Assert.Single(results, result => result.Outcome == MakerGtdPaperMutationOutcome.AlreadyApplied);
        Assert.Single(repository.PaperFills);
        Assert.Single(repository.PaperPositions);
        Assert.Equal(PaperOrderStatus.Filled, repository.PaperOrders.Single().Status);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, repository.StrategyMarketPaperRuns.Single().Status);
    }

    [Fact]
    public async Task Expiry_AtExpiryTime_PersistsExactCallerModelsAndIsIdempotent()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);

        var first = await repository.TryExpireMakerGtdPaperOrderAsync(fixture.ExpiryRequest);
        var semanticallyEquivalentRetry = fixture.ExpiryRequest with
        {
            SkippedRun = fixture.ExpiryRequest.SkippedRun with
            {
                SkipDiagnosticsJson = "{\"attempts\":10,\"evidence\":\"no_cross\"}"
            }
        };
        var second = await repository.TryExpireMakerGtdPaperOrderAsync(semanticallyEquivalentRetry);

        Assert.Equal(MakerGtdPaperMutationOutcome.Applied, first.Outcome);
        Assert.Equal(MakerGtdPaperMutationOutcome.AlreadyApplied, second.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExpiryAlreadyApplied, second.ReasonCode);
        Assert.Equal(fixture.ExpiryRequest.ExpiredOrder, repository.PaperOrders.Single());
        Assert.Equal(fixture.ExpiryRequest.SkippedRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task Expiry_PostgresRoundedCreatedAtTimestamp_IsEligible()
    {
        var callerCreatedAtUtc = DateTimeOffset.Parse(
            "2026-08-27T05:59:33.1706318+00:00",
            CultureInfo.InvariantCulture);
        var persistedCreatedAtUtc = DateTimeOffset.Parse(
            "2026-08-27T05:59:33.1706320+00:00",
            CultureInfo.InvariantCulture);
        var fixture = CreateFixture(createdAtUtc: callerCreatedAtUtc);
        var repository = Seed(fixture);
        repository.PaperOrders[0] = fixture.PendingOrder with { CreatedAtUtc = persistedCreatedAtUtc };

        var result = await repository.TryExpireMakerGtdPaperOrderAsync(fixture.ExpiryRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.Applied, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExpiryApplied, result.ReasonCode);
        Assert.Equal(PaperOrderStatus.Expired, repository.PaperOrders.Single().Status);
    }

    [Fact]
    public async Task Expiry_BeforeExpiryTime_IsNotEligible()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var evaluatedAtUtc = fixture.PendingOrder.ExpiresAtUtc.AddTicks(-10);
        var request = fixture.ExpiryRequest with
        {
            EvaluatedAtUtc = evaluatedAtUtc,
            SkippedRun = fixture.ExpiryRequest.SkippedRun with { UpdatedAtUtc = evaluatedAtUtc }
        };

        var result = await repository.TryExpireMakerGtdPaperOrderAsync(request);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExpiryNotReached, result.ReasonCode);
        AssertRepositoryStillResting(repository, fixture);
    }

    [Fact]
    public async Task Expiry_WithExistingFill_IsNotEligible()
    {
        var fixture = CreateFixture();
        var repository = Seed(fixture);
        var existingFill = fixture.FullFillRequest.Fill with { Id = Guid.NewGuid(), Evidence = "pre-existing" };
        repository.PaperFills.Add(existingFill);

        var result = await repository.TryExpireMakerGtdPaperOrderAsync(fixture.ExpiryRequest);

        Assert.Equal(MakerGtdPaperMutationOutcome.NotEligible, result.Outcome);
        Assert.Equal(MakerGtdPaperMutationReasonCodes.ExistingFillConflict, result.ReasonCode);
        Assert.Equal(fixture.PendingOrder, repository.PaperOrders.Single());
        Assert.Equal(fixture.RestingRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Equal(existingFill, repository.PaperFills.Single());
    }

    [Fact]
    public void PostgresImplementation_UsesOneTransactionWithRequiredLocksAndExactWrites()
    {
        var source = ReadPostgresSource().Replace("\r\n", "\n", StringComparison.Ordinal);
        var expiryMethodStart = source.IndexOf(
            "public async Task<MakerGtdPaperMutationResult> TryExpireMakerGtdPaperOrderAsync",
            StringComparison.Ordinal);
        Assert.True(expiryMethodStart > 0);
        var fillSection = source[..expiryMethodStart];
        var expirySection = source[expiryMethodStart..];

        Assert.Contains("BeginTransactionAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("LockPaperWalletsAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("forUpdate: true", fillSection, StringComparison.Ordinal);
        Assert.Contains("ReadMakerGtdStrategyRunAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("ReadPaperFillsForReconciliationAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("InsertMakerGtdPaperFillAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("UpdatePaperOrderForReconciliationAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("UpsertPaperPositionsBatchAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("UpdateMakerGtdStrategyRunAsync", fillSection, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", fillSection, StringComparison.Ordinal);

        Assert.Contains("BeginTransactionAsync", expirySection, StringComparison.Ordinal);
        Assert.Contains("forUpdate: true", expirySection, StringComparison.Ordinal);
        Assert.Contains("ReadMakerGtdStrategyRunAsync", expirySection, StringComparison.Ordinal);
        Assert.Contains("ReadPaperFillsForReconciliationAsync", expirySection, StringComparison.Ordinal);
        Assert.Contains("UpdatePaperOrderForReconciliationAsync", expirySection, StringComparison.Ordinal);
        Assert.Contains("UpdateMakerGtdStrategyRunAsync", expirySection, StringComparison.Ordinal);
        Assert.Contains("AddMakerGtdStrategyRunParameters", expirySection, StringComparison.Ordinal);
        Assert.Contains("run.SkipDiagnosticsJson ?? DBNull.Value", expirySection, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE;", expirySection, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", expirySection, StringComparison.Ordinal);
    }

    private static MakerGtdFixture CreateFixture(
        DateTimeOffset? fillAtUtc = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var actualCreatedAtUtc = createdAtUtc ?? new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var expiresAtUtc = actualCreatedAtUtc.AddMinutes(4);
        var actualFillAtUtc = fillAtUtc ?? actualCreatedAtUtc.AddSeconds(30);
        var strategyId = Guid.Parse("87c50005-0000-4000-8223-000000000105");
        var signalId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var orderId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var runId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var wallet = "strategy:eth_reference_average_maker_gtd";
        var pendingOrder = new PaperOrder(
            orderId,
            signalId,
            wallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.48m,
            10m,
            4.80m,
            actualCreatedAtUtc,
            expiresAtUtc,
            StrategyId: strategyId,
            RawDecisionJson: "{\"mode\":\"maker_gtd\"}",
            CorrelationId: Guid.Parse("40000000-0000-0000-0000-000000000001"),
            ExecutionSource: ExecutionSource);
        var restingRun = new StrategyMarketPaperRun(
            runId,
            strategyId,
            "market-1",
            pendingOrder.ConditionId,
            "eth-updown-5m-1",
            "ETH Up or Down",
            "Crypto",
            actualCreatedAtUtc.AddMinutes(1),
            expiresAtUtc.AddMinutes(1),
            actualCreatedAtUtc.AddSeconds(-5),
            actualCreatedAtUtc,
            StrategyMarketPaperRunStatuses.Resting,
            pendingOrder.AssetId,
            pendingOrder.Outcome,
            pendingOrder.Price,
            pendingOrder.NotionalUsd,
            pendingOrder.SizeShares,
            signalId,
            orderId,
            null,
            null,
            null,
            null,
            null,
            null,
            actualCreatedAtUtc.AddSeconds(-5),
            actualCreatedAtUtc);
        var fill = new PaperFill(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            orderId,
            pendingOrder.Price,
            pendingOrder.SizeShares,
            actualFillAtUtc,
            "authoritative_trade_crossed_buy_limit",
            FeeUsd: 0.02m,
            FeeAccountingStatus: "Calculated",
            FeeLiquidityRole: "Maker",
            FeeCalculationSource: "test_maker_fee",
            FeeRate: 0.01m,
            FeeExponent: 2,
            FeeTakerOnly: false,
            FeeCalculatedAtUtc: actualFillAtUtc,
            NetRealizedPnlUsd: -0.02m);
        var filledOrder = pendingOrder with
        {
            Status = PaperOrderStatus.Filled,
            FilledAtUtc = actualFillAtUtc
        };
        var position = new PaperPosition(
            pendingOrder.AssetId,
            pendingOrder.ConditionId,
            pendingOrder.Outcome,
            pendingOrder.SizeShares,
            pendingOrder.Price,
            5m,
            0.20m,
            actualFillAtUtc,
            wallet,
            fill.FeeUsd,
            fill.FeeAccountingStatus,
            fill.FeeLiquidityRole,
            fill.FeeCalculationSource,
            fill.FeeRate,
            fill.FeeExponent,
            fill.FeeTakerOnly,
            fill.FeeCalculatedAtUtc,
            0.18m);
        var enteredRun = restingRun with
        {
            Status = StrategyMarketPaperRunStatuses.Entered,
            EntryPrice = fill.Price,
            StakeUsd = pendingOrder.NotionalUsd,
            SizeShares = fill.SizeShares,
            EnteredAtUtc = actualFillAtUtc,
            UpdatedAtUtc = actualFillAtUtc,
            FeeUsd = fill.FeeUsd,
            FeeAccountingStatus = fill.FeeAccountingStatus,
            FeeLiquidityRole = fill.FeeLiquidityRole,
            FeeCalculationSource = fill.FeeCalculationSource,
            FeeRate = fill.FeeRate,
            FeeExponent = fill.FeeExponent,
            FeeTakerOnly = fill.FeeTakerOnly,
            FeeCalculatedAtUtc = fill.FeeCalculatedAtUtc
        };
        var fullFillRequest = new MakerGtdPaperFullFillRequest(
            ExecutionSource,
            filledOrder,
            fill,
            ExpectedPosition: null,
            position,
            enteredRun);
        var expiredOrder = pendingOrder with { Status = PaperOrderStatus.Expired };
        var skippedRun = restingRun with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "maker_gtd_expired_without_cross",
            SkipDiagnosticsJson = "{\"evidence\":\"no_cross\",\"attempts\":10}",
            UpdatedAtUtc = expiresAtUtc
        };
        var expiryRequest = new MakerGtdPaperExpiryRequest(
            ExecutionSource,
            expiresAtUtc,
            expiredOrder,
            skippedRun);
        return new MakerGtdFixture(pendingOrder, restingRun, fullFillRequest, expiryRequest);
    }

    private static Signal CreateSignal(PaperOrder order)
    {
        var leaderTrade = new LeaderTrade(
            order.CopiedTraderWallet,
            "Maker-GTD PostgreSQL timestamp regression",
            order.ConditionId,
            order.AssetId,
            "maker-gtd-postgres-timestamp-regression",
            "Maker-GTD PostgreSQL timestamp regression",
            order.Outcome,
            order.Side,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            order.CreatedAtUtc);
        return new Signal(
            order.SignalId,
            leaderTrade,
            100,
            true,
            "maker_gtd_postgres_timestamp_regression",
            [],
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            order.CreatedAtUtc);
    }

    private static async Task AddStrategyFixtureAsync(
        PostgresConnectionFactory factory,
        PaperOrder order)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
INSERT INTO strategies (id, code, name, created_at_utc, updated_at_utc)
VALUES (@Id, @Code, @Name, @CreatedAtUtc, @CreatedAtUtc);
""",
            connection);
        command.Parameters.AddWithValue("Id", order.StrategyId);
        command.Parameters.AddWithValue("Code", $"maker_gtd_timestamp_{order.StrategyId:N}");
        command.Parameters.AddWithValue("Name", $"Maker-GTD timestamp {order.StrategyId:N}");
        command.Parameters.AddWithValue("CreatedAtUtc", order.CreatedAtUtc);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static TestAppRepository Seed(MakerGtdFixture fixture)
    {
        var repository = new TestAppRepository();
        repository.PaperOrders.Add(fixture.PendingOrder);
        repository.StrategyMarketPaperRuns.Add(fixture.RestingRun);
        return repository;
    }

    private static void AssertRepositoryStillResting(
        TestAppRepository repository,
        MakerGtdFixture fixture,
        DateTimeOffset? expectedCreatedAtUtc = null)
    {
        var expectedPendingOrder = expectedCreatedAtUtc is { } createdAtUtc
            ? fixture.PendingOrder with { CreatedAtUtc = createdAtUtc }
            : fixture.PendingOrder;
        Assert.Equal(expectedPendingOrder, repository.PaperOrders.Single());
        Assert.Equal(fixture.RestingRun, repository.StrategyMarketPaperRuns.Single());
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    private static string ReadPostgresSource([CallerFilePath] string testFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(testFilePath)
            ?? throw new InvalidOperationException("The test source directory was not resolved.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(testsDirectory, "..", ".."));
        return File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "PolyCopyTrader.Storage",
            "PostgresAppRepository.MakerGtdPaper.cs"));
    }

    private sealed record MakerGtdFixture(
        PaperOrder PendingOrder,
        StrategyMarketPaperRun RestingRun,
        MakerGtdPaperFullFillRequest FullFillRequest,
        MakerGtdPaperExpiryRequest ExpiryRequest);
}
