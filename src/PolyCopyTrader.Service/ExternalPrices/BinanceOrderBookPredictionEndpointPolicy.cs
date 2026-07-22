namespace PolyCopyTrader.Service.ExternalPrices;

public static class BinanceOrderBookPredictionEndpointPolicy
{
    private static readonly HashSet<string> JsonStreams = new(StringComparer.Ordinal)
    {
        "btcusdt@trade",
        "btcusdt@bookTicker"
    };

    private static readonly HashSet<string> SbeStreams = new(StringComparer.Ordinal)
    {
        "btcusdt@trade",
        "btcusdt@bestBidAsk"
    };

    public static Uri Validate(
        BinanceOrderBookPredictionSource source,
        string streamUrl)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Binance stream URL must be an absolute wss URL.", nameof(streamUrl));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.AbsolutePath, "/stream", StringComparison.Ordinal))
        {
            throw new ArgumentException("Binance stream URL must not contain user info or a fragment and must use /stream.", nameof(streamUrl));
        }

        string expectedHost;
        int expectedPort;
        HashSet<string> expectedStreams;
        if (source == BinanceOrderBookPredictionSource.Sbe)
        {
            expectedHost = "stream-sbe.binance.com";
            expectedPort = 9443;
            expectedStreams = SbeStreams;
        }
        else
        {
            expectedHost = "data-stream.binance.vision";
            expectedPort = 443;
            expectedStreams = JsonStreams;
        }

        if (!string.Equals(uri.IdnHost, expectedHost, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != expectedPort)
        {
            throw new ArgumentException(
                $"The selected source is restricted to wss://{expectedHost}:{expectedPort}/stream.",
                nameof(streamUrl));
        }

        IReadOnlyDictionary<string, string> query = ParseQuery(uri.Query);
        if (query.Count != 1 || !query.TryGetValue("streams", out string? streamsValue))
        {
            throw new ArgumentException("Binance stream URL must contain only the streams query parameter.", nameof(streamUrl));
        }

        string[] streams = streamsValue.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (streams.Length != expectedStreams.Count || !expectedStreams.SetEquals(streams))
        {
            throw new ArgumentException(
                "Binance stream URL must contain exactly the BTCUSDT trade and matching top-of-book streams.",
                nameof(streamUrl));
        }

        return uri;
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string queryText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string trimmed = queryText.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (string pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            string value = Uri.UnescapeDataString(equals < 0 ? string.Empty : pair[(equals + 1)..]);
            if (!result.TryAdd(key, value))
            {
                throw new ArgumentException("Binance stream URL contains a duplicate query parameter.", nameof(queryText));
            }
        }

        return result;
    }
}
