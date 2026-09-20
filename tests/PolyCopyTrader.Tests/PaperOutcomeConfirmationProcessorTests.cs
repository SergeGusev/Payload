using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class PaperOutcomeConfirmationProcessorTests
{
    internal static PaperOrder Order() => new(Guid.NewGuid(), Guid.NewGuid(), "wallet", PaperOrderStatus.Filled,
        TradeSide.Buy, "token-up", "condition", "Up", .5m, 12m, 6m,
        DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));

    internal static PolymarketOnChainTokenMetadata Metadata() => new("token-up", "condition", "market", "slug", "title",
        "Up", 0, "crypto", DateTimeOffset.UtcNow.AddHours(-1), false, true, false, true, "Down",
        ["token-up", "token-down"], ["Up", "Down"], true, null,
        """{"umaResolutionStatus":"resolved","outcomePrices":"[\"0\",\"1\"]"}""", DateTimeOffset.UtcNow);

    [Fact]
    public void FinalOutcomeUsesExactMarketAndTokenMapping()
    {
        var result = PaperOutcomeConfirmationProcessor.Resolve(Order(), [Metadata()], DateTimeOffset.UtcNow);
        Assert.NotNull(result);
        Assert.Equal("token-down", result.WinningAssetId);
        Assert.Equal("Down", result.WinningOutcome);
        Assert.Contains("GammaClosedMarket", result.EvidenceJson);
    }

    [Theory]
    [InlineData("unresolved")]
    [InlineData("open")]
    [InlineData("lookup_failed")]
    [InlineData("wrong_condition")]
    [InlineData("wrong_token")]
    [InlineData("wrong_outcome")]
    [InlineData("missing_winner")]
    [InlineData("duplicate_winner")]
    [InlineData("token_mapping")]
    public void MissingOrContradictoryMetadataCannotConfirm(string defect)
    {
        var original = Metadata();
        var metadata = defect switch
        {
            "unresolved" => original with { Resolved = false },
            "open" => original with { Closed = false },
            "lookup_failed" => original with { LookupSucceeded = false },
            "wrong_condition" => original with { ConditionId = "different" },
            "wrong_token" => original with { TokenId = "different" },
            "wrong_outcome" => original with { Outcome = "different" },
            "missing_winner" => original with { WinningOutcome = null },
            "duplicate_winner" => original with { Outcomes = ["Down", "Down"] },
            _ => original with { ClobTokenIds = ["token-up"] }
        };
        Assert.Null(PaperOutcomeConfirmationProcessor.Resolve(Order(), [metadata], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ContradictoryResponseRowsCannotConfirm()
    {
        Assert.Null(PaperOutcomeConfirmationProcessor.Resolve(Order(),
            [Metadata(), Metadata() with { WinningOutcome = "Up" }], DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"umaResolutionStatus\":\"proposed\",\"outcomePrices\":\"[\\\"0\\\",\\\"1\\\"]\"}")]
    [InlineData("{\"umaResolutionStatus\":\"disputed\",\"outcomePrices\":\"[\\\"0\\\",\\\"1\\\"]\"}")]
    [InlineData("{\"umaResolutionStatus\":\"resolved\",\"outcomePrices\":\"[\\\"0.001\\\",\\\"0.999\\\"]\"}")]
    public void ClosedIsNotEnough_FinalOracleEvidenceRequired(string raw)
    {
        Assert.Null(PaperOutcomeConfirmationProcessor.Resolve(Order(), [Metadata() with { RawJson = raw }], DateTimeOffset.UtcNow));
    }
}
