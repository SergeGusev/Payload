using Npgsql;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    public async Task MarkPaperConfirmationProjectionUnknownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("UPDATE paper_confirmation_projection_state SET initialized=false;",connection)
                { CommandTimeout = 2 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (NpgsqlException) { /* Failure is reported by the caller; stale timestamp remains visible. */ }
    }
    // Separate, resumable projection: never changes the legacy bootstrap version.
    public async Task<int> ApplyPaperConfirmationProjectionAsync(int limit = 250,
        DateTimeOffset? capturedAtUtc = null, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var captured = (capturedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        await using var command = new NpgsqlCommand("""
            SET LOCAL lock_timeout='100ms'; SET LOCAL statement_timeout='2s';
            SELECT pg_try_advisory_xact_lock(hashtextextended('paper-confirmation-projection',0));
            """, connection, transaction);
        if (!(bool)(await command.ExecuteScalarAsync(cancellationToken))!) return 0;
        command.CommandText = "SELECT kind,cursor_id FROM paper_confirmation_projection_cursor WHERE NOT completed ORDER BY kind LIMIT 1 FOR UPDATE;";
        string? kind = null; var cursor = Guid.Empty;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken)) { kind = reader.GetString(0); cursor = reader.GetGuid(1); }
        command.Parameters.AddWithValue("Limit", limit);
        command.Parameters.AddWithValue("Captured", captured);
        if (kind is not null)
        {
            var table = kind switch { "O" => "paper_orders", "R" => "strategy_market_paper_runs",
                "S" => "paper_position_settlements", "F" => "paper_fills", _ => throw new InvalidOperationException() };
            command.Parameters.AddWithValue("Kind", kind);
            command.Parameters.AddWithValue("Cursor", cursor);
            command.CommandText = $"""
                WITH page AS MATERIALIZED (SELECT id FROM {table} WHERE id>@Cursor ORDER BY id LIMIT @Limit),
                queued AS (INSERT INTO paper_confirmation_projection_queue(kind,id,observed_confirmed)
                    SELECT @Kind,id,CASE WHEN @Kind='O' THEN false ELSE NULL END FROM page)
                UPDATE paper_confirmation_projection_cursor SET
                    cursor_id=COALESCE((SELECT id FROM page ORDER BY id DESC LIMIT 1),cursor_id),
                    completed=(SELECT count(*) FROM page)<@Limit WHERE kind=@Kind;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = """
            WITH page AS (SELECT sequence_id FROM paper_confirmation_projection_queue ORDER BY sequence_id LIMIT @Limit FOR UPDATE SKIP LOCKED),
            removed AS (DELETE FROM paper_confirmation_projection_queue q USING page p WHERE q.sequence_id=p.sequence_id
                RETURNING q.kind,q.id,q.observed_confirmed)
            SELECT paper_confirmation_projection_refresh(kind,id,@Captured,observed_confirmed)
                FROM (SELECT kind,id,bool_or(observed_confirmed) AS observed_confirmed FROM removed GROUP BY kind,id) batch;
            """;
        var processed = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) processed++;
        command.CommandText = """
            WITH page AS MATERIALIZED (SELECT kind,id FROM paper_confirmation_projection_members
                WHERE expires_at<=@Captured AND (pending OR expires_at<@Captured) ORDER BY expires_at LIMIT @Limit)
            SELECT paper_confirmation_projection_refresh(kind,id,@Captured) FROM (SELECT DISTINCT kind,id FROM page) expired;
            UPDATE paper_confirmation_projection_state SET refreshed_at=@Captured,
                initialized=initialized OR (NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_cursor WHERE NOT completed)
                    AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_queue)
                    AND NOT EXISTS(SELECT 1 FROM paper_confirmation_projection_members WHERE expires_at<=@Captured AND (pending OR expires_at<@Captured)));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return processed;
    }
}
