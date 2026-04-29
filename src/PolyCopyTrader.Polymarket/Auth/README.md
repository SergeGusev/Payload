# Polymarket Auth

Current status: authenticated header infrastructure, dry-run CLOB V2 order signing, and gated live maker-only order submission.

Task 14 implemented secure secret lookup, L2 HMAC signing, L2 header construction, and
auth readiness reporting. Task 15 implemented dry-run-only CLOB V2 order construction,
amount conversion, EIP-712 signing, and redacted payload rendering. Task 16 implements
live `POST /order`, cancel-one, cancel-all, and order-status polling, but only behind
service-level live gates.

Implementation must follow `docs/auth_signing_plan.md`:

- L1 auth signs the `ClobAuthDomain` EIP-712 message to create or derive API keys.
- L2 auth signs requests with HMAC-SHA256 using the API secret. The HMAC message is
  `timestamp + method + requestPath + serializedBodyIfPresent`.
- CLOB V2 order signing uses the `Polymarket CTF Exchange` domain version `2`.
- Live order posting must remain BUY-only, GTD-only, post-only, tiny-size, and manually
  enabled.

Configured secret providers:

- `Environment`: secret names are environment variable names.
- `CredentialManager`: secret names are Windows Credential Manager generic credential
  targets.

`PolymarketAuth` config may contain provider names and lookup names only. It must not
contain private keys, API secrets, passphrases, or raw header values.

Dry-run signing may resolve `DryRunPrivateKeyName` through the configured secret
provider. A missing dry-run key produces an unsigned dry-run payload. Tests use a
deterministic public development key only; never fund that key and never replace it
with a real credential in repository files.

Live signing resolves `OrderSigningPrivateKeyName` through the configured secret
provider. Live API credentials also resolve through lookup names only, including
`ApiKeyOwnerName`, `ApiKeyName`, `ApiSecretName`, and `ApiPassphraseName`.

Do not add private key fields to the dashboard. Do not log auth headers, API secrets,
passphrases, private keys, or signatures. Persisted dry-run payloads must remain
redacted, and persisted live payload/response records must not include secrets or
unredacted signatures.
