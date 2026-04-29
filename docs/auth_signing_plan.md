# Polymarket Auth And Signing Plan

Verified on: 2026-04-29

Scope: research and implementation planning only. Task 13 does not request keys, does not
load secrets, does not sign a live order, and does not call authenticated trading endpoints.

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
   metadata. `expiration` remains in the `order` body for GTD/order-expiry handling
   but is not part of the EIP-712 signed struct. `signature` is the EIP-712 signature
   over the signed struct, not an input field to that struct. Top-level fields such as
   `owner`, `orderType`, `deferExec`, and SDK-supported `postOnly` are transport or
   matching controls and are not part of the EIP-712 signed struct.

   Native C# should follow the current POST `/order` endpoint and official V2 client:
   do not send legacy V1 `taker`, `nonce`, or `feeRateBps` unless the official docs
   and staging tests later require a change.

10. `signatureType` choice:
    - `0` EOA: standard externally owned wallet; funder is the EOA address.
    - `1` POLY_PROXY: Polymarket proxy wallet for Magic/email style accounts.
    - `2` POLY_GNOSIS_SAFE: Polymarket Gnosis Safe proxy wallet; docs describe this
      as the common choice for users who do not fit EOA or Magic/email proxy.
    - The official V2 Python enum also exposes `3` POLY_1271 for smart-contract
      wallet signatures. That should stay out of the first live path unless explicitly
      validated later.

    The app should not guess this from a private key. It should require explicit
    configuration and show `NotConfigured` until the user chooses a signature type and
    funder model.

11. `funder` or proxy wallet versus signer wallet:
    The signer is the wallet/key that signs EIP-712 messages. The funder/maker is the
    address holding funds on Polymarket. For an EOA they are normally the same address.
    For proxy or safe users, the signer can be an EOA while the funder/maker is the
    Polymarket proxy/safe wallet shown on Polymarket. Orders should use `maker=funder`
    and `signer=signing address`.

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
    order and should keep the current paper/risk gates in front of signing.

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
    - Static tests that no live `POST /order` implementation exists until the live task.

## Native C# Implementation Plan

Task 14 should implement auth/HMAC infrastructure only:

- Done in task 14: strongly typed auth options with lookup-name references only.
- Done in task 14: environment-variable and Windows Credential Manager secret providers.
- Done in task 14: `IPolymarketAuthService` readiness implementation.
- Deferred: L1 EIP-712 auth header builder for create/derive API credentials because it
  needs signing-key handling and should be covered by a dedicated dry-run signing task.
- Done in task 14: L2 HMAC header builder with deterministic serialized-body input.
- Done in task 14: fake-secret tests and an official Python-client HMAC test vector.
- Still true after task 14: `IPolymarketTradingClient` has no order-posting methods.

Task 15 should add dry-run order signing only:

- Add V2 order models and typed-data generation.
- Add fixed-math BUY/SELL amount conversion tests.
- Add dry-run signature and payload rendering without HTTP submission.
- Add maker-only payload checks using `postOnly`.
- Keep live POST disabled and unreachable.

Task 16 should add live maker-only trading only if all prior gates pass:

- Require manual `EnableLiveTrading` and auth readiness.
- Require tiny bankroll, separate trading wallet, and explicit signature type/funder.
- Implement cancel-one and cancel-all before allowing live order posting.
- Keep kill switch and post-only checks in the live path.
- Start with small production orders only after staging/dry-run validation.

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
