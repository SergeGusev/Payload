# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 03: configuration, SQLite storage, and logging. It contains project structure, typed configuration, SQLite schema initialization, a basic repository, a Worker Service heartbeat, and a basic WPF dashboard shell.

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

The service initializes SQLite on startup. The default database path is configured in `src/PolyCopyTrader.Service/appsettings.json`:

```json
"Storage": {
  "DatabasePath": "data/polycopytrader.db"
}
```

Relative database paths resolve under the service output directory.

## Known Limitations

- No public Polymarket API calls yet.
- No scanner, signal engine, risk engine, or paper trading implementation yet.
- No WebSocket support yet.
- No auth/signing/live trading support.

## Next Recommended Task

Implement `Codex/04_TASK_POLYMARKET_PUBLIC_API_CLIENTS.md`.
