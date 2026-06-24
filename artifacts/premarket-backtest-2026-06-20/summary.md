# ETH Premarket currency-only backtest

Range: 2025-12-20T00:00:00.0000000+00:00 through 2026-06-20T00:00:00.0000000+00:00 exclusive.
Markets evaluated: 52415. Missing-price skips: 0.
Model: fixed Down countertrend. Enter Down when the previous ETH 5m move from previous start to start-offset is positive and at least threshold bps. Entry price is assumed to be 0.5. Outcome uses ETHUSDT move over the current 5m window.
Timestamp rule: price at a timestamp is the last Binance 1s close strictly before that timestamp.

## Baseline

All 5m Down baseline: settled=52257, win_rate=49.9282%, ROI=-0.1431%, pnl_units=-75.

## Suitability counts

- research_candidate: 272
- watch_only: 28

## Top by ROI, minimum 500 settled trades

| Rank | Offset | Threshold | Settled | Win % | ROI % | Wilson lower % | Z | Active months | Positive months | Suitability |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | -10s | 41 | 1036 | 56.9498 | 13.8862 | 53.9144 | 4.4739 | 7 | 6 | research_candidate |
| 2 | -10s | 42 | 991 | 56.9122 | 13.8105 | 53.8082 | 4.3519 | 7 | 6 | research_candidate |
| 3 | -10s | 40 | 1100 | 56.6364 | 13.2607 | 53.6896 | 4.4021 | 7 | 6 | research_candidate |
| 4 | -5s | 35 | 1494 | 56.4257 | 12.8428 | 53.898 | 4.9674 | 7 | 7 | research_candidate |
| 5 | -5s | 37 | 1339 | 56.3854 | 12.7612 | 53.7146 | 4.6731 | 7 | 7 | research_candidate |
| 6 | -25s | 31 | 1789 | 56.3443 | 12.6816 | 54.0349 | 5.3669 | 7 | 7 | research_candidate |
| 7 | -10s | 43 | 943 | 56.2036 | 12.3941 | 53.0181 | 3.81 | 7 | 6 | research_candidate |
| 8 | -5s | 30 | 2041 | 56.1979 | 12.3837 | 54.0358 | 5.6001 | 7 | 7 | research_candidate |
| 9 | -5s | 36 | 1406 | 56.1878 | 12.3667 | 53.5809 | 4.6404 | 7 | 7 | research_candidate |
| 10 | -5s | 38 | 1263 | 56.1362 | 12.2627 | 53.385 | 4.3614 | 7 | 6 | research_candidate |
| 11 | -10s | 44 | 891 | 56.1167 | 12.2197 | 52.8389 | 3.6516 | 6 | 5 | research_candidate |
| 12 | -5s | 33 | 1672 | 56.1005 | 12.1937 | 53.7104 | 4.989 | 7 | 7 | research_candidate |
| 13 | -5s | 34 | 1573 | 56.0712 | 12.1347 | 53.6067 | 4.8158 | 7 | 7 | research_candidate |
| 14 | -15s | 30 | 1980 | 56.0606 | 12.1151 | 53.8648 | 5.3936 | 7 | 7 | research_candidate |
| 15 | -10s | 38 | 1229 | 56.0618 | 12.1138 | 53.2724 | 4.2502 | 7 | 6 | research_candidate |
| 16 | -5s | 29 | 2179 | 56.0349 | 12.0587 | 53.942 | 5.6341 | 7 | 7 | research_candidate |
| 17 | -15s | 31 | 1829 | 55.9869 | 11.9672 | 53.7017 | 5.1208 | 7 | 7 | research_candidate |
| 18 | -5s | 41 | 1063 | 55.9737 | 11.9361 | 52.9732 | 3.8953 | 7 | 6 | research_candidate |
| 19 | -10s | 39 | 1173 | 55.925 | 11.8399 | 53.069 | 4.0585 | 7 | 6 | research_candidate |
| 20 | -5s | 31 | 1919 | 55.9145 | 11.8229 | 53.6835 | 5.1819 | 7 | 7 | research_candidate |

## Best candidate set

- -10s / 41 bps: settled=1036, win=56.9498%, ROI=13.8862%, Wilson lower=53.9144%, months=6/7.
- -10s / 42 bps: settled=991, win=56.9122%, ROI=13.8105%, Wilson lower=53.8082%, months=6/7.
- -10s / 40 bps: settled=1100, win=56.6364%, ROI=13.2607%, Wilson lower=53.6896%, months=6/7.
- -5s / 35 bps: settled=1494, win=56.4257%, ROI=12.8428%, Wilson lower=53.898%, months=7/7.
- -5s / 37 bps: settled=1339, win=56.3854%, ROI=12.7612%, Wilson lower=53.7146%, months=7/7.
- -25s / 31 bps: settled=1789, win=56.3443%, ROI=12.6816%, Wilson lower=54.0349%, months=7/7.
- -10s / 43 bps: settled=943, win=56.2036%, ROI=12.3941%, Wilson lower=53.0181%, months=6/7.
- -5s / 30 bps: settled=2041, win=56.1979%, ROI=12.3837%, Wilson lower=54.0358%, months=7/7.
- -5s / 36 bps: settled=1406, win=56.1878%, ROI=12.3667%, Wilson lower=53.5809%, months=7/7.
- -5s / 38 bps: settled=1263, win=56.1362%, ROI=12.2627%, Wilson lower=53.385%, months=6/7.
- -10s / 44 bps: settled=891, win=56.1167%, ROI=12.2197%, Wilson lower=52.8389%, months=5/6.
- -5s / 33 bps: settled=1672, win=56.1005%, ROI=12.1937%, Wilson lower=53.7104%, months=7/7.
- -5s / 34 bps: settled=1573, win=56.0712%, ROI=12.1347%, Wilson lower=53.6067%, months=7/7.
- -15s / 30 bps: settled=1980, win=56.0606%, ROI=12.1151%, Wilson lower=53.8648%, months=7/7.
- -10s / 38 bps: settled=1229, win=56.0618%, ROI=12.1138%, Wilson lower=53.2724%, months=6/7.
- -5s / 29 bps: settled=2179, win=56.0349%, ROI=12.0587%, Wilson lower=53.942%, months=7/7.
- -15s / 31 bps: settled=1829, win=55.9869%, ROI=11.9672%, Wilson lower=53.7017%, months=7/7.
- -5s / 41 bps: settled=1063, win=55.9737%, ROI=11.9361%, Wilson lower=52.9732%, months=6/7.
- -10s / 39 bps: settled=1173, win=55.925%, ROI=11.8399%, Wilson lower=53.069%, months=6/7.
- -5s / 31 bps: settled=1919, win=55.9145%, ROI=11.8229%, Wilson lower=53.6835%, months=7/7.
- -25s / 30 bps: settled=1904, win=55.8824%, ROI=11.7585%, Wilson lower=53.6424%, months=7/7.
- -10s / 36 bps: settled=1371, win=55.8716%, ROI=11.7347%, Wilson lower=53.2304%, months=7/7.
- -10s / 35 bps: settled=1457, win=55.8682%, ROI=11.7284%, Wilson lower=53.3064%, months=7/7.
- -20s / 31 bps: settled=1807, win=55.8384%, ROI=11.6704%, Wilson lower=53.5388%, months=7/7.
- -5s / 32 bps: settled=1786, win=55.8231%, ROI=11.6396%, Wilson lower=53.5099%, months=7/7.
- -10s / 31 bps: settled=1876, win=55.8102%, ROI=11.6143%, Wilson lower=53.5533%, months=7/7.
- -20s / 30 bps: settled=1937, win=55.808%, ROI=11.6099%, Wilson lower=53.587%, months=7/7.
- -15s / 32 bps: settled=1722, win=55.8072%, ROI=11.6077%, Wilson lower=53.4512%, months=7/7.
- -25s / 33 bps: settled=1583, win=55.7802%, ROI=11.553%, Wilson lower=53.3225%, months=7/7.
- -5s / 39 bps: settled=1194, win=55.7789%, ROI=11.5481%, Wilson lower=52.9477%, months=6/7.

## Watch-only set

- -30s / 49 bps: settled=662, win=53.7764%, ROI=7.5415%, Wilson lower=49.9676%, months=6/6.
- -30s / 48 bps: settled=697, win=53.6585%, ROI=7.3066%, Wilson lower=49.9465%, months=6/6.
- -5s / 50 bps: settled=714, win=53.5014%, ROI=6.993%, Wilson lower=49.8339%, months=5/6.
- -30s / 50 bps: settled=630, win=53.4921%, ROI=6.9731%, Wilson lower=49.5878%, months=6/6.
- -15s / 4 bps: settled=17946, win=51.4767%, ROI=2.9451%, Wilson lower=50.7452%, months=6/7.
- -30s / 6 bps: settled=14407, win=51.475%, ROI=2.9426%, Wilson lower=50.6586%, months=6/7.
- -10s / 3 bps: settled=19771, win=51.4643%, ROI=2.9192%, Wilson lower=50.7674%, months=6/7.
- -25s / 5 bps: settled=16075, win=51.4588%, ROI=2.91%, Wilson lower=50.6859%, months=6/7.
- -5s / 2 bps: settled=21752, win=51.4252%, ROI=2.8414%, Wilson lower=50.7608%, months=6/7.
- -15s / 3 bps: settled=19765, win=51.3989%, ROI=2.7894%, Wilson lower=50.7019%, months=6/7.
- -10s / 2 bps: settled=21752, win=51.3884%, ROI=2.768%, Wilson lower=50.724%, months=6/7.
- -30s / 4 bps: settled=17807, win=51.3562%, ROI=2.7054%, Wilson lower=50.6219%, months=7/7.
- -25s / 4 bps: settled=17827, win=51.3547%, ROI=2.7021%, Wilson lower=50.6208%, months=7/7.
- -5s / 1 bps: settled=23875, win=51.3257%, ROI=2.6422%, Wilson lower=50.6915%, months=7/7.
- -20s / 4 bps: settled=17890, win=51.3248%, ROI=2.6421%, Wilson lower=50.5921%, months=6/7.
- -30s / 5 bps: settled=16071, win=51.316%, ROI=2.6254%, Wilson lower=50.543%, months=6/7.
- -25s / 3 bps: settled=19726, win=51.2623%, ROI=2.5172%, Wilson lower=50.5646%, months=6/7.
- -20s / 3 bps: settled=19765, win=51.2421%, ROI=2.4769%, Wilson lower=50.5451%, months=6/7.
- -10s / 1 bps: settled=23877, win=51.1915%, ROI=2.3752%, Wilson lower=50.5573%, months=7/7.
- -20s / 2 bps: settled=21780, win=51.1708%, ROI=2.3343%, Wilson lower=50.5068%, months=6/7.
- -30s / 3 bps: settled=19709, win=51.1644%, ROI=2.3223%, Wilson lower=50.4664%, months=6/7.
- -15s / 2 bps: settled=21791, win=51.1404%, ROI=2.2737%, Wilson lower=50.4765%, months=6/7.
- -25s / 2 bps: settled=21744, win=51.1313%, ROI=2.2554%, Wilson lower=50.4668%, months=6/7.
- -20s / 1 bps: settled=23899, win=51.1277%, ROI=2.2479%, Wilson lower=50.4938%, months=7/7.
- -25s / 1 bps: settled=23872, win=51.0766%, ROI=2.146%, Wilson lower=50.4423%, months=7/7.
- -15s / 1 bps: settled=23895, win=51.0734%, ROI=2.14%, Wilson lower=50.4395%, months=7/7.
- -30s / 1 bps: settled=23927, win=50.9759%, ROI=1.9453%, Wilson lower=50.3423%, months=7/7.
- -30s / 2 bps: settled=21765, win=50.9166%, ROI=1.8273%, Wilson lower=50.2523%, months=5/7.

## Source files

- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2025-12.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-01.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-02.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-03.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-04.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-05.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-01.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-02.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-03.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-04.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-05.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-06.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-07.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-08.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-09.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-10.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-11.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-12.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-13.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-14.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-15.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-16.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-17.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-18.zip
- https://data.binance.vision/data/spot/daily/klines/ETHUSDT/1s/ETHUSDT-1s-2026-06-19.zip
