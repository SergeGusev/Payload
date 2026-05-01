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
            ["CreatedAtUtc", "OrderId", "SignalId", "Status", "Side", "AssetId", "ConditionId", "Outcome", "Price", "SizeShares", "NotionalUsd", "ExpiresAtUtc", "FilledAtUtc", "CancelledAtUtc"],
            (await repository.GetRecentPaperOrdersAsync(ExportLimit, cancellationToken)).Select(order => new object?[]
            {
                order.CreatedAtUtc,
                order.Id,
                order.SignalId,
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
            ["UpdatedAtUtc", "AssetId", "ConditionId", "Outcome", "SizeShares", "AveragePrice", "EstimatedValueUsd", "UnrealizedPnlUsd"],
            (await repository.GetPaperPositionsAsync(cancellationToken)).Select(position => new object?[]
            {
                position.UpdatedAtUtc,
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
