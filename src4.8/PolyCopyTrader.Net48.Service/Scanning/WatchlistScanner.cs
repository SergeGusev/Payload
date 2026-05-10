using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Scanning;

public sealed class WatchlistScanner(
    ILogger<WatchlistScanner> logger,
    WatchlistOptions watchlistOptions,
    IPolymarketDataApiClient dataApiClient,
    IAppRepository repository,
    ILeaderTradeCandidateQueue candidateQueue) : IWatchlistScanner
{
    private const string ScannerName = "WatchlistScanner";
    private DateTimeOffset? lastSuccessfulScanUtc;
    private DateTimeOffset? lastErrorUtc;
    private string? lastErrorMessage;

    public async Task<ScannerStatusSnapshot> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        var enabledTraders = watchlistOptions.Traders.Where(trader => trader.Enabled).ToArray();
        if (enabledTraders.Length == 0)
        {
            return await PersistStatusAsync("Idle", 0, 0, 0, cancellationToken);
        }

        var tradesFetched = 0;
        var newTradesStored = 0;
        var positionsFetched = 0;
        var anySuccessfulTrader = false;
        var scanErrorMessage = default(string);

        foreach (var trader in enabledTraders)
        {
            if (!WalletAddressValidator.IsValid(trader.Wallet))
            {
                scanErrorMessage = $"Invalid wallet for watchlist trader '{trader.Name}': '{trader.Wallet}'.";
                logger.LogWarning("{ScannerError}", scanErrorMessage);
                continue;
            }

            var wallet = WalletAddressValidator.Normalize(trader.Wallet);
            try
            {
                var trades = await dataApiClient.GetUserTradesAsync(
                    wallet,
                    takerOnly: false,
                    limit: watchlistOptions.MaxTradesPerTraderPerPoll,
                    offset: 0,
                    cancellationToken);

                tradesFetched += trades.Count;
                foreach (var trade in trades.Take(watchlistOptions.MaxTradesPerTraderPerPoll))
                {
                    var normalizedTrade = NormalizeTrade(trade, trader, wallet);
                    if (await repository.TryAddLeaderTradeAsync(normalizedTrade, cancellationToken))
                    {
                        newTradesStored++;
                        await candidateQueue.EnqueueAsync(normalizedTrade, cancellationToken);
                    }
                }

                var positions = await dataApiClient.GetUserPositionsAsync(
                    wallet,
                    limit: watchlistOptions.MaxPositionsPerTraderPerPoll,
                    offset: 0,
                    cancellationToken);

                positionsFetched += positions.Count;
                foreach (var position in positions.Take(watchlistOptions.MaxPositionsPerTraderPerPoll))
                {
                    await repository.AddLeaderPositionAsync(NormalizePosition(position, wallet), cancellationToken);
                }

                anySuccessfulTrader = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                scanErrorMessage = $"Trader '{trader.Name}' scan failed: {ex.Message}";
                logger.LogError(ex, "Watchlist scan failed for trader {TraderName} ({Wallet}).", trader.Name, wallet);
                await TryRecordApiErrorAsync("ScanTrader", scanErrorMessage, cancellationToken);
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (anySuccessfulTrader)
        {
            lastSuccessfulScanUtc = now;
        }

        if (!string.IsNullOrWhiteSpace(scanErrorMessage))
        {
            lastErrorUtc = now;
            lastErrorMessage = scanErrorMessage;
        }

        var scannerStatus = scanErrorMessage is null
            ? "Healthy"
            : anySuccessfulTrader
                ? "Degraded"
                : "Warning";

        return await PersistStatusAsync(scannerStatus, tradesFetched, newTradesStored, positionsFetched, cancellationToken);
    }

    private Task<ScannerStatusSnapshot> PersistStatusAsync(
        string scannerStatus,
        int tradesFetched,
        int newTradesStored,
        int positionsFetched,
        CancellationToken cancellationToken)
    {
        var status = new ScannerStatusSnapshot(
            ScannerName,
            lastSuccessfulScanUtc,
            lastErrorUtc,
            lastErrorMessage,
            tradesFetched,
            newTradesStored,
            positionsFetched,
            scannerStatus,
            DateTimeOffset.UtcNow);

        return PersistAndReturnAsync(status, cancellationToken);
    }

    private async Task<ScannerStatusSnapshot> PersistAndReturnAsync(
        ScannerStatusSnapshot status,
        CancellationToken cancellationToken)
    {
        await repository.UpsertScannerStatusAsync(status, cancellationToken);
        return status;
    }

    private async Task TryRecordApiErrorAsync(
        string operation,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), ScannerName, operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist scanner API error for {Operation}.", operation);
        }
    }

    private static LeaderTrade NormalizeTrade(LeaderTrade trade, TraderRuleOptions trader, string wallet)
    {
        return trade with
        {
            TraderWallet = wallet,
            TraderName = string.IsNullOrWhiteSpace(trade.TraderName) ? trader.Name : trade.TraderName
        };
    }

    private static LeaderPosition NormalizePosition(LeaderPosition position, string wallet)
    {
        return position with
        {
            TraderWallet = wallet,
            SnapshotAtUtc = position.SnapshotAtUtc == new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero)
                ? DateTimeOffset.UtcNow
                : position.SnapshotAtUtc
        };
    }
}
