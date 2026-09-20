using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

public interface IPaperSettlementProcessor
{
    Task<PaperSettlementProcessingResult> ProcessOpenPositionsAsync(CancellationToken cancellationToken = default);

    Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(MarketDataUpdate update, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Final venue evidence is required.");

    Task<PaperSettlementProcessingResult> SettleMarketResolutionAsync(
        string? conditionId,
        string? assetId,
        string? winningAssetId,
        string? winningOutcome,
        string? category,
        string settlementSource,
        DateTimeOffset settledAtUtc,
        CancellationToken cancellationToken = default);
}

public sealed record PaperSettlementProcessingResult(
    int PositionsChecked,
    int PositionsSettled,
    int SettlementsInserted,
    int PerformanceRowsRefreshed);
