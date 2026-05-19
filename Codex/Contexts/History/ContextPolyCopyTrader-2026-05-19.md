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
