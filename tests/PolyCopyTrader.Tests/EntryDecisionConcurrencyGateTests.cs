using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class EntryDecisionConcurrencyGateTests
{
    [Fact]
    public async Task PriorityLeaseUsesReservedSlotWithoutIncreasingTotalConcurrency()
    {
        var gate = new EntryDecisionConcurrencyGate(maxConcurrency: 2, reservedPrioritySlots: 1);
        using var firstRegular = await gate.EnterAsync(priority: false);

        var secondRegularTask = gate.EnterAsync(priority: false).AsTask();
        await AssertStillWaitingAsync(secondRegularTask);

        using var priority = await gate.EnterAsync(priority: true).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        var secondPriorityTask = gate.EnterAsync(priority: true).AsTask();
        await AssertStillWaitingAsync(secondPriorityTask);

        priority.Dispose();
        using var secondPriority = await secondPriorityTask.WaitAsync(TimeSpan.FromSeconds(1));

        firstRegular.Dispose();
        using var secondRegular = await secondRegularTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, gate.MaxConcurrency);
        Assert.Equal(1, gate.ReservedPrioritySlots);
        Assert.Equal(1, gate.NonPriorityCapacity);
    }

    private static async Task AssertStillWaitingAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(50)));
        Assert.NotSame(task, completed);
    }
}
