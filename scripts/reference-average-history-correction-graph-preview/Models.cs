using System.Globalization;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal static class CorrectionContract
{
    public const string RequiredHost = "192.168.0.101";
    public const int RequiredPort = 5432;
    public const string RequiredDatabase = "polycopytrader";
    public const string RequiredInputManifestSha256 =
        "19BE8C1EA87BBA18FEEAEC4791EA075C3649EC0276225BDE9E85097A8BB8EACD";
    public static readonly DateTimeOffset RequiredCutoffUtc =
        DateTimeOffset.ParseExact(
            "2026-07-27T13:24:05.9322820+00:00",
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    public const string RequiredInputTool = "reference-average-history-correction-preview";
    public const int RequiredInputSchemaVersion = 1;
    public const string RequiredInputCatalogPath =
        "Codex/Tasks/REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md";
    public const string RequiredInputCatalogSourceSha256 =
        "D39901FAF640AEDF94DB4CD4A1CCFECA817E51C828BC140D86664A9E5B42D0B1";
    public const string RequiredInputCatalogCsvSha256 =
        "E92924047BBC020A45DB04B1B36757DE3713D7B9B49A46DACF9EF437252589C1";
    public const string RequiredInputSourceManifestSha256 =
        "FC6105945EA23E7D2B52156CEC8D8249D0F88CCDDC8AF9B0F2B87DEA028EFB7B";
    public const string RequiredInputReplayClassifierSha256 =
        "BFDF594E4DEEDE640C08D9AA7C41EE34DD056B781A9ADB2DC329B97FCF21FC08";
    public const string LegacyReferenceDecisionSource = "reference_price_max_average_bps_premarket";
    public const int RequiredCatalogStrategyCount = 848;
    public const int RequiredPotentialAddStrategyCount = 192;
    public const int RequiredRemoveCount = 603_460;
    public const int RequiredAddCount = 327;
    public const int RequiredRetainCount = 41_390;
    public const int RequiredStillSkipCount = 84_981;
    public const int RequiredUnreplayableCount = 27_911;
    public const decimal RegularFillPrice = 0.52m;
    public const decimal LowerEnterFillPrice = 0.50m;
    public const decimal FakWorstPrice = 0.99m;
    public const decimal MinimumStakeSafetyMultiplier = 1.10m;
    public const int ResolutionMinimumSamples = 2;
    public const int ResolutionMaximumEndAgeMilliseconds = 15_000;
    public const string PotentialAddSkipReason = "optimized_average_required_window_not_selected";
    public const string ChildPricingMode = "child_parent_mirror";
    public const string CorrectedSkipReason = "reference_average_history_correction_v2_would_skip";
}

internal sealed record SignalPreviewRow(
    string Scope,
    string Asset,
    string Family,
    string Location,
    string Kind,
    string Trigger,
    int CatalogThresholdBps,
    Guid StrategyId,
    string StrategyCode,
    Guid RunId,
    Guid? PaperOrderId,
    string MarketId,
    DateTimeOffset EntryDueAtUtc,
    DateTimeOffset? SettledAtUtc,
    string RunOutcome,
    string OrderOutcome,
    string Action,
    string Reason,
    decimal? AssumedFillPrice,
    string LegacyV1Outcome,
    string CorrectedV2Outcome,
    string ReplayEvidenceJson,
    string ReplayEvidenceSha256);

internal sealed record SignalPreviewFileEvidence(string FileName, long RowCount, string Sha256);

internal sealed record SignalPreviewCatalogRow(
    string Asset,
    string Family,
    string Location,
    string Kind,
    string Trigger,
    int CatalogThresholdBps,
    int ReferenceThresholdBps,
    Guid StrategyId,
    string StrategyCode,
    string StrategyName,
    bool UsesLowEnterPrice);

internal sealed record SignalPreviewInput(
    string Directory,
    string ManifestPath,
    string ManifestSha256,
    DateTimeOffset CutoffUtc,
    string ServerAddress,
    IReadOnlyList<SignalPreviewRow> Removes,
    IReadOnlyList<SignalPreviewRow> Adds,
    IReadOnlyList<SignalPreviewCatalogRow> Catalog,
    IReadOnlyList<SignalPreviewFileEvidence> Files);

internal sealed record ChildStrategy(Guid Id, string Code, string Name, string Asset, string Kind);

internal sealed record ChildRunLinkEvidence(
    Guid RunId,
    Guid StrategyId,
    Guid? SignalId,
    Guid? PaperOrderId,
    DateTimeOffset EntryDueAtUtc);

internal sealed record ChildOrderRunLinkValidation(
    bool Valid,
    int CandidateRunCount,
    int ExactPreCutoffRunCount,
    string Reason);

internal sealed record GraphOrder(
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
    string? SignalTraderWalletProof,
    decimal? SignalLeaderPriceProof,
    int? SignalScoreProof,
    bool? SignalAcceptedProof,
    string? SignalDecisionProof,
    decimal? SignalProposedPaperPriceProof,
    decimal? SignalProposedSizeSharesProof,
    decimal? SignalProposedNotionalUsdProof,
    DateTimeOffset? SignalCreatedAtUtcProof,
    DateTimeOffset OrderExpiresAtUtc,
    DateTimeOffset? OrderFilledAtUtc,
    DateTimeOffset? OrderCancelledAtUtc,
    string RunFullRowSha256,
    string OrderFullRowSha256,
    string SignalFullRowSha256,
    string StrategyNameProof,
    string MarketSlugProof,
    string? RunCategoryProof,
    DateTimeOffset? RunEnteredAtUtcProof,
    DateTimeOffset RunCreatedAtUtcProof,
    DateTimeOffset RunUpdatedAtUtcProof,
    DateTimeOffset? MarketEndUtcProof,
    string? RunSkipReasonProof,
    bool RunSkipDiagnosticsIsNullProof,
    Guid? SignalLeaderTradeIdProof,
    decimal? SignalBestBidProof,
    decimal? SignalBestAskProof,
    decimal? SignalSpreadAbsProof,
    decimal? SignalSpreadPctProof,
    int? SignalLagSecondsProof,
    string? SignalRawContextJsonProof,
    bool SignalNullableShapeValidProof,
    string OrderExecutionModeProof,
    string RawDecisionProofSha256,
    string? RawDecisionJson);

internal sealed record GraphFill(
    string Scope,
    Guid? ParentMainRunId,
    Guid RunId,
    Guid OrderId,
    Guid FillId,
    decimal Price,
    decimal SizeShares,
    DateTimeOffset FilledAtUtc,
    decimal RealizedPnlUsd,
    string Evidence,
    string FullRowSha256);

internal sealed record MainRemovalSummary(
    Guid RunId,
    Guid StrategyId,
    string StrategyCode,
    string MarketId,
    Guid OrderId,
    Guid SignalId,
    string AssetId,
    string Outcome,
    string CopiedTraderWallet,
    int FillCount,
    decimal FillSizeShares,
    decimal FillNotionalUsd,
    decimal FillRealizedPnlUsd,
    decimal RunRealizedPnlUsd,
    DateTimeOffset SettledAtUtc,
    string CorrectedSkipReason,
    DateTimeOffset CorrectedSkippedUpdatedAtUtc,
    decimal RestoredBaseStakeUsd,
    decimal HistoricalEffectiveStakeUsd,
    decimal HistoricalTargetNotionalUsd,
    string HistoricalStakeSizingSource,
    string StakeSizingProofSha256,
    string ClassifierAction,
    string ClassifierReason,
    string SignalPreviewManifestSha256,
    string ReplayClassifierSha256,
    string ReplayEvidenceJson,
    string ReplayEvidenceSha256,
    string GraphStateSha256,
    string FillSetSha256);

internal sealed record RemovalStakeEvidence(
    decimal BaseStakeUsd,
    decimal EffectiveStakeUsd,
    decimal TargetNotionalUsd,
    string StakeSizingSource,
    string ProofSha256);

internal sealed record ChildRemovalSummary(
    Guid ParentRunId,
    Guid ParentOrderId,
    Guid ParentSignalId,
    Guid ChildRunId,
    Guid ChildStrategyId,
    string ChildStrategyCode,
    string MarketId,
    Guid ChildOrderId,
    Guid ChildSignalId,
    string Outcome,
    int FillCount,
    decimal FillSizeShares,
    decimal FillNotionalUsd,
    decimal RunRealizedPnlUsd,
    DateTimeOffset SettledAtUtc,
    string GraphStateSha256,
    string FillSetSha256);

internal sealed record LiveShadowOverlap(
    string Relation,
    string RowType,
    string RowId,
    Guid? StrategyId,
    Guid? PaperOrderId,
    Guid? SignalId,
    Guid? LiveOrderId,
    Guid? CorrelationId,
    string Status,
    string Details,
    bool BlocksMutation);

internal sealed record DependencyRow(
    string DependencyClass,
    string Relation,
    string TableName,
    string RowId,
    Guid? GraphOrderId,
    Guid? GraphSignalId,
    Guid? CorrelationId,
    string Details,
    bool BlocksMutation);

internal sealed record ForeignKeyEvidence(
    string ConstraintName,
    string SourceTable,
    string SourceColumns,
    string TargetTable,
    string TargetColumns,
    string DeleteAction,
    string UpdateAction,
    bool Expected);

internal sealed record SchemaReferenceColumn(string TableName, string ColumnName, string DataType, bool Expected);

internal sealed record PositionKey(
    string CopiedTraderWallet,
    string AssetId,
    int GraphOrderCount,
    int DatabaseOrderCount,
    int OutsideGraphOrderCount,
    int PositionCount,
    int SettlementCount,
    bool Exclusive,
    bool BlocksMutation,
    string Details);

internal sealed record PositionRow(
    Guid Id,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    decimal SizeShares,
    decimal AveragePrice,
    decimal EstimatedValueUsd,
    decimal UnrealizedPnlUsd,
    DateTimeOffset UpdatedAtUtc,
    string FullRowSha256);

internal sealed record PositionSettlementRow(
    Guid Id,
    string CopiedTraderWallet,
    string AssetId,
    string ConditionId,
    string Outcome,
    string? WinningAssetId,
    string WinningOutcome,
    decimal SettledSizeShares,
    decimal AveragePrice,
    decimal CostBasisUsd,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    bool Won,
    string SettlementSource,
    DateTimeOffset SettledAtUtc,
    string? Category,
    DateTimeOffset CreatedAtUtc,
    string FullRowSha256);

internal sealed record PositionSemanticValidation(
    bool Valid,
    string Reason,
    string Details);

internal sealed record GammaMarketEvidence(
    string MarketId,
    string ConditionId,
    decimal? OrderMinSize,
    string OutcomesJson,
    string TokenIdsJson);

internal sealed record LiveGammaResolutionEvidence(
    string MarketId,
    string ConditionId,
    string MarketSlug,
    bool Closed,
    string OutcomesJson,
    string TokenIdsJson,
    string OutcomePricesJson,
    string WinningOutcome,
    string WinningTokenId,
    decimal? OrderMinSize,
    string? ResolutionSource,
    string RequestUrl,
    string RawSha256,
    long RawBytes,
    DateTimeOffset FetchedAtUtc);

internal sealed record ResolvedMarketLedgerEvidence(
    Guid Id,
    string AssetSymbol,
    string MarketId,
    string ConditionId,
    string MarketSlug,
    DateTimeOffset MarketStartUtc,
    DateTimeOffset MarketEndUtc,
    string WinningOutcome,
    string? WinningAssetId,
    DateTimeOffset EventTimestampUtc,
    DateTimeOffset FirstReceivedAtUtc,
    DateTimeOffset LastReceivedAtUtc,
    int EventCount,
    decimal ResultDelaySeconds,
    string Source,
    string RawEventType,
    string RawJson,
    string RawSha256,
    long RawBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

internal sealed record MarketResolvedEventEvidence(
    Guid Id,
    string Component,
    string RawEventType,
    string AssetId,
    string ConditionId,
    string WinningAssetId,
    string WinningOutcome,
    DateTimeOffset EventTimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    bool ActiveSnapshotFound,
    string SnapshotMarketId,
    string SnapshotConditionId,
    string SnapshotMarketSlug,
    string SnapshotAssetSymbol,
    DateTimeOffset SnapshotMarketStartUtc,
    bool SnapshotIsCryptoUpDown5m,
    string RecorderAction,
    string RawJson,
    string RawSha256,
    long RawBytes,
    DateTimeOffset CreatedAtUtc);

internal sealed record ValidatedMarketResolvedDiagnostics(
    string MarketId,
    string ConditionId,
    string WinningOutcome,
    string WinningTokenId,
    int DiagnosticRowCount,
    int DistinctRawEventCount,
    string Source,
    string ProvenanceGroup);

internal sealed record AddSourceRow(
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
    string MarketSlug,
    string RunFullRowSha256,
    DateTimeOffset UpdatedAtUtc,
    string? Category);

internal sealed record ReferenceResolutionTick(
    string AssetSymbol,
    string MarketId,
    string ConditionId,
    DateTimeOffset MarketEndUtc,
    DateTimeOffset SampledAtUtc,
    decimal BinancePriceUsd,
    decimal BinanceStartPriceUsd,
    DateTimeOffset BinanceSourceUpdatedAtUtc,
    DateTimeOffset CreatedAtUtc);

internal sealed record ArchivedReferenceResolution(
    string AssetSymbol,
    string MarketId,
    string ConditionId,
    int SampleCount,
    decimal StartPriceUsd,
    decimal EndPriceUsd,
    DateTimeOffset StartSampledAtUtc,
    DateTimeOffset EndSampledAtUtc,
    DateTimeOffset EndSourceUpdatedAtUtc,
    decimal EndAgeMilliseconds,
    string WinningOutcome,
    string Source,
    string ProvenanceGroup);

internal sealed record AddFeasibility(
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
    string SelectedOutcome,
    string SelectedTokenId,
    string ResolvedWinningOutcome,
    string ResolvedWinningTokenId,
    string ResolutionLedgerSource,
    string ResolutionLedgerProvenanceGroup,
    string ResolutionLedgerWinningAssetId,
    bool ResolutionLedgerWinningAssetAgreesWithGamma,
    string ResolutionLedgerRawEventType,
    string ResolutionLedgerRawSha256,
    long ResolutionLedgerRawBytes,
    DateTimeOffset ResolutionLedgerEventTimestampUtc,
    DateTimeOffset? ResolutionLedgerRawEventTimestampUtc,
    DateTimeOffset ResolutionLedgerFirstReceivedAtUtc,
    DateTimeOffset ResolutionLedgerLastReceivedAtUtc,
    bool ResolutionLedgerRawValidated,
    string RawWebSocketResolutionSource,
    string RawWebSocketResolutionProvenanceGroup,
    int RawWebSocketDiagnosticRowCount,
    int RawWebSocketDistinctEventCount,
    string ArchivedTickSource,
    string ArchivedTickProvenanceGroup,
    int ArchivedTickSampleCount,
    decimal ArchivedTickStartPriceUsd,
    decimal ArchivedTickEndPriceUsd,
    decimal ArchivedTickEndAgeMilliseconds,
    string ArchivedTickWinningOutcome,
    bool ArchivedTickAgreesWithAuthoritativeWinner,
    string GammaResolutionSource,
    string GammaResolutionProvenanceGroup,
    string GammaRequestUrl,
    string GammaRawSha256,
    long GammaRawBytes,
    DateTimeOffset GammaFetchedAtUtc,
    string GammaResolutionSourceDetail,
    decimal? GammaLiveOrderMinSize,
    int AgreeingIndependentResolutionSourceCount,
    decimal RawWorstPriceNotionalUsd,
    decimal RoundedWorstPriceNotionalUsd,
    decimal WorstPriceTargetSizeShares,
    decimal RequestedNotionalUsd,
    decimal FilledSizeShares,
    bool Won,
    decimal SettlementPrice,
    decimal SettlementValueUsd,
    decimal RealizedPnlUsd,
    bool CanAdd,
    string Reason);

internal sealed record ModeledAddPayloadEvidence(
    string RawDecisionJson,
    string RawDecisionSha256,
    string FillEvidence,
    string PayloadJson,
    string PayloadSha256);

internal sealed record InvariantError(string Scope, string EntityId, string Code, string Details);

internal sealed record GraphSemanticValidation(bool Valid, string Reason, string Details);

internal sealed record DatabaseSnapshotMetadata(
    string Host,
    int Port,
    string Database,
    string ServerAddress,
    int ServerPort,
    string CurrentDatabase,
    string ServerVersion,
    string TransactionIsolation,
    bool TransactionReadOnly,
    string TimeZone,
    string SearchPath,
    long DailyReportsRowCount);

internal sealed record ReconciliationTarget(
    string TargetId,
    string TableName,
    string KeyScope,
    string MethodId,
    string RequiredAction,
    string Reason,
    bool BlocksMutation)
{
    public string TargetContractSha256 => ReconciliationContract.HashTarget(this);
}

internal sealed record OperationFootprintRow(
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

internal sealed record SqlRehearsalEvidence(
    string QueryName,
    string SqlSha256,
    string PostgreSqlVersion,
    bool ExplainPlanned);

internal sealed record OutputEvidence(string FileName, long RowCount, string Sha256);

internal static class Format
{
    public static string Decimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    public static string NullableDecimal(decimal? value) => value is null ? string.Empty : Decimal(value.Value);
    public static string Guid(Guid? value) => value?.ToString("D") ?? string.Empty;
    public static string Timestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
    public static string Bool(bool value) => value ? "true" : "false";
}
