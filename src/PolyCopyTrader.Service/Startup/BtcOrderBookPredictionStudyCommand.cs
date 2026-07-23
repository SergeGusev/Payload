using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.Analytics;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Service.Startup;

public static class BtcOrderBookPredictionStudyCommand
{
    public const string CommandFlag = "--btc-orderbook-prediction-study";
    public const string GenericCommandFlag = "--crypto-orderbook-prediction-study";
    private const string SbeApiKeyEnvironmentVariable = "POLYCOPYTRADER_BINANCE_SBE_API_KEY";
    private const int ManifestSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<int> ExecuteAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        StudyOptions options;
        try
        {
            options = StudyOptions.FromArgs(args);
        }
        catch (ArgumentException ex)
        {
            await output.WriteLineAsync("Crypto order-book prediction study configuration error: " + ex.Message);
            return 2;
        }

        return options.Mode == StudyMode.Analyze
            ? await AnalyzeExistingRunAsync(options, output, cancellationToken)
            : await CollectAndAnalyzeAsync(options, output, cancellationToken);
    }

    private static async Task<int> CollectAndAnalyzeAsync(
        StudyOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string assetCode = options.Asset.ToCode();
        string assetSymbol = options.Asset.ToDisplaySymbol();
        string runId = assetCode + "-orderbook-prediction-" +
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" +
            Guid.NewGuid().ToString("N")[..8];
        string runDirectory = Path.Combine(options.OutputDirectory!, runId);
        Directory.CreateDirectory(runDirectory);
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        long stopwatchAnchorTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        DateTimeOffset stopwatchAnchorUtc = DateTimeOffset.UtcNow;
        SecretValue? sbeApiKey = null;
        if (options.Source == BinanceOrderBookPredictionSource.Sbe)
        {
            try
            {
                sbeApiKey = ResolveSbeApiKey(options.SbeApiKeyFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await output.WriteLineAsync(assetSymbol + " order-book prediction study failed to resolve the SBE API key id: " + ex.Message);
                return 2;
            }

            if (sbeApiKey is null)
            {
                await output.WriteLineAsync(
                    assetSymbol + " order-book prediction study requires the Binance API key id for source=sbe. " +
                    "Set POLYCOPYTRADER_BINANCE_SBE_API_KEY or pass --binance-sbe-api-key-file. The Ed25519 private key is not read.");
                return 2;
            }
        }

        var manifest = new BtcOrderBookPredictionRunManifest(
            ManifestSchemaVersion,
            runId,
            "collect",
            options.Source.ToString(),
            options.StreamUrl,
            options.GammaBaseUrl,
            ServiceBuildVersion.GetHeartbeatVersion(),
            "in_progress",
            startedAtUtc,
            null,
            runDirectory,
            options.DurationSeconds,
            options.SegmentDurationSeconds,
            options.DecisionLeadSeconds,
            options.FeatureWindowSeconds,
            options.MaximumQuoteAgeMilliseconds,
            options.MinimumQuoteCoverageRatio,
            options.MinimumLabeledMarkets,
            options.MinimumDistinctUtcDays,
            options.MinimumMarketsPerClass,
            options.TrainFraction,
            options.ValidationFraction,
            options.TestFraction,
            System.Diagnostics.Stopwatch.Frequency,
            stopwatchAnchorUtc,
            stopwatchAnchorTicks,
            sbeApiKey?.Source,
            BtcOrderBookPredictionEventStore.IndexFileName,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            null,
            assetSymbol);
        await WriteJsonAtomicAsync(Path.Combine(runDirectory, "run.json"), manifest, CancellationToken.None);

        await output.WriteLineAsync(assetSymbol + " order-book prediction collection started.");
        await output.WriteLineAsync("Run directory: " + runDirectory);
        await output.WriteLineAsync("Asset: " + assetSymbol);
        await output.WriteLineAsync("Source: " + options.Source);
        await output.WriteLineAsync("Duration seconds: " + options.DurationSeconds.ToString(CultureInfo.InvariantCulture));
        await output.WriteLineAsync("Decision lead seconds: " + string.Join(',', options.DecisionLeadSeconds));
        await output.WriteLineAsync("Feature windows seconds: " + string.Join(',', options.FeatureWindowSeconds));

        BtcOrderBookPredictionCollectionSummary summary;
        string? eventsSha256 = null;
        await using (var eventStore = new BtcOrderBookPredictionEventStore(
            runDirectory,
            runId,
            options.Asset,
            TimeSpan.FromSeconds(options.SegmentDurationSeconds)))
        {
            var collectorOptions = new BinanceOrderBookPredictionCollectorOptions(
                options.Source,
                options.Asset,
                options.StreamUrl,
                sbeApiKey?.Value,
                TimeSpan.FromSeconds(options.DurationSeconds),
                options.QueueCapacity,
                options.ReconnectBaseDelayMilliseconds,
                options.ReconnectMaxDelayMilliseconds,
                options.ConnectTimeoutMilliseconds,
                options.NoDataTimeoutMilliseconds);
            var collector = new BinanceOrderBookPredictionCollector(eventStore, runId, collectorOptions);
            summary = await collector.CollectAsync(cancellationToken);
            if (summary.FailureReason is null)
            {
                string finalEventsPath = await eventStore.CompleteAsync(cancellationToken);
                eventsSha256 = BtcOrderBookPredictionEventStore.ComputeSha256(finalEventsPath);
                summary = summary with { EventsPath = finalEventsPath };
            }
        }

        manifest = manifest with
        {
            Status = summary.Status,
            CompletedAtUtc = summary.CompletedAtUtc,
            EventsFile = Path.GetFileName(summary.EventsPath),
            EventsSha256 = eventsSha256,
            BookEvents = summary.BookEvents,
            TradeEvents = summary.TradeEvents,
            ControlEvents = summary.ControlEvents,
            DecodeErrors = summary.DecodeErrors,
            Reconnects = summary.Reconnects,
            QueueHighWaterMark = summary.QueueHighWaterMark,
            FailureReason = summary.FailureReason
        };
        await WriteJsonAtomicAsync(Path.Combine(runDirectory, "run.json"), manifest, CancellationToken.None);
        await output.WriteLineAsync(
            $"Collection finished. Status={summary.Status}; book={summary.BookEvents}; trades={summary.TradeEvents}; controls={summary.ControlEvents}; decodeErrors={summary.DecodeErrors}; reconnects={summary.Reconnects}.");
        if (summary.FailureReason is not null)
        {
            await output.WriteLineAsync("Collection failure: " + summary.FailureReason);
            return 4;
        }

        return await AnalyzeRunAsync(manifest, output, cancellationToken);
    }

    private static async Task<int> AnalyzeExistingRunAsync(
        StudyOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        string runDirectory = options.InputDirectory!;
        string manifestPath = Path.Combine(runDirectory, "run.json");
        if (!File.Exists(manifestPath))
        {
            await output.WriteLineAsync("Crypto order-book prediction run manifest was not found: " + manifestPath);
            return 2;
        }

        BtcOrderBookPredictionRunManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<BtcOrderBookPredictionRunManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            await output.WriteLineAsync("Crypto order-book prediction run manifest is unreadable: " + ex.Message);
            return 2;
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.EventsFile))
        {
            await output.WriteLineAsync("Crypto order-book prediction run has no readable event index.");
            return 2;
        }

        manifest = manifest with { OutputDirectory = runDirectory };

        if (!manifest.IsComplete)
        {
            string snapshotPath = Path.Combine(manifest.OutputDirectory, manifest.EventsFile);
            if (!File.Exists(snapshotPath))
            {
                await output.WriteLineAsync("Crypto order-book prediction run is incomplete and has no finalized segment checkpoint.");
                return 2;
            }

            await output.WriteLineAsync(
                "Warning: analyzing only atomically finalized segments from an incomplete collection snapshot.");
        }

        return await AnalyzeRunAsync(manifest, output, cancellationToken);
    }

    private static async Task<int> AnalyzeRunAsync(
        BtcOrderBookPredictionRunManifest manifest,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.RunId))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest has no run id.");
            return 3;
        }

        if (!TryResolveManifestAsset(manifest, out CryptoOrderBookPredictionAsset asset))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest has an invalid or missing asset.");
            return 3;
        }

        if (!TryParseSource(manifest.Source, out BinanceOrderBookPredictionSource source))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest has an invalid source.");
            return 3;
        }

        try
        {
            BinanceOrderBookPredictionEndpointPolicy.Validate(source, manifest.StreamUrl, asset);
        }
        catch (ArgumentException ex)
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest stream identity is invalid: " + ex.Message);
            return 3;
        }

        if (manifest.SchemaVersion == ManifestSchemaVersion &&
            !manifest.RunId.StartsWith(asset.ToCode() + "-orderbook-prediction-", StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest run id does not match its asset.");
            return 3;
        }

        if (Path.IsPathFullyQualified(manifest.EventsFile!) ||
            !string.Equals(Path.GetFileName(manifest.EventsFile), manifest.EventsFile, StringComparison.Ordinal))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest contains a non-local event path.");
            return 2;
        }

        string eventsPath = Path.Combine(manifest.OutputDirectory, manifest.EventsFile!);
        if (!File.Exists(eventsPath))
        {
            await output.WriteLineAsync("Crypto order-book prediction event file was not found: " + eventsPath);
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(manifest.EventsSha256))
        {
            string actualSha256 = BtcOrderBookPredictionEventStore.ComputeSha256(eventsPath);
            if (!string.Equals(actualSha256, manifest.EventsSha256, StringComparison.OrdinalIgnoreCase))
            {
                await output.WriteLineAsync("Crypto order-book prediction event SHA-256 does not match run.json. Analysis stopped.");
                return 3;
            }
        }
        else if (manifest.SchemaVersion == ManifestSchemaVersion && manifest.IsComplete)
        {
            await output.WriteLineAsync("Completed crypto order-book prediction manifest has no event index SHA-256.");
            return 3;
        }

        BtcOrderBookPredictionEventIndex eventIndex;
        try
        {
            eventIndex = BtcOrderBookPredictionEventStore.ReadIndex(eventsPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
        {
            await output.WriteLineAsync("Crypto order-book prediction event index validation failed: " + ex.Message);
            return 3;
        }

        if (!ValidateEventIndexIdentity(manifest, asset, eventIndex, out string? indexIdentityError))
        {
            await output.WriteLineAsync("Crypto order-book prediction event index identity is invalid: " + indexIdentityError);
            return 3;
        }

        if (manifest.StopwatchFrequency <= 0)
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest has an invalid stopwatch frequency.");
            return 3;
        }

        if (manifest.DecisionLeadSeconds is null || manifest.DecisionLeadSeconds.Count != 1)
        {
            await output.WriteLineAsync("Crypto order-book prediction analysis requires exactly one decision lead.");
            return 3;
        }

        if (manifest.FeatureWindowSeconds is null || manifest.FeatureWindowSeconds.Count == 0)
        {
            await output.WriteLineAsync("Crypto order-book prediction analysis requires at least one feature window.");
            return 3;
        }

        if (!IsOfficialGammaBaseUrl(manifest.GammaBaseUrl))
        {
            await output.WriteLineAsync("Crypto order-book prediction manifest contains a non-official Gamma endpoint.");
            return 3;
        }

        (DateTimeOffset First, DateTimeOffset Last)? bounds;
        IReadOnlyList<BtcOrderBookPredictionFeatureRow> featureRows;
        try
        {
            bounds = BtcOrderBookPredictionStudyAnalyzer.GetReceivedBounds(
                ReadEventsWithMonotonicUtc(eventsPath, manifest));
            if (bounds is null)
            {
                await output.WriteLineAsync("Crypto order-book prediction event file contains no events.");
                return 3;
            }

            await output.WriteLineAsync("Building prospective feature windows without Gamma labels...");
            featureRows = BtcOrderBookPredictionStudyAnalyzer.BuildFeatureRows(
                    ReadEventsWithMonotonicUtc(eventsPath, manifest),
                    bounds.Value.First,
                    bounds.Value.Last,
                    manifest.DecisionLeadSeconds,
                    manifest.FeatureWindowSeconds,
                    new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>(),
                    manifest.MaximumQuoteAgeMilliseconds,
                    manifest.MinimumQuoteCoverageRatio);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            await output.WriteLineAsync("Crypto order-book prediction event validation failed: " + ex.Message);
            return 3;
        }
        DateTimeOffset[] marketStarts = featureRows
            .Select(row => row.MarketStartUtc)
            .Distinct()
            .Order()
            .ToArray();
        IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel> labels =
            await LoadGammaLabelsAsync(asset, manifest.GammaBaseUrl, marketStarts, cancellationToken);
        await WriteJsonAtomicAsync(
            Path.Combine(manifest.OutputDirectory, "gamma-labels.json"),
            labels.Values.OrderBy(label => label.MarketStartUtc).ToArray(),
            cancellationToken);
        featureRows = featureRows.Select(row =>
        {
            labels.TryGetValue(row.MarketStartUtc, out var label);
            return row with
            {
                GammaOutcome = label?.Outcome,
                GammaLabelStatus = label?.Status ?? "not_requested"
            };
        }).ToArray();
        await WriteFeatureRowsAsync(
            Path.Combine(manifest.OutputDirectory, "windows.csv"),
            asset.ToDisplaySymbol(),
            featureRows,
            cancellationToken);

        BtcOrderBookPredictionAnalysisResult analysis = BtcOrderBookPredictionStudyAnalyzer.Analyze(
            featureRows,
            manifest.MinimumLabeledMarkets,
            manifest.MinimumDistinctUtcDays,
            manifest.MinimumMarketsPerClass,
            manifest.TrainFraction,
            manifest.ValidationFraction,
            manifest.TestFraction) with
        {
            AssetSymbol = asset.ToDisplaySymbol()
        };
        await WriteJsonAtomicAsync(Path.Combine(manifest.OutputDirectory, "analysis.json"), analysis, cancellationToken);
        await WritePredictionsAsync(
            Path.Combine(manifest.OutputDirectory, "predictions.csv"),
            asset.ToDisplaySymbol(),
            analysis.TestPredictions,
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(manifest.OutputDirectory, "report.md"),
            BuildMarkdownReport(asset, manifest, analysis),
            new UTF8Encoding(false),
            cancellationToken);

        await output.WriteLineAsync(
            $"Analysis finished. Status={analysis.Status}; featureRows={analysis.FeatureRows}; labeledMarkets={analysis.LabeledMarkets}; validCommonMarkets={analysis.ValidCommonMarkets}; days={analysis.DistinctUtcDays}.");
        await output.WriteLineAsync("Conclusion: " + analysis.Conclusion);
        await output.WriteLineAsync("Report: " + Path.Combine(manifest.OutputDirectory, "report.md"));
        return 0;
    }

    private static async Task<IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>> LoadGammaLabelsAsync(
        CryptoOrderBookPredictionAsset asset,
        string gammaBaseUrl,
        IReadOnlyList<DateTimeOffset> marketStarts,
        CancellationToken cancellationToken)
    {
        var labels = new Dictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel>();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PolyCopyTrader-CryptoOrderBookPredictionStudy/1.0");
        foreach (DateTimeOffset start in marketStarts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string slug = asset.ToMarketSlug(start);
            string requestUri = gammaBaseUrl.TrimEnd('/') +
                "/markets?closed=true&limit=2&slug=" + Uri.EscapeDataString(slug);
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                DateTimeOffset fetchedAtUtc = DateTimeOffset.UtcNow;
                try
                {
                    using HttpResponseMessage response = await httpClient.GetAsync(requestUri, cancellationToken);
                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    response.EnsureSuccessStatusCode();
                    using JsonDocument document = JsonDocument.Parse(json);
                    PolymarketGammaMarket[] exactMarkets = PolymarketJsonParser
                        .ParseGammaActiveMarkets(document.RootElement)
                        .Where(market => string.Equals(market.Slug, slug, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (exactMarkets.Length == 0)
                    {
                        labels[start] = CreateMissingGammaLabel(
                            start,
                            slug,
                            "not_resolved",
                            fetchedAtUtc,
                            requestUri,
                            json,
                            null);
                        break;
                    }

                    if (exactMarkets.Length != 1)
                    {
                        labels[start] = CreateMissingGammaLabel(
                            start,
                            slug,
                            "ambiguous_gamma_response",
                            fetchedAtUtc,
                            requestUri,
                            json,
                            "Exact slug matches=" + exactMarkets.Length.ToString(CultureInfo.InvariantCulture));
                        break;
                    }

                    PolymarketGammaMarket market = exactMarkets[0];
                    DateTimeOffset expectedEnd = start.AddMinutes(5);
                    DateTimeOffset? slugStart = asset.TryParseMarketStartUtc(market.Slug);
                    bool eventSlugMatches = string.IsNullOrWhiteSpace(market.EventSlug) ||
                        string.Equals(market.EventSlug, slug, StringComparison.OrdinalIgnoreCase);
                    bool eventStartMatches = market.EventStartTimeUtc is null ||
                        market.EventStartTimeUtc.Value.ToUniversalTime() == start.ToUniversalTime();
                    bool endMatches = market.EndDateUtc?.ToUniversalTime() == expectedEnd.ToUniversalTime();
                    if (slugStart?.ToUniversalTime() != start.ToUniversalTime() ||
                        !eventSlugMatches || !eventStartMatches || !endMatches)
                    {
                        labels[start] = new BtcOrderBookPredictionGammaLabel(
                            start,
                            start.AddMinutes(5),
                            slug,
                            market.MarketId,
                            market.ConditionId,
                            null,
                            "metadata_mismatch",
                            fetchedAtUtc,
                            requestUri,
                            Sha256Text(market.RawJson),
                            market.RawJson,
                            "Gamma metadata mismatch: slugStart=" +
                            slugStart?.ToString("O", CultureInfo.InvariantCulture) +
                            "; eventStart=" + market.EventStartTimeUtc?.ToString("O", CultureInfo.InvariantCulture) +
                            "; end=" + market.EndDateUtc?.ToString("O", CultureInfo.InvariantCulture) +
                            "; eventSlug=" + market.EventSlug);
                        break;
                    }

                    if (!HasExactUpDownOutcomes(market.RawJson))
                    {
                        labels[start] = new BtcOrderBookPredictionGammaLabel(
                            start,
                            expectedEnd,
                            slug,
                            market.MarketId,
                            market.ConditionId,
                            null,
                            "invalid_outcomes",
                            fetchedAtUtc,
                            requestUri,
                            Sha256Text(market.RawJson),
                            market.RawJson,
                            "Gamma outcomes are not exactly Up and Down.");
                        break;
                    }

                    string? outcome = Btc5mHistoryFillCommand.TryGetWinningOutcome(market.RawJson, market.Closed);
                    labels[start] = new BtcOrderBookPredictionGammaLabel(
                        start,
                        start.AddMinutes(5),
                        slug,
                        market.MarketId,
                        market.ConditionId,
                        outcome,
                        outcome is null ? "ambiguous_or_unresolved" : "gamma_closed",
                        fetchedAtUtc,
                        requestUri,
                        Sha256Text(market.RawJson),
                        market.RawJson,
                        null);
                    break;
                }
                catch (Exception ex) when (
                    ex is HttpRequestException or JsonException ||
                    ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    if (attempt == 3)
                    {
                        labels[start] = CreateMissingGammaLabel(
                            start,
                            slug,
                            "gamma_api_error",
                            fetchedAtUtc,
                            requestUri,
                            null,
                            ex.GetType().Name + ": " + ex.Message);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                }
            }
        }

        return labels;
    }

    internal static bool HasExactUpDownOutcomes(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty("outcomes", out JsonElement outcomesElement))
            {
                return false;
            }

            JsonElement arrayElement = outcomesElement;
            JsonDocument? encodedDocument = null;
            if (outcomesElement.ValueKind == JsonValueKind.String)
            {
                string? encoded = outcomesElement.GetString();
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    return false;
                }

                encodedDocument = JsonDocument.Parse(encoded);
                arrayElement = encodedDocument.RootElement;
            }

            try
            {
                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                string?[] outcomes = arrayElement
                    .EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                    .ToArray();
                return outcomes.Length == 2 &&
                    outcomes.All(value => !string.IsNullOrWhiteSpace(value)) &&
                    outcomes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Up", "Down"]);
            }
            finally
            {
                encodedDocument?.Dispose();
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static BtcOrderBookPredictionGammaLabel CreateMissingGammaLabel(
        DateTimeOffset start,
        string slug,
        string status,
        DateTimeOffset fetchedAtUtc,
        string requestUri,
        string? rawJson,
        string? detail)
    {
        return new BtcOrderBookPredictionGammaLabel(
            start,
            start.AddMinutes(5),
            slug,
            null,
            null,
            null,
            status,
            fetchedAtUtc,
            requestUri,
            rawJson is null ? null : Sha256Text(rawJson),
            rawJson,
            detail);
    }

    private static async Task WriteFeatureRowsAsync(
        string path,
        string assetSymbol,
        IReadOnlyCollection<BtcOrderBookPredictionFeatureRow> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "asset_symbol,market_start_utc,market_end_utc,decision_utc,decision_lead_seconds,feature_window_seconds,feature_window_start_utc,gamma_outcome,gamma_label_status," +
            "binance_proxy_outcome,binance_start_price,binance_end_price,quote_event_count,trade_event_count,quote_coverage_ratio,last_quote_age_ms," +
            "last_bid,last_ask,last_bid_qty,last_ask_qty,last_spread_bps,last_imbalance,time_weighted_imbalance,min_imbalance,max_imbalance,imbalance_slope_per_second," +
            "last_microprice_offset_bps,time_weighted_microprice_offset_bps,observed_l1_ofi,observed_l1_ofi_normalized,signed_trade_qty,total_trade_qty,trade_flow_imbalance," +
            "premarket_trade_return_bps,has_quality_gap,is_valid,invalid_reason");
        foreach (var row in rows.OrderBy(item => item.MarketStartUtc).ThenBy(item => item.DecisionLeadSeconds).ThenBy(item => item.FeatureWindowSeconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',',
                assetSymbol,
                Date(row.MarketStartUtc),
                Date(row.MarketEndUtc),
                Date(row.DecisionUtc),
                row.DecisionLeadSeconds.ToString(CultureInfo.InvariantCulture),
                row.FeatureWindowSeconds.ToString(CultureInfo.InvariantCulture),
                Date(row.FeatureWindowStartUtc),
                Csv(row.GammaOutcome),
                Csv(row.GammaLabelStatus),
                Csv(row.BinanceProxyOutcome),
                Decimal(row.BinanceStartPrice),
                Decimal(row.BinanceEndPrice),
                row.QuoteEventCount.ToString(CultureInfo.InvariantCulture),
                row.TradeEventCount.ToString(CultureInfo.InvariantCulture),
                Decimal(row.QuoteCoverageRatio),
                Decimal(row.LastQuoteAgeMilliseconds),
                Decimal(row.LastBid),
                Decimal(row.LastAsk),
                Decimal(row.LastBidQty),
                Decimal(row.LastAskQty),
                Decimal(row.LastSpreadBps),
                Decimal(row.LastImbalance),
                Decimal(row.TimeWeightedImbalance),
                Decimal(row.MinimumImbalance),
                Decimal(row.MaximumImbalance),
                Decimal(row.ImbalanceSlopePerSecond),
                Decimal(row.LastMicropriceOffsetBps),
                Decimal(row.TimeWeightedMicropriceOffsetBps),
                Decimal(row.ObservedL1Ofi),
                Decimal(row.ObservedL1OfiNormalized),
                Decimal(row.SignedTradeQuantity),
                Decimal(row.TotalTradeQuantity),
                Decimal(row.TradeFlowImbalance),
                Decimal(row.PremarketTradeReturnBps),
                row.HasQualityGap ? "true" : "false",
                row.IsValid ? "true" : "false",
                Csv(row.InvalidReason)));
        }
    }

    private static async Task WritePredictionsAsync(
        string path,
        string assetSymbol,
        IReadOnlyCollection<BtcOrderBookPredictionMarketPrediction> predictions,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        await writer.WriteLineAsync(
            "asset_symbol,market_start_utc,actual_outcome,predicted_outcome,feature_value,majority_baseline,model_correct,baseline_correct");
        foreach (var item in predictions.OrderBy(value => value.MarketStartUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',',
                assetSymbol,
                Date(item.MarketStartUtc),
                item.ActualOutcome,
                item.PredictedOutcome,
                item.FeatureValue.ToString("G29", CultureInfo.InvariantCulture),
                item.BaselinePrediction,
                item.ModelCorrect ? "true" : "false",
                item.BaselineCorrect ? "true" : "false"));
        }
    }

    private static string BuildMarkdownReport(
        CryptoOrderBookPredictionAsset asset,
        BtcOrderBookPredictionRunManifest manifest,
        BtcOrderBookPredictionAnalysisResult analysis)
    {
        string assetSymbol = asset.ToDisplaySymbol();
        var builder = new StringBuilder();
        builder.AppendLine("# " + assetSymbol + " Order-Book Prediction Study");
        builder.AppendLine();
        builder.AppendLine("- Run: `" + manifest.RunId + "`");
        builder.AppendLine("- Asset: `" + assetSymbol + "`");
        builder.AppendLine("- Binance symbol: `" + asset.ToBinanceSymbol() + "`");
        builder.AppendLine("- Source: `" + manifest.Source + "`");
        builder.AppendLine("- Study build: `" + manifest.StudyBuildVersion + "`");
        builder.AppendLine("- Collection status: `" + manifest.Status + "`");
        builder.AppendLine("- Status: `" + analysis.Status + "`");
        builder.AppendLine("- Prediction target: official Gamma `Up`/`Down` for `" + asset.ToMarketSlugPrefix() + "<market_start_unix>`.");
        builder.AppendLine("- Availability rule: only events whose receive stopwatch reconstructs before the configured decision cutoff.");
        builder.AppendLine("- Durable segment seconds: `" + manifest.SegmentDurationSeconds.ToString(CultureInfo.InvariantCulture) + "`");
        builder.AppendLine("- Decision lead seconds: `" + string.Join(',', manifest.DecisionLeadSeconds) + "`");
        builder.AppendLine("- Feature window seconds: `" + string.Join(',', manifest.FeatureWindowSeconds) + "`");
        builder.AppendLine();
        builder.AppendLine("## Data");
        builder.AppendLine();
        builder.AppendLine("- Feature rows: " + analysis.FeatureRows.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- Unique markets: " + analysis.UniqueMarkets.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- Gamma-labeled markets: " + analysis.LabeledMarkets.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- Valid markets common to every configuration: " + analysis.ValidCommonMarkets.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- Distinct UTC days: " + analysis.DistinctUtcDays.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("- Gamma/Binance proxy agreement: " + Percent(analysis.GammaBinanceAgreement));
        if (analysis.ExclusionReasons.Count > 0)
        {
            builder.AppendLine("- Exclusions: " + string.Join("; ", analysis.ExclusionReasons));
        }

        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine(analysis.Conclusion);
        if (analysis.SelectedRule is { } rule && analysis.TestMetrics is { } test && analysis.MajorityBaselineMetrics is { } baseline)
        {
            builder.AppendLine();
            builder.AppendLine("- Selected book feature: `" + rule.FeatureName + "`");
            builder.AppendLine("- Selected lead/window: `" + rule.DecisionLeadSeconds + "s / " + rule.FeatureWindowSeconds + "s`");
            builder.AppendLine("- Threshold: `" + rule.Threshold.ToString("G29", CultureInfo.InvariantCulture) + "`");
            builder.AppendLine("- Test markets: " + test.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Test accuracy: " + Percent(test.Accuracy));
            builder.AppendLine("- Test balanced accuracy: " + Percent(test.BalancedAccuracy));
            builder.AppendLine("- Majority accuracy: " + Percent(baseline.Accuracy));
            builder.AppendLine("- Accuracy lift: " + Percent(analysis.AccuracyLiftVsMajority));
            if (analysis.MomentumBaselineMetrics is { } momentum)
            {
                builder.AppendLine("- Premarket momentum accuracy: " + Percent(momentum.Accuracy));
                builder.AppendLine("- Momentum baseline coverage: " +
                    analysis.MomentumBaselineAvailableMarkets.ToString(CultureInfo.InvariantCulture) + "/" +
                    test.Count.ToString(CultureInfo.InvariantCulture));
                builder.AppendLine("- Accuracy lift vs momentum: " + Percent(analysis.AccuracyLiftVsMomentum));
                builder.AppendLine("- Balanced-accuracy lift vs momentum: " + Percent(analysis.BalancedAccuracyLiftVsMomentum));
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Limitations");
        builder.AppendLine();
        builder.AppendLine("- This is an L1 best-bid/best-ask study, not a reconstructed full-depth order book.");
        builder.AppendLine("- Binance Spot is an external predictor/proxy; the canonical target remains the official resolved Polymarket market outcome.");
        builder.AppendLine("- SBE bestBidAsk may auto-cull obsolete queued updates; JSON bookTicker has no exchange event timestamp.");
        builder.AppendLine("- Predictive lift does not establish executable profit. Polymarket spread, depth, latency, fill probability, fees, and slippage are not part of this first gate.");
        builder.AppendLine("- This first gate has no block-bootstrap confidence interval; a positive status remains preliminary until a longer out-of-sample Paper/shadow study.");
        builder.AppendLine("- Any positive result permits only a separate Paper/shadow validation, never automatic Live trading.");
        return builder.ToString();
    }

    private static IEnumerable<BtcOrderBookPredictionRawEvent> ReadEventsWithMonotonicUtc(
        string eventsPath,
        BtcOrderBookPredictionRunManifest manifest)
    {
        long? previousStopwatchTicks = null;
        long? previousLogicalSequence = null;
        foreach (var item in BtcOrderBookPredictionEventStore.ReadEvents(eventsPath))
        {
            if (item.SchemaVersion != 1)
            {
                throw new InvalidDataException("Persisted raw event has an unsupported schema version.");
            }

            if (!string.Equals(item.RunId, manifest.RunId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Persisted raw event run id does not match run.json.");
            }

            if (previousStopwatchTicks is { } previous && item.ReceivedStopwatchTicks < previous)
            {
                throw new InvalidDataException("Persisted receive stopwatch ticks are not monotonic.");
            }

            if (previousLogicalSequence is { } previousSequence && item.LogicalSequence <= previousSequence)
            {
                throw new InvalidDataException("Persisted logical event sequences are not strictly increasing.");
            }

            previousStopwatchTicks = item.ReceivedStopwatchTicks;
            previousLogicalSequence = item.LogicalSequence;
            decimal elapsedStopwatchTicks = item.ReceivedStopwatchTicks - manifest.StopwatchAnchorTicks;
            decimal elapsedTimeSpanTicks = elapsedStopwatchTicks * TimeSpan.TicksPerSecond /
                manifest.StopwatchFrequency;
            long roundedTimeSpanTicks = decimal.ToInt64(decimal.Floor(elapsedTimeSpanTicks));
            DateTimeOffset reconstructedReceivedUtc = manifest.StopwatchAnchorUtc.AddTicks(roundedTimeSpanTicks);
            yield return item with { ReceivedUtc = reconstructedReceivedUtc };
        }
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + ".partial";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private static SecretValue? ResolveSbeApiKey(string? apiKeyFile)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(SbeApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new SecretValue(fromEnvironment.Trim(), SbeApiKeyEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(apiKeyFile))
        {
            return null;
        }

        string value = File.ReadAllText(apiKeyFile).Trim();
        if (value.StartsWith("-----BEGIN ", StringComparison.Ordinal))
        {
            throw new ArgumentException("The SBE API key file must contain the API key id, not a PEM private/public key.");
        }

        if (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))
        {
            throw new ArgumentException("The SBE API key id file must contain exactly one line.");
        }

        return string.IsNullOrWhiteSpace(value) ? null : new SecretValue(value, "api-key-file");
    }

    private static bool TryResolveManifestAsset(
        BtcOrderBookPredictionRunManifest manifest,
        out CryptoOrderBookPredictionAsset asset)
    {
        asset = default;
        if (manifest.SchemaVersion == 1)
        {
            if (string.IsNullOrWhiteSpace(manifest.AssetSymbol))
            {
                asset = CryptoOrderBookPredictionAsset.Btc;
                return true;
            }

            return CryptoOrderBookPredictionAssetCatalog.TryParse(manifest.AssetSymbol, out asset) &&
                asset == CryptoOrderBookPredictionAsset.Btc;
        }

        return manifest.SchemaVersion == ManifestSchemaVersion &&
            CryptoOrderBookPredictionAssetCatalog.TryParse(manifest.AssetSymbol, out asset);
    }

    private static bool TryParseSource(
        string? value,
        out BinanceOrderBookPredictionSource source)
    {
        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            source = BinanceOrderBookPredictionSource.Json;
            return true;
        }

        if (string.Equals(value, "sbe", StringComparison.OrdinalIgnoreCase))
        {
            source = BinanceOrderBookPredictionSource.Sbe;
            return true;
        }

        source = default;
        return false;
    }

    private static bool ValidateEventIndexIdentity(
        BtcOrderBookPredictionRunManifest manifest,
        CryptoOrderBookPredictionAsset asset,
        BtcOrderBookPredictionEventIndex index,
        out string? error)
    {
        if (manifest.SchemaVersion == 1)
        {
            error = index.SchemaVersion == BtcOrderBookPredictionEventStore.LegacyIndexSchemaVersion
                ? null
                : "Legacy manifest requires a legacy BTC event index.";
            return error is null;
        }

        if (index.SchemaVersion != BtcOrderBookPredictionEventStore.CurrentIndexSchemaVersion)
        {
            error = "Current manifest requires a current event index.";
            return false;
        }

        if (!string.Equals(index.RunId, manifest.RunId, StringComparison.Ordinal))
        {
            error = "Index run id does not match run.json.";
            return false;
        }

        if (!CryptoOrderBookPredictionAssetCatalog.TryParse(index.AssetSymbol, out var indexAsset) ||
            indexAsset != asset)
        {
            error = "Index asset does not match run.json.";
            return false;
        }

        error = null;
        return true;
    }

    private static string Sha256Text(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string Date(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsOfficialGammaBaseUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.IdnHost, "gamma-api.polymarket.com", StringComparison.OrdinalIgnoreCase) &&
            uri.Port == 443 &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            uri.AbsolutePath is "" or "/";
    }

    private static string Decimal(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Percent(decimal? value) =>
        value is null ? "n/a" : (value.Value * 100m).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private sealed record SecretValue(string Value, string Source);

    private enum StudyMode
    {
        Collect,
        Analyze
    }

    private sealed record StudyOptions(
        StudyMode Mode,
        CryptoOrderBookPredictionAsset Asset,
        BinanceOrderBookPredictionSource Source,
        string? OutputDirectory,
        string? InputDirectory,
        string StreamUrl,
        string GammaBaseUrl,
        string? SbeApiKeyFile,
        int DurationSeconds,
        int SegmentDurationSeconds,
        IReadOnlyList<int> DecisionLeadSeconds,
        IReadOnlyList<int> FeatureWindowSeconds,
        int MaximumQuoteAgeMilliseconds,
        decimal MinimumQuoteCoverageRatio,
        int MinimumLabeledMarkets,
        int MinimumDistinctUtcDays,
        int MinimumMarketsPerClass,
        decimal TrainFraction,
        decimal ValidationFraction,
        decimal TestFraction,
        int QueueCapacity,
        int ReconnectBaseDelayMilliseconds,
        int ReconnectMaxDelayMilliseconds,
        int ConnectTimeoutMilliseconds,
        int NoDataTimeoutMilliseconds)
    {
        public static StudyOptions FromArgs(string[] args)
        {
            string modeText = GetOptionValue(args, "--btc-orderbook-study-mode") ?? "collect";
            StudyMode mode = modeText.ToLowerInvariant() switch
            {
                "collect" => StudyMode.Collect,
                "analyze" => StudyMode.Analyze,
                _ => throw new ArgumentException("--btc-orderbook-study-mode must be collect or analyze.")
            };
            CryptoOrderBookPredictionAsset asset = CryptoOrderBookPredictionAssetCatalog.Parse(
                GetOptionValue(args, "--btc-orderbook-study-asset") ?? "btc");
            string sourceText = GetOptionValue(args, "--btc-orderbook-study-source") ?? "json";
            BinanceOrderBookPredictionSource source = sourceText.ToLowerInvariant() switch
            {
                "json" => BinanceOrderBookPredictionSource.Json,
                "sbe" => BinanceOrderBookPredictionSource.Sbe,
                _ => throw new ArgumentException("--btc-orderbook-study-source must be json or sbe.")
            };
            string? outputDirectory = NormalizeAbsoluteDirectory(
                GetOptionValue(args, "--btc-orderbook-study-output-dir"),
                "--btc-orderbook-study-output-dir",
                required: mode == StudyMode.Collect);
            string? inputDirectory = NormalizeAbsoluteDirectory(
                GetOptionValue(args, "--btc-orderbook-study-input-dir"),
                "--btc-orderbook-study-input-dir",
                required: mode == StudyMode.Analyze);
            int durationSeconds = GetIntOption(args, "--btc-orderbook-study-duration-seconds", -1, 10, 604_800);
            if (durationSeconds < 0)
            {
                int durationMinutes = GetIntOption(args, "--btc-orderbook-study-duration-minutes", 60, 1, 10_080);
                durationSeconds = checked(durationMinutes * 60);
            }

            IReadOnlyList<int> decisionLeadSeconds = ParsePositiveIntList(
                GetOptionValue(args, "--btc-orderbook-study-decision-lead-seconds") ?? "30",
                1,
                300,
                "--btc-orderbook-study-decision-lead-seconds");
            if (decisionLeadSeconds.Count != 1)
            {
                throw new ArgumentException(
                    "--btc-orderbook-study-decision-lead-seconds must contain exactly one value per run.");
            }
            IReadOnlyList<int> featureWindows = ParsePositiveIntList(
                GetOptionValue(args, "--btc-orderbook-study-feature-window-seconds") ?? "1,5,15,30,60,300",
                1,
                3_600,
                "--btc-orderbook-study-feature-window-seconds");
            decimal trainFraction = GetDecimalOption(args, "--btc-orderbook-study-train-fraction", 0.60m, 0.10m, 0.80m);
            decimal validationFraction = GetDecimalOption(args, "--btc-orderbook-study-validation-fraction", 0.20m, 0.05m, 0.40m);
            decimal testFraction = 1m - trainFraction - validationFraction;
            if (testFraction < 0.10m)
            {
                throw new ArgumentException("Train and validation fractions must leave at least 0.10 for the untouched test segment.");
            }

            string streamSymbol = asset.ToBinanceStreamSymbol();
            string defaultUrl = source == BinanceOrderBookPredictionSource.Sbe
                ? $"wss://stream-sbe.binance.com:9443/stream?streams={streamSymbol}@trade/{streamSymbol}@bestBidAsk"
                : $"wss://data-stream.binance.vision:443/stream?streams={streamSymbol}@trade/{streamSymbol}@bookTicker";
            Uri streamUri = BinanceOrderBookPredictionEndpointPolicy.Validate(
                source,
                GetOptionValue(args, "--btc-orderbook-study-stream-url") ?? defaultUrl,
                asset);

            string gammaBaseUrl = GetOptionValue(args, "--btc-orderbook-study-gamma-base-url") ?? "https://gamma-api.polymarket.com";
            if (!IsOfficialGammaBaseUrl(gammaBaseUrl))
            {
                throw new ArgumentException("--btc-orderbook-study-gamma-base-url must be the official https://gamma-api.polymarket.com endpoint.");
            }

            return new StudyOptions(
                mode,
                asset,
                source,
                outputDirectory,
                inputDirectory,
                streamUri.ToString(),
                "https://gamma-api.polymarket.com",
                GetOptionValue(args, "--binance-sbe-api-key-file"),
                durationSeconds,
                checked(GetIntOption(args, "--btc-orderbook-study-segment-minutes", 5, 1, 60) * 60),
                decisionLeadSeconds,
                featureWindows,
                GetIntOption(args, "--btc-orderbook-study-maximum-quote-age-ms", 2_000, 1, 60_000),
                GetDecimalOption(args, "--btc-orderbook-study-minimum-quote-coverage", 0.90m, 0m, 1m),
                GetIntOption(args, "--btc-orderbook-study-minimum-labeled-markets", 500, 20, 100_000),
                GetIntOption(args, "--btc-orderbook-study-minimum-utc-days", 3, 1, 365),
                GetIntOption(args, "--btc-orderbook-study-minimum-markets-per-class", 100, 5, 50_000),
                trainFraction,
                validationFraction,
                testFraction,
                GetIntOption(args, "--btc-orderbook-study-queue-capacity", 100_000, 1_000, 1_000_000),
                GetIntOption(args, "--btc-orderbook-study-reconnect-base-ms", 1_000, 100, 60_000),
                GetIntOption(args, "--btc-orderbook-study-reconnect-max-ms", 30_000, 1_000, 300_000),
                GetIntOption(args, "--btc-orderbook-study-connect-timeout-ms", 30_000, 1_000, 120_000),
                GetIntOption(args, "--btc-orderbook-study-no-data-timeout-ms", 60_000, 5_000, 300_000));
        }

        private static IReadOnlyList<int> ParsePositiveIntList(
            string value,
            int minimum,
            int maximum,
            string optionName)
        {
            var result = new SortedSet<int>();
            foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                    parsed < minimum || parsed > maximum)
                {
                    throw new ArgumentException($"{optionName} values must be integers from {minimum} through {maximum}.");
                }

                result.Add(parsed);
            }

            return result.Count == 0
                ? throw new ArgumentException(optionName + " must contain at least one value.")
                : result.ToArray();
        }

        private static string? NormalizeAbsoluteDirectory(string? value, string optionName, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return required ? throw new ArgumentException(optionName + " is required.") : null;
            }

            if (!Path.IsPathFullyQualified(value))
            {
                throw new ArgumentException(optionName + " must be an absolute path.");
            }

            return Path.GetFullPath(value);
        }

        private static int GetIntOption(
            string[] args,
            string name,
            int defaultValue,
            int minimum,
            int maximum)
        {
            string? value = GetOptionValue(args, name);
            if (value is null)
            {
                return defaultValue;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < minimum || parsed > maximum)
            {
                throw new ArgumentException($"{name} must be an integer from {minimum} through {maximum}.");
            }

            return parsed;
        }

        private static decimal GetDecimalOption(
            string[] args,
            string name,
            decimal defaultValue,
            decimal minimum,
            decimal maximum)
        {
            string? value = GetOptionValue(args, name);
            if (value is null)
            {
                return defaultValue;
            }

            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ||
                parsed < minimum || parsed > maximum)
            {
                throw new ArgumentException($"{name} must be a decimal from {minimum} through {maximum}.");
            }

            return parsed;
        }

        private static string? GetOptionValue(string[] args, string name)
        {
            string? genericName = name.StartsWith("--btc-orderbook-study-", StringComparison.Ordinal)
                ? "--crypto-orderbook-study-" + name["--btc-orderbook-study-".Length..]
                : null;
            string? result = null;
            string? matchedName = null;
            for (int index = 0; index < args.Length; index++)
            {
                if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                    (genericName is null ||
                     !string.Equals(args[index], genericName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (matchedName is not null)
                {
                    throw new ArgumentException(
                        $"Specify {name} or {genericName}, once, not both aliases.");
                }

                matchedName = args[index];
                result = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[index + 1]
                    : string.Empty;
            }

            return result;
        }
    }
}
