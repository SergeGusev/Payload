## 2026-06-12T16:43:46.7659095+00:00
Request:
Оцени 15-ти минутные рынки для всех трёх валют - стоит ли туда соваться?

Result:
Added and ran a read-only C# diagnostic under `outputs/15m-market-assessment-2026-06-12/` against production PostgreSQL and public CLOB book reads. Concluded that 15m should not be enabled for Live: BTC 15m is only a possible Paper-only experiment with strict liquidity/freshness/depth filters, while ETH/SOL 15m remain too sporadic for Live use.
## 2026-06-12T18:34:30.0000000+00:00
Request:
Сделай ежедневный отчёт по текущим Live стратегиям. Excel файл с одной вкладкой. Столбцы - стратегии, строки - даты. Total по каждой стратегии и по каждой дате и результирующий Total по всем.

Result:
Generated `outputs/live-strategy-daily-matrix-2026-06-12/live-strategy-daily-matrix-2026-06-12.xlsx`, a one-sheet `Live Daily PnL` workbook with current Live strategy columns, UTC date rows, per-date totals, per-strategy totals, and a grand total. Snapshot contained 6 current Live strategies, 8 UTC dates, 492 settled live orders, and total PnL 139.09311486. Generator used a read-only repeatable-read production PostgreSQL transaction and passed OpenXML plus Excel COM verification.

## 2026-06-12T17:42:00.0000000+00:00
Request:
Delete all 15-minute strategies including their history; clear history carefully in small batches.

Result:
Added and ran `outputs/delete-15m-strategies-2026-06-12/` C# cleanup against production PostgreSQL. Dry-run found 540 strategies, 41,996 paper orders, 103,080 strategy-market runs, 21,121 fills, 21,121 positions, and 21,109 settlements, with 0 target live/dry/shadow rows and 0 open/unsettled live orders. Cleanup used batch size 1000 and deleted all target rows; independent verification found 0 residual 15m strategies, synthetic 15m wallet rows, and 15m run slugs/titles. Summary saved to `outputs/delete-15m-strategies-2026-06-12/cleanup-summary.txt`. Full test suite passed (`dotnet test PolyCopyTrader.sln --no-restore`, 655/655; existing nullable warnings remain).

## 2026-06-12T21:19:25.4136827+00:00
Request:
Удали все PreOpen стратегии и их историю. Удаляй аккуратно, маленькими порциями

Result:
Added and ran `outputs/delete-preopen-strategies-2026-06-12/` C# cleanup against production PostgreSQL. Dry-run found 720 PreOpen strategies, 128,903 paper orders, 2,816 fills, 133,353 strategy-market runs, 3,720 positions, 3,280 settlements, 960 performance rows, and 0 target live/dry/shadow rows; open/unsettled live-order guard count was 0. Cleanup used batch size 500 and deleted 133,353 runs, 2,816 fills, 3,280 settlements, 3,720 positions, 960 performance rows, 128,903 paper orders, 144,767 PreOpen synthetic-wallet signals, and 720 strategies. Source schema no longer seeds PreOpen rows and the missing-strategy run guard now covers all PreOpen categories. Final verify-only scan found 0 residual PreOpen strategies, paper orders, signals, positions, settlements, performance rows, and run categories. Summary saved to `outputs/delete-preopen-strategies-2026-06-12/cleanup-summary.txt`. Full test suite passed (`dotnet test PolyCopyTrader.sln --no-restore`, 655/655; existing nullable warnings remain).
