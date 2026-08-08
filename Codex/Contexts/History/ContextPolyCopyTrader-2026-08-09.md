## 2026-08-08T22:22:54.3624015Z
Request:
Ты понимаешь, как правильно делать PaperOnly ставки, чтобы они не были отвергнуты?

Result:
Verified the current PaperOnly FAK acceptance contract from implementation, parity documentation, tests, and persisted project evidence. PaperOnly blocks external submission but retains all Live-valid intent semantics; positive partial fills are accepted with remainder cancellation, while invalid intent, missing/stale market evidence, price/spread/minimum-size failures, and zero executable depth correctly remain no-bet skips. No product or production state changed.

## 2026-08-08T22:27:38.7005785Z
Request:
Я не понял, как связано PaperOnly и FAK. Я думал, что FAK - это как раз наоборот, покупать по текущей цене

Result:
Clarified that PaperOnly and FAK are independent dimensions. PaperOnly suppresses external submission; BUY FAK immediately takes currently available asks up to a hard worst-price cap and cancels the remainder. It may sweep several current ask levels rather than fill at one last-trade or displayed price. No product or production state changed.
