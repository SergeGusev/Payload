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

        if (signalOptions.RequireKnownMarketCategory && IsUnknownCategory(category))
        {
            return Reject(SignalReasonCodes.MissingMarketCategory, now);
        }

        if (EvaluateLeaderCategoryPerformance(context, category, now) is { } categoryPerformanceRejection)
        {
            return categoryPerformanceRejection;
        }

        if (EvaluateCopiedTraderPerformance(context, category, now) is { } copiedTraderPerformanceRejection)
        {
            return copiedTraderPerformanceRejection;
        }

        if (trade.Side is not (TradeSide.Buy or TradeSide.Sell))
        {
            return Reject(SignalReasonCodes.UnsupportedSide, now);
        }

        if (trade.Side == TradeSide.Sell && context.AvailablePositionSizeShares is not > 0m)
        {
            return Reject(SignalReasonCodes.NoPaperPositionToSell, now);
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
        if (orderBook?.BestBid is null || orderBook.BestAsk is null)
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

        var proposedPrice = ProposedLeaderPrice(trade.Price, now);
        if (!proposedPrice.Accepted)
        {
            return proposedPrice.Decision!;
        }

        var score = Score(context, proposedPrice.Price, age, spreadAbs, spreadPct, maxSpreadAbs, maxSpreadPct);
        if (score < signalOptions.IgnoreBelowScore)
        {
            return Reject(SignalReasonCodes.ScoreBelowThreshold, now, score);
        }

        if (score < signalOptions.ObserveBelowScore)
        {
            return Reject(SignalReasonCodes.ObserveOnly, now, score);
        }

        var marketMinOrderSize = orderBook.MinOrderSize is > 0m ? orderBook.MinOrderSize.Value : 1m;
        var proposedSize = ProposedSize(score, proposedPrice.Price, marketMinOrderSize);
        var proposedNotional = proposedSize * proposedPrice.Price;
        if (trade.Side == TradeSide.Sell)
        {
            if (paperTradingOptions.UseMinimumMarketOrderSize &&
                (context.AvailablePositionSizeShares ?? 0m) < marketMinOrderSize)
            {
                return Reject(SignalReasonCodes.PaperPositionBelowMarketMinimum, now, score);
            }

            proposedSize = Math.Min(proposedSize, context.AvailablePositionSizeShares ?? 0m);
            proposedNotional = proposedSize * proposedPrice.Price;
            if (proposedSize <= 0m || proposedNotional <= 0m)
            {
                return Reject(SignalReasonCodes.NoPaperPositionToSell, now, score);
            }
        }

        var riskDecision = riskEngine.Evaluate(
            new ProposedOrderIntent(
                trade.TraderWallet,
                trade.ConditionId,
                trade.AssetId,
                category,
                trade.Side,
                proposedPrice.Price,
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
                proposedPrice.Price,
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
            proposedPrice.Price,
            riskDecision.AllowedSizeShares,
            riskDecision.AllowedNotionalUsd,
            now);
    }

    private static PriceDecision ProposedLeaderPrice(decimal leaderPrice, DateTimeOffset now)
    {
        return leaderPrice <= 0m || leaderPrice > 1m
            ? PriceDecision.Reject(SignalReasonCodes.NoSafeMakerPrice, now)
            : PriceDecision.Accept(leaderPrice);
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

        var entryDelta = trade.Side == TradeSide.Sell
            ? trade.Price - proposedPrice
            : proposedPrice - trade.Price;
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

        if (HasUsableLeaderCategoryPerformance(context.LeaderCategoryPerformance, context.MarketInfo?.Category, DateTimeOffset.UtcNow))
        {
            score += signalOptions.LeaderCategoryPerformanceScore;
        }

        if (HasUsableCopiedTraderPerformance(
                context.CopiedTraderCategoryPerformance,
                trade.TraderWallet,
                context.MarketInfo?.Category) ||
            HasUsableCopiedTraderPerformance(
                context.CopiedTraderOverallPerformance,
                trade.TraderWallet,
                "OVERALL"))
        {
            score += signalOptions.CopiedTraderPerformanceScore;
        }

        if (spreadAbs > maxSpreadAbs * 0.75m || spreadPct > maxSpreadPct * 0.75m)
        {
            score -= signalOptions.BorderlineSpreadPenalty;
        }

        return score;
    }

    private SignalDecision? EvaluateLeaderCategoryPerformance(
        SignalEvaluationContext context,
        string? category,
        DateTimeOffset now)
    {
        if (!signalOptions.RequireLeaderCategoryPerformance)
        {
            return null;
        }

        if (IsUnknownCategory(category))
        {
            return Reject(SignalReasonCodes.MissingMarketCategory, now);
        }

        var performance = context.LeaderCategoryPerformance;
        if (performance is null ||
            !string.Equals(performance.Wallet, context.LeaderTrade.TraderWallet, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(performance.Category, category, StringComparison.OrdinalIgnoreCase))
        {
            return Reject(SignalReasonCodes.MissingLeaderCategoryPerformance, now);
        }

        if (IsPerformanceStale(performance, now))
        {
            return Reject(SignalReasonCodes.LeaderCategoryPerformanceStale, now);
        }

        if (performance.ResolvedPositions < signalOptions.MinLeaderCategoryResolvedPositions)
        {
            return Reject(SignalReasonCodes.LeaderCategoryResolvedSampleTooSmall, now);
        }

        if (SampleQualityRank(performance.SampleQuality) < SampleQualityRank(signalOptions.MinLeaderCategorySampleQuality))
        {
            return Reject(SignalReasonCodes.LeaderCategorySampleQualityTooLow, now);
        }

        if (performance.Score < signalOptions.MinLeaderCategoryScore)
        {
            return Reject(SignalReasonCodes.LeaderCategoryScoreTooLow, now);
        }

        if (performance.ResolvedRoiPct < signalOptions.MinLeaderCategoryResolvedRoiPct)
        {
            return Reject(SignalReasonCodes.LeaderCategoryRoiTooLow, now);
        }

        if (performance.WinRatePct < signalOptions.MinLeaderCategoryWinRatePct)
        {
            return Reject(SignalReasonCodes.LeaderCategoryWinRateTooLow, now);
        }

        return null;
    }

    private SignalDecision? EvaluateCopiedTraderPerformance(
        SignalEvaluationContext context,
        string? category,
        DateTimeOffset now)
    {
        if (!signalOptions.CopiedTraderPerformanceGuardEnabled)
        {
            return null;
        }

        if (IsWeakCopiedTraderPerformance(
                context.CopiedTraderCategoryPerformance,
                context.LeaderTrade.TraderWallet,
                category))
        {
            return Reject(SignalReasonCodes.CopiedTraderCategoryPerformanceTooWeak, now);
        }

        if (IsWeakCopiedTraderPerformance(
                context.CopiedTraderOverallPerformance,
                context.LeaderTrade.TraderWallet,
                "OVERALL"))
        {
            return Reject(SignalReasonCodes.CopiedTraderPerformanceTooWeak, now);
        }

        return null;
    }

    private bool HasUsableLeaderCategoryPerformance(
        PolymarketOnChainWalletCategoryPerformance? performance,
        string? category,
        DateTimeOffset now)
    {
        return performance is not null &&
            !IsUnknownCategory(category) &&
            string.Equals(performance.Category, category, StringComparison.OrdinalIgnoreCase) &&
            !IsPerformanceStale(performance, now) &&
            performance.ResolvedPositions >= signalOptions.MinLeaderCategoryResolvedPositions &&
            SampleQualityRank(performance.SampleQuality) >= SampleQualityRank(signalOptions.MinLeaderCategorySampleQuality) &&
            performance.Score >= signalOptions.MinLeaderCategoryScore &&
            performance.ResolvedRoiPct >= signalOptions.MinLeaderCategoryResolvedRoiPct &&
            performance.WinRatePct >= signalOptions.MinLeaderCategoryWinRatePct;
    }

    private bool HasUsableCopiedTraderPerformance(
        PaperCopiedTraderPerformance? performance,
        string wallet,
        string? category)
    {
        return HasMinimumCopiedTraderSample(performance, wallet, category) &&
            !IsWeakCopiedTraderPerformance(performance, wallet, category);
    }

    private bool IsWeakCopiedTraderPerformance(
        PaperCopiedTraderPerformance? performance,
        string wallet,
        string? category)
    {
        if (!HasMinimumCopiedTraderSample(performance, wallet, category))
        {
            return false;
        }

        var observedPerformance = performance!;
        return observedPerformance.TotalPnlUsd <= signalOptions.CopiedTraderPerformanceMinTotalPnlUsd ||
            observedPerformance.RoiPct <= signalOptions.CopiedTraderPerformanceMinRoiPct ||
            observedPerformance.Score < signalOptions.CopiedTraderPerformanceMinScore;
    }

    private bool HasMinimumCopiedTraderSample(
        PaperCopiedTraderPerformance? performance,
        string wallet,
        string? category)
    {
        return performance is not null &&
            !IsUnknownCategory(category) &&
            string.Equals(performance.CopiedTraderWallet, wallet, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(performance.Category, category, StringComparison.OrdinalIgnoreCase) &&
            performance.SettledPositionsCount >= signalOptions.CopiedTraderPerformanceMinSettledPositions;
    }

    private bool IsPerformanceStale(PolymarketOnChainWalletCategoryPerformance performance, DateTimeOffset now)
    {
        return now - performance.RefreshedAtUtc > TimeSpan.FromHours(signalOptions.LeaderCategoryPerformanceStaleAfterHours);
    }

    private decimal ProposedSize(int score, decimal proposedPrice, decimal marketMinOrderSize)
    {
        if (paperTradingOptions.UseMinimumMarketOrderSize)
        {
            return marketMinOrderSize;
        }

        var maxTradeNotional = paperTradingOptions.InitialBankrollUsd * riskOptions.MaxTradeBankrollPct / 100m;
        var proposedNotional = score >= signalOptions.NormalPaperOrderScore
            ? maxTradeNotional
            : maxTradeNotional / 2m;
        return proposedNotional / proposedPrice;
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

    private static bool IsUnknownCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ||
            string.Equals(category, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static int SampleQualityRank(string? sampleQuality)
    {
        if (string.Equals(sampleQuality, "High", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(sampleQuality, "Medium", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(sampleQuality, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static SignalDecision Reject(string reasonCode, DateTimeOffset now, int score = 0)
    {
        return new SignalDecision(false, score, reasonCode, [reasonCode], null, null, null, now);
    }

    private readonly record struct PriceDecision(
        bool Accepted,
        decimal Price,
        SignalDecision? Decision)
    {
        public static PriceDecision Accept(decimal price)
        {
            return new PriceDecision(true, price, null);
        }

        public static PriceDecision Reject(string reasonCode, DateTimeOffset now)
        {
            return new PriceDecision(false, 0m, DefaultSignalEngine.Reject(reasonCode, now));
        }
    }
}
