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
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps5Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_5");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps5InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_5_instant");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps20Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_20");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps20InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_20_instant");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps45InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_45_instant");

    private static readonly BtcUpDown5mStrategyVariant Middle1Bps100Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_100");

    private static BtcUpDown5mStrategyVariant Middle1RevertVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert");

    private static BtcUpDown5mStrategyVariant Middle1RevertBps100Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert_bps_100");

    private static BtcUpDown5mStrategyVariant Middle1RevertBps100InstantVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert_bps_100_instant");

    private static readonly BtcUpDown5mStrategyVariant Skip3Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_3");

    private static readonly BtcUpDown5mStrategyVariant Skip1Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_1");

    private static readonly BtcUpDown5mStrategyVariant UpBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant DownBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant Up15mBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_15m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant Down15mBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_15m_down_bps_2_instant");

    private static BtcUpDown5mStrategyVariant Skip3RevertVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_skip_3_revert");

    private static readonly BtcUpDown5mStrategyVariant AlwaysUpVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up");

    private static readonly BtcUpDown5mStrategyVariant AlwaysDownVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_down");

    private static readonly BtcUpDown5mStrategyVariant UpSimpleVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mUpSimpleCode);

    private static readonly BtcUpDown5mStrategyVariant DownSimpleVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mDownSimpleCode);

    private static readonly BtcUpDown5mStrategyVariant UpMakerVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mUpMakerCode);

    private static readonly BtcUpDown5mStrategyVariant DownMakerVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mDownMakerCode);

    private static readonly BtcUpDown5mStrategyVariant UpMaker50Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mUpMaker50Code);

    private static readonly BtcUpDown5mStrategyVariant DownMaker50Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mDownMaker50Code);

    private static readonly BtcUpDown5mStrategyVariant BinanceVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance");

    private static readonly BtcUpDown5mStrategyVariant Binance45Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_45");

    private static readonly BtcUpDown5mStrategyVariant Binance47Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_47");

    private static readonly BtcUpDown5mStrategyVariant Binance49Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_49");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps01Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_1");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps05Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_5");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps09Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_9");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps1Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_10");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps11Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_11");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps18Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_18");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps19Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_19");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps2Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_20");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps21Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_21");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps22Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_22");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps23Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_23");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps3Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_30");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps49Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_49");

    private static readonly BtcUpDown5mStrategyVariant BinanceBps5Variant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_binance_bps_50");

    private static readonly BtcUpDown5mStrategyVariant EthBinanceBps2Variant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_binance_bps_2");

    private static readonly BtcUpDown5mStrategyVariant EthSkip3Variant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_skip_3");

    private static readonly BtcUpDown5mStrategyVariant EthUpBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthDownBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthUp15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_15m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthDown15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_15m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthUpSimpleVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_up_simple");

    private static readonly BtcUpDown5mStrategyVariant EthDownSimpleVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_down_simple");

    private static readonly BtcUpDown5mStrategyVariant EthMiddle1Variant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_middle_100");

    private static readonly BtcUpDown5mStrategyVariant EthMiddleBps20Variant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_middle_100_bps_20");

    private static readonly BtcUpDown5mStrategyVariant SolUpBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant SolDownBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant SolUp15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_15m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant SolDown15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_15m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant SolUpSimpleVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_up_simple");

    private static readonly BtcUpDown5mStrategyVariant SolDownSimpleVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_down_simple");

    private static BtcUpDown5mStrategyVariant SolMiddleRevertBps100InstantVariant =>
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_middle_100_revert_bps_100_instant");

    private static readonly BtcUpDown5mStrategyVariant SolBinanceBps1InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_binance_bps_1_instant");

    private static readonly BtcUpDown5mStrategyVariant SolBinanceBps24InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_binance_bps_24_instant");

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

    private static readonly BtcUpDown5mStrategyVariant PreviousScoreCounterTrendFakVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_prev_score_countertrend_fak");

    private static readonly BtcUpDown5mStrategyVariant PreviousScoreCounterTrendFakPremarketVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_prev_score_countertrend_fak_premarket");

    private static readonly BtcUpDown5mStrategyVariant PreviousScoreCounterTrendFakRevertVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_prev_score_countertrend_fak_revert");

    private static readonly BtcUpDown5mStrategyVariant PreviousScoreCounterTrendFakPremarketRevertVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_prev_score_countertrend_fak_premarket_revert");

    private static readonly BtcUpDown5mStrategyVariant EthPreviousScoreCounterTrendFakVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_prev_score_countertrend_fak");

    private static readonly BtcUpDown5mStrategyVariant EthPreviousScoreCounterTrendFakRevertVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_prev_score_countertrend_fak_revert");

    private static readonly BtcUpDown5mStrategyVariant EthPreviousScoreCounterTrendFakPremarketVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_prev_score_countertrend_fak_premarket");

    private static readonly BtcUpDown5mStrategyVariant EthPreviousScoreCounterTrendFakPremarketRevertVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_prev_score_countertrend_fak_premarket_revert");

    private static readonly BtcUpDown5mStrategyVariant SolPreviousScoreCounterTrendFakVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_prev_score_countertrend_fak");

    private static readonly BtcUpDown5mStrategyVariant SolPreviousScoreCounterTrendFakRevertVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_prev_score_countertrend_fak_revert");

    private static readonly BtcUpDown5mStrategyVariant SolPreviousScoreCounterTrendFakPremarketVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_prev_score_countertrend_fak_premarket");

    private static readonly BtcUpDown5mStrategyVariant SolPreviousScoreCounterTrendFakPremarketRevertVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_prev_score_countertrend_fak_premarket_revert");

    [Fact]
    public void StrategyIds_IncludeStandardMartinAndGammaBtcVariants()
    {
        Assert.Equal(1516, StrategyIds.BtcUpDown5mVariants.Count);
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Code).Distinct().Count());
        Assert.Equal(18, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Standard));
        Assert.Equal(15, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StandardEntryPriceCap));
        Assert.Equal(18, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Less180Martin);
        Assert.Equal(210, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert));
        Assert.Equal(200, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant));
        Assert.Equal(5, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_skip_bps_", StringComparison.Ordinal));
        Assert.Equal(200, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant));
        Assert.Equal(56, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysUp);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysDown);
        Assert.Equal(2, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant));
        Assert.Equal(4, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomeMaker));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelative);
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice));
        Assert.Equal(50, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_binance_bps_", StringComparison.Ordinal) &&
            variant.Code.EndsWith("_instant", StringComparison.Ordinal));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever);
        Assert.Equal(2, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin));
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge));
        Assert.Equal(3, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.EnsembleVote);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DynamicMarkov);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StrategySelector);
        Assert.Equal(76, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend));
        Assert.Equal(40, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket));
        Assert.Equal(24, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend));
        Assert.Equal(144, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend));
        Assert.Equal(100, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress));
        Assert.Equal(2, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress));
        Assert.Equal(9, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend));
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert);
        Assert.Single(StrategyIds.BtcUpDown5mVariants, variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert);
        Assert.Equal(320, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.Contains("_preopen_full_", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell &&
            variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.HalfPeriod));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell &&
            variant.PreOpenLifetimeMode == BtcUpDownPreOpenLifetimeMode.FullPeriod));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.OneHour &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(80, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FourHours &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirection));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.OneHour &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant =>
            variant.MarketInterval == BtcUpDownMarketInterval.FourHours &&
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreOpenFixedDirectionSell));
        AssertDiffCounterTrendFakPremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        AssertDiffProgressGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        AssertDiffShiftProgressGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        Assert.Equal("BTC Up or Down 5m Less 180 Gamma", StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.Less,
            180,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection).Name);
        Assert.Equal("BTC Up or Down 5m Middle 100", Middle1Variant.Name);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_middle_100_revert");
        Assert.Equal(100, Middle1Variant.DecisionDepth);
        Assert.Equal(
            [10, 20, 30, 40, 50, 60, 70, 80, 90, 100],
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference &&
                    variant.DecisionThresholdBps is null)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(depth => depth)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_middle_1" ||
            variant.Code.StartsWith("btc_up_down_5m_middle_1_", StringComparison.Ordinal) ||
            variant.Code == "btc_up_down_5m_middle_1_revert");
        Assert.Equal("BTC Up/Down 5m Maker", UpMakerVariant.Category);
        Assert.Equal(BtcUpDownFixedOutcome.Up, UpMakerVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, DownMakerVariant.FixedOutcome);
        Assert.Equal("BTC Up or Down 5m Up Simple", UpSimpleVariant.Name);
        Assert.Equal("BTC Up or Down 5m Down Simple", DownSimpleVariant.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Up, UpSimpleVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, DownSimpleVariant.FixedOutcome);
        Assert.Equal(0.50m, UpSimpleVariant.FixedLimitPrice);
        Assert.Equal(0.50m, DownSimpleVariant.FixedLimitPrice);
        Assert.Equal("Simple", UpSimpleVariant.Category);
        Assert.Equal("Simple", DownSimpleVariant.Category);
        Assert.Equal("BTC Up or Down 5m Up Maker 50", UpMaker50Variant.Name);
        Assert.Equal("BTC Up or Down 5m Down Maker 50", DownMaker50Variant.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Up, UpMaker50Variant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, DownMaker50Variant.FixedOutcome);
        Assert.Equal(0.50m, UpMaker50Variant.FixedLimitPrice);
        Assert.Equal(0.50m, DownMaker50Variant.FixedLimitPrice);
        Assert.Equal(0.50m, UpMaker50Variant.MakerMinBestAskExclusive);
        Assert.Equal(0.50m, DownMaker50Variant.MakerMinBestAskExclusive);
        Assert.Equal("BTC Up/Down 5m Maker", UpMaker50Variant.Category);
        Assert.Equal("BTC Up or Down 5m Middle 100 10 bps", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_100_bps_10").Name);
        Assert.Equal(10m, StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_middle_100_bps_10").DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Middle 100 100 bps", Middle1Bps100Variant.Name);
        Assert.Equal(100m, Middle1Bps100Variant.DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Middle 100 5 bps Instant", Middle1Bps5InstantVariant.Name);
        Assert.Equal(5m, Middle1Bps5InstantVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDown5mStrategyBehavior.MiddleReferenceInstant, Middle1Bps5InstantVariant.Behavior);
        Assert.Equal(
            Enumerable.Range(1, 20).Select(threshold => (decimal)(threshold * 5)).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference &&
                    variant.DecisionDepth == 100 &&
                    variant.DecisionThresholdBps is > 0m)
                .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.Equal(
            Enumerable.Range(1, 20).Select(threshold => (decimal)(threshold * 5)).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant &&
                    variant.DecisionDepth == 100 &&
                    variant.DecisionThresholdBps is > 0m)
                .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant &&
            variant.DecisionDepth == 100 &&
            variant.DecisionThresholdBps is > 0m);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert &&
            variant.DecisionDepth == 100 &&
            variant.DecisionThresholdBps is > 0m);
        Assert.Equal("BTC Up or Down 5m Skip 5", StrategyIds.BtcUpDown5mVariants.Single(
            variant => variant.Code == "btc_up_down_5m_skip_5").Name);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_skip_5_revert");
        Assert.Equal("BTC Up or Down 5m Up 2 bps Instant", UpBps2InstantVariant.Name);
        Assert.Equal(2m, UpBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDownFixedOutcome.Up, UpBps2InstantVariant.FixedOutcome);
        Assert.Equal("BTC Up or Down 5m Down 2 bps Instant", DownBps2InstantVariant.Name);
        Assert.Equal(2m, DownBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDownFixedOutcome.Down, DownBps2InstantVariant.FixedOutcome);
        Assert.Equal(BtcUpDownMarketInterval.FiveMinutes, UpBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownMarketInterval.FiveMinutes, DownBps2InstantVariant.MarketInterval);
        Assert.Equal("BTC Up or Down 15m Up 2 bps Instant", Up15mBps2InstantVariant.Name);
        Assert.Equal(2m, Up15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, Up15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Up, Up15mBps2InstantVariant.FixedOutcome);
        Assert.Equal("BTC Up or Down 15m Down 2 bps Instant", Down15mBps2InstantVariant.Name);
        Assert.Equal(2m, Down15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, Down15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Down, Down15mBps2InstantVariant.FixedOutcome);
        foreach (var interval in new[] { BtcUpDownMarketInterval.FiveMinutes, BtcUpDownMarketInterval.FifteenMinutes })
        {
            Assert.Equal(
                Enumerable.Range(1, 50).Select(threshold => (decimal)threshold).ToArray(),
                StrategyIds.BtcUpDown5mVariants
                    .Where(variant =>
                        variant.MarketInterval == interval &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                Enumerable.Range(1, 50).Select(threshold => (decimal)threshold).ToArray(),
                StrategyIds.BtcUpDown5mVariants
                    .Where(variant =>
                        variant.MarketInterval == interval &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
        }
        Assert.Equal("BTC Up or Down 5m Up", AlwaysUpVariant.Name);
        Assert.Equal("BTC Up or Down 5m Down", AlwaysDownVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance", BinanceVariant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 45", Binance45Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 47", Binance47Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 49", Binance49Variant.Name);
        Assert.Equal("BTC Up or Down 5m Binance 1 bps", BinanceBps01Variant.Name);
        Assert.Equal(1m, BinanceBps01Variant.DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Binance 5 bps", BinanceBps05Variant.Name);
        Assert.Equal(5m, BinanceBps05Variant.DecisionThresholdBps);
        Assert.Equal("BTC Up or Down 5m Binance 9 bps", BinanceBps09Variant.Name);
        Assert.Equal(9m, BinanceBps09Variant.DecisionThresholdBps);
        Assert.Equal(
            Enumerable.Range(1, 50).Select(threshold => (decimal)threshold).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold)
                .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant);
        Assert.Equal("BTC Up or Down 5m Binance 10 bps", BinanceBps1Variant.Name);
        Assert.Equal(10, BinanceBps1Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 11 bps", BinanceBps11Variant.Name);
        Assert.Equal(11m, BinanceBps11Variant.DecisionThresholdBps);
        Assert.Equal(11, BinanceBps11Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 20 bps", BinanceBps2Variant.Name);
        Assert.Equal(20, BinanceBps2Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 30 bps", BinanceBps3Variant.Name);
        Assert.Equal(30m, BinanceBps3Variant.DecisionThresholdBps);
        Assert.Equal(30, BinanceBps3Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 49 bps", BinanceBps49Variant.Name);
        Assert.Equal(49m, BinanceBps49Variant.DecisionThresholdBps);
        Assert.Equal(49, BinanceBps49Variant.DecisionDepth);
        Assert.Equal("BTC Up or Down 5m Binance 50 bps", BinanceBps5Variant.Name);
        Assert.Equal(50, BinanceBps5Variant.DecisionDepth);
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
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_15m_preopen_full_up_49_sell");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_4h_preopen_full_down_30");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_4h_preopen_full_down_10");
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
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_more_60_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 60 Gamma Below 70" ||
            variant.Code == "btc_up_down_5m_more_90_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 90 Gamma Below 70" ||
            variant.Code == "btc_up_down_5m_more_120_gamma_below_65" ||
            variant.Name == "BTC Up or Down 5m More 120 Gamma Below 65" ||
            variant.Code == "btc_up_down_5m_more_120_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 120 Gamma Below 70" ||
            variant.Code == "btc_up_down_5m_more_150_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 150 Gamma Below 70" ||
            variant.Code == "btc_up_down_5m_more_150_gamma_below_80" ||
            variant.Name == "BTC Up or Down 5m More 150 Gamma Below 80");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000090") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_90" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 90" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000085") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_85" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 85" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000080") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_80" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 80" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000075") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_75" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 75" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000070") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_70" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 70" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000065") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_65" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 65" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000060") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_60" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 60" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000055") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_55" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 55");
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
        Assert.Equal("BTC Up or Down 5m Prev Score Countertrend", PreviousScoreCounterTrendFakVariant.Name);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak, PreviousScoreCounterTrendFakVariant.Behavior);
        Assert.Null(PreviousScoreCounterTrendFakVariant.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 5m Previous Score Countertrend", PreviousScoreCounterTrendFakVariant.Category);
        Assert.Equal("BTC Up or Down 5m Prev Score Countertrend Premarket", PreviousScoreCounterTrendFakPremarketVariant.Name);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket, PreviousScoreCounterTrendFakPremarketVariant.Behavior);
        Assert.Equal(-30, PreviousScoreCounterTrendFakPremarketVariant.EntryDelaySeconds);
        Assert.Null(PreviousScoreCounterTrendFakPremarketVariant.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 5m Previous Score Countertrend Premarket", PreviousScoreCounterTrendFakPremarketVariant.Category);
        Assert.Equal("BTC Up or Down 5m Prev Score Countertrend Revert", PreviousScoreCounterTrendFakRevertVariant.Name);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert, PreviousScoreCounterTrendFakRevertVariant.Behavior);
        Assert.Null(PreviousScoreCounterTrendFakRevertVariant.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 5m Previous Score Countertrend", PreviousScoreCounterTrendFakRevertVariant.Category);
        Assert.Equal("BTC Up or Down 5m Prev Score Countertrend Premarket Revert", PreviousScoreCounterTrendFakPremarketRevertVariant.Name);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert, PreviousScoreCounterTrendFakPremarketRevertVariant.Behavior);
        Assert.Equal(-30, PreviousScoreCounterTrendFakPremarketRevertVariant.EntryDelaySeconds);
        Assert.Null(PreviousScoreCounterTrendFakPremarketRevertVariant.FixedLimitPrice);
        Assert.Equal("BTC Up/Down 5m Previous Score Countertrend Premarket", PreviousScoreCounterTrendFakPremarketRevertVariant.Category);
        Assert.Equal(
            Enumerable.Range(0, 9).Select(index => 0.10m + (index * 0.05m)).ToArray(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend)
                .Select(variant => variant.FixedLimitPrice.GetValueOrDefault())
                .OrderBy(price => price)
                .ToArray());
        Assert.Equal(
            ExpectedDiffCounterThresholds(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                    variant.Code.Contains("_up_diff_", StringComparison.Ordinal) &&
                    !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
            variant.Code.Contains("_up_diff_", StringComparison.Ordinal) &&
            variant.Code.Contains("_revert_", StringComparison.Ordinal));
        Assert.Equal(
            ExpectedDiffCounterThresholds(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                    variant.Code.Contains("_down_diff_", StringComparison.Ordinal) &&
                    !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
            variant.Code.Contains("_down_diff_", StringComparison.Ordinal) &&
            variant.Code.Contains("_revert_", StringComparison.Ordinal));
        Assert.Equal(
            ExpectedAdjustedDiffCounterThresholds(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                    variant.Code.Contains("_up_adjusted_diff_", StringComparison.Ordinal) &&
                    !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
            variant.Code.Contains("_up_adjusted_diff_", StringComparison.Ordinal) &&
            variant.Code.Contains("_revert_", StringComparison.Ordinal));
        Assert.Equal(
            ExpectedAdjustedDiffCounterThresholds(),
            StrategyIds.BtcUpDown5mVariants
                .Where(variant =>
                    variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                    variant.Code.Contains("_down_adjusted_diff_", StringComparison.Ordinal) &&
                    !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
            variant.Code.Contains("_down_adjusted_diff_", StringComparison.Ordinal) &&
            variant.Code.Contains("_revert_", StringComparison.Ordinal));
        foreach (var shift in ExpectedShiftDiffCounterShifts())
        {
            Assert.Equal(
                ExpectedShiftDiffCounterThresholds(),
                StrategyIds.BtcUpDown5mVariants
                    .Where(variant =>
                        variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                        variant.Code.Contains("_up_shift_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                        variant.ShiftDiffCount == shift)
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                variant.Code.Contains("_up_shift_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                variant.ShiftDiffCount == shift);
            Assert.Equal(
                ExpectedShiftDiffCounterThresholds(),
                StrategyIds.BtcUpDown5mVariants
                    .Where(variant =>
                        variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                        variant.Code.Contains("_down_shift_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                        variant.ShiftDiffCount == shift)
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                variant.Code.Contains("_down_shift_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                variant.ShiftDiffCount == shift);
        }
        var btcDiff2 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_up_diff_2_instant");
        Assert.Equal("BTC Up or Down 5m Up 2 Diff Instant", btcDiff2.Name);
        Assert.Equal("BTC Up/Down 5m Diff Up", btcDiff2.Category);
        Assert.Equal(BtcUpDownFixedOutcome.Down, btcDiff2.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, btcDiff2.DiffCounterTriggerOutcome);
        var btcAdjustedDiff20 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_up_adjusted_diff_20_instant");
        Assert.Equal("BTC Up or Down 5m Up 20 AdjustedDiff Instant", btcAdjustedDiff20.Name);
        Assert.Equal("BTC Up/Down 5m AdjustedDiff Up", btcAdjustedDiff20.Category);
        Assert.Equal(BtcUpDownFixedOutcome.Down, btcAdjustedDiff20.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, btcAdjustedDiff20.DiffCounterTriggerOutcome);
        var btcShiftDiff2x4 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_up_shift_diff_2_4_instant");
        Assert.Equal("BTC Up or Down 5m Up 2 4 ShiftDiff Instant", btcShiftDiff2x4.Name);
        Assert.Equal("BTC Up/Down 5m ShiftDiff 2", btcShiftDiff2x4.Category);
        Assert.Equal(BtcUpDownFixedOutcome.Down, btcShiftDiff2x4.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, btcShiftDiff2x4.DiffCounterTriggerOutcome);
        Assert.Equal(2, btcShiftDiff2x4.ShiftDiffCount);
        Assert.Equal(4, btcShiftDiff2x4.DecisionDepth);
        var btcProgress17 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_diff_17_up_progress");
        Assert.Equal("BTC Up or Down 5m 17 Diff Up Progress", btcProgress17.Name);
        Assert.Equal("BTC Up/Down 5m Diff Progress", btcProgress17.Category);
        Assert.Equal(BtcUpDownFixedOutcome.Down, btcProgress17.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, btcProgress17.DiffCounterTriggerOutcome);
        Assert.Equal(17, btcProgress17.DecisionDepth);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_up_diff_2_revert_instant" ||
            variant.Code == "btc_up_down_5m_up_adjusted_diff_20_revert_instant" ||
            variant.Code == "btc_up_down_5m_up_shift_diff_2_4_revert_instant");
    }

    [Fact]
    public void StrategyIds_ExcludeDeletedGammaVariants()
    {
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Id == Guid.Parse("b7c50005-0000-4000-8022-000000060080") ||
            variant.Code == "btc_up_down_5m_more_60_gamma_below_80" ||
            variant.Name == "BTC Up or Down 5m More 60 Gamma Below 80" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8022-000000150070") ||
            variant.Code == "btc_up_down_5m_more_150_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 150 Gamma Below 70" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8022-000000090070") ||
            variant.Code == "btc_up_down_5m_more_90_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 90 Gamma Below 70" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8022-000000120070") ||
            variant.Code == "btc_up_down_5m_more_120_gamma_below_70" ||
            variant.Name == "BTC Up or Down 5m More 120 Gamma Below 70" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8022-000000150080") ||
            variant.Code == "btc_up_down_5m_more_150_gamma_below_80" ||
            variant.Name == "BTC Up or Down 5m More 150 Gamma Below 80");
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_more_60_gamma_below_80"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_more_150_gamma_below_70"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_more_90_gamma_below_70"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_more_120_gamma_below_70"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_more_150_gamma_below_80"));
    }

    [Fact]
    public void StrategyIds_ExcludeDeletedPreviousScoreCounterTrend55AndAboveVariants()
    {
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000090") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_90" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 90" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000085") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_85" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 85" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000080") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_80" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 80" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000075") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_75" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 75" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000070") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_70" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 70" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000065") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_65" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 65" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000060") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_60" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 60" ||
            variant.Id == Guid.Parse("b7c50005-0000-4000-8025-000000000055") ||
            variant.Code == "btc_up_down_5m_prev_score_countertrend_55" ||
            variant.Name == "BTC Up or Down 5m Prev Score Countertrend 55");
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_90"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_85"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_80"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_75"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_70"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_65"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_60"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_prev_score_countertrend_55"));
    }

    [Fact]
    public void StrategyIds_IncludeEthAndSolBinanceBpsVariants()
    {
        Assert.Equal(2389, StrategyIds.CryptoUpDown5mVariants.Count);
        Assert.Equal(3905, StrategyIds.UpDown5mStrategyVariants.Count);
        Assert.Equal(
            StrategyIds.UpDown5mStrategyVariants.Count,
            StrategyIds.UpDown5mStrategyVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(
            StrategyIds.UpDown5mStrategyVariants.Count,
            StrategyIds.UpDown5mStrategyVariants.Select(variant => variant.Code).Distinct().Count());
        Assert.Equal(1226, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1163, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            string.Equals(variant.ReferenceAssetSymbol, "SOL", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(100, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold));
        Assert.Equal(100, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant));
        Assert.Equal(420, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference));
        Assert.Equal(400, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant));
        Assert.Equal(10, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.Contains("_up_down_5m_skip_bps_", StringComparison.Ordinal));
        Assert.Equal(400, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant));
        Assert.Single(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak);
        Assert.Equal(62, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket));
        Assert.Equal(112, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket));
        Assert.Equal(2, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak));
        Assert.Equal(2, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert));
        Assert.Equal(2, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket));
        Assert.Equal(2, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert));
        Assert.Equal(4, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant));
        Assert.Equal(152, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend));
        Assert.Equal(80, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket));
        Assert.Equal(48, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend));
        Assert.Equal(288, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend));
        Assert.Equal(200, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress));
        Assert.Equal(4, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress));
        Assert.Equal("ETH Up or Down 5m Up Simple", EthUpSimpleVariant.Name);
        Assert.Equal("ETH Up or Down 5m Down Simple", EthDownSimpleVariant.Name);
        Assert.Equal("SOL Up or Down 5m Up Simple", SolUpSimpleVariant.Name);
        Assert.Equal("SOL Up or Down 5m Down Simple", SolDownSimpleVariant.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Up, EthUpSimpleVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, EthDownSimpleVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, SolUpSimpleVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, SolDownSimpleVariant.FixedOutcome);
        AssertDiffCounterTrendFakPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffCounterTrendFakPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffProgressGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffProgressGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffShiftProgressGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffShiftProgressGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        Assert.Equal("ETH Up or Down 5m Prev Score Countertrend", EthPreviousScoreCounterTrendFakVariant.Name);
        Assert.Equal("ETH Up/Down 5m Previous Score Countertrend", EthPreviousScoreCounterTrendFakVariant.Category);
        Assert.Equal("ETH", EthPreviousScoreCounterTrendFakVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak, EthPreviousScoreCounterTrendFakVariant.Behavior);
        Assert.Null(EthPreviousScoreCounterTrendFakVariant.FixedLimitPrice);
        Assert.Equal("ETH Up or Down 5m Prev Score Countertrend Revert", EthPreviousScoreCounterTrendFakRevertVariant.Name);
        Assert.Equal("ETH Up/Down 5m Previous Score Countertrend", EthPreviousScoreCounterTrendFakRevertVariant.Category);
        Assert.Equal("ETH", EthPreviousScoreCounterTrendFakRevertVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert, EthPreviousScoreCounterTrendFakRevertVariant.Behavior);
        Assert.Null(EthPreviousScoreCounterTrendFakRevertVariant.FixedLimitPrice);
        Assert.Equal("ETH Up or Down 5m Prev Score Countertrend Premarket", EthPreviousScoreCounterTrendFakPremarketVariant.Name);
        Assert.Equal("ETH Up/Down 5m Previous Score Countertrend Premarket", EthPreviousScoreCounterTrendFakPremarketVariant.Category);
        Assert.Equal("ETH", EthPreviousScoreCounterTrendFakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket, EthPreviousScoreCounterTrendFakPremarketVariant.Behavior);
        Assert.Equal(-30, EthPreviousScoreCounterTrendFakPremarketVariant.EntryDelaySeconds);
        Assert.Null(EthPreviousScoreCounterTrendFakPremarketVariant.FixedLimitPrice);
        Assert.Equal("ETH Up or Down 5m Prev Score Countertrend Premarket Revert", EthPreviousScoreCounterTrendFakPremarketRevertVariant.Name);
        Assert.Equal("ETH Up/Down 5m Previous Score Countertrend Premarket", EthPreviousScoreCounterTrendFakPremarketRevertVariant.Category);
        Assert.Equal("ETH", EthPreviousScoreCounterTrendFakPremarketRevertVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert, EthPreviousScoreCounterTrendFakPremarketRevertVariant.Behavior);
        Assert.Equal(-30, EthPreviousScoreCounterTrendFakPremarketRevertVariant.EntryDelaySeconds);
        Assert.Null(EthPreviousScoreCounterTrendFakPremarketRevertVariant.FixedLimitPrice);
        Assert.Equal("SOL Up or Down 5m Prev Score Countertrend", SolPreviousScoreCounterTrendFakVariant.Name);
        Assert.Equal("SOL Up/Down 5m Previous Score Countertrend", SolPreviousScoreCounterTrendFakVariant.Category);
        Assert.Equal("SOL", SolPreviousScoreCounterTrendFakVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak, SolPreviousScoreCounterTrendFakVariant.Behavior);
        Assert.Null(SolPreviousScoreCounterTrendFakVariant.FixedLimitPrice);
        Assert.Equal("SOL Up or Down 5m Prev Score Countertrend Revert", SolPreviousScoreCounterTrendFakRevertVariant.Name);
        Assert.Equal("SOL Up/Down 5m Previous Score Countertrend", SolPreviousScoreCounterTrendFakRevertVariant.Category);
        Assert.Equal("SOL", SolPreviousScoreCounterTrendFakRevertVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert, SolPreviousScoreCounterTrendFakRevertVariant.Behavior);
        Assert.Null(SolPreviousScoreCounterTrendFakRevertVariant.FixedLimitPrice);
        Assert.Equal("SOL Up or Down 5m Prev Score Countertrend Premarket", SolPreviousScoreCounterTrendFakPremarketVariant.Name);
        Assert.Equal("SOL Up/Down 5m Previous Score Countertrend Premarket", SolPreviousScoreCounterTrendFakPremarketVariant.Category);
        Assert.Equal("SOL", SolPreviousScoreCounterTrendFakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket, SolPreviousScoreCounterTrendFakPremarketVariant.Behavior);
        Assert.Equal(-30, SolPreviousScoreCounterTrendFakPremarketVariant.EntryDelaySeconds);
        Assert.Null(SolPreviousScoreCounterTrendFakPremarketVariant.FixedLimitPrice);
        Assert.Equal("SOL Up or Down 5m Prev Score Countertrend Premarket Revert", SolPreviousScoreCounterTrendFakPremarketRevertVariant.Name);
        Assert.Equal("SOL Up/Down 5m Previous Score Countertrend Premarket", SolPreviousScoreCounterTrendFakPremarketRevertVariant.Category);
        Assert.Equal("SOL", SolPreviousScoreCounterTrendFakPremarketRevertVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert, SolPreviousScoreCounterTrendFakPremarketRevertVariant.Behavior);
        Assert.Equal(-30, SolPreviousScoreCounterTrendFakPremarketRevertVariant.EntryDelaySeconds);
        Assert.Null(SolPreviousScoreCounterTrendFakPremarketRevertVariant.FixedLimitPrice);
        Assert.All(
            new[] { EthUpSimpleVariant, EthDownSimpleVariant, SolUpSimpleVariant, SolDownSimpleVariant },
            variant =>
            {
                Assert.Equal(0.50m, variant.FixedLimitPrice);
                Assert.Equal("Simple", variant.Category);
            });

        var expectedThresholds = Enumerable.Range(1, 50)
            .Select(threshold => (decimal)threshold)
            .ToArray();
        var expectedMiddleThresholds = Enumerable.Range(1, 20)
            .Select(threshold => (decimal)(threshold * 5))
            .ToArray();
        var expectedDiffThresholds = ExpectedDiffCounterThresholds();
        var expectedAdjustedDiffThresholds = ExpectedAdjustedDiffCounterThresholds();
        foreach (var assetSymbol in new[] { "ETH", "SOL" })
        {
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                ExpectedReferenceAverageBpsThresholds(),
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket &&
                        variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                ExpectedReferenceAverageBpsThresholds(),
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket &&
                        variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Down)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            if (string.Equals(assetSymbol, "ETH", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(
                    expectedThresholds,
                    StrategyIds.CryptoUpDown5mVariants
                        .Where(variant =>
                            string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket &&
                            variant.EntryDelaySeconds == -30)
                        .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                        .OrderBy(threshold => threshold)
                        .ToArray());
            }
            else
            {
                Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket &&
                    variant.EntryDelaySeconds == -30);
            }
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                Enumerable.Range(1, 5).ToArray(),
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults)
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(depth => depth)
                    .ToArray());
            Assert.Equal(
                [10, 20, 30, 40, 50, 60, 70, 80, 90, 100],
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference &&
                        variant.DecisionThresholdBps is null)
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(depth => depth)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert &&
                variant.DecisionThresholdBps is null);
            Assert.Equal(
                expectedMiddleThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference &&
                        variant.DecisionDepth == 100 &&
                        variant.DecisionThresholdBps is > 0m)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                expectedMiddleThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant &&
                        variant.DecisionDepth == 100)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert &&
                variant.DecisionDepth == 100 &&
                variant.DecisionThresholdBps is > 0m);
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant &&
                variant.DecisionDepth == 100);
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.MarketInterval == BtcUpDownMarketInterval.FiveMinutes &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                expectedThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.MarketInterval == BtcUpDownMarketInterval.FifteenMinutes &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant &&
                        variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.Equal(
                expectedDiffThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                        variant.Code.Contains("_up_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                variant.Code.Contains("_up_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal));
            Assert.Equal(
                expectedDiffThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                        variant.Code.Contains("_down_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend &&
                variant.Code.Contains("_down_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal));
            Assert.Equal(
                expectedAdjustedDiffThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                        variant.Code.Contains("_up_adjusted_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                variant.Code.Contains("_up_adjusted_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal));
            Assert.Equal(
                expectedAdjustedDiffThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                        variant.Code.Contains("_down_adjusted_diff_", StringComparison.Ordinal) &&
                        !variant.Code.Contains("_revert_", StringComparison.Ordinal))
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                variant.Code.Contains("_down_adjusted_diff_", StringComparison.Ordinal) &&
                variant.Code.Contains("_revert_", StringComparison.Ordinal));
            foreach (var shift in ExpectedShiftDiffCounterShifts())
            {
                Assert.Equal(
                    ExpectedShiftDiffCounterThresholds(),
                    StrategyIds.CryptoUpDown5mVariants
                        .Where(variant =>
                            string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                            variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                            variant.Code.Contains("_up_shift_diff_", StringComparison.Ordinal) &&
                            !variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                            variant.ShiftDiffCount == shift)
                        .Select(variant => variant.DecisionDepth)
                        .OrderBy(threshold => threshold)
                        .ToArray());
                Assert.Equal(
                    ExpectedShiftDiffCounterThresholds(),
                    StrategyIds.CryptoUpDown5mVariants
                        .Where(variant =>
                            string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                            variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                            variant.Code.Contains("_down_shift_diff_", StringComparison.Ordinal) &&
                            !variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                            variant.ShiftDiffCount == shift)
                        .Select(variant => variant.DecisionDepth)
                        .OrderBy(threshold => threshold)
                        .ToArray());
                Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                    variant.Code.Contains("_up_shift_diff_", StringComparison.Ordinal) &&
                    variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                    variant.ShiftDiffCount == shift);
                Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                    string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend &&
                    variant.Code.Contains("_down_shift_diff_", StringComparison.Ordinal) &&
                    variant.Code.Contains("_revert_", StringComparison.Ordinal) &&
                    variant.ShiftDiffCount == shift);
            }
        }

        Assert.Equal("ETH Up or Down 5m Binance 2 bps", EthBinanceBps2Variant.Name);
        Assert.Equal(2m, EthBinanceBps2Variant.DecisionThresholdBps);
        Assert.Equal("ETH", EthBinanceBps2Variant.ReferenceAssetSymbol);
        Assert.Equal("ETH Up or Down 5m Skip 3", EthSkip3Variant.Name);
        Assert.Equal(3, EthSkip3Variant.DecisionDepth);
        Assert.Equal("ETH", EthSkip3Variant.ReferenceAssetSymbol);
        Assert.Equal("ETH Up or Down 5m Up 2 bps Instant", EthUpBps2InstantVariant.Name);
        Assert.Equal(2m, EthUpBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("ETH", EthUpBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Up, EthUpBps2InstantVariant.FixedOutcome);
        Assert.Equal("ETH Up or Down 5m Down 2 bps Instant", EthDownBps2InstantVariant.Name);
        Assert.Equal(2m, EthDownBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("ETH", EthDownBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, EthDownBps2InstantVariant.FixedOutcome);
        var ethDownBps9FakVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_down_bps_9_fak");
        Assert.Equal("ETH Up or Down 5m Down 9 bps", ethDownBps9FakVariant.Name);
        Assert.Equal(9m, ethDownBps9FakVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak, ethDownBps9FakVariant.Behavior);
        Assert.Equal("ETH", ethDownBps9FakVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDownBps9FakVariant.FixedOutcome);
        var ethDownBps9FakPremarketVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_down_reference_average_bps_9_fak_premarket");
        Assert.Equal("ETH Up or Down 5m Down 9 bps Reference Average Premarket", ethDownBps9FakPremarketVariant.Name);
        Assert.Equal(9m, ethDownBps9FakPremarketVariant.DecisionThresholdBps);
        Assert.Equal(BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket, ethDownBps9FakPremarketVariant.Behavior);
        Assert.Equal("ETH", ethDownBps9FakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethDownBps9FakPremarketVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDownBps9FakPremarketVariant.DiffCounterTriggerOutcome);
        Assert.Equal("ETH Up/Down 5m Reference Average Bps Premarket", ethDownBps9FakPremarketVariant.Category);
        Assert.Equal(-30, ethDownBps9FakPremarketVariant.EntryDelaySeconds);
        var ethDownDiff3FakPremarketVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_down_diff_3_fak_premarket");
        var ethDownDiffFakPremarketVariants = StrategyIds.CryptoUpDown5mVariants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket &&
                variant.ReferenceAssetSymbol == "ETH" &&
                variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Down)
            .ToArray();
        Assert.Equal(20, ethDownDiffFakPremarketVariants.Length);
        Assert.Equal(
            Enumerable.Range(1, 10),
            ethDownDiffFakPremarketVariants
                .Where(variant => variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        Assert.Equal(
            Enumerable.Range(1, 10),
            ethDownDiffFakPremarketVariants
                .Where(variant => variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        Assert.Equal("ETH Up or Down 5m Down 3 Diff Premarket", ethDownDiff3FakPremarketVariant.Name);
        Assert.Equal(3, ethDownDiff3FakPremarketVariant.DecisionDepth);
        Assert.Equal(BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket, ethDownDiff3FakPremarketVariant.Behavior);
        Assert.Equal("ETH", ethDownDiff3FakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethDownDiff3FakPremarketVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDownDiff3FakPremarketVariant.DiffCounterTriggerOutcome);
        Assert.Equal("ETH Up/Down 5m Diff Down Premarket", ethDownDiff3FakPremarketVariant.Category);
        Assert.Equal(-30, ethDownDiff3FakPremarketVariant.EntryDelaySeconds);
        var ethDownDiff3RevertFakPremarketVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_down_diff_3_revert_fak_premarket");
        Assert.Equal("ETH Up or Down 5m Down 3 Diff Revert Premarket", ethDownDiff3RevertFakPremarketVariant.Name);
        Assert.Equal(3, ethDownDiff3RevertFakPremarketVariant.DecisionDepth);
        Assert.Equal(BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket, ethDownDiff3RevertFakPremarketVariant.Behavior);
        Assert.Equal("ETH", ethDownDiff3RevertFakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDownDiff3RevertFakPremarketVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDownDiff3RevertFakPremarketVariant.DiffCounterTriggerOutcome);
        Assert.Equal("ETH Up/Down 5m Diff Down Revert Premarket", ethDownDiff3RevertFakPremarketVariant.Category);
        Assert.Equal(-30, ethDownDiff3RevertFakPremarketVariant.EntryDelaySeconds);
        var ethUpDiffFakPremarketVariants = StrategyIds.CryptoUpDown5mVariants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket &&
                variant.ReferenceAssetSymbol == "ETH" &&
                variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up)
            .ToArray();
        Assert.Equal(20, ethUpDiffFakPremarketVariants.Length);
        Assert.Equal(
            Enumerable.Range(1, 10),
            ethUpDiffFakPremarketVariants
                .Where(variant => variant.FixedOutcome == BtcUpDownFixedOutcome.Down)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        Assert.Equal(
            Enumerable.Range(1, 10),
            ethUpDiffFakPremarketVariants
                .Where(variant => variant.FixedOutcome == BtcUpDownFixedOutcome.Up)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        var ethUpDiff3FakPremarketVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_up_diff_3_fak_premarket");
        Assert.Equal("ETH Up or Down 5m Up 3 Diff Premarket", ethUpDiff3FakPremarketVariant.Name);
        Assert.Equal(3, ethUpDiff3FakPremarketVariant.DecisionDepth);
        Assert.Equal(BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket, ethUpDiff3FakPremarketVariant.Behavior);
        Assert.Equal("ETH", ethUpDiff3FakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethUpDiff3FakPremarketVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethUpDiff3FakPremarketVariant.DiffCounterTriggerOutcome);
        Assert.Equal("ETH Up/Down 5m Diff Up Premarket", ethUpDiff3FakPremarketVariant.Category);
        Assert.Equal(-30, ethUpDiff3FakPremarketVariant.EntryDelaySeconds);
        var ethUpDiff3RevertFakPremarketVariant = StrategyIds.CryptoUpDown5mVariants.Single(variant =>
            variant.Code == "eth_up_down_5m_up_diff_3_revert_fak_premarket");
        Assert.Equal("ETH Up or Down 5m Up 3 Diff Revert Premarket", ethUpDiff3RevertFakPremarketVariant.Name);
        Assert.Equal(3, ethUpDiff3RevertFakPremarketVariant.DecisionDepth);
        Assert.Equal(BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket, ethUpDiff3RevertFakPremarketVariant.Behavior);
        Assert.Equal("ETH", ethUpDiff3RevertFakPremarketVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethUpDiff3RevertFakPremarketVariant.FixedOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethUpDiff3RevertFakPremarketVariant.DiffCounterTriggerOutcome);
        Assert.Equal("ETH Up/Down 5m Diff Up Revert Premarket", ethUpDiff3RevertFakPremarketVariant.Category);
        Assert.Equal(-30, ethUpDiff3RevertFakPremarketVariant.EntryDelaySeconds);
        Assert.Equal("ETH Up or Down 15m Up 2 bps Instant", EthUp15mBps2InstantVariant.Name);
        Assert.Equal(2m, EthUp15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("ETH", EthUp15mBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, EthUp15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Up, EthUp15mBps2InstantVariant.FixedOutcome);
        Assert.Equal("ETH Up or Down 15m Down 2 bps Instant", EthDown15mBps2InstantVariant.Name);
        Assert.Equal(2m, EthDown15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("ETH", EthDown15mBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, EthDown15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Down, EthDown15mBps2InstantVariant.FixedOutcome);
        Assert.Equal("ETH Up or Down 5m Middle 100", EthMiddle1Variant.Name);
        Assert.Equal(100, EthMiddle1Variant.DecisionDepth);
        Assert.Equal("ETH", EthMiddle1Variant.ReferenceAssetSymbol);
        Assert.Equal("ETH Up or Down 5m Middle 100 20 bps", EthMiddleBps20Variant.Name);
        Assert.Equal(20m, EthMiddleBps20Variant.DecisionThresholdBps);
        Assert.Equal("ETH", EthMiddleBps20Variant.ReferenceAssetSymbol);
        Assert.Equal("SOL Up or Down 5m Up 2 bps Instant", SolUpBps2InstantVariant.Name);
        Assert.Equal(2m, SolUpBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("SOL", SolUpBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Up, SolUpBps2InstantVariant.FixedOutcome);
        Assert.Equal("SOL Up or Down 5m Down 2 bps Instant", SolDownBps2InstantVariant.Name);
        Assert.Equal(2m, SolDownBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("SOL", SolDownBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, SolDownBps2InstantVariant.FixedOutcome);
        Assert.Equal("SOL Up or Down 15m Up 2 bps Instant", SolUp15mBps2InstantVariant.Name);
        Assert.Equal(2m, SolUp15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("SOL", SolUp15mBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, SolUp15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Up, SolUp15mBps2InstantVariant.FixedOutcome);
        Assert.Equal("SOL Up or Down 15m Down 2 bps Instant", SolDown15mBps2InstantVariant.Name);
        Assert.Equal(2m, SolDown15mBps2InstantVariant.DecisionThresholdBps);
        Assert.Equal("SOL", SolDown15mBps2InstantVariant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, SolDown15mBps2InstantVariant.MarketInterval);
        Assert.Equal(BtcUpDownFixedOutcome.Down, SolDown15mBps2InstantVariant.FixedOutcome);
        Assert.Equal("SOL Up or Down 5m Binance 1 bps Instant", SolBinanceBps1InstantVariant.Name);
        Assert.Equal(1m, SolBinanceBps1InstantVariant.DecisionThresholdBps);
        Assert.Equal("SOL", SolBinanceBps1InstantVariant.ReferenceAssetSymbol);
    }

    [Fact]
    public void StrategyIds_IncludeEthDown9FakVariant()
    {
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_bps_9_fak");

        Assert.Equal("ETH Up or Down 5m Down 9 bps", variant.Name);
        Assert.Equal(9m, variant.DecisionThresholdBps);
        Assert.Equal(BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak, variant.Behavior);
        Assert.Equal("ETH", variant.ReferenceAssetSymbol);
        Assert.Equal(BtcUpDownFixedOutcome.Down, variant.FixedOutcome);
    }

    [Fact]
    public void StrategyIds_IncludeEthDownLegacyFakPremarketVariants()
    {
        var variants = StrategyIds.CryptoUpDown5mVariants
            .Where(item => item.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket)
            .OrderBy(item => item.EntryDelaySeconds)
            .ThenBy(item => item.DecisionThresholdBps.GetValueOrDefault())
            .ToArray();
        var minus10Variants = variants
            .Where(item => item.EntryDelaySeconds == -10)
            .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
            .ToArray();
        var minus5Variants = variants
            .Where(item => item.EntryDelaySeconds == -5)
            .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
            .ToArray();
        var minus30Variants = variants
            .Where(item => item.EntryDelaySeconds == -30)
            .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
            .ToArray();

        Assert.Equal(62, variants.Length);
        Assert.Equal(
            Enumerable.Range(1, 50).Select(threshold => (decimal)threshold).ToArray(),
            minus30Variants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
        Assert.Equal(
            new[] { 40m, 41m, 42m },
            minus10Variants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
        Assert.Equal(
            Enumerable.Range(30, 9).Select(threshold => (decimal)threshold).ToArray(),
            minus5Variants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
        Assert.All(variants, item =>
        {
            Assert.Equal("ETH", item.ReferenceAssetSymbol);
            Assert.Equal(BtcUpDownFixedOutcome.Down, item.FixedOutcome);
        });
        Assert.All(minus30Variants, item => Assert.Equal(-30, item.EntryDelaySeconds));
        Assert.All(minus10Variants, item => Assert.Equal(-10, item.EntryDelaySeconds));
        Assert.All(minus5Variants, item => Assert.Equal(-5, item.EntryDelaySeconds));
        Assert.Equal("ETH Up or Down 5m Down 9 bps Premarket", minus30Variants.Single(item => item.DecisionThresholdBps == 9m).Name);
        Assert.Equal("eth_up_down_5m_down_bps_9_fak_premarket", minus30Variants.Single(item => item.DecisionThresholdBps == 9m).Code);
        Assert.Equal("ETH Up or Down 5m Down 41 bps Premarket -10s", minus10Variants.Single(item => item.DecisionThresholdBps == 41m).Name);
        Assert.Equal("eth_up_down_5m_down_bps_35_fak_premarket_m5s", minus5Variants.Single(item => item.DecisionThresholdBps == 35m).Code);
    }

    [Fact]
    public void StrategyIds_IncludeReferenceAverageFakPremarketVariants()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(item => item.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket)
            .ToArray();

        Assert.Equal(168, variants.Length);
        Assert.All(variants, item => Assert.Contains(" Reference Average ", item.Name, StringComparison.Ordinal));
        foreach (var assetSymbol in new[] { "BTC", "ETH", "SOL" })
        {
            foreach (var triggerOutcome in new[] { BtcUpDownFixedOutcome.Up, BtcUpDownFixedOutcome.Down })
            {
                var triggerVariants = variants
                    .Where(item =>
                        string.Equals(item.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        item.DiffCounterTriggerOutcome == triggerOutcome)
                    .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
                    .ToArray();

                Assert.Equal(ExpectedReferenceAverageBpsThresholds(), triggerVariants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
                Assert.All(triggerVariants, item =>
                {
                    Assert.Equal(-30, item.EntryDelaySeconds);
                    Assert.Equal($"{assetSymbol} Up/Down 5m Reference Average Bps Premarket", item.Category);
                    Assert.Equal(
                        triggerOutcome == BtcUpDownFixedOutcome.Up ? BtcUpDownFixedOutcome.Down : BtcUpDownFixedOutcome.Up,
                        item.FixedOutcome);
                });
            }
        }

        Assert.DoesNotContain(variants, item => item.Code == "eth_up_down_5m_down_bps_9_fak_premarket");
        var ethDown9 = variants.Single(item => item.Code == "eth_up_down_5m_down_reference_average_bps_9_fak_premarket");
        Assert.Equal("ETH Up or Down 5m Down 9 bps Reference Average Premarket", ethDown9.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Down, ethDown9.DiffCounterTriggerOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, ethDown9.FixedOutcome);

        var btcUp100 = variants.Single(item => item.Code == "btc_up_down_5m_up_bps_100_fak_premarket");
        Assert.Equal("BTC Up or Down 5m Up 100 bps Reference Average Premarket", btcUp100.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Up, btcUp100.DiffCounterTriggerOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Down, btcUp100.FixedOutcome);

        var solDown15 = variants.Single(item => item.Code == "sol_up_down_5m_down_bps_15_fak_premarket");
        Assert.Equal("SOL Up or Down 5m Down 15 bps Reference Average Premarket", solDown15.Name);
        Assert.Equal(BtcUpDownFixedOutcome.Down, solDown15.DiffCounterTriggerOutcome);
        Assert.Equal(BtcUpDownFixedOutcome.Up, solDown15.FixedOutcome);
    }

    [Fact]
    public void MarketAnalyzersRecognizeFifteenMinuteUpDownMarkets()
    {
        var windowStart = new DateTimeOffset(2026, 6, 7, 8, 45, 0, TimeSpan.Zero);
        var btcMarket = CreateMarket(
            windowStart,
            windowStart.AddMinutes(15),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"btc-updown-15m-{windowStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "btc-up-or-down-15m") with
        {
            EventStartTimeUtc = null
        };
        var ethMarket = CreateMarket(
            windowStart,
            windowStart.AddMinutes(15),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-15m-{windowStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-15m",
            question: "ETH Up or Down - test",
            marketId: "eth-15m-market-1",
            conditionId: "eth-15m-condition-1",
            upAssetId: "eth-15m-asset-up",
            downAssetId: "eth-15m-asset-down") with
        {
            EventStartTimeUtc = null
        };

        Assert.False(BtcUpDown5mMarketAnalyzer.IsCandidate(btcMarket));
        Assert.True(BtcUpDown5mMarketAnalyzer.IsStrategyCandidate(btcMarket));
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, BtcUpDown5mMarketAnalyzer.GetMarketInterval(btcMarket));
        Assert.Equal(windowStart, BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(btcMarket));
        Assert.Equal(TimeSpan.FromMinutes(15), BtcUpDown5mMarketAnalyzer.GetIntervalDuration(BtcUpDownMarketInterval.FifteenMinutes));

        Assert.True(CryptoUpDown5mMarketAnalyzer.TryGetAssetSymbol(
            ethMarket,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ETH" },
            out var assetSymbol));
        Assert.Equal("ETH", assetSymbol);
        Assert.Equal(BtcUpDownMarketInterval.FifteenMinutes, CryptoUpDown5mMarketAnalyzer.GetMarketInterval(ethMarket));
        Assert.Equal(windowStart, CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(ethMarket));
        Assert.Equal(TimeSpan.FromMinutes(15), CryptoUpDown5mMarketAnalyzer.GetIntervalDuration(BtcUpDownMarketInterval.FifteenMinutes));
    }

    [Fact]
    public async Task ProcessAsync_LessVariantObservesDueMarketAndBuysLowerPricedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-120),
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
            now.AddSeconds(-120),
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
    public async Task ProcessAsync_AllowsPaperOppositeOutcomeWhenSameMarketHasOpenBuyOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AlwaysUpVariant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.35m,
            3m,
            1.05m,
            now.AddSeconds(-15),
            now.AddMinutes(2),
            StrategyId: AlwaysUpVariant.Id));
        var processor = CreateProcessor(repository, [], More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.True(
            result.EntriesPlaced == 1,
            string.Join("; ", repository.StrategyMarketPaperRuns.Select(run =>
                $"{run.MarketStartUtc:o}:{run.Status}:{run.SkipReason}:{run.SkipDiagnosticsJson}")));
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(2, repository.PaperOrders.Count);
        Assert.DoesNotContain(
            repository.StrategyMarketPaperRuns,
            item => item.SkipReason == SignalReasonCodes.OppositeOutcomeOpenOrder);
        var run = Assert.Single(
            repository.StrategyMarketPaperRuns,
            item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(More60Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Null(run.SkipReason);
        Assert.DoesNotContain(
            SignalReasonCodes.OppositeOutcomeOpenOrder,
            run.SkipDiagnosticsJson ?? string.Empty,
            StringComparison.Ordinal);
        var order = repository.PaperOrders.Single(item => item.StrategyId == More60Variant.Id);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
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

        Assert.True(
            result.EntriesPlaced == 1,
            string.Join("; ", repository.StrategyMarketPaperRuns.Select(run =>
                $"{run.MarketStartUtc:o}:{run.Status}:{run.SkipReason}:{run.SkipDiagnosticsJson}")));
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
        Assert.DoesNotContain(
            repository.StrategyMarketPaperRuns,
            item => item.SkipReason == SignalReasonCodes.OppositeOutcomeOpenOrder);
        var run = Assert.Single(
            repository.StrategyMarketPaperRuns,
            item => item.Status == StrategyMarketPaperRunStatuses.Entered);
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
    public async Task ProcessAsync_SettlesEnteredRunFromClosedGammaMetadata()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            Less60Variant.Id,
            "market-previous",
            "condition-previous",
            "btc-updown-5m-1778067600",
            "Bitcoin Up or Down - previous",
            "Crypto",
            now.AddMinutes(-16),
            now.AddMinutes(-11),
            now.AddMinutes(-16),
            now.AddMinutes(-15),
            StrategyMarketPaperRunStatuses.Settled,
            "asset-previous-up",
            "Up",
            0.50m,
            1m,
            2m,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now.AddMinutes(-15),
            SettlementPrice: 0.50m,
            SettlementValueUsd: 1m,
            RealizedPnlUsd: 0m,
            SettledAtUtc: now.AddMinutes(-11),
            SkipReason: null,
            now.AddMinutes(-16),
            now.AddMinutes(-11)));
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
        var updatedRun = repository.StrategyMarketPaperRuns.Single(item => item.Id == run.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(0m, updatedRun.SettlementPrice);
        Assert.Equal(-1m, updatedRun.RealizedPnlUsd);
        var settings = repository.StrategySettings[Less60Variant.Id];
        Assert.False(settings.AutoLivePaused);
        Assert.False(settings.Paused);
        Assert.Null(settings.PausedUntilUtc);

        var settlement = Assert.Single(repository.PaperPositionSettlements);
        Assert.False(settlement.Won);
        Assert.Equal(-1m, settlement.RealizedPnlUsd);
        Assert.Equal(0m, Assert.Single(repository.PaperPositions).SizeShares);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotPauseStrategyAfterSingleSettledLoss()
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
        Assert.Equal(-1m, updatedRun.RealizedPnlUsd);
        var settings = repository.StrategySettings[Less60Variant.Id];
        Assert.False(settings.AutoLivePaused);
        Assert.False(settings.Paused);
        Assert.Null(settings.PausedUntilUtc);
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
            item.Code == "btc_up_down_1h_preopen_half_up_37");
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
    public async Task ProcessAsync_PaperLostCounterBoostsNextPaperStakeAndCapsCoeff()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 1m,
            PaperLostCoeff = 2m
        };
        var metadata = new List<PolymarketOnChainTokenMetadata>();
        for (var index = 1; index <= 6; index++)
        {
            var upAssetId = "asset-up-previous-loss-" + index.ToString(CultureInfo.InvariantCulture);
            var downAssetId = "asset-down-previous-loss-" + index.ToString(CultureInfo.InvariantCulture);
            var previousLossRun = AddEnteredRun(
                repository,
                Middle1Variant,
                "previous-loss-" + index.ToString(CultureInfo.InvariantCulture),
                now.AddMinutes(-10 - (index * 5)),
                selectedAssetId: upAssetId,
                selectedOutcome: "Up",
                stakeUsd: 1m);
            repository.PaperOrders.Add(new PaperOrder(
                previousLossRun.PaperOrderId!.Value,
                previousLossRun.SignalId!.Value,
                Middle1Variant.CopiedTraderWallet,
                PaperOrderStatus.Filled,
                TradeSide.Buy,
                upAssetId,
                previousLossRun.ConditionId,
                "Up",
                0.50m,
                2m,
                1m,
                previousLossRun.EnteredAtUtc!.Value,
                previousLossRun.MarketEndUtc!.Value,
                FilledAtUtc: previousLossRun.EnteredAtUtc!.Value.AddSeconds(1),
                StrategyId: Middle1Variant.Id));
            repository.PaperFills.Add(new PaperFill(
                Guid.NewGuid(),
                previousLossRun.PaperOrderId.Value,
                0.50m,
                2m,
                previousLossRun.EnteredAtUtc.Value.AddSeconds(1),
                "TestOpeningLimitFill"));
            metadata.Add(TokenMetadata(upAssetId, "Up", "Down"));
            metadata.Add(TokenMetadata(downAssetId, "Down", "Down"));
        }

        var processor = CreateProcessorCoreWithOptions(
            repository,
            metadata,
            DefaultOrderBooks(),
            _ => { },
            [],
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(103m),
            btcUsdReferencePriceCache: CreateBtcUsdReferenceCache(99m, 101m));

        var settleResult = await processor.ProcessAsync();

        Assert.Equal(6, settleResult.RunsSettled);
        Assert.Equal(0, settleResult.EntriesPlaced);
        Assert.Equal(6, repository.PaperOrders.Count);
        Assert.Equal(6, repository.StrategySettings[Middle1Variant.Id].PaperLostCounter);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));

        var entryResult = await processor.ProcessAsync();

        Assert.Equal(1, entryResult.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Middle1Variant.Id &&
            string.Equals(item.MarketId, "market-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(3m, run.StakeUsd);
        Assert.Equal(6m, run.SizeShares);

        var order = Assert.Single(repository.PaperOrders, item =>
            string.Equals(item.AssetId, "asset-down", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3m, order.NotionalUsd);
        Assert.Equal(6m, order.SizeShares);
        Assert.Contains("\"paper_lost_coeff_configured\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_counter\":6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_counter_coeff\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_base_stake_usd\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_add_stake_usd\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_effective_stake_usd\":3", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PaperLostCounterWinCanGoNegative()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 1m,
            PaperLostCoeff = 2m
        };
        var upAssetId = "asset-up-previous-win";
        var downAssetId = "asset-down-previous-win";
        var previousWinRun = AddEnteredRun(
            repository,
            Middle1Variant,
            "previous-win",
            now.AddMinutes(-10),
            selectedAssetId: upAssetId,
            selectedOutcome: "Up",
            stakeUsd: 1m);
        repository.PaperOrders.Add(new PaperOrder(
            previousWinRun.PaperOrderId!.Value,
            previousWinRun.SignalId!.Value,
            Middle1Variant.CopiedTraderWallet,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            upAssetId,
            previousWinRun.ConditionId,
            "Up",
            0.50m,
            2m,
            1m,
            previousWinRun.EnteredAtUtc!.Value,
            previousWinRun.MarketEndUtc!.Value,
            FilledAtUtc: previousWinRun.EnteredAtUtc!.Value.AddSeconds(1),
            StrategyId: Middle1Variant.Id));
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            previousWinRun.PaperOrderId.Value,
            0.50m,
            2m,
            previousWinRun.EnteredAtUtc.Value.AddSeconds(1),
            "TestOpeningLimitFill"));

        var processor = CreateProcessorCoreWithOptions(
            repository,
            [
                TokenMetadata(upAssetId, "Up", "Up"),
                TokenMetadata(downAssetId, "Down", "Up")
            ],
            DefaultOrderBooks(),
            _ => { },
            [],
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(103m),
            btcUsdReferencePriceCache: CreateBtcUsdReferenceCache(99m, 101m));

        var settleResult = await processor.ProcessAsync();

        Assert.Equal(1, settleResult.RunsSettled);
        Assert.Equal(-1, repository.StrategySettings[Middle1Variant.Id].PaperLostCounter);
    }

    [Fact]
    public async Task ProcessAsync_NegativePaperLostCounterDoesNotBoostStake()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Variant.Id] = StrategyRuntimeSettings.Default(Middle1Variant.Id) with
        {
            PaperStakeAmount = 1m,
            PaperLostCoeff = 2m,
            PaperLostCounter = -2
        };
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
            [],
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(103m),
            btcUsdReferencePriceCache: CreateBtcUsdReferenceCache(99m, 101m));

        var entryResult = await processor.ProcessAsync();

        Assert.Equal(1, entryResult.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == Middle1Variant.Id &&
            string.Equals(item.MarketId, "market-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders, item =>
            string.Equals(item.AssetId, "asset-down", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"paper_lost_counter\":-2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_counter_coeff\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_add_stake_usd\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_lost_effective_stake_usd\":1", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBpsThresholdSkipsSmallMeanDeviation()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Bps20Variant.Id] = StrategyRuntimeSettings.Default(Middle1Bps20Variant.Id) with
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
            currentBtcUsd: 100.05m,
            cachedBtcUsd: [100m],
            Middle1Bps20Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Middle1Bps20Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_reference_mean_deviation_below_threshold", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"btc_move_from_mean_bps\":5", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_mean_bps\":5", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_abs_move_from_mean_bps\":5", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_mean_bps\":20", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":null", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBulkSkipsEarliestDueGroupBeyondConfiguredEntryLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var btcReferenceClient = new FakeBtcUsdReferencePriceClient(100.05m);
        var enabledCodes = new[]
        {
            Middle1Bps20Variant.Code,
            Middle1Bps100Variant.Code
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledCodes,
                maxEntriesPerCycle: 1,
                maxConcurrentEntryDecisions: 1),
            btcReferenceClient,
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(2, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(2, result.RunsSkipped);
        Assert.Equal(1, btcReferenceClient.RequestCount);
        Assert.Equal(1, repository.BulkStrategyMarketPaperRunUpdateCalls);
        Assert.Empty(repository.PaperOrders);
        Assert.Equal(2, repository.StrategyMarketPaperRuns.Count);
        Assert.All(repository.StrategyMarketPaperRuns, run =>
        {
            Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
            Assert.Equal("btc_reference_mean_deviation_below_threshold", run.SkipReason);
            Assert.NotNull(run.SkipDiagnosticsJson);
            Assert.Contains("\"btc_move_from_mean_bps\":5", run.SkipDiagnosticsJson!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBpsThresholdEntersWhenMeanDeviationReachesThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Bps20Variant.Id] = StrategyRuntimeSettings.Default(Middle1Bps20Variant.Id) with
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
            currentBtcUsd: 100.21m,
            cachedBtcUsd: [100m],
            Middle1Bps20Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Middle1Bps20Variant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Middle1Bps20Variant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"btc_move_from_mean_bps\":21", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_mean_bps\":21", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_abs_move_from_mean_bps\":21", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_mean_bps\":20", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBpsInstantPricesOpeningLimitFromExecutableAskDepth()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.39m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.64m, 20m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Bps20InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100.21m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Middle1Bps20InstantVariant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Middle1Bps20InstantVariant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.64m, order.Price);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_mean_bps\":20", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_target_size_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceBpsInstantSkipsWhenExecutableAskDepthRequiresPriceAboveCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.39m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.62m, 100m)],
                [new OrderBookLevel(0.64m, 4m), new OrderBookLevel(0.66m, 20m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1Bps20InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100.21m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal(SignalReasonCodes.InstantPriceAboveMax, run.SkipReason);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":0.65", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.66", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_executable_ask_shares\":4", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MiddleReferenceRevertBpsInstantInvertsDirectionAndPricesFromExecutableAskDepth()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
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
            CreateBtcOptions(paperTakerPricingEnabled: false, [Middle1RevertBps100InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101.01m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Middle1RevertBps100InstantVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Middle1RevertBps100InstantVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference_revert\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"revert_decision\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_mean_bps\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
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
    public async Task ProcessAsync_SimpleUpPricesOpeningLimitFromExecutableAskDepthAtOrBelowHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.49m,
            downPrice: 0.51m));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.49m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.50m, 100m)],
                [new OrderBookLevel(0.51m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpSimpleVariant.Code]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(UpSimpleVariant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.49m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(UpSimpleVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.49m, order.Price);
        Assert.Contains("\"decision_source\":\"simple_up_instant\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":0.50", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.49", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"instant_resting_at_max_price\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SimpleDownPlacesRestingHalfOrderWhenExecutableAskDepthAboveHalf()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "sol-asset-up",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.38m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "sol-asset-down",
                [new OrderBookLevel(0.61m, 100m)],
                [new OrderBookLevel(0.62m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [SolDownSimpleVariant.Code]));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolDownSimpleVariant.Id, run.StrategyId);
        Assert.Equal("sol-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(SolDownSimpleVariant.Id, order.StrategyId);
        Assert.Equal("sol-asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"decision_source\":\"simple_down_instant\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":0.50", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_raw_limit_price\":0.62", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.50", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_resting_at_max_price\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_executable_ask_shares\":0", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SimpleLiveStakeCreatesPaperShadowAndLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[UpSimpleVariant.Id] = StrategyRuntimeSettings.Default(UpSimpleVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.49m,
            downPrice: 0.51m));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.49m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.50m, 100m)],
                [new OrderBookLevel(0.51m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpSimpleVariant.Code]),
            tradingClient: tradingClient,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m });

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.True(
            tradingClient.PlaceCalls == 1,
            string.Join(" | ", repository.LiveOrders.Select(order => order.Status + ": " + order.ValidationSummary)));
        Assert.NotNull(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, tradingClient.LastRequest.OrderType);
        Assert.False(tradingClient.LastRequest.PostOnly);
        Assert.Null(tradingClient.LastRequest.GtdExpirationUtc);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.Equal(0.49m, liveOrder.Price);
        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"post_only\":false", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.False(decision.PostOnly);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_ProcessesLiveDueEntriesBeforeNonLiveDueEntries()
    {
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var repository = new TestAppRepository();
        repository.StrategySettings[UpSimpleVariant.Id] = StrategyRuntimeSettings.Default(UpSimpleVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.StrategySettings[DownSimpleVariant.Id] = StrategyRuntimeSettings.Default(DownSimpleVariant.Id) with
        {
            LiveStakes = false,
            PaperStakeAmount = 2.50m
        };

        var market = CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.49m,
            downPrice: 0.51m);
        repository.PolymarketGammaMarkets.Add(market);
        var nonLiveRun = CreateObservedRun(
            DownSimpleVariant,
            market,
            now,
            now.AddSeconds(-40),
            now.AddSeconds(-30));
        var liveRun = CreateObservedRun(
            UpSimpleVariant,
            market,
            now,
            now.AddSeconds(-5),
            now);
        repository.StrategyMarketPaperRuns.Add(nonLiveRun);
        repository.StrategyMarketPaperRuns.Add(liveRun);

        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.49m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.50m, 100m)],
                [new OrderBookLevel(0.51m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [UpSimpleVariant.Code, DownSimpleVariant.Code],
                maxEntriesPerCycle: 1),
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(UpSimpleVariant.Id, paperOrder.StrategyId);

        var updatedLiveRun = repository.StrategyMarketPaperRuns.Single(run => run.Id == liveRun.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, updatedLiveRun.Status);
        Assert.NotNull(updatedLiveRun.PaperOrderId);
        Assert.Null(updatedLiveRun.SkipReason);

        var updatedNonLiveRun = repository.StrategyMarketPaperRuns.Single(run => run.Id == nonLiveRun.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, updatedNonLiveRun.Status);
        Assert.Equal("entry_due_expired", updatedNonLiveRun.SkipReason);
    }

    [Fact]
    public async Task ProcessAsync_LiveStakePrioritizesHigherPreparedLiveRealizedPnl()
    {
        var now = new DateTimeOffset(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);
        var repository = new TestAppRepository();
        repository.StrategySettings[UpSimpleVariant.Id] = StrategyRuntimeSettings.Default(UpSimpleVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.StrategySettings[DownSimpleVariant.Id] = StrategyRuntimeSettings.Default(DownSimpleVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.LiveOrders.Add(CreateSettledLiveOrder(UpSimpleVariant.Id, 1m, now.AddMinutes(-10)));
        repository.LiveOrders.Add(CreateSettledLiveOrder(DownSimpleVariant.Id, 5m, now.AddMinutes(-5)));

        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.49m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.48m, 100m)],
                [new OrderBookLevel(0.49m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpSimpleVariant.Code, DownSimpleVariant.Code]),
            tradingClient: tradingClient,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            timeProvider: new ManualTimeProvider(now),
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m });

        var warmupResult = await processor.ProcessAsync();
        Assert.Equal(0, warmupResult.EntriesPlaced);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.49m,
            downPrice: 0.49m));

        var result = await processor.ProcessAsync();

        Assert.True(
            result.EntriesPlaced >= 1,
            string.Join(" | ", repository.StrategyMarketPaperRuns.Select(run =>
                $"{run.StrategyId}:{run.Status}:{run.SkipReason}:{run.SkipDiagnosticsJson}")));
        var currentMarketPaperOrders = repository.PaperOrders
            .Where(order => string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(currentMarketPaperOrders);
        Assert.Equal(DownSimpleVariant.Id, currentMarketPaperOrders[0].StrategyId);

        var currentMarketLiveOrders = repository.LiveOrders
            .Where(order => string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(currentMarketLiveOrders);
        Assert.Equal(DownSimpleVariant.Id, currentMarketLiveOrders[0].StrategyId);
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
    public async Task ProcessDiffCounterDueEntriesAsync_DiffUpThresholdBuysDownAtInstantExecutableAskPrice()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_2_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var gammaClient = new FakeGammaClient([]);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: gammaClient,
            timeProvider: timeProvider);

        var startupResult = await processor.ProcessDiffCounterDueEntriesAsync();
        Assert.Equal(0, startupResult.EntriesPlaced);
        Assert.Equal(0, gammaClient.ClosedMarketRequestCount);

        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.True(
            result.EntriesPlaced == 1,
            string.Join("; ", repository.StrategyMarketPaperRuns.Select(run =>
                $"{run.MarketStartUtc:o}:{run.Status}:{run.SkipReason}:{run.SkipDiagnosticsJson}")));
        var run = repository.StrategyMarketPaperRuns.Single(item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.45m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        Assert.Equal(0, gammaClient.ClosedMarketRequestCount);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_countertrend\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_mode\":\"utc_day_start\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_result_source\":\"ResolvedMarketLedger\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_target_market_result_source\":\"MarketWebSocket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_target_market_result_received\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_count\":15", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":1.00", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.45", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffUpRevertThresholdBuysUpAtInstantExecutableAskPrice()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_2_revert_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.44m, bestAsk: 0.45m, entryNow),
            OrderBook("asset-down", bestBid: 0.54m, bestAsk: 0.55m, entryNow)
        };
        var gammaClient = new FakeGammaClient([]);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: gammaClient,
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-revert-entry-market",
            conditionId: "diff-revert-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.45m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        Assert.Equal(0, gammaClient.ClosedMarketRequestCount);
        Assert.Contains("\"trigger_side\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_trigger_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffInstantBuysAtExecutableAskPriceAboveHalf()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_2_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.44m, bestAsk: 0.45m, entryNow),
            OrderBook("asset-down", bestBid: 0.54m, bestAsk: 0.55m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.55m, run.EntryPrice);
        Assert.Null(run.SkipReason);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.55m, order.Price);
        Assert.Contains("\"instant_max_buy_price\":1.00", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_raw_limit_price\":0.55", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.55", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"instant_resting_at_max_price\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"instant_executable_ask_shares\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain(SignalReasonCodes.InstantPriceAboveMax, order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffDownThresholdBuysUpAtInstantExecutableAskPrice()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_down_diff_2_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Down",
            "Down",
            "Down",
            "Down",
            "Down");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.36m, bestAsk: 0.37m, entryNow),
            OrderBook("asset-down", bestBid: 0.62m, bestAsk: 0.63m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.37m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.37m, order.Price);
        Assert.Contains("\"up_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_count\":-15", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":-5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.37", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffInstantFakFillsAvailablePartialAskDepth()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_down_diff_2_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Down",
            "Down",
            "Down",
            "Down",
            "Down");
        var orderBooks = new[]
        {
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.36m, 100m)],
                [new OrderBookLevel(0.37m, 1m)],
                entryNow,
                minOrderSize: 1m),
            OrderBook("asset-down", bestBid: 0.62m, bestAsk: 0.63m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-partial-entry-market",
            conditionId: "diff-partial-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, repository.PaperEntryPersistenceBatchCalls);
        var run = repository.StrategyMarketPaperRuns.Single(item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.37m, run.EntryPrice);
        Assert.Equal(0.37m, run.StakeUsd);
        Assert.Equal(1m, run.SizeShares);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.37m, order.Price);
        Assert.Equal(1m, order.SizeShares);
        Assert.Equal(0.37m, order.NotionalUsd);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_fak_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_partial_fill\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_filled_notional_usd\":0.37", order.RawDecisionJson, StringComparison.Ordinal);
        var fill = Assert.Single(repository.PaperFills);
        Assert.Equal(order.Id, fill.PaperOrderId);
        var position = Assert.Single(repository.PaperPositions);
        Assert.Equal("asset-up", position.AssetId);
        Assert.Equal(1m, position.SizeShares);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffProgressUpBuysDownWithDiffMinusThresholdStake()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_diff_2_up_progress");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-progress-entry-market",
            conditionId: "diff-progress-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(3m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(3m, order.NotionalUsd);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_progress\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_mode_before\":\"Waiting\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_mode_after\":\"Betting\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"threshold\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_multiplier\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_usd\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffProgressCapsStakeMultiplierAtTen()
    {
        var entryMarketStartUtc = new DateTimeOffset(2026, 6, 8, 14, 35, 0, TimeSpan.Zero);
        var entryNow = entryMarketStartUtc.AddMinutes(2);
        var timeProvider = new ManualTimeProvider(entryNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_diff_2_up_progress");
        var repository = new TestAppRepository();
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            Enumerable.Repeat("Up", 13).ToArray());
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-progress-capped-entry-market",
            conditionId: "diff-progress-capped-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(10m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(10m, order.NotionalUsd);
        Assert.Contains("\"effective_diff\":13", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"threshold\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"uncapped_progress_stake_multiplier\":11", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_multiplier_cap\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_multiplier\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_multiplier_capped\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_stake_usd\":10", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffProgressResetsUtcMidnightWhileBetting()
    {
        var previousDayMarketStartUtc = new DateTimeOffset(2026, 6, 8, 23, 55, 0, TimeSpan.Zero);
        var previousDayNow = previousDayMarketStartUtc.AddMinutes(3);
        var nextDayMarketStartUtc = new DateTimeOffset(2026, 6, 9, 0, 5, 0, TimeSpan.Zero);
        var nextDayNow = nextDayMarketStartUtc.AddMinutes(2);
        var timeProvider = new ManualTimeProvider(previousDayNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_diff_2_up_progress");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousDayMarketStartUtc,
            previousDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "previous-day-progress-market",
            conditionId: "previous-day-progress-condition"));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            previousDayMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, nextDayNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, nextDayNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var previousDayResult = await processor.ProcessDiffCounterDueEntriesAsync();
        Assert.Equal(1, previousDayResult.EntriesPlaced);

        timeProvider.UtcNow = nextDayNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            nextDayMarketStartUtc,
            nextDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "next-day-progress-market",
            conditionId: "next-day-progress-condition"));
        AddWebSocketDiffResults(repository, "BTC", new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero), "Down");

        var nextDayResult = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, nextDayResult.EntriesPlaced);
        Assert.Equal(1, nextDayResult.RunsSkipped);
        var nextRun = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == nextDayMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, nextRun.Status);
        Assert.Equal("diff_progress_returned_to_threshold", nextRun.SkipReason);
        Assert.NotNull(nextRun.SkipDiagnosticsJson);
        Assert.Contains("\"progress_mode_before\":\"Betting\"", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_mode_after\":\"Waiting\"", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"reset_postponed\":false", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_start_market_start_utc\":\"2026-06-09T00:00:00", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":0", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":1", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":-1", nextRun.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            repository.PaperOrders,
            order => string.Equals(order.ConditionId, "next-day-progress-condition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffShiftProgressPersistsPendingBetWithDiffPlusOneStake()
    {
        var entryMarketStartUtc = new DateTimeOffset(2026, 6, 8, 14, 35, 0, TimeSpan.Zero);
        var entryNow = entryMarketStartUtc.AddMinutes(2);
        var timeProvider = new ManualTimeProvider(entryNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "btc_up_down_5m_diff_up_shift_progress");
        var repository = new TestAppRepository();
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-shift-progress-entry-market",
            conditionId: "diff-shift-progress-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(3m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(3m, order.NotionalUsd);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"persistent_diff_shift_progress\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_usd\":3", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(variant.Id, state.StrategyId);
        Assert.Equal("BTC", state.AssetSymbol);
        Assert.Equal("Up", state.TriggerOutcome);
        Assert.Equal(2, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(0m, state.SumAmount);
        Assert.Equal(entryMarketStartUtc.AddMinutes(-5), state.LastProcessedMarketStartUtc);
        Assert.Equal(entryMarketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Down", state.PendingTargetOutcome);
        Assert.Equal(3m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffShiftProgressShiftsDiffWhenSumExceedsUnit()
    {
        var entryMarketStartUtc = new DateTimeOffset(2026, 6, 8, 15, 35, 0, TimeSpan.Zero);
        var entryNow = entryMarketStartUtc.AddMinutes(2);
        var timeProvider = new ManualTimeProvider(entryNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "btc_up_down_5m_diff_up_shift_progress");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "BTC",
            "Up",
            UpCount: 2,
            DownCount: 0,
            SumAmount: 2.50m,
            LastProcessedMarketStartUtc: entryMarketStartUtc.AddMinutes(-5),
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: entryNow.AddMinutes(-10),
            UpdatedAtUtc: entryNow.AddMinutes(-10)));
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-shift-progress-shift-market",
            conditionId: "diff-shift-progress-shift-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(2m, run.StakeUsd);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(2m, order.NotionalUsd);
        Assert.Contains("\"shift_count\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":2", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(1, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(1.50m, state.SumAmount);
        Assert.Equal(entryMarketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Down", state.PendingTargetOutcome);
        Assert.Equal(2m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_EthDown3FakPremarketBuysUpFromPremarketOrderBook()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_down_diff_3_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-diff-premarket-entry-market",
            conditionId: "eth-diff-premarket-entry-condition",
            upAssetId: "eth-diff-premarket-up",
            downAssetId: "eth-diff-premarket-down"));
        AddWebSocketDiffResults(
            repository,
            "ETH",
            marketStartUtc.AddMinutes(-10),
            "Down",
            "Down",
            "Down");
        var orderBooks = new[]
        {
            OrderBook(
                "eth-diff-premarket-up",
                [new OrderBookLevel(0.45m, 100m)],
                [new OrderBookLevel(0.47m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-diff-premarket-down",
                [new OrderBookLevel(0.52m, 100m)],
                [new OrderBookLevel(0.54m, 100m)],
                now,
                minOrderSize: 1m)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-diff-premarket-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.47m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-diff-premarket-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.47m, order.Price);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_countertrend_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_fak_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_trigger_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_average_fill_price\":0.47", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_EthDown4RevertFakPremarketBuysDownFromPremarketOrderBook()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_down_diff_4_revert_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-diff-premarket-revert-entry-market",
            conditionId: "eth-diff-premarket-revert-entry-condition",
            upAssetId: "eth-diff-premarket-revert-up",
            downAssetId: "eth-diff-premarket-revert-down"));
        AddWebSocketDiffResults(
            repository,
            "ETH",
            marketStartUtc.AddMinutes(-10),
            "Down",
            "Down",
            "Down",
            "Down");
        var orderBooks = new[]
        {
            OrderBook(
                "eth-diff-premarket-revert-up",
                [new OrderBookLevel(0.45m, 100m)],
                [new OrderBookLevel(0.47m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-diff-premarket-revert-down",
                [new OrderBookLevel(0.52m, 100m)],
                [new OrderBookLevel(0.54m, 100m)],
                now,
                minOrderSize: 1m)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-diff-premarket-revert-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.54m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-diff-premarket-revert-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.54m, order.Price);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_countertrend_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_fak_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_trigger_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_average_fill_price\":0.54", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_EthUp4FakPremarketBuysDownFromPremarketOrderBook()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_up_diff_4_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-up-diff-premarket-entry-market",
            conditionId: "eth-up-diff-premarket-entry-condition",
            upAssetId: "eth-up-diff-premarket-up",
            downAssetId: "eth-up-diff-premarket-down"));
        AddWebSocketDiffResults(
            repository,
            "ETH",
            marketStartUtc.AddMinutes(-10),
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook(
                "eth-up-diff-premarket-up",
                [new OrderBookLevel(0.45m, 100m)],
                [new OrderBookLevel(0.47m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-up-diff-premarket-down",
                [new OrderBookLevel(0.52m, 100m)],
                [new OrderBookLevel(0.54m, 100m)],
                now,
                minOrderSize: 1m)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-up-diff-premarket-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.54m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-up-diff-premarket-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.54m, order.Price);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_countertrend_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_fak_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_trigger_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_average_fill_price\":0.54", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_EthUp4RevertFakPremarketBuysUpFromPremarketOrderBook()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_up_diff_4_revert_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-up-diff-premarket-revert-entry-market",
            conditionId: "eth-up-diff-premarket-revert-entry-condition",
            upAssetId: "eth-up-diff-premarket-revert-up",
            downAssetId: "eth-up-diff-premarket-revert-down"));
        AddWebSocketDiffResults(
            repository,
            "ETH",
            marketStartUtc.AddMinutes(-10),
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook(
                "eth-up-diff-premarket-revert-up",
                [new OrderBookLevel(0.45m, 100m)],
                [new OrderBookLevel(0.47m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-up-diff-premarket-revert-down",
                [new OrderBookLevel(0.52m, 100m)],
                [new OrderBookLevel(0.54m, 100m)],
                now,
                minOrderSize: 1m)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-up-diff-premarket-revert-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.47m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-up-diff-premarket-revert-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.47m, order.Price);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"utc_day_start_resolved_market_diff_countertrend_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_fak_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"trigger_side\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_counter_trigger_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_average_fill_price\":0.47", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffThresholdSkipsWhenCounterIsBelowThreshold()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_5_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Down");
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.True(
            string.Equals(run.SkipReason, "diff_counter_threshold_not_reached", StringComparison.Ordinal),
            run.SkipReason + ": " + run.SkipDiagnosticsJson);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"up_count\":4", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_count\":13", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_ShiftDiffThresholdBuysDownBeforeShiftTrigger()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(32);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(30);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_shift_diff_2_4_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "shift-diff-entry-market",
            conditionId: "shift-diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.True(
            result.EntriesPlaced == 1,
            string.Join("; ", repository.StrategyMarketPaperRuns.Select(run =>
                $"{run.MarketStartUtc:o}:{run.Status}:{run.SkipReason}:{run.SkipDiagnosticsJson}")));
        var run = repository.StrategyMarketPaperRuns.Single(item => item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(variant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.45m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        Assert.Contains("\"decision_source\":\"continuous_shift_diff_countertrend\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_mode\":\"continuous_shift_diff\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_count\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_trigger_abs\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_positive_adjustments\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"threshold\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Empty(repository.CryptoUpDown5mDiffSnapshots);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_ShiftDiffAppliesPositiveAdjustmentBeforeThreshold()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_shift_diff_2_4_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "shift-diff-entry-market",
            conditionId: "shift-diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.True(
            string.Equals(run.SkipReason, "diff_counter_threshold_not_reached", StringComparison.Ordinal),
            run.SkipReason + ": " + run.SkipDiagnosticsJson);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_mode\":\"continuous_shift_diff\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_count\":2", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_trigger_abs\":5", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_positive_adjustments\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"shift_diff_negative_adjustments\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
        Assert.Empty(repository.CryptoUpDown5mDiffSnapshots);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_AdjustedDiffThresholdUsesTrendZero()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(127);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(125);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_adjusted_diff_20_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            Enumerable.Repeat("Up", 24).ToArray());
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "adjusted-diff-entry-market",
            conditionId: "adjusted-diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_threshold_not_reached", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"decision_source\":\"continuous_trend_zero_adjusted_diff_countertrend\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_mode\":\"continuous_trend_zero\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"trend_zero_mode\":\"ema_24_slow_step_continuous\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"raw_diff\":24", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":24", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"trend_zero\":", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"adjusted_diff\":", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffStrategyLiveStakeCreatesPaperShadowAndLiveOrder()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(37);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(35);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_2_instant");
        var repository = new TestAppRepository();
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            entryMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var orderBooks = new[]
        {
            OrderBook("asset-up", bestBid: 0.54m, bestAsk: 0.55m, entryNow),
            OrderBook("asset-down", bestBid: 0.44m, bestAsk: 0.45m, entryNow)
        };
        var tradingClient = new CapturingTradingClient();
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            tradingClient: tradingClient,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.True(
            tradingClient.PlaceCalls == 1,
            string.Join(" | ", repository.LiveOrders.Select(order => order.Status + ": " + order.ValidationSummary)));
        Assert.NotNull(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, tradingClient.LastRequest.OrderType);
        Assert.False(tradingClient.LastRequest.PostOnly);
        Assert.Null(tradingClient.LastRequest.GtdExpirationUtc);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, order.CorrelationId);
        Assert.Contains("\"paper_live_shadow_test\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.45m, order.Price);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(order.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterSkipsMissingPreviousResultOnlyAfterFourMinutes()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_5_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closedMarkets = CreateClosedDiffMarkets(
            "BTC",
            startupMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up",
            "Up",
            "Down");
        var gammaClient = new FakeGammaClient([], closedMarkets);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: gammaClient,
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(0, gammaClient.ClosedMarketRequestCount);
        Assert.Empty(repository.PaperOrders);
        var waitingRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, waitingRun.Status);
        Assert.Null(waitingRun.SkipReason);
        Assert.Null(waitingRun.SkipDiagnosticsJson);

        timeProvider.UtcNow = startupMarketStartUtc.AddMinutes(4).AddSeconds(1);

        var timedOutResult = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, timedOutResult.EntriesPlaced);
        Assert.Equal(1, timedOutResult.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_previous_market_resolved_event_missing", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_initialized\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_mode\":\"utc_day_start\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_result_source\":\"ResolvedMarketLedger\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_target_market_result_received\":false", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"processed_market_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterRecordsSnapshotsForEachEnabledAsset()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var counterStartMarketStartUtc = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var timeProvider = new ManualTimeProvider(startupNow);
        var repository = new TestAppRepository();
        var enabledCodes = new[]
        {
            "btc_up_down_5m_up_diff_1_instant",
            "eth_up_down_5m_up_diff_1_instant",
            "sol_up_down_5m_up_diff_1_instant"
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, enabledCodes),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();

        var snapshots = repository.CryptoUpDown5mDiffSnapshots
            .OrderBy(snapshot => snapshot.AssetSymbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(3, snapshots.Length);
        Assert.Equal(["BTC", "ETH", "SOL"], snapshots.Select(snapshot => snapshot.AssetSymbol).ToArray());
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.CounterInitialized);
            Assert.Equal(startupNow, snapshot.SampledAtUtc);
            Assert.Equal(counterStartMarketStartUtc, snapshot.CounterStartMarketStartUtc);
            Assert.Equal(counterStartMarketStartUtc.AddMinutes(-5), snapshot.HighWaterMarketStartUtc);
            Assert.Equal(0, snapshot.UpCount);
            Assert.Equal(0, snapshot.DownCount);
            Assert.Equal(0, snapshot.DiffCount);
            Assert.Equal(0, snapshot.Diff);
            Assert.Equal(0, snapshot.ProcessedMarketCount);
        });
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterResetsAtUtcMidnight()
    {
        var previousDayMarketStartUtc = new DateTimeOffset(2026, 6, 8, 23, 55, 0, TimeSpan.Zero);
        var previousDayNow = previousDayMarketStartUtc.AddMinutes(3);
        var nextDayMarketStartUtc = new DateTimeOffset(2026, 6, 9, 0, 5, 0, TimeSpan.Zero);
        var nextDayNow = nextDayMarketStartUtc.AddMinutes(2);
        var nextDayCounterStartUtc = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(previousDayNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_10_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousDayMarketStartUtc,
            previousDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "previous-day-market",
            conditionId: "previous-day-condition"));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            previousDayMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up",
            "Up",
            "Up");
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();

        timeProvider.UtcNow = nextDayNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            nextDayMarketStartUtc,
            nextDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "next-day-market",
            conditionId: "next-day-condition"));
        AddWebSocketDiffResults(repository, "BTC", nextDayCounterStartUtc, "Up");

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == nextDayMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_threshold_not_reached", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_mode\":\"utc_day_start\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"processed_market_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);

        var latestSnapshot = repository.CryptoUpDown5mDiffSnapshots
            .Where(snapshot => string.Equals(snapshot.AssetSymbol, "BTC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(snapshot => snapshot.SampledAtUtc)
            .Last();
        Assert.Equal(nextDayCounterStartUtc, latestSnapshot.CounterStartMarketStartUtc);
        Assert.Equal(nextDayCounterStartUtc, latestSnapshot.HighWaterMarketStartUtc);
        Assert.Equal(1, latestSnapshot.UpCount);
        Assert.Equal(0, latestSnapshot.DownCount);
        Assert.Equal(1, latestSnapshot.Diff);
        Assert.Equal(1, latestSnapshot.ProcessedMarketCount);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_AdjustedDiffCounterDoesNotResetAtUtcMidnight()
    {
        var previousDayMarketStartUtc = new DateTimeOffset(2026, 6, 8, 23, 50, 0, TimeSpan.Zero);
        var previousDayNow = previousDayMarketStartUtc.AddMinutes(2);
        var nextDayMarketStartUtc = new DateTimeOffset(2026, 6, 9, 0, 5, 0, TimeSpan.Zero);
        var nextDayNow = nextDayMarketStartUtc.AddMinutes(2);
        var timeProvider = new ManualTimeProvider(previousDayNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_adjusted_diff_5_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            previousDayMarketStartUtc,
            previousDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "previous-day-adjusted-market",
            conditionId: "previous-day-adjusted-condition"));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();

        timeProvider.UtcNow = nextDayNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            nextDayMarketStartUtc,
            nextDayMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "next-day-adjusted-market",
            conditionId: "next-day-adjusted-condition"));
        AddWebSocketDiffResults(
            repository,
            "BTC",
            nextDayMarketStartUtc.AddMinutes(-5),
            "Up",
            "Up",
            "Up");

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == nextDayMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_threshold_not_reached", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_mode\":\"continuous_trend_zero\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_start_market_start_utc\":\"2026-06-08T23:50:00+00:00\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"processed_market_count\":3", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.CryptoUpDown5mDiffSnapshots);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterMidnightMarketDoesNotWaitForPreviousUtcDayResult()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        var nowUtc = marketStartUtc.AddMinutes(3);
        var timeProvider = new ManualTimeProvider(nowUtc);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_1_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "midnight-market",
            conditionId: "midnight-condition"));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_threshold_not_reached", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_target_market_result_received\":false", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"up_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"processed_market_count\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterHistoryFetchFailureSkipsWithBackoff()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(17);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(15);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_5_instant");
        var repository = new TestAppRepository();
        repository.ThrowOnGetCryptoUpDown5mWebSocketResolvedMarkets = true;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Empty(repository.PaperOrders);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_counter_history_fetch_backoff", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_history_fetch_retry_after_utc\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_initialized\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Equal(2, repository.ApiErrors.Count(error => error.Operation == "GetDiffCounterWebSocketResults"));
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterUsesWebSocketResultsInsteadOfGammaClosedHistory()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var entryNow = startupMarketStartUtc.AddMinutes(17);
        var entryMarketStartUtc = startupMarketStartUtc.AddMinutes(15);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_5_instant");
        var repository = new TestAppRepository();
        var market = CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m);
        repository.PolymarketGammaMarkets.Add(market);
        AddWebSocketDiffResults(repository, "BTC", entryMarketStartUtc.AddMinutes(-5), "Up");
        var gammaClient = new FakeGammaClient(
            [],
            [CreateClosedDiffMarket("BTC", entryMarketStartUtc.AddMinutes(-5), "Up")],
            rejectEqualClosedTimeRange: true);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: gammaClient,
            timeProvider: timeProvider);

        _ = await processor.ProcessDiffCounterDueEntriesAsync();
        timeProvider.UtcNow = entryNow;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "market-2",
            conditionId: "condition-2"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Empty(repository.ApiErrors);
        Assert.Equal(0, gammaClient.ClosedMarketRequestCount);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal("diff_counter_threshold_not_reached", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"counter_target_market_result_received\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
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
    public async Task ProcessAsync_PreviousScoreCounterTrendBuysUpAfterPreviousDownBiasAtFiftyCents()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item =>
            item.Code == "btc_up_down_5m_prev_score_countertrend_50");
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
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_limit_price\":0.50", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakBuysDownAfterPreviousUpBias()
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
                binancePriceUsd: 101m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrendFakVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(PreviousScoreCounterTrendFakVariant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_btc_market_time_weighted_winsor_score_countertrend_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"countertrend\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_fak_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_filled_notional_usd\":1", order.RawDecisionJson, StringComparison.Ordinal);
        using var rawDecision = JsonDocument.Parse(order.RawDecisionJson);
        Assert.Equal(100m, rawDecision.RootElement.GetProperty("previous_score_bps").GetDecimal());
        Assert.Equal(100m, rawDecision.RootElement.GetProperty("previous_score_abs_bps").GetDecimal());
        Assert.Equal(100m, rawDecision.RootElement.GetProperty("selected_signal_bps").GetDecimal());
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakBuysUpAfterPreviousDownBias()
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
                "previous-down-score-market",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 99m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrendFakVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_type\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakBuysDownAfterPreviousEthUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "eth-updown-5m-" + suffix,
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-current-score-market",
            conditionId: "eth-current-score-condition",
            upAssetId: "eth-score-asset-up",
            downAssetId: "eth-score-asset-down"));
        for (var index = 0; index < 10; index++)
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-previous-up-score-market",
                "eth-previous-up-score-condition",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 2020m,
                startPriceUsd: 2000m,
                upAssetId: "eth-previous-score-up",
                downAssetId: "eth-previous-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("eth-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("eth-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            EthPreviousScoreCounterTrendFakVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthPreviousScoreCounterTrendFakVariant.Id, run.StrategyId);
        Assert.Equal("eth-score-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("eth-score-asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_eth_market_time_weighted_winsor_score_countertrend_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakRevertBuysUpAfterPreviousEthUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "eth-updown-5m-" + suffix,
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - revert test",
            marketId: "eth-current-revert-score-market",
            conditionId: "eth-current-revert-score-condition",
            upAssetId: "eth-revert-score-asset-up",
            downAssetId: "eth-revert-score-asset-down"));
        for (var index = 0; index < 10; index++)
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-previous-up-revert-score-market",
                "eth-previous-up-revert-score-condition",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 2020m,
                startPriceUsd: 2000m,
                upAssetId: "eth-previous-revert-score-up",
                downAssetId: "eth-previous-revert-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("eth-revert-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("eth-revert-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            EthPreviousScoreCounterTrendFakRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthPreviousScoreCounterTrendFakRevertVariant.Id, run.StrategyId);
        Assert.Equal("eth-revert-score-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("eth-revert-score-asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_eth_market_time_weighted_winsor_score_same_direction_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakBuysUpAfterPreviousSolDownBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "sol-updown-5m-" + suffix,
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-current-score-market",
            conditionId: "sol-current-score-condition",
            upAssetId: "sol-score-asset-up",
            downAssetId: "sol-score-asset-down"));
        for (var index = 0; index < 10; index++)
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-previous-down-score-market",
                "sol-previous-down-score-condition",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 149m,
                startPriceUsd: 150m,
                upAssetId: "sol-previous-score-up",
                downAssetId: "sol-previous-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("sol-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("sol-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            SolPreviousScoreCounterTrendFakVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolPreviousScoreCounterTrendFakVariant.Id, run.StrategyId);
        Assert.Equal("sol-score-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("sol-score-asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_sol_market_time_weighted_winsor_score_countertrend_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakRevertBuysDownAfterPreviousSolDownBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now;
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "sol-updown-5m-revert-" + suffix,
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - revert test",
            marketId: "sol-current-revert-score-market",
            conditionId: "sol-current-revert-score-condition",
            upAssetId: "sol-revert-score-asset-up",
            downAssetId: "sol-revert-score-asset-down"));
        for (var index = 0; index < 10; index++)
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-previous-down-revert-score-market",
                "sol-previous-down-revert-score-condition",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 149m,
                startPriceUsd: 150m,
                upAssetId: "sol-previous-revert-score-up",
                downAssetId: "sol-previous-revert-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("sol-revert-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("sol-revert-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            SolPreviousScoreCounterTrendFakRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolPreviousScoreCounterTrendFakRevertVariant.Id, run.StrategyId);
        Assert.Equal("sol-revert-score-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("sol-revert-score-asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_sol_market_time_weighted_winsor_score_same_direction_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakPremarketBuysDownFromSyntheticUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddBtcOddsTick(
                repository,
                "previous-score-premarket-carryover",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 100m : 101m,
                startPriceUsd: 200m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddBtcOddsTick(
                repository,
                "current-score-premarket",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 101m,
                startPriceUsd: 200m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            PreviousScoreCounterTrendFakPremarketVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(PreviousScoreCounterTrendFakPremarketVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_btc_5_5m_time_weighted_winsor_score_countertrend_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"countertrend_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"entry_delay_seconds\":-30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakPremarketRevertBuysUpFromSyntheticUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddBtcOddsTick(
                repository,
                "previous-score-premarket-revert-carryover",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 100m : 101m,
                startPriceUsd: 200m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddBtcOddsTick(
                repository,
                "current-score-premarket-revert",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 101m,
                startPriceUsd: 200m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            PreviousScoreCounterTrendFakPremarketRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(PreviousScoreCounterTrendFakPremarketRevertVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_btc_5_5m_time_weighted_winsor_score_same_direction_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_premarket_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"entry_delay_seconds\":-30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakPremarketBuysDownFromSyntheticEthUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "eth-updown-5m-" + suffix,
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - premarket score test",
            marketId: "eth-current-score-premarket-target",
            conditionId: "eth-current-score-premarket-condition",
            upAssetId: "eth-score-asset-up",
            downAssetId: "eth-score-asset-down"));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-previous-score-premarket-carryover",
                "eth-previous-score-premarket-condition",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 2000m : 2020m,
                startPriceUsd: 3000m,
                upAssetId: "eth-previous-score-up",
                downAssetId: "eth-previous-score-down");
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-current-score-premarket",
                "eth-current-score-premarket-condition",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 2020m,
                startPriceUsd: 3000m,
                upAssetId: "eth-current-score-up",
                downAssetId: "eth-current-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("eth-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("eth-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            EthPreviousScoreCounterTrendFakPremarketVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthPreviousScoreCounterTrendFakPremarketVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("eth-score-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("eth-score-asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_eth_5_5m_time_weighted_winsor_score_countertrend_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"countertrend_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":2000", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakPremarketRevertBuysUpFromSyntheticEthUpBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "eth-updown-5m-revert-" + suffix,
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - premarket revert score test",
            marketId: "eth-current-score-premarket-revert-target",
            conditionId: "eth-current-score-premarket-revert-condition",
            upAssetId: "eth-score-revert-asset-up",
            downAssetId: "eth-score-revert-asset-down"));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-previous-score-premarket-revert-carryover",
                "eth-previous-score-premarket-revert-condition",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 2000m : 2020m,
                startPriceUsd: 3000m,
                upAssetId: "eth-previous-score-revert-up",
                downAssetId: "eth-previous-score-revert-down");
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddCryptoOddsTick(
                repository,
                "ETH",
                "eth-current-score-premarket-revert",
                "eth-current-score-premarket-revert-condition",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 2020m,
                startPriceUsd: 3000m,
                upAssetId: "eth-current-score-revert-up",
                downAssetId: "eth-current-score-revert-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("eth-score-revert-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("eth-score-revert-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            EthPreviousScoreCounterTrendFakPremarketRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthPreviousScoreCounterTrendFakPremarketRevertVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("eth-score-revert-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("eth-score-revert-asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_eth_5_5m_time_weighted_winsor_score_same_direction_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_premarket_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"entry_delay_seconds\":-30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":2000", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakPremarketBuysUpFromSyntheticSolDownBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "sol-updown-5m-" + suffix,
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - premarket score test",
            marketId: "sol-current-score-premarket-target",
            conditionId: "sol-current-score-premarket-condition",
            upAssetId: "sol-score-asset-up",
            downAssetId: "sol-score-asset-down"));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-previous-score-premarket-carryover",
                "sol-previous-score-premarket-condition",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 150m : 149m,
                startPriceUsd: 1m,
                upAssetId: "sol-previous-score-up",
                downAssetId: "sol-previous-score-down");
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-current-score-premarket",
                "sol-current-score-premarket-condition",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 149m,
                startPriceUsd: 1m,
                upAssetId: "sol-current-score-up",
                downAssetId: "sol-current-score-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("sol-score-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("sol-score-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            SolPreviousScoreCounterTrendFakPremarketVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolPreviousScoreCounterTrendFakPremarketVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("sol-score-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("sol-score-asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_sol_5_5m_time_weighted_winsor_score_countertrend_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"countertrend_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":150", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_CryptoPreviousScoreCounterTrendFakPremarketRevertBuysDownFromSyntheticSolDownBias()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(29);
        var currentScoredMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var previousScoredMarketStartUtc = marketStartUtc.AddMinutes(-10);
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: "sol-updown-5m-revert-" + suffix,
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - premarket revert score test",
            marketId: "sol-current-score-premarket-revert-target",
            conditionId: "sol-current-score-premarket-revert-condition",
            upAssetId: "sol-score-revert-asset-up",
            downAssetId: "sol-score-revert-asset-down"));
        foreach (var sampleOffsetSeconds in new[] { 240, 250, 260, 270, 280, 290 })
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-previous-score-premarket-revert-carryover",
                "sol-previous-score-premarket-revert-condition",
                previousScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: sampleOffsetSeconds == 240 ? 150m : 149m,
                startPriceUsd: 1m,
                upAssetId: "sol-previous-score-revert-up",
                downAssetId: "sol-previous-score-revert-down");
        }

        for (var sampleOffsetSeconds = 0; sampleOffsetSeconds <= 270; sampleOffsetSeconds += 30)
        {
            AddCryptoOddsTick(
                repository,
                "SOL",
                "sol-current-score-premarket-revert",
                "sol-current-score-premarket-revert-condition",
                currentScoredMarketStartUtc,
                sampleOffsetSeconds,
                binancePriceUsd: 149m,
                startPriceUsd: 1m,
                upAssetId: "sol-current-score-revert-up",
                downAssetId: "sol-current-score-revert-down");
        }

        var processor = CreateProcessorCore(
            repository,
            [],
            [
                OrderBook("sol-score-revert-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
                OrderBook("sol-score-revert-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
            ],
            SolPreviousScoreCounterTrendFakPremarketRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolPreviousScoreCounterTrendFakPremarketRevertVariant.Id, run.StrategyId);
        Assert.Equal(marketStartUtc.AddSeconds(-30), run.EntryDueAtUtc);
        Assert.Equal("sol-score-revert-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("sol-score-revert-asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_sol_5_5m_time_weighted_winsor_score_same_direction_premarket_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction_premarket_5_5m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_countertrend_premarket_enabled\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_premarket_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"entry_delay_seconds\":-30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_start_price_usd\":150", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_raw_sample_count\":16", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_previous_market_seconds\":60", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_premarket_current_market_seconds\":270", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakRevertBuysDownAfterPreviousDownBias()
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
                "previous-down-score-market",
                previousMarketStartUtc,
                index * 30,
                binancePriceUsd: 99m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrendFakRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(PreviousScoreCounterTrendFakRevertVariant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"decision_source\":\"previous_btc_market_time_weighted_winsor_score_same_direction_fak\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_direction_mode\":\"previous_bias_same_direction\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_score_same_direction_revert_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_bias\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"fak_stats_probe_worst_price\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_filled_notional_usd\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
    }

    [Fact]
    public async Task ProcessAsync_PreviousScoreCounterTrendFakRevertBuysUpAfterPreviousUpBias()
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
                binancePriceUsd: 101m,
                startPriceUsd: 100m,
                upPriceProxy: 0.50m,
                downPriceProxy: 0.50m);
        }

        var processor = CreateProcessor(repository, [], PreviousScoreCounterTrendFakRevertVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.36m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"previous_bias\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_type\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Single(repository.PaperFills);
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
    public async Task ProcessAsync_PausedStrategySkipsNewPaperEntry()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[AlwaysDownVariant.Id] = StrategyRuntimeSettings.Default(AlwaysDownVariant.Id) with
        {
            Paused = true,
            PausedUntilUtc = now.AddHours(1)
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessor(repository, [], AlwaysDownVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        Assert.Empty(repository.LiveOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("strategy_paused", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"strategy_paused\":true", run.SkipDiagnosticsJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerBaselinesFirstBookWithoutOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-5),
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerPlacesPostOnlyOrderWhenBestAskMakesNewHigh()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.EnforceStrategyRunPaperOrderForeignKey = true;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(TradeSide.Buy, order.Side);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.44m, order.Price);
        Assert.True(order.Price < 0.45m);
        Assert.Equal(marketEndUtc, order.ExpiresAtUtc);
        Assert.Equal("btc_updown5m_maker_post_only", order.ExecutionSource);
        Assert.Contains("\"post_only\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"maker_limit_price\":0.44", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"maker_decision_interval_seconds\":30", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"maker_decision_slot\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"pricing_mode\":\"paper_gtd_limit\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":0", order.RawDecisionJson, StringComparison.Ordinal);

        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("BTC Up/Down 5m Maker", run.Category);
        Assert.Equal(order.Id, run.PaperOrderId);
        Assert.Equal(order.NotionalUsd, run.StakeUsd);
        Assert.Equal(0.44m, run.EntryPrice);
        Assert.Contains(":maker:up:0.45", run.MarketId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpMaker50PlacesFixedHalfOrderOnlyAboveHalfBestAsk()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.46m, 100m)], [new OrderBookLevel(0.48m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.50m, 100m)], [new OrderBookLevel(0.52m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMaker50Variant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.48m, 100m)], [new OrderBookLevel(0.50m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.48m, 100m)], [new OrderBookLevel(0.50m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var atHalf = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-61));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.49m, 100m)], [new OrderBookLevel(0.51m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.47m, 100m)], [new OrderBookLevel(0.49m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var aboveHalf = await processor.ProcessAsync();

        Assert.Equal(0, atHalf.EntriesPlaced);
        Assert.Equal(0, atHalf.RunsSkipped);
        Assert.Equal(1, aboveHalf.EntriesPlaced);
        Assert.Equal(0, aboveHalf.RunsSkipped);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(UpMaker50Variant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.True(order.Price < 0.51m);
        var rawDecisionJson = order.RawDecisionJson ?? throw new InvalidOperationException("Maker order raw decision JSON is missing.");
        using var rawDecision = JsonDocument.Parse(rawDecisionJson);
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_limit_price").GetDecimal());
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_fixed_limit_price").GetDecimal());
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_min_best_ask_exclusive").GetDecimal());
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(0.50m, run.EntryPrice);
        Assert.Contains(":maker:up:0.51", run.MarketId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_DownMaker50UsesDownBookAndFixedHalfPrice()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.46m, 100m)], [new OrderBookLevel(0.48m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.50m, 100m)], [new OrderBookLevel(0.52m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, DownMaker50Variant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.44m, 100m)], [new OrderBookLevel(0.46m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.52m, 100m)], [new OrderBookLevel(0.54m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(DownMaker50Variant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.50m, order.Price);
        Assert.True(order.Price < 0.54m);
        var rawDecisionJson = order.RawDecisionJson ?? throw new InvalidOperationException("Maker order raw decision JSON is missing.");
        using var rawDecision = JsonDocument.Parse(rawDecisionJson);
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_limit_price").GetDecimal());
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_fixed_limit_price").GetDecimal());
        Assert.Equal(0.50m, rawDecision.RootElement.GetProperty("maker_min_best_ask_exclusive").GetDecimal());
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Contains(":maker:down:0.54", run.MarketId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerDoesNotOrderBeforeNextThirtySecondSlot()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-5),
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerDoesNotRaiseHighWaterBetweenThirtySecondSlots()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-15));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.53m, 100m)], [new OrderBookLevel(0.55m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.45m, 100m)], [new OrderBookLevel(0.47m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var beforeFirstSlot = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.43m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.57m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var firstSlot = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-45));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var betweenSlots = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-61));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.42m, 100m)], [new OrderBookLevel(0.44m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.56m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var secondSlot = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-91));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.48m, 100m)], [new OrderBookLevel(0.50m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.50m, 100m)], [new OrderBookLevel(0.52m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var thirdSlot = await processor.ProcessAsync();

        Assert.Equal(0, beforeFirstSlot.EntriesPlaced);
        Assert.Equal(1, firstSlot.EntriesPlaced);
        Assert.Equal(0, betweenSlots.EntriesPlaced);
        Assert.Equal(0, secondSlot.EntriesPlaced);
        Assert.Equal(1, thirdSlot.EntriesPlaced);
        Assert.Equal([0.44m, 0.49m], repository.PaperOrders.Select(order => order.Price).ToArray());
        Assert.Contains(
            "\"previous_max_best_ask\":0.42",
            repository.PaperOrders[0].RawDecisionJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"previous_max_best_ask\":0.45",
            repository.PaperOrders[1].RawDecisionJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerWaitsForBestAskToExceedPriorHighAfterFalling()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var firstRise = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-61));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.37m, 100m)], [new OrderBookLevel(0.40m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.60m, 100m)], [new OrderBookLevel(0.63m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var fall = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-91));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.39m, 100m)], [new OrderBookLevel(0.43m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.57m, 100m)], [new OrderBookLevel(0.60m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);
        var belowPriorHighRise = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-121));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.41m, 100m)], [new OrderBookLevel(0.46m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.54m, 100m)], [new OrderBookLevel(0.59m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var newHighRise = await processor.ProcessAsync();

        Assert.Equal(1, firstRise.EntriesPlaced);
        Assert.Equal(0, fall.EntriesPlaced);
        Assert.Equal(0, belowPriorHighRise.EntriesPlaced);
        Assert.Equal(1, newHighRise.EntriesPlaced);
        Assert.Equal(2, repository.PaperOrders.Count);
        Assert.Equal([0.44m, 0.45m], repository.PaperOrders.Select(order => order.Price).ToArray());
        Assert.All(repository.PaperOrders, order => Assert.Equal("btc_updown5m_maker_post_only", order.ExecutionSource));
        Assert.Equal(2, repository.StrategyMarketPaperRuns.Count);
        Assert.Contains(repository.StrategyMarketPaperRuns, run => run.MarketId.Contains(":maker:up:0.45", StringComparison.Ordinal));
        Assert.Contains(repository.StrategyMarketPaperRuns, run => run.MarketId.Contains(":maker:up:0.46", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_UpMakerDoesNotOrderWhenBestAskFalls()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-5),
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.39m, 100m)], [new OrderBookLevel(0.41m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.59m, 100m)], [new OrderBookLevel(0.61m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_UpMakerIgnoresOppositeOutcomeOpenOrderFromOtherStrategy()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            BinanceDelayed30Variant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-down",
            "condition-1",
            "Down",
            0.50m,
            5m,
            2.50m,
            now.AddSeconds(-1),
            marketEndUtc,
            StrategyId: BinanceDelayed30Variant.Id));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var processor = CreateMakerProcessor(repository, clobClient, UpMakerVariant.Code);

        await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(2, repository.PaperOrders.Count);
        var makerOrder = repository.PaperOrders.Single(order => order.StrategyId == UpMakerVariant.Id);
        Assert.Equal("asset-up", makerOrder.AssetId);
        Assert.Equal("Up", makerOrder.Outcome);
        Assert.Equal(0.44m, makerOrder.Price);
        Assert.DoesNotContain(
            SignalReasonCodes.OppositeOutcomeOpenOrder,
            Assert.Single(repository.StrategyMarketPaperRuns).SkipDiagnosticsJson ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MakerVariantRunsInLiveModeAsPaperOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveMakerProcessor(
            repository,
            tradingClient,
            clobClient,
            UpMakerVariant.Code);

        var baseline = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, baseline.MarketsObserved);
        Assert.Equal(0, baseline.EntriesPlaced);
        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.LiveOrders);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.44m, order.Price);
        Assert.Equal("btc_updown5m_maker_post_only", order.ExecutionSource);
        Assert.Contains("\"post_only\":true", order.RawDecisionJson, StringComparison.Ordinal);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(order.Id, run.PaperOrderId);
    }

    [Fact]
    public async Task ProcessAsync_MakerVariantWithLiveStakesCreatesFakPaperShadowAndLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStartUtc = now.AddSeconds(-5);
        var marketEndUtc = now.AddMinutes(5);
        var repository = new TestAppRepository();
        repository.StrategySettings[UpMakerVariant.Id] = StrategyRuntimeSettings.Default(UpMakerVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketEndUtc,
            upPrice: 0.50m,
            downPrice: 0.50m,
            orderMinSize: 5m));
        var clobClient = new MutableFakeClobClient([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.42m, 100m)], now, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.58m, 100m)], [new OrderBookLevel(0.60m, 100m)], now, 5m, 0.01m)
        ]);
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveMakerProcessor(
            repository,
            tradingClient,
            clobClient,
            UpMakerVariant.Code);

        var baseline = await processor.ProcessAsync();
        await Task.Delay(75);
        SetFirstMarketWindowStart(repository, DateTimeOffset.UtcNow.AddSeconds(-31));
        clobClient.SetOrderBooks([
            OrderBook("asset-up", [new OrderBookLevel(0.40m, 100m)], [new OrderBookLevel(0.45m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m),
            OrderBook("asset-down", [new OrderBookLevel(0.55m, 100m)], [new OrderBookLevel(0.58m, 100m)], DateTimeOffset.UtcNow, 5m, 0.01m)
        ]);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, baseline.MarketsObserved);
        Assert.Equal(0, baseline.EntriesPlaced);
        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, tradingClient.LastRequest.OrderType);
        Assert.False(tradingClient.LastRequest.PostOnly);
        Assert.Null(tradingClient.LastRequest.GtdExpirationUtc);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.Equal(0.44m, liveOrder.Price);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal("paper_live_shadow_test", order.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, order.CorrelationId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.44m, order.Price);
        Assert.Contains("\"paper_live_shadow_test\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"post_only\":false", order.RawDecisionJson, StringComparison.Ordinal);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(order.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.False(decision.PostOnly);
        Assert.Equal("live_submitted", decision.Status);
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
        Assert.Contains("\"btc_min_move_from_start_bps\":20", run.SkipDiagnosticsJson, StringComparison.Ordinal);
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
            new FakeBtcUsdReferencePriceClient(100.21m),
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
        Assert.Contains("\"btc_move_from_start_bps\":21", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_start_bps\":21", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":20", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps5ThresholdUsesRescaledMoveThreshold()
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
            new FakeBtcUsdReferencePriceClient(100.06m),
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
        Assert.Contains("\"btc_move_from_start_bps\":6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_abs_move_from_start_bps\":6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpBpsInstantEntersOnlyWhenPreviousMovePointsDown()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousMarketId = GetCloseBookMarketId(previousStart);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Down");
        AddBtcOddsTick(repository, previousMarketId, previousStart, 0, 100m, 100m, 0.50m, 0.50m);
        AddBtcOddsTick(repository, previousMarketId, previousStart, 299, 99.98m, 100m, 0.50m, 0.50m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpBps2InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == UpBps2InstantVariant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.41m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(UpBps2InstantVariant.Id, order.StrategyId);
        Assert.Equal("asset-up", order.AssetId);
        Assert.Equal(0.41m, order.Price);
        Assert.Contains("\"fixed_outcome_previous_result_bps_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_btc_cumulative_abs_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_UpBpsInstantSkipsWhenPreviousMovePointsUp()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousMarketId = GetCloseBookMarketId(previousStart);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        AddBtcOddsTick(repository, previousMarketId, previousStart, 0, 100m, 100m, 0.50m, 0.50m);
        AddBtcOddsTick(repository, previousMarketId, previousStart, 299, 100.02m, 100m, 0.50m, 0.50m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpBps2InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.True(result.RunsSkipped >= 1);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == UpBps2InstantVariant.Id &&
            item.MarketId == "market-1");
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("btc_previous_market_move_fixed_outcome_mismatch", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"fixed_outcome_previous_result_bps_enabled\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Up\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":null", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_btc_cumulative_abs_move_from_start_bps\":2", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_DownBpsInstantEntersAboveDefaultInstantPriceCapWhenPreviousMovePointsUp()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousMarketId = GetCloseBookMarketId(previousStart);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        AddBtcOddsTick(repository, previousMarketId, previousStart, 0, 100m, 100m, 0.50m, 0.50m);
        AddBtcOddsTick(repository, previousMarketId, previousStart, 299, 100.02m, 100m, 0.50m, 0.50m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "asset-up",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.39m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "asset-down",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.66m, 20m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [DownBps2InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == DownBps2InstantVariant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.66m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(DownBps2InstantVariant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.66m, order.Price);
        Assert.Contains("\"fixed_outcome_previous_result_bps_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_btc_cumulative_abs_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":1.00", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.66", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain(SignalReasonCodes.InstantPriceAboveMax, order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthSkipConsecutiveResultsUsesCryptoPreviousCloseBookMarkets()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[EthSkip3Variant.Id] = StrategyRuntimeSettings.Default(EthSkip3Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", now, "Up", "Up", "Up");
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook("eth-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("eth-asset-down", bestBid: 0.49m, bestAsk: 0.51m, now)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [EthSkip3Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == EthSkip3Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("eth-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);
        Assert.Equal(2.50m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(EthSkip3Variant.Id, order.StrategyId);
        Assert.Equal("eth-asset-down", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"ETHUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"winning_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthSkipConsecutiveResultsSkipsTemporaryUpEntriesInPaper()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[EthSkip3Variant.Id] = StrategyRuntimeSettings.Default(EthSkip3Variant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", now, "Down", "Down", "Down");
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook("eth-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("eth-asset-down", bestBid: 0.49m, bestAsk: 0.51m, now)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [EthSkip3Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.True(result.RunsSkipped >= 1);
        Assert.Empty(repository.PaperOrders);
        Assert.Empty(repository.LiveOrders);

        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == EthSkip3Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Skipped &&
            string.Equals(item.SkipReason, "eth_skip_up_direction_temporarily_disabled", StringComparison.Ordinal));
        Assert.Null(run.SelectedOutcome);
        Assert.Equal("eth_skip_up_direction_temporarily_disabled", run.SkipReason);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Up\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"skip_reason\":\"eth_skip_up_direction_temporarily_disabled\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthUpBpsInstantUsesCryptoStreakMoveAndFixedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", now, "Down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 299,
            binancePriceUsd: 3_199.36m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-asset-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "eth-asset-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [EthUpBps2InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == EthUpBps2InstantVariant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("eth-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.41m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(EthUpBps2InstantVariant.Id, order.StrategyId);
        Assert.Equal("eth-asset-up", order.AssetId);
        Assert.Equal(0.41m, order.Price);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence_previous_crypto_move_threshold\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"ETHUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_reference_asset_cumulative_abs_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthDown9FakPremarketUsesReferenceAverageAndCurrentOrderBook()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_reference_average_bps_9_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-next-market",
            conditionId: "eth-next-condition",
            upAssetId: "eth-next-up",
            downAssetId: "eth-next-down"));
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("ETH", 3_196m);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("ETH", 3_150m, 3_200m, 3_175m, 3_180m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-next-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-next-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            cryptoReferencePriceClient: cryptoPriceClient,
            timeProvider: new ManualTimeProvider(now),
            cryptoReferencePriceAverageProvider: averageProvider);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-next-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.41m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-next-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(0.41m, order.Price);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"decision_source\":\"reference_price_max_average_bps_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_source\":\"crypto_reference_price_average_cache\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_reference_average_price_usd\":3200", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_reference_average_window\":\"12h\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"current_price_usd\":3196", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_trigger_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_target_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_move_from_middle_bps\":-12.5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_reference_average_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthDown40FakPremarketMinus10UsesEndMinus10Reference()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(10);
        var previousStartUtc = marketStartUtc.AddMinutes(-5);
        var previousSuffix = previousStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_bps_40_fak_premarket_m10s");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-next-market-minus10",
            conditionId: "eth-next-condition-minus10",
            upAssetId: "eth-next-up-minus10",
            downAssetId: "eth-next-down-minus10"));
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-previous-market-" + previousSuffix,
            "eth-previous-condition-" + previousSuffix,
            previousStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-previous-up-" + previousSuffix,
            downAssetId: "eth-previous-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-previous-market-" + previousSuffix,
            "eth-previous-condition-" + previousSuffix,
            previousStartUtc,
            sampleOffsetSeconds: 290,
            binancePriceUsd: 3_213m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-previous-up-" + previousSuffix,
            downAssetId: "eth-previous-down-" + previousSuffix);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-next-up-minus10",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-next-down-minus10",
                [new OrderBookLevel(0.57m, 100m)],
                [new OrderBookLevel(0.61m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal("eth-next-down-minus10", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.61m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("eth-next-down-minus10", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(0.61m, order.Price);
        Assert.Contains("\"premarket_previous_result_sample_seconds_before_end\":10", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_previous_result_source\":\"ReferencePricePremarketEndMinus10\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_reference_asset_move_from_start_bps\":40.625", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthDown9FakLiveStakeSubmitsFakMarketBuyAmount()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_bps_9_fak");
        var repository = new TestAppRepository();
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 1m,
            LiveLostCoeff = 10m,
            LiveLostCounter = 3,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 1m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", now, "Up");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 299,
            binancePriceUsd: 3_202.88m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-asset-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now),
            OrderBook(
                "eth-asset-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now)
        ];
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xfak",
                "matched",
                null,
                "0.80",
                "1.25",
                """{"status":"matched","makingAmount":"0.80","takingAmount":"1.25"}""",
                "{}")
        };
        var processor = CreateLiveProcessorWithCryptoReference(
            repository,
            tradingClient,
            new FakeCryptoReferencePriceClient(),
            orderBooks,
            closeBookOrderBooks,
            variant.Code);

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var request = Assert.IsType<ClobV2OrderRequest>(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);
        Assert.Equal(0.99m, request.Price);
        Assert.Equal(1m, request.MarketBuyAmountUsd);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("eth-asset-down", liveOrder.AssetId);
        Assert.Equal("Down", liveOrder.Outcome);
        Assert.Equal(0.99m, liveOrder.Price);
        Assert.Equal(1m, liveOrder.NotionalUsd);
        Assert.Equal(1.25m, liveOrder.SizeShares);
        Assert.Equal(1.25m, liveOrder.FilledSize);
        Assert.Equal(0m, liveOrder.RemainingSize);
        Assert.Equal(0.64m, liveOrder.AverageFillPrice);
        Assert.Equal(0.80m, liveOrder.FilledNotionalUsd);
        Assert.Equal(0.80m, liveOrder.CostBasisUsd);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Contains("\"order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"live_order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("FAK", decision.OrderType);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
    }

    [Fact]
    public async Task ProcessAsync_EthDown9FakPremarketLiveStakeSubmitsBeforeMarketStart()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_reference_average_bps_9_fak_premarket");
        var repository = new TestAppRepository();
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 1m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 1m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-premarket-live-market",
            conditionId: "eth-premarket-live-condition",
            upAssetId: "eth-premarket-live-up",
            downAssetId: "eth-premarket-live-down"));
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("ETH", 3_196m);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("ETH", 3_150m, 3_200m, 3_175m, 3_180m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-premarket-live-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-premarket-live-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xfak-premarket",
                "matched",
                null,
                "0.80",
                "1.25",
                """{"status":"matched","makingAmount":"0.80","takingAmount":"1.25"}""",
                "{}")
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            tradingClient: tradingClient,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            liveTradingOptions: new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED",
                MaxOrderNotionalUsd = 10m,
                BlockOnGeoblockCheckFailure = false
            },
            geoClient: new ThrowingGeoClient(),
            cryptoReferencePriceClient: cryptoPriceClient,
            timeProvider: new ManualTimeProvider(now),
            cryptoReferencePriceAverageProvider: averageProvider);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var request = Assert.IsType<ClobV2OrderRequest>(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal("eth-premarket-live-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.DoesNotContain("has not started", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Geoblock check failed", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("live_submitted", decision.Status);
        var warning = Assert.Single(repository.LiveTradingEvents, item => item.Action == "GeoblockCheck");
        Assert.Equal("Warning", warning.Status);
        Assert.Contains("geoblock endpoint unavailable", warning.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_EthDown9FakLiveStakeRejectsZeroFill()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_down_bps_9_fak");
        var repository = new TestAppRepository();
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 1m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 1m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", now, "Up");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-close-market-" + previousSuffix,
            "eth-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 299,
            binancePriceUsd: 3_202.88m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-asset-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now),
            OrderBook(
                "eth-asset-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now)
        ];
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xfak-zero",
                "matched",
                null,
                null,
                null,
                """{"status":"matched"}""",
                "{}")
        };
        var processor = CreateLiveProcessorWithCryptoReference(
            repository,
            tradingClient,
            new FakeCryptoReferencePriceClient(),
            orderBooks,
            closeBookOrderBooks,
            variant.Code);

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Rejected, liveOrder.Status);
        Assert.Equal(0m, liveOrder.FilledSize);
        Assert.Equal(0m, liveOrder.RemainingSize);
        Assert.Null(liveOrder.AverageFillPrice);
        Assert.Equal(0m, liveOrder.FilledNotionalUsd);
        Assert.Equal(0m, liveOrder.CostBasisUsd);
        Assert.Equal("FAK order reported no immediate fill.", liveOrder.ValidationSummary);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("live_rejected", decision.Status);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
    }

    [Fact]
    public async Task ProcessAsync_SolDownBpsInstantUsesCryptoStreakMoveAndFixedOutcome()
    {
        var now = DateTimeOffset.UtcNow;
        var previousStart = now.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "SOL", now, "Up");
        AddCryptoOddsTick(
            repository,
            "SOL",
            "sol-close-market-" + previousSuffix,
            "sol-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 150m,
            startPriceUsd: 150m,
            upAssetId: "sol-close-up-" + previousSuffix,
            downAssetId: "sol-close-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "SOL",
            "sol-close-market-" + previousSuffix,
            "sol-close-condition-" + previousSuffix,
            previousStart,
            sampleOffsetSeconds: 299,
            binancePriceUsd: 150.03m,
            startPriceUsd: 150m,
            upAssetId: "sol-close-up-" + previousSuffix,
            downAssetId: "sol-close-down-" + previousSuffix);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "sol-asset-up",
                [new OrderBookLevel(0.37m, 100m)],
                [new OrderBookLevel(0.39m, 100m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "sol-asset-down",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.64m, 20m)],
                now,
                minOrderSize: 5m)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            closeBookOrderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [SolDownBps2InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == SolDownBps2InstantVariant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("sol-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(SolDownBps2InstantVariant.Id, order.StrategyId);
        Assert.Equal("sol-asset-down", order.AssetId);
        Assert.Equal(0.64m, order.Price);
        Assert.Contains("\"decision_source\":\"clob_close_book_price_evidence_previous_crypto_move_threshold\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"SOLUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"previous_reference_asset_cumulative_abs_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthBinanceBpsThresholdEntersWhenMoveReachesThreshold()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        AddCryptoOddsStartTick(
            repository,
            "ETH",
            "eth-market-1",
            "eth-condition-1",
            now,
            startPriceUsd: 3_200m,
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down");
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("ETH", 3_201m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook("eth-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("eth-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [EthBinanceBps2Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient: cryptoPriceClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthBinanceBps2Variant.Id, run.StrategyId);
        Assert.Equal("eth-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(EthBinanceBps2Variant.Id, order.StrategyId);
        Assert.Equal("eth-asset-up", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"ETHUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_current_price_usd\":3201", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_start_price_usd\":3200", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_move_from_start_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_EthMiddleBpsUsesCryptoReferenceMean()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            slug: $"eth-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetSamples("ETH", 3_200m);
        cryptoPriceClient.SetPrice("ETH", 3_208m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook("eth-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("eth-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
        ];
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [EthMiddleBps20Variant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient: cryptoPriceClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(EthMiddleBps20Variant.Id, run.StrategyId);
        Assert.Equal("eth-asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.50m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(EthMiddleBps20Variant.Id, order.StrategyId);
        Assert.Equal("eth-asset-down", order.AssetId);
        Assert.Equal(0.50m, order.Price);
        Assert.Contains("\"reference_asset_symbol\":\"ETH\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"ETHUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_current_price_usd\":3208", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_arithmetic_mean_usd\":3200", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_move_from_mean_bps\":25", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_move_from_mean_bps\":20", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SolMiddleRevertBpsInstantUsesCryptoMeanAndExecutableAskDepth()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetSamples("SOL", 150m);
        cryptoPriceClient.SetPrice("SOL", 151.6m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "sol-asset-up",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.64m, 20m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "sol-asset-down",
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
            CreateBtcOptions(paperTakerPricingEnabled: false, [SolMiddleRevertBps100InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient: cryptoPriceClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolMiddleRevertBps100InstantVariant.Id, run.StrategyId);
        Assert.Equal("sol-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(SolMiddleRevertBps100InstantVariant.Id, order.StrategyId);
        Assert.Equal("sol-asset-up", order.AssetId);
        Assert.Equal(0.64m, order.Price);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"SOLUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference_revert\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"revert_decision\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_current_price_usd\":151.6", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_arithmetic_mean_usd\":150", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_abs_move_from_mean_bps\":106.", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_move_from_mean_bps\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"base_selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SolBinanceBpsInstantPricesOpeningLimitFromExecutableAskDepth()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        AddCryptoOddsStartTick(
            repository,
            "SOL",
            "sol-market-1",
            "sol-condition-1",
            now,
            startPriceUsd: 150m,
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down");
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("SOL", 150.02m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "sol-asset-up",
                [new OrderBookLevel(0.60m, 100m)],
                [new OrderBookLevel(0.61m, 4m), new OrderBookLevel(0.64m, 20m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "sol-asset-down",
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
            CreateBtcOptions(paperTakerPricingEnabled: false, [SolBinanceBps1InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient: cryptoPriceClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolBinanceBps1InstantVariant.Id, run.StrategyId);
        Assert.Equal("sol-asset-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(0.64m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(SolBinanceBps1InstantVariant.Id, order.StrategyId);
        Assert.Equal("sol-asset-up", order.AssetId);
        Assert.Equal(0.64m, order.Price);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"SOLUSDT\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_current_price_usd\":150.02", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_start_price_usd\":150", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_move_from_start_bps\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_gtd_initial_executable_ask_shares\":6.25", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_SolBinanceBpsInstantSkipsWhenExecutableAskDepthRequiresPriceAboveCap()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        AddCryptoOddsStartTick(
            repository,
            "SOL",
            "sol-market-1",
            "sol-condition-1",
            now,
            startPriceUsd: 150m,
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down");
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("SOL", 150.02m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "sol-asset-up",
                [new OrderBookLevel(0.62m, 100m)],
                [new OrderBookLevel(0.64m, 4m), new OrderBookLevel(0.66m, 20m)],
                now,
                minOrderSize: 5m),
            OrderBook(
                "sol-asset-down",
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
            CreateBtcOptions(paperTakerPricingEnabled: false, [SolBinanceBps1InstantVariant.Code]),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient: cryptoPriceClient);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(SolBinanceBps1InstantVariant.Id, run.StrategyId);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal(SignalReasonCodes.InstantPriceAboveMax, run.SkipReason);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_max_buy_price\":0.65", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_limit_price\":0.66", run.SkipDiagnosticsJson, StringComparison.Ordinal);
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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
        foreach (var variant in new[] { Middle1Variant, Middle1Bps5Variant })
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
            Middle1Bps5Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(2, result.MarketsObserved);
        Assert.Equal(2, result.EntriesPlaced);
        Assert.Equal(1, btcUsdReferencePriceClient.RequestCount);
        Assert.Equal(2, repository.PaperOrders.Count);
        Assert.All(repository.PaperOrders, order =>
        {
            Assert.Equal(PaperOrderStatus.Pending, order.Status);
            Assert.Equal(0.50m, order.Price);
            Assert.Equal(5m, order.SizeShares);
        });
    }

    [Fact]
    public async Task ProcessAsync_BinanceStartRelativeConcurrentEntriesShareCurrentPriceAndFlushOnce()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        foreach (var variant in new[] { BinanceVariant, Binance45Variant })
        {
            repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
            {
                PaperStakeAmount = 2.50m
            };
        }

        var btcUsdReferencePriceClient = new FakeBtcUsdReferencePriceClient(101m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                [BinanceVariant.Code, Binance45Variant.Code],
                maxConcurrentEntryDecisions: 2),
            btcUsdReferencePriceClient,
            CreateBtcUsdReferenceCache(100m));

        var result = await processor.ProcessAsync();

        Assert.Equal(2, result.EntriesPlaced);
        Assert.Equal(1, btcUsdReferencePriceClient.RequestCount);
        Assert.Equal(1, repository.PaperEntryPersistenceBatchCalls);
        Assert.Equal(2, repository.PaperOrders.Count);
        Assert.All(repository.PaperOrders, order =>
        {
            Assert.Equal(PaperOrderStatus.Pending, order.Status);
            Assert.Equal("asset-up", order.AssetId);
            Assert.Equal("Up", order.Outcome);
        });
        Assert.Equal(2, repository.StrategyMarketPaperRuns.Count(run =>
            string.Equals(run.Status, StrategyMarketPaperRunStatuses.Entered, StringComparison.Ordinal)));
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == Skip1Variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status);
        Assert.Null(run.SkipReason);
        Assert.Null(run.SkipDiagnosticsJson);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotObservePreviousResultStrategies()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
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
            Skip1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(0, result.EntriesPlaced);
        Assert.Empty(repository.StrategyMarketPaperRuns);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessPreviousResultDueEntriesAsync_EntersAfterResolvedLedgerArrives()
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

        var waitingResult = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, waitingResult.MarketsObserved);
        Assert.Equal(0, waitingResult.EntriesPlaced);
        Assert.Equal(0, waitingResult.RunsSkipped);
        var waitingRun = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == Skip1Variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, waitingRun.Status);

        repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
            "BTC",
            now.AddMinutes(-5),
            "Up"));

        var enteredResult = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, enteredResult.EntriesPlaced);
        var enteredRun = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == Skip1Variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("Down", enteredRun.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Contains("\"result_source\":\"resolved_market_ledger_MarketWebSocket\"", order.RawDecisionJson, StringComparison.Ordinal);
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var firstResult = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var secondResult = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var firstResult = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var secondResult = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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
        Assert.Contains("\"selected_candidate_strategy_code\":\"btc_up_down_5m_middle_100\"", order.RawDecisionJson, StringComparison.Ordinal);
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.False(repository.StrategySettings[Skip1Variant.Id].LiveStakes);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.PreflightRejected, liveOrder.Status);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Contains("live available balance is insufficient", liveOrder.ValidationSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(repository.LiveTradingEvents, item => item.Action == "StrategyLiveBalance");
    }

    [Fact]
    public async Task ProcessAsync_Skip1LiveStakeCreatesPaperShadowAndFakLiveOrder()
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);
        Assert.Equal(2.50m, request.MarketBuyAmountUsd);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal(liveOrder.CreatedAtUtc, liveOrder.ExpiresAtUtc);
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
    public async Task ProcessAsync_LiveStakeAllowsOpenLiveOrderInDifferentMarket()
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
        repository.LiveOrders.Add(CreateOpenLiveOrder(
            now,
            "other-asset",
            "other-condition",
            "Up",
            AlwaysUpVariant.Id));
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.Equal(2, repository.LiveOrders.Count);
        var candidateOrder = repository.LiveOrders.Single(order =>
            string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(LiveOrderStatus.Matched, candidateOrder.Status);
        Assert.Empty(candidateOrder.ValidationSummary);
    }

    [Fact]
    public async Task ProcessAsync_LiveStakePreflightRejectsOppositeLiveOrderInSameMarket()
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
        repository.LiveOrders.Add(CreateOpenLiveOrder(
            now,
            "asset-up",
            "condition-1",
            "Up",
            AlwaysUpVariant.Id));
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Equal(2, repository.LiveOrders.Count);
        var candidateOrder = repository.LiveOrders.Single(order =>
            string.Equals(order.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase) &&
            order.StrategyId == Skip1Variant.Id);
        Assert.Equal(LiveOrderStatus.PreflightRejected, candidateOrder.Status);
        Assert.Contains("Opposite outcome open order exists", candidateOrder.ValidationSummary, StringComparison.Ordinal);
        Assert.Contains("BlockingSource=Live", candidateOrder.ValidationSummary, StringComparison.Ordinal);

        var run = Assert.Single(
            repository.StrategyMarketPaperRuns,
            item => string.Equals(item.ConditionId, "condition-1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Null(run.SkipReason);
        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Cancelled, paperOrder.Status);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("live_preflight_rejected", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_LiveStakeIgnoresOppositePaperOrderInSameMarket()
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
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            AlwaysUpVariant.CopiedTraderWallet,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-up",
            "condition-1",
            "Up",
            0.50m,
            5m,
            2.50m,
            now.AddSeconds(-15),
            now.AddMinutes(5),
            StrategyId: AlwaysUpVariant.Id));
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var closeBookOrderBooks = AddCloseBookResults(repository, now, "Up");
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessor(repository, tradingClient, closeBookOrderBooks, Skip1Variant.Code);

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Empty(liveOrder.ValidationSummary);
        Assert.Equal(2, repository.PaperOrders.Count);
        var paperOrder = repository.PaperOrders.Single(order => order.StrategyId == Skip1Variant.Id);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("asset-down", paperOrder.AssetId);
    }

    [Fact]
    public async Task ProcessAsync_AutoLivePausedStrategyStillCreatesPaperOrderWithoutLiveShadow()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = true,
            AutoLivePaused = true,
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.LiveOrders);
        Assert.Empty(repository.PaperLiveShadowDecisions);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(Skip1Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal(string.Empty, paperOrder.ExecutionSource);
        Assert.Null(paperOrder.CorrelationId);
        Assert.DoesNotContain("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.True(repository.StrategySettings[Skip1Variant.Id].LiveStakes);
        Assert.True(repository.StrategySettings[Skip1Variant.Id].AutoLivePaused);
    }

    [Fact]
    public async Task ProcessAsync_LiveUncheckedStrategyCreatesPaperOrderWithoutLiveShadow()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Skip1Variant.Id] = StrategyRuntimeSettings.Default(Skip1Variant.Id) with
        {
            LiveStakes = false,
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.LiveOrders);
        Assert.Empty(repository.PaperLiveShadowDecisions);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(Skip1Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal(string.Empty, paperOrder.ExecutionSource);
        Assert.Null(paperOrder.CorrelationId);
        Assert.DoesNotContain("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.False(repository.StrategySettings[Skip1Variant.Id].LiveStakes);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps1LiveStakeCreatesPaperShadowAndFakLiveOrder()
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
            100.11m,
            [100m],
            [],
            BinanceBps1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps1Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
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
        Assert.Contains("\"btc_min_move_from_start_bps\":10", paperOrder.RawDecisionJson, StringComparison.Ordinal);
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
    public async Task ProcessAsync_BinanceBps18LiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps18Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps18Variant.Id) with
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
            100.19m,
            [100m],
            [],
            BinanceBps18Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps18Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps18Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":18", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps18Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps19LiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps19Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps19Variant.Id) with
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
            100.20m,
            [100m],
            [],
            BinanceBps19Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps19Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps19Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":19", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps19Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps2LiveStakeCreatesPaperShadowAndFakLiveOrder()
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
            100.21m,
            [100m],
            [],
            BinanceBps2Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps2Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
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
        Assert.Contains("\"btc_min_move_from_start_bps\":20", paperOrder.RawDecisionJson, StringComparison.Ordinal);
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
    public async Task ProcessAsync_SolBinanceBps24InstantLiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[SolBinanceBps24InstantVariant.Id] = StrategyRuntimeSettings.Default(SolBinanceBps24InstantVariant.Id) with
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
            downPrice: 0.38m,
            slug: $"sol-updown-5m-{now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - test",
            marketId: "sol-market-1",
            conditionId: "sol-condition-1",
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down"));
        AddCryptoOddsStartTick(
            repository,
            "SOL",
            "sol-market-1",
            "sol-condition-1",
            now,
            startPriceUsd: 150m,
            upAssetId: "sol-asset-up",
            downAssetId: "sol-asset-down");
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("SOL", 150.40m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook("sol-asset-up", bestBid: 0.34m, bestAsk: 0.36m, now),
            OrderBook("sol-asset-down", bestBid: 0.64m, bestAsk: 0.66m, now)
        ];
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xsol-instant-fak",
                "matched",
                null,
                "2.50",
                "6.9444444444",
                """{"status":"matched","makingAmount":"2.50","takingAmount":"6.9444444444"}""",
                "{}")
        };
        var processor = CreateLiveProcessorWithCryptoReference(
            repository,
            tradingClient,
            cryptoPriceClient,
            orderBooks,
            [],
            SolBinanceBps24InstantVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);
        Assert.Equal(0.36m, request.Price);
        Assert.Equal(2.50m, request.MarketBuyAmountUsd);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(SolBinanceBps24InstantVariant.Id, liveOrder.StrategyId);
        Assert.Equal("sol-asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.36m, liveOrder.Price);
        Assert.Equal(2.50m, liveOrder.FilledNotionalUsd);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(SolBinanceBps24InstantVariant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", paperOrder.ExecutionSource);
        Assert.NotNull(paperOrder.FilledAtUtc);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("sol-asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.36m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"live_order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_asset_symbol\":\"SOL\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_binance_symbol\":\"SOLUSDT\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_current_price_usd\":150.4", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_start_price_usd\":150", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"crypto_min_move_from_start_bps\":24", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_pricing_source\":\"websocket_cache\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_filled_notional_usd\":2.50", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        var paperFill = Assert.Single(repository.PaperFills);
        Assert.Equal(paperOrder.Id, paperFill.PaperOrderId);
        Assert.Equal(paperOrder.Price, paperFill.Price);
        Assert.Equal(paperOrder.SizeShares, paperFill.SizeShares);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(SolBinanceBps24InstantVariant.Id, decision.StrategyId);
        Assert.Equal("FAK", decision.OrderType);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_MiddleBps45InstantLiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[Middle1Bps45InstantVariant.Id] = StrategyRuntimeSettings.Default(Middle1Bps45InstantVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xmiddle-instant-fak",
                "matched",
                null,
                "2.50",
                "6.9444444444",
                """{"status":"matched","makingAmount":"2.50","takingAmount":"6.9444444444"}""",
                "{}")
        };
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            99.52m,
            [100m],
            [],
            Middle1Bps45InstantVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);
        Assert.Equal(0.36m, request.Price);
        Assert.Equal(2.50m, request.MarketBuyAmountUsd);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(Middle1Bps45InstantVariant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.36m, liveOrder.Price);
        Assert.Equal(2.50m, liveOrder.FilledNotionalUsd);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(Middle1Bps45InstantVariant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal("btc_updown5m_fak_taker_paper", paperOrder.ExecutionSource);
        Assert.NotNull(paperOrder.FilledAtUtc);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.36m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"live_order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_middle_reference\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_mean_bps\":45", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"opening_limit_price_mode\":\"instant_executable_ask_depth\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instant_pricing_source\":\"websocket_cache\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_order_execution_mode\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_filled_notional_usd\":2.50", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        var paperFill = Assert.Single(repository.PaperFills);
        Assert.Equal(paperOrder.Id, paperFill.PaperOrderId);
        Assert.Equal(paperOrder.Price, paperFill.Price);
        Assert.Equal(paperOrder.SizeShares, paperFill.SizeShares);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(Middle1Bps45InstantVariant.Id, decision.StrategyId);
        Assert.Equal("FAK", decision.OrderType);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_PaperLiveShadowPersistSubmitFailureKeepsStrategyLive()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository
        {
            ThrowOnNextLiveOrderUpdate = true
        };
        repository.StrategySettings[Middle1Bps45InstantVariant.Id] = StrategyRuntimeSettings.Default(Middle1Bps45InstantVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            99.52m,
            [100m],
            [],
            Middle1Bps45InstantVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.Equal(1, tradingClient.CancelOrderCalls);
        Assert.Empty(repository.StrategyLiveStakeUpdates);
        Assert.True(repository.StrategySettings[Middle1Bps45InstantVariant.Id].LiveStakes);
        Assert.Contains(
            repository.LiveTradingEvents,
            item => item.Action == "BtcUpDown5mPaperLiveShadowPersistSubmit" &&
                item.Status == "Error" &&
                item.Details.Contains("Simulated live order update failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_PaperLiveShadowIntentFailureKeepsStrategyLive()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository
        {
            ThrowOnNextLiveOrderAdd = true
        };
        repository.StrategySettings[Middle1Bps45InstantVariant.Id] = StrategyRuntimeSettings.Default(Middle1Bps45InstantVariant.Id) with
        {
            LiveStakes = true,
            LiveStakeAmount = 2.50m,
            LiveAvailableBalance = 100m,
            PaperStakeAmount = 2.50m
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.38m,
            downPrice: 0.62m));
        var tradingClient = new CapturingTradingClient();
        var processor = CreateLiveProcessorWithBtcReference(
            repository,
            tradingClient,
            99.52m,
            [100m],
            [],
            Middle1Bps45InstantVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, tradingClient.PlaceCalls);
        Assert.Empty(repository.StrategyLiveStakeUpdates);
        Assert.True(repository.StrategySettings[Middle1Bps45InstantVariant.Id].LiveStakes);
        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Cancelled, paperOrder.Status);
        Assert.Contains(
            repository.LiveTradingEvents,
            item => item.Action == "BtcUpDown5mPaperLiveShadowIntent" &&
                item.Status == "Error" &&
                item.Details.Contains("Simulated live order add failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps21LiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps21Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps21Variant.Id) with
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
            100.22m,
            [100m],
            [],
            BinanceBps21Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps21Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps21Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":21", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps21Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps22LiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps22Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps22Variant.Id) with
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
            100.23m,
            [100m],
            [],
            BinanceBps22Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps22Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps22Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":22", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps22Variant.Id, decision.StrategyId);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(paperOrder.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessAsync_BinanceBps23LiveStakeCreatesPaperShadowAndFakLiveOrder()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[BinanceBps23Variant.Id] = StrategyRuntimeSettings.Default(BinanceBps23Variant.Id) with
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
            100.24m,
            [100m],
            [],
            BinanceBps23Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        Assert.NotNull(tradingClient.LastRequest);
        var request = tradingClient.LastRequest;
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(BinanceBps23Variant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.50m, liveOrder.Price);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(BinanceBps23Variant.Id, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, paperOrder.Status);
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal("asset-up", paperOrder.AssetId);
        Assert.Equal("Up", paperOrder.Outcome);
        Assert.Equal(0.50m, paperOrder.Price);
        Assert.Equal(liveOrder.SizeShares, paperOrder.SizeShares);
        Assert.Contains("\"decision_source\":\"binance_trade_stream_market_start_relative\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"btc_min_move_from_start_bps\":23", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Up\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(BinanceBps23Variant.Id, decision.StrategyId);
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
            100.11m,
            [100m],
            [],
            BinanceBps1Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
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

        var result = await processor.ProcessPreviousResultDueEntriesAsync();

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

    private static BtcUpDown5mPaperStrategyProcessor CreateMakerProcessor(
        TestAppRepository repository,
        MutableFakeClobClient clobClient,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes,
                paperTakerMaxQuoteAgeMilliseconds: 50),
            clobClient: clobClient);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveMakerProcessor(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        MutableFakeClobClient clobClient,
        params string[] enabledVariantCodes)
    {
        return CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledVariantCodes,
                paperTakerMaxQuoteAgeMilliseconds: 50),
            clobClient: clobClient,
            tradingClient: tradingClient,
            botOptions: new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m });
    }

    private static LiveOrder CreateOpenLiveOrder(
        DateTimeOffset createdAtUtc,
        string assetId,
        string conditionId,
        string outcome,
        Guid strategyId)
    {
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Live,
            "0xopen-" + Guid.NewGuid().ToString("N"),
            TradeSide.Buy,
            assetId,
            conditionId,
            outcome,
            0.50m,
            5m,
            2.50m,
            "GTD",
            createdAtUtc,
            createdAtUtc.AddMinutes(5),
            createdAtUtc,
            "live",
            0m,
            5m,
            string.Empty,
            "{}",
            string.Empty,
            createdAtUtc,
            StrategyId: strategyId);
    }

    private static LiveOrder CreateSettledLiveOrder(
        Guid strategyId,
        decimal realizedPnlUsd,
        DateTimeOffset settledAtUtc)
    {
        var suffix = Guid.NewGuid().ToString("N");
        const decimal costBasisUsd = 1m;
        return new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xmatched-" + suffix,
            TradeSide.Buy,
            "history-asset-" + suffix,
            "history-condition-" + suffix,
            realizedPnlUsd >= 0m ? "Up" : "Down",
            0.50m,
            2m,
            costBasisUsd,
            "GTD",
            settledAtUtc.AddMinutes(-10),
            settledAtUtc.AddMinutes(-5),
            settledAtUtc.AddMinutes(-10),
            "matched",
            2m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            settledAtUtc,
            StrategyId: strategyId,
            BalanceEffectApplied: true,
            SettlementValueUsd: costBasisUsd + realizedPnlUsd,
            RealizedPnlUsd: realizedPnlUsd,
            SettledAtUtc: settledAtUtc,
            WinningAssetId: "history-asset-" + suffix,
            WinningOutcome: realizedPnlUsd >= 0m ? "Up" : "Down",
            AverageFillPrice: 0.50m,
            FilledNotionalUsd: costBasisUsd,
            CostBasisUsd: costBasisUsd,
            Won: realizedPnlUsd >= 0m,
            SettlementSource: "test",
            ExecutionSource: "history");
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
        return CreateLiveProcessorWithReferences(
            repository,
            tradingClient,
            new FakeBtcUsdReferencePriceClient(currentBtcUsd),
            CreateBtcUsdReferenceCache(cachedBtcUsd),
            new FakeCryptoReferencePriceClient(),
            DefaultOrderBooks(),
            clobOrderBooks,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveProcessorWithCryptoReference(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        ICryptoReferencePriceClient cryptoReferencePriceClient,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
    {
        return CreateLiveProcessorWithReferences(
            repository,
            tradingClient,
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient,
            orderBooks,
            clobOrderBooks,
            enabledVariantCodes);
    }

    private static BtcUpDown5mPaperStrategyProcessor CreateLiveProcessorWithReferences(
        TestAppRepository repository,
        CapturingTradingClient tradingClient,
        IBtcUsdReferencePriceClient btcUsdReferencePriceClient,
        IBtcUsdReferencePriceCache btcUsdReferencePriceCache,
        ICryptoReferencePriceClient cryptoReferencePriceClient,
        IReadOnlyList<OrderBookSnapshot> orderBooks,
        IReadOnlyList<OrderBookSnapshot> clobOrderBooks,
        params string[] enabledVariantCodes)
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
                InstantOpeningLimitMaxPrice = 0.65m,
                DiffCounterInstantMaxPrice = 1.00m,
                OpeningLimitPriceTickSize = 0.01m
            },
            marketDataWebSocketOptions,
            new FakeGammaClient([]),
            new FakeClobClient(orderBooks.Concat(clobOrderBooks).ToArray()),
            new PassGeoClient(),
            tradingClient,
            new ReadyAuthService(),
            btcUsdReferencePriceClient,
            btcUsdReferencePriceCache,
            cryptoReferencePriceClient,
            new FakeCryptoReferencePriceAverageProvider(),
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
        ICryptoReferencePriceClient? cryptoReferencePriceClient = null,
        IPolymarketGammaClient? gammaClient = null,
        IPolymarketClobPublicClient? clobClient = null,
        IPolymarketTradingClient? tradingClient = null,
        BotOptions? botOptions = null,
        PaperTradingOptions? paperTradingOptions = null,
        TimeProvider? timeProvider = null,
        LiveTradingOptions? liveTradingOptions = null,
        IPolymarketGeoClient? geoClient = null,
        ICryptoReferencePriceAverageProvider? cryptoReferencePriceAverageProvider = null)
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
            botOptions ?? new BotOptions { Mode = BotMode.Paper },
            new PolymarketAuthOptions
            {
                Enabled = true,
                SigningAddress = "0x1111111111111111111111111111111111111111",
                FunderAddress = "0x1111111111111111111111111111111111111111",
                SignatureType = "EOA"
            },
            paperTradingOptions ?? new PaperTradingOptions { InitialBankrollUsd = 10_000m },
            liveTradingOptions ?? new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 2.50m },
            strategyOptions,
            marketDataWebSocketOptions,
            gammaClient ?? new FakeGammaClient(metadata),
            clobClient ?? new FakeClobClient(clobOrderBooks),
            geoClient ?? new PassGeoClient(),
            tradingClient ?? new CapturingTradingClient(),
            new ReadyAuthService(),
            btcUsdReferencePriceClient ?? new FakeBtcUsdReferencePriceClient(100m),
            btcUsdReferencePriceCache ?? CreateBtcUsdReferenceCache(100m),
            cryptoReferencePriceClient ?? new FakeCryptoReferencePriceClient(),
            cryptoReferencePriceAverageProvider ?? new FakeCryptoReferencePriceAverageProvider(),
            marketDataCache,
            activeMarketAssetSubscriptionRegistry,
            new ExposureSnapshotCache(repository),
            new ServiceControlState(),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository,
            timeProvider);
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
        int maxMarketsPerCycle = 500,
        int paperTakerMaxQuoteAgeMilliseconds = 1_500)
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
            PaperTakerMaxQuoteAgeMilliseconds = paperTakerMaxQuoteAgeMilliseconds,
            PaperTakerMaxEntryPrice = 0.80m,
            PaperTakerMaxReferenceSlippage = 0.05m,
            PaperTakerMaxSpreadAbs = 0.20m,
            PaperTakerMaxGammaClobDiff = 0.20m,
            OpeningLimitDynamicBreakEvenPricingEnabled = openingLimitDynamicBreakEvenPricingEnabled,
            OpeningLimitBreakEvenLookbackRuns = openingLimitBreakEvenLookbackRuns,
            OpeningLimitBreakEvenMinSettledRuns = openingLimitBreakEvenMinSettledRuns,
            OpeningLimitBreakEvenMargin = openingLimitBreakEvenMargin,
            OpeningLimitMaxPrice = 0.50m,
            InstantOpeningLimitMaxPrice = 0.65m,
            DiffCounterInstantMaxPrice = 1.00m,
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
        var sampleCount = prices.Count == 0 ? 0 : 100;
        for (var index = 0; index < sampleCount; index++)
        {
            cache.Add(new BtcUsdReferencePricePoint(
                prices[index % prices.Count],
                now.AddMinutes(index - sampleCount),
                now.AddMinutes(index - sampleCount),
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

    private static void AssertDiffCounterTrendFakPremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(40, assetVariants.Length);
        AssertDiffCounterTrendFakPremarketSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Up);
        AssertDiffCounterTrendFakPremarketSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Down);
    }

    private static void AssertDiffCounterTrendFakPremarketSide(
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        BtcUpDownFixedOutcome triggerOutcome)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var triggerCode = triggerOutcome == BtcUpDownFixedOutcome.Up ? "up" : "down";
        var triggerName = triggerOutcome.ToString();
        var counterTrendOutcome = triggerOutcome == BtcUpDownFixedOutcome.Up
            ? BtcUpDownFixedOutcome.Down
            : BtcUpDownFixedOutcome.Up;
        var sideVariants = variants
            .Where(variant => variant.DiffCounterTriggerOutcome == triggerOutcome)
            .ToArray();

        Assert.Equal(20, sideVariants.Length);
        Assert.Equal(
            Enumerable.Range(1, 10),
            sideVariants
                .Where(variant => variant.FixedOutcome == counterTrendOutcome)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        Assert.Equal(
            Enumerable.Range(1, 10),
            sideVariants
                .Where(variant => variant.FixedOutcome == triggerOutcome)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        Assert.Contains(sideVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_{triggerCode}_diff_3_fak_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m {triggerName} 3 Diff Premarket" &&
            variant.FixedOutcome == counterTrendOutcome);
        Assert.Contains(sideVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_{triggerCode}_diff_3_revert_fak_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m {triggerName} 3 Diff Revert Premarket" &&
            variant.FixedOutcome == triggerOutcome);
    }

    private static void AssertDiffProgressGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(100, assetVariants.Length);
        AssertDiffProgressSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Up);
        AssertDiffProgressSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Down);
        Assert.All(assetVariants, variant =>
            Assert.Equal($"{assetSymbol} Up/Down 5m Diff Progress", variant.Category));
    }

    private static void AssertDiffProgressSide(
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        BtcUpDownFixedOutcome triggerOutcome)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var triggerCode = triggerOutcome == BtcUpDownFixedOutcome.Up ? "up" : "down";
        var triggerName = triggerOutcome.ToString();
        var targetOutcome = triggerOutcome == BtcUpDownFixedOutcome.Up
            ? BtcUpDownFixedOutcome.Down
            : BtcUpDownFixedOutcome.Up;
        var sideVariants = variants
            .Where(variant => variant.DiffCounterTriggerOutcome == triggerOutcome)
            .ToArray();

        Assert.Equal(50, sideVariants.Length);
        Assert.Equal(
            ExpectedDiffProgressThresholds(),
            sideVariants
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.All(sideVariants, variant => Assert.Equal(targetOutcome, variant.FixedOutcome));
        Assert.Contains(sideVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_diff_17_{triggerCode}_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 17 Diff {triggerName} Progress" &&
            variant.FixedOutcome == targetOutcome);
    }

    private static void AssertDiffShiftProgressGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(2, assetVariants.Length);
        Assert.All(assetVariants, variant =>
        {
            Assert.Equal("Up Or Down 5 min Diff Shift Progress", variant.Category);
            Assert.Equal(0, variant.DecisionDepth);
        });

        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_diff_up_shift_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m Diff Up Shift Progress" &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Down &&
            variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up);
        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_diff_down_shift_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m Diff Down Shift Progress" &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Up &&
            variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Down);
    }

    private static int[] ExpectedDiffCounterThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat(Enumerable.Range(3, 28).Select(index => index * 5))
            .ToArray();
    }

    private static int[] ExpectedDiffProgressThresholds()
    {
        return Enumerable.Range(1, 50).ToArray();
    }

    private static decimal[] ExpectedReferenceAverageBpsThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat(Enumerable.Range(3, 18).Select(index => index * 5))
            .Select(threshold => (decimal)threshold)
            .ToArray();
    }

    private static int[] ExpectedAdjustedDiffCounterThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat([15, 20])
            .ToArray();
    }

    private static int[] ExpectedShiftDiffCounterShifts()
    {
        return Enumerable.Range(1, 6).ToArray();
    }

    private static int[] ExpectedShiftDiffCounterThresholds()
    {
        return Enumerable.Range(1, 12).ToArray();
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
        decimal? minOrderSize = null,
        decimal? tickSize = null)
    {
        return new OrderBookSnapshot(
            assetId,
            bids,
            asks,
            now,
            "condition-1",
            minOrderSize,
            tickSize);
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
                marketId: GetCloseBookMarketId(marketStartUtc),
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

    private static string GetCloseBookMarketId(DateTimeOffset marketStartUtc)
    {
        return "close-market-" + marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<OrderBookSnapshot> AddCryptoCloseBookResults(
        TestAppRepository repository,
        string assetSymbol,
        DateTimeOffset currentMarketStartUtc,
        params string[] winningOutcomes)
    {
        var normalized = assetSymbol.ToUpperInvariant();
        var slugPrefix = normalized.ToLowerInvariant();
        var orderBooks = new List<OrderBookSnapshot>();
        for (var index = 0; index < winningOutcomes.Length; index++)
        {
            var marketStartUtc = currentMarketStartUtc.AddMinutes(-5 * (index + 1));
            var marketEndUtc = marketStartUtc.AddMinutes(5);
            var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var upAssetId = slugPrefix + "-close-up-" + suffix;
            var downAssetId = slugPrefix + "-close-down-" + suffix;
            repository.PolymarketGammaMarkets.Add(CreateMarket(
                marketStartUtc,
                marketEndUtc,
                upPrice: 0.50m,
                downPrice: 0.50m,
                slug: $"{slugPrefix}-updown-5m-{suffix}",
                seriesSlug: $"{slugPrefix}-up-or-down-5m",
                question: normalized + " Up or Down - test",
                marketId: slugPrefix + "-close-market-" + suffix,
                conditionId: slugPrefix + "-close-condition-" + suffix,
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

    private static IReadOnlyList<PolymarketGammaMarket> CreateClosedDiffMarkets(
        DateTimeOffset currentMarketStartUtc,
        params string[] winningOutcomes)
    {
        return CreateClosedDiffMarkets("BTC", currentMarketStartUtc, winningOutcomes);
    }

    private static DateTimeOffset GetCurrentFiveMinuteMarketStartUtc()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var unixSeconds = nowUtc.ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds - (unixSeconds % 300));
    }

    private static IReadOnlyList<PolymarketGammaMarket> CreateClosedDiffMarkets(
        string assetSymbol,
        DateTimeOffset currentMarketStartUtc,
        params string[] winningOutcomes)
    {
        var markets = new List<PolymarketGammaMarket>(winningOutcomes.Length);
        for (var index = 0; index < winningOutcomes.Length; index++)
        {
            var offset = winningOutcomes.Length - index;
            var marketStartUtc = currentMarketStartUtc.AddMinutes(-5 * offset);
            markets.Add(CreateClosedDiffMarket(assetSymbol, marketStartUtc, winningOutcomes[index]));
        }

        return markets;
    }

    private static void AddWebSocketDiffResults(
        TestAppRepository repository,
        string assetSymbol,
        DateTimeOffset latestMarketStartUtc,
        params string[] winningOutcomes)
    {
        for (var index = 0; index < winningOutcomes.Length; index++)
        {
            var offset = winningOutcomes.Length - index - 1;
            var marketStartUtc = latestMarketStartUtc.AddMinutes(-5 * offset);
            repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
                assetSymbol,
                marketStartUtc,
                winningOutcomes[index]));
        }
    }

    private static CryptoUpDown5mWebSocketResolvedMarket CreateWebSocketDiffResult(
        string assetSymbol,
        DateTimeOffset marketStartUtc,
        string winningOutcome)
    {
        var normalized = assetSymbol.Trim().ToUpperInvariant();
        var prefix = normalized.ToLowerInvariant();
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var slug = prefix + "-updown-5m-" + suffix;
        var receivedAtUtc = marketStartUtc.AddMinutes(5).AddSeconds(8);
        return new CryptoUpDown5mWebSocketResolvedMarket(
            Guid.NewGuid(),
            normalized,
            prefix + "-ws-market-" + suffix,
            prefix + "-ws-condition-" + suffix,
            slug,
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase) ? "Up" : "Down",
            string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase)
                ? prefix + "-ws-up-" + suffix
                : prefix + "-ws-down-" + suffix,
            receivedAtUtc,
            receivedAtUtc,
            receivedAtUtc,
            1,
            8m,
            "MarketWebSocket",
            "market_resolved",
            "{}",
            receivedAtUtc,
            receivedAtUtc);
    }

    private static PolymarketGammaMarket CreateClosedDiffMarket(
        string assetSymbol,
        DateTimeOffset marketStartUtc,
        string winningOutcome)
    {
        var normalized = assetSymbol.ToUpperInvariant();
        var prefix = normalized.ToLowerInvariant();
        var suffix = marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var isBtc = string.Equals(normalized, "BTC", StringComparison.OrdinalIgnoreCase);
        var slug = isBtc
            ? "btc-updown-5m-" + suffix
            : prefix + "-updown-5m-" + suffix;
        var seriesSlug = isBtc
            ? "btc-up-or-down-5m"
            : prefix + "-up-or-down-5m";
        var upWon = string.Equals(winningOutcome, "Up", StringComparison.OrdinalIgnoreCase);
        return CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upWon ? 1m : 0m,
            upWon ? 0m : 1m,
            slug: slug,
            seriesSlug: seriesSlug,
            question: normalized + " Up or Down - closed test",
            marketId: prefix + "-diff-market-" + suffix,
            conditionId: prefix + "-diff-condition-" + suffix,
            upAssetId: prefix + "-diff-up-" + suffix,
            downAssetId: prefix + "-diff-down-" + suffix) with
        {
            Active = false,
            Closed = true,
            AcceptingOrders = false
        };
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

    private static void SetFirstMarketWindowStart(
        TestAppRepository repository,
        DateTimeOffset windowStartUtc)
    {
        repository.PolymarketGammaMarkets[0] = repository.PolymarketGammaMarkets[0] with
        {
            EventStartTimeUtc = windowStartUtc
        };
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
        return CreateObservedRun(
            variant,
            market,
            marketStartUtc,
            detectedAtUtc,
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds));
    }

    private static StrategyMarketPaperRun CreateObservedRun(
        BtcUpDown5mStrategyVariant variant,
        PolymarketGammaMarket market,
        DateTimeOffset marketStartUtc,
        DateTimeOffset detectedAtUtc,
        DateTimeOffset entryDueAtUtc)
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
            entryDueAtUtc,
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

    private static void AddCryptoOddsStartTick(
        TestAppRepository repository,
        string assetSymbol,
        string marketId,
        string conditionId,
        DateTimeOffset marketStartUtc,
        decimal startPriceUsd,
        string upAssetId,
        string downAssetId)
    {
        AddCryptoOddsTick(
            repository,
            assetSymbol,
            marketId,
            conditionId,
            marketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: startPriceUsd,
            startPriceUsd: startPriceUsd,
            upAssetId: upAssetId,
            downAssetId: downAssetId);
    }

    private static void AddCryptoOddsTick(
        TestAppRepository repository,
        string assetSymbol,
        string marketId,
        string conditionId,
        DateTimeOffset marketStartUtc,
        int sampleOffsetSeconds,
        decimal binancePriceUsd,
        decimal startPriceUsd,
        string upAssetId,
        string downAssetId,
        decimal upPriceProxy = 0.50m,
        decimal downPriceProxy = 0.50m)
    {
        var normalized = assetSymbol.ToUpperInvariant();
        var sampledAtUtc = marketStartUtc.AddSeconds(sampleOffsetSeconds);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var moveUsd = binancePriceUsd - startPriceUsd;
        var moveBps = startPriceUsd == 0m ? 0m : moveUsd / startPriceUsd * 10_000m;
        var upBid = Math.Max(0.01m, upPriceProxy - 0.01m);
        var upAsk = Math.Min(0.99m, upPriceProxy + 0.01m);
        var downBid = Math.Max(0.01m, downPriceProxy - 0.01m);
        var downAsk = Math.Min(0.99m, downPriceProxy + 0.01m);
        repository.CryptoUpDown5mOddsTicks.Add(new CryptoUpDown5mOddsTick(
            Guid.NewGuid(),
            normalized,
            normalized + "USDT",
            marketId,
            conditionId,
            normalized.ToLowerInvariant() + "-updown-5m-" + marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
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
            upAssetId,
            upBid,
            upAsk,
            upPriceProxy,
            upPriceProxy,
            "mid",
            null,
            "test",
            0,
            downAssetId,
            downBid,
            downAsk,
            downPriceProxy,
            downPriceProxy,
            "mid",
            null,
            "test",
            0,
            "{}",
            sampledAtUtc));
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

    private static StrategyMarketPaperRun AddEnteredRun(
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
        var run = new StrategyMarketPaperRun(
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
            marketStartUtc.AddSeconds(variant.EntryDelaySeconds));
        repository.StrategyMarketPaperRuns.Add(run);
        return run;
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

    private sealed class FakeCryptoReferencePriceClient : ICryptoReferencePriceClient
    {
        private readonly Dictionary<string, decimal> prices = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ETH"] = 3_200m,
            ["SOL"] = 150m
        };
        private readonly Dictionary<string, decimal[]> samples = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ETH"] = [3_200m],
            ["SOL"] = [150m]
        };

        public void SetPrice(string assetSymbol, decimal priceUsd)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            prices[normalized] = priceUsd;
        }

        public void SetSamples(string assetSymbol, params decimal[] pricesUsd)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            samples[normalized] = pricesUsd;
        }

        public Task<CryptoReferencePricePoint> GetPriceAsync(
            string assetSymbol,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            if (!prices.TryGetValue(normalized, out var priceUsd))
            {
                throw new InvalidOperationException("No fake crypto price configured for " + normalized + ".");
            }

            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new CryptoReferencePricePoint(
                normalized,
                normalized + "USDT",
                priceUsd,
                now,
                now,
                "Test"));
        }

        public BtcUsdReferencePriceSnapshot GetSnapshot(string assetSymbol)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            samples.TryGetValue(normalized, out var pricesUsd);
            var now = DateTimeOffset.UtcNow;
            var samplePrices = pricesUsd ?? [];
            var sampleCount = samplePrices.Length == 0 ? 0 : 100;
            var points = Enumerable.Range(0, sampleCount)
                .Select(index => new BtcUsdReferencePricePoint(
                    samplePrices[index % samplePrices.Length],
                    now.AddSeconds(index - sampleCount),
                    now.AddSeconds(index - sampleCount),
                    "Test"))
                .ToArray();
            var mean = points.Length == 0
                ? (decimal?)null
                : points.Sum(point => point.PriceUsd) / points.Length;

            return new BtcUsdReferencePriceSnapshot(
                "Test",
                100,
                points.Length,
                points.Length >= 100,
                mean,
                points.FirstOrDefault(),
                points,
                now);
        }
    }

    private sealed class FakeCryptoReferencePriceAverageProvider : ICryptoReferencePriceAverageProvider
    {
        private readonly Dictionary<string, IReadOnlyList<CryptoReferencePriceAverage>> averagesByAsset =
            new(StringComparer.OrdinalIgnoreCase);

        public void SetFullAverages(string assetSymbol, params decimal[] averagePricesUsd)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            var now = DateTimeOffset.UtcNow;
            var labels = new[] { "24h", "12h", "6h", "3h", "90m", "45m", "20m", "10m" };
            averagesByAsset[normalized] = averagePricesUsd
                .Select((price, index) =>
                {
                    var windowSeconds = index < labels.Length
                        ? labels[index] switch
                        {
                            "24h" => 86_400,
                            "12h" => 43_200,
                            "6h" => 21_600,
                            "3h" => 10_800,
                            "90m" => 5_400,
                            "45m" => 2_700,
                            "20m" => 1_200,
                            _ => 600
                        }
                        : 600;
                    return new CryptoReferencePriceAverage(
                        normalized,
                        normalized + "USDT",
                        index < labels.Length ? labels[index] : "test-" + index.ToString(CultureInfo.InvariantCulture),
                        windowSeconds,
                        Math.Max(10, windowSeconds / 60),
                        60,
                        60,
                        IsFullWindow: true,
                        price,
                        now.AddSeconds(-windowSeconds),
                        now,
                        now);
                })
                .ToArray();
        }

        public CryptoReferencePriceAveragesSnapshot GetSnapshot()
        {
            return new CryptoReferencePriceAveragesSnapshot(
                DateTimeOffset.UtcNow,
                averagesByAsset.Values.SelectMany(item => item).ToArray());
        }

        public IReadOnlyList<CryptoReferencePriceAverage> GetAssetAverages(string assetSymbol)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            return averagesByAsset.TryGetValue(normalized, out var averages)
                ? averages
                : [];
        }

        public CryptoReferencePriceAverage? GetAverage(string assetSymbol, string windowLabel)
        {
            return GetAssetAverages(assetSymbol)
                .FirstOrDefault(average => string.Equals(average.WindowLabel, windowLabel, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    private sealed class FakeGammaClient(
        IReadOnlyList<PolymarketOnChainTokenMetadata> metadata,
        IReadOnlyList<PolymarketGammaMarket>? closedMarkets = null,
        DateTimeOffset? maxAllowedClosedStartTimeMaxUtc = null,
        bool rejectEqualClosedTimeRange = false) : IPolymarketGammaClient
    {
        private readonly IReadOnlyList<PolymarketGammaMarket> closedGammaMarkets = closedMarkets ?? [];

        public int ClosedMarketRequestCount { get; private set; }

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(
            int limit = 500,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>([]);
        }

        public Task<IReadOnlyList<PolymarketGammaMarket>> GetClosedMarketsBySeriesSlugAsync(
            string seriesSlug,
            DateTimeOffset startTimeMinUtc,
            DateTimeOffset startTimeMaxUtc,
            CancellationToken cancellationToken = default)
        {
            ClosedMarketRequestCount++;
            if (maxAllowedClosedStartTimeMaxUtc is { } maxAllowed &&
                startTimeMaxUtc > maxAllowed)
            {
                throw new InvalidOperationException("Closed market history requested past the latest closed 5m market.");
            }

            if (rejectEqualClosedTimeRange &&
                startTimeMinUtc == startTimeMaxUtc)
            {
                throw new InvalidOperationException("Closed market history requested an invalid single-slot time range.");
            }

            var selected = closedGammaMarkets
                .Where(market => string.Equals(market.SeriesSlug, seriesSlug, StringComparison.OrdinalIgnoreCase))
                .Select(market => new
                {
                    Market = market,
                    WindowStartUtc = string.Equals(seriesSlug, "btc-up-or-down-5m", StringComparison.OrdinalIgnoreCase)
                        ? BtcUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
                        : CryptoUpDown5mMarketAnalyzer.GetWindowStartUtc(market)
                })
                .Where(item => item.WindowStartUtc is not null &&
                    item.WindowStartUtc >= startTimeMinUtc &&
                    item.WindowStartUtc <= startTimeMaxUtc)
                .OrderBy(item => item.WindowStartUtc)
                .Select(item => item.Market)
                .ToArray();

            return Task.FromResult<IReadOnlyList<PolymarketGammaMarket>>(selected);
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

    private sealed class MutableFakeClobClient(IReadOnlyList<OrderBookSnapshot> orderBooks) : IPolymarketClobPublicClient
    {
        private readonly object sync = new();
        private Dictionary<string, OrderBookSnapshot> orderBooksByAssetId = ToDictionary(orderBooks);

        public void SetOrderBooks(IReadOnlyList<OrderBookSnapshot> nextOrderBooks)
        {
            lock (sync)
            {
                orderBooksByAssetId = ToDictionary(nextOrderBooks);
            }
        }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                return Task.FromResult(
                    orderBooksByAssetId.TryGetValue(assetId, out var orderBook)
                        ? orderBook
                        : null);
            }
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

        private static Dictionary<string, OrderBookSnapshot> ToDictionary(IReadOnlyList<OrderBookSnapshot> source)
        {
            return source
                .Where(orderBook => !string.IsNullOrWhiteSpace(orderBook.AssetId))
                .ToDictionary(orderBook => orderBook.AssetId, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class PassGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeoblockStatus(false, "127.0.0.1", "US", null));
        }
    }

    private sealed class ThrowingGeoClient : IPolymarketGeoClient
    {
        public Task<GeoblockStatus> GetGeoblockStatusAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("geoblock endpoint unavailable");
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

        public int CancelOrderCalls { get; private set; }

        public int CancelAllOrdersCalls { get; private set; }

        public ClobV2OrderRequest? LastRequest { get; private set; }

        public LiveOrderPlacementResult PlacementResult { get; init; } =
            new(
                true,
                "0xorder",
                "matched",
                null,
                "2.50",
                "5",
                """{"status":"matched","makingAmount":"2.50","takingAmount":"5"}""",
                "{}");

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
            CancelOrderCalls++;
            return Task.FromResult(new LiveOrderCancellationResult(true, [orderId], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderCancellationResult> CancelAllOrdersAsync(CancellationToken ct)
        {
            CancelAllOrdersCalls++;
            return Task.FromResult(new LiveOrderCancellationResult(true, [], new Dictionary<string, string>(), "{}"));
        }

        public Task<LiveOrderStatusResult?> GetLiveOrderStatusAsync(string orderId, CancellationToken ct)
        {
            return Task.FromResult<LiveOrderStatusResult?>(null);
        }
    }
}
