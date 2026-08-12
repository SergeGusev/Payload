## 2026-08-12T08:37:33Z
Request:
Я перезагрузил сервер, проверяй

Result:
Verified the new service start at `08:30:32.449789Z` through repeated read-only UTC snapshots on the exact server DB. Heartbeat, BTC/ETH/SOL references, and Polymarket WebSockets became fresh and advanced; the expected build was running without service-level error or DB lock waits. The full `08:35Z` five-minute cycle created 87 ordinary FAK Paper orders, all Filled, with p95 entry latency `0.387-1.210s`, zero late-after-market-end activity, and intact run/order/fill linkage. Since start the service created 154 Paper orders, 94 fills, and 470 settlements. Overdue Observed drained `4,708 -> 0`, overdue Entered `700 -> 231`, and expired Maker-GTD `1,210 -> 1,054`. Exact `8223/8224` post-start contracts, restart fences/no-backfill evidence, labels, linkage, and Paper-only isolation passed; Live/shadow/Child activity was zero. Dashboard and copied-performance processing resumed and queues declined, but Dashboard still lagged raw data materially with 106,525 events and 2,607 reconciliation rows pending. Stream errors stopped after the initial recovery interval; one isolated OKX timeout remained. Core runtime and Paper betting were restored, but projection and Maker backlogs were still catching up. No system or data changes were made by Codex.

## 2026-08-12T10:28:03.1736621Z
Request:
Скорректируй алгорим пересчёта обновлённого Net Pnl и Net ROI.

Пусть стратегии пересчитываются в порядке Gross PnP от самой успешной к самой неуспешной

Result:
Changed the retained historical pure-Paper FAK fee/net backfill from global chronological fill paging to stable strategy-first sweeps. Each sweep now derives the exact Dashboard lifetime Gross realized PnL directly from raw retained runs, skip rollups, fills, and mapped settlements, orders eligible strategies by Gross descending with strategy-ID tie-break, and finishes one strategy's eligible fill pages before starting the next. Cursor and repository contracts are strategy-bound, and invalid mixed pages or unordered/duplicate ranks fail closed. Transient fee lookups and conflicts continue to lower-ranked strategies and retry on a later freshly ranked sweep. Existing Gross values, fee/Net PnL/Net ROI formulas, cutoff, two-source allowlist, cadence, foreground yielding, and atomic apply rules remain unchanged. Focused tests passed 20 with one env-gated PostgreSQL test skipped; the broad non-stale suite passed 1,220 with 32 env-gated skips; the full solution built with zero errors. The full suite still has 121 catalog-dependent failures across three untouched stale test classes. Documentation and active context were updated. No database/runtime/trading/configuration mutation, schema change, or deployment was performed; the production ranking-query plan remains unverified without a test/production PostgreSQL scope.

## 2026-08-12T10:58:57.6132971Z
Request:
Выложил, проверяй

Result:
Verified production strictly read-only through `2026-08-12T10:58:30.496093Z`. The running service is exact build `28f4a60e0596e1e80b51162c15a36e616ccc132b`, started at `10:42:46.765117Z`, and remained `Running / Live` with an advancing heartbeat and empty service-level error. After the configured five-minute delay, the deployed historical backfill had written zero new provenance rows; the latest such fill remained from `2026-08-11T05:09:07.315354Z`, while an exact eligible `LegacyUnknown` BUY fill still exists. An independent execution of the exact production raw-Gross ranking SQL was canceled by its own 10-second timeout, so the rollout was not accepted as operational and no Net PnL/Net ROI recalculation attributable to it was advancing. Remote access to the authoritative server file log timed out, so the immediate worker state could not be distinguished between foreground-queue deferral and an attempted ranking timeout. No production, service, configuration, trading, database, or product-code state was changed.
