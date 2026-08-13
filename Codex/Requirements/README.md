# Requirement Contracts

Every repository mutation is governed by the fail-closed process in
`Codex/Rules/RequirementGate.md`.

## Lifecycle

1. Copy `contract.template.json` to
   `Contracts/RC-YYYYMMDD-short-description.json`.
2. Copy the user's relevant prompts verbatim and write literal `REQ-*` items.
3. Record exact scope, acceptance criteria, paths, assumptions, deviations, and
   planned verification.
4. Calculate the semantic digest with the validator, present the complete
   contract and digest to the user, and stop before product edits. The approval
   field remains pending while the digest is calculated.
5. Ask the user for exactly `APPROVE <contract-id> <semantic-digest>`. After that
   exact approval, record it verbatim, set `status` to `approved`, and commit
   that approval record before product edits.
6. Implement only mapped requirements in a later commit. The validator rejects
   a product edit whose approved contract did not already exist in its parent.
7. Record passing verification, obtain independent semantic review, set
   `status` to `completed`, and run the validator before staging and committing.

The contract record is permanent evidence. A completed contract must never be
edited, deleted, or reused for another change.

## Commands

```powershell
# Check current tracked and untracked changes.
.\scripts\requirements\Validate-RequirementContract.ps1 -Mode WorkingTree

# Print the semantic digest of a draft contract before requesting approval.
.\scripts\requirements\Validate-RequirementContract.ps1 `
  -Mode Contract `
  -ContractPath .\Codex\Requirements\Contracts\RC-YYYYMMDD-task.json `
  -AllowDraft

# Check exactly what is staged (this is what pre-commit runs).
.\scripts\requirements\Validate-RequirementContract.ps1 -Mode Staged

# Install the repository Git hooks in this clone.
.\scripts\requirements\Install-RequirementGitHooks.ps1

# Run validator regression tests in a disposable D:\CodexTemp directory.
.\scripts\requirements\Test-RequirementContractGate.ps1

# Run Codex and Git hook regression tests in the same marked session.
.\scripts\requirements\Test-CodexRequirementHooks.ps1 -TempRoot <marked-test-path>
.\scripts\requirements\Test-RequirementGitHooks.ps1 -TempRoot <marked-test-path>
```

Project Codex hooks are discovered automatically in trusted projects. Codex will
ask once to trust the exact hook definition. Use the hooks UI or `/hooks` to
review it; changed hooks require renewed trust.

Central rejection requires GitHub branch protection with `requirement-contract`
configured as a required check and direct pushes to the default branch disabled.
Do not squash an approval commit together with its implementation commit: range
validation intentionally rejects that loss of the parent-revision checkpoint.
The one-time bootstrap merge is locally validated and independently reviewed;
the workflow can centrally protect only later pull requests after it exists on
the protected base branch.
