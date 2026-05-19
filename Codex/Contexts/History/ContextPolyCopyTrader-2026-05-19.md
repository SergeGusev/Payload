## 2026-05-19T09:43:27.8306190Z
Request:
Set SOL and ETH strategies `Enabled=true`, but Paper mode only.

Result:
Updated production PostgreSQL at `192.168.0.101` for all `eth_up_down_5m_binance_bps_%` and `sol_up_down_5m_binance_bps_%` strategies: `200` rows updated, `200` enabled, `0` live rows. Verification showed ETH `100/100` enabled with `live_stakes=0`, SOL `100/100` enabled with `live_stakes=0`, and the only LiveStakes rows remain the four BTC strategies: `btc_up_down_5m_binance_bps_1_9`, `btc_up_down_5m_binance_bps_2`, `btc_up_down_5m_binance_bps_2_instant`, and `btc_up_down_5m_binance_bps_2_1`. Service heartbeat was fresh in `Live` mode, and `PaperTrading:RunInLiveMode=true` keeps Paper runtime active. No source code change, service restart, live order submission, or cancel action was performed.

## 2026-05-19T05:18:34.5194569Z
Request:
Set `Enabled=false` for all strategies except the Dashboard category `BTC Up or Down 5m Binance`.

Result:
Updated the Dashboard remote PostgreSQL database at `192.168.0.101` with a transactional `strategies` update. Before the update there were `1266` strategies total, `1236` enabled, `113` enabled in `BTC Up or Down 5m Binance`, and `1123` enabled outside that category. After commit and verification there are `113` enabled strategies, all in the Binance 5m category, with `0` enabled outside it. `live_stakes` was not changed and remains enabled for `3` strategies. No service restart, live order submission, or cancel action was performed.

## 2026-05-19T04:04:43.0707448Z
Request:
Если я просто сниму галочки Enabled в Dashboard, то стратегия перестанет работать и загружать ресурсы, правильно?

Result:
Explained that Dashboard row `Enabled` checkboxes update `strategies.enabled`; disabled strategies stop creating new Follow leader signals or BTC entries after the Service refreshes runtime settings, but existing paper/live orders and positions can still be maintained/settled, and shared background workers such as Gamma ingestion, Binance BTC stream, BTC odds archive, order-book refresh, live maintenance, and heartbeat continue unless disabled by their own configuration/service switches.

## 2026-05-19T04:30:02.0507066Z
Request:
Оцени рынки ETH Up or Down 5m
SOL Up or Down 5m
XRP Up or Down 5m
DOGE Up or Down 5m в Polymarket с двух аспектов: 1. Объём ставок - стоит ли туда лезть. 2. Наличие сигнала от Binance, аналогично тому, как мы сейчас работаем с BTC

Result:
Evaluated current Polymarket Gamma 5m volumes/liquidity/spreads for ETH/SOL/XRP/DOGE against BTC and checked Binance public market-data availability. ETH looked like the only reasonable non-BTC candidate for paper/research; SOL was only exploratory paper; XRP and DOGE were not recommended for live due to weak Polymarket-side volume/spread quality. Binance-side ETHUSDT/SOLUSDT/XRPUSDT/DOGEUSDT data was available, but current Service code only has disabled research plumbing for ETH/SOL/XRP and no DOGE default.
## 2026-05-19T05:48:50.3587112Z
Request:
Сделай ETH и SOL стратегии, аналогичные всем BTC Up or Down 5m Binance bps стратегиям, но работающие с ETH и SOL соответственно

Result:
Added disabled-by-default Paper-only ETH/SOL Binance bps and Instant bps strategy variants, wired the paper processor to ETH/SOL 5m Gamma markets with crypto Binance/archive references, enabled ETH/SOL reference/archive workers, updated Dashboard categories and README, and added tests. Verified with solution build in a separate output path plus full test run: 499/499 passed.

## 2026-05-19T09:26:35.6896849+00:00
Request:
Выложил, проверяй

Result:
Checked production read-only after deploy. `PolyCopyTrader.Service` is running in `Live` mode on build `info=1.0.0+a5050c2c9aa60c0f487d055e62c0b2c03cad6ccd`, started `2026-05-19T09:10:46Z`, with fresh heartbeat and no heartbeat error. LiveStakes is enabled only for `btc_up_down_5m_binance_bps_1_9`, `btc_up_down_5m_binance_bps_2`, `btc_up_down_5m_binance_bps_2_instant`, and `btc_up_down_5m_binance_bps_2_1`, each with live stake `1`. The instant strategy is being processed by the new binary; post-start windows through `09:25Z` skipped with `btc_reference_move_below_bps_threshold`, so no instant live order has been created yet. Post-start live orders for the four live strategies are `0`, instant live orders total/open are `0/0`, Paper/Live shadow discrepancies since start are `0`, API errors in the last 60 minutes are `0`, current BTC odds/WebSocket data is fresh, and read-only CLOB `GET /data/orders` reported `Orders summarized: 0`. No database writes, code changes, service restart, live order submission, or cancel action was performed.

## 2026-05-19T09:05:12.7872480+00:00
Request:
Давай выведем в Live стратегию BTC Up or Down 5m Binance 2 bps Instant

Result:
Added `btc_up_down_5m_binance_bps_2_instant` to the BTC Paper/Live-shadow allowlist, added a regression test proving it creates a Paper-shadow plus GTD Live order with instant ask-depth pricing, updated the live-stakes admin test and README live safety docs, and verified targeted tests 2/2 plus full tests 500/500. Updated production PostgreSQL LiveStakes on host `192.168.0.101` to enable only `btc_up_down_5m_binance_bps_1_9`, `btc_up_down_5m_binance_bps_2`, `btc_up_down_5m_binance_bps_2_instant`, and `btc_up_down_5m_binance_bps_2_1` (`4` enabled, `1262` disabled, `0` failed), then verified the live-shadow state shows the instant strategy live-enabled with `liveStake=1`, `liveBalance=100`, `liveOpen=0`, and `liveOrders=0`. No manual live order submission, cancel action, or service restart was performed; production service redeploy/restart from the new commit is still required before this strategy can live-place orders.
