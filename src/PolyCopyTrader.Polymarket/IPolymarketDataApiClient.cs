using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketDataApiClient
{
    Task<IReadOnlyList<TraderProfile>> GetTraderLeaderboardAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
        string wallet,
        bool takerOnly,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
        string wallet,
        CancellationToken cancellationToken = default);
}
