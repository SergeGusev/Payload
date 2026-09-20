using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    public async Task ConfirmFinalPaperOrderAsync(Guid orderId, FinalMarketOutcomeEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockFinalPaperIdentityAsync(connection, transaction, orderId, cancellationToken);
        await ConfirmFinalOrdersAsync(connection, transaction, evidence, [orderId], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    public Task<StrategyLiveBalanceAdjustmentResult> ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(
        Guid liveOrderId, Guid strategyId, decimal value, decimal gross, decimal? net, string? winner,
        string outcome, DateTimeOffset settled, DateTimeOffset updated, long version, FinalMarketOutcomeEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        if (winner != evidence.WinningAssetId || outcome != evidence.WinningOutcome)
            throw new InvalidOperationException("Final Live winner conflict.");
        return ApplyLiveOrderSettlementToStrategyBalanceCoreAsync(liveOrderId,strategyId,value,gross,net,
            winner,outcome,settled,updated,version,cancellationToken,evidence);
    }
    private static async Task ConfirmLinkedPaperAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid liveOrderId, FinalMarketOutcomeEvidence? evidence, CancellationToken cancellationToken)
    {
        if (evidence is null) return;
        await using var command = new NpgsqlCommand("SELECT paper_order_id FROM live_orders WHERE id=@Id", connection, transaction);
        command.Parameters.AddWithValue("Id", liveOrderId);
        if (await command.ExecuteScalarAsync(cancellationToken) is Guid paperId)
            await ConfirmFinalOrdersAsync(connection, transaction, evidence, [paperId], cancellationToken);
    }

    public async Task<PolymarketGammaMarket?> GetGammaMarketForOutcomeAsync(string conditionId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = CreateCommand(connection,
            $"SELECT {PolymarketGammaMarketSelectColumns} FROM polymarket_gamma_markets WHERE condition_id=@Condition LIMIT 2");
        command.Parameters.AddWithValue("Condition", conditionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var result = ReadPolymarketGammaMarket(reader);
        return await reader.ReadAsync(cancellationToken) ? null : result;
    }

    public async Task<StrategyLostCounterUpdateResult> PersistFinalPaperRunAsync(FinalPaperRunSettlement write,
        CancellationToken cancellationToken = default)
    {
        var run = write.Run;
        var evidence = write.Evidence;
        if (run.Status != StrategyMarketPaperRunStatuses.Settled ||
            !evidence.Matches(run.ConditionId, run.SelectedAssetId ?? "", run.SelectedOutcome ?? "") ||
            run.SettlementPrice != (run.SelectedAssetId == evidence.WinningAssetId ? 1m : 0m))
            throw new InvalidOperationException("Final run identity or payout conflict.");
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Match fill reconciliation and generic settlement: wallet, position, order,
        // then financial strategy/run rows. Never wait for a wallet while holding a run.
        if (run.PaperOrderId is { } paperId)
            await LockFinalPaperIdentityAsync(connection, transaction, paperId, cancellationToken, write.Position);
        else if (write.Position is { } unlockedPosition)
            await LockPaperPositionKeysAsync(connection, transaction, [unlockedPosition], [], cancellationToken);
        await using (var gate = new NpgsqlCommand("SELECT id FROM strategies WHERE id=@Strategy FOR UPDATE", connection, transaction))
        {
            gate.Parameters.AddWithValue("Strategy", run.StrategyId);
            await gate.ExecuteScalarAsync(cancellationToken);
        }
        await using (var current = new NpgsqlCommand("""
            SELECT status,market_id,condition_id,selected_asset_id,selected_outcome,settlement_price,paper_order_id
            FROM strategy_market_paper_runs WHERE id=@Run AND strategy_id=@Strategy FOR UPDATE
            """, connection, transaction))
        {
            current.Parameters.AddWithValue("Run", run.Id);
            current.Parameters.AddWithValue("Strategy", run.StrategyId);
            await using var reader = await current.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || reader.GetString(1) != run.MarketId ||
                reader.GetString(2) != run.ConditionId || reader.GetString(3) != run.SelectedAssetId ||
                reader.GetString(4) != run.SelectedOutcome ||
                (reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6)) != run.PaperOrderId)
                throw new InvalidOperationException("Final run identity changed.");
            if (reader.GetString(0) == StrategyMarketPaperRunStatuses.Settled)
            {
                if (reader.IsDBNull(5) || reader.GetDecimal(5) != run.SettlementPrice)
                    throw new InvalidOperationException("Conflicting final run outcome.");
                return new(false, 0, 0);
            }
            if (reader.GetString(0) != StrategyMarketPaperRunStatuses.Entered)
                throw new InvalidOperationException("Final run is no longer entered.");
        }
        if (write.Position is { } position)
        {
            if (!evidence.Matches(position.ConditionId, position.AssetId, position.Outcome))
                throw new InvalidOperationException("Final position identity conflict.");
            await LockPaperPositionKeysAsync(connection, transaction, [position], [], cancellationToken);
            if (write.Settlement is { } settlement)
            {
                ValidateFinalSettlement(settlement, evidence);
                await AssertExistingSettlementAsync(connection, transaction, settlement, cancellationToken);
                await AddPaperPositionSettlementsBatchAsync(connection, transaction, [settlement], cancellationToken);
            }
            await UpsertPaperPositionsBatchAsync(connection, transaction, [position], cancellationToken);
        }
        var audit = System.Text.Json.Nodes.JsonNode.Parse(run.SkipDiagnosticsJson ?? "{}")?.AsObject()
            ?? throw new InvalidOperationException("Run audit must be an object.");
        audit["final_outcome"] = System.Text.Json.Nodes.JsonNode.Parse(evidence.ToAuditJson());
        run = run with { SkipDiagnosticsJson = audit.ToJsonString() };
        await UpdateStrategyMarketPaperRunAsync(connection, transaction, run, cancellationToken);
        await using (var retire = new NpgsqlCommand("UPDATE paper_algorithm_outcomes SET retired=true WHERE run_id=@Run", connection, transaction))
        {
            retire.Parameters.AddWithValue("Run", run.Id);
            await retire.ExecuteNonQueryAsync(cancellationToken);
        }
        StrategyLostCounterUpdateResult counters;
        await using (var update = new NpgsqlCommand("""
            UPDATE strategies SET paper_lost_counter=CASE WHEN paper_lost_coeff<=1 THEN 0
                ELSE paper_lost_counter + @Contribution END,updated_at_utc=@Now WHERE id=@Strategy
            RETURNING paper_lost_counter,live_lost_counter
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("Strategy", run.StrategyId);
            update.Parameters.AddWithValue("Contribution", run.SelectedAssetId == evidence.WinningAssetId ? -1 : 1);
            update.Parameters.AddWithValue("Now", run.UpdatedAtUtc.UtcDateTime);
            await using var reader = await update.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Strategy missing.");
            counters = new(true, reader.GetInt32(0), reader.GetInt32(1));
        }
        if (run.PaperOrderId is { } orderId)
            await ConfirmFinalOrdersAsync(connection, transaction, evidence, [orderId], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return counters;
    }

    public Task<int> PersistFinalPaperPositionsAsync(IReadOnlyList<PaperPositionSettlementWrite> writes,
        FinalMarketOutcomeEvidence evidence, Action<PaperSettlementPersistenceStageEvent>? observer,
        CancellationToken cancellationToken = default)
    {
        foreach (var write in writes) ValidateFinalSettlement(write.Settlement, evidence);
        return PersistPaperPositionSettlementBatchCoreAsync(writes, observer, cancellationToken, evidence);
    }

    private static void ValidateFinalSettlement(PaperPositionSettlement settlement, FinalMarketOutcomeEvidence evidence)
    {
        if (!evidence.Matches(settlement.ConditionId, settlement.AssetId, settlement.Outcome) ||
            settlement.WinningAssetId != evidence.WinningAssetId || settlement.WinningOutcome != evidence.WinningOutcome ||
            settlement.Won != (settlement.AssetId == evidence.WinningAssetId))
            throw new InvalidOperationException("Final settlement identity conflict.");
    }

    private static async Task AssertExistingSettlementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PaperPositionSettlement settlement, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM paper_position_settlements WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset
                AND (condition_id<>@Condition OR outcome<>@Outcome OR won<>@Won OR winning_asset_id IS DISTINCT FROM @Winner))
            """, connection, transaction);
        command.Parameters.AddWithValue("Wallet", settlement.CopiedTraderWallet);
        command.Parameters.AddWithValue("Asset", settlement.AssetId);
        command.Parameters.AddWithValue("Condition", settlement.ConditionId);
        command.Parameters.AddWithValue("Outcome", settlement.Outcome);
        command.Parameters.AddWithValue("Won", settlement.Won);
        command.Parameters.AddWithValue("Winner", settlement.WinningAssetId!);
        if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!) throw new InvalidOperationException("Conflicting stored final settlement.");
    }

    private static async Task<PaperOrder?> LockFinalPaperIdentityAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid orderId, CancellationToken cancellationToken,
        PaperPosition? expectedPosition = null)
    {
        var initial = await ReadPaperOrderForReconciliationAsync(connection, transaction, orderId, false, cancellationToken);
        if (initial is null) return null;
        if (expectedPosition is not null && (initial.CopiedTraderWallet != expectedPosition.CopiedTraderWallet ||
            initial.AssetId != expectedPosition.AssetId || initial.ConditionId != expectedPosition.ConditionId ||
            initial.Outcome != expectedPosition.Outcome))
            throw new InvalidOperationException("Final position/order identity conflict.");
        await LockPaperWalletsAsync(connection, transaction, [initial.CopiedTraderWallet], cancellationToken);
        await ReadPaperPositionForReconciliationAsync(connection, transaction, initial.CopiedTraderWallet,
            initial.AssetId, cancellationToken);
        var current = await ReadPaperOrderForReconciliationAsync(connection, transaction, orderId, true, cancellationToken);
        if (current is null || current.CopiedTraderWallet != initial.CopiedTraderWallet ||
            current.AssetId != initial.AssetId || current.ConditionId != initial.ConditionId ||
            current.Outcome != initial.Outcome || current.StrategyId != initial.StrategyId)
            throw new InvalidOperationException("Final Paper identity changed while acquiring locks.");
        return current;
    }

    // All predicates use scalar identities or a bounded set of related order IDs.
    // Keeping them outside the outer UPDATE prevents correlated whole-history scans.
    private const string FinalPaperReadinessSql = """
        SELECT EXISTS(SELECT 1 FROM paper_positions WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset
            AND (size_shares>0 OR condition_id<>@Condition OR outcome<>@Outcome))
        OR EXISTS(SELECT 1 FROM strategy_market_paper_runs WHERE paper_order_id=ANY(@RelatedIds)
            AND (status IN ('Entered','Resting') OR condition_id<>@Condition
                OR selected_asset_id IS DISTINCT FROM @Asset OR selected_outcome IS DISTINCT FROM @Outcome
                OR (status='Settled' AND settlement_price IS DISTINCT FROM @Price)))
        OR EXISTS(SELECT 1 FROM paper_position_settlements WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset
            AND (condition_id<>@Condition OR outcome<>@Outcome
                OR winning_asset_id IS DISTINCT FROM @Winner OR winning_outcome<>@WinningOutcome))
        OR EXISTS(SELECT 1 FROM live_orders WHERE paper_order_id=@Id AND
            (condition_id<>@Condition OR asset_id<>@Asset OR outcome<>@Outcome
                OR status IN ('Submitted','Live','Delayed','Unmatched','CancelRequested','CancelFailed','Error')
                OR (filled_size>0 AND settled_at_utc IS NULL)));
        """;

    private static async Task ConfirmFinalOrdersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FinalMarketOutcomeEvidence evidence, Guid[] ids, CancellationToken cancellationToken)
    {
        foreach (var id in ids.Distinct().Order())
        {
            // Callers already own the wallet/position locks before reaching order writes.
            var order = await ReadPaperOrderForReconciliationAsync(connection, transaction, id, true, cancellationToken);
            if (order is null || order.Confirmed || !evidence.Matches(order.ConditionId, order.AssetId, order.Outcome)) continue;
            var blocked = order.Status is PaperOrderStatus.Pending or PaperOrderStatus.PartiallyFilled;
            if (!blocked)
            {
                var relatedIds = new List<Guid>();
                await using (var related = new NpgsqlCommand("""
                    SELECT id FROM paper_orders WHERE copied_trader_wallet=@Wallet AND asset_id=@Asset
                        AND condition_id=@Condition AND strategy_id=@Strategy
                    """, connection, transaction))
                {
                    related.Parameters.AddWithValue("Wallet", order.CopiedTraderWallet);
                    related.Parameters.AddWithValue("Asset", order.AssetId);
                    related.Parameters.AddWithValue("Condition", order.ConditionId);
                    related.Parameters.AddWithValue("Strategy", order.StrategyId);
                    await using var reader = await related.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken)) relatedIds.Add(reader.GetGuid(0));
                }
                await using var readiness = new NpgsqlCommand(FinalPaperReadinessSql, connection, transaction);
                readiness.Parameters.AddWithValue("Id", id);
                readiness.Parameters.AddWithValue("Wallet", order.CopiedTraderWallet);
                readiness.Parameters.AddWithValue("Asset", order.AssetId);
                readiness.Parameters.AddWithValue("Condition", order.ConditionId);
                readiness.Parameters.AddWithValue("Outcome", order.Outcome);
                readiness.Parameters.AddWithValue("RelatedIds", relatedIds.ToArray());
                readiness.Parameters.AddWithValue("Price", order.AssetId == evidence.WinningAssetId ? 1m : 0m);
                readiness.Parameters.AddWithValue("Winner", evidence.WinningAssetId);
                readiness.Parameters.AddWithValue("WinningOutcome", evidence.WinningOutcome);
                blocked = (bool)(await readiness.ExecuteScalarAsync(cancellationToken))!;
            }
            await using var update = new NpgsqlCommand("""
                UPDATE paper_orders SET confirmed=@Confirmed,confirmation_evidence=CAST(@Evidence AS jsonb)
                WHERE id=@Id AND NOT confirmed;
                """, connection, transaction);
            update.Parameters.AddWithValue("Id", id);
            update.Parameters.AddWithValue("Confirmed", !blocked);
            update.Parameters.AddWithValue("Evidence", evidence.ToAuditJson());
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}