using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

var repository = new PostgresAppRepository(new PostgresConnectionFactory(new StorageOptions()));
var markets = await repository.GetBtcUpDown5mGammaMarketsAsync(20);
Console.WriteLine("btc_gamma_markets=" + markets.Count);
foreach (var market in markets.Take(5))
{
    Console.WriteLine("market=" + market.Slug + " active=" + market.Active + " closed=" + market.Closed + " archived=" + market.Archived + " start=" + market.EventStartTimeUtc?.ToString("O") + " end=" + market.EndDateUtc?.ToString("O") + " outcomes=" + string.Join("/", market.Outcomes) + " tokens=" + market.ClobTokenIds.Count);
}

var ticks = (await repository.GetRecentBtcUpDown5mOddsTicksAsync(500)).ToArray();

Console.WriteLine("recent_rows=" + ticks.Length);
if (ticks.Length == 0)
{
    return;
}

var latest = ticks[0];
Console.WriteLine("latest_sampled_utc=" + latest.SampledAtUtc.ToString("O"));
Console.WriteLine("latest_market=" + latest.MarketSlug);
Console.WriteLine("latest_binance=" + latest.BinancePriceUsd);
Console.WriteLine("latest_start=" + latest.BinanceStartPriceUsd);
Console.WriteLine("latest_move_usd=" + latest.BtcMoveFromStartUsd);
Console.WriteLine("latest_up_proxy=" + latest.UpPriceProxy);
Console.WriteLine("latest_up_proxy_kind=" + latest.UpPriceProxyKind);
Console.WriteLine("latest_up_source=" + latest.UpBookSource);
Console.WriteLine("latest_down_proxy=" + latest.DownPriceProxy);
Console.WriteLine("latest_down_proxy_kind=" + latest.DownPriceProxyKind);
Console.WriteLine("latest_down_source=" + latest.DownBookSource);

var withUp = ticks.Where(tick => tick.UpPriceProxy is not null).ToArray();
Console.WriteLine("recent_with_up_proxy=" + withUp.Length);
Console.WriteLine("markets=" + ticks.Select(tick => tick.MarketId).Distinct(StringComparer.OrdinalIgnoreCase).Count());

var binanceStrategy = StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mBinanceCode);
var binanceRuns = await repository.GetRecentStrategyMarketPaperRunsAsync(binanceStrategy.Id, StrategyMarketPaperRunStatuses.Entered, 10);
var binanceObserved = await repository.GetRecentStrategyMarketPaperRunsAsync(binanceStrategy.Id, StrategyMarketPaperRunStatuses.Observed, 10);
var binanceSkipped = await repository.GetRecentStrategyMarketPaperRunsAsync(binanceStrategy.Id, StrategyMarketPaperRunStatuses.Skipped, 10);
Console.WriteLine("binance_entered_runs=" + binanceRuns.Count);
Console.WriteLine("binance_observed_runs=" + binanceObserved.Count);
Console.WriteLine("binance_skipped_runs=" + binanceSkipped.Count);
foreach (var run in binanceRuns.Take(3))
{
    Console.WriteLine("binance_entered market=" + run.MarketSlug + " outcome=" + run.SelectedOutcome + " price=" + run.EntryPrice + " stake=" + run.StakeUsd + " entered=" + run.EnteredAtUtc?.ToString("O"));
}
