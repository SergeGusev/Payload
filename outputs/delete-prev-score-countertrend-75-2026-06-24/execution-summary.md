# Delete BTC Prev Score Countertrend 75

Target strategy:

- Id: `b7c50005-0000-4000-8025-000000000075`
- Code: `btc_up_down_5m_prev_score_countertrend_75`
- Name: `BTC Up or Down 5m Prev Score Countertrend 75`
- Wallet: `strategy:btc_up_down_5m_prev_score_countertrend_75`
- Database: `polycopytrader` at `192.168.0.101`

## Dry Run

Started: `2026-06-24T18:39:39.7688615Z`

- Strategy matches: 1
- Exact strategy row: 1
- Paper orders: 302
- Paper fills: 300
- Strategy market paper runs: 372
- Live orders: 0
- Open/unsettled live orders guard: 0
- Paper positions: 300
- Paper position settlements: 299
- Signals: 302

No rows were deleted during dry run.

## Execute

Started: `2026-06-24T18:40:12.5939386Z`

- Disabled/de-live-updated strategy rows: 1
- Open/unsettled live orders guard after cache wait: 0
- Deleted strategy market paper runs: 373, including 1 late strategy run
- Deleted paper fills: 301
- Deleted paper position settlements: 300
- Deleted paper positions: 301
- Deleted paper orders: 303
- Deleted signals: 303
- Deleted strategies: 1
- Deleted live orders: 0

## Final Verify

Started: `2026-06-24T18:41:09.3679433Z`

- Strategy matches: 0
- Exact strategy row: 0
- Paper orders: 0
- Paper fills: 0
- Strategy market paper runs: 0
- Dry-run orders: 0
- Live orders: 0
- Open/unsettled live orders: 0
- Paper/live shadow decisions: 0
- Paper/live shadow discrepancies: 0
- Paper positions: 0
- Paper position settlements: 0
- Paper copied trader performance: 0
- Paper copied leader positions: 0
- Paper copied leader activity events: 0
- Polymarket onchain paper signal results: 0
- Signals: 0
- Signal rejections: 0

`error.txt` was not created.

## Local Verification

- `dotnet restore outputs\delete-prev-score-countertrend-75-2026-06-24\DeletePrevScoreCountertrend75.csproj`
- `dotnet build outputs\delete-prev-score-countertrend-75-2026-06-24\DeletePrevScoreCountertrend75.csproj --no-restore`
- `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj --no-restore --filter 'FullyQualifiedName~StorageTests.PostgresSchema_ContainsRequiredTables|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_ExcludeDeletedPreviousScoreCounterTrend75AndAboveVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeEthAndSolBinanceBpsVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeStandardMartinAndGammaBtcVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.ProcessAsync_PreviousScoreCounterTrendBuysUpAfterPreviousDownBiasAtSeventyCents'`
- `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj --no-restore /p:UseSharedCompilation=false`
- `git diff --check`
- `rg -n "prev_score_countertrend_75|Prev Score Countertrend 75|000000000075|SeventyFive|generate_series\(10, 75, 5\)" src README.md`
