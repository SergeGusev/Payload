# Child / Child ROI Daily PnL Report

This tool creates the one-sheet Excel report requested for the best all-history
Paper PnL strategy in each `BTC/ETH/SOL x Child/Child ROI` group.

The runner uses one read-only production snapshot, independently reconciles the
six winners and their daily PnL, builds the workbook, verifies it through Excel,
and performs a final import/render check. Every stage has an explicit timeout,
and `run-summary.json` records stage timings and the exact failure point.

## One-time setup

Create `scripts/child-child-roi-daily-report/node_modules` as a junction to the
`node_modules` directory returned by the Codex workspace dependency loader.
The junction is ignored by Git and must never modify the loader-owned directory.

Build the exporter once:

```powershell
dotnet build scripts/child-child-roi-daily-report/ChildDailyReportExport.csproj -c Release
```

## Run

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/child-child-roi-daily-report/run-report.ps1
```

The default total time budget is 180 seconds. The command fails explicitly with
`STAGE_TIMEOUT`, `STAGE_FAILED`, or `REPORT_TIMEOUT` instead of waiting silently.
Generated workbooks and evidence are written under the ignored `outputs/`
directory.
