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

    private static async Task ConfirmFinalOrdersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        FinalMarketOutcomeEvidence evidence, Guid[] ids, CancellationToken cancellationToken)
    {
        // Confirm only completed identities whose final records already match this evidence.
        // Financial writes and this flag share the caller's transaction.
        await using var command = new NpgsqlCommand("""
            UPDATE paper_orders o SET confirmation_evidence=CAST(@Evidence AS jsonb)
            WHERE o.id=ANY(@Ids) AND NOT o.confirmed AND o.condition_id=@Condition
                AND EXISTS(SELECT 1 FROM unnest(@Tokens::text[],@Outcomes::text[]) m(asset,outcome)
                    WHERE m.asset=o.asset_id AND lower(m.outcome)=lower(o.outcome));
            UPDATE paper_orders o SET confirmed=true,
                confirmation_evidence=CAST(@Evidence AS jsonb)
            WHERE o.id=ANY(@Ids) AND NOT o.confirmed AND o.condition_id=@Condition
                AND o.status NOT IN ('Pending','PartiallyFilled')
                AND EXISTS(SELECT 1 FROM unnest(@Tokens::text[],@Outcomes::text[]) m(asset,outcome)
                    WHERE m.asset=o.asset_id AND lower(m.outcome)=lower(o.outcome))
                AND NOT EXISTS(SELECT 1 FROM paper_positions p WHERE p.copied_trader_wallet=o.copied_trader_wallet
                    AND p.asset_id=o.asset_id AND (p.size_shares>0 OR p.condition_id<>o.condition_id OR p.outcome<>o.outcome))
                AND NOT EXISTS(SELECT 1 FROM strategy_market_paper_runs r WHERE r.paper_order_id IN
                    (SELECT id FROM paper_orders p WHERE p.copied_trader_wallet=o.copied_trader_wallet AND p.asset_id=o.asset_id
                        AND p.condition_id=o.condition_id AND p.strategy_id=o.strategy_id)
                    AND (r.status IN ('Entered','Resting') OR r.condition_id<>o.condition_id
                        OR r.selected_asset_id IS DISTINCT FROM o.asset_id OR r.selected_outcome IS DISTINCT FROM o.outcome
                        OR (r.status='Settled' AND r.settlement_price IS DISTINCT FROM CASE WHEN o.asset_id=@Winner THEN 1::numeric ELSE 0::numeric END)))
                AND NOT EXISTS(SELECT 1 FROM paper_position_settlements s WHERE s.copied_trader_wallet=o.copied_trader_wallet
                    AND s.asset_id=o.asset_id AND (s.condition_id<>o.condition_id OR s.outcome<>o.outcome
                        OR s.winning_asset_id IS DISTINCT FROM @Winner OR s.winning_outcome<>@WinningOutcome))
                AND NOT EXISTS(SELECT 1 FROM live_orders l WHERE l.paper_order_id=o.id AND
                    (l.condition_id<>o.condition_id OR l.asset_id<>o.asset_id OR l.outcome<>o.outcome
                     OR l.status IN ('Submitted','Live','Delayed','Unmatched','CancelRequested','CancelFailed','Error')
                     OR (l.filled_size>0 AND l.settled_at_utc IS NULL)));
            """, connection, transaction);
        command.Parameters.AddWithValue("Ids", ids);
        command.Parameters.AddWithValue("Now", evidence.ObservedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("Evidence", evidence.ToAuditJson());
        command.Parameters.AddWithValue("Market", evidence.MarketId);
        command.Parameters.AddWithValue("Condition", evidence.ConditionId);
        command.Parameters.AddWithValue("Tokens", evidence.Tokens.ToArray());
        command.Parameters.AddWithValue("Outcomes", evidence.Outcomes.ToArray());
        command.Parameters.AddWithValue("Winner", evidence.WinningAssetId);
        command.Parameters.AddWithValue("WinningOutcome", evidence.WinningOutcome);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
