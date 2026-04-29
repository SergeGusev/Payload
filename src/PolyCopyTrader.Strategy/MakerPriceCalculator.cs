namespace PolyCopyTrader.Strategy;

public static class MakerPriceCalculator
{
    public static decimal Calculate(decimal bestBid, decimal bestAsk, decimal tickSize, decimal maxEntry)
    {
        return Math.Min(Math.Min(bestBid + tickSize, maxEntry), bestAsk - tickSize);
    }
}
