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

## 2026-08-29T09:05:38.8410000Z
Request:
`APPROVE RC-20260829-maker-gtd-midpoint-even-timestamps sha256:a6b68ba6fcb21b9da8b128921c52c54600d6fe48a9ea1a8574859b5a229a90ca`

Result:
Implemented only the approved Maker-GTD immutable initial-order timestamp correction. The shared `CreatedAtUtc`/`ExpiresAtUtc` comparison now reproduces PostgreSQL midpoint-to-even microsecond storage, including `.7195025 -> .719502` and `.7195035 -> .719504`; all non-timestamp predicates and execution semantics remain unchanged. Final focused verification passed 71/71 with zero skips on allowlisted disposable local PostgreSQL, the database was dropped and confirmed absent, Release solution build passed with zero errors, and independent reviewer `agent:/root/review_midpoint_even` returned PASS with no findings. Production was not changed. Classification remains `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`.

## 2026-08-29T12:24:36.0844822Z
Request:
Сделай для каждой из валют (BTC, ETH, SOL) набор стратегий Follow Market M N,&#x20;

где M меняется от 30 до 570 с шагом 30 и означает время после начала рынка в секундах,&#x20;
а N меняется от 50 до 95 с шагом 2 и означает ставку, по которой можно сразу гарантированно купить позицию в FAK режиме. Стратегия покупает если ставка равна или больше N

Покупаем в режиме FAK с минимально доступной суммой.&#x20;

То есть идея в следующем - следуем за рынком и покупаем каждые 30 секунд, если выполняются условия по N

Re: Это стратегии для **15-минутных рынков**? `M=570` невозможно для 5-минутного рынка. &#x20;

Для 5-ти минутных. Это я ошибся, имел в виду 270

Re: Какой набор `N` нужен: `50, 52, …, 94` или дополнительно `95`? &#x20;

От 50 до 95 включительно с шагом 5

Re: В момент `start + M` покупаем исход `Up/Down` с более высокой текущей исполнимой ценой ask, если она `≥ N/100`? &#x20;

Я не понял этого вопроса. Покупаем так, чтобы FAK ставка сработала по этой ставке.

Re: Каждая стратегия `(валюта, M, N)` делает только одну FAK-попытку на рынок; при недостаточной глубине повторов нет? &#x20;

Только одна попытка. Глубину стакана не учитываем, считаем, что она достаточна

Ты мне задаёшь вопросы, требующщие слишком глубокого понимания, которого у меня нет. Я, грубо, вижу это примерно так:  Up продают по 65, мы для стратегий с N <= 65 делаем FAK заявку с 0.99, чтобы мы все взяли точно.

`APPROVE RC-20260829-follow-market-fak-strategies sha256:7f1df7c333de54a9fcc378b1dcaa48d0d7bc9612be6806d6919415929c84c523`

Result:
Implemented the approved 270-strategy BTC/ETH/SOL Follow Market FAK family. The processor uses fresh executable Up/Down asks at `marketStartUtc + M`, selects the unique higher ask, applies inclusive N, submits one minimum-size Paper BUY FAK intent capped at `0.99`, does not gate on cumulative depth and does not retry. Exact venue minimum sizing uses multiplier `1.0`; missing minimum size fails closed. Paper retains real full/partial/no-fill semantics and audit evidence; every variant is Live-disabled through `PaperOnly=true`. Focused tests passed 12/12, the solution built with 0 errors, exact-scope staged and isolated WorkingTree requirement gates passed, and independent semantic review returned PASS with no findings. No Production or real-order action occurred.
