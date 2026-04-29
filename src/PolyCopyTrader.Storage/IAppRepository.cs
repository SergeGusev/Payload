using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IAppRepository
{
    Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<bool> TryAddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(CancellationToken cancellationToken = default);

    Task AddLeaderPositionAsync(LeaderPosition position, CancellationToken cancellationToken = default);

    Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default);

    Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default);

    Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task UpdatePaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default);

    Task AddPaperFillAsync(PaperFill fill, CancellationToken cancellationToken = default);

    Task UpsertPaperPositionAsync(PaperPosition position, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperPosition>> GetPaperPositionsAsync(CancellationToken cancellationToken = default);

    Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default);

    Task UpsertScannerStatusAsync(ScannerStatusSnapshot status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScannerStatusSnapshot>> GetScannerStatusesAsync(CancellationToken cancellationToken = default);

    Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default);
}
