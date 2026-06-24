using System.Globalization;
using System.Text;
using System.Text.Json;

var options = MonitorOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
var capturedAt = DateTimeOffset.UtcNow;
var csvPath = Path.Combine(options.OutputDirectory, "samples.csv");
var summaryPath = Path.Combine(options.OutputDirectory, "summary.md");

using var client = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(8)
};
client.DefaultRequestHeaders.UserAgent.ParseAdd("PolyCopyTraderEthPremarketPriceMonitor/1.0");

var marketCache = new Dictionary<string, MarketInfo>(StringComparer.OrdinalIgnoreCase);
var samples = new List<Sample>();
var failures = new List<string>();

await using var csv = new StreamWriter(csvPath, append: false, Encoding.UTF8);
await csv.WriteLineAsync("captured_utc,slug,start_utc,seconds_before_start,down_token,best_bid,best_ask,spread,avg_fill_price,filled_notional_usd,filled_shares,levels_used,min_order_size,tick_size,last_trade_price,error");

var endUtc = capturedAt.AddSeconds(options.DurationSeconds);
Console.WriteLine($"Monitoring ETH Down CLOB books until {endUtc:O}. Output: {csvPath}");

while (DateTimeOffset.UtcNow < endUtc)
{
    var now = DateTimeOffset.UtcNow;
    foreach (var startUtc in GetCandidateStarts(now, options.LookaheadSeconds))
    {
        var slug = "eth-updown-5m-" + startUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var secondsBefore = (startUtc - now).TotalSeconds;
        if (secondsBefore < -2 || secondsBefore > options.LookaheadSeconds)
        {
            continue;
        }

        try
        {
            if (!marketCache.TryGetValue(slug, out var market))
            {
                market = await GetMarketAsync(client, slug);
                marketCache[slug] = market;
            }

            var book = await GetBookAsync(client, market.DownTokenId);
            var estimate = EstimateTakerBuy(book.Asks, options.TargetNotionalUsd, options.MaxAllowedPrice);
            var sample = new Sample(
                now,
                slug,
                startUtc,
                secondsBefore,
                market.DownTokenId,
                book.BestBid,
                book.BestAsk,
                book.Spread,
                estimate.AverageFillPrice,
                estimate.FilledNotionalUsd,
                estimate.FilledShares,
                estimate.LevelsUsed,
                book.MinOrderSize,
                book.TickSize,
                book.LastTradePrice,
                Error: null);
            samples.Add(sample);
            await WriteSampleAsync(csv, sample);

            if (secondsBefore <= options.ConsoleLookaheadSeconds)
            {
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{now:HH:mm:ss.fff} {slug} t-{secondsBefore,6:0.0}s ask={Format(book.BestAsk),5} vwap={Format(estimate.AverageFillPrice),8} bid={Format(book.BestBid),5} levels={estimate.LevelsUsed}"));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex.Message.ReplaceLineEndings(" ");
            failures.Add($"{now:O} {slug}: {message}");
            var sample = new Sample(
                now,
                slug,
                startUtc,
                secondsBefore,
                DownTokenId: "",
                BestBid: null,
                BestAsk: null,
                Spread: null,
                AverageFillPrice: null,
                FilledNotionalUsd: 0m,
                FilledShares: 0m,
                LevelsUsed: 0,
                MinOrderSize: null,
                TickSize: null,
                LastTradePrice: null,
                Error: message);
            samples.Add(sample);
            await WriteSampleAsync(csv, sample);
            Console.WriteLine($"{now:HH:mm:ss.fff} {slug} error={message}");
        }
    }

    await csv.FlushAsync();
    var delay = Math.Max(100, options.PollMilliseconds);
    await Task.Delay(delay);
}

await WriteSummaryAsync(summaryPath, samples, failures, options, capturedAt, DateTimeOffset.UtcNow);
Console.WriteLine($"Summary: {summaryPath}");

static IEnumerable<DateTimeOffset> GetCandidateStarts(DateTimeOffset now, int lookaheadSeconds)
{
    var unix = now.ToUnixTimeSeconds();
    var baseStart = unix - unix % 300;
    for (var offset = 0; offset <= lookaheadSeconds + 300; offset += 300)
    {
        yield return DateTimeOffset.FromUnixTimeSeconds(baseStart + offset);
    }
}

static async Task<MarketInfo> GetMarketAsync(HttpClient client, string slug)
{
    var url = "https://gamma-api.polymarket.com/markets?active=true&closed=false&limit=1&slug=" +
        Uri.EscapeDataString(slug);
    using var response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var json = await JsonDocument.ParseAsync(stream);
    var root = json.RootElement;
    var market = root.ValueKind == JsonValueKind.Array
        ? root.EnumerateArray().FirstOrDefault()
        : root;
    if (market.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
    {
        throw new InvalidOperationException("market_not_found");
    }

    var outcomes = GetStringArray(market, "outcomes");
    var tokenIds = GetStringArray(market, "clobTokenIds");
    var downIndex = Array.FindIndex(outcomes, item => string.Equals(item, "Down", StringComparison.OrdinalIgnoreCase));
    if (downIndex < 0 || downIndex >= tokenIds.Length)
    {
        throw new InvalidOperationException("down_token_not_found");
    }

    return new MarketInfo(slug, tokenIds[downIndex]);
}

static async Task<Book> GetBookAsync(HttpClient client, string tokenId)
{
    var url = "https://clob.polymarket.com/book?token_id=" + Uri.EscapeDataString(tokenId);
    using var response = await client.GetAsync(url);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var json = await JsonDocument.ParseAsync(stream);
    var root = json.RootElement;
    var bids = GetLevels(root, "bids").ToArray();
    var asks = GetLevels(root, "asks").ToArray();
    var bestBid = bids.Length == 0 ? (decimal?)null : bids.Max(level => level.Price);
    var bestAsk = asks.Length == 0 ? (decimal?)null : asks.Min(level => level.Price);
    return new Book(
        bids,
        asks,
        bestBid,
        bestAsk,
        bestBid is not null && bestAsk is not null ? bestAsk.Value - bestBid.Value : null,
        GetDecimalOrNull(root, "min_order_size"),
        GetDecimalOrNull(root, "tick_size"),
        GetDecimalOrNull(root, "last_trade_price"));
}

static FillEstimate EstimateTakerBuy(IReadOnlyList<Level> asks, decimal targetNotionalUsd, decimal maxAllowedPrice)
{
    var filledShares = 0m;
    var totalCost = 0m;
    var levelsUsed = 0;
    foreach (var ask in asks.Where(level => level.Size > 0m && level.Price > 0m).OrderBy(level => level.Price))
    {
        if (ask.Price > maxAllowedPrice)
        {
            break;
        }

        var remainingNotional = targetNotionalUsd - totalCost;
        if (remainingNotional <= 0.00000001m)
        {
            break;
        }

        var availableNotional = ask.Price * ask.Size;
        var takeNotional = Math.Min(remainingNotional, availableNotional);
        if (takeNotional <= 0m)
        {
            continue;
        }

        filledShares += takeNotional / ask.Price;
        totalCost += takeNotional;
        levelsUsed++;
    }

    return filledShares <= 0m
        ? new FillEstimate(null, totalCost, filledShares, levelsUsed)
        : new FillEstimate(totalCost / filledShares, totalCost, filledShares, levelsUsed);
}

static Level[] GetLevels(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var levels) || levels.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    return levels
        .EnumerateArray()
        .Select(level => new Level(
            GetDecimal(level, "price"),
            GetDecimal(level, "size")))
        .Where(level => level.Price > 0m && level.Size > 0m)
        .ToArray();
}

static string[] GetStringArray(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var value))
    {
        return [];
    }

    if (value.ValueKind == JsonValueKind.Array)
    {
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    if (value.ValueKind == JsonValueKind.String && value.GetString() is { } raw)
    {
        using var nested = JsonDocument.Parse(raw);
        return nested.RootElement.ValueKind == JsonValueKind.Array
            ? nested.RootElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray()
            : [];
    }

    return [];
}

static decimal GetDecimal(JsonElement root, string propertyName)
{
    return GetDecimalOrNull(root, propertyName) ?? 0m;
}

static decimal? GetDecimalOrNull(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var value))
    {
        return null;
    }

    return value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out var numeric) => numeric,
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text) => text,
        _ => null
    };
}

static async Task WriteSampleAsync(StreamWriter writer, Sample sample)
{
    await writer.WriteLineAsync(string.Join(
        ',',
        Csv(sample.CapturedUtc.ToString("O", CultureInfo.InvariantCulture)),
        Csv(sample.Slug),
        Csv(sample.StartUtc.ToString("O", CultureInfo.InvariantCulture)),
        sample.SecondsBeforeStart.ToString("0.###", CultureInfo.InvariantCulture),
        Csv(sample.DownTokenId),
        Number(sample.BestBid),
        Number(sample.BestAsk),
        Number(sample.Spread),
        Number(sample.AverageFillPrice),
        sample.FilledNotionalUsd.ToString("0.########", CultureInfo.InvariantCulture),
        sample.FilledShares.ToString("0.########", CultureInfo.InvariantCulture),
        sample.LevelsUsed.ToString(CultureInfo.InvariantCulture),
        Number(sample.MinOrderSize),
        Number(sample.TickSize),
        Number(sample.LastTradePrice),
        Csv(sample.Error ?? "")));
}

static async Task WriteSummaryAsync(
    string path,
    IReadOnlyList<Sample> samples,
    IReadOnlyList<string> failures,
    MonitorOptions options,
    DateTimeOffset startedUtc,
    DateTimeOffset finishedUtc)
{
    await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
    await writer.WriteLineAsync("# ETH 14 FAK Premarket Down Price Monitor");
    await writer.WriteLineAsync();
    await writer.WriteLineAsync($"Started UTC: `{startedUtc:O}`");
    await writer.WriteLineAsync($"Finished UTC: `{finishedUtc:O}`");
    await writer.WriteLineAsync($"Target notional USD: `{options.TargetNotionalUsd.ToString(CultureInfo.InvariantCulture)}`");
    await writer.WriteLineAsync($"Threshold: `{options.ThresholdPrice.ToString(CultureInfo.InvariantCulture)}`");
    await writer.WriteLineAsync();
    await writer.WriteLineAsync("| Market | Samples | Window Sec Before | Last VWAP <= Threshold | First VWAP > Threshold After Safe | Min VWAP | Max VWAP | Min Ask | Max Ask |");
    await writer.WriteLineAsync("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var group in samples
        .Where(sample => sample.AverageFillPrice is not null)
        .GroupBy(sample => sample.Slug)
        .OrderBy(group => group.Key))
    {
        var ordered = group.OrderByDescending(sample => sample.SecondsBeforeStart).ToArray();
        var safe = ordered.Where(sample => sample.AverageFillPrice <= options.ThresholdPrice).OrderBy(sample => sample.SecondsBeforeStart).FirstOrDefault();
        Sample? firstAboveAfterSafe = null;
        if (safe is not null)
        {
            firstAboveAfterSafe = ordered
                .Where(sample => sample.SecondsBeforeStart < safe.SecondsBeforeStart && sample.AverageFillPrice > options.ThresholdPrice)
                .OrderByDescending(sample => sample.SecondsBeforeStart)
                .FirstOrDefault();
        }
        else
        {
            firstAboveAfterSafe = ordered
                .Where(sample => sample.AverageFillPrice > options.ThresholdPrice)
                .OrderByDescending(sample => sample.SecondsBeforeStart)
                .FirstOrDefault();
        }

        var minSec = group.Min(sample => sample.SecondsBeforeStart);
        var maxSec = group.Max(sample => sample.SecondsBeforeStart);
        await writer.WriteLineAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"| `{group.Key}` | {group.Count()} | {maxSec:0.0}..{minSec:0.0} | {FormatSeconds(safe)} | {FormatSeconds(firstAboveAfterSafe)} | {group.Min(sample => sample.AverageFillPrice):0.########} | {group.Max(sample => sample.AverageFillPrice):0.########} | {group.Min(sample => sample.BestAsk):0.########} | {group.Max(sample => sample.BestAsk):0.########} |"));
    }

    if (failures.Count > 0)
    {
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("## Failures");
        foreach (var failure in failures.Take(20))
        {
            await writer.WriteLineAsync("- " + failure);
        }
    }
}

static string FormatSeconds(Sample? sample)
{
    return sample is null
        ? ""
        : sample.SecondsBeforeStart.ToString("0.0", CultureInfo.InvariantCulture) + "s @ " +
            sample.AverageFillPrice?.ToString("0.########", CultureInfo.InvariantCulture);
}

static string Format(decimal? value)
{
    return value?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";
}

static string Number(decimal? value)
{
    return value?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";
}

static string Csv(string value)
{
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

sealed record MonitorOptions(
    int DurationSeconds,
    int PollMilliseconds,
    int LookaheadSeconds,
    int ConsoleLookaheadSeconds,
    decimal TargetNotionalUsd,
    decimal MaxAllowedPrice,
    decimal ThresholdPrice,
    string OutputDirectory)
{
    public static MonitorOptions Parse(string[] args)
    {
        var map = args
            .Where(arg => arg.StartsWith("--", StringComparison.Ordinal))
            .Select(arg =>
            {
                var index = arg.IndexOf('=', StringComparison.Ordinal);
                return index < 0
                    ? (Key: arg[2..], Value: "true")
                    : (Key: arg[2..index], Value: arg[(index + 1)..]);
            })
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

        return new MonitorOptions(
            DurationSeconds: GetInt(map, "duration-seconds", 600),
            PollMilliseconds: GetInt(map, "poll-ms", 1000),
            LookaheadSeconds: GetInt(map, "lookahead-seconds", 360),
            ConsoleLookaheadSeconds: GetInt(map, "console-lookahead-seconds", 180),
            TargetNotionalUsd: GetDecimalArg(map, "target-notional", 6.0093m),
            MaxAllowedPrice: GetDecimalArg(map, "max-allowed-price", 0.99m),
            ThresholdPrice: GetDecimalArg(map, "threshold", 0.52m),
            OutputDirectory: map.TryGetValue("output", out var output) && !string.IsNullOrWhiteSpace(output)
                ? output
                : "artifacts/eth-14-premarket-price-monitor-2026-06-21");
    }

    private static int GetInt(IReadOnlyDictionary<string, string> map, string key, int fallback)
    {
        return map.TryGetValue(key, out var raw) &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static decimal GetDecimalArg(IReadOnlyDictionary<string, string> map, string key, decimal fallback)
    {
        return map.TryGetValue(key, out var raw) &&
            decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}

sealed record MarketInfo(string Slug, string DownTokenId);

sealed record Level(decimal Price, decimal Size);

sealed record Book(
    IReadOnlyList<Level> Bids,
    IReadOnlyList<Level> Asks,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    decimal? MinOrderSize,
    decimal? TickSize,
    decimal? LastTradePrice);

sealed record FillEstimate(
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal FilledShares,
    int LevelsUsed);

sealed record Sample(
    DateTimeOffset CapturedUtc,
    string Slug,
    DateTimeOffset StartUtc,
    double SecondsBeforeStart,
    string DownTokenId,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    decimal? AverageFillPrice,
    decimal FilledNotionalUsd,
    decimal FilledShares,
    int LevelsUsed,
    decimal? MinOrderSize,
    decimal? TickSize,
    decimal? LastTradePrice,
    string? Error);
