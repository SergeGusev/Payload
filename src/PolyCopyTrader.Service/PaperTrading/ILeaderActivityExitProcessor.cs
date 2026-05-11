namespace PolyCopyTrader.Service.PaperTrading;

public interface ILeaderActivityExitProcessor
{
    Task<LeaderActivityExitProcessingResult> ProcessOnceAsync(CancellationToken cancellationToken = default);
}

public sealed record LeaderActivityExitProcessingResult(
    int PositionsSelected,
    int WalletsChecked,
    int ActivityRowsFetched,
    int SellEventsMatched,
    int ExitOrdersCreated,
    int DuplicateEvents,
    int Errors);
