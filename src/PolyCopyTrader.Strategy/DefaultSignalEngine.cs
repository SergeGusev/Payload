using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Strategy;

public sealed class DefaultSignalEngine(
    SignalOptions signalOptions,
    ExecutionOptions executionOptions,
    RiskOptions riskOptions,
    PaperTradingOptions paperTradingOptions,
    IRiskEngine riskEngine) : ISignalEngine
{
    public SignalDecision Evaluate(SignalEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var now = DateTimeOffset.UtcNow;
        var trade = context.LeaderTrade;
        var traderRule = context.TraderRule;
        var category = context.MarketInfo?.Category;

        if (!traderRule.Enabled)
        {
            return Reject(SignalReasonCodes.TraderDisabled, now);
        }

        if (!IsCategoryAllowed(traderRule, category))
        {
            return Reject(SignalReasonCodes.CategoryNotAllowed, now);
        }

        if (trade.Side != TradeSide.Buy)
        {
            return Reject(SignalReasonCodes.UnsupportedSide, now);
        }

        var age = now - trade.TimestampUtc;
        if (age > TimeSpan.FromSeconds(traderRule.MaxLagSeconds))
        {
            return Reject(SignalReasonCodes.TradeTooOld, now);
        }

        var minLeaderTradeUsd = Math.Max(traderRule.MinLeaderTradeUsd, executionOptions.MinLeaderTradeUsd);
        if (trade.CashValueUsd < minLeaderTradeUsd)
        {
            return Reject(SignalReasonCodes.LeaderTradeTooSmall, now);
        }

        if (context.MarketInfo?.EndDateUtc is { } endDate &&
            endDate <= now.AddMinutes(signalOptions.MarketCloseWindowMinutes))
        {
            return Reject(SignalReasonCodes.MarketTooCloseToEvent, now);
        }

        var orderBook = context.OrderBookSnapshot;
        if (orderBook?.BestBid is not { } bestBid || orderBook.BestAsk is not { } bestAsk)
        {
            return Reject(SignalReasonCodes.MissingOrderBook, now);
        }

        var maxSpreadAbs = Math.Min(traderRule.MaxSpreadCents, executionOptions.MaxSpreadCents) / 100m;
        if (orderBook.SpreadAbs is not { } spreadAbs || spreadAbs > maxSpreadAbs)
        {
            return Reject(SignalReasonCodes.SpreadTooWideAbs, now);
        }

        var maxSpreadPct = Math.Min(traderRule.MaxSpreadPct, executionOptions.MaxSpreadPct);
        if (orderBook.SpreadPct is not { } spreadPct || spreadPct > maxSpreadPct)
        {
            return Reject(SignalReasonCodes.SpreadTooWidePct, now);
        }

        var tickSize = orderBook.TickSize is > 0m ? orderBook.TickSize.Value : 0.01m;
        var maxEntry = trade.Price + Math.Min(traderRule.MaxSlippageCents, executionOptions.MaxSlippageCents) / 100m;
        if (bestAsk > maxEntry + tickSize)
        {
            return Reject(SignalReasonCodes.PriceMovedTooFar, now);
        }

        var proposedPrice = MakerPriceCalculator.Calculate(bestBid, bestAsk, tickSize, maxEntry);
        if (proposedPrice <= 0m || proposedPrice >= bestAsk)
        {
            return Reject(SignalReasonCodes.NoSafeMakerPrice, now);
        }

        if (proposedPrice > maxEntry)
        {
            return Reject(SignalReasonCodes.PriceMovedTooFar, now);
        }

        var score = Score(context, proposedPrice, age, spreadAbs, spreadPct, maxSpreadAbs, maxSpreadPct);
        if (score < signalOptions.IgnoreBelowScore)
        {
            return Reject(SignalReasonCodes.ScoreBelowThreshold, now, score);
        }

        if (score < signalOptions.ObserveBelowScore)
        {
            return Reject(SignalReasonCodes.ObserveOnly, now, score);
        }

        var proposedNotional = ProposedNotional(score);
        var proposedSize = proposedNotional / proposedPrice;
        var riskDecision = riskEngine.Evaluate(
            new ProposedOrderIntent(
                trade.TraderWallet,
                trade.ConditionId,
                trade.AssetId,
                category,
                trade.Side,
                proposedPrice,
                proposedSize,
                proposedNotional),
            context.Exposure);

        if (!riskDecision.Allowed)
        {
            return new SignalDecision(
                false,
                score,
                riskDecision.ReasonCodes[0],
                riskDecision.ReasonCodes,
                proposedPrice,
                proposedSize,
                proposedNotional,
                now);
        }

        var decisionCode = score >= signalOptions.NormalPaperOrderScore
            ? "paper_order_normal"
            : "paper_order_small";

        return new SignalDecision(
            true,
            score,
            decisionCode,
            [],
            proposedPrice,
            riskDecision.AllowedSizeShares,
            riskDecision.AllowedNotionalUsd,
            now);
    }

    private int Score(
        SignalEvaluationContext context,
        decimal proposedPrice,
        TimeSpan age,
        decimal spreadAbs,
        decimal spreadPct,
        decimal maxSpreadAbs,
        decimal maxSpreadPct)
    {
        var score = 0;
        var trade = context.LeaderTrade;

        if (IsCategoryAllowed(context.TraderRule, context.MarketInfo?.Category))
        {
            score += signalOptions.CategoryAllowedScore;
        }

        if (age < TimeSpan.FromSeconds(10))
        {
            score += signalOptions.AgeUnder10SecondsScore;
        }
        else if (age < TimeSpan.FromSeconds(60))
        {
            score += signalOptions.AgeUnder60SecondsScore;
        }
        else if (age < TimeSpan.FromMinutes(5))
        {
            score += signalOptions.AgeUnder5MinutesScore;
        }

        var entryDelta = proposedPrice - trade.Price;
        if (entryDelta <= 0.005m)
        {
            score += signalOptions.EntryWithinHalfCentScore;
        }
        else if (entryDelta <= 0.01m)
        {
            score += signalOptions.EntryWithinOneCentScore;
        }
        else if (entryDelta <= 0.02m)
        {
            score += signalOptions.EntryWithinTwoCentsScore;
        }

        var largeTradeThreshold = Math.Max(
            context.TraderRule.MinLeaderTradeUsd,
            executionOptions.MinLeaderTradeUsd) * signalOptions.LargeLeaderTradeMultiplier;
        if (trade.CashValueUsd >= largeTradeThreshold)
        {
            score += signalOptions.LargeLeaderTradeScore;
        }

        if (context.OrderBookSnapshot?.HasEnoughDepth == true && context.OrderBookSnapshot.IsCrossed == false)
        {
            score += signalOptions.DepthAcceptableScore;
        }

        if (context.MarketInfo?.EndDateUtc is { } endDate && endDate > DateTimeOffset.UtcNow.AddDays(1))
        {
            score += signalOptions.SlowMarketScore;
        }

        if (spreadAbs > maxSpreadAbs * 0.75m || spreadPct > maxSpreadPct * 0.75m)
        {
            score -= signalOptions.BorderlineSpreadPenalty;
        }

        return score;
    }

    private decimal ProposedNotional(int score)
    {
        var maxTradeNotional = paperTradingOptions.InitialBankrollUsd * riskOptions.MaxTradeBankrollPct / 100m;
        return score >= signalOptions.NormalPaperOrderScore
            ? maxTradeNotional
            : maxTradeNotional / 2m;
    }

    private static bool IsCategoryAllowed(TraderRule traderRule, string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || traderRule.AllowedCategories.Count == 0)
        {
            return true;
        }

        return traderRule.AllowedCategories.Any(
            allowed => string.Equals(allowed, category, StringComparison.OrdinalIgnoreCase));
    }

    private static SignalDecision Reject(string reasonCode, DateTimeOffset now, int score = 0)
    {
        return new SignalDecision(false, score, reasonCode, [reasonCode], null, null, null, now);
    }
}
