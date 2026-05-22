## 2026-05-22T09:55:00Z
Request:
Dashboard throws `System.InvalidOperationException`: a TwoWay or OneWayToSource binding cannot work on read-only property `AutoLivePaused` of `StrategyPerformanceRow`.

Result:
Fixed `src/PolyCopyTrader.Dashboard/MainWindow.xaml` by changing the `Auto Live Pause` column binding to `Binding="{Binding AutoLivePaused, Mode=OneWay}"`, keeping the column read-only. Normal Dashboard build was blocked because the running Dashboard/Visual Studio locked output DLLs; verified successfully with a temporary output path build: `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj --no-restore -p:BaseOutputPath=%TEMP%\polycopytrader-dashboard-autolive-binding\`, 0 errors and existing Storage nullable warnings only. No production DB writes, live order submissions, or cancel actions were performed.

## 2026-05-22T09:45:00Z
Request:
Change automatic pause policy so only Live bets pause indefinitely; Paper continues and checks 12-hour PnL after later bets/settlements; when recent PnL becomes positive, resume Live and repeat the cycle.

Result:
Implemented separate automatic Live-only pause state. Added `strategies.auto_live_paused`, `StrategyRuntimeSettings.AutoLivePaused`, and `EffectiveLiveStakes = LiveStakes && !AutoLivePaused`. Replaced the old timed `PauseStrategyAfterLossIfRecentPnlNegativeAsync` with `UpdateStrategyAutoLivePauseFromRecentPnlAsync`: after every Paper/Live settlement it sets auto Live pause when 12-hour settled count is greater than 1 and recent PnL is negative, and clears auto Live pause when recent PnL is positive. Manual Dashboard `Paused` remains a full Paper+Live pause. Updated BTC strategy, Follow leader live gating, live settlement, paper settlement, Dashboard filters/readiness/CSV, README, and config docs. Added regression coverage for auto Live pause/resume and Paper continuing without live-shadow orders while auto-paused. Verification passed: targeted tests 165/165, full solution tests 530/530, Dashboard build with 0 warnings/errors, and `git diff --check` with LF/CRLF warnings only. No production DB writes, live order submissions, or cancel actions were performed.

## 2026-05-22T04:29:30.3518283Z
Request:
Проверь свою статистику

Result:
Queried production PostgreSQL read-only at `192.168.0.101` without printing secrets. The service is alive in `Running`/`Live` mode with heartbeat age about `45s`, but it is still on build `info=1.0.0+aad193e9a0279d804a265a7c3c79a88c1f1d9d20`, not the new scanner commit `4c248a2`. The new `btc_up_down_5m_arbitrage_scans` table does not exist in production yet, so the covered-arbitrage scanner has not been deployed/restarted there. Existing `btc_up_down_5m_statistics_ticks` has `175660` rows, but latest sample is `2026-05-19T05:18:12Z`; there were `0` ticks in the last hour/15 minutes. No scanner/statistics API errors appeared in the last hour; only stale Binance ETH/USDT errors from `BtcUpDown5mPaperStrategyProcessor/GetCryptoReferencePrice`. Temporary C# probe was removed. No source code change, DB write, service restart, live order submission, cancel action, or tests were performed.
## 2026-05-22T05:13:52.5797634+00:00
Request:
Верни автоматические паузы. Принцип такой: 1. При каждом поражении делаем выборку по данной стратегии за 12 часов. 2. Если сделано больше одной ставки и выручка со стратегии отрицательная, она уходит в паузу на 12 часов и не делает ставок. 3. Возможность управлять паузами вручную, через галочку - оставь.

Result:
Restored automatic strategy pausing after losing settlements. The service now checks the same strategy's 12-hour settled Paper/Live history, pauses for 12 hours only when settled count is greater than 1 and realized PnL is negative, preserves manual indefinite pauses from the Dashboard checkbox, and no longer clears timed pauses on schema initialization. Updated docs/tests; focused tests passed 163/163, full solution tests passed 528/528, and `git diff --check` passed with LF/CRLF warnings only.
## 2026-05-22T05:27:13.0736118+00:00
Request:
Выложил, проверяй

Result:
Verified production read-only after deploy. The service is running commit `d52d9d5` in `Live` mode with fresh heartbeat and empty `last_error`; schema initialization ran and `btc_up_down_5m_arbitrage_scans` exists. Automatic pauses are working: BTC instant bps `0.1` through `1.4` paused after post-deploy losses at about `2026-05-22T05:20:27Z` until about `2026-05-22T17:20:27Z`, then skipped later entries with `strategy_paused`; SOL non-instant bps `0.1` through `1.1` paused after losses at about `2026-05-22T05:26:51Z` until about `2026-05-22T17:26:51Z`. No pause-related API errors or live events were found. Temporary C# read-only probe was removed.
## 2026-05-22T05:32:29.4040848+00:00
Request:
Твой сбор статистики работает?

Result:
Checked production PostgreSQL read-only. The new `btc_up_down_5m_arbitrage_scans` collection is working: 522 total rows by `2026-05-22T05:32:14Z`, 210 rows in the last 5 minutes, 522 in the last hour, and 4 `would_arbitrage=true` rows in the last hour. The old `btc_up_down_5m_statistics_ticks` feed is not fresh: latest row is still `2026-05-19T05:18:12Z`, with 0 rows in the last hour, because production strategy row `btc_up_down_5m_statistics` has `enabled=false`. No scanner/statistics worker API errors were found in the last 2 hours. Temporary C# read-only probe was removed.
## 2026-05-22T07:14:09.7585350+00:00
Request:
Как дела у нового сканера?

Result:
Checked production PostgreSQL read-only. The new BTC 5m covered-arbitrage scanner is actively writing: `4818` total rows from `2026-05-22T05:20:23Z` through latest sample `2026-05-22T07:13:51Z`; `213` rows in the last 5 minutes, `638` in the last 15 minutes, and `2542` in the last hour. It observed `23` `covered_arbitrage` rows in the last hour and `42` in the last 6 hours, with best observed `net_profit_usd=14.8181`. Last-hour decision mix was `2359` `no_covered_arbitrage`, `160` `missing_asks`, and `23` `covered_arbitrage`; no `BtcUpDown5mArbitrageScannerWorker` API errors were found. Temporary C# read-only probe was removed. Initial DB connection timed out, but PostgreSQL succeeded on retry; IPC port `5118` was not reachable from this machine.
## 2026-05-22T07:36:23.4810450+00:00
Request:
Уже можно сделать какие-то выводы по результатам работы сканера?

Result:
Queried production scanner output read-only. Preliminary conclusion: covered micro-windows exist, but they are extremely short and not yet a live-trading signal. At `2026-05-22T07:36Z`, `btc_up_down_5m_arbitrage_scans` had `5760` rows from `05:20:23Z` to `07:36:00Z` across `28` markets, with `51` raw `would_arbitrage=true` rows across `22` markets and `22` positives in the last hour. Best observed `net_profit_usd=14.8181`, median positive net profit `0.4772`, and p90 `6.4976`; `35/51` positive rows had both order-book ages <= 2 seconds and all were <= 5 seconds. However, positives grouped into `46` episodes with total observed positive duration only `7.39s`, average duration `0.16s`, max duration `1.76s`, and max `2` scans per episode. No scanner worker API errors were found. Temporary C# read-only probe was removed.
## 2026-05-22T08:00:57.2886799Z
Request:
Ок, тогда продолжай исследования и в итоге предложи свой вариант использования перекоса.

Result:
Continued production skew research read-only. At about `2026-05-22T08:00Z`, the scanner had `6792` rows from `05:20:23Z` to `08:00:15Z`, `60` `would_arbitrage=true` rows across `26` markets. Joined scanner rows to BTC odds ticks and settled paper-run outcomes. The settled sample remains small: `18` eligible positive rows across `7` markets, `13` with both books age <= 2 seconds. Buying the cheap side blindly was not a useful directional signal (`27.8%` wins, `+0.07` one-share PnL on the tiny sample). Binance direction alone with abs move >= `2 bps` and age<=2s was negative on first-per-market signals (`9` markets, `66.7%`, `-0.70`). Adding skew improved the small sample: `would_arbitrage=true` plus abs move >= `2 bps` gave `5` first-per-market signals, `100%`, `+0.95`; `top_ask_sum<=0.99` produced the same first-per-market set. Proposed one-sided use: treat skew as a discount/confirmation gate for an independent Binance direction signal, not as a two-sided arbitrage trade. Temporary C# read-only probe was removed; no source behavior or production data was changed.
## 2026-05-22T08:11:42.8020209Z
Request:
Ок, давай продолжай исследование по Binance-направление + реальный перекос

Result:
Continued read-only production research on Binance direction gated by real skew. Scanner had `7265` rows from `2026-05-22T05:20:23Z` to about `08:11:24Z`, `61` positive `would_arbitrage=true` rows across `27` markets. Winner coverage combined live observations and paper runs: `1809` markets, `793` observation-sourced, `1016` paper-sourced, `0` mismatches; the current eligible scanner sample was `1588` rows across `9` markets. Baseline Binance direction with abs move >= `2 bps` and first signal per market was weak: `9` markets, `6` wins, `-0.70` one-share PnL, average ask `0.7444`. Adding real skew (`would_arbitrage=true` or `ask_sum<=0.99`) gave `5/5` first-per-market wins and `+0.95` one-share PnL, but all windows were extremely short: `5` episodes, `6` rows total, `1.3752s` total observed duration. Practical filters: `seconds_to_close>=30` left `3/3`, `+0.84`, avg ask `0.72`; `seconds_to_close>=60` or `ask<=0.85` left `2/2`, `+0.71`, avg ask `0.645`. Recommended only a read-only/Paper shadow strategy for now, not live.
## 2026-05-22T09:16:18.3559147Z
Request:
А текущие Binance стратегии так и работают? Up, если Binance выше старта рынка и Down, если Binance ниже старта рынка ?

Result:
Inspected current code/docs/tests and confirmed the Binance strategy family uses start-relative direction: current reference price above archived market-start reference selects `Up`, below selects `Down`, equality gives no direction and may defer while the opening-limit wait is still active. BTC Binance, bps, Instant bps, fixed-price, delayed, Clever/Edge, and ETH/SOL crypto Binance bps variants share this base direction, with additional variant-specific filters and pricing.

## 2026-05-22T11:03:30.7622196+00:00
Request:
Но Dashboard подключился к Remote database и показыват, что всё нормально. Что можем сделать?

Result:
Changed Dashboard liveness evaluation so a Remote database cannot look healthy because of Dashboard-machine clock skew. Added `IAppRepository.GetDatabaseNowUtcAsync`, implemented it for PostgreSQL with `SELECT clock_timestamp();`, and changed both full and strategies-only Dashboard refresh paths to evaluate `service_heartbeats` freshness against the selected PostgreSQL server clock. Updated README and `docs/configuration_reference.md`, republished Dashboard to `D:\My\Business\PolyMarketPublished\PayloadDashboard`, and left production DB/service state unchanged. Verification passed: Dashboard temp-output build, targeted `StorageTests` 24/24, full solution tests 530/530, publish, and `git diff --check` with LF/CRLF warnings only. Existing Storage nullable warnings remain. The next operational step is to restart the Dashboard from the published folder and restart/recheck the stale production service.

## 2026-05-22T10:21:39.3368559+00:00
Request:
Проверь - живой ли и актуальный ли сервис на бою

Result:
Checked production PostgreSQL read-only with `default_transaction_read_only=on`. At DB time `2026-05-22T10:21:23Z`, `PolyCopyTrader.Service` still had persisted status `Running`/`Live`, but the heartbeat was stale: last heartbeat `2026-05-22T09:39:30.677Z`, about `2513s` old. BTC runtime feeds stopped at the same time: latest arbitrage scan `2026-05-22T09:39:55.932Z`, statistics tick/live observation `2026-05-22T09:39:54.884Z`, odds tick `2026-05-22T09:39:52.630Z`, and `0` rows in the last 15 minutes. Market data status also stopped updating around `09:39:33Z`-`09:39:50Z`; no API errors were recorded in the last 6 hours. Production build is `info=1.0.0+6822553f5fdd20a72ef46f76ea0f6c5f17818403`; local HEAD is `e98d97c`, while latest service/storage/domain/strategy code commit is `577ec0b`, so the production service build includes the latest service code but is not equal to repository HEAD because later commits are Dashboard/context-only. Temporary C# read-only probe was removed; no service state, production data, orders, or cancels were changed.

## 2026-05-22T10:16:53.2546298+00:00
Request:
Как там живёт сбор статистики для новой стратегии? Есть перспектива?

Result:
Checked production PostgreSQL read-only with `default_transaction_read_only=on`. The collection is currently not alive enough to evaluate: the service row still says `Running`/`Live`, but the heartbeat is stale by about 36 minutes with last heartbeat at `2026-05-22T09:39:30Z`, and production is running build `info=1.0.0+6822553f5fdd20a72ef46f76ea0f6c5f17818403` rather than current local `d8e1fc8`. `btc_up_down_5m_arbitrage_scans` has only 15 rows from `2026-05-22T09:39:33Z` through `09:39:55Z`, all `missing_asks`, 0 positives. Related BTC feeds stopped at the same time: statistics ticks latest `09:39:54Z`, odds ticks latest `09:39:52Z`, live observations latest `09:39:54Z`. `market_data_status` also stopped updating around `09:39:33Z`-`09:39:50Z`; no API errors were found in the last 6 hours. Conclusion: the current production statistics collection is stale, so the new strategy's perspective cannot be judged from fresh data until the current service build is restarted/deployed and allowed to collect continuous settled samples. Temporary C# read-only probe was removed; no source behavior, production DB data, service state, orders, or cancels were changed.
