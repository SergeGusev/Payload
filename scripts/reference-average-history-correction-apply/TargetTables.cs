using Npgsql;
using NpgsqlTypes;

namespace ReferenceAverageHistoryCorrectionApply;

internal sealed record LoadedTargets(IReadOnlyDictionary<Guid, EntityIds> AddEntityIds);

internal static class TargetTables
{
    public static async Task<LoadedTargets> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GraphPackage graph,
        CancellationToken cancellationToken)
    {
        await using (var create = new NpgsqlCommand(CreateSql, connection, transaction))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var entityIds = graph.Adds.ToDictionary(add => add.RunId, add => new EntityIds(
            DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "signal"),
            DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_order"),
            DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_fill"),
            DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_position"),
            DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_position_settlement")));

        await ImportMainAsync(connection, graph.MainRemovals, cancellationToken);
        await ImportChildrenAsync(connection, graph.ChildRemovals, cancellationToken);
        await ImportAddsAsync(connection, graph, entityIds, cancellationToken);
        await ImportPositionKeysAsync(connection, graph.PositionKeys, cancellationToken);
        await ImportGraphRowHashesAsync(connection, graph.GraphRowHashes, cancellationToken);
        await ImportFillRowHashesAsync(connection, graph.FillRowHashes, cancellationToken);
        await ImportPositionRowHashesAsync(connection, graph.PositionRowHashes, cancellationToken);
        await ImportPositionSettlementRowHashesAsync(connection, graph.PositionSettlementRowHashes,
            cancellationToken);
        await PopulateDerivedTargetsAsync(connection, transaction, cancellationToken);
        return new LoadedTargets(entityIds);
    }

    private static async Task ImportGraphRowHashesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<GraphPhysicalRowHashes> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_graph_row_hashes (
                run_id, order_id, signal_id, run_full_row_sha256,
                order_full_row_sha256, signal_full_row_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.RunId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.OrderId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.SignalId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.RunFullRowSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.OrderFullRowSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.SignalFullRowSha256, NpgsqlDbType.Text, cancellationToken);
        }
        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportFillRowHashesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<FillPhysicalRowHash> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_fill_row_hashes (fill_id, order_id, full_row_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.FillId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.OrderId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.FullRowSha256, NpgsqlDbType.Text, cancellationToken);
        }
        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportPositionRowHashesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PositionPhysicalRowHash> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_position_row_hashes (id, copied_trader_wallet, asset_id, full_row_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.Id, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.CopiedTraderWallet, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.AssetId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.FullRowSha256, NpgsqlDbType.Text, cancellationToken);
        }
        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportPositionSettlementRowHashesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PositionSettlementPhysicalRowHash> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_position_settlement_row_hashes (
                id, copied_trader_wallet, asset_id, full_row_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.Id, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.CopiedTraderWallet, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.AssetId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.FullRowSha256, NpgsqlDbType.Text, cancellationToken);
        }
        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportPositionKeysAsync(
        NpgsqlConnection connection,
        IReadOnlyList<PositionKeyTarget> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_position_keys (copied_trader_wallet, asset_id)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.CopiedTraderWallet, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.AssetId, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportMainAsync(
        NpgsqlConnection connection,
        IReadOnlyList<MainRemoval> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_main_removals (
                run_id, strategy_id, strategy_code, market_id, order_id, signal_id,
                asset_id, outcome, copied_trader_wallet, corrected_skipped_updated_at_utc,
                restored_base_stake_usd,
                historical_effective_stake_usd, historical_target_notional_usd,
                historical_stake_sizing_source, stake_sizing_proof_sha256,
                classifier_reason, classifier_action, signal_preview_manifest_sha256,
                replay_classifier_sha256, replay_evidence_json, replay_evidence_sha256,
                graph_state_sha256, fill_set_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.RunId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.StrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.StrategyCode, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.MarketId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.OrderId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.SignalId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.AssetId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.Outcome, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.CopiedTraderWallet, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.CorrectedSkippedUpdatedAtUtc.UtcDateTime,
                NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(row.RestoredBaseStakeUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.HistoricalEffectiveStakeUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.HistoricalTargetNotionalUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.HistoricalStakeSizingSource, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.StakeSizingProofSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ClassifierReason, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ClassifierAction, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.SignalPreviewManifestSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ReplayClassifierSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ReplayEvidenceJson, NpgsqlDbType.Jsonb, cancellationToken);
            await importer.WriteAsync(row.ReplayEvidenceSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.GraphStateSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.FillSetSha256, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportChildrenAsync(
        NpgsqlConnection connection,
        IReadOnlyList<ChildRemoval> rows,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_child_removals (
                parent_run_id, run_id, strategy_id, strategy_code, market_id,
                order_id, signal_id, graph_state_sha256, fill_set_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.ParentRunId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.ChildRunId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.ChildStrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.ChildStrategyCode, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.MarketId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ChildOrderId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.ChildSignalId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.GraphStateSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.FillSetSha256, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task ImportAddsAsync(
        NpgsqlConnection connection,
        GraphPackage graph,
        IReadOnlyDictionary<Guid, EntityIds> ids,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync("""
            COPY correction_adds (
                run_id, strategy_id, strategy_code, market_id, condition_id, asset_symbol, kind,
                modeled_entry_at_utc, modeled_settled_at_utc, resolution_ledger_first_received_at_utc,
                modeled_settlement_timestamp_source,
                settlement_category, modeled_raw_decision_json, modeled_raw_decision_sha256,
                modeled_fill_evidence, modeled_mutation_payload_json,
                modeled_mutation_payload_sha256,
                assumed_fill_price, historical_stake_multiplier, selected_outcome, selected_token_id,
                resolved_winning_outcome, resolved_winning_token_id, requested_notional_usd,
                worst_price_target_size_shares, filled_size_shares, won, settlement_price,
                settlement_value_usd, realized_pnl_usd,
                source_run_state_sha256, signal_id, order_id, fill_id, position_id, settlement_id,
                source_run_full_row_sha256)
            FROM STDIN (FORMAT BINARY)
            """, cancellationToken);
        foreach (var row in graph.Adds)
        {
            var entity = ids[row.RunId];
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync(row.RunId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.StrategyId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.StrategyCode, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.MarketId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ConditionId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.Asset, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.Kind, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledEntryAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz,
                cancellationToken);
            await importer.WriteAsync(row.ModeledSettledAtUtc.UtcDateTime, NpgsqlDbType.TimestampTz,
                cancellationToken);
            await importer.WriteAsync(row.ResolutionLedgerFirstReceivedAtUtc.UtcDateTime,
                NpgsqlDbType.TimestampTz, cancellationToken);
            await importer.WriteAsync(row.ModeledSettlementTimestampSource, NpgsqlDbType.Text,
                cancellationToken);
            await importer.WriteAsync(row.SettlementCategory, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledRawDecisionJson, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledRawDecisionSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledFillEvidence, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledMutationPayloadJson, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ModeledMutationPayloadSha256, NpgsqlDbType.Text,
                cancellationToken);
            await importer.WriteAsync(row.AssumedFillPrice, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.HistoricalStakeMultiplier, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.SelectedOutcome, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.SelectedTokenId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ResolvedWinningOutcome, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.ResolvedWinningTokenId, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(row.RequestedNotionalUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.WorstPriceTargetSizeShares, NpgsqlDbType.Numeric,
                cancellationToken);
            await importer.WriteAsync(row.FilledSizeShares, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.Won, NpgsqlDbType.Boolean, cancellationToken);
            await importer.WriteAsync(row.SettlementPrice, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.SettlementValueUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.RealizedPnlUsd, NpgsqlDbType.Numeric, cancellationToken);
            await importer.WriteAsync(row.AddSourceStateSha256, NpgsqlDbType.Text, cancellationToken);
            await importer.WriteAsync(entity.SignalId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(entity.OrderId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(entity.FillId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(entity.PositionId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(entity.SettlementId, NpgsqlDbType.Uuid, cancellationToken);
            await importer.WriteAsync(row.AddSourceRunFullRowSha256, NpgsqlDbType.Text, cancellationToken);
        }

        await importer.CompleteAsync(cancellationToken);
    }

    private static async Task PopulateDerivedTargetsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO correction_target_runs (id)
            SELECT run_id FROM correction_main_removals
            UNION SELECT run_id FROM correction_child_removals
            UNION SELECT run_id FROM correction_adds;

            INSERT INTO correction_target_signals (id)
            SELECT signal_id FROM correction_main_removals
            UNION SELECT signal_id FROM correction_child_removals
            UNION SELECT signal_id FROM correction_adds;

            INSERT INTO correction_target_orders (id)
            SELECT order_id FROM correction_main_removals
            UNION SELECT order_id FROM correction_child_removals
            UNION SELECT order_id FROM correction_adds;

            INSERT INTO correction_target_strategies (id)
            SELECT strategy_id FROM correction_main_removals
            UNION SELECT strategy_id FROM correction_child_removals
            UNION SELECT strategy_id FROM correction_adds;

            INSERT INTO correction_position_keys (copied_trader_wallet, asset_id)
            SELECT DISTINCT paper_order.copied_trader_wallet, paper_order.asset_id
            FROM public.paper_orders paper_order
            JOIN correction_target_orders target ON target.id = paper_order.id
            ON CONFLICT DO NOTHING;

            INSERT INTO correction_position_keys (copied_trader_wallet, asset_id)
            SELECT 'strategy:' || strategy_code, selected_token_id
            FROM correction_adds
            ON CONFLICT DO NOTHING;

            INSERT INTO correction_target_wallets (copied_trader_wallet)
            SELECT DISTINCT copied_trader_wallet FROM correction_position_keys;

            INSERT INTO correction_target_conditions (strategy_id, condition_id)
            SELECT strategy_id, condition_id FROM correction_adds
            UNION
            SELECT paper_order.strategy_id, paper_order.condition_id
            FROM public.paper_orders paper_order
            JOIN correction_target_orders target ON target.id = paper_order.id;

            INSERT INTO correction_target_correlations (id)
            SELECT DISTINCT paper_order.correlation_id
            FROM public.paper_orders paper_order
            JOIN correction_target_orders target ON target.id = paper_order.id
            WHERE paper_order.correlation_id IS NOT NULL;
            """, connection, transaction) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string CreateSql = """
        CREATE TEMP TABLE correction_main_removals (
            run_id uuid PRIMARY KEY,
            strategy_id uuid NOT NULL,
            strategy_code text NOT NULL,
            market_id text NOT NULL,
            order_id uuid NOT NULL UNIQUE,
            signal_id uuid NOT NULL UNIQUE,
            asset_id text NOT NULL,
            outcome text NOT NULL,
            copied_trader_wallet text NOT NULL,
            corrected_skipped_updated_at_utc timestamptz NOT NULL,
            restored_base_stake_usd numeric NOT NULL,
            historical_effective_stake_usd numeric NOT NULL,
            historical_target_notional_usd numeric NOT NULL,
            historical_stake_sizing_source text NOT NULL,
            stake_sizing_proof_sha256 text NOT NULL,
            classifier_reason text NOT NULL,
            classifier_action text NOT NULL,
            signal_preview_manifest_sha256 text NOT NULL,
            replay_classifier_sha256 text NOT NULL,
            replay_evidence_json jsonb NOT NULL,
            replay_evidence_sha256 text NOT NULL,
            graph_state_sha256 text NOT NULL,
            fill_set_sha256 text NOT NULL
        ) ON COMMIT DROP;

        CREATE TEMP TABLE correction_child_removals (
            parent_run_id uuid NOT NULL,
            run_id uuid PRIMARY KEY,
            strategy_id uuid NOT NULL,
            strategy_code text NOT NULL,
            market_id text NOT NULL,
            order_id uuid NOT NULL UNIQUE,
            signal_id uuid NOT NULL UNIQUE,
            graph_state_sha256 text NOT NULL,
            fill_set_sha256 text NOT NULL
        ) ON COMMIT DROP;

        CREATE TEMP TABLE correction_adds (
            run_id uuid PRIMARY KEY,
            strategy_id uuid NOT NULL,
            strategy_code text NOT NULL,
            market_id text NOT NULL,
            condition_id text NOT NULL,
            asset_symbol text NOT NULL,
            kind text NOT NULL,
            modeled_entry_at_utc timestamptz NOT NULL,
            modeled_settled_at_utc timestamptz NOT NULL,
            resolution_ledger_first_received_at_utc timestamptz NOT NULL,
            modeled_settlement_timestamp_source text NOT NULL,
            settlement_category text NOT NULL,
            modeled_raw_decision_json text NOT NULL,
            modeled_raw_decision_sha256 text NOT NULL,
            modeled_fill_evidence text NOT NULL,
            modeled_mutation_payload_json text NOT NULL,
            modeled_mutation_payload_sha256 text NOT NULL,
            assumed_fill_price numeric NOT NULL,
            historical_stake_multiplier numeric NOT NULL,
            selected_outcome text NOT NULL,
            selected_token_id text NOT NULL,
            resolved_winning_outcome text NOT NULL,
            resolved_winning_token_id text NOT NULL,
            requested_notional_usd numeric NOT NULL,
            worst_price_target_size_shares numeric NOT NULL,
            filled_size_shares numeric NOT NULL,
            won boolean NOT NULL,
            settlement_price numeric NOT NULL,
            settlement_value_usd numeric NOT NULL,
            realized_pnl_usd numeric NOT NULL,
            source_run_state_sha256 text NOT NULL,
            signal_id uuid NOT NULL UNIQUE,
            order_id uuid NOT NULL UNIQUE,
            fill_id uuid NOT NULL UNIQUE,
            position_id uuid NOT NULL UNIQUE,
            settlement_id uuid NOT NULL UNIQUE,
            source_run_full_row_sha256 text NOT NULL
        ) ON COMMIT DROP;

        CREATE TEMP TABLE correction_graph_row_hashes (
            run_id uuid PRIMARY KEY,
            order_id uuid NOT NULL UNIQUE,
            signal_id uuid NOT NULL UNIQUE,
            run_full_row_sha256 text NOT NULL,
            order_full_row_sha256 text NOT NULL,
            signal_full_row_sha256 text NOT NULL
        ) ON COMMIT DROP;
        CREATE TEMP TABLE correction_fill_row_hashes (
            fill_id uuid PRIMARY KEY,
            order_id uuid NOT NULL UNIQUE,
            full_row_sha256 text NOT NULL
        ) ON COMMIT DROP;
        CREATE TEMP TABLE correction_position_row_hashes (
            id uuid PRIMARY KEY,
            copied_trader_wallet text NOT NULL,
            asset_id text NOT NULL,
            full_row_sha256 text NOT NULL
        ) ON COMMIT DROP;
        CREATE TEMP TABLE correction_position_settlement_row_hashes (
            id uuid PRIMARY KEY,
            copied_trader_wallet text NOT NULL,
            asset_id text NOT NULL,
            full_row_sha256 text NOT NULL
        ) ON COMMIT DROP;

        CREATE TEMP TABLE correction_target_runs (id uuid PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_signals (id uuid PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_orders (id uuid PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_strategies (id uuid PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_wallets (copied_trader_wallet text PRIMARY KEY) ON COMMIT DROP;
        CREATE TEMP TABLE correction_position_keys (
            copied_trader_wallet text NOT NULL,
            asset_id text NOT NULL,
            PRIMARY KEY (copied_trader_wallet, asset_id)
        ) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_conditions (
            strategy_id uuid NOT NULL,
            condition_id text NOT NULL,
            PRIMARY KEY (strategy_id, condition_id)
        ) ON COMMIT DROP;
        CREATE TEMP TABLE correction_target_correlations (id uuid PRIMARY KEY) ON COMMIT DROP;
        """;
}
