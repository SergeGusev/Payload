using System.Globalization;

namespace ReferenceAverageHistoryCorrectionApply;

internal enum OperationMode
{
    Preflight,
    Prepare,
    Apply,
    MaintenanceRebuild,
    PostChildGate,
    FinalizeApply,
    FinalizeRollback,
    Rollback,
    RollbackReconciled
}

internal sealed record ToolOptions(
    OperationMode Mode,
    string Host,
    int Port,
    string Database,
    DateTimeOffset CutoffUtc,
    string GraphDirectory,
    string GraphManifestSha256,
    string? StagingDirectory,
    string? DurableBackupDirectory,
    string? FullBackupDirectory,
    string? FullBackupHashManifestPath,
    string? FullBackupMetadataManifestPath,
    string? FullBackupRestoreEvidencePath,
    string? FullBackupRestoredRowCountManifestPath,
    string? FullBackupSchemaManifestPath,
    string? PreparedPackageSha256,
    string? RollbackManifestPath,
    string? OperatorAttestationPath,
    string? OperatorAttestationSha256,
    string? ChildRefreshAttestationPath,
    string? ChildRefreshAttestationSha256,
    int HeartbeatStaleMinutes);

internal sealed record OperatorAttestation(
    int SchemaVersion,
    string Host,
    int Port,
    string Database,
    string DataDirectory,
    string DataVolume,
    long FreeBytes,
    string ServiceName,
    string ServiceState,
    string ServiceStartMode,
    string CollectionMethod,
    string Observer,
    DateTimeOffset ObservedAtUtc);

internal sealed record ChildRefreshAttestation(
    int SchemaVersion,
    string Host,
    int Port,
    string Database,
    string ServiceName,
    string ServiceLogFileName,
    string ServiceLogSha256,
    string CompletionLogLine,
    DateTimeOffset RefreshCompletedAtUtc,
    int Children,
    int ActiveParents,
    string CollectionMethod,
    string Observer,
    DateTimeOffset ObservedAtUtc);

internal sealed record GraphFileEvidence(string FileName, long RowCount, string Sha256);

internal sealed record GraphManifest(
    int SchemaVersion,
    string Tool,
    DateTimeOffset CutoffUtc,
    string ManifestSha256,
    string Directory,
    IReadOnlyDictionary<string, GraphFileEvidence> Files,
    int InvariantErrors,
    int LiveShadowBlockers,
    int DependencyBlockers,
    int PositionBlockers,
    int ReconciliationBlockers,
    int InfeasibleAdds,
    bool SafeToPrepareMutation,
    int ReconciliationSchemaVersion,
    string ReconciliationAlgorithm,
    string ReconciliationSerialization,
    string ReconciliationContractSha256,
    int ReconciliationTargetCount,
    int ReconciliationBlockingTargetCount,
    string ReconciliationApplyHandshake);

internal sealed record ReconciliationTargetContract(
    string TargetId,
    string TableName,
    string KeyScope,
    string MethodId,
    string RequiredAction,
    string Reason,
    bool BlocksMutation,
    string TargetContractSha256);

internal sealed record MainRemoval(
    Guid RunId,
    Guid StrategyId,
    string StrategyCode,
    string MarketId,
    Guid OrderId,
    Guid SignalId,
    string AssetId,
    string Outcome,
    string CopiedTraderWallet,
    DateTimeOffset CorrectedSkippedUpdatedAtUtc,
    decimal RestoredBaseStakeUsd,
    decimal HistoricalEffectiveStakeUsd,
    decimal HistoricalTargetNotionalUsd,
    string HistoricalStakeSizingSource,
    string StakeSizingProofSha256,
    string ClassifierReason,
    string ClassifierAction,
    string SignalPreviewManifestSha256,
    string ReplayClassifierSha256,
    string ReplayEvidenceJson,
    string ReplayEvidenceSha256,
    string GraphStateSha256,
    string FillSetSha256);

internal sealed record ChildRemoval(
    Guid ParentRunId,
    Guid ChildRunId,
    Guid ChildStrategyId,
    string ChildStrategyCode,
    string MarketId,
    Guid ChildOrderId,
    Guid ChildSignalId,
    string GraphStateSha256,
    string FillSetSha256);

internal sealed record AddCandidate(
    Guid RunId,
    Guid StrategyId,
    string StrategyCode,
    string MarketId,
    string ConditionId,
    string Asset,
    string Kind,
    string AddSourceStateSha256,
    string AddSourceRunFullRowSha256,
    DateTimeOffset ModeledEntryAtUtc,
    DateTimeOffset ModeledSettledAtUtc,
    string ModeledSettlementTimestampSource,
    string SettlementCategory,
    string ModeledRawDecisionJson,
    string ModeledRawDecisionSha256,
    string ModeledFillEvidence,
    string ModeledMutationPayloadJson,
    string ModeledMutationPayloadSha256,
    decimal AssumedFillPrice,
    decimal HistoricalStakeMultiplier,
    decimal GammaOrderMinSize,
    decimal RawWorstPriceNotionalUsd,
    decimal RoundedWorstPriceNotionalUsd,
    string SelectedOutcome,
    string SelectedTokenId,
    string ResolvedWinningOutcome,
    string ResolvedWinningTokenId,
    string ResolutionLedgerSource,
    string ResolutionLedgerProvenanceGroup,
    string ResolutionLedgerRawEventType,
    string ResolutionLedgerRawSha256,
    long ResolutionLedgerRawBytes,
    DateTimeOffset ResolutionLedgerFirstReceivedAtUtc,
    DateTimeOffset ResolutionLedgerLastReceivedAtUtc,
    bool ResolutionLedgerRawValidated,
    string ArchivedTickSource,
    string ArchivedTickProvenanceGroup,
    int ArchivedTickSampleCount,
    bool ArchivedTickAgreesWithAuthoritativeWinner,
    string GammaResolutionSource,
    string GammaResolutionProvenanceGroup,
    string GammaRequestUrl,
    string GammaRawSha256,
    long GammaRawBytes,
    DateTimeOffset GammaFetchedAtUtc,
    int AgreeingIndependentResolutionSourceCount,
    decimal WorstPriceTargetSizeShares,
    decimal RequestedNotionalUsd,
    decimal FilledSizeShares,
    bool Won,
    decimal SettlementPrice,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    bool CanAdd,
    string Reason);

internal sealed record PositionKeyTarget(
    string CopiedTraderWallet,
    string AssetId,
    int GraphOrderCount,
    int DatabaseOrderCount,
    int OutsideGraphOrderCount,
    int PositionCount,
    int SettlementCount,
    bool Exclusive,
    bool BlocksMutation);

internal sealed record ForeignKeyContract(
    string ConstraintName,
    string SourceTable,
    string SourceColumns,
    string TargetTable,
    string TargetColumns,
    string DeleteAction,
    string UpdateAction,
    bool Expected);

internal sealed record SchemaReferenceColumnContract(
    string TableName,
    string ColumnName,
    string DataType,
    bool Expected);

internal sealed record GraphPhysicalRowHashes(
    string Scope,
    Guid RunId,
    Guid OrderId,
    Guid SignalId,
    string RunFullRowSha256,
    string OrderFullRowSha256,
    string SignalFullRowSha256);

internal sealed record FillPhysicalRowHash(Guid FillId, Guid OrderId, string FullRowSha256);

internal sealed record PositionPhysicalRowHash(
    Guid Id,
    string CopiedTraderWallet,
    string AssetId,
    string FullRowSha256);

internal sealed record PositionSettlementPhysicalRowHash(
    Guid Id,
    string CopiedTraderWallet,
    string AssetId,
    string FullRowSha256);

internal sealed record OperationFootprintContract(
    string Scope,
    string TableName,
    string Operation,
    string Selector,
    long SelectorIdentityCount,
    long? SnapshotRowCount,
    long? SnapshotPgColumnSizeBytes,
    long PlannedDirectRowOperations,
    bool ExactSnapshotMeasurement,
    string Evidence);

internal sealed record GraphPackage(
    GraphManifest Manifest,
    IReadOnlyList<MainRemoval> MainRemovals,
    IReadOnlyList<ChildRemoval> ChildRemovals,
    IReadOnlyList<AddCandidate> Adds,
    IReadOnlyList<PositionKeyTarget> PositionKeys,
    IReadOnlyList<ForeignKeyContract> ForeignKeys,
    IReadOnlyList<SchemaReferenceColumnContract> SchemaReferenceColumns,
    IReadOnlyList<GraphPhysicalRowHashes> GraphRowHashes,
    IReadOnlyList<FillPhysicalRowHash> FillRowHashes,
    IReadOnlyList<PositionPhysicalRowHash> PositionRowHashes,
    IReadOnlyList<PositionSettlementPhysicalRowHash> PositionSettlementRowHashes,
    IReadOnlyList<OperationFootprintContract> OperationFootprint,
    IReadOnlyList<string> BlockingErrors)
{
    public IReadOnlySet<Guid> RunIds => MainRemovals.Select(row => row.RunId)
        .Concat(ChildRemovals.Select(row => row.ChildRunId))
        .Concat(Adds.Select(row => row.RunId))
        .ToHashSet();

    public IReadOnlySet<Guid> RemovalOrderIds => MainRemovals.Select(row => row.OrderId)
        .Concat(ChildRemovals.Select(row => row.ChildOrderId))
        .ToHashSet();

    public IReadOnlySet<Guid> RemovalSignalIds => MainRemovals.Select(row => row.SignalId)
        .Concat(ChildRemovals.Select(row => row.ChildSignalId))
        .ToHashSet();

    public IReadOnlySet<Guid> StrategyIds => MainRemovals.Select(row => row.StrategyId)
        .Concat(ChildRemovals.Select(row => row.ChildStrategyId))
        .Concat(Adds.Select(row => row.StrategyId))
        .ToHashSet();

    public IReadOnlySet<string> Wallets => PositionKeys.Select(row => row.CopiedTraderWallet)
        .Concat(Adds.Select(row => $"strategy:{row.StrategyCode}"))
        .ToHashSet(StringComparer.Ordinal);
}

internal sealed record EntityIds(Guid SignalId, Guid OrderId, Guid FillId, Guid PositionId, Guid SettlementId);

internal sealed record DatabasePreflight(
    string ServerAddress,
    int ServerPort,
    string Database,
    string SearchPath,
    string TimeZone,
    string Isolation,
    bool ReadOnly,
    DateTimeOffset? ServiceLastHeartbeatUtc,
    string? ServiceStatus,
    int OtherPolyCopyTraderSessions,
    int OtherActiveSessions,
    int LiveShadowOverlapRows,
    int UnsupportedDependencyRows,
    long DailyReportRows,
    string DataDirectory,
    IReadOnlyList<TableFootprint> ScopedFootprint,
    long GlobalProjectionRelationBytes,
    long EstimatedWalPolicyBytes,
    long RequiredFreeSpacePolicyBytes,
    long? AttestedFreeBytes,
    IReadOnlyList<string> BlockingErrors);

internal sealed record TableFootprint(string TableName, long RowCount, long RowBytes);

internal sealed record SnapshotFile(string TableName, string FileName, long RowCount, string Sha256);

internal sealed record CorrectionBackupManifest(
    int SchemaVersion,
    string Tool,
    string Operation,
    string State,
    string GraphManifestSha256,
    DateTimeOffset CutoffUtc,
    string Host,
    int Port,
    string Database,
    string SearchPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset MutationTimestampUtc,
    string TransactionId,
    IReadOnlyList<SnapshotFile> Preimage,
    IReadOnlyList<SnapshotFile> Postimage,
    IReadOnlyDictionary<string, string> DeterministicIds,
    IReadOnlyList<Guid> RunIds,
    IReadOnlyList<Guid> StrategyIds,
    IReadOnlyList<string> Wallets,
    string FullBackupHashManifestSha256,
    string FullBackupMetadataManifestSha256,
    string FullBackupRestoreEvidenceSha256,
    string FullBackupRestoredRowCountManifestSha256,
    string FullBackupSchemaManifestSha256,
    string FullBackupSchemaFingerprintSha256);

internal sealed record PreparedBackupFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

internal sealed record PreparedPackageManifest(
    int SchemaVersion,
    string Tool,
    string State,
    string GraphManifestSha256,
    DateTimeOffset CutoffUtc,
    string Host,
    int Port,
    string Database,
    string SearchPath,
    DateTimeOffset PreparedAtUtc,
    IReadOnlyList<PreparedBackupFile> FullBackupFiles,
    string FullBackupHashManifestSha256,
    string FullBackupMetadataManifestSha256,
    string FullBackupRestoreEvidenceSha256,
    string FullBackupRestoredRowCountManifestSha256,
    string FullBackupSchemaManifestSha256,
    string FullBackupSchemaFingerprintSha256);

internal sealed record ApplyCommitMarker(
    int SchemaVersion,
    string Tool,
    string GraphManifestSha256,
    string TransactionId,
    string CommitReadyManifestSha256,
    DateTimeOffset CommittedAtUtc);

internal sealed class ApplyCommitRecoveryRequiredException(
    string message,
    bool commitAcknowledged,
    Exception innerException) : Exception(message, innerException)
{
    public bool CommitAcknowledged { get; } = commitAcknowledged;
}

internal sealed record RollbackCommitManifest(
    int SchemaVersion,
    string Tool,
    string State,
    string RollbackMode,
    string GraphManifestSha256,
    string ApplyTransactionId,
    string RollbackTransactionId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<SnapshotFile> PostRollbackImage,
    bool RequiresPostRestartHourlyAndChildRefreshVerification);

internal sealed record RollbackCommitMarker(
    int SchemaVersion,
    string Tool,
    string GraphManifestSha256,
    string RollbackMode,
    string RollbackTransactionId,
    string RollbackCommitReadyManifestSha256,
    DateTimeOffset CommittedAtUtc);

internal sealed record MaintenanceEvidence(
    int SchemaVersion,
    string Tool,
    string State,
    string GraphManifestSha256,
    string ApplyTransactionId,
    DateTimeOffset ApplyMutationTimestampUtc,
    DateTimeOffset MaintenanceStartedAtUtc,
    DateTimeOffset MaintenanceCompletedAtUtc,
    int DashboardStrategyCount,
    int DashboardRecentFactCount,
    int DashboardRecentRowCount,
    int DashboardEventsDiscarded,
    int HourlyRowsWritten,
    int CopiedPerformanceRowsWritten,
    IReadOnlyList<SnapshotFile> Preimage,
    IReadOnlyList<SnapshotFile> Postimage);

internal sealed record MaintenanceStartEvidence(
    int SchemaVersion,
    string Tool,
    string State,
    string GraphManifestSha256,
    string ApplyTransactionId,
    DateTimeOffset ApplyMutationTimestampUtc,
    DateTimeOffset MaintenanceStartedAtUtc,
    IReadOnlyList<SnapshotFile> Preimage);

internal sealed record PostChildGateEvidence(
    int SchemaVersion,
    string Tool,
    string GraphManifestSha256,
    string ApplyTransactionId,
    DateTimeOffset MaintenanceCompletedAtUtc,
    DateTimeOffset ChildRefreshCompletedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    int Children,
    int ActiveParents,
    int CycleActiveAssignmentRows,
    DateTimeOffset? EarliestCycleAssignmentUpdatedAtUtc,
    DateTimeOffset? LatestCycleAssignmentUpdatedAtUtc,
    string ChildRefreshAttestationSha256,
    string ServiceLogSha256,
    string StoppedServiceAttestationSha256,
    SnapshotFile AssignmentSnapshot);

internal sealed class RollbackCommitRecoveryRequiredException(
    string message,
    bool commitAcknowledged,
    Exception innerException) : Exception(message, innerException)
{
    public bool CommitAcknowledged { get; } = commitAcknowledged;
}

internal static class Parse
{
    public static Guid Guid(string value, string field) =>
        System.Guid.TryParse(value, out var result)
            ? result
            : throw new InvalidDataException($"Invalid UUID in {field}: '{value}'.");

    public static decimal Decimal(string value, string field) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException($"Invalid decimal in {field}: '{value}'.");

    public static long Long(string value, string field) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : throw new InvalidDataException($"Invalid integer in {field}: '{value}'.");

    public static int Int(string value, string field) => checked((int)Long(value, field));

    public static bool Bool(string value, string field) =>
        bool.TryParse(value, out var result)
            ? result
            : throw new InvalidDataException($"Invalid boolean in {field}: '{value}'.");

    public static DateTimeOffset Timestamp(string value, string field)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var result))
        {
            throw new InvalidDataException($"Invalid timestamp in {field}: '{value}'.");
        }

        return result.ToUniversalTime();
    }
}
