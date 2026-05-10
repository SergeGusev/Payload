using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.DataApiTraderActivity;

public interface IDataApiTraderActivityIngestionProcessor
{
    Task<DataApiTraderActivityIngestionResult> RefreshAsync(CancellationToken cancellationToken = default);

    Task<DataApiTraderActivityIngestionResult> RefreshTraderSyncBatchAsync(CancellationToken cancellationToken = default);

    Task<DataApiTraderRatingRefreshResult> RefreshPolymarketRatingBatchAsync(CancellationToken cancellationToken = default);
}

public sealed record DataApiTraderActivityIngestionResult(
    int GlobalTradesFetched,
    int UsableGlobalTrades,
    int UniqueTraders,
    int TradersUpserted,
    int NewTraders,
    int ExistingTraders,
    int FullSyncs,
    int IncrementalSyncs,
    int TraderSyncFailures,
    int UserTradesFetched,
    int UserTradesAdvanced,
    int GlobalTradesInserted,
    int PositionRefreshes,
    int PositionRefreshFailures,
    int CurrentPositionsFetched,
    int ClosedPositionsFetched,
    int PositionsUpserted,
    int WalletPerformanceRowsUpserted,
    int CategoryPerformanceRowsUpserted);

public sealed record DataApiTraderActivitySyncResult(
    int TradesFetched,
    int NewTradesObserved,
    DateTimeOffset? LatestTradeTimestampUtc);

public sealed record DataApiTraderRatingRefreshResult(
    int TradersSelected,
    int MappingsLoaded,
    int WalletRefreshes,
    int WalletFailures,
    int RatingRowsUpserted,
    int CurrentPositionsFetched = 0,
    int ClosedPositionsFetched = 0);
