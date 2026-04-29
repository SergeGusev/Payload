using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Strategy;

public sealed class DefaultRiskEngine(
    RiskOptions riskOptions,
    PaperTradingOptions paperTradingOptions) : IRiskEngine
{
    public RiskDecision Evaluate(ProposedOrderIntent proposedOrder, ExposureSnapshot exposure)
    {
        var reasons = new List<string>();
        var bankroll = paperTradingOptions.InitialBankrollUsd;
        var notional = proposedOrder.NotionalUsd;
        var exposureAfterOrder = exposure.TotalDeployedUsd + notional;

        if (exposure.OpenOrdersCount >= riskOptions.MaxOpenOrders)
        {
            reasons.Add(SignalReasonCodes.RiskOpenOrdersLimit);
        }

        if (exposure.OldestOpenOrderAgeSeconds > riskOptions.MaxOrderAgeSeconds)
        {
            reasons.Add(SignalReasonCodes.RiskOrderAgeLimit);
        }

        if (notional > Budget(riskOptions.MaxTradeBankrollPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskTradeLimit);
        }

        if (exposure.MarketExposureUsd + notional > Budget(riskOptions.MaxMarketBankrollPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskMarketLimit);
        }

        if (exposure.TraderExposureUsd + notional > Budget(riskOptions.MaxTraderBankrollPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskTraderLimit);
        }

        if (exposure.CategoryExposureUsd + notional > Budget(riskOptions.MaxCategoryBankrollPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskCategoryLimit);
        }

        if (exposureAfterOrder > Budget(riskOptions.MaxTotalDeployedPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskTotalDeployedLimit);
        }

        if (exposure.DailyLossUsd > Budget(riskOptions.MaxDailyLossPct, bankroll))
        {
            reasons.Add(SignalReasonCodes.RiskDailyLossLimit);
        }

        if (reasons.Count > 0)
        {
            return new RiskDecision(false, reasons, 0m, exposureAfterOrder);
        }

        return new RiskDecision(
            true,
            [],
            proposedOrder.NotionalUsd,
            exposureAfterOrder,
            proposedOrder.SizeShares);
    }

    private static decimal Budget(decimal pct, decimal bankroll)
    {
        return bankroll * pct / 100m;
    }
}
