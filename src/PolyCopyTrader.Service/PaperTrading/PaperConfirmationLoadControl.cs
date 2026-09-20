using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.PaperTrading;

public sealed class PaperConfirmationLoadControl(PaperConfirmationOptions options)
{
    private readonly object sync = new();
    private long? lastFailures;
    private int lastPending, growth;
    private DateTimeOffset pauseUntil;
    private int batchSize = options.MaxBatchSize;
    public int BatchSize { get { lock (sync) return batchSize; } }
    public bool IsPaused(DateTimeOffset now) { lock (sync) return now < pauseUntil; }
    public void Observe(DateTimeOffset now, int pending, long failures)
    {
        lock (sync)
        {
            growth = pending > lastPending ? growth + 1 : 0;
            if (growth >= 3 || lastFailures is not null && failures > lastFailures)
            {
                pauseUntil = now.AddSeconds(options.OverloadPauseSeconds);
                growth = 0;
            }
            lastPending = pending;
            lastFailures = failures;
        }
    }
    public void Timeout(DateTimeOffset now)
    {
        lock (sync)
        {
            batchSize = Math.Max(1, batchSize / 2);
            pauseUntil = now.AddSeconds(options.OverloadPauseSeconds);
        }
    }
    public void Succeeded()
    {
        lock (sync) batchSize = Math.Min(options.MaxBatchSize, batchSize + 1);
    }
}
