using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PaperFakFeeBackfillEventServiceTests
{
    [Fact]
    public async Task Recorder_StampsIdentitySequenceAndBoundedFailureDetails()
    {
        var repository = new TestAppRepository();
        var recorder = new RepositoryPaperFakFeeBackfillEventRecorder(
            NullLogger<RepositoryPaperFakFeeBackfillEventRecorder>.Instance,
            repository);
        var firstId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        await recorder.RecordAsync(new PaperFakFeeBackfillEvent
        {
            Id = firstId,
            OccurredAtUtc = new DateTimeOffset(2026, 8, 12, 15, 0, 0, TimeSpan.FromHours(3)),
            Level = PaperFakFeeBackfillEventLevels.Error,
            EventType = PaperFakFeeBackfillEventTypes.CycleFailed,
            Message = "cycle failed",
            ExceptionType = new string('t', RepositoryPaperFakFeeBackfillEventRecorder.MaxExceptionTypeLength + 1),
            ExceptionMessage = new string('m', RepositoryPaperFakFeeBackfillEventRecorder.MaxExceptionMessageLength + 1)
        });
        await recorder.RecordAsync(new PaperFakFeeBackfillEvent
        {
            Level = PaperFakFeeBackfillEventLevels.Information,
            EventType = PaperFakFeeBackfillEventTypes.CycleCompleted,
            Message = "cycle completed"
        });

        Assert.Equal(2, repository.PaperFakFeeBackfillEvents.Count);
        var first = repository.PaperFakFeeBackfillEvents[0];
        var second = repository.PaperFakFeeBackfillEvents[1];
        Assert.Equal(firstId, first.Id);
        Assert.NotEqual(Guid.Empty, first.WorkerInstanceId);
        Assert.Equal(first.WorkerInstanceId, second.WorkerInstanceId);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(TimeSpan.Zero, first.OccurredAtUtc.Offset);
        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.False(string.IsNullOrWhiteSpace(first.BuildVersion));
        Assert.False(string.IsNullOrWhiteSpace(first.HostName));
        Assert.True(first.ProcessId > 0);
        Assert.Equal(
            RepositoryPaperFakFeeBackfillEventRecorder.MaxExceptionTypeLength,
            first.ExceptionType?.Length);
        Assert.Equal(
            RepositoryPaperFakFeeBackfillEventRecorder.MaxExceptionMessageLength,
            first.ExceptionMessage?.Length);
    }

    [Fact]
    public async Task Recorder_DatabaseFailureDoesNotEscapeBackfillPath()
    {
        var repository = new TestAppRepository
        {
            PaperFakFeeBackfillEventFailuresToThrow = 1
        };
        var recorder = new RepositoryPaperFakFeeBackfillEventRecorder(
            NullLogger<RepositoryPaperFakFeeBackfillEventRecorder>.Instance,
            repository);

        var exception = await Record.ExceptionAsync(() => recorder.RecordAsync(
            new PaperFakFeeBackfillEvent
            {
                Level = PaperFakFeeBackfillEventLevels.Error,
                EventType = PaperFakFeeBackfillEventTypes.CycleFailed,
                Message = "database unavailable"
            }));

        Assert.Null(exception);
        Assert.Empty(repository.PaperFakFeeBackfillEvents);
    }

    [Fact]
    public async Task RetentionWorker_UsesFixedTwentyFourHourCutoffAndFiveHundredRowBatch()
    {
        var repository = new TestAppRepository();
        var worker = new PaperFakFeeBackfillEventRetentionWorker(
            NullLogger<PaperFakFeeBackfillEventRetentionWorker>.Instance,
            repository);
        var now = new DateTimeOffset(2026, 8, 12, 18, 30, 0, TimeSpan.FromHours(3));

        var deleted = await worker.RunCleanupCycleAsync(now);

        Assert.Equal(0, deleted);
        Assert.Equal(24, PaperFakFeeBackfillEventRetentionWorker.RetentionHours);
        Assert.Equal(10, PaperFakFeeBackfillEventRetentionWorker.CleanupIntervalMinutes);
        Assert.Equal(500, PaperFakFeeBackfillEventRetentionWorker.CleanupBatchSize);
        var cleanupCall = Assert.Single(repository.PaperFakFeeBackfillEventCleanupCalls);
        Assert.Equal(now.ToUniversalTime().AddHours(-24), cleanupCall.OccurredBeforeUtc);
        Assert.Equal(500, cleanupCall.BatchSize);
    }
}
