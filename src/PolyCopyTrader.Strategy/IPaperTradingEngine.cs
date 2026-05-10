using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface IPaperTradingEngine
{
    PaperOrder CreateOrder(Signal signal, decimal price, decimal sizeShares, DateTimeOffset expiresAtUtc);

    PaperOrder ExpireIfNeeded(PaperOrder order, DateTimeOffset nowUtc);

    PaperFill? TrySimulateFill(
        PaperOrder order,
        OrderBookSnapshot? orderBookSnapshot,
        LeaderTrade? observedTrade,
        DateTimeOffset nowUtc,
        decimal previouslyFilledShares = 0m);

    PaperOrder ApplyFillStatus(PaperOrder order, PaperFill fill, decimal previouslyFilledShares = 0m);

    PaperPosition ApplyBuyFill(PaperPosition? currentPosition, PaperOrder order, PaperFill fill, decimal currentBid, DateTimeOffset nowUtc);

    PaperPosition ApplySellFill(PaperPosition currentPosition, PaperOrder order, PaperFill fill, decimal currentBid, DateTimeOffset nowUtc);
}
