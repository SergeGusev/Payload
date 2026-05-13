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

## 2026-05-13T05:58:15.0000000+00:00
Request:
User asked to clear all Paper history and start collecting Paper results again from a clean slate.

Result:
Stopped the Debug service, cleared the Paper execution/accounting/run tables in PostgreSQL, preserved strategies/live orders/market data/leader and signal history/on-chain dedupe audit, and nulled old Paper-order links from live/on-chain audit rows before deleting Paper orders. Cleared tables included `strategy_market_paper_runs`, `paper_orders`, `paper_fills`, `paper_positions`, `paper_position_settlements`, `paper_copied_trader_performance`, `paper_copied_leader_positions`, `paper_copied_leader_activity_events`, `paper_live_shadow_decisions`, and `paper_live_shadow_discrepancies`. Verified all cleared tables were zero before restarting the service. Restarted the service; IPC `/status` returned `Running`, Paper active, Live paused, kill switch false, and `lastError=null`. No code changed and the temporary reset tool was removed.

## 2026-05-13T06:40:02.2207635+00:00
Request:
Нет, меня не устраивает вариант, при котором мы пропускаем ставки из-за медленной работы, так портится статистика. Мне надо понять, что мы можем сделать, чтобы такой медленной работы не было.

Ок, давай делать

Result:
Implemented a BTC PreOpen due-entry throughput fix. PreOpen fixed-direction entries now run through a priority pass that selects the complete earliest due timestamp group without being split by `MaxEntriesPerCycle`, while ordinary BTC entries keep the global capped queue. Added per-cycle CLOB `/book` fallback task caching per asset so a shared PreOpen token makes one fallback request instead of repeating the same slow failure/timeout per strategy. Raised Debug `MaxConcurrentEntryDecisions` to 32, updated docs, added regression coverage for complete PreOpen due-group placement with shared fallback fetch, rebuilt and restarted the Debug service, cleared fresh Paper history again, and verified IPC `/status` is `Running` with Paper active, Live paused, kill switch false, and `lastError=null`. Verification passed with focused BTC processor tests `100/100`, full solution tests `438/438`, normal Debug service build, and `git diff --check` with line-ending warnings only.

## 2026-05-13T06:46:04.2490766+00:00
Request:
Ок, чисть историю и начнём заново

Result:
Stopped the Debug service, cleared Paper execution/accounting history in PostgreSQL, verified the tracked Paper tables were zero before restart, removed the temporary C# reset utility, and restarted the Debug service. Pre-clean counts were `strategy_market_paper_runs=2948`, `paper_orders=524`, and `paper_copied_trader_performance=694`; `paper_fills`, `paper_positions`, `paper_position_settlements`, copied-leader activity, and paper-live-shadow tables were already zero. Preserved strategies, market data, signals, live orders, and on-chain audit/dedupe history, while nulling any `paper_order_id` links before deleting Paper orders. IPC `/status` after restart returned `Running`, Paper active, Live paused, kill switch false, and `lastError=null`.

## 2026-05-13T07:03:55.4737554+00:00
Request:
Ок, последи. Сейчас меня интересует следующий параметр: через какое время после начала принятия решения по ставке эта ставка сделана. Например, если мы ставим за 5 минут до старта рынка - то когда реально поставим. Если стратегия должна срабатывать через 30, 60 и т.д. секунд - то через сколько она реально срабатывает.

Result:
Monitored the fresh Paper sample with a temporary read-only C# Npgsql utility, comparing `strategy_market_paper_runs.entry_due_at_utc` with linked `paper_orders.created_at_utc` / run `entered_at_utc`. Clean 5m PreOpen groups after reset were roughly `4.7s` average and `6.8s` max once restart noise was excluded; 60/90/120/150s offset groups were mostly sub-second to about `6s` in the latest cycle. The large remaining delays were concentrated in Binance/open-family entries: market `btc-updown-5m-1778655300` reached about `75.9s` after due, and the next market `btc-updown-5m-1778655600` still showed about `32.8s` for Binance-at-open. The likely cause is scheduling priority: PreOpen entries for the next market have the same `entry_due_at_utc` as current-market open/Binance entries, and the new PreOpen priority pass can run before those more time-sensitive current-market entries. The temporary utility was removed; no production code changed.

## 2026-05-13T07:15:59.8008082+00:00
Request:
Ты писал: PreOpen для следующего рынка имеет тот же entry_due_at_utc, что и open/Binance стратегии
  текущего рынка. Мы можем сменить приоритеты так, чтобы стратегии для текущего рынка имели приоритет над стратегиями будущих рынков ?

Result:
Changed the BTC entry scheduler so regular/current-market due entries and Martin due entries run before the PreOpen due batch both before and after observation. PreOpen still drains the complete earliest due timestamp group without `MaxEntriesPerCycle` splitting, but it can no longer block same-cycle current-market open/delayed entries with the same due timestamp. Added regression coverage proving a current-market always-up entry is placed before a same-due future PreOpen group while the PreOpen group still drains fully with `MaxEntriesPerCycle=1`. Updated README, ran focused BTC processor tests `101/101`, ran full solution tests `439/439`, rebuilt normal Debug service output, and restarted the service. IPC `/status` after restart returned `Running`, Paper active, Live paused, kill switch false, and `lastError=null`.
