using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdLivePlacementClassifierTests
{
    [Fact]
    public void FreezeOrderIdentity_MakesRepeatedBuildsUseSamePreSubmitSalt()
    {
        var builder = new ClobV2OrderBuilder(new OrderAmountCalculator());
        var request = new ClobV2OrderRequest(
            "1234",
            TradeSide.Buy,
            0.48m,
            10m,
            0.01m,
            5m,
            "0x1111111111111111111111111111111111111111",
            "0x1111111111111111111111111111111111111111",
            ClobV2SignatureType.EOA,
            ClobV2OrderType.GTD,
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 9, 12, 5, 0, TimeSpan.Zero),
            PostOnly: true);

        var frozen = ClobV2OrderBuilder.FreezeOrderIdentity(request);
        var first = builder.Build(frozen);
        var second = builder.Build(frozen);

        Assert.Null(request.Salt);
        Assert.False(string.IsNullOrWhiteSpace(frozen.Salt));
        Assert.Equal(frozen.Salt, first.Salt);
        Assert.Equal(first.Salt, second.Salt);
    }

    [Fact]
    public void Classify_AcceptsOnlySuccessfulLiveResponseWithOrderId()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: true,
            orderId: " 0xorder ",
            responseStatus: " LIVE ",
            httpStatusCode: 200));

        Assert.Equal(MakerGtdLivePlacementDisposition.AcceptedResting, classification.Disposition);
        Assert.Equal("0xorder", classification.OrderId);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.False(classification.RequiresReconciliation);
    }

    [Fact]
    public void Classify_LiveResponseWithoutOrderIdIsAmbiguous()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: true,
            orderId: null,
            responseStatus: "live",
            httpStatusCode: 200));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcileAmbiguous, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData("INVALID_POST_ONLY_ORDER")]
    [InlineData("HTTP 400: INVALID_POST_ONLY_ORDER")]
    [InlineData("invalid post-only order: order crosses book")]
    public void Classify_RetriesOnlyAllowlistedPostOnlyCrossingRejections(string error)
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: "BadRequest",
            errorMessage: error,
            httpStatusCode: 400));

        Assert.Equal(MakerGtdLivePlacementDisposition.RetryNewIntent, classification.Disposition);
        Assert.True(classification.CanSubmitNewIntent);
        Assert.False(classification.RequiresReconciliation);
    }

    [Fact]
    public void Classify_DoesNotRetryCrossingResponseThatAlsoContainsOrderId()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: "0xunexpected",
            responseStatus: "BadRequest",
            errorMessage: "INVALID_POST_ONLY_ORDER",
            httpStatusCode: 400));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcilePending, classification.Disposition);
        Assert.Equal("0xunexpected", classification.OrderId);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Fact]
    public void Classify_DoesNotRetryGenericCrossingTextOutsideAllowlist()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: "BadRequest",
            errorMessage: "order crosses book",
            httpStatusCode: 400));

        Assert.Equal(MakerGtdLivePlacementDisposition.TerminalFailure, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
    }

    [Fact]
    public void Classify_DoesNotRetryWhenOfficialCodeIsOnlyAnEmbeddedSubstring()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: "BadRequest",
            errorMessage: "NOT_INVALID_POST_ONLY_ORDER_OVERRIDE",
            httpStatusCode: 400));

        Assert.Equal(MakerGtdLivePlacementDisposition.TerminalFailure, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
    }

    [Fact]
    public void Classify_PostOnlyMatchedStatusIsInvariantViolation()
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: true,
            orderId: "0xmatched",
            responseStatus: "matched",
            httpStatusCode: 200));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcileInvariantViolation, classification.Disposition);
        Assert.Equal("0xmatched", classification.OrderId);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData("delayed")]
    [InlineData("unmatched")]
    [InlineData("unknown_future_status")]
    public void Classify_AnyNonAcceptedStatusWithOrderIdRequiresReconciliation(string responseStatus)
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: true,
            orderId: "0xpending",
            responseStatus: responseStatus,
            httpStatusCode: 200));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcilePending, classification.Disposition);
        Assert.Equal("0xpending", classification.OrderId);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Classify_TransientHttpResponseWithoutOrderIdIsAmbiguous(int statusCode)
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: statusCode.ToString(),
            httpStatusCode: statusCode));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcileAmbiguous, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData("DUPLICATED_ORDER")]
    [InlineData("duplicated order")]
    [InlineData("order already exists")]
    public void Classify_DuplicateOrderWithoutReturnedIdRequiresReconciliation(string error)
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: "Conflict",
            errorMessage: error,
            httpStatusCode: 409));

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcilePending, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData(400, "not enough balance / allowance")]
    [InlineData(401, "invalid api key")]
    [InlineData(403, "address is banned")]
    [InlineData(400, "market is closed")]
    [InlineData(422, "invalid tick size")]
    public void Classify_DefinitiveNonCrossingClientRejectionIsTerminal(int statusCode, string error)
    {
        var classification = MakerGtdLivePlacementClassifier.Classify(Result(
            success: false,
            orderId: null,
            responseStatus: statusCode.ToString(),
            errorMessage: error,
            httpStatusCode: statusCode));

        Assert.Equal(MakerGtdLivePlacementDisposition.TerminalFailure, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.False(classification.RequiresReconciliation);
    }

    [Theory]
    [MemberData(nameof(AmbiguousSubmissionExceptions))]
    public void ClassifyThrownFailure_TransportOrUncertainResponseFailureIsAmbiguous(Exception exception)
    {
        var classification = MakerGtdLivePlacementClassifier.ClassifyThrownFailure(exception);

        Assert.Equal(MakerGtdLivePlacementDisposition.ReconcileAmbiguous, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.True(classification.RequiresReconciliation);
    }

    [Theory]
    [InlineData("GTD expiration must be more than 60 seconds after order creation.")]
    [InlineData("order signing private key secret reference is not configured.")]
    [InlineData("Order signing private key does not match the configured signer address.")]
    public void ClassifyThrownFailure_KnownPreSubmitConfigurationFailureIsTerminal(string message)
    {
        Exception exception = message.StartsWith("GTD", StringComparison.Ordinal)
            ? new ArgumentException(message)
            : new InvalidOperationException(message);

        var classification = MakerGtdLivePlacementClassifier.ClassifyThrownFailure(exception);

        Assert.Equal(MakerGtdLivePlacementDisposition.TerminalFailure, classification.Disposition);
        Assert.False(classification.CanSubmitNewIntent);
        Assert.False(classification.RequiresReconciliation);
    }

    public static TheoryData<Exception> AmbiguousSubmissionExceptions => new()
    {
        new HttpRequestException("connection reset"),
        new TimeoutException("timed out"),
        new TaskCanceledException("request canceled"),
        new JsonException("malformed success response"),
        new Exception("unknown submission failure")
    };

    private static LiveOrderPlacementResult Result(
        bool success,
        string? orderId,
        string responseStatus,
        string? errorMessage = null,
        int? httpStatusCode = null)
    {
        return new LiveOrderPlacementResult(
            success,
            orderId,
            responseStatus,
            errorMessage,
            null,
            null,
            "{}",
            "{}",
            httpStatusCode);
    }
}
