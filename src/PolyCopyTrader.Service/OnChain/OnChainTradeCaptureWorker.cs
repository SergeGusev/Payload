using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Service.OnChain;

public sealed class OnChainTradeCaptureWorker(
    ILogger<OnChainTradeCaptureWorker> logger,
    OnChainIngestionOptions options,
    IOnChainTradeCaptureProcessor processor,
    IAppRepository repository) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.TradeCaptureEnabled)
        {
            logger.LogInformation("On-chain trade capture worker is disabled.");
            return;
        }

        var pollDelay = TimeSpan.FromMilliseconds(options.TradeCapturePollDelayMilliseconds);
        var baseErrorDelay = TimeSpan.FromMilliseconds(options.TradeCaptureErrorDelayMilliseconds);
        var maxErrorDelay = TimeSpan.FromMilliseconds(options.TradeCaptureMaxErrorDelayMilliseconds);
        var currentErrorDelay = baseErrorDelay;

        logger.LogInformation(
            "On-chain trade capture worker started. PollDelayMilliseconds={PollDelayMilliseconds} Confirmations={Confirmations} ErrorDelayMilliseconds={ErrorDelayMilliseconds} MaxErrorDelayMilliseconds={MaxErrorDelayMilliseconds}",
            options.TradeCapturePollDelayMilliseconds,
            options.TradeCaptureConfirmations,
            options.TradeCaptureErrorDelayMilliseconds,
            options.TradeCaptureMaxErrorDelayMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await processor.CaptureOnceAsync(stoppingToken);
                currentErrorDelay = baseErrorDelay;

                if (result.RangesScanned > 0 || result.LogsFetched > 0)
                {
                    logger.LogInformation(
                        "On-chain trade capture cycle completed. LatestBlock={LatestBlock} TargetBlock={TargetBlock} Contracts={Contracts} Ranges={Ranges} Logs={Logs} CapturesStored={CapturesStored} HotCandidates={HotCandidates} HotPaperOrders={HotPaperOrders}",
                        result.LatestBlock,
                        result.TargetBlock,
                        result.ContractsScanned,
                        result.RangesScanned,
                        result.LogsFetched,
                        result.CapturesStored,
                        result.HotCandidatesProcessed,
                        result.HotPaperOrdersCreated);
                }

                await DelayPollAsync(pollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex) when (IsAlreadyRunning(ex))
            {
                logger.LogInformation("On-chain trade capture skipped because another run is active.");
                await DelayPollAsync(pollDelay, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "On-chain trade capture cycle failed. Retrying in {DelayMilliseconds} ms.", currentErrorDelay.TotalMilliseconds);
                await TryRecordApiErrorAsync("CaptureOrderFilled", ex.Message, stoppingToken);
                await Task.Delay(currentErrorDelay, stoppingToken);
                currentErrorDelay = NextErrorDelay(currentErrorDelay, maxErrorDelay);
            }
        }

        logger.LogInformation("On-chain trade capture worker stopped.");
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
                new ApiError(Guid.NewGuid(), "OnChainTradeCaptureWorker", operation, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist on-chain trade capture error.");
        }
    }
}
