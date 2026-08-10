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
- `PaperOnly` is a transport restriction, not a general semantic exemption. Logic
  without a proven Live equivalent is `ResearchOnly` and must not contribute to
  Paper PnL except for the closed exceptions explicitly approved by the user
  on 2026-08-09: the exact 28 `ETH` neutral Reference Average Maker-GTD strategies
  at thresholds `1..10` and `15..100` step `5`, behavior
  `ReferenceAverageBpsThresholdMakerGtdPremarket`, catalog ID
  `b7c50005-0000-4000-8223-{100+threshold, zero-padded to 12 digits}`, and execution
  source `eth_reference_average_maker_gtd_paper`, and `PaperOnly=true`. New
  placements use contract `maker_gtd_paper_v2` with S0 pricing exactly
  `floor_to_tick(min(S0.bestAsk - S0.tickSize, 0.99))`. Exact-family records already
  persisted under `maker_gtd_paper_v1` remain grandfathered with their original
  one-tick-improvement formula; persisted version/formula fields separate the two
  regimes. This family
  intentionally enters ordinary Paper metrics under the mandatory label
  `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`; Live
  submission is disabled. No alias, clone, descendant, future strategy, or changed
  execution semantic inherits the
  exception, and the broad parity/ResearchOnly rule remains unchanged otherwise.
- The second exact exception is catalog group `8224`: BTC/ETH/SOL 5m paired
  Up/Down Maker-GTD legs, behavior `PairedFixedOutcomeMakerGtdFirstAccepting`,
  mutually linked ID suffixes `101/102`, `201/202`, `301/302`, first-observed
  accepting timing, source `crypto_paired_maker_gtd_first_accepting_paper`,
  `PaperOnly=true`, Up cap `0.50`, Down cap `0.49`, equal frozen shares,
  independent S0/S1 acceptance, no pair atomicity/rollback, and expiry at market
  end minus one minute. New `paired_maker_gtd_paper_v3` placements prove direct
  HTTP freshness with a bounded ordered request/receipt/response/evaluation
  bracket; the authoritative venue timestamp remains audit evidence and may be
  old for a quiet unchanged book. Exact v1/v2 orders remain grandfathered for their
  lifecycle. Under `paired_touch_no_depth_gap_recovery_v1`, restart, reconnect,
  reassignment, or delivery failure pauses inference and installs a new exact-asset
  fence; the confirming frame cannot fill, only a later authoritative event in the
  unchanged segment can fill, and gap/cache/REST/pre-fence events are not backfilled.
  They carry the same mandatory label and may enter ordinary
  Paper metrics. Maker rebates are never inferred or included in Paper PnL; Live
  remains disabled. No predicate mismatch inherits this exception.
- Do not silently ignore API errors; persist/log explicit failure reasons.
- Run tests before declaring implementation tasks complete.
- Update README/project memory/task context when behavior changes.
