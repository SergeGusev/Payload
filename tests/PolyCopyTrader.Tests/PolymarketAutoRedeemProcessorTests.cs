using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.AutoRedeem;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketAutoRedeemProcessorTests
{
    private const string Wallet = "0x1111111111111111111111111111111111111111";

    [Fact]
    public async Task ProcessAsync_RecordsDryRunAttemptForRedeemableBinaryPosition()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('a', 64);
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId)]));

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, result.PositionsFetched);
        Assert.Equal(1, result.RedeemablePositions);
        Assert.Equal(1, result.AttemptsRecorded);
        Assert.Equal(0, result.Skipped);
        var attempt = Assert.Single(repository.PolymarketAutoRedeemAttempts);
        Assert.Equal(Wallet, attempt.Wallet);
        Assert.Equal(conditionId, attempt.ConditionId);
        Assert.Equal(PolymarketAutoRedeemStatuses.DryRunReady, attempt.Status);
        Assert.True(attempt.DryRun);
        Assert.False(attempt.AutoSubmitEnabled);
        Assert.Equal("0x4D97DCd97eC945f40cF65F87097ACe5EA0476045", attempt.TargetContract);
        Assert.Equal([1, 2], attempt.IndexSets);
        Assert.StartsWith("0x01b7037c", attempt.Calldata, StringComparison.Ordinal);
        Assert.Null(attempt.LastError);
    }

    [Fact]
    public async Task ProcessAsync_SkipsNegativeRiskPositionWithExplicitReason()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('b', 64);
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId) with { NegativeRisk = true }]));

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, result.AttemptsRecorded);
        Assert.Equal(1, result.Skipped);
        var attempt = Assert.Single(repository.PolymarketAutoRedeemAttempts);
        Assert.Equal(PolymarketAutoRedeemStatuses.SkippedUnsupported, attempt.Status);
        Assert.Contains("Negative-risk", attempt.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, attempt.Calldata);
    }

    [Fact]
    public async Task ProcessAsync_UsesSizeForRedeemablePositionWhenCurrentValueIsZero()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('e', 64);
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId) with { CurrentValue = 0m, Size = 6.25m }]));

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, result.RedeemablePositions);
        var attempt = Assert.Single(repository.PolymarketAutoRedeemAttempts);
        Assert.Equal(PolymarketAutoRedeemStatuses.DryRunReady, attempt.Status);
        Assert.Equal(6.25m, attempt.RedeemableValueUsd);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotReplaceSubmittedAttempt()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('c', 64);
        repository.PolymarketAutoRedeemAttempts.Add(new PolymarketAutoRedeemAttempt(
            Guid.NewGuid(),
            Wallet,
            null,
            conditionId,
            "asset",
            "slug",
            "title",
            "Up",
            0,
            1m,
            1m,
            PolymarketAutoRedeemStatuses.Submitted,
            false,
            true,
            "0x4D97DCd97eC945f40cF65F87097ACe5EA0476045",
            "0xsubmitted",
            "0xC011a7E12a19f7B1f670d46F03B03f3342E82DFB",
            "0x" + new string('0', 64),
            [1, 2],
            "relayer-id",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(-10),
            null,
            DateTimeOffset.UtcNow.AddMinutes(-10),
            "{}"));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId)]));

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(0, result.AttemptsRecorded);
        Assert.Equal(1, result.Skipped);
        Assert.Equal("0xsubmitted", Assert.Single(repository.PolymarketAutoRedeemAttempts).Calldata);
    }

    [Fact]
    public async Task ProcessAsync_SubmitsRedeemableDepositWalletAttemptWhenLiveSubmitEnabled()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('d', 64);
        var relayerClient = new FakeRelayerClient(new PolymarketRelayerSubmissionResult("relayer-1", "STATE_NEW", null));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId)]),
            new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = false,
                AutoSubmitEnabled = true,
                ManualEnableCode = "AUTO_REDEEM_ENABLED",
                WalletAddress = Wallet,
                WalletType = "WALLET",
                CurrentPositionsLimit = 500,
                MaxPositionPages = 1,
                MaxClaimsPerCycle = 10,
                MinRedeemableValueUsd = 0.01m
            },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x2222222222222222222222222222222222222222",
                FunderAddress = Wallet,
                SignatureType = "POLY_1271"
            },
            relayerClient);

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(1, result.Submitted);
        var attempt = Assert.Single(repository.PolymarketAutoRedeemAttempts);
        Assert.Equal(PolymarketAutoRedeemStatuses.Submitted, attempt.Status);
        Assert.False(attempt.DryRun);
        Assert.Equal("relayer-1", attempt.RelayerTransactionId);
        Assert.Null(attempt.LastError);
        Assert.Equal(Wallet, relayerClient.DepositWalletAddress);
        Assert.Equal("0x2222222222222222222222222222222222222222", relayerClient.OwnerAddress);
        Assert.Single(relayerClient.Calls);
        Assert.Equal(1, relayerClient.SubmissionCount);
    }

    [Fact]
    public async Task ProcessAsync_ThrottlesLiveRelayerSubmissionsPerCycle()
    {
        var repository = new TestAppRepository();
        var conditionId1 = "0x" + new string('d', 64);
        var conditionId2 = "0x" + new string('e', 64);
        var relayerClient = new FakeRelayerClient(new PolymarketRelayerSubmissionResult("relayer-1", "STATE_NEW", null));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient(
            [
                RedeemablePosition(conditionId1),
                RedeemablePosition(conditionId2) with { AssetId = "asset-2", MarketSlug = "market-slug-2" }
            ]),
            new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = false,
                AutoSubmitEnabled = true,
                ManualEnableCode = "AUTO_REDEEM_ENABLED",
                WalletAddress = Wallet,
                WalletType = "WALLET",
                CurrentPositionsLimit = 500,
                MaxPositionPages = 1,
                MaxClaimsPerCycle = 10,
                MaxLiveSubmissionsPerCycle = 1,
                MinRedeemableValueUsd = 0.01m
            },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x2222222222222222222222222222222222222222",
                FunderAddress = Wallet,
                SignatureType = "POLY_1271"
            },
            relayerClient);

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(2, result.AttemptsRecorded);
        Assert.Equal(1, result.Submitted);
        Assert.Equal(1, relayerClient.SubmissionCount);
        Assert.Contains(repository.PolymarketAutoRedeemAttempts, attempt => attempt.Status == PolymarketAutoRedeemStatuses.Submitted);
        Assert.Contains(repository.PolymarketAutoRedeemAttempts, attempt => attempt.Status == PolymarketAutoRedeemStatuses.SubmitPending);
    }

    [Fact]
    public async Task ProcessAsync_KeepsTransientRelayerFailurePendingForRetry()
    {
        var repository = new TestAppRepository();
        var conditionId = "0x" + new string('f', 64);
        var relayerClient = new FakeRelayerClient(
            new PolymarketRelayerSubmissionResult("unused", "STATE_NEW", null),
            new PolymarketApiException("test", "SubmitDepositWalletBatch", "Relayer submit failed with HTTP 400: {\"error\":\"wallet busy: active action exists\"}"));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([RedeemablePosition(conditionId)]),
            new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = false,
                AutoSubmitEnabled = true,
                ManualEnableCode = "AUTO_REDEEM_ENABLED",
                WalletAddress = Wallet,
                WalletType = "WALLET",
                CurrentPositionsLimit = 500,
                MaxPositionPages = 1,
                MaxClaimsPerCycle = 10,
                MaxLiveSubmissionsPerCycle = 1,
                MinRedeemableValueUsd = 0.01m
            },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x2222222222222222222222222222222222222222",
                FunderAddress = Wallet,
                SignatureType = "POLY_1271"
            },
            relayerClient);

        var result = await processor.ProcessAsync(CancellationToken.None);

        Assert.Equal(0, result.Submitted);
        var attempt = Assert.Single(repository.PolymarketAutoRedeemAttempts);
        Assert.Equal(PolymarketAutoRedeemStatuses.SubmitRetryPending, attempt.Status);
        Assert.Contains("wallet busy", attempt.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, relayerClient.SubmissionCount);
    }

    private static PolymarketAutoRedeemProcessor CreateProcessor(
        TestAppRepository repository,
        IPolymarketDataApiClient dataApiClient,
        PolymarketAutoRedeemOptions? options = null,
        PolymarketAuthOptions? authOptions = null,
        IPolymarketRelayerClient? relayerClient = null)
    {
        return new PolymarketAutoRedeemProcessor(
            NullLogger<PolymarketAutoRedeemProcessor>.Instance,
            options ?? new PolymarketAutoRedeemOptions
            {
                Enabled = true,
                DryRun = true,
                AutoSubmitEnabled = false,
                WalletAddress = Wallet,
                CurrentPositionsLimit = 500,
                MaxPositionPages = 1,
                MaxClaimsPerCycle = 10,
                MinRedeemableValueUsd = 0.01m
            },
            authOptions ?? new PolymarketAuthOptions(),
            dataApiClient,
            new PolymarketRedeemCalldataBuilder(),
            relayerClient ?? new FakeRelayerClient(new PolymarketRelayerSubmissionResult("unused", "STATE_NEW", null)),
            repository);
    }

    private static PolymarketDataApiPosition RedeemablePosition(string conditionId)
    {
        return new PolymarketDataApiPosition(
            Wallet,
            PolymarketDataApiPositionStatus.Open,
            "asset-1",
            conditionId,
            6m,
            0.5m,
            3m,
            6m,
            3m,
            100m,
            6m,
            0m,
            0m,
            1m,
            DateTimeOffset.UtcNow,
            "Market title",
            "market-slug",
            null,
            "event-1",
            "event-slug",
            "Crypto",
            "Up",
            0,
            "Down",
            "asset-2",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            true,
            false,
            false,
            """{"conditionId":"test"}""");
    }

    private sealed class FakeDataApiClient(IReadOnlyList<PolymarketDataApiPosition> currentPositions) : IPolymarketDataApiClient
    {
        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderPosition>>([]);
        }

        public Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserCurrentPositionsAsync(
            string wallet,
            int limit = 500,
            int offset = 0,
            string sortBy = "CURRENT",
            string sortDirection = "DESC",
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(offset == 0 ? currentPositions : []);
        }
    }

    private sealed class FakeRelayerClient(
        PolymarketRelayerSubmissionResult result,
        Exception? exception = null) : IPolymarketRelayerClient
    {
        public string? OwnerAddress { get; private set; }

        public string? DepositWalletAddress { get; private set; }

        public IReadOnlyList<PolymarketDepositWalletCall> Calls { get; private set; } = [];

        public int SubmissionCount { get; private set; }

        public Task<PolymarketRelayerSubmissionResult> SubmitDepositWalletBatchAsync(
            string ownerAddress,
            string depositWalletAddress,
            IReadOnlyList<PolymarketDepositWalletCall> calls,
            string? metadata,
            CancellationToken cancellationToken = default)
        {
            SubmissionCount++;
            OwnerAddress = ownerAddress;
            DepositWalletAddress = depositWalletAddress;
            Calls = calls;
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result);
        }
    }
}
