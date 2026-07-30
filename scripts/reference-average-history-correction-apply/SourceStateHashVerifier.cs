using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace ReferenceAverageHistoryCorrectionApply;

internal static class SourceStateHashVerifier
{
    private const int SchemaVersion = 1;

    public static async Task VerifyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await VerifyGraphOrdersAsync(connection, transaction, cancellationToken);
        await VerifyFillSetsAsync(connection, transaction, cancellationToken);
        await VerifyFullPhysicalRowsAsync(connection, transaction, cancellationToken);
        await VerifyAddSourcesAsync(connection, transaction, cancellationToken);
    }

    private static async Task VerifyGraphOrdersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH expected AS (
                SELECT 'Main'::text AS scope, NULL::uuid AS parent_main_run_id,
                       run_id, graph_state_sha256 AS expected_sha256
                FROM correction_main_removals
                UNION ALL
                SELECT 'Child', parent_run_id, run_id, graph_state_sha256
                FROM correction_child_removals
            )
            SELECT expected.scope, expected.parent_main_run_id,
                   strategy_run.id, strategy_run.strategy_id, strategy.code,
                   strategy_run.market_id, strategy_run.condition_id,
                   strategy_run.entry_due_at_utc, strategy_run.status,
                   strategy_run.selected_outcome, strategy_run.selected_asset_id,
                   strategy_run.entry_price, strategy_run.stake_usd, strategy_run.size_shares,
                   strategy_run.settlement_price, strategy_run.settlement_value_usd,
                   strategy_run.realized_pnl_usd, strategy_run.settled_at_utc,
                   paper_order.id, paper_order.signal_id, paper_order.status,
                   paper_order.side, paper_order.outcome, paper_order.asset_id,
                   paper_order.copied_trader_wallet, paper_order.price,
                   paper_order.size_shares, paper_order.notional_usd,
                   paper_order.correlation_id, paper_order.execution_source,
                   paper_order.created_at_utc,
                   strategy_run.signal_id, strategy_run.paper_order_id,
                   paper_order.strategy_id,
                   signal.id, signal.outcome, signal.asset_id, signal.condition_id,
                   paper_order.raw_decision_json::text,
                   expected.expected_sha256
            FROM expected
            JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = expected.run_id
            JOIN public.strategies strategy ON strategy.id = strategy_run.strategy_id
            JOIN public.paper_orders paper_order ON paper_order.id = strategy_run.paper_order_id
            LEFT JOIN public.signals signal ON signal.id = strategy_run.signal_id
            ORDER BY expected.scope COLLATE "C", strategy_run.id;
            """, connection, transaction) { CommandTimeout = 0 };
        var count = 0;
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var rawDecisionHash = data.IsDBNull(38)
                ? string.Empty
                : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.GetString(38))));
            var row = new GraphOrderState(
                data.GetString(0),
                data.IsDBNull(1) ? null : data.GetGuid(1),
                data.GetGuid(2), data.GetGuid(3), data.GetString(4), data.GetString(5), data.GetString(6),
                Utc(data.GetDateTime(7)), data.GetString(8),
                data.IsDBNull(9) ? null : data.GetString(9),
                data.IsDBNull(10) ? null : data.GetString(10),
                data.IsDBNull(11) ? null : data.GetDecimal(11), data.GetDecimal(12),
                data.IsDBNull(13) ? null : data.GetDecimal(13),
                data.IsDBNull(14) ? null : data.GetDecimal(14),
                data.IsDBNull(15) ? null : data.GetDecimal(15),
                data.IsDBNull(16) ? null : data.GetDecimal(16),
                data.IsDBNull(17) ? null : Utc(data.GetDateTime(17)),
                data.GetGuid(18), data.GetGuid(19), data.GetString(20), data.GetString(21),
                data.GetString(22), data.GetString(23), data.GetString(24), data.GetDecimal(25),
                data.GetDecimal(26), data.GetDecimal(27),
                data.IsDBNull(28) ? null : data.GetGuid(28), data.GetString(29), Utc(data.GetDateTime(30)),
                data.IsDBNull(31) ? null : data.GetGuid(31),
                data.IsDBNull(32) ? null : data.GetGuid(32), data.GetGuid(33),
                data.IsDBNull(34) ? null : data.GetGuid(34),
                data.IsDBNull(35) ? null : data.GetString(35),
                data.IsDBNull(36) ? null : data.GetString(36),
                data.IsDBNull(37) ? null : data.GetString(37), rawDecisionHash);
            var actual = HashGraphOrder(row);
            var expected = data.GetString(39).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fresh graph-state hash mismatch for run {row.RunId:D}: expected {expected}, actual {actual}.");
            }
            count++;
        }

        var expectedCount = await ScalarCountAsync(connection, transaction,
            "SELECT (SELECT count(*) FROM correction_main_removals) + (SELECT count(*) FROM correction_child_removals);",
            cancellationToken);
        if (count != expectedCount)
        {
            throw new InvalidOperationException($"Fresh graph-state row count mismatch: expected {expectedCount}, read {count}.");
        }
    }

    private static async Task VerifyFillSetsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var expected = new Dictionary<Guid, (string Hash, string Scope, Guid? ParentRunId, Guid RunId)>();
        await using (var expectedCommand = new NpgsqlCommand("""
                         SELECT order_id, fill_set_sha256, 'Main'::text, NULL::uuid, run_id
                         FROM correction_main_removals
                         UNION ALL
                         SELECT order_id, fill_set_sha256, 'Child', parent_run_id, run_id
                         FROM correction_child_removals;
                         """, connection, transaction))
        await using (var expectedData = await expectedCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await expectedData.ReadAsync(cancellationToken))
            {
                expected.Add(expectedData.GetGuid(0),
                    (expectedData.GetString(1).ToLowerInvariant(), expectedData.GetString(2),
                        expectedData.IsDBNull(3) ? null : expectedData.GetGuid(3), expectedData.GetGuid(4)));
            }
        }

        var fills = expected.Keys.ToDictionary(id => id, _ => new List<GraphFillState>());
        await using var command = new NpgsqlCommand("""
            SELECT paper_fill.paper_order_id, paper_fill.id, paper_fill.price,
                   paper_fill.size_shares, paper_fill.filled_at_utc,
                   paper_fill.realized_pnl_usd, paper_fill.evidence
            FROM public.paper_fills paper_fill
            JOIN correction_target_orders target ON target.id = paper_fill.paper_order_id
            WHERE EXISTS (SELECT 1 FROM correction_main_removals row WHERE row.order_id = paper_fill.paper_order_id)
               OR EXISTS (SELECT 1 FROM correction_child_removals row WHERE row.order_id = paper_fill.paper_order_id)
            ORDER BY paper_fill.paper_order_id, paper_fill.filled_at_utc, paper_fill.id;
            """, connection, transaction) { CommandTimeout = 0 };
        await using (var data = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await data.ReadAsync(cancellationToken))
            {
                var orderId = data.GetGuid(0);
                var identity = expected[orderId];
                fills[orderId].Add(new GraphFillState(identity.Scope, identity.ParentRunId, identity.RunId,
                    orderId, data.GetGuid(1), data.GetDecimal(2), data.GetDecimal(3),
                    Utc(data.GetDateTime(4)), data.GetDecimal(5), data.GetString(6)));
            }
        }

        foreach (var pair in expected)
        {
            var actual = HashFillSet(fills[pair.Key]);
            if (!actual.Equals(pair.Value.Hash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fresh fill-set hash mismatch for order {pair.Key:D}: expected {pair.Value.Hash}, actual {actual}.");
            }
        }
    }

    private static async Task VerifyAddSourcesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT strategy_run.id, strategy_run.strategy_id, strategy.code,
                   strategy_run.market_id, strategy_run.condition_id,
                   strategy_run.status, strategy_run.skip_reason, strategy_run.entry_due_at_utc,
                   strategy_run.market_end_utc, strategy_run.stake_usd,
                   strategy_run.selected_asset_id, strategy_run.selected_outcome,
                   strategy_run.entry_price, strategy_run.size_shares,
                   strategy_run.signal_id, strategy_run.paper_order_id,
                   strategy_run.entered_at_utc, strategy_run.settlement_price,
                   strategy_run.settlement_value_usd, strategy_run.realized_pnl_usd,
                   strategy_run.settled_at_utc, strategy_run.skip_diagnostics_json::text,
                   strategy_run.market_slug, target.source_run_state_sha256,
                   to_jsonb(strategy_run)::text, target.source_run_full_row_sha256
            FROM correction_adds target
            JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
            JOIN public.strategies strategy ON strategy.id = strategy_run.strategy_id
            ORDER BY strategy_run.id;
            """, connection, transaction) { CommandTimeout = 0 };
        var count = 0;
        await using var data = await command.ExecuteReaderAsync(cancellationToken);
        while (await data.ReadAsync(cancellationToken))
        {
            var row = new AddSourceState(
                data.GetGuid(0), data.GetGuid(1), data.GetString(2), data.GetString(3), data.GetString(4),
                data.GetString(5), data.IsDBNull(6) ? null : data.GetString(6), Utc(data.GetDateTime(7)),
                data.IsDBNull(8) ? null : Utc(data.GetDateTime(8)), data.GetDecimal(9),
                data.IsDBNull(10) ? null : data.GetString(10), data.IsDBNull(11) ? null : data.GetString(11),
                data.IsDBNull(12) ? null : data.GetDecimal(12), data.IsDBNull(13) ? null : data.GetDecimal(13),
                data.IsDBNull(14) ? null : data.GetGuid(14), data.IsDBNull(15) ? null : data.GetGuid(15),
                data.IsDBNull(16) ? null : Utc(data.GetDateTime(16)),
                data.IsDBNull(17) ? null : data.GetDecimal(17),
                data.IsDBNull(18) ? null : data.GetDecimal(18),
                data.IsDBNull(19) ? null : data.GetDecimal(19),
                data.IsDBNull(20) ? null : Utc(data.GetDateTime(20)),
                data.IsDBNull(21) ? null : data.GetString(21), data.GetString(22));
            var actual = HashAddSource(row);
            var expected = data.GetString(23).ToLowerInvariant();
            if (!actual.Equals(expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Fresh add-source hash mismatch for run {row.RunId:D}: expected {expected}, actual {actual}.");
            }
            VerifyPostgresFullRowHash(data.GetString(24), data.GetString(25),
                $"add source run {row.RunId:D}");
            count++;
        }

        var expectedCount = await ScalarCountAsync(connection, transaction,
            "SELECT count(*) FROM correction_adds;", cancellationToken);
        if (count != expectedCount)
        {
            throw new InvalidOperationException($"Fresh add-source row count mismatch: expected {expectedCount}, read {count}.");
        }
    }

    private static async Task VerifyFullPhysicalRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand("""
                         SELECT expected.run_id,
                                to_jsonb(strategy_run)::text, expected.run_full_row_sha256,
                                to_jsonb(paper_order)::text, expected.order_full_row_sha256,
                                to_jsonb(signal)::text, expected.signal_full_row_sha256
                         FROM correction_graph_row_hashes expected
                         JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = expected.run_id
                         JOIN public.paper_orders paper_order ON paper_order.id = expected.order_id
                         JOIN public.signals signal ON signal.id = expected.signal_id
                         ORDER BY expected.run_id;
                         """, connection, transaction) { CommandTimeout = 0 })
        {
            var count = 0;
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                var runId = data.GetGuid(0);
                VerifyPostgresFullRowHash(data.GetString(1), data.GetString(2), $"run {runId:D}");
                VerifyPostgresFullRowHash(data.GetString(3), data.GetString(4), $"order for run {runId:D}");
                VerifyPostgresFullRowHash(data.GetString(5), data.GetString(6), $"signal for run {runId:D}");
                count++;
            }
            var expected = await ScalarCountAsync(connection, transaction,
                "SELECT count(*) FROM correction_graph_row_hashes;", cancellationToken);
            if (count != expected)
            {
                throw new InvalidOperationException(
                    $"Full run/order/signal hash row-count mismatch: expected {expected}, read {count}.");
            }
        }

        await VerifySingleTableFullHashesAsync(connection, transaction, "paper_fills",
            "correction_fill_row_hashes", cancellationToken);
        await VerifySingleTableFullHashesAsync(connection, transaction, "paper_positions",
            "correction_position_row_hashes", cancellationToken);
        await VerifySingleTableFullHashesAsync(connection, transaction, "paper_position_settlements",
            "correction_position_settlement_row_hashes", cancellationToken);
    }

    private static async Task VerifySingleTableFullHashesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string expectedTable,
        CancellationToken cancellationToken)
    {
        var sql = $"""
            SELECT expected.id, to_jsonb(source)::text, expected.full_row_sha256
            FROM {expectedTable} expected
            JOIN public.{table} source ON source.id = expected.id
            ORDER BY expected.id;
            """;
        var count = 0;
        await using (var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 })
        await using (var data = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await data.ReadAsync(cancellationToken))
            {
                VerifyPostgresFullRowHash(data.GetString(1), data.GetString(2),
                    $"{table} row {data.GetGuid(0):D}");
                count++;
            }
        }
        var expectedCount = await ScalarCountAsync(connection, transaction,
            $"SELECT count(*) FROM {expectedTable};", cancellationToken);
        if (count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Full-row hash count mismatch for {table}: expected {expectedCount}, read {count}.");
        }
    }

    private static void VerifyPostgresFullRowHash(string rowJson, string expectedHash, string label)
    {
        var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rowJson))).ToLowerInvariant();
        var expected = expectedHash.ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Fresh full-row hash mismatch for {label}: expected {expected}, actual {actual}.");
        }
    }

    internal static string HashGraphOrder(GraphOrderState item) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "graph_order_mutation_scope",
        item.Scope,
        parent_main_run_id = FormatGuid(item.ParentMainRunId),
        run_id = item.RunId.ToString("D"),
        strategy_id = item.StrategyId.ToString("D"),
        item.StrategyCode,
        item.MarketId,
        item.ConditionId,
        entry_due_at_utc = FormatTimestamp(item.EntryDueAtUtc),
        item.RunStatus,
        run_outcome = item.RunOutcome ?? string.Empty,
        run_asset_id = item.RunAssetId ?? string.Empty,
        entry_price = FormatNullableDecimal(item.EntryPrice),
        stake_usd = FormatDecimal(item.StakeUsd),
        run_size_shares = FormatNullableDecimal(item.RunSizeShares),
        settlement_price = FormatNullableDecimal(item.SettlementPrice),
        settlement_value_usd = FormatNullableDecimal(item.SettlementValueUsd),
        run_realized_pnl_usd = FormatNullableDecimal(item.RunRealizedPnlUsd),
        settled_at_utc = FormatTimestamp(item.SettledAtUtc),
        paper_order_id = item.OrderId.ToString("D"),
        signal_id = item.SignalId.ToString("D"),
        item.OrderStatus,
        item.OrderSide,
        item.OrderOutcome,
        item.AssetId,
        item.CopiedTraderWallet,
        order_price = FormatDecimal(item.OrderPrice),
        order_size_shares = FormatDecimal(item.OrderSizeShares),
        order_notional_usd = FormatDecimal(item.OrderNotionalUsd),
        correlation_id = FormatGuid(item.CorrelationId),
        item.ExecutionSource,
        order_created_at_utc = FormatTimestamp(item.OrderCreatedAtUtc),
        run_signal_id = FormatGuid(item.RunSignalIdProof),
        run_paper_order_id = FormatGuid(item.RunPaperOrderIdProof),
        order_strategy_id = item.OrderStrategyIdProof.ToString("D"),
        signal_row_id = FormatGuid(item.SignalRowIdProof),
        signal_outcome = item.SignalOutcomeProof ?? string.Empty,
        signal_asset_id = item.SignalAssetIdProof ?? string.Empty,
        signal_condition_id = item.SignalConditionIdProof ?? string.Empty,
        item.RawDecisionProofSha256
    });

    internal static string HashFillSet(IEnumerable<GraphFillState> rows) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "graph_fill_set_mutation_scope",
        rows = rows.OrderBy(item => item.OrderId)
            .ThenBy(item => item.FilledAtUtc)
            .ThenBy(item => item.FillId)
            .Select(item => new
            {
                item.Scope,
                parent_main_run_id = FormatGuid(item.ParentMainRunId),
                run_id = item.RunId.ToString("D"),
                paper_order_id = item.OrderId.ToString("D"),
                fill_id = item.FillId.ToString("D"),
                price = FormatDecimal(item.Price),
                size_shares = FormatDecimal(item.SizeShares),
                filled_at_utc = FormatTimestamp(item.FilledAtUtc),
                realized_pnl_usd = FormatDecimal(item.RealizedPnlUsd),
                item.Evidence
            })
    });

    internal static string HashAddSource(AddSourceState item) => HashObject(new
    {
        schema_version = SchemaVersion,
        entity = "add_source_run_mutation_scope",
        run_id = item.RunId.ToString("D"),
        strategy_id = item.StrategyId.ToString("D"),
        item.StrategyCode,
        item.MarketId,
        item.ConditionId,
        item.RunStatus,
        skip_reason = item.SkipReason ?? string.Empty,
        entry_due_at_utc = FormatTimestamp(item.EntryDueAtUtc),
        market_end_utc = FormatTimestamp(item.MarketEndUtc),
        stake_usd = FormatDecimal(item.StakeUsd),
        selected_asset_id = item.SelectedAssetId ?? string.Empty,
        selected_outcome = item.SelectedOutcome ?? string.Empty,
        entry_price = FormatNullableDecimal(item.EntryPrice),
        size_shares = FormatNullableDecimal(item.SizeShares),
        signal_id = FormatGuid(item.SignalId),
        paper_order_id = FormatGuid(item.PaperOrderId),
        entered_at_utc = FormatTimestamp(item.EnteredAtUtc),
        settlement_price = FormatNullableDecimal(item.SettlementPrice),
        settlement_value_usd = FormatNullableDecimal(item.SettlementValueUsd),
        realized_pnl_usd = FormatNullableDecimal(item.RealizedPnlUsd),
        settled_at_utc = FormatTimestamp(item.SettledAtUtc),
        skip_diagnostics_json = item.SkipDiagnosticsJson ?? string.Empty,
        item.MarketSlug
    });

    private static string HashObject<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
    private static string FormatNullableDecimal(decimal? value) => value is null ? string.Empty : FormatDecimal(value.Value);
    private static string FormatGuid(Guid? value) => value?.ToString("D") ?? string.Empty;
    private static string FormatTimestamp(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O") ?? string.Empty;
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static async Task<int> ScalarCountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal sealed record GraphOrderState(
    string Scope,
    Guid? ParentMainRunId,
    Guid RunId,
    Guid StrategyId,
    string StrategyCode,
    string MarketId,
    string ConditionId,
    DateTimeOffset EntryDueAtUtc,
    string RunStatus,
    string? RunOutcome,
    string? RunAssetId,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? RunSizeShares,
    decimal? SettlementPrice,
    decimal? SettlementValueUsd,
    decimal? RunRealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    Guid OrderId,
    Guid SignalId,
    string OrderStatus,
    string OrderSide,
    string OrderOutcome,
    string AssetId,
    string CopiedTraderWallet,
    decimal OrderPrice,
    decimal OrderSizeShares,
    decimal OrderNotionalUsd,
    Guid? CorrelationId,
    string ExecutionSource,
    DateTimeOffset OrderCreatedAtUtc,
    Guid? RunSignalIdProof,
    Guid? RunPaperOrderIdProof,
    Guid OrderStrategyIdProof,
    Guid? SignalRowIdProof,
    string? SignalOutcomeProof,
    string? SignalAssetIdProof,
    string? SignalConditionIdProof,
    string RawDecisionProofSha256);

internal sealed record GraphFillState(
    string Scope,
    Guid? ParentMainRunId,
    Guid RunId,
    Guid OrderId,
    Guid FillId,
    decimal Price,
    decimal SizeShares,
    DateTimeOffset FilledAtUtc,
    decimal RealizedPnlUsd,
    string Evidence);

internal sealed record AddSourceState(
    Guid RunId,
    Guid StrategyId,
    string StrategyCode,
    string MarketId,
    string ConditionId,
    string RunStatus,
    string? SkipReason,
    DateTimeOffset EntryDueAtUtc,
    DateTimeOffset? MarketEndUtc,
    decimal StakeUsd,
    string? SelectedAssetId,
    string? SelectedOutcome,
    decimal? EntryPrice,
    decimal? SizeShares,
    Guid? SignalId,
    Guid? PaperOrderId,
    DateTimeOffset? EnteredAtUtc,
    decimal? SettlementPrice,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string? SkipDiagnosticsJson,
    string MarketSlug);
