namespace PolyCopyTrader.Polymarket.OnChain;

public interface IPolygonRpcClient
{
    Task<long> GetLatestBlockNumberAsync(CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetBlockTimestampAsync(long blockNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PolygonRpcLog>> GetLogsAsync(
        string contractAddress,
        string topic0,
        long fromBlock,
        long toBlock,
        CancellationToken cancellationToken = default);
}

public sealed record PolygonRpcLog(
    string Address,
    IReadOnlyList<string> Topics,
    string Data,
    long BlockNumber,
    string BlockHash,
    string TransactionHash,
    long TransactionIndex,
    long LogIndex,
    bool Removed);
