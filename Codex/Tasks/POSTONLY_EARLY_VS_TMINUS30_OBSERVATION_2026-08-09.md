# PostOnly: first accepting vs T-30s

Date of capture: 2026-08-09 UTC
Mode: strictly read-only; no order was submitted, cancelled, or filled

## Scope

- Formula: `floor_to_tick(min(bestBid + tick, bestAsk - tick, 0.99))`.
- Assets and outcomes: `BTC/ETH/SOL × Up/Down`.
- EARLY group: six markets starting `2026-08-10 15:30..15:55Z`, observed at the first available `acceptingOrders=true` checkpoint. The first computable quotes were captured `23.809593..23.858539` hours before the event-window start, not at exactly 24 hours.
- T-30 group: six different markets starting `2026-08-09 16:15..16:40Z`, observed at `slot start - 30s`.
- Planned scale: `36` asset/outcome combinations per group. The canonical CSV contains all `72` planned rows, exact market/condition/token identifiers, timestamps, top of book, formula price, S0/S1 result, attempts, and reason.

These are two non-paired groups of different market instances. They support a descriptive quote comparison, not a causal estimate of the gain from early placement.

## What 0.50 and 0.49 mean

They are already the final Maker limit prices produced by the current formula. Do not subtract another tick.

For a one-tick spread:

- Up `bid=0.50`, `ask=0.51`, `tick=0.01` gives `min(0.51, 0.50, 0.99) = 0.50`.
- Down `bid=0.49`, `ask=0.50`, `tick=0.01` gives `min(0.50, 0.49, 0.99) = 0.49`.

The `ask - tick` term has already kept the limit one tick below the ask. At S1, the frozen limit must still be strictly below the new best ask. Equality is not resting PostOnly evidence and causes a retry or local rejection.

`accepted_hypothetically` below means only that the read-only S0/S1 Paper gate found fresh evidence and a resting limit. It is not Polymarket venue acceptance and is not fill evidence.

## EARLY results

In the table, `✓` is the accepted hypothetical limit, `F` is a computable formula candidate that failed the local freshness/S0 gate, and `—` is the explicitly missed checkpoint. Up is shown before Down.

| Start UTC | BTC Up / Down | ETH Up / Down | SOL Up / Down | S0/S1 |
|---|---:|---:|---:|---:|
| 15:30 | 0.50F / 0.49F | 0.02F / 0.02F | 0.50F / 0.49F | 0/6 |
| 15:35 | — / — | — / — | — / — | MISSED 6/6 |
| 15:40 | 0.02✓ / 0.49✓ | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 6/6 |
| 15:45 | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 6/6 |
| 15:50 | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 6/6 |
| 15:55 | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 0.50✓ / 0.49✓ | 6/6 |

Strict deduplication result:

- `30/36` formula prices are available; the late duplicates for the missed `15:35Z` checkpoint were excluded.
- `24/36` passed the hypothetical S0/S1 gate, `6/36` failed freshness at `15:30Z`, and `6/36` were missed.
- The formula matched the modal `Up=0.50 / Down=0.49` pattern in `26/30` available rows.
- The accepted limit matched that pattern in `23/24` accepted rows. The exception was BTC Up `0.02` at `15:40Z`.
- BTC Down at `15:40Z` demonstrates why formula and accepted price must stay separate: its first formula candidate was `0.50`, but S1 detected crossing; attempt 4 was accepted at `0.49`.

Therefore, `0.50/0.49` is the early mode and median in this sample, not a guaranteed market-opening price.

## T-30 results

The same legend applies. All formula values are from the first request at the target; retries did not change the formula in any of the 36 rows.

| Start UTC | BTC Up / Down | ETH Up / Down | SOL Up / Down | S0/S1 |
|---|---:|---:|---:|---:|
| 16:15 | 0.49✓ / 0.50✓ | 0.49✓ / 0.50✓ | 0.48✓ / 0.51✓ | 6/6 |
| 16:20 | 0.49F / 0.50F | 0.49✓ / 0.50✓ | 0.48✓ / 0.51✓ | 4/6 |
| 16:25 | 0.49✓ / 0.50✓ | 0.51✓ / 0.48✓ | 0.49✓ / 0.50✓ | 6/6 |
| 16:30 | 0.51✓ / 0.48✓ | 0.54F / 0.45F | 0.51F / 0.48F | 2/6 |
| 16:35 | 0.48✓ / 0.52✓ | 0.52✓ / 0.47✓ | 0.51F / 0.48F | 4/6 |
| 16:40 | 0.44✓ / 0.55✓ | 0.47F / 0.52F | 0.49✓ / 0.50F | 3/6 |

Completeness and timing:

- Formula: `36/36`; independently recomputed mismatches: `0`.
- Hypothetical S0/S1 result: `25/36`; BTC `10/12`, ETH `8/12`, SOL `7/12`.
- All `11` local failures were `maker_gtd_s0_book_not_current`. They are stale-book evidence failures, not venue rejections.
- First requests began `+1.2371..+14.3204 ms` from the exact targets; median `+8.8002 ms`.
- First responses arrived `+128.6671..+347.6680 ms`; median `+226.1268 ms`.
- Of 25 accepted rows, 15 passed on attempt 1. Final hypothetical acceptance was `+199.9870..+1996.1281 ms`; median `+511.9169 ms`.

## Comparison with the modal early benchmark

This comparison uses `0.50` for Up and `0.49` for Down as the observed early modal benchmark. It does not pretend that every EARLY row had those values.

| Outcome | T-30 mean | Median | Range | Early benchmark cheaper | Early benchmark more expensive |
|---|---:|---:|---:|---:|---:|
| Up | 0.493333 | 0.49 | 0.44–0.54 | 6/18 | 12/18 |
| Down | 0.497222 | 0.50 | 0.45–0.55 | 12/18 | 6/18 |

- For the original Up-only idea, an early `0.50` was cheaper than the T-30 formula in only `6/18` observations and more expensive in `12/18`. The mean `T-30 - early` difference was `-0.006667` per Up share.
- For Down, early `0.49` was cheaper in `12/18` and more expensive in `6/18`; mean difference `+0.007222`.
- With equal Up/Down weight, T-30 averaged `0.495278` versus early benchmark `0.495000`, a difference of only `+0.000278` per share.
- For `17/18` asset-slot pairs, T-30 Up plus Down equalled `0.99`; one pair equalled `1.00`. Most observed movement was redistribution between outcomes, not a stable reduction in their combined price.

## Conclusion

The sample does not show a general price advantage for placing Up at `0.50` early. It helps on price only in the subset where Up later costs more than `0.50`; in this unconditional six-slot T-30 sample that occurred in `6/18` cases. Whether the proposed `any other strategy says Up` filter selects that favorable subset is unknown because strategy signals were not captured in this experiment.

The experiment also does not measure queue position, fill probability, pre-confirmation adverse selection, cancellation races, or PnL. No orders were submitted. A direct estimate of early-placement gain requires paired observations of the same market IDs at first accepting and at their own T-30 checkpoint, plus order/fill or defensible queue evidence.

## Durable evidence

- Canonical data: `Codex/Tasks/POSTONLY_EARLY_VS_TMINUS30_OBSERVATIONS_2026-08-09.csv`
- Canonical CSV rows: `72/72`; unique `group × slot × asset × outcome` keys: `72/72`.
- Canonical CSV SHA-256: `28F5C540A90C297D1612FFD82D28420129451CA736FD131EF1953776663414E0`.
- Raw EARLY JSONL before temp cleanup: 84 lines, SHA-256 `DF2EE414A31517225460C776B44FEA1B69C4169222F52E33A46BC1683AF5237D`.
- Missed-checkpoint audit before temp cleanup: SHA-256 `B24CB9EC9814095B76BE1B4C3F905EC0F505AB3E7025E3CEA119692BDCBBA21A`.
- Raw T-30 JSONL before temp cleanup: 98 lines, SHA-256 `F1ACFFA977CD4FD1B212B9EC615081820F2448872587899F35A0756EC6492A31`.
