# PolyCopyTrader Context History - 2026-08-01

## 2026-07-31T22:17:22.8497320Z
Request:
Выложил, проверяй

Result:
Verified the new production deployment read-only. The running service is build `8765a8d1`, which includes `7e74c4ee`; all 550 LowEnter/LowerEnter strategies are enabled, Paper-only, and unpaused. In the first two complete post-start cycles all 550 were evaluated, and every fresh LowEnter/LowerEnter Paper order used an immutable FAK intent, hard `$0.50` price cap, persisted decision-time book, and no legacy `$0.99` sweep. Fill prices did not exceed `$0.50`, no fresh Live orders or parity-specific API errors were found, and the exact BTC 2 bps LowEnter strategy had three qualifying-time evaluations but no entry. Clarified that the win-rate-minus-one-cent dynamic cap is not implemented and old history was not replayed. Production was not changed.

## 2026-07-31T22:30:27.8221706Z
Request:
Продолжаем думать над WinRate. Мне нравится идея, что мы ставим всегда ниже, чем WR минус комиссия. Накидывай идей, как получить реальный WinRate

Result:
Developed a read-only design for estimating conditional LowEnter win probability rather than using raw lifetime or filled-only WR. Recommended a hierarchical parent prior updated by all LowEnter-eligible opportunities, conservative lower probability bounds, exact market-specific fee math, compact prospective evidence for skipped/no-fill candidates, and grouped walk-forward validation in ResearchOnly shadow mode. Verified that current skipped/no-fill rows lose the selected outcome and diagnostics, parent linkage is not persisted, and current Paper PnL/FAK intent does not model market fees; therefore exact historical all-opportunities WR and a trustworthy net-EV dynamic cap are not currently reconstructible. No code, DB, service, strategy, order, deployment, or remote state changed.
