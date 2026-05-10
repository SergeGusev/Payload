using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface ISignalEngine
{
    SignalDecision Evaluate(SignalEvaluationContext context);
}
