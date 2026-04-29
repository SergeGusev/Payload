namespace PolyCopyTrader.Polymarket;

internal static class PolymarketRequestUrlFormatter
{
    public static string Format(Uri requestUri)
    {
        var value = requestUri.IsAbsoluteUri ? requestUri.AbsoluteUri : requestUri.ToString();
        return string.IsNullOrEmpty(requestUri.Query) || value.Contains('?', StringComparison.Ordinal)
            ? value
            : value + requestUri.Query;
    }
}
