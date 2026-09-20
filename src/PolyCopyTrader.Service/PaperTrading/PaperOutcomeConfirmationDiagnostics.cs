using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed record PaperConfirmationQueueSnapshot(DateTimeOffset ObservedAtUtc, int PendingBatches,
    int PendingUpdates, int PendingDiagnostics, int PendingGeneral, int InFlightGeneral,
    int PendingMaker, int InFlightMaker);

public sealed class PaperOutcomeConfirmationDiagnostics(ILogger logger, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly object sync = new();
    private DateTimeOffset intervalStart = (timeProvider ?? TimeProvider.System).GetUtcNow();
    private long intervalStamp = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private long checks, attempts;
    private readonly Dictionary<string, long> skips = [];
    private readonly Dictionary<string, long> outcomes = [];
    private readonly Dictionary<ServiceActivitySource, long> busySources = [];
    private readonly Dictionary<ServiceActivitySource, long> cancellationSources = [];
    private PaperOutcomeConfirmationTrace? active;
    private ServiceActivitySnapshot? lastActivity;
    private PaperConfirmationQueueSnapshot? lastQueues;

    public void ObserveIdle(ServiceActivitySnapshot activity, PaperConfirmationQueueSnapshot queues)
    {
        lock (sync)
        {
            checks++;
            lastActivity = activity;
            lastQueues = queues;
            if (activity.Reason != "Idle") Add(skips, activity.Reason);
            foreach (var source in activity.Sources.Keys) Add(busySources, source);
        }
    }
    public PaperOutcomeConfirmationTrace Begin()
    {
        lock (sync) { attempts++; return active = new(clock); }
    }
    public void End(PaperOutcomeConfirmationTrace trace, ServiceActivityCancellation cancellation)
    {
        var snapshot = trace.Snapshot();
        lock (sync)
        {
            Add(outcomes, snapshot.Outcome);
            if (snapshot.Outcome == "Canceled" && cancellation.Source is { } source) Add(cancellationSources, source);
            if (ReferenceEquals(active, trace)) active = null;
        }
        // Empty queues of candidates are routine and appear only in the summary.
        if (snapshot.Outcome == "NoCandidate") return;
        logger.LogInformation(
            "Paper confirmation attempt finished. AttemptId={AttemptId} PaperOrderId={PaperOrderId} StartedAtUtc={StartedAtUtc} Outcome={Outcome} Reason={Reason} Stage={Stage} StageAgeMs={StageAgeMs} DurationMs={DurationMs} ErrorType={ErrorType} SqlState={SqlState} ErrorStage={ErrorStage} CancellationReason={CancellationReason} CancellationSource={CancellationSource} CancellationEventType={CancellationEventType} CommitStarted={CommitStarted} CommitAcknowledged={CommitAcknowledged} Stages={@Stages}",
            snapshot.AttemptId, snapshot.PaperOrderId, snapshot.StartedAtUtc, snapshot.Outcome, snapshot.Reason,
            snapshot.Stage, snapshot.StageAgeMs, snapshot.DurationMs, snapshot.ErrorType, snapshot.SqlState,
            snapshot.ErrorStage, cancellation.Reason, cancellation.Source?.Name, cancellation.Source?.EventType,
            snapshot.CommitStarted, snapshot.CommitAcknowledged, snapshot.Stages);
    }
    public void LogSummaryIfDue()
    {
        object summary;
        lock (sync)
        {
            if (clock.GetElapsedTime(intervalStamp).TotalSeconds < 30) return;
            var now = clock.GetUtcNow();
            summary = new
            {
                FromUtc = intervalStart, ToUtc = now, IdleChecks = checks, Attempts = attempts,
                Skips = new Dictionary<string, long>(skips), Outcomes = new Dictionary<string, long>(outcomes),
                BusySources = busySources.Select(x => new { Source = x.Key.Name, x.Key.EventType, Checks = x.Value }).ToArray(),
                CancellationSources = cancellationSources.Select(x => new { Source = x.Key.Name, x.Key.EventType, Attempts = x.Value }).ToArray(),
                LastActivity = lastActivity is null ? null : new
                {
                    lastActivity.Reason, lastActivity.ActiveCount,
                    Sources = lastActivity.Sources.Select(x => new { Source = x.Key.Name, x.Key.EventType, Count = x.Value }).ToArray()
                },
                LastQueues = lastQueues, ActiveAttempt = active?.Snapshot()
            };
            checks = attempts = 0;
            skips.Clear(); outcomes.Clear(); busySources.Clear(); cancellationSources.Clear();
            intervalStart = now; intervalStamp = clock.GetTimestamp();
        }
        logger.LogInformation("Paper confirmation 30-second summary. Summary={@Summary}", summary);
    }
    private static void Add<T>(Dictionary<T, long> counts, T key) where T : notnull
    { counts.TryGetValue(key, out var count); counts[key] = count + 1; }
}
