## 2026-08-20 Isolate Paper Lifecycle From Position Marks

- Approved contract: `RC-20260820-paper-lifecycle-worker-isolation`, digest `sha256:08bd4998940c9e8c18e5d855610aa6128a3fde7a559217006aacf97822edcf41`.
- Implemented variant 1: Paper open-order lifecycle and whole-portfolio position mark refresh now run in separate hosted workers backed by one `PaperTradingProcessor` singleton.
- `ProcessOpenOrdersAsync` no longer performs mark refresh; the new mark worker preserves the existing calculations, cache-first/REST-fallback lookup, optimistic CAS, cache update, and error handling.
- Deterministic regressions prove a blocked mark pass neither overlaps its next pass nor delays `PaperTradingWorker` or terminal exact-family Maker-GTD expiry.
- Verification: isolation/Maker 34/34, focused 39/39, broader related 62/62, complete Debug solution build 0 errors / 126 existing warnings, `git diff --check` clean, requirement gate clean, independent semantic review PASS with no findings.
- Work remained in an isolated branch/worktree; unrelated dirty Historical Gross/Net and Progress-strategy changes were not touched. Production and external systems were not mutated.
