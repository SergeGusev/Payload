# Requirement Fidelity Gate

This file defines the project-wide fail-closed process that prevents an agent's
technical preference from replacing a user's requirement. It applies to every
repository change, regardless of the task, agent, session, or working directory.

## 1. Governing Principle

The user's words define the requested behavior. Engineering, safety, evidence,
and verification rules constrain implementation; they do not authorize a
different behavior. An agent must never silently add a restriction, fallback,
default, optimization, or “safer” interpretation that changes the result.

If two interpretations would produce different behavior, data, scope, risk, or
cost, implementation is blocked until the user chooses one.

## 2. When A Contract Is Required

A requirement contract is required before changing any tracked repository file
except:

- `Codex/Contexts/ContextPolyCopyTrader.md`;
- `Codex/Contexts/History/**`;
- a new task contract under `Codex/Requirements/Contracts/**` while that
  contract is being drafted.

Read-only work with no repository changes does not require a contract. Existing
contract records are immutable after completion.

## 3. Mandatory Checkpoints

### Checkpoint A: Literal contract

Before material edits, create a draft from
`Codex/Requirements/contract.template.json`. It must contain:

- every relevant user prompt copied verbatim;
- explicit `REQ-*` statements linked to exact quotes from those prompts;
- observable acceptance criteria, including examples when boundary behavior
  matters;
- exact in-scope and out-of-scope items;
- every assumption and every proposed deviation, even if the agent considers it
  harmless, conservative, safer, conventional, or technically preferable;
- planned implementation paths and verification for every requirement.

Chat history and prior design notes are navigation aids, not requirements. Only
the user's verbatim prompts in the contract authorize behavior.

### Checkpoint B: User approval

Show the literal contract and its semantic SHA-256 to the user. Do not make
material edits until a later user message approves that exact contract.

Approval must be stored verbatim in the contract and bound to the semantic
digest. Any change to the request, scope, requirements, assumptions, or
deviations invalidates approval and returns the contract to draft.

The normal approval form is:

```text
APPROVE <contract-id> <semantic-digest>
```

Approval must use that exact form. An agent, reviewer, test, documentation file,
or looser natural-language acknowledgement cannot approve on the user's behalf.

The approved contract must be committed before product edits. A product change
and its first approval record cannot appear in the same commit. This parent-
revision checkpoint makes the approval order mechanically verifiable. The sole
one-time exception is the bootstrap contract named in section 6.

After approval, implementation may continue in that approval turn. A later user
prompt is a new checkpoint: it must either be read-only or be added verbatim to
the contract and approved under a new semantic digest before further material
edits. Short phrases such as “continue” do not silently broaden or reapprove an
old contract.

### Checkpoint C: Implementation traceability

After approval, every governed changed path must map to at least one `REQ-*`.
Every requirement must map to observable verification. Unrequested changes are
forbidden, even when adjacent or convenient.

If implementation uncovers a new decision that changes meaning or behavior,
stop, update the contract, present the new digest, and obtain new approval.

### Checkpoint D: Independent semantic review

Before completion, a reviewer other than the implementation author must compare:

1. the verbatim user prompts;
2. the approved contract;
3. the actual diff;
4. the tests and evidence.

The reviewer must record a pass with no open findings. Reviewing only the design
summary or only the tests is insufficient.

### Checkpoint E: Mechanical validation

Set the contract status to `completed`, record passing evidence, and run:

```powershell
.\scripts\requirements\Validate-RequirementContract.ps1 -Mode WorkingTree
```

The staged Git hook repeats validation against the index. CI repeats it against
the exact Git range. A failure is a completion blocker, not a caveat.

## 4. Assumptions And Deviations

The normal value for both arrays is empty.

Every non-empty assumption or deviation must include its impact and a separate,
verbatim user approval tied to the current semantic digest. “Conservative”,
“safer”, “minimal”, “standard”, and “recommended” are not approvals.

Safety can block an unsafe request. It cannot silently substitute a different
requested outcome. Report the conflict and ask the smallest focused question.

## 5. Enforcement Layers And Limits

The repository uses four reinforcing layers:

- root `AGENTS.md`, automatically loaded by new Codex sessions;
- project Codex hooks in `.codex/hooks.json` after the project and hook hash are
  trusted once;
- local Git hooks in `.githooks` after running the installer;
- the requirement-gate CI workflow, which must be a required protected-branch
  check to reject non-compliant merges centrally.

These controls make omissions and unapproved changes visible and mechanically
block normal paths. They are not a mathematical proof that natural language was
understood correctly, and local hooks can be bypassed. The explicit user approval
and independent semantic review remain mandatory. Branch protection is required
for central enforcement. Preserve the contract lifecycle commits when merging;
squashing an approval commit together with its implementation destroys the
parent-revision checkpoint and must be rejected by range validation.

## 6. Bootstrap Exception

`RC-20260813-project-requirement-gates` is the single bootstrap contract for
introducing this gate. It is based on the user's explicit instruction to deploy
the checkpoints after the checkpoints had been described. This exception cannot
authorize any later product change and must not be copied or generalized. The
bootstrap change itself is locally validated and independently reviewed because
its CI workflow cannot protect the default branch until after this first merge.
