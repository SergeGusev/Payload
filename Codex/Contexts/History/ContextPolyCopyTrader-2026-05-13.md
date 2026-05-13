## 2026-05-13T05:22:05.9805193+00:00
Request:
Почему так мало Settles заявок для рынка 1h PreOpen full ? Разве мы не должны ставить всегда?

Result:
Diagnosed `BTC Up or Down 1h PreOpen Full` with a temporary read-only C# PostgreSQL probe, IPC status, and service logs. Confirmed the strategies are enabled and recent hourly markets are receiving 80 entered Paper orders, so the always-entry path is working. The low `Settled` count is mainly settlement backlog: hundreds of ended 1h PreOpen Full runs remain `Entered`, while logs show repeated Gamma metadata timeouts on earlier 5m variants and settlement stopping at the 5-second per-cycle budget across 839 variants. Also clarified that unfilled GTD orders become `Skipped` with `gtd_limit_not_filled`, not `Settled`. No production code changed; the temporary probe was removed.
