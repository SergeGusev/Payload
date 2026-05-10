namespace PolyCopyTrader.Service.Signals;

public interface ISignalProcessor
{
    Task<SignalProcessingResult> ProcessQueuedAsync(CancellationToken cancellationToken = default);
}

public sealed record SignalProcessingResult(
    int CandidatesProcessed,
    int SignalsAccepted,
    int SignalsRejected,
    int PaperOrdersCreated,
    int LiveOrdersSubmitted = 0);
