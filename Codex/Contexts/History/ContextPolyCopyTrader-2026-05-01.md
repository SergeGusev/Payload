## 2026-05-01T06:41:53.1859190+00:00
Request:
В какой таблице мы храним полные логи выкачиваемого блокчейна?

Result:
Inspected the on-chain schema, repository writes, ingestion processor, README, and configuration reference. Confirmed raw Polygon `OrderFilled` event logs are stored in `polymarket_onchain_logs`; decoded fills are stored in `polymarket_onchain_fills`. No source code was changed.
