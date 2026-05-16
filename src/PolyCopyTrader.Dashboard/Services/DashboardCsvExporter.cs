using System.IO;
using System.Text;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Dashboard.Services;

public sealed class DashboardCsvExporter(
    IAppRepository repository,
    AppConfiguration configuration)
{
    private const int ExportLimit = 10_000;

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

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperOrders.csv"),
            ["CreatedAtUtc", "OrderId", "SignalId", "CopiedTraderWallet", "Status", "Side", "AssetId", "ConditionId", "Outcome", "Price", "SizeShares", "NotionalUsd", "ExpiresAtUtc", "FilledAtUtc", "CancelledAtUtc"],
            (await repository.GetRecentPaperOrdersAsync(ExportLimit, cancellationToken)).Select(order => new object?[]
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
                order.CancelledAtUtc
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperPositions.csv"),
            ["UpdatedAtUtc", "CopiedTraderWallet", "AssetId", "ConditionId", "Outcome", "SizeShares", "AveragePrice", "EstimatedValueUsd", "UnrealizedPnlUsd"],
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
                position.UnrealizedPnlUsd
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperPositionSettlements.csv"),
            ["SettledAtUtc", "CopiedTraderWallet", "AssetId", "ConditionId", "Outcome", "WinningAssetId", "WinningOutcome", "Category", "SettledSizeShares", "AveragePrice", "CostBasisUsd", "SettlementValueUsd", "RealizedPnlUsd", "Won", "SettlementSource"],
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
                settlement.Won,
                settlement.SettlementSource
            }),
            cancellationToken);

        await WriteAsync(
            Path.Combine(exportDirectory, "PaperCopiedTraderPerformance.csv"),
            ["CopiedTraderWallet", "Category", "Score", "MarkToMarketPnlUsd", "MarkToMarketRoiPct", "WinRatePct", "OrdersCount", "FilledOrdersCount", "OpenPositionsCount", "SettledPositionsCount", "WonPositionsCount", "LostPositionsCount", "BuyCostUsd", "SellProceedsUsd", "SettlementValueUsd", "RealizedPnlUsd", "UnrealizedPnlUsd", "FirstOrderUtc", "LastOrderUtc", "RefreshedAtUtc"],
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
            ["Name", "Enabled", "LiveStakes", "PaperStakeAmount", "LiveStakeAmount", "LiveAvailableBalance", "OrdersCount", "FilledOrdersCount", "OpenOrdersCount", "OpenPositionsCount", "ObservedRunsCount", "EnteredRunsCount", "SkippedRunsCount", "SettledRunsCount", "SettledPositionsCount", "WonPositionsCount", "LostPositionsCount", "StakeUsd", "RealizedPnlUsd", "OpenUnrealizedPnlUsd", "MarkToMarketPnlUsd", "WinRatePct", "LossRatePct", "AvgWinPnlUsd", "AvgLossPnlUsd", "ProfitFactor", "ExpectancyPnlUsd", "MarkToMarketRoiPct", "ClosedRoiPct", "AvgEntryDelaySeconds", "MaxEntryDelaySeconds", "LiveOrdersCount", "LiveFilledOrdersCount", "LiveOpenOrdersCount", "LiveSettledOrdersCount", "LiveSkippedOrdersCount", "LiveConditionSkippedOrdersCount", "LiveTechnicalSkippedOrdersCount", "LiveRejectedOrdersCount", "LiveWonOrdersCount", "LiveLostOrdersCount", "LiveStakeUsd", "LiveRealizedPnlUsd", "LiveWinRatePct", "LiveLossRatePct", "LiveAvgWinPnlUsd", "LiveAvgLossPnlUsd", "LiveProfitFactor", "LiveExpectancyPnlUsd", "LiveRoiPct", "LiveLastOrderUtc", "LiveLastSettlementUtc", "LastOrderUtc", "LastRunUtc"],
            (await repository.GetStrategyPerformanceAsync(ExportLimit, cancellationToken)).Select(strategy => new object?[]
            {
                strategy.Name,
                strategy.Enabled,
                strategy.LiveStakes,
                strategy.PaperStakeAmount,
                strategy.LiveStakeAmount,
                strategy.LiveAvailableBalance,
                strategy.OrdersCount,
                strategy.FilledOrdersCount,
                strategy.OpenOrdersCount,
                strategy.OpenPositionsCount,
                strategy.ObservedRunsCount,
                strategy.EnteredRunsCount,
                strategy.SkippedRunsCount,
                strategy.SettledRunsCount,
                strategy.SettledPositionsCount,
                strategy.WonPositionsCount,
                strategy.LostPositionsCount,
                strategy.StakeUsd,
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
                strategy.LiveOrdersCount,
                strategy.LiveFilledOrdersCount,
                strategy.LiveOpenOrdersCount,
                strategy.LiveSettledOrdersCount,
                strategy.LiveSkippedOrdersCount,
                strategy.LiveConditionSkippedOrdersCount,
                strategy.LiveTechnicalSkippedOrdersCount,
                strategy.LiveRejectedOrdersCount,
                strategy.LiveWonOrdersCount,
                strategy.LiveLostOrdersCount,
                strategy.LiveStakeUsd,
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
            ["Window", "Name", "OrdersCount", "FilledOrdersCount", "ExpiredOrdersCount", "OpenOrdersCount", "EnteredRunsCount", "SkippedRunsCount", "SettledRunsCount", "WonRunsCount", "LostRunsCount", "WinRatePct", "RoiPct", "LiveSettledOrdersCount", "LiveSkippedOrdersCount", "LiveConditionSkippedOrdersCount", "LiveTechnicalSkippedOrdersCount", "LiveRejectedOrdersCount", "LiveWonOrdersCount", "LiveLostOrdersCount", "LiveRealizedPnlUsd", "LiveRoiPct", "RealizedPnlUsd", "FilledCostUsd", "AvgFillPrice", "AvgEntryDelaySeconds", "MaxEntryDelaySeconds", "TopSkipReason", "LastOrderUtc", "LastRunUtc"],
            (await repository.GetStrategyRecentPerformanceAsync(ExportLimit, cancellationToken)).Select(strategy => new object?[]
            {
                strategy.Window,
                strategy.Name,
                strategy.OrdersCount,
                strategy.FilledOrdersCount,
                strategy.ExpiredOrdersCount,
                strategy.OpenOrdersCount,
                strategy.EnteredRunsCount,
                strategy.SkippedRunsCount,
                strategy.SettledRunsCount,
                strategy.WonRunsCount,
                strategy.LostRunsCount,
                strategy.WinRatePct,
                strategy.RoiPct,
                strategy.LiveSettledOrdersCount,
                strategy.LiveSkippedOrdersCount,
                strategy.LiveConditionSkippedOrdersCount,
                strategy.LiveTechnicalSkippedOrdersCount,
                strategy.LiveRejectedOrdersCount,
                strategy.LiveWonOrdersCount,
                strategy.LiveLostOrdersCount,
                strategy.LiveRealizedPnlUsd,
                strategy.LiveRoiPct,
                strategy.RealizedPnlUsd,
                strategy.FilledCostUsd,
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

    private static string ResolveExportRoot(string configuredPath)
    {
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }
}
