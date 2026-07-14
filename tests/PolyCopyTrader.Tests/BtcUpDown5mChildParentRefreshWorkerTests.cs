using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class BtcUpDown5mChildParentRefreshWorkerTests
{
    [Theory]
    [InlineData("2026-07-14T10:00:00Z", "2026-07-14T10:01:00Z")]
    [InlineData("2026-07-14T10:00:59Z", "2026-07-14T10:01:00Z")]
    [InlineData("2026-07-14T10:01:00Z", "2026-07-14T10:01:00Z")]
    [InlineData("2026-07-14T10:01:00.001Z", "2026-07-14T10:06:00Z")]
    [InlineData("2026-07-14T10:04:59Z", "2026-07-14T10:06:00Z")]
    [InlineData("2026-07-14T10:05:00Z", "2026-07-14T10:06:00Z")]
    public void GetNextRefreshUtc_SchedulesOneMinuteAfterFiveMinuteBoundary(
        string nowUtcText,
        string expectedUtcText)
    {
        var nowUtc = DateTimeOffset.Parse(nowUtcText);
        var expectedUtc = DateTimeOffset.Parse(expectedUtcText);

        var actualUtc = BtcUpDown5mChildParentRefreshWorker.GetNextRefreshUtc(nowUtc, 60);

        Assert.Equal(expectedUtc, actualUtc);
    }
}
