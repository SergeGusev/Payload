# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 05: watchlist scanner. It contains project structure, typed configuration, PostgreSQL schema initialization, a basic repository, read-only Polymarket Data/CLOB/Geo clients, a Worker Service scanner loop, and a basic WPF dashboard shell.

## Safety

- No live trading exists in this scaffold.
- No authenticated Polymarket endpoints exist.
- No private key handling exists.
- Default mode is read-only/paper-first by project policy.

## Project Structure

```text
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

## Build

```powershell
dotnet build
```

## Test

```powershell
dotnet test
```

## Run Service

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

The service logs a heartbeat in scaffold mode and writes rolling logs under its output `logs` directory.

## Print Config Summary

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -- --print-config
```

The summary is sanitized and does not include secrets. Live trading is disabled by configuration and validation.

## Run Dashboard

```powershell
dotnet run --project src/PolyCopyTrader.Dashboard/PolyCopyTrader.Dashboard.csproj
```

The dashboard currently opens a basic shell. Live database views and service status polling are added in later tasks.

## Storage

The service uses PostgreSQL through Npgsql. Do not store credentials in repository files. Configure the connection string through the `POLYCOPYTRADER_POSTGRES_CONNECTION` environment variable.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

If no PostgreSQL connection string is configured, storage is disabled and the service uses a no-op repository. Set `Storage:RequireConfiguredDatabase` to `true` for production/VPS runs.

## Polymarket Public APIs

The `PolyCopyTrader.Polymarket` project contains read-only clients for:

- Data API: trader leaderboard, user trades, and current positions.
- CLOB public API: order book, server time, midpoint, and spread.
- Geo endpoint: current geoblock status.

User trade calls explicitly send `takerOnly=false` when requested so maker fills are not silently excluded. HTTP failures are retried for transient `429`/`5xx` responses and persisted to `ApiErrors` through the configured repository. When PostgreSQL is not configured, the no-op repository keeps local scaffold runs read-only and dependency-free.

## Watchlist Scanner

The service scans enabled `Watchlist:Traders` entries on `Bot:PollIntervalSeconds`. Each enabled wallet is validated before any API call. Recent trades are fetched with `takerOnly=false`, deduplicated, persisted to `LeaderTrades`, and queued as in-memory candidates for the future signal engine. Current positions are written as snapshots to `LeaderPositions`.

Scanner health is persisted to `scanner_status` with last success/error timestamps and per-loop fetched/stored counts. Invalid placeholder wallets are warned and skipped without crashing the service.

## Known Limitations

- No signal engine, risk engine, or paper trading implementation yet.
- No WebSocket support yet.
- No auth/signing/live trading support.

## Next Recommended Task

Implement `Codex/06_TASK_SIGNAL_AND_RISK_ENGINES.md`.
