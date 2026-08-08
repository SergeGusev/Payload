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

## 2026-08-07T23:32:30.8596844Z
Request:
Ок, пройдись по всем сделанным Paper стратегиям и, если они работали в режиме FAK, рассчитай их корректный Pnl и ROI с учётом комиссий

Делай так, чтобы исторические Paper ставки по итогу не отличались от того, что ставится прямо сейчас, с учётом комиссии

Result:
Выполнен строгий read-only production preview исторического FAK backfill. Доказанный cohort содержит `2,199,628` fills / `2,199,621` orders / `2,217` strategies / `30,658` uniquely mapped crypto markets и gross entry notional `$28,666,682.4052446693649144`; `2,198,742` pre-deploy fills остаются `LegacyUnknown`, `886` post-deploy fills уже `Calculated/Taker`. Из legacy scope `2,196,391` — direct/child pure-Paper FAK, ещё `2,351` — FAK Paper/Live-shadow; `865` GTD shadow fills исключены. Текущий `.07/e1/taker-only` schedule сильно подтверждён, но immutable as-of evidence для каждой старой сделки отсутствует, поэтому допустим только явно помеченный current-Paper-model backfill, не `VenueReported`. Production mutation остановлена до явного разрешения на maintenance stop/trigger guards; shadow cohort требует отдельного расширения на historical Live accounting и balance reconciliation. Никаких production writes, schema/service/trading actions или backfill не выполнялось.

## 2026-08-08T07:29:54.5242755+00:00
Request:
Не понимаю, зачем останавливать сервис. В новых ставках комиссия уже считается правильно, то есть нам не нужно работать на опережение.  Сделай отдельную задачу в сервисе, чтобы она потихонечку, не торопясь, в паузы, пересчитывала Paper стратегии с учётом комиссии. Пусть это займет неделю, например. Не пугает

Result:
Реализован отдельный online-worker для постепенного пересчёта `2,196,391` исторических pure-Paper FAK fills без остановки основного сервиса. Он уступает foreground-очередям, после пятиминутной задержки обрабатывает максимум `50` строк каждые `15s`, применяет тот же текущий fee calculator с явной provenance, а fee/net обновляет короткой атомарной CAS-транзакцией без изменения gross PnL. Добавлены keyset paging, concurrent partial indexes, защита projection queues от fee-only событий, idempotency/conflict handling, конфигурация, документация и тесты. Shadow/GTD/Maker/ambiguous rows исключены. Сборка прошла; focused suite `180 passed / 1 env-gated PostgreSQL integration skipped / 0 failed`; broader suite сохранил ранее существующие unrelated failures. Production deployment, DML/DDL и запуск backfill не выполнялись.

## 2026-08-08T08:23:09.2622073+00:00
Request:
Выложил, проверяй

Result:
Строго read-only проверен production build `1c4eda11` на `192.168.0.101/polycopytrader`. Сервис `Running/Live`, heartbeat свежий, оба новых partial index valid/ready/live, fee/net schema и projection guards развёрнуты. Historical worker после grace period пересчитал `260` строк: все `Calculated/Taker`, exact source/BUY/pre-cutoff, fee `$116.69602000`; независимая формула и fill→run→position→settlement identities дали ноль расхождений. `1,103/1,103` новых post-start fills также рассчитаны корректно. Из первых шести завершённых страниц `260/300` применены, а `40` безопасно оставлены `LegacyUnknown`; все `40` имеют доказанный конфликт старой terminal position/run/settlement chain, необъяснённых eligible-пропусков нет. Ноль финальных lock waiters/idle transactions и ноль fee/backfill-named persisted errors. Remote file logs/runtime overrides недоступны, поэтому фиксированный ETA не подтверждён. Codex не выполнял production writes, DDL, service actions или trading/API calls.
