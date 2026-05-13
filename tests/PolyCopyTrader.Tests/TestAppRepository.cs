using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

internal sealed class TestAppRepository : IAppRepository
{
    private readonly object sync = new();
    private readonly HashSet<string> leaderTradeKeys = [];
    private int polymarketGammaMarketLookupsInFlight;
    private int maxPolymarketGammaMarketLookupsInFlight;

    public TimeSpan PolymarketGammaMarketLookupDelay { get; set; } = TimeSpan.Zero;

    public int MaxConcurrentPolymarketGammaMarketLookups =>
        System.Threading.Volatile.Read(ref maxPolymarketGammaMarketLookupsInFlight);

    public List<LeaderTrade> LeaderTrades { get; } = [];

    public List<LeaderPosition> LeaderPositions { get; } = [];

    public List<TraderLeaderboardSnapshot> TraderLeaderboardSnapshots { get; } = [];

    public List<TraderDiscoveryCandidate> TraderDiscoveryCandidates { get; } = [];

    public List<PolymarketDataApiTrader> PolymarketDataApiTraders { get; } = [];

    public List<PolymarketDataApiPosition> PolymarketDataApiPositions { get; } = [];

    public List<PolymarketDataApiPerformanceRefreshResult> PolymarketDataApiPerformanceRefreshResults { get; } = [];

    public List<PolymarketDataApiWalletCategoryRating> PolymarketDataApiWalletCategoryRatings { get; } = [];

    public List<PolymarketCategoryMapping> PolymarketCategoryMappings { get; } =
    [
        new("Politics", "POLITICS"),
        new("Sports", "SPORTS"),
        new("Crypto", "CRYPTO"),
        new("Culture", "CULTURE"),
        new("Pop Culture", "CULTURE"),
        new("Mentions", "MENTIONS"),
        new("Weather", "WEATHER"),
        new("Economics", "ECONOMICS"),
        new("Tech", "TECH"),
        new("Finance", "FINANCE")
    ];

    public HashSet<string> PolymarketMappedLocalCategories { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Politics",
        "Sports",
        "Crypto",
        "Culture",
        "Pop Culture",
        "Mentions",
        "Weather",
        "Economics",
        "Tech",
        "Finance"
    };

    public List<PolymarketGammaMarket> PolymarketGammaMarkets { get; } = [];

    public List<StrategyMarketPaperRun> StrategyMarketPaperRuns { get; } = [];

    public Dictionary<Guid, bool> StrategyEnabledStates { get; } =
        StrategyIds.AllStrategyIds.ToDictionary(strategyId => strategyId, _ => true);

    public Dictionary<Guid, StrategyRuntimeSettings> StrategySettings { get; } =
        StrategyIds.AllStrategyIds.ToDictionary(StrategyIds.Normalize, StrategyRuntimeSettings.Default);

    public List<Signal> Signals { get; } = [];

    public List<SignalRejection> SignalRejections { get; } = [];

    public List<PaperOrder> PaperOrders { get; } = [];

    public List<PaperFill> PaperFills { get; } = [];

    public List<PaperPosition> PaperPositions { get; } = [];

    public List<PaperPositionSettlement> PaperPositionSettlements { get; } = [];

    public List<PaperCopiedTraderPerformance> PaperCopiedTraderPerformances { get; } = [];

    public List<PaperCopiedLeaderPosition> PaperCopiedLeaderPositions { get; } = [];

    public List<PaperCopiedLeaderActivityEvent> PaperCopiedLeaderActivityEvents { get; } = [];

    public List<DryRunOrder> DryRunOrders { get; } = [];

    public List<LiveOrder> LiveOrders { get; } = [];

    public List<LiveTradingEvent> LiveTradingEvents { get; } = [];

    public List<PaperLiveShadowDecision> PaperLiveShadowDecisions { get; } = [];

    public List<PaperLiveShadowDiscrepancy> PaperLiveShadowDiscrepancies { get; } = [];

    public List<BtcUpDown5mOddsTick> BtcUpDown5mOddsTicks { get; } = [];

    public List<CryptoUpDown5mOddsTick> CryptoUpDown5mOddsTicks { get; } = [];

    public List<ApiError> ApiErrors { get; } = [];

    public List<PolymarketHttpLogEntry> PolymarketHttpLogs { get; } = [];

    public List<PolymarketOnChainLog> PolymarketOnChainLogs { get; } = [];

    public List<PolymarketOnChainFill> PolymarketOnChainFills { get; } = [];

    public List<PolymarketOnChainTradeCapture> PolymarketOnChainTradeCaptures { get; } = [];

    public List<PolymarketOnChainWalletFill> PolymarketOnChainWalletFills { get; } = [];

    public List<PolymarketOnChainWalletExecution> PolymarketOnChainWalletExecutions { get; } = [];

    public List<PolymarketOnChainTokenMetadata> PolymarketOnChainTokenMetadata { get; } = [];

    public HashSet<string> PolymarketOnChainTokenMetadataRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainWalletPosition> PolymarketOnChainWalletPositions { get; } = [];

    public HashSet<string> PolymarketOnChainPositionRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainWalletPerformance> PolymarketOnChainWalletPerformance { get; } = [];

    public HashSet<string> PolymarketOnChainWalletPerformanceRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainWalletCategoryPerformance> PolymarketOnChainWalletCategoryPerformance { get; } = [];

    public HashSet<string> PolymarketOnChainWalletCategoryPerformanceRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<PolymarketOnChainSignalCandidate> PolymarketOnChainSignalCandidates { get; } = [];

    public List<PolymarketOnChainSignalCandidateReason> PolymarketOnChainSignalCandidateReasons { get; } = [];

    public List<OnChainPaperSignalResult> OnChainPaperSignalResults { get; } = [];

    public HashSet<string> PolymarketOnChainSignalCandidateRefreshQueue { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int onChainSignalCandidateBackfillCursorIndex;

    private bool onChainSignalCandidateBackfillComplete;

    public List<OnChainBlockRange> OnChainWalletDerivedRefreshRanges { get; } = [];

    public Action<OnChainBlockRange>? BeforeOnChainWalletDerivedRefresh { get; set; }

    public bool RebuildDerivedDataOnAddFills { get; set; } = true;

    public List<OnChainIngestionCursor> OnChainIngestionCursors { get; } = [];

    public List<OnChainTradeCaptureCursor> OnChainTradeCaptureCursors { get; } = [];

    public List<ScannerStatusSnapshot> ScannerStatuses { get; } = [];

    public List<PolymarketWebSocketTradeTick> PolymarketWebSocketTradeTicks { get; } = [];

    public List<OrderBookSnapshot> OrderBookSnapshots { get; } = [];

    public bool ThrowOnTryAddLeaderTrade { get; set; }

    public bool ThrowOnAddApiError { get; set; }

    public bool ThrowOnUpsertServiceHeartbeat { get; set; }

    public TaskCompletionSource<ServiceHeartbeat> ServiceHeartbeatAttempt { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Action<PolymarketGammaMarket>? BeforeUpsertPolymarketGammaMarket { get; set; }

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

    public Task<PolymarketDataApiTrader?> GetPolymarketDataApiTraderAsync(
        string wallet,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PolymarketDataApiTraders.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase)));
    }

    public Task UpsertPolymarketDataApiTraderAsync(
        PolymarketDataApiTrader trader,
        CancellationToken cancellationToken = default)
    {
        var existing = PolymarketDataApiTraders.FirstOrDefault(item =>
            string.Equals(item.Wallet, trader.Wallet, StringComparison.OrdinalIgnoreCase));
        PolymarketDataApiTraders.RemoveAll(item =>
            string.Equals(item.Wallet, trader.Wallet, StringComparison.OrdinalIgnoreCase));
        PolymarketDataApiTraders.Add(existing is null
            ? trader
            : trader with
            {
                FirstSeenAtUtc = existing.FirstSeenAtUtc,
                LastFullSyncAtUtc = existing.LastFullSyncAtUtc,
                LastIncrementalSyncAtUtc = existing.LastIncrementalSyncAtUtc,
                LastTradeTimestampUtc = existing.LastTradeTimestampUtc,
                FullSyncCompleted = existing.FullSyncCompleted,
                FullSyncTradesFetched = existing.FullSyncTradesFetched,
                FullSyncTradesInserted = existing.FullSyncTradesInserted,
                IncrementalSyncCount = existing.IncrementalSyncCount,
                PolymarketRatingRefreshedAtUtc = existing.PolymarketRatingRefreshedAtUtc,
                PolymarketRatingNextRefreshAtUtc = existing.PolymarketRatingNextRefreshAtUtc,
                PolymarketRatingRefreshAttempts = existing.PolymarketRatingRefreshAttempts,
                PolymarketRatingLastError = existing.PolymarketRatingLastError
            });
        return Task.CompletedTask;
    }

    public async Task<int> UpsertPolymarketDataApiTradersAsync(
        IReadOnlyList<PolymarketDataApiTrader> traders,
        CancellationToken cancellationToken = default)
    {
        foreach (var trader in traders)
        {
            await UpsertPolymarketDataApiTraderAsync(trader, cancellationToken);
        }

        return traders.Count;
    }

    public Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForSyncAsync(
        int limit,
        DateTimeOffset incrementalSyncBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        var traders = PolymarketDataApiTraders
            .Where(trader =>
                !trader.FullSyncCompleted ||
                trader.LastIncrementalSyncAtUtc is null ||
                trader.LastIncrementalSyncAtUtc <= incrementalSyncBeforeUtc)
            .OrderBy(trader => trader.FullSyncCompleted ? 1 : 0)
            .ThenBy(trader => trader.LastFullSyncAtUtc ?? trader.FirstSeenAtUtc)
            .ThenBy(trader => trader.LastIncrementalSyncAtUtc ?? trader.FirstSeenAtUtc)
            .ThenByDescending(trader => trader.LastSeenAtUtc)
            .Take(limit)
            .ToArray();
        return Task.FromResult<IReadOnlyList<PolymarketDataApiTrader>>(traders);
    }

    public Task<IReadOnlyList<PolymarketDataApiTrader>> GetPolymarketDataApiTradersForRatingRefreshAsync(
        int limit,
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        var traders = PolymarketDataApiTraders
            .Where(trader =>
                trader.PolymarketRatingNextRefreshAtUtc is null ||
                trader.PolymarketRatingNextRefreshAtUtc <= dueBeforeUtc)
            .OrderBy(trader => trader.PolymarketRatingNextRefreshAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(trader => trader.LastSeenAtUtc)
            .Take(limit)
            .ToArray();
        return Task.FromResult<IReadOnlyList<PolymarketDataApiTrader>>(traders);
    }

    public Task MarkPolymarketDataApiTraderSyncedAsync(
        string wallet,
        bool fullSync,
        int tradesFetched,
        int tradesInserted,
        DateTimeOffset? latestTradeTimestampUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = PolymarketDataApiTraders.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Task.CompletedTask;
        }

        PolymarketDataApiTraders.Remove(existing);
        var now = DateTimeOffset.UtcNow;
        var latest = Max(existing.LastTradeTimestampUtc, latestTradeTimestampUtc);
        PolymarketDataApiTraders.Add(fullSync
            ? existing with
            {
                LastFullSyncAtUtc = now,
                LastTradeTimestampUtc = latest,
                FullSyncCompleted = true,
                FullSyncTradesFetched = existing.FullSyncTradesFetched + tradesFetched,
                FullSyncTradesInserted = existing.FullSyncTradesInserted + tradesInserted,
                UpdatedAtUtc = now
            }
            : existing with
            {
                LastIncrementalSyncAtUtc = now,
                LastTradeTimestampUtc = latest,
                IncrementalSyncCount = existing.IncrementalSyncCount + 1,
                UpdatedAtUtc = now
            });
        return Task.CompletedTask;
    }

    public Task<PolymarketDataApiPerformanceRefreshResult> RefreshPolymarketDataApiPositionsAndPerformanceAsync(
        string wallet,
        IReadOnlyList<PolymarketDataApiPosition> currentPositions,
        IReadOnlyList<PolymarketDataApiPosition> closedPositions,
        CancellationToken cancellationToken = default)
    {
        PolymarketDataApiPositions.RemoveAll(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase) &&
            item.Status == PolymarketDataApiPositionStatus.Open);

        foreach (var position in currentPositions.Concat(closedPositions))
        {
            var normalized = position with { Wallet = wallet };
            PolymarketDataApiPositions.RemoveAll(item =>
                string.Equals(item.Wallet, normalized.Wallet, StringComparison.OrdinalIgnoreCase) &&
                item.Status == normalized.Status &&
                string.Equals(item.AssetId, normalized.AssetId, StringComparison.OrdinalIgnoreCase));
            PolymarketDataApiPositions.Add(normalized);
        }

        var result = new PolymarketDataApiPerformanceRefreshResult(
            currentPositions.Count,
            closedPositions.Count,
            currentPositions.Count + closedPositions.Count,
            currentPositions.Count + closedPositions.Count > 0 ? 1 : 0,
            currentPositions.Count + closedPositions.Count > 0 ? 1 : 0);
        PolymarketDataApiPerformanceRefreshResults.Add(result);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> GetMissingPolymarketLeaderboardCategoryMappingsAsync(
        string wallet,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var categories = PolymarketDataApiPositions
            .Where(item => string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Category?.Trim())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Where(category => !string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(category => !PolymarketMappedLocalCategories.Contains(category!))
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(category => category!)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(categories);
    }

    public Task<IReadOnlyList<PolymarketCategoryMapping>> GetEnabledPolymarketCategoryMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketCategoryMapping>>(PolymarketCategoryMappings.ToArray());
    }

    public Task<int> UpsertPolymarketDataApiWalletCategoryRatingsAsync(
        IReadOnlyList<PolymarketDataApiWalletCategoryRating> ratings,
        CancellationToken cancellationToken = default)
    {
        foreach (var rating in ratings)
        {
            PolymarketDataApiWalletCategoryRatings.RemoveAll(item =>
                string.Equals(item.Wallet, rating.Wallet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.LocalCategory, rating.LocalCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.PolymarketCategory, rating.PolymarketCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.TimePeriod, rating.TimePeriod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.OrderBy, rating.OrderBy, StringComparison.OrdinalIgnoreCase));
            PolymarketDataApiWalletCategoryRatings.Add(rating);
        }

        return Task.FromResult(ratings.Count);
    }

    public Task MarkPolymarketDataApiTraderRatingRefreshedAsync(
        string wallet,
        DateTimeOffset refreshedAtUtc,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = PolymarketDataApiTraders.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Task.CompletedTask;
        }

        PolymarketDataApiTraders.Remove(existing);
        PolymarketDataApiTraders.Add(existing with
        {
            PolymarketRatingRefreshedAtUtc = refreshedAtUtc,
            PolymarketRatingNextRefreshAtUtc = nextRefreshAtUtc,
            PolymarketRatingRefreshAttempts = 0,
            PolymarketRatingLastError = null,
            UpdatedAtUtc = refreshedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task MarkPolymarketDataApiTraderRatingRefreshFailedAsync(
        string wallet,
        string errorMessage,
        DateTimeOffset nextRefreshAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = PolymarketDataApiTraders.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Task.CompletedTask;
        }

        PolymarketDataApiTraders.Remove(existing);
        PolymarketDataApiTraders.Add(existing with
        {
            PolymarketRatingNextRefreshAtUtc = nextRefreshAtUtc,
            PolymarketRatingRefreshAttempts = existing.PolymarketRatingRefreshAttempts + 1,
            PolymarketRatingLastError = errorMessage,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        return Task.CompletedTask;
    }

    public Task UpsertPolymarketGammaMarketAsync(
        PolymarketGammaMarket market,
        CancellationToken cancellationToken = default)
    {
        BeforeUpsertPolymarketGammaMarket?.Invoke(market);
        PolymarketGammaMarkets.RemoveAll(item =>
            string.Equals(item.MarketId, market.MarketId, StringComparison.OrdinalIgnoreCase));
        PolymarketGammaMarkets.Add(market);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDown5mGammaMarketsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>(PolymarketGammaMarkets
            .Where(market =>
                market.Active &&
                !market.Archived &&
                (market.Slug.StartsWith("btc-updown-5m-", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(market.SeriesSlug, "btc-up-or-down-5m", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(market => market.EventStartTimeUtc ?? market.EndDateUtc ?? market.CreatedAtUtc)
            .Take(limit)
            .ToArray());
    }

    public Task<IReadOnlyList<PolymarketGammaMarket>> GetBtcUpDownStrategyGammaMarketsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>(PolymarketGammaMarkets
            .Where(market =>
                market.Active &&
                !market.Archived &&
                BtcUpDown5mMarketAnalyzer.IsStrategyCandidate(market))
            .OrderBy(market => market.EventStartTimeUtc ?? market.EndDateUtc ?? market.CreatedAtUtc)
            .Take(limit)
            .ToArray());
    }

    public async Task<PolymarketGammaMarket?> GetPolymarketGammaMarketAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        var current = System.Threading.Interlocked.Increment(ref polymarketGammaMarketLookupsInFlight);
        try
        {
            var recorded = System.Threading.Volatile.Read(ref maxPolymarketGammaMarketLookupsInFlight);
            while (current > recorded)
            {
                var previous = System.Threading.Interlocked.CompareExchange(
                    ref maxPolymarketGammaMarketLookupsInFlight,
                    current,
                    recorded);
                if (previous == recorded)
                {
                    break;
                }

                recorded = previous;
            }

            if (PolymarketGammaMarketLookupDelay > TimeSpan.Zero)
            {
                await Task.Delay(PolymarketGammaMarketLookupDelay, cancellationToken);
            }

            lock (sync)
            {
                return PolymarketGammaMarkets.FirstOrDefault(market =>
                    string.Equals(market.MarketId, marketId, StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            System.Threading.Interlocked.Decrement(ref polymarketGammaMarketLookupsInFlight);
        }
    }

    public Task<bool> TryAddStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (StrategyMarketPaperRuns.Any(item =>
                item.StrategyId == run.StrategyId &&
                string.Equals(item.MarketId, run.MarketId, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            StrategyMarketPaperRuns.Add(run);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(
        Guid strategyId,
        string status,
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>(StrategyMarketPaperRuns
                .Where(run => run.StrategyId == strategyId)
                .Where(run => string.Equals(run.Status, status, StringComparison.OrdinalIgnoreCase))
                .Where(run => run.EntryDueAtUtc <= dueBeforeUtc)
                .OrderBy(run => run.EntryDueAtUtc)
                .ThenBy(run => run.DetectedAtUtc)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyList<StrategyMarketPaperRun>> GetDueStrategyMarketPaperRunsAsync(
        IReadOnlyCollection<Guid> strategyIds,
        string status,
        DateTimeOffset dueBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyIds = strategyIds
            .Select(StrategyIds.Normalize)
            .ToHashSet();
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>(StrategyMarketPaperRuns
                .Where(run => normalizedStrategyIds.Contains(StrategyIds.Normalize(run.StrategyId)))
                .Where(run => string.Equals(run.Status, status, StringComparison.OrdinalIgnoreCase))
                .Where(run => run.EntryDueAtUtc <= dueBeforeUtc)
                .OrderBy(run => run.EntryDueAtUtc)
                .ThenBy(run => run.DetectedAtUtc)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(
        Guid strategyId,
        DateTimeOffset marketEndedBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>(StrategyMarketPaperRuns
                .Where(run => run.StrategyId == strategyId)
                .Where(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Entered, StringComparison.OrdinalIgnoreCase))
                .Where(run => run.MarketEndUtc is not null && run.MarketEndUtc <= marketEndedBeforeUtc)
                .OrderBy(run => run.MarketEndUtc)
                .ThenBy(run => run.EnteredAtUtc)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyList<StrategyMarketPaperRun>> GetStrategyMarketPaperRunsForSettlementAsync(
        IReadOnlyCollection<Guid> strategyIds,
        DateTimeOffset marketEndedBeforeUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyIds = strategyIds
            .Select(StrategyIds.Normalize)
            .ToHashSet();
        lock (sync)
        {
            var filledPaperOrderIds = PaperFills
                .Select(fill => fill.PaperOrderId)
                .ToHashSet();
            var paperOrdersById = PaperOrders.ToDictionary(order => order.Id);
            return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>(StrategyMarketPaperRuns
                .Where(run => normalizedStrategyIds.Contains(StrategyIds.Normalize(run.StrategyId)))
                .Where(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Entered, StringComparison.OrdinalIgnoreCase))
                .Where(run => run.MarketEndUtc is not null && run.MarketEndUtc <= marketEndedBeforeUtc)
                .OrderBy(run => GetSettlementPriority(run, paperOrdersById, filledPaperOrderIds))
                .ThenBy(run => run.MarketEndUtc)
                .ThenBy(run => run.EnteredAtUtc)
                .ThenBy(run => run.DetectedAtUtc)
                .Take(limit)
                .ToArray());
        }
    }

    public Task<IReadOnlyList<StrategyMarketPaperRun>> GetRecentStrategyMarketPaperRunsAsync(
        Guid strategyId,
        string status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StrategyMarketPaperRun>>(StrategyMarketPaperRuns
            .Where(run => run.StrategyId == strategyId)
            .Where(run => string.Equals(run.Status, status, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.SettledAtUtc ?? run.EnteredAtUtc ?? run.UpdatedAtUtc)
            .ThenByDescending(run => run.MarketStartUtc ?? run.EntryDueAtUtc)
            .Take(limit)
            .ToArray());
    }

    private static int GetSettlementPriority(
        StrategyMarketPaperRun run,
        IReadOnlyDictionary<Guid, PaperOrder> paperOrdersById,
        IReadOnlySet<Guid> filledPaperOrderIds)
    {
        if (run.PaperOrderId is { } paperOrderId && filledPaperOrderIds.Contains(paperOrderId))
        {
            return 0;
        }

        if (run.PaperOrderId is { } orderId &&
            paperOrdersById.TryGetValue(orderId, out var order))
        {
            return order.Status switch
            {
                PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.PartiallyFilledExpired => 1,
                PaperOrderStatus.Expired => 2,
                _ => 4
            };
        }

        return 3;
    }

    public Task<IReadOnlyList<BtcUpDown5mMarketResult>> GetRecentBtcUpDown5mMarketResultsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<BtcUpDown5mMarketResult>>([]);
        }

        var results = StrategyMarketPaperRuns
            .Where(run =>
                string.Equals(run.Status, StrategyMarketPaperRunStatuses.Settled, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(run.SelectedOutcome) &&
                run.RealizedPnlUsd is not null)
            .GroupBy(run => string.IsNullOrWhiteSpace(run.ConditionId) ? run.MarketId : run.ConditionId, StringComparer.OrdinalIgnoreCase)
            .Select(TryCreateBtcUpDown5mMarketResult)
            .Where(result => result is not null)
            .Select(result => result!)
            .OrderByDescending(result => result.MarketStartUtc ?? result.MarketEndUtc ?? result.SettledAtUtc)
            .ThenByDescending(result => result.SettledAtUtc)
            .Take(limit)
            .ToArray();
        return Task.FromResult<IReadOnlyList<BtcUpDown5mMarketResult>>(results);
    }

    public Task UpdateStrategyMarketPaperRunAsync(
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            StrategyMarketPaperRuns.RemoveAll(item => item.Id == run.Id);
            StrategyMarketPaperRuns.Add(run);
        }
        return Task.CompletedTask;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left > right ? left : right;
    }

    public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            Signals.Add(signal);
        }
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
        lock (sync)
        {
            PaperOrders.Add(order);
        }
        return Task.CompletedTask;
    }

    public Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            PaperOrders.RemoveAll(item => item.Id == order.Id);
            PaperOrders.Add(order);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>(PaperOrders
            .Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled)
            .ToArray());
    }

    public Task<PaperOrder?> GetPaperOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult(PaperOrders.FirstOrDefault(order => order.Id == paperOrderId));
        }
    }

    public Task<PaperOrder?> GetPaperOrderByCorrelationIdAsync(Guid correlationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PaperOrders.FirstOrDefault(order => order.CorrelationId == correlationId));
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

    public Task<IReadOnlyList<PaperFill>> GetPaperFillsForOrderAsync(Guid paperOrderId, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            return Task.FromResult<IReadOnlyList<PaperFill>>(PaperFills
                .Where(item => item.PaperOrderId == paperOrderId)
                .OrderBy(item => item.FilledAtUtc)
                .ThenBy(item => item.Id)
                .ToArray());
        }
    }

    public Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            PaperPositions.RemoveAll(item =>
                string.Equals(item.AssetId, position.AssetId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.CopiedTraderWallet, position.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase));
            PaperPositions.Add(position);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPosition>>(PaperPositions.ToArray());
    }

    public Task<bool> TryAddPaperPositionSettlementAsync(PaperPositionSettlement settlement, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (PaperPositionSettlements.Any(item =>
                string.Equals(item.AssetId, settlement.AssetId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.CopiedTraderWallet, settlement.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }

            PaperPositionSettlements.Add(settlement);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<PaperPositionSettlement>> GetRecentPaperPositionSettlementsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperPositionSettlement>>(PaperPositionSettlements
            .OrderByDescending(item => item.SettledAtUtc)
            .Take(limit)
            .ToArray());
    }

    public Task<int> RefreshPaperCopiedTraderPerformanceAsync(CancellationToken cancellationToken = default)
    {
        PaperCopiedTraderPerformances.Clear();
        var rows = BuildPaperCopiedTraderPerformance();
        PaperCopiedTraderPerformances.AddRange(rows);
        return Task.FromResult(rows.Count);
    }

    public Task<IReadOnlyList<PaperCopiedTraderPerformance>> GetPaperCopiedTraderPerformanceAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperCopiedTraderPerformance>>(PaperCopiedTraderPerformances
            .OrderByDescending(item => string.Equals(item.Category, "OVERALL", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Score)
            .ThenByDescending(item => item.TotalPnlUsd)
            .Take(limit)
            .ToArray());
    }

    public Task<PaperCopiedTraderPerformance?> GetPaperCopiedTraderPerformanceAsync(
        string copiedTraderWallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        var performance = PaperCopiedTraderPerformances.FirstOrDefault(item =>
            string.Equals(item.CopiedTraderWallet, copiedTraderWallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(performance);
    }

    public Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(int limit = 1000, CancellationToken cancellationToken = default)
    {
        var strategies = new[]
        {
            new
            {
                Id = StrategyIds.FollowLeader,
                Code = StrategyIds.FollowLeaderCode,
                Name = StrategyIds.FollowLeaderName,
                Settings = GetStrategySettings(StrategyIds.FollowLeader)
            }
        }
        .Concat(StrategyIds.BtcUpDown5mVariants.Select(variant => new
        {
            Id = variant.Id,
            variant.Code,
            variant.Name,
            Settings = GetStrategySettings(variant.Id)
        }));

        var rows = new List<StrategyPerformance>();
        foreach (var strategy in strategies)
        {
            var orders = PaperOrders
                .Where(order => StrategyIds.Normalize(order.StrategyId) == strategy.Id)
                .ToArray();
            var fills = PaperFills
                .Join(orders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => new { Fill = fill, Order = order })
                .ToArray();
            var positions = PaperPositions
                .Where(position => StrategyIdForSyntheticWallet(position.CopiedTraderWallet) == strategy.Id)
                .Where(position => position.SizeShares > 0m)
                .ToArray();
            var settlements = PaperPositionSettlements
                .Where(settlement => StrategyIdForSyntheticWallet(settlement.CopiedTraderWallet) == strategy.Id)
                .ToArray();
            var runs = StrategyMarketPaperRuns
                .Where(run => run.StrategyId == strategy.Id)
                .ToArray();
            var useRunsForSettled = runs.Length > 0;
            var settledRuns = runs
                .Where(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Settled, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var settledCount = useRunsForSettled ? settledRuns.Length : settlements.Length;
            var wonCount = useRunsForSettled
                ? settledRuns.Count(run => (run.RealizedPnlUsd ?? 0m) > 0m)
                : settlements.Count(settlement => settlement.Won);
            var lostCount = useRunsForSettled
                ? settledRuns.Count(run => (run.RealizedPnlUsd ?? 0m) < 0m)
                : settlements.Count(settlement => !settlement.Won);
            var stake = orders
                .Where(order => order.Side == TradeSide.Buy)
                .Where(order => order.Status is PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.PartiallyFilledExpired)
                .Sum(order => order.NotionalUsd);
            if (stake <= 0m)
            {
                stake = useRunsForSettled
                    ? settledRuns.Sum(run => run.StakeUsd)
                    : settlements.Sum(settlement => settlement.CostBasisUsd);
            }

            var realized = (useRunsForSettled
                    ? settledRuns.Sum(run => run.RealizedPnlUsd ?? 0m)
                    : settlements.Sum(settlement => settlement.RealizedPnlUsd)) +
                fills.Sum(item => item.Fill.RealizedPnlUsd);
            var unrealized = positions.Sum(position => position.UnrealizedPnlUsd);
            var total = realized + unrealized;
            var closedStake = (useRunsForSettled
                    ? settledRuns.Sum(run => run.StakeUsd)
                    : settlements.Sum(settlement => settlement.CostBasisUsd)) +
                fills
                    .Where(item => item.Order.Side == TradeSide.Sell)
                    .Sum(item => item.Fill.Price * item.Fill.SizeShares - item.Fill.RealizedPnlUsd);
            var closedPnlRows = useRunsForSettled
                ? settledRuns.Select(run => run.RealizedPnlUsd ?? 0m).ToArray()
                : settlements.Select(settlement => settlement.RealizedPnlUsd).ToArray();
            var winPnlRows = useRunsForSettled
                ? closedPnlRows.Where(value => value > 0m).ToArray()
                : settlements.Where(settlement => settlement.Won).Select(settlement => settlement.RealizedPnlUsd).ToArray();
            var lossPnlRows = useRunsForSettled
                ? closedPnlRows.Where(value => value < 0m).ToArray()
                : settlements.Where(settlement => !settlement.Won).Select(settlement => settlement.RealizedPnlUsd).ToArray();
            var positivePnl = closedPnlRows.Where(value => value > 0m).Sum();
            var lossAbsPnl = -closedPnlRows.Where(value => value < 0m).Sum();
            decimal? profitFactor = lossAbsPnl == 0m ? null : positivePnl / lossAbsPnl;
            var entryDelaySeconds = runs
                .Where(run => run.EnteredAtUtc.HasValue)
                .Select(run => Math.Max(0m, (decimal)(run.EnteredAtUtc!.Value - run.EntryDueAtUtc).TotalSeconds))
                .ToArray();
            var liveOrders = LiveOrders
                .Where(order => StrategyIds.Normalize(order.StrategyId) == strategy.Id)
                .ToArray();
            var liveSettled = liveOrders
                .Where(order => order.SettledAtUtc is not null && order.RealizedPnlUsd is not null)
                .ToArray();
            var liveWon = liveSettled.Count(order => order.Won ?? order.SettlementValueUsd > 0m);
            var liveLost = liveSettled.Length - liveWon;
            var liveStake = liveSettled.Sum(order =>
                order.CostBasisUsd > 0m
                    ? order.CostBasisUsd
                    : order.FilledNotionalUsd > 0m
                        ? order.FilledNotionalUsd + order.FeeUsd
                        : order.FilledSize > 0m
                            ? order.Price * order.FilledSize + order.FeeUsd
                            : 0m);
            var liveRealized = liveSettled.Sum(order => order.RealizedPnlUsd ?? 0m);
            var liveWinRows = liveSettled
                .Where(order => order.Won ?? order.SettlementValueUsd > 0m)
                .Select(order => order.RealizedPnlUsd ?? 0m)
                .ToArray();
            var liveLossRows = liveSettled
                .Where(order => !(order.Won ?? order.SettlementValueUsd > 0m))
                .Select(order => order.RealizedPnlUsd ?? 0m)
                .ToArray();
            var livePositivePnl = liveSettled.Select(order => order.RealizedPnlUsd ?? 0m).Where(value => value > 0m).Sum();
            var liveLossAbsPnl = -liveSettled.Select(order => order.RealizedPnlUsd ?? 0m).Where(value => value < 0m).Sum();
            decimal? liveProfitFactor = liveLossAbsPnl == 0m ? null : livePositivePnl / liveLossAbsPnl;

            rows.Add(new StrategyPerformance(
                strategy.Id,
                strategy.Code,
                strategy.Name,
                strategy.Settings.Enabled,
                strategy.Settings.LiveStakes,
                strategy.Settings.PaperStakeAmount,
                strategy.Settings.LiveStakeAmount,
                strategy.Settings.LiveAvailableBalance,
                orders.Length,
                orders.Count(order => order.Status is PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.PartiallyFilledExpired),
                orders.Count(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled),
                positions.Length,
                runs.Count(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Observed, StringComparison.OrdinalIgnoreCase)),
                runs.Count(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Entered, StringComparison.OrdinalIgnoreCase)),
                runs.Count(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Skipped, StringComparison.OrdinalIgnoreCase)),
                settledRuns.Length,
                settledCount,
                wonCount,
                lostCount,
                stake,
                realized,
                unrealized,
                total,
                settledCount == 0 ? 0m : wonCount * 100m / settledCount,
                settledCount == 0 ? 0m : lostCount * 100m / settledCount,
                winPnlRows.Length == 0 ? 0m : winPnlRows.Average(),
                lossPnlRows.Length == 0 ? 0m : lossPnlRows.Average(),
                profitFactor,
                closedPnlRows.Length == 0 ? 0m : closedPnlRows.Average(),
                stake <= 0m ? 0m : total * 100m / stake,
                closedStake <= 0m ? 0m : realized * 100m / closedStake,
                entryDelaySeconds.Length == 0 ? 0m : entryDelaySeconds.Average(),
                entryDelaySeconds.Length == 0 ? 0m : entryDelaySeconds.Max(),
                liveOrders.Length,
                liveOrders.Count(order => order.FilledSize > 0m),
                liveOrders.Count(order =>
                    (order.Status == LiveOrderStatus.Submitted ||
                     order.Status == LiveOrderStatus.Live ||
                     order.Status == LiveOrderStatus.Delayed ||
                     order.Status == LiveOrderStatus.Unmatched ||
                     order.Status == LiveOrderStatus.CancelRequested) &&
                    order.RemainingSize > 0m),
                liveSettled.Length,
                liveWon,
                liveLost,
                liveStake,
                liveRealized,
                liveSettled.Length == 0 ? 0m : liveWon * 100m / liveSettled.Length,
                liveSettled.Length == 0 ? 0m : liveLost * 100m / liveSettled.Length,
                liveWinRows.Length == 0 ? 0m : liveWinRows.Average(),
                liveLossRows.Length == 0 ? 0m : liveLossRows.Average(),
                liveProfitFactor,
                liveSettled.Length == 0 ? 0m : liveSettled.Average(order => order.RealizedPnlUsd ?? 0m),
                liveStake <= 0m ? 0m : liveRealized * 100m / liveStake,
                liveOrders.Select(order => (DateTimeOffset?)order.CreatedAtUtc).DefaultIfEmpty(null).Max(),
                liveSettled.Select(order => order.SettledAtUtc).DefaultIfEmpty(null).Max(),
                orders.Select(order => (DateTimeOffset?)order.CreatedAtUtc).DefaultIfEmpty(null).Max(),
                runs.Select(run => (DateTimeOffset?)run.UpdatedAtUtc).DefaultIfEmpty(null).Max()));
        }

        return Task.FromResult<IReadOnlyList<StrategyPerformance>>(rows
            .OrderBy(row => row.Code == StrategyIds.FollowLeaderCode ? 0 : 1)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray());
    }

    public Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(int limit = 3000, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windows = new[]
        {
            new { Label = "1h", Hours = 1, StartUtc = now.AddHours(-1) },
            new { Label = "6h", Hours = 6, StartUtc = now.AddHours(-6) },
            new { Label = "24h", Hours = 24, StartUtc = now.AddHours(-24) }
        };
        var strategies = new[]
        {
            new
            {
                Id = StrategyIds.FollowLeader,
                Code = StrategyIds.FollowLeaderCode,
                Name = StrategyIds.FollowLeaderName
            }
        }
        .Concat(StrategyIds.BtcUpDown5mVariants.Select(variant => new
        {
            Id = variant.Id,
            variant.Code,
            variant.Name
        }))
        .OrderBy(strategy => strategy.Code == StrategyIds.FollowLeaderCode ? 0 : 1)
        .ThenBy(strategy => strategy.Code, StringComparer.OrdinalIgnoreCase)
        .Take(limit)
        .ToArray();

        var rows = new List<StrategyRecentPerformance>();
        foreach (var strategy in strategies)
        {
            foreach (var window in windows)
            {
                var orders = PaperOrders
                    .Where(order => StrategyIds.Normalize(order.StrategyId) == strategy.Id)
                    .Where(order => order.CreatedAtUtc >= window.StartUtc && order.CreatedAtUtc <= now)
                    .ToArray();
                var allStrategyOrders = PaperOrders
                    .Where(order => StrategyIds.Normalize(order.StrategyId) == strategy.Id)
                    .ToArray();
                var fills = PaperFills
                    .Join(allStrategyOrders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => fill)
                    .Where(fill => fill.FilledAtUtc >= window.StartUtc && fill.FilledAtUtc <= now)
                    .ToArray();
                var runs = StrategyMarketPaperRuns
                    .Where(run => StrategyIds.Normalize(run.StrategyId) == strategy.Id)
                    .ToArray();
                var enteredRuns = runs
                    .Where(run => run.EnteredAtUtc.HasValue)
                    .Where(run => run.EnteredAtUtc!.Value >= window.StartUtc && run.EnteredAtUtc.Value <= now)
                    .ToArray();
                var skippedRuns = runs
                    .Where(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Skipped, StringComparison.OrdinalIgnoreCase))
                    .Where(run => run.UpdatedAtUtc >= window.StartUtc && run.UpdatedAtUtc <= now)
                    .ToArray();
                var settledRuns = runs
                    .Where(run => string.Equals(run.Status, StrategyMarketPaperRunStatuses.Settled, StringComparison.OrdinalIgnoreCase))
                    .Where(run => run.SettledAtUtc.HasValue)
                    .Where(run => run.SettledAtUtc!.Value >= window.StartUtc && run.SettledAtUtc.Value <= now)
                    .ToArray();
                var runsUpdatedInWindow = runs
                    .Where(run => run.UpdatedAtUtc >= window.StartUtc && run.UpdatedAtUtc <= now)
                    .ToArray();
                var filledCost = fills.Sum(fill => fill.Price * fill.SizeShares);
                var filledShares = fills.Sum(fill => fill.SizeShares);
                var realizedPnl = settledRuns.Sum(run => run.RealizedPnlUsd ?? 0m);
                var settledStake = settledRuns.Sum(run => run.StakeUsd);
                var wonRuns = settledRuns.Count(run => (run.RealizedPnlUsd ?? 0m) > 0m);
                var lostRuns = settledRuns.Count(run => (run.RealizedPnlUsd ?? 0m) < 0m);
                var entryDelaySeconds = enteredRuns
                    .Select(run => Math.Max(0m, (decimal)(run.EnteredAtUtc!.Value - run.EntryDueAtUtc).TotalSeconds))
                    .ToArray();
                var topSkip = skippedRuns
                    .Where(run => !string.IsNullOrWhiteSpace(run.SkipReason))
                    .GroupBy(run => run.SkipReason!, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                rows.Add(new StrategyRecentPerformance(
                    strategy.Id,
                    strategy.Code,
                    strategy.Name,
                    window.Label,
                    window.Hours,
                    window.StartUtc,
                    now,
                    orders.Length,
                    orders.Count(order => order.Status is PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.PartiallyFilledExpired),
                    orders.Count(order => order.Status is PaperOrderStatus.Expired or PaperOrderStatus.PartiallyFilledExpired),
                    orders.Count(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled),
                    enteredRuns.Length,
                    skippedRuns.Length,
                    settledRuns.Length,
                    wonRuns,
                    lostRuns,
                    filledCost,
                    realizedPnl,
                    filledShares <= 0m ? 0m : filledCost / filledShares,
                    entryDelaySeconds.Length == 0 ? 0m : entryDelaySeconds.Average(),
                    entryDelaySeconds.Length == 0 ? 0m : entryDelaySeconds.Max(),
                    settledRuns.Length == 0 ? 0m : wonRuns * 100m / settledRuns.Length,
                    settledStake > 0m
                        ? realizedPnl * 100m / settledStake
                        : filledCost > 0m
                            ? realizedPnl * 100m / filledCost
                            : 0m,
                    topSkip is null ? string.Empty : $"{topSkip.Key}:{topSkip.Count()}",
                    orders.Select(order => (DateTimeOffset?)order.CreatedAtUtc).DefaultIfEmpty(null).Max(),
                    runsUpdatedInWindow.Select(run => (DateTimeOffset?)run.UpdatedAtUtc).DefaultIfEmpty(null).Max()));
            }
        }

        return Task.FromResult<IReadOnlyList<StrategyRecentPerformance>>(rows);
    }

    public Task<IReadOnlyDictionary<Guid, bool>> GetStrategyEnabledStatesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(new Dictionary<Guid, bool>(StrategyEnabledStates));
    }

    public Task<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>> GetStrategyRuntimeSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, StrategyRuntimeSettings>>(
            StrategySettings.ToDictionary(
                item => StrategyIds.Normalize(item.Key),
                item => GetStrategySettings(item.Key)));
    }

    public Task<bool> SetStrategyEnabledAsync(
        Guid strategyId,
        bool enabled,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        if (!StrategyEnabledStates.ContainsKey(normalizedStrategyId))
        {
            return Task.FromResult(false);
        }

        StrategyEnabledStates[normalizedStrategyId] = enabled;
        StrategySettings[normalizedStrategyId] = GetStrategySettings(normalizedStrategyId) with { Enabled = enabled };
        return Task.FromResult(true);
    }

    public Task<bool> SetStrategyLiveStakesAsync(
        Guid strategyId,
        bool liveStakes,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        if (!StrategySettings.ContainsKey(normalizedStrategyId))
        {
            return Task.FromResult(false);
        }

        StrategySettings[normalizedStrategyId] = GetStrategySettings(normalizedStrategyId) with
        {
            LiveStakes = liveStakes
        };
        return Task.FromResult(true);
    }

    public Task<bool> SetStrategyStakeAmountsAsync(
        Guid strategyId,
        decimal paperStakeAmount,
        decimal liveStakeAmount,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        if (!StrategySettings.ContainsKey(normalizedStrategyId) ||
            paperStakeAmount <= 0m ||
            liveStakeAmount <= 0m)
        {
            return Task.FromResult(false);
        }

        StrategySettings[normalizedStrategyId] = GetStrategySettings(normalizedStrategyId) with
        {
            PaperStakeAmount = paperStakeAmount,
            LiveStakeAmount = liveStakeAmount
        };
        return Task.FromResult(true);
    }

    public Task<bool> SetStrategyLiveAvailableBalanceAsync(
        Guid strategyId,
        decimal liveAvailableBalance,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        if (!StrategySettings.ContainsKey(normalizedStrategyId) ||
            liveAvailableBalance < 0m)
        {
            return Task.FromResult(false);
        }

        StrategySettings[normalizedStrategyId] = GetStrategySettings(normalizedStrategyId) with
        {
            LiveAvailableBalance = liveAvailableBalance
        };
        return Task.FromResult(true);
    }

    public Task<bool> TryAddPaperCopiedLeaderPositionAsync(
        PaperCopiedLeaderPosition position,
        CancellationToken cancellationToken = default)
    {
        if (PaperCopiedLeaderPositions.Any(item => item.EntryPaperOrderId == position.EntryPaperOrderId))
        {
            return Task.FromResult(false);
        }

        PaperCopiedLeaderPositions.Add(position);
        return Task.FromResult(true);
    }

    public Task ActivatePaperCopiedLeaderPositionAsync(
        Guid entryPaperOrderId,
        decimal copiedInitialSizeShares,
        DateTimeOffset filledAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = PaperCopiedLeaderPositions.FirstOrDefault(item => item.EntryPaperOrderId == entryPaperOrderId);
        if (existing is null ||
            existing.Status is not (PaperCopiedLeaderPositionStatus.PendingEntry or PaperCopiedLeaderPositionStatus.Active))
        {
            return Task.CompletedTask;
        }

        PaperCopiedLeaderPositions.Remove(existing);
        PaperCopiedLeaderPositions.Add(existing with
        {
            Status = PaperCopiedLeaderPositionStatus.Active,
            CopiedInitialSizeShares = existing.Status == PaperCopiedLeaderPositionStatus.Active
                ? existing.CopiedInitialSizeShares + copiedInitialSizeShares
                : copiedInitialSizeShares,
            NextActivitySyncAtUtc = existing.NextActivitySyncAtUtc < filledAtUtc ? existing.NextActivitySyncAtUtc : filledAtUtc,
            UpdatedAtUtc = filledAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperCopiedLeaderPosition>> GetPaperCopiedLeaderPositionsForExitTrackingAsync(
        int limit,
        DateTimeOffset dueBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperCopiedLeaderPosition>>(PaperCopiedLeaderPositions
            .Where(item => item.Status == PaperCopiedLeaderPositionStatus.Active)
            .Where(item => item.NextActivitySyncAtUtc <= dueBeforeUtc)
            .Where(item => item.LeaderInitialSizeShares > item.LeaderSoldSizeShares)
            .Where(item => item.CopiedInitialSizeShares > item.CopiedExitRequestedSizeShares)
            .OrderBy(item => item.NextActivitySyncAtUtc)
            .ThenBy(item => item.UpdatedAtUtc)
            .Take(limit)
            .ToArray());
    }

    public Task MarkPaperCopiedLeaderPositionsActivitySyncedAsync(
        string copiedTraderWallet,
        DateTimeOffset syncedAtUtc,
        DateTimeOffset nextSyncAtUtc,
        CancellationToken cancellationToken = default)
    {
        var matches = PaperCopiedLeaderPositions
            .Where(item => item.Status == PaperCopiedLeaderPositionStatus.Active)
            .Where(item => string.Equals(item.CopiedTraderWallet, copiedTraderWallet, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var existing in matches)
        {
            PaperCopiedLeaderPositions.Remove(existing);
            PaperCopiedLeaderPositions.Add(existing with
            {
                LastActivitySyncAtUtc = syncedAtUtc,
                NextActivitySyncAtUtc = nextSyncAtUtc,
                UpdatedAtUtc = syncedAtUtc
            });
        }

        return Task.CompletedTask;
    }

    public Task<bool> ApplyPaperCopiedLeaderExitAsync(
        PaperCopiedLeaderActivityEvent activityEvent,
        IReadOnlyList<PaperCopiedLeaderPositionExitUpdate> positionUpdates,
        IReadOnlyList<Signal> signals,
        IReadOnlyList<PaperOrder> paperOrders,
        CancellationToken cancellationToken = default)
    {
        if (PaperCopiedLeaderActivityEvents.Any(item => string.Equals(item.DedupKey, activityEvent.DedupKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(false);
        }

        PaperCopiedLeaderActivityEvents.Add(activityEvent);
        foreach (var update in positionUpdates)
        {
            var existing = PaperCopiedLeaderPositions.FirstOrDefault(item => item.Id == update.PositionId);
            if (existing is null)
            {
                continue;
            }

            PaperCopiedLeaderPositions.Remove(existing);
            PaperCopiedLeaderPositions.Add(existing with
            {
                LeaderSoldSizeShares = update.LeaderSoldSizeShares,
                CopiedExitRequestedSizeShares = update.CopiedExitRequestedSizeShares,
                Status = update.Status,
                LastActivityTimestampUtc = update.LastActivityTimestampUtc,
                LastActivityTransactionHash = update.LastActivityTransactionHash,
                UpdatedAtUtc = update.UpdatedAtUtc
            });
        }

        Signals.AddRange(signals);
        PaperOrders.AddRange(paperOrders);
        return Task.FromResult(true);
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
            .Where(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Unmatched or LiveOrderStatus.CancelRequested)
            .ToArray());
    }

    public Task<IReadOnlyList<LiveOrder>> GetOpenLiveOrdersForStrategyOrCorrelationAsync(
        Guid strategyId,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders
            .Where(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Unmatched or LiveOrderStatus.CancelRequested)
            .Where(order => StrategyIds.Normalize(order.StrategyId) == normalizedStrategyId || (correlationId is not null && order.CorrelationId == correlationId))
            .ToArray());
    }

    public Task<IReadOnlyList<LiveOrder>> GetMatchedLiveOrdersPendingBalanceSettlementAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LiveOrder>>(LiveOrders
            .Where(order => order.Status == LiveOrderStatus.Matched)
            .Where(order => !order.BalanceEffectApplied)
            .Where(order => order.FilledSize > 0m)
            .OrderBy(order => order.UpdatedAtUtc)
            .Take(limit)
            .ToArray());
    }

    public Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceAsync(
        Guid liveOrderId,
        Guid strategyId,
        decimal settlementValueUsd,
        decimal realizedPnlUsd,
        string? winningAssetId,
        string winningOutcome,
        DateTimeOffset settledAtUtc,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        var existing = LiveOrders.FirstOrDefault(order =>
            order.Id == liveOrderId &&
            StrategyIds.Normalize(order.StrategyId) == normalizedStrategyId);
        if (existing is null || existing.BalanceEffectApplied)
        {
            var currentBalance = GetStrategySettings(normalizedStrategyId).LiveAvailableBalance;
            return Task.FromResult(new StrategyLiveBalanceAdjustmentResult(false, currentBalance, false));
        }

        LiveOrders.Remove(existing);
        LiveOrders.Add(existing with
        {
            BalanceEffectApplied = true,
            SettlementValueUsd = settlementValueUsd,
            RealizedPnlUsd = realizedPnlUsd,
            SettledAtUtc = settledAtUtc,
            WinningAssetId = winningAssetId,
            WinningOutcome = winningOutcome,
            Won = settlementValueUsd > 0m,
            SettlementSource = "gamma_resolved_metadata",
            UpdatedAtUtc = updatedAtUtc
        });

        var settings = GetStrategySettings(normalizedStrategyId);
        var availableBalance = Math.Max(0m, settings.LiveAvailableBalance + realizedPnlUsd);
        var liveStakes = availableBalance < settings.LiveStakeAmount ? false : settings.LiveStakes;
        StrategySettings[normalizedStrategyId] = settings with
        {
            LiveAvailableBalance = availableBalance,
            LiveStakes = liveStakes
        };

        return Task.FromResult(new StrategyLiveBalanceAdjustmentResult(
            true,
            availableBalance,
            !liveStakes && availableBalance < settings.LiveStakeAmount));
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

    public Task AddPaperLiveShadowDecisionAsync(PaperLiveShadowDecision decision, CancellationToken cancellationToken = default)
    {
        PaperLiveShadowDecisions.Add(decision);
        return Task.CompletedTask;
    }

    public Task UpdatePaperLiveShadowDecisionLinksAsync(
        Guid correlationId,
        Guid? signalId,
        Guid? paperOrderId,
        Guid? liveOrderId,
        string status,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var existing = PaperLiveShadowDecisions.FirstOrDefault(decision => decision.CorrelationId == correlationId);
        if (existing is not null)
        {
            PaperLiveShadowDecisions.Remove(existing);
            PaperLiveShadowDecisions.Add(existing with
            {
                SignalId = signalId ?? existing.SignalId,
                PaperOrderId = paperOrderId ?? existing.PaperOrderId,
                LiveOrderId = liveOrderId ?? existing.LiveOrderId,
                Status = status,
                UpdatedAtUtc = updatedAtUtc
            });
        }

        return Task.CompletedTask;
    }

    public Task AddPaperLiveShadowDiscrepancyAsync(PaperLiveShadowDiscrepancy discrepancy, CancellationToken cancellationToken = default)
    {
        PaperLiveShadowDiscrepancies.Add(discrepancy);
        return Task.CompletedTask;
    }

    public Task AddBtcUpDown5mOddsTickAsync(BtcUpDown5mOddsTick tick, CancellationToken cancellationToken = default)
    {
        BtcUpDown5mOddsTicks.Add(tick);
        return Task.CompletedTask;
    }

    public Task<decimal?> GetBtcUpDown5mOddsStartPriceAsync(string marketId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            BtcUpDown5mOddsTicks
                .Where(tick => string.Equals(tick.MarketId, marketId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tick => tick.SampledAtUtc)
                .Select(tick => (decimal?)tick.BinanceStartPriceUsd)
                .FirstOrDefault());
    }

    public Task<BtcUpDown5mOddsTick?> GetLatestBtcUpDown5mOddsTickAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            BtcUpDown5mOddsTicks
                .Where(tick => string.Equals(tick.MarketId, marketId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(tick => tick.SampledAtUtc)
                .ThenByDescending(tick => tick.CreatedAtUtc)
                .FirstOrDefault());
    }

    public Task<IReadOnlyList<BtcUpDown5mOddsTick>> GetRecentBtcUpDown5mOddsTicksAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<BtcUpDown5mOddsTick>>(
            BtcUpDown5mOddsTicks
                .OrderByDescending(tick => tick.SampledAtUtc)
                .Take(limit)
                .ToArray());
    }

    public Task<IReadOnlyList<PolymarketGammaMarket>> GetCryptoUpDown5mGammaMarketsAsync(
        IReadOnlyCollection<string> assetSymbols,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var allowed = assetSymbols
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>(
            PolymarketGammaMarkets
                .Where(market => CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(market, allowed, out _))
                .OrderBy(market => market.EventStartTimeUtc ?? market.EndDateUtc ?? market.CreatedAtUtc ?? DateTimeOffset.MaxValue)
                .Take(limit)
                .ToArray());
    }

    public Task AddCryptoUpDown5mOddsTickAsync(CryptoUpDown5mOddsTick tick, CancellationToken cancellationToken = default)
    {
        CryptoUpDown5mOddsTicks.Add(tick);
        return Task.CompletedTask;
    }

    public Task<decimal?> GetCryptoUpDown5mOddsStartPriceAsync(
        string assetSymbol,
        string marketId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            CryptoUpDown5mOddsTicks
                .Where(tick =>
                    string.Equals(tick.AssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(tick.MarketId, marketId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tick => tick.SampledAtUtc)
                .Select(tick => (decimal?)tick.BinanceStartPriceUsd)
                .FirstOrDefault());
    }

    public Task<IReadOnlyList<CryptoUpDown5mOddsTick>> GetRecentCryptoUpDown5mOddsTicksAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<CryptoUpDown5mOddsTick>>(
            CryptoUpDown5mOddsTicks
                .OrderByDescending(tick => tick.SampledAtUtc)
                .Take(limit)
                .ToArray());
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

    public Task<PolymarketHttpLogCleanupResult> CleanupPolymarketHttpLogsAsync(
        DateTimeOffset successfulBeforeUtc,
        DateTimeOffset failedBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var candidates = PolymarketHttpLogs
            .Where(item =>
                item.Succeeded
                    ? item.RequestedAtUtc < successfulBeforeUtc
                    : item.RequestedAtUtc < failedBeforeUtc)
            .OrderBy(item => item.RequestedAtUtc)
            .Take(batchSize)
            .ToArray();

        var successful = candidates.Count(item => item.Succeeded);
        var failed = candidates.Length - successful;
        foreach (var item in candidates)
        {
            PolymarketHttpLogs.Remove(item);
        }

        return Task.FromResult(new PolymarketHttpLogCleanupResult(candidates.Length, successful, failed));
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
            PolymarketOnChainTokenMetadataRefreshQueue.Add(fill.TokenId);
        }

        if (RebuildDerivedDataOnAddFills)
        {
            RebuildOnChainWalletDerivedData();
        }

        return Task.CompletedTask;
    }

    public Task<int> AddPolymarketOnChainTradeCapturesAsync(IReadOnlyList<PolymarketOnChainTradeCapture> captures, CancellationToken cancellationToken = default)
    {
        var rows = 0;
        foreach (var capture in captures)
        {
            PolymarketOnChainTradeCaptures.RemoveAll(item =>
                string.Equals(item.TransactionHash, capture.TransactionHash, StringComparison.OrdinalIgnoreCase) &&
                item.LogIndex == capture.LogIndex);
            PolymarketOnChainTradeCaptures.Add(capture);
            rows++;
        }

        return Task.FromResult(rows);
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

    public Task UpsertOnChainTradeCaptureCursorAsync(OnChainTradeCaptureCursor cursor, CancellationToken cancellationToken = default)
    {
        OnChainTradeCaptureCursors.RemoveAll(item =>
            string.Equals(item.ContractAddress, cursor.ContractAddress, StringComparison.OrdinalIgnoreCase));
        OnChainTradeCaptureCursors.Add(cursor);
        return Task.CompletedTask;
    }

    public Task<OnChainTradeCaptureCursor?> GetOnChainTradeCaptureCursorAsync(string contractAddress, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OnChainTradeCaptureCursor?>(
            OnChainTradeCaptureCursors.FirstOrDefault(item =>
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
            PolymarketOnChainTokenMetadataRefreshQueue.Add(tokenId);
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
            PolymarketOnChainTokenMetadataRefreshQueue
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
            if (item.LookupSucceeded && !string.IsNullOrWhiteSpace(item.Category))
            {
                PolymarketOnChainTokenMetadataRefreshQueue.Remove(item.TokenId);
            }
            else
            {
                PolymarketOnChainTokenMetadataRefreshQueue.Add(item.TokenId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<PolymarketOnChainTokenMetadata?> GetPolymarketOnChainTokenMetadataAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PolymarketOnChainTokenMetadata.FirstOrDefault(item =>
            string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase)));
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

    public Task<PolymarketOnChainWalletCategoryPerformance?> GetPolymarketOnChainWalletCategoryPerformanceAsync(
        string wallet,
        string category,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PolymarketOnChainWalletCategoryPerformance.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase)));
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

    public Task<OnChainSignalCandidateQueueRefreshResult> RefreshPolymarketOnChainSignalCandidateQueueAsync(
        int queueSeedLimit = 1_000,
        int retryLimit = 250,
        CancellationToken cancellationToken = default)
    {
        var queued = 0;
        if (!onChainSignalCandidateBackfillComplete)
        {
            var orderedFills = PolymarketOnChainWalletFills
                .OrderBy(fill => fill.BlockTimestampUtc)
                .ThenBy(fill => fill.BlockNumber)
                .ThenBy(fill => fill.LogIndex)
                .ThenBy(fill => fill.Role.ToString(), StringComparer.Ordinal)
                .ToArray();
            var selected = orderedFills
                .Skip(onChainSignalCandidateBackfillCursorIndex)
                .Take(queueSeedLimit)
                .ToArray();

            foreach (var fill in selected)
            {
                var existing = PolymarketOnChainSignalCandidates.FirstOrDefault(candidate =>
                    candidate.SourceFillId == fill.SourceFillId &&
                    string.Equals(candidate.ParticipantRole.ToString(), fill.Role.ToString(), StringComparison.OrdinalIgnoreCase));
                if (existing is null && PolymarketOnChainSignalCandidateRefreshQueue.Add(SignalCandidateQueueKey(fill.SourceFillId, fill.Role)))
                {
                    queued++;
                }
            }

            onChainSignalCandidateBackfillCursorIndex += selected.Length;
            onChainSignalCandidateBackfillComplete = onChainSignalCandidateBackfillCursorIndex >= orderedFills.Length;
        }

        var retriesQueued = 0;
        var refreshableRejected = PolymarketOnChainSignalCandidates
            .Where(candidate =>
                candidate.DecisionStatus == "Rejected" &&
                candidate.UpdatedAtUtc <= DateTimeOffset.UtcNow.AddMinutes(-10) &&
                IsRefreshableOnChainSignalCandidateDecision(candidate.DecisionCode))
            .OrderBy(candidate => candidate.UpdatedAtUtc)
            .ThenBy(candidate => candidate.BlockTimestampUtc)
            .Take(retryLimit);
        foreach (var candidate in refreshableRejected)
        {
            if (PolymarketOnChainSignalCandidateRefreshQueue.Add(SignalCandidateQueueKey(candidate.SourceFillId, candidate.ParticipantRole)))
            {
                retriesQueued++;
            }
        }

        return Task.FromResult(new OnChainSignalCandidateQueueRefreshResult(
            queued,
            retriesQueued,
            PolymarketOnChainSignalCandidateRefreshQueue.Count));
    }

    public Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetPendingOnChainPaperSignalCandidatesAsync(
        string ratingTimePeriod,
        string ratingOrderBy,
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        var processed = OnChainPaperSignalResults
            .Select(result => OnChainPaperSignalKey(result.TransactionHash, result.LogIndex, result.ParticipantRole))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = PolymarketOnChainTradeCaptures
            .Where(capture => !capture.Removed)
            .SelectMany(capture => ToOnChainPaperParticipants(capture, ratingTimePeriod, ratingOrderBy))
            .Where(candidate => !processed.Contains(OnChainPaperSignalKey(
                candidate.TransactionHash,
                candidate.LogIndex,
                candidate.ParticipantRole)))
            .OrderBy(candidate => candidate.BlockTimestampUtc)
            .ThenBy(candidate => candidate.BlockNumber)
            .ThenBy(candidate => candidate.LogIndex)
            .ThenBy(candidate => candidate.ParticipantRole.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

        return Task.FromResult<IReadOnlyList<OnChainPaperSignalCandidate>>(rows);
    }

    public Task<IReadOnlyList<OnChainPaperSignalCandidate>> GetOnChainPaperSignalCandidatesForCapturesAsync(
        IReadOnlyList<PolymarketOnChainTradeCapture> captures,
        string ratingTimePeriod,
        string ratingOrderBy,
        CancellationToken cancellationToken = default)
    {
        var processed = OnChainPaperSignalResults
            .Select(result => OnChainPaperSignalKey(result.TransactionHash, result.LogIndex, result.ParticipantRole))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = captures
            .Where(capture => !capture.Removed)
            .SelectMany(capture => ToOnChainPaperParticipants(capture, ratingTimePeriod, ratingOrderBy))
            .Where(candidate => !processed.Contains(OnChainPaperSignalKey(
                candidate.TransactionHash,
                candidate.LogIndex,
                candidate.ParticipantRole)))
            .OrderBy(candidate => candidate.BlockTimestampUtc)
            .ThenBy(candidate => candidate.BlockNumber)
            .ThenBy(candidate => candidate.LogIndex)
            .ThenBy(candidate => candidate.ParticipantRole.ToString(), StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult<IReadOnlyList<OnChainPaperSignalCandidate>>(rows);
    }

    public Task AddOnChainPaperSignalResultAsync(
        OnChainPaperSignalResult result,
        CancellationToken cancellationToken = default)
    {
        if (OnChainPaperSignalResults.Any(existing =>
            string.Equals(existing.TransactionHash, result.TransactionHash, StringComparison.OrdinalIgnoreCase) &&
            existing.LogIndex == result.LogIndex &&
            existing.ParticipantRole == result.ParticipantRole))
        {
            return Task.CompletedTask;
        }

        OnChainPaperSignalResults.Add(result);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainSignalCandidateSource>> GetPolymarketOnChainSignalCandidateSourcesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        var rows = PolymarketOnChainWalletFills
            .Where(fill => PolymarketOnChainSignalCandidateRefreshQueue.Contains(SignalCandidateQueueKey(fill.SourceFillId, fill.Role)))
            .OrderBy(fill => fill.BlockTimestampUtc)
            .ThenBy(fill => fill.BlockNumber)
            .ThenBy(fill => fill.LogIndex)
            .ThenBy(fill => fill.Role.ToString(), StringComparer.Ordinal)
            .Take(limit)
            .Select(fill =>
            {
                var metadata = PolymarketOnChainTokenMetadata.FirstOrDefault(item =>
                    string.Equals(item.TokenId, fill.TokenId, StringComparison.OrdinalIgnoreCase));
                var category = string.IsNullOrWhiteSpace(metadata?.Category) ? "unknown" : metadata!.Category;
                var performance = PolymarketOnChainWalletCategoryPerformance.FirstOrDefault(item =>
                    string.Equals(item.Wallet, fill.Wallet, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));

                return new PolymarketOnChainSignalCandidateSource(
                    fill.SourceFillId,
                    fill.ContractName,
                    fill.ContractAddress,
                    fill.ExchangeVersion,
                    fill.BlockNumber,
                    fill.BlockTimestampUtc,
                    fill.TransactionHash,
                    fill.LogIndex,
                    fill.OrderHash,
                    fill.Role,
                    fill.Wallet,
                    fill.Counterparty,
                    fill.Side,
                    fill.TokenId,
                    fill.Price,
                    fill.SizeShares,
                    fill.NotionalUsd,
                    fill.FeeAmount,
                    fill.FeeAssetId,
                    fill.ImportedAtUtc,
                    metadata,
                    performance);
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<PolymarketOnChainSignalCandidateSource>>(rows);
    }

    public Task UpsertPolymarketOnChainSignalCandidateDecisionsAsync(
        IReadOnlyList<PolymarketOnChainSignalCandidateDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        foreach (var decision in decisions)
        {
            var existing = PolymarketOnChainSignalCandidates.FirstOrDefault(candidate =>
                candidate.SourceFillId == decision.Candidate.SourceFillId &&
                candidate.ParticipantRole == decision.Candidate.ParticipantRole);
            var persistedCandidate = existing is null
                ? decision.Candidate
                : decision.Candidate with
                {
                    Id = existing.Id,
                    CreatedAtUtc = existing.CreatedAtUtc
                };

            if (existing is not null)
            {
                PolymarketOnChainSignalCandidates.Remove(existing);
            }

            PolymarketOnChainSignalCandidates.Add(persistedCandidate);
            PolymarketOnChainSignalCandidateReasons.RemoveAll(reason => reason.CandidateId == persistedCandidate.Id);
            PolymarketOnChainSignalCandidateReasons.AddRange(decision.Reasons.Select(reason => reason with
            {
                CandidateId = persistedCandidate.Id
            }));
            PolymarketOnChainSignalCandidateRefreshQueue.Remove(SignalCandidateQueueKey(
                persistedCandidate.SourceFillId,
                persistedCandidate.ParticipantRole));
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketOnChainSignalCandidate>> GetRecentPolymarketOnChainSignalCandidatesAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketOnChainSignalCandidate>>(
            PolymarketOnChainSignalCandidates
                .OrderByDescending(item => item.UpdatedAtUtc)
                .ThenByDescending(item => item.BlockTimestampUtc)
                .Take(limit)
                .ToArray());
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

    private IEnumerable<OnChainPaperSignalCandidate> ToOnChainPaperParticipants(
        PolymarketOnChainTradeCapture capture,
        string ratingTimePeriod,
        string ratingOrderBy)
    {
        yield return ToOnChainPaperSignalCandidate(
            capture,
            OnChainParticipantRole.Maker,
            capture.Maker,
            capture.Taker,
            capture.Side,
            ratingTimePeriod,
            ratingOrderBy);

        yield return ToOnChainPaperSignalCandidate(
            capture,
            OnChainParticipantRole.Taker,
            capture.Taker,
            capture.Maker,
            OppositeSide(capture.Side),
            ratingTimePeriod,
            ratingOrderBy);
    }

    private OnChainPaperSignalCandidate ToOnChainPaperSignalCandidate(
        PolymarketOnChainTradeCapture capture,
        OnChainParticipantRole role,
        string wallet,
        string counterparty,
        TradeSide side,
        string ratingTimePeriod,
        string ratingOrderBy)
    {
        var market = ResolveGammaMarket(capture.TokenId);
        var category = market.Market?.Category;
        var mapping = PolymarketCategoryMappings.FirstOrDefault(item =>
            string.Equals(item.LocalCategory, category, StringComparison.OrdinalIgnoreCase));
        var rating = PolymarketDataApiWalletCategoryRatings.FirstOrDefault(item =>
            string.Equals(item.Wallet, wallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.LocalCategory, category, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.PolymarketCategory, mapping?.PolymarketLeaderboardCategory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TimePeriod, ratingTimePeriod, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.OrderBy, ratingOrderBy, StringComparison.OrdinalIgnoreCase));

        return new OnChainPaperSignalCandidate(
            capture.Id,
            capture.ContractName,
            capture.ContractAddress,
            capture.ExchangeVersion,
            capture.BlockNumber,
            capture.BlockTimestampUtc,
            capture.TransactionHash,
            capture.LogIndex,
            capture.OrderHash,
            role,
            wallet.Trim().ToLowerInvariant(),
            counterparty.Trim().ToLowerInvariant(),
            side,
            capture.TokenId,
            capture.Price,
            capture.SizeShares,
            capture.NotionalUsd,
            market.Market?.ConditionId ?? string.Empty,
            market.Market?.MarketId ?? string.Empty,
            market.Market?.Slug ?? string.Empty,
            market.Market?.Question ?? string.Empty,
            market.Outcome,
            category,
            market.Market is not null,
            market.Market?.Active ?? false,
            market.Market?.Closed ?? false,
            market.Market?.Archived ?? false,
            market.Market?.Restricted ?? false,
            market.Market?.AcceptingOrders ?? false,
            market.Market?.EnableOrderBook ?? false,
            market.Market?.EndDateUtc,
            mapping?.PolymarketLeaderboardCategory,
            rating?.Found,
            rating?.Rank,
            rating?.UserName,
            rating?.LeaderboardPnlUsd,
            rating?.LeaderboardVolumeUsd,
            rating?.LeaderboardPnlToVolumePct,
            rating?.CurrentPositionsCount ?? 0,
            rating?.ClosedPositionsCount ?? 0,
            rating?.PositionsTotalPnlUsd ?? 0m,
            rating?.PositionsTotalPercentPnl,
            rating?.RefreshedAtUtc);
    }

    private GammaMarketResolution ResolveGammaMarket(string tokenId)
    {
        foreach (var market in PolymarketGammaMarkets.OrderByDescending(item => item.FetchedAtUtc))
        {
            for (var index = 0; index < market.ClobTokenIds.Count; index++)
            {
                if (!string.Equals(market.ClobTokenIds[index], tokenId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new GammaMarketResolution(
                    market,
                    index < market.Outcomes.Count ? market.Outcomes[index] : string.Empty);
            }
        }

        return new GammaMarketResolution(null, string.Empty);
    }

    private static TradeSide OppositeSide(TradeSide side)
    {
        return side switch
        {
            TradeSide.Buy => TradeSide.Sell,
            TradeSide.Sell => TradeSide.Buy,
            _ => TradeSide.Unknown
        };
    }

    private static string OnChainPaperSignalKey(string transactionHash, long logIndex, OnChainParticipantRole role)
    {
        return transactionHash.Trim().ToLowerInvariant() + "\u001F" + logIndex + "\u001F" + role;
    }

    private static bool IsRefreshableOnChainSignalCandidateDecision(string decisionCode)
    {
        return decisionCode is
            "missing_market_metadata" or
            "missing_market_category" or
            "missing_leader_category_performance" or
            "leader_category_performance_stale" or
            "leader_trade_too_small" or
            "unsupported_side" or
            "market_inactive" or
            "market_resolved";
    }

    private static string SignalCandidateQueueKey(Guid sourceFillId, OnChainParticipantRole role)
    {
        return sourceFillId.ToString("N") + "\u001F" + role;
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

    private sealed record GammaMarketResolution(PolymarketGammaMarket? Market, string Outcome);

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
        OrderBookSnapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<OrderBookSnapshot?> GetLatestOrderBookSnapshotAsync(string assetId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OrderBookSnapshots
            .Where(snapshot => string.Equals(snapshot.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(snapshot => snapshot.SnapshotAtUtc)
            .FirstOrDefault());
    }

    public Task<IReadOnlyList<OrderBookSnapshot>> GetLatestOrderBookSnapshotsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OrderBookSnapshot>>(OrderBookSnapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.AssetId))
            .GroupBy(snapshot => snapshot.AssetId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(snapshot => snapshot.SnapshotAtUtc).First())
            .OrderByDescending(snapshot => snapshot.SnapshotAtUtc)
            .Take(limit)
            .ToArray());
    }

    public Task AddMarketDataEventAsync(MarketDataEvent marketDataEvent, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MarketDataEvent>> GetRecentMarketDataEventsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MarketDataEvent>>([]);
    }

    public Task<bool> TryAddPolymarketWebSocketTradeTickAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default)
    {
        if (PolymarketWebSocketTradeTicks.Any(item =>
            string.Equals(item.DedupKey, tradeTick.DedupKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(false);
        }

        PolymarketWebSocketTradeTicks.Add(tradeTick);
        return Task.FromResult(true);
    }

    public Task UpdatePolymarketWebSocketTradeTickMatchAsync(PolymarketWebSocketTradeTick tradeTick, CancellationToken cancellationToken = default)
    {
        PolymarketWebSocketTradeTicks.RemoveAll(item =>
            string.Equals(item.DedupKey, tradeTick.DedupKey, StringComparison.OrdinalIgnoreCase));
        PolymarketWebSocketTradeTicks.Add(tradeTick);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetPendingPolymarketWebSocketTradeTickMatchesAsync(
        DateTimeOffset dueBeforeUtc,
        int maxAttempts,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketWebSocketTradeTick>>(
            PolymarketWebSocketTradeTicks
                .Where(item => item.TraderMatchStatus == TradeTickTraderMatchStatus.NotFound)
                .Where(item => item.MatchAttempts < maxAttempts)
                .Where(item => !string.IsNullOrWhiteSpace(item.ConditionId))
                .Where(item => item.LastMatchAttemptUtc is null || item.LastMatchAttemptUtc <= dueBeforeUtc)
                .OrderBy(item => item.LastMatchAttemptUtc ?? item.ReceivedAtUtc)
                .ThenBy(item => item.ReceivedAtUtc)
                .Take(limit)
                .ToArray());
    }

    public Task<IReadOnlyList<PolymarketWebSocketTradeTick>> GetRecentPolymarketWebSocketTradeTicksAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PolymarketWebSocketTradeTick>>(
            PolymarketWebSocketTradeTicks
                .OrderByDescending(item => item.ReceivedAtUtc)
                .Take(limit)
                .ToArray());
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
            PaperOrders.Count(order => order.Status is PaperOrderStatus.Expired or PaperOrderStatus.PartiallyFilledExpired),
            PaperPositions.Sum(position => position.UnrealizedPnlUsd) +
                PaperFills.Sum(fill => fill.RealizedPnlUsd) +
                PaperPositionSettlements.Sum(settlement => settlement.RealizedPnlUsd),
            PaperOrders.Where(order => order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled).Sum(order => order.NotionalUsd)
                + PaperPositions.Sum(position => position.EstimatedValueUsd),
            string.Empty,
            ApiErrors.Count,
            DateTimeOffset.UtcNow));
    }

    private IReadOnlyList<PaperCopiedTraderPerformance> BuildPaperCopiedTraderPerformance()
    {
        var wallets = PaperOrders.Select(order => order.CopiedTraderWallet)
            .Concat(PaperPositions.Select(position => position.CopiedTraderWallet))
            .Concat(PaperPositionSettlements.Select(settlement => settlement.CopiedTraderWallet))
            .Where(wallet => !string.IsNullOrWhiteSpace(wallet))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rows = new List<PaperCopiedTraderPerformance>();
        foreach (var wallet in wallets)
        {
            var categories = new[] { "OVERALL" }
                .Concat(PaperOrders.Where(order => SameWallet(order.CopiedTraderWallet, wallet)).Select(order => CategoryForCondition(order.ConditionId)))
                .Concat(PaperPositions.Where(position => SameWallet(position.CopiedTraderWallet, wallet)).Select(position => CategoryForCondition(position.ConditionId)))
                .Concat(PaperPositionSettlements.Where(settlement => SameWallet(settlement.CopiedTraderWallet, wallet)).Select(settlement => settlement.Category ?? CategoryForCondition(settlement.ConditionId)))
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var category in categories)
            {
                var orders = PaperOrders
                    .Where(order => SameWallet(order.CopiedTraderWallet, wallet) && MatchesCategory(order.ConditionId, category))
                    .ToArray();
                var orderIds = orders.Select(order => order.Id).ToHashSet();
                var fills = PaperFills.Where(fill => orderIds.Contains(fill.PaperOrderId)).ToArray();
                var positions = PaperPositions
                    .Where(position => SameWallet(position.CopiedTraderWallet, wallet) && MatchesCategory(position.ConditionId, category))
                    .ToArray();
                var settlements = PaperPositionSettlements
                    .Where(settlement => SameWallet(settlement.CopiedTraderWallet, wallet) &&
                        (string.Equals(category, "OVERALL", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(settlement.Category ?? CategoryForCondition(settlement.ConditionId), category, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                var buyCost = fills
                    .Join(PaperOrders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => (fill, order))
                    .Where(item => item.order.Side == TradeSide.Buy)
                    .Sum(item => item.fill.Price * item.fill.SizeShares);
                var sellProceeds = fills
                    .Join(PaperOrders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => (fill, order))
                    .Where(item => item.order.Side == TradeSide.Sell)
                    .Sum(item => item.fill.Price * item.fill.SizeShares);
                var realized = fills.Sum(fill => fill.RealizedPnlUsd) + settlements.Sum(settlement => settlement.RealizedPnlUsd);
                var unrealized = positions.Sum(position => position.UnrealizedPnlUsd);
                var total = realized + unrealized;
                var roi = buyCost == 0m ? 0m : total / buyCost * 100m;
                var settled = settlements.Length;
                var winRate = settled == 0 ? 0m : settlements.Count(settlement => settlement.Won) * 100m / settled;
                var lost = settlements.Count(settlement => !settlement.Won);
                var open = positions.Count(position => position.SizeShares > 0m);
                var score = CalculateCopiedTraderPerformanceScore(total, roi, winRate, settled, lost, open);

                rows.Add(new PaperCopiedTraderPerformance(
                    wallet,
                    category,
                    orders.Length,
                    orders.Count(order => order.Status is PaperOrderStatus.Filled or PaperOrderStatus.PartiallyFilled or PaperOrderStatus.PartiallyFilledExpired),
                    fills.Join(PaperOrders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => order).Count(order => order.Side == TradeSide.Buy),
                    fills.Join(PaperOrders, fill => fill.PaperOrderId, order => order.Id, (fill, order) => order).Count(order => order.Side == TradeSide.Sell),
                    positions.Count(position => position.SizeShares > 0m),
                    settled,
                    settlements.Count(settlement => settlement.Won),
                    lost,
                    buyCost,
                    sellProceeds,
                    settlements.Sum(settlement => settlement.SettlementValueUsd),
                    realized,
                    unrealized,
                    total,
                    roi,
                    winRate,
                    score,
                    orders.Select(order => (DateTimeOffset?)order.CreatedAtUtc).DefaultIfEmpty(null).Min(),
                    orders.Select(order => (DateTimeOffset?)order.CreatedAtUtc).DefaultIfEmpty(null).Max(),
                    DateTimeOffset.UtcNow));
            }
        }

        return rows;
    }

    private static decimal CalculateCopiedTraderPerformanceScore(
        decimal totalPnlUsd,
        decimal roiPct,
        decimal winRatePct,
        int settledPositionsCount,
        int lostPositionsCount,
        int openPositionsCount)
    {
        var score = 50m
            + Clamp(roiPct, -50m, 50m) * 0.35m
            + (winRatePct - 50m) * 0.25m
            + Clamp(totalPnlUsd, -20m, 20m) * 1.25m
            + Math.Min(settledPositionsCount, 20) * 0.5m
            - lostPositionsCount * 1.25m
            - openPositionsCount * 0.1m;

        return Clamp(score, 0m, 100m);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static BtcUpDown5mMarketResult? TryCreateBtcUpDown5mMarketResult(
        IGrouping<string, StrategyMarketPaperRun> group)
    {
        var rows = group
            .OrderByDescending(run => run.MarketStartUtc ?? run.MarketEndUtc ?? run.SettledAtUtc ?? run.UpdatedAtUtc)
            .ThenByDescending(run => run.SettledAtUtc ?? run.UpdatedAtUtc)
            .ToArray();
        var winners = rows
            .Select(run => TryInferBtcWinningOutcome(run.SelectedOutcome, run.RealizedPnlUsd))
            .Where(outcome => !string.IsNullOrWhiteSpace(outcome))
            .Select(outcome => outcome!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (winners.Length != 1)
        {
            return null;
        }

        var latest = rows[0];
        return new BtcUpDown5mMarketResult(
            latest.MarketId,
            latest.ConditionId,
            latest.MarketSlug,
            latest.MarketStartUtc,
            latest.MarketEndUtc,
            winners[0],
            rows.Select(run => run.SettledAtUtc ?? run.UpdatedAtUtc).Max());
    }

    private static string? TryInferBtcWinningOutcome(string? selectedOutcome, decimal? realizedPnlUsd)
    {
        var normalized = NormalizeBtcOutcome(selectedOutcome);
        if (normalized is null || realizedPnlUsd is null or 0m)
        {
            return null;
        }

        return realizedPnlUsd > 0m ? normalized : OppositeBtcOutcome(normalized);
    }

    private static string? NormalizeBtcOutcome(string? outcome)
    {
        if (string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase))
        {
            return "Up";
        }

        return string.Equals(outcome, "Down", StringComparison.OrdinalIgnoreCase) ? "Down" : null;
    }

    private static string OppositeBtcOutcome(string outcome)
    {
        return string.Equals(outcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Down" : "Up";
    }

    private string CategoryForCondition(string conditionId)
    {
        return PolymarketGammaMarkets.FirstOrDefault(market =>
            string.Equals(market.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))?.Category ?? "unknown";
    }

    private bool MatchesCategory(string conditionId, string category)
    {
        return string.Equals(category, "OVERALL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CategoryForCondition(conditionId), category, StringComparison.OrdinalIgnoreCase);
    }

    private static Guid? StrategyIdForSyntheticWallet(string wallet)
    {
        if (!wallet.StartsWith("strategy:", StringComparison.OrdinalIgnoreCase))
        {
            return StrategyIds.FollowLeader;
        }

        if (string.Equals(wallet, "strategy:" + StrategyIds.FollowLeaderCode, StringComparison.OrdinalIgnoreCase))
        {
            return StrategyIds.FollowLeader;
        }

        foreach (var variant in StrategyIds.BtcUpDown5mVariants)
        {
            if (string.Equals(wallet, variant.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase))
            {
                return variant.Id;
            }
        }

        return null;
    }

    private StrategyRuntimeSettings GetStrategySettings(Guid strategyId)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        if (StrategySettings.TryGetValue(normalizedStrategyId, out var settings))
        {
            return settings with
            {
                Enabled = StrategyEnabledStates.GetValueOrDefault(normalizedStrategyId, settings.Enabled)
            };
        }

        return StrategyRuntimeSettings.Default(normalizedStrategyId) with
        {
            Enabled = StrategyEnabledStates.GetValueOrDefault(normalizedStrategyId, true)
        };
    }

    private static bool SameWallet(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
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
        ServiceHeartbeatAttempt.TrySetResult(heartbeat);
        if (ThrowOnUpsertServiceHeartbeat)
        {
            throw new InvalidOperationException("simulated heartbeat database recovery");
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceHeartbeat>>([]);
    }
}
