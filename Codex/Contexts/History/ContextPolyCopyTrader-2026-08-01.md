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

## 2026-08-01T06:35:15.9259296Z
Request:
Давай здесь продолжим. Конечная цель - как можно сильнее оптимизировать работу с базой, не теряя истории.

Result:
Completed a bounded read-only production baseline for continued database optimization. At `2026-08-01T06:18:28.135921Z` the exact database size was `73,633,044,159` bytes, up `1,197,072,384` bytes over `60.0590h` from the July 29 baseline, or `0.47836 GB/day`; the lower slope is not yet a durable forecast. Exact last-24h flow was `695,649` runs, including `613,591 Skipped`, versus `79,338` Paper orders. At the 48-hour cutoff there were `263,192` age-qualified pre-blocker `PaperOnly/Skipped` rows across `2,325` strategies with zero intrinsic bet/settlement fields, but the deployed row-by-row blocker function could not return an exact count or even the first 20 dependency-qualified IDs within a 10-second timeout. Therefore retention apply remains blocked and no rollup, tombstone, or deletion occurred. Identified history-preserving priorities: optimize the exact retention query first; separately review removal of the `4,084,785,152`-byte zero-scan `ix_paper_positions_wallet_updated`; concurrently rebuild three open-position partial indexes occupying `1,222,754,304` bytes for only `446` current rows and the approximately `299.3 MB` actively used Dashboard event indexes; and reduce proven Dashboard lifetime/recent write amplification. Production remained read-only; no DDL, VACUUM/REINDEX, service/configuration/order change, deployment, backup, or temp artifact occurred. The context update was committed locally but intentionally not pushed because local `master` was already six commits ahead of `origin/master` and a push would publish those broader pre-existing commits as well.

## 2026-08-01T07:04:52.4256863Z
Request:
Ок, вперёд

Result:
Implemented a local set-based retention eligibility rewrite: all intrinsic and external blockers are now visible to PostgreSQL as direct, explicitly `public`-qualified relational predicates rather than a composite blocker-function call per candidate, while preview, summary, and serializable transfer continue to share one exact filter and both production gates remain false. Expanded focused tests to protect actual Entered/Settled Paper order/fill/position/settlement graphs, Live guard, Live-order and shadow-decision promotion, stale external dependencies, every individual external blocker, hostile `search_path`, legacy-function parity, and a non-zero diagnostic benchmark. Retention unit tests passed `7/7`; an actual disposable PostgreSQL 17 suite passed `8/8` with no skipped tests, and the same tests now report an explicit skip when the PostgreSQL environment is absent. On a `1,000`-run fixture with `500` eligible and `500` Paper-order-blocked rows, exact summary measured `116.69 ms` and preview `129.20 ms`; this is local evidence, not a production forecast or performance gate. Post-test checks found zero fixture residue. A broader unit invocation from external artifacts failed because existing unrelated tests assume standard in-repository output/source paths. Independently proved that Serializable recheck does not prevent a Paper/Live/shadow dependency committed after raw-run deletion; the current tombstone cannot restore or protect that case. Production apply therefore remains blocked pending an explicit late-dependency policy. No production access, deployment, DDL, configuration, service, order, backup, or data change occurred. The disposable PostgreSQL process was stopped and protected cleanup removed the complete marked temp session (`3,044` files / `385,807,503` bytes); its path and port were verified gone.
