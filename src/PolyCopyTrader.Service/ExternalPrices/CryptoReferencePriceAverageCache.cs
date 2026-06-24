using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;

namespace PolyCopyTrader.Service.ExternalPrices;

public interface ICryptoReferencePriceAverageProvider
{
    CryptoReferencePriceAveragesSnapshot GetSnapshot();

    IReadOnlyList<CryptoReferencePriceAverage> GetAssetAverages(string assetSymbol);

    CryptoReferencePriceAverage? GetAverage(string assetSymbol, string windowLabel);
}

public interface ICryptoReferencePriceAverageCache : ICryptoReferencePriceAverageProvider
{
    void Reset(IEnumerable<CryptoReferencePriceTick> ticks, DateTimeOffset nowUtc);

    void Add(CryptoReferencePriceTick tick, DateTimeOffset nowUtc);
}

public sealed class CryptoReferencePriceAverageCache : ICryptoReferencePriceAverageCache
{
    private readonly object sync = new();
    private readonly IReadOnlyList<WindowDefinition> windows;
    private readonly string[] configuredAssetSymbols;
    private readonly Dictionary<string, AssetState> assets = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset snapshotAtUtc = DateTimeOffset.UtcNow;

    public CryptoReferencePriceAverageCache(CryptoReferencePriceHistoryOptions options)
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

            snapshotAtUtc = normalizedNowUtc;
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
            snapshotAtUtc = normalizedNowUtc;
        }
    }

    public CryptoReferencePriceAveragesSnapshot GetSnapshot()
    {
        lock (sync)
        {
            var averages = assets.Values
                .OrderBy(asset => asset.AssetSymbol, StringComparer.OrdinalIgnoreCase)
                .SelectMany(asset => asset.GetAverages(snapshotAtUtc))
                .ToArray();
            return new CryptoReferencePriceAveragesSnapshot(snapshotAtUtc, averages);
        }
    }

    public IReadOnlyList<CryptoReferencePriceAverage> GetAssetAverages(string assetSymbol)
    {
        var normalized = NormalizeAssetSymbol(assetSymbol);
        lock (sync)
        {
            return assets.TryGetValue(normalized, out var asset)
                ? asset.GetAverages(snapshotAtUtc).ToArray()
                : [];
        }
    }

    public CryptoReferencePriceAverage? GetAverage(string assetSymbol, string windowLabel)
    {
        var normalized = NormalizeAssetSymbol(assetSymbol);
        lock (sync)
        {
            if (!assets.TryGetValue(normalized, out var asset))
            {
                return null;
            }

            return asset.GetAverages(snapshotAtUtc)
                .FirstOrDefault(average => string.Equals(average.WindowLabel, windowLabel, StringComparison.OrdinalIgnoreCase));
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
        return options.WindowMinutes
            .Distinct()
            .OrderByDescending(minutes => minutes)
            .Select(minutes =>
            {
                var windowSeconds = checked(minutes * 60);
                var stepSeconds = Math.Max(minStepSeconds, (int)Math.Ceiling(windowSeconds / (double)targetSamples));
                var expectedSamples = Math.Max(1, (int)Math.Ceiling(windowSeconds / (double)stepSeconds));
                return new WindowDefinition(
                    FormatWindowLabel(minutes),
                    windowSeconds,
                    stepSeconds,
                    expectedSamples);
            })
            .ToArray();
    }

    private static string FormatWindowLabel(int windowMinutes)
    {
        return windowMinutes % 60 == 0
            ? (windowMinutes / 60).ToString("0") + "h"
            : windowMinutes.ToString("0") + "m";
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
        string Label,
        int WindowSeconds,
        int SampleStepSeconds,
        int ExpectedSampleCount);

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

        public IEnumerable<CryptoReferencePriceAverage> GetAverages(DateTimeOffset snapshotAtUtc)
        {
            return periods.Select(period => period.ToAverage(AssetSymbol, BinanceSymbol, snapshotAtUtc));
        }
    }

    private sealed class PeriodState(WindowDefinition window)
    {
        private readonly SortedDictionary<long, PriceBucket> buckets = [];
        private decimal bucketAverageSum;

        public void Add(CryptoReferencePriceTick tick)
        {
            var bucketUnixSeconds = GetBucketUnixSeconds(tick.SampledAtUtc, window.SampleStepSeconds);
            if (!buckets.TryGetValue(bucketUnixSeconds, out var bucket))
            {
                bucket = new PriceBucket();
                buckets[bucketUnixSeconds] = bucket;
            }
            else
            {
                bucketAverageSum -= bucket.Average;
            }

            bucket.Add(tick.PriceUsd);
            bucketAverageSum += bucket.Average;
        }

        public void Trim(DateTimeOffset nowUtc)
        {
            var cutoffUnixSeconds = nowUtc.ToUniversalTime().ToUnixTimeSeconds() - window.WindowSeconds;
            while (buckets.Count > 0)
            {
                var first = buckets.First();
                if (first.Key > cutoffUnixSeconds)
                {
                    break;
                }

                bucketAverageSum -= first.Value.Average;
                buckets.Remove(first.Key);
            }
        }

        public CryptoReferencePriceAverage ToAverage(
            string assetSymbol,
            string binanceSymbol,
            DateTimeOffset updatedAtUtc)
        {
            var sampleCount = buckets.Count;
            return new CryptoReferencePriceAverage(
                assetSymbol,
                binanceSymbol,
                window.Label,
                window.WindowSeconds,
                window.SampleStepSeconds,
                sampleCount,
                window.ExpectedSampleCount,
                sampleCount >= window.ExpectedSampleCount,
                sampleCount == 0 ? null : bucketAverageSum / sampleCount,
                sampleCount == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(buckets.First().Key),
                sampleCount == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(buckets.Last().Key),
                updatedAtUtc);
        }
    }

    private sealed class PriceBucket
    {
        public decimal Sum { get; private set; }

        public int Count { get; private set; }

        public decimal Average => Count == 0 ? 0m : Sum / Count;

        public void Add(decimal priceUsd)
        {
            Sum += priceUsd;
            Count++;
        }
    }
}
