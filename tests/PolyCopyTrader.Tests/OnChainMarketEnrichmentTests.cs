using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.OnChain;

namespace PolyCopyTrader.Tests;

public sealed class OnChainMarketEnrichmentTests
{
    [Fact]
    public async Task Processor_EnrichesMissingTokenMetadataAndStoresNotFoundTokens()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-2"));
        QueueMetadataRefresh(repository, "token-1", "token-2");
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] =
        [
            Metadata("token-1")
        ];

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(2, result.TokensRequested);
        Assert.Equal(1, result.TokensResolved);
        Assert.Equal(1, result.TokensNotFound);
        Assert.Equal(1, result.BatchesRun);
        Assert.False(result.ReachedBatchLimit);
        Assert.Equal(2, repository.PolymarketOnChainTokenMetadata.Count);
        Assert.Contains(repository.PolymarketOnChainTokenMetadata, item => item.TokenId == "token-1" && item.LookupSucceeded);
        Assert.Contains(repository.PolymarketOnChainTokenMetadata, item => item.TokenId == "token-2" && !item.LookupSucceeded);
        Assert.Contains(gamma.Requests, item => item == ("token-1", false));
        Assert.Contains(gamma.Requests, item => item == ("token-2", true));
    }

    [Fact]
    public async Task Processor_RepeatsBatchesUntilMissingTokensAreDone()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-2"));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-3"));
        QueueMetadataRefresh(repository, "token-1", "token-2", "token-3");
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] = [Metadata("token-1")];
        gamma.Metadata["token-2"] = [Metadata("token-2")];
        gamma.Metadata["token-3"] = [Metadata("token-3")];

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 1,
                MarketEnrichmentMaxBatchesPerRun = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(3, result.TokensRequested);
        Assert.Equal(3, result.TokensResolved);
        Assert.Equal(0, result.TokensNotFound);
        Assert.Equal(3, result.MetadataRowsStored);
        Assert.Equal(3, result.BatchesRun);
        Assert.False(result.ReachedBatchLimit);
        Assert.Empty(await repository.GetOnChainTokenIdsMissingMetadataAsync());
    }

    [Fact]
    public async Task Processor_ReenrichesTokenMetadataWithMissingCategory()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        repository.PolymarketOnChainTokenMetadata.Add(Metadata("token-1", category: null));
        QueueMetadataRefresh(repository, "token-1");
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] = [Metadata("token-1")];

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.TokensRequested);
        Assert.Equal(1, result.TokensResolved);
        Assert.Contains(gamma.Requests, item => item == ("token-1", false));
        Assert.Contains(repository.PolymarketOnChainTokenMetadata, item =>
            item.TokenId == "token-1" &&
            item.LookupSucceeded &&
            item.Category == "Politics");
    }

    [Fact]
    public async Task Processor_UsesConditionFallbackWhenTokenLookupHasNoCategory()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        QueueMetadataRefresh(repository, "token-1");
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] = [Metadata("token-1", category: null)];
        gamma.MetadataByCondition["condition-1"] = [Metadata("token-1", category: "Politics")];
        var clob = new FakeClobClient
        {
            MarketsByToken =
            {
                ["token-1"] = new PolymarketClobMarketByToken("condition-1", "token-1", "token-1-no")
            }
        };

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            clob,
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.TokensRequested);
        Assert.Equal(1, result.TokensResolved);
        Assert.Contains(clob.Requests, tokenId => tokenId == "token-1");
        Assert.Contains(gamma.ConditionRequests, item => item == ("condition-1", "token-1", false));
        Assert.Contains(repository.PolymarketOnChainTokenMetadata, item =>
            item.TokenId == "token-1" &&
            item.LookupSucceeded &&
            item.Category == "Politics");
    }

    [Fact]
    public async Task Processor_UsesEventCategoryFallbackWhenMarketLookupHasEventWithoutCategory()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        QueueMetadataRefresh(repository, "token-1");
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] = [Metadata("token-1", category: null) with { RawJson = MarketRawJson("event-1") }];
        gamma.EventCategories["event-1"] = "Politics";

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(1, result.TokensRequested);
        Assert.Equal(1, result.TokensResolved);
        Assert.Contains(gamma.EventRequests, eventId => eventId == "event-1");
        Assert.Contains(repository.PolymarketOnChainTokenMetadata, item =>
            item.TokenId == "token-1" &&
            item.LookupSucceeded &&
            item.Category == "Politics");
    }

    [Fact]
    public async Task Processor_StopsAtBatchLimitWhenMissingTokensRemain()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-2"));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-3"));
        QueueMetadataRefresh(repository, "token-1", "token-2", "token-3");
        var gamma = new FakeGammaClient();

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 1,
                MarketEnrichmentMaxBatchesPerRun = 2,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(2, result.TokensRequested);
        Assert.Equal(0, result.TokensResolved);
        Assert.Equal(2, result.TokensNotFound);
        Assert.Equal(2, result.MetadataRowsStored);
        Assert.Equal(2, result.BatchesRun);
        Assert.True(result.ReachedBatchLimit);
        Assert.Contains("token-3", await repository.GetOnChainTokenIdsMissingMetadataAsync());
    }

    [Fact]
    public async Task Processor_RejectsConcurrentRefresh()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        QueueMetadataRefresh(repository, "token-1");
        var beforeReturnGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gamma = new FakeGammaClient
        {
            BeforeReturnGate = beforeReturnGate
        };

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 1,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var firstRun = processor.RefreshAsync();
        await gamma.RequestObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.RefreshAsync());

        Assert.Contains("already running", ex.Message, StringComparison.OrdinalIgnoreCase);
        beforeReturnGate.SetResult(null);
        await firstRun;
    }

    [Fact]
    public async Task Processor_UsesMetadataRefreshQueueInsteadOfScanningExecutions()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("token-1"));
        var gamma = new FakeGammaClient();
        gamma.Metadata["token-1"] = [Metadata("token-1")];

        var processor = new OnChainMarketEnrichmentProcessor(
            NullLogger<OnChainMarketEnrichmentProcessor>.Instance,
            new OnChainIngestionOptions
            {
                MarketEnrichmentBatchSize = 10,
                RequestDelayMilliseconds = 0
            },
            gamma,
            new FakeClobClient(),
            repository);

        var result = await processor.RefreshAsync();

        Assert.Equal(0, result.TokensRequested);
        Assert.Empty(gamma.Requests);
        Assert.Empty(repository.PolymarketOnChainTokenMetadata);
    }

    private static PolymarketOnChainTokenMetadata Metadata(string tokenId, string? category = "Politics")
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
            DateTimeOffset.UtcNow.AddDays(1),
            true,
            false,
            false,
            false,
            null,
            [tokenId, tokenId + "-no"],
            ["Yes", "No"],
            true,
            null,
            "{}",
            DateTimeOffset.UtcNow);
    }

    private static PolymarketOnChainWalletExecution Execution(string tokenId)
    {
        return new PolymarketOnChainWalletExecution(
            "CTF Exchange V2",
            "0xexchange",
            "V2",
            1,
            DateTimeOffset.UtcNow,
            "0xtx",
            0,
            0,
            "0xwallet",
            TradeSide.Buy,
            tokenId,
            1,
            1,
            0,
            10,
            5,
            0.5m,
            0,
            DateTimeOffset.UtcNow);
    }

    private static void QueueMetadataRefresh(TestAppRepository repository, params string[] tokenIds)
    {
        foreach (var tokenId in tokenIds)
        {
            repository.PolymarketOnChainTokenMetadataRefreshQueue.Add(tokenId);
        }
    }

    private static string MarketRawJson(string eventId)
    {
        return $$"""
{
  "events": [
    {
      "id": "{{eventId}}"
    }
  ]
}
""";
    }

    private sealed class FakeGammaClient : IPolymarketGammaClient
    {
        public Dictionary<string, IReadOnlyList<PolymarketOnChainTokenMetadata>> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<PolymarketOnChainTokenMetadata>> MetadataByCondition { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string?> EventCategories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string TokenId, bool Closed)> Requests { get; } = [];

        public List<(string ConditionId, string TokenId, bool Closed)> ConditionRequests { get; } = [];

        public List<string> EventRequests { get; } = [];

        public TaskCompletionSource<object?> RequestObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?>? BeforeReturnGate { get; init; }

        public async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((tokenId, closed));
            RequestObserved.TrySetResult(null);
            if (BeforeReturnGate is not null)
            {
                await BeforeReturnGate.Task.WaitAsync(cancellationToken);
            }

            return !closed && Metadata.TryGetValue(tokenId, out var metadata)
                ? metadata
                : [];
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            ConditionRequests.Add((conditionId, requestedTokenId, closed));
            return Task.FromResult(!closed && MetadataByCondition.TryGetValue(conditionId, out var metadata)
                ? metadata
                : []);
        }

        public Task<string?> GetEventCategoryAsync(
            string eventId,
            CancellationToken cancellationToken = default)
        {
            EventRequests.Add(eventId);
            return Task.FromResult(EventCategories.TryGetValue(eventId, out var category) ? category : null);
        }
    }

    private sealed class FakeClobClient : IPolymarketClobPublicClient
    {
        public Dictionary<string, PolymarketClobMarketByToken> MarketsByToken { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Requests { get; } = [];

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<OrderBookSnapshot?>(null);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            Requests.Add(tokenId);
            return Task.FromResult(MarketsByToken.TryGetValue(tokenId, out var market) ? market : null);
        }
    }
}
