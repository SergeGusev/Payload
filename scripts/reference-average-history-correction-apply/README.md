# Reference-average history correction apply tool

This is a fail-closed, one-off C#/.NET tool for the physical Paper-history
correction authorized by the reviewed reference-average replay. It has not been
run against production by Codex. It never inserts, updates, deletes, or alters a
`live_orders` row, and it never changes `session_replication_role` or disables all
triggers/constraints.

The tool is pinned to:

- `192.168.0.101:5432/polycopytrader`
- `search_path=pg_catalog,public`
- UTC transactions
- a command-line SHA-256 pin for the final graph manifest

Credentials are accepted only through `POLYCOPYTRADER_POSTGRES_CONNECTION`.

## Safety model

The default mode is read-only `--preflight`. Physical apply/recovery modes require
a fresh SHA-pinned operator attestation proving all of the following at the time
of the operation:

- Windows service `PolyCopyTrader.Service` is `Stopped`;
- its startup mode is `Disabled`;
- PostgreSQL's exact `data_directory` and the free bytes on its `C:` data volume;
- observation age is at most 15 minutes.

The database gate independently requires a stale service heartbeat, no other
PolyCopyTrader sessions, no other active client writer, no Live/shadow overlap,
no unsupported dependency, and exactly zero `daily_reports` rows. Apply uses a
`SERIALIZABLE` transaction, a transaction-scoped advisory lock, `NOWAIT` table
locks, full-row optimistic hashes, exact operation-footprint rows/bytes, and exact
schema/FK/action/reference-column checks. The multi-connection maintenance phase
holds the same advisory lock as a session lease from its first database check
through durable completion evidence. Rollback reopens each snapshot with a
write-denying lease, revalidates its hash and row count from that same stream,
and rejects any restored row outside the exact graph-derived scope.

The attestation JSON contract is:

```json
{
  "schema_version": 1,
  "host": "192.168.0.101",
  "port": 5432,
  "database": "polycopytrader",
  "data_directory": "<exact SHOW data_directory result>",
  "data_volume": "C:",
  "free_bytes": 0,
  "service_name": "PolyCopyTrader.Service",
  "service_state": "Stopped",
  "service_start_mode": "Disabled",
  "collection_method": "windows_service_and_driveinfo_local_capture_v1",
  "observer": "<operator identity>",
  "observed_at_utc": "<fresh UTC timestamp>"
}
```

`free_bytes` must be the actual positive value, not the example zero. Supply the
file and its lowercase SHA-256 with `--operator-attestation` and
`--operator-attestation-sha256`.

## Existing full-backup evidence

The nested `backup-evidence-generator` seals an already completed PostgreSQL 18
directory-format dump. It deliberately does **not** invoke `pg_dump` and therefore
must not be used to start a second dump.

It requires:

- the exact existing dump directory and its original log/start/end/exit evidence;
- PostgreSQL 18 `pg_restore.exe`;
- `POLYCOPYTRADER_POSTGRES_CONNECTION` for the pinned source;
- `REFERENCE_AVERAGE_RESTORE_CONNECTION` for an empty loopback database whose name
  starts with `reference_history_restore_`.

It runs `pg_restore --list`, restores with `--jobs=2`, and compares the canonical
public schema and every public-table row count between source-before,
source-after, and the restored database. On restore failure it leaves the restore
database intact for diagnosis.

Example shape (paths and timestamps must come from the completed dump):

```powershell
dotnet run --project scripts/reference-average-history-correction-apply/backup-evidence-generator -- `
  --source-host 192.168.0.101 --source-port 5432 --source-database polycopytrader `
  --dump-dir <existing-directory-dump> --dump-log <original-pg-dump-log> `
  --dump-started-at-utc <UTC> --dump-completed-at-utc <UTC> --dump-exit-code 0 `
  --evidence-dir <new-empty-evidence-dir> --postgres-bin-dir <PostgreSQL-18-bin>
```

## Required sequence

All graph arguments below must point to the final reviewed replacement graph and
its exact manifest SHA-256. Do not substitute an earlier preview.

1. Run default preflight. It opens only a read-only `REPEATABLE READ` transaction.
2. Run `--prepare` once. This is offline: it opens no database connection. It
   copies and seals the graph plus full-backup evidence into a unique empty child
   of `outputs/postgres-backups` and prints a prepared-package SHA-256.
3. Stop and disable the Windows service, capture a fresh operator attestation, and
   run `--apply` with the prepared-package SHA and a unique marked
   `D:\CodexTemp\runs\<session>\...` staging directory.
4. If apply reports an uncertain/acknowledged commit-finalization failure, do not
   retry apply. Use `--finalize-apply` with the durable `backup-manifest.json`.
5. While the service remains Stopped+Disabled, run `--maintenance-rebuild`. It
   directly invokes the exact Storage dashboard bootstrap, refreshes hourly PnL
   for the complete `StrategyIds.DateDependentStrategyVariants` set, and rebuilds
   copied-trader performance for the exact affected wallets. It does not start
   hosted Paper, Child, or Live workers.
6. A normal service start is permitted only for the child-parent assignment
   refresh. Stop and disable it again before any final gate or rollback.

The complete command syntax is printed by:

```powershell
dotnet run --project scripts/reference-average-history-correction-apply -- --help
```

## Rollback boundaries

Before `--maintenance-rebuild`, `--rollback` requires the exact postimage and
restores the complete scoped preimage. After derived projections have been
rebuilt, only `--rollback-reconciled` is eligible; it additionally proves the
immutable corrected state, validates the sealed maintenance evidence, rechecks
the complete 24-row hourly-PnL set for every exact Domain date-dependent strategy,
rechecks Dashboard/copied-performance state, and proves zero new affected
Main/Child/Paper/Live decisions. It then restores the base preimage and queues
another rebuild.

Rollback commit uncertainty is resolved only by `--finalize-rollback`; blind retry
is rejected.

## Child-refresh evidence

The product schema has no durable child-assignment cycle marker, so
`--post-child-gate` requires an external SHA-pinned operator attestation and the
exact SHA-pinned Serilog file containing one completion line. The attestation and
log must be in the same directory; `service_log_file_name` must be a leaf name.

```json
{
  "schema_version": 1,
  "host": "192.168.0.101",
  "port": 5432,
  "database": "polycopytrader",
  "service_name": "PolyCopyTrader.Service",
  "service_log_file_name": "polycopytrader-service-YYYYMMDD.log",
  "service_log_sha256": "<lowercase SHA-256>",
  "completion_log_line": "[YYYY-MM-DD HH:mm:ss.fff <UTC offset> INF] BTC Up or Down 5m child-parent assignments refreshed. Children=N ActiveParents=M",
  "refresh_completed_at_utc": "<the exact timestamp parsed from that line>",
  "children": 0,
  "active_parents": 0,
  "collection_method": "serilog_plaintext_exact_line_sha256_plus_operator_capture_v1",
  "observer": "<operator identity>",
  "observed_at_utc": "<fresh UTC timestamp>"
}
```

The gate verifies the exact line occurs once, its parsed timestamp/counts match the
attestation, the service is stopped and disabled again, no new affected
Main/Child/Paper/Live decision exists, maintenance-derived projections remain
valid, active assignment counts match the log, and affected active assignments
are not stale. It then copies the attestations, log, and exact affected-assignment
snapshot into durable evidence. This adds no product table or schema marker.

The correction intentionally preserves `strategies.paper_lost_counter`; future
stake state is not replayed.

## Build and test

Use a marked `D:\CodexTemp` run for artifacts and set `TEMP`, `TMP`, and `TMPDIR`
to its `temp` child before running:

```powershell
dotnet build scripts/reference-average-history-correction-apply/ReferenceAverageHistoryCorrectionApply.csproj --artifacts-path <marked-run>\build
dotnet test scripts/reference-average-history-correction-apply/tests/ReferenceAverageHistoryCorrectionApply.Tests.csproj --artifacts-path <marked-run>\tests --results-directory <marked-run>\tests\results
dotnet build scripts/reference-average-history-correction-apply/backup-evidence-generator/ReferenceAverageHistoryCorrection.BackupEvidence.csproj --artifacts-path <marked-run>\backup-generator-build
```
