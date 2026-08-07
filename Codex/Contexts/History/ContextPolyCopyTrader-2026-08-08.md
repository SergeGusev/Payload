## 2026-08-07T21:05:36.8557781+00:00
Request:
Какие ещё есть особенности Paper стратегий, отличающие их от Live, которые могут повлиять на достоверность?

Result:
Выполнен строго read-only аудит текущих BTC/ETH/SOL Up/Down 5m Paper/Live путей по deployed source, production aggregates, schema/tests, parity contract и official Polymarket lifecycle. Кроме ранее найденного пропуска комиссий, подтверждены: `14.968%` Paper-estimated fills без Live match в сопоставимом shadow cohort; материальная разница Live/Paper цен и задержка; отсутствие Live-only rejection gates в pure Paper; повторное использование одной ликвидности многими Paper strategies; отсутствие per-run code/config version; смешение simulated Paper и actual Live-shadow в lifetime Dashboard; отсутствие exact per-trade/finality reconciliation; и critical Live risk defect, при котором Matched-but-unsettled positions выпадают из capital reservation/exposure. Точная ETH neutral 2bps стратегия имела zero Live orders, поэтому её `2,320` Paper entries не проверяют Live fill/rejection/slippage. Никаких production, strategy, order, service, schema, configuration или product-code changes не выполнялось.

## 2026-08-07T22:37:31.8352013+00:00
Request:
Добавь вычисление комиссии для всех стратегий. Причём добавь так, чтобы можно было отличить ставки с учётом комиссии от ставок без учёта. Потому что потом я попрошу пересчитать все стратегии

Result:
Добавлен единый forward-looking fee-accounting для всех текущих Paper и Live execution paths с market-specific CLOB V2 schedule, явными Maker/Taker/Unknown roles и статусами `LegacyUnknown`, `CalculationUnavailable`, `Calculated`, `VenueReported`, `PartiallyCalculated`. Исторические строки не пересчитывались: они остаются `LegacyUnknown`, fee хранится отдельно от gross PnL, а nullable net PnL появляется только при полной fee coverage. Dashboard/CSV показывают fee/status/net; Live balance ждёт достоверный net, gross ROI исключает fee из denominator. Production/deployment/backfill не выполнялись. Финальная сборка прошла; целевой suite `222/222` green. Broad suite остаётся не green из-за ранее существующих retired-catalog/path-sensitive тестов и зафиксирован без маскировки.
