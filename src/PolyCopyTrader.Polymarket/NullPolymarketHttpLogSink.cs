using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public sealed class NullPolymarketHttpLogSink : IPolymarketHttpLogSink
{
    public Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
