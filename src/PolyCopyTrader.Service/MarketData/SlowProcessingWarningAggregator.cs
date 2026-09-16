namespace PolyCopyTrader.Service.MarketData;

internal sealed record SlowProcessingWarningObservation(
    string Component,
    string EventType,
    string? AssetId,
    string LatencyCategory,
    double QueueDelayMilliseconds,
    double ProcessingDurationMilliseconds,
    int PendingDepth,
    string ActivePhase,
    string? ActiveOperation,
    double ActivePhaseDurationMilliseconds,
    string SlowestPhase,
    string? SlowestOperation,
    double SlowestPhaseDurationMilliseconds);

internal sealed class SlowProcessingWarningAggregator
{
    private readonly object sync = new();
    private readonly ILogger logger;
    private readonly TimeSpan aggregationInterval;
    private readonly string warningSubject;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Dictionary<IncidentKey, IncidentState> incidents = [];
    private int activeIncidentCount;

    internal SlowProcessingWarningAggregator(
        ILogger logger,
        TimeSpan aggregationInterval,
        string warningSubject,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(warningSubject);
        if (aggregationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aggregationInterval),
                aggregationInterval,
                "The slow-warning aggregation interval must be positive.");
        }

        this.logger = logger;
        this.aggregationInterval = aggregationInterval;
        this.warningSubject = warningSubject;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal void RecordSlow(SlowProcessingWarningObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Component);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.LatencyCategory);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ActivePhase);

        var observedAtUtc = utcNow();
        var key = new IncidentKey(
            observation.Component,
            observation.EventType,
            observation.LatencyCategory,
            observation.ActivePhase);
        AggregateSnapshot? aggregate = null;
        var firstOccurrence = false;

        lock (sync)
        {
            if (!incidents.TryGetValue(key, out var incident))
            {
                incidents.Add(key, IncidentState.Create(observation, observedAtUtc));
                Volatile.Write(ref activeIncidentCount, incidents.Count);
                firstOccurrence = true;
            }
            else
            {
                incident.AddRepeat(observation, observedAtUtc);
                if (observedAtUtc - incident.LastAggregateAtUtc >= aggregationInterval)
                {
                    aggregate = incident.CaptureAggregate(key, observedAtUtc);
                }
            }
        }

        if (firstOccurrence)
        {
            LogFirstOccurrence(observation, observedAtUtc);
        }
        else if (aggregate is not null)
        {
            LogAggregate(aggregate);
        }
    }

    internal void RecordNonSlow(
        string component,
        string eventType,
        string activePhase,
        string? assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(activePhase);

        if (Volatile.Read(ref activeIncidentCount) == 0)
        {
            return;
        }

        var recoveredAtUtc = utcNow();
        List<RecoverySnapshot> recoveries = [];
        lock (sync)
        {
            foreach (var pair in incidents
                         .Where(pair =>
                             string.Equals(pair.Key.Component, component, StringComparison.Ordinal) &&
                             string.Equals(pair.Key.EventType, eventType, StringComparison.Ordinal) &&
                             string.Equals(pair.Key.ActivePhase, activePhase, StringComparison.Ordinal))
                         .ToArray())
            {
                incidents.Remove(pair.Key);
                recoveries.Add(pair.Value.CaptureRecovery(
                    pair.Key,
                    assetId ?? pair.Value.LastAssetId,
                    recoveredAtUtc));
            }

            Volatile.Write(ref activeIncidentCount, incidents.Count);
        }

        foreach (var recovery in recoveries)
        {
            LogRecovery(recovery);
        }
    }

    private void LogFirstOccurrence(
        SlowProcessingWarningObservation observation,
        DateTimeOffset observedAtUtc)
    {
        logger.LogWarning(
            "{WarningSubject} was slow. Component={Component} EventType={EventType} AssetId={AssetId} LatencyCategory={LatencyCategory} ObservedAtUtc={ObservedAtUtc} QueueDelayMs={QueueDelayMs} ProcessingDurationMs={ProcessingDurationMs} PendingDepth={PendingDepth} ActivePhase={ActivePhase} ActiveOperation={ActiveOperation} ActivePhaseDurationMs={ActivePhaseDurationMs} SlowestPhase={SlowestPhase} SlowestOperation={SlowestOperation} SlowestPhaseDurationMs={SlowestPhaseDurationMs}",
            warningSubject,
            observation.Component,
            observation.EventType,
            observation.AssetId,
            observation.LatencyCategory,
            observedAtUtc,
            observation.QueueDelayMilliseconds,
            observation.ProcessingDurationMilliseconds,
            observation.PendingDepth,
            observation.ActivePhase,
            observation.ActiveOperation,
            observation.ActivePhaseDurationMilliseconds,
            observation.SlowestPhase,
            observation.SlowestOperation,
            observation.SlowestPhaseDurationMilliseconds);
    }

    private void LogAggregate(AggregateSnapshot aggregate)
    {
        logger.LogWarning(
            "Repeated {WarningSubject} slow completions were aggregated. Component={Component} EventType={EventType} AssetId={AssetId} LatencyCategory={LatencyCategory} ActivePhase={ActivePhase} Count={Count} FirstUtc={FirstUtc} LastUtc={LastUtc} MaxQueueDelayMs={MaxQueueDelayMs} MaxProcessingDurationMs={MaxProcessingDurationMs} MaxPendingDepth={MaxPendingDepth}",
            warningSubject,
            aggregate.Key.Component,
            aggregate.Key.EventType,
            aggregate.LastAssetId,
            aggregate.Key.LatencyCategory,
            aggregate.Key.ActivePhase,
            aggregate.Count,
            aggregate.FirstUtc,
            aggregate.LastUtc,
            aggregate.MaxQueueDelayMilliseconds,
            aggregate.MaxProcessingDurationMilliseconds,
            aggregate.MaxPendingDepth);
    }

    private void LogRecovery(RecoverySnapshot recovery)
    {
        logger.LogInformation(
            "{WarningSubject} recovered after slow processing. Component={Component} EventType={EventType} AssetId={AssetId} LatencyCategory={LatencyCategory} ActivePhase={ActivePhase} Count={Count} FirstUtc={FirstUtc} LastUtc={LastUtc} RecoveredAtUtc={RecoveredAtUtc} MaxQueueDelayMs={MaxQueueDelayMs} MaxProcessingDurationMs={MaxProcessingDurationMs} MaxPendingDepth={MaxPendingDepth}",
            warningSubject,
            recovery.Key.Component,
            recovery.Key.EventType,
            recovery.AssetId,
            recovery.Key.LatencyCategory,
            recovery.Key.ActivePhase,
            recovery.Count,
            recovery.FirstUtc,
            recovery.LastUtc,
            recovery.RecoveredAtUtc,
            recovery.MaxQueueDelayMilliseconds,
            recovery.MaxProcessingDurationMilliseconds,
            recovery.MaxPendingDepth);
    }

    private readonly record struct IncidentKey(
        string Component,
        string EventType,
        string LatencyCategory,
        string ActivePhase);

    private sealed class IncidentState
    {
        private IncidentState(
            SlowProcessingWarningObservation observation,
            DateTimeOffset observedAtUtc)
        {
            FirstUtc = observedAtUtc;
            LastUtc = observedAtUtc;
            LastAggregateAtUtc = observedAtUtc;
            Count = 1;
            LastAssetId = observation.AssetId;
            MaxQueueDelayMilliseconds = observation.QueueDelayMilliseconds;
            MaxProcessingDurationMilliseconds = observation.ProcessingDurationMilliseconds;
            MaxPendingDepth = observation.PendingDepth;
        }

        internal DateTimeOffset FirstUtc { get; }

        internal DateTimeOffset LastUtc { get; private set; }

        internal DateTimeOffset LastAggregateAtUtc { get; private set; }

        internal int Count { get; private set; }

        internal string? LastAssetId { get; private set; }

        internal double MaxQueueDelayMilliseconds { get; private set; }

        internal double MaxProcessingDurationMilliseconds { get; private set; }

        internal int MaxPendingDepth { get; private set; }

        private int PendingRepeatCount { get; set; }

        private DateTimeOffset? PendingRepeatFirstUtc { get; set; }

        private DateTimeOffset? PendingRepeatLastUtc { get; set; }

        private double PendingMaxQueueDelayMilliseconds { get; set; }

        private double PendingMaxProcessingDurationMilliseconds { get; set; }

        private int PendingMaxPendingDepth { get; set; }

        internal static IncidentState Create(
            SlowProcessingWarningObservation observation,
            DateTimeOffset observedAtUtc)
        {
            return new IncidentState(observation, observedAtUtc);
        }

        internal void AddRepeat(
            SlowProcessingWarningObservation observation,
            DateTimeOffset observedAtUtc)
        {
            Count++;
            LastUtc = observedAtUtc;
            LastAssetId = observation.AssetId;
            MaxQueueDelayMilliseconds = Math.Max(
                MaxQueueDelayMilliseconds,
                observation.QueueDelayMilliseconds);
            MaxProcessingDurationMilliseconds = Math.Max(
                MaxProcessingDurationMilliseconds,
                observation.ProcessingDurationMilliseconds);
            MaxPendingDepth = Math.Max(MaxPendingDepth, observation.PendingDepth);

            PendingRepeatCount++;
            PendingRepeatFirstUtc ??= observedAtUtc;
            PendingRepeatLastUtc = observedAtUtc;
            PendingMaxQueueDelayMilliseconds = Math.Max(
                PendingMaxQueueDelayMilliseconds,
                observation.QueueDelayMilliseconds);
            PendingMaxProcessingDurationMilliseconds = Math.Max(
                PendingMaxProcessingDurationMilliseconds,
                observation.ProcessingDurationMilliseconds);
            PendingMaxPendingDepth = Math.Max(PendingMaxPendingDepth, observation.PendingDepth);
        }

        internal AggregateSnapshot CaptureAggregate(
            IncidentKey key,
            DateTimeOffset reportedAtUtc)
        {
            var aggregate = new AggregateSnapshot(
                key,
                PendingRepeatCount,
                PendingRepeatFirstUtc.GetValueOrDefault(reportedAtUtc),
                PendingRepeatLastUtc.GetValueOrDefault(reportedAtUtc),
                LastAssetId,
                PendingMaxQueueDelayMilliseconds,
                PendingMaxProcessingDurationMilliseconds,
                PendingMaxPendingDepth);

            PendingRepeatCount = 0;
            PendingRepeatFirstUtc = null;
            PendingRepeatLastUtc = null;
            PendingMaxQueueDelayMilliseconds = 0d;
            PendingMaxProcessingDurationMilliseconds = 0d;
            PendingMaxPendingDepth = 0;
            LastAggregateAtUtc = reportedAtUtc;
            return aggregate;
        }

        internal RecoverySnapshot CaptureRecovery(
            IncidentKey key,
            string? assetId,
            DateTimeOffset recoveredAtUtc)
        {
            return new RecoverySnapshot(
                key,
                Count,
                FirstUtc,
                LastUtc,
                recoveredAtUtc,
                assetId,
                MaxQueueDelayMilliseconds,
                MaxProcessingDurationMilliseconds,
                MaxPendingDepth);
        }
    }

    private sealed record AggregateSnapshot(
        IncidentKey Key,
        int Count,
        DateTimeOffset FirstUtc,
        DateTimeOffset LastUtc,
        string? LastAssetId,
        double MaxQueueDelayMilliseconds,
        double MaxProcessingDurationMilliseconds,
        int MaxPendingDepth);

    private sealed record RecoverySnapshot(
        IncidentKey Key,
        int Count,
        DateTimeOffset FirstUtc,
        DateTimeOffset LastUtc,
        DateTimeOffset RecoveredAtUtc,
        string? AssetId,
        double MaxQueueDelayMilliseconds,
        double MaxProcessingDurationMilliseconds,
        int MaxPendingDepth);
}
