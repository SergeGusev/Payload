namespace ReferenceAverageHistoryCorrectionApply;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var options = CommandLine.Parse(args);
            Console.WriteLine($"Mode: {options.Mode}");
            Console.WriteLine($"Graph: {options.GraphDirectory}");
            Console.WriteLine($"Expected graph manifest SHA-256: {options.GraphManifestSha256}");
            Console.WriteLine($"Pinned database: {options.Host}:{options.Port}/{options.Database}; search_path={DatabaseConnection.RequiredSearchPath}");

            var graph = GraphPackageReader.Read(options);
            Console.WriteLine(
                $"Verified graph package: main removals={graph.MainRemovals.Count:N0}; child removals={graph.ChildRemovals.Count:N0}; adds={graph.Adds.Count:N0}.");
            if (graph.BlockingErrors.Count != 0)
            {
                Console.Error.WriteLine("FAIL CLOSED: graph package does not authorize physical mutation:");
                foreach (var error in graph.BlockingErrors)
                {
                    Console.Error.WriteLine("  - " + error);
                }
                return 3;
            }

            switch (options.Mode)
            {
                case OperationMode.Prepare:
                {
                    Console.WriteLine("Preparing and sealing durable full-backup + graph evidence. No database connection will be opened.");
                    var prepared = await PreparedPackageStore.PrepareAsync(options, graph, cancellation.Token);
                    Console.WriteLine("PREPARATION COMPLETE. Prepared manifest: " + prepared.Path);
                    Console.WriteLine("Required --prepared-package-sha256: " + prepared.Sha256);
                    return 0;
                }
                case OperationMode.Preflight:
                {
                    var database = new CorrectionDatabase(options, graph);
                    var report = await database.PreflightAsync(cancellation.Token);
                    PrintPreflight(report);
                    return report.BlockingErrors.Count == 0 ? 0 : 3;
                }
                case OperationMode.Apply:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine("All graph checks passed. Starting locked backup/apply transaction.");
                    var manifest = await database.ApplyAsync(cancellation.Token);
                    Console.WriteLine("APPLY COMMITTED. Durable rollback manifest: " + manifest);
                    Console.WriteLine("Keep the service stopped until all queued projection rebuilds and post-restart gates are verified.");
                    return 0;
                }
                case OperationMode.MaintenanceRebuild:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine(
                        "Starting explicit stopped-service maintenance rebuild. Entry/Live workers are not started by this tool.");
                    var evidence = await database.MaintenanceRebuildAsync(cancellation.Token);
                    Console.WriteLine("MAINTENANCE REBUILD VERIFIED. Evidence: " + evidence);
                    Console.WriteLine(
                        "Immediate rollback window is now closed. A normal service start is allowed only for child-assignment refresh, followed by stop + --post-child-gate.");
                    return 0;
                }
                case OperationMode.PostChildGate:
                {
                    var database = new CorrectionDatabase(options, graph);
                    var evidence = await database.PostChildGateAsync(cancellation.Token);
                    Console.WriteLine("FINAL CHILD-ASSIGNMENT GATE PASSED. Evidence: " + evidence);
                    return 0;
                }
                case OperationMode.FinalizeApply:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine(
                        "Starting commit recovery. No mutation will be retried; committed xid and exact postimage are mandatory.");
                    var manifest = await database.FinalizeApplyAsync(cancellation.Token);
                    Console.WriteLine("APPLY COMMIT EVIDENCE FINALIZED. Durable rollback manifest: " + manifest);
                    return 0;
                }
                case OperationMode.FinalizeRollback:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine(
                        "Starting rollback commit recovery. No rollback mutation will be retried; committed xid and exact post-rollback image are mandatory.");
                    var manifest = await database.FinalizeRollbackAsync(cancellation.Token);
                    Console.WriteLine("ROLLBACK COMMIT EVIDENCE FINALIZED. Durable manifest: " + manifest);
                    return 0;
                }
                case OperationMode.Rollback:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine("All graph checks passed. Starting locked rollback transaction.");
                    await database.RollbackAsync(reconciled: false, cancellation.Token);
                    Console.WriteLine("ROLLBACK COMMITTED. Exact scoped preimage was restored.");
                    return 0;
                }
                case OperationMode.RollbackReconciled:
                {
                    var database = new CorrectionDatabase(options, graph);
                    Console.WriteLine(
                        "All graph checks passed. Starting reconciled rollback with immutable-base and zero-new-decisions gates.");
                    await database.RollbackAsync(reconciled: true, cancellation.Token);
                    Console.WriteLine(
                        "RECONCILED ROLLBACK COMMITTED. Original base scope was restored; full projection/hourly/child refresh verification is mandatory after restart.");
                    return 0;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (HelpRequestedException)
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled. Any open database transaction was rolled back by disposal.");
            return 130;
        }
        catch (ApplyCommitRecoveryRequiredException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(
                exception.CommitAcknowledged
                    ? "The database is committed. Only --finalize-apply or a verified rollback is allowed."
                    : "The database outcome is ambiguous. Only --finalize-apply may determine it; never retry --apply blindly.");
            return 4;
        }
        catch (RollbackCommitRecoveryRequiredException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(
                exception.CommitAcknowledged
                    ? "The rollback database transaction is committed. Only --finalize-rollback is allowed."
                    : "The rollback outcome is ambiguous. Only --finalize-rollback may determine it; never retry rollback blindly.");
            return 5;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL CLOSED: " + exception.Message);
            return 2;
        }
    }

    private static void PrintPreflight(DatabasePreflight report)
    {
        Console.WriteLine(
            $"Database identity: {report.ServerAddress}:{report.ServerPort}/{report.Database}; " +
            $"search_path={report.SearchPath}; timezone={report.TimeZone}; isolation={report.Isolation}; read_only={report.ReadOnly}.");
        Console.WriteLine(
            $"Service heartbeat: status={report.ServiceStatus ?? "<missing>"}; last={report.ServiceLastHeartbeatUtc?.ToString("O") ?? "<missing>"}.");
        Console.WriteLine(
            $"Runtime evidence: PolyCopyTrader sessions={report.OtherPolyCopyTraderSessions:N0}; " +
            $"active sessions={report.OtherActiveSessions:N0}; Live/shadow={report.LiveShadowOverlapRows:N0}; " +
            $"unsupported dependencies={report.UnsupportedDependencyRows:N0}; daily_reports={report.DailyReportRows:N0}.");
        Console.WriteLine(
            $"Storage policy: data_directory={report.DataDirectory}; scoped row bytes=" +
            $"{report.ScopedFootprint.Sum(item => item.RowBytes):N0}; global projection relation bytes=" +
            $"{report.GlobalProjectionRelationBytes:N0}; estimated WAL policy bytes={report.EstimatedWalPolicyBytes:N0}; " +
            $"required free-space policy bytes={report.RequiredFreeSpacePolicyBytes:N0}; " +
            $"attested C: free bytes={report.AttestedFreeBytes?.ToString("N0") ?? "<not supplied>"}.");
        foreach (var table in report.ScopedFootprint)
        {
            Console.WriteLine(
                $"  footprint {table.TableName}: rows={table.RowCount:N0}; row_bytes={table.RowBytes:N0}");
        }
        if (report.BlockingErrors.Count == 0)
        {
            Console.WriteLine("READ-ONLY PREFLIGHT PASSED. No mutation was attempted.");
            return;
        }

        Console.Error.WriteLine("READ-ONLY PREFLIGHT BLOCKED:");
        foreach (var error in report.BlockingErrors)
        {
            Console.Error.WriteLine("  - " + error);
        }
    }
}
