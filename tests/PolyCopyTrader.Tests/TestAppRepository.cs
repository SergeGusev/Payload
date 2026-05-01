using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

internal sealed class TestAppRepository : IAppRepository
{
    private readonly HashSet<string> leaderTradeKeys = [];

    public List<LeaderTrade> LeaderTrades { get; } = [];

    public List<LeaderPosition> LeaderPositions { get; } = [];

    public List<TraderLeaderboardSnapshot> TraderLeaderboardSnapshots { get; } = [];

    public List<TraderDiscoveryCandidate> TraderDiscoveryCandidates { get; } = [];

    public List<Signal> Signals { get; } = [];

    public List<SignalRejection> SignalRejections { get; } = [];

    public List<PaperOrder> PaperOrders { get; } = [];

    public List<PaperFill> PaperFills { get; } = [];

    public List<PaperPosition> PaperPositions { get; } = [];

    public List<DryRunOrder> DryRunOrders { get; } = [];

    public List<LiveOrder> LiveOrders { get; } = [];

    public List<LiveTradingEvent> LiveTradingEvents { get; } = [];

    public List<ApiError> ApiErrors { get; } = [];

    public List<PolymarketHttpLogEntry> PolymarketHttpLogs { get; } = [];

    public List<PolymarketOnChainLog> PolymarketOnChainLogs { get; } = [];

    public List<PolymarketOnChainFill> PolymarketOnChainFills { get; } = [];

    public List<PolymarketOnChainWalletFill> PolymarketOnChainWalletFills { get; } = [];

    public List<PolymarketOnChainWalletExecution> PolymarketOnChainWalletExecutions { get; } = [];

    public List<PolymarketOnChainTokenMetadata> PolymarketOnChainTokenMetadata { get; } = [];

    public List<PolymarketOnChainWalletPosition> PolymarketOnChainWalletPositions { get; } = [];

    public HashSet<string> PolymarketOnChainPositionRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainWalletPerformance> PolymarketOnChainWalletPerformance { get; } = [];

    public HashSet<string> PolymarketOnChainWalletPerformanceRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainWalletCategoryPerformance> PolymarketOnChainWalletCategoryPerformance { get; } = [];

    public HashSet<string> PolymarketOnChainWalletCategoryPerformanceRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<OnChainBlockRange> OnChainWalletDerivedRefreshRanges { get; } = [];

    public Action<OnChainBlockRange>? BeforeOnChainWalletDerivedRefresh { get; set; }

    public bool RebuildDerivedDataOnAddFills { get; set; } = true;

    public List<OnChainIngestionCursor> OnChainIngestionCursors { get; } = [];

    public List<ScannerStatusSnapshot> ScannerStatuses { get; } = [];

    public bool ThrowOnTryAddLeaderTrade { get; set; }

    public bool ThrowOnAddApiError { get; set; }

    public Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        return TryAddLeaderTradeAsync(trade, cancellationToken);
    }

    public Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        if (ThrowOnTryAddLeaderTrade)
        {
            throw new InvalidOperationException("temporary database failure");
        }

        if (!leaderTradeKeys.Add(LeaderTradeDeduplication.BuildKey(trade)))
        {
            return Task.FromResult(false);
        }

        LeaderTrades.Add(trade);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LeaderTrade>>(LeaderTrades.OrderByDescending(item => item.TimestampUtc).Take(limit).ToArray());
    }

    public Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default)
    {
        LeaderPositions.Add(position);
        return Task.CompletedTask;
    }

    public Task AddTraderLeaderboardSnapshotsAsync(
        IReadOnlyList<TraderLeaderboardSnapshot> snapshots,
        CancellationToken cancellationToken = default)
    {
        foreach (var snapshot in snapshots)
        {
            TraderLeaderboardSnapshots.RemoveAll(item =>
                string.Equals(item.Category, snapshot.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.TimePeriod, snapshot.TimePeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Wallet, snapshot.Wallet, StringComparison.OrdinalIgnoreCase));
            TraderLeaderboardSnapshots.Add(snapshot);
        }

        return Task.CompletedTask;
    }

    public Task UpsertTraderDiscoveryCandidatesAsync(
        IReadOnlyList<TraderDiscoveryCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates)
        {
            TraderDiscoveryCandidates.RemoveAll(item =>
                string.Equals(item.DiscoveryType, candidate.DiscoveryType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Category, candidate.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.TimePeriod, candidate.TimePeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Wallet, candidate.Wallet, StringComparison.OrdinalIgnoreCase));
            TraderDiscoveryCandidates.Add(candidate);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TraderDiscoveryCandidate>> GetRecentTraderDiscoveryCandidatesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderDiscoveryCandidate>>(
            TraderDiscoveryCandidates
                .OrderByDescending(item => item.SnapshotAtUtc)
                .Take(limit)
                .ToArray());
    }

    public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        Signals.Add(signal);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalSummary>> GetRecentSignalsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalSummary>>(Signals
            .OrderByDescending(signal => signal.CreatedAtUtc)
            .Take(limit)
            .Select(signal => new SignalSummary(
                signal.Id,
                signal.LeaderTrade.TraderWallet,
                signal.LeaderTrade.ConditionId,
                signal.LeaderTrade.AssetId,
                signal.LeaderTrade.Outcome,
                signal.LeaderTrade.Price,
                null,
                null,
                null,
                null,
                (int)Math.Max(0, (signal.CreatedAtUtc - signal.LeaderTrade.TimestampUtc).TotalSeconds),
                signal.Score,
                signal.Accepted,
                signal.DecisionCode,
                signal.Reasons,
                signal.ProposedPaperPrice,
                signal.ProposedSizeShares,
                signal.ProposedNotionalUsd,
                signal.CreatedAtUtc))
            .ToArray());
    }

    public Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        SignalRejections.Add(rejection);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SignalRejection>> GetRecentSignalRejectionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SignalRejection>>(SignalRejections.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        PaperOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        PaperOrders.RemoveAll(item => item.Id == order.Id);
        PaperOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>(PaperOrders
            .Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled)
            .ToArray());
    }

    public Task<IReadOnlyList<PaperOrder>> GetRecentPaperOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>(PaperOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default)
    {
        PaperFills.Add(fill);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperFill>> GetRecentPaperFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperFill>>(PaperFills.OrderByDescending(item => item.FilledAtUtc).Take(limit).ToArray());
    }

    public Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        PaperPositions.RemoveAll(item => string.Equals(item.AssetId, position.AssetId, StringComparison.OrdinalIgnoreCase));
        PaperPositions.Add(position);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPosition>>(PaperPositions.ToArray());
    }

    public Task AddDryRunOrderAsync(DryRunOrder order, CancellationToken cancellationToken = default)
    {
        DryRunOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DryRunOrder>> GetRecentDryRunOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DryRunOrder>>(DryRunOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        LiveOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task UpdateLiveOrderAsync(LiveOrder order, CancellationToken cancellationToken = default)
    {
        LiveOrders.RemoveAll(item => item.Id == order.Id);
        LiveOrders.Add(order);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders
            .Where(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.CancelRequested)
            .ToArray());
    }

    public Task<IReadOnlyList<LiveOrder>> GetRecentLiveOrdersAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddLiveTradingEventAsync(LiveTradingEvent liveEvent, CancellationToken cancellationToken = default)
    {
        LiveTradingEvents.Add(liveEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveTradingEvent>> GetRecentLiveTradingEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveTradingEvent>>(LiveTradingEvents.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        if (ThrowOnAddApiError)
        {
            throw new InvalidOperationException("temporary database failure while recording error");
        }

        ApiErrors.Add(error);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiError>> GetRecentApiErrorsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ApiError>>(ApiErrors.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToArray());
    }

    public Task AddPolymarketHttpLogAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        PolymarketHttpLogs.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketHttpLogEntry>> GetRecentPolymarketHttpLogsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketHttpLogEntry>>(
            PolymarketHttpLogs.OrderByDescending(item => item.RequestedAtUtc).Take(limit).ToArray());
    }

    public Task AddPolymarketOnChainLogsAsync(IReadOnlyList<PolymarketOnChainLog> logs, CancellationToken cancellationToken = default)
    {
        foreach (var log in logs)
        {
            PolymarketOnChainLogs.RemoveAll(item =>
                string.Equals(item.TransactionHash, log.TransactionHash, StringComparison.OrdinalIgnoreCase) &&
                item.LogIndex == log.LogIndex);
            PolymarketOnChainLogs.Add(log);
        }

        return Task.CompletedTask;
    }

    public Task AddPolymarketOnChainFillsAsync(IReadOnlyList<PolymarketOnChainFill> fills, CancellationToken cancellationToken = default)
    {
        foreach (var fill in fills)
        {
            PolymarketOnChainFills.RemoveAll(item =>
                string.Equals(item.TransactionHash, fill.TransactionHash, StringComparison.OrdinalIgnoreCase) &&
                item.LogIndex == fill.LogIndex);
            PolymarketOnChainFills.Add(fill);
            PolymarketOnChainPositionRefreshQueue.Add(fill.TokenId);
        }

        if (RebuildDerivedDataOnAddFills)
        {
            RebuildOnChainWalletDerivedData();
        }

        return Task.CompletedTask;
    }

    public Task UpsertOnChainIngestionCursorAsync(OnChainIngestionCursor cursor, CancellationToken cancellationToken = default)
    {
        OnChainIngestionCursors.RemoveAll(item =>
            string.Equals(item.ContractAddress, cursor.ContractAddress, StringComparison.OrdinalIgnoreCase));
        OnChainIngestionCursors.Add(cursor);
        return Task.CompletedTask;
    }

    public Task<OnChainIngestionCursor?> GetOnChainIngestionCursorAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainIngestionCursor?>(
            OnChainIngestionCursors.FirstOrDefault(item =>
                string.Equals(item.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<long?> GetLatestPolymarketOnChainFillBlockAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        var blocks = PolymarketOnChainFills
            .Where(item => string.Equals(item.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.BlockNumber)
            .ToArray();
        return Task.FromResult(blocks.Length == 0 ? (long?)null : blocks.Max());
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainFillBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        var blocks = PolymarketOnChainFills
            .Where(item => string.Equals(item.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.BlockNumber)
            .ToArray();
        return Task.FromResult(blocks.Length == 0 ? null : new OnChainBlockRange(blocks.Min(), blocks.Max()));
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainWalletExecutionBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        var blocks = PolymarketOnChainWalletExecutions
            .Where(item => string.Equals(item.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.BlockNumber)
            .ToArray();
        return Task.FromResult(blocks.Length == 0 ? null : new OnChainBlockRange(blocks.Min(), blocks.Max()));
    }

    public Task<OnChainBlockRange?> GetPolymarketOnChainTradeDetailsBlockRangeAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return GetPolymarketOnChainFillBlockRangeAsync(contractAddress, cancellationToken);
    }

    public Task RefreshPolymarketOnChainWalletDerivedDataAsync(string contractAddress, long fromBlock, long toBlock, CancellationToken cancellationToken = default)
    {
        BeforeOnChainWalletDerivedRefresh?.Invoke(new OnChainBlockRange(fromBlock, toBlock));
        OnChainWalletDerivedRefreshRanges.Add(new OnChainBlockRange(fromBlock, toBlock));
        RebuildOnChainWalletDerivedData();
        foreach (var tokenId in PolymarketOnChainWalletExecutions
            .Where(item => string.Equals(item.ContractAddress, contractAddress, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.BlockNumber >= fromBlock && item.BlockNumber <= toBlock)
            .Select(item => item.TokenId)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PolymarketOnChainPositionRefreshQueue.Add(tokenId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletExecution>> GetRecentPolymarketOnChainWalletExecutionsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletExecution>>(
            PolymarketOnChainWalletExecutions
                .OrderByDescending(item => item.BlockTimestampUtc)
                .ThenByDescending(item => item.BlockNumber)
                .ThenByDescending(item => item.FirstLogIndex)
                .Take(limit)
                .ToArray());
    }

    public Task<IReadOnlyList<string>> GetOnChainTokenIdsMissingMetadataAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var metadataByToken = PolymarketOnChainTokenMetadata
            .GroupBy(item => item.TokenId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<string>>(
            PolymarketOnChainWalletExecutions
                .Select(item => item.TokenId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(tokenId =>
                    !metadataByToken.TryGetValue(tokenId, out var metadata) ||
                    !metadata.LookupSucceeded ||
                    string.IsNullOrWhiteSpace(metadata.Category))
                .OrderBy(tokenId => tokenId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray());
    }

    public Task UpsertPolymarketOnChainTokenMetadataAsync(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata, CancellationToken cancellationToken = default)
    {
        foreach (var item in metadata)
        {
            PolymarketOnChainTokenMetadata.RemoveAll(existing =>
                string.Equals(existing.TokenId, item.TokenId, StringComparison.OrdinalIgnoreCase));
            PolymarketOnChainTokenMetadata.Add(item);
            PolymarketOnChainPositionRefreshQueue.Add(item.TokenId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainFill>> GetRecentPolymarketOnChainFillsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainFill>>(
            PolymarketOnChainFills.OrderByDescending(item => item.BlockTimestampUtc).Take(limit).ToArray());
    }

    public Task<IReadOnlyList<TraderOnChainStats>> GetTraderOnChainStatsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var stats = PolymarketOnChainWalletExecutions
            .GroupBy(item => item.Wallet, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var executions = group.ToArray();
                var volume = executions.Sum(item => item.NotionalUsd);
                var feesUsd = executions.Sum(item => item.FeesUsd);
                return new TraderOnChainStats(
                    group.Key,
                    executions.Length,
                    executions.Count(item => item.Side == TradeSide.Buy),
                    executions.Count(item => item.Side == TradeSide.Sell),
                    executions.Select(item => item.TokenId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    volume,
                    executions.Length == 0 ? 0m : volume / executions.Length,
                    feesUsd,
                    volume + executions.Length + executions.Select(item => item.TokenId).Distinct(StringComparer.OrdinalIgnoreCase).Count() * 5,
                    executions.Min(item => item.BlockTimestampUtc),
                    executions.Max(item => item.BlockTimestampUtc));
            })
            .OrderByDescending(item => item.ActivityScore)
            .ThenByDescending(item => item.VolumeUsd)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<TraderOnChainStats>>(stats);
    }

    public Task<OnChainActivityRefreshResult> RefreshPolymarketOnChainWalletActivityAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainActivityRefreshResult(0, 0, 0, 0));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletPosition>> GetPolymarketOnChainWalletPositionsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletPosition>>(
            PolymarketOnChainWalletPositions
                .OrderByDescending(item => Math.Abs(item.NetCostUsd))
                .ThenByDescending(item => item.VolumeUsd)
                .ThenByDescending(item => item.LastTradeUtc)
                .Take(limit)
                .ToArray());
    }

    public Task<OnChainPositionRefreshResult> RefreshPolymarketOnChainWalletPositionsAsync(
        int tokenLimit = 50,
        int queueSeedTokenLimit = 500,
        CancellationToken cancellationToken = default)
    {
        var queued = 0;
        var knownPositionTokens = PolymarketOnChainWalletPositions
            .Select(item => item.TokenId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tokenId in PolymarketOnChainWalletExecutions
            .Select(item => item.TokenId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(tokenId => !knownPositionTokens.Contains(tokenId))
            .Take(queueSeedTokenLimit))
        {
            if (PolymarketOnChainPositionRefreshQueue.Add(tokenId))
            {
                queued++;
            }
        }

        var tokenIds = PolymarketOnChainPositionRefreshQueue
            .OrderBy(tokenId => tokenId, StringComparer.Ordinal)
            .Take(tokenLimit)
            .ToArray();
        if (tokenIds.Length == 0)
        {
            return Task.FromResult(new OnChainPositionRefreshResult(queued, 0, 0, 0));
        }

        RefreshPositionsForTokens(tokenIds);
        foreach (var tokenId in tokenIds)
        {
            PolymarketOnChainPositionRefreshQueue.Remove(tokenId);
        }

        return Task.FromResult(new OnChainPositionRefreshResult(
            queued,
            tokenIds.Length,
            PolymarketOnChainWalletPositions.Count(item => tokenIds.Contains(item.TokenId, StringComparer.OrdinalIgnoreCase)),
            PolymarketOnChainPositionRefreshQueue.Count));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletPerformance>> GetPolymarketOnChainWalletPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletPerformance>>(
            PolymarketOnChainWalletPerformance
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.ResolvedPnlUsd)
                .ThenByDescending(item => item.VolumeUsd)
                .Take(limit)
                .ToArray());
    }

    public Task<OnChainPerformanceRefreshResult> RefreshPolymarketOnChainWalletPerformanceAsync(
        int walletLimit = 100,
        int queueSeedWalletLimit = 500,
        CancellationToken cancellationToken = default)
    {
        var queued = 0;
        var knownWallets = PolymarketOnChainWalletPerformance
            .Select(item => item.Wallet)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var wallet in PolymarketOnChainWalletPositions
            .Select(item => item.Wallet)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(wallet => !knownWallets.Contains(wallet))
            .Take(queueSeedWalletLimit))
        {
            if (PolymarketOnChainWalletPerformanceRefreshQueue.Add(wallet))
            {
                queued++;
            }
        }

        var wallets = PolymarketOnChainWalletPerformanceRefreshQueue
            .OrderBy(wallet => wallet, StringComparer.Ordinal)
            .Take(walletLimit)
            .ToArray();
        if (wallets.Length == 0)
        {
            return Task.FromResult(new OnChainPerformanceRefreshResult(queued, 0, 0, 0));
        }

        RefreshPerformanceForWallets(wallets);
        foreach (var wallet in wallets)
        {
            PolymarketOnChainWalletPerformanceRefreshQueue.Remove(wallet);
        }

        return Task.FromResult(new OnChainPerformanceRefreshResult(
            queued,
            wallets.Length,
            PolymarketOnChainWalletPerformance.Count(item => wallets.Contains(item.Wallet, StringComparer.OrdinalIgnoreCase)),
            PolymarketOnChainWalletPerformanceRefreshQueue.Count));
    }

    public Task<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var rows = PolymarketOnChainWalletCategoryPerformance.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category))
        {
            rows = rows.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<PolymarketOnChainWalletCategoryPerformance>>(
            rows
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.ResolvedPnlUsd)
                .ThenByDescending(item => item.VolumeUsd)
                .Take(limit)
                .ToArray());
    }

    public Task<OnChainCategoryPerformanceRefreshResult> RefreshPolymarketOnChainWalletCategoryPerformanceAsync(
        int pairLimit = 500,
        int queueSeedPairLimit = 1_000,
        CancellationToken cancellationToken = default)
    {
        var queued = 0;
        var knownPairs = PolymarketOnChainWalletCategoryPerformance
            .Select(item => CategoryPerformanceKey(item.Wallet, item.Category))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in PolymarketOnChainWalletPositions
            .Select(item => CategoryPerformanceKey(item.Wallet, CategoryName(item.Category)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(pair => !knownPairs.Contains(pair))
            .Take(queueSeedPairLimit))
        {
            if (PolymarketOnChainWalletCategoryPerformanceRefreshQueue.Add(pair))
            {
                queued++;
            }
        }

        var pairs = PolymarketOnChainWalletCategoryPerformanceRefreshQueue
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .Take(pairLimit)
            .Select(SplitCategoryPerformanceKey)
            .ToArray();
        if (pairs.Length == 0)
        {
            return Task.FromResult(new OnChainCategoryPerformanceRefreshResult(queued, 0, 0, 0));
        }

        RefreshCategoryPerformanceForPairs(pairs);
        foreach (var pair in pairs)
        {
            PolymarketOnChainWalletCategoryPerformanceRefreshQueue.Remove(CategoryPerformanceKey(pair.Wallet, pair.Category));
        }

        return Task.FromResult(new OnChainCategoryPerformanceRefreshResult(
            queued,
            pairs.Length,
            PolymarketOnChainWalletCategoryPerformance.Count(item =>
                pairs.Any(pair =>
                    string.Equals(pair.Wallet, item.Wallet, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pair.Category, item.Category, StringComparison.OrdinalIgnoreCase))),
            PolymarketOnChainWalletCategoryPerformanceRefreshQueue.Count));
    }

    public Task<IReadOnlyList<PolymarketOnChainTradeDetails>> GetRecentPolymarketOnChainTradeDetailsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        var metadataByToken = PolymarketOnChainTokenMetadata
            .GroupBy(item => item.TokenId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var rows = PolymarketOnChainFills
            .OrderByDescending(item => item.BlockTimestampUtc)
            .ThenByDescending(item => item.BlockNumber)
            .ThenByDescending(item => item.LogIndex)
            .Take(limit)
            .Select(fill =>
            {
                metadataByToken.TryGetValue(fill.TokenId, out var metadata);
                return new PolymarketOnChainTradeDetails(
                    fill.ContractName,
                    fill.ContractAddress,
                    fill.ExchangeVersion,
                    fill.BlockNumber,
                    fill.BlockTimestampUtc,
                    fill.TransactionHash,
                    fill.LogIndex,
                    fill.OrderHash,
                    fill.Maker,
                    fill.Taker,
                    fill.Side,
                    Opposite(fill.Side),
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
                    fill.FeeAmount,
                    fill.FeeAssetId,
                    fill.Builder,
                    fill.Metadata,
                    metadata?.ConditionId ?? string.Empty,
                    metadata?.MarketId ?? string.Empty,
                    metadata?.MarketSlug ?? string.Empty,
                    string.IsNullOrWhiteSpace(metadata?.MarketTitle) ? "Unenriched token " + fill.TokenId[..Math.Min(16, fill.TokenId.Length)] : metadata!.MarketTitle,
                    string.IsNullOrWhiteSpace(metadata?.Outcome) ? "Unknown" : metadata!.Outcome,
                    metadata?.Category,
                    metadata?.LookupSucceeded ?? false,
                    metadata?.Active ?? false,
                    metadata?.Closed ?? false,
                    metadata?.Archived ?? false,
                    metadata?.Resolved ?? false,
                    metadata?.WinningOutcome,
                    fill.ImportedAtUtc);
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<PolymarketOnChainTradeDetails>>(rows);
    }

    public Task<IReadOnlyList<PolymarketOnChainParticipantDetails>> GetPolymarketOnChainParticipantDetailsAsync(int limit = 250, CancellationToken cancellationToken = default)
    {
        var performanceByWallet = PolymarketOnChainWalletPerformance
            .GroupBy(item => item.Wallet, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var positionsByWallet = PolymarketOnChainWalletPositions
            .GroupBy(item => item.Wallet, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var rows = PolymarketOnChainWalletExecutions
            .GroupBy(item => item.Wallet, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var executions = group.ToArray();
                var volume = executions.Sum(item => item.NotionalUsd);
                var fees = executions.Sum(item => item.FeesUsd);
                performanceByWallet.TryGetValue(group.Key, out var performance);
                positionsByWallet.TryGetValue(group.Key, out var positions);
                positions ??= [];
                var positionsCount = positions.Length;
                var openPositions = positions.Count(item => item.PositionStatus == "Open");
                var flatPositions = positions.Count(item => item.PositionStatus == "Flat");
                var resolvedPositions = positions.Count(item => item.PositionStatus == "Resolved");
                var profitableResolved = positions.Count(item => item.PositionStatus == "Resolved" && (item.ResolvedPnlUsd ?? 0m) > 0m);
                var losingResolved = positions.Count(item => item.PositionStatus == "Resolved" && (item.ResolvedPnlUsd ?? 0m) < 0m);
                var openExposure = positions.Where(item => item.PositionStatus == "Open").Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedCost = positions.Where(item => item.PositionStatus == "Resolved" && item.ResolvedPnlUsd.HasValue).Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedPnl = positions.Sum(item => item.ResolvedPnlUsd ?? 0m);
                var activityScore = volume + executions.Length + executions.Select(item => item.TokenId).Distinct(StringComparer.OrdinalIgnoreCase).Count() * 5;

                return new PolymarketOnChainParticipantDetails(
                    group.Key,
                    executions.Length,
                    executions.Count(item => item.Side == TradeSide.Buy),
                    executions.Count(item => item.Side == TradeSide.Sell),
                    executions.Select(item => item.TokenId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    volume,
                    executions.Length == 0 ? 0m : volume / executions.Length,
                    fees,
                    activityScore,
                    performance?.PositionsCount ?? positionsCount,
                    performance?.OpenPositions ?? openPositions,
                    performance?.FlatPositions ?? flatPositions,
                    performance?.ResolvedPositions ?? resolvedPositions,
                    performance?.ProfitableResolvedPositions ?? profitableResolved,
                    performance?.LosingResolvedPositions ?? losingResolved,
                    performance?.OpenExposureUsd ?? openExposure,
                    performance?.ResolvedCostUsd ?? resolvedCost,
                    performance?.ResolvedPnlUsd ?? resolvedPnl,
                    performance?.ResolvedRoiPct ?? 0m,
                    performance?.WinRatePct ?? 0m,
                    performance?.AveragePositionSizeUsd ?? 0m,
                    performance?.Score ?? activityScore,
                    performance?.SampleQuality ?? "ActivityOnly",
                    executions.Min(item => item.BlockTimestampUtc),
                    executions.Max(item => item.BlockTimestampUtc),
                    executions.Max(item => item.ImportedAtUtc),
                    performance?.RefreshedAtUtc);
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.VolumeUsd)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<PolymarketOnChainParticipantDetails>>(rows);
    }

    private void RefreshPositionsForTokens(IReadOnlyList<string> tokenIds)
    {
        var tokenSet = tokenIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var affectedCategoryPairs = PolymarketOnChainWalletPositions
            .Where(item => tokenSet.Contains(item.TokenId))
            .Select(item => CategoryPerformanceKey(item.Wallet, CategoryName(item.Category)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PolymarketOnChainWalletPositions.RemoveAll(item => tokenSet.Contains(item.TokenId));
        var metadataByToken = PolymarketOnChainTokenMetadata.ToDictionary(
            item => item.TokenId,
            StringComparer.OrdinalIgnoreCase);
        var positions = PolymarketOnChainWalletExecutions
            .Where(item => tokenSet.Contains(item.TokenId))
            .GroupBy(
                item =>
                {
                    metadataByToken.TryGetValue(item.TokenId, out var metadata);
                    return new PositionKey(
                        item.Wallet,
                        item.TokenId,
                        metadata?.ConditionId ?? item.TokenId,
                        metadata?.MarketId ?? string.Empty,
                        metadata?.MarketSlug ?? string.Empty,
                        string.IsNullOrWhiteSpace(metadata?.MarketTitle) ? "Unenriched token " + item.TokenId[..Math.Min(16, item.TokenId.Length)] : metadata!.MarketTitle,
                        string.IsNullOrWhiteSpace(metadata?.Outcome) ? "Unknown" : metadata!.Outcome,
                        metadata?.Category,
                        metadata?.LookupSucceeded ?? false,
                        metadata?.Resolved ?? false,
                        metadata?.WinningOutcome);
                })
            .Select(group =>
            {
                var rows = group.ToArray();
                var buyShares = rows.Where(item => item.Side == TradeSide.Buy).Sum(item => item.SizeShares);
                var sellShares = rows.Where(item => item.Side == TradeSide.Sell).Sum(item => item.SizeShares);
                var buyNotional = rows.Where(item => item.Side == TradeSide.Buy).Sum(item => item.NotionalUsd);
                var sellNotional = rows.Where(item => item.Side == TradeSide.Sell).Sum(item => item.NotionalUsd);
                var fees = rows.Sum(item => item.FeesUsd);
                var netShares = buyShares - sellShares;
                var netCost = buyNotional - sellNotional + fees;
                var resolvedPnl = group.Key.MarketResolved && !string.IsNullOrWhiteSpace(group.Key.WinningOutcome)
                    ? (string.Equals(group.Key.Outcome, group.Key.WinningOutcome, StringComparison.OrdinalIgnoreCase) ? netShares : 0m) - netCost
                    : (decimal?)null;

                return new PolymarketOnChainWalletPosition(
                    group.Key.Wallet,
                    group.Key.TokenId,
                    group.Key.ConditionId,
                    group.Key.MarketId,
                    group.Key.MarketSlug,
                    group.Key.MarketTitle,
                    group.Key.Outcome,
                    group.Key.Category,
                    group.Key.LookupSucceeded,
                    group.Key.MarketResolved,
                    group.Key.WinningOutcome,
                    rows.Length,
                    rows.Count(item => item.Side == TradeSide.Buy),
                    rows.Count(item => item.Side == TradeSide.Sell),
                    buyShares,
                    sellShares,
                    netShares,
                    buyNotional,
                    sellNotional,
                    netCost,
                    fees,
                    buyShares == 0m ? 0m : buyNotional / buyShares,
                    sellShares == 0m ? 0m : sellNotional / sellShares,
                    rows.Sum(item => item.NotionalUsd),
                    resolvedPnl,
                    group.Key.MarketResolved ? "Resolved" : Math.Abs(netShares) < 0.00000001m ? "Flat" : "Open",
                    rows.Min(item => item.BlockTimestampUtc),
                    rows.Max(item => item.BlockTimestampUtc));
            })
            .OrderByDescending(item => Math.Abs(item.NetCostUsd))
            .ThenByDescending(item => item.VolumeUsd)
            .ThenByDescending(item => item.LastTradeUtc)
            .ToArray();

        PolymarketOnChainWalletPositions.AddRange(positions);
        foreach (var wallet in positions.Select(item => item.Wallet).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            PolymarketOnChainWalletPerformanceRefreshQueue.Add(wallet);
        }

        foreach (var pair in positions.Select(item => CategoryPerformanceKey(item.Wallet, CategoryName(item.Category))))
        {
            affectedCategoryPairs.Add(pair);
        }

        foreach (var pair in affectedCategoryPairs)
        {
            PolymarketOnChainWalletCategoryPerformanceRefreshQueue.Add(pair);
        }
    }

    private void RefreshPerformanceForWallets(IReadOnlyList<string> wallets)
    {
        var walletSet = wallets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        PolymarketOnChainWalletPerformance.RemoveAll(item => walletSet.Contains(item.Wallet));

        var rows = PolymarketOnChainWalletPositions
            .Where(item => walletSet.Contains(item.Wallet))
            .GroupBy(item => item.Wallet, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var positions = group.ToArray();
                var resolved = positions.Where(item => item.PositionStatus == "Resolved").ToArray();
                var volume = positions.Sum(item => item.VolumeUsd);
                var resolvedVolume = resolved.Sum(item => item.VolumeUsd);
                var openExposure = positions.Where(item => item.PositionStatus == "Open").Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedCost = resolved.Where(item => item.ResolvedPnlUsd is not null).Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedPnl = resolved.Sum(item => item.ResolvedPnlUsd ?? 0m);
                var profitable = resolved.Count(item => (item.ResolvedPnlUsd ?? 0m) > 0m);
                var losing = resolved.Count(item => (item.ResolvedPnlUsd ?? 0m) < 0m);
                var roi = resolvedCost == 0m ? 0m : resolvedPnl / resolvedCost * 100m;
                var winRate = resolved.Length == 0 ? 0m : profitable * 100m / resolved.Length;
                var samplePenalty = resolved.Length < 5 ? (5 - resolved.Length) * 10m : 0m;
                var score = resolvedPnl + roi * 2m + profitable * 5m +
                    (decimal)Math.Log((double)(volume + 1m)) +
                    Math.Min(resolved.Length, 50) * 2m -
                    openExposure * 0.02m -
                    samplePenalty;

                return new PolymarketOnChainWalletPerformance(
                    group.Key,
                    positions.Length,
                    positions.Count(item => item.PositionStatus == "Open"),
                    positions.Count(item => item.PositionStatus == "Flat"),
                    resolved.Length,
                    profitable,
                    losing,
                    positions.Select(item => item.ConditionId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    volume,
                    resolvedVolume,
                    openExposure,
                    resolvedCost,
                    resolvedPnl,
                    roi,
                    winRate,
                    positions.Length == 0 ? 0m : positions.Average(item => Math.Abs(item.NetCostUsd)),
                    score,
                    resolved.Length >= 25 && volume >= 1_000m ? "High" :
                        resolved.Length >= 10 ? "Medium" :
                        resolved.Length >= 3 ? "Low" : "Thin",
                    positions.Min(item => item.FirstTradeUtc),
                    positions.Max(item => item.LastTradeUtc),
                    DateTimeOffset.UtcNow);
            });

        PolymarketOnChainWalletPerformance.AddRange(rows);
    }

    private void RefreshCategoryPerformanceForPairs(IReadOnlyList<CategoryPerformancePair> pairs)
    {
        PolymarketOnChainWalletCategoryPerformance.RemoveAll(item =>
            pairs.Any(pair =>
                string.Equals(pair.Wallet, item.Wallet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.Category, item.Category, StringComparison.OrdinalIgnoreCase)));

        var rows = PolymarketOnChainWalletPositions
            .Where(item => pairs.Any(pair =>
                string.Equals(pair.Wallet, item.Wallet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.Category, CategoryName(item.Category), StringComparison.OrdinalIgnoreCase)))
            .GroupBy(
                item => new CategoryPerformancePair(item.Wallet, CategoryName(item.Category)),
                CategoryPerformancePairComparer.Instance)
            .Select(group =>
            {
                var positions = group.ToArray();
                var resolved = positions.Where(item => item.PositionStatus == "Resolved").ToArray();
                var volume = positions.Sum(item => item.VolumeUsd);
                var resolvedVolume = resolved.Sum(item => item.VolumeUsd);
                var openExposure = positions.Where(item => item.PositionStatus == "Open").Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedCost = resolved.Where(item => item.ResolvedPnlUsd is not null).Sum(item => Math.Abs(item.NetCostUsd));
                var resolvedPnl = resolved.Sum(item => item.ResolvedPnlUsd ?? 0m);
                var profitable = resolved.Count(item => (item.ResolvedPnlUsd ?? 0m) > 0m);
                var losing = resolved.Count(item => (item.ResolvedPnlUsd ?? 0m) < 0m);
                var roi = resolvedCost == 0m ? 0m : resolvedPnl / resolvedCost * 100m;
                var winRate = resolved.Length == 0 ? 0m : profitable * 100m / resolved.Length;
                var samplePenalty = resolved.Length < 5 ? (5 - resolved.Length) * 10m : 0m;
                var score = resolvedPnl + roi * 2m + profitable * 5m +
                    (decimal)Math.Log((double)(volume + 1m)) +
                    Math.Min(resolved.Length, 50) * 2m -
                    openExposure * 0.02m -
                    samplePenalty;

                return new PolymarketOnChainWalletCategoryPerformance(
                    group.Key.Wallet,
                    group.Key.Category,
                    positions.Length,
                    positions.Count(item => item.PositionStatus == "Open"),
                    positions.Count(item => item.PositionStatus == "Flat"),
                    resolved.Length,
                    profitable,
                    losing,
                    positions.Select(item => item.ConditionId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    volume,
                    resolvedVolume,
                    openExposure,
                    resolvedCost,
                    resolvedPnl,
                    roi,
                    winRate,
                    positions.Length == 0 ? 0m : positions.Average(item => Math.Abs(item.NetCostUsd)),
                    score,
                    resolved.Length >= 25 && volume >= 1_000m ? "High" :
                        resolved.Length >= 10 ? "Medium" :
                        resolved.Length >= 3 ? "Low" : "Thin",
                    positions.Min(item => item.FirstTradeUtc),
                    positions.Max(item => item.LastTradeUtc),
                    DateTimeOffset.UtcNow);
            });

        PolymarketOnChainWalletCategoryPerformance.AddRange(rows);
    }

    private static string CategoryName(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? "unknown" : category;
    }

    private static string CategoryPerformanceKey(string wallet, string category)
    {
        return wallet + "\u001F" + category;
    }

    private static CategoryPerformancePair SplitCategoryPerformanceKey(string key)
    {
        var parts = key.Split('\u001F', 2);
        return new CategoryPerformancePair(parts[0], parts.Length > 1 ? parts[1] : "unknown");
    }

    private sealed record CategoryPerformancePair(string Wallet, string Category);

    private sealed class CategoryPerformancePairComparer : IEqualityComparer<CategoryPerformancePair>
    {
        public static readonly CategoryPerformancePairComparer Instance = new();

        public bool Equals(CategoryPerformancePair? x, CategoryPerformancePair? y)
        {
            return x is not null &&
                y is not null &&
                string.Equals(x.Wallet, y.Wallet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Category, y.Category, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CategoryPerformancePair obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Wallet),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Category));
        }
    }

    private sealed record PositionKey(
        string Wallet,
        string TokenId,
        string ConditionId,
        string MarketId,
        string MarketSlug,
        string MarketTitle,
        string Outcome,
        string? Category,
        bool LookupSucceeded,
        bool MarketResolved,
        string? WinningOutcome);

    private void RebuildOnChainWalletDerivedData()
    {
        PolymarketOnChainWalletFills.Clear();
        foreach (var fill in PolymarketOnChainFills)
        {
            PolymarketOnChainWalletFills.Add(ToWalletFill(fill, OnChainParticipantRole.Maker));
            PolymarketOnChainWalletFills.Add(ToWalletFill(fill, OnChainParticipantRole.Taker));
        }

        PolymarketOnChainWalletExecutions.Clear();
        PolymarketOnChainWalletExecutions.AddRange(
            PolymarketOnChainWalletFills
                .GroupBy(
                    item => new
                    {
                        ContractAddress = item.ContractAddress.ToLowerInvariant(),
                        TransactionHash = item.TransactionHash.ToLowerInvariant(),
                        Wallet = item.Wallet.ToLowerInvariant(),
                        item.Side,
                        item.TokenId
                    })
                .Select(group =>
                {
                    var rows = group.ToArray();
                    var size = rows.Sum(item => item.SizeShares);
                    var notional = rows.Sum(item => item.NotionalUsd);
                    return new PolymarketOnChainWalletExecution(
                        rows[0].ContractName,
                        rows[0].ContractAddress,
                        rows[0].ExchangeVersion,
                        rows.Min(item => item.BlockNumber),
                        rows.Min(item => item.BlockTimestampUtc),
                        rows[0].TransactionHash,
                        rows.Min(item => item.LogIndex),
                        rows.Max(item => item.LogIndex),
                        rows[0].Wallet,
                        rows[0].Side,
                        rows[0].TokenId,
                        rows.Length,
                        rows.Count(item => item.Role == OnChainParticipantRole.Maker),
                        rows.Count(item => item.Role == OnChainParticipantRole.Taker),
                        size,
                        notional,
                        size == 0m ? 0m : notional / size,
                        rows.Where(item => item.FeeAssetId == "0").Sum(item => item.FeeAmount),
                        rows.Max(item => item.ImportedAtUtc));
                }));
    }

    private static PolymarketOnChainWalletFill ToWalletFill(
        PolymarketOnChainFill fill,
        OnChainParticipantRole role)
    {
        var isMaker = role == OnChainParticipantRole.Maker;
        return new PolymarketOnChainWalletFill(
            fill.Id,
            fill.ContractName,
            fill.ContractAddress,
            fill.ExchangeVersion,
            fill.BlockNumber,
            fill.BlockTimestampUtc,
            fill.TransactionHash,
            fill.LogIndex,
            fill.OrderHash,
            role,
            isMaker ? fill.Maker : fill.Taker,
            isMaker ? fill.Taker : fill.Maker,
            isMaker ? fill.Side : Opposite(fill.Side),
            fill.TokenId,
            fill.Price,
            fill.SizeShares,
            fill.NotionalUsd,
            isMaker ? fill.FeeAmount : 0m,
            isMaker ? fill.FeeAssetId : "0",
            fill.ImportedAtUtc);
    }

    private static TradeSide Opposite(TradeSide side)
    {
        return side switch
        {
            TradeSide.Buy => TradeSide.Sell,
            TradeSide.Sell => TradeSide.Buy,
            _ => side
        };
    }

    public Task<IReadOnlyList<RiskEvent>> GetRecentRiskEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RiskEvent>>([]);
    }

    public Task AddOrderBookSnapshotAsync(OrderBookSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OrderBookSnapshot?>(null);
    }

    public Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OrderBookSnapshot>>([]);
    }

    public Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MarketDataEvent>>([]);
    }

    public Task UpsertMarketDataStatusAsync(MarketDataStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MarketDataStatusSnapshot>> GetMarketDataStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MarketDataStatusSnapshot>>([]);
    }

    public Task AddPinnedMarketAssetAsync(PinnedMarketAsset asset, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemovePinnedMarketAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PinnedMarketAsset>> GetPinnedMarketAssetsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PinnedMarketAsset>>([]);
    }

    public Task<DailyReport> BuildDailyReportAsync(DateOnly reportDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new DailyReport(
            reportDate,
            Signals.Count,
            Signals.Count(signal => signal.Accepted),
            Signals.Count(signal => !signal.Accepted),
            PaperOrders.Count,
            PaperFills.Count,
            PaperOrders.Count(order => order.Status == PaperOrderStatus.Expired),
            PaperPositions.Sum(position => position.UnrealizedPnlUsd),
            PaperOrders.Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled).Sum(order => order.NotionalUsd)
                + PaperPositions.Sum(position => position.EstimatedValueUsd),
            string.Empty,
            ApiErrors.Count,
            DateTimeOffset.UtcNow));
    }

    public Task UpsertDailyReportAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DailyReport>> GetDailyReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<DailyReport>>([]);
    }

    public Task<IReadOnlyList<TraderPerformanceReport>> GetTraderPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<TraderPerformanceReport>>([]);
    }

    public Task<IReadOnlyList<CategoryPerformanceReport>> GetCategoryPerformanceReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CategoryPerformanceReport>>([]);
    }

    public Task<IReadOnlyList<ExecutionQualityReport>> GetExecutionQualityReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ExecutionQualityReport>>([]);
    }

    public Task<IReadOnlyList<RejectionAnalysisReport>> GetRejectionAnalysisReportsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<RejectionAnalysisReport>>([]);
    }

    public Task AddServiceCommandAuditAsync(ServiceCommandAudit audit, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceCommandAudit>> GetRecentServiceCommandAuditsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceCommandAudit>>([]);
    }

    public Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        ScannerStatuses.Add(status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ScannerStatusSnapshot>>(ScannerStatuses.ToArray());
    }

    public Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceHeartbeat>>([]);
    }
}
