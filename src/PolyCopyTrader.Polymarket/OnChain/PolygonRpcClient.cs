using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Polymarket.OnChain;

public sealed class PolygonRpcClient(HttpClient httpClient, OnChainIngestionOptions options) : IPolygonRpcClient
{
    private static long nextRequestId;

    public async Task<long> GetLatestBlockNumberAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("eth_blockNumber", [], cancellationToken);
        return HexToInt64(result.GetString() ?? "0x0");
    }

    public async Task<DateTimeOffset> GetBlockTimestampAsync(long blockNumber, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(
            "eth_getBlockByNumber",
            [ToQuantityHex(blockNumber), false],
            cancellationToken);

        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new PolymarketApiException(
                nameof(PolygonRpcClient),
                nameof(GetBlockTimestampAsync),
                $"Polygon RPC returned no block for {blockNumber}.");
        }

        var timestampHex = result.GetProperty("timestamp").GetString() ?? "0x0";
        return DateTimeOffset.FromUnixTimeSeconds(HexToInt64(timestampHex));
    }

    public async Task<IReadOnlyList<PolygonRpcLog>> GetLogsAsync(
        string contractAddress,
        string topic0,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken = default)
    {
        var filter = new
        {
            address = NormalizeHex(contractAddress),
            fromBlock = ToQuantityHex(fromBlock),
            toBlock = ToQuantityHex(toBlock),
            topics = new[] { NormalizeHex(topic0) }
        };

        var result = await SendAsync("eth_getLogs", [filter], cancellationToken);
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new PolymarketApiException(
                nameof(PolygonRpcClient),
                nameof(GetLogsAsync),
                "Polygon RPC eth_getLogs result was not an array.");
        }

        var logs = new List<PolygonRpcLog>();
        foreach (var item in result.EnumerateArray())
        {
            logs.Add(new PolygonRpcLog(
                NormalizeHex(item.GetProperty("address").GetString() ?? string.Empty),
                item.GetProperty("topics").EnumerateArray()
                    .Select(topic => NormalizeHex(topic.GetString() ?? string.Empty))
                    .ToArray(),
                NormalizeHex(item.GetProperty("data").GetString() ?? "0x"),
                HexToInt64(item.GetProperty("blockNumber").GetString() ?? "0x0"),
                NormalizeHex(item.GetProperty("blockHash").GetString() ?? string.Empty),
                NormalizeHex(item.GetProperty("transactionHash").GetString() ?? string.Empty),
                HexToInt64(item.GetProperty("transactionIndex").GetString() ?? "0x0"),
                HexToInt64(item.GetProperty("logIndex").GetString() ?? "0x0"),
                item.TryGetProperty("removed", out var removed) && removed.GetBoolean()));
        }

        return logs;
    }

    private async Task<JsonElement> SendAsync(
        string method,
        object[] parameters,
        CancellationToken cancellationToken)
    {
        var request = new JsonRpcRequest(
            "2.0",
            Interlocked.Increment(ref nextRequestId),
            method,
            parameters);

        using var response = await httpClient.PostAsJsonAsync(GetRpcUri(), request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new PolymarketApiException(
                nameof(PolygonRpcClient),
                method,
                $"Polygon RPC {method} failed with HTTP {(int)response.StatusCode}: {Trim(body)}");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            throw new PolymarketApiException(
                nameof(PolygonRpcClient),
                method,
                $"Polygon RPC {method} returned error: {Trim(error.ToString())}");
        }

        if (!root.TryGetProperty("result", out var result))
        {
            throw new PolymarketApiException(
                nameof(PolygonRpcClient),
                method,
                $"Polygon RPC {method} response did not contain result.");
        }

        return result.Clone();
    }

    private Uri GetRpcUri()
    {
        if (!string.IsNullOrWhiteSpace(options.RpcUrlEnvironmentVariable))
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(options.RpcUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return new Uri(fromEnvironment);
            }
        }

        return new Uri(options.PolygonRpcUrl);
    }

    private static long HexToInt64(string value)
    {
        var hex = StripHexPrefix(value);
        if (hex.Length == 0)
        {
            return 0;
        }

        return long.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static string ToQuantityHex(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "RPC block numbers must not be negative.");
        }

        return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
    }

    private static string NormalizeHex(string value)
    {
        var hex = StripHexPrefix(value).ToLowerInvariant();
        return "0x" + hex;
    }

    private static string StripHexPrefix(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
    }

    private static string Trim(string value)
    {
        return value.Length <= 512 ? value : value[..512];
    }

    private sealed record JsonRpcRequest(
        string Jsonrpc,
        long Id,
        string Method,
        object[] Params);
}
