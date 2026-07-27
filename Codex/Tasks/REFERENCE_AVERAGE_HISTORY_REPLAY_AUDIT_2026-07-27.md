# Reference Average historical replay sufficiency audit — 2026-07-27

## Decision

The production database is sufficient to build a high-confidence, row-level counterfactual signal replay, but it is **not sufficient for a complete bit-exact rewrite of the historical Paper/Live portfolio**.

The practical boundary is:

- Existing Paper entries can be reclassified after per-row validation of their persisted decision inputs and retain their actual fill, settlement, and PnL when the corrected signal says the entry should remain.
- Existing Paper entries rejected by the corrected signal can be marked as `would_skip` in a replay ledger and excluded from a corrected analytical view.
- A previously skipped signal can be classified exactly only when its persisted diagnostics contain the decision-time current price and the complete set of full reference windows that was available to the online selector.
- A newly inferred signal does not, by itself, provide an exact historical FAK fill. Full ask depth for the newly selected outcome is not systematically persisted at every skipped decision time.
- Historical Live fills cannot be cancelled retroactively. Any correction to Live history is necessarily counterfactual reporting, not a reversal of an exchange trade.
- Fully counterfactual Child reassignment and LostCounter stake progression are not exactly reproducible because the required historical state is not event-sourced.

The safe result is therefore an immutable, versioned replay/correction ledger alongside the actual history, not an in-place conversion of historical `Filled`/`Settled` rows to `Cancelled` and not synthetic insertion into actual Live history.

## Exact scope and cutover

Production evidence:

- PostgreSQL endpoint: `192.168.0.101:5432/polycopytrader`.
- Deployed service start/cutover: `2026-07-27T13:24:05.932282Z` (`2026-07-27 16:24:05.932282 Europe/Sofia`).
- Runtime heartbeat reported `Running`, `Live`, `last_error = null`, and exact informational version `1.0.0+ce430a2021840950f7e2c64bdc75d57409d25375`.
- The exact allowlist came from `REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md`: 848 statically affected IDs and 247 conditionally downstream Child IDs.
- All 848 static IDs existed in production and all 848 had historical runs before the cutover.

All counts below use only rows with `entry_due_at_utc < 2026-07-27T13:24:05.932282Z` or, for order tables, `created_at_utc` before the same cutoff. The transaction timezone was UTC.

## Static history at the v2 cutover

| Asset | Runs | Settled | Skipped | Run-linked Paper orders |
|---|---:|---:|---:|---:|
| BTC | 1,069,195 | 222,419 | 846,776 | 222,429 |
| ETH | 1,117,744 | 218,973 | 898,771 | 219,056 |
| SOL | 879,475 | 266,380 | 613,095 | 266,480 |
| **Total** | **3,066,414** | **707,772** | **2,358,642** | **707,965** |

Additional verified totals:

- distinct markets: `21,319`;
- first static entry due: `2026-06-23T09:59:30Z`;
- last legacy entry due before cutover: `2026-07-27T13:19:30Z`;
- `paper_fills` linked to the 848 static IDs before cutover: `707,772`;
- `paper_orders.raw_decision_json`: present on `707,965 / 707,965` orders;
- `live_orders` records linked to the 848 static IDs before cutover: `2,094`.

The two independent total checks agree:

- `707,772 Settled + 2,358,642 Skipped = 3,066,414 runs`;
- `1,069,195 BTC + 1,117,744 ETH + 879,475 SOL = 3,066,414 runs`.

The 193-order difference between Paper orders and settled runs consists of run/order lifecycles that ended in `Skipped`; it must not be silently treated as an additional settled fill.

## Skipped-decision diagnostics

For a terminal skipped run, the possible replay source is `strategy_market_paper_runs.skip_diagnostics_json`.

| Asset | Skipped | With some diagnostic JSON | Missing diagnostic JSON | Presence rate |
|---|---:|---:|---:|---:|
| BTC | 846,776 | 760,410 | 86,366 | 89.80% |
| ETH | 898,771 | 809,818 | 88,953 | 90.10% |
| SOL | 613,095 | 552,002 | 61,093 | 90.04% |
| **Total** | **2,358,642** | **2,122,230** | **236,412** | **89.9768%** |

`With some diagnostic JSON` is deliberately not called `exact replayable`: a row can contain diagnostics from an earlier rejection stage. Reference Average inputs must be validated inside each direct or nested decision before replay.

The missing diagnostics are not evenly distributed. In particular:

- `2026-07-26`: 228,340 skipped runs, 157,697 with diagnostics, 70,643 without;
- legacy portion of `2026-07-27` before v2 cutover: 122,622 skipped runs, **zero** with diagnostics.

This agrees with the current repository behavior that deliberately stores `NULL` diagnostics for terminal `Skipped` runs. Historical rows retained before that behavior remain available, but the database cannot reconstruct the missing decision-time current-price fetch exactly from a later top-of-book or reference-tick row.

## Exact cohort capable of producing newly added signals

Derived from the verified old and new selectors plus the exact inventory:

- 208 of the 848 static variants are behaviorally unchanged by Max/Min selection: 112 ordinary fixed-Up variants and 96 Optimized fixed-Up variants.
- The other 640 variants are structurally capable of losing legacy entries.
- Only the 192 Optimized fixed-Down and Optimized neutral variants can also gain entries. Their required selected boundary window can change from the legacy maximum to the new minimum, including a new match on the required `3h` window.
- Ordinary fixed-Down, ordinary neutral, native LowEnter, Confirmed, and non-Optimized LowerEnter paths can only retain or remove old entries; the corrected lower-envelope condition is a subset of the legacy maximum-only lower condition.
- Child variants can lose copied entries under frozen historical assignments. A complete counterfactual parent reselection is a separate stateful replay and is not exact from current storage.

Production coverage for the 192 potential-add variants at cutover:

| Measure | Count |
|---|---:|
| Runs | 277,588 |
| Existing orders | 23,772 |
| Skipped runs | 253,816 |
| Skips with diagnostic JSON | 208,031 |
| Skips with decision-time current price and a non-empty persisted full-window array | 207,991 |
| Skips without exact persisted signal inputs | 45,825 |

Exact persisted signal-input coverage for the potential-add skipped cohort is therefore `207,991 / 253,816 = 81.9456%`.

All `23,772 / 23,772` existing orders in this 192-strategy cohort had a current price and non-empty `reference_averages` array. Of the skipped rows with diagnostics, 40 lacked the decision-time current price; the other 45,785 exact-input gaps had no diagnostic JSON.

An exact online replay must use the persisted set of full windows that was available at that instant. It must not fabricate an unavailable 24-hour window during warm-up. For reference, `112,705` skipped rows and `10,744` existing orders in this cohort passed the strict current-price plus eight-array plus `reference_full_average_count = 8` gate; the other replayable rows still had a non-empty persisted online set but did not pass that stricter all-eight gate.

## Why exact newly added FAK fills are not generally recoverable

The strategy evaluates the Reference Average signal before it requests the selected outcome's order book. Therefore a legacy skip caused by the signal path normally has no FAK execution summary for the hypothetical new outcome.

Verified persistence boundaries:

- `crypto_up_down_5m_odds_ticks` stores best bid/ask and related top-of-book fields for both outcomes, not full ask depth.
- `paper_orders.raw_decision_json` stores FAK summary/VWAP/levels/fill information for the actually evaluated outcome only.
- `paper_live_shadow_decisions.order_book_snapshot_json` can store up to 20 levels, but only for a selected shadow decision; a signal-level skip never reaches that path.
- The global `order_book_snapshots` table contained 819,023 rows, including 806,458 non-null `raw_json` values at `2026-07-27T13:54:13Z`, but this global inventory does not prove a same-time, correct-condition, correct-outcome, full-depth snapshot for each missed decision.
- The earlier focused ETH replay found both saved outcome-side Filled FAK summaries within two seconds on only `1,510 / 2,088` strict-identity markets; even a five-second allowance reached only 1,587. That observed incomplete cohort independently disproves complete historical execution coverage.

Consequently:

- `would_enter` can be exact when signal inputs are complete;
- exact historical fill/VWAP/partial quantity can be used only for a row with independently validated matching depth and freshness;
- otherwise the added trade must be labelled `modeled_execution`, with explicit price/depth/stake assumptions;
- modeled PnL must never be merged with factual Live fills or presented as an observed Paper fill.

## Other blockers to a bit-exact full-portfolio rewrite

### Progression state

The next stake can depend on `LostCounter`. Current counters/settings are mutable fields; the database does not hold a complete event history of their value at every historical decision. Removing or adding an early trade changes the subsequent counterfactual stake path.

### Child parent selection

Before cutover, the 247 conditional Child IDs had `496,784` settled runs, all with linked Paper orders:

| Asset | Child runs before cutover |
|---|---:|
| BTC | 177,132 |
| ETH | 128,574 |
| SOL | 191,078 |
| **Total** | **496,784** |

Existing Child order JSON preserves the historical parent link, so a frozen-assignment replay can remove a child copied from a removed parent entry. However, parent choice is refreshed from rolling PnL/ROI. The active assignment row's metrics are overwritten rather than journalled at every refresh, and historical enabled/paused/settings snapshots are absent. After correcting parent PnL, a child might have selected another parent; that full reselection path is not exactly reconstructible.

### Canonical storage and projections

`strategy_market_paper_runs` is unique on `(strategy_id, market_id)`. Actual and corrected rows cannot coexist under the same identity. Dashboard projections can be rebuilt from canonical data, but the processed outbox is deleted and is not an audit trail. Rewriting one run status is not a complete correction: orders, fills, positions, settlements, progression state, Child links, and projections form one dependent graph.

### Live history

A filled or settled Live order is an external fact. Changing `live_orders` locally cannot reverse the exchange fill and risks desynchronizing the local balance, settlement, shadow, and discrepancy records. The `2,094` pre-cutover Live-order records must first be separated by actual status; any filled/settled correction requires a counterfactual annotation/view, not historical cancellation.

## Recommended correction model

Do not mutate actual historical orders in place. Build a versioned counterfactual ledger with at least:

- source `strategy_id`, `market_id`, original run/order/fill IDs;
- algorithm version and deployed cutover;
- `actual_action` and `counterfactual_action` (`retain`, `would_skip`, `would_enter`);
- exact Max/Min boundary values and selected window;
- signal evidence tier (`persisted_exact`, `tick_reconstructed`, `unreplayable`);
- execution evidence tier (`actual_fill`, `matched_full_depth`, `modeled_top_of_book`, `unreplayable`);
- actual and counterfactual stake/fill/PnL, kept in separate columns;
- all assumptions, freshness, unmatched reason, and replay code/version.

Suggested reporting tiers:

1. **Exact correction** — persisted decision inputs and actual fill, or a separately validated full-depth match.
2. **Exact signal / modeled execution** — exact `would_enter`, but no exact historical FAK depth.
3. **Reconstructed signal / modeled execution** — missing diagnostics reconstructed from ticks with explicit non-bit-exact limitations.
4. **Unreplayable** — insufficient input; preserve the factual row and report the gap.

Only after this ledger is independently validated should the dashboard optionally expose a separate `Corrected v2 replay` view. The actual Paper and Live histories should remain immutable.

## Reproducibility and verification

All production queries used:

```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SET TRANSACTION READ ONLY;
SET LOCAL TIME ZONE 'UTC';
-- exact allowlist: strategy_id = ANY(@exact_ids)
-- exact legacy cutoff: entry_due_at_utc/created_at_utc < @cutoff
ROLLBACK;
```

Representative aggregation:

```sql
SELECT
    status,
    count(*) AS runs,
    count(*) FILTER (WHERE paper_order_id IS NOT NULL) AS with_order,
    count(*) FILTER (WHERE skip_diagnostics_json IS NOT NULL) AS with_skip_json
FROM strategy_market_paper_runs
WHERE strategy_id = ANY(@static_848_ids)
  AND entry_due_at_utc < TIMESTAMPTZ '2026-07-27T13:24:05.932282Z'
GROUP BY status;
```

The potential-add cohort was derived from exact migration-report rows containing `Optimized Average` and trigger `Down` or `Neutral`; the parser asserted exactly 192 unique IDs before querying.

Independent confirmations used:

- deployed runtime version and fresh heartbeat;
- exact inventory reflection and production ID presence;
- status-total and asset-total arithmetic;
- non-null raw decision count on all Paper orders;
- source call order showing signal evaluation before book lookup;
- persistence schemas and focused direct/Confirmed JSON samples;
- the prior production-backed ETH execution-coverage replay.

Several attempted broad JSON scans exceeded bounded read-only query timeouts and were cancelled. Narrowed family/cohort scans produced the reported coverage. No database row, service, order, strategy, or production setting was changed; every successful transaction was rolled back.
