# Master prompt for Codex

Use this prompt only after placing `AGENTS.md` and the numbered task files in the repository.

```text
You are working on PolyCopyTrader. Read AGENTS.md and 00_INDEX.md first.

Important: implement tasks sequentially. Do not implement live trading until the specific live trading task is requested. Do not ask for private keys. Do not store secrets. Default mode is ReadOnly/Paper.

Start with the next unfinished numbered task file. For this run, implement only that task. Before coding, summarize your plan. After coding, run build/tests and summarize changes, limitations, and next recommended task.
```
