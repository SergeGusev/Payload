# Polymarket Auth And Signing Plan

Verified on: 2026-04-29

Scope: research and implementation tracking. Task 13 did not request keys, did not load
secrets, did not sign a live order, and did not call authenticated trading endpoints.
Task 15 signs local dry-run payloads only. A later bootstrap command added safe
L1 `ClobAuth` signing for L2 API credential derive/create. Task 16 adds live
maker-only authenticated trading endpoints behind explicit service gates.

## Sources Checked

- Polymarket Authentication: https://docs.polymarket.com/api-reference/authentication
- Polymarket CLOB V2 migration: https://docs.polymarket.com/v2-migration
- Polymarket Post a new order: https://docs.polymarket.com/api-reference/trade/post-a-new-order
- Polymarket Cancel single order: https://docs.polymarket.com/api-reference/trade/cancel-single-order
- Polymarket Cancel all orders: https://docs.polymarket.com/api-reference/trade/cancel-all-orders
- Official Python CLOB V2 client HMAC source: https://github.com/Polymarket/py-clob-client-v2/blob/main/py_clob_client_v2/signing/hmac.py
- Official Python CLOB V2 client order source: https://github.com/Polymarket/py-clob-client-v2/blob/main/py_clob_client_v2/order_utils/model/order_data_v2.py
- Official Python CLOB V2 typed-data source: https://github.com/Polymarket/py-clob-client-v2/blob/main/py_clob_client_v2/order_utils/model/ctf_exchange_v2_typed_data.py
- Official Python CLOB V2 contract config: https://github.com/Polymarket/py-clob-client-v2/blob/main/py_clob_client_v2/config.py

## Research Answers

1. L1 headers:
   `POLY_ADDRESS`, `POLY_SIGNATURE`, `POLY_TIMESTAMP`, and `POLY_NONCE`.
   These prove wallet control by signing the CLOB auth EIP-712 message. They are used
   to create or derive API credentials and to support local order-payload signing.

2. L2 headers:
   `POLY_ADDRESS`, `POLY_SIGNATURE`, `POLY_TIMESTAMP`, `POLY_API_KEY`, and
   `POLY_PASSPHRASE`. They are required for CLOB trading/account endpoints including
   posting signed orders, cancellations, heartbeat, balances, and user order reads.
   L2 auth does not remove the need to EIP-712 sign order payloads locally.

3. API credentials:
   Credentials are created with `POST /auth/api-key` or derived with
   `GET /auth/derive-api-key`. Both calls require L1 headers. The response includes
   API key, API secret, and passphrase. The secret must never be logged or committed.

4. HMAC signature:
   The official V2 Python client decodes the API secret with URL-safe base64, builds
   the message as:

   ```text
   timestamp + method + requestPath + serializedBodyIfPresent
   ```

   Then it computes HMAC-SHA256 over the UTF-8 message and returns a URL-safe base64
   digest. The `requestPath` is the path only, for example `/order` or `/cancel-all`.
   The body used for signing must be exactly the JSON bytes sent on the wire, so the
   C# implementation should serialize once and reuse that string for both HMAC and
   the HTTP request.

5. CLOB auth EIP-712 domain:
   L1 auth signs a `ClobAuth` struct under domain:

   ```text
   name: ClobAuthDomain
   version: 1
   chainId: 137
   ```

   The signed value contains `address`, `timestamp` as a string, `nonce`, and the
   fixed attestation message `This message attests that I control the given wallet`.
   Polymarket's V2 migration notes that this auth domain remains version `1`.

6. CLOB V2 order EIP-712 domain:
   V2 orders use:

   ```text
   name: Polymarket CTF Exchange
   version: 2
   chainId: 137
   verifyingContract: 0xE111180000d2663C0091e4f400237545B87B996B
   ```

   Neg-risk markets use verifying contract
   `0xe2222d279d744050d28e00520010520000310F59`.

7. CLOB V1 to V2 changes:
   - The exchange contract and neg-risk exchange contract changed.
   - The exchange EIP-712 domain version changed from `1` to `2`.
   - Order uniqueness moved away from caller-managed nonces to timestamp-based order
     values.
   - The signed order struct drops `taker`, `expiration`, `nonce`, and `feeRateBps`.
   - The signed order struct adds `timestamp`, `metadata`, and `builder`.
   - Fees are determined at match time and are not embedded in signed orders.
   - Builder attribution moved from builder HMAC headers to a `builderCode`/`builder`
     field attached to orders.
   - Collateral migrated from USDC.e to pUSD.

8. Fields signed in the V2 order struct:

   ```text
   salt: uint256
   maker: address
   signer: address
   tokenId: uint256
   makerAmount: uint256
   takerAmount: uint256
   side: uint8
   signatureType: uint8
   timestamp: uint256
   metadata: bytes32
   builder: bytes32
   ```

9. Fields sent in the HTTP body but not signed:
   The V2 POST `/order` body includes a signed `order` object plus top-level order
   metadata. `signature` is the EIP-712 signature over the signed struct, not an
   input field to that struct. Top-level fields such as `owner`, `orderType`,
   `deferExec`, and SDK-supported `postOnly` are transport or matching controls
   and are not part of the EIP-712 signed struct.

   Native C# follows the current V2 order shape with `timestamp`, `metadata`,
   and `builder` in the signed order struct. It must not send the older signed
   `taker`, `nonce`, or `feeRateBps` fields.

10. `signatureType` choice:
    - `0` EOA: standard externally owned wallet; funder is the EOA address.
    - `1` POLY_PROXY: Polymarket proxy wallet for Magic/email style accounts.
    - `2` POLY_GNOSIS_SAFE: Polymarket Gnosis Safe proxy wallet.
    - `3` POLY_1271: Polymarket deposit wallet. This uses the same V2 order fields,
      but `maker` and `signer` are both the deposit wallet address, while the owner
      EOA signs an ERC-7739 `TypedDataSign` wrapper. The wrapped signature appends
      the app domain separator, order struct hash, order type string, and type-string
      length to the 65-byte inner signature.

    The app should not guess this from a private key. It should require explicit
    configuration and show `NotConfigured` until the user chooses a signature type and
    funder model.

11. `funder` or proxy wallet versus signer wallet:
    The signer is the wallet/key that signs EIP-712 messages. The funder/maker is the
    address holding funds on Polymarket. For an EOA they are normally the same address.
    For proxy or safe users, the signer can be an EOA while the funder/maker is the
    Polymarket proxy/safe wallet shown on Polymarket. Orders should use `maker=funder`
    and `signer=signing address`. For deposit-wallet `POLY_1271` users, the order
    payload should use `maker=funder` and `signer=funder`; the private key still
    belongs to the configured EOA `SigningAddress`.

12. BUY/SELL makerAmount/takerAmount mapping:
    The official V2 builder uses 6-decimal token units for collateral and conditional
    tokens.

    For a limit BUY:
    - `makerAmount = size * price` in collateral units.
    - `takerAmount = size` in conditional-token units.
    - Effective price is `makerAmount / takerAmount`.

    For a limit SELL:
    - `makerAmount = size` in conditional-token units.
    - `takerAmount = size * price` in collateral units.
    - Effective price is `takerAmount / makerAmount`.

    The C# implementation should use decimal and integer fixed-math conversions, not
    binary floating point, and should round according to the market tick size and
    minimum order-size rules from CLOB market metadata.

13. GTD expirations:
    Use `orderType: "GTD"` and include `expiration` as a UNIX timestamp in seconds in
    the POST `/order` wire body. In V2 this field is not signed. For GTC orders use
    expiration `0`. The implementation should validate UTC timestamps, enforce a
    future expiration with a small clock-skew buffer, and use server time for readiness
    checks when possible.

14. Post-only/maker-only behavior:
    Use `postOnly: true` with GTC or GTD orders. Do not combine post-only with FOK or
    FAK. Before creating a post-only order, preflight against fresh order book data:
    a BUY must not cross the best ask and a SELL must not cross the best bid. The live
    path should reject any response that reports an immediate match for a maker-only
    order and should keep the current paper/risk gates in front of signing. Current
    Follow leader Paper signal prices use the leader's historical trade price, so
    the live path must reject them unless a separate live execution policy is
    explicitly added.
    BTC 5-minute live-shadow tests use BUY-only GTD limit orders with
    `postOnly: false`, `OpeningLimitGtdTtlSeconds` (`120` seconds by default), a
    shared Paper-shadow/Live decision, and the same live/manual/risk/strategy-balance
    gates before signing.

15. Cancel one order or all orders:
    Cancel one order with L2 headers:

    ```text
    DELETE /order
    body: { "orderID": "0x..." }
    ```

    Cancel all open orders for the authenticated user with L2 headers:

    ```text
    DELETE /cancel-all
    body: none
    ```

    Both official pages state cancellation works even in cancel-only mode. Cancellation
    should be implemented before or alongside any live post path so the kill switch can
    remove open live orders.

16. Safe tests without real keys:
    - HMAC test vectors with fake base64 secrets.
    - Header construction with fake API credentials and deterministic timestamp/body.
    - EIP-712 typed-data JSON construction using a known local development private key.
    - Order amount fixed-math tests for BUY/SELL without network calls.
    - Payload serialization tests proving the HMAC body matches the HTTP body.
    - Dashboard readiness tests for `NotConfigured`.
    - Static tests that live `POST /order` is unreachable unless live mode, manual
      enablement, auth readiness, geoblock, risk, and kill-switch gates pass.

## Native C# Implementation Plan

Task 14 should implement auth/HMAC infrastructure only:

- Done in task 14: strongly typed auth options with lookup-name references only.
- Done in task 14: environment-variable and Windows Credential Manager secret providers.
- Done in task 14: `IPolymarketAuthService` readiness implementation.
- Done after task 16: L1 EIP-712 auth header builder and a command-mode CLOB API
  credential bootstrap path for create/derive using the configured signing key.
- Done in task 14: L2 HMAC header builder with deterministic serialized-body input.
- Done in task 14: fake-secret tests and an official Python-client HMAC test vector.
- Still true after task 14: `IPolymarketTradingClient` has no order-posting methods.

Task 15 added dry-run order signing only:

- Done in task 15: V2 order models and EIP-712 typed-data signing.
- Done in task 15: fixed-math BUY/SELL amount conversion tests.
- Done in task 15: dry-run signature and payload rendering without HTTP submission.
- Done in task 15: `postOnly` payload support and rejection for FOK/FAK combinations.
- Done in task 15: unsigned, signed, and rejected dry-run records persisted for the dashboard.
- Still true after task 15: live POST and cancellation remain disabled and unreachable.

Task 15 tests use a deterministic public local development key only. It is not a
secret and must never be funded.

Task 16 added live maker-only trading only if all prior gates pass:

- Done in task 16: manual `EnableLiveTrading`, `Bot.Mode=Live`, and
  `LiveTrading.ManualEnableCode=LIVE_TRADING_ENABLED` gates.
- Done in task 16: tiny order cap, explicit signer/funder/signature type, and fail-closed
  auth readiness.
- Done in task 16: cancel-one, cancel-all, and order-status polling.
- Done in task 16: kill switch pauses live trading and requests cancel-all.
- Done in task 16: geoblock, CLOB server-time drift, API-error lockout, stale order,
  maker-only spread, BUY-only, GTD-only, and blocked crypto/sports text checks.
- Still required operationally: start with tiny production orders only after VPS
  deployment, geoblock verification from the actual host, dry-run validation, and
  manual cancel-all testing.

Post-task bootstrap note:

- `--bootstrap-polymarket-api-credentials` runs only while Live is disabled, signs
  the L1 CLOB auth message locally, tries `GET /auth/derive-api-key`, falls back
  to `POST /auth/api-key` when no key exists, and stores the returned API key,
  API key owner, secret, and passphrase through Windows Credential Manager target
  names. It must not print credential values, signatures, private keys, or raw
  auth headers.

## Security Design

- Development secrets: environment variables only.
- VPS secrets: Windows DPAPI, Windows Credential Manager, or another OS-backed secret
  store. Do not persist private keys or API secrets in JSON appsettings.
- Logging: redact private keys, API secret, API key, passphrase, signatures, and
  authorization headers.
- Wallet isolation: use a dedicated trading wallet and a tiny bankroll.
- Live gate: require a manual live enable flag and fail closed when auth is incomplete.
- Dashboard: show `Auth: NotConfigured` until all required inputs are configured and
  validated.
- Testing: use fake test vectors and known public development keys only; never use real
  Polymarket credentials in tests.
