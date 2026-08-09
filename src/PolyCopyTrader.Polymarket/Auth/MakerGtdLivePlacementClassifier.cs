using System.Text.Json;

namespace PolyCopyTrader.Polymarket.Auth;

public enum MakerGtdLivePlacementDisposition
{
    AcceptedResting,
    RetryNewIntent,
    ReconcileInvariantViolation,
    ReconcilePending,
    ReconcileAmbiguous,
    TerminalFailure
}

public sealed record MakerGtdLivePlacementClassification(
    MakerGtdLivePlacementDisposition Disposition,
    string ReasonCode,
    string? OrderId)
{
    public bool CanSubmitNewIntent => Disposition == MakerGtdLivePlacementDisposition.RetryNewIntent;

    public bool RequiresReconciliation => Disposition is
        MakerGtdLivePlacementDisposition.ReconcileInvariantViolation or
        MakerGtdLivePlacementDisposition.ReconcilePending or
        MakerGtdLivePlacementDisposition.ReconcileAmbiguous;
}

public static class MakerGtdLivePlacementClassifier
{
    public const string AcceptedRestingReason = "maker_gtd_live_accepted_resting";
    public const string PostOnlyWouldCrossReason = "maker_gtd_live_post_only_would_cross";
    public const string PostOnlyMatchedInvariantReason = "maker_gtd_live_post_only_matched_invariant";
    public const string PendingReconciliationReason = "maker_gtd_live_pending_reconciliation";
    public const string AmbiguousSubmissionReason = "maker_gtd_live_ambiguous_submission";
    public const string TerminalFailureReason = "maker_gtd_live_terminal_failure";

    private const string InvalidPostOnlyOrderCode = "INVALID_POST_ONLY_ORDER";
    private const string InvalidPostOnlyCrossingMessage = "invalid post-only order: order crosses book";

    private static readonly string[] DuplicateOrderErrors =
    [
        "DUPLICATED_ORDER",
        "duplicated order",
        "duplicate order",
        "order already exists"
    ];

    private static readonly string[] AmbiguousResponseStatuses =
    [
        "RequestTimeout",
        "TooEarly",
        "TooManyRequests",
        "InternalServerError",
        "BadGateway",
        "ServiceUnavailable",
        "GatewayTimeout"
    ];

    private static readonly string[] KnownPreSubmitConfigurationFailures =
    [
        "secret reference is not configured",
        "is unavailable from the configured secret provider",
        "private key does not match the configured signer address",
        "signature verification failed"
    ];

    public static MakerGtdLivePlacementClassification Classify(LiveOrderPlacementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var orderId = Normalize(result.OrderId);
        var responseStatus = Normalize(result.ResponseStatus) ?? string.Empty;
        var error = result.ErrorMessage ?? string.Empty;

        if (responseStatus.Equals("matched", StringComparison.OrdinalIgnoreCase))
        {
            return ReconcileInvariant(orderId);
        }

        if (result.Success &&
            orderId is not null &&
            responseStatus.Equals("live", StringComparison.OrdinalIgnoreCase))
        {
            return Accepted(orderId);
        }

        // Any venue order identifier proves that blind replacement can create two resting orders.
        if (orderId is not null)
        {
            return ReconcilePending(orderId);
        }

        // A transport/server result can be uncertain even when its body resembles a rejection.
        if (IsAmbiguousHttpStatus(result.HttpStatusCode) ||
            IsAmbiguousResponseStatus(responseStatus))
        {
            return ReconcileAmbiguous();
        }

        // A successful HTTP response without an order identifier cannot prove rejection or acceptance.
        if (result.Success || IsSuccessfulHttpStatus(result.HttpStatusCode))
        {
            return ReconcileAmbiguous();
        }

        var venueEvidence = responseStatus + "\n" + error;
        if (ContainsAllowlisted(venueEvidence, DuplicateOrderErrors))
        {
            return ReconcilePending(null);
        }

        if (IsDefinitivePostOnlyCrossing(responseStatus, error))
        {
            return RetryNewIntent();
        }

        return Terminal();
    }

    public static MakerGtdLivePlacementClassification ClassifyThrownFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ArgumentException ||
            exception is InvalidOperationException &&
            ContainsAllowlisted(exception.Message, KnownPreSubmitConfigurationFailures))
        {
            return Terminal();
        }

        if (exception is HttpRequestException or
            TimeoutException or
            OperationCanceledException or
            JsonException)
        {
            return ReconcileAmbiguous();
        }

        // Unknown failures are deliberately fail-closed because the caller cannot prove
        // whether the HTTP request reached the venue.
        return ReconcileAmbiguous();
    }

    private static MakerGtdLivePlacementClassification Accepted(string orderId)
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.AcceptedResting,
            AcceptedRestingReason,
            orderId);
    }

    private static MakerGtdLivePlacementClassification RetryNewIntent()
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.RetryNewIntent,
            PostOnlyWouldCrossReason,
            null);
    }

    private static MakerGtdLivePlacementClassification ReconcileInvariant(string? orderId)
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.ReconcileInvariantViolation,
            PostOnlyMatchedInvariantReason,
            orderId);
    }

    private static MakerGtdLivePlacementClassification ReconcilePending(string? orderId)
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.ReconcilePending,
            PendingReconciliationReason,
            orderId);
    }

    private static MakerGtdLivePlacementClassification ReconcileAmbiguous()
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.ReconcileAmbiguous,
            AmbiguousSubmissionReason,
            null);
    }

    private static MakerGtdLivePlacementClassification Terminal()
    {
        return new MakerGtdLivePlacementClassification(
            MakerGtdLivePlacementDisposition.TerminalFailure,
            TerminalFailureReason,
            null);
    }

    private static bool ContainsAllowlisted(string value, IEnumerable<string> allowlist)
    {
        return allowlist.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDefinitivePostOnlyCrossing(string responseStatus, string error)
    {
        var venueError = StripHttpStatusPrefix(error);
        return IsExactOrPrefixedErrorCode(responseStatus, InvalidPostOnlyOrderCode) ||
            IsExactOrPrefixedErrorCode(venueError, InvalidPostOnlyOrderCode) ||
            venueError.Contains(InvalidPostOnlyCrossingMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactOrPrefixedErrorCode(string value, string code)
    {
        var normalized = value.Trim();
        return normalized.Equals(code, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(code + ":", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(code + " ", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripHttpStatusPrefix(string value)
    {
        var normalized = value.Trim();
        if (!normalized.StartsWith("HTTP ", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var colonIndex = normalized.IndexOf(':');
        if (colonIndex <= 5 ||
            !int.TryParse(normalized.AsSpan(5, colonIndex - 5), out _))
        {
            return normalized;
        }

        return normalized[(colonIndex + 1)..].TrimStart();
    }

    private static bool IsAmbiguousHttpStatus(int? statusCode)
    {
        return statusCode is 408 or 425 or 429 || statusCode is >= 500 and <= 599;
    }

    private static bool IsSuccessfulHttpStatus(int? statusCode)
    {
        return statusCode is >= 200 and <= 299;
    }

    private static bool IsAmbiguousResponseStatus(string responseStatus)
    {
        if (int.TryParse(responseStatus, out var numericStatus))
        {
            return IsAmbiguousHttpStatus(numericStatus);
        }

        return AmbiguousResponseStatuses.Any(candidate =>
            responseStatus.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
