## 2026-07-04T10:44:18.0882100+00:00
Request:
Добавь в Dashboard галочку "Hide progress" во все табы. При нажатой галочке будут прятаться все стратегии, у которых в названии есть слово Progress

Result:
Added `Hide progress` checkboxes to the Dashboard Strategies `All`, `24 hours`, `6 hours`, and `1 hour` tabs. Each checkbox hides strategy rows whose visible name contains `Progress`, case-insensitively; the `All` tab setting also applies to its nested recent-performance grid. Updated README and configuration reference documentation. Verification passed: Dashboard build with a temporary output directory succeeded, and `git diff --check` reported no whitespace errors, only existing line-ending warnings. No production DB writes, order submissions, cancels, service restarts, or Live setting changes were performed.
