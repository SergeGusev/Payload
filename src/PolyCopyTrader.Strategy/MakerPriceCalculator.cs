namespace PolyCopyTrader.Strategy;

public static class MakerPriceCalculator
{
    public static decimal Calculate(decimal bestBid, decimal bestAsk, decimal tickSize, decimal maxEntry)
    {
        return Math.Min(Math.Min(bestBid + tickSize, maxEntry), bestAsk - tickSize);
    }

    public static decimal CalculateSell(decimal bestBid, decimal bestAsk, decimal tickSize, decimal minExit)
    {
        return Math.Max(Math.Max(bestAsk - tickSize, minExit), bestBid + tickSize);
    }
}
