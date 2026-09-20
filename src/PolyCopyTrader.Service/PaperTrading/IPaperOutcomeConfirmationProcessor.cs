using PolyCopyTrader.Service.Control;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperOutcomeConfirmationProcessor
{
    Task ProcessOneAsync(ServiceActivityState.BackgroundLease idle, CancellationToken cancellationToken = default);
}
