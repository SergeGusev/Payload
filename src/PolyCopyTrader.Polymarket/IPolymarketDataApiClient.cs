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
        string? user = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
        string wallet,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketDataApiTrade>> GetUserDataApiTradesAsync(
        string wallet,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Typed Data API trades are not implemented by this client.");
    }

    Task<IReadOnlyList<PolymarketDataApiTrade>> GetGlobalDataApiTradesAsync(
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Global Data API trades are not implemented by this client.");
    }

    Task<IReadOnlyList<PolymarketDataApiActivity>> GetUserActivityAsync(
        string wallet,
        int limit = 500,
        int offset = 0,
        string sortBy = "TIMESTAMP",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Typed Data API activity is not implemented by this client.");
    }

    Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
        string conditionId,
        bool takerOnly,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
        string wallet,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserCurrentPositionsAsync(
        string wallet,
        int limit = 500,
        int offset = 0,
        string sortBy = "CURRENT",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Typed Data API current positions are not implemented by this client.");
    }

    Task<IReadOnlyList<PolymarketDataApiPosition>> GetUserClosedPositionsAsync(
        string wallet,
        int limit = 50,
        int offset = 0,
        string sortBy = "TIMESTAMP",
        string sortDirection = "DESC",
        long? timestampCacheBuster = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Typed Data API closed positions are not implemented by this client.");
    }
}

public interface IPolymarketGammaClient
{
    Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
        int limit = 500,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<PolymarketGammaMarket?> GetClosedMarketBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PolymarketGammaMarket?>(null);
    }

    Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
        string tokenId,
        bool closed,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
        string conditionId,
        string requestedTokenId,
        bool closed,
        CancellationToken cancellationToken = default);

    Task<string?> GetEventCategoryAsync(
        string eventId,
        CancellationToken cancellationToken = default);
}
