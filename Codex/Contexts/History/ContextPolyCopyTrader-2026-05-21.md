## 2026-05-20T21:19:38.4496811+00:00
Request:
Выложил, проверяй

Result:
Verified production after deploy read-only through remote PostgreSQL host `192.168.0.101`. `PolyCopyTrader.Service` is `Running` in `Live` mode on build `info=1.0.0+e547fcd535f2ecbbeafd7259635eda26e5503d0b`, restarted at `2026-05-20T21:13:13Z`, and has fresh heartbeat with empty `last_error`. The new Maker strategy rows exist and are enabled with `live_stakes=false`; Maker runs/orders are `0` as expected because production is in `Live` mode and the strategies are Paper-only. Normal BTC Paper processing continued after restart, Live orders since restart were `0`, Paper/Live-shadow discrepancies for 24h were `0`, market WebSocket recovered to `Connected`, and no API errors appeared after `2026-05-20T21:15:10Z`. No DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-20T21:35:39.7360339+00:00
Request:
Ты меня не понял. Maker-стратегии должны уже работать. Просто не ставить живых ставок, а ставить только Paper ставки для теста

Result:
Corrected BTC 5m Maker behavior so the Maker variants run whenever Paper runtime is enabled, including `Bot:Mode=Live` with `PaperTrading:RunInLiveMode=true`, while still creating only Paper orders. Removed the extra `BotMode.Paper` gate, kept the order path on `CreatePendingOpeningLimitPaperOrder` with `execution_source=btc_updown5m_maker_post_only`, added a Live-mode paper-only regression that verifies no live trading client call and no `live_orders` row, and updated README/configuration docs. Verification passed: targeted BTC/storage tests `152/152`, full tests `512/512`, and `git diff --check` with LF/CRLF warnings only. No DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-21T03:26:49.2170645Z
Request:
Почему BTC Up or Down 5m Up Maker и BTC Up or Down 5m Down Maker не сделали ни одной ставки?

Result:
Checked production PostgreSQL read-only on host `192.168.0.101`. Confirmed Service is `Running`/`Live` on build `info=1.0.0+229ce8ec60e523e95fe833a3ad8071874b43229e`, both Maker strategies are enabled and actively writing skipped `strategy_market_paper_runs`, but Paper orders are `0`. Root cause: every new-maximum Maker candidate was skipped, mostly by the global `opposite_outcome_open_order` guard because other BTC 5m Paper BUY orders already existed on the opposite outcome for the same condition; remaining candidates were skipped as `maker_expiration_elapsed` because the new max arrived after `market_end_utc - 60s`. No DB writes, service restart, live order submission, cancel action, or source-code changes were performed.

## 2026-05-21T03:32:17.0476056Z
Request:
Не понимаю. Если BTC Up or Down 5m Up Maker блокируется текущими ставками, значит должен ставится BTC Up or Down 5m Down Maker и наоборот.

Result:
Clarified with production DB examples that Maker sides are independently edge-triggered and do not switch to the opposite side when one side is blocked. On `btc-updown-5m-1779333600`, Up Maker had `19` new-max attempts from best ask `0.66` to `0.99`, all blocked by a Down Paper BUY from `btc_up_down_5m_binance_15s`, while Down Maker had `0` new-max events. On `btc-updown-5m-1779333300`, Up Maker was blocked by Down Paper BUY orders around `03:15-03:16Z`; Down Maker only got new maxima at `03:19:44Z`, `03:19:47Z`, and `03:19:54Z`, after the `03:19:00Z` Maker cutoff, so it skipped as `maker_expiration_elapsed`. No DB writes, service restart, live order submission, cancel action, or source-code changes were performed.

## 2026-05-21T05:29:55.5053497+00:00
Request:
Добавь в каждую стратегию свойство Paused, которое будет работать так: 1. После каждого проигрыша стратегия делает выборку своих ставок за последние 12 часов. 2. Если за это время она оказалась в минусах, то свойство Paused устанавливается в True и следующие 12 часов стратегия не делает ставок, ни Paper, ни живых. 3. Добавь эту галочку в Dashboard справа от колонки Live, чтобы галочку можно было устанавливать или снимать.

Result:
Added per-strategy `Paused`/`PausedUntilUtc` support across domain models, PostgreSQL schema/repository, runtime settings, Dashboard strategy rows/grid/CSV, Follow Leader signal processing, BTC 5m Paper entry paths, generic Paper settlement handling, and Live settlement handling. After a losing settlement, the repository now sums the strategy's realized Paper and Live PnL over the previous 12 hours and pauses it until `now + 12h` when that window is negative; paused strategies skip new Paper and Live entries. Dashboard now has an editable `Paused` checkbox immediately after `Live`, plus `Paused until`. Verification passed: targeted tests `158/158`, full tests `513/513`, Dashboard build, Service build, and `git diff --check` with LF/CRLF warnings only. No production DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-21T09:32:45.2364920+00:00
Request:
Убери из Dashboard колонки Paused until и убери справа от Live ROI колонки Live tech skip, Live ignored, Live gtd unfilled, Live cancel ignored, Live reject/error

Result:
Removed `Paused until` from the main Dashboard Strategies grid and removed `Live tech skip`, `Live ignored`, `Live gtd unfilled`, `Live cancel ignored`, and `Live reject/error` from both Dashboard strategy DataGrids. Kept the underlying metrics and CSV export intact. Verification passed: the removed headers no longer appear in `MainWindow.xaml`, Dashboard build passed, full tests passed `513/513`, and `git diff --check` passed with LF/CRLF warning only. No production DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-21T11:11:45.2765090Z
Request:
Давай скорректируем правило для паузы стратегии - ставим на паузу только если количество setteled ставок больше 1

Result:
Changed the shared strategy auto-pause decision so a losing settlement pauses the strategy only when the 12-hour realized Paper/Live PnL is negative and the same 12-hour settled row count is greater than `1`. Added `RecentSettledCount` to pause decisions/logs, updated BTC 5m and storage tests, and documented the new rule. Verification passed: targeted tests `155/155`, full tests `515/515`, and `git diff --check` with LF/CRLF warnings only. No production DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-21T13:18:30.0344825Z
Request:
Давай разберёмся с BTC Up or Down 5m Down Maker / BTC Up or Down 5m Up Maker, я так и не понял ситуацию. Сделай мне график за ближайший маркет - как движется стакан, какие ставки мы при этом делаем по этой стратегии

Result:
Added and ran a .NET/Npgsql Maker market report probe. Generated `outputs/maker-market-report/btc-updown-5m-1779369300-maker-report.html` plus ticks/events/orders CSV exports from production PostgreSQL host `192.168.0.101` using read-only SELECT queries. The nearest Maker-active market had 22 archived odds ticks, 1 Up Maker skip, 0 Down Maker events, and 0 Maker paper orders. The Up Maker event moved from previous max ask `0.46` to new max ask `0.53`, computed maker limit `0.52`, and skipped as `opposite_outcome_open_order` because `btc_up_down_5m_binance_30s` already had a pending Down order. Verification passed: probe build succeeded with 0 warnings/errors and the HTML contains the expected SVG/event table. No production DB writes, service restart, live order submission, or cancel action was performed.
