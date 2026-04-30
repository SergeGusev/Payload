# AGENTS.md

## Context persistence workflow

Within repository-local instructions, `Codex/Rules/Workflow.md` is the
authoritative workflow contract for context recovery, task initialization, active
context updates, and daily history. Higher-priority runtime instructions and the
safety rules in this file still apply.

ActiveContextFile: Codex/Contexts/ContextPolyCopyTrader.md

## Project

This repository contains **PolyCopyTrader**, a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

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
