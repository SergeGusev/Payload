using System.Globalization;
using System.Text.Json;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed record OkxExpiryFuturesInstrument(
    string AssetSymbol,
    string InstrumentId,
    DateTimeOffset ExpiryAtUtc);

public sealed record OkxExpiryFuturesTicker(
    string InstrumentId,
    decimal BidPriceUsd,
    decimal AskPriceUsd,
    decimal MidPriceUsd,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc);

public sealed record OkxUsdIndexTicker(
    string AssetSymbol,
    string InstrumentId,
    decimal IndexPriceUsd,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset FetchedAtUtc);

public static class OkxExpiryFuturesResponseParser
{
    public const string SourceName = "OkxExpiryFuturesRest";

    public static bool TryParseInstruments(
        ReadOnlySpan<byte> utf8Json,
        IReadOnlySet<string> assetSymbols,
        out IReadOnlyList<OkxExpiryFuturesInstrument> instruments,
        out string? error)
    {
        instruments = [];
        error = null;

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            if (!TryReadSuccessfulDataArray(document.RootElement, out var data, out error))
            {
                return false;
            }

            var parsed = new List<OkxExpiryFuturesInstrument>();
            foreach (var item in data.EnumerateArray())
            {
                var instrumentId = ReadString(item, "instId")?.Trim().ToUpperInvariant();
                var instrumentFamily = ReadString(item, "instFamily")?.Trim().ToUpperInvariant();
                var instrumentType = ReadString(item, "instType")?.Trim().ToUpperInvariant();
                var contractType = ReadString(item, "ctType")?.Trim().ToLowerInvariant();
                var settlementCurrency = ReadString(item, "settleCcy")?.Trim().ToUpperInvariant();
                var state = ReadString(item, "state")?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(instrumentId) ||
                    string.IsNullOrWhiteSpace(instrumentFamily) ||
                    !string.Equals(instrumentType, "FUTURES", StringComparison.Ordinal) ||
                    !string.Equals(contractType, "linear", StringComparison.Ordinal) ||
                    !string.Equals(settlementCurrency, "USD", StringComparison.Ordinal) ||
                    !string.Equals(state, "live", StringComparison.Ordinal) ||
                    instrumentId.Contains("XPERP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var assetSymbol = instrumentFamily.EndsWith("-USD_UM", StringComparison.Ordinal)
                    ? instrumentFamily[..^"-USD_UM".Length]
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(assetSymbol) || !assetSymbols.Contains(assetSymbol))
                {
                    continue;
                }

                if (!TryReadPositiveUnixMilliseconds(item, "expTime", out var expiryAtUtc))
                {
                    error = $"OKX instrument {instrumentId} did not include a valid positive expTime.";
                    return false;
                }

                parsed.Add(new OkxExpiryFuturesInstrument(assetSymbol, instrumentId, expiryAtUtc));
            }

            instruments = parsed
                .OrderBy(instrument => instrument.AssetSymbol, StringComparer.Ordinal)
                .ThenBy(instrument => instrument.ExpiryAtUtc)
                .ThenBy(instrument => instrument.InstrumentId, StringComparer.Ordinal)
                .ToArray();
            return true;
        }
        catch (JsonException ex)
        {
            error = "Invalid OKX instruments JSON: " + ex.Message;
            return false;
        }
    }

    public static bool TryParseFuturesTickers(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset fetchedAtUtc,
        IReadOnlySet<string> instrumentIds,
        out IReadOnlyDictionary<string, OkxExpiryFuturesTicker> tickers,
        out string? error)
    {
        tickers = new Dictionary<string, OkxExpiryFuturesTicker>(StringComparer.OrdinalIgnoreCase);
        error = null;

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            if (!TryReadSuccessfulDataArray(document.RootElement, out var data, out error))
            {
                return false;
            }

            var parsed = new Dictionary<string, OkxExpiryFuturesTicker>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.EnumerateArray())
            {
                var instrumentId = ReadString(item, "instId")?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(instrumentId) || !instrumentIds.Contains(instrumentId))
                {
                    continue;
                }

                if (!TryReadPositiveDecimal(item, "bidPx", out var bidPriceUsd) ||
                    !TryReadPositiveDecimal(item, "askPx", out var askPriceUsd) ||
                    askPriceUsd < bidPriceUsd ||
                    !TryReadPositiveUnixMilliseconds(item, "ts", out var sourceUpdatedAtUtc))
                {
                    continue;
                }

                parsed[instrumentId] = new OkxExpiryFuturesTicker(
                    instrumentId,
                    bidPriceUsd,
                    askPriceUsd,
                    (bidPriceUsd + askPriceUsd) / 2m,
                    sourceUpdatedAtUtc,
                    fetchedAtUtc);
            }

            tickers = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = "Invalid OKX futures tickers JSON: " + ex.Message;
            return false;
        }
    }

    public static bool TryParseIndexTicker(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset fetchedAtUtc,
        string expectedAssetSymbol,
        out OkxUsdIndexTicker? ticker,
        out string? error)
    {
        ticker = null;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            if (!TryReadSuccessfulDataArray(document.RootElement, out var data, out error))
            {
                return false;
            }

            var normalizedAsset = NormalizeAssetSymbol(expectedAssetSymbol);
            var expectedInstrumentId = normalizedAsset + "-USD";
            foreach (var item in data.EnumerateArray())
            {
                var instrumentId = ReadString(item, "instId")?.Trim().ToUpperInvariant();
                if (!string.Equals(instrumentId, expectedInstrumentId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryReadPositiveDecimal(item, "idxPx", out var indexPriceUsd))
                {
                    error = $"OKX index ticker {expectedInstrumentId} did not include a positive idxPx.";
                    return false;
                }

                if (!TryReadPositiveUnixMilliseconds(item, "ts", out var sourceUpdatedAtUtc))
                {
                    error = $"OKX index ticker {expectedInstrumentId} did not include a valid positive ts.";
                    return false;
                }

                ticker = new OkxUsdIndexTicker(
                    normalizedAsset,
                    expectedInstrumentId,
                    indexPriceUsd,
                    sourceUpdatedAtUtc,
                    fetchedAtUtc);
                return true;
            }

            error = $"OKX index ticker response did not include {expectedInstrumentId}.";
            return false;
        }
        catch (JsonException ex)
        {
            error = "Invalid OKX index ticker JSON: " + ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<OkxExpiryFuturesInstrument> SelectNearestExpiries(
        IEnumerable<OkxExpiryFuturesInstrument> instruments,
        string assetSymbol,
        DateTimeOffset targetMarketEndUtc,
        int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var normalizedAsset = NormalizeAssetSymbol(assetSymbol);
        return instruments
            .Where(instrument => string.Equals(instrument.AssetSymbol, normalizedAsset, StringComparison.OrdinalIgnoreCase))
            .Where(instrument => instrument.ExpiryAtUtc >= targetMarketEndUtc)
            .OrderBy(instrument => instrument.ExpiryAtUtc)
            .ThenBy(instrument => instrument.InstrumentId, StringComparer.Ordinal)
            .GroupBy(instrument => instrument.ExpiryAtUtc)
            .Select(group => group.First())
            .Take(count)
            .ToArray();
    }

    private static bool TryReadSuccessfulDataArray(
        JsonElement root,
        out JsonElement data,
        out string? error)
    {
        data = default;
        error = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "OKX JSON root was not an object.";
            return false;
        }

        var code = ReadString(root, "code");
        if (!string.Equals(code, "0", StringComparison.Ordinal))
        {
            error = $"OKX response code was {code ?? "missing"}: {ReadString(root, "msg") ?? string.Empty}".TrimEnd();
            return false;
        }

        if (!root.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Array)
        {
            error = "OKX response did not include a data array.";
            return false;
        }

        return true;
    }

    private static string NormalizeAssetSymbol(string assetSymbol)
    {
        return string.IsNullOrWhiteSpace(assetSymbol)
            ? string.Empty
            : assetSymbol.Trim().ToUpperInvariant();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool TryReadPositiveDecimal(JsonElement element, string propertyName, out decimal value)
    {
        value = 0m;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
            value > 0m;
    }

    private static bool TryReadPositiveUnixMilliseconds(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !long.TryParse(property.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var unixMilliseconds) ||
            unixMilliseconds <= 0)
        {
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
