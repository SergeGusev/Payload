using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class DashboardDataService(
    IAppRepository repository,
    AppConfiguration configuration,
    bool storageConfigured,
    IPolymarketAuthService authService)
{
    public async Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var heartbeats = await repository.GetServiceHeartbeatsAsync(cancellationToken);
        var scannerStatuses = await repository.GetScannerStatusesAsync(cancellationToken);
        var leaderTrades = await repository.GetRecentLeaderTradesAsync(cancellationToken: cancellationToken);
        var signals = await repository.GetRecentSignalsAsync(cancellationToken: cancellationToken);
        var recentPaperOrders = await repository.GetRecentPaperOrdersAsync(cancellationToken: cancellationToken);
        var openPaperOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var paperPositions = await repository.GetPaperPositionsAsync(cancellationToken);
        var dryRunOrders = await repository.GetRecentDryRunOrdersAsync(cancellationToken: cancellationToken);
        var liveOrders = await repository.GetRecentLiveOrdersAsync(cancellationToken: cancellationToken);
        var liveTradingEvents = await repository.GetRecentLiveTradingEventsAsync(cancellationToken: cancellationToken);
        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var riskEvents = await repository.GetRecentRiskEventsAsync(cancellationToken: cancellationToken);
        var commandAudits = await repository.GetRecentServiceCommandAuditsAsync(cancellationToken: cancellationToken);
        var marketDataStatuses = await repository.GetMarketDataStatusesAsync(cancellationToken);
        var orderBookSnapshots = await repository.GetLatestOrderBookSnapshotsAsync(cancellationToken: cancellationToken);
        var marketDataEvents = await repository.GetRecentMarketDataEventsAsync(cancellationToken: cancellationToken);
        var orderBooksByAsset = orderBookSnapshots.ToDictionary(
            item => item.AssetId,
            StringComparer.OrdinalIgnoreCase);
        var reportLimit = configuration.Analytics.DashboardReportLimit;
        var dailyReports = await repository.GetDailyReportsAsync(reportLimit, cancellationToken);
        var traderPerformance = await repository.GetTraderPerformanceReportsAsync(reportLimit, cancellationToken);
        var categoryPerformance = await repository.GetCategoryPerformanceReportsAsync(reportLimit, cancellationToken);
        var executionQuality = await repository.GetExecutionQualityReportsAsync(reportLimit, cancellationToken);
        var rejectionAnalysis = await repository.GetRejectionAnalysisReportsAsync(reportLimit, cancellationToken);
        var authReadiness = await authService.GetReadinessAsync(cancellationToken);

        var overview = BuildOverview(heartbeats, scannerStatuses, openPaperOrders, paperPositions, liveOrders, apiErrors, marketDataStatuses, authReadiness);
        var riskUsage = BuildRiskUsage(openPaperOrders, paperPositions);

        return new DashboardSnapshot(
            overview,
            BuildWatchlist(scannerStatuses, leaderTrades),
            leaderTrades.Select(ToLeaderTradeRow).ToArray(),
            signals.Select(ToSignalRow).ToArray(),
            recentPaperOrders.Select(ToPaperOrderRow).ToArray(),
            paperPositions.Select(position => ToPaperPositionRow(position, orderBooksByAsset)).ToArray(),
            dryRunOrders.Select(ToDryRunOrderRow).ToArray(),
            liveOrders.Select(ToLiveOrderRow).ToArray(),
            liveTradingEvents.Select(ToLiveTradingEventRow).ToArray(),
            orderBookSnapshots.Select(ToMarketDataRow).ToArray(),
            dailyReports.Select(ToDailyReportRow).ToArray(),
            traderPerformance.Select(ToTraderPerformanceRow).ToArray(),
            categoryPerformance.Select(ToCategoryPerformanceRow).ToArray(),
            executionQuality.Select(ToExecutionQualityRow).ToArray(),
            rejectionAnalysis.Select(ToRejectionAnalysisRow).ToArray(),
            riskUsage,
            BuildDiagnostics(overview, scannerStatuses, marketDataStatuses, apiErrors, riskUsage, authReadiness),
            BuildLogs(apiErrors, riskEvents, commandAudits, marketDataEvents, liveTradingEvents));
    }

    private IReadOnlyList<OverviewMetric> BuildOverview(
        IReadOnlyList<ServiceHeartbeat> heartbeats,
        IReadOnlyList<ScannerStatusSnapshot> scannerStatuses,
        IReadOnlyList<PaperOrder> openPaperOrders,
        IReadOnlyList<PaperPosition> paperPositions,
        IReadOnlyList<LiveOrder> liveOrders,
        IReadOnlyList<ApiError> apiErrors,
        IReadOnlyList<MarketDataStatusSnapshot> marketDataStatuses,
        AuthReadinessStatus authReadiness)
    {
        var heartbeat = heartbeats.FirstOrDefault(item => item.ServiceName == "PolyCopyTrader.Service")
            ?? heartbeats.FirstOrDefault();
        var scanner = scannerStatuses.FirstOrDefault();
        var marketData = marketDataStatuses.FirstOrDefault(item => item.Component == "PolymarketMarketWebSocket")
            ?? marketDataStatuses.FirstOrDefault();
        var openExposure = openPaperOrders.Sum(order => order.NotionalUsd);
        var positionValue = paperPositions.Sum(position => position.EstimatedValueUsd);
        var paperPnl = paperPositions.Sum(position => position.UnrealizedPnlUsd);

        return
        [
            new OverviewMetric("Mode", heartbeat?.Mode.ToString() ?? configuration.Bot.Mode.ToString()),
            new OverviewMetric("Service status", heartbeat?.Status ?? "No heartbeat"),
            new OverviewMetric("Last heartbeat UTC", FormatDate(heartbeat?.LastHeartbeatUtc)),
            new OverviewMetric("Current loop", heartbeat?.CurrentLoop ?? "Waiting for service data"),
            new OverviewMetric("Storage configured", storageConfigured ? "Yes" : "No"),
            new OverviewMetric("API status", apiErrors.Count == 0 ? "No recorded errors" : $"{apiErrors.Count} recent errors"),
            new OverviewMetric("Live open orders", liveOrders.Count(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed).ToString()),
            new OverviewMetric("Geoblock status", "Not checked by dashboard"),
            new OverviewMetric("Auth", authReadiness.State),
            new OverviewMetric("Scanner status", scanner?.ScannerStatus ?? "No scanner status"),
            new OverviewMetric("WebSocket status", marketData?.ConnectionState.ToString() ?? "No market data status"),
            new OverviewMetric("Subscribed assets", marketData?.SubscribedAssetsCount.ToString() ?? "0"),
            new OverviewMetric("Last WS message UTC", FormatDate(marketData?.LastMessageUtc)),
            new OverviewMetric("Paper bankroll", FormatUsd(configuration.PaperTrading.InitialBankrollUsd)),
            new OverviewMetric("Open paper exposure", FormatUsd(openExposure)),
            new OverviewMetric("Paper position value", FormatUsd(positionValue)),
            new OverviewMetric("Daily paper PnL", FormatUsd(paperPnl)),
            new OverviewMetric("Open paper orders", openPaperOrders.Count.ToString())
        ];
    }

    private IReadOnlyList<WatchlistRow> BuildWatchlist(
        IReadOnlyList<ScannerStatusSnapshot> scannerStatuses,
        IReadOnlyList<LeaderTrade> leaderTrades)
    {
        var scanner = scannerStatuses.FirstOrDefault();
        if (configuration.Watchlist.Traders.Count == 0)
        {
            return
            [
                new WatchlistRow(
                    "Configured in service",
                    "n/a",
                    false,
                    "n/a",
                    FormatDate(scanner?.LastSuccessfulScanUtc),
                    FormatDate(leaderTrades.FirstOrDefault()?.TimestampUtc),
                    scanner?.TradesFetched ?? 0,
                    scanner?.NewTradesStored ?? 0,
                    scanner?.ScannerStatus ?? "No watchlist config in dashboard",
                    scanner?.LastErrorMessage ?? string.Empty)
            ];
        }

        return configuration.Watchlist.Traders.Select(trader =>
        {
            var lastTrade = leaderTrades.FirstOrDefault(
                trade => string.Equals(trade.TraderWallet, trader.Wallet, StringComparison.OrdinalIgnoreCase));

            return new WatchlistRow(
                trader.Name,
                trader.Wallet,
                trader.Enabled,
                string.Join(", ", trader.AllowedCategories),
                FormatDate(scanner?.LastSuccessfulScanUtc),
                FormatDate(lastTrade?.TimestampUtc),
                scanner?.TradesFetched ?? 0,
                scanner?.NewTradesStored ?? 0,
                scanner?.ScannerStatus ?? "No scanner status",
                scanner?.LastErrorMessage ?? string.Empty);
        }).ToArray();
    }

    private IReadOnlyList<RiskUsageRow> BuildRiskUsage(
        IReadOnlyList<PaperOrder> openPaperOrders,
        IReadOnlyList<PaperPosition> paperPositions)
    {
        var openExposure = openPaperOrders.Sum(order => order.NotionalUsd);
        var positionValue = paperPositions.Sum(position => position.EstimatedValueUsd);
        var total = openExposure + positionValue;
        var bankroll = configuration.PaperTrading.InitialBankrollUsd;
        var risk = configuration.Risk;

        return
        [
            Usage("Max trade", bankroll * risk.MaxTradeBankrollPct / 100m, openPaperOrders.Select(order => order.NotionalUsd).DefaultIfEmpty(0m).Max()),
            Usage("Max total deployed", bankroll * risk.MaxTotalDeployedPct / 100m, total),
            Usage("Max daily loss", bankroll * risk.MaxDailyLossPct / 100m, Math.Max(0m, -paperPositions.Sum(position => position.UnrealizedPnlUsd))),
            Usage("Max open orders", risk.MaxOpenOrders, openPaperOrders.Count)
        ];
    }

    private IReadOnlyList<DiagnosticRow> BuildDiagnostics(
        IReadOnlyList<OverviewMetric> overview,
        IReadOnlyList<ScannerStatusSnapshot> scannerStatuses,
        IReadOnlyList<MarketDataStatusSnapshot> marketDataStatuses,
        IReadOnlyList<ApiError> apiErrors,
        IReadOnlyList<RiskUsageRow> riskUsage,
        AuthReadinessStatus authReadiness)
    {
        var scanner = scannerStatuses.FirstOrDefault();
        var marketData = marketDataStatuses.FirstOrDefault(item => item.Component == "PolymarketMarketWebSocket")
            ?? marketDataStatuses.FirstOrDefault();
        var latestApiErrors = apiErrors.Take(3).Select(error => $"{FormatDate(error.CreatedAtUtc)} {error.Component}.{error.Operation}: {error.Message}");

        var rows = new List<DiagnosticRow>
        {
            new("Config summary", AppOptionsValidator.ToSanitizedSummary(configuration), "Info"),
            new("Storage provider", configuration.Storage.Provider, storageConfigured ? "OK" : "Warning"),
            new("Storage configured", storageConfigured ? "Yes" : "No", storageConfigured ? "OK" : "Warning"),
            new("Storage env var", configuration.Storage.ConnectionStringEnvironmentVariable, "Info"),
            new("Mode", configuration.Bot.Mode.ToString(), configuration.Bot.EnableLiveTrading && configuration.Bot.Mode != BotMode.Live ? "Error" : "OK"),
            new("Live trading enabled", configuration.Bot.EnableLiveTrading ? "Yes" : "No", configuration.Bot.EnableLiveTrading ? "Warning" : "OK"),
            new("Live max order notional", FormatUsd(configuration.LiveTrading.MaxOrderNotionalUsd), "Info"),
            new("Auth", authReadiness.State, AuthDiagnosticStatus(authReadiness)),
            new("Auth details", AuthDetails(authReadiness), AuthDiagnosticStatus(authReadiness)),
            new("Service status", overview.FirstOrDefault(item => item.Name == "Service status")?.Value ?? "No heartbeat", "Info"),
            new("Scanner status", scanner?.ScannerStatus ?? "No scanner status", scanner?.ScannerStatus == "Healthy" ? "OK" : "Warning"),
            new("Scanner last error", scanner?.LastErrorMessage ?? string.Empty, string.IsNullOrWhiteSpace(scanner?.LastErrorMessage) ? "OK" : "Warning"),
            new("WebSocket status", marketData?.ConnectionState.ToString() ?? "No market data status", marketData?.ConnectionState == MarketDataConnectionState.Connected ? "OK" : "Info"),
            new("Watchlist summary", $"{configuration.Watchlist.Traders.Count} configured; {configuration.Watchlist.Traders.Count(trader => trader.Enabled)} enabled", "Info"),
            new("Risk usage", string.Join("; ", riskUsage.Select(row => $"{row.Name}={row.UsedUsd:0.##}/{row.LimitUsd:0.##} {row.Status}")), riskUsage.Any(row => row.Status == "Limit") ? "Warning" : "OK"),
            new("Latest API errors", string.Join(Environment.NewLine, latestApiErrors), apiErrors.Count == 0 ? "OK" : "Warning")
        };

        return rows;
    }

    private static IReadOnlyList<LogRow> BuildLogs(
        IReadOnlyList<ApiError> apiErrors,
        IReadOnlyList<RiskEvent> riskEvents,
        IReadOnlyList<ServiceCommandAudit> commandAudits,
        IReadOnlyList<MarketDataEvent> marketDataEvents,
        IReadOnlyList<LiveTradingEvent> liveTradingEvents)
    {
        return apiErrors.Select(error => new LogRow(
                FormatDate(error.CreatedAtUtc),
                "Error",
                error.Component,
                error.Message,
                error.Operation))
            .Concat(riskEvents.Select(evt => new LogRow(
                FormatDate(evt.CreatedAtUtc),
                "Risk",
                evt.ReasonCode,
                evt.Details,
                evt.Id.ToString())))
            .Concat(commandAudits.Select(command => new LogRow(
                FormatDate(command.CreatedAtUtc),
                command.Accepted ? "Command" : "CommandRejected",
                command.Command,
                command.Message,
                command.Source)))
            .Concat(liveTradingEvents.Select(liveEvent => new LogRow(
                FormatDate(liveEvent.CreatedAtUtc),
                "Live",
                liveEvent.Action,
                liveEvent.Details,
                liveEvent.Status)))
            .Concat(marketDataEvents.Select(evt => new LogRow(
                FormatDate(evt.ReceivedAtUtc),
                "MarketData",
                evt.EventType.ToString(),
                evt.Message,
                evt.AssetId ?? evt.ConditionId ?? evt.Id.ToString())))
            .OrderByDescending(row => row.TimestampUtc)
            .ToArray();
    }

    private static LeaderTradeRow ToLeaderTradeRow(LeaderTrade trade)
    {
        return new LeaderTradeRow(
            FormatDate(trade.TimestampUtc),
            string.IsNullOrWhiteSpace(trade.TraderName) ? trade.TraderWallet : trade.TraderName,
            trade.MarketTitle,
            trade.Outcome,
            trade.Side.ToString(),
            trade.Price,
            trade.Size,
            trade.CashValueUsd,
            "n/a",
            trade.TransactionHash ?? string.Empty);
    }

    private static SignalRow ToSignalRow(SignalSummary signal)
    {
        return new SignalRow(
            FormatDate(signal.CreatedAtUtc),
            signal.TraderWallet,
            signal.ConditionId,
            signal.Outcome,
            signal.Score,
            signal.Accepted,
            signal.DecisionCode,
            string.Join(", ", signal.ReasonCodes),
            signal.LeaderPrice,
            signal.BestBid,
            signal.BestAsk,
            signal.SpreadAbs,
            signal.SpreadPct,
            signal.LagSeconds,
            signal.ProposedPaperPrice,
            signal.ProposedSizeShares,
            signal.ProposedNotionalUsd);
    }

    private static PaperOrderRow ToPaperOrderRow(PaperOrder order)
    {
        return new PaperOrderRow(
            order.Status.ToString(),
            order.Side.ToString(),
            order.ConditionId,
            order.Outcome,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            FormatDate(order.CreatedAtUtc),
            FormatDate(order.ExpiresAtUtc),
            FormatDate(order.FilledAtUtc),
            TtlRemaining(order),
            order.SignalId.ToString());
    }

    private static PaperPositionRow ToPaperPositionRow(
        PaperPosition position,
        IReadOnlyDictionary<string, OrderBookSnapshot> orderBooksByAsset)
    {
        orderBooksByAsset.TryGetValue(position.AssetId, out var orderBook);
        return new PaperPositionRow(
            position.ConditionId,
            position.Outcome,
            position.SizeShares,
            position.AveragePrice,
            FormatDecimal(orderBook?.BestBid),
            FormatDecimal(orderBook?.BestAsk),
            position.EstimatedValueUsd,
            position.UnrealizedPnlUsd,
            "n/a",
            "n/a");
    }

    private static DryRunOrderRow ToDryRunOrderRow(DryRunOrder order)
    {
        return new DryRunOrderRow(
            FormatDate(order.CreatedAtUtc),
            order.Status.ToString(),
            order.Side.ToString(),
            order.AssetId,
            order.Outcome,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            order.OrderType,
            order.ValidationSummary,
            order.SignalId.ToString());
    }

    private static LiveOrderRow ToLiveOrderRow(LiveOrder order)
    {
        return new LiveOrderRow(
            FormatDate(order.CreatedAtUtc),
            order.Status.ToString(),
            order.OrderId ?? string.Empty,
            order.Side.ToString(),
            order.AssetId,
            order.Outcome,
            order.Price,
            order.SizeShares,
            order.NotionalUsd,
            order.OrderType,
            FormatDate(order.ExpiresAtUtc),
            order.ResponseStatus,
            order.FilledSize,
            order.RemainingSize,
            order.CancelStatus,
            order.ValidationSummary,
            order.SignalId.ToString());
    }

    private static LiveTradingEventRow ToLiveTradingEventRow(LiveTradingEvent liveEvent)
    {
        return new LiveTradingEventRow(
            FormatDate(liveEvent.CreatedAtUtc),
            liveEvent.Action,
            liveEvent.Status,
            liveEvent.Details);
    }

    private static MarketDataRow ToMarketDataRow(OrderBookSnapshot snapshot)
    {
        return new MarketDataRow(
            snapshot.AssetId,
            snapshot.ConditionId ?? string.Empty,
            FormatDecimal(snapshot.BestBid),
            FormatDecimal(snapshot.BestAsk),
            FormatDecimal(snapshot.SpreadAbs),
            FormatDate(snapshot.SnapshotAtUtc));
    }

    private static DailyReportRow ToDailyReportRow(DailyReport report)
    {
        return new DailyReportRow(
            report.ReportDate.ToString("yyyy-MM-dd"),
            report.SignalsObserved,
            report.SignalsAccepted,
            report.SignalsRejected,
            report.PaperOrdersCreated,
            report.PaperFills,
            report.PaperExpiredOrders,
            report.PaperPnl,
            report.OpenPaperExposure,
            report.TopRejectionReasons,
            report.ApiErrors,
            FormatDate(report.GeneratedAtUtc));
    }

    private static TraderPerformanceRow ToTraderPerformanceRow(TraderPerformanceReport report)
    {
        return new TraderPerformanceRow(
            report.TraderWallet,
            report.Signals,
            report.AcceptanceRatePct,
            report.FillRatePct,
            FormatDecimal(report.AverageLagSeconds),
            FormatDecimal(report.AverageLeaderPrice),
            FormatDecimal(report.AverageProposedPrice),
            FormatDecimal(report.AveragePriceDifference),
            report.PaperPnl,
            report.PaperPnlByCategory,
            report.RejectionReasons);
    }

    private static CategoryPerformanceRow ToCategoryPerformanceRow(CategoryPerformanceReport report)
    {
        return new CategoryPerformanceRow(
            report.Category,
            report.Signals,
            report.Accepted,
            report.Filled,
            report.PaperPnl,
            FormatDecimal(report.AverageSpread),
            FormatDecimal(report.AverageLagSeconds));
    }

    private static ExecutionQualityRow ToExecutionQualityRow(ExecutionQualityReport report)
    {
        return new ExecutionQualityRow(
            FormatDate(report.CreatedAtUtc),
            report.TraderWallet,
            report.AssetId,
            report.LeaderPrice,
            FormatDecimal(report.ProposedPrice),
            FormatDecimal(report.PaperFillPrice),
            FormatDecimal(report.ProposedMinusLeader),
            FormatDecimal(report.FillMinusProposed),
            report.LagSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a",
            FormatDecimal(report.SpreadAtSignal),
            FormatDecimal(report.MidAfter1m),
            FormatDecimal(report.MidAfter5m),
            FormatDecimal(report.MidAfter30m));
    }

    private static RejectionAnalysisRow ToRejectionAnalysisRow(RejectionAnalysisReport report)
    {
        return new RejectionAnalysisRow(
            report.ReasonCode,
            report.Count,
            report.RejectedPct,
            FormatDate(report.LastRejectedAtUtc));
    }

    private static RiskUsageRow Usage(string name, decimal limit, decimal used)
    {
        var usedPct = limit <= 0m ? 0m : used / limit * 100m;
        var status = usedPct >= 100m ? "Limit" : usedPct >= 80m ? "High" : "OK";
        return new RiskUsageRow(name, limit, used, usedPct, status);
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;
    }

    private static string AuthDetails(AuthReadinessStatus status)
    {
        if (status.MissingRequirements.Count > 0)
        {
            return string.Join("; ", status.MissingRequirements);
        }

        return status.State == "Ready"
            ? "Authenticated readiness check passed."
            : "All configured auth material is present; no server-side authenticated check has run.";
    }

    private static string AuthDiagnosticStatus(AuthReadinessStatus status)
    {
        return status.State switch
        {
            "Ready" => "OK",
            "ConfiguredButUntested" => "Warning",
            "Error" => "Error",
            _ => "Info"
        };
    }

    private static string FormatUsd(decimal value)
    {
        return value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
    }

    private static string FormatDecimal(decimal? value)
    {
        return value?.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
    }

    private static string TtlRemaining(PaperOrder order)
    {
        if (order.Status is not (PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled))
        {
            return string.Empty;
        }

        var remaining = order.ExpiresAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "Expired";
        }

        return $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
    }
}
