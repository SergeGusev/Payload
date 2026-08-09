using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdPaperPlacementHandoffTests
{
    [Theory]
    [InlineData(MakerGtdPaperExecutionContract.ExecutionSource)]
    [InlineData(PairedMakerGtdPaperExecutionContract.ExecutionSource)]
    public async Task PlacementAdmission_AcceptsEachClosedMakerGtdExecutionSource(string executionSource)
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var orderId = Guid.NewGuid();
        await using (var admission = await handoff.EnterPlacementAdmissionAsync("asset-1"))
        {
            admission.ActivatePendingOrder(orderId, executionSource);
        }

        Assert.Contains(orderId, handoff.GetPendingOrderIds("asset-1"));
        handoff.MarkPublished(orderId);
        Assert.DoesNotContain(orderId, handoff.GetPendingOrderIds("asset-1"));
    }

    [Fact]
    public async Task PlacementAdmission_RejectsUnknownExecutionSource()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        await using var admission = await handoff.EnterPlacementAdmissionAsync("asset-1");

        Assert.Throws<ArgumentException>(() =>
            admission.ActivatePendingOrder(Guid.NewGuid(), "unknown_maker_source"));
    }

    [Fact]
    public async Task ReceiptAndExpiryAdmissions_ProvideDeterministicNoSleepFence()
    {
        var handoff = new MakerGtdPaperPlacementHandoff();
        var receiptAdmission = await handoff.EnterMarketDataReceiptAsync();

        Assert.Null(await handoff.TryEnterExpiryAdmissionAsync());

        await receiptAdmission.DisposeAsync();
        var expiryAdmission = Assert.IsAssignableFrom<IAsyncDisposable>(
            await handoff.TryEnterExpiryAdmissionAsync());
        var nextReceiptTask = handoff.EnterMarketDataReceiptAsync().AsTask();
        Assert.False(nextReceiptTask.IsCompleted);

        await expiryAdmission.DisposeAsync();
        await using var nextReceiptAdmission = await nextReceiptTask;
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
