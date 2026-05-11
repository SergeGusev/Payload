using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.OnChain;
using PolyCopyTrader.Service.OnChain;

namespace PolyCopyTrader.Tests;

public sealed class OnChainTradeCaptureTests
{
    private static readonly OnChainExchangeContractOptions V2Contract = new(
        "CTF Exchange V2",
        "0xE111180000d2663C0091e4f400237545B87B996B",
        "V2");

    [Fact]
    public async Task Processor_StoresDecodedTradeCapturesWithoutDerivedOnChainWrites()
    {
        var fakeRpc = new FakePolygonRpcClient { LatestBlock = 100 };
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 100,
            dataWords:
            [
                Word(0),
                Word(789),
                Word(25_000_000),
                Word(50_000_000),
                Word(0),
                Word(0),
                Word(0)
            ]));
        var repository = new TestAppRepository();
        var paperProcessor = new FakeOnChainPaperSignalProcessor();
        var processor = CreateProcessor(fakeRpc, repository, paperProcessor);

        var result = await processor.CaptureOnceAsync();

        Assert.Equal(1, result.LogsFetched);
        Assert.Equal(1, result.CapturesStored);
        Assert.Equal(2, result.HotCandidatesProcessed);
        Assert.Empty(repository.PolymarketOnChainLogs);
        Assert.Empty(repository.PolymarketOnChainFills);
        Assert.Empty(repository.PolymarketOnChainWalletFills);
        Assert.Single(paperProcessor.CaptureBatches);

        var capture = Assert.Single(repository.PolymarketOnChainTradeCaptures);
        Assert.Equal("789", capture.TokenId);
        Assert.Equal(TradeSide.Buy, capture.Side);
        Assert.Equal(0.5m, capture.Price);
        Assert.Equal("0x1111111111111111111111111111111111111111", capture.Maker);
        Assert.Equal("0x2222222222222222222222222222222222222222", capture.Taker);
        Assert.Equal("0x1111111111111111111111111111111111111111", capture.Wallet);
        Assert.Equal(4, capture.RawTopics.Count);
        Assert.StartsWith("0x", capture.RawData, StringComparison.Ordinal);

        var cursor = Assert.Single(repository.OnChainTradeCaptureCursors);
        Assert.Equal(101, cursor.NextBlock);
        Assert.Equal(100, cursor.LastScannedBlock);
        Assert.Equal(100, cursor.LastTargetBlock);
    }

    [Fact]
    public async Task Processor_UsesTradeCaptureCursorWhenAlreadyCaughtUp()
    {
        var fakeRpc = new FakePolygonRpcClient { LatestBlock = 100 };
        var repository = new TestAppRepository();
        repository.OnChainTradeCaptureCursors.Add(new OnChainTradeCaptureCursor(
            V2Contract.Address.ToLowerInvariant(),
            V2Contract.Name,
            V2Contract.Version,
            101,
            100,
            100,
            10,
            5,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow));
        var processor = CreateProcessor(fakeRpc, repository);

        var result = await processor.CaptureOnceAsync();

        Assert.Equal(0, result.RangesScanned);
        Assert.Empty(fakeRpc.LogRequests);
        Assert.Empty(repository.PolymarketOnChainTradeCaptures);
        var cursor = Assert.Single(repository.OnChainTradeCaptureCursors);
        Assert.Equal(101, cursor.NextBlock);
    }

    [Fact]
    public async Task Processor_CanProcessHotSignalsWithoutPersistingCaptures()
    {
        var fakeRpc = new FakePolygonRpcClient { LatestBlock = 100 };
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 100,
            dataWords:
            [
                Word(0),
                Word(789),
                Word(25_000_000),
                Word(50_000_000),
                Word(0),
                Word(0),
                Word(0)
            ]));
        var repository = new TestAppRepository();
        var paperProcessor = new FakeOnChainPaperSignalProcessor(
            new OnChainPaperSignalProcessingResult(2, 2, 1, 1, 1, 0));
        var processor = CreateProcessor(
            fakeRpc,
            repository,
            paperProcessor,
            Options(tradeCapturePersistCaptures: false));

        var result = await processor.CaptureOnceAsync();

        Assert.Equal(1, result.LogsFetched);
        Assert.Equal(0, result.CapturesStored);
        Assert.Equal(2, result.HotCandidatesProcessed);
        Assert.Equal(1, result.HotPaperOrdersCreated);
        Assert.Empty(repository.PolymarketOnChainTradeCaptures);
        var captures = Assert.Single(paperProcessor.CaptureBatches);
        Assert.Single(captures);
        var cursor = Assert.Single(repository.OnChainTradeCaptureCursors);
        Assert.Equal(101, cursor.NextBlock);
    }

    [Fact]
    public async Task Processor_SkipsStaleCursorToRecentRangeWhenConfigured()
    {
        var fakeRpc = new FakePolygonRpcClient { LatestBlock = 100 };
        var repository = new TestAppRepository();
        repository.OnChainTradeCaptureCursors.Add(new OnChainTradeCaptureCursor(
            V2Contract.Address.ToLowerInvariant(),
            V2Contract.Name,
            V2Contract.Version,
            10,
            9,
            9,
            10,
            5,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow));
        var processor = CreateProcessor(
            fakeRpc,
            repository,
            options: Options(tradeCaptureSkipStaleCursor: true, tradeCaptureMaxCursorLagBlocks: 2));

        var result = await processor.CaptureOnceAsync();

        Assert.Equal(1, result.RangesScanned);
        var request = Assert.Single(fakeRpc.LogRequests);
        Assert.Equal(99, request.FromBlock);
        Assert.Equal(100, request.ToBlock);
        var cursor = Assert.Single(repository.OnChainTradeCaptureCursors);
        Assert.Equal(101, cursor.NextBlock);
    }

    private static OnChainTradeCaptureProcessor CreateProcessor(
        FakePolygonRpcClient rpcClient,
        TestAppRepository repository,
        FakeOnChainPaperSignalProcessor? paperProcessor = null,
        OnChainIngestionOptions? options = null)
    {
        return new OnChainTradeCaptureProcessor(
            NullLogger<OnChainTradeCaptureProcessor>.Instance,
            options ?? Options(),
            rpcClient,
            repository,
            paperProcessor ?? new FakeOnChainPaperSignalProcessor());
    }

    private static OnChainIngestionOptions Options(
        bool tradeCapturePersistCaptures = true,
        bool tradeCaptureSkipStaleCursor = false,
        int tradeCaptureMaxCursorLagBlocks = 2)
    {
        return new OnChainIngestionOptions
        {
            TradeCaptureEnabled = true,
            TradeCapturePersistCaptures = tradeCapturePersistCaptures,
            TradeCaptureSkipStaleCursor = tradeCaptureSkipStaleCursor,
            TradeCaptureMaxCursorLagBlocks = tradeCaptureMaxCursorLagBlocks,
            MaxBlockRange = 10,
            TradeCaptureStartLookbackBlocks = 1,
            TradeCaptureConfirmations = 0,
            TradeCaptureRequestDelayMilliseconds = 0,
            ExchangeContracts = [V2Contract]
        };
    }

    private static PolygonRpcLog Log(
        string topic0,
        long blockNumber,
        IReadOnlyList<string> dataWords,
        long? logIndex = null,
        string? transactionHash = null)
    {
        return new PolygonRpcLog(
            V2Contract.Address,
            [
                topic0,
                "0x" + new string('a', 64),
                AddressTopic("0x1111111111111111111111111111111111111111"),
                AddressTopic("0x2222222222222222222222222222222222222222")
            ],
            "0x" + string.Concat(dataWords.Select(word => word[2..])),
            blockNumber,
            "0x" + new string('b', 64),
            transactionHash ?? "0x" + new string('c', 64),
            0,
            logIndex ?? blockNumber,
            false);
    }

    private static string Word(ulong value)
    {
        return "0x" + value.ToString("x").PadLeft(64, '0');
    }

    private static string AddressTopic(string address)
    {
        return "0x" + address[2..].PadLeft(64, '0');
    }

    private sealed class FakePolygonRpcClient : IPolygonRpcClient
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        public long LatestBlock { get; init; } = 100;

        public List<PolygonRpcLog> Logs { get; } = [];

        public List<LogRequest> LogRequests { get; } = [];

        public Task<long> GetLatestBlockNumberAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LatestBlock);
        }

        public Task<DateTimeOffset> GetBlockTimestampAsync(long blockNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Now.AddSeconds(blockNumber - LatestBlock));
        }

        public Task<IReadOnlyList<PolygonRpcLog>> GetLogsAsync(
            string contractAddress,
            string topic0,
            long fromBlock,
            long toBlock,
            CancellationToken cancellationToken = default)
        {
            LogRequests.Add(new LogRequest(contractAddress, fromBlock, toBlock));
            return Task.FromResult<IReadOnlyList<PolygonRpcLog>>(
                Logs
                    .Where(log => log.BlockNumber >= fromBlock && log.BlockNumber <= toBlock)
                    .Where(log => string.Equals(log.Address, contractAddress, StringComparison.OrdinalIgnoreCase))
                    .Where(log => string.Equals(log.Topics[0], topic0, StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }
    }

    private sealed record LogRequest(string ContractAddress, long FromBlock, long ToBlock);

    private sealed class FakeOnChainPaperSignalProcessor(
        OnChainPaperSignalProcessingResult? processingResult = null) : IOnChainPaperSignalProcessor
    {
        private readonly OnChainPaperSignalProcessingResult result =
            processingResult ?? new OnChainPaperSignalProcessingResult(2, 0, 0, 0, 0, 0);

        public List<IReadOnlyList<PolymarketOnChainTradeCapture>> CaptureBatches { get; } = [];

        public Task<OnChainPaperSignalProcessingResult> ProcessOnceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0));
        }

        public Task<OnChainPaperSignalProcessingResult> ProcessCapturesAsync(
            IReadOnlyList<PolymarketOnChainTradeCapture> captures,
            CancellationToken cancellationToken = default)
        {
            CaptureBatches.Add(captures.ToArray());
            return Task.FromResult(result);
        }
    }
}
