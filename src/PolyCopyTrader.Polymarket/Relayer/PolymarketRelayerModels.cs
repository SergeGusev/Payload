namespace PolyCopyTrader.Polymarket;

public sealed record PolymarketDepositWalletCall(
    string Target,
    string Value,
    string Data);

public sealed record PolymarketDepositWalletBatch(
    string OwnerAddress,
    string DepositWalletAddress,
    string Nonce,
    string Deadline,
    IReadOnlyList<PolymarketDepositWalletCall> Calls);

public sealed record PolymarketRelayerSubmissionResult(
    string TransactionId,
    string State,
    string? TransactionHash);
