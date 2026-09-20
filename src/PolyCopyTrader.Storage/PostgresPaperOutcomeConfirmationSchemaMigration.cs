namespace PolyCopyTrader.Storage;

public static class PostgresPaperOutcomeConfirmationSchemaMigration
{
    public const string Id = "0010-paper-outcome-confirmation";
    public const string Sql = """
        ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS confirmed boolean NOT NULL DEFAULT false;
        ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS confirmation_next_attempt_at_utc timestamptz NOT NULL DEFAULT '-infinity';
        ALTER TABLE paper_orders ADD COLUMN IF NOT EXISTS confirmation_evidence jsonb;
        CREATE INDEX IF NOT EXISTS ix_paper_orders_unconfirmed
            ON paper_orders (confirmation_next_attempt_at_utc, created_at_utc, id) WHERE NOT confirmed;
        """;
}
