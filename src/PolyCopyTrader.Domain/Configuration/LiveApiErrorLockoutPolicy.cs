namespace PolyCopyTrader.Domain.Configuration;

public static class LiveApiErrorLockoutPolicy
{
    private static readonly string[] LiveOrderApiComponents =
    [
        "PolymarketClobPublicClient",
        "PolymarketTradingClient",
        "PolymarketGeoClient"
    ];

    public static bool CountsForLiveOrderLockout(ApiError error)
    {
        if (string.IsNullOrWhiteSpace(error.Component))
        {
            return false;
        }

        if (error.Component.StartsWith("PolymarketMarketWebSocket", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (LiveOrderRejectionClassifier.IsInsufficientBalanceOrAllowance(error.Message))
        {
            return false;
        }

        return LiveOrderApiComponents.Any(component =>
            error.Component.StartsWith(component, StringComparison.OrdinalIgnoreCase));
    }
}
