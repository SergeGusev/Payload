## 2026-08-04T05:46:39.6768549Z
Request:
Выложил, проверяй

Result:
Verified production deployment strictly read-only against exact primary `192.168.0.101:5432/polycopytrader`. The service is healthy on exact commit `014bc3de794df01fac545a4727f3f4d28975aa9a`, which contains removal of the modern schema declaration for `ix_paper_positions_wallet_updated`; heartbeat advanced across multiple one-minute periods, status stayed `Running / Live`, and `last_error` stayed null. The still-present target index was not rebuilt, remains valid at OID/relfilenode `133896`, occupies `4,330,291,200` bytes, and still has zero scans/no last scan. All nine retained Paper, Live, and Live-shadow history tables preserved their exact pre-deploy OID/relfilenode and had delete delta zero under the unchanged statistics epoch; Paper activity continued, and one indexed canary per table remained present. No long transaction, active index build, or target lock wait existed. No production or source state was changed. Before any separately approved concurrent drop, a fresh server-side process/service inventory is still required because PostgreSQL cannot conclusively exclude a dormant legacy process.
