## 2026-09-20T06:16:47.09154Z
Request:
Я не понимаю ситуацию.  Вот как для меня выглядят данные по стратегии ETH Up or Down 5m 22 Child ROI за последние сутки:

1. Значение Net realized увеличилось примерно с 612 до 640, то есть мы как бы выигрываем.
2. Но при этом значение Net live realized уменьшилось примерно с -10 до -36, то есть мы проигрываем.

Проверь, где расхождение берётся.

Result:
Read-only production diagnosis of exactly b7c50005-0000-4000-8195-000000000022 (sole exact name match), endpoint192.168.0.101:5432/polycopytrader. No product or runtime changes. Sources: raw run/order rows, independent SQL aggregate and PowerShell decimal recount, persisted Paper settlement evidence, current authoritative ledger and production logs. Service-reported version0e4cef2021228b78f744a120f7bb06963e24a5ef; implicated source files unchanged between that revision and currentHEAD e26163c5 (no independent binary hash audit).

Fixed half-open settlement interval [2026-09-19T06:10:26.233155Z,2026-09-20T06:10:26.233155Z), Europe/Sofia09:10:26..09:10:26.22distinct LiveIDs,22distinct PaperIDs,22linked runs,0unmatched. Asset/condition/outcome/fill-price/fill-size/fee mismatch counts all0. Paper13wins9losses,net18.42226796; Live8wins14losses,net-43.223948. Both fees4.699770. Difference61.64621596, of which five outcome mismatches61.64621602;17remaining pairs rounding residual-0.00000006. This independently reproduces the snapshot24h sums, whose stored window is05:42:13.495907UTC..05:42:13.495907UTC; no additional target settlements between those edges and our selected edges.

Lifetime snapshot sums640.31200145Paper/2472settled runs and-36.688425Live/57settled orders equal independently queried raw sums. The user's earlier612/-10 are approximate observations without timestamps; their precise original interval/deltas cannot be reconstructed as observed UI snapshots from this evidence.

Five mismatches; each Paper outcome is win and Live outcome is loss, position settlement source BtcUpDown5mResolvedLedger:BinanceTimedClose. Current same ledgerID source MarketWebSocket agrees with Live gamma_resolved_metadata outcome:
|Market slug|Live ID|Run ID|Paper net|Live net|Difference|Actual winning outcome|
|---|---|---|---:|---:|---:|---|
|eth-updown-5m-1789810500|1322b5a8-f36d-4e8f-9f2f-2e51a1de1f4e|f70816d2-7fe5-4a66-ba3c-01ecae3ba6a6|6.54335800|-6.22260000|12.76595800|Down|
|eth-updown-5m-1789835100|b5fc560d-a26b-457d-b79d-0beb5f20983e|dee68412-e2f4-4d4b-9bbe-7147859a83cf|7.71408901|-6.23940000|13.95348901|Up|
|eth-updown-5m-1789857900|ba09fa99-c941-4c6b-a2b0-e9a9a7bc2958|12aea786-f272-4f79-9560-5ce50d32f0cc|4.16842797|-6.17640000|10.34482797|Up|
|eth-updown-5m-1789868100|38e1c90b-ab86-4016-8b7f-0392ba835e60|30d1d88e-8bb0-4451-9ebe-2f6b443c217a|5.33686199|-6.20160000|11.53846199|Down|
|eth-updown-5m-1789876500|66ce83e6-a8db-4e23-b68a-32c8abfb1d54|1ee026d3-65e8-48e8-9a20-648887717edb|6.81667905|-6.22680000|13.04347905|Up|

Mechanism: CryptoUpDown5mResultPollingProcessor.cs:240-308 computes Binance start/close movement and explicitly marks result provisional=true. BtcUpDown5mPaperStrategyProcessor.cs:8092-8120 falls back from unresolvedGamma to canonicalledger;8559-8563 allows BinanceTimedClose;8290-8313 persists runSettled. PostgresAppRepository.cs:1005-1021 normal settlement query selects onlyEntered. LiveTradingProcessor.cs:283-320 resolves viaGamma and updatesLive;544+ synchronization handles fill state, not settled run outcome. Current five runs updated_at equals their original Paper settlement times, before finalWebSocket ledger updates and Live settlement.

Production log confirmations under \\192.168.0.101\CodexLogs (all Strategy=eth_up_down_5m_22_child_roi, Won=true, SettlementSource=BtcUpDown5mResolvedLedger:BinanceTimedClose):
- polycopytrader-service-20260919_004.log:19725,2026-09-19T12:40:09.058+03:00,market1789810500.
- polycopytrader-service-20260919_006.log:51112,2026-09-19T19:30:10.163+03:00,market1789835100.
- polycopytrader-service-20260920.log:74518,2026-09-20T01:50:14.343+03:00,market1789857900.
- polycopytrader-service-20260920_001.log:65523,2026-09-20T04:40:08.887+03:00,market1789868100.
- polycopytrader-service-20260920_002.log:41931,2026-09-20T07:00:11.279+03:00,market1789876500.

Reproduction SQL (execute only READ ONLY,UTC,statement_timeout15s,lock_timeout1s,idle_in_transaction_session_timeout20s,max_parallel_workers_per_gather0):
```sql
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT l.id,l.paper_order_id,r.id run_id,r.market_slug,l.asset_id,p.asset_id paper_asset,l.condition_id,p.condition_id paper_condition,l.outcome,p.outcome paper_outcome,l.average_fill_price,p.price paper_price,l.filled_size,p.size_shares paper_size,l.fee_usd,r.fee_usd paper_fee,l.net_realized_pnl_usd live_net,r.net_realized_pnl_usd paper_net,l.won,r.settlement_price,r.skip_diagnostics_json,l.winning_outcome,l.settlement_source,r.settled_at_utc paper_settled,l.settled_at_utc live_settled
FROM live_orders l LEFT JOIN paper_orders p ON p.id=l.paper_order_id LEFT JOIN strategy_market_paper_runs r ON r.paper_order_id=p.id
WHERE l.strategy_id='b7c50005-0000-4000-8195-000000000022' AND l.settled_at_utc>='2026-09-19T06:10:26.233155Z' AND l.settled_at_utc<'2026-09-20T06:10:26.233155Z' ORDER BY l.created_at_utc;
SELECT 'paper' source,count(*),sum(net_realized_pnl_usd),sum(realized_pnl_usd),sum(fee_usd) FROM strategy_market_paper_runs WHERE strategy_id='b7c50005-0000-4000-8195-000000000022' AND settled_at_utc>='2026-09-19T06:10:26.233155Z' AND settled_at_utc<'2026-09-20T06:10:26.233155Z'
UNION ALL SELECT 'live',count(*),sum(net_realized_pnl_usd),sum(realized_pnl_usd),sum(fee_usd) FROM live_orders WHERE strategy_id='b7c50005-0000-4000-8195-000000000022' AND settled_at_utc>='2026-09-19T06:10:26.233155Z' AND settled_at_utc<'2026-09-20T06:10:26.233155Z';
SELECT 'paper_lifetime',count(*),sum(net_realized_pnl_usd) FROM strategy_market_paper_runs WHERE strategy_id='b7c50005-0000-4000-8195-000000000022' AND settled_at_utc IS NOT NULL
UNION ALL SELECT 'live_lifetime',count(*),sum(net_realized_pnl_usd) FROM live_orders WHERE strategy_id='b7c50005-0000-4000-8195-000000000022' AND settled_at_utc IS NOT NULL;
COMMIT;
```
Independent settlement verification joins paper_position_settlements by exact copied_trader_wallet+asset_id+condition_id and crypto_up_down_5m_websocket_resolved_markets by diagnostics.settlement_resolution.ledger_id. No exclusions from the22pair cohort. No venue account/redeem/on-chain balance audit or other-strategy analysis performed. No general claim about all historicalPaper correctness. No code/build/test changes required for read-only numerical diagnosis.

SHA256 pairs.sql: 4FC0EC40D7AA5364AFA8298D79F07232B9B806DD6494E01C9E28B2F15CCC2B2B

SHA256 pairs.json: D83DC82006CBD66BF94FFD8DC5EDB983A9767DFCFAB45E1F51D98D2CAFE777DC

SHA256 independent.json: A909D43A7332BC0B780E468E92843529479BD4E77C122BB6EBD0FE130189FF08

SHA256 settlement-logs.txt: FE7DBEFD8DD7AF6E03D7700030C5F87C434AA68FFA714163A24502B158D942BE

Finalization: staged requirement validation and staged diff check PASS (only2exempt context/history paths). WorkingTree gate is blocked by pre-existing unrelated RC-20260917-single-disabled-strategy-timed-delete.json SEMANTIC_CHANGE_AFTER_APPROVAL; unrelated changes are excluded and preserved. No product tests run because no product files changed.

## 2026-09-20T06:25:36.1639046+00:00
Request:
Эта проблема проявляется по всем нашим Paper стратегиям за всю историю? Приняв по ним решение, мы потом его не проверяем?

Result:
## Active Update 2026-09-20 Paper Settlement Recheck Scope
Goal: Answer whether the ETH22 settlement divergence affects all Paper strategies for all history and whether closed outcomes are rechecked.
Status: Completed bounded source inspection; full historical incidence remains unknown.
Done:
- Current common SettleDueRunsAsync dispatch covers StrategyIds.UpDown5mStrategyVariants, including disabled/nonconfigured variants and Child. Fallback TryResolveSettlementFromCanonicalLedgerAsync requires exact catalog identity, FiveMinutes and BTC/ETH/SOL; it admits BinanceTimedClose. This is a shared eligible path, not evidence every strategy/trade is wrong.
- Normal storage settlement query selects Entered only; settlement persists Settled. PaperSettlementProcessor reads only open positions, including upon market-resolution events. Live shadow synchronization handles fills. Dashboard reconciliation rebuilds aggregates from existing source rows. Inspection of service hosted workers, outcome/settlement write callsites, reconciliation and correction searches found no routine that revisits these already-settled provisional results against final venue outcomes. Prior five-case runtime evidence independently demonstrates their persistence after authoritative updates.
- Local git introduction of this fallback is35f72c0c dated2026-08-15; this does not establish production rollout time or all historical implementation versions. Full-history strategy/error counts and financial impact are not measured. No production query or code/config/runtime mutation in this follow-up.
Notes: Read-only source/history examination. Relevant paths: BtcUpDown5mPaperStrategyProcessor.cs:203,270,8393,8559; PostgresAppRepository.cs:1005,7735; PaperSettlementProcessor.cs:18,84; LiveTradingProcessor.cs:544; DashboardStrategyProjectionReconciliationWorker.cs:32 and PostgresDashboardProjectionRepository.Reconciliation.cs:69. Existing fallback test cases inspected, not executed; no product changes or build needed.
Next: None within this explanatory question.
Blockers: None for the mechanism answer; universal historical incidence is unverified.

Scope distinction: common eligibility for the flawed5mBTC/ETH/SOLfallback is verified in current source; all-strategy/all-history prevalence is unknown. No full-history database scan performed in this follow-up; no measured universal-incidence claim is made. Closed outcomes remain outside the normal settlement selector; refreshing or reconciling Dashboard does not validate their winners. FollowLeader/general Paper positions use separate Gamma/WebSocket paths, so universal exposure is not inferred. Prior five-case runtime evidence plus current dispatch/storage code substantiate the mechanism; no tests rerun for unchanged code. Only exempt context/history bookkeeping staged; existing unrelated changes preserved.
