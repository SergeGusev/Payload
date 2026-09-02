using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public async Task<int> CleanupCryptoReferencePriceTicksAsync(
        string assetSymbol,
        DateTimeOffset sampledBeforeUtc,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (assetSymbol is not ("BTC" or "ETH" or "SOL"))
        {
            throw new ArgumentException("Reference-price retention permits only exact BTC, ETH or SOL symbols.", nameof(assetSymbol));
        }

        if (batchSize <= 0)
        {
            return 0;
        }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var limits = new NpgsqlCommand(
            "SET LOCAL statement_timeout = '5s'; SET LOCAL lock_timeout = '250ms';", connection, transaction))
        {
            limits.CommandTimeout = 5;
            await limits.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
WITH selected AS MATERIALIZED (
    SELECT id
    FROM public.crypto_reference_price_ticks
    WHERE asset_symbol = @AssetSymbol
      AND sampled_at_utc < @SampledBeforeUtc
    ORDER BY sampled_at_utc ASC, id ASC
    LIMIT @BatchSize
    FOR UPDATE SKIP LOCKED
)
DELETE FROM public.crypto_reference_price_ticks ticks
USING selected
WHERE ticks.id = selected.id
  AND ticks.asset_symbol = @AssetSymbol
  AND ticks.sampled_at_utc < @SampledBeforeUtc;
""", connection, transaction);
        command.CommandTimeout = 5;
        command.Parameters.Add("AssetSymbol", NpgsqlDbType.Text).Value = assetSymbol;
        command.Parameters.Add("SampledBeforeUtc", NpgsqlDbType.TimestampTz).Value = UtcDateTime(sampledBeforeUtc);
        command.Parameters.Add("BatchSize", NpgsqlDbType.Integer).Value = Math.Min(batchSize, 1000);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }
}
