using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mPaperStrategyProcessorTests
{
    private static readonly BtcUpDown5mStrategyVariant Less60Variant =
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 60);

    private static readonly BtcUpDown5mStrategyVariant More60Variant =
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 60);

    private static readonly BtcUpDown5mStrategyVariant More270Variant =
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 270);

    private static readonly BtcUpDown5mStrategyVariant More90Below70Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below70Code);

    private static readonly BtcUpDown5mStrategyVariant More90Below65Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below65Code);

    private static readonly BtcUpDown5mStrategyVariant More90Below60Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below60Code);

    private static readonly BtcUpDown5mStrategyVariant More90Below55Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below55Code);

    private static readonly BtcUpDown5mStrategyVariant More60Below60Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore60Below60Code);

    private static readonly BtcUpDown5mStrategyVariant More60Below55Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore60Below55Code);

    private static readonly BtcUpDown5mStrategyVariant More30Below55Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore30Below55Code);

    private static readonly BtcUpDown5mStrategyVariant More120Below70Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore120Below70Code);

    private static readonly BtcUpDown5mStrategyVariant More150Below65Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore150Below65Code);

    private static readonly BtcUpDown5mStrategyVariant More270Below65Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore270Below65Code);

    private static readonly BtcUpDown5mStrategyVariant More270Below60Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore270Below60Code);

    private static readonly BtcUpDown5mStrategyVariant Less120Below20Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mLess120Below20Code);

    private static readonly BtcUpDown5mStrategyVariant Less120Below30Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mLess120Below30Code);

    private static readonly BtcUpDown5mStrategyVariant Less90Below20Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mLess90Below20Code);

    private static readonly BtcUpDown5mStrategyVariant Less60Below20Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mLess60Below20Code);

    private static readonly BtcUpDown5mStrategyVariant More60GammaBelow70Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore60GammaBelow70Code);

    private static readonly BtcUpDown5mStrategyVariant More120GammaBelow65Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore120GammaBelow65Code);

    private static readonly BtcUpDown5mStrategyVariant More150GammaBelow80Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore150GammaBelow80Code);

    private static readonly BtcUpDown5mStrategyVariant Less60GammaVariant =
        StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.Less,
            60,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);

    private static readonly BtcUpDown5mStrategyVariant More60GammaVariant =
        StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.More,
            60,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);

    private static readonly BtcUpDown5mStrategyVariant Less180Variant =
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 180);

    private static readonly BtcUpDown5mStrategyVariant Less180MartinVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Less180Martin);

    private static readonly BtcUpDown5mStrategyVariant Middle1Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_1");

    private static readonly BtcUpDown5mStrategyVariant Middle2Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_2");

    private static readonly BtcUpDown5mStrategyVariant Middle3Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_3");

    private static readonly BtcUpDown5mStrategyVariant Middle1RevertVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_1_revert");

    private static readonly BtcUpDown5mStrategyVariant Skip3Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_3");

    private static readonly BtcUpDown5mStrategyVariant Skip1Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_1");

    private static readonly BtcUpDown5mStrategyVariant Skip3RevertVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_3_revert");

    private static readonly BtcUpDown5mStrategyVariant AlwaysUpVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up");

    private static readonly BtcUpDown5mStrategyVariant AlwaysDownVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_down");

    private static readonly BtcUpDown5mStrategyVariant BinanceVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance");

    private static readonly BtcUpDown5mStrategyVariant Binance45Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_45");

    private static readonly BtcUpDown5mStrategyVariant Binance47Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_47");

    private static readonly BtcUpDown5mStrategyVariant Binance49Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_49");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps01Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_0_1");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps05Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_0_5");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps09Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_0_9");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps1Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_1");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps11Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_1_1");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps2Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_2");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps3Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_3");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps49Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_4_9");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps5Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_5");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps1InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_1_instant");

    private static readonly BtcUpDown5mStrategyVariant BinanceCleverVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_clever");

    private static readonly BtcUpDown5mStrategyVariant BinanceCleverAggressiveVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_clever_aggressive");

    private static readonly BtcUpDown5mStrategyVariant BinanceCleverConservativeVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_clever_conservative");

    private static readonly BtcUpDown5mStrategyVariant BinanceEdge2Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mBinanceEdge2Code);

    private static readonly BtcUpDown5mStrategyVariant BinanceDelayed30Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mBinanceDelayed30Code);

    private static readonly BtcUpDown5mStrategyVariant EnsembleVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mEnsemble2Of3Code);

    private static readonly BtcUpDown5mStrategyVariant DynamicMarkovVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mDynamicMarkovCode);

    private static readonly BtcUpDown5mStrategyVariant StrategySelectorVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mStrategySelectorCode);

    private static readonly BtcUpDown5mStrategyVariant PreviousScoreCounterTrend35Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_prev_score_countertrend_35");

    [Fact]
    public void StrategyIds_IncludeStandardMartinAndGammaBtcVariants()
    {
        Assert.Equal(1264, StrategyIds.BtcUpDown5mVariants.Count);
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Code).Distinct().Count());
        Assert.Equal(18, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Standard));
        Assert.Equal(15, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StandardEntryPriceCap));
        Assert.Equal(18, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection));
        Assert.Equal(7, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Less180Martin);
        Assert.Equal(50, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference));
        Assert.Equal(50, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert));
        Assert.Equal(5, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults));
        Assert.Equal(5, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysUp);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysDown);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelative);
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice));
        Assert.Equal(50, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold));
        Assert.Equal(50, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever);
        Assert.Equal(2, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin));
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge));
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.EnsembleVote);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DynamicMarkov);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StrategySelector);
        Assert.Equal(17, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend));
        Assert.Equal(640, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(320, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell &&
            variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.HalfPeriod));
        Assert.Equal(320, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell &&
            variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.FullPeriod));
        Assert.Equal(160, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(160, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(160, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.OneHour &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(160, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FourHours &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.OneHour &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FourHours &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal("BTC Up or Down 5m Less 180 Gamma", StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.Less,
            180,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection).Name);
        Assert.Equal("BTC Up or Down 5m Middle 5", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5").Name);
        Assert.Equal("BTC Up or Down 5m Middle 5 Revert", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5_revert").Name);
        Assert.Equal("BTC Up or Down 5m Middle 5 0.9 bps", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5_bps_0_9").Name);
        Assert.Equal(0.9m, StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5_bps_0_9").DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Middle 5 Revert 0.9 bps", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5_revert_bps_0_9").Name);
        Assert.Equal(0.9m, StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_5_revert_bps_0_9").DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Skip 5", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_skip_5").Name);
        Assert.Equal("BTC Up or Down 5m Skip 5 Revert", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_skip_5_revert").Name);
        Assert.Equal("BTC Up or Down 5m Up", AlwaysUpVariant.Name);
        Assert.Equal("BTC Up or Down 5m Down", AlwaysDownVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance", BinanceVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 45", Binance45Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 47", Binance47Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 49", Binance49Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 0.1 bps", BinanceBps01Variant.Name);
        Assert.Equal(0.1m, BinanceBps01Variant.DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Binance 0.5 bps", BinanceBps05Variant.Name);
        Assert.Equal(0.5m, BinanceBps05Variant.DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Binance 0.9 bps", BinanceBps09Variant.Name);
        Assert.Equal(0.9m, BinanceBps09Variant.DecisionThresholdBps);
        Assert.Equal(
            Enumerable.Range(1, 50).Select(thresholdTenths => thresholdTenths / 10m).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold)
                .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.Equal(
            Enumerable.Range(1, 50).Select(thresholdTenths => thresholdTenths / 10m).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant)
                .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.Equal("BTC Up or Down 5m Binance 1 bps", BinanceBps1Variant.Name);
        Assert.Equal(1, BinanceBps1Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 1 bps Instant", BinanceBps1InstantVariant.Name);
        Assert.Equal(1m, BinanceBps1InstantVariant.DecisionThresholdBps);
        Assert.Equal(1, BinanceBps1InstantVariant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 1.1 bps", BinanceBps11Variant.Name);
        Assert.Equal(1.1m, BinanceBps11Variant.DecisionThresholdBps);
        Assert.Equal(0, BinanceBps11Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 2 bps", BinanceBps2Variant.Name);
        Assert.Equal(2, BinanceBps2Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 3 bps", BinanceBps3Variant.Name);
        Assert.Equal(3m, BinanceBps3Variant.DecisionThresholdBps);
        Assert.Equal(3, BinanceBps3Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 4.9 bps", BinanceBps49Variant.Name);
        Assert.Equal(4.9m, BinanceBps49Variant.DecisionThresholdBps);
        Assert.Equal(0, BinanceBps49Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 5 bps", BinanceBps5Variant.Name);
        Assert.Equal(5, BinanceBps5Variant.DecisionDepth);
        var preOpen15mHalfUp49 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_15m_preopen_half_up_49");
        Assert.Equal("BTC Up or Down 15m PreOpen Half Up 49", preOpen15mHalfUp49.Name);
        Assert.Equal(-300, preOpen15mHalfUp49.EntryDelaySeconds);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, preOpen15mHalfUp49.MarketInterval);
        Assert.Equal(BtcUpDownPreOpenLifetimeMode.HalfPeriod, preOpen15mHalfUp49.PreOpenLifetimeMode);
        Assert.Equal(BtcUpDownFixedOutcome.Up, preOpen15mHalfUp49.FixedOutcome);
        Assert.Equal(0.49m, preOpen15mHalfUp49.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 15m PreOpen Half", preOpen15mHalfUp49.Category);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_15m_preopen_half_up_49_sell");
        var preOpen15mFullUp49Sell = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        Assert.Equal("BTC Up or Down 15m PreOpen Full Up 49 Sell", preOpen15mFullUp49Sell.Name);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell, preOpen15mFullUp49Sell.Behavior);
        Assert.Equal(-300, preOpen15mFullUp49Sell.EntryDelaySeconds);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, preOpen15mFullUp49Sell.MarketInterval);
        Assert.Equal(BtcUpDownPreOpenLifetimeMode.FullPeriod, preOpen15mFullUp49Sell.PreOpenLifetimeMode);
        Assert.Equal(BtcUpDownFixedOutcome.Up, preOpen15mFullUp49Sell.FixedOutcome);
        Assert.Equal(0.49m, preOpen15mFullUp49Sell.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 15m PreOpen Full Sell", preOpen15mFullUp49Sell.Category);
        Assert.Contains(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_4h_preopen_full_down_30" &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Down &&
            variant.FixedLimitPrice == 0.30m);
        Assert.Contains(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_4h_preopen_full_down_10" &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Down &&
            variant.FixedLimitPrice == 0.10m);
        Assert.Equal("BTC Up or Down 5m Binance Clever", BinanceCleverVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance Clever Aggressive", BinanceCleverAggressiveVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance Clever Conservative", BinanceCleverConservativeVariant.Name);
        Assert.Equal("BTC Up or Down 5m More 90 Below 70", More90Below70Variant.Name);
        Assert.Equal(70, More90Below70Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 90 Below 65", More90Below65Variant.Name);
        Assert.Equal(65, More90Below65Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 90 Below 60", More90Below60Variant.Name);
        Assert.Equal(60, More90Below60Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 90 Below 55", More90Below55Variant.Name);
        Assert.Equal(55, More90Below55Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 60 Below 60", More60Below60Variant.Name);
        Assert.Equal(60, More60Below60Variant.EntryDelaySeconds);
        Assert.Equal(60, More60Below60Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 60 Below 55", More60Below55Variant.Name);
        Assert.Equal(60, More60Below55Variant.EntryDelaySeconds);
        Assert.Equal(55, More60Below55Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 30 Below 55", More30Below55Variant.Name);
        Assert.Equal(30, More30Below55Variant.EntryDelaySeconds);
        Assert.Equal(55, More30Below55Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 120 Below 70", More120Below70Variant.Name);
        Assert.Equal(120, More120Below70Variant.EntryDelaySeconds);
        Assert.Equal(70, More120Below70Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 150 Below 65", More150Below65Variant.Name);
        Assert.Equal(150, More150Below65Variant.EntryDelaySeconds);
        Assert.Equal(65, More150Below65Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 270 Below 65", More270Below65Variant.Name);
        Assert.Equal(270, More270Below65Variant.EntryDelaySeconds);
        Assert.Equal(65, More270Below65Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 270 Below 60", More270Below60Variant.Name);
        Assert.Equal(270, More270Below60Variant.EntryDelaySeconds);
        Assert.Equal(60, More270Below60Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Less 120 Below 20", Less120Below20Variant.Name);
        Assert.Equal(BtcUpDown5mStrategyDirection.Less, Less120Below20Variant.Direction);
        Assert.Equal(120, Less120Below20Variant.EntryDelaySeconds);
        Assert.Equal(20, Less120Below20Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Less 120 Below 30", Less120Below30Variant.Name);
        Assert.Equal(30, Less120Below30Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Less 90 Below 20", Less90Below20Variant.Name);
        Assert.Equal(90, Less90Below20Variant.EntryDelaySeconds);
        Assert.Equal("BTC Up or Down 5m Less 60 Below 20", Less60Below20Variant.Name);
        Assert.Equal(60, Less60Below20Variant.EntryDelaySeconds);
        Assert.Equal("BTC Up or Down 5m More 60 Gamma Below 70", More60GammaBelow70Variant.Name);
        Assert.Equal(60, More60GammaBelow70Variant.EntryDelaySeconds);
        Assert.Equal(70, More60GammaBelow70Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 120 Gamma Below 65", More120GammaBelow65Variant.Name);
        Assert.Equal(120, More120GammaBelow65Variant.EntryDelaySeconds);
        Assert.Equal(65, More120GammaBelow65Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m More 150 Gamma Below 80", More150GammaBelow80Variant.Name);
        Assert.Equal(150, More150GammaBelow80Variant.EntryDelaySeconds);
        Assert.Equal(80, More150GammaBelow80Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance Edge 2", BinanceEdge2Variant.Name);
        Assert.Equal(2, BinanceEdge2Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 30s", BinanceDelayed30Variant.Name);
        Assert.Equal(30, BinanceDelayed30Variant.EntryDelaySeconds);
        Assert.Equal("BTC Up or Down 5m Ensemble 2 of 3", EnsembleVariant.Name);
        Assert.Equal("BTC Up or Down 5m Dynamic Markov", DynamicMarkovVariant.Name);
        Assert.Equal("BTC Up or Down 5m Strategy Selector", StrategySelectorVariant.Name);
        Assert.Equal("BTC Up or Down 5m Prev Score Countertrend 35", PreviousScoreCounterTrend35Variant.Name);
        Assert.Equal(0.35m, PreviousScoreCounterTrend35Variant.FixedLimitPrice);
        Assert.Equal(35, PreviousScoreCounterTrend35Variant.DecisionDepth);
        Assert.Equal("BTC Up/Down 5m Previous Score Countertrend", PreviousScoreCounterTrend35Variant.Category);
        Assert.Equal(
            Enumerable.Range(0, 17).Select(index => 0.10m + (index * 0.05m)).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend)
                .Select(variant => variant.FixedLimitPrice.GetValueOrDefault())
                .OrderBy(price => price)
                .ToArray());
    }

    [Fact]
    public async Task ProcessAsync_LessVariantObservesDueMarketAndBuysLowerPricedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessor(repository, [], Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Less60Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.35m, run.EntryPrice);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Less60Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(Less60Variant.CopiedTraderWallet, order.CopiedTraderWallet);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.35m, order.Price);
        Assert.Equal(2.8571428571m, order.SizeShares, 10);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);

        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotCreateNewRunsForDisabledVariant()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategyEnabledStates[Less60Variant.Id] = false;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessor(repository, [], Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotObserveFarFutureBtc5mMarket()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddHours(2),
            now.AddHours(2).AddMinutes(5),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessor(repository, [], Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_MoreVariantObservesDueMarketAndBuysHigherPricedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessor(repository, [], More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(More60Variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.65m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More60Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(More60Variant.CopiedTraderWallet, order.CopiedTraderWallet);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.65m, order.Price);
    }

    [Fact]
    public async Task ProcessAsync_UsesGammaOutcomePriceWhenOrderBookSnapshotIsUnavailable()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessorWithoutOrderBooks(repository, [], Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal(0.35m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.35m, order.Price);
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task ProcessAsync_MoreVariantUsesGammaOutcomePriceNotLowerBestAsk()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", bestBid: 0.94m, bestAsk: 0.95m, now),
                OrderBook("asset-down", bestBid: 0.05m, bestAsk: 0.06m, now)
            ],
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal(0.65m, run.EntryPrice);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.65m, order.Price);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingUsesOrderBookDepthVwap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.36m, 1m), new OrderBookLevel(0.37m, 2m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.3663366337m, run.EntryPrice.GetValueOrDefault(), 10);
        Assert.Equal(2.7297297297m, run.SizeShares.GetValueOrDefault(), 10);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.3663366337m, order.Price, 10);
        Assert.Equal(2.7297297297m, order.SizeShares, 10);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"pricing_mode\":\"paper_gtd_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"pre_gtd_pricing_mode\":\"paper_taker_vwap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"websocket_cache\"", order.RawDecisionJson, StringComparison.Ordinal);

        Assert.Empty(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperRestFallbackClampsFutureSnapshotAgeAndRecordsCacheDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;
        var futureSnapshotAt = now.AddSeconds(10);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.36m, 100m)],
                    futureSnapshotAt),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    futureSnapshotAt)
            ],
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"rest_attempted\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"cache_status\":\"Missing\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"quote_age_ms\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"quote_age_ms\":-", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperRestFallbackUsesLocalReceiveTimeForStaleExchangeTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var staleSnapshotAt = now.AddSeconds(-30);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.36m, 100m)],
                    staleSnapshotAt),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    staleSnapshotAt)
            ],
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"rest_attempted\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("missing_orderbook_cache_stale", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperSelectionUsesExecutableClobPricesForLess()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.07m, 100m)],
                    [new OrderBookLevel(0.08m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.08m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.08m, order.Price);
        Assert.Contains("\"outcome_selection_source\":\"clob_executable_vwap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"asset_id\":\"asset-up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"asset_id\":\"asset-down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperSelectionUsesExecutableClobPricesForMore()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.07m, 100m)],
                    [new OrderBookLevel(0.08m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.92m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.92m, order.Price);
        Assert.Contains("\"outcome_selection_source\":\"clob_executable_vwap\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_More90Below70EntersWhenExecutablePriceIsBelowCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-90),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.68m, 100m)],
                [new OrderBookLevel(0.69m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.31m, 100m)],
                [new OrderBookLevel(0.32m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            More90Below70Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(More90Below70Variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.70m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More90Below70Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.70m, order.Price);
        Assert.True(order.ExpiresAtUtc <= now.AddSeconds(121));
        Assert.Empty(repository.PaperFills);
        Assert.Contains("\"strategy_entry_price_cap\":0.7", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome_selection_source\":\"clob_executable_vwap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"strategy_entry_price_cap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_More60Below60EntersWhenExecutablePriceIsBelowCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.42m,
            downPrice: 0.58m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.57m, 100m)],
                [new OrderBookLevel(0.58m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.42m, 100m)],
                [new OrderBookLevel(0.43m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            More60Below60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(More60Below60Variant.Id, run.StrategyId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.60m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More60Below60Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.60m, order.Price);
        Assert.Empty(repository.PaperFills);
        Assert.Contains("\"strategy_entry_price_cap\":0.6", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_More270UsesMarketEndCapWhenEntryIsAfterMarketMidpoint()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-270);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.58m,
            downPrice: 0.42m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.57m, 100m)],
                [new OrderBookLevel(0.58m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.42m, 100m)],
                [new OrderBookLevel(0.43m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            More270Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(More270Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.58m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More270Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.58m, order.Price);
        Assert.InRange((order.ExpiresAtUtc - marketEndUtc).TotalMilliseconds, -100d, 100d);
        Assert.DoesNotContain("opening_limit_market_relative_expiration_elapsed", run.SkipReason ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("\"gtd_expiration_mode\":\"market_end_cap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"market_end_expire_before_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"converted_to_gtd_limit_order\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_Less120Below20EntersWhenExecutablePriceIsBelowCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-120),
            now.AddMinutes(3),
            upPrice: 0.15m,
            downPrice: 0.85m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.17m, 100m)],
                [new OrderBookLevel(0.18m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.81m, 100m)],
                [new OrderBookLevel(0.82m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            Less120Below20Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Less120Below20Variant.Id, run.StrategyId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.20m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Less120Below20Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.20m, order.Price);
        Assert.Empty(repository.PaperFills);
        Assert.Contains("\"strategy_entry_price_cap\":0.2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome_selection_source\":\"clob_executable_vwap\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_More90Below70PlacesGtdWhenExecutablePriceIsAtOrAboveCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-90),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.74m, 100m)],
                [new OrderBookLevel(0.75m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.25m, 100m)],
                [new OrderBookLevel(0.26m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            More90Below70Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(More90Below70Variant.Id, run.StrategyId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.70m, run.EntryPrice);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.70m, order.Price);
        Assert.Contains("\"estimated_fill_price\":0.75", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_price\":0.7", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_GammaBelowVariantUsesGammaSelectionAndPlacesGtdAtCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.07m, 100m)],
                    [new OrderBookLevel(0.08m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            More60GammaBelow70Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(More60GammaBelow70Variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.70m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More60GammaBelow70Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.70m, order.Price);
        Assert.Empty(repository.PaperFills);
        Assert.Contains("\"decision_source\":\"gamma_outcome_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome_selection_source\":\"gamma_outcome_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"gamma_outcome_price\":0.65", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"strategy_entry_price_cap\":0.7", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"strategy_entry_price_cap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_GammaVariantUsesGammaSelectionBeforeTakerPricingForLess()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.07m, 100m)],
                    [new OrderBookLevel(0.08m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            Less60GammaVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.92m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.92m, order.Price);
        Assert.Contains("\"outcome_selection_source\":\"gamma_outcome_price\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_GammaVariantUsesGammaSelectionBeforeTakerPricingForMore()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.07m, 100m)],
                    [new OrderBookLevel(0.08m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            More60GammaVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.08m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.08m, order.Price);
        Assert.Contains("\"outcome_selection_source\":\"gamma_outcome_price\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingFallsBackToClobBookWhenCacheMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.36m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now)
            ],
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingTrustsClobBookWhenGammaDiffIsLarge()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.09m, 100m)],
                    [new OrderBookLevel(0.10m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.89m, 100m)],
                    [new OrderBookLevel(0.90m, 100m)],
                    now)
            ],
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.90m, run.EntryPrice);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.90m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"clob_vs_gamma_diff\":0.25", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingUsesClobBookWhenWebSocketCacheHasBadExecutionPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.28m, 100m)],
                    [new OrderBookLevel(0.29m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.29m, 100m)],
                    [new OrderBookLevel(0.30m, 100m)],
                    now)
            ],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.27m, 100m)],
                    [new OrderBookLevel(0.28m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.71m, 100m)],
                    [new OrderBookLevel(0.72m, 100m)],
                    now)
            ],
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.72m, run.EntryPrice);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.72m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingSkipsLessWhenExecutablePriceIsAboveHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.91m, 100m)],
                    [new OrderBookLevel(0.92m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.90m, 100m)],
                    [new OrderBookLevel(0.91m, 100m)],
                    now)
            ],
            Less180Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal(SignalReasonCodes.ExecutionPriceDirectionMismatch, run.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingSkipsMoreWhenExecutablePriceIsBelowHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [],
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.29m, 100m)],
                    [new OrderBookLevel(0.30m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.28m, 100m)],
                    [new OrderBookLevel(0.29m, 100m)],
                    now)
            ],
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal(SignalReasonCodes.ExecutionPriceDirectionMismatch, run.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingPlacesRestingLimitWhenAskSideEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks: [],
            clobOrderBooks:
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now)
            ],
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Null(run.SkipReason);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);
        Assert.Equal(2.5m, run.SizeShares);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.40m, order.Price);
        Assert.Equal(2.5m, order.SizeShares);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"pricing_mode\":\"paper_gtd_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"pre_gtd_pricing_mode\":\"paper_taker_resting_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"resting_limit_no_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"resting_limit_due_to_empty_ask_side\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"empty_side_reason\":\"missing_orderbook_empty_side\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"rest_attempted\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"cache_status\":\"Missing\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"has_executable_ask_depth\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"asks\":[]", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_GammaSelectionPlacesRestingLimitWhenSelectedAskSideEmpty()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks: [],
            clobOrderBooks:
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now)
            ],
            Less60GammaVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(Less60GammaVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(Less60GammaVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.40m, order.Price);
        Assert.Contains("\"outcome_selection_source\":\"gamma_outcome_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"pre_gtd_pricing_mode\":\"paper_taker_resting_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"resting_limit_no_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"resting_limit_due_to_empty_ask_side\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"empty_side_reason\":\"missing_orderbook_empty_side\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingAllowsBestAskAboveReferenceCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.41m, 100m)],
                    now),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now)
            ],
            clobOrderBook: null,
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.41m, run.EntryPrice);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.41m, order.Price);
        Assert.Contains("\"max_allowed_price\":0.41", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingPlacesGtdOrderAtSelectedExecutablePrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.34m, 100m)],
                [new OrderBookLevel(0.36m, 1m), new OrderBookLevel(0.41m, 100m)],
                now),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.64m, 100m)],
                [new OrderBookLevel(0.66m, 100m)],
                now)
        };
        var processor = CreateTakerProcessorCore(
            repository,
            orderBooks,
            orderBooks,
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.36m, run.EntryPrice);
        Assert.Equal(2.7777777778m, run.SizeShares.GetValueOrDefault(), 10);
        Assert.Equal(1m, run.StakeUsd);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.36m, order.Price);
        Assert.Equal(2.7777777778m, order.SizeShares, 10);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_TakerPaperPricingUsesMinimumStakeMultiplierWithSafetyBuffer()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateTakerProcessorCore(
            repository,
            [
                OrderBook(
                    "asset-up",
                    [new OrderBookLevel(0.34m, 100m)],
                    [new OrderBookLevel(0.36m, 100m)],
                    now,
                    minOrderSize: 5m),
                OrderBook(
                    "asset-down",
                    [new OrderBookLevel(0.64m, 100m)],
                    [new OrderBookLevel(0.66m, 100m)],
                    now,
                    minOrderSize: 5m)
            ],
            clobOrderBook: null,
            Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.36m, order.Price);
        Assert.Equal(5.56m, order.SizeShares);
        Assert.Equal(2.0016m, order.NotionalUsd);
        Assert.Contains("\"stake_multiplier\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"minimum_stake_safety_multiplier\":1.10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"minimum_notional_usd\":1.80", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"raw_target_notional_usd\":1.9800", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_notional_rounding\":\"ceil_usd\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"target_notional_usd\":2.0016", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"target_size_shares\":5.56", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipsMoreEntryWhenOutcomePriceIsNotAboveHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.49m,
            downPrice: 0.48m));
        var processor = CreateProcessorWithoutOrderBooks(repository, [], More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal(SignalReasonCodes.OutcomePriceDirectionMismatch, run.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Theory]
    [InlineData("btc-updown-15m-1777983300", "btc-up-or-down-15m")]
    [InlineData("btc-updown-4h-1777982400", "btc-up-or-down-4h")]
    [InlineData("bitcoin-up-or-down-may-5-2026-8am-et", "btc-up-or-down-hourly")]
    public async Task ProcessAsync_IgnoresNonFiveMinuteUpDownMarkets(string slug, string seriesSlug)
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddMinutes(-2),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m,
            slug: slug,
            seriesSlug: seriesSlug,
            question: "Bitcoin Up or Down - test"));
        var processor = CreateProcessor(repository, [], Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenHalfPeriodAlwaysUpPlacesFifteenMinuteGtdLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(5);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_half_up_49");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", [new OrderBookLevel(0.48m, 100m)], [new OrderBookLevel(0.52m, 100m)], now, 5m),
                OrderBook("asset-down", [new OrderBookLevel(0.48m, 100m)], [new OrderBookLevel(0.52m, 100m)], now, 5m)
            ],
            variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal(marketStart.AddMinutes(-5), run.EntryDueAtUtc);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.49m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.49m, order.Price);
        Assert.Equal(marketStart.AddMinutes(7.5), order.ExpiresAtUtc);
        Assert.Contains("\"decision_source\":\"fixed_up_preopen\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"gtd_expiration_mode\":\"preopen_half_period\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fixed\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.49", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFixedDirectionCreatesOrderWithoutSelectedBookLiquidity()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(5);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_half_up_49");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [OrderBook("asset-up", [], [], now, 5m)],
            variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        Assert.Empty(repository.PaperFills);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.49m, order.Price);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_sizing_source\":\"websocket_cache\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFixedDirectionAllowsLateEntryBeforeMarketStart()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(4);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_half_up_49");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [OrderBook("asset-up", [], [], now, 5m)],
            variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(marketStart.AddMinutes(-5), run.EntryDueAtUtc);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Null(run.SkipReason);
        Assert.Single(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFullSellPlacesSellWhenLastQuarterDirectionOpposes()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(-12);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        var repository = new TestAppRepository();
        var market = CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m);
        repository.PolymarketGammaMarkets.Add(market);
        var signalId = Guid.NewGuid();
        var buyOrderId = Guid.NewGuid();
        repository.PaperOrders.Add(new PaperOrder(
            buyOrderId,
            signalId,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.49m,
            10m,
            4.9m,
            marketStart.AddMinutes(-5),
            marketEnd,
            FilledAtUtc: marketStart.AddMinutes(-4),
            StrategyId: variant.Id));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            buyOrderId,
            0.49m,
            10m,
            marketStart.AddMinutes(-4),
            "entry"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            10m,
            0.49m,
            3m,
            -1.9m,
            now,
            variant.CopiedTraderWallet));
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            market.Category,
            marketStart,
            marketEnd,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-up",
            "Up",
            0.49m,
            4.9m,
            10m,
            signalId,
            buyOrderId,
            marketStart.AddMinutes(-4),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-4)));
        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", [new OrderBookLevel(0.30m, 10m)], [new OrderBookLevel(0.32m, 100m)], now, 5m),
                OrderBook("asset-down", [new OrderBookLevel(0.68m, 100m)], [new OrderBookLevel(0.70m, 100m)], now, 5m)
            ],
            variant.Code);

        await processor.ProcessAsync();

        var sellOrder = Assert.Single(repository.PaperOrders, order => order.Side == TradeSide.Sell);
        Assert.Equal(PaperOrderStatus.Pending, sellOrder.Status);
        Assert.Equal("asset-up", sellOrder.AssetId);
        Assert.Equal("Up", sellOrder.Outcome);
        Assert.Equal(0.30m, sellOrder.Price);
        Assert.Equal(10m, sellOrder.SizeShares);
        Assert.Equal(variant.Id, sellOrder.StrategyId);
        Assert.Equal("btc_preopen_sell_exit", sellOrder.ExecutionSource);
        Assert.Contains("\"selected_direction\":\"Up\"", sellOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"current_direction\":\"Down\"", sellOrder.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFullSellDoesNotSellWhenLastQuarterDirectionMatches()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(-12);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        var repository = new TestAppRepository();
        var market = CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m);
        repository.PolymarketGammaMarkets.Add(market);
        var signalId = Guid.NewGuid();
        var buyOrderId = Guid.NewGuid();
        repository.PaperOrders.Add(new PaperOrder(
            buyOrderId,
            signalId,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.49m,
            10m,
            4.9m,
            marketStart.AddMinutes(-5),
            marketEnd,
            FilledAtUtc: marketStart.AddMinutes(-4),
            StrategyId: variant.Id));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            buyOrderId,
            0.49m,
            10m,
            marketStart.AddMinutes(-4),
            "entry"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            10m,
            0.49m,
            7m,
            2.1m,
            now,
            variant.CopiedTraderWallet));
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            market.Category,
            marketStart,
            marketEnd,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-up",
            "Up",
            0.49m,
            4.9m,
            10m,
            signalId,
            buyOrderId,
            marketStart.AddMinutes(-4),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-4)));
        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", [new OrderBookLevel(0.68m, 100m)], [new OrderBookLevel(0.70m, 100m)], now, 5m),
                OrderBook("asset-down", [new OrderBookLevel(0.30m, 10m)], [new OrderBookLevel(0.32m, 100m)], now, 5m)
            ],
            variant.Code);

        await processor.ProcessAsync();

        Assert.DoesNotContain(repository.PaperOrders, order => order.Side == TradeSide.Sell);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenDueEntriesUseCompleteEarliestDueGroupAndSharedBookFetch()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(4);
        var marketEnd = marketStart.AddMinutes(15);
        var variants = new[]
        {
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_15m_preopen_half_up_49"),
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_15m_preopen_half_up_48"),
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_15m_preopen_half_up_47")
        };
        var repository = new TestAppRepository();
        var market = CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m);
        repository.PolymarketGammaMarkets.Add(market);
        repository.StrategyMarketPaperRuns.AddRange(variants.Select(variant =>
            CreateObservedRun(variant, market, marketStart, now.AddMinutes(-2))));
        var clobClient = new FakeClobClient([]);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes: variants.Select(variant => variant.Code).ToArray(),
                maxEntriesPerCycle: 1,
                maxConcurrentEntryDecisions: 4),
            clobClient: clobClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(3, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(3, repository.PaperOrders.Count);
        Assert.All(repository.StrategyMarketPaperRuns, run => Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status));
        Assert.Equal(1, clobClient.GetOrderBookCalls);
    }

    [Fact]
    public async Task ProcessAsync_CurrentMarketEntriesRunBeforeSameDueFuturePreOpenEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var currentMarketStart = now.AddSeconds(-2);
        var currentMarketEnd = currentMarketStart.AddMinutes(5);
        var futureMarketStart = currentMarketStart.AddMinutes(5);
        var futureMarketEnd = futureMarketStart.AddMinutes(5);
        var preOpenVariants = new[]
        {
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_preopen_half_up_49"),
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_preopen_half_up_48"),
            StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_preopen_half_up_47")
        };
        var repository = new TestAppRepository();
        var currentMarket = CreateMarket(
            currentMarketStart,
            currentMarketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "current-market",
            conditionId: "current-condition",
            upAssetId: "current-up",
            downAssetId: "current-down",
            orderMinSize: 5m);
        var futureMarket = CreateMarket(
            futureMarketStart,
            futureMarketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "future-market",
            conditionId: "future-condition",
            upAssetId: "future-up",
            downAssetId: "future-down",
            orderMinSize: 5m);
        repository.PolymarketGammaMarkets.Add(currentMarket);
        repository.PolymarketGammaMarkets.Add(futureMarket);
        repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
            AlwaysUpVariant,
            currentMarket,
            currentMarketStart,
            currentMarketStart.AddMinutes(-1)));
        repository.StrategyMarketPaperRuns.AddRange(preOpenVariants.Select(variant =>
            CreateObservedRun(variant, futureMarket, futureMarketStart, currentMarketStart.AddMinutes(-1))));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [
                OrderBook("current-up", [], [], now, 5m),
                OrderBook("future-up", [], [], now, 5m)
            ],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes: preOpenVariants.Select(variant => variant.Code).Append(AlwaysUpVariant.Code).ToArray(),
                maxEntriesPerCycle: 1,
                maxConcurrentEntryDecisions: 1,
                maxMarketsPerCycle: 0));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(4, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(4, repository.PaperOrders.Count);
        Assert.Equal(AlwaysUpVariant.Id, repository.PaperOrders[0].StrategyId);
        Assert.All(repository.StrategyMarketPaperRuns, run => Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status));
        foreach (var variant in preOpenVariants)
        {
            Assert.Contains(repository.PaperOrders, order => order.StrategyId == variant.Id);
        }
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFullPeriodAlwaysDownPlacesFourHourGtdLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(5);
        var marketEnd = marketStart.AddHours(4);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_4h_preopen_full_down_30");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-4h-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-4h",
            orderMinSize: 5m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", [new OrderBookLevel(0.49m, 100m)], [new OrderBookLevel(0.51m, 100m)], now, 5m),
                OrderBook("asset-down", [new OrderBookLevel(0.29m, 100m)], [new OrderBookLevel(0.31m, 100m)], now, 5m)
            ],
            variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.30m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.30m, order.Price);
        Assert.Equal(marketEnd.AddMinutes(-1), order.ExpiresAtUtc);
        Assert.Contains("\"decision_source\":\"fixed_down_preopen\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"gtd_expiration_mode\":\"preopen_full_period\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.3", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreOpenFullSellPlacesFifteenMinuteGtdLimitWithoutPrecloseCancel()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddMinutes(5);
        var marketEnd = marketStart.AddMinutes(15);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketEnd,
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "btc-updown-15m-" + marketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            seriesSlug: "btc-up-or-down-15m",
            orderMinSize: 5m));
        var processor = CreateProcessorCore(
            repository,
            [],
            [OrderBook("asset-up", [], [], now, 5m)],
            variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.49m, order.Price);
        Assert.Equal(marketEnd, order.ExpiresAtUtc);
        Assert.Contains("\"gtd_expiration_mode\":\"preopen_full_period_no_preclose_cancel\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.49", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SettlesEnteredRunFromClosedGammaMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Less60Variant.Id,
            "market-1",
            "condition-1",
            "btc-updown-5m-1778067900",
            "Bitcoin Up or Down - test",
            "Crypto",
            now.AddMinutes(-6),
            now.AddMinutes(-1),
            now.AddMinutes(-6),
            now.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-up",
            "Up",
            0.40m,
            1m,
            2.5m,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now.AddMinutes(-4),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            now.AddMinutes(-6),
            now.AddMinutes(-4));
        repository.StrategyMarketPaperRuns.Add(run);
        repository.StrategyEnabledStates[Less60Variant.Id] = false;
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            2.5m,
            0.40m,
            1m,
            0m,
            now.AddMinutes(-4),
            Less60Variant.CopiedTraderWallet));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var updatedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(0m, updatedRun.SettlementPrice);
        Assert.Equal(-1m, updatedRun.RealizedPnlUsd);

        var settlement = Assert.Single(repository.PaperPositionSettlements);
        Assert.False(settlement.Won);
        Assert.Equal(-1m, settlement.RealizedPnlUsd);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
    }

    [Fact]
    public async Task ProcessAsync_SettlesOpeningLimitRunUsingOnlyFilledShares()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var paperOrderId = Guid.NewGuid();
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Middle1Variant.Id,
            "market-1",
            "condition-1",
            "btc-updown-5m-1778067900",
            "Bitcoin Up or Down - test",
            "Crypto",
            now.AddMinutes(-6),
            now.AddMinutes(-1),
            now.AddMinutes(-6),
            now.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-down",
            "Down",
            0.50m,
            5m,
            10m,
            Guid.NewGuid(),
            paperOrderId,
            now.AddMinutes(-5),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            now.AddMinutes(-6),
            now.AddMinutes(-5));
        repository.StrategyMarketPaperRuns.Add(run);
        repository.PaperOrders.Add(new PaperOrder(
            paperOrderId,
            run.SignalId!.Value,
            Middle1Variant.CopiedTraderWallet,
            PaperOrderStatus.PartiallyFilled,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            10m,
            5m,
            now.AddMinutes(-5),
            now.AddMinutes(-1),
            StrategyId: Middle1Variant.Id));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            paperOrderId,
            0.49m,
            4m,
            now.AddMinutes(-4),
            "BalancedGtcDepth"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-down",
            "condition-1",
            "Down",
            4m,
            0.49m,
            2m,
            0.04m,
            now.AddMinutes(-4),
            Middle1Variant.CopiedTraderWallet));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, Middle1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var updatedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(4m, updatedRun.SizeShares);
        Assert.Equal(0.49m, updatedRun.EntryPrice);
        Assert.Equal(1.96m, updatedRun.StakeUsd);
        Assert.Equal(4m, updatedRun.SettlementValueUsd);
        Assert.Equal(2.04m, updatedRun.RealizedPnlUsd);

        var settlement = Assert.Single(repository.PaperPositionSettlements);
        Assert.True(settlement.Won);
        Assert.Equal(4m, settlement.SettledSizeShares);
        Assert.Equal(1.96m, settlement.CostBasisUsd);
        Assert.Equal(2.04m, settlement.RealizedPnlUsd);
        Assert.Equal(PaperOrderStatus.PartiallyFilledExpired, Assert.Single(repository.PaperOrders).Status);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
    }

    [Fact]
    public async Task ProcessAsync_FillsInitialExecutableOpeningLimitOrderBeforeSkippingUnfilledGtdRun()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var paperOrderId = Guid.NewGuid();
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Less60Variant.Id,
            "market-1",
            "condition-1",
            "btc-updown-5m-1778067900",
            "Bitcoin Up or Down - test",
            "Crypto",
            now.AddMinutes(-6),
            now.AddMinutes(-1),
            now.AddMinutes(-6),
            now.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-down",
            "Down",
            0.50m,
            3m,
            6m,
            Guid.NewGuid(),
            paperOrderId,
            now.AddMinutes(-5),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            now.AddMinutes(-6),
            now.AddMinutes(-5));
        repository.StrategyMarketPaperRuns.Add(run);
        repository.PaperOrders.Add(new PaperOrder(
            paperOrderId,
            run.SignalId!.Value,
            Less60Variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            6m,
            3m,
            now.AddMinutes(-5),
            now.AddMinutes(-1),
            StrategyId: Less60Variant.Id,
            RawDecisionJson: JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["pricing_mode"] = "paper_gtd_limit",
                ["order_type"] = "GTD",
                ["order_execution_mode"] = "GTD",
                ["paper_gtd_initial_snapshot_at_utc"] = now.AddMinutes(-5).ToString("O"),
                ["paper_gtd_initial_best_bid"] = 0.49m,
                ["paper_gtd_initial_best_ask"] = 0.50m,
                ["paper_gtd_initial_last_trade_price"] = 0.49m,
                ["paper_gtd_initial_queue_ahead_shares"] = 0m,
                ["paper_gtd_initial_executable_ask_shares"] = 6m,
                ["paper_gtd_initial_executable_ask_vwap"] = 0.50m
            }),
            ExecutionSource: "btc_updown5m_gtd_limit"));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, Less60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(paperOrderId, fill.PaperOrderId);
        Assert.Contains("ConservativeGtdImmediateFill", fill.Evidence);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Contains("filled_immediate_marketable", order.RawDecisionJson);
        var updatedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Null(updatedRun.SkipReason);
        Assert.Equal(6m, updatedRun.SizeShares);
        Assert.Equal(3m, updatedRun.StakeUsd);
        Assert.Equal(6m, updatedRun.SettlementValueUsd);
        Assert.Equal(3m, updatedRun.RealizedPnlUsd);
    }

    [Fact]
    public async Task ProcessAsync_SettlesPreOpenFullSellRunWithSellFillPnlOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        var marketStart = now.AddMinutes(-20);
        var marketEnd = now.AddMinutes(-5);
        var buyOrderId = Guid.NewGuid();
        var sellOrderId = Guid.NewGuid();
        var signalId = Guid.NewGuid();
        var run = new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            "market-1",
            "condition-1",
            "btc-updown-15m-1778067900",
            "Bitcoin Up or Down - test",
            "Crypto",
            marketStart,
            marketEnd,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Entered,
            "asset-up",
            "Up",
            0.49m,
            4.9m,
            10m,
            signalId,
            buyOrderId,
            marketStart.AddMinutes(-4),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            marketStart.AddMinutes(-6),
            marketStart.AddMinutes(-4));
        repository.StrategyMarketPaperRuns.Add(run);
        repository.PaperOrders.Add(new PaperOrder(
            buyOrderId,
            signalId,
            variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.49m,
            10m,
            4.9m,
            marketStart.AddMinutes(-5),
            marketEnd,
            FilledAtUtc: marketStart.AddMinutes(-4),
            StrategyId: variant.Id));
        repository.PaperOrders.Add(new PaperOrder(
            sellOrderId,
            Guid.NewGuid(),
            variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Sell,
            "asset-up",
            "condition-1",
            "Up",
            0.30m,
            10m,
            3m,
            marketStart.AddMinutes(12),
            marketEnd,
            FilledAtUtc: marketStart.AddMinutes(12),
            StrategyId: variant.Id,
            ExecutionSource: "btc_preopen_sell_exit"));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            buyOrderId,
            0.49m,
            10m,
            marketStart.AddMinutes(-4),
            "entry"));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            sellOrderId,
            0.30m,
            10m,
            marketStart.AddMinutes(12),
            "sell",
            RealizedPnlUsd: -1.9m));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            0m,
            0m,
            0m,
            0m,
            marketStart.AddMinutes(12),
            variant.CopiedTraderWallet));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var updatedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(10m, updatedRun.SizeShares);
        Assert.Equal(0.49m, updatedRun.EntryPrice);
        Assert.Equal(4.9m, updatedRun.StakeUsd);
        Assert.Equal(3m, updatedRun.SettlementValueUsd);
        Assert.Equal(-1.9m, updatedRun.RealizedPnlUsd);
        Assert.Empty(repository.PaperPositionSettlements);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
    }

    [Fact]
    public async Task ProcessAsync_SettlementUsesGlobalConcurrentQueueSoSlowEarlyVariantsDoNotStarvePreOpen()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var less30Variant = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 30);
        var slowRun1 = CreateEnteredSettlementRun(
            less30Variant,
            "slow-market-1",
            "slow-condition-1",
            "slow-asset-1",
            "Up",
            now.AddMinutes(-30),
            paperOrderId: null);
        var slowRun2 = CreateEnteredSettlementRun(
            Less60Variant,
            "slow-market-2",
            "slow-condition-2",
            "slow-asset-2",
            "Up",
            now.AddMinutes(-25),
            paperOrderId: null);
        var preOpenVariant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_1h_preopen_full_up_37");
        var preOpenOrderId = Guid.NewGuid();
        var preOpenRun = CreateEnteredSettlementRun(
            preOpenVariant,
            "preopen-market",
            "condition-1",
            "asset-up",
            "Up",
            now.AddMinutes(-20),
            preOpenOrderId);
        repository.StrategyMarketPaperRuns.AddRange([slowRun1, slowRun2, preOpenRun]);
        repository.PaperOrders.Add(new PaperOrder(
            preOpenOrderId,
            preOpenRun.SignalId!.Value,
            preOpenVariant.CopiedTraderWallet,
            PaperOrderStatus.PartiallyFilled,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.37m,
            5m,
            1.85m,
            now.AddMinutes(-20),
            now.AddMinutes(-1),
            StrategyId: preOpenVariant.Id));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            preOpenOrderId,
            0.37m,
            5m,
            now.AddMinutes(-19),
            "BalancedGtcDepth"));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            5m,
            0.37m,
            1.85m,
            0m,
            now.AddMinutes(-19),
            preOpenVariant.CopiedTraderWallet));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [
                TokenMetadata("asset-up", "Up", "Up"),
                TokenMetadata("asset-down", "Down", "Up")
            ],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes: [less30Variant.Code, Less60Variant.Code, preOpenVariant.Code],
                maxSettlementsPerCycle: 3,
                maxConcurrentSettlements: 3),
            gammaClient: new SlowTokenMetadataGammaClient(
                ["slow-asset-1", "slow-asset-2"],
                [
                    TokenMetadata("asset-up", "Up", "Up"),
                    TokenMetadata("asset-down", "Down", "Up")
                ]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, repository.StrategyMarketPaperRuns.Single(run => run.Id == preOpenRun.Id).Status);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, repository.StrategyMarketPaperRuns.Single(run => run.Id == slowRun1.Id).Status);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, repository.StrategyMarketPaperRuns.Single(run => run.Id == slowRun2.Id).Status);
    }

    [Fact]
    public async Task ProcessAsync_Less180MartinWaitsForThreeLess180Losses()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddSettledRun(repository, Less180Variant, "source-1", now.AddMinutes(-12), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "source-2", now.AddMinutes(-7), stakeUsd: 1m, realizedPnlUsd: -1m);
        var processor = CreateProcessorWithoutOrderBooks(repository, [], Less180MartinVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var martinRun = repository.StrategyMarketPaperRuns.Single(run => run.StrategyId == Less180MartinVariant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, martinRun.Status);
        Assert.Equal("martin_waiting_for_less180_losses_2_of_3", martinRun.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_Less180MartinEntersAfterThreeLess180Losses()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddSettledRun(repository, Less180Variant, "source-1", now.AddMinutes(-17), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "source-2", now.AddMinutes(-12), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "source-3", now.AddMinutes(-7), stakeUsd: 1m, realizedPnlUsd: -1m);
        var processor = CreateProcessorWithoutOrderBooks(repository, [], Less180MartinVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var martinRun = repository.StrategyMarketPaperRuns.Single(run => run.StrategyId == Less180MartinVariant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, martinRun.Status);
        Assert.Equal(1m, martinRun.StakeUsd);
        Assert.Equal("asset-up", martinRun.SelectedAssetId);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Less180MartinVariant.Id, order.StrategyId);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Equal(Less180MartinVariant.CopiedTraderWallet, order.CopiedTraderWallet);
    }

    [Fact]
    public async Task ProcessAsync_Less180MartinDoublesAfterOwnLossAndResetsAfterMaxStakeLoss()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddSettledRun(repository, Less180MartinVariant, "martin-loss-1", now.AddMinutes(-7), stakeUsd: 1m, realizedPnlUsd: -1m);
        var processor = CreateProcessorWithoutOrderBooks(repository, [], Less180MartinVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(2m, order.NotionalUsd);

        var resetRepository = new TestAppRepository();
        resetRepository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddSettledRun(resetRepository, Less180MartinVariant, "martin-loss-16", now.AddMinutes(-7), stakeUsd: 16m, realizedPnlUsd: -16m);
        var resetProcessor = CreateProcessorWithoutOrderBooks(resetRepository, [], Less180MartinVariant.Code);

        var resetResult = await resetProcessor.ProcessAsync();

        Assert.Equal(1, resetResult.EntriesPlaced);
        var resetOrder = Assert.Single(resetRepository.PaperOrders);
        Assert.Equal(1m, resetOrder.NotionalUsd);
    }

    [Fact]
    public async Task ProcessAsync_Less180MartinSettlesDueOwnLossBeforeNextEntryDecision()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddEnteredRun(
            repository,
            Less180MartinVariant,
            "previous-martin",
            now.AddMinutes(-5),
            selectedAssetId: "asset-up-previous-martin",
            selectedOutcome: "Up",
            stakeUsd: 1m);
        var metadata = new[]
        {
            TokenMetadata("asset-up-previous-martin", "Up", "Down"),
            TokenMetadata("asset-down-previous-martin", "Down", "Down")
        };
        var processor = CreateProcessorWithoutOrderBooks(repository, metadata, Less180MartinVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        Assert.Equal(1, result.EntriesPlaced);
        Assert.Contains(repository.StrategyMarketPaperRuns, run =>
            string.Equals(run.MarketId, "previous-martin", StringComparison.OrdinalIgnoreCase) &&
            run.Status == StrategyMarketPaperRunStatuses.Settled &&
            run.RealizedPnlUsd == -1m);
        var newRun = repository.StrategyMarketPaperRuns.Single(run =>
            run.StrategyId == Less180MartinVariant.Id &&
            string.Equals(run.MarketId, "market-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, newRun.Status);
        Assert.Equal(2m, newRun.StakeUsd);
        Assert.Equal(2m, Assert.Single(repository.PaperOrders).NotionalUsd);
    }

    [Fact]
    public async Task ProcessAsync_Less180MartinWinResetsAndWaitsForFreshLess180Losses()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-180),
            now.AddMinutes(2),
            upPrice: 0.35m,
            downPrice: 0.65m));
        AddSettledRun(repository, Less180Variant, "old-source-1", now.AddMinutes(-35), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "old-source-2", now.AddMinutes(-30), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "old-source-3", now.AddMinutes(-25), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180MartinVariant, "martin-win", now.AddMinutes(-20), stakeUsd: 4m, realizedPnlUsd: 4m);
        AddSettledRun(repository, Less180Variant, "fresh-source-1", now.AddMinutes(-12), stakeUsd: 1m, realizedPnlUsd: -1m);
        AddSettledRun(repository, Less180Variant, "fresh-source-2", now.AddMinutes(-7), stakeUsd: 1m, realizedPnlUsd: -1m);
        var processor = CreateProcessorWithoutOrderBooks(repository, [], Less180MartinVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var martinRun = repository.StrategyMarketPaperRuns.Single(run =>
            run.StrategyId == Less180MartinVariant.Id &&
            string.Equals(run.MarketId, "market-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("martin_waiting_for_less180_losses_2_of_3", martinRun.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBuysDownWhenCurrentPriceIsAboveMean()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorWithBtcReference(
            repository,
            currentBtcUsd: 103m,
            cachedBtcUsd: [99m, 101m],
            Middle1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Middle1Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);
        Assert.Equal(2.50m, run.StakeUsd);
        Assert.Equal(5m, run.SizeShares);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(0.50m, order.Price);
        Assert.Equal(5m, order.SizeShares);
        Assert.Equal(2.50m, order.NotionalUsd);
        Assert.Contains("\"pricing_mode\":\"paper_gtd_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_ttl_seconds\":240", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"configured_order_ttl_seconds\":120", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"gtd_expiration_mode\":\"market_end_relative\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"market_end_expire_before_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"post_only\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.InRange((order.ExpiresAtUtc - now).TotalSeconds, 238d, 241d);
        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceUsesDynamicBreakEvenLimitPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1Variant, now.AddHours(-3), wins: 4, losses: 6);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Middle1Variant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.StrategyId == Middle1Variant.Id && item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(0.30m, run.EntryPrice);
        Assert.Equal(2.50m, run.StakeUsd);
        Assert.Equal(8.333333333333333333333333333m, run.SizeShares);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.30m, order.Price);
        Assert.Equal(2.50m, order.NotionalUsd);
        Assert.Equal(8.333333333333333333333333333m, order.SizeShares);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_settled_runs\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_wins\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_win_rate\":0.4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_margin\":0.10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_price\":0.3", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_AlwaysUpPlacesFixedGtdLimitAfterTradingStarts()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessor(repository, [], AlwaysUpVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(AlwaysUpVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.45m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        Assert.InRange((order.ExpiresAtUtc - now).TotalSeconds, 238d, 241d);
        Assert.Contains("\"decision_source\":\"always_up_after_trading_started\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fixed\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.45", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_ttl_seconds\":240", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"configured_order_ttl_seconds\":120", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"gtd_expiration_mode\":\"market_end_relative\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendBuysDownAfterPreviousUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        for (var index = 0; index < 10; index++)
        {
            AddBtcOddsTick(
                repository,
                "previous-up-score-market",
                previousMarketStartUtc,
                index * 30,
                index == 9 ? 80m : 101m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrend35Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(PreviousScoreCounterTrend35Variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.35m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.35m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_btc_market_time_weighted_winsor_score_countertrend\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"winsor_percent\":0.10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fixed\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendBuysUpAfterPreviousDownBiasAtNinetyCents()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_5m_prev_score_countertrend_90");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        for (var index = 0; index < 10; index++)
        {
            AddBtcOddsTick(
                repository,
                "previous-down-score-market",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 99m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.90m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.90m, order.Price);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.9", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendSkipsNeutralPreviousScore()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        for (var index = 0; index < 10; index++)
        {
            AddBtcOddsTick(
                repository,
                "previous-neutral-score-market",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 100m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrend35Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_previous_score_neutral", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"previous_bias\":\"None\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"skip_reason\":\"btc_previous_score_neutral\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_DueRunsForSameVariantAreProcessedConcurrently()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository
        {
            PolymarketGammaMarketLookupDelay = TimeSpan.FromMilliseconds(75)
        };
        for (var index = 0; index < 4; index++)
        {
            var marketStartUtc = now.AddSeconds(-5 - index);
            var market = CreateMarket(
                marketStartUtc,
                marketStartUtc.AddMinutes(5),
                upPrice: 0.50m,
                downPrice: 0.50m,
                marketId: "parallel-market-" + index.ToString(CultureInfo.InvariantCulture),
                conditionId: "parallel-condition-" + index.ToString(CultureInfo.InvariantCulture),
                upAssetId: "parallel-up-" + index.ToString(CultureInfo.InvariantCulture),
                downAssetId: "parallel-down-" + index.ToString(CultureInfo.InvariantCulture),
                orderMinSize: 5m);
            repository.PolymarketGammaMarkets.Add(market);
            repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
                Guid.NewGuid(),
                AlwaysUpVariant.Id,
                market.MarketId,
                market.ConditionId,
                market.Slug,
                market.Question,
                market.Category,
                marketStartUtc,
                marketStartUtc.AddMinutes(5),
                now.AddMinutes(-1),
                marketStartUtc,
                StrategyMarketPaperRunStatuses.Observed,
                SelectedAssetId: null,
                SelectedOutcome: null,
                EntryPrice: null,
                StakeUsd: 1m,
                SizeShares: null,
                SignalId: null,
                PaperOrderId: null,
                EnteredAtUtc: null,
                SettlementPrice: null,
                SettlementValueUsd: null,
                RealizedPnlUsd: null,
                SettledAtUtc: null,
                SkipReason: null,
                now.AddMinutes(-1),
                now.AddMinutes(-1)));
        }

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes: [AlwaysUpVariant.Code],
                maxEntriesPerCycle: 4,
                maxConcurrentEntryDecisions: 4));

        var result = await processor.ProcessAsync();

        Assert.Equal(4, result.EntriesPlaced);
        Assert.True(repository.MaxConcurrentPolymarketGammaMarketLookups > 1);
        Assert.Equal(4, repository.PaperOrders.Count);
        Assert.All(
            repository.StrategyMarketPaperRuns.Where(run => run.StrategyId == AlwaysUpVariant.Id),
            run => Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status));
    }

    [Fact]
    public async Task ProcessAsync_AlwaysDownWaitsUntilMarketAcceptsOrders()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var market = CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m) with
        {
            AcceptingOrders = false
        };
        repository.PolymarketGammaMarkets.Add(market);
        var processor = CreateProcessor(repository, [], AlwaysDownVariant.Code);

        var waiting = await processor.ProcessAsync();

        Assert.Equal(1, waiting.MarketsObserved);
        Assert.Equal(0, waiting.EntriesPlaced);
        Assert.Equal(0, waiting.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, Assert.Single(repository.StrategyMarketPaperRuns).Status);

        repository.PolymarketGammaMarkets[0] = market with { AcceptingOrders = true };

        var entered = await processor.ProcessAsync();

        Assert.Equal(1, entered.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        Assert.Contains("\"decision_source\":\"always_down_after_trading_started\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_AlwaysUpSkipsWhenMarketRelativeGtdDeadlinePassed()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddMinutes(-4),
            now.AddSeconds(30),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessor(repository, [], AlwaysUpVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("opening_limit_market_relative_expiration_elapsed", run.SkipReason);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeBuysUpWhenCurrentBtcIsAboveMarketStart()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceVariant.Id] = StrategyRuntimeSettings.Default(BinanceVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_start_price_usd\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_current_price_usd\":101", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fixed\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeBuysDownWhenCurrentBtcIsBelowMarketStart()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(99m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceVariant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_move_from_start_usd\":-1", order.RawDecisionJson, StringComparison.Ordinal);
    }

    public static TheoryData<string, decimal> BinanceFixedPriceVariants =>
        new()
        {
            { "btc_up_down_5m_binance_45", 0.45m },
            { "btc_up_down_5m_binance_47", 0.47m },
            { "btc_up_down_5m_binance_49", 0.49m }
        };

    [Theory]
    [MemberData(nameof(BinanceFixedPriceVariants))]
    public async Task ProcessAsync_BinanceFixedPriceVariantsUseConfiguredLimitPrice(
        string variantCode,
        decimal expectedLimitPrice)
    {
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(candidate => candidate.Code == variantCode);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal(expectedLimitPrice, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(expectedLimitPrice, order.Price);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains($"\"limit_price\":{expectedLimitPrice.ToString(CultureInfo.InvariantCulture)}", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains($"\"fixed_limit_price\":{expectedLimitPrice.ToString(CultureInfo.InvariantCulture)}", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fixed\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBpsThresholdSkipsSmallMove()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceBps2Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100.01m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceBps2Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_reference_move_below_bps_threshold", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"btc_current_price_usd\":100.01", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_move_from_start_bps\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_start_bps\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":2", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Up\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":null", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBpsThresholdEntersWhenMoveReachesThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceBps2Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100.03m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceBps2Variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps2Variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"btc_move_from_start_bps\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_start_bps\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceHalfBpsThresholdUsesDecimalMoveThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceBps05Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100.006m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceBps05Variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps05Variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"btc_move_from_start_bps\":0.6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_start_bps\":0.6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":0.5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBpsInstantPricesOpeningLimitFromExecutableAskDepth()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.64m, 20m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.39m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceBps1InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100.02m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceBps1InstantVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps1InstantVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.64m, order.Price);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_target_size_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeWaitsForArchivedMarketStartPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeDefersEqualStartPriceWithinEntryGrace()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var firstResult = await processor.ProcessAsync();

        Assert.Equal(1, firstResult.MarketsObserved);
        Assert.Equal(0, firstResult.EntriesPlaced);
        Assert.Equal(0, firstResult.RunsSkipped);
        var observedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, observedRun.Status);
        Assert.Empty(repository.PaperOrders);

        processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache(100m));

        var secondResult = await processor.ProcessAsync();

        Assert.Equal(1, secondResult.EntriesPlaced);
        var enteredRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, enteredRun.Status);
        Assert.Equal("Up", enteredRun.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Contains("\"btc_start_price_usd\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_current_price_usd\":101", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeDefersEqualStartPriceWithinOpeningLimitTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddSeconds(-45);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketStart.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", marketStart, startPriceUsd: 100m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var firstResult = await processor.ProcessAsync();

        Assert.Equal(1, firstResult.MarketsObserved);
        Assert.Equal(0, firstResult.EntriesPlaced);
        Assert.Equal(0, firstResult.RunsSkipped);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, Assert.Single(repository.StrategyMarketPaperRuns).Status);
        Assert.Empty(repository.PaperOrders);

        AddBtcOddsTick(
            repository,
            "market-1",
            marketStart,
            sampleOffsetSeconds: 60,
            binancePriceUsd: 99m,
            startPriceUsd: 100m,
            upPriceProxy: 0.40m,
            downPriceProxy: 0.60m);
        processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var secondResult = await processor.ProcessAsync();

        Assert.Equal(1, secondResult.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("Down", run.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"btc_current_source\":\"BinanceTradeWebSocketOddsArchive\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_current_price_usd\":99", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeUsesLatestMarketOddsTickForCurrentPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 8,
            binancePriceUsd: 99m,
            startPriceUsd: 100m,
            upPriceProxy: 0.40m,
            downPriceProxy: 0.60m);

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("Down", run.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"btc_current_price_usd\":99", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_current_source\":\"BinanceTradeWebSocketOddsArchive\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceDelayedVariantRunsAtConfiguredDelay()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddSeconds(-30);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketStart.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", marketStart, startPriceUsd: 100m);
        AddBtcOddsTick(
            repository,
            "market-1",
            marketStart,
            sampleOffsetSeconds: 30,
            binancePriceUsd: 101m,
            startPriceUsd: 100m,
            upPriceProxy: 0.62m,
            downPriceProxy: 0.38m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceDelayed30Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(BinanceDelayed30Variant.Id, run.StrategyId);
        Assert.Equal(marketStart.AddSeconds(30), run.EntryDueAtUtc);
        Assert.Equal("Up", run.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"entry_delay_seconds\":30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_current_price_usd\":101", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeUsesGammaMinOrderSizeWhenOrderBookIsMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            orderMinSize: 5m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 8,
            binancePriceUsd: 101m,
            startPriceUsd: 100m,
            upPriceProxy: 0.60m,
            downPriceProxy: 0.40m);

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Equal(6.00m, order.SizeShares);
        Assert.Contains("\"stake_sizing_source\":\"gamma_market_order_min_size\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"min_order_size\":5", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeUsesStaleClobMinOrderSizeForOpeningLimitSizing()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            orderMinSize: 5m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 8,
            binancePriceUsd: 101m,
            startPriceUsd: 100m,
            upPriceProxy: 0.60m,
            downPriceProxy: 0.40m);
        var staleClobBook = OrderBook(
            "asset-up",
            [new OrderBookLevel(0.49m, 100m)],
            [new OrderBookLevel(0.51m, 100m)],
            now.AddSeconds(-5),
            minOrderSize: 5m);

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [staleClobBook],
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(6.00m, order.SizeShares);
        Assert.Contains("\"stake_sizing_source\":\"clob_book_stale_min_order_size\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"min_order_size\":5", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceCleverUsesArchiveFairValueLimitWhenCurrentBtcIsAboveMarketStart()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.45m,
            downPrice: 0.55m));
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 5,
            binancePriceUsd: 100.20m,
            startPriceUsd: 100m,
            upPriceProxy: 0.45m,
            downPriceProxy: 0.55m);
        AddBtcCleverHistoricalTicks(
            repository,
            now.AddHours(-2),
            isUp: true,
            samples: 20,
            startPriceUsd: 100m,
            moveBps: 20m,
            targetPriceProxy: 0.47m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceCleverVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100.20m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceCleverVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.43m, order.Price);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative_clever\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fair_value_model\":\"archive_weighted_knn_v1\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fair_value_candidate_samples\":20", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fair_value_edge_margin\":0.03", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"binance_clever_fair_value\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    public static TheoryData<string, decimal, decimal> BinanceCleverMarginVariants =>
        new()
        {
            { "btc_up_down_5m_binance_clever_aggressive", 0.01m, 0.45m },
            { "btc_up_down_5m_binance_clever_conservative", 0.05m, 0.41m }
        };

    [Theory]
    [MemberData(nameof(BinanceCleverMarginVariants))]
    public async Task ProcessAsync_BinanceCleverMarginVariantsUseConfiguredSafetyMargin(
        string variantCode,
        decimal expectedMargin,
        decimal expectedLimitPrice)
    {
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.BtcUpDown5mVariants.Single(candidate => candidate.Code == variantCode);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.45m,
            downPrice: 0.55m));
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 5,
            binancePriceUsd: 100.20m,
            startPriceUsd: 100m,
            upPriceProxy: 0.45m,
            downPriceProxy: 0.55m);
        AddBtcCleverHistoricalTicks(
            repository,
            now.AddHours(-2),
            isUp: true,
            samples: 20,
            startPriceUsd: 100m,
            moveBps: 20m,
            targetPriceProxy: 0.47m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            new FakeBtcUsdReferencePriceClient(100.20m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(expectedLimitPrice, order.Price);
        Assert.Contains($"\"fair_value_edge_margin\":{expectedMargin.ToString(CultureInfo.InvariantCulture)}", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains($"\"fair_value_limit_price\":{expectedLimitPrice.ToString(CultureInfo.InvariantCulture)}", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"binance_clever_fair_value\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceEdgeVariantUsesConfiguredFairValueEdge()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.45m,
            downPrice: 0.55m));
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 5,
            binancePriceUsd: 100.20m,
            startPriceUsd: 100m,
            upPriceProxy: 0.45m,
            downPriceProxy: 0.55m);
        AddBtcCleverHistoricalTicks(
            repository,
            now.AddHours(-2),
            isUp: true,
            samples: 20,
            startPriceUsd: 100m,
            moveBps: 20m,
            targetPriceProxy: 0.47m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceEdge2Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100.20m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceEdge2Variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.44m, order.Price);
        Assert.Contains("\"fair_value_edge_margin\":0.02", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fair_value_limit_price\":0.44", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceCleverSkipsWhenFairValueSampleIsInsufficient()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.45m,
            downPrice: 0.55m));
        AddBtcOddsTick(
            repository,
            "market-1",
            now,
            sampleOffsetSeconds: 5,
            binancePriceUsd: 100.20m,
            startPriceUsd: 100m,
            upPriceProxy: 0.45m,
            downPriceProxy: 0.55m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [BinanceCleverVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100.20m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_clever_fair_value_sample_insufficient", run.SkipReason);
        Assert.Contains("\"fair_value_candidate_samples\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceRevertInvertsSelectedDirectionAndUsesDynamicLimitPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1RevertVariant.Id] = StrategyRuntimeSettings.Default(Middle1RevertVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1RevertVariant, now.AddHours(-3), wins: 5, losses: 5);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Middle1RevertVariant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.StrategyId == Middle1RevertVariant.Id && item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Middle1RevertVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.40m, order.Price);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference_revert\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"revert_decision\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceRevertBootstrapsDynamicLimitFromBaseMiddleHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1RevertVariant.Id] = StrategyRuntimeSettings.Default(Middle1RevertVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1Variant, now.AddHours(-3), wins: 6, losses: 4);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Middle1RevertVariant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.30m, order.Price);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even_revert_bootstrap_from_base_middle\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_settled_runs\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_wins\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_win_rate\":0.4", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceCapsDynamicBreakEvenLimitPriceAtHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1Variant, now.AddHours(-3), wins: 9, losses: 1);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Middle1Variant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"break_even_raw_limit_price\":0.80", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_max_price\":0.50", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_price\":0.5", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceUsesBookBootstrapWhenDynamicBreakEvenSampleIsInsufficient()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1Variant, now.AddHours(-3), wins: 2, losses: 1);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Middle1Variant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.StrategyId == Middle1Variant.Id && item.MarketId == "market-1");
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Null(run.SkipReason);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even_book_bootstrap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_settled_runs\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_insufficient_reason\":\"opening_limit_break_even_sample_insufficient\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"book_bootstrap_price_source\":\"best_bid_plus_tick\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"book_bootstrap_best_bid\":0.64", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_Skip1UsesBookBestAskBootstrapWhenDynamicBreakEvenSampleIsInsufficient()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.70m, bestAsk: 0.72m, now),
            OrderBook("asset-down", bestBid: 0.28m, bestAsk: 0.29m, now)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Skip1Variant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.29m, order.Price);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even_book_bootstrap\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_settled_runs\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"book_bootstrap_price_source\":\"best_ask\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"book_bootstrap_best_ask\":0.29", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceRoundsMinimumStakeUpToWholeDollar()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.49m, 100m)],
                [new OrderBookLevel(0.50m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.49m, 100m)],
                [new OrderBookLevel(0.50m, 100m)],
                now,
                minOrderSize: 5m)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Variant.Code]),
            new FakeBtcUsdReferencePriceClient(103m),
            CreateBtcUsdReferenceCache([99m, 101m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(3m, run.StakeUsd);
        Assert.Equal(6m, run.SizeShares);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(3m, order.NotionalUsd);
        Assert.Equal(6m, order.SizeShares);
        Assert.Contains("\"minimum_notional_usd\":2.50", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"raw_target_notional_usd\":2.7500", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_notional_rounding\":\"ceil_usd\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"target_notional_usd\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"target_size_shares\":6", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceReusesFreshCurrentPriceForSameMarket()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        foreach (var variant in new[] { Middle1Variant, Middle2Variant, Middle3Variant })
        {
            repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
            {
                PaperStakeAmount = 2.50m
            };
        }

        var btcUsdReferencePriceClient = new FakeBtcUsdReferencePriceClient(103m);
        var processor = CreateProcessorWithBtcReference(
            repository,
            btcUsdReferencePriceClient,
            cachedBtcUsd: [90m, 100m, 102m],
            Middle1Variant.Code,
            Middle2Variant.Code,
            Middle3Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(3, result.MarketsObserved);
        Assert.Equal(3, result.EntriesPlaced);
        Assert.Equal(1, btcUsdReferencePriceClient.RequestCount);
        Assert.Equal(3, repository.PaperOrders.Count);
        Assert.All(repository.PaperOrders, order =>
        {
            Assert.Equal(PaperOrderStatus.Pending, order.Status);
            Assert.Equal(0.50m, order.Price);
            Assert.Equal(5m, order.SizeShares);
        });
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceSkipsWhenCurrentAndCachedSamplesDisagree()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle2Variant.Id] = StrategyRuntimeSettings.Default(Middle2Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorWithBtcReference(
            repository,
            currentBtcUsd: 110m,
            cachedBtcUsd: [100m, 90m],
            Middle2Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_reference_mixed_around_mean", run.SkipReason);
        Assert.Contains("\"required_cached_samples\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsBuysDownAfterThreeUpMarkets()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip3Variant.Id] = StrategyRuntimeSettings.Default(Skip3Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up", "Up", "Up");
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            closeBookOrderBooks,
            Skip3Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Skip3Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);
        Assert.Equal(2.50m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source_details\":[\"clob_close_book_up_midpoint\"]", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"inferred_up_midpoint\":0.6", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsDefersWhenImmediatePreviousMarketIsMissingInsideGrace()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorWithBtcReference(
            repository,
            currentBtcUsd: 100m,
            cachedBtcUsd: [100m],
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == Skip1Variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status);
        Assert.Null(run.SkipReason);
        Assert.Null(run.SkipDiagnosticsJson);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsTreatsUpMidpointAtHalfAsUp()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "TieUp");
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            closeBookOrderBooks,
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"winning_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"inferred_up_midpoint\":0.5", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsInfersUpFromSingleUpBestBid()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));

        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var previousUpAssetId = "single-side-up-" + previousSuffix;
        var previousDownAssetId = "single-side-down-" + previousSuffix;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousStart,
            now,
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "single-side-market-" + previousSuffix,
            conditionId: "single-side-condition-" + previousSuffix,
            upAssetId: previousUpAssetId,
            downAssetId: previousDownAssetId));
        var closeBook = OrderBook(
            previousUpAssetId,
            [new OrderBookLevel(0.99m, 100m)],
            [],
            now);
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            [closeBook],
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"winning_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"result_source\":\"clob_close_book_up_best_bid\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_best_bid\":0.99", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"inferred_up_price\":0.99", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsUsesStoredSnapshotWhenCloseBookFetchStopped()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));

        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var previousUpAssetId = "stored-up-" + previousSuffix;
        var previousDownAssetId = "stored-down-" + previousSuffix;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousStart,
            now,
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "stored-market-" + previousSuffix,
            conditionId: "stored-condition-" + previousSuffix,
            upAssetId: previousUpAssetId,
            downAssetId: previousDownAssetId));
        await repository.AddOrderBookSnapshotAsync(OrderBook(
            previousDownAssetId,
            [],
            [new OrderBookLevel(0.01m, 100m)],
            now.AddSeconds(-10)));
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            clobOrderBooks: [],
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"winning_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"result_source\":\"stored_close_book_snapshot_down_best_ask_complement\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_best_ask\":0.01", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"inferred_up_price\":0.99", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_CapturesClosingOrderBooksBeforeMarketClose()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddMinutes(-4),
            now.AddSeconds(30),
            upPrice: 0.50m,
            downPrice: 0.50m,
            upAssetId: "closing-up",
            downAssetId: "closing-down"));
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            [
                OrderBook("closing-up", bestBid: 0.99m, bestAsk: 1.00m, now),
                OrderBook("closing-down", bestBid: 0.00m, bestAsk: 0.01m, now)
            ],
            Skip1Variant.Code);

        await processor.ProcessAsync();

        Assert.Contains(repository.OrderBookSnapshots, snapshot => snapshot.AssetId == "closing-up");
        Assert.Contains(repository.OrderBookSnapshots, snapshot => snapshot.AssetId == "closing-down");
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsSkipsAndRecordsDiagnosticsWhenCloseBookUnavailable()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-5);
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));

        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousStart,
            now,
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "close-market-missing-book-" + previousSuffix,
            conditionId: "close-condition-missing-book-" + previousSuffix,
            upAssetId: "missing-close-up-" + previousSuffix,
            downAssetId: "missing-close-down-" + previousSuffix));
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            clobOrderBooks: [],
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.True(result.RunsSkipped >= 1);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Skip1Variant.Id &&
            item.MarketId == "market-1");
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_previous_close_book_orderbook_unavailable", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"diagnostic_type\":\"btc_skip_close_book_result_lookup\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"btc_close_book_price_evidence_unavailable\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"orderbook_unavailable\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_lookup_reason\":\"missing_orderbook_rest_missing\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsDefersWhenPreviousSequenceHasGapInsideGrace()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip3Variant.Id] = StrategyRuntimeSettings.Default(Skip3Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            closeBookOrderBooks,
            Skip3Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.True(result.RunsSkipped >= 0);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == Skip3Variant.Id &&
            item.MarketStartUtc == now);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status);
        Assert.Null(run.SkipReason);
        Assert.Null(run.SkipDiagnosticsJson);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsEntersAfterDeferredPreviousMarketResultArrives()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorWithBtcReference(
            repository,
            currentBtcUsd: 100m,
            cachedBtcUsd: [100m],
            Skip1Variant.Code);

        var firstResult = await processor.ProcessAsync();

        Assert.Equal(0, firstResult.EntriesPlaced);
        Assert.Equal(0, firstResult.RunsSkipped);
        var observedRun = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == Skip1Variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, observedRun.Status);

        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            closeBookOrderBooks,
            Skip1Variant.Code);

        var secondResult = await processor.ProcessAsync();

        Assert.Equal(1, secondResult.EntriesPlaced);
        var enteredRun = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Skip1Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, enteredRun.Status);
        Assert.Equal("Down", enteredRun.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsWaitsPastEntryGraceUntilPreviousMarketResultArrives()
    {
        var marketStart = DateTimeOffset.UtcNow.AddSeconds(-30);
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStart,
            marketStart.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorWithBtcReference(
            repository,
            currentBtcUsd: 100m,
            cachedBtcUsd: [100m],
            Skip1Variant.Code);

        var firstResult = await processor.ProcessAsync();

        Assert.Equal(0, firstResult.EntriesPlaced);
        Assert.Equal(0, firstResult.RunsSkipped);
        var observedRun = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == Skip1Variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, observedRun.Status);

        var closeBookOrderBooks = AddCloseBookResults(repository, marketStart, "Up");
        processor = CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            new FakeBtcUsdReferencePriceClient(100m),
            cachedBtcUsd: [100m],
            closeBookOrderBooks,
            Skip1Variant.Code);

        var secondResult = await processor.ProcessAsync();

        Assert.Equal(1, secondResult.EntriesPlaced);
        var enteredRun = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Skip1Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, enteredRun.Status);
        Assert.Equal("Down", enteredRun.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Contains("\"decision_seconds_after_market_start\":", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"strict_previous_result_lags\":", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsUsesDynamicBreakEvenLimitPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip3Variant.Id] = StrategyRuntimeSettings.Default(Skip3Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up", "Up", "Up");
        AddOpeningLimitBreakEvenHistory(repository, Skip3Variant, now.AddHours(-3), wins: 5, losses: 5);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Skip3Variant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.StrategyId == Skip3Variant.Id && item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.40m, order.Price);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_wins\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_price\":0.4", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsRevertInvertsSelectedDirectionAndUsesDynamicLimitPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip3RevertVariant.Id] = StrategyRuntimeSettings.Default(Skip3RevertVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up", "Up", "Up");
        AddOpeningLimitBreakEvenHistory(repository, Skip3RevertVariant, now.AddHours(-3), wins: 5, losses: 5);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Skip3RevertVariant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.StrategyId == Skip3RevertVariant.Id && item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Skip3RevertVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.40m, order.Price);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence_revert\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"revert_decision\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SkipConsecutiveResultsRevertBootstrapsDynamicLimitFromBaseSkipHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip3RevertVariant.Id] = StrategyRuntimeSettings.Default(Skip3RevertVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up", "Up", "Up");
        AddOpeningLimitBreakEvenHistory(repository, Skip3Variant, now.AddHours(-3), wins: 6, losses: 4);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [Skip3RevertVariant.Code],
                openingLimitDynamicBreakEvenPricingEnabled: true,
                openingLimitBreakEvenLookbackRuns: 10,
                openingLimitBreakEvenMinSettledRuns: 10),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.30m, order.Price);
        Assert.Contains("\"limit_pricing_mode\":\"dynamic_break_even_revert_bootstrap_from_base_skip\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_settled_runs\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_wins\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"break_even_win_rate\":0.4", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EnsembleVoteEntersWhenTwoSignalsAgree()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[EnsembleVariant.Id] = StrategyRuntimeSettings.Default(EnsembleVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Down");
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [EnsembleVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache([102m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == EnsembleVariant.Id &&
            item.MarketId == "market-1");
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(EnsembleVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Contains("\"decision_source\":\"ensemble_vote_2_of_3\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"required_votes\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_votes\":3", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_DynamicMarkovSelectsLikelyNextOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[DynamicMarkovVariant.Id] = StrategyRuntimeSettings.Default(DynamicMarkovVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));

        var sequenceStart = now.AddMinutes(-115);
        for (var index = 0; index < 22; index++)
        {
            AddBtcSettledMarketResult(
                repository,
                sequenceStart.AddMinutes(index * 5),
                index % 2 == 0 ? "Up" : "Down");
        }

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [DynamicMarkovVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(DynamicMarkovVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Contains("\"decision_source\":\"btc_result_markov_transition\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_probability\":1", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_StrategySelectorReusesBestPositiveCandidateSignal()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[StrategySelectorVariant.Id] = StrategyRuntimeSettings.Default(StrategySelectorVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddOpeningLimitBreakEvenHistory(repository, Middle1Variant, now.AddHours(-3), wins: 10, losses: 0);
        AddOpeningLimitBreakEvenHistory(repository, BinanceVariant, now.AddHours(-4), wins: 1, losses: 9);

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [StrategySelectorVariant.Code]),
            new FakeBtcUsdReferencePriceClient(99m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(StrategySelectorVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Contains("\"decision_source\":\"recent_paper_strategy_selector\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_candidate_strategy_code\":\"btc_up_down_5m_middle_1\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_LiveStakeWithInsufficientStrategyBalanceDisablesLiveStakes()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 1m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.False(repository.StrategySettings[Skip1Variant.Id].LiveStakes);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, liveOrder.Status);
        Assert.Equal("GTD", liveOrder.OrderType);
        Assert.Contains("live available balance is insufficient", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "StrategyLiveBalance");
    }

    [Fact]
    public async Task ProcessAsync_Skip1LiveStakeCreatesPaperShadowAndGtdLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.GTD, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.NotNull(request.GtdExpirationUtc);
        Assert.InRange((request.GtdExpirationUtc.Value - request.CreatedAtUtc).TotalSeconds, 295d, 301d);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal("GTD", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Live, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.InRange((liveOrder.ExpiresAtUtc - liveOrder.CreatedAtUtc).TotalSeconds, 235d, 241d);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-down", paperOrder.AssetId);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps1LiveStakeCreatesPaperShadowAndGtdLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps1Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            100.02m,
            [100m],
            [],
            BinanceBps1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.GTD, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.NotNull(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps1Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("GTD", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Live, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps1Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":1", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps1Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps2LiveStakeCreatesPaperShadowAndGtdLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps2Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps2Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            100.03m,
            [100m],
            [],
            BinanceBps2Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.GTD, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.NotNull(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps2Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("GTD", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Live, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps2Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":2", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps2Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps1LiveStakeIgnoresPaperExposureForLiveCaps()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps1Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            string.Empty,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "old-paper-asset",
            "condition-1",
            "Up",
            0.50m,
            2_000m,
            1_000m,
            now.AddMinutes(-10),
            now.AddMinutes(5),
            FilledAtUtc: null,
            CancelledAtUtc: null,
            StrategyId: Skip1Variant.Id,
            RawDecisionJson: "{}"));
        repository.PaperPositions.Add(new PaperPosition(
            "old-paper-position-asset",
            "condition-1",
            "Up",
            2_000m,
            0.50m,
            1_000m,
            0m,
            now.AddMinutes(-1)));
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            100.02m,
            [100m],
            [],
            BinanceBps1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Live, liveOrder.Status);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.DoesNotContain("Live market exposure", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Live total deployed exposure", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);

        var shadowPaper = Assert.Single(
            repository.PaperOrders,
            order => order.ExecutionSource == "paper_live_shadow_test");
        Assert.Equal(liveOrder.CorrelationId, shadowPaper.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, shadowPaper.Id);
    }

    [Fact]
    public async Task ProcessAsync_Skip1LiveStakeUsesMatchedSubmitAmountsForActualFill()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xorder",
                "matched",
                null,
                "0.25",
                "5",
                """{"status":"matched","makingAmount":"0.25","takingAmount":"5"}""",
                "{}")
        };
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal(5m, liveOrder.SizeShares);
        Assert.Equal(5m, liveOrder.FilledSize);
        Assert.Equal(0m, liveOrder.RemainingSize);
        Assert.Equal(0.05m, liveOrder.AverageFillPrice);
        Assert.Equal(0.25m, liveOrder.FilledNotionalUsd);
        Assert.Equal(0.25m, liveOrder.CostBasisUsd);
    }

    [Fact]
    public async Task ProcessAsync_Skip1LiveStakeRefusesFutureMarketWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var futureStartUtc = now.AddDays(1);
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        var market = CreateMarket(
            futureStartUtc,
            futureStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m);
        repository.PolymarketGammaMarkets.Add(market);
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Skip1Variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            market.Category,
            futureStartUtc,
            futureStartUtc.AddMinutes(5),
            now,
            now,
            StrategyMarketPaperRunStatuses.Observed,
            SelectedAssetId: null,
            SelectedOutcome: null,
            EntryPrice: null,
            StakeUsd: 2.50m,
            SizeShares: null,
            SignalId: null,
            PaperOrderId: null,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            now,
            now));
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.LiveOrders);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessor(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCore(repository, metadata, DefaultOrderBooks(), enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithoutOrderBooks(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCore(repository, metadata, [], enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithBtcReference(
        TestAppRepository repository,
        decimal currentBtcUsd,
        IReadOnlyList<decimal> cachedBtcUsd,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorWithBtcReference(
            repository,
            new FakeBtcUsdReferencePriceClient(currentBtcUsd),
            cachedBtcUsd,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithBtcReference(
        TestAppRepository repository,
        FakeBtcUsdReferencePriceClient btcUsdReferencePriceClient,
        IReadOnlyList<decimal> cachedBtcUsd,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorWithBtcReferenceAndClobOrderBooks(
            repository,
            btcUsdReferencePriceClient,
            cachedBtcUsd,
            [],
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithBtcReferenceAndClobOrderBooks(
        TestAppRepository repository,
        FakeBtcUsdReferencePriceClient btcUsdReferencePriceClient,
        IReadOnlyList<decimal> cachedBtcUsd,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            clobOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, enabledVariantCodes),
            btcUsdReferencePriceClient,
            CreateBtcUsdReferenceCache(cachedBtcUsd));
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveProcessor(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        params string[] enabledVariantCodes)
    {
        return CreateLiveProcessor(repository, tradingClient, [], enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveProcessor(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
    {
        return CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            100m,
            [100m],
            clobOrderBooks,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveProcessorWithBtcReference(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        decimal currentBtcUsd,
        IReadOnlyList<decimal> cachedBtcUsd,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
    {
        var marketDataWebSocketOptions = new MarketDataWebSocketOptions { StaleAfterSeconds = 30 };
        var marketDataCache = new MarketDataCache(marketDataWebSocketOptions);
        var orderBooks = DefaultOrderBooks();
        marketDataCache.ReplaceSubscribedAssets(orderBooks.Select(orderBook => orderBook.AssetId).ToArray());
        foreach (var orderBook in orderBooks)
        {
            marketDataCache.ApplyUpdate(new MarketDataUpdate(
                MarketDataEventType.Book,
                "book",
                orderBook.AssetId,
                orderBook.ConditionId,
                orderBook,
                orderBook.BestBid,
                orderBook.BestAsk,
                null,
                null,
                TradeSide.Unknown,
                false,
                orderBook.SnapshotAtUtc));
        }

        return new BtcUpDown5mPaperStrategyProcessor(
            NullLogger<BtcUpDown5mPaperStrategyProcessor>.Instance,
            new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x1111111111111111111111111111111111111111",
                SignatureType = "EOA"
            },
            new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 2.50m },
            new BtcUpDown5mStrategyOptions
            {
                StakeUsd = 1m,
                EntryGraceSeconds = 10,
                MaxMarketsPerCycle = 500,
                MaxEntriesPerCycle = 25,
                MaxSettlementsPerCycle = 50,
                EnabledVariantCodes = enabledVariantCodes.ToList(),
                PaperTakerPricingEnabled = true,
                PaperTakerRestFallbackEnabled = true,
                PaperTakerMaxQuoteAgeMilliseconds = 1_500,
                PaperTakerMaxEntryPrice = 0.80m,
                PaperTakerMaxReferenceSlippage = 0.05m,
                PaperTakerMaxSpreadAbs = 0.20m,
                OpeningLimitDynamicBreakEvenPricingEnabled = false,
                OpeningLimitMaxPrice = 0.50m,
                OpeningLimitPriceTickSize = 0.01m
            },
            marketDataWebSocketOptions,
            new FakeGammaClient([]),
            new FakeClobClient(orderBooks.Concat(clobOrderBooks).ToArray()),
            new PassGeoClient(),
            tradingClient,
            new ReadyAuthService(),
            new FakeBtcUsdReferencePriceClient(currentBtcUsd),
            CreateBtcUsdReferenceCache(cachedBtcUsd),
            marketDataCache,
            new ActiveMarketAssetSubscriptionRegistry(),
            new ExposureSnapshotCache(repository),
            new ServiceControlState(),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithRegistryBestAsk(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        string assetId,
        decimal bestBid,
        decimal bestAsk,
        DateTimeOffset updatedAtUtc,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorWithRegistryBestAskCore(
            repository,
            metadata,
            assetId,
            bestBid,
            bestAsk,
            updatedAtUtc,
            null,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithRegistryBestAskAndClob(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        string assetId,
        decimal bestBid,
        decimal bestAsk,
        DateTimeOffset updatedAtUtc,
        OrderBookSnapshot clobOrderBook,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorWithRegistryBestAskCore(
            repository,
            metadata,
            assetId,
            bestBid,
            bestAsk,
            updatedAtUtc,
            clobOrderBook,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorWithRegistryBestAskCore(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        string assetId,
        decimal bestBid,
        decimal bestAsk,
        DateTimeOffset updatedAtUtc,
        OrderBookSnapshot? clobOrderBook,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCore(
            repository,
            metadata,
            [],
            registry =>
            {
                registry.AddOrUpdateMarkets(repository.PolymarketGammaMarkets);
                registry.ApplyMarketDataUpdate(new MarketDataUpdate(
                    MarketDataEventType.BestBidAsk,
                    "best_bid_ask",
                    assetId,
                    "condition-1",
                    null,
                    bestBid,
                    bestAsk,
                    null,
                    null,
                    TradeSide.Unknown,
                    false,
                    updatedAtUtc));
            },
            clobOrderBook,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorCore(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCore(
            repository,
            metadata,
            orderBooks,
            _ => { },
            null,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorCore(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        Action<ActiveMarketAssetSubscriptionRegistry> configureRegistry,
        OrderBookSnapshot? clobOrderBook,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            metadata,
            orderBooks,
            configureRegistry,
            clobOrderBook,
            CreateBtcOptions(paperTakerPricingEnabled: false, enabledVariantCodes));
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateTakerProcessorCore(
        TestAppRepository repository,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        OrderBookSnapshot? clobOrderBook,
        params string[] enabledVariantCodes)
    {
        return CreateTakerProcessorCore(
            repository,
            orderBooks,
            ToClobOrderBooks(clobOrderBook),
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateTakerProcessorCore(
        TestAppRepository repository,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            clobOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: true, enabledVariantCodes));
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorCoreWithOptions(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        Action<ActiveMarketAssetSubscriptionRegistry> configureRegistry,
        OrderBookSnapshot? clobOrderBook,
        BtcUpDown5mStrategyOptions strategyOptions)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            metadata,
            orderBooks,
            configureRegistry,
            ToClobOrderBooks(clobOrderBook),
            strategyOptions);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateProcessorCoreWithOptions(
        TestAppRepository repository,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        Action<ActiveMarketAssetSubscriptionRegistry> configureRegistry,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        BtcUpDown5mStrategyOptions strategyOptions,
        IBtcUsdReferencePriceClient? btcUsdReferencePriceClient = null,
        IBtcUsdReferencePriceCache? btcUsdReferencePriceCache = null,
        IPolymarketGammaClient? gammaClient = null,
        IPolymarketClobPublicClient? clobClient = null)
    {
        var marketDataWebSocketOptions = new MarketDataWebSocketOptions { StaleAfterSeconds = 30 };
        var marketDataCache = new MarketDataCache(marketDataWebSocketOptions);
        marketDataCache.ReplaceSubscribedAssets(orderBooks.Select(orderBook => orderBook.AssetId).ToArray());
        foreach (var orderBook in orderBooks)
        {
            marketDataCache.ApplyUpdate(new MarketDataUpdate(
                MarketDataEventType.Book,
                "book",
                orderBook.AssetId,
                orderBook.ConditionId,
                orderBook,
                orderBook.BestBid,
                orderBook.BestAsk,
                null,
                null,
                TradeSide.Unknown,
                false,
                orderBook.SnapshotAtUtc));
        }

        var activeMarketAssetSubscriptionRegistry = new ActiveMarketAssetSubscriptionRegistry();
        configureRegistry(activeMarketAssetSubscriptionRegistry);

        return new BtcUpDown5mPaperStrategyProcessor(
            NullLogger<BtcUpDown5mPaperStrategyProcessor>.Instance,
            new BotOptions { Mode = BotMode.Paper },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x1111111111111111111111111111111111111111",
                SignatureType = "EOA"
            },
            new PaperTradingOptions { InitialBankrollUsd = 10_000m },
            new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 2.50m },
            strategyOptions,
            marketDataWebSocketOptions,
            gammaClient ?? new FakeGammaClient(metadata),
            clobClient ?? new FakeClobClient(clobOrderBooks),
            new PassGeoClient(),
            new CapturingTradingClient(),
            new ReadyAuthService(),
            btcUsdReferencePriceClient ?? new FakeBtcUsdReferencePriceClient(100m),
            btcUsdReferencePriceCache ?? CreateBtcUsdReferenceCache(100m),
            marketDataCache,
            activeMarketAssetSubscriptionRegistry,
            new ExposureSnapshotCache(repository),
            new ServiceControlState(),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository);
    }

    private static IReadOnlyList<OrderBookSnapshot> ToClobOrderBooks(OrderBookSnapshot? orderBook)
    {
        return orderBook is null ? [] : [orderBook];
    }

    private static BtcUpDown5mStrategyOptions CreateBtcOptions(
        bool paperTakerPricingEnabled,
        IReadOnlyCollection<string> enabledVariantCodes,
        bool openingLimitDynamicBreakEvenPricingEnabled = false,
        int openingLimitBreakEvenLookbackRuns = 100,
        int openingLimitBreakEvenMinSettledRuns = 30,
        decimal openingLimitBreakEvenMargin = 0.10m,
        int maxEntriesPerCycle = 25,
        int maxConcurrentEntryDecisions = 1,
        int maxSettlementsPerCycle = 50,
        int maxConcurrentSettlements = 1,
        int maxMarketsPerCycle = 500)
    {
        return new BtcUpDown5mStrategyOptions
        {
            StakeUsd = 1m,
            EntryGraceSeconds = 10,
            MaxMarketsPerCycle = maxMarketsPerCycle,
            MaxEntriesPerCycle = maxEntriesPerCycle,
            MaxConcurrentEntryDecisions = maxConcurrentEntryDecisions,
            MaxSettlementsPerCycle = maxSettlementsPerCycle,
            MaxConcurrentSettlements = maxConcurrentSettlements,
            MartinStakeLevels = 5,
            EnabledVariantCodes = enabledVariantCodes.ToList(),
            PaperTakerPricingEnabled = paperTakerPricingEnabled,
            PaperTakerRestFallbackEnabled = true,
            PaperTakerMaxQuoteAgeMilliseconds = 1_500,
            PaperTakerMaxEntryPrice = 0.80m,
            PaperTakerMaxReferenceSlippage = 0.05m,
            PaperTakerMaxSpreadAbs = 0.20m,
            PaperTakerMaxGammaClobDiff = 0.20m,
            OpeningLimitDynamicBreakEvenPricingEnabled = openingLimitDynamicBreakEvenPricingEnabled,
            OpeningLimitBreakEvenLookbackRuns = openingLimitBreakEvenLookbackRuns,
            OpeningLimitBreakEvenMinSettledRuns = openingLimitBreakEvenMinSettledRuns,
            OpeningLimitBreakEvenMargin = openingLimitBreakEvenMargin,
            OpeningLimitMaxPrice = 0.50m,
            OpeningLimitPriceTickSize = 0.01m
        };
    }

    private static IBtcUsdReferencePriceCache CreateBtcUsdReferenceCache(params decimal[] prices)
    {
        return CreateBtcUsdReferenceCache((IReadOnlyList<decimal>)prices);
    }

    private static IBtcUsdReferencePriceCache CreateBtcUsdReferenceCache(IReadOnlyList<decimal> prices)
    {
        var cache = new BtcUsdReferencePriceCache(new CoinbaseExchangeOptions { WindowSize = 100 });
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < prices.Count; index++)
        {
            cache.Add(new BtcUsdReferencePricePoint(
                prices[index],
                now.AddMinutes(index - prices.Count),
                now.AddMinutes(index - prices.Count),
                "Test"));
        }

        return cache;
    }

    private static IReadOnlyList<OrderBookSnapshot> DefaultOrderBooks()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            OrderBook("asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
        ];
    }

    private static OrderBookSnapshot OrderBook(
        string assetId,
        decimal bestBid,
        decimal bestAsk,
        DateTimeOffset now)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(bestBid, 1_000m)],
            [new OrderBookLevel(bestAsk, 1_000m)],
            now,
            "condition-1");
    }

    private static OrderBookSnapshot OrderBook(
        string assetId,
        IReadOnlyList<OrderBookLevel> bids,
        IReadOnlyList<OrderBookLevel> asks,
        DateTimeOffset now,
        decimal? minOrderSize = null)
    {
        return new OrderBookSnapshot(
            assetId,
            bids,
            asks,
            now,
            "condition-1",
            minOrderSize);
    }

    private static IReadOnlyList<OrderBookSnapshot> AddCloseBookResults(
        TestAppRepository repository,
        DateTimeOffset currentMarketStartUtc,
        params string[] winningOutcomes)
    {
        var orderBooks = new List<OrderBookSnapshot>();
        for (var index = 0; index < winningOutcomes.Length; index++)
        {
            var marketStartUtc = currentMarketStartUtc.AddMinutes(-5 * (index + 1));
            var marketEndUtc = marketStartUtc.AddMinutes(5);
            var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var upAssetId = "close-up-" + suffix;
            var downAssetId = "close-down-" + suffix;
            repository.PolymarketGammaMarkets.Add(CreateMarket(
                marketStartUtc,
                marketEndUtc,
                upPrice: 0.50m,
                downPrice: 0.50m,
                marketId: "close-market-" + suffix,
                conditionId: "close-condition-" + suffix,
                upAssetId: upAssetId,
                downAssetId: downAssetId));

            var tieUp = string.Equals(winningOutcomes[index], "TieUp", StringComparison.OrdinalIgnoreCase);
            var upWon = tieUp || string.Equals(winningOutcomes[index], "Up", StringComparison.OrdinalIgnoreCase);
            orderBooks.Add(OrderBook(
                upAssetId,
                bestBid: tieUp ? 0.49m : upWon ? 0.58m : 0.38m,
                bestAsk: tieUp ? 0.51m : upWon ? 0.62m : 0.42m,
                DateTimeOffset.UtcNow));
            orderBooks.Add(OrderBook(
                downAssetId,
                bestBid: upWon ? 0.38m : 0.58m,
                bestAsk: upWon ? 0.42m : 0.62m,
                DateTimeOffset.UtcNow));
        }

        return orderBooks;
    }

    private static PolymarketGammaMarket CreateMarket(
        DateTimeOffset windowStartUtc,
        DateTimeOffset endUtc,
        decimal upPrice,
        decimal downPrice,
        string? slug = null,
        string seriesSlug = "btc-up-or-down-5m",
        string question = "Bitcoin Up or Down - test",
        string marketId = "market-1",
        string conditionId = "condition-1",
        string upAssetId = "asset-up",
        string downAssetId = "asset-down",
        decimal? orderMinSize = null)
    {
        return new PolymarketGammaMarket(
            marketId,
            conditionId,
            "question-1",
            slug ?? $"btc-updown-5m-{windowStartUtc.ToUnixTimeSeconds()}",
            question,
            null,
            null,
            null,
            seriesSlug,
            "Crypto",
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: true,
            AcceptingOrders: true,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: null,
            LiquidityClob: null,
            Volume: null,
            Volume24Hr: null,
            BestBid: null,
            BestAsk: null,
            Spread: null,
            CreatedAtUtc: windowStartUtc.AddMinutes(-10),
            UpdatedAtUtc: windowStartUtc.AddMinutes(1),
            StartDateUtc: null,
            EndDateUtc: endUtc,
            EventStartTimeUtc: windowStartUtc,
            Outcomes: ["Up", "Down"],
            ClobTokenIds: [upAssetId, downAssetId],
            RawJson: "{\"outcomePrices\":\"[\\\"" +
                upPrice.ToString(CultureInfo.InvariantCulture) +
                "\\\", \\\"" +
                downPrice.ToString(CultureInfo.InvariantCulture) +
                "\\\"]\"}",
            FetchedAtUtc: DateTimeOffset.UtcNow,
            OrderMinSize: orderMinSize);
    }

    private static void AddSettledRun(
        TestAppRepository repository,
        BtcUpDown5mStrategyVariant variant,
        string marketId,
        DateTimeOffset marketStartUtc,
        decimal stakeUsd,
        decimal realizedPnlUsd)
    {
        var selectedOutcome = variant.Direction == BtcUpDown5mStrategyDirection.Less ? "Up" : "Down";
        var selectedAssetId = selectedOutcome == "Up" ? "asset-up-" + marketId : "asset-down-" + marketId;
        var entryPrice = 0.50m;
        var sizeShares = stakeUsd / entryPrice;
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            marketId,
            "condition-" + marketId,
            "btc-updown-5m-" + marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "Bitcoin Up or Down - " + marketId,
            "Crypto",
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            marketStartUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            StrategyMarketPaperRunStatuses.Settled,
            selectedAssetId,
            selectedOutcome,
            entryPrice,
            stakeUsd,
            sizeShares,
            Guid.NewGuid(),
            Guid.NewGuid(),
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            realizedPnlUsd >= 0m ? 1m : 0m,
            stakeUsd + realizedPnlUsd,
            realizedPnlUsd,
            marketStartUtc.AddMinutes(5),
            SkipReason: null,
            marketStartUtc,
            marketStartUtc.AddMinutes(5)));
    }

    private static StrategyMarketPaperRun CreateEnteredSettlementRun(
        BtcUpDown5mStrategyVariant variant,
        string marketId,
        string conditionId,
        string selectedAssetId,
        string selectedOutcome,
        DateTimeOffset marketStartUtc,
        Guid? paperOrderId)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            marketId,
            conditionId,
            "btc-updown-5m-" + marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "Bitcoin Up or Down - " + marketId,
            "Crypto",
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            marketStartUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            StrategyMarketPaperRunStatuses.Entered,
            selectedAssetId,
            selectedOutcome,
            0.37m,
            1.85m,
            5m,
            Guid.NewGuid(),
            paperOrderId,
            marketStartUtc.AddSeconds(Math.Max(0, variant.EntryDelaySeconds)),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            marketStartUtc,
            marketStartUtc.AddSeconds(Math.Max(0, variant.EntryDelaySeconds)));
    }

    private static StrategyMarketPaperRun CreateObservedRun(
        BtcUpDown5mStrategyVariant variant,
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset detectedAtUtc)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            market.MarketId,
            market.ConditionId,
            market.Slug,
            market.Question,
            market.Category,
            marketStartUtc,
            market.EndDateUtc,
            detectedAtUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            StrategyMarketPaperRunStatuses.Observed,
            SelectedAssetId: null,
            SelectedOutcome: null,
            EntryPrice: null,
            StakeUsd: 1m,
            SizeShares: null,
            SignalId: null,
            PaperOrderId: null,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            detectedAtUtc,
            detectedAtUtc);
    }

    private static void AddBtcSettledMarketResult(
        TestAppRepository repository,
        DateTimeOffset marketStartUtc,
        string winningOutcome)
    {
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            BinanceVariant.Id,
            "result-market-" + suffix,
            "result-condition-" + suffix,
            "btc-updown-5m-" + suffix,
            "Bitcoin Up or Down - " + suffix,
            "Crypto",
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            marketStartUtc,
            marketStartUtc,
            StrategyMarketPaperRunStatuses.Settled,
            string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase)
                ? "asset-up-" + suffix
                : "asset-down-" + suffix,
            winningOutcome,
            0.50m,
            1m,
            2m,
            Guid.NewGuid(),
            Guid.NewGuid(),
            marketStartUtc,
            1m,
            2m,
            1m,
            marketStartUtc.AddMinutes(5),
            SkipReason: null,
            marketStartUtc,
            marketStartUtc.AddMinutes(5)));
    }

    private static void AddBtcOddsStartTick(
        TestAppRepository repository,
        string marketId,
        DateTimeOffset marketStartUtc,
        decimal startPriceUsd)
    {
        repository.BtcUpDown5mOddsTicks.Add(new BtcUpDown5mOddsTick(
            Guid.NewGuid(),
            marketId,
            "condition-1",
            $"btc-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            marketStartUtc,
            0m,
            300m,
            startPriceUsd,
            marketStartUtc,
            marketStartUtc,
            startPriceUsd,
            0m,
            0m,
            "asset-up",
            0.49m,
            0.51m,
            0.50m,
            0.50m,
            "mid",
            null,
            "test",
            0,
            "asset-down",
            0.49m,
            0.51m,
            0.50m,
            0.50m,
            "mid",
            null,
            "test",
            0,
            "{}",
            marketStartUtc));
    }

    private static void AddBtcCleverHistoricalTicks(
        TestAppRepository repository,
        DateTimeOffset latestMarketStartUtc,
        bool isUp,
        int samples,
        decimal startPriceUsd,
        decimal moveBps,
        decimal targetPriceProxy)
    {
        for (var index = 0; index < samples; index++)
        {
            var marketStartUtc = latestMarketStartUtc.AddMinutes(-5 * index);
            var signedMoveBps = isUp ? moveBps : -moveBps;
            var binancePriceUsd = startPriceUsd * (1m + signedMoveBps / 10_000m);
            var upPriceProxy = isUp
                ? targetPriceProxy
                : 1m - targetPriceProxy;
            var downPriceProxy = !isUp
                ? targetPriceProxy
                : 1m - targetPriceProxy;
            AddBtcOddsTick(
                repository,
                "history-" + index.ToString(CultureInfo.InvariantCulture),
                marketStartUtc,
                sampleOffsetSeconds: 5,
                binancePriceUsd,
                startPriceUsd,
                upPriceProxy,
                downPriceProxy);
        }
    }

    private static void AddBtcOddsTick(
        TestAppRepository repository,
        string marketId,
        DateTimeOffset marketStartUtc,
        int sampleOffsetSeconds,
        decimal binancePriceUsd,
        decimal startPriceUsd,
        decimal upPriceProxy,
        decimal downPriceProxy)
    {
        var sampledAtUtc = marketStartUtc.AddSeconds(sampleOffsetSeconds);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var moveUsd = binancePriceUsd - startPriceUsd;
        var moveBps = startPriceUsd == 0m ? 0m : moveUsd / startPriceUsd * 10_000m;
        var upBid = Math.Max(0.01m, upPriceProxy - 0.01m);
        var upAsk = Math.Min(0.99m, upPriceProxy + 0.01m);
        var downBid = Math.Max(0.01m, downPriceProxy - 0.01m);
        var downAsk = Math.Min(0.99m, downPriceProxy + 0.01m);
        repository.BtcUpDown5mOddsTicks.Add(new BtcUpDown5mOddsTick(
            Guid.NewGuid(),
            marketId,
            "condition-" + marketId,
            $"btc-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            marketStartUtc,
            marketEndUtc,
            sampledAtUtc,
            sampleOffsetSeconds,
            Math.Max(0m, (decimal)(marketEndUtc - sampledAtUtc).TotalSeconds),
            binancePriceUsd,
            sampledAtUtc,
            sampledAtUtc,
            startPriceUsd,
            moveUsd,
            moveBps,
            "asset-up-" + marketId,
            upBid,
            upAsk,
            upPriceProxy,
            upPriceProxy,
            "mid",
            null,
            "websocket_cache",
            0,
            "asset-down-" + marketId,
            downBid,
            downAsk,
            downPriceProxy,
            downPriceProxy,
            "mid",
            null,
            "websocket_cache",
            0,
            "{}",
            sampledAtUtc));
    }

    private static void AddOpeningLimitBreakEvenHistory(
        TestAppRepository repository,
        BtcUpDown5mStrategyVariant variant,
        DateTimeOffset latestMarketStartUtc,
        int wins,
        int losses)
    {
        for (var index = 0; index < wins; index++)
        {
            AddSettledRun(
                repository,
                variant,
                "break-even-win-" + index.ToString(CultureInfo.InvariantCulture),
                latestMarketStartUtc.AddMinutes(-index),
                stakeUsd: 1m,
                realizedPnlUsd: 1m);
        }

        for (var index = 0; index < losses; index++)
        {
            AddSettledRun(
                repository,
                variant,
                "break-even-loss-" + index.ToString(CultureInfo.InvariantCulture),
                latestMarketStartUtc.AddMinutes(-(wins + index)),
                stakeUsd: 1m,
                realizedPnlUsd: -1m);
        }
    }

    private static void AddEnteredRun(
        TestAppRepository repository,
        BtcUpDown5mStrategyVariant variant,
        string marketId,
        DateTimeOffset marketStartUtc,
        string selectedAssetId,
        string selectedOutcome,
        decimal stakeUsd)
    {
        var entryPrice = 0.50m;
        var sizeShares = stakeUsd / entryPrice;
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            marketId,
            "condition-" + marketId,
            "btc-updown-5m-" + marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "Bitcoin Up or Down - " + marketId,
            "Crypto",
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            marketStartUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            StrategyMarketPaperRunStatuses.Entered,
            selectedAssetId,
            selectedOutcome,
            entryPrice,
            stakeUsd,
            sizeShares,
            Guid.NewGuid(),
            Guid.NewGuid(),
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds),
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            marketStartUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds)));
    }

    private static PolymarketOnChainTokenMetadata TokenMetadata(
        string tokenId,
        string outcome,
        string winningOutcome)
    {
        return new PolymarketOnChainTokenMetadata(
            tokenId,
            "condition-1",
            "market-1",
            "btc-updown-5m-1778067900",
            "Bitcoin Up or Down - test",
            outcome,
            outcome == "Up" ? 0 : 1,
            "Crypto",
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Active: false,
            Closed: true,
            Archived: false,
            Resolved: true,
            winningOutcome,
            ["asset-up", "asset-down"],
            ["Up", "Down"],
            LookupSucceeded: true,
            LookupError: null,
            RawJson: "{}",
            LastRefreshedUtc: DateTimeOffset.UtcNow);
    }

    private sealed class FakeBtcUsdReferencePriceClient(decimal priceUsd) : IBtcUsdReferencePriceClient
    {
        public int RequestCount { get; private set; }

        public Task<BtcUsdReferencePricePoint> GetBtcUsdPriceAsync(CancellationToken cancellationToken = default)
        {
            RequestCount++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new BtcUsdReferencePricePoint(priceUsd, now, now, "Test"));
        }
    }

    private sealed class FakeGammaClient(IReadOnlyList<PolymarketOnChainTokenMetadata> metadata) : IPolymarketGammaClient
    {
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<string?> GetEventCategoryAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class SlowTokenMetadataGammaClient(
        IReadOnlyCollection<string> slowTokenIds,
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata) : IPolymarketGammaClient
    {
        private readonly HashSet<string> slowTokenIdSet = slowTokenIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public async Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(
            string tokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            if (slowTokenIdSet.Contains(tokenId))
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return [];
            }

            return metadata.Any(item => string.Equals(item.TokenId, tokenId, StringComparison.OrdinalIgnoreCase))
                ? metadata
                : [];
        }

        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(
            string conditionId,
            string requestedTokenId,
            bool closed,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>(
                metadata.Any(item => string.Equals(item.ConditionId, conditionId, StringComparison.OrdinalIgnoreCase))
                    ? metadata
                    : []);
        }

        public Task<string?> GetEventCategoryAsync(string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeClobClient : IPolymarketClobPublicClient
    {
        private readonly IReadOnlyDictionary<string, OrderBookSnapshot> orderBooksByAssetId;
        private readonly object sync = new();
        private readonly Dictionary<string, int> orderBookCallsByAssetId = new(StringComparer.OrdinalIgnoreCase);

        public int GetOrderBookCalls
        {
            get
            {
                lock (sync)
                {
                    return orderBookCallsByAssetId.Values.Sum();
                }
            }
        }

        public FakeClobClient(OrderBookSnapshot? orderBook)
            : this(ToClobOrderBooks(orderBook))
        {
        }

        public FakeClobClient(IReadOnlyList<OrderBookSnapshot> orderBooks)
        {
            orderBooksByAssetId = orderBooks
                .Where(orderBook => !string.IsNullOrWhiteSpace(orderBook.AssetId))
                .ToDictionary(orderBook => orderBook.AssetId, StringComparer.OrdinalIgnoreCase);
        }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                orderBookCallsByAssetId.TryGetValue(assetId, out var calls);
                orderBookCallsByAssetId[assetId] = calls + 1;
            }

            return Task.FromResult(
                orderBooksByAssetId.TryGetValue(assetId, out var orderBook)
                    ? orderBook
                    : null);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }

    private sealed class PassGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(false, "127.0.0.1", "US", null));
        }
    }

    private sealed class ReadyAuthService : IPolymarketAuthService
    {
        public Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct)
        {
            return Task.FromResult(AuthReadinessStatus.Ready());
        }
    }

    private sealed class CapturingTradingClient : IPolymarketTradingClient
    {
        public int PlaceCalls { get; private set; }

        public ClobV2OrderRequest? LastRequest { get; private set; }

        public LiveOrderPlacementResult PlacementResult { get; init; } =
            new(true, "0xorder", "live", null, null, null, "{}", "{}");

        public Task<ClobV2DryRunOrderResult> PrepareDryRunOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public Task<LiveOrderPlacementResult> PlaceLiveOrderAsync(ClobV2OrderRequest request, CancellationToken ct)
        {
            PlaceCalls++;
            LastRequest = request;
            return Task.FromResult(PlacementResult);
        }

        public Task<LiveOrderCancellationResult> CancelOrderAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult(new LiveOrderCancellationResult(true, [orderId], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            return Task.FromResult(new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult<LiveOrderStatusResult?>(null);
        }
    }
}
