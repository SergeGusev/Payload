# PolyCopyTrader History — 2026-08-16 UTC

## 2026-08-16T06:46:41.304596Z — Maker precision / resolved-ledger deployment verification
- User request: `Выложил, проверяй`.
- Mode: production read-only verification against PostgreSQL `192.168.0.101:5432/polycopytrader`, UTC, `REPEATABLE READ READ ONLY`, `statement_timeout=15s`.
- Service was active (`Running/Live`) on exact deployed build `ede8399191a11ff6090ea2875038f2a1d0071a8b`, but this build did not contain the approved Maker 5-tick precision correction or canonical resolved-ledger settlement fallback from corrected commit `35f72c0c1045851213a5062eda78393ee98ed888`.
- Runtime independently confirmed the missing settlement fallback: the exact 230 Aug-05 BTC/ETH/SOL Paper runs remained `Entered`, with 0 `settlement_resolution` evidence rows and 0 matching position settlements; stake remained `$4,420`.
- Core Paper betting remained active and structurally correct: 325/325 post-start orders Filled, linkage defects 0, late-at/after-market-end entries 0, maximum placement latency `2.842s`. Current references and WebSockets were healthy; brief recovered reference-staleness and Copy-performance stream errors remained warning-level.
- Safe next action: deploy/merge corrected commit `35f72c0c` and re-run the same read-only verification. Codex made no production, database, service, configuration, strategy, or order changes.

## 2026-08-16T06:55:14.4907581Z
Request:
А откуда у нас изменения в другой ветке появились? Вливай, перевыложу

Result:
Установлено, что исправление выполнялось в отдельном изолированном worktree на ветке `codex/maker-precision-resolved-ledger-settlement`, чтобы не смешивать его с параллельной работой текущей ветки. Exact commit `35f72c0c1045851213a5062eda78393ee98ed888` влит без squash в `codex/reference-average-available-windows` merge-коммитом `022998904759898fa3fbf5267f09f5e0b8f9bd37`; сохранены approval/contract history и обе линии разработки. Единственный конфликт был в активном context Markdown и разрешён сохранением обеих записей; product-код конфликтов не имел. Debug solution build прошёл с 0 ошибок, focused Maker/settlement regression — 90/90, staged/range RequirementGate и diff-check прошли. Production и сервер не изменялись.
