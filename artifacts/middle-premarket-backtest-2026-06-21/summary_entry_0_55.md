# Middle Premarket recalculation at entry price 0.55

Source: existing `strategy_results.csv` and `monthly_summary.csv` from the Middle Premarket currency-only backtest. No Binance data was re-downloaded and no production database was changed.

Formula: buying one binary share at 0.55 gives +0.45 on win and -0.55 on loss; ROI is measured on deployed cost. Break-even win rate is 55%.

## Suitability counts

- insufficient_sample: 277
- reject: 2907
- research_candidate: 10
- watch_only: 586

## Top by ROI@0.55

| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.55 % | BE price | Months | Suitability |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | SOL | -10s | 10 | 65 | 512 | 57.6172 | 4.6763 | 0.5762 | 4/5 | research_candidate |
| 2 | SOL | -5s | 10 | 60 | 696 | 57.3276 | 4.1602 | 0.5733 | 5/6 | research_candidate |
| 3 | SOL | -10s | 20 | 80 | 749 | 57.2764 | 4.0682 | 0.5728 | 4/6 | research_candidate |
| 4 | SOL | -15s | 10 | 60 | 635 | 57.0079 | 3.583 | 0.5701 | 4/6 | research_candidate |
| 5 | SOL | -15s | 20 | 75 | 895 | 56.9832 | 3.5426 | 0.5698 | 5/6 | research_candidate |
| 6 | SOL | -5s | 40 | 95 | 1201 | 56.9525 | 3.5005 | 0.5695 | 3/7 | reject |
| 7 | SOL | -25s | 20 | 70 | 1063 | 56.9144 | 3.4259 | 0.5691 | 5/7 | research_candidate |
| 8 | SOL | -25s | 20 | 80 | 694 | 56.9164 | 3.4107 | 0.5692 | 3/6 | watch_only |
| 9 | SOL | -10s | 10 | 60 | 652 | 56.9018 | 3.4057 | 0.569 | 5/6 | research_candidate |
| 10 | SOL | -5s | 30 | 90 | 946 | 56.871 | 3.3523 | 0.5687 | 3/7 | reject |
| 11 | SOL | -5s | 20 | 80 | 767 | 56.8449 | 3.2984 | 0.5684 | 4/6 | research_candidate |
| 12 | BTC | -30s | 10 | 45 | 514 | 56.8093 | 3.2897 | 0.5681 | 4/6 | research_candidate |
| 13 | SOL | -10s | 20 | 70 | 1089 | 56.7493 | 3.1345 | 0.5675 | 3/7 | reject |
| 14 | SOL | -15s | 20 | 55 | 1985 | 56.7254 | 3.0981 | 0.5673 | 5/7 | research_candidate |
| 15 | SOL | -30s | 20 | 70 | 1049 | 56.6254 | 2.9135 | 0.5663 | 5/7 | watch_only |
| 16 | SOL | -5s | 20 | 90 | 521 | 56.6219 | 2.8933 | 0.5662 | 4/6 | watch_only |
| 17 | BTC | -10s | 20 | 40 | 1822 | 56.5862 | 2.8839 | 0.5659 | 4/7 | watch_only |
| 18 | SOL | -20s | 20 | 75 | 871 | 56.6016 | 2.8595 | 0.566 | 4/6 | watch_only |
| 19 | SOL | -10s | 20 | 90 | 516 | 56.5891 | 2.8344 | 0.5659 | 3/6 | watch_only |
| 20 | SOL | -10s | 20 | 75 | 907 | 56.5601 | 2.7934 | 0.5656 | 5/6 | watch_only |
| 21 | SOL | -20s | 20 | 80 | 716 | 56.5642 | 2.7857 | 0.5656 | 4/6 | watch_only |
| 22 | SOL | -15s | 20 | 80 | 725 | 56.5517 | 2.7679 | 0.5655 | 5/6 | watch_only |
| 23 | SOL | -10s | 30 | 75 | 1514 | 56.539 | 2.7616 | 0.5654 | 5/7 | watch_only |
| 24 | SOL | -5s | 20 | 65 | 1366 | 56.5154 | 2.7194 | 0.5652 | 4/7 | watch_only |
| 25 | SOL | -25s | 10 | 60 | 607 | 56.5074 | 2.692 | 0.5651 | 4/6 | watch_only |
| 26 | SOL | -10s | 10 | 50 | 1108 | 56.4982 | 2.6828 | 0.565 | 5/7 | watch_only |
| 27 | SOL | -5s | 40 | 85 | 1602 | 56.4919 | 2.6807 | 0.5649 | 3/7 | reject |
| 28 | SOL | -15s | 20 | 70 | 1087 | 56.4857 | 2.6597 | 0.5649 | 4/7 | watch_only |
| 29 | BTC | -10s | 50 | 45 | 3704 | 56.4525 | 2.6409 | 0.5645 | 5/7 | watch_only |
| 30 | SOL | -10s | 30 | 85 | 1095 | 56.4384 | 2.5775 | 0.5644 | 3/7 | reject |

## Stable deploy-relevant offsets

Filter: offsets -30s/-10s/-5s, at least 500 settled trades, and 7/7 profitable active months under entry price 0.55.

| Rank | Asset | Offset | Depth | Threshold | Settled | Win % | ROI@0.55 % | BE price | Wilson lower % |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | ETH | -5s | 30 | 30 | 7762 | 55.7073 | 1.2845 | 0.5571 | 54.5997 |

## Best stable by asset

### BTC
No stable deploy-relevant candidate passed the filter.

### ETH
- -5s / depth 30 / 30 bps: settled=7762, win=55.7073%, ROI@0.55=1.2845%, BE=0.5571, Wilson lower=54.5997%.

### SOL
No stable deploy-relevant candidate passed the filter.
