## 2026-06-28T21:13:37.2331901Z
Request:
Мне казалось, что я уже давал такую задачу, но на всякий случай повторю - убедись в том, что все текущие стратегии делают ставки в FAK режиме со ставкой 0.99 для гарантированной выборки стека. Как в Paper, так и в Live режиме.

Result:
Forced current Up/Down FAK strategy order paths to use worst price 0.99 for Paper and Live while preserving actual fill VWAP separately; updated focused FAK Paper/Live tests. Focused verification passed. Full solution build was blocked by running Dashboard/Visual Studio locked DLLs.
