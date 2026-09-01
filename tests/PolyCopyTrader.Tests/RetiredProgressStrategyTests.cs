using Npgsql;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class RetiredProgressStrategyTests
{
    private static readonly (Guid SourceId, string SourceCode, Guid OwnerId, string OwnerCode)[] LegacyBaselineRetainedLowerEnterLinks =
    [
        (
            Guid.Parse("b7c50005-0000-4000-8166-000000000003"),
            "btc_up_down_5m_3_diff_shift_progress_premarket",
            Guid.Parse("b7c50005-0001-4000-8166-000000000003"),
            "btc_up_down_5m_3_diff_shift_progress_lower_enter_premarket"),
        (
            Guid.Parse("b7c50005-0000-4000-8172-000000000004"),
            "btc_up_down_5m_4_diff_real_limit_progress_premarket",
            Guid.Parse("b7c50005-0001-4000-8172-000000000004"),
            "btc_up_down_5m_4_diff_real_limit_progress_lower_enter_premarket"),
        (
            Guid.Parse("b7c50005-0000-4000-8172-000000000005"),
            "btc_up_down_5m_5_diff_real_limit_progress_premarket",
            Guid.Parse("b7c50005-0001-4000-8172-000000000005"),
            "btc_up_down_5m_5_diff_real_limit_progress_lower_enter_premarket")
    ];

    private static readonly (Guid SourceId, string SourceCode, Guid OwnerId, string OwnerCode)[] RemovedLowerEnterLinks =
    [
        .. LegacyBaselineRetainedLowerEnterLinks,
        (
            Guid.Parse("b7c50005-0000-4000-8182-000000000101"),
            "btc_up_down_5m_futures_basis_bps_1_fak_premarket",
            Guid.Parse("b7c50005-0001-4000-8182-000000000101"),
            "btc_up_down_5m_futures_basis_bps_1_fak_lower_enter_premarket")
    ];

    [Fact]
    public void StrategyIds_ExcludeExactHopelessProgressAllowlist()
    {
        var retiredCodes = GetRetiredCodes();

        Assert.Equal(57, retiredCodes.Count);
        Assert.All(retiredCodes, code => Assert.Null(StrategyIds.TryGetStrategyIdByCode(code)));

        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_3_diff_shift_progress_premarket"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_4_diff_real_limit_progress_premarket"));
        Assert.Null(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_5_diff_real_limit_progress_premarket"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_15_child_progress"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("btc_up_down_5m_4_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("eth_up_down_5m_1_child_progress_roi"));
        Assert.NotNull(StrategyIds.TryGetStrategyIdByCode("sol_up_down_5m_9_child_progress_roi"));
    }

    [Fact]
    public void PostgresSchema_ExcludesRetiredProgressSeedsAndContainsExactCleanupMigration()
    {
        Assert.Contains("20260713_remove_hopeless_progress_strategies", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("allowlist=57", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("mode_code = 'child_progress'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("mode_code = 'child_progress_roi'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold_value IN (1, 2, 13, 14, 15, 16)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold_value IN (1, 2, 4, 5)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("WHERE asset_symbol <> 'BTC'", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("Refusing hopeless Progress cleanup because a strategy id/code collision was found.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("Refusing hopeless Progress cleanup because % active Live orders still exist.", PostgresSchema.SchemaSql, StringComparison.Ordinal);
    }

    [Fact]
    public void StrategyIds_ExcludeExact217AndExact15DisabledOrDependentTargets()
    {
        var targets = StrategyIds.NegativeProgressPurgeTargets;
        var targetIds = targets.Keys.ToHashSet();
        var disabledTargets = StrategyIds.DisabledAndDependentLowerEnterPurgeTargets;
        var disabledTargetIds = disabledTargets.Keys.ToHashSet();

        Assert.Equal(217, targets.Count);
        Assert.Equal(217, targets.Keys.Distinct().Count());
        Assert.Equal(217, targets.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(targets, target =>
        {
            Assert.StartsWith("b7c50005-", target.Key.ToString("D"), StringComparison.Ordinal);
            Assert.Contains("progress", target.Value, StringComparison.Ordinal);
            Assert.Null(StrategyIds.TryGetStrategyIdByCode(target.Value));
            Assert.DoesNotContain(StrategyIds.UpDown5mStrategyVariants, variant => variant.Id == target.Key);
        });

        Assert.Equal(15, disabledTargets.Count);
        Assert.Equal(15, disabledTargets.Keys.Distinct().Count());
        Assert.Equal(15, disabledTargets.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(disabledTargets, target =>
        {
            Assert.StartsWith("b7c50005-", target.Key.ToString("D"), StringComparison.Ordinal);
            Assert.Null(StrategyIds.TryGetStrategyIdByCode(target.Value));
            Assert.DoesNotContain(StrategyIds.UpDown5mStrategyVariants, variant => variant.Id == target.Key);
        });

        Assert.All(RemovedLowerEnterLinks, link =>
        {
            Assert.Contains(link.SourceId, disabledTargetIds);
            Assert.Contains(link.OwnerId, disabledTargetIds);
            Assert.DoesNotContain(StrategyIds.UpDown5mStrategyVariants, variant =>
                variant.Id == link.SourceId || variant.Id == link.OwnerId);
        });

        Assert.DoesNotContain(
            StrategyIds.UpDown5mStrategyVariants,
            retained =>
                (retained.BaseSignalStrategyId is { } baseId &&
                 (targetIds.Contains(baseId) || disabledTargetIds.Contains(baseId))) ||
                (retained.ConfirmationSignalStrategyId is { } confirmationId &&
                 (targetIds.Contains(confirmationId) || disabledTargetIds.Contains(confirmationId))) ||
                (retained.LowerEnterSourceStrategyId is { } lowerEnterId &&
                 (targetIds.Contains(lowerEnterId) || disabledTargetIds.Contains(lowerEnterId))));
    }

    [Fact]
    public void PostgresSchema_ExcludesExact217SeedsWithoutAutomaticCleanup()
    {
        Assert.DoesNotContain(
            "20260819_remove_217_unreferenced_negative_progress_strategies",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("allowlist=217", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DISABLE TRIGGER trg_historical_gross_net_parity_audit_immutable",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE TRIGGER trg_historical_gross_net_parity_audit_immutable",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "DROP TRIGGER IF EXISTS trg_historical_gross_net_parity_audit_immutable",
            PostgresHistoricalParityAuditTriggerSchemaMigration.Sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CREATE TRIGGER",
            PostgresHistoricalParityAuditTriggerSchemaMigration.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CREATE OR REPLACE FUNCTION public.reject_historical_gross_net_parity_immutable_change()",
            PostgresSchema.SchemaSql,
            StringComparison.Ordinal);
        Assert.Contains("lookback_hours IN (1, 2, 3, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24)", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("threshold_value BETWEEN 1 AND 19", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("asset_symbol IN ('ETH', 'SOL')", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("excluded.id <> 'b7c50005-0000-4000-8166-000000000003'::uuid", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8172-000000000004'::uuid", PostgresSchema.SchemaSql, StringComparison.Ordinal);
        Assert.Contains("'b7c50005-0000-4000-8172-000000000005'::uuid", PostgresSchema.SchemaSql, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "btc_up_down_5m_1_diff_real_limit_progress_lower_enter_premarket",
            StrategyIds.BtcLowerEnterPremarketVariants.Select(variant => variant.Code));
        Assert.DoesNotContain(
            "btc_up_down_5m_2_diff_real_limit_progress_lower_enter_premarket",
            StrategyIds.BtcLowerEnterPremarketVariants.Select(variant => variant.Code));
        Assert.DoesNotContain(
            "btc_up_down_5m_3_diff_real_limit_progress_lower_enter_premarket",
            StrategyIds.BtcLowerEnterPremarketVariants.Select(variant => variant.Code));
    }

    [Fact]
    public async Task PostgresSchema_InitializesWithoutRetiredProgressRows()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        await new PostgresSchemaInitializer(factory).InitializeAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
SELECT
    (SELECT count(*)::integer FROM strategies WHERE code = ANY(@RetiredCodes)) AS retired_count,
    (SELECT count(*)::integer FROM strategies WHERE code = ANY(@WatchCodes)) AS watch_count,
    (SELECT count(*)::integer FROM schema_data_migrations WHERE migration_key = '20260713_remove_hopeless_progress_strategies') AS migration_count,
    (SELECT details FROM schema_data_migrations WHERE migration_key = '20260713_remove_hopeless_progress_strategies') AS migration_details,
    (SELECT count(*)::integer FROM schema_data_migrations WHERE migration_key = '20260819_remove_217_unreferenced_negative_progress_strategies') AS negative_progress_migration_count;
""", connection);
        command.Parameters.AddWithValue("RetiredCodes", GetRetiredCodes().ToArray());
        command.Parameters.AddWithValue("WatchCodes", new[]
        {
            "btc_up_down_5m_3_diff_shift_progress_premarket",
            "btc_up_down_5m_4_diff_real_limit_progress_premarket",
            "btc_up_down_5m_5_diff_real_limit_progress_premarket"
        });

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(3, reader.GetInt32(1));
        Assert.Equal(1, reader.GetInt32(2));
        Assert.Contains("allowlist=57;target_strategies=0", reader.GetString(3), StringComparison.Ordinal);
        Assert.Equal(0, reader.GetInt32(4));
    }

    [Fact]
    public async Task NegativeProgressCatalogRelease_PreservesExistingTargetRowsAndHistoryAcrossInitialization()
    {
        var connectionString = Environment.GetEnvironmentVariable("POLYCOPYTRADER_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var factory = new PostgresConnectionFactory(new StorageOptions { ConnectionString = connectionString });
        var initializer = new PostgresSchemaInitializer(factory);
        await initializer.InitializeAsync();

        var localTargets = StrategyIds.NegativeProgressPurgeTargets
            .Where(target =>
                !target.Value.Contains("_child_progress", StringComparison.Ordinal) &&
                !target.Value.Contains("_lower_enter_", StringComparison.Ordinal))
            .OrderBy(target => target.Key)
            .ToArray();
        Assert.Equal(129, localTargets.Length);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal(0, await ReadTargetCountAsync(connection));
        var nonTargetCountBefore = await ReadNonTargetStrategyCountAsync(connection);
        var preservedBefore = await ReadPreservedSourcePayloadsAsync(connection);
        Assert.Equal(3, preservedBefore.Count);

        await InsertTargetStrategiesAsync(connection, localTargets);
        Assert.Equal(129, await ReadTargetCountAsync(connection));

        var signalId = Guid.Parse("31000000-0000-4000-8000-000000000010");
        var paperOrderId = Guid.Parse("31000000-0000-4000-8000-000000000011");
        var correlationId = Guid.Parse("31000000-0000-4000-8000-000000000012");
        await InsertSignalAsync(connection, signalId, localTargets[0].Value);
        await InsertPaperOrderAsync(
            connection, paperOrderId, signalId, localTargets[0], correlationId);
        await ExecuteAsync(connection, """
INSERT INTO live_orders (
    id, signal_id, strategy_id, status, side, asset_id, condition_id, outcome,
    price, size_shares, notional_usd, order_type, created_at_utc, expires_at_utc,
    response_status, filled_size, remaining_size, cancel_status, raw_response_json,
    validation_summary, average_fill_price, filled_notional_usd, cost_basis_usd,
    realized_pnl_usd, won, settlement_source, settled_at_utc, updated_at_utc)
VALUES (
    '31000000-0000-4000-8000-000000000009', @SignalId, @StrategyId,
    'Settled', 'BUY', 'asset-live', 'condition-live', 'Up',
    0.5, 2, 1, 'GTC', '2026-08-01T00:00:00Z', '2026-08-01T00:05:00Z',
    'filled', 2, 0, '', '{}'::jsonb, 'fixture', 0.5, 1, 1, -1, false, 'fixture',
    '2026-08-01T00:05:00Z', '2026-08-01T00:05:00Z');
""", new NpgsqlParameter("SignalId", signalId), new NpgsqlParameter("StrategyId", localTargets[0].Key));
        var fillId = Guid.Parse("31000000-0000-4000-8000-000000000014");
        var runId = Guid.Parse("31000000-0000-4000-8000-000000000015");
        var positionId = Guid.Parse("31000000-0000-4000-8000-000000000016");
        var settlementId = Guid.Parse("31000000-0000-4000-8000-000000000017");
        await ExecuteAsync(connection, """
INSERT INTO paper_fills (id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd)
VALUES (@Id, @OrderId, 0.5, 2, '2026-08-01T00:01:00Z', 'fixture', -1);
""", new NpgsqlParameter("Id", fillId), new NpgsqlParameter("OrderId", paperOrderId));
        await ExecuteAsync(connection, """
INSERT INTO strategy_market_paper_runs (
    id, strategy_id, market_id, condition_id, market_slug, market_title,
    detected_at_utc, entry_due_at_utc, status, selected_asset_id, selected_outcome,
    entry_price, stake_usd, size_shares, signal_id, paper_order_id, entered_at_utc,
    settlement_price, settlement_value_usd, realized_pnl_usd, settled_at_utc,
    created_at_utc, updated_at_utc)
VALUES (
    @Id, @StrategyId, 'market-run', 'condition-run', 'slug-run', 'Run fixture',
    '2026-08-01T00:00:00Z', '2026-08-01T00:00:30Z', 'Settled', 'asset-run', 'Up',
    0.5, 1, 2, @SignalId, @OrderId, '2026-08-01T00:00:30Z',
    0, 0, -1, '2026-08-01T00:05:00Z', '2026-08-01T00:00:00Z', '2026-08-01T00:05:00Z');
""",
            new NpgsqlParameter("Id", runId), new NpgsqlParameter("StrategyId", localTargets[0].Key),
            new NpgsqlParameter("SignalId", signalId), new NpgsqlParameter("OrderId", paperOrderId));
        await ExecuteAsync(connection, """
INSERT INTO paper_positions (
    id, copied_trader_wallet, asset_id, condition_id, outcome, size_shares,
    average_price, estimated_value_usd, unrealized_pnl_usd, updated_at_utc)
VALUES (@Id, @Wallet, 'asset-position', 'condition-position', 'Up', 1, 0.5, 0.4, -0.1, '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("Id", positionId), new NpgsqlParameter("Wallet", "strategy:" + localTargets[0].Value));
        await ExecuteAsync(connection, """
INSERT INTO paper_position_settlements (
    id, copied_trader_wallet, asset_id, condition_id, outcome, winning_outcome,
    settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
    realized_pnl_usd, won, settlement_source, settled_at_utc, created_at_utc)
VALUES (@Id, @Wallet, 'asset-settlement', 'condition-settlement', 'Up', 'Down',
    1, 0.5, 0.5, 0, -0.5, false, 'fixture', '2026-08-01T00:05:00Z', '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("Id", settlementId), new NpgsqlParameter("Wallet", "strategy:" + localTargets[0].Value));

        var endedAssignmentId = Guid.Parse("31000000-0000-4000-8000-000000000020");
        await ExecuteAsync(connection, """
INSERT INTO strategy_child_parent_assignments (
    id, child_strategy_id, parent_strategy_id, asset_symbol, lookback_hours,
    child_mode, parent_pnl_usd, parent_roi_pct, assigned_at_utc, ended_at_utc, updated_at_utc)
VALUES (
    @Id, 'b7c50005-0000-4000-8166-000000000003', @TargetId, 'BTC', 3,
    'Progress', -1, -1, '2026-07-31T00:00:00Z', '2026-08-01T00:00:00Z', '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("Id", endedAssignmentId), new NpgsqlParameter("TargetId", localTargets[1].Key));

        var v1RunId = Guid.Parse("31000000-0000-4000-8000-000000000021");
        await ExecuteAsync(connection, """
INSERT INTO strategy_market_paper_skip_tombstones (
    strategy_id, market_id, archived_run_id, archived_at_utc, archive_format_version,
    condition_id, market_slug, market_title, detected_at_utc, entry_due_at_utc,
    stake_usd, skip_reason, run_created_at_utc, run_updated_at_utc, rollup_bucket_start_utc)
VALUES (
    @StrategyId, 'market-v1', @RunId, '2026-08-01T01:00:00Z', 1,
    'condition-v1', 'slug-v1', 'V1 fixture', '2026-08-01T00:00:00Z', '2026-08-01T00:00:30Z',
    1, 'fixture_skip_v1', '2026-08-01T00:00:00Z', '2026-08-01T00:01:00Z', '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("StrategyId", localTargets[1].Key), new NpgsqlParameter("RunId", v1RunId));
        await ExecuteAsync(connection, """
WITH inserted_identity AS (
    INSERT INTO strategy_skip_archive_market_identities (market_id)
    VALUES ('market-v2')
    ON CONFLICT (market_id) DO NOTHING
    RETURNING market_identity_id
), identity AS (
    SELECT market_identity_id FROM inserted_identity
    UNION ALL
    SELECT market_identity_id
    FROM strategy_skip_archive_market_identities
    WHERE market_id = 'market-v2'
    LIMIT 1
), inserted_metadata AS (
    INSERT INTO strategy_skip_archive_market_metadata_versions (
        market_identity_id, condition_id, market_slug, market_title)
    SELECT market_identity_id, 'condition-v2', 'slug-v2', 'V2 fixture' FROM identity
    ON CONFLICT (
        market_identity_id,
        condition_id,
        market_slug,
        market_title,
        category,
        market_start_utc,
        market_end_utc)
    DO NOTHING
    RETURNING metadata_version_id, market_identity_id
), metadata AS (
    SELECT metadata_version_id, market_identity_id FROM inserted_metadata
    UNION ALL
    SELECT version.metadata_version_id, version.market_identity_id
    FROM strategy_skip_archive_market_metadata_versions version
    JOIN identity ON identity.market_identity_id = version.market_identity_id
    WHERE version.condition_id = 'condition-v2'
      AND version.market_slug = 'slug-v2'
      AND version.market_title = 'V2 fixture'
      AND version.category IS NULL
      AND version.market_start_utc IS NULL
      AND version.market_end_utc IS NULL
    LIMIT 1
), inserted_reason AS (
    INSERT INTO strategy_skip_archive_reasons (skip_reason)
    VALUES ('fixture_skip_v2')
    ON CONFLICT (skip_reason) DO NOTHING
    RETURNING skip_reason_id
), reason AS (
    SELECT skip_reason_id FROM inserted_reason
    UNION ALL
    SELECT skip_reason_id
    FROM strategy_skip_archive_reasons
    WHERE skip_reason = 'fixture_skip_v2'
    LIMIT 1
)
INSERT INTO strategy_market_paper_skip_tombstones_v2 (
    strategy_id, market_identity_id, metadata_version_id, archived_run_id,
    detected_at_utc, entry_due_at_utc, stake_usd, skip_reason_id, run_updated_at_utc)
SELECT @StrategyId, metadata.market_identity_id, metadata.metadata_version_id, @RunId,
       '2026-08-01T00:00:00Z', '2026-08-01T00:00:30Z', 1,
       reason.skip_reason_id, '2026-08-01T00:02:00Z'
FROM metadata CROSS JOIN reason;
""", new NpgsqlParameter("StrategyId", localTargets[2].Key),
            new NpgsqlParameter("RunId", Guid.Parse("31000000-0000-4000-8000-000000000022")));
        await ExecuteAsync(connection, """
INSERT INTO strategy_paper_skip_rollups (
    strategy_id, bucket_start_utc, skip_reason, run_count,
    first_updated_at_utc, last_updated_at_utc, created_at_utc, updated_at_utc)
VALUES
    (@V1StrategyId, '2026-08-01T00:00:00Z', 'fixture_skip_v1', 1,
     '2026-08-01T00:01:00Z', '2026-08-01T00:01:00Z', '2026-08-01T00:01:00Z', '2026-08-01T00:01:00Z'),
    (@V2StrategyId, '2026-08-01T00:00:00Z', 'fixture_skip_v2', 1,
     '2026-08-01T00:02:00Z', '2026-08-01T00:02:00Z', '2026-08-01T00:02:00Z', '2026-08-01T00:02:00Z');
""", new NpgsqlParameter("V1StrategyId", localTargets[1].Key), new NpgsqlParameter("V2StrategyId", localTargets[2].Key));

        await ExecuteAsync(connection, """
INSERT INTO historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, old_payload_json, new_payload_json, evidence_payload_json)
VALUES (
    '31000000-0000-4000-8000-000000000023', 'PaperOrder', @OrderId, @StrategyId,
    'fixture-v1', 'AccountingBaseline', 'fixture-v1', '{}'::jsonb, '{}'::jsonb, '{}'::jsonb);
""", new NpgsqlParameter("OrderId", paperOrderId), new NpgsqlParameter("StrategyId", localTargets[0].Key));
        await ExecuteAsync(connection, """
INSERT INTO paper_live_shadow_decisions (
    correlation_id, strategy_id, market_id, condition_id, asset_id, outcome, side,
    limit_price, target_notional_usd, requested_size_shares, max_reserved_notional_usd,
    order_type, post_only, order_book_snapshot_json, source, quote_received_at_utc,
    decision_created_at_utc, submit_deadline_utc, cancel_deadline_utc,
    signal_id, paper_order_id, status, updated_at_utc)
VALUES (
    @CorrelationId, @StrategyId, 'market-shadow', 'condition-shadow', 'asset-shadow', 'Up', 'BUY',
    0.5, 1, 2, 1, 'FAK', false, '{}'::jsonb, 'fixture', '2026-08-01T00:00:00Z',
    '2026-08-01T00:00:00Z', '2026-08-01T00:01:00Z', '2026-08-01T00:02:00Z',
    @SignalId, @OrderId, 'Completed', '2026-08-01T00:02:00Z');
""", new NpgsqlParameter("CorrelationId", correlationId), new NpgsqlParameter("StrategyId", localTargets[0].Key),
            new NpgsqlParameter("SignalId", signalId), new NpgsqlParameter("OrderId", paperOrderId));
        await ExecuteAsync(connection, """
INSERT INTO paper_live_shadow_discrepancies (
    id, correlation_id, strategy_id, classification, severity, details, raw_json, created_at_utc)
VALUES (
    '31000000-0000-4000-8000-000000000024', @CorrelationId, @StrategyId,
    'fixture', 'Info', 'target history', '{}'::jsonb, '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("CorrelationId", correlationId), new NpgsqlParameter("StrategyId", localTargets[0].Key));
        await ExecuteAsync(connection, """
INSERT INTO polymarket_onchain_paper_signal_results (
    id, capture_id, transaction_hash, log_index, participant_role,
    copied_trader_wallet, counterparty_wallet, side, token_id, condition_id,
    market_slug, outcome, signal_id, paper_order_id, status, decision_code,
    reason_details, processed_at_utc)
VALUES (
    '31000000-0000-4000-8000-000000000025', '31000000-0000-4000-8000-000000000026',
    'fixture-tx', 1, 'maker', @Wallet, 'counterparty', 'BUY', 'asset-onchain',
    'condition-onchain', 'slug-onchain', 'Up', @SignalId, @OrderId,
    'Processed', 'fixture', 'target history', '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("Wallet", "strategy:" + localTargets[0].Value),
            new NpgsqlParameter("SignalId", signalId), new NpgsqlParameter("OrderId", paperOrderId));
        await ExecuteAsync(connection, """
INSERT INTO dashboard_strategy_lifetime_projection_states (
    strategy_id, state_json, updated_at_utc)
VALUES (@StrategyId, '{}'::jsonb, '2026-08-01T00:00:00Z');
INSERT INTO dashboard_strategy_recent_projection_states (
    strategy_id, window_hours, state_json, updated_at_utc)
VALUES (@StrategyId, 24, '{}'::jsonb, '2026-08-01T00:00:00Z');
INSERT INTO dashboard_strategy_recent_projection_facts (
    source_kind, source_id, fact_kind, strategy_id, occurred_at_utc,
    contribution_json, applied_1h, applied_6h, applied_24h, updated_at_utc)
VALUES ('Fixture', @RunId, 'Fixture', @StrategyId, '2026-08-01T00:00:00Z',
        '{}'::jsonb, false, false, true, '2026-08-01T00:00:00Z');
INSERT INTO dashboard_projection_reconciliation_queue (strategy_id, reason)
VALUES (@StrategyId, 'fixture')
ON CONFLICT (strategy_id) DO UPDATE SET reason = EXCLUDED.reason;
""", new NpgsqlParameter("StrategyId", localTargets[0].Key), new NpgsqlParameter("RunId", runId));

        var targetIds = localTargets.Select(target => target.Key).ToArray();
        var targetWallets = localTargets.Select(target => "strategy:" + target.Value).ToArray();
        var historyBefore = await ReadTargetHistoryFingerprintAsync(connection, targetIds, targetWallets);
        Assert.True(historyBefore.RowCount >= 147, $"Unexpectedly small target fixture: {historyBefore}");

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        var historyAfter = await ReadTargetHistoryFingerprintAsync(connection, targetIds, targetWallets);
        Assert.Equal(historyBefore, historyAfter);
        Assert.Equal(129, await ReadTargetCountAsync(connection));
        Assert.Equal(nonTargetCountBefore, await ReadNonTargetStrategyCountAsync(connection));
        Assert.Equal(preservedBefore, await ReadPreservedSourcePayloadsAsync(connection));
        Assert.Equal(0, await ExecuteScalarAsync<int>(connection, """
SELECT count(*)::integer
FROM schema_data_migrations
WHERE migration_key = '20260819_remove_217_unreferenced_negative_progress_strategies';
"""));
    }

    private static async Task InsertTargetStrategiesAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<KeyValuePair<Guid, string>> targets)
    {
        await using var command = new NpgsqlCommand("""
INSERT INTO strategies (
    id, code, name, description, enabled, live_stakes, paused,
    created_at_utc, updated_at_utc)
SELECT fixture.id, fixture.code, 'Purge fixture ' || fixture.code, 'fixture',
       false, false, true, '2026-08-01T00:00:00Z', '2026-08-01T00:00:00Z'
FROM unnest(@Ids, @Codes) AS fixture(id, code);
""", connection);
        command.Parameters.AddWithValue("Ids", targets.Select(target => target.Key).ToArray());
        command.Parameters.AddWithValue("Codes", targets.Select(target => target.Value).ToArray());
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSignalAsync(
        NpgsqlConnection connection,
        Guid signalId,
        string strategyCode)
    {
        await ExecuteAsync(connection, """
INSERT INTO signals (
    id, trader_wallet, condition_id, asset_id, outcome, leader_price,
    score, accepted, decision, created_at_utc)
VALUES (@Id, @Wallet, 'condition-signal', 'asset-signal', 'Up', 0.5,
        1, true, 'fixture', '2026-08-01T00:00:00Z');
""", new NpgsqlParameter("Id", signalId), new NpgsqlParameter("Wallet", "strategy:" + strategyCode));
    }

    private static async Task InsertPaperOrderAsync(
        NpgsqlConnection connection,
        Guid orderId,
        Guid signalId,
        KeyValuePair<Guid, string> strategy,
        Guid correlationId)
    {
        await ExecuteAsync(connection, """
INSERT INTO paper_orders (
    id, signal_id, strategy_id, copied_trader_wallet, status, side,
    asset_id, condition_id, outcome, price, size_shares, notional_usd,
    created_at_utc, expires_at_utc, filled_at_utc, correlation_id, execution_source)
VALUES (
    @Id, @SignalId, @StrategyId, @Wallet, 'Filled', 'BUY',
    'asset-order', 'condition-order', 'Up', 0.5, 2, 1,
    '2026-08-01T00:00:00Z', '2026-08-01T00:05:00Z',
    '2026-08-01T00:01:00Z', @CorrelationId, 'fixture');
""", new NpgsqlParameter("Id", orderId), new NpgsqlParameter("SignalId", signalId),
            new NpgsqlParameter("StrategyId", strategy.Key), new NpgsqlParameter("Wallet", "strategy:" + strategy.Value),
            new NpgsqlParameter("CorrelationId", correlationId));
    }

    private static async Task<TargetHistoryFingerprint> ReadTargetHistoryFingerprintAsync(
        NpgsqlConnection connection,
        Guid[] targetIds,
        string[] targetWallets)
    {
        await using var command = new NpgsqlCommand("""
WITH fixture_rows(entity, identity_value, payload) AS (
    SELECT 'strategies', row.id::text, to_jsonb(row)::text
    FROM strategies row WHERE row.id = ANY(@TargetIds)
    UNION ALL
    SELECT 'signals', row.id::text, to_jsonb(row)::text
    FROM signals row WHERE row.trader_wallet = ANY(@TargetWallets)
    UNION ALL
    SELECT 'paper_orders', row.id::text, to_jsonb(row)::text
    FROM paper_orders row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'paper_fills', row.id::text, to_jsonb(row)::text
    FROM paper_fills row
    JOIN paper_orders paper_order ON paper_order.id = row.paper_order_id
    WHERE paper_order.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'live_orders', row.id::text, to_jsonb(row)::text
    FROM live_orders row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'strategy_market_paper_runs', row.id::text, to_jsonb(row)::text
    FROM strategy_market_paper_runs row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'paper_positions', row.id::text, to_jsonb(row)::text
    FROM paper_positions row WHERE row.copied_trader_wallet = ANY(@TargetWallets)
    UNION ALL
    SELECT 'paper_position_settlements', row.id::text, to_jsonb(row)::text
    FROM paper_position_settlements row WHERE row.copied_trader_wallet = ANY(@TargetWallets)
    UNION ALL
    SELECT 'strategy_child_parent_assignments', row.id::text, to_jsonb(row)::text
    FROM strategy_child_parent_assignments row
    WHERE row.child_strategy_id = ANY(@TargetIds) OR row.parent_strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'strategy_market_paper_skip_tombstones', row.archived_run_id::text, to_jsonb(row)::text
    FROM strategy_market_paper_skip_tombstones row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'strategy_market_paper_skip_tombstones_v2', row.archived_run_id::text, to_jsonb(row)::text
    FROM strategy_market_paper_skip_tombstones_v2 row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'strategy_paper_skip_rollups',
           row.strategy_id::text || '/' || row.bucket_start_utc::text || '/' || row.skip_reason,
           to_jsonb(row)::text
    FROM strategy_paper_skip_rollups row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'historical_gross_net_parity_audit', row.audit_id::text, to_jsonb(row)::text
    FROM historical_gross_net_parity_audit row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'paper_live_shadow_decisions', row.correlation_id::text, to_jsonb(row)::text
    FROM paper_live_shadow_decisions row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'paper_live_shadow_discrepancies', row.id::text, to_jsonb(row)::text
    FROM paper_live_shadow_discrepancies row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'polymarket_onchain_paper_signal_results', row.id::text, to_jsonb(row)::text
    FROM polymarket_onchain_paper_signal_results row
    WHERE row.copied_trader_wallet = ANY(@TargetWallets)
    UNION ALL
    SELECT 'dashboard_strategy_lifetime_projection_states', row.strategy_id::text, to_jsonb(row)::text
    FROM dashboard_strategy_lifetime_projection_states row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'dashboard_strategy_recent_projection_states',
           row.strategy_id::text || '/' || row.window_hours::text,
           to_jsonb(row)::text
    FROM dashboard_strategy_recent_projection_states row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'dashboard_strategy_recent_projection_facts',
           row.source_kind || '/' || row.source_id::text || '/' || row.fact_kind,
           to_jsonb(row)::text
    FROM dashboard_strategy_recent_projection_facts row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'dashboard_projection_reconciliation_queue', row.strategy_id::text, to_jsonb(row)::text
    FROM dashboard_projection_reconciliation_queue row WHERE row.strategy_id = ANY(@TargetIds)
    UNION ALL
    SELECT 'dashboard_projection_events', row.id::text, to_jsonb(row)::text
    FROM dashboard_projection_events row
    WHERE row.source_id::text = ANY(@TargetIdTexts)
       OR row.old_payload ->> 'strategy_id' = ANY(@TargetIdTexts)
       OR row.new_payload ->> 'strategy_id' = ANY(@TargetIdTexts)
)
SELECT count(*)::integer,
       COALESCE(
           md5(string_agg(entity || chr(31) || identity_value || chr(31) || payload,
                          chr(30) ORDER BY entity, identity_value)),
           md5(''))
FROM fixture_rows;
""", connection);
        command.Parameters.AddWithValue("TargetIds", targetIds);
        command.Parameters.AddWithValue("TargetIdTexts", targetIds.Select(id => id.ToString("D")).ToArray());
        command.Parameters.AddWithValue("TargetWallets", targetWallets);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TargetHistoryFingerprint(reader.GetInt32(0), reader.GetString(1));
    }

    private static Task<int> ReadTargetCountAsync(NpgsqlConnection connection) =>
        ExecuteScalarAsync<int>(connection, """
SELECT count(*)::integer
FROM strategies strategy
WHERE strategy.id = ANY(@Ids) OR strategy.code = ANY(@Codes);
""",
            new NpgsqlParameter("Ids", StrategyIds.NegativeProgressPurgeTargets.Keys.ToArray()),
            new NpgsqlParameter("Codes", StrategyIds.NegativeProgressPurgeTargets.Values.ToArray()));

    private static Task<int> ReadNonTargetStrategyCountAsync(NpgsqlConnection connection) =>
        ExecuteScalarAsync<int>(connection, """
SELECT count(*)::integer
FROM strategies strategy
WHERE NOT (strategy.id = ANY(@Ids) OR strategy.code = ANY(@Codes));
""",
            new NpgsqlParameter("Ids", StrategyIds.NegativeProgressPurgeTargets.Keys.ToArray()),
            new NpgsqlParameter("Codes", StrategyIds.NegativeProgressPurgeTargets.Values.ToArray()));

    private static async Task<IReadOnlyList<string>> ReadPreservedSourcePayloadsAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
SELECT to_jsonb(strategy)::text
FROM strategies strategy
WHERE strategy.id = ANY(@Ids)
ORDER BY strategy.id;
""", connection);
        command.Parameters.AddWithValue(
            "Ids",
            LegacyBaselineRetainedLowerEnterLinks.Select(link => link.SourceId).ToArray());
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static IReadOnlyList<string> GetRetiredCodes()
    {
        var codes = new List<string>();
        codes.AddRange(Enumerable.Range(1, 5).Select(value =>
            $"btc_up_down_5m_{value}_diff_limit_progress_premarket"));
        codes.AddRange(new[] { 1, 2, 4, 5 }.Select(value =>
            $"btc_up_down_5m_{value}_diff_shift_progress_premarket"));
        codes.AddRange(new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 13, 14, 19, 21, 24 }.Select(value =>
            $"eth_up_down_5m_{value}_child_progress"));
        codes.AddRange(new[] { 3, 5, 7, 8, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 23, 24 }.Select(value =>
            $"eth_up_down_5m_{value}_child_progress_roi"));
        codes.Add("eth_up_down_5m_4_diff_shift_progress_premarket");
        codes.AddRange(new[] { 1, 2, 13, 14, 15, 16 }.Select(value =>
            $"eth_up_down_5m_diff_{value}_up_progress"));
        codes.AddRange(new[] { 4, 5, 6, 13, 14, 19, 21, 23 }.Select(value =>
            $"sol_up_down_5m_{value}_child_progress_roi"));
        return codes;
    }

    private sealed record TargetHistoryFingerprint(int RowCount, string Md5);
}
