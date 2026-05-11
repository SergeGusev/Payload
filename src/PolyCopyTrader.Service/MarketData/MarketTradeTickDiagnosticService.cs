using System.Globalization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.MarketData;

public sealed class MarketTradeTickDiagnosticService(
    ILogger<MarketTradeTickDiagnosticService> logger,
    MarketTradeDiagnosticsOptions options,
    IAppRepository repository) : IMarketTradeTickDiagnosticService
{
    public async Task RecordAsync(MarketDataUpdate update, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled ||
            update.EventType != MarketDataEventType.LastTradePrice ||
            string.IsNullOrWhiteSpace(update.AssetId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var transactionHash = NormalizeNullable(update.TransactionHash);
        var tradeTick = new PolymarketWebSocketTradeTick(
            Guid.NewGuid(),
            BuildDedupKey(update),
            update.AssetId,
            NormalizeNullable(update.ConditionId),
            update.Side,
            update.Price,
            update.Size,
            update.TimestampUtc,
            transactionHash,
            !string.IsNullOrWhiteSpace(transactionHash),
            TradeTickTraderMatchStatus.NotFound,
            null,
            now,
            null,
            0,
            null,
            null,
            null,
            "recorded",
            update.RawJson,
            now);

        try
        {
            await repository.TryAddPolymarketWebSocketTradeTickAsync(tradeTick, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record market trade diagnostic tick.");
        }
    }

    private static string BuildDedupKey(MarketDataUpdate update)
    {
        var transactionHash = NormalizeKeyPart(update.TransactionHash);
        var prefix = string.IsNullOrWhiteSpace(transactionHash) ? "fallback" : $"tx:{transactionHash}";
        return string.Join(
            "|",
            prefix,
            $"condition:{NormalizeKeyPart(update.ConditionId)}",
            $"asset:{NormalizeKeyPart(update.AssetId)}",
            $"side:{update.Side.ToString().ToLowerInvariant()}",
            $"ts:{update.TimestampUtc.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}",
            $"price:{FormatDecimal(update.Price)}",
            $"size:{FormatDecimal(update.Size)}");
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.########", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string NormalizeKeyPart(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
