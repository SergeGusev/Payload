namespace PolyCopyTrader.Storage;

public static class PostgresHistoricalParityPaperRunOrderIndexSchemaMigration
{
    public const string Id = "0006-historical-parity-paper-run-order-index";

    public const string SemanticChecksum = "cf849b3359dea554f86f4f2a7d2d2ecbb5a6b1240f957f6e31d932a599db603a";

    public const string Sql = """
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_historical_parity_paper_run_order
ON public.historical_gross_net_parity_audit ((old_payload_json ->> 'paper_order_id'))
WHERE source_kind = 'PaperRun'
  AND operation_kind = 'AccountingDecision'
  AND calculation_version = 'historical-gross-net-parity-v1';
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
    WHERE index_namespace.nspname = 'public'
      AND index_relation.relname = 'ix_historical_parity_paper_run_order'
      AND index_relation.relkind = 'i'
      AND table_namespace.nspname = 'public'
      AND table_relation.relname = 'historical_gross_net_parity_audit'
      AND table_relation.relkind IN ('r', 'p')
      AND access_method.amname = 'btree'
      AND index_metadata.indisvalid
      AND index_metadata.indisready
      AND index_metadata.indislive
      AND NOT index_metadata.indisunique
      AND NOT index_metadata.indisprimary
      AND NOT index_metadata.indisexclusion
      AND index_metadata.indpred IS NOT NULL
      AND index_metadata.indexprs IS NOT NULL
      AND index_metadata.indnkeyatts = 1
      AND index_metadata.indnatts = 1
      AND index_metadata.indkey::text = '0'
      AND pg_catalog.pg_get_indexdef(index_relation.oid) =
          'CREATE INDEX ix_historical_parity_paper_run_order ON public.historical_gross_net_parity_audit USING btree (((old_payload_json ->> ''paper_order_id''::text))) WHERE ((source_kind = ''PaperRun''::text) AND (operation_kind = ''AccountingDecision''::text) AND (calculation_version = ''historical-gross-net-parity-v1''::text))'
);
""";
}
