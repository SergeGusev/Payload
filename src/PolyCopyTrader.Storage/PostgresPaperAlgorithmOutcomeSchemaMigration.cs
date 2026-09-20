namespace PolyCopyTrader.Storage;

internal static class PostgresPaperAlgorithmOutcomeSchemaMigration
{
    public const string Id = "0016-paper-algorithm-outcomes";
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS paper_algorithm_outcomes (
            run_id uuid PRIMARY KEY REFERENCES strategy_market_paper_runs(id) ON DELETE CASCADE,
            strategy_id uuid NOT NULL REFERENCES strategies(id) ON DELETE CASCADE,
            market_id text NOT NULL, condition_id text NOT NULL, asset_id text NOT NULL, outcome text NOT NULL,
            winning_asset_id text NOT NULL, winning_outcome text NOT NULL, source text NOT NULL,
            evidence jsonb NOT NULL, observed_at_utc timestamptz NOT NULL,
            contribution integer NOT NULL CHECK(contribution IN (-1,1)),
            retired boolean NOT NULL DEFAULT false, reset_excluded boolean NOT NULL DEFAULT false
        );
        CREATE INDEX IF NOT EXISTS ix_paper_algorithm_outcomes_strategy_active
            ON paper_algorithm_outcomes(strategy_id) WHERE NOT retired AND NOT reset_excluded;
        """;
}
