using System.Text;

namespace PolyCopyTrader.Storage;

public static class PostgresFollowMarketStrategySchemaMigration
{
    public const string Id = "0005-follow-market-strategies";

    public const string SemanticChecksum = "7adfb6e9831bb6a8a9ee3e15f33e91bd16fae4be822afc54433dd02b3739c7df";

    private static readonly int[] EntryDelaySeconds =
        [30, 60, 90, 120, 150, 180, 210, 240, 270];

    private static readonly int[] ThresholdCents =
        [50, 55, 60, 65, 70, 75, 80, 85, 90, 95];

    private static readonly AssetSeed[] Assets =
        [
            new("BTC", 8233),
            new("ETH", 8234),
            new("SOL", 8235)
        ];

    public static string Sql { get; } = BuildSql();

    private static string BuildSql()
    {
        var seeds = Assets
            .SelectMany(asset => EntryDelaySeconds.SelectMany(entryDelaySeconds =>
                ThresholdCents.Select(thresholdCents =>
                    CreateSeed(asset, entryDelaySeconds, thresholdCents))))
            .ToArray();
        var sql = new StringBuilder(seeds.Length * 600);
        sql.AppendLine("""
CREATE TEMP TABLE follow_market_strategy_seed (
    id uuid PRIMARY KEY,
    code text NOT NULL UNIQUE,
    name text NOT NULL,
    description text NOT NULL
) ON COMMIT DROP;

INSERT INTO follow_market_strategy_seed (id, code, name, description)
VALUES
""");

        for (var index = 0; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            sql.Append("    (")
                .Append(ToSqlLiteral(seed.Id.ToString("D")))
                .Append("::uuid, ")
                .Append(ToSqlLiteral(seed.Code))
                .Append(", ")
                .Append(ToSqlLiteral(seed.Name))
                .Append(", ")
                .Append(ToSqlLiteral(seed.Description))
                .AppendLine(index == seeds.Length - 1 ? ");" : "),");
        }

        sql.AppendLine("""

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM follow_market_strategy_seed seed
        JOIN public.strategies existing ON existing.id = seed.id
        WHERE existing.code <> seed.code
    ) OR EXISTS (
        SELECT 1
        FROM follow_market_strategy_seed seed
        JOIN public.strategies existing ON existing.code = seed.code
        WHERE existing.id <> seed.id
    ) THEN
        RAISE EXCEPTION 'Follow Market strategy identity collision; no strategy row was changed.';
    END IF;
END $$;

INSERT INTO public.strategies (
    id,
    code,
    name,
    description,
    enabled,
    live_stakes,
    auto_live_paused,
    auto_live_paused_at_utc,
    auto_live_pause_window_start_utc,
    paused,
    paused_until_utc,
    paper_stake_amount,
    live_stake_amount,
    paper_lost_coeff,
    live_lost_coeff,
    paper_lost_counter,
    live_lost_counter,
    live_available_balance,
    live_enabled_at_utc,
    created_at_utc,
    updated_at_utc
)
SELECT
    seed.id,
    seed.code,
    seed.name,
    seed.description,
    true,
    false,
    false,
    NULL,
    NULL,
    false,
    NULL,
    1.00,
    1.00,
    1.00,
    1.00,
    0,
    0,
    100.00,
    NULL,
    timestamp.now_utc,
    timestamp.now_utc
FROM follow_market_strategy_seed seed
CROSS JOIN (SELECT clock_timestamp() AS now_utc) timestamp
ON CONFLICT (id) DO UPDATE
SET code = EXCLUDED.code,
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    enabled = true,
    live_stakes = false,
    auto_live_paused = false,
    auto_live_paused_at_utc = NULL,
    auto_live_pause_window_start_utc = NULL,
    paused = false,
    paused_until_utc = NULL,
    live_enabled_at_utc = NULL,
    updated_at_utc = EXCLUDED.updated_at_utc;
""");

        return sql.ToString();
    }

    private static StrategySeed CreateSeed(
        AssetSeed asset,
        int entryDelaySeconds,
        int thresholdCents)
    {
        var assetCode = asset.Symbol.ToLowerInvariant();
        var idSuffix = (entryDelaySeconds * 1_000) + thresholdCents;
        return new StrategySeed(
            Guid.Parse($"b7c50005-0000-4000-{asset.IdGroup:0000}-{idSuffix:000000000000}"),
            $"{assetCode}_up_down_5m_follow_market_{entryDelaySeconds}_{thresholdCents}",
            $"{asset.Symbol} Follow Market {entryDelaySeconds} {thresholdCents}",
            $"{entryDelaySeconds} seconds after the {asset.Symbol} 5m market starts, compare the fresh immediately executable Up and Down best asks and select the unique higher-priced outcome. If that ask is at least {thresholdCents} cents, submit one minimum-size Paper BUY FAK intent capped at 0.99. Cumulative depth is not an entry gate; actual full, partial, or no-fill execution is retained and no retry is made.");
    }

    private static string ToSqlLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record AssetSeed(string Symbol, int IdGroup);

    private sealed record StrategySeed(Guid Id, string Code, string Name, string Description);
}
