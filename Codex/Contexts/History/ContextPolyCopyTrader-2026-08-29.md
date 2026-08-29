## 2026-08-29T08:20:02.6062183Z
Request:
Выложил, проверяй

Result:
Verified exact deployed build `c82e05194aa1ca9047aeff0942231f7ab3ef25c1` read-only. The service is Running/Live with a fresh heartbeat and no last error. At `2026-08-29T08:14:23.382Z` it selected exact strategy `b7c50005-0000-4000-8079-000000000150` as the closed priority while preserving real current rank 194 and Gross `303.07384912`; six Exact pages then scanned 300 fallback-eligible targets with zero donors and zero deferrals. Production SQL still showed 406 old Paper and 403 old Live Net-missing rows and zero post-deploy audit rows because fallback had not begun; all 131 post-cutoff Paper and 131 post-cutoff Live rows were Net-complete. Checked logs had no ERR or FTL. A startup warning burst had ended in the checked interval, and one transient waiting database lock disappeared before the immediate detail query. No Production state changed.

## 2026-08-29T08:25:21.780641Z
Request:
Выложил, проверяй

Result:
Continued the exact read-only deployment verification. The formerly failing Legacy rank-2 query completed in 6,325 ms, then in 72 ms and 153 ms, reached the strategy boundary and advanced to rank 3 without retry, ERR, lock deferral or query cancellation. ETH Up 50 remained selected through ten Exact pages / 500 fallback-eligible targets with zero deferrals; fallback writes had not begun. At the exact cutoff the service was Running on build `c82e05194aa1ca9047aeff0942231f7ab3ef25c1` and had 717 post-start Paper orders across 311 strategies, 716 fills and one expiry. The expiry exposed a separate Maker-GTD defect: one order emitted 11,010 shape-mismatch warnings because its requested `created_at_utc` differed from the persisted value by 5 .NET ticks across a PostgreSQL-microsecond rounding boundary, then expired with no fill; another Maker-GTD order filled. Slow critical market-data bursts peaked at 11.563 seconds queue delay but drained, after which both websocket components were Connected/fresh/non-stale and book refreshes were healthy. No Production state changed.
