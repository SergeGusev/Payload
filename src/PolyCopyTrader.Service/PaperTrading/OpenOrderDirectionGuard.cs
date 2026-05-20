using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public static class OpenOrderDirectionGuard
{
    public static OppositeOutcomeOpenOrderBlock? FindOppositeOutcomeOpenOrder(
        string conditionId,
        string outcome,
        IEnumerable<PaperOrder> openPaperOrders,
        IEnumerable<LiveOrder> openLiveOrders)
    {
        if (string.IsNullOrWhiteSpace(conditionId) || string.IsNullOrWhiteSpace(outcome))
        {
            return null;
        }

        var paperBlock = openPaperOrders
            .Where(order => order.Side == TradeSide.Buy)
            .FirstOrDefault(order => IsOppositeOutcome(conditionId, outcome, order.ConditionId, order.Outcome));
        if (paperBlock is not null)
        {
            return new OppositeOutcomeOpenOrderBlock(
                "Paper",
                paperBlock.Id,
                paperBlock.StrategyId,
                paperBlock.ConditionId,
                paperBlock.Outcome);
        }

        var liveBlock = openLiveOrders
            .Where(order => order.Side == TradeSide.Buy)
            .FirstOrDefault(order => IsOppositeOutcome(conditionId, outcome, order.ConditionId, order.Outcome));
        if (liveBlock is not null)
        {
            return new OppositeOutcomeOpenOrderBlock(
                "Live",
                liveBlock.Id,
                liveBlock.StrategyId,
                liveBlock.ConditionId,
                liveBlock.Outcome);
        }

        return null;
    }

    public static string CreateValidationMessage(
        string candidateOutcome,
        OppositeOutcomeOpenOrderBlock block)
    {
        return
            "Opposite outcome open order exists. " +
            $"CandidateOutcome={candidateOutcome}; " +
            $"BlockingSource={block.Source}; " +
            $"BlockingOutcome={block.Outcome}; " +
            $"BlockingOrderId={block.OrderId}; " +
            $"BlockingStrategyId={block.StrategyId}.";
    }

    private static bool IsOppositeOutcome(
        string candidateConditionId,
        string candidateOutcome,
        string openConditionId,
        string openOutcome)
    {
        return string.Equals(openConditionId, candidateConditionId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(openOutcome, candidateOutcome, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record OppositeOutcomeOpenOrderBlock(
    string Source,
    Guid OrderId,
    Guid StrategyId,
    string ConditionId,
    string Outcome);
