using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed record TradingExposureSnapshot(
    IReadOnlyList<PaperOrder> OpenPaperOrders,
    IReadOnlyList<PaperPosition> PaperPositions,
    IReadOnlyList<LiveOrder> OpenLiveOrders,
    DateTimeOffset LoadedAtUtc);

public interface IExposureSnapshotCache
{
    Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    void ApplyPaperOrder(PaperOrder order);

    void ApplyPaperPosition(PaperPosition position);

    void ApplyLiveOrder(LiveOrder order);
}
