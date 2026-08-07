using System.Net;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;

namespace PolyCopyTrader.Tests;

public sealed class PolymarketFeeCalculatorTests
{
    private const string ConditionId =
        "0x206d197a65562d14262aebfce01c7d4ae307311c223d462885a7aad75d543c5f";

    [Fact]
    public void ParseClobMarketInfo_ReadsCompressedFeeFields()
    {
        using var json = JsonDocument.Parse(CurrentMarketInfoJson);

        var result = PolymarketJsonParser.ParseClobMarketInfo(json.RootElement, ConditionId);

        Assert.Equal(ConditionId, result.ConditionId);
        Assert.Equal(1000L, result.MakerBaseFeeBps);
        Assert.Equal(1000L, result.TakerBaseFeeBps);
        Assert.NotNull(result.FeeSchedule);
        Assert.Equal(0.07m, result.FeeSchedule.Rate);
        Assert.Equal(1, result.FeeSchedule.Exponent);
        Assert.True(result.FeeSchedule.TakerOnly);
        Assert.Contains("\"fd\"", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseClobMarketInfo_UsesRequestedConditionIdWhenResponseOmitsIt()
    {
        using var json = JsonDocument.Parse("""{"mbf":0,"tbf":0}""");

        var result = PolymarketJsonParser.ParseClobMarketInfo(json.RootElement, ConditionId);

        Assert.Equal(ConditionId, result.ConditionId);
        Assert.Null(result.FeeSchedule);
    }

    [Fact]
    public void ParseClobMarketInfo_PreservesInvalidFeeScheduleAsUnavailableInput()
    {
        using var json = JsonDocument.Parse("""{"mbf":1000,"tbf":1000,"fd":{"r":0.07,"e":1.5,"to":true}}""");

        var marketInfo = PolymarketJsonParser.ParseClobMarketInfo(json.RootElement, ConditionId);
        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            10m,
            0.5m,
            FeeLiquidityRole.Taker,
            marketInfo);

        Assert.NotNull(marketInfo.FeeSchedule);
        Assert.Null(marketInfo.FeeSchedule.Exponent);
        Assert.Equal(FeeAccountingStatus.CalculationUnavailable, result.Status);
        Assert.Null(result.FeeUsd);
        Assert.Contains("non-integer", result.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseClobMarketInfo_RejectsMismatchedConditionId()
    {
        using var json = JsonDocument.Parse("""{"c":"0xdifferent","mbf":0,"tbf":0}""");

        Assert.Throws<JsonException>(() =>
            PolymarketJsonParser.ParseClobMarketInfo(json.RootElement, ConditionId));
    }

    [Fact]
    public async Task ClobClient_ReadsClobMarketInfoEndpoint()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(CurrentMarketInfoJson)
        });
        var client = new PolymarketClobPublicClient(
            new HttpClient(handler),
            TestOptions,
            new NullPolymarketApiErrorSink());

        var result = await client.GetClobMarketInfoAsync(ConditionId);

        Assert.Equal(ConditionId, result.ConditionId);
        Assert.Equal(0.07m, result.FeeSchedule?.Rate);
        Assert.Equal(
            $"https://clob.polymarket.com/clob-markets/{ConditionId}",
            handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public void CalculatePlatformFee_AppliesDocumentedExponentCurve()
    {
        var marketInfo = MarketInfo(rate: 0.02m, exponent: 2, takerOnly: true);

        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Taker,
            marketInfo);

        Assert.Equal(FeeAccountingStatus.Calculated, result.Status);
        Assert.Equal(0.12500m, result.FeeUsd);
        Assert.Equal(0.125m, result.UnroundedFeeUsd);
        Assert.Equal(
            PolymarketFeeCalculationConstants.FeeCurveCalculationSource,
            result.CalculationSource);
    }

    [Fact]
    public void CalculatePlatformFee_ReturnsZeroForMakerOnTakerOnlySchedule()
    {
        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Maker,
            MarketInfo(rate: 0.07m, exponent: 1, takerOnly: true));

        Assert.Equal(FeeAccountingStatus.Calculated, result.Status);
        Assert.Equal(0m, result.FeeUsd);
    }

    [Fact]
    public void CalculatePlatformFee_ReturnsZeroWhenFeeDetailsAreAbsentAndBaseFeesAreExplicitlyZero()
    {
        var marketInfo = new PolymarketClobMarketInfo(ConditionId, 0L, 0L, null, "{}");

        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Unknown,
            marketInfo);

        Assert.Equal(FeeAccountingStatus.Calculated, result.Status);
        Assert.Equal(0m, result.FeeUsd);
        Assert.Equal(
            PolymarketFeeCalculationConstants.FeeFreeMarketCalculationSource,
            result.CalculationSource);
    }

    [Fact]
    public void CalculatePlatformFee_DoesNotGuessWhenFeeDetailsOrBaseFeeEvidenceIsMissing()
    {
        var marketInfo = new PolymarketClobMarketInfo(ConditionId, null, 0L, null, "{}");

        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Unknown,
            marketInfo);

        Assert.Equal(FeeAccountingStatus.CalculationUnavailable, result.Status);
        Assert.Null(result.FeeUsd);
    }

    [Fact]
    public void CalculatePlatformFee_DoesNotGuessWhenFeeDetailsAreAbsentButBaseFeeIsNonZero()
    {
        var marketInfo = new PolymarketClobMarketInfo(ConditionId, 0L, 1000L, null, "{}");

        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Taker,
            marketInfo);

        Assert.Equal(FeeAccountingStatus.CalculationUnavailable, result.Status);
        Assert.Null(result.FeeUsd);
    }

    [Fact]
    public void CalculatePlatformFee_DoesNotGuessUnknownLiquidityRole()
    {
        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            100m,
            0.5m,
            FeeLiquidityRole.Unknown,
            MarketInfo(rate: 0.07m, exponent: 1, takerOnly: true));

        Assert.Equal(FeeAccountingStatus.CalculationUnavailable, result.Status);
        Assert.Null(result.FeeUsd);
    }

    [Fact]
    public void CalculatePlatformFee_UsesVersionedFiveDecimalAwayFromZeroModel()
    {
        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            1m,
            0.5m,
            FeeLiquidityRole.Taker,
            MarketInfo(rate: 0.00002m, exponent: 1, takerOnly: true));

        Assert.Equal(0.000005m, result.UnroundedFeeUsd);
        Assert.Equal(PolymarketFeeCalculationConstants.MinimumNonZeroFeeUsd, result.FeeUsd);
        Assert.Contains("round5-away-from-zero-v1", result.CalculationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculatePlatformFee_RoundsSubMinimumValueToZero()
    {
        var result = PolymarketFeeCalculator.CalculatePlatformFee(
            0.1m,
            0.5m,
            FeeLiquidityRole.Taker,
            MarketInfo(rate: 0.00002m, exponent: 1, takerOnly: true));

        Assert.Equal(0.0000005m, result.UnroundedFeeUsd);
        Assert.Equal(0m, result.FeeUsd);
    }

    private static PolymarketClobMarketInfo MarketInfo(
        decimal? rate,
        int? exponent,
        bool? takerOnly)
    {
        return new PolymarketClobMarketInfo(
            ConditionId,
            1000L,
            1000L,
            new PolymarketClobFeeSchedule(rate, exponent, takerOnly),
            "{}");
    }

    private static PolymarketOptions TestOptions => new()
    {
        ClobBaseUrl = "https://clob.polymarket.com",
        MaxRetries = 0,
        RetryBaseDelayMilliseconds = 0
    };

    private const string CurrentMarketInfoJson =
        """
        {
          "c":"0x206d197a65562d14262aebfce01c7d4ae307311c223d462885a7aad75d543c5f",
          "mbf":1000,
          "tbf":1000,
          "fd":{"r":0.07,"e":1,"to":true}
        }
        """;

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
