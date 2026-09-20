using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public Task<PaperConfirmationProgress?> GetPaperConfirmationProgressAsync(CancellationToken cancellationToken = default)
        => new PostgresDashboardSnapshotRepository(connectionFactory).GetPaperConfirmationProgressAsync(cancellationToken);
    public async Task<IReadOnlyList<PaperOrder>> ClaimPaperConfirmationBatchAsync(
        PaperConfirmationLane lane, DateTimeOffset nowUtc, int recentHours, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recentHours);
        if (limit is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var comparison = lane == PaperConfirmationLane.Recent ? ">=" : "<";
        await using var command = CreateCommand(connection, $"""
            WITH candidates AS (
                SELECT id FROM paper_orders
                WHERE NOT confirmed AND confirmation_next_attempt_at_utc<=@Now
                  AND created_at_utc {comparison} @Cutoff
                ORDER BY confirmation_next_attempt_at_utc,created_at_utc,id LIMIT @Limit FOR UPDATE SKIP LOCKED
            )
            UPDATE paper_orders SET confirmation_next_attempt_at_utc=@Now+interval '1 minute',
                confirmation_evidence=jsonb_build_object('last_attempt','lookup_started','attempted_at_utc',@Now::timestamptz)
            WHERE id IN (SELECT id FROM candidates)
            RETURNING {PaperOrderSelectColumns};
            """);
        command.Parameters.AddWithValue("Now", nowUtc.UtcDateTime);
        command.Parameters.AddWithValue("Cutoff", nowUtc.AddHours(-recentHours).UtcDateTime);
        command.Parameters.AddWithValue("Limit", limit);
        var orders = new List<PaperOrder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) orders.Add(ReadPaperOrder(reader));
        return orders.OrderBy(x => x.ConditionId, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
    }

    public async Task<PaperConfirmationMarketEvidence?> GetPaperConfirmationMarketAsync(
        string conditionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection,
            "SELECT evidence::text FROM paper_confirmation_markets WHERE condition_id=@Condition");
        command.Parameters.AddWithValue("Condition", conditionId.ToLowerInvariant());
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : JsonSerializer.Deserialize<PaperConfirmationMarketEvidence>(json)
            ?? throw new InvalidOperationException("Invalid final market evidence.");
    }

    public async Task<PaperConfirmationMarketEvidence> SavePaperConfirmationMarketAsync(
        PaperConfirmationMarketEvidence evidence, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection, """
            INSERT INTO paper_confirmation_markets(condition_id,evidence,checked_at_utc)
            VALUES(@Condition,@Evidence::jsonb,@Checked)
            ON CONFLICT(condition_id) DO NOTHING;
            SELECT evidence::text FROM paper_confirmation_markets WHERE condition_id=@Condition;
            """);
        command.Parameters.AddWithValue("Condition", evidence.ConditionId.ToLowerInvariant());
        command.Parameters.AddWithValue("Evidence", JsonSerializer.Serialize(evidence));
        command.Parameters.AddWithValue("Checked", evidence.CheckedAtUtc.UtcDateTime);
        return JsonSerializer.Deserialize<PaperConfirmationMarketEvidence>(
            (string)(await command.ExecuteScalarAsync(cancellationToken))!)
            ?? throw new InvalidOperationException("Invalid final market evidence.");
    }
}
