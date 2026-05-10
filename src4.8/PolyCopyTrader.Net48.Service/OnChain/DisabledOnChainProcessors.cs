using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.OnChain;

public sealed class DisabledOnChainIngestionProcessor : IOnChainIngestionProcessor
{
    public Task<OnChainIngestionResult> RefreshLookbackAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new OnChainIngestionResult(now, now, 0, 0, 0, 0, 0));
    }

    public Task<OnChainIngestionResult> RefreshBackgroundCycleAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new OnChainIngestionResult(now, now, 0, 0, 0, 0, 0));
    }

    public bool RequestCancel()
    {
        return false;
    }
}

public sealed class DisabledOnChainMarketEnrichmentProcessor : IOnChainMarketEnrichmentProcessor
{
    public Task<OnChainMarketEnrichmentResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OnChainMarketEnrichmentResult(0, 0, 0, 0, 0, false));
    }
}
