using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperPlacementHandoffTests
{
    [Fact]
    public async Task ReceiptAndExpiryAdmissions_QueuedExpiryPrecedesLaterReceipts()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var activeReceipt = await handoff.EnterMarketDataReceiptAsync();
        var expiryTask = handoff.EnterExpiryAdmissionAsync().AsTask();
        var laterReceiptTasks = Enumerable.Range(0, 64)
            .Select(_ => handoff.EnterMarketDataReceiptAsync().AsTask())
            .ToArray();

        Assert.False(expiryTask.IsCompleted);
        Assert.All(laterReceiptTasks, task => Assert.False(task.IsCompleted));

        await activeReceipt.DisposeAsync();
        await using var expiryAdmission = await expiryTask;
        Assert.All(laterReceiptTasks, task => Assert.False(task.IsCompleted));

        await expiryAdmission.DisposeAsync();
        var laterReceipts = await Task.WhenAll(laterReceiptTasks);
        foreach (var laterReceipt in laterReceipts)
        {
            await laterReceipt.DisposeAsync();
        }
    }

    [Fact]
    public async Task ReceiptAdmissions_RemainConcurrentWithoutPendingExpiry()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();

        await using var firstReceipt = await handoff.EnterMarketDataReceiptAsync();
        var secondReceiptTask = handoff.EnterMarketDataReceiptAsync().AsTask();

        Assert.True(secondReceiptTask.IsCompletedSuccessfully);
        await using var secondReceipt = await secondReceiptTask;
    }

    [Fact]
    public async Task CancelledExpiryAdmission_UnblocksReceiptsAndLeavesHandoffUsable()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var activeReceipt = await handoff.EnterMarketDataReceiptAsync();
        using var cancellation = new CancellationTokenSource();
        var expiryTask = handoff.EnterExpiryAdmissionAsync(cancellation.Token).AsTask();
        var laterReceiptTask = handoff.EnterMarketDataReceiptAsync().AsTask();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expiryTask);
        var laterReceipt = await laterReceiptTask;

        await laterReceipt.DisposeAsync();
        await activeReceipt.DisposeAsync();
        await using var laterExpiry = await handoff.EnterExpiryAdmissionAsync();
    }

    [Fact]
    public async Task MultipleQueuedExpiries_AllPrecedeLaterReceipts()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var activeReceipt = await handoff.EnterMarketDataReceiptAsync();
        var firstExpiryTask = handoff.EnterExpiryAdmissionAsync().AsTask();
        var secondExpiryTask = handoff.EnterExpiryAdmissionAsync().AsTask();
        var laterReceiptTask = handoff.EnterMarketDataReceiptAsync().AsTask();

        await activeReceipt.DisposeAsync();
        var firstExpiry = await firstExpiryTask;
        Assert.False(secondExpiryTask.IsCompleted);
        Assert.False(laterReceiptTask.IsCompleted);

        await firstExpiry.DisposeAsync();
        var secondExpiry = await secondExpiryTask;
        Assert.False(laterReceiptTask.IsCompleted);

        await secondExpiry.DisposeAsync();
        await using var laterReceipt = await laterReceiptTask;
    }

    [Fact]
    public async Task NoOpHandoff_ExpiryAdmissionRemainsNonblockingAndNonNull()
    {
        await using var admission =
            await NoOpMakerGtdPaperPlacementHandoff.Instance.EnterExpiryAdmissionAsync();

        Assert.NotNull(admission);
    }

    [Fact]
    public void FailureLookup_UsesStrictLifetimeExactIdentityAndClear()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var orderId = Guid.NewGuid();
        var otherOrderId = Guid.NewGuid();
        var acceptedAtUtc = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var expiresAtUtc = acceptedAtUtc.AddMinutes(4);
        var affectedOrderIds = new HashSet<Guid> { orderId, otherOrderId };
        handoff.TrackMakerGtdPaperOrder(
            orderId,
            MakerGtdPaperExecutionContract.ExecutionSource);

        handoff.RecordMarketDataFailure(
            "asset-1",
            "condition-1",
            acceptedAtUtc,
            affectedOrderIds,
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
        handoff.RecordMarketDataFailure(
            "asset-1",
            "condition-1",
            expiresAtUtc,
            affectedOrderIds,
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);
        handoff.RecordMarketDataFailure(
            "asset-1",
            "condition-1",
            acceptedAtUtc.AddMinutes(1),
            affectedOrderIds,
            MakerGtdPaperExecutionContract.MarketDataHandlerFailureCode);

        Assert.True(handoff.TryGetMarketDataFailure(
            orderId,
            "asset-1",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc,
            out var failure));
        Assert.Equal(acceptedAtUtc.AddMinutes(1), Assert.IsType<MakerGtdPaperMarketDataFailure>(failure).ReceivedAtUtc);
        Assert.False(handoff.TryGetMarketDataFailure(
            otherOrderId,
            "asset-1",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc,
            out _));
        Assert.False(handoff.TryGetMarketDataFailure(
            orderId,
            "asset-2",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc,
            out _));

        handoff.ClearMarketDataFailures(orderId);

        Assert.False(handoff.TryGetMarketDataFailure(
            orderId,
            "asset-1",
            "condition-1",
            acceptedAtUtc,
            expiresAtUtc,
            out _));
    }
}
