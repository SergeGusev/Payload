# ETH 24h Growth / Mirror Decision Graph — 2026-07-27

## Scope

- Strategy: `ETH Up or Down 5m 2 bps Reference Average Premarket`
- Strategy ID: `b7c50005-0000-4000-8179-000000000102`
- Strategy code: `eth_up_down_5m_reference_average_bps_2_fak_premarket`
- Final database snapshot: `2026-07-27T09:22:38.1340270Z`
- PostgreSQL access: one `REPEATABLE READ / READ ONLY` transaction with bounded statement/lock timeouts, ended by rollback.
- All ranking, tick reflection, eight-average replay, signal calculation, and rendering ran in C#/.NET memory. No database row, strategy setting, order, service, or product state changed.

The rendered PNG is:

`D:\My\Business\PolyMarket\outputs\eth-mirror-24h-growth-20260727\eth-mirror-24h-growth.png`

The conversation visualization fragment is:

`C:\Users\serge\.codex\visualizations\2026\07\26\019fa040-51f3-7111-98e9-f239d14d6c75\eth-mirror-24h-decision.html`

## Example selection

The read-only snapshot loaded 916 entered `Down` decisions from the available 30-day interval. Of those, 432 had exactly the eight expected unique full positive averages and a positive mirrored decision price. The chosen point had the maximum endpoint return from the first plotted ETH tick after `Tcache - 24h` to the saved decision price. A second independent query/replay selected the same market as rank `1/432`.

- market ID: `2910584`
- slug: `eth-updown-5m-1784054400`
- market start: `2026-07-14T18:40:00Z`
- entry due: `2026-07-14T18:39:30Z`
- decision clock: `2026-07-14T18:39:31.0286053Z`
- average-cache snapshot: `2026-07-14T18:39:29.9152019Z`
- current Binance trade timestamp: `2026-07-14T18:39:30.4230000Z`
- plot start: `2026-07-13T18:39:29.9152019Z`
- first plotted tick / mirror pivot: `$1,760.60` at `2026-07-13T18:39:35.6857330Z`
- saved current price: `$1,875.33`
- endpoint growth: `+651.652845620811087129387709 bps`, or `+6.51652845620811087129387709%`

The plot contains 8,424 saved ETH ticks. Using raw `sampled_at_utc` differences, it has 9 gaps over 60 seconds and a maximum gap of `147.013537s`. The price path is broken across those gaps; no price interpolation is used in the average replay.

## Exact eight-average result

Production contract: bucket key is `floor(ToUnixTimeSeconds(sampled_at_utc) / step) * step`; a bucket is the arithmetic mean of its ticks; the window value is the unweighted arithmetic mean of the retained bucket means; bucket starts `<= Tcache - window` are removed; full means at least 60 buckets. Selection is average descending, then window duration descending.

The persisted originals and independently transformed/replayed mirror values used in the graph are:

| Window | Step | Buckets | Actual average | Mirrored average |
|---|---:|---:|---:|---:|
| 24h | 1,440s | 60 | 1803.6577372074858050131544092 | 1717.5422627925141949868455918 |
| 12h | 720s | 60 | 1831.1603098262709889913036693 | 1690.0396901737290110086963315 |
| 6h | 360s | 60 | 1870.2791706388738291015330863 | 1650.9208293611261708984669135 |
| 3h | 180s | 60 | 1871.8967631033182503770739063 | 1649.3032368966817496229260937 |
| 90m | 90s | 60 | 1869.3728194444444444444444445 | 1651.8271805555555555555555555 |
| 45m | 45s | 60 | 1871.0808361111111111111111112 | 1650.1191638888888888888888888 |
| 20m | 20s | 60 | 1871.2139166666666666666666667 | 1649.9860833333333333333333333 |
| 10m | 10s | 60 | 1872.1748333333333333333333333 | 1649.0251666666666666666666667 |

### Why the selected window changes despite exact symmetry

The strategy does not preserve the identity of the previously selected line. On each path it sorts all full positive averages by price descending and selects the first one. The reflection is `A' = 2P0 - A`, which reverses every vertical comparison: whenever `A_i > A_j`, the mirrored values satisfy `A'_i < A'_j`.

For this graph `2P0 = 3521.20`, so the two extrema exchange roles:

| Window | Actual role/value | Mirrored role/value |
|---|---|---|
| 10m | maximum: `1872.174833333333...` | minimum: `1649.025166666666...` |
| 24h | minimum: `1803.657737207485...` | maximum: `1717.542262792514...` |

Therefore `max(A') = 2P0 - min(A)`, not `2P0 - max(A)`. The charts are exactly symmetric; that exact symmetry is what reverses the ranking and changes the selected reference from `10m` to `24h`.

Original decision:

- maximum average: `10m = $1,872.1748333333333333333333333`;
- move: `(1875.33 / 1872.1748333333333333333333333 - 1) * 10000 = +16.852948829832399021850613 bps`;
- inclusive `2 bps` threshold passed;
- positive move is inverted by the neutral strategy, producing `Down`.

## Mirror replay

The graph reflection is anchored at the first plotted tick:

```text
P0 = 1760.60
P'(t) = 2 * P0 - P(t)
Current' = 2 * 1760.60 - 1875.33 = 1645.87
```

Every stored ETH tick used by the cache replay and the saved current decision price was transformed; timestamps were unchanged. The eight mirrored averages were then recalculated independently.

Mirror decision:

- maximum average: `24h = $1,717.5422627925141949868455918`;
- move: `(1645.87 / 1717.5422627925141949868455918 - 1) * 10000 = -417.29548288368659436180094 bps`;
- inclusive `2 bps` threshold passed;
- negative move is inverted by the neutral strategy, producing `Up`.

For this selected point the strategy's selected outcome/token does reverse, but the reference does not: the actual decision uses the `10m` maximum, while the mirror uses the `24h` maximum. This sentence concerns the signal, not the later market winner. Algebraically, linear reflection makes `max(A') = 2P0 - min(A)`, so the actual minimum (`24h`) becomes the mirrored maximum.

## Pairwise settlement interpretation

The chart stops at the decision and does not contain the following five-minute settlement interval. The exact market metadata for `2910584` states that settlement uses the Chainlink ETH/USD stream: `Up` wins when the end price is greater than or equal to the beginning price; otherwise `Down` wins.

If the same fixed vertical reflection is extended through the complete Chainlink settlement path, with original start/end prices `O,C` and mirrored values `O'=2P0-O`, `C'=2P0-C`, then:

| Chainlink condition | Original `Down` bet | Mirrored `Up` bet |
|---|---|---|
| `C < O` | wins | wins |
| `C > O` | loses | loses |
| `C = O` | loses | wins |

Thus the pair has identical win/loss status for every non-tie path. Exact equality is the sole settlement-direction exception because reflection preserves equality and the market assigns equality to `Up`.

Equal win/loss does not by itself prove equal dollar PnL. Equal PnL additionally requires the mirrored-book contract `counterfactual Up book = actual Down book`, equal requested notional and stake state, identical FAK eligibility/fills, the same holding/exit path, and equal fees. Under those conditions the paired trades have identical shares and cost, so the settlement formula produces identical PnL. Without those execution conditions, the graph remains signal-only evidence.

For this exact selected market, a read-only production check at `2026-07-27T10:11:46Z` found the official `MarketWebSocket market_resolved` winner `Down`, with the target run settled as a win: entry/VWAP `0.52`, filled notional `$6.0093`, `11.55634615` shares, and Paper PnL `+$5.54704615`. This rules out the tie case for the original market. Therefore, under the additional complete-Chainlink reflection and identical mirrored-book fill premises, the counterfactual `Up` also wins and has the same `+$5.54704615` Paper PnL.

## Verification

- Selected-market settlement/fill audit: exact production `192.168.0.101:5432/polycopytrader`, UTC, `REPEATABLE READ / READ ONLY`, transaction timestamp `2026-07-27T10:16:35.361263Z`, snapshot `577920158:577920985:577920158,577920842,577920982`, ended by rollback. Filtering `strategy_market_paper_runs` by target strategy ID and market `2910584`, then joining its order, fill, target-wallet/asset/condition settlement, ETH resolved ledger, and Gamma market produced exactly one required row in each relation and zero unmatched links.
- Mirrored-fill audit: candidates were same-market Filled `Down` FAK summaries with multiplier `1`, requested and filled notional `$6.0093`, saved snapshot timestamp, and gap at most 2 seconds. There were 84 eligible summaries, 50 inside 2 seconds, and all 50 matched `0.52 / 11.55634615 / $6.0093` with zero partial fills. Deterministic order by absolute gap then order ID selected gap `0.222320s`.
- Settlement arithmetic was independently recomputed: `11.55634615 - 6.00930000 = 5.54704615`, matching both the target run and target-specific settlement row exactly.
- Fresh official Gamma `GET /markets/2910584` at `2026-07-27T10:14:57.9786379Z` returned HTTP 200, `closed=true`, outcomes `[Up,Down]`, prices `[0,1]`, and response SHA-256 `3EF297C45A32113420A45416CA9ABDA288F226DE9DBC60CAA6F986E0041E4472`, independently confirming `Down`.
- Independent selection query: same market, rank `1/432`, same pivot, current price, and endpoint return.
- Independent production-order .NET replay: all eight original persisted decimal averages and the stored original bps reproduced exactly.
- Renderer replay: all bucket counts and first/last bucket boundaries matched exactly; its alternative deterministic tick ordering differed from persisted long-window decimals by at most `8e-25 USD`. The graph therefore uses the exact persisted original decimals above.
- Direct mirrored-tick replay was checked against `A' = 2P0 - A`; maximum absolute decimal accumulation difference was `1e-24 USD`, with unchanged maximum window and signal.
- Final diagnostic project built in Release with `0` warnings and `0` errors.
- Focused production tests for `CryptoReferencePriceAverageCacheTests` and `ProcessAsync_EthNeutral9FakPremarketUsesReferenceAverageMoveSign` passed `4/4`; the build emitted only the repository's existing nullable warnings.
- PNG exists, was visually inspected at its original `2400 × 1800` resolution, is `395,228` bytes, and has SHA-256 `C824A763AAC82B67312348E09678D1129A5F7EE5CD6DE2876F4AD668A7CF2709`.
- The HTML fragment is `261,365` bytes, starts with the required root `<div>`, contains one SVG, has no document wrapper, literal escape artifacts, or dashed-line styling.
- Protected temp cleanup initially met one Roslyn analyzer DLL lock. Process `23944` was verified to have five loaded modules inside the exact marked run directory before only that task-created `VBCSCompiler` was stopped; lifecycle cleanup then removed the marked run and a final check confirmed the directory absent.

## Display semantics and limitation

Each colored average is a horizontal snapshot scalar. Its segment spans the first through last actual ETH sample retained for that window; it is not presented as a historical rolling-average curve. The decision marker is the saved decision clock, while the current price comes from the separately saved Binance trade timestamp stated above.

This graph answers only the signal question. It does not claim mirrored CLOB depth, FAK execution, settlement, or PnL for this one market. Those require the separate mirrored-book assumptions and execution replay documented in `Codex/Tasks/ETH_MIRROR_REPLAY_2026-07-27.md`.
