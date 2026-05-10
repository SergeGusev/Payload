using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Scanning;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.DataApiTraderActivity;

public sealed class DataApiTraderActivityIngestionProcessor(
    ILogger<DataApiTraderActivityIngestionProcessor> logger,
    DataApiTraderIngestionOptions options,
    IPolymarketDataApiClient dataApiClient,
    IPolymarketGammaClient gammaClient,
    IAppRepository repository) : IDataApiTraderActivityIngestionProcessor
{
    private const string ComponentName = "DataApiTraderActivityIngestion";
    private readonly Dictionary<string, string?> categoryByCondition = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> categoryByEventId = new(StringComparer.OrdinalIgnoreCase);

    public async Task<DataApiTraderActivityIngestionResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var globalTrades = await dataApiClient.GetGlobalDataApiTradesAsync(
            options.TakerOnly,
            options.GlobalTradesLimit,
            offset: 0,
            cacheBuster,
            cancellationToken);

        var normalizedGlobalTrades = globalTrades
            .Where(trade => WalletAddressValidator.IsValid(trade.TraderWallet))
            .Select(trade => NormalizeTradeWallet(trade, trade.TraderWallet))
            .ToArray();
        var traderGroups = normalizedGlobalTrades
            .GroupBy(trade => trade.TraderWallet, StringComparer.OrdinalIgnoreCase)
            .Take(options.MaxTradersPerCycle)
            .ToArray();

        var traders = traderGroups
            .Select(traderGroup => traderGroup
                .OrderByDescending(trade => trade.TimestampUtc)
                .First())
            .Select(trade => BuildTrader(trade, existing: null))
            .ToArray();
        var tradersUpserted = await repository.UpsertPolymarketDataApiTradersAsync(traders, cancellationToken);

        return new DataApiTraderActivityIngestionResult(
            globalTrades.Count,
            normalizedGlobalTrades.Length,
            traderGroups.Length,
            tradersUpserted,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);
    }

    public async Task<DataApiTraderActivityIngestionResult> RefreshTraderSyncBatchAsync(
        CancellationToken cancellationToken = default)
    {
        var incrementalSyncBeforeUtc = DateTimeOffset.UtcNow.AddSeconds(-options.ExistingTraderRefreshIntervalSeconds);
        var traders = await repository.GetPolymarketDataApiTradersForSyncAsync(
            options.SyncBatchSize,
            incrementalSyncBeforeUtc,
            cancellationToken);

        var fullSyncs = 0;
        var incrementalSyncs = 0;
        var traderSyncFailures = 0;
        var userTradesFetched = 0;
        var userTradesAdvanced = 0;
        var positionRefreshes = 0;
        var positionRefreshFailures = 0;
        var currentPositionsFetched = 0;
        var closedPositionsFetched = 0;
        var positionsUpserted = 0;
        var walletPerformanceRowsUpserted = 0;
        var categoryPerformanceRowsUpserted = 0;

        foreach (var trader in traders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wallet = trader.Wallet;
            var isFullSync = !trader.FullSyncCompleted;

            try
            {
                var syncResult = isFullSync
                    ? await SyncFullTraderActivityAsync(wallet, cancellationToken)
                    : await SyncFreshTraderActivityAsync(trader, cancellationToken);

                userTradesFetched += syncResult.TradesFetched;
                userTradesAdvanced += syncResult.NewTradesObserved;
                if (isFullSync)
                {
                    fullSyncs++;
                }
                else
                {
                    incrementalSyncs++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                traderSyncFailures++;
                logger.LogWarning(ex, "Data API trader activity sync failed for {Wallet}.", wallet);
                await TryRecordApiErrorAsync("SyncTraderActivity", $"Wallet {wallet}: {ex.Message}", cancellationToken);
            }

            /*
            Legacy self-computed positions/performance refresh is intentionally disabled.
            The methods below are kept in this file so we can return to our own rating
            calculation if the Polymarket-only rating table does not provide enough coverage.

            if (options.RefreshPositionsEnabled && positionRefreshes < options.MaxPositionRefreshesPerCycle)
            {
                var performanceResult = await RefreshTraderPositionsAndPerformanceAsync(wallet, cancellationToken);
                ...
                await LogMissingPolymarketLeaderboardCategoryMappingsAsync(wallet, cancellationToken);
            }
            */
        }

        return new DataApiTraderActivityIngestionResult(
            0,
            0,
            traders.Count,
            0,
            0,
            0,
            fullSyncs,
            incrementalSyncs,
            traderSyncFailures,
            userTradesFetched,
            userTradesAdvanced,
            0,
            positionRefreshes,
            positionRefreshFailures,
            currentPositionsFetched,
            closedPositionsFetched,
            positionsUpserted,
            walletPerformanceRowsUpserted,
            categoryPerformanceRowsUpserted);
    }

    public async Task<DataApiTraderRatingRefreshResult> RefreshPolymarketRatingBatchAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var traders = await repository.GetPolymarketDataApiTradersForRatingRefreshAsync(
            options.SyncBatchSize,
            now,
            cancellationToken);

        if (traders.Count == 0)
        {
            return new DataApiTraderRatingRefreshResult(0, 0, 0, 0, 0);
        }

        var mappings = await repository.GetEnabledPolymarketCategoryMappingsAsync(cancellationToken);
        if (mappings.Count == 0)
        {
            logger.LogWarning("Polymarket rating refresh skipped because no enabled category mappings exist.");
            await TryRecordApiErrorAsync(
                "RefreshPolymarketRatings",
                "No enabled Polymarket category mappings exist.",
                cancellationToken);
            return new DataApiTraderRatingRefreshResult(traders.Count, 0, 0, traders.Count, 0);
        }

        var walletRefreshes = 0;
        var walletFailures = 0;
        var ratingRowsUpserted = 0;
        var currentPositionsFetched = 0;
        var closedPositionsFetched = 0;
        foreach (var trader in traders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var ratingResult = await FetchPolymarketCategoryRatingsAsync(
                    trader.Wallet,
                    mappings,
                    now,
                    cancellationToken);
                currentPositionsFetched += ratingResult.CurrentPositionsFetched;
                closedPositionsFetched += ratingResult.ClosedPositionsFetched;
                ratingRowsUpserted += await repository.UpsertPolymarketDataApiWalletCategoryRatingsAsync(
                    ratingResult.Ratings,
                    cancellationToken);
                await repository.MarkPolymarketDataApiTraderRatingRefreshedAsync(
                    trader.Wallet,
                    now,
                    now.AddSeconds(options.PolymarketRatingRefreshIntervalSeconds),
                    cancellationToken);
                walletRefreshes++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                walletFailures++;
                logger.LogWarning(ex, "Polymarket-only rating refresh failed for {Wallet}.", trader.Wallet);
                await TryRecordApiErrorAsync(
                    "RefreshPolymarketRatings",
                    $"Wallet {trader.Wallet}: {ex.Message}",
                    cancellationToken);
                await repository.MarkPolymarketDataApiTraderRatingRefreshFailedAsync(
                    trader.Wallet,
                    ex.Message,
                    now.AddSeconds(options.PolymarketRatingFailureDelaySeconds),
                    cancellationToken);
            }
        }

        return new DataApiTraderRatingRefreshResult(
            traders.Count,
            mappings.Count,
            walletRefreshes,
            walletFailures,
            ratingRowsUpserted,
            currentPositionsFetched,
            closedPositionsFetched);
    }

    private async Task<PolymarketRatingFetchResult> FetchPolymarketCategoryRatingsAsync(
        string wallet,
        IReadOnlyList<PolymarketCategoryMapping> mappings,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedWallet = WalletAddressValidator.Normalize(wallet);
        var entryByPolymarketCategory = new Dictionary<string, TraderLeaderboardEntry?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var polymarketCategory in mappings
            .Select(mapping => mapping.PolymarketLeaderboardCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await dataApiClient.GetTraderLeaderboardAsync(
                polymarketCategory,
                options.PolymarketRatingTimePeriod,
                options.PolymarketRatingOrderBy,
                limit: 1,
                offset: 0,
                user: normalizedWallet,
                cancellationToken);
            entryByPolymarketCategory[polymarketCategory] = entries.FirstOrDefault(entry =>
                string.Equals(entry.Wallet, normalizedWallet, StringComparison.OrdinalIgnoreCase));

            if (options.PolymarketRatingRequestDelayMilliseconds > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(options.PolymarketRatingRequestDelayMilliseconds),
                    cancellationToken);
            }
        }

        var currentPositions = Array.Empty<PolymarketDataApiPosition>();
        var closedPositions = Array.Empty<PolymarketDataApiPosition>();
        if (options.PolymarketRatingPositionsEnabled)
        {
            currentPositions = (await FetchRatingCurrentPositionsAsync(normalizedWallet, cancellationToken)).ToArray();
            closedPositions = (await FetchRatingClosedPositionsAsync(normalizedWallet, cancellationToken)).ToArray();
            currentPositions = (await EnrichPositionCategoriesAsync(currentPositions, cancellationToken)).ToArray();
            closedPositions = (await EnrichPositionCategoriesAsync(closedPositions, cancellationToken)).ToArray();
        }

        var positionSummaries = BuildPositionCategorySummaries(
            mappings,
            currentPositions,
            closedPositions,
            options.PolymarketRatingPositionsEnabled ? refreshedAtUtc : null);

        var ratings = mappings
            .Select(mapping =>
            {
                entryByPolymarketCategory.TryGetValue(mapping.PolymarketLeaderboardCategory, out var entry);
                positionSummaries.TryGetValue(BuildRatingMappingKey(mapping), out var summary);
                summary ??= PositionCategorySummary.Empty(null);
                return new PolymarketDataApiWalletCategoryRating(
                    normalizedWallet,
                    mapping.LocalCategory,
                    mapping.PolymarketLeaderboardCategory,
                    options.PolymarketRatingTimePeriod,
                    options.PolymarketRatingOrderBy,
                    entry is not null,
                    entry?.Rank,
                    entry?.UserName,
                    entry?.XUsername,
                    entry?.ProfileImage,
                    entry?.VerifiedBadge ?? false,
                    entry?.Pnl,
                    entry?.Volume,
                    Percent(entry?.Pnl, entry?.Volume),
                    refreshedAtUtc,
                    entry is null ? "{}" : JsonSerializer.Serialize(entry),
                    summary.CurrentPositionsCount,
                    summary.CurrentPositionsInitialValueUsd,
                    summary.CurrentPositionsCurrentValueUsd,
                    summary.CurrentPositionsCashPnlUsd,
                    summary.CurrentPositionsRealizedPnlUsd,
                    summary.CurrentPositionsTotalPnlUsd,
                    summary.CurrentPositionsPercentPnl,
                    summary.CurrentPositionsPercentRealizedPnl,
                    summary.ClosedPositionsCount,
                    summary.ClosedPositionsCostBasisUsd,
                    summary.ClosedPositionsRealizedPnlUsd,
                    summary.ClosedPositionsPercentRealizedPnl,
                    summary.PositionsTotalCostBasisUsd,
                    summary.PositionsTotalPnlUsd,
                    summary.PositionsTotalPercentPnl,
                    summary.PositionsRefreshedAtUtc);
            })
            .ToArray();

        return new PolymarketRatingFetchResult(
            ratings,
            currentPositions.Length,
            closedPositions.Length);
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> FetchRatingCurrentPositionsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var results = new List<PolymarketDataApiPosition>();
        var limit = Math.Max(1, options.PolymarketRatingCurrentPositionsLimit);
        for (var offset = 0; offset <= options.PolymarketRatingMaxCurrentPositionsOffset; offset += limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserCurrentPositionsAsync(
                wallet,
                limit,
                offset,
                sortBy: "CURRENT",
                sortDirection: "DESC",
                timestampCacheBuster: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken: cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            results.AddRange(page.Select(position => NormalizePositionWallet(position, wallet)));
            if (page.Count < limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> FetchRatingClosedPositionsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var results = new List<PolymarketDataApiPosition>();
        var limit = Math.Max(1, options.PolymarketRatingClosedPositionsLimit);
        for (var offset = 0; offset <= options.PolymarketRatingMaxClosedPositionsOffset; offset += limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserClosedPositionsAsync(
                wallet,
                limit,
                offset,
                sortBy: "TIMESTAMP",
                sortDirection: "DESC",
                timestampCacheBuster: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken: cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            results.AddRange(page.Select(position => NormalizePositionWallet(position, wallet)));
            if (page.Count < limit)
            {
                break;
            }
        }

        return results;
    }

    private static IReadOnlyDictionary<string, PositionCategorySummary> BuildPositionCategorySummaries(
        IReadOnlyList<PolymarketCategoryMapping> mappings,
        IReadOnlyList<PolymarketDataApiPosition> currentPositions,
        IReadOnlyList<PolymarketDataApiPosition> closedPositions,
        DateTimeOffset? refreshedAtUtc)
    {
        var mappedLocalCategories = mappings
            .Select(mapping => mapping.LocalCategory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var summaries = new Dictionary<string, PositionCategorySummary>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            var current = currentPositions
                .Where(position => PositionMatchesMapping(position.Category, mapping, mappedLocalCategories))
                .ToArray();
            var closed = closedPositions
                .Where(position => PositionMatchesMapping(position.Category, mapping, mappedLocalCategories))
                .ToArray();
            summaries[BuildRatingMappingKey(mapping)] = BuildPositionCategorySummary(
                current,
                closed,
                refreshedAtUtc);
        }

        return summaries;
    }

    private static PositionCategorySummary BuildPositionCategorySummary(
        IReadOnlyList<PolymarketDataApiPosition> currentPositions,
        IReadOnlyList<PolymarketDataApiPosition> closedPositions,
        DateTimeOffset? refreshedAtUtc)
    {
        var currentInitialValue = currentPositions.Sum(position => position.CostBasisUsd);
        var currentValue = currentPositions.Sum(position => position.CurrentValue ?? 0m);
        var currentCashPnl = currentPositions.Sum(position => position.CashPnl ?? 0m);
        var currentRealizedPnl = currentPositions.Sum(position => position.RealizedPnl);
        var currentTotalPnl = currentPositions.Sum(position => position.PositionPnlUsd);
        var closedCostBasis = closedPositions.Sum(position => position.CostBasisUsd);
        var closedRealizedPnl = closedPositions.Sum(position => position.RealizedPnl);
        var totalCostBasis = currentInitialValue + closedCostBasis;
        var totalPnl = currentTotalPnl + closedRealizedPnl;

        return new PositionCategorySummary(
            currentPositions.Count,
            currentInitialValue,
            currentValue,
            currentCashPnl,
            currentRealizedPnl,
            currentTotalPnl,
            Percent(currentTotalPnl, currentInitialValue),
            Percent(currentRealizedPnl, currentInitialValue),
            closedPositions.Count,
            closedCostBasis,
            closedRealizedPnl,
            Percent(closedRealizedPnl, closedCostBasis),
            totalCostBasis,
            totalPnl,
            Percent(totalPnl, totalCostBasis),
            refreshedAtUtc);
    }

    private static bool PositionMatchesMapping(
        string? category,
        PolymarketCategoryMapping mapping,
        IReadOnlyCollection<string> mappedLocalCategories)
    {
        var normalizedCategory = NormalizeCategory(category);
        if (!HasCategory(normalizedCategory) ||
            string.Equals(normalizedCategory, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(normalizedCategory, mapping.LocalCategory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mappedLocalCategories.Contains(normalizedCategory!))
        {
            return false;
        }

        return string.Equals(
            NormalizePolymarketCategoryKey(normalizedCategory!),
            NormalizePolymarketCategoryKey(mapping.PolymarketLeaderboardCategory),
            StringComparison.Ordinal);
    }

    private static string BuildRatingMappingKey(PolymarketCategoryMapping mapping)
    {
        return string.Concat(mapping.LocalCategory, "|", mapping.PolymarketLeaderboardCategory);
    }

    private static string NormalizePolymarketCategoryKey(string category)
    {
        return category
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToUpperInvariant();
    }

    private static decimal? Percent(decimal numerator, decimal denominator)
    {
        return denominator == 0m ? null : numerator / denominator * 100m;
    }

    private static decimal? Percent(decimal? numerator, decimal? denominator)
    {
        return numerator.HasValue && denominator is > 0m
            ? numerator.Value / denominator.Value * 100m
            : null;
    }

    private async Task<DataApiTraderActivitySyncResult> SyncFullTraderActivityAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var tradesFetched = 0;
        var newTradesObserved = 0;
        var latestTradeTimestamp = default(DateTimeOffset?);

        for (var offset = 0; offset <= options.MaxUserHistoricalOffset; offset += options.UserTradesLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserDataApiTradesAsync(
                wallet,
                options.TakerOnly,
                options.UserTradesLimit,
                offset,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            tradesFetched += page.Count;
            foreach (var trade in page)
            {
                if (!WalletAddressValidator.IsValid(trade.TraderWallet))
                {
                    continue;
                }

                var normalized = NormalizeTradeWallet(trade, wallet);
                latestTradeTimestamp = Max(latestTradeTimestamp, normalized.TimestampUtc);
                newTradesObserved++;
            }

            if (page.Count < options.UserTradesLimit)
            {
                break;
            }
        }

        await repository.MarkPolymarketDataApiTraderSyncedAsync(
            wallet,
            fullSync: true,
            tradesFetched,
            newTradesObserved,
            latestTradeTimestamp,
            cancellationToken);

        return new DataApiTraderActivitySyncResult(tradesFetched, newTradesObserved, latestTradeTimestamp);
    }

    private async Task<PolymarketDataApiPerformanceRefreshResult> RefreshTraderPositionsAndPerformanceAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var currentPositions = await FetchCurrentPositionsAsync(wallet, cancellationToken);
        var closedPositions = await FetchClosedPositionsAsync(wallet, cancellationToken);
        currentPositions = await EnrichPositionCategoriesAsync(currentPositions, cancellationToken);
        closedPositions = await EnrichPositionCategoriesAsync(closedPositions, cancellationToken);

        return await repository.RefreshPolymarketDataApiPositionsAndPerformanceAsync(
            wallet,
            currentPositions,
            closedPositions,
            cancellationToken);
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> FetchCurrentPositionsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var results = new List<PolymarketDataApiPosition>();
        for (var offset = 0; offset <= options.MaxCurrentPositionsOffset; offset += options.CurrentPositionsLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserCurrentPositionsAsync(
                wallet,
                options.CurrentPositionsLimit,
                offset,
                sortBy: "CURRENT",
                sortDirection: "DESC",
                timestampCacheBuster: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken: cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            results.AddRange(page.Select(position => NormalizePositionWallet(position, wallet)));
            if (page.Count < options.CurrentPositionsLimit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> FetchClosedPositionsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        var results = new List<PolymarketDataApiPosition>();
        for (var offset = 0; offset <= options.MaxClosedPositionsOffset; offset += options.ClosedPositionsLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserClosedPositionsAsync(
                wallet,
                options.ClosedPositionsLimit,
                offset,
                sortBy: "TIMESTAMP",
                sortDirection: "DESC",
                timestampCacheBuster: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken: cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            results.AddRange(page.Select(position => NormalizePositionWallet(position, wallet)));
            if (page.Count < options.ClosedPositionsLimit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<PolymarketDataApiPosition>> EnrichPositionCategoriesAsync(
        IReadOnlyList<PolymarketDataApiPosition> positions,
        CancellationToken cancellationToken)
    {
        if (positions.Count == 0)
        {
            return positions;
        }

        var enriched = new List<PolymarketDataApiPosition>(positions.Count);
        foreach (var position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasCategory(position.Category))
            {
                enriched.Add(position with { Category = NormalizeCategory(position.Category) });
                continue;
            }

            var category = await ResolvePositionCategoryAsync(
                position,
                categoryByCondition,
                categoryByEventId,
                cancellationToken);

            enriched.Add(HasCategory(category)
                ? position with { Category = category }
                : position);
        }

        return enriched;
    }

    private async Task<string?> ResolvePositionCategoryAsync(
        PolymarketDataApiPosition position,
        Dictionary<string, string?> categoryByCondition,
        Dictionary<string, string?> categoryByEventId,
        CancellationToken cancellationToken)
    {
        var expectedClosed = position.Status == PolymarketDataApiPositionStatus.Closed;
        var category = await ResolveConditionCategoryAsync(
            position,
            expectedClosed,
            categoryByCondition,
            categoryByEventId,
            cancellationToken);
        if (HasCategory(category))
        {
            return category;
        }

        category = await ResolveConditionCategoryAsync(
            position,
            !expectedClosed,
            categoryByCondition,
            categoryByEventId,
            cancellationToken);
        if (HasCategory(category))
        {
            return category;
        }

        return await ResolveEventCategoryAsync(position.EventId, categoryByEventId, cancellationToken);
    }

    private async Task<string?> ResolveConditionCategoryAsync(
        PolymarketDataApiPosition position,
        bool closed,
        Dictionary<string, string?> categoryByCondition,
        Dictionary<string, string?> categoryByEventId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(position.ConditionId) || string.IsNullOrWhiteSpace(position.AssetId))
        {
            return null;
        }

        var key = BuildCategoryConditionKey(position.ConditionId, position.AssetId, closed);
        if (categoryByCondition.TryGetValue(key, out var cachedCategory))
        {
            return cachedCategory;
        }

        try
        {
            var metadata = await gammaClient.GetTokenMetadataByConditionIdAsync(
                position.ConditionId,
                position.AssetId,
                closed,
                cancellationToken);

            var category = metadata
                .Select(item => NormalizeCategory(item.Category))
                .FirstOrDefault(HasCategory);
            if (!HasCategory(category))
            {
                category = await ResolveMetadataEventCategoryAsync(
                    metadata,
                    categoryByEventId,
                    cancellationToken);
            }

            categoryByCondition[key] = category;
            return category;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            categoryByCondition[key] = null;
            logger.LogWarning(
                ex,
                "Gamma category enrichment failed for Data API position. ConditionId={ConditionId} AssetId={AssetId} Closed={Closed}",
                position.ConditionId,
                position.AssetId,
                closed);
            await TryRecordApiErrorAsync(
                "EnrichPositionCategory",
                $"Condition {position.ConditionId} asset {position.AssetId} closed {closed}: {ex.Message}",
                cancellationToken);
            return null;
        }
    }

    private async Task<string?> ResolveMetadataEventCategoryAsync(
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        Dictionary<string, string?> categoryByEventId,
        CancellationToken cancellationToken)
    {
        var eventId = metadata
            .Select(item => TryParseGammaMarketEventId(item.RawJson))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return await ResolveEventCategoryAsync(eventId, categoryByEventId, cancellationToken);
    }

    private async Task<string?> ResolveEventCategoryAsync(
        string? eventId,
        Dictionary<string, string?> categoryByEventId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return null;
        }

        if (categoryByEventId.TryGetValue(eventId, out var cachedCategory))
        {
            return cachedCategory;
        }

        try
        {
            var category = NormalizeCategory(await gammaClient.GetEventCategoryAsync(eventId, cancellationToken));
            categoryByEventId[eventId] = category;
            return category;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            categoryByEventId[eventId] = null;
            logger.LogWarning(
                ex,
                "Gamma event category enrichment failed for Data API position. EventId={EventId}",
                eventId);
            await TryRecordApiErrorAsync(
                "EnrichPositionEventCategory",
                $"Event {eventId}: {ex.Message}",
                cancellationToken);
            return null;
        }
    }

    private async Task<DataApiTraderActivitySyncResult> SyncFreshTraderActivityAsync(
        PolymarketDataApiTrader trader,
        CancellationToken cancellationToken)
    {
        var wallet = trader.Wallet;
        var knownLatestTradeTimestamp = trader.LastTradeTimestampUtc?.ToUniversalTime();
        var tradesFetched = 0;
        var newTradesObserved = 0;
        var latestTradeTimestamp = default(DateTimeOffset?);
        var reachedExistingTrade = false;

        for (var offset = 0; offset <= options.MaxUserHistoricalOffset; offset += options.UserTradesLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await dataApiClient.GetUserDataApiTradesAsync(
                wallet,
                options.TakerOnly,
                options.UserTradesLimit,
                offset,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken);

            if (page.Count == 0)
            {
                break;
            }

            tradesFetched += page.Count;
            foreach (var trade in page)
            {
                if (!WalletAddressValidator.IsValid(trade.TraderWallet))
                {
                    continue;
                }

                var normalized = NormalizeTradeWallet(trade, wallet);
                if (knownLatestTradeTimestamp is not null &&
                    normalized.TimestampUtc.ToUniversalTime() <= knownLatestTradeTimestamp.Value)
                {
                    reachedExistingTrade = true;
                    break;
                }

                latestTradeTimestamp = Max(latestTradeTimestamp, normalized.TimestampUtc);
                newTradesObserved++;
            }

            if (reachedExistingTrade || page.Count < options.UserTradesLimit)
            {
                break;
            }
        }

        await repository.MarkPolymarketDataApiTraderSyncedAsync(
            wallet,
            fullSync: false,
            tradesFetched,
            newTradesObserved,
            latestTradeTimestamp,
            cancellationToken);

        return new DataApiTraderActivitySyncResult(tradesFetched, newTradesObserved, latestTradeTimestamp);
    }

    private static PolymarketDataApiTrader BuildTrader(
        PolymarketDataApiTrade trade,
        PolymarketDataApiTrader? existing)
    {
        var now = DateTimeOffset.UtcNow;
        var wallet = WalletAddressValidator.Normalize(trade.TraderWallet);
        return new PolymarketDataApiTrader(
            wallet,
            string.IsNullOrWhiteSpace(trade.TraderName) ? existing?.Name ?? string.Empty : trade.TraderName,
            string.IsNullOrWhiteSpace(trade.Pseudonym) ? existing?.Pseudonym : trade.Pseudonym,
            string.IsNullOrWhiteSpace(trade.Bio) ? existing?.Bio : trade.Bio,
            string.IsNullOrWhiteSpace(trade.ProfileImage) ? existing?.ProfileImage : trade.ProfileImage,
            string.IsNullOrWhiteSpace(trade.ProfileImageOptimized) ? existing?.ProfileImageOptimized : trade.ProfileImageOptimized,
            existing?.FirstSeenAtUtc ?? now,
            now,
            now,
            existing?.LastFullSyncAtUtc,
            existing?.LastIncrementalSyncAtUtc,
            Max(existing?.LastTradeTimestampUtc, trade.TimestampUtc),
            existing?.FullSyncCompleted ?? false,
            existing?.FullSyncTradesFetched ?? 0,
            existing?.FullSyncTradesInserted ?? 0,
            existing?.IncrementalSyncCount ?? 0,
            now);
    }

    private static PolymarketDataApiTrade NormalizeTradeWallet(PolymarketDataApiTrade trade, string wallet)
    {
        return trade with { TraderWallet = WalletAddressValidator.Normalize(wallet) };
    }

    private static PolymarketDataApiPosition NormalizePositionWallet(PolymarketDataApiPosition position, string wallet)
    {
        return position with { Wallet = WalletAddressValidator.Normalize(wallet) };
    }

    private static string BuildCategoryConditionKey(string conditionId, string assetId, bool closed)
    {
        return string.Concat(conditionId.Trim(), "|", assetId.Trim(), "|", closed ? "closed" : "open");
    }

    private static bool HasCategory(string? category)
    {
        return !string.IsNullOrWhiteSpace(category);
    }

    private static string? NormalizeCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? null : category.Trim();
    }

    private static string? TryParseGammaMarketEventId(string rawJson)
    {
        try
        {
            return PolymarketJsonParser.ParseGammaMarketEventId(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
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

    private async Task LogMissingPolymarketLeaderboardCategoryMappingsAsync(
        string wallet,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> missingCategories;
        try
        {
            missingCategories = await repository.GetMissingPolymarketLeaderboardCategoryMappingsAsync(
                wallet,
                limit: 100,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Polymarket leaderboard category mapping check failed for {Wallet}.", wallet);
            await TryRecordApiErrorAsync(
                "CheckPolymarketCategoryMapping",
                $"Wallet {wallet}: {ex.Message}",
                cancellationToken);
            return;
        }

        foreach (var category in missingCategories)
        {
            logger.LogWarning(
                "Polymarket leaderboard category mapping is missing. Wallet={Wallet} LocalCategory={LocalCategory}",
                wallet,
                category);
            await TryRecordApiErrorAsync(
                "MissingPolymarketCategoryMapping",
                $"Wallet {wallet}: local category {category}",
                cancellationToken);
        }
    }

    private sealed record PolymarketRatingFetchResult(
        IReadOnlyList<PolymarketDataApiWalletCategoryRating> Ratings,
        int CurrentPositionsFetched,
        int ClosedPositionsFetched);

    private sealed record PositionCategorySummary(
        int CurrentPositionsCount,
        decimal CurrentPositionsInitialValueUsd,
        decimal CurrentPositionsCurrentValueUsd,
        decimal CurrentPositionsCashPnlUsd,
        decimal CurrentPositionsRealizedPnlUsd,
        decimal CurrentPositionsTotalPnlUsd,
        decimal? CurrentPositionsPercentPnl,
        decimal? CurrentPositionsPercentRealizedPnl,
        int ClosedPositionsCount,
        decimal ClosedPositionsCostBasisUsd,
        decimal ClosedPositionsRealizedPnlUsd,
        decimal? ClosedPositionsPercentRealizedPnl,
        decimal PositionsTotalCostBasisUsd,
        decimal PositionsTotalPnlUsd,
        decimal? PositionsTotalPercentPnl,
        DateTimeOffset? PositionsRefreshedAtUtc)
    {
        public static PositionCategorySummary Empty(DateTimeOffset? refreshedAtUtc)
        {
            return new PositionCategorySummary(
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                null,
                null,
                0,
                0m,
                0m,
                null,
                0m,
                0m,
                null,
                refreshedAtUtc);
        }
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ComponentName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist Data API trader activity error.");
        }
    }
}
