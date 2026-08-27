using System.Data;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public sealed partial class PostgresAppRepository
{
    private const int HistoricalGrossNetParityMaximumPageSize = 250;
    private const string HistoricalGrossNetParityExactCurveSource =
        "polymarket-clob-v2-fd-shares-rate-price-curve-round5-away-from-zero-v1";
    private const string HistoricalGrossNetParityExactNoFeeSource =
        "polymarket-clob-v2-no-fd-no-base-fees-v1";
    private const string HistoricalGrossNetParityHistoricalModelPrefix =
        "historical-current-paper-model-v1:";

    public async Task<HistoricalGrossNetParityCandidatePage>
        LoadHistoricalGrossNetParityCandidatePageAsync(
            HistoricalGrossNetParityCandidatePageRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityCandidatePageRequest(request);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await ConfigureHistoricalGrossNetParityTransactionAsync(
            connection,
            transaction,
            request.CommandTimeoutSeconds,
            request.LockTimeoutMilliseconds,
            readOnly: true,
            cancellationToken);

        var selection = await LoadHistoricalGrossNetParityCandidateKeysAsync(
            connection,
            transaction,
            request,
            cancellationToken);
        var candidates = selection.Candidates;
        if (candidates.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new HistoricalGrossNetParityCandidatePage(
                HistoricalGrossNetParityReadStatus.Complete,
                [], [], [], [], [], [], [], [], [],
                selection.NextCursor,
                selection.ReachedBoundary,
                selection.Details);
        }

        var paperFills = await LoadHistoricalGrossNetParityPagePaperFillsAsync(
            connection, transaction, candidates, request.CommandTimeoutSeconds, cancellationToken);
        var paperPositions = await LoadHistoricalGrossNetParityPagePaperPositionsAsync(
            connection, transaction, candidates, request.CommandTimeoutSeconds, cancellationToken);
        var paperSettlements = await LoadHistoricalGrossNetParityPagePaperSettlementsAsync(
            connection, transaction, candidates, request.CommandTimeoutSeconds, cancellationToken);
        var paperRuns = await LoadHistoricalGrossNetParityPagePaperRunsAsync(
            connection, transaction, candidates, request.CommandTimeoutSeconds, cancellationToken);
        var sourceSelections = await LoadHistoricalGrossNetParityPageSourceSelectionsAsync(
            connection, transaction, candidates, request.CommandTimeoutSeconds, cancellationToken);
        var live = await LoadHistoricalGrossNetParityPageLiveTargetsAsync(
            connection, transaction, candidates, request, cancellationToken);
        var conflicts = await LoadHistoricalGrossNetParityPageConflictsAsync(
            connection, transaction, candidates, request, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        var last = candidates[^1];
        return new HistoricalGrossNetParityCandidatePage(
            HistoricalGrossNetParityReadStatus.Complete,
            candidates,
            live.Targets,
            paperFills,
            paperPositions,
            paperSettlements,
            paperRuns,
            sourceSelections,
            live.LookupRequests,
            conflicts,
            new HistoricalGrossNetParityCandidateCursor(
                last.StrategyRank,
                last.StrategyId,
                last.SourceOrder,
                last.OriginatedAtUtc,
                last.SourceId),
            selection.ReachedBoundary,
            selection.Details);
    }

    public async Task<IReadOnlyList<HistoricalGrossNetParityRankedStrategy>>
        LoadHistoricalGrossNetParityStrategyRankingAsync(
            HistoricalGrossNetParityStrategyRankingRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CommandTimeoutSeconds <= 0 || request.LockTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await ConfigureHistoricalGrossNetParityTransactionAsync(
            connection,
            transaction,
            request.CommandTimeoutSeconds,
            request.LockTimeoutMilliseconds,
            readOnly: true,
            cancellationToken);
        var ranking = await LoadHistoricalGrossNetParityStrategyRankingAsync(
            connection,
            transaction,
            request.CommandTimeoutSeconds,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ranking;
    }

    public async Task<HistoricalGrossNetParityDonorPreviewResult>
        LoadHistoricalGrossNetParityDonorPreviewAsync(
            HistoricalGrossNetParityDonorPreviewRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityDonorPreviewRequest(request);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        await ConfigureHistoricalGrossNetParityTransactionAsync(
            connection,
            transaction,
            request.CommandTimeoutSeconds,
            request.LockTimeoutMilliseconds,
            readOnly: true,
            cancellationToken);

        var currentTargetHash = await ReadHistoricalGrossNetParityTargetHashAsync(
            connection,
            transaction,
            request.TargetSourceKind,
            request.TargetSourceId,
            request.CommandTimeoutSeconds,
            forUpdate: false,
            cancellationToken);
        if (!string.Equals(currentTargetHash, request.ExpectedTargetTupleHash, StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new HistoricalGrossNetParityDonorPreviewResult(
                HistoricalGrossNetParityReadStatus.InvariantConflict,
                [],
                request.CandidateOffset,
                false,
                "The target stable tuple changed before donor preview.");
        }

        var end = Math.Min(
            request.OrderedCandidates.Count,
            checked(request.CandidateOffset + request.PageSize));
        var aggregates = new List<HistoricalGrossNetParityDonorCandidateAggregate>(
            end - request.CandidateOffset);
        try
        {
            for (var index = request.CandidateOffset; index < end; index++)
            {
                aggregates.Add(await LoadHistoricalGrossNetParityDonorAggregateStreamingAsync(
                    connection,
                    transaction,
                    request.TargetSourceKind,
                    request.OrderedCandidates[index],
                    request.PageSize,
                    request.CommandTimeoutSeconds,
                    cancellationToken));
            }
        }
        catch (HistoricalGrossNetParitySequentialDonorPlanException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new HistoricalGrossNetParityDonorPreviewResult(
                HistoricalGrossNetParityReadStatus.DeferredOperational,
                [],
                request.CandidateOffset,
                false,
                exception.Message);
        }

        await transaction.CommitAsync(cancellationToken);
        return new HistoricalGrossNetParityDonorPreviewResult(
            HistoricalGrossNetParityReadStatus.Complete,
            aggregates,
            end,
            end == request.OrderedCandidates.Count);
    }

    public async Task<HistoricalGrossNetParityApplyResult>
        TryApplyHistoricalGrossNetParityPaperDecisionAsync(
            HistoricalGrossNetParityPaperDecisionRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityPaperDecisionRequest(request);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await ConfigureHistoricalGrossNetParityTransactionAsync(
                connection,
                transaction,
                request.CommandTimeoutSeconds,
                request.LockTimeoutMilliseconds,
                readOnly: false,
                cancellationToken);

            var result = await TryApplyHistoricalGrossNetParityPaperDecisionCoreAsync(
                connection,
                transaction,
                request,
                cancellationToken);
            if (result.Status is HistoricalGrossNetParityApplyStatus.Applied or
                HistoricalGrossNetParityApplyStatus.TerminalNoOp)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (PostgresException exception) when (IsHistoricalGrossNetParityDeferred(exception))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredOperational,
                false,
                request.Target.TargetTupleHash,
                null,
                exception.SqlState);
        }
        catch (HistoricalGrossNetParitySequentialDonorPlanException exception)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredOperational,
                false,
                request.Target.TargetTupleHash,
                null,
                exception.Message);
        }
    }

    public async Task<HistoricalGrossNetParityApplyResult>
        TryApplyHistoricalGrossNetParityLiveAccountingAsync(
            HistoricalGrossNetParityLiveAccountingRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityLiveAccountingRequest(request);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await ConfigureHistoricalGrossNetParityTransactionAsync(
                connection,
                transaction,
                request.CommandTimeoutSeconds,
                request.LockTimeoutMilliseconds,
                readOnly: false,
                cancellationToken);
            var result = await TryApplyHistoricalGrossNetParityLiveAccountingCoreAsync(
                connection,
                transaction,
                request,
                cancellationToken);
            if (result.Status is HistoricalGrossNetParityApplyStatus.Applied or
                HistoricalGrossNetParityApplyStatus.TerminalNoOp)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (PostgresException exception) when (IsHistoricalGrossNetParityDeferred(exception))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredOperational,
                false,
                request.Target.TargetTupleHash,
                request.Target.Ownership,
                exception.SqlState);
        }
        catch (HistoricalGrossNetParitySequentialDonorPlanException exception)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredOperational,
                false,
                request.Target.TargetTupleHash,
                request.Target.Ownership,
                exception.Message);
        }
    }

    public async Task<HistoricalGrossNetParityLiveBalanceResult>
        TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
            HistoricalGrossNetParityLiveBalanceRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityLiveBalanceRequest(request);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await ConfigureHistoricalGrossNetParityTransactionAsync(
                connection,
                transaction,
                request.CommandTimeoutSeconds,
                request.LockTimeoutMilliseconds,
                readOnly: false,
                cancellationToken);
            var result = await TryApplyHistoricalGrossNetParityEarliestLiveBalanceCoreAsync(
                connection,
                transaction,
                request,
                cancellationToken);
            if (result.Status is HistoricalGrossNetParityApplyStatus.Applied or
                HistoricalGrossNetParityApplyStatus.TerminalNoOp)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (PostgresException exception) when (IsHistoricalGrossNetParityDeferred(exception))
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.DeferredOperational,
                request.LiveOrderId,
                HistoricalGrossNetParityOwnership.Pending,
                null, null, null, false,
                exception.SqlState);
        }
    }

    public async Task<HistoricalGrossNetParityVenueRevisionResult>
        ApplyHistoricalGrossNetParityVenueRevisionAsync(
            HistoricalGrossNetParityVenueRevisionRequest request,
            CancellationToken cancellationToken = default)
    {
        ValidateHistoricalGrossNetParityVenueRevisionRequest(request);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            await ConfigureHistoricalGrossNetParityTransactionAsync(
                connection,
                transaction,
                30,
                2_000,
                readOnly: false,
                cancellationToken);
            var result = await ApplyHistoricalGrossNetParityVenueRevisionCoreAsync(
                connection,
                transaction,
                request,
                cancellationToken);
            if (result.Applied || result.Idempotent)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return result;
        }
        catch (PostgresException exception) when (IsHistoricalGrossNetParityDeferred(exception))
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, false, HistoricalGrossNetParityOwnership.None,
                null, null, null, false, exception.SqlState);
        }
    }

    private static async Task<HistoricalGrossNetParityApplyResult>
        TryApplyHistoricalGrossNetParityPaperDecisionCoreAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityPaperDecisionRequest request,
            CancellationToken cancellationToken)
    {
        if (await HistoricalGrossNetParityAuditExistsAsync(
                connection, transaction, request.Target.SourceKind, request.Target.SourceId,
                request.CalculationVersion, HistoricalGrossNetParityOperationKind.AccountingDecision,
                cancellationToken))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.TerminalNoOp,
                false,
                await ReadHistoricalGrossNetParityTargetHashAsync(
                    connection, transaction, request.Target.SourceKind, request.Target.SourceId,
                    request.CommandTimeoutSeconds, false, cancellationToken) ?? request.Target.TargetTupleHash,
                null);
        }

        var binding = await ValidateHistoricalGrossNetParityTargetBindingAsync(
            connection, transaction, request.Target, request.CommandTimeoutSeconds, cancellationToken);
        if (binding is not null)
        {
            return binding;
        }

        if (!await HistoricalGrossNetParityDecisionSelectionMatchesAsync(
                connection, transaction, request.Target.SourceKind, request.Decision,
                request.OrderedCandidates, request.DonorPageSize, request.CommandTimeoutSeconds,
                cancellationToken))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                request.Target.TargetTupleHash, null,
                "The complete donor selection changed inside the mutation transaction.");
        }

        var oldPayload = request.Target.CanonicalPayloadJson;
        var resultingRowVersion = request.Target.RowVersion;
        if (request.Decision.DecisionKind != HistoricalGrossNetParityDecisionKind.ExistingExactPreserved)
        {
            resultingRowVersion = await UpdateHistoricalGrossNetParityPaperAccountingAsync(
                connection, transaction, request.Target, request.Decision,
                request.CommandTimeoutSeconds, cancellationToken);
            if (resultingRowVersion < 0)
            {
                return new HistoricalGrossNetParityApplyResult(
                    HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                    request.Target.TargetTupleHash, null,
                    "The Paper target changed before its accounting CAS.");
            }
        }

        var newPayload = await ReadHistoricalGrossNetParityTargetPayloadAsync(
            connection, transaction, request.Target.SourceKind, request.Target.SourceId,
            request.CommandTimeoutSeconds, cancellationToken);
        if (newPayload is null)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                request.Target.TargetTupleHash, null,
                "The Paper target disappeared after its accounting CAS.");
        }

        await InsertHistoricalGrossNetParityAuditAsync(
            connection, transaction,
            request.Target.SourceKind, request.Target.SourceId, request.Target.StrategyId,
            request.CalculationVersion, HistoricalGrossNetParityOperationKind.AccountingDecision,
            request.Decision.EvidenceVersion, request.Decision.DecisionKind,
            oldPayload, newPayload,
            CreateHistoricalGrossNetParityDecisionEvidencePayload(
                request.Target, request.Decision.EvidenceJson),
            request.Target.RowVersion, resultingRowVersion,
            baselineKind: null, nominalGross: null, nominalNet: null,
            desiredCumulativeAdjustment: null, priorActual: null, requestedDelta: null,
            balanceBefore: null, balanceAfter: null, actualDelta: null,
            newActual: null, residual: null, clampApplied: null,
            authorityId: null, authorityOrderKey: null, supersedesEvidenceVersion: null,
            cancellationToken);
        await QueueHistoricalGrossNetParityReconciliationAsync(
            connection, transaction, request.Target.StrategyId, cancellationToken);
        var currentHash = HashHistoricalGrossNetParityPayload(newPayload);
        return new HistoricalGrossNetParityApplyResult(
            HistoricalGrossNetParityApplyStatus.Applied, true, currentHash, null);
    }

    private static async Task<HistoricalGrossNetParityApplyResult>
        TryApplyHistoricalGrossNetParityLiveAccountingCoreAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityLiveAccountingRequest request,
            CancellationToken cancellationToken)
    {
        var existingState = await ReadHistoricalGrossNetParityLiveAuditStateAsync(
            connection, transaction, request.Target.SourceId, request.CalculationVersion,
            request.CommandTimeoutSeconds, cancellationToken);
        if (existingState.DecisionExists && existingState.BaselineExists &&
            existingState.Ownership is HistoricalGrossNetParityOwnership.Pending or
                HistoricalGrossNetParityOwnership.Completed)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.TerminalNoOp,
                false,
                existingState.CurrentTargetHash ?? request.Target.TargetTupleHash,
                existingState.Ownership);
        }
        if (existingState.DecisionExists || existingState.BaselineExists ||
            existingState.Ownership != HistoricalGrossNetParityOwnership.None)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.InvariantConflict,
                false,
                existingState.CurrentTargetHash ?? request.Target.TargetTupleHash,
                existingState.Ownership,
                "Live parity ownership, immutable baseline, and accounting decision are incomplete or inconsistent.");
        }

        var binding = await ValidateHistoricalGrossNetParityTargetBindingAsync(
            connection, transaction, request.Target, request.CommandTimeoutSeconds, cancellationToken);
        if (binding is not null)
        {
            return binding;
        }

        if (request.Target.Ownership != HistoricalGrossNetParityOwnership.None)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                request.Target.TargetTupleHash, existingState.Ownership,
                "The Live target ownership/baseline changed before Transaction A.");
        }

        if (!TryClassifyHistoricalGrossNetParityBaseline(request.Target, out var baselineKind,
                out var nominalNet))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.InvariantConflict, false,
                request.Target.TargetTupleHash, request.Target.Ownership,
                "The original Live balance/accounting tuple has no approved baseline classification.");
        }

        if (!await HistoricalGrossNetParityDecisionSelectionMatchesAsync(
                connection, transaction, request.Target.SourceKind, request.Decision,
                request.OrderedCandidates, request.DonorPageSize, request.CommandTimeoutSeconds,
                cancellationToken))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                request.Target.TargetTupleHash, request.Target.Ownership,
                "The complete donor selection changed inside Live Transaction A.");
        }

        var desired = baselineKind switch
        {
            HistoricalGrossNetParityBaselineEffectKind.None => request.Decision.NetPnlUsd,
            HistoricalGrossNetParityBaselineEffectKind.LegacyGrossApplied =>
                request.Decision.NetPnlUsd - request.Target.GrossPnlUsd,
            HistoricalGrossNetParityBaselineEffectKind.NetAlreadyApplied =>
                request.Decision.NetPnlUsd - nominalNet!.Value,
            _ => throw new ArgumentOutOfRangeException()
        };
        var oldPayload = request.Target.CanonicalPayloadJson;
        await InsertHistoricalGrossNetParityAuditAsync(
            connection, transaction,
            HistoricalGrossNetParitySourceKind.LiveOrder, request.Target.SourceId,
            request.Target.StrategyId, request.CalculationVersion,
            HistoricalGrossNetParityOperationKind.AccountingBaseline,
            "baseline:" + request.Target.BindingHash,
            decisionKind: null,
            oldPayload, oldPayload,
            JsonSerializer.Serialize(new
            {
                schema = "HistoricalGrossNetParityAccountingBaselineV1",
                binding_hash = request.Target.BindingHash,
                baseline_effect_kind = baselineKind.ToString()
            }),
            request.Target.RowVersion, request.Target.RowVersion,
            baselineKind, request.Target.GrossPnlUsd, nominalNet,
            desiredCumulativeAdjustment: null, priorActual: null, requestedDelta: null,
            balanceBefore: null, balanceAfter: null, actualDelta: null,
            newActual: null, residual: null, clampApplied: null,
            authorityId: null, authorityOrderKey: null, supersedesEvidenceVersion: null,
            cancellationToken);

        var resultingRowVersion = await UpdateHistoricalGrossNetParityLiveAccountingAsync(
            connection, transaction, request.Target, request.Decision,
            HistoricalGrossNetParityOwnership.None,
            HistoricalGrossNetParityOwnership.Pending,
            request.CommandTimeoutSeconds,
            cancellationToken);
        if (resultingRowVersion < 0)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                request.Target.TargetTupleHash, request.Target.Ownership,
                "The Live target changed before Transaction A accounting CAS.");
        }

        var newPayload = await ReadHistoricalGrossNetParityTargetPayloadAsync(
            connection, transaction, HistoricalGrossNetParitySourceKind.LiveOrder,
            request.Target.SourceId, request.CommandTimeoutSeconds, cancellationToken) ?? "{}";
        await InsertHistoricalGrossNetParityAuditAsync(
            connection, transaction,
            HistoricalGrossNetParitySourceKind.LiveOrder, request.Target.SourceId,
            request.Target.StrategyId, request.CalculationVersion,
            HistoricalGrossNetParityOperationKind.AccountingDecision,
            request.Decision.EvidenceVersion, request.Decision.DecisionKind,
            oldPayload, newPayload,
            CreateHistoricalGrossNetParityDecisionEvidencePayload(
                request.Target, request.Decision.EvidenceJson),
            request.Target.RowVersion, resultingRowVersion,
            baselineKind, request.Target.GrossPnlUsd, nominalNet,
            desired, priorActual: 0m, requestedDelta: null,
            balanceBefore: null, balanceAfter: null, actualDelta: null,
            newActual: 0m, residual: desired, clampApplied: null,
            authorityId: null, authorityOrderKey: null, supersedesEvidenceVersion: null,
            cancellationToken);
        await QueueHistoricalGrossNetParityReconciliationAsync(
            connection, transaction, request.Target.StrategyId, cancellationToken);
        return new HistoricalGrossNetParityApplyResult(
            HistoricalGrossNetParityApplyStatus.Applied, true,
            HashHistoricalGrossNetParityPayload(newPayload),
            HistoricalGrossNetParityOwnership.Pending);
    }

    private sealed record HistoricalGrossNetParityLiveBalanceState(
        Guid LiveOrderId,
        Guid StrategyId,
        HistoricalGrossNetParityOwnership Ownership,
        long RowVersion,
        decimal GrossPnlUsd,
        decimal? NetPnlUsd,
        DateTimeOffset SettledAtUtc,
        string CurrentPayloadJson,
        HistoricalGrossNetParityBaselineEffectKind BaselineKind,
        decimal NominalGrossPnlUsd,
        decimal? NominalNetPnlUsd,
        decimal DesiredCumulativeAdjustment,
        string LatestEvidenceVersion,
        decimal PriorActualCumulativeAdjustment);

    private static async Task<HistoricalGrossNetParityLiveBalanceResult>
        TryApplyHistoricalGrossNetParityEarliestLiveBalanceCoreAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityLiveBalanceRequest request,
            CancellationToken cancellationToken)
    {
        var state = await ReadHistoricalGrossNetParityLiveBalanceStateAsync(
            connection, transaction, request, cancellationToken);
        if (state is null)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, request.LiveOrderId,
                HistoricalGrossNetParityOwnership.None, null, null, null, false,
                "The Live target or its durable baseline/decision is missing.");
        }

        if (state.Ownership == HistoricalGrossNetParityOwnership.Completed)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.TerminalNoOp, request.LiveOrderId,
                state.Ownership, 0m, 0m, 0m, false);
        }

        if (state.Ownership != HistoricalGrossNetParityOwnership.Pending)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, request.LiveOrderId,
                state.Ownership, null, null, null, false,
                "Only a Pending parity-owned Live row may run Transaction B.");
        }

        var earliest = await ReadEarliestHistoricalGrossNetParityPendingLiveOrderAsync(
            connection, transaction, request.StrategyId, request.CommandTimeoutSeconds,
            cancellationToken);
        if (earliest != request.LiveOrderId)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.NotEarliest, request.LiveOrderId,
                state.Ownership, null, null, null, false,
                "An earlier eligible not-Completed same-strategy Live row must complete first.");
        }

        var balanceBefore = await ReadHistoricalGrossNetParityStrategyBalanceForUpdateAsync(
            connection, transaction, request.StrategyId, request.CommandTimeoutSeconds,
            cancellationToken);
        if (balanceBefore is null)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.InvariantConflict, request.LiveOrderId,
                state.Ownership, null, null, null, false,
                "The target strategy is missing.");
        }

        var requestedDelta = state.DesiredCumulativeAdjustment - state.PriorActualCumulativeAdjustment;
        var unclamped = balanceBefore.Value + requestedDelta;
        var balanceAfter = Math.Clamp(unclamped, 0m, 100m);
        var actualDelta = balanceAfter - balanceBefore.Value;
        var newActual = state.PriorActualCumulativeAdjustment + actualDelta;
        var residual = state.DesiredCumulativeAdjustment - newActual;
        var clamp = balanceAfter != unclamped;
        await UpdateHistoricalGrossNetParityStrategyBalanceAsync(
            connection, transaction, request.StrategyId, balanceAfter,
            request.CommandTimeoutSeconds, cancellationToken);

        var resultingRowVersion = await CompleteHistoricalGrossNetParityLiveOwnershipAsync(
            connection, transaction, request.LiveOrderId, state.RowVersion,
            request.CommandTimeoutSeconds, cancellationToken);
        if (resultingRowVersion < 0)
        {
            return new HistoricalGrossNetParityLiveBalanceResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, request.LiveOrderId,
                state.Ownership, null, null, null, false,
                "The Pending Live target changed before Transaction B CAS.");
        }

        var newPayload = await ReadHistoricalGrossNetParityTargetPayloadAsync(
            connection, transaction, HistoricalGrossNetParitySourceKind.LiveOrder,
            request.LiveOrderId, request.CommandTimeoutSeconds, cancellationToken) ?? "{}";
        await InsertHistoricalGrossNetParityAuditAsync(
            connection, transaction,
            HistoricalGrossNetParitySourceKind.LiveOrder, request.LiveOrderId,
            request.StrategyId, request.CalculationVersion,
            HistoricalGrossNetParityOperationKind.InitialBalanceApplication,
            "initial-balance:" + state.LatestEvidenceVersion,
            decisionKind: null,
            state.CurrentPayloadJson, newPayload,
            JsonSerializer.Serialize(new
            {
                schema = "HistoricalGrossNetParityInitialBalanceApplicationV1",
                latest_evidence_version = state.LatestEvidenceVersion
            }),
            state.RowVersion, resultingRowVersion,
            state.BaselineKind, state.NominalGrossPnlUsd, state.NominalNetPnlUsd,
            state.DesiredCumulativeAdjustment, state.PriorActualCumulativeAdjustment,
            requestedDelta, balanceBefore, balanceAfter, actualDelta, newActual, residual, clamp,
            authorityId: null, authorityOrderKey: null, supersedesEvidenceVersion: null,
            cancellationToken);
        await QueueHistoricalGrossNetParityReconciliationAsync(
            connection, transaction, request.StrategyId, cancellationToken);
        return new HistoricalGrossNetParityLiveBalanceResult(
            HistoricalGrossNetParityApplyStatus.Applied, request.LiveOrderId,
            HistoricalGrossNetParityOwnership.Completed,
            requestedDelta, actualDelta, residual, true);
    }

    private static void ValidateHistoricalGrossNetParityCandidatePageRequest(
        HistoricalGrossNetParityCandidatePageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHistoricalGrossNetParityCommonOptions(
            request.CutoffUtc, request.PageSize,
            request.CommandTimeoutSeconds, request.LockTimeoutMilliseconds,
            request.CalculationVersion);
        if (!Enum.IsDefined(request.Phase))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Strategy is null)
        {
            throw new ArgumentException(
                "Historical parity candidate discovery must be scoped to one strategy.",
                nameof(request));
        }

        if (request.StrategyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Strategy.StrategyCode) ||
            request.Strategy.StrategyRank <= 0)
        {
            throw new ArgumentException(
                "A strategy-scoped historical parity request requires a valid ranked strategy.",
                nameof(request));
        }

        if (request.After is { } after && after.StrategyId != request.StrategyId)
        {
            throw new ArgumentException(
                "A strategy-scoped cursor must belong to the selected strategy.",
                nameof(request));
        }

        if (request.After is { SourceOrder: < 1 or > 5 })
        {
            throw new ArgumentException(
                "A strategy-scoped historical parity cursor must identify a canonical source order.",
                nameof(request));
        }
    }

    private static void ValidateHistoricalGrossNetParityDonorPreviewRequest(
        HistoricalGrossNetParityDonorPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHistoricalGrossNetParitySourceKind(request.TargetSourceKind, allowLive: true);
        ValidateHistoricalGrossNetParityCandidates(request.OrderedCandidates);
        if (request.TargetSourceId == Guid.Empty || request.TargetStrategyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ExpectedTargetTupleHash) ||
            request.CandidateOffset < 0 || request.CandidateOffset > request.OrderedCandidates.Count ||
            request.PageSize <= 0 || request.PageSize > HistoricalGrossNetParityMaximumPageSize ||
            request.CommandTimeoutSeconds <= 0 || request.LockTimeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    private static void ValidateHistoricalGrossNetParityPaperDecisionRequest(
        HistoricalGrossNetParityPaperDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHistoricalGrossNetParityCommonOptions(
            request.CutoffUtc, request.DonorPageSize,
            request.CommandTimeoutSeconds, request.LockTimeoutMilliseconds,
            request.CalculationVersion);
        ValidateHistoricalGrossNetParitySourceKind(request.Target.SourceKind, allowLive: false);
        ValidateHistoricalGrossNetParityTargetAndDecision(
            request.Target, request.Decision, request.CutoffUtc);
        ValidateHistoricalGrossNetParityCandidates(request.OrderedCandidates);
    }

    private static void ValidateHistoricalGrossNetParityLiveAccountingRequest(
        HistoricalGrossNetParityLiveAccountingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHistoricalGrossNetParityCommonOptions(
            request.CutoffUtc, request.DonorPageSize,
            request.CommandTimeoutSeconds, request.LockTimeoutMilliseconds,
            request.CalculationVersion);
        if (request.Target.SourceKind != HistoricalGrossNetParitySourceKind.LiveOrder)
        {
            throw new ArgumentException("Live Transaction A requires a LiveOrder target.", nameof(request));
        }
        ValidateHistoricalGrossNetParityTargetAndDecision(
            request.Target, request.Decision, request.CutoffUtc);
        ValidateHistoricalGrossNetParityCandidates(request.OrderedCandidates);
    }

    private static void ValidateHistoricalGrossNetParityLiveBalanceRequest(
        HistoricalGrossNetParityLiveBalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateHistoricalGrossNetParityCommonOptions(
            request.CutoffUtc, 1, request.CommandTimeoutSeconds,
            request.LockTimeoutMilliseconds, request.CalculationVersion);
        if (request.StrategyId == Guid.Empty || request.LiveOrderId == Guid.Empty)
        {
            throw new ArgumentException("Transaction B requires nonempty strategy/order identities.", nameof(request));
        }
    }

    private static void ValidateHistoricalGrossNetParityVenueRevisionRequest(
        HistoricalGrossNetParityVenueRevisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.LiveOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AuthorityId) ||
            string.IsNullOrWhiteSpace(request.AuthorityOrderKey) ||
            string.IsNullOrWhiteSpace(request.EvidenceVersion) ||
            string.IsNullOrWhiteSpace(request.SupersedesEvidenceVersion) ||
            request.FeeUsd < 0m ||
            string.IsNullOrWhiteSpace(request.FeeCalculationSource) ||
            !Enum.TryParse<FeeLiquidityRole>(request.FeeLiquidityRole, false, out _) ||
            request.ReportedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Venue revision has an invalid authority, accounting, or evidence tuple.", nameof(request));
        }
        _ = NormalizeHistoricalGrossNetParityJson(request.EvidenceJson);
    }

    private static void ValidateHistoricalGrossNetParityCommonOptions(
        DateTimeOffset cutoffUtc,
        int pageSize,
        int commandTimeoutSeconds,
        int lockTimeoutMilliseconds,
        string calculationVersion)
    {
        if (cutoffUtc != HistoricalGrossNetParityConstants.CutoffUtc ||
            pageSize <= 0 || pageSize > HistoricalGrossNetParityMaximumPageSize ||
            commandTimeoutSeconds <= 0 || lockTimeoutMilliseconds <= 0 ||
            !string.Equals(calculationVersion,
                HistoricalGrossNetParityConstants.CalculationVersion,
                StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(cutoffUtc),
                "Historical Gross/Net parity options must match the approved closed configuration.");
        }
    }

    private static void ValidateHistoricalGrossNetParitySourceKind(
        HistoricalGrossNetParitySourceKind sourceKind,
        bool allowLive)
    {
        if (sourceKind is not (
                HistoricalGrossNetParitySourceKind.PaperRun or
                HistoricalGrossNetParitySourceKind.PaperPosition or
                HistoricalGrossNetParitySourceKind.PaperSettlement or
                HistoricalGrossNetParitySourceKind.PaperSellFill) &&
            (!allowLive || sourceKind != HistoricalGrossNetParitySourceKind.LiveOrder))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }
    }

    private static void ValidateHistoricalGrossNetParityCandidates(
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count > 10_000 ||
            candidates.Any(value => value.StrategyId == Guid.Empty ||
                                    value.MatcherOrder < 0 || value.Tier < BigInteger.Zero ||
                                    string.IsNullOrWhiteSpace(value.CanonicalMatcherOrderKey)) ||
            candidates.Select(value => value.StrategyId).Distinct().Count() != candidates.Count)
        {
            throw new ArgumentException("Donor candidate descriptors are invalid or not unique.", nameof(candidates));
        }
    }

    private static void ValidateHistoricalGrossNetParityTargetAndDecision(
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityAccountingDecisionV1 decision,
        DateTimeOffset cutoffUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(decision);
        if (target.SourceId == Guid.Empty || target.StrategyId == Guid.Empty ||
            target.RowVersion < 0 || target.OriginatedAtUtc >= cutoffUtc ||
            (target.GrossRoiBasisUsd < 0m &&
             decision.DecisionKind != HistoricalGrossNetParityDecisionKind.NonpositiveBasis) ||
            target.ProvedComponentFloorUsd < 0m ||
            string.IsNullOrWhiteSpace(target.TargetTupleHash) ||
            string.IsNullOrWhiteSpace(target.LineageHash) ||
            string.IsNullOrWhiteSpace(target.ComponentHash) ||
            string.IsNullOrWhiteSpace(target.BindingHash) ||
            decision.StoredFeeUsd < 0m || decision.ContributionEffectiveFeeUsd < 0m ||
            decision.ComponentFloorUsd < 0m ||
            decision.ComponentFloorUsd != target.ProvedComponentFloorUsd ||
            (decision.DecisionKind is not (
                 HistoricalGrossNetParityDecisionKind.Fixed0p033 or
                 HistoricalGrossNetParityDecisionKind.Fixed0p0333) &&
             decision.ContributionEffectiveFeeUsd < decision.ComponentFloorUsd) ||
            decision.NetPnlUsd != target.GrossPnlUsd - decision.ContributionEffectiveFeeUsd ||
            string.IsNullOrWhiteSpace(decision.EvidenceVersion) ||
            !Enum.TryParse<FeeAccountingStatus>(decision.FeeAccountingStatus, false, out var status) ||
            status is not (FeeAccountingStatus.Calculated or FeeAccountingStatus.VenueReported) ||
            !Enum.TryParse<FeeLiquidityRole>(decision.FeeLiquidityRole, false, out _))
        {
            throw new ArgumentException("Target/decision accounting invariants are invalid.");
        }

        if (target.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill)
        {
            if (decision.StoredFeeUsd > decision.ContributionEffectiveFeeUsd)
            {
                throw new ArgumentException("A SELL exit Fee cannot exceed contribution-effective Fee.");
            }
        }
        else if (decision.StoredFeeUsd != decision.ContributionEffectiveFeeUsd)
        {
            throw new ArgumentException("Non-SELL stored Fee must equal contribution-effective Fee.");
        }

        if (target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder &&
            decision.CostBasisUsd != Math.Round(
                target.GrossRoiBasisUsd + decision.ContributionEffectiveFeeUsd,
                8,
                MidpointRounding.AwayFromZero))
        {
            throw new ArgumentException("Live derived cost basis must equal frozen Gross basis plus Fee.");
        }

        var requiresDonorSelection = decision.DecisionKind is
            HistoricalGrossNetParityDecisionKind.DonorRatio or
            HistoricalGrossNetParityDecisionKind.Fixed0p033;
        var permitsLegacyDonorSelection =
            decision.DecisionKind == HistoricalGrossNetParityDecisionKind.Fixed0p0333;
        if ((requiresDonorSelection && decision.DonorDecision is null) ||
            (!requiresDonorSelection && !permitsLegacyDonorSelection && decision.DonorDecision is not null))
        {
            throw new ArgumentException(
                "Donor decisions require selection proof; direct fixed decisions do not.");
        }
        _ = NormalizeHistoricalGrossNetParityJson(decision.EvidenceJson);
    }

    private static async Task ConfigureHistoricalGrossNetParityTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int commandTimeoutSeconds,
        int lockTimeoutMilliseconds,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            readOnly
                ? "SET TRANSACTION READ ONLY; SELECT " +
                  "set_config('statement_timeout', @StatementTimeout, true), " +
                  "set_config('lock_timeout', @LockTimeout, true);"
                : "SELECT set_config('statement_timeout', @StatementTimeout, true), " +
                  "set_config('lock_timeout', @LockTimeout, true);",
            connection,
            transaction);
        command.Parameters.AddWithValue(
            "StatementTimeout",
            checked(commandTimeoutSeconds * 1_000).ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "LockTimeout",
            lockTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeHistoricalGrossNetParityJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return "{}";
        using var document = JsonDocument.Parse(payload);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string HashHistoricalGrossNetParityPayload(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();

    private static bool IsHistoricalGrossNetParityExactLocalSource(
        string calculationSource,
        decimal fee,
        string liquidityRole,
        decimal? feeRate,
        int? feeExponent,
        bool? feeTakerOnly)
    {
        var source = calculationSource.StartsWith(
                HistoricalGrossNetParityHistoricalModelPrefix,
                StringComparison.Ordinal)
            ? calculationSource[HistoricalGrossNetParityHistoricalModelPrefix.Length..]
            : calculationSource;
        if (string.Equals(source, HistoricalGrossNetParityExactNoFeeSource, StringComparison.Ordinal))
        {
            return fee == 0m;
        }

        return string.Equals(source, HistoricalGrossNetParityExactCurveSource, StringComparison.Ordinal) &&
               !string.Equals(liquidityRole, nameof(FeeLiquidityRole.Unknown), StringComparison.Ordinal) &&
               feeRate is >= 0m && feeExponent is >= 0 && feeTakerOnly is not null;
    }

    private sealed record HistoricalGrossNetParityLockedTarget(
        long RowVersion,
        HistoricalGrossNetParityOwnership? Ownership);

    private static async Task<HistoricalGrossNetParityApplyResult?>
        ValidateHistoricalGrossNetParityTargetBindingAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityTargetSnapshot target,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        string componentHash;
        try
        {
            componentHash = HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(
                target.ProvedComponents);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.InvariantConflict, false,
                target.TargetTupleHash, target.Ownership,
                "ComponentEvidenceGraphV1 is invalid: " + exception.Message);
        }

        var canonicalHash = HashHistoricalGrossNetParityPayload(
            NormalizeHistoricalGrossNetParityJson(target.CanonicalPayloadJson));
        var lineageHash = HashHistoricalGrossNetParityPayload(
            NormalizeHistoricalGrossNetParityJson(target.LineagePayloadJson));
        var expectedBindingHash = HistoricalGrossNetParityBindingV1.Compute(
            target.TargetTupleHash, target.LineageHash, target.ComponentHash);
        if (!StringComparer.Ordinal.Equals(canonicalHash, target.TargetTupleHash) ||
            !StringComparer.Ordinal.Equals(lineageHash, target.LineageHash) ||
            !StringComparer.Ordinal.Equals(componentHash, target.ComponentHash) ||
            !StringComparer.Ordinal.Equals(expectedBindingHash, target.BindingHash))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.InvariantConflict, false,
                target.TargetTupleHash, target.Ownership,
                "The target binding hashes do not reproduce their canonical payloads.");
        }

        var locked = await LockHistoricalGrossNetParityTargetAsync(
            connection, transaction, target.SourceKind, target.SourceId,
            commandTimeoutSeconds, cancellationToken);
        if (locked is null)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                target.TargetTupleHash, target.Ownership,
                "The target disappeared before its mutation transaction.");
        }
        if (target.SourceKind != HistoricalGrossNetParitySourceKind.PaperPosition &&
            locked.RowVersion != target.RowVersion)
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                target.TargetTupleHash, locked.Ownership ?? target.Ownership,
                "The target row version changed before its mutation transaction.");
        }

        var currentHash = await ReadHistoricalGrossNetParityTargetHashAsync(
            connection, transaction, target.SourceKind, target.SourceId,
            commandTimeoutSeconds, false, cancellationToken);
        if (!StringComparer.Ordinal.Equals(currentHash, target.TargetTupleHash))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredCas, false,
                currentHash ?? target.TargetTupleHash,
                locked.Ownership ?? target.Ownership,
                "The stable target tuple changed before its mutation transaction.");
        }

        foreach (var reference in target.BindingEvidenceReferences ?? [])
        {
            if (reference.SourceKind is null || reference.SourceId is null)
            {
                return new HistoricalGrossNetParityApplyResult(
                    HistoricalGrossNetParityApplyStatus.InvariantConflict, false,
                    target.TargetTupleHash, locked.Ownership ?? target.Ownership,
                    "A binding evidence reference has no canonical source identity.");
            }

            var evidenceHash = await ReadHistoricalGrossNetParityBindingEvidenceHashAsync(
                connection, transaction, reference.SourceKind.Value,
                reference.SourceId.Value, commandTimeoutSeconds, cancellationToken);
            if (!StringComparer.Ordinal.Equals(evidenceHash, reference.EvidenceHash))
            {
                return new HistoricalGrossNetParityApplyResult(
                    HistoricalGrossNetParityApplyStatus.DeferredLineage, false,
                    target.TargetTupleHash, locked.Ownership ?? target.Ownership,
                    $"Binding evidence {reference.SourceKind}/{reference.SourceId:D} changed.");
            }
        }

        if (!await HistoricalGrossNetParityBindingSetMatchesAsync(
                connection, transaction, target, commandTimeoutSeconds, cancellationToken))
        {
            return new HistoricalGrossNetParityApplyResult(
                HistoricalGrossNetParityApplyStatus.DeferredLineage, false,
                target.TargetTupleHash, locked.Ownership ?? target.Ownership,
                "The closed Paper fill/source-selection binding set changed.");
        }

        return null;
    }

    private static async Task<HistoricalGrossNetParityLockedTarget?>
        LockHistoricalGrossNetParityTargetAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind sourceKind,
            Guid sourceId,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var sql = sourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperRun =>
                "SELECT xmin::text::bigint, NULL::text FROM strategy_market_paper_runs WHERE id=@Id FOR UPDATE;",
            HistoricalGrossNetParitySourceKind.PaperPosition =>
                "SELECT xmin::text::bigint, NULL::text FROM paper_positions WHERE id=@Id FOR UPDATE;",
            HistoricalGrossNetParitySourceKind.PaperSettlement =>
                "SELECT xmin::text::bigint, NULL::text FROM paper_position_settlements WHERE id=@Id FOR UPDATE;",
            HistoricalGrossNetParitySourceKind.PaperSellFill =>
                "SELECT fill.xmin::text::bigint, NULL::text FROM paper_fills fill " +
                "INNER JOIN paper_orders paper_order ON paper_order.id=fill.paper_order_id " +
                "WHERE fill.id=@Id FOR UPDATE OF fill, paper_order;",
            HistoricalGrossNetParitySourceKind.LiveOrder =>
                "SELECT row_version, historical_gross_net_parity_ownership FROM live_orders " +
                "WHERE id=@Id FOR UPDATE;",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new HistoricalGrossNetParityLockedTarget(
            reader.GetInt64(0),
            reader.IsDBNull(1)
                ? null
                : Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(1), false));
    }

    private static async Task<string?> ReadHistoricalGrossNetParityBindingEvidenceHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sql = sourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperFillEvidence => """
SELECT jsonb_build_object(
           'fill_id', lower(fill.id::text), 'paper_order_id', lower(paper_order.id::text),
           'strategy_id', lower(paper_order.strategy_id::text),
           'wallet', paper_order.copied_trader_wallet, 'status', paper_order.status,
           'side', paper_order.side, 'execution_source', paper_order.execution_source,
           'asset_id', paper_order.asset_id, 'condition_id', paper_order.condition_id,
           'outcome', paper_order.outcome, 'order_price', paper_order.price,
           'order_size_shares', paper_order.size_shares,
           'order_created_at_utc', paper_order.created_at_utc,
           'fill_price', fill.price, 'fill_size_shares', fill.size_shares,
           'filled_at_utc', fill.filled_at_utc, 'realized_pnl_usd', fill.realized_pnl_usd,
           'fee_usd', fill.fee_usd, 'fee_accounting_status', fill.fee_accounting_status,
           'fee_liquidity_role', fill.fee_liquidity_role,
           'fee_calculation_source', fill.fee_calculation_source,
           'fee_rate', fill.fee_rate, 'fee_exponent', fill.fee_exponent,
           'fee_taker_only', fill.fee_taker_only,
           'fee_calculated_at_utc', fill.fee_calculated_at_utc,
           'net_realized_pnl_usd', fill.net_realized_pnl_usd)::text
FROM paper_fills fill
INNER JOIN paper_orders paper_order ON paper_order.id=fill.paper_order_id
WHERE fill.id=@Id;
""",
            HistoricalGrossNetParitySourceKind.PaperSourceSelection => """
SELECT jsonb_build_object(
           'strategy_id', lower(strategy.id::text),
           'raw_run_count', (SELECT count(*) FROM strategy_market_paper_runs run WHERE run.strategy_id=strategy.id),
           'compact_rollup_run_count', COALESCE((SELECT sum(rollup.run_count) FROM strategy_paper_skip_rollups rollup WHERE rollup.strategy_id=strategy.id),0))::text
FROM strategies strategy WHERE strategy.id=@Id;
""",
            HistoricalGrossNetParitySourceKind.PaperOrderFillLineage => """
SELECT jsonb_build_object(
           'paper_order_id', lower(@Id::uuid::text),
           'fills', COALESCE(jsonb_agg(jsonb_build_object(
               'fill_id', lower(fill.id::text), 'filled_at_utc', fill.filled_at_utc)
               ORDER BY fill.filled_at_utc, lower(fill.id::text))
               FILTER (WHERE fill.id IS NOT NULL), '[]'::jsonb))::text
FROM paper_fills fill WHERE fill.paper_order_id=@Id;
""",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", sourceId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull) return null;
        return HashHistoricalGrossNetParityPayload(NormalizeHistoricalGrossNetParityJson(
            Convert.ToString(value, CultureInfo.InvariantCulture)));
    }

    private static async Task<bool> HistoricalGrossNetParityBindingSetMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParityTargetSnapshot target,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (target.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder)
        {
            return true;
        }

        var expectedFillIds = (target.BindingEvidenceReferences ?? [])
            .Where(value => value.SourceKind == HistoricalGrossNetParitySourceKind.PaperFillEvidence &&
                            value.SourceId is not null)
            .Select(value => value.SourceId!.Value)
            .OrderBy(value => value.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        using var canonical = JsonDocument.Parse(target.CanonicalPayloadJson);
        var root = canonical.RootElement;
        string sql;
        var parameters = new List<NpgsqlParameter>();
        if (target.SourceKind == HistoricalGrossNetParitySourceKind.PaperRun)
        {
            if (!root.TryGetProperty("paper_order_id", out var paperOrderElement) ||
                paperOrderElement.ValueKind == JsonValueKind.Null)
            {
                return expectedFillIds.Length == 0;
            }
            sql = "SELECT id FROM paper_fills WHERE paper_order_id=@PaperOrderId ORDER BY lower(id::text);";
            parameters.Add(new NpgsqlParameter("PaperOrderId", Guid.Parse(paperOrderElement.GetString()!)));
        }
        else
        {
            var wallet = root.GetProperty("wallet").GetString()!;
            var assetId = root.GetProperty("asset_id").GetString()!;
            DateTimeOffset? endAt = target.SourceKind switch
            {
                HistoricalGrossNetParitySourceKind.PaperSettlement => target.SettledAtUtc,
                HistoricalGrossNetParitySourceKind.PaperSellFill => target.SettledAtUtc,
                _ => null
            };
            Guid? endOrderId = null;
            Guid? endFillId = null;
            if (target.SourceKind == HistoricalGrossNetParitySourceKind.PaperSellFill)
            {
                endOrderId = Guid.Parse(root.GetProperty("paper_order_id").GetString()!);
                endFillId = target.SourceId;
            }
            sql = """
SELECT fill.id
FROM paper_orders paper_order
INNER JOIN paper_fills fill ON fill.paper_order_id=paper_order.id
WHERE paper_order.copied_trader_wallet=@Wallet AND paper_order.asset_id=@AssetId
  AND (@EndAt IS NULL OR fill.filled_at_utc < @EndAt
       OR (fill.filled_at_utc = @EndAt AND
           (@EndFillId IS NULL OR
            ROW(lower(paper_order.id::text), lower(fill.id::text)) <=
            ROW(lower(@EndOrderId::uuid::text), lower(@EndFillId::uuid::text)))))
ORDER BY lower(fill.id::text);
""";
            parameters.Add(new NpgsqlParameter("Wallet", wallet));
            parameters.Add(new NpgsqlParameter("AssetId", assetId));
            parameters.Add(new NpgsqlParameter("EndAt", NpgsqlDbType.TimestampTz)
            {
                Value = endAt is null ? DBNull.Value : UtcDateTime(endAt.Value)
            });
            parameters.Add(new NpgsqlParameter("EndOrderId", NpgsqlDbType.Uuid)
            {
                Value = endOrderId is null ? DBNull.Value : endOrderId.Value
            });
            parameters.Add(new NpgsqlParameter("EndFillId", NpgsqlDbType.Uuid)
            {
                Value = endFillId is null ? DBNull.Value : endFillId.Value
            });
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddRange(parameters.ToArray());
        var actual = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) actual.Add(reader.GetGuid(0));
        return actual.OrderBy(value => value.ToString("D"), StringComparer.Ordinal)
            .SequenceEqual(expectedFillIds);
    }

    private static string CreateHistoricalGrossNetParityDecisionEvidencePayload(
        HistoricalGrossNetParityTargetSnapshot target,
        string decisionEvidenceJson)
    {
        using var decisionEvidence = JsonDocument.Parse(decisionEvidenceJson);
        var fillIds = (target.BindingEvidenceReferences ?? [])
            .Where(value => value.SourceKind == HistoricalGrossNetParitySourceKind.PaperFillEvidence &&
                            value.SourceId is not null)
            .Select(value => value.SourceId!.Value.ToString("D").ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            historicalGrossNetParityBindingV1 = new
            {
                target.BindingHash,
                target.TargetTupleHash,
                target.LineageHash,
                target.ComponentHash,
                paperFillIds = fillIds,
                bindingEvidenceReferences = target.BindingEvidenceReferences ?? []
            },
            componentEvidenceGraphV1 =
                HistoricalGrossNetParityComponentGraphV1.ToEvidenceRecords(target.ProvedComponents),
            componentEvidenceProvedComplete =
                target.AuthoritativeEffectiveFeeUsd is not null && target.ProvedComponents.Count > 0,
            decisionEvidence = decisionEvidence.RootElement
        });
    }

    private static async Task<bool> HistoricalGrossNetParityDecisionSelectionMatchesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind targetSourceKind,
        HistoricalGrossNetParityAccountingDecisionV1 decision,
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> orderedCandidates,
        int donorPageSize,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (decision.DonorDecision is null)
        {
            return decision.DecisionKind == HistoricalGrossNetParityDecisionKind.Fixed0p0333 ||
                   decision.DecisionKind is not (
                       HistoricalGrossNetParityDecisionKind.DonorRatio or
                       HistoricalGrossNetParityDecisionKind.Fixed0p033);
        }

        var actual = await RecomputeHistoricalGrossNetParitySelectionAsync(
            connection, transaction, targetSourceKind, orderedCandidates,
            donorPageSize, commandTimeoutSeconds, cancellationToken);
        if (!HistoricalGrossNetParityDonorDecisionMatches(decision.DonorDecision, actual))
        {
            return false;
        }

        if (actual.SelectedStrategyId is null)
        {
            return (decision.DecisionKind == HistoricalGrossNetParityDecisionKind.Fixed0p0333 &&
                    decision.DonorDecision.Ratio == 0.0333m) ||
                   (decision.DecisionKind == HistoricalGrossNetParityDecisionKind.Fixed0p033 &&
                    decision.DonorDecision.Ratio == 0.033m);
        }

        var descriptor = orderedCandidates.Single(value =>
            value.StrategyId == actual.SelectedStrategyId.Value);
        var aggregate = await LoadHistoricalGrossNetParityDonorAggregateStreamingAsync(
            connection, transaction, targetSourceKind, descriptor,
            donorPageSize,
            commandTimeoutSeconds, cancellationToken);
        return decision.DecisionKind == HistoricalGrossNetParityDecisionKind.DonorRatio &&
               decision.DonorDecision.RawDonorCount == aggregate.RawDonorCount &&
               decision.DonorDecision.ExactDonorCount == aggregate.ExactDonorCount &&
               decision.DonorDecision.DeduplicatedDonorCount == aggregate.DeduplicatedDonorCount &&
               decision.DonorDecision.AggregateStakeUsd == aggregate.AggregateStakeUsd &&
               decision.DonorDecision.N == aggregate.N &&
               decision.DonorDecision.D == aggregate.D &&
               StringComparer.Ordinal.Equals(
                   decision.DonorDecision.MembershipHashV1,
                   aggregate.MembershipHashV1);
    }

    private static async Task<long> UpdateHistoricalGrossNetParityPaperAccountingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityAccountingDecisionV1 decision,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var sql = target.SourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperRun => """
UPDATE strategy_market_paper_runs
SET fee_usd=@StoredFee, fee_accounting_status=@Status,
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@CalculatedAt, net_realized_pnl_usd=@Net,
    updated_at_utc=GREATEST(updated_at_utc,@CalculatedAt)
WHERE id=@Id AND xmin::text::bigint=@ExpectedRowVersion
  AND realized_pnl_usd=@Gross
RETURNING xmin::text::bigint;
""",
            HistoricalGrossNetParitySourceKind.PaperPosition => """
UPDATE paper_positions
SET fee_usd=@StoredFee, fee_accounting_status=@Status,
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@CalculatedAt,
    net_unrealized_pnl_usd=unrealized_pnl_usd-@EffectiveFee,
    updated_at_utc=GREATEST(updated_at_utc,@CalculatedAt)
WHERE id=@Id
  AND average_price*size_shares=@Basis
RETURNING xmin::text::bigint;
""",
            HistoricalGrossNetParitySourceKind.PaperSettlement => """
UPDATE paper_position_settlements
SET fee_usd=@StoredFee, fee_accounting_status=@Status,
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@CalculatedAt, net_realized_pnl_usd=@Net
WHERE id=@Id AND xmin::text::bigint=@ExpectedRowVersion
  AND realized_pnl_usd=@Gross AND cost_basis_usd=@Basis
RETURNING xmin::text::bigint;
""",
            HistoricalGrossNetParitySourceKind.PaperSellFill => """
UPDATE paper_fills
SET fee_usd=@StoredFee, fee_accounting_status=@Status,
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@CalculatedAt, net_realized_pnl_usd=@Net
WHERE id=@Id AND xmin::text::bigint=@ExpectedRowVersion
  AND realized_pnl_usd=@Gross
  AND (price*size_shares)-realized_pnl_usd=@Basis
RETURNING xmin::text::bigint;
""",
            _ => throw new ArgumentOutOfRangeException(nameof(target.SourceKind))
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        AddHistoricalGrossNetParityAccountingParameters(command, target, decision);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? -1L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void AddHistoricalGrossNetParityAccountingParameters(
        NpgsqlCommand command,
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityAccountingDecisionV1 decision)
    {
        command.Parameters.AddWithValue("Id", target.SourceId);
        command.Parameters.AddWithValue("ExpectedRowVersion", target.RowVersion);
        command.Parameters.AddWithValue("StoredFee", decision.StoredFeeUsd);
        command.Parameters.AddWithValue("EffectiveFee", decision.ContributionEffectiveFeeUsd);
        command.Parameters.AddWithValue("Net", decision.NetPnlUsd);
        command.Parameters.AddWithValue("Gross", target.GrossPnlUsd);
        command.Parameters.AddWithValue("Basis", target.GrossRoiBasisUsd);
        command.Parameters.AddWithValue("Status", decision.FeeAccountingStatus);
        command.Parameters.AddWithValue("Role", decision.FeeLiquidityRole);
        command.Parameters.AddWithValue("Source", decision.FeeCalculationSource);
        command.Parameters.AddWithValue("Rate", NullableDecimal(decision.FeeRate));
        command.Parameters.AddWithValue("Exponent", decision.FeeExponent is null
            ? DBNull.Value : decision.FeeExponent.Value);
        command.Parameters.AddWithValue("TakerOnly", decision.FeeTakerOnly is null
            ? DBNull.Value : decision.FeeTakerOnly.Value);
        command.Parameters.AddWithValue("CalculatedAt", UtcDateTime(decision.FeeCalculatedAtUtc));
    }

    private static async Task<long> UpdateHistoricalGrossNetParityLiveAccountingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParityTargetSnapshot target,
        HistoricalGrossNetParityAccountingDecisionV1 decision,
        HistoricalGrossNetParityOwnership expectedOwnership,
        HistoricalGrossNetParityOwnership resultingOwnership,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE live_orders
SET fee_usd=@StoredFee, fee_accounting_status=@Status,
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@CalculatedAt, net_realized_pnl_usd=@Net,
    cost_basis_usd=@CostBasis,
    historical_gross_net_parity_ownership=@ResultingOwnership,
    row_version=row_version+1,
    updated_at_utc=GREATEST(updated_at_utc,@CalculatedAt)
WHERE id=@Id AND row_version=@ExpectedRowVersion
  AND historical_gross_net_parity_ownership=@ExpectedOwnership
  AND realized_pnl_usd=@Gross
RETURNING row_version;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        AddHistoricalGrossNetParityAccountingParameters(command, target, decision);
        command.Parameters.AddWithValue("CostBasis", decision.CostBasisUsd!.Value);
        command.Parameters.AddWithValue("ExpectedOwnership", expectedOwnership.ToString());
        command.Parameters.AddWithValue("ResultingOwnership", resultingOwnership.ToString());
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? -1L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HistoricalGrossNetParityAuditExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        string calculationVersion,
        HistoricalGrossNetParityOperationKind operationKind,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT EXISTS (
    SELECT 1 FROM historical_gross_net_parity_audit
    WHERE source_kind=@SourceKind AND source_id=@SourceId
      AND calculation_version=@CalculationVersion AND operation_kind=@OperationKind);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("SourceKind", sourceKind.ToString());
        command.Parameters.AddWithValue("SourceId", sourceId);
        command.Parameters.AddWithValue("CalculationVersion", calculationVersion);
        command.Parameters.AddWithValue("OperationKind", operationKind.ToString());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task InsertHistoricalGrossNetParityAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        Guid strategyId,
        string calculationVersion,
        HistoricalGrossNetParityOperationKind operationKind,
        string evidenceVersion,
        HistoricalGrossNetParityDecisionKind? decisionKind,
        string oldPayload,
        string newPayload,
        string evidencePayload,
        long? expectedRowVersion,
        long? resultingRowVersion,
        HistoricalGrossNetParityBaselineEffectKind? baselineKind,
        decimal? nominalGross,
        decimal? nominalNet,
        decimal? desiredCumulativeAdjustment,
        decimal? priorActual,
        decimal? requestedDelta,
        decimal? balanceBefore,
        decimal? balanceAfter,
        decimal? actualDelta,
        decimal? newActual,
        decimal? residual,
        bool? clampApplied,
        string? authorityId,
        string? authorityOrderKey,
        string? supersedesEvidenceVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
INSERT INTO historical_gross_net_parity_audit (
    audit_id, source_kind, source_id, strategy_id, calculation_version,
    operation_kind, evidence_version, operation_id, decision_kind,
    old_payload_json, new_payload_json, evidence_payload_json,
    expected_row_version, resulting_row_version, baseline_effect_kind,
    nominal_baseline_gross_pnl_usd, nominal_baseline_net_pnl_usd,
    desired_cumulative_adjustment, prior_actual_cumulative_adjustment,
    requested_delta, balance_before, balance_after, actual_applied_delta,
    new_actual_cumulative_adjustment, residual_unapplied_delta, clamp_applied,
    authority_id, authority_order_key, supersedes_evidence_version)
VALUES (
    @AuditId, @SourceKind, @SourceId, @StrategyId, @CalculationVersion,
    @OperationKind, @EvidenceVersion, @OperationId, @DecisionKind,
    CAST(@OldPayload AS jsonb), CAST(@NewPayload AS jsonb), CAST(@EvidencePayload AS jsonb),
    @ExpectedRowVersion, @ResultingRowVersion, @BaselineKind,
    @NominalGross, @NominalNet, @Desired, @PriorActual, @Requested,
    @BalanceBefore, @BalanceAfter, @Actual, @NewActual, @Residual, @Clamp,
    @AuthorityId, @AuthorityOrderKey, @SupersedesEvidenceVersion);
""",
            connection,
            transaction);
        command.Parameters.AddWithValue("AuditId", Guid.NewGuid());
        command.Parameters.AddWithValue("SourceKind", sourceKind.ToString());
        command.Parameters.AddWithValue("SourceId", sourceId);
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("CalculationVersion", calculationVersion);
        command.Parameters.AddWithValue("OperationKind", operationKind.ToString());
        command.Parameters.AddWithValue("EvidenceVersion", evidenceVersion);
        command.Parameters.AddWithValue("OperationId", Guid.NewGuid());
        command.Parameters.AddWithValue("DecisionKind", DbValue(decisionKind?.ToString()));
        command.Parameters.AddWithValue("OldPayload", NormalizeHistoricalGrossNetParityJson(oldPayload));
        command.Parameters.AddWithValue("NewPayload", NormalizeHistoricalGrossNetParityJson(newPayload));
        command.Parameters.AddWithValue("EvidencePayload", NormalizeHistoricalGrossNetParityJson(evidencePayload));
        command.Parameters.AddWithValue("ExpectedRowVersion", DbValue(expectedRowVersion));
        command.Parameters.AddWithValue("ResultingRowVersion", DbValue(resultingRowVersion));
        command.Parameters.AddWithValue("BaselineKind", DbValue(baselineKind?.ToString()));
        command.Parameters.AddWithValue("NominalGross", DbValue(nominalGross));
        command.Parameters.AddWithValue("NominalNet", DbValue(nominalNet));
        command.Parameters.AddWithValue("Desired", DbValue(desiredCumulativeAdjustment));
        command.Parameters.AddWithValue("PriorActual", DbValue(priorActual));
        command.Parameters.AddWithValue("Requested", DbValue(requestedDelta));
        command.Parameters.AddWithValue("BalanceBefore", DbValue(balanceBefore));
        command.Parameters.AddWithValue("BalanceAfter", DbValue(balanceAfter));
        command.Parameters.AddWithValue("Actual", DbValue(actualDelta));
        command.Parameters.AddWithValue("NewActual", DbValue(newActual));
        command.Parameters.AddWithValue("Residual", DbValue(residual));
        command.Parameters.AddWithValue("Clamp", DbValue(clampApplied));
        command.Parameters.AddWithValue("AuthorityId", DbValue(authorityId));
        command.Parameters.AddWithValue("AuthorityOrderKey", DbValue(authorityOrderKey));
        command.Parameters.AddWithValue("SupersedesEvidenceVersion", DbValue(supersedesEvidenceVersion));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object DbValue<T>(T? value) => value is null ? DBNull.Value : value;

    private static Task QueueHistoricalGrossNetParityReconciliationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        CancellationToken cancellationToken) =>
        PostgresDashboardProjectionRepository.QueueReconciliationAsync(
            connection,
            transaction,
            strategyId,
            100,
            "historical-gross-net-parity-v1",
            cancellationToken);

    private sealed record HistoricalGrossNetParityLiveAuditState(
        HistoricalGrossNetParityOwnership Ownership,
        bool BaselineExists,
        bool DecisionExists,
        string? CurrentTargetHash);

    private static async Task<HistoricalGrossNetParityLiveAuditState>
        ReadHistoricalGrossNetParityLiveAuditStateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid liveOrderId,
            string calculationVersion,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT live_order.historical_gross_net_parity_ownership,
       EXISTS (SELECT 1 FROM historical_gross_net_parity_audit audit
               WHERE audit.source_kind='LiveOrder' AND audit.source_id=live_order.id
                 AND audit.calculation_version=@CalculationVersion
                 AND audit.operation_kind='AccountingBaseline'),
       EXISTS (SELECT 1 FROM historical_gross_net_parity_audit audit
               WHERE audit.source_kind='LiveOrder' AND audit.source_id=live_order.id
                 AND audit.calculation_version=@CalculationVersion
                 AND audit.operation_kind='AccountingDecision'),
       to_jsonb(live_order)::text
FROM live_orders live_order WHERE live_order.id=@Id FOR UPDATE;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", liveOrderId);
        command.Parameters.AddWithValue("CalculationVersion", calculationVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new HistoricalGrossNetParityLiveAuditState(
                HistoricalGrossNetParityOwnership.None, false, false, null);
        }
        var payload = NormalizeHistoricalGrossNetParityJson(reader.GetString(3));
        return new HistoricalGrossNetParityLiveAuditState(
            Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(0), false),
            reader.GetBoolean(1), reader.GetBoolean(2),
            HashHistoricalGrossNetParityPayload(payload));
    }

    private static bool TryClassifyHistoricalGrossNetParityBaseline(
        HistoricalGrossNetParityTargetSnapshot target,
        out HistoricalGrossNetParityBaselineEffectKind baselineKind,
        out decimal? nominalNet)
    {
        nominalNet = null;
        if (!target.BalanceEffectApplied)
        {
            baselineKind = HistoricalGrossNetParityBaselineEffectKind.None;
            return true;
        }

        if (string.Equals(target.FeeAccountingStatus,
                nameof(FeeAccountingStatus.LegacyUnknown), StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(target.FeeCalculationSource) &&
            target.FeeCalculatedAtUtc is null && target.NetPnlUsd is null)
        {
            baselineKind = HistoricalGrossNetParityBaselineEffectKind.LegacyGrossApplied;
            return true;
        }

        if (target.ExactEligibility == HistoricalGrossNetParityExactEligibility.ExistingExactPreserved &&
            target.NetPnlUsd is not null && target.FeeUsd >= 0m &&
            target.NetPnlUsd.Value == target.GrossPnlUsd - target.FeeUsd)
        {
            baselineKind = HistoricalGrossNetParityBaselineEffectKind.NetAlreadyApplied;
            nominalNet = target.NetPnlUsd.Value;
            return true;
        }

        baselineKind = default;
        return false;
    }

    private static async Task<HistoricalGrossNetParityLiveBalanceState?>
        ReadHistoricalGrossNetParityLiveBalanceStateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityLiveBalanceRequest request,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
WITH baseline AS MATERIALIZED (
    SELECT audit.baseline_effect_kind, audit.nominal_baseline_gross_pnl_usd,
           audit.nominal_baseline_net_pnl_usd
    FROM historical_gross_net_parity_audit audit
    WHERE audit.source_kind='LiveOrder' AND audit.source_id=@LiveOrderId
      AND audit.calculation_version=@CalculationVersion
      AND audit.operation_kind='AccountingBaseline'
), latest_decision AS MATERIALIZED (
    SELECT audit.evidence_version, audit.desired_cumulative_adjustment
    FROM historical_gross_net_parity_audit audit
    WHERE audit.source_kind='LiveOrder' AND audit.source_id=@LiveOrderId
      AND audit.calculation_version=@CalculationVersion
      AND audit.operation_kind IN ('AccountingDecision','VenueReportedRevision')
      AND audit.desired_cumulative_adjustment IS NOT NULL
    ORDER BY CASE WHEN audit.operation_kind='VenueReportedRevision' THEN 1 ELSE 0 END DESC,
             audit.authority_order_key DESC NULLS LAST,
             audit.occurred_at_utc DESC, lower(audit.audit_id::text) DESC
    LIMIT 1
), applied AS MATERIALIZED (
    SELECT COALESCE(sum(audit.actual_applied_delta),0) AS total
    FROM historical_gross_net_parity_audit audit
    WHERE audit.source_kind='LiveOrder' AND audit.source_id=@LiveOrderId
      AND audit.calculation_version=@CalculationVersion
      AND audit.operation_kind IN ('InitialBalanceApplication','VenueReportedRevision')
      AND audit.actual_applied_delta IS NOT NULL
)
SELECT live_order.id, live_order.strategy_id,
       live_order.historical_gross_net_parity_ownership, live_order.row_version,
       live_order.realized_pnl_usd, live_order.net_realized_pnl_usd,
       live_order.settled_at_utc, to_jsonb(live_order)::text,
       baseline.baseline_effect_kind, baseline.nominal_baseline_gross_pnl_usd,
       baseline.nominal_baseline_net_pnl_usd,
       latest_decision.desired_cumulative_adjustment,
       latest_decision.evidence_version, applied.total
FROM live_orders live_order
CROSS JOIN baseline
CROSS JOIN latest_decision
CROSS JOIN applied
WHERE live_order.id=@LiveOrderId AND live_order.strategy_id=@StrategyId
  AND live_order.settled_at_utc IS NOT NULL
  AND live_order.realized_pnl_usd IS NOT NULL
FOR UPDATE OF live_order;
""",
            connection,
            transaction)
        {
            CommandTimeout = request.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("LiveOrderId", request.LiveOrderId);
        command.Parameters.AddWithValue("StrategyId", request.StrategyId);
        command.Parameters.AddWithValue("CalculationVersion", request.CalculationVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new HistoricalGrossNetParityLiveBalanceState(
            reader.GetGuid(0), reader.GetGuid(1),
            Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(2), false),
            reader.GetInt64(3), reader.GetDecimal(4),
            reader.IsDBNull(5) ? null : reader.GetDecimal(5),
            DateTimeOffsetFromUtc(reader.GetDateTime(6)),
            NormalizeHistoricalGrossNetParityJson(reader.GetString(7)),
            Enum.Parse<HistoricalGrossNetParityBaselineEffectKind>(reader.GetString(8), false),
            reader.GetDecimal(9), reader.IsDBNull(10) ? null : reader.GetDecimal(10),
            reader.GetDecimal(11), reader.GetString(12), reader.GetDecimal(13));
    }

    private static async Task<Guid?> ReadEarliestHistoricalGrossNetParityPendingLiveOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT live_order.id
FROM live_orders live_order
LEFT JOIN LATERAL (
    SELECT min(fill.filled_at_utc) AS first_fill_at_utc
    FROM paper_fills fill WHERE fill.paper_order_id=live_order.paper_order_id
) linked_fill ON true
WHERE live_order.strategy_id=@StrategyId
  AND live_order.historical_gross_net_parity_ownership IN ('None','Pending')
  AND live_order.settled_at_utc IS NOT NULL
  AND live_order.realized_pnl_usd IS NOT NULL
  AND COALESCE(live_order.submitted_at_utc, linked_fill.first_fill_at_utc,
               live_order.created_at_utc) < @CutoffUtc
ORDER BY live_order.settled_at_utc, lower(live_order.id::text)
LIMIT 1
FOR UPDATE OF live_order;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("CutoffUtc", UtcDateTime(HistoricalGrossNetParityConstants.CutoffUtc));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task<decimal?> ReadHistoricalGrossNetParityStrategyBalanceForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT live_available_balance FROM strategies WHERE id=@Id FOR UPDATE;",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", strategyId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static async Task UpdateHistoricalGrossNetParityStrategyBalanceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid strategyId,
        decimal balance,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE strategies
SET live_available_balance=@Balance, updated_at_utc=clock_timestamp()
WHERE id=@Id;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", strategyId);
        command.Parameters.AddWithValue("Balance", balance);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("Historical parity strategy balance target disappeared.");
        }
    }

    private static async Task<long> CompleteHistoricalGrossNetParityLiveOwnershipAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid liveOrderId,
        long expectedRowVersion,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE live_orders
SET balance_effect_applied=true,
    historical_gross_net_parity_ownership='Completed',
    row_version=row_version+1,
    updated_at_utc=clock_timestamp()
WHERE id=@Id AND row_version=@ExpectedRowVersion
  AND historical_gross_net_parity_ownership='Pending'
RETURNING row_version;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", liveOrderId);
        command.Parameters.AddWithValue("ExpectedRowVersion", expectedRowVersion);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? -1L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private sealed record HistoricalGrossNetParityVenueState(
        Guid StrategyId,
        long RowVersion,
        HistoricalGrossNetParityOwnership Ownership,
        decimal GrossPnlUsd,
        decimal BasisUsd,
        string CurrentPayloadJson,
        HistoricalGrossNetParityBaselineEffectKind BaselineKind,
        decimal NominalGrossPnlUsd,
        decimal? NominalNetPnlUsd,
        string LatestEvidenceVersion,
        string? LatestVenueAuthorityId,
        string? LatestVenueAuthorityOrderKey,
        decimal PriorActualCumulativeAdjustment);

    private static async Task<HistoricalGrossNetParityVenueRevisionResult>
        ApplyHistoricalGrossNetParityVenueRevisionCoreAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityVenueRevisionRequest request,
            CancellationToken cancellationToken)
    {
        var state = await ReadHistoricalGrossNetParityVenueStateAsync(
            connection, transaction, request.LiveOrderId, cancellationToken);
        if (state is null || state.Ownership == HistoricalGrossNetParityOwnership.None)
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, false, state?.Ownership ?? HistoricalGrossNetParityOwnership.None,
                null, null, null, false,
                "Venue evidence requires an existing parity baseline and Pending/Completed ownership.");
        }

        if (StringComparer.Ordinal.Equals(state.LatestEvidenceVersion, request.EvidenceVersion))
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, true, state.Ownership, null, null, null,
                state.Ownership == HistoricalGrossNetParityOwnership.Pending,
                "The same VenueReported evidence version was already accepted.");
        }
        if (!StringComparer.Ordinal.Equals(
                state.LatestEvidenceVersion,
                request.SupersedesEvidenceVersion) ||
            state.LatestVenueAuthorityId is not null &&
                !StringComparer.Ordinal.Equals(state.LatestVenueAuthorityId, request.AuthorityId) ||
            state.LatestVenueAuthorityOrderKey is not null &&
                StringComparer.Ordinal.Compare(
                    request.AuthorityOrderKey,
                    state.LatestVenueAuthorityOrderKey) <= 0)
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, false, state.Ownership, null, null, null, false,
                "Venue evidence does not strictly supersede the latest accepted authority chain.");
        }

        if (request.NetRealizedPnlUsd != state.GrossPnlUsd - request.FeeUsd)
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, false, state.Ownership, null, null, null, false,
                "VenueReported Net does not equal current Gross minus authoritative Fee.");
        }

        var desired = state.BaselineKind switch
        {
            HistoricalGrossNetParityBaselineEffectKind.None => request.NetRealizedPnlUsd,
            HistoricalGrossNetParityBaselineEffectKind.LegacyGrossApplied =>
                request.NetRealizedPnlUsd - state.NominalGrossPnlUsd,
            HistoricalGrossNetParityBaselineEffectKind.NetAlreadyApplied
                when state.NominalNetPnlUsd is not null =>
                request.NetRealizedPnlUsd - state.NominalNetPnlUsd.Value,
            _ => throw new InvalidOperationException("Venue baseline is incomplete.")
        };
        decimal? requestedDelta = null;
        decimal? actualDelta = null;
        decimal? residual = desired - state.PriorActualCumulativeAdjustment;
        decimal? balanceBefore = null;
        decimal? balanceAfter = null;
        decimal? newActual = state.PriorActualCumulativeAdjustment;
        bool? clamp = null;
        if (state.Ownership == HistoricalGrossNetParityOwnership.Completed)
        {
            balanceBefore = await ReadHistoricalGrossNetParityStrategyBalanceForUpdateAsync(
                connection, transaction, state.StrategyId, 30, cancellationToken);
            if (balanceBefore is null)
            {
                return new HistoricalGrossNetParityVenueRevisionResult(
                    false, false, state.Ownership, null, null, null, false,
                    "Venue correction strategy is missing.");
            }
            requestedDelta = desired - state.PriorActualCumulativeAdjustment;
            var unclamped = balanceBefore.Value + requestedDelta.Value;
            balanceAfter = Math.Clamp(unclamped, 0m, 100m);
            actualDelta = balanceAfter.Value - balanceBefore.Value;
            newActual = state.PriorActualCumulativeAdjustment + actualDelta.Value;
            residual = desired - newActual.Value;
            clamp = balanceAfter.Value != unclamped;
            await UpdateHistoricalGrossNetParityStrategyBalanceAsync(
                connection, transaction, state.StrategyId, balanceAfter.Value, 30,
                cancellationToken);
        }

        var resultingRowVersion = await UpdateHistoricalGrossNetParityVenueAccountingAsync(
            connection, transaction, request, state, cancellationToken);
        if (resultingRowVersion < 0)
        {
            return new HistoricalGrossNetParityVenueRevisionResult(
                false, false, state.Ownership, null, null, null, false,
                "Live order changed before the atomic VenueReported CAS.");
        }

        var newPayload = await ReadHistoricalGrossNetParityTargetPayloadAsync(
            connection, transaction, HistoricalGrossNetParitySourceKind.LiveOrder,
            request.LiveOrderId, 30, cancellationToken) ?? "{}";
        using var venueEvidence = JsonDocument.Parse(request.EvidenceJson);
        var auditEvidence = JsonSerializer.Serialize(new
        {
            schema = "HistoricalGrossNetParityVenueEvidenceV1",
            request.AuthorityId,
            request.AuthorityOrderKey,
            request.EvidenceVersion,
            request.SupersedesEvidenceVersion,
            associatedLiveOrderId = request.LiveOrderId,
            evidence = venueEvidence.RootElement
        });
        await InsertHistoricalGrossNetParityAuditAsync(
            connection, transaction,
            HistoricalGrossNetParitySourceKind.LiveOrder, request.LiveOrderId,
            state.StrategyId, HistoricalGrossNetParityConstants.CalculationVersion,
            HistoricalGrossNetParityOperationKind.VenueReportedRevision,
            request.EvidenceVersion, decisionKind: null,
            state.CurrentPayloadJson, newPayload, auditEvidence,
            state.RowVersion, resultingRowVersion,
            state.BaselineKind, state.NominalGrossPnlUsd, state.NominalNetPnlUsd,
            desired, state.PriorActualCumulativeAdjustment,
            requestedDelta, balanceBefore, balanceAfter, actualDelta,
            newActual, residual, clamp,
            request.AuthorityId, request.AuthorityOrderKey,
            request.SupersedesEvidenceVersion,
            cancellationToken);
        await QueueHistoricalGrossNetParityReconciliationAsync(
            connection, transaction, state.StrategyId, cancellationToken);
        return new HistoricalGrossNetParityVenueRevisionResult(
            true, false, state.Ownership,
            requestedDelta, actualDelta, residual,
            state.Ownership == HistoricalGrossNetParityOwnership.Pending,
            "VenueReported evidence revision applied.");
    }

    private static async Task<HistoricalGrossNetParityVenueState?>
        ReadHistoricalGrossNetParityVenueStateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid liveOrderId,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
WITH baseline AS MATERIALIZED (
    SELECT baseline_effect_kind, nominal_baseline_gross_pnl_usd,
           nominal_baseline_net_pnl_usd
    FROM historical_gross_net_parity_audit
    WHERE source_kind='LiveOrder' AND source_id=@Id
      AND calculation_version=@CalculationVersion
      AND operation_kind='AccountingBaseline'
), accounting AS MATERIALIZED (
    SELECT evidence_version FROM historical_gross_net_parity_audit
    WHERE source_kind='LiveOrder' AND source_id=@Id
      AND calculation_version=@CalculationVersion
      AND operation_kind='AccountingDecision'
), venue AS MATERIALIZED (
    SELECT evidence_version, authority_id, authority_order_key
    FROM historical_gross_net_parity_audit
    WHERE source_kind='LiveOrder' AND source_id=@Id
      AND calculation_version=@CalculationVersion
      AND operation_kind='VenueReportedRevision'
    ORDER BY authority_order_key DESC, occurred_at_utc DESC, lower(audit_id::text) DESC
    LIMIT 1
), applied AS MATERIALIZED (
    SELECT COALESCE(sum(actual_applied_delta),0) AS total
    FROM historical_gross_net_parity_audit
    WHERE source_kind='LiveOrder' AND source_id=@Id
      AND calculation_version=@CalculationVersion
      AND operation_kind IN ('InitialBalanceApplication','VenueReportedRevision')
      AND actual_applied_delta IS NOT NULL
)
SELECT live_order.strategy_id, live_order.row_version,
       live_order.historical_gross_net_parity_ownership,
       live_order.realized_pnl_usd,
       CASE WHEN live_order.filled_notional_usd>0 THEN live_order.filled_notional_usd
            WHEN live_order.filled_size>0 THEN live_order.price*live_order.filled_size
            WHEN live_order.cost_basis_usd>0
            THEN GREATEST(0,live_order.cost_basis_usd-live_order.fee_usd)
            ELSE 0 END AS basis,
       to_jsonb(live_order)::text,
       baseline.baseline_effect_kind, baseline.nominal_baseline_gross_pnl_usd,
       baseline.nominal_baseline_net_pnl_usd,
       COALESCE(venue.evidence_version,accounting.evidence_version),
       venue.authority_id, venue.authority_order_key, applied.total
FROM live_orders live_order
CROSS JOIN baseline
CROSS JOIN accounting
CROSS JOIN applied
LEFT JOIN venue ON true
WHERE live_order.id=@Id
FOR UPDATE OF live_order;
""",
            connection,
            transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("Id", liveOrderId);
        command.Parameters.AddWithValue(
            "CalculationVersion", HistoricalGrossNetParityConstants.CalculationVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new HistoricalGrossNetParityVenueState(
            reader.GetGuid(0), reader.GetInt64(1),
            Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(2), false),
            reader.GetDecimal(3), reader.GetDecimal(4),
            NormalizeHistoricalGrossNetParityJson(reader.GetString(5)),
            Enum.Parse<HistoricalGrossNetParityBaselineEffectKind>(reader.GetString(6), false),
            reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetDecimal(12));
    }

    private static async Task<long> UpdateHistoricalGrossNetParityVenueAccountingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParityVenueRevisionRequest request,
        HistoricalGrossNetParityVenueState state,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
UPDATE live_orders
SET fee_usd=@Fee, fee_accounting_status='VenueReported',
    fee_liquidity_role=@Role, fee_calculation_source=@Source,
    fee_rate=@Rate, fee_exponent=@Exponent, fee_taker_only=@TakerOnly,
    fee_calculated_at_utc=@ReportedAt, net_realized_pnl_usd=@Net,
    cost_basis_usd=@CostBasis, row_version=row_version+1,
    updated_at_utc=GREATEST(updated_at_utc,@ReportedAt)
WHERE id=@Id AND row_version=@ExpectedRowVersion
  AND historical_gross_net_parity_ownership=@Ownership
  AND realized_pnl_usd=@Gross
RETURNING row_version;
""",
            connection,
            transaction)
        {
            CommandTimeout = 30
        };
        command.Parameters.AddWithValue("Id", request.LiveOrderId);
        command.Parameters.AddWithValue("ExpectedRowVersion", state.RowVersion);
        command.Parameters.AddWithValue("Ownership", state.Ownership.ToString());
        command.Parameters.AddWithValue("Gross", state.GrossPnlUsd);
        command.Parameters.AddWithValue("Fee", request.FeeUsd);
        command.Parameters.AddWithValue("Net", request.NetRealizedPnlUsd);
        command.Parameters.AddWithValue("CostBasis", Math.Round(
            state.BasisUsd + request.FeeUsd, 8, MidpointRounding.AwayFromZero));
        command.Parameters.AddWithValue("Role", request.FeeLiquidityRole);
        command.Parameters.AddWithValue("Source", request.FeeCalculationSource);
        command.Parameters.AddWithValue("Rate", NullableDecimal(request.FeeRate));
        command.Parameters.AddWithValue("Exponent", DbValue(request.FeeExponent));
        command.Parameters.AddWithValue("TakerOnly", DbValue(request.FeeTakerOnly));
        command.Parameters.AddWithValue("ReportedAt", UtcDateTime(request.ReportedAtUtc));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? -1L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task<HistoricalGrossNetParityCandidateSelection>
        LoadHistoricalGrossNetParityCandidateKeysAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityCandidatePageRequest request,
            CancellationToken cancellationToken)
    {
        return await LoadHistoricalGrossNetParityStrategyCandidateKeysAsync(
            connection,
            transaction,
            request,
            request.Strategy,
            cancellationToken);
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityRankedStrategy>>
        LoadHistoricalGrossNetParityStrategyRankingAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
SELECT strategy.id, strategy.code, performance.realized_pnl_usd
FROM strategies strategy
LEFT JOIN dashboard_strategy_performance_snapshots performance
  ON performance.strategy_id = strategy.id;
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };

        var rows = new List<(Guid StrategyId, string StrategyCode, decimal? GrossPnl)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
            }
        }

        var missingIds = rows
            .Where(value => value.GrossPnl is null)
            .Select(value => value.StrategyId)
            .ToArray();
        var fallbackGross = missingIds.Length == 0
            ? new Dictionary<Guid, decimal>()
            : await LoadHistoricalGrossNetParityMissingStrategyGrossAsync(
                connection,
                transaction,
                missingIds,
                commandTimeoutSeconds,
                cancellationToken);

        return rows
            .Select(value => new
            {
                value.StrategyId,
                value.StrategyCode,
                GrossPnl = value.GrossPnl ?? fallbackGross.GetValueOrDefault(value.StrategyId)
            })
            .OrderByDescending(value => value.GrossPnl)
            .ThenBy(value => value.StrategyId.ToString("D"), StringComparer.Ordinal)
            .Select((value, index) => new HistoricalGrossNetParityRankedStrategy(
                value.StrategyId,
                value.StrategyCode,
                index + 1,
                value.GrossPnl))
            .ToArray();
    }

    private static async Task<Dictionary<Guid, decimal>>
        LoadHistoricalGrossNetParityMissingStrategyGrossAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid[] strategyIds,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $$"""
SELECT strategy.id,
       CASE
           WHEN EXISTS (
               SELECT 1 FROM strategy_market_paper_runs run_presence
               WHERE run_presence.strategy_id = strategy.id)
             OR EXISTS (
               SELECT 1 FROM strategy_paper_skip_rollups rollup_presence
               WHERE rollup_presence.strategy_id = strategy.id)
           THEN COALESCE((
               SELECT SUM(COALESCE(run.realized_pnl_usd, 0))
               FROM strategy_market_paper_runs run
               WHERE run.strategy_id = strategy.id
                 AND run.status = '{{StrategyMarketPaperRunStatuses.Settled}}'), 0)
           ELSE COALESCE((
               SELECT SUM(fill.realized_pnl_usd)
               FROM paper_orders paper_order
               INNER JOIN paper_fills fill ON fill.paper_order_id = paper_order.id
               WHERE paper_order.strategy_id = strategy.id), 0)
             + COALESCE((
               SELECT SUM(settlement.realized_pnl_usd)
               FROM paper_position_settlements settlement
               WHERE (strategy.id = @FollowLeaderStrategyId
                      AND lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%')
                  OR (strategy.id <> @FollowLeaderStrategyId
                      AND lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy.code))), 0)
       END AS gross_pnl
FROM strategies strategy
WHERE strategy.id = ANY(@StrategyIds);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        command.Parameters.Add("StrategyIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = strategyIds;

        var result = new Dictionary<Guid, decimal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetGuid(0), reader.GetDecimal(1));
        }

        return result;
    }

    private static async Task<HistoricalGrossNetParityCandidateSelection>
        LoadHistoricalGrossNetParityStrategyCandidateKeysAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityCandidatePageRequest request,
            HistoricalGrossNetParityRankedStrategy strategy,
            CancellationToken cancellationToken)
    {
        var candidates = new List<HistoricalGrossNetParityCandidateKey>(request.PageSize);
        var sourceKinds = new[]
        {
            HistoricalGrossNetParitySourceKind.PaperRun,
            HistoricalGrossNetParitySourceKind.PaperPosition,
            HistoricalGrossNetParitySourceKind.PaperSettlement,
            HistoricalGrossNetParitySourceKind.PaperSellFill,
            HistoricalGrossNetParitySourceKind.LiveOrder
        };

        foreach (var sourceKind in sourceKinds)
        {
            var sourceOrder = GetHistoricalGrossNetParitySourceOrder(sourceKind);
            if (request.After is { } after && sourceOrder < after.SourceOrder)
            {
                continue;
            }

            var sourceCandidates = await LoadHistoricalGrossNetParitySourceCandidateKeysAsync(
                connection,
                transaction,
                request,
                strategy,
                sourceKind,
                request.PageSize - candidates.Count,
                cancellationToken);
            candidates.AddRange(sourceCandidates);
            if (candidates.Count == request.PageSize)
            {
                var last = candidates[^1];
                return new HistoricalGrossNetParityCandidateSelection(
                    candidates,
                    new HistoricalGrossNetParityCandidateCursor(
                        last.StrategyRank,
                        last.StrategyId,
                        last.SourceOrder,
                        last.OriginatedAtUtc,
                        last.SourceId),
                    false,
                    $"Loaded a full {sourceKind} page for strategy {strategy.StrategyId:D}.");
            }
        }

        var nextCursor = candidates.Count == 0
            ? request.After
            : new HistoricalGrossNetParityCandidateCursor(
                candidates[^1].StrategyRank,
                candidates[^1].StrategyId,
                candidates[^1].SourceOrder,
                candidates[^1].OriginatedAtUtc,
                candidates[^1].SourceId);
        return new HistoricalGrossNetParityCandidateSelection(
            candidates,
            nextCursor,
            true,
            $"Reached the source boundary for strategy {strategy.StrategyId:D}.");
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityCandidateKey>>
        LoadHistoricalGrossNetParitySourceCandidateKeysAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParityCandidatePageRequest request,
            HistoricalGrossNetParityRankedStrategy strategy,
            HistoricalGrossNetParitySourceKind sourceKind,
            int pageSize,
            CancellationToken cancellationToken)
    {
        var sourceOrder = GetHistoricalGrossNetParitySourceOrder(sourceKind);
        var sourceSql = GetHistoricalGrossNetParitySourceCandidateSql(sourceKind);
        await using var command = new NpgsqlCommand(
            $$"""
SELECT candidate.source_id, candidate.originated_at,
       candidate.row_version, candidate.ownership
FROM (
{{sourceSql}}
) candidate
WHERE candidate.originated_at < @CutoffUtc
  AND (NOT @HasAfter
       OR candidate.originated_at > @AfterOriginatedAt
       OR (candidate.originated_at = @AfterOriginatedAt
           AND lower(candidate.source_id::text) > @AfterSourceId))
ORDER BY candidate.originated_at, lower(candidate.source_id::text)
LIMIT @PageSize;
""",
            connection,
            transaction)
        {
            CommandTimeout = request.CommandTimeoutSeconds
        };
        var hasAfter = request.After is { SourceOrder: var afterSourceOrder } &&
            afterSourceOrder == sourceOrder;
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        command.Parameters.AddWithValue("StrategyId", strategy.StrategyId);
        command.Parameters.AddWithValue("StrategyCode", strategy.StrategyCode);
        command.Parameters.AddWithValue("CutoffUtc", UtcDateTime(request.CutoffUtc));
        command.Parameters.AddWithValue("CalculationVersion", request.CalculationVersion);
        command.Parameters.AddWithValue("PageSize", pageSize);
        command.Parameters.AddWithValue("HasAfter", hasAfter);
        command.Parameters.AddWithValue(
            "AfterOriginatedAt",
            hasAfter ? UtcDateTime(request.After!.OriginatedAtUtc) : DateTime.UnixEpoch);
        command.Parameters.AddWithValue(
            "AfterSourceId",
            hasAfter ? request.After!.SourceId.ToString("D").ToLowerInvariant() : string.Empty);

        var result = new List<HistoricalGrossNetParityCandidateKey>(pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityCandidateKey(
                sourceKind,
                reader.GetGuid(0),
                strategy.StrategyId,
                strategy.StrategyCode,
                strategy.StrategyRank,
                strategy.GrossPnlUsd,
                DateTimeOffsetFromUtc(reader.GetDateTime(1)),
                sourceOrder,
                reader.GetInt64(2),
                Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(3), false)));
        }

        return result;
    }

    private static int GetHistoricalGrossNetParitySourceOrder(
        HistoricalGrossNetParitySourceKind sourceKind) => sourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperRun => 1,
            HistoricalGrossNetParitySourceKind.PaperPosition => 2,
            HistoricalGrossNetParitySourceKind.PaperSettlement => 3,
            HistoricalGrossNetParitySourceKind.PaperSellFill => 4,
            HistoricalGrossNetParitySourceKind.LiveOrder => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
        };

    private static string GetHistoricalGrossNetParitySourceCandidateSql(
        HistoricalGrossNetParitySourceKind sourceKind) => sourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperRun =>
                $$"""
SELECT run.id AS source_id,
       CASE WHEN linked_run_fill.originated_at IS NULL
            THEN COALESCE(run.entered_at_utc, run.created_at_utc)
            ELSE linked_run_fill.originated_at END AS originated_at,
       run.xmin::text::bigint AS row_version,
       'None'::text AS ownership
FROM strategy_market_paper_runs run
LEFT JOIN LATERAL (
    SELECT MIN(fill.filled_at_utc) AS originated_at
    FROM paper_fills fill
    WHERE fill.paper_order_id = run.paper_order_id
) linked_run_fill ON true
WHERE run.strategy_id = @StrategyId
  AND run.status = '{{StrategyMarketPaperRunStatuses.Settled}}'
  AND NOT EXISTS (
      SELECT 1 FROM historical_gross_net_parity_audit audit
      WHERE audit.source_kind = 'PaperRun' AND audit.source_id = run.id
        AND audit.calculation_version = @CalculationVersion
        AND audit.operation_kind = 'AccountingDecision')
""",
            HistoricalGrossNetParitySourceKind.PaperPosition =>
                $$"""
SELECT position.id AS source_id, origin.originated_at,
       position.xmin::text::bigint AS row_version,
       'None'::text AS ownership
FROM paper_positions position
INNER JOIN LATERAL (
    SELECT MIN(fill.filled_at_utc) AS originated_at
    FROM paper_orders paper_order
    INNER JOIN paper_fills fill ON fill.paper_order_id = paper_order.id
    WHERE paper_order.copied_trader_wallet = position.copied_trader_wallet
      AND paper_order.asset_id = position.asset_id
      AND paper_order.side = '{{TradeSide.Buy}}'
) origin ON origin.originated_at IS NOT NULL
WHERE position.size_shares > 0
  AND ((@StrategyId = @FollowLeaderStrategyId
        AND lower(position.copied_trader_wallet) NOT LIKE 'strategy:%')
       OR (@StrategyId <> @FollowLeaderStrategyId
           AND lower(position.copied_trader_wallet) = lower('strategy:' || @StrategyCode)))
  AND NOT EXISTS (
      SELECT 1 FROM historical_gross_net_parity_audit audit
      WHERE audit.source_kind = 'PaperPosition' AND audit.source_id = position.id
        AND audit.calculation_version = @CalculationVersion
        AND audit.operation_kind = 'AccountingDecision')
""",
            HistoricalGrossNetParitySourceKind.PaperSettlement =>
                $$"""
SELECT settlement.id AS source_id, origin.originated_at,
       settlement.xmin::text::bigint AS row_version,
       'None'::text AS ownership
FROM paper_position_settlements settlement
INNER JOIN LATERAL (
    SELECT MIN(fill.filled_at_utc) AS originated_at
    FROM paper_orders paper_order
    INNER JOIN paper_fills fill ON fill.paper_order_id = paper_order.id
    WHERE paper_order.copied_trader_wallet = settlement.copied_trader_wallet
      AND paper_order.asset_id = settlement.asset_id
      AND paper_order.side = '{{TradeSide.Buy}}'
      AND fill.filled_at_utc <= settlement.settled_at_utc
) origin ON origin.originated_at IS NOT NULL
WHERE ((@StrategyId = @FollowLeaderStrategyId
        AND lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%')
       OR (@StrategyId <> @FollowLeaderStrategyId
           AND lower(settlement.copied_trader_wallet) = lower('strategy:' || @StrategyCode)))
  AND NOT EXISTS (
      SELECT 1 FROM strategy_market_paper_runs run_presence
      WHERE run_presence.strategy_id = @StrategyId)
  AND NOT EXISTS (
      SELECT 1 FROM strategy_paper_skip_rollups rollup_presence
      WHERE rollup_presence.strategy_id = @StrategyId)
  AND NOT EXISTS (
      SELECT 1 FROM historical_gross_net_parity_audit audit
      WHERE audit.source_kind = 'PaperSettlement' AND audit.source_id = settlement.id
        AND audit.calculation_version = @CalculationVersion
        AND audit.operation_kind = 'AccountingDecision')
""",
            HistoricalGrossNetParitySourceKind.PaperSellFill =>
                $$"""
SELECT sell_fill.id AS source_id, origin.originated_at,
       sell_fill.xmin::text::bigint AS row_version,
       'None'::text AS ownership
FROM paper_orders sell_order
INNER JOIN paper_fills sell_fill ON sell_fill.paper_order_id = sell_order.id
INNER JOIN LATERAL (
    SELECT MIN(buy_fill.filled_at_utc) AS originated_at
    FROM paper_orders buy_order
    INNER JOIN paper_fills buy_fill ON buy_fill.paper_order_id = buy_order.id
    WHERE buy_order.copied_trader_wallet = sell_order.copied_trader_wallet
      AND buy_order.asset_id = sell_order.asset_id
      AND buy_order.side = '{{TradeSide.Buy}}'
      AND (buy_fill.filled_at_utc < sell_fill.filled_at_utc
           OR (buy_fill.filled_at_utc = sell_fill.filled_at_utc
               AND ROW(lower(buy_order.id::text), lower(buy_fill.id::text)) <=
                   ROW(lower(sell_order.id::text), lower(sell_fill.id::text))))
) origin ON origin.originated_at IS NOT NULL
WHERE sell_order.strategy_id = @StrategyId
  AND sell_order.side = '{{TradeSide.Sell}}'
  AND NOT EXISTS (
      SELECT 1 FROM strategy_market_paper_runs run_presence
      WHERE run_presence.strategy_id = @StrategyId)
  AND NOT EXISTS (
      SELECT 1 FROM strategy_paper_skip_rollups rollup_presence
      WHERE rollup_presence.strategy_id = @StrategyId)
  AND NOT EXISTS (
      SELECT 1 FROM historical_gross_net_parity_audit audit
      WHERE audit.source_kind = 'PaperSellFill' AND audit.source_id = sell_fill.id
        AND audit.calculation_version = @CalculationVersion
        AND audit.operation_kind = 'AccountingDecision')
""",
            HistoricalGrossNetParitySourceKind.LiveOrder =>
                """
SELECT live_order.id AS source_id,
       COALESCE(live_order.submitted_at_utc, linked_fill.originated_at, live_order.created_at_utc)
           AS originated_at,
       live_order.row_version,
       live_order.historical_gross_net_parity_ownership AS ownership
FROM live_orders live_order
LEFT JOIN LATERAL (
    SELECT MIN(fill.filled_at_utc) AS originated_at
    FROM paper_fills fill
    WHERE fill.paper_order_id = live_order.paper_order_id
) linked_fill ON true
WHERE live_order.strategy_id = @StrategyId
  AND live_order.settled_at_utc IS NOT NULL
  AND live_order.realized_pnl_usd IS NOT NULL
  AND live_order.historical_gross_net_parity_ownership IN ('None', 'Pending')
  AND (live_order.historical_gross_net_parity_ownership = 'Pending'
       OR NOT EXISTS (
          SELECT 1 FROM historical_gross_net_parity_audit audit
          WHERE audit.source_kind = 'LiveOrder' AND audit.source_id = live_order.id
            AND audit.calculation_version = @CalculationVersion
            AND audit.operation_kind = 'AccountingDecision'))
""",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null)
        };

    private sealed record HistoricalGrossNetParityCandidateSelection(
        IReadOnlyList<HistoricalGrossNetParityCandidateKey> Candidates,
        HistoricalGrossNetParityCandidateCursor? NextCursor,
        bool ReachedBoundary,
        string Details)
    {
        public static HistoricalGrossNetParityCandidateSelection Empty(
            HistoricalGrossNetParityCandidateCursor? nextCursor,
            bool reachedBoundary,
            string details) => new([], nextCursor, reachedBoundary, details);
    }

    private static Guid[] GetHistoricalGrossNetParityIds(
        IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
        HistoricalGrossNetParitySourceKind sourceKind) =>
        candidates.Where(candidate => candidate.SourceKind == sourceKind)
            .Select(candidate => candidate.SourceId)
            .ToArray();

    private static Guid[] GetHistoricalGrossNetParityStrategyIds(
        IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates) =>
        candidates.Select(candidate => candidate.StrategyId).Distinct().ToArray();

    private static async Task<IReadOnlyList<HistoricalGrossNetParityPaperRunObservation>>
        LoadHistoricalGrossNetParityPagePaperRunsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken,
            bool ensureIndexedDonorPlan = false)
    {
        var ids = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperRun);
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
SELECT run.id, run.xmin::text::bigint, run.strategy_id, run.status, run.condition_id,
       run.selected_asset_id, run.selected_outcome, run.entry_price, run.stake_usd,
       run.size_shares, run.paper_order_id, run.entered_at_utc, run.settlement_price,
       run.settlement_value_usd, run.realized_pnl_usd, run.fee_usd,
       run.fee_accounting_status, run.fee_liquidity_role, run.fee_calculation_source,
       run.fee_rate, run.fee_exponent, run.fee_taker_only, run.fee_calculated_at_utc,
       run.net_realized_pnl_usd, run.settled_at_utc, run.retention_scope,
       jsonb_build_object(
           'run_id', lower(run.id::text), 'strategy_id', lower(run.strategy_id::text),
           'status', run.status, 'condition_id', run.condition_id,
           'asset_id', run.selected_asset_id, 'outcome', run.selected_outcome,
           'entry_price', run.entry_price, 'stake_usd', run.stake_usd,
           'size_shares', run.size_shares, 'paper_order_id', lower(run.paper_order_id::text),
           'entered_at_utc', run.entered_at_utc, 'settlement_price', run.settlement_price,
           'settlement_value_usd', run.settlement_value_usd,
           'realized_pnl_usd', run.realized_pnl_usd, 'fee_usd', run.fee_usd,
           'fee_accounting_status', run.fee_accounting_status,
           'fee_liquidity_role', run.fee_liquidity_role,
           'fee_calculation_source', run.fee_calculation_source,
           'fee_rate', run.fee_rate, 'fee_exponent', run.fee_exponent,
           'fee_taker_only', run.fee_taker_only,
           'fee_calculated_at_utc', run.fee_calculated_at_utc,
           'net_realized_pnl_usd', run.net_realized_pnl_usd,
           'settled_at_utc', run.settled_at_utc,
           'retention_scope', run.retention_scope)::text
FROM strategy_market_paper_runs run
WHERE run.id = ANY(@Ids)
ORDER BY run.strategy_id, COALESCE(run.entered_at_utc, run.created_at_utc), lower(run.id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        if (ensureIndexedDonorPlan)
        {
            await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        }
        var result = new List<HistoricalGrossNetParityPaperRunObservation>(ids.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityPaperRunObservation(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetGuid(10),
                reader.IsDBNull(11) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(11)),
                reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                reader.GetDecimal(15), reader.GetString(16), reader.GetString(17), reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                reader.IsDBNull(20) ? null : reader.GetInt32(20),
                reader.IsDBNull(21) ? null : reader.GetBoolean(21),
                reader.IsDBNull(22) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(22)),
                reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                reader.IsDBNull(24) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(24)),
                reader.GetString(25),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(26))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityPaperPositionObservation>>
        LoadHistoricalGrossNetParityPagePaperPositionsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken,
            bool ensureIndexedDonorPlan = false)
    {
        var ids = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperPosition);
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
WITH mapped AS (
    SELECT position.*,
           position.xmin::text::bigint AS row_version,
           CASE
               WHEN lower(position.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE @FollowLeaderStrategyId
           END AS strategy_id
    FROM paper_positions position
    LEFT JOIN strategies strategy_by_wallet
      ON strategy_by_wallet.code = lower(substring(position.copied_trader_wallet from 10))
     AND lower(position.copied_trader_wallet) LIKE 'strategy:%'
    WHERE position.id = ANY(@Ids)
)
SELECT id, row_version, strategy_id, copied_trader_wallet, asset_id, condition_id, outcome,
       size_shares, average_price, estimated_value_usd, unrealized_pnl_usd, fee_usd,
       fee_accounting_status, fee_liquidity_role, fee_calculation_source, fee_rate,
       fee_exponent, fee_taker_only, fee_calculated_at_utc, net_unrealized_pnl_usd,
       updated_at_utc,
       jsonb_build_object(
           'position_id', lower(id::text), 'strategy_id', lower(strategy_id::text),
           'wallet', copied_trader_wallet, 'asset_id', asset_id, 'condition_id', condition_id,
           'outcome', outcome, 'size_shares', size_shares, 'average_price', average_price,
           'fee_usd', fee_usd, 'fee_accounting_status', fee_accounting_status,
           'fee_liquidity_role', fee_liquidity_role,
           'fee_calculation_source', fee_calculation_source, 'fee_rate', fee_rate,
           'fee_exponent', fee_exponent, 'fee_taker_only', fee_taker_only,
           'fee_calculated_at_utc', fee_calculated_at_utc)::text
FROM mapped
ORDER BY strategy_id, lower(id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        if (ensureIndexedDonorPlan)
        {
            await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        }
        var result = new List<HistoricalGrossNetParityPaperPositionObservation>(ids.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityPaperPositionObservation(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetString(12), reader.GetString(13), reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                reader.IsDBNull(16) ? null : reader.GetInt32(16),
                reader.IsDBNull(17) ? null : reader.GetBoolean(17),
                reader.IsDBNull(18) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(18)),
                reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                DateTimeOffsetFromUtc(reader.GetDateTime(20)),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(21))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityPaperSettlementObservation>>
        LoadHistoricalGrossNetParityPagePaperSettlementsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken,
            bool ensureIndexedDonorPlan = false)
    {
        var ids = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperSettlement);
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
WITH mapped AS (
    SELECT settlement.*,
           settlement.xmin::text::bigint AS row_version,
           CASE
               WHEN lower(settlement.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE @FollowLeaderStrategyId
           END AS strategy_id
    FROM paper_position_settlements settlement
    LEFT JOIN strategies strategy_by_wallet
      ON strategy_by_wallet.code = lower(substring(settlement.copied_trader_wallet from 10))
     AND lower(settlement.copied_trader_wallet) LIKE 'strategy:%'
    WHERE settlement.id = ANY(@Ids)
)
SELECT id, row_version, strategy_id, copied_trader_wallet, asset_id, condition_id, outcome,
       settled_size_shares, average_price, cost_basis_usd, settlement_value_usd,
       realized_pnl_usd, fee_usd, fee_accounting_status, fee_liquidity_role,
       fee_calculation_source, fee_rate, fee_exponent, fee_taker_only,
       fee_calculated_at_utc, net_realized_pnl_usd, settled_at_utc,
       jsonb_build_object(
           'settlement_id', lower(id::text), 'strategy_id', lower(strategy_id::text),
           'wallet', copied_trader_wallet, 'asset_id', asset_id, 'condition_id', condition_id,
           'outcome', outcome, 'settled_size_shares', settled_size_shares,
           'average_price', average_price, 'cost_basis_usd', cost_basis_usd,
           'settlement_value_usd', settlement_value_usd,
           'realized_pnl_usd', realized_pnl_usd, 'fee_usd', fee_usd,
           'fee_accounting_status', fee_accounting_status,
           'fee_liquidity_role', fee_liquidity_role,
           'fee_calculation_source', fee_calculation_source,
           'fee_rate', fee_rate, 'fee_exponent', fee_exponent,
           'fee_taker_only', fee_taker_only, 'fee_calculated_at_utc', fee_calculated_at_utc,
           'net_realized_pnl_usd', net_realized_pnl_usd,
           'settled_at_utc', settled_at_utc)::text
FROM mapped
ORDER BY strategy_id, settled_at_utc, lower(id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        if (ensureIndexedDonorPlan)
        {
            await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        }
        var result = new List<HistoricalGrossNetParityPaperSettlementObservation>(ids.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityPaperSettlementObservation(
                reader.GetGuid(0), reader.GetInt64(1), reader.GetGuid(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11),
                reader.GetDecimal(12), reader.GetString(13), reader.GetString(14), reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                reader.IsDBNull(17) ? null : reader.GetInt32(17),
                reader.IsDBNull(18) ? null : reader.GetBoolean(18),
                reader.IsDBNull(19) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(19)),
                reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                DateTimeOffsetFromUtc(reader.GetDateTime(21)),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(22))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityPaperFillObservation>>
        LoadHistoricalGrossNetParityPagePaperFillsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken,
            bool ensureIndexedDonorPlan = false)
    {
        var runIds = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperRun);
        var positionIds = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperPosition);
        var settlementIds = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperSettlement);
        var sellFillIds = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperSellFill);
        if (runIds.Length + positionIds.Length + settlementIds.Length + sellFillIds.Length == 0) return [];

        await using var command = new NpgsqlCommand(
            """
WITH direct_orders AS (
    SELECT run.paper_order_id AS paper_order_id, NULL::timestamptz AS end_at,
           NULL::uuid AS end_order_id, NULL::uuid AS end_fill_id
    FROM strategy_market_paper_runs run
    WHERE run.id = ANY(@RunIds) AND run.paper_order_id IS NOT NULL
), target_pools AS (
    SELECT position.copied_trader_wallet AS wallet, position.asset_id,
           NULL::timestamptz AS end_at, NULL::uuid AS end_order_id,
           NULL::uuid AS end_fill_id
    FROM paper_positions position WHERE position.id = ANY(@PositionIds)
    UNION ALL
    SELECT settlement.copied_trader_wallet, settlement.asset_id, settlement.settled_at_utc,
           NULL::uuid, NULL::uuid
    FROM paper_position_settlements settlement WHERE settlement.id = ANY(@SettlementIds)
    UNION ALL
    SELECT paper_order.copied_trader_wallet, paper_order.asset_id, fill.filled_at_utc,
           paper_order.id, fill.id
    FROM paper_fills fill
    INNER JOIN paper_orders paper_order ON paper_order.id = fill.paper_order_id
    WHERE fill.id = ANY(@SellFillIds)
), selected_orders AS (
    SELECT direct_order.paper_order_id, direct_order.end_at,
           direct_order.end_order_id, direct_order.end_fill_id
    FROM direct_orders direct_order
    UNION
    SELECT paper_order.id, target_pool.end_at,
           target_pool.end_order_id, target_pool.end_fill_id
    FROM target_pools target_pool
    INNER JOIN paper_orders paper_order
      ON paper_order.copied_trader_wallet = target_pool.wallet
     AND paper_order.asset_id = target_pool.asset_id
), selected_fills AS (
    SELECT DISTINCT fill.id
    FROM selected_orders selected_order
    INNER JOIN paper_fills fill ON fill.paper_order_id = selected_order.paper_order_id
    WHERE selected_order.end_at IS NULL
       OR fill.filled_at_utc < selected_order.end_at
       OR (fill.filled_at_utc = selected_order.end_at AND
           (selected_order.end_fill_id IS NULL OR
            ROW(lower(selected_order.paper_order_id::text), lower(fill.id::text)) <=
            ROW(lower(selected_order.end_order_id::text), lower(selected_order.end_fill_id::text))))
)
SELECT fill.id, fill.xmin::text::bigint, paper_order.id, paper_order.xmin::text::bigint,
       paper_order.strategy_id, paper_order.copied_trader_wallet, paper_order.status,
       paper_order.side, paper_order.execution_source, paper_order.asset_id,
       paper_order.condition_id, paper_order.outcome, paper_order.price,
       paper_order.size_shares, paper_order.created_at_utc, fill.price,
       fill.size_shares, fill.filled_at_utc, fill.realized_pnl_usd, fill.fee_usd,
       fill.fee_accounting_status, fill.fee_liquidity_role, fill.fee_calculation_source,
       fill.fee_rate, fill.fee_exponent, fill.fee_taker_only,
       fill.fee_calculated_at_utc, fill.net_realized_pnl_usd,
       jsonb_build_object(
           'fill_id', lower(fill.id::text), 'paper_order_id', lower(paper_order.id::text),
           'strategy_id', lower(paper_order.strategy_id::text),
           'wallet', paper_order.copied_trader_wallet, 'status', paper_order.status,
           'side', paper_order.side, 'execution_source', paper_order.execution_source,
           'asset_id', paper_order.asset_id, 'condition_id', paper_order.condition_id,
           'outcome', paper_order.outcome, 'order_price', paper_order.price,
           'order_size_shares', paper_order.size_shares,
           'order_created_at_utc', paper_order.created_at_utc,
           'fill_price', fill.price, 'fill_size_shares', fill.size_shares,
           'filled_at_utc', fill.filled_at_utc, 'realized_pnl_usd', fill.realized_pnl_usd,
           'fee_usd', fill.fee_usd, 'fee_accounting_status', fill.fee_accounting_status,
           'fee_liquidity_role', fill.fee_liquidity_role,
           'fee_calculation_source', fill.fee_calculation_source,
           'fee_rate', fill.fee_rate, 'fee_exponent', fill.fee_exponent,
           'fee_taker_only', fill.fee_taker_only,
           'fee_calculated_at_utc', fill.fee_calculated_at_utc,
           'net_realized_pnl_usd', fill.net_realized_pnl_usd)::text
FROM selected_fills selected_fill
INNER JOIN paper_fills fill ON fill.id = selected_fill.id
INNER JOIN paper_orders paper_order ON paper_order.id = fill.paper_order_id
ORDER BY lower(paper_order.copied_trader_wallet), paper_order.asset_id,
         fill.filled_at_utc, lower(paper_order.id::text), lower(fill.id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        command.Parameters.Add("PositionIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = positionIds;
        command.Parameters.Add("SettlementIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = settlementIds;
        command.Parameters.Add("SellFillIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = sellFillIds;
        if (ensureIndexedDonorPlan)
        {
            await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        }
        var result = new List<HistoricalGrossNetParityPaperFillObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var fillId = reader.GetGuid(0);
            var paperOrderId = reader.GetGuid(2);
            var filledAt = DateTimeOffsetFromUtc(reader.GetDateTime(17));
            result.Add(new HistoricalGrossNetParityPaperFillObservation(
                fillId, reader.GetInt64(1), paperOrderId, reader.GetInt64(3), reader.GetGuid(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
                reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetDecimal(12),
                reader.GetDecimal(13), DateTimeOffsetFromUtc(reader.GetDateTime(14)), reader.GetDecimal(15),
                reader.GetDecimal(16), filledAt, reader.GetDecimal(18), reader.GetDecimal(19),
                reader.GetString(20), reader.GetString(21), reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                reader.IsDBNull(24) ? null : reader.GetInt32(24),
                reader.IsDBNull(25) ? null : reader.GetBoolean(25),
                reader.IsDBNull(26) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(26)),
                reader.IsDBNull(27) ? null : reader.GetDecimal(27),
                string.Create(CultureInfo.InvariantCulture, $"{filledAt:O}|{paperOrderId:D}|{fillId:D}"),
                NormalizeHistoricalGrossNetParityJson(reader.GetString(28))));
        }

        return result;
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityPaperSourceSelection>>
        LoadHistoricalGrossNetParityPageSourceSelectionsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var strategyIds = GetHistoricalGrossNetParityStrategyIds(candidates);
        if (strategyIds.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
SELECT strategy.id,
       COALESCE(raw.run_count, 0),
       COALESCE(rollup.run_count, 0),
       jsonb_build_object(
           'strategy_id', lower(strategy.id::text),
           'raw_run_count', COALESCE(raw.run_count, 0),
           'compact_rollup_run_count', COALESCE(rollup.run_count, 0))::text
FROM strategies strategy
LEFT JOIN (
    SELECT strategy_id, count(*)::bigint AS run_count
    FROM strategy_market_paper_runs WHERE strategy_id = ANY(@StrategyIds)
    GROUP BY strategy_id
) raw ON raw.strategy_id = strategy.id
LEFT JOIN (
    SELECT strategy_id, sum(run_count)::bigint AS run_count
    FROM strategy_paper_skip_rollups WHERE strategy_id = ANY(@StrategyIds)
    GROUP BY strategy_id
) rollup ON rollup.strategy_id = strategy.id
WHERE strategy.id = ANY(@StrategyIds)
ORDER BY lower(strategy.id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.Add("StrategyIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = strategyIds;
        var result = new List<HistoricalGrossNetParityPaperSourceSelection>(strategyIds.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = NormalizeHistoricalGrossNetParityJson(reader.GetString(3));
            var rawCount = reader.GetInt64(1);
            var rollupCount = reader.GetInt64(2);
            result.Add(new HistoricalGrossNetParityPaperSourceSelection(
                reader.GetGuid(0),
                rawCount > 0 || rollupCount > 0,
                rawCount,
                rollupCount,
                HashHistoricalGrossNetParityPayload(payload),
                payload));
        }

        return result;
    }

    private sealed record HistoricalGrossNetParityPageLiveRows(
        IReadOnlyList<HistoricalGrossNetParityTargetSnapshot> Targets,
        IReadOnlyList<HistoricalGrossNetParityLookupRequest> LookupRequests);

    private static async Task<HistoricalGrossNetParityPageLiveRows>
        LoadHistoricalGrossNetParityPageLiveTargetsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            HistoricalGrossNetParityCandidatePageRequest request,
            CancellationToken cancellationToken)
    {
        var ids = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.LiveOrder);
        if (ids.Length == 0) return new HistoricalGrossNetParityPageLiveRows([], []);
        var candidateById = candidates.Where(candidate => candidate.SourceKind == HistoricalGrossNetParitySourceKind.LiveOrder)
            .ToDictionary(candidate => candidate.SourceId);
        await using var command = new NpgsqlCommand(
            """
SELECT live_order.id, live_order.row_version, live_order.strategy_id, live_order.status,
       live_order.side, live_order.asset_id, live_order.condition_id, live_order.outcome,
       live_order.price, live_order.size_shares, live_order.created_at_utc,
       live_order.submitted_at_utc, live_order.filled_size, live_order.remaining_size,
       live_order.average_fill_price, live_order.filled_notional_usd,
       live_order.cost_basis_usd, live_order.fee_usd, live_order.fee_accounting_status,
       live_order.fee_liquidity_role, live_order.fee_calculation_source,
       live_order.fee_rate, live_order.fee_exponent, live_order.fee_taker_only,
       live_order.fee_calculated_at_utc, live_order.balance_effect_applied,
       live_order.realized_pnl_usd, live_order.net_realized_pnl_usd,
       live_order.settled_at_utc, live_order.paper_order_id,
       live_order.historical_gross_net_parity_ownership,
       linked_fill.originated_at_utc, linked_fill.lineage_payload_json,
       baseline.baseline_effect_kind, baseline.nominal_baseline_gross_pnl_usd,
       baseline.nominal_baseline_net_pnl_usd,
       venue.evidence_version, venue.evidence_payload_json::text,
       to_jsonb(live_order)::text
FROM live_orders live_order
LEFT JOIN LATERAL (
    SELECT MIN(fill.filled_at_utc) AS originated_at_utc,
           jsonb_build_object(
               'paper_order_id', lower(live_order.paper_order_id::text),
               'fills', COALESCE(
                   jsonb_agg(jsonb_build_object(
                       'fill_id', lower(fill.id::text),
                       'filled_at_utc', fill.filled_at_utc)
                       ORDER BY fill.filled_at_utc, lower(fill.id::text))
                       FILTER (WHERE fill.id IS NOT NULL),
                   '[]'::jsonb))::text AS lineage_payload_json
    FROM paper_fills fill WHERE fill.paper_order_id = live_order.paper_order_id
) linked_fill ON true
LEFT JOIN LATERAL (
    SELECT audit.baseline_effect_kind, audit.nominal_baseline_gross_pnl_usd,
           audit.nominal_baseline_net_pnl_usd
    FROM historical_gross_net_parity_audit audit
    WHERE audit.source_kind = 'LiveOrder' AND audit.source_id = live_order.id
      AND audit.calculation_version = @CalculationVersion
      AND audit.operation_kind = 'AccountingBaseline'
    LIMIT 1
) baseline ON true
LEFT JOIN LATERAL (
    SELECT audit.evidence_version, audit.evidence_payload_json
    FROM historical_gross_net_parity_audit audit
    WHERE audit.source_kind = 'LiveOrder' AND audit.source_id = live_order.id
      AND audit.calculation_version = @CalculationVersion
      AND audit.operation_kind = 'VenueReportedRevision'
    ORDER BY audit.authority_order_key DESC, audit.evidence_version DESC
    LIMIT 1
) venue ON true
WHERE live_order.id = ANY(@Ids)
ORDER BY live_order.strategy_id, live_order.settled_at_utc, lower(live_order.id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = request.CommandTimeoutSeconds
        };
        command.Parameters.Add("Ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = ids;
        command.Parameters.AddWithValue("CalculationVersion", request.CalculationVersion);
        var targets = new List<HistoricalGrossNetParityTargetSnapshot>(ids.Length);
        var lookups = new List<HistoricalGrossNetParityLookupRequest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetGuid(0);
            var candidate = candidateById[id];
            var rowVersion = reader.GetInt64(1);
            var strategyId = reader.GetGuid(2);
            var side = reader.GetString(4);
            var assetId = reader.GetString(5);
            var conditionId = reader.GetString(6);
            var outcome = reader.GetString(7);
            var price = reader.GetDecimal(8);
            var orderSize = reader.GetDecimal(9);
            var createdAt = DateTimeOffsetFromUtc(reader.GetDateTime(10));
            var submittedAt = reader.IsDBNull(11) ? (DateTimeOffset?)null : DateTimeOffsetFromUtc(reader.GetDateTime(11));
            var filledSize = reader.GetDecimal(12);
            var averageFillPrice = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14);
            var filledNotional = reader.GetDecimal(15);
            var costBasis = reader.GetDecimal(16);
            var fee = reader.GetDecimal(17);
            var feeStatus = reader.GetString(18);
            var feeRole = reader.GetString(19);
            var feeSource = reader.GetString(20);
            var feeRate = reader.IsDBNull(21) ? (decimal?)null : reader.GetDecimal(21);
            var feeExponent = reader.IsDBNull(22) ? (int?)null : reader.GetInt32(22);
            var feeTakerOnly = reader.IsDBNull(23) ? (bool?)null : reader.GetBoolean(23);
            var feeCalculatedAt = reader.IsDBNull(24) ? (DateTimeOffset?)null : DateTimeOffsetFromUtc(reader.GetDateTime(24));
            var balanceApplied = reader.GetBoolean(25);
            var gross = reader.GetDecimal(26);
            var net = reader.IsDBNull(27) ? (decimal?)null : reader.GetDecimal(27);
            var settledAt = DateTimeOffsetFromUtc(reader.GetDateTime(28));
            var paperOrderId = reader.IsDBNull(29) ? (Guid?)null : reader.GetGuid(29);
            var ownership = Enum.Parse<HistoricalGrossNetParityOwnership>(reader.GetString(30), false);
            var linkedFillAt = reader.IsDBNull(31) ? (DateTimeOffset?)null : DateTimeOffsetFromUtc(reader.GetDateTime(31));
            var lineagePayload = reader.IsDBNull(32)
                ? "{}"
                : NormalizeHistoricalGrossNetParityJson(reader.GetString(32));
            var canonicalPayload = NormalizeHistoricalGrossNetParityJson(reader.GetString(38));
            var venueVersion = reader.IsDBNull(36) ? null : reader.GetString(36);
            var venuePayload = reader.IsDBNull(37) ? null : NormalizeHistoricalGrossNetParityJson(reader.GetString(37));
            var componentPayload = venuePayload ?? "{}";
            var targetHash = HashHistoricalGrossNetParityPayload(canonicalPayload);
            var lineageHash = HashHistoricalGrossNetParityPayload(lineagePayload);
            var componentHash = HashHistoricalGrossNetParityPayload(componentPayload);
            var bindingHash = HistoricalGrossNetParityBindingV1.Compute(
                targetHash,
                lineageHash,
                componentHash);
            var venueAuthoritative =
                string.Equals(feeStatus, "VenueReported", StringComparison.Ordinal) &&
                venueVersion is not null && venuePayload is not null;
            var localAuthoritative =
                string.Equals(feeStatus, "Calculated", StringComparison.Ordinal) &&
                fee >= 0m && feeCalculatedAt is not null &&
                IsHistoricalGrossNetParityExactLocalSource(feeSource, fee, feeRole, feeRate, feeExponent, feeTakerOnly);
            var formulaComplete =
                fee >= 0m && net is not null && net.Value == gross - fee &&
                (localAuthoritative || venueAuthoritative);
            var exactEvidence = new List<HistoricalGrossNetParityEvidenceReferenceV1>();
            if (venueAuthoritative)
            {
                exactEvidence.Add(new HistoricalGrossNetParityEvidenceReferenceV1(
                    "LiveVenueReported", venueVersion!, componentHash,
                    HistoricalGrossNetParitySourceKind.LiveOrder, id));
            }
            else if (localAuthoritative)
            {
                exactEvidence.Add(new HistoricalGrossNetParityEvidenceReferenceV1(
                    "LiveLocalFormula", feeSource,
                    HashHistoricalGrossNetParityPayload(JsonSerializer.Serialize(new
                    {
                        feeSource, fee, feeRole, feeRate, feeExponent, feeTakerOnly, feeCalculatedAt
                    })),
                    HistoricalGrossNetParitySourceKind.LiveOrder, id));
            }

            var eligibility = formulaComplete
                ? HistoricalGrossNetParityExactEligibility.ExistingExactPreserved
                : venueAuthoritative || localAuthoritative
                    ? HistoricalGrossNetParityExactEligibility.AuthoritativeNetRepair
                    : !string.IsNullOrWhiteSpace(conditionId) && !string.IsNullOrWhiteSpace(assetId) &&
                      price is > 0m and < 1m && filledSize > 0m
                        ? HistoricalGrossNetParityExactEligibility.LocalLookupRequired
                        : HistoricalGrossNetParityExactEligibility.FallbackRequired;
            var basis = filledNotional > 0m
                ? filledNotional
                : filledSize > 0m
                    ? price * filledSize
                    : costBasis > 0m
                        ? Math.Max(0m, costBasis - fee)
                        : 0m;
            var bindingEvidence = new List<HistoricalGrossNetParityEvidenceReferenceV1>();
            if (submittedAt is null && linkedFillAt is not null && paperOrderId is not null)
            {
                bindingEvidence.Add(new HistoricalGrossNetParityEvidenceReferenceV1(
                    "live-linked-paper-fill-lineage",
                    $"paper-order:{paperOrderId.Value:D}", lineageHash,
                    HistoricalGrossNetParitySourceKind.PaperOrderFillLineage,
                    paperOrderId));
            }

            var baselineKind = reader.IsDBNull(33)
                ? (HistoricalGrossNetParityBaselineEffectKind?)null
                : Enum.Parse<HistoricalGrossNetParityBaselineEffectKind>(reader.GetString(33), false);
            targets.Add(new HistoricalGrossNetParityTargetSnapshot(
                HistoricalGrossNetParitySourceKind.LiveOrder, id, strategyId,
                candidate.StrategyRank, candidate.StrategyGrossPnlUsd, rowVersion,
                candidate.OriginatedAtUtc, settledAt,
                gross, basis, fee, feeStatus, feeRole, feeSource, feeRate, feeExponent,
                feeTakerOnly, feeCalculatedAt, net, balanceApplied, ownership,
                targetHash, lineageHash, componentHash, eligibility,
                venueAuthoritative || localAuthoritative ? fee : null,
                exactEvidence, null, null, 0m, [], baselineKind,
                reader.IsDBNull(34) ? null : reader.GetDecimal(34),
                reader.IsDBNull(35) ? null : reader.GetDecimal(35),
                canonicalPayload, lineagePayload, componentPayload, bindingHash,
                paperOrderId is null ? null : $"paper-order:{paperOrderId.Value:D}",
                venueAuthoritative
                    ? HistoricalGrossNetParityDonorRepresentation.VenueReportedLive
                    : localAuthoritative
                        ? HistoricalGrossNetParityDonorRepresentation.LocalExactLive
                        : null,
                venueAuthoritative
                    ? (int)HistoricalGrossNetParityDonorRepresentation.VenueReportedLive
                    : localAuthoritative
                        ? (int)HistoricalGrossNetParityDonorRepresentation.LocalExactLive
                        : 0,
                $"live-order:{id:D}",
                bindingEvidence));

            if (eligibility == HistoricalGrossNetParityExactEligibility.LocalLookupRequired)
            {
                var lookupPayload = NormalizeHistoricalGrossNetParityJson(JsonSerializer.Serialize(new
                {
                    source_kind = "LiveOrder",
                    source_id = id,
                    condition_id = conditionId,
                    asset_id = assetId,
                    side,
                    outcome,
                    quantity = filledSize,
                    price = averageFillPrice ?? price,
                    liquidity_role = feeRole,
                    gross_roi_basis_usd = basis
                }));
                lookups.Add(new HistoricalGrossNetParityLookupRequest(
                    HashHistoricalGrossNetParityPayload(lookupPayload),
                    HistoricalGrossNetParitySourceKind.LiveOrder,
                    id, strategyId, conditionId, assetId, side, outcome, basis,
                    filledSize, averageFillPrice ?? price, feeRole,
                    HistoricalGrossNetParityLookupFeeApplicationKind.TotalContributionFee,
                    $"live-local:{id:D}:{HistoricalGrossNetParityExactCurveSource}",
                    $"live-local:{id:D}:{HistoricalGrossNetParityExactCurveSource}",
                    lookupPayload));
            }
        }

        return new HistoricalGrossNetParityPageLiveRows(targets, lookups);
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityTargetConflict>>
        LoadHistoricalGrossNetParityPageConflictsAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<HistoricalGrossNetParityCandidateKey> candidates,
            HistoricalGrossNetParityCandidatePageRequest request,
            CancellationToken cancellationToken)
    {
        var runIds = GetHistoricalGrossNetParityIds(candidates, HistoricalGrossNetParitySourceKind.PaperRun);
        if (runIds.Length == 0) return [];
        await using var command = new NpgsqlCommand(
            """
SELECT 'settled_run_realized_pnl_null'::text, 'PaperRun'::text,
       run.id, run.strategy_id,
       'A Settled Gross-selected run has NULL realized_pnl_usd.'::text
FROM strategy_market_paper_runs run
WHERE run.id = ANY(@RunIds) AND run.status = 'Settled' AND run.realized_pnl_usd IS NULL
ORDER BY run.strategy_id, lower(run.id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = request.CommandTimeoutSeconds
        };
        command.Parameters.Add("RunIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid).Value = runIds;
        var result = new List<HistoricalGrossNetParityTargetConflict>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistoricalGrossNetParityTargetConflict(
                reader.GetString(0),
                Enum.Parse<HistoricalGrossNetParitySourceKind>(reader.GetString(1), false),
                reader.GetGuid(2), reader.GetGuid(3), reader.GetString(4)));
        }

        return result;
    }

    private sealed record HistoricalGrossNetParityDonorReplayCharge(
        string SourceChargeId,
        Guid PaperOrderId,
        decimal AmountUsd,
        string EvidenceHash,
        string EvidenceJson);

    private sealed record HistoricalGrossNetParityFreshPaperEvidence(
        IReadOnlyDictionary<
            (HistoricalGrossNetParitySourceKind SourceKind, Guid SourceId), string> ComponentHashes,
        IReadOnlyDictionary<
            (HistoricalGrossNetParitySourceKind SourceKind, Guid SourceId), IReadOnlyList<Guid>>
            RemainingPaperOrderIds);

    private sealed record HistoricalGrossNetParityDonorReplayState(
        decimal SizeShares,
        decimal AveragePrice,
        decimal FeeUsd,
        bool EntryFeesExact,
        bool HasPriorSell,
        IReadOnlyList<HistoricalGrossNetParityDonorReplayCharge> EntryCharges)
    {
        public static HistoricalGrossNetParityDonorReplayState Empty { get; } =
            new(0m, 0m, 0m, true, false, []);
    }

    private sealed record HistoricalGrossNetParityDonorSellReplay(
        HistoricalGrossNetParityDonorReplayState Before,
        HistoricalGrossNetParityDonorReplayState After,
        decimal GrossPnlUsd,
        decimal NetPnlUsd,
        HistoricalGrossNetParityComponentAllocationV1? EntryAllocation);

    private sealed record HistoricalGrossNetParityDonorReplayEvent(
        HistoricalGrossNetParityPaperFillObservation Fill,
        HistoricalGrossNetParityDonorReplayState Before,
        HistoricalGrossNetParityDonorReplayState After,
        HistoricalGrossNetParityDonorSellReplay? Sell);

    private static async Task<HistoricalGrossNetParityFreshPaperEvidence>
        LoadHistoricalGrossNetParityFreshPaperComponentHashesAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            Guid strategyId,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var candidates = await LoadHistoricalGrossNetParityFreshPaperEvidenceCandidatesAsync(
            connection, transaction, strategyId, commandTimeoutSeconds, cancellationToken,
            ensureIndexedDonorPlan: true);
        if (candidates.Count == 0)
        {
            return new HistoricalGrossNetParityFreshPaperEvidence(
                new Dictionary<(HistoricalGrossNetParitySourceKind, Guid), string>(),
                new Dictionary<(HistoricalGrossNetParitySourceKind, Guid), IReadOnlyList<Guid>>());
        }

        var fills = await LoadHistoricalGrossNetParityPagePaperFillsAsync(
            connection, transaction, candidates, commandTimeoutSeconds, cancellationToken,
            ensureIndexedDonorPlan: true);
        var runs = await LoadHistoricalGrossNetParityPagePaperRunsAsync(
            connection, transaction, candidates, commandTimeoutSeconds, cancellationToken,
            ensureIndexedDonorPlan: true);
        var positions = await LoadHistoricalGrossNetParityPagePaperPositionsAsync(
            connection, transaction, candidates, commandTimeoutSeconds, cancellationToken,
            ensureIndexedDonorPlan: true);
        var settlements = await LoadHistoricalGrossNetParityPagePaperSettlementsAsync(
            connection, transaction, candidates, commandTimeoutSeconds, cancellationToken,
            ensureIndexedDonorPlan: true);
        var fillsByOrder = fills.GroupBy(fill => fill.PaperOrderId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var replayByPool = fills.GroupBy(fill => (fill.CopiedTraderWallet, fill.AssetId))
            .ToDictionary(
                group => group.Key,
                group => ReplayHistoricalGrossNetParityDonorPool(group.ToArray()));
        var hashes = new Dictionary<(HistoricalGrossNetParitySourceKind, Guid), string>();
        var remainingPaperOrderIds = new Dictionary<
            (HistoricalGrossNetParitySourceKind, Guid), IReadOnlyList<Guid>>();

        foreach (var run in runs)
        {
            if (!string.Equals(run.FeeCalculationSource, "mixed", StringComparison.Ordinal) ||
                run.PaperOrderId is null ||
                !fillsByOrder.TryGetValue(run.PaperOrderId.Value, out var linked) ||
                linked.Length == 0 || linked.Any(fill => !IsHistoricalGrossNetParityExactFill(fill)) ||
                RoundHistoricalGrossNetParity8(linked.Sum(fill => fill.FeeUsd)) != run.FeeUsd)
            {
                continue;
            }

            var components = linked.Select(fill =>
                    CreateHistoricalGrossNetParityDirectComponent(
                        $"paper-run-entry:{run.RunId:D}:{fill.FillId:D}",
                        $"paper-fill:{fill.FillId:D}:entry",
                        fill.FeeUsd,
                        fill.CanonicalPayloadJson))
                .ToArray();
            hashes[(HistoricalGrossNetParitySourceKind.PaperRun, run.RunId)] =
                HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(components);
        }

        foreach (var position in positions)
        {
            if (!replayByPool.TryGetValue(
                    (position.CopiedTraderWallet, position.AssetId),
                    out var replay))
            {
                continue;
            }

            var state = replay.Count == 0
                ? HistoricalGrossNetParityDonorReplayState.Empty
                : replay[^1].After;
            if (!state.EntryFeesExact || state.EntryCharges.Count == 0 ||
                RoundHistoricalGrossNetParity8(state.SizeShares) != position.SizeShares ||
                RoundHistoricalGrossNetParity8(state.AveragePrice) != position.AveragePrice ||
                RoundHistoricalGrossNetParity8(state.FeeUsd) != position.FeeUsd)
            {
                continue;
            }

            remainingPaperOrderIds[(HistoricalGrossNetParitySourceKind.PaperPosition, position.PositionId)] =
                state.EntryCharges.Select(charge => charge.PaperOrderId).Distinct().Order().ToArray();
            if (!string.Equals(position.FeeCalculationSource, "mixed", StringComparison.Ordinal))
            {
                continue;
            }

            var allocationId = $"paper-entry-remaining:PaperPosition:{position.PositionId:D}";
            var poolId = CreateHistoricalGrossNetParityDonorPoolId(
                position.CopiedTraderWallet,
                position.AssetId);
            var component = state.HasPriorSell
                ? CreateHistoricalGrossNetParityRemainingComponent(
                    allocationId,
                    state,
                    position.CopiedTraderWallet,
                    position.AssetId,
                    position.CanonicalPayloadJson)
                : CreateHistoricalGrossNetParityEntryPoolComponent(
                    allocationId,
                    RoundHistoricalGrossNetParity8(state.FeeUsd),
                    state,
                    poolId,
                    position.CanonicalPayloadJson,
                    null);
            hashes[(HistoricalGrossNetParitySourceKind.PaperPosition, position.PositionId)] =
                HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash([component]);
        }

        foreach (var settlement in settlements)
        {
            if (!replayByPool.TryGetValue(
                    (settlement.CopiedTraderWallet, settlement.AssetId),
                    out var replay) ||
                replay.Any(value => value.Fill.FilledAtUtc == settlement.SettledAtUtc))
            {
                continue;
            }

            var state = replay.LastOrDefault(value => value.Fill.FilledAtUtc < settlement.SettledAtUtc)?.After ??
                HistoricalGrossNetParityDonorReplayState.Empty;
            if (!state.EntryFeesExact || state.EntryCharges.Count == 0 ||
                RoundHistoricalGrossNetParity8(state.SizeShares) != settlement.SettledSizeShares ||
                RoundHistoricalGrossNetParity8(state.AveragePrice) != settlement.AveragePrice ||
                RoundHistoricalGrossNetParity8(state.AveragePrice * state.SizeShares) != settlement.CostBasisUsd ||
                RoundHistoricalGrossNetParity8(state.FeeUsd) != settlement.FeeUsd)
            {
                continue;
            }

            remainingPaperOrderIds[(HistoricalGrossNetParitySourceKind.PaperSettlement, settlement.SettlementId)] =
                state.EntryCharges.Select(charge => charge.PaperOrderId).Distinct().Order().ToArray();
            if (!string.Equals(settlement.FeeCalculationSource, "mixed", StringComparison.Ordinal))
            {
                continue;
            }

            var allocationId = $"paper-entry-remaining:PaperSettlement:{settlement.SettlementId:D}";
            var poolId = CreateHistoricalGrossNetParityDonorPoolId(
                settlement.CopiedTraderWallet,
                settlement.AssetId);
            var component = state.HasPriorSell
                ? CreateHistoricalGrossNetParityRemainingComponent(
                    allocationId,
                    state,
                    settlement.CopiedTraderWallet,
                    settlement.AssetId,
                    settlement.CanonicalPayloadJson)
                : CreateHistoricalGrossNetParityEntryPoolComponent(
                    allocationId,
                    RoundHistoricalGrossNetParity8(state.FeeUsd),
                    state,
                    poolId,
                    settlement.CanonicalPayloadJson,
                    null);
            hashes[(HistoricalGrossNetParitySourceKind.PaperSettlement, settlement.SettlementId)] =
                HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash([component]);
        }

        foreach (var fill in fills.Where(fill => string.Equals(fill.OrderSide, "Sell", StringComparison.Ordinal)))
        {
            if (!replayByPool.TryGetValue((fill.CopiedTraderWallet, fill.AssetId), out var replay))
            {
                continue;
            }
            var sell = replay.SingleOrDefault(value => value.Fill.FillId == fill.FillId)?.Sell;
            if (sell is null || sell.EntryAllocation is null ||
                !IsHistoricalGrossNetParityExactFill(fill) || fill.NetRealizedPnlUsd is null ||
                fill.RealizedPnlUsd != sell.GrossPnlUsd ||
                fill.NetRealizedPnlUsd.Value != sell.NetPnlUsd)
            {
                continue;
            }

            var exit = CreateHistoricalGrossNetParityDirectComponent(
                $"paper-exit-allocation:{fill.FillId:D}",
                $"paper-fill:{fill.FillId:D}:exit",
                fill.FeeUsd,
                fill.CanonicalPayloadJson);
            hashes[(HistoricalGrossNetParitySourceKind.PaperSellFill, fill.FillId)] =
                HistoricalGrossNetParityComponentGraphV1.ComputeComponentHash(
                    [sell.EntryAllocation, exit]);
        }

        return new HistoricalGrossNetParityFreshPaperEvidence(hashes, remainingPaperOrderIds);
    }

    private static async Task<IReadOnlyList<HistoricalGrossNetParityCandidateKey>>
        LoadHistoricalGrossNetParityFreshPaperEvidenceCandidatesAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
        Guid strategyId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken,
        bool ensureIndexedDonorPlan = false)
    {
        await using var command = new NpgsqlCommand(
            """
WITH candidate_strategy AS MATERIALIZED (
    SELECT id, code FROM strategies WHERE id=@StrategyId
), uses_runs AS MATERIALIZED (
    SELECT EXISTS (
        SELECT 1 FROM strategy_market_paper_runs WHERE strategy_id=@StrategyId
        UNION ALL
        SELECT 1 FROM strategy_paper_skip_rollups WHERE strategy_id=@StrategyId) AS value
)
SELECT 'PaperRun'::text, run.id, run.xmin::text::bigint
FROM strategy_market_paper_runs run
WHERE run.strategy_id=@StrategyId AND (SELECT value FROM uses_runs)
  AND run.status='Settled' AND run.fee_calculation_source='mixed'
UNION ALL
SELECT 'PaperPosition', position.id, position.xmin::text::bigint
FROM paper_positions position CROSS JOIN candidate_strategy strategy
WHERE position.size_shares>0
  AND ((strategy.id=@FollowLeaderStrategyId
        AND lower(position.copied_trader_wallet) NOT LIKE 'strategy:%')
       OR (strategy.id<>@FollowLeaderStrategyId
        AND lower(position.copied_trader_wallet)=lower('strategy:'||strategy.code)))
UNION ALL
SELECT 'PaperSettlement', settlement.id, settlement.xmin::text::bigint
FROM paper_position_settlements settlement CROSS JOIN candidate_strategy strategy
WHERE NOT (SELECT value FROM uses_runs)
  AND ((strategy.id=@FollowLeaderStrategyId
        AND lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%')
       OR (strategy.id<>@FollowLeaderStrategyId
        AND lower(settlement.copied_trader_wallet)=lower('strategy:'||strategy.code)))
UNION ALL
SELECT 'PaperSellFill', fill.id, fill.xmin::text::bigint
FROM paper_orders paper_order
INNER JOIN paper_fills fill ON fill.paper_order_id=paper_order.id
WHERE paper_order.strategy_id=@StrategyId AND paper_order.side='Sell'
  AND NOT (SELECT value FROM uses_runs);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("StrategyId", strategyId);
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        if (ensureIndexedDonorPlan)
        {
            await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(command, cancellationToken);
        }
        var result = new List<HistoricalGrossNetParityCandidateKey>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceKind = Enum.Parse<HistoricalGrossNetParitySourceKind>(reader.GetString(0), false);
            result.Add(new HistoricalGrossNetParityCandidateKey(
                sourceKind,
                reader.GetGuid(1),
                strategyId,
                string.Empty,
                0,
                0m,
                DateTimeOffset.UnixEpoch,
                sourceKind switch
                {
                    HistoricalGrossNetParitySourceKind.PaperRun => 1,
                    HistoricalGrossNetParitySourceKind.PaperPosition => 2,
                    HistoricalGrossNetParitySourceKind.PaperSettlement => 3,
                    _ => 4
                },
                reader.GetInt64(2),
                HistoricalGrossNetParityOwnership.None));
        }
        return result;
    }

    private static IReadOnlyList<HistoricalGrossNetParityDonorReplayEvent>
        ReplayHistoricalGrossNetParityDonorPool(
            IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> fills)
    {
        var ordered = fills
            .OrderBy(fill => fill.FilledAtUtc)
            .ThenBy(fill => fill.PaperOrderId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(fill => fill.FillId.ToString("D"), StringComparer.Ordinal)
            .ToArray();
        var events = new List<HistoricalGrossNetParityDonorReplayEvent>(ordered.Length);
        var state = HistoricalGrossNetParityDonorReplayState.Empty;
        foreach (var fill in ordered)
        {
            var before = state;
            HistoricalGrossNetParityDonorSellReplay? sell = null;
            if (string.Equals(fill.OrderSide, "Buy", StringComparison.Ordinal))
            {
                var newSize = RoundHistoricalGrossNetParity8(state.SizeShares + fill.FillSizeShares);
                if (newSize <= 0m)
                {
                    continue;
                }
                var average = RoundHistoricalGrossNetParity8(
                    ((state.SizeShares * state.AveragePrice) +
                     (fill.FillPrice * fill.FillSizeShares)) / newSize);
                var newFee = RoundHistoricalGrossNetParity8(state.FeeUsd + fill.FeeUsd);
                var sourceChargeId = $"paper-fill:{fill.FillId:D}:entry";
                var sourceEvidence = JsonSerializer.Serialize(new
                {
                    version = "HistoricalGrossNetParityPaperBuySourceChargeV1",
                    fill.FillId,
                    fill.PaperOrderId,
                    sourceChargeId,
                    amountUsd = newFee - state.FeeUsd,
                    fill.CanonicalPayloadJson
                });
                state = new HistoricalGrossNetParityDonorReplayState(
                    newSize,
                    average,
                    newFee,
                    state.EntryFeesExact && IsHistoricalGrossNetParityExactFill(fill),
                    state.HasPriorSell,
                    state.EntryCharges.Append(new HistoricalGrossNetParityDonorReplayCharge(
                        sourceChargeId,
                        fill.PaperOrderId,
                        newFee - state.FeeUsd,
                        HashHistoricalGrossNetParityPayload(sourceEvidence),
                        sourceEvidence)).ToArray());
            }
            else if (string.Equals(fill.OrderSide, "Sell", StringComparison.Ordinal) &&
                     state.SizeShares > 0m)
            {
                var sellSize = RoundHistoricalGrossNetParity8(fill.FillSizeShares);
                var currentSize = RoundHistoricalGrossNetParity8(state.SizeShares);
                var sellFraction = Math.Min(1m, sellSize / currentSize);
                var rawAllocation = state.FeeUsd * sellFraction;
                var grossRaw = (fill.FillPrice - state.AveragePrice) * sellSize;
                var netRaw = grossRaw - rawAllocation - fill.FeeUsd;
                var gross8 = RoundHistoricalGrossNetParity8(grossRaw);
                var net8 = RoundHistoricalGrossNetParity8(netRaw);
                var effectiveEntry = (gross8 - net8) - fill.FeeUsd;
                var newSize = RoundHistoricalGrossNetParity8(Math.Max(0m, currentSize - sellSize));
                var remainingFraction = Math.Max(0m, Math.Min(1m, newSize / currentSize));
                var remainingFee = RoundHistoricalGrossNetParity8(state.FeeUsd * remainingFraction);
                var decrement = state.FeeUsd - remainingFee;
                var residual = effectiveEntry - decrement;
                var poolId = CreateHistoricalGrossNetParityDonorPoolId(
                    fill.CopiedTraderWallet, fill.AssetId);
                var movementEvidence = JsonSerializer.Serialize(new
                {
                    version = "HistoricalGrossNetParityPaperSellPoolMovementV1",
                    Wallet = fill.CopiedTraderWallet,
                    fill.AssetId,
                    fill.FillId,
                    poolId,
                    poolAllocatedRaw = rawAllocation,
                    remainingBeforeUsd = state.FeeUsd,
                    poolDecrement8 = decrement,
                    remainingPool8 = remainingFee,
                    residual8 = residual,
                    effectiveEntrySlice8 = effectiveEntry
                });
                var movement = new HistoricalGrossNetParityPoolMovementV1(
                    poolId,
                    rawAllocation,
                    state.FeeUsd,
                    decrement,
                    remainingFee,
                    residual,
                    HashHistoricalGrossNetParityPayload(movementEvidence),
                    movementEvidence);
                var entry = state.EntryFeesExact && state.EntryCharges.Count > 0 && effectiveEntry >= 0m
                    ? CreateHistoricalGrossNetParityEntryPoolComponent(
                        $"paper-entry-allocation:{fill.FillId:D}",
                        effectiveEntry,
                        state,
                        poolId,
                        movementEvidence,
                        movement)
                    : null;
                state = new HistoricalGrossNetParityDonorReplayState(
                    newSize,
                    newSize == 0m ? 0m : state.AveragePrice,
                    newSize == 0m ? 0m : remainingFee,
                    newSize == 0m || state.EntryFeesExact,
                    true,
                    newSize == 0m ? [] : state.EntryCharges);
                sell = new HistoricalGrossNetParityDonorSellReplay(
                    before, state, gross8, net8, entry);
            }
            events.Add(new HistoricalGrossNetParityDonorReplayEvent(fill, before, state, sell));
        }
        return events;
    }

    private static HistoricalGrossNetParityComponentAllocationV1
        CreateHistoricalGrossNetParityRemainingComponent(
            string allocationId,
            HistoricalGrossNetParityDonorReplayState state,
            string wallet,
            string assetId,
            string contextPayload)
    {
        var remainingFee = RoundHistoricalGrossNetParity8(state.FeeUsd);
        var poolId = CreateHistoricalGrossNetParityDonorPoolId(wallet, assetId);
        var movementEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityRemainingEntryPoolMovementV1",
            allocationId,
            poolId,
            remainingFee,
            contextEvidenceHash = HashHistoricalGrossNetParityPayload(contextPayload)
        });
        var movement = new HistoricalGrossNetParityPoolMovementV1(
            poolId, remainingFee, remainingFee, remainingFee, 0m, 0m,
            HashHistoricalGrossNetParityPayload(movementEvidence), movementEvidence);
        return CreateHistoricalGrossNetParityEntryPoolComponent(
            allocationId, remainingFee, state, poolId, contextPayload, movement);
    }

    private static HistoricalGrossNetParityComponentAllocationV1
        CreateHistoricalGrossNetParityEntryPoolComponent(
            string allocationId,
            decimal amountUsd,
            HistoricalGrossNetParityDonorReplayState state,
            string poolId,
            string contextPayload,
            HistoricalGrossNetParityPoolMovementV1? movement)
    {
        var charges = state.EntryCharges.Select(charge =>
            new HistoricalGrossNetParitySourceChargeV1(
                charge.SourceChargeId, charge.AmountUsd, charge.EvidenceHash, charge.EvidenceJson)).ToArray();
        var contextHash = HashHistoricalGrossNetParityPayload(contextPayload);
        var edges = state.EntryCharges.Select(charge =>
        {
            var edgeEvidence = JsonSerializer.Serialize(new
            {
                version = "HistoricalGrossNetParityEntryPoolCoverageV1",
                charge.SourceChargeId,
                poolId,
                allocationId,
                chargeEvidenceHash = charge.EvidenceHash,
                contextEvidenceHash = contextHash
            });
            return new HistoricalGrossNetParityChargeCoverageEdgeV1(
                charge.SourceChargeId, poolId, allocationId,
                HashHistoricalGrossNetParityPayload(edgeEvidence), edgeEvidence);
        }).ToArray();
        return HistoricalGrossNetParityComponentGraphV1.Create(
            allocationId, RoundHistoricalGrossNetParity8(amountUsd), charges, edges, movement);
    }

    private static HistoricalGrossNetParityComponentAllocationV1
        CreateHistoricalGrossNetParityDirectComponent(
            string allocationId,
            string sourceChargeId,
            decimal amountUsd,
            string evidencePayload)
    {
        var amount = RoundHistoricalGrossNetParity8(amountUsd);
        var sourceEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityDirectSourceChargeV1",
            allocationId,
            sourceChargeId,
            amountUsd = amount,
            evidencePayload
        });
        var poolId = "canonical-direct:" + HashHistoricalGrossNetParityPayload(sourceChargeId);
        var edgeEvidence = JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityDirectCoverageV1",
            sourceChargeId,
            poolId,
            allocationId
        });
        return HistoricalGrossNetParityComponentGraphV1.Create(
            allocationId,
            amount,
            [new HistoricalGrossNetParitySourceChargeV1(
                sourceChargeId, amount,
                HashHistoricalGrossNetParityPayload(sourceEvidence), sourceEvidence)],
            [new HistoricalGrossNetParityChargeCoverageEdgeV1(
                sourceChargeId, poolId, allocationId,
                HashHistoricalGrossNetParityPayload(edgeEvidence), edgeEvidence)]);
    }

    private static bool IsHistoricalGrossNetParityExactFill(
        HistoricalGrossNetParityPaperFillObservation fill) =>
        string.Equals(fill.FeeAccountingStatus, "Calculated", StringComparison.Ordinal) &&
        fill.FeeUsd >= 0m && fill.FeeCalculatedAtUtc is not null &&
        IsHistoricalGrossNetParityExactLocalSource(
            fill.FeeCalculationSource, fill.FeeUsd, fill.FeeLiquidityRole,
            fill.FeeRate, fill.FeeExponent, fill.FeeTakerOnly);

    private static string CreateHistoricalGrossNetParityDonorPoolId(string wallet, string assetId) =>
        "paper-entry-pool:" + HashHistoricalGrossNetParityPayload(JsonSerializer.Serialize(new
        {
            version = "HistoricalGrossNetParityPaperEntryPoolV1",
            Wallet = wallet,
            AssetId = assetId
        }));

    private static decimal RoundHistoricalGrossNetParity8(decimal value) =>
        Math.Round(value, 8, MidpointRounding.AwayFromZero);

    private static async Task<HistoricalGrossNetParityDonorCandidateAggregate>
        LoadHistoricalGrossNetParityDonorAggregateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            HistoricalGrossNetDonorCandidateDescriptorV1 candidate,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        var emptyComponentHash = HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash([]);
        var freshPaperEvidence = await LoadHistoricalGrossNetParityFreshPaperComponentHashesAsync(
            connection,
            transaction,
            candidate.StrategyId,
            commandTimeoutSeconds,
            cancellationToken);
        var freshComponentHashesJson = JsonSerializer.Serialize(
            freshPaperEvidence.ComponentHashes.ToDictionary(
                value => $"{value.Key.SourceKind}:{value.Key.SourceId:D}",
                value => value.Value,
                StringComparer.Ordinal));
        var freshCompositePaperOrderIdsJson = JsonSerializer.Serialize(
            freshPaperEvidence.RemainingPaperOrderIds.ToDictionary(
                value => $"{value.Key.SourceKind}:{value.Key.SourceId:D}",
                value => value.Value.Select(id => id.ToString("D")).ToArray(),
                StringComparer.Ordinal));
        await using var command = new NpgsqlCommand(
            """
WITH candidate_strategy AS MATERIALIZED (
    SELECT strategy.id, strategy.code FROM strategies strategy WHERE strategy.id = @StrategyId
), uses_runs AS MATERIALIZED (
    SELECT EXISTS (
        SELECT 1 FROM strategy_market_paper_runs run WHERE run.strategy_id = @StrategyId
        UNION ALL
        SELECT 1 FROM strategy_paper_skip_rollups rollup WHERE rollup.strategy_id = @StrategyId)
        AS value
), paper_raw AS MATERIALIZED (
    SELECT 'PaperRun'::text AS source_kind,
           run.id AS source_id,
           CASE WHEN run.paper_order_id IS NULL
                THEN 'paper-run:' || lower(run.id::text)
                ELSE 'paper-order:' || lower(run.paper_order_id::text) END AS economic_key,
           100::bigint AS representation_precedence,
           'ClosedRealized'::text AS contribution_kind,
           run.realized_pnl_usd AS gross,
           run.stake_usd AS basis,
           run.fee_usd AS stored_fee,
           run.realized_pnl_usd - run.net_realized_pnl_usd AS effective_fee,
           run.net_realized_pnl_usd AS net,
           run.fee_accounting_status AS status,
           run.fee_calculation_source AS calculation_source,
           run.fee_liquidity_role AS liquidity_role,
           run.fee_rate, run.fee_exponent, run.fee_taker_only,
           run.fee_calculated_at_utc AS calculated_at,
           NULL::text AS venue_evidence_version,
           CASE WHEN run.fee_calculation_source = 'mixed'
                THEN @FreshComponentHashes::jsonb ->>
                     ('PaperRun:' || lower(run.id::text))
                ELSE @EmptyComponentHash::text END AS component_hash,
           run.paper_order_id AS paper_order_id,
           NULL::text AS pool_wallet,
           NULL::text AS pool_asset,
           '[]'::jsonb AS remaining_paper_order_ids
    FROM strategy_market_paper_runs run
    WHERE run.strategy_id = @StrategyId
      AND (SELECT value FROM uses_runs)
      AND run.status = 'Settled'
      AND run.realized_pnl_usd IS NOT NULL

    UNION ALL

    SELECT 'PaperPosition', position.id,
           'paper-position:' || lower(position.id::text), 100, 'OpenMarkToMarket',
           position.unrealized_pnl_usd,
           position.average_price * position.size_shares,
           position.fee_usd,
           position.unrealized_pnl_usd - position.net_unrealized_pnl_usd,
           position.net_unrealized_pnl_usd,
           position.fee_accounting_status, position.fee_calculation_source,
           position.fee_liquidity_role, position.fee_rate, position.fee_exponent,
           position.fee_taker_only, position.fee_calculated_at_utc,
           NULL, CASE WHEN position.fee_calculation_source = 'mixed'
                      THEN @FreshComponentHashes::jsonb ->>
                           ('PaperPosition:' || lower(position.id::text))
                      ELSE @EmptyComponentHash::text END,
           NULL::uuid,
           lower(position.copied_trader_wallet),
           position.asset_id,
           COALESCE(
               @FreshCompositePaperOrderIds::jsonb ->
                   ('PaperPosition:' || lower(position.id::text)),
               '[]'::jsonb)
    FROM paper_positions position
    INNER JOIN candidate_strategy strategy
      ON (strategy.id = @FollowLeaderStrategyId
          AND lower(position.copied_trader_wallet) NOT LIKE 'strategy:%')
          OR (strategy.id <> @FollowLeaderStrategyId
          AND lower(position.copied_trader_wallet) = lower('strategy:' || strategy.code))
    WHERE position.size_shares > 0

    UNION ALL

    SELECT 'PaperSettlement', settlement.id,
           'paper-settlement:' || lower(settlement.id::text), 100, 'ClosedRealized',
           settlement.realized_pnl_usd, settlement.cost_basis_usd,
           settlement.fee_usd,
           settlement.realized_pnl_usd - settlement.net_realized_pnl_usd,
           settlement.net_realized_pnl_usd,
           settlement.fee_accounting_status, settlement.fee_calculation_source,
           settlement.fee_liquidity_role, settlement.fee_rate, settlement.fee_exponent,
           settlement.fee_taker_only, settlement.fee_calculated_at_utc,
           NULL, CASE WHEN settlement.fee_calculation_source = 'mixed'
                      THEN @FreshComponentHashes::jsonb ->>
                           ('PaperSettlement:' || lower(settlement.id::text))
                      ELSE @EmptyComponentHash::text END,
           NULL::uuid,
           lower(settlement.copied_trader_wallet),
           settlement.asset_id,
           COALESCE(
               @FreshCompositePaperOrderIds::jsonb ->
                   ('PaperSettlement:' || lower(settlement.id::text)),
               '[]'::jsonb)
    FROM paper_position_settlements settlement
    INNER JOIN candidate_strategy strategy
      ON (strategy.id = @FollowLeaderStrategyId
          AND lower(settlement.copied_trader_wallet) NOT LIKE 'strategy:%')
          OR (strategy.id <> @FollowLeaderStrategyId
          AND lower(settlement.copied_trader_wallet) = lower('strategy:' || strategy.code))
    WHERE NOT (SELECT value FROM uses_runs)

    UNION ALL

    SELECT 'PaperSellFill', sell_fill.id,
           CASE WHEN order_fill_count.fill_count = 1
                THEN 'paper-order:' || lower(sell_order.id::text)
                ELSE 'paper-fill:' || lower(sell_fill.id::text) END,
           100, 'ClosedRealized', sell_fill.realized_pnl_usd,
           (sell_fill.price * sell_fill.size_shares) - sell_fill.realized_pnl_usd,
           sell_fill.fee_usd,
           sell_fill.realized_pnl_usd - sell_fill.net_realized_pnl_usd,
           sell_fill.net_realized_pnl_usd,
           sell_fill.fee_accounting_status, sell_fill.fee_calculation_source,
           sell_fill.fee_liquidity_role, sell_fill.fee_rate, sell_fill.fee_exponent,
           sell_fill.fee_taker_only, sell_fill.fee_calculated_at_utc,
           NULL, @FreshComponentHashes::jsonb ->>
                 ('PaperSellFill:' || lower(sell_fill.id::text)),
           sell_order.id,
           NULL::text,
           NULL::text,
           '[]'::jsonb
    FROM paper_orders sell_order
    INNER JOIN paper_fills sell_fill ON sell_fill.paper_order_id = sell_order.id
    INNER JOIN LATERAL (
        SELECT count(*)::bigint AS fill_count FROM paper_fills sibling
        WHERE sibling.paper_order_id = sell_order.id
    ) order_fill_count ON true
    WHERE sell_order.strategy_id = @StrategyId
      AND sell_order.side = 'Sell'
      AND NOT (SELECT value FROM uses_runs)
), live_raw AS MATERIALIZED (
    SELECT 'LiveOrder'::text AS source_kind,
           live_order.id AS source_id,
           CASE
               WHEN live_order.paper_order_id IS NOT NULL
               THEN 'paper-order:' || lower(live_order.paper_order_id::text)
               ELSE 'live-order:' || lower(live_order.id::text)
           END AS economic_key,
           CASE WHEN live_order.fee_accounting_status = 'VenueReported' THEN 300 ELSE 200 END::bigint,
           'ClosedRealized'::text AS contribution_kind,
           live_order.realized_pnl_usd AS gross,
           CASE WHEN live_order.filled_notional_usd > 0 THEN live_order.filled_notional_usd
                WHEN live_order.filled_size > 0 THEN live_order.price * live_order.filled_size
                WHEN live_order.cost_basis_usd > 0
                THEN GREATEST(0, live_order.cost_basis_usd - live_order.fee_usd)
                ELSE 0 END AS basis,
           live_order.fee_usd AS stored_fee,
           live_order.realized_pnl_usd - live_order.net_realized_pnl_usd AS effective_fee,
           live_order.net_realized_pnl_usd AS net,
           live_order.fee_accounting_status AS status,
           live_order.fee_calculation_source AS calculation_source,
           live_order.fee_liquidity_role AS liquidity_role,
           live_order.fee_rate, live_order.fee_exponent, live_order.fee_taker_only,
           live_order.fee_calculated_at_utc AS calculated_at,
           venue.evidence_version AS venue_evidence_version,
           @EmptyComponentHash::text AS component_hash,
           live_order.paper_order_id,
           lower(linked_paper_order.copied_trader_wallet),
           linked_paper_order.asset_id,
           '[]'::jsonb
    FROM live_orders live_order
    LEFT JOIN paper_orders linked_paper_order ON linked_paper_order.id = live_order.paper_order_id
    LEFT JOIN LATERAL (
        SELECT audit.evidence_version
        FROM historical_gross_net_parity_audit audit
        WHERE audit.source_kind = 'LiveOrder' AND audit.source_id = live_order.id
          AND audit.calculation_version = @CalculationVersion
          AND audit.operation_kind IN ('AccountingDecision', 'VenueReportedRevision')
          AND audit.new_payload_json ->> 'fee_accounting_status' = 'VenueReported'
        ORDER BY CASE WHEN audit.operation_kind = 'VenueReportedRevision' THEN 1 ELSE 0 END DESC,
                 audit.authority_order_key DESC NULLS LAST, audit.occurred_at_utc DESC
        LIMIT 1
    ) venue ON true
    WHERE @IncludeLive
      AND live_order.strategy_id = @StrategyId
      AND live_order.settled_at_utc IS NOT NULL
      AND live_order.realized_pnl_usd IS NOT NULL
), raw AS MATERIALIZED (
    SELECT * FROM paper_raw
    UNION ALL
    SELECT * FROM live_raw
), exact_pre_dedup AS MATERIALIZED (
    SELECT raw.*,
           CASE WHEN raw.status = 'VenueReported' THEN raw.venue_evidence_version
                ELSE raw.calculation_source END AS evidence_version
    FROM raw
    WHERE raw.basis > 0
      AND raw.stored_fee >= 0
      AND raw.effective_fee >= 0
      AND raw.net = raw.gross - raw.effective_fee
      AND (raw.source_kind = 'PaperSellFill' OR raw.effective_fee = raw.stored_fee)
      AND (
          (raw.status = 'VenueReported'
           AND raw.source_kind = 'LiveOrder'
           AND raw.venue_evidence_version IS NOT NULL)
          OR
          (raw.status = 'Calculated'
           AND raw.calculated_at IS NOT NULL
           AND (
               ((raw.calculation_source = @ExactCurveSource
                 OR raw.calculation_source = @HistoricalPrefix || @ExactCurveSource)
                AND raw.liquidity_role <> 'Unknown'
                AND raw.fee_rate IS NOT NULL
                AND raw.fee_exponent IS NOT NULL
                AND raw.fee_taker_only IS NOT NULL)
               OR
                ((raw.calculation_source = @ExactNoFeeSource
                  OR raw.calculation_source = @HistoricalPrefix || @ExactNoFeeSource)
                 AND raw.effective_fee = 0)))
               OR
               (raw.source_kind <> 'LiveOrder'
                AND raw.calculation_source = 'mixed'
                AND raw.component_hash IS NOT NULL)
          )
      AND (raw.source_kind <> 'PaperSellFill'
           OR (raw.effective_fee >= raw.stored_fee AND raw.component_hash IS NOT NULL))
), exact_linked_live_orders AS MATERIALIZED (
    SELECT DISTINCT candidate.paper_order_id
    FROM exact_pre_dedup candidate
    WHERE candidate.source_kind = 'LiveOrder'
      AND candidate.paper_order_id IS NOT NULL
), exact_after_lineage AS MATERIALIZED (
    SELECT candidate.*
    FROM exact_pre_dedup candidate
    WHERE NOT (
        candidate.source_kind IN ('PaperRun', 'PaperSellFill')
        AND candidate.paper_order_id IS NOT NULL
        AND EXISTS (
            SELECT 1 FROM exact_linked_live_orders linked_live
            WHERE linked_live.paper_order_id = candidate.paper_order_id))
      AND NOT (
        candidate.source_kind IN ('PaperPosition', 'PaperSettlement')
        AND EXISTS (
            SELECT 1
            FROM jsonb_array_elements_text(candidate.remaining_paper_order_ids) active_order(value)
            INNER JOIN exact_linked_live_orders linked_live
              ON lower(linked_live.paper_order_id::text) = active_order.value))
), precedence AS MATERIALIZED (
    SELECT exact_after_lineage.*,
           row_number() OVER (
               PARTITION BY exact_after_lineage.economic_key
               ORDER BY exact_after_lineage.representation_precedence DESC,
                        octet_length(exact_after_lineage.source_kind),
                        exact_after_lineage.source_kind COLLATE "C",
                        lower(exact_after_lineage.source_id::text)) AS representation_rank
    FROM exact_after_lineage
), selected AS MATERIALIZED (
    SELECT * FROM precedence WHERE representation_rank = 1
), counts AS MATERIALIZED (
    SELECT (SELECT count(*) FROM raw)::bigint AS raw_count,
           (SELECT count(*) FROM exact_pre_dedup)::bigint AS exact_count,
           (SELECT count(*) FROM selected)::bigint AS deduplicated_count
)
SELECT selected.source_kind, selected.source_id, selected.economic_key,
       selected.representation_precedence, selected.contribution_kind,
       selected.gross, selected.basis, selected.effective_fee, selected.net,
       selected.status, selected.calculation_source, selected.evidence_version,
       selected.liquidity_role, selected.fee_rate, selected.fee_exponent,
       selected.fee_taker_only, selected.calculated_at, selected.component_hash,
       counts.raw_count, counts.exact_count, counts.deduplicated_count
FROM counts
LEFT JOIN selected ON true
ORDER BY octet_length(selected.economic_key), selected.economic_key COLLATE "C",
         octet_length(selected.source_kind), selected.source_kind COLLATE "C",
         lower(selected.source_id::text);
""",
            connection,
            transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("StrategyId", candidate.StrategyId);
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        command.Parameters.AddWithValue(
            "IncludeLive",
            targetSourceKind == HistoricalGrossNetParitySourceKind.LiveOrder);
        command.Parameters.AddWithValue("CalculationVersion", HistoricalGrossNetParityConstants.CalculationVersion);
        command.Parameters.AddWithValue("ExactCurveSource", HistoricalGrossNetParityExactCurveSource);
        command.Parameters.AddWithValue("ExactNoFeeSource", HistoricalGrossNetParityExactNoFeeSource);
        command.Parameters.AddWithValue("HistoricalPrefix", HistoricalGrossNetParityHistoricalModelPrefix);
        command.Parameters.AddWithValue("EmptyComponentHash", emptyComponentHash);
        command.Parameters.Add("FreshComponentHashes", NpgsqlDbType.Jsonb).Value =
            freshComponentHashesJson;
        command.Parameters.Add("FreshCompositePaperOrderIds", NpgsqlDbType.Jsonb).Value =
            freshCompositePaperOrderIdsJson;

        await EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(
            command,
            cancellationToken);

        long rawCount = 0;
        long exactCount = 0;
        long deduplicatedCount = 0;
        decimal aggregateStake = 0m;
        decimal numerator = 0m;
        decimal denominator = 0m;
        HistoricalGrossNetDonorMembershipHashBuilderV1? membership = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (membership is null)
            {
                rawCount = reader.GetInt64(18);
                exactCount = reader.GetInt64(19);
                deduplicatedCount = reader.GetInt64(20);
                membership = HistoricalGrossNetDonorHashV1.CreateMembershipHashBuilder(
                    checked((uint)deduplicatedCount));
            }

            if (reader.IsDBNull(0))
            {
                continue;
            }

            var sourceKind = Enum.Parse<HistoricalGrossNetParitySourceKind>(reader.GetString(0), false);
            var gross = reader.GetDecimal(5);
            var basis = reader.GetDecimal(6);
            var fee = reader.GetDecimal(7);
            var net = reader.GetDecimal(8);
            aggregateStake += basis;
            numerator += fee;
            denominator += basis;
            membership.Append(new HistoricalGrossNetDonorMembershipRecordV1(
                reader.GetString(2),
                sourceKind,
                HistoricalGrossNetDonorSourceIdV1.FromUuid(reader.GetGuid(1)),
                null,
                new BigInteger(reader.GetInt64(3)),
                Enum.Parse<HistoricalGrossNetParityDonorContributionKind>(reader.GetString(4), false),
                HistoricalGrossNetHashDecimalV1.FromDecimal(gross),
                HistoricalGrossNetHashDecimalV1.FromDecimal(basis),
                HistoricalGrossNetHashDecimalV1.FromDecimal(fee),
                HistoricalGrossNetHashDecimalV1.FromDecimal(net),
                reader.GetString(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : HistoricalGrossNetHashDecimalV1.FromDecimal(reader.GetDecimal(13)),
                reader.IsDBNull(14) ? null : new BigInteger(reader.GetInt32(14)),
                reader.IsDBNull(15) ? null : reader.GetBoolean(15),
                reader.IsDBNull(16) ? null : DateTimeOffsetFromUtc(reader.GetDateTime(16)),
                reader.GetString(17)));
        }

        membership ??= HistoricalGrossNetDonorHashV1.CreateMembershipHashBuilder(0);
        var membershipHash = membership.Complete();
        membership.Dispose();
        return new HistoricalGrossNetParityDonorCandidateAggregate(
            candidate.StrategyId,
            candidate.MatcherOrder,
            candidate.Tier,
            candidate.DistanceComponents,
            rawCount,
            exactCount,
            deduplicatedCount,
            aggregateStake,
            numerator,
            denominator,
            membershipHash);
    }

    private static async Task EnsureHistoricalGrossNetParityDonorPlanIsIndexedAsync(
        NpgsqlCommand donorCommand,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(donorCommand);
        await using var explain = new NpgsqlCommand(
            "EXPLAIN (FORMAT JSON) " + donorCommand.CommandText,
            donorCommand.Connection,
            donorCommand.Transaction)
        {
            CommandTimeout = donorCommand.CommandTimeout
        };
        foreach (NpgsqlParameter parameter in donorCommand.Parameters)
        {
            var clone = new NpgsqlParameter(parameter.ParameterName, parameter.NpgsqlDbType)
            {
                Value = parameter.Value
            };
            if (!string.IsNullOrWhiteSpace(parameter.DataTypeName))
            {
                clone.DataTypeName = parameter.DataTypeName;
            }
            explain.Parameters.Add(clone);
        }

        var rawPlan = Convert.ToString(
            await explain.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(rawPlan))
        {
            throw new HistoricalGrossNetParitySequentialDonorPlanException(
                "The exact donor statement returned no EXPLAIN plan.");
        }

        using var document = JsonDocument.Parse(rawPlan);
        var sequentialRelations = new HashSet<string>(StringComparer.Ordinal);
        CollectHistoricalGrossNetParitySequentialDonorRelations(
            document.RootElement,
            sequentialRelations);
        var oversizedSequentialRelations = new List<string>();
        foreach (var relation in sequentialRelations.OrderBy(value => value, StringComparer.Ordinal))
        {
            var quotedRelation = new NpgsqlCommandBuilder().QuoteIdentifier(relation);
            await using var boundedSizeProof = new NpgsqlCommand(
                $"SELECT EXISTS (SELECT 1 FROM {quotedRelation} OFFSET @MaximumRows LIMIT 1);",
                donorCommand.Connection,
                donorCommand.Transaction)
            {
                CommandTimeout = donorCommand.CommandTimeout
            };
            boundedSizeProof.Parameters.AddWithValue(
                "MaximumRows",
                HistoricalGrossNetParityMaximumPageSize);
            if (await boundedSizeProof.ExecuteScalarAsync(cancellationToken) is true)
            {
                oversizedSequentialRelations.Add(relation);
            }
        }

        if (oversizedSequentialRelations.Count != 0)
        {
            throw new HistoricalGrossNetParitySequentialDonorPlanException(
                "The exact donor statement planned a forbidden sequential full-corpus scan " +
                $"over more than {HistoricalGrossNetParityMaximumPageSize.ToString(CultureInfo.InvariantCulture)} rows: " +
                string.Join(", ", oversizedSequentialRelations));
        }
    }

    private static void CollectHistoricalGrossNetParitySequentialDonorRelations(
        JsonElement element,
        ISet<string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Node Type", out var nodeType) &&
                nodeType.ValueKind == JsonValueKind.String &&
                string.Equals(nodeType.GetString(), "Seq Scan", StringComparison.Ordinal) &&
                element.TryGetProperty("Relation Name", out var relationName) &&
                relationName.ValueKind == JsonValueKind.String &&
                relationName.GetString() is { } relation &&
                HistoricalGrossNetParityDonorRelations.Contains(relation))
            {
                result.Add(relation);
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectHistoricalGrossNetParitySequentialDonorRelations(property.Value, result);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                CollectHistoricalGrossNetParitySequentialDonorRelations(child, result);
            }
        }
    }

    private static readonly HashSet<string> HistoricalGrossNetParityDonorRelations =
        new(StringComparer.Ordinal)
        {
            "strategy_market_paper_runs",
            "strategy_paper_skip_rollups",
            "paper_positions",
            "paper_position_settlements",
            "paper_orders",
            "paper_fills",
            "live_orders",
            "historical_gross_net_parity_audit"
        };

    private sealed class HistoricalGrossNetParitySequentialDonorPlanException(string message)
        : InvalidOperationException(message);

    private static async Task<HistoricalGrossNetDonorSelectionEvaluationV1>
        RecomputeHistoricalGrossNetParitySelectionAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            HistoricalGrossNetParitySourceKind targetSourceKind,
            IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> orderedCandidates,
            int donorPageSize,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken)
    {
        if (orderedCandidates.Count == 0)
        {
            return HistoricalGrossNetDonorSelectionV1.Evaluate(
                [],
                new Dictionary<Guid, HistoricalGrossNetDonorSelectionAggregateV1>());
        }

        var aggregates = new Dictionary<Guid, HistoricalGrossNetDonorSelectionAggregateV1>();
        for (var offset = 0; offset < orderedCandidates.Count; offset += donorPageSize)
        {
            var end = Math.Min(orderedCandidates.Count, checked(offset + donorPageSize));
            for (var index = offset; index < end; index++)
            {
                var aggregate = await LoadHistoricalGrossNetParityDonorAggregateStreamingAsync(
                    connection,
                    transaction,
                    targetSourceKind,
                    orderedCandidates[index],
                    donorPageSize,
                    commandTimeoutSeconds,
                    cancellationToken);
                aggregates.Add(aggregate.CandidateStrategyId, new HistoricalGrossNetDonorSelectionAggregateV1(
                    aggregate.CandidateStrategyId,
                    new BigInteger(aggregate.DeduplicatedDonorCount),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(aggregate.AggregateStakeUsd),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(aggregate.N),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(aggregate.D),
                    aggregate.MembershipHashV1));
            }
        }

        return HistoricalGrossNetDonorSelectionV1.Evaluate(orderedCandidates, aggregates);
    }

    private static bool HistoricalGrossNetParityDonorDecisionMatches(
        HistoricalGrossNetParityDonorDecisionV1 expected,
        HistoricalGrossNetDonorSelectionEvaluationV1 actual)
    {
        if (!string.Equals(expected.SelectionHashV1, actual.SelectionHashV1, StringComparison.Ordinal) ||
            expected.SelectedDonorStrategyId != actual.SelectedStrategyId ||
            expected.SelectedTier != actual.SelectedTier)
        {
            return false;
        }

        if (actual.SelectedStrategyId is null)
        {
            return expected.RawDonorCount == 0 && expected.ExactDonorCount == 0 &&
                   expected.DeduplicatedDonorCount == 0 && expected.N == 0m && expected.D == 0m;
        }

        var selected = actual.InspectedRecords.Single(record =>
            record.CandidateStrategyId == actual.SelectedStrategyId.Value);
        return expected.DeduplicatedDonorCount == checked((long)selected.ExactDonorCount) &&
               expected.ExactDonorCount >= expected.DeduplicatedDonorCount &&
               expected.N == ToHistoricalGrossNetParityDecimal(selected.N) &&
               expected.D == ToHistoricalGrossNetParityDecimal(selected.D) &&
               expected.AggregateStakeUsd == ToHistoricalGrossNetParityDecimal(selected.AggregateStake) &&
               string.Equals(expected.MembershipHashV1, selected.MembershipHash, StringComparison.Ordinal) &&
               expected.Ratio == (expected.D == 0m ? 0m : expected.N / expected.D);
    }

    private static decimal ToHistoricalGrossNetParityDecimal(HistoricalGrossNetHashDecimalV1 value)
    {
        var divisor = BigInteger.Pow(10, value.Scale);
        return (decimal)value.UnscaledValue / (decimal)divisor;
    }

    private static async Task<string?> ReadHistoricalGrossNetParityTargetHashAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        int commandTimeoutSeconds,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var payload = await ReadHistoricalGrossNetParityTargetPayloadAsync(
            connection,
            transaction,
            sourceKind,
            sourceId,
            commandTimeoutSeconds,
            cancellationToken,
            forUpdate);
        return payload is null ? null : HashHistoricalGrossNetParityPayload(payload);
    }

    private static async Task<string?> ReadHistoricalGrossNetParityTargetPayloadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        HistoricalGrossNetParitySourceKind sourceKind,
        Guid sourceId,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var lockClause = forUpdate ? " FOR UPDATE" : string.Empty;
        var paperFillLockClause = forUpdate ? " FOR UPDATE OF fill, paper_order" : string.Empty;
        var sql = sourceKind switch
        {
            HistoricalGrossNetParitySourceKind.PaperRun => $$"""
SELECT jsonb_build_object(
           'run_id', lower(run.id::text), 'strategy_id', lower(run.strategy_id::text),
           'status', run.status, 'condition_id', run.condition_id,
           'asset_id', run.selected_asset_id, 'outcome', run.selected_outcome,
           'entry_price', run.entry_price, 'stake_usd', run.stake_usd,
           'size_shares', run.size_shares, 'paper_order_id', lower(run.paper_order_id::text),
           'entered_at_utc', run.entered_at_utc, 'settlement_price', run.settlement_price,
           'settlement_value_usd', run.settlement_value_usd,
           'realized_pnl_usd', run.realized_pnl_usd, 'fee_usd', run.fee_usd,
           'fee_accounting_status', run.fee_accounting_status,
           'fee_liquidity_role', run.fee_liquidity_role,
           'fee_calculation_source', run.fee_calculation_source,
           'fee_rate', run.fee_rate, 'fee_exponent', run.fee_exponent,
           'fee_taker_only', run.fee_taker_only,
           'fee_calculated_at_utc', run.fee_calculated_at_utc,
           'net_realized_pnl_usd', run.net_realized_pnl_usd,
           'settled_at_utc', run.settled_at_utc,
           'retention_scope', run.retention_scope)::text
FROM strategy_market_paper_runs run WHERE run.id = @Id{{lockClause}};
""",
            HistoricalGrossNetParitySourceKind.PaperPosition => $$"""
SELECT jsonb_build_object(
           'position_id', lower(position.id::text),
           'strategy_id', lower(CASE
               WHEN lower(position.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE @FollowLeaderStrategyId END::text),
           'wallet', position.copied_trader_wallet, 'asset_id', position.asset_id,
           'condition_id', position.condition_id, 'outcome', position.outcome,
           'size_shares', position.size_shares, 'average_price', position.average_price,
           'fee_usd', position.fee_usd, 'fee_accounting_status', position.fee_accounting_status,
           'fee_liquidity_role', position.fee_liquidity_role,
           'fee_calculation_source', position.fee_calculation_source,
           'fee_rate', position.fee_rate, 'fee_exponent', position.fee_exponent,
           'fee_taker_only', position.fee_taker_only,
           'fee_calculated_at_utc', position.fee_calculated_at_utc)::text
FROM paper_positions position
LEFT JOIN strategies strategy_by_wallet
  ON strategy_by_wallet.code = lower(substring(position.copied_trader_wallet from 10))
 AND lower(position.copied_trader_wallet) LIKE 'strategy:%'
WHERE position.id = @Id{{lockClause}};
""",
            HistoricalGrossNetParitySourceKind.PaperSettlement => $$"""
SELECT jsonb_build_object(
           'settlement_id', lower(settlement.id::text),
           'strategy_id', lower(CASE
               WHEN lower(settlement.copied_trader_wallet) LIKE 'strategy:%' THEN strategy_by_wallet.id
               ELSE @FollowLeaderStrategyId END::text),
           'wallet', settlement.copied_trader_wallet, 'asset_id', settlement.asset_id,
           'condition_id', settlement.condition_id, 'outcome', settlement.outcome,
           'settled_size_shares', settlement.settled_size_shares,
           'average_price', settlement.average_price,
           'cost_basis_usd', settlement.cost_basis_usd,
           'settlement_value_usd', settlement.settlement_value_usd,
           'realized_pnl_usd', settlement.realized_pnl_usd,
           'fee_usd', settlement.fee_usd,
           'fee_accounting_status', settlement.fee_accounting_status,
           'fee_liquidity_role', settlement.fee_liquidity_role,
           'fee_calculation_source', settlement.fee_calculation_source,
           'fee_rate', settlement.fee_rate, 'fee_exponent', settlement.fee_exponent,
           'fee_taker_only', settlement.fee_taker_only,
           'fee_calculated_at_utc', settlement.fee_calculated_at_utc,
           'net_realized_pnl_usd', settlement.net_realized_pnl_usd,
           'settled_at_utc', settlement.settled_at_utc)::text
FROM paper_position_settlements settlement
LEFT JOIN strategies strategy_by_wallet
  ON strategy_by_wallet.code = lower(substring(settlement.copied_trader_wallet from 10))
 AND lower(settlement.copied_trader_wallet) LIKE 'strategy:%'
WHERE settlement.id = @Id{{lockClause}};
""",
            HistoricalGrossNetParitySourceKind.PaperSellFill => $$"""
SELECT jsonb_build_object(
           'fill_id', lower(fill.id::text), 'paper_order_id', lower(paper_order.id::text),
           'strategy_id', lower(paper_order.strategy_id::text),
           'wallet', paper_order.copied_trader_wallet, 'status', paper_order.status,
           'side', paper_order.side, 'execution_source', paper_order.execution_source,
           'asset_id', paper_order.asset_id, 'condition_id', paper_order.condition_id,
           'outcome', paper_order.outcome, 'order_price', paper_order.price,
           'order_size_shares', paper_order.size_shares,
           'order_created_at_utc', paper_order.created_at_utc,
           'fill_price', fill.price, 'fill_size_shares', fill.size_shares,
           'filled_at_utc', fill.filled_at_utc, 'realized_pnl_usd', fill.realized_pnl_usd,
           'fee_usd', fill.fee_usd, 'fee_accounting_status', fill.fee_accounting_status,
           'fee_liquidity_role', fill.fee_liquidity_role,
           'fee_calculation_source', fill.fee_calculation_source,
           'fee_rate', fill.fee_rate, 'fee_exponent', fill.fee_exponent,
           'fee_taker_only', fill.fee_taker_only,
           'fee_calculated_at_utc', fill.fee_calculated_at_utc,
           'net_realized_pnl_usd', fill.net_realized_pnl_usd)::text
FROM paper_fills fill
INNER JOIN paper_orders paper_order ON paper_order.id = fill.paper_order_id
WHERE fill.id = @Id{{paperFillLockClause}};
""",
            HistoricalGrossNetParitySourceKind.LiveOrder => $$"""
SELECT to_jsonb(live_order)::text FROM live_orders live_order WHERE live_order.id = @Id{{lockClause}};
""",
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind))
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = commandTimeoutSeconds
        };
        command.Parameters.AddWithValue("Id", sourceId);
        command.Parameters.AddWithValue("FollowLeaderStrategyId", StrategyIds.FollowLeader);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : NormalizeHistoricalGrossNetParityJson(
                Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static bool IsHistoricalGrossNetParityDeferred(PostgresException exception) =>
        exception.SqlState is PostgresErrorCodes.SerializationFailure or
            PostgresErrorCodes.DeadlockDetected or
            PostgresErrorCodes.LockNotAvailable or
            PostgresErrorCodes.QueryCanceled;
}
