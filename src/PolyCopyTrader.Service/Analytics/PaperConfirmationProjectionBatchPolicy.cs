namespace PolyCopyTrader.Service.Analytics;

// In-memory pacing only. The repository transaction owns durable progress and retries.
public sealed class PaperConfirmationProjectionBatchPolicy
{
    public int Limit { get; private set; } = 250;
    public DateTimeOffset RetryAtUtc { get; private set; } = DateTimeOffset.MinValue;
    private int fastSuccesses;

    public bool CanRun(DateTimeOffset now) => now >= RetryAtUtc;

    public void Failed(DateTimeOffset now, string? sqlState)
    {
        fastSuccesses = 0;
        if (sqlState is not ("57014" or "55P03")) return;
        Limit = Math.Max(1, Limit / 2);
        RetryAtUtc = now.AddSeconds(30);
    }

    public void Succeeded(int processed, TimeSpan elapsed)
    {
        if (processed == 0 || elapsed >= TimeSpan.FromMilliseconds(500))
        {
            fastSuccesses = 0;
            return;
        }
        if (++fastSuccesses < 8) return;
        Limit = Math.Min(250, Limit * 2);
        fastSuccesses = 0;
    }
}
