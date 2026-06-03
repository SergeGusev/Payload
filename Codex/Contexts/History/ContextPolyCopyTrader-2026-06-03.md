# ContextPolyCopyTrader History 2026-06-03

## 2026-06-03T00:08:12.6752745+03:00
Request:
User deployed and asked to verify production after the separate Live condition skip boundary change.

Result:
Verified production read-only via `out\dbprobe` with a temporary host override to `192.168.0.101` and without printing the connection string. `PolyCopyTrader.Service` is running in `Live` mode on service-code commit `ad88ae97f6411b30cd1130ca21f2d29b629aa5e1`, started `2026-06-02T20:58:11Z`, with fresh heartbeat and empty `last_error`. The `strategies.live_enabled_at_utc` schema column exists; production has `3` Live strategies, all `3` have non-null Live boundaries, and `0` non-Live strategies have stale boundaries. BTC Middle 47, ETH Skip 7, and SOL Skip 42 are all effective-live with `auto_live_paused=false`. The Live condition skip boundary is active: BTC old all-paper condition skip `2530` vs Live condition skip `97`; ETH `1040` vs `47`; SOL `2462` vs `2`. Recent API errors in the last 15 minutes are `0`. No production writes or source code changes were performed in the verification step.

## 2026-06-03T06:45:05.4936965Z
Request:
Что лучше делать с точки зрения ликвидности ордера, то есть чтобы он легче находил встречное предложение, еслия хочу увеличит ставку вдвое. Делать одни ставку вдвое больше или делать пакетную передачу двух ордеров на одинаковую сумму?

Result:
Answered using current official Polymarket documentation. For same side, same token, same limit price, and ordinary passive GTC/GTD placement, two equal orders do not create extra available liquidity or make matching easier versus one doubled order; they still face the same order book and price/time priority. Recommended one doubled order for simpler accounting and cancellation unless there is a specific need for independent slices. For immediate execution, recommended using order book depth and FAK/partial-fill behavior when partial execution is acceptable; two smaller FOK orders are only useful when each slice must be all-or-nothing and one slice may fit available depth while a single doubled FOK would fail. No source code or production state changed.

## 2026-06-03T06:49:40.9069264Z
Request:
Если я ставлю ордер на 6 евро, а в стакане есть только 3, он на эти 3 евро закроется?

Result:
Answered that partial fill behavior depends on order type. FOK will not fill at all unless the whole 6 can fill immediately. FAK will fill the available 3 immediately and cancel the rest. A marketable GTC/GTD limit order can take the available 3 and leave the unfilled remainder live until expiration/cancel; if the order is not marketable, it simply rests until someone crosses it. Inspected current live-shadow code and noted BTC/ETH/SOL 5m live requests are `GTD` limit BUY with `PostOnly=false`, so they are not all-or-nothing FOK orders. No source code or production state changed.

## 2026-06-03T10:52:47.6689622+03:00
Request:
Для каждой стратегии заводим 2 поля - PaperLostCoeff, LiveLostCoeff, по умолчанию везде 1, одно для Paper режима, другое - для Live

Нужно иметь возможность менять через Dashboard эти значения. Если значение PaperLostCoeff больше 1, то работает счётчик перекосов LostCounter в сторону поражений. Работает он так:

1. Значение хранится в памяти и обнуляется каждый раз при рестарте сервиса.
2. Поражение увеличивает LostCounter на 1, выигрыш - уменьшает. При достижении счётчика 0 уменьшение останавливается.
3. Если в момент ставки по стратегии счётчик больше 0, то определяем дополнительную сумму ставки:
3.1  Множитель LostCoeff определяется как LostCounter, но не превышает 5. То есть для LostCounter = 4 LostCoeff будет 4, а для LostCounter = 10 LostCoeff будет 5.
3.2. Берём текущую вычисленную ставку Stake и вычисляем добавление AddStake = Stake * LostCoeff.
3.3 Итоговая ставка будет State + AddStake

Result:
Added persisted per-strategy `paper_lost_coeff` and `live_lost_coeff` columns with default `1.00` and minimum-`1` constraints. Exposed them in the Dashboard `Strategies` grid as editable `Paper Lost` and `Live Lost`, wired validation/save/CSV export, and preserved values in the service stake admin command. Implemented the requested in-memory Paper `LostCounter` in the Up/Down paper strategy processor: losses increment, wins decrement down to zero, restart clears the dictionary, and when `PaperLostCoeff > 1` a positive counter adds `Stake * min(LostCounter, 5)` to the computed Paper stake. Added decision JSON diagnostics for the adjustment and regression tests for schema/repository storage plus the capped stake behavior. `LiveLostCoeff` is stored/editable but intentionally not applied to live sizing yet because the request specified the Paper counter behavior and changing real live notional needs an explicit live-risk policy. Verification passed: full test project 569/569, Dashboard temp-output build passed with existing Storage nullable warnings, Service build passed with existing Storage nullable warnings, and `git diff --check` passed with LF/CRLF warnings only.

## 2026-06-03T09:09:01.9487853Z
Request:
Выложил, проверяй

Result:
Verified production read-only through `out\dbprobe` with a temporary host override to `192.168.0.101` and without printing the connection string. `PolyCopyTrader.Service` is running in `Live` mode on deployed commit `3ed77b1a8ab2187d7dd29fabc1070f1abee2f5ef`, started `2026-06-03T08:50:10Z`, with fresh heartbeat and empty `last_error`. The production `strategies` table has the new `paper_lost_coeff` and `live_lost_coeff` columns, both `numeric NOT NULL DEFAULT 1.00`, plus the minimum-`1` constraints. All `2886` strategy rows have valid lost coefficients: `2883` remain at `PaperLostCoeff=1`, while the three Live strategies BTC Middle 47, ETH Skip 7, and SOL Skip 42 have `PaperLostCoeff=2` and `LiveLostCoeff=1`. All three Live strategies are effective-live and not auto-live-paused; no Live orders have been created since the restart. Fresh post-start Paper orders include the new `paper_lost_*` decision JSON diagnostics; sampled diagnostics show default strategies at `configured_coeff=1`, `counter=0`. The three `PaperLostCoeff=2` strategies have not placed a new Paper entry since restart, but ETH Skip 7 did settle one Paper loss after restart, so its in-memory lost counter should be positive before the next qualifying entry. Recent API errors were not fatal: one startup Binance reference warmup error and one current WebSocket close error from `PolymarketMarketWebSocket:shard-004`. No production writes, service changes, source behavior changes, or tests were performed during this verification.

## 2026-06-03T10:25:22.2575413+00:00
Request:
Мне кажется, или ставки не ставятся?

Result:
Diagnosed production read-only and confirmed the service was running in `Live` mode with a fresh heartbeat, but post-restart ETH Skip 7 Paper/Live-shadow attempts were being `PreflightRejected`: `PaperLostCoeff=2` plus a positive in-memory Paper lost counter doubled the shadow notional and pushed required notional over `LiveTrading.MaxOrderNotionalUsd`. Updated `BtcUpDown5mPaperStrategyProcessor` so Paper/Live-shadow entries do not apply the Paper lost-counter stake boost; this keeps `PaperLostCoeff` Paper-only and leaves `LiveLostCoeff` stored/editable but unused until an explicit live-risk policy is requested. Added regression coverage proving an ETH live-shadow entry with `PaperLostCoeff=2` and a positive Paper lost counter still submits at base notional. Verification passed: targeted processor tests 3/3, full test project 570/570, and `git diff --check` with LF/CRLF warnings only. Production needs deploy/restart before the fix affects live runtime.

## 2026-06-03T10:35:43.5125030+00:00
Request:
А когда мы начнём работать с LiveLostCoeff, Live preflight будет что-то резать по лимиту? А что это за лимит, по которому мы режем?

Result:
Inspected current code/config and clarified that BTC/ETH/SOL Paper/Live-shadow preflight currently rejects the whole Live order when computed notional exceeds the effective cap; it does not silently trim the order down to the cap. The effective per-order cap is `min(LiveTrading.MaxOrderNotionalUsd, LiveTrading.MaxTradeBankrollPct / 100 * PaperTrading.InitialBankrollUsd)`, and service config currently sets `LiveTrading.MaxOrderNotionalUsd=5.0`, which matched production `Cap=5` rejects. Also noted the separate strategy `LiveAvailableBalance`, max open live order count, per-market exposure, and total deployed exposure checks. Recommended keeping hard preflight caps when LiveLostCoeff is implemented unless a future explicit policy chooses capped/saturated sizing.

## 2026-06-03T11:03:31.3685242+00:00
Request:
А откуда взялся LiveTrading:MaxOrderNotionalUsd = 5.0 ? Я не заказывал. И я не хочу, чтобы ограничивали себя ставками

Result:
Traced `LiveTrading:MaxOrderNotionalUsd`: it was introduced with the first gated maker-only live trading config at `1.0` and later raised to `5.0` in commit `7b715ae9`, so it was an internal smoke-test/safety cap rather than a Polymarket or user-requested limit. Updated Service and Dashboard configs to remove the old tiny cap as a practical limiter by setting `LiveTrading:MaxOrderNotionalUsd=100.0`, `MaxTradeBankrollPct=1.00`, and `MaxMarketBankrollPct=1.00`, while preserving live risk controls through explicit Live gates, kill switch/live pause, strategy `Live $` and `Live bal`, max open live orders, market/total exposure, geoblock, clock, API-error, and opposite-outcome checks. Updated Dashboard readiness text and docs to describe `MaxOrderNotionalUsd` as a hard emergency ceiling, not normal stake sizing. Verification passed: focused `ConfigurationTests` 27/27, Service build 0 warnings/errors, Dashboard temp-output build passed with existing Storage nullable warnings after the normal Dashboard build was blocked by running Dashboard/Visual Studio locks, full test project 570/570, and `git diff --check` with LF/CRLF warnings only. Production needs deploy/restart for the new config to take effect.

## 2026-06-03T11:16:14.0889244+00:00
Request:
Выложил, проверяй

Result:
Verified production read-only via `out\dbprobe` with a temporary host override to `192.168.0.101` and without printing the connection string. `PolyCopyTrader.Service` is running in `Live` mode on commit `527a4624695ab10bbc028ec61347e40e1fbb97b2`, started `2026-06-03T11:11:10Z`, with fresh heartbeat and `last_error=null`. BTC Middle 47, ETH Skip 7, and SOL Skip 42 remain enabled, unpaused, effective-live, and not auto-live-paused; all three have `PaperLostCoeff=2` and `LiveLostCoeff=1`. After the restart there were `0` preflight rejects and `0` `Cap=5` rejects. One post-restart ETH Skip 7 Paper/Live-shadow order was submitted and matched at `2026-06-03T11:15:06Z` for `$4.00` notional (`Down`, price `0.64`, size `6.25`), with live event `BtcUpDown5mPaperLiveShadowPlaceOrder OK`. Startup geoblock check was OK; the only post-start API errors in the snapshot were transient SOL Binance reference warmup messages immediately after startup. No production writes, source changes, tests, live submissions, or cancels were performed by Codex.
