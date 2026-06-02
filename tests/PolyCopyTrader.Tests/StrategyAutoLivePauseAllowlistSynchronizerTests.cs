using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class StrategyAutoLivePauseAllowlistSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_ClearsPausedStrategiesOutsideAutoLivePauseAllowlist()
    {
        var repository = new TestAppRepository();
        var keepStrategyId = StrategyIds.FollowLeader;
        var clearStrategyId = StrategyIds.CryptoUpDown5mVariants
            .Single(variant => variant.Code == "eth_up_down_5m_skip_bps_7_instant")
            .Id;
        repository.StrategySettings[keepStrategyId] = repository.StrategySettings[keepStrategyId] with
        {
            AutoLivePaused = true
        };
        repository.StrategySettings[clearStrategyId] = repository.StrategySettings[clearStrategyId] with
        {
            AutoLivePaused = true
        };
        var synchronizer = new StrategyAutoLivePauseAllowlistSynchronizer(
            NullLogger<StrategyAutoLivePauseAllowlistSynchronizer>.Instance,
            new LiveTradingOptions { AutoLivePauseStrategies = [StrategyIds.FollowLeaderCode] },
            repository);

        var cleared = await synchronizer.SynchronizeAsync(
            new DateTimeOffset(2026, 6, 2, 18, 30, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(1, cleared);
        Assert.True(repository.StrategySettings[keepStrategyId].AutoLivePaused);
        Assert.False(repository.StrategySettings[clearStrategyId].AutoLivePaused);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyAllowlistClearsEveryAutoLivePauseFlag()
    {
        var repository = new TestAppRepository();
        var firstStrategyId = StrategyIds.FollowLeader;
        var secondStrategyId = StrategyIds.CryptoUpDown5mVariants
            .Single(variant => variant.Code == "eth_up_down_5m_skip_bps_7_instant")
            .Id;
        repository.StrategySettings[firstStrategyId] = repository.StrategySettings[firstStrategyId] with
        {
            AutoLivePaused = true
        };
        repository.StrategySettings[secondStrategyId] = repository.StrategySettings[secondStrategyId] with
        {
            AutoLivePaused = true
        };
        var synchronizer = new StrategyAutoLivePauseAllowlistSynchronizer(
            NullLogger<StrategyAutoLivePauseAllowlistSynchronizer>.Instance,
            new LiveTradingOptions(),
            repository);

        var cleared = await synchronizer.SynchronizeAsync(
            new DateTimeOffset(2026, 6, 2, 18, 30, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(2, cleared);
        Assert.False(repository.StrategySettings[firstStrategyId].AutoLivePaused);
        Assert.False(repository.StrategySettings[secondStrategyId].AutoLivePaused);
    }
}
