using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.ExternalPrices;

// Diagnostic state never participates in price selection or freshness decisions.
internal sealed class BinanceCryptoReferenceDiagnostics(ILogger logger, TimeProvider clock)
{
    private static readonly TimeSpan EventInterval = TimeSpan.FromSeconds(60);
    private readonly object sync = new();
    private readonly Dictionary<string, AssetState> assets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ETH"] = new(),
        ["SOL"] = new()
    };
    private long connectionGeneration;
    private string connectionState = "NotStarted";
    private string activePhase = "Idle";
    private DateTimeOffset? phaseStartedAtUtc;
    private long? phaseStartedTimestamp;
    private DateTimeOffset? lastReceiveCompletedAtUtc;
    private long receivedFrames;
    private long completedMessages;
    private long parserRejectedMessages;
    private long ignoredMessages;
    private long? parseStarted;
    private long? publicationWaiting;
    private long? publicationEntered;
    private long? sampleLogStarted;
    private double? lastParseDurationMs;
    private double? lastPublicationLockWaitMs;
    private double? lastPublicationLockHoldMs;
    private double? lastSampleLogDurationMs;

    internal void ConnectionStarting() => Update("Connecting", (now, _) =>
    {
        connectionGeneration++;
        connectionState = "Connecting";
    });

    internal void ConnectionOpened() => Update("Idle", (_, _) => connectionState = "Connected");

    internal void ConnectionClosed() => Update("Disconnected", (_, _) => connectionState = "Disconnected");

    internal void ReceiveStarted() => Update("Receive", null);

    internal void ReceiveCompleted(bool endOfMessage) => Update("Received", (_, utc) =>
    {
        receivedFrames++;
        if (endOfMessage)
        {
            completedMessages++;
        }

        lastReceiveCompletedAtUtc = utc;
    });

    internal void ParseStarted() => Update("Parse", (now, _) => parseStarted = now);

    internal void ParseCompleted(bool accepted) => Update("Parsed", (now, _) =>
    {
        lastParseDurationMs = ElapsedMilliseconds(parseStarted, now);
        if (!accepted)
        {
            // A failed parser does not establish a trustworthy asset identity.
            parserRejectedMessages++;
        }
    });

    internal void MessageIgnored() => Update("Ignored", (_, _) => ignoredMessages++);

    internal void PublicationWaiting() => Update("PublicationLockWait", (now, _) => publicationWaiting = now);

    internal void PublicationEntered() => Update("Publication", (now, _) =>
    {
        lastPublicationLockWaitMs = ElapsedMilliseconds(publicationWaiting, now);
        publicationEntered = now;
    });

    internal long? RecordPublished(CryptoReferencePricePoint point)
    {
        try
        {
            var acceptedAtUtc = clock.GetUtcNow();
            lock (sync)
            {
                if (!assets.TryGetValue(point.AssetSymbol, out var asset))
                {
                    return null;
                }

                asset.AcceptedMessages++;
                asset.LastSourceUpdatedAtUtc = point.SourceUpdatedAtUtc;
                asset.LastFetchedAtUtc = point.FetchedAtUtc;
                asset.LastAcceptedMessageAtUtc = acceptedAtUtc;
                return asset.AcceptedMessages;
            }
        }
        catch
        {
            return null;
        }
    }

    internal void SampleLogStarted() => Update("SampleLog", (now, _) => sampleLogStarted = now);

    internal void SampleLogCompleted() => Update("Publication", (now, _) =>
        lastSampleLogDurationMs = ElapsedMilliseconds(sampleLogStarted, now));

    internal void PublicationCompleted() => Update("Idle", (now, _) =>
        lastPublicationLockHoldMs = ElapsedMilliseconds(publicationEntered, now));

    internal void ObserveStale(CryptoReferencePricePoint point, TimeSpan age, TimeSpan staleAfter)
    {
        try
        {
            DiagnosticEvent? diagnosticEvent;
            lock (sync)
            {
                if (!assets.TryGetValue(point.AssetSymbol, out var asset))
                {
                    return;
                }

                asset.IsStale = true;
                // Publication is recorded under the existing cache lock. Even a getter
                // that captured an older price must wait for a later publication to recover.
                asset.StaleObservedPublication = asset.AcceptedMessages;
                diagnosticEvent = PrepareEvent(point, age, staleAfter, asset, recovery: false);
            }

            Emit(diagnosticEvent);
        }
        catch
        {
            // Diagnostics must not replace the original stale-price exception.
        }
    }

    internal void ObserveRecovery(
        CryptoReferencePricePoint point,
        long? publication,
        Func<DateTimeOffset> utcNow,
        TimeSpan staleAfter)
    {
        try
        {
            DiagnosticEvent? diagnosticEvent;
            lock (sync)
            {
                if (!assets.TryGetValue(point.AssetSymbol, out var asset) ||
                    !asset.IsStale || publication is null ||
                    publication != asset.AcceptedMessages ||
                    publication <= asset.StaleObservedPublication)
                {
                    return;
                }

                var age = utcNow() - point.FetchedAtUtc;
                if (age > staleAfter)
                {
                    return;
                }

                asset.IsStale = false;
                diagnosticEvent = PrepareEvent(point, age, staleAfter, asset, recovery: true);
            }

            Emit(diagnosticEvent);
        }
        catch
        {
            // Clock, snapshot and logging failures cannot reject an accepted message.
        }
    }

    internal BinanceCryptoReferenceDiagnosticSnapshot? GetSnapshot(string assetSymbol)
    {
        try
        {
            lock (sync)
            {
                return assets.TryGetValue(assetSymbol, out var asset)
                    ? CreateSnapshot(assetSymbol, asset, clock.GetTimestamp())
                    : null;
            }
        }
        catch
        {
            return null;
        }
    }

    private void Update(string phase, Action<long, DateTimeOffset>? update)
    {
        try
        {
            var now = clock.GetTimestamp();
            var utc = clock.GetUtcNow();
            lock (sync)
            {
                update?.Invoke(now, utc);
                activePhase = phase;
                phaseStartedAtUtc = utc;
                phaseStartedTimestamp = now;
            }
        }
        catch
        {
            // Instrumentation is deliberately best effort and performs no fallback work.
        }
    }

    private DiagnosticEvent? PrepareEvent(
        CryptoReferencePricePoint point,
        TimeSpan age,
        TimeSpan staleAfter,
        AssetState asset,
        bool recovery)
    {
        var kind = recovery ? asset.Recovery : asset.Stale;
        var now = clock.GetTimestamp();
        if (kind.InFlight ||
            (kind.LastEmittedTimestamp is { } last && clock.GetElapsedTime(last, now) < EventInterval))
        {
            kind.SuppressedCount++;
            return null;
        }

        var snapshot = CreateSnapshot(point.AssetSymbol, asset, now);
        kind.InFlight = true;
        return new DiagnosticEvent(
            point.AssetSymbol, recovery, kind, now, kind.SuppressedCount,
            point.FetchedAtUtc, age.TotalSeconds, staleAfter.TotalSeconds, snapshot);
    }

    private void Emit(DiagnosticEvent? diagnosticEvent)
    {
        if (diagnosticEvent is null)
        {
            return;
        }

        var emitted = false;
        try
        {
            logger.Log(
                diagnosticEvent.Recovery ? LogLevel.Information : LogLevel.Warning,
                "Binance crypto reference freshness diagnostic. DiagnosticKind={DiagnosticKind} AssetSymbol={AssetSymbol} SuppressedCount={SuppressedCount} ObservedFetchedAtUtc={ObservedFetchedAtUtc} ObservedAgeSeconds={ObservedAgeSeconds} StaleAfterSeconds={StaleAfterSeconds} Diagnostic={@Diagnostic}",
                diagnosticEvent.Recovery ? "recovery" : "stale",
                diagnosticEvent.AssetSymbol,
                diagnosticEvent.SuppressedCount,
                diagnosticEvent.FetchedAtUtc,
                diagnosticEvent.AgeSeconds,
                diagnosticEvent.StaleAfterSeconds,
                diagnosticEvent.Snapshot);
            emitted = true;
        }
        catch
        {
            // Preserve the original result even when a diagnostic sink fails.
        }
        finally
        {
            lock (sync)
            {
                diagnosticEvent.Kind.InFlight = false;
                if (emitted)
                {
                    diagnosticEvent.Kind.LastEmittedTimestamp = diagnosticEvent.Timestamp;
                    // Concurrent candidates suppressed during emission belong to the next event.
                    diagnosticEvent.Kind.SuppressedCount -= diagnosticEvent.SuppressedCount;
                }
            }
        }
    }

    private double? ElapsedMilliseconds(long? started, long now) =>
        started is { } value ? clock.GetElapsedTime(value, now).TotalMilliseconds : null;

    private BinanceCryptoReferenceDiagnosticSnapshot CreateSnapshot(string symbol, AssetState asset, long now) => new(
        symbol, connectionGeneration, connectionState, activePhase, phaseStartedAtUtc,
        ElapsedMilliseconds(phaseStartedTimestamp, now), lastReceiveCompletedAtUtc,
        receivedFrames, completedMessages, parserRejectedMessages, ignoredMessages,
        asset.AcceptedMessages, asset.LastAcceptedMessageAtUtc, asset.LastSourceUpdatedAtUtc,
        asset.LastFetchedAtUtc, lastParseDurationMs, lastPublicationLockWaitMs,
        lastPublicationLockHoldMs, lastSampleLogDurationMs, asset.IsStale,
        asset.Stale.SuppressedCount, asset.Recovery.SuppressedCount);

    private sealed class AssetState
    {
        internal long AcceptedMessages;
        internal DateTimeOffset? LastAcceptedMessageAtUtc;
        internal DateTimeOffset? LastSourceUpdatedAtUtc;
        internal DateTimeOffset? LastFetchedAtUtc;
        internal bool IsStale;
        internal long StaleObservedPublication;
        internal EventKindState Stale { get; } = new();
        internal EventKindState Recovery { get; } = new();
    }

    private sealed class EventKindState
    {
        internal long? LastEmittedTimestamp;
        internal long SuppressedCount;
        internal bool InFlight;
    }

    private sealed record DiagnosticEvent(
        string AssetSymbol,
        bool Recovery,
        EventKindState Kind,
        long Timestamp,
        long SuppressedCount,
        DateTimeOffset FetchedAtUtc,
        double AgeSeconds,
        double StaleAfterSeconds,
        BinanceCryptoReferenceDiagnosticSnapshot Snapshot);
}

internal sealed record BinanceCryptoReferenceDiagnosticSnapshot(
    string AssetSymbol,
    long ConnectionGeneration,
    string ConnectionState,
    string ActivePhase,
    DateTimeOffset? PhaseStartedAtUtc,
    double? ActivePhaseDurationMs,
    DateTimeOffset? LastReceiveCompletedAtUtc,
    long ReceivedFrames,
    long CompletedMessages,
    long ParserRejectedMessages,
    long IgnoredMessages,
    long AcceptedMessages,
    DateTimeOffset? LastAcceptedMessageAtUtc,
    DateTimeOffset? LastSourceUpdatedAtUtc,
    DateTimeOffset? LastFetchedAtUtc,
    double? LastParseDurationMs,
    double? LastPublicationLockWaitMs,
    double? LastPublicationLockHoldMs,
    double? LastSampleLogDurationMs,
    bool IsStale,
    long SuppressedStaleCount,
    long SuppressedRecoveryCount);
