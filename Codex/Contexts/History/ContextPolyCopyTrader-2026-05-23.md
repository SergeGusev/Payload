## 2026-05-23T09:20:39.1783837Z
Request:
Сдеалай Skip стратегии для ETH Up or Down 5m и SOL Up or Down 5m по аналогии со Skip стратегиями BTC Up or Down 5m кроме Revert стратегий

Result:
Added ETH/SOL non-Revert Skip strategy variants (`Skip 1..5`, `Skip bps 1..50`, and `Skip bps 1..50 Instant`), seeded them in PostgreSQL, extended processor/storage logic to use ETH/SOL close-book markets and crypto odds ticks, updated tests and docs, and verified focused and full test suites plus `git diff --check`.
