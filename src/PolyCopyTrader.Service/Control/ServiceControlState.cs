using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Control;

public sealed class ServiceControlState
{
    private readonly object sync = new();
    private ServiceRunState runState = ServiceRunState.Starting;
    private bool scanningPaused;
    private bool paperTradingPaused;
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
            runState = scanningPaused || paperTradingPaused ? ServiceRunState.Paused : ServiceRunState.Running;
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
                ? scanningPaused || paperTradingPaused ? ServiceRunState.Paused : ServiceRunState.Running
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
            runState = scanningPaused || paperTradingPaused ? ServiceRunState.Paused : ServiceRunState.Running;
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
            runState = scanningPaused || paperTradingPaused ? ServiceRunState.Paused : ServiceRunState.Running;
            return new ServiceCommandResult("ResumePaperTrading", source, true, "Paper trading resumed.");
        }
    }

    public ServiceCommandResult PauseAll(string source)
    {
        lock (sync)
        {
            scanningPaused = true;
            paperTradingPaused = true;
            runState = ServiceRunState.Paused;
            return new ServiceCommandResult("Pause", source, true, "Scanning and paper trading paused.");
        }
    }

    public ServiceCommandResult ResumeAll(string source)
    {
        lock (sync)
        {
            scanningPaused = false;
            paperTradingPaused = false;
            runState = ServiceRunState.Running;
            return new ServiceCommandResult("Resume", source, true, "Scanning and paper trading resumed.");
        }
    }
}

public sealed record ServiceControlSnapshot(
    ServiceRunState RunState,
    bool ScanningPaused,
    bool PaperTradingPaused,
    string CurrentLoop,
    string? LastError,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset SnapshotAtUtc);

public sealed record ServiceCommandResult(
    string Command,
    string Source,
    bool Accepted,
    string Message);
