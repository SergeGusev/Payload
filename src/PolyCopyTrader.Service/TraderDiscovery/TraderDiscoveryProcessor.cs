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
        var pnlLeaderboard = await FetchLeaderboardWindowAsync(OrderByPnl, cancellationToken);
        var volumeLeaderboard = await FetchLeaderboardWindowAsync(OrderByVolume, cancellationToken);
        if (pnlLeaderboard.Count == 0 && volumeLeaderboard.Count == 0)
        {
            logger.LogInformation("Trader discovery found no leaderboard entries.");
            return [];
        }

        await repository.AddTraderLeaderboardSnapshotsAsync(
            BuildMergedSnapshots(discoveryRunId, snapshotAt, pnlLeaderboard, volumeLeaderboard),
            cancellationToken);

        var best = pnlLeaderboard
            .Select(item => item.Entry)
            .Where(entry => entry.Pnl > 0m)
            .OrderByDescending(entry => entry.Pnl)
            .ThenByDescending(entry => entry.Volume)
            .Take(options.CandidatesPerSide)
            .ToArray();
        var worst = volumeLeaderboard
            .Select(item => item.Entry)
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

    private async Task<IReadOnlyList<LeaderboardWindowEntry>> FetchLeaderboardWindowAsync(
        string orderBy,
        CancellationToken cancellationToken)
    {
        var byWallet = new Dictionary<string, LeaderboardWindowEntry>(StringComparer.OrdinalIgnoreCase);
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
                byWallet[entry.Wallet] = new LeaderboardWindowEntry(entry, offset);
            }

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

    private IReadOnlyList<TraderLeaderboardSnapshot> BuildMergedSnapshots(
        Guid discoveryRunId,
        DateTimeOffset snapshotAt,
        IReadOnlyList<LeaderboardWindowEntry> pnlLeaderboard,
        IReadOnlyList<LeaderboardWindowEntry> volumeLeaderboard)
    {
        var byWallet = new Dictionary<string, (LeaderboardWindowEntry? Pnl, LeaderboardWindowEntry? Volume)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in pnlLeaderboard)
        {
            byWallet[item.Entry.Wallet] = (item, byWallet.GetValueOrDefault(item.Entry.Wallet).Volume);
        }

        foreach (var item in volumeLeaderboard)
        {
            byWallet[item.Entry.Wallet] = (byWallet.GetValueOrDefault(item.Entry.Wallet).Pnl, item);
        }

        return byWallet
            .Select(item => ToMergedSnapshot(discoveryRunId, snapshotAt, item.Value.Pnl, item.Value.Volume))
            .ToArray();
    }

    private TraderLeaderboardSnapshot ToMergedSnapshot(
        Guid discoveryRunId,
        DateTimeOffset snapshotAt,
        LeaderboardWindowEntry? pnl,
        LeaderboardWindowEntry? volume)
    {
        var entry = pnl?.Entry ?? volume?.Entry ?? throw new InvalidOperationException("Merged leaderboard snapshot must have at least one source.");
        var xUsername = FirstNonEmpty(pnl?.Entry.XUsername, volume?.Entry.XUsername);
        var userName = FirstNonEmpty(pnl?.Entry.UserName, volume?.Entry.UserName) ?? entry.Wallet;

        return new TraderLeaderboardSnapshot(
            Guid.NewGuid(),
            discoveryRunId,
            options.Category.ToUpperInvariant(),
            options.TimePeriod.ToUpperInvariant(),
            entry.Wallet,
            userName,
            xUsername,
            (pnl?.Entry.VerifiedBadge ?? false) || (volume?.Entry.VerifiedBadge ?? false),
            pnl?.Entry.Rank,
            pnl?.PageOffset,
            pnl?.Entry.Pnl,
            pnl?.Entry.Volume,
            pnl is null ? null : snapshotAt,
            volume?.Entry.Rank,
            volume?.PageOffset,
            volume?.Entry.Pnl,
            volume?.Entry.Volume,
            volume is null ? null : snapshotAt,
            snapshotAt);
    }

    private static bool IsUsableEntry(TraderLeaderboardEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Wallet) &&
            entry.Wallet.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed record LeaderboardWindowEntry(TraderLeaderboardEntry Entry, int PageOffset);
}
