using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.OnChain;
using PolyCopyTrader.Service.OnChain;

namespace PolyCopyTrader.Tests;

public sealed class OnChainIngestionTests
{
    [Fact]
    public void Parser_DecodesV1BuyOrderFilled()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V1",
            "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E",
            "V1");
        var log = Log(
            PolymarketOnChainOrderFilledParser.V1OrderFilledTopic,
            blockNumber: 10,
            dataWords:
            [
                Word(0),
                Word(123),
                Word(50_000_000),
                Word(100_000_000),
                Word(0)
            ]);

        var fill = PolymarketOnChainOrderFilledParser.Parse(log, contract, DateTimeOffset.UnixEpoch);

        Assert.Equal(TradeSide.Buy, fill.Side);
        Assert.Equal("123", fill.TokenId);
        Assert.Equal(0.5m, fill.Price);
        Assert.Equal(100m, fill.SizeShares);
        Assert.Equal(50m, fill.NotionalUsd);
        Assert.Equal("0", fill.MakerAssetId);
        Assert.Equal("123", fill.TakerAssetId);
    }

    [Fact]
    public void Parser_DecodesV2SellOrderFilled()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var log = Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 10,
            dataWords:
            [
                Word(1),
                Word(456),
                Word(20_000_000),
                Word(12_000_000),
                Word(25_000),
                Word(0),
                Word(0)
            ]);

        var fill = PolymarketOnChainOrderFilledParser.Parse(log, contract, DateTimeOffset.UnixEpoch);

        Assert.Equal(TradeSide.Sell, fill.Side);
        Assert.Equal("456", fill.TokenId);
        Assert.Equal(0.6m, fill.Price);
        Assert.Equal(20m, fill.SizeShares);
        Assert.Equal(12m, fill.NotionalUsd);
        Assert.Equal("456", fill.MakerAssetId);
        Assert.Equal("0", fill.TakerAssetId);
        Assert.Equal("0", fill.FeeAssetId);
        Assert.Equal(0.025m, fill.FeeAmount);
    }

    [Fact]
    public async Task Processor_StoresLogsFillsAndCursorForLookbackWindow()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 1,
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
        var processor = new OnChainIngestionProcessor(
            NullLogger<OnChainIngestionProcessor>.Instance,
            new OnChainIngestionOptions
            {
                LookbackDays = 1,
                MaxBlockRange = 10,
                RequestDelayMilliseconds = 0,
                ExchangeContracts = [contract]
            },
            fakeRpc,
            repository);

        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(1, result.LogsFetched);
        Assert.Equal(1, result.FillsStored);
        Assert.Single(repository.PolymarketOnChainLogs);
        var fill = Assert.Single(repository.PolymarketOnChainFills);
        Assert.Equal("789", fill.TokenId);
        Assert.Equal(0.5m, fill.Price);
        Assert.Equal(2, repository.PolymarketOnChainWalletFills.Count);
        Assert.Equal(2, repository.PolymarketOnChainWalletExecutions.Count);
        Assert.Contains(repository.PolymarketOnChainWalletFills, item =>
            item.Role == OnChainParticipantRole.Maker &&
            item.Wallet == "0x1111111111111111111111111111111111111111" &&
            item.Side == TradeSide.Buy);
        Assert.Contains(repository.PolymarketOnChainWalletFills, item =>
            item.Role == OnChainParticipantRole.Taker &&
            item.Wallet == "0x2222222222222222222222222222222222222222" &&
            item.Side == TradeSide.Sell);
        Assert.Single(repository.OnChainIngestionCursors);
    }

    [Fact]
    public async Task Repository_AggregatesWalletExecutionsByTransactionWalletTokenAndSide()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var repository = new TestAppRepository();
        var fill1 = PolymarketOnChainOrderFilledParser.Parse(
            Log(
                PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
                blockNumber: 1,
                dataWords: [Word(0), Word(101), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)],
                logIndex: 1),
            contract,
            DateTimeOffset.UnixEpoch);
        var fill2 = PolymarketOnChainOrderFilledParser.Parse(
            Log(
                PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
                blockNumber: 1,
                dataWords: [Word(0), Word(101), Word(15_000_000), Word(30_000_000), Word(0), Word(0), Word(0)],
                logIndex: 2),
            contract,
            DateTimeOffset.UnixEpoch);

        await repository.AddPolymarketOnChainFillsAsync([fill1, fill2]);

        Assert.Equal(4, repository.PolymarketOnChainWalletFills.Count);
        Assert.Equal(2, repository.PolymarketOnChainWalletExecutions.Count);
        Assert.Contains(fill1.TokenId, repository.PolymarketOnChainTokenMetadataRefreshQueue);
        var makerExecution = Assert.Single(repository.PolymarketOnChainWalletExecutions, item =>
            item.Wallet == "0x1111111111111111111111111111111111111111" &&
            item.Side == TradeSide.Buy);
        Assert.Equal(2, makerExecution.FillCount);
        Assert.Equal(2, makerExecution.MakerFillCount);
        Assert.Equal(0, makerExecution.TakerFillCount);
        Assert.Equal(50m, makerExecution.SizeShares);
        Assert.Equal(25m, makerExecution.NotionalUsd);
        Assert.Equal(0.5m, makerExecution.AveragePrice);

        var takerExecution = Assert.Single(repository.PolymarketOnChainWalletExecutions, item =>
            item.Wallet == "0x2222222222222222222222222222222222222222" &&
            item.Side == TradeSide.Sell);
        Assert.Equal(2, takerExecution.FillCount);
        Assert.Equal(0, takerExecution.MakerFillCount);
        Assert.Equal(2, takerExecution.TakerFillCount);
    }

    [Fact]
    public async Task Repository_BuildsWalletPositionsWithResolvedPnl()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainWalletExecutions.Add(Execution("0xwallet", TradeSide.Buy, "token-yes", 100m, 40m, 1m, "0x" + new string('a', 64)));
        repository.PolymarketOnChainWalletExecutions.Add(Execution("0xwallet", TradeSide.Sell, "token-yes", 25m, 15m, 0m, "0x" + new string('b', 64)));
        await repository.UpsertPolymarketOnChainTokenMetadataAsync(
        [
            new PolymarketOnChainTokenMetadata(
                "token-yes",
                "condition-1",
                "market-1",
                "market-slug",
                "Market title",
                "Yes",
                0,
                "Politics",
                DateTimeOffset.UtcNow.AddDays(-1),
                false,
                true,
                false,
                true,
                "Yes",
                ["token-yes", "token-no"],
                ["Yes", "No"],
                true,
                null,
                "{}",
                DateTimeOffset.UtcNow)
        ]);

        var refreshResult = await repository.RefreshPolymarketOnChainWalletPositionsAsync();
        var position = Assert.Single(await repository.GetPolymarketOnChainWalletPositionsAsync());
        var performanceResult = await repository.RefreshPolymarketOnChainWalletPerformanceAsync();
        var performance = Assert.Single(await repository.GetPolymarketOnChainWalletPerformanceAsync());
        var categoryPerformanceResult = await repository.RefreshPolymarketOnChainWalletCategoryPerformanceAsync();
        var categoryPerformance = Assert.Single(await repository.GetPolymarketOnChainWalletCategoryPerformanceAsync("Politics"));

        Assert.Equal(1, refreshResult.TokensProcessed);
        Assert.Equal(1, refreshResult.PositionsUpserted);
        Assert.Equal("0xwallet", position.Wallet);
        Assert.Equal("Market title", position.MarketTitle);
        Assert.Equal("Yes", position.Outcome);
        Assert.Equal("Resolved", position.PositionStatus);
        Assert.Equal(2, position.Executions);
        Assert.Equal(100m, position.BuyShares);
        Assert.Equal(25m, position.SellShares);
        Assert.Equal(75m, position.NetShares);
        Assert.Equal(26m, position.NetCostUsd);
        Assert.Equal(0.4m, position.AverageBuyPrice);
        Assert.Equal(0.6m, position.AverageSellPrice);
        Assert.Equal(49m, position.ResolvedPnlUsd);
        Assert.Equal(1, performanceResult.WalletsProcessed);
        Assert.Equal(1, performanceResult.WalletsUpserted);
        Assert.Equal("0xwallet", performance.Wallet);
        Assert.Equal(1, performance.PositionsCount);
        Assert.Equal(1, performance.ResolvedPositions);
        Assert.Equal(49m, performance.ResolvedPnlUsd);
        Assert.Equal(188.46153846153846153846153846m, performance.ResolvedRoiPct);
        Assert.Equal(100m, performance.WinRatePct);
        Assert.True(performance.Score > 0m);
        Assert.Equal(1, categoryPerformanceResult.PairsProcessed);
        Assert.Equal(1, categoryPerformanceResult.PairsUpserted);
        Assert.Equal("0xwallet", categoryPerformance.Wallet);
        Assert.Equal("Politics", categoryPerformance.Category);
        Assert.Equal(performance.ResolvedPnlUsd, categoryPerformance.ResolvedPnlUsd);
        Assert.Equal(performance.Score, categoryPerformance.Score);
    }

    [Fact]
    public async Task Processor_ResumesAfterExistingCursor()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 1,
            dataWords: [Word(0), Word(101), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 2,
            dataWords: [Word(0), Word(202), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        var repository = new TestAppRepository();
        await repository.UpsertOnChainIngestionCursorAsync(new OnChainIngestionCursor(
            contract.Address.ToLowerInvariant(),
            contract.Name,
            contract.Version,
            1,
            1,
            1,
            1,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(-1)));

        var processor = CreateProcessor(contract, fakeRpc, repository);
        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(1, result.LogsFetched);
        var request = Assert.Single(fakeRpc.LogRequests);
        Assert.Equal((2, 2), (request.FromBlock, request.ToBlock));
        Assert.Single(repository.PolymarketOnChainFills);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().ToBlock);
    }

    [Fact]
    public async Task Processor_ResumesFromStoredFillsWhenCursorIsMissing()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 1,
            dataWords: [Word(0), Word(101), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 2,
            dataWords: [Word(0), Word(202), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        var repository = new TestAppRepository();
        var existingFill = PolymarketOnChainOrderFilledParser.Parse(fakeRpc.Logs[0], contract, DateTimeOffset.UtcNow.AddHours(-12));
        await repository.AddPolymarketOnChainFillsAsync([existingFill]);

        var processor = CreateProcessor(contract, fakeRpc, repository);
        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(1, result.LogsFetched);
        var request = Assert.Single(fakeRpc.LogRequests);
        Assert.Equal((2, 2), (request.FromBlock, request.ToBlock));
        Assert.Equal(2, repository.PolymarketOnChainFills.Count);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().ToBlock);
    }

    [Fact]
    public async Task Processor_BuildsDerivedWalletDataForExistingRawFills()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        var repository = new TestAppRepository();
        var existingFill = PolymarketOnChainOrderFilledParser.Parse(
            Log(
                PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
                blockNumber: 1,
                dataWords: [Word(0), Word(101), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]),
            contract,
            fakeRpc.Now.AddHours(-12));
        var secondExistingFill = PolymarketOnChainOrderFilledParser.Parse(
            Log(
                PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
                blockNumber: 2,
                dataWords: [Word(0), Word(202), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)],
                transactionHash: "0x" + new string('d', 64)),
            contract,
            fakeRpc.Now);
        repository.PolymarketOnChainFills.Add(existingFill);
        repository.PolymarketOnChainFills.Add(secondExistingFill);
        await repository.UpsertOnChainIngestionCursorAsync(new OnChainIngestionCursor(
            contract.Address.ToLowerInvariant(),
            contract.Name,
            contract.Version,
            1,
            2,
            0,
            0,
            fakeRpc.Now.AddMinutes(-1),
            fakeRpc.Now.AddMinutes(-1)));

        var processor = CreateProcessor(contract, fakeRpc, repository);
        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(0, result.LogsFetched);
        Assert.Empty(fakeRpc.LogRequests);
        Assert.Equal([new OnChainBlockRange(1, 1), new OnChainBlockRange(2, 2)], repository.OnChainWalletDerivedRefreshRanges);
        Assert.Equal(4, repository.PolymarketOnChainWalletFills.Count);
        Assert.Equal(4, repository.PolymarketOnChainWalletExecutions.Count);
    }

    [Fact]
    public async Task Processor_CatchesUpFreshBlocksBeforeRepairingExistingDerivedData()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 2,
            dataWords: [Word(0), Word(202), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        var repository = new TestAppRepository();
        repository.RebuildDerivedDataOnAddFills = false;
        var existingFill = PolymarketOnChainOrderFilledParser.Parse(
            Log(
                PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
                blockNumber: 1,
                dataWords: [Word(0), Word(101), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]),
            contract,
            fakeRpc.Now.AddHours(-12));
        repository.PolymarketOnChainFills.Add(existingFill);
        await repository.UpsertOnChainIngestionCursorAsync(new OnChainIngestionCursor(
            contract.Address.ToLowerInvariant(),
            contract.Name,
            contract.Version,
            1,
            1,
            0,
            0,
            fakeRpc.Now.AddMinutes(-1),
            fakeRpc.Now.AddMinutes(-1)));
        repository.BeforeOnChainWalletDerivedRefresh = _ =>
            Assert.Equal(2, repository.OnChainIngestionCursors.Single().ToBlock);

        var processor = CreateProcessor(contract, fakeRpc, repository);
        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(1, result.LogsFetched);
        Assert.Equal([new LogRequest(2, 2)], fakeRpc.LogRequests);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().ToBlock);
        Assert.Equal(
            [new OnChainBlockRange(1, 1), new OnChainBlockRange(2, 2)],
            repository.OnChainWalletDerivedRefreshRanges);
    }

    [Fact]
    public async Task Processor_DoesNotBackfillHistoryAfterFreshTailIsCaughtUp()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 0,
            dataWords: [Word(0), Word(303), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        var repository = new TestAppRepository();
        await repository.UpsertOnChainIngestionCursorAsync(new OnChainIngestionCursor(
            contract.Address.ToLowerInvariant(),
            contract.Name,
            contract.Version,
            1,
            2,
            0,
            0,
            fakeRpc.Now.AddMinutes(-1),
            fakeRpc.Now.AddMinutes(-1)));

        var processor = CreateProcessor(contract, fakeRpc, repository);
        var result = await processor.RefreshLookbackAsync();

        Assert.Equal(0, result.LogsFetched);
        Assert.Empty(fakeRpc.LogRequests);
        Assert.Equal(1, repository.OnChainIngestionCursors.Single().FromBlock);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().ToBlock);
        Assert.Empty(repository.PolymarketOnChainFills);
    }

    [Fact]
    public async Task Processor_BackgroundCycleDoesNotRunHistoricalBackfill()
    {
        var contract = new OnChainExchangeContractOptions(
            "CTF Exchange V2",
            "0xE111180000d2663C0091e4f400237545B87B996B",
            "V2");
        var fakeRpc = new FakePolygonRpcClient();
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 0,
            dataWords: [Word(0), Word(404), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)]));
        fakeRpc.Logs.Add(Log(
            PolymarketOnChainOrderFilledParser.V2OrderFilledTopic,
            blockNumber: 1,
            dataWords: [Word(0), Word(505), Word(10_000_000), Word(20_000_000), Word(0), Word(0), Word(0)],
            transactionHash: "0x" + new string('e', 64)));
        var repository = new TestAppRepository();
        await repository.UpsertOnChainIngestionCursorAsync(new OnChainIngestionCursor(
            contract.Address.ToLowerInvariant(),
            contract.Name,
            contract.Version,
            2,
            2,
            0,
            0,
            fakeRpc.Now.AddMinutes(-1),
            fakeRpc.Now.AddMinutes(-1)));

        var processor = new OnChainIngestionProcessor(
            NullLogger<OnChainIngestionProcessor>.Instance,
            new OnChainIngestionOptions
            {
                LookbackDays = 1,
                MaxBlockRange = 1,
                RequestDelayMilliseconds = 0,
                ExchangeContracts = [contract]
            },
            fakeRpc,
            repository);

        var firstResult = await processor.RefreshBackgroundCycleAsync();

        Assert.Equal(0, firstResult.LogsFetched);
        Assert.Empty(fakeRpc.LogRequests);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().FromBlock);

        var secondResult = await processor.RefreshBackgroundCycleAsync();

        Assert.Equal(0, secondResult.LogsFetched);
        Assert.Empty(fakeRpc.LogRequests);
        Assert.Equal(2, repository.OnChainIngestionCursors.Single().FromBlock);
    }

    [Fact]
    public void OnChainIngestion_InvalidConfiguration_ReturnsErrors()
    {
        var configuration = new AppConfiguration
        {
            OnChainIngestion = new OnChainIngestionOptions
            {
                PolygonRpcUrl = "not-a-url",
                RpcUrlEnvironmentVariable = "BAD ENV",
                LookbackDays = 31,
                MaxBlockRange = 0,
                BackgroundSyncIdleDelaySeconds = 0,
                BackgroundErrorDelaySeconds = 0,
                BackgroundMaxErrorDelaySeconds = -1,
                MarketEnrichmentBatchSize = 0,
                MarketEnrichmentMaxBatchesPerRun = 0,
                MarketEnrichmentIntervalSeconds = 0,
                PositionRefreshIntervalSeconds = 0,
                PositionRefreshTokenBatchSize = 0,
                PositionRefreshQueueSeedTokenBatchSize = 0,
                ActivityRefreshIntervalSeconds = 0,
                ActivityRefreshWalletBatchSize = 0,
                ActivityRefreshQueueSeedWalletBatchSize = 0,
                PerformanceRefreshIntervalSeconds = 0,
                PerformanceRefreshWalletBatchSize = 0,
                PerformanceRefreshQueueSeedWalletBatchSize = 0,
                CategoryPerformanceRefreshIntervalSeconds = 0,
                CategoryPerformancePairBatchSize = 0,
                CategoryPerformanceQueueSeedPairBatchSize = 0,
                ExchangeContracts =
                [
                    new OnChainExchangeContractOptions("Broken", "0x123", "V9")
                ]
            }
        };

        var errors = AppOptionsValidator.Validate(configuration);

        Assert.Contains(errors, error => error.Contains("OnChainIngestion.PolygonRpcUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("RpcUrlEnvironmentVariable", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("LookbackDays", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MaxBlockRange", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BackgroundSyncIdleDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BackgroundErrorDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("BackgroundMaxErrorDelaySeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketEnrichmentBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketEnrichmentMaxBatchesPerRun", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("MarketEnrichmentIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PositionRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PositionRefreshTokenBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PositionRefreshQueueSeedTokenBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ActivityRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ActivityRefreshWalletBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ActivityRefreshQueueSeedWalletBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PerformanceRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PerformanceRefreshWalletBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PerformanceRefreshQueueSeedWalletBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CategoryPerformanceRefreshIntervalSeconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CategoryPerformancePairBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("CategoryPerformanceQueueSeedPairBatchSize", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Version", StringComparison.Ordinal));
    }

    private static PolygonRpcLog Log(
        string topic0,
        long blockNumber,
        IReadOnlyList<string> dataWords,
        long? logIndex = null,
        string? transactionHash = null)
    {
        return new PolygonRpcLog(
            "0xE111180000d2663C0091e4f400237545B87B996B",
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

    private static PolymarketOnChainWalletExecution Execution(
        string wallet,
        TradeSide side,
        string tokenId,
        decimal sizeShares,
        decimal notionalUsd,
        decimal feesUsd,
        string transactionHash)
    {
        return new PolymarketOnChainWalletExecution(
            "CTF Exchange V2",
            "0xexchange",
            "V2",
            1,
            DateTimeOffset.UtcNow,
            transactionHash,
            0,
            0,
            wallet,
            side,
            tokenId,
            1,
            side == TradeSide.Buy ? 1 : 0,
            side == TradeSide.Sell ? 1 : 0,
            sizeShares,
            notionalUsd,
            sizeShares == 0m ? 0m : notionalUsd / sizeShares,
            feesUsd,
            DateTimeOffset.UtcNow);
    }

    private static string Word(ulong value)
    {
        return "0x" + value.ToString("x").PadLeft(64, '0');
    }

    private static string AddressTopic(string address)
    {
        return "0x" + address[2..].PadLeft(64, '0');
    }

    private static OnChainIngestionProcessor CreateProcessor(
        OnChainExchangeContractOptions contract,
        FakePolygonRpcClient rpcClient,
        TestAppRepository repository)
    {
        return new OnChainIngestionProcessor(
            NullLogger<OnChainIngestionProcessor>.Instance,
            new OnChainIngestionOptions
            {
                LookbackDays = 1,
                MaxBlockRange = 1,
                RequestDelayMilliseconds = 0,
                ExchangeContracts = [contract]
            },
            rpcClient,
            repository);
    }

    private sealed class FakePolygonRpcClient : IPolygonRpcClient
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        public List<PolygonRpcLog> Logs { get; } = [];

        public List<LogRequest> LogRequests { get; } = [];

        public Task<long> GetLatestBlockNumberAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(2L);
        }

        public Task<DateTimeOffset> GetBlockTimestampAsync(long blockNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(blockNumber switch
            {
                0 => Now.AddDays(-2),
                1 => Now.AddHours(-12),
                _ => Now
            });
        }

        public Task<IReadOnlyList<PolygonRpcLog>> GetLogsAsync(
            string contractAddress,
            string topic0,
            long fromBlock,
            long toBlock,
            CancellationToken cancellationToken = default)
        {
            LogRequests.Add(new LogRequest(fromBlock, toBlock));
            return Task.FromResult<IReadOnlyList<PolygonRpcLog>>(
                Logs
                    .Where(log => log.BlockNumber >= fromBlock && log.BlockNumber <= toBlock)
                    .Where(log => string.Equals(log.Topics[0], topic0, StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }
    }

    private sealed record LogRequest(long FromBlock, long ToBlock);
}
