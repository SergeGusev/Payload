namespace PolyCopyTrader.Polymarket;

public interface IPolymarketRelayerClient
{
    Task<PolymarketRelayerSubmissionResult> SubmitDepositWalletBatchAsync(
        string ownerAddress,
        string depositWalletAddress,
        IReadOnlyList<PolymarketDepositWalletCall> calls,
        string? metadata,
        CancellationToken cancellationToken = default);
}
