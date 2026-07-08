# Delete BTC More 150 Gamma Below 70

Target strategy:

- Id: `b7c50005-0000-4000-8022-000000150070`
- Code: `btc_up_down_5m_more_150_gamma_below_70`
- Name: `BTC Up or Down 5m More 150 Gamma Below 70`
- Wallet: `strategy:btc_up_down_5m_more_150_gamma_below_70`
- Database: `polycopytrader` at `192.168.0.101`

## Dry Run

Started: `2026-06-24T18:06:33.6147126Z`

- Strategy matches: 1
- Exact strategy row: 1
- Paper orders: 354
- Paper fills: 242
- Strategy market paper runs: 366
- Live orders: 0
- Open/unsettled live orders guard: 0
- Paper positions: 242
- Paper position settlements: 241
- Signals: 354

No rows were deleted during dry run.

## Execute

Started: `2026-06-24T18:07:13.0128973Z`

- Disabled/de-live-updated strategy rows: 1
- Open/unsettled live orders guard after cache wait: 0
- Deleted strategy market paper runs: 366
- Deleted paper fills: 242
- Deleted paper positions: 242
- Deleted paper orders: 355
- Deleted signals: 355
- Deleted strategies: 1
- Deleted live orders: 0

The first execute pass left 1 late `paper_position_settlements` row for the strategy wallet after the initial cleanup snapshot.

## Follow-up Execute

Started: `2026-06-24T18:08:57.7586770Z`

- Deleted remaining paper position settlements: 1
- All other target-dependent rows: 0 remaining/deleted

## Final Verify

Started: `2026-06-24T18:09:39.4889886Z`

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

- `dotnet restore outputs\delete-more150-gamma-below70-2026-06-24\DeleteMore150GammaBelow70.csproj`
- `dotnet build outputs\delete-more150-gamma-below70-2026-06-24\DeleteMore150GammaBelow70.csproj --no-restore`
- `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj --no-restore --filter 'FullyQualifiedName~StorageTests.PostgresSchema_ContainsRequiredTables|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_ExcludeDeletedGammaVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeEthAndSolBinanceBpsVariants|FullyQualifiedName~BtcUpDown5mPaperStrategyProcessorTests.StrategyIds_IncludeStandardMartinAndGammaBtcVariants'`
- `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj --no-restore /p:UseSharedCompilation=false`
- `git diff --check`
- `rg -n "more_150_gamma_below_70|More 150 Gamma Below 70|000000150070|More150GammaBelow70" src README.md`
