# Middle Premarket currency-only backtest

Range: 2025-12-20T00:00:00.0000000+00:00 through 2026-06-20T00:00:00.0000000+00:00 exclusive.
Assets: BTC, ETH, SOL. Markets evaluated per asset: BTC=52416, ETH=52416, SOL=52416.
Model: current catalog Middle signal moved to premarket time. At `market_start - offset`, compare Binance price to the arithmetic mean of the latest N sampled prices; above mean buys Down, below mean buys Up. Threshold rows require absolute deviation from mean >= threshold bps. Outcome uses the asset's Binance move over the current 5m window.
Sampling model: one sample every 60 seconds, latest sample aligned to UTC minute boundary at or before decision time; depths are 100,90,...,10. This approximates `BinanceBtcUsdReference`/`BinanceCryptoReference` rolling sample caches and fixes the otherwise runtime-dependent sample phase.
Entry-price model: assumed fixed entry price 0.50. Break-even entry price equals win rate. `roi_pct_at_0_52` is included as a sensitivity check for slightly expensive premarket asks. No Polymarket pre-start order book depth/liquidity is simulated.
Timestamp rule: price at a timestamp is the last Binance 1s close strictly before that timestamp.

## Baseline

| Asset | Markets | Up win % | Up ROI@0.50 % | Down win % | Down ROI@0.50 % |
|---|---:|---:|---:|---:|---:|
| BTC | 52416 | 50.0497 | 0.0995 | 49.9503 | -0.0995 |
| ETH | 52416 | 50.0708 | 0.1416 | 49.9292 | -0.1416 |
| SOL | 52416 | 49.9861 | -0.0277 | 50.0139 | 0.0277 |

## Suitability counts

- insufficient_sample: 277
- reject: 2
- research_candidate: 3305
- watch_only: 196

## Top by ROI, minimum 500 settled trades

| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.50 % | BE price | ROI@0.52 % | Wilson lower % | Months | Suitability |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | SOL | -10s | 10 | 65 | 512 | 57.6172 | 14.9712 | 0.5762 | 10.8023 | 53.2956 | 5/5 | research_candidate |
| 2 | SOL | -5s | 10 | 60 | 696 | 57.3276 | 14.4068 | 0.5733 | 10.2454 | 53.6227 | 6/6 | research_candidate |
| 3 | SOL | -10s | 20 | 80 | 749 | 57.2764 | 14.3045 | 0.5728 | 10.1469 | 53.7054 | 6/6 | research_candidate |
| 4 | SOL | -15s | 10 | 60 | 635 | 57.0079 | 13.7558 | 0.5701 | 9.6305 | 53.1265 | 6/6 | research_candidate |
| 5 | SOL | -15s | 20 | 75 | 895 | 56.9832 | 13.7212 | 0.5698 | 9.5832 | 53.7165 | 6/6 | research_candidate |
| 6 | SOL | -5s | 40 | 95 | 1201 | 56.9525 | 13.711 | 0.5695 | 9.5241 | 54.1344 | 7/7 | research_candidate |
| 7 | BTC | -30s | 10 | 45 | 514 | 56.8093 | 13.6187 | 0.5681 | 9.2487 | 52.4921 | 5/6 | research_candidate |
| 8 | SOL | -25s | 20 | 70 | 1063 | 56.9144 | 13.6111 | 0.5691 | 9.4508 | 53.9178 | 6/7 | research_candidate |
| 9 | SOL | -10s | 10 | 60 | 652 | 56.9018 | 13.5952 | 0.569 | 9.4266 | 53.0711 | 6/6 | research_candidate |
| 10 | SOL | -5s | 30 | 90 | 946 | 56.871 | 13.5417 | 0.5687 | 9.3674 | 53.6935 | 6/7 | research_candidate |
| 11 | SOL | -25s | 20 | 80 | 694 | 56.9164 | 13.5402 | 0.5692 | 9.4547 | 53.204 | 4/6 | research_candidate |
| 12 | SOL | -5s | 20 | 80 | 767 | 56.8449 | 13.4615 | 0.5684 | 9.317 | 53.3141 | 5/6 | research_candidate |
| 13 | SOL | -10s | 20 | 70 | 1089 | 56.7493 | 13.3032 | 0.5675 | 9.1333 | 53.7881 | 6/7 | research_candidate |
| 14 | SOL | -15s | 20 | 55 | 1985 | 56.7254 | 13.2836 | 0.5673 | 9.0874 | 54.5349 | 7/7 | research_candidate |
| 15 | BTC | -10s | 20 | 40 | 1822 | 56.5862 | 13.1723 | 0.5659 | 8.8196 | 54.2988 | 6/7 | research_candidate |
| 16 | SOL | -30s | 20 | 70 | 1049 | 56.6254 | 13.0639 | 0.5663 | 8.8949 | 53.6075 | 6/7 | research_candidate |
| 17 | SOL | -5s | 20 | 90 | 521 | 56.6219 | 12.9944 | 0.5662 | 8.8882 | 52.3331 | 6/6 | research_candidate |
| 18 | SOL | -20s | 20 | 75 | 871 | 56.6016 | 12.9651 | 0.566 | 8.8492 | 53.2882 | 6/6 | research_candidate |
| 19 | SOL | -10s | 20 | 90 | 516 | 56.5891 | 12.9278 | 0.5659 | 8.8253 | 52.2794 | 6/6 | research_candidate |
| 20 | SOL | -10s | 20 | 75 | 907 | 56.5601 | 12.9207 | 0.5656 | 8.7694 | 53.3132 | 6/6 | research_candidate |
| 21 | SOL | -10s | 30 | 75 | 1514 | 56.539 | 12.9074 | 0.5654 | 8.7288 | 54.0285 | 6/7 | research_candidate |
| 22 | BTC | -10s | 50 | 45 | 3704 | 56.4525 | 12.905 | 0.5645 | 8.5625 | 54.8498 | 7/7 | research_candidate |
| 23 | SOL | -5s | 20 | 65 | 1366 | 56.5154 | 12.8613 | 0.5652 | 8.6834 | 53.8718 | 6/7 | research_candidate |
| 24 | SOL | -20s | 20 | 80 | 716 | 56.5642 | 12.8591 | 0.5656 | 8.7774 | 52.908 | 6/6 | research_candidate |
| 25 | SOL | -15s | 20 | 80 | 725 | 56.5517 | 12.8552 | 0.5655 | 8.7533 | 52.9183 | 6/6 | research_candidate |
| 26 | SOL | -5s | 40 | 85 | 1602 | 56.4919 | 12.8316 | 0.5649 | 8.6382 | 54.0515 | 7/7 | research_candidate |
| 27 | SOL | -10s | 10 | 50 | 1108 | 56.4982 | 12.8 | 0.565 | 8.6504 | 53.5615 | 7/7 | research_candidate |
| 28 | BTC | -15s | 20 | 35 | 2417 | 56.3922 | 12.7844 | 0.5639 | 8.4466 | 54.4066 | 7/7 | research_candidate |
| 29 | SOL | -25s | 10 | 60 | 607 | 56.5074 | 12.7832 | 0.5651 | 8.6681 | 52.5348 | 5/6 | research_candidate |
| 30 | SOL | -15s | 20 | 70 | 1087 | 56.4857 | 12.7717 | 0.5649 | 8.6264 | 53.5207 | 6/7 | research_candidate |

## Top deploy-relevant offsets

Only offsets matching the existing ETH Premarket shape are shown here: -30s, -10s, -5s.

| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.50 % | BE price | ROI@0.52 % | Months | Suitability |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | SOL | -10s | 10 | 65 | 512 | 57.6172 | 14.9712 | 0.5762 | 10.8023 | 5/5 | research_candidate |
| 2 | SOL | -5s | 10 | 60 | 696 | 57.3276 | 14.4068 | 0.5733 | 10.2454 | 6/6 | research_candidate |
| 3 | SOL | -10s | 20 | 80 | 749 | 57.2764 | 14.3045 | 0.5728 | 10.1469 | 6/6 | research_candidate |
| 4 | SOL | -5s | 40 | 95 | 1201 | 56.9525 | 13.711 | 0.5695 | 9.5241 | 7/7 | research_candidate |
| 5 | BTC | -30s | 10 | 45 | 514 | 56.8093 | 13.6187 | 0.5681 | 9.2487 | 5/6 | research_candidate |
| 6 | SOL | -10s | 10 | 60 | 652 | 56.9018 | 13.5952 | 0.569 | 9.4266 | 6/6 | research_candidate |
| 7 | SOL | -5s | 30 | 90 | 946 | 56.871 | 13.5417 | 0.5687 | 9.3674 | 6/7 | research_candidate |
| 8 | SOL | -5s | 20 | 80 | 767 | 56.8449 | 13.4615 | 0.5684 | 9.317 | 5/6 | research_candidate |
| 9 | SOL | -10s | 20 | 70 | 1089 | 56.7493 | 13.3032 | 0.5675 | 9.1333 | 6/7 | research_candidate |
| 10 | BTC | -10s | 20 | 40 | 1822 | 56.5862 | 13.1723 | 0.5659 | 8.8196 | 6/7 | research_candidate |
| 11 | SOL | -30s | 20 | 70 | 1049 | 56.6254 | 13.0639 | 0.5663 | 8.8949 | 6/7 | research_candidate |
| 12 | SOL | -5s | 20 | 90 | 521 | 56.6219 | 12.9944 | 0.5662 | 8.8882 | 6/6 | research_candidate |
| 13 | SOL | -10s | 20 | 90 | 516 | 56.5891 | 12.9278 | 0.5659 | 8.8253 | 6/6 | research_candidate |
| 14 | SOL | -10s | 20 | 75 | 907 | 56.5601 | 12.9207 | 0.5656 | 8.7694 | 6/6 | research_candidate |
| 15 | SOL | -10s | 30 | 75 | 1514 | 56.539 | 12.9074 | 0.5654 | 8.7288 | 6/7 | research_candidate |
| 16 | BTC | -10s | 50 | 45 | 3704 | 56.4525 | 12.905 | 0.5645 | 8.5625 | 7/7 | research_candidate |
| 17 | SOL | -5s | 20 | 65 | 1366 | 56.5154 | 12.8613 | 0.5652 | 8.6834 | 6/7 | research_candidate |
| 18 | SOL | -5s | 40 | 85 | 1602 | 56.4919 | 12.8316 | 0.5649 | 8.6382 | 7/7 | research_candidate |
| 19 | SOL | -10s | 10 | 50 | 1108 | 56.4982 | 12.8 | 0.565 | 8.6504 | 7/7 | research_candidate |
| 20 | SOL | -10s | 30 | 85 | 1095 | 56.4384 | 12.6913 | 0.5644 | 8.5353 | 6/7 | research_candidate |
| 21 | SOL | -10s | 30 | 90 | 927 | 56.4186 | 12.6461 | 0.5642 | 8.4972 | 6/7 | research_candidate |
| 22 | BTC | -5s | 20 | 35 | 2486 | 56.3154 | 12.6307 | 0.5632 | 8.2988 | 6/7 | research_candidate |
| 23 | BTC | -5s | 20 | 40 | 1861 | 56.3138 | 12.6276 | 0.5631 | 8.2958 | 6/7 | research_candidate |
| 24 | SOL | -30s | 10 | 60 | 578 | 56.4014 | 12.585 | 0.564 | 8.4642 | 6/6 | research_candidate |
| 25 | BTC | -30s | 40 | 45 | 2903 | 56.2866 | 12.5732 | 0.5629 | 8.2435 | 6/7 | research_candidate |
| 26 | SOL | -5s | 10 | 65 | 541 | 56.3771 | 12.5227 | 0.5638 | 8.4175 | 5/6 | research_candidate |
| 27 | SOL | -5s | 30 | 85 | 1115 | 56.3229 | 12.4779 | 0.5632 | 8.3132 | 7/7 | research_candidate |
| 28 | BTC | -10s | 20 | 35 | 2458 | 56.2246 | 12.4491 | 0.5622 | 8.1242 | 6/7 | research_candidate |
| 29 | SOL | -5s | 20 | 70 | 1133 | 56.3107 | 12.4456 | 0.5631 | 8.2898 | 6/7 | research_candidate |
| 30 | SOL | -5s | 30 | 95 | 817 | 56.3035 | 12.4246 | 0.563 | 8.2761 | 5/6 | research_candidate |

## Best by asset

### BTC

- -30s / depth 10 / 45 bps: settled=514, win=56.8093%, ROI@0.50=13.6187%, BE=0.5681, ROI@0.52=9.2487%, months=5/6, research_candidate.
- -10s / depth 20 / 40 bps: settled=1822, win=56.5862%, ROI@0.50=13.1723%, BE=0.5659, ROI@0.52=8.8196%, months=6/7, research_candidate.
- -10s / depth 50 / 45 bps: settled=3704, win=56.4525%, ROI@0.50=12.905%, BE=0.5645, ROI@0.52=8.5625%, months=7/7, research_candidate.
- -15s / depth 20 / 35 bps: settled=2417, win=56.3922%, ROI@0.50=12.7844%, BE=0.5639, ROI@0.52=8.4466%, months=7/7, research_candidate.
- -15s / depth 20 / 40 bps: settled=1812, win=56.3466%, ROI@0.50=12.6932%, BE=0.5635, ROI@0.52=8.3588%, months=6/7, research_candidate.
- -15s / depth 40 / 45 bps: settled=2939, win=56.3457%, ROI@0.50=12.6914%, BE=0.5635, ROI@0.52=8.3571%, months=6/7, research_candidate.
- -15s / depth 30 / 40 bps: settled=2824, win=56.3385%, ROI@0.50=12.6771%, BE=0.5634, ROI@0.52=8.3433%, months=6/7, research_candidate.
- -5s / depth 20 / 35 bps: settled=2486, win=56.3154%, ROI@0.50=12.6307%, BE=0.5632, ROI@0.52=8.2988%, months=6/7, research_candidate.
- -5s / depth 20 / 40 bps: settled=1861, win=56.3138%, ROI@0.50=12.6276%, BE=0.5631, ROI@0.52=8.2958%, months=6/7, research_candidate.
- -30s / depth 40 / 45 bps: settled=2903, win=56.2866%, ROI@0.50=12.5732%, BE=0.5629, ROI@0.52=8.2435%, months=6/7, research_candidate.

### ETH

- -5s / depth 20 / 80 bps: settled=688, win=56.1047%, ROI@0.50=12.2093%, BE=0.561, ROI@0.52=7.8936%, months=5/6, research_candidate.
- -10s / depth 20 / 75 bps: settled=797, win=56.0853%, ROI@0.50=12.1706%, BE=0.5609, ROI@0.52=7.8564%, months=5/6, research_candidate.
- -10s / depth 20 / 80 bps: settled=674, win=56.0831%, ROI@0.50=12.1662%, BE=0.5608, ROI@0.52=7.8521%, months=5/6, research_candidate.
- -5s / depth 10 / 55 bps: settled=774, win=56.0724%, ROI@0.50=12.1447%, BE=0.5607, ROI@0.52=7.8314%, months=5/6, research_candidate.
- -5s / depth 30 / 100 bps: settled=638, win=55.9561%, ROI@0.50=11.9122%, BE=0.5596, ROI@0.52=7.6079%, months=5/5, research_candidate.
- -5s / depth 10 / 20 bps: settled=6525, win=55.954%, ROI@0.50=11.8953%, BE=0.5595, ROI@0.52=7.6039%, months=7/7, research_candidate.
- -15s / depth 20 / 80 bps: settled=678, win=55.8997%, ROI@0.50=11.7994%, BE=0.559, ROI@0.52=7.4994%, months=5/6, research_candidate.
- -5s / depth 20 / 20 bps: settled=10679, win=55.9041%, ROI@0.50=11.7939%, BE=0.559, ROI@0.52=7.5079%, months=7/7, research_candidate.
- -5s / depth 40 / 35 bps: settled=7639, win=55.8974%, ROI@0.50=11.7824%, BE=0.559, ROI@0.52=7.4949%, months=7/7, research_candidate.
- -5s / depth 50 / 40 bps: settled=7345, win=55.7931%, ROI@0.50=11.5751%, BE=0.5579, ROI@0.52=7.2943%, months=7/7, research_candidate.

### SOL

- -10s / depth 10 / 65 bps: settled=512, win=57.6172%, ROI@0.50=14.9712%, BE=0.5762, ROI@0.52=10.8023%, months=5/5, research_candidate.
- -5s / depth 10 / 60 bps: settled=696, win=57.3276%, ROI@0.50=14.4068%, BE=0.5733, ROI@0.52=10.2454%, months=6/6, research_candidate.
- -10s / depth 20 / 80 bps: settled=749, win=57.2764%, ROI@0.50=14.3045%, BE=0.5728, ROI@0.52=10.1469%, months=6/6, research_candidate.
- -15s / depth 10 / 60 bps: settled=635, win=57.0079%, ROI@0.50=13.7558%, BE=0.5701, ROI@0.52=9.6305%, months=6/6, research_candidate.
- -15s / depth 20 / 75 bps: settled=895, win=56.9832%, ROI@0.50=13.7212%, BE=0.5698, ROI@0.52=9.5832%, months=6/6, research_candidate.
- -5s / depth 40 / 95 bps: settled=1201, win=56.9525%, ROI@0.50=13.711%, BE=0.5695, ROI@0.52=9.5241%, months=7/7, research_candidate.
- -25s / depth 20 / 70 bps: settled=1063, win=56.9144%, ROI@0.50=13.6111%, BE=0.5691, ROI@0.52=9.4508%, months=6/7, research_candidate.
- -10s / depth 10 / 60 bps: settled=652, win=56.9018%, ROI@0.50=13.5952%, BE=0.569, ROI@0.52=9.4266%, months=6/6, research_candidate.
- -5s / depth 30 / 90 bps: settled=946, win=56.871%, ROI@0.50=13.5417%, BE=0.5687, ROI@0.52=9.3674%, months=6/7, research_candidate.
- -25s / depth 20 / 80 bps: settled=694, win=56.9164%, ROI@0.50=13.5402%, BE=0.5692, ROI@0.52=9.4547%, months=4/6, research_candidate.

## Candidate sets

Research candidates: 3305. Watch-only: 196.

Research-candidate gate: >=500 settled trades, >=4 active months, ROI@0.50 >= 3%, Wilson lower win rate > 50%, and at least 60% profitable active months. This is a research filter, not a live-trading recommendation.

## Source files

- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-01.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-02.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-03.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-04.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-05.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-06.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-07.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-08.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-09.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-10.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-11.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-12.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-13.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-14.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-15.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-16.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-17.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-18.zip
- https://data.binance.vision/data/spot/daily/klines/BTCUSDT/1s/BTCUSDT-1s-2026-06-19.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2025-12.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2026-01.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2026-02.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2026-03.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2026-04.zip
- https://data.binance.vision/data/spot/monthly/klines/BTCUSDT/1s/BTCUSDT-1s-2026-05.zip
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
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2025-12.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-01.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-02.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-03.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-04.zip
- https://data.binance.vision/data/spot/monthly/klines/ETHUSDT/1s/ETHUSDT-1s-2026-05.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-01.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-02.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-03.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-04.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-05.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-06.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-07.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-08.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-09.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-10.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-11.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-12.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-13.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-14.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-15.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-16.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-17.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-18.zip
- https://data.binance.vision/data/spot/daily/klines/SOLUSDT/1s/SOLUSDT-1s-2026-06-19.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2025-12.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2026-01.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2026-02.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2026-03.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2026-04.zip
- https://data.binance.vision/data/spot/monthly/klines/SOLUSDT/1s/SOLUSDT-1s-2026-05.zip
