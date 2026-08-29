namespace PolyCopyTrader.Storage;

public static class PostgresSchemaMigrationCatalog
{
    public const string LegacyBaselineId = "0001-legacy-baseline-a3b0457f";
    public const string LegacyBaselineParentRevision = "a3b0457fc113fc5ef482aabd5c090c3162045001";
    public const string ExpectedExistingServiceBuild = "a3b0457fc113fc5ef482aabd5c090c3162045001";
    public const string RequiredDataMigrationKey = "20260813_remove_paired_maker_gtd_first_accepting_strategies";

    public const string LegacyBaselineSemanticChecksum = "4dba8fe092057778ff146e61be43cabbb882358c679c8ee70ade832a872b00d2";

    public static IReadOnlyList<PostgresSchemaMigration> CreateDefault()
    {
        var baseline = new PostgresSchemaMigration(
            order: 0,
            id: LegacyBaselineId,
            sql: PostgresSchema.SchemaSql,
            transactional: false,
            details: $"legacy-baseline; parent={LegacyBaselineParentRevision}",
            isLegacyBaseline: true);

        if (!string.Equals(
                baseline.SemanticChecksum,
                LegacyBaselineSemanticChecksum,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Legacy PostgreSQL baseline checksum mismatch for migration '{LegacyBaselineId}'. " +
                $"Expected {LegacyBaselineSemanticChecksum}, actual {baseline.SemanticChecksum}. " +
                "The immutable legacy schema must not be edited; add a new ordered migration instead.");
        }

        var lossDiffStrategies = new PostgresSchemaMigration(
            order: 1,
            id: PostgresLossDiffStrategySchemaMigration.Id,
            sql: PostgresLossDiffStrategySchemaMigration.Sql,
            transactional: true,
            details: "seed two fixed ETH LossDiff children with zero-cutoff durable parent-outcome state");
        if (!string.Equals(
                lossDiffStrategies.SemanticChecksum,
                PostgresLossDiffStrategySchemaMigration.SemanticChecksum,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration checksum mismatch for '{PostgresLossDiffStrategySchemaMigration.Id}'. " +
                $"Expected {PostgresLossDiffStrategySchemaMigration.SemanticChecksum}, " +
                $"actual {lossDiffStrategies.SemanticChecksum}.");
        }

        var ethUp8LossDiffStrategies = new PostgresSchemaMigration(
            order: 2,
            id: PostgresEthUp8LossDiffStrategySchemaMigration.Id,
            sql: PostgresEthUp8LossDiffStrategySchemaMigration.Sql,
            transactional: true,
            details: "seed two fixed ETH Up 8 bps LossDiff children with zero-cutoff durable parent-outcome state");
        if (!string.Equals(
                ethUp8LossDiffStrategies.SemanticChecksum,
                PostgresEthUp8LossDiffStrategySchemaMigration.SemanticChecksum,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration checksum mismatch for '{PostgresEthUp8LossDiffStrategySchemaMigration.Id}'. " +
                $"Expected {PostgresEthUp8LossDiffStrategySchemaMigration.SemanticChecksum}, " +
                $"actual {ethUp8LossDiffStrategies.SemanticChecksum}.");
        }

        var signalsTraderWalletIndex = new PostgresSchemaMigration(
            order: 3,
            id: PostgresSignalsTraderWalletIndexSchemaMigration.Id,
            sql: PostgresSignalsTraderWalletIndexSchemaMigration.Sql,
            transactional: false,
            details: "create exact signals trader-wallet/id lookup index",
            completionCheckSql: PostgresSignalsTraderWalletIndexSchemaMigration.CompletionCheckSql);
        if (!string.Equals(
                signalsTraderWalletIndex.SemanticChecksum,
                PostgresSignalsTraderWalletIndexSchemaMigration.SemanticChecksum,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"PostgreSQL migration checksum mismatch for '{PostgresSignalsTraderWalletIndexSchemaMigration.Id}'. " +
                $"Expected {PostgresSignalsTraderWalletIndexSchemaMigration.SemanticChecksum}, " +
                $"actual {signalsTraderWalletIndex.SemanticChecksum}.");
        }

        return ValidateAndOrder(
            [baseline, lossDiffStrategies, ethUp8LossDiffStrategies, signalsTraderWalletIndex]);
    }

    public static IReadOnlyList<PostgresSchemaMigration> ValidateAndOrder(
        IEnumerable<PostgresSchemaMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var ordered = migrations.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0)
        {
            throw new InvalidOperationException("The PostgreSQL migration catalog is empty.");
        }

        var duplicateId = ordered
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate PostgreSQL migration id '{duplicateId.Key}'.");
        }

        var duplicateOrder = ordered
            .GroupBy(item => item.Order)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOrder is not null)
        {
            throw new InvalidOperationException(
                $"Duplicate PostgreSQL migration order '{duplicateOrder.Key}'.");
        }

        var baselines = ordered.Where(item => item.IsLegacyBaseline).ToArray();
        if (baselines.Length != 1 || !ReferenceEquals(ordered[0], baselines[0]))
        {
            throw new InvalidOperationException(
                "The PostgreSQL migration catalog must contain exactly one first legacy baseline.");
        }

        return ordered;
    }
}
