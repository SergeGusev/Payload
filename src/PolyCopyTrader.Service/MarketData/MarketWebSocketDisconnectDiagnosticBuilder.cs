using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace PolyCopyTrader.Service.MarketData;

internal sealed record MarketWebSocketDisconnectContext(
    string Reason,
    string Component,
    Uri? Endpoint,
    string Phase,
    int ConnectionAttempt,
    int ReconnectCountBeforeIncrement,
    int SubscribedAssetsCount,
    WebSocketState SocketState,
    WebSocketCloseStatus? CloseStatus,
    string? CloseStatusDescription,
    DateTimeOffset? ConnectedAtUtc,
    DateTimeOffset? LastMessageUtc,
    DateTimeOffset ObservedAtUtc);

internal static class MarketWebSocketDisconnectDiagnosticBuilder
{
    private const int MaxTextValueLength = 512;

    public static string Build(
        MarketWebSocketDisconnectContext context,
        Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var webSocketException = FindWebSocketException(exception);
        var fields = new List<string>
        {
            Field("Reason", context.Reason),
            Field("Component", context.Component),
            Field("EndpointHost", context.Endpoint?.Host),
            Field("Phase", context.Phase),
            Field("ConnectionAttempt", context.ConnectionAttempt),
            Field("ReconnectCountBeforeIncrement", context.ReconnectCountBeforeIncrement),
            Field("SubscribedAssets", context.SubscribedAssetsCount),
            Field("SocketState", context.SocketState),
            Field("CloseStatus", FormatCloseStatus(context.CloseStatus)),
            Field("CloseDescriptionLengthChars", GetTextLength(context.CloseStatusDescription)),
            Field("CloseDescriptionSha256", ComputeTextFingerprint(context.CloseStatusDescription)),
            Field("ConnectedAtUtc", FormatTimestamp(context.ConnectedAtUtc)),
            Field("ConnectionAgeMs", FormatAgeMilliseconds(context.ConnectedAtUtc, context.ObservedAtUtc)),
            Field("LastMessageUtc", FormatTimestamp(context.LastMessageUtc)),
            Field("LastMessageAgeMs", FormatAgeMilliseconds(context.LastMessageUtc, context.ObservedAtUtc)),
            Field("ObservedAtUtc", context.ObservedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
        };
        AddExceptionFields(fields, exception, webSocketException);

        return string.Join("; ", fields);
    }

    private static void AddExceptionFields(
        List<string> fields,
        Exception? exception,
        WebSocketException? webSocketException)
    {
        fields.Add(Field("ExceptionType", exception?.GetType().FullName));
        fields.Add(Field("ExceptionMessageLengthChars", GetTextLength(exception?.Message)));
        fields.Add(Field("ExceptionMessageSha256", ComputeTextFingerprint(exception?.Message)));
        fields.Add(Field("WebSocketErrorCode", webSocketException?.WebSocketErrorCode));
        fields.Add(Field("NativeErrorCode", webSocketException?.NativeErrorCode));
        fields.Add(Field("HResult", exception is null ? null : $"0x{exception.HResult:X8}"));
        fields.Add(Field("InnerExceptionType", exception?.InnerException?.GetType().FullName));
        fields.Add(Field("InnerExceptionMessageLengthChars", GetTextLength(exception?.InnerException?.Message)));
        fields.Add(Field("InnerExceptionMessageSha256", ComputeTextFingerprint(exception?.InnerException?.Message)));
    }

    private static WebSocketException? FindWebSocketException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is WebSocketException webSocketException)
            {
                return webSocketException;
            }

            exception = exception.InnerException;
        }

        return null;
    }

    private static string Field(string name, object? value)
    {
        return $"{name}={Sanitize(value?.ToString())}";
    }

    private static string FormatCloseStatus(WebSocketCloseStatus? closeStatus)
    {
        return closeStatus is null
            ? "<null>"
            : $"{closeStatus.Value}({(int)closeStatus.Value})";
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "<null>"
            : timestamp.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string FormatAgeMilliseconds(DateTimeOffset? timestamp, DateTimeOffset observedAtUtc)
    {
        if (timestamp is null)
        {
            return "<null>";
        }

        var age = Math.Max(0d, (observedAtUtc - timestamp.Value).TotalMilliseconds);
        return age.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static int? GetTextLength(string? value)
    {
        return value?.Length;
    }

    private static string? ComputeTextFingerprint(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<null>";
        }

        var builder = new StringBuilder(Math.Min(value.Length, MaxTextValueLength));
        foreach (var character in value.Trim())
        {
            builder.Append(character is '\r' or '\n' or ';' ? ' ' : character);
            if (builder.Length >= MaxTextValueLength)
            {
                break;
            }
        }

        return builder.ToString();
    }
}
