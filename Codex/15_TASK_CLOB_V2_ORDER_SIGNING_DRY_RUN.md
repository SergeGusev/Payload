# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 15 — CLOB V2 order signing dry-run only

## Goal

Implement native C# CLOB V2 order construction and EIP-712 signing in dry-run mode only. Do not send orders.

## Scope

Create order objects, calculate maker/taker amounts, sign locally using test keys, and validate signatures in tests. Do not call `POST /order` in production.

## Prerequisite

Complete tasks 13 and 14.

## Official docs

- CLOB V2 migration: https://docs.polymarket.com/v2-migration
- Post order endpoint: https://docs.polymarket.com/api-reference/trade/post-a-new-order
- Authentication: https://docs.polymarket.com/api-reference/authentication

## Components

```text
ClobV2OrderBuilder
ClobV2OrderSigner
OrderAmountCalculator
DryRunTradingClient
OrderSigningTests
```

## Requirements

Implement:

- BUY order amount conversion
- SELL order amount conversion
- price/tick validation
- min order size validation
- GTD expiration validation
- signature type handling model
- signed payload creation
- dry-run validation output

## Dry-run behavior

When strategy wants to place a live order but mode is `DryRun`:

1. Build order payload.
2. Sign with configured test key only if available.
3. Do not send to Polymarket.
4. Persist dry-run order payload with secrets/signature redacted as needed.
5. Show in dashboard as `DryRunSigned` or `DryRunUnsigned`.

## Test keys

Use only local deterministic test keys. They must not hold funds. Make it explicit in code comments and README.

## Validation

Test:

- price 0.74 creates expected maker/taker amounts
- BUY/SELL side mapping
- GTD expiration body field
- missing key results in `UnsignedDryRun`
- signature output stable for fixed inputs

## Acceptance criteria

1. CLOB V2 order builder exists.
2. EIP-712 dry-run signer exists.
3. Unit tests cover amount conversion and signing stability.
4. No live order is sent.
5. Dashboard shows dry-run order status if enabled.
6. Live mode remains disabled by default.

## What to avoid

- Do not use funded wallets for tests.
- Do not call `POST /order`.
- Do not assume V1 order fields are valid in V2.
- Do not implement live trading here.
