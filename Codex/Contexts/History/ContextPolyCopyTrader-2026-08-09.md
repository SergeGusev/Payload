## 2026-08-08T22:22:54.3624015Z
Request:
Ты понимаешь, как правильно делать PaperOnly ставки, чтобы они не были отвергнуты?

Result:
Verified the current PaperOnly FAK acceptance contract from implementation, parity documentation, tests, and persisted project evidence. PaperOnly blocks external submission but retains all Live-valid intent semantics; positive partial fills are accepted with remainder cancellation, while invalid intent, missing/stale market evidence, price/spread/minimum-size failures, and zero executable depth correctly remain no-bet skips. No product or production state changed.
