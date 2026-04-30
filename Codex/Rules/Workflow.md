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
