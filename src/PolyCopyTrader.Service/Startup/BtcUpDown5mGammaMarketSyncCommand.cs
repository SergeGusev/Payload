using System.Globalization;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.Startup;

public static class BtcUpDown5mGammaMarketSyncCommand
{
    private const int DefaultLookBehindWindows = 1;
    private const int DefaultLookAheadWindows = 24;
    private const int MaxLookAroundWindows = 576;
    private const int SlugBatchSize = 50;

    public static async Task<int> ExecuteAsync(
        AppConfiguration configuration,
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        var repository = new PostgresAppRepository(new PostgresConnectionFactory(configuration.Storage));
        using var httpClient = new HttpClient();
        var gammaClient = new PolymarketGammaClient(
            httpClient,
            configuration.Polymarket,
            new NullPolymarketApiErrorSink(),
            new NullPolymarketHttpLogSink());

        var nowUtc = DateTimeOffset.UtcNow;
        var lookBehindWindows = ParseIntOption(args, "--btc-5m-sync-lookbehind-windows", DefaultLookBehindWindows);
        var lookAheadWindows = ParseIntOption(args, "--btc-5m-sync-lookahead-windows", DefaultLookAheadWindows);
        if (lookBehindWindows < 0 || lookBehindWindows > MaxLookAroundWindows)
        {
            await output.WriteLineAsync($"--btc-5m-sync-lookbehind-windows must be between 0 and {MaxLookAroundWindows}.");
            return 1;
        }

        if (lookAheadWindows < 0 || lookAheadWindows > MaxLookAroundWindows)
        {
            await output.WriteLineAsync($"--btc-5m-sync-lookahead-windows must be between 0 and {MaxLookAroundWindows}.");
            return 1;
        }

        var slugs = BtcUpDown5mMarketAnalyzer.BuildFiveMinuteSlugs(
            nowUtc,
            lookBehindWindows,
            lookAheadWindows);
        var markets = new List<PolyCopyTrader.Domain.PolymarketGammaMarket>();
        for (var offset = 0; offset < slugs.Count; offset += SlugBatchSize)
        {
            var batch = slugs.Skip(offset).Take(SlugBatchSize).ToArray();
            markets.AddRange(await gammaClient.GetMarketsBySlugsAsync(batch, activeOnly: true, cancellationToken));
        }

        var btcMarkets = markets
            .Where(BtcUpDown5mMarketAnalyzer.IsCandidate)
            .DistinctBy(market => market.MarketId)
            .ToArray();

        foreach (var market in btcMarkets)
        {
            await repository.UpsertPolymarketGammaMarketAsync(market, cancellationToken);
        }

        await output.WriteLineAsync($"BTC 5m priority sync UTC: {nowUtc:O}");
        await output.WriteLineAsync($"Slugs requested: {slugs.Count}");
        await output.WriteLineAsync($"Look-behind windows: {lookBehindWindows}");
        await output.WriteLineAsync($"Look-ahead windows: {lookAheadWindows}");
        await output.WriteLineAsync($"Markets fetched: {markets.Count}");
        await output.WriteLineAsync($"BTC 5m markets upserted: {btcMarkets.Length}");
        foreach (var market in btcMarkets.OrderBy(market => BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)))
        {
            await output.WriteLineAsync(
                $"Market {market.Slug}; id={market.MarketId}; start={BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market):O}; end={market.EndDateUtc:O}; active={market.Active}; closed={market.Closed}; accepting={market.AcceptingOrders}");
        }

        return btcMarkets.Length > 0 ? 0 : 1;
    }

    private static int ParseIntOption(string[] args, string name, int defaultValue)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length ||
                !int.TryParse(args[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return -1;
            }

            return value;
        }

        return defaultValue;
    }
}
