## 2026-07-21T05:21:27.3445662+00:00
Request:
Сделай Excel отчёт по Child и Child ROI стратегиям по всем трём валютам. На одной вкладке.

Столбцы - стратегии с самым высоким Pnl в рамках валюты и типа, всего получится шесть стратегий, в отчете их нужно сгруппировать от самого низкого Pnl к самому высокому.

В строках - даты
В ячейках - Paper Pnl за эту дату. Отрицательные значения выделяются красным на белом фоне со знаком минус.

Последний столбец - сумма по дню, последняя строчка - сумма по категории. На их пересечении - общий Total

Первую строчку и первый столбец закрепи, чтобы не пропадали при скролллинге

Result:
Generated and fully verified a fresh one-sheet workbook from a forced read-only production snapshot through `2026-07-21T05:16:05.818012Z`. It contains the six independently reconciled unique BTC/ETH/SOL x Child/Child ROI winners ordered by total PnL ascending, 14 UTC dates, red-on-white negative values with minus signs, formula-driven daily/category/grand totals, and frozen first row plus first column. Excel and artifact-tool checks found zero formula errors and reconciled grand Total to `$1,588.07027893`; the visually inspected workbook SHA-256 is `7FBAB3FFFA00C481B760F942AED78D5ECDC8179F6CF50037FF19547FE38FBDA9`.

## 2026-07-21T07:22:56.7640730+00:00
Request:
Проверь, всё ли нормально с нашими ставками, есть ли проблемы и задержки, нужно ли что-то доработать

Result:
Production was audited through forced read-only transactions. The core path is healthy: 84,565 Paper bets in 24h reconciled one-to-one across runs, orders, and fills with no backlog or linkage defects; entry latency was p50 0.554s and p95 2.193s, all six recent Live bets settled normally, the service and market feeds were current, and all 90,950 measured strategy stages succeeded. Two required improvements were proved: all 16 BTC Futures Basis strategies have been blocked since 2026-07-19 by an unquoted OKX expiry contract, and ten enabled ETH Down legacy thresholds 55..100 are absent from the processor registry and never run. Secondary monitoring/index/cleanup and rare SOL tail-latency work were also identified; production was not changed.

## 2026-07-21T07:31:47.2082054+00:00
Request:
Поясни, что такое "выбранный OKX-контракт BTC-USD_UM-260731". Я думал, мы каждые 5 минут подбираем новый контракт

Result:
Clarified from the deployed selection path that every five-minute decision does recalculate the reference set, but it selects the three nearest calendar fixed-expiry OKX contracts after the Polymarket market end, not a new five-minute futures contract. The current set is July 24, July 31, and September 25; `BTC-USD_UM-260731` is the second confirmation reference and still has no bid/ask. The set remains unchanged across five-minute markets until an expiry boundary or live-instrument change. No production state changed.
