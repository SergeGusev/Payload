# Context And History Persistence Protocol

Purpose: this document describes the repository-local context persistence workflow used in this project so another Codex client can reproduce it in
a new project and avoid losing working context after an overnight reset, context compaction, process restart, or new chat session.

The key design rule is simple: conversational memory is never authoritative. The repository files are authoritative.

## 1. Problem This Solves

Codex chat context can be lost, compacted, truncated, or resumed without the full conversation. If the project relies on chat history only, Codex
may forget:

- what task was active;
- which files were changed;
- which tests passed or failed;
- what blocker was found;
- what the user already approved;
- what should happen next;
- which decisions must not be revisited.

This project avoids that by writing the working state into versioned files after every task.

## 2. Required Files And Directories

Use these paths in the new project, adjusted for the project name if needed:

```text
AGENTS.md
Codex/Rules/Workflow.md
Codex/Rules/CodingRules.md
Codex/Contexts/Context<ProjectName>.md
Codex/Contexts/History/Context<ProjectName>-YYYY-MM-DD.md
Codex/Tasks/
```

In this repository the actual paths are:

```text
AGENTS.md
Admin.Shared/Codex/Rules/Workflow.md
Admin.Shared/Codex/Rules/CodingRules.md
Admin.Shared/Codex/Contexts/ContextAdmin.md
Admin.Shared/Codex/Contexts/History/ContextAdmin-YYYY-MM-DD.md
Admin.Shared/Codex/Tasks/
```

The path names are not magic. What matters is that `AGENTS.md` points to the active context file, `Workflow.md` defines mandatory behavior, and
the history directory stores append-only daily files.

## 3. File Responsibilities

### 3.1 `AGENTS.md`

`AGENTS.md` is the root instruction file. It must contain:

- a rule that `Workflow.md` has absolute priority;
- bootstrap-mode behavior for the exact prompt `start`;
- task-execution behavior for every other prompt;
- the mutable `ActiveContextFile` value.

The active context line is the pointer Codex uses to find the current rolling state:

```text
ActiveContextFile: Codex/Contexts/Context<ProjectName>.md
```

If a user explicitly changes the active context path, Codex must update this line in `AGENTS.md`.

### 3.2 `Workflow.md`

`Workflow.md` is the highest-priority workflow contract. It tells Codex how to recover state, how to run tasks, and how to persist updates.

It must explicitly say:

- task state comes from repository files, not conversational memory;
- context resets are normal;
- the `start` prompt has a special bootstrap flow;
- every non-`start` prompt must initialize from files again;
- after each task, Codex updates the active context and appends daily history;
- if context is reset, Codex re-reads the workflow, `AGENTS.md`, active context, task files, and repository state.

### 3.3 Active Context File

The active context file is the rolling current-state summary. It is optimized for fast resume.

It should contain the newest task at the top, using sections like:

```md
## Active Update YYYY-MM-DD <TaskId or Short Name>
Goal:
Status:
Done:
- ...
Next:
Notes:
Blockers:
```

The active context file should answer these questions without reading the chat:

- What is the current or most recent task?
- Is it completed, in progress, or blocked?
- What files/artifacts were changed?
- Which commands/tests/builds were run?
- What were the important results?
- What should happen next?
- What blockers require user input?
- What decisions must be preserved?

The active context is not a full transcript. It is a curated operational summary.

### 3.4 History Files

History files are append-only daily journals.

The naming convention is:

```text
Codex/Contexts/History/<ActiveContextBaseName>-YYYY-MM-DD.md
```

For example:

```text
Codex/Contexts/History/ContextAdmin-2026-04-30.md
```

Every completed non-`start` task appends one entry:

```md
## <CapturedAtUtc>
Request:
<exact user prompt text>

Result:
<short factual summary of what was done or why execution stopped>
```

`<CapturedAtUtc>` must be UTC in ISO 8601 round-trip format, for example:

```text
2026-04-30T15:20:56.1656334+00:00
```

History files are not rewritten. They accumulate the chronological trail. This is important because the active context can be edited into a
rolling summary, while history preserves the sequence of user requests and task results.

## 4. Bootstrap Flow For `start`

The exact prompt `start` is special.

When the user prompt is exactly:

```text
start
```

Codex must do only the bootstrap phase:

1. Read `Workflow.md`.
2. Read `AGENTS.md`.
3. Read all `*.md` files from `Codex/Contexts/History`, sorted by filename ascending.
4. Read the `ActiveContextFile` from `AGENTS.md`.
5. Output exactly one line:

```text
Current context file: <ActiveContextFile>
```

No extra diagnostics, explanations, plans, greetings, or task work are allowed during bootstrap.

Why this works:

- reading history sorted ascending reconstructs long-term chronology;
- reading active context reconstructs current state;
- outputting only the current context path confirms bootstrap without polluting the session;
- the next user message can ask "Where did we stop?" or instruct Codex to continue.

## 5. Task Initialization Flow For Every Non-`start` Prompt

For every prompt other than `start`, Codex must not assume chat continuity. It must reinitialize from repository files.

Required steps:

1. Run project-required repository sync, if the project uses Git:

```powershell
git pull --ff-only
```

2. Re-read:

```text
Codex/Rules/Workflow.md
AGENTS.md
Codex/Rules/CodingRules.md
```

3. Determine the active context file path:

- if the current user prompt explicitly provides a context path, use it and update `AGENTS.md`;
- otherwise use `ActiveContextFile` from `AGENTS.md`;
- otherwise fall back to a default such as `Context.md`.

4. If the active context file does not exist, create it from the project template.

5. Read the active context file before task logic.

6. Read task-specific documents, for example plan files, prompt files, run reports, skip reports, task evidence, or backlog files.

7. Inspect repository state:

```powershell
git status --porcelain=v1
git log -1 --oneline
```

8. Continue from the persisted state.

The key behavior is that Codex treats repository state as the source of truth. It should not ask the user to repeat context when the context can be
recovered from files.

## 6. Task Execution Flow

During a task, Codex should persist important progress in files, not just in chat.

For long tasks:

- update task evidence files;
- update run reports;
- update matrix/plan files if the task changes status;
- write blockers explicitly;
- keep the active context ready for a resume at any time.

The active context does not need to be updated after every command, but it must be updated after every completed task, and before stopping on a
blocker.

## 7. Task Finalization Flow

A task is complete only when the result is persisted.

At the end of every completed task:

1. Update the active context file with a new top section.
2. Append one daily history entry.
3. Run project-required verification commands.
4. Stage all current working tree changes unless the user explicitly requested exclusions.
5. Run staged diff checks.
6. Commit.
7. Push.
8. Verify the working tree is clean.
9. Verify the last commit id.

Typical Git sequence:

```powershell
git status --porcelain=v1
git diff --check
git add -- <changed-files>
git diff --cached --check
git commit -m "Short meaningful message"
git push
git status --porcelain=v1
git log -1 --oneline
```

If no source files changed and the task is answer-only, the project may still update active context and history so the answer is not lost.

## 8. Active Context Update Template

Use this template for the top of the active context file:

```md
## Active Update YYYY-MM-DD <Task Name>
Goal: <one sentence>
Status: Completed | In Progress | Blocked
Done:
- <concrete artifact or decision>
- <concrete artifact or decision>
Next: <next task or "None">
Notes: <commands/tests/builds/checks and important observations>
Blockers: <None or precise blocker requiring user action>
```

Rules for good active-context entries:

- write facts, not vague impressions;
- name changed files;
- name tests/builds/checks and whether they passed;
- include commit ids when available;
- include the exact blocker if execution stopped;
- include user decisions that must not be lost;
- keep newest entries at the top.

## 9. Daily History Entry Template

Append this to the daily history file:

```md
## <CapturedAtUtc>
Request:
<exact user prompt text>

Result:
<short factual summary of what was done or why execution stopped>
```

Rules:

- append, never overwrite;
- preserve UTF-8;
- use UTF-8 with BOM on Windows if tools otherwise mis-detect Cyrillic;
- capture the user's prompt exactly enough to understand why the task happened;
- keep result concise but specific;
- include stopped/blocker status when applicable.

## 10. Encoding Rules

For Windows projects, explicitly preserve UTF-8 when writing context/history files.

Avoid shell redirection that depends on the console code page. Prefer explicit encoding when appending with PowerShell. If scripts are used, make
them write UTF-8 explicitly.

This matters for Cyrillic text: bad encoding can make history unreadable after a reset.

## 11. What To Put In Active Context Versus History

Use active context for the current operational state:

- current task;
- latest completed task;
- next step;
- blockers;
- important decisions;
- verification status;
- commit id;
- known caveats.

Use history for chronological audit:

- every completed request;
- short factual result;
- stop reason;
- enough detail to reconstruct the sequence.

Do not put huge command output into either file. Store full logs in separate artifacts only when they are needed.

## 12. Recovery After Overnight Loss

When Codex loses context overnight, the next session should work like this:

1. User sends:

```text
start
```

2. Codex reads workflow, agents, all history, and active context.

3. Codex outputs:

```text
Current context file: Codex/Contexts/Context<ProjectName>.md
```

4. User asks:

```text
На чём мы остановились?
```

5. Codex answers from active context and history, not from memory.

6. User can say:

```text
Продолжай дальше автономно, останавливайся только на настоящих блокерах.
```

7. Codex continues from persisted artifacts.

## 13. Exact Minimal `AGENTS.md` For A New Project

Use this as a starting point and adapt project-specific rules below it:

```md
# Agents rules

## Rule priority

Workflow.md has ABSOLUTE priority over this document.
In case of any conflict, Workflow.md MUST be followed.

---

## 1. Context file display (Bootstrap mode only)

This section applies ONLY when the user prompt is exactly:
`start`

For any other prompt, this section MUST NOT be executed,
and Codex MUST NOT output the bootstrap prompt.

Bootstrap mode:
- Output MUST follow section 1.2 exactly.
- HARD GATE: Before printing the bootstrap output line, Codex MUST complete all mandatory bootstrap reads from section 2 in `Codex/Rules/Workflow.md`.
- If any mandatory bootstrap step is not completed, bootstrap output is FORBIDDEN.

## 1.2 Bootstrap output format (MANDATORY)

When the user prompt is exactly `start`, Codex MUST output EXACTLY one line:

Current context file: <ActiveContextFile>

No other bootstrap lines are allowed.
No extra whitespace, prefixes, or suffixes are allowed.

---

## 2. Task execution mode (default)

For any prompt other than `start`, Codex operates in Task Execution Mode.

## 2.1 Working tree handling

- Unexpected pre-existing changes in Git working tree MUST NOT stop execution.
- Codex MUST continue in current working tree and include all current changes in the next commit by default.
- Excluding files from commit is allowed only when user explicitly requests exclusions.

---

## 3. Active context file (mutable)

ActiveContextFile: Codex/Contexts/Context<ProjectName>.md
```

## 14. Exact Minimal `Workflow.md` For A New Project

Use this as the baseline workflow:

```md
# Codex Workflow

This document defines the authoritative execution workflow for Codex.
Any deviation from this workflow invalidates the task result.

Workflow.md has ABSOLUTE priority over AGENTS.md, task prompts, skills, and any other documents.

## 0. Core Principles

- Codex is an autonomous execution agent.
- Task state MUST be derived from repository files, not conversational memory.
- Context resets are expected and normal.
- All important state MUST be persisted in files: active context, history, task reports, tests, commits.
- The absence of conversational context MUST NOT block task execution.

## 1. Workflow Phases

Codex operates through:

1. Bootstrap phase.
2. Task initialization.
3. Task execution loop.
4. Task finalization.

## 2. Bootstrap Phase (`start` only)

This phase is executed ONLY when the user prompt is exactly `start`.

Codex MUST:

- Read `Codex/Rules/Workflow.md`.
- Read `AGENTS.md`.
- Read all `*.md` files from `Codex/Contexts/History` sorted by filename ascending.
- Read `ActiveContextFile` from `AGENTS.md`.
- Output exactly: `Current context file: <ActiveContextFile>`.

No task logic is allowed in this phase.

## 3. Task Initialization Phase

For any prompt other than `start`, Codex MUST:

- run `git pull --ff-only` if the project uses Git;
- re-read `Codex/Rules/Workflow.md`;
- re-read `AGENTS.md`;
- re-read project coding/task rules;
- determine the active context path;
- create the active context file if missing;
- read the active context file;
- read task-specific files;
- inspect `git status --porcelain=v1`;
- resume from persisted repository state.

## 4. Task Execution Loop

Codex MUST:

- execute task steps sequentially;
- persist progress in files;
- update task evidence and reports;
- continue autonomously when a reasonable fallback exists;
- stop only on true blockers.

Codex MUST NOT:

- rely on conversational memory;
- pause because of ordinary uncertainty;
- ask questions when repository context is sufficient.

## 5. Task Finalization

After every completed non-`start` task, Codex MUST:

- update the active context file;
- append one daily history entry;
- run required checks;
- commit and push changes when repository files changed;
- verify clean working tree;
- report the last commit id.

## 6. Daily History Entry Format

Append one entry to `Codex/Contexts/History/<ActiveContextBaseName>-YYYY-MM-DD.md`:

```md
## <CapturedAtUtc>
Request:
<exact user prompt text>

Result:
<short factual summary of what was done or why execution stopped>
```

`<CapturedAtUtc>` must be UTC ISO 8601 round-trip format.

## 7. Context Reset Handling

After any context reset, Codex MUST:

1. Re-enter task initialization.
2. Re-read `Workflow.md` and `AGENTS.md`.
3. Re-read the active context file.
4. Inspect repository state.
5. Resume from persisted artifacts.
```

## 15. Initial Active Context File

Create:

```text
Codex/Contexts/Context<ProjectName>.md
```

Initial content:

```md
## Active Update YYYY-MM-DD Bootstrap
Goal: Initialize persistent Codex context for this project.
Status: Completed
Done:
- Created `AGENTS.md`.
- Created `Codex/Rules/Workflow.md`.
- Created active context and history directories.
Next: Use `start` in new Codex sessions, then continue from this context.
Notes: Repository-local context is authoritative; chat memory is not authoritative.
Blockers: None.
```

## 16. Initial History File

Create the first history file for the current local date:

```text
Codex/Contexts/History/Context<ProjectName>-YYYY-MM-DD.md
```

Initial content:

```md
## <CapturedAtUtc>
Request:
Initialize persistent Codex context.

Result:
Created active context and history workflow so future Codex sessions can resume from repository files.
```

## 17. Mandatory Behavior For The New Codex Client

Give the new Codex client this instruction:

```text
You must treat repository files as authoritative state. On `start`, follow bootstrap exactly. On every other prompt, re-read Workflow.md,
AGENTS.md, the active context file, task-specific files, and git status before acting. After every completed task, update the active context,
append daily history, commit, and push. If context is lost, resume from these files instead of asking me to reconstruct the chat.
```

## 18. Common Failure Modes To Prevent

Do not allow these behaviors:

- Codex says "I remember" without reading active context.
- Codex starts work after `start` instead of outputting only the context line.
- Codex appends no history after a completed task.
- Codex updates history but not active context.
- Codex overwrites history instead of appending.
- Codex stores the next step only in chat.
- Codex treats a context reset as a blocker.
- Codex asks the user what happened before reading repository context.
- Codex changes the active context path without updating `AGENTS.md`.
- Codex leaves uncommitted context/history changes after a completed task.

## 19. Recommended Prompt For Starting A New Project

After creating the files above, use this prompt in the new project:

```text
start
```

Then:

```text
На чём мы остановились?
```

Then:

```text
Продолжай автономно по сохранённому контексту. Останавливайся только на настоящих блокерах, когда следующий шаг технически невозможен без моего участия.
```

## 20. Why This Preserves Context Overnight

The model may lose the chat. The repository does not.

This workflow writes the current state into:

- one active rolling context file for quick resume;
- one append-only daily history file for chronological recovery;
- task-specific artifacts for detailed evidence;
- Git commits for durable checkpoints.

When the next session starts, Codex reads those files and reconstructs the task state. That is why this project can resume after a night without
losing the thread.
