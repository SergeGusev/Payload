using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.ExternalPrices;

public sealed class BtcUsdReferencePriceCache : IBtcUsdReferencePriceCache
{
    private readonly object sync = new();
    private readonly List<BtcUsdReferencePricePoint> samples = [];
    private readonly int windowSize;
    private readonly string source;

    public BtcUsdReferencePriceCache(CoinbaseExchangeOptions options)
        : this(options.WindowSize, "CoinbaseExchange")
    {
    }

    public BtcUsdReferencePriceCache(BinanceBtcUsdReferenceOptions options)
        : this(options.WindowSize, BinanceBtcUsdTradeParser.SourceName)
    {
    }

    public BtcUsdReferencePriceCache(int windowSize, string source)
    {
        this.windowSize = Math.Max(1, windowSize);
        this.source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
    }

    public BtcUsdReferencePriceSnapshot Snapshot
    {
        get
        {
            lock (sync)
            {
                return CreateSnapshot();
            }
        }
    }

    public void Add(BtcUsdReferencePricePoint point)
    {
        if (point.PriceUsd <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "BTC/USD reference price must be greater than zero.");
        }

        lock (sync)
        {
            samples.Add(point);
            TrimToWindow();
        }
    }

    private BtcUsdReferencePriceSnapshot CreateSnapshot()
    {
        var orderedSamples = samples
            .OrderByDescending(sample => sample.FetchedAtUtc)
            .ToArray();
        var latest = orderedSamples.FirstOrDefault();
        var mean = orderedSamples.Length == 0
            ? (decimal?)null
            : orderedSamples.Sum(sample => sample.PriceUsd) / orderedSamples.Length;

        return new BtcUsdReferencePriceSnapshot(
            source,
            windowSize,
            orderedSamples.Length,
            orderedSamples.Length >= windowSize,
            mean,
            latest,
            orderedSamples,
            DateTimeOffset.UtcNow);
    }

    private void TrimToWindow()
    {
        var extra = samples.Count - windowSize;
        if (extra > 0)
        {
            samples.RemoveRange(0, extra);
        }
    }
}
