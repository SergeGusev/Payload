# PolyCopyTrader .NET Framework 4.8 Port

This folder is an isolated Windows Server 2008 R2 compatibility workspace.
It does not replace the main `src` / `PolyCopyTrader.sln` implementation.

Target:

- Runtime: .NET Framework 4.8
- Intended legacy OS: Windows Server 2008 R2 SP1
- Build posture: classic .NET Framework projects under `src4.8`, buildable with
  Visual Studio/MSBuild and NuGet restore; the `src4.8` solution no longer uses
  SDK-style projects and does not require the .NET SDK or the repository root
  `global.json`.
- Runtime posture while porting: Paper / ReadOnly first, Live only after explicit revalidation

Current scaffold:

- `PolyCopyTrader.Net48.Domain`
- `PolyCopyTrader.Net48.Strategy`
- `PolyCopyTrader.Net48.Polymarket`
- `PolyCopyTrader.Net48.Storage`
- `PolyCopyTrader.Net48.Service`
- `PolyCopyTrader.Net48.Dashboard.Behaviors`
- `PolyCopyTrader.Net48.Dashboard`

Build commands:

```powershell
cd src4.8
& "C:\Path\To\MSBuild.exe" .\PolyCopyTrader.Net48.sln /t:Restore /p:Configuration=Release /p:Platform="Any CPU"
& "C:\Path\To\MSBuild.exe" .\PolyCopyTrader.Net48.sln /p:Configuration=Release /p:Platform="Any CPU"
```

The output is written to each project's `bin\Release` folder, for example:

```powershell
.\PolyCopyTrader.Net48.Service\bin\Release\PolyCopyTrader.Net48.Service.exe --host-smoke
.\PolyCopyTrader.Net48.Service\bin\Release\PolyCopyTrader.Net48.Service.exe --storage-smoke
```

The projects use `PackageReference`, including `Microsoft.Net.Compilers.Toolset`,
so NuGet restore must be available. This is still a .NET Framework 4.8 build;
the compiler package is only a build-time tool so current C# syntax can be built
without SDK-style projects.

Service commands:

```powershell
.\PolyCopyTrader.Net48.Service.exe --console
.\PolyCopyTrader.Net48.Service.exe --install
.\PolyCopyTrader.Net48.Service.exe --start
.\PolyCopyTrader.Net48.Service.exe --stop
.\PolyCopyTrader.Net48.Service.exe --uninstall
.\PolyCopyTrader.Net48.Service.exe --strategy-smoke
```

Run `--install`, `--start`, `--stop`, and `--uninstall` from an elevated
administrator console. Direct interactive launch without arguments prints the
same scaffold/help text as `--console`; the process only enters Windows Service
mode when started by the Service Control Manager.

`--strategy-smoke` runs the first ported Paper-only strategy pipeline slice in
process: signal evaluation, risk check, paper order creation, and simulated fill.

Porting constraints:

- Do not share source files blindly from the modern `net10.0` implementation until package and language compatibility are checked.
- Downgrade NuGet packages deliberately and pin versions.
- Revalidate TLS, WebSocket, PostgreSQL, Polymarket API parsing, signing, and paper accounting before any Live use.
- Keep secrets out of source control and logs.
