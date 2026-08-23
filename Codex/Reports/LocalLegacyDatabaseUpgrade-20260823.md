# Local Legacy Database Upgrade Audit — 2026-08-23

Target: local PostgreSQL `127.0.0.1:5432/polycopytrader` only. Production was not accessed or changed.

## Approved preflight

- UTC cutoff before the advisory lock: `2026-08-23T10:52:49.582574Z`.
- Locked UTC cutoff: `2026-08-23T10:52:49.783592Z`.
- PostgreSQL `17.5`, primary, 576 application objects, 89 public tables, 307 public indexes, one sequence, no migration-history relation.
- Exact targets: 27 strategies; 334 runs; 11 Paper orders; 11 fills; 11 signals; 11 positions; one settlement; ten diff-state rows; 27 lifetime Dashboard snapshots; 81 recent Dashboard snapshots; zero Live orders.
- Global before-counts: strategies 1,928; Paper orders 32,577; fills 5,522; runs 61,380; signals 84,666; positions 4,863; settlements 3,972; Live orders 0.
- Immutable baseline: `0001-legacy-baseline-a3b0457f`, semantic checksum `4dba8fe092057778ff146e61be43cabbb882358c679c8ee70ade832a872b00d2`.

## Execution and fail-closed stop

- All 789 statements from the immutable baseline completed.
- The five absent embedded data-migration markers were added. Their recorded cleanup totals match the approved targets: Follow Leader 1; ETH Down Filtered Average 10 with 110 runs; hopeless Progress 16; SOL Binance 0; paired Maker-GTD 0 with verified zero residuals.
- The post-baseline gate stopped before inserting the baseline history row because the baseline also seeded 1,440 current-catalog strategies and updated 938 strategies that predated the cutoff. This effect was not included in the approved scope.
- `public.schema_migration_history` exists but contains zero rows. No baseline was falsely marked applied.
- No further database mutation was performed after this mismatch.

## Current read-only state

Read-only audit cutoff: `2026-08-23T10:54:16.414531Z`.

- Strategies: 3,341. Exactly 1,440 have `created_at_utc` between `2026-08-23T10:52:52.212760Z` and `2026-08-23T10:52:58.488877Z`; all 1,440 are enabled and unpaused, all have `live_stakes=false`, and they have zero Paper runs, Paper orders, or Live orders.
- Exactly 938 pre-existing strategies have `updated_at_utc` at or after the execution cutoff. Without a pre-upgrade row snapshot, which individual prior field values changed cannot be reconstructed from the database alone.
- Core post-cleanup counts: Paper orders 32,566; fills 5,511; runs 61,046; signals 84,655; positions 4,852; settlements 3,971; Live orders 0.
- Schema: 110 public tables and 376 public indexes. Required `dashboard_projection_control` and `paper_copied_trader_performance_refresh_queue` relations now exist.
- `schema_data_migrations` contains 15 rows; migration history contains zero rows.
- The latest service heartbeat remains the old July build `e58c6dd64a94b289f70464ce3f12fdf35fc435b3`; the full service host was not started.

## Earlier unresolved decision

The current service still cannot pass versioned initialization because the history relation is empty. Completing recovery now requires a newly approved choice about the 1,440 newly seeded enabled/unpaused strategies and the 938 updated pre-existing strategies. There is no full automatic rollback because no backup was authorized and the baseline is non-transactional.

That state was subsequently resolved under separately approved startup-baseline work. The section above is retained as the exact result of the earlier stopped attempt, not as the final local startup status.

## Direct-skip compaction correction

Approved contract: `RC-20260823-direct-skip-compaction-linear-validation`.

- Product change: removed only the redundant correlated `input_rows other_input` duplicate scan from the production-v1 direct Paper skip archive. The existing C# `EnsureUnambiguousDirectPaperSkipInput` gate remains before the database archive call. The dormant v2 SQL segment was normalized and SHA-256 compared with `HEAD`; both hashes are `95dc0c46d83250391210bec97a5f6755bb379933bc6a3e2d07dc155bef206c5b`.
- Focused tests: 3 passed, 0 failed, 0 skipped. This covered the production-v1 SQL shape, pre-persistence ambiguous-input rejection, and an exact 2,000-row unique pure-skip batch.
- The 2,000-row test verified 2,000 returned logical IDs, 0 retained raw rows, 2,000 rollups, 2,000 tombstones, 2,000 projection events, one reconciliation-queue row, and an empty idempotent retry. Its measured repository call remained below the unchanged 30-second command timeout.
- Complete `StrategyRunRetentionPostgresIntegrationTests`: 37 passed and 9 failed. All 9 failures were reproduced independently, by exact test name, against the unmodified approved baseline commit `d76cfc3a` in a fresh PostgreSQL 18.6 database; therefore this correction introduced 0 new failures. The old failures remain outside this contract.
- Release solution build: 0 errors and 126 warnings.

## Bounded local runtime verification

Endpoint: only `127.0.0.1:5432/polycopytrader`. Production was not accessed. The runtime binary was built from approved baseline commit `d76cfc3a` plus only the 12-line SQL removal, excluding unrelated working-tree changes.

- Binary SHA-256: `9a258b8c180397c17a2a4dec97f9dc99760767fb225a9ee774e87cd7a9e760cb`.
- Product version: `1.0.0+d76cfc3aec31c96fa776c5ec52891f885eb876cd`; MVID suffix reported by heartbeat: `eee1038dbf40`.
- Read-only preflight cutoff: `2026-08-23T18:51:54.747709Z`. PostgreSQL 17.5 primary; one exact baseline row; 3,341 strategies, of which 3,195 were enabled and unpaused; zero active `live_stakes`; zero Live orders; zero ungranted locks; no service process. `Bot.EnableLiveTrading=false` was verified from the exact runtime configuration.
- Exact started process: PID 81548. Process start `2026-08-23T18:52:43.1448174Z`; first healthy heartbeat `2026-08-23T18:52:54.778174Z`; second advancing heartbeat `2026-08-23T18:53:54.869309Z`; stop requested `2026-08-23T18:54:22.0148492Z`; process ended `2026-08-23T18:54:22.0825951Z`.
- Runtime duration was 98.938 seconds absolute and 86.476 seconds after first healthy observation, within the approved 120/90-second bounds. Both observations were `Running`, mode `Live`, with `last_error=NULL`. Mode `Live` is the service execution mode; Live order placement remained disabled and the Live-order count stayed zero.
- Exact service-log interval contained 13,391 timestamped events, 0 Error, 0 Fatal, 0 timeout/cancellation, and 0 advisory-lock messages. It contained 876 warnings: 873 slow market-data side-effect warnings and 3 skipped crypto-reference ticks. These warnings did not prevent heartbeat progress, but they are reported rather than treated as part of this SQL correction.
- PostgreSQL logged 11 advisory-lock waits and 11 corresponding acquisitions; the longest reported wait was 4.512 seconds. The interval contained 0 Error/Fatal/Panic, 0 statement cancellation, and 0 timeout. Post-run checks found zero ungranted locks, zero granted advisory locks, zero remaining service connections, and no remaining PID 81548 process.
- Post-run read-only cutoff: `2026-08-23T18:54:45.834040Z`. Ordinary local runtime deltas were: signals +80, Paper orders +80, settlements +3, fills +0, positions +0, Live orders +0. Raw strategy runs changed from 67,692 to 65,802 because direct skip compaction ran while new Paper cycles were also persisted.

No schema migration, DDL, manual data repair, strategy/configuration change, Production access, deployment, or Live venue action was performed by this correction. The isolated PostgreSQL test databases and exact verification process were removed after verification.
