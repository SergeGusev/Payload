# ETH Reference Average Mirror Replay — 2026-07-27

## Scope

- Strategy: `ETH Up or Down 5m 2 bps Reference Average Premarket`
- Strategy ID: `b7c50005-0000-4000-8179-000000000102`
- Strategy code: `eth_up_down_5m_reference_average_bps_2_fak_premarket`
- Fixed database snapshot: `2026-07-27T07:06:41.4845900Z`
- Database access: one PostgreSQL `REPEATABLE READ / READ ONLY` transaction; it was rolled back after loading inputs. All replay calculations and nearest-book pairing ran in process memory. No replay row, temporary table, strategy setting, order, or database state was written.

> Algorithm-status note (2026-07-27): this is a frozen replay of the legacy maximum-only Reference Average selector present in the saved production decisions. The current source now uses the Max/Min envelope contract (`Up -> Amax`, `Down -> Amin`, neutral -> either outside boundary). The counts and PnL below are historical evidence for the legacy selector, not a performance claim for the migrated selector.

## Mirror definition

The primary vertical reflection used the first replayable decision as the fixed anchor:

- anchor time: `2026-07-04T14:29:33.7421265Z`
- anchor price: `$1,770.80`
- linear mirror: `P' = 2 * 1770.80 - P`
- resulting mirrored tick range: `$1,564.93 .. $1,828.06`

A log-return sensitivity used `P' = 1770.80^2 / P`, with range `$1,586.3713 .. $1,829.9734`.

For both transformations, timestamps were unchanged. The stored decision-time current ETH price and all persisted warm-up/history ticks were transformed. The eight averages were then recomputed independently using the production bucket contract:

| Window | Bucket step | Required buckets |
|---|---:|---:|
| 24h | 1,440s | 60 |
| 12h | 720s | 60 |
| 6h | 360s | 60 |
| 3h | 180s | 60 |
| 90m | 90s | 60 |
| 45m | 45s | 60 |
| 20m | 20s | 60 |
| 10m | 10s | 60 |

Each bucket is the arithmetic mean of its ticks; a window average is the unweighted mean of its bucket means. The strategy again selected the maximum full average, applied the inclusive `2 bps` threshold, and chose the outcome opposite the sign of the deviation.

The mirrored-book assumption was:

- counterfactual Up book = actual Down book;
- counterfactual Down book = actual Up book.

Prices and sizes were not changed to `1-price`.

## Input coverage and controls

- Completed target runs: `6,519`.
- Runs with decision/reference-average JSON: `6,344`.
- Runs with non-null complete replay inputs: `6,273`.
- ETH ticks loaded: `200,001`, from `2026-07-03T13:29:32.3290320Z` through `2026-07-27T06:59:28.0645730Z`.
- Gaps over 60 seconds: `56`; maximum gap `01:48:05.7103300`. No interpolation was used.
- Known official winners: `6,271`; unknown: `2`.
- Winners confirmed by at least two persisted official-settlement sources: `6,271`; official conflicts: `0`.
- The provisional Binance close ledger disagreed with the official settlement outcome in `247` rows and was not used for PnL.
- Eight averages recomputed per scenario: `50,184` values.

The persisted tick replay exactly reproduced every stored average/bucket boundary and the signal on `2,088` decisions; all primary counterfactual results below are restricted to those strict identity rows with a known winner. A looser diagnostic found all stored averages within `$0.01` on `6,250/6,273` rows and the same entry/skip/outcome behavior on `6,263/6,273`, but these rows were not admitted to the strict financial cohort.

The direct linear-average calculation was independently checked against `avg(2A-P) = 2A-avg(P)` over all windows. Maximum absolute difference was `2e-22 USD`.

## Signal replay result

On the `2,088` strict identity markets:

| Scenario | Entries | Skips | Up | Down | Wins | Losses | Win rate |
|---|---:|---:|---:|---:|---:|---:|---:|
| Original | 1,903 | 185 | 1,565 | 338 | 1,018 | 885 | 53.4945% |
| Linear mirror | 2,027 | 61 | 1,828 | 199 | 1,027 | 1,000 | 50.6660% |
| Log mirror | 2,022 | 66 | 1,826 | 196 | 1,024 | 998 | 50.6429% |

For the linear mirror:

- selected average window changed on `2,080/2,088` markets;
- both original and mirror entered on `1,842` markets;
- only `537/1,842` of those entries were exact Up/Down swaps;
- `1,305/1,842` were not exact swaps;
- `61` entries disappeared and `185` new entries appeared.

This directly refutes a one-for-one outcome reversal.

## FAK replay result

The historical database does not preserve full premarket depth for both outcomes. The execution replay therefore used only saved, already-calculated Filled FAK summaries with all of these gates:

- same market and opposite actual outcome after book swap;
- `execution_source = btc_updown5m_fak_taker_paper`;
- `stake_multiplier = 1`;
- requested notional exactly `$6.0093`;
- nearest snapshot to the strategy decision;
- both Up and Down summaries present within the stated maximum lag.

Primary common cohort: both sides within 2 seconds, `1,510/2,088` strict identity markets.

| Scenario | Entries | Wins/Losses | Filled notional | PnL | ROI |
|---|---:|---:|---:|---:|---:|
| Original | 1,394 | 746 / 648 | $8,376.9642 | **+$328.74250312** | **+3.9244%** |
| Linear mirror | 1,469 | 714 / 755 | $8,827.6617 | **−$437.41497612** | **−4.9550%** |
| Log mirror | 1,466 | 713 / 753 | $8,809.6338 | **−$431.37452710** | **−4.8966%** |

Sensitivity cohort: both sides within 5 seconds, `1,587` markets.

| Scenario | PnL | ROI |
|---|---:|---:|
| Original | +$344.77833213 | +3.9110% |
| Linear mirror | −$395.74289090 | −4.2597% |
| Log mirror | −$389.70244188 | −4.2028% |

Each portfolio was summed independently in two forms: per-row win/loss PnL and `sum(winning shares) - sum(all filled notional)`. Maximum absolute discrepancy was below `5e-24 USD`.

The existing focused `CryptoReferencePriceAverageCacheTests` and `TakerBuyFillEstimatorTests` passed `8/8`. The temporary replay project built with zero warnings and zero errors.

## Why the intuitive swap control appears valid but the real replay does not

For the `5,786` actually settled target orders at the snapshot:

- stored PnL: `+$887.11180322`;
- independently reconstructed fill/settlement PnL: `+$887.11180365`;
- difference: `$0.00000043`, attributable to stored 8-decimal row rounding;
- forcibly swapping every selected outcome and every winner while preserving the exact same fills also gives `+$887.11180365`.

This last calculation is a forced relabeling control, not an exact vertical-reflection control for a true Chainlink tie. A vertical reflection preserves equality, and the official market rule keeps a tie as `Up`; the retained inputs do not identify which official `Up` winners, if any, were exact Chainlink ties. The control is therefore exact for non-tie rows, while its tie blast radius is unknown.

So the proposed logic is algebraically correct for non-tie rows only after assuming the original trades remain one-to-one and merely relabeling both bets and winners. The actual strategy replay does not preserve those trades: reflecting the ticks changes which of the eight averages is maximal, changes thresholds, creates and removes entries, and usually does not reverse the selected outcome.

## Conclusion and limitations

For the legacy maximum-only selector, the user's equal-PnL hypothesis is rejected under the specified mirrored-tick and mirrored-book model. On the strict common FAK cohort the original path earned about `+$328.74`, while the primary linear mirror lost about `-$437.41`. A fresh replay is required before drawing the corresponding conclusion for the migrated Max/Min envelope selector.

This is an in-sample counterfactual, not a forecast or a production trading rule. The strict cohort covers only rows whose persisted ticks exactly replayed the online cache and whose two FAK summaries were available within 2 seconds. Full historical order-book depth is absent, exact-tie outcome behavior is not separately observable, fees are absent from the current Paper model, and the chosen reflection anchor is part of the stated hypothetical.
