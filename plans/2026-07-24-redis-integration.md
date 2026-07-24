# Redis 導入計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 目標

補掉**多 pod 漏洞**：目前 idempotency de-dup 與限流都用**行程內記憶體（per-pod）**，多副本時各自為政 → 去重/限流跨不了 pod。Redis 提供**共享狀態**解掉這個。
附帶：一個誠實的 Redis 實戰點（面試用）——「把 de-dup 從單機記憶體搬到 Redis 解多 pod」。

## 範圍（分階段，右尺寸）

### Phase 1（✅ 已完成 MR!24）
**idempotency de-dup：`IMemoryCache` → Redis**
- `IdempotencyMiddleware` 改用 Redis **`SET key NX EX 60`**（單一原子命令）。
- 額外好處：`SET NX` 原子 → 順帶修掉現在 `TryGetValue → Set` 的微小 race（連記憶體版都有）。
- **契約不變**：缺 key/非 UUID → 400、同 key 60 秒內 → 409。
- 抽 `IIdempotencyStore`（`Task<bool> TryMarkAsync(key, ttl)`）→ middleware 不直接綁 Redis：單元測 mock、整合測用真 Redis。

### Phase 2 / 3（選配、之後——都不是「現在」的正確性 bug）

**② 限流：per-pod → 分散式（✅ 已完成）**
- 自訂 `RedisFixedWindowRateLimiter`（Lua 原子 `INCR`+首次 `PEXPIRE`）插進 .NET `PartitionedRateLimiter`——**未加第三方套件**（hand-roll，portfolio 料）。
- fail-open 同 Phase 1；回報 `IdleDuration` 讓框架回收 idle partition。整合測含「跨連線＝跨 pod 共用計數」。
- 邊際價值低：per-pod 限流的實際效果只是「上限 ×N pod、較寬鬆」，**不是正確性 bug**——做它是 portfolio/readiness，不是救火。

**③ session 撤銷跨 pod 失效（★ 真 gap，Phase 1 漏了、後來才想清楚）（✅ 已完成）**
- ~~`SessionService` 用 IMemoryCache 讀穿快取。**讀**沒問題（miss → 查 DB → 自癒）。但 `DeleteAsync` / `DeleteByDiscordAsync` **只清「當下 pod」的快取 + DB** → 其他 pod 的快取還留著已刪的 session，直到 TTL → 登出/強制下線在多 pod 下不會立即在所有 pod 生效。~~
- 修法（已採）：抽 `ISessionCache`（Get/Set/Remove）+ `RedisSessionCache`（JSON、fail-open，同 idempotency 決策）→ session 快取搬**共享 Redis**，撤銷一次 `KEYDEL` 即在所有 pod 生效。`DeleteAsync` 改「先刪 DB 再清快取」（cache-aside 慣例）。
- **關鍵**：`SessionService` 用在 **API 與 bot 兩個 host**；bot 的 `MemberRemoved/Updated` 也會撤 session → **bot 也接了 Redis**（compose/k8s bot 補 `Redis__Configuration`），否則 bot 撤了、API pod 還留著。
- 整合測 `RedisSessionCacheIntegrationTests`：set/get round-trip +「撤銷跨連線＝跨 pod 立即生效」。
- 殘留（YAGNI）：cache-aside 仍有「刪快取後被舊值回填」的極窄窗口（TTL 上界）；要全消可改 post-commit 失效（`DbContext.AfterCommit`）或 pub/sub。現 replicas=1，不急。

### 非範圍（YAGNI，這次不碰）
- session **讀**快取的讀面（per-pod miss 自癒、不影響正確性）——僅「**撤銷**」面是 gap，見上方 Phase 3。
- 一般分散式快取。
- runtime **不碰 Dapper**（Redis 只進 middleware / infra 層）。

> ⚠️ 修正紀錄：本 plan 初版把 session cache 整個列「非範圍／不影響正確性」——**不準**。讀面對，但**撤銷面是真多 pod gap**，已改列 Phase 3。

## 關鍵決策（動手前要拍板）

**★ Redis 掛掉時：fail-open 還是 fail-closed？**
- **fail-open（建議）**：Redis 連不到 → **放行、記 log**，暫時失去 de-dup。理由：de-dup 是「防護」不是「正確性關卡」，不該因快取層抖動就擋掉所有寫入。真正的重複由**上層守**（報名有 `ExistAsync` + auto-assign 的 advisory lock；`X-Idempotency-Key` 只是額外一層）。代價：Redis 故障期間重開雙擊窗口（短、可接受）。
- fail-closed：Redis 掛 → 拒所有寫入。安全但把 Redis 變成單點故障 → 對這規模不划算。
- → **選 fail-open**，`try/catch` 包 Redis 呼叫，錯誤時放行 + 記 log。

其餘：TTL 60s（同現況）、key 格式 `idempotency:{uuid}`（同現況）、StackExchange.Redis 自動重連。

## 基礎設施

- **docker-compose**：加 `redis` 服務（`redis:7-alpine`）。
- **k8s**：redis Deployment + Service（單 pod 夠；密碼走 Secret，比照 DB 的檔案掛載模式）。
- **設定**：連線字串走 env/secret，沿用現有 `*File` 覆寫慣例。
- **健康檢查**：readiness **不**因 Redis 掛而 fail（配合 fail-open）——Redis 非啟動必需。

## 驗收

- [ ] 缺 key / 非 UUID → 400；同 key 60 秒內 → 409（現有整合測仍過）。
- [ ] **跨連線（模擬跨 pod）**：兩條連線共用同一 Redis，同 key → 第二個 409（Testcontainers Redis 整合測）。
- [ ] Redis 掛（停容器）→ 寫入仍放行、有 log（fail-open 驗證）。
- [ ] `IdempotencyMiddlewareTests`（單元）改成 mock `IIdempotencyStore`，仍綠。
- [ ] compose + k8s 起得來、middleware 連得到 Redis。
- [ ]（若做 Phase 2）限流上限跨 pod 一致。

## 工時估

- Phase 1 ≈ 一個週末（Redis infra + `IIdempotencyStore` 抽象 + Redis 實作 + 整合測 + compose/k8s）。
- Phase 2 ≈ 另一塊（自訂/套件分散式 limiter + 測試）。

## 面試框（誠實）

> 「我把 idempotency de-dup 從 per-pod 的 `IMemoryCache` 搬到 Redis 的 `SET NX EX`——單機記憶體多副本各自為政、跨不了 pod。選 **fail-open**：Redis 掛就退化成沒 de-dup、放行記 log，不讓快取層抖動擋掉寫入；真正的重複由 DB 層 / advisory lock 守。限流我判斷 per-pod 只是上限變寬、非正確性問題，排後面。」

→ 展示：知道為何要 Redis、知道 fail-open/closed 的取捨、知道哪些該做哪些 YAGNI。
