using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class CoinbaseExchangeBtcUsdClient(
    HttpClient httpClient,
    CoinbaseExchangeOptions options) : IBtcUsdReferencePriceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildRequestUri());
        AddHeaders(request);

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Coinbase Exchange BTC/USD request failed with HTTP {(int)response.StatusCode} {response.StatusCode}: {TrimBody(body)}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<CoinbaseExchangeTicker>(
            stream,
            JsonOptions,
            cancellationToken);

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Price) ||
            !decimal.TryParse(payload.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var priceUsd) ||
            priceUsd <= 0m)
        {
            throw new InvalidOperationException("Coinbase Exchange BTC/USD ticker response did not include a positive price.");
        }

        var fetchedAtUtc = DateTimeOffset.UtcNow;
        var sourceUpdatedAtUtc = payload.Time is { } time
            ? time.ToUniversalTime()
            : fetchedAtUtc;

        return new BtcUsdReferencePricePoint(
            priceUsd,
            sourceUpdatedAtUtc,
            fetchedAtUtc,
            "CoinbaseExchange");
    }

    private Uri BuildRequestUri()
    {
        var baseUri = new Uri(EnsureTrailingSlash(options.BaseUrl));
        var productId = Uri.EscapeDataString(options.ProductId);
        return new Uri(
            baseUri,
            $"products/{productId}/ticker");
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
    }

    private static string TrimBody(string body)
    {
        const int maxLength = 500;
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return body.Length <= maxLength ? body : body.Substring(0, maxLength);
    }

    private sealed class CoinbaseExchangeTicker
    {
        [JsonPropertyName("price")]
        public string? Price { get; init; }

        [JsonPropertyName("time")]
        public DateTimeOffset? Time { get; init; }
    }
}
