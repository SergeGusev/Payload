using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.OnChain;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class OnChainSignalCandidateTests
{
    [Fact]
    public async Task RefreshAsync_AcceptsSmallBuyFillWithKnownCategoryAndStrongPerformance()
    {
        var repository = new TestAppRepository();
        var sourceFillId = Guid.NewGuid();
        repository.PolymarketOnChainWalletFills.Add(WalletFill(sourceFillId, TradeSide.Buy, 5m, DateTimeOffset.UtcNow.AddDays(-7)));
        repository.PolymarketOnChainTokenMetadata.Add(TokenMetadata("token-yes", "Politics", active: true));
        repository.PolymarketOnChainWalletCategoryPerformance.Add(CategoryPerformance("0xleader", "Politics"));

        var processor = CreateProcessor(repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.SourcesQueued);
        Assert.Equal(1, result.SourcesFetched);
        Assert.Equal(1, result.Accepted);
        var candidate = Assert.Single(repository.PolymarketOnChainSignalCandidates);
        Assert.Equal("Accepted", candidate.DecisionStatus);
        Assert.Equal("onchain_candidate_ready", candidate.DecisionCode);
        Assert.Equal("Politics", candidate.Category);
        Assert.Equal(100m, candidate.LeaderCategoryScore);
        Assert.Empty(repository.PolymarketOnChainSignalCandidateReasons);
    }

    [Fact]
    public async Task RefreshAsync_RejectsWhenCategoryOrPerformanceIsMissing()
    {
        var repository = new TestAppRepository();
        var sourceFillId = Guid.NewGuid();
        repository.PolymarketOnChainWalletFills.Add(WalletFill(sourceFillId, TradeSide.Buy, 250m));
        repository.PolymarketOnChainTokenMetadata.Add(TokenMetadata("token-yes", null, active: true));

        var processor = CreateProcessor(repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.Rejected);
        var candidate = Assert.Single(repository.PolymarketOnChainSignalCandidates);
        Assert.Equal("Rejected", candidate.DecisionStatus);
        Assert.Equal(SignalReasonCodes.MissingMarketCategory, candidate.DecisionCode);
        Assert.Contains(repository.PolymarketOnChainSignalCandidateReasons, reason =>
            reason.CandidateId == candidate.Id &&
            reason.ReasonCode == SignalReasonCodes.MissingMarketCategory);
    }

    [Fact]
    public async Task RefreshAsync_RejectsSellFillsWithExplicitReason()
    {
        var repository = new TestAppRepository();
        var sourceFillId = Guid.NewGuid();
        repository.PolymarketOnChainWalletFills.Add(WalletFill(sourceFillId, TradeSide.Sell, 5m));
        repository.PolymarketOnChainTokenMetadata.Add(TokenMetadata("token-yes", "Politics", active: true));
        repository.PolymarketOnChainWalletCategoryPerformance.Add(CategoryPerformance("0xleader", "Politics"));

        var processor = CreateProcessor(repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.Rejected);
        var candidate = Assert.Single(repository.PolymarketOnChainSignalCandidates);
        Assert.Equal(SignalReasonCodes.UnsupportedSide, candidate.DecisionCode);
        Assert.Contains(repository.PolymarketOnChainSignalCandidateReasons, reason =>
            reason.ReasonCode == SignalReasonCodes.UnsupportedSide);
        Assert.DoesNotContain(repository.PolymarketOnChainSignalCandidateReasons, reason =>
            reason.ReasonCode == SignalReasonCodes.LeaderTradeTooSmall);
    }

    [Fact]
    public async Task RefreshAsync_BackfillsHistoricalRowsAcrossQueueBatches()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletFills.Add(WalletFill(Guid.NewGuid(), TradeSide.Buy, 250m, DateTimeOffset.UtcNow.AddDays(-10)));
        repository.PolymarketOnChainWalletFills.Add(WalletFill(Guid.NewGuid(), TradeSide.Buy, 250m, DateTimeOffset.UtcNow.AddDays(-9)));
        repository.PolymarketOnChainWalletFills.Add(WalletFill(Guid.NewGuid(), TradeSide.Buy, 250m, DateTimeOffset.UtcNow.AddDays(-8)));
        repository.PolymarketOnChainTokenMetadata.Add(TokenMetadata("token-yes", "Politics", active: true));
        repository.PolymarketOnChainWalletCategoryPerformance.Add(CategoryPerformance("0xleader", "Politics"));

        var processor = CreateProcessor(repository, signalCandidateBatchSize: 1, signalCandidateQueueSeedBatchSize: 2);

        await processor.RefreshAsync();
        await processor.RefreshAsync();
        await processor.RefreshAsync();

        Assert.Equal(3, repository.PolymarketOnChainSignalCandidates.Count);
        Assert.All(repository.PolymarketOnChainSignalCandidates, candidate =>
            Assert.Equal("Accepted", candidate.DecisionStatus));
        Assert.Empty(repository.PolymarketOnChainSignalCandidateRefreshQueue);
    }

    private static OnChainSignalCandidateProcessor CreateProcessor(
        TestAppRepository repository,
        int signalCandidateBatchSize = 100,
        int signalCandidateQueueSeedBatchSize = 100)
    {
        return new OnChainSignalCandidateProcessor(
            new OnChainIngestionOptions
            {
                Enabled = true,
                BackgroundSignalCandidateRefreshEnabled = true,
                SignalCandidateBatchSize = signalCandidateBatchSize,
                SignalCandidateQueueSeedBatchSize = signalCandidateQueueSeedBatchSize,
                SignalCandidateRetryBatchSize = 100
            },
            new SignalOptions
            {
                MinLeaderCategoryResolvedPositions = 3,
                MinLeaderCategorySampleQuality = "Low",
                MinLeaderCategoryResolvedRoiPct = 0m,
                MinLeaderCategoryWinRatePct = 50m,
                MinLeaderCategoryScore = 0m,
                LeaderCategoryPerformanceStaleAfterHours = 24
            },
            repository);
    }

    private static PolymarketOnChainWalletFill WalletFill(
        Guid sourceFillId,
        TradeSide side,
        decimal notionalUsd,
        DateTimeOffset? blockTimestampUtc = null)
    {
        var timestamp = blockTimestampUtc ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        return new PolymarketOnChainWalletFill(
            sourceFillId,
            "CTF Exchange V1",
            "0xcontract",
            "V1",
            123,
            timestamp,
            "0xtx",
            1,
            "0xorder",
            OnChainParticipantRole.Maker,
            "0xleader",
            "0xcounterparty",
            side,
            "token-yes",
            0.50m,
            notionalUsd / 0.50m,
            notionalUsd,
            0m,
            "0",
            DateTimeOffset.UtcNow);
    }

    private static PolymarketOnChainTokenMetadata TokenMetadata(
        string tokenId,
        string? category,
        bool active)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            "condition-1",
            "market-1",
            "market-slug",
            "Market title",
            "Yes",
            0,
            category,
            DateTimeOffset.UtcNow.AddDays(7),
            active,
            false,
            false,
            false,
            null,
            [tokenId, "token-no"],
            ["Yes", "No"],
            true,
            null,
            "{}",
            DateTimeOffset.UtcNow);
    }

    private static PolymarketOnChainWalletCategoryPerformance CategoryPerformance(
        string wallet,
        string category)
    {
        return new PolymarketOnChainWalletCategoryPerformance(
            wallet,
            category,
            PositionsCount: 10,
            OpenPositions: 2,
            FlatPositions: 3,
            ResolvedPositions: 5,
            ProfitableResolvedPositions: 3,
            LosingResolvedPositions: 2,
            MarketsTraded: 8,
            VolumeUsd: 2_000m,
            ResolvedVolumeUsd: 1_000m,
            OpenExposureUsd: 100m,
            ResolvedCostUsd: 500m,
            ResolvedPnlUsd: 50m,
            ResolvedRoiPct: 10m,
            WinRatePct: 60m,
            AveragePositionSizeUsd: 200m,
            Score: 100m,
            SampleQuality: "Low",
            FirstActiveUtc: DateTimeOffset.UtcNow.AddDays(-10),
            LastActiveUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            RefreshedAtUtc: DateTimeOffset.UtcNow);
    }
}
