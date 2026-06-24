## 2026-06-20T07:16:36.1737967+00:00
Request:
Проверь на исторических данных за полгода новые Premarket стратегии как с текущими -30 секунд, так и до -5 секунд с шагом 5. Дай итоговую аналитику по потенциальной пригодности каждой из стратегий к дальнешему исследованию

Забыл уточнить - делай без данных Polymarket, только по валюте, считай, что всегда берём по ставке 0.5

Result:
Ran a currency-only ETHUSDT 1-second Binance backtest for ETH Down FAK Premarket-style fixed Down countertrend variants over 2025-12-20T00:00:00Z through 2026-06-20T00:00:00Z exclusive, assuming every qualifying entry buys at 0.5 and using no Polymarket data. Tested offsets -30/-25/-20/-15/-10/-5 seconds and thresholds 1..50 bps. Wrote full CSV, top ranking, monthly summary, and markdown report under `artifacts/premarket-backtest-2026-06-20`; all 300 combinations were positive in this simplified model, with strongest zones around -10s 40..42 bps and -5s 30..38 bps. No product code, PostgreSQL data, service state, live orders, balances, strategy flags, or secrets were changed.

## 2026-06-20T17:31:24.7155270+00:00
Request:
А ты можешь проверить на реальных premarket odds/liquidity ? Эти данные у тебя есть?

Result:
Answered that a full six-month real Polymarket premarket odds/liquidity validation is not currently possible from available local data. Existing odds archive tables capture active market windows, not historical pre-open books; the earlier ETH probe found zero pre-start rows. Only small June 18 ad hoc monitor samples exist locally, useful for sanity checks but not statistical validation. Recommended adding a dedicated pre-start order-book archiver for future validation.

## 2026-06-20T17:44:47.5527063Z
Request:
Ок, сделай тогда эти стратегии для боя. Имей в виду - боевая база сейчас тебе недоступна, сделай их пока чисто в сервисе, завтра занесём на бой.

Result:
Added the selected ETH Down FAK Premarket production-candidate variants in service/catalog/schema only: `-10s` thresholds `40..42` bps and `-5s` thresholds `30..38` bps. Existing `-30s` Premarket rows remain unchanged. Premarket previous-result sampling now follows each variant's entry offset and records matching sources such as `ReferencePricePremarketEndMinus10`; schema seeding inserts the new rows with `enabled=true`, `live_stakes=false`, and `paper_stake_amount=1.00`. Updated docs and focused tests. Verification passed with 5 focused tests and a Service build; production database, live orders, balances, service state, secrets, and strategy flags were not touched.
