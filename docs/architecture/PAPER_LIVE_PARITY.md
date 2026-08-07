# Paper/Live Execution Parity Contract

## Purpose

Paper results are valid only when they model behavior that the current Live
Polymarket integration can perform. This contract is mandatory for every strategy,
order type, simulator, execution-policy change, replay, and performance claim.

The governing rule is:

> No proven Live equivalent means no Paper trade.

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

Paper must model the documented behavior of the matching Live order type:

- enforce the same price boundary, liquidity boundary, time-in-force, partial-fill
  behavior, and cancellation behavior;
- never fill liquidity that the admissible market evidence does not contain;
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
rule in this contract.

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

`RealizedPnlUsd` and existing ROI remain gross, before platform fees.
`NetRealizedPnlUsd` is nullable and may be populated only under full fee coverage;
otherwise it remains unknown. No historical fee backfill is part of the current
implementation, so old rows remain `LegacyUnknown`, not zero-fee rows. Aggregate
Dashboard PnL/ROI continues to display the existing gross metric until every
contributing record has full fee coverage and the aggregate can be labeled net
without mixing modeled fees with unaccounted defaults.

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

An algorithm that depends on unavailable Live behavior or hindsight must be
classified `ResearchOnly`. Its records and metrics must be physically or
logically separated from Paper trades, Paper PnL, Paper win rate, and any statement
about expected Live execution. Research output must clearly identify the
counterfactual assumption.

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
5. Run the relevant test suite. Missing evidence, an unsupported guarantee, or a
   failing parity test blocks completion and Paper performance claims.

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
