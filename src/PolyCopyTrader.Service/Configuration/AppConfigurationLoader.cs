using Microsoft.Extensions.Configuration;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.Configuration;

public static class AppConfigurationLoader
{
    public static AppConfiguration Load(IConfiguration configuration)
    {
        var watchlist = configuration.GetSection("Watchlist").Get<WatchlistOptions>();
        if (watchlist is null)
        {
            var legacyWatchlist = configuration.GetSection("Watchlist").Get<List<TraderRuleOptions>>() ?? [];
            watchlist = new WatchlistOptions { Traders = legacyWatchlist };
        }

        return new AppConfiguration
        {
            Bot = configuration.GetSection("Bot").Get<BotOptions>() ?? new BotOptions(),
            Risk = configuration.GetSection("Risk").Get<RiskOptions>() ?? new RiskOptions(),
            Execution = configuration.GetSection("Execution").Get<ExecutionOptions>() ?? new ExecutionOptions(),
            Signal = configuration.GetSection("Signal").Get<SignalOptions>() ?? new SignalOptions(),
            Polymarket = configuration.GetSection("Polymarket").Get<PolymarketOptions>() ?? new PolymarketOptions(),
            Watchlist = watchlist,
            PaperTrading = configuration.GetSection("PaperTrading").Get<PaperTradingOptions>() ?? new PaperTradingOptions(),
            Dashboard = configuration.GetSection("Dashboard").Get<DashboardOptions>() ?? new DashboardOptions(),
            Storage = configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions()
        };
    }
}
