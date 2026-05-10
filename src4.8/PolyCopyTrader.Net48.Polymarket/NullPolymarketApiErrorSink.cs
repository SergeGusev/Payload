using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Polymarket;

public sealed class NullPolymarketApiErrorSink : IPolymarketApiErrorSink
{
    public Task RecordAsync(ApiError error, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
