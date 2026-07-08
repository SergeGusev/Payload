# Delete BTC More 120 Gamma Below 70

Target strategy:

- Id: `b7c50005-0000-4000-8022-000000120070`
- Code: `btc_up_down_5m_more_120_gamma_below_70`
- Name: `BTC Up or Down 5m More 120 Gamma Below 70`
- Wallet: `strategy:btc_up_down_5m_more_120_gamma_below_70`
- Database: `polycopytrader` at `192.168.0.101`

## Dry Run

Started: `2026-06-24T18:49:03.3422767Z`

- Strategy matches: 1
- Exact strategy row: 1
- Paper orders: 363
- Paper fills: 269
- Strategy market paper runs: 374
- Dry-run orders: 0
- Live orders: 0
- Open/unsettled live orders guard: 0
- Paper/live shadow rows: 0
- Paper positions: 269
- Paper position settlements: 268
- Copied/performance/onchain rows: 0
- Signals: 363
- Signal rejections: 0

No rows were deleted during dry run.

## Execute

Started: `2026-06-24T18:49:30.3636107Z`

- Disabled/de-live-updated strategy rows: 1
- Open/unsettled live orders guard before/after cache wait: 0
- Deleted strategy market paper runs: 374
- Deleted dry-run orders: 0
- Deleted live orders: 0
- Deleted paper fills: 269
- Deleted paper position settlements: 268
- Deleted paper positions: 269
- Deleted paper orders: 363
- Deleted signal rejections: 0
- Deleted signals: 363
- Deleted strategies: 1
- Deleted shadow/copied/onchain/performance rows: 0

## Final Verify

Started: `2026-06-24T18:50:24.0703076Z`

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

- `dotnet restore outputs\delete-more120-gamma-below70-2026-06-24\DeleteMore120GammaBelow70.csproj`
- `dotnet build outputs\delete-more120-gamma-below70-2026-06-24\DeleteMore120GammaBelow70.csproj --no-restore`
- `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj --no-restore --filter 'FullyQualifiedName~StorageTests.PostgresSchema_ContainsRequiredTables|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_ExcludeDeletedGammaVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeEthAndSolBinanceBpsVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeStandardMartinAndGammaBtcVariants'`
- `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj --no-restore /p:UseSharedCompilation=false`
- `git diff --check`
- `rg -n "more_120_gamma_below_70|More 120 Gamma Below 70|8022-000000120070|More120GammaBelow70" src README.md`
