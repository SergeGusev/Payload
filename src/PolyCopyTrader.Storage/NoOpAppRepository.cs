using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed class NoOpAppRepository : IAppRepository
{
    public Task AddLeaderTradeAsync(LeaderTrade trade, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LeaderTrade>> GetRecentLeaderTradesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
    }

    public Task AddSignalAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddSignalRejectionAsync(SignalRejection rejection, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddPaperOrderAsync(PaperOrder order, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PaperOrder>> GetOpenPaperOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PaperOrder>>([]);
    }

    public Task AddApiErrorAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task UpsertServiceHeartbeatAsync(ServiceHeartbeat heartbeat, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServiceHeartbeat>> GetServiceHeartbeatsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ServiceHeartbeat>>([]);
    }
}
