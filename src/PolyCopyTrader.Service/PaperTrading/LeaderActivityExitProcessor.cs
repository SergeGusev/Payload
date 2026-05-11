using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class LeaderActivityExitProcessor(
    ILogger<LeaderActivityExitProcessor> logger,
    PaperTradingOptions options,
    IPolymarketDataApiClient dataApiClient,
    IPaperTradingEngine paperTradingEngine,
    IExposureSnapshotCache exposureCache,
    IAppRepository repository) : ILeaderActivityExitProcessor
{
    private const string DecisionCode = "paper_leader_activity_partial_exit";
    private const int ExitScore = 100;
    private readonly SemaphoreSlim singleRun = new(1, 1);

    public async Task<LeaderActivityExitProcessingResult> ProcessOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!options.LeaderActivityExitTrackingEnabled)
        {
            return new LeaderActivityExitProcessingResult(0, 0, 0, 0, 0, 0, 0);
        }

        if (!await singleRun.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            logger.LogInformation("Leader activity exit tracking skipped because another run is active.");
            return new LeaderActivityExitProcessingResult(0, 0, 0, 0, 0, 0, 0);
        }

        try
        {
            return await ProcessOnceCoreAsync(cancellationToken);
        }
        finally
        {
            singleRun.Release();
        }
    }

    private async Task<LeaderActivityExitProcessingResult> ProcessOnceCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var duePositions = (await repository.GetPaperCopiedLeaderPositionsForExitTrackingAsync(
                options.LeaderActivityExitTrackingBatchSize,
                now,
                cancellationToken))
            .ToList();
        if (duePositions.Count == 0)
        {
            return new LeaderActivityExitProcessingResult(0, 0, 0, 0, 0, 0, 0);
        }

        var paperPositions = await repository.GetPaperPositionsAsync(cancellationToken);
        var openOrders = (await repository.GetOpenPaperOrdersAsync(cancellationToken)).ToList();
        var walletsChecked = 0;
        var activityRowsFetched = 0;
        var sellEventsMatched = 0;
        var exitOrdersCreated = 0;
        var duplicateEvents = 0;
        var errors = 0;

        foreach (var walletGroup in duePositions.GroupBy(position => position.CopiedTraderWallet, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wallet = walletGroup.Key;
            walletsChecked++;
            try
            {
                var walletPositions = walletGroup
                    .OrderBy(position => position.EntryTimestampUtc)
                    .ThenBy(position => position.Id)
                    .ToList();
                var activities = await dataApiClient.GetUserActivityAsync(
                    wallet,
                    options.LeaderActivityExitTrackingActivityLimit,
                    offset: 0,
                    sortBy: "TIMESTAMP",
                    sortDirection: "DESC",
                    timestampCacheBuster: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    cancellationToken);
                activityRowsFetched += activities.Count;

                var trackedAssets = walletPositions
                    .Select(position => position.AssetId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var firstEntryUtc = walletPositions.Min(position => position.EntryTimestampUtc);
                var sellActivities = activities
                    .Where(activity => activity.Type == PolymarketDataApiActivityType.Trade)
                    .Where(activity => activity.Side == TradeSide.Sell)
                    .Where(activity => trackedAssets.Contains(activity.AssetId))
                    .Where(activity => activity.TimestampUtc >= firstEntryUtc)
                    .OrderBy(activity => activity.TimestampUtc)
                    .ThenBy(activity => activity.TransactionHash, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var activity in sellActivities)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var matchingPositions = walletPositions
                        .Where(position => position.Status == PaperCopiedLeaderPositionStatus.Active)
                        .Where(position => string.Equals(position.AssetId, activity.AssetId, StringComparison.OrdinalIgnoreCase))
                        .Where(position => activity.TimestampUtc >= position.EntryTimestampUtc)
                        .Where(position => position.LeaderInitialSizeShares > position.LeaderSoldSizeShares)
                        .Where(position => position.CopiedInitialSizeShares > position.CopiedExitRequestedSizeShares)
                        .OrderBy(position => position.EntryTimestampUtc)
                        .ThenBy(position => position.Id)
                        .ToArray();
                    if (matchingPositions.Length == 0)
                    {
                        continue;
                    }

                    var availableToSell = GetAvailablePaperPositionSize(paperPositions, openOrders, wallet, activity.AssetId);
                    if (availableToSell <= 0m)
                    {
                        continue;
                    }

                    var plan = BuildExitPlan(matchingPositions, activity, availableToSell, DateTimeOffset.UtcNow);
                    if (plan.TotalCopiedExitSize <= 0m || plan.Updates.Count == 0)
                    {
                        continue;
                    }

                    var price = GetLeaderActivitySellPrice(activity);
                    if (price <= 0m)
                    {
                        continue;
                    }

                    var signal = CreateExitSignal(activity, plan.TotalCopiedExitSize, price, DateTimeOffset.UtcNow);
                    var order = paperTradingEngine.CreateOrder(
                        signal,
                        price,
                        plan.TotalCopiedExitSize,
                        signal.CreatedAtUtc.AddSeconds(options.DefaultOrderTtlSeconds));
                    var applied = await repository.ApplyPaperCopiedLeaderExitAsync(
                        CreateActivityEvent(activity, wallet, DateTimeOffset.UtcNow),
                        plan.Updates,
                        [signal],
                        [order],
                        cancellationToken);
                    if (!applied)
                    {
                        duplicateEvents++;
                        continue;
                    }

                    sellEventsMatched++;
                    exitOrdersCreated++;
                    openOrders.Add(order);
                    exposureCache.ApplyPaperOrder(order);
                    ApplyUpdatesInMemory(walletPositions, duePositions, plan.Updates);
                }

                var syncedAt = DateTimeOffset.UtcNow;
                await repository.MarkPaperCopiedLeaderPositionsActivitySyncedAsync(
                    wallet,
                    syncedAt,
                    syncedAt.AddMilliseconds(options.LeaderActivityExitTrackingPollDelayMilliseconds),
                    cancellationToken);

                if (options.LeaderActivityExitTrackingRequestDelayMilliseconds > 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(options.LeaderActivityExitTrackingRequestDelayMilliseconds),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors++;
                logger.LogError(ex, "Leader activity exit tracking failed for wallet {Wallet}.", wallet);
                await TryRecordApiErrorAsync("ProcessWallet", $"Wallet {wallet}: {ex.Message}", cancellationToken);
            }
        }

        return new LeaderActivityExitProcessingResult(
            duePositions.Count,
            walletsChecked,
            activityRowsFetched,
            sellEventsMatched,
            exitOrdersCreated,
            duplicateEvents,
            errors);
    }

    private static decimal GetLeaderActivitySellPrice(PolymarketDataApiActivity activity)
    {
        return activity.Price is > 0m and <= 1m ? activity.Price : 0m;
    }

    private static decimal GetAvailablePaperPositionSize(
        IReadOnlyList<PaperPosition> paperPositions,
        IReadOnlyList<PaperOrder> openOrders,
        string copiedTraderWallet,
        string assetId)
    {
        var currentSize = paperPositions
            .Where(position => string.Equals(position.CopiedTraderWallet, copiedTraderWallet, StringComparison.OrdinalIgnoreCase))
            .Where(position => string.Equals(position.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
            .Sum(position => position.SizeShares);
        var pendingSellSize = openOrders
            .Where(order => order.Side == TradeSide.Sell)
            .Where(order => string.Equals(order.CopiedTraderWallet, copiedTraderWallet, StringComparison.OrdinalIgnoreCase))
            .Where(order => string.Equals(order.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.SizeShares);

        return Math.Max(0m, currentSize - pendingSellSize);
    }

    private static ExitPlan BuildExitPlan(
        IReadOnlyList<PaperCopiedLeaderPosition> positions,
        PolymarketDataApiActivity activity,
        decimal availableToSell,
        DateTimeOffset now)
    {
        var remainingLeader = positions
            .Select(position => Math.Max(0m, position.LeaderInitialSizeShares - position.LeaderSoldSizeShares))
            .ToArray();
        var totalRemainingLeader = remainingLeader.Sum();
        if (totalRemainingLeader <= 0m || activity.Size <= 0m || availableToSell <= 0m)
        {
            return new ExitPlan(0m, []);
        }

        var leaderSellToAllocate = Math.Min(activity.Size, totalRemainingLeader);
        var desiredUpdates = new List<PlannedExitUpdate>();
        var desiredCopiedTotal = 0m;
        var remainingAllocation = leaderSellToAllocate;
        for (var index = 0; index < positions.Count; index++)
        {
            var position = positions[index];
            var leaderRemaining = remainingLeader[index];
            if (leaderRemaining <= 0m)
            {
                continue;
            }

            var leaderAllocation = index == positions.Count - 1
                ? remainingAllocation
                : leaderSellToAllocate * leaderRemaining / totalRemainingLeader;
            leaderAllocation = Math.Min(leaderRemaining, Math.Max(0m, leaderAllocation));
            remainingAllocation -= leaderAllocation;

            var copiedRemaining = Math.Max(0m, position.CopiedInitialSizeShares - position.CopiedExitRequestedSizeShares);
            var copiedIncrement = position.LeaderInitialSizeShares <= 0m
                ? 0m
                : leaderAllocation * position.CopiedInitialSizeShares / position.LeaderInitialSizeShares;
            copiedIncrement = Math.Min(copiedRemaining, Math.Max(0m, copiedIncrement));
            if (leaderAllocation <= 0m && copiedIncrement <= 0m)
            {
                continue;
            }

            desiredCopiedTotal += copiedIncrement;
            desiredUpdates.Add(new PlannedExitUpdate(position, leaderAllocation, copiedIncrement));
        }

        if (desiredCopiedTotal <= 0m)
        {
            return new ExitPlan(0m, []);
        }

        var scale = desiredCopiedTotal > availableToSell ? availableToSell / desiredCopiedTotal : 1m;
        var totalCopiedExit = 0m;
        var updates = new List<PaperCopiedLeaderPositionExitUpdate>();
        foreach (var update in desiredUpdates)
        {
            var leaderIncrement = update.LeaderSoldIncrement * scale;
            var copiedIncrement = update.CopiedExitIncrement * scale;
            var leaderSold = update.Position.LeaderSoldSizeShares + leaderIncrement;
            var copiedRequested = update.Position.CopiedExitRequestedSizeShares + copiedIncrement;
            totalCopiedExit += copiedIncrement;
            var closed =
                leaderSold >= update.Position.LeaderInitialSizeShares - 0.00000001m ||
                copiedRequested >= update.Position.CopiedInitialSizeShares - 0.00000001m;
            updates.Add(new PaperCopiedLeaderPositionExitUpdate(
                update.Position.Id,
                leaderSold,
                copiedRequested,
                closed ? PaperCopiedLeaderPositionStatus.Closed : PaperCopiedLeaderPositionStatus.Active,
                activity.TimestampUtc,
                activity.TransactionHash,
                now));
        }

        return new ExitPlan(totalCopiedExit, updates);
    }

    private static Signal CreateExitSignal(PolymarketDataApiActivity activity, decimal copiedExitSize, decimal price, DateTimeOffset now)
    {
        var leaderTrade = new LeaderTrade(
            activity.Wallet,
            string.IsNullOrWhiteSpace(activity.TraderName) ? activity.Wallet : activity.TraderName,
            activity.ConditionId,
            activity.AssetId,
            activity.MarketSlug,
            activity.MarketTitle,
            activity.Outcome,
            TradeSide.Sell,
            activity.Price,
            activity.Size,
            activity.UsdcSize > 0m ? activity.UsdcSize : activity.Price * activity.Size,
            activity.TimestampUtc,
            activity.TransactionHash);

        return new Signal(
            Guid.NewGuid(),
            leaderTrade,
            ExitScore,
            Accepted: true,
            DecisionCode,
            [],
            price,
            copiedExitSize,
            price * copiedExitSize,
            now);
    }

    private static PaperCopiedLeaderActivityEvent CreateActivityEvent(
        PolymarketDataApiActivity activity,
        string wallet,
        DateTimeOffset observedAtUtc)
    {
        return new PaperCopiedLeaderActivityEvent(
            Guid.NewGuid(),
            BuildDedupKey(activity),
            wallet,
            activity.AssetId,
            activity.ConditionId,
            activity.Side,
            activity.Price,
            activity.Size,
            activity.UsdcSize,
            activity.TransactionHash,
            activity.TimestampUtc,
            activity.RawJson,
            observedAtUtc);
    }

    private static string BuildDedupKey(PolymarketDataApiActivity activity)
    {
        return string.Join(
            "|",
            activity.Wallet.Trim().ToLowerInvariant(),
            activity.AssetId.Trim(),
            activity.ConditionId.Trim().ToLowerInvariant(),
            activity.TransactionHash?.Trim().ToLowerInvariant() ?? string.Empty,
            activity.TimestampUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            activity.Side.ToString(),
            activity.Size.ToString(CultureInfo.InvariantCulture),
            activity.Price.ToString(CultureInfo.InvariantCulture));
    }

    private static void ApplyUpdatesInMemory(
        List<PaperCopiedLeaderPosition> walletPositions,
        List<PaperCopiedLeaderPosition> allPositions,
        IReadOnlyList<PaperCopiedLeaderPositionExitUpdate> updates)
    {
        foreach (var update in updates)
        {
            var existing = walletPositions.FirstOrDefault(position => position.Id == update.PositionId);
            if (existing is null)
            {
                continue;
            }

            var updated = existing with
            {
                LeaderSoldSizeShares = update.LeaderSoldSizeShares,
                CopiedExitRequestedSizeShares = update.CopiedExitRequestedSizeShares,
                Status = update.Status,
                LastActivityTimestampUtc = update.LastActivityTimestampUtc,
                LastActivityTransactionHash = update.LastActivityTransactionHash,
                UpdatedAtUtc = update.UpdatedAtUtc
            };

            walletPositions.Remove(existing);
            allPositions.Remove(existing);
            walletPositions.Add(updated);
            allPositions.Add(updated);
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
                new ApiError(Guid.NewGuid(), "LeaderActivityExitProcessor", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist leader activity exit processor API error for {Operation}.", operation);
        }
    }

    private sealed record PlannedExitUpdate(
        PaperCopiedLeaderPosition Position,
        decimal LeaderSoldIncrement,
        decimal CopiedExitIncrement);

    private sealed record ExitPlan(
        decimal TotalCopiedExitSize,
        IReadOnlyList<PaperCopiedLeaderPositionExitUpdate> Updates);
}
