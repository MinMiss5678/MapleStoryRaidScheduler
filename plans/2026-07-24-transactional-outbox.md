# Transactional Outbox 計畫

> **狀態（2026-07-25）：已實作**。`OutboxMessage` 表 + `IOutbox`/`Outbox`（寫，共用 UoW 交易）+ `OutboxDispatcher`（`FOR UPDATE SKIP LOCKED`，跑 bot）+ `ConfigChangedOutboxHandler`；`SystemConfigService` 由 `AfterCommit` 改 outbox。單元 + 整合測（原子 / 投遞標記 / SKIP LOCKED / 無 handler 放棄）齊。
>
> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> 定位誠實：**這是面試材料 / readiness，不是救火**。現況 replicas=1、示範用的副作用是「可補的 Discord 通知」——但它同時修掉一個下面驗證出來的**跨行程真 gap**。

## 目標

把 post-commit 副作用從「in-process `DbContext.AfterCommit`」升級成 **transactional outbox**：把「要做什麼」的意圖**寫進同一筆交易**，另一個 dispatcher 讀**已提交**的列去投遞 → **at-least-once 可靠 + crash-safe + 跨行程**。以「設定變更 → 喚醒 `RegistrationDeadlineJob` 重算截止」為示範。

這是我們聊快取一致性時說的「可靠版 = 把意圖持久化進交易裡」那一層（outbox 之於副作用，等同 CDC/lease 之於快取）。見 [[../plans/2026-07-24-redis-integration]]。

## 現況與兩個 gap（已對 code 驗證）

`SystemConfigService.UpdateAsync`（Infrastructure）寫 config 後呼叫 `_dbContext.AfterCommit(_notifier.Notify)`。

- **Gap 1（crash-safety，通用）**：`AfterCommit` 的動作存記憶體 list，`CommitAsync` 後才 `RunAfterCommitActions`。若 process 在 **commit 後、跑動作前**崩 → 副作用**永久遺失**。
- **Gap 2（跨行程，★ 這專案的真 gap）**：
  - `SystemConfigController.UpdateAsync` 在 **API** 行程執行 → config 在 API 改。
  - 訂閱 `ConfigChangeNotifier.OnChanged` 的 `RegistrationDeadlineJob` **只註冊在 bot**（API 只掛 `WeeklyPeriodJob`）。
  - API 的 `ConfigChangeNotifier` 是該行程自己的 singleton、**沒有訂閱者** → `Notify()` 在 API 是 **no-op**。**in-process C# event 跨不了行程邊界**。
  - 結果：**管理員在 API 改截止設定，bot 的 job 不會被即時喚醒**，只能等 job 下次自然醒（`Task.Delay` 可長達到下個週重製 ~7 天）才撿到 → 期間截止通知可能**遲發**（deadline 改早）或**早發**（deadline 改晚）。低嚴重度（可補的訊息、會自癒），但是真的錯。

**outbox 兩個一起解**：寫入端（API）在同一交易插 outbox 列；派發端（bot）輪詢**已提交**的列 → 靠**共享 DB** 跨行程可靠投遞，且 crash 重啟接著送。

## 範圍（右尺寸）

### 做
- `outbox_message` 表（golang-migrate migration）。
- **寫入**：`IOutbox.Enqueue(type, payload)`（Application 介面）+ Dapper 實作，用**當前 UoW 的 `DbContext.Connection + Transaction`** 插列 → 與業務資料**原子提交/回滾**（請求 rollback → outbox 列也不在，無鬼影事件）。
- **派發**：`OutboxDispatcher : BackgroundService`（**跑在 bot**，因 handler 要喚醒 bot 的 job）——`FOR UPDATE SKIP LOCKED` 撈批 → 依 `type` 派給 handler → 標 `processed_at`。
- **handler**：`ConfigChanged` → `notifier.Notify()`（冪等：只是喚醒 job 重讀 config）。
- `SystemConfigService.UpdateAsync` 改成 `_outbox.Enqueue("ConfigChanged", …)` 取代 `AfterCommit(_notifier.Notify)`。

### 不做（YAGNI）
- 通用 message bus / MediatR / 事件溯源。
- 把**所有** `AfterCommit` 都搬 outbox——只示範 config 這條；其餘 in-process best-effort 留著。
- exactly-once、精確全域排序、Saga、dead-letter 的 UI（只做 log + 上限重試）。
- Debezium/CDC（讀 WAL）——outbox 已足夠，CDC 是更重的另一條路。

## 關鍵決策（動手前拍板）

- **at-least-once + 冪等 handler**：dispatcher 在「投遞成功、標 processed 之前」崩 → 重啟會**重送**。handler 必須容忍重複。`ConfigChanged → Notify` 天生冪等（重讀 config 無害）。→ **選 at-least-once**，不追 exactly-once（分散式下本就做不到，靠冪等兜）。
- **多 pod 派發用 `FOR UPDATE SKIP LOCKED`**：多個 dispatcher 併跑時各撈**不相交**的批、互不重送、無需選 leader、無狀態。（呼應整個 Redis 多 pod 主題。）
- **輪詢間隔**：預設數秒（延遲換負載）。要更低延遲 → **hybrid（選配）**：保留 in-process notify 當「快路徑」立刻戳 dispatcher，outbox 仍是可靠底線。
- **保留/清理**：processed 列定期刪（`processed_at < now() - interval '7 days'`），表不無限長。
- **重試/毒訊息**：`attempt_count` + `last_error`；超上限 → 記警示 log（不做 DLQ UI）。
- **寫入端與派發端在不同行程**（API 寫、bot 派）——**這正是 outbox 存在的理由**：靠共享 DB 的已提交列傳遞，而非跨不了行程的記憶體事件。

## 表結構（草案）

```sql
CREATE TABLE outbox_message (
  id            BIGSERIAL PRIMARY KEY,
  type          TEXT        NOT NULL,
  payload       JSONB       NOT NULL,
  occurred_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
  processed_at  TIMESTAMPTZ,
  attempt_count INT         NOT NULL DEFAULT 0,
  last_error    TEXT
);
-- 只索引「未處理」列 → 撈取快，即使歷史表變大也不拖
CREATE INDEX ix_outbox_unprocessed ON outbox_message (id) WHERE processed_at IS NULL;
```

## 派發查詢（草案）

```sql
-- 撈一批，跳過別的 worker 已鎖住的列（多 pod 分工核心）
SELECT id, type, payload
FROM outbox_message
WHERE processed_at IS NULL
ORDER BY id
FOR UPDATE SKIP LOCKED
LIMIT 20;

-- 每筆投遞成功後（同一交易內）
UPDATE outbox_message SET processed_at = now() WHERE id = $1;
```

## 驗收

- [ ] **原子**：config update 的請求 rollback → outbox 列**也不在**（同一交易）。
- [ ] dispatcher 投遞 + 標 `processed_at`；Testcontainers Postgres 整合測。
- [ ] **crash 重送（at-least-once）**：投遞後、標 processed 前中止 → 列還在 → 重啟重送。
- [ ] **多 worker 不相交**：兩個 dispatcher 併跑 `FOR UPDATE SKIP LOCKED` → 同一列不被兩個處理。
- [ ] **冪等**：重複投遞 `ConfigChanged` → job 只是多重讀一次，無副作用。
- [ ] **跨行程（修 Gap 2）**：API 改 config → bot 的 `RegistrationDeadlineJob` 被喚醒重算。
- [ ] 保留：processed 列被清理工作刪除。

## 工時估

- 表 + migration + `IOutbox`/Dapper 寫入（共用交易）≈ 半天。
- `OutboxDispatcher` + `SKIP LOCKED` 撈批 + `type→handler` 對映 + 註冊到 bot ≈ 半天~一天。
- 整合測（原子 / crash 重送 / SKIP LOCKED 不相交 / 跨行程）≈ 半天。

## 面試框（誠實）

> 「我把設定變更通知從 in-process `AfterCommit` 升級成 **transactional outbox**。動機兩個，一個通用、一個是我專案的真 gap：(1) `AfterCommit` 存記憶體，commit 後崩就掉；(2) 更關鍵——設定在 **API** 改、要喚醒的 job 在 **bot**，in-process 事件跨不了行程，所以現況 API 改設定**根本不會即時通知 bot**，只能等 job 下次自然醒。outbox 把『要送什麼』寫進**同一筆交易**，dispatcher 讀**已提交**的列去送 → **crash-safe + 跨行程可靠**。多 pod 我用 Postgres 的 **`FOR UPDATE SKIP LOCKED`** 讓多個 dispatcher 分工不重送；投遞是 **at-least-once**，exactly-once 做不到就靠 **handler 冪等**（通知只是喚醒重讀、重送無害）兜。取捨：現況 replicas=1、副作用是可補的 Discord 訊息，所以這是 readiness/示範——但它順帶修掉那個跨行程的真 gap。」

→ 展示：知道 outbox 解什麼（**原子 + 跨行程 + crash-safe**）、at-least-once/冪等的取捨、Postgres `SKIP LOCKED` 的多 worker 模式、以及**右尺寸的誠實判斷**（知道現在其實不需要、為何仍值得做）。
