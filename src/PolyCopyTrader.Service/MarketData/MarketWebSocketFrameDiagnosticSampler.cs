using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.MarketData;

public sealed record MarketWebSocketFrameDiagnosticSamplingDecision(
    bool ShouldCapture,
    bool Important,
    string Reason);

public sealed class MarketWebSocketFrameDiagnosticSampler(MarketDataWebSocketOptions options)
{
    private long ordinaryFrameSequence;

    public MarketWebSocketFrameDiagnosticSamplingDecision Evaluate(
        string message,
        IReadOnlyCollection<MarketDataUpdate>? updates,
        bool parseSucceeded)
    {
        if (!parseSucceeded)
        {
            return new MarketWebSocketFrameDiagnosticSamplingDecision(true, true, "parse_failure");
        }

        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Equals("PING", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("PONG", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketWebSocketFrameDiagnosticSamplingDecision(true, true, "heartbeat");
        }

        if ((updates?.Any(update => update.MarketResolved || update.EventType == MarketDataEventType.MarketResolved) ?? false) ||
            trimmed.Contains("market_resolved", StringComparison.OrdinalIgnoreCase))
        {
            return new MarketWebSocketFrameDiagnosticSamplingDecision(true, true, "market_resolved");
        }

        if (updates is { Count: >= 100 })
        {
            return new MarketWebSocketFrameDiagnosticSamplingDecision(true, true, "bulk_frame");
        }

        var sampleEvery = options.CriticalFrameDiagnosticSampleEvery;
        if (sampleEvery <= 0)
        {
            return new MarketWebSocketFrameDiagnosticSamplingDecision(false, false, "important_only");
        }

        var sequence = Interlocked.Increment(ref ordinaryFrameSequence);
        return sequence % sampleEvery == 0
            ? new MarketWebSocketFrameDiagnosticSamplingDecision(true, false, "routine_sample")
            : new MarketWebSocketFrameDiagnosticSamplingDecision(false, false, "not_sampled");
    }
}
