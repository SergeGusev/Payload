# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 14 — CLOB V2 auth and HMAC implementation, still no live orders

## Goal

Implement native C# authenticated request support needed for CLOB V2, without placing live orders.

## Scope

Implement L2 HMAC header generation and secure config/secrets abstraction. Implement L1 credential derivation only if it can be tested safely. Do not place orders.

## Prerequisite

Complete `13_TASK_AUTH_SIGNING_RESEARCH_ONLY.md` first.

## Components

```text
SecureSecretProvider
PolymarketL2HmacSigner
PolymarketAuthHeaderFactory
PolymarketAuthReadinessService
```

## Secret storage

Implement abstraction:

```csharp
public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string name, CancellationToken ct);
}
```

Implement safe providers:

- environment variable provider
- Windows DPAPI/Credential Manager provider if practical
- test fake provider

Do not store secrets in `appsettings.json`.

## HMAC signer

Implement deterministic HMAC signing based on current official docs and SDK reference.

Inputs:

```text
apiSecret
timestamp
HTTP method
request path
body
```

Output:

```text
POLY_SIGNATURE
```

Add tests with deterministic test vectors. If official SDK test vectors are unavailable, generate vectors using a controlled fixture and document the limitation.

## Auth status

Expose status:

```text
NotConfigured
ConfiguredButUntested
Ready
Error
```

Dashboard should show auth readiness but not reveal secrets.

## Authenticated read-only endpoint

If safe, implement an authenticated read-only endpoint such as get user orders, but do not post/cancel orders yet.

## Acceptance criteria

1. HMAC signer implemented.
2. HMAC signer tested.
3. Secrets are not stored in appsettings.
4. Dashboard shows auth readiness.
5. No live order placement exists.
6. No real secrets are printed/logged.

## What to avoid

- Do not implement `POST /order` yet.
- Do not implement live cancel-all yet.
- Do not put private key into UI.
- Do not commit test secrets that resemble real keys.
