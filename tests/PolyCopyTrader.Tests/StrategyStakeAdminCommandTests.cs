using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class StrategyStakeAdminCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UpdatesPaperStakeForAllStrategiesWithoutChangingLiveStake()
    {
        var repository = new TestAppRepository();
        var strategyId = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 30).Id;
        repository.StrategySettings[strategyId] = repository.StrategySettings[strategyId] with
        {
            PaperStakeAmount = 2.50m,
            LiveStakeAmount = 7.50m
        };
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteAsync(
            repository,
            5.00m,
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.All(repository.StrategySettings.Values, settings => Assert.Equal(5.00m, settings.PaperStakeAmount));
        Assert.Equal(7.50m, repository.StrategySettings[strategyId].LiveStakeAmount);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesPaperAndLiveStakeForAllStrategies()
    {
        var repository = new TestAppRepository();
        var strategyId = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 30).Id;
        repository.StrategySettings[strategyId] = repository.StrategySettings[strategyId] with
        {
            PaperStakeAmount = 5.00m,
            LiveStakeAmount = 2.50m
        };
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteAsync(
            repository,
            1.00m,
            1.00m,
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.All(repository.StrategySettings.Values, settings => Assert.Equal(1.00m, settings.PaperStakeAmount));
        Assert.All(repository.StrategySettings.Values, settings => Assert.Equal(1.00m, settings.LiveStakeAmount));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNonPositiveStake()
    {
        var repository = new TestAppRepository();
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteAsync(
            repository,
            0m,
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task ExecuteLiveStakesOnlyAsync_EnablesOnlyRequestedStrategyCode()
    {
        var repository = new TestAppRepository();
        var target = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_skip_1").Id;
        var other = StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == "btc_up_down_5m_skip_2").Id;
        repository.StrategySettings[target] = repository.StrategySettings[target] with { LiveStakes = false };
        repository.StrategySettings[other] = repository.StrategySettings[other] with { LiveStakes = true };
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteLiveStakesOnlyAsync(
            repository,
            "btc_up_down_5m_skip_1",
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(repository.StrategySettings[target].LiveStakes);
        Assert.All(
            repository.StrategySettings.Where(item => item.Key != target),
            item => Assert.False(item.Value.LiveStakes));
    }

    [Fact]
    public async Task ExecuteLiveStakesOnlyAsync_RejectsUnknownStrategyCode()
    {
        var repository = new TestAppRepository();
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteLiveStakesOnlyAsync(
            repository,
            "missing_strategy",
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
    }
}
