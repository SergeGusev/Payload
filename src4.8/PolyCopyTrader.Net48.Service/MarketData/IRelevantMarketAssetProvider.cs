namespace PolyCopyTrader.Service.MarketData;

public interface IRelevantMarketAssetProvider
{
    Task<IReadOnlyCollection<string>> GetRelevantAssetIdsAsync(CancellationToken cancellationToken = default);
}
