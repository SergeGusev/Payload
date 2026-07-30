# PolyCopyTrader Coding Rules

These rules summarize the project-local engineering constraints. `AGENTS.md`
contains the full safety and project rules.

- Use C#/.NET native code only unless the user explicitly changes that rule.
- Use WPF for the dashboard and Worker Service / Windows Service for the 24/7
  engine.
- Use PostgreSQL for persistence, Serilog for logs, CommunityToolkit.Mvvm for
  MVVM, dependency injection, nullable reference types, and async/await.
- Keep domain logic independent from WPF.
- Keep Polymarket API clients separate from strategy logic.
- Keep `PolyCopyTrader.Strategy` independent from
  `PolyCopyTrader.Polymarket`; orchestration belongs in service/application
  layers.
- Never request, print, store, log, or commit private keys or secrets.
- Do not implement new live order placement unless the active user task
  explicitly requests it.
- Default runtime posture is `ReadOnly` or `Paper`.
- Follow the mandatory Paper/live execution contract in
  `docs/architecture/PAPER_LIVE_PARITY.md`.
- Build one pre-submit `ExecutionIntent` for both Paper and Live; do not let either
  path silently replace its price limit, size, side, order type, time-in-force, or
  other execution semantics.
- Treat fills and VWAP as outcomes for accounting and future decisions only. Never
  use post-fill data to authorize or roll back the originating Paper order.
- Never re-fetch an order book after freezing a FAK/FOK intent in order to validate
  liquidity, resize, or reprice it. Local request-shape validation may only accept
  or reject the unchanged intent; the venue determines the actual fill.
- `PaperOnly` is a transport restriction, not a semantic exemption. Logic without
  a proven Live equivalent is `ResearchOnly` and must not contribute to Paper PnL.
- Do not silently ignore API errors; persist/log explicit failure reasons.
- Run tests before declaring implementation tasks complete.
- Update README/project memory/task context when behavior changes.
