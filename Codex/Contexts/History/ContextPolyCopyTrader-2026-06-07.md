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
