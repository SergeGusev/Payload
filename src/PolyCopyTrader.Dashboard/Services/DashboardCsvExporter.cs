using System.IO;
using System.Text;
using PolyCopyTrader.Dashboard.Models;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class DashboardCsvExporter(
    IAppRepository repository,
    IDashboardSnapshotRepository dashboardSnapshots,
    AppConfiguration configuration)
{
    private const int ExportLimit = 25_000;

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var exportRoot = ResolveExportRoot(configuration.Analytics.CsvExportDirectory);
        var exportDirectory = Path.Combine(exportRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(exportDirectory);

        await WriteAsync(
            Path.Combine(exportDirectory, "LeaderTrades.csv"),
            ["TimestampUtc", "TraderWallet", "TraderName", "ConditionId", "AssetId", "MarketSlug", "MarketTitle", "Outcome", "Side", "Price", "Size", "CashValueUsd", "TransactionHash"],
            (await repository.GetRecentLeaderTradesAsync(ExportLimit, cancellationToken)).Select(trade => new object?[]
            {
                trade.TimestampUtc,
                trade.TraderWallet,
                trade.TraderName,
                trade.ConditionId,
                trade.AssetId,
                trade.MarketSlug,
                trade.MarketTitle,
                trade.Outcome,
                trade.Side,
                trade.Price,
                trade.Size,
                trade.CashValueUsd,
                trade.TransactionHash
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "Signals.csv"),
            ["CreatedAtUtc", "SignalId", "TraderWallet", "ConditionId", "AssetId", "Outcome", "Score", "Accepted", "DecisionCode", "ReasonCodes", "LeaderPrice", "BestBid", "BestAsk", "SpreadAbs", "SpreadPct", "LagSeconds", "ProposedPaperPrice", "ProposedSizeShares", "ProposedNotionalUsd"],
            (await repository.GetRecentSignalsAsync(ExportLimit, cancellationToken)).Select(signal => new object?[]
            {
                signal.CreatedAtUtc,
                signal.Id,
                signal.TraderWallet,
                signal.ConditionId,
                signal.AssetId,
                signal.Outcome,
                signal.Score,
                signal.Accepted,
                signal.DecisionCode,
                string.Join("; ", signal.ReasonCodes),
                signal.LeaderPrice,
                signal.BestBid,
                signal.BestAsk,
                signal.SpreadAbs,
                signal.SpreadPct,
                signal.LagSeconds,
                signal.ProposedPaperPrice,
                signal.ProposedSizeShares,
                signal.ProposedNotionalUsd
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "SignalRejections.csv"),
            ["CreatedAtUtc", "SignalId", "ReasonCode", "ReasonDetails", "RejectionId"],
            (await repository.GetRecentSignalRejectionsAsync(ExportLimit, cancellationToken)).Select(rejection => new object?[]
            {
                rejection.CreatedAtUtc,
                rejection.SignalId,
                rejection.ReasonCode,
                rejection.ReasonDetails,
                rejection.Id
            }),
            cancellationToken);

        var paperOrders = await repository.GetRecentPaperOrdersAsync(ExportLimit, cancellationToken);
        var paperOrderIds = paperOrders
            .Select(order => order.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var paperOrderFills = await repository.GetPaperFillsForOrdersAsync(paperOrderIds, cancellationToken);
        var paperOrderFeeSummaries = paperOrderFills
            .GroupBy(fill => fill.PaperOrderId)
            .ToDictionary(group => group.Key, group => BuildPaperOrderFeeSummary(group.ToArray()));

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperOrders.csv"),
            ["CreatedAtUtc", "OrderId", "SignalId", "CopiedTraderWallet", "Status", "Side", "AssetId", "ConditionId", "Outcome", "Price", "SizeShares", "NotionalUsd", "ExpiresAtUtc", "FilledAtUtc", "CancelledAtUtc", "FeeUsd", "FeeAccountingStatus", "FeeLiquidityRole", "FeeCalculationSource", "FeeRate", "FeeExponent", "FeeTakerOnly", "FeeCalculatedAtUtc", "NetRealizedPnlUsd"],
            paperOrders.Select(order =>
            {
                var fee = paperOrderFeeSummaries.GetValueOrDefault(order.Id, PaperOrderFeeSummary.LegacyUnknown);
                return new object?[]
                {
                    order.CreatedAtUtc,
                    order.Id,
                    order.SignalId,
                    order.CopiedTraderWallet,
                    order.Status,
                    order.Side,
                    order.AssetId,
                    order.ConditionId,
                    order.Outcome,
                    order.Price,
                    order.SizeShares,
                    order.NotionalUsd,
                    order.ExpiresAtUtc,
                    order.FilledAtUtc,
                    order.CancelledAtUtc,
                    fee.FeeUsd,
                    fee.FeeAccountingStatus,
                    fee.FeeLiquidityRole,
                    fee.FeeCalculationSource,
                    fee.FeeRate,
                    fee.FeeExponent,
                    fee.FeeTakerOnly,
                    fee.FeeCalculatedAtUtc,
                    fee.NetRealizedPnlUsd
                };
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperPositions.csv"),
            ["UpdatedAtUtc", "CopiedTraderWallet", "AssetId", "ConditionId", "Outcome", "SizeShares", "AveragePrice", "EstimatedValueUsd", "GrossUnrealizedPnlUsd", "FeeUsd", "FeeAccountingStatus", "FeeLiquidityRole", "FeeCalculationSource", "FeeRate", "FeeExponent", "FeeTakerOnly", "FeeCalculatedAtUtc", "NetUnrealizedPnlUsd"],
            (await repository.GetPaperPositionsAsync(cancellationToken)).Select(position => new object?[]
            {
                position.UpdatedAtUtc,
                position.CopiedTraderWallet,
                position.AssetId,
                position.ConditionId,
                position.Outcome,
                position.SizeShares,
                position.AveragePrice,
                position.EstimatedValueUsd,
                position.UnrealizedPnlUsd,
                position.FeeUsd,
                position.FeeAccountingStatus,
                position.FeeLiquidityRole,
                position.FeeCalculationSource,
                position.FeeRate,
                position.FeeExponent,
                position.FeeTakerOnly,
                position.FeeCalculatedAtUtc,
                position.NetUnrealizedPnlUsd
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperPositionSettlements.csv"),
            ["SettledAtUtc", "CopiedTraderWallet", "AssetId", "ConditionId", "Outcome", "WinningAssetId", "WinningOutcome", "Category", "SettledSizeShares", "AveragePrice", "GrossCostBasisUsd", "SettlementValueUsd", "GrossRealizedPnlUsd", "FeeUsd", "FeeAccountingStatus", "FeeLiquidityRole", "FeeCalculationSource", "FeeRate", "FeeExponent", "FeeTakerOnly", "FeeCalculatedAtUtc", "NetRealizedPnlUsd", "Won", "SettlementSource"],
            (await repository.GetRecentPaperPositionSettlementsAsync(ExportLimit, cancellationToken)).Select(settlement => new object?[]
            {
                settlement.SettledAtUtc,
                settlement.CopiedTraderWallet,
                settlement.AssetId,
                settlement.ConditionId,
                settlement.Outcome,
                settlement.WinningAssetId,
                settlement.WinningOutcome,
                settlement.Category,
                settlement.SettledSizeShares,
                settlement.AveragePrice,
                settlement.CostBasisUsd,
                settlement.SettlementValueUsd,
                settlement.RealizedPnlUsd,
                settlement.FeeUsd,
                settlement.FeeAccountingStatus,
                settlement.FeeLiquidityRole,
                settlement.FeeCalculationSource,
                settlement.FeeRate,
                settlement.FeeExponent,
                settlement.FeeTakerOnly,
                settlement.FeeCalculatedAtUtc,
                settlement.NetRealizedPnlUsd,
                settlement.Won,
                settlement.SettlementSource
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperCopiedTraderPerformance.csv"),
            ["CopiedTraderWallet", "Category", "Score", "GrossMarkToMarketPnlUsd", "GrossMarkToMarketRoiPct", "WinRatePct", "OrdersCount", "FilledOrdersCount", "OpenPositionsCount", "SettledPositionsCount", "WonPositionsCount", "LostPositionsCount", "BuyCostUsd", "SellProceedsUsd", "SettlementValueUsd", "GrossRealizedPnlUsd", "GrossUnrealizedPnlUsd", "FirstOrderUtc", "LastOrderUtc", "RefreshedAtUtc"],
            (await repository.GetPaperCopiedTraderPerformanceAsync(ExportLimit, cancellationToken)).Select(performance => new object?[]
            {
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
                performance.SellProceedsUsd,
                performance.SettlementValueUsd,
                performance.RealizedPnlUsd,
                performance.UnrealizedPnlUsd,
                performance.FirstOrderUtc,
                performance.LastOrderUtc,
                performance.RefreshedAtUtc
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "Strategies.csv"),
            [
                "Name", "Enabled", "LiveStakes", "Paused", "PausedUntilUtc", "PaperStakeAmount",
                "LiveStakeAmount", "PaperLostCoeff", "LiveLostCoeff", "PaperLostCounter", "LiveLostCounter",
                "LiveAvailableBalance", "OrdersCount", "FilledOrdersCount", "OpenOrdersCount", "OpenPositionsCount",
                "ObservedRunsCount", "EnteredRunsCount", "SkippedRunsCount", "PaperConditionSkippedRunsCount",
                "PaperNotAcceptedRunsCount", "SettledRunsCount", "SettledPositionsCount", "WonPositionsCount",
                "LostPositionsCount", "StakeUsd",
                "NetRealizedPnlUsd", "NetOpenUnrealizedPnlUsd", "NetMarkToMarketPnlUsd",
                "NetMarkToMarketRoiPct", "NetClosedRoiPct", "AccountedFeeUsd",
                "FeeAccountedSettledCount", "FeeRequiredSettledCount", "ClosedFeeCoverage",
                "FeeAccountedOpenPositionCount", "FeeRequiredOpenPositionCount", "MarkToMarketFeeCoverage",
                "GrossRealizedPnlUsd", "GrossOpenUnrealizedPnlUsd", "GrossMarkToMarketPnlUsd",
                "WinRatePct", "LossRatePct", "GrossAvgWinPnlUsd", "GrossAvgLossPnlUsd", "GrossProfitFactor",
                "GrossExpectancyPnlUsd", "GrossMarkToMarketRoiPct", "GrossClosedRoiPct",
                "AvgEntryDelaySeconds", "MaxEntryDelaySeconds", "AvgCountertrendScoreBps",
                "AvgCountertrendSignalBps", "LastCountertrendSignalBps", "LiveOrdersCount",
                "LiveFilledOrdersCount", "LiveOpenOrdersCount", "LiveSettledOrdersCount", "LiveSkippedOrdersCount",
                "LiveConditionSkippedOrdersCount", "LiveTechnicalSkippedOrdersCount", "LiveIgnoredOrdersCount",
                "LiveIgnoredGtdUnfilledCount", "LiveIgnoredCancelledOrdersCount", "LiveIgnoredRejectedOrdersCount",
                "LiveWonOrdersCount", "LiveLostOrdersCount", "LiveStakeUsd",
                "LiveNetRealizedPnlUsd", "LiveNetRoiPct", "LiveAccountedFeeUsd",
                "LiveFeeAccountedSettledCount", "LiveFeeRequiredSettledCount", "LiveFeeCoverage",
                "GrossLiveRealizedPnlUsd", "LiveWinRatePct", "LiveLossRatePct", "GrossLiveAvgWinPnlUsd",
                "GrossLiveAvgLossPnlUsd", "GrossLiveProfitFactor", "GrossLiveExpectancyPnlUsd", "GrossLiveRoiPct",
                "LiveLastOrderUtc", "LiveLastSettlementUtc", "LastOrderUtc", "LastRunUtc"
            ],
            (await dashboardSnapshots.GetStrategyPerformanceSnapshotAsync(ExportLimit, cancellationToken)).Select(strategy => new object?[]
            {
                strategy.Name,
                strategy.Enabled,
                strategy.LiveStakes,
                strategy.Paused,
                strategy.PausedUntilUtc,
                strategy.PaperStakeAmount,
                strategy.LiveStakeAmount,
                strategy.PaperLostCoeff,
                strategy.LiveLostCoeff,
                strategy.PaperLostCounter,
                strategy.LiveLostCounter,
                strategy.LiveAvailableBalance,
                strategy.OrdersCount,
                strategy.FilledOrdersCount,
                strategy.OpenOrdersCount,
                strategy.OpenPositionsCount,
                strategy.ObservedRunsCount,
                strategy.EnteredRunsCount,
                strategy.SkippedRunsCount,
                strategy.PaperConditionSkippedRunsCount,
                strategy.PaperNotAcceptedRunsCount,
                strategy.SettledRunsCount,
                strategy.SettledPositionsCount,
                strategy.WonPositionsCount,
                strategy.LostPositionsCount,
                strategy.StakeUsd,
                strategy.NetRealizedPnlUsd,
                strategy.NetUnrealizedPnlUsd,
                strategy.NetTotalPnlUsd,
                strategy.NetRoiPct,
                strategy.NetClosedRoiPct,
                strategy.AccountedFeeUsd,
                strategy.FeeAccountedSettledCount,
                strategy.FeeRequiredSettledCount,
                FormatFeeCoverage(strategy.FeeAccountedSettledCount, strategy.FeeRequiredSettledCount),
                strategy.FeeAccountedOpenPositionCount,
                strategy.FeeRequiredOpenPositionCount,
                FormatFeeCoverage(
                    strategy.FeeAccountedSettledCount + strategy.FeeAccountedOpenPositionCount,
                    strategy.FeeRequiredSettledCount + strategy.FeeRequiredOpenPositionCount),
                strategy.RealizedPnlUsd,
                strategy.UnrealizedPnlUsd,
                strategy.TotalPnlUsd,
                strategy.WinRatePct,
                strategy.LossRatePct,
                strategy.AvgWinPnlUsd,
                strategy.AvgLossPnlUsd,
                strategy.ProfitFactor,
                strategy.ExpectancyPnlUsd,
                strategy.RoiPct,
                strategy.ClosedRoiPct,
                strategy.AvgEntryDelaySeconds,
                strategy.MaxEntryDelaySeconds,
                strategy.AvgCountertrendScoreBps,
                strategy.AvgCountertrendSignalBps,
                strategy.LastCountertrendSignalBps,
                strategy.LiveOrdersCount,
                strategy.LiveFilledOrdersCount,
                strategy.LiveOpenOrdersCount,
                strategy.LiveSettledOrdersCount,
                strategy.LiveSkippedOrdersCount,
                strategy.LiveConditionSkippedOrdersCount,
                strategy.LiveTechnicalSkippedOrdersCount,
                strategy.LiveIgnoredOrdersCount,
                strategy.LiveIgnoredGtdUnfilledCount,
                strategy.LiveIgnoredCancelledOrdersCount,
                strategy.LiveIgnoredRejectedOrdersCount,
                strategy.LiveWonOrdersCount,
                strategy.LiveLostOrdersCount,
                strategy.LiveStakeUsd,
                strategy.LiveNetRealizedPnlUsd,
                strategy.LiveNetRoiPct,
                strategy.LiveAccountedFeeUsd,
                strategy.LiveFeeAccountedSettledCount,
                strategy.LiveFeeRequiredSettledCount,
                FormatFeeCoverage(
                    strategy.LiveFeeAccountedSettledCount,
                    strategy.LiveFeeRequiredSettledCount),
                strategy.LiveRealizedPnlUsd,
                strategy.LiveWinRatePct,
                strategy.LiveLossRatePct,
                strategy.LiveAvgWinPnlUsd,
                strategy.LiveAvgLossPnlUsd,
                strategy.LiveProfitFactor,
                strategy.LiveExpectancyPnlUsd,
                strategy.LiveRoiPct,
                strategy.LiveLastOrderUtc,
                strategy.LiveLastSettlementUtc,
                strategy.LastOrderUtc,
                strategy.LastRunUtc
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "StrategyRecentPerformance.csv"),
            [
                "Window", "Name", "OrdersCount", "FilledOrdersCount", "ExpiredOrdersCount", "OpenOrdersCount",
                "EnteredRunsCount", "SkippedRunsCount", "PaperConditionSkippedRunsCount",
                "PaperNotAcceptedRunsCount", "SettledRunsCount", "WonRunsCount", "LostRunsCount", "WinRatePct",
                "NetRealizedPnlUsd", "NetRoiPct", "AccountedFeeUsd", "FeeAccountedSettledCount",
                "FeeRequiredSettledCount", "FeeCoverage", "GrossRealizedPnlUsd", "GrossRoiPct",
                "GrossFilledCostUsd", "LiveSettledOrdersCount", "LiveSkippedOrdersCount",
                "LiveConditionSkippedOrdersCount", "LiveTechnicalSkippedOrdersCount", "LiveIgnoredOrdersCount",
                "LiveIgnoredGtdUnfilledCount", "LiveIgnoredCancelledOrdersCount", "LiveIgnoredRejectedOrdersCount",
                "LiveWonOrdersCount", "LiveLostOrdersCount", "LiveNetRealizedPnlUsd", "LiveNetRoiPct",
                "LiveAccountedFeeUsd", "LiveFeeAccountedSettledCount", "LiveFeeRequiredSettledCount",
                "LiveFeeCoverage", "GrossLiveRealizedPnlUsd", "GrossLiveRoiPct", "AvgFillPrice",
                "AvgEntryDelaySeconds", "MaxEntryDelaySeconds", "TopSkipReason", "LastOrderUtc", "LastRunUtc"
            ],
            (await dashboardSnapshots.GetStrategyRecentPerformanceSnapshotAsync(ExportLimit, cancellationToken)).Select(strategy => new object?[]
            {
                strategy.Window,
                strategy.Name,
                strategy.OrdersCount,
                strategy.FilledOrdersCount,
                strategy.ExpiredOrdersCount,
                strategy.OpenOrdersCount,
                strategy.EnteredRunsCount,
                strategy.SkippedRunsCount,
                strategy.PaperConditionSkippedRunsCount,
                strategy.PaperNotAcceptedRunsCount,
                strategy.SettledRunsCount,
                strategy.WonRunsCount,
                strategy.LostRunsCount,
                strategy.WinRatePct,
                strategy.NetRealizedPnlUsd,
                strategy.NetRoiPct,
                strategy.AccountedFeeUsd,
                strategy.FeeAccountedSettledCount,
                strategy.FeeRequiredSettledCount,
                FormatFeeCoverage(strategy.FeeAccountedSettledCount, strategy.FeeRequiredSettledCount),
                strategy.RealizedPnlUsd,
                strategy.RoiPct,
                strategy.FilledCostUsd,
                strategy.LiveSettledOrdersCount,
                strategy.LiveSkippedOrdersCount,
                strategy.LiveConditionSkippedOrdersCount,
                strategy.LiveTechnicalSkippedOrdersCount,
                strategy.LiveIgnoredOrdersCount,
                strategy.LiveIgnoredGtdUnfilledCount,
                strategy.LiveIgnoredCancelledOrdersCount,
                strategy.LiveIgnoredRejectedOrdersCount,
                strategy.LiveWonOrdersCount,
                strategy.LiveLostOrdersCount,
                strategy.LiveNetRealizedPnlUsd,
                strategy.LiveNetRoiPct,
                strategy.LiveAccountedFeeUsd,
                strategy.LiveFeeAccountedSettledCount,
                strategy.LiveFeeRequiredSettledCount,
                FormatFeeCoverage(
                    strategy.LiveFeeAccountedSettledCount,
                    strategy.LiveFeeRequiredSettledCount),
                strategy.LiveRealizedPnlUsd,
                strategy.LiveRoiPct,
                strategy.AvgFillPrice,
                strategy.AvgEntryDelaySeconds,
                strategy.MaxEntryDelaySeconds,
                strategy.TopSkipReason,
                strategy.LastOrderUtc,
                strategy.LastRunUtc
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "OnChainTrades.csv"),
            ["BlockTimestampUtc", "MarketTitle", "Outcome", "Category", "Maker", "Taker", "MakerSide", "TakerSide", "Price", "SizeShares", "NotionalUsd", "MakerAmount", "TakerAmount", "FeeAmount", "TokenId", "TransactionHash", "LogIndex", "MarketResolved", "WinningOutcome"],
            (await repository.GetRecentPolymarketOnChainTradeDetailsAsync(ExportLimit, cancellationToken)).Select(trade => new object?[]
            {
                trade.BlockTimestampUtc,
                trade.MarketTitle,
                trade.Outcome,
                trade.Category,
                trade.Maker,
                trade.Taker,
                trade.MakerSide,
                trade.TakerSide,
                trade.Price,
                trade.SizeShares,
                trade.NotionalUsd,
                trade.MakerAmount,
                trade.TakerAmount,
                trade.FeeAmount,
                trade.TokenId,
                trade.TransactionHash,
                trade.LogIndex,
                trade.MarketResolved,
                trade.WinningOutcome
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "OnChainParticipants.csv"),
            ["Wallet", "Executions", "BuyExecutions", "SellExecutions", "MarketsTraded", "PositionsCount", "OpenPositions", "ResolvedPositions", "VolumeUsd", "AverageTradeUsd", "FeesUsd", "OpenExposureUsd", "ResolvedPnlUsd", "ResolvedRoiPct", "WinRatePct", "Score", "SampleQuality", "FirstTradeUtc", "LastTradeUtc"],
            (await repository.GetPolymarketOnChainParticipantDetailsAsync(ExportLimit, cancellationToken)).Select(participant => new object?[]
            {
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
                participant.FirstTradeUtc,
                participant.LastTradeUtc
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "DailyReports.csv"),
            ["ReportDate", "SignalsObserved", "SignalsAccepted", "SignalsRejected", "PaperOrdersCreated", "PaperFills", "PaperExpiredOrders", "PaperPnl", "OpenPaperExposure", "TopRejectionReasons", "ApiErrors", "GeneratedAtUtc"],
            (await repository.GetDailyReportsAsync(ExportLimit, cancellationToken)).Select(report => new object?[]
            {
                report.ReportDate,
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
                report.GeneratedAtUtc
            }),
            cancellationToken);

        return exportDirectory;
    }

    public async Task<string> ExportDashboardErrorsAsync(
        IReadOnlyList<DashboardErrorRow> errors,
        CancellationToken cancellationToken = default)
    {
        var exportRoot = ResolveExportRoot(configuration.Analytics.CsvExportDirectory);
        var exportDirectory = Path.Combine(exportRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-dashboard-errors");
        Directory.CreateDirectory(exportDirectory);

        var path = Path.Combine(exportDirectory, "DashboardErrors.csv");
        await WriteAsync(
            path,
            ["TimestampUtc", "Source", "Message", "Details"],
            errors.Select(error => new object?[]
            {
                error.TimestampUtc,
                error.Source,
                error.Message,
                error.Details
            }),
            cancellationToken);

        return path;
    }

    private static async Task WriteAsync(
        string path,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CsvFormatter.FormatRow(headers));
        foreach (var row in rows)
        {
            builder.AppendLine(CsvFormatter.FormatRow(row));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string FormatFeeCoverage(int accountedCount, int requiredCount)
    {
        return accountedCount == 0 && requiredCount == 0
            ? "N/A"
            : FormattableString.Invariant($"{accountedCount}/{requiredCount}");
    }

    private static PaperOrderFeeSummary BuildPaperOrderFeeSummary(IReadOnlyList<PaperFill> fills)
    {
        var status = FeeAccountingRules.Aggregate(fills.Select(fill => fill.FeeAccountingStatus));
        return new PaperOrderFeeSummary(
            fills.Sum(fill => fill.FeeUsd),
            status.ToString(),
            SingleValueOrDefault(fills.Select(fill => fill.FeeLiquidityRole), FeeLiquidityRole.Unknown.ToString()),
            SingleValueOrDefault(fills.Select(fill => fill.FeeCalculationSource), "mixed"),
            SingleNullableValueOrDefault(fills.Select(fill => fill.FeeRate)),
            SingleNullableValueOrDefault(fills.Select(fill => fill.FeeExponent)),
            SingleNullableValueOrDefault(fills.Select(fill => fill.FeeTakerOnly)),
            fills.Max(fill => fill.FeeCalculatedAtUtc),
            FeeAccountingRules.IsAccounted(status) && fills.All(fill => fill.NetRealizedPnlUsd.HasValue)
                ? fills.Sum(fill => fill.NetRealizedPnlUsd!.Value)
                : null);
    }

    private static string SingleValueOrDefault(IEnumerable<string?> values, string fallback)
    {
        var distinct = values
            .Select(value => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinct.Length == 1 ? distinct[0] : fallback;
    }

    private static T? SingleNullableValueOrDefault<T>(IEnumerable<T?> values)
        where T : struct
    {
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    private static string ResolveExportRoot(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    private sealed record PaperOrderFeeSummary(
        decimal FeeUsd,
        string FeeAccountingStatus,
        string FeeLiquidityRole,
        string FeeCalculationSource,
        decimal? FeeRate,
        int? FeeExponent,
        bool? FeeTakerOnly,
        DateTimeOffset? FeeCalculatedAtUtc,
        decimal? NetRealizedPnlUsd)
    {
        public static readonly PaperOrderFeeSummary LegacyUnknown = new(
            0m,
            PolyCopyTrader.Domain.FeeAccountingStatus.LegacyUnknown.ToString(),
            PolyCopyTrader.Domain.FeeLiquidityRole.Unknown.ToString(),
            string.Empty,
            null,
            null,
            null,
            null,
            null);
    }
}
