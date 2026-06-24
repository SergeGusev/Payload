# Diff Strategy Backtest

- Counter model: zero-start per asset at the first loaded historical market, dynamic `DiffCount` threshold +/-5.
- Entry signal: market `T` uses trend-adjusted Diff after market `T-5m`; positive Diff buys `Down`, negative Diff buys `Up`.
- `instant` model: first available odds tick in the first 60 seconds, selected outcome `best_ask <= 0.65`, settled only when the terminal odds result is strong (`winner >= 0.80`, loser `<= 0.20`).
- `fixed05_strong` model: same strong terminal odds result, assumed entry price `0.50`.
- `fixed05_binance` model: assumed entry price `0.50`, settled by the final Binance move sign; this is a coverage/sensitivity check, not the primary Polymarket settlement proxy.

## Asset Summary

| Asset | Markets | Period UTC | Instant bets | Instant PnL | Instant ROI | Fixed 0.50 strong bets | Fixed 0.50 PnL | Fixed 0.50 ROI |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| BTC | 7802 | 2026-05-08 21:45..2026-06-08 20:35 | 13999 | -458.48594418 | -3.27513354% | 14496 | 44 | 0.30353201% |
| ETH | 5864 | 2026-05-09 10:15..2026-06-08 20:35 | 10356 | -384.78624431 | -3.71558753% | 10582 | 20 | 0.18900019% |
| SOL | 5859 | 2026-05-09 10:15..2026-06-08 20:35 | 12326 | -804.40459225 | -6.52607977% | 12802 | -350 | -2.73394782% |

## Top Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| ETH Up or Down 5m Up 4 Diff Instant | 382 | 208 | 54.45026178% | 26.58722182 | 6.96000571% | 0.51125654 |
| ETH Up or Down 5m Up 6 Diff Instant | 102 | 64 | 62.74509804% | 24.95714928 | 24.46779341% | 0.50696078 |
| ETH Up or Down 5m Up 5 Diff Instant | 223 | 124 | 55.60538117% | 20.49076096 | 9.18868204% | 0.51174888 |
| BTC Up or Down 5m Down 9 Diff Instant | 87 | 50 | 57.47126437% | 8.97277938 | 10.31353952% | 0.50965517 |
| ETH Up or Down 5m Up 7 Diff Instant | 58 | 34 | 58.62068966% | 8.90720479 | 15.35724963% | 0.50586207 |
| BTC Up or Down 5m Down 10 Diff Instant | 69 | 39 | 56.52173913% | 5.6135444 | 8.13557159% | 0.51072464 |
| ETH Up or Down 5m Up 8 Diff Instant | 27 | 16 | 59.25925926% | 4.92303349 | 18.23345737% | 0.50740741 |
| SOL Up or Down 5m Down 9 Diff Instant | 84 | 45 | 53.57142857% | 3.73704389 | 4.44886177% | 0.50797619 |
| SOL Up or Down 5m Down 10 Diff Instant | 66 | 36 | 54.54545455% | 3.18352726 | 4.82352615% | 0.50772727 |
| BTC Up or Down 5m Down 8 Diff Instant | 115 | 60 | 52.17391304% | 1.58548452 | 1.37868219% | 0.50756522 |
| ETH Up or Down 5m Down 8 Diff Instant | 30 | 16 | 53.33333333% | 0.74074832 | 2.46916106% | 0.51833333 |
| BTC Up or Down 5m Up 10 Diff Instant | 68 | 33 | 48.52941176% | -2.71340568 | -3.99030247% | 0.50455882 |
| BTC Up or Down 5m Down 7 Diff Instant | 160 | 80 | 50% | -3.2522035 | -2.03262719% | 0.50925 |
| ETH Up or Down 5m Down 7 Diff Instant | 74 | 37 | 50% | -3.31033919 | -4.47343133% | 0.51918919 |
| ETH Up or Down 5m Down 6 Diff Instant | 129 | 66 | 51.1627907% | -3.68733058 | -2.8583958% | 0.52062016 |

## Worst Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m Up 1 Diff Instant | 2212 | 1057 | 47.78481013% | -147.50060566 | -6.66820098% | 0.51368897 |
| ETH Up or Down 5m Down 1 Diff Instant | 2209 | 1056 | 47.8044364% | -145.14817323 | -6.57076384% | 0.51082843 |
| SOL Up or Down 5m Down 1 Diff Instant | 2205 | 1064 | 48.25396825% | -127.96674389 | -5.80348045% | 0.51187302 |
| ETH Up or Down 5m Down 2 Diff Instant | 1330 | 634 | 47.66917293% | -102.88229645 | -7.73551101% | 0.51539098 |
| SOL Up or Down 5m Up 2 Diff Instant | 1428 | 681 | 47.68907563% | -97.77949936 | -6.84730388% | 0.51443277 |
| SOL Up or Down 5m Down 2 Diff Instant | 1402 | 675 | 48.14550642% | -87.93976727 | -6.2724513% | 0.51347361 |
| BTC Up or Down 5m Down 2 Diff Instant | 1676 | 818 | 48.80668258% | -85.11763058 | -5.07861758% | 0.51119332 |
| ETH Up or Down 5m Down 3 Diff Instant | 786 | 368 | 46.81933842% | -76.71054462 | -9.75961128% | 0.5174173 |
| BTC Up or Down 5m Up 2 Diff Instant | 1709 | 832 | 48.68344061% | -72.82846133 | -4.26146643% | 0.50738444 |
| SOL Up or Down 5m Down 3 Diff Instant | 895 | 422 | 47.15083799% | -70.10761052 | -7.83325257% | 0.51363128 |
| BTC Up or Down 5m Down 1 Diff Instant | 2797 | 1384 | 49.48158742% | -67.95685801 | -2.42963382% | 0.50675366 |
| SOL Up or Down 5m Up 3 Diff Instant | 895 | 434 | 48.49162011% | -44.83281871 | -5.00925349% | 0.51487151 |
| ETH Up or Down 5m Up 1 Diff Instant | 2222 | 1116 | 50.2250225% | -44.68835939 | -2.01117729% | 0.51125563 |
| BTC Up or Down 5m Down 4 Diff Instant | 511 | 245 | 47.94520548% | -39.42643842 | -7.71554568% | 0.5160274 |
| BTC Up or Down 5m Down 3 Diff Instant | 965 | 478 | 49.53367876% | -39.34425209 | -4.07712457% | 0.51374093 |

## Top Fixed 0.50 Strong Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m Up 1 Diff Instant | 2870 | 1459 | 50.83623693% | 48 | 1.67247387% | 0.5 |
| ETH Up or Down 5m Up 4 Diff Instant | 389 | 214 | 55.01285347% | 39 | 10.02570694% | 0.5 |
| ETH Up or Down 5m Up 2 Diff Instant | 1366 | 699 | 51.17130307% | 32 | 2.34260615% | 0.5 |
| ETH Up or Down 5m Up 6 Diff Instant | 105 | 67 | 63.80952381% | 29 | 27.61904762% | 0.5 |
| ETH Up or Down 5m Up 5 Diff Instant | 226 | 127 | 56.19469027% | 28 | 12.38938053% | 0.5 |
| ETH Up or Down 5m Up 1 Diff Instant | 2271 | 1146 | 50.46235139% | 21 | 0.92470277% | 0.5 |
| BTC Up or Down 5m Down 9 Diff Instant | 91 | 54 | 59.34065934% | 17 | 18.68131868% | 0.5 |
| BTC Up or Down 5m Down 1 Diff Instant | 2917 | 1466 | 50.25711347% | 15 | 0.51422695% | 0.5 |
| BTC Up or Down 5m Down 10 Diff Instant | 73 | 43 | 58.90410959% | 13 | 17.80821918% | 0.5 |
| BTC Up or Down 5m Down 3 Diff Instant | 1005 | 509 | 50.64676617% | 13 | 1.29353234% | 0.5 |
| ETH Up or Down 5m Up 7 Diff Instant | 60 | 36 | 60% | 12 | 20% | 0.5 |
| SOL Up or Down 5m Down 10 Diff Instant | 69 | 39 | 56.52173913% | 9 | 13.04347826% | 0.5 |
| SOL Up or Down 5m Down 9 Diff Instant | 87 | 48 | 55.17241379% | 9 | 10.34482759% | 0.5 |
| BTC Up or Down 5m Down 8 Diff Instant | 121 | 65 | 53.71900826% | 9 | 7.43801653% | 0.5 |
| BTC Up or Down 5m Down 5 Diff Instant | 337 | 173 | 51.33531157% | 9 | 2.67062315% | 0.5 |
