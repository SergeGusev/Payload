using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface IRiskEngine
{
    RiskDecision Evaluate(ProposedOrderIntent proposedOrder, ExposureSnapshot exposure);
}
