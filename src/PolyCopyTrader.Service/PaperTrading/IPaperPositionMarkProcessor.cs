namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperPositionMarkProcessor
{
    Task<int> RefreshPositionMarksAsync(CancellationToken cancellationToken = default);
}
