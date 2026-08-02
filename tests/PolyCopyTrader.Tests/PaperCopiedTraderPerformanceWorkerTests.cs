using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class PaperCopiedTraderPerformanceWorkerTests
{
    [Fact]
    public async Task Worker_ForwardsBoundedProjectionOptions()
    {
        var repository = new TestAppRepository();
        var telemetryState = new DatabaseScanTelemetryState();
        var worker = new PaperCopiedTraderPerformanceWorker(
            NullLogger<PaperCopiedTraderPerformanceWorker>.Instance,
            new PaperTradingOptions
            {
                CopiedTraderPerformanceProjectionEnabled = true,
                CopiedTraderPerformanceRefreshSeconds = 60,
                CopiedTraderPerformanceWalletBatchSize = 17,
                CopiedTraderPerformanceReconciliationWalletBatchSize = 7,
                CopiedTraderPerformanceReconciliationSeedWalletBatchSize = 41
            },
            repository,
            telemetryState);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(
                () => repository.RefreshPaperCopiedTraderPerformanceProjectionCalls > 0 &&
                      !telemetryState.GetHeartbeatSummary().Contains(
                          "CopiedPerformance=pending",
                          StringComparison.Ordinal),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Equal(1, repository.RefreshPaperCopiedTraderPerformanceProjectionCalls);
        Assert.Equal(17, repository.LastPaperCopiedTraderPerformanceWalletBatchSize);
        Assert.Equal(7, repository.LastPaperCopiedTraderPerformanceReconciliationWalletBatchSize);
        Assert.Equal(41, repository.LastPaperCopiedTraderPerformanceReconciliationSeedWalletBatchSize);
        Assert.Contains(
            "Seed(last=unmeasured/unmeasured,total=0/0,lastPositive=none)",
            telemetryState.GetHeartbeatSummary(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_UsesFixedCadenceInsteadOfCycleDurationPlusDelay()
    {
        var firstCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new TestAppRepository();
        repository.PaperCopiedTraderPerformanceRefreshHook = async cancellationToken =>
        {
            if (repository.RefreshPaperCopiedTraderPerformanceProjectionCalls == 1)
            {
                firstCallEntered.TrySetResult();
                await releaseFirstCall.Task.WaitAsync(cancellationToken);
            }
            else if (repository.RefreshPaperCopiedTraderPerformanceProjectionCalls == 2)
            {
                secondCallEntered.TrySetResult();
            }
        };
        var worker = new PaperCopiedTraderPerformanceWorker(
            NullLogger<PaperCopiedTraderPerformanceWorker>.Instance,
            new PaperTradingOptions
            {
                CopiedTraderPerformanceProjectionEnabled = true,
                CopiedTraderPerformanceRefreshSeconds = 1,
                CopiedTraderPerformanceWalletBatchSize = 1,
                CopiedTraderPerformanceReconciliationWalletBatchSize = 1,
                CopiedTraderPerformanceReconciliationSeedWalletBatchSize = 1
            },
            repository);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await firstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(1_200));
            releaseFirstCall.TrySetResult();

            await secondCallEntered.Task.WaitAsync(TimeSpan.FromMilliseconds(800));
        }
        finally
        {
            releaseFirstCall.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(repository.RefreshPaperCopiedTraderPerformanceProjectionCalls >= 2);
    }

    [Fact]
    public async Task Worker_DoesNotRefreshWhenProjectionIsDisabled()
    {
        var repository = new TestAppRepository();
        var worker = new PaperCopiedTraderPerformanceWorker(
            NullLogger<PaperCopiedTraderPerformanceWorker>.Instance,
            new PaperTradingOptions
            {
                CopiedTraderPerformanceProjectionEnabled = false
            },
            repository);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, repository.RefreshPaperCopiedTraderPerformanceProjectionCalls);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The projection worker did not run within the test timeout.");
            }

            await Task.Delay(10);
        }
    }
}
