using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IAppRepository
{
    Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(CancellationToken cancellationToken = default);

    Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default);

    Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default);
}
