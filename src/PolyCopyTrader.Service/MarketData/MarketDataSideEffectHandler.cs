using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed record MarketDataSideEffectWorkItem(
    string Component,
    MarketDataUpdate Update,
    ActiveMarketAssetSnapshot? ActiveMarketSnapshot,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset EnqueuedAtUtc,
    IReadOnlySet<Guid>? EligiblePaperOrderIds,
    bool Replaceable,
    ConfirmedAssetSubscriptionSnapshot? ConfirmedAssetSubscription = null);

public interface IMarketDataSideEffectHandler
{
    Task ProcessUpdateAsync(MarketDataSideEffectWorkItem workItem, CancellationToken cancellationToken = default);

    Task PersistFrameDiagnosticAsync(
        MarketWebSocketFrameDiagnostic diagnostic,
        CancellationToken cancellationToken = default);

    Task PersistApiErrorAsync(
        ApiError apiError,
        CancellationToken cancellationToken = default);
}

public sealed class MarketDataSideEffectHandler(
    MarketDataWebSocketOptions options,
    IMarketTradeTickDiagnosticService tradeTickDiagnosticService,
    IPaperTradingMarketDataUpdater paperTradingUpdater,
    IAppRepository repository,
    ICryptoUpDown5mMarketResolvedEventRecorder? cryptoUpDown5mMarketResolvedEventRecorder = null)
    : IMarketDataSideEffectHandler
{
    private readonly ICryptoUpDown5mMarketResolvedEventRecorder cryptoResolvedEventRecorder =
        cryptoUpDown5mMarketResolvedEventRecorder ?? NoOpCryptoUpDown5mMarketResolvedEventRecorder.Instance;

    public async Task ProcessUpdateAsync(
        MarketDataSideEffectWorkItem workItem,
        CancellationToken cancellationToken = default)
    {
        await RunPhaseAsync(
            "RecordResolvedEvent",
            () => cryptoResolvedEventRecorder.RecordAsync(
                workItem.Component,
                workItem.Update,
                workItem.ActiveMarketSnapshot,
                workItem.ReceivedAtUtc,
                cancellationToken));

        await RunPhaseAsync(
            "RecordTradeTick",
            () => tradeTickDiagnosticService.RecordAsync(workItem.Update, cancellationToken));

        if (options.PersistOrderBookSnapshots && workItem.Update.OrderBookSnapshot is not null)
        {
            await RunPhaseAsync(
                "PersistOrderBookSnapshot",
                () => repository.AddOrderBookSnapshotAsync(workItem.Update.OrderBookSnapshot, cancellationToken));
        }

        if (options.PersistMarketDataEvents)
        {
            await RunPhaseAsync(
                "PersistMarketDataEvent",
                () => repository.AddMarketDataEventAsync(ToMarketDataEvent(workItem.Update), cancellationToken));
        }

        await RunPhaseAsync(
            "ApplyPaperTradingUpdate",
            () => paperTradingUpdater.ApplyUpdateAsync(
                workItem.Update,
                workItem.ReceivedAtUtc,
                workItem.EligiblePaperOrderIds,
                workItem.ConfirmedAssetSubscription,
                cancellationToken));
    }

    public Task PersistFrameDiagnosticAsync(
        MarketWebSocketFrameDiagnostic diagnostic,
        CancellationToken cancellationToken = default)
    {
        return repository.AddMarketWebSocketFrameDiagnosticAsync(diagnostic, cancellationToken);
    }

    public Task PersistApiErrorAsync(ApiError apiError, CancellationToken cancellationToken = default)
    {
        return repository.AddApiErrorAsync(apiError, cancellationToken);
    }

    private static async Task RunPhaseAsync(string phase, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MarketDataSideEffectPhaseException(phase, ex);
        }
    }

    private static MarketDataEvent ToMarketDataEvent(MarketDataUpdate update)
    {
        var message = update.EventType switch
        {
            MarketDataEventType.Book => "Order book snapshot received.",
            MarketDataEventType.PriceChange => "Price change received.",
            MarketDataEventType.LastTradePrice => "Last trade price received.",
            MarketDataEventType.BestBidAsk => "Best bid/ask received.",
            MarketDataEventType.MarketResolved => "Market resolved event received.",
            MarketDataEventType.TickSizeChange => "Tick size change received.",
            _ => $"Market data event received: {update.RawEventType}."
        };

        return new MarketDataEvent(
            Guid.NewGuid(),
            update.EventType,
            update.AssetId,
            update.ConditionId,
            message,
            DateTimeOffset.UtcNow);
    }
}

public sealed class MarketDataSideEffectPhaseException(string phase, Exception innerException)
    : Exception($"Market-data side-effect phase '{phase}' failed: {innerException.Message}", innerException)
{
    public string Phase { get; } = phase;
}
