using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperOutcomeConfirmationProcessor
{
    Task<PaperConfirmationProgress?> GetProgressAsync(CancellationToken cancellationToken)
        => Task.FromResult<PaperConfirmationProgress?>(null);
    Task<PaperOrder?> ClaimAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PaperOrder>> ClaimBatchAsync(PaperConfirmationLane lane, int limit,
        CancellationToken cancellationToken);
    Task<PaperOutcomeConfirmationResult> ApplyGroupAsync(IReadOnlyList<PaperOutcomeConfirmation> confirmations,
        PaperOutcomeConfirmationTrace trace, CancellationToken cancellationToken);
    Task<PaperOutcomeConfirmation?> LookupAsync(PaperOrder order, PaperOutcomeConfirmationTrace trace,
        CancellationToken cancellationToken);
    Task<PaperOutcomeConfirmationResult> ApplyAsync(PaperOutcomeConfirmation confirmation,
        PaperOutcomeConfirmationTrace trace, CancellationToken cancellationToken);
    Task DeferAsync(Guid orderId, string reason, CancellationToken cancellationToken);
}
