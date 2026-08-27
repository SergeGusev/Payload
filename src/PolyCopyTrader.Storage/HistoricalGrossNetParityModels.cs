using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public interface IHistoricalGrossNetParityStore
{
    Task<IReadOnlyList<HistoricalGrossNetParityRankedStrategy>>
        LoadHistoricalGrossNetParityStrategyRankingAsync(
            HistoricalGrossNetParityStrategyRankingRequest request,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<HistoricalGrossNetParityRankedStrategy>>([]);

    Task<HistoricalGrossNetParityCandidatePage> LoadHistoricalGrossNetParityCandidatePageAsync(
        HistoricalGrossNetParityCandidatePageRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoricalGrossNetParityCandidatePage.Unavailable(request));

    Task<HistoricalGrossNetParityDonorPreviewResult> LoadHistoricalGrossNetParityDonorPreviewAsync(
        HistoricalGrossNetParityDonorPreviewRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoricalGrossNetParityDonorPreviewResult.Unavailable());

    Task<HistoricalGrossNetParityApplyResult> TryApplyHistoricalGrossNetParityPaperDecisionAsync(
        HistoricalGrossNetParityPaperDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoricalGrossNetParityApplyResult.Unavailable(request.Target.TargetTupleHash));

    Task<HistoricalGrossNetParityApplyResult> TryApplyHistoricalGrossNetParityLiveAccountingAsync(
        HistoricalGrossNetParityLiveAccountingRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoricalGrossNetParityApplyResult.Unavailable(request.Target.TargetTupleHash));

    Task<HistoricalGrossNetParityLiveBalanceResult> TryApplyHistoricalGrossNetParityEarliestLiveBalanceAsync(
        HistoricalGrossNetParityLiveBalanceRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HistoricalGrossNetParityLiveBalanceResult.Unavailable(request.LiveOrderId));

    Task<HistoricalGrossNetParityVenueRevisionResult> ApplyHistoricalGrossNetParityVenueRevisionAsync(
        HistoricalGrossNetParityVenueRevisionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HistoricalGrossNetParityVenueRevisionResult(
            false,
            false,
            HistoricalGrossNetParityOwnership.None,
            null,
            null,
            null,
            false,
            "Historical Gross/Net parity storage is unavailable."));
}

public static class HistoricalGrossNetParityConstants
{
    public const string BaseContractId = "RC-20260814-historical-gross-net-parity";
    public const string SupplementalContractId =
        "RC-20260814-historical-gross-net-parity-incremental-service";
    public const string SupplementalSemanticDigest =
        "sha256:cdd2a4cde278932099a46c8bf4429b4141cdd43f2e01848e04e036f2cd995b40";
    public const string StrategyCompletionContractId =
        "RC-20260823-historical-parity-gross-ordered-strategy-completion";
    public const string StrategyCompletionSemanticDigest =
        "sha256:ddae8fef59aed07fc98c719396f699370f6d385c377637a9d908a9342134bddf";
    public const string DirectFixedFallbackContractId =
        "RC-20260827-historical-parity-direct-fixed-3p33";
    public const string DirectFixedFallbackSemanticDigest =
        "sha256:36999b41e5a89d88c23ae7f2febf3e605d7bbed18e8838bb3954956ea2e1f0f5";
    public const string CalculationVersion = "historical-gross-net-parity-v1";
    public const string DonorMembershipEncodingDomain = "HGNM1";
    public const string DonorSelectionEncodingDomain = "HGNS1";
    public static readonly DateTimeOffset CutoffUtc =
        new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
}

public enum HistoricalGrossNetParitySourceKind
{
    PaperRun,
    PaperPosition,
    PaperSettlement,
    PaperSellFill,
    LiveOrder,
    PaperFillEvidence,
    PaperSourceSelection,
    PaperOrderFillLineage
}

public enum HistoricalGrossNetParityProcessingPhase
{
    Exact,
    Fallback
}

public enum HistoricalGrossNetParityDecisionKind
{
    ExistingExactPreserved,
    AuthoritativeNetRepair,
    LocalExactCalculated,
    DonorRatio,
    Fixed0p0333,
    Fixed0p033,
    NonpositiveBasis
}

public enum HistoricalGrossNetParityDonorRepresentation
{
    ExactPaper = 100,
    LocalExactLive = 200,
    VenueReportedLive = 300
}

public enum HistoricalGrossNetParityDonorContributionKind
{
    ClosedRealized,
    OpenMarkToMarket
}

public enum HistoricalGrossNetParityOperationKind
{
    AccountingBaseline,
    AccountingDecision,
    InitialBalanceApplication,
    VenueReportedRevision
}

public enum HistoricalGrossNetParityBaselineEffectKind
{
    None,
    LegacyGrossApplied,
    NetAlreadyApplied
}

public enum HistoricalGrossNetParityLookupOutcomeStatus
{
    Success,
    Proved404,
    SemanticUnavailable,
    OperationalFailure,
    ProtocolInvariantConflict,
    HistoricalLookupExhausted
}

public enum HistoricalGrossNetParityLookupFeeApplicationKind
{
    TotalContributionFee,
    AdditionalNonoverlappingComponent
}

public enum HistoricalGrossNetParityExactEligibility
{
    ExistingExactPreserved,
    AuthoritativeNetRepair,
    LocalLookupRequired,
    FallbackRequired,
    InvariantConflict
}

public enum HistoricalGrossNetParityReadStatus
{
    Complete,
    DeferredOperational,
    InvariantConflict
}

public enum HistoricalGrossNetParityApplyStatus
{
    Applied,
    TerminalNoOp,
    DeferredOperational,
    DeferredCas,
    DeferredLineage,
    InvariantConflict,
    NotEarliest
}

public sealed record HistoricalGrossNetParityCandidateCursor(
    int StrategyRank,
    Guid StrategyId,
    int SourceOrder,
    DateTimeOffset OriginatedAtUtc,
    Guid SourceId);

public sealed record HistoricalGrossNetParityStrategyRankingRequest(
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds);

public sealed record HistoricalGrossNetParityRankedStrategy(
    Guid StrategyId,
    string StrategyCode,
    int StrategyRank,
    decimal GrossPnlUsd);

public sealed record HistoricalGrossNetParityCandidatePageRequest(
    HistoricalGrossNetParityProcessingPhase Phase,
    DateTimeOffset CutoffUtc,
    int PageSize,
    HistoricalGrossNetParityCandidateCursor? After,
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds,
    string CalculationVersion,
    HistoricalGrossNetParityRankedStrategy Strategy)
{
    public Guid StrategyId => Strategy.StrategyId;
}

public sealed record HistoricalGrossNetParityCandidateKey(
    HistoricalGrossNetParitySourceKind SourceKind,
    Guid SourceId,
    Guid StrategyId,
    string StrategyCode,
    int StrategyRank,
    decimal StrategyGrossPnlUsd,
    DateTimeOffset OriginatedAtUtc,
    int SourceOrder,
    long RowVersion,
    HistoricalGrossNetParityOwnership Ownership);

public sealed record HistoricalGrossNetParityCandidatePage(
    HistoricalGrossNetParityReadStatus Status,
    IReadOnlyList<HistoricalGrossNetParityCandidateKey> Candidates,
    IReadOnlyList<HistoricalGrossNetParityTargetSnapshot> LiveTargets,
    IReadOnlyList<HistoricalGrossNetParityPaperFillObservation> PaperFillObservations,
    IReadOnlyList<HistoricalGrossNetParityPaperPositionObservation> PaperPositionObservations,
    IReadOnlyList<HistoricalGrossNetParityPaperSettlementObservation> PaperSettlementObservations,
    IReadOnlyList<HistoricalGrossNetParityPaperRunObservation> PaperRunObservations,
    IReadOnlyList<HistoricalGrossNetParityPaperSourceSelection> PaperSourceSelections,
    IReadOnlyList<HistoricalGrossNetParityLookupRequest> LookupRequests,
    IReadOnlyList<HistoricalGrossNetParityTargetConflict> Conflicts,
    HistoricalGrossNetParityCandidateCursor? NextCursor,
    bool ReachedBoundary,
    string Details = "")
{
    public static HistoricalGrossNetParityCandidatePage Unavailable(
        HistoricalGrossNetParityCandidatePageRequest request) =>
        new(
            HistoricalGrossNetParityReadStatus.DeferredOperational,
            [], [], [], [], [], [], [], [], [],
            request.After,
            false,
            "Historical Gross/Net parity storage is unavailable.");
}

public sealed record HistoricalGrossNetParityComponentAllocationV1(
    string AllocationId,
    decimal AmountUsd,
    string AllocationHash,
    string CoverageHash,
    string EvidenceJson,
    IReadOnlyList<HistoricalGrossNetParitySourceChargeV1>? SourceCharges = null,
    IReadOnlyList<HistoricalGrossNetParityChargeCoverageEdgeV1>? CoverageEdges = null,
    HistoricalGrossNetParityPoolMovementV1? PoolMovement = null);

public sealed record HistoricalGrossNetParitySourceChargeV1(
    string SourceChargeId,
    decimal AmountUsd,
    string EvidenceHash,
    string EvidenceJson);

public sealed record HistoricalGrossNetParityChargeCoverageEdgeV1(
    string SourceChargeId,
    string PoolId,
    string AllocationId,
    string EvidenceHash,
    string EvidenceJson);

public sealed record HistoricalGrossNetParityPoolMovementV1(
    string PoolId,
    decimal RawAllocationUsd,
    decimal RemainingBeforeUsd,
    decimal DecrementUsd,
    decimal RemainingAfterUsd,
    decimal ResidualUsd,
    string EvidenceHash,
    string EvidenceJson);

public static class HistoricalGrossNetParityComponentGraphV1
{
    private const string AllocationDomain = "HGNA1";
    private const string CoverageDomain = "HGNC1";
    private const string ComponentDomain = "HGNP1";

    public static HistoricalGrossNetParityComponentAllocationV1 Create(
        string allocationId,
        decimal amountUsd,
        IReadOnlyList<HistoricalGrossNetParitySourceChargeV1> sourceCharges,
        IReadOnlyList<HistoricalGrossNetParityChargeCoverageEdgeV1> coverageEdges,
        HistoricalGrossNetParityPoolMovementV1? poolMovement = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allocationId);
        ArgumentNullException.ThrowIfNull(sourceCharges);
        ArgumentNullException.ThrowIfNull(coverageEdges);

        var orderedCharges = sourceCharges
            .OrderBy(value => value.SourceChargeId, StringComparer.Ordinal)
            .ThenBy(value => value.EvidenceHash, StringComparer.Ordinal)
            .ToArray();
        var orderedEdges = coverageEdges
            .OrderBy(value => value.SourceChargeId, StringComparer.Ordinal)
            .ThenBy(value => value.PoolId, StringComparer.Ordinal)
            .ThenBy(value => value.AllocationId, StringComparer.Ordinal)
            .ThenBy(value => value.EvidenceHash, StringComparer.Ordinal)
            .ToArray();

        ValidateGraph(allocationId, amountUsd, orderedCharges, orderedEdges, poolMovement);
        var allocationHash = ComputeAllocationHash(allocationId, amountUsd);
        var coverageHash = Hash(EncodeCoverage(
            allocationHash,
            orderedCharges,
            orderedEdges,
            poolMovement));
        var evidenceJson = BuildEvidenceJson(
            allocationId,
            amountUsd,
            allocationHash,
            coverageHash,
            orderedCharges,
            orderedEdges,
            poolMovement);
        return new HistoricalGrossNetParityComponentAllocationV1(
            allocationId,
            amountUsd,
            allocationHash,
            coverageHash,
            evidenceJson,
            orderedCharges,
            orderedEdges,
            poolMovement);
    }

    public static string ComputeAllocationHash(string allocationId, decimal amountUsd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(allocationId);
        if (amountUsd < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amountUsd));
        }

        return Hash(EncodeAllocation(allocationId, amountUsd));
    }

    public static void Validate(HistoricalGrossNetParityComponentAllocationV1 component)
    {
        ArgumentNullException.ThrowIfNull(component);
        var canonical = Create(
            component.AllocationId,
            component.AmountUsd,
            component.SourceCharges ?? [],
            component.CoverageEdges ?? [],
            component.PoolMovement);
        if (!StringComparer.Ordinal.Equals(component.AllocationHash, canonical.AllocationHash) ||
            !StringComparer.Ordinal.Equals(component.CoverageHash, canonical.CoverageHash) ||
            !StringComparer.Ordinal.Equals(component.EvidenceJson, canonical.EvidenceJson))
        {
            throw new InvalidOperationException(
                $"Component allocation {component.AllocationId} is not canonical HistoricalGrossNetParityComponentGraphV1 evidence.");
        }
    }

    public static string ComputeComponentHash(
        IReadOnlyList<HistoricalGrossNetParityComponentAllocationV1> components)
    {
        return HistoricalGrossNetComponentEvidenceGraphV1.ComputeHash(
            ToEvidenceRecords(components));
    }

    public static IReadOnlyList<HistoricalGrossNetComponentEvidenceRecordV1> ToEvidenceRecords(
        IReadOnlyList<HistoricalGrossNetParityComponentAllocationV1> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var ordered = components
            .OrderBy(value => value.AllocationId, StringComparer.Ordinal)
            .ThenBy(value => value.AllocationHash, StringComparer.Ordinal)
            .ThenBy(value => value.CoverageHash, StringComparer.Ordinal)
            .ToArray();
        if (ordered.GroupBy(value => value.AllocationId, StringComparer.Ordinal)
            .Any(group => group.Count() != 1))
        {
            throw new InvalidOperationException(
                "Component allocations must have globally unique canonical identities.");
        }
        foreach (var component in ordered)
        {
            Validate(component);
        }
        var records = new List<HistoricalGrossNetComponentEvidenceRecordV1>();
        foreach (var component in ordered)
        {
            records.Add(HistoricalGrossNetComponentEvidenceRecordV1.EffectiveAllocation(
                component.AllocationId,
                HistoricalGrossNetHashDecimalV1.FromDecimal(component.AmountUsd),
                component.AllocationHash));
            records.AddRange((component.SourceCharges ?? []).Select(charge =>
                HistoricalGrossNetComponentEvidenceRecordV1.SourceCharge(
                    charge.SourceChargeId,
                    HistoricalGrossNetHashDecimalV1.FromDecimal(charge.AmountUsd),
                    charge.EvidenceHash)));
            records.AddRange((component.CoverageEdges ?? []).Select(edge =>
                HistoricalGrossNetComponentEvidenceRecordV1.CoverageEdge(
                    edge.AllocationId,
                    edge.SourceChargeId)));
            if (component.PoolMovement is { } movement)
            {
                records.Add(HistoricalGrossNetComponentEvidenceRecordV1.PoolMovement(
                    component.AllocationId,
                    HistoricalGrossNetHashDecimalV1.FromDecimal(movement.RawAllocationUsd),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(movement.RemainingAfterUsd),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(movement.DecrementUsd),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(movement.ResidualUsd)));
            }
        }

        return records;
    }

    private static void ValidateGraph(
        string allocationId,
        decimal amountUsd,
        IReadOnlyList<HistoricalGrossNetParitySourceChargeV1> charges,
        IReadOnlyList<HistoricalGrossNetParityChargeCoverageEdgeV1> edges,
        HistoricalGrossNetParityPoolMovementV1? movement)
    {
        if (amountUsd < 0m || charges.Count == 0 || edges.Count == 0)
        {
            throw new InvalidOperationException(
                "A component allocation requires a nonnegative amount and nonempty source-charge coverage.");
        }

        var chargeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var charge in charges)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(charge.SourceChargeId);
            ArgumentException.ThrowIfNullOrWhiteSpace(charge.EvidenceHash);
            ArgumentNullException.ThrowIfNull(charge.EvidenceJson);
            if (charge.AmountUsd < 0m || !chargeIds.Add(charge.SourceChargeId))
            {
                throw new InvalidOperationException(
                    "Source charges must be nonnegative and have unique canonical identities.");
            }
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        var coveredCharges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.SourceChargeId);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.PoolId);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.AllocationId);
            ArgumentException.ThrowIfNullOrWhiteSpace(edge.EvidenceHash);
            ArgumentNullException.ThrowIfNull(edge.EvidenceJson);
            if (!StringComparer.Ordinal.Equals(edge.AllocationId, allocationId) ||
                !chargeIds.Contains(edge.SourceChargeId) ||
                !edgeIds.Add(edge.SourceChargeId + "\u001f" + edge.PoolId + "\u001f" + edge.AllocationId) ||
                !coveredCharges.Add(edge.SourceChargeId))
            {
                throw new InvalidOperationException(
                    "Coverage edges must identify one proved source charge, pool, and the canonical allocation.");
            }
        }

        if (!coveredCharges.SetEquals(chargeIds))
        {
            throw new InvalidOperationException(
                "Every proved source charge must have exactly one canonical coverage edge.");
        }

        if (movement is null)
        {
            var explained = charges.Aggregate(0m, (sum, charge) => checked(sum + charge.AmountUsd));
            if (explained != amountUsd)
            {
                throw new InvalidOperationException(
                    "A component without a pool movement must be covered exactly by its raw source-charge edges.");
            }

            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(movement.PoolId);
        ArgumentException.ThrowIfNullOrWhiteSpace(movement.EvidenceHash);
        ArgumentNullException.ThrowIfNull(movement.EvidenceJson);
        if (movement.RawAllocationUsd < 0m ||
            movement.RemainingBeforeUsd < 0m ||
            movement.DecrementUsd < 0m ||
            movement.RemainingAfterUsd < 0m ||
            movement.RemainingBeforeUsd - movement.DecrementUsd != movement.RemainingAfterUsd ||
            movement.DecrementUsd + movement.ResidualUsd != amountUsd ||
            edges.Any(edge => !StringComparer.Ordinal.Equals(edge.PoolId, movement.PoolId)))
        {
            throw new InvalidOperationException(
                "Pool movement and source-charge coverage do not reproduce the aggregate effective allocation.");
        }
    }

    private static byte[] EncodeAllocation(string allocationId, decimal amountUsd)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, AllocationDomain);
        WriteString(stream, allocationId);
        WriteDecimal(stream, amountUsd);
        return stream.ToArray();
    }

    private static byte[] EncodeCoverage(
        string allocationHash,
        IReadOnlyList<HistoricalGrossNetParitySourceChargeV1> charges,
        IReadOnlyList<HistoricalGrossNetParityChargeCoverageEdgeV1> edges,
        HistoricalGrossNetParityPoolMovementV1? movement)
    {
        using var stream = new MemoryStream();
        WriteAscii(stream, CoverageDomain);
        WriteString(stream, allocationHash);
        WriteCount(stream, charges.Count);
        foreach (var charge in charges)
        {
            WriteString(stream, charge.SourceChargeId);
            WriteDecimal(stream, charge.AmountUsd);
            WriteString(stream, charge.EvidenceHash);
        }

        WriteCount(stream, edges.Count);
        foreach (var edge in edges)
        {
            WriteString(stream, edge.SourceChargeId);
            WriteString(stream, edge.PoolId);
            WriteString(stream, edge.AllocationId);
            WriteString(stream, edge.EvidenceHash);
        }

        stream.WriteByte(movement is null ? (byte)0 : (byte)1);
        if (movement is not null)
        {
            WriteString(stream, movement.PoolId);
            WriteDecimal(stream, movement.RawAllocationUsd);
            WriteDecimal(stream, movement.RemainingBeforeUsd);
            WriteDecimal(stream, movement.DecrementUsd);
            WriteDecimal(stream, movement.RemainingAfterUsd);
            WriteDecimal(stream, movement.ResidualUsd);
            WriteString(stream, movement.EvidenceHash);
        }

        return stream.ToArray();
    }

    private static string BuildEvidenceJson(
        string allocationId,
        decimal amountUsd,
        string allocationHash,
        string coverageHash,
        IReadOnlyList<HistoricalGrossNetParitySourceChargeV1> charges,
        IReadOnlyList<HistoricalGrossNetParityChargeCoverageEdgeV1> edges,
        HistoricalGrossNetParityPoolMovementV1? movement)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "HistoricalGrossNetParityComponentGraphV1");
            writer.WriteString("allocationId", allocationId);
            writer.WriteString("amountUsd", CanonicalDecimal(amountUsd));
            writer.WriteString("allocationHash", allocationHash);
            writer.WriteString("coverageHash", coverageHash);
            writer.WriteStartArray("sourceCharges");
            foreach (var charge in charges)
            {
                writer.WriteStartObject();
                writer.WriteString("sourceChargeId", charge.SourceChargeId);
                writer.WriteString("amountUsd", CanonicalDecimal(charge.AmountUsd));
                writer.WriteString("evidenceHash", charge.EvidenceHash);
                writer.WriteString("evidenceJson", charge.EvidenceJson);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("coverageEdges");
            foreach (var edge in edges)
            {
                writer.WriteStartObject();
                writer.WriteString("sourceChargeId", edge.SourceChargeId);
                writer.WriteString("poolId", edge.PoolId);
                writer.WriteString("allocationId", edge.AllocationId);
                writer.WriteString("evidenceHash", edge.EvidenceHash);
                writer.WriteString("evidenceJson", edge.EvidenceJson);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (movement is null)
            {
                writer.WriteNull("poolMovement");
            }
            else
            {
                writer.WriteStartObject("poolMovement");
                writer.WriteString("poolId", movement.PoolId);
                writer.WriteString("rawAllocationUsd", CanonicalDecimal(movement.RawAllocationUsd));
                writer.WriteString("remainingBeforeUsd", CanonicalDecimal(movement.RemainingBeforeUsd));
                writer.WriteString("decrementUsd", CanonicalDecimal(movement.DecrementUsd));
                writer.WriteString("remainingAfterUsd", CanonicalDecimal(movement.RemainingAfterUsd));
                writer.WriteString("residualUsd", CanonicalDecimal(movement.ResidualUsd));
                writer.WriteString("evidenceHash", movement.EvidenceHash);
                writer.WriteString("evidenceJson", movement.EvidenceJson);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string CanonicalDecimal(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void WriteAscii(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));

    private static void WriteCount(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
        stream.Write(bytes);
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteCount(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteDecimal(Stream stream, decimal value) =>
        WriteString(stream, CanonicalDecimal(value));
}

public sealed record HistoricalGrossNetParityEvidenceReferenceV1(
    string EvidenceKind,
    string EvidenceVersion,
    string EvidenceHash,
    HistoricalGrossNetParitySourceKind? SourceKind,
    Guid? SourceId);

public sealed record HistoricalGrossNetParityTargetSnapshot(
    HistoricalGrossNetParitySourceKind SourceKind,
    Guid SourceId,
    Guid StrategyId,
    int StrategyRank,
    decimal StrategyGrossPnlUsd,
    long RowVersion,
    DateTimeOffset OriginatedAtUtc,
    DateTimeOffset? SettledAtUtc,
    decimal GrossPnlUsd,
    decimal GrossRoiBasisUsd,
    decimal FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? FeeCalculatedAtUtc,
    decimal? NetPnlUsd,
    bool BalanceEffectApplied,
    HistoricalGrossNetParityOwnership Ownership,
    string TargetTupleHash,
    string LineageHash,
    string ComponentHash,
    HistoricalGrossNetParityExactEligibility ExactEligibility,
    decimal? AuthoritativeEffectiveFeeUsd,
    IReadOnlyList<HistoricalGrossNetParityEvidenceReferenceV1> ExactEvidenceReferences,
    string? ProvedCryptoAssetSymbol,
    HistoricalGrossNetParityEvidenceReferenceV1? CryptoAssetEvidenceReference,
    decimal ProvedComponentFloorUsd,
    IReadOnlyList<HistoricalGrossNetParityComponentAllocationV1> ProvedComponents,
    HistoricalGrossNetParityBaselineEffectKind? BaselineEffectKind,
    decimal? NominalBaselineGrossPnlUsd,
    decimal? NominalBaselineNetPnlUsd,
    string CanonicalPayloadJson,
    string LineagePayloadJson,
    string ComponentPayloadJson,
    string BindingHash,
    string? EconomicExecutionKey = null,
    HistoricalGrossNetParityDonorRepresentation? Representation = null,
    int RepresentationPrecedence = 0,
    string RawIdentity = "",
    IReadOnlyList<HistoricalGrossNetParityEvidenceReferenceV1>? BindingEvidenceReferences = null);

public sealed record HistoricalGrossNetParityLookupRequest(
    string TupleHash,
    HistoricalGrossNetParitySourceKind SourceKind,
    Guid SourceId,
    Guid StrategyId,
    string ConditionId,
    string AssetId,
    string Side,
    string Outcome,
    decimal GrossRoiBasisUsd,
    decimal Quantity,
    decimal Price,
    string LiquidityRole,
    HistoricalGrossNetParityLookupFeeApplicationKind FeeApplicationKind,
    string FeeAllocationId,
    string FeeSourceChargeId,
    string CanonicalPayloadJson);

public sealed record HistoricalGrossNetParityLookupOutcome(
    string TupleHash,
    HistoricalGrossNetParityLookupOutcomeStatus Status,
    decimal? FeeUsd,
    string CalculationSource,
    string FeeLiquidityRole,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    HistoricalGrossNetParityLookupFeeApplicationKind FeeApplicationKind,
    string FeeAllocationId,
    string FeeSourceChargeId,
    DateTimeOffset CapturedAtUtc,
    string EvidenceJson);

public sealed record HistoricalGrossNetParityTargetConflict(
    string Code,
    HistoricalGrossNetParitySourceKind? SourceKind,
    Guid? SourceId,
    Guid? StrategyId,
    string Details);

public sealed record HistoricalGrossNetParityPaperFillObservation(
    Guid FillId,
    long FillRowVersion,
    Guid PaperOrderId,
    long PaperOrderRowVersion,
    Guid StrategyId,
    string CopiedTraderWallet,
    string OrderStatus,
    string OrderSide,
    string ExecutionSource,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal OrderPrice,
    decimal OrderSizeShares,
    DateTimeOffset OrderCreatedAtUtc,
    decimal FillPrice,
    decimal FillSizeShares,
    DateTimeOffset FilledAtUtc,
    decimal RealizedPnlUsd,
    decimal FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? FeeCalculatedAtUtc,
    decimal? NetRealizedPnlUsd,
    string CanonicalEventKey,
    string CanonicalPayloadJson);

public sealed record HistoricalGrossNetParityPaperPositionObservation(
    Guid PositionId,
    long RowVersion,
    Guid StrategyId,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd,
    decimal FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? FeeCalculatedAtUtc,
    decimal? NetUnrealizedPnlUsd,
    DateTimeOffset UpdatedAtUtc,
    string CanonicalPayloadJson);

public sealed record HistoricalGrossNetParityPaperSettlementObservation(
    Guid SettlementId,
    long RowVersion,
    Guid StrategyId,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SettledSizeShares,
    decimal AveragePrice,
    decimal CostBasisUsd,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    decimal FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? FeeCalculatedAtUtc,
    decimal? NetRealizedPnlUsd,
    DateTimeOffset SettledAtUtc,
    string CanonicalPayloadJson);

public sealed record HistoricalGrossNetParityPaperRunObservation(
    Guid RunId,
    long RowVersion,
    Guid StrategyId,
    string Status,
    string ConditionId,
    string? AssetId,
    string? Outcome,
    decimal? EntryPrice,
    decimal StakeUsd,
    decimal? SizeShares,
    Guid? PaperOrderId,
    DateTimeOffset? EnteredAtUtc,
    decimal? SettlementPrice,
    decimal? SettlementValueUsd,
    decimal? RealizedPnlUsd,
    decimal FeeUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? FeeCalculatedAtUtc,
    decimal? NetRealizedPnlUsd,
    DateTimeOffset? SettledAtUtc,
    string RetentionScope,
    string CanonicalPayloadJson);

public sealed record HistoricalGrossNetParityPaperSourceSelection(
    Guid StrategyId,
    bool UsesRuns,
    long RawRunCount,
    long CompactRollupRunCount,
    string EvidenceHash,
    string CanonicalPayloadJson);

public enum HistoricalGrossNetDonorHashValueKind
{
    Null,
    String,
    Integer,
    Decimal,
    Boolean,
    Uuid,
    Enum,
    Timestamp,
    PositiveInfinity
}

public sealed record HistoricalGrossNetHashDecimalV1(BigInteger UnscaledValue, int Scale)
{
    public static HistoricalGrossNetHashDecimalV1 FromDecimal(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = ((BigInteger)(uint)bits[2] << 64) |
                        ((BigInteger)(uint)bits[1] << 32) |
                        (uint)bits[0];
        if ((bits[3] & int.MinValue) != 0)
        {
            magnitude = -magnitude;
        }

        return new HistoricalGrossNetHashDecimalV1(
            magnitude,
            (bits[3] >> 16) & 0x7f);
    }

    public decimal ToDecimal()
    {
        var divisor = BigInteger.Pow(10, Scale);
        return checked((decimal)UnscaledValue / (decimal)divisor);
    }
}

public sealed class HistoricalGrossNetDonorHashValueV1
{
    private HistoricalGrossNetDonorHashValueV1(
        HistoricalGrossNetDonorHashValueKind kind,
        object? value)
    {
        Kind = kind;
        Value = value;
    }

    public HistoricalGrossNetDonorHashValueKind Kind { get; }
    internal object? Value { get; }

    public static HistoricalGrossNetDonorHashValueV1 Null() => new(HistoricalGrossNetDonorHashValueKind.Null, null);
    public static HistoricalGrossNetDonorHashValueV1 String(string value) => new(HistoricalGrossNetDonorHashValueKind.String, value ?? throw new ArgumentNullException(nameof(value)));
    public static HistoricalGrossNetDonorHashValueV1 Integer(BigInteger value) => new(HistoricalGrossNetDonorHashValueKind.Integer, value);
    public static HistoricalGrossNetDonorHashValueV1 Decimal(HistoricalGrossNetHashDecimalV1 value) => new(HistoricalGrossNetDonorHashValueKind.Decimal, value ?? throw new ArgumentNullException(nameof(value)));
    public static HistoricalGrossNetDonorHashValueV1 Decimal(decimal value) => Decimal(HistoricalGrossNetHashDecimalV1.FromDecimal(value));
    public static HistoricalGrossNetDonorHashValueV1 Boolean(bool value) => new(HistoricalGrossNetDonorHashValueKind.Boolean, value);
    public static HistoricalGrossNetDonorHashValueV1 Uuid(Guid value) => new(HistoricalGrossNetDonorHashValueKind.Uuid, value);
    public static HistoricalGrossNetDonorHashValueV1 Enum(string declaredName) => new(HistoricalGrossNetDonorHashValueKind.Enum, declaredName ?? throw new ArgumentNullException(nameof(declaredName)));
    public static HistoricalGrossNetDonorHashValueV1 Timestamp(DateTimeOffset value) => new(HistoricalGrossNetDonorHashValueKind.Timestamp, value);
    public static HistoricalGrossNetDonorHashValueV1 PositiveInfinity() => new(HistoricalGrossNetDonorHashValueKind.PositiveInfinity, null);
}

public sealed record HistoricalGrossNetDonorSourceIdV1(
    HistoricalGrossNetDonorHashValueKind Kind,
    Guid? UuidValue,
    string? StringValue)
{
    public static HistoricalGrossNetDonorSourceIdV1 FromUuid(Guid value) =>
        new(HistoricalGrossNetDonorHashValueKind.Uuid, value, null);

    public static HistoricalGrossNetDonorSourceIdV1 FromString(string value) =>
        new(HistoricalGrossNetDonorHashValueKind.String, null, value ?? throw new ArgumentNullException(nameof(value)));

    internal HistoricalGrossNetDonorHashValueV1 ToHashValue() => Kind switch
    {
        HistoricalGrossNetDonorHashValueKind.Uuid when UuidValue is not null =>
            HistoricalGrossNetDonorHashValueV1.Uuid(UuidValue.Value),
        HistoricalGrossNetDonorHashValueKind.String when StringValue is not null =>
            HistoricalGrossNetDonorHashValueV1.String(StringValue),
        _ => throw new InvalidOperationException("A donor source ID must be exactly UUID or string.")
    };
}

public enum HistoricalGrossNetComponentEvidenceRecordKind
{
    EffectiveAllocation,
    SourceCharge,
    CoverageEdge,
    PoolMovement
}

public sealed record HistoricalGrossNetComponentEvidenceRecordV1(
    HistoricalGrossNetComponentEvidenceRecordKind RecordKind,
    string? AllocationId,
    string? SourceChargeId,
    HistoricalGrossNetHashDecimalV1? Amount,
    string? EvidenceVersion,
    HistoricalGrossNetHashDecimalV1? PoolAllocatedRaw,
    HistoricalGrossNetHashDecimalV1? RemainingPool,
    HistoricalGrossNetHashDecimalV1? PoolDecrement,
    HistoricalGrossNetHashDecimalV1? PoolRoundingResidual)
{
    public static HistoricalGrossNetComponentEvidenceRecordV1 EffectiveAllocation(
        string allocationId,
        HistoricalGrossNetHashDecimalV1 amount,
        string? evidenceVersion = null) =>
        new(HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation,
            allocationId, null, amount, evidenceVersion, null, null, null, null);

    public static HistoricalGrossNetComponentEvidenceRecordV1 SourceCharge(
        string sourceChargeId,
        HistoricalGrossNetHashDecimalV1 amount,
        string? evidenceVersion = null) =>
        new(HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge,
            null, sourceChargeId, amount, evidenceVersion, null, null, null, null);

    public static HistoricalGrossNetComponentEvidenceRecordV1 CoverageEdge(
        string allocationId,
        string sourceChargeId) =>
        new(HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge,
            allocationId, sourceChargeId, null, null, null, null, null, null);

    public static HistoricalGrossNetComponentEvidenceRecordV1 PoolMovement(
        string allocationId,
        HistoricalGrossNetHashDecimalV1 poolAllocatedRaw,
        HistoricalGrossNetHashDecimalV1 remainingPool,
        HistoricalGrossNetHashDecimalV1 poolDecrement,
        HistoricalGrossNetHashDecimalV1 poolRoundingResidual) =>
        new(HistoricalGrossNetComponentEvidenceRecordKind.PoolMovement,
            allocationId, null, null, null, poolAllocatedRaw, remainingPool,
            poolDecrement, poolRoundingResidual);
}

public static class HistoricalGrossNetComponentEvidenceGraphV1
{
    public static string ComputeHash(
        IEnumerable<HistoricalGrossNetComponentEvidenceRecordV1> records) =>
        HistoricalGrossNetDonorHashV1.ComputeComponentAllocationHash(records);

    public static void Validate(
        IReadOnlyList<HistoricalGrossNetComponentEvidenceRecordV1> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var allocations = new Dictionary<string, HistoricalGrossNetComponentEvidenceRecordV1>(
            StringComparer.Ordinal);
        var charges = new Dictionary<string, HistoricalGrossNetComponentEvidenceRecordV1>(
            StringComparer.Ordinal);
        var movements = new Dictionary<string, HistoricalGrossNetComponentEvidenceRecordV1>(
            StringComparer.Ordinal);
        var edges = new HashSet<string>(StringComparer.Ordinal);
        var allocationsWithEdges = new HashSet<string>(StringComparer.Ordinal);
        var chargesWithEdges = new HashSet<string>(StringComparer.Ordinal);
        var chargeEdgeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            ValidateShape(record);
            switch (record.RecordKind)
            {
                case HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation:
                    if (!allocations.TryAdd(record.AllocationId!, record))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate effective allocation identity {record.AllocationId}.");
                    }
                    break;
                case HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge:
                    if (!charges.TryAdd(record.SourceChargeId!, record))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate source-charge identity {record.SourceChargeId}.");
                    }
                    break;
                case HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge:
                    var edgeIdentity = record.AllocationId + "\u001f" + record.SourceChargeId;
                    if (!edges.Add(edgeIdentity))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate coverage edge {record.AllocationId}/{record.SourceChargeId}.");
                    }
                    allocationsWithEdges.Add(record.AllocationId!);
                    chargesWithEdges.Add(record.SourceChargeId!);
                    chargeEdgeCounts[record.SourceChargeId!] =
                        chargeEdgeCounts.GetValueOrDefault(record.SourceChargeId!) + 1;
                    break;
                case HistoricalGrossNetComponentEvidenceRecordKind.PoolMovement:
                    if (!movements.TryAdd(record.AllocationId!, record))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate pool movement for allocation {record.AllocationId}.");
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(records), "Undefined component evidence kind.");
            }
        }

        foreach (var edge in records.Where(value =>
                     value.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge))
        {
            if (!allocations.ContainsKey(edge.AllocationId!) ||
                !charges.ContainsKey(edge.SourceChargeId!))
            {
                throw new InvalidOperationException(
                    $"Coverage edge {edge.AllocationId}/{edge.SourceChargeId} has a missing endpoint.");
            }
        }

        if (allocations.Keys.Any(value => !allocationsWithEdges.Contains(value)) ||
            charges.Keys.Any(value => !chargesWithEdges.Contains(value)) ||
            chargeEdgeCounts.Values.Any(value => value != 1))
        {
            throw new InvalidOperationException(
                "Every effective allocation and source charge must participate exactly once in the proved coverage graph.");
        }

        foreach (var movement in movements.Values)
        {
            if (!allocations.TryGetValue(movement.AllocationId!, out var allocation))
            {
                throw new InvalidOperationException(
                    $"Pool movement {movement.AllocationId} has no effective-allocation endpoint.");
            }

            var expected = Add(movement.PoolDecrement!, movement.PoolRoundingResidual!);
            if (!NumericEquals(allocation.Amount!, expected))
            {
                throw new InvalidOperationException(
                    $"Allocation {movement.AllocationId} does not equal pool decrement plus rounding residual.");
            }
        }

        foreach (var allocation in allocations.Values.Where(value =>
                     !movements.ContainsKey(value.AllocationId!)))
        {
            var linked = records
                .Where(value => value.RecordKind == HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge &&
                                StringComparer.Ordinal.Equals(value.AllocationId, allocation.AllocationId))
                .Select(value => charges[value.SourceChargeId!].Amount!)
                .ToArray();
            var explained = linked.Aggregate(
                new HistoricalGrossNetHashDecimalV1(BigInteger.Zero, 0),
                Add);
            if (!NumericEquals(allocation.Amount!, explained))
            {
                throw new InvalidOperationException(
                    $"Allocation {allocation.AllocationId} has no pool movement and is not explained by its source charges.");
            }
        }
    }

    private static void ValidateShape(HistoricalGrossNetComponentEvidenceRecordV1 record)
    {
        var allocation = !string.IsNullOrWhiteSpace(record.AllocationId);
        var charge = !string.IsNullOrWhiteSpace(record.SourceChargeId);
        var amount = record.Amount is not null;
        var evidence = record.EvidenceVersion is not null;
        var pool = record.PoolAllocatedRaw is not null && record.RemainingPool is not null &&
                   record.PoolDecrement is not null && record.PoolRoundingResidual is not null;
        if (record.EvidenceVersion is { Length: 0 } ||
            record.Amount is { UnscaledValue.Sign: < 0 } ||
            record.PoolAllocatedRaw is { UnscaledValue.Sign: < 0 } ||
            record.RemainingPool is { UnscaledValue.Sign: < 0 } ||
            record.PoolDecrement is { UnscaledValue.Sign: < 0 })
        {
            throw new InvalidOperationException("Component evidence contains an invalid identity, evidence version, or amount.");
        }

        var valid = record.RecordKind switch
        {
            HistoricalGrossNetComponentEvidenceRecordKind.EffectiveAllocation =>
                allocation && !charge && amount && !pool &&
                record.PoolAllocatedRaw is null && record.RemainingPool is null &&
                record.PoolDecrement is null && record.PoolRoundingResidual is null,
            HistoricalGrossNetComponentEvidenceRecordKind.SourceCharge =>
                !allocation && charge && amount && !pool &&
                record.PoolAllocatedRaw is null && record.RemainingPool is null &&
                record.PoolDecrement is null && record.PoolRoundingResidual is null,
            HistoricalGrossNetComponentEvidenceRecordKind.CoverageEdge =>
                allocation && charge && !amount && !evidence &&
                record.PoolAllocatedRaw is null && record.RemainingPool is null &&
                record.PoolDecrement is null && record.PoolRoundingResidual is null,
            HistoricalGrossNetComponentEvidenceRecordKind.PoolMovement =>
                allocation && !charge && !amount && !evidence && pool,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Component evidence record {record.RecordKind} violates its closed nullability shape.");
        }
    }

    private static HistoricalGrossNetHashDecimalV1 Add(
        HistoricalGrossNetHashDecimalV1 left,
        HistoricalGrossNetHashDecimalV1 right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        return new HistoricalGrossNetHashDecimalV1(
            left.UnscaledValue * BigInteger.Pow(10, scale - left.Scale) +
            right.UnscaledValue * BigInteger.Pow(10, scale - right.Scale),
            scale);
    }

    private static bool NumericEquals(
        HistoricalGrossNetHashDecimalV1 left,
        HistoricalGrossNetHashDecimalV1 right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        return left.UnscaledValue * BigInteger.Pow(10, scale - left.Scale) ==
               right.UnscaledValue * BigInteger.Pow(10, scale - right.Scale);
    }
}

public sealed record HistoricalGrossNetDonorMembershipRecordV1(
    string EconomicDedupKey,
    HistoricalGrossNetParitySourceKind SourceKind,
    HistoricalGrossNetDonorSourceIdV1 SourceId,
    string? AllocationId,
    BigInteger RepresentationPrecedence,
    HistoricalGrossNetParityDonorContributionKind ContributionKind,
    HistoricalGrossNetHashDecimalV1 Gross,
    HistoricalGrossNetHashDecimalV1 Basis,
    HistoricalGrossNetHashDecimalV1 Fee,
    HistoricalGrossNetHashDecimalV1 Net,
    string Status,
    string CalculationSource,
    string? EvidenceVersion,
    string LiquidityRole,
    HistoricalGrossNetHashDecimalV1? FeeRate,
    BigInteger? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset? CalculatedAt,
    string ComponentAllocationHash);

public sealed record HistoricalGrossNetDonorDistanceComponentV1(
    string Name,
    HistoricalGrossNetDonorHashValueV1 Value);

public sealed record HistoricalGrossNetDonorCandidateDescriptorV1(
    Guid StrategyId,
    int MatcherOrder,
    BigInteger Tier,
    IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> DistanceComponents,
    string CanonicalMatcherOrderKey);

public sealed record HistoricalGrossNetDonorSelectionRecordV1(
    Guid CandidateStrategyId,
    int MatcherOrder,
    BigInteger Tier,
    IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> DistanceComponents,
    BigInteger ExactDonorCount,
    HistoricalGrossNetHashDecimalV1 AggregateStake,
    HistoricalGrossNetHashDecimalV1 N,
    HistoricalGrossNetHashDecimalV1 D,
    string MembershipHash);

public sealed record HistoricalGrossNetDonorSelectionAggregateV1(
    Guid StrategyId,
    BigInteger ExactDonorCount,
    HistoricalGrossNetHashDecimalV1 AggregateStake,
    HistoricalGrossNetHashDecimalV1 N,
    HistoricalGrossNetHashDecimalV1 D,
    string MembershipHash);

public sealed record HistoricalGrossNetDonorSelectionEvaluationV1(
    IReadOnlyList<HistoricalGrossNetDonorSelectionRecordV1> InspectedRecords,
    Guid? SelectedStrategyId,
    BigInteger? SelectedTier,
    string SelectionHashV1);

public sealed record HistoricalGrossNetParityDonorCandidateAggregate(
    Guid CandidateStrategyId,
    int MatcherOrder,
    BigInteger Tier,
    IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> DistanceComponents,
    long RawDonorCount,
    long ExactDonorCount,
    long DeduplicatedDonorCount,
    decimal AggregateStakeUsd,
    decimal N,
    decimal D,
    string MembershipHashV1);

public sealed record HistoricalGrossNetParityDonorPreviewRequest(
    HistoricalGrossNetParitySourceKind TargetSourceKind,
    Guid TargetSourceId,
    Guid TargetStrategyId,
    string ExpectedTargetTupleHash,
    IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> OrderedCandidates,
    int CandidateOffset,
    int PageSize,
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds);

public sealed record HistoricalGrossNetParityDonorPreviewResult(
    HistoricalGrossNetParityReadStatus Status,
    IReadOnlyList<HistoricalGrossNetParityDonorCandidateAggregate> Aggregates,
    int NextCandidateOffset,
    bool ReachedEnd,
    string Details = "")
{
    public static HistoricalGrossNetParityDonorPreviewResult Unavailable() =>
        new(HistoricalGrossNetParityReadStatus.DeferredOperational, [], 0, false,
            "Historical Gross/Net parity storage is unavailable.");
}

public sealed record HistoricalGrossNetParityDonorDecisionV1(
    Guid? SelectedDonorStrategyId,
    BigInteger? SelectedTier,
    long RawDonorCount,
    long ExactDonorCount,
    long DeduplicatedDonorCount,
    decimal AggregateStakeUsd,
    decimal N,
    decimal D,
    string MembershipHashV1,
    string SelectionHashV1,
    decimal Ratio);

public sealed record HistoricalGrossNetParityAccountingDecisionV1(
    HistoricalGrossNetParityDecisionKind DecisionKind,
    decimal StoredFeeUsd,
    decimal ContributionEffectiveFeeUsd,
    decimal NetPnlUsd,
    string FeeAccountingStatus,
    string FeeLiquidityRole,
    string FeeCalculationSource,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset FeeCalculatedAtUtc,
    decimal? CostBasisUsd,
    decimal ComponentFloorUsd,
    HistoricalGrossNetParityDonorDecisionV1? DonorDecision,
    string EvidenceVersion,
    string EvidenceJson);

public sealed record HistoricalGrossNetParityPaperDecisionRequest(
    HistoricalGrossNetParityTargetSnapshot Target,
    HistoricalGrossNetParityAccountingDecisionV1 Decision,
    IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> OrderedCandidates,
    DateTimeOffset CutoffUtc,
    int DonorPageSize,
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds,
    string CalculationVersion);

public sealed record HistoricalGrossNetParityLiveAccountingRequest(
    HistoricalGrossNetParityTargetSnapshot Target,
    HistoricalGrossNetParityAccountingDecisionV1 Decision,
    IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> OrderedCandidates,
    DateTimeOffset CutoffUtc,
    int DonorPageSize,
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds,
    string CalculationVersion);

public sealed record HistoricalGrossNetParityApplyResult(
    HistoricalGrossNetParityApplyStatus Status,
    bool ReconciliationQueued,
    string CurrentTargetTupleHash,
    HistoricalGrossNetParityOwnership? Ownership,
    string Details = "")
{
    public static HistoricalGrossNetParityApplyResult Unavailable(string targetHash) =>
        new(HistoricalGrossNetParityApplyStatus.DeferredOperational, false, targetHash, null,
            "Historical Gross/Net parity storage is unavailable.");
}

public sealed record HistoricalGrossNetParityLiveBalanceRequest(
    Guid StrategyId,
    Guid LiveOrderId,
    DateTimeOffset CutoffUtc,
    int CommandTimeoutSeconds,
    int LockTimeoutMilliseconds,
    string CalculationVersion);

public sealed record HistoricalGrossNetParityLiveBalanceResult(
    HistoricalGrossNetParityApplyStatus Status,
    Guid LiveOrderId,
    HistoricalGrossNetParityOwnership Ownership,
    decimal? RequestedDelta,
    decimal? ActualAppliedDelta,
    decimal? ResidualUnappliedDelta,
    bool ReconciliationQueued,
    string Details = "")
{
    public static HistoricalGrossNetParityLiveBalanceResult Unavailable(Guid liveOrderId) =>
        new(HistoricalGrossNetParityApplyStatus.DeferredOperational, liveOrderId,
            HistoricalGrossNetParityOwnership.None, null, null, null, false,
            "Historical Gross/Net parity storage is unavailable.");
}

public sealed record HistoricalGrossNetParityVenueRevisionRequest(
    Guid LiveOrderId,
    string AuthorityId,
    string AuthorityOrderKey,
    string EvidenceVersion,
    string SupersedesEvidenceVersion,
    decimal FeeUsd,
    decimal NetRealizedPnlUsd,
    string FeeCalculationSource,
    string FeeLiquidityRole,
    decimal? FeeRate,
    int? FeeExponent,
    bool? FeeTakerOnly,
    DateTimeOffset ReportedAtUtc,
    string EvidenceJson);

public sealed record HistoricalGrossNetParityVenueRevisionResult(
    bool Applied,
    bool Idempotent,
    HistoricalGrossNetParityOwnership Ownership,
    decimal? RequestedDelta,
    decimal? ActualAppliedDelta,
    decimal? ResidualUnappliedDelta,
    bool BalanceDeferred,
    string Details);

public static class HistoricalGrossNetDonorHashV1
{
    public static HistoricalGrossNetComponentEvidenceHashBuilderV1 CreateComponentEvidenceHashBuilder(
        uint recordCount) => new(recordCount);

    public static HistoricalGrossNetDonorMembershipHashBuilderV1 CreateMembershipHashBuilder(
        uint recordCount) => new(recordCount);

    public static string ComputeComponentAllocationHash(
        IEnumerable<HistoricalGrossNetComponentEvidenceRecordV1> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var materialized = records.ToArray();
        HistoricalGrossNetComponentEvidenceGraphV1.Validate(materialized);
        var encoded = materialized.Select(EncodeComponentEvidence).ToArray();
        Array.Sort(encoded, static (left, right) => CompareComponentRecords(left, right));
        return HashPayload(
            HistoricalGrossNetParityConstants.DonorMembershipEncodingDomain,
            encoded.Select(value => value.Record).ToArray());
    }

    public static string ComputeMembershipHash(
        IEnumerable<HistoricalGrossNetDonorMembershipRecordV1> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var encoded = records.Select(record => EncodeMembership(record)).ToArray();
        Array.Sort(encoded, static (left, right) => CompareMembershipRecords(left, right));
        return HashPayload(
            HistoricalGrossNetParityConstants.DonorMembershipEncodingDomain,
            encoded.Select(value => value.Record).ToArray());
    }

    public static string ComputeSelectionHash(
        IEnumerable<HistoricalGrossNetDonorSelectionRecordV1> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var ordered = records
            .OrderBy(record => record.MatcherOrder)
            .ThenBy(
                record => record.CandidateStrategyId.ToString("D").ToLowerInvariant(),
                StringComparer.Ordinal)
            .Select(EncodeSelection)
            .ToArray();
        return HashPayload(HistoricalGrossNetParityConstants.DonorSelectionEncodingDomain, ordered);
    }

    public static string EncodeDistanceComponentsKey(
        IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        using var stream = new MemoryStream();
        WriteUInt32(stream, checked((uint)components.Count));
        foreach (var component in components)
        {
            WriteString(stream, component.Name);
            WriteValue(stream, component.Value);
        }

        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    internal sealed record EncodedComponentEvidence(
        byte[] RecordKind,
        byte[] AllocationId,
        byte[] SourceChargeId,
        byte[] Record);

    internal static EncodedComponentEvidence EncodeComponentEvidence(
        HistoricalGrossNetComponentEvidenceRecordV1 record)
    {
        var recordKindName = Enum.GetName(record.RecordKind) ??
            throw new ArgumentOutOfRangeException(nameof(record), "Undefined component evidence kind.");
        var recordKind = Encode(stream => WriteEnum(stream, recordKindName));
        var allocationId = Encode(stream => WriteNullableString(stream, record.AllocationId));
        var sourceChargeId = Encode(stream => WriteNullableString(stream, record.SourceChargeId));
        using var stream = new MemoryStream();
        stream.Write(recordKind);
        stream.Write(allocationId);
        stream.Write(sourceChargeId);
        WriteNullableDecimal(stream, record.Amount);
        WriteNullableString(stream, record.EvidenceVersion);
        WriteNullableDecimal(stream, record.PoolAllocatedRaw);
        WriteNullableDecimal(stream, record.RemainingPool);
        WriteNullableDecimal(stream, record.PoolDecrement);
        WriteNullableDecimal(stream, record.PoolRoundingResidual);
        return new EncodedComponentEvidence(
            recordKind, allocationId, sourceChargeId, stream.ToArray());
    }

    internal sealed record EncodedMembership(
        byte[] EconomicKey,
        byte[] SourceKind,
        byte[] SourceId,
        byte[] AllocationId,
        byte[] Record);

    internal static EncodedMembership EncodeMembership(HistoricalGrossNetDonorMembershipRecordV1 record)
    {
        var economicKey = Encode(writer => WriteString(writer, record.EconomicDedupKey));
        var sourceKindName = Enum.GetName(record.SourceKind) ??
            throw new ArgumentOutOfRangeException(nameof(record), "Undefined donor source kind.");
        var contributionKindName = Enum.GetName(record.ContributionKind) ??
            throw new ArgumentOutOfRangeException(nameof(record), "Undefined donor contribution kind.");
        if (!IsDeclaredEnumName<FeeAccountingStatus>(record.Status) ||
            !IsDeclaredEnumName<FeeLiquidityRole>(record.LiquidityRole))
        {
            throw new ArgumentException("Donor status and liquidity role must be declared enum names.", nameof(record));
        }

        var sourceKind = Encode(writer => WriteEnum(writer, sourceKindName));
        var sourceId = Encode(writer => WriteValue(writer, record.SourceId.ToHashValue()));
        var allocationId = Encode(writer => WriteNullableString(writer, record.AllocationId));
        using var stream = new MemoryStream();
        stream.Write(economicKey);
        stream.Write(sourceKind);
        stream.Write(sourceId);
        stream.Write(allocationId);
        WriteInteger(stream, record.RepresentationPrecedence);
        WriteEnum(stream, contributionKindName);
        WriteDecimal(stream, record.Gross);
        WriteDecimal(stream, record.Basis);
        WriteDecimal(stream, record.Fee);
        WriteDecimal(stream, record.Net);
        WriteEnum(stream, record.Status);
        WriteString(stream, record.CalculationSource);
        WriteNullableString(stream, record.EvidenceVersion);
        WriteEnum(stream, record.LiquidityRole);
        WriteNullableDecimal(stream, record.FeeRate);
        WriteNullableInteger(stream, record.FeeExponent);
        WriteNullableBoolean(stream, record.FeeTakerOnly);
        WriteNullableTimestamp(stream, record.CalculatedAt);
        WriteString(stream, record.ComponentAllocationHash);
        return new EncodedMembership(economicKey, sourceKind, sourceId, allocationId, stream.ToArray());
    }

    private static byte[] EncodeSelection(HistoricalGrossNetDonorSelectionRecordV1 record)
    {
        using var stream = new MemoryStream();
        WriteUuid(stream, record.CandidateStrategyId);
        WriteInteger(stream, record.Tier);
        WriteUInt32(stream, checked((uint)record.DistanceComponents.Count));
        foreach (var component in record.DistanceComponents)
        {
            WriteString(stream, component.Name);
            WriteValue(stream, component.Value);
        }

        WriteInteger(stream, record.ExactDonorCount);
        WriteDecimal(stream, record.AggregateStake);
        WriteDecimal(stream, record.N);
        WriteDecimal(stream, record.D);
        WriteString(stream, record.MembershipHash);
        return stream.ToArray();
    }

    internal static int CompareComponentRecords(
        EncodedComponentEvidence left,
        EncodedComponentEvidence right)
    {
        var comparison = CompareBytes(left.RecordKind, right.RecordKind);
        if (comparison == 0) comparison = CompareBytes(left.AllocationId, right.AllocationId);
        if (comparison == 0) comparison = CompareBytes(left.SourceChargeId, right.SourceChargeId);
        return comparison == 0 ? CompareBytes(left.Record, right.Record) : comparison;
    }

    internal static int CompareMembershipRecords(EncodedMembership left, EncodedMembership right)
    {
        var comparison = CompareBytes(left.EconomicKey, right.EconomicKey);
        if (comparison == 0) comparison = CompareBytes(left.SourceKind, right.SourceKind);
        if (comparison == 0) comparison = CompareBytes(left.SourceId, right.SourceId);
        if (comparison == 0) comparison = CompareBytes(left.AllocationId, right.AllocationId);
        return comparison == 0 ? CompareBytes(left.Record, right.Record) : comparison;
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var common = Math.Min(left.Length, right.Length);
        for (var index = 0; index < common; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0) return comparison;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static string HashPayload(string domain, IReadOnlyList<byte[]> records)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(domain));
        WriteUInt32(stream, checked((uint)records.Count));
        foreach (var record in records)
        {
            stream.Write(record);
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static byte[] Encode(Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        return stream.ToArray();
    }

    private static void WriteValue(Stream stream, HistoricalGrossNetDonorHashValueV1 value)
    {
        switch (value.Kind)
        {
            case HistoricalGrossNetDonorHashValueKind.Null:
                stream.WriteByte(0x00);
                return;
            case HistoricalGrossNetDonorHashValueKind.String:
                WriteString(stream, (string)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.Integer:
                WriteInteger(stream, (BigInteger)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.Decimal:
                WriteDecimal(stream, (HistoricalGrossNetHashDecimalV1)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.Boolean:
                stream.WriteByte((bool)value.Value! ? (byte)0x05 : (byte)0x04);
                return;
            case HistoricalGrossNetDonorHashValueKind.Uuid:
                WriteUuid(stream, (Guid)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.Enum:
                WriteEnum(stream, (string)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.Timestamp:
                WriteTimestamp(stream, (DateTimeOffset)value.Value!);
                return;
            case HistoricalGrossNetDonorHashValueKind.PositiveInfinity:
                stream.WriteByte(0x09);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        stream.WriteByte(0x01);
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteNullableString(Stream stream, string? value)
    {
        if (value is null) stream.WriteByte(0x00);
        else WriteString(stream, value);
    }

    private static void WriteInteger(Stream stream, BigInteger value)
    {
        stream.WriteByte(0x02);
        var bytes = Encoding.UTF8.GetBytes(value.ToString(CultureInfo.InvariantCulture));
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteNullableInteger(Stream stream, BigInteger? value)
    {
        if (value is null) stream.WriteByte(0x00);
        else WriteInteger(stream, value.Value);
    }

    private static void WriteDecimal(Stream stream, HistoricalGrossNetHashDecimalV1 value)
    {
        stream.WriteByte(0x03);
        WriteInteger(stream, value.UnscaledValue);
        Span<byte> scale = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(scale, value.Scale);
        stream.Write(scale);
    }

    private static void WriteNullableDecimal(Stream stream, HistoricalGrossNetHashDecimalV1? value)
    {
        if (value is null) stream.WriteByte(0x00);
        else WriteDecimal(stream, value);
    }

    private static void WriteNullableBoolean(Stream stream, bool? value)
    {
        stream.WriteByte(value switch { null => (byte)0x00, false => (byte)0x04, true => (byte)0x05 });
    }

    private static void WriteUuid(Stream stream, Guid value)
    {
        stream.WriteByte(0x06);
        stream.Write(Encoding.ASCII.GetBytes(value.ToString("D").ToLowerInvariant()));
    }

    private static void WriteEnum(Stream stream, string value)
    {
        stream.WriteByte(0x07);
        WriteString(stream, value);
    }

    private static void WriteTimestamp(Stream stream, DateTimeOffset value)
    {
        stream.WriteByte(0x08);
        var utcTicks = value.ToUniversalTime().UtcTicks;
        var microsecondTicks = utcTicks - utcTicks % 10L;
        WriteInteger(stream, microsecondTicks);
    }

    private static void WriteNullableTimestamp(Stream stream, DateTimeOffset? value)
    {
        if (value is null) stream.WriteByte(0x00);
        else WriteTimestamp(stream, value.Value);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static bool IsDeclaredEnumName<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) &&
        Enum.IsDefined(parsed) &&
        string.Equals(Enum.GetName(parsed), value, StringComparison.Ordinal);
}

public static class HistoricalGrossNetDonorSelectionV1
{
    public static HistoricalGrossNetDonorSelectionEvaluationV1 Evaluate(
        IReadOnlyList<HistoricalGrossNetDonorCandidateDescriptorV1> orderedCandidates,
        IReadOnlyDictionary<Guid, HistoricalGrossNetDonorSelectionAggregateV1> aggregates)
    {
        ArgumentNullException.ThrowIfNull(orderedCandidates);
        ArgumentNullException.ThrowIfNull(aggregates);
        if (orderedCandidates.Select(value => value.StrategyId).Distinct().Count() != orderedCandidates.Count ||
            aggregates.Keys.Any(id => orderedCandidates.All(candidate => candidate.StrategyId != id)))
        {
            throw new InvalidOperationException("The donor candidate/aggregate set is partial, duplicated, or foreign.");
        }

        var emptyMembership = HistoricalGrossNetDonorHashV1.ComputeMembershipHash([]);
        var inspected = new List<HistoricalGrossNetDonorSelectionRecordV1>();
        Guid? selectedStrategy = null;
        BigInteger? selectedTier = null;
        foreach (var tierGroup in orderedCandidates
                     .GroupBy(candidate => candidate.Tier)
                     .OrderBy(group => group.Min(candidate => candidate.MatcherOrder)))
        {
            var populated = tierGroup.Select(candidate =>
            {
                aggregates.TryGetValue(candidate.StrategyId, out var aggregate);
                aggregate ??= new HistoricalGrossNetDonorSelectionAggregateV1(
                    candidate.StrategyId,
                    BigInteger.Zero,
                    HistoricalGrossNetHashDecimalV1.FromDecimal(0m),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(0m),
                    HistoricalGrossNetHashDecimalV1.FromDecimal(0m),
                    emptyMembership);
                return new PopulatedCandidate(candidate, aggregate, Populate(candidate, aggregate));
            }).ToArray();
            Array.Sort(populated, ComparePopulated);
            foreach (var item in populated)
            {
                inspected.Add(new HistoricalGrossNetDonorSelectionRecordV1(
                    item.Candidate.StrategyId,
                    inspected.Count,
                    item.Candidate.Tier,
                    item.Components,
                    item.Aggregate.ExactDonorCount,
                    item.Aggregate.AggregateStake,
                    item.Aggregate.N,
                    item.Aggregate.D,
                    item.Aggregate.MembershipHash));
            }

            var winner = populated.FirstOrDefault(item =>
                item.Aggregate.ExactDonorCount > 0 && CompareDecimalToZero(item.Aggregate.D) > 0);
            if (winner is not null)
            {
                selectedStrategy = winner.Candidate.StrategyId;
                selectedTier = winner.Candidate.Tier;
                break;
            }
        }

        return new HistoricalGrossNetDonorSelectionEvaluationV1(
            inspected,
            selectedStrategy,
            selectedTier,
            HistoricalGrossNetDonorHashV1.ComputeSelectionHash(inspected));
    }

    private sealed record PopulatedCandidate(
        HistoricalGrossNetDonorCandidateDescriptorV1 Candidate,
        HistoricalGrossNetDonorSelectionAggregateV1 Aggregate,
        IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> Components);

    private static IReadOnlyList<HistoricalGrossNetDonorDistanceComponentV1> Populate(
        HistoricalGrossNetDonorCandidateDescriptorV1 candidate,
        HistoricalGrossNetDonorSelectionAggregateV1 aggregate) =>
        candidate.DistanceComponents.Select(component => component.Name switch
        {
            "negativeAggregateExactDonorStake" => new HistoricalGrossNetDonorDistanceComponentV1(
                component.Name,
                HistoricalGrossNetDonorHashValueV1.Decimal(new HistoricalGrossNetHashDecimalV1(
                    -aggregate.AggregateStake.UnscaledValue,
                    aggregate.AggregateStake.Scale))),
            "negativeExactDonorCount" => new HistoricalGrossNetDonorDistanceComponentV1(
                component.Name,
                HistoricalGrossNetDonorHashValueV1.Integer(-aggregate.ExactDonorCount)),
            _ => component
        }).ToArray();

    private static int ComparePopulated(PopulatedCandidate left, PopulatedCandidate right)
    {
        var common = Math.Min(left.Components.Count, right.Components.Count);
        for (var index = 0; index < common; index++)
        {
            if (!string.Equals(left.Components[index].Name, right.Components[index].Name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Candidate distance component schemas differ.");
            }

            var comparison = CompareValue(left.Components[index].Value, right.Components[index].Value);
            if (comparison != 0) return comparison;
        }

        var lengthComparison = left.Components.Count.CompareTo(right.Components.Count);
        return lengthComparison != 0
            ? lengthComparison
            : string.CompareOrdinal(
                left.Candidate.StrategyId.ToString("D").ToLowerInvariant(),
                right.Candidate.StrategyId.ToString("D").ToLowerInvariant());
    }

    private static int CompareValue(
        HistoricalGrossNetDonorHashValueV1 left,
        HistoricalGrossNetDonorHashValueV1 right)
    {
        if (left.Kind != right.Kind)
        {
            if (left.Kind == HistoricalGrossNetDonorHashValueKind.Null) return -1;
            if (right.Kind == HistoricalGrossNetDonorHashValueKind.Null) return 1;
            if (left.Kind == HistoricalGrossNetDonorHashValueKind.PositiveInfinity) return 1;
            if (right.Kind == HistoricalGrossNetDonorHashValueKind.PositiveInfinity) return -1;
            throw new InvalidOperationException("Candidate distance component domains differ.");
        }

        return left.Kind switch
        {
            HistoricalGrossNetDonorHashValueKind.Null or
            HistoricalGrossNetDonorHashValueKind.PositiveInfinity => 0,
            HistoricalGrossNetDonorHashValueKind.String or
            HistoricalGrossNetDonorHashValueKind.Enum =>
                string.CompareOrdinal((string)left.Value!, (string)right.Value!),
            HistoricalGrossNetDonorHashValueKind.Integer =>
                ((BigInteger)left.Value!).CompareTo((BigInteger)right.Value!),
            HistoricalGrossNetDonorHashValueKind.Decimal =>
                CompareDecimal(
                    (HistoricalGrossNetHashDecimalV1)left.Value!,
                    (HistoricalGrossNetHashDecimalV1)right.Value!),
            HistoricalGrossNetDonorHashValueKind.Boolean =>
                ((bool)left.Value!).CompareTo((bool)right.Value!),
            HistoricalGrossNetDonorHashValueKind.Uuid => string.CompareOrdinal(
                ((Guid)left.Value!).ToString("D").ToLowerInvariant(),
                ((Guid)right.Value!).ToString("D").ToLowerInvariant()),
            HistoricalGrossNetDonorHashValueKind.Timestamp =>
                ((DateTimeOffset)left.Value!).ToUniversalTime().CompareTo(
                    ((DateTimeOffset)right.Value!).ToUniversalTime()),
            _ => throw new ArgumentOutOfRangeException(nameof(left))
        };
    }

    private static int CompareDecimal(
        HistoricalGrossNetHashDecimalV1 left,
        HistoricalGrossNetHashDecimalV1 right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        var leftValue = left.UnscaledValue * BigInteger.Pow(10, scale - left.Scale);
        var rightValue = right.UnscaledValue * BigInteger.Pow(10, scale - right.Scale);
        return leftValue.CompareTo(rightValue);
    }

    private static int CompareDecimalToZero(HistoricalGrossNetHashDecimalV1 value) =>
        value.UnscaledValue.CompareTo(BigInteger.Zero);
}

public static class HistoricalGrossNetParityBindingV1
{
    public static string Compute(string targetTupleHash, string lineageHash, string componentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTupleHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineageHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentHash);
        var payload = Encoding.ASCII.GetBytes(
            "HGNB1" + targetTupleHash + lineageHash + componentHash);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}

public sealed class HistoricalGrossNetComponentEvidenceHashBuilderV1 : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly uint expectedCount;
    private uint appendedCount;
    private HistoricalGrossNetDonorHashV1.EncodedComponentEvidence? previous;
    private bool completed;

    internal HistoricalGrossNetComponentEvidenceHashBuilderV1(uint recordCount)
    {
        expectedCount = recordCount;
        hash.AppendData(Encoding.ASCII.GetBytes(HistoricalGrossNetParityConstants.DonorMembershipEncodingDomain));
        Span<byte> count = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(count, recordCount);
        hash.AppendData(count);
    }

    public void Append(HistoricalGrossNetComponentEvidenceRecordV1 record)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var encoded = HistoricalGrossNetDonorHashV1.EncodeComponentEvidence(record);
        if (previous is not null &&
            HistoricalGrossNetDonorHashV1.CompareComponentRecords(previous, encoded) > 0)
        {
            throw new InvalidOperationException("Component evidence records are not in canonical HGNM1 order.");
        }

        if (appendedCount == expectedCount)
        {
            throw new InvalidOperationException("More component evidence records were appended than declared.");
        }

        hash.AppendData(encoded.Record);
        previous = encoded;
        appendedCount++;
    }

    public string Complete()
    {
        ObjectDisposedException.ThrowIf(completed, this);
        if (appendedCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} component evidence records but appended {appendedCount}.");
        }

        completed = true;
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!completed)
        {
            completed = true;
        }

        hash.Dispose();
    }
}

public sealed class HistoricalGrossNetDonorMembershipHashBuilderV1 : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly uint expectedCount;
    private uint appendedCount;
    private HistoricalGrossNetDonorHashV1.EncodedMembership? previous;
    private bool completed;

    internal HistoricalGrossNetDonorMembershipHashBuilderV1(uint recordCount)
    {
        expectedCount = recordCount;
        hash.AppendData(Encoding.ASCII.GetBytes(HistoricalGrossNetParityConstants.DonorMembershipEncodingDomain));
        Span<byte> count = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(count, recordCount);
        hash.AppendData(count);
    }

    public void Append(HistoricalGrossNetDonorMembershipRecordV1 record)
    {
        ObjectDisposedException.ThrowIf(completed, this);
        var encoded = HistoricalGrossNetDonorHashV1.EncodeMembership(record);
        if (previous is not null &&
            HistoricalGrossNetDonorHashV1.CompareMembershipRecords(previous, encoded) > 0)
        {
            throw new InvalidOperationException("Membership records are not in canonical HGNM1 order.");
        }

        if (appendedCount == expectedCount)
        {
            throw new InvalidOperationException("More membership records were appended than declared.");
        }

        hash.AppendData(encoded.Record);
        previous = encoded;
        appendedCount++;
    }

    public string Complete()
    {
        ObjectDisposedException.ThrowIf(completed, this);
        if (appendedCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} membership records but appended {appendedCount}.");
        }

        completed = true;
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!completed)
        {
            completed = true;
        }

        hash.Dispose();
    }
}
