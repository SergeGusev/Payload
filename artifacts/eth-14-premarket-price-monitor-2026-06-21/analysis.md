# ETH 14 FAK Premarket Down Price Analysis

Captured UTC: `2026-06-21T20:36:25Z`

## Live Monitor

Monitor target: ETH Up/Down 5m Down outcome, FAK VWAP for target notional `6.0093`, threshold `0.52`.

Pre-start observations:

| Market | Pre-start samples | Seconds before start | Max pre-start VWAP | Max pre-start ask | First pre-start VWAP > 0.52 | Last pre-start VWAP <= 0.52 |
|---|---:|---:|---:|---:|---:|---:|
| `eth-updown-5m-1782073800` | 206 | `234.993..0.875` | `0.50000000` | `0.50000000` | none | `0.875s` |
| `eth-updown-5m-1782074100` | 365 | `418.942..0.098` | `0.50733622` | `0.50000000` | none | `0.098s` |
| `eth-updown-5m-1782074400` | 177 | `419.090..215.262` | `0.50579159` | `0.50000000` | none | `215.262s` |

The `20:30Z` market had a post-start spike to `0.64` at about `t+0.3s`; it is not a Premarket sample and should not be used for pre-start entry timing.

## Current Paper History

Strategy: `eth_up_down_5m_down_bps_14_fak_premarket`.

All current settled Paper entries:

| Entries | Price <= 0.52 | Pct <= 0.52 | Avg seconds before start | Min seconds before | Max seconds before | Avg price | Median price | Min price | Max price |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 43 | 11 | 25.58% | 25.943 | 18.499 | 29.968 | 0.53912484 | 0.54099733 | 0.48000000 | 0.58871662 |

By actual entry-time bucket:

| Seconds before start | Entries | Price <= 0.52 | Pct <= 0.52 | Avg price | Median price | Min price | Max price |
|---|---:|---:|---:|---:|---:|---:|---:|
| 25-30 | 26 | 7 | 26.92% | 0.53838354 | 0.54049867 | 0.48000000 | 0.57159158 |
| 20-25 | 16 | 4 | 25.00% | 0.53839977 | 0.54261828 | 0.49000000 | 0.58871662 |
| 15-20 | 1 | 0 | 0.00% | 0.57000000 | 0.57000000 | 0.57000000 | 0.57000000 |

By price bucket:

| Price bucket | Settled | Wins | Losses | Win rate | Avg price | Cost | Settlement value | PnL | ROI |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `<=0.52` | 11 | 6 | 5 | 54.5455% | 0.50534655 | 66.10230000 | 71.50784208 | 5.40554211 | 8.1775% |
| `>0.52` | 32 | 24 | 8 | 75.0000% | 0.55073613 | 192.29760000 | 262.60881979 | 70.31122001 | 36.5638% |

## Conclusion

A fixed time offset alone does not guarantee `<=0.52`. The current `-30s` strategy has only 25.58% of historical Paper entries at `<=0.52`, while the live-monitored markets stayed below `0.52` almost until start. The expensive fills therefore appear to be market-specific skew/liquidity conditions, not a universal time boundary.

Use an explicit entry price filter on the current Down book, preferably FAK VWAP for the intended notional, if the requirement is "do not enter above 0.52". A time experiment such as `-60s` or `-90s` can be tested, but it should still include the price/VWAP cap.
