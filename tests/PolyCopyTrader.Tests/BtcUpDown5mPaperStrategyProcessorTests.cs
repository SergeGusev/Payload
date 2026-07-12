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
using PolyCopyTrader.Storage;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mPaperStrategyProcessorTests
{
    private static BtcUpDown5mStrategyVariant More60Variant =>
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 60);

    private static BtcUpDown5mStrategyVariant More270Variant =>
        StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 270);

    private static BtcUpDown5mStrategyVariant More90Below70Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below70Code);

    private static BtcUpDown5mStrategyVariant More90Below65Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below65Code);

    private static BtcUpDown5mStrategyVariant More90Below60Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below60Code);

    private static BtcUpDown5mStrategyVariant More90Below55Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore90Below55Code);

    private static BtcUpDown5mStrategyVariant More60Below60Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore60Below60Code);

    private static BtcUpDown5mStrategyVariant More60Below55Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore60Below55Code);

    private static BtcUpDown5mStrategyVariant More30Below55Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore30Below55Code);

    private static BtcUpDown5mStrategyVariant More120Below70Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore120Below70Code);

    private static BtcUpDown5mStrategyVariant More150Below65Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore150Below65Code);

    private static BtcUpDown5mStrategyVariant More270Below65Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore270Below65Code);

    private static BtcUpDown5mStrategyVariant More270Below60Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == StrategyIds.BtcUpDown5mMore270Below60Code);

    private static BtcUpDown5mStrategyVariant More60GammaVariant =>
        StrategyIds.GetBtcUpDown5mVariant(
            BtcUpDown5mStrategyDirection.More,
            60,
            BtcUpDown5mStrategyBehavior.GammaOutcomeSelection);

    private static BtcUpDown5mStrategyVariant Middle1Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100");

    private static BtcUpDown5mStrategyVariant Middle1Bps5Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_5");

    private static BtcUpDown5mStrategyVariant Middle1Bps5InstantVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_5_instant");

    private static BtcUpDown5mStrategyVariant Middle1Bps20Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_20");

    private static BtcUpDown5mStrategyVariant Middle1Bps20InstantVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_20_instant");

    private static BtcUpDown5mStrategyVariant Middle1Bps45InstantVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_45_instant");

    private static BtcUpDown5mStrategyVariant Middle1Bps100Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_bps_100");

    private static BtcUpDown5mStrategyVariant Middle1RevertVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert");

    private static BtcUpDown5mStrategyVariant Middle1RevertBps100Variant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert_bps_100");

    private static BtcUpDown5mStrategyVariant Middle1RevertBps100InstantVariant =>
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_middle_100_revert_bps_100_instant");

    private static readonly BtcUpDown5mStrategyVariant UpBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant DownBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant Up15mBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_15m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant Down15mBps2InstantVariant =
        StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_15m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant UpSimpleVariant =
        CreateRetiredSimpleVariant(
            "BTC",
            StrategyIds.BtcUpDown5mUpSimple,
            StrategyIds.BtcUpDown5mUpSimpleCode,
            isUp: true);

    private static readonly BtcUpDown5mStrategyVariant DownSimpleVariant =
        CreateRetiredSimpleVariant(
            "BTC",
            StrategyIds.BtcUpDown5mDownSimple,
            StrategyIds.BtcUpDown5mDownSimpleCode,
            isUp: false);

    private static readonly BtcUpDown5mStrategyVariant EthUpBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthDownBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthUp15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_15m_up_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthDown15mBps2InstantVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_15m_down_bps_2_instant");

    private static readonly BtcUpDown5mStrategyVariant EthUpSimpleVariant =
        CreateRetiredSimpleVariant(
            "ETH",
            Guid.Parse("b7c50005-0000-4000-8123-000000000001"),
            "eth_up_down_5m_up_simple",
            isUp: true);

    private static readonly BtcUpDown5mStrategyVariant EthDownSimpleVariant =
        CreateRetiredSimpleVariant(
            "ETH",
            Guid.Parse("b7c50005-0000-4000-8124-000000000001"),
            "eth_up_down_5m_down_simple",
            isUp: false);

    private static BtcUpDown5mStrategyVariant EthMiddle1Variant =>
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "eth_up_down_5m_middle_100");

    private static BtcUpDown5mStrategyVariant EthMiddleBps20Variant =>
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
        CreateRetiredSimpleVariant(
            "SOL",
            Guid.Parse("b7c50005-0000-4000-8125-000000000001"),
            "sol_up_down_5m_up_simple",
            isUp: true);

    private static readonly BtcUpDown5mStrategyVariant SolDownSimpleVariant =
        CreateRetiredSimpleVariant(
            "SOL",
            Guid.Parse("b7c50005-0000-4000-8126-000000000001"),
            "sol_up_down_5m_down_simple",
            isUp: false);

    private static BtcUpDown5mStrategyVariant SolMiddleRevertBps100InstantVariant =>
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == "sol_up_down_5m_middle_100_revert_bps_100_instant");

    private static readonly BtcUpDown5mStrategyVariant SolDown8ReferenceAveragePremarketVariant =
        StrategyIds.CryptoUpDown5mVariants.Single(variant => variant.Code == StrategyIds.SolUpDown5mDown8BpsReferenceAveragePremarketCode);

    private static BtcUpDown5mStrategyVariant CreateRetiredSimpleVariant(
        string assetSymbol,
        Guid id,
        string code,
        bool isUp)
    {
        var outcomeName = isUp ? "Up" : "Down";
        return new BtcUpDown5mStrategyVariant(
            id,
            code,
            $"{assetSymbol} Up or Down 5m {outcomeName} Simple",
            $"Retired {assetSymbol} Simple test variant.",
            BtcUpDown5mStrategyDirection.Dynamic,
            0,
            BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant,
            FixedOutcome: isUp ? BtcUpDownFixedOutcome.Up : BtcUpDownFixedOutcome.Down,
            FixedLimitPrice: 0.50m,
            Category: "Simple",
            ReferenceAssetSymbol: assetSymbol);
    }

    [Fact]
    public void StrategyIds_IncludeStandardMartinAndGammaBtcVariants()
    {
        Assert.Equal(929, StrategyIds.BtcUpDown5mVariants.Count);
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(StrategyIds.BtcUpDown5mVariants.Count, StrategyIds.BtcUpDown5mVariants.Select(variant => variant.Code).Distinct().Count());
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.Standard));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StandardEntryPriceCap));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelection));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.GammaOutcomeSelectionEntryPriceCap));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_less_", StringComparison.Ordinal));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_more_", StringComparison.Ordinal) ||
            variant.Name.Contains(" More ", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.Contains("_middle_", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.Contains("_up_down_5m_skip_", StringComparison.Ordinal));
        Assert.Equal(200, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant));
        Assert.Equal(84, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysUp));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AlwaysDown));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomeMaker));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelative));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeFixedPrice));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_binance", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeClever));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeCleverMargin));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeEdge));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeDelayed));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.EnsembleVote));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DynamicMarkov));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.StrategySelector));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend));
        Assert.Equal(40, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.Contains("_adjusted_diff_", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend));
        Assert.Equal(100, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress));
        Assert.Equal(7, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress));
        Assert.Equal(5, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket));
        Assert.Equal(5, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket));
        Assert.Equal(14, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket));
        Assert.Equal(28, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket));
        Assert.Equal(14, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket));
        Assert.Equal(24, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ChildMirror));
        Assert.Equal(24, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressMirror));
        Assert.Equal(24, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ChildRoiMirror));
        Assert.Equal(24, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert));
        Assert.Equal(0, StrategyIds.BtcUpDown5mVariants.Count(variant => variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert));
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
        AssertDiffLimitProgressPremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        AssertDiffRealLimitProgressPremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        AssertDiffReferenceAveragePremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        AssertBpsConfirmedAveragePremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC", confirmationDiffThreshold: 5, idGroup: 8200);
        AssertDiffConfirmedAveragePremarketGrid(StrategyIds.BtcUpDown5mVariants, "BTC", confirmationBpsThreshold: 45, idGroup: 8203);
        AssertChildMirrorGrid(StrategyIds.BtcUpDown5mVariants, "BTC");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_middle_", StringComparison.Ordinal));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code == "btc_up_down_5m_up" ||
            variant.Code == "btc_up_down_5m_down" ||
            variant.Code == "btc_up_down_5m_up_maker" ||
            variant.Code == "btc_up_down_5m_down_maker" ||
            variant.Code == "btc_up_down_5m_down_maker_50");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.EndsWith("_simple", StringComparison.Ordinal));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.Contains("_up_down_5m_skip_", StringComparison.Ordinal));
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
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_binance", StringComparison.Ordinal));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThreshold);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.BinanceStartRelativeBpsThresholdInstant);
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
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_less_", StringComparison.Ordinal) ||
            variant.Name.Contains(" Less ", StringComparison.Ordinal));
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code.StartsWith("btc_up_down_5m_more_", StringComparison.Ordinal) ||
            variant.Name.Contains(" More ", StringComparison.Ordinal));
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
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Code is "btc_up_down_5m_up_maker_50"
                or "btc_up_down_5m_ensemble_2_of_3"
                or "btc_up_down_5m_dynamic_markov"
                or "btc_up_down_5m_strategy_selector");
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, IsCountertrendVariant);
        Assert.DoesNotContain(StrategyIds.BtcUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
            variant.Code.Contains("_adjusted_diff_", StringComparison.Ordinal));
        var btcProgress17 = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_diff_17_up_progress");
        Assert.Equal("BTC Up or Down 5m 17 Diff Up Progress", btcProgress17.Name);
        Assert.Equal("BTC Up/Down 5m Diff Up Progress", btcProgress17.Category);
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
    public void StrategyIds_ExcludeCryptoBinanceBpsVariants()
    {
        Assert.Equal(1239, StrategyIds.CryptoUpDown5mVariants.Count);
        Assert.Equal(2168, StrategyIds.UpDown5mStrategyVariants.Count);
        Assert.Equal(
            StrategyIds.UpDown5mStrategyVariants.Count,
            StrategyIds.UpDown5mStrategyVariants.Select(variant => variant.Id).Distinct().Count());
        Assert.Equal(
            StrategyIds.UpDown5mStrategyVariants.Count,
            StrategyIds.UpDown5mStrategyVariants.Select(variant => variant.Code).Distinct().Count());
        Assert.Equal(652, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(587, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            string.Equals(variant.ReferenceAssetSymbol, "SOL", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThreshold));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReference));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceInstant));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.MiddleReferenceRevertInstant));
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.Contains("_middle_", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResults));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipConsecutiveMarketResultsRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThreshold));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SkipPreviousResultBpsThresholdInstant));
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.Contains("_up_down_5m_skip_", StringComparison.Ordinal));
        Assert.Equal(400, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdInstant));
        Assert.Single(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFak);
        Assert.Equal(62, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FixedOutcomePreviousResultBpsThresholdFakPremarket));
        Assert.Equal(168, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.SimpleFixedOutcomeInstant));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrend));
        Assert.Equal(40, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend));
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.Contains("_adjusted_diff_", StringComparison.Ordinal));
        Assert.Equal(0, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend));
        Assert.Equal(198, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffProgress));
        Assert.Equal(14, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffShiftProgress));
        Assert.Equal(10, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket));
        Assert.Equal(10, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket));
        Assert.Equal(28, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket));
        Assert.Equal(56, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket));
        Assert.Equal(28, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket));
        Assert.Equal(48, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ChildMirror));
        Assert.Equal(48, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressMirror));
        Assert.Equal(48, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ChildRoiMirror));
        Assert.Equal(48, StrategyIds.CryptoUpDown5mVariants.Count(variant =>
            variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror));
        AssertDiffCounterTrendFakPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffCounterTrendFakPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffProgressGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffProgressGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffShiftProgressGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffShiftProgressGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffLimitProgressPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffLimitProgressPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffRealLimitProgressPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffRealLimitProgressPremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertDiffReferenceAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertDiffReferenceAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        AssertBpsConfirmedAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH", confirmationDiffThreshold: 3, idGroup: 8201);
        AssertBpsConfirmedAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL", confirmationDiffThreshold: 1, idGroup: 8202);
        AssertDiffConfirmedAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "ETH", confirmationBpsThreshold: 5, idGroup: 8204);
        AssertDiffConfirmedAveragePremarketGrid(StrategyIds.CryptoUpDown5mVariants, "SOL", confirmationBpsThreshold: 35, idGroup: 8205);
        AssertChildMirrorGrid(StrategyIds.CryptoUpDown5mVariants, "ETH");
        AssertChildMirrorGrid(StrategyIds.CryptoUpDown5mVariants, "SOL");
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, IsCountertrendVariant);
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.EndsWith("_simple", StringComparison.Ordinal));

        var expectedThresholds = Enumerable.Range(1, 50)
            .Select(threshold => (decimal)threshold)
            .ToArray();
        foreach (var assetSymbol in new[] { "ETH", "SOL" })
        {
            var expectedBinanceThresholds = Array.Empty<decimal>();
            Assert.Equal(
                expectedBinanceThresholds,
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
                expectedBinanceThresholds,
                StrategyIds.CryptoUpDown5mVariants
                    .Where(variant =>
                        string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                        variant.Behavior == BtcUpDown5mStrategyBehavior.CryptoBinanceStartRelativeBpsThresholdInstant)
                    .Select(variant => variant.DecisionThresholdBps.GetValueOrDefault())
                    .OrderBy(threshold => threshold)
                    .ToArray());
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Code.Contains("_up_down_5m_skip_", StringComparison.Ordinal));
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Code.Contains("_up_down_5m_middle_", StringComparison.Ordinal));
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
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                IsCountertrendVariant(variant));
            Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                variant.Behavior == BtcUpDown5mStrategyBehavior.AdjustedDiffCounterTrend &&
                variant.Code.Contains("_adjusted_diff_", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.StartsWith("eth_up_down_5m_binance_bps_", StringComparison.Ordinal) ||
            variant.Name.StartsWith("ETH Up or Down 5m Binance ", StringComparison.Ordinal));
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
        Assert.Equal("ETH Up/Down 5m Down Bps Reference Average Premarket", ethDownBps9FakPremarketVariant.Category);
        Assert.Equal(-30, ethDownBps9FakPremarketVariant.EntryDelaySeconds);
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.OrdinalIgnoreCase) &&
            IsCountertrendVariant(variant));
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
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_binance_bps_1"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_binance_bps_1_instant"));
        Assert.DoesNotContain(StrategyIds.CryptoUpDown5mVariants, variant =>
            variant.Code.StartsWith("sol_up_down_5m_binance_bps_", StringComparison.Ordinal));
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

        Assert.Equal(252, variants.Length);
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
                    Assert.Equal($"{assetSymbol} Up/Down 5m {triggerOutcome} Bps Reference Average Premarket", item.Category);
                    Assert.Equal(
                        triggerOutcome == BtcUpDownFixedOutcome.Up ? BtcUpDownFixedOutcome.Down : BtcUpDownFixedOutcome.Up,
                        item.FixedOutcome);
                });
            }

            var neutralVariants = variants
                .Where(item =>
                    string.Equals(item.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase) &&
                    item.DiffCounterTriggerOutcome is null)
                .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
                .ToArray();

            Assert.Equal(ExpectedReferenceAverageBpsThresholds(), neutralVariants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
            Assert.All(neutralVariants, item =>
            {
                Assert.Equal(-30, item.EntryDelaySeconds);
                Assert.Equal($"{assetSymbol} Up/Down 5m Bps Reference Average Premarket", item.Category);
                Assert.Null(item.FixedOutcome);
            });
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

        var ethNeutral9 = variants.Single(item => item.Code == "eth_up_down_5m_reference_average_bps_9_fak_premarket");
        Assert.Equal("ETH Up or Down 5m 9 bps Reference Average Premarket", ethNeutral9.Name);
        Assert.Null(ethNeutral9.DiffCounterTriggerOutcome);
        Assert.Null(ethNeutral9.FixedOutcome);
    }

    [Fact]
    public void StrategyIds_ExcludeEthDownFilteredAveragePremarketVariants()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(item => item.Behavior == BtcUpDown5mStrategyBehavior.FilteredReferenceAverageBpsThresholdFakPremarket)
            .ToArray();

        Assert.Empty(variants);
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("eth_up_down_5m_down_filtered_average_bps_1_fak_premarket"));
        Assert.DoesNotContain(StrategyIds.UpDown5mStrategyVariants, item =>
            item.Code.StartsWith("eth_up_down_5m_down_filtered_average_bps_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_ChildRoiMirrorSelectsParentByAdjustedRoiAfterMinimumSample()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var childVariant = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_1_child_roi");
        var thinHighRoiParent = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_up_bps_1_instant");
        var rawRoiParent = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_up_bps_2_instant");
        var adjustedRoiParent = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_down_bps_2_instant");

        AddSettledRun(repository, thinHighRoiParent, "thin-high-roi-parent", now.AddMinutes(-30), stakeUsd: 1m, realizedPnlUsd: 1m);
        for (var index = 0; index < 10; index++)
        {
            AddSettledRun(repository, rawRoiParent, "raw-roi-parent-" + index.ToString(CultureInfo.InvariantCulture), now.AddMinutes(-30 - index), stakeUsd: 6m, realizedPnlUsd: 1.8m);
            AddSettledRun(repository, adjustedRoiParent, "adjusted-roi-parent-" + index.ToString(CultureInfo.InvariantCulture), now.AddMinutes(-30 - index), stakeUsd: 24m, realizedPnlUsd: 4.8m);
        }

        var processor = CreateProcessorWithoutOrderBooks(
            repository,
            [],
            childVariant.Code,
            thinHighRoiParent.Code,
            rawRoiParent.Code,
            adjustedRoiParent.Code);

        await processor.ProcessAsync();

        var assignment = Assert.Single(repository.StrategyChildParentAssignments, item =>
            item.EndedAtUtc is null &&
            StrategyIds.Normalize(item.ChildStrategyId) == StrategyIds.Normalize(childVariant.Id));
        Assert.Equal(StrategyIds.Normalize(adjustedRoiParent.Id), StrategyIds.Normalize(assignment.ParentStrategyId));
        Assert.Equal(StrategyChildParentAssignmentModes.ChildRoi, assignment.ChildMode);
        Assert.Equal(48m, assignment.ParentPnlUsd);
        Assert.Equal(20m, assignment.ParentRoiPct);
    }

    [Fact]
    public async Task ProcessAsync_ChildMirrorStrategiesExcludeFuturesParents()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        var childVariants = new[]
        {
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_1_child"),
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_1_child_progress"),
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_1_child_roi"),
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_1_child_progress_roi")
        };
        var futuresParent = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_futures_basis_bps_1_fak_premarket");
        var nonFuturesParent = StrategyIds.BtcUpDown5mVariants.Single(variant =>
            variant.Code == "btc_up_down_5m_reference_average_bps_1_fak_premarket");

        AddSettledRun(repository, futuresParent, "futures-parent", now.AddMinutes(-30), stakeUsd: 1m, realizedPnlUsd: 100m);
        for (var index = 0; index < 10; index++)
        {
            AddSettledRun(repository, nonFuturesParent, "non-futures-parent-" + index.ToString(CultureInfo.InvariantCulture), now.AddMinutes(-30 - index), stakeUsd: 10m, realizedPnlUsd: 0.1m);
        }
        var processor = CreateProcessorWithoutOrderBooks(
            repository,
            [],
            childVariants.Select(variant => variant.Code)
                .Append(futuresParent.Code)
                .Append(nonFuturesParent.Code)
                .ToArray());

        await processor.ProcessAsync();

        var activeAssignments = repository.StrategyChildParentAssignments
            .Where(assignment => assignment.EndedAtUtc is null)
            .Where(assignment => childVariants
                .Select(variant => StrategyIds.Normalize(variant.Id))
                .Contains(StrategyIds.Normalize(assignment.ChildStrategyId)))
            .ToArray();

        Assert.Equal(childVariants.Length, activeAssignments.Length);
        Assert.All(activeAssignments, assignment =>
        {
            Assert.Equal(StrategyIds.Normalize(nonFuturesParent.Id), StrategyIds.Normalize(assignment.ParentStrategyId));
            Assert.Equal(1m, assignment.ParentPnlUsd);
            Assert.Equal(1m, assignment.ParentRoiPct);
        });
        Assert.DoesNotContain(activeAssignments, assignment =>
            StrategyIds.Normalize(assignment.ParentStrategyId) == StrategyIds.Normalize(futuresParent.Id));
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
    public async Task ProcessAsync_StandardVariantCreatesGtdPaperOrder()
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
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.65m, run.EntryPrice);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(More60Variant.Id, order.StrategyId);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(More60Variant.CopiedTraderWallet, order.CopiedTraderWallet);
        Assert.Equal("asset-down", order.AssetId);
        Assert.Equal(0.65m, order.Price);
        Assert.Equal(1.5384615385m, order.SizeShares, 10);
        Assert.Contains("\"order_execution_mode\":\"GTD\"", order.RawDecisionJson, StringComparison.Ordinal);

        Assert.Empty(repository.PaperFills);
        Assert.Empty(repository.PaperPositions);
    }

    [Fact]
    public async Task ProcessAsync_DoesNotCreateNewRunsForDisabledVariant()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategyEnabledStates[More60Variant.Id] = false;
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now.AddSeconds(-60),
            now.AddMinutes(3),
            upPrice: 0.35m,
            downPrice: 0.65m));
        var processor = CreateProcessor(repository, [], More60Variant.Code);

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
        var processor = CreateProcessor(repository, [], More60Variant.Code);

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
            UpSimpleVariant.CopiedTraderWallet,
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
            StrategyId: UpSimpleVariant.Id));
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
        var processor = CreateProcessorWithoutOrderBooks(repository, [], More60Variant.Code);

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
            More60Variant.Code);

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
            More60Variant.Code);

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
            More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(0.36m, order.Price);
        Assert.Contains("\"source\":\"clob_book\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"rest_attempted\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("missing_orderbook_cache_stale", order.RawDecisionJson, StringComparison.Ordinal);
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
            More60Variant.Code);

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
            More60Variant.Code);

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
                    [],
                    now)
            ],
            More60GammaVariant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal(More60GammaVariant.Id, run.StrategyId);
        Assert.Equal("asset-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(0.40m, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Pending, order.Status);
        Assert.Equal(More60GammaVariant.Id, order.StrategyId);
        Assert.Equal("asset-down", order.AssetId);
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
            More60Variant.Code);

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
            More60Variant.Code);

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
            More60Variant.Code);

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
        var processor = CreateProcessor(repository, [], More60Variant.Code);

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
            UpSimpleVariant,
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
                enabledVariantCodes: preOpenVariants.Select(variant => variant.Code).Append(UpSimpleVariant.Code).ToArray(),
                maxEntriesPerCycle: 1,
                maxConcurrentEntryDecisions: 1,
                maxMarketsPerCycle: 0));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.MarketsObserved);
        Assert.Equal(4, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Equal(4, repository.PaperOrders.Count);
        Assert.Equal(UpSimpleVariant.Id, repository.PaperOrders[0].StrategyId);
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
            More60Variant.Id,
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
            More60Variant.Id,
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
        repository.StrategyEnabledStates[More60Variant.Id] = false;
        repository.PaperPositions.Add(new PaperPosition(
            "asset-up",
            "condition-1",
            "Up",
            2.5m,
            0.40m,
            1m,
            0m,
            now.AddMinutes(-4),
            More60Variant.CopiedTraderWallet));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var updatedRun = repository.StrategyMarketPaperRuns.Single(item => item.Id == run.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(0m, updatedRun.SettlementPrice);
        Assert.Equal(-1m, updatedRun.RealizedPnlUsd);
        var settings = repository.StrategySettings[More60Variant.Id];
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
            More60Variant.Id,
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
            More60Variant.CopiedTraderWallet));
        var metadata = new[]
        {
            TokenMetadata("asset-up", "Up", "Down"),
            TokenMetadata("asset-down", "Down", "Down")
        };
        var processor = CreateProcessor(repository, metadata, More60Variant.Code);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.RunsSettled);
        var updatedRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Settled, updatedRun.Status);
        Assert.Equal(-1m, updatedRun.RealizedPnlUsd);
        var settings = repository.StrategySettings[More60Variant.Id];
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
            More60Variant.Id,
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
            More60Variant.CopiedTraderWallet,
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
            StrategyId: More60Variant.Id,
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
        var processor = CreateProcessor(repository, metadata, More60Variant.Code);

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
        var more30Variant = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 30);
        var slowRun1 = CreateEnteredSettlementRun(
            more30Variant,
            "slow-market-1",
            "slow-condition-1",
            "slow-asset-1",
            "Up",
            now.AddMinutes(-30),
            paperOrderId: null);
        var slowRun2 = CreateEnteredSettlementRun(
            More60Variant,
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
                enabledVariantCodes: [more30Variant.Code, More60Variant.Code, preOpenVariant.Code],
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
            liveTradingOptions: new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED",
                MaxOrderNotionalUsd = 25m,
                MaxTradeBankrollPct = 1m
            });

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
        Assert.Equal(0.99m, liveOrder.Price);
        var paperOrder = Assert.Single(repository.PaperOrders);
        AssertFilledFakPaperShadowOrder(paperOrder, liveOrder, UpSimpleVariant.Id, "asset-up", "Up");
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
        Assert.Single(repository.PaperFills);
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
    public async Task ProcessDiffCounterFastDueEntriesAsync_DoesNotObserveMarkets()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMinutes(3);
        var timeProvider = new ManualTimeProvider(startupNow);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item => item.Code == "btc_up_down_5m_up_diff_2_instant");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            startupMarketStartUtc,
            startupMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider);

        var dueResult = await processor.ProcessDiffCounterFastDueEntriesAsync();

        Assert.Equal(0, dueResult.MarketsObserved);
        Assert.Empty(repository.StrategyMarketPaperRuns);

        var observeResult = await processor.ProcessDiffCounterObserveAsync();

        Assert.Equal(1, observeResult.MarketsObserved);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, run.Status);
        Assert.Equal(variant.Id, run.StrategyId);
    }

    [Fact]
    public async Task ProcessDiffCounterFastDueEntriesAsync_UsesExposureCacheForDeferredPaperPositions()
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
        var exposureCache = new TestExposureSnapshotCache([]);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider,
            exposureSnapshotCache: exposureCache);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));
        timeProvider.UtcNow = entryMarketStartUtc.AddMinutes(-2);
        _ = await processor.ProcessDiffCounterObserveAsync();
        timeProvider.UtcNow = entryNow;

        var result = await processor.ProcessDiffCounterFastDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, exposureCache.GetSnapshotCalls);
        Assert.Equal(0, repository.GetPaperPositionsCalls);
        Assert.Single(exposureCache.AppliedPaperPositions);
    }

    [Fact]
    public async Task ProcessDiffCounterFastDueEntriesAsync_DoesNotReprocessQueuedRunsBeforeWriterFlush()
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
        var queue = new CapturingPaperEntryPersistenceQueue();
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            gammaClient: new FakeGammaClient([]),
            timeProvider: timeProvider,
            exposureSnapshotCache: new TestExposureSnapshotCache([]),
            paperEntryPersistenceQueue: queue);

        repository.PolymarketGammaMarkets.Add(CreateMarket(
            entryMarketStartUtc,
            entryMarketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "diff-entry-market",
            conditionId: "diff-entry-condition"));
        timeProvider.UtcNow = entryMarketStartUtc.AddMinutes(-2);
        _ = await processor.ProcessDiffCounterObserveAsync();
        timeProvider.UtcNow = entryNow;

        var firstResult = await processor.ProcessDiffCounterFastDueEntriesAsync();
        var secondResult = await processor.ProcessDiffCounterFastDueEntriesAsync();

        Assert.Equal(1, firstResult.EntriesPlaced);
        Assert.Equal(0, secondResult.EntriesPlaced);
        Assert.Equal(0, repository.PaperEntryPersistenceBatchCalls);
        var batch = Assert.Single(queue.Batches);
        Assert.Single(batch.PaperOrders);
        Assert.Single(batch.PaperFills);
        Assert.Single(batch.PaperPositionMaterializations);
        Assert.Single(batch.StrategyRuns);
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

        var batchCallsBeforeEntry = repository.PaperEntryPersistenceBatchCalls;
        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(batchCallsBeforeEntry + 1, repository.PaperEntryPersistenceBatchCalls);
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
        Assert.Contains("\"paper_execution_evidence_class\":\"paper_executable_snapshot_model\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_fill_model\":\"fak_taker_executable_snapshot_v2\"", order.RawDecisionJson, StringComparison.Ordinal);
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
    public async Task ProcessDiffCounterDueEntriesAsync_DiffShiftProgressSkipsWhenDiffIsZero()
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
            "Down");
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
            marketId: "diff-shift-progress-zero-market",
            conditionId: "diff-shift-progress-zero-condition"));

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == entryMarketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_shift_progress_non_positive_diff", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"up_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"down_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"effective_diff\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(1, state.UpCount);
        Assert.Equal(1, state.DownCount);
        Assert.Equal(0m, state.SumAmount);
        Assert.Null(state.PendingMarketStartUtc);
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
            DampingActive: false,
            DampingDirection: null,
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
    public async Task ProcessDiffCounterDueEntriesAsync_DiffShiftProgressPremarketUsesSyntheticResultAndAbsDiffStake()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_2_diff_shift_progress_premarket");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "ETH",
            "Up",
            UpCount: 1,
            DownCount: 0,
            SumAmount: 5m,
            DampingActive: false,
            DampingDirection: null,
            LastProcessedMarketStartUtc: marketStartUtc.AddMinutes(-10),
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: now.AddMinutes(-30),
            UpdatedAtUtc: now.AddMinutes(-30)));
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-shift-previous-market",
            "eth-shift-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-previous-up",
            downAssetId: "eth-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-shift-previous-market",
            "eth-shift-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 2020m,
            startPriceUsd: 2000m,
            upAssetId: "eth-previous-up",
            downAssetId: "eth-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - shift progress premarket",
            marketId: "eth-shift-progress-premarket-market",
            conditionId: "eth-shift-progress-premarket-condition",
            upAssetId: "eth-shift-current-up",
            downAssetId: "eth-shift-current-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-shift-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-shift-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-shift-current-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(2m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("eth-shift-current-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(2m, order.NotionalUsd);
        Assert.Contains("\"threshold\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"progress_mode\":\"Damping\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"damping_active\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_result_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":2", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(2, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(0m, state.SumAmount);
        Assert.True(state.DampingActive);
        Assert.Equal("Up", state.DampingDirection);
        Assert.Equal(previousMarketStartUtc, state.LastProcessedMarketStartUtc);
        Assert.Equal(marketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Down", state.PendingTargetOutcome);
        Assert.Equal(2m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffLimitProgressPremarketCapsAbsDiffStake()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_2_diff_limit_progress_premarket");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "ETH",
            "Up",
            UpCount: 2,
            DownCount: 0,
            SumAmount: 5m,
            DampingActive: false,
            DampingDirection: null,
            LastProcessedMarketStartUtc: marketStartUtc.AddMinutes(-10),
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: now.AddMinutes(-30),
            UpdatedAtUtc: now.AddMinutes(-30)));
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-limit-previous-market",
            "eth-limit-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-limit-previous-up",
            downAssetId: "eth-limit-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-limit-previous-market",
            "eth-limit-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 2020m,
            startPriceUsd: 2000m,
            upAssetId: "eth-limit-previous-up",
            downAssetId: "eth-limit-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - limit progress premarket",
            marketId: "eth-limit-progress-premarket-market",
            conditionId: "eth-limit-progress-premarket-condition",
            upAssetId: "eth-limit-current-up",
            downAssetId: "eth-limit-current-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-limit-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-limit-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-limit-current-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(2m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("eth-limit-current-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(2m, order.NotionalUsd);
        Assert.Equal("btc_updown5m_fak_taker_paper", order.ExecutionSource);
        Assert.Contains("\"diff_limit_progress_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"persistent_utc_day_diff_limit_progress_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"order_type\":\"FAK\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"uncapped_stake_multiplier\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_cap\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_capped\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_result_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(variant.Id, state.StrategyId);
        Assert.Equal("ETH", state.AssetSymbol);
        Assert.Equal("Up", state.TriggerOutcome);
        Assert.Equal(3, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(5m, state.SumAmount);
        Assert.False(state.DampingActive);
        Assert.Null(state.DampingDirection);
        Assert.Equal(previousMarketStartUtc, state.LastProcessedMarketStartUtc);
        Assert.Equal(marketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Down", state.PendingTargetOutcome);
        Assert.Equal(2m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffRealLimitProgressPremarketFreezesCountersAtLimit()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_2_diff_real_limit_progress_premarket");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "ETH",
            "Up",
            UpCount: 2,
            DownCount: 0,
            SumAmount: 5m,
            DampingActive: false,
            DampingDirection: null,
            LastProcessedMarketStartUtc: marketStartUtc.AddMinutes(-10),
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: now.AddMinutes(-30),
            UpdatedAtUtc: now.AddMinutes(-30)));
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-real-limit-previous-market",
            "eth-real-limit-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-real-limit-previous-up",
            downAssetId: "eth-real-limit-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-real-limit-previous-market",
            "eth-real-limit-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 2020m,
            startPriceUsd: 2000m,
            upAssetId: "eth-real-limit-previous-up",
            downAssetId: "eth-real-limit-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - real limit progress premarket",
            marketId: "eth-real-limit-progress-premarket-market",
            conditionId: "eth-real-limit-progress-premarket-condition",
            upAssetId: "eth-real-limit-current-up",
            downAssetId: "eth-real-limit-current-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-real-limit-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-real-limit-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-real-limit-current-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(2m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("eth-real-limit-current-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(2m, order.NotionalUsd);
        Assert.Contains("\"diff_real_limit_progress_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"persistent_utc_day_diff_real_limit_progress_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_real_limit_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_real_limit\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"uncapped_stake_multiplier\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_cap\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_capped\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_result_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(variant.Id, state.StrategyId);
        Assert.Equal("ETH", state.AssetSymbol);
        Assert.Equal("Up", state.TriggerOutcome);
        Assert.Equal(2, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(5m, state.SumAmount);
        Assert.False(state.DampingActive);
        Assert.Null(state.DampingDirection);
        Assert.Equal(previousMarketStartUtc, state.LastProcessedMarketStartUtc);
        Assert.Equal(marketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Down", state.PendingTargetOutcome);
        Assert.Equal(2m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffReferenceAveragePremarketUsesExtremeAverageDiff()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var historicalTargetMarketStartUtc = previousMarketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_3_diff_reference_average_premarket");
        var repository = new TestAppRepository();
        var historicalOutcomes = Enumerable.Range(0, 279)
            .Select(index => index % 2 == 0 ? "Down" : "Up")
            .Concat(Enumerable.Repeat("Up", 8))
            .ToArray();
        AddWebSocketDiffResults(
            repository,
            "ETH",
            historicalTargetMarketStartUtc,
            historicalOutcomes);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-diff-average-previous-market",
            "eth-diff-average-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-diff-average-previous-up",
            downAssetId: "eth-diff-average-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-diff-average-previous-market",
            "eth-diff-average-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 2020m,
            startPriceUsd: 2000m,
            upAssetId: "eth-diff-average-previous-up",
            downAssetId: "eth-diff-average-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - diff reference average premarket",
            marketId: "eth-diff-average-premarket-market",
            conditionId: "eth-diff-average-premarket-condition",
            upAssetId: "eth-diff-average-current-up",
            downAssetId: "eth-diff-average-current-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-diff-average-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-diff-average-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-diff-average-current-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("eth-diff-average-current-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"diff_reference_average_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"decision_source\":\"rolling_24h_diff_reference_average_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_mode\":\"rolling_24h_no_utc_day_reset\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_average_window\":\"45m\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_average_diff\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"current_diff\":8", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_delta_from_average\":4", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff_reference_average_min_delta\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_result_outcome\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"selected_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BpsConfirmedAveragePremarketEntersWhenSignalsAgreeAndSharesDiffHistory()
    {
        var firstVariant = StrategyIds.CryptoUpDown5mVariants.Single(item =>
            item.Code == "eth_up_down_5m_5_bps_confirmed_average_premarket");
        var secondVariant = StrategyIds.CryptoUpDown5mVariants.Single(item =>
            item.Code == "eth_up_down_5m_6_bps_confirmed_average_premarket");
        var context = CreateEthConfirmedAverageTestContext(
            currentReferencePriceUsd: 2020m,
            [firstVariant.Code, secondVariant.Code]);

        var result = await context.Processor.ProcessAsync();

        Assert.Equal(2, result.EntriesPlaced);
        Assert.Equal(2, context.Repository.PaperOrders.Count);
        Assert.Equal(1, context.Repository.GetCryptoUpDown5mWebSocketResolvedMarketsCalls);
        Assert.All(context.Repository.PaperOrders, order =>
        {
            Assert.Equal("eth-confirmed-current-down", order.AssetId);
            Assert.Equal("Down", order.Outcome);
            Assert.Contains("\"confirmed_average_premarket_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"base_signal_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"confirmation_signal_strategy_code\":\"eth_up_down_5m_3_diff_reference_average_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"confirmation_signal_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"signals_agree\":true", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"base_signal_decision\":{", order.RawDecisionJson, StringComparison.Ordinal);
            Assert.Contains("\"confirmation_signal_decision\":{", order.RawDecisionJson, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffConfirmedAveragePremarketEntersWhenSignalsAgree()
    {
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item =>
            item.Code == "eth_up_down_5m_3_diff_confirmed_average_premarket");
        var context = CreateEthConfirmedAverageTestContext(
            currentReferencePriceUsd: 2020m,
            [variant.Code]);

        var result = await context.Processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, context.Repository.GetCryptoUpDown5mWebSocketResolvedMarketsCalls);
        var run = Assert.Single(context.Repository.StrategyMarketPaperRuns, item => item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-confirmed-current-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);

        var order = Assert.Single(context.Repository.PaperOrders);
        Assert.Contains("\"base_signal_strategy_code\":\"eth_up_down_5m_3_diff_reference_average_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"confirmation_signal_strategy_code\":\"eth_up_down_5m_reference_average_bps_5_fak_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"signals_agree\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BpsConfirmedAveragePremarketSkipsWhenSignalsDisagree()
    {
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item =>
            item.Code == "eth_up_down_5m_5_bps_confirmed_average_premarket");
        var context = CreateEthConfirmedAverageTestContext(
            currentReferencePriceUsd: 1980m,
            [variant.Code]);

        var result = await context.Processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(context.Repository.PaperOrders);
        Assert.Equal(1, context.Repository.GetCryptoUpDown5mWebSocketResolvedMarketsCalls);
        var run = Assert.Single(context.Repository.StrategyMarketPaperRuns, item => item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("confirmed_average_signal_mismatch", run.SkipReason);
        Assert.Contains("\"base_signal_outcome\":\"Up\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"confirmation_signal_outcome\":\"Down\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"signals_agree\":false", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"skip_reason\":\"confirmed_average_signal_mismatch\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BpsConfirmedAveragePremarketUsesFakLiveShadowWhenLiveIsEnabled()
    {
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item =>
            item.Code == "eth_up_down_5m_5_bps_confirmed_average_premarket");
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xconfirmed",
                "matched",
                null,
                "0.80",
                "1.25",
                "{\"status\":\"matched\",\"makingAmount\":\"0.80\",\"takingAmount\":\"1.25\"}",
                "{}")
        };
        var context = CreateEthConfirmedAverageTestContext(
            currentReferencePriceUsd: 2020m,
            [variant.Code],
            tradingClient);

        var result = await context.Processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var request = Assert.IsType<ClobV2OrderRequest>(tradingClient.LastRequest);
        Assert.Equal(ClobV2OrderType.FAK, request.OrderType);
        Assert.False(request.PostOnly);
        Assert.Null(request.GtdExpirationUtc);

        var liveOrder = Assert.Single(context.Repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("eth-confirmed-current-down", liveOrder.AssetId);
        Assert.Equal("Down", liveOrder.Outcome);

        var paperOrder = Assert.Single(context.Repository.PaperOrders);
        Assert.Equal("paper_live_shadow_actual_fill", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
        Assert.Equal(liveOrder.FilledNotionalUsd, paperOrder.NotionalUsd);
        Assert.Contains("\"confirmed_average_premarket_enabled\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_actual_fill\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var decision = Assert.Single(context.Repository.PaperLiveShadowDecisions);
        Assert.Equal("live_submitted", decision.Status);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffLimitProgressPremarketResetsAtUtcDayStart()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 9, 0, 5, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_5_diff_limit_progress_premarket");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "ETH",
            "Up",
            UpCount: 5,
            DownCount: 0,
            SumAmount: 7m,
            DampingActive: true,
            DampingDirection: "Up",
            LastProcessedMarketStartUtc: marketStartUtc.AddMinutes(-10),
            PendingMarketStartUtc: marketStartUtc.AddMinutes(-10),
            PendingTargetOutcome: "Down",
            PendingStakeUsd: 3m,
            PendingCreatedAtUtc: now.AddMinutes(-10),
            CreatedAtUtc: now.AddDays(-1),
            UpdatedAtUtc: now.AddMinutes(-10)));
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-limit-reset-previous-market",
            "eth-limit-reset-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-limit-reset-previous-up",
            downAssetId: "eth-limit-reset-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-limit-reset-previous-market",
            "eth-limit-reset-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 1990m,
            startPriceUsd: 2000m,
            upAssetId: "eth-limit-reset-previous-up",
            downAssetId: "eth-limit-reset-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - limit progress UTC reset",
            marketId: "eth-limit-progress-reset-market",
            conditionId: "eth-limit-progress-reset-condition",
            upAssetId: "eth-limit-reset-current-up",
            downAssetId: "eth-limit-reset-current-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-limit-reset-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-limit-reset-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status);
        Assert.Equal("eth-limit-reset-current-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);
        Assert.Equal(1m, run.StakeUsd);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("eth-limit-reset-current-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Equal(1m, order.NotionalUsd);
        Assert.Contains("\"utc_day_reset_applied\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"counter_day_start_utc\":\"2026-06-09T00:00:00", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":-1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_cap\":5", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier\":1", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"stake_multiplier_capped\":false", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_result_outcome\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(0, state.UpCount);
        Assert.Equal(1, state.DownCount);
        Assert.Equal(0m, state.SumAmount);
        Assert.False(state.DampingActive);
        Assert.Null(state.DampingDirection);
        Assert.Equal(previousMarketStartUtc, state.LastProcessedMarketStartUtc);
        Assert.Equal(marketStartUtc, state.PendingMarketStartUtc);
        Assert.Equal("Up", state.PendingTargetOutcome);
        Assert.Equal(1m, state.PendingStakeUsd);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffShiftProgressPremarketDampingShiftsToZeroAndSkips()
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 13, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var timeProvider = new ManualTimeProvider(now);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_2_diff_shift_progress_premarket");
        var repository = new TestAppRepository();
        repository.CryptoUpDown5mDiffShiftProgressStates.Add(new CryptoUpDown5mDiffShiftProgressState(
            variant.Id,
            "ETH",
            "Up",
            UpCount: 2,
            DownCount: 0,
            SumAmount: 2.50m,
            DampingActive: true,
            DampingDirection: "Up",
            LastProcessedMarketStartUtc: previousMarketStartUtc,
            PendingMarketStartUtc: null,
            PendingTargetOutcome: null,
            PendingStakeUsd: null,
            PendingCreatedAtUtc: null,
            CreatedAtUtc: now.AddMinutes(-30),
            UpdatedAtUtc: now.AddMinutes(-30)));
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - shift progress damping",
            marketId: "eth-shift-progress-damping-market",
            conditionId: "eth-shift-progress-damping-condition",
            upAssetId: "eth-shift-damping-up",
            downAssetId: "eth-shift-damping-down"));
        var orderBooks = new[]
        {
            OrderBook("eth-shift-damping-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-shift-damping-down", bestBid: 0.44m, bestAsk: 0.45m, now)
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

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        var run = repository.StrategyMarketPaperRuns.Single(item =>
            item.MarketStartUtc == marketStartUtc &&
            item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("diff_shift_progress_zero_diff", run.SkipReason);
        Assert.NotNull(run.SkipDiagnosticsJson);
        Assert.Contains("\"shift_count\":2", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"damping_active\":false", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"diff\":0", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Empty(repository.PaperOrders);

        var state = Assert.Single(repository.CryptoUpDown5mDiffShiftProgressStates);
        Assert.Equal(0, state.UpCount);
        Assert.Equal(0, state.DownCount);
        Assert.Equal(0m, state.SumAmount);
        Assert.False(state.DampingActive);
        Assert.Null(state.DampingDirection);
        Assert.Null(state.PendingMarketStartUtc);
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
        AssertFilledFakPaperShadowOrder(order, liveOrder, variant.Id, "asset-down", "Down");
        Assert.Contains("\"paper_live_shadow_test\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal("Down", order.Outcome);
        Assert.Equal(liveOrder.AverageFillPrice, order.Price);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal(liveOrder.CorrelationId, decision.CorrelationId);
        Assert.Equal(order.Id, decision.PaperOrderId);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.Equal("live_submitted", decision.Status);
    }

    [Fact]
    public async Task ProcessDiffCounterDueEntriesAsync_DiffCounterSkipsMissingPreviousResultAfterEntryGrace()
    {
        var startupMarketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero);
        var startupNow = startupMarketStartUtc.AddMilliseconds(500);
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
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code], entryGraceSeconds: 1),
            gammaClient: gammaClient,
            timeProvider: timeProvider);

        var result = await processor.ProcessDiffCounterDueEntriesAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(0, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var waitingRun = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(StrategyMarketPaperRunStatuses.Observed, waitingRun.Status);
        Assert.Null(waitingRun.SkipReason);
        Assert.Null(waitingRun.SkipDiagnosticsJson);

        timeProvider.UtcNow = startupMarketStartUtc.AddSeconds(2);

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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_35");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8025-000000000035"), run.StrategyId);
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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_fak");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8025-000000000998"), run.StrategyId);
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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_fak");

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
            "eth_up_down_5m_prev_score_countertrend_fak");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8141-000000000998"), run.StrategyId);
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
            "eth_up_down_5m_prev_score_countertrend_fak_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8141-000000000999"), run.StrategyId);
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
            "sol_up_down_5m_prev_score_countertrend_fak");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8142-000000000998"), run.StrategyId);
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
            "sol_up_down_5m_prev_score_countertrend_fak_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8142-000000000999"), run.StrategyId);
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
            "btc_up_down_5m_prev_score_countertrend_fak_premarket");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8025-000000000997"), run.StrategyId);
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
            "btc_up_down_5m_prev_score_countertrend_fak_premarket_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8025-000000000996"), run.StrategyId);
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
            "eth_up_down_5m_prev_score_countertrend_fak_premarket");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8141-000000000997"), run.StrategyId);
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
            "eth_up_down_5m_prev_score_countertrend_fak_premarket_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8141-000000000996"), run.StrategyId);
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
            "sol_up_down_5m_prev_score_countertrend_fak_premarket");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8142-000000000997"), run.StrategyId);
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
            "sol_up_down_5m_prev_score_countertrend_fak_premarket_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8142-000000000996"), run.StrategyId);
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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_fak_revert");

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns);
        Assert.Equal(Guid.Parse("b7c50005-0000-4000-8025-000000000999"), run.StrategyId);
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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_fak_revert");

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

        var processor = CreateProcessor(repository, [], "btc_up_down_5m_prev_score_countertrend_35");

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
                UpSimpleVariant.Id,
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
                enabledVariantCodes: [UpSimpleVariant.Code],
                maxEntriesPerCycle: 4,
                maxConcurrentEntryDecisions: 4));

        var result = await processor.ProcessAsync();

        Assert.Equal(4, result.EntriesPlaced);
        Assert.True(repository.MaxConcurrentPolymarketGammaMarketLookups > 1);
        Assert.Equal(4, repository.PaperOrders.Count);
        Assert.All(
            repository.StrategyMarketPaperRuns.Where(run => run.StrategyId == UpSimpleVariant.Id),
            run => Assert.Equal(StrategyMarketPaperRunStatuses.Entered, run.Status));
    }

    [Fact]
    public async Task ProcessAsync_PausedStrategySkipsNewPaperEntry()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.StrategySettings[DownSimpleVariant.Id] = StrategyRuntimeSettings.Default(DownSimpleVariant.Id) with
        {
            Paused = true,
            PausedUntilUtc = now.AddHours(1)
        };
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m));
        var processor = CreateProcessor(repository, [], DownSimpleVariant.Code);

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
    public async Task ProcessPreviousResultFastDueEntriesAsync_SharesInstantOrderBookRestFetchAcrossDueBatch()
    {
        var now = DateTimeOffset.UtcNow;
        var marketStart = now.AddSeconds(-2);
        var previousStart = marketStart.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var previousMarketId = "btc-ws-market-" + previousSuffix;
        var variants = new[]
        {
            UpBps2InstantVariant,
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up_bps_3_instant"),
            StrategyIds.BtcUpDown5mVariants.Single(variant => variant.Code == "btc_up_down_5m_up_bps_4_instant")
        };
        var repository = new TestAppRepository();
        var market = CreateMarket(
            marketStart,
            marketStart.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m);
        repository.PolymarketGammaMarkets.Add(market);
        repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
            "BTC",
            previousStart,
            "Down"));
        AddBtcOddsTick(repository, previousMarketId, previousStart, 0, 100m, 100m, 0.50m, 0.50m);
        AddBtcOddsTick(repository, previousMarketId, previousStart, 299, 99.95m, 100m, 0.50m, 0.50m);
        foreach (var variant in variants)
        {
            repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with
            {
                PaperStakeAmount = 2.50m
            };
            repository.StrategyMarketPaperRuns.Add(CreateObservedRun(
                variant,
                market,
                marketStart,
                marketStart.AddSeconds(-1),
                marketStart));
        }

        var clobClient = new FakeClobClient(
            [OrderBook(
                "asset-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 5m)],
            responseDelay: TimeSpan.FromMilliseconds(50));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            [],
            _ => { },
            [],
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                variants.Select(variant => variant.Code).ToArray(),
                maxConcurrentEntryDecisions: 4),
            new FakeBtcUsdReferencePriceClient(100m),
            CreateBtcUsdReferenceCache([100m]),
            clobClient: clobClient);

        var result = await processor.ProcessPreviousResultFastDueEntriesAsync();

        Assert.Equal(3, result.EntriesPlaced);
        Assert.Equal(3, repository.PaperOrders.Count);
        Assert.All(repository.PaperOrders, order => Assert.Equal("asset-up", order.AssetId));
        Assert.Equal(1, clobClient.GetOrderBookCalls);
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
        Assert.Contains("\"paper_execution_evidence_class\":\"paper_executable_snapshot_model\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fak_fill_model\":\"fak_taker_executable_snapshot_v2\"", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BtcFuturesBasisPremarketBuysUpWhenFuturesMidIsAboveSpot()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_futures_basis_bps_2_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "btc-futures-basis-market",
            conditionId: "btc-futures-basis-condition",
            upAssetId: "btc-futures-basis-up",
            downAssetId: "btc-futures-basis-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "btc-futures-basis-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "btc-futures-basis-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var futuresClient = new FakeExpiryFuturesReferencePriceClient();
        futuresClient.SetPrices(
            "BTC",
            100m,
            (100.02m, 100.04m),
            (100.0001m, 100.0003m),
            (100.0002m, 100.0004m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(100m),
            expiryFuturesReferencePriceClient: futuresClient,
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("btc-futures-basis-up", run.SelectedAssetId);
        Assert.Equal("Up", run.SelectedOutcome);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc-futures-basis-up", order.AssetId);
        Assert.Equal("Up", order.Outcome);
        Assert.Contains("\"decision_source\":\"okx_three_expiry_confirmed_futures_basis_bps_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_instrument_id\":\"BTC-USD_UM-300101\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instrument_id\":\"BTC-USD_UM-300108\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"instrument_id\":\"BTC-USD_UM-300115\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_mid_price_usd\":100.03", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"current_price_usd\":100", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_min_move_bps\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_target_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_required_expiry_count\":3", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_required_count\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_matching_count\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_signs_match\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"premarket_futures_basis_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_sign_confirmation_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", order.RawDecisionJson, StringComparison.Ordinal);
        using var decisionDocument = JsonDocument.Parse(order.RawDecisionJson!);
        var expiryDiagnostics = decisionDocument.RootElement
            .GetProperty("futures_expiries")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, expiryDiagnostics.Length);
        Assert.Equal("primary_threshold", expiryDiagnostics[0].GetProperty("role").GetString());
        Assert.Equal("sign_confirmation", expiryDiagnostics[1].GetProperty("role").GetString());
        Assert.Equal("sign_confirmation", expiryDiagnostics[2].GetProperty("role").GetString());
        Assert.Equal("Up", expiryDiagnostics[0].GetProperty("basis_sign").GetString());
        Assert.Equal("Up", expiryDiagnostics[1].GetProperty("basis_sign").GetString());
        Assert.Equal("Up", expiryDiagnostics[2].GetProperty("basis_sign").GetString());
        Assert.True(expiryDiagnostics[1].GetProperty("basis_bps").GetDecimal() < 2m);
        Assert.True(expiryDiagnostics[2].GetProperty("basis_bps").GetDecimal() < 2m);
    }

    [Fact]
    public async Task ProcessAsync_BtcFuturesBasisRevertPremarketBuysDownWhenFuturesMidIsAboveSpot()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_futures_basis_bps_2_revert_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "btc-futures-basis-revert-market",
            conditionId: "btc-futures-basis-revert-condition",
            upAssetId: "btc-futures-basis-revert-up",
            downAssetId: "btc-futures-basis-revert-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "btc-futures-basis-revert-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "btc-futures-basis-revert-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var futuresClient = new FakeExpiryFuturesReferencePriceClient();
        futuresClient.SetPrice("BTC", bidPriceUsd: 100.02m, askPriceUsd: 100.04m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(100m),
            expiryFuturesReferencePriceClient: futuresClient,
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("btc-futures-basis-revert-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal("btc-futures-basis-revert-down", order.AssetId);
        Assert.Equal("Down", order.Outcome);
        Assert.Contains("\"decision_source\":\"okx_three_expiry_confirmed_futures_basis_bps_revert_premarket\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"revert_decision\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_trigger_direction\":\"Up\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_target_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_signs_match\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BtcFuturesBasisPremarketSkipsWhenBasisBelowThreshold()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_futures_basis_bps_2_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "btc-futures-basis-skip-market",
            conditionId: "btc-futures-basis-skip-condition",
            upAssetId: "btc-futures-basis-skip-up",
            downAssetId: "btc-futures-basis-skip-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "btc-futures-basis-skip-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "btc-futures-basis-skip-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var futuresClient = new FakeExpiryFuturesReferencePriceClient();
        futuresClient.SetPrice("BTC", bidPriceUsd: 100.00m, askPriceUsd: 100.02m);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(100m),
            expiryFuturesReferencePriceClient: futuresClient,
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("futures_basis_move_below_bps_threshold", run.SkipReason);
        Assert.Contains("\"decision_source\":\"okx_three_expiry_confirmed_futures_basis_bps_premarket\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_target_direction\":null", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_signs_match\":true", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BtcFuturesBasisPremarketSkipsWhenConfirmationSignDiffers()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_futures_basis_bps_2_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "btc-futures-basis-confirmation-mismatch-market",
            conditionId: "btc-futures-basis-confirmation-mismatch-condition",
            upAssetId: "btc-futures-basis-confirmation-mismatch-up",
            downAssetId: "btc-futures-basis-confirmation-mismatch-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "btc-futures-basis-confirmation-mismatch-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "btc-futures-basis-confirmation-mismatch-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var futuresClient = new FakeExpiryFuturesReferencePriceClient();
        futuresClient.SetPrices(
            "BTC",
            100m,
            (100.02m, 100.04m),
            (100.05m, 100.07m),
            (99.95m, 99.97m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(100m),
            expiryFuturesReferencePriceClient: futuresClient,
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(0, result.EntriesPlaced);
        Assert.Equal(1, result.RunsSkipped);
        Assert.Empty(repository.PaperOrders);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item => item.StrategyId == variant.Id);
        Assert.Equal(StrategyMarketPaperRunStatuses.Skipped, run.Status);
        Assert.Equal("futures_basis_confirmation_sign_mismatch", run.SkipReason);
        Assert.Contains("\"futures_confirmation_matching_count\":1", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_signs_match\":false", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"basis_sign\":\"Down\"", run.SkipDiagnosticsJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_target_direction\":null", run.SkipDiagnosticsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_BtcFuturesBasisPremarketBuysDownWhenAllThreeBasisSignsAreNegative()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_futures_basis_bps_2_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            marketId: "btc-futures-basis-negative-market",
            conditionId: "btc-futures-basis-negative-condition",
            upAssetId: "btc-futures-basis-negative-up",
            downAssetId: "btc-futures-basis-negative-down"));
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "btc-futures-basis-negative-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "btc-futures-basis-negative-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
        var futuresClient = new FakeExpiryFuturesReferencePriceClient();
        futuresClient.SetPrices(
            "BTC",
            100m,
            (99.96m, 99.98m),
            (99.95m, 99.97m),
            (99.90m, 99.92m));
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(paperTakerPricingEnabled: false, [variant.Code]),
            btcUsdReferencePriceClient: new FakeBtcUsdReferencePriceClient(100m),
            expiryFuturesReferencePriceClient: futuresClient,
            timeProvider: new ManualTimeProvider(now));

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal("btc-futures-basis-negative-down", run.SelectedAssetId);
        Assert.Equal("Down", run.SelectedOutcome);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal("Down", order.Outcome);
        Assert.Contains("\"futures_basis_trigger_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_basis_target_direction\":\"Down\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_matching_count\":2", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"futures_confirmation_signs_match\":true", order.RawDecisionJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(3196, "Down", "Up", "eth-neutral-up", 41)]
    [InlineData(3204, "Up", "Down", "eth-neutral-down", 60)]
    public async Task ProcessAsync_EthNeutral9FakPremarketUsesReferenceAverageMoveSign(
        int currentPriceUsd,
        string expectedTriggerDirection,
        string expectedTargetDirection,
        string expectedAssetId,
        int expectedEntryPriceCents)
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var marketStartUtc = now.AddSeconds(30);
        var variant = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "eth_up_down_5m_reference_average_bps_9_fak_premarket");
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-neutral-market",
            conditionId: "eth-neutral-condition",
            upAssetId: "eth-neutral-up",
            downAssetId: "eth-neutral-down"));
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("ETH", currentPriceUsd);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("ETH", 3_150m, 3_200m, 3_175m, 3_180m);
        OrderBookSnapshot[] orderBooks =
        [
            OrderBook(
                "eth-neutral-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "eth-neutral-down",
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
        var expectedEntryPrice = expectedEntryPriceCents / 100m;

        Assert.Equal(1, result.EntriesPlaced);
        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(now, run.EntryDueAtUtc);
        Assert.Equal(expectedAssetId, run.SelectedAssetId);
        Assert.Equal(expectedTargetDirection, run.SelectedOutcome);
        Assert.Equal(expectedEntryPrice, run.EntryPrice);

        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, order.Status);
        Assert.Equal(variant.Id, order.StrategyId);
        Assert.Equal(expectedAssetId, order.AssetId);
        Assert.Equal(expectedTargetDirection, order.Outcome);
        Assert.Equal(expectedEntryPrice, order.Price);
        Assert.Contains("\"reference_average_auto_direction_enabled\":true", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_direction_source\":\"move_sign\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains($"\"reference_average_trigger_direction\":\"{expectedTriggerDirection}\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains($"\"reference_average_target_direction\":\"{expectedTargetDirection}\"", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fixed_outcome\":null", order.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"reference_average_abs_move_from_middle_bps\":12.5", order.RawDecisionJson, StringComparison.Ordinal);
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
        var currentMarketStart = GetCurrentFiveMinuteMarketStartUtc();
        var previousStart = currentMarketStart.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var previousMarketId = "eth-ws-market-" + previousSuffix;
        var previousConditionId = "eth-ws-condition-" + previousSuffix;
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
            currentMarketStart,
            currentMarketStart.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{currentMarketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", currentMarketStart, "Up");
        repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
            "ETH",
            previousStart,
            "Up"));
        AddCryptoOddsTick(
            repository,
            "ETH",
            previousMarketId,
            previousConditionId,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-ws-up-" + previousSuffix,
            downAssetId: "eth-ws-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            previousMarketId,
            previousConditionId,
            previousStart,
            sampleOffsetSeconds: 299,
            binancePriceUsd: 3_202.88m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-ws-up-" + previousSuffix,
            downAssetId: "eth-ws-down-" + previousSuffix);
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
        Assert.True(
            tradingClient.PlaceCalls == 1,
            string.Join(" | ", repository.LiveOrders.Select(order => order.ValidationSummary)));
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
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
        Assert.Equal(liveOrder.FilledNotionalUsd, paperOrder.NotionalUsd);
        Assert.Equal(liveOrder.FilledSize, paperOrder.SizeShares);
        Assert.Equal("paper_live_shadow_actual_fill", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Contains("\"order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"live_order_type\":\"FAK\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"fak_stats_probe\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_actual_fill\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fill_model\":\"live_order_actual_fill_v1\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var paperFill = Assert.Single(repository.PaperFills);
        Assert.Equal(paperOrder.Id, paperFill.PaperOrderId);
        Assert.Equal(paperOrder.Price, paperFill.Price);
        Assert.Equal(paperOrder.SizeShares, paperFill.SizeShares);

        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(paperOrder.Price, run.EntryPrice);
        Assert.Equal(paperOrder.NotionalUsd, run.StakeUsd);
        Assert.Equal(paperOrder.SizeShares, run.SizeShares);

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
        Assert.Equal(1.25m, liveOrder.FilledSize);
        Assert.Equal(0.64m, liveOrder.AverageFillPrice);
        Assert.Equal(0.80m, liveOrder.FilledNotionalUsd);
        Assert.Equal(0.80m, liveOrder.CostBasisUsd);

        var paperOrder = Assert.Single(repository.PaperOrders);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
        Assert.Equal(liveOrder.FilledNotionalUsd, paperOrder.NotionalUsd);
        Assert.Equal(liveOrder.FilledSize, paperOrder.SizeShares);
        Assert.Equal("paper_live_shadow_actual_fill", paperOrder.ExecutionSource);
        Assert.Contains("\"paper_live_shadow_actual_fill\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fill_model\":\"live_order_actual_fill_v1\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);

        var paperFill = Assert.Single(repository.PaperFills);
        Assert.Equal(paperOrder.Id, paperFill.PaperOrderId);
        Assert.Equal(paperOrder.Price, paperFill.Price);
        Assert.Equal(paperOrder.SizeShares, paperFill.SizeShares);

        var run = Assert.Single(repository.StrategyMarketPaperRuns, item =>
            item.StrategyId == variant.Id &&
            item.Status == StrategyMarketPaperRunStatuses.Entered);
        Assert.Equal(paperOrder.Price, run.EntryPrice);
        Assert.Equal(paperOrder.NotionalUsd, run.StakeUsd);
        Assert.Equal(paperOrder.SizeShares, run.SizeShares);

        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("live_submitted", decision.Status);
        var warning = Assert.Single(repository.LiveTradingEvents, item => item.Action == "GeoblockCheck");
        Assert.Equal("Warning", warning.Status);
        Assert.Contains("geoblock endpoint unavailable", warning.Details, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_DateDependentNegativeHourlySnapshotDoesNotSkipLive()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var variant = SolDown8ReferenceAveragePremarketVariant;
        var repository = CreateSolDown8ReferenceAveragePremarketRepository(now, variant);
        repository.DateDependentStrategyHourlyPaperPnl[(StrategyIds.Normalize(variant.Id), now.UtcDateTime.Hour)] = -0.01m;
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("SOL", 149.80m);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("SOL", 149m, 150m, 148m, 147m);
        var orderBooks = CreateSolDown8ReferenceAveragePremarketOrderBooks(now);
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xsol-date-gate-negative",
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
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m },
            cryptoReferencePriceClient: cryptoPriceClient,
            timeProvider: new ManualTimeProvider(now),
            cryptoReferencePriceAverageProvider: averageProvider);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("sol-date-up", liveOrder.AssetId);
        var decision = Assert.Single(repository.PaperLiveShadowDecisions);
        Assert.Equal("live_submitted", decision.Status);
        Assert.Equal(liveOrder.Id, decision.LiveOrderId);
        Assert.DoesNotContain(repository.LiveTradingEvents, item => item.Action == "DateDependentSnapshotLiveGate");
        Assert.True(repository.StrategySettings[variant.Id].LiveStakes);
    }

    [Fact]
    public async Task ProcessAsync_DateDependentZeroHourlySnapshotAllowsLive()
    {
        var now = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        var variant = SolDown8ReferenceAveragePremarketVariant;
        var repository = CreateSolDown8ReferenceAveragePremarketRepository(now, variant);
        repository.DateDependentStrategyHourlyPaperPnl[(StrategyIds.Normalize(variant.Id), now.UtcDateTime.Hour)] = 0m;
        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("SOL", 149.80m);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("SOL", 149m, 150m, 148m, 147m);
        var orderBooks = CreateSolDown8ReferenceAveragePremarketOrderBooks(now);
        var tradingClient = new CapturingTradingClient
        {
            PlacementResult = new LiveOrderPlacementResult(
                true,
                "0xsol-date-gate",
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
            liveTradingOptions: new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m },
            cryptoReferencePriceClient: cryptoPriceClient,
            timeProvider: new ManualTimeProvider(now),
            cryptoReferencePriceAverageProvider: averageProvider);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(1, tradingClient.PlaceCalls);
        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(variant.Id, liveOrder.StrategyId);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal("sol-date-up", liveOrder.AssetId);
        Assert.DoesNotContain(repository.LiveTradingEvents, item => item.Action == "DateDependentSnapshotLiveGate");
    }

    [Fact]
    public async Task ProcessAsync_EthDown9FakLiveStakeRejectsZeroFill()
    {
        var now = DateTimeOffset.UtcNow;
        var currentMarketStart = GetCurrentFiveMinuteMarketStartUtc();
        var previousStart = currentMarketStart.AddMinutes(-5);
        var previousSuffix = previousStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var previousMarketId = "eth-ws-market-" + previousSuffix;
        var previousConditionId = "eth-ws-condition-" + previousSuffix;
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
            currentMarketStart,
            currentMarketStart.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{currentMarketStart.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - test",
            marketId: "eth-market-1",
            conditionId: "eth-condition-1",
            upAssetId: "eth-asset-up",
            downAssetId: "eth-asset-down"));
        var closeBookOrderBooks = AddCryptoCloseBookResults(repository, "ETH", currentMarketStart, "Up");
        repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
            "ETH",
            previousStart,
            "Up"));
        AddCryptoOddsTick(
            repository,
            "ETH",
            previousMarketId,
            previousConditionId,
            previousStart,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 3_200m,
            startPriceUsd: 3_200m,
            upAssetId: "eth-close-up-" + previousSuffix,
            downAssetId: "eth-close-down-" + previousSuffix);
        AddCryptoOddsTick(
            repository,
            "ETH",
            previousMarketId,
            previousConditionId,
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
    public async Task ProcessAsync_QueuesDeferredPaperEntryPersistenceWhenQueueConfigured()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            now,
            now.AddMinutes(5),
            upPrice: 0.62m,
            downPrice: 0.38m));
        AddBtcOddsStartTick(repository, "market-1", now, startPriceUsd: 100m);
        repository.StrategySettings[UpSimpleVariant.Id] = StrategyRuntimeSettings.Default(UpSimpleVariant.Id) with
        {
            PaperStakeAmount = 2.50m
        };
        var queue = new CapturingPaperEntryPersistenceQueue();
        var exposureCache = new TestExposureSnapshotCache([]);
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            DefaultOrderBooks(),
            _ => { },
            Array.Empty<OrderBookSnapshot>(),
            CreateBtcOptions(paperTakerPricingEnabled: false, [UpSimpleVariant.Code]),
            new FakeBtcUsdReferencePriceClient(101m),
            CreateBtcUsdReferenceCache(100m),
            exposureSnapshotCache: exposureCache,
            paperEntryPersistenceQueue: queue);

        var result = await processor.ProcessAsync();

        Assert.Equal(1, result.EntriesPlaced);
        Assert.Equal(0, repository.PaperEntryPersistenceBatchAttempts);
        Assert.Equal(0, repository.PaperEntryPersistenceBatchCalls);
        var batch = Assert.Single(queue.Batches);
        Assert.Single(batch.PaperOrders);
        Assert.Empty(batch.PaperPositions);
        Assert.Empty(batch.PaperPositionMaterializations);
        Assert.Single(batch.StrategyRuns);
        Assert.Single(exposureCache.AppliedPaperOrders);
        Assert.Empty(exposureCache.AppliedPaperPositions);
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
        Assert.Equal(0.99m, request.Price);
        Assert.Equal(2.50m, request.MarketBuyAmountUsd);

        var liveOrder = Assert.Single(repository.LiveOrders);
        Assert.Equal(Middle1Bps45InstantVariant.Id, liveOrder.StrategyId);
        Assert.Equal("asset-up", liveOrder.AssetId);
        Assert.Equal("Up", liveOrder.Outcome);
        Assert.Equal("FAK", liveOrder.OrderType);
        Assert.Equal(LiveOrderStatus.Matched, liveOrder.Status);
        Assert.Equal(0.99m, liveOrder.Price);
        Assert.Equal(2.50m, liveOrder.FilledNotionalUsd);
        Assert.Equal("paper_live_shadow_test", liveOrder.ExecutionSource);
        Assert.False(liveOrder.PostOnly);
        Assert.NotNull(liveOrder.CorrelationId);

        var paperOrder = Assert.Single(repository.PaperOrders);
        AssertFilledFakPaperShadowOrder(paperOrder, liveOrder, Middle1Bps45InstantVariant.Id, "asset-up", "Up");
        Assert.NotNull(paperOrder.FilledAtUtc);
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
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
        Assert.Equal("paper_live_shadow_test", paperOrder.ExecutionSource);
        Assert.Empty(repository.PaperFills);
        Assert.Contains(
            repository.LiveTradingEvents,
            item => item.Action == "BtcUpDown5mPaperLiveShadowIntent" &&
                item.Status == "Error" &&
                item.Details.Contains("Simulated live order add failure", StringComparison.Ordinal));
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
            liveTradingOptions: new LiveTradingOptions
            {
                ManualEnableCode = "LIVE_TRADING_ENABLED",
                MaxOrderNotionalUsd = 25m,
                MaxTradeBankrollPct = 1m
            });
    }

    private static TestAppRepository CreateSolDown8ReferenceAveragePremarketRepository(
        DateTimeOffset now,
        BtcUpDown5mStrategyVariant variant)
    {
        var marketStartUtc = now.AddSeconds(30);
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
            slug: $"sol-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "sol-up-or-down-5m",
            question: "SOL Up or Down - date gate test",
            marketId: "sol-date-market",
            conditionId: "sol-date-condition",
            upAssetId: "sol-date-up",
            downAssetId: "sol-date-down"));
        return repository;
    }

    private static OrderBookSnapshot[] CreateSolDown8ReferenceAveragePremarketOrderBooks(DateTimeOffset now)
    {
        return
        [
            OrderBook(
                "sol-date-up",
                [new OrderBookLevel(0.39m, 100m)],
                [new OrderBookLevel(0.41m, 100m)],
                now,
                minOrderSize: 1m),
            OrderBook(
                "sol-date-down",
                [new OrderBookLevel(0.58m, 100m)],
                [new OrderBookLevel(0.60m, 100m)],
                now,
                minOrderSize: 1m)
        ];
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
            new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 25m },
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
            new FakeExpiryFuturesReferencePriceClient(),
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
        IExpiryFuturesReferencePriceClient? expiryFuturesReferencePriceClient = null,
        IPolymarketGammaClient? gammaClient = null,
        IPolymarketClobPublicClient? clobClient = null,
        IPolymarketTradingClient? tradingClient = null,
        BotOptions? botOptions = null,
        PaperTradingOptions? paperTradingOptions = null,
        TimeProvider? timeProvider = null,
        LiveTradingOptions? liveTradingOptions = null,
        IPolymarketGeoClient? geoClient = null,
        ICryptoReferencePriceAverageProvider? cryptoReferencePriceAverageProvider = null,
        IExposureSnapshotCache? exposureSnapshotCache = null,
        IPaperEntryPersistenceQueue? paperEntryPersistenceQueue = null)
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
            liveTradingOptions ?? new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 25m },
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
            expiryFuturesReferencePriceClient ?? new FakeExpiryFuturesReferencePriceClient(),
            marketDataCache,
            activeMarketAssetSubscriptionRegistry,
            exposureSnapshotCache ?? new ExposureSnapshotCache(repository),
            new ServiceControlState(),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository,
            timeProvider,
            paperEntryPersistenceQueue);
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
        int paperTakerMaxQuoteAgeMilliseconds = 1_500,
        int entryGraceSeconds = 10)
    {
        return new BtcUpDown5mStrategyOptions
        {
            StakeUsd = 1m,
            EntryGraceSeconds = entryGraceSeconds,
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
        string assetSymbol,
        bool includeRevert = false)
    {
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffCounterTrendFakPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var expectedCounterTrendCount =
            ExpectedDiffCounterTrendFakPremarketThresholds(assetSymbol, BtcUpDownFixedOutcome.Up).Length +
            ExpectedDiffCounterTrendFakPremarketThresholds(assetSymbol, BtcUpDownFixedOutcome.Down).Length;
        Assert.Equal(includeRevert ? expectedCounterTrendCount * 2 : expectedCounterTrendCount, assetVariants.Length);
        AssertDiffCounterTrendFakPremarketSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Up, includeRevert);
        AssertDiffCounterTrendFakPremarketSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Down, includeRevert);
    }

    private static void AssertDiffCounterTrendFakPremarketSide(
        IReadOnlyCollection<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        BtcUpDownFixedOutcome triggerOutcome,
        bool includeRevert)
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
        var expectedThresholds = ExpectedDiffCounterTrendFakPremarketThresholds(assetSymbol, triggerOutcome);

        Assert.Equal(includeRevert ? expectedThresholds.Length * 2 : expectedThresholds.Length, sideVariants.Length);
        Assert.Equal(
            expectedThresholds,
            sideVariants
                .Where(variant => variant.FixedOutcome == counterTrendOutcome)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold));
        if (includeRevert)
        {
            Assert.Equal(
                expectedThresholds,
                sideVariants
                    .Where(variant => variant.FixedOutcome == triggerOutcome)
                    .Select(variant => variant.DecisionDepth)
                    .OrderBy(threshold => threshold));
        }
        else
        {
            Assert.DoesNotContain(sideVariants, variant => variant.FixedOutcome == triggerOutcome);
        }
        Assert.Contains(sideVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_{triggerCode}_diff_3_fak_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m {triggerName} 3 Diff Premarket" &&
            variant.FixedOutcome == counterTrendOutcome);
        if (includeRevert)
        {
            Assert.Contains(sideVariants, variant =>
                variant.Code == $"{assetCode}_up_down_5m_{triggerCode}_diff_3_revert_fak_premarket" &&
                variant.Name == $"{assetSymbol} Up or Down 5m {triggerName} 3 Diff Revert Premarket" &&
                variant.FixedOutcome == triggerOutcome);
        }
        else
        {
            Assert.DoesNotContain(sideVariants, variant =>
                variant.Code == $"{assetCode}_up_down_5m_{triggerCode}_diff_3_revert_fak_premarket" ||
                variant.Name == $"{assetSymbol} Up or Down 5m {triggerName} 3 Diff Revert Premarket");
        }
    }

    private static int[] ExpectedDiffCounterTrendFakPremarketThresholds(
        string assetSymbol,
        BtcUpDownFixedOutcome triggerOutcome)
    {
        var maxThreshold = string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase) &&
            triggerOutcome == BtcUpDownFixedOutcome.Down
                ? 30
                : 10;
        return Enumerable.Range(1, maxThreshold).ToArray();
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

        Assert.Equal(ExpectedDiffProgressCount(assetSymbol), assetVariants.Length);
        AssertDiffProgressSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Up);
        AssertDiffProgressSide(assetVariants, assetSymbol, BtcUpDownFixedOutcome.Down);
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
        var expectedThresholds = ExpectedDiffProgressThresholds(assetSymbol, triggerOutcome);

        Assert.Equal(expectedThresholds.Length, sideVariants.Length);
        Assert.Equal(
            expectedThresholds,
            sideVariants
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());
        Assert.All(sideVariants, variant => Assert.Equal(targetOutcome, variant.FixedOutcome));
        Assert.All(sideVariants, variant => Assert.Equal(
            string.Equals(assetSymbol, "BTC", StringComparison.OrdinalIgnoreCase)
                ? $"{assetSymbol} Up/Down 5m Diff {triggerName} Progress"
                : $"{assetSymbol} Up/Down 5m Diff Progress",
            variant.Category));
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

        Assert.Equal(7, assetVariants.Length);
        Assert.All(assetVariants, variant =>
        {
            var expectedCategory = variant.EntryDelaySeconds < 0
                ? $"{assetSymbol} Up/Down 5m Diff Shift Progress Premarket"
                : $"{assetSymbol} Up/Down 5m Diff Shift Progress";
            Assert.Equal(expectedCategory, variant.Category);
        });
        Assert.Equal(
            [1, 2, 3, 4, 5],
            assetVariants
                .Where(variant => variant.EntryDelaySeconds < 0)
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());

        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_diff_up_shift_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m Diff Up Shift Progress" &&
            variant.DecisionDepth == 0 &&
            variant.EntryDelaySeconds == 0 &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Down &&
            variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Up);
        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_diff_down_shift_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m Diff Down Shift Progress" &&
            variant.DecisionDepth == 0 &&
            variant.EntryDelaySeconds == 0 &&
            variant.FixedOutcome == BtcUpDownFixedOutcome.Up &&
            variant.DiffCounterTriggerOutcome == BtcUpDownFixedOutcome.Down);
        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_3_diff_shift_progress_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 3 Diff Shift Progress Premarket" &&
            variant.DecisionDepth == 3 &&
            variant.EntryDelaySeconds == -30 &&
            variant.FixedOutcome is null &&
            variant.DiffCounterTriggerOutcome is null);
    }

    private static void AssertDiffLimitProgressPremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffLimitProgressPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(5, assetVariants.Length);
        Assert.All(assetVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Diff Limit Progress", variant.Category);
            Assert.Equal(-30, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
            Assert.Null(variant.DiffCounterTriggerOutcome);
        });
        Assert.Equal(
            [1, 2, 3, 4, 5],
            assetVariants
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());

        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_3_diff_limit_progress_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 3 Diff Limit Progress Premarket");
    }

    private static void AssertDiffRealLimitProgressPremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffRealLimitProgressPremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(5, assetVariants.Length);
        Assert.All(assetVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Diff Real Limit Progress", variant.Category);
            Assert.Equal(-30, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
            Assert.Null(variant.DiffCounterTriggerOutcome);
        });
        Assert.Equal(
            [1, 2, 3, 4, 5],
            assetVariants
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());

        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_3_diff_real_limit_progress_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 3 Diff Real Limit Progress Premarket");
    }

    private static void AssertDiffReferenceAveragePremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(14, assetVariants.Length);
        Assert.All(assetVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Diff Reference Average Premarket", variant.Category);
            Assert.Equal(-30, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
            Assert.Null(variant.DiffCounterTriggerOutcome);
            Assert.Equal(variant.DecisionDepth, variant.DecisionThresholdBps);
        });
        Assert.Equal(
            ExpectedDiffReferenceAverageThresholds(),
            assetVariants
                .Select(variant => variant.DecisionDepth)
                .OrderBy(threshold => threshold)
                .ToArray());

        Assert.Contains(assetVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_15_diff_reference_average_premarket" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 15 Diff Reference Average Premarket");
    }

    private static void AssertBpsConfirmedAveragePremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        int confirmationDiffThreshold,
        int idGroup)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.BpsConfirmedAveragePremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(variant => variant.DecisionThresholdBps)
            .ToArray();

        Assert.Equal(28, assetVariants.Length);
        Assert.Equal(ExpectedReferenceAverageBpsThresholds(), assetVariants.Select(variant => variant.DecisionThresholdBps.GetValueOrDefault()).ToArray());
        Assert.All(assetVariants, variant =>
        {
            var threshold = decimal.ToInt32(variant.DecisionThresholdBps.GetValueOrDefault());
            Assert.Equal($"b7c50005-0000-4000-{idGroup:0000}-{100 + threshold:000000000000}", variant.Id.ToString());
            Assert.Equal($"{assetCode}_up_down_5m_{threshold}_bps_confirmed_average_premarket", variant.Code);
            Assert.Equal($"{assetSymbol} Up or Down 5m {threshold} bps Confirmed Average Premarket", variant.Name);
            Assert.Equal($"{assetSymbol} Up/Down 5m Bps Confirmed Average Premarket", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(-30, variant.EntryDelaySeconds);
            Assert.Equal(threshold, variant.DecisionDepth);
            Assert.Null(variant.FixedOutcome);
            Assert.Null(variant.DiffCounterTriggerOutcome);
            Assert.NotNull(variant.BaseSignalStrategyId);
            Assert.NotNull(variant.ConfirmationSignalStrategyId);

            var baseVariant = StrategyIds.UpDown5mStrategyVariants.Single(candidate => candidate.Id == variant.BaseSignalStrategyId);
            Assert.Equal(BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket, baseVariant.Behavior);
            Assert.Equal(threshold, baseVariant.DecisionThresholdBps);
            Assert.Null(baseVariant.FixedOutcome);
            Assert.Null(baseVariant.DiffCounterTriggerOutcome);
            Assert.Equal(assetSymbol, baseVariant.ReferenceAssetSymbol);

            var confirmationVariant = StrategyIds.UpDown5mStrategyVariants.Single(candidate => candidate.Id == variant.ConfirmationSignalStrategyId);
            Assert.Equal(BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket, confirmationVariant.Behavior);
            Assert.Equal(confirmationDiffThreshold, confirmationVariant.DecisionDepth);
            Assert.Equal(assetSymbol, confirmationVariant.ReferenceAssetSymbol);
        });
    }

    private static void AssertDiffConfirmedAveragePremarketGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol,
        int confirmationBpsThreshold,
        int idGroup)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var assetVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.DiffConfirmedAveragePremarket &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(variant => variant.DecisionDepth)
            .ToArray();

        Assert.Equal(14, assetVariants.Length);
        Assert.Equal(ExpectedDiffReferenceAverageThresholds(), assetVariants.Select(variant => variant.DecisionDepth).ToArray());
        Assert.All(assetVariants, variant =>
        {
            var threshold = variant.DecisionDepth;
            Assert.Equal($"b7c50005-0000-4000-{idGroup:0000}-{threshold:000000000000}", variant.Id.ToString());
            Assert.Equal($"{assetCode}_up_down_5m_{threshold}_diff_confirmed_average_premarket", variant.Code);
            Assert.Equal($"{assetSymbol} Up or Down 5m {threshold} Diff Confirmed Average Premarket", variant.Name);
            Assert.Equal($"{assetSymbol} Up/Down 5m Diff Confirmed Average Premarket", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(-30, variant.EntryDelaySeconds);
            Assert.Equal(threshold, variant.DecisionThresholdBps);
            Assert.Null(variant.FixedOutcome);
            Assert.Null(variant.DiffCounterTriggerOutcome);
            Assert.NotNull(variant.BaseSignalStrategyId);
            Assert.NotNull(variant.ConfirmationSignalStrategyId);

            var baseVariant = StrategyIds.UpDown5mStrategyVariants.Single(candidate => candidate.Id == variant.BaseSignalStrategyId);
            Assert.Equal(BtcUpDown5mStrategyBehavior.DiffReferenceAveragePremarket, baseVariant.Behavior);
            Assert.Equal(threshold, baseVariant.DecisionDepth);
            Assert.Equal(assetSymbol, baseVariant.ReferenceAssetSymbol);

            var confirmationVariant = StrategyIds.UpDown5mStrategyVariants.Single(candidate => candidate.Id == variant.ConfirmationSignalStrategyId);
            Assert.Equal(BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdFakPremarket, confirmationVariant.Behavior);
            Assert.Equal(confirmationBpsThreshold, confirmationVariant.DecisionThresholdBps);
            Assert.Null(confirmationVariant.FixedOutcome);
            Assert.Null(confirmationVariant.DiffCounterTriggerOutcome);
            Assert.Equal(assetSymbol, confirmationVariant.ReferenceAssetSymbol);
        });
    }

    private static void AssertChildMirrorGrid(
        IEnumerable<BtcUpDown5mStrategyVariant> variants,
        string assetSymbol)
    {
        var assetCode = assetSymbol.ToLowerInvariant();
        var childVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ChildMirror &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var progressVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressMirror &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var roiVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ChildRoiMirror &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var progressRoiVariants = variants
            .Where(variant =>
                variant.Behavior == BtcUpDown5mStrategyBehavior.ChildProgressRoiMirror &&
                string.Equals(variant.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(24, childVariants.Length);
        Assert.Equal(24, progressVariants.Length);
        Assert.Equal(24, roiVariants.Length);
        Assert.Equal(24, progressRoiVariants.Length);
        Assert.Equal(Enumerable.Range(1, 24).ToArray(), childVariants.Select(variant => variant.DecisionDepth).Order().ToArray());
        Assert.Equal(Enumerable.Range(1, 24).ToArray(), progressVariants.Select(variant => variant.DecisionDepth).Order().ToArray());
        Assert.Equal(Enumerable.Range(1, 24).ToArray(), roiVariants.Select(variant => variant.DecisionDepth).Order().ToArray());
        Assert.Equal(Enumerable.Range(1, 24).ToArray(), progressRoiVariants.Select(variant => variant.DecisionDepth).Order().ToArray());

        Assert.All(childVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Child", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(0, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
        });
        Assert.All(progressVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Child Progress", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(0, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
        });
        Assert.All(roiVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Child ROI", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(0, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
        });
        Assert.All(progressRoiVariants, variant =>
        {
            Assert.Equal($"{assetSymbol} Up/Down 5m Child Progress ROI", variant.Category);
            Assert.Equal(BtcUpDown5mStrategyDirection.Dynamic, variant.Direction);
            Assert.Equal(0, variant.EntryDelaySeconds);
            Assert.Null(variant.FixedOutcome);
        });

        Assert.Contains(childVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_1_child" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 1 Child");
        Assert.Contains(progressVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_24_child_progress" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 24 Child Progress");
        Assert.Contains(roiVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_1_child_roi" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 1 Child ROI");
        Assert.Contains(progressRoiVariants, variant =>
            variant.Code == $"{assetCode}_up_down_5m_24_child_progress_roi" &&
            variant.Name == $"{assetSymbol} Up or Down 5m 24 Child Progress ROI");
    }

    private static bool IsCountertrendVariant(BtcUpDown5mStrategyVariant variant)
    {
        return variant.Behavior is
            BtcUpDown5mStrategyBehavior.DiffCounterTrend or
            BtcUpDown5mStrategyBehavior.ShiftDiffCounterTrend or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrend or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFak or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarket or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakRevert or
            BtcUpDown5mStrategyBehavior.PreviousScoreCounterTrendFakPremarketRevert;
    }

    private static int[] ExpectedDiffCounterThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat(Enumerable.Range(3, 28).Select(index => index * 5))
            .ToArray();
    }

    private static int ExpectedDiffProgressCount(string assetSymbol)
    {
        return string.Equals(assetSymbol, "SOL", StringComparison.OrdinalIgnoreCase)
            ? 98
            : 100;
    }

    private static int[] ExpectedDiffProgressThresholds(
        string assetSymbol,
        BtcUpDownFixedOutcome triggerOutcome)
    {
        var thresholds = Enumerable.Range(1, 50);
        if (string.Equals(assetSymbol, "SOL", StringComparison.OrdinalIgnoreCase) &&
            triggerOutcome == BtcUpDownFixedOutcome.Up)
        {
            thresholds = thresholds.Where(threshold => threshold is not (1 or 2));
        }

        return thresholds.ToArray();
    }

    private static decimal[] ExpectedReferenceAverageBpsThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat(Enumerable.Range(3, 18).Select(index => index * 5))
            .Select(threshold => (decimal)threshold)
            .ToArray();
    }

    private static int[] ExpectedDiffReferenceAverageThresholds()
    {
        return Enumerable.Range(1, 10)
            .Concat([15, 20, 25, 30])
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

    private static void AddResolvedPreviousBtcResult(
        TestAppRepository repository,
        DateTimeOffset currentMarketStartUtc,
        string winningOutcome)
    {
        repository.CryptoUpDown5mWebSocketResolvedMarkets.Add(CreateWebSocketDiffResult(
            "BTC",
            currentMarketStartUtc.AddMinutes(-5),
            winningOutcome,
            "BinanceTimedClose"));
    }

    private static void AssertFilledFakPaperShadowOrder(
        PaperOrder paperOrder,
        LiveOrder liveOrder,
        Guid expectedStrategyId,
        string expectedAssetId,
        string expectedOutcome)
    {
        Assert.Equal(expectedStrategyId, paperOrder.StrategyId);
        Assert.Equal(PaperOrderStatus.Filled, paperOrder.Status);
        Assert.Equal("paper_live_shadow_actual_fill", paperOrder.ExecutionSource);
        Assert.Equal(liveOrder.CorrelationId, paperOrder.CorrelationId);
        Assert.Equal(liveOrder.PaperOrderId, paperOrder.Id);
        Assert.Equal(expectedAssetId, paperOrder.AssetId);
        Assert.Equal(expectedOutcome, paperOrder.Outcome);
        Assert.Equal(liveOrder.AverageFillPrice, paperOrder.Price);
        Assert.Equal(liveOrder.FilledSize, paperOrder.SizeShares);
        Assert.Equal(liveOrder.FilledNotionalUsd, paperOrder.NotionalUsd);
        Assert.Contains("\"paper_live_shadow_test\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_live_shadow_actual_fill\":true", paperOrder.RawDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"paper_fill_model\":\"live_order_actual_fill_v1\"", paperOrder.RawDecisionJson, StringComparison.Ordinal);
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

    private static (BtcUpDown5mPaperStrategyProcessor Processor, TestAppRepository Repository) CreateEthConfirmedAverageTestContext(
        decimal currentReferencePriceUsd,
        IReadOnlyCollection<string> enabledStrategyCodes,
        CapturingTradingClient? tradingClient = null)
    {
        var marketStartUtc = new DateTimeOffset(2026, 6, 8, 12, 35, 0, TimeSpan.Zero);
        var previousMarketStartUtc = marketStartUtc.AddMinutes(-5);
        var historicalTargetMarketStartUtc = previousMarketStartUtc.AddMinutes(-5);
        var now = marketStartUtc.AddSeconds(-30);
        var repository = new TestAppRepository();
        if (tradingClient is not null)
        {
            foreach (var strategyCode in enabledStrategyCodes)
            {
                var strategy = StrategyIds.UpDown5mStrategyVariants.Single(variant => variant.Code == strategyCode);
                repository.StrategySettings[strategy.Id] = StrategyRuntimeSettings.Default(strategy.Id) with
                {
                    LiveStakes = true,
                    LiveStakeAmount = 1m,
                    LiveAvailableBalance = 100m,
                    PaperStakeAmount = 1m
                };
            }
        }

        var historicalOutcomes = Enumerable.Range(0, 279)
            .Select(index => index % 2 == 0 ? "Down" : "Up")
            .Concat(Enumerable.Repeat("Up", 8))
            .ToArray();
        AddWebSocketDiffResults(
            repository,
            "ETH",
            historicalTargetMarketStartUtc,
            historicalOutcomes);
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-confirmed-previous-market",
            "eth-confirmed-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 0,
            binancePriceUsd: 2000m,
            startPriceUsd: 2000m,
            upAssetId: "eth-confirmed-previous-up",
            downAssetId: "eth-confirmed-previous-down");
        AddCryptoOddsTick(
            repository,
            "ETH",
            "eth-confirmed-previous-market",
            "eth-confirmed-previous-condition",
            previousMarketStartUtc,
            sampleOffsetSeconds: 270,
            binancePriceUsd: 2020m,
            startPriceUsd: 2000m,
            upAssetId: "eth-confirmed-previous-up",
            downAssetId: "eth-confirmed-previous-down");
        repository.PolymarketGammaMarkets.Add(CreateMarket(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            upPrice: 0.50m,
            downPrice: 0.50m,
            slug: $"eth-updown-5m-{marketStartUtc.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}",
            seriesSlug: "eth-up-or-down-5m",
            question: "ETH Up or Down - confirmed average premarket",
            marketId: "eth-confirmed-current-market",
            conditionId: "eth-confirmed-current-condition",
            upAssetId: "eth-confirmed-current-up",
            downAssetId: "eth-confirmed-current-down"));

        var cryptoPriceClient = new FakeCryptoReferencePriceClient();
        cryptoPriceClient.SetPrice("ETH", currentReferencePriceUsd);
        var averageProvider = new FakeCryptoReferencePriceAverageProvider();
        averageProvider.SetFullAverages("ETH", 2000m, 2000m, 2000m, 2000m, 2000m, 2000m);
        var orderBooks = new[]
        {
            OrderBook("eth-confirmed-current-up", bestBid: 0.54m, bestAsk: 0.55m, now),
            OrderBook("eth-confirmed-current-down", bestBid: 0.44m, bestAsk: 0.45m, now)
        };
        var processor = CreateProcessorCoreWithOptions(
            repository,
            [],
            orderBooks,
            _ => { },
            orderBooks,
            CreateBtcOptions(
                paperTakerPricingEnabled: false,
                enabledStrategyCodes,
                maxConcurrentEntryDecisions: 4),
            gammaClient: new FakeGammaClient([]),
            tradingClient: tradingClient,
            botOptions: tradingClient is null
                ? null
                : new BotOptions { Mode = BotMode.Live, EnableLiveTrading = true },
            paperTradingOptions: tradingClient is null
                ? null
                : new PaperTradingOptions { InitialBankrollUsd = 10_000m, RunInLiveMode = true },
            cryptoReferencePriceClient: cryptoPriceClient,
            timeProvider: new ManualTimeProvider(now),
            liveTradingOptions: tradingClient is null
                ? null
                : new LiveTradingOptions { ManualEnableCode = "LIVE_TRADING_ENABLED", MaxOrderNotionalUsd = 10m },
            cryptoReferencePriceAverageProvider: averageProvider);

        return (processor, repository);
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
        string winningOutcome,
        string source = "MarketWebSocket")
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
            source,
            string.Equals(source, "BinanceTimedClose", StringComparison.OrdinalIgnoreCase)
                ? "binance_timed_close_provisional"
                : "market_resolved",
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
            UpSimpleVariant.Id,
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

    private sealed class FakeExpiryFuturesReferencePriceClient : IExpiryFuturesReferencePriceClient
    {
        private readonly Dictionary<string, IReadOnlyList<ExpiryFuturesReferencePricePoint>> prices = new(StringComparer.OrdinalIgnoreCase);

        public FakeExpiryFuturesReferencePriceClient()
        {
            SetPrice("BTC", bidPriceUsd: 100.01m, askPriceUsd: 100.03m, indexPriceUsd: 100m);
            SetPrice("ETH", bidPriceUsd: 3_200.1m, askPriceUsd: 3_200.3m, indexPriceUsd: 3_200m);
            SetPrice("SOL", bidPriceUsd: 150.01m, askPriceUsd: 150.03m, indexPriceUsd: 150m);
        }

        public void SetPrice(
            string assetSymbol,
            decimal bidPriceUsd,
            decimal askPriceUsd,
            decimal indexPriceUsd = 100m)
        {
            SetPrices(
                assetSymbol,
                indexPriceUsd,
                (bidPriceUsd, askPriceUsd),
                (bidPriceUsd, askPriceUsd),
                (bidPriceUsd, askPriceUsd));
        }

        public void SetPrices(
            string assetSymbol,
            decimal indexPriceUsd,
            params (decimal BidPriceUsd, decimal AskPriceUsd)[] expiryPrices)
        {
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            var now = DateTimeOffset.UtcNow;
            prices[normalized] = expiryPrices
                .Select((price, index) => new ExpiryFuturesReferencePricePoint(
                    normalized,
                    normalized + "-USD_UM-" + new DateTime(2030, 1, 1).AddDays(index * 7).ToString("yyMMdd", CultureInfo.InvariantCulture),
                    new DateTimeOffset(2030, 1, 1, 8, 0, 0, TimeSpan.Zero).AddDays(index * 7),
                    price.BidPriceUsd,
                    price.AskPriceUsd,
                    (price.BidPriceUsd + price.AskPriceUsd) / 2m,
                    indexPriceUsd,
                    now,
                    now,
                    now,
                    "Test"))
                .ToArray();
        }

        public Task<IReadOnlyList<ExpiryFuturesReferencePricePoint>> GetNearestExpiryPricesAsync(
            string assetSymbol,
            DateTimeOffset targetMarketEndUtc,
            int requiredExpiryCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = assetSymbol.Trim().ToUpperInvariant();
            if (!prices.TryGetValue(normalized, out var configuredPrices))
            {
                throw new InvalidOperationException("No fake futures price configured for " + normalized + ".");
            }

            var selectedPrices = configuredPrices
                .Where(price => price.ExpiryAtUtc >= targetMarketEndUtc)
                .Take(requiredExpiryCount)
                .ToArray();
            if (selectedPrices.Length < requiredExpiryCount)
            {
                throw new InvalidOperationException(
                    $"Only {selectedPrices.Length} fake futures expiries are available for {normalized}; {requiredExpiryCount} required.");
            }

            return Task.FromResult<IReadOnlyList<ExpiryFuturesReferencePricePoint>>(selectedPrices);
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

    private sealed class TestExposureSnapshotCache(IReadOnlyList<PaperPosition> paperPositions) : IExposureSnapshotCache
    {
        private readonly List<PaperOrder> appliedPaperOrders = [];
        private readonly List<PaperPosition> appliedPaperPositions = [.. paperPositions];
        private readonly List<LiveOrder> appliedLiveOrders = [];

        public int GetSnapshotCalls { get; private set; }

        public IReadOnlyList<PaperOrder> AppliedPaperOrders => appliedPaperOrders;

        public IReadOnlyList<PaperPosition> AppliedPaperPositions => appliedPaperPositions;

        public Task<TradingExposureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            GetSnapshotCalls++;
            return Task.FromResult(new TradingExposureSnapshot(
                appliedPaperOrders.ToArray(),
                appliedPaperPositions.ToArray(),
                appliedLiveOrders.ToArray(),
                DateTimeOffset.UtcNow));
        }

        public PaperPosition? GetPaperPosition(string copiedTraderWallet, string assetId)
        {
            return appliedPaperPositions.FirstOrDefault(position =>
                string.Equals(position.CopiedTraderWallet, copiedTraderWallet, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(position.AssetId, assetId, StringComparison.OrdinalIgnoreCase));
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void ApplyPaperOrder(PaperOrder order)
        {
            ApplyPaperOrders([order]);
        }

        public void ApplyPaperOrders(IReadOnlyCollection<PaperOrder> orders)
        {
            foreach (var order in orders)
            {
                appliedPaperOrders.RemoveAll(item => item.Id == order.Id);
                appliedPaperOrders.Add(order);
            }
        }

        public void ApplyPaperPosition(PaperPosition position)
        {
            ApplyPaperPositions([position]);
        }

        public void ApplyPaperPositions(IReadOnlyCollection<PaperPosition> positions)
        {
            foreach (var position in positions)
            {
                appliedPaperPositions.RemoveAll(item =>
                    string.Equals(item.CopiedTraderWallet, position.CopiedTraderWallet, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.AssetId, position.AssetId, StringComparison.OrdinalIgnoreCase));
                appliedPaperPositions.Add(position);
            }
        }

        public void ApplyLiveOrder(LiveOrder order)
        {
            appliedLiveOrders.RemoveAll(item => item.Id == order.Id);
            appliedLiveOrders.Add(order);
        }
    }

    private sealed class CapturingPaperEntryPersistenceQueue : IPaperEntryPersistenceQueue
    {
        private readonly List<PaperEntryPersistenceBatch> batches = [];

        public IReadOnlyList<PaperEntryPersistenceBatch> Batches => batches;

        public int PendingBatches => batches.Count;

        public ValueTask EnqueueAsync(
            PaperEntryPersistenceBatch batch,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batches.Add(batch);
            return ValueTask.CompletedTask;
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
        private readonly TimeSpan responseDelay;
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

        public FakeClobClient(IReadOnlyList<OrderBookSnapshot> orderBooks, TimeSpan? responseDelay = null)
        {
            orderBooksByAssetId = orderBooks
                .Where(orderBook => !string.IsNullOrWhiteSpace(orderBook.AssetId))
                .ToDictionary(orderBook => orderBook.AssetId, StringComparer.OrdinalIgnoreCase);
            this.responseDelay = responseDelay ?? TimeSpan.Zero;
        }

        public async Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                orderBookCallsByAssetId.TryGetValue(assetId, out var calls);
                orderBookCallsByAssetId[assetId] = calls + 1;
            }

            if (responseDelay > TimeSpan.Zero)
            {
                await Task.Delay(responseDelay, cancellationToken);
            }

            return orderBooksByAssetId.TryGetValue(assetId, out var orderBook)
                ? orderBook
                : null;
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
