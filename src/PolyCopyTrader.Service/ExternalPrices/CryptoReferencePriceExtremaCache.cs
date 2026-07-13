using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface ICryptoReferencePriceExtremaProvider
{
    IReadOnlyList<CryptoReferencePriceExtrema> GetAssetExtrema(string assetSymbol, DateTimeOffset nowUtc);

    CryptoReferencePriceExtrema? GetExtrema(string assetSymbol, int lookbackHours, DateTimeOffset nowUtc);
}

public interface ICryptoReferencePriceExtremaCache : ICryptoReferencePriceExtremaProvider
{
    void Reset(IEnumerable<CryptoReferencePriceTick> ticks, DateTimeOffset nowUtc);

    void Add(CryptoReferencePriceTick tick, DateTimeOffset nowUtc);
}

public sealed class CryptoReferencePriceExtremaCache : ICryptoReferencePriceExtremaCache
{
    private const int MinimumLookbackHours = 1;
    private const int MaximumLookbackHours = 24;
    private readonly object sync = new();
    private readonly IReadOnlyList<WindowDefinition> windows;
    private readonly string[] configuredAssetSymbols;
    private readonly Dictionary<string, AssetState> assets = new(StringComparer.OrdinalIgnoreCase);

    public CryptoReferencePriceExtremaCache(CryptoReferencePriceHistoryOptions options)
    {
        configuredAssetSymbols = options.AssetSymbols
            .Select(NormalizeAssetSymbol)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        windows = BuildWindowDefinitions(options);

        foreach (var assetSymbol in configuredAssetSymbols)
        {
            assets[assetSymbol] = new AssetState(assetSymbol, assetSymbol + "USDT", windows);
        }
    }

    public void Reset(IEnumerable<CryptoReferencePriceTick> ticks, DateTimeOffset nowUtc)
    {
        var orderedTicks = ticks
            .Where(tick => tick.PriceUsd > 0m)
            .OrderBy(tick => tick.SampledAtUtc)
            .ToArray();
        var normalizedNowUtc = nowUtc.ToUniversalTime();

        lock (sync)
        {
            assets.Clear();
            foreach (var assetSymbol in configuredAssetSymbols)
            {
                assets[assetSymbol] = new AssetState(assetSymbol, assetSymbol + "USDT", windows);
            }

            foreach (var tick in orderedTicks)
            {
                AddUnderLock(tick, normalizedNowUtc);
            }

            foreach (var asset in assets.Values)
            {
                asset.Trim(normalizedNowUtc);
            }
        }
    }

    public void Add(CryptoReferencePriceTick tick, DateTimeOffset nowUtc)
    {
        if (tick.PriceUsd <= 0m)
        {
            return;
        }

        var normalizedNowUtc = nowUtc.ToUniversalTime();
        lock (sync)
        {
            AddUnderLock(tick, normalizedNowUtc);
        }
    }

    public IReadOnlyList<CryptoReferencePriceExtrema> GetAssetExtrema(
        string assetSymbol,
        DateTimeOffset nowUtc)
    {
        var normalized = NormalizeAssetSymbol(assetSymbol);
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        lock (sync)
        {
            if (!assets.TryGetValue(normalized, out var asset))
            {
                return [];
            }

            asset.Trim(normalizedNowUtc);
            return asset.GetExtrema(normalizedNowUtc).ToArray();
        }
    }

    public CryptoReferencePriceExtrema? GetExtrema(
        string assetSymbol,
        int lookbackHours,
        DateTimeOffset nowUtc)
    {
        var normalized = NormalizeAssetSymbol(assetSymbol);
        var normalizedNowUtc = nowUtc.ToUniversalTime();
        lock (sync)
        {
            if (!assets.TryGetValue(normalized, out var asset))
            {
                return null;
            }

            asset.Trim(normalizedNowUtc);
            return asset.GetExtrema(normalizedNowUtc)
                .FirstOrDefault(extrema => extrema.LookbackHours == lookbackHours);
        }
    }

    private void AddUnderLock(CryptoReferencePriceTick tick, DateTimeOffset nowUtc)
    {
        var assetSymbol = NormalizeAssetSymbol(tick.AssetSymbol);
        if (!assets.TryGetValue(assetSymbol, out var asset))
        {
            asset = new AssetState(assetSymbol, NormalizeBinanceSymbol(tick.BinanceSymbol, assetSymbol), windows);
            assets[assetSymbol] = asset;
        }

        asset.Add(tick, nowUtc);
    }

    private static IReadOnlyList<WindowDefinition> BuildWindowDefinitions(CryptoReferencePriceHistoryOptions options)
    {
        var targetSamples = Math.Max(1, options.TargetSamplesPerWindow);
        var minStepSeconds = Math.Max(1, options.WriteIntervalSeconds);
        return Enumerable.Range(MinimumLookbackHours, MaximumLookbackHours)
            .Select(lookbackHours =>
            {
                var windowSeconds = checked(lookbackHours * 60 * 60);
                var stepSeconds = Math.Max(minStepSeconds, (int)Math.Ceiling(windowSeconds / (double)targetSamples));
                var expectedBuckets = Math.Max(1, (int)Math.Ceiling(windowSeconds / (double)stepSeconds));
                var boundaryToleranceSeconds = Math.Max(30, checked(minStepSeconds * 3));
                return new WindowDefinition(
                    lookbackHours,
                    windowSeconds,
                    stepSeconds,
                    expectedBuckets,
                    boundaryToleranceSeconds);
            })
            .ToArray();
    }

    private static string NormalizeAssetSymbol(string assetSymbol)
    {
        return assetSymbol.Trim().ToUpperInvariant();
    }

    private static string NormalizeBinanceSymbol(string binanceSymbol, string assetSymbol)
    {
        return string.IsNullOrWhiteSpace(binanceSymbol)
            ? assetSymbol + "USDT"
            : binanceSymbol.Trim().ToUpperInvariant();
    }

    private static long GetBucketUnixSeconds(DateTimeOffset timestampUtc, int stepSeconds)
    {
        var unixSeconds = timestampUtc.ToUniversalTime().ToUnixTimeSeconds();
        return unixSeconds - unixSeconds % stepSeconds;
    }

    private sealed record WindowDefinition(
        int LookbackHours,
        int WindowSeconds,
        int CoverageBucketSeconds,
        int ExpectedCoverageBucketCount,
        int BoundaryToleranceSeconds);

    private sealed class AssetState
    {
        private readonly PeriodState[] periods;

        public AssetState(string assetSymbol, string binanceSymbol, IReadOnlyList<WindowDefinition> windows)
        {
            AssetSymbol = assetSymbol;
            BinanceSymbol = binanceSymbol;
            periods = windows.Select(window => new PeriodState(window)).ToArray();
        }

        public string AssetSymbol { get; }

        public string BinanceSymbol { get; private set; }

        public void Add(CryptoReferencePriceTick tick, DateTimeOffset nowUtc)
        {
            BinanceSymbol = NormalizeBinanceSymbol(tick.BinanceSymbol, AssetSymbol);
            foreach (var period in periods)
            {
                period.Add(tick);
                period.Trim(nowUtc);
            }
        }

        public void Trim(DateTimeOffset nowUtc)
        {
            foreach (var period in periods)
            {
                period.Trim(nowUtc);
            }
        }

        public IEnumerable<CryptoReferencePriceExtrema> GetExtrema(DateTimeOffset snapshotAtUtc)
        {
            return periods.Select(period => period.ToExtrema(AssetSymbol, BinanceSymbol, snapshotAtUtc));
        }
    }

    private sealed class PeriodState(WindowDefinition window)
    {
        private readonly SortedDictionary<long, PriceBucket> buckets = [];
        private int tickCount;

        public void Add(CryptoReferencePriceTick tick)
        {
            var bucketUnixSeconds = GetBucketUnixSeconds(tick.SampledAtUtc, window.CoverageBucketSeconds);
            if (!buckets.TryGetValue(bucketUnixSeconds, out var bucket))
            {
                bucket = new PriceBucket();
                buckets[bucketUnixSeconds] = bucket;
            }

            bucket.Add(tick.PriceUsd, tick.SampledAtUtc.ToUniversalTime());
            tickCount++;
        }

        public void Trim(DateTimeOffset nowUtc)
        {
            var cutoffUtc = nowUtc.ToUniversalTime().AddSeconds(-window.WindowSeconds);
            while (buckets.Count > 0)
            {
                var first = buckets.First();
                var removedCount = first.Value.Trim(cutoffUtc);
                tickCount -= removedCount;
                if (first.Value.Count > 0)
                {
                    break;
                }

                buckets.Remove(first.Key);
            }
        }

        public CryptoReferencePriceExtrema ToExtrema(
            string assetSymbol,
            string binanceSymbol,
            DateTimeOffset updatedAtUtc)
        {
            PriceExtreme? minimum = null;
            PriceExtreme? maximum = null;
            foreach (var bucket in buckets.Values)
            {
                if (bucket.Minimum is { } bucketMinimum &&
                    (minimum is null || bucketMinimum.PriceUsd < minimum.PriceUsd))
                {
                    minimum = bucketMinimum;
                }

                if (bucket.Maximum is { } bucketMaximum &&
                    (maximum is null || bucketMaximum.PriceUsd > maximum.PriceUsd))
                {
                    maximum = bucketMaximum;
                }
            }

            var coverageBucketCount = buckets.Count;
            var firstSampledAtUtc = coverageBucketCount == 0
                ? (DateTimeOffset?)null
                : buckets.First().Value.FirstSampledAtUtc;
            var lastSampledAtUtc = coverageBucketCount == 0
                ? (DateTimeOffset?)null
                : buckets.Last().Value.LastSampledAtUtc;
            var cutoffUtc = updatedAtUtc.ToUniversalTime().AddSeconds(-window.WindowSeconds);
            var boundaryTolerance = TimeSpan.FromSeconds(window.BoundaryToleranceSeconds);
            var coversFullWindow = firstSampledAtUtc is { } firstSample &&
                lastSampledAtUtc is { } lastSample &&
                firstSample <= cutoffUtc + boundaryTolerance &&
                lastSample >= updatedAtUtc - boundaryTolerance;
            return new CryptoReferencePriceExtrema(
                assetSymbol,
                binanceSymbol,
                window.LookbackHours,
                window.WindowSeconds,
                window.CoverageBucketSeconds,
                tickCount,
                coverageBucketCount,
                window.ExpectedCoverageBucketCount,
                coverageBucketCount >= window.ExpectedCoverageBucketCount && coversFullWindow,
                minimum?.PriceUsd,
                minimum?.SampledAtUtc,
                maximum?.PriceUsd,
                maximum?.SampledAtUtc,
                coverageBucketCount == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(buckets.First().Key),
                coverageBucketCount == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(buckets.Last().Key),
                updatedAtUtc);
        }
    }

    private sealed class PriceBucket
    {
        private readonly List<PriceExtreme> points = [];

        public int Count => points.Count;

        public DateTimeOffset? FirstSampledAtUtc => points.Count == 0
            ? null
            : points.Min(point => point.SampledAtUtc);

        public DateTimeOffset? LastSampledAtUtc => points.Count == 0
            ? null
            : points.Max(point => point.SampledAtUtc);

        public PriceExtreme? Minimum { get; private set; }

        public PriceExtreme? Maximum { get; private set; }

        public void Add(decimal priceUsd, DateTimeOffset sampledAtUtc)
        {
            var point = new PriceExtreme(priceUsd, sampledAtUtc);
            points.Add(point);
            if (Minimum is null || priceUsd < Minimum.PriceUsd)
            {
                Minimum = point;
            }

            if (Maximum is null || priceUsd > Maximum.PriceUsd)
            {
                Maximum = point;
            }
        }

        public int Trim(DateTimeOffset cutoffUtc)
        {
            var removedCount = points.RemoveAll(point => point.SampledAtUtc <= cutoffUtc);
            if (removedCount == 0)
            {
                return 0;
            }

            Minimum = points.Count == 0
                ? null
                : points.MinBy(point => point.PriceUsd);
            Maximum = points.Count == 0
                ? null
                : points.MaxBy(point => point.PriceUsd);
            return removedCount;
        }
    }

    private sealed record PriceExtreme(decimal PriceUsd, DateTimeOffset SampledAtUtc);
}
