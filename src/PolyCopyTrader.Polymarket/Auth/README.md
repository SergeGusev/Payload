# Polymarket Auth

Current status: research-only placeholder.

Task 13 intentionally does not implement authenticated CLOB calls, private-key loading,
API-key creation, order signing, or live order placement. The auth namespace currently
contains only readiness/trading interfaces and the `AuthReadinessStatus` value used to
make the dashboard and future implementations explicit.

Implementation must follow `docs/auth_signing_plan.md`:

- L1 auth signs the `ClobAuthDomain` EIP-712 message to create or derive API keys.
- L2 auth signs requests with HMAC-SHA256 using the API secret.
- CLOB V2 order signing uses the `Polymarket CTF Exchange` domain version `2`.
- Live order posting is out of scope until the dedicated live-trading task.

Do not add private key fields to the dashboard. Do not log auth headers, API secrets,
passphrases, private keys, or signatures.
