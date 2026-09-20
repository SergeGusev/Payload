namespace PolyCopyTrader.Service.Control;

/// <summary>Foreground activity never waits for the background verifier.</summary>
public sealed class ServiceActivityState
{
    private readonly object sync = new();
    private int active;
    private BackgroundLease? background;

    public IDisposable EnterTradingCycle()
    {
        lock (sync)
        {
            active++;
            // CancelAsync schedules callbacks rather than running database/HTTP callbacks
            // on the latency-sensitive trading thread.
            background?.Cancel();
        }
        return new TradingLease(this);
    }

    public BackgroundLease? TryEnterIdle(Func<bool> queuesEmpty, CancellationToken stoppingToken)
    {
        lock (sync)
        {
            if (active != 0 || background is not null || !queuesEmpty()) return null;
            return background = new BackgroundLease(this, queuesEmpty, stoppingToken);
        }
    }

    private sealed class TradingLease(ServiceActivityState owner) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.sync) owner.active--;
        }
    }

    public sealed class BackgroundLease : IDisposable
    {
        private readonly ServiceActivityState owner;
        private readonly Func<bool> queuesEmpty;
        private readonly CancellationTokenSource cancellation;
        private Task cancellationCompletion = Task.CompletedTask;
        private bool disposed;

        internal BackgroundLease(ServiceActivityState owner, Func<bool> queuesEmpty, CancellationToken token)
        {
            this.owner = owner;
            this.queuesEmpty = queuesEmpty;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public CancellationToken Token => cancellation.Token;
        internal void Cancel()
        {
            if (!cancellation.IsCancellationRequested) cancellationCompletion = cancellation.CancelAsync();
        }

        public void CheckIdle()
        {
            lock (owner.sync)
            {
                if (owner.active != 0 || !queuesEmpty()) Cancel();
                Token.ThrowIfCancellationRequested();
            }
        }

        public void Dispose()
        {
            lock (owner.sync)
            {
                if (disposed) return;
                disposed = true;
                owner.background = null;
                if (cancellationCompletion.IsCompleted) cancellation.Dispose();
                else _ = cancellationCompletion.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
            }
        }
    }
}
