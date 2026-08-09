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

- Before any non-trivial task, explicitly lock scope before material work. State the goal, exact in-scope entities, out-of-scope entities, period/window/filter, mode (`read-only`, local edit, or mutation), expected scale if known, and the first verification step.
- Do not start material work when a missing choice could change meaning, risk, cost, data touched, or runtime behavior. Ask the smallest focused question instead.
- Never substitute one meaning of a term for another. In particular, a strategy lookback window, calculation window, or chart window is not an analysis period unless the user explicitly says so.
- For production, trading, financial, database, statistical, deletion, deployment, security, or service-state tasks, run a read-only preview first. The preview must include exact identifiers, row counts or candidate counts, period/timezone, key filters, and dependency/risk checks relevant to the requested action.
- Compare preview counts and scale against the user's stated or implied expectation before drawing conclusions or mutating anything. If the result is surprising or inconsistent, halt, report the mismatch, and resolve scope before continuing.
- If new evidence contradicts an earlier premise, invalidate every downstream conclusion based on that premise until rechecked. Do not patch only the final sentence.
- Do not convert a bounded task into a framework, broad audit, generalized migration, backup, deployment, service stop, live-order action, cleanup campaign, or long-running job unless the user explicitly requests or approves that exact expansion.
- Use the simplest working method that satisfies the locked scope. Optional hardening, broader verification, and reusable tooling are separate work and require explicit approval when they add material time or risk.
- A communicated time estimate is for transparency, not an automatic stop condition. If the approved task is still running within the same locked scope, method, risk profile, and resource usage, continue and report the updated ETA. Stop and ask only when continuing requires additional resources, new workstreams, materially different methods, broader verification, higher risk, writes/mutations/deployments/backups/external actions, or a user-specified hard time limit would be exceeded.

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

- Except for the single closed user-approved ordinary-Paper exception enumerated
  below, a Paper strategy may model only an order and execution sequence that the
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
