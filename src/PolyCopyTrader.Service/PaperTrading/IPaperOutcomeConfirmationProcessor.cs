using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperOutcomeConfirmationProcessor
{
    Task<PaperOrder?> ClaimAsync(CancellationToken cancellationToken);
    Task<PaperOutcomeConfirmation?> LookupAsync(PaperOrder order, PaperOutcomeConfirmationTrace trace,
        CancellationToken cancellationToken);
    Task<PaperOutcomeConfirmationResult> ApplyAsync(PaperOutcomeConfirmation confirmation,
        PaperOutcomeConfirmationTrace trace, CancellationToken cancellationToken);
    Task DeferAsync(Guid orderId, string reason, CancellationToken cancellationToken);
}
