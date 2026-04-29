# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 05 — Watchlist scanner

## Goal

Implement the background scanner that monitors configured traders, persists their latest trades and positions, and creates candidate events for the strategy engine.

## Scope

Read-only scanning. No paper decisions yet except optional placeholder events. No live trading.

## Scanner responsibilities

For each enabled trader in the watchlist:

1. Validate wallet address format.
2. Fetch recent trades with `takerOnly=false`.
3. Deduplicate by transaction hash + asset + side + timestamp, or another robust key.
4. Persist new `LeaderTrades`.
5. Fetch current positions.
6. Persist/refresh `LeaderPositions`.
7. Queue new trades for signal evaluation.
8. Write scanner health and API errors.

## Polling

Configurable:

```text
PollIntervalSeconds: default 10
MaxTradesPerTraderPerPoll: default 100
MaxPositionsPerTraderPerPoll: default 500
```

Avoid wasteful repeated calls. Respect rate limits and add backoff on 429/5xx.

## Deduplication

A leader trade may be returned multiple times across polling loops. The scanner must not create duplicate signals.

Dedup key candidates:

```text
transactionHash + asset + side + price + size
```

or:

```text
transactionHash + asset + timestamp
```

Make the chosen dedup logic explicit and test it.

## Position snapshots

Persist current positions periodically. Do not overwrite all historical data; keep snapshots or enough history to detect whether the leader is adding/reducing.

Minimum position snapshot fields:

```text
TraderWallet
ConditionId
AssetId
Outcome
Size
AvgPrice
CurrentValue
CashPnl
CurPrice
EndDate
SnapshotAtUtc
```

## Health state

Track:

```text
LastSuccessfulScanUtc
LastErrorUtc
LastErrorMessage
TradesFetched
NewTradesStored
PositionsFetched
ScannerStatus
```

## Acceptance criteria

1. Service scans configured watchlist.
2. New leader trades are stored once.
3. Leader positions are stored/refreshed.
4. Scanner status is visible in logs and dashboard-ready storage.
5. Scanner handles invalid wallet placeholders by disabling or warning without crashing.
6. Unit tests cover deduplication.
7. No paper orders or live orders are created yet unless later tasks add that.

## What to avoid

- Do not crash the whole service because one trader/API call fails.
- Do not use hardcoded trader wallets in code.
- Do not assume leaderboard top traders are automatically watchlisted.
