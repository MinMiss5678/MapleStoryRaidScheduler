# Outbox 重試指數退避（NextRetryAt · 尊重暫時性失敗）

> 輕量 plan（動手前 spec）。定位**誠實**：這**不是**「不漏資料」的修補——站內「我的邀請／我的隊」清單才是權威真相（leader-led §11），DM 掉了不代表資料掉了。本案要修的是兩個**投遞品質**問題：(1) 目前失敗列**無 per-message 退避**，每輪（甚至同批撈滿時立即）被重撈，Discord 5xx／限流時反而**猛打 Discord**、加劇問題；(2) 5 次重試被固定 5s 輪詢擠在 ~25s 內用完，撐不過 Discord 幾分鐘的伺服器抖動。加一個 `NextRetryAt` 退避欄位，讓暫時性失敗以**指數退避 + jitter** 拉開重試、且不再霸佔批次名額。

## 目標

1. 失敗的 outbox 列改為**延後可見**：`NextRetryAt` 到期前不被 `OutboxDispatcher` 撈取。
2. 退避策略為**指數退避 + jitter**（如 5s → 10s → 20s → … 上限封頂），policy 寫在 C#（可單元測），時間基準用 DB `now()`（多 pod 免時鐘偏移）。
3. 沿用既有失敗分流語意**不變**：403/404（關 DM／退公會）永久失敗照樣吞掉標 processed；超 `MaxAttempts` 照樣 GiveUp（標 processed + 記 `LastError`）。退避**只影響暫時性失敗的重試間隔**。
4. 相容多 pod 派發（`FOR UPDATE SKIP LOCKED`，見 `plans/2026-09-04-multi-pod-outbox-dispatch.md`）——退避是 per-row、免協調。

## 背景 / 現況

`Infrastructure/BackgroundJobs/OutboxDispatcher.cs`：

- `MaxAttempts=5`、`PollInterval=5s`、`BatchSize=20`。
- 失敗列走 `MarkFailedSql`（`AttemptCount+1`、記 `LastError`），但 **`ProcessedAt` 仍 NULL** → 下一輪 `ClaimSql`（`WHERE "ProcessedAt" IS NULL ORDER BY "Id"`）**立即重撈**（批撈滿時甚至同一迴圈 `continue` 馬上再撈）。
- 結果：一筆一直失敗的列約 **~20–25s 內把 5 次用完**就 GiveUp；期間對 Discord 無退避地連打。

失敗分類（`Infrastructure/BackgroundJobs/TeamNotificationOutboxHandler.cs`，DSharpPlus 5.0 `DSharpPlus.Exceptions`）：

- **永久**：`UnauthorizedException`(403 關 DM)、`NotFoundException`(404 退公會) → 吞掉、標 processed、不重試。
- **暫時**：`ServerErrorException`(Discord 5xx)、網路／逾時 → rethrow → 重試（本案要退避的正是這條）。
- 429：DSharpPlus 5.0 REST 管線**內建**尊重 `Retry-After` 等待重送，通常**輪不到** outbox；本案不介入 429。

## 設計 / 工項

### 1. Migration `000027_outbox_next_retry_at`

```sql
-- up
ALTER TABLE "OutboxMessage"
    ADD COLUMN "NextRetryAt" timestamptz;   -- NULL = 立即可派發（新列 / 未曾失敗）

-- 撈取條件多了 NextRetryAt 殘留過濾；把 partial index 換成含 NextRetryAt，
-- 讓「待處理且已到期」的掃描仍走索引（退避中的未來列不必反覆掃過）。
DROP INDEX "ix_outbox_unprocessed";
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("NextRetryAt" NULLS FIRST, "Id")
    WHERE "ProcessedAt" IS NULL;
```

```sql
-- down
DROP INDEX "ix_outbox_unprocessed";
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("Id") WHERE "ProcessedAt" IS NULL;
ALTER TABLE "OutboxMessage" DROP COLUMN "NextRetryAt";
```

> 同步更新 `k8s/ha-demo/20-seed-job.yaml` 的 inline `OutboxMessage` 建表（那份是 demo 環境手寫 schema，會漂移）。

### 2. `OutboxDispatcher` SQL 改動

- **ClaimSql** 加到期過濾：
  ```sql
  WHERE "ProcessedAt" IS NULL
    AND ("NextRetryAt" IS NULL OR "NextRetryAt" <= now())
  ORDER BY "Id"
  FOR UPDATE SKIP LOCKED
  LIMIT @Limit
  ```
- **MarkFailedSql** 設下次可見時間，**時間基準用 DB `now()`**、delay 由 C# 算好傳入（秒）：
  ```sql
  UPDATE "OutboxMessage"
  SET "AttemptCount" = "AttemptCount" + 1,
      "LastError"    = @Error,
      "NextRetryAt"  = now() + (@DelaySeconds * interval '1 second')
  WHERE "Id" = @Id
  ```
- **GiveUpSql** 不動（已設 `ProcessedAt`，`NextRetryAt` 無所謂）。

### 3. 退避 policy（C#，可單元測）

- 新增純函式（`OutboxDispatcher` 內 `internal static` 或抽 `BackoffPolicy`）：
  - 依「即將成為的嘗試次數」= `row.AttemptCount + 1` 算 delay。
  - 指數：`base * 2^(attempt-1)`，`base=5s`，**封頂** `cap`（如 5 分鐘），避免無限拉長。
  - **jitter**：乘上 `[0.5, 1.5)` 隨機或加 `±20%`——多 pod／同批多筆同時失敗時錯開重試，避免 thundering herd 與同步猛打 Discord。
  - 回傳整數秒傳給 `@DelaySeconds`。
- `ProcessBatchAsync` 失敗分支：`MaxAttempts` 未到 → 算 delay → `MarkFailedSql`；到達 → `GiveUpSql`（維持現行）。

### 4. 參數微調（趁機）

- 退避後失敗列不再霸佔批次名額 → 可安全把 `MaxAttempts` 調大（如 **8**），配 base=5s、cap=5m，總重試窗約拉到**數十分鐘**，撐得過 Discord 短暫 5xx。
- `PollInterval` 維持 5s（它只決定「多久掃一次到期列」，不再等於重試間隔）。

## 驗收

- [ ] Migration up/down 對稱、本機套用不報錯；`down` 能還原索引與欄位。
- [ ] **單元測（退避 policy）**：`attempt=1..n` 單調遞增、封頂不超過 cap、jitter 落在界內；`MaxAttempts` 邊界走 GiveUp 而非 MarkFailed。
- [ ] **整合測（`OutboxIntegrationTests` 延伸，Testcontainers postgres:18）**：
  - 暫時性失敗 → 列 `ProcessedAt` 仍 NULL、`NextRetryAt` 落在未來、`AttemptCount+1`、`LastError` 有值。
  - `NextRetryAt` 未到期 → **同一輪 `ProcessBatchAsync` 不重撈**該列（撈取集合不含它）。
  - 手動把 `NextRetryAt` 撥到過去（或 base 設極小）→ 下一輪**被重撈**。
  - 既有案例續綠：無 handler → GiveUp 標 processed + 記 `LastError`（不受 `NextRetryAt` 影響）；SKIP LOCKED／原子投遞不變。
- [ ] 多 pod 併發整合測（`OutboxConcurrencyIntegrationTests`）續綠——退避 per-row，不破壞恰一次。
- [ ] `dotnet format --verify-no-changes` 綠、`dotnet test` 全綠。

## 非範圍 / 誠實取捨（YAGNI）

- **不做 DLQ / 重試 UI / 告警加值**：GiveUp 仍只標 processed + 記 `LastError` + Seq log（現況）。DLQ 是未來多公會接 MQ 那層的事（`plans/2026-07-24-transactional-outbox.md:39`、`plans/2026-09-11-async-confirm-mq.md`）。
- **不改 429 路徑**：交給 DSharpPlus 內建 `Retry-After`，不在 outbox 重複造輪子。
- **不追求「不漏 DM」**：站內清單是權威，退避提升的是**到達率與對 Discord 友善度**，非資料正確性。
- **退避不解永久失敗**：403/404 仍是即刻吞掉，不進退避。

## 風險 / 待確認

- **時鐘**：delay 在 C# 算、但**套在 DB `now()`** 上（`now() + interval`），避免多 pod app 時鐘偏移導致到期判斷不一致；claim 的 `now()` 也是 DB 時鐘，一致。
- **索引效益**：換成 `("NextRetryAt" NULLS FIRST, "Id")` 後，正常「立即可派發」列（`NextRetryAt` NULL）仍排在前、走索引；本專案量小，退化風險低，但 migration 後留意 `EXPLAIN` claim 查詢。
- **重複窗**：退避不改 at-least-once 本質；handler 非嚴格冪等（Discord DM 無 idempotency key）——重送頂多重發相同 DM，維持現行可接受取捨（`TeamNotificationOutboxHandler` 檔頭註解）。
- **GiveUp 與 `NextRetryAt`**：GiveUp 已設 `ProcessedAt`，即使殘留舊 `NextRetryAt` 也不會再被撈；無需清欄位。
