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
    MarketDataSideEffectExecutionTrace? ExecutionTrace = null)
{
    public bool PersistPositionMarks { get; init; } = true;
}

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
            MarketDataSideEffectPhases.RecordResolvedEvent,
            "CryptoResolvedEventRecorder.Record",
            workItem.ExecutionTrace,
            () => cryptoResolvedEventRecorder.RecordAsync(
                workItem.Component,
                workItem.Update,
                workItem.ActiveMarketSnapshot,
                workItem.ReceivedAtUtc,
                cancellationToken));

        await RunPhaseAsync(
            MarketDataSideEffectPhases.RecordTradeTick,
            "MarketTradeTickDiagnosticService.Record",
            workItem.ExecutionTrace,
            () => tradeTickDiagnosticService.RecordAsync(workItem.Update, cancellationToken));

        if (options.PersistOrderBookSnapshots && workItem.Update.OrderBookSnapshot is not null)
        {
            await RunPhaseAsync(
                MarketDataSideEffectPhases.PersistOrderBookSnapshot,
                "IAppRepository.AddOrderBookSnapshot",
                workItem.ExecutionTrace,
                () => repository.AddOrderBookSnapshotAsync(workItem.Update.OrderBookSnapshot, cancellationToken));
        }

        if (options.PersistMarketDataEvents)
        {
            await RunPhaseAsync(
                MarketDataSideEffectPhases.PersistMarketDataEvent,
                "IAppRepository.AddMarketDataEvent",
                workItem.ExecutionTrace,
                () => repository.AddMarketDataEventAsync(ToMarketDataEvent(workItem.Update), cancellationToken));
        }

        await RunPhaseAsync(
            MarketDataSideEffectPhases.ApplyPaperTradingUpdate,
            "PaperTradingMarketDataUpdater.ApplyUpdate",
            workItem.ExecutionTrace,
            () => paperTradingUpdater.ApplyUpdateAsync(
                workItem.Update,
                workItem.ReceivedAtUtc,
                workItem.EligiblePaperOrderIds,
                cancellationToken,
                workItem.ExecutionTrace,
                workItem.PersistPositionMarks));
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

    private static async Task RunPhaseAsync(
        string phase,
        string operation,
        MarketDataSideEffectExecutionTrace? executionTrace,
        Func<Task> action)
    {
        executionTrace?.EnterPhase(phase, operation, DateTimeOffset.UtcNow);
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
