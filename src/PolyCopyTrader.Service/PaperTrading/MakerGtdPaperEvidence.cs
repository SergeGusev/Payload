using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

internal sealed record MakerGtdPaperAcceptedMarketDataStatus(
    MarketDataConnectionState ConnectionState,
    bool Stale,
    int ReconnectCount,
    DateTimeOffset? LastConnectedUtc,
    DateTimeOffset? LastDisconnectedUtc,
    bool AssetSubscribed,
    int SubscribedAssetsCount,
    DateTimeOffset AcceptedAtUtc);

internal sealed record MakerGtdPaperOrderEvidence(
    DateTimeOffset AcceptedAtUtc,
    DateTimeOffset EffectiveExpiresAtUtc,
    MakerGtdPaperAcceptedMarketDataStatus AcceptedMarketDataStatus);

internal sealed record MakerGtdPaperContinuityEvaluation(
    bool Continuous,
    string ReasonCode,
    string Detail);

internal static class MakerGtdPaperOrderEvidenceParser
{
    private const string MakerGtdProperty = "maker_gtd";
    private const string MarketDataStatusProperty = "market_data_status_at_acceptance";
    private const long PostgreSqlTimestampLowerBoundToleranceTicks = 5;

    public static bool TryParse(
        PaperOrder order,
        out MakerGtdPaperOrderEvidence? evidence,
        out string failureDetail)
    {
        evidence = null;
        failureDetail = string.Empty;
        if (!MakerGtdPaperExecutionContract.IsMakerGtdOrder(order))
        {
            failureDetail = "execution_source_mismatch";
            return false;
        }

        if (string.IsNullOrWhiteSpace(order.RawDecisionJson))
        {
            failureDetail = "raw_decision_json_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(order.RawDecisionJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failureDetail = "raw_decision_json_not_object";
                return false;
            }

            if (!TryGetObject(root, MakerGtdProperty, out var makerGtd))
            {
                failureDetail = "maker_gtd_missing";
                return false;
            }

            if (!TryGetRequiredTimestamp(makerGtd, "accepted_at_utc", out var acceptedAtUtc) ||
                !TryGetRequiredTimestamp(
                    makerGtd,
                    "effective_expires_at_utc",
                    out var effectiveExpiresAtUtc))
            {
                failureDetail = "maker_gtd_lifetime_invalid";
                return false;
            }

            if (!TryGetObject(root, MarketDataStatusProperty, out var statusRoot) ||
                !TryGetRequiredEnum(
                    statusRoot,
                    "connection_state",
                    out MarketDataConnectionState connectionState) ||
                !TryGetRequiredBoolean(statusRoot, "stale", out var stale) ||
                !TryGetRequiredNonNegativeInt32(
                    statusRoot,
                    "reconnect_count",
                    out var reconnectCount) ||
                !TryGetOptionalTimestamp(
                    statusRoot,
                    "last_connected_utc",
                    out var lastConnectedUtc) ||
                !TryGetOptionalTimestamp(
                    statusRoot,
                    "last_disconnected_utc",
                    out var lastDisconnectedUtc) ||
                !TryGetRequiredBoolean(
                    statusRoot,
                    "asset_subscribed",
                    out var assetSubscribed) ||
                !TryGetRequiredNonNegativeInt32(
                    statusRoot,
                    "subscribed_assets_count",
                    out var subscribedAssetsCount) ||
                !TryGetRequiredTimestamp(
                    statusRoot,
                    "accepted_at_utc",
                    out var statusAcceptedAtUtc))
            {
                failureDetail = "acceptance_market_data_status_invalid";
                return false;
            }

            if (!SameTimestamp(acceptedAtUtc, statusAcceptedAtUtc))
            {
                failureDetail = "acceptance_timestamp_mismatch";
                return false;
            }

            if (!SameTimestamp(effectiveExpiresAtUtc, order.ExpiresAtUtc) ||
                IsEarlierThanPersistedTimestampLowerBound(acceptedAtUtc, order.CreatedAtUtc) ||
                acceptedAtUtc >= effectiveExpiresAtUtc)
            {
                failureDetail = "order_lifetime_mismatch";
                return false;
            }

            evidence = new MakerGtdPaperOrderEvidence(
                acceptedAtUtc,
                effectiveExpiresAtUtc,
                new MakerGtdPaperAcceptedMarketDataStatus(
                    connectionState,
                    stale,
                    reconnectCount,
                    lastConnectedUtc,
                    lastDisconnectedUtc,
                    assetSubscribed,
                    subscribedAssetsCount,
                    statusAcceptedAtUtc));
            return true;
        }
        catch (JsonException)
        {
            failureDetail = "raw_decision_json_invalid";
            return false;
        }
    }

    private static bool TryGetObject(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        return root.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetRequiredTimestamp(
        JsonElement root,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            element.TryGetDateTimeOffset(out value);
    }

    private static bool TryGetOptionalTimestamp(
        JsonElement root,
        string propertyName,
        out DateTimeOffset? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String ||
            !element.TryGetDateTimeOffset(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetRequiredBoolean(
        JsonElement root,
        string propertyName,
        out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetRequiredNonNegativeInt32(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value >= 0;
    }

    private static bool TryGetRequiredEnum<TEnum>(
        JsonElement root,
        string propertyName,
        out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        return root.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: false, out value) &&
            Enum.IsDefined(value);
    }

    private static bool SameTimestamp(DateTimeOffset left, DateTimeOffset right)
    {
        return left.UtcDateTime.Ticks / 10 == right.UtcDateTime.Ticks / 10;
    }

    private static bool IsEarlierThanPersistedTimestampLowerBound(
        DateTimeOffset timestamp,
        DateTimeOffset persistedTimestamp)
    {
        return timestamp < persistedTimestamp &&
            persistedTimestamp.UtcDateTime.Ticks - timestamp.UtcDateTime.Ticks >
            PostgreSqlTimestampLowerBoundToleranceTicks;
    }
}

internal static class MakerGtdPaperContinuityEvaluator
{
    public static MakerGtdPaperContinuityEvaluation Evaluate(
        PaperOrder order,
        MarketDataStatusSnapshot currentStatus,
        IReadOnlyCollection<string> currentSubscribedAssetIds)
    {
        if (!MakerGtdPaperOrderEvidenceParser.TryParse(
                order,
                out var orderEvidence,
                out var failureDetail) ||
            orderEvidence is null)
        {
            return Unavailable(failureDetail);
        }

        var acceptedStatus = orderEvidence.AcceptedMarketDataStatus;
        if (acceptedStatus.ConnectionState != MarketDataConnectionState.Connected ||
            acceptedStatus.Stale)
        {
            return Unavailable("acceptance_connection_not_healthy");
        }

        if (!acceptedStatus.AssetSubscribed || acceptedStatus.SubscribedAssetsCount <= 0)
        {
            return Unavailable("asset_not_subscribed_at_acceptance");
        }

        if (!currentSubscribedAssetIds.Contains(order.AssetId, StringComparer.Ordinal))
        {
            return Unavailable("asset_not_currently_subscribed");
        }

        if (currentStatus.ConnectionState != MarketDataConnectionState.Connected ||
            currentStatus.Stale)
        {
            return Unavailable("current_connection_not_healthy");
        }

        if (currentStatus.ReconnectCount < 0 ||
            currentStatus.ReconnectCount != acceptedStatus.ReconnectCount)
        {
            return Unavailable("reconnect_count_changed");
        }

        if (acceptedStatus.LastConnectedUtc is not { } acceptedLastConnectedUtc ||
            acceptedLastConnectedUtc > order.CreatedAtUtc ||
            acceptedStatus.LastDisconnectedUtc is { } acceptedLastDisconnectedUtc &&
            acceptedLastDisconnectedUtc > order.CreatedAtUtc)
        {
            return Unavailable("acceptance_connection_timeline_invalid");
        }

        if (currentStatus.LastConnectedUtc is not { } currentLastConnectedUtc ||
            currentLastConnectedUtc > order.CreatedAtUtc ||
            currentStatus.LastDisconnectedUtc is { } currentLastDisconnectedUtc &&
            currentLastDisconnectedUtc > order.CreatedAtUtc)
        {
            return Unavailable("current_connection_timeline_changed");
        }

        return new MakerGtdPaperContinuityEvaluation(
            Continuous: true,
            MakerGtdPaperExecutionContract.ExpiredUnfilledReasonCode,
            "continuous_market_websocket_evidence");
    }

    private static MakerGtdPaperContinuityEvaluation Unavailable(string detail)
    {
        return new MakerGtdPaperContinuityEvaluation(
            Continuous: false,
            MakerGtdPaperExecutionContract.EvidenceUnavailableReasonCode,
            string.IsNullOrWhiteSpace(detail) ? "continuity_evidence_missing" : detail);
    }
}
