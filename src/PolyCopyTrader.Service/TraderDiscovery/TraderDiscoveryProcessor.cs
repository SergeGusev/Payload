using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.TraderDiscovery;

public sealed class TraderDiscoveryProcessor(
    ILogger<TraderDiscoveryProcessor> logger,
    TraderDiscoveryOptions options,
    IPolymarketDataApiClient dataApiClient,
    IAppRepository repository) : ITraderDiscoveryProcessor
{
    private const string BestPnl = "BestPnl";
    private const string WorstPnl = "WorstPnl";
    private const string OrderByPnl = "PNL";
    private const string OrderByVolume = "VOL";

    public async Task<IReadOnlyList<TraderDiscoveryCandidate>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var discoveryRunId = Guid.NewGuid();
        var snapshotAt = DateTimeOffset.UtcNow;
        var pnlLeaderboard = await FetchLeaderboardWindowAsync(OrderByPnl, discoveryRunId, snapshotAt, cancellationToken);
        var volumeLeaderboard = await FetchLeaderboardWindowAsync(OrderByVolume, discoveryRunId, snapshotAt, cancellationToken);
        if (pnlLeaderboard.Count == 0 && volumeLeaderboard.Count == 0)
        {
            logger.LogInformation("Trader discovery found no leaderboard entries.");
            return [];
        }

        var best = pnlLeaderboard
            .Where(entry => entry.Pnl > 0m)
            .OrderByDescending(entry => entry.Pnl)
            .ThenByDescending(entry => entry.Volume)
            .Take(options.CandidatesPerSide)
            .ToArray();
        var worst = volumeLeaderboard
            .Where(entry => entry.Pnl < 0m)
            .OrderBy(entry => entry.Pnl)
            .ThenByDescending(entry => entry.Volume)
            .Take(options.CandidatesPerSide)
            .ToArray();

        var candidates = new List<TraderDiscoveryCandidate>();
        foreach (var entry in best)
        {
            candidates.Add(await BuildCandidateAsync(BestPnl, entry, cancellationToken));
        }

        foreach (var entry in worst)
        {
            candidates.Add(await BuildCandidateAsync(WorstPnl, entry, cancellationToken));
        }

        await repository.UpsertTraderDiscoveryCandidatesAsync(candidates, cancellationToken);
        logger.LogInformation(
            "Trader discovery refreshed. Category={Category} TimePeriod={TimePeriod} RunId={RunId} PnlEntries={PnlEntries} VolumeEntries={VolumeEntries} Candidates={Candidates}",
            options.Category,
            options.TimePeriod,
            discoveryRunId,
            pnlLeaderboard.Count,
            volumeLeaderboard.Count,
            candidates.Count);

        return candidates;
    }

    private async Task<IReadOnlyList<TraderLeaderboardEntry>> FetchLeaderboardWindowAsync(
        string orderBy,
        Guid discoveryRunId,
        DateTimeOffset snapshotAt,
        CancellationToken cancellationToken)
    {
        var byWallet = new Dictionary<string, TraderLeaderboardEntry>(StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < options.LeaderboardPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = page * 50;
            if (offset > 1000)
            {
                break;
            }

            var entries = await dataApiClient.GetTraderLeaderboardAsync(
                options.Category,
                options.TimePeriod,
                orderBy,
                50,
                offset,
                cancellationToken: cancellationToken);

            foreach (var entry in entries.Where(IsUsableEntry))
            {
                byWallet[entry.Wallet] = entry;
            }

            await repository.AddTraderLeaderboardSnapshotsAsync(
                entries
                    .Where(IsUsableEntry)
                    .Select(entry => ToSnapshot(discoveryRunId, snapshotAt, orderBy, offset, entry))
                    .ToArray(),
                cancellationToken);

            if (entries.Count < 50)
            {
                break;
            }

            await DelayIfConfiguredAsync(cancellationToken);
        }

        return byWallet.Values.ToArray();
    }

    private async Task<TraderDiscoveryCandidate> BuildCandidateAsync(
        string discoveryType,
        TraderLeaderboardEntry entry,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LeaderTrade> trades = [];
        IReadOnlyList<LeaderPosition> positions = [];
        var notes = new List<string>();
        var allTimeEntry = await FetchAllTimeEntryAsync(entry, notes, cancellationToken);

        try
        {
            trades = await dataApiClient.GetUserTradesAsync(
                entry.Wallet,
                takerOnly: false,
                limit: options.TradesPerCandidate,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add("trades_fetch_failed");
            logger.LogWarning(ex, "Trader discovery failed to fetch trades for {Wallet}.", entry.Wallet);
        }

        await DelayIfConfiguredAsync(cancellationToken);

        try
        {
            positions = await dataApiClient.GetUserPositionsAsync(
                entry.Wallet,
                limit: options.PositionsPerCandidate,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add("positions_fetch_failed");
            logger.LogWarning(ex, "Trader discovery failed to fetch positions for {Wallet}.", entry.Wallet);
        }

        await DelayIfConfiguredAsync(cancellationToken);

        if (trades.Count < 10)
        {
            notes.Add("low_trade_sample");
        }

        if (entry.Volume <= 0m)
        {
            notes.Add("zero_leaderboard_volume");
        }

        if (positions.Count == 0)
        {
            notes.Add("no_open_positions");
        }

        if (string.Equals(discoveryType, WorstPnl, StringComparison.Ordinal))
        {
            notes.Add("loss_selected_from_volume_leaderboard");
        }

        var recentTradeVolume = trades.Sum(trade => trade.CashValueUsd);
        var buyTrades = trades.Count(trade => trade.Side == TradeSide.Buy);
        var sellTrades = trades.Count(trade => trade.Side == TradeSide.Sell);
        var lastTradeUtc = trades
            .Where(trade => trade.TimestampUtc > DateTimeOffset.UnixEpoch)
            .Select(trade => (DateTimeOffset?)trade.TimestampUtc)
            .Max();

        return new TraderDiscoveryCandidate(
            Guid.NewGuid(),
            discoveryType,
            options.Category.ToUpperInvariant(),
            options.TimePeriod.ToUpperInvariant(),
            entry.Rank,
            entry.Wallet,
            entry.UserName,
            entry.XUsername,
            entry.Pnl,
            entry.Volume,
            allTimeEntry?.Pnl,
            allTimeEntry?.Volume,
            entry.VerifiedBadge,
            trades.Count,
            buyTrades,
            sellTrades,
            recentTradeVolume,
            trades.Count == 0 ? 0m : recentTradeVolume / trades.Count,
            lastTradeUtc,
            positions.Count,
            positions.Sum(position => position.CurrentValue),
            positions.Sum(position => position.CashPnl),
            positions.Sum(position => position.RealizedPnl),
            string.Join(", ", notes),
            DateTimeOffset.UtcNow);
    }

    private async Task<TraderLeaderboardEntry?> FetchAllTimeEntryAsync(
        TraderLeaderboardEntry entry,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (string.Equals(options.TimePeriod, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return entry;
        }

        try
        {
            var entries = await dataApiClient.GetTraderLeaderboardAsync(
                options.Category,
                "ALL",
                OrderByPnl,
                1,
                0,
                entry.Wallet,
                cancellationToken);

            await DelayIfConfiguredAsync(cancellationToken);
            var allTime = entries.FirstOrDefault();
            if (allTime is null)
            {
                notes.Add("all_time_not_found");
            }

            return allTime;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            notes.Add("all_time_fetch_failed");
            logger.LogWarning(ex, "Trader discovery failed to fetch all-time leaderboard stats for {Wallet}.", entry.Wallet);
            return null;
        }
    }

    private async Task DelayIfConfiguredAsync(CancellationToken cancellationToken)
    {
        if (options.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds), cancellationToken);
        }
    }

    private TraderLeaderboardSnapshot ToSnapshot(
        Guid discoveryRunId,
        DateTimeOffset snapshotAt,
        string orderBy,
        int pageOffset,
        TraderLeaderboardEntry entry)
    {
        return new TraderLeaderboardSnapshot(
            Guid.NewGuid(),
            discoveryRunId,
            options.Category.ToUpperInvariant(),
            options.TimePeriod.ToUpperInvariant(),
            orderBy.ToUpperInvariant(),
            pageOffset,
            entry.Rank,
            entry.Wallet,
            entry.UserName,
            entry.XUsername,
            entry.Pnl,
            entry.Volume,
            entry.VerifiedBadge,
            snapshotAt);
    }

    private static bool IsUsableEntry(TraderLeaderboardEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Wallet) &&
            entry.Wallet.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
    }
}
