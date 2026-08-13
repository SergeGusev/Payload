# Codex Workflow

This document defines the repository-local execution workflow for Codex in
PolyCopyTrader. Repository files are the authoritative project memory; chat
history is not authoritative.

Higher-priority system and developer instructions still apply. Within project
files, this workflow has priority over other repository instructions when the
topic is context recovery, task initialization, or task finalization.

## 1. Bootstrap Phase

This phase runs only when the user prompt is exactly:

```text
start
```

Codex must:

- read this file;
- read `AGENTS.md`;
- read all `*.md` files from `Codex/Contexts/History`, sorted by filename
  ascending;
- read the active context file named by `ActiveContextFile` in `AGENTS.md`;
- output exactly one line:

```text
Current context file: <ActiveContextFile>
```

No task logic, diagnostics, greetings, plans, or extra output are allowed during
bootstrap.

## 2. Task Initialization

For every prompt other than `start`, Codex must initialize from repository files
before relying on prior chat context.

Required steps:

- run `git pull --ff-only` when a tracking remote exists; if no upstream is
  configured, record that fact and continue locally;
- read this file;
- read `AGENTS.md`;
- read `Codex/Rules/CodingRules.md`;
- determine `ActiveContextFile` from `AGENTS.md`;
- create the active context file if it is missing;
- read the active context file;
- read relevant task, project-memory, or documentation files for the request;
- inspect `git status --porcelain=v1` and `git log -1 --oneline`;
- continue from persisted repository state.

Do not ask the user to reconstruct context when it can be recovered from files.

## 3. Task Execution

Codex should keep important progress in files, not only in chat.

For substantial or multi-step work:

- update task evidence, reports, or project memory when useful;
- preserve blockers explicitly;
- keep the active context accurate enough for a new session to resume;
- continue autonomously when a reasonable fallback exists;
- stop only on true blockers.

For every new or changed Paper strategy or execution rule, apply the mandatory
Paper/live parity gate in `docs/architecture/PAPER_LIVE_PARITY.md` before treating
the implementation or its results as Paper trading. The only non-parity path is
the closed user-approved exception defined below:

- identify and document the exact current Live API order equivalent;
- verify that Paper and Live consume the same pre-submit `ExecutionIntent` and
  preserve its order semantics;
- verify that post-fill information affects accounting or future decisions only;
- reject unsupported atomicity, rollback, post-fill acceptance, and aggregate
  fill-price guarantees;
- classify an algorithm as `ResearchOnly`, outside Paper PnL and Paper performance
  claims, when a Live equivalent cannot be proved;
- allow ordinary Paper classification without proven fill equivalence only for the
  exact exception approved by the user on 2026-08-09, and only when every predicate
  matches: asset `ETH`; neutral Reference Average Maker-GTD threshold `1..10` or
  `15..100` step `5`; behavior
  `ReferenceAverageBpsThresholdMakerGtdPremarket`; catalog ID
  `b7c50005-0000-4000-8223-{100+threshold, zero-padded to 12 digits}`; execution
  source `eth_reference_average_maker_gtd_paper`; and `PaperOnly=true`. New
  placements use contract `maker_gtd_paper_v2` with S0 pricing exactly
  `floor_to_tick(min(S0.bestAsk - S0.tickSize, 0.99))`. Exact-family records already
  persisted under `maker_gtd_paper_v1` remain grandfathered with their original
  one-tick-improvement formula; persisted version/formula fields separate the two
  regimes. On 2026-08-13 the user explicitly approved this exact family inheriting
  the shared Reference Average v4 signal rule: Max/Min boundaries remain full-only,
  while an incomplete explicit `24h` record may supply its first populated real
  bucket as the bps denominator; no other window may be substituted. The
  `maker_gtd_paper_v2` execution contract is unchanged. Its ordinary
  Paper orders, PnL, win rate, and performance are intentional, but every result
  must say
  `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`. Live
  submission is
  disabled, and no alias, clone, descendant, future strategy, or changed execution
  semantic inherits the exception;
- allow the second exact closed ordinary-Paper exception only for the six
  group-`8224` BTC/ETH/SOL five-minute paired Maker-GTD legs: fixed Up/Down
  outcomes, mutually linked pair IDs, first-observed `acceptingOrders=true`
  timing, source `crypto_paired_maker_gtd_first_accepting_paper`, Up cap `0.50`,
  Down cap `0.49`, one frozen equal-share quantity per pair, independent S0/S1
  acceptance, no atomic rollback, effective Paper expiry at market end, and
  stated/wire GTD expiration at market end plus 60 seconds because of the venue
  security threshold. Their new PostOnly GTD placements use
  `paired_maker_gtd_paper_v4`: every direct HTTP S0/S1 read
  must carry a bounded ordered local request/client-receipt/response/evaluation
  bracket. The authoritative venue snapshot timestamp remains mandatory audit
  evidence but may be old for a freshly fetched unchanged quiet book. Exact-family
  v1/v2/v3 orders remain grandfathered under their former effective Paper expiry at
  market end minus 60 seconds and stated/wire expiration at market end for lifecycle
  completion. All other approved predicates and lifecycle semantics remain unchanged. Under
  `paired_touch_no_depth_gap_recovery_v1`, a restart, reconnect, reassignment, or
  delivery failure creates a new exact-asset fence: the confirming frame cannot
  fill, only a later authoritative event in that unchanged segment can fill, and
  missing/cache/REST/pre-fence events are never backfilled. Terminal evidence must
  persist the acceptance, fence, trigger, policy version, and `no_backfill=true`.
  They use the same mandatory label; maker rebates are excluded from Paper PnL;
  Live is disabled, and no predicate mismatch inherits this exception;
- add or update parity tests and verify that intent, market evidence, fills, and
  outcomes are persisted or otherwise auditable.

Except for that exact closed exception, missing Live-equivalence evidence or a
failing parity test is a completion blocker, not a documentation caveat. A predicate
mismatch, missing mandatory label, enabled Live path, or failing exception contract
test is likewise a completion blocker for the exception.

## 4. Task Finalization

After every completed non-`start` task:

- update the active context file with a newest-first active update;
- append one entry to the daily history file;
- run project-required verification commands appropriate to the change;
- run staged or unstaged diff checks where practical;
- commit and push when repository files changed and a Git remote/upstream is
  available;
- if commit or push cannot be completed because the repository has no upstream,
  record that limitation in context/history and report it clearly.

Never commit secrets. Never revert unrelated user changes just to make the tree
clean.

## 5. Active Context Format

Use this format at the top of the active context file:

```md
## Active Update YYYY-MM-DD <Task Name>
Goal: <one sentence>
Status: Completed | In Progress | Blocked
Done:
- <concrete artifact or decision>
Next: <next task or "None">
Notes: <commands/tests/builds/checks and important observations>
Blockers: <None or precise blocker requiring user action>
```

The newest entry belongs at the top.

## 6. Daily History Format

Append one entry to:

```text
Codex/Contexts/History/ContextPolyCopyTrader-YYYY-MM-DD.md
```

Use UTC ISO 8601 round-trip timestamps:

```md
## <CapturedAtUtc>
Request:
<exact user prompt text>

Result:
<short factual summary of what was done or why execution stopped>
```

Append only. Do not rewrite prior history entries.

## 7. Encoding

Preserve UTF-8 for context and history files. This matters because project
history often contains Cyrillic user prompts.

## 8. Recovery Rule

After any context reset, re-enter task initialization: read workflow, `AGENTS.md`,
coding rules, active context, relevant docs, and Git status before continuing.
