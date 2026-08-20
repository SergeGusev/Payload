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
