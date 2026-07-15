namespace PolyCopyTrader.Service.Strategies;

internal interface IFixedRateTickSource : IDisposable
{
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}

internal static class FixedRateWorkerLoop
{
    public static async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> processCycleAsync,
        CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The fixed-rate interval must be positive.");
        }

        ArgumentNullException.ThrowIfNull(processCycleAsync);

        using var tickSource = new PeriodicTimerTickSource(interval);
        await RunAsync(tickSource, processCycleAsync, cancellationToken);
    }

    internal static async Task RunAsync(
        IFixedRateTickSource tickSource,
        Func<CancellationToken, Task> processCycleAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tickSource);
        ArgumentNullException.ThrowIfNull(processCycleAsync);

        while (!cancellationToken.IsCancellationRequested)
        {
            await processCycleAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!await tickSource.WaitForNextTickAsync(cancellationToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private sealed class PeriodicTimerTickSource(TimeSpan interval) : IFixedRateTickSource
    {
        private readonly PeriodicTimer timer = new(interval);

        public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
        {
            return timer.WaitForNextTickAsync(cancellationToken);
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }
}
