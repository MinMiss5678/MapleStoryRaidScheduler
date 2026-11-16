# Redis 不可用時的 fail-open：補測試＋縮短等待

> 建立：2026/11/16｜狀態：**✅ case 表已核可（2026/11/16，含 R 組）**
> 範圍：`Presentation.WebApi/Program.cs`（Redis 連線設定）、`Test.Integration/`（新增 fail-open 測試）
> 起因：階段 0 的 D1 整合測試實際看到——Redis 連不上時，**每個寫入請求卡約 5 秒**才放行

---

## 一、現況

三個元件都設計成 Redis 不可用時 **fail-open**（放行＋記 log）：

| 元件 | 不可用時 | 位置 |
|---|---|---|
| 冪等 store | 當成第一次，放行 | `Infrastructure/Services/RedisIdempotencyStore.cs:30` |
| session 快取 | Get 當作沒命中（退回查 DB）；Set／Remove 忽略 | `Infrastructure/Services/RedisSessionCache.cs:35,50,63` |
| 限流 | 放行 | `Presentation.WebApi/RateLimiting/RedisFixedWindowRateLimiter.cs:58` |

**問題一：沒有任何測試驗證這三個 fail-open。** 第二層（Testcontainers 真 Redis）只測「Redis 正常時對不對」。

**問題二：fail-open 不是立刻放行，而是等逾時才放行。**
連線字串只有 `redis:6379`（compose／k8s），沒設逾時 → 用 StackExchange.Redis 預設值：

| 設定 | 預設 | 意思（套件說明原文節錄） |
|---|---|---|
| `SyncTimeout` | 5000 ms | synchronous operations 的時限 |
| `AsyncTimeout` | 同 `SyncTimeout` | asynchronous operations 的時限 |
| `ConnectTimeout` | 5000 ms | 建立連線的時限 |
| `BacklogPolicy` | `Default`：連線不健康時把指令**排隊等重連** | `FailFast`：「failing fast and not attempting to queue and retry」 |

Redis 掛掉時，指令被排隊、等到 `AsyncTimeout` 才丟例外、才 fail-open。
一個登入後的寫入請求會依序碰到 **限流 → 冪等 →（管理員）session 快取**，最壞約 **5 × 3 ≈ 15 秒**才回應——「Redis 掛了只是放行」實際上是「Redis 掛了全站變慢」。

---

## 二、修法（建議）

`Program.cs` 建立 `ConfigurationOptions` 時明確設定：

| 設定 | 建議值 | 理由 |
|---|---|---|
| `BacklogPolicy` | **`FailFast`** | 連線斷了就立刻失敗 → 立刻 fail-open，不排隊等 |
| `AsyncTimeout`／`SyncTimeout` | **500 ms** | 連線還在但 Redis 卡住時的上限；同機房／同叢集的 Redis 指令通常 < 1 ms |
| `ConnectTimeout` | 2000 ms | 只影響背景重連，不擋請求 |

代價：Redis 偶爾慢於 500 ms 時會 fail-open（重複提交防護、限流、session 快取暫時失效），三者的真正保證都在別處（DB 約束／advisory lock、DB 為 session 真實來源），與現行 fail-open 決策一致。

---

## 三、★ Case 表（✅ 已核可）

> 測試位置：`Test.Integration/`。「Redis 不可用」＝指向沒有服務的 port（例如 `127.0.0.1:1`），**使用正式環境同一套連線設定**（把 `Program.cs` 建 `ConfigurationOptions` 的邏輯抽成可測的方法，測試與正式共用，避免測試自己設一套）。

### F 組 — 三個元件的 fail-open

| # | 情境 | 期望 |
|---|---|---|
| F1 | 冪等 store，Redis 不可用 | `TryMarkAsync` 回 `true`，**1 秒內**回應 |
| F2 | session 快取，Redis 不可用 | `GetAsync` 回 null；`SetAsync`／`RemoveAsync` 不丟例外；各自 **1 秒內** |
| F3 | 限流器，Redis 不可用 | 取得許可（放行），**1 秒內** |

### G 組 — 整條管線

| # | 情境 | 期望 |
|---|---|---|
| G1 | 真實管線（WebApplicationFactory，**用正式的 Redis 元件**，Redis 不可用），一般使用者送寫入請求 | 請求被處理（不是 5xx 來自 Redis；限流與冪等都放行），**整個請求 2 秒內**回應 |

### H 組 — 連線設定

| # | 情境 | 期望 |
|---|---|---|
| H1 | 正式環境用的 `ConfigurationOptions` | `BacklogPolicy == FailFast`、`AsyncTimeout ≤ 500`、`SyncTimeout ≤ 500`、`AbortOnConnectFail == false` |

### R 組 — Redis 恢復後自動重連（2026/11/16 追加）

> 做法：Testcontainers 起真 Redis，**主機 port 事先選定並固定綁定**（`WithPortBinding(固定 port, 6379)`）——
> 預設的隨機 port 在容器停／啟後會換，連線端永遠連不回去，測不到「重連」。用正式的 `ConfigurationOptions` 連上後：
> 停容器（模擬 Redis 重啟／當機）→ 確認 fail-open → 再啟動容器 → 等待恢復。

| # | 情境 | 期望 |
|---|---|---|
| R1 | Redis 正常 → 停止 → 啟動 | 停止期間：三個元件 fail-open 且各自 1 秒內回應（同 F 組）。啟動後 **30 秒內**自動恢復正常行為：冪等同一 key 第二次回 `false`、session 快取 Set 後 Get 拿得回、限流達上限會擋 |
| R2 | 同 R1，恢復後 | 恢復期間發生的 fail-open **不留後遺症**：恢復後新的 key／session／計數都照常運作（不因先前的例外卡在壞狀態） |

### 預期紅燈
F1–F3、G1 在現況下會因為等 5 秒而**超過時限 → 紅**；H1 現況為預設值 → 紅。改設定後轉綠。
牙齒：把 `BacklogPolicy` 改回 `Default` → F／G 組變紅。

---

## 四、不在範圍

- 讓 fail-open 時發出告警（目前只有 log；Sentry 告警另議）

---

## 五、執行前提：整合測試要能透過 Rider MCP 跑

test-first 稽核只採計 Rider MCP 執行的測試，但 Testcontainers 要連 WSL 的 docker 需要 `DOCKER_HOST=tcp://127.0.0.1:2375`，
Rider 終端機的指令不經 shell、無法在指令前設環境變數。
→ 改用 Testcontainers 的使用者設定檔 `~/.testcontainers.properties`（`docker.host=tcp://127.0.0.1:2375`、`ryuk.disabled=true`），
執行前照舊在 WSL 起 socat 轉發。屬於本機環境設定、不進 repo，記入記憶。

---

## 實作紀錄（2026/11/16）

- 新增 `Infrastructure/Redis/RedisConnectionOptions.Build`（Program.cs 與測試共用）：`AbortOnConnectFail=false`、**`BacklogPolicy=FailFast`**、`AsyncTimeout`／`SyncTimeout`=500 ms、`ConnectTimeout`=2000 ms。
- 新增 `Test.Integration/RedisFailOpenIntegrationTests.cs`（F1–F3、G1、H1、R1/R2）：先編譯失敗 → 樁 → 紅；
  先以「只抽出現行設定」的版本實作，**實測每個操作 5000–5124 ms**（印證診斷），再加 FailFast／短逾時轉綠（各操作 < 1 秒）。
- 測試修正（`tdd-testfix`，期望值 404／2 秒內不變）：G1 原以為不存在的路由不碰 DB，實際上寫入方法會經過 `UnitOfWorkMiddleware` 開交易、測試環境無 DB → 500（log 顯示已通過冪等與限流）。改在 G1 的 Factory 以 no-op `IUnitOfWork` 取代。
- 牙齒：`BacklogPolicy` 改回 `Default`（逾時仍 500 ms）→ F1–F3、H1、R1 紅（各操作約 1000 ms，超過 1 秒時限）。**只縮短逾時不夠，FailFast 是關鍵。**
- 本機需 `~/.testcontainers.properties`＋WSL socat，Rider MCP 才能跑整合測試（見 §五）。
- 結果：單元 395、整合 82（+6）、E2E 53 passed（1 筆 flaky，見下）。

### E2E flaky（與本修正無關，另案追查）
G11.1（同一管理員第二個 session）一次 401、重試通過。後端 log **沒有任何 Redis fail-open 警告**，排除本修正。
懷疑：`UnitOfWorkMiddleware` 在 `await _next(context)` **之後**才 commit——回應可能先送到用戶端、交易才提交；
用戶端拿到新 sessionId 立刻用，可能讀到尚未提交的狀態。未證實，需另立 case 追查（若成立，也代表 commit 失敗時用戶端已收到 200）。
