# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 02: repository scaffold. It contains project structure, placeholder domain/API/strategy/storage contracts, a Worker Service heartbeat, and a basic WPF dashboard shell.

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

## Run Dashboard

```powershell
dotnet run --project src/PolyCopyTrader.Dashboard/PolyCopyTrader.Dashboard.csproj
```

The dashboard currently opens a basic shell. Live database views and service status polling are added in later tasks.

## Known Limitations

- No SQLite persistence yet.
- No public Polymarket API calls yet.
- No scanner, signal engine, risk engine, or paper trading implementation yet.
- No WebSocket support yet.
- No auth/signing/live trading support.

## Next Recommended Task

Implement `Codex/03_TASK_CONFIG_STORAGE_LOGGING.md`.
