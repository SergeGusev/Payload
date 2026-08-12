using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperFakFeeBackfillEventRecorder
{
    Task RecordAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken = default);
}

public sealed class RepositoryPaperFakFeeBackfillEventRecorder : IPaperFakFeeBackfillEventRecorder
{
    internal static readonly TimeSpan PersistenceTimeout = TimeSpan.FromSeconds(2);
    internal const int MaxExceptionTypeLength = 512;
    internal const int MaxExceptionMessageLength = 16 * 1024;

    private readonly ILogger<RepositoryPaperFakFeeBackfillEventRecorder> logger;
    private readonly IAppRepository repository;
    private readonly Guid workerInstanceId = Guid.NewGuid();
    private readonly string buildVersion = ServiceBuildVersion.GetHeartbeatVersion();
    private readonly string hostName = Environment.MachineName;
    private readonly int processId = Environment.ProcessId;
    private long sequence;

    public RepositoryPaperFakFeeBackfillEventRecorder(
        ILogger<RepositoryPaperFakFeeBackfillEventRecorder> logger,
        IAppRepository repository)
    {
        this.logger = logger;
        this.repository = repository;
    }

    public async Task RecordAsync(
        PaperFakFeeBackfillEvent entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var stamped = entry with
            {
                Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
                WorkerInstanceId = entry.WorkerInstanceId == Guid.Empty
                    ? workerInstanceId
                    : entry.WorkerInstanceId,
                Sequence = Interlocked.Increment(ref sequence),
                OccurredAtUtc = entry.OccurredAtUtc == default
                    ? DateTimeOffset.UtcNow
                    : entry.OccurredAtUtc.ToUniversalTime(),
                BuildVersion = string.IsNullOrWhiteSpace(entry.BuildVersion)
                    ? buildVersion
                    : entry.BuildVersion,
                HostName = string.IsNullOrWhiteSpace(entry.HostName)
                    ? hostName
                    : entry.HostName,
                ProcessId = entry.ProcessId == 0 ? processId : entry.ProcessId,
                ExceptionType = Truncate(entry.ExceptionType, MaxExceptionTypeLength),
                ExceptionMessage = Truncate(entry.ExceptionMessage, MaxExceptionMessageLength)
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PersistenceTimeout);
            await repository.AddPaperFakFeeBackfillEventAsync(
                stamped,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal service shutdown must not be reported as a persistence failure.
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Historical Paper FAK fee backfill database-event persistence failed. " +
                "EventType={EventType}. File logging remains active.",
                entry.EventType);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
