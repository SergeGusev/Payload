namespace PolyCopyTrader.Storage;

public static class PostgresPaperConfirmationBatchSchemaMigration
{
    public const string Id = "0011-paper-confirmation-market-cache";
    public const string Sql = """
        CREATE TABLE paper_confirmation_markets (
            condition_id text PRIMARY KEY,
            evidence jsonb NOT NULL,
            checked_at_utc timestamptz NOT NULL
        );
        """;

    public const string IndexId = "0012-paper-confirmation-batch-indexes";
    public const string IndexSql = """
        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_confirmation_created
        ON paper_orders (created_at_utc, id) INCLUDE (confirmation_next_attempt_at_utc)
        WHERE NOT confirmed;
        """;
    public const string InventoryIndexId = "0013-paper-confirmation-inventory-index";
    public const string InventoryIndexSql = """
        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_confirmation_open_inventory
        ON paper_positions (copied_trader_wallet, asset_id) WHERE size_shares > 0;
        """;
    public const string IndexCheckSql = """
        SELECT count(*) = 1 FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid
        WHERE i.relnamespace='public'::regnamespace
          AND i.relname = 'ix_paper_confirmation_created'
          AND x.indisvalid AND x.indisready;
        """;
    public const string InventoryIndexCheckSql = """
        SELECT count(*)=1 FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid
        WHERE i.relnamespace='public'::regnamespace AND i.relname='ix_paper_confirmation_open_inventory'
          AND x.indisvalid AND x.indisready;
        """;
}
