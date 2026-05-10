namespace PolyCopyTrader.Domain;

public static class AnalyticsMath
{
    public static decimal Percentage(int numerator, int denominator)
    {
        return denominator <= 0
            ? 0m
            : decimal.Round((decimal)numerator / denominator * 100m, 4);
    }

    public static decimal? Difference(decimal? value, decimal? baseline)
    {
        return value is { } current && baseline is { } start
            ? current - start
            : null;
    }
}
