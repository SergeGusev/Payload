using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainPaperSignalWorker(
    ILogger<OnChainPaperSignalWorker> logger,
    OnChainIngestionOptions options,
    IOnChainPaperSignalProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.PaperSignalEnabled || !options.PaperSignalBacklogEnabled)
        {
            logger.LogInformation("On-chain paper signal backlog worker is disabled.");
            return;
        }

        var pollDelay = TimeSpan.FromMilliseconds(options.PaperSignalPollDelayMilliseconds);
        var baseErrorDelay = TimeSpan.FromMilliseconds(options.TradeCaptureErrorDelayMilliseconds);
        var maxErrorDelay = TimeSpan.FromMilliseconds(options.TradeCaptureMaxErrorDelayMilliseconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "On-chain paper signal worker started. PollDelayMilliseconds={PollDelayMilliseconds} BatchSize={BatchSize}",
            options.PaperSignalPollDelayMilliseconds,
            options.PaperSignalBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.ProcessOnceAsync(stoppingToken);
                currentErrorDelay = baseErrorDelay;

                if (result.CandidatesFetched > 0 || result.PaperOrdersCreated > 0 || result.Errors > 0)
                {
                    logger.LogInformation(
                        "On-chain paper signal cycle completed. Candidates={Candidates} Signals={Signals} Accepted={Accepted} Rejected={Rejected} PaperOrders={PaperOrders} Errors={Errors}",
                        result.CandidatesFetched,
                        result.SignalsCreated,
                        result.SignalsAccepted,
                        result.SignalsRejected,
                        result.PaperOrdersCreated,
                        result.Errors);
                }

                await DelayPollAsync(pollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex) when (IsAlreadyRunning(ex))
            {
                logger.LogInformation("On-chain paper signal processing skipped because another run is active.");
                await DelayPollAsync(pollDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "On-chain paper signal cycle failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
                await TryRecordApiErrorAsync("ProcessOnChainPaperSignals", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("On-chain paper signal worker stopped.");
    }

    private static bool IsAlreadyRunning(Exception ex)
    {
        return ex.Message.Contains("already running", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan NextErrorDelay(TimeSpan current, TimeSpan max)
    {
        return TimeSpan.FromMilliseconds(Math.Min(current.TotalMilliseconds * 2, max.TotalMilliseconds));
    }

    private static async Task DelayPollAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            await Task.Yield();
            return;
        }

        await Task.Delay(delay, cancellationToken);
    }

    private async Task TryRecordApiErrorAsync(string operation, string message, CancellationToken cancellationToken)
    {
        try
        {
            await repository.AddApiErrorAsync(
                new ApiError(Guid.NewGuid(), "OnChainPaperSignalWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist on-chain paper signal error.");
        }
    }
}
