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
- Do not silently ignore API errors; persist/log explicit failure reasons.
- Run tests before declaring implementation tasks complete.
- Update README/project memory/task context when behavior changes.
