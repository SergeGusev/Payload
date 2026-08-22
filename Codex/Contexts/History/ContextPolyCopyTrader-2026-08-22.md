# 2026-08-22

## Versioned Migration Startup Failure Diagnosis

Goal: Diagnose why the newly deployed service and the same local Release build exit immediately.

Status: Root cause confirmed; no product or production mutation performed.

Facts:
- The supplied startup log records `System.InvalidOperationException: The PostgreSQL migration catalog is empty.` from `PostgresSchemaMigrationCatalog.ValidateAndOrder`, reached through the two-argument `PostgresSchemaInitializer` constructor during `GetRequiredService<IStorageSchemaInitializer>()`, before `host.RunAsync()`.
- Current startup registers `IStorageSchemaInitializer` by implementation type but registers no `PostgresSchemaMigration` services. The built-in DI container supplies an empty `IEnumerable<PostgresSchemaMigration>` to the longer public constructor, so the default catalog constructor path is bypassed.
- The top-level exception handler logs Fatal and returns normally, causing process exit code `0` even on fatal startup failure.
- Current migration tests instantiate the initializer directly; no production-DI resolution test covers this wiring.
- Exact production snapshot cutoff `2026-08-22T06:36:37.981626Z`: endpoint `192.168.0.101:5432/polycopytrader`, UTC, `REPEATABLE READ READ ONLY`, `statement_timeout=15s`; migration history relation absent; service sessions `0`; ungranted locks `0`; migration advisory locks `0`; heartbeat fixed at `2026-08-21T21:38:37.311826Z`, age `32280.670s`, old build `a3b0457fc113fc5ef482aabd5c090c3162045001`.

Next: On explicit repair request, create and approve a RequirementGate contract for explicit default migration catalog wiring, a DI regression test, and nonzero fatal startup exit behavior.

### Repair checkpoint

- User approved `RC-20260822-migration-startup-wiring` digest `sha256:f1231d773ea41646fa211644ba6efd4807374d51c7fd46733ae2e186c69276e8`; approval-only commit `31ea72a2` precedes product edits.
- Minimal DI/exit-code edits were made, but the first production-shape DI test failed on the next hidden blocker: current dirty `PostgresSchema.cs` changes the immutable baseline checksum from expected `4dba8fe092057778ff146e61be43cabbb882358c679c8ee70ade832a872b00d2` to `6dcac6e1d5ef16c9ffeb1bad5d281c4121adff9ddff2d722696aceb43d3d6ab0` by adding `ix_signals_trader_wallet_id` inside legacy baseline SQL.
- The exact index/schema-test edits are governed by approved-but-incomplete `RC-20260820-add-signals-trader-wallet-index` and were explicitly outside the current repair contract. Work paused before changing or moving them.
- No production mutation occurred; the failed test used dummy `127.0.0.1:1` and opened no database connection.

### Emergency startup repair completion

- User's final instruction restricted scope to service restoration only; final approved digest is `sha256:26e37d7011ed832b5a993aaa7a8ef2e09744681532ca1096b330d559e6f8ea05`, with final approval-only commit `f2ec5933`.
- Product diff is exactly: make the custom-catalog initializer constructor internal and add one production-shape DI regression. No `Program.cs`, migration catalog, schema/baseline, index, exit-code, strategy, order, or runtime behavior change was delivered.
- Preserved unfinished exact index edits in `stash@{0}` commit `94b5b3c21613947d490d5874d89bb9c0d1df6d62`, message `codex-preserve-signals-index-before-emergency-startup-repair`; pre/stash patch hash `c1b1dbd235292198bfe85d81fac80245d8216d29` matched exactly.
- Verification: DI-only `1/1`; complete migration suite `9/9` on isolated PostgreSQL 17; complete Debug solution build `0` errors / `126` pre-existing warnings; WorkingTree and Staged gates passed; independent reviewer PASS with no findings.
- Implementation committed and pushed as `f17318d6`; isolated PostgreSQL stopped; production unchanged.

## 2026-08-22T07:31:31.833575Z
Request:
Сервис на проде запустился. Проверь, как он работает и делает ли ставки

Result:
Read-only production verification confirmed build `a28aff1d769299a6c6b40191c3a637b79f40a417` running with advancing heartbeat, fresh BTC/ETH/SOL reference data, successful one-row baseline registration, zero waiting locks, and active five-minute processing. Since start the service created and filled 168 Paper orders (BTC 25, ETH 63, SOL 80) and settled 525 runs. No new Live order occurred; no qualifying Live signal was proved. Nine startup warm-up/transient stream errors stopped after `07:25:34.656105Z`. A residual fixed inventory of 270 old `Observed` rows due on 2026-08-13 remains but is not blocking current betting.
