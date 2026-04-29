using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class DashboardDataService(
    IAppRepository repository,
    AppConfiguration configuration,
    bool storageConfigured)
{
    public async Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var heartbeats = await repository.GetServiceHeartbeatsAsync(cancellationToken);
        var scannerStatuses = await repository.GetScannerStatusesAsync(cancellationToken);
        var leaderTrades = await repository.GetRecentLeaderTradesAsync(cancellationToken);
        var signals = await repository.GetRecentSignalsAsync(cancellationToken: cancellationToken);
        var recentPaperOrders = await repository.GetRecentPaperOrdersAsync(cancellationToken: cancellationToken);
        var openPaperOrders = await repository.GetOpenPaperOrdersAsync(cancellationToken);
        var paperPositions = await repository.GetPaperPositionsAsync(cancellationToken);
        var apiErrors = await repository.GetRecentApiErrorsAsync(cancellationToken: cancellationToken);
        var riskEvents = await repository.GetRecentRiskEventsAsync(cancellationToken: cancellationToken);

        return new DashboardSnapshot(
            BuildOverview(heartbeats, scannerStatuses, openPaperOrders, paperPositions, apiErrors),
            BuildWatchlist(scannerStatuses, leaderTrades),
            leaderTrades.Select(ToLeaderTradeRow).ToArray(),
            signals.Select(ToSignalRow).ToArray(),
            recentPaperOrders.Select(ToPaperOrderRow).ToArray(),
            paperPositions.Select(ToPaperPositionRow).ToArray(),
            BuildRiskUsage(openPaperOrders, paperPositions),
            BuildLogs(apiErrors, riskEvents));
    }

    private IReadOnlyList<OverviewMetric> BuildOverview(
        IReadOnlyList<ServiceHeartbeat> heartbeats,
        IReadOnlyList<ScannerStatusSnapshot> scannerStatuses,
        IReadOnlyList<PaperOrder> openPaperOrders,
        IReadOnlyList<PaperPosition> paperPositions,
        IReadOnlyList<ApiError> apiErrors)
    {
        var heartbeat = heartbeats.FirstOrDefault(item => item.ServiceName == "PolyCopyTrader.Service")
            ?? heartbeats.FirstOrDefault();
        var scanner = scannerStatuses.FirstOrDefault();
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
            new OverviewMetric("Geoblock status", "Not checked by dashboard"),
            new OverviewMetric("Scanner status", scanner?.ScannerStatus ?? "No scanner status"),
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

    private static IReadOnlyList<LogRow> BuildLogs(
        IReadOnlyList<ApiError> apiErrors,
        IReadOnlyList<RiskEvent> riskEvents)
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

    private static PaperPositionRow ToPaperPositionRow(PaperPosition position)
    {
        return new PaperPositionRow(
            position.ConditionId,
            position.Outcome,
            position.SizeShares,
            position.AveragePrice,
            "n/a",
            "n/a",
            position.EstimatedValueUsd,
            position.UnrealizedPnlUsd,
            "n/a",
            "n/a");
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

    private static string FormatUsd(decimal value)
    {
        return value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
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
