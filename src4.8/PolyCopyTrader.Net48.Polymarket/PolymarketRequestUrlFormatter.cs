namespace PolyCopyTrader.Polymarket;

internal static class PolymarketRequestUrlFormatter
{
    public static string Format(Uri requestUri)
    {
        var value = requestUri.IsAbsoluteUri ? requestUri.AbsoluteUri : requestUri.ToString();
        return string.IsNullOrEmpty(requestUri.Query) || value.IndexOf("?", StringComparison.Ordinal) >= 0
            ? value
            : value + requestUri.Query;
    }
}
