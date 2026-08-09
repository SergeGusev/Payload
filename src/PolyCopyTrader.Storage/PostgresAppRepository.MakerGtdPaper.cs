using Npgsql;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const string MakerGtdStrategyRunSelectColumns = "id, strategy_id, market_id, condition_id, market_slug, market_title, category,\n       market_start_utc, market_end_utc, detected_at_utc, entry_due_at_utc, status,\n       selected_asset_id, selected_outcome, entry_price, stake_usd, size_shares,\n       signal_id, paper_order_id, entered_at_utc, settlement_price, settlement_value_usd,\n       realized_pnl_usd, settled_at_utc, skip_reason, created_at_utc, updated_at_utc,\n       skip_diagnostics_json::text,\n       fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,\n       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd";

    public async Task<MakerGtdPaperMutationResult> TryApplyMakerGtdPaperFullFillAsync(
        MakerGtdPaperFullFillRequest request,
        CancellationToken cancellationToken = default)
    {
        MakerGtdPaperPersistenceTransitions.Validate(request);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var initialOrder = await ReadPaperOrderForReconciliationAsync(
            connection,
            transaction: null,
            request.FilledOrder.Id,
            forUpdate: false,
            cancellationToken);
        if (initialOrder is null)
        {
            return NotEligible(MakerGtdPaperMutationReasonCodes.OrderNotFound);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockPaperWalletsAsync(connection, transaction, [initialOrder.CopiedTraderWallet], cancellationToken);
        var currentPosition = await ReadPaperPositionForReconciliationAsync(
            connection,
            transaction,
            initialOrder.CopiedTraderWallet,
            initialOrder.AssetId,
            cancellationToken);
        var currentOrder = await ReadPaperOrderForReconciliationAsync(
            connection,
            transaction,
            request.FilledOrder.Id,
            forUpdate: true,
            cancellationToken);
        if (currentOrder is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(MakerGtdPaperMutationReasonCodes.OrderNotFound);
        }

        if (!string.Equals(currentOrder.CopiedTraderWallet, initialOrder.CopiedTraderWallet, StringComparison.Ordinal) ||
            !string.Equals(currentOrder.AssetId, initialOrder.AssetId, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(
                MakerGtdPaperMutationReasonCodes.FilledOrderShapeMismatch,
                currentOrder,
                paperPosition: currentPosition);
        }

        var currentRun = await ReadMakerGtdStrategyRunAsync(
            connection,
            transaction,
            request.EnteredRun.Id,
            cancellationToken);
        if (currentRun is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(
                MakerGtdPaperMutationReasonCodes.StrategyRunNotFound,
                currentOrder,
                paperPosition: currentPosition);
        }

        var existingFills = await ReadPaperFillsForReconciliationAsync(
            connection,
            transaction,
            currentOrder.Id,
            cancellationToken);
        if (MakerGtdPaperPersistenceTransitions.IsAlreadyApplied(
            currentOrder,
            currentRun,
            existingFills,
            request))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MakerGtdPaperMutationResult(
                MakerGtdPaperMutationOutcome.AlreadyApplied,
                MakerGtdPaperMutationReasonCodes.FillAlreadyApplied,
                currentOrder,
                currentRun,
                existingFills[0],
                currentPosition);
        }

        var ineligibilityReason = MakerGtdPaperPersistenceTransitions.GetFullFillIneligibilityReason(
            currentOrder,
            currentRun,
            existingFills,
            currentPosition,
            request);
        if (ineligibilityReason is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(ineligibilityReason, currentOrder, currentRun, paperPosition: currentPosition);
        }

        await InsertMakerGtdPaperFillAsync(connection, transaction, request.Fill, cancellationToken);
        await UpdatePaperOrderForReconciliationAsync(
            connection,
            transaction,
            request.FilledOrder,
            cancellationToken);
        await UpsertPaperPositionsBatchAsync(
            connection,
            transaction,
            [request.Position],
            cancellationToken);
        await UpdateMakerGtdStrategyRunAsync(
            connection,
            transaction,
            request.EnteredRun,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new MakerGtdPaperMutationResult(
            MakerGtdPaperMutationOutcome.Applied,
            MakerGtdPaperMutationReasonCodes.FillApplied,
            request.FilledOrder,
            request.EnteredRun,
            request.Fill,
            request.Position);
    }

    public async Task<MakerGtdPaperMutationResult> TryExpireMakerGtdPaperOrderAsync(
        MakerGtdPaperExpiryRequest request,
        CancellationToken cancellationToken = default)
    {
        MakerGtdPaperPersistenceTransitions.Validate(request);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var currentOrder = await ReadPaperOrderForReconciliationAsync(
            connection,
            transaction,
            request.ExpiredOrder.Id,
            forUpdate: true,
            cancellationToken);
        if (currentOrder is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(MakerGtdPaperMutationReasonCodes.OrderNotFound);
        }

        var currentRun = await ReadMakerGtdStrategyRunAsync(
            connection,
            transaction,
            request.SkippedRun.Id,
            cancellationToken);
        if (currentRun is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(MakerGtdPaperMutationReasonCodes.StrategyRunNotFound, currentOrder);
        }

        var existingFills = await ReadPaperFillsForReconciliationAsync(
            connection,
            transaction,
            currentOrder.Id,
            cancellationToken);
        if (MakerGtdPaperPersistenceTransitions.IsAlreadyApplied(
            currentOrder,
            currentRun,
            existingFills,
            request))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MakerGtdPaperMutationResult(
                MakerGtdPaperMutationOutcome.AlreadyApplied,
                MakerGtdPaperMutationReasonCodes.ExpiryAlreadyApplied,
                currentOrder,
                currentRun);
        }

        var ineligibilityReason = MakerGtdPaperPersistenceTransitions.GetExpiryIneligibilityReason(
            currentOrder,
            currentRun,
            existingFills,
            request);
        if (ineligibilityReason is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotEligible(ineligibilityReason, currentOrder, currentRun);
        }

        await UpdatePaperOrderForReconciliationAsync(
            connection,
            transaction,
            request.ExpiredOrder,
            cancellationToken);
        await UpdateMakerGtdStrategyRunAsync(
            connection,
            transaction,
            request.SkippedRun,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new MakerGtdPaperMutationResult(
            MakerGtdPaperMutationOutcome.Applied,
            MakerGtdPaperMutationReasonCodes.ExpiryApplied,
            request.ExpiredOrder,
            request.SkippedRun);
    }

    private static async Task<StrategyMarketPaperRun?> ReadMakerGtdStrategyRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyRunId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            "SELECT " + MakerGtdStrategyRunSelectColumns +
            "\nFROM strategy_market_paper_runs\nWHERE id = @Id\nLIMIT 1\nFOR UPDATE;");
        command.Transaction = transaction;
        command.Parameters.AddWithValue("Id", strategyRunId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadStrategyMarketPaperRun(reader) : null;
    }

    private static async Task InsertMakerGtdPaperFillAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PaperFill fill,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, """
INSERT INTO paper_fills (
    id, paper_order_id, price, size_shares, filled_at_utc, evidence, realized_pnl_usd,
    fee_usd, fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
    fee_exponent, fee_taker_only, fee_calculated_at_utc, net_realized_pnl_usd
) VALUES (
    @Id, @PaperOrderId, @Price, @SizeShares, @FilledAtUtc, @Evidence, @RealizedPnlUsd,
    @FeeUsd, @FeeAccountingStatus, @FeeLiquidityRole, @FeeCalculationSource, @FeeRate,
    @FeeExponent, @FeeTakerOnly, @FeeCalculatedAtUtc, @NetRealizedPnlUsd
);
""");
        command.Transaction = transaction;
        AddPaperFillParameters(command, fill);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Maker-GTD Paper fill insert did not affect exactly one row.");
        }
    }

    private static async Task UpdateMakerGtdStrategyRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StrategyMarketPaperRun run,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, """
UPDATE strategy_market_paper_runs
SET strategy_id = @StrategyId,
    market_id = @MarketId,
    condition_id = @ConditionId,
    market_slug = @MarketSlug,
    market_title = @MarketTitle,
    category = @Category,
    market_start_utc = @MarketStartUtc,
    market_end_utc = @MarketEndUtc,
    detected_at_utc = @DetectedAtUtc,
    entry_due_at_utc = @EntryDueAtUtc,
    status = @Status,
    selected_asset_id = @SelectedAssetId,
    selected_outcome = @SelectedOutcome,
    entry_price = @EntryPrice,
    stake_usd = @StakeUsd,
    size_shares = @SizeShares,
    signal_id = @SignalId,
    paper_order_id = @PaperOrderId,
    entered_at_utc = @EnteredAtUtc,
    settlement_price = @SettlementPrice,
    settlement_value_usd = @SettlementValueUsd,
    realized_pnl_usd = @RealizedPnlUsd,
    settled_at_utc = @SettledAtUtc,
    skip_reason = @SkipReason,
    skip_diagnostics_json = CAST(@SkipDiagnosticsJson AS jsonb),
    created_at_utc = @CreatedAtUtc,
    updated_at_utc = @UpdatedAtUtc,
    fee_usd = @FeeUsd,
    fee_accounting_status = @FeeAccountingStatus,
    fee_liquidity_role = @FeeLiquidityRole,
    fee_calculation_source = @FeeCalculationSource,
    fee_rate = @FeeRate,
    fee_exponent = @FeeExponent,
    fee_taker_only = @FeeTakerOnly,
    fee_calculated_at_utc = @FeeCalculatedAtUtc,
    net_realized_pnl_usd = @NetRealizedPnlUsd
WHERE id = @Id;
""");
        command.Transaction = transaction;
        AddMakerGtdStrategyRunParameters(command, run);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Maker-GTD strategy-run update did not affect exactly one row.");
        }
    }

    private static void AddMakerGtdStrategyRunParameters(
        NpgsqlCommand command,
        StrategyMarketPaperRun run)
    {
        AddStrategyMarketPaperRunParameters(command, run);
        command.Parameters["SkipDiagnosticsJson"].Value =
            (object?)run.SkipDiagnosticsJson ?? DBNull.Value;
    }

    private static MakerGtdPaperMutationResult NotEligible(
        string reasonCode,
        PaperOrder? paperOrder = null,
        StrategyMarketPaperRun? strategyRun = null,
        PaperFill? paperFill = null,
        PaperPosition? paperPosition = null)
    {
        return new MakerGtdPaperMutationResult(
            MakerGtdPaperMutationOutcome.NotEligible,
            reasonCode,
            paperOrder,
            strategyRun,
            paperFill,
            paperPosition);
    }
}
