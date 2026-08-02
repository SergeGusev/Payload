using Npgsql;

namespace PolyCopyTrader.Storage;

internal readonly record struct PostgresPaperPositionsScanStats(
    long SequentialScans,
    long SequentialTuplesRead)
{
    public static PostgresPaperPositionsScanStats? Delta(
        PostgresPaperPositionsScanStats? before,
        PostgresPaperPositionsScanStats? after)
    {
        if (before is not PostgresPaperPositionsScanStats measuredBefore ||
            after is not PostgresPaperPositionsScanStats measuredAfter ||
            measuredAfter.SequentialScans < measuredBefore.SequentialScans ||
            measuredAfter.SequentialTuplesRead < measuredBefore.SequentialTuplesRead)
        {
            return null;
        }

        return new PostgresPaperPositionsScanStats(
            measuredAfter.SequentialScans - measuredBefore.SequentialScans,
            measuredAfter.SequentialTuplesRead - measuredBefore.SequentialTuplesRead);
    }
}

internal static class PostgresPaperPositionsScanTelemetry
{
    public static async Task<PostgresPaperPositionsScanStats?> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT seq_scan, seq_tup_read
FROM pg_catalog.pg_stat_xact_user_tables
WHERE relid = 'public.paper_positions'::regclass;
""",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PostgresPaperPositionsScanStats(reader.GetInt64(0), reader.GetInt64(1));
    }
}
