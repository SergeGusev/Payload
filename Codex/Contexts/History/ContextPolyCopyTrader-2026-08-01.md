# PolyCopyTrader Context History - 2026-08-01

## 2026-07-31T22:17:22.8497320Z
Request:
Выложил, проверяй

Result:
Verified the new production deployment read-only. The running service is build `8765a8d1`, which includes `7e74c4ee`; all 550 LowEnter/LowerEnter strategies are enabled, Paper-only, and unpaused. In the first two complete post-start cycles all 550 were evaluated, and every fresh LowEnter/LowerEnter Paper order used an immutable FAK intent, hard `$0.50` price cap, persisted decision-time book, and no legacy `$0.99` sweep. Fill prices did not exceed `$0.50`, no fresh Live orders or parity-specific API errors were found, and the exact BTC 2 bps LowEnter strategy had three qualifying-time evaluations but no entry. Clarified that the win-rate-minus-one-cent dynamic cap is not implemented and old history was not replayed. Production was not changed.
