using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface IPaperTradingMarketDataUpdater
{
    Task ApplyMakerGtdUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset receivedAtUtc,
        IReadOnlySet<Guid> eligibleMakerGtdPaperOrderIds,
        CancellationToken cancellationToken = default,
        MarketDataSideEffectExecutionTrace? executionTrace = null);

    Task ApplyUpdateAsync(
        MarketDataUpdate update,
        DateTimeOffset? receivedAtUtc = null,
        IReadOnlySet<Guid>? eligiblePaperOrderIds = null,
        CancellationToken cancellationToken = default,
        MarketDataSideEffectExecutionTrace? executionTrace = null,
        bool persistPositionMarks = true);
}
