using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface IRiskEngine
{
    RiskDecision Evaluate(
        PaperOrder proposedOrder,
        IReadOnlyList<PaperPosition> currentPositions);
}
