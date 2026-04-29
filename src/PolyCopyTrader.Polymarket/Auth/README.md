# Polymarket Auth

Current status: authenticated header infrastructure only.

Task 14 implements secure secret lookup, L2 HMAC signing, L2 header construction, and
auth readiness reporting. It still does not implement private-key loading, L1 API-key
creation, order signing, order posting, or live cancellation.

Implementation must follow `docs/auth_signing_plan.md`:

- L1 auth signs the `ClobAuthDomain` EIP-712 message to create or derive API keys.
- L2 auth signs requests with HMAC-SHA256 using the API secret. The HMAC message is
  `timestamp + method + requestPath + serializedBodyIfPresent`.
- CLOB V2 order signing uses the `Polymarket CTF Exchange` domain version `2`.
- Live order posting is out of scope until the dedicated live-trading task.

Configured secret providers:

- `Environment`: secret names are environment variable names.
- `CredentialManager`: secret names are Windows Credential Manager generic credential
  targets.

`PolymarketAuth` config may contain provider names and lookup names only. It must not
contain private keys, API secrets, passphrases, or raw header values.

Do not add private key fields to the dashboard. Do not log auth headers, API secrets,
passphrases, private keys, or signatures.
