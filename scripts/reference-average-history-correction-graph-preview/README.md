# Reference Average physical-correction graph preview

Standalone, read-only second-stage preview for the historical Max/Min correction.
It consumes the completed `remove.csv`, `add.csv`, `catalog.csv`, and `manifest.json` emitted by
`scripts/reference-average-history-correction-preview`; it never reclassifies the
Reference signal itself.

The tool has no mutation mode. It connects only to
`192.168.0.101:5432/polycopytrader`, pins `search_path=pg_catalog,public`, opens a
`REPEATABLE READ`, `READ ONLY`, UTC transaction, produces the dependency graph in
memory, and explicitly rolls the transaction back before writing its output manifest.
Every input CSV is checked against the source manifest's SHA-256 and row count. The
repository migration catalog itself must also have the exact pinned source SHA-256;
matching row counts alone are insufficient.
All graph-sized UUID reads are split into deterministic batches of at most `25,000`
identities. The only remaining parallel `unnest` inputs are the at-most-327 Add
candidates and their fixed resolution evidence sets.
Before scanning the large graph, PostgreSQL 18 must successfully `EXPLAIN` both exact
composed Main and Child SQL strings inside the same read-only transaction. Their names,
SHA-256 hashes, server version, and plan status are written to the manifest. Runtime
PostgreSQL errors include the calling query stage and a short SQL hash.

## Exact run

Run from the repository root. Both directories must be below `D:\CodexTemp`; the
output directory must be empty. Credentials are read from the existing environment
connection string, whose host is forcibly replaced with the required host.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION = '<existing connection string>'
$input = 'D:\CodexTemp\runs\reference-history-physical-correction-20260727-a42d68f1\results\preview-9-reclassified'
$output = 'D:\CodexTemp\runs\reference-history-physical-correction-20260727-a42d68f1\results\graph-preview-1'

dotnet run --project scripts/reference-average-history-correction-graph-preview/ReferenceAverageHistoryCorrectionGraphPreview.csproj -- `
  --host 192.168.0.101 `
  --cutoff 2026-07-27T13:24:05.932282Z `
  --signal-preview-manifest-sha256 19BE8C1EA87BBA18FEEAEC4791EA075C3649EC0276225BDE9E85097A8BB8EACD `
  --signal-preview-dir $input `
  --output-dir $output `
  --command-timeout-seconds 180
```

The source manifest may express the same cutoff with another ISO-8601 offset (the
current reclassified manifest uses `2026-07-27T16:24:05.932282+03:00`). The reader
normalizes it to UTC and requires the exact same instant.

Exit code `3` means the complete preview was written but mutation is deliberately
blocked. Schema v2 currently has 14 reconciliation targets, of which 10 remain
plan-required blockers, so this tool cannot authorize an apply operation. In addition, any linked
Live order, shadow decision, shadow discrepancy, partial Child link, unknown
reference column/FK, shared wallet+asset position key, or infeasible add forces the
fail-closed result.
The 14-row reconciliation allowlist is a versioned machine contract. Every row carries
an immutable `target_id`, `method_id`, `blocks_mutation`, and per-target SHA-256; the
ordered complete-set SHA-256 is
`4ACCCDFBBE34B1C3AEB1B3CAF7B982FB280B55EBE56E4CF00755EE56B169A7D8`.
A separate apply tool must prove exact set equality and this complete hash. Any missing,
extra, or changed target fails closed; it must not change `blocks_mutation` merely to
make the graph preview report safe.
The reviewed projection method is a global full bootstrap, not a per-strategy patch:
apply first removes affected-strategy events and moves the singleton control row to
`PendingHistoryCorrectionBootstrap`. While the Windows service remains stopped, an
explicit maintenance host takes a global `REPEATABLE READ` snapshot, clears/rebuilds
all projection state/fact tables for all strategies, upserts all lifetime/recent
snapshots, deletes all events visible in that bootstrap snapshot, clears the entire
reconciliation queue, and returns control to `Running` at calculation version `2`.
The same stopped-service maintenance phase must explicitly refresh/verify hourly PnL
and affected copied-trader performance before the normal service restart. Child-parent
assignments are the only reviewed post-normal-restart refresh gate unless a separate
one-shot child refresher is reviewed.

## Contract

- Main removals must still be the exact settled `Up` run/order/signal/fill graph from
  `remove.csv`. The accepted dispatch is exact FAK only: Main source
  `btc_updown5m_fak_taker_paper`, Child source
  `btc_updown5m_child_mirror_fak_paper`, `Filled` `Buy`, null correlation, coincident
  created/expires/filled timestamps, and exactly one matching positive fill.
- A Child row is included only when `pricing_mode=child_parent_mirror`, its market is
  the same as the main parent, and all saved parent run, Paper-order, and signal IDs
  resolve to the same main row. The saved parent strategy ID is also verified.
- The independent raw-Child inventory scans every `paper_orders.raw_decision_json`
  containing a top-level parent key, without a Paper-order creation-time cutoff. Every
  hit on the exact Main allowlist must resolve to one valid pre-cutoff run/order/signal
  graph; orphan, partial, ambiguous, post-cutoff-run, malformed, or out-of-catalog links
  are invariants.
- Any Live/shadow/discrepancy overlap blocks physical mutation; rows are never silently
  excluded from the preview.
- Position/settlement rows are exclusive only when every Paper order with the same
  `(copied_trader_wallet, asset_id)` belongs to the exact removal graph. If such rows
  exist, the zeroed position and the settlement identity, fill size, average/cost,
  value, PnL, `won`, and exact binary opposite outcome are recomputed from the graph.
  `winning_asset_id` is emitted as non-authoritative evidence because the historical
  sticky-token anomaly is known; exact `winning_outcome` plus arithmetic is decisive.
- Every Main removal restores `stake_usd` only from the historical order's root
  `paper_lost_base_stake_usd`. The compact raw-decision projection also proves the
  lost-counter coefficient/add/effective stake, stake multiplier, target sizing, and
  (for FAK) actual filled size/notional/average. Missing or inconsistent proof blocks.
  The correction-specific skip reason is exactly
  `reference_average_history_correction_v2_would_skip`; canonical replay evidence from
  the pinned classifier is preserved with its SHA-256.
- Add sizing uses the per-row historical effective stake multiplier from
  `skip_diagnostics_json.target_notional_usd`, Gamma `order_min_size`, the runtime FAK
  worst price `0.99`, safety multiplier `1.10`, whole-dollar upward rounding, and
  two-decimal upward worst-price size rounding. The resulting requested notional is
  modeled as fully filled at `0.52` for regular rows or `0.50` for LowEnter/LowerEnter.
- An Add entry timestamp is the exact source skipped-run `updated_at_utc`. Its settlement
  timestamp is only a deterministic modeled proxy,
  `max(market_end_utc,resolution_ledger_first_received_at_utc)`; it is never described as
  an exact historical runtime timestamp. Each row contains the complete canonical
  signal/order/fill/run-update/zero-position/settlement mutation payload. The payload
  explicitly records that no historical order-book snapshot is asserted.
- New entity IDs are specified by UUIDv5 namespace
  `02e29185-5f14-5f40-b5f7-8c584e8b22e8` and name
  `reference-average-history-correction-v2/{graph_manifest_sha256}/{run_id:D}/{entity_kind}`
  for `signal`, `paper_order`, `paper_fill`, `paper_position`, and
  `paper_position_settlement`.
- Every add requires a fresh official `GET /markets/{id}` response with exact binary
  Up/Down tokens and a unique `1/0` winner, the identical historical DB Gamma token
  mapping, exact resolved-ledger identity/provenance, and at least two consistent
  archived Binance start/end ticks with a close no more than 15 seconds old.
- For `MarketWebSocket` ledger rows, the latest persisted raw event must independently
  match market/condition, official winner/token, the complete binary token set, and
  timestamp bounds. Its raw event timestamp is not compared with the scalar ledger
  event timestamp: the scalar is the earliest sticky value while `raw_json` is the
  latest event. For `BinanceTimedClose`, the exact archived tick replay must agree and
  is the same provenance group, not an extra source.
- A sticky ledger `winning_asset_id` conflict never overrides official Gamma or a
  validated latest WebSocket raw event. It is emitted as explicit warning evidence,
  not an invariant in that proven case. Matching diagnostic-table rows are validated
  if present; zero rows are unavailable, carry no fabricated winner/token, and are not
  an independent source.
- The snapshot records the exact `daily_reports` row count and requires zero; apply
  must repeat the exact-zero gate immediately before mutation. Hourly PnL requires an
  explicit stopped-service maintenance-host refresh/verification before normal restart;
  Child assignments require the explicit post-normal-restart refresh gate.
  `paper_lost_counter` is intentionally left unchanged as a history-only limitation.

## Outputs

- `main-removals.csv` and `child-removals.csv`: exact removal allowlists, historical
  base-stake/replay proof for Main, and canonical graph/fill mutation-scope SHA-256.
- `graph-orders.csv` and `graph-fills.csv`: the complete Paper order/fill graph;
  `graph-orders.csv` carries the compact/full raw-decision proof hash and graph-state hash.
  Run, order, signal, fill, position, and settlement preimages also carry PostgreSQL-side
  full-row SHA-256 values computed from UTF-8 `to_jsonb(row)::text`.
- `live-shadow-overlaps.csv`: linked Live orders, shadow decisions, and discrepancies;
  every row is a blocker.
- `dependencies.csv`, `foreign-keys.csv`, and `schema-reference-columns.csv`: exact row
  dependencies plus the reviewed runtime schema surface.
- `position-keys.csv`, `positions.csv`, and `position-settlements.csv`: exclusivity
  proof and exact semantic rows.
- `live-gamma-resolutions.csv`: official request URL, fetch time, response byte count,
  and SHA-256; raw response bytes remain only in memory.
- `market-resolved-event-evidence.csv`: matching DB diagnostic metadata and the hash
  of PostgreSQL's `jsonb::text` representation; raw JSON is not written.
- `reconciliation-targets.csv`: derived tables that still require a separately
  reviewed deterministic rebuild plan, with exact target/method IDs and contract hashes.
- `operation-footprint.csv`: exact snapshot `count(*)` and
  `sum(pg_column_size(target_row))` for every current physical target, including all
  affected-strategy projection events and reconciliation-queue rows, plus the exact
  singleton projection-control transition and the exact direct row-operation floor for
  modeled inserts/upserts. The control preimage must be exactly `initialized=true`,
  calculation version `2`, status `Running`, and null `last_error`. These heap-row byte sums are
  not WAL estimates: indexes, TOAST side tables, tuple/WAL overhead, triggers, full-page
  images, and vacuum are deliberately excluded.
- `add-feasibility.csv`: per-row source-run hash, sizing, selected token, raw-ledger
  hash/bytes/timestamps/validation, resolution provenance, and modeled settlement/PnL.
- `invariant-errors.csv`: all fail-closed contradictions.
- `manifest.json`: input hashes, transaction evidence, row counts, blocker counts, and
  SHA-256 for every deterministic CSV.

This tool does not write an apply plan, modify projection state, stop the service,
create a backup, or alter PostgreSQL. Those require a separate reviewed workflow.

## Test

```powershell
dotnet test scripts/reference-average-history-correction-graph-preview/tests/ReferenceAverageHistoryCorrectionGraphPreview.Tests.csproj
```

The tests cover the pinned-manifest/catalog gates, exact, orphan, post-cutoff-run, and
wrong-mode Child links,
worst-price sizing (`6.0093` for min size `5` and multiplier `1`), regular/LowEnter
fills, official Gamma parsing, stale archived ticks, unavailable/validated raw
diagnostics, repeated WebSocket events with different sticky/latest timestamps, source
disagreement, non-authoritative sticky tokens, historical base-stake recovery,
position/settlement arithmetic, exact FAK provenance/shape, >2-batch identity
preservation, complete CSV cardinality, the all-time wallet+asset Add collision gate,
the frozen reconciliation allowlist/hash, and mutation-scope hashes.
