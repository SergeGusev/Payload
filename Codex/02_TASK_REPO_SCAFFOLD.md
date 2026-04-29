# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 02 — Repository scaffold

## Goal

Create the initial C#/.NET solution structure for PolyCopyTrader.

## Scope

Implement only the project skeleton, references, basic build, and placeholder classes. Do not implement Polymarket API calls yet. Do not implement live trading.

## Required solution structure

```text
PolyCopyTrader.sln

src/
  PolyCopyTrader.Domain/
  PolyCopyTrader.Polymarket/
  PolyCopyTrader.Strategy/
  PolyCopyTrader.Storage/
  PolyCopyTrader.Service/
  PolyCopyTrader.Dashboard/

tests/
  PolyCopyTrader.Tests/
```

## Project types

- `PolyCopyTrader.Domain`: class library.
- `PolyCopyTrader.Polymarket`: class library.
- `PolyCopyTrader.Strategy`: class library.
- `PolyCopyTrader.Storage`: class library.
- `PolyCopyTrader.Service`: Worker Service / console host that can later be installed as Windows Service.
- `PolyCopyTrader.Dashboard`: WPF application.
- `PolyCopyTrader.Tests`: test project.

## Required references

```text
Domain -> no project dependencies
Polymarket -> Domain
Strategy -> Domain
Storage -> Domain
Service -> Domain, Polymarket, Strategy, Storage
Dashboard -> Domain, Storage, Strategy if needed
Tests -> all relevant projects
```

Keep `PolyCopyTrader.Strategy` independent from `PolyCopyTrader.Polymarket`. The service/application layer should fetch Polymarket data, normalize it into domain models, and pass those models into strategy engines. This keeps signal/risk logic testable without HTTP clients or API DTOs.

## NuGet packages

Add only packages needed for scaffolding:

- `Microsoft.Extensions.Hosting`
- `Microsoft.Extensions.Configuration.Json`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Logging`
- `Serilog`
- `Serilog.Extensions.Hosting`
- `Serilog.Sinks.File`
- `CommunityToolkit.Mvvm`
- test framework of your choice, preferably xUnit or NUnit

Do not add trading/signing libraries yet.

## Required files

- `README.md`
- `AGENTS.md` if not already present
- `src/PolyCopyTrader.Service/appsettings.json`
- `src/PolyCopyTrader.Dashboard/appsettings.json` if needed
- `.gitignore`

## Placeholder classes

Create minimal placeholder classes/interfaces:

```text
Domain:
  TraderProfile
  TraderRule
  LeaderTrade
  LeaderPosition
  MarketInfo
  OrderBookSnapshot
  Signal
  RiskDecision
  PaperOrder
  PaperPosition

Polymarket:
  IPolymarketDataApiClient
  IPolymarketClobPublicClient

Strategy:
  ISignalEngine
  IRiskEngine
  IPaperTradingEngine

Storage:
  IAppRepository

Service:
  BotWorker

Dashboard:
  MainViewModel
```

## Acceptance criteria

1. `dotnet build` succeeds.
2. `dotnet test` succeeds, even if tests are minimal.
3. WPF dashboard opens with a basic shell.
4. Worker service starts and logs a heartbeat.
5. No Polymarket live/authenticated functionality exists.
6. README explains how to build, test, run service, run dashboard.

## What to avoid

- Do not implement live trading.
- Do not ask for private keys.
- Do not introduce Node/Python sidecars.
- Do not put business logic in WPF views.
