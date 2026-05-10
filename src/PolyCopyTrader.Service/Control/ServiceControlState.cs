using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Control;

public sealed class ServiceControlState
{
    private readonly object sync = new();
    private ServiceRunState runState = ServiceRunState.Starting;
    private bool scanningPaused;
    private bool paperTradingPaused;
    private bool liveTradingPaused;
    private bool killSwitchActive;
    private string currentLoop = "Starting";
    private string? lastError;

    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public ServiceRunState RunState
    {
        get
        {
            lock (sync)
            {
                return runState;
            }
        }
    }

    public bool ScanningPaused
    {
        get
        {
            lock (sync)
            {
                return scanningPaused;
            }
        }
    }

    public bool PaperTradingPaused
    {
        get
        {
            lock (sync)
            {
                return paperTradingPaused;
            }
        }
    }

    public bool LiveTradingPaused
    {
        get
        {
            lock (sync)
            {
                return liveTradingPaused || killSwitchActive;
            }
        }
    }

    public bool KillSwitchActive
    {
        get
        {
            lock (sync)
            {
                return killSwitchActive;
            }
        }
    }

    public ServiceControlSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return new ServiceControlSnapshot(
                    runState,
                    scanningPaused,
                    paperTradingPaused,
                    liveTradingPaused || killSwitchActive,
                    killSwitchActive,
                    currentLoop,
                    lastError,
                    StartedAtUtc,
                    DateTimeOffset.UtcNow);
            }
        }
    }

    public void MarkRunning()
    {
        lock (sync)
        {
            runState = ComputeActiveRunState();
        }
    }

    public void MarkStopping()
    {
        lock (sync)
        {
            runState = ServiceRunState.Stopping;
        }
    }

    public void MarkStopped()
    {
        lock (sync)
        {
            runState = ServiceRunState.Stopped;
        }
    }

    public void MarkError(string message)
    {
        lock (sync)
        {
            runState = ServiceRunState.Error;
            lastError = message;
        }
    }

    public void RecordLoop(string loop, string? error)
    {
        lock (sync)
        {
            currentLoop = loop;
            lastError = error;
            runState = error is null
                ? ComputeActiveRunState()
                : ServiceRunState.Error;
        }
    }

    public ServiceCommandResult PauseScanning(string source)
    {
        lock (sync)
        {
            scanningPaused = true;
            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("PauseScanning", source, true, "Scanning paused.");
        }
    }

    public ServiceCommandResult ResumeScanning(string source)
    {
        lock (sync)
        {
            scanningPaused = false;
            runState = ComputeActiveRunState();
            return new ServiceCommandResult("ResumeScanning", source, true, "Scanning resumed.");
        }
    }

    public ServiceCommandResult PausePaperTrading(string source)
    {
        lock (sync)
        {
            paperTradingPaused = true;
            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("PausePaperTrading", source, true, "Paper trading paused.");
        }
    }

    public ServiceCommandResult ResumePaperTrading(string source)
    {
        lock (sync)
        {
            paperTradingPaused = false;
            runState = ComputeActiveRunState();
            return new ServiceCommandResult("ResumePaperTrading", source, true, "Paper trading resumed.");
        }
    }

    public ServiceCommandResult PauseLiveTrading(string source)
    {
        lock (sync)
        {
            liveTradingPaused = true;
            runState = ComputeActiveRunStatePreservingStarting();
            return new ServiceCommandResult("PauseLiveTrading", source, true, "Live trading paused.");
        }
    }

    public ServiceCommandResult ResumeLiveTrading(string source)
    {
        lock (sync)
        {
            if (killSwitchActive)
            {
                return new ServiceCommandResult("ResumeLiveTrading", source, false, "Kill switch is active. Clear it before resuming live trading.");
            }

            liveTradingPaused = false;
            runState = ComputeActiveRunState();
            return new ServiceCommandResult("ResumeLiveTrading", source, true, "Live trading resumed.");
        }
    }

    public ServiceCommandResult ActivateKillSwitch(string source)
    {
        lock (sync)
        {
            scanningPaused = true;
            paperTradingPaused = true;
            liveTradingPaused = true;
            killSwitchActive = true;
            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("KillSwitch", source, true, "Kill switch active. Scanning, paper trading, and live trading paused.");
        }
    }

    public ServiceCommandResult ClearKillSwitch(string source)
    {
        lock (sync)
        {
            killSwitchActive = false;
            runState = ComputeActiveRunState();
            return new ServiceCommandResult("ClearKillSwitch", source, true, "Kill switch cleared. Resume subsystems explicitly as needed.");
        }
    }

    public ServiceCommandResult PauseAll(string source)
    {
        lock (sync)
        {
            scanningPaused = true;
            paperTradingPaused = true;
            liveTradingPaused = true;
            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("Pause", source, true, "Scanning, paper trading, and live trading paused.");
        }
    }

    public ServiceCommandResult ResumeAll(string source)
    {
        lock (sync)
        {
            scanningPaused = false;
            paperTradingPaused = false;
            if (!killSwitchActive)
            {
                liveTradingPaused = false;
                runState = ServiceRunState.Running;
                return new ServiceCommandResult("Resume", source, true, "Scanning, paper trading, and live trading resumed.");
            }

            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("Resume", source, true, "Scanning and paper trading resumed. Live trading remains paused because kill switch is active.");
        }
    }

    private ServiceRunState ComputeActiveRunState()
    {
        return scanningPaused || paperTradingPaused || killSwitchActive
            ? ServiceRunState.Paused
            : ServiceRunState.Running;
    }

    private ServiceRunState ComputeActiveRunStatePreservingStarting()
    {
        return runState == ServiceRunState.Starting
            ? ServiceRunState.Starting
            : ComputeActiveRunState();
    }
}

public sealed record ServiceControlSnapshot(
    ServiceRunState RunState,
    bool ScanningPaused,
    bool PaperTradingPaused,
    bool LiveTradingPaused,
    bool KillSwitchActive,
    string CurrentLoop,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset SnapshotAtUtc);

public sealed record ServiceCommandResult(
    string Command,
    string Source,
    bool Accepted,
    string Message);
