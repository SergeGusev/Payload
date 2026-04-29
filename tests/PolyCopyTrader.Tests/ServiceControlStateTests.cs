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
}
