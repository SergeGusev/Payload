namespace PolyCopyTrader.Storage;

public interface IDashboardProjectionRepository
{
    Task<DashboardProjectionControlState> GetControlStateAsync(
        CancellationToken cancellationToken = default);

    Task<DashboardProjectionBootstrapResult> BootstrapAsync(
        CancellationToken cancellationToken = default);

    Task<DashboardProjectionBatchResult> ApplyPendingEventsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<DashboardProjectionExpiryResult> ExpireRecentFactsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<DashboardProjectionReconciliationResult> ReconcileNextStrategyAsync(
        CancellationToken cancellationToken = default);

    Task RecordFailureAsync(
        string operation,
        string error,
        CancellationToken cancellationToken = default);
}
