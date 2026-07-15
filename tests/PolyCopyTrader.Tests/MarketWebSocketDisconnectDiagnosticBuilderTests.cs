using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Tests;

public sealed class MarketWebSocketDisconnectDiagnosticBuilderTests
{
    [Fact]
    public void Build_IncludesWebSocketFailureAndConnectionContextWithoutEndpointSecrets()
    {
        const string outerMessage =
            "Request failed for wss://endpoint-user:endpoint-secret@example.test/ws?access_token=query-secret; " +
            "bearer=outer-secret";
        const string innerMessage = "WebSocket authorization failed; token=inner-secret";
        var observedAtUtc = new DateTimeOffset(2026, 7, 15, 9, 0, 30, TimeSpan.Zero);
        var context = new MarketWebSocketDisconnectContext(
            "Exception",
            CriticalCryptoUpDown5mAssetSelector.ComponentName,
            new Uri("wss://endpoint-user:endpoint-secret@example.test/ws?access_token=query-secret"),
            "ReceiveLoop",
            7,
            6,
            18,
            WebSocketState.Aborted,
            null,
            null,
            observedAtUtc.AddSeconds(-30),
            observedAtUtc.AddSeconds(-5),
            observedAtUtc);
        var exception = new InvalidOperationException(
            outerMessage,
            new WebSocketException(WebSocketError.ConnectionClosedPrematurely, innerMessage));

        var diagnostic = MarketWebSocketDisconnectDiagnosticBuilder.Build(context, exception);

        Assert.Contains("Reason=Exception", diagnostic, StringComparison.Ordinal);
        Assert.Contains("EndpointHost=example.test", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Phase=ReceiveLoop", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ConnectionAttempt=7", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ReconnectCountBeforeIncrement=6", diagnostic, StringComparison.Ordinal);
        Assert.Contains("SubscribedAssets=18", diagnostic, StringComparison.Ordinal);
        Assert.Contains("SocketState=Aborted", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ConnectionAgeMs=30000", diagnostic, StringComparison.Ordinal);
        Assert.Contains("LastMessageAgeMs=5000", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=System.InvalidOperationException", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"ExceptionMessageLengthChars={outerMessage.Length}", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"ExceptionMessageSha256={ComputeSha256(outerMessage)}", diagnostic, StringComparison.Ordinal);
        Assert.Contains("WebSocketErrorCode=ConnectionClosedPrematurely", diagnostic, StringComparison.Ordinal);
        Assert.Contains("NativeErrorCode=", diagnostic, StringComparison.Ordinal);
        Assert.Contains("HResult=0x", diagnostic, StringComparison.Ordinal);
        Assert.Contains("InnerExceptionType=System.Net.WebSockets.WebSocketException", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"InnerExceptionMessageLengthChars={innerMessage.Length}", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"InnerExceptionMessageSha256={ComputeSha256(innerMessage)}", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-user", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("outer-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("; ExceptionMessage=", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("; InnerExceptionMessage=", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FingerprintsCloseFrameMetadataWithoutExposingIt()
    {
        const string closeDescription =
            "maintenance; retry\r\nlater at wss://endpoint-user:endpoint-secret@example.test/ws?token=close-secret";
        var observedAtUtc = new DateTimeOffset(2026, 7, 15, 9, 1, 0, TimeSpan.Zero);
        var context = new MarketWebSocketDisconnectContext(
            "CloseFrame",
            "market-ws-test",
            new Uri("wss://example.test/ws"),
            "ReceiveLoop",
            2,
            1,
            3,
            WebSocketState.CloseReceived,
            WebSocketCloseStatus.EndpointUnavailable,
            closeDescription,
            observedAtUtc.AddMinutes(-2),
            null,
            observedAtUtc);

        var diagnostic = MarketWebSocketDisconnectDiagnosticBuilder.Build(context);

        Assert.Contains("Reason=CloseFrame", diagnostic, StringComparison.Ordinal);
        Assert.Contains("SocketState=CloseReceived", diagnostic, StringComparison.Ordinal);
        Assert.Contains("CloseStatus=EndpointUnavailable(1001)", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"CloseDescriptionLengthChars={closeDescription.Length}", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"CloseDescriptionSha256={ComputeSha256(closeDescription)}", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ConnectionAgeMs=120000", diagnostic, StringComparison.Ordinal);
        Assert.Contains("LastMessageUtc=<null>", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=<null>", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseDescription=", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("maintenance", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-user", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("close-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', diagnostic);
        Assert.DoesNotContain('\n', diagnostic);
    }

    [Fact]
    public void Build_FingerprintsImmediateInnerExceptionWithoutExposingIt()
    {
        const string outerMessage = "outer; bearer=outer-secret";
        const string innerMessage = "inner; token=inner-secret";
        var observedAtUtc = new DateTimeOffset(2026, 7, 15, 9, 2, 0, TimeSpan.Zero);
        var context = new MarketWebSocketDisconnectContext(
            "Exception",
            "market-ws-test",
            new Uri("wss://example.test/ws"),
            "Connect",
            1,
            0,
            0,
            WebSocketState.Closed,
            null,
            null,
            null,
            null,
            observedAtUtc);
        var exception = new InvalidOperationException(
            outerMessage,
            new IOException(innerMessage));

        var diagnostic = MarketWebSocketDisconnectDiagnosticBuilder.Build(context, exception);

        Assert.Contains($"ExceptionMessageLengthChars={outerMessage.Length}", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"ExceptionMessageSha256={ComputeSha256(outerMessage)}", diagnostic, StringComparison.Ordinal);
        Assert.Contains("InnerExceptionType=System.IO.IOException", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"InnerExceptionMessageLengthChars={innerMessage.Length}", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"InnerExceptionMessageSha256={ComputeSha256(innerMessage)}", diagnostic, StringComparison.Ordinal);
        Assert.Contains("WebSocketErrorCode=<null>", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("outer-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("inner-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("; ExceptionMessage=", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("; InnerExceptionMessage=", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_AllowsUnavailableEndpointWithoutInventingEndpointDetails()
    {
        var observedAtUtc = new DateTimeOffset(2026, 7, 15, 9, 3, 0, TimeSpan.Zero);
        var context = new MarketWebSocketDisconnectContext(
            "Exception",
            "market-ws-test",
            null,
            "EndpointValidation",
            0,
            0,
            0,
            WebSocketState.None,
            null,
            null,
            null,
            null,
            observedAtUtc);

        var diagnostic = MarketWebSocketDisconnectDiagnosticBuilder.Build(
            context,
            new UriFormatException("wss://endpoint-user:endpoint-secret@example.test/ws?token=query-secret"));

        Assert.Contains("EndpointHost=<null>", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=System.UriFormatException", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-user", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("endpoint-secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_UsesSafeDiagnosticsAndCapturesDisconnectTimeBeforeHeartbeatCleanup()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "MarketData",
            "MarketDataWebSocketShardRunner.cs").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ex.Message", source, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(
            source,
            @"logger\.LogWarning\s*\(\s*ex\s*,",
            RegexOptions.CultureInvariant));
        Assert.Contains(
            "TryRecordApiErrorAsync(\"SubscriptionUpdate\", exceptionDiagnostic",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryRecordApiErrorAsync(\"Heartbeat\", exceptionDiagnostic",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "TryRecordApiErrorAsync(\"ConnectionLoop\", disconnectDiagnostic",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"new MarketDataStatusSnapshot\(\s*Component,\s*state,\s*GetDiagnosticEndpointHost\(\),",
                RegexOptions.CultureInvariant),
            source);
        Assert.Matches(
            new Regex(
                @"new MarketWebSocketCloseFrame\(\s*result\.CloseStatus,\s*result\.CloseStatusDescription,\s*DateTimeOffset\.UtcNow\)",
                RegexOptions.CultureInvariant),
            source);
        Assert.Contains(
            "receiveLoopObservedAtUtc = closeFrame?.ObservedAtUtc ?? DateTimeOffset.UtcNow;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "var reconnectCountBeforeIncrement = reconnectCount;\n                reconnectCount++;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DateTimeOffset.UtcNow,\n                        reconnectCountBeforeIncrement);",
            source,
            StringComparison.Ordinal);

        var failureObservedIndex = source.IndexOf(
            "failureObservedAtUtc = DateTimeOffset.UtcNow;",
            StringComparison.Ordinal);
        Assert.True(failureObservedIndex >= 0);
        var heartbeatCleanupIndex = source.IndexOf(
            "await connectionCts.CancelAsync();",
            failureObservedIndex,
            StringComparison.Ordinal);
        Assert.True(heartbeatCleanupIndex > failureObservedIndex);

        var guardedGetterIndex = source.IndexOf(
            "private static string? TryGetDisconnectDiagnostic",
            StringComparison.Ordinal);
        Assert.True(guardedGetterIndex >= 0);
        var guardedGetterEndIndex = source.IndexOf(
            "private static void TrySetDisconnectDiagnostic",
            guardedGetterIndex,
            StringComparison.Ordinal);
        Assert.True(guardedGetterEndIndex > guardedGetterIndex);
        var guardedGetter = source[guardedGetterIndex..guardedGetterEndIndex];
        Assert.Contains("try", guardedGetter, StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", guardedGetter, StringComparison.Ordinal);
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string ReadRepositorySource(params string[] segments)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            Path.Combine(segments)));
        return File.ReadAllText(path);
    }
}
