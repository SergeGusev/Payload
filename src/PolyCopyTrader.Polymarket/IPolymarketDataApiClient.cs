using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketDataApiClient
{
    Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
        string category = "OVERALL",
        string timePeriod = "DAY",
        string orderBy = "PNL",
        int limit = 25,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
        string wallet,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
        string wallet,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);
}
