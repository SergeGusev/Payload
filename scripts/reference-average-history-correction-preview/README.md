# Reference Average history correction preview

Read-only streaming preview for the 848 static Reference Average strategies listed in
`Codex/Tasks/REFERENCE_AVERAGE_MAX_MIN_MIGRATION_2026-07-27.md`.

The tool never writes to PostgreSQL. It opens a `REPEATABLE READ`, `READ ONLY`, UTC
transaction and always rolls it back. It rejects any `--host` other than
`192.168.0.101`, and it overrides the host in
`POLYCOPYTRADER_POSTGRES_CONNECTION` with that exact address.

## Run

Run from the repository root. The output directory must be empty and below
`D:\CodexTemp`.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION = '<existing connection string>'
dotnet run --project scripts/reference-average-history-correction-preview/ReferenceAverageHistoryCorrectionPreview.csproj -- `
  --host 192.168.0.101 `
  --cutoff 2026-07-27T13:24:05.932282Z `
  --output-dir D:\CodexTemp\reference-average-history-correction-preview-YYYYMMDD-HHMMSS `
  --command-timeout-seconds 120
```

This command is intentionally documented but was not run against production while
the tool was created.

## Output

- `retain.csv`: exact existing settled `Up` bets retained by v2. Existing `Down`
  bets are omitted because the corrected upper branch still uses the same MAX boundary.
- `remove.csv`: exact existing `Up` bets for which the corrected Reference signal skips.
- `add.csv`: exact Optimized Down/Neutral legacy skips that independently pass both
  the legacy MAX preconditions and the v2 MIN/3h signal. The modeled fill is `0.50`
  for the LowEnter-priced cohort and `0.52` otherwise. The persisted decision-time
  stake multiplier is emitted separately; final notional still requires the exact
  market `min_order_size` replay.
- `still-skip.csv`: exact replayable Optimized candidates that still skip under v2.
- `unreplayable.csv`: rows without enough persisted decision input.
- `invariant-errors.csv`: contradictory run/order/fill or v1/v2 evidence. Any row in
  this file makes the process fail closed with exit code `3`.
- `catalog.csv`: the parsed 848-strategy allowlist with asset, family and location.
- `manifest.json`: catalog hash/count assertions, transaction evidence, group counts,
  action counts and SHA-256 hashes of every CSV.

Only an exact joined Paper order with at least one positive-price, positive-size fill
is treated as an existing bet. Detailed replay is restricted to existing `Up` bets,
the only outcome that can be removed by the Max-to-Min change. Each query is
restricted to one strategy ID so the
production `(strategy_id, status, entry_due_at_utc)` index can stream a bounded
range; no 848-ID wide join is used.

For DiffConfirmed history, the persisted nested Reference threshold is authoritative:
historical wrapper links can differ from the current catalog link. The wrapper's
Paper-order strategy identity plus the nested asset, decision source, and saved
threshold remain part of the row-level evidence.

## Test

```powershell
dotnet test scripts/reference-average-history-correction-preview/tests/ReferenceAverageHistoryCorrectionPreview.Tests.csproj
```

The preview does not expand ChildMirror dependencies and does not calculate stake
progression, settlement/PnL replacements, positions, Live links, or deletion order.
