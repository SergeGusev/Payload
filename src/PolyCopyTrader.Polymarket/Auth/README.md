# Polymarket Auth

Current status: authenticated header infrastructure, safe CLOB L2 API credential
bootstrap, dry-run CLOB V2 order signing, gated live maker-only Follow leader
scaffolding, and gated BTC 5-minute GTD order submission.

Task 14 implemented secure secret lookup, L2 HMAC signing, L2 header
construction, and auth readiness reporting. A later bootstrap command added L1
`ClobAuth` signing for CLOB API credential create/derive and stores returned L2
credentials in Windows Credential Manager without printing the values. Task 15
implemented dry-run-only CLOB V2 order construction, amount conversion, EIP-712
signing, and redacted payload rendering. Task 16 implements live `POST /order`,
cancel-one, cancel-all, and order-status polling, but only behind service-level
live gates.

Implementation must follow `docs/auth_signing_plan.md`:

- L1 auth signs the `ClobAuthDomain` EIP-712 message to create or derive API keys.
- L2 auth signs requests with HMAC-SHA256 using the API secret. The HMAC message is
  `timestamp + method + requestPath + serializedBodyIfPresent`.
- CLOB V2 order signing uses the `Polymarket CTF Exchange` domain version `2`.
- Follow leader live order posting must remain BUY-only, GTD-only, post-only,
  tiny-size, and manually enabled; current leader-price Paper signals are rejected
  by live maker-only preflight. BTC 5-minute live stakes use BUY-only `GTD`
  orders with `postOnly=false` after the separate BTC live gates pass.

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

To create or derive CLOB L2 API credentials for a configured signer, keep
`Bot:Mode` non-live and `Bot:EnableLiveTrading=false`, then run from the service
output directory:

```powershell
.\PolyCopyTrader.Service.exe --bootstrap-polymarket-api-credentials
```

The command reads the order-signing key from the configured secret provider,
signs the L1 `ClobAuth` message, calls CLOB `derive-api-key` and falls back to
`api-key` creation when no key exists, then writes only the configured
Credential Manager targets. It prints redacted status and target names only.

To validate local readiness without sending authenticated HTTP requests:

```powershell
.\PolyCopyTrader.Service.exe --auth-readiness-smoke
.\PolyCopyTrader.Service.exe --clob-authenticated-read-smoke
.\PolyCopyTrader.Service.exe --dry-run-signing-smoke
```

The first command verifies that configured L2 credential targets can produce
local HMAC headers. The second sends a read-only CLOB `GET /trades` request
with L2 headers and does not print the response body. The third verifies local
order EIP-712 signing.

When the operator has confirmed there are no manual account orders to preserve,
the cancel endpoint can be tested explicitly:

```powershell
.\PolyCopyTrader.Service.exe --clob-cancel-all-smoke
```

This sends CLOB `DELETE /cancel-all` and prints only canceled/not-canceled
counts.

Do not add private key fields to the dashboard. Do not log auth headers, API secrets,
passphrases, private keys, or signatures. Persisted dry-run payloads must remain
redacted, and persisted live payload/response records must not include secrets or
unredacted signatures.
