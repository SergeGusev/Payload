using Npgsql;
using NpgsqlTypes;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresDashboardProjectionRepository
{
    public async Task<DashboardProjectionBatchResult> ApplyPendingEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await TryAcquireProjectionLockAsync(connection, transaction, cancellationToken))
        {
            return new DashboardProjectionBatchResult(0, 0, 0, 0, 0);
        }

        var initialized = await IsProjectionInitializedAsync(connection, transaction, cancellationToken);
        if (!initialized)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DashboardProjectionBatchResult(0, 0, 0, 0, 0);
        }

        var (events, positionEventVersions) = await ReadPendingEventsAsync(connection, transaction, limit, cancellationToken);
        if (events.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new DashboardProjectionBatchResult(0, 0, 0, 0, 0);
        }

        var nowUtc = await ReadDatabaseNowAsync(connection, transaction, cancellationToken);
        var storedPositionFacts = await ReadPositionFactBatchAsync(
            connection,
            transaction,
            events
                .Where(projectionEvent =>
                    projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition)
                .Select(projectionEvent => projectionEvent.SourceId)
                .Distinct()
                .ToArray(),
            cancellationToken);
        var strategyIds = events
            .SelectMany(GetAffectedStrategyIds)
            .Concat(storedPositionFacts.Values.Select(fact => fact.StrategyId))
            .Distinct()
            .ToArray();
        var descriptors = await ReadStrategyDescriptorBatchAsync(
            connection,
            transaction,
            strategyIds,
            cancellationToken);
        var lifetimeStates = await ReadLifetimeStateBatchAsync(
            connection,
            transaction,
            strategyIds,
            cancellationToken);
        var recentStates = await ReadRecentStateBatchAsync(
            connection,
            transaction,
            strategyIds,
            cancellationToken);
        var storedFacts = await ReadFactBatchAsync(
            connection,
            transaction,
            events.Select(projectionEvent => projectionEvent.SourceId).Distinct().ToArray(),
            cancellationToken);

        var touchedStrategies = new HashSet<Guid>();
        var blockedStrategies = new HashSet<Guid>();
        var removedStrategies = new HashSet<Guid>();
        var processedEventIds = new List<long>(events.Count);
        var replacedSources = new HashSet<(string SourceKind, Guid SourceId)>();
        var factsToInsert = new Dictionary<
            (string SourceKind, Guid SourceId),
            IReadOnlyList<DashboardRecentProjectionFact>>();
        var candidateRebuilds = new HashSet<(Guid StrategyId, int WindowHours)>();
        var reconciliationRequests = new Dictionary<Guid, string>();
        var positionFactChanges = new Dictionary<Guid, PaperPositionProjectionPayload?>();
        var appliedEvents = 0;

        foreach (var projectionEvent in events)
        {
            PaperPositionProjectionPayload? storedPositionFact = null;
            if (projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition)
            {
                storedPositionFacts.TryGetValue(projectionEvent.SourceId, out storedPositionFact);
            }

            var eventStrategyIdValue = projectionEvent.StrategyId ?? storedPositionFact?.StrategyId;
            if (eventStrategyIdValue is null)
            {
                processedEventIds.Add(projectionEvent.Id);
                continue;
            }

            var eventStrategyId = eventStrategyIdValue.Value;
            if (!descriptors.ContainsKey(eventStrategyId))
            {
                if (removedStrategies.Add(eventStrategyId))
                {
                    await DeleteStrategyProjectionAsync(
                        connection,
                        transaction,
                        eventStrategyId,
                        cancellationToken);
                    lifetimeStates.Remove(eventStrategyId);
                    foreach (var windowHours in WindowHours)
                    {
                        recentStates.Remove((eventStrategyId, windowHours));
                    }
                }

                processedEventIds.Add(projectionEvent.Id);
                appliedEvents++;
                continue;
            }

            if (blockedStrategies.Contains(eventStrategyId))
            {
                continue;
            }

            if (projectionEvent.SourceKind == DashboardProjectionSourceKinds.Strategy)
            {
                var oldStrategy = DeserializeOptional<StrategyProjectionPayload>(projectionEvent.OldPayloadJson);
                var newStrategy = DeserializeOptional<StrategyProjectionPayload>(projectionEvent.NewPayloadJson);
                if (newStrategy is null)
                {
                    await DeleteStrategyProjectionAsync(
                        connection,
                        transaction,
                        eventStrategyId,
                        cancellationToken);
                    lifetimeStates.Remove(eventStrategyId);
                    foreach (var windowHours in WindowHours)
                    {
                        recentStates.Remove((eventStrategyId, windowHours));
                    }

                    processedEventIds.Add(projectionEvent.Id);
                    appliedEvents++;
                    continue;
                }

                if (oldStrategy is null || oldStrategy.LiveEnabledAtUtc != newStrategy.LiveEnabledAtUtc)
                {
                    reconciliationRequests[eventStrategyId] = oldStrategy is null
                        ? "strategy_created"
                        : "live_enabled_at_changed";
                    blockedStrategies.Add(eventStrategyId);
                    processedEventIds.Add(projectionEvent.Id);
                    continue;
                }

                if (lifetimeStates.ContainsKey(eventStrategyId))
                {
                    touchedStrategies.Add(eventStrategyId);
                    processedEventIds.Add(projectionEvent.Id);
                    appliedEvents++;
                }

                continue;
            }

            var oldSourceStrategyId = projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition
                ? storedPositionFact?.StrategyId ?? ReadPayloadStrategyId(
                    projectionEvent.SourceKind,
                    projectionEvent.OldPayloadJson)
                : ReadPayloadStrategyId(
                    projectionEvent.SourceKind,
                    projectionEvent.OldPayloadJson);
            var newSourceStrategyId = ReadPayloadStrategyId(
                projectionEvent.SourceKind,
                projectionEvent.NewPayloadJson);
            if (oldSourceStrategyId is not null &&
                newSourceStrategyId is not null &&
                oldSourceStrategyId != newSourceStrategyId)
            {
                QueueAffectedStrategy(oldSourceStrategyId.Value, "source_strategy_changed");
                QueueAffectedStrategy(newSourceStrategyId.Value, "source_strategy_changed");
                processedEventIds.Add(projectionEvent.Id);
                continue;
            }

            if (PaperOrderSideChanged(projectionEvent))
            {
                reconciliationRequests[eventStrategyId] = "paper_order_side_changed";
                blockedStrategies.Add(eventStrategyId);
                processedEventIds.Add(projectionEvent.Id);
                continue;
            }

            if (projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition &&
                storedPositionFact is null &&
                projectionEvent.OldPayloadJson is not null)
            {
                QueueAffectedStrategy(eventStrategyId, "position_fact_missing");
                processedEventIds.Add(projectionEvent.Id);
                continue;
            }

            if (!lifetimeStates.TryGetValue(eventStrategyId, out var lifetimeState) ||
                WindowHours.Any(window => !recentStates.ContainsKey((eventStrategyId, window))))
            {
                reconciliationRequests[eventStrategyId] = "projection_state_missing";
                blockedStrategies.Add(eventStrategyId);
                continue;
            }

            if (projectionEvent.Operation == DashboardProjectionOperations.Delete &&
                projectionEvent.SourceKind != DashboardProjectionSourceKinds.PaperPosition)
            {
                reconciliationRequests[eventStrategyId] = "source_deleted";
            }

            var oldLifetime = projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition
                ? storedPositionFact is null
                    ? null
                    : DashboardProjectionCalculator.GetLifetimeContribution(storedPositionFact)
                : GetLifetimeContribution(
                    projectionEvent.SourceKind,
                    projectionEvent.OldPayloadJson);
            var newLifetime = GetLifetimeContribution(
                projectionEvent.SourceKind,
                projectionEvent.NewPayloadJson);
            if (oldLifetime is not null &&
                DashboardProjectionCalculator.RequiresLifetimeCandidateRebuild(
                    lifetimeState,
                    oldLifetime,
                    newLifetime))
            {
                reconciliationRequests[eventStrategyId] = "lifetime_candidate_removed";
            }

            if (oldLifetime is not null)
            {
                DashboardProjectionCalculator.Apply(lifetimeState, oldLifetime, -1);
            }

            if (newLifetime is not null)
            {
                DashboardProjectionCalculator.Apply(lifetimeState, newLifetime, 1);
            }

            if (projectionEvent.SourceKind == DashboardProjectionSourceKinds.PaperPosition)
            {
                var newPositionFact = DeserializeOptional<PaperPositionProjectionPayload>(
                    projectionEvent.NewPayloadJson);
                positionFactChanges[projectionEvent.SourceId] = newPositionFact;
                if (newPositionFact is null)
                {
                    storedPositionFacts.Remove(projectionEvent.SourceId);
                }
                else
                {
                    storedPositionFacts[projectionEvent.SourceId] = newPositionFact;
                }
            }

            var sourceKey = (projectionEvent.SourceKind, projectionEvent.SourceId);
            var oldFacts = storedFacts.TryGetValue(sourceKey, out var existingFacts)
                ? existingFacts
                : [];
            var newFacts = GetRecentFacts(
                    projectionEvent.SourceKind,
                    projectionEvent.NewPayloadJson)
                .Where(fact => fact.OccurredAtUtc >= nowUtc.AddHours(-24))
                .Select(fact => PrepareFact(fact, nowUtc))
                .ToList();
            foreach (var windowHours in WindowHours)
            {
                var recentState = recentStates[(eventStrategyId, windowHours)];
                var oldWindowFacts = oldFacts.Where(fact => IsApplied(fact, windowHours)).ToArray();
                var newWindowFacts = newFacts.Where(fact => IsApplied(fact, windowHours)).ToArray();
                if (DashboardProjectionCalculator.RequiresRecentCandidateRebuild(
                    recentState,
                    oldWindowFacts,
                    newWindowFacts))
                {
                    candidateRebuilds.Add((eventStrategyId, windowHours));
                }
            }

            foreach (var oldFact in oldFacts)
            {
                ApplyFact(recentStates, oldFact, -1);
            }

            foreach (var newFact in newFacts)
            {
                ApplyFact(recentStates, newFact, 1);
            }

            storedFacts[sourceKey] = newFacts;
            replacedSources.Add(sourceKey);
            factsToInsert[sourceKey] = newFacts;
            lifetimeState.ProjectionVersion++;
            lifetimeState.LastEventId = projectionEvent.Id;
            foreach (var windowHours in WindowHours)
            {
                var state = recentStates[(eventStrategyId, windowHours)];
                state.ProjectionVersion++;
                state.LastEventId = projectionEvent.Id;
            }

            touchedStrategies.Add(eventStrategyId);
            processedEventIds.Add(projectionEvent.Id);
            appliedEvents++;

            void QueueAffectedStrategy(Guid strategyId, string reason)
            {
                if (!descriptors.ContainsKey(strategyId))
                {
                    return;
                }

                reconciliationRequests[strategyId] = reason;
                blockedStrategies.Add(strategyId);
            }
        }

        foreach (var request in reconciliationRequests)
        {
            await QueueReconciliationAsync(
                connection,
                transaction,
                request.Key,
                priority: 100,
                request.Value,
                cancellationToken);
        }

        await ReplaceFactsAsync(
            connection,
            transaction,
            replacedSources,
            factsToInsert.Values.SelectMany(sourceFacts => sourceFacts).ToArray(),
            nowUtc,
            cancellationToken);
        await ReplacePositionFactsAsync(
            connection,
            transaction,
            positionFactChanges,
            nowUtc,
            cancellationToken);
        if (candidateRebuilds.Count > 0)
        {
            await RebuildRecentCandidatesAsync(
                connection,
                transaction,
                recentStates,
                candidateRebuilds,
                cancellationToken);
        }

        var touchedDescriptors = descriptors
            .Where(pair => touchedStrategies.Contains(pair.Key))
            .ToDictionary();
        var touchedLifetime = lifetimeStates
            .Where(pair => touchedStrategies.Contains(pair.Key))
            .ToDictionary();
        var touchedRecent = recentStates
            .Where(pair => touchedStrategies.Contains(pair.Key.StrategyId))
            .ToDictionary();
        if (touchedStrategies.Count > 0)
        {
            await WriteProjectionAsync(
                connection,
                transaction,
                touchedDescriptors,
                touchedLifetime,
                touchedRecent,
                nowUtc,
                cancellationToken);
        }

        if (processedEventIds.Count > 0)
        {
            await DeleteProcessedEventsAsync(
                connection,
                transaction,
                processedEventIds,
                positionEventVersions,
                cancellationToken);
        }

        await UpdateEventControlAsync(connection, transaction, nowUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DashboardProjectionBatchResult(
            events.Count,
            appliedEvents,
            0,
            touchedStrategies.Count,
            reconciliationRequests.Count);
    }

    private static async Task<bool> IsProjectionInitializedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT initialized AND calculation_version = @CalculationVersion
FROM dashboard_projection_control
WHERE singleton_id = 1
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("CalculationVersion", CalculationVersion);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<(List<DashboardProjectionEvent> Events, Dictionary<long, string> PositionVersions)> ReadPendingEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int limit,
        CancellationToken cancellationToken)
    {
        // Bound the scan before taking any row locks. Seek past locked rows to
        // preserve SKIP LOCKED backfill, but never lock a row outside a window
        // that can still fit into this batch.
        await using var upperCommand = new NpgsqlCommand(
            "SELECT COALESCE(max(id), 0) FROM dashboard_projection_events;", connection, transaction);
        var upperId = (long)(await upperCommand.ExecuteScalarAsync(cancellationToken))!;
        var afterId = 0L;
        var results = new List<DashboardProjectionEvent>();
        var positionVersions = new Dictionary<long, string>();
        while (afterId < upperId && results.Count < limit)
        {
            await using var command = new NpgsqlCommand(
                """
WITH candidates AS MATERIALIZED (
    SELECT id, source_kind, source_id, strategy_id, operation,
           old_payload, new_payload, created_at_utc, xmin::text AS row_version
    FROM dashboard_projection_events
    WHERE id > @AfterId AND id <= @UpperId
    ORDER BY id
    LIMIT @Limit
), locked_events AS (
    SELECT pending.id, pending.source_kind, pending.source_id, pending.strategy_id, pending.operation,
           pending.old_payload, pending.new_payload, pending.created_at_utc, pending.xmin::text AS row_version
    FROM dashboard_projection_events pending
    INNER JOIN candidates candidate ON candidate.id = pending.id
    WHERE pending.source_kind <> 'PaperPosition'
    ORDER BY pending.id
    FOR UPDATE OF pending SKIP LOCKED
), eligible AS (
    SELECT * FROM locked_events
    UNION ALL
    SELECT * FROM candidates WHERE source_kind = 'PaperPosition'
)
SELECT eligible.id, eligible.source_kind, eligible.source_id, eligible.strategy_id, eligible.operation,
       eligible.old_payload::text, eligible.new_payload::text, eligible.created_at_utc,
       eligible.row_version, scanned.last_id
FROM eligible
RIGHT JOIN (SELECT max(id) AS last_id FROM candidates) scanned ON true
ORDER BY eligible.id;
""", connection, transaction);
            command.Parameters.AddWithValue("AfterId", afterId);
            command.Parameters.AddWithValue("UpperId", upperId);
            command.Parameters.AddWithValue("Limit", limit - results.Count);
            var lastId = afterId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(9))
                    lastId = reader.GetInt64(9);
                if (reader.IsDBNull(0))
                    continue;

                results.Add(new DashboardProjectionEvent(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    false,
                    UtcNow(reader.GetDateTime(7))));
                if (reader.GetString(1) == DashboardProjectionSourceKinds.PaperPosition)
                    positionVersions.Add(reader.GetInt64(0), reader.GetString(8));
            }
            if (lastId == afterId)
                break;
            afterId = lastId;
        }
        return (results, positionVersions);
    }

    private static async Task<Dictionary<Guid, DashboardStrategyDescriptor>> ReadStrategyDescriptorBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] strategyIds,
        CancellationToken cancellationToken)
    {
        if (strategyIds.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
SELECT id, code, name, enabled, live_stakes, paused, paused_until_utc,
       paper_stake_amount, live_stake_amount, paper_lost_coeff, live_lost_coeff,
       paper_lost_counter, live_lost_counter, live_available_balance, live_enabled_at_utc
FROM strategies
WHERE id = ANY(@StrategyIds);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyIds", strategyIds);
        var results = new Dictionary<Guid, DashboardStrategyDescriptor>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var descriptor = new DashboardStrategyDescriptor(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                reader.GetBoolean(4), reader.GetBoolean(5), ReadNullableUtc(reader, 6),
                reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
                reader.GetInt32(11), reader.GetInt32(12), reader.GetDecimal(13), ReadNullableUtc(reader, 14));
            results[descriptor.StrategyId] = descriptor;
        }

        return results;
    }

    private static async Task<Dictionary<Guid, DashboardLifetimeProjectionState>> ReadLifetimeStateBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] strategyIds,
        CancellationToken cancellationToken)
    {
        if (strategyIds.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
SELECT strategy_id, state_json::text, projection_version, last_event_id, last_reconciled_at_utc
FROM dashboard_strategy_lifetime_projection_states
WHERE strategy_id = ANY(@StrategyIds)
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyIds", strategyIds);
        var results = new Dictionary<Guid, DashboardLifetimeProjectionState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var state = Deserialize<DashboardLifetimeProjectionState>(reader.GetString(1));
            state.ProjectionVersion = reader.GetInt64(2);
            state.LastEventId = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            state.LastReconciledAtUtc = ReadNullableUtc(reader, 4);
            results[reader.GetGuid(0)] = state;
        }

        return results;
    }

    private static async Task<Dictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState>> ReadRecentStateBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] strategyIds,
        CancellationToken cancellationToken)
    {
        if (strategyIds.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
SELECT strategy_id, window_hours, state_json::text, projection_version, last_event_id, last_reconciled_at_utc
FROM dashboard_strategy_recent_projection_states
WHERE strategy_id = ANY(@StrategyIds)
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyIds", strategyIds);
        var results = new Dictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var state = Deserialize<DashboardRecentProjectionState>(reader.GetString(2));
            state.ProjectionVersion = reader.GetInt64(3);
            state.LastEventId = reader.IsDBNull(4) ? null : reader.GetInt64(4);
            state.LastReconciledAtUtc = ReadNullableUtc(reader, 5);
            results[(reader.GetGuid(0), reader.GetInt32(1))] = state;
        }

        return results;
    }

    private static async Task<Dictionary<(string SourceKind, Guid SourceId), List<DashboardRecentProjectionFact>>> ReadFactBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] sourceIds,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
SELECT source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
       contribution_json::text, applied_1h, applied_6h, applied_24h
FROM dashboard_strategy_recent_projection_facts
WHERE source_id = ANY(@SourceIds)
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("SourceIds", sourceIds);
        var results = new Dictionary<(string SourceKind, Guid SourceId), List<DashboardRecentProjectionFact>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetString(0), reader.GetGuid(1));
            if (!results.TryGetValue(key, out var facts))
            {
                facts = [];
                results[key] = facts;
            }

            facts.Add(new DashboardRecentProjectionFact(
                key.Item1,
                key.Item2,
                reader.GetString(2),
                reader.GetGuid(3),
                UtcNow(reader.GetDateTime(4)),
                Deserialize<DashboardRecentContribution>(reader.GetString(5)),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8)));
        }

        return results;
    }

    private static async Task<Dictionary<Guid, PaperPositionProjectionPayload>> ReadPositionFactBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid[] sourceIds,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Length == 0)
        {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
SELECT source_id, strategy_id, size_shares, unrealized_pnl_usd,
       average_price, fee_usd, fee_accounting_status, net_unrealized_pnl_usd
FROM dashboard_strategy_position_projection_facts
WHERE source_id = ANY(@SourceIds)
FOR UPDATE;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("SourceIds", sourceIds);
        var results = new Dictionary<Guid, PaperPositionProjectionPayload>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = new PaperPositionProjectionPayload(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetDecimal(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7));
            results[payload.Id] = payload;
        }

        return results;
    }

    private static DashboardLifetimeContribution? GetLifetimeContribution(string sourceKind, string? payloadJson)
    {
        if (payloadJson is null)
        {
            return null;
        }

        return sourceKind switch
        {
            DashboardProjectionSourceKinds.PaperOrder => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<PaperOrderProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.PaperFill => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<PaperFillProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.StrategyRun => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<StrategyRunProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.PaperPosition => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<PaperPositionProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.PaperSettlement => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<PaperSettlementProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.LiveOrder => DashboardProjectionCalculator.GetLifetimeContribution(
                Deserialize<LiveOrderProjectionPayload>(payloadJson)),
            _ => throw new InvalidOperationException($"Unsupported Dashboard projection source kind '{sourceKind}'.")
        };
    }

    private static IEnumerable<Guid> GetAffectedStrategyIds(DashboardProjectionEvent projectionEvent)
    {
        var ids = new HashSet<Guid>();
        if (projectionEvent.StrategyId is not null)
        {
            ids.Add(projectionEvent.StrategyId.Value);
        }

        var oldStrategyId = ReadPayloadStrategyId(
            projectionEvent.SourceKind,
            projectionEvent.OldPayloadJson);
        if (oldStrategyId is not null)
        {
            ids.Add(oldStrategyId.Value);
        }

        var newStrategyId = ReadPayloadStrategyId(
            projectionEvent.SourceKind,
            projectionEvent.NewPayloadJson);
        if (newStrategyId is not null)
        {
            ids.Add(newStrategyId.Value);
        }

        return ids;
    }

    private static Guid? ReadPayloadStrategyId(string sourceKind, string? payloadJson)
    {
        if (payloadJson is null)
        {
            return null;
        }

        return sourceKind switch
        {
            DashboardProjectionSourceKinds.Strategy =>
                Deserialize<StrategyProjectionPayload>(payloadJson).Id,
            DashboardProjectionSourceKinds.PaperOrder =>
                Deserialize<PaperOrderProjectionPayload>(payloadJson).StrategyId,
            DashboardProjectionSourceKinds.PaperFill =>
                Deserialize<PaperFillProjectionPayload>(payloadJson).StrategyId,
            DashboardProjectionSourceKinds.StrategyRun =>
                Deserialize<StrategyRunProjectionPayload>(payloadJson).StrategyId,
            DashboardProjectionSourceKinds.PaperPosition =>
                Deserialize<PaperPositionProjectionPayload>(payloadJson).StrategyId,
            DashboardProjectionSourceKinds.PaperSettlement =>
                Deserialize<PaperSettlementProjectionPayload>(payloadJson).StrategyId,
            DashboardProjectionSourceKinds.LiveOrder =>
                Deserialize<LiveOrderProjectionPayload>(payloadJson).StrategyId,
            _ => null
        };
    }

    private static bool PaperOrderSideChanged(DashboardProjectionEvent projectionEvent)
    {
        if (projectionEvent.SourceKind != DashboardProjectionSourceKinds.PaperOrder ||
            projectionEvent.OldPayloadJson is null ||
            projectionEvent.NewPayloadJson is null)
        {
            return false;
        }

        var oldPayload = Deserialize<PaperOrderProjectionPayload>(projectionEvent.OldPayloadJson);
        var newPayload = Deserialize<PaperOrderProjectionPayload>(projectionEvent.NewPayloadJson);
        return !oldPayload.Side.Equals(newPayload.Side, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DashboardRecentProjectionFact> GetRecentFacts(string sourceKind, string? payloadJson)
    {
        if (payloadJson is null)
        {
            return [];
        }

        return sourceKind switch
        {
            DashboardProjectionSourceKinds.PaperOrder => DashboardProjectionCalculator.GetRecentFacts(
                Deserialize<PaperOrderProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.PaperFill => DashboardProjectionCalculator.GetRecentFacts(
                Deserialize<PaperFillProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.StrategyRun => DashboardProjectionCalculator.GetRecentFacts(
                Deserialize<StrategyRunProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.LiveOrder => DashboardProjectionCalculator.GetRecentFacts(
                Deserialize<LiveOrderProjectionPayload>(payloadJson)),
            DashboardProjectionSourceKinds.PaperPosition or DashboardProjectionSourceKinds.PaperSettlement => [],
            _ => throw new InvalidOperationException($"Unsupported Dashboard projection source kind '{sourceKind}'.")
        };
    }

    private static T? DeserializeOptional<T>(string? json) where T : class =>
        json is null ? null : Deserialize<T>(json);

    private static bool IsApplied(DashboardRecentProjectionFact fact, int windowHours) => windowHours switch
    {
        1 => fact.Applied1Hour,
        6 => fact.Applied6Hours,
        24 => fact.Applied24Hours,
        _ => false
    };

    private static async Task ReplaceFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<(string SourceKind, Guid SourceId)> replacedSources,
        IReadOnlyCollection<DashboardRecentProjectionFact> facts,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (replacedSources.Count == 0)
        {
            return;
        }

        await using var batch = new NpgsqlBatch(connection) { Transaction = transaction };
        foreach (var source in replacedSources)
        {
            var delete = new NpgsqlBatchCommand(
                """
DELETE FROM dashboard_strategy_recent_projection_facts
WHERE source_kind = @SourceKind AND source_id = @SourceId;
""");
            delete.Parameters.AddWithValue("SourceKind", source.SourceKind);
            delete.Parameters.AddWithValue("SourceId", source.SourceId);
            batch.BatchCommands.Add(delete);
        }

        foreach (var fact in facts)
        {
            var insert = new NpgsqlBatchCommand(
                """
INSERT INTO dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id, occurred_at_utc, contribution_json,
    applied_1h, applied_6h, applied_24h, updated_at_utc)
VALUES (
    @SourceKind, @SourceId, @FactKind, @StrategyId, @OccurredAtUtc, CAST(@ContributionJson AS jsonb),
    @Applied1h, @Applied6h, @Applied24h, @UpdatedAtUtc);
""");
            insert.Parameters.AddWithValue("SourceKind", fact.SourceKind);
            insert.Parameters.AddWithValue("SourceId", fact.SourceId);
            insert.Parameters.AddWithValue("FactKind", fact.FactKind);
            insert.Parameters.AddWithValue("StrategyId", fact.StrategyId);
            insert.Parameters.AddWithValue("OccurredAtUtc", UtcDateTime(fact.OccurredAtUtc));
            insert.Parameters.AddWithValue("ContributionJson", Serialize(fact.Contribution));
            insert.Parameters.AddWithValue("Applied1h", fact.Applied1Hour);
            insert.Parameters.AddWithValue("Applied6h", fact.Applied6Hours);
            insert.Parameters.AddWithValue("Applied24h", fact.Applied24Hours);
            insert.Parameters.AddWithValue("UpdatedAtUtc", UtcDateTime(nowUtc));
            batch.BatchCommands.Add(insert);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReplacePositionFactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyDictionary<Guid, PaperPositionProjectionPayload?> changes,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM dashboard_strategy_position_projection_facts WHERE source_id = ANY(@SourceIds);",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("SourceIds", changes.Keys.ToArray());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        var inserts = changes.Values.OfType<PaperPositionProjectionPayload>().ToArray();
        if (inserts.Length == 0)
        {
            return;
        }

        await using var importer = await connection.BeginBinaryImportAsync(
            """
COPY dashboard_strategy_position_projection_facts (
    source_id, strategy_id, size_shares, unrealized_pnl_usd, average_price,
    fee_usd, fee_accounting_status, net_unrealized_pnl_usd, updated_at_utc)
FROM STDIN (FORMAT BINARY)
""",
            cancellationToken);
        foreach (var fact in inserts)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(fact.Id, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(fact.StrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(fact.SizeShares, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(fact.UnrealizedPnlUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(fact.AveragePrice, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(fact.FeeUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(fact.FeeAccountingStatus, NpgsqlDbType.Text, cancellationToken);
            if (fact.NetUnrealizedPnlUsd is null)
            {
                await importer.WriteNullAsync(cancellationToken);
            }
            else
            {
                await importer.WriteAsync(
                    fact.NetUnrealizedPnlUsd.Value,
                    NpgsqlDbType.Numeric,
                    cancellationToken);
            }
            await importer.WriteAsync(UtcDateTime(nowUtc), NpgsqlDbType.TimestampTz, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    internal static async Task RebuildRecentCandidatesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IDictionary<(Guid StrategyId, int WindowHours), DashboardRecentProjectionState> recentStates,
        IReadOnlySet<(Guid StrategyId, int WindowHours)> rebuildKeys,
        CancellationToken cancellationToken)
    {
        var strategyIds = rebuildKeys.Select(key => key.StrategyId).Distinct().ToArray();
        if (strategyIds.Length == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            """
SELECT strategy_id, contribution_json::text, applied_1h, applied_6h, applied_24h
FROM dashboard_strategy_recent_projection_facts
WHERE strategy_id = ANY(@StrategyIds);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyIds", strategyIds);
        var contributions = rebuildKeys.ToDictionary(
            key => key,
            _ => new List<DashboardRecentContribution>());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var strategyId = reader.GetGuid(0);
            var contribution = Deserialize<DashboardRecentContribution>(reader.GetString(1));
            Add(1, reader.GetBoolean(2));
            Add(6, reader.GetBoolean(3));
            Add(24, reader.GetBoolean(4));

            void Add(int windowHours, bool applied)
            {
                var key = (strategyId, windowHours);
                if (applied && contributions.TryGetValue(key, out var values))
                {
                    values.Add(contribution);
                }
            }
        }

        foreach (var pair in contributions)
        {
            if (recentStates.TryGetValue(pair.Key, out var state))
            {
                DashboardProjectionCalculator.RebuildRecentCandidates(state, pair.Value);
            }
        }
    }

    internal static async Task QueueReconciliationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        int priority,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO dashboard_projection_reconciliation_queue (
    strategy_id, priority, reason, requested_at_utc, next_attempt_at_utc)
VALUES (
    @StrategyId, @Priority, @Reason, clock_timestamp(), clock_timestamp())
ON CONFLICT (strategy_id) DO UPDATE SET
    priority = GREATEST(dashboard_projection_reconciliation_queue.priority, EXCLUDED.priority),
    reason = EXCLUDED.reason,
    requested_at_utc = LEAST(dashboard_projection_reconciliation_queue.requested_at_utc, EXCLUDED.requested_at_utc),
    next_attempt_at_utc = LEAST(dashboard_projection_reconciliation_queue.next_attempt_at_utc, EXCLUDED.next_attempt_at_utc);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("Priority", priority);
        command.Parameters.AddWithValue("Reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteStrategyProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken,
        bool deleteReconciliationRequest = true)
    {
        await using var command = new NpgsqlCommand(
            """
DELETE FROM dashboard_strategy_recent_projection_facts WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_strategy_position_projection_facts WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_strategy_recent_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_strategy_lifetime_projection_states WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_strategy_recent_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_strategy_performance_snapshots WHERE strategy_id = @StrategyId;
DELETE FROM dashboard_projection_reconciliation_queue WHERE strategy_id = @StrategyId AND @DeleteReconciliationRequest;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("DeleteReconciliationRequest", deleteReconciliationRequest);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteProcessedEventsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<long> eventIds,
        IReadOnlyDictionary<long, string> positionEventVersions,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
WITH acknowledged AS MATERIALIZED (
    SELECT pending.id
    FROM dashboard_projection_events AS pending
    WHERE pending.id = ANY(@EventIds)
      AND (pending.source_kind <> 'PaperPosition' OR EXISTS (
          SELECT 1 FROM unnest(@PositionEventIds, @PositionEventVersions) AS processed(id, row_version)
          WHERE processed.id = pending.id AND processed.row_version = pending.xmin::text))
    FOR UPDATE OF pending SKIP LOCKED
)
DELETE FROM dashboard_projection_events AS pending
USING acknowledged
WHERE pending.id = acknowledged.id;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("EventIds", eventIds.ToArray());
        // A producer can coalesce a new payload into the same ID during the
        // calculation. Only the version actually applied may be acknowledged.
        // Lock it only here, without waiting for producers. Skipped position
        // events replay against stored position facts in a later normal pass.
        var versions = positionEventVersions.ToArray();
        command.Parameters.AddWithValue("PositionEventIds", versions.Select(pair => pair.Key).ToArray());
        command.Parameters.AddWithValue("PositionEventVersions", versions.Select(pair => pair.Value).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DateTimeOffset> ReadDatabaseNowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT clock_timestamp();", connection, transaction);
        return UtcNow((DateTime)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Database clock query returned no value.")));
    }

    private static async Task UpdateEventControlAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE dashboard_projection_control
SET status = 'Running',
    last_event_applied_at_utc = @NowUtc,
    last_error = NULL,
    updated_at_utc = @NowUtc
WHERE singleton_id = 1;
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("NowUtc", UtcDateTime(nowUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
