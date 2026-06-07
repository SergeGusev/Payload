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
