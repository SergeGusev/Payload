# Paper/Live Execution Parity Contract

## Purpose

Except for the closed user-approved ordinary-Paper exceptions defined in this
document, Paper results are valid only when they model behavior that the
current Live Polymarket integration can perform. This contract is mandatory for
every strategy, order type, simulator, execution-policy change, replay, and
performance claim.

The governing rule is:

> Default rule: no proven Live equivalent means no Paper trade.

The default has two closed exceptions, defined under **Closed user-approved
ordinary-Paper exceptions** below. They are classification decisions, not evidence
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

### Closed user-approved ordinary-Paper exceptions

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

For these exact 28 strategies, ordinary Paper orders, positions, fills, PnL, win
rate, and performance inclusion is intentional. Every result must carry the label
`optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`. The
exception does not claim that a Live order would fill or fill completely. It does
not relax immutable-intent, PostOnly acceptance, GTD expiry, atomic persistence,
evidence, audit, or testing requirements. No alias, clone, descendant, future
strategy, different execution source, predicate mismatch, or changed execution
semantic inherits the exception; every other unsupported execution model remains
`ResearchOnly`.

On 2026-08-09 the user explicitly approved a second closed exception for the exact
six five-minute paired Maker-GTD legs in catalog group `8224`. It applies only when
all of these predicates hold:

- asset is exactly `BTC`, `ETH`, or `SOL`, with one fixed `Up` and one fixed `Down`
  leg per asset;
- behavior is `PairedFixedOutcomeMakerGtdFirstAccepting`, interval is five minutes,
  and timing is `FirstAcceptingOrders` with nominal metadata delay `-86400` seconds;
- catalog ID suffixes are exactly `101/102`, `201/202`, and `301/302`, and each pair
  has symmetric `PairedStrategyId` links;
- persisted source is `crypto_paired_maker_gtd_first_accepting_paper`, new
  placements use contract `paired_maker_gtd_paper_v2`, and `PaperOnly=true`;
- S0 price is `floor_to_tick(min(S0.bestAsk-S0.tickSize, cap))`, with Up cap `0.50`
  and Down cap `0.49`, so accepted pair limits cannot exceed `0.99` in total;
- each v2 direct HTTP S0/S1 read persists and validates one ordered local bracket:
  request start, client receipt, response completion, and evaluation. Both receipt
  age and request duration must remain within the configured maximum. The venue
  snapshot timestamp and hash remain audit evidence and the timestamp must be
  authoritative and no later than local receipt, but an old unchanged snapshot
  does not by itself make a freshly fetched quiet book stale. Exact-family v1
  orders already persisted under the former venue-source-age rule remain
  grandfathered for fill/expiry and historical accounting;
- one common CLOB-normalized share quantity is frozen from both valid S0 sizing
  results before either leg is attempted. Each leg then has independent S0/S1
  PostOnly attempts and independent persistence. The venue provides no pair
  atomicity: rejection or non-fill of one leg never rolls back, cancels, or resizes
  an accepted peer;
- accepted legs use market end as wire GTD expiry and stop Paper execution one
  minute earlier;
- a TouchNoDepth fill additionally requires the same market-data service session
  and uninterrupted healthy owning-shard component plus per-asset subscription
  generation from acceptance through the triggering event; a service restart,
  owning-shard reconnect, or asset reassignment fails closed as evidence unavailable.

For this exact family, ordinary Paper orders, positions, fills, PnL, win rate, and
performance inclusion is intentional. Queue position, book depth, event size, and
aggressor remain ignored, so inferred full fills may still be overstated. Every
result carries `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`.
Maker rebates are not modeled, inferred, or included in Paper PnL; eligibility and
amount require an authoritative venue payout ledger. Live submission remains
disabled. No alias, clone, different source, changed cap/timing/pair link, or other
predicate mismatch inherits this exception.

Authoritative references, verified 2026-08-09:

- [Create Order](https://docs.polymarket.com/trading/orders/create)
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
closed exceptions above may depart only from the explicitly qualified rules
below; every other execution and outcome rule still applies:

- enforce the same price boundary, time-in-force, and cancellation behavior;
- enforce the same liquidity boundary and partial-fill behavior, except for the
  exact closed `TouchNoDepth` exceptions above;
- never fill liquidity that the admissible market evidence does not contain, except
  for the exact closed `TouchNoDepth` inferences above;
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
to the exact closed exceptions above; those exceptions still cannot submit Live.

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
  schedule, including a calculated zero-fee result.
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

Backfill writes are small, atomic, conditional, and idempotent. They may update
only fee/provenance and nullable net fields on an unchanged fill and its exact
settled run, zero-size position, and settlement chain. Gross accounting and its
timestamps remain unchanged. The worker yields to foreground persistence queues,
does not persist transient market-info failures as zero fees, and reports
dependency or concurrency mismatches as deferred conflicts. Reaching the end of
a keyset sweep is not proof that every legacy row was successfully accounted.

Historical GTD, Maker, ambiguous, already-accounted, and every
`paper_live_shadow_actual_fill` row are outside this worker. Current shadow
semantics calculate the fee on the aggregate linked Live execution and copy that
accounting into one canonical Paper fill. Independently recalculating a legacy
shadow Paper row could disagree with Live cost basis, settlement, balance effects,
or canonical multi-fill replacement, so those rows require a separate
Live-accounting reconciliation rather than a Paper-only estimate.

The current model has three material limits:

- a Paper depth sweep can be stored as one aggregate VWAP fill, while the
  nonlinear curve and per-result rounding can differ from a sum over individual
  matches;
- a locally modeled result remains `Calculated`, never `VenueReported`, because
  the public formula does not independently establish the venue's exact
  midpoint-tie behavior for every execution;
- maker rebates and builder-attribution fees are excluded. Rebates need a
  separate authoritative payout ledger, and builder fees need their own evidence
  and calculation rather than being folded into the CLOB platform-fee field.
- legacy Paper/Live-shadow replacement inside an aggregate with additional
  non-shadow size cannot reconstruct the removed component's provenance because
  no versioned pre-shadow fee snapshot exists. The aggregate must therefore stay
  conservatively partial/unknown with nullable net PnL instead of inferring full
  coverage from a numeric fee subtraction.

## Research-only algorithms

Except for the exact closed user-approved ordinary-Paper exceptions above, an
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
5. Run the relevant test suite. Outside the exact closed exceptions, missing
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
