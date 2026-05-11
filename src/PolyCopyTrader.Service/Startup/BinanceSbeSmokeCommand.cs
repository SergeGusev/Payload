using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Service.Startup;

public static class BinanceSbeSmokeCommand
{
    private const string DefaultPrivateKeyPath = @"C:\Keys\Binance\binance_sbe_ed25519_private.pem";
    private const string ApiKeyEnvironmentVariable = "POLYCOPYTRADER_BINANCE_SBE_API_KEY";

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        int messageCount = GetIntOption(args, "--binance-sbe-message-count", 8, minValue: 1, maxValue: 1_000);
        int timeoutSeconds = GetIntOption(args, "--binance-sbe-timeout-seconds", 20, minValue: 1, maxValue: 300);
        string streams = GetOptionValue(args, "--binance-sbe-streams") ?? "btcusdt@trade/btcusdt@bestBidAsk";
        string baseUrl = GetOptionValue(args, "--binance-sbe-base-url") ?? "wss://stream-sbe.binance.com:9443";
        string privateKeyPath = GetOptionValue(args, "--binance-sbe-private-key-path") ?? DefaultPrivateKeyPath;
        string publicKeyPath = GetOptionValue(args, "--binance-sbe-public-key-path") ?? GetDefaultPublicKeyPath(privateKeyPath);
        SecretValue? apiKey = ResolveApiKey(args, publicKeyPath);

        if (apiKey is null)
        {
            await output.WriteLineAsync(
                "Binance SBE smoke failed: API key is not configured. Set POLYCOPYTRADER_BINANCE_SBE_API_KEY, pass --binance-sbe-api-key, or provide --binance-sbe-api-key-file.");
            await output.WriteLineAsync("The Ed25519 private key file is not sent to Binance and is not sufficient for the X-MBX-APIKEY header.");
            return 2;
        }

        var url = BuildStreamUrl(baseUrl, streams);
        await output.WriteLineAsync("Binance SBE smoke starting.");
        await output.WriteLineAsync("Endpoint: " + url);
        await output.WriteLineAsync("Streams: " + streams);
        await output.WriteLineAsync("API key source: " + apiKey.Source);
        await output.WriteLineAsync("Private key file present: " + File.Exists(privateKeyPath));
        await output.WriteLineAsync("Target decoded binary messages: " + messageCount.ToString(CultureInfo.InvariantCulture));

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("X-MBX-APIKEY", apiKey.Value);
            await socket.ConnectAsync(new Uri(url), timeoutCts.Token);
            await output.WriteLineAsync("Connected. Waiting for SBE binary frames...");

            int decoded = await ReceiveAndDecodeAsync(socket, messageCount, output, timeoutCts.Token);
            await output.WriteLineAsync(
                "Binance SBE smoke completed. DecodedMessages=" + decoded.ToString(CultureInfo.InvariantCulture));
            return decoded > 0 ? 0 : 3;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await output.WriteLineAsync("Binance SBE smoke timed out before enough binary messages were decoded.");
            return 4;
        }
        catch (WebSocketException ex)
        {
            await output.WriteLineAsync("Binance SBE smoke WebSocket error: " + ex.Message);
            await output.WriteLineAsync("If the API key source is a public PEM body, Binance may still require the API key id from API Management.");
            return 5;
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync("Binance SBE smoke failed: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> ReceiveAndDecodeAsync(
        ClientWebSocket socket,
        int messageCount,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        int decoded = 0;

        while (socket.State == WebSocketState.Open && decoded < messageCount)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await output.WriteLineAsync(
                        $"WebSocket closed by server. Status={result.CloseStatus}; Description={result.CloseStatusDescription}");
                    return decoded;
                }

                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string text = Encoding.UTF8.GetString(message.ToArray());
                await output.WriteLineAsync("Text frame: " + Truncate(text, 500));
                continue;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                continue;
            }

            byte[] payload = message.ToArray();
            if (!BinanceSbeMarketDataDecoder.TryDecode(payload, receivedAtUtc, out var decodedMessage, out string? error) ||
                decodedMessage is null)
            {
                await output.WriteLineAsync("Binary frame skipped: " + error);
                continue;
            }

            decoded++;
            await output.WriteLineAsync(FormatDecodedMessage(decoded, decodedMessage));
        }

        return decoded;
    }

    private static string FormatDecodedMessage(int index, BinanceSbeMarketDataEvent message)
    {
        decimal lagMilliseconds = (decimal)(message.ReceivedAtUtc - message.EventTimeUtc).TotalMilliseconds;
        string prefix =
            $"[{index}] {message.Symbol} {message.MessageName} event={message.EventTimeUtc:O} recv={message.ReceivedAtUtc:O} lagMs={lagMilliseconds:0.###}";

        return message switch
        {
            BinanceSbeBestBidAskEvent best =>
                prefix +
                $" updateId={best.BookUpdateId} bid={best.BidPrice:0.########} bidQty={best.BidQty:0.########} ask={best.AskPrice:0.########} askQty={best.AskQty:0.########}",
            BinanceSbeTradeEvent trade when trade.Trades.Count > 0 =>
                prefix +
                $" trades={trade.Trades.Count} firstTradeId={trade.Trades[0].Id} firstPrice={trade.Trades[0].Price:0.########} firstQty={trade.Trades[0].Quantity:0.########} buyerMaker={trade.Trades[0].IsBuyerMaker}",
            BinanceSbeTradeEvent trade =>
                prefix + $" trades={trade.Trades.Count}",
            _ => prefix
        };
    }

    private static SecretValue? ResolveApiKey(string[] args, string publicKeyPath)
    {
        string? fromArgument = GetOptionValue(args, "--binance-sbe-api-key");
        if (!string.IsNullOrWhiteSpace(fromArgument))
        {
            return new SecretValue(fromArgument.Trim(), "argument");
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new SecretValue(fromEnvironment.Trim(), ApiKeyEnvironmentVariable);
        }

        string? apiKeyFile = GetOptionValue(args, "--binance-sbe-api-key-file");
        if (!string.IsNullOrWhiteSpace(apiKeyFile))
        {
            return new SecretValue(ReadSingleLineSecret(apiKeyFile), "api-key-file");
        }

        if (File.Exists(publicKeyPath))
        {
            string candidate = ReadPemBody(publicKeyPath);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return new SecretValue(candidate, "public-key-pem-body");
            }
        }

        return null;
    }

    private static string ReadSingleLineSecret(string path)
    {
        string text = File.ReadAllText(path);
        string trimmed = text.Trim();
        if (trimmed.StartsWith("-----BEGIN ", StringComparison.Ordinal))
        {
            return NormalizePemBody(trimmed);
        }

        return trimmed.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string ReadPemBody(string path)
    {
        return NormalizePemBody(File.ReadAllText(path));
    }

    private static string NormalizePemBody(string pem)
    {
        var builder = new StringBuilder();
        foreach (string line in pem.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 ||
                trimmed.StartsWith("-----BEGIN ", StringComparison.Ordinal) ||
                trimmed.StartsWith("-----END ", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    private static string BuildStreamUrl(string baseUrl, string streams)
    {
        string normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');
        string normalized = streams.Trim().Trim('/');
        return normalized.Contains('/', StringComparison.Ordinal)
            ? normalizedBaseUrl + "/stream?streams=" + normalized
            : normalizedBaseUrl + "/ws/" + normalized;
    }

    private static string GetDefaultPublicKeyPath(string privateKeyPath)
    {
        return privateKeyPath.EndsWith("_private.pem", StringComparison.OrdinalIgnoreCase)
            ? privateKeyPath[..^"_private.pem".Length] + "_public.pem"
            : Path.ChangeExtension(privateKeyPath, ".public.pem");
    }

    private static int GetIntOption(
        string[] args,
        string name,
        int defaultValue,
        int minValue,
        int maxValue)
    {
        string? value = GetOptionValue(args, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, minValue, maxValue);
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        return null;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    private sealed record SecretValue(string Value, string Source);
}
