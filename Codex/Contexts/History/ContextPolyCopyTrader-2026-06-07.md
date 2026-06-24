## 2026-06-07T07:18:24.2412466Z
Request:
Сделай Excel отчёт по живым стратегиям. Колонки - дни, строки - стратегии. С результатам по дням и стратегиями и общим результатом.

Result:
Created `outputs/live-strategies-report-20260607/LiveStrategiesDailyReport.xlsx` from read-only production PostgreSQL queries against current `live_stakes=true` strategies. The workbook contains daily UTC settlement columns, strategy rows, strategy totals, day totals, status context, and source settled Live orders. Verified the `.xlsx` package structure and opened it read-only through Excel COM.

## 2026-06-07T08:05:40.0078873Z
Request:
Поставь False в Enabled всем стратегиям, кроме ETH Up or Down 5m Down N bps Instant, ETH Up or Down 5m Up N bps Instant, BTC Up or Down 5m Down N bps Instant, SOL Up or Down 5m Down N bps Instant, SOL Up or Down 5m Down N bps Instant,
BTC Up or Down 5m Up N bps Instant, SOL Up or Down 5m Up N bps Instant

Result:
Updated production PostgreSQL `strategies.enabled` so every currently enabled strategy outside `BTC/ETH/SOL Up or Down 5m Up/Down N bps Instant` groups is now disabled. `1454` rows were changed to `enabled=false`; `300` exception rows remain enabled and `0` enabled rows remain outside the exception regex. Verified the six current Live strategies are still enabled and effective Live.

## 2026-06-07T08:18:49.0969881Z
Request:
Проверь объёмы рынков XRP Up or Down 5m, я ещё раз думаю, стоит ли туда лезть. И DOGE Up or Down 5m

Result:
Checked XRP and DOGE Up or Down 5m volumes/liquidity read-only through production PostgreSQL plus public Polymarket Gamma and CLOB order-book endpoints. XRP had `83.93` 6h volume and `3653.45` 24h volume with median 5m volume `0`; DOGE had `15.89` 6h volume and `1050.07` 24h volume with median 5m volume `0`. CLOB depth near balanced markets was thin, especially DOGE. No production rows, source files, configs, live flags, or orders were changed.

## 2026-06-07T08:33:21.7689773Z
Request:
Assess whether SOL Up or Down 5m was also a bad Live inclusion after the XRP/DOGE volume review.

Result:
Checked production read-only. Both SOL fixed 50 bps Instant strategies remain enabled/live and unpaused with `$1` live stake. Since Live enablement, SOL has `65` Live rows, `54` settled, `23W/31L`, cost `158.819707`, PnL `+0.600893`, ROI `0.3783%`; SOL Down is negative and SOL Up is positive. Paper over the same window is mildly positive (`+6.239793`, ROI `2.9593%`) but weaker than BTC/ETH. Recent SOL market volume remains thin: 6h volume `243.93`, only `11/72` nonzero markets, median 5m volume `0`; 24h volume `10289.74`, `33/288` nonzero, median `0`. Public CLOB book check showed `$1` entries are feasible in a balanced next SOL market, but SOL should be treated as marginal and not scaled without better volume/Live ROI evidence.

## 2026-06-07T09:15:34.1381706+00:00
Request:
Сделай аналоги ETH Up or Down 5m Down N bps Instant, ETH Up or Down 5m Up N bps Instant, BTC Up or Down 5m Down N bps Instant, SOL Up or Down 5m Down N bps Instant, SOL Up or Down 5m Down N bps Instant,
BTC Up or Down 5m Up N bps Instant, SOL Up or Down 5m Up N bps Instant для 15-минутных рынков. И переведи их значения для 50 bps сразу в Live аналогично тому, как сейчас работаем с 5-ти минутными рынками.

Result:
Added BTC/ETH/SOL 15-minute fixed `Up/Down 1..50 bps Instant` strategy variants, interval-aware market discovery/streak/odds lookup support, PostgreSQL seeds, tests, and documentation. Production PostgreSQL now has 300 new 15m fixed-bps rows enabled and exactly the six 50 bps target rows set to Live; current production service heartbeat is still old commit `d51a8f9c1dca...`, so deploy/restart of the new commit is needed before the running service can process the new 15m StrategyIds. Verification passed: full tests 605/605 and `git diff --check`.

## 2026-06-07T09:34:21.0199370+00:00
Request:
Выложил, проверяй

Result:
Verified production deployment read-only through `out\dbprobe` with host override `192.168.0.101`. `PolyCopyTrader.Service` is running commit `7d474f7f22a66f9...` in Live mode with fresh heartbeat and `last_error=null`. Production has 300 BTC/ETH/SOL 15m fixed-bps rows enabled and exactly six 50 bps target rows effective Live. 15m Gamma discovery and odds ticks are active after deploy; the first due 15m market created runs for all six Live strategies and skipped them because the previous 15m move was only about `5.6632 bps`, below the `50 bps` threshold. No target 15m Live orders have been created yet and no post-deploy `api_errors` were found.

## 2026-06-07T09:46:34.1797093+00:00
Request:
Оцени по объёму 15-ти минутные рынки для всех трёх валют, с которыми мы сейчас работаем.

Result:
Assessed BTC/ETH/SOL 15m Up/Down market volume read-only from production PostgreSQL Gamma-derived rows, grouping markets by slug timestamp. At DB time `2026-06-07T09:45:45Z`, BTC 15m had 6h volume `467.76` across `19/24` nonzero markets and 24h volume `85831.74` across `64/96`; ETH 15m had 6h `249.86` across `4/24` and 24h `21610.11` across `33/96`; SOL 15m had 6h `451.38` across `1/24` and 24h `5299.83` across `14/96`. Conclusion: BTC 15m is the only semi-usable 15m market by continuity, ETH 15m is sporadic/experimental, and SOL 15m should not be scaled.

## 2026-06-07T10:07:52.4521039Z
Request:
Ясно. Я погорячился. Давай убирать все 15 мин стратегии

Result:
Disabled all production 15-minute strategy rows through `out\dbprobe` with host override `192.168.0.101`: `540` total 15m rows now have `0` enabled, `0` Live, and `0` effective Live, with Live timestamps cleared. Verified the six BTC/ETH/SOL 5m 50 bps Live strategies remained enabled/effective Live. Updated seeds and documentation so BTC/ETH/SOL 15m fixed bps Instant rows and BTC 15m pre-open rows are disabled by default. Two 15m Live orders were created before the disable; ETH 15m Up 50 bps settled at `-3.00330000` realized PnL and SOL 15m Up 50 bps settled at `-3.00370000` realized PnL.

## 2026-06-07T13:14:05.4839153Z
Request:
Фифизчески убери из базы данных все 15-м стратегии и объединяющие их категории.

Result:
Physically deleted production 15m strategy rows and dependent strategy data through `out\dbprobe` with host override `192.168.0.101`: `540` strategies, `89,451` paper orders, `92,062` strategy runs, `7,136` paper fills, `2` live orders, `2` live-shadow decisions, `99,179` signals, and 15m synthetic-wallet paper aggregate rows. Verified `0` remaining 15m strategy rows, `0` 15m strategy-wallet signals/orders/positions/settlements/performance rows, and `0` 15m run rows; 5m fixed rows and six 5m Live 50 bps strategies remained intact. Added a production guard trigger for the currently deployed old binary so missing 15m strategy IDs no longer create FK errors, and updated source schema/processor/docs so 15m rows are no longer seeded and missing strategy rows are treated as disabled. Verification passed: focused tests `224/224` and full tests `605/605`.
## 2026-06-07T16:05:09.3737065+00:00
Request:
В какой стратегии ты увидел успешный заказ?

Result:
Queried production PostgreSQL read-only and confirmed the successful Live order referenced after the rejection burst was `SOL Up or Down 5m Down 50 bps Instant`, created `2026-06-07T15:40:07Z`, status `Matched`, response `matched`, outcome `Down`, price `0.55`, size `7.28`, notional `4.004`. No production rows/configs/orders/secrets/processes were changed.

## 2026-06-07T15:50:55.6929729+00:00
Request:
Проверь, почему все последние Live ставки имеют статус Rejected

Result:
Queried production PostgreSQL read-only through `out\dbprobe` with host override `192.168.0.101`. Found a limited Live rejection burst from `2026-06-07T14:25:51Z` to `2026-06-07T14:35:18Z`: 7 `Rejected` rows, all CLOB `HTTP 400`; 6 returned `the order signer address has to be the address of the API KEY`, and 1 returned `invalid POLY_1271 signature: wallet state warming`. Confirmed service heartbeat was fresh and no production rows/configs/orders/secrets/processes were changed; a later Live order at `2026-06-07T15:40:07Z` was already `Matched`, so the rejection was not still continuous at the snapshot.

## 2026-06-07T16:19:09.6278907+00:00
Request:
Пройдись по истории рынка всей истории рынка BTC Up or Down 5m Down , за последний месяц и скажи, чего было больше - Up или Down ?

Result:
Counted BTC Up or Down 5m resolved market winners read-only from Polymarket Gamma by enumerating `btc-updown-5m-<unix>` slugs. Production PostgreSQL was checked as a sanity subset but had only `785` closed BTC 5m rows in the requested month, so the full result used Gamma API batches of `100` to avoid endpoint truncation. Rolling-month range `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z` returned `8928` resolved rows: `Up=4436`, `Down=4492`, `Unknown=0`; `Down` led by `56` markets. Calendar-date check `2026-05-07T00:00:00Z..2026-06-08T00:00:00Z` returned `Up=4525`, `Down=4597`, so `Down` also led there.

## 2026-06-07T16:36:25.6854344+00:00
Request:
Сделай аналогичный анализ для ETH и SOL

Result:
Counted ETH and SOL Up or Down 5m resolved market winners read-only from Polymarket Gamma using `eth-updown-5m-<unix>` and `sol-updown-5m-<unix>` slugs, batch size `100`, and the same winner rule as the BTC count. Rolling-month range `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`: ETH had `8928` resolved rows with `Up=4387`, `Down=4541` (`Down` +154); SOL had `8928` resolved rows with `Up=4408`, `Down=4520` (`Down` +112). Calendar-date check `2026-05-07T00:00:00Z..2026-06-08T00:00:00Z` also had `Down` ahead for both assets. Production PostgreSQL was checked but has `0` closed ETH/SOL Gamma rows, so the count used live Gamma API data.

## 2026-06-07T16:43:54.7133722+00:00
Request:
Посчитай по всем троим валютам, как часто 1. После Down был Up. 2. После Up был Down. Интересует процент таких случаев.

Result:
Counted adjacent resolved 5-minute market transitions read-only from Polymarket Gamma for BTC, ETH, and SOL over `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`. Each asset had `8928` resolved markets and `8927` valid adjacent transitions. BTC: `Down->Up=2218/4492` (`49.3767%`), `Up->Down=2217/4435` (`49.9887%`). ETH: `Down->Up=2312/4540` (`50.9251%`), `Up->Down=2312/4387` (`52.7012%`). SOL: `Down->Up=2240/4519` (`49.5685%`), `Up->Down=2241/4408` (`50.8394%`). Combined: `Down->Up=6770/13551` (`49.9594%`), `Up->Down=6770/13230` (`51.1716%`). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T16:51:16.9288795+00:00
Request:
Теперь то же самое посчитай, но после 1. Двух подряд Down сколько раз был Up 2. Сколько после двух подряд Up был Down. Также в процентах

Result:
Counted overlapping adjacent triples of resolved 5-minute markets read-only from Polymarket Gamma for BTC, ETH, and SOL over `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`. Each asset had `8928` resolved markets and `8926` valid triples. BTC: after `Down,Down`, `Up=1150/2274` (`50.5717%`); after `Up,Up`, `Down=1121/2218` (`50.5410%`). ETH: after `Down,Down`, `Up=1182/2227` (`53.0759%`); after `Up,Up`, `Down=1063/2075` (`51.2289%`). SOL: after `Down,Down`, `Up=1170/2278` (`51.3608%`); after `Up,Up`, `Down=1106/2167` (`51.0383%`). Combined: after `Down,Down`, `Up=3502/6779` (`51.6595%`); after `Up,Up`, `Down=3290/6460` (`50.9288%`). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T16:59:53.4954622+00:00
Request:
Подсчитай то же самое после трёх подряд

Result:
Counted overlapping adjacent quadruples of resolved 5-minute markets read-only from Polymarket Gamma for BTC, ETH, and SOL over `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`. Each asset had `8928` resolved markets and `8925` valid quadruples. BTC: after `Down,Down,Down`, `Up=572/1124` (`50.8897%`); after `Up,Up,Up`, `Down=561/1097` (`51.1395%`). ETH: after `Down,Down,Down`, `Up=571/1045` (`54.6411%`); after `Up,Up,Up`, `Down=526/1012` (`51.9763%`). SOL: after `Down,Down,Down`, `Up=569/1108` (`51.3538%`); after `Up,Up,Up`, `Down=540/1061` (`50.8954%`). Combined: after `Down,Down,Down`, `Up=1712/3277` (`52.2429%`); after `Up,Up,Up`, `Down=1627/3170` (`51.3249%`). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T17:04:45.1389835+00:00
Request:
А после четырёх?

Result:
Counted overlapping adjacent quintuples of resolved 5-minute markets read-only from Polymarket Gamma for BTC, ETH, and SOL over `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`. Each asset had `8928` resolved markets and `8924` valid quintuples. BTC: after `Down,Down,Down,Down`, `Up=292/552` (`52.8986%`); after `Up,Up,Up,Up`, `Down=255/536` (`47.5746%`). ETH: after `Down,Down,Down,Down`, `Up=260/474` (`54.8523%`); after `Up,Up,Up,Up`, `Down=259/486` (`53.2922%`). SOL: after `Down,Down,Down,Down`, `Up=279/539` (`51.7625%`); after `Up,Up,Up,Up`, `Down=277/521` (`53.1670%`). Combined: after `Down,Down,Down,Down`, `Up=831/1565` (`53.0990%`); after `Up,Up,Up,Up`, `Down=791/1543` (`51.2638%`). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T17:09:44.3300777+00:00
Request:
А после пяти?

Result:
Counted overlapping adjacent sextuples of resolved 5-minute markets read-only from Polymarket Gamma for BTC, ETH, and SOL over `2026-05-07T16:10:00Z..2026-06-07T16:10:00Z`. Each asset had `8928` resolved markets and `8923` valid sextuples. BTC: after five `Down`, `Up=133/260` (`51.1538%`); after five `Up`, `Down=130/281` (`46.2633%`). ETH: after five `Down`, `Up=126/214` (`58.8785%`); after five `Up`, `Down=123/227` (`54.1850%`). SOL: after five `Down`, `Up=130/260` (`50.0000%`); after five `Up`, `Down=128/244` (`52.4590%`). Combined: after five `Down`, `Up=389/734` (`52.9973%`); after five `Up`, `Down=381/752` (`50.6649%`). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T18:20:00.1543549+00:00
Request:
За какой период максимально ты можешь получить данные из API ?

Result:
Checked Polymarket official market-data docs and live Gamma API read-only to determine the maximum currently available history range for BTC/ETH/SOL Up or Down 5m markets. Confirmed `closed=true` is required for closed historical data. `events/pagination?closed=true&limit=1&series_slug=<asset>-up-or-down-5m&order=startTime` showed the first closed event for BTC, ETH, and SOL is `2025-12-18T04:25:00Z..04:30:00Z` with slug suffix `1766031900`; the latest closed event observed was `2026-06-07T18:10:00Z..18:15:00Z` with slug suffix `1780855800`. The inclusive first-to-latest span is `171.5729` days and `49414` possible 5-minute slots. `events/pagination.totalResults` reported BTC `44758`, ETH `42796`, and SOL `42800` closed events, so the historical series are not fully continuous. No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T18:59:11.6566821+00:00
Request:
Ок, возьми данные за последние 2 месяца по каждой валюте и сделай следующее: 1. Начинай считать отдельно результаты Up в UpCount и Результаты Down в DownCount. 2. Вычисляй их разницу Diff = UpCount - DownCount. 3. Веди статистику
минимального и максимального Diff за 2 месяца. 4. Веди статистику случаев, когда Diff = 0

Result:
Queried Polymarket Gamma API read-only through `/events/keyset` for BTC/ETH/SOL Up or Down 5m over `2026-04-07T18:35:00Z..2026-06-07T18:35:00Z`, using `closed=true`, `series_slug=<asset>-up-or-down-5m`, `start_time_min`, `start_time_max`, and `after_cursor` pagination. Counted events chronologically, with `Up` as `+1` and `Down` as `-1`; `Diff=0` hits were counted after each resolved market, with the initial zero reported separately. BTC: `17564` resolved events, `UpCount=8797`, `DownCount=8767`, final `Diff=30`, min `Diff=-5`, max `Diff=148`, zero hits `17` (`18` including initial). ETH: `17562` resolved events, `UpCount=8722`, `DownCount=8840`, final `Diff=-118`, min `Diff=-140`, max `Diff=106`, zero hits `208` (`209` including initial). SOL: `17563` resolved events, `UpCount=8783`, `DownCount=8780`, final `Diff=3`, min `Diff=-16`, max `Diff=157`, zero hits `52` (`53` including initial). No source code, production rows, configs, service processes, or secrets were changed.

## 2026-06-07T19:41:31.7285023+00:00
Request:
Для каждой валюты создай 2 вида виртуальных (то есть без записи в базу, только в памяти) стратегий: Curr UP N Diff и Curr Down N Diff, где N меняется от 1 до 150, а Curr принимает значения BTC, ETH, SOL.

Стратегии основаны на том же принципе Diff, который мы использовали в рассчёте, только их абсолютное значение (Up использует то же Diff, а Down - diff со знаком минус). Буква N в названии стратегии означает
как раз этот Diff.

Стратегии работают таким образом - если текущий Diff равен значению N в названии, то делается ставка на противоположный исход по цене 0.5

Например, UpCount = 100, DownCount = 90, Diff = 10.  Тогда стратегии от Curr 1 Up Diff до Curr 10 Up Diff ставят на Down, все остальные стратегии не ставят.

Другой пример: UpCount = 90, DownCount = 100, Diff = -10.  Тогда стратегии от Curr 1 Down Diff до Curr 10 Down Diff ставят на Up, все остальные стратегии не ставят.

В общем, аналог с bps в текущих живых стратегиях.

Резутат - таблица с результатами по каждой из стратегий.

Result:
Backtested virtual in-memory BTC/ETH/SOL `Up/Down N Diff` strategies for `N=1..150` over the prior two-month window `2026-04-07T18:35:00Z..2026-06-07T18:35:00Z`. No database strategy rows were created and no production rows, source code, configs, service processes, or secrets were changed. The user's examples were implemented as threshold logic: `Curr Up N Diff` triggers when pre-market `Diff >= N` and bets `Down`; `Curr Down N Diff` triggers when pre-market `-Diff >= N` and bets `Up`. Each trigger used a virtual `$1` stake at price `0.5`, so wins are `+$1`, losses are `-$1`. Full `900`-row CSV table: `artifacts/diff-strategies-2026-06-07/crypto-5m-diff-strategy-results.csv`; summary CSV: `crypto-5m-diff-strategy-summary.csv`; top-30 CSV: `crypto-5m-diff-strategy-top-30.csv`. Inputs: BTC `17564` resolved events, ETH `17562`, SOL `17563`, `0` unknown. Best min-100-bet rows: ETH `ETH Up 101 Diff` `154` bets, `93/61`, `+32` PnL, `20.7792%` ROI; BTC `BTC Up 142 Diff` `146` bets, `88/58`, `+30`, `20.5479%`; ETH `ETH Down 134 Diff` `129` bets, `77/52`, `+25`, `19.3798%`; SOL `SOL Down 10 Diff` `105` bets, `60/45`, `+15`, `14.2857%`. Best absolute PnL: BTC `BTC Up 114 Diff` `5164` bets, `2677/2487`, `+190`, `3.6793%` ROI.

## 2026-06-07T19:46:32.5119817+00:00
Request:
Какие твои рекомендации по созданию стратегий?

Result:
Reviewed the virtual Diff strategy backtest CSV and recommended a conservative Paper-only rollout instead of creating all 900 strategies. Recommended candidate clusters with reasonable sample/PnL balance: BTC `Up 110..120 Diff` with emphasis on `BTC Up 114 Diff`; SOL `Up 63..64 Diff` and `SOL Up 110..111 Diff`; ETH `Up 100..101 Diff` only as a small experimental Paper set. Recommended avoiding low-sample 100% ROI extremes such as BTC `Up 148`, ETH `Down 140`, and SOL `Down 16`, and avoiding the negative ETH `Down 25..40 Diff` cluster. Recommended guardrails: Paper only, no Live, min historical bets threshold, rolling out-of-sample validation, max active Diff-strategy entries, one-side exposure caps, spread/liquidity/price filters, and explicit rejection logging. No source code, production rows, configs, service processes, or secrets were changed.
## 2026-06-07T20:21:43.8323269+00:00
Request:
1. Давай сделаем с диапазонами 1-150 с шагом 5 2. При старте сервиса читаем данные за последний месяц и ориентируемся на них, потом по мере появления новых результатов корректируем счётчики. 3. Для каждой группы стратегий
 делаем свою категорию в Dashboard. 4. Пока все стратегии работают только в Paper режиме.

Result:
Implemented Paper-only BTC/ETH/SOL Diff strategy variants for thresholds 5..150 step 5, one-month in-memory Gamma bootstrap/catch-up, Dashboard Diff Up/Down grouping, PostgreSQL seeding, docs, and regression tests. Verification passed: focused Diff/category tests 14/14, full test suite 607/607, and `git diff --check` with only CRLF warnings. Commit/push was not performed because the worktree already contains unrelated pre-existing modifications, including in files touched by this task.
## 2026-06-07T20:36:46.0681594Z
Request:
Изменяем только что созданные Diff  стратегии - добавляем к каждой в название Instant и ставку вычисляем также, как для текущих Bsp Instant стратегий

Result:
Changed the newly created BTC/ETH/SOL Diff strategy variants to `Diff Instant`: codes now include `_instant`, names include `Instant`, seed descriptions use executable ask-depth pricing, and the processor routes `DiffCounterTrend` through the shared Instant opening-limit pricing/sizing path instead of fixed `0.50`. Diff strategies remain Paper-only and excluded from Paper/Live-shadow placement even if Live is enabled in the Dashboard. Updated tests and docs; focused Diff/category tests passed 14/14, full test project passed 607/607, and `git diff --check` passed with CRLF warnings only. Commit/push was not performed because the worktree already contains unrelated pre-existing tracked and untracked changes.
## 2026-06-07T20:51:19.6039903Z
Request:
Выложил, следи

Result:
Monitored production read-only through PostgreSQL because local IPC `127.0.0.1:5118` was not reachable from this machine. The service restarted at `2026-06-07T20:43:48Z`, is `Running` in `Live` mode with fresh heartbeat and `last_error=null`, and after the current start there were `0` API errors and `0` Live orders while Paper activity continued. However, the heartbeat version still reports `info=1.0.0+7d474f7f22a66f9ad61632e0688f1d9b0dc2d8af`, and production has `0` Diff or Diff Instant strategy rows, so the deployed binary is not the newly changed Diff Instant build and the new seed did not run. The six existing 5m `50 bps Instant` Live strategies remain enabled/live/not paused/not auto-paused. A pre-restart FK error burst for missing strategy id `b7c50005-0000-4000-8033-000000021049` was visible before the successful restart, but no such errors appeared after the current start. No production rows, strategy flags, service state, orders, cancels, source files, or configs were changed.

## 2026-06-07T21:22:28.9242309Z
Request:
Перевыложил, проверяй

Result:
Rechecked production read-only after the second deploy. The service restarted at `2026-06-07T20:56:15Z`, heartbeat was fresh in `Live` mode with `last_error=null`, and production had the expected `180` enabled Diff Instant strategy rows (`60` each for BTC/ETH/SOL), all Paper-only with `live_stakes=false`; no Live orders were created after restart and the existing six `50 bps Instant` Live strategies remained enabled/live/not paused/not auto-paused. Monitoring found the new Diff strategies were failing at runtime: Gamma closed-history fetches returned `HTTP 422` `invalid time range`, causing repeated API errors in `GetClosedMarketsBySeriesSlug`, `PlaceDueEntry`, and the worker cycle, with Diff runs skipping instead of entering. Latest read-only snapshot at heartbeat `2026-06-07T21:24:17Z`: `0` Live orders, `652` Paper orders, and `4680` API errors since start; error groups were `PolymarketGammaClient/GetClosedMarketsBySeriesSlug=2377`, `BtcUpDown5mPaperStrategyProcessor/PlaceDueEntry=1696`, and `BtcUpDown5mPaperStrategyWorker/Cycle=680`; Diff runs were `Skipped market_already_ended=1260` and `Observed=360`. Implemented a local fix in `BtcUpDown5mPaperStrategyProcessor`: clamp Diff history ranges to the last actually closed 5m window, fail closed on history-fetch errors, apply a one-minute per-asset backoff, and add diagnostics for history-fetch failures/stale counters. Added regression tests for future-market clamping and history-fetch backoff. Verification passed: focused Diff/category tests 16/16, full test project 609/609, and `git diff --check` with CRLF warnings only. Production still needs another redeploy to pick up this fix.
