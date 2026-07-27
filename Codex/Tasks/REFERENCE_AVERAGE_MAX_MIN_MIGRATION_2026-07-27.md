# Reference Average Max/Min migration catalog inventory

Generated at UTC: 2026-07-27T12:39:55.5252729+00:00

Local Git HEAD at capture: 2b9849e3bda7d8f0d08c7c21dee6f7a4cc41eb82

Catalog source used for deterministic enumeration: D:\My\Business\PolyMarket\src\PolyCopyTrader.Domain\bin\Release\net10.0\PolyCopyTrader.Domain.dll

Catalog assembly SHA-256: E67FEDD5378050D354B5EFE6E5C23FACD1E0AFD45163CB89D4BB0219E0B48DC2

## Scope and result

This report enumerates every current local catalog strategy whose decision can change when the shared multi-window price Reference Average selector moves from maximum-only behavior to the confirmed maximum/minimum envelope contract.

- Directly affected: 680 variants.
- Indirectly affected through statically linked Reference Average signals: 168 variants.
- Static signal surface: 848 unique IDs, codes, and names.
- Conditional ChildMirror downstream surface: 247 additional unique IDs, codes, and names.
- Expanded migration-impact inventory: 1,095 unique IDs, codes, and names.
- Full local Up/Down catalog inspected: 3,193 variants.
- This is a source/catalog inventory. It is not evidence that the same rows or binary version are deployed in production.

The migration changes Reference Average descriptions and processor behavior, but does not change any affected strategy ID, code, or name. The exact identity list below was enumerated from the existing Release catalog and cross-checked against the current factory formulas and current identity-field diff.

An independent verification built the current worktree Domain project in a protected temporary output and reflected that fresh assembly. Its SHA-256 was E3A883737F8C47C5726BFD6DCA4212ADB23E2ECAC10E07B0AFC4ACD71A0A18D6. Comparison against the static-signal portion produced 848/848 matches, 848 unique IDs, 848 unique codes, zero missing/extra IDs or codes, asset counts BTC 312 / ETH 322 / SOL 214, and exclusion counts single-window 56 / Absolute 480 / Filtered 0.

A separate ChildMirror catalog audit verified all 247 conditional downstream rows against both the factory formula and reflection: 247 unique IDs, codes, and names; zero formula mismatches; BTC 96 / ETH 63 / SOL 88. The static and conditional sets are disjoint.

A final independent post-review reflection used the current-worktree Domain assembly with SHA-256 `9B9040278F4195C4CFBC1C13D87E74AA964A4A093EA2D66E3C366B8CEBBA38EA`. It matched all 1,095 report `ID + code + name` triples exactly: zero missing/extra rows, 1,095 unique IDs/codes/names, no placeholders, and expanded asset counts BTC 408 / ETH 385 / SOL 302.

## Summary counts

| Asset | Direct Reference Average | Direct Optimized | Direct native LowEnter | Direct total | Indirect BpsConfirmed | Indirect DiffConfirmed | Indirect total | Static affected | Conditional ChildMirror | Expanded total |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| BTC | 140 | 60 | 28 | 228 | 56 | 28 | 84 | 312 | 96 | 408 |
| ETH | 84 | 168 | 28 | 280 | 28 | 14 | 42 | 322 | 63 | 385 |
| SOL | 84 | 60 | 28 | 172 | 28 | 14 | 42 | 214 | 88 | 302 |
| **All assets** | **308** | **288** | **84** | **680** | **112** | **56** | **168** | **848** | **247** | **1,095** |

Threshold grids:

- R28 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100]
- O10 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
- D14 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25, 30]

Generic LowerEnter clones preserve the source behavior and linked strategy IDs. Their stable transformation is: GUID second segment 0000 becomes 0001; code token _premarket becomes _lower_enter_premarket; name suffix Premarket becomes LowerEnter Premarket.

## BTC — 312 affected variants

### Direct: Reference Average — 140 variants

Calls GetReferenceAverageBpsThresholdEntryDecisionAsync directly. Includes fixed Up, fixed Down, neutral, and registered generic LowerEnter clones.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8135-000000000101 | btc_up_down_5m_up_bps_1_fak_premarket | BTC Up or Down 5m Up 1 bps Reference Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8135-000000000102 | btc_up_down_5m_up_bps_2_fak_premarket | BTC Up or Down 5m Up 2 bps Reference Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8135-000000000103 | btc_up_down_5m_up_bps_3_fak_premarket | BTC Up or Down 5m Up 3 bps Reference Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8135-000000000104 | btc_up_down_5m_up_bps_4_fak_premarket | BTC Up or Down 5m Up 4 bps Reference Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8135-000000000105 | btc_up_down_5m_up_bps_5_fak_premarket | BTC Up or Down 5m Up 5 bps Reference Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8135-000000000106 | btc_up_down_5m_up_bps_6_fak_premarket | BTC Up or Down 5m Up 6 bps Reference Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8135-000000000107 | btc_up_down_5m_up_bps_7_fak_premarket | BTC Up or Down 5m Up 7 bps Reference Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8135-000000000108 | btc_up_down_5m_up_bps_8_fak_premarket | BTC Up or Down 5m Up 8 bps Reference Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8135-000000000109 | btc_up_down_5m_up_bps_9_fak_premarket | BTC Up or Down 5m Up 9 bps Reference Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8135-000000000110 | btc_up_down_5m_up_bps_10_fak_premarket | BTC Up or Down 5m Up 10 bps Reference Average Premarket |
| 11 | Base | Up | 15 | b7c50005-0000-4000-8135-000000000115 | btc_up_down_5m_up_bps_15_fak_premarket | BTC Up or Down 5m Up 15 bps Reference Average Premarket |
| 12 | Base | Up | 20 | b7c50005-0000-4000-8135-000000000120 | btc_up_down_5m_up_bps_20_fak_premarket | BTC Up or Down 5m Up 20 bps Reference Average Premarket |
| 13 | Base | Up | 25 | b7c50005-0000-4000-8135-000000000125 | btc_up_down_5m_up_bps_25_fak_premarket | BTC Up or Down 5m Up 25 bps Reference Average Premarket |
| 14 | Base | Up | 30 | b7c50005-0000-4000-8135-000000000130 | btc_up_down_5m_up_bps_30_fak_premarket | BTC Up or Down 5m Up 30 bps Reference Average Premarket |
| 15 | Base | Up | 35 | b7c50005-0000-4000-8135-000000000135 | btc_up_down_5m_up_bps_35_fak_premarket | BTC Up or Down 5m Up 35 bps Reference Average Premarket |
| 16 | Base | Up | 40 | b7c50005-0000-4000-8135-000000000140 | btc_up_down_5m_up_bps_40_fak_premarket | BTC Up or Down 5m Up 40 bps Reference Average Premarket |
| 17 | Base | Up | 45 | b7c50005-0000-4000-8135-000000000145 | btc_up_down_5m_up_bps_45_fak_premarket | BTC Up or Down 5m Up 45 bps Reference Average Premarket |
| 18 | Base | Up | 50 | b7c50005-0000-4000-8135-000000000150 | btc_up_down_5m_up_bps_50_fak_premarket | BTC Up or Down 5m Up 50 bps Reference Average Premarket |
| 19 | Base | Up | 55 | b7c50005-0000-4000-8135-000000000155 | btc_up_down_5m_up_bps_55_fak_premarket | BTC Up or Down 5m Up 55 bps Reference Average Premarket |
| 20 | Base | Up | 60 | b7c50005-0000-4000-8135-000000000160 | btc_up_down_5m_up_bps_60_fak_premarket | BTC Up or Down 5m Up 60 bps Reference Average Premarket |
| 21 | Base | Up | 65 | b7c50005-0000-4000-8135-000000000165 | btc_up_down_5m_up_bps_65_fak_premarket | BTC Up or Down 5m Up 65 bps Reference Average Premarket |
| 22 | Base | Up | 70 | b7c50005-0000-4000-8135-000000000170 | btc_up_down_5m_up_bps_70_fak_premarket | BTC Up or Down 5m Up 70 bps Reference Average Premarket |
| 23 | Base | Up | 75 | b7c50005-0000-4000-8135-000000000175 | btc_up_down_5m_up_bps_75_fak_premarket | BTC Up or Down 5m Up 75 bps Reference Average Premarket |
| 24 | Base | Up | 80 | b7c50005-0000-4000-8135-000000000180 | btc_up_down_5m_up_bps_80_fak_premarket | BTC Up or Down 5m Up 80 bps Reference Average Premarket |
| 25 | Base | Up | 85 | b7c50005-0000-4000-8135-000000000185 | btc_up_down_5m_up_bps_85_fak_premarket | BTC Up or Down 5m Up 85 bps Reference Average Premarket |
| 26 | Base | Up | 90 | b7c50005-0000-4000-8135-000000000190 | btc_up_down_5m_up_bps_90_fak_premarket | BTC Up or Down 5m Up 90 bps Reference Average Premarket |
| 27 | Base | Up | 95 | b7c50005-0000-4000-8135-000000000195 | btc_up_down_5m_up_bps_95_fak_premarket | BTC Up or Down 5m Up 95 bps Reference Average Premarket |
| 28 | Base | Up | 100 | b7c50005-0000-4000-8135-000000000200 | btc_up_down_5m_up_bps_100_fak_premarket | BTC Up or Down 5m Up 100 bps Reference Average Premarket |
| 29 | LowerEnter clone | Up | 1 | b7c50005-0001-4000-8135-000000000101 | btc_up_down_5m_up_bps_1_fak_lower_enter_premarket | BTC Up or Down 5m Up 1 bps Reference Average LowerEnter Premarket |
| 30 | LowerEnter clone | Up | 2 | b7c50005-0001-4000-8135-000000000102 | btc_up_down_5m_up_bps_2_fak_lower_enter_premarket | BTC Up or Down 5m Up 2 bps Reference Average LowerEnter Premarket |
| 31 | LowerEnter clone | Up | 3 | b7c50005-0001-4000-8135-000000000103 | btc_up_down_5m_up_bps_3_fak_lower_enter_premarket | BTC Up or Down 5m Up 3 bps Reference Average LowerEnter Premarket |
| 32 | LowerEnter clone | Up | 4 | b7c50005-0001-4000-8135-000000000104 | btc_up_down_5m_up_bps_4_fak_lower_enter_premarket | BTC Up or Down 5m Up 4 bps Reference Average LowerEnter Premarket |
| 33 | LowerEnter clone | Up | 5 | b7c50005-0001-4000-8135-000000000105 | btc_up_down_5m_up_bps_5_fak_lower_enter_premarket | BTC Up or Down 5m Up 5 bps Reference Average LowerEnter Premarket |
| 34 | LowerEnter clone | Up | 6 | b7c50005-0001-4000-8135-000000000106 | btc_up_down_5m_up_bps_6_fak_lower_enter_premarket | BTC Up or Down 5m Up 6 bps Reference Average LowerEnter Premarket |
| 35 | LowerEnter clone | Up | 7 | b7c50005-0001-4000-8135-000000000107 | btc_up_down_5m_up_bps_7_fak_lower_enter_premarket | BTC Up or Down 5m Up 7 bps Reference Average LowerEnter Premarket |
| 36 | LowerEnter clone | Up | 8 | b7c50005-0001-4000-8135-000000000108 | btc_up_down_5m_up_bps_8_fak_lower_enter_premarket | BTC Up or Down 5m Up 8 bps Reference Average LowerEnter Premarket |
| 37 | LowerEnter clone | Up | 9 | b7c50005-0001-4000-8135-000000000109 | btc_up_down_5m_up_bps_9_fak_lower_enter_premarket | BTC Up or Down 5m Up 9 bps Reference Average LowerEnter Premarket |
| 38 | LowerEnter clone | Up | 10 | b7c50005-0001-4000-8135-000000000110 | btc_up_down_5m_up_bps_10_fak_lower_enter_premarket | BTC Up or Down 5m Up 10 bps Reference Average LowerEnter Premarket |
| 39 | LowerEnter clone | Up | 15 | b7c50005-0001-4000-8135-000000000115 | btc_up_down_5m_up_bps_15_fak_lower_enter_premarket | BTC Up or Down 5m Up 15 bps Reference Average LowerEnter Premarket |
| 40 | LowerEnter clone | Up | 20 | b7c50005-0001-4000-8135-000000000120 | btc_up_down_5m_up_bps_20_fak_lower_enter_premarket | BTC Up or Down 5m Up 20 bps Reference Average LowerEnter Premarket |
| 41 | LowerEnter clone | Up | 25 | b7c50005-0001-4000-8135-000000000125 | btc_up_down_5m_up_bps_25_fak_lower_enter_premarket | BTC Up or Down 5m Up 25 bps Reference Average LowerEnter Premarket |
| 42 | LowerEnter clone | Up | 30 | b7c50005-0001-4000-8135-000000000130 | btc_up_down_5m_up_bps_30_fak_lower_enter_premarket | BTC Up or Down 5m Up 30 bps Reference Average LowerEnter Premarket |
| 43 | LowerEnter clone | Up | 35 | b7c50005-0001-4000-8135-000000000135 | btc_up_down_5m_up_bps_35_fak_lower_enter_premarket | BTC Up or Down 5m Up 35 bps Reference Average LowerEnter Premarket |
| 44 | LowerEnter clone | Up | 40 | b7c50005-0001-4000-8135-000000000140 | btc_up_down_5m_up_bps_40_fak_lower_enter_premarket | BTC Up or Down 5m Up 40 bps Reference Average LowerEnter Premarket |
| 45 | LowerEnter clone | Up | 45 | b7c50005-0001-4000-8135-000000000145 | btc_up_down_5m_up_bps_45_fak_lower_enter_premarket | BTC Up or Down 5m Up 45 bps Reference Average LowerEnter Premarket |
| 46 | LowerEnter clone | Up | 50 | b7c50005-0001-4000-8135-000000000150 | btc_up_down_5m_up_bps_50_fak_lower_enter_premarket | BTC Up or Down 5m Up 50 bps Reference Average LowerEnter Premarket |
| 47 | LowerEnter clone | Up | 55 | b7c50005-0001-4000-8135-000000000155 | btc_up_down_5m_up_bps_55_fak_lower_enter_premarket | BTC Up or Down 5m Up 55 bps Reference Average LowerEnter Premarket |
| 48 | LowerEnter clone | Up | 60 | b7c50005-0001-4000-8135-000000000160 | btc_up_down_5m_up_bps_60_fak_lower_enter_premarket | BTC Up or Down 5m Up 60 bps Reference Average LowerEnter Premarket |
| 49 | LowerEnter clone | Up | 65 | b7c50005-0001-4000-8135-000000000165 | btc_up_down_5m_up_bps_65_fak_lower_enter_premarket | BTC Up or Down 5m Up 65 bps Reference Average LowerEnter Premarket |
| 50 | LowerEnter clone | Up | 70 | b7c50005-0001-4000-8135-000000000170 | btc_up_down_5m_up_bps_70_fak_lower_enter_premarket | BTC Up or Down 5m Up 70 bps Reference Average LowerEnter Premarket |
| 51 | LowerEnter clone | Up | 75 | b7c50005-0001-4000-8135-000000000175 | btc_up_down_5m_up_bps_75_fak_lower_enter_premarket | BTC Up or Down 5m Up 75 bps Reference Average LowerEnter Premarket |
| 52 | LowerEnter clone | Up | 80 | b7c50005-0001-4000-8135-000000000180 | btc_up_down_5m_up_bps_80_fak_lower_enter_premarket | BTC Up or Down 5m Up 80 bps Reference Average LowerEnter Premarket |
| 53 | LowerEnter clone | Up | 85 | b7c50005-0001-4000-8135-000000000185 | btc_up_down_5m_up_bps_85_fak_lower_enter_premarket | BTC Up or Down 5m Up 85 bps Reference Average LowerEnter Premarket |
| 54 | LowerEnter clone | Up | 90 | b7c50005-0001-4000-8135-000000000190 | btc_up_down_5m_up_bps_90_fak_lower_enter_premarket | BTC Up or Down 5m Up 90 bps Reference Average LowerEnter Premarket |
| 55 | LowerEnter clone | Up | 95 | b7c50005-0001-4000-8135-000000000195 | btc_up_down_5m_up_bps_95_fak_lower_enter_premarket | BTC Up or Down 5m Up 95 bps Reference Average LowerEnter Premarket |
| 56 | LowerEnter clone | Up | 100 | b7c50005-0001-4000-8135-000000000200 | btc_up_down_5m_up_bps_100_fak_lower_enter_premarket | BTC Up or Down 5m Up 100 bps Reference Average LowerEnter Premarket |
| 57 | Base | Down | 1 | b7c50005-0000-4000-8136-000000000101 | btc_up_down_5m_down_bps_1_fak_premarket | BTC Up or Down 5m Down 1 bps Reference Average Premarket |
| 58 | Base | Down | 2 | b7c50005-0000-4000-8136-000000000102 | btc_up_down_5m_down_bps_2_fak_premarket | BTC Up or Down 5m Down 2 bps Reference Average Premarket |
| 59 | Base | Down | 3 | b7c50005-0000-4000-8136-000000000103 | btc_up_down_5m_down_bps_3_fak_premarket | BTC Up or Down 5m Down 3 bps Reference Average Premarket |
| 60 | Base | Down | 4 | b7c50005-0000-4000-8136-000000000104 | btc_up_down_5m_down_bps_4_fak_premarket | BTC Up or Down 5m Down 4 bps Reference Average Premarket |
| 61 | Base | Down | 5 | b7c50005-0000-4000-8136-000000000105 | btc_up_down_5m_down_bps_5_fak_premarket | BTC Up or Down 5m Down 5 bps Reference Average Premarket |
| 62 | Base | Down | 6 | b7c50005-0000-4000-8136-000000000106 | btc_up_down_5m_down_bps_6_fak_premarket | BTC Up or Down 5m Down 6 bps Reference Average Premarket |
| 63 | Base | Down | 7 | b7c50005-0000-4000-8136-000000000107 | btc_up_down_5m_down_bps_7_fak_premarket | BTC Up or Down 5m Down 7 bps Reference Average Premarket |
| 64 | Base | Down | 8 | b7c50005-0000-4000-8136-000000000108 | btc_up_down_5m_down_bps_8_fak_premarket | BTC Up or Down 5m Down 8 bps Reference Average Premarket |
| 65 | Base | Down | 9 | b7c50005-0000-4000-8136-000000000109 | btc_up_down_5m_down_bps_9_fak_premarket | BTC Up or Down 5m Down 9 bps Reference Average Premarket |
| 66 | Base | Down | 10 | b7c50005-0000-4000-8136-000000000110 | btc_up_down_5m_down_bps_10_fak_premarket | BTC Up or Down 5m Down 10 bps Reference Average Premarket |
| 67 | Base | Down | 15 | b7c50005-0000-4000-8136-000000000115 | btc_up_down_5m_down_bps_15_fak_premarket | BTC Up or Down 5m Down 15 bps Reference Average Premarket |
| 68 | Base | Down | 20 | b7c50005-0000-4000-8136-000000000120 | btc_up_down_5m_down_bps_20_fak_premarket | BTC Up or Down 5m Down 20 bps Reference Average Premarket |
| 69 | Base | Down | 25 | b7c50005-0000-4000-8136-000000000125 | btc_up_down_5m_down_bps_25_fak_premarket | BTC Up or Down 5m Down 25 bps Reference Average Premarket |
| 70 | Base | Down | 30 | b7c50005-0000-4000-8136-000000000130 | btc_up_down_5m_down_bps_30_fak_premarket | BTC Up or Down 5m Down 30 bps Reference Average Premarket |
| 71 | Base | Down | 35 | b7c50005-0000-4000-8136-000000000135 | btc_up_down_5m_down_bps_35_fak_premarket | BTC Up or Down 5m Down 35 bps Reference Average Premarket |
| 72 | Base | Down | 40 | b7c50005-0000-4000-8136-000000000140 | btc_up_down_5m_down_bps_40_fak_premarket | BTC Up or Down 5m Down 40 bps Reference Average Premarket |
| 73 | Base | Down | 45 | b7c50005-0000-4000-8136-000000000145 | btc_up_down_5m_down_bps_45_fak_premarket | BTC Up or Down 5m Down 45 bps Reference Average Premarket |
| 74 | Base | Down | 50 | b7c50005-0000-4000-8136-000000000150 | btc_up_down_5m_down_bps_50_fak_premarket | BTC Up or Down 5m Down 50 bps Reference Average Premarket |
| 75 | Base | Down | 55 | b7c50005-0000-4000-8136-000000000155 | btc_up_down_5m_down_bps_55_fak_premarket | BTC Up or Down 5m Down 55 bps Reference Average Premarket |
| 76 | Base | Down | 60 | b7c50005-0000-4000-8136-000000000160 | btc_up_down_5m_down_bps_60_fak_premarket | BTC Up or Down 5m Down 60 bps Reference Average Premarket |
| 77 | Base | Down | 65 | b7c50005-0000-4000-8136-000000000165 | btc_up_down_5m_down_bps_65_fak_premarket | BTC Up or Down 5m Down 65 bps Reference Average Premarket |
| 78 | Base | Down | 70 | b7c50005-0000-4000-8136-000000000170 | btc_up_down_5m_down_bps_70_fak_premarket | BTC Up or Down 5m Down 70 bps Reference Average Premarket |
| 79 | Base | Down | 75 | b7c50005-0000-4000-8136-000000000175 | btc_up_down_5m_down_bps_75_fak_premarket | BTC Up or Down 5m Down 75 bps Reference Average Premarket |
| 80 | Base | Down | 80 | b7c50005-0000-4000-8136-000000000180 | btc_up_down_5m_down_bps_80_fak_premarket | BTC Up or Down 5m Down 80 bps Reference Average Premarket |
| 81 | Base | Down | 85 | b7c50005-0000-4000-8136-000000000185 | btc_up_down_5m_down_bps_85_fak_premarket | BTC Up or Down 5m Down 85 bps Reference Average Premarket |
| 82 | Base | Down | 90 | b7c50005-0000-4000-8136-000000000190 | btc_up_down_5m_down_bps_90_fak_premarket | BTC Up or Down 5m Down 90 bps Reference Average Premarket |
| 83 | Base | Down | 95 | b7c50005-0000-4000-8136-000000000195 | btc_up_down_5m_down_bps_95_fak_premarket | BTC Up or Down 5m Down 95 bps Reference Average Premarket |
| 84 | Base | Down | 100 | b7c50005-0000-4000-8136-000000000200 | btc_up_down_5m_down_bps_100_fak_premarket | BTC Up or Down 5m Down 100 bps Reference Average Premarket |
| 85 | LowerEnter clone | Down | 1 | b7c50005-0001-4000-8136-000000000101 | btc_up_down_5m_down_bps_1_fak_lower_enter_premarket | BTC Up or Down 5m Down 1 bps Reference Average LowerEnter Premarket |
| 86 | LowerEnter clone | Down | 2 | b7c50005-0001-4000-8136-000000000102 | btc_up_down_5m_down_bps_2_fak_lower_enter_premarket | BTC Up or Down 5m Down 2 bps Reference Average LowerEnter Premarket |
| 87 | LowerEnter clone | Down | 3 | b7c50005-0001-4000-8136-000000000103 | btc_up_down_5m_down_bps_3_fak_lower_enter_premarket | BTC Up or Down 5m Down 3 bps Reference Average LowerEnter Premarket |
| 88 | LowerEnter clone | Down | 4 | b7c50005-0001-4000-8136-000000000104 | btc_up_down_5m_down_bps_4_fak_lower_enter_premarket | BTC Up or Down 5m Down 4 bps Reference Average LowerEnter Premarket |
| 89 | LowerEnter clone | Down | 5 | b7c50005-0001-4000-8136-000000000105 | btc_up_down_5m_down_bps_5_fak_lower_enter_premarket | BTC Up or Down 5m Down 5 bps Reference Average LowerEnter Premarket |
| 90 | LowerEnter clone | Down | 6 | b7c50005-0001-4000-8136-000000000106 | btc_up_down_5m_down_bps_6_fak_lower_enter_premarket | BTC Up or Down 5m Down 6 bps Reference Average LowerEnter Premarket |
| 91 | LowerEnter clone | Down | 7 | b7c50005-0001-4000-8136-000000000107 | btc_up_down_5m_down_bps_7_fak_lower_enter_premarket | BTC Up or Down 5m Down 7 bps Reference Average LowerEnter Premarket |
| 92 | LowerEnter clone | Down | 8 | b7c50005-0001-4000-8136-000000000108 | btc_up_down_5m_down_bps_8_fak_lower_enter_premarket | BTC Up or Down 5m Down 8 bps Reference Average LowerEnter Premarket |
| 93 | LowerEnter clone | Down | 9 | b7c50005-0001-4000-8136-000000000109 | btc_up_down_5m_down_bps_9_fak_lower_enter_premarket | BTC Up or Down 5m Down 9 bps Reference Average LowerEnter Premarket |
| 94 | LowerEnter clone | Down | 10 | b7c50005-0001-4000-8136-000000000110 | btc_up_down_5m_down_bps_10_fak_lower_enter_premarket | BTC Up or Down 5m Down 10 bps Reference Average LowerEnter Premarket |
| 95 | LowerEnter clone | Down | 15 | b7c50005-0001-4000-8136-000000000115 | btc_up_down_5m_down_bps_15_fak_lower_enter_premarket | BTC Up or Down 5m Down 15 bps Reference Average LowerEnter Premarket |
| 96 | LowerEnter clone | Down | 20 | b7c50005-0001-4000-8136-000000000120 | btc_up_down_5m_down_bps_20_fak_lower_enter_premarket | BTC Up or Down 5m Down 20 bps Reference Average LowerEnter Premarket |
| 97 | LowerEnter clone | Down | 25 | b7c50005-0001-4000-8136-000000000125 | btc_up_down_5m_down_bps_25_fak_lower_enter_premarket | BTC Up or Down 5m Down 25 bps Reference Average LowerEnter Premarket |
| 98 | LowerEnter clone | Down | 30 | b7c50005-0001-4000-8136-000000000130 | btc_up_down_5m_down_bps_30_fak_lower_enter_premarket | BTC Up or Down 5m Down 30 bps Reference Average LowerEnter Premarket |
| 99 | LowerEnter clone | Down | 35 | b7c50005-0001-4000-8136-000000000135 | btc_up_down_5m_down_bps_35_fak_lower_enter_premarket | BTC Up or Down 5m Down 35 bps Reference Average LowerEnter Premarket |
| 100 | LowerEnter clone | Down | 40 | b7c50005-0001-4000-8136-000000000140 | btc_up_down_5m_down_bps_40_fak_lower_enter_premarket | BTC Up or Down 5m Down 40 bps Reference Average LowerEnter Premarket |
| 101 | LowerEnter clone | Down | 45 | b7c50005-0001-4000-8136-000000000145 | btc_up_down_5m_down_bps_45_fak_lower_enter_premarket | BTC Up or Down 5m Down 45 bps Reference Average LowerEnter Premarket |
| 102 | LowerEnter clone | Down | 50 | b7c50005-0001-4000-8136-000000000150 | btc_up_down_5m_down_bps_50_fak_lower_enter_premarket | BTC Up or Down 5m Down 50 bps Reference Average LowerEnter Premarket |
| 103 | LowerEnter clone | Down | 55 | b7c50005-0001-4000-8136-000000000155 | btc_up_down_5m_down_bps_55_fak_lower_enter_premarket | BTC Up or Down 5m Down 55 bps Reference Average LowerEnter Premarket |
| 104 | LowerEnter clone | Down | 60 | b7c50005-0001-4000-8136-000000000160 | btc_up_down_5m_down_bps_60_fak_lower_enter_premarket | BTC Up or Down 5m Down 60 bps Reference Average LowerEnter Premarket |
| 105 | LowerEnter clone | Down | 65 | b7c50005-0001-4000-8136-000000000165 | btc_up_down_5m_down_bps_65_fak_lower_enter_premarket | BTC Up or Down 5m Down 65 bps Reference Average LowerEnter Premarket |
| 106 | LowerEnter clone | Down | 70 | b7c50005-0001-4000-8136-000000000170 | btc_up_down_5m_down_bps_70_fak_lower_enter_premarket | BTC Up or Down 5m Down 70 bps Reference Average LowerEnter Premarket |
| 107 | LowerEnter clone | Down | 75 | b7c50005-0001-4000-8136-000000000175 | btc_up_down_5m_down_bps_75_fak_lower_enter_premarket | BTC Up or Down 5m Down 75 bps Reference Average LowerEnter Premarket |
| 108 | LowerEnter clone | Down | 80 | b7c50005-0001-4000-8136-000000000180 | btc_up_down_5m_down_bps_80_fak_lower_enter_premarket | BTC Up or Down 5m Down 80 bps Reference Average LowerEnter Premarket |
| 109 | LowerEnter clone | Down | 85 | b7c50005-0001-4000-8136-000000000185 | btc_up_down_5m_down_bps_85_fak_lower_enter_premarket | BTC Up or Down 5m Down 85 bps Reference Average LowerEnter Premarket |
| 110 | LowerEnter clone | Down | 90 | b7c50005-0001-4000-8136-000000000190 | btc_up_down_5m_down_bps_90_fak_lower_enter_premarket | BTC Up or Down 5m Down 90 bps Reference Average LowerEnter Premarket |
| 111 | LowerEnter clone | Down | 95 | b7c50005-0001-4000-8136-000000000195 | btc_up_down_5m_down_bps_95_fak_lower_enter_premarket | BTC Up or Down 5m Down 95 bps Reference Average LowerEnter Premarket |
| 112 | LowerEnter clone | Down | 100 | b7c50005-0001-4000-8136-000000000200 | btc_up_down_5m_down_bps_100_fak_lower_enter_premarket | BTC Up or Down 5m Down 100 bps Reference Average LowerEnter Premarket |
| 113 | Base | Neutral | 1 | b7c50005-0000-4000-8178-000000000101 | btc_up_down_5m_reference_average_bps_1_fak_premarket | BTC Up or Down 5m 1 bps Reference Average Premarket |
| 114 | Base | Neutral | 2 | b7c50005-0000-4000-8178-000000000102 | btc_up_down_5m_reference_average_bps_2_fak_premarket | BTC Up or Down 5m 2 bps Reference Average Premarket |
| 115 | Base | Neutral | 3 | b7c50005-0000-4000-8178-000000000103 | btc_up_down_5m_reference_average_bps_3_fak_premarket | BTC Up or Down 5m 3 bps Reference Average Premarket |
| 116 | Base | Neutral | 4 | b7c50005-0000-4000-8178-000000000104 | btc_up_down_5m_reference_average_bps_4_fak_premarket | BTC Up or Down 5m 4 bps Reference Average Premarket |
| 117 | Base | Neutral | 5 | b7c50005-0000-4000-8178-000000000105 | btc_up_down_5m_reference_average_bps_5_fak_premarket | BTC Up or Down 5m 5 bps Reference Average Premarket |
| 118 | Base | Neutral | 6 | b7c50005-0000-4000-8178-000000000106 | btc_up_down_5m_reference_average_bps_6_fak_premarket | BTC Up or Down 5m 6 bps Reference Average Premarket |
| 119 | Base | Neutral | 7 | b7c50005-0000-4000-8178-000000000107 | btc_up_down_5m_reference_average_bps_7_fak_premarket | BTC Up or Down 5m 7 bps Reference Average Premarket |
| 120 | Base | Neutral | 8 | b7c50005-0000-4000-8178-000000000108 | btc_up_down_5m_reference_average_bps_8_fak_premarket | BTC Up or Down 5m 8 bps Reference Average Premarket |
| 121 | Base | Neutral | 9 | b7c50005-0000-4000-8178-000000000109 | btc_up_down_5m_reference_average_bps_9_fak_premarket | BTC Up or Down 5m 9 bps Reference Average Premarket |
| 122 | Base | Neutral | 10 | b7c50005-0000-4000-8178-000000000110 | btc_up_down_5m_reference_average_bps_10_fak_premarket | BTC Up or Down 5m 10 bps Reference Average Premarket |
| 123 | Base | Neutral | 15 | b7c50005-0000-4000-8178-000000000115 | btc_up_down_5m_reference_average_bps_15_fak_premarket | BTC Up or Down 5m 15 bps Reference Average Premarket |
| 124 | Base | Neutral | 20 | b7c50005-0000-4000-8178-000000000120 | btc_up_down_5m_reference_average_bps_20_fak_premarket | BTC Up or Down 5m 20 bps Reference Average Premarket |
| 125 | Base | Neutral | 25 | b7c50005-0000-4000-8178-000000000125 | btc_up_down_5m_reference_average_bps_25_fak_premarket | BTC Up or Down 5m 25 bps Reference Average Premarket |
| 126 | Base | Neutral | 30 | b7c50005-0000-4000-8178-000000000130 | btc_up_down_5m_reference_average_bps_30_fak_premarket | BTC Up or Down 5m 30 bps Reference Average Premarket |
| 127 | Base | Neutral | 35 | b7c50005-0000-4000-8178-000000000135 | btc_up_down_5m_reference_average_bps_35_fak_premarket | BTC Up or Down 5m 35 bps Reference Average Premarket |
| 128 | Base | Neutral | 40 | b7c50005-0000-4000-8178-000000000140 | btc_up_down_5m_reference_average_bps_40_fak_premarket | BTC Up or Down 5m 40 bps Reference Average Premarket |
| 129 | Base | Neutral | 45 | b7c50005-0000-4000-8178-000000000145 | btc_up_down_5m_reference_average_bps_45_fak_premarket | BTC Up or Down 5m 45 bps Reference Average Premarket |
| 130 | Base | Neutral | 50 | b7c50005-0000-4000-8178-000000000150 | btc_up_down_5m_reference_average_bps_50_fak_premarket | BTC Up or Down 5m 50 bps Reference Average Premarket |
| 131 | Base | Neutral | 55 | b7c50005-0000-4000-8178-000000000155 | btc_up_down_5m_reference_average_bps_55_fak_premarket | BTC Up or Down 5m 55 bps Reference Average Premarket |
| 132 | Base | Neutral | 60 | b7c50005-0000-4000-8178-000000000160 | btc_up_down_5m_reference_average_bps_60_fak_premarket | BTC Up or Down 5m 60 bps Reference Average Premarket |
| 133 | Base | Neutral | 65 | b7c50005-0000-4000-8178-000000000165 | btc_up_down_5m_reference_average_bps_65_fak_premarket | BTC Up or Down 5m 65 bps Reference Average Premarket |
| 134 | Base | Neutral | 70 | b7c50005-0000-4000-8178-000000000170 | btc_up_down_5m_reference_average_bps_70_fak_premarket | BTC Up or Down 5m 70 bps Reference Average Premarket |
| 135 | Base | Neutral | 75 | b7c50005-0000-4000-8178-000000000175 | btc_up_down_5m_reference_average_bps_75_fak_premarket | BTC Up or Down 5m 75 bps Reference Average Premarket |
| 136 | Base | Neutral | 80 | b7c50005-0000-4000-8178-000000000180 | btc_up_down_5m_reference_average_bps_80_fak_premarket | BTC Up or Down 5m 80 bps Reference Average Premarket |
| 137 | Base | Neutral | 85 | b7c50005-0000-4000-8178-000000000185 | btc_up_down_5m_reference_average_bps_85_fak_premarket | BTC Up or Down 5m 85 bps Reference Average Premarket |
| 138 | Base | Neutral | 90 | b7c50005-0000-4000-8178-000000000190 | btc_up_down_5m_reference_average_bps_90_fak_premarket | BTC Up or Down 5m 90 bps Reference Average Premarket |
| 139 | Base | Neutral | 95 | b7c50005-0000-4000-8178-000000000195 | btc_up_down_5m_reference_average_bps_95_fak_premarket | BTC Up or Down 5m 95 bps Reference Average Premarket |
| 140 | Base | Neutral | 100 | b7c50005-0000-4000-8178-000000000200 | btc_up_down_5m_reference_average_bps_100_fak_premarket | BTC Up or Down 5m 100 bps Reference Average Premarket |

### Direct: Optimized Reference Average — 60 variants

Calls the shared selector directly and additionally requires the direction-relevant selected boundary window to be 3h.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8219-000000000101 | btc_up_down_5m_up_optimized_average_bps_1_fak_premarket | BTC Up or Down 5m Up 1 bps Optimized Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8219-000000000102 | btc_up_down_5m_up_optimized_average_bps_2_fak_premarket | BTC Up or Down 5m Up 2 bps Optimized Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8219-000000000103 | btc_up_down_5m_up_optimized_average_bps_3_fak_premarket | BTC Up or Down 5m Up 3 bps Optimized Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8219-000000000104 | btc_up_down_5m_up_optimized_average_bps_4_fak_premarket | BTC Up or Down 5m Up 4 bps Optimized Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8219-000000000105 | btc_up_down_5m_up_optimized_average_bps_5_fak_premarket | BTC Up or Down 5m Up 5 bps Optimized Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8219-000000000106 | btc_up_down_5m_up_optimized_average_bps_6_fak_premarket | BTC Up or Down 5m Up 6 bps Optimized Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8219-000000000107 | btc_up_down_5m_up_optimized_average_bps_7_fak_premarket | BTC Up or Down 5m Up 7 bps Optimized Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8219-000000000108 | btc_up_down_5m_up_optimized_average_bps_8_fak_premarket | BTC Up or Down 5m Up 8 bps Optimized Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8219-000000000109 | btc_up_down_5m_up_optimized_average_bps_9_fak_premarket | BTC Up or Down 5m Up 9 bps Optimized Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8219-000000000110 | btc_up_down_5m_up_optimized_average_bps_10_fak_premarket | BTC Up or Down 5m Up 10 bps Optimized Average Premarket |
| 11 | LowerEnter clone | Up | 1 | b7c50005-0001-4000-8219-000000000101 | btc_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket | BTC Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket |
| 12 | LowerEnter clone | Up | 2 | b7c50005-0001-4000-8219-000000000102 | btc_up_down_5m_up_optimized_average_bps_2_fak_lower_enter_premarket | BTC Up or Down 5m Up 2 bps Optimized Average LowerEnter Premarket |
| 13 | LowerEnter clone | Up | 3 | b7c50005-0001-4000-8219-000000000103 | btc_up_down_5m_up_optimized_average_bps_3_fak_lower_enter_premarket | BTC Up or Down 5m Up 3 bps Optimized Average LowerEnter Premarket |
| 14 | LowerEnter clone | Up | 4 | b7c50005-0001-4000-8219-000000000104 | btc_up_down_5m_up_optimized_average_bps_4_fak_lower_enter_premarket | BTC Up or Down 5m Up 4 bps Optimized Average LowerEnter Premarket |
| 15 | LowerEnter clone | Up | 5 | b7c50005-0001-4000-8219-000000000105 | btc_up_down_5m_up_optimized_average_bps_5_fak_lower_enter_premarket | BTC Up or Down 5m Up 5 bps Optimized Average LowerEnter Premarket |
| 16 | LowerEnter clone | Up | 6 | b7c50005-0001-4000-8219-000000000106 | btc_up_down_5m_up_optimized_average_bps_6_fak_lower_enter_premarket | BTC Up or Down 5m Up 6 bps Optimized Average LowerEnter Premarket |
| 17 | LowerEnter clone | Up | 7 | b7c50005-0001-4000-8219-000000000107 | btc_up_down_5m_up_optimized_average_bps_7_fak_lower_enter_premarket | BTC Up or Down 5m Up 7 bps Optimized Average LowerEnter Premarket |
| 18 | LowerEnter clone | Up | 8 | b7c50005-0001-4000-8219-000000000108 | btc_up_down_5m_up_optimized_average_bps_8_fak_lower_enter_premarket | BTC Up or Down 5m Up 8 bps Optimized Average LowerEnter Premarket |
| 19 | LowerEnter clone | Up | 9 | b7c50005-0001-4000-8219-000000000109 | btc_up_down_5m_up_optimized_average_bps_9_fak_lower_enter_premarket | BTC Up or Down 5m Up 9 bps Optimized Average LowerEnter Premarket |
| 20 | LowerEnter clone | Up | 10 | b7c50005-0001-4000-8219-000000000110 | btc_up_down_5m_up_optimized_average_bps_10_fak_lower_enter_premarket | BTC Up or Down 5m Up 10 bps Optimized Average LowerEnter Premarket |
| 21 | Base | Down | 1 | b7c50005-0000-4000-8212-000000000101 | btc_up_down_5m_down_optimized_average_bps_1_fak_premarket | BTC Up or Down 5m Down 1 bps Optimized Average Premarket |
| 22 | Base | Down | 2 | b7c50005-0000-4000-8212-000000000102 | btc_up_down_5m_down_optimized_average_bps_2_fak_premarket | BTC Up or Down 5m Down 2 bps Optimized Average Premarket |
| 23 | Base | Down | 3 | b7c50005-0000-4000-8212-000000000103 | btc_up_down_5m_down_optimized_average_bps_3_fak_premarket | BTC Up or Down 5m Down 3 bps Optimized Average Premarket |
| 24 | Base | Down | 4 | b7c50005-0000-4000-8212-000000000104 | btc_up_down_5m_down_optimized_average_bps_4_fak_premarket | BTC Up or Down 5m Down 4 bps Optimized Average Premarket |
| 25 | Base | Down | 5 | b7c50005-0000-4000-8212-000000000105 | btc_up_down_5m_down_optimized_average_bps_5_fak_premarket | BTC Up or Down 5m Down 5 bps Optimized Average Premarket |
| 26 | Base | Down | 6 | b7c50005-0000-4000-8212-000000000106 | btc_up_down_5m_down_optimized_average_bps_6_fak_premarket | BTC Up or Down 5m Down 6 bps Optimized Average Premarket |
| 27 | Base | Down | 7 | b7c50005-0000-4000-8212-000000000107 | btc_up_down_5m_down_optimized_average_bps_7_fak_premarket | BTC Up or Down 5m Down 7 bps Optimized Average Premarket |
| 28 | Base | Down | 8 | b7c50005-0000-4000-8212-000000000108 | btc_up_down_5m_down_optimized_average_bps_8_fak_premarket | BTC Up or Down 5m Down 8 bps Optimized Average Premarket |
| 29 | Base | Down | 9 | b7c50005-0000-4000-8212-000000000109 | btc_up_down_5m_down_optimized_average_bps_9_fak_premarket | BTC Up or Down 5m Down 9 bps Optimized Average Premarket |
| 30 | Base | Down | 10 | b7c50005-0000-4000-8212-000000000110 | btc_up_down_5m_down_optimized_average_bps_10_fak_premarket | BTC Up or Down 5m Down 10 bps Optimized Average Premarket |
| 31 | LowerEnter clone | Down | 1 | b7c50005-0001-4000-8212-000000000101 | btc_up_down_5m_down_optimized_average_bps_1_fak_lower_enter_premarket | BTC Up or Down 5m Down 1 bps Optimized Average LowerEnter Premarket |
| 32 | LowerEnter clone | Down | 2 | b7c50005-0001-4000-8212-000000000102 | btc_up_down_5m_down_optimized_average_bps_2_fak_lower_enter_premarket | BTC Up or Down 5m Down 2 bps Optimized Average LowerEnter Premarket |
| 33 | LowerEnter clone | Down | 3 | b7c50005-0001-4000-8212-000000000103 | btc_up_down_5m_down_optimized_average_bps_3_fak_lower_enter_premarket | BTC Up or Down 5m Down 3 bps Optimized Average LowerEnter Premarket |
| 34 | LowerEnter clone | Down | 4 | b7c50005-0001-4000-8212-000000000104 | btc_up_down_5m_down_optimized_average_bps_4_fak_lower_enter_premarket | BTC Up or Down 5m Down 4 bps Optimized Average LowerEnter Premarket |
| 35 | LowerEnter clone | Down | 5 | b7c50005-0001-4000-8212-000000000105 | btc_up_down_5m_down_optimized_average_bps_5_fak_lower_enter_premarket | BTC Up or Down 5m Down 5 bps Optimized Average LowerEnter Premarket |
| 36 | LowerEnter clone | Down | 6 | b7c50005-0001-4000-8212-000000000106 | btc_up_down_5m_down_optimized_average_bps_6_fak_lower_enter_premarket | BTC Up or Down 5m Down 6 bps Optimized Average LowerEnter Premarket |
| 37 | LowerEnter clone | Down | 7 | b7c50005-0001-4000-8212-000000000107 | btc_up_down_5m_down_optimized_average_bps_7_fak_lower_enter_premarket | BTC Up or Down 5m Down 7 bps Optimized Average LowerEnter Premarket |
| 38 | LowerEnter clone | Down | 8 | b7c50005-0001-4000-8212-000000000108 | btc_up_down_5m_down_optimized_average_bps_8_fak_lower_enter_premarket | BTC Up or Down 5m Down 8 bps Optimized Average LowerEnter Premarket |
| 39 | LowerEnter clone | Down | 9 | b7c50005-0001-4000-8212-000000000109 | btc_up_down_5m_down_optimized_average_bps_9_fak_lower_enter_premarket | BTC Up or Down 5m Down 9 bps Optimized Average LowerEnter Premarket |
| 40 | LowerEnter clone | Down | 10 | b7c50005-0001-4000-8212-000000000110 | btc_up_down_5m_down_optimized_average_bps_10_fak_lower_enter_premarket | BTC Up or Down 5m Down 10 bps Optimized Average LowerEnter Premarket |
| 41 | Base | Neutral | 1 | b7c50005-0000-4000-8220-000000000101 | btc_up_down_5m_optimized_average_bps_1_fak_premarket | BTC Up or Down 5m 1 bps Optimized Average Premarket |
| 42 | Base | Neutral | 2 | b7c50005-0000-4000-8220-000000000102 | btc_up_down_5m_optimized_average_bps_2_fak_premarket | BTC Up or Down 5m 2 bps Optimized Average Premarket |
| 43 | Base | Neutral | 3 | b7c50005-0000-4000-8220-000000000103 | btc_up_down_5m_optimized_average_bps_3_fak_premarket | BTC Up or Down 5m 3 bps Optimized Average Premarket |
| 44 | Base | Neutral | 4 | b7c50005-0000-4000-8220-000000000104 | btc_up_down_5m_optimized_average_bps_4_fak_premarket | BTC Up or Down 5m 4 bps Optimized Average Premarket |
| 45 | Base | Neutral | 5 | b7c50005-0000-4000-8220-000000000105 | btc_up_down_5m_optimized_average_bps_5_fak_premarket | BTC Up or Down 5m 5 bps Optimized Average Premarket |
| 46 | Base | Neutral | 6 | b7c50005-0000-4000-8220-000000000106 | btc_up_down_5m_optimized_average_bps_6_fak_premarket | BTC Up or Down 5m 6 bps Optimized Average Premarket |
| 47 | Base | Neutral | 7 | b7c50005-0000-4000-8220-000000000107 | btc_up_down_5m_optimized_average_bps_7_fak_premarket | BTC Up or Down 5m 7 bps Optimized Average Premarket |
| 48 | Base | Neutral | 8 | b7c50005-0000-4000-8220-000000000108 | btc_up_down_5m_optimized_average_bps_8_fak_premarket | BTC Up or Down 5m 8 bps Optimized Average Premarket |
| 49 | Base | Neutral | 9 | b7c50005-0000-4000-8220-000000000109 | btc_up_down_5m_optimized_average_bps_9_fak_premarket | BTC Up or Down 5m 9 bps Optimized Average Premarket |
| 50 | Base | Neutral | 10 | b7c50005-0000-4000-8220-000000000110 | btc_up_down_5m_optimized_average_bps_10_fak_premarket | BTC Up or Down 5m 10 bps Optimized Average Premarket |
| 51 | LowerEnter clone | Neutral | 1 | b7c50005-0001-4000-8220-000000000101 | btc_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket | BTC Up or Down 5m 1 bps Optimized Average LowerEnter Premarket |
| 52 | LowerEnter clone | Neutral | 2 | b7c50005-0001-4000-8220-000000000102 | btc_up_down_5m_optimized_average_bps_2_fak_lower_enter_premarket | BTC Up or Down 5m 2 bps Optimized Average LowerEnter Premarket |
| 53 | LowerEnter clone | Neutral | 3 | b7c50005-0001-4000-8220-000000000103 | btc_up_down_5m_optimized_average_bps_3_fak_lower_enter_premarket | BTC Up or Down 5m 3 bps Optimized Average LowerEnter Premarket |
| 54 | LowerEnter clone | Neutral | 4 | b7c50005-0001-4000-8220-000000000104 | btc_up_down_5m_optimized_average_bps_4_fak_lower_enter_premarket | BTC Up or Down 5m 4 bps Optimized Average LowerEnter Premarket |
| 55 | LowerEnter clone | Neutral | 5 | b7c50005-0001-4000-8220-000000000105 | btc_up_down_5m_optimized_average_bps_5_fak_lower_enter_premarket | BTC Up or Down 5m 5 bps Optimized Average LowerEnter Premarket |
| 56 | LowerEnter clone | Neutral | 6 | b7c50005-0001-4000-8220-000000000106 | btc_up_down_5m_optimized_average_bps_6_fak_lower_enter_premarket | BTC Up or Down 5m 6 bps Optimized Average LowerEnter Premarket |
| 57 | LowerEnter clone | Neutral | 7 | b7c50005-0001-4000-8220-000000000107 | btc_up_down_5m_optimized_average_bps_7_fak_lower_enter_premarket | BTC Up or Down 5m 7 bps Optimized Average LowerEnter Premarket |
| 58 | LowerEnter clone | Neutral | 8 | b7c50005-0001-4000-8220-000000000108 | btc_up_down_5m_optimized_average_bps_8_fak_lower_enter_premarket | BTC Up or Down 5m 8 bps Optimized Average LowerEnter Premarket |
| 59 | LowerEnter clone | Neutral | 9 | b7c50005-0001-4000-8220-000000000109 | btc_up_down_5m_optimized_average_bps_9_fak_lower_enter_premarket | BTC Up or Down 5m 9 bps Optimized Average LowerEnter Premarket |
| 60 | LowerEnter clone | Neutral | 10 | b7c50005-0001-4000-8220-000000000110 | btc_up_down_5m_optimized_average_bps_10_fak_lower_enter_premarket | BTC Up or Down 5m 10 bps Optimized Average LowerEnter Premarket |

### Direct: Native LowEnter Reference Average — 28 variants

Uses the same neutral envelope signal directly, then applies the Paper-only average-fill-price cap.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Native LowEnter | Neutral | 1 | b7c50005-0000-4000-8213-000000000101 | btc_up_down_5m_low_enter_average_bps_1_fak_premarket | BTC Up or Down 5m 1 bps LowEnter Average Premarket |
| 2 | Native LowEnter | Neutral | 2 | b7c50005-0000-4000-8213-000000000102 | btc_up_down_5m_low_enter_average_bps_2_fak_premarket | BTC Up or Down 5m 2 bps LowEnter Average Premarket |
| 3 | Native LowEnter | Neutral | 3 | b7c50005-0000-4000-8213-000000000103 | btc_up_down_5m_low_enter_average_bps_3_fak_premarket | BTC Up or Down 5m 3 bps LowEnter Average Premarket |
| 4 | Native LowEnter | Neutral | 4 | b7c50005-0000-4000-8213-000000000104 | btc_up_down_5m_low_enter_average_bps_4_fak_premarket | BTC Up or Down 5m 4 bps LowEnter Average Premarket |
| 5 | Native LowEnter | Neutral | 5 | b7c50005-0000-4000-8213-000000000105 | btc_up_down_5m_low_enter_average_bps_5_fak_premarket | BTC Up or Down 5m 5 bps LowEnter Average Premarket |
| 6 | Native LowEnter | Neutral | 6 | b7c50005-0000-4000-8213-000000000106 | btc_up_down_5m_low_enter_average_bps_6_fak_premarket | BTC Up or Down 5m 6 bps LowEnter Average Premarket |
| 7 | Native LowEnter | Neutral | 7 | b7c50005-0000-4000-8213-000000000107 | btc_up_down_5m_low_enter_average_bps_7_fak_premarket | BTC Up or Down 5m 7 bps LowEnter Average Premarket |
| 8 | Native LowEnter | Neutral | 8 | b7c50005-0000-4000-8213-000000000108 | btc_up_down_5m_low_enter_average_bps_8_fak_premarket | BTC Up or Down 5m 8 bps LowEnter Average Premarket |
| 9 | Native LowEnter | Neutral | 9 | b7c50005-0000-4000-8213-000000000109 | btc_up_down_5m_low_enter_average_bps_9_fak_premarket | BTC Up or Down 5m 9 bps LowEnter Average Premarket |
| 10 | Native LowEnter | Neutral | 10 | b7c50005-0000-4000-8213-000000000110 | btc_up_down_5m_low_enter_average_bps_10_fak_premarket | BTC Up or Down 5m 10 bps LowEnter Average Premarket |
| 11 | Native LowEnter | Neutral | 15 | b7c50005-0000-4000-8213-000000000115 | btc_up_down_5m_low_enter_average_bps_15_fak_premarket | BTC Up or Down 5m 15 bps LowEnter Average Premarket |
| 12 | Native LowEnter | Neutral | 20 | b7c50005-0000-4000-8213-000000000120 | btc_up_down_5m_low_enter_average_bps_20_fak_premarket | BTC Up or Down 5m 20 bps LowEnter Average Premarket |
| 13 | Native LowEnter | Neutral | 25 | b7c50005-0000-4000-8213-000000000125 | btc_up_down_5m_low_enter_average_bps_25_fak_premarket | BTC Up or Down 5m 25 bps LowEnter Average Premarket |
| 14 | Native LowEnter | Neutral | 30 | b7c50005-0000-4000-8213-000000000130 | btc_up_down_5m_low_enter_average_bps_30_fak_premarket | BTC Up or Down 5m 30 bps LowEnter Average Premarket |
| 15 | Native LowEnter | Neutral | 35 | b7c50005-0000-4000-8213-000000000135 | btc_up_down_5m_low_enter_average_bps_35_fak_premarket | BTC Up or Down 5m 35 bps LowEnter Average Premarket |
| 16 | Native LowEnter | Neutral | 40 | b7c50005-0000-4000-8213-000000000140 | btc_up_down_5m_low_enter_average_bps_40_fak_premarket | BTC Up or Down 5m 40 bps LowEnter Average Premarket |
| 17 | Native LowEnter | Neutral | 45 | b7c50005-0000-4000-8213-000000000145 | btc_up_down_5m_low_enter_average_bps_45_fak_premarket | BTC Up or Down 5m 45 bps LowEnter Average Premarket |
| 18 | Native LowEnter | Neutral | 50 | b7c50005-0000-4000-8213-000000000150 | btc_up_down_5m_low_enter_average_bps_50_fak_premarket | BTC Up or Down 5m 50 bps LowEnter Average Premarket |
| 19 | Native LowEnter | Neutral | 55 | b7c50005-0000-4000-8213-000000000155 | btc_up_down_5m_low_enter_average_bps_55_fak_premarket | BTC Up or Down 5m 55 bps LowEnter Average Premarket |
| 20 | Native LowEnter | Neutral | 60 | b7c50005-0000-4000-8213-000000000160 | btc_up_down_5m_low_enter_average_bps_60_fak_premarket | BTC Up or Down 5m 60 bps LowEnter Average Premarket |
| 21 | Native LowEnter | Neutral | 65 | b7c50005-0000-4000-8213-000000000165 | btc_up_down_5m_low_enter_average_bps_65_fak_premarket | BTC Up or Down 5m 65 bps LowEnter Average Premarket |
| 22 | Native LowEnter | Neutral | 70 | b7c50005-0000-4000-8213-000000000170 | btc_up_down_5m_low_enter_average_bps_70_fak_premarket | BTC Up or Down 5m 70 bps LowEnter Average Premarket |
| 23 | Native LowEnter | Neutral | 75 | b7c50005-0000-4000-8213-000000000175 | btc_up_down_5m_low_enter_average_bps_75_fak_premarket | BTC Up or Down 5m 75 bps LowEnter Average Premarket |
| 24 | Native LowEnter | Neutral | 80 | b7c50005-0000-4000-8213-000000000180 | btc_up_down_5m_low_enter_average_bps_80_fak_premarket | BTC Up or Down 5m 80 bps LowEnter Average Premarket |
| 25 | Native LowEnter | Neutral | 85 | b7c50005-0000-4000-8213-000000000185 | btc_up_down_5m_low_enter_average_bps_85_fak_premarket | BTC Up or Down 5m 85 bps LowEnter Average Premarket |
| 26 | Native LowEnter | Neutral | 90 | b7c50005-0000-4000-8213-000000000190 | btc_up_down_5m_low_enter_average_bps_90_fak_premarket | BTC Up or Down 5m 90 bps LowEnter Average Premarket |
| 27 | Native LowEnter | Neutral | 95 | b7c50005-0000-4000-8213-000000000195 | btc_up_down_5m_low_enter_average_bps_95_fak_premarket | BTC Up or Down 5m 95 bps LowEnter Average Premarket |
| 28 | Native LowEnter | Neutral | 100 | b7c50005-0000-4000-8213-000000000200 | btc_up_down_5m_low_enter_average_bps_100_fak_premarket | BTC Up or Down 5m 100 bps LowEnter Average Premarket |

### Indirect: Bps Confirmed Average — 56 variants

Recomputes a linked neutral Reference Average base signal at the same Bps threshold, then requires agreement with a Diff Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8200-000000000101 | btc_up_down_5m_1_bps_confirmed_average_premarket | BTC Up or Down 5m 1 bps Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8200-000000000102 | btc_up_down_5m_2_bps_confirmed_average_premarket | BTC Up or Down 5m 2 bps Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8200-000000000103 | btc_up_down_5m_3_bps_confirmed_average_premarket | BTC Up or Down 5m 3 bps Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8200-000000000104 | btc_up_down_5m_4_bps_confirmed_average_premarket | BTC Up or Down 5m 4 bps Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8200-000000000105 | btc_up_down_5m_5_bps_confirmed_average_premarket | BTC Up or Down 5m 5 bps Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8200-000000000106 | btc_up_down_5m_6_bps_confirmed_average_premarket | BTC Up or Down 5m 6 bps Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8200-000000000107 | btc_up_down_5m_7_bps_confirmed_average_premarket | BTC Up or Down 5m 7 bps Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8200-000000000108 | btc_up_down_5m_8_bps_confirmed_average_premarket | BTC Up or Down 5m 8 bps Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8200-000000000109 | btc_up_down_5m_9_bps_confirmed_average_premarket | BTC Up or Down 5m 9 bps Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8200-000000000110 | btc_up_down_5m_10_bps_confirmed_average_premarket | BTC Up or Down 5m 10 bps Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8200-000000000115 | btc_up_down_5m_15_bps_confirmed_average_premarket | BTC Up or Down 5m 15 bps Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8200-000000000120 | btc_up_down_5m_20_bps_confirmed_average_premarket | BTC Up or Down 5m 20 bps Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8200-000000000125 | btc_up_down_5m_25_bps_confirmed_average_premarket | BTC Up or Down 5m 25 bps Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8200-000000000130 | btc_up_down_5m_30_bps_confirmed_average_premarket | BTC Up or Down 5m 30 bps Confirmed Average Premarket |
| 15 | Base | Composite | 35 | b7c50005-0000-4000-8200-000000000135 | btc_up_down_5m_35_bps_confirmed_average_premarket | BTC Up or Down 5m 35 bps Confirmed Average Premarket |
| 16 | Base | Composite | 40 | b7c50005-0000-4000-8200-000000000140 | btc_up_down_5m_40_bps_confirmed_average_premarket | BTC Up or Down 5m 40 bps Confirmed Average Premarket |
| 17 | Base | Composite | 45 | b7c50005-0000-4000-8200-000000000145 | btc_up_down_5m_45_bps_confirmed_average_premarket | BTC Up or Down 5m 45 bps Confirmed Average Premarket |
| 18 | Base | Composite | 50 | b7c50005-0000-4000-8200-000000000150 | btc_up_down_5m_50_bps_confirmed_average_premarket | BTC Up or Down 5m 50 bps Confirmed Average Premarket |
| 19 | Base | Composite | 55 | b7c50005-0000-4000-8200-000000000155 | btc_up_down_5m_55_bps_confirmed_average_premarket | BTC Up or Down 5m 55 bps Confirmed Average Premarket |
| 20 | Base | Composite | 60 | b7c50005-0000-4000-8200-000000000160 | btc_up_down_5m_60_bps_confirmed_average_premarket | BTC Up or Down 5m 60 bps Confirmed Average Premarket |
| 21 | Base | Composite | 65 | b7c50005-0000-4000-8200-000000000165 | btc_up_down_5m_65_bps_confirmed_average_premarket | BTC Up or Down 5m 65 bps Confirmed Average Premarket |
| 22 | Base | Composite | 70 | b7c50005-0000-4000-8200-000000000170 | btc_up_down_5m_70_bps_confirmed_average_premarket | BTC Up or Down 5m 70 bps Confirmed Average Premarket |
| 23 | Base | Composite | 75 | b7c50005-0000-4000-8200-000000000175 | btc_up_down_5m_75_bps_confirmed_average_premarket | BTC Up or Down 5m 75 bps Confirmed Average Premarket |
| 24 | Base | Composite | 80 | b7c50005-0000-4000-8200-000000000180 | btc_up_down_5m_80_bps_confirmed_average_premarket | BTC Up or Down 5m 80 bps Confirmed Average Premarket |
| 25 | Base | Composite | 85 | b7c50005-0000-4000-8200-000000000185 | btc_up_down_5m_85_bps_confirmed_average_premarket | BTC Up or Down 5m 85 bps Confirmed Average Premarket |
| 26 | Base | Composite | 90 | b7c50005-0000-4000-8200-000000000190 | btc_up_down_5m_90_bps_confirmed_average_premarket | BTC Up or Down 5m 90 bps Confirmed Average Premarket |
| 27 | Base | Composite | 95 | b7c50005-0000-4000-8200-000000000195 | btc_up_down_5m_95_bps_confirmed_average_premarket | BTC Up or Down 5m 95 bps Confirmed Average Premarket |
| 28 | Base | Composite | 100 | b7c50005-0000-4000-8200-000000000200 | btc_up_down_5m_100_bps_confirmed_average_premarket | BTC Up or Down 5m 100 bps Confirmed Average Premarket |
| 29 | LowerEnter clone | Composite | 1 | b7c50005-0001-4000-8200-000000000101 | btc_up_down_5m_1_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 1 bps Confirmed Average LowerEnter Premarket |
| 30 | LowerEnter clone | Composite | 2 | b7c50005-0001-4000-8200-000000000102 | btc_up_down_5m_2_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 2 bps Confirmed Average LowerEnter Premarket |
| 31 | LowerEnter clone | Composite | 3 | b7c50005-0001-4000-8200-000000000103 | btc_up_down_5m_3_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 3 bps Confirmed Average LowerEnter Premarket |
| 32 | LowerEnter clone | Composite | 4 | b7c50005-0001-4000-8200-000000000104 | btc_up_down_5m_4_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 4 bps Confirmed Average LowerEnter Premarket |
| 33 | LowerEnter clone | Composite | 5 | b7c50005-0001-4000-8200-000000000105 | btc_up_down_5m_5_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 5 bps Confirmed Average LowerEnter Premarket |
| 34 | LowerEnter clone | Composite | 6 | b7c50005-0001-4000-8200-000000000106 | btc_up_down_5m_6_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 6 bps Confirmed Average LowerEnter Premarket |
| 35 | LowerEnter clone | Composite | 7 | b7c50005-0001-4000-8200-000000000107 | btc_up_down_5m_7_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 7 bps Confirmed Average LowerEnter Premarket |
| 36 | LowerEnter clone | Composite | 8 | b7c50005-0001-4000-8200-000000000108 | btc_up_down_5m_8_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 8 bps Confirmed Average LowerEnter Premarket |
| 37 | LowerEnter clone | Composite | 9 | b7c50005-0001-4000-8200-000000000109 | btc_up_down_5m_9_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 9 bps Confirmed Average LowerEnter Premarket |
| 38 | LowerEnter clone | Composite | 10 | b7c50005-0001-4000-8200-000000000110 | btc_up_down_5m_10_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 10 bps Confirmed Average LowerEnter Premarket |
| 39 | LowerEnter clone | Composite | 15 | b7c50005-0001-4000-8200-000000000115 | btc_up_down_5m_15_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 15 bps Confirmed Average LowerEnter Premarket |
| 40 | LowerEnter clone | Composite | 20 | b7c50005-0001-4000-8200-000000000120 | btc_up_down_5m_20_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 20 bps Confirmed Average LowerEnter Premarket |
| 41 | LowerEnter clone | Composite | 25 | b7c50005-0001-4000-8200-000000000125 | btc_up_down_5m_25_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 25 bps Confirmed Average LowerEnter Premarket |
| 42 | LowerEnter clone | Composite | 30 | b7c50005-0001-4000-8200-000000000130 | btc_up_down_5m_30_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 30 bps Confirmed Average LowerEnter Premarket |
| 43 | LowerEnter clone | Composite | 35 | b7c50005-0001-4000-8200-000000000135 | btc_up_down_5m_35_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 35 bps Confirmed Average LowerEnter Premarket |
| 44 | LowerEnter clone | Composite | 40 | b7c50005-0001-4000-8200-000000000140 | btc_up_down_5m_40_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 40 bps Confirmed Average LowerEnter Premarket |
| 45 | LowerEnter clone | Composite | 45 | b7c50005-0001-4000-8200-000000000145 | btc_up_down_5m_45_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 45 bps Confirmed Average LowerEnter Premarket |
| 46 | LowerEnter clone | Composite | 50 | b7c50005-0001-4000-8200-000000000150 | btc_up_down_5m_50_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 50 bps Confirmed Average LowerEnter Premarket |
| 47 | LowerEnter clone | Composite | 55 | b7c50005-0001-4000-8200-000000000155 | btc_up_down_5m_55_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 55 bps Confirmed Average LowerEnter Premarket |
| 48 | LowerEnter clone | Composite | 60 | b7c50005-0001-4000-8200-000000000160 | btc_up_down_5m_60_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 60 bps Confirmed Average LowerEnter Premarket |
| 49 | LowerEnter clone | Composite | 65 | b7c50005-0001-4000-8200-000000000165 | btc_up_down_5m_65_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 65 bps Confirmed Average LowerEnter Premarket |
| 50 | LowerEnter clone | Composite | 70 | b7c50005-0001-4000-8200-000000000170 | btc_up_down_5m_70_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 70 bps Confirmed Average LowerEnter Premarket |
| 51 | LowerEnter clone | Composite | 75 | b7c50005-0001-4000-8200-000000000175 | btc_up_down_5m_75_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 75 bps Confirmed Average LowerEnter Premarket |
| 52 | LowerEnter clone | Composite | 80 | b7c50005-0001-4000-8200-000000000180 | btc_up_down_5m_80_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 80 bps Confirmed Average LowerEnter Premarket |
| 53 | LowerEnter clone | Composite | 85 | b7c50005-0001-4000-8200-000000000185 | btc_up_down_5m_85_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 85 bps Confirmed Average LowerEnter Premarket |
| 54 | LowerEnter clone | Composite | 90 | b7c50005-0001-4000-8200-000000000190 | btc_up_down_5m_90_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 90 bps Confirmed Average LowerEnter Premarket |
| 55 | LowerEnter clone | Composite | 95 | b7c50005-0001-4000-8200-000000000195 | btc_up_down_5m_95_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 95 bps Confirmed Average LowerEnter Premarket |
| 56 | LowerEnter clone | Composite | 100 | b7c50005-0001-4000-8200-000000000200 | btc_up_down_5m_100_bps_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 100 bps Confirmed Average LowerEnter Premarket |

### Indirect: Diff Confirmed Average — 28 variants

Uses Diff Reference Average as its base signal and recomputes a linked neutral price Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8203-000000000001 | btc_up_down_5m_1_diff_confirmed_average_premarket | BTC Up or Down 5m 1 Diff Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8203-000000000002 | btc_up_down_5m_2_diff_confirmed_average_premarket | BTC Up or Down 5m 2 Diff Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8203-000000000003 | btc_up_down_5m_3_diff_confirmed_average_premarket | BTC Up or Down 5m 3 Diff Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8203-000000000004 | btc_up_down_5m_4_diff_confirmed_average_premarket | BTC Up or Down 5m 4 Diff Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8203-000000000005 | btc_up_down_5m_5_diff_confirmed_average_premarket | BTC Up or Down 5m 5 Diff Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8203-000000000006 | btc_up_down_5m_6_diff_confirmed_average_premarket | BTC Up or Down 5m 6 Diff Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8203-000000000007 | btc_up_down_5m_7_diff_confirmed_average_premarket | BTC Up or Down 5m 7 Diff Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8203-000000000008 | btc_up_down_5m_8_diff_confirmed_average_premarket | BTC Up or Down 5m 8 Diff Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8203-000000000009 | btc_up_down_5m_9_diff_confirmed_average_premarket | BTC Up or Down 5m 9 Diff Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8203-000000000010 | btc_up_down_5m_10_diff_confirmed_average_premarket | BTC Up or Down 5m 10 Diff Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8203-000000000015 | btc_up_down_5m_15_diff_confirmed_average_premarket | BTC Up or Down 5m 15 Diff Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8203-000000000020 | btc_up_down_5m_20_diff_confirmed_average_premarket | BTC Up or Down 5m 20 Diff Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8203-000000000025 | btc_up_down_5m_25_diff_confirmed_average_premarket | BTC Up or Down 5m 25 Diff Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8203-000000000030 | btc_up_down_5m_30_diff_confirmed_average_premarket | BTC Up or Down 5m 30 Diff Confirmed Average Premarket |
| 15 | LowerEnter clone | Composite | 1 | b7c50005-0001-4000-8203-000000000001 | btc_up_down_5m_1_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 1 Diff Confirmed Average LowerEnter Premarket |
| 16 | LowerEnter clone | Composite | 2 | b7c50005-0001-4000-8203-000000000002 | btc_up_down_5m_2_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 2 Diff Confirmed Average LowerEnter Premarket |
| 17 | LowerEnter clone | Composite | 3 | b7c50005-0001-4000-8203-000000000003 | btc_up_down_5m_3_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 3 Diff Confirmed Average LowerEnter Premarket |
| 18 | LowerEnter clone | Composite | 4 | b7c50005-0001-4000-8203-000000000004 | btc_up_down_5m_4_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 4 Diff Confirmed Average LowerEnter Premarket |
| 19 | LowerEnter clone | Composite | 5 | b7c50005-0001-4000-8203-000000000005 | btc_up_down_5m_5_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 5 Diff Confirmed Average LowerEnter Premarket |
| 20 | LowerEnter clone | Composite | 6 | b7c50005-0001-4000-8203-000000000006 | btc_up_down_5m_6_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 6 Diff Confirmed Average LowerEnter Premarket |
| 21 | LowerEnter clone | Composite | 7 | b7c50005-0001-4000-8203-000000000007 | btc_up_down_5m_7_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 7 Diff Confirmed Average LowerEnter Premarket |
| 22 | LowerEnter clone | Composite | 8 | b7c50005-0001-4000-8203-000000000008 | btc_up_down_5m_8_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 8 Diff Confirmed Average LowerEnter Premarket |
| 23 | LowerEnter clone | Composite | 9 | b7c50005-0001-4000-8203-000000000009 | btc_up_down_5m_9_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 9 Diff Confirmed Average LowerEnter Premarket |
| 24 | LowerEnter clone | Composite | 10 | b7c50005-0001-4000-8203-000000000010 | btc_up_down_5m_10_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 10 Diff Confirmed Average LowerEnter Premarket |
| 25 | LowerEnter clone | Composite | 15 | b7c50005-0001-4000-8203-000000000015 | btc_up_down_5m_15_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 15 Diff Confirmed Average LowerEnter Premarket |
| 26 | LowerEnter clone | Composite | 20 | b7c50005-0001-4000-8203-000000000020 | btc_up_down_5m_20_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 20 Diff Confirmed Average LowerEnter Premarket |
| 27 | LowerEnter clone | Composite | 25 | b7c50005-0001-4000-8203-000000000025 | btc_up_down_5m_25_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 25 Diff Confirmed Average LowerEnter Premarket |
| 28 | LowerEnter clone | Composite | 30 | b7c50005-0001-4000-8203-000000000030 | btc_up_down_5m_30_diff_confirmed_average_lower_enter_premarket | BTC Up or Down 5m 30 Diff Confirmed Average LowerEnter Premarket |

## ETH — 322 affected variants

### Direct: Reference Average — 84 variants

Calls GetReferenceAverageBpsThresholdEntryDecisionAsync directly for fixed Up, fixed Down, and neutral variants.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8137-000000000101 | eth_up_down_5m_up_bps_1_fak_premarket | ETH Up or Down 5m Up 1 bps Reference Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8137-000000000102 | eth_up_down_5m_up_bps_2_fak_premarket | ETH Up or Down 5m Up 2 bps Reference Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8137-000000000103 | eth_up_down_5m_up_bps_3_fak_premarket | ETH Up or Down 5m Up 3 bps Reference Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8137-000000000104 | eth_up_down_5m_up_bps_4_fak_premarket | ETH Up or Down 5m Up 4 bps Reference Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8137-000000000105 | eth_up_down_5m_up_bps_5_fak_premarket | ETH Up or Down 5m Up 5 bps Reference Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8137-000000000106 | eth_up_down_5m_up_bps_6_fak_premarket | ETH Up or Down 5m Up 6 bps Reference Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8137-000000000107 | eth_up_down_5m_up_bps_7_fak_premarket | ETH Up or Down 5m Up 7 bps Reference Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8137-000000000108 | eth_up_down_5m_up_bps_8_fak_premarket | ETH Up or Down 5m Up 8 bps Reference Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8137-000000000109 | eth_up_down_5m_up_bps_9_fak_premarket | ETH Up or Down 5m Up 9 bps Reference Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8137-000000000110 | eth_up_down_5m_up_bps_10_fak_premarket | ETH Up or Down 5m Up 10 bps Reference Average Premarket |
| 11 | Base | Up | 15 | b7c50005-0000-4000-8137-000000000115 | eth_up_down_5m_up_bps_15_fak_premarket | ETH Up or Down 5m Up 15 bps Reference Average Premarket |
| 12 | Base | Up | 20 | b7c50005-0000-4000-8137-000000000120 | eth_up_down_5m_up_bps_20_fak_premarket | ETH Up or Down 5m Up 20 bps Reference Average Premarket |
| 13 | Base | Up | 25 | b7c50005-0000-4000-8137-000000000125 | eth_up_down_5m_up_bps_25_fak_premarket | ETH Up or Down 5m Up 25 bps Reference Average Premarket |
| 14 | Base | Up | 30 | b7c50005-0000-4000-8137-000000000130 | eth_up_down_5m_up_bps_30_fak_premarket | ETH Up or Down 5m Up 30 bps Reference Average Premarket |
| 15 | Base | Up | 35 | b7c50005-0000-4000-8137-000000000135 | eth_up_down_5m_up_bps_35_fak_premarket | ETH Up or Down 5m Up 35 bps Reference Average Premarket |
| 16 | Base | Up | 40 | b7c50005-0000-4000-8137-000000000140 | eth_up_down_5m_up_bps_40_fak_premarket | ETH Up or Down 5m Up 40 bps Reference Average Premarket |
| 17 | Base | Up | 45 | b7c50005-0000-4000-8137-000000000145 | eth_up_down_5m_up_bps_45_fak_premarket | ETH Up or Down 5m Up 45 bps Reference Average Premarket |
| 18 | Base | Up | 50 | b7c50005-0000-4000-8137-000000000150 | eth_up_down_5m_up_bps_50_fak_premarket | ETH Up or Down 5m Up 50 bps Reference Average Premarket |
| 19 | Base | Up | 55 | b7c50005-0000-4000-8137-000000000155 | eth_up_down_5m_up_bps_55_fak_premarket | ETH Up or Down 5m Up 55 bps Reference Average Premarket |
| 20 | Base | Up | 60 | b7c50005-0000-4000-8137-000000000160 | eth_up_down_5m_up_bps_60_fak_premarket | ETH Up or Down 5m Up 60 bps Reference Average Premarket |
| 21 | Base | Up | 65 | b7c50005-0000-4000-8137-000000000165 | eth_up_down_5m_up_bps_65_fak_premarket | ETH Up or Down 5m Up 65 bps Reference Average Premarket |
| 22 | Base | Up | 70 | b7c50005-0000-4000-8137-000000000170 | eth_up_down_5m_up_bps_70_fak_premarket | ETH Up or Down 5m Up 70 bps Reference Average Premarket |
| 23 | Base | Up | 75 | b7c50005-0000-4000-8137-000000000175 | eth_up_down_5m_up_bps_75_fak_premarket | ETH Up or Down 5m Up 75 bps Reference Average Premarket |
| 24 | Base | Up | 80 | b7c50005-0000-4000-8137-000000000180 | eth_up_down_5m_up_bps_80_fak_premarket | ETH Up or Down 5m Up 80 bps Reference Average Premarket |
| 25 | Base | Up | 85 | b7c50005-0000-4000-8137-000000000185 | eth_up_down_5m_up_bps_85_fak_premarket | ETH Up or Down 5m Up 85 bps Reference Average Premarket |
| 26 | Base | Up | 90 | b7c50005-0000-4000-8137-000000000190 | eth_up_down_5m_up_bps_90_fak_premarket | ETH Up or Down 5m Up 90 bps Reference Average Premarket |
| 27 | Base | Up | 95 | b7c50005-0000-4000-8137-000000000195 | eth_up_down_5m_up_bps_95_fak_premarket | ETH Up or Down 5m Up 95 bps Reference Average Premarket |
| 28 | Base | Up | 100 | b7c50005-0000-4000-8137-000000000200 | eth_up_down_5m_up_bps_100_fak_premarket | ETH Up or Down 5m Up 100 bps Reference Average Premarket |
| 29 | Base | Down | 1 | b7c50005-0000-4000-8140-000000000101 | eth_up_down_5m_down_reference_average_bps_1_fak_premarket | ETH Up or Down 5m Down 1 bps Reference Average Premarket |
| 30 | Base | Down | 2 | b7c50005-0000-4000-8140-000000000102 | eth_up_down_5m_down_reference_average_bps_2_fak_premarket | ETH Up or Down 5m Down 2 bps Reference Average Premarket |
| 31 | Base | Down | 3 | b7c50005-0000-4000-8140-000000000103 | eth_up_down_5m_down_reference_average_bps_3_fak_premarket | ETH Up or Down 5m Down 3 bps Reference Average Premarket |
| 32 | Base | Down | 4 | b7c50005-0000-4000-8140-000000000104 | eth_up_down_5m_down_reference_average_bps_4_fak_premarket | ETH Up or Down 5m Down 4 bps Reference Average Premarket |
| 33 | Base | Down | 5 | b7c50005-0000-4000-8140-000000000105 | eth_up_down_5m_down_reference_average_bps_5_fak_premarket | ETH Up or Down 5m Down 5 bps Reference Average Premarket |
| 34 | Base | Down | 6 | b7c50005-0000-4000-8140-000000000106 | eth_up_down_5m_down_reference_average_bps_6_fak_premarket | ETH Up or Down 5m Down 6 bps Reference Average Premarket |
| 35 | Base | Down | 7 | b7c50005-0000-4000-8140-000000000107 | eth_up_down_5m_down_reference_average_bps_7_fak_premarket | ETH Up or Down 5m Down 7 bps Reference Average Premarket |
| 36 | Base | Down | 8 | b7c50005-0000-4000-8140-000000000108 | eth_up_down_5m_down_reference_average_bps_8_fak_premarket | ETH Up or Down 5m Down 8 bps Reference Average Premarket |
| 37 | Base | Down | 9 | b7c50005-0000-4000-8140-000000000109 | eth_up_down_5m_down_reference_average_bps_9_fak_premarket | ETH Up or Down 5m Down 9 bps Reference Average Premarket |
| 38 | Base | Down | 10 | b7c50005-0000-4000-8140-000000000110 | eth_up_down_5m_down_reference_average_bps_10_fak_premarket | ETH Up or Down 5m Down 10 bps Reference Average Premarket |
| 39 | Base | Down | 15 | b7c50005-0000-4000-8140-000000000115 | eth_up_down_5m_down_reference_average_bps_15_fak_premarket | ETH Up or Down 5m Down 15 bps Reference Average Premarket |
| 40 | Base | Down | 20 | b7c50005-0000-4000-8140-000000000120 | eth_up_down_5m_down_reference_average_bps_20_fak_premarket | ETH Up or Down 5m Down 20 bps Reference Average Premarket |
| 41 | Base | Down | 25 | b7c50005-0000-4000-8140-000000000125 | eth_up_down_5m_down_reference_average_bps_25_fak_premarket | ETH Up or Down 5m Down 25 bps Reference Average Premarket |
| 42 | Base | Down | 30 | b7c50005-0000-4000-8140-000000000130 | eth_up_down_5m_down_reference_average_bps_30_fak_premarket | ETH Up or Down 5m Down 30 bps Reference Average Premarket |
| 43 | Base | Down | 35 | b7c50005-0000-4000-8140-000000000135 | eth_up_down_5m_down_reference_average_bps_35_fak_premarket | ETH Up or Down 5m Down 35 bps Reference Average Premarket |
| 44 | Base | Down | 40 | b7c50005-0000-4000-8140-000000000140 | eth_up_down_5m_down_reference_average_bps_40_fak_premarket | ETH Up or Down 5m Down 40 bps Reference Average Premarket |
| 45 | Base | Down | 45 | b7c50005-0000-4000-8140-000000000145 | eth_up_down_5m_down_reference_average_bps_45_fak_premarket | ETH Up or Down 5m Down 45 bps Reference Average Premarket |
| 46 | Base | Down | 50 | b7c50005-0000-4000-8140-000000000150 | eth_up_down_5m_down_reference_average_bps_50_fak_premarket | ETH Up or Down 5m Down 50 bps Reference Average Premarket |
| 47 | Base | Down | 55 | b7c50005-0000-4000-8140-000000000155 | eth_up_down_5m_down_reference_average_bps_55_fak_premarket | ETH Up or Down 5m Down 55 bps Reference Average Premarket |
| 48 | Base | Down | 60 | b7c50005-0000-4000-8140-000000000160 | eth_up_down_5m_down_reference_average_bps_60_fak_premarket | ETH Up or Down 5m Down 60 bps Reference Average Premarket |
| 49 | Base | Down | 65 | b7c50005-0000-4000-8140-000000000165 | eth_up_down_5m_down_reference_average_bps_65_fak_premarket | ETH Up or Down 5m Down 65 bps Reference Average Premarket |
| 50 | Base | Down | 70 | b7c50005-0000-4000-8140-000000000170 | eth_up_down_5m_down_reference_average_bps_70_fak_premarket | ETH Up or Down 5m Down 70 bps Reference Average Premarket |
| 51 | Base | Down | 75 | b7c50005-0000-4000-8140-000000000175 | eth_up_down_5m_down_reference_average_bps_75_fak_premarket | ETH Up or Down 5m Down 75 bps Reference Average Premarket |
| 52 | Base | Down | 80 | b7c50005-0000-4000-8140-000000000180 | eth_up_down_5m_down_reference_average_bps_80_fak_premarket | ETH Up or Down 5m Down 80 bps Reference Average Premarket |
| 53 | Base | Down | 85 | b7c50005-0000-4000-8140-000000000185 | eth_up_down_5m_down_reference_average_bps_85_fak_premarket | ETH Up or Down 5m Down 85 bps Reference Average Premarket |
| 54 | Base | Down | 90 | b7c50005-0000-4000-8140-000000000190 | eth_up_down_5m_down_reference_average_bps_90_fak_premarket | ETH Up or Down 5m Down 90 bps Reference Average Premarket |
| 55 | Base | Down | 95 | b7c50005-0000-4000-8140-000000000195 | eth_up_down_5m_down_reference_average_bps_95_fak_premarket | ETH Up or Down 5m Down 95 bps Reference Average Premarket |
| 56 | Base | Down | 100 | b7c50005-0000-4000-8140-000000000200 | eth_up_down_5m_down_reference_average_bps_100_fak_premarket | ETH Up or Down 5m Down 100 bps Reference Average Premarket |
| 57 | Base | Neutral | 1 | b7c50005-0000-4000-8179-000000000101 | eth_up_down_5m_reference_average_bps_1_fak_premarket | ETH Up or Down 5m 1 bps Reference Average Premarket |
| 58 | Base | Neutral | 2 | b7c50005-0000-4000-8179-000000000102 | eth_up_down_5m_reference_average_bps_2_fak_premarket | ETH Up or Down 5m 2 bps Reference Average Premarket |
| 59 | Base | Neutral | 3 | b7c50005-0000-4000-8179-000000000103 | eth_up_down_5m_reference_average_bps_3_fak_premarket | ETH Up or Down 5m 3 bps Reference Average Premarket |
| 60 | Base | Neutral | 4 | b7c50005-0000-4000-8179-000000000104 | eth_up_down_5m_reference_average_bps_4_fak_premarket | ETH Up or Down 5m 4 bps Reference Average Premarket |
| 61 | Base | Neutral | 5 | b7c50005-0000-4000-8179-000000000105 | eth_up_down_5m_reference_average_bps_5_fak_premarket | ETH Up or Down 5m 5 bps Reference Average Premarket |
| 62 | Base | Neutral | 6 | b7c50005-0000-4000-8179-000000000106 | eth_up_down_5m_reference_average_bps_6_fak_premarket | ETH Up or Down 5m 6 bps Reference Average Premarket |
| 63 | Base | Neutral | 7 | b7c50005-0000-4000-8179-000000000107 | eth_up_down_5m_reference_average_bps_7_fak_premarket | ETH Up or Down 5m 7 bps Reference Average Premarket |
| 64 | Base | Neutral | 8 | b7c50005-0000-4000-8179-000000000108 | eth_up_down_5m_reference_average_bps_8_fak_premarket | ETH Up or Down 5m 8 bps Reference Average Premarket |
| 65 | Base | Neutral | 9 | b7c50005-0000-4000-8179-000000000109 | eth_up_down_5m_reference_average_bps_9_fak_premarket | ETH Up or Down 5m 9 bps Reference Average Premarket |
| 66 | Base | Neutral | 10 | b7c50005-0000-4000-8179-000000000110 | eth_up_down_5m_reference_average_bps_10_fak_premarket | ETH Up or Down 5m 10 bps Reference Average Premarket |
| 67 | Base | Neutral | 15 | b7c50005-0000-4000-8179-000000000115 | eth_up_down_5m_reference_average_bps_15_fak_premarket | ETH Up or Down 5m 15 bps Reference Average Premarket |
| 68 | Base | Neutral | 20 | b7c50005-0000-4000-8179-000000000120 | eth_up_down_5m_reference_average_bps_20_fak_premarket | ETH Up or Down 5m 20 bps Reference Average Premarket |
| 69 | Base | Neutral | 25 | b7c50005-0000-4000-8179-000000000125 | eth_up_down_5m_reference_average_bps_25_fak_premarket | ETH Up or Down 5m 25 bps Reference Average Premarket |
| 70 | Base | Neutral | 30 | b7c50005-0000-4000-8179-000000000130 | eth_up_down_5m_reference_average_bps_30_fak_premarket | ETH Up or Down 5m 30 bps Reference Average Premarket |
| 71 | Base | Neutral | 35 | b7c50005-0000-4000-8179-000000000135 | eth_up_down_5m_reference_average_bps_35_fak_premarket | ETH Up or Down 5m 35 bps Reference Average Premarket |
| 72 | Base | Neutral | 40 | b7c50005-0000-4000-8179-000000000140 | eth_up_down_5m_reference_average_bps_40_fak_premarket | ETH Up or Down 5m 40 bps Reference Average Premarket |
| 73 | Base | Neutral | 45 | b7c50005-0000-4000-8179-000000000145 | eth_up_down_5m_reference_average_bps_45_fak_premarket | ETH Up or Down 5m 45 bps Reference Average Premarket |
| 74 | Base | Neutral | 50 | b7c50005-0000-4000-8179-000000000150 | eth_up_down_5m_reference_average_bps_50_fak_premarket | ETH Up or Down 5m 50 bps Reference Average Premarket |
| 75 | Base | Neutral | 55 | b7c50005-0000-4000-8179-000000000155 | eth_up_down_5m_reference_average_bps_55_fak_premarket | ETH Up or Down 5m 55 bps Reference Average Premarket |
| 76 | Base | Neutral | 60 | b7c50005-0000-4000-8179-000000000160 | eth_up_down_5m_reference_average_bps_60_fak_premarket | ETH Up or Down 5m 60 bps Reference Average Premarket |
| 77 | Base | Neutral | 65 | b7c50005-0000-4000-8179-000000000165 | eth_up_down_5m_reference_average_bps_65_fak_premarket | ETH Up or Down 5m 65 bps Reference Average Premarket |
| 78 | Base | Neutral | 70 | b7c50005-0000-4000-8179-000000000170 | eth_up_down_5m_reference_average_bps_70_fak_premarket | ETH Up or Down 5m 70 bps Reference Average Premarket |
| 79 | Base | Neutral | 75 | b7c50005-0000-4000-8179-000000000175 | eth_up_down_5m_reference_average_bps_75_fak_premarket | ETH Up or Down 5m 75 bps Reference Average Premarket |
| 80 | Base | Neutral | 80 | b7c50005-0000-4000-8179-000000000180 | eth_up_down_5m_reference_average_bps_80_fak_premarket | ETH Up or Down 5m 80 bps Reference Average Premarket |
| 81 | Base | Neutral | 85 | b7c50005-0000-4000-8179-000000000185 | eth_up_down_5m_reference_average_bps_85_fak_premarket | ETH Up or Down 5m 85 bps Reference Average Premarket |
| 82 | Base | Neutral | 90 | b7c50005-0000-4000-8179-000000000190 | eth_up_down_5m_reference_average_bps_90_fak_premarket | ETH Up or Down 5m 90 bps Reference Average Premarket |
| 83 | Base | Neutral | 95 | b7c50005-0000-4000-8179-000000000195 | eth_up_down_5m_reference_average_bps_95_fak_premarket | ETH Up or Down 5m 95 bps Reference Average Premarket |
| 84 | Base | Neutral | 100 | b7c50005-0000-4000-8179-000000000200 | eth_up_down_5m_reference_average_bps_100_fak_premarket | ETH Up or Down 5m 100 bps Reference Average Premarket |

### Direct: Optimized Reference Average — 168 variants

Calls the shared selector directly and additionally requires the direction-relevant selected boundary window to be 3h.

#### Base variants — 84

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8209-000000000101 | eth_up_down_5m_up_optimized_average_bps_1_fak_premarket | ETH Up or Down 5m Up 1 bps Optimized Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8209-000000000102 | eth_up_down_5m_up_optimized_average_bps_2_fak_premarket | ETH Up or Down 5m Up 2 bps Optimized Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8209-000000000103 | eth_up_down_5m_up_optimized_average_bps_3_fak_premarket | ETH Up or Down 5m Up 3 bps Optimized Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8209-000000000104 | eth_up_down_5m_up_optimized_average_bps_4_fak_premarket | ETH Up or Down 5m Up 4 bps Optimized Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8209-000000000105 | eth_up_down_5m_up_optimized_average_bps_5_fak_premarket | ETH Up or Down 5m Up 5 bps Optimized Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8209-000000000106 | eth_up_down_5m_up_optimized_average_bps_6_fak_premarket | ETH Up or Down 5m Up 6 bps Optimized Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8209-000000000107 | eth_up_down_5m_up_optimized_average_bps_7_fak_premarket | ETH Up or Down 5m Up 7 bps Optimized Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8209-000000000108 | eth_up_down_5m_up_optimized_average_bps_8_fak_premarket | ETH Up or Down 5m Up 8 bps Optimized Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8209-000000000109 | eth_up_down_5m_up_optimized_average_bps_9_fak_premarket | ETH Up or Down 5m Up 9 bps Optimized Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8209-000000000110 | eth_up_down_5m_up_optimized_average_bps_10_fak_premarket | ETH Up or Down 5m Up 10 bps Optimized Average Premarket |
| 11 | Base | Up | 15 | b7c50005-0000-4000-8209-000000000115 | eth_up_down_5m_up_optimized_average_bps_15_fak_premarket | ETH Up or Down 5m Up 15 bps Optimized Average Premarket |
| 12 | Base | Up | 20 | b7c50005-0000-4000-8209-000000000120 | eth_up_down_5m_up_optimized_average_bps_20_fak_premarket | ETH Up or Down 5m Up 20 bps Optimized Average Premarket |
| 13 | Base | Up | 25 | b7c50005-0000-4000-8209-000000000125 | eth_up_down_5m_up_optimized_average_bps_25_fak_premarket | ETH Up or Down 5m Up 25 bps Optimized Average Premarket |
| 14 | Base | Up | 30 | b7c50005-0000-4000-8209-000000000130 | eth_up_down_5m_up_optimized_average_bps_30_fak_premarket | ETH Up or Down 5m Up 30 bps Optimized Average Premarket |
| 15 | Base | Up | 35 | b7c50005-0000-4000-8209-000000000135 | eth_up_down_5m_up_optimized_average_bps_35_fak_premarket | ETH Up or Down 5m Up 35 bps Optimized Average Premarket |
| 16 | Base | Up | 40 | b7c50005-0000-4000-8209-000000000140 | eth_up_down_5m_up_optimized_average_bps_40_fak_premarket | ETH Up or Down 5m Up 40 bps Optimized Average Premarket |
| 17 | Base | Up | 45 | b7c50005-0000-4000-8209-000000000145 | eth_up_down_5m_up_optimized_average_bps_45_fak_premarket | ETH Up or Down 5m Up 45 bps Optimized Average Premarket |
| 18 | Base | Up | 50 | b7c50005-0000-4000-8209-000000000150 | eth_up_down_5m_up_optimized_average_bps_50_fak_premarket | ETH Up or Down 5m Up 50 bps Optimized Average Premarket |
| 19 | Base | Up | 55 | b7c50005-0000-4000-8209-000000000155 | eth_up_down_5m_up_optimized_average_bps_55_fak_premarket | ETH Up or Down 5m Up 55 bps Optimized Average Premarket |
| 20 | Base | Up | 60 | b7c50005-0000-4000-8209-000000000160 | eth_up_down_5m_up_optimized_average_bps_60_fak_premarket | ETH Up or Down 5m Up 60 bps Optimized Average Premarket |
| 21 | Base | Up | 65 | b7c50005-0000-4000-8209-000000000165 | eth_up_down_5m_up_optimized_average_bps_65_fak_premarket | ETH Up or Down 5m Up 65 bps Optimized Average Premarket |
| 22 | Base | Up | 70 | b7c50005-0000-4000-8209-000000000170 | eth_up_down_5m_up_optimized_average_bps_70_fak_premarket | ETH Up or Down 5m Up 70 bps Optimized Average Premarket |
| 23 | Base | Up | 75 | b7c50005-0000-4000-8209-000000000175 | eth_up_down_5m_up_optimized_average_bps_75_fak_premarket | ETH Up or Down 5m Up 75 bps Optimized Average Premarket |
| 24 | Base | Up | 80 | b7c50005-0000-4000-8209-000000000180 | eth_up_down_5m_up_optimized_average_bps_80_fak_premarket | ETH Up or Down 5m Up 80 bps Optimized Average Premarket |
| 25 | Base | Up | 85 | b7c50005-0000-4000-8209-000000000185 | eth_up_down_5m_up_optimized_average_bps_85_fak_premarket | ETH Up or Down 5m Up 85 bps Optimized Average Premarket |
| 26 | Base | Up | 90 | b7c50005-0000-4000-8209-000000000190 | eth_up_down_5m_up_optimized_average_bps_90_fak_premarket | ETH Up or Down 5m Up 90 bps Optimized Average Premarket |
| 27 | Base | Up | 95 | b7c50005-0000-4000-8209-000000000195 | eth_up_down_5m_up_optimized_average_bps_95_fak_premarket | ETH Up or Down 5m Up 95 bps Optimized Average Premarket |
| 28 | Base | Up | 100 | b7c50005-0000-4000-8209-000000000200 | eth_up_down_5m_up_optimized_average_bps_100_fak_premarket | ETH Up or Down 5m Up 100 bps Optimized Average Premarket |
| 29 | Base | Down | 1 | b7c50005-0000-4000-8210-000000000101 | eth_up_down_5m_down_optimized_average_bps_1_fak_premarket | ETH Up or Down 5m Down 1 bps Optimized Average Premarket |
| 30 | Base | Down | 2 | b7c50005-0000-4000-8210-000000000102 | eth_up_down_5m_down_optimized_average_bps_2_fak_premarket | ETH Up or Down 5m Down 2 bps Optimized Average Premarket |
| 31 | Base | Down | 3 | b7c50005-0000-4000-8210-000000000103 | eth_up_down_5m_down_optimized_average_bps_3_fak_premarket | ETH Up or Down 5m Down 3 bps Optimized Average Premarket |
| 32 | Base | Down | 4 | b7c50005-0000-4000-8210-000000000104 | eth_up_down_5m_down_optimized_average_bps_4_fak_premarket | ETH Up or Down 5m Down 4 bps Optimized Average Premarket |
| 33 | Base | Down | 5 | b7c50005-0000-4000-8210-000000000105 | eth_up_down_5m_down_optimized_average_bps_5_fak_premarket | ETH Up or Down 5m Down 5 bps Optimized Average Premarket |
| 34 | Base | Down | 6 | b7c50005-0000-4000-8210-000000000106 | eth_up_down_5m_down_optimized_average_bps_6_fak_premarket | ETH Up or Down 5m Down 6 bps Optimized Average Premarket |
| 35 | Base | Down | 7 | b7c50005-0000-4000-8210-000000000107 | eth_up_down_5m_down_optimized_average_bps_7_fak_premarket | ETH Up or Down 5m Down 7 bps Optimized Average Premarket |
| 36 | Base | Down | 8 | b7c50005-0000-4000-8210-000000000108 | eth_up_down_5m_down_optimized_average_bps_8_fak_premarket | ETH Up or Down 5m Down 8 bps Optimized Average Premarket |
| 37 | Base | Down | 9 | b7c50005-0000-4000-8210-000000000109 | eth_up_down_5m_down_optimized_average_bps_9_fak_premarket | ETH Up or Down 5m Down 9 bps Optimized Average Premarket |
| 38 | Base | Down | 10 | b7c50005-0000-4000-8210-000000000110 | eth_up_down_5m_down_optimized_average_bps_10_fak_premarket | ETH Up or Down 5m Down 10 bps Optimized Average Premarket |
| 39 | Base | Down | 15 | b7c50005-0000-4000-8210-000000000115 | eth_up_down_5m_down_optimized_average_bps_15_fak_premarket | ETH Up or Down 5m Down 15 bps Optimized Average Premarket |
| 40 | Base | Down | 20 | b7c50005-0000-4000-8210-000000000120 | eth_up_down_5m_down_optimized_average_bps_20_fak_premarket | ETH Up or Down 5m Down 20 bps Optimized Average Premarket |
| 41 | Base | Down | 25 | b7c50005-0000-4000-8210-000000000125 | eth_up_down_5m_down_optimized_average_bps_25_fak_premarket | ETH Up or Down 5m Down 25 bps Optimized Average Premarket |
| 42 | Base | Down | 30 | b7c50005-0000-4000-8210-000000000130 | eth_up_down_5m_down_optimized_average_bps_30_fak_premarket | ETH Up or Down 5m Down 30 bps Optimized Average Premarket |
| 43 | Base | Down | 35 | b7c50005-0000-4000-8210-000000000135 | eth_up_down_5m_down_optimized_average_bps_35_fak_premarket | ETH Up or Down 5m Down 35 bps Optimized Average Premarket |
| 44 | Base | Down | 40 | b7c50005-0000-4000-8210-000000000140 | eth_up_down_5m_down_optimized_average_bps_40_fak_premarket | ETH Up or Down 5m Down 40 bps Optimized Average Premarket |
| 45 | Base | Down | 45 | b7c50005-0000-4000-8210-000000000145 | eth_up_down_5m_down_optimized_average_bps_45_fak_premarket | ETH Up or Down 5m Down 45 bps Optimized Average Premarket |
| 46 | Base | Down | 50 | b7c50005-0000-4000-8210-000000000150 | eth_up_down_5m_down_optimized_average_bps_50_fak_premarket | ETH Up or Down 5m Down 50 bps Optimized Average Premarket |
| 47 | Base | Down | 55 | b7c50005-0000-4000-8210-000000000155 | eth_up_down_5m_down_optimized_average_bps_55_fak_premarket | ETH Up or Down 5m Down 55 bps Optimized Average Premarket |
| 48 | Base | Down | 60 | b7c50005-0000-4000-8210-000000000160 | eth_up_down_5m_down_optimized_average_bps_60_fak_premarket | ETH Up or Down 5m Down 60 bps Optimized Average Premarket |
| 49 | Base | Down | 65 | b7c50005-0000-4000-8210-000000000165 | eth_up_down_5m_down_optimized_average_bps_65_fak_premarket | ETH Up or Down 5m Down 65 bps Optimized Average Premarket |
| 50 | Base | Down | 70 | b7c50005-0000-4000-8210-000000000170 | eth_up_down_5m_down_optimized_average_bps_70_fak_premarket | ETH Up or Down 5m Down 70 bps Optimized Average Premarket |
| 51 | Base | Down | 75 | b7c50005-0000-4000-8210-000000000175 | eth_up_down_5m_down_optimized_average_bps_75_fak_premarket | ETH Up or Down 5m Down 75 bps Optimized Average Premarket |
| 52 | Base | Down | 80 | b7c50005-0000-4000-8210-000000000180 | eth_up_down_5m_down_optimized_average_bps_80_fak_premarket | ETH Up or Down 5m Down 80 bps Optimized Average Premarket |
| 53 | Base | Down | 85 | b7c50005-0000-4000-8210-000000000185 | eth_up_down_5m_down_optimized_average_bps_85_fak_premarket | ETH Up or Down 5m Down 85 bps Optimized Average Premarket |
| 54 | Base | Down | 90 | b7c50005-0000-4000-8210-000000000190 | eth_up_down_5m_down_optimized_average_bps_90_fak_premarket | ETH Up or Down 5m Down 90 bps Optimized Average Premarket |
| 55 | Base | Down | 95 | b7c50005-0000-4000-8210-000000000195 | eth_up_down_5m_down_optimized_average_bps_95_fak_premarket | ETH Up or Down 5m Down 95 bps Optimized Average Premarket |
| 56 | Base | Down | 100 | b7c50005-0000-4000-8210-000000000200 | eth_up_down_5m_down_optimized_average_bps_100_fak_premarket | ETH Up or Down 5m Down 100 bps Optimized Average Premarket |
| 57 | Base | Neutral | 1 | b7c50005-0000-4000-8211-000000000101 | eth_up_down_5m_optimized_average_bps_1_fak_premarket | ETH Up or Down 5m 1 bps Optimized Average Premarket |
| 58 | Base | Neutral | 2 | b7c50005-0000-4000-8211-000000000102 | eth_up_down_5m_optimized_average_bps_2_fak_premarket | ETH Up or Down 5m 2 bps Optimized Average Premarket |
| 59 | Base | Neutral | 3 | b7c50005-0000-4000-8211-000000000103 | eth_up_down_5m_optimized_average_bps_3_fak_premarket | ETH Up or Down 5m 3 bps Optimized Average Premarket |
| 60 | Base | Neutral | 4 | b7c50005-0000-4000-8211-000000000104 | eth_up_down_5m_optimized_average_bps_4_fak_premarket | ETH Up or Down 5m 4 bps Optimized Average Premarket |
| 61 | Base | Neutral | 5 | b7c50005-0000-4000-8211-000000000105 | eth_up_down_5m_optimized_average_bps_5_fak_premarket | ETH Up or Down 5m 5 bps Optimized Average Premarket |
| 62 | Base | Neutral | 6 | b7c50005-0000-4000-8211-000000000106 | eth_up_down_5m_optimized_average_bps_6_fak_premarket | ETH Up or Down 5m 6 bps Optimized Average Premarket |
| 63 | Base | Neutral | 7 | b7c50005-0000-4000-8211-000000000107 | eth_up_down_5m_optimized_average_bps_7_fak_premarket | ETH Up or Down 5m 7 bps Optimized Average Premarket |
| 64 | Base | Neutral | 8 | b7c50005-0000-4000-8211-000000000108 | eth_up_down_5m_optimized_average_bps_8_fak_premarket | ETH Up or Down 5m 8 bps Optimized Average Premarket |
| 65 | Base | Neutral | 9 | b7c50005-0000-4000-8211-000000000109 | eth_up_down_5m_optimized_average_bps_9_fak_premarket | ETH Up or Down 5m 9 bps Optimized Average Premarket |
| 66 | Base | Neutral | 10 | b7c50005-0000-4000-8211-000000000110 | eth_up_down_5m_optimized_average_bps_10_fak_premarket | ETH Up or Down 5m 10 bps Optimized Average Premarket |
| 67 | Base | Neutral | 15 | b7c50005-0000-4000-8211-000000000115 | eth_up_down_5m_optimized_average_bps_15_fak_premarket | ETH Up or Down 5m 15 bps Optimized Average Premarket |
| 68 | Base | Neutral | 20 | b7c50005-0000-4000-8211-000000000120 | eth_up_down_5m_optimized_average_bps_20_fak_premarket | ETH Up or Down 5m 20 bps Optimized Average Premarket |
| 69 | Base | Neutral | 25 | b7c50005-0000-4000-8211-000000000125 | eth_up_down_5m_optimized_average_bps_25_fak_premarket | ETH Up or Down 5m 25 bps Optimized Average Premarket |
| 70 | Base | Neutral | 30 | b7c50005-0000-4000-8211-000000000130 | eth_up_down_5m_optimized_average_bps_30_fak_premarket | ETH Up or Down 5m 30 bps Optimized Average Premarket |
| 71 | Base | Neutral | 35 | b7c50005-0000-4000-8211-000000000135 | eth_up_down_5m_optimized_average_bps_35_fak_premarket | ETH Up or Down 5m 35 bps Optimized Average Premarket |
| 72 | Base | Neutral | 40 | b7c50005-0000-4000-8211-000000000140 | eth_up_down_5m_optimized_average_bps_40_fak_premarket | ETH Up or Down 5m 40 bps Optimized Average Premarket |
| 73 | Base | Neutral | 45 | b7c50005-0000-4000-8211-000000000145 | eth_up_down_5m_optimized_average_bps_45_fak_premarket | ETH Up or Down 5m 45 bps Optimized Average Premarket |
| 74 | Base | Neutral | 50 | b7c50005-0000-4000-8211-000000000150 | eth_up_down_5m_optimized_average_bps_50_fak_premarket | ETH Up or Down 5m 50 bps Optimized Average Premarket |
| 75 | Base | Neutral | 55 | b7c50005-0000-4000-8211-000000000155 | eth_up_down_5m_optimized_average_bps_55_fak_premarket | ETH Up or Down 5m 55 bps Optimized Average Premarket |
| 76 | Base | Neutral | 60 | b7c50005-0000-4000-8211-000000000160 | eth_up_down_5m_optimized_average_bps_60_fak_premarket | ETH Up or Down 5m 60 bps Optimized Average Premarket |
| 77 | Base | Neutral | 65 | b7c50005-0000-4000-8211-000000000165 | eth_up_down_5m_optimized_average_bps_65_fak_premarket | ETH Up or Down 5m 65 bps Optimized Average Premarket |
| 78 | Base | Neutral | 70 | b7c50005-0000-4000-8211-000000000170 | eth_up_down_5m_optimized_average_bps_70_fak_premarket | ETH Up or Down 5m 70 bps Optimized Average Premarket |
| 79 | Base | Neutral | 75 | b7c50005-0000-4000-8211-000000000175 | eth_up_down_5m_optimized_average_bps_75_fak_premarket | ETH Up or Down 5m 75 bps Optimized Average Premarket |
| 80 | Base | Neutral | 80 | b7c50005-0000-4000-8211-000000000180 | eth_up_down_5m_optimized_average_bps_80_fak_premarket | ETH Up or Down 5m 80 bps Optimized Average Premarket |
| 81 | Base | Neutral | 85 | b7c50005-0000-4000-8211-000000000185 | eth_up_down_5m_optimized_average_bps_85_fak_premarket | ETH Up or Down 5m 85 bps Optimized Average Premarket |
| 82 | Base | Neutral | 90 | b7c50005-0000-4000-8211-000000000190 | eth_up_down_5m_optimized_average_bps_90_fak_premarket | ETH Up or Down 5m 90 bps Optimized Average Premarket |
| 83 | Base | Neutral | 95 | b7c50005-0000-4000-8211-000000000195 | eth_up_down_5m_optimized_average_bps_95_fak_premarket | ETH Up or Down 5m 95 bps Optimized Average Premarket |
| 84 | Base | Neutral | 100 | b7c50005-0000-4000-8211-000000000200 | eth_up_down_5m_optimized_average_bps_100_fak_premarket | ETH Up or Down 5m 100 bps Optimized Average Premarket |

#### LowerEnter clones — 84

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 85 | LowerEnter clone | Up | 1 | b7c50005-0001-4000-8209-000000000101 | eth_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket | ETH Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket |
| 86 | LowerEnter clone | Up | 2 | b7c50005-0001-4000-8209-000000000102 | eth_up_down_5m_up_optimized_average_bps_2_fak_lower_enter_premarket | ETH Up or Down 5m Up 2 bps Optimized Average LowerEnter Premarket |
| 87 | LowerEnter clone | Up | 3 | b7c50005-0001-4000-8209-000000000103 | eth_up_down_5m_up_optimized_average_bps_3_fak_lower_enter_premarket | ETH Up or Down 5m Up 3 bps Optimized Average LowerEnter Premarket |
| 88 | LowerEnter clone | Up | 4 | b7c50005-0001-4000-8209-000000000104 | eth_up_down_5m_up_optimized_average_bps_4_fak_lower_enter_premarket | ETH Up or Down 5m Up 4 bps Optimized Average LowerEnter Premarket |
| 89 | LowerEnter clone | Up | 5 | b7c50005-0001-4000-8209-000000000105 | eth_up_down_5m_up_optimized_average_bps_5_fak_lower_enter_premarket | ETH Up or Down 5m Up 5 bps Optimized Average LowerEnter Premarket |
| 90 | LowerEnter clone | Up | 6 | b7c50005-0001-4000-8209-000000000106 | eth_up_down_5m_up_optimized_average_bps_6_fak_lower_enter_premarket | ETH Up or Down 5m Up 6 bps Optimized Average LowerEnter Premarket |
| 91 | LowerEnter clone | Up | 7 | b7c50005-0001-4000-8209-000000000107 | eth_up_down_5m_up_optimized_average_bps_7_fak_lower_enter_premarket | ETH Up or Down 5m Up 7 bps Optimized Average LowerEnter Premarket |
| 92 | LowerEnter clone | Up | 8 | b7c50005-0001-4000-8209-000000000108 | eth_up_down_5m_up_optimized_average_bps_8_fak_lower_enter_premarket | ETH Up or Down 5m Up 8 bps Optimized Average LowerEnter Premarket |
| 93 | LowerEnter clone | Up | 9 | b7c50005-0001-4000-8209-000000000109 | eth_up_down_5m_up_optimized_average_bps_9_fak_lower_enter_premarket | ETH Up or Down 5m Up 9 bps Optimized Average LowerEnter Premarket |
| 94 | LowerEnter clone | Up | 10 | b7c50005-0001-4000-8209-000000000110 | eth_up_down_5m_up_optimized_average_bps_10_fak_lower_enter_premarket | ETH Up or Down 5m Up 10 bps Optimized Average LowerEnter Premarket |
| 95 | LowerEnter clone | Up | 15 | b7c50005-0001-4000-8209-000000000115 | eth_up_down_5m_up_optimized_average_bps_15_fak_lower_enter_premarket | ETH Up or Down 5m Up 15 bps Optimized Average LowerEnter Premarket |
| 96 | LowerEnter clone | Up | 20 | b7c50005-0001-4000-8209-000000000120 | eth_up_down_5m_up_optimized_average_bps_20_fak_lower_enter_premarket | ETH Up or Down 5m Up 20 bps Optimized Average LowerEnter Premarket |
| 97 | LowerEnter clone | Up | 25 | b7c50005-0001-4000-8209-000000000125 | eth_up_down_5m_up_optimized_average_bps_25_fak_lower_enter_premarket | ETH Up or Down 5m Up 25 bps Optimized Average LowerEnter Premarket |
| 98 | LowerEnter clone | Up | 30 | b7c50005-0001-4000-8209-000000000130 | eth_up_down_5m_up_optimized_average_bps_30_fak_lower_enter_premarket | ETH Up or Down 5m Up 30 bps Optimized Average LowerEnter Premarket |
| 99 | LowerEnter clone | Up | 35 | b7c50005-0001-4000-8209-000000000135 | eth_up_down_5m_up_optimized_average_bps_35_fak_lower_enter_premarket | ETH Up or Down 5m Up 35 bps Optimized Average LowerEnter Premarket |
| 100 | LowerEnter clone | Up | 40 | b7c50005-0001-4000-8209-000000000140 | eth_up_down_5m_up_optimized_average_bps_40_fak_lower_enter_premarket | ETH Up or Down 5m Up 40 bps Optimized Average LowerEnter Premarket |
| 101 | LowerEnter clone | Up | 45 | b7c50005-0001-4000-8209-000000000145 | eth_up_down_5m_up_optimized_average_bps_45_fak_lower_enter_premarket | ETH Up or Down 5m Up 45 bps Optimized Average LowerEnter Premarket |
| 102 | LowerEnter clone | Up | 50 | b7c50005-0001-4000-8209-000000000150 | eth_up_down_5m_up_optimized_average_bps_50_fak_lower_enter_premarket | ETH Up or Down 5m Up 50 bps Optimized Average LowerEnter Premarket |
| 103 | LowerEnter clone | Up | 55 | b7c50005-0001-4000-8209-000000000155 | eth_up_down_5m_up_optimized_average_bps_55_fak_lower_enter_premarket | ETH Up or Down 5m Up 55 bps Optimized Average LowerEnter Premarket |
| 104 | LowerEnter clone | Up | 60 | b7c50005-0001-4000-8209-000000000160 | eth_up_down_5m_up_optimized_average_bps_60_fak_lower_enter_premarket | ETH Up or Down 5m Up 60 bps Optimized Average LowerEnter Premarket |
| 105 | LowerEnter clone | Up | 65 | b7c50005-0001-4000-8209-000000000165 | eth_up_down_5m_up_optimized_average_bps_65_fak_lower_enter_premarket | ETH Up or Down 5m Up 65 bps Optimized Average LowerEnter Premarket |
| 106 | LowerEnter clone | Up | 70 | b7c50005-0001-4000-8209-000000000170 | eth_up_down_5m_up_optimized_average_bps_70_fak_lower_enter_premarket | ETH Up or Down 5m Up 70 bps Optimized Average LowerEnter Premarket |
| 107 | LowerEnter clone | Up | 75 | b7c50005-0001-4000-8209-000000000175 | eth_up_down_5m_up_optimized_average_bps_75_fak_lower_enter_premarket | ETH Up or Down 5m Up 75 bps Optimized Average LowerEnter Premarket |
| 108 | LowerEnter clone | Up | 80 | b7c50005-0001-4000-8209-000000000180 | eth_up_down_5m_up_optimized_average_bps_80_fak_lower_enter_premarket | ETH Up or Down 5m Up 80 bps Optimized Average LowerEnter Premarket |
| 109 | LowerEnter clone | Up | 85 | b7c50005-0001-4000-8209-000000000185 | eth_up_down_5m_up_optimized_average_bps_85_fak_lower_enter_premarket | ETH Up or Down 5m Up 85 bps Optimized Average LowerEnter Premarket |
| 110 | LowerEnter clone | Up | 90 | b7c50005-0001-4000-8209-000000000190 | eth_up_down_5m_up_optimized_average_bps_90_fak_lower_enter_premarket | ETH Up or Down 5m Up 90 bps Optimized Average LowerEnter Premarket |
| 111 | LowerEnter clone | Up | 95 | b7c50005-0001-4000-8209-000000000195 | eth_up_down_5m_up_optimized_average_bps_95_fak_lower_enter_premarket | ETH Up or Down 5m Up 95 bps Optimized Average LowerEnter Premarket |
| 112 | LowerEnter clone | Up | 100 | b7c50005-0001-4000-8209-000000000200 | eth_up_down_5m_up_optimized_average_bps_100_fak_lower_enter_premarket | ETH Up or Down 5m Up 100 bps Optimized Average LowerEnter Premarket |
| 113 | LowerEnter clone | Down | 1 | b7c50005-0001-4000-8210-000000000101 | eth_up_down_5m_down_optimized_average_bps_1_fak_lower_enter_premarket | ETH Up or Down 5m Down 1 bps Optimized Average LowerEnter Premarket |
| 114 | LowerEnter clone | Down | 2 | b7c50005-0001-4000-8210-000000000102 | eth_up_down_5m_down_optimized_average_bps_2_fak_lower_enter_premarket | ETH Up or Down 5m Down 2 bps Optimized Average LowerEnter Premarket |
| 115 | LowerEnter clone | Down | 3 | b7c50005-0001-4000-8210-000000000103 | eth_up_down_5m_down_optimized_average_bps_3_fak_lower_enter_premarket | ETH Up or Down 5m Down 3 bps Optimized Average LowerEnter Premarket |
| 116 | LowerEnter clone | Down | 4 | b7c50005-0001-4000-8210-000000000104 | eth_up_down_5m_down_optimized_average_bps_4_fak_lower_enter_premarket | ETH Up or Down 5m Down 4 bps Optimized Average LowerEnter Premarket |
| 117 | LowerEnter clone | Down | 5 | b7c50005-0001-4000-8210-000000000105 | eth_up_down_5m_down_optimized_average_bps_5_fak_lower_enter_premarket | ETH Up or Down 5m Down 5 bps Optimized Average LowerEnter Premarket |
| 118 | LowerEnter clone | Down | 6 | b7c50005-0001-4000-8210-000000000106 | eth_up_down_5m_down_optimized_average_bps_6_fak_lower_enter_premarket | ETH Up or Down 5m Down 6 bps Optimized Average LowerEnter Premarket |
| 119 | LowerEnter clone | Down | 7 | b7c50005-0001-4000-8210-000000000107 | eth_up_down_5m_down_optimized_average_bps_7_fak_lower_enter_premarket | ETH Up or Down 5m Down 7 bps Optimized Average LowerEnter Premarket |
| 120 | LowerEnter clone | Down | 8 | b7c50005-0001-4000-8210-000000000108 | eth_up_down_5m_down_optimized_average_bps_8_fak_lower_enter_premarket | ETH Up or Down 5m Down 8 bps Optimized Average LowerEnter Premarket |
| 121 | LowerEnter clone | Down | 9 | b7c50005-0001-4000-8210-000000000109 | eth_up_down_5m_down_optimized_average_bps_9_fak_lower_enter_premarket | ETH Up or Down 5m Down 9 bps Optimized Average LowerEnter Premarket |
| 122 | LowerEnter clone | Down | 10 | b7c50005-0001-4000-8210-000000000110 | eth_up_down_5m_down_optimized_average_bps_10_fak_lower_enter_premarket | ETH Up or Down 5m Down 10 bps Optimized Average LowerEnter Premarket |
| 123 | LowerEnter clone | Down | 15 | b7c50005-0001-4000-8210-000000000115 | eth_up_down_5m_down_optimized_average_bps_15_fak_lower_enter_premarket | ETH Up or Down 5m Down 15 bps Optimized Average LowerEnter Premarket |
| 124 | LowerEnter clone | Down | 20 | b7c50005-0001-4000-8210-000000000120 | eth_up_down_5m_down_optimized_average_bps_20_fak_lower_enter_premarket | ETH Up or Down 5m Down 20 bps Optimized Average LowerEnter Premarket |
| 125 | LowerEnter clone | Down | 25 | b7c50005-0001-4000-8210-000000000125 | eth_up_down_5m_down_optimized_average_bps_25_fak_lower_enter_premarket | ETH Up or Down 5m Down 25 bps Optimized Average LowerEnter Premarket |
| 126 | LowerEnter clone | Down | 30 | b7c50005-0001-4000-8210-000000000130 | eth_up_down_5m_down_optimized_average_bps_30_fak_lower_enter_premarket | ETH Up or Down 5m Down 30 bps Optimized Average LowerEnter Premarket |
| 127 | LowerEnter clone | Down | 35 | b7c50005-0001-4000-8210-000000000135 | eth_up_down_5m_down_optimized_average_bps_35_fak_lower_enter_premarket | ETH Up or Down 5m Down 35 bps Optimized Average LowerEnter Premarket |
| 128 | LowerEnter clone | Down | 40 | b7c50005-0001-4000-8210-000000000140 | eth_up_down_5m_down_optimized_average_bps_40_fak_lower_enter_premarket | ETH Up or Down 5m Down 40 bps Optimized Average LowerEnter Premarket |
| 129 | LowerEnter clone | Down | 45 | b7c50005-0001-4000-8210-000000000145 | eth_up_down_5m_down_optimized_average_bps_45_fak_lower_enter_premarket | ETH Up or Down 5m Down 45 bps Optimized Average LowerEnter Premarket |
| 130 | LowerEnter clone | Down | 50 | b7c50005-0001-4000-8210-000000000150 | eth_up_down_5m_down_optimized_average_bps_50_fak_lower_enter_premarket | ETH Up or Down 5m Down 50 bps Optimized Average LowerEnter Premarket |
| 131 | LowerEnter clone | Down | 55 | b7c50005-0001-4000-8210-000000000155 | eth_up_down_5m_down_optimized_average_bps_55_fak_lower_enter_premarket | ETH Up or Down 5m Down 55 bps Optimized Average LowerEnter Premarket |
| 132 | LowerEnter clone | Down | 60 | b7c50005-0001-4000-8210-000000000160 | eth_up_down_5m_down_optimized_average_bps_60_fak_lower_enter_premarket | ETH Up or Down 5m Down 60 bps Optimized Average LowerEnter Premarket |
| 133 | LowerEnter clone | Down | 65 | b7c50005-0001-4000-8210-000000000165 | eth_up_down_5m_down_optimized_average_bps_65_fak_lower_enter_premarket | ETH Up or Down 5m Down 65 bps Optimized Average LowerEnter Premarket |
| 134 | LowerEnter clone | Down | 70 | b7c50005-0001-4000-8210-000000000170 | eth_up_down_5m_down_optimized_average_bps_70_fak_lower_enter_premarket | ETH Up or Down 5m Down 70 bps Optimized Average LowerEnter Premarket |
| 135 | LowerEnter clone | Down | 75 | b7c50005-0001-4000-8210-000000000175 | eth_up_down_5m_down_optimized_average_bps_75_fak_lower_enter_premarket | ETH Up or Down 5m Down 75 bps Optimized Average LowerEnter Premarket |
| 136 | LowerEnter clone | Down | 80 | b7c50005-0001-4000-8210-000000000180 | eth_up_down_5m_down_optimized_average_bps_80_fak_lower_enter_premarket | ETH Up or Down 5m Down 80 bps Optimized Average LowerEnter Premarket |
| 137 | LowerEnter clone | Down | 85 | b7c50005-0001-4000-8210-000000000185 | eth_up_down_5m_down_optimized_average_bps_85_fak_lower_enter_premarket | ETH Up or Down 5m Down 85 bps Optimized Average LowerEnter Premarket |
| 138 | LowerEnter clone | Down | 90 | b7c50005-0001-4000-8210-000000000190 | eth_up_down_5m_down_optimized_average_bps_90_fak_lower_enter_premarket | ETH Up or Down 5m Down 90 bps Optimized Average LowerEnter Premarket |
| 139 | LowerEnter clone | Down | 95 | b7c50005-0001-4000-8210-000000000195 | eth_up_down_5m_down_optimized_average_bps_95_fak_lower_enter_premarket | ETH Up or Down 5m Down 95 bps Optimized Average LowerEnter Premarket |
| 140 | LowerEnter clone | Down | 100 | b7c50005-0001-4000-8210-000000000200 | eth_up_down_5m_down_optimized_average_bps_100_fak_lower_enter_premarket | ETH Up or Down 5m Down 100 bps Optimized Average LowerEnter Premarket |
| 141 | LowerEnter clone | Neutral | 1 | b7c50005-0001-4000-8211-000000000101 | eth_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket | ETH Up or Down 5m 1 bps Optimized Average LowerEnter Premarket |
| 142 | LowerEnter clone | Neutral | 2 | b7c50005-0001-4000-8211-000000000102 | eth_up_down_5m_optimized_average_bps_2_fak_lower_enter_premarket | ETH Up or Down 5m 2 bps Optimized Average LowerEnter Premarket |
| 143 | LowerEnter clone | Neutral | 3 | b7c50005-0001-4000-8211-000000000103 | eth_up_down_5m_optimized_average_bps_3_fak_lower_enter_premarket | ETH Up or Down 5m 3 bps Optimized Average LowerEnter Premarket |
| 144 | LowerEnter clone | Neutral | 4 | b7c50005-0001-4000-8211-000000000104 | eth_up_down_5m_optimized_average_bps_4_fak_lower_enter_premarket | ETH Up or Down 5m 4 bps Optimized Average LowerEnter Premarket |
| 145 | LowerEnter clone | Neutral | 5 | b7c50005-0001-4000-8211-000000000105 | eth_up_down_5m_optimized_average_bps_5_fak_lower_enter_premarket | ETH Up or Down 5m 5 bps Optimized Average LowerEnter Premarket |
| 146 | LowerEnter clone | Neutral | 6 | b7c50005-0001-4000-8211-000000000106 | eth_up_down_5m_optimized_average_bps_6_fak_lower_enter_premarket | ETH Up or Down 5m 6 bps Optimized Average LowerEnter Premarket |
| 147 | LowerEnter clone | Neutral | 7 | b7c50005-0001-4000-8211-000000000107 | eth_up_down_5m_optimized_average_bps_7_fak_lower_enter_premarket | ETH Up or Down 5m 7 bps Optimized Average LowerEnter Premarket |
| 148 | LowerEnter clone | Neutral | 8 | b7c50005-0001-4000-8211-000000000108 | eth_up_down_5m_optimized_average_bps_8_fak_lower_enter_premarket | ETH Up or Down 5m 8 bps Optimized Average LowerEnter Premarket |
| 149 | LowerEnter clone | Neutral | 9 | b7c50005-0001-4000-8211-000000000109 | eth_up_down_5m_optimized_average_bps_9_fak_lower_enter_premarket | ETH Up or Down 5m 9 bps Optimized Average LowerEnter Premarket |
| 150 | LowerEnter clone | Neutral | 10 | b7c50005-0001-4000-8211-000000000110 | eth_up_down_5m_optimized_average_bps_10_fak_lower_enter_premarket | ETH Up or Down 5m 10 bps Optimized Average LowerEnter Premarket |
| 151 | LowerEnter clone | Neutral | 15 | b7c50005-0001-4000-8211-000000000115 | eth_up_down_5m_optimized_average_bps_15_fak_lower_enter_premarket | ETH Up or Down 5m 15 bps Optimized Average LowerEnter Premarket |
| 152 | LowerEnter clone | Neutral | 20 | b7c50005-0001-4000-8211-000000000120 | eth_up_down_5m_optimized_average_bps_20_fak_lower_enter_premarket | ETH Up or Down 5m 20 bps Optimized Average LowerEnter Premarket |
| 153 | LowerEnter clone | Neutral | 25 | b7c50005-0001-4000-8211-000000000125 | eth_up_down_5m_optimized_average_bps_25_fak_lower_enter_premarket | ETH Up or Down 5m 25 bps Optimized Average LowerEnter Premarket |
| 154 | LowerEnter clone | Neutral | 30 | b7c50005-0001-4000-8211-000000000130 | eth_up_down_5m_optimized_average_bps_30_fak_lower_enter_premarket | ETH Up or Down 5m 30 bps Optimized Average LowerEnter Premarket |
| 155 | LowerEnter clone | Neutral | 35 | b7c50005-0001-4000-8211-000000000135 | eth_up_down_5m_optimized_average_bps_35_fak_lower_enter_premarket | ETH Up or Down 5m 35 bps Optimized Average LowerEnter Premarket |
| 156 | LowerEnter clone | Neutral | 40 | b7c50005-0001-4000-8211-000000000140 | eth_up_down_5m_optimized_average_bps_40_fak_lower_enter_premarket | ETH Up or Down 5m 40 bps Optimized Average LowerEnter Premarket |
| 157 | LowerEnter clone | Neutral | 45 | b7c50005-0001-4000-8211-000000000145 | eth_up_down_5m_optimized_average_bps_45_fak_lower_enter_premarket | ETH Up or Down 5m 45 bps Optimized Average LowerEnter Premarket |
| 158 | LowerEnter clone | Neutral | 50 | b7c50005-0001-4000-8211-000000000150 | eth_up_down_5m_optimized_average_bps_50_fak_lower_enter_premarket | ETH Up or Down 5m 50 bps Optimized Average LowerEnter Premarket |
| 159 | LowerEnter clone | Neutral | 55 | b7c50005-0001-4000-8211-000000000155 | eth_up_down_5m_optimized_average_bps_55_fak_lower_enter_premarket | ETH Up or Down 5m 55 bps Optimized Average LowerEnter Premarket |
| 160 | LowerEnter clone | Neutral | 60 | b7c50005-0001-4000-8211-000000000160 | eth_up_down_5m_optimized_average_bps_60_fak_lower_enter_premarket | ETH Up or Down 5m 60 bps Optimized Average LowerEnter Premarket |
| 161 | LowerEnter clone | Neutral | 65 | b7c50005-0001-4000-8211-000000000165 | eth_up_down_5m_optimized_average_bps_65_fak_lower_enter_premarket | ETH Up or Down 5m 65 bps Optimized Average LowerEnter Premarket |
| 162 | LowerEnter clone | Neutral | 70 | b7c50005-0001-4000-8211-000000000170 | eth_up_down_5m_optimized_average_bps_70_fak_lower_enter_premarket | ETH Up or Down 5m 70 bps Optimized Average LowerEnter Premarket |
| 163 | LowerEnter clone | Neutral | 75 | b7c50005-0001-4000-8211-000000000175 | eth_up_down_5m_optimized_average_bps_75_fak_lower_enter_premarket | ETH Up or Down 5m 75 bps Optimized Average LowerEnter Premarket |
| 164 | LowerEnter clone | Neutral | 80 | b7c50005-0001-4000-8211-000000000180 | eth_up_down_5m_optimized_average_bps_80_fak_lower_enter_premarket | ETH Up or Down 5m 80 bps Optimized Average LowerEnter Premarket |
| 165 | LowerEnter clone | Neutral | 85 | b7c50005-0001-4000-8211-000000000185 | eth_up_down_5m_optimized_average_bps_85_fak_lower_enter_premarket | ETH Up or Down 5m 85 bps Optimized Average LowerEnter Premarket |
| 166 | LowerEnter clone | Neutral | 90 | b7c50005-0001-4000-8211-000000000190 | eth_up_down_5m_optimized_average_bps_90_fak_lower_enter_premarket | ETH Up or Down 5m 90 bps Optimized Average LowerEnter Premarket |
| 167 | LowerEnter clone | Neutral | 95 | b7c50005-0001-4000-8211-000000000195 | eth_up_down_5m_optimized_average_bps_95_fak_lower_enter_premarket | ETH Up or Down 5m 95 bps Optimized Average LowerEnter Premarket |
| 168 | LowerEnter clone | Neutral | 100 | b7c50005-0001-4000-8211-000000000200 | eth_up_down_5m_optimized_average_bps_100_fak_lower_enter_premarket | ETH Up or Down 5m 100 bps Optimized Average LowerEnter Premarket |

### Direct: Native LowEnter Reference Average — 28 variants

Uses the same neutral envelope signal directly, then applies the Paper-only average-fill-price cap.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Native LowEnter | Neutral | 1 | b7c50005-0000-4000-8214-000000000101 | eth_up_down_5m_low_enter_average_bps_1_fak_premarket | ETH Up or Down 5m 1 bps LowEnter Average Premarket |
| 2 | Native LowEnter | Neutral | 2 | b7c50005-0000-4000-8214-000000000102 | eth_up_down_5m_low_enter_average_bps_2_fak_premarket | ETH Up or Down 5m 2 bps LowEnter Average Premarket |
| 3 | Native LowEnter | Neutral | 3 | b7c50005-0000-4000-8214-000000000103 | eth_up_down_5m_low_enter_average_bps_3_fak_premarket | ETH Up or Down 5m 3 bps LowEnter Average Premarket |
| 4 | Native LowEnter | Neutral | 4 | b7c50005-0000-4000-8214-000000000104 | eth_up_down_5m_low_enter_average_bps_4_fak_premarket | ETH Up or Down 5m 4 bps LowEnter Average Premarket |
| 5 | Native LowEnter | Neutral | 5 | b7c50005-0000-4000-8214-000000000105 | eth_up_down_5m_low_enter_average_bps_5_fak_premarket | ETH Up or Down 5m 5 bps LowEnter Average Premarket |
| 6 | Native LowEnter | Neutral | 6 | b7c50005-0000-4000-8214-000000000106 | eth_up_down_5m_low_enter_average_bps_6_fak_premarket | ETH Up or Down 5m 6 bps LowEnter Average Premarket |
| 7 | Native LowEnter | Neutral | 7 | b7c50005-0000-4000-8214-000000000107 | eth_up_down_5m_low_enter_average_bps_7_fak_premarket | ETH Up or Down 5m 7 bps LowEnter Average Premarket |
| 8 | Native LowEnter | Neutral | 8 | b7c50005-0000-4000-8214-000000000108 | eth_up_down_5m_low_enter_average_bps_8_fak_premarket | ETH Up or Down 5m 8 bps LowEnter Average Premarket |
| 9 | Native LowEnter | Neutral | 9 | b7c50005-0000-4000-8214-000000000109 | eth_up_down_5m_low_enter_average_bps_9_fak_premarket | ETH Up or Down 5m 9 bps LowEnter Average Premarket |
| 10 | Native LowEnter | Neutral | 10 | b7c50005-0000-4000-8214-000000000110 | eth_up_down_5m_low_enter_average_bps_10_fak_premarket | ETH Up or Down 5m 10 bps LowEnter Average Premarket |
| 11 | Native LowEnter | Neutral | 15 | b7c50005-0000-4000-8214-000000000115 | eth_up_down_5m_low_enter_average_bps_15_fak_premarket | ETH Up or Down 5m 15 bps LowEnter Average Premarket |
| 12 | Native LowEnter | Neutral | 20 | b7c50005-0000-4000-8214-000000000120 | eth_up_down_5m_low_enter_average_bps_20_fak_premarket | ETH Up or Down 5m 20 bps LowEnter Average Premarket |
| 13 | Native LowEnter | Neutral | 25 | b7c50005-0000-4000-8214-000000000125 | eth_up_down_5m_low_enter_average_bps_25_fak_premarket | ETH Up or Down 5m 25 bps LowEnter Average Premarket |
| 14 | Native LowEnter | Neutral | 30 | b7c50005-0000-4000-8214-000000000130 | eth_up_down_5m_low_enter_average_bps_30_fak_premarket | ETH Up or Down 5m 30 bps LowEnter Average Premarket |
| 15 | Native LowEnter | Neutral | 35 | b7c50005-0000-4000-8214-000000000135 | eth_up_down_5m_low_enter_average_bps_35_fak_premarket | ETH Up or Down 5m 35 bps LowEnter Average Premarket |
| 16 | Native LowEnter | Neutral | 40 | b7c50005-0000-4000-8214-000000000140 | eth_up_down_5m_low_enter_average_bps_40_fak_premarket | ETH Up or Down 5m 40 bps LowEnter Average Premarket |
| 17 | Native LowEnter | Neutral | 45 | b7c50005-0000-4000-8214-000000000145 | eth_up_down_5m_low_enter_average_bps_45_fak_premarket | ETH Up or Down 5m 45 bps LowEnter Average Premarket |
| 18 | Native LowEnter | Neutral | 50 | b7c50005-0000-4000-8214-000000000150 | eth_up_down_5m_low_enter_average_bps_50_fak_premarket | ETH Up or Down 5m 50 bps LowEnter Average Premarket |
| 19 | Native LowEnter | Neutral | 55 | b7c50005-0000-4000-8214-000000000155 | eth_up_down_5m_low_enter_average_bps_55_fak_premarket | ETH Up or Down 5m 55 bps LowEnter Average Premarket |
| 20 | Native LowEnter | Neutral | 60 | b7c50005-0000-4000-8214-000000000160 | eth_up_down_5m_low_enter_average_bps_60_fak_premarket | ETH Up or Down 5m 60 bps LowEnter Average Premarket |
| 21 | Native LowEnter | Neutral | 65 | b7c50005-0000-4000-8214-000000000165 | eth_up_down_5m_low_enter_average_bps_65_fak_premarket | ETH Up or Down 5m 65 bps LowEnter Average Premarket |
| 22 | Native LowEnter | Neutral | 70 | b7c50005-0000-4000-8214-000000000170 | eth_up_down_5m_low_enter_average_bps_70_fak_premarket | ETH Up or Down 5m 70 bps LowEnter Average Premarket |
| 23 | Native LowEnter | Neutral | 75 | b7c50005-0000-4000-8214-000000000175 | eth_up_down_5m_low_enter_average_bps_75_fak_premarket | ETH Up or Down 5m 75 bps LowEnter Average Premarket |
| 24 | Native LowEnter | Neutral | 80 | b7c50005-0000-4000-8214-000000000180 | eth_up_down_5m_low_enter_average_bps_80_fak_premarket | ETH Up or Down 5m 80 bps LowEnter Average Premarket |
| 25 | Native LowEnter | Neutral | 85 | b7c50005-0000-4000-8214-000000000185 | eth_up_down_5m_low_enter_average_bps_85_fak_premarket | ETH Up or Down 5m 85 bps LowEnter Average Premarket |
| 26 | Native LowEnter | Neutral | 90 | b7c50005-0000-4000-8214-000000000190 | eth_up_down_5m_low_enter_average_bps_90_fak_premarket | ETH Up or Down 5m 90 bps LowEnter Average Premarket |
| 27 | Native LowEnter | Neutral | 95 | b7c50005-0000-4000-8214-000000000195 | eth_up_down_5m_low_enter_average_bps_95_fak_premarket | ETH Up or Down 5m 95 bps LowEnter Average Premarket |
| 28 | Native LowEnter | Neutral | 100 | b7c50005-0000-4000-8214-000000000200 | eth_up_down_5m_low_enter_average_bps_100_fak_premarket | ETH Up or Down 5m 100 bps LowEnter Average Premarket |

### Indirect: Bps Confirmed Average — 28 variants

Recomputes a linked neutral Reference Average base signal at the same Bps threshold, then requires agreement with a Diff Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8201-000000000101 | eth_up_down_5m_1_bps_confirmed_average_premarket | ETH Up or Down 5m 1 bps Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8201-000000000102 | eth_up_down_5m_2_bps_confirmed_average_premarket | ETH Up or Down 5m 2 bps Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8201-000000000103 | eth_up_down_5m_3_bps_confirmed_average_premarket | ETH Up or Down 5m 3 bps Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8201-000000000104 | eth_up_down_5m_4_bps_confirmed_average_premarket | ETH Up or Down 5m 4 bps Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8201-000000000105 | eth_up_down_5m_5_bps_confirmed_average_premarket | ETH Up or Down 5m 5 bps Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8201-000000000106 | eth_up_down_5m_6_bps_confirmed_average_premarket | ETH Up or Down 5m 6 bps Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8201-000000000107 | eth_up_down_5m_7_bps_confirmed_average_premarket | ETH Up or Down 5m 7 bps Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8201-000000000108 | eth_up_down_5m_8_bps_confirmed_average_premarket | ETH Up or Down 5m 8 bps Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8201-000000000109 | eth_up_down_5m_9_bps_confirmed_average_premarket | ETH Up or Down 5m 9 bps Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8201-000000000110 | eth_up_down_5m_10_bps_confirmed_average_premarket | ETH Up or Down 5m 10 bps Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8201-000000000115 | eth_up_down_5m_15_bps_confirmed_average_premarket | ETH Up or Down 5m 15 bps Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8201-000000000120 | eth_up_down_5m_20_bps_confirmed_average_premarket | ETH Up or Down 5m 20 bps Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8201-000000000125 | eth_up_down_5m_25_bps_confirmed_average_premarket | ETH Up or Down 5m 25 bps Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8201-000000000130 | eth_up_down_5m_30_bps_confirmed_average_premarket | ETH Up or Down 5m 30 bps Confirmed Average Premarket |
| 15 | Base | Composite | 35 | b7c50005-0000-4000-8201-000000000135 | eth_up_down_5m_35_bps_confirmed_average_premarket | ETH Up or Down 5m 35 bps Confirmed Average Premarket |
| 16 | Base | Composite | 40 | b7c50005-0000-4000-8201-000000000140 | eth_up_down_5m_40_bps_confirmed_average_premarket | ETH Up or Down 5m 40 bps Confirmed Average Premarket |
| 17 | Base | Composite | 45 | b7c50005-0000-4000-8201-000000000145 | eth_up_down_5m_45_bps_confirmed_average_premarket | ETH Up or Down 5m 45 bps Confirmed Average Premarket |
| 18 | Base | Composite | 50 | b7c50005-0000-4000-8201-000000000150 | eth_up_down_5m_50_bps_confirmed_average_premarket | ETH Up or Down 5m 50 bps Confirmed Average Premarket |
| 19 | Base | Composite | 55 | b7c50005-0000-4000-8201-000000000155 | eth_up_down_5m_55_bps_confirmed_average_premarket | ETH Up or Down 5m 55 bps Confirmed Average Premarket |
| 20 | Base | Composite | 60 | b7c50005-0000-4000-8201-000000000160 | eth_up_down_5m_60_bps_confirmed_average_premarket | ETH Up or Down 5m 60 bps Confirmed Average Premarket |
| 21 | Base | Composite | 65 | b7c50005-0000-4000-8201-000000000165 | eth_up_down_5m_65_bps_confirmed_average_premarket | ETH Up or Down 5m 65 bps Confirmed Average Premarket |
| 22 | Base | Composite | 70 | b7c50005-0000-4000-8201-000000000170 | eth_up_down_5m_70_bps_confirmed_average_premarket | ETH Up or Down 5m 70 bps Confirmed Average Premarket |
| 23 | Base | Composite | 75 | b7c50005-0000-4000-8201-000000000175 | eth_up_down_5m_75_bps_confirmed_average_premarket | ETH Up or Down 5m 75 bps Confirmed Average Premarket |
| 24 | Base | Composite | 80 | b7c50005-0000-4000-8201-000000000180 | eth_up_down_5m_80_bps_confirmed_average_premarket | ETH Up or Down 5m 80 bps Confirmed Average Premarket |
| 25 | Base | Composite | 85 | b7c50005-0000-4000-8201-000000000185 | eth_up_down_5m_85_bps_confirmed_average_premarket | ETH Up or Down 5m 85 bps Confirmed Average Premarket |
| 26 | Base | Composite | 90 | b7c50005-0000-4000-8201-000000000190 | eth_up_down_5m_90_bps_confirmed_average_premarket | ETH Up or Down 5m 90 bps Confirmed Average Premarket |
| 27 | Base | Composite | 95 | b7c50005-0000-4000-8201-000000000195 | eth_up_down_5m_95_bps_confirmed_average_premarket | ETH Up or Down 5m 95 bps Confirmed Average Premarket |
| 28 | Base | Composite | 100 | b7c50005-0000-4000-8201-000000000200 | eth_up_down_5m_100_bps_confirmed_average_premarket | ETH Up or Down 5m 100 bps Confirmed Average Premarket |

### Indirect: Diff Confirmed Average — 14 variants

Uses Diff Reference Average as its base signal and recomputes a linked neutral price Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8204-000000000001 | eth_up_down_5m_1_diff_confirmed_average_premarket | ETH Up or Down 5m 1 Diff Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8204-000000000002 | eth_up_down_5m_2_diff_confirmed_average_premarket | ETH Up or Down 5m 2 Diff Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8204-000000000003 | eth_up_down_5m_3_diff_confirmed_average_premarket | ETH Up or Down 5m 3 Diff Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8204-000000000004 | eth_up_down_5m_4_diff_confirmed_average_premarket | ETH Up or Down 5m 4 Diff Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8204-000000000005 | eth_up_down_5m_5_diff_confirmed_average_premarket | ETH Up or Down 5m 5 Diff Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8204-000000000006 | eth_up_down_5m_6_diff_confirmed_average_premarket | ETH Up or Down 5m 6 Diff Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8204-000000000007 | eth_up_down_5m_7_diff_confirmed_average_premarket | ETH Up or Down 5m 7 Diff Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8204-000000000008 | eth_up_down_5m_8_diff_confirmed_average_premarket | ETH Up or Down 5m 8 Diff Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8204-000000000009 | eth_up_down_5m_9_diff_confirmed_average_premarket | ETH Up or Down 5m 9 Diff Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8204-000000000010 | eth_up_down_5m_10_diff_confirmed_average_premarket | ETH Up or Down 5m 10 Diff Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8204-000000000015 | eth_up_down_5m_15_diff_confirmed_average_premarket | ETH Up or Down 5m 15 Diff Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8204-000000000020 | eth_up_down_5m_20_diff_confirmed_average_premarket | ETH Up or Down 5m 20 Diff Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8204-000000000025 | eth_up_down_5m_25_diff_confirmed_average_premarket | ETH Up or Down 5m 25 Diff Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8204-000000000030 | eth_up_down_5m_30_diff_confirmed_average_premarket | ETH Up or Down 5m 30 Diff Confirmed Average Premarket |

## SOL — 214 affected variants

### Direct: Reference Average — 84 variants

Calls GetReferenceAverageBpsThresholdEntryDecisionAsync directly for fixed Up, fixed Down, and neutral variants.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8138-000000000101 | sol_up_down_5m_up_bps_1_fak_premarket | SOL Up or Down 5m Up 1 bps Reference Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8138-000000000102 | sol_up_down_5m_up_bps_2_fak_premarket | SOL Up or Down 5m Up 2 bps Reference Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8138-000000000103 | sol_up_down_5m_up_bps_3_fak_premarket | SOL Up or Down 5m Up 3 bps Reference Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8138-000000000104 | sol_up_down_5m_up_bps_4_fak_premarket | SOL Up or Down 5m Up 4 bps Reference Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8138-000000000105 | sol_up_down_5m_up_bps_5_fak_premarket | SOL Up or Down 5m Up 5 bps Reference Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8138-000000000106 | sol_up_down_5m_up_bps_6_fak_premarket | SOL Up or Down 5m Up 6 bps Reference Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8138-000000000107 | sol_up_down_5m_up_bps_7_fak_premarket | SOL Up or Down 5m Up 7 bps Reference Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8138-000000000108 | sol_up_down_5m_up_bps_8_fak_premarket | SOL Up or Down 5m Up 8 bps Reference Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8138-000000000109 | sol_up_down_5m_up_bps_9_fak_premarket | SOL Up or Down 5m Up 9 bps Reference Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8138-000000000110 | sol_up_down_5m_up_bps_10_fak_premarket | SOL Up or Down 5m Up 10 bps Reference Average Premarket |
| 11 | Base | Up | 15 | b7c50005-0000-4000-8138-000000000115 | sol_up_down_5m_up_bps_15_fak_premarket | SOL Up or Down 5m Up 15 bps Reference Average Premarket |
| 12 | Base | Up | 20 | b7c50005-0000-4000-8138-000000000120 | sol_up_down_5m_up_bps_20_fak_premarket | SOL Up or Down 5m Up 20 bps Reference Average Premarket |
| 13 | Base | Up | 25 | b7c50005-0000-4000-8138-000000000125 | sol_up_down_5m_up_bps_25_fak_premarket | SOL Up or Down 5m Up 25 bps Reference Average Premarket |
| 14 | Base | Up | 30 | b7c50005-0000-4000-8138-000000000130 | sol_up_down_5m_up_bps_30_fak_premarket | SOL Up or Down 5m Up 30 bps Reference Average Premarket |
| 15 | Base | Up | 35 | b7c50005-0000-4000-8138-000000000135 | sol_up_down_5m_up_bps_35_fak_premarket | SOL Up or Down 5m Up 35 bps Reference Average Premarket |
| 16 | Base | Up | 40 | b7c50005-0000-4000-8138-000000000140 | sol_up_down_5m_up_bps_40_fak_premarket | SOL Up or Down 5m Up 40 bps Reference Average Premarket |
| 17 | Base | Up | 45 | b7c50005-0000-4000-8138-000000000145 | sol_up_down_5m_up_bps_45_fak_premarket | SOL Up or Down 5m Up 45 bps Reference Average Premarket |
| 18 | Base | Up | 50 | b7c50005-0000-4000-8138-000000000150 | sol_up_down_5m_up_bps_50_fak_premarket | SOL Up or Down 5m Up 50 bps Reference Average Premarket |
| 19 | Base | Up | 55 | b7c50005-0000-4000-8138-000000000155 | sol_up_down_5m_up_bps_55_fak_premarket | SOL Up or Down 5m Up 55 bps Reference Average Premarket |
| 20 | Base | Up | 60 | b7c50005-0000-4000-8138-000000000160 | sol_up_down_5m_up_bps_60_fak_premarket | SOL Up or Down 5m Up 60 bps Reference Average Premarket |
| 21 | Base | Up | 65 | b7c50005-0000-4000-8138-000000000165 | sol_up_down_5m_up_bps_65_fak_premarket | SOL Up or Down 5m Up 65 bps Reference Average Premarket |
| 22 | Base | Up | 70 | b7c50005-0000-4000-8138-000000000170 | sol_up_down_5m_up_bps_70_fak_premarket | SOL Up or Down 5m Up 70 bps Reference Average Premarket |
| 23 | Base | Up | 75 | b7c50005-0000-4000-8138-000000000175 | sol_up_down_5m_up_bps_75_fak_premarket | SOL Up or Down 5m Up 75 bps Reference Average Premarket |
| 24 | Base | Up | 80 | b7c50005-0000-4000-8138-000000000180 | sol_up_down_5m_up_bps_80_fak_premarket | SOL Up or Down 5m Up 80 bps Reference Average Premarket |
| 25 | Base | Up | 85 | b7c50005-0000-4000-8138-000000000185 | sol_up_down_5m_up_bps_85_fak_premarket | SOL Up or Down 5m Up 85 bps Reference Average Premarket |
| 26 | Base | Up | 90 | b7c50005-0000-4000-8138-000000000190 | sol_up_down_5m_up_bps_90_fak_premarket | SOL Up or Down 5m Up 90 bps Reference Average Premarket |
| 27 | Base | Up | 95 | b7c50005-0000-4000-8138-000000000195 | sol_up_down_5m_up_bps_95_fak_premarket | SOL Up or Down 5m Up 95 bps Reference Average Premarket |
| 28 | Base | Up | 100 | b7c50005-0000-4000-8138-000000000200 | sol_up_down_5m_up_bps_100_fak_premarket | SOL Up or Down 5m Up 100 bps Reference Average Premarket |
| 29 | Base | Down | 1 | b7c50005-0000-4000-8139-000000000101 | sol_up_down_5m_down_bps_1_fak_premarket | SOL Up or Down 5m Down 1 bps Reference Average Premarket |
| 30 | Base | Down | 2 | b7c50005-0000-4000-8139-000000000102 | sol_up_down_5m_down_bps_2_fak_premarket | SOL Up or Down 5m Down 2 bps Reference Average Premarket |
| 31 | Base | Down | 3 | b7c50005-0000-4000-8139-000000000103 | sol_up_down_5m_down_bps_3_fak_premarket | SOL Up or Down 5m Down 3 bps Reference Average Premarket |
| 32 | Base | Down | 4 | b7c50005-0000-4000-8139-000000000104 | sol_up_down_5m_down_bps_4_fak_premarket | SOL Up or Down 5m Down 4 bps Reference Average Premarket |
| 33 | Base | Down | 5 | b7c50005-0000-4000-8139-000000000105 | sol_up_down_5m_down_bps_5_fak_premarket | SOL Up or Down 5m Down 5 bps Reference Average Premarket |
| 34 | Base | Down | 6 | b7c50005-0000-4000-8139-000000000106 | sol_up_down_5m_down_bps_6_fak_premarket | SOL Up or Down 5m Down 6 bps Reference Average Premarket |
| 35 | Base | Down | 7 | b7c50005-0000-4000-8139-000000000107 | sol_up_down_5m_down_bps_7_fak_premarket | SOL Up or Down 5m Down 7 bps Reference Average Premarket |
| 36 | Base | Down | 8 | b7c50005-0000-4000-8139-000000000108 | sol_up_down_5m_down_bps_8_fak_premarket | SOL Up or Down 5m Down 8 bps Reference Average Premarket |
| 37 | Base | Down | 9 | b7c50005-0000-4000-8139-000000000109 | sol_up_down_5m_down_bps_9_fak_premarket | SOL Up or Down 5m Down 9 bps Reference Average Premarket |
| 38 | Base | Down | 10 | b7c50005-0000-4000-8139-000000000110 | sol_up_down_5m_down_bps_10_fak_premarket | SOL Up or Down 5m Down 10 bps Reference Average Premarket |
| 39 | Base | Down | 15 | b7c50005-0000-4000-8139-000000000115 | sol_up_down_5m_down_bps_15_fak_premarket | SOL Up or Down 5m Down 15 bps Reference Average Premarket |
| 40 | Base | Down | 20 | b7c50005-0000-4000-8139-000000000120 | sol_up_down_5m_down_bps_20_fak_premarket | SOL Up or Down 5m Down 20 bps Reference Average Premarket |
| 41 | Base | Down | 25 | b7c50005-0000-4000-8139-000000000125 | sol_up_down_5m_down_bps_25_fak_premarket | SOL Up or Down 5m Down 25 bps Reference Average Premarket |
| 42 | Base | Down | 30 | b7c50005-0000-4000-8139-000000000130 | sol_up_down_5m_down_bps_30_fak_premarket | SOL Up or Down 5m Down 30 bps Reference Average Premarket |
| 43 | Base | Down | 35 | b7c50005-0000-4000-8139-000000000135 | sol_up_down_5m_down_bps_35_fak_premarket | SOL Up or Down 5m Down 35 bps Reference Average Premarket |
| 44 | Base | Down | 40 | b7c50005-0000-4000-8139-000000000140 | sol_up_down_5m_down_bps_40_fak_premarket | SOL Up or Down 5m Down 40 bps Reference Average Premarket |
| 45 | Base | Down | 45 | b7c50005-0000-4000-8139-000000000145 | sol_up_down_5m_down_bps_45_fak_premarket | SOL Up or Down 5m Down 45 bps Reference Average Premarket |
| 46 | Base | Down | 50 | b7c50005-0000-4000-8139-000000000150 | sol_up_down_5m_down_bps_50_fak_premarket | SOL Up or Down 5m Down 50 bps Reference Average Premarket |
| 47 | Base | Down | 55 | b7c50005-0000-4000-8139-000000000155 | sol_up_down_5m_down_bps_55_fak_premarket | SOL Up or Down 5m Down 55 bps Reference Average Premarket |
| 48 | Base | Down | 60 | b7c50005-0000-4000-8139-000000000160 | sol_up_down_5m_down_bps_60_fak_premarket | SOL Up or Down 5m Down 60 bps Reference Average Premarket |
| 49 | Base | Down | 65 | b7c50005-0000-4000-8139-000000000165 | sol_up_down_5m_down_bps_65_fak_premarket | SOL Up or Down 5m Down 65 bps Reference Average Premarket |
| 50 | Base | Down | 70 | b7c50005-0000-4000-8139-000000000170 | sol_up_down_5m_down_bps_70_fak_premarket | SOL Up or Down 5m Down 70 bps Reference Average Premarket |
| 51 | Base | Down | 75 | b7c50005-0000-4000-8139-000000000175 | sol_up_down_5m_down_bps_75_fak_premarket | SOL Up or Down 5m Down 75 bps Reference Average Premarket |
| 52 | Base | Down | 80 | b7c50005-0000-4000-8139-000000000180 | sol_up_down_5m_down_bps_80_fak_premarket | SOL Up or Down 5m Down 80 bps Reference Average Premarket |
| 53 | Base | Down | 85 | b7c50005-0000-4000-8139-000000000185 | sol_up_down_5m_down_bps_85_fak_premarket | SOL Up or Down 5m Down 85 bps Reference Average Premarket |
| 54 | Base | Down | 90 | b7c50005-0000-4000-8139-000000000190 | sol_up_down_5m_down_bps_90_fak_premarket | SOL Up or Down 5m Down 90 bps Reference Average Premarket |
| 55 | Base | Down | 95 | b7c50005-0000-4000-8139-000000000195 | sol_up_down_5m_down_bps_95_fak_premarket | SOL Up or Down 5m Down 95 bps Reference Average Premarket |
| 56 | Base | Down | 100 | b7c50005-0000-4000-8139-000000000200 | sol_up_down_5m_down_bps_100_fak_premarket | SOL Up or Down 5m Down 100 bps Reference Average Premarket |
| 57 | Base | Neutral | 1 | b7c50005-0000-4000-8180-000000000101 | sol_up_down_5m_reference_average_bps_1_fak_premarket | SOL Up or Down 5m 1 bps Reference Average Premarket |
| 58 | Base | Neutral | 2 | b7c50005-0000-4000-8180-000000000102 | sol_up_down_5m_reference_average_bps_2_fak_premarket | SOL Up or Down 5m 2 bps Reference Average Premarket |
| 59 | Base | Neutral | 3 | b7c50005-0000-4000-8180-000000000103 | sol_up_down_5m_reference_average_bps_3_fak_premarket | SOL Up or Down 5m 3 bps Reference Average Premarket |
| 60 | Base | Neutral | 4 | b7c50005-0000-4000-8180-000000000104 | sol_up_down_5m_reference_average_bps_4_fak_premarket | SOL Up or Down 5m 4 bps Reference Average Premarket |
| 61 | Base | Neutral | 5 | b7c50005-0000-4000-8180-000000000105 | sol_up_down_5m_reference_average_bps_5_fak_premarket | SOL Up or Down 5m 5 bps Reference Average Premarket |
| 62 | Base | Neutral | 6 | b7c50005-0000-4000-8180-000000000106 | sol_up_down_5m_reference_average_bps_6_fak_premarket | SOL Up or Down 5m 6 bps Reference Average Premarket |
| 63 | Base | Neutral | 7 | b7c50005-0000-4000-8180-000000000107 | sol_up_down_5m_reference_average_bps_7_fak_premarket | SOL Up or Down 5m 7 bps Reference Average Premarket |
| 64 | Base | Neutral | 8 | b7c50005-0000-4000-8180-000000000108 | sol_up_down_5m_reference_average_bps_8_fak_premarket | SOL Up or Down 5m 8 bps Reference Average Premarket |
| 65 | Base | Neutral | 9 | b7c50005-0000-4000-8180-000000000109 | sol_up_down_5m_reference_average_bps_9_fak_premarket | SOL Up or Down 5m 9 bps Reference Average Premarket |
| 66 | Base | Neutral | 10 | b7c50005-0000-4000-8180-000000000110 | sol_up_down_5m_reference_average_bps_10_fak_premarket | SOL Up or Down 5m 10 bps Reference Average Premarket |
| 67 | Base | Neutral | 15 | b7c50005-0000-4000-8180-000000000115 | sol_up_down_5m_reference_average_bps_15_fak_premarket | SOL Up or Down 5m 15 bps Reference Average Premarket |
| 68 | Base | Neutral | 20 | b7c50005-0000-4000-8180-000000000120 | sol_up_down_5m_reference_average_bps_20_fak_premarket | SOL Up or Down 5m 20 bps Reference Average Premarket |
| 69 | Base | Neutral | 25 | b7c50005-0000-4000-8180-000000000125 | sol_up_down_5m_reference_average_bps_25_fak_premarket | SOL Up or Down 5m 25 bps Reference Average Premarket |
| 70 | Base | Neutral | 30 | b7c50005-0000-4000-8180-000000000130 | sol_up_down_5m_reference_average_bps_30_fak_premarket | SOL Up or Down 5m 30 bps Reference Average Premarket |
| 71 | Base | Neutral | 35 | b7c50005-0000-4000-8180-000000000135 | sol_up_down_5m_reference_average_bps_35_fak_premarket | SOL Up or Down 5m 35 bps Reference Average Premarket |
| 72 | Base | Neutral | 40 | b7c50005-0000-4000-8180-000000000140 | sol_up_down_5m_reference_average_bps_40_fak_premarket | SOL Up or Down 5m 40 bps Reference Average Premarket |
| 73 | Base | Neutral | 45 | b7c50005-0000-4000-8180-000000000145 | sol_up_down_5m_reference_average_bps_45_fak_premarket | SOL Up or Down 5m 45 bps Reference Average Premarket |
| 74 | Base | Neutral | 50 | b7c50005-0000-4000-8180-000000000150 | sol_up_down_5m_reference_average_bps_50_fak_premarket | SOL Up or Down 5m 50 bps Reference Average Premarket |
| 75 | Base | Neutral | 55 | b7c50005-0000-4000-8180-000000000155 | sol_up_down_5m_reference_average_bps_55_fak_premarket | SOL Up or Down 5m 55 bps Reference Average Premarket |
| 76 | Base | Neutral | 60 | b7c50005-0000-4000-8180-000000000160 | sol_up_down_5m_reference_average_bps_60_fak_premarket | SOL Up or Down 5m 60 bps Reference Average Premarket |
| 77 | Base | Neutral | 65 | b7c50005-0000-4000-8180-000000000165 | sol_up_down_5m_reference_average_bps_65_fak_premarket | SOL Up or Down 5m 65 bps Reference Average Premarket |
| 78 | Base | Neutral | 70 | b7c50005-0000-4000-8180-000000000170 | sol_up_down_5m_reference_average_bps_70_fak_premarket | SOL Up or Down 5m 70 bps Reference Average Premarket |
| 79 | Base | Neutral | 75 | b7c50005-0000-4000-8180-000000000175 | sol_up_down_5m_reference_average_bps_75_fak_premarket | SOL Up or Down 5m 75 bps Reference Average Premarket |
| 80 | Base | Neutral | 80 | b7c50005-0000-4000-8180-000000000180 | sol_up_down_5m_reference_average_bps_80_fak_premarket | SOL Up or Down 5m 80 bps Reference Average Premarket |
| 81 | Base | Neutral | 85 | b7c50005-0000-4000-8180-000000000185 | sol_up_down_5m_reference_average_bps_85_fak_premarket | SOL Up or Down 5m 85 bps Reference Average Premarket |
| 82 | Base | Neutral | 90 | b7c50005-0000-4000-8180-000000000190 | sol_up_down_5m_reference_average_bps_90_fak_premarket | SOL Up or Down 5m 90 bps Reference Average Premarket |
| 83 | Base | Neutral | 95 | b7c50005-0000-4000-8180-000000000195 | sol_up_down_5m_reference_average_bps_95_fak_premarket | SOL Up or Down 5m 95 bps Reference Average Premarket |
| 84 | Base | Neutral | 100 | b7c50005-0000-4000-8180-000000000200 | sol_up_down_5m_reference_average_bps_100_fak_premarket | SOL Up or Down 5m 100 bps Reference Average Premarket |

### Direct: Optimized Reference Average — 60 variants

Calls the shared selector directly and additionally requires the direction-relevant selected boundary window to be 3h.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Up | 1 | b7c50005-0000-4000-8221-000000000101 | sol_up_down_5m_up_optimized_average_bps_1_fak_premarket | SOL Up or Down 5m Up 1 bps Optimized Average Premarket |
| 2 | Base | Up | 2 | b7c50005-0000-4000-8221-000000000102 | sol_up_down_5m_up_optimized_average_bps_2_fak_premarket | SOL Up or Down 5m Up 2 bps Optimized Average Premarket |
| 3 | Base | Up | 3 | b7c50005-0000-4000-8221-000000000103 | sol_up_down_5m_up_optimized_average_bps_3_fak_premarket | SOL Up or Down 5m Up 3 bps Optimized Average Premarket |
| 4 | Base | Up | 4 | b7c50005-0000-4000-8221-000000000104 | sol_up_down_5m_up_optimized_average_bps_4_fak_premarket | SOL Up or Down 5m Up 4 bps Optimized Average Premarket |
| 5 | Base | Up | 5 | b7c50005-0000-4000-8221-000000000105 | sol_up_down_5m_up_optimized_average_bps_5_fak_premarket | SOL Up or Down 5m Up 5 bps Optimized Average Premarket |
| 6 | Base | Up | 6 | b7c50005-0000-4000-8221-000000000106 | sol_up_down_5m_up_optimized_average_bps_6_fak_premarket | SOL Up or Down 5m Up 6 bps Optimized Average Premarket |
| 7 | Base | Up | 7 | b7c50005-0000-4000-8221-000000000107 | sol_up_down_5m_up_optimized_average_bps_7_fak_premarket | SOL Up or Down 5m Up 7 bps Optimized Average Premarket |
| 8 | Base | Up | 8 | b7c50005-0000-4000-8221-000000000108 | sol_up_down_5m_up_optimized_average_bps_8_fak_premarket | SOL Up or Down 5m Up 8 bps Optimized Average Premarket |
| 9 | Base | Up | 9 | b7c50005-0000-4000-8221-000000000109 | sol_up_down_5m_up_optimized_average_bps_9_fak_premarket | SOL Up or Down 5m Up 9 bps Optimized Average Premarket |
| 10 | Base | Up | 10 | b7c50005-0000-4000-8221-000000000110 | sol_up_down_5m_up_optimized_average_bps_10_fak_premarket | SOL Up or Down 5m Up 10 bps Optimized Average Premarket |
| 11 | LowerEnter clone | Up | 1 | b7c50005-0001-4000-8221-000000000101 | sol_up_down_5m_up_optimized_average_bps_1_fak_lower_enter_premarket | SOL Up or Down 5m Up 1 bps Optimized Average LowerEnter Premarket |
| 12 | LowerEnter clone | Up | 2 | b7c50005-0001-4000-8221-000000000102 | sol_up_down_5m_up_optimized_average_bps_2_fak_lower_enter_premarket | SOL Up or Down 5m Up 2 bps Optimized Average LowerEnter Premarket |
| 13 | LowerEnter clone | Up | 3 | b7c50005-0001-4000-8221-000000000103 | sol_up_down_5m_up_optimized_average_bps_3_fak_lower_enter_premarket | SOL Up or Down 5m Up 3 bps Optimized Average LowerEnter Premarket |
| 14 | LowerEnter clone | Up | 4 | b7c50005-0001-4000-8221-000000000104 | sol_up_down_5m_up_optimized_average_bps_4_fak_lower_enter_premarket | SOL Up or Down 5m Up 4 bps Optimized Average LowerEnter Premarket |
| 15 | LowerEnter clone | Up | 5 | b7c50005-0001-4000-8221-000000000105 | sol_up_down_5m_up_optimized_average_bps_5_fak_lower_enter_premarket | SOL Up or Down 5m Up 5 bps Optimized Average LowerEnter Premarket |
| 16 | LowerEnter clone | Up | 6 | b7c50005-0001-4000-8221-000000000106 | sol_up_down_5m_up_optimized_average_bps_6_fak_lower_enter_premarket | SOL Up or Down 5m Up 6 bps Optimized Average LowerEnter Premarket |
| 17 | LowerEnter clone | Up | 7 | b7c50005-0001-4000-8221-000000000107 | sol_up_down_5m_up_optimized_average_bps_7_fak_lower_enter_premarket | SOL Up or Down 5m Up 7 bps Optimized Average LowerEnter Premarket |
| 18 | LowerEnter clone | Up | 8 | b7c50005-0001-4000-8221-000000000108 | sol_up_down_5m_up_optimized_average_bps_8_fak_lower_enter_premarket | SOL Up or Down 5m Up 8 bps Optimized Average LowerEnter Premarket |
| 19 | LowerEnter clone | Up | 9 | b7c50005-0001-4000-8221-000000000109 | sol_up_down_5m_up_optimized_average_bps_9_fak_lower_enter_premarket | SOL Up or Down 5m Up 9 bps Optimized Average LowerEnter Premarket |
| 20 | LowerEnter clone | Up | 10 | b7c50005-0001-4000-8221-000000000110 | sol_up_down_5m_up_optimized_average_bps_10_fak_lower_enter_premarket | SOL Up or Down 5m Up 10 bps Optimized Average LowerEnter Premarket |
| 21 | Base | Down | 1 | b7c50005-0000-4000-8218-000000000101 | sol_up_down_5m_down_optimized_average_bps_1_fak_premarket | SOL Up or Down 5m Down 1 bps Optimized Average Premarket |
| 22 | Base | Down | 2 | b7c50005-0000-4000-8218-000000000102 | sol_up_down_5m_down_optimized_average_bps_2_fak_premarket | SOL Up or Down 5m Down 2 bps Optimized Average Premarket |
| 23 | Base | Down | 3 | b7c50005-0000-4000-8218-000000000103 | sol_up_down_5m_down_optimized_average_bps_3_fak_premarket | SOL Up or Down 5m Down 3 bps Optimized Average Premarket |
| 24 | Base | Down | 4 | b7c50005-0000-4000-8218-000000000104 | sol_up_down_5m_down_optimized_average_bps_4_fak_premarket | SOL Up or Down 5m Down 4 bps Optimized Average Premarket |
| 25 | Base | Down | 5 | b7c50005-0000-4000-8218-000000000105 | sol_up_down_5m_down_optimized_average_bps_5_fak_premarket | SOL Up or Down 5m Down 5 bps Optimized Average Premarket |
| 26 | Base | Down | 6 | b7c50005-0000-4000-8218-000000000106 | sol_up_down_5m_down_optimized_average_bps_6_fak_premarket | SOL Up or Down 5m Down 6 bps Optimized Average Premarket |
| 27 | Base | Down | 7 | b7c50005-0000-4000-8218-000000000107 | sol_up_down_5m_down_optimized_average_bps_7_fak_premarket | SOL Up or Down 5m Down 7 bps Optimized Average Premarket |
| 28 | Base | Down | 8 | b7c50005-0000-4000-8218-000000000108 | sol_up_down_5m_down_optimized_average_bps_8_fak_premarket | SOL Up or Down 5m Down 8 bps Optimized Average Premarket |
| 29 | Base | Down | 9 | b7c50005-0000-4000-8218-000000000109 | sol_up_down_5m_down_optimized_average_bps_9_fak_premarket | SOL Up or Down 5m Down 9 bps Optimized Average Premarket |
| 30 | Base | Down | 10 | b7c50005-0000-4000-8218-000000000110 | sol_up_down_5m_down_optimized_average_bps_10_fak_premarket | SOL Up or Down 5m Down 10 bps Optimized Average Premarket |
| 31 | LowerEnter clone | Down | 1 | b7c50005-0001-4000-8218-000000000101 | sol_up_down_5m_down_optimized_average_bps_1_fak_lower_enter_premarket | SOL Up or Down 5m Down 1 bps Optimized Average LowerEnter Premarket |
| 32 | LowerEnter clone | Down | 2 | b7c50005-0001-4000-8218-000000000102 | sol_up_down_5m_down_optimized_average_bps_2_fak_lower_enter_premarket | SOL Up or Down 5m Down 2 bps Optimized Average LowerEnter Premarket |
| 33 | LowerEnter clone | Down | 3 | b7c50005-0001-4000-8218-000000000103 | sol_up_down_5m_down_optimized_average_bps_3_fak_lower_enter_premarket | SOL Up or Down 5m Down 3 bps Optimized Average LowerEnter Premarket |
| 34 | LowerEnter clone | Down | 4 | b7c50005-0001-4000-8218-000000000104 | sol_up_down_5m_down_optimized_average_bps_4_fak_lower_enter_premarket | SOL Up or Down 5m Down 4 bps Optimized Average LowerEnter Premarket |
| 35 | LowerEnter clone | Down | 5 | b7c50005-0001-4000-8218-000000000105 | sol_up_down_5m_down_optimized_average_bps_5_fak_lower_enter_premarket | SOL Up or Down 5m Down 5 bps Optimized Average LowerEnter Premarket |
| 36 | LowerEnter clone | Down | 6 | b7c50005-0001-4000-8218-000000000106 | sol_up_down_5m_down_optimized_average_bps_6_fak_lower_enter_premarket | SOL Up or Down 5m Down 6 bps Optimized Average LowerEnter Premarket |
| 37 | LowerEnter clone | Down | 7 | b7c50005-0001-4000-8218-000000000107 | sol_up_down_5m_down_optimized_average_bps_7_fak_lower_enter_premarket | SOL Up or Down 5m Down 7 bps Optimized Average LowerEnter Premarket |
| 38 | LowerEnter clone | Down | 8 | b7c50005-0001-4000-8218-000000000108 | sol_up_down_5m_down_optimized_average_bps_8_fak_lower_enter_premarket | SOL Up or Down 5m Down 8 bps Optimized Average LowerEnter Premarket |
| 39 | LowerEnter clone | Down | 9 | b7c50005-0001-4000-8218-000000000109 | sol_up_down_5m_down_optimized_average_bps_9_fak_lower_enter_premarket | SOL Up or Down 5m Down 9 bps Optimized Average LowerEnter Premarket |
| 40 | LowerEnter clone | Down | 10 | b7c50005-0001-4000-8218-000000000110 | sol_up_down_5m_down_optimized_average_bps_10_fak_lower_enter_premarket | SOL Up or Down 5m Down 10 bps Optimized Average LowerEnter Premarket |
| 41 | Base | Neutral | 1 | b7c50005-0000-4000-8222-000000000101 | sol_up_down_5m_optimized_average_bps_1_fak_premarket | SOL Up or Down 5m 1 bps Optimized Average Premarket |
| 42 | Base | Neutral | 2 | b7c50005-0000-4000-8222-000000000102 | sol_up_down_5m_optimized_average_bps_2_fak_premarket | SOL Up or Down 5m 2 bps Optimized Average Premarket |
| 43 | Base | Neutral | 3 | b7c50005-0000-4000-8222-000000000103 | sol_up_down_5m_optimized_average_bps_3_fak_premarket | SOL Up or Down 5m 3 bps Optimized Average Premarket |
| 44 | Base | Neutral | 4 | b7c50005-0000-4000-8222-000000000104 | sol_up_down_5m_optimized_average_bps_4_fak_premarket | SOL Up or Down 5m 4 bps Optimized Average Premarket |
| 45 | Base | Neutral | 5 | b7c50005-0000-4000-8222-000000000105 | sol_up_down_5m_optimized_average_bps_5_fak_premarket | SOL Up or Down 5m 5 bps Optimized Average Premarket |
| 46 | Base | Neutral | 6 | b7c50005-0000-4000-8222-000000000106 | sol_up_down_5m_optimized_average_bps_6_fak_premarket | SOL Up or Down 5m 6 bps Optimized Average Premarket |
| 47 | Base | Neutral | 7 | b7c50005-0000-4000-8222-000000000107 | sol_up_down_5m_optimized_average_bps_7_fak_premarket | SOL Up or Down 5m 7 bps Optimized Average Premarket |
| 48 | Base | Neutral | 8 | b7c50005-0000-4000-8222-000000000108 | sol_up_down_5m_optimized_average_bps_8_fak_premarket | SOL Up or Down 5m 8 bps Optimized Average Premarket |
| 49 | Base | Neutral | 9 | b7c50005-0000-4000-8222-000000000109 | sol_up_down_5m_optimized_average_bps_9_fak_premarket | SOL Up or Down 5m 9 bps Optimized Average Premarket |
| 50 | Base | Neutral | 10 | b7c50005-0000-4000-8222-000000000110 | sol_up_down_5m_optimized_average_bps_10_fak_premarket | SOL Up or Down 5m 10 bps Optimized Average Premarket |
| 51 | LowerEnter clone | Neutral | 1 | b7c50005-0001-4000-8222-000000000101 | sol_up_down_5m_optimized_average_bps_1_fak_lower_enter_premarket | SOL Up or Down 5m 1 bps Optimized Average LowerEnter Premarket |
| 52 | LowerEnter clone | Neutral | 2 | b7c50005-0001-4000-8222-000000000102 | sol_up_down_5m_optimized_average_bps_2_fak_lower_enter_premarket | SOL Up or Down 5m 2 bps Optimized Average LowerEnter Premarket |
| 53 | LowerEnter clone | Neutral | 3 | b7c50005-0001-4000-8222-000000000103 | sol_up_down_5m_optimized_average_bps_3_fak_lower_enter_premarket | SOL Up or Down 5m 3 bps Optimized Average LowerEnter Premarket |
| 54 | LowerEnter clone | Neutral | 4 | b7c50005-0001-4000-8222-000000000104 | sol_up_down_5m_optimized_average_bps_4_fak_lower_enter_premarket | SOL Up or Down 5m 4 bps Optimized Average LowerEnter Premarket |
| 55 | LowerEnter clone | Neutral | 5 | b7c50005-0001-4000-8222-000000000105 | sol_up_down_5m_optimized_average_bps_5_fak_lower_enter_premarket | SOL Up or Down 5m 5 bps Optimized Average LowerEnter Premarket |
| 56 | LowerEnter clone | Neutral | 6 | b7c50005-0001-4000-8222-000000000106 | sol_up_down_5m_optimized_average_bps_6_fak_lower_enter_premarket | SOL Up or Down 5m 6 bps Optimized Average LowerEnter Premarket |
| 57 | LowerEnter clone | Neutral | 7 | b7c50005-0001-4000-8222-000000000107 | sol_up_down_5m_optimized_average_bps_7_fak_lower_enter_premarket | SOL Up or Down 5m 7 bps Optimized Average LowerEnter Premarket |
| 58 | LowerEnter clone | Neutral | 8 | b7c50005-0001-4000-8222-000000000108 | sol_up_down_5m_optimized_average_bps_8_fak_lower_enter_premarket | SOL Up or Down 5m 8 bps Optimized Average LowerEnter Premarket |
| 59 | LowerEnter clone | Neutral | 9 | b7c50005-0001-4000-8222-000000000109 | sol_up_down_5m_optimized_average_bps_9_fak_lower_enter_premarket | SOL Up or Down 5m 9 bps Optimized Average LowerEnter Premarket |
| 60 | LowerEnter clone | Neutral | 10 | b7c50005-0001-4000-8222-000000000110 | sol_up_down_5m_optimized_average_bps_10_fak_lower_enter_premarket | SOL Up or Down 5m 10 bps Optimized Average LowerEnter Premarket |

### Direct: Native LowEnter Reference Average — 28 variants

Uses the same neutral envelope signal directly, then applies the Paper-only average-fill-price cap.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Native LowEnter | Neutral | 1 | b7c50005-0000-4000-8215-000000000101 | sol_up_down_5m_low_enter_average_bps_1_fak_premarket | SOL Up or Down 5m 1 bps LowEnter Average Premarket |
| 2 | Native LowEnter | Neutral | 2 | b7c50005-0000-4000-8215-000000000102 | sol_up_down_5m_low_enter_average_bps_2_fak_premarket | SOL Up or Down 5m 2 bps LowEnter Average Premarket |
| 3 | Native LowEnter | Neutral | 3 | b7c50005-0000-4000-8215-000000000103 | sol_up_down_5m_low_enter_average_bps_3_fak_premarket | SOL Up or Down 5m 3 bps LowEnter Average Premarket |
| 4 | Native LowEnter | Neutral | 4 | b7c50005-0000-4000-8215-000000000104 | sol_up_down_5m_low_enter_average_bps_4_fak_premarket | SOL Up or Down 5m 4 bps LowEnter Average Premarket |
| 5 | Native LowEnter | Neutral | 5 | b7c50005-0000-4000-8215-000000000105 | sol_up_down_5m_low_enter_average_bps_5_fak_premarket | SOL Up or Down 5m 5 bps LowEnter Average Premarket |
| 6 | Native LowEnter | Neutral | 6 | b7c50005-0000-4000-8215-000000000106 | sol_up_down_5m_low_enter_average_bps_6_fak_premarket | SOL Up or Down 5m 6 bps LowEnter Average Premarket |
| 7 | Native LowEnter | Neutral | 7 | b7c50005-0000-4000-8215-000000000107 | sol_up_down_5m_low_enter_average_bps_7_fak_premarket | SOL Up or Down 5m 7 bps LowEnter Average Premarket |
| 8 | Native LowEnter | Neutral | 8 | b7c50005-0000-4000-8215-000000000108 | sol_up_down_5m_low_enter_average_bps_8_fak_premarket | SOL Up or Down 5m 8 bps LowEnter Average Premarket |
| 9 | Native LowEnter | Neutral | 9 | b7c50005-0000-4000-8215-000000000109 | sol_up_down_5m_low_enter_average_bps_9_fak_premarket | SOL Up or Down 5m 9 bps LowEnter Average Premarket |
| 10 | Native LowEnter | Neutral | 10 | b7c50005-0000-4000-8215-000000000110 | sol_up_down_5m_low_enter_average_bps_10_fak_premarket | SOL Up or Down 5m 10 bps LowEnter Average Premarket |
| 11 | Native LowEnter | Neutral | 15 | b7c50005-0000-4000-8215-000000000115 | sol_up_down_5m_low_enter_average_bps_15_fak_premarket | SOL Up or Down 5m 15 bps LowEnter Average Premarket |
| 12 | Native LowEnter | Neutral | 20 | b7c50005-0000-4000-8215-000000000120 | sol_up_down_5m_low_enter_average_bps_20_fak_premarket | SOL Up or Down 5m 20 bps LowEnter Average Premarket |
| 13 | Native LowEnter | Neutral | 25 | b7c50005-0000-4000-8215-000000000125 | sol_up_down_5m_low_enter_average_bps_25_fak_premarket | SOL Up or Down 5m 25 bps LowEnter Average Premarket |
| 14 | Native LowEnter | Neutral | 30 | b7c50005-0000-4000-8215-000000000130 | sol_up_down_5m_low_enter_average_bps_30_fak_premarket | SOL Up or Down 5m 30 bps LowEnter Average Premarket |
| 15 | Native LowEnter | Neutral | 35 | b7c50005-0000-4000-8215-000000000135 | sol_up_down_5m_low_enter_average_bps_35_fak_premarket | SOL Up or Down 5m 35 bps LowEnter Average Premarket |
| 16 | Native LowEnter | Neutral | 40 | b7c50005-0000-4000-8215-000000000140 | sol_up_down_5m_low_enter_average_bps_40_fak_premarket | SOL Up or Down 5m 40 bps LowEnter Average Premarket |
| 17 | Native LowEnter | Neutral | 45 | b7c50005-0000-4000-8215-000000000145 | sol_up_down_5m_low_enter_average_bps_45_fak_premarket | SOL Up or Down 5m 45 bps LowEnter Average Premarket |
| 18 | Native LowEnter | Neutral | 50 | b7c50005-0000-4000-8215-000000000150 | sol_up_down_5m_low_enter_average_bps_50_fak_premarket | SOL Up or Down 5m 50 bps LowEnter Average Premarket |
| 19 | Native LowEnter | Neutral | 55 | b7c50005-0000-4000-8215-000000000155 | sol_up_down_5m_low_enter_average_bps_55_fak_premarket | SOL Up or Down 5m 55 bps LowEnter Average Premarket |
| 20 | Native LowEnter | Neutral | 60 | b7c50005-0000-4000-8215-000000000160 | sol_up_down_5m_low_enter_average_bps_60_fak_premarket | SOL Up or Down 5m 60 bps LowEnter Average Premarket |
| 21 | Native LowEnter | Neutral | 65 | b7c50005-0000-4000-8215-000000000165 | sol_up_down_5m_low_enter_average_bps_65_fak_premarket | SOL Up or Down 5m 65 bps LowEnter Average Premarket |
| 22 | Native LowEnter | Neutral | 70 | b7c50005-0000-4000-8215-000000000170 | sol_up_down_5m_low_enter_average_bps_70_fak_premarket | SOL Up or Down 5m 70 bps LowEnter Average Premarket |
| 23 | Native LowEnter | Neutral | 75 | b7c50005-0000-4000-8215-000000000175 | sol_up_down_5m_low_enter_average_bps_75_fak_premarket | SOL Up or Down 5m 75 bps LowEnter Average Premarket |
| 24 | Native LowEnter | Neutral | 80 | b7c50005-0000-4000-8215-000000000180 | sol_up_down_5m_low_enter_average_bps_80_fak_premarket | SOL Up or Down 5m 80 bps LowEnter Average Premarket |
| 25 | Native LowEnter | Neutral | 85 | b7c50005-0000-4000-8215-000000000185 | sol_up_down_5m_low_enter_average_bps_85_fak_premarket | SOL Up or Down 5m 85 bps LowEnter Average Premarket |
| 26 | Native LowEnter | Neutral | 90 | b7c50005-0000-4000-8215-000000000190 | sol_up_down_5m_low_enter_average_bps_90_fak_premarket | SOL Up or Down 5m 90 bps LowEnter Average Premarket |
| 27 | Native LowEnter | Neutral | 95 | b7c50005-0000-4000-8215-000000000195 | sol_up_down_5m_low_enter_average_bps_95_fak_premarket | SOL Up or Down 5m 95 bps LowEnter Average Premarket |
| 28 | Native LowEnter | Neutral | 100 | b7c50005-0000-4000-8215-000000000200 | sol_up_down_5m_low_enter_average_bps_100_fak_premarket | SOL Up or Down 5m 100 bps LowEnter Average Premarket |

### Indirect: Bps Confirmed Average — 28 variants

Recomputes a linked neutral Reference Average base signal at the same Bps threshold, then requires agreement with a Diff Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8202-000000000101 | sol_up_down_5m_1_bps_confirmed_average_premarket | SOL Up or Down 5m 1 bps Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8202-000000000102 | sol_up_down_5m_2_bps_confirmed_average_premarket | SOL Up or Down 5m 2 bps Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8202-000000000103 | sol_up_down_5m_3_bps_confirmed_average_premarket | SOL Up or Down 5m 3 bps Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8202-000000000104 | sol_up_down_5m_4_bps_confirmed_average_premarket | SOL Up or Down 5m 4 bps Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8202-000000000105 | sol_up_down_5m_5_bps_confirmed_average_premarket | SOL Up or Down 5m 5 bps Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8202-000000000106 | sol_up_down_5m_6_bps_confirmed_average_premarket | SOL Up or Down 5m 6 bps Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8202-000000000107 | sol_up_down_5m_7_bps_confirmed_average_premarket | SOL Up or Down 5m 7 bps Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8202-000000000108 | sol_up_down_5m_8_bps_confirmed_average_premarket | SOL Up or Down 5m 8 bps Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8202-000000000109 | sol_up_down_5m_9_bps_confirmed_average_premarket | SOL Up or Down 5m 9 bps Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8202-000000000110 | sol_up_down_5m_10_bps_confirmed_average_premarket | SOL Up or Down 5m 10 bps Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8202-000000000115 | sol_up_down_5m_15_bps_confirmed_average_premarket | SOL Up or Down 5m 15 bps Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8202-000000000120 | sol_up_down_5m_20_bps_confirmed_average_premarket | SOL Up or Down 5m 20 bps Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8202-000000000125 | sol_up_down_5m_25_bps_confirmed_average_premarket | SOL Up or Down 5m 25 bps Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8202-000000000130 | sol_up_down_5m_30_bps_confirmed_average_premarket | SOL Up or Down 5m 30 bps Confirmed Average Premarket |
| 15 | Base | Composite | 35 | b7c50005-0000-4000-8202-000000000135 | sol_up_down_5m_35_bps_confirmed_average_premarket | SOL Up or Down 5m 35 bps Confirmed Average Premarket |
| 16 | Base | Composite | 40 | b7c50005-0000-4000-8202-000000000140 | sol_up_down_5m_40_bps_confirmed_average_premarket | SOL Up or Down 5m 40 bps Confirmed Average Premarket |
| 17 | Base | Composite | 45 | b7c50005-0000-4000-8202-000000000145 | sol_up_down_5m_45_bps_confirmed_average_premarket | SOL Up or Down 5m 45 bps Confirmed Average Premarket |
| 18 | Base | Composite | 50 | b7c50005-0000-4000-8202-000000000150 | sol_up_down_5m_50_bps_confirmed_average_premarket | SOL Up or Down 5m 50 bps Confirmed Average Premarket |
| 19 | Base | Composite | 55 | b7c50005-0000-4000-8202-000000000155 | sol_up_down_5m_55_bps_confirmed_average_premarket | SOL Up or Down 5m 55 bps Confirmed Average Premarket |
| 20 | Base | Composite | 60 | b7c50005-0000-4000-8202-000000000160 | sol_up_down_5m_60_bps_confirmed_average_premarket | SOL Up or Down 5m 60 bps Confirmed Average Premarket |
| 21 | Base | Composite | 65 | b7c50005-0000-4000-8202-000000000165 | sol_up_down_5m_65_bps_confirmed_average_premarket | SOL Up or Down 5m 65 bps Confirmed Average Premarket |
| 22 | Base | Composite | 70 | b7c50005-0000-4000-8202-000000000170 | sol_up_down_5m_70_bps_confirmed_average_premarket | SOL Up or Down 5m 70 bps Confirmed Average Premarket |
| 23 | Base | Composite | 75 | b7c50005-0000-4000-8202-000000000175 | sol_up_down_5m_75_bps_confirmed_average_premarket | SOL Up or Down 5m 75 bps Confirmed Average Premarket |
| 24 | Base | Composite | 80 | b7c50005-0000-4000-8202-000000000180 | sol_up_down_5m_80_bps_confirmed_average_premarket | SOL Up or Down 5m 80 bps Confirmed Average Premarket |
| 25 | Base | Composite | 85 | b7c50005-0000-4000-8202-000000000185 | sol_up_down_5m_85_bps_confirmed_average_premarket | SOL Up or Down 5m 85 bps Confirmed Average Premarket |
| 26 | Base | Composite | 90 | b7c50005-0000-4000-8202-000000000190 | sol_up_down_5m_90_bps_confirmed_average_premarket | SOL Up or Down 5m 90 bps Confirmed Average Premarket |
| 27 | Base | Composite | 95 | b7c50005-0000-4000-8202-000000000195 | sol_up_down_5m_95_bps_confirmed_average_premarket | SOL Up or Down 5m 95 bps Confirmed Average Premarket |
| 28 | Base | Composite | 100 | b7c50005-0000-4000-8202-000000000200 | sol_up_down_5m_100_bps_confirmed_average_premarket | SOL Up or Down 5m 100 bps Confirmed Average Premarket |

### Indirect: Diff Confirmed Average — 14 variants

Uses Diff Reference Average as its base signal and recomputes a linked neutral price Reference Average confirmation signal.

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Base | Composite | 1 | b7c50005-0000-4000-8205-000000000001 | sol_up_down_5m_1_diff_confirmed_average_premarket | SOL Up or Down 5m 1 Diff Confirmed Average Premarket |
| 2 | Base | Composite | 2 | b7c50005-0000-4000-8205-000000000002 | sol_up_down_5m_2_diff_confirmed_average_premarket | SOL Up or Down 5m 2 Diff Confirmed Average Premarket |
| 3 | Base | Composite | 3 | b7c50005-0000-4000-8205-000000000003 | sol_up_down_5m_3_diff_confirmed_average_premarket | SOL Up or Down 5m 3 Diff Confirmed Average Premarket |
| 4 | Base | Composite | 4 | b7c50005-0000-4000-8205-000000000004 | sol_up_down_5m_4_diff_confirmed_average_premarket | SOL Up or Down 5m 4 Diff Confirmed Average Premarket |
| 5 | Base | Composite | 5 | b7c50005-0000-4000-8205-000000000005 | sol_up_down_5m_5_diff_confirmed_average_premarket | SOL Up or Down 5m 5 Diff Confirmed Average Premarket |
| 6 | Base | Composite | 6 | b7c50005-0000-4000-8205-000000000006 | sol_up_down_5m_6_diff_confirmed_average_premarket | SOL Up or Down 5m 6 Diff Confirmed Average Premarket |
| 7 | Base | Composite | 7 | b7c50005-0000-4000-8205-000000000007 | sol_up_down_5m_7_diff_confirmed_average_premarket | SOL Up or Down 5m 7 Diff Confirmed Average Premarket |
| 8 | Base | Composite | 8 | b7c50005-0000-4000-8205-000000000008 | sol_up_down_5m_8_diff_confirmed_average_premarket | SOL Up or Down 5m 8 Diff Confirmed Average Premarket |
| 9 | Base | Composite | 9 | b7c50005-0000-4000-8205-000000000009 | sol_up_down_5m_9_diff_confirmed_average_premarket | SOL Up or Down 5m 9 Diff Confirmed Average Premarket |
| 10 | Base | Composite | 10 | b7c50005-0000-4000-8205-000000000010 | sol_up_down_5m_10_diff_confirmed_average_premarket | SOL Up or Down 5m 10 Diff Confirmed Average Premarket |
| 11 | Base | Composite | 15 | b7c50005-0000-4000-8205-000000000015 | sol_up_down_5m_15_diff_confirmed_average_premarket | SOL Up or Down 5m 15 Diff Confirmed Average Premarket |
| 12 | Base | Composite | 20 | b7c50005-0000-4000-8205-000000000020 | sol_up_down_5m_20_diff_confirmed_average_premarket | SOL Up or Down 5m 20 Diff Confirmed Average Premarket |
| 13 | Base | Composite | 25 | b7c50005-0000-4000-8205-000000000025 | sol_up_down_5m_25_diff_confirmed_average_premarket | SOL Up or Down 5m 25 Diff Confirmed Average Premarket |
| 14 | Base | Composite | 30 | b7c50005-0000-4000-8205-000000000030 | sol_up_down_5m_30_diff_confirmed_average_premarket | SOL Up or Down 5m 30 Diff Confirmed Average Premarket |

## Conditional downstream: ChildMirror — 247 variants

These variants are not part of the 848 static signal surface: they neither call the Reference Average selector nor hold a fixed Reference Average strategy link. They are conditionally downstream because the child-parent refresh can dynamically select an affected parent of the same asset, after which the runtime copies each accepted parent Paper entry with the same market, outcome, notional, and share size.

Parent selection excludes Child, Futures, Paper-only, non-5m, disabled, and paused strategies. Therefore Optimized, native LowEnter, and all LowerEnter clones cannot become Child parents. Before runtime active/paused and performance gates, the structurally eligible affected parent set contains 378 variants: 126 per asset, comprising 84 ordinary base Reference Average variants, 28 base BpsConfirmed variants, and 14 base DiffConfirmed variants. All have names without Progress, so both ordinary and Progress Child modes can conditionally select them when their PnL/ROI eligibility gates pass.

This category is intentionally conditional. A Child changes only when its current dynamic assignment resolves to one of those affected parents and that parent's accepted entry changes under Max/Min. Assignment to an unrelated parent, no eligible positive-performance parent, a disabled/paused child, or failure of ROI sample gates means no downstream change.

Catalog formula:

- ID: b7c50005-0000-4000-{group:0000}-{lookbackHours:000000000000}.
- Code: {assetLower}_up_down_5m_{lookbackHours}_{child|child_progress|child_roi|child_progress_roi}.
- Name: {ASSET} Up or Down 5m {lookbackHours} {Child|Child Progress|Child ROI|Child Progress ROI}.

| Asset | Mode | ID group | Registered lookback hours | Count |
|---|---|---:|---|---:|
| BTC | Child | 8185 | 1-24 | 24 |
| BTC | Child Progress | 8188 | 1-24 | 24 |
| BTC | Child ROI | 8194 | 1-24 | 24 |
| BTC | Child Progress ROI | 8197 | 1-24 | 24 |
| ETH | Child | 8186 | 1-24 | 24 |
| ETH | Child Progress | 8189 | 7, 12, 15, 16, 17, 18, 20, 22, 23 | 9 |
| ETH | Child ROI | 8195 | 1-24 | 24 |
| ETH | Child Progress ROI | 8198 | 1, 2, 4, 6, 10, 20 | 6 |
| SOL | Child | 8187 | 1-24 | 24 |
| SOL | Child Progress | 8190 | 1-24 | 24 |
| SOL | Child ROI | 8196 | 1-24 | 24 |
| SOL | Child Progress ROI | 8199 | 1, 2, 3, 7, 8, 9, 10, 11, 12, 15, 16, 17, 18, 20, 22, 24 | 16 |

### BTC conditional ChildMirror — 96 variants

#### BTC Child — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child | 1 | b7c50005-0000-4000-8185-000000000001 | btc_up_down_5m_1_child | BTC Up or Down 5m 1 Child |
| 2 | Conditional downstream | Child | 2 | b7c50005-0000-4000-8185-000000000002 | btc_up_down_5m_2_child | BTC Up or Down 5m 2 Child |
| 3 | Conditional downstream | Child | 3 | b7c50005-0000-4000-8185-000000000003 | btc_up_down_5m_3_child | BTC Up or Down 5m 3 Child |
| 4 | Conditional downstream | Child | 4 | b7c50005-0000-4000-8185-000000000004 | btc_up_down_5m_4_child | BTC Up or Down 5m 4 Child |
| 5 | Conditional downstream | Child | 5 | b7c50005-0000-4000-8185-000000000005 | btc_up_down_5m_5_child | BTC Up or Down 5m 5 Child |
| 6 | Conditional downstream | Child | 6 | b7c50005-0000-4000-8185-000000000006 | btc_up_down_5m_6_child | BTC Up or Down 5m 6 Child |
| 7 | Conditional downstream | Child | 7 | b7c50005-0000-4000-8185-000000000007 | btc_up_down_5m_7_child | BTC Up or Down 5m 7 Child |
| 8 | Conditional downstream | Child | 8 | b7c50005-0000-4000-8185-000000000008 | btc_up_down_5m_8_child | BTC Up or Down 5m 8 Child |
| 9 | Conditional downstream | Child | 9 | b7c50005-0000-4000-8185-000000000009 | btc_up_down_5m_9_child | BTC Up or Down 5m 9 Child |
| 10 | Conditional downstream | Child | 10 | b7c50005-0000-4000-8185-000000000010 | btc_up_down_5m_10_child | BTC Up or Down 5m 10 Child |
| 11 | Conditional downstream | Child | 11 | b7c50005-0000-4000-8185-000000000011 | btc_up_down_5m_11_child | BTC Up or Down 5m 11 Child |
| 12 | Conditional downstream | Child | 12 | b7c50005-0000-4000-8185-000000000012 | btc_up_down_5m_12_child | BTC Up or Down 5m 12 Child |
| 13 | Conditional downstream | Child | 13 | b7c50005-0000-4000-8185-000000000013 | btc_up_down_5m_13_child | BTC Up or Down 5m 13 Child |
| 14 | Conditional downstream | Child | 14 | b7c50005-0000-4000-8185-000000000014 | btc_up_down_5m_14_child | BTC Up or Down 5m 14 Child |
| 15 | Conditional downstream | Child | 15 | b7c50005-0000-4000-8185-000000000015 | btc_up_down_5m_15_child | BTC Up or Down 5m 15 Child |
| 16 | Conditional downstream | Child | 16 | b7c50005-0000-4000-8185-000000000016 | btc_up_down_5m_16_child | BTC Up or Down 5m 16 Child |
| 17 | Conditional downstream | Child | 17 | b7c50005-0000-4000-8185-000000000017 | btc_up_down_5m_17_child | BTC Up or Down 5m 17 Child |
| 18 | Conditional downstream | Child | 18 | b7c50005-0000-4000-8185-000000000018 | btc_up_down_5m_18_child | BTC Up or Down 5m 18 Child |
| 19 | Conditional downstream | Child | 19 | b7c50005-0000-4000-8185-000000000019 | btc_up_down_5m_19_child | BTC Up or Down 5m 19 Child |
| 20 | Conditional downstream | Child | 20 | b7c50005-0000-4000-8185-000000000020 | btc_up_down_5m_20_child | BTC Up or Down 5m 20 Child |
| 21 | Conditional downstream | Child | 21 | b7c50005-0000-4000-8185-000000000021 | btc_up_down_5m_21_child | BTC Up or Down 5m 21 Child |
| 22 | Conditional downstream | Child | 22 | b7c50005-0000-4000-8185-000000000022 | btc_up_down_5m_22_child | BTC Up or Down 5m 22 Child |
| 23 | Conditional downstream | Child | 23 | b7c50005-0000-4000-8185-000000000023 | btc_up_down_5m_23_child | BTC Up or Down 5m 23 Child |
| 24 | Conditional downstream | Child | 24 | b7c50005-0000-4000-8185-000000000024 | btc_up_down_5m_24_child | BTC Up or Down 5m 24 Child |

#### BTC Child Progress — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress | 1 | b7c50005-0000-4000-8188-000000000001 | btc_up_down_5m_1_child_progress | BTC Up or Down 5m 1 Child Progress |
| 2 | Conditional downstream | Child Progress | 2 | b7c50005-0000-4000-8188-000000000002 | btc_up_down_5m_2_child_progress | BTC Up or Down 5m 2 Child Progress |
| 3 | Conditional downstream | Child Progress | 3 | b7c50005-0000-4000-8188-000000000003 | btc_up_down_5m_3_child_progress | BTC Up or Down 5m 3 Child Progress |
| 4 | Conditional downstream | Child Progress | 4 | b7c50005-0000-4000-8188-000000000004 | btc_up_down_5m_4_child_progress | BTC Up or Down 5m 4 Child Progress |
| 5 | Conditional downstream | Child Progress | 5 | b7c50005-0000-4000-8188-000000000005 | btc_up_down_5m_5_child_progress | BTC Up or Down 5m 5 Child Progress |
| 6 | Conditional downstream | Child Progress | 6 | b7c50005-0000-4000-8188-000000000006 | btc_up_down_5m_6_child_progress | BTC Up or Down 5m 6 Child Progress |
| 7 | Conditional downstream | Child Progress | 7 | b7c50005-0000-4000-8188-000000000007 | btc_up_down_5m_7_child_progress | BTC Up or Down 5m 7 Child Progress |
| 8 | Conditional downstream | Child Progress | 8 | b7c50005-0000-4000-8188-000000000008 | btc_up_down_5m_8_child_progress | BTC Up or Down 5m 8 Child Progress |
| 9 | Conditional downstream | Child Progress | 9 | b7c50005-0000-4000-8188-000000000009 | btc_up_down_5m_9_child_progress | BTC Up or Down 5m 9 Child Progress |
| 10 | Conditional downstream | Child Progress | 10 | b7c50005-0000-4000-8188-000000000010 | btc_up_down_5m_10_child_progress | BTC Up or Down 5m 10 Child Progress |
| 11 | Conditional downstream | Child Progress | 11 | b7c50005-0000-4000-8188-000000000011 | btc_up_down_5m_11_child_progress | BTC Up or Down 5m 11 Child Progress |
| 12 | Conditional downstream | Child Progress | 12 | b7c50005-0000-4000-8188-000000000012 | btc_up_down_5m_12_child_progress | BTC Up or Down 5m 12 Child Progress |
| 13 | Conditional downstream | Child Progress | 13 | b7c50005-0000-4000-8188-000000000013 | btc_up_down_5m_13_child_progress | BTC Up or Down 5m 13 Child Progress |
| 14 | Conditional downstream | Child Progress | 14 | b7c50005-0000-4000-8188-000000000014 | btc_up_down_5m_14_child_progress | BTC Up or Down 5m 14 Child Progress |
| 15 | Conditional downstream | Child Progress | 15 | b7c50005-0000-4000-8188-000000000015 | btc_up_down_5m_15_child_progress | BTC Up or Down 5m 15 Child Progress |
| 16 | Conditional downstream | Child Progress | 16 | b7c50005-0000-4000-8188-000000000016 | btc_up_down_5m_16_child_progress | BTC Up or Down 5m 16 Child Progress |
| 17 | Conditional downstream | Child Progress | 17 | b7c50005-0000-4000-8188-000000000017 | btc_up_down_5m_17_child_progress | BTC Up or Down 5m 17 Child Progress |
| 18 | Conditional downstream | Child Progress | 18 | b7c50005-0000-4000-8188-000000000018 | btc_up_down_5m_18_child_progress | BTC Up or Down 5m 18 Child Progress |
| 19 | Conditional downstream | Child Progress | 19 | b7c50005-0000-4000-8188-000000000019 | btc_up_down_5m_19_child_progress | BTC Up or Down 5m 19 Child Progress |
| 20 | Conditional downstream | Child Progress | 20 | b7c50005-0000-4000-8188-000000000020 | btc_up_down_5m_20_child_progress | BTC Up or Down 5m 20 Child Progress |
| 21 | Conditional downstream | Child Progress | 21 | b7c50005-0000-4000-8188-000000000021 | btc_up_down_5m_21_child_progress | BTC Up or Down 5m 21 Child Progress |
| 22 | Conditional downstream | Child Progress | 22 | b7c50005-0000-4000-8188-000000000022 | btc_up_down_5m_22_child_progress | BTC Up or Down 5m 22 Child Progress |
| 23 | Conditional downstream | Child Progress | 23 | b7c50005-0000-4000-8188-000000000023 | btc_up_down_5m_23_child_progress | BTC Up or Down 5m 23 Child Progress |
| 24 | Conditional downstream | Child Progress | 24 | b7c50005-0000-4000-8188-000000000024 | btc_up_down_5m_24_child_progress | BTC Up or Down 5m 24 Child Progress |

#### BTC Child ROI — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child ROI | 1 | b7c50005-0000-4000-8194-000000000001 | btc_up_down_5m_1_child_roi | BTC Up or Down 5m 1 Child ROI |
| 2 | Conditional downstream | Child ROI | 2 | b7c50005-0000-4000-8194-000000000002 | btc_up_down_5m_2_child_roi | BTC Up or Down 5m 2 Child ROI |
| 3 | Conditional downstream | Child ROI | 3 | b7c50005-0000-4000-8194-000000000003 | btc_up_down_5m_3_child_roi | BTC Up or Down 5m 3 Child ROI |
| 4 | Conditional downstream | Child ROI | 4 | b7c50005-0000-4000-8194-000000000004 | btc_up_down_5m_4_child_roi | BTC Up or Down 5m 4 Child ROI |
| 5 | Conditional downstream | Child ROI | 5 | b7c50005-0000-4000-8194-000000000005 | btc_up_down_5m_5_child_roi | BTC Up or Down 5m 5 Child ROI |
| 6 | Conditional downstream | Child ROI | 6 | b7c50005-0000-4000-8194-000000000006 | btc_up_down_5m_6_child_roi | BTC Up or Down 5m 6 Child ROI |
| 7 | Conditional downstream | Child ROI | 7 | b7c50005-0000-4000-8194-000000000007 | btc_up_down_5m_7_child_roi | BTC Up or Down 5m 7 Child ROI |
| 8 | Conditional downstream | Child ROI | 8 | b7c50005-0000-4000-8194-000000000008 | btc_up_down_5m_8_child_roi | BTC Up or Down 5m 8 Child ROI |
| 9 | Conditional downstream | Child ROI | 9 | b7c50005-0000-4000-8194-000000000009 | btc_up_down_5m_9_child_roi | BTC Up or Down 5m 9 Child ROI |
| 10 | Conditional downstream | Child ROI | 10 | b7c50005-0000-4000-8194-000000000010 | btc_up_down_5m_10_child_roi | BTC Up or Down 5m 10 Child ROI |
| 11 | Conditional downstream | Child ROI | 11 | b7c50005-0000-4000-8194-000000000011 | btc_up_down_5m_11_child_roi | BTC Up or Down 5m 11 Child ROI |
| 12 | Conditional downstream | Child ROI | 12 | b7c50005-0000-4000-8194-000000000012 | btc_up_down_5m_12_child_roi | BTC Up or Down 5m 12 Child ROI |
| 13 | Conditional downstream | Child ROI | 13 | b7c50005-0000-4000-8194-000000000013 | btc_up_down_5m_13_child_roi | BTC Up or Down 5m 13 Child ROI |
| 14 | Conditional downstream | Child ROI | 14 | b7c50005-0000-4000-8194-000000000014 | btc_up_down_5m_14_child_roi | BTC Up or Down 5m 14 Child ROI |
| 15 | Conditional downstream | Child ROI | 15 | b7c50005-0000-4000-8194-000000000015 | btc_up_down_5m_15_child_roi | BTC Up or Down 5m 15 Child ROI |
| 16 | Conditional downstream | Child ROI | 16 | b7c50005-0000-4000-8194-000000000016 | btc_up_down_5m_16_child_roi | BTC Up or Down 5m 16 Child ROI |
| 17 | Conditional downstream | Child ROI | 17 | b7c50005-0000-4000-8194-000000000017 | btc_up_down_5m_17_child_roi | BTC Up or Down 5m 17 Child ROI |
| 18 | Conditional downstream | Child ROI | 18 | b7c50005-0000-4000-8194-000000000018 | btc_up_down_5m_18_child_roi | BTC Up or Down 5m 18 Child ROI |
| 19 | Conditional downstream | Child ROI | 19 | b7c50005-0000-4000-8194-000000000019 | btc_up_down_5m_19_child_roi | BTC Up or Down 5m 19 Child ROI |
| 20 | Conditional downstream | Child ROI | 20 | b7c50005-0000-4000-8194-000000000020 | btc_up_down_5m_20_child_roi | BTC Up or Down 5m 20 Child ROI |
| 21 | Conditional downstream | Child ROI | 21 | b7c50005-0000-4000-8194-000000000021 | btc_up_down_5m_21_child_roi | BTC Up or Down 5m 21 Child ROI |
| 22 | Conditional downstream | Child ROI | 22 | b7c50005-0000-4000-8194-000000000022 | btc_up_down_5m_22_child_roi | BTC Up or Down 5m 22 Child ROI |
| 23 | Conditional downstream | Child ROI | 23 | b7c50005-0000-4000-8194-000000000023 | btc_up_down_5m_23_child_roi | BTC Up or Down 5m 23 Child ROI |
| 24 | Conditional downstream | Child ROI | 24 | b7c50005-0000-4000-8194-000000000024 | btc_up_down_5m_24_child_roi | BTC Up or Down 5m 24 Child ROI |

#### BTC Child Progress ROI — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress ROI | 1 | b7c50005-0000-4000-8197-000000000001 | btc_up_down_5m_1_child_progress_roi | BTC Up or Down 5m 1 Child Progress ROI |
| 2 | Conditional downstream | Child Progress ROI | 2 | b7c50005-0000-4000-8197-000000000002 | btc_up_down_5m_2_child_progress_roi | BTC Up or Down 5m 2 Child Progress ROI |
| 3 | Conditional downstream | Child Progress ROI | 3 | b7c50005-0000-4000-8197-000000000003 | btc_up_down_5m_3_child_progress_roi | BTC Up or Down 5m 3 Child Progress ROI |
| 4 | Conditional downstream | Child Progress ROI | 4 | b7c50005-0000-4000-8197-000000000004 | btc_up_down_5m_4_child_progress_roi | BTC Up or Down 5m 4 Child Progress ROI |
| 5 | Conditional downstream | Child Progress ROI | 5 | b7c50005-0000-4000-8197-000000000005 | btc_up_down_5m_5_child_progress_roi | BTC Up or Down 5m 5 Child Progress ROI |
| 6 | Conditional downstream | Child Progress ROI | 6 | b7c50005-0000-4000-8197-000000000006 | btc_up_down_5m_6_child_progress_roi | BTC Up or Down 5m 6 Child Progress ROI |
| 7 | Conditional downstream | Child Progress ROI | 7 | b7c50005-0000-4000-8197-000000000007 | btc_up_down_5m_7_child_progress_roi | BTC Up or Down 5m 7 Child Progress ROI |
| 8 | Conditional downstream | Child Progress ROI | 8 | b7c50005-0000-4000-8197-000000000008 | btc_up_down_5m_8_child_progress_roi | BTC Up or Down 5m 8 Child Progress ROI |
| 9 | Conditional downstream | Child Progress ROI | 9 | b7c50005-0000-4000-8197-000000000009 | btc_up_down_5m_9_child_progress_roi | BTC Up or Down 5m 9 Child Progress ROI |
| 10 | Conditional downstream | Child Progress ROI | 10 | b7c50005-0000-4000-8197-000000000010 | btc_up_down_5m_10_child_progress_roi | BTC Up or Down 5m 10 Child Progress ROI |
| 11 | Conditional downstream | Child Progress ROI | 11 | b7c50005-0000-4000-8197-000000000011 | btc_up_down_5m_11_child_progress_roi | BTC Up or Down 5m 11 Child Progress ROI |
| 12 | Conditional downstream | Child Progress ROI | 12 | b7c50005-0000-4000-8197-000000000012 | btc_up_down_5m_12_child_progress_roi | BTC Up or Down 5m 12 Child Progress ROI |
| 13 | Conditional downstream | Child Progress ROI | 13 | b7c50005-0000-4000-8197-000000000013 | btc_up_down_5m_13_child_progress_roi | BTC Up or Down 5m 13 Child Progress ROI |
| 14 | Conditional downstream | Child Progress ROI | 14 | b7c50005-0000-4000-8197-000000000014 | btc_up_down_5m_14_child_progress_roi | BTC Up or Down 5m 14 Child Progress ROI |
| 15 | Conditional downstream | Child Progress ROI | 15 | b7c50005-0000-4000-8197-000000000015 | btc_up_down_5m_15_child_progress_roi | BTC Up or Down 5m 15 Child Progress ROI |
| 16 | Conditional downstream | Child Progress ROI | 16 | b7c50005-0000-4000-8197-000000000016 | btc_up_down_5m_16_child_progress_roi | BTC Up or Down 5m 16 Child Progress ROI |
| 17 | Conditional downstream | Child Progress ROI | 17 | b7c50005-0000-4000-8197-000000000017 | btc_up_down_5m_17_child_progress_roi | BTC Up or Down 5m 17 Child Progress ROI |
| 18 | Conditional downstream | Child Progress ROI | 18 | b7c50005-0000-4000-8197-000000000018 | btc_up_down_5m_18_child_progress_roi | BTC Up or Down 5m 18 Child Progress ROI |
| 19 | Conditional downstream | Child Progress ROI | 19 | b7c50005-0000-4000-8197-000000000019 | btc_up_down_5m_19_child_progress_roi | BTC Up or Down 5m 19 Child Progress ROI |
| 20 | Conditional downstream | Child Progress ROI | 20 | b7c50005-0000-4000-8197-000000000020 | btc_up_down_5m_20_child_progress_roi | BTC Up or Down 5m 20 Child Progress ROI |
| 21 | Conditional downstream | Child Progress ROI | 21 | b7c50005-0000-4000-8197-000000000021 | btc_up_down_5m_21_child_progress_roi | BTC Up or Down 5m 21 Child Progress ROI |
| 22 | Conditional downstream | Child Progress ROI | 22 | b7c50005-0000-4000-8197-000000000022 | btc_up_down_5m_22_child_progress_roi | BTC Up or Down 5m 22 Child Progress ROI |
| 23 | Conditional downstream | Child Progress ROI | 23 | b7c50005-0000-4000-8197-000000000023 | btc_up_down_5m_23_child_progress_roi | BTC Up or Down 5m 23 Child Progress ROI |
| 24 | Conditional downstream | Child Progress ROI | 24 | b7c50005-0000-4000-8197-000000000024 | btc_up_down_5m_24_child_progress_roi | BTC Up or Down 5m 24 Child Progress ROI |

### ETH conditional ChildMirror — 63 variants

#### ETH Child — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child | 1 | b7c50005-0000-4000-8186-000000000001 | eth_up_down_5m_1_child | ETH Up or Down 5m 1 Child |
| 2 | Conditional downstream | Child | 2 | b7c50005-0000-4000-8186-000000000002 | eth_up_down_5m_2_child | ETH Up or Down 5m 2 Child |
| 3 | Conditional downstream | Child | 3 | b7c50005-0000-4000-8186-000000000003 | eth_up_down_5m_3_child | ETH Up or Down 5m 3 Child |
| 4 | Conditional downstream | Child | 4 | b7c50005-0000-4000-8186-000000000004 | eth_up_down_5m_4_child | ETH Up or Down 5m 4 Child |
| 5 | Conditional downstream | Child | 5 | b7c50005-0000-4000-8186-000000000005 | eth_up_down_5m_5_child | ETH Up or Down 5m 5 Child |
| 6 | Conditional downstream | Child | 6 | b7c50005-0000-4000-8186-000000000006 | eth_up_down_5m_6_child | ETH Up or Down 5m 6 Child |
| 7 | Conditional downstream | Child | 7 | b7c50005-0000-4000-8186-000000000007 | eth_up_down_5m_7_child | ETH Up or Down 5m 7 Child |
| 8 | Conditional downstream | Child | 8 | b7c50005-0000-4000-8186-000000000008 | eth_up_down_5m_8_child | ETH Up or Down 5m 8 Child |
| 9 | Conditional downstream | Child | 9 | b7c50005-0000-4000-8186-000000000009 | eth_up_down_5m_9_child | ETH Up or Down 5m 9 Child |
| 10 | Conditional downstream | Child | 10 | b7c50005-0000-4000-8186-000000000010 | eth_up_down_5m_10_child | ETH Up or Down 5m 10 Child |
| 11 | Conditional downstream | Child | 11 | b7c50005-0000-4000-8186-000000000011 | eth_up_down_5m_11_child | ETH Up or Down 5m 11 Child |
| 12 | Conditional downstream | Child | 12 | b7c50005-0000-4000-8186-000000000012 | eth_up_down_5m_12_child | ETH Up or Down 5m 12 Child |
| 13 | Conditional downstream | Child | 13 | b7c50005-0000-4000-8186-000000000013 | eth_up_down_5m_13_child | ETH Up or Down 5m 13 Child |
| 14 | Conditional downstream | Child | 14 | b7c50005-0000-4000-8186-000000000014 | eth_up_down_5m_14_child | ETH Up or Down 5m 14 Child |
| 15 | Conditional downstream | Child | 15 | b7c50005-0000-4000-8186-000000000015 | eth_up_down_5m_15_child | ETH Up or Down 5m 15 Child |
| 16 | Conditional downstream | Child | 16 | b7c50005-0000-4000-8186-000000000016 | eth_up_down_5m_16_child | ETH Up or Down 5m 16 Child |
| 17 | Conditional downstream | Child | 17 | b7c50005-0000-4000-8186-000000000017 | eth_up_down_5m_17_child | ETH Up or Down 5m 17 Child |
| 18 | Conditional downstream | Child | 18 | b7c50005-0000-4000-8186-000000000018 | eth_up_down_5m_18_child | ETH Up or Down 5m 18 Child |
| 19 | Conditional downstream | Child | 19 | b7c50005-0000-4000-8186-000000000019 | eth_up_down_5m_19_child | ETH Up or Down 5m 19 Child |
| 20 | Conditional downstream | Child | 20 | b7c50005-0000-4000-8186-000000000020 | eth_up_down_5m_20_child | ETH Up or Down 5m 20 Child |
| 21 | Conditional downstream | Child | 21 | b7c50005-0000-4000-8186-000000000021 | eth_up_down_5m_21_child | ETH Up or Down 5m 21 Child |
| 22 | Conditional downstream | Child | 22 | b7c50005-0000-4000-8186-000000000022 | eth_up_down_5m_22_child | ETH Up or Down 5m 22 Child |
| 23 | Conditional downstream | Child | 23 | b7c50005-0000-4000-8186-000000000023 | eth_up_down_5m_23_child | ETH Up or Down 5m 23 Child |
| 24 | Conditional downstream | Child | 24 | b7c50005-0000-4000-8186-000000000024 | eth_up_down_5m_24_child | ETH Up or Down 5m 24 Child |

#### ETH Child Progress — 9

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress | 7 | b7c50005-0000-4000-8189-000000000007 | eth_up_down_5m_7_child_progress | ETH Up or Down 5m 7 Child Progress |
| 2 | Conditional downstream | Child Progress | 12 | b7c50005-0000-4000-8189-000000000012 | eth_up_down_5m_12_child_progress | ETH Up or Down 5m 12 Child Progress |
| 3 | Conditional downstream | Child Progress | 15 | b7c50005-0000-4000-8189-000000000015 | eth_up_down_5m_15_child_progress | ETH Up or Down 5m 15 Child Progress |
| 4 | Conditional downstream | Child Progress | 16 | b7c50005-0000-4000-8189-000000000016 | eth_up_down_5m_16_child_progress | ETH Up or Down 5m 16 Child Progress |
| 5 | Conditional downstream | Child Progress | 17 | b7c50005-0000-4000-8189-000000000017 | eth_up_down_5m_17_child_progress | ETH Up or Down 5m 17 Child Progress |
| 6 | Conditional downstream | Child Progress | 18 | b7c50005-0000-4000-8189-000000000018 | eth_up_down_5m_18_child_progress | ETH Up or Down 5m 18 Child Progress |
| 7 | Conditional downstream | Child Progress | 20 | b7c50005-0000-4000-8189-000000000020 | eth_up_down_5m_20_child_progress | ETH Up or Down 5m 20 Child Progress |
| 8 | Conditional downstream | Child Progress | 22 | b7c50005-0000-4000-8189-000000000022 | eth_up_down_5m_22_child_progress | ETH Up or Down 5m 22 Child Progress |
| 9 | Conditional downstream | Child Progress | 23 | b7c50005-0000-4000-8189-000000000023 | eth_up_down_5m_23_child_progress | ETH Up or Down 5m 23 Child Progress |

#### ETH Child ROI — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child ROI | 1 | b7c50005-0000-4000-8195-000000000001 | eth_up_down_5m_1_child_roi | ETH Up or Down 5m 1 Child ROI |
| 2 | Conditional downstream | Child ROI | 2 | b7c50005-0000-4000-8195-000000000002 | eth_up_down_5m_2_child_roi | ETH Up or Down 5m 2 Child ROI |
| 3 | Conditional downstream | Child ROI | 3 | b7c50005-0000-4000-8195-000000000003 | eth_up_down_5m_3_child_roi | ETH Up or Down 5m 3 Child ROI |
| 4 | Conditional downstream | Child ROI | 4 | b7c50005-0000-4000-8195-000000000004 | eth_up_down_5m_4_child_roi | ETH Up or Down 5m 4 Child ROI |
| 5 | Conditional downstream | Child ROI | 5 | b7c50005-0000-4000-8195-000000000005 | eth_up_down_5m_5_child_roi | ETH Up or Down 5m 5 Child ROI |
| 6 | Conditional downstream | Child ROI | 6 | b7c50005-0000-4000-8195-000000000006 | eth_up_down_5m_6_child_roi | ETH Up or Down 5m 6 Child ROI |
| 7 | Conditional downstream | Child ROI | 7 | b7c50005-0000-4000-8195-000000000007 | eth_up_down_5m_7_child_roi | ETH Up or Down 5m 7 Child ROI |
| 8 | Conditional downstream | Child ROI | 8 | b7c50005-0000-4000-8195-000000000008 | eth_up_down_5m_8_child_roi | ETH Up or Down 5m 8 Child ROI |
| 9 | Conditional downstream | Child ROI | 9 | b7c50005-0000-4000-8195-000000000009 | eth_up_down_5m_9_child_roi | ETH Up or Down 5m 9 Child ROI |
| 10 | Conditional downstream | Child ROI | 10 | b7c50005-0000-4000-8195-000000000010 | eth_up_down_5m_10_child_roi | ETH Up or Down 5m 10 Child ROI |
| 11 | Conditional downstream | Child ROI | 11 | b7c50005-0000-4000-8195-000000000011 | eth_up_down_5m_11_child_roi | ETH Up or Down 5m 11 Child ROI |
| 12 | Conditional downstream | Child ROI | 12 | b7c50005-0000-4000-8195-000000000012 | eth_up_down_5m_12_child_roi | ETH Up or Down 5m 12 Child ROI |
| 13 | Conditional downstream | Child ROI | 13 | b7c50005-0000-4000-8195-000000000013 | eth_up_down_5m_13_child_roi | ETH Up or Down 5m 13 Child ROI |
| 14 | Conditional downstream | Child ROI | 14 | b7c50005-0000-4000-8195-000000000014 | eth_up_down_5m_14_child_roi | ETH Up or Down 5m 14 Child ROI |
| 15 | Conditional downstream | Child ROI | 15 | b7c50005-0000-4000-8195-000000000015 | eth_up_down_5m_15_child_roi | ETH Up or Down 5m 15 Child ROI |
| 16 | Conditional downstream | Child ROI | 16 | b7c50005-0000-4000-8195-000000000016 | eth_up_down_5m_16_child_roi | ETH Up or Down 5m 16 Child ROI |
| 17 | Conditional downstream | Child ROI | 17 | b7c50005-0000-4000-8195-000000000017 | eth_up_down_5m_17_child_roi | ETH Up or Down 5m 17 Child ROI |
| 18 | Conditional downstream | Child ROI | 18 | b7c50005-0000-4000-8195-000000000018 | eth_up_down_5m_18_child_roi | ETH Up or Down 5m 18 Child ROI |
| 19 | Conditional downstream | Child ROI | 19 | b7c50005-0000-4000-8195-000000000019 | eth_up_down_5m_19_child_roi | ETH Up or Down 5m 19 Child ROI |
| 20 | Conditional downstream | Child ROI | 20 | b7c50005-0000-4000-8195-000000000020 | eth_up_down_5m_20_child_roi | ETH Up or Down 5m 20 Child ROI |
| 21 | Conditional downstream | Child ROI | 21 | b7c50005-0000-4000-8195-000000000021 | eth_up_down_5m_21_child_roi | ETH Up or Down 5m 21 Child ROI |
| 22 | Conditional downstream | Child ROI | 22 | b7c50005-0000-4000-8195-000000000022 | eth_up_down_5m_22_child_roi | ETH Up or Down 5m 22 Child ROI |
| 23 | Conditional downstream | Child ROI | 23 | b7c50005-0000-4000-8195-000000000023 | eth_up_down_5m_23_child_roi | ETH Up or Down 5m 23 Child ROI |
| 24 | Conditional downstream | Child ROI | 24 | b7c50005-0000-4000-8195-000000000024 | eth_up_down_5m_24_child_roi | ETH Up or Down 5m 24 Child ROI |

#### ETH Child Progress ROI — 6

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress ROI | 1 | b7c50005-0000-4000-8198-000000000001 | eth_up_down_5m_1_child_progress_roi | ETH Up or Down 5m 1 Child Progress ROI |
| 2 | Conditional downstream | Child Progress ROI | 2 | b7c50005-0000-4000-8198-000000000002 | eth_up_down_5m_2_child_progress_roi | ETH Up or Down 5m 2 Child Progress ROI |
| 3 | Conditional downstream | Child Progress ROI | 4 | b7c50005-0000-4000-8198-000000000004 | eth_up_down_5m_4_child_progress_roi | ETH Up or Down 5m 4 Child Progress ROI |
| 4 | Conditional downstream | Child Progress ROI | 6 | b7c50005-0000-4000-8198-000000000006 | eth_up_down_5m_6_child_progress_roi | ETH Up or Down 5m 6 Child Progress ROI |
| 5 | Conditional downstream | Child Progress ROI | 10 | b7c50005-0000-4000-8198-000000000010 | eth_up_down_5m_10_child_progress_roi | ETH Up or Down 5m 10 Child Progress ROI |
| 6 | Conditional downstream | Child Progress ROI | 20 | b7c50005-0000-4000-8198-000000000020 | eth_up_down_5m_20_child_progress_roi | ETH Up or Down 5m 20 Child Progress ROI |

### SOL conditional ChildMirror — 88 variants

#### SOL Child — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child | 1 | b7c50005-0000-4000-8187-000000000001 | sol_up_down_5m_1_child | SOL Up or Down 5m 1 Child |
| 2 | Conditional downstream | Child | 2 | b7c50005-0000-4000-8187-000000000002 | sol_up_down_5m_2_child | SOL Up or Down 5m 2 Child |
| 3 | Conditional downstream | Child | 3 | b7c50005-0000-4000-8187-000000000003 | sol_up_down_5m_3_child | SOL Up or Down 5m 3 Child |
| 4 | Conditional downstream | Child | 4 | b7c50005-0000-4000-8187-000000000004 | sol_up_down_5m_4_child | SOL Up or Down 5m 4 Child |
| 5 | Conditional downstream | Child | 5 | b7c50005-0000-4000-8187-000000000005 | sol_up_down_5m_5_child | SOL Up or Down 5m 5 Child |
| 6 | Conditional downstream | Child | 6 | b7c50005-0000-4000-8187-000000000006 | sol_up_down_5m_6_child | SOL Up or Down 5m 6 Child |
| 7 | Conditional downstream | Child | 7 | b7c50005-0000-4000-8187-000000000007 | sol_up_down_5m_7_child | SOL Up or Down 5m 7 Child |
| 8 | Conditional downstream | Child | 8 | b7c50005-0000-4000-8187-000000000008 | sol_up_down_5m_8_child | SOL Up or Down 5m 8 Child |
| 9 | Conditional downstream | Child | 9 | b7c50005-0000-4000-8187-000000000009 | sol_up_down_5m_9_child | SOL Up or Down 5m 9 Child |
| 10 | Conditional downstream | Child | 10 | b7c50005-0000-4000-8187-000000000010 | sol_up_down_5m_10_child | SOL Up or Down 5m 10 Child |
| 11 | Conditional downstream | Child | 11 | b7c50005-0000-4000-8187-000000000011 | sol_up_down_5m_11_child | SOL Up or Down 5m 11 Child |
| 12 | Conditional downstream | Child | 12 | b7c50005-0000-4000-8187-000000000012 | sol_up_down_5m_12_child | SOL Up or Down 5m 12 Child |
| 13 | Conditional downstream | Child | 13 | b7c50005-0000-4000-8187-000000000013 | sol_up_down_5m_13_child | SOL Up or Down 5m 13 Child |
| 14 | Conditional downstream | Child | 14 | b7c50005-0000-4000-8187-000000000014 | sol_up_down_5m_14_child | SOL Up or Down 5m 14 Child |
| 15 | Conditional downstream | Child | 15 | b7c50005-0000-4000-8187-000000000015 | sol_up_down_5m_15_child | SOL Up or Down 5m 15 Child |
| 16 | Conditional downstream | Child | 16 | b7c50005-0000-4000-8187-000000000016 | sol_up_down_5m_16_child | SOL Up or Down 5m 16 Child |
| 17 | Conditional downstream | Child | 17 | b7c50005-0000-4000-8187-000000000017 | sol_up_down_5m_17_child | SOL Up or Down 5m 17 Child |
| 18 | Conditional downstream | Child | 18 | b7c50005-0000-4000-8187-000000000018 | sol_up_down_5m_18_child | SOL Up or Down 5m 18 Child |
| 19 | Conditional downstream | Child | 19 | b7c50005-0000-4000-8187-000000000019 | sol_up_down_5m_19_child | SOL Up or Down 5m 19 Child |
| 20 | Conditional downstream | Child | 20 | b7c50005-0000-4000-8187-000000000020 | sol_up_down_5m_20_child | SOL Up or Down 5m 20 Child |
| 21 | Conditional downstream | Child | 21 | b7c50005-0000-4000-8187-000000000021 | sol_up_down_5m_21_child | SOL Up or Down 5m 21 Child |
| 22 | Conditional downstream | Child | 22 | b7c50005-0000-4000-8187-000000000022 | sol_up_down_5m_22_child | SOL Up or Down 5m 22 Child |
| 23 | Conditional downstream | Child | 23 | b7c50005-0000-4000-8187-000000000023 | sol_up_down_5m_23_child | SOL Up or Down 5m 23 Child |
| 24 | Conditional downstream | Child | 24 | b7c50005-0000-4000-8187-000000000024 | sol_up_down_5m_24_child | SOL Up or Down 5m 24 Child |

#### SOL Child Progress — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress | 1 | b7c50005-0000-4000-8190-000000000001 | sol_up_down_5m_1_child_progress | SOL Up or Down 5m 1 Child Progress |
| 2 | Conditional downstream | Child Progress | 2 | b7c50005-0000-4000-8190-000000000002 | sol_up_down_5m_2_child_progress | SOL Up or Down 5m 2 Child Progress |
| 3 | Conditional downstream | Child Progress | 3 | b7c50005-0000-4000-8190-000000000003 | sol_up_down_5m_3_child_progress | SOL Up or Down 5m 3 Child Progress |
| 4 | Conditional downstream | Child Progress | 4 | b7c50005-0000-4000-8190-000000000004 | sol_up_down_5m_4_child_progress | SOL Up or Down 5m 4 Child Progress |
| 5 | Conditional downstream | Child Progress | 5 | b7c50005-0000-4000-8190-000000000005 | sol_up_down_5m_5_child_progress | SOL Up or Down 5m 5 Child Progress |
| 6 | Conditional downstream | Child Progress | 6 | b7c50005-0000-4000-8190-000000000006 | sol_up_down_5m_6_child_progress | SOL Up or Down 5m 6 Child Progress |
| 7 | Conditional downstream | Child Progress | 7 | b7c50005-0000-4000-8190-000000000007 | sol_up_down_5m_7_child_progress | SOL Up or Down 5m 7 Child Progress |
| 8 | Conditional downstream | Child Progress | 8 | b7c50005-0000-4000-8190-000000000008 | sol_up_down_5m_8_child_progress | SOL Up or Down 5m 8 Child Progress |
| 9 | Conditional downstream | Child Progress | 9 | b7c50005-0000-4000-8190-000000000009 | sol_up_down_5m_9_child_progress | SOL Up or Down 5m 9 Child Progress |
| 10 | Conditional downstream | Child Progress | 10 | b7c50005-0000-4000-8190-000000000010 | sol_up_down_5m_10_child_progress | SOL Up or Down 5m 10 Child Progress |
| 11 | Conditional downstream | Child Progress | 11 | b7c50005-0000-4000-8190-000000000011 | sol_up_down_5m_11_child_progress | SOL Up or Down 5m 11 Child Progress |
| 12 | Conditional downstream | Child Progress | 12 | b7c50005-0000-4000-8190-000000000012 | sol_up_down_5m_12_child_progress | SOL Up or Down 5m 12 Child Progress |
| 13 | Conditional downstream | Child Progress | 13 | b7c50005-0000-4000-8190-000000000013 | sol_up_down_5m_13_child_progress | SOL Up or Down 5m 13 Child Progress |
| 14 | Conditional downstream | Child Progress | 14 | b7c50005-0000-4000-8190-000000000014 | sol_up_down_5m_14_child_progress | SOL Up or Down 5m 14 Child Progress |
| 15 | Conditional downstream | Child Progress | 15 | b7c50005-0000-4000-8190-000000000015 | sol_up_down_5m_15_child_progress | SOL Up or Down 5m 15 Child Progress |
| 16 | Conditional downstream | Child Progress | 16 | b7c50005-0000-4000-8190-000000000016 | sol_up_down_5m_16_child_progress | SOL Up or Down 5m 16 Child Progress |
| 17 | Conditional downstream | Child Progress | 17 | b7c50005-0000-4000-8190-000000000017 | sol_up_down_5m_17_child_progress | SOL Up or Down 5m 17 Child Progress |
| 18 | Conditional downstream | Child Progress | 18 | b7c50005-0000-4000-8190-000000000018 | sol_up_down_5m_18_child_progress | SOL Up or Down 5m 18 Child Progress |
| 19 | Conditional downstream | Child Progress | 19 | b7c50005-0000-4000-8190-000000000019 | sol_up_down_5m_19_child_progress | SOL Up or Down 5m 19 Child Progress |
| 20 | Conditional downstream | Child Progress | 20 | b7c50005-0000-4000-8190-000000000020 | sol_up_down_5m_20_child_progress | SOL Up or Down 5m 20 Child Progress |
| 21 | Conditional downstream | Child Progress | 21 | b7c50005-0000-4000-8190-000000000021 | sol_up_down_5m_21_child_progress | SOL Up or Down 5m 21 Child Progress |
| 22 | Conditional downstream | Child Progress | 22 | b7c50005-0000-4000-8190-000000000022 | sol_up_down_5m_22_child_progress | SOL Up or Down 5m 22 Child Progress |
| 23 | Conditional downstream | Child Progress | 23 | b7c50005-0000-4000-8190-000000000023 | sol_up_down_5m_23_child_progress | SOL Up or Down 5m 23 Child Progress |
| 24 | Conditional downstream | Child Progress | 24 | b7c50005-0000-4000-8190-000000000024 | sol_up_down_5m_24_child_progress | SOL Up or Down 5m 24 Child Progress |

#### SOL Child ROI — 24

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child ROI | 1 | b7c50005-0000-4000-8196-000000000001 | sol_up_down_5m_1_child_roi | SOL Up or Down 5m 1 Child ROI |
| 2 | Conditional downstream | Child ROI | 2 | b7c50005-0000-4000-8196-000000000002 | sol_up_down_5m_2_child_roi | SOL Up or Down 5m 2 Child ROI |
| 3 | Conditional downstream | Child ROI | 3 | b7c50005-0000-4000-8196-000000000003 | sol_up_down_5m_3_child_roi | SOL Up or Down 5m 3 Child ROI |
| 4 | Conditional downstream | Child ROI | 4 | b7c50005-0000-4000-8196-000000000004 | sol_up_down_5m_4_child_roi | SOL Up or Down 5m 4 Child ROI |
| 5 | Conditional downstream | Child ROI | 5 | b7c50005-0000-4000-8196-000000000005 | sol_up_down_5m_5_child_roi | SOL Up or Down 5m 5 Child ROI |
| 6 | Conditional downstream | Child ROI | 6 | b7c50005-0000-4000-8196-000000000006 | sol_up_down_5m_6_child_roi | SOL Up or Down 5m 6 Child ROI |
| 7 | Conditional downstream | Child ROI | 7 | b7c50005-0000-4000-8196-000000000007 | sol_up_down_5m_7_child_roi | SOL Up or Down 5m 7 Child ROI |
| 8 | Conditional downstream | Child ROI | 8 | b7c50005-0000-4000-8196-000000000008 | sol_up_down_5m_8_child_roi | SOL Up or Down 5m 8 Child ROI |
| 9 | Conditional downstream | Child ROI | 9 | b7c50005-0000-4000-8196-000000000009 | sol_up_down_5m_9_child_roi | SOL Up or Down 5m 9 Child ROI |
| 10 | Conditional downstream | Child ROI | 10 | b7c50005-0000-4000-8196-000000000010 | sol_up_down_5m_10_child_roi | SOL Up or Down 5m 10 Child ROI |
| 11 | Conditional downstream | Child ROI | 11 | b7c50005-0000-4000-8196-000000000011 | sol_up_down_5m_11_child_roi | SOL Up or Down 5m 11 Child ROI |
| 12 | Conditional downstream | Child ROI | 12 | b7c50005-0000-4000-8196-000000000012 | sol_up_down_5m_12_child_roi | SOL Up or Down 5m 12 Child ROI |
| 13 | Conditional downstream | Child ROI | 13 | b7c50005-0000-4000-8196-000000000013 | sol_up_down_5m_13_child_roi | SOL Up or Down 5m 13 Child ROI |
| 14 | Conditional downstream | Child ROI | 14 | b7c50005-0000-4000-8196-000000000014 | sol_up_down_5m_14_child_roi | SOL Up or Down 5m 14 Child ROI |
| 15 | Conditional downstream | Child ROI | 15 | b7c50005-0000-4000-8196-000000000015 | sol_up_down_5m_15_child_roi | SOL Up or Down 5m 15 Child ROI |
| 16 | Conditional downstream | Child ROI | 16 | b7c50005-0000-4000-8196-000000000016 | sol_up_down_5m_16_child_roi | SOL Up or Down 5m 16 Child ROI |
| 17 | Conditional downstream | Child ROI | 17 | b7c50005-0000-4000-8196-000000000017 | sol_up_down_5m_17_child_roi | SOL Up or Down 5m 17 Child ROI |
| 18 | Conditional downstream | Child ROI | 18 | b7c50005-0000-4000-8196-000000000018 | sol_up_down_5m_18_child_roi | SOL Up or Down 5m 18 Child ROI |
| 19 | Conditional downstream | Child ROI | 19 | b7c50005-0000-4000-8196-000000000019 | sol_up_down_5m_19_child_roi | SOL Up or Down 5m 19 Child ROI |
| 20 | Conditional downstream | Child ROI | 20 | b7c50005-0000-4000-8196-000000000020 | sol_up_down_5m_20_child_roi | SOL Up or Down 5m 20 Child ROI |
| 21 | Conditional downstream | Child ROI | 21 | b7c50005-0000-4000-8196-000000000021 | sol_up_down_5m_21_child_roi | SOL Up or Down 5m 21 Child ROI |
| 22 | Conditional downstream | Child ROI | 22 | b7c50005-0000-4000-8196-000000000022 | sol_up_down_5m_22_child_roi | SOL Up or Down 5m 22 Child ROI |
| 23 | Conditional downstream | Child ROI | 23 | b7c50005-0000-4000-8196-000000000023 | sol_up_down_5m_23_child_roi | SOL Up or Down 5m 23 Child ROI |
| 24 | Conditional downstream | Child ROI | 24 | b7c50005-0000-4000-8196-000000000024 | sol_up_down_5m_24_child_roi | SOL Up or Down 5m 24 Child ROI |

#### SOL Child Progress ROI — 16

| # | Kind | Trigger | Threshold | Strategy ID | Code | Name |
|---:|---|---|---:|---|---|---|
| 1 | Conditional downstream | Child Progress ROI | 1 | b7c50005-0000-4000-8199-000000000001 | sol_up_down_5m_1_child_progress_roi | SOL Up or Down 5m 1 Child Progress ROI |
| 2 | Conditional downstream | Child Progress ROI | 2 | b7c50005-0000-4000-8199-000000000002 | sol_up_down_5m_2_child_progress_roi | SOL Up or Down 5m 2 Child Progress ROI |
| 3 | Conditional downstream | Child Progress ROI | 3 | b7c50005-0000-4000-8199-000000000003 | sol_up_down_5m_3_child_progress_roi | SOL Up or Down 5m 3 Child Progress ROI |
| 4 | Conditional downstream | Child Progress ROI | 7 | b7c50005-0000-4000-8199-000000000007 | sol_up_down_5m_7_child_progress_roi | SOL Up or Down 5m 7 Child Progress ROI |
| 5 | Conditional downstream | Child Progress ROI | 8 | b7c50005-0000-4000-8199-000000000008 | sol_up_down_5m_8_child_progress_roi | SOL Up or Down 5m 8 Child Progress ROI |
| 6 | Conditional downstream | Child Progress ROI | 9 | b7c50005-0000-4000-8199-000000000009 | sol_up_down_5m_9_child_progress_roi | SOL Up or Down 5m 9 Child Progress ROI |
| 7 | Conditional downstream | Child Progress ROI | 10 | b7c50005-0000-4000-8199-000000000010 | sol_up_down_5m_10_child_progress_roi | SOL Up or Down 5m 10 Child Progress ROI |
| 8 | Conditional downstream | Child Progress ROI | 11 | b7c50005-0000-4000-8199-000000000011 | sol_up_down_5m_11_child_progress_roi | SOL Up or Down 5m 11 Child Progress ROI |
| 9 | Conditional downstream | Child Progress ROI | 12 | b7c50005-0000-4000-8199-000000000012 | sol_up_down_5m_12_child_progress_roi | SOL Up or Down 5m 12 Child Progress ROI |
| 10 | Conditional downstream | Child Progress ROI | 15 | b7c50005-0000-4000-8199-000000000015 | sol_up_down_5m_15_child_progress_roi | SOL Up or Down 5m 15 Child Progress ROI |
| 11 | Conditional downstream | Child Progress ROI | 16 | b7c50005-0000-4000-8199-000000000016 | sol_up_down_5m_16_child_progress_roi | SOL Up or Down 5m 16 Child Progress ROI |
| 12 | Conditional downstream | Child Progress ROI | 17 | b7c50005-0000-4000-8199-000000000017 | sol_up_down_5m_17_child_progress_roi | SOL Up or Down 5m 17 Child Progress ROI |
| 13 | Conditional downstream | Child Progress ROI | 18 | b7c50005-0000-4000-8199-000000000018 | sol_up_down_5m_18_child_progress_roi | SOL Up or Down 5m 18 Child Progress ROI |
| 14 | Conditional downstream | Child Progress ROI | 20 | b7c50005-0000-4000-8199-000000000020 | sol_up_down_5m_20_child_progress_roi | SOL Up or Down 5m 20 Child Progress ROI |
| 15 | Conditional downstream | Child Progress ROI | 22 | b7c50005-0000-4000-8199-000000000022 | sol_up_down_5m_22_child_progress_roi | SOL Up or Down 5m 22 Child Progress ROI |
| 16 | Conditional downstream | Child Progress ROI | 24 | b7c50005-0000-4000-8199-000000000024 | sol_up_down_5m_24_child_progress_roi | SOL Up or Down 5m 24 Child Progress ROI |

## Explicit exclusions

| Exclusion | Current count | Why excluded from the 848 |
|---|---:|---|
| ETH 3Hour Average | 28 | Single-window 3h selector; maximum and minimum are the same average. |
| ETH 3Hour LowEnter Average | 28 | Same single-window 3h signal, with a separate Paper fill cap. |
| Single-window subtotal | 56 | Behaviorally unchanged by a multi-window Max/Min envelope. |
| Absolute Premarket | 480 | Separate extrema provider and decision method; already compares current price with historical maximum/minimum rather than average boundaries. |
| Filtered Reference Average | 0 | Dormant behavior/dispatch path exists, but no current catalog variants are registered. |
| Diff Reference Average | 56 | Separate Diff-history average algorithm. It is not directly migrated; only composites that also invoke a price Reference Average signal are included. |
| ChildMirror | 247 conditional rows listed separately | Not part of the 848 direct/statically linked set. A row is affected only while runtime assignment points it to one of the 378 eligible affected parents. |
| FuturesBasis, Instant, previous-result, Progress, and other families | Not part of this list | They do not directly, statically, or through the verified ChildMirror assignment/copy path invoke the multi-window price Reference Average selector. |

## Catalog and implementation evidence

- Catalog composition: src/PolyCopyTrader.Domain/Models.cs:1289-1296.
- BTC Reference Average and Optimized grids: src/PolyCopyTrader.Domain/Models.cs:1364-1395.
- ETH/SOL grids and terminal Optimized LowerEnter clones: src/PolyCopyTrader.Domain/Models.cs:1710-1812.
- Generic BTC LowerEnter eligibility and stable identity transformation: src/PolyCopyTrader.Domain/Models.cs:1818-1890.
- BpsConfirmed and DiffConfirmed links: src/PolyCopyTrader.Domain/Models.cs:2171-2227.
- Threshold grids: src/PolyCopyTrader.Domain/Models.cs:2298-2308 and 2361-2371.
- Exact ID/code/name factories: src/PolyCopyTrader.Domain/Models.cs:2728-2932.
- ID-group mappings: src/PolyCopyTrader.Domain/Models.cs:2255-2273 and 2973-3025.
- ChildMirror catalog expansion and exact identity factory: src/PolyCopyTrader.Domain/Models.cs:2415-2501.
- Retired ChildMirror variants and surviving ETH/SOL lookbacks: src/PolyCopyTrader.Domain/Models.cs:1943-1978.
- Direct dispatcher routes: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:7243-7284.
- Composite dispatcher and linked-signal evaluation: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:7393-7402 and 9875-10033.
- Multi-window decision method and boundary selector: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:12798 onward and 14971-14996.
- Single-window exclusion: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:6758-6761.
- Absolute separate path: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:13123 onward.
- ChildMirror behavior identification and exclusion from ordinary entry evaluation: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:177-180 and 2311-2316.
- ChildMirror assignment refresh and eligible-parent selection: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:4304-4438. Candidate parents must be active, five-minute, same-asset, non-ChildMirror, non-FuturesBasis, and non-PaperOnly; Progress/ROI variants add their respective gates before the top candidate is persisted.
- ChildMirror assignment attachment and accepted parent-entry copy: src/PolyCopyTrader.Service/Strategies/BtcUpDown5mPaperStrategyProcessor.cs:4460-4701, with entry/fill call sites at 5347-5361 and 5630-5645.

The current worktree has corrected the formerly stale BTC Optimized catalog assertion at tests/PolyCopyTrader.Tests/BtcUpDown5mPaperStrategyProcessorTests.cs:235 from 10 to 30, matching the exact grid test, current factory, and reflected catalog.

## Reproducible verification

Run this read-only PowerShell inventory after building the Domain project that corresponds to the source revision being verified:

~~~powershell
$dll = (Resolve-Path 'src/PolyCopyTrader.Domain/bin/Release/net10.0/PolyCopyTrader.Domain.dll').Path
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
$type = $asm.GetType('PolyCopyTrader.Domain.StrategyIds', $true)
$all = @($type.GetField('UpDown5mStrategyVariants').GetValue($null))

$directBehaviors = @(
  'ReferenceAverageBpsThresholdFakPremarket',
  'OptimizedReferenceAverageBpsThresholdFakPremarket',
  'LowEnterReferenceAverageBpsThresholdFakPremarket'
)
$indirectBehaviors = @(
  'BpsConfirmedAveragePremarket',
  'DiffConfirmedAveragePremarket'
)
$conditionalBehaviors = @(
  'ChildMirror',
  'ChildProgressMirror',
  'ChildRoiMirror',
  'ChildProgressRoiMirror'
)

$direct = @($all | Where-Object { $directBehaviors -contains $_.Behavior.ToString() })
$indirect = @($all | Where-Object { $indirectBehaviors -contains $_.Behavior.ToString() })
$affected = @($direct + $indirect)
$conditional = @($all | Where-Object { $conditionalBehaviors -contains $_.Behavior.ToString() })
$expanded = @($affected + $conditional)
$eligibleAffectedParents = @($affected | Where-Object {
  -not $_.PaperOnly -and
  $_.Behavior.ToString() -ne 'OptimizedReferenceAverageBpsThresholdFakPremarket'
})
$staticIdKeys = @($affected | ForEach-Object { $_.Id.ToString() })
$conditionalIdKeys = @($conditional | ForEach-Object { $_.Id.ToString() })

[pscustomobject]@{
  Catalog = $all.Count
  Direct = $direct.Count
  Indirect = $indirect.Count
  StaticAffected = $affected.Count
  ConditionalChild = $conditional.Count
  Expanded = $expanded.Count
  EligibleAffectedParentsBeforeRuntimeGates = $eligibleAffectedParents.Count
  StaticUniqueIds = @($affected.Id | Sort-Object -Unique).Count
  StaticUniqueCodes = @($affected.Code | Sort-Object -Unique).Count
  ConditionalUniqueIds = @($conditional.Id | Sort-Object -Unique).Count
  ConditionalUniqueCodes = @($conditional.Code | Sort-Object -Unique).Count
  ConditionalUniqueNames = @($conditional.Name | Sort-Object -Unique).Count
  ExpandedUniqueIds = @($expanded.Id | Sort-Object -Unique).Count
  ExpandedUniqueCodes = @($expanded.Code | Sort-Object -Unique).Count
  ExpandedUniqueNames = @($expanded.Name | Sort-Object -Unique).Count
  StaticConditionalIdOverlap = @($staticIdKeys | Where-Object { $conditionalIdKeys -contains $_ } | Sort-Object -Unique).Count
  SingleWindow = @($all | Where-Object { $_.Behavior.ToString() -in @('ThreeHourReferenceAverageBpsThresholdFakPremarket', 'ThreeHourLowEnterReferenceAverageBpsThresholdFakPremarket') }).Count
  Absolute = @($all | Where-Object { $_.Behavior.ToString() -eq 'AbsoluteBpsThresholdFakPremarket' }).Count
  Filtered = @($all | Where-Object { $_.Behavior.ToString() -eq 'FilteredReferenceAverageBpsThresholdFakPremarket' }).Count
}

$affected |
  Group-Object ReferenceAssetSymbol, Behavior, { $_.LowerEnterSourceStrategyId -ne $null } |
  Sort-Object Name |
  Select-Object Name, Count

$conditional |
  Group-Object ReferenceAssetSymbol, Behavior |
  Sort-Object Name |
  Select-Object Name, Count

$expanded |
  Group-Object ReferenceAssetSymbol |
  Sort-Object Name |
  Select-Object Name, Count
~~~

Expected headline result: Catalog 3193; Direct 680; Indirect 168; StaticAffected/StaticUniqueIds/StaticUniqueCodes 848; ConditionalChild/ConditionalUniqueIds/ConditionalUniqueCodes/ConditionalUniqueNames 247; Expanded/ExpandedUniqueIds/ExpandedUniqueCodes/ExpandedUniqueNames 1095; EligibleAffectedParentsBeforeRuntimeGates 378; StaticConditionalIdOverlap 0; SingleWindow 56; Absolute 480; Filtered 0. Expanded asset counts are BTC 408 / ETH 385 / SOL 302.

## Integrity statement

The tables contain exactly 1,095 data rows: 848 direct or statically linked variants plus a disjoint set of 247 conditional ChildMirror variants. Each row is one catalog variant. Across the combined inventory, all 1,095 IDs, codes, and names are unique and match the reflected catalog exactly; verification must reject duplicates, missing/extra identities, or any deviation from the expected direct, indirect, conditional, exclusion, asset, or full-catalog counts. Both the original local Release-catalog enumeration and the independent freshly built current-worktree reflection produced the same 848-member static identity set; the separate ChildMirror audit matched all 247 conditional identities against reflection and the catalog factory formula.
