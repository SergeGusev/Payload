## 2026-05-19T04:04:43.0707448Z
Request:
Если я просто сниму галочки Enabled в Dashboard, то стратегия перестанет работать и загружать ресурсы, правильно?

Result:
Explained that Dashboard row `Enabled` checkboxes update `strategies.enabled`; disabled strategies stop creating new Follow leader signals or BTC entries after the Service refreshes runtime settings, but existing paper/live orders and positions can still be maintained/settled, and shared background workers such as Gamma ingestion, Binance BTC stream, BTC odds archive, order-book refresh, live maintenance, and heartbeat continue unless disabled by their own configuration/service switches.
