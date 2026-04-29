# Paper Trading Evaluation

Paper results are decision support, not a promise of live PnL. Paper fills are
approximate, long positions are marked from bid-side data, and latency/slippage can be
different in production.

## Minimum Review Areas

- Signal count and acceptance rate.
- Rejection reasons and whether they match intended filters.
- Trader-level performance.
- Category-level performance.
- Paper fill rate.
- Expired order rate.
- Average lag from leader trade to signal.
- Leader price versus proposed maker price.
- Approximate PnL and drawdown.
- API reliability and stale market-data periods.

## Viability Questions

- Are accepted signals frequent enough to matter?
- Are rejections mostly intentional rather than data failures?
- Does performance rely on one trader, category, or market type?
- Does the strategy still look reasonable after excluding lucky outliers?
- Are paper fills plausible given observed spread and depth?
- Would tiny live orders remain below risk limits?

## Red Flags

- High PnL from very few trades.
- High expired-order rate.
- Frequent missing order books.
- Frequent wide-spread rejections.
- Many stale WebSocket periods.
- Strategy changes made without written review.
- Paper success that depends on taker fills or crossing the spread.

## Decision

Before live trading, write a short review with:

- review period;
- number of signals;
- accepted/rejected counts;
- paper orders/fills/expired orders;
- approximate PnL;
- top traders and categories;
- top rejection reasons;
- known data-quality issues;
- explicit go/no-go decision.
