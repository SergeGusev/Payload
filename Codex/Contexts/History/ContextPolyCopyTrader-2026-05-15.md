## 2026-05-15T05:15:20.5568365Z
Request:
Проверь статус стратегий

Result:
Queried the server database at `192.168.0.101` read-only for strategy status. `PolyCopyTrader.Service` was running in `Live` mode with a fresh heartbeat and no heartbeat error; `1178` strategies existed, `1176` were enabled, `0` had `live_stakes=true`, `follow_leader` was disabled, and `BTC Up or Down 5m Statistics` was enabled with live stakes off. Overall Paper performance remained negative (`24188` settled, PnL `-20140.2655`, ROI `-29.7678%`), with only a few positive 100+ sample candidates led by `btc_up_down_5m_less_60_gamma`. The Statistics strategy was active (`36041` ticks, latest sample age about `3.5s`, `9202/9310` observations applied), but resolved first-per-market would-bets remained negative. Found that Gamma max-offset API errors are still being logged on the server, indicating the server process still needs the max-offset fix deployed/restarted.
