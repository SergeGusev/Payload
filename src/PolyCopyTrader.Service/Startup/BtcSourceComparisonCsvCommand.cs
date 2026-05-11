using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Service.Startup;

public static class BtcSourceComparisonCsvCommand
{
    private const string BinanceSbeApiKeyEnvironmentVariable = "POLYCOPYTRADER_BINANCE_SBE_API_KEY";
    private const string DefaultOutputDirectory = "artifacts/btc-source-comparison";

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var options = BtcSourceComparisonOptions.FromArgs(args);
        SecretValue? sbeApiKey = ResolveSbeApiKey(args);
        if (sbeApiKey is null)
        {
            await output.WriteLineAsync(
                "BTC source comparison failed: Binance SBE API key id is not configured. Set POLYCOPYTRADER_BINANCE_SBE_API_KEY, pass --binance-sbe-api-key, or pass --binance-sbe-api-key-file.");
            return 2;
        }

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset targetStartUtc = options.StartMode == BtcSourceComparisonStartMode.Current
            ? FloorToFiveMinuteWindow(nowUtc)
            : CeilToNextFiveMinuteWindow(nowUtc);

        await output.WriteLineAsync("BTC source comparison starting.");
        await output.WriteLineAsync("Start mode: " + options.StartMode);
        await output.WriteLineAsync("Target market start UTC: " + targetStartUtc.ToString("O", CultureInfo.InvariantCulture));
        await output.WriteLineAsync("Sample interval ms: " + options.SampleIntervalMilliseconds.ToString(CultureInfo.InvariantCulture));
        await output.WriteLineAsync("SBE API key source: " + sbeApiKey.Source);

        PolymarketGammaMarket market = await WaitForBtcMarketAsync(
            targetStartUtc,
            options,
            output,
            cancellationToken);

        var outcomeQuotes = BtcUpDown5mMarketAnalyzer.GetOutcomeQuotes(market);
        BtcUpDown5mOutcomeQuote? upQuote = outcomeQuotes.FirstOrDefault(
            quote => string.Equals(quote.Outcome, "Up", StringComparison.OrdinalIgnoreCase)) ?? outcomeQuotes.FirstOrDefault();
        BtcUpDown5mOutcomeQuote? downQuote = outcomeQuotes.FirstOrDefault(
            quote => string.Equals(quote.Outcome, "Down", StringComparison.OrdinalIgnoreCase));

        if (upQuote is null)
        {
            await output.WriteLineAsync("BTC source comparison failed: target market has no CLOB token ids/outcome quotes.");
            return 3;
        }

        DateTimeOffset marketStartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market) ?? targetStartUtc;
        DateTimeOffset marketEndUtc = market.EndDateUtc ?? marketStartUtc.AddMinutes(5);
        if (marketEndUtc <= nowUtc)
        {
            await output.WriteLineAsync("BTC source comparison failed: selected market is already closed.");
            return 4;
        }

        await output.WriteLineAsync("Market: " + market.Slug);
        await output.WriteLineAsync("Market id: " + market.MarketId);
        await output.WriteLineAsync("Condition id: " + market.ConditionId);
        await output.WriteLineAsync("Up asset id: " + upQuote.AssetId);
        await output.WriteLineAsync("Down asset id: " + (downQuote?.AssetId ?? "<missing>"));
        await output.WriteLineAsync("Market close UTC: " + marketEndUtc.ToString("O", CultureInfo.InvariantCulture));

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var sbeSource = new BinanceSbeBestBidAskSource(
            options.SbeStreamUrl,
            sbeApiKey.Value,
            output);
        using var jsonSource = new BinanceJsonBookTickerSource(
            options.JsonBookTickerStreamUrl,
            output);
        using var polymarketSource = new PolymarketBookSource(
            options.ClobBaseUrl,
            options.PolymarketTimeoutSeconds,
            upQuote.AssetId,
            downQuote?.AssetId);

        await sbeSource.StartAsync(runCts.Token);
        await jsonSource.StartAsync(runCts.Token);

        if (DateTimeOffset.UtcNow < marketStartUtc)
        {
            TimeSpan wait = marketStartUtc - DateTimeOffset.UtcNow;
            await output.WriteLineAsync("Waiting for market start: " + wait.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " seconds.");
            await Task.Delay(wait, cancellationToken);
        }

        var rows = new List<BtcSourceComparisonRow>(capacity: 512);
        var baselines = new ComparisonBaselines();
        await output.WriteLineAsync("Sampling started.");

        while (DateTimeOffset.UtcNow < marketEndUtc && !cancellationToken.IsCancellationRequested)
        {
            DateTimeOffset sampleUtc = DateTimeOffset.UtcNow;
            Task<BinanceBookSnapshot?> sbeTask = sbeSource.GetSnapshotAsync(sampleUtc, cancellationToken);
            Task<BinanceBookSnapshot?> jsonTask = jsonSource.GetSnapshotAsync(sampleUtc, cancellationToken);
            Task<PolymarketComparisonSnapshot> polymarketTask = polymarketSource.GetSnapshotAsync(sampleUtc, cancellationToken);

            await Task.WhenAll(sbeTask, jsonTask, polymarketTask);

            var sbe = await sbeTask;
            var json = await jsonTask;
            var polymarket = await polymarketTask;
            baselines.Observe(sbe, json, polymarket);
            rows.Add(BtcSourceComparisonRow.FromSnapshots(
                sampleUtc,
                market,
                marketStartUtc,
                marketEndUtc,
                upQuote,
                downQuote,
                sbe,
                json,
                polymarket,
                baselines));

            TimeSpan delay = TimeSpan.FromMilliseconds(options.SampleIntervalMilliseconds);
            DateTimeOffset nextSampleUtc = sampleUtc + delay;
            TimeSpan remaining = nextSampleUtc - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }
        }

        runCts.Cancel();
        string outputPath = WriteCsv(options, market, rows);
        await output.WriteLineAsync("Sampling completed. Rows=" + rows.Count.ToString(CultureInfo.InvariantCulture));
        await output.WriteLineAsync("CSV: " + outputPath);
        return rows.Count > 0 ? 0 : 5;
    }

    private static async Task<PolymarketGammaMarket> WaitForBtcMarketAsync(
        DateTimeOffset targetStartUtc,
        BtcSourceComparisonOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var polymarketOptions = new PolymarketOptions
        {
            GammaBaseUrl = options.GammaBaseUrl,
            ClobBaseUrl = options.ClobBaseUrl,
            TimeoutSeconds = options.PolymarketTimeoutSeconds,
            MaxRetries = 1,
            RetryBaseDelayMilliseconds = 500
        };

        using var httpClient = new HttpClient();
        var gammaClient = new PolymarketGammaClient(
            httpClient,
            polymarketOptions,
            new NullPolymarketApiErrorSink(),
            null);
        DateTimeOffset stopPollingUtc = targetStartUtc.AddSeconds(options.MarketLookupGraceSeconds);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow <= stopPollingUtc)
        {
            try
            {
                var markets = await gammaClient.GetActiveMarketsAsync(500, 0, cancellationToken);
                var candidate = markets
                    .Where(BtcUpDown5mMarketAnalyzer.IsCandidate)
                    .Select(market => new
                    {
                        Market = market,
                        StartUtc = BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
                    })
                    .Where(item => item.StartUtc.HasValue)
                    .OrderBy(item => Math.Abs((item.StartUtc!.Value - targetStartUtc).TotalSeconds))
                    .FirstOrDefault(item => item.StartUtc == targetStartUtc);

                if (candidate is not null)
                {
                    return candidate.Market;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            await output.WriteLineAsync(
                "Waiting for BTC 5m Gamma market " +
                targetStartUtc.ToString("O", CultureInfo.InvariantCulture) +
                (lastError is null ? string.Empty : " LastError=" + lastError.Message));
            await Task.Delay(TimeSpan.FromSeconds(options.MarketLookupPollSeconds), cancellationToken);
        }

        throw new InvalidOperationException(
            "Could not find active BTC Up or Down 5m market for start " +
            targetStartUtc.ToString("O", CultureInfo.InvariantCulture) +
            (lastError is null ? "." : ". Last error: " + lastError.Message));
    }

    private static string WriteCsv(
        BtcSourceComparisonOptions options,
        PolymarketGammaMarket market,
        IReadOnlyList<BtcSourceComparisonRow> rows)
    {
        string outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        string safeSlug = string.Join("_", market.Slug.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        string path = Path.Combine(
            outputDirectory,
            "btc-source-comparison-" + safeSlug + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");

        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(BtcSourceComparisonRow.CsvHeader);
        foreach (var row in rows)
        {
            writer.WriteLine(row.ToCsvLine());
        }

        return path;
    }

    private static SecretValue? ResolveSbeApiKey(string[] args)
    {
        string? fromArgument = GetOptionValue(args, "--binance-sbe-api-key");
        if (!string.IsNullOrWhiteSpace(fromArgument))
        {
            return new SecretValue(fromArgument.Trim(), "argument");
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(BinanceSbeApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new SecretValue(fromEnvironment.Trim(), BinanceSbeApiKeyEnvironmentVariable);
        }

        string? apiKeyFile = GetOptionValue(args, "--binance-sbe-api-key-file");
        if (!string.IsNullOrWhiteSpace(apiKeyFile))
        {
            return new SecretValue(File.ReadAllText(apiKeyFile).Trim(), "api-key-file");
        }

        return null;
    }

    private static DateTimeOffset FloorToFiveMinuteWindow(DateTimeOffset value)
    {
        long unix = value.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unix - unix % 300);
    }

    private static DateTimeOffset CeilToNextFiveMinuteWindow(DateTimeOffset value)
    {
        long unix = value.ToUnixTimeSeconds();
        long next = (unix / 300 + 1) * 300;
        return DateTimeOffset.FromUnixTimeSeconds(next);
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

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minValue, maxValue)
            : defaultValue;
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return value.Contains('"', StringComparison.Ordinal) ||
               value.Contains(',', StringComparison.Ordinal) ||
               value.Contains('\n', StringComparison.Ordinal) ||
               value.Contains('\r', StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static string Csv(DateTimeOffset? value)
    {
        return value.HasValue ? value.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Csv(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Csv(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static decimal? Mid(decimal? bid, decimal? ask)
    {
        return bid.HasValue && ask.HasValue ? (bid.Value + ask.Value) / 2m : null;
    }

    private sealed class BinanceSbeBestBidAskSource(
        string streamUrl,
        string apiKey,
        TextWriter output) : IDisposable
    {
        private readonly object sync = new();
        private CancellationTokenSource? sourceCts;
        private Task? runTask;
        private BinanceBookSnapshot? latest;
        private string? lastError;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            sourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runTask = Task.Run(() => RunAsync(sourceCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task<BinanceBookSnapshot?> GetSnapshotAsync(DateTimeOffset sampleUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                return Task.FromResult<BinanceBookSnapshot?>(latest is null
                    ? new BinanceBookSnapshot("BinanceSBE", null, null, null, null, null, null, sampleUtc, sampleUtc, lastError ?? "no_snapshot")
                    : latest with { SampleUtc = sampleUtc, Error = lastError });
            }
        }

        public void Dispose()
        {
            sourceCts?.Cancel();
            sourceCts?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var socket = new ClientWebSocket();
                    socket.Options.SetRequestHeader("X-MBX-APIKEY", apiKey);
                    await socket.ConnectAsync(new Uri(streamUrl), cancellationToken);
                    await output.WriteLineAsync("Binance SBE bestBidAsk stream connected.");
                    await ReceiveAsync(socket, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        lastError = ex.Message;
                    }

                    await output.WriteLineAsync("Binance SBE bestBidAsk stream error: " + ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    continue;
                }

                DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
                if (!BinanceSbeMarketDataDecoder.TryDecode(message.ToArray(), receivedAtUtc, out var decoded, out string? error))
                {
                    lock (sync)
                    {
                        lastError = error;
                    }

                    continue;
                }

                if (decoded is not BinanceSbeBestBidAskEvent best)
                {
                    continue;
                }

                lock (sync)
                {
                    latest = new BinanceBookSnapshot(
                        "BinanceSBE",
                        best.BidPrice,
                        best.BidQty,
                        best.AskPrice,
                        best.AskQty,
                        Mid(best.BidPrice, best.AskPrice),
                        best.EventTimeUtc,
                        receivedAtUtc,
                        receivedAtUtc,
                        null);
                    lastError = null;
                }
            }
        }
    }

    private sealed class BinanceJsonBookTickerSource(
        string streamUrl,
        TextWriter output) : IDisposable
    {
        private readonly object sync = new();
        private CancellationTokenSource? sourceCts;
        private BinanceBookSnapshot? latest;
        private string? lastError;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            sourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(() => RunAsync(sourceCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task<BinanceBookSnapshot?> GetSnapshotAsync(DateTimeOffset sampleUtc, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                return Task.FromResult<BinanceBookSnapshot?>(latest is null
                    ? new BinanceBookSnapshot("BinanceJsonBookTicker", null, null, null, null, null, null, sampleUtc, sampleUtc, lastError ?? "no_snapshot")
                    : latest with { SampleUtc = sampleUtc, Error = lastError });
            }
        }

        public void Dispose()
        {
            sourceCts?.Cancel();
            sourceCts?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var socket = new ClientWebSocket();
                    await socket.ConnectAsync(new Uri(streamUrl), cancellationToken);
                    await output.WriteLineAsync("Binance JSON bookTicker stream connected.");
                    await ReceiveAsync(socket, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        lastError = ex.Message;
                    }

                    await output.WriteLineAsync("Binance JSON bookTicker stream error: " + ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
            }
        }

        private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                DateTimeOffset receivedAtUtc = DateTimeOffset.UtcNow;
                if (!TryParseBookTicker(message.ToArray(), receivedAtUtc, out var snapshot, out string? error))
                {
                    lock (sync)
                    {
                        lastError = error;
                    }

                    continue;
                }

                lock (sync)
                {
                    latest = snapshot;
                    lastError = null;
                }
            }
        }

        private static bool TryParseBookTicker(
            byte[] payload,
            DateTimeOffset receivedAtUtc,
            out BinanceBookSnapshot? snapshot,
            out string? error)
        {
            snapshot = null;
            error = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement.TryGetProperty("data", out JsonElement data)
                    ? data
                    : document.RootElement;
                decimal? bid = ReadDecimal(root, "b");
                decimal? bidQty = ReadDecimal(root, "B");
                decimal? ask = ReadDecimal(root, "a");
                decimal? askQty = ReadDecimal(root, "A");
                if (bid is null || ask is null)
                {
                    error = "bookTicker has no bid/ask.";
                    return false;
                }

                snapshot = new BinanceBookSnapshot(
                    "BinanceJsonBookTicker",
                    bid,
                    bidQty,
                    ask,
                    askQty,
                    Mid(bid, ask),
                    null,
                    receivedAtUtc,
                    receivedAtUtc,
                    null);
                return true;
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private sealed class PolymarketBookSource : IDisposable
    {
        private readonly string upAssetId;
        private readonly string? downAssetId;
        private readonly HttpClient httpClient;
        private readonly PolymarketClobPublicClient client;

        public PolymarketBookSource(
            string clobBaseUrl,
            int timeoutSeconds,
            string upAssetId,
            string? downAssetId)
        {
            this.upAssetId = upAssetId;
            this.downAssetId = downAssetId;
            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            client = new PolymarketClobPublicClient(
                httpClient,
                new PolymarketOptions
                {
                    ClobBaseUrl = clobBaseUrl,
                    TimeoutSeconds = timeoutSeconds,
                    MaxRetries = 1,
                    RetryBaseDelayMilliseconds = 250
                },
                new NullPolymarketApiErrorSink(),
                null);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }

        public async Task<PolymarketComparisonSnapshot> GetSnapshotAsync(
            DateTimeOffset sampleUtc,
            CancellationToken cancellationToken)
        {
            Task<OrderBookSnapshot?> upTask = client.GetOrderBookAsync(upAssetId, cancellationToken);
            Task<OrderBookSnapshot?>? downTask = string.IsNullOrWhiteSpace(downAssetId)
                ? null
                : client.GetOrderBookAsync(downAssetId, cancellationToken);

            try
            {
                OrderBookSnapshot? up = await upTask;
                OrderBookSnapshot? down = downTask is null ? null : await downTask;
                decimal? upMid = Mid(up?.BestBid, up?.BestAsk);
                decimal? downMid = Mid(down?.BestBid, down?.BestAsk);
                decimal? impliedUpMid = upMid ?? (downMid.HasValue ? 1m - downMid.Value : null);
                return new PolymarketComparisonSnapshot(
                    sampleUtc,
                    up?.BestBid,
                    up?.BestAsk,
                    upMid,
                    up?.LastTradePrice,
                    down?.BestBid,
                    down?.BestAsk,
                    downMid,
                    down?.LastTradePrice,
                    impliedUpMid,
                    up?.SnapshotAtUtc,
                    null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new PolymarketComparisonSnapshot(
                    sampleUtc,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    ex.Message);
            }
        }
    }

    private sealed class ComparisonBaselines
    {
        public decimal? SbeMid { get; private set; }
        public decimal? JsonMid { get; private set; }
        public decimal? PolymarketUpMid { get; private set; }

        public void Observe(
            BinanceBookSnapshot? sbe,
            BinanceBookSnapshot? json,
            PolymarketComparisonSnapshot polymarket)
        {
            SbeMid ??= sbe?.MidPrice;
            JsonMid ??= json?.MidPrice;
            PolymarketUpMid ??= polymarket.ImpliedUpMid;
        }
    }

    private sealed record SecretValue(string Value, string Source);

    private enum BtcSourceComparisonStartMode
    {
        Next,
        Current
    }

    private sealed record BtcSourceComparisonOptions(
        BtcSourceComparisonStartMode StartMode,
        int SampleIntervalMilliseconds,
        int MarketLookupGraceSeconds,
        int MarketLookupPollSeconds,
        int PolymarketTimeoutSeconds,
        string OutputDirectory,
        string GammaBaseUrl,
        string ClobBaseUrl,
        string SbeStreamUrl,
        string JsonBookTickerStreamUrl)
    {
        public static BtcSourceComparisonOptions FromArgs(string[] args)
        {
            var startModeValue = GetOptionValue(args, "--btc-source-comparison-start-mode") ?? "next";
            var startMode = string.Equals(startModeValue, "current", StringComparison.OrdinalIgnoreCase)
                ? BtcSourceComparisonStartMode.Current
                : BtcSourceComparisonStartMode.Next;
            return new BtcSourceComparisonOptions(
                startMode,
                GetIntOption(args, "--btc-source-comparison-sample-ms", 1000, 100, 60_000),
                GetIntOption(args, "--btc-source-comparison-market-lookup-grace-seconds", 90, 5, 600),
                GetIntOption(args, "--btc-source-comparison-market-lookup-poll-seconds", 2, 1, 60),
                GetIntOption(args, "--btc-source-comparison-polymarket-timeout-seconds", 10, 1, 60),
                GetOptionValue(args, "--btc-source-comparison-output-dir") ?? DefaultOutputDirectory,
                GetOptionValue(args, "--btc-source-comparison-gamma-base-url") ?? "https://gamma-api.polymarket.com",
                GetOptionValue(args, "--btc-source-comparison-clob-base-url") ?? "https://clob.polymarket.com",
                GetOptionValue(args, "--btc-source-comparison-sbe-stream-url") ?? "wss://stream-sbe.binance.com:9443/ws/btcusdt@bestBidAsk",
                GetOptionValue(args, "--btc-source-comparison-json-bookticker-url") ?? "wss://data-stream.binance.vision:443/ws/btcusdt@bookTicker");
        }
    }

    private sealed record BinanceBookSnapshot(
        string Source,
        decimal? BidPrice,
        decimal? BidQuantity,
        decimal? AskPrice,
        decimal? AskQuantity,
        decimal? MidPrice,
        DateTimeOffset? SourceEventUtc,
        DateTimeOffset ReceivedAtUtc,
        DateTimeOffset SampleUtc,
        string? Error);

    private sealed record PolymarketComparisonSnapshot(
        DateTimeOffset SampleUtc,
        decimal? UpBid,
        decimal? UpAsk,
        decimal? UpMid,
        decimal? UpLastTrade,
        decimal? DownBid,
        decimal? DownAsk,
        decimal? DownMid,
        decimal? DownLastTrade,
        decimal? ImpliedUpMid,
        DateTimeOffset? OrderBookTimestampUtc,
        string? Error);

    private sealed record BtcSourceComparisonRow(
        DateTimeOffset SampleUtc,
        decimal SecondsFromMarketStart,
        string MarketSlug,
        string MarketId,
        string ConditionId,
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc,
        string UpAssetId,
        string UpOutcome,
        string DownAssetId,
        string DownOutcome,
        decimal? SbeBid,
        decimal? SbeAsk,
        decimal? SbeMid,
        decimal? SbeMidBpsFromStart,
        DateTimeOffset? SbeEventUtc,
        decimal? SbeAgeMs,
        string? SbeError,
        decimal? JsonBid,
        decimal? JsonAsk,
        decimal? JsonMid,
        decimal? JsonMidBpsFromStart,
        decimal? JsonAgeMs,
        string? JsonError,
        decimal? PolymarketUpBid,
        decimal? PolymarketUpAsk,
        decimal? PolymarketUpMid,
        decimal? PolymarketUpImpliedMid,
        decimal? PolymarketUpRelativeBpsFromStart,
        decimal? PolymarketUpDeltaProbabilityBpsFromStart,
        decimal? PolymarketDownBid,
        decimal? PolymarketDownAsk,
        decimal? PolymarketDownMid,
        DateTimeOffset? PolymarketOrderBookTimestampUtc,
        decimal? PolymarketAgeMs,
        string? PolymarketError)
    {
        public const string CsvHeader =
            "sample_utc,seconds_from_market_start,market_slug,market_id,condition_id,market_start_utc,market_end_utc,up_asset_id,up_outcome,down_asset_id,down_outcome," +
            "sbe_bid,sbe_ask,sbe_mid,sbe_mid_bps_from_start,sbe_event_utc,sbe_age_ms,sbe_error," +
            "json_bid,json_ask,json_mid,json_mid_bps_from_start,json_age_ms,json_error," +
            "polymarket_up_bid,polymarket_up_ask,polymarket_up_mid,polymarket_up_implied_mid,polymarket_up_relative_bps_from_start,polymarket_up_delta_probability_bps_from_start," +
            "polymarket_down_bid,polymarket_down_ask,polymarket_down_mid,polymarket_orderbook_timestamp_utc,polymarket_age_ms,polymarket_error";

        public static BtcSourceComparisonRow FromSnapshots(
            DateTimeOffset sampleUtc,
            PolymarketGammaMarket market,
            DateTimeOffset marketStartUtc,
            DateTimeOffset marketEndUtc,
            BtcUpDown5mOutcomeQuote upQuote,
            BtcUpDown5mOutcomeQuote? downQuote,
            BinanceBookSnapshot? sbe,
            BinanceBookSnapshot? json,
            PolymarketComparisonSnapshot polymarket,
            ComparisonBaselines baselines)
        {
            return new BtcSourceComparisonRow(
                sampleUtc,
                (decimal)(sampleUtc - marketStartUtc).TotalSeconds,
                market.Slug,
                market.MarketId,
                market.ConditionId,
                marketStartUtc,
                marketEndUtc,
                upQuote.AssetId,
                upQuote.Outcome,
                downQuote?.AssetId ?? string.Empty,
                downQuote?.Outcome ?? string.Empty,
                sbe?.BidPrice,
                sbe?.AskPrice,
                sbe?.MidPrice,
                RelativeBps(sbe?.MidPrice, baselines.SbeMid),
                sbe?.SourceEventUtc,
                sbe?.SourceEventUtc is { } sbeEventUtc ? (decimal)(sampleUtc - sbeEventUtc).TotalMilliseconds : null,
                sbe?.Error,
                json?.BidPrice,
                json?.AskPrice,
                json?.MidPrice,
                RelativeBps(json?.MidPrice, baselines.JsonMid),
                json is null ? null : (decimal)(sampleUtc - json.ReceivedAtUtc).TotalMilliseconds,
                json?.Error,
                polymarket.UpBid,
                polymarket.UpAsk,
                polymarket.UpMid,
                polymarket.ImpliedUpMid,
                RelativeBps(polymarket.ImpliedUpMid, baselines.PolymarketUpMid),
                DeltaProbabilityBps(polymarket.ImpliedUpMid, baselines.PolymarketUpMid),
                polymarket.DownBid,
                polymarket.DownAsk,
                polymarket.DownMid,
                polymarket.OrderBookTimestampUtc,
                polymarket.OrderBookTimestampUtc is { } orderBookTimestampUtc ? (decimal)(sampleUtc - orderBookTimestampUtc).TotalMilliseconds : null,
                polymarket.Error);
        }

        public string ToCsvLine()
        {
            return string.Join(
                ',',
                Csv(SampleUtc),
                Csv(SecondsFromMarketStart),
                Csv(MarketSlug),
                Csv(MarketId),
                Csv(ConditionId),
                Csv(MarketStartUtc),
                Csv(MarketEndUtc),
                Csv(UpAssetId),
                Csv(UpOutcome),
                Csv(DownAssetId),
                Csv(DownOutcome),
                Csv(SbeBid),
                Csv(SbeAsk),
                Csv(SbeMid),
                Csv(SbeMidBpsFromStart),
                Csv(SbeEventUtc),
                Csv(SbeAgeMs),
                Csv(SbeError),
                Csv(JsonBid),
                Csv(JsonAsk),
                Csv(JsonMid),
                Csv(JsonMidBpsFromStart),
                Csv(JsonAgeMs),
                Csv(JsonError),
                Csv(PolymarketUpBid),
                Csv(PolymarketUpAsk),
                Csv(PolymarketUpMid),
                Csv(PolymarketUpImpliedMid),
                Csv(PolymarketUpRelativeBpsFromStart),
                Csv(PolymarketUpDeltaProbabilityBpsFromStart),
                Csv(PolymarketDownBid),
                Csv(PolymarketDownAsk),
                Csv(PolymarketDownMid),
                Csv(PolymarketOrderBookTimestampUtc),
                Csv(PolymarketAgeMs),
                Csv(PolymarketError));
        }

        private static decimal? RelativeBps(decimal? value, decimal? baseline)
        {
            return value.HasValue && baseline is > 0m
                ? (value.Value - baseline.Value) / baseline.Value * 10_000m
                : null;
        }

        private static decimal? DeltaProbabilityBps(decimal? value, decimal? baseline)
        {
            return value.HasValue && baseline.HasValue ? (value.Value - baseline.Value) * 10_000m : null;
        }
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out decimal number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String &&
               decimal.TryParse(property.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : null;
    }
}
