using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperOutcomeConfirmationDiagnosticsTests
{
    [Fact]
    public void FirstCancellationAndNestedActiveSourcesArePreserved()
    {
        var activity = new ServiceActivityState();
        using (activity.EnterTradingCycle("Entry"))
        using (activity.EnterTradingCycle("Entry"))
        {
            Assert.Null(activity.TryEnterIdle(() => true, default, out var busy));
            Assert.Equal("ActiveTrading", busy.Reason);
            Assert.Equal(2, busy.Sources[new("Entry")]);
        }
        using var idle = activity.TryEnterIdle(() => true, default)!;
        using (activity.EnterTradingCycle("Market", MarketDataEventType.PriceChange)) { }
        using (activity.EnterTradingCycle("Later")) { }
        Assert.Equal(new ServiceActivityCancellation("Foreground", new("Market", MarketDataEventType.PriceChange)), idle.Cancellation);
    }

    [Fact]
    public async Task EmptyCandidatesAndBusyChecksAreSummarizedWithoutFlood()
    {
        var clock = new ManualClock();
        var logger = new CaptureLogger();
        var setup = Create(logger, clock);
        setup.Repository.Claim = _ => Task.FromResult<PaperOrder?>(null);
        for (var i = 0; i < 50; i++) await setup.Worker.ProcessIdleGapAsync(default);
        Assert.Empty(logger.Entries);
        var diagnostics = new PaperOutcomeConfirmationDiagnostics(logger, clock);
        Parallel.For(0, 100, _ => diagnostics.ObserveIdle(new("ActiveTrading", 1,
            new Dictionary<ServiceActivitySource, int> { [new("Entry")] = 1 }), Queues(clock)));
        clock.Advance(TimeSpan.FromSeconds(29)); diagnostics.LogSummaryIfDue();
        Assert.Empty(logger.Entries);
        clock.Advance(TimeSpan.FromSeconds(1)); diagnostics.LogSummaryIfDue();
        var summary = JsonSerializer.SerializeToElement(Assert.Single(logger.Entries)["@Summary"]);
        Assert.Equal(100, summary.GetProperty("IdleChecks").GetInt64());
        Assert.Equal(100, summary.GetProperty("Skips").GetProperty("ActiveTrading").GetInt64());
        diagnostics.LogSummaryIfDue(); Assert.Single(logger.Entries);
        clock.Advance(TimeSpan.FromSeconds(30)); diagnostics.LogSummaryIfDue();
        var next = JsonSerializer.SerializeToElement(logger.Entries.Last()["@Summary"]);
        Assert.Equal(0, next.GetProperty("IdleChecks").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulAttemptHasOrderCorrelationStageDurationsAndCommitAcknowledgement(bool corrected)
    {
        var logger = new CaptureLogger(); var clock = new ManualClock(); var setup = Create(logger, clock);
        setup.Repository.Confirm = (_, _, trace) =>
        {
            trace!.Enter(PaperConfirmationStage.Commit); clock.Advance(TimeSpan.FromMilliseconds(17));
            trace.AcknowledgeCommit(); return Task.FromResult(new PaperOutcomeConfirmationResult(true, corrected, "confirmed"));
        };
        await RunSteps(setup, 3);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal(setup.Order.Id, log["PaperOrderId"]);
        Assert.IsType<Guid>(log["AttemptId"]);
        Assert.Equal(corrected ? "Corrected" : "Matched", log["Outcome"]);
        Assert.Equal(true, log["CommitAcknowledged"]);
        Assert.Equal(17d, log["StageAgeMs"]);
        var stages = Assert.IsAssignableFrom<IReadOnlyList<PaperConfirmationStageTiming>>(log["@Stages"]);
        Assert.Contains(stages, x => x.Stage == PaperConfirmationStage.Claim);
        Assert.Contains(stages, x => x.Stage == PaperConfirmationStage.GammaToken);
    }

    [Theory]
    [InlineData("claim", PaperConfirmationStage.Claim)]
    [InlineData("http", PaperConfirmationStage.GammaToken)]
    [InlineData("commit", PaperConfirmationStage.Commit)]
    public async Task StopCancellationReportsActualStage(string where, PaperConfirmationStage stage)
    {
        var logger = new CaptureLogger(); var setup = Create(logger);
        using var stop = new CancellationTokenSource();
        void Cancel() { stop.Cancel(); }
        if (where == "claim") setup.Repository.Claim = token => { Cancel(); return Task.FromCanceled<PaperOrder?>(token); };
        if (where == "http") setup.Gamma.Lookup = (_, token) => { Cancel(); return Task.FromCanceled<IReadOnlyList<PolymarketOnChainTokenMetadata>>(token); };
        if (where == "commit") setup.Repository.Confirm = (_, token, trace) =>
        { trace!.Enter(PaperConfirmationStage.Commit); Cancel(); return Task.FromCanceled<PaperOutcomeConfirmationResult>(token); };
        var steps = where == "claim" ? 1 : where == "http" ? 2 : 3;
        for (var i = 0; i < steps; i++) await setup.Worker.ProcessIdleGapAsync(stop.Token);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("Canceled", log["Outcome"]); Assert.Equal(stage, log["Stage"]);
        Assert.Equal("ServiceStopping", log["CancellationReason"]);
        Assert.Null(log["CancellationSource"]);
        Assert.Equal(false, log["CommitAcknowledged"]);
        Assert.Equal(where == "commit", log["CommitStarted"]);
    }

    [Fact]
    public async Task FirstHttpErrorSurvivesForegroundAndStopBeforeRetryAndSecretsAreNotLogged()
    {
        var logger = new CaptureLogger(); var setup = Create(logger);
        setup.Gamma.Lookup = (_, _) =>
        {
            using (setup.Activity.EnterTradingCycle("Entry")) { }
            throw new HttpRequestException("secret-response-header");
        };
        await RunSteps(setup, 2);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await setup.Worker.ProcessIdleGapAsync(stop.Token);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("Canceled", log["Outcome"]);
        Assert.Equal(nameof(HttpRequestException), log["ErrorType"]);
        Assert.Equal(PaperConfirmationStage.GammaToken, log["ErrorStage"]);
        Assert.DoesNotContain("secret-response-header", JsonSerializer.Serialize(logger.Entries));
        Assert.Equal(0, setup.Repository.Defers);
    }

    [Fact]
    public async Task SqlStateAndFirstDatabaseFailureSurviveRetryFailure()
    {
        var logger = new CaptureLogger(); var setup = Create(logger);
        setup.Repository.Confirm = (_, _, trace) =>
        { trace!.Enter(PaperConfirmationStage.OrderLock); throw new PostgresException("secret-db-details", "ERROR", "ERROR", "55P03"); };
        setup.Repository.Defer = _ => throw new InvalidOperationException("secret-retry-details");
        await RunSteps(setup, 4);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("55P03", log["SqlState"]);
        Assert.Equal(nameof(PostgresException), log["ErrorType"]);
        Assert.Equal(PaperConfirmationStage.OrderLock, log["ErrorStage"]);
        Assert.Equal(PaperConfirmationStage.Defer, log["Stage"]);
        Assert.DoesNotContain("secret-", JsonSerializer.Serialize(logger.Entries));
    }

    [Fact]
    public async Task ConditionFallbackAndDeferredOutcomeKeepTheirReachedStages()
    {
        var logger = new CaptureLogger(); var setup = Create(logger);
        setup.Gamma.Lookup = (_, _) => Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
        await RunSteps(setup, 3);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("Deferred", log["Outcome"]); Assert.Equal(1, setup.Repository.Defers);
        var stages = Assert.IsAssignableFrom<IReadOnlyList<PaperConfirmationStageTiming>>(log["@Stages"]);
        Assert.Contains(stages, x => x.Stage == PaperConfirmationStage.GammaCondition);
        Assert.Equal(PaperConfirmationStage.Defer, log["Stage"]);
    }

    [Fact]
    public async Task ActualGammaTimeoutIsDistinctFromForegroundCancellation()
    {
        var logger = new CaptureLogger(); var setup = Create(logger);
        setup.Gamma.Lookup = async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return []; };
        await RunSteps(setup, 3).WaitAsync(TimeSpan.FromSeconds(10));
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("Timeout", log["Outcome"]); Assert.Equal("GammaTimeout", log["Reason"]);
        Assert.Equal(1, setup.Repository.Defers);
    }

    [Fact]
    public async Task IndependentSummaryTimerShowsHungAttemptAndShutdownCancellation()
    {
        var logger = new CaptureLogger(); var clock = new ManualClock(); var setup = Create(logger, clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A provider can take time to acknowledge cancellation; summaries must remain independent.
        setup.Repository.Claim = async token => { started.TrySetResult(); await release.Task; token.ThrowIfCancellationRequested(); return null; };
        await setup.Worker.StartAsync(default);
        await UntilAsync(() => clock.TimerCount == 2);
        clock.Advance(TimeSpan.FromSeconds(1)); await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(TimeSpan.FromSeconds(30)); await UntilAsync(() => logger.Entries.Any(x => x.ContainsKey("@Summary")));
        var summary = JsonSerializer.SerializeToElement(logger.Entries.Single(x => x.ContainsKey("@Summary"))["@Summary"]);
        Assert.Equal((int)PaperConfirmationStage.Claim, summary.GetProperty("ActiveAttempt").GetProperty("Stage").GetInt32());
        Assert.Equal(30000d, summary.GetProperty("ActiveAttempt").GetProperty("StageAgeMs").GetDouble());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stopping = setup.Worker.StopAsync(deadline.Token);
        release.SetResult();
        await stopping;
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("ServiceStopping", log["CancellationReason"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoopFailureCancelsSiblingAndReachesBackgroundService(bool summaryFailure)
    {
        var logger = new CaptureLogger { FailSummary = summaryFailure };
        var clock = new ManualClock(); var setup = Create(logger, clock);
        if (!summaryFailure) setup.Market.Failure = new InvalidOperationException("metrics unavailable");
        await setup.Worker.StartAsync(default);
        await UntilAsync(() => clock.TimerCount == 2);
        clock.Advance(TimeSpan.FromSeconds(summaryFailure ? 30 : 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, clock.ActiveTimers);
        await setup.Worker.StopAsync(default);
    }

    [Fact]
    public void BackgroundAndQueueBusyObservationsHaveExactReasons()
    {
        var activity = new ServiceActivityState();
        Assert.Null(activity.TryEnterIdle(() => false, default, out var queues));
        Assert.Equal("QueuesBusy", queues.Reason);
        var empty = true;
        using var idle = activity.TryEnterIdle(() => empty, default)!;
        Assert.Null(activity.TryEnterIdle(() => true, default, out var background));
        Assert.Equal("BackgroundBusy", background.Reason);
        empty = false;
        Assert.Throws<OperationCanceledException>(idle.CheckIdle);
        Assert.Equal("QueuesBusy", idle.Cancellation.Reason);
    }

    [Theory]
    [InlineData("claim", 2)]
    [InlineData("lookup", 5)]
    [InlineData("apply", 2)]
    [InlineData("defer", 2)]
    public async Task EachAdmittedStageHasItsOwnDeadlineAndReleasesCandidate(string stage, int seconds)
    {
        var logger = new CaptureLogger(); var clock = new ManualClock(); var setup = Create(logger, clock);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hang(CancellationToken token)
        { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
        if (stage == "claim") setup.Repository.Claim = async token => { await Hang(token); return null; };
        else
        {
            await RunSteps(setup, 1);
            if (stage == "lookup") setup.Gamma.Lookup = async (_, token) => { await Hang(token); return []; };
            else
            {
                if (stage == "defer") setup.Gamma.Lookup = (_, _) => Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([]);
                await RunSteps(setup, 1);
                if (stage == "apply") setup.Repository.Confirm = async (_, token, _) => { await Hang(token); return new(true, false, "unexpected"); };
                else setup.Repository.Defer = Hang;
            }
        }
        var running = setup.Worker.ProcessIdleGapAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1));
        Assert.False(running.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await running.WaitAsync(TimeSpan.FromSeconds(1));
        if (stage is "lookup" or "apply") await RunSteps(setup, 1); // Persist retry in its own gap.
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("Timeout", log["Outcome"]);
        Assert.Equal(stage == "lookup" ? "GammaTimeout" : "DatabaseTimeout", log["Reason"]);
        Assert.Equal(false, log["CommitAcknowledged"]);
        // A terminal timeout does not retain the order or keep the stage alive.
        setup.Repository.Claim = _ => Task.FromResult<PaperOrder?>(null);
        await RunSteps(setup, 1);
        Assert.Single(logger.Attempts);
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    public async Task WaitingResultKeepsCorrelationAndBoundedTrace_AndStopReleasesIt()
    {
        var logger = new CaptureLogger(); var clock = new ManualClock(); var setup = Create(logger, clock);
        var lookups = 0;
        setup.Gamma.Lookup = (_, _) => { lookups++; return Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([PaperOutcomeConfirmationProcessorTests.Metadata()]); };
        await RunSteps(setup, 2);
        using var foreground = setup.Activity.EnterTradingCycle("Trading");
        await setup.Worker.StartAsync(default);
        await UntilAsync(() => clock.ActiveTimers == 2);
        clock.Advance(TimeSpan.FromSeconds(30));
        await UntilAsync(() => logger.Entries.Any(x => x.ContainsKey("@Summary")));
        var first = JsonSerializer.SerializeToElement(logger.Entries.Last(x => x.ContainsKey("@Summary"))["@Summary"])
            .GetProperty("ActiveAttempt");
        for (var i = 0; i < 1000; i++) await setup.Worker.ProcessIdleGapAsync(default);
        clock.Advance(TimeSpan.FromSeconds(30));
        await UntilAsync(() => logger.Entries.Count(x => x.ContainsKey("@Summary")) == 2);
        var second = JsonSerializer.SerializeToElement(logger.Entries.Last(x => x.ContainsKey("@Summary"))["@Summary"])
            .GetProperty("ActiveAttempt");
        Assert.Equal((int)PaperConfirmationStage.WaitingForApplyIdle, second.GetProperty("Stage").GetInt32());
        Assert.Equal(first.GetProperty("AttemptId").GetGuid(), second.GetProperty("AttemptId").GetGuid());
        Assert.Equal(first.GetProperty("Stages").GetArrayLength(), second.GetProperty("Stages").GetArrayLength());
        Assert.Equal("Waiting", second.GetProperty("Outcome").GetString());
        Assert.Equal(1, lookups);
        Assert.Empty(logger.Attempts);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await setup.Worker.StopAsync(deadline.Token);
        var log = Assert.Single(logger.Attempts);
        Assert.Equal("ServiceStopping", log["Reason"]);
        Assert.Equal(setup.Order.Id, log["PaperOrderId"]);
        Assert.Equal(first.GetProperty("AttemptId").GetGuid(), log["AttemptId"]);
        foreground.Dispose();
        setup.Repository.Claim = _ => Task.FromResult<PaperOrder?>(null);
        await RunSteps(setup, 1); // Explicit call verifies no retained Apply after Stop.
        Assert.Single(logger.Attempts);
    }

    private static async Task RunSteps(Setup setup, int count)
    { for (var i = 0; i < count; i++) await setup.Worker.ProcessIdleGapAsync(default); }
    private static PaperConfirmationQueueSnapshot Queues(ManualClock clock) => new(clock.GetUtcNow(),0,0,0,0,0,0,0);
    private static async Task UntilAsync(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!done()) await Task.Delay(5, timeout.Token);
    }
    private sealed record Setup(PaperOutcomeConfirmationWorker Worker, ServiceActivityState Activity,
        RepositoryProxy Repository, Gamma Gamma, PaperOrder Order, Market Market);
    private static Setup Create(CaptureLogger logger, TimeProvider? clock = null)
    {
        var repository = DispatchProxy.Create<IAppRepository, RepositoryProxy>();
        var proxy = (RepositoryProxy)repository;
        var order = PaperOutcomeConfirmationProcessorTests.Order();
        proxy.Claim = _ => Task.FromResult<PaperOrder?>(order);
        var gamma = new Gamma(); var activity = new ServiceActivityState();
        var processor = new PaperOutcomeConfirmationProcessor(NullLogger<PaperOutcomeConfirmationProcessor>.Instance,
            repository, gamma, new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, new NoOpAppRepository()));
        var market = new Market();
        return new(new(logger, activity, new Entries(), market, processor, clock), activity, proxy, gamma, order, market);
    }
    public class RepositoryProxy : DispatchProxy
    {
        public Func<CancellationToken, Task<PaperOrder?>> Claim = _ => Task.FromResult<PaperOrder?>(null);
        public Func<PaperOutcomeConfirmation,CancellationToken,PaperOutcomeConfirmationTrace?,Task<PaperOutcomeConfirmationResult>> Confirm = (_,_,_) => Task.FromResult(new PaperOutcomeConfirmationResult(true,false,"confirmed"));
        public Func<CancellationToken,Task> Defer = _ => Task.CompletedTask;
        public int Defers;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            nameof(IAppRepository.TryClaimPaperOutcomeConfirmationAsync) => Claim((CancellationToken)args![1]!),
            nameof(IAppRepository.ConfirmPaperOutcomeAsync) => Confirm((PaperOutcomeConfirmation)args![0]!, (CancellationToken)args[1]!, (PaperOutcomeConfirmationTrace?)args[2]),
            nameof(IAppRepository.DeferPaperOutcomeConfirmationAsync) => DeferCall((CancellationToken)args![3]!),
            _ => throw new NotSupportedException(method.Name)
        };
        private Task DeferCall(CancellationToken token) { Defers++; return Defer(token); }
    }
    private sealed class Gamma : IPolymarketGammaClient
    {
        public Func<bool,CancellationToken,Task<IReadOnlyList<PolymarketOnChainTokenMetadata>>> Lookup = (_,_) => Task.FromResult<IReadOnlyList<PolymarketOnChainTokenMetadata>>([PaperOutcomeConfirmationProcessorTests.Metadata()]);
        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataAsync(string tokenId,bool closed,CancellationToken cancellationToken=default)=>Lookup(false,cancellationToken);
        public Task<IReadOnlyList<PolymarketOnChainTokenMetadata>> GetTokenMetadataByConditionIdAsync(string conditionId,string requestedTokenId,bool closed,CancellationToken cancellationToken=default)=>Lookup(true,cancellationToken);
        public Task<IReadOnlyList<PolymarketGammaMarket>> GetActiveMarketsAsync(int limit=500,int offset=0,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<string?> GetEventCategoryAsync(string eventId,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class Entries : IPaperEntryPersistenceQueue
    {
        public int PendingBatches => 0;
        public ValueTask EnqueueAsync(PaperEntryPersistenceBatch batch,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class Market : IMarketDataSideEffectQueue
    {
        public Exception? Failure;
        public MarketDataSideEffectQueueMetrics GetMetrics()=>Failure is { } error ? throw error : new(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);
        public MarketDataSideEffectEnqueueOutcome EnqueueUpdate(string component,MarketDataUpdate update,ActiveMarketAssetSnapshot? activeMarketSnapshot,DateTimeOffset receivedAtUtc,IReadOnlySet<Guid>? eligiblePaperOrderIds)=>throw new NotSupportedException();
        public MarketDataSideEffectEnqueueOutcome EnqueueFrameDiagnostic(MarketWebSocketFrameDiagnostic diagnostic,bool important)=>throw new NotSupportedException();
        public MarketDataSideEffectEnqueueOutcome EnqueueApiError(ApiError error)=>throw new NotSupportedException();
    }
    private sealed class CaptureLogger : ILogger<PaperOutcomeConfirmationWorker>
    {
        public bool FailSummary;
        public ConcurrentQueue<Dictionary<string,object?>> Entries { get; } = new();
        public IEnumerable<Dictionary<string,object?>> Attempts => Entries.Where(x=>x.ContainsKey("AttemptId"));
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> formatter)
        {
            var fields = ((IEnumerable<KeyValuePair<string,object?>>)state!).ToDictionary(x=>x.Key,x=>x.Value);
            if (FailSummary && fields.ContainsKey("@Summary")) throw new InvalidOperationException("summary logger unavailable");
            Entries.Enqueue(fields);
        }
    }
    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        private readonly List<ManualTimer> timers=[];
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>Interlocked.Read(ref ticks);
        public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
        public int TimerCount { get { lock(timers) return timers.Count; } }
        public int ActiveTimers { get { lock(timers) return timers.Count(x => !x.Disposed); } }
        public override ITimer CreateTimer(TimerCallback callback,object? state,TimeSpan dueTime,TimeSpan period)
        { var timer=new ManualTimer(this,callback,state,dueTime,period);lock(timers)timers.Add(timer);return timer; }
        public void Advance(TimeSpan by)
        { Interlocked.Add(ref ticks,by.Ticks);ManualTimer[] copy;lock(timers)copy=timers.ToArray();foreach(var timer in copy)timer.Fire(); }
        private sealed class ManualTimer(ManualClock clock,TimerCallback callback,object? state,TimeSpan due,TimeSpan period):ITimer
        {
            private long next=clock.GetTimestamp()+due.Ticks; private bool disposed;
            public bool Disposed => disposed;
            public void Fire(){if(!disposed&&clock.GetTimestamp()>=next){next=period==Timeout.InfiniteTimeSpan?long.MaxValue:clock.GetTimestamp()+period.Ticks;callback(state);}}
            public bool Change(TimeSpan dueTime,TimeSpan newPeriod){next=clock.GetTimestamp()+dueTime.Ticks;period=newPeriod;return true;}
            public void Dispose()=>disposed=true;
            public ValueTask DisposeAsync(){Dispose();return ValueTask.CompletedTask;}
        }
    }
}
