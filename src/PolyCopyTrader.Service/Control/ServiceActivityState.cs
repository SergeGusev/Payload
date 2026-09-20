using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.Control;

public readonly record struct ServiceActivitySource(string Name, MarketDataEventType? EventType = null);
public sealed record ServiceActivitySnapshot(string Reason, int ActiveCount,
    IReadOnlyDictionary<ServiceActivitySource, int> Sources);
public sealed record ServiceActivityCancellation(string Reason, ServiceActivitySource? Source);

/// <summary>Foreground activity never waits for the background verifier.</summary>
public sealed class ServiceActivityState
{
    private readonly object sync = new();
    private readonly Dictionary<ServiceActivitySource, int> sources = [];
    private int active;
    private BackgroundLease? background;

    public IDisposable EnterTradingCycle(string source = "Unknown", MarketDataEventType? eventType = null)
    {
        var key = new ServiceActivitySource(source, eventType);
        lock (sync)
        {
            active++;
            sources.TryGetValue(key, out var count);
            sources[key] = count + 1;
            background?.Cancel("Foreground", key);
        }
        return new TradingLease(this, key);
    }

    public BackgroundLease? TryEnterIdle(Func<bool> queuesEmpty, CancellationToken stoppingToken) =>
        TryEnterIdle(queuesEmpty, stoppingToken, out _);

    public BackgroundLease? TryEnterIdle(Func<bool> queuesEmpty, CancellationToken stoppingToken,
        out ServiceActivitySnapshot observation, bool cancelOnForeground = true)
    {
        lock (sync)
        {
            // Preserve the existing predicate and short-circuit ordering.
            var reason = active != 0 ? "ActiveTrading" : background is not null ? "BackgroundBusy" :
                !queuesEmpty() ? "QueuesBusy" : "Idle";
            observation = new(reason, active, new Dictionary<ServiceActivitySource, int>(sources));
            if (reason != "Idle") return null;
            return background = new BackgroundLease(this, queuesEmpty, stoppingToken, cancelOnForeground);
        }
    }

    private sealed class TradingLease(ServiceActivityState owner, ServiceActivitySource source) : IDisposable
    {
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lock (owner.sync)
            {
                owner.active--;
                if (--owner.sources[source] == 0) owner.sources.Remove(source);
            }
        }
    }

    public sealed class BackgroundLease : IDisposable
    {
        private readonly ServiceActivityState owner;
        private readonly Func<bool> queuesEmpty;
        private readonly CancellationTokenSource cancellation;
        private readonly CancellationToken stoppingToken;
        private readonly bool cancelOnForeground;
        private Task cancellationCompletion = Task.CompletedTask;
        private ServiceActivityCancellation? firstCancellation;
        private bool disposed;
        public PaperOutcomeConfirmationTrace? Trace { get; set; }

        internal BackgroundLease(ServiceActivityState owner, Func<bool> queuesEmpty, CancellationToken token,
            bool cancelOnForeground)
        {
            this.owner = owner;
            this.queuesEmpty = queuesEmpty;
            stoppingToken = token;
            this.cancelOnForeground = cancelOnForeground;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

        public CancellationToken Token => cancellation.Token;
        public ServiceActivityCancellation Cancellation
        {
            get { lock (owner.sync) return firstCancellation ??
                new(stoppingToken.IsCancellationRequested ? "ServiceStopping" : "Unknown", null); }
        }
        internal void Cancel(string reason, ServiceActivitySource? source = null)
        {
            if (!cancelOnForeground) return;
            if (cancellation.IsCancellationRequested) return;
            firstCancellation = new(reason, source);
            // No logging, formatting, or synchronous cancellation callbacks on this path.
            cancellationCompletion = cancellation.CancelAsync();
        }

        public void CheckIdle()
        {
            lock (owner.sync)
            {
                if (owner.active != 0) Cancel("Foreground");
                else if (!queuesEmpty()) Cancel("QueuesBusy");
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
