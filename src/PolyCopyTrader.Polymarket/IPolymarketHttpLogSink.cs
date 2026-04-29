using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public interface IPolymarketHttpLogSink
{
    Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default);
}
