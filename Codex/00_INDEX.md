# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


## How to use this pack

Put these Markdown files at the root of the repository or in a `codex_tasks/` folder. Use **one task file at a time** with Codex. Do not paste the whole chat. Do not ask Codex to implement all tasks in one run.

`AGENTS.md` is included because Codex reads that file as persistent project instructions. Keep it at the repository root. The numbered files are task prompts to run sequentially.

## Recommended implementation order

| Order | File | Purpose |
|---:|---|---|
| 1 | `01_PROJECT_BRIEF.md` | Full product brief and constraints |
| 2 | `02_TASK_REPO_SCAFFOLD.md` | Create solution/project skeleton |
| 3 | `03_TASK_CONFIG_STORAGE_LOGGING.md` | Config, SQLite, logging |
| 4 | `04_TASK_POLYMARKET_PUBLIC_API_CLIENTS.md` | Public read-only API clients |
| 5 | `05_TASK_WATCHLIST_SCANNER.md` | Watchlist and scanner loop |
| 6 | `06_TASK_SIGNAL_AND_RISK_ENGINES.md` | Signal scoring and risk checks |
| 7 | `07_TASK_PAPER_TRADING_ENGINE.md` | Paper order/position simulation |
| 8 | `08_TASK_WPF_DASHBOARD.md` | WPF dashboard |
| 9 | `09_TASK_WORKER_SERVICE_AND_IPC.md` | 24/7 service + dashboard communication |
| 10 | `10_TASK_WEBSOCKET_MARKET_DATA.md` | Market/user websocket support for monitoring |
| 11 | `11_TASK_ANALYTICS_REPORTING.md` | Reports and strategy analytics |
| 12 | `12_TASK_TESTING_QA_HARDENING.md` | Tests, QA, resilience |
| 13 | `13_TASK_AUTH_SIGNING_RESEARCH_ONLY.md` | Research authenticated trading, no live orders |
| 14 | `14_TASK_CLOB_V2_AUTH_AND_HMAC.md` | Implement auth/HMAC, still no live orders |
| 15 | `15_TASK_CLOB_V2_ORDER_SIGNING_DRY_RUN.md` | Implement order signing dry-run only |
| 16 | `16_TASK_LIVE_TRADING_MAKER_ONLY.md` | Enable tiny maker-only live trading |
| 17 | `17_TASK_DEPLOYMENT_WINDOWS_VPS_SECURITY.md` | VPS deployment and security |
| 18 | `18_TASK_OPERATIONS_RUNBOOK.md` | Daily operation and incident playbook |

## Phases

### Phase A — safe MVP

Complete tasks 1–12. This gives a read-only + paper-trading app with UI. No keys. No live trading. This is the most important phase.

### Phase B — authenticated plumbing

Complete tasks 13–15. These tasks implement signing/authentication in a controlled, testable way but still do not place real orders.

### Phase C — live trading

Only after paper results are stable, complete task 16 with tiny maker-only orders. Use a separate trading wallet with a small bankroll.

## Non-negotiable safety constraints

- No live order placement before explicit manual approval.
- No real private keys in tests, logs, screenshots, prompts, or repository files.
- Default mode must remain `Paper` or `ReadOnly`.
- The UI must not be required for the bot to run.
- Every rejected signal must be logged with a reason.
- Every order action, even paper, must be reproducible from logs and database records.

## Official documentation links to keep nearby

- OpenAI Codex prompting: https://developers.openai.com/codex/prompting
- OpenAI Codex AGENTS.md guide: https://developers.openai.com/codex/guides/agents-md
- Polymarket CLOB V2 migration: https://docs.polymarket.com/v2-migration
- Polymarket authentication: https://docs.polymarket.com/api-reference/authentication
- Polymarket trades endpoint: https://docs.polymarket.com/api-reference/core/get-trades-for-a-user-or-markets
- Polymarket positions endpoint: https://docs.polymarket.com/api-reference/core/get-current-positions-for-a-user
- Polymarket leaderboard endpoint: https://docs.polymarket.com/api-reference/core/get-trader-leaderboard-rankings
- Polymarket order book endpoint: https://docs.polymarket.com/api-reference/market-data/get-order-book
- Polymarket WebSocket overview: https://docs.polymarket.com/market-data/websocket/overview
- Polymarket geoblock endpoint: https://docs.polymarket.com/api-reference/geoblock
