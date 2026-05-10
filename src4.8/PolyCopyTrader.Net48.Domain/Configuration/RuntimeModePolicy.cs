using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Domain.Configuration;

public static class RuntimeModePolicy
{
    public static bool IsPaperTradingEnabled(BotOptions botOptions, PaperTradingOptions paperTradingOptions)
    {
        if (botOptions is null)
        {
            throw new ArgumentNullException(nameof(botOptions));
        }

        if (paperTradingOptions is null)
        {
            throw new ArgumentNullException(nameof(paperTradingOptions));
        }

        return botOptions.Mode == BotMode.Paper ||
            botOptions.Mode == BotMode.Live && paperTradingOptions.RunInLiveMode;
    }

    public static string PaperTradingDisabledReason(BotOptions botOptions, PaperTradingOptions paperTradingOptions)
    {
        if (botOptions is null)
        {
            throw new ArgumentNullException(nameof(botOptions));
        }

        if (paperTradingOptions is null)
        {
            throw new ArgumentNullException(nameof(paperTradingOptions));
        }

        return botOptions.Mode == BotMode.Live
            ? "Bot mode is Live, but PaperTrading:RunInLiveMode is false."
            : $"Bot mode is {botOptions.Mode}; paper trading requires Paper mode or Live mode with PaperTrading:RunInLiveMode=true.";
    }
}
