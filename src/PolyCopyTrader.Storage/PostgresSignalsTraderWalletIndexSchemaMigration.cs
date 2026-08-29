namespace PolyCopyTrader.Storage;

public static class PostgresSignalsTraderWalletIndexSchemaMigration
{
    public const string Id = "0004-signals-trader-wallet-id-index";

    public const string SemanticChecksum = "86bc4907878ec4475afbc47fea9e5f760a86a9d2adf072db733116668c2bd164";

    public const string Sql = """
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_signals_trader_wallet_id
ON public.signals(trader_wallet, id);
""";

    public const string CompletionCheckSql = """
SELECT EXISTS (
    SELECT 1
    FROM pg_catalog.pg_class AS index_relation
    INNER JOIN pg_catalog.pg_namespace AS index_namespace
        ON index_namespace.oid = index_relation.relnamespace
    INNER JOIN pg_catalog.pg_index AS index_metadata
        ON index_metadata.indexrelid = index_relation.oid
    INNER JOIN pg_catalog.pg_class AS table_relation
        ON table_relation.oid = index_metadata.indrelid
    INNER JOIN pg_catalog.pg_namespace AS table_namespace
        ON table_namespace.oid = table_relation.relnamespace
    INNER JOIN pg_catalog.pg_am AS access_method
        ON access_method.oid = index_relation.relam
    INNER JOIN pg_catalog.pg_attribute AS trader_wallet_column
        ON trader_wallet_column.attrelid = table_relation.oid
       AND trader_wallet_column.attname = 'trader_wallet'
       AND trader_wallet_column.attnum > 0
       AND NOT trader_wallet_column.attisdropped
    INNER JOIN pg_catalog.pg_attribute AS id_column
        ON id_column.attrelid = table_relation.oid
       AND id_column.attname = 'id'
       AND id_column.attnum > 0
       AND NOT id_column.attisdropped
    WHERE index_namespace.nspname = 'public'
      AND index_relation.relname = 'ix_signals_trader_wallet_id'
      AND index_relation.relkind = 'i'
      AND table_namespace.nspname = 'public'
      AND table_relation.relname = 'signals'
      AND table_relation.relkind IN ('r', 'p')
      AND access_method.amname = 'btree'
      AND index_metadata.indisvalid
      AND index_metadata.indisready
      AND index_metadata.indislive
      AND NOT index_metadata.indisunique
      AND NOT index_metadata.indisprimary
      AND NOT index_metadata.indisexclusion
      AND index_metadata.indpred IS NULL
      AND index_metadata.indexprs IS NULL
      AND index_metadata.indnkeyatts = 2
      AND index_metadata.indnatts = 2
      AND index_metadata.indkey::text =
          concat(trader_wallet_column.attnum, ' ', id_column.attnum)
      AND pg_catalog.pg_get_indexdef(index_relation.oid) =
          'CREATE INDEX ix_signals_trader_wallet_id ON public.signals USING btree (trader_wallet, id)'
);
""";
}
