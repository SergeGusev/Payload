namespace PolyCopyTrader.Storage;

public static class PostgresHistoricalParityAuditTriggerSchemaMigration
{
    public const string Id = "0007-drop-historical-parity-audit-immutable-trigger";

    public const string SemanticChecksum = "b4df914154ed2cdf14c1bab3724be49c9118b1f73677fd5abbc06dfebe4bef74";

    public const string Sql = """
DROP TRIGGER IF EXISTS trg_historical_gross_net_parity_audit_immutable
ON public.historical_gross_net_parity_audit;
""";
}
