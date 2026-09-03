# AGENTS.md

## Context persistence workflow

Within repository-local instructions, `Codex/Rules/Workflow.md` is the
authoritative workflow contract for context recovery, task initialization, active
context updates, and daily history. Higher-priority runtime instructions and the
safety rules in this file still apply.

ActiveContextFile: Codex/Contexts/ContextPolyCopyTrader.md

## Project

This repository contains **PolyCopyTrader**, a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

## User interaction rules

- When the user asks to inspect a picture/image/screenshot, assume the image is in the Windows clipboard unless the user explicitly provides another source. First try to extract the clipboard bitmap to a temporary image file and inspect it.
- Immediately report unexpected tooling, permission, environment, or runtime obstacles that block or materially delay the task. Name the exact stage and error, state whether user systems or data are affected, and describe the bounded workaround. Do not repeat a failing operation silently or leave the user waiting through unexplained retries.
- Never use dashed, dotted, or dash-dot lines in charts. Distinguish series with solid colors, direct labels, and markers instead.

## Operational scope lock and execution gates

- Before any repository mutation, read and follow
  `Codex/Rules/RequirementGate.md`. Create a machine-readable requirement
  contract from the user's verbatim words, present its semantic digest, and
  obtain the user's later approval before material edits. Every assumption or
  deviation requires its own explicit user approval; technical preference,
  conservatism, safety language, convention, and prior chat summaries are not
  authorization to change requested behavior.
- Treat the requirement contract as fail-closed. If implementation discovers a
  choice that could change behavior, scope, data, risk, cost, or acceptance
  criteria, stop, revise the contract, and obtain approval for the new digest.
  Before completion, require an independent reviewer to compare the verbatim
  request and approved contract against the actual diff and verification.
- Before any non-trivial task, explicitly lock scope before material work. State the goal, exact in-scope entities, out-of-scope entities, period/window/filter, mode (`read-only`, local edit, or mutation), expected scale if known, and the first verification step.
- Do not start material work when a missing choice could change meaning, risk, cost, data touched, or runtime behavior. Ask the smallest focused question instead.
- Never substitute one meaning of a term for another. In particular, a strategy lookback window, calculation window, or chart window is not an analysis period unless the user explicitly says so.
- For production, trading, financial, database, statistical, deletion, deployment, security, or service-state tasks, run a read-only preview first. The preview must include exact identifiers, row counts or candidate counts, period/timezone, key filters, and dependency/risk checks relevant to the requested action.
- Compare preview counts and scale against the user's stated or implied expectation before drawing conclusions or mutating anything. If the result is surprising or inconsistent, halt, report the mismatch, and resolve scope before continuing.
- If new evidence contradicts an earlier premise, invalidate every downstream conclusion based on that premise until rechecked. Do not patch only the final sentence.
- Do not convert a bounded task into a framework, broad audit, generalized migration, backup, deployment, service stop, live-order action, cleanup campaign, or long-running job unless the user explicitly requests or approves that exact expansion.
- Use the simplest working method that satisfies the locked scope. Optional hardening, broader verification, and reusable tooling are separate work and require explicit approval when they add material time or risk.
- A communicated time estimate is for transparency, not an automatic stop condition. If the approved task is still running within the same locked scope, method, risk profile, and resource usage, continue and report the updated ETA. Stop and ask only when continuing requires additional resources, new workstreams, materially different methods, broader verification, higher risk, writes/mutations/deployments/backups/external actions, or a user-specified hard time limit would be exceeded.

## Gentle production operations

- This is a standing PolyMarket rule for every task that can affect the running
  production service or production database. Use the least disruptive practical
  execution mode, minimizing database load, lock duration, service interference,
  and latency impact even when a gentler operation takes longer.
- Before mutation, run a read-only preview and report the exact candidate count,
  proposed batch size and batch count, transaction boundaries, expected lock/load
  profile, and the durable progress or resume point. Choose batch size from this
  evidence and current production state; do not apply a universal fixed batch
  size blindly.
- A non-trivial production mutation defaults to short, bounded batches instead of
  one transaction covering the entire operation. Each batch must be independently
  atomic, idempotent, and safely restartable from an auditable durable progress
  point. Do not create generalized batching infrastructure or schema objects
  unless the user separately approves that scope.
- Between batches, verify service heartbeat and errors, waiting locks, actual
  affected counts, and task-specific invariants. Stop before the next batch when
  any value deviates from the preview or when production health worsens; report
  the exact completed and remaining scope.
- A single large all-or-nothing production transaction is not the default. If a
  verified correctness invariant cannot be preserved by batching, stop and
  explain that exact invariant, affected scale, expected duration, lock/load
  impact, failure recovery, and narrower alternatives. Obtain the user's explicit
  approval for the large transaction before running it; technical preference or
  generic safety language is not approval.
- Never introduce a service stop or restart, broad table lock, trigger bypass,
  schema change, backup, or similarly high-impact mechanism as a hidden batching
  aid. Each remains a separate operation requiring its own evidence and explicit
  authorization under the existing scope gates.

## Core principle

This is **not** a blind copy-trading bot.

Leader trades are signal candidates, not commands. The bot may act only when category, freshness, price, spread, liquidity, and portfolio risk filters pass.

## Safety rules

- Never request, print, store, or log private keys.
- Never commit secrets.
- Never implement live order placement unless the active task explicitly requests it.
- Default mode must be `ReadOnly` or `Paper`.
- Any future live trading must include kill switch, cancel-all, small trade sizes, risk limits, and explicit manual enablement.
- The WPF dashboard must not be required for the background service to keep running.
- The service must be able to run 24/7 on a Windows VPS.
- Do not use Python, Node.js, TypeScript, or sidecars unless a later explicit task changes this. This project is C#/.NET native.

## Paper/live execution parity

Closed historical accounting exception approved by
`RC-20260903-eth-progress34-native-history`, digest
`d2ec671347eb083cab33ab7ed9c67280e6f8887eba06bcae14b2e6eae57602f2`:
only children `b7c50005-0000-4000-8236-{cap:12 digits}` caps1..16 of parent
`b7c50005-0000-4000-8137-000000000104`, and
`b7c50005-0000-4000-8237-{cap:12 digits}` caps1..18 of parent
`b7c50005-0000-4000-8137-000000000108`, entered strictly before
`2026-09-03T05:32:51.200614Z`, source
`eth_lossdiff_positive_progress_history_research_paper`, evidence
`eth_progress34_parent_average_full_fill_history_v1`. This command's
sufficient-depth, parent-average full-fill model retains `ResearchOnly`
provenance with `ordinary_paper_metrics_included=true` and enters native Paper
history/counts/WinRate/Net PnL/fee-inclusive Net ROI. Own fees use recorded
schedules, including explicitly accepted retrospective modeled schedules, not
claimed historical venue charges. This exception overrides separation only for
those imported records; it proves neither depth nor Live equivalence. Current
counters, post-rollout trades and actual-depth Paper/Live execution stay unchanged.
No clone, future trade or other strategy inherits it; the existing28-family
exception and every other parity rule remain unchanged.

- Except for the closed user-approved ordinary-Paper exception enumerated below,
  a Paper strategy may model only an order and execution sequence that the
  current Live Polymarket API can perform with the same pre-submit constraints and
  order semantics. See `docs/architecture/PAPER_LIVE_PARITY.md`.
- Strategy logic must produce one pre-submit `ExecutionIntent`. Paper simulation
  and Live submission must consume that same intent without changing its side,
  size, price limit, order type, time-in-force, or other execution constraints.
- Fill data, including fill prices, filled size, and VWAP, is outcome data. It may
  be used for accounting and later decisions, but never to authorize, reject,
  alter, or roll back the order that produced it.
- After an immediate FAK/FOK intent is frozen, the Live path must not fetch a
  newer order book merely to validate liquidity, resize the amount, or reprice
  the order. Submit the unchanged hard-limit intent and let the venue determine
  the fill. Purely local payload validation (price/tick/size/format) is allowed;
  it must not read market data or change the intent.
- `PaperOnly` disables external submission; outside the closed exception below, it
  does not permit execution semantics that are unavailable in Live. Counterfactual
  logic without a proven Live equivalent must be classified `ResearchOnly` and
  excluded from Paper PnL and Paper performance claims.
- Closed exception approved explicitly by the user on 2026-08-09: ordinary Paper
  accounting is allowed only when every predicate is true: asset `ETH`; neutral
  Reference Average Maker-GTD thresholds `1..10` and `15..100` in steps of `5`;
  behavior `ReferenceAverageBpsThresholdMakerGtdPremarket`; catalog ID
  `b7c50005-0000-4000-8223-{100+threshold, zero-padded to 12 digits}`; persisted
  execution source `eth_reference_average_maker_gtd_paper`; and `PaperOnly=true`.
  New placements use contract `maker_gtd_paper_v2` with S0 pricing exactly
  `floor_to_tick(min(S0.bestAsk - S0.tickSize, 0.99))`. Exact-family records
  already persisted under `maker_gtd_paper_v1` remain grandfathered under their
  original `floor_to_tick(min(S0.bestBid + S0.tickSize, S0.bestAsk - S0.tickSize,
  0.99))` pricing so their lifecycle and historical accounting are not orphaned;
  the persisted contract version and formula distinguish the two regimes.
  These exact 28 PaperOnly strategies intentionally contribute orders, positions, PnL, win rate,
  and performance to ordinary Paper metrics even though their optimistic
  `TouchNoDepth` full-fill inference is not Live-equivalent and may overstate
  fills. Every result must carry the label
  `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`. Live
  submission remains disabled. No
  alias, clone, descendant, future strategy, or changed execution semantic inherits
  this exception; all other unsupported behavior remains `ResearchOnly`.
- Never simulate atomicity, rollback, post-fill rejection, or aggregate fill-price
  guarantees unless the Live venue explicitly provides that guarantee for the
  same order type.
- Outside that exact closed exception, a new or changed Paper execution rule is
  incomplete until its Live equivalent is documented, parity tests pass, and the
  execution intent, decision inputs, market snapshot reference, fills, and outcome
  are persistable for audit. The exception still requires focused contract tests
  and persistable intent, evidence, fills, and outcomes.

## Engineering rules

- Use C#/.NET.
- Use WPF for the dashboard.
- Use a background Worker Service / Windows Service for the 24/7 engine.
- Use PostgreSQL for MVP persistence.
- Use Serilog for logs.
- Use CommunityToolkit.Mvvm for MVVM.
- Keep domain logic independent from WPF.
- Keep Polymarket API clients separate from strategy logic.
- Keep `PolyCopyTrader.Strategy` independent from `PolyCopyTrader.Polymarket`; orchestration belongs in the service/application layer.
- Use typed models, not dynamic JSON, except for temporary diagnostics.
- Use dependency injection.
- Use nullable reference types.
- Use async/await correctly.
- Add unit tests for strategy, risk, paper trading, and signing logic when introduced.
- Do not silently ignore API errors.
- Log rejected signals with explicit rejection reasons.

## Polymarket strategy rules

Default strategy:

- Paper trading only until live trading is explicitly requested.
- Maker-style simulated entries only in MVP.
- No taker market buys by default.
- No live trading in MVP.
- Do not chase price.
- Max slippage from leader price must be configurable.
- Max spread must be configurable.
- Trade only allowed categories per tracked trader.
- Risk limits must be checked before creating any paper/live order.

## Testing

Run tests before declaring a task complete. Add tests when changing:

- `SignalEngine`
- `RiskEngine`
- `PaperTradingEngine`
- API response parsing
- WebSocket event parsing
- Auth/HMAC/EIP-712 signing logic

## Documentation

Keep README and task status updated. Each implementation task should end with:

- What changed
- How to run it
- How to test it
- Known limitations
- Next recommended task
