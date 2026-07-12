using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class StrategyStakeAdminCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UpdatesPaperStakeForAllStrategiesWithoutChangingLiveStake()
    {
        var repository = new TestAppRepository();
        var strategyId = GetBtcStrategyId("btc_up_down_5m_up_bps_2_instant");
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
        var strategyId = GetBtcStrategyId("btc_up_down_5m_up_bps_2_instant");
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
        const string targetCode = "btc_up_down_5m_up_bps_2_instant";
        var target = GetBtcStrategyId(targetCode);
        var other = GetBtcStrategyId("btc_up_down_5m_down_bps_2_instant");
        repository.StrategySettings[target] = repository.StrategySettings[target] with { LiveStakes = false };
        repository.StrategySettings[other] = repository.StrategySettings[other] with { LiveStakes = true };
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteLiveStakesOnlyAsync(
            repository,
            targetCode,
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(repository.StrategySettings[target].LiveStakes);
        Assert.All(
            repository.StrategySettings.Where(item => item.Key != target),
            item => Assert.False(item.Value.LiveStakes));
    }

    [Fact]
    public async Task ExecuteLiveStakesOnlyAsync_EnablesOnlyRequestedStrategyCodes()
    {
        var repository = new TestAppRepository();
        var first = GetBtcStrategyId("btc_up_down_5m_up_bps_2_instant");
        var second = GetBtcStrategyId("btc_up_down_5m_down_bps_2_instant");
        var third = GetBtcStrategyId("btc_up_down_5m_up_bps_4_instant");
        var fourth = GetBtcStrategyId("btc_up_down_5m_down_bps_4_instant");
        var fifth = GetBtcStrategyId("btc_up_down_5m_up_bps_3_instant");
        var sixth = StrategyIds.CryptoUpDown5mVariants.Single(item => item.Code == "sol_up_down_5m_down_bps_24_instant").Id;
        var other = GetBtcStrategyId("btc_up_down_5m_down_bps_3_instant");
        repository.StrategySettings[first] = repository.StrategySettings[first] with { LiveStakes = false };
        repository.StrategySettings[second] = repository.StrategySettings[second] with { LiveStakes = false };
        repository.StrategySettings[third] = repository.StrategySettings[third] with { LiveStakes = false };
        repository.StrategySettings[fourth] = repository.StrategySettings[fourth] with { LiveStakes = false };
        repository.StrategySettings[fifth] = repository.StrategySettings[fifth] with { LiveStakes = false };
        repository.StrategySettings[sixth] = repository.StrategySettings[sixth] with { LiveStakes = false };
        repository.StrategySettings[other] = repository.StrategySettings[other] with { LiveStakes = true };
        using var output = new StringWriter();

        var exitCode = await StrategyStakeAdminCommand.ExecuteLiveStakesOnlyAsync(
            repository,
            ["btc_up_down_5m_up_bps_2_instant", "btc_up_down_5m_down_bps_2_instant", "btc_up_down_5m_up_bps_4_instant", "btc_up_down_5m_down_bps_4_instant", "btc_up_down_5m_up_bps_3_instant", "sol_up_down_5m_down_bps_24_instant"],
            output,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(repository.StrategySettings[first].LiveStakes);
        Assert.True(repository.StrategySettings[second].LiveStakes);
        Assert.True(repository.StrategySettings[third].LiveStakes);
        Assert.True(repository.StrategySettings[fourth].LiveStakes);
        Assert.True(repository.StrategySettings[fifth].LiveStakes);
        Assert.True(repository.StrategySettings[sixth].LiveStakes);
        Assert.All(
            repository.StrategySettings.Where(item => item.Key != first && item.Key != second && item.Key != third && item.Key != fourth && item.Key != fifth && item.Key != sixth),
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

    private static Guid GetBtcStrategyId(string code)
    {
        return StrategyIds.BtcUpDown5mVariants.Single(item => item.Code == code).Id;
    }
}
