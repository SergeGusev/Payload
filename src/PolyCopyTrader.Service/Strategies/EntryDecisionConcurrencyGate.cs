namespace PolyCopyTrader.Service.Strategies;

internal sealed class EntryDecisionConcurrencyGate
{
    private readonly SemaphoreSlim totalCapacity;
    private readonly SemaphoreSlim nonPriorityCapacity;

    public EntryDecisionConcurrencyGate(int maxConcurrency, int reservedPrioritySlots)
    {
        MaxConcurrency = Math.Max(1, maxConcurrency);
        ReservedPrioritySlots = Math.Clamp(
            reservedPrioritySlots,
            0,
            Math.Max(0, MaxConcurrency - 1));
        NonPriorityCapacity = MaxConcurrency - ReservedPrioritySlots;
        totalCapacity = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
        nonPriorityCapacity = new SemaphoreSlim(NonPriorityCapacity, NonPriorityCapacity);
    }

    public int MaxConcurrency { get; }

    public int ReservedPrioritySlots { get; }

    public int NonPriorityCapacity { get; }

    public async ValueTask<IDisposable> EnterAsync(
        bool priority,
        CancellationToken cancellationToken = default)
    {
        var nonPriorityAcquired = false;
        if (!priority)
        {
            await nonPriorityCapacity.WaitAsync(cancellationToken);
            nonPriorityAcquired = true;
        }

        try
        {
            await totalCapacity.WaitAsync(cancellationToken);
            return new Lease(totalCapacity, nonPriorityAcquired ? nonPriorityCapacity : null);
        }
        catch
        {
            if (nonPriorityAcquired)
            {
                nonPriorityCapacity.Release();
            }

            throw;
        }
    }

    private sealed class Lease(
        SemaphoreSlim totalCapacity,
        SemaphoreSlim? nonPriorityCapacity) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            totalCapacity.Release();
            nonPriorityCapacity?.Release();
        }
    }
}
