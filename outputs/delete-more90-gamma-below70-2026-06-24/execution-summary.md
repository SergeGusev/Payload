# Delete BTC More 90 Gamma Below 70

Target strategy:

- Id: `b7c50005-0000-4000-8022-000000090070`
- Code: `btc_up_down_5m_more_90_gamma_below_70`
- Name: `BTC Up or Down 5m More 90 Gamma Below 70`
- Wallet: `strategy:btc_up_down_5m_more_90_gamma_below_70`
- Database: `polycopytrader` at `192.168.0.101`

## Dry Run

Started: `2026-06-24T19:00:59.3327803Z`

- Strategy matches: 1
- Exact strategy row: 1
- Paper orders: 365
- Paper fills: 280
- Strategy market paper runs: 377
- Dry-run orders: 0
- Live orders: 0
- Open/unsettled live orders guard: 0
- Paper/live shadow rows: 0
- Paper positions: 280
- Paper position settlements: 279
- Copied/performance/onchain rows: 0
- Signals: 365
- Signal rejections: 0

No rows were deleted during dry run.

## Execute

Started: `2026-06-24T19:01:35.4230986Z`

- Disabled/de-live-updated strategy rows: 1
- Open/unsettled live orders guard before/after cache wait: 0
- Deleted strategy market paper runs: 377
- Deleted dry-run orders: 0
- Deleted live orders: 0
- Deleted paper fills: 280
- Deleted paper position settlements: 279
- Deleted paper positions: 280
- Deleted paper orders: 366
- Deleted signal rejections: 0
- Deleted signals: 366
- Deleted strategies: 1
- Deleted shadow/copied/onchain/performance rows: 0

The execute pass saw one extra target paper order and signal compared with the dry-run snapshot; the utility refreshed target ids before deleting orders/signals and removed them in the same execute pass.

## Final Verify

Started: `2026-06-24T19:02:30.5230783Z`

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

- `dotnet restore outputs\delete-more90-gamma-below70-2026-06-24\DeleteMore90GammaBelow70.csproj`
- `dotnet build outputs\delete-more90-gamma-below70-2026-06-24\DeleteMore90GammaBelow70.csproj --no-restore`
- `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj --no-restore --filter "FullyQualifiedName~StorageTests.PostgresSchema_ContainsRequiredTables|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_ExcludeDeletedGammaVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeEthAndSolBinanceBpsVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeStandardMartinAndGammaBtcVariants"`
- `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj --no-restore /p:UseSharedCompilation=false`
- `git diff --check`
- `rg -n "more_90_gamma_below_70|More 90 Gamma Below 70|8022-000000090070|More90GammaBelow70" src README.md`
