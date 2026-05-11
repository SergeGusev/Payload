## 2026-05-11T05:09:30.0000000+00:00
Request:
Проверь, появились ли кандидаты на Live

Result:
Checked the old/local PostgreSQL 17.5 database with a temporary read-only C# probe and removed the probe afterward. Service data is fresh: latest paper order `2026-05-11 05:08:31 UTC`, `226` paper orders and `154` BTC odds ticks in the last 15 minutes. Strongest Paper candidates are `BTC Up or Down 5m More 150 Below 65`, `BTC Up or Down 5m More 90 Below 55`, and `BTC Up or Down 5m More 120 Below 70`; `More 270 Below 60/65` is strong but sample is still thin. `BTC Up or Down 5m Binance` is no longer a candidate after turning negative all-time and over 24h. No Live flags were enabled.

## 2026-05-10T21:04:30.0000000+00:00
Request:
Сейчас мы работаем в старой базе, так и нужно

Result:
Confirmed that continuing to collect statistics in the old/local PostgreSQL database is intentional. Verified the Net48 service remains running in Paper mode with IPC status `Running`, paper trading unpaused, kill switch inactive, and no last error. No production code changed.
