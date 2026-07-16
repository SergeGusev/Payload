using System.Runtime.CompilerServices;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.LiveTrading;

public interface IPaperLiveShadowFillReconciler
{
    Task<PaperLiveShadowFillReconciliationResult> ReconcileAsync(
        Guid paperOrderId,
        Guid liveOrderId,
        CancellationToken cancellationToken = default);
}

public sealed class PaperLiveShadowFillReconciler(
    IAppRepository repository,
    IExposureSnapshotCache exposureCache) : IPaperLiveShadowFillReconciler
{
    private static readonly ConditionalWeakTable<IAppRepository, SemaphoreSlim> RepositoryLocks = new();
    private readonly SemaphoreSlim sync = RepositoryLocks.GetValue(repository, _ => new SemaphoreSlim(1, 1));

    public async Task<PaperLiveShadowFillReconciliationResult> ReconcileAsync(
        Guid paperOrderId,
        Guid liveOrderId,
        CancellationToken cancellationToken = default)
    {
        await sync.WaitAsync(cancellationToken);
        try
        {
            var result = await repository.ReconcilePaperLiveShadowFillAsync(
                new PaperLiveShadowFillReconciliationRequest(
                    paperOrderId,
                    liveOrderId,
                    DateTimeOffset.UtcNow),
                cancellationToken);
            exposureCache.ApplyPaperOrder(result.PaperOrder);
            exposureCache.ApplyPaperPosition(result.PaperPosition);
            return result;
        }
        finally
        {
            sync.Release();
        }
    }
}
