using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.OnChain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainIngestionProcessor(
    ILogger<OnChainIngestionProcessor> logger,
    OnChainIngestionOptions options,
    IPolygonRpcClient rpcClient,
    IAppRepository repository) : IOnChainIngestionProcessor
{
    private readonly object sync = new();
    private CancellationTokenSource? currentRunCancellation;

    public Task<OnChainIngestionResult> RefreshLookbackAsync(CancellationToken cancellationToken = default)
    {
        return RefreshWithSingleRunnerAsync(cancellationToken);
    }

    public Task<OnChainIngestionResult> RefreshBackgroundCycleAsync(CancellationToken cancellationToken = default)
    {
        return RefreshWithSingleRunnerAsync(cancellationToken);
    }

    private async Task<OnChainIngestionResult> RefreshWithSingleRunnerAsync(CancellationToken cancellationToken)
    {
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (sync)
        {
            if (currentRunCancellation is not null)
            {
                linkedCancellation.Dispose();
                throw new InvalidOperationException("On-chain ingestion is already running.");
            }

            currentRunCancellation = linkedCancellation;
        }

        try
        {
            return await RefreshLookbackCoreAsync(linkedCancellation.Token);
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(currentRunCancellation, linkedCancellation))
                {
                    currentRunCancellation = null;
                }
            }

            linkedCancellation.Dispose();
        }
    }

    public bool RequestCancel()
    {
        lock (sync)
        {
            if (currentRunCancellation is null)
            {
                return false;
            }

            currentRunCancellation.Cancel();
            return true;
        }
    }

    private async Task<OnChainIngestionResult> RefreshLookbackCoreAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("On-chain ingestion is disabled.");
            var now = DateTimeOffset.UtcNow;
            return new OnChainIngestionResult(now, now, 0, 0, 0, 0, 0);
        }

        var toUtc = DateTimeOffset.UtcNow;
        var freshFromUtc = toUtc.AddDays(-options.LookbackDays);
        var latestBlock = await rpcClient.GetLatestBlockNumberAsync(cancellationToken);
        var freshFromBlock = await FindFirstBlockAtOrAfterAsync(freshFromUtc, latestBlock, cancellationToken);
        var seedFromBlock = freshFromBlock;
        var logsFetched = 0;
        var fillsStored = 0;

        logger.LogInformation(
            "Starting Polymarket on-chain ingestion. FreshFromUtc={FreshFromUtc} ToUtc={ToUtc} FreshFromBlock={FreshFromBlock} ToBlock={ToBlock} Contracts={Contracts}",
            freshFromUtc,
            toUtc,
            freshFromBlock,
            latestBlock,
            options.ExchangeContracts.Count);

        var contractStates = new List<IngestionContractState>();

        foreach (var contract in options.ExchangeContracts)
        {
            var topic = PolymarketOnChainOrderFilledParser.GetOrderFilledTopic(contract.Version);
            var contractAddress = contract.Address.ToLowerInvariant();
            var cursor = await repository.GetOnChainIngestionCursorAsync(contractAddress, cancellationToken);
            var storedRange = await repository.GetPolymarketOnChainFillBlockRangeAsync(contractAddress, cancellationToken);
            var completedRange = GetCompletedRange(cursor, storedRange);
            var state = new IngestionContractState(
                contract,
                topic,
                contractAddress,
                DateTimeOffset.UtcNow,
                completedRange?.FromBlock ?? seedFromBlock,
                completedRange?.ToBlock ?? seedFromBlock - 1);
            contractStates.Add(state);

            logger.LogInformation(
                "Polymarket on-chain contract scan prepared. Contract={Contract} CompletedFromBlock={CompletedFromBlock} CompletedToBlock={CompletedToBlock} LatestBlock={LatestBlock} FreshFromBlock={FreshFromBlock} CursorFromBlock={CursorFromBlock} CursorToBlock={CursorToBlock} StoredFromBlock={StoredFromBlock} StoredToBlock={StoredToBlock}",
                contract.Name,
                state.CompletedFromBlock,
                state.CompletedToBlock,
                latestBlock,
                freshFromBlock,
                cursor?.FromBlock,
                cursor?.ToBlock,
                storedRange?.FromBlock,
                storedRange?.ToBlock);

            if (state.CompletedToBlock < latestBlock)
            {
                logger.LogInformation(
                    "Polymarket on-chain fresh catch-up starting. Contract={Contract} FromBlock={FromBlock} ToBlock={ToBlock}",
                    contract.Name,
                    state.CompletedToBlock + 1,
                    latestBlock);

                for (var startBlock = state.CompletedToBlock + 1; startBlock <= latestBlock; startBlock += options.MaxBlockRange)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var endBlock = Math.Min(latestBlock, startBlock + options.MaxBlockRange - 1);
                    var batch = await IngestBatchAsync("fresh", contract, topic, startBlock, endBlock, cancellationToken);
                    state.LogsFetched += batch.LogsFetched;
                    logsFetched += batch.LogsFetched;
                    state.FillsStored += batch.FillsStored;
                    fillsStored += batch.FillsStored;
                    state.CompletedToBlock = endBlock;

                    await UpsertCursorAsync(
                        state.ContractAddress,
                        state.Contract,
                        state.CompletedFromBlock,
                        state.CompletedToBlock,
                        state.LogsFetched,
                        state.FillsStored,
                        state.StartedAtUtc,
                        cancellationToken);

                    await DelayIfConfiguredAsync(cancellationToken);
                }
            }
            else
            {
                logger.LogInformation(
                    "Polymarket on-chain fresh catch-up already complete. Contract={Contract} ToBlock={ToBlock}",
                    contract.Name,
                    state.CompletedToBlock);
            }
        }

        foreach (var state in contractStates)
        {
            var rawRange = await repository.GetPolymarketOnChainFillBlockRangeAsync(state.ContractAddress, cancellationToken);
            await RefreshMissingDerivedDataAsync(state.Contract.Name, state.ContractAddress, rawRange, cancellationToken);
        }

        logger.LogInformation(
            "Polymarket on-chain ingestion finished. FromBlock={FromBlock} ToBlock={ToBlock} Logs={Logs} Fills={Fills}",
            freshFromBlock,
            latestBlock,
            logsFetched,
            fillsStored);

        return new OnChainIngestionResult(
            freshFromUtc,
            toUtc,
            freshFromBlock,
            latestBlock,
            options.ExchangeContracts.Count,
            logsFetched,
            fillsStored);
    }

    private static IngestionProgress? GetCompletedRange(
        OnChainIngestionCursor? cursor,
        OnChainBlockRange? storedRange)
    {
        if (cursor is not null && cursor.FromBlock <= cursor.ToBlock)
        {
            return new IngestionProgress(cursor.FromBlock, cursor.ToBlock);
        }

        if (storedRange is not null && storedRange.FromBlock <= storedRange.ToBlock)
        {
            return new IngestionProgress(storedRange.FromBlock, storedRange.ToBlock);
        }

        return null;
    }

    private async Task RefreshMissingDerivedDataAsync(
        string contractName,
        string contractAddress,
        OnChainBlockRange? rawRange,
        CancellationToken cancellationToken)
    {
        if (rawRange is null)
        {
            return;
        }

        var derivedRange = await repository.GetPolymarketOnChainWalletExecutionBlockRangeAsync(contractAddress, cancellationToken);
        var tradeDetailsRange = await repository.GetPolymarketOnChainTradeDetailsBlockRangeAsync(contractAddress, cancellationToken);
        if (derivedRange is null || tradeDetailsRange is null)
        {
            logger.LogInformation(
                "Refreshing on-chain serving data for existing raw fills. Contract={Contract} Blocks={FromBlock}-{ToBlock} WalletExecutionRange={WalletExecutionRange} TradeDetailsRange={TradeDetailsRange}",
                contractName,
                rawRange.FromBlock,
                rawRange.ToBlock,
                FormatRange(derivedRange),
                FormatRange(tradeDetailsRange));
            await RefreshDerivedRangeAsync(
                contractName,
                contractAddress,
                rawRange.FromBlock,
                rawRange.ToBlock,
                cancellationToken);
            return;
        }

        var servingFromBlock = Math.Max(derivedRange.FromBlock, tradeDetailsRange.FromBlock);
        var servingToBlock = Math.Min(derivedRange.ToBlock, tradeDetailsRange.ToBlock);
        if (servingFromBlock > servingToBlock)
        {
            logger.LogInformation(
                "Refreshing on-chain serving data because materialized ranges do not overlap. Contract={Contract} Blocks={FromBlock}-{ToBlock} WalletExecutionRange={WalletExecutionRange} TradeDetailsRange={TradeDetailsRange}",
                contractName,
                rawRange.FromBlock,
                rawRange.ToBlock,
                FormatRange(derivedRange),
                FormatRange(tradeDetailsRange));
            await RefreshDerivedRangeAsync(
                contractName,
                contractAddress,
                rawRange.FromBlock,
                rawRange.ToBlock,
                cancellationToken);
            return;
        }

        if (rawRange.FromBlock < servingFromBlock)
        {
            logger.LogInformation(
                "Refreshing older on-chain serving data gap. Contract={Contract} Blocks={FromBlock}-{ToBlock}",
                contractName,
                rawRange.FromBlock,
                servingFromBlock - 1);
            await RefreshDerivedRangeAsync(
                contractName,
                contractAddress,
                rawRange.FromBlock,
                servingFromBlock - 1,
                cancellationToken);
        }

        if (rawRange.ToBlock > servingToBlock)
        {
            logger.LogInformation(
                "Refreshing newer on-chain serving data gap. Contract={Contract} Blocks={FromBlock}-{ToBlock}",
                contractName,
                servingToBlock + 1,
                rawRange.ToBlock);
            await RefreshDerivedRangeAsync(
                contractName,
                contractAddress,
                servingToBlock + 1,
                rawRange.ToBlock,
                cancellationToken);
        }
    }

    private async Task RefreshDerivedRangeAsync(
        string contractName,
        string contractAddress,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken)
    {
        for (var startBlock = fromBlock; startBlock <= toBlock; startBlock += options.MaxBlockRange)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endBlock = Math.Min(toBlock, startBlock + options.MaxBlockRange - 1);
            await repository.RefreshPolymarketOnChainWalletDerivedDataAsync(
                contractAddress,
                startBlock,
                endBlock,
                cancellationToken);

            logger.LogInformation(
                "On-chain serving data refreshed. Contract={Contract} Blocks={FromBlock}-{ToBlock}",
                contractName,
                startBlock,
                endBlock);

            await DelayIfConfiguredAsync(cancellationToken);
        }
    }

    private static string FormatRange(OnChainBlockRange? range)
    {
        return range is null ? "none" : $"{range.FromBlock}-{range.ToBlock}";
    }

    private async Task<BatchIngestionResult> IngestBatchAsync(
        string phase,
        OnChainExchangeContractOptions contract,
        string topic,
        long startBlock,
        long endBlock,
        CancellationToken cancellationToken)
    {
        var logs = await rpcClient.GetLogsAsync(contract.Address, topic, startBlock, endBlock, cancellationToken);

        var observedAt = DateTimeOffset.UtcNow;
        await repository.AddPolymarketOnChainLogsAsync(
            logs.Select(log => PolymarketOnChainOrderFilledParser.ToDomainLog(log, contract, observedAt)).ToArray(),
            cancellationToken);

        var fills = await DecodeFillsAsync(logs, contract, cancellationToken);
        await repository.AddPolymarketOnChainFillsAsync(fills, cancellationToken);

        logger.LogInformation(
            "On-chain batch ingested. Phase={Phase} Contract={Contract} Blocks={StartBlock}-{EndBlock} Logs={Logs} Fills={Fills}",
            phase,
            contract.Name,
            startBlock,
            endBlock,
            logs.Count,
            fills.Count);

        return new BatchIngestionResult(logs.Count, fills.Count);
    }

    private async Task UpsertCursorAsync(
        string contractAddress,
        OnChainExchangeContractOptions contract,
        long fromBlock,
        long toBlock,
        int logsFetched,
        int fillsStored,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        await repository.UpsertOnChainIngestionCursorAsync(
            new OnChainIngestionCursor(
                contractAddress,
                contract.Name,
                contract.Version.ToUpperInvariant(),
                fromBlock,
                toBlock,
                logsFetched,
                fillsStored,
                startedAtUtc,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private async Task<IReadOnlyList<PolymarketOnChainFill>> DecodeFillsAsync(
        IReadOnlyList<PolygonRpcLog> logs,
        OnChainExchangeContractOptions contract,
        CancellationToken cancellationToken)
    {
        if (logs.Count == 0)
        {
            return [];
        }

        var blockTimestamps = new Dictionary<long, DateTimeOffset>();
        foreach (var blockNumber in logs.Select(log => log.BlockNumber).Distinct().OrderBy(blockNumber => blockNumber))
        {
            blockTimestamps[blockNumber] = await rpcClient.GetBlockTimestampAsync(blockNumber, cancellationToken);
            await DelayIfConfiguredAsync(cancellationToken);
        }

        var fills = new List<PolymarketOnChainFill>();
        foreach (var log in logs)
        {
            try
            {
                fills.Add(PolymarketOnChainOrderFilledParser.Parse(log, contract, blockTimestamps[log.BlockNumber]));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                logger.LogWarning(
                    ex,
                    "Skipping undecodable OrderFilled log. Contract={Contract} Tx={TransactionHash} LogIndex={LogIndex}",
                    contract.Name,
                    log.TransactionHash,
                    log.LogIndex);
            }
        }

        return fills;
    }

    private async Task<long> FindFirstBlockAtOrAfterAsync(
        DateTimeOffset targetUtc,
        long latestBlock,
        CancellationToken cancellationToken)
    {
        var low = 0L;
        var high = latestBlock;

        while (low < high)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mid = low + ((high - low) / 2);
            var timestamp = await rpcClient.GetBlockTimestampAsync(mid, cancellationToken);
            if (timestamp < targetUtc)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }

            await DelayIfConfiguredAsync(cancellationToken);
        }

        return low;
    }

    private Task DelayIfConfiguredAsync(CancellationToken cancellationToken)
    {
        return options.RequestDelayMilliseconds <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds), cancellationToken);
    }

    private sealed class IngestionContractState(
        OnChainExchangeContractOptions contract,
        string topic,
        string contractAddress,
        DateTimeOffset startedAtUtc,
        long completedFromBlock,
        long completedToBlock)
    {
        public OnChainExchangeContractOptions Contract { get; } = contract;

        public string Topic { get; } = topic;

        public string ContractAddress { get; } = contractAddress;

        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;

        public long CompletedFromBlock { get; set; } = completedFromBlock;

        public long CompletedToBlock { get; set; } = completedToBlock;

        public int LogsFetched { get; set; }

        public int FillsStored { get; set; }
    }

    private sealed record IngestionProgress(long FromBlock, long ToBlock);

    private sealed record BatchIngestionResult(int LogsFetched, int FillsStored);
}
