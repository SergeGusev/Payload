# Paper coverage projection correction — 2026-09-20

Approved contract: `RC-20260920-paper-coverage-projection-fix`,
`sha256:eec65a37d17a39c2b698bb6ddf5a101ca158c9452dc5e2cd083cad44fe2d2b2d`.
Approval-only parent: `8a8d60fd`. No production operation in this task.

## Change and evidence boundary

The preceding deployment check recorded 98 SQLSTATE57014 coverage failures between
13:05:00.288598 and 13:09:57.283 UTC. At 13:11:49.802766 UTC the projection still had
zero cursors/members and 15,645 queued events. Its settlement query used a wallet-only
index, filtering asset afterwards. These are the preceding check's observations,
not a fresh production measurement in this task.

The fix adds migration 0015: one concurrent all-source btree index on
`paper_orders(copied_trader_wallet,asset_id)`. Its completion check requires the exact
index definition and valid/ready flags. Existing migration SQL/checksums, financial
SQL, repository transaction boundaries and Dashboard projection version stay unchanged.

The worker's coverage portion starts at 250, halves on 57014/55P03 to a minimum 1 and
pauses coverage for 30 seconds. The legacy loop continues. Eight consecutive nonempty
commits under 500 ms double the limit, capped at 250. Slow/empty portions and failures reset
that streak. Shutdown cancellation is propagated. No failed event is discarded.

## Reproduction

Use native PostgreSQL 17 on loopback, a disposable database named exactly
`pct_codex_paper_confirmation_test`, and a marked run below `D:/CodexTemp/runs`.
The executed environment uses port 55439, user postgres, local trust authentication,
and synthetic data only. Set `TEMP`, `TMP`, `TMPDIR` to `$RunRoot/temp` and
`POLYCOPYTRADER_TEST_POSTGRES_CONNECTION` to that isolated connection.
Set `POLYCOPYTRADER_REPOSITORY_ROOT` to the checkout for source-contract tests.

```powershell
dotnet test tests/PolyCopyTrader.Tests/PolyCopyTrader.Tests.csproj --filter 'FullyQualifiedName~PostgresSchemaMigrationTests|FullyQualifiedName~PaperConfirmationProjectionBatchPolicyTests|FullyQualifiedName~DashboardSnapshotTests|FullyQualifiedName~PaperConfirmationProjectionTests' --artifacts-path "$RunRoot/artifacts" --results-directory "$RunRoot/results" --logger 'trx;LogFileName=focused.trx'
dotnet test tests/PolyCopyTrader.Tests/PolyCopyTrader.Tests.csproj --filter FullyQualifiedName~PaperConfirmationProjectionLoadTests --artifacts-path "$RunRoot/artifacts" --results-directory "$RunRoot/results" --logger 'trx;LogFileName=load.trx'
dotnet build src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj --artifacts-path "$RunRoot/artifacts"
dotnet build src/PolyCopyTrader.Dashboard/PolyCopyTrader.Dashboard.csproj --artifacts-path "$RunRoot/artifacts"
```

Run the load invocation separately, last. It explicitly resets the disposable
projection's four cursors/initialized flag to test initial filling. It does not
clean its 100,000-row fixture; recreate the disposable DB before other suites. Its unique
strategy/wallet/asset names isolate aggregate assertions from smaller earlier
fixtures. No trigger is disabled for the load scenario.

## Verification

- Focused run: 53 passed, 0 failed, 0 skipped, 30 seconds. Includes a real 2-second SQL timeout,
  exact queue/cursor/totals/state rollback, actual BackgroundService dispatch,
  legacy event drain during the 30-second coverage pause, Unknown state and replay
  without double counting. Also verifies wrong existing index definition is
  rejected without migration history, then accepted after fixture correction.
- Initial test build: 0 errors, 127 compiler/analyzer warnings in existing code.
- Dense load: 1 passed, 0 failed, 0 skipped, 5m 30s total. Setup with ordinary triggers
  took 201,542 ms. Initial queue/cursor drain took 68,855 ms, 204,046 refresh results,
  zero errors. The fixture's first 1,000 settlements yielded 500 confirmed, Net 200 and
  fee-inclusive denominator 300; raw and projected values matched.
- Concurrent phase: 600 orders, 600 fills, 600 settlements over 60 seconds generated 1,800
  queue events. Drain after producer completion took 1 ms; cumulative 205,846 refresh
  results, zero errors. Final 1,600 settlements / 800 confirmed / Net 320 / denominator 480
  matched independently aggregated source rows. Both settlement subplans used
  `ix_paper_orders_confirmation_wallet_asset` with wallet AND asset Index Cond.
- Across 49,730 consumer calls, p95 was 2.124 ms and maximum 563.357 ms. These statistics
  include empty polls. The load harness calls the real repository and batch policy
  directly without the service's 1-second idle cadence; p95 is not busy-portion latency
  or production worker throughput. The separate focused test exercises actual
  BackgroundService dispatch and pause behavior.
- Independent read-only post-test SQL at 2026-09-20T13:58:07.482925Z:100,616 raw orders
  = 100,616 projected; 50,310 raw confirmed = 50,310 projected; queue 0; all 4 cursors completed;
  initialized true. Unique fixture strategy 56f4a8b0-9a22-4e27-880b-d64b32401f46 has
  100,600 orders; 16 other orders are earlier focused-test fixtures. No exclusions in
  the global cross-check; strategy totals filter the exact synthetic fixture.
- Final Service and Dashboard incremental builds each: 0 warnings, 0 errors.
- Independent semantic review: agent:/root/paper_coverage_projection_reviewer PASS, no open findings. Compared verbatim requests, approved contract, full diff/new files, README/report and actual TRX/build logs.

Exact independent post-test SQL (forced read-only connection, 5-second statement budget,
UTC, local dedicated test database):

```sql
SELECT count(*) AS orders,count(*) FILTER(WHERE confirmed) AS confirmed FROM paper_orders;
SELECT sum(records) AS orders,sum(confirmed) AS confirmed
FROM paper_confirmation_projection_totals WHERE kind='O' AND hours=0;
SELECT count(*) FROM paper_confirmation_projection_queue;
SELECT kind,completed FROM paper_confirmation_projection_cursor ORDER BY kind;
SELECT initialized FROM paper_confirmation_projection_state;
SELECT closed,confirmed,net,denominator FROM paper_confirmation_projection_totals
WHERE kind='S' AND strategy_id='56f4a8b0-9a22-4e27-880b-d64b32401f46';
```

TRX evidence SHA256 before protected temporary cleanup:

- focused.trx:`028f03bba146f072b003b5e1fe33ad2698e30a39ceccc92567a3fb69041ae492`
- load.trx:`49468f9ff24f25d76a0b20088aeb679b9e5223195ec1a49b3c59d4a01a125f29`

## Limits

This checks coverage projection correctness and bounded progress on a synthetic
dense-wallet fixture, including a real consumer concurrent with new events. It
does not benchmark the outcome verifier, Gamma, Live submissions or production
contention. It does not establish production throughput or whole-history ETA.
At limit 1 a persistently failing event remains queued and coverage remains Unknown;
the fix never skips that event to manufacture progress. Production rollout and its
read-only post-deployment validation remain separate user actions.
