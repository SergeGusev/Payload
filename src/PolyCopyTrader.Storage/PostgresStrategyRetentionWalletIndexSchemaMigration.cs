namespace PolyCopyTrader.Storage;

public static class PostgresStrategyRetentionWalletIndexSchemaMigration
{
    public const string Id = "0009-strategy-retention-wallet-index";

    public const string SemanticChecksum = "aacc7783a6dc585df7f7e8e1980e2e6adccb8fe8913d5daf06028615efde6704";

    public const string Sql = """
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_strategies_retention_wallet_lookup
ON public.strategies USING hash (lower('strategy:' || code));
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
    INNER JOIN pg_catalog.pg_attribute AS code_column
        ON code_column.attrelid = table_relation.oid
       AND code_column.attname = 'code'
       AND code_column.attnum > 0
       AND NOT code_column.attisdropped
    INNER JOIN pg_catalog.pg_opclass AS operator_class
        ON operator_class.oid = index_metadata.indclass[0]
    INNER JOIN pg_catalog.pg_namespace AS operator_namespace
        ON operator_namespace.oid = operator_class.opcnamespace
    WHERE index_namespace.nspname = 'public'
      AND index_relation.relname = 'ix_strategies_retention_wallet_lookup'
      AND index_relation.relkind = 'i'
      AND table_namespace.nspname = 'public'
      AND table_relation.relname = 'strategies'
      AND table_relation.relkind = 'r'
      AND access_method.amname = 'hash'
      AND index_metadata.indisvalid
      AND index_metadata.indisready
      AND index_metadata.indislive
      AND NOT index_metadata.indisunique
      AND NOT index_metadata.indisprimary
      AND NOT index_metadata.indisexclusion
      AND index_metadata.indpred IS NULL
      AND index_metadata.indexprs IS NOT NULL
      AND index_metadata.indnkeyatts = 1
      AND index_metadata.indnatts = 1
      AND index_metadata.indkey::text = '0'
      AND index_metadata.indoption::text = '0'
      AND index_metadata.indcollation[0] = code_column.attcollation
      AND operator_class.opcmethod = access_method.oid
      AND operator_namespace.nspname = 'pg_catalog'
      AND operator_class.opcname = 'text_ops'
      AND operator_class.opcdefault
      AND pg_catalog.pg_get_indexdef(index_relation.oid) =
          'CREATE INDEX ix_strategies_retention_wallet_lookup ON public.strategies USING hash (lower((''strategy:''::text || code)))'
);
""";
}
