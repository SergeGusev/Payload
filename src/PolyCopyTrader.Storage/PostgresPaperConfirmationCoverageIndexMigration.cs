namespace PolyCopyTrader.Storage;

public static class PostgresPaperConfirmationCoverageIndexMigration
{
    public const string Id = "0015-paper-confirmation-coverage-index";
    public const string Sql = """
        CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_paper_orders_confirmation_wallet_asset
        ON public.paper_orders (copied_trader_wallet, asset_id);
        """;
    public const string CompletionCheckSql = """
        SELECT EXISTS (
            SELECT 1 FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid
            WHERE i.relnamespace='public'::regnamespace
              AND i.relname='ix_paper_orders_confirmation_wallet_asset'
              AND x.indisvalid AND x.indisready
              AND pg_get_indexdef(x.indexrelid)=
                'CREATE INDEX ix_paper_orders_confirmation_wallet_asset ON public.paper_orders USING btree (copied_trader_wallet, asset_id)'
        );
        """;
}
