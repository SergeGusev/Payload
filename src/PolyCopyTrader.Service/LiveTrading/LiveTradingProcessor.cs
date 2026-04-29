using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.LiveTrading;

public sealed class LiveTradingProcessor(
    ILogger<LiveTradingProcessor> logger,
    LiveTradingOptions liveTradingOptions,
    RiskOptions riskOptions,
    IPolymarketTradingClient tradingClient,
    IAppRepository repository,
    ServiceControlState controlState) : ILiveTradingProcessor
{
    public async Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var openOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        if (openOrders.Count == 0)
        {
            return new LiveTradingProcessingResult(0, 0, 0);
        }

        var polled = 0;
        var canceled = 0;
        foreach (var order in openOrders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (ShouldCancel(order))
                {
                    var cancelResult = order.OrderId is null
                        ? await tradingClient.CancelAllOrdersAsync(cancellationToken)
                        : await tradingClient.CancelOrderAsync(order.OrderId, cancellationToken);
                    await UpdateAfterCancelAsync(order, cancelResult, cancellationToken);
                    canceled++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(order.OrderId))
                {
                    var status = await tradingClient.GetLiveOrderStatusAsync(order.OrderId, cancellationToken);
                    if (status is not null)
                    {
                        await repository.UpdateLiveOrderAsync(ApplyStatus(order, status), cancellationToken);
                        polled++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Live order processing failed for {LiveOrderId}.", order.Id);
                await repository.AddLiveTradingEventAsync(
                    new LiveTradingEvent(Guid.NewGuid(), "ProcessLiveOrder", "Error", ex.Message, DateTimeOffset.UtcNow),
                    cancellationToken);
            }
        }

        return new LiveTradingProcessingResult(openOrders.Count, polled, canceled);
    }

    public async Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default)
    {
        var openOrders = await repository.GetOpenLiveOrdersAsync(cancellationToken);
        var result = await tradingClient.CancelAllOrdersAsync(cancellationToken);
        foreach (var order in openOrders)
        {
            await UpdateAfterCancelAsync(order, result, cancellationToken);
        }

        await repository.AddLiveTradingEventAsync(
            new LiveTradingEvent(
                Guid.NewGuid(),
                "CancelAll",
                result.Success ? "OK" : "Error",
                $"{source}: canceled={result.CanceledOrderIds.Count}; notCanceled={result.NotCanceled.Count}; {result.ErrorMessage}",
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    private bool ShouldCancel(LiveOrder order)
    {
        var now = DateTimeOffset.UtcNow;
        if (controlState.KillSwitchActive || controlState.LiveTradingPaused)
        {
            return true;
        }

        return now >= order.ExpiresAtUtc ||
            now - order.CreatedAtUtc > TimeSpan.FromSeconds(Math.Min(riskOptions.MaxOrderAgeSeconds, liveTradingOptions.DefaultOrderTtlSeconds));
    }

    private async Task UpdateAfterCancelAsync(
        LiveOrder order,
        LiveOrderCancellationResult result,
        CancellationToken cancellationToken)
    {
        var orderId = order.OrderId ?? string.Empty;
        var canceled = string.IsNullOrWhiteSpace(orderId) ||
            result.CanceledOrderIds.Any(id => string.Equals(id, orderId, StringComparison.OrdinalIgnoreCase));
        var notCanceled = !string.IsNullOrWhiteSpace(orderId) && result.NotCanceled.TryGetValue(orderId, out var notCanceledReason)
            ? notCanceledReason
            : result.ErrorMessage;

        await repository.UpdateLiveOrderAsync(order with
        {
            Status = canceled ? LiveOrderStatus.Cancelled : LiveOrderStatus.CancelFailed,
            CancelStatus = canceled ? "cancelled" : notCanceled,
            RawResponseJson = string.IsNullOrWhiteSpace(result.RawResponseJson) ? order.RawResponseJson : result.RawResponseJson,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
    }

    private static LiveOrder ApplyStatus(LiveOrder order, LiveOrderStatusResult status)
    {
        var originalSize = FromTokenUnits(status.OriginalSize);
        var filledSize = FromTokenUnits(status.SizeMatched);
        var remaining = Math.Max(0m, originalSize - filledSize);
        return order with
        {
            Status = MapStatus(status.Status),
            ResponseStatus = status.Status,
            FilledSize = filledSize,
            RemainingSize = remaining,
            RawResponseJson = status.RawResponseJson,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static LiveOrderStatus MapStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "ORDER_STATUS_LIVE" or "LIVE" => LiveOrderStatus.Live,
            "ORDER_STATUS_MATCHED" or "MATCHED" => LiveOrderStatus.Matched,
            "ORDER_STATUS_CANCELED" or "ORDER_STATUS_CANCELED_MARKET_RESOLVED" or "CANCELLED" or "CANCELED" => LiveOrderStatus.Cancelled,
            "ORDER_STATUS_INVALID" or "INVALID" => LiveOrderStatus.Rejected,
            _ => LiveOrderStatus.Submitted
        };
    }

    private static decimal FromTokenUnits(string value)
    {
        return decimal.TryParse(value, out var units) ? units / 1_000_000m : 0m;
    }
}
