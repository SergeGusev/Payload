using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Polymarket;

public sealed class RepositoryPolymarketHttpLogSink(
    IAppRepository repository,
    PolymarketHttpLoggingOptions options,
    ILogger<RepositoryPolymarketHttpLogSink> logger) : IPolymarketHttpLogSink
{
    private long successfulRequestCount;

    public async Task RecordAsync(PolymarketHttpLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!ShouldPersist(entry))
        {
            return;
        }

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

    private bool ShouldPersist(PolymarketHttpLogEntry entry)
    {
        if (!options.Enabled)
        {
            return false;
        }

        if (!entry.Succeeded)
        {
            return ShouldPersistFailure(entry);
        }

        if (options.PersistSuccessfulRequests)
        {
            return true;
        }

        if (options.SuccessfulRequestSampleRate <= 0)
        {
            return false;
        }

        return Interlocked.Increment(ref successfulRequestCount) % options.SuccessfulRequestSampleRate == 0;
    }

    private bool ShouldPersistFailure(PolymarketHttpLogEntry entry)
    {
        if (entry.StatusCode is null)
        {
            return options.PersistNetworkErrors;
        }

        if (entry.StatusCode == 404)
        {
            return options.PersistNotFound;
        }

        if (entry.StatusCode == 429)
        {
            return options.PersistRateLimitedRequests;
        }

        if (entry.StatusCode is 401 or 403)
        {
            return options.PersistAuthFailures;
        }

        if (entry.StatusCode >= 500)
        {
            return options.PersistServerErrors;
        }

        if (entry.StatusCode >= 400)
        {
            return options.PersistOtherClientErrors;
        }

        return options.PersistNetworkErrors;
    }
}
