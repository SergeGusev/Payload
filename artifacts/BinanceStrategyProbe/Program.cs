using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_POSTGRES_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("POLYCOPYTRADER_POSTGRES_CONNECTION is not set.");
    return 1;
}

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await PrintQueryAsync(connection, "strategies", """
select id, code, name, enabled, live_stakes, paper_stake_amount, updated_at_utc
from strategies
where code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
order by code;
""");

await PrintQueryAsync(connection, "paper_orders_by_strategy_last_24h", """
select s.code,
       count(*) as orders_24h,
       max(po.created_at_utc) as last_order_utc,
       string_agg(po.status || ':' || status_count, ', ' order by po.status) as statuses
from strategies s
left join lateral (
    select status, count(*) as status_count, max(created_at_utc) as created_at_utc
    from paper_orders
    where strategy_id = s.id
      and created_at_utc >= now() - interval '24 hours'
    group by status
) po on true
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
group by s.code
order by s.code;
""");

await PrintQueryAsync(connection, "latest_paper_orders", """
select s.code,
       po.created_at_utc,
       po.status,
       po.outcome,
       po.price,
       po.size_shares,
       po.notional_usd,
       po.expires_at_utc,
       po.raw_decision_json::text as raw_decision_json
from paper_orders po
join strategies s on s.id = po.strategy_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
order by po.created_at_utc desc
limit 12;
""", maxTextLength: 650);

await PrintQueryAsync(connection, "runs_by_strategy_last_24h", """
select s.code,
       r.status,
       coalesce(r.skip_reason, '') as skip_reason,
       count(*) as runs,
       max(r.updated_at_utc) as last_updated_utc
from strategy_market_paper_runs r
join strategies s on s.id = r.strategy_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
  and r.created_at_utc >= now() - interval '24 hours'
group by s.code, r.status, coalesce(r.skip_reason, '')
order by s.code, runs desc, r.status, skip_reason;
""");

await PrintQueryAsync(connection, "latest_runs", """
select s.code,
       r.market_slug,
       r.detected_at_utc,
       r.entry_due_at_utc,
       r.status,
       coalesce(r.selected_outcome, '') as selected_outcome,
       r.entry_price,
       r.entered_at_utc,
       coalesce(r.skip_reason, '') as skip_reason,
       coalesce(r.skip_diagnostics_json::text, '') as skip_diagnostics_json
from strategy_market_paper_runs r
join strategies s on s.id = r.strategy_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
order by r.updated_at_utc desc
limit 20;
""", maxTextLength: 650);

await PrintQueryAsync(connection, "btc_odds_archive_health", """
select count(*) as ticks_30m,
       min(sampled_at_utc) as first_sample_utc,
       max(sampled_at_utc) as last_sample_utc,
       max(binance_fetched_at_utc) as last_binance_fetched_utc,
       max(binance_price_usd) as max_recorded_price
from btc_up_down_5m_odds_ticks
where sampled_at_utc >= now() - interval '30 minutes';
""");

await PrintQueryAsync(connection, "latest_odds_markets", """
select market_slug,
       market_id,
       min(sampled_at_utc) as first_sample_utc,
       max(sampled_at_utc) as last_sample_utc,
       min(market_start_utc) as market_start_utc,
       max(market_end_utc) as market_end_utc,
       min(binance_start_price_usd) as start_price,
       max(binance_price_usd) as max_price,
       min(binance_price_usd) as min_price,
       count(*) as ticks
from btc_up_down_5m_odds_ticks
where sampled_at_utc >= now() - interval '20 minutes'
group by market_slug, market_id
order by last_sample_utc desc
limit 12;
""");

await PrintQueryAsync(connection, "recent_strategy_runs_since_restart_window", """
select s.code,
       r.market_slug,
       r.market_id,
       r.entry_due_at_utc,
       r.status,
       coalesce(r.selected_outcome, '') as selected_outcome,
       r.entry_price,
       r.entered_at_utc,
       coalesce(r.skip_reason, '') as skip_reason,
       r.created_at_utc,
       r.updated_at_utc
from strategy_market_paper_runs r
join strategies s on s.id = r.strategy_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
  and r.updated_at_utc >= timestamp with time zone '2026-05-09T11:32:00Z'
order by r.updated_at_utc desc
limit 40;
""");

await PrintQueryAsync(connection, "strategy_runs_for_latest_odds_markets", """
with latest_odds as (
    select distinct market_slug, market_id
    from btc_up_down_5m_odds_ticks
    where sampled_at_utc >= now() - interval '20 minutes'
)
select o.market_slug as odds_market_slug,
       o.market_id as odds_market_id,
       s.code,
       r.market_slug as run_market_slug,
       r.market_id as run_market_id,
       r.entry_due_at_utc,
       r.status,
       coalesce(r.selected_outcome, '') as selected_outcome,
       r.entry_price,
       r.entered_at_utc,
       coalesce(r.skip_reason, '') as skip_reason,
       r.created_at_utc,
       r.updated_at_utc
from latest_odds o
cross join strategies s
left join strategy_market_paper_runs r on r.strategy_id = s.id
    and (r.market_slug = o.market_slug or r.market_id = o.market_id)
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
order by o.market_slug desc, s.code, r.updated_at_utc desc nulls last;
""");

await PrintQueryAsync(connection, "oldest_due_observed_runs", """
select s.code,
       r.market_slug,
       r.market_id,
       r.entry_due_at_utc,
       r.market_end_utc,
       r.status,
       r.created_at_utc,
       r.updated_at_utc
from strategy_market_paper_runs r
join strategies s on s.id = r.strategy_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
  and r.status = 'Observed'
  and r.entry_due_at_utc <= now()
order by r.entry_due_at_utc asc, r.detected_at_utc asc
limit 30;
""");

await PrintQueryAsync(connection, "oldest_due_observed_market_flags", """
select s.code,
       r.market_slug,
       r.entry_due_at_utc,
       r.market_end_utc,
       m.accepting_orders,
       m.enable_order_book,
       m.closed,
       m.archived,
       m.updated_at_utc as gamma_updated_at_utc
from strategy_market_paper_runs r
join strategies s on s.id = r.strategy_id
left join polymarket_gamma_markets m on m.market_id = r.market_id
where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
  and r.status = 'Observed'
  and r.entry_due_at_utc <= now()
order by r.entry_due_at_utc asc, r.detected_at_utc asc
limit 30;
""");

await PrintQueryAsync(connection, "recent_skipped_market_archive_first_tick", """
with recent_skips as (
    select distinct r.market_id, r.market_slug, r.entry_due_at_utc, r.skip_diagnostics_json
    from strategy_market_paper_runs r
    join strategies s on s.id = r.strategy_id
    where s.code in ('btc_up_down_5m_binance', 'btc_up_down_5m_binance_clever')
      and r.skip_reason = 'btc_reference_equal_market_start'
    order by r.entry_due_at_utc desc
    limit 6
)
select rs.market_slug,
       rs.entry_due_at_utc,
       first_tick.sampled_at_utc as first_tick_sampled_utc,
       first_tick.binance_price_usd as first_tick_price,
       first_tick.binance_start_price_usd as stored_start_price,
       rs.skip_diagnostics_json->>'btc_current_price_usd' as decision_current_price,
       rs.skip_diagnostics_json->>'btc_current_fetched_at_utc' as decision_current_fetched_utc
from recent_skips rs
left join lateral (
    select sampled_at_utc, binance_price_usd, binance_start_price_usd
    from btc_up_down_5m_odds_ticks t
    where t.market_id = rs.market_id
    order by sampled_at_utc asc
    limit 1
) first_tick on true
order by rs.entry_due_at_utc desc, rs.market_slug;
""", maxTextLength: 500);

await PrintQueryAsync(connection, "recent_binance_api_errors", """
select component, operation, created_at_utc, message
from api_errors
where created_at_utc >= now() - interval '24 hours'
  and component ilike '%Binance%'
order by created_at_utc desc
limit 20;
""", maxTextLength: 500);

return 0;

static async Task PrintQueryAsync(NpgsqlConnection connection, string title, string sql, int maxTextLength = 260)
{
    Console.WriteLine();
    Console.WriteLine("## " + title);
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var rows = 0;
    while (await reader.ReadAsync())
    {
        rows++;
        var values = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var value = reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            value = value.ReplaceLineEndings(" ");
            if (value.Length > maxTextLength)
            {
                value = value[..maxTextLength] + "...";
            }

            values.Add(name + "=" + value);
        }

        Console.WriteLine(string.Join(" | ", values));
    }

    if (rows == 0)
    {
        Console.WriteLine("(no rows)");
    }
}
