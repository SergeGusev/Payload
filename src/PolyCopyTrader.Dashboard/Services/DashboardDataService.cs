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
    private IReadOnlyList<StrategyPerformance>? cachedStrategyPerformance;
    private DateTimeOffset cachedStrategyPerformanceAtUtc = DateTimeOffset.MinValue;
    private IReadOnlyList<StrategyRecentPerformance>? cachedStrategyRecentPerformance;
    private DateTimeOffset cachedStrategyRecentPerformanceAtUtc = DateTimeOffset.MinValue;

    public async Task<DashboardSnapshot> LoadAsync(
        ControlStatusResponse? controlStatus = null,
        string? controlStatusError = null,
        CancellationToken cancellationToken = default)
    {
        var heartbeats = await repository.GetServiceHeartbeatsAsync(cancellationToken);
        var scannerStatuses = await repository.GetScannerStatusesAsync(cancellationToken);
        var traderDiscovery = await repository.GetRecentTraderDiscoveryCandidatesAsync(
            configuration.TraderDiscovery.CandidatesPerSide * 2,
            cancellationToken);
        var onChainLeaders = await repository.GetPolymarketOnChainWalletPerformanceAsync(100, cancellationToken);
        var onChainTraders = await repository.GetTraderOnChainStatsAsync(100, cancellationToken);
        var onChainPositions = await repository.GetPolymarketOnChainWalletPositionsAsync(250, cancellationToken);
        var onChainExecutions = await repository.GetRecentPolymarketOnChainWalletExecutionsAsync(250, cancellationToken);
        var onChainTradeDetails = await repository.GetRecentPolymarketOnChainTradeDetailsAsync(500, cancellationToken);
        var onChainParticipantDetails = await repository.GetPolymarketOnChainParticipantDetailsAsync(250, cancellationToken);
        var leaderTrades = await repository.GetRecentLeaderTradesAsync(cancellationToken: cancellationToken);
        var signals = await repository.GetRecentSignalsAsync(cancellationToken: cancellationToken);
        var recentPaperOrders = await repository.GetRecentPaperOrdersAsync(cancellationToken: cancellationToken);
        var openPaperOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var paperPositions = await repository.GetPaperPositionsAsync(cancellationToken);
        var paperCopiedTraderPerformance = await repository.GetPaperCopiedTraderPerformanceAsync(250, cancellationToken);
        var strategyPerformance = await GetStrategyPerformanceAsync(cancellationToken);
        var strategyRecentPerformance = await GetStrategyRecentPerformanceAsync(cancellationToken);
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

        var overview = BuildOverview(
            heartbeats,
            scannerStatuses,
            openPaperOrders,
            paperPositions,
            liveOrders,
            liveTradingEvents,
            apiErrors,
            marketDataStatuses,
            authReadiness);
        var riskUsage = BuildRiskUsage(openPaperOrders, paperPositions);
        var liveReadiness = BuildLiveReadiness(
            authReadiness,
            strategyPerformance,
            dryRunOrders,
            liveOrders,
            liveTradingEvents,
            apiErrors,
            riskEvents,
            marketDataStatuses,
            controlStatus,
            controlStatusError);

        return new DashboardSnapshot(
            overview,
            BuildWatchlist(scannerStatuses, leaderTrades),
            traderDiscovery.Select(ToTraderDiscoveryRow).ToArray(),
            onChainLeaders.Select(ToOnChainLeaderRow).ToArray(),
            onChainTraders.Select(ToOnChainTraderRow).ToArray(),
            onChainPositions.Select(ToOnChainPositionRow).ToArray(),
            onChainExecutions.Select(ToOnChainFillRow).ToArray(),
            onChainTradeDetails.Select(ToOnChainTradeDetailRow).ToArray(),
            onChainParticipantDetails.Select(ToOnChainParticipantDetailRow).ToArray(),
            leaderTrades.Select(ToLeaderTradeRow).ToArray(),
            signals.Select(ToSignalRow).ToArray(),
            recentPaperOrders.Select(ToPaperOrderRow).ToArray(),
            paperPositions.Select(position => ToPaperPositionRow(position, orderBooksByAsset)).ToArray(),
            strategyPerformance.Select(ToStrategyPerformanceRow).ToArray(),
            strategyRecentPerformance.Select(ToStrategyRecentPerformanceRow).ToArray(),
            paperCopiedTraderPerformance.Select(ToPaperCopiedTraderPerformanceRow).ToArray(),
            dryRunOrders.Select(ToDryRunOrderRow).ToArray(),
            liveOrders.Select(ToLiveOrderRow).ToArray(),
            liveTradingEvents.Select(ToLiveTradingEventRow).ToArray(),
            liveReadiness,
            orderBookSnapshots.Select(ToMarketDataRow).ToArray(),
            dailyReports.Select(ToDailyReportRow).ToArray(),
            traderPerformance.Select(ToTraderPerformanceRow).ToArray(),
            categoryPerformance.Select(ToCategoryPerformanceRow).ToArray(),
            executionQuality.Select(ToExecutionQualityRow).ToArray(),
            rejectionAnalysis.Select(ToRejectionAnalysisRow).ToArray(),
            riskUsage,
            BuildDiagnostics(overview, scannerStatuses, marketDataStatuses, apiErrors, riskUsage, authReadiness),
            BuildRunbookLinks(),
            BuildLogs(apiErrors, riskEvents, commandAudits, marketDataEvents, liveTradingEvents));
    }

    public void InvalidateStrategyPerformanceCache()
    {
        cachedStrategyPerformance = null;
        cachedStrategyPerformanceAtUtc = DateTimeOffset.MinValue;
        cachedStrategyRecentPerformance = null;
        cachedStrategyRecentPerformanceAtUtc = DateTimeOffset.MinValue;
    }

    private async Task<IReadOnlyList<StrategyPerformance>> GetStrategyPerformanceAsync(
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var refreshInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.Dashboard.StrategyRefreshIntervalSeconds));
        if (cachedStrategyPerformance is not null &&
            nowUtc - cachedStrategyPerformanceAtUtc < refreshInterval)
        {
            return cachedStrategyPerformance;
        }

        cachedStrategyPerformance = await repository.GetStrategyPerformanceAsync(250, cancellationToken);
        cachedStrategyPerformanceAtUtc = nowUtc;
        return cachedStrategyPerformance;
    }

    private async Task<IReadOnlyList<StrategyRecentPerformance>> GetStrategyRecentPerformanceAsync(
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var refreshInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.Dashboard.StrategyRefreshIntervalSeconds));
        if (cachedStrategyRecentPerformance is not null &&
            nowUtc - cachedStrategyRecentPerformanceAtUtc < refreshInterval)
        {
            return cachedStrategyRecentPerformance;
        }

        cachedStrategyRecentPerformance = await repository.GetStrategyRecentPerformanceAsync(250, cancellationToken);
        cachedStrategyRecentPerformanceAtUtc = nowUtc;
        return cachedStrategyRecentPerformance;
    }

    private IReadOnlyList<OverviewMetric> BuildOverview(
        IReadOnlyList<ServiceHeartbeat> heartbeats,
        IReadOnlyList<ScannerStatusSnapshot> scannerStatuses,
        IReadOnlyList<PaperOrder> openPaperOrders,
        IReadOnlyList<PaperPosition> paperPositions,
        IReadOnlyList<LiveOrder> liveOrders,
        IReadOnlyList<LiveTradingEvent> liveTradingEvents,
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
            new OverviewMetric("Live open orders", liveOrders.Count(order => order.Status is LiveOrderStatus.Submitted or LiveOrderStatus.Live or LiveOrderStatus.Delayed or LiveOrderStatus.Unmatched).ToString()),
            new OverviewMetric("Geoblock status", FormatGeoblockOverview(liveTradingEvents)),
            new OverviewMetric("Auth", authReadiness.State),
            new OverviewMetric("Scanner status", scanner?.ScannerStatus ?? "No scanner status"),
            new OverviewMetric("WebSocket status", marketData?.ConnectionState.ToString() ?? "No market data status"),
            new OverviewMetric("Subscribed assets", marketData?.SubscribedAssetsCount.ToString() ?? "0"),
            new OverviewMetric("Last WS message UTC", FormatDate(marketData?.LastMessageUtc)),
            new OverviewMetric("Paper bankroll", FormatUsd(configuration.PaperTrading.InitialBankrollUsd)),
            new OverviewMetric("Open paper exposure", FormatUsd(openExposure)),
            new OverviewMetric("Paper position value", FormatUsd(positionValue)),
            new OverviewMetric("Daily paper PnL", FormatUsd(paperPnl)),
            new OverviewMetric("Open paper orders", openPaperOrders.Count.ToString()),
            new OverviewMetric(
                "On-chain ingestion",
                configuration.OnChainIngestion.Enabled
                    ? $"{configuration.OnChainIngestion.LookbackDays}d catch-up; live tail only"
                    : configuration.OnChainIngestion.TradeCaptureEnabled
                        ? "Diagnostic trade capture only"
                        : "Disabled")
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

    private IReadOnlyList<LiveReadinessRow> BuildLiveReadiness(
        AuthReadinessStatus authReadiness,
        IReadOnlyList<StrategyPerformance> strategies,
        IReadOnlyList<DryRunOrder> dryRunOrders,
        IReadOnlyList<LiveOrder> liveOrders,
        IReadOnlyList<LiveTradingEvent> liveTradingEvents,
        IReadOnlyList<ApiError> apiErrors,
        IReadOnlyList<RiskEvent> riskEvents,
        IReadOnlyList<MarketDataStatusSnapshot> marketDataStatuses,
        ControlStatusResponse? controlStatus,
        string? controlStatusError)
    {
        var now = DateTimeOffset.UtcNow;
        var liveOpenStatuses = new[]
        {
            LiveOrderStatus.Submitted,
            LiveOrderStatus.Live,
            LiveOrderStatus.Delayed,
            LiveOrderStatus.Unmatched,
            LiveOrderStatus.CancelRequested
        };
        var openLiveOrders = liveOrders
            .Where(order => liveOpenStatuses.Contains(order.Status))
            .ToArray();
        var staleLiveOrders = openLiveOrders.Count(order =>
            now - order.CreatedAtUtc > TimeSpan.FromSeconds(configuration.LiveTrading.DefaultOrderTtlSeconds));
        var recentPolymarketErrors = apiErrors.Count(error =>
            error.CreatedAtUtc >= now.AddMinutes(-configuration.LiveTrading.ApiErrorLockoutWindowMinutes) &&
            error.Component.Contains("Polymarket", StringComparison.OrdinalIgnoreCase));
        var dailyLossLockout = riskEvents.Any(item =>
            item.CreatedAtUtc >= now.AddDays(-1) &&
            item.ReasonCode.Contains("daily_loss", StringComparison.OrdinalIgnoreCase));
        var latestGeoblock = LatestEvent(liveTradingEvents, "StartupGeoblockCheck");
        var liveEnabledStrategies = strategies.Where(item => item.LiveStakes).ToArray();
        var fundedLiveStrategies = liveEnabledStrategies
            .Count(item => item.LiveStakeAmount > 0m && item.LiveAvailableBalance >= item.LiveStakeAmount);
        var marketData = marketDataStatuses.FirstOrDefault(item => item.Component == "PolymarketMarketWebSocket")
            ?? marketDataStatuses.FirstOrDefault();
        var lastDryRunSigned = dryRunOrders
            .Where(item => item.Status == DryRunOrderStatus.DryRunSigned)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();

        var rows = new List<LiveReadinessRow>
        {
            Gate(
                "Bot mode",
                configuration.Bot.Mode.ToString(),
                configuration.Bot.Mode == BotMode.Live,
                "Live orders are unreachable unless Bot:Mode is Live."),
            Gate(
                "Explicit live enable",
                configuration.Bot.EnableLiveTrading ? "true" : "false",
                configuration.Bot.EnableLiveTrading,
                "Bot:EnableLiveTrading must be true."),
            Gate(
                "Manual enable code",
                string.IsNullOrWhiteSpace(configuration.LiveTrading.ManualEnableCode) ? "empty" : "configured",
                string.Equals(configuration.LiveTrading.ManualEnableCode, "LIVE_TRADING_ENABLED", StringComparison.Ordinal),
                "LiveTrading:ManualEnableCode must equal LIVE_TRADING_ENABLED."),
            Gate(
                "Maker-only execution",
                $"MakerOnly={configuration.Execution.MakerOnly}; AllowTaker={configuration.Execution.AllowTaker}",
                configuration.Execution.MakerOnly && !configuration.Execution.AllowTaker,
                "Initial live trading must remain post-only maker style."),
            new(
                "Max live order size",
                FormatUsd(configuration.LiveTrading.MaxOrderNotionalUsd),
                configuration.LiveTrading.MaxOrderNotionalUsd <= 1m ? "OK" : "Warning",
                "Keep the first live session at a tiny order cap."),
            Gate(
                "Auth readiness",
                authReadiness.State,
                authReadiness.CanAuthenticate,
                AuthDetails(authReadiness)),
            new(
                "Dry-run signed order",
                lastDryRunSigned is null ? "none recent" : FormatDate(lastDryRunSigned.CreatedAtUtc),
                lastDryRunSigned is null ? "Warning" : "OK",
                "Run dry-run signing before a live session and confirm a DryRunSigned row."),
            BuildGeoblockReadinessRow(latestGeoblock),
            controlStatus is null
                ? new LiveReadinessRow("IPC service status", "Unavailable", "Warning", controlStatusError ?? "Dashboard could not read /status.")
                : new LiveReadinessRow("IPC service status", controlStatus.State, controlStatus.State is "Running" or "Paused" ? "OK" : "Warning", controlStatus.LastError ?? string.Empty),
            controlStatus is null
                ? new LiveReadinessRow("Live pause", "Unknown", "Warning", "IPC status is unavailable.")
                : Gate("Live pause", controlStatus.LiveTradingPaused ? "paused" : "clear", !controlStatus.LiveTradingPaused, "Live trading must be unpaused for a live session."),
            controlStatus is null
                ? new LiveReadinessRow("Kill switch", "Unknown", "Warning", "IPC status is unavailable.")
                : Gate("Kill switch", controlStatus.KillSwitchActive ? "active" : "clear", !controlStatus.KillSwitchActive, "Kill switch must be clear."),
            Gate(
                "Open live order count",
                $"{openLiveOrders.Length}/{configuration.LiveTrading.MaxOpenLiveOrders}",
                openLiveOrders.Length < configuration.LiveTrading.MaxOpenLiveOrders,
                "The live preflight blocks when the open live-order cap is reached."),
            Gate(
                "Stale live orders",
                staleLiveOrders.ToString(System.Globalization.CultureInfo.InvariantCulture),
                staleLiveOrders == 0,
                "Stale live orders must be cancelled before new placement."),
            Gate(
                "API error lockout",
                $"{recentPolymarketErrors}/{configuration.LiveTrading.ApiErrorLockoutCount}",
                recentPolymarketErrors < configuration.LiveTrading.ApiErrorLockoutCount,
                $"Window: {configuration.LiveTrading.ApiErrorLockoutWindowMinutes} minutes."),
            Gate(
                "Daily loss lockout",
                dailyLossLockout ? "active" : "clear",
                !dailyLossLockout,
                "Recent daily_loss risk events block live placement."),
            new(
                "Strategy live stakes",
                $"{fundedLiveStrategies}/{liveEnabledStrategies.Length} funded enabled",
                liveEnabledStrategies.Length == 0 ? "Warning" : fundedLiveStrategies == liveEnabledStrategies.Length ? "OK" : "Warning",
                "A strategy also needs Live enabled and enough Live bal for its next stake."),
            new(
                "Market WebSocket",
                marketData?.ConnectionState.ToString() ?? "No status",
                marketData?.ConnectionState == MarketDataConnectionState.Connected && !marketData.Stale ? "OK" : "Warning",
                "Not the only live price source, but useful for preflight quality.")
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

    private static IReadOnlyList<RunbookLinkRow> BuildRunbookLinks()
    {
        return
        [
            new("Runbook", "docs/runbook.md", "Daily and weekly operating checklist."),
            new("Incident Response", "docs/incident_response.md", "Action guide for API, WebSocket, DB, geoblock, clock, signing, and live-order incidents."),
            new("Live Trading Checklist", "docs/live_trading_checklist.md", "Required checks before live mode or tiny production orders."),
            new("Paper Evaluation", "docs/paper_trading_evaluation.md", "How to judge whether paper results are viable."),
            new("Configuration Reference", "docs/configuration_reference.md", "Config sections, safety defaults, and secret handling.")
        ];
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

    private static TraderDiscoveryRow ToTraderDiscoveryRow(TraderDiscoveryCandidate candidate)
    {
        return new TraderDiscoveryRow(
            FormatDate(candidate.SnapshotAtUtc),
            candidate.DiscoveryType,
            candidate.Category,
            candidate.TimePeriod,
            candidate.Rank?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a",
            string.IsNullOrWhiteSpace(candidate.UserName) ? candidate.Wallet : candidate.UserName,
            candidate.Wallet,
            candidate.LeaderboardPnl,
            candidate.LeaderboardVolume,
            candidate.AllTimePnl,
            candidate.AllTimeVolume,
            candidate.VerifiedBadge,
            candidate.TradesFetched,
            candidate.BuyTrades,
            candidate.SellTrades,
            candidate.RecentTradeVolumeUsd,
            candidate.AverageTradeUsd,
            FormatDate(candidate.LastTradeUtc),
            candidate.PositionsFetched,
            candidate.OpenPositionValueUsd,
            candidate.OpenPositionCashPnlUsd,
            candidate.Notes);
    }

    private static OnChainTraderRow ToOnChainTraderRow(TraderOnChainStats stats)
    {
        return new OnChainTraderRow(
            stats.Wallet,
            stats.Fills,
            stats.BuyFills,
            stats.SellFills,
            stats.MarketsTraded,
            stats.VolumeUsd,
            stats.AverageTradeUsd,
            stats.FeesUsd,
            stats.ActivityScore,
            FormatDate(stats.FirstTradeUtc),
            FormatDate(stats.LastTradeUtc));
    }

    private static OnChainLeaderRow ToOnChainLeaderRow(PolymarketOnChainWalletPerformance performance)
    {
        return new OnChainLeaderRow(
            performance.Wallet,
            performance.Score,
            performance.SampleQuality,
            performance.ResolvedPnlUsd,
            performance.ResolvedRoiPct,
            performance.WinRatePct,
            performance.ResolvedPositions,
            performance.OpenPositions,
            performance.MarketsTraded,
            performance.VolumeUsd,
            performance.OpenExposureUsd,
            performance.AveragePositionSizeUsd,
            FormatDate(performance.LastActiveUtc));
    }

    private static OnChainFillRow ToOnChainFillRow(PolymarketOnChainWalletExecution execution)
    {
        return new OnChainFillRow(
            FormatDate(execution.BlockTimestampUtc),
            execution.Wallet,
            execution.Side.ToString(),
            execution.TokenId,
            execution.AveragePrice,
            execution.SizeShares,
            execution.NotionalUsd,
            execution.ContractName,
            execution.ExchangeVersion,
            execution.TransactionHash);
    }

    private static OnChainPositionRow ToOnChainPositionRow(PolymarketOnChainWalletPosition position)
    {
        return new OnChainPositionRow(
            position.Wallet,
            string.IsNullOrWhiteSpace(position.MarketTitle) ? position.MarketSlug : position.MarketTitle,
            position.Outcome,
            position.Category ?? string.Empty,
            position.PositionStatus,
            position.NetShares,
            position.NetCostUsd,
            position.BuyShares,
            position.SellShares,
            position.AverageBuyPrice,
            position.AverageSellPrice,
            position.VolumeUsd,
            FormatDecimal(position.ResolvedPnlUsd),
            FormatDate(position.LastTradeUtc),
            position.TokenId);
    }

    private static OnChainTradeDetailRow ToOnChainTradeDetailRow(PolymarketOnChainTradeDetails trade)
    {
        var status = trade.MarketResolved
            ? $"Resolved {trade.WinningOutcome ?? string.Empty}".Trim()
            : trade.MarketClosed ? "Closed" : trade.MarketActive ? "Active" : trade.LookupSucceeded ? "Inactive" : "Unenriched";

        return new OnChainTradeDetailRow(
            FormatDate(trade.BlockTimestampUtc),
            string.IsNullOrWhiteSpace(trade.MarketTitle) ? trade.MarketSlug : trade.MarketTitle,
            trade.Outcome,
            trade.Category ?? string.Empty,
            trade.Maker,
            trade.Taker,
            trade.MakerSide.ToString(),
            trade.TakerSide.ToString(),
            trade.Price,
            trade.SizeShares,
            trade.NotionalUsd,
            trade.MakerAmount,
            trade.TakerAmount,
            trade.FeeAmount,
            status,
            trade.TokenId,
            trade.TransactionHash);
    }

    private static OnChainParticipantDetailRow ToOnChainParticipantDetailRow(PolymarketOnChainParticipantDetails participant)
    {
        return new OnChainParticipantDetailRow(
            participant.Wallet,
            participant.Executions,
            participant.BuyExecutions,
            participant.SellExecutions,
            participant.MarketsTraded,
            participant.PositionsCount,
            participant.OpenPositions,
            participant.ResolvedPositions,
            participant.VolumeUsd,
            participant.AverageTradeUsd,
            participant.FeesUsd,
            participant.OpenExposureUsd,
            participant.ResolvedPnlUsd,
            participant.ResolvedRoiPct,
            participant.WinRatePct,
            participant.Score,
            participant.SampleQuality,
            FormatDate(participant.FirstTradeUtc),
            FormatDate(participant.LastTradeUtc));
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
            order.CopiedTraderWallet,
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
            string.IsNullOrWhiteSpace(position.CopiedTraderWallet) ? "n/a" : position.CopiedTraderWallet);
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

    private static PaperCopiedTraderPerformanceRow ToPaperCopiedTraderPerformanceRow(PaperCopiedTraderPerformance performance)
    {
        return new PaperCopiedTraderPerformanceRow(
            performance.CopiedTraderWallet,
            performance.Category,
            performance.Score,
            performance.TotalPnlUsd,
            performance.RoiPct,
            performance.WinRatePct,
            performance.OrdersCount,
            performance.FilledOrdersCount,
            performance.OpenPositionsCount,
            performance.SettledPositionsCount,
            performance.WonPositionsCount,
            performance.LostPositionsCount,
            performance.BuyCostUsd,
            performance.RealizedPnlUsd,
            performance.UnrealizedPnlUsd,
            FormatDate(performance.LastOrderUtc),
            FormatDate(performance.RefreshedAtUtc));
    }

    private static StrategyPerformanceRow ToStrategyPerformanceRow(StrategyPerformance performance)
    {
        return new StrategyPerformanceRow(
            performance.StrategyId,
            performance.Name,
            performance.Enabled,
            performance.LiveStakes,
            performance.PaperStakeAmount,
            performance.LiveStakeAmount,
            performance.LiveAvailableBalance,
            performance.OrdersCount,
            performance.FilledOrdersCount,
            performance.OpenOrdersCount,
            performance.OpenPositionsCount,
            performance.ObservedRunsCount,
            performance.EnteredRunsCount,
            performance.SkippedRunsCount,
            performance.SettledRunsCount,
            performance.SettledPositionsCount,
            performance.WonPositionsCount,
            performance.LostPositionsCount,
            performance.StakeUsd,
            performance.RealizedPnlUsd,
            performance.UnrealizedPnlUsd,
            performance.TotalPnlUsd,
            performance.WinRatePct,
            performance.LossRatePct,
            performance.AvgWinPnlUsd,
            performance.AvgLossPnlUsd,
            performance.ProfitFactor,
            performance.ExpectancyPnlUsd,
            performance.RoiPct,
            performance.ClosedRoiPct,
            performance.AvgEntryDelaySeconds,
            performance.MaxEntryDelaySeconds,
            performance.LiveOrdersCount,
            performance.LiveFilledOrdersCount,
            performance.LiveOpenOrdersCount,
            performance.LiveSettledOrdersCount,
            performance.LiveWonOrdersCount,
            performance.LiveLostOrdersCount,
            performance.LiveStakeUsd,
            performance.LiveRealizedPnlUsd,
            performance.LiveWinRatePct,
            performance.LiveLossRatePct,
            performance.LiveAvgWinPnlUsd,
            performance.LiveAvgLossPnlUsd,
            performance.LiveProfitFactor,
            performance.LiveExpectancyPnlUsd,
            performance.LiveRoiPct,
            FormatDate(performance.LiveLastOrderUtc),
            FormatDate(performance.LiveLastSettlementUtc),
            FormatDate(performance.LastOrderUtc),
            FormatDate(performance.LastRunUtc));
    }

    private static StrategyRecentPerformanceRow ToStrategyRecentPerformanceRow(StrategyRecentPerformance performance)
    {
        return new StrategyRecentPerformanceRow(
            performance.Window,
            performance.WindowHours,
            performance.Name,
            performance.OrdersCount,
            performance.FilledOrdersCount,
            performance.ExpiredOrdersCount,
            performance.OpenOrdersCount,
            performance.EnteredRunsCount,
            performance.SkippedRunsCount,
            performance.SettledRunsCount,
            performance.WonRunsCount,
            performance.LostRunsCount,
            performance.WinRatePct,
            performance.RoiPct,
            performance.RealizedPnlUsd,
            performance.FilledCostUsd,
            performance.AvgFillPrice,
            performance.AvgEntryDelaySeconds,
            performance.MaxEntryDelaySeconds,
            performance.TopSkipReason,
            FormatDate(performance.LastOrderUtc),
            FormatDate(performance.LastRunUtc));
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
            order.AverageFillPrice,
            order.FilledNotionalUsd,
            order.CostBasisUsd,
            order.FeeUsd,
            order.SettlementValueUsd,
            order.RealizedPnlUsd,
            FormatDate(order.SettledAtUtc),
            order.WinningOutcome ?? string.Empty,
            order.Won,
            order.SettlementSource,
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

    private static LiveReadinessRow Gate(string gate, string value, bool passed, string details)
    {
        return new LiveReadinessRow(gate, value, passed ? "OK" : "Blocked", details);
    }

    private static LiveReadinessRow BuildGeoblockReadinessRow(LiveTradingEvent? latestGeoblock)
    {
        if (latestGeoblock is null)
        {
            return new LiveReadinessRow(
                "Startup geoblock check",
                "No event",
                "Warning",
                "Restart the service on the intended host so StartupGeoblockCheck is recorded.");
        }

        var status = latestGeoblock.Status switch
        {
            "OK" => "OK",
            "Blocked" => "Blocked",
            "Error" => "Error",
            _ => "Warning"
        };
        return new LiveReadinessRow(
            "Startup geoblock check",
            latestGeoblock.Status,
            status,
            $"{FormatDate(latestGeoblock.CreatedAtUtc)} {latestGeoblock.Details}");
    }

    private static LiveTradingEvent? LatestEvent(IReadOnlyList<LiveTradingEvent> events, string action)
    {
        return events
            .Where(item => string.Equals(item.Action, action, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static string FormatGeoblockOverview(IReadOnlyList<LiveTradingEvent> liveTradingEvents)
    {
        var latest = LatestEvent(liveTradingEvents, "StartupGeoblockCheck");
        return latest is null
            ? "No startup check event"
            : $"{latest.Status} {FormatDate(latest.CreatedAtUtc)}";
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
