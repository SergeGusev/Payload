using System.Diagnostics;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.OnChain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainTradeCaptureProcessor(
    ILogger<OnChainTradeCaptureProcessor> logger,
    OnChainIngestionOptions options,
    IPolygonRpcClient rpcClient,
    IAppRepository repository,
    IOnChainPaperSignalProcessor paperSignalProcessor) : IOnChainTradeCaptureProcessor
{
    private readonly SemaphoreSlim singleRun = new(1, 1);

    public async Task<OnChainTradeCaptureResult> CaptureOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!await singleRun.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException("On-chain trade capture is already running.");
        }

        try
        {
            return await CaptureOnceCoreAsync(cancellationToken);
        }
        finally
        {
            singleRun.Release();
        }
    }

    private async Task<OnChainTradeCaptureResult> CaptureOnceCoreAsync(CancellationToken cancellationToken)
    {
        if (!options.TradeCaptureEnabled)
        {
            logger.LogInformation("On-chain trade capture is disabled.");
            return new OnChainTradeCaptureResult(0, 0, 0, 0, 0, 0);
        }

        var latestBlock = await rpcClient.GetLatestBlockNumberAsync(cancellationToken);
        var targetBlock = Math.Max(0, latestBlock - options.TradeCaptureConfirmations);
        var contractsScanned = 0;
        var rangesScanned = 0;
        var logsFetched = 0;
        var capturesStored = 0;
        var hotCandidatesProcessed = 0;
        var hotPaperOrdersCreated = 0;
        var maxBlockRange = Math.Max(1, options.MaxBlockRange);

        foreach (var contract in options.ExchangeContracts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var topic = PolymarketOnChainOrderFilledParser.GetOrderFilledTopic(contract.Version);
            var contractAddress = contract.Address.ToLowerInvariant();
            var cursor = await repository.GetOnChainTradeCaptureCursorAsync(contractAddress, cancellationToken);
            var startedAtUtc = cursor?.StartedAtUtc ?? DateTimeOffset.UtcNow;
            var cumulativeLogsFetched = cursor?.LogsFetched ?? 0;
            var cumulativeCapturesStored = cursor?.CapturesStored ?? 0;
            var nextBlock = cursor?.NextBlock ?? GetInitialNextBlock(targetBlock);
            nextBlock = MoveStaleCursorToRecentRange(contract.Name, nextBlock, targetBlock);

            if (nextBlock > targetBlock)
            {
                contractsScanned++;
                continue;
            }

            for (var fromBlock = nextBlock; fromBlock <= targetBlock; fromBlock += maxBlockRange)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toBlock = Math.Min(targetBlock, fromBlock + maxBlockRange - 1);
                var rangeStopwatch = Stopwatch.StartNew();
                var fetchStopwatch = Stopwatch.StartNew();
                var logs = await rpcClient.GetLogsAsync(contract.Address, topic, fromBlock, toBlock, cancellationToken);
                fetchStopwatch.Stop();
                var observedAtUtc = DateTimeOffset.UtcNow;
                var decodeStopwatch = Stopwatch.StartNew();
                var captures = await DecodeCapturesAsync(logs, contract, observedAtUtc, cancellationToken);
                decodeStopwatch.Stop();
                var hotSignalStopwatch = Stopwatch.StartNew();
                var hotResult = captures.Count == 0
                    ? new OnChainPaperSignalProcessingResult(0, 0, 0, 0, 0, 0)
                    : await paperSignalProcessor.ProcessCapturesAsync(captures, cancellationToken);
                hotSignalStopwatch.Stop();
                var persistStopwatch = Stopwatch.StartNew();
                var rowsStored = options.TradeCapturePersistCaptures
                    ? await repository.AddPolymarketOnChainTradeCapturesAsync(captures, cancellationToken)
                    : 0;
                persistStopwatch.Stop();

                rangesScanned++;
                logsFetched += logs.Count;
                capturesStored += rowsStored;
                hotCandidatesProcessed += hotResult.CandidatesFetched;
                hotPaperOrdersCreated += hotResult.PaperOrdersCreated;
                cumulativeLogsFetched += logs.Count;
                cumulativeCapturesStored += rowsStored;

                await repository.UpsertOnChainTradeCaptureCursorAsync(
                    new OnChainTradeCaptureCursor(
                        contractAddress,
                        contract.Name,
                        contract.Version.ToUpperInvariant(),
                        toBlock + 1,
                        toBlock,
                        targetBlock,
                        cumulativeLogsFetched,
                        cumulativeCapturesStored,
                        startedAtUtc,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
                rangeStopwatch.Stop();

                if (logs.Count > 0)
                {
                    logger.LogInformation(
                        "On-chain trade captures processed. Contract={Contract} Blocks={FromBlock}-{ToBlock} Logs={Logs} CapturesStored={CapturesStored} HotCandidates={HotCandidates} HotPaperOrders={HotPaperOrders} FetchMs={FetchMs} DecodeMs={DecodeMs} HotSignalMs={HotSignalMs} PersistMs={PersistMs} TotalMs={TotalMs}",
                        contract.Name,
                        fromBlock,
                        toBlock,
                        logs.Count,
                        rowsStored,
                        hotResult.CandidatesFetched,
                        hotResult.PaperOrdersCreated,
                        fetchStopwatch.ElapsedMilliseconds,
                        decodeStopwatch.ElapsedMilliseconds,
                        hotSignalStopwatch.ElapsedMilliseconds,
                        persistStopwatch.ElapsedMilliseconds,
                        rangeStopwatch.ElapsedMilliseconds);
                }

                await DelayRequestIfConfiguredAsync(cancellationToken);
            }

            contractsScanned++;
        }

        return new OnChainTradeCaptureResult(
            latestBlock,
            targetBlock,
            contractsScanned,
            rangesScanned,
            logsFetched,
            capturesStored,
            hotCandidatesProcessed,
            hotPaperOrdersCreated);
    }

    private long GetInitialNextBlock(long targetBlock)
    {
        if (options.TradeCaptureStartLookbackBlocks == 0)
        {
            return targetBlock + 1;
        }

        return Math.Max(0, targetBlock - options.TradeCaptureStartLookbackBlocks + 1);
    }

    private long MoveStaleCursorToRecentRange(string contractName, long nextBlock, long targetBlock)
    {
        if (!options.TradeCaptureSkipStaleCursor || nextBlock > targetBlock)
        {
            return nextBlock;
        }

        var maxCursorLagBlocks = Math.Max(0, options.TradeCaptureMaxCursorLagBlocks);
        var earliestRecentBlock = Math.Max(0, targetBlock - maxCursorLagBlocks + 1);
        if (nextBlock >= earliestRecentBlock)
        {
            return nextBlock;
        }

        logger.LogWarning(
            "Skipping stale on-chain trade capture cursor. Contract={Contract} OriginalNextBlock={OriginalNextBlock} NewNextBlock={NewNextBlock} TargetBlock={TargetBlock}",
            contractName,
            nextBlock,
            earliestRecentBlock,
            targetBlock);
        return earliestRecentBlock;
    }

    private async Task<IReadOnlyList<PolymarketOnChainTradeCapture>> DecodeCapturesAsync(
        IReadOnlyList<PolygonRpcLog> logs,
        OnChainExchangeContractOptions contract,
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        if (logs.Count == 0)
        {
            return [];
        }

        var blockTimestamps = new Dictionary<long, DateTimeOffset>();
        var useObservedTimestamp = !options.TradeCapturePersistCaptures && options.PaperSignalHotPathEnabled;
        foreach (var blockNumber in logs.Select(log => log.BlockNumber).Distinct().OrderBy(blockNumber => blockNumber))
        {
            if (useObservedTimestamp)
            {
                blockTimestamps[blockNumber] = observedAtUtc;
                continue;
            }

            blockTimestamps[blockNumber] = await rpcClient.GetBlockTimestampAsync(blockNumber, cancellationToken);
            await DelayRequestIfConfiguredAsync(cancellationToken);
        }

        var captures = new List<PolymarketOnChainTradeCapture>(logs.Count);
        foreach (var log in logs)
        {
            try
            {
                var rawLog = PolymarketOnChainOrderFilledParser.ToDomainLog(log, contract, observedAtUtc);
                var fill = PolymarketOnChainOrderFilledParser.Parse(log, contract, blockTimestamps[log.BlockNumber]);
                captures.Add(ToCapture(fill, rawLog));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Skipping undecodable on-chain trade capture log. Contract={Contract} Tx={TransactionHash} LogIndex={LogIndex}",
                    contract.Name,
                    log.TransactionHash,
                    log.LogIndex);
            }
        }

        return captures;
    }

    private static PolymarketOnChainTradeCapture ToCapture(
        PolymarketOnChainFill fill,
        PolymarketOnChainLog rawLog)
    {
        return new PolymarketOnChainTradeCapture(
            fill.Id,
            fill.ContractName,
            fill.ContractAddress,
            fill.ExchangeVersion,
            fill.BlockNumber,
            fill.BlockTimestampUtc,
            rawLog.BlockHash,
            fill.TransactionHash,
            rawLog.TransactionIndex,
            fill.LogIndex,
            fill.OrderHash,
            fill.Maker,
            fill.Taker,
            fill.Wallet,
            fill.Side,
            fill.TokenId,
            fill.MakerAssetId,
            fill.TakerAssetId,
            fill.MakerAmountRaw,
            fill.TakerAmountRaw,
            fill.MakerAmount,
            fill.TakerAmount,
            fill.Price,
            fill.SizeShares,
            fill.NotionalUsd,
            fill.FeeRaw,
            fill.FeeAmount,
            fill.FeeAssetId,
            fill.Builder,
            fill.Metadata,
            rawLog.Topics,
            rawLog.Data,
            rawLog.Removed,
            rawLog.ObservedAtUtc,
            fill.ImportedAtUtc);
    }

    private async Task DelayRequestIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (options.TradeCaptureRequestDelayMilliseconds <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(options.TradeCaptureRequestDelayMilliseconds), cancellationToken);
    }
}
