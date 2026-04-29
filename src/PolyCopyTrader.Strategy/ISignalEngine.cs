using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface ISignalEngine
{
    Signal Evaluate(
        LeaderTrade leaderTrade,
        TraderRule traderRule,
        OrderBookSnapshot? orderBookSnapshot);
}
