namespace PolyCopyTrader.Service.LiveTrading;

public sealed class DisabledLiveTradingProcessor : ILiveTradingProcessor
{
    public Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LiveTradingProcessingResult(0, 0, 0));
    }

    public Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
