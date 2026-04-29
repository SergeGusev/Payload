namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperTradingProcessor
{
    Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(CancellationToken cancellationToken = default);
}

public sealed record PaperTradingProcessingResult(
    int OpenOrdersChecked,
    int OrdersFilled,
    int OrdersExpired,
    int PositionsUpdated);
