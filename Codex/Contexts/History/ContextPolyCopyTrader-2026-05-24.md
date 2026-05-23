## 2026-05-23T21:27:02.2699098+00:00
Request:
Сделай стратегии Middle для ETH и SOL аналогично BTC

Result:
Added ETH/SOL Middle strategy families analogous to BTC, including base, bps 1..100, Instant, Revert, and Revert Instant variants. Extended BinanceCryptoReference with per-asset rolling mean snapshots, routed Middle decisions through BTC or crypto reference sources, seeded PostgreSQL strategy rows, updated README/config docs, and covered the change with strategy/config/schema tests. Verification passed: focused tests 206/206, full `dotnet test PolyCopyTrader.sln --no-restore` 549/549, and `git diff --check`.

## 2026-05-23T22:20:01.9190311+00:00
Request:
Production deployment was completed; verify it.

Result:
Verified production read-only after deploying ETH/SOL Middle strategies. `PolyCopyTrader.Service` is running commit `e12f1fed595d0bfb3a2aabb491304f9461fc700f` in Live mode with a fresh heartbeat and null `last_error`. Production contains all ETH/SOL Middle rows, 402 per asset and 804 total, enabled but Paper-only with no live stakes. Found an operational issue: ETH/SOL Middle runs are observed for current markets, but due windows checked so far skipped all 402 ETH and 402 SOL rows with `entry_due_expired`, and there are no ETH/SOL Middle paper orders. Likely cause is the large immediate-entry due set exceeding the configured 10-second entry grace with `MaxEntriesPerCycle=250`; Middle variants are not allowed after grace. No production rows were changed.
