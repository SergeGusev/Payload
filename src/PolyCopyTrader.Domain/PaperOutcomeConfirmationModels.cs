namespace PolyCopyTrader.Domain;

public sealed record PaperOutcomeConfirmation(
    Guid PaperOrderId,
    string ConditionId,
    string AssetId,
    string Outcome,
    string WinningAssetId,
    string WinningOutcome,
    DateTimeOffset CheckedAtUtc,
    string EvidenceJson);

public sealed record PaperOutcomeConfirmationResult(bool Confirmed, bool Corrected, string Reason);
