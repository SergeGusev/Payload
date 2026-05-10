using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Control;

namespace PolyCopyTrader.Tests;

public sealed class ServiceControlStateTests
{
    [Fact]
    public void PauseAndResumeScanning_UpdatesState()
    {
        var state = new ServiceControlState();
        state.MarkRunning();

        var pause = state.PauseScanning("test");
        var paused = state.Snapshot;
        var resume = state.ResumeScanning("test");
        var resumed = state.Snapshot;

        Assert.True(pause.Accepted);
        Assert.True(paused.ScanningPaused);
        Assert.Equal(ServiceRunState.Paused, paused.RunState);
        Assert.True(resume.Accepted);
        Assert.False(resumed.ScanningPaused);
        Assert.Equal(ServiceRunState.Running, resumed.RunState);
    }

    [Fact]
    public void PauseAll_StopsScannerAndPaperTrading()
    {
        var state = new ServiceControlState();
        state.MarkRunning();

        var result = state.PauseAll("test");
        var snapshot = state.Snapshot;

        Assert.True(result.Accepted);
        Assert.True(snapshot.ScanningPaused);
        Assert.True(snapshot.PaperTradingPaused);
        Assert.Equal(ServiceRunState.Paused, snapshot.RunState);
    }

    [Fact]
    public void PauseLiveTradingOnly_DoesNotPauseServiceRunState()
    {
        var state = new ServiceControlState();
        state.MarkRunning();

        var result = state.PauseLiveTrading("test");
        state.RecordLoop("loop", null);
        var snapshot = state.Snapshot;

        Assert.True(result.Accepted);
        Assert.True(snapshot.LiveTradingPaused);
        Assert.False(snapshot.ScanningPaused);
        Assert.False(snapshot.PaperTradingPaused);
        Assert.Equal(ServiceRunState.Running, snapshot.RunState);
    }

    [Fact]
    public void StartupLivePause_RemainsStartingUntilServiceMarksRunning()
    {
        var state = new ServiceControlState();

        state.PauseLiveTrading("startup");
        var starting = state.Snapshot;
        state.MarkRunning();
        var running = state.Snapshot;

        Assert.True(starting.LiveTradingPaused);
        Assert.Equal(ServiceRunState.Starting, starting.RunState);
        Assert.True(running.LiveTradingPaused);
        Assert.Equal(ServiceRunState.Running, running.RunState);
    }

    [Fact]
    public void RecordLoop_KeepsPausedStateWhenOneSubsystemIsPaused()
    {
        var state = new ServiceControlState();
        state.MarkRunning();
        state.PauseScanning("test");

        state.RecordLoop("loop", null);
        var snapshot = state.Snapshot;

        Assert.True(snapshot.ScanningPaused);
        Assert.False(snapshot.PaperTradingPaused);
        Assert.Equal(ServiceRunState.Paused, snapshot.RunState);
    }

    [Fact]
    public void RecordLoop_WithErrorMarksError()
    {
        var state = new ServiceControlState();
        state.MarkRunning();

        state.RecordLoop("loop", "fatal");
        var snapshot = state.Snapshot;

        Assert.Equal(ServiceRunState.Error, snapshot.RunState);
        Assert.Equal("fatal", snapshot.LastError);
    }

    [Fact]
    public void KillSwitch_PausesLiveAndRequiresExplicitClear()
    {
        var state = new ServiceControlState();
        state.MarkRunning();

        var kill = state.ActivateKillSwitch("test");
        var resumeLive = state.ResumeLiveTrading("test");
        var killed = state.Snapshot;
        var clear = state.ClearKillSwitch("test");

        Assert.True(kill.Accepted);
        Assert.False(resumeLive.Accepted);
        Assert.True(killed.KillSwitchActive);
        Assert.True(killed.LiveTradingPaused);
        Assert.True(clear.Accepted);
    }
}
