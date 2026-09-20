using PolyCopyTrader.Service.Control;

namespace PolyCopyTrader.Tests;

public sealed class ServiceActivityStateTests
{
    [Fact]
    public void AdmittedNonPreemptibleStageIgnoresForegroundAndQueueChangesButHonorsStop()
    {
        var activity = new ServiceActivityState(); var empty = true;
        using var stop = new CancellationTokenSource();
        using var idle = activity.TryEnterIdle(() => empty, stop.Token, out _, cancelOnForeground: false)!;
        using (activity.EnterTradingCycle("Quote"))
        {
            empty = false;
            idle.CheckIdle();
            Assert.False(idle.Token.IsCancellationRequested);
            Assert.Null(activity.TryEnterIdle(() => true, default));
        }
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(idle.CheckIdle);
        Assert.Equal("ServiceStopping", idle.Cancellation.Reason);
    }

    [Fact]
    public void ActiveCyclesAndQueuesExcludeBackground_AndNestedCyclesAreCounted()
    {
        var activity = new ServiceActivityState();
        using (var first = activity.EnterTradingCycle())
        {
            using (activity.EnterTradingCycle()) Assert.Null(activity.TryEnterIdle(() => true, default));
            Assert.Null(activity.TryEnterIdle(() => true, default));
        }
        Assert.Null(activity.TryEnterIdle(() => false, default));
        using var idle = activity.TryEnterIdle(() => true, default);
        Assert.NotNull(idle);
        Assert.Null(activity.TryEnterIdle(() => true, default));
    }

    [Fact]
    public async Task ForegroundDoesNotWaitForCancellationCallbacks()
    {
        var activity = new ServiceActivityState();
        using var idle = activity.TryEnterIdle(() => true, default)!;
        using var callbackRelease = new ManualResetEventSlim();
        using var registration = idle.Token.Register(() => callbackRelease.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var foreground = Task.Run(() => activity.EnterTradingCycle());
            using var cycle = await foreground.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(idle.Token.IsCancellationRequested);
            Assert.Throws<OperationCanceledException>(idle.CheckIdle);
        }
        finally { callbackRelease.Set(); }
    }

    [Fact]
    public void NewQueuedWorkPreemptsBeforeNextStage()
    {
        var empty = true;
        var activity = new ServiceActivityState();
        using var idle = activity.TryEnterIdle(() => empty, default)!;
        empty = false;
        Assert.Throws<OperationCanceledException>(idle.CheckIdle);
    }
}
