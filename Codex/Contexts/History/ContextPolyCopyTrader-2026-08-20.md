## 2026-08-20 Isolate Paper Lifecycle From Position Marks

- Approved contract: `RC-20260820-paper-lifecycle-worker-isolation`, digest `sha256:08bd4998940c9e8c18e5d855610aa6128a3fde7a559217006aacf97822edcf41`.
- Implemented variant 1: Paper open-order lifecycle and whole-portfolio position mark refresh now run in separate hosted workers backed by one `PaperTradingProcessor` singleton.
- `ProcessOpenOrdersAsync` no longer performs mark refresh; the new mark worker preserves the existing calculations, cache-first/REST-fallback lookup, optimistic CAS, cache update, and error handling.
- Deterministic regressions prove a blocked mark pass neither overlaps its next pass nor delays `PaperTradingWorker` or terminal exact-family Maker-GTD expiry.
- Verification: isolation/Maker 34/34, focused 39/39, broader related 62/62, complete Debug solution build 0 errors / 126 existing warnings, `git diff --check` clean, requirement gate clean, independent semantic review PASS with no findings.
- Work remained in an isolated branch/worktree; unrelated dirty Historical Gross/Net and Progress-strategy changes were not touched. Production and external systems were not mutated.

## 2026-08-20T08:40:59.6811970Z
Request:
Выложил, проверяй

Result:
Verified exact deployed build `6ceddfcaed35fee713c2a8ab4c2d63cb5e2d8365` read-only on production. Service, heartbeat, BTC/ETH/SOL references, current Polymarket WebSockets, position marks, betting, linkage, latency, and active backlogs were healthy; the `08:40Z` cycle had 302 orders with zero linkage/fill defects or late entries. Four exact ETH Maker-GTD orders eventually expired automatically but `239–254s` late with `maker_gtd_market_data_apply_failed`, so lifecycle isolation is active but expiry latency remains a warning. Five copied-performance stream errors stopped by `08:37:02Z` and refreshes resumed, while a 555-row queue remained. No production mutation was made.

## 2026-08-20T09:08:31.2331020Z
Request:
То есть сейчас какой статус?

Result:
Current read-only production recheck found core Service, ordinary BTC/ETH/SOL Paper betting, references, WebSockets, DB locks, and position marks healthy, but exact ETH Maker-GTD lifecycle not healthy: 15 Pending/Resting orders were overdue across expiries `08:49Z..09:04Z`, oldest by `1,171.456s`, and the backlog was not draining. No persisted lifecycle/mark worker error explained the delay; the decisive deferral state is in-memory/log-only. One further copied-performance stream error occurred at `09:05:32Z`. No production mutation was made.

## 2026-08-20T09:31:45.3424171+00:00
Request:
Делай доработку

Result:
Traced the exact deployed Maker-GTD receipt/expiry admission path and identified a source-level starvation mechanism: expiry returns null whenever any receipt is active and has no priority over later receipts. Drafted and mechanically validated `RC-20260820-maker-expiry-admission-fairness` at digest `sha256:3377cd58a796cb051825a438376467b63fad572807e15f22d43bc96943394efc`. The proposed behavior waits for already active receipts, prevents later receipts from overtaking expiry, and preserves every existing evidence/outstanding-update/atomic gate without adding a timeout or dropping events. Product and production state were not changed; implementation awaits exact approval.

## 2026-08-20T09:34:41.3578263+00:00
Request:
APPROVE RC-20260820-maker-expiry-admission-fairness sha256:3377cd58a796cb051825a438376467b63fad572807e15f22d43bc96943394efc

Result:
Recorded exact approval for `RC-20260820-maker-expiry-admission-fairness` without changing its semantic payload. The approved contract must now be mechanically validated and committed as the parent revision before any product edit.

## 2026-08-20T09:45:33.1694981+00:00
Request:
APPROVE RC-20260820-maker-expiry-admission-fairness sha256:3377cd58a796cb051825a438376467b63fad572807e15f22d43bc96943394efc

Result:
Implemented the approved starvation-free Maker-GTD expiry admission fence. Active receipts drain first, later receipts cannot overtake expiry, cancellation recovers, and the existing outstanding-update/evidence/continuity/atomic rules remain unchanged. Final focused tests passed 58/58; the full Debug build completed with 0 errors and 126 existing warnings; independent semantic review passed with no findings. No production mutation occurred; final gated commit and push remain.

## 2026-08-20T11:04:28.8727750+00:00
Request:
Выложил, проверяй

Result:
Verified exact deployed build `4934b60f216ad4835d4cdde1c783c81de9edfa12` read-only. Service, heartbeat, PostgreSQL locks, BTC/ETH/SOL references, current Polymarket WebSockets, ordinary Paper entries, linkage, settlements, and position marks were healthy; no Live/shadow activity or enabled overdue run backlog existed. Exact Maker open/overdue was 0/0, but the sole new terminal cohort still reproduced a material delay: six orders expired at `10:24:00Z` and terminalized together at `10:28:44.329197Z` (`284.329s`) as `maker_gtd_evidence_unavailable / maker_gtd_market_data_apply_failed`, with failure evidence received at `10:19:33.212579Z`. Therefore the deployment removed the current open backlog but did not prove timely expiry; the remaining in-memory long-receipt versus outstanding-side-effect cause is not persisted. No production mutation occurred.

## 2026-08-20T11:10:04.4814581+00:00
Request:
Ок, делай доработку

Result:
Traced the remaining Maker-GTD terminal-delay path without mutating product or production. Receipt admission is short and ends after frame parsing/enqueue; the existing expiry path instead releases its fair admission whenever any accepted eligible pre-expiry side-effect update remains behind the single global worker. Drafted and mechanically validated `RC-20260820-maker-expiry-side-effect-priority` at digest `sha256:97b9af821cacc2469010ab121145af731ccae23d3938f1d88e3a2acabd04424c` to prioritize and await that exact accepted work without dropping events, changing fill semantics, or rewriting the six terminal production rows. Product edits await exact approval.

## 2026-08-20T11:34:53.1374804+00:00
Request:
APPROVE RC-20260820-maker-expiry-side-effect-priority sha256:97b9af821cacc2469010ab121145af731ccae23d3938f1d88e3a2acabd04424c

Result:
Recorded exact approval for `RC-20260820-maker-expiry-side-effect-priority` without changing its semantic payload. The approved contract must now be mechanically validated and committed as the parent revision before product edits.

## 2026-08-20T11:51:22.2527628Z
Request:
APPROVE RC-20260820-maker-expiry-side-effect-priority sha256:97b9af821cacc2469010ab121145af731ccae23d3938f1d88e3a2acabd04424c

Result:
Implemented the approved priority drain for accepted pre-expiry exact ETH Maker-GTD side-effect updates. The affected asset is prioritized without breaking same-asset FIFO or interrupting current in-flight work; expiry retains exclusive admission, reloads persisted state after the drain, and reaches the existing fill-or-terminal path in the same lifecycle pass. Final focused tests passed 80/80; the complete Debug build had 0 errors and 126 existing warnings; independent semantic review passed with no findings. No production mutation or deployment occurred; final gated commit and push remain.

## 2026-08-20T12:47:53.8021330Z
Request:
Выложил, проверяй

Result:
Verified exact production build `e01fdd92074f381514cbc525c69f89019eab57eb` read-only. Service, heartbeat, PostgreSQL locks, BTC/ETH/SOL references, current Polymarket WebSockets, Paper entries, linkage, latency, settlements, and position marks were healthy; 685 post-start orders had zero linkage/fill defects or late entries, and all enabled lifecycle backlogs cleared normally. Exact Maker inventory was 28/28 with no Live/open/overdue orders, but six post-start cycles produced 168 threshold skips and zero Maker orders, so the deployed code is confirmed while the new priority-drain expiry path has not yet been exercised by a real order. Warning-only diagnostics were two copied-performance stream errors and two isolated OKX two-second HTTP timeouts; processing remained active and no OKX-related strategy skip was persisted. No production mutation occurred.
