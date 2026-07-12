using System.Text;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class OkxExpiryFuturesReferenceTests
{
    [Fact]
    public void ResponseParser_SelectsThreeNearestDistinctLiveLinearUsdExpiriesAfterTarget()
    {
        var json = """
            {
              "code":"0",
              "msg":"",
              "data":[
                {"instType":"FUTURES","instId":"BTC-USD_UM-260717","instFamily":"BTC-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1784275200000"},
                {"instType":"FUTURES","instId":"BTC-USD_UM-260724","instFamily":"BTC-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1784880000000"},
                {"instType":"FUTURES","instId":"BTC-USD_UM-260925","instFamily":"BTC-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1790323200000"},
                {"instType":"FUTURES","instId":"BTC-USD_UM-261225","instFamily":"BTC-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1798185600000"},
                {"instType":"FUTURES","instId":"BTC-USD-260717","instFamily":"BTC-USD","ctType":"inverse","settleCcy":"BTC","state":"live","expTime":"1784275200000"},
                {"instType":"FUTURES","instId":"BTC-USD_UM_XPERP-310404","instFamily":"BTC-USD_UM_XPERP","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1933056000000"},
                {"instType":"FUTURES","instId":"ETH-USD_UM-260717","instFamily":"ETH-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1784275200000"},
                {"instType":"FUTURES","instId":"SOL-USD_UM-260717","instFamily":"SOL-USD_UM","ctType":"linear","settleCcy":"USD","state":"live","expTime":"1784275200000"}
              ]
            }
            """;
        IReadOnlySet<string> assets = new HashSet<string>(["BTC", "ETH", "SOL"], StringComparer.OrdinalIgnoreCase);

        var parsed = OkxExpiryFuturesResponseParser.TryParseInstruments(
            Encoding.UTF8.GetBytes(json),
            assets,
            out var instruments,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(6, instruments.Count);
        Assert.DoesNotContain(instruments, item => item.InstrumentId.Contains("XPERP", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(instruments, item => item.InstrumentId == "BTC-USD-260717");

        var targetEndUtc = new DateTimeOffset(2026, 7, 11, 12, 5, 0, TimeSpan.Zero);
        var selected = OkxExpiryFuturesResponseParser.SelectNearestExpiries(instruments, "BTC", targetEndUtc, 3);

        Assert.Equal(
            ["BTC-USD_UM-260717", "BTC-USD_UM-260724", "BTC-USD_UM-260925"],
            selected.Select(item => item.InstrumentId).ToArray());
        Assert.All(selected, item => Assert.True(item.ExpiryAtUtc >= targetEndUtc));
        Assert.Equal(3, selected.Select(item => item.ExpiryAtUtc).Distinct().Count());

        var exactExpiry = OkxExpiryFuturesResponseParser.SelectNearestExpiries(
            instruments,
            "BTC",
            selected[0].ExpiryAtUtc,
            3);
        Assert.Equal(selected, exactExpiry);

        var insufficientEthExpiries = OkxExpiryFuturesResponseParser.SelectNearestExpiries(
            instruments,
            "ETH",
            targetEndUtc,
            3);
        Assert.Single(insufficientEthExpiries);
    }

    [Fact]
    public void ResponseParser_ParsesSelectedFuturesAndIndexTickers()
    {
        var fetchedAtUtc = new DateTimeOffset(2026, 7, 10, 22, 36, 29, TimeSpan.Zero);
        var futuresJson = """
            {"code":"0","msg":"","data":[{"instId":"BTC-USD_UM-260717","bidPx":"64078.4","askPx":"64205.7","ts":"1783722988077"}]}
            """;
        IReadOnlySet<string> instrumentIds = new HashSet<string>(["BTC-USD_UM-260717"], StringComparer.OrdinalIgnoreCase);

        var futuresParsed = OkxExpiryFuturesResponseParser.TryParseFuturesTickers(
            Encoding.UTF8.GetBytes(futuresJson),
            fetchedAtUtc,
            instrumentIds,
            out var futuresTickers,
            out var futuresError);

        Assert.True(futuresParsed, futuresError);
        var futuresTicker = Assert.Single(futuresTickers).Value;
        Assert.Equal(64_078.4m, futuresTicker.BidPriceUsd);
        Assert.Equal(64_205.7m, futuresTicker.AskPriceUsd);
        Assert.Equal(64_142.05m, futuresTicker.MidPriceUsd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_783_722_988_077), futuresTicker.SourceUpdatedAtUtc);
        Assert.Equal(fetchedAtUtc, futuresTicker.FetchedAtUtc);

        var indexJson = """
            {"code":"0","msg":"","data":[{"instId":"BTC-USD","idxPx":"64090.4","ts":"1783722988100"}]}
            """;
        var indexParsed = OkxExpiryFuturesResponseParser.TryParseIndexTicker(
            Encoding.UTF8.GetBytes(indexJson),
            fetchedAtUtc,
            "BTC",
            out var indexTicker,
            out var indexError);

        Assert.True(indexParsed, indexError);
        Assert.NotNull(indexTicker);
        Assert.Equal("BTC-USD", indexTicker.InstrumentId);
        Assert.Equal(64_090.4m, indexTicker.IndexPriceUsd);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_783_722_988_100), indexTicker.SourceUpdatedAtUtc);
    }

    [Fact]
    public void StrategyIds_KeepExistingFuturesBasisVariantsWithThreeExpiryConfirmationDescription()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants
            .Where(item => item.Behavior is BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarket or
                BtcUpDown5mStrategyBehavior.FuturesBasisBpsThresholdFakPremarketRevert)
            .ToArray();

        Assert.Equal(48, variants.Length);
        Assert.Equal(variants.Length, variants.Select(item => item.Id).Distinct().Count());
        Assert.Equal(variants.Length, variants.Select(item => item.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var assetSymbol in new[] { "BTC", "ETH", "SOL" })
        {
            var assetVariants = variants
                .Where(item => string.Equals(item.ReferenceAssetSymbol, assetSymbol, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.DecisionThresholdBps.GetValueOrDefault())
                .ToArray();

            Assert.Equal(
                [1m, 1m, 2m, 2m, 3m, 3m, 5m, 5m, 8m, 8m, 10m, 10m, 15m, 15m, 20m, 20m],
                assetVariants.Select(item => item.DecisionThresholdBps.GetValueOrDefault()).ToArray());
            Assert.All(assetVariants, item =>
            {
                Assert.Equal(-30, item.EntryDelaySeconds);
                Assert.True(
                    item.Category == $"{assetSymbol} Up/Down 5m Bps Futures Basis Premarket" ||
                    item.Category == $"{assetSymbol} Up/Down 5m Bps Futures Basis Revert Premarket");
                Assert.Null(item.FixedOutcome);
                Assert.StartsWith(assetSymbol.ToLowerInvariant() + "_up_down_5m_futures_basis_bps_", item.Code, StringComparison.Ordinal);
                Assert.EndsWith("_fak_premarket", item.Code, StringComparison.Ordinal);
                Assert.Contains("OKX linear USD fixed-expiry", item.Description, StringComparison.Ordinal);
                Assert.Contains("three live OKX linear USD fixed-expiry contracts", item.Description, StringComparison.Ordinal);
                Assert.Contains("closest distinct expiries at or after the target market end", item.Description, StringComparison.Ordinal);
                Assert.Contains("threshold only to the nearest expiry", item.Description, StringComparison.Ordinal);
                Assert.Contains("both following expiries to confirm its nonzero basis sign", item.Description, StringComparison.Ordinal);
                Assert.Contains("never substitute a perpetual contract", item.Description, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void StrategyDisplayCategory_GroupsFuturesBasisPremarketByAsset()
    {
        Assert.Equal(
            "BTC Up or Down 5m Bps Futures Basis Premarket",
            StrategyDisplayCategories.GetCategory("BTC Up or Down 5m 2 bps Futures Basis Premarket"));
        Assert.Equal(
            "ETH Up or Down 5m Bps Futures Basis Premarket",
            StrategyDisplayCategories.GetCategory("ETH Up or Down 5m 15 bps Futures Basis Premarket"));
        Assert.Equal(
            "SOL Up or Down 5m Bps Futures Basis Premarket",
            StrategyDisplayCategories.GetCategory("SOL Up or Down 5m 20 bps Futures Basis Premarket"));
        Assert.Equal(
            "BTC Up or Down 5m Bps Futures Basis Revert Premarket",
            StrategyDisplayCategories.GetCategory("BTC Up or Down 5m 2 bps Futures Basis Revert Premarket"));
    }
}
