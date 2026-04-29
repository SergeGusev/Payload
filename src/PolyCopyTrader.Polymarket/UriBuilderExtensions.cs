namespace PolyCopyTrader.Polymarket;

internal static class UriBuilderExtensions
{
    public static Uri WithPathAndQuery(
        string baseUrl,
        string path,
        IReadOnlyDictionary<string, string?> query)
    {
        var builder = new UriBuilder(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        var parameters = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}");

        builder.Query = string.Join("&", parameters);
        return builder.Uri;
    }
}
