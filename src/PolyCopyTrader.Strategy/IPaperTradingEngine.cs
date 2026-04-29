using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface IPaperTradingEngine
{
    PaperOrder CreateOrder(Signal signal, decimal price, decimal sizeShares, DateTimeOffset expiresAtUtc);

    PaperOrder ExpireIfNeeded(PaperOrder order, DateTimeOffset nowUtc);

    PaperFill? TrySimulateFill(PaperOrder order, OrderBookSnapshot? orderBookSnapshot, LeaderTrade? observedTrade, DateTimeOffset nowUtc);

    PaperOrder ApplyFillStatus(PaperOrder order, PaperFill fill);

    PaperPosition ApplyBuyFill(PaperPosition? currentPosition, PaperOrder order, PaperFill fill, decimal currentBid, DateTimeOffset nowUtc);
}
