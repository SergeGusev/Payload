## 2026-05-13T05:22:05.9805193+00:00
Request:
Почему так мало Settles заявок для рынка 1h PreOpen full ? Разве мы не должны ставить всегда?

Result:
Diagnosed `BTC Up or Down 1h PreOpen Full` with a temporary read-only C# PostgreSQL probe, IPC status, and service logs. Confirmed the strategies are enabled and recent hourly markets are receiving 80 entered Paper orders, so the always-entry path is working. The low `Settled` count is mainly settlement backlog: hundreds of ended 1h PreOpen Full runs remain `Entered`, while logs show repeated Gamma metadata timeouts on earlier 5m variants and settlement stopping at the 5-second per-cycle budget across 839 variants. Also clarified that unfilled GTD orders become `Skipped` with `gtd_limit_not_filled`, not `Settled`. No production code changed; the temporary probe was removed.

## 2026-05-13T05:40:00.0000000+00:00
Request:
User asked to implement the proposed settlement fix for the low `1h PreOpen Full` `Settled` count.

Result:
Implemented a global BTC settlement queue across enabled variants instead of sequential per-variant settlement with a 5-second cycle budget. Added a multi-strategy due-run repository query, settlement prioritization for filled/partially-filled/expired runs, bounded concurrent settlement via `BtcUpDown5mStrategy.MaxConcurrentSettlements`, per-cycle Gamma metadata lookup caching, a supporting schema index, docs/config updates, and regression tests showing slow early variants no longer starve `1h PreOpen Full`. Verification passed with focused tests `143/143`, full solution tests `437/437`, normal Debug service build, `git diff --check`, and a service restart with IPC `/status` `Running` and live trading still paused.
