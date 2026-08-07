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

## 2026-08-07T22:42:26.1271107+00:00
Request:
Но сервис-то надо перевыложить, чтобы по новому начало считать?

Result:
Подтверждено: да, commit/push меняют только исходный код. Для начала нового fee-accounting необходимо остановить, republish и запустить Windows Service из commit `b7b181ed`, затем проверить heartbeat version и новые schema columns. Вопрос не трактовался как разрешение на production deployment: сервис не перезапускался, production/schema не изменялись, historical backfill не выполнялся.

## 2026-08-07T22:57:34.5556103+00:00
Request:
Выложил, проверяй

Result:
Выполнена строгая read-only production-проверка deployed build `092b76c5` на `192.168.0.101/polycopytrader`. Heartbeat свежий, fee schema `45/45` корректна. На фиксированном canary `257/257` новых Paper fills имели `Calculated/Taker`, точный schedule `.07/1/takerOnly`, ненулевую fee и ноль независимых formula mismatches; суммы stored/recomputed fee совпали `$316.95486`. Все exact order/run/position links совпали. Позже `121/121` canary runs закрылись с сохранённой fee и точным `net=gross-fee`. Старая история осталась `LegacyUnknown`. Новых Live orders не было, поэтому production Live canary остаётся Unknown. Fee-related ошибок не найдено; отдельно замечены не связанные доказанно с fee ошибки ProjectionCycle/OKX/reference workers. Никаких production writes/restarts/backfill не выполнялось.
