## 2026-07-17T11:21:44.0543071Z
Request:
Нужно проверить версию о влиянии времени на поведение стратегии. Для каждой стратегии нужно эмулировать стратегию, которая делает следующее:

1. Перед каждой сделанной Paper ставкой делает выборку Paper ставок за предшесвующее текущей ставке время с группировкой по часу (получится 24 результата)
2. Выбираем 12 часов с самым высоким Pnl и 12 часов с самым высоким ROI (то есть две версии будем проверять)
3. Если текущий час попадает в эти удачные 12 часов, то эмулируем ставку (по факту повторяем уже сделанную Paper ставку) , если не попадает - не делаем.

В результате по каждой стратегии нужно понять

1. Удалось ли увеличить Pnl или ROI c выборкой лучших часов по Pnl
2. Удалось ли увеличить Pnl или ROI c выборкой лучших часов по ROI

Предоставить отсчёт по всем стратегиям с рекомендацией - стоит ли затеваться с этим или нет.

Все рассчёты и эмуляцию нужно вести в памяти, в базе ничего не меняем. Работаем с серверной базой, а не с локальной.

Result:
Completed a causal in-memory walk-forward replay on one exact read-only production snapshot: `1,026,567` settled Paper rows and all `1,861` current strategies. The global top-12-by-PnL filter worsened comparable PnL by `$26,652.55513914` and ROI by `0.8862535778` percentage points; the top-12-by-ROI filter worsened PnL by `$21,633.72276348` and ROI by `0.7508488705` percentage points. The negative conclusion survived min-sample, tie, maintenance-exclusion, split-half, and bootstrap checks. Recommended no global or Live implementation; only a predeclared future Paper holdout for individual candidates, preferably the 66 strategies shared by both strict candidate sets. Delivered and fully verified the five-sheet Excel report in the ignored output directory; no production data or product code was changed.
