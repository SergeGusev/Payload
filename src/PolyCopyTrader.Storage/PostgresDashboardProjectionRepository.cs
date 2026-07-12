using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository(
    PostgresConnectionFactory connectionFactory) : IDashboardProjectionRepository
{
    internal const int CalculationVersion = DashboardProjectionVersions.Current;
    internal static readonly int[] WindowHours = [1, 6, 24];
    private const int WriteBatchSize = 250;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new UtcDateTimeOffsetJsonConverter() }
    };

    public async Task<DashboardProjectionControlState> GetControlStateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
SELECT initialized,
       calculation_version,
       status,
       reconciliation_cursor_strategy_id,
       bootstrap_started_at_utc,
       bootstrap_completed_at_utc,
       last_event_applied_at_utc,
       last_expiry_at_utc,
       last_reconciliation_at_utc,
       last_error
FROM dashboard_projection_control
WHERE singleton_id = 1;
""",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Dashboard projection control row is missing.");
        }

        return new DashboardProjectionControlState(
            reader.GetBoolean(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            ReadNullableUtc(reader, 4),
            ReadNullableUtc(reader, 5),
            ReadNullableUtc(reader, 6),
            ReadNullableUtc(reader, 7),
            ReadNullableUtc(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    public async Task RecordFailureAsync(
        string operation,
        string error,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET status = @Status,
    last_error = @Error,
    updated_at_utc = clock_timestamp()
WHERE singleton_id = 1;
""",
            connection);
        command.Parameters.AddWithValue("Status", $"{operation}Failed");
        command.Parameters.AddWithValue("Error", error);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task<IReadOnlyList<DashboardStrategyDescriptor>> ReadStrategyDescriptorsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? strategyId,
        CancellationToken cancellationToken)
    {
        var filter = strategyId is null ? string.Empty : "WHERE strategy.id = @StrategyId";
        await using var command = new NpgsqlCommand(
            $$"""
SELECT strategy.id,
       strategy.code,
       strategy.name,
       strategy.enabled,
       strategy.live_stakes,
       strategy.paused,
       strategy.paused_until_utc,
       strategy.paper_stake_amount,
       strategy.live_stake_amount,
       strategy.paper_lost_coeff,
       strategy.live_lost_coeff,
       strategy.paper_lost_counter,
       strategy.live_lost_counter,
       strategy.live_available_balance,
       strategy.live_enabled_at_utc
FROM strategies strategy
{{filter}}
ORDER BY strategy.id;
""",
            connection,
            transaction);
        if (strategyId is not null)
        {
            command.Parameters.AddWithValue("StrategyId", strategyId.Value);
        }

        var results = new List<DashboardStrategyDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new DashboardStrategyDescriptor(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                ReadNullableUtc(reader, 6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetInt32(11),
                reader.GetInt32(12),
                reader.GetDecimal(13),
                ReadNullableUtc(reader, 14)));
        }

        return results;
    }

    internal async Task<DashboardStrategyDescriptor?> ReadStrategyDescriptorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        return (await ReadStrategyDescriptorsAsync(
            connection,
            transaction,
            strategyId,
            cancellationToken)).SingleOrDefault();
    }

    internal async Task<DashboardLifetimeProjectionState?> ReadLifetimeStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT state_json::text,
       projection_version,
       last_event_id,
       last_reconciled_at_utc
FROM dashboard_strategy_lifetime_projection_states
WHERE strategy_id = @StrategyId
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var state = Deserialize<DashboardLifetimeProjectionState>(reader.GetString(0));
        state.ProjectionVersion = reader.GetInt64(1);
        state.LastEventId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        state.LastReconciledAtUtc = ReadNullableUtc(reader, 3);
        return state;
    }

    internal async Task<Dictionary<int, DashboardRecentProjectionState>> ReadRecentStatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT window_hours,
       state_json::text,
       projection_version,
       last_event_id,
       last_reconciled_at_utc
FROM dashboard_strategy_recent_projection_states
WHERE strategy_id = @StrategyId
ORDER BY window_hours
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        var results = new Dictionary<int, DashboardRecentProjectionState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var state = Deserialize<DashboardRecentProjectionState>(reader.GetString(1));
            state.ProjectionVersion = reader.GetInt64(2);
            state.LastEventId = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            state.LastReconciledAtUtc = ReadNullableUtc(reader, 4);
            results[reader.GetInt32(0)] = state;
        }

        return results;
    }

    internal async Task WriteProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyDictionary<Guid, DashboardStrategyDescriptor> strategies,
        IReadOnlyDictionary<Guid, DashboardLifetimeProjectionState> lifetimeStates,
        IReadOnlyDictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState> recentStates,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        var strategyIds = lifetimeStates.Keys.OrderBy(id => id).ToArray();
        foreach (var chunk in strategyIds.Chunk(WriteBatchSize))
        {
            await using var batch = new NpgsqlBatch(connection) { Transaction = transaction };
            foreach (var strategyId in chunk)
            {
                var strategy = strategies[strategyId];
                var state = lifetimeStates[strategyId];
                batch.BatchCommands.Add(CreateLifetimeStateUpsert(strategyId, state, refreshedAtUtc));
                batch.BatchCommands.Add(PostgresDashboardSnapshotRepository.CreateUpsertCommand(
                    DashboardProjectionCalculator.ToStrategyPerformance(strategy, state, refreshedAtUtc),
                    refreshedAtUtc));

                foreach (var windowHours in WindowHours)
                {
                    var recentState = recentStates[(strategyId, windowHours)];
                    batch.BatchCommands.Add(CreateRecentStateUpsert(
                        strategyId,
                        windowHours,
                        recentState,
                        refreshedAtUtc));
                    batch.BatchCommands.Add(PostgresDashboardSnapshotRepository.CreateUpsertCommand(
                        DashboardProjectionCalculator.ToStrategyRecentPerformance(
                            strategy,
                            recentState,
                            windowHours,
                            refreshedAtUtc),
                        refreshedAtUtc));
                }
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal async Task WriteRecentProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyDictionary<Guid, DashboardStrategyDescriptor> strategies,
        IReadOnlyDictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState> recentStates,
        DateTimeOffset refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        var strategyIds = recentStates.Keys
            .Select(key => key.StrategyId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        foreach (var chunk in strategyIds.Chunk(WriteBatchSize))
        {
            await using var batch = new NpgsqlBatch(connection) { Transaction = transaction };
            foreach (var strategyId in chunk)
            {
                if (!strategies.TryGetValue(strategyId, out var strategy))
                {
                    continue;
                }

                foreach (var windowHours in WindowHours)
                {
                    if (!recentStates.TryGetValue((strategyId, windowHours), out var recentState))
                    {
                        continue;
                    }

                    batch.BatchCommands.Add(CreateRecentStateUpsert(
                        strategyId,
                        windowHours,
                        recentState,
                        refreshedAtUtc));
                    batch.BatchCommands.Add(PostgresDashboardSnapshotRepository.CreateUpsertCommand(
                        DashboardProjectionCalculator.ToStrategyRecentPerformance(
                            strategy,
                            recentState,
                            windowHours,
                            refreshedAtUtc),
                        refreshedAtUtc));
                }
            }

            if (batch.BatchCommands.Count > 0)
            {
                await batch.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    internal static DashboardRecentProjectionFact PrepareFact(
        DashboardRecentProjectionFact fact,
        DateTimeOffset nowUtc)
    {
        var notFuture = fact.OccurredAtUtc <= nowUtc;
        return fact with
        {
            Applied1Hour = notFuture && fact.OccurredAtUtc >= nowUtc.AddHours(-1),
            Applied6Hours = notFuture && fact.OccurredAtUtc >= nowUtc.AddHours(-6),
            Applied24Hours = notFuture && fact.OccurredAtUtc >= nowUtc.AddHours(-24)
        };
    }

    internal static void ApplyFact(
        IDictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState> states,
        DashboardRecentProjectionFact fact,
        int sign,
        ISet<(Guid StrategyId, int WindowHours)>? candidateRebuilds = null)
    {
        ApplyFactToWindow(1, fact.Applied1Hour);
        ApplyFactToWindow(6, fact.Applied6Hours);
        ApplyFactToWindow(24, fact.Applied24Hours);
        return;

        void ApplyFactToWindow(int windowHours, bool applied)
        {
            if (!applied)
            {
                return;
            }

            var key = (fact.StrategyId, windowHours);
            if (!states.TryGetValue(key, out var state))
            {
                state = new DashboardRecentProjectionState();
                states[key] = state;
            }

            if (DashboardProjectionCalculator.Apply(state, fact.Contribution, sign))
            {
                candidateRebuilds?.Add(key);
            }
        }
    }

    internal static NpgsqlBatchCommand CreateLifetimeStateUpsert(
        Guid strategyId,
        DashboardLifetimeProjectionState state,
        DateTimeOffset updatedAtUtc)
    {
        var command = new NpgsqlBatchCommand(
            """
INSERT INTO dashboard_strategy_lifetime_projection_states (
    strategy_id, state_json, projection_version, last_event_id, last_reconciled_at_utc, updated_at_utc)
VALUES (
    @StrategyId, CAST(@StateJson AS jsonb), @ProjectionVersion, @LastEventId, @LastReconciledAtUtc, @UpdatedAtUtc)
ON CONFLICT (strategy_id) DO UPDATE SET
    state_json = EXCLUDED.state_json,
    projection_version = EXCLUDED.projection_version,
    last_event_id = EXCLUDED.last_event_id,
    last_reconciled_at_utc = EXCLUDED.last_reconciled_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;
""");
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("StateJson", Serialize(state));
        command.Parameters.AddWithValue("ProjectionVersion", state.ProjectionVersion);
        AddNullable(command, "LastEventId", state.LastEventId, NpgsqlDbType.Bigint);
        AddNullable(command, "LastReconciledAtUtc", state.LastReconciledAtUtc, NpgsqlDbType.TimestampTz);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
        return command;
    }

    internal static NpgsqlBatchCommand CreateRecentStateUpsert(
        Guid strategyId,
        int windowHours,
        DashboardRecentProjectionState state,
        DateTimeOffset updatedAtUtc)
    {
        var command = new NpgsqlBatchCommand(
            """
INSERT INTO dashboard_strategy_recent_projection_states (
    strategy_id, window_hours, state_json, projection_version, last_event_id, last_reconciled_at_utc, updated_at_utc)
VALUES (
    @StrategyId, @WindowHours, CAST(@StateJson AS jsonb), @ProjectionVersion, @LastEventId, @LastReconciledAtUtc, @UpdatedAtUtc)
ON CONFLICT (strategy_id, window_hours) DO UPDATE SET
    state_json = EXCLUDED.state_json,
    projection_version = EXCLUDED.projection_version,
    last_event_id = EXCLUDED.last_event_id,
    last_reconciled_at_utc = EXCLUDED.last_reconciled_at_utc,
    updated_at_utc = EXCLUDED.updated_at_utc;
""");
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("WindowHours", windowHours);
        command.Parameters.AddWithValue("StateJson", Serialize(state));
        command.Parameters.AddWithValue("ProjectionVersion", state.ProjectionVersion);
        AddNullable(command, "LastEventId", state.LastEventId, NpgsqlDbType.Bigint);
        AddNullable(command, "LastReconciledAtUtc", state.LastReconciledAtUtc, NpgsqlDbType.TimestampTz);
        command.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(updatedAtUtc));
        return command;
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.GetDateTimeOffset().ToUniversalTime();
        }

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToUniversalTime());
        }
    }

    internal static T Deserialize<T>(string json) where T : class =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name}.");

    internal static DateTimeOffset UtcNow(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    internal static DateTime UtcDateTime(DateTimeOffset value) => value.UtcDateTime;

    internal static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : UtcNow(reader.GetDateTime(ordinal));

    internal static void AddNullable(
        NpgsqlBatchCommand command,
        string name,
        object? value,
        NpgsqlDbType type)
    {
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    }

    internal static void AddNullable(
        NpgsqlCommand command,
        string name,
        object? value,
        NpgsqlDbType type)
    {
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    }

    internal sealed record ProjectionBuildResult(
        IReadOnlyDictionary<Guid, DashboardStrategyDescriptor> Strategies,
        IReadOnlyDictionary<Guid, DashboardLifetimeProjectionState> LifetimeStates,
        IReadOnlyDictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState> RecentStates,
        int RecentFactCount);
}
