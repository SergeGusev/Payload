# PolyCopyTrader .NET Framework 4.8 Port

This folder is an isolated Windows Server 2008 R2 compatibility workspace.
It does not replace the main `src` / `PolyCopyTrader.sln` implementation.

Target:

- Runtime: .NET Framework 4.8
- Intended legacy OS: Windows Server 2008 R2 SP1
- Build posture: build on a modern development machine, deploy binaries to the legacy server
- Runtime posture while porting: Paper / ReadOnly first, Live only after explicit revalidation

Current scaffold:

- `PolyCopyTrader.Net48.Domain`
- `PolyCopyTrader.Net48.Strategy`
- `PolyCopyTrader.Net48.Polymarket`
- `PolyCopyTrader.Net48.Storage`
- `PolyCopyTrader.Net48.Service`
- `PolyCopyTrader.Net48.Dashboard`

Service commands:

```powershell
.\PolyCopyTrader.Net48.Service.exe --console
.\PolyCopyTrader.Net48.Service.exe --install
.\PolyCopyTrader.Net48.Service.exe --start
.\PolyCopyTrader.Net48.Service.exe --stop
.\PolyCopyTrader.Net48.Service.exe --uninstall
```

Run `--install`, `--start`, `--stop`, and `--uninstall` from an elevated
administrator console. Direct interactive launch without arguments prints the
same scaffold/help text as `--console`; the process only enters Windows Service
mode when started by the Service Control Manager.

Porting constraints:

- Do not share source files blindly from the modern `net10.0` implementation until package and language compatibility are checked.
- Downgrade NuGet packages deliberately and pin versions.
- Revalidate TLS, WebSocket, PostgreSQL, Polymarket API parsing, signing, and paper accounting before any Live use.
- Keep secrets out of source control and logs.
