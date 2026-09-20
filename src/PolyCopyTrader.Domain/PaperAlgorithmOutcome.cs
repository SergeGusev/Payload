namespace PolyCopyTrader.Domain;

/// <summary>Algorithm input only; never a payout, realized result or confirmation.</summary>
public sealed record PaperAlgorithmOutcome(Guid RunId, Guid StrategyId, string MarketId,
    string ConditionId, string AssetId, string Outcome, string WinningAssetId, string WinningOutcome,
    string Source, string EvidenceJson, DateTimeOffset ObservedAtUtc)
{
    public int Contribution => AssetId == WinningAssetId ? -1 : 1;
}

public sealed record FinalPaperRunSettlement(StrategyMarketPaperRun Run, PaperPosition? Position,
    PaperPositionSettlement? Settlement, FinalMarketOutcomeEvidence Evidence);
