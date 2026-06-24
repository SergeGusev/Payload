using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.LiveTrading;

internal static class LiveGeoblockPreflight
{
    public const string FailurePrefix = "Geoblock check failed: ";

    public static async Task ValidateAsync(
        IPolymarketGeoClient geoClient,
        LiveTradingOptions liveTradingOptions,
        IAppRepository repository,
        List<string> validation,
        CancellationToken cancellationToken)
    {
        try
        {
            var geoblock = await geoClient.GetGeoblockStatusAsync(cancellationToken);
            if (geoblock.Blocked)
            {
                validation.Add($"Geoblock is active for VPS IP {geoblock.Ip ?? "unknown"}.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = FailurePrefix + ex.Message;
            if (liveTradingOptions.BlockOnGeoblockCheckFailure)
            {
                validation.Add(message);
                return;
            }

            await TryRecordWarningAsync(repository, message, cancellationToken);
        }
    }

    private static async Task TryRecordWarningAsync(
        IAppRepository repository,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddLiveTradingEventAsync(
                new LiveTradingEvent(
                    Guid.NewGuid(),
                    "GeoblockCheck",
                    "Warning",
                    message + " Live placement was allowed because LiveTrading.BlockOnGeoblockCheckFailure is false.",
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Best-effort telemetry must not turn an explicitly allowed geoblock endpoint failure back into a live blocker.
        }
    }
}
