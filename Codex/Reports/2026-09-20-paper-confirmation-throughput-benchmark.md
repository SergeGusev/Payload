# Paper confirmation throughput and trust — local evidence

Approved contract: `RC-20260920-paper-confirmation-throughput-and-trust-v2`,
`sha256:af5d392a16c476be3da48c8fd2a61e9871851105ba8531f18a7322e7b3e4dac2`.
Approval parent: `aa6f2cd3`. Implementation is the commit containing this report.
Scope: local Service, Dashboard, specialized migrations and tests only. No production
connection, migration, deployment, service restart or historical production mutation.

## Reproduction and fixture

Native Windows PostgreSQL 17.5, loopback port 55439; disposable database
`pct_codex_paper_confirmation_test`. All build/test/database artifacts were under
the marked `D:/CodexTemp/runs/paper-throughput-v2-20260920-01` session.
The executable fixture and exact SQL are in
[`PaperConfirmationBenchmarkTests.cs`](../../tests/PolyCopyTrader.Tests/PaperConfirmationBenchmarkTests.cs).
The frozen staged-Idle scheduler from the approval parent is in
[`PaperConfirmationBaselineWorker.cs`](../../tests/PolyCopyTrader.Tests/PaperConfirmationBaselineWorker.cs).

Final test interval: **2026-09-20 12:51:30.3305788–12:53:43.4002836 UTC**.
Each scheduler observes 60 seconds, with actual elapsed duration below including stop.
The same fixture is reset between baseline and new scheduling. Fixture prefix:
`pcbench-a9471ffe2c7540bca6453201863b4d47`.

- Exactly 10,000 orders, 100 distinct conditions, 20 strategies; deterministic IDs
  derive from the random per-run prefix and fixture index.
- 2,000 filled BUYs with actual test fills, settled runs and settlement records;
  8,000 cancelled/unfilled BUYs share accounting units with those fills.
- Each filled order costs $6, has 12 shares and $0.20 recorded entry fees.
  Initial outcome is Up; even market indices stay Up, odd indices resolve Down.
  Correct final gross/net is respectively $6/$5.80 or −$6/−$6.20 per filled run.
- Half the markets are Recent (approximately two hours old), half Archive
  (approximately two days old). Scheduling cutoff is created_at UTC minus 24h.
  Other fixture orders are excluded by setting their retry timestamp to infinity.
- Gamma is a deterministic 5 ms fake with final status and exact token mapping;
  database claims, locks, financial writes, triggers and commits are real PostgreSQL.
- Foreground updates rotate across these same 20 strategy rows with a 20 ms delay,
  taking a normal trading-activity lease. Nearest-rank p95 uses all completed write
  durations. Waiting-lock counts are samples, not proof of absence of brief waits.
- Both paths use the current repository and new trigger schema. Baseline retains
  single-order apply, uncached lookup and the frozen staged-Idle scheduler. The
  benchmark does not run either Dashboard projection consumer concurrently.

Set `POLYCOPYTRADER_REPOSITORY_ROOT` to the checkout and the local test connection
through `POLYCOPYTRADER_TEST_POSTGRES_CONNECTION`; set
`POLYCOPYTRADER_CONFIRMATION_BENCHMARK=1`, then run:

```powershell
dotnet test tests/PolyCopyTrader.Tests/PolyCopyTrader.Tests.csproj --artifacts-path "$env:CODEX_TASK_RUN/artifacts" --results-directory "$env:CODEX_TASK_RUN/results" --filter "FullyQualifiedName~PaperConfirmationBenchmarkTests" --logger trx
```

The run finishes well below its approved ten-minute limit. No claim is made that
all 10,000 records must finish within the observation window.

## Final measurements

| Measure | Baseline staged Idle | New bounded verifier |
|---|---:|---:|
| Elapsed seconds | 60.0685316 | 60.0077691 |
| Unique confirmed orders | 12 | 3,527 |
| Orders carrying corrected accounting-unit evidence | 1 | 1,618 |
| Recent confirmed | 0 | 2,353 |
| Archive confirmed | 12 | 1,174 |
| Confirmed/minute, elapsed-normalized | 11.986309 | 3,526.543366 |
| Foreground writes | 2,274 | 1,893 |
| Foreground p95, ms | 1.7820 | 4.3856 |
| Foreground errors | 0 | 0 |
| Sampled waiting locks | 0 | 0 |
| Fake Gamma calls | 13 | 100 |
| Incorrect financial runs in confirmed accounting units | 0 | 0 |

Corrected evidence is attached to every newly confirmed order in a corrected unit;
it is not a count of distinct cash payouts or distinct changed filled runs.
The new path meets the agreed local checks: at least 100 processed, real financial
corrections, progress in both lanes, more than 34 confirmations/minute, and p95
increase **2.6036 ms**, below `max(50 ms, 10% × baseline p95) = 50 ms`.
This is a local synthetic result, not a production speed, catch-up ETA, full-history
verification or proof of historical Paper fill realism.

## Indexed access and financial checks

`EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)` ran without disabling sequential scans.
Full queries are retained in the benchmark source. Claims use `NOT confirmed`, due
retry, the exact lane cutoff, retry/created/id ordering, limit 32 and SKIP LOCKED.
Matched financial probes use the exact strategy, wallet, asset and condition.

| Query | Rows | Execution ms | Shared hits / reads | Indexes used |
|---|---:|---:|---:|---|
| Recent claim | 32 | 0.128 | 127 / 0 | ix_paper_orders_unconfirmed |
| Archive claim | 32 | 0.064 | 66 / 0 | ix_paper_orders_unconfirmed |
| Related matched runs | 1 | 0.022 | 16 / 0 | ix_paper_orders_strategy_condition; ix_strategy_market_paper_runs_order |
| Matched settlement | 1 | 0.012 | 3 / 0 | ux_paper_position_settlements_wallet_asset |

No sequential scan of orders, runs or settlements occurred in these four plans.
This does not assert identical plans or costs for the production dataset. The
additional created-at and positive-inventory indexes are migration-registered;
the measured claim planner chose the existing due-retry index.

The benchmark independently checks every run whose strategy/condition contains a
confirmed order against the parity-of-market-index payout above; mismatches are zero.
An independent post-run read of the exact prefix reproduced 10,000/3,527/1,618/1,174
for total/confirmed/corrected/archive. The confirmed-unit run cohort contained 706
runs: 350 losses and 356 wins. Stored gross $36 and net −$105.20 equal both the SQL
sum of expected per-market payouts and `356*6 - 350*6`, less `706*0.20` in fees.
The exact independent cohort/aggregation query was:

```sql
SELECT count(*), count(*) FILTER(WHERE r.net_realized_pnl_usd<0),
       sum(r.realized_pnl_usd), sum(r.net_realized_pnl_usd),
       sum(CASE WHEN split_part(r.condition_id,'-condition-',2)::integer%2=0 THEN 6 ELSE -6 END),
       sum(CASE WHEN split_part(r.condition_id,'-condition-',2)::integer%2=0 THEN 5.8 ELSE -6.2 END)
FROM strategy_market_paper_runs r
WHERE r.condition_id LIKE 'pcbench-a9471ffe2c7540bca6453201863b4d47%'
  AND EXISTS(SELECT 1 FROM paper_orders o WHERE o.strategy_id=r.strategy_id
             AND o.condition_id=r.condition_id AND o.confirmed);
```

Checks on sales, fees, missing fees, Live immutability, counters, LossDiff, hourly and
wallet aggregates are covered by focused integration fixtures, not by this benchmark.

## Consolidated verification

- `focused-final.trx`: **205 passed**, including 74 confirmation tests and the
  immutable baseline/default migration-catalog check. Fourteen existing Dashboard
  tests were guarded out because they require a different disposable database name.
- `dashboard-postgres-final.trx`: those exact **14 tests passed, zero skips**, on
  `pct_codex_skip_v2_20260920122500_af5d392a`; 2m22s. Independent review compared the
  skipped and executed test names and found no unmatched test.
- `benchmark-final.trx`: **1 passed**, explicitly enabled; 2m13s. Thus all **220
  distinct selected checks** actually ran and passed across the three final runs.
- `boundaries-final.trx`: earlier additional targeted run, **12 passed, zero skips**;
  these are also covered by the consolidated run, not added to the distinct count.
- Service and Dashboard final incremental builds: **0 errors, 0 warnings** each.
  Compilation during test builds emitted compiler/analyzer warnings, including
  CS0649 for the test fake's unassigned `InFlight` field; these were not all classified
  as pre-existing. No test failure is hidden by build success.
- Actual PostgreSQL tests cover multi-order rollback/retry, sales and entry/exit
  fee denominator, Confirmed-only propagation, sibling-run ambiguity, fill-dependent
  settlement invalidation, unavailable projection → Unknown, source deletion before
  event consumption, duplicate-event idempotence, restart, inclusive recent endpoints
  and activation of future timestamps without another source write.
- WPF was compiled; no interactive visual smoke test is claimed.

TRX SHA-256 (compact audit fingerprints; disposable logs are removed on completion):

```text
focused-final:            47bd466f19069517e78b80e130dd90cb35b8d86e79b9b0003b9381f2e963dea8
dashboard-postgres-final: 44d997c40e2a4184ef56e78dfdac6036f0adc9e3372505028b91f25c08392c16
benchmark-final:          5eb5587bc935d64c61458026a7f2dde5f77bf6ba23ade7054edda2599bdf6e90
```

## Operational limits

Independent semantic review by `agent:/root/paper_throughput_trust_reviewer`: **PASS,
no open findings**, after comparing the original requests, approved v2 contract,
complete diff, README, this report and final TRX/build evidence.

The final cache and projection seed incrementally; initial coverage is Unknown until
bounded initialization catches up. Financial corrections still recompute complete
dependent accounting units inside one bounded transaction. Large production units
may time out and remain deferred. Durable rates begin after initialization and use
unique transitions retained across source-row deletion. Confirmed means outcome and
recorded accounting were checked; it does not repair hypothetical fills or replay
past strategy decisions.

The next separately authorized rollout requires read-only production counts, distinct
market distribution, index validity/plans and health before choosing its operational
batch size. Concurrent index creation can leave an invalid index if interrupted;
completion checks fail closed and require operator recovery before successful startup.
Neither production throughput nor time to verify the entire database is established here.
