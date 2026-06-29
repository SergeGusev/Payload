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

    PaperPosition? GetPaperPosition(string copiedTraderWallet, string assetId);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    void ApplyPaperOrder(PaperOrder order);

    void ApplyPaperOrders(IReadOnlyCollection<PaperOrder> orders);

    void ApplyPaperPosition(PaperPosition position);

    void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions);

    void ApplyLiveOrder(LiveOrder order);
}
