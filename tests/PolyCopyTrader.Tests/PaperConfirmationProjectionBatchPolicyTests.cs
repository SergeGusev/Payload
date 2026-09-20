using PolyCopyTrader.Service.Analytics;

namespace PolyCopyTrader.Tests;

public sealed class PaperConfirmationProjectionBatchPolicyTests
{
    [Theory]
    [InlineData("57014")]
    [InlineData("55P03")]
    public void OverloadHalvesToOne_AndPausesExactlyThirtySeconds(string state)
    {
        var policy = new PaperConfirmationProjectionBatchPolicy();
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(250, policy.Limit);
        foreach (var expected in new[] { 125, 62, 31, 15, 7, 3, 1, 1 })
        {
            policy.Failed(now, state);
            Assert.Equal(expected, policy.Limit);
            Assert.False(policy.CanRun(now.AddSeconds(30).AddTicks(-1)));
            Assert.True(policy.CanRun(now.AddSeconds(30)));
        }
    }

    [Fact]
    public void RecoveryRequiresEightConsecutiveFastNonemptyCommits_AndCapsAt250()
    {
        var policy = new PaperConfirmationProjectionBatchPolicy();
        var now = DateTimeOffset.UtcNow;
        policy.Failed(now, "57014");
        for (var i = 0; i < 7; i++) policy.Succeeded(1, TimeSpan.FromMilliseconds(499));
        Assert.Equal(125, policy.Limit);
        policy.Succeeded(1, TimeSpan.FromMilliseconds(500));
        for (var i = 0; i < 7; i++) policy.Succeeded(1, TimeSpan.Zero);
        Assert.Equal(125, policy.Limit);
        policy.Succeeded(0, TimeSpan.Zero);
        for (var i = 0; i < 7; i++) policy.Succeeded(1, TimeSpan.Zero);
        policy.Failed(now, "XX000");
        Assert.Equal(125, policy.Limit);
        for (var i = 0; i < 7; i++) policy.Succeeded(1, TimeSpan.Zero);
        Assert.Equal(125, policy.Limit);
        policy.Succeeded(1, TimeSpan.Zero);
        Assert.Equal(250, policy.Limit);
        for (var i = 0; i < 16; i++) policy.Succeeded(1, TimeSpan.Zero);
        Assert.Equal(250, policy.Limit);
    }
}
