using System.Data;
using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Storage;

namespace ReferenceAverageHistoryCorrectionApply;

internal sealed class CorrectionDatabase(ToolOptions options, GraphPackage graph)
{
    private const long AdvisoryLockKey = 0x5241464856435632; // "RAFHVCV2"
    private static readonly IReadOnlySet<string> BaseSnapshotTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "signals", "signal_rejections", "paper_orders", "paper_fills",
        "strategy_market_paper_runs", "paper_positions", "paper_position_settlements"
    };

    public async Task<DatabasePreflight> PreflightAsync(CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: true, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options, expectedReadOnly: true,
                "repeatable read", cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var report = await InspectRuntimeAsync(connection, transaction, requireStoppedService: false, cancellationToken);
            VerifyFreshOperationFootprint(report.ScopedFootprint);
            await ValidateCurrentRowsAsync(connection, transaction, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return report;
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<string> ApplyAsync(CancellationToken cancellationToken)
    {
        var stagingRoot = options.StagingDirectory ?? throw new InvalidOperationException("Staging directory missing.");
        var durableRoot = options.DurableBackupDirectory ??
                          throw new InvalidOperationException("Durable backup directory missing.");
        using var prepared = PreparedPackageStore.VerifyAndLock(options, graph);
        PrepareEmptyDirectory(stagingRoot, "staging");

        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var triggersDisabled = false;
        var commitAttempted = false;
        var commitAcknowledged = false;
        var durableManifestPath = Path.Combine(durableRoot, "backup-manifest.json");
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options, expectedReadOnly: false,
                "serializable", cancellationToken);
            await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireTableLocksAsync(connection, transaction, cancellationToken);
            var loaded = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true, cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            VerifyFreshOperationFootprint(runtime.ScopedFootprint);
            await ValidateCurrentRowsAsync(connection, transaction, cancellationToken);

            var scopedStaging = Path.Combine(stagingRoot, "scoped");
            var preimage = await BackupStore.SnapshotAsync(connection, transaction, scopedStaging, "preimage",
                cancellationToken);
            BackupStore.ValidateSnapshotSet(scopedStaging, preimage);

            var mutationTimestamp = await ScalarTimestampAsync(connection, transaction,
                "SELECT clock_timestamp();", cancellationToken);
            var transactionId = await ScalarStringAsync(connection, transaction,
                "SELECT pg_current_xact_id()::text;", cancellationToken);
            var preparedManifest = BuildBackupManifest("prepared", mutationTimestamp, transactionId,
                preimage, [], loaded, prepared.Manifest);
            var stagingManifestPath = Path.Combine(scopedStaging, "backup-manifest.json");
            await BackupStore.WriteManifestAsync(stagingManifestPath, preparedManifest, cancellationToken);
            BackupStore.CopyScopedEvidence(scopedStaging, durableRoot);
            ValidateDurablePreimage(durableRoot, preparedManifest);

            await SetProjectionTriggersAsync(connection, transaction, enabled: false, cancellationToken);
            triggersDisabled = true;
            await ApplyMutationAsync(connection, transaction, mutationTimestamp, cancellationToken);
            await SetProjectionTriggersAsync(connection, transaction, enabled: true, cancellationToken);
            triggersDisabled = false;
            await VerifyAppliedStateAsync(connection, transaction, cancellationToken);

            var postimage = await BackupStore.SnapshotAsync(connection, transaction, scopedStaging, "postimage",
                cancellationToken);
            BackupStore.ValidateSnapshotSet(scopedStaging, postimage);
            CopyPostimageToDurable(scopedStaging, durableRoot, postimage);
            var readyManifest = BuildBackupManifest("commit_ready", mutationTimestamp, transactionId,
                preimage, postimage, loaded, prepared.Manifest);
            var immutableReadyPath = Path.Combine(durableRoot, "commit-ready-manifest.json");
            await BackupStore.WriteManifestNewAsync(immutableReadyPath, readyManifest, cancellationToken);
            await BackupStore.WriteManifestAsync(durableManifestPath, readyManifest, cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            commitAcknowledged = true;

            await FinalizeDurableApplyEvidenceAsync(durableRoot, readyManifest, CancellationToken.None);
            return durableManifestPath;
        }
        catch (Exception exception)
        {
            if (commitAcknowledged)
            {
                throw new ApplyCommitRecoveryRequiredException(
                    "DATABASE COMMIT WAS ACKNOWLEDGED, but durable evidence finalization failed. " +
                    $"Do not rerun --apply. Run --finalize-apply against {durableManifestPath}.",
                    commitAcknowledged: true,
                    exception);
            }

            if (commitAttempted)
            {
                throw new ApplyCommitRecoveryRequiredException(
                    "DATABASE COMMIT OUTCOME IS UNKNOWN because CommitAsync did not return successfully. " +
                    $"Do not rerun --apply and do not assume rollback. Run --finalize-apply against {durableManifestPath}; " +
                    "it will require committed transaction status and exact postimage proof.",
                    commitAcknowledged: false,
                    exception);
            }

            if (triggersDisabled)
            {
                // Trigger DDL is transactional; rollback restores the original trigger state even if this re-enable fails.
                try
                {
                    await SetProjectionTriggersAsync(connection, transaction, enabled: true, cancellationToken);
                }
                catch
                {
                    // Preserve the original exception; transaction rollback is the recovery boundary.
                }
            }

            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<string> FinalizeApplyAsync(CancellationToken cancellationToken)
    {
        var manifestPath = options.RollbackManifestPath ??
                           throw new InvalidOperationException("Apply manifest path is missing.");
        var manifest = BackupStore.ReadManifest(manifestPath);
        ValidateFinalizeManifest(manifest, manifestPath);
        var durableRoot = Path.GetDirectoryName(manifestPath)!;
        var scopedDirectory = Path.Combine(durableRoot, "scoped");
        BackupStore.ValidateSnapshotSet(scopedDirectory, manifest.Preimage);
        BackupStore.ValidateSnapshotSet(scopedDirectory, manifest.Postimage);

        var stagingRoot = options.StagingDirectory ??
                          throw new InvalidOperationException("Finalize staging directory is missing.");
        PrepareEmptyDirectory(stagingRoot, "finalize staging");

        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options, expectedReadOnly: false,
                "serializable", cancellationToken);
            await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireTableLocksAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            await VerifyCommittedTransactionAsync(connection, transaction, manifest.TransactionId, cancellationToken);
            await VerifyAppliedStateAsync(connection, transaction, cancellationToken);
            await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
            await VerifyCurrentPostimageAsync(connection, transaction, manifest, stagingRoot, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }

        await FinalizeDurableApplyEvidenceAsync(durableRoot, manifest with { State = "commit_ready" },
            CancellationToken.None);
        return manifestPath;
    }

    public async Task RollbackAsync(bool reconciled, CancellationToken cancellationToken)
    {
        var manifestPath = options.RollbackManifestPath ??
                           throw new InvalidOperationException("Rollback manifest path is missing.");
        var manifest = BackupStore.ReadManifest(manifestPath);
        ValidateRollbackManifest(manifest, manifestPath);
        var durableRoot = Path.GetDirectoryName(manifestPath)!;
        var scopedDirectory = Path.Combine(durableRoot, "scoped");
        var rollbackStaging = options.StagingDirectory ??
                              throw new InvalidOperationException("Rollback staging directory is missing.");
        PrepareEmptyDirectory(rollbackStaging, "rollback staging");
        BackupStore.ValidateSnapshotSet(scopedDirectory, manifest.Preimage);
        BackupStore.ValidateSnapshotSet(scopedDirectory, manifest.Postimage);

        MaintenanceStartEvidence? reconciledMaintenanceStart = null;
        Guid[]? reconciledHourlyStrategyIds = null;
        if (reconciled)
        {
            var maintenanceRoot = Path.Combine(durableRoot, "maintenance");
            reconciledMaintenanceStart = BackupStore.ReadJson<MaintenanceStartEvidence>(
                Path.Combine(maintenanceRoot, "maintenance-started.json"), "Maintenance start evidence");
            ValidateMaintenanceStart(reconciledMaintenanceStart, manifest);
            var maintenance = BackupStore.ReadJson<MaintenanceEvidence>(
                Path.Combine(maintenanceRoot, "maintenance-complete.json"), "Maintenance completion evidence");
            ValidateMaintenanceComplete(maintenance, reconciledMaintenanceStart, manifest);
            var maintenanceSnapshots = Path.Combine(maintenanceRoot, "scoped");
            BackupStore.ValidateMaintenanceSnapshotSet(maintenanceSnapshots, maintenance.Preimage,
                "maintenance-preimage");
            BackupStore.ValidateMaintenanceSnapshotSet(maintenanceSnapshots, maintenance.Postimage,
                "maintenance-postimage");
            reconciledHourlyStrategyIds = StrategyIds.DateDependentStrategyVariants
                .Select(variant => StrategyIds.Normalize(variant.Id))
                .Distinct()
                .Order()
                .ToArray();
            if (reconciledHourlyStrategyIds.Length == 0)
            {
                throw new InvalidOperationException("The exact Domain date-dependent strategy set is empty.");
            }
        }

        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var triggersDisabled = false;
        var commitAttempted = false;
        var commitAcknowledged = false;
        RollbackCommitManifest? rollbackReady = null;
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options, expectedReadOnly: false,
                "serializable", cancellationToken);
            await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireTableLocksAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var rollbackTransactionId = await ScalarStringAsync(connection, transaction,
                "SELECT pg_current_xact_id()::text;", cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true, cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            if (reconciled)
            {
                await VerifyMaintenanceDerivedStateAsync(connection, transaction,
                    reconciledMaintenanceStart!.MaintenanceStartedAtUtc,
                    reconciledHourlyStrategyIds!, cancellationToken);
                await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
                await ProveZeroNewAffectedDecisionsAsync(connection, transaction, manifest.MutationTimestampUtc,
                    cancellationToken);
                var current = await BackupStore.SnapshotAsync(connection, transaction, rollbackStaging,
                    "reconciled-current", cancellationToken);
                PersistReconciledRollbackPreimage(durableRoot, current, rollbackStaging, manifest);
            }
            else
            {
                await VerifyCurrentPostimageAsync(connection, transaction, manifest, rollbackStaging, cancellationToken);
            }

            await SetProjectionTriggersAsync(connection, transaction, enabled: false, cancellationToken);
            triggersDisabled = true;
            await DeleteCurrentScopeAsync(connection, transaction, cancellationToken);
            await BackupStore.RestorePreimageAsync(connection, transaction, scopedDirectory, manifest.Preimage,
                reconciled ? BaseSnapshotTables : null, cancellationToken);
            if (reconciled)
            {
                await EnqueueReconciliationAsync(connection, transaction, DateTimeOffset.UtcNow, cancellationToken);
            }
            await SetProjectionTriggersAsync(connection, transaction, enabled: true, cancellationToken);
            triggersDisabled = false;
            if (!reconciled)
            {
                await VerifyRestoredPreimageAsync(connection, transaction, manifest, rollbackStaging, cancellationToken);
            }
            else
            {
                await VerifyRestoredBasePreimageAsync(connection, transaction, manifest, rollbackStaging,
                    cancellationToken);
            }
            var rollbackSnapshotDirectory = Path.Combine(rollbackStaging, "rollback-commit-snapshot");
            var rollbackPostimage = await BackupStore.SnapshotAsync(connection, transaction,
                rollbackSnapshotDirectory, "rollback-postimage", cancellationToken);
            BackupStore.ValidateSnapshotSet(rollbackSnapshotDirectory, rollbackPostimage);
            CopyPostimageToDurable(rollbackSnapshotDirectory, durableRoot, rollbackPostimage);
            rollbackReady = new RollbackCommitManifest(
                1,
                "reference-average-history-correction-apply",
                "commit_ready",
                reconciled ? "reconciled_zero_new_decisions" : "immediate_exact_postimage",
                graph.Manifest.ManifestSha256,
                manifest.TransactionId,
                rollbackTransactionId,
                DateTimeOffset.UtcNow,
                rollbackPostimage,
                reconciled);
            await BackupStore.WriteJsonNewAsync(Path.Combine(durableRoot, "rollback-commit-ready.json"),
                rollbackReady, cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            commitAcknowledged = true;
            await FinalizeDurableRollbackEvidenceAsync(durableRoot, manifest, rollbackReady,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            if (commitAcknowledged)
            {
                throw new RollbackCommitRecoveryRequiredException(
                    "ROLLBACK DATABASE COMMIT WAS ACKNOWLEDGED, but durable evidence finalization failed. " +
                    $"Do not rerun rollback. Run --finalize-rollback against {manifestPath}.",
                    commitAcknowledged: true,
                    exception);
            }
            if (commitAttempted)
            {
                throw new RollbackCommitRecoveryRequiredException(
                    "ROLLBACK DATABASE COMMIT OUTCOME IS UNKNOWN because CommitAsync did not return successfully. " +
                    $"Do not retry rollback and do not assume rollback. Run --finalize-rollback against {manifestPath}.",
                    commitAcknowledged: false,
                    exception);
            }

            if (triggersDisabled)
            {
                try
                {
                    await SetProjectionTriggersAsync(connection, transaction, enabled: true, cancellationToken);
                }
                catch
                {
                    // Transaction rollback restores trigger state.
                }
            }

            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    public async Task<string> FinalizeRollbackAsync(CancellationToken cancellationToken)
    {
        var manifestPath = options.RollbackManifestPath ??
                           throw new InvalidOperationException("Rollback manifest path is missing.");
        var applyManifest = BackupStore.ReadManifest(manifestPath);
        ValidateFinalizeRollbackApplyManifest(applyManifest, manifestPath);
        var durableRoot = Path.GetDirectoryName(manifestPath)!;
        var readyPath = Path.Combine(durableRoot, "rollback-commit-ready.json");
        var ready = BackupStore.ReadJson<RollbackCommitManifest>(readyPath, "Rollback commit-ready manifest");
        ValidateRollbackReadyManifest(ready, applyManifest);
        BackupStore.ValidateSnapshotSet(Path.Combine(durableRoot, "scoped"), ready.PostRollbackImage);

        var stagingRoot = options.StagingDirectory ??
                          throw new InvalidOperationException("Finalize rollback staging directory is missing.");
        PrepareEmptyDirectory(stagingRoot, "finalize rollback staging");

        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options, expectedReadOnly: false,
                "serializable", cancellationToken);
            await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireTableLocksAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            await VerifyCommittedTransactionAsync(connection, transaction, ready.RollbackTransactionId,
                cancellationToken);
            var verificationDirectory = Path.Combine(stagingRoot, "rollback-postimage-verification");
            var current = await BackupStore.SnapshotAsync(connection, transaction, verificationDirectory,
                "rollback-postimage", cancellationToken);
            CompareSnapshotSets(ready.PostRollbackImage, current, "finalized rollback postimage");
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }

        await FinalizeDurableRollbackEvidenceAsync(durableRoot, applyManifest, ready, CancellationToken.None);
        return manifestPath;
    }

    public async Task<string> MaintenanceRebuildAsync(CancellationToken cancellationToken)
    {
        var manifestPath = options.RollbackManifestPath ??
                           throw new InvalidOperationException("Apply manifest path is missing.");
        var applyManifest = BackupStore.ReadManifest(manifestPath);
        ValidateRollbackManifest(applyManifest, manifestPath);

        var durableRoot = Path.GetDirectoryName(manifestPath)!;
        var maintenanceRoot = Path.Combine(durableRoot, "maintenance");
        var durableSnapshotRoot = Path.Combine(maintenanceRoot, "scoped");
        var startPath = Path.Combine(maintenanceRoot, "maintenance-started.json");
        var completePath = Path.Combine(maintenanceRoot, "maintenance-complete.json");
        if (File.Exists(completePath))
        {
            throw new InvalidOperationException(
                "Stopped-service maintenance is already complete; blind rebuild retry is forbidden.");
        }

        var stagingRoot = options.StagingDirectory ??
                           throw new InvalidOperationException("Maintenance staging directory is missing.");
        PrepareEmptyDirectory(stagingRoot, "maintenance staging");
        await using var maintenanceLease = await AcquireMaintenanceSessionLeaseAsync(cancellationToken);

        MaintenanceStartEvidence start;
        if (File.Exists(startPath))
        {
            start = BackupStore.ReadJson<MaintenanceStartEvidence>(startPath, "Maintenance start evidence");
            ValidateMaintenanceStart(start, applyManifest);
            BackupStore.ValidateMaintenanceSnapshotSet(durableSnapshotRoot, start.Preimage, "maintenance-preimage");
        }
        else
        {
            if (Directory.Exists(maintenanceRoot) && Directory.EnumerateFileSystemEntries(maintenanceRoot).Any())
            {
                throw new InvalidDataException(
                    "Incomplete unsealed maintenance evidence exists; inspect it before retrying.");
            }

            Directory.CreateDirectory(durableSnapshotRoot);
            var preimageStaging = Path.Combine(stagingRoot, "preimage");
            IReadOnlyList<SnapshotFile> preimage;
            DateTimeOffset startedAt;
            await using (var connection = DatabaseConnection.Create(options))
            {
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);
                try
                {
                    await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
                    await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                        expectedReadOnly: false, "serializable", cancellationToken);
                    await maintenanceLease.AssertConnectedAsync(cancellationToken);
                    await ValidateSchemaAsync(connection, transaction, cancellationToken);
                    await AcquireMaintenanceTableLocksAsync(connection, transaction, cancellationToken);
                    _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
                    var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                        cancellationToken);
                    ThrowIfBlocked(runtime.BlockingErrors);
                    await VerifyAppliedStateAsync(connection, transaction, cancellationToken);
                    await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
                    startedAt = await ScalarTimestampAsync(connection, transaction,
                        "SELECT clock_timestamp();", cancellationToken);
                    preimage = await BackupStore.SnapshotMaintenanceAsync(connection, transaction,
                        preimageStaging, "maintenance-preimage", cancellationToken);
                    BackupStore.ValidateMaintenanceSnapshotSet(preimageStaging, preimage,
                        "maintenance-preimage");
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await TryRollbackAsync(transaction, cancellationToken);
                    throw;
                }
            }

            CopySnapshotSet(preimageStaging, durableSnapshotRoot, preimage);
            BackupStore.ValidateMaintenanceSnapshotSet(durableSnapshotRoot, preimage,
                "maintenance-preimage");
            start = new MaintenanceStartEvidence(
                1,
                "reference-average-history-correction-apply",
                "started",
                graph.Manifest.ManifestSha256,
                applyManifest.TransactionId,
                applyManifest.MutationTimestampUtc,
                startedAt,
                preimage);
            await BackupStore.WriteJsonNewAsync(startPath, start, cancellationToken);
        }

        // The application service remains Stopped+Disabled. These are direct calls to the exact
        // Storage implementations; no hosted entry, child, or Live worker is started here.
        await VerifyStoppedCorrectedStateAsync(maintenanceLease, requirePendingDashboardControl: false,
            cancellationToken);
        var factory = CreateMaintenanceConnectionFactory();
        var dashboardRepository = new PostgresDashboardProjectionRepository(factory);
        var dashboard = await dashboardRepository.BootstrapAsync(cancellationToken);

        await VerifyStoppedCorrectedStateAsync(maintenanceLease, requirePendingDashboardControl: false,
            cancellationToken);
        var repository = new PostgresAppRepository(factory);
        var allDateDependentStrategyIds = StrategyIds.DateDependentStrategyVariants
            .Select(variant => StrategyIds.Normalize(variant.Id))
            .Distinct()
            .Order()
            .ToArray();
        if (allDateDependentStrategyIds.Length == 0)
        {
            throw new InvalidOperationException("The exact Domain date-dependent strategy set is empty.");
        }

        var hourlyRefreshedAt = await repository.GetDatabaseNowUtcAsync(cancellationToken);
        var hourlyRows = await repository.RefreshDateDependentStrategyHourlyPaperPnlAsync(
            allDateDependentStrategyIds, hourlyRefreshedAt, cancellationToken);
        var expectedHourlyRows = checked(allDateDependentStrategyIds.Length * 24);
        if (hourlyRows != expectedHourlyRows)
        {
            throw new InvalidOperationException(
                $"Full hourly refresh wrote {hourlyRows:N0} rows; expected exactly {expectedHourlyRows:N0}.");
        }

        var copiedPerformanceRows = await RefreshCopiedPerformanceAsync(maintenanceLease, start,
            allDateDependentStrategyIds, cancellationToken);

        var postimageStaging = Path.Combine(stagingRoot, "postimage");
        IReadOnlyList<SnapshotFile> postimage;
        DateTimeOffset completedAt;
        await using (var connection = DatabaseConnection.Create(options))
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
                await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                    expectedReadOnly: false, "serializable", cancellationToken);
                await maintenanceLease.AssertConnectedAsync(cancellationToken);
                await ValidateSchemaAsync(connection, transaction, cancellationToken);
                await AcquireMaintenanceTableLocksAsync(connection, transaction, cancellationToken);
                _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
                var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                    cancellationToken);
                ThrowIfBlocked(runtime.BlockingErrors);
                await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
                await VerifyMaintenanceDerivedStateAsync(connection, transaction, start.MaintenanceStartedAtUtc,
                    allDateDependentStrategyIds, cancellationToken);
                completedAt = await ScalarTimestampAsync(connection, transaction,
                    "SELECT clock_timestamp();", cancellationToken);
                postimage = await BackupStore.SnapshotMaintenanceAsync(connection, transaction,
                    postimageStaging, "maintenance-postimage", cancellationToken);
                BackupStore.ValidateMaintenanceSnapshotSet(postimageStaging, postimage,
                    "maintenance-postimage");
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await TryRollbackAsync(transaction, cancellationToken);
                throw;
            }
        }

        CopySnapshotSet(postimageStaging, durableSnapshotRoot, postimage);
        BackupStore.ValidateMaintenanceSnapshotSet(durableSnapshotRoot, postimage,
            "maintenance-postimage");
        var complete = new MaintenanceEvidence(
            1,
            "reference-average-history-correction-apply",
            "complete",
            graph.Manifest.ManifestSha256,
            applyManifest.TransactionId,
            applyManifest.MutationTimestampUtc,
            start.MaintenanceStartedAtUtc,
            completedAt,
            dashboard.Strategies,
            dashboard.RecentFacts,
            dashboard.RecentRows,
            dashboard.BootstrappedEventsDiscarded,
            hourlyRows,
            copiedPerformanceRows,
            start.Preimage,
            postimage);
        await BackupStore.WriteJsonNewAsync(completePath, complete, CancellationToken.None);
        return completePath;
    }

    public async Task<string> PostChildGateAsync(CancellationToken cancellationToken)
    {
        var manifestPath = options.RollbackManifestPath ??
                           throw new InvalidOperationException("Apply manifest path is missing.");
        var applyManifest = BackupStore.ReadManifest(manifestPath);
        ValidateRollbackManifest(applyManifest, manifestPath);
        var durableRoot = Path.GetDirectoryName(manifestPath)!;
        var maintenanceRoot = Path.Combine(durableRoot, "maintenance");
        var maintenanceStartPath = Path.Combine(maintenanceRoot, "maintenance-started.json");
        var maintenanceCompletePath = Path.Combine(maintenanceRoot, "maintenance-complete.json");
        var maintenanceStart = BackupStore.ReadJson<MaintenanceStartEvidence>(maintenanceStartPath,
            "Maintenance start evidence");
        ValidateMaintenanceStart(maintenanceStart, applyManifest);
        var maintenance = BackupStore.ReadJson<MaintenanceEvidence>(maintenanceCompletePath,
            "Maintenance completion evidence");
        ValidateMaintenanceComplete(maintenance, maintenanceStart, applyManifest);
        var maintenanceSnapshots = Path.Combine(maintenanceRoot, "scoped");
        BackupStore.ValidateMaintenanceSnapshotSet(maintenanceSnapshots, maintenance.Preimage,
            "maintenance-preimage");
        BackupStore.ValidateMaintenanceSnapshotSet(maintenanceSnapshots, maintenance.Postimage,
            "maintenance-postimage");

        var childAttestation = ChildRefreshAttestationStore.Read(options,
            maintenance.MaintenanceCompletedAtUtc);
        var stagingRoot = options.StagingDirectory ??
                          throw new InvalidOperationException("Post-child-gate staging directory is missing.");
        PrepareEmptyDirectory(stagingRoot, "post-child-gate staging");
        var assignmentStaging = Path.Combine(stagingRoot, "assignments");

        var allDateDependentStrategyIds = StrategyIds.DateDependentStrategyVariants
            .Select(variant => StrategyIds.Normalize(variant.Id))
            .Distinct()
            .Order()
            .ToArray();
        ChildAssignmentGateMetrics metrics;
        SnapshotFile assignmentSnapshot;
        DateTimeOffset verifiedAt;
        await using (var connection = DatabaseConnection.Create(options))
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            try
            {
                await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
                await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                    expectedReadOnly: false, "serializable", cancellationToken);
                await AcquireAdvisoryLockAsync(connection, transaction, cancellationToken);
                await ValidateSchemaAsync(connection, transaction, cancellationToken);
                await AcquireMaintenanceTableLocksAsync(connection, transaction, cancellationToken);
                _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
                var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                    cancellationToken);
                ThrowIfBlocked(runtime.BlockingErrors);
                await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
                await VerifyMaintenanceDerivedStateAsync(connection, transaction,
                    maintenance.MaintenanceStartedAtUtc, allDateDependentStrategyIds, cancellationToken);
                await ProveZeroNewAffectedDecisionsAsync(connection, transaction,
                    applyManifest.MutationTimestampUtc, cancellationToken);
                metrics = await VerifyChildAssignmentCycleAsync(connection, transaction,
                    maintenance.MaintenanceCompletedAtUtc, childAttestation.Attestation,
                    cancellationToken);
                verifiedAt = await ScalarTimestampAsync(connection, transaction,
                    "SELECT clock_timestamp();", cancellationToken);
                assignmentSnapshot = await BackupStore.SnapshotChildAssignmentsAsync(connection, transaction,
                    assignmentStaging, "post-child-gate", cancellationToken);
                BackupStore.ValidateChildAssignmentSnapshot(assignmentStaging, assignmentSnapshot);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await TryRollbackAsync(transaction, cancellationToken);
                throw;
            }
        }

        var gateRoot = Path.Combine(maintenanceRoot, "child-gate");
        if (Directory.Exists(gateRoot) && Directory.EnumerateFileSystemEntries(gateRoot).Any())
        {
            throw new InvalidOperationException("Child-gate durable evidence already exists; blind retry is forbidden.");
        }
        Directory.CreateDirectory(gateRoot);
        var copiedChildAttestation = Path.Combine(gateRoot, "child-refresh-attestation.json");
        var copiedServiceLog = Path.Combine(gateRoot, "child-refresh-service.log");
        var copiedStoppedAttestation = Path.Combine(gateRoot, "stopped-service-attestation.json");
        File.Copy(childAttestation.AttestationPath, copiedChildAttestation, overwrite: false);
        File.Copy(childAttestation.ServiceLogPath, copiedServiceLog, overwrite: false);
        File.Copy(options.OperatorAttestationPath!, copiedStoppedAttestation, overwrite: false);
        File.Copy(Path.Combine(assignmentStaging, assignmentSnapshot.FileName),
            Path.Combine(gateRoot, assignmentSnapshot.FileName), overwrite: false);
        if (GraphPackageReader.Sha256File(copiedChildAttestation) != childAttestation.AttestationSha256 ||
            GraphPackageReader.Sha256File(copiedServiceLog) != childAttestation.Attestation.ServiceLogSha256 ||
            GraphPackageReader.Sha256File(copiedStoppedAttestation) != options.OperatorAttestationSha256)
        {
            throw new InvalidDataException("Durable child-gate attestation/log copy hash mismatch.");
        }
        BackupStore.ValidateChildAssignmentSnapshot(gateRoot, assignmentSnapshot);

        var evidence = new PostChildGateEvidence(
            1,
            "reference-average-history-correction-apply",
            graph.Manifest.ManifestSha256,
            applyManifest.TransactionId,
            maintenance.MaintenanceCompletedAtUtc,
            childAttestation.Attestation.RefreshCompletedAtUtc,
            verifiedAt,
            childAttestation.Attestation.Children,
            childAttestation.Attestation.ActiveParents,
            metrics.CycleActiveRows,
            metrics.EarliestUpdatedAtUtc,
            metrics.LatestUpdatedAtUtc,
            childAttestation.AttestationSha256,
            childAttestation.Attestation.ServiceLogSha256,
            options.OperatorAttestationSha256!,
            assignmentSnapshot);
        var evidencePath = Path.Combine(gateRoot, "post-child-gate.json");
        await BackupStore.WriteJsonNewAsync(evidencePath, evidence, CancellationToken.None);
        return evidencePath;
    }

    private PostgresConnectionFactory CreateMaintenanceConnectionFactory()
    {
        using var pinned = DatabaseConnection.Create(options);
        return new PostgresConnectionFactory(new StorageOptions
        {
            Provider = "PostgreSQL",
            ConnectionString = pinned.ConnectionString,
            ConnectionStringEnvironmentVariable = DatabaseConnection.ConnectionEnvironmentVariable,
            RequireConfiguredDatabase = true
        }, "reference-average-history-correction-maintenance");
    }

    private async Task VerifyStoppedCorrectedStateAsync(
        MaintenanceSessionLease maintenanceLease,
        bool requirePendingDashboardControl,
        CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                expectedReadOnly: false, "serializable", cancellationToken);
            await maintenanceLease.AssertConnectedAsync(cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireMaintenanceTableLocksAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
            if (requirePendingDashboardControl)
            {
                await VerifyAppliedStateAsync(connection, transaction, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private async Task<int> RefreshCopiedPerformanceAsync(
        MaintenanceSessionLease maintenanceLease,
        MaintenanceStartEvidence start,
        IReadOnlyCollection<Guid> allDateDependentStrategyIds,
        CancellationToken cancellationToken)
    {
        await using var connection = DatabaseConnection.Create(options);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                expectedReadOnly: false, "serializable", cancellationToken);
            await maintenanceLease.AssertConnectedAsync(cancellationToken);
            await ValidateSchemaAsync(connection, transaction, cancellationToken);
            await AcquireMaintenanceTableLocksAsync(connection, transaction, cancellationToken);
            _ = await TargetTables.LoadAsync(connection, transaction, graph, cancellationToken);
            var runtime = await InspectRuntimeAsync(connection, transaction, requireStoppedService: true,
                cancellationToken);
            ThrowIfBlocked(runtime.BlockingErrors);
            await VerifyImmutableCorrectedStateAsync(connection, transaction, cancellationToken);
            await VerifyDashboardAndHourlyStateAsync(connection, transaction, start.MaintenanceStartedAtUtc,
                allDateDependentStrategyIds, cancellationToken);
            var refreshedAt = await ScalarTimestampAsync(connection, transaction,
                "SELECT clock_timestamp();", cancellationToken);
            await using var command = new NpgsqlCommand(CorrectionSql.RefreshCopiedPerformanceSql,
                connection, transaction) { CommandTimeout = 0 };
            command.Parameters.AddWithValue("refreshed_at_utc", refreshedAt.UtcDateTime);
            var rows = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);
            return rows;
        }
        catch
        {
            await TryRollbackAsync(transaction, cancellationToken);
            throw;
        }
    }

    private static void CopySnapshotSet(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyList<SnapshotFile> snapshots)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var snapshot in snapshots)
        {
            File.Copy(Path.Combine(sourceDirectory, snapshot.FileName),
                Path.Combine(destinationDirectory, snapshot.FileName), overwrite: false);
        }
    }

    private void ValidateMaintenanceStart(
        MaintenanceStartEvidence start,
        CorrectionBackupManifest applyManifest)
    {
        if (start.SchemaVersion != 1 ||
            !start.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !start.State.Equals("started", StringComparison.Ordinal) ||
            !start.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            !start.ApplyTransactionId.Equals(applyManifest.TransactionId, StringComparison.Ordinal) ||
            start.ApplyMutationTimestampUtc != applyManifest.MutationTimestampUtc ||
            start.MaintenanceStartedAtUtc < applyManifest.MutationTimestampUtc ||
            start.MaintenanceStartedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidDataException("Maintenance start evidence does not match the exact committed apply.");
        }
    }

    private void ValidateMaintenanceComplete(
        MaintenanceEvidence complete,
        MaintenanceStartEvidence start,
        CorrectionBackupManifest applyManifest)
    {
        if (complete.SchemaVersion != 1 ||
            !complete.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !complete.State.Equals("complete", StringComparison.Ordinal) ||
            !complete.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            !complete.ApplyTransactionId.Equals(applyManifest.TransactionId, StringComparison.Ordinal) ||
            complete.ApplyMutationTimestampUtc != applyManifest.MutationTimestampUtc ||
            complete.MaintenanceStartedAtUtc != start.MaintenanceStartedAtUtc ||
            complete.MaintenanceCompletedAtUtc < complete.MaintenanceStartedAtUtc ||
            complete.MaintenanceCompletedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            complete.DashboardStrategyCount < 1 || complete.DashboardRecentFactCount < 0 ||
            complete.DashboardRecentRowCount != checked(complete.DashboardStrategyCount * 3) ||
            complete.DashboardEventsDiscarded < 0 || complete.HourlyRowsWritten < 1 ||
            complete.CopiedPerformanceRowsWritten < 1)
        {
            throw new InvalidDataException(
                "Maintenance completion evidence does not match the exact committed apply/start contract.");
        }
        CompareSnapshotSets(start.Preimage, complete.Preimage, "maintenance immutable preimage");
    }

    private static async Task<ChildAssignmentGateMetrics> VerifyChildAssignmentCycleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset maintenanceCompletedAtUtc,
        ChildRefreshAttestation attestation,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH cycle_active AS (
                SELECT assignment.*
                FROM public.strategy_child_parent_assignments assignment
                WHERE assignment.ended_at_utc IS NULL
                  AND assignment.updated_at_utc >= @maintenance_completed_at_utc
            ), invalid_cycle AS (
                SELECT assignment.id
                FROM cycle_active assignment
                LEFT JOIN public.strategies child ON child.id = assignment.child_strategy_id
                LEFT JOIN public.strategies parent ON parent.id = assignment.parent_strategy_id
                WHERE child.id IS NULL OR parent.id IS NULL
                   OR assignment.child_strategy_id = assignment.parent_strategy_id
                   OR assignment.lookback_hours NOT BETWEEN 1 AND 24
                   OR assignment.child_mode NOT IN ('Child','ChildProgress','ChildRoi','ChildProgressRoi')
                   OR assignment.asset_symbol <> upper(btrim(assignment.asset_symbol))
                   OR btrim(assignment.asset_symbol) = ''
                   OR assignment.parent_pnl_usd <= 0
                   OR assignment.assigned_at_utc > assignment.updated_at_utc
                   OR assignment.updated_at_utc > @refresh_completed_at_utc + interval '1 second'
            ), stale_affected_active AS (
                SELECT assignment.id
                FROM public.strategy_child_parent_assignments assignment
                WHERE assignment.ended_at_utc IS NULL
                  AND assignment.updated_at_utc < @maintenance_completed_at_utc
                  AND EXISTS (
                    SELECT 1 FROM correction_target_strategies target
                    WHERE target.id = assignment.child_strategy_id
                       OR target.id = assignment.parent_strategy_id)
            )
            SELECT (SELECT count(*)::integer FROM cycle_active),
                   (SELECT min(updated_at_utc) FROM cycle_active),
                   (SELECT max(updated_at_utc) FROM cycle_active),
                   ((SELECT count(*) FROM invalid_cycle) +
                    (SELECT count(*) FROM stale_affected_active))::integer;
            """, connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("maintenance_completed_at_utc", maintenanceCompletedAtUtc.UtcDateTime);
        command.Parameters.AddWithValue("refresh_completed_at_utc", attestation.RefreshCompletedAtUtc.UtcDateTime);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Child-assignment SQL gate returned no row.");
        }
        var rows = reader.GetInt32(0);
        var earliest = reader.IsDBNull(1) ? (DateTimeOffset?)null :
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
        var latest = reader.IsDBNull(2) ? (DateTimeOffset?)null :
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc));
        var invalid = reader.GetInt32(3);
        if (rows != attestation.ActiveParents || invalid != 0 ||
            (rows == 0 && (earliest is not null || latest is not null)) ||
            (rows != 0 && (earliest is null || latest is null)))
        {
            throw new InvalidOperationException(
                $"Child-assignment post-refresh gate failed: active rows since maintenance={rows:N0}, " +
                $"attested active parents={attestation.ActiveParents:N0}, invalid/stale affected={invalid:N0}.");
        }
        return new ChildAssignmentGateMetrics(rows, earliest, latest);
    }

    private sealed record ChildAssignmentGateMetrics(
        int CycleActiveRows,
        DateTimeOffset? EarliestUpdatedAtUtc,
        DateTimeOffset? LatestUpdatedAtUtc);

    private static async Task ConfigureTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool readOnly,
        CancellationToken cancellationToken)
    {
        var sql = readOnly
            ? "SET TRANSACTION READ ONLY; SET LOCAL TIME ZONE 'UTC'; SET LOCAL search_path TO pg_catalog, public;"
            : "SET TRANSACTION READ WRITE; SET LOCAL TIME ZONE 'UTC'; SET LOCAL search_path TO pg_catalog, public;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(@key);", connection, transaction);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        var acquired = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false;
        if (!acquired)
        {
            throw new InvalidOperationException("Another history-correction transaction holds the advisory lock.");
        }
    }

    private async Task<MaintenanceSessionLease> AcquireMaintenanceSessionLeaseAsync(
        CancellationToken cancellationToken)
    {
        var connection = DatabaseConnection.Create(options);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            await ConfigureTransactionAsync(connection, transaction, readOnly: false, cancellationToken);
            await DatabaseConnection.VerifyIdentityAsync(connection, transaction, options,
                expectedReadOnly: false, "serializable", cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@key);", connection, transaction);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            var acquired = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false;
            if (!acquired)
            {
                throw new InvalidOperationException(
                    "Another history-correction operation holds the maintenance session lease.");
            }

            await transaction.CommitAsync(cancellationToken);
            var lease = new MaintenanceSessionLease(connection, AdvisoryLockKey);
            await lease.AssertConnectedAsync(cancellationToken);
            return lease;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class MaintenanceSessionLease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        private bool disposed;

        public async Task AssertConnectedAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            const long mask = uint.MaxValue;
            var high = (key >> 32) & mask;
            var low = key & mask;
            await using var command = new NpgsqlCommand("""
                SELECT count(*) = 1
                FROM pg_catalog.pg_locks
                WHERE locktype = 'advisory'
                  AND pid = pg_backend_pid()
                  AND mode = 'ExclusiveLock'
                  AND granted
                  AND classid::bigint = @high
                  AND objid::bigint = @low
                  AND objsubid = 1;
                """, connection);
            command.Parameters.AddWithValue("high", high);
            command.Parameters.AddWithValue("low", low);
            var held = (bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false;
            if (!held)
            {
                throw new InvalidOperationException(
                    "The maintenance session lease was lost; derived-state work cannot continue.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            await connection.DisposeAsync();
        }
    }

    private static async Task AcquireTableLocksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            LOCK TABLE public.service_heartbeats, public.strategies, public.daily_reports,
                public.live_orders, public.paper_live_shadow_decisions,
                public.paper_live_shadow_discrepancies,
                public.dry_run_orders, public.paper_copied_leader_positions,
                public.polymarket_onchain_paper_signal_results,
                public.paper_copied_trader_performance_refresh_inflight,
                public.paper_copied_trader_performance_projection_control,
                public.dashboard_strategy_lifetime_projection_states,
                public.dashboard_strategy_recent_projection_states,
                public.dashboard_strategy_recent_projection_facts,
                public.dashboard_strategy_position_projection_facts,
                public.paper_copied_trader_performance,
                public.date_dependent_strategy_hourly_paper_pnl,
                public.strategy_child_parent_assignments
            IN SHARE MODE NOWAIT;

            LOCK TABLE public.strategy_market_paper_runs, public.signals,
                public.signal_rejections, public.paper_orders, public.paper_fills,
                public.paper_positions, public.paper_position_settlements,
                public.dashboard_projection_events,
                public.dashboard_projection_control,
                public.dashboard_projection_reconciliation_queue,
                public.paper_copied_trader_performance_refresh_queue
            IN SHARE ROW EXCLUSIVE MODE NOWAIT;
            """, connection, transaction) { CommandTimeout = 15 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireMaintenanceTableLocksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            LOCK TABLE public.service_heartbeats, public.strategies, public.daily_reports,
                public.live_orders, public.paper_live_shadow_decisions,
                public.paper_live_shadow_discrepancies, public.dry_run_orders,
                public.paper_copied_leader_positions, public.polymarket_onchain_paper_signal_results,
                public.polymarket_gamma_markets, public.strategy_child_parent_assignments,
                public.signals, public.signal_rejections, public.paper_orders, public.paper_fills,
                public.paper_positions, public.paper_position_settlements,
                public.strategy_market_paper_runs
            IN SHARE MODE NOWAIT;

            LOCK TABLE public.dashboard_projection_events, public.dashboard_projection_control,
                public.dashboard_projection_reconciliation_queue,
                public.dashboard_strategy_lifetime_projection_states,
                public.dashboard_strategy_recent_projection_states,
                public.dashboard_strategy_recent_projection_facts,
                public.dashboard_strategy_position_projection_facts,
                public.dashboard_strategy_performance_snapshots,
                public.dashboard_strategy_recent_performance_snapshots,
                public.date_dependent_strategy_hourly_paper_pnl,
                public.paper_copied_trader_performance,
                public.paper_copied_trader_performance_refresh_queue,
                public.paper_copied_trader_performance_refresh_inflight,
                public.paper_copied_trader_performance_projection_control
            IN SHARE ROW EXCLUSIVE MODE NOWAIT;
            """, connection, transaction) { CommandTimeout = 15 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ValidateSchemaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var tables = new NpgsqlCommand("""
            WITH required(name) AS (VALUES
                ('signals'), ('signal_rejections'), ('paper_orders'), ('paper_fills'),
                ('strategy_market_paper_runs'), ('paper_positions'), ('paper_position_settlements'),
                ('service_heartbeats'), ('daily_reports'), ('live_orders'),
                ('paper_live_shadow_decisions'), ('paper_live_shadow_discrepancies'),
                ('dashboard_projection_events'), ('dashboard_projection_control'),
                ('dashboard_projection_reconciliation_queue'),
                ('paper_copied_trader_performance_refresh_queue'),
                ('paper_copied_trader_performance_refresh_inflight'),
                ('paper_copied_trader_performance_projection_control'),
                ('strategies'), ('polymarket_gamma_markets'),
                ('strategy_child_parent_assignments'),
                ('dashboard_strategy_lifetime_projection_states'),
                ('dashboard_strategy_recent_projection_states'),
                ('dashboard_strategy_recent_projection_facts'),
                ('dashboard_strategy_position_projection_facts'),
                ('dashboard_strategy_performance_snapshots'),
                ('dashboard_strategy_recent_performance_snapshots'),
                ('date_dependent_strategy_hourly_paper_pnl'),
                ('paper_copied_trader_performance'))
            SELECT name FROM required WHERE to_regclass('public.' || name) IS NULL ORDER BY name;
            """, connection, transaction);
        var missing = new List<string>();
        await using (var data = await tables.ExecuteReaderAsync(cancellationToken))
        {
            while (await data.ReadAsync(cancellationToken))
            {
                missing.Add(data.GetString(0));
            }
        }

        if (missing.Count != 0)
        {
            throw new InvalidOperationException("Required database tables are missing: " + string.Join(", ", missing));
        }

        await using var triggers = new NpgsqlCommand("""
            WITH required(table_name, trigger_name) AS (VALUES
                ('paper_orders', 'trg_dashboard_projection_paper_order'),
                ('paper_fills', 'trg_dashboard_projection_paper_fill'),
                ('strategy_market_paper_runs', 'trg_dashboard_projection_strategy_run'),
                ('paper_positions', 'trg_dashboard_projection_paper_position'),
                ('paper_position_settlements', 'trg_dashboard_projection_paper_settlement'),
                ('paper_orders', 'trg_paper_copied_trader_performance_order'),
                ('paper_fills', 'trg_paper_copied_trader_performance_fill'),
                ('paper_position_settlements', 'trg_paper_copied_trader_performance_settlement'),
                ('paper_positions', 'trg_paper_copied_trader_performance_position_insert'),
                ('paper_positions', 'trg_paper_copied_trader_performance_position_update'),
                ('paper_positions', 'trg_paper_copied_trader_performance_position_delete'))
            SELECT required.table_name, required.trigger_name, trigger_row.tgenabled
            FROM required
            LEFT JOIN pg_class table_row ON table_row.oid = to_regclass('public.' || required.table_name)
            LEFT JOIN pg_trigger trigger_row
              ON trigger_row.tgrelid = table_row.oid
             AND trigger_row.tgname = required.trigger_name
             AND NOT trigger_row.tgisinternal
            WHERE trigger_row.oid IS NULL OR trigger_row.tgenabled <> 'O'
            ORDER BY required.table_name, required.trigger_name;
            """, connection, transaction);
        var invalid = new List<string>();
        await using (var data = await triggers.ExecuteReaderAsync(cancellationToken))
        {
            while (await data.ReadAsync(cancellationToken))
            {
                invalid.Add(data.GetString(0) + "." + data.GetString(1));
            }
        }

        if (invalid.Count != 0)
        {
            throw new InvalidOperationException("Required application triggers are missing or disabled: " +
                                                string.Join(", ", invalid));
        }

        await ValidateFreshSchemaContractAsync(connection, transaction, cancellationToken);
    }

    private async Task ValidateFreshSchemaContractAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var actualForeignKeys = new List<ForeignKeyContract>();
        await using (var command = new NpgsqlCommand("""
                         SELECT constraint_row.conname,
                                source_table.relname,
                                string_agg(source_column.attname, ',' ORDER BY source_key.ordinality),
                                target_table.relname,
                                string_agg(target_column.attname, ',' ORDER BY source_key.ordinality),
                                CASE constraint_row.confdeltype
                                  WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE'
                                  WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT' ELSE constraint_row.confdeltype::text END,
                                CASE constraint_row.confupdtype
                                  WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE'
                                  WHEN 'n' THEN 'SET NULL' WHEN 'd' THEN 'SET DEFAULT' ELSE constraint_row.confupdtype::text END
                         FROM pg_constraint constraint_row
                         JOIN pg_class source_table ON source_table.oid = constraint_row.conrelid
                         JOIN pg_namespace source_schema ON source_schema.oid = source_table.relnamespace
                         JOIN pg_class target_table ON target_table.oid = constraint_row.confrelid
                         JOIN pg_namespace target_schema ON target_schema.oid = target_table.relnamespace
                         JOIN unnest(constraint_row.conkey) WITH ORDINALITY source_key(attnum, ordinality) ON true
                         JOIN unnest(constraint_row.confkey) WITH ORDINALITY target_key(attnum, ordinality)
                           ON target_key.ordinality = source_key.ordinality
                         JOIN pg_attribute source_column ON source_column.attrelid = source_table.oid
                                                        AND source_column.attnum = source_key.attnum
                         JOIN pg_attribute target_column ON target_column.attrelid = target_table.oid
                                                        AND target_column.attnum = target_key.attnum
                         WHERE constraint_row.contype = 'f'
                           AND source_schema.nspname = 'public'
                           AND target_schema.nspname = 'public'
                           AND (source_table.relname = ANY(@tables) OR target_table.relname = ANY(@tables))
                         GROUP BY constraint_row.conname, source_table.relname, target_table.relname,
                                  constraint_row.confdeltype, constraint_row.confupdtype
                         ORDER BY source_table.relname, constraint_row.conname;
                         """, connection, transaction))
        {
            command.Parameters.AddWithValue("tables", NpgsqlDbType.Array | NpgsqlDbType.Text,
                SchemaGraphTables);
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                actualForeignKeys.Add(new ForeignKeyContract(
                    data.GetString(0), data.GetString(1), data.GetString(2), data.GetString(3), data.GetString(4),
                    data.GetString(5), data.GetString(6), true));
            }
        }

        if (!actualForeignKeys.OrderBy(item => item.SourceTable, StringComparer.Ordinal)
                .ThenBy(item => item.ConstraintName, StringComparer.Ordinal)
                .SequenceEqual(graph.ForeignKeys.OrderBy(item => item.SourceTable, StringComparer.Ordinal)
                    .ThenBy(item => item.ConstraintName, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Fresh public foreign-key/action set differs from the graph package; schema drift blocks mutation.");
        }

        var actualColumns = new List<SchemaReferenceColumnContract>();
        await using (var command = new NpgsqlCommand("""
                         SELECT table_name, column_name, data_type
                         FROM information_schema.columns
                         WHERE table_schema = 'public'
                           AND column_name = ANY(@column_names)
                         ORDER BY table_name, column_name;
                         """, connection, transaction))
        {
            command.Parameters.AddWithValue("column_names", NpgsqlDbType.Array | NpgsqlDbType.Text,
                new[] { "signal_id", "paper_order_id", "entry_signal_id", "entry_paper_order_id",
                    "live_order_id", "correlation_id", "source_id" });
            await using var data = await command.ExecuteReaderAsync(cancellationToken);
            while (await data.ReadAsync(cancellationToken))
            {
                actualColumns.Add(new SchemaReferenceColumnContract(
                    data.GetString(0), data.GetString(1), data.GetString(2), true));
            }
        }

        if (!actualColumns.SequenceEqual(graph.SchemaReferenceColumns
                .OrderBy(item => item.TableName, StringComparer.Ordinal)
                .ThenBy(item => item.ColumnName, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Fresh public reference-column set differs from the graph package; schema drift blocks mutation.");
        }
    }

    private static readonly string[] SchemaGraphTables =
    [
        "strategies", "leader_trades", "signals", "signal_rejections", "paper_orders", "paper_fills",
        "strategy_market_paper_runs", "strategy_child_parent_assignments", "dry_run_orders",
        "live_orders", "paper_live_shadow_decisions", "paper_live_shadow_discrepancies",
        "paper_positions", "paper_position_settlements", "paper_copied_leader_positions",
        "polymarket_onchain_paper_signal_results", "dashboard_projection_events",
        "dashboard_projection_reconciliation_queue", "dashboard_strategy_lifetime_projection_states",
        "dashboard_strategy_recent_projection_states", "dashboard_strategy_recent_projection_facts",
        "dashboard_strategy_position_projection_facts", "dashboard_strategy_performance_snapshots",
        "dashboard_strategy_recent_performance_snapshots", "date_dependent_strategy_hourly_paper_pnl",
        "paper_copied_trader_performance", "dashboard_projection_control",
        "paper_copied_trader_performance_refresh_queue",
        "paper_copied_trader_performance_refresh_inflight",
        "paper_copied_trader_performance_projection_control", "polymarket_gamma_markets"
    ];

    private async Task<DatabasePreflight> InspectRuntimeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool requireStoppedService,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var attestation = OperatorAttestationStore.Read(options, required: requireStoppedService);
        string? serviceStatus = null;
        DateTimeOffset? heartbeat = null;
        await using (var command = new NpgsqlCommand("""
                         SELECT status, last_heartbeat_utc
                         FROM public.service_heartbeats
                         WHERE service_name = 'PolyCopyTrader.Service';
                         """, connection, transaction))
        await using (var data = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await data.ReadAsync(cancellationToken))
            {
                serviceStatus = data.GetString(0);
                heartbeat = new DateTimeOffset(data.GetDateTime(1), TimeSpan.Zero);
            }
        }

        if (requireStoppedService)
        {
            if (heartbeat is null)
            {
                errors.Add("PolyCopyTrader.Service heartbeat row is absent; stopped-service state cannot be proven.");
            }
            else if (heartbeat > DateTimeOffset.UtcNow.AddMinutes(-options.HeartbeatStaleMinutes))
            {
                errors.Add(
                    $"PolyCopyTrader.Service heartbeat is not stale: {heartbeat:O}; required age is {options.HeartbeatStaleMinutes} minutes.");
            }
        }

        var otherPolySessions = await ScalarIntAsync(connection, transaction, """
            SELECT count(*)::integer FROM pg_stat_activity
            WHERE pid <> pg_backend_pid()
              AND datname = current_database()
              AND application_name ILIKE '%PolyCopyTrader%';
            """, cancellationToken);
        var activeSessions = await ScalarIntAsync(connection, transaction, """
            SELECT count(*)::integer FROM pg_stat_activity
            WHERE pid <> pg_backend_pid()
              AND datname = current_database()
              AND backend_type = 'client backend'
              AND state <> 'idle';
            """, cancellationToken);
        if (requireStoppedService && otherPolySessions != 0)
        {
            errors.Add($"Found {otherPolySessions:N0} other PolyCopyTrader database sessions.");
        }
        if (requireStoppedService && activeSessions != 0)
        {
            errors.Add($"Found {activeSessions:N0} other active database sessions.");
        }

        var liveOverlap = await CountLiveShadowOverlapsAsync(connection, transaction, cancellationToken);
        if (liveOverlap != 0)
        {
            errors.Add($"Fresh database check found {liveOverlap:N0} Live/shadow overlap rows.");
        }

        var unsupported = await CountUnsupportedDependenciesAsync(connection, transaction, cancellationToken);
        if (unsupported != 0)
        {
            errors.Add($"Fresh database check found {unsupported:N0} unsupported graph dependencies/collisions.");
        }

        var dailyReports = await ScalarLongAsync(connection, transaction,
            "SELECT count(*) FROM public.daily_reports;", cancellationToken);
        if (dailyReports != 0)
        {
            errors.Add($"daily_reports must be exactly empty; found {dailyReports:N0} rows.");
        }

        var dataDirectory = await ScalarStringAsync(connection, transaction,
            "SHOW data_directory;", cancellationToken);
        var footprint = await ReadScopedFootprintAsync(connection, transaction, cancellationToken);
        var scopedBytes = footprint.Sum(item => item.RowBytes);
        var globalProjectionBytes = await ScalarLongAsync(connection, transaction, """
            SELECT COALESCE(sum(pg_total_relation_size(to_regclass('public.' || table_name))), 0)::bigint
            FROM unnest(ARRAY[
                'dashboard_projection_events', 'dashboard_projection_reconciliation_queue',
                'dashboard_strategy_lifetime_projection_states',
                'dashboard_strategy_recent_projection_states',
                'dashboard_strategy_recent_projection_facts',
                'dashboard_strategy_position_projection_facts',
                'dashboard_strategy_performance_snapshots',
                'dashboard_strategy_recent_performance_snapshots',
                'date_dependent_strategy_hourly_paper_pnl',
                'paper_copied_trader_performance']) AS table_name;
            """, cancellationToken);
        var estimatedWalBytes = Math.Max(2L * 1024 * 1024 * 1024,
            SaturatingPolicyBytes(scopedBytes, 20, globalProjectionBytes, 3));
        var workingSpaceBytes = Math.Max(2L * 1024 * 1024 * 1024,
            SaturatingPolicyBytes(scopedBytes, 10, globalProjectionBytes, 2));
        var requiredFreeBytes = SaturatingAdd(estimatedWalBytes, workingSpaceBytes);
        if (attestation is not null)
        {
            if (!NormalizeDatabasePath(attestation.DataDirectory)
                    .Equals(NormalizeDatabasePath(dataDirectory), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Operator-attested data_directory '{attestation.DataDirectory}' differs from fresh PostgreSQL '{dataDirectory}'.");
            }
            if (attestation.FreeBytes < requiredFreeBytes)
            {
                errors.Add(
                    $"Operator-attested C: free bytes {attestation.FreeBytes:N0} are below the " +
                    $"conservative correction/bootstrap policy floor {requiredFreeBytes:N0}.");
            }
        }

        return new DatabasePreflight(options.Host, options.Port, options.Database,
            DatabaseConnection.RequiredSearchPath, "UTC",
            requireStoppedService ? "serializable" : "repeatable read", !requireStoppedService,
            heartbeat, serviceStatus, otherPolySessions, activeSessions, liveOverlap, unsupported,
            dailyReports, dataDirectory, footprint, globalProjectionBytes, estimatedWalBytes,
            requiredFreeBytes, attestation?.FreeBytes, errors);
    }

    private static async Task<IReadOnlyList<TableFootprint>> ReadScopedFootprintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT table_name, row_count, row_bytes
            FROM (
                SELECT 'signals'::text table_name, count(*)::bigint row_count,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint row_bytes
                FROM public.signals source JOIN correction_target_signals target ON target.id = source.id
                UNION ALL SELECT 'signal_rejections', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.signal_rejections source
                JOIN correction_target_signals target ON target.id = source.signal_id
                UNION ALL SELECT 'paper_orders', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_orders source JOIN correction_target_orders target ON target.id = source.id
                UNION ALL SELECT 'paper_fills', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_fills source
                JOIN correction_target_orders target ON target.id = source.paper_order_id
                UNION ALL SELECT 'strategy_market_paper_runs', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.strategy_market_paper_runs source JOIN correction_target_runs target ON target.id = source.id
                UNION ALL SELECT 'paper_positions', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_positions source JOIN correction_position_keys target
                  ON target.copied_trader_wallet = source.copied_trader_wallet AND target.asset_id = source.asset_id
                UNION ALL SELECT 'paper_position_settlements', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_position_settlements source JOIN correction_position_keys target
                  ON target.copied_trader_wallet = source.copied_trader_wallet AND target.asset_id = source.asset_id
                UNION ALL SELECT 'dashboard_projection_events', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.dashboard_projection_events source
                JOIN correction_target_strategies target ON target.id = source.strategy_id
                UNION ALL SELECT 'dashboard_projection_reconciliation_queue', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.dashboard_projection_reconciliation_queue source
                JOIN correction_target_strategies target ON target.id = source.strategy_id
                UNION ALL SELECT 'paper_copied_trader_performance_refresh_queue', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_copied_trader_performance_refresh_queue source
                JOIN correction_target_wallets target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                UNION ALL SELECT 'paper_copied_trader_performance', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.paper_copied_trader_performance source
                JOIN correction_target_wallets target
                  ON target.copied_trader_wallet = source.copied_trader_wallet
                UNION ALL SELECT 'dashboard_projection_control', count(*)::bigint,
                       COALESCE(sum(pg_column_size(source)), 0)::bigint
                FROM public.dashboard_projection_control source WHERE source.singleton_id = 1
            ) footprint
            ORDER BY table_name;
            """, connection, transaction) { CommandTimeout = 0 };
        var result = new List<TableFootprint>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TableFootprint(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }
        return result;
    }

    private static long SaturatingPolicyBytes(long first, int firstMultiplier, long second, int secondMultiplier)
    {
        var value = (decimal)first * firstMultiplier + (decimal)second * secondMultiplier;
        return value >= long.MaxValue ? long.MaxValue : decimal.ToInt64(decimal.Ceiling(value));
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private void VerifyFreshOperationFootprint(IReadOnlyList<TableFootprint> fresh)
    {
        var expected = graph.OperationFootprint
            .Where(row => row.ExactSnapshotMeasurement)
            .GroupBy(row => row.TableName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new TableFootprint(group.Key,
                    group.Sum(row => row.SnapshotRowCount ?? 0),
                    group.Sum(row => row.SnapshotPgColumnSizeBytes ?? 0)),
                StringComparer.Ordinal);
        var actual = fresh.ToDictionary(row => row.TableName, StringComparer.Ordinal);
        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var row) ||
                row.RowCount != pair.Value.RowCount || row.RowBytes != pair.Value.RowBytes)
            {
                throw new InvalidOperationException(
                    $"Fresh operation-footprint mismatch for {pair.Key}: graph rows/bytes=" +
                    $"{pair.Value.RowCount:N0}/{pair.Value.RowBytes:N0}, fresh=" +
                    (row is null ? "<missing>" : $"{row.RowCount:N0}/{row.RowBytes:N0}"));
            }
        }
    }

    private static string NormalizeDatabasePath(string value) =>
        value.Trim().TrimEnd('\\', '/');

    private static async Task<int> CountLiveShadowOverlapsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        return await ScalarIntAsync(connection, transaction, """
            WITH live_matches AS (
                SELECT live_order.id, live_order.correlation_id
                FROM public.live_orders live_order
                WHERE EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = live_order.paper_order_id)
                   OR EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = live_order.signal_id)
                   OR EXISTS (SELECT 1 FROM correction_target_correlations target WHERE target.id = live_order.correlation_id)
                   OR EXISTS (
                        SELECT 1 FROM correction_target_conditions target
                        WHERE target.strategy_id = live_order.strategy_id
                          AND target.condition_id = live_order.condition_id)
            ), shadow_matches AS (
                SELECT shadow.correlation_id
                FROM public.paper_live_shadow_decisions shadow
                WHERE EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = shadow.paper_order_id)
                   OR EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = shadow.signal_id)
                   OR EXISTS (SELECT 1 FROM live_matches target WHERE target.id = shadow.live_order_id)
                   OR EXISTS (SELECT 1 FROM correction_target_correlations target WHERE target.id = shadow.correlation_id)
                   OR EXISTS (
                        SELECT 1 FROM correction_target_conditions target
                        WHERE target.strategy_id = shadow.strategy_id
                          AND target.condition_id = shadow.condition_id)
            ), discrepancy_matches AS (
                SELECT discrepancy.id
                FROM public.paper_live_shadow_discrepancies discrepancy
                WHERE EXISTS (SELECT 1 FROM correction_target_correlations target WHERE target.id = discrepancy.correlation_id)
                   OR EXISTS (SELECT 1 FROM live_matches target WHERE target.correlation_id = discrepancy.correlation_id)
                   OR EXISTS (SELECT 1 FROM shadow_matches target WHERE target.correlation_id = discrepancy.correlation_id)
            )
            SELECT ((SELECT count(*) FROM live_matches) +
                    (SELECT count(*) FROM shadow_matches) +
                    (SELECT count(*) FROM discrepancy_matches))::integer;
            """, cancellationToken);
    }

    private static async Task<int> CountUnsupportedDependenciesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        return await ScalarIntAsync(connection, transaction, """
            SELECT (
                (SELECT count(*) FROM public.dry_run_orders row
                 WHERE EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = row.signal_id)) +
                (SELECT count(*) FROM public.paper_copied_leader_positions row
                 WHERE EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = row.entry_paper_order_id)
                    OR EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = row.entry_signal_id)) +
                (SELECT count(*) FROM public.polymarket_onchain_paper_signal_results row
                 WHERE EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = row.paper_order_id)
                    OR EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = row.signal_id)) +
                (SELECT count(*) FROM public.strategy_market_paper_runs row
                 WHERE (EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = row.paper_order_id)
                     OR EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = row.signal_id))
                   AND NOT EXISTS (SELECT 1 FROM correction_target_runs target WHERE target.id = row.id)) +
                (SELECT count(*) FROM public.paper_orders row
                 WHERE (EXISTS (SELECT 1 FROM correction_target_signals target WHERE target.id = row.signal_id)
                     OR EXISTS (SELECT 1 FROM correction_target_correlations target WHERE target.id = row.correlation_id))
                   AND NOT EXISTS (SELECT 1 FROM correction_target_orders target WHERE target.id = row.id)) +
                (SELECT count(*) FROM correction_adds target
                 JOIN public.paper_orders row
                   ON row.strategy_id = target.strategy_id AND row.condition_id = target.condition_id
                  AND row.id <> target.order_id) +
                (SELECT count(*) FROM correction_adds target
                 JOIN public.signals row
                   ON row.condition_id = target.condition_id
                  AND row.decision = target.strategy_code || '_entry'
                  AND row.id <> target.signal_id) +
                (SELECT count(*) FROM correction_adds target
                 JOIN public.paper_positions row
                   ON row.copied_trader_wallet = 'strategy:' || target.strategy_code
                  AND row.asset_id = target.selected_token_id
                  AND row.id <> target.position_id) +
                (SELECT count(*) FROM correction_adds target
                 JOIN public.paper_position_settlements row
                   ON row.copied_trader_wallet = 'strategy:' || target.strategy_code
                  AND row.asset_id = target.selected_token_id
                  AND row.id <> target.settlement_id) +
                (SELECT count(*) FROM public.paper_orders row
                 JOIN correction_position_keys target
                   ON target.copied_trader_wallet = row.copied_trader_wallet
                  AND target.asset_id = row.asset_id
                 WHERE NOT EXISTS (SELECT 1 FROM correction_target_orders expected WHERE expected.id = row.id))
            )::integer;
            """, cancellationToken);
    }

    private static async Task ValidateCurrentRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var mainMismatch = await ScalarIntAsync(connection, transaction, """
            SELECT count(*)::integer
            FROM correction_main_removals target
            LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
            LEFT JOIN public.paper_orders paper_order ON paper_order.id = target.order_id
            LEFT JOIN public.signals signal ON signal.id = target.signal_id
            WHERE strategy_run.id IS NULL OR paper_order.id IS NULL OR signal.id IS NULL
               OR strategy_run.strategy_id <> target.strategy_id OR strategy_run.market_id <> target.market_id
               OR strategy_run.status <> 'Settled'
               OR strategy_run.paper_order_id <> target.order_id OR strategy_run.signal_id <> target.signal_id
               OR paper_order.signal_id <> target.signal_id OR paper_order.strategy_id <> target.strategy_id
               OR paper_order.asset_id <> target.asset_id OR paper_order.outcome <> target.outcome
               OR paper_order.copied_trader_wallet <> target.copied_trader_wallet
               OR signal.asset_id <> target.asset_id OR signal.outcome <> target.outcome
               OR (paper_order.raw_decision_json ->> 'paper_lost_base_stake_usd')::numeric
                    IS DISTINCT FROM target.restored_base_stake_usd
               OR (paper_order.raw_decision_json ->> 'paper_lost_effective_stake_usd')::numeric
                    IS DISTINCT FROM target.historical_effective_stake_usd
               OR (paper_order.raw_decision_json ->> 'target_notional_usd')::numeric
                    IS DISTINCT FROM target.historical_target_notional_usd
               OR paper_order.raw_decision_json ->> 'stake_sizing_source'
                    IS DISTINCT FROM target.historical_stake_sizing_source
               OR encode(sha256(convert_to(paper_order.raw_decision_json::text, 'UTF8')), 'hex')
                    IS DISTINCT FROM target.stake_sizing_proof_sha256;
            """, cancellationToken);
        var childMismatch = await ScalarIntAsync(connection, transaction, """
            SELECT count(*)::integer
            FROM correction_child_removals target
            LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
            LEFT JOIN public.paper_orders paper_order ON paper_order.id = target.order_id
            LEFT JOIN public.signals signal ON signal.id = target.signal_id
            WHERE strategy_run.id IS NULL OR paper_order.id IS NULL OR signal.id IS NULL
               OR strategy_run.strategy_id <> target.strategy_id OR strategy_run.market_id <> target.market_id
               OR strategy_run.status <> 'Settled'
               OR strategy_run.paper_order_id <> target.order_id OR strategy_run.signal_id <> target.signal_id
               OR paper_order.signal_id <> target.signal_id OR paper_order.strategy_id <> target.strategy_id;
            """, cancellationToken);
        var addMismatch = await ScalarIntAsync(connection, transaction, """
            SELECT count(*)::integer
            FROM correction_adds target
            LEFT JOIN public.strategy_market_paper_runs strategy_run ON strategy_run.id = target.run_id
            LEFT JOIN public.strategies strategy ON strategy.id = target.strategy_id
            WHERE strategy_run.id IS NULL OR strategy.id IS NULL
               OR strategy_run.strategy_id <> target.strategy_id
               OR strategy.code <> target.strategy_code
               OR strategy_run.market_id <> target.market_id
               OR strategy_run.condition_id <> target.condition_id
               OR strategy_run.status <> 'Skipped'
               OR strategy_run.skip_reason <> 'optimized_average_required_window_not_selected'
               OR strategy_run.market_end_utc IS NULL
               OR strategy_run.updated_at_utc <> target.modeled_entry_at_utc
               OR target.modeled_settled_at_utc <>
                    GREATEST(strategy_run.market_end_utc, target.resolution_ledger_first_received_at_utc)
               OR strategy_run.skip_diagnostics_json ->> 'decision_source'
                    IS DISTINCT FROM 'reference_price_max_average_bps_premarket'
               OR (strategy_run.skip_diagnostics_json ->> 'target_notional_usd')::numeric
                    IS DISTINCT FROM target.historical_stake_multiplier
               OR CASE
                    WHEN (strategy_run.skip_diagnostics_json ? 'paper_lost_base_stake_usd') <>
                         (strategy_run.skip_diagnostics_json ? 'paper_lost_effective_stake_usd') THEN true
                    WHEN strategy_run.skip_diagnostics_json ? 'paper_lost_base_stake_usd' THEN
                         (strategy_run.skip_diagnostics_json ->> 'paper_lost_base_stake_usd')::numeric
                             IS DISTINCT FROM strategy_run.stake_usd
                         OR (strategy_run.skip_diagnostics_json ->> 'paper_lost_effective_stake_usd')::numeric
                             IS DISTINCT FROM target.historical_stake_multiplier
                    ELSE target.historical_stake_multiplier IS DISTINCT FROM strategy_run.stake_usd
                  END
               OR strategy_run.selected_asset_id IS NOT NULL OR strategy_run.selected_outcome IS NOT NULL
               OR strategy_run.entry_price IS NOT NULL OR strategy_run.size_shares IS NOT NULL
               OR strategy_run.signal_id IS NOT NULL OR strategy_run.paper_order_id IS NOT NULL
               OR strategy_run.entered_at_utc IS NOT NULL OR strategy_run.settlement_price IS NOT NULL
               OR strategy_run.settlement_value_usd IS NOT NULL OR strategy_run.realized_pnl_usd IS NOT NULL
               OR strategy_run.settled_at_utc IS NOT NULL;
            """, cancellationToken);
        var controlMismatch = await ScalarIntAsync(connection, transaction, """
            SELECT CASE WHEN count(*) = 1 AND bool_and(
                singleton_id = 1 AND initialized AND calculation_version = 2
                AND status = 'Running' AND last_error IS NULL)
                THEN 0 ELSE 1 END::integer
            FROM public.dashboard_projection_control;
            """, cancellationToken);
        if (mainMismatch != 0 || childMismatch != 0 || addMismatch != 0 || controlMismatch != 0)
        {
            throw new InvalidOperationException(
                $"Fresh graph state mismatch: main={mainMismatch:N0}, child={childMismatch:N0}, " +
                $"adds={addMismatch:N0}, dashboard_projection_control={controlMismatch:N0}.");
        }

        var addIdCollisions = await ScalarIntAsync(connection, transaction,
            CorrectionSql.AddIdCollisionSql, cancellationToken);
        if (addIdCollisions != 0)
        {
            throw new InvalidOperationException(
                $"Deterministic modeled-add destination ID collisions detected: {addIdCollisions:N0}.");
        }

        await VerifyFreshSourceHashesAsync(connection, transaction, cancellationToken);
    }

    private static Task VerifyFreshSourceHashesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken) =>
        SourceStateHashVerifier.VerifyAsync(connection, transaction, cancellationToken);

    private async Task ApplyMutationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.ApplySql, connection, transaction)
        {
            CommandTimeout = 0
        };
        command.Parameters.AddWithValue("graph_manifest_sha256", graph.Manifest.ManifestSha256);
        command.Parameters.AddWithValue("cutoff_utc", graph.Manifest.CutoffUtc.UtcDateTime);
        command.Parameters.AddWithValue("applied_at_utc", appliedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteCurrentScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.DeleteScopeSql, connection, transaction)
        {
            CommandTimeout = 0
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetProjectionTriggersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var action = enabled ? "ENABLE" : "DISABLE";
        var commands = new[]
        {
            $"ALTER TABLE public.paper_orders {action} TRIGGER trg_dashboard_projection_paper_order",
            $"ALTER TABLE public.paper_fills {action} TRIGGER trg_dashboard_projection_paper_fill",
            $"ALTER TABLE public.strategy_market_paper_runs {action} TRIGGER trg_dashboard_projection_strategy_run",
            $"ALTER TABLE public.paper_positions {action} TRIGGER trg_dashboard_projection_paper_position",
            $"ALTER TABLE public.paper_position_settlements {action} TRIGGER trg_dashboard_projection_paper_settlement",
            $"ALTER TABLE public.paper_orders {action} TRIGGER trg_paper_copied_trader_performance_order",
            $"ALTER TABLE public.paper_fills {action} TRIGGER trg_paper_copied_trader_performance_fill",
            $"ALTER TABLE public.paper_position_settlements {action} TRIGGER trg_paper_copied_trader_performance_settlement",
            $"ALTER TABLE public.paper_positions {action} TRIGGER trg_paper_copied_trader_performance_position_insert",
            $"ALTER TABLE public.paper_positions {action} TRIGGER trg_paper_copied_trader_performance_position_update",
            $"ALTER TABLE public.paper_positions {action} TRIGGER trg_paper_copied_trader_performance_position_delete"
        };
        foreach (var sql in commands)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task VerifyAppliedStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.AppliedVerificationSql,
            connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("graph_manifest_sha256", graph.Manifest.ManifestSha256);
        command.Parameters.AddWithValue("cutoff_utc", graph.Manifest.CutoffUtc.UtcDateTime);
        var mismatches = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (mismatches != 0)
        {
            throw new InvalidOperationException($"Applied-state verification found {mismatches:N0} mismatches.");
        }
    }

    private static async Task VerifyCurrentPostimageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorrectionBackupManifest manifest,
        string rollbackStaging,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(rollbackStaging, "postimage-verification");
        Directory.CreateDirectory(directory);
        try
        {
            var current = await BackupStore.SnapshotAsync(connection, transaction, directory, "postimage",
                cancellationToken);
            CompareSnapshotSets(manifest.Postimage, current, "current postimage");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }

    private async Task VerifyImmutableCorrectedStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.ImmutableAppliedVerificationSql,
            connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("graph_manifest_sha256", graph.Manifest.ManifestSha256);
        command.Parameters.AddWithValue("cutoff_utc", graph.Manifest.CutoffUtc.UtcDateTime);
        var mismatches = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (mismatches != 0)
        {
            throw new InvalidOperationException(
                $"Reconciled rollback immutable corrected-base gate found {mismatches:N0} mismatches.");
        }
    }

    private static async Task VerifyDashboardAndHourlyStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset maintenanceStartedAt,
        IReadOnlyCollection<Guid> allDateDependentStrategyIds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH expected_hourly_strategies AS (
                SELECT unnest(@date_dependent_strategy_ids::uuid[]) AS strategy_id
            ), strategy_total AS (
                SELECT count(*)::integer AS value FROM public.strategies
            ), expected_hours AS (
                SELECT strategy_id, generate_series(0, 23)::integer AS hour_utc
                FROM expected_hourly_strategies
            )
            SELECT (
                (SELECT CASE WHEN count(*) = 1 AND bool_and(
                    singleton_id = 1 AND initialized AND calculation_version = 2
                    AND status = 'Running' AND reconciliation_cursor_strategy_id IS NULL
                    AND bootstrap_started_at_utc >= @maintenance_started_at_utc
                    AND bootstrap_completed_at_utc >= @maintenance_started_at_utc
                    AND bootstrap_completed_at_utc >= bootstrap_started_at_utc
                    AND last_error IS NULL) THEN 0 ELSE 1 END
                 FROM public.dashboard_projection_control) +
                (SELECT count(*) FROM public.dashboard_projection_events) +
                (SELECT count(*) FROM public.dashboard_projection_reconciliation_queue) +
                (SELECT abs(count(*) - (SELECT value FROM strategy_total))
                 FROM public.dashboard_strategy_lifetime_projection_states) +
                (SELECT count(*) FROM public.dashboard_strategy_lifetime_projection_states
                 WHERE projection_version <> 1 OR last_reconciled_at_utc < @maintenance_started_at_utc
                    OR updated_at_utc < @maintenance_started_at_utc) +
                (SELECT abs(count(*) - (SELECT value * 3 FROM strategy_total))
                 FROM public.dashboard_strategy_recent_projection_states) +
                (SELECT count(*) FROM public.dashboard_strategy_recent_projection_states
                 WHERE window_hours NOT IN (1, 6, 24) OR projection_version <> 1
                    OR last_reconciled_at_utc < @maintenance_started_at_utc
                    OR updated_at_utc < @maintenance_started_at_utc) +
                (SELECT abs(count(*) - (SELECT value FROM strategy_total))
                 FROM public.dashboard_strategy_performance_snapshots) +
                (SELECT count(*) FROM public.dashboard_strategy_performance_snapshots
                 WHERE refreshed_at_utc < @maintenance_started_at_utc) +
                (SELECT abs(count(*) - (SELECT value * 3 FROM strategy_total))
                 FROM public.dashboard_strategy_recent_performance_snapshots) +
                (SELECT count(*) FROM public.dashboard_strategy_recent_performance_snapshots
                 WHERE window_hours NOT IN (1, 6, 24)
                    OR refreshed_at_utc < @maintenance_started_at_utc) +
                (SELECT count(*)
                 FROM public.dashboard_strategy_recent_projection_facts fact
                 WHERE NOT EXISTS (SELECT 1 FROM public.strategies strategy WHERE strategy.id = fact.strategy_id)
                    OR CASE fact.source_kind
                        WHEN 'PaperOrder' THEN NOT EXISTS (
                            SELECT 1 FROM public.paper_orders source
                            WHERE source.id = fact.source_id AND source.strategy_id = fact.strategy_id)
                        WHEN 'PaperFill' THEN NOT EXISTS (
                            SELECT 1 FROM public.paper_fills source
                            JOIN public.paper_orders paper_order ON paper_order.id = source.paper_order_id
                            WHERE source.id = fact.source_id AND paper_order.strategy_id = fact.strategy_id)
                        WHEN 'StrategyRun' THEN NOT EXISTS (
                            SELECT 1 FROM public.strategy_market_paper_runs source
                            WHERE source.id = fact.source_id AND source.strategy_id = fact.strategy_id)
                        WHEN 'LiveOrder' THEN NOT EXISTS (
                            SELECT 1 FROM public.live_orders source
                            WHERE source.id = fact.source_id AND source.strategy_id = fact.strategy_id)
                        ELSE true
                       END) +
                (SELECT count(*)
                 FROM public.dashboard_strategy_position_projection_facts fact
                 WHERE NOT EXISTS (
                    SELECT 1
                    FROM public.paper_positions position
                    JOIN public.strategies strategy
                      ON position.copied_trader_wallet = 'strategy:' || strategy.code
                    WHERE position.id = fact.source_id AND strategy.id = fact.strategy_id)) +
                (SELECT abs(count(*) - (SELECT count(*) * 24 FROM expected_hourly_strategies))
                 FROM public.date_dependent_strategy_hourly_paper_pnl) +
                (SELECT count(*) FROM expected_hours expected
                 WHERE NOT EXISTS (
                    SELECT 1 FROM public.date_dependent_strategy_hourly_paper_pnl actual
                    WHERE actual.strategy_id = expected.strategy_id
                      AND actual.hour_utc = expected.hour_utc
                      AND actual.refreshed_at_utc >= @maintenance_started_at_utc)) +
                (SELECT count(*) FROM public.date_dependent_strategy_hourly_paper_pnl actual
                 WHERE NOT EXISTS (
                    SELECT 1 FROM expected_hourly_strategies expected
                    WHERE expected.strategy_id = actual.strategy_id))
            )::integer;
            """, connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("date_dependent_strategy_ids",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid, allDateDependentStrategyIds.ToArray());
        command.Parameters.AddWithValue("maintenance_started_at_utc", maintenanceStartedAt.UtcDateTime);
        var mismatches = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (mismatches != 0)
        {
            throw new InvalidOperationException(
                $"Stopped-service dashboard/hourly maintenance gate found {mismatches:N0} mismatches.");
        }
    }

    private static async Task VerifyMaintenanceDerivedStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset maintenanceStartedAt,
        IReadOnlyCollection<Guid> allDateDependentStrategyIds,
        CancellationToken cancellationToken)
    {
        await VerifyDashboardAndHourlyStateAsync(connection, transaction, maintenanceStartedAt,
            allDateDependentStrategyIds, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT (
                (SELECT count(*) FROM public.paper_copied_trader_performance_refresh_queue queue
                 JOIN correction_target_wallets target
                   ON target.copied_trader_wallet = queue.copied_trader_wallet) +
                (SELECT count(*) FROM public.paper_copied_trader_performance_refresh_inflight inflight
                 JOIN correction_target_wallets target
                   ON target.copied_trader_wallet = inflight.copied_trader_wallet) +
                (SELECT count(*) FROM correction_target_wallets target
                 WHERE NOT EXISTS (
                    SELECT 1 FROM public.paper_copied_trader_performance performance
                    WHERE performance.copied_trader_wallet = target.copied_trader_wallet
                      AND performance.category = 'OVERALL'
                      AND performance.refreshed_at_utc >= @maintenance_started_at_utc)) +
                (SELECT count(*) FROM public.paper_copied_trader_performance performance
                 JOIN correction_target_wallets target
                   ON target.copied_trader_wallet = performance.copied_trader_wallet
                 WHERE performance.refreshed_at_utc < @maintenance_started_at_utc)
            )::integer;
            """, connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("maintenance_started_at_utc", maintenanceStartedAt.UtcDateTime);
        var mismatches = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (mismatches != 0)
        {
            throw new InvalidOperationException(
                $"Stopped-service copied-performance maintenance gate found {mismatches:N0} mismatches.");
        }
    }

    private static async Task ProveZeroNewAffectedDecisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset mutationTimestamp,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.ZeroNewAffectedDecisionsSql,
            connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("mutation_timestamp_utc", mutationTimestamp.UtcDateTime);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        if (count != 0)
        {
            throw new InvalidOperationException(
                $"Reconciled rollback is unsafe: {count:N0} new affected Main/Child/Paper/Live decisions or updates exist since apply.");
        }
    }

    private static async Task EnqueueReconciliationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset requestedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CorrectionSql.EnqueueReconciliationSql,
            connection, transaction) { CommandTimeout = 0 };
        command.Parameters.AddWithValue("applied_at_utc", requestedAt.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void PersistReconciledRollbackPreimage(
        string durableRoot,
        IReadOnlyList<SnapshotFile> snapshot,
        string staging,
        CorrectionBackupManifest applyManifest)
    {
        var directory = Path.Combine(durableRoot,
            "rollback-reconciled-preimage-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var item in snapshot)
        {
            var source = Path.Combine(staging, item.FileName);
            var target = Path.Combine(directory, item.FileName);
            File.Copy(source, target, overwrite: false);
            if (!GraphPackageReader.Sha256File(target).Equals(item.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Reconciled rollback durable preimage copy mismatch: {item.TableName}.");
            }
        }

        var evidence = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            operation = "rollback_reconciled_preimage",
            graph_manifest_sha256 = applyManifest.GraphManifestSha256,
            apply_mutation_timestamp_utc = applyManifest.MutationTimestampUtc,
            captured_at_utc = DateTimeOffset.UtcNow,
            files = snapshot
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }) + Environment.NewLine;
        File.WriteAllText(Path.Combine(directory, "manifest.json"), evidence, new System.Text.UTF8Encoding(false));
    }

    private static async Task VerifyRestoredBasePreimageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorrectionBackupManifest manifest,
        string rollbackStaging,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(rollbackStaging, "reconciled-restored-base-verification");
        Directory.CreateDirectory(directory);
        try
        {
            var current = await BackupStore.SnapshotAsync(connection, transaction, directory, "preimage",
                cancellationToken);
            CompareSnapshotSets(manifest.Preimage.Where(item => BaseSnapshotTables.Contains(item.TableName)).ToArray(),
                current.Where(item => BaseSnapshotTables.Contains(item.TableName)).ToArray(),
                "reconciled restored base preimage");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }

    private static async Task VerifyRestoredPreimageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CorrectionBackupManifest manifest,
        string rollbackStaging,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(rollbackStaging, "preimage-verification");
        Directory.CreateDirectory(directory);
        try
        {
            var current = await BackupStore.SnapshotAsync(connection, transaction, directory, "preimage",
                cancellationToken);
            CompareSnapshotSets(manifest.Preimage, current, "restored preimage");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                File.Delete(file);
            }
            Directory.Delete(directory);
        }
    }

    private CorrectionBackupManifest BuildBackupManifest(
        string state,
        DateTimeOffset mutationTimestamp,
        string transactionId,
        IReadOnlyList<SnapshotFile> preimage,
        IReadOnlyList<SnapshotFile> postimage,
        LoadedTargets loaded,
        PreparedPackageManifest prepared)
    {
        return new CorrectionBackupManifest(
            1,
            "reference-average-history-correction-apply",
            "apply",
            state,
            graph.Manifest.ManifestSha256,
            graph.Manifest.CutoffUtc,
            options.Host,
            options.Port,
            options.Database,
            DatabaseConnection.RequiredSearchPath,
            DateTimeOffset.UtcNow,
            mutationTimestamp,
            transactionId,
            preimage,
            postimage,
            loaded.AddEntityIds.OrderBy(pair => pair.Key).ToDictionary(
                pair => pair.Key.ToString("D"),
                pair => JsonSerializer.Serialize(pair.Value),
                StringComparer.Ordinal),
            graph.RunIds.Order().ToArray(),
            graph.StrategyIds.Order().ToArray(),
            graph.Wallets.Order(StringComparer.Ordinal).ToArray(),
            prepared.FullBackupHashManifestSha256,
            prepared.FullBackupMetadataManifestSha256,
            prepared.FullBackupRestoreEvidenceSha256,
            prepared.FullBackupRestoredRowCountManifestSha256,
            prepared.FullBackupSchemaManifestSha256,
            prepared.FullBackupSchemaFingerprintSha256);
    }

    private async Task FinalizeDurableApplyEvidenceAsync(
        string durableRoot,
        CorrectionBackupManifest readyManifest,
        CancellationToken cancellationToken)
    {
        var immutableReadyPath = Path.Combine(durableRoot, "commit-ready-manifest.json");
        var immutableReady = BackupStore.ReadManifest(immutableReadyPath);
        ValidateReadyManifestEquivalent(readyManifest with { State = "commit_ready" }, immutableReady);
        var readySha256 = GraphPackageReader.Sha256File(immutableReadyPath);
        var markerPath = Path.Combine(durableRoot, "apply-committed.json");
        if (File.Exists(markerPath))
        {
            ValidateApplyCommitMarker(markerPath, immutableReady, readySha256);
        }
        else
        {
            var marker = new ApplyCommitMarker(
                1,
                "reference-average-history-correction-apply",
                graph.Manifest.ManifestSha256,
                readyManifest.TransactionId,
                readySha256,
                DateTimeOffset.UtcNow);
            await BackupStore.WriteJsonNewAsync(markerPath, marker, cancellationToken);
            ValidateApplyCommitMarker(markerPath, immutableReady, readySha256);
        }

        await BackupStore.WriteManifestAsync(Path.Combine(durableRoot, "backup-manifest.json"),
            immutableReady with { State = "applied" }, cancellationToken);
    }

    private static void ValidateReadyManifestEquivalent(
        CorrectionBackupManifest expected,
        CorrectionBackupManifest actual)
    {
        if (actual.SchemaVersion != expected.SchemaVersion || actual.Tool != expected.Tool ||
            actual.Operation != expected.Operation || actual.State != "commit_ready" ||
            actual.GraphManifestSha256 != expected.GraphManifestSha256 || actual.CutoffUtc != expected.CutoffUtc ||
            actual.Host != expected.Host || actual.Port != expected.Port || actual.Database != expected.Database ||
            actual.SearchPath != expected.SearchPath || actual.MutationTimestampUtc != expected.MutationTimestampUtc ||
            actual.TransactionId != expected.TransactionId ||
            actual.FullBackupHashManifestSha256 != expected.FullBackupHashManifestSha256 ||
            actual.FullBackupMetadataManifestSha256 != expected.FullBackupMetadataManifestSha256 ||
            actual.FullBackupRestoreEvidenceSha256 != expected.FullBackupRestoreEvidenceSha256 ||
            actual.FullBackupRestoredRowCountManifestSha256 != expected.FullBackupRestoredRowCountManifestSha256 ||
            actual.FullBackupSchemaManifestSha256 != expected.FullBackupSchemaManifestSha256 ||
            actual.FullBackupSchemaFingerprintSha256 != expected.FullBackupSchemaFingerprintSha256 ||
            !actual.RunIds.Order().SequenceEqual(expected.RunIds.Order()) ||
            !actual.StrategyIds.Order().SequenceEqual(expected.StrategyIds.Order()) ||
            !actual.Wallets.Order(StringComparer.Ordinal).SequenceEqual(expected.Wallets.Order(StringComparer.Ordinal)) ||
            !DictionaryEqual(actual.DeterministicIds, expected.DeterministicIds))
        {
            throw new InvalidDataException("Immutable commit-ready manifest identity differs from apply evidence.");
        }

        CompareSnapshotSets(expected.Preimage, actual.Preimage, "immutable commit-ready preimage");
        CompareSnapshotSets(expected.Postimage, actual.Postimage, "immutable commit-ready postimage");
    }

    private static bool DictionaryEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value, StringComparison.Ordinal));

    private static void ValidateApplyCommitMarker(
        string markerPath,
        CorrectionBackupManifest readyManifest,
        string readySha256)
    {
        var marker = JsonSerializer.Deserialize<ApplyCommitMarker>(File.ReadAllBytes(markerPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }) ??
                     throw new InvalidDataException("Apply commit marker is invalid.");
        if (marker.SchemaVersion != 1 ||
            !marker.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !marker.GraphManifestSha256.Equals(readyManifest.GraphManifestSha256, StringComparison.Ordinal) ||
            !marker.TransactionId.Equals(readyManifest.TransactionId, StringComparison.Ordinal) ||
            !marker.CommitReadyManifestSha256.Equals(readySha256, StringComparison.Ordinal) ||
            marker.CommittedAtUtc == default || marker.CommittedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidDataException("Apply commit marker does not match immutable commit-ready evidence.");
        }
    }

    private static async Task VerifyCommittedTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string transactionId,
        CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(transactionId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException("Apply transaction ID is not a valid xid8 value.");
        }

        await using var command = new NpgsqlCommand(
            "SELECT pg_xact_status(CAST(@transaction_id AS xid8));", connection, transaction);
        command.Parameters.AddWithValue("transaction_id", transactionId);
        var status = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (!string.Equals(status, "committed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Apply transaction {transactionId} is not confirmed committed (pg_xact_status={status ?? "<null>"}).");
        }
    }

    private void ValidateFinalizeManifest(CorrectionBackupManifest manifest, string manifestPath)
    {
        if (manifest.SchemaVersion != 1 ||
            !manifest.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !manifest.Operation.Equals("apply", StringComparison.Ordinal) ||
            manifest.State is not ("commit_ready" or "applied") ||
            !manifest.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            manifest.CutoffUtc != options.CutoffUtc ||
            !manifest.Host.Equals(options.Host, StringComparison.Ordinal) || manifest.Port != options.Port ||
            !manifest.Database.Equals(options.Database, StringComparison.Ordinal) ||
            !manifest.SearchPath.Equals(DatabaseConnection.RequiredSearchPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Finalize manifest identity does not match the exact graph/database contract.");
        }
        ValidateManifestGraphScope(manifest);

        var immutablePath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "commit-ready-manifest.json");
        var immutable = BackupStore.ReadManifest(immutablePath);
        ValidateReadyManifestEquivalent(manifest with { State = "commit_ready" }, immutable);
    }

    private void ValidateRollbackManifest(CorrectionBackupManifest manifest, string manifestPath)
    {
        if (manifest.SchemaVersion != 1 ||
            !manifest.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !manifest.Operation.Equals("apply", StringComparison.Ordinal) ||
            !manifest.State.Equals("applied", StringComparison.Ordinal) ||
            !manifest.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            manifest.CutoffUtc != options.CutoffUtc ||
            manifest.MutationTimestampUtc == default ||
            manifest.MutationTimestampUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            !manifest.Host.Equals(options.Host, StringComparison.Ordinal) || manifest.Port != options.Port ||
            !manifest.Database.Equals(options.Database, StringComparison.Ordinal) ||
            !manifest.SearchPath.Equals(DatabaseConnection.RequiredSearchPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rollback manifest identity does not match the exact graph/database contract.");
        }
        ValidateManifestGraphScope(manifest);

        var root = Path.GetDirectoryName(manifestPath)!;
        if (File.Exists(Path.Combine(root, "rollback-commit-ready.json")))
        {
            throw new InvalidDataException(
                "A rollback commit-ready record already exists; blind rollback retry is forbidden. Use --finalize-rollback.");
        }
        var readyPath = Path.Combine(root, "commit-ready-manifest.json");
        var ready = BackupStore.ReadManifest(readyPath);
        ValidateReadyManifestEquivalent(manifest with { State = "commit_ready" }, ready);
        ValidateApplyCommitMarker(Path.Combine(root, "apply-committed.json"), ready,
            GraphPackageReader.Sha256File(readyPath));
    }

    private void ValidateFinalizeRollbackApplyManifest(
        CorrectionBackupManifest manifest,
        string manifestPath)
    {
        if (manifest.SchemaVersion != 1 ||
            !manifest.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !manifest.Operation.Equals("apply", StringComparison.Ordinal) ||
            manifest.State is not ("applied" or "rolled_back") ||
            !manifest.GraphManifestSha256.Equals(graph.Manifest.ManifestSha256, StringComparison.Ordinal) ||
            manifest.CutoffUtc != options.CutoffUtc ||
            !manifest.Host.Equals(options.Host, StringComparison.Ordinal) || manifest.Port != options.Port ||
            !manifest.Database.Equals(options.Database, StringComparison.Ordinal) ||
            !manifest.SearchPath.Equals(DatabaseConnection.RequiredSearchPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Rollback finalization manifest identity does not match the exact graph/database contract.");
        }
        ValidateManifestGraphScope(manifest);

        var root = Path.GetDirectoryName(manifestPath)!;
        var readyPath = Path.Combine(root, "commit-ready-manifest.json");
        var applyReady = BackupStore.ReadManifest(readyPath);
        ValidateReadyManifestEquivalent(manifest with { State = "commit_ready" }, applyReady);
        ValidateApplyCommitMarker(Path.Combine(root, "apply-committed.json"), applyReady,
            GraphPackageReader.Sha256File(readyPath));
    }

    private void ValidateManifestGraphScope(CorrectionBackupManifest manifest)
    {
        if (manifest.RunIds.Count != graph.RunIds.Count ||
            !manifest.RunIds.ToHashSet().SetEquals(graph.RunIds) ||
            manifest.StrategyIds.Count != graph.StrategyIds.Count ||
            !manifest.StrategyIds.ToHashSet().SetEquals(graph.StrategyIds) ||
            manifest.Wallets.Count != graph.Wallets.Count ||
            !manifest.Wallets.ToHashSet(StringComparer.Ordinal).SetEquals(graph.Wallets))
        {
            throw new InvalidDataException(
                "Backup manifest run/strategy/wallet scope differs from the exact graph scope.");
        }

        var expectedIds = graph.Adds.OrderBy(add => add.RunId).ToDictionary(
            add => add.RunId.ToString("D"),
            add => JsonSerializer.Serialize(new EntityIds(
                DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "signal"),
                DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_order"),
                DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_fill"),
                DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId, "paper_position"),
                DeterministicGuid.Create(graph.Manifest.ManifestSha256, add.RunId,
                    "paper_position_settlement"))),
            StringComparer.Ordinal);
        if (!DictionaryEqual(manifest.DeterministicIds, expectedIds))
        {
            throw new InvalidDataException(
                "Backup manifest deterministic modeled-add IDs differ from the exact graph-derived IDs.");
        }
    }

    private static void ValidateRollbackReadyManifest(
        RollbackCommitManifest ready,
        CorrectionBackupManifest applyManifest)
    {
        if (ready.SchemaVersion != 1 ||
            !ready.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !ready.State.Equals("commit_ready", StringComparison.Ordinal) ||
            ready.RollbackMode is not ("immediate_exact_postimage" or "reconciled_zero_new_decisions") ||
            !ready.GraphManifestSha256.Equals(applyManifest.GraphManifestSha256, StringComparison.Ordinal) ||
            !ready.ApplyTransactionId.Equals(applyManifest.TransactionId, StringComparison.Ordinal) ||
            ready.CreatedAtUtc == default || ready.CreatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            ready.PostRollbackImage.Count == 0 ||
            ready.RequiresPostRestartHourlyAndChildRefreshVerification !=
            ready.RollbackMode.Equals("reconciled_zero_new_decisions", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rollback commit-ready manifest is invalid or belongs to another apply.");
        }
        if (!ulong.TryParse(ready.RollbackTransactionId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidDataException("Rollback commit-ready transaction ID is invalid.");
        }
    }

    private async Task FinalizeDurableRollbackEvidenceAsync(
        string durableRoot,
        CorrectionBackupManifest applyManifest,
        RollbackCommitManifest expectedReady,
        CancellationToken cancellationToken)
    {
        var readyPath = Path.Combine(durableRoot, "rollback-commit-ready.json");
        var ready = BackupStore.ReadJson<RollbackCommitManifest>(readyPath, "Rollback commit-ready manifest");
        ValidateRollbackReadyManifest(ready, applyManifest);
        if (ready.SchemaVersion != expectedReady.SchemaVersion || ready.Tool != expectedReady.Tool ||
            ready.State != expectedReady.State || ready.RollbackMode != expectedReady.RollbackMode ||
            ready.GraphManifestSha256 != expectedReady.GraphManifestSha256 ||
            ready.ApplyTransactionId != expectedReady.ApplyTransactionId ||
            ready.RollbackTransactionId != expectedReady.RollbackTransactionId ||
            ready.CreatedAtUtc != expectedReady.CreatedAtUtc ||
            ready.RequiresPostRestartHourlyAndChildRefreshVerification !=
            expectedReady.RequiresPostRestartHourlyAndChildRefreshVerification)
        {
            throw new InvalidDataException("Rollback commit-ready evidence changed after database verification.");
        }
        CompareSnapshotSets(expectedReady.PostRollbackImage, ready.PostRollbackImage,
            "rollback commit-ready immutable postimage");
        BackupStore.ValidateSnapshotSet(Path.Combine(durableRoot, "scoped"), ready.PostRollbackImage);
        var readySha256 = GraphPackageReader.Sha256File(readyPath);
        var markerPath = Path.Combine(durableRoot, "rollback-committed.json");
        if (File.Exists(markerPath))
        {
            ValidateRollbackCommitMarker(markerPath, ready, readySha256);
        }
        else
        {
            var marker = new RollbackCommitMarker(
                1,
                "reference-average-history-correction-apply",
                ready.GraphManifestSha256,
                ready.RollbackMode,
                ready.RollbackTransactionId,
                readySha256,
                DateTimeOffset.UtcNow);
            await BackupStore.WriteJsonNewAsync(markerPath, marker, cancellationToken);
            ValidateRollbackCommitMarker(markerPath, ready, readySha256);
        }

        await BackupStore.WriteManifestAsync(Path.Combine(durableRoot, "backup-manifest.json"),
            applyManifest with { State = "rolled_back" }, cancellationToken);
    }

    private static void ValidateRollbackCommitMarker(
        string markerPath,
        RollbackCommitManifest ready,
        string readySha256)
    {
        var marker = BackupStore.ReadJson<RollbackCommitMarker>(markerPath, "Rollback commit marker");
        if (marker.SchemaVersion != 1 ||
            !marker.Tool.Equals("reference-average-history-correction-apply", StringComparison.Ordinal) ||
            !marker.GraphManifestSha256.Equals(ready.GraphManifestSha256, StringComparison.Ordinal) ||
            !marker.RollbackMode.Equals(ready.RollbackMode, StringComparison.Ordinal) ||
            !marker.RollbackTransactionId.Equals(ready.RollbackTransactionId, StringComparison.Ordinal) ||
            !marker.RollbackCommitReadyManifestSha256.Equals(readySha256, StringComparison.Ordinal) ||
            marker.CommittedAtUtc == default || marker.CommittedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new InvalidDataException("Rollback commit marker does not match immutable commit-ready evidence.");
        }
    }

    private static void ValidateDurablePreimage(string durableRoot, CorrectionBackupManifest manifest)
    {
        var scoped = Path.Combine(durableRoot, "scoped");
        BackupStore.ValidateSnapshotSet(scoped, manifest.Preimage);
        var copiedManifest = Path.Combine(scoped, "backup-manifest.json");
        if (!File.Exists(copiedManifest))
        {
            throw new InvalidDataException("Durable scoped backup manifest copy is missing.");
        }
    }

    private static void CopyPostimageToDurable(
        string scopedStaging,
        string durableRoot,
        IReadOnlyList<SnapshotFile> postimage)
    {
        var scopedDurable = Path.Combine(durableRoot, "scoped");
        foreach (var evidence in postimage)
        {
            File.Copy(Path.Combine(scopedStaging, evidence.FileName),
                Path.Combine(scopedDurable, evidence.FileName), overwrite: false);
        }
        BackupStore.ValidateSnapshotSet(scopedDurable, postimage);
    }

    private static void CompareSnapshotSets(
        IReadOnlyList<SnapshotFile> expected,
        IReadOnlyList<SnapshotFile> actual,
        string label)
    {
        var actualByTable = actual.ToDictionary(item => item.TableName, StringComparer.Ordinal);
        foreach (var item in expected)
        {
            if (!actualByTable.TryGetValue(item.TableName, out var candidate) ||
                item.RowCount != candidate.RowCount || !item.Sha256.Equals(candidate.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} hash/count mismatch for {item.TableName}.");
            }
        }
    }

    private static void PrepareEmptyDirectory(string path, string label)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{label} directory must be unique and empty: {path}.");
        }
        Directory.CreateDirectory(path);
    }

    private static void ThrowIfBlocked(IReadOnlyList<string> errors)
    {
        if (errors.Count != 0)
        {
            throw new InvalidOperationException("Runtime preflight blocked mutation: " + string.Join(" | ", errors));
        }
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToInt32(await ScalarAsync(connection, transaction, sql, cancellationToken), CultureInfo.InvariantCulture);

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, sql, cancellationToken), CultureInfo.InvariantCulture);

    private static async Task<string> ScalarStringAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        Convert.ToString(await ScalarAsync(connection, transaction, sql, cancellationToken), CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException("Scalar query returned null.");

    private static async Task<DateTimeOffset> ScalarTimestampAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        var value = await ScalarAsync(connection, transaction, sql, cancellationToken);
        var timestamp = (DateTime)value;
        return new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
    }

    private static async Task<object> ScalarAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 0 };
        return await command.ExecuteScalarAsync(cancellationToken) ??
               throw new InvalidOperationException("Scalar query returned null.");
    }

    private static async Task TryRollbackAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch
        {
            // Connection disposal remains the final rollback boundary.
        }
    }
}
