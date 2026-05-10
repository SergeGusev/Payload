using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketGeoClient
{
    Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default);
}
