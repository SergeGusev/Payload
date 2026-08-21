using System.Security.Cryptography;
using System.Text;

namespace PolyCopyTrader.Storage;

public sealed record PostgresSchemaMigration
{
    public PostgresSchemaMigration(
        int order,
        string id,
        string sql,
        bool transactional,
        string details,
        bool isLegacyBaseline = false,
        string? completionCheckSql = null)
    {
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentException.ThrowIfNullOrWhiteSpace(details);

        if (!transactional && !isLegacyBaseline && string.IsNullOrWhiteSpace(completionCheckSql))
        {
            throw new ArgumentException(
                "A non-transactional migration requires an explicit completion check.",
                nameof(completionCheckSql));
        }

        Order = order;
        Id = id.Trim();
        Sql = NormalizeSql(sql);
        Transactional = transactional;
        Details = details.Trim();
        IsLegacyBaseline = isLegacyBaseline;
        CompletionCheckSql = string.IsNullOrWhiteSpace(completionCheckSql)
            ? null
            : completionCheckSql.Trim();
        SemanticChecksum = CalculateSemanticChecksum(Sql);
    }

    public int Order { get; }

    public string Id { get; }

    public string Sql { get; }

    public bool Transactional { get; }

    public string Details { get; }

    public bool IsLegacyBaseline { get; }

    public string? CompletionCheckSql { get; }

    public string SemanticChecksum { get; }

    public static string CalculateSemanticChecksum(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var bytes = Encoding.UTF8.GetBytes(NormalizeSql(sql));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string NormalizeSql(string sql)
    {
        return sql
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}

public sealed record AppliedPostgresSchemaMigration(
    string Id,
    string SemanticChecksum,
    DateTimeOffset AppliedAtUtc,
    string Details);

public sealed record PostgresLegacyBaselineState(
    int ApplicationObjectCount,
    bool HasExpectedHeartbeat,
    bool HasRequiredDataMigration,
    bool HasDashboardControlRelation,
    bool HasCopiedPerformanceQueueRelation,
    bool HasDashboardTerminalTrigger,
    bool HasCopiedPerformanceTerminalTrigger)
{
    public bool IsEmpty => ApplicationObjectCount == 0;

    public bool IsEligibleExistingDatabase =>
        ApplicationObjectCount > 0 &&
        HasExpectedHeartbeat &&
        HasRequiredDataMigration &&
        HasDashboardControlRelation &&
        HasCopiedPerformanceQueueRelation &&
        HasDashboardTerminalTrigger &&
        HasCopiedPerformanceTerminalTrigger;

    public string GetFailureDiagnostic()
    {
        return string.Join(
            "; ",
            $"application_objects={ApplicationObjectCount}",
            $"expected_heartbeat={HasExpectedHeartbeat}",
            $"required_data_migration={HasRequiredDataMigration}",
            $"dashboard_control_relation={HasDashboardControlRelation}",
            $"copied_performance_queue_relation={HasCopiedPerformanceQueueRelation}",
            $"dashboard_terminal_trigger={HasDashboardTerminalTrigger}",
            $"copied_performance_terminal_trigger={HasCopiedPerformanceTerminalTrigger}");
    }
}
