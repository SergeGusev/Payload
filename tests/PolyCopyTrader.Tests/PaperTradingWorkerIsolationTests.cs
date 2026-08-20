using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Control;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class PaperTradingWorkerIsolationTests
{
    [Fact]
    public async Task BlockedPositionMarkWorker_DoesNotBlockOpenOrderWorker()
    {
        var markProcessor = new BlockingPositionMarkProcessor();
        var orderProcessor = new SignalingPaperTradingProcessor();
        var repository = new TestAppRepository();
        var botOptions = new BotOptions { Mode = BotMode.Paper };
        var paperOptions = new PaperTradingOptions { OpenOrderProcessingIntervalSeconds = 1 };
        var controlState = new ServiceControlState();
        var markWorker = new PaperPositionMarkWorker(
            NullLogger<PaperPositionMarkWorker>.Instance,
            botOptions,
            paperOptions,
            markProcessor,
            controlState,
            repository);
        var orderWorker = new PaperTradingWorker(
            NullLogger<PaperTradingWorker>.Instance,
            botOptions,
            paperOptions,
            orderProcessor,
            controlState,
            repository);

        await markWorker.StartAsync(CancellationToken.None);
        try
        {
            await markProcessor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await orderWorker.StartAsync(CancellationToken.None);
            await orderProcessor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(TimeSpan.FromMilliseconds(1200));

            Assert.Equal(1, markProcessor.Calls);
            Assert.True(orderProcessor.Calls >= 1);
            Assert.False(markProcessor.Completed.Task.IsCompleted);
        }
        finally
        {
            await orderWorker.StopAsync(CancellationToken.None);
            await markWorker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class BlockingPositionMarkProcessor : IPaperPositionMarkProcessor
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public async Task<int> RefreshPositionMarksAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            finally
            {
                Completed.TrySetResult();
            }
        }
    }

    private sealed class SignalingPaperTradingProcessor : IPaperTradingProcessor
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task<PaperTradingProcessingResult> ProcessOpenOrdersAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            return Task.FromResult(new PaperTradingProcessingResult(0, 0, 0, 0));
        }
    }
}
