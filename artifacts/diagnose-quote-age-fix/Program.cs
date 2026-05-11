using System.Globalization;
using Npgsql;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

var sinceUtc = args.Length > 0 && DateTimeOffset.TryParse(args[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
    ? parsed.ToUniversalTime()
    : DateTimeOffset.UtcNow.AddMinutes(-15);

var connectionFactory = new PostgresConnectionFactory(new StorageOptions());
await using var connection = connectionFactory.CreateConnection();
await connection.OpenAsync();

Console.WriteLine("utc_now=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
Console.WriteLine("since_utc=" + sinceUtc.ToString("O", CultureInfo.InvariantCulture));

await using var command = new NpgsqlCommand(
    """
    SELECT
        s.code,
        po.status,
        po.outcome,
        po.price,
        po.created_at_utc,
        COALESCE(po.raw_decision_json ->> 'source', '') AS source,
        COALESCE(po.raw_decision_json ->> 'quote_age_ms', '') AS quote_age_ms,
        COALESCE(po.raw_decision_json ->> 'decision_delay_ms', '') AS decision_delay_ms,
        COALESCE(po.raw_decision_json ->> 'entry_due_at_utc', '') AS entry_due_at_utc,
        COALESCE(po.raw_decision_json ->> 'cache_status', '') AS cache_status,
        COALESCE(po.raw_decision_json ->> 'rest_attempted', '') AS rest_attempted,
        COALESCE(po.raw_decision_json ->> 'cache_age_ms', '') AS cache_age_ms
    FROM paper_orders po
    INNER JOIN strategies s ON s.id = po.strategy_id
    WHERE s.code LIKE 'btc_up_down_5m_%'
      AND po.created_at_utc >= @SinceUtc
    ORDER BY po.created_at_utc DESC
    LIMIT 40;
    """,
    connection);
command.Parameters.AddWithValue("SinceUtc", sinceUtc);

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine(string.Join(
        " ",
        "order",
        "code=" + reader.GetString(0),
        "status=" + reader.GetString(1),
        "outcome=" + reader.GetString(2),
        "price=" + reader.GetDecimal(3).ToString("0.########", CultureInfo.InvariantCulture),
        "created=" + reader.GetFieldValue<DateTimeOffset>(4).ToString("O", CultureInfo.InvariantCulture),
        "source=" + Format(reader.GetString(5)),
        "quote_age_ms=" + Format(reader.GetString(6)),
        "decision_delay_ms=" + Format(reader.GetString(7)),
        "entry_due_at_utc=" + Format(reader.GetString(8)),
        "cache_status=" + Format(reader.GetString(9)),
        "rest_attempted=" + Format(reader.GetString(10)),
        "cache_age_ms=" + Format(reader.GetString(11))));
}

static string Format(string value)
{
    return string.IsNullOrWhiteSpace(value) ? "-" : value;
}
