# Paper/Live Execution Parity Contract

## Purpose

Except for the closed user-approved ordinary-Paper exception defined in this
document, Paper results are valid only when they model behavior that the
current Live Polymarket integration can perform. This contract is mandatory for
every strategy, order type, simulator, execution-policy change, replay, and
performance claim.

The governing rule is:

> Default rule: no proven Live equivalent means no Paper trade.

The default has one closed exception, defined under **Closed user-approved
ordinary-Paper exception** below. It is a classification decision, not evidence
of Live-equivalent fills, and cannot be inferred for any other strategy.

## Verified Polymarket FAK equivalent

The current official Polymarket CLOB V2 documentation defines FAK as an
immediate order that fills available liquidity and cancels the unfilled
remainder. For a BUY market order, `amount` is the cash amount to spend and
`price` is the worst-price limit (slippage protection), not an expected or
target fill price. FAK/FOK orders cannot be post-only.

Authoritative references, verified 2026-07-30:

- [Create Order](https://docs.polymarket.com/trading/orders/create)
- [Orders Overview](https://docs.polymarket.com/trading/orders/overview)
- [Order Lifecycle](https://docs.polymarket.com/concepts/order-lifecycle)

Therefore the supported Live equivalent for a capped Paper FAK BUY is:
`side=BUY`, `orderType=FAK`, `postOnly=false`, a cash amount, and the same hard
maximum order price. Paper may fill only snapshot asks at or below that price;
it must mark an unfilled remainder cancelled immediately.

## Maker post-only GTD intent and Paper approximation

The current official CLOB documentation also supports a share-based `GTD` BUY
with `postOnly=true`. Post-only is valid only with `GTC` or `GTD`; if the order
would match immediately, the venue rejects it instead of executing it as a
Taker. A stated GTD expiration includes the documented one-minute security
threshold. The ETH Reference Average Maker experiment therefore states market
end as the CLOB expiration and stops Paper execution one minute before market
end.

For one placement attempt, a fresh complete decision book `S0` produces the
tick-aligned limit, size, and immutable intent. For the exact ETH exception below,
the approved S0 limit uses the maximum-resting formula
`floor_to_tick(min(S0.bestAsk - S0.tickSize, 0.99))`; for a tick-aligned venue book
this is the highest tick-aligned price strictly below `S0.bestAsk`, and `S0.bestBid`
does not cap it.
Live must submit that unchanged intent. Paper obtains a separate fresh book `S1` only to emulate the venue-side
post-only decision: a BUY limit greater than or equal to `S1.bestAsk` is a
definitive simulated crossing rejection; a lower limit is accepted as
`Resting`. `S1` cannot resize or reprice the frozen intent. A rejected attempt
may create a new intent from a newer `S0`, up to the strategy's explicit limit
of ten attempts. A Live replacement is allowed only after an unambiguous
`INVALID_POST_ONLY_ORDER` crossing rejection with no order identifier; an order
identifier or ambiguous transport/server result requires reconciliation and
must not be retried blindly.

The named Paper-only Maker experiment uses the deliberately optimistic
`TouchNoDepth` outcome model after acceptance. For the exact token, the first
authoritative post-acceptance and pre-expiry `last_trade_price <= limit`, or a
current reconstructed `bestAsk <= limit`, marks the entire Paper order filled
at its own limit. Equality is executable. Queue position, depth, observed trade
size, and aggressor side are ignored by explicit design; consequently this is
not proof that the equivalent Live order would fill or fill completely. Its
performance must be labeled as this approximation and must not be presented as
expected Live execution. Missing/stale timestamp evidence cannot fill an order,
and a reconnect, restart, or other observable continuity loss prevents an
unfilled expiry from being represented as proven continuous observation.

### Closed user-approved ordinary-Paper exception

On 2026-08-09 the user explicitly approved one closed exception that permits this
optimistic model to contribute to ordinary Paper accounting. The exception applies
only when every predicate below is true:

- asset is `ETH`;
- the strategy is a neutral Reference Average Maker-GTD threshold in the exact set
  `1..10` plus `15..100` in steps of `5`;
- behavior is `ReferenceAverageBpsThresholdMakerGtdPremarket`;
- catalog ID is
  `b7c50005-0000-4000-8223-{100+threshold, zero-padded to 12 digits}`;
- persisted execution source is `eth_reference_average_maker_gtd_paper`;
- new placements use `maker_gtd_paper_v2` and S0 pricing exactly
  `floor_to_tick(min(S0.bestAsk - S0.tickSize, 0.99))`;
- the strategy has `PaperOnly=true`, and no Live submission path is enabled.

Exact-family orders and results already persisted under `maker_gtd_paper_v1` are
grandfathered within this same closed exception with their original
`floor_to_tick(min(S0.bestBid + S0.tickSize, S0.bestAsk - S0.tickSize, 0.99))`
pricing. They remain eligible for lifecycle completion and historical accounting;
the persisted contract version and formula distinguish them from v2. Runtime v2
placement is fail-closed unless the exact asset, behavior, ID, threshold, code,
timing, Paper-only flag, and `0.99` cap predicates pass.

The acceptance-evidence lifetime check permits only the verified one-sided
PostgreSQL timestamp round-trip tolerance on its lower bound. JSON
`accepted_at_utc` may be at most five .NET ticks (half a microsecond) earlier than
persisted `paper_orders.created_at_utc`; exactly five ticks passes and six ticks
fails closed as `maker_gtd_evidence_unavailable` with detail
`order_lifetime_mismatch`. Equality and an accepted timestamp later than creation
but before effective expiry remain valid. Effective-expiry equality, the upper
lifetime bound, root/nested accepted-timestamp identity, subscription,
stale/reconnect/market-data continuity, exact-family, TouchNoDepth, pricing,
GTD/PostOnly, PaperOnly, and Live-disabled gates do not change. The correction
does not reopen, replay, or rewrite a record already terminal before deployment.

Expiry evaluation for this exact family uses a starvation-free market-data
receipt handoff. Receipts already active when expiry requests admission finish
first; receipts arriving later wait until the exclusive expiry lease is released
and therefore cannot continually overtake it. Receipt processing remains
concurrent while no expiry request is pending. After admission, the existing
queued/in-flight eligible pre-expiry update check becomes an awaited priority
drain. The affected asset is selected ahead of unrelated asset backlog while its
matching work remains, but FIFO within that asset is preserved, the current
in-flight work item is never interrupted, and each accepted update is processed
exactly once. Only updates received strictly after acceptance and strictly before
effective expiry are eligible. When the drain completes, the same lifecycle pass
rechecks outstanding state and then applies the unchanged evidence parsing,
continuity, fill, and atomic terminal rules. Handler failures publish the existing
failure evidence before the drain completes. No timeout, frame drop, guessed
no-fill, or post-expiry fill evidence is introduced; a genuinely non-completing
in-flight handler remains fail-closed and can still block the affected lifecycle
work.

For these exact 28 strategies, ordinary Paper orders, positions, fills, PnL, win
rate, and performance inclusion is intentional. Every result must carry the label
`optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`. The
exception does not claim that a Live order would fill or fill completely. It does
not relax immutable-intent, PostOnly acceptance, GTD expiry, atomic persistence,
evidence, audit, or testing requirements. No alias, clone, descendant, future
strategy, different execution source, predicate mismatch, or changed execution
semantic inherits the exception; every other unsupported execution model remains
`ResearchOnly`.

Reference Average decision contract v5 changes only the shared pre-execution
signal. Every usable configured average participates in Max/Min selection whether
its window is complete or incomplete, and gaps or incomplete coverage alone do not
block the signal. The explicitly named 3h families remain 3h-only but accept a
usable incomplete 3h average. The exact ETH Maker-GTD family inherits this signal
change only: `maker_gtd_paper_v2` pricing, immutable post-only GTD intent,
acceptance, expiry, `TouchNoDepth` fill/lifecycle rules, `PaperOnly=true`, disabled
Live submission, exact exception predicates, and the mandatory
`optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills` label are
unchanged.

Authoritative references, verified 2026-08-10:

- [Place Orders](https://docs.polymarket.com/trading/place-orders)
- [Orders Overview](https://docs.polymarket.com/trading/orders/overview)
- [Market WebSocket channel](https://docs.polymarket.com/api-reference/wss/market)

## Execution intent

A strategy must create one immutable pre-submit `ExecutionIntent` before Paper or
Live execution begins. It must contain every field that affects venue behavior,
including at least:

- market and token identifiers;
- side and amount semantics (cash amount or share quantity);
- order type and time-in-force;
- requested price limit or worst acceptable price;
- tick-size rounding and the resulting effective price;
- expiry and cancellation conditions when applicable;
- strategy and decision identifiers needed for audit.

Paper simulation and Live submission must consume the same intent. Transport,
authentication, and response mapping may differ; economic and order semantics may
not. A shared default must not overwrite a strategy-specific constraint.
Before Paper simulation, the requested BUY cash amount must be normalized by the
same current amount calculator and precision rules used by the Live request. Both
paths consume that effective amount. Auth, kill-switch, bankroll, exposure, and
other non-market safety gates may still reject it. The Live FAK path must not add
a second order-book lookup merely to revalidate transient liquidity: it submits
the unchanged intent, and the venue determines the actual fill. No preflight may
resize, reprice, or otherwise replace the intent before submission.

Every decision encoded in the intent must use information available before the
submission time. Future book changes, eventual fills, resolution results, or any
other later information are forbidden inputs.

## Execution and outcome rules

Paper must model the documented behavior of the matching Live order type. The exact
closed exception above may depart only from the explicitly qualified rules
below; every other execution and outcome rule still applies:

- enforce the same price boundary, time-in-force, and cancellation behavior;
- enforce the same liquidity boundary and partial-fill behavior, except for the
  exact closed `TouchNoDepth` exception above;
- never fill liquidity that the admissible market evidence does not contain, except
  for the exact closed `TouchNoDepth` inference above;
- never model atomicity, rollback, or conditional acceptance that the venue does
  not provide;
- never treat an aggregate result such as final VWAP as a pre-submit constraint
  unless the Live API can enforce that exact constraint atomically.

Fill price, filled size, VWAP, fees, remainder cancellation, and rejection details
are outcomes. They may update accounting, risk, telemetry, and later decisions.
They must not authorize, reject, resize, reprice, or undo the originating order.

For example, a Live FAK buy with a hard price cap may consume eligible asks and
cancel the remainder. Paper must do the same. Paper must not sweep beyond the cap
and then retain or discard the trade based on the resulting average price.
A delayed Paper worker must not refetch a later order book to simulate an
immediate FAK. It must use the immutable decision-time snapshot; when that
snapshot is absent, the candidate is rejected as non-reproducible and contributes
no Paper fill or PnL.

Until fill, order, position, and copied-position writes share one atomic commit,
an open Paper FAK that already has a persisted fill is a fail-safe no-op. The
processor must neither duplicate uncertain accounting nor terminalize and hide
the order; explicit reconciliation is required.

`PaperOnly` means that the intent is not sent externally. It does not relax any
rule in this contract except the ordinary-Paper classification explicitly granted
to the exact closed exception above; that exception still cannot submit Live.

## BTC/ETH/SOL five-minute Paper settlement authority

The ordinary due-run settlement path keeps the current Gamma result as its primary
authority. It first selects the first metadata row satisfying `Resolved=true` and
a nonblank `WinningOutcome`. When that predicate succeeds, the processor performs
zero `crypto_up_down_5m_websocket_resolved_markets` lookups; a ledger row cannot
confirm, reject, or override the Gamma winner.

Only when Gamma supplies no resolved winner may the processor use the canonical
ledger. The fallback requires exactly one row for normalized reference asset plus
`market_start_utc`, current exact membership in
`StrategyIds.UpDown5mStrategyVariants`, an exact five-minute variant, exact market
id, condition id, slug, start and end, an `Up` or `Down` winning outcome, a
nonempty winning asset, an event timestamp at or after market end, and exact
winning and selected token mappings from the paired
`polymarket_gamma_markets.outcomes_json` and `clob_token_ids_json` arrays. Only
ledger sources `GammaClosedMarket`, `MarketWebSocket`, and `BinanceTimedClose` are
accepted. `ReferenceStartEnd`, `TerminalOrderBook`, unknown sources, absent winner
data, event-before-end rows, duplicate rows, non-five-minute or non-current
variants, and every asset, market, condition, slug, time, outcome, or token
mismatch leave the run `Entered` and create no settlement.

`BinanceTimedClose` is derived from the Binance timed close rather than a direct
Polymarket resolution event. Its user-approved Paper authority exists only in the
fallback above, after Gamma has no winner and all exact validation gates pass. It
may differ from a later Polymarket result, cannot authorize Live trading, and does
not change Live settlement or execution.

Every fallback-settled run persists `settlement_resolution` inside
`skip_diagnostics_json` with
`contract_version=btc_up_down_5m_resolved_ledger_settlement_v1`, ledger id/source,
normalized asset, exact market/condition/slug/start/end, winning outcome/asset,
event timestamp, and `validation_result=exact_identity_token_time_match`. SQL
`NULL` becomes a new JSON object. An existing object keeps every member and gains
the evidence member. Semantically identical existing evidence is accepted only
for idempotent recovery. Conflicting evidence, any valid non-object JSON value
including JSON `null`, or malformed in-memory JSON fails closed: the run remains
`Entered`, no settlement, position, or lost-counter mutation occurs, and the
bounded error includes the run id and rejection detail. PostgreSQL itself cannot
store malformed JSON in the `jsonb` column.

When the existing non-`FixedOutcomeMaker` path creates a
`PaperPositionSettlement`, its source is exactly
`BtcUpDown5mResolvedLedger:GammaClosedMarket`,
`BtcUpDown5mResolvedLedger:MarketWebSocket`, or
`BtcUpDown5mResolvedLedger:BinanceTimedClose`. `FixedOutcomeMaker` and
zero-remaining-position paths still retain the durable run evidence when no
position-settlement row exists. The fallback otherwise reuses the existing fill,
position, stake, fee, Gross, Net, lost-counter, and run-settlement calculations.
Eligible historical `Entered` runs may be processed only by the ordinary worker
after deployment; this contract authorizes no direct history rewrite, database
mutation, or Live behavior change.

## Fee accounting and performance reporting

Platform fees are execution outcomes, not pre-submit decision inputs. Paper and
Live retain the existing gross economics separately from `FeeUsd`; fees must not
be used to authorize, reject, resize, reprice, or undo the originating intent.

The current calculation reads the condition-specific CLOB V2 market record from
`GET /clob-markets/{condition_id}`. Its `fd` object supplies the fee rate `r`,
integer exponent `e`, and taker-only flag `to`. For one fill, the modeled platform
fee is:

`shares * rate * (price * (1 - price))^exponent`

The result is rounded to five decimal places with the versioned local rule
`MidpointRounding.AwayFromZero`; values below `0.00001` become zero. When `fd` is
absent, the market is modeled as fee-free only if both maker and taker base-fee
fields are explicitly present and zero. Missing, invalid, or non-integer schedule
or base-fee evidence produces unavailable accounting rather than a guessed fee.
The authoritative API and formula references are:

- [Get CLOB market info](https://docs.polymarket.com/api-reference/markets/get-clob-market-info)
- [Fees](https://docs.polymarket.com/trading/fees)
- [CLOB V2 migration](https://docs.polymarket.com/v2-migration)

Each fee-bearing record also retains one liquidity role:

- `Maker`: proven post-only/resting execution. It has a calculated zero platform
  fee when `fd.to=true`.
- `Taker`: FAK/FOK or other explicitly persisted taker execution.
- `Unknown`: ambiguous non-post-only resting execution or contradictory evidence.
  A non-zero applicable schedule cannot be calculated from this role.

Fee coverage is never inferred from the numeric default of `FeeUsd`:

- `LegacyUnknown`: the row has not been evaluated by the current model. This is
  the status for retained historical rows unless they are explicitly backfilled.
- `CalculationUnavailable`: evaluation was attempted, but the required market
  schedule, liquidity role, price, shares, or other evidence was missing or
  invalid.
- `Calculated`: a deterministic result from the stored fill and public per-market
  schedule, including a calculated zero-fee result. The explicitly approved
  terminal run-level ratio fallback below also uses this status even though its
  Fee is approximate; its distinct source preserves the calculation method.
- `VenueReported`: an authoritative fee supplied by the venue, not by the local
  model.
- `PartiallyCalculated`: an aggregate contains at least one accounted child and
  at least one legacy, unavailable, or already-partial child.

An aggregate is fully fee-accounted only when every contributing child is
`Calculated` or `VenueReported`; all-venue children remain `VenueReported`, while
a fully accounted mix is `Calculated`. A partial aggregate may retain the sum of
known fee components, but it must not expose that sum as a complete net result.

`RealizedPnlUsd`, `UnrealizedPnlUsd`, and the existing gross ROI fields remain
unchanged audit values before platform fees. `NetRealizedPnlUsd` and other net
aggregates are nullable and may be populated only under full fee coverage for
their exact scope; otherwise they remain unknown. The Dashboard uses those
nullable net values as the primary strategy metrics, leaves incomplete values
blank, and displays `accounted/required` coverage. A `0/0` scope is `N/A`, not an
unknown fee coerced to zero. Known accounted-fee sums may still be shown for a
partial scope, but they are not a complete fee total.

Net ROI uses fee-inclusive cash outlay as its denominator: the gross stake or
cost plus the fully accounted platform fee. Gross PnL and gross ROI remain
explicitly labeled secondary audit columns in the Dashboard and CSV exports;
they must not be labeled or interpreted as net profitability.

The online `PaperFakFeeBackfill` worker may evaluate retained historical rows only
when persisted execution evidence proves a pure-Paper BUY FAK. Its allowlist is
limited to `btc_updown5m_fak_taker_paper` and
`btc_updown5m_child_mirror_fak_paper`, before the configured fixed historical
cutoff. It uses the same current fee calculator as new fills, forces the proven
`Taker` role, and prefixes locally calculated provenance with
`historical-current-paper-model-v1`. This is a calculation under the current
Paper model, not a venue-reported historical fee.

Backfill writes are small, atomic, conditional, and idempotent. Exactly two
dependency shapes are accepted:

- `FullChain` requires the unchanged exact fill/run/zero-size-position/settlement
  chain. The settlement source/time is either exact
  `BtcUpDown5mGammaClosedMarket` at the run settlement time or exact
  `MarketWebSocket` at or before the run settlement time. All identity,
  economic, uniqueness, and accounting guards remain mandatory; only fee,
  provenance, and nullable net fields on fill, run, position, and settlement are
  updated.
- `RunOnlyLegacy` requires exactly one unchanged, settled, economically
  self-consistent run and zero position and settlement rows. Only fill and run
  fee/provenance/net fields are updated; missing rows are never synthesized.

After that exact allowlisted phase, the same Gross-ranked worker has a separate
canonical-run phase for every strategy. It covers all historical and future
`Settled` Paper runs with positive stake and non-null Gross, without the exact
phase's cutoff or execution-source allowlist. An authoritative nonnegative Fee
already marked `Calculated` or `VenueReported` is repaired first by deriving
only `Net = Gross - Fee`; Fee, status, source, and fee metadata remain exact and
unchanged. A run still incomplete after the exact paths may then use a bounded
same-strategy lifetime ratio. Each transaction recomputes
`R = SUM(exact Fee) / SUM(exact positive Stake)` from complete `Calculated` or
`VenueReported` donor runs satisfying `Net = Gross - Fee`; prior ratio results
are excluded. Without a valid donor or positive aggregate donor stake, no run is
changed.

For an eligible fallback target, the approximate Fee is
`ROUND(Stake * R, 8)` and Net is exactly `Gross - Fee`. Only the canonical
strategy run is updated. It is stored as ordinary `Calculated` with the exact
case-sensitive source `strategy-settled-fee-stake-ratio-v1`, receives no visible
`Estimated` status or label, and is terminal: the worker never revisits it when
donors change or exact evidence becomes available. Related fill, order,
position, and settlement rows are deliberately not synthesized or updated and
may therefore retain earlier accounting or blank Net in detailed exports. The
run-only divergence is an accounting/reporting choice and does not change the
execution intent, Paper fills, Live submission, Live accounting, or risk gates.

Gross accounting and timestamps remain unchanged. Item-level exact-phase
structural or accounting conflicts advance its cursor after completed SQL;
eligible canonical runs can subsequently enter the separate run-level phase.
A whole-batch advisory-lock timeout or query cancellation does not advance the
applicable cursor, so the same work is retried. Transport failures, programming
errors, and service cancellation are operational deferrals and never create a
financial estimate. Compare-and-set protection leaves a concurrently completed
exact run untouched. The worker yields to foreground persistence queues and
does not persist transient market-info failures as zero fees. Gross ordering
and the Dashboard Gross/Net PnL and Net ROI aggregate formulas remain unchanged;
the fixed cutoff, source allowlist, candidate filters, and exact financial
formula remain unchanged for the exact phase. Reaching the end of a keyset sweep
is not proof that every unresolved row was successfully accounted.

Historical GTD, Maker, ambiguous, already-accounted, and
`paper_live_shadow_actual_fill` rows remain outside the exact fill-recalculation
phase. Complete Paper runs from those families may be exact donors for the
separate canonical-run phase, and financially incomplete `PaperOnly` runs may be
fallback targets. Runs classified `LiveOrShadow` remain outside that phase.
Current shadow semantics calculate the fee on the aggregate linked Live execution
and copy that accounting into one canonical Paper fill. Independently
recalculating a legacy shadow Paper row could disagree with Live cost basis,
settlement, balance effects, or canonical multi-fill replacement, so those rows
require a separate Live-accounting reconciliation rather than a Paper-only
estimate.

### Historical Gross/Net contribution parity

The separate `HistoricalGrossNetParity` workflow is an accounting-only repair
for originating entries strictly before `2026-08-10T00:00:00Z`. It mirrors the
existing Gross branch instead of inventing a second trade population: a
Gross-selected contribution receives exactly one Fee/Net contribution, while a
Gross-excluded row contributes no Net requirement and cannot blank the strategy.
Gross values, ROI bases, fills, execution intents, prices, sizes, settlement
facts, and recent-window membership remain unchanged.

The canonical set is run-backed Settled Paper runs, positive open Paper
positions, the existing runless settlement/SELL fallback, and counted settled
Live orders. `usesRuns` retains the current raw-run plus compacted-skip-rollup
meaning. Paper recent windows still use Settled-run facts only; open and runless
fallback accounting remains lifetime/MtM. Live remains settled-realized only;
no Live open-MtM metric is added.

Accounting precedence is proved `VenueReported`, complete exact local evidence,
authoritative-Fee Net repair, exact historical CLOB calculation, deterministic
exact-donor Fee-to-basis ratio, and finally fixed `0.0333`. The donor matcher
uses typed catalog semantics rather than names, resolves same-strategy and
nearest-family tiers deterministically, then falls through to any proved crypto
strategy before the fixed coefficient. It deduplicates linked Paper/Live
evidence and never lets estimated rows donate. Final Fee is rounded once to eight
decimal places away from zero, cannot fall below proved non-overlapping Fee
components, and Net remains exactly `Gross - Fee`. A fallback is stored as
ordinary terminal `Calculated` with versioned provenance; Gross is never
rewritten.

Paper pooled lineage is replayed with the persisted engine rounding and
proportional entry-Fee allocation. Every contributing originating BUY must be
pre-cutoff. Mixed/unproved origin, overlapping Fee evidence, unexpected nonzero
runless BUY PnL, or a Settled run whose projected Gross is not explicitly stored
as zero is a fail-closed conflict, not an estimate.

For Live, the workflow uses only authoritative Fee already associated with the
order; it does not create an on-chain matcher. A modeled public-schedule Fee is
`Calculated`, never `VenueReported`. A strictly newer associated
`VenueReported` revision may supersede earlier non-authoritative accounting and
applies an audited cumulative balance correction. Historical balance writes are
serialized in settlement order, clamped by the existing balance bounds, and do
not toggle Live, alter loss counters, pause trading, or emit ordinary settlement
notifications.

The deployed service performs this historical repair incrementally. It first
reaches the current exact/authoritative/local-calculation boundary, then handles
each unresolved fallback target in Gross order. Donor candidates are generated
from typed strategy descriptors and queried only for finite strategy-ID pages;
there is no full donor-universe scan, frozen universe, or durable global plan.
The target-time aggregate and the complete winning/absence proof are revalidated
inside the target's serializable accounting transaction. A conflict retries only
that target, while independent committed targets remain complete. Restarts rescan
unresolved/Pending canonical state and permanent audit.

Different targets can therefore observe different exact donor membership while
the service progresses; the stored numerator, denominator, counts, deterministic
membership/selection hashes, ratio, and provenance preserve each decision. A
terminal Paper estimate is not recalculated when later exact donors appear. Live
initial balance effects remain ordered by settlement time and UUID per strategy:
an earlier unfinished row gates later initial balance transactions for that
strategy, while accounting and other strategies continue. The background cadence
and projection reconciliation can leave Net temporarily blank until the relevant
target and snapshot refresh complete.

Linked Live/Paper donor deduplication is replay-based, not inferred from a
shared wallet/asset name. If an exact Live order is still represented inside a
composite Paper position or settlement and the aggregate rounding residual
cannot be split between linked and unlinked BUY charges without inventing
per-BUY economics, the exact Live row is retained and the entire indivisible
Paper composite is excluded from that target-time donor aggregate. Matching
continues through the remaining tiers and fixed `0.0333`. This prevents double
counting and preserves Live precedence, but can omit otherwise exact unlinked
Paper economics from `N/D` compared with a hypothetical exactly partitioned
aggregate. No synthetic residual Paper row is created. A fully consumed older
linked order that replay proves is absent from the remaining composite does not
cause that exclusion.

The current model has five material limits:

- a Paper depth sweep can be stored as one aggregate VWAP fill, while the
  nonlinear curve and per-result rounding can differ from a sum over individual
  matches;
- a locally modeled result remains `Calculated`, never `VenueReported`, because
  the public formula does not independently establish the venue's exact
  midpoint-tie behavior for every execution;
- maker rebates and builder-attribution fees are excluded. Rebates need a
  separate authoritative payout ledger, and builder fees need their own evidence
  and calculation rather than being folded into the CLOB platform-fee field.
- an indivisible Paper composite overlapping an exact linked Live row is
  excluded in full when its nonlinked residual cannot be proved exactly; the
  retained Live row prevents duplicate accounting, but donor `N/D` may omit
  exact nonlinked Paper economics contained in that same aggregate;
- legacy Paper/Live-shadow replacement inside an aggregate with additional
  non-shadow size cannot reconstruct the removed component's provenance because
  no versioned pre-shadow fee snapshot exists. The aggregate must therefore stay
  conservatively partial/unknown with nullable net PnL instead of inferring full
  coverage from a numeric fee subtraction.

## Research-only algorithms

Except for the exact closed user-approved ordinary-Paper exception above, an
algorithm that depends on unavailable Live behavior or hindsight must be classified
`ResearchOnly`. Its records and metrics must be physically or logically separated
from Paper trades, Paper PnL, Paper win rate, and any statement about expected Live
execution. Research output must clearly identify the counterfactual assumption.

A research result may become Paper only after its Live equivalent is documented,
implemented through the common intent path, and covered by the parity gate below.

## Mandatory parity gate

Before completing a new or changed Paper execution feature:

1. Document the exact Live API mechanism and authoritative venue behavior for each
   material intent field and execution guarantee.
2. Inspect the actual Paper and Live dispatch paths and prove that both preserve
   the same intent. Names, comments, and strategy descriptions are not proof.
3. Add focused contract tests that compare the intents and cover, as applicable:
   price boundaries, tick rounding, full fill, partial fill, no fill, and remainder
   cancellation.
4. Add a regression test showing that post-fill data cannot alter acceptance of
   the originating order.
5. Run the relevant test suite. Outside the exact closed exception, missing
   evidence, an unsupported guarantee, or a failing parity test blocks completion
   and Paper performance claims. For the exception, a predicate mismatch, missing
   mandatory label, enabled Live path, or failing exception contract test blocks
   completion and ordinary Paper performance claims.

## Persistence and audit evidence

Each Paper execution candidate must be reproducible from durable evidence. Persist
or durably reference:

- the complete requested and effective `ExecutionIntent`;
- strategy version and decision inputs;
- submission/decision timestamp and market-data timestamp;
- the order-book snapshot or immutable snapshot reference used by the simulator;
- all simulated fills, fees, partial-fill and cancellation details;
- rejection or no-fill reason and final accounting outcome;
- correlation identifiers connecting the decision, intent, execution, and result.

The execution-intent decision identifier is an audit/correlation identifier and
does not have to equal the Paper signal identifier. Persisted timestamp identity
checks use PostgreSQL's microsecond precision. A snapshot obtained by an awaited
REST fallback can legitimately be later than the strategy-cycle timestamp when
the complete intent and snapshot are persisted atomically in the same decision
payload.

Audit records must not contain private keys, signatures, credentials, or other
secrets. If the required market evidence was not retained, the result must be
marked non-reproducible; it must not be represented as an exact replay.

A pre-intent strategy observation that terminates without a signal, execution
intent, Paper/DryRun/Live order, fill, position, settlement, or Live-shadow
decision is not a Paper execution record. Such a proven `PaperOnly` no-bet
`Skipped` observation may be replaced atomically by a durable deduplication and
restoration marker plus an aggregate skip count. The marker must retain the exact
strategy, market, condition, timestamps, stake, and skip reason needed by runtime
deduplication and Dashboard accounting. Any execution or accounting dependency,
and every `LiveOrShadow` run, keeps the complete raw run and all execution audit
evidence.
