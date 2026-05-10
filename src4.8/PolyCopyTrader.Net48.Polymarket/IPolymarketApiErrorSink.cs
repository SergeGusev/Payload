using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketApiErrorSink
{
    Task RecordAsync(ApiError error, CancellationToken cancellationToken = default);
}
