using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Strategy;

public interface IPaperTradingEngine
{
    PaperOrder CreateOrder(Signal signal, decimal price, decimal sizeShares, DateTimeOffset expiresAtUtc);
}
