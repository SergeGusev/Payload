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

## 2026-08-08T22:37:13.3403682Z
Request:
Моя цель - как раз отказаться от Taker покупок и перейти на режим Maker

Result:
Confirmed that the current PaperOnly FAK path is incompatible with the stated Maker-only goal. A genuine Maker path requires GTC/GTD with postOnly=true, a non-crossing resting price, pending-order lifecycle, later queue/trade-through fill evidence, and explicit expiry/cancellation. Existing components are reusable, but no registered end-to-end Maker strategy exists and the dormant Maker branch cannot be enabled unchanged. No product or production state changed; implementation awaits exact strategy scope and execution-policy choices.
