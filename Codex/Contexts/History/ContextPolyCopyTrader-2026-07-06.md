## 2026-07-06T21:01:01.8993110Z
Request:
Remove strategies `BTC Up or Down 5m Up`, `BTC Up or Down 5m Down`, `BTC Up or Down 5m Down Maker 50`, `BTC Up or Down 5m Up Maker`, `BTC Up or Down 5m Down Maker`, and their history.

Result:
Removed the five target BTC strategies from the active catalog and PostgreSQL seed while keeping `BTC Up or Down 5m Up Maker 50`. Deleted obsolete constants/factories for the removed variants, updated tests/docs, and purged both databases. Local PostgreSQL `127.0.0.1` purge deleted `5` strategies, `288` runs, `195` Paper orders, `33` fills, `2,392` signals, `32` positions, and `32` settlements. Server PostgreSQL `192.168.0.101` purge first disabled/paused the target rows to stop new writes from the running service, then deleted `5` strategies, `9,750` runs, `9,460` Paper orders, `518` fills, `34,058` signals, `411` positions, `205` settlements, `5` dashboard snapshots, and `15` recent snapshots. No Live orders were present for the target strategies. Final verification after a short wait showed zero target strategies/history on both databases. Focused tests passed `13/13`, Maker tests passed `10/10`, service build passed with `0` warnings/errors, and `git diff --check` passed with CRLF warnings only. A temporary maintenance utility was removed after use. No staging, commit, or push was performed because the worktree already has unrelated dirty changes. Updated code still needs deployment before the next server service restart to prevent old seed recreation.

## 2026-07-06T05:40:23.0360330Z
Request:
Screenshot showed service startup failing during PostgreSQL schema initialization with `42601: syntax error at or near "b7c50005-0000-4000-8002-000000000030"`.

Result:
Fixed the malformed `INSERT INTO strategies ... VALUES` seed SQL in `src/PolyCopyTrader.Storage/PostgresSchema.cs` by restoring the missing opening tuple parenthesis before `btc_up_down_5m_more_30`. Verified the full schema initializer on a temporary local PostgreSQL database and dropped that database afterward; no production/server database writes were made for validation. Focused tests passed: schema initialization `1/1` and catalog/storage tests `77/77`; `git diff --check -- src/PolyCopyTrader.Storage/PostgresSchema.cs` passed with LF/CRLF warning only. No staging, commit, or push was performed because the worktree already had many unrelated dirty changes, including in the touched file.

## 2026-07-06T05:48:19.5736929Z
Request:
Выложил, проверяй на сервере

Result:
Checked after deployment. The fresh local Debug service log showed the previous PostgreSQL seed syntax error was gone and the service reached normal strategy cycles. Remote PostgreSQL `192.168.0.101:5432` had an open TCP port and `pg_isready` reported accepting connections, but every `psql` attempt to both `polycopytrader` and `postgres` failed with `FATAL: sorry, too many clients already`, so heartbeat/API-error/strategy verification on the server was blocked. Confirmed this machine is `192.168.0.102` and remote process/file inspection of `.101` was not available via WinRM/admin share. No DB writes, service restart, source edits, staging, commit, or push were performed.

## 2026-07-06T05:55:29.8305313Z
Request:
А сейчас?

Result:
Rechecked the server successfully. Remote PostgreSQL `192.168.0.101:5432` accepted connections again. `PolyCopyTrader.Service` heartbeat was fresh: status `Running`, mode `Live`, current loop `BTC5mOnly WatchlistScanner=CommentedOut; FollowLeaderSignals=CommentedOut`, last heartbeat `2026-07-06 08:55:03 +03`, age about `16` seconds, no last error. Restored `Diff FAK Premarket` server rows remained `100` total, `100` enabled, `0` paused, `0` live. Paper processing was active with recent entered/filled orders. No Live orders were created in the latest 20-30 minute window; latest Live order remained `2026-07-05 20:54:30 +03`. Latest Live-enabled strategies were processed but skipped by normal conditions, not by startup/database failure. No DB writes, source edits, service restart, staging, commit, or push were performed.

## 2026-07-06T18:17:38.4107980Z
Request:
Сделай ежедневный отчёт по текущим Live стратегиям. Excel файл с одной вкладкой. Столбцы - стратегии в порядке возрастания Live realized, строки - даты. 
Total по каждой стратегии и по каждой дате и результирующий Total по всем.

Result:
Built `outputs/live-daily-report-2026-07-06/current-live-daily-realized-report.xlsx` from server PostgreSQL `192.168.0.101:5432`. The report has one worksheet with UTC date rows, current Live strategy columns sorted by ascending total Live realised, per-date totals, per-strategy totals, and grand total. At generation time it included `9` current Live strategies, `445` Live orders, date range `2026-06-05` through `2026-07-06`, and grand total `253.73`. Rendered and inspected the workbook preview; no production DB writes, service restarts, staging, commit, or push were performed.

## 2026-07-06T19:33:56.0702838Z
Request:
Удалить все Revert стратегии и их историю.

Result:
Removed active `Diff Revert Premarket` generation from the strategy catalog and PostgreSQL seed so BTC/ETH/SOL Diff Premarket rows are no longer recreated as Revert variants. Purged server PostgreSQL `192.168.0.101`: `40` Revert strategies, `6,960` strategy runs, `1,647` Paper orders, `1,647` Paper fills, `1,647` strategy signals, `1,647` Paper positions, `1,612` Paper position settlements, `40` dashboard snapshots, and `120` recent snapshots; no Live orders were present. A second server orphan-history pass deleted `12,245` old `strategy:%revert%` signals plus `3,400` Paper positions and `3,400` settlements. Purged local PostgreSQL `127.0.0.1`: `45` Revert strategies, `810` runs, `196` Paper orders, `66` Paper fills, `1,541` signals, `63` positions, and `63` settlements, then removed `3` remaining old orphan Revert signals. A final follow-up check found and deleted `120` remaining/recreated server recent dashboard snapshot rows with Revert codes. Final verification on both local and server showed `0` Revert strategies, Paper orders, Live orders, runs, signals, positions, settlements, dashboard snapshots, and recent snapshots. Focused tests passed `75/75`; service build passed; Dashboard build was blocked by running `devenv.exe`/`PolyCopyTrader.Dashboard.exe` file locks. No staging, commit, or push was performed because the worktree already contains unrelated dirty changes. Updated code still needs deployment to the server before the next service restart, otherwise an old deployed binary could recreate old seed rows.

## 2026-07-06T19:58:38.6492737Z
Request:
Удали More стратегии

Result:
Removed active BTC `More` strategy generation from the catalog and PostgreSQL seed so standard `More`, capped `More ... Below`, and `More ... Gamma` rows are no longer registered or seeded. Purged local PostgreSQL `127.0.0.1`: `36` More strategies, `5,163` strategy runs, `3,131` Paper orders, `1,793` Paper fills, `24` inactive Live orders, `21,741` signals, `1,757` Paper positions, and `1,754` settlements. Purged server PostgreSQL `192.168.0.101`: after one deadlock on the first delete attempt, disabled/paused the `29` server More strategies and retried successfully; deleted `29` strategies, `7,203` runs, `6,154` Paper orders, `2,498` Paper fills, `24,660` signals, `2,500` Paper positions, `2,497` settlements, `29` dashboard snapshots, and `87` recent snapshots; no server Live orders were present. Final verification after a short wait showed `0` More strategies, Paper orders, Live orders, runs, signals, positions, settlements, dashboard snapshots, and recent snapshots on both databases. Focused tests passed `17/17`, service build passed with `0` warnings/errors, and `git diff --check` passed with CRLF warnings only. Updated code still needs deployment before the next server service restart to prevent old seed recreation. No staging, commit, or push was performed because the worktree already has unrelated dirty changes.
