using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Polymarket;

public sealed class RepositoryPolymarketHttpLogSink(
    IAppRepository repository,
    ILogger<RepositoryPolymarketHttpLogSink> logger) : IPolymarketHttpLogSink
{
    public async Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.AddPolymarketHttpLogAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to record Polymarket HTTP log for {Component}.{Operation}.",
                entry.Component,
                entry.Operation);
        }
    }
}
