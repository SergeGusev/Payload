using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.ExternalPrices;
using PolyCopyTrader.Service.Startup;

namespace PolyCopyTrader.Tests;

public sealed class BtcOrderBookPredictionStudyTests
{
    [Fact]
    public async Task EventStore_RoundTripsUtcDecimalsAndControlDetail()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "btc-orderbook-event-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string? eventPath = null;
        try
        {
            var expected = new BtcOrderBookPredictionRawEvent(
                1,
                "run-1",
                2,
                3,
                4,
                BtcOrderBookPredictionEventType.Control,
                new DateTimeOffset(2026, 7, 22, 15, 34, 56, TimeSpan.FromHours(3)).AddTicks(1234),
                null,
                new DateTimeOffset(2026, 7, 22, 12, 34, 57, TimeSpan.Zero).AddTicks(5678),
                987654321,
                null,
                null,
                null,
                12345.678901234567890123456789m,
                0.00000001m,
                null,
                null,
                null,
                null,
                null,
                100,
                7,
                "connection_error",
                "comma, newline\nUnicode: тест");

            await using (var store = new BtcOrderBookPredictionEventStore(
                directory,
                "run-1",
                CryptoOrderBookPredictionAsset.Btc))
            {
                await store.WriteAsync(expected);
                eventPath = await store.CompleteAsync();
            }

            var actual = Assert.Single(BtcOrderBookPredictionEventStore.ReadEvents(eventPath));
            Assert.Equal(expected with
            {
                ExchangeEventUtc = expected.ExchangeEventUtc?.ToUniversalTime(),
                ReceivedUtc = expected.ReceivedUtc.ToUniversalTime()
            }, actual);
            BtcOrderBookPredictionEventIndex index = BtcOrderBookPredictionEventStore.ReadIndex(eventPath);
            Assert.Equal("completed", index.Status);
            Assert.Single(index.Segments);
            Assert.Equal(1, index.TotalEvents);
            Assert.Equal(BtcOrderBookPredictionEventStore.CurrentIndexSchemaVersion, index.SchemaVersion);
            Assert.Equal("run-1", index.RunId);
            Assert.Equal("BTC", index.AssetSymbol);
            Assert.Equal(64, BtcOrderBookPredictionEventStore.ComputeSha256(eventPath).Length);
        }
        finally
        {
            foreach (string file in Directory.Exists(directory)
                         ? Directory.EnumerateFiles(directory).ToArray()
                         : [])
            {
                File.Delete(file);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    [Fact]
    public async Task EventStore_RotatesAndHashesIndependentSegments()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "btc-orderbook-segments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            DateTimeOffset startedUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
            BtcOrderBookPredictionRawEvent first = Book(1, startedUtc, 100m, 1m, 101m, 1m) with
            {
                ReceivedStopwatchTicks = 10_000
            };
            BtcOrderBookPredictionRawEvent second = Book(2, startedUtc.AddSeconds(2), 100m, 2m, 101m, 1m) with
            {
                ReceivedStopwatchTicks = 10_000 + Stopwatch.Frequency * 2
            };
            string indexPath;
            await using (var store = new BtcOrderBookPredictionEventStore(
                directory,
                "test-run",
                CryptoOrderBookPredictionAsset.Btc,
                TimeSpan.FromSeconds(1)))
            {
                await store.WriteAsync(first);
                await store.WriteAsync(second);
                indexPath = await store.CompleteAsync();
            }

            BtcOrderBookPredictionEventIndex index = BtcOrderBookPredictionEventStore.ReadIndex(indexPath);
            Assert.Equal(2, index.Segments.Count);
            Assert.Equal(2, index.TotalEvents);
            Assert.All(index.Segments, segment => Assert.Equal(1, segment.EventCount));
            Assert.Equal(new long[] { 1, 2 },
                BtcOrderBookPredictionEventStore.ReadEvents(indexPath).Select(item => item.LogicalSequence));
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory).ToArray())
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    [Fact]
    public async Task EventStore_ReadsLegacyIndexWithoutIdentityFields()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "btc-orderbook-legacy-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, BtcOrderBookPredictionEventStore.IndexFileName);
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                SchemaVersion = BtcOrderBookPredictionEventStore.LegacyIndexSchemaVersion,
                Status = "completed",
                SegmentDurationSeconds = 300,
                TotalEvents = 0,
                UpdatedAtUtc = DateTimeOffset.Parse("2026-07-22T21:24:39Z"),
                Segments = Array.Empty<object>()
            }));

            BtcOrderBookPredictionEventIndex index = BtcOrderBookPredictionEventStore.ReadIndex(path);

            Assert.Equal(BtcOrderBookPredictionEventStore.LegacyIndexSchemaVersion, index.SchemaVersion);
            Assert.Null(index.RunId);
            Assert.Null(index.AssetSymbol);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory).ToArray())
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    [Fact]
    public void DecodeJsonFrame_DecodesBookAndTradeWithReceiveOrdering()
    {
        long logicalSequence = 0;
        long? previousBookId = 99;
        long? previousTradeId = 199;
        DateTimeOffset receivedUtc = DateTimeOffset.Parse("2026-07-22T12:34:56.1234567Z");
        byte[] bookPayload = Encoding.UTF8.GetBytes(
            "{\"stream\":\"btcusdt@bookTicker\",\"data\":{\"u\":100,\"s\":\"BTCUSDT\",\"b\":\"70000.10\",\"B\":\"1.25\",\"a\":\"70000.20\",\"A\":\"0.75\"}}");

        var book = Assert.Single(BinanceOrderBookPredictionCollector.DecodeJsonFrame(
            bookPayload,
            CryptoOrderBookPredictionAsset.Btc,
            "run",
            7,
            10,
            ref logicalSequence,
            receivedUtc,
            1234,
            ref previousBookId,
            ref previousTradeId));

        Assert.Equal(BtcOrderBookPredictionEventType.Book, book.EventType);
        Assert.Null(book.ExchangeEventUtc);
        Assert.Equal(receivedUtc, book.ReceivedUtc);
        Assert.Equal(100, book.BookUpdateId);
        Assert.Equal(1, book.IdDelta);
        Assert.Equal(70000.10m, book.Bid);
        Assert.Equal(0.75m, book.AskQty);

        byte[] tradePayload = Encoding.UTF8.GetBytes(
            "{\"stream\":\"btcusdt@trade\",\"data\":{\"e\":\"trade\",\"E\":1784723696000,\"T\":1784723695999,\"s\":\"BTCUSDT\",\"t\":201,\"p\":\"70001.25\",\"q\":\"0.01\",\"m\":true}}");
        var trade = Assert.Single(BinanceOrderBookPredictionCollector.DecodeJsonFrame(
            tradePayload,
            CryptoOrderBookPredictionAsset.Btc,
            "run",
            7,
            11,
            ref logicalSequence,
            receivedUtc.AddMilliseconds(1),
            1235,
            ref previousBookId,
            ref previousTradeId));

        Assert.Equal(BtcOrderBookPredictionEventType.Trade, trade.EventType);
        Assert.Equal(201, trade.TradeId);
        Assert.Equal(2, trade.IdDelta);
        Assert.Equal(70001.25m, trade.TradePrice);
        Assert.Equal(0.01m, trade.TradeQty);
        Assert.True(trade.IsBuyerMaker);
        Assert.Equal(2, logicalSequence);
    }

    [Fact]
    public void EndpointPolicy_RejectsCredentialBearingSbeHostOverride()
    {
        Uri validated = BinanceOrderBookPredictionEndpointPolicy.Validate(
            BinanceOrderBookPredictionSource.Sbe,
            "wss://stream-sbe.binance.com:9443/stream?streams=btcusdt@bestBidAsk/btcusdt@trade",
            CryptoOrderBookPredictionAsset.Btc);

        Assert.Equal("stream-sbe.binance.com", validated.Host);
        Assert.Throws<ArgumentException>(() => BinanceOrderBookPredictionEndpointPolicy.Validate(
            BinanceOrderBookPredictionSource.Sbe,
            "wss://attacker.example:9443/stream?streams=btcusdt@trade/btcusdt@bestBidAsk",
            CryptoOrderBookPredictionAsset.Btc));
    }

    [Fact]
    public void DecodeJsonFrame_RejectsWrongInstrument()
    {
        long logicalSequence = 0;
        long? previousBookId = null;
        long? previousTradeId = null;
        byte[] payload = Encoding.UTF8.GetBytes(
            "{\"stream\":\"ethusdt@bookTicker\",\"data\":{\"u\":100,\"s\":\"ETHUSDT\",\"b\":\"3500\",\"B\":\"1\",\"a\":\"3501\",\"A\":\"1\"}}");

        Assert.Throws<System.Text.Json.JsonException>(() =>
            BinanceOrderBookPredictionCollector.DecodeJsonFrame(
                payload,
                CryptoOrderBookPredictionAsset.Btc,
                "run",
                1,
                1,
                ref logicalSequence,
                DateTimeOffset.UtcNow,
                1,
                ref previousBookId,
                ref previousTradeId));
    }

    [Theory]
    [InlineData(CryptoOrderBookPredictionAsset.Btc, "BTC", "BTCUSDT", "btcusdt", "btc-updown-5m-")]
    [InlineData(CryptoOrderBookPredictionAsset.Eth, "ETH", "ETHUSDT", "ethusdt", "eth-updown-5m-")]
    [InlineData(CryptoOrderBookPredictionAsset.Sol, "SOL", "SOLUSDT", "solusdt", "sol-updown-5m-")]
    public void AssetCatalog_MapsExactContracts(
        CryptoOrderBookPredictionAsset asset,
        string displaySymbol,
        string binanceSymbol,
        string streamSymbol,
        string slugPrefix)
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.FromUnixTimeSeconds(1_784_788_200);

        Assert.Equal(displaySymbol, asset.ToDisplaySymbol());
        Assert.Equal(binanceSymbol, asset.ToBinanceSymbol());
        Assert.Equal(streamSymbol, asset.ToBinanceStreamSymbol());
        Assert.Equal(slugPrefix, asset.ToMarketSlugPrefix());
        Assert.Equal(slugPrefix + "1784788200", asset.ToMarketSlug(marketStartUtc));
        Assert.Equal(marketStartUtc, asset.TryParseMarketStartUtc(slugPrefix + "1784788200"));
        Assert.Null(asset.TryParseMarketStartUtc(slugPrefix + "9223372036854775800"));
    }

    [Theory]
    [InlineData("{\"outcomes\":[\"Up\",\"Down\"]}", true)]
    [InlineData("{\"outcomes\":\"[\\\"Down\\\",\\\"Up\\\"]\"}", true)]
    [InlineData("{\"outcomes\":[\"Up\",\"Other\"]}", false)]
    [InlineData("{\"outcomes\":[\"Up\",\"Down\",\"Other\"]}", false)]
    [InlineData("{\"outcomes\":[\"Up\",\"Up\"]}", false)]
    [InlineData("{\"outcomes\":null}", false)]
    public void GammaOutcomeContract_RequiresExactlyUpAndDown(string rawJson, bool expected)
    {
        Assert.Equal(expected, BtcOrderBookPredictionStudyCommand.HasExactUpDownOutcomes(rawJson));
    }

    [Theory]
    [InlineData(CryptoOrderBookPredictionAsset.Btc, BinanceOrderBookPredictionSource.Json,
        "wss://data-stream.binance.vision:443/stream?streams=btcusdt@trade/btcusdt@bookTicker")]
    [InlineData(CryptoOrderBookPredictionAsset.Eth, BinanceOrderBookPredictionSource.Json,
        "wss://data-stream.binance.vision:443/stream?streams=ethusdt@bookTicker/ethusdt@trade")]
    [InlineData(CryptoOrderBookPredictionAsset.Sol, BinanceOrderBookPredictionSource.Json,
        "wss://data-stream.binance.vision:443/stream?streams=solusdt@trade/solusdt@bookTicker")]
    [InlineData(CryptoOrderBookPredictionAsset.Btc, BinanceOrderBookPredictionSource.Sbe,
        "wss://stream-sbe.binance.com:9443/stream?streams=btcusdt@trade/btcusdt@bestBidAsk")]
    [InlineData(CryptoOrderBookPredictionAsset.Eth, BinanceOrderBookPredictionSource.Sbe,
        "wss://stream-sbe.binance.com:9443/stream?streams=ethusdt@bestBidAsk/ethusdt@trade")]
    [InlineData(CryptoOrderBookPredictionAsset.Sol, BinanceOrderBookPredictionSource.Sbe,
        "wss://stream-sbe.binance.com:9443/stream?streams=solusdt@trade/solusdt@bestBidAsk")]
    public void EndpointPolicy_AcceptsOnlySelectedAssetStreams(
        CryptoOrderBookPredictionAsset asset,
        BinanceOrderBookPredictionSource source,
        string url)
    {
        Uri result = BinanceOrderBookPredictionEndpointPolicy.Validate(source, url, asset);

        Assert.Equal("wss", result.Scheme);
    }

    [Fact]
    public void EndpointPolicy_RejectsMixedAssetStreams()
    {
        Assert.Throws<ArgumentException>(() => BinanceOrderBookPredictionEndpointPolicy.Validate(
            BinanceOrderBookPredictionSource.Json,
            "wss://data-stream.binance.vision:443/stream?streams=ethusdt@trade/solusdt@bookTicker",
            CryptoOrderBookPredictionAsset.Eth));
    }

    [Theory]
    [InlineData(CryptoOrderBookPredictionAsset.Eth, "ethusdt", "ETHUSDT")]
    [InlineData(CryptoOrderBookPredictionAsset.Sol, "solusdt", "SOLUSDT")]
    public void DecodeJsonFrame_AcceptsSelectedEthAndSolInstrument(
        CryptoOrderBookPredictionAsset asset,
        string streamSymbol,
        string payloadSymbol)
    {
        long logicalSequence = 0;
        long? previousBookId = null;
        long? previousTradeId = null;
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{{\"stream\":\"{streamSymbol}@bookTicker\",\"data\":{{\"u\":100,\"s\":\"{payloadSymbol}\",\"b\":\"100\",\"B\":\"1\",\"a\":\"101\",\"A\":\"2\"}}}}");

        BtcOrderBookPredictionRawEvent decoded = Assert.Single(
            BinanceOrderBookPredictionCollector.DecodeJsonFrame(
                payload,
                asset,
                "run",
                1,
                1,
                ref logicalSequence,
                DateTimeOffset.UtcNow,
                1,
                ref previousBookId,
                ref previousTradeId));

        Assert.Equal(BtcOrderBookPredictionEventType.Book, decoded.EventType);
        Assert.Equal(100m, decoded.Bid);
        Assert.Equal(2m, decoded.AskQty);
    }

    [Theory]
    [InlineData(CryptoOrderBookPredictionAsset.Eth, "ETHUSDT")]
    [InlineData(CryptoOrderBookPredictionAsset.Sol, "SOLUSDT")]
    public void FlattenSbeEvent_AcceptsSelectedEthAndSolInstrument(
        CryptoOrderBookPredictionAsset asset,
        string symbol)
    {
        long logicalSequence = 0;
        long? previousBookId = null;
        long? previousTradeId = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var decoded = new BinanceSbeBestBidAskEvent(symbol, 7, 100m, 1m, 101m, 2m, now, now);

        BtcOrderBookPredictionRawEvent item = Assert.Single(
            BinanceOrderBookPredictionCollector.FlattenSbeEvent(
                decoded,
                asset,
                "run",
                1,
                1,
                ref logicalSequence,
                1,
                ref previousBookId,
                ref previousTradeId));

        Assert.Equal(BtcOrderBookPredictionEventType.Book, item.EventType);
        Assert.Equal(7, item.BookUpdateId);
    }

    [Fact]
    public void FlattenSbeEvent_RejectsAssetMismatch()
    {
        long logicalSequence = 0;
        long? previousBookId = null;
        long? previousTradeId = null;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var decoded = new BinanceSbeBestBidAskEvent("SOLUSDT", 7, 100m, 1m, 101m, 2m, now, now);

        Assert.Throws<InvalidDataException>(() =>
            BinanceOrderBookPredictionCollector.FlattenSbeEvent(
                decoded,
                CryptoOrderBookPredictionAsset.Eth,
                "run",
                1,
                1,
                ref logicalSequence,
                1,
                ref previousBookId,
                ref previousTradeId));
    }

    [Fact]
    public async Task EventStore_RejectsRawEventFromAnotherRun()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "crypto-orderbook-run-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var store = new BtcOrderBookPredictionEventStore(
                directory,
                "expected-run",
                CryptoOrderBookPredictionAsset.Eth);
            BtcOrderBookPredictionRawEvent wrongRun = Book(
                1,
                DateTimeOffset.Parse("2026-07-23T06:30:00Z"),
                100m,
                1m,
                101m,
                1m);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteAsync(wrongRun).AsTask());
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory).ToArray())
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    [Fact]
    public async Task Command_GenericAssetAliasRejectsCrossAssetStream()
    {
        using var output = new StringWriter();

        int exitCode = await BtcOrderBookPredictionStudyCommand.ExecuteAsync(
            [
                BtcOrderBookPredictionStudyCommand.GenericCommandFlag,
                "--crypto-orderbook-study-mode", "collect",
                "--crypto-orderbook-study-asset", "eth",
                "--crypto-orderbook-study-output-dir", Path.GetFullPath(Path.GetTempPath()),
                "--crypto-orderbook-study-stream-url",
                "wss://data-stream.binance.vision:443/stream?streams=solusdt@trade/solusdt@bookTicker"
            ],
            output,
            CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Contains("ETHUSDT", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2, null, 3)]
    [InlineData(1, "ETH", 3)]
    [InlineData(1, null, 2)]
    public async Task AnalyzeManifest_AcceptsLegacyOnlyAsBtc(
        int schemaVersion,
        string? assetSymbol,
        int expectedExitCode)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "crypto-orderbook-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            BtcOrderBookPredictionRunManifest manifest = Manifest(
                schemaVersion,
                "btc-orderbook-prediction-legacy",
                assetSymbol,
                CryptoOrderBookPredictionAsset.Btc,
                BtcOrderBookPredictionEventStore.IndexFileName,
                eventsSha256: null);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "run.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }));
            using var output = new StringWriter();

            int exitCode = await BtcOrderBookPredictionStudyCommand.ExecuteAsync(
                [
                    BtcOrderBookPredictionStudyCommand.GenericCommandFlag,
                    "--crypto-orderbook-study-mode", "analyze",
                    "--crypto-orderbook-study-input-dir", directory
                ],
                output,
                CancellationToken.None);

            Assert.Equal(expectedExitCode, exitCode);
            if (expectedExitCode == 3)
            {
                Assert.Contains("invalid or missing asset", output.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains("event file was not found", output.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory).ToArray())
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    [Fact]
    public async Task AnalyzeManifest_RejectsIndexFromAnotherRun()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "crypto-orderbook-index-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string actualRunId = "eth-orderbook-prediction-actual";
            string indexPath;
            await using (var store = new BtcOrderBookPredictionEventStore(
                directory,
                actualRunId,
                CryptoOrderBookPredictionAsset.Eth))
            {
                await store.WriteAsync(Book(
                    1,
                    DateTimeOffset.Parse("2026-07-23T06:30:00Z"),
                    100m,
                    1m,
                    101m,
                    1m) with
                {
                    RunId = actualRunId
                });
                indexPath = await store.CompleteAsync();
            }

            BtcOrderBookPredictionRunManifest manifest = Manifest(
                2,
                "eth-orderbook-prediction-declared",
                "ETH",
                CryptoOrderBookPredictionAsset.Eth,
                Path.GetFileName(indexPath),
                BtcOrderBookPredictionEventStore.ComputeSha256(indexPath));
            await File.WriteAllTextAsync(
                Path.Combine(directory, "run.json"),
                JsonSerializer.Serialize(manifest));
            using var output = new StringWriter();

            int exitCode = await BtcOrderBookPredictionStudyCommand.ExecuteAsync(
                [
                    BtcOrderBookPredictionStudyCommand.GenericCommandFlag,
                    "--crypto-orderbook-study-mode", "analyze",
                    "--crypto-orderbook-study-input-dir", directory
                ],
                output,
                CancellationToken.None);

            Assert.Equal(3, exitCode);
            Assert.Contains("Index run id does not match", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory).ToArray())
            {
                File.Delete(file);
            }

            Directory.Delete(directory, recursive: false);
        }
    }

    [Fact]
    public void BuildFeatureRows_ExcludesEventReceivedAtDecisionCutoff()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(-1), 100m, 1m, 102m, 1m),
            Book(2, windowStartUtc.AddSeconds(5), 100m, 3m, 102m, 1m),
            Book(3, decisionUtc, 100m, 1m, 102m, 99m),
            Book(4, decisionUtc.AddSeconds(1), 100m, 1m, 102m, 99m)
        };
        var labels = new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>
        {
            [marketStartUtc] = Label(marketStartUtc, "Up")
        };

        var row = Assert.Single(BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
            events,
            events[0].ReceivedUtc,
            events[^1].ReceivedUtc,
            [30],
            [10],
            labels,
            maximumQuoteAgeMilliseconds: 6_000,
            minimumQuoteCoverageRatio: 1m));

        Assert.True(row.IsValid, row.InvalidReason);
        Assert.Equal(1, row.QuoteEventCount);
        Assert.Equal(0.5m, row.LastImbalance);
        Assert.Equal(0.25m, row.TimeWeightedImbalance);
        Assert.Equal(0.1m, row.ImbalanceSlopePerSecond);
        Assert.Equal(1m, row.QuoteCoverageRatio);
        Assert.Equal(5_000m, row.LastQuoteAgeMilliseconds);
        Assert.Equal("Up", row.GammaOutcome);
    }

    [Fact]
    public void BuildFeatureRows_InvalidatesWindowContainingDisconnect()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(-1), 100m, 1m, 102m, 1m),
            Book(2, windowStartUtc.AddSeconds(5), 100m, 3m, 102m, 1m),
            Control(3, windowStartUtc.AddSeconds(7), "disconnected"),
            Book(4, decisionUtc, 100m, 3m, 102m, 1m)
        };

        var row = Assert.Single(BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
            events,
            events[0].ReceivedUtc,
            events[^1].ReceivedUtc,
            [30],
            [10],
            new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
            maximumQuoteAgeMilliseconds: 6_000,
            minimumQuoteCoverageRatio: 1m));

        Assert.False(row.IsValid);
        Assert.True(row.HasQualityGap);
        Assert.Equal("quality_gap", row.InvalidReason);
    }

    [Fact]
    public void BuildFeatureRows_InvalidatesWindowOverlappingActiveReconnectGap()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(-3), 100m, 1m, 102m, 1m),
            Control(2, windowStartUtc.AddSeconds(-2), "connection_error"),
            Control(3, windowStartUtc.AddSeconds(5), "connected"),
            Book(4, windowStartUtc.AddSeconds(6), 100m, 3m, 102m, 1m),
            Book(5, decisionUtc, 100m, 3m, 102m, 1m)
        };

        var row = Assert.Single(BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
            events,
            events[0].ReceivedUtc,
            events[^1].ReceivedUtc,
            [30],
            [10],
            new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
            maximumQuoteAgeMilliseconds: 6_000,
            minimumQuoteCoverageRatio: 0m));

        Assert.False(row.IsValid);
        Assert.True(row.HasQualityGap);
        Assert.Equal("quality_gap", row.InvalidReason);
    }

    [Fact]
    public void BuildFeatureRows_DoesNotCountStaleQuoteAsWindowCoverage()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(-10), 100m, 1m, 102m, 1m),
            Book(2, decisionUtc.AddSeconds(-1), 100m, 3m, 102m, 1m),
            Book(3, decisionUtc, 100m, 3m, 102m, 1m)
        };

        var row = Assert.Single(BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
            events,
            events[0].ReceivedUtc,
            events[^1].ReceivedUtc,
            [30],
            [10],
            new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
            maximumQuoteAgeMilliseconds: 2_000,
            minimumQuoteCoverageRatio: 0.90m));

        Assert.False(row.IsValid);
        Assert.Equal(0.1m, row.QuoteCoverageRatio);
        Assert.Equal("insufficient_quote_coverage", row.InvalidReason);
    }

    [Fact]
    public void BuildFeatureRows_RejectsMultipleDecisionLeads()
    {
        DateTimeOffset firstUtc = DateTimeOffset.Parse("2026-07-22T11:59:00Z");

        Assert.Throws<ArgumentException>(() =>
            BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
                Array.Empty<BtcOrderBookPredictionRawEvent>(),
                firstUtc,
                firstUtc.AddMinutes(1),
                [30, 60],
                [10],
                new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
                maximumQuoteAgeMilliseconds: 2_000,
                minimumQuoteCoverageRatio: 0.90m));
    }

    [Fact]
    public void BuildFeatureRows_RejectsOutOfOrderReceiveTime()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(5), 100m, 1m, 102m, 1m),
            Book(2, windowStartUtc.AddSeconds(4), 100m, 2m, 102m, 1m)
        };

        Assert.Throws<InvalidDataException>(() =>
            BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
                events,
                windowStartUtc.AddSeconds(-1),
                decisionUtc.AddSeconds(1),
                [30],
                [10],
                new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
                maximumQuoteAgeMilliseconds: 10_000,
                minimumQuoteCoverageRatio: 0m));
    }

    [Fact]
    public void BuildFeatureRows_InvalidQuoteDoesNotContributeToObservedOfi()
    {
        DateTimeOffset marketStartUtc = DateTimeOffset.Parse("2026-07-22T12:00:00Z");
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-10);
        var events = new[]
        {
            Book(1, windowStartUtc.AddSeconds(-1), 100m, 1m, 102m, 1m),
            Book(2, windowStartUtc.AddSeconds(2), 103m, 100m, 102m, 1m),
            Book(3, windowStartUtc.AddSeconds(4), 100m, 1m, 102m, 1m),
            Book(4, decisionUtc, 100m, 1m, 102m, 1m)
        };

        BtcOrderBookPredictionFeatureRow row = Assert.Single(
            BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
                events,
                events[0].ReceivedUtc,
                events[^1].ReceivedUtc,
                [30],
                [10],
                new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
                maximumQuoteAgeMilliseconds: 10_000,
                minimumQuoteCoverageRatio: 0m));

        Assert.True(row.IsValid, row.InvalidReason);
        Assert.Equal(0m, row.ObservedL1Ofi);
        Assert.Null(row.ObservedL1OfiNormalized);
    }

    [Fact]
    public void Analyze_ExcludesOutcomeWithoutOfficialClosedGammaStatus()
    {
        DateTimeOffset firstMarketUtc = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        var rows = Enumerable.Range(0, 20)
            .Select(index => FeatureRow(
                firstMarketUtc.AddMinutes(index * 5),
                index % 2 == 0 ? "Up" : "Down",
                index % 2 == 0 ? 0.8m : -0.8m,
                gammaStatus: "reference_start_end"))
            .ToArray();

        BtcOrderBookPredictionAnalysisResult result = BtcOrderBookPredictionStudyAnalyzer.Analyze(
            rows,
            minimumLabeledMarkets: 20,
            minimumDistinctUtcDays: 1,
            minimumMarketsPerClass: 5,
            trainFraction: 0.60m,
            validationFraction: 0.20m,
            testFraction: 0.20m);

        Assert.Equal("InsufficientData", result.Status);
        Assert.Equal(0, result.ValidCommonMarkets);
        Assert.Contains("label_reference_start_end=20", result.ExclusionReasons);
    }

    [Fact]
    public void Analyze_RejectsMarketWithConflictingGammaOutcomes()
    {
        DateTimeOffset firstMarketUtc = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        var rows = Enumerable.Range(0, 20)
            .SelectMany(index => new[]
            {
                FeatureRow(
                    firstMarketUtc.AddMinutes(index * 5),
                    index % 2 == 0 ? "Up" : "Down",
                    index % 2 == 0 ? 0.8m : -0.8m,
                    featureWindowSeconds: 30),
                FeatureRow(
                    firstMarketUtc.AddMinutes(index * 5),
                    index == 0 ? "Down" : index % 2 == 0 ? "Up" : "Down",
                    index % 2 == 0 ? 0.8m : -0.8m,
                    featureWindowSeconds: 60)
            })
            .ToArray();

        BtcOrderBookPredictionAnalysisResult result = BtcOrderBookPredictionStudyAnalyzer.Analyze(
            rows,
            minimumLabeledMarkets: 20,
            minimumDistinctUtcDays: 1,
            minimumMarketsPerClass: 5,
            trainFraction: 0.60m,
            validationFraction: 0.20m,
            testFraction: 0.20m);

        Assert.Equal("InsufficientData", result.Status);
        Assert.Equal(19, result.ValidCommonMarkets);
        Assert.Contains("conflicting_gamma_outcome=1", result.ExclusionReasons);
    }

    [Fact]
    public void Analyze_DoesNotTreatOneTradeAsCompleteMomentumBaseline()
    {
        DateTimeOffset firstMarketUtc = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        var rows = Enumerable.Range(0, 50)
            .Select(index => FeatureRow(
                firstMarketUtc.AddMinutes(index * 5),
                index % 2 == 0 ? "Up" : "Down",
                index % 2 == 0 ? 0.8m : -0.8m,
                tradeEventCount: 1,
                premarketReturnBps: 0m))
            .ToArray();

        BtcOrderBookPredictionAnalysisResult result = BtcOrderBookPredictionStudyAnalyzer.Analyze(
            rows,
            minimumLabeledMarkets: 20,
            minimumDistinctUtcDays: 1,
            minimumMarketsPerClass: 5,
            trainFraction: 0.60m,
            validationFraction: 0.20m,
            testFraction: 0.20m);

        Assert.Equal(0, result.MomentumBaselineAvailableMarkets);
        Assert.Equal("ExploratoryPointEstimateLiftVsMajorityOnly", result.Status);
    }

    [Fact]
    public void Analyze_DoesNotUseTestFeatureValuesToSelectRule()
    {
        DateTimeOffset firstMarketUtc = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        BtcOrderBookPredictionFeatureRow[] original = Enumerable.Range(0, 50)
            .Select(index => FeatureRow(
                firstMarketUtc.AddMinutes(index * 5),
                index % 2 == 0 ? "Up" : "Down",
                index % 2 == 0 ? 0.8m : -0.8m,
                featureWindowSeconds: 300))
            .ToArray();
        BtcOrderBookPredictionFeatureRow[] changedTest = original
            .Select((row, index) => index >= 42
                ? row with
                {
                    LastImbalance = -row.LastImbalance,
                    TimeWeightedImbalance = -row.TimeWeightedImbalance,
                    ImbalanceSlopePerSecond = -row.ImbalanceSlopePerSecond,
                    LastMicropriceOffsetBps = -row.LastMicropriceOffsetBps,
                    TimeWeightedMicropriceOffsetBps = -row.TimeWeightedMicropriceOffsetBps,
                    ObservedL1OfiNormalized = -row.ObservedL1OfiNormalized
                }
                : row)
            .ToArray();

        BtcOrderBookPredictionAnalysisResult first = AnalyzeForTest(original);
        BtcOrderBookPredictionAnalysisResult second = AnalyzeForTest(changedTest);

        Assert.NotNull(first.SelectedRule);
        Assert.Equal(first.SelectedRule, second.SelectedRule);
        Assert.NotEqual(first.TestMetrics, second.TestMetrics);
    }

    [Fact]
    public void Analyze_UsesChronologicalEmbargoedSplit()
    {
        DateTimeOffset firstMarketUtc = DateTimeOffset.Parse("2026-07-20T00:00:00Z");
        var rows = Enumerable.Range(0, 50)
            .Select(index => FeatureRow(
                firstMarketUtc.AddMinutes(index * 5),
                index % 2 == 0 ? "Up" : "Down",
                index % 2 == 0 ? 0.8m : -0.8m,
                featureWindowSeconds: 300))
            .ToArray();

        BtcOrderBookPredictionAnalysisResult result = BtcOrderBookPredictionStudyAnalyzer.Analyze(
            rows,
            minimumLabeledMarkets: 20,
            minimumDistinctUtcDays: 1,
            minimumMarketsPerClass: 5,
            trainFraction: 0.60m,
            validationFraction: 0.20m,
            testFraction: 0.20m);

        BtcOrderBookPredictionSplit split = Assert.IsType<BtcOrderBookPredictionSplit>(result.Split);
        Assert.Equal(2, split.EmbargoMarkets);
        Assert.True(split.TrainMarkets.Max() < split.ValidationMarkets.Min());
        Assert.True(split.ValidationMarkets.Max() < split.TestMarkets.Min());
        Assert.Equal(TimeSpan.FromMinutes(15), split.ValidationMarkets.Min() - split.TrainMarkets.Max());
        Assert.Equal(TimeSpan.FromMinutes(15), split.TestMarkets.Min() - split.ValidationMarkets.Max());
        Assert.True(
            split.ValidationMarkets.Min().AddSeconds(-330) >= split.TrainMarkets.Max().AddMinutes(5),
            "The first validation feature window must start after the final train market has resolved.");
        Assert.True(
            split.TestMarkets.Min().AddSeconds(-330) >= split.ValidationMarkets.Max().AddMinutes(5),
            "The first test feature window must start after the final validation market has resolved.");
        Assert.NotNull(result.SelectedRule);
        Assert.NotNull(result.TestMetrics);
        Assert.Equal("ExploratoryPointEstimateLiftVsMajorityOnly", result.Status);
        Assert.Equal(0, result.MomentumBaselineAvailableMarkets);
    }

    private static BtcOrderBookPredictionAnalysisResult AnalyzeForTest(
        IReadOnlyCollection<BtcOrderBookPredictionFeatureRow> rows)
    {
        return BtcOrderBookPredictionStudyAnalyzer.Analyze(
            rows,
            minimumLabeledMarkets: 20,
            minimumDistinctUtcDays: 1,
            minimumMarketsPerClass: 5,
            trainFraction: 0.60m,
            validationFraction: 0.20m,
            testFraction: 0.20m);
    }

    private static BtcOrderBookPredictionRunManifest Manifest(
        int schemaVersion,
        string runId,
        string? assetSymbol,
        CryptoOrderBookPredictionAsset asset,
        string eventsFile,
        string? eventsSha256)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.Parse("2026-07-23T06:00:00Z");
        return new BtcOrderBookPredictionRunManifest(
            SchemaVersion: schemaVersion,
            RunId: runId,
            CommandMode: "collect",
            Source: BinanceOrderBookPredictionSource.Json.ToString(),
            StreamUrl:
                $"wss://data-stream.binance.vision:443/stream?streams={asset.ToBinanceStreamSymbol()}@trade/{asset.ToBinanceStreamSymbol()}@bookTicker",
            GammaBaseUrl: "https://gamma-api.polymarket.com",
            StudyBuildVersion: "test",
            Status: "completed",
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: startedAtUtc.AddMinutes(1),
            OutputDirectory: "ignored",
            DurationSeconds: 60,
            SegmentDurationSeconds: 60,
            DecisionLeadSeconds: [30],
            FeatureWindowSeconds: [1, 5],
            MaximumQuoteAgeMilliseconds: 2_000,
            MinimumQuoteCoverageRatio: 0.90m,
            MinimumLabeledMarkets: 20,
            MinimumDistinctUtcDays: 1,
            MinimumMarketsPerClass: 5,
            TrainFraction: 0.60m,
            ValidationFraction: 0.20m,
            TestFraction: 0.20m,
            StopwatchFrequency: Stopwatch.Frequency,
            StopwatchAnchorUtc: startedAtUtc,
            StopwatchAnchorTicks: 1,
            ApiKeySource: null,
            EventsFile: eventsFile,
            EventsSha256: eventsSha256,
            BookEvents: 0,
            TradeEvents: 0,
            ControlEvents: 0,
            DecodeErrors: 0,
            Reconnects: 0,
            QueueHighWaterMark: 0,
            FailureReason: null,
            AssetSymbol: assetSymbol);
    }

    private static BtcOrderBookPredictionRawEvent Book(
        long sequence,
        DateTimeOffset receivedUtc,
        decimal bid,
        decimal bidQty,
        decimal ask,
        decimal askQty)
    {
        return new BtcOrderBookPredictionRawEvent(
            1,
            "test-run",
            1,
            sequence,
            sequence,
            BtcOrderBookPredictionEventType.Book,
            receivedUtc.AddMilliseconds(-1),
            null,
            receivedUtc,
            sequence,
            sequence,
            null,
            null,
            bid,
            bidQty,
            ask,
            askQty,
            null,
            null,
            null,
            sequence - 1,
            1,
            "delivered",
            null);
    }

    private static BtcOrderBookPredictionRawEvent Control(
        long sequence,
        DateTimeOffset receivedUtc,
        string status)
    {
        return new BtcOrderBookPredictionRawEvent(
            1,
            "test-run",
            1,
            sequence,
            sequence,
            BtcOrderBookPredictionEventType.Control,
            null,
            null,
            receivedUtc,
            sequence,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            status,
            null);
    }

    private static BtcOrderBookPredictionGammaLabel Label(DateTimeOffset marketStartUtc, string outcome)
    {
        return new BtcOrderBookPredictionGammaLabel(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            "btc-updown-5m-" + marketStartUtc.ToUnixTimeSeconds(),
            "market",
            "condition",
            outcome,
            "gamma_closed",
            marketStartUtc.AddMinutes(10),
            "https://gamma-api.polymarket.com/markets?slug=test",
            "ABC",
            "{}",
            null);
    }

    private static BtcOrderBookPredictionFeatureRow FeatureRow(
        DateTimeOffset marketStartUtc,
        string outcome,
        decimal featureValue,
        int featureWindowSeconds = 30,
        string gammaStatus = "gamma_closed",
        int tradeEventCount = 0,
        decimal? premarketReturnBps = null)
    {
        DateTimeOffset decisionUtc = marketStartUtc.AddSeconds(-30);
        return new BtcOrderBookPredictionFeatureRow(
            marketStartUtc,
            marketStartUtc.AddMinutes(5),
            decisionUtc,
            30,
            featureWindowSeconds,
            decisionUtc.AddSeconds(-featureWindowSeconds),
            outcome,
            gammaStatus,
            null,
            null,
            null,
            10,
            tradeEventCount,
            1m,
            1m,
            100m,
            101m,
            1m,
            1m,
            99.5m,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            featureValue,
            null,
            null,
            null,
            premarketReturnBps,
            false,
            true,
            null);
    }
}
