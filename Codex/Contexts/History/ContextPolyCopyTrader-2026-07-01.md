## 2026-06-30T21:20:35.8082438+00:00
Request:
Поле Notional в Paper ставках означает, какая фактическая сумма была поставлена, правильно?

Result:
Reviewed Paper order creation, Dashboard binding, and fill accounting. Clarified that Paper `Notional` is the created order notional, usually `price * sizeShares` or the signal's proposed notional, and is not always the actual filled amount. Actual Paper fill cost is derived from `paper_fills` as `fill.Price * fill.SizeShares`; it matches `Notional` only when the full order fills at the order price. No production DB writes, live orders, service restart, cancel action, source-code behavior changes, build, or tests were performed.
