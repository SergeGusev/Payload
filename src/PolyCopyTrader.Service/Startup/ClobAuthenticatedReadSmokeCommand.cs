using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Service.Startup;

public static class ClobAuthenticatedReadSmokeCommand
{
    private const string ReadPath = "/trades";
    private const string OrdersPath = "/data/orders";

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        return await ExecuteAsync(
            configuration,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            httpClient,
            output,
            cancellationToken);
    }

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(output);

        var authOptions = configuration.PolymarketAuth;
        await output.WriteLineAsync("CLOB authenticated read smoke: GET /trades only; no order or cancel request will be sent.");
        await output.WriteLineAsync($"Live trading enabled: {configuration.Bot.EnableLiveTrading}");
        await output.WriteLineAsync($"Auth enabled: {authOptions.Enabled}");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(authOptions.FunderAddress)}");

        if (!authOptions.Enabled)
        {
            await output.WriteLineAsync("CLOB authenticated read smoke status: AuthDisabled");
            return 1;
        }

        try
        {
            var apiKey = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiKeyName, "API key", cancellationToken);
            var apiSecret = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiSecretName, "API secret", cancellationToken);
            var apiPassphrase = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiPassphraseName, "API passphrase", cancellationToken);
            var credentials = new PolymarketApiCredentials(apiKey, apiSecret, apiPassphrase);

            httpClient.Timeout = TimeSpan.FromSeconds(configuration.Polymarket.TimeoutSeconds);
            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PolyCopyTrader", "1.0"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(configuration.Polymarket.ClobBaseUrl), ReadPath))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };

            var headerFactory = new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner());
            foreach (var header in headerFactory.CreateL2Headers(
                authOptions.SigningAddress,
                credentials,
                new PolymarketAuthenticatedRequest(HttpMethod.Get.Method, ReadPath),
                DateTimeOffset.UtcNow))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            await output.WriteLineAsync($"HTTP status: {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                await output.WriteLineAsync("CLOB authenticated read smoke status: Error");
                await output.WriteLineAsync($"Reason: CLOB read-only authenticated request failed with HTTP {(int)response.StatusCode}; response body is not printed.");
                return 1;
            }

            await output.WriteLineAsync($"Response shape: {DescribeJsonShape(body)}");
            await output.WriteLineAsync("CLOB authenticated read smoke status: OK");
            return 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("CLOB authenticated read smoke status: Error");
            await output.WriteLineAsync("Reason: network request timed out.");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("CLOB authenticated read smoke status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ExecuteReportAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        return await ExecuteReportAsync(
            configuration,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            httpClient,
            output,
            cancellationToken);
    }

    public static async Task<int> ExecuteReportAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(output);

        var authOptions = configuration.PolymarketAuth;
        await output.WriteLineAsync("CLOB authenticated trades report: GET /trades only; no order or cancel request will be sent.");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(authOptions.FunderAddress)}");

        if (!authOptions.Enabled)
        {
            await output.WriteLineAsync("CLOB authenticated trades report status: AuthDisabled");
            return 1;
        }

        try
        {
            var apiKey = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiKeyName, "API key", cancellationToken);
            var apiSecret = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiSecretName, "API secret", cancellationToken);
            var apiPassphrase = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiPassphraseName, "API passphrase", cancellationToken);
            var credentials = new PolymarketApiCredentials(apiKey, apiSecret, apiPassphrase);

            httpClient.Timeout = TimeSpan.FromSeconds(configuration.Polymarket.TimeoutSeconds);
            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PolyCopyTrader", "1.0"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(configuration.Polymarket.ClobBaseUrl), ReadPath))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };

            var headerFactory = new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner());
            foreach (var header in headerFactory.CreateL2Headers(
                authOptions.SigningAddress,
                credentials,
                new PolymarketAuthenticatedRequest(HttpMethod.Get.Method, ReadPath),
                DateTimeOffset.UtcNow))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            await output.WriteLineAsync($"HTTP status: {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                await output.WriteLineAsync("CLOB authenticated trades report status: Error");
                return 1;
            }

            WriteTradeSummaries(body, output);
            await output.WriteLineAsync("CLOB authenticated trades report status: OK");
            return 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("CLOB authenticated trades report status: Error");
            await output.WriteLineAsync("Reason: network request timed out.");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("CLOB authenticated trades report status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
    }

    public static async Task<int> ExecuteOpenOrdersReportAsync(
        AppConfiguration configuration,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        return await ExecuteOpenOrdersReportAsync(
            configuration,
            PolymarketSecretProviderFactory.Create(configuration.PolymarketAuth),
            httpClient,
            output,
            cancellationToken);
    }

    public static async Task<int> ExecuteOpenOrdersReportAsync(
        AppConfiguration configuration,
        ISecretProvider secretProvider,
        HttpClient httpClient,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretProvider);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(output);

        var authOptions = configuration.PolymarketAuth;
        await output.WriteLineAsync("CLOB authenticated open orders report: GET /data/orders only; no order or cancel request will be sent.");
        await output.WriteLineAsync($"Signer address: {RedactAddress(authOptions.SigningAddress)}");
        await output.WriteLineAsync($"Funder address: {RedactAddress(authOptions.FunderAddress)}");

        if (!authOptions.Enabled)
        {
            await output.WriteLineAsync("CLOB authenticated open orders report status: AuthDisabled");
            return 1;
        }

        try
        {
            var apiKey = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiKeyName, "API key", cancellationToken);
            var apiSecret = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiSecretName, "API secret", cancellationToken);
            var apiPassphrase = await ReadRequiredSecretAsync(secretProvider, authOptions.ApiPassphraseName, "API passphrase", cancellationToken);
            var credentials = new PolymarketApiCredentials(apiKey, apiSecret, apiPassphrase);

            httpClient.Timeout = TimeSpan.FromSeconds(configuration.Polymarket.TimeoutSeconds);
            if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
            {
                httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PolyCopyTrader", "1.0"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(configuration.Polymarket.ClobBaseUrl), OrdersPath))
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };

            var headerFactory = new PolymarketAuthHeaderFactory(new PolymarketL2HmacSigner());
            foreach (var header in headerFactory.CreateL2Headers(
                authOptions.SigningAddress,
                credentials,
                new PolymarketAuthenticatedRequest(HttpMethod.Get.Method, OrdersPath),
                DateTimeOffset.UtcNow))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            await output.WriteLineAsync($"HTTP status: {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                await output.WriteLineAsync("CLOB authenticated open orders report status: Error");
                return 1;
            }

            WriteOrderSummaries(body, output);
            await output.WriteLineAsync("CLOB authenticated open orders report status: OK");
            return 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("CLOB authenticated open orders report status: Error");
            await output.WriteLineAsync("Reason: network request timed out.");
            return 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync("CLOB authenticated open orders report status: Error");
            await output.WriteLineAsync($"Error type: {ex.GetType().Name}");
            await output.WriteLineAsync($"Reason: {ex.Message}");
            return 1;
        }
    }

    private static async Task<string> ReadRequiredSecretAsync(
        ISecretProvider secretProvider,
        string name,
        string label,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"{label} secret reference is not configured.");
        }

        var value = await secretProvider.GetSecretAsync(name, cancellationToken);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{label} is unavailable from the configured secret provider.")
            : value;
    }

    private static string DescribeJsonShape(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "empty";
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => $"array[{document.RootElement.GetArrayLength()}]",
            JsonValueKind.Object when document.RootElement.TryGetProperty("count", out var count) => $"object count={count}",
            JsonValueKind.Object when document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => $"object data[{data.GetArrayLength()}]",
            JsonValueKind.Object => "object",
            _ => document.RootElement.ValueKind.ToString()
        };
    }

    private static void WriteTradeSummaries(string body, TextWriter output)
    {
        using var document = JsonDocument.Parse(body);
        var trades = ExtractTrades(document.RootElement).Take(10).ToArray();
        output.WriteLine($"Trades summarized: {trades.Length}");
        foreach (var trade in trades)
        {
            output.WriteLine(
                "Trade " +
                $"id={Shorten(GetString(trade, "id") ?? GetString(trade, "trade_id"))}; " +
                $"order={Shorten(GetString(trade, "order_id") ?? GetString(trade, "orderID") ?? GetString(trade, "orderId"))}; " +
                $"market={Shorten(GetString(trade, "market") ?? GetString(trade, "market_id") ?? GetString(trade, "condition_id"))}; " +
                $"asset={Shorten(GetString(trade, "asset_id") ?? GetString(trade, "assetId"))}; " +
                $"outcome={GetString(trade, "outcome")}; " +
                $"side={GetString(trade, "side")}; " +
                $"price={GetString(trade, "price")}; " +
                $"size={GetString(trade, "size") ?? GetString(trade, "amount")}; " +
                $"status={GetString(trade, "status")}; " +
                $"time={GetString(trade, "created_at") ?? GetString(trade, "match_time") ?? GetString(trade, "timestamp")}; " +
                $"tx={Shorten(GetString(trade, "transaction_hash") ?? GetString(trade, "transactionHash"))}");
        }
    }

    private static void WriteOrderSummaries(string body, TextWriter output)
    {
        using var document = JsonDocument.Parse(body);
        var orders = ExtractTrades(document.RootElement).Take(20).ToArray();
        output.WriteLine($"Orders summarized: {orders.Length}");
        foreach (var order in orders)
        {
            output.WriteLine(
                "Order " +
                $"id={Shorten(GetString(order, "id") ?? GetString(order, "order_id") ?? GetString(order, "orderID") ?? GetString(order, "orderId"))}; " +
                $"market={Shorten(GetString(order, "market") ?? GetString(order, "market_id") ?? GetString(order, "condition_id"))}; " +
                $"asset={Shorten(GetString(order, "asset_id") ?? GetString(order, "assetId"))}; " +
                $"outcome={GetString(order, "outcome")}; " +
                $"side={GetString(order, "side")}; " +
                $"price={GetString(order, "price")}; " +
                $"originalSize={GetString(order, "original_size") ?? GetString(order, "originalSize") ?? GetString(order, "size")}; " +
                $"matchedSize={GetString(order, "size_matched") ?? GetString(order, "matchedSize")}; " +
                $"status={GetString(order, "status")}; " +
                $"created={GetString(order, "created_at") ?? GetString(order, "createdAt") ?? GetString(order, "timestamp")}");
        }
    }

    private static IEnumerable<JsonElement> ExtractTrades(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "data", "trades", "results" })
            {
                if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    return array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object);
                }
            }
        }

        return [];
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static string Shorten(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= 14
            ? value
            : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
    }

    private static string RedactAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "not configured";
        }

        return value.Length <= 12
            ? value
            : string.Concat(value.AsSpan(0, 6), "...", value.AsSpan(value.Length - 4, 4));
    }
}
