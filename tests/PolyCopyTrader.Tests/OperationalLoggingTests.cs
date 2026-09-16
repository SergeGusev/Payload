using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.Strategies;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PolyCopyTrader.Tests;

public sealed class OperationalLoggingTests
{
    [Fact]
    public void ServiceLoggerConfiguration_SuppressesHttpClientLifecycleNoiseBelowWarningOnly()
    {
        var sink = new RecordingSerilogSink();
        using var logger = ServiceLoggerConfiguration.CreateBaseConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();
        var httpLogger = logger.ForContext(
            Constants.SourceContextPropertyName,
            "System.Net.Http.HttpClient.Polymarket.LogicalHandler");
        var applicationLogger = logger.ForContext(
            Constants.SourceContextPropertyName,
            "PolyCopyTrader.Service.CustomHttpAudit");

        httpLogger.Debug("HTTP lifecycle debug");
        httpLogger.Information("HTTP lifecycle information");
        httpLogger.Warning("HTTP lifecycle warning");
        applicationLogger.Information("Application information");

        Assert.Collection(
            sink.Events,
            warning =>
            {
                Assert.Equal(LogEventLevel.Warning, warning.Level);
                Assert.Equal("HTTP lifecycle warning", warning.RenderMessage());
            },
            information =>
            {
                Assert.Equal(LogEventLevel.Information, information.Level);
                Assert.Equal("Application information", information.RenderMessage());
            });
    }

    [Fact]
    public void RoutineStrategySkip_LogsCompactInformationAndFullDebugWithoutChangingRun()
    {
        var diagnostics = "{\"reason_code\":\"routine_threshold\",\"note\":\"данные\"}";
        var run = CreateSkippedRun("routine_threshold", diagnostics);
        var original = run with { };
        var variant = StrategyIds.UpDown5mStrategyVariants.First();
        var logger = new RecordingMicrosoftLogger();

        BtcUpDown5mPaperStrategyProcessor.LogSkippedRun(logger, run, variant);

        Assert.Equal(original, run);
        var information = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        var debug = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.Equal(run.Id, information.Properties["RunId"]);
        Assert.Equal(run.StrategyId, information.Properties["StrategyId"]);
        Assert.Equal(run.MarketId, information.Properties["MarketId"]);
        Assert.Equal(run.ConditionId, information.Properties["ConditionId"]);
        Assert.Equal(run.MarketSlug, information.Properties["MarketSlug"]);
        Assert.Equal(run.SkipReason, information.Properties["Reason"]);
        Assert.Equal(diagnostics.Length, information.Properties["DiagnosticsCharacterLength"]);
        Assert.Equal(Encoding.UTF8.GetByteCount(diagnostics), information.Properties["DiagnosticsUtf8ByteLength"]);
        var expectedFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(diagnostics)))
            .ToLowerInvariant();
        Assert.Equal(expectedFingerprint, information.Properties["DiagnosticsSha256"]);
        Assert.Matches("^[0-9a-f]{64}$", Assert.IsType<string>(information.Properties["DiagnosticsSha256"]));
        Assert.False(information.Properties.ContainsKey("Diagnostics"));
        Assert.DoesNotContain(diagnostics, information.Message, StringComparison.Ordinal);
        Assert.Equal(diagnostics, debug.Properties["Diagnostics"]);
    }

    [Fact]
    public void MakerStrategySkip_RetainsFullInformationDiagnosticsAndMandatoryLabel()
    {
        var diagnostics =
            "{\"reason_code\":\"maker_gtd_post_only_attempts_exhausted\",\"paper_model_label\":\"" +
            StrategyIds.OptimisticTouchNoDepthPaperLabel +
            "\"}";
        var run = CreateSkippedRun("maker_gtd_post_only_attempts_exhausted", diagnostics);
        var variant = StrategyIds.UpDown5mStrategyVariants.Single(item =>
            item.Code == "eth_up_down_5m_reference_average_bps_9_maker_gtd_premarket");
        var logger = new RecordingMicrosoftLogger();

        BtcUpDown5mPaperStrategyProcessor.LogSkippedRun(logger, run, variant);

        var information = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Debug);
        Assert.Equal(diagnostics, information.Properties["Diagnostics"]);
        Assert.Equal(
            StrategyIds.OptimisticTouchNoDepthPaperLabel,
            information.Properties["PaperModelLabel"]);
    }

    [Theory]
    [InlineData("paper_live_shadow_shape_mismatch")]
    [InlineData("order_evidence_unavailable")]
    public void ExplicitActionableSkipDiagnostics_RetainFullInformationPayload(string diagnosticCode)
    {
        var diagnostics = $"{{\"diagnostic_code\":\"{diagnosticCode}\"}}";
        var run = CreateSkippedRun("entry_rejected", diagnostics);
        var variant = StrategyIds.UpDown5mStrategyVariants.First();
        var logger = new RecordingMicrosoftLogger();

        BtcUpDown5mPaperStrategyProcessor.LogSkippedRun(logger, run, variant);

        var information = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.Equal(diagnostics, information.Properties["Diagnostics"]);
    }

    [Fact]
    public void SlowWarningAggregator_LogsFirstAggregatesRepeatsRecoversOnceAndDoesNotSuppressErrors()
    {
        var now = new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero);
        var logger = new RecordingMicrosoftLogger();
        var aggregator = new SlowProcessingWarningAggregator(
            logger,
            TimeSpan.FromSeconds(30),
            "Queued market-data side effect",
            () => now);

        aggregator.RecordSlow(Observation(queueDelayMs: 1_100, processingMs: 400, pendingDepth: 3));
        Assert.Single(logger.Entries);
        var first = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.StartsWith("Queued market-data side effect was slow.", first.Message, StringComparison.Ordinal);

        now = now.AddSeconds(5);
        aggregator.RecordSlow(Observation(queueDelayMs: 1_500, processingMs: 600, pendingDepth: 5));
        Assert.Single(logger.Entries);

        var exception = new InvalidOperationException("immediate failure");
        logger.LogError(exception, "Queued market-data side effect failed.");
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(exception, error.Exception);

        now = now.AddSeconds(26);
        aggregator.RecordSlow(Observation(queueDelayMs: 1_300, processingMs: 900, pendingDepth: 4));
        var aggregate = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.StartsWith("Repeated Queued market-data side effect", StringComparison.Ordinal));
        Assert.Equal(2, aggregate.Properties["Count"]);
        Assert.Equal(1_500d, aggregate.Properties["MaxQueueDelayMs"]);
        Assert.Equal(900d, aggregate.Properties["MaxProcessingDurationMs"]);
        Assert.Equal(5, aggregate.Properties["MaxPendingDepth"]);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 10, 0, 5, TimeSpan.Zero), aggregate.Properties["FirstUtc"]);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 10, 0, 31, TimeSpan.Zero), aggregate.Properties["LastUtc"]);

        now = now.AddSeconds(1);
        aggregator.RecordNonSlow("MarketDataWebSocket", "PriceChange", "UpdatePositionMarks", "asset-1");
        var recovery = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        Assert.StartsWith(
            "Queued market-data side effect recovered after slow processing.",
            recovery.Message,
            StringComparison.Ordinal);
        Assert.Equal(3, recovery.Properties["Count"]);
        Assert.Equal(1_500d, recovery.Properties["MaxQueueDelayMs"]);
        Assert.Equal(900d, recovery.Properties["MaxProcessingDurationMs"]);
        Assert.Equal(5, recovery.Properties["MaxPendingDepth"]);

        aggregator.RecordNonSlow("MarketDataWebSocket", "PriceChange", "UpdatePositionMarks", "asset-1");
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    private static SlowProcessingWarningObservation Observation(
        double queueDelayMs,
        double processingMs,
        int pendingDepth)
    {
        return new SlowProcessingWarningObservation(
            "MarketDataWebSocket",
            "PriceChange",
            "asset-1",
            "QueueDelay",
            queueDelayMs,
            processingMs,
            pendingDepth,
            "UpdatePositionMarks",
            "IAppRepository.TryUpdatePaperPositionMarks",
            processingMs,
            "UpdatePositionMarks",
            "IAppRepository.TryUpdatePaperPositionMarks",
            processingMs);
    }

    private static StrategyMarketPaperRun CreateSkippedRun(
        string reason,
        string diagnostics)
    {
        var now = new DateTimeOffset(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);
        var variant = StrategyIds.UpDown5mStrategyVariants.First();
        return new StrategyMarketPaperRun(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            StrategyId: variant.Id,
            MarketId: "market-identity",
            ConditionId: "condition-identity",
            MarketSlug: "market-slug",
            MarketTitle: "Market title",
            Category: "Crypto",
            MarketStartUtc: now,
            MarketEndUtc: now.AddMinutes(5),
            DetectedAtUtc: now,
            EntryDueAtUtc: now.AddMinutes(1),
            Status: StrategyMarketPaperRunStatuses.Skipped,
            SelectedAssetId: null,
            SelectedOutcome: null,
            EntryPrice: null,
            StakeUsd: 1m,
            SizeShares: null,
            SignalId: null,
            PaperOrderId: null,
            EnteredAtUtc: null,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: reason,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            SkipDiagnosticsJson: diagnostics);
    }

    private sealed class RecordingSerilogSink : ILogEventSink
    {
        private readonly List<LogEvent> events = [];

        internal IReadOnlyList<LogEvent> Events => events;

        public void Emit(LogEvent logEvent)
        {
            events.Add(logEvent);
        }
    }

    private sealed class RecordingMicrosoftLogger : Microsoft.Extensions.Logging.ILogger
    {
        internal ConcurrentQueue<MicrosoftLogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Enqueue(new MicrosoftLogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                properties));
        }
    }

    private sealed record MicrosoftLogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
