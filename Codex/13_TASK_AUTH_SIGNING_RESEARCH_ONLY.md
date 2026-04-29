# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 13 — Auth/signing research only, no implementation of live trading

## Goal

Research authenticated Polymarket CLOB V2 requirements and produce an implementation plan for native C# auth/signing. Do not place orders. Do not ask for real keys.

## Scope

Research, models, interfaces, test-vector plan. No real private keys. No live order placement.

## Official docs

- Authentication: https://docs.polymarket.com/api-reference/authentication
- CLOB V2 migration: https://docs.polymarket.com/v2-migration
- Post new order: https://docs.polymarket.com/api-reference/trade/post-a-new-order
- Cancel order: https://docs.polymarket.com/api-reference/trade/cancel-an-order

## Research questions

Answer in a new document `docs/auth_signing_plan.md`:

1. What are L1 headers?
2. What are L2 headers?
3. How are API credentials created/derived?
4. How is HMAC signature formed?
5. What EIP-712 domain is used for CLOB auth?
6. What EIP-712 domain is used for CLOB V2 orders?
7. What changed from CLOB V1 to V2?
8. Which fields are signed in V2 order struct?
9. Which fields are sent in HTTP body but not signed?
10. How should `signatureType` be chosen for EOA/proxy/safe?
11. What is `funder`/proxy wallet vs signer wallet?
12. How do makerAmount/takerAmount map to price/size for BUY/SELL?
13. How do GTD expirations work?
14. How to enforce post-only/maker-only behavior?
15. How to cancel one order / all orders?
16. What can be safely tested without real keys?

## Deliverables

Create:

```text
docs/auth_signing_plan.md
src/PolyCopyTrader.Polymarket/Auth/README.md
```

Add interfaces only:

```csharp
public interface IPolymarketAuthService
{
    Task<AuthReadinessStatus> GetReadinessAsync(CancellationToken ct);
}

public interface IPolymarketTradingClient
{
    // Placeholder only. Do not implement live order sending yet.
}
```

## Security design

Propose secure secret handling:

- environment variables for development
- Windows DPAPI / Credential Manager for VPS
- never log secrets
- separate trading wallet
- tiny bankroll
- manual live enable flag

## Acceptance criteria

1. Auth/signing research doc is created.
2. Project still has no live order placement implementation.
3. No private key is requested.
4. Interfaces are placeholders only.
5. Dashboard may show `Auth: NotConfigured`.
6. Tests still pass.

## What to avoid

- Do not implement `POST /order` yet.
- Do not create a UI field for entering private keys unless secure storage is implemented in a later task.
- Do not use real secrets in tests.
