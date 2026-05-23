## 2026-05-23T21:27:02.2699098+00:00
Request:
Сделай стратегии Middle для ETH и SOL аналогично BTC

Result:
Added ETH/SOL Middle strategy families analogous to BTC, including base, bps 1..100, Instant, Revert, and Revert Instant variants. Extended BinanceCryptoReference with per-asset rolling mean snapshots, routed Middle decisions through BTC or crypto reference sources, seeded PostgreSQL strategy rows, updated README/config docs, and covered the change with strategy/config/schema tests. Verification passed: focused tests 206/206, full `dotnet test PolyCopyTrader.sln --no-restore` 549/549, and `git diff --check`.
