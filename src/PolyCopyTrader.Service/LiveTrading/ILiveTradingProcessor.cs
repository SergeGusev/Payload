namespace PolyCopyTrader.Service.LiveTrading;

public interface ILiveTradingProcessor
{
    Task<LiveTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default);

    Task CancelAllOpenOrdersAsync(string source, CancellationToken cancellationToken = default);
}

public sealed record LiveTradingProcessingResult(
    int OpenOrdersChecked,
    int OrdersPolled,
    int OrdersCanceled);
