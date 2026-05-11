using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 1;
}

const string sql = """
select asset_symbol,
       count(*) as ticks,
       min(sampled_at_utc) as first_sample,
       max(sampled_at_utc) as last_sample,
       count(*) filter (where up_book_source = 'clob_rest' or down_book_source = 'clob_rest') as rest_backed
from crypto_up_down_5m_odds_ticks
where sampled_at_utc >= now() - interval '30 minutes'
group by asset_symbol
order by asset_symbol;
""";

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var command = new NpgsqlCommand(sql, connection);
await using var reader = await command.ExecuteReaderAsync();

var rows = 0;
while (await reader.ReadAsync())
{
    rows++;
    var asset = reader.GetString(0);
    var ticks = reader.GetInt64(1);
    var first = reader.GetFieldValue<DateTimeOffset>(2);
    var last = reader.GetFieldValue<DateTimeOffset>(3);
    var restBacked = reader.GetInt64(4);
    Console.WriteLine($"{asset} ticks={ticks} first_utc={first:u} last_utc={last:u} rest_backed={restBacked}");
}

if (rows == 0)
{
    Console.WriteLine("No crypto analytics ticks found in the last 30 minutes.");
}

return 0;
