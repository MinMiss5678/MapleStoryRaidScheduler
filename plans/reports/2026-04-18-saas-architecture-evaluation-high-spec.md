# 轉型評估（高規硬體版）：多租戶社群平台

**日期**：2026-04-18
**專案**：MapleStoryRaidScheduler
**前提**：不受 Lightsail $3.5 / 512MB RAM 限制，目標為中小型 SaaS 規模（數十至數百公會）

對照文件：`2026-04-18-saas-architecture-evaluation.md`（低規 $3.5 版）

---

## 硬體基準假設

| 項目 | 規格 | 月費估算 |
|---|---|---|
| 運算 | 4 vCPU / 8GB RAM（如 Lightsail $80 或 Hetzner CX32） | $20–$80 |
| 資料庫 | 獨立 PostgreSQL 節點（4GB RAM） | $20–$40 |
| 快取 | Redis（1GB，Upstash 或自建） | $0–$10 |
| CDN/Tunnel | Cloudflare（免費 tier 足夠） | $0 |

---

## 一、多租戶隔離策略升級

### 低規版（$3.5）→ 高規版比較

| 策略 | 低規建議 | 高規建議 |
|---|---|---|
| 租戶隔離 | 單 DB + GuildId 欄位（邏輯隔離） | **Schema-per-tenant**（物理隔離） |
| 快取 | MemoryCache（in-process） | **Redis**（跨 instance 共享） |
| 連線管理 | Dapper per-request 連線 | **PgBouncer** 連線池 |
| 水平擴展 | 單一 process | **多個 backend replica** |

### Schema-per-tenant 的優勢

```sql
-- 每個公會獨立 Schema
CREATE SCHEMA guild_840590742270771220;
CREATE TABLE guild_840590742270771220."Boss" (...);
CREATE TABLE guild_840590742270771220."Period" (...);

-- 查詢時動態切換 search_path
SET search_path TO guild_{guildId}, public;
```

**優勢**：
- 備份/還原可以 per-guild 操作
- 租戶資料完全物理隔離，無跨公會查詢洩漏風險
- Schema 刪除 = 租戶資料完整清除，GDPR 友善

**成本與注意事項**：
- Dapper Repository 需動態注入 `search_path`，改動集中於 `DbContext`（每個請求連線後執行 `SET search_path TO guild_{guildId}`）
- Migration 需 per-guild 執行，**不可一次對所有 Schema 開啟交易**：大量 Schema 同時 migrate 會短暫鎖定 PG System Catalog，需分批（Batching）執行
- 建議用 BackgroundService 分批 migrate，每批 10–20 個 Schema，批次間 sleep 避免 catalog 鎖競爭

```csharp
// 分批 migration 邏輯草稿
foreach (var batch in allGuildIds.Chunk(20))
{
    foreach (var guildId in batch)
        await RunMigrateAsync($"guild_{guildId}");
    await Task.Delay(TimeSpan.FromSeconds(2)); // 批次間緩衝
}
```

---

## 二、快取層（Redis）

低規版使用 in-process MemoryCache，多 replica 部署時快取不共享。高規版引入 Redis：

### 快取策略

| 快取對象 | Key 格式 | TTL | Invalidation |
|---|---|---|---|
| 使用者身分組 | `roles:{guildId}:{discordId}` | 5 分鐘 | `MemberUpdatedHandler` 觸發 |
| 公會系統設定 | `config:{guildId}` | 10 分鐘 | 管理員更新設定時 |
| 公會角色映射 | `role-mapping:{guildId}` | 30 分鐘 | `/setup` 更新時 |
| 報名截止狀態 | `deadline:{periodId}` | 1 小時 | `RegistrationDeadlineJob` 觸發 |

### 平滑切換介面（Phase 1 → Phase 2 零改動業務層）

定義統一快取介面，Phase 1 用 MemoryCache 實作，Phase 2 換 Redis 實作，業務層不感知：

```csharp
// Application/Interface/IAppCache.cs
public interface IAppCache
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
}

// Phase 1：InProcessCache（MemoryCache）
// Phase 2：RedisCache（StackExchange.Redis）
// DI 只需換一行註冊，業務層不動
```

### 實作位置

`AuthService`、`SystemConfigService` 等高頻讀取服務注入 `IAppCache`，Repository 層不變。

---

## 三、連線池（PgBouncer）

高規版多 backend replica 場景下，每個 instance 直連 PostgreSQL 會耗盡連線數。

```
backend replica 1 ─┐
backend replica 2 ─┼→ PgBouncer (transaction pooling) → PostgreSQL
backend replica 3 ─┘
```

**Transaction pooling 模式**：與現有 `UnitOfWorkMiddleware` 兼容（每個 HTTP 請求為一個 transaction 邊界）。

**啟用時機**：Schema-per-tenant 場景下，每個 tenant 可能有獨立連線需求，PG `max_connections` 容易提前耗盡。決定從單機 Docker 搬到獨立 DB 節點時，同步掛上 PgBouncer，資源消耗極小但防護效果立竿見影。

---

## 四、OAuth2 身分組查詢優化（與低規版相同）

無論硬體規格，此優化皆適用：

**登入時改用 `guilds.members.read` OAuth2 scope**：
- 現在：`GET /guilds/{guildId}/members/{discordId}`（Bot Token，消耗 bot rate limit）
- 改後：`GET /users/@me/guilds/{guildId}/member`（Bearer userAccessToken，per-user rate limit）

高規版搭配 Redis 快取此結果，TTL 5 分鐘，`MemberUpdatedHandler` 主動 invalidate。

---

## 五、Bot 架構升級

### 低規版
- 單一 Bot process，單一 GuildId 硬編碼
- Gateway 連線維持在 `DiscordBotService`

### 高規版
- Bot 支援多 Guild Gateway 事件（DSharpPlus 預設支援，移除 GuildId 過濾即可）
- `MemberUpdatedHandler` 依 GuildId 路由到對應 Redis invalidation key
- 考慮 **Discord Sharding**（若公會數超過 2500）

---

## 六、Onboarding 流程（Web Admin Panel）

高規版有足夠資源支撐完整的 Web Admin Panel：

```
Admin 登入 → 進入公會設定頁
  ↓
後端呼叫 Discord REST API（Bot Token）：
  GET /guilds/{guildId}/roles    → 身分組下拉選單
  GET /guilds/{guildId}/channels → 頻道下拉選單
  ↓
Admin 選擇 Admin Role / User Role / 通知頻道
  ↓
POST /api/setup/role-mapping  → 寫入 DiscordRoleMapping（per-guild schema）
POST /api/setup/notification  → 寫入 SystemConfig.ChannelId
```

所需新 API：
```csharp
GET  /api/discord/guild/roles     // Admin only
GET  /api/discord/guild/channels  // Admin only
POST /api/setup/role-mapping
POST /api/setup/notification
```

---

## 七、Guild Active Check

高規版可實作完整的公會生命週期管理：

```sql
-- 新增公會註冊表
CREATE TABLE "GuildRegistration" (
    guild_id     BIGINT PRIMARY KEY,
    is_active    BOOLEAN NOT NULL DEFAULT FALSE,
    registered_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    admin_discord_id BIGINT
);
```

Bot 加入新伺服器時（`GuildCreated` 事件）寫入 `is_active = false`，管理員完成 `/setup` 後更新為 `true`。未啟用公會的所有 Slash Command 回傳引導訊息。

### 女巫攻擊（Sybil Attack）防護

開放 Public Bot 後，惡意大量創群註冊會造成 Schema 數量爆炸。大量 Schema 本身不耗 RAM，但 PG `information_schema` 查詢會隨 Schema 數增加而變慢。

**冷熱分離策略**：

```sql
ALTER TABLE "GuildRegistration"
    ADD COLUMN last_active_at TIMESTAMPTZ,
    ADD COLUMN is_dormant BOOLEAN NOT NULL DEFAULT FALSE;
```

- 超過 30 天無排班活動的公會標記 `is_dormant = true`
- BackgroundService 的巡檢（`DailyNotificationService`、`RegistrationDeadlineJob`）跳過 dormant 公會
- Schema 保留但不參與任何自動化流程，降低系統負載
- 重新活躍時自動解除 dormant 標記

---

## 潛在漏洞與補強方案

| 漏洞 | 影響程度 | 程式碼位置 | 補強方案 |
|---|---|---|---|
| **Cross-tenant Leak（讀+寫）** | 極高（安全性） | `UnitOfWorkMiddleware.cs:17` — GET 請求直接跳過，search_path 從未設定 | 新增 `TenantContextMiddleware`，所有請求（含 GET）都設定 search_path，finally 強制清除；`UnitOfWorkMiddleware` 只管 transaction |
| **OAuth2 Token 明文** | 高（安全性） | `SessionRepository.cs:24-25` — AccessToken/RefreshToken 明文字串直寫 DB | 寫入前以 AES-256 加密，讀取後解密；Refresh 邏輯確保 Token 過期時自動換發 |
| **IAppCache 不存在** | 中（可維護性） | `Program.cs` — 只有 `AddMemoryCache()`，無抽象介面 | 建立 `IAppCache` 介面，Phase 1 封裝 MemoryCache，Phase 2 換 Redis，業務層不感知 |
| **Redis Key 碰撞** | 中（正確性） | IAppCache 建立時需同步設計 | 強制加入 `msr:` ApplicationPrefix，Key 格式 `msr:{module}:{guildId}:{id}` |
| **Redis Memory Leak** | 低（資源） | GuildRegistration 尚無軟刪除欄位 | 公會刪除時觸發 Hook 批次清除 `msr:*:{guildId}:*` |
| **Discord Sharding 未預留** | 高（可用性） | `DiscordBotService.cs:18` — 直接 `ConnectAsync()`，無 Sharding 配置 | 門檻 **800 公會**觸發；在 `ConnectAsync` 前預留 ShardCount 參數擴充點 |
| **API Rate Limit** | 中（可用性） | 無現有實作 | 加入 Distributed Rate Limiter（per-guild），防止單一公會影響其他租戶 |

---

## 低規 vs 高規決策對照表

| 維度 | 低規（$3.5） | 高規（$40–$120） |
|---|---|---|
| 租戶隔離 | 單 DB + GuildId 欄位 | Schema-per-tenant |
| 快取 | MemoryCache（in-process） | Redis（shared） |
| 連線管理 | 直連 PostgreSQL | PgBouncer |
| 水平擴展 | 單 process | 多 replica |
| Setup UX | Slash Command | Web Admin Panel |
| Bot 擴展 | 單 Guild hardcoded | 多 Guild + Sharding（>800 公會觸發） |
| 適用公會數 | < 20 | < 800（Sharding 前） |
