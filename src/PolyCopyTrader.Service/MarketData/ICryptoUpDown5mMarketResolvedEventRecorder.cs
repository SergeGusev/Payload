using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.MarketData;

public interface ICryptoUpDown5mMarketResolvedEventRecorder
{
    Task RecordAsync(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed class NoOpCryptoUpDown5mMarketResolvedEventRecorder : ICryptoUpDown5mMarketResolvedEventRecorder
{
    public static NoOpCryptoUpDown5mMarketResolvedEventRecorder Instance { get; } = new();

    private NoOpCryptoUpDown5mMarketResolvedEventRecorder()
    {
    }

    public Task RecordAsync(
        string component,
        MarketDataUpdate update,
        ActiveMarketAssetSnapshot? activeMarketSnapshot,
        DateTimeOffset receivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
