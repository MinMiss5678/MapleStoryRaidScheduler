# Outbox 重試：指數退避 + 有效期限 + 送出前失效檢查

> 輕量 plan（動手前 spec）。定位**誠實**：這**不是**「不漏資料」的修補——站內「我的邀請／我的隊」清單才是權威真相（leader-led §11），DM 掉了不代表資料掉了。要修的是**投遞品質**：
> 1. 目前失敗列**無退避**，下一輪立即重撈，~20–25s 內 5 次用完就放棄，撐不過 Discord 幾分鐘的抖動，期間還猛打 Discord。
> 2. 放棄條件是「次數」，但真正該問的是「**這則通知現在送還有沒有意義**」。
> 3. 延遲送達的按鈕通知可能已失效（邀請已撤、隊已滿），送出去變死按鈕。

## 定案（2026-09-30 grill）

| 決策 | 定案 |
|---|---|
| 放棄條件 | **有效期限為主**（`DeliverBefore`）、**次數為輔**（只管無法分類的錯誤） |
| 期限 | 有按鈕的通知（邀請/申請/轉讓）＝隊伍時間（排程 `SlotDateTime`、即時 `ExpiresAt`）；其餘＝建立後 24h；**一律不超過建立後 24h** |
| 期限存哪 | 入列時寫進 outbox 新欄位 `DeliverBefore`；dispatcher 通用判斷，不需懂各通知 |
| 錯誤分類 | **暫時性**（Discord 5xx、429 用完、網路、逾時）→ 不計次數、退避到期限；**無法分類**（程式錯誤）→ 維持 5 次上限 |
| 退避 | 指數：5s × 2^(n-1)，封頂 10 分鐘，jitter ±20%；時間基準 DB `now()` |
| 送出前檢查 | 僅邀請/申請/轉讓：狀態已不是待處理 → 不送、標完成、記原因 |
| 警示 | 過期放棄＝Error（觸發既有 Seq 警示信）；失效不送＝Information（正常業務，不警示） |
| DLQ | 不做（過期放棄＝已無意義，重送反而錯） |
| 429 | DSharpPlus 內部依 Retry-After 重試（`MaximumRatelimitRetries`），用完才丟 `RateLimitException` → 歸暫時性 |

## 背景 / 現況

- `OutboxDispatcher`：`MaxAttempts=5`、`PollInterval=5s`、`BatchSize=20`；失敗走 `MarkFailedSql`（`AttemptCount+1`、`LastError`）但 `ProcessedAt` 仍 NULL → 下一輪 `ClaimSql` 立即重撈。達上限 `LogError` + `GiveUpSql`（`LogError` 已由 Seq Error alert 寄信，見 `plans/2026-07-31-error-alerting.md`）。
- `TeamNotificationOutboxHandler`：403（關 DM）/404（退公會）永久失敗 → 吞掉標完成；其餘例外 rethrow 重試。handler 有 `IDbConnectionFactory`，可直接查 DB。
- 入列：`TeamLeaderService.NotifyAsync`（所有 leader-led 通知）、`EnqueueInviteRevokedCleanupAsync`、`AvailabilityFreshnessNudgeJob`，都走 `IOutbox.EnqueueAsync(type, payload)`。

## 設計 / 工項

### 1. Migration `000027_outbox_retry_backoff`

```sql
-- up
ALTER TABLE "OutboxMessage"
    ADD COLUMN "NextRetryAt"   timestamptz,   -- NULL = 立即可派發
    ADD COLUMN "DeliverBefore" timestamptz;   -- NULL = 預設（建立後 24h）
DROP INDEX "ix_outbox_unprocessed";
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("NextRetryAt" NULLS FIRST, "Id")
    WHERE "ProcessedAt" IS NULL;
-- down：還原索引、DROP 兩欄
```
同步更新 `k8s/ha-demo/20-seed-job.yaml` 的 inline 建表。

### 2. 入列端
- `IOutbox.EnqueueAsync(string type, object payload, DateTimeOffset? deliverBefore = null)`（新增選填參數，舊呼叫端不動）。
- `TeamLeaderService.NotifyAsync` 多收 `deliverBefore`：按鈕通知傳隊伍時間（`Kind==Instant ? ExpiresAt : SlotDateTime`），純文字不傳。

### 3. Dispatcher
- **有效期限**（DB 時鐘）：`effective = LEAST(COALESCE("DeliverBefore", "OccurredAt" + 24h), "OccurredAt" + 24h)`；ClaimSql 一併回傳 `Expired = effective <= now()`。
- **ClaimSql**：`WHERE "ProcessedAt" IS NULL AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= now())`。
- **過期**：不送，`GiveUpSql`（`LastError` = `expired`）+ `LogError`。
- **失敗分流**：
  - `OutboxDeliverySkippedException`（handler 判定失效）→ 標完成、`LastError` = `skipped: <原因>`、`LogInformation`。
  - 暫時性 → `MarkFailedSql` 設 `NextRetryAt = now() + @DelaySeconds`，不計次數上限。
  - 其他 → `AttemptCount+1 >= 5` 則 GiveUp + `LogError`，否則同上退避。
- **退避 policy**（純函式、可單元測）：`raw = min(5 × 2^(n-1), 600)` 秒（n＝即將成為的嘗試次數）；`delay = round(raw × U[0.8, 1.2))`，最小 1。
- **錯誤分類**（純函式）：`ServerErrorException`、`RateLimitException`、`HttpRequestException`、`TimeoutException`、`IOException`、非停機造成的 `TaskCanceledException` ＝暫時性。

### 4. Handler 送出前失效檢查
- `InviteResponse`：成員 `Status` 仍為 `Invited`；`ApplicationReview`：仍為 `Applied`；`TransferResponse`：隊伍 `PendingLeaderDiscordId` 仍為收件人。
- 查無列或狀態不符 → 丟 `OutboxDeliverySkippedException("...")`，不送 DM。
- 純文字、撤邀清理（`InviteRevokedCleanup`）、新鮮度提醒不檢查。
- 修正 handler 註解（429 先由 DSharpPlus 內部重試）。

## 非範圍（YAGNI）
- 不做 DLQ / 重送 UI。
- 不自行處理 429 的 Retry-After（交給 DSharpPlus）。
- 不改 at-least-once 本質：DM 無 idempotency key，重送頂多重複一則。

## 風險
- **時鐘**：期限與退避都以 DB `now()` 計算，多 pod 一致。
- **索引**：改為 `("NextRetryAt" NULLS FIRST, "Id")`，量小，migration 後看一次 claim 的 `EXPLAIN`。
- **暫時性錯誤不設次數上限**：若分類錯誤地把程式錯誤歸成暫時性，會一直重試到期限（最多 24h，約 150 次）才放棄；期限是最後防線。
