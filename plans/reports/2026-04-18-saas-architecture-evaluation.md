# 轉型評估：單公會工具 → 多租戶社群平台

**日期**：2026-04-18
**專案**：MapleStoryRaidScheduler
**範疇**：從「單一公會自用」轉向「多租戶社群 SaaS」的技術架構評估

---

## 執行摘要

經過對原始碼的全面審查，最關鍵的發現是：**`GuildId` 僅存在於環境變數，資料庫完全沒有租戶隔離欄位。** 這是轉型 SaaS 最核心的技術債。

---

## 一、多租戶隔離與擴展性

### 現況診斷

```sql
-- 目前所有核心表均無 guild_id
CREATE TABLE "Player"   (discord_id BIGINT PRIMARY KEY, ...)
CREATE TABLE "Boss"     (id UUID PRIMARY KEY, ...)
CREATE TABLE "Period"   (id UUID PRIMARY KEY, ...)
CREATE TABLE "TeamSlot" (id UUID PRIMARY KEY, ...)
```

`GuildId` 只出現在 `DiscordOptions`（環境變數），Bot 和 Web API 各自硬綁一個公會。

### 多租戶策略選擇

| 策略 | 資源消耗 (Lightsail $3.5) | 隔離強度 | 遷移複雜度 |
|---|---|---|---|
| **A. 單 DB + GuildId 欄位** | 最低 | 邏輯隔離 | 中等（需改所有表 + Query） |
| **B. Schema-per-tenant** | 中 (PG schema 切換) | 物理隔離 | 高 |
| **C. DB-per-tenant** | 極高（無法跑在 $3.5） | 最強 | 極高 |

**建議：策略 A（單 DB + GuildId）**，原因：
- $3.5 Lightsail 512MB RAM，B/C 方案在記憶體上直接撐不住
- Dapper 的 SqlBuilder 可以在 Repository 層統一注入 `WHERE guild_id = @GuildId`，改動面集中

### 最小可行改動清單

**資料庫層（需新增欄位的表）：**

```sql
ALTER TABLE "Boss"              ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "Period"            ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "TeamSlot"          ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "SystemConfig"      ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "DiscordRoleMapping" ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
-- Player/Character 可從 Discord 層解析，不一定需要 guild_id
```

**Application 層：**

在 `IUnitOfWork` 或 DI 範圍內注入 `TenantContext`，Repository 基類統一附加 `guild_id` 條件。

### SqlBuilder 在多公會並發下的效能陷阱

自製 `SqlBuilder` 使用 `ExpressionVisitor` 在每次查詢時重新解析 Lambda 表達式樹。單公會低頻呼叫沒問題，但多公會並發時：

**潛在問題**：`SqlExpressionVisitor` 無快取，每次 `Build()` 都是完整的 Reflection 計算。

**建議**：在 `QueryBuilder.Build()` 加 `ConcurrentDictionary` 快取 Expression → SQL 片段的編譯結果，Key 為 `typeof(T).FullName + expression.ToString()`。

---

## 二、OAuth2 與 Bot 的權限邊界

### 雙軌認證在多公會場景的健壯性評估

**現有流程的核心邏輯：**

```csharp
// AuthenticationMiddleware.cs 核心路徑
// 1. Session 路徑（Admin）：sessionId 存在 DB Session 表
// 2. JWT 路徑（Player）：JWT payload 含 discordId，Role 從 Player 表查
```

**多公會場景下的邏輯漏洞：**

| 情境 | 現有行為 | 多公會後的問題 |
|---|---|---|
| 同一 Discord ID 在兩個公會都有角色 | 正常 | JWT 無 GuildId claim，後端無法辨識當前公會上下文 |
| 同名角色在不同公會 | N/A | Character 表無 guild_id，查詢會拉到其他公會的角色 |
| Admin 在 A 公會降權 | `MemberUpdatedHandler` 清除 Session | Bot 監聽的是單一 GuildId，無法偵測其他公會的角色變更 |

**建議修正：**

JWT payload 加入 `guild_id` claim：

```csharp
// JwtService.CreateJwt() 修改
var claims = new[]
{
    new Claim("discordId", discordId.ToString()),
    new Claim("guildId", guildId.ToString()),  // 新增
};
```

### Discord API Rate Limit 風險

**現有機制**：登入時才抓 Discord Roles（`GetUserAsync` → `GetGuildMemberAsync`）。

**多公會場景**：若 100 個公會同時有玩家登入，`GetGuildMemberAsync` 請求量會急增。Discord 的 Rate Limit 是 per-bot，不是 per-guild。

**優化建議**：

1. **登入路徑改用 `guilds.members.read` OAuth2 scope**：登入時用使用者自己的 `accessToken` 查詢身分組，取代 Bot Token 的 REST 呼叫。
   - 現在：`GET /guilds/{guildId}/members/{discordId}`（Bot Token，計入 bot rate limit）
   - 改後：`GET /users/@me/guilds/{guildId}/member`（Bearer userAccessToken，per-user rate limit，不消耗 bot 配額）
   - OAuth2 授權 URL 加上 `guilds.members.read` scope 即可，`accessToken` 已在 `ExchangeCodeAsync` 回傳中存在。
2. 短期：在 MemoryCache 快取 `discordId:guildId → roles` 結果，TTL 設 5 分鐘
3. 中期：`MemberUpdatedHandler`（已有）主動 invalidate 快取，確保角色異動即時反映

---

## 三、管理員 UX 摩擦力（Onboarding）

### 現況

目前「身分組綁定」需要手動操作 `DiscordRoleMapping` 表：

```sql
INSERT INTO "DiscordRoleMapping" (discord_role_id, role, priority)
VALUES (123456789, 'Admin', 1);
```

非技術管理員完全無法完成此步驟。

### Onboarding 三層摩擦力分析

**L1 - 最高摩擦（必須解決）：**
- Bot 邀請後需手動設定 `GuildId`（環境變數，需重啟服務）
- `DiscordRoleMapping` 無 UI，純 SQL 操作

**L2 - 中等摩擦（建議解決）：**
- Webhook 設定（目前 `ChannelId` 硬編碼）

**L3 - 低摩擦（可接受）：**
- `SystemConfig` 的截止時間設定（邏輯直覺）
- `BossTemplate` 職業需求權重設定（已有 Slash Command）

### 最低成本解法

不需要大改 UI，只需補齊以下兩個 Slash Command：

```
/setup admin-role @角色名稱
/setup user-role @角色名稱
/setup notification-channel #頻道名稱
```

Bot 接收後寫入 DB，取代手動 SQL。Boss 範本已有 Slash Command，無需重複實作。

---

## 四、運維安全性

### Cloudflare Tunnel 在多租戶下的安全邊界

**現有架構優勢**：`cloudflared` 作為 reverse proxy，所有公會流量統一走加密隧道，不暴露原始 IP。這在多租戶下天然是優勢。

**域名策略選擇：**

| 選項 | 說明 | 建議 |
|---|---|---|
| A. 全部公會共用同一域名 | 最省資源，GuildId 隔離靠 DB + JWT | 採用 |
| B. 每公會子域名 | 需 Cloudflare DNS 管理，$3.5 Lightsail 無法負荷 | 不採用 |

**建議選項 A**，並在 Webhook URL 加入 HMAC 簽名驗證，確保跨租戶請求無法偽造。

### X-Idempotency-Key 在多端場景的衝突問題

**多載具衝突場景**：不同公會同時觸發相同操作，Key 空間重疊概率極低，但需要加入 `guildId` 作為 Key 前綴：

```javascript
// 前端建議
const key = `${guildId}-${crypto.randomUUID()}`;
```

---

## 轉型風險清單（最容易崩潰的 3 個技術點）

### Risk 1：資料庫無 GuildId 隔離（最高優先）

**崩潰情境**：公會 A 的 Admin 登入後，`/api/boss` 回傳的是所有公會的 Boss 資料。

**根本原因**：所有 Query 無 `guild_id` WHERE 條件。

**修復成本**：中高（需改 12 張表的 migration + 所有 Repository 查詢）。

---

### Risk 2：Bot 的 GuildId 硬編碼（次高優先）

**崩潰情境**：Bot 被邀請到第二個公會伺服器後，`MemberUpdatedHandler` 只處理原始公會的角色變更事件，第二個公會的 Admin 降權後 Session 不會被清除。

**根本原因**：`DiscordOptions.GuildId` 單一值，事件處理無多 GuildId 過濾邏輯。

**修復成本**：低（事件處理加 GuildId 比對，DB 存多個 GuildId）。

---

### Risk 3：SystemConfig 單例（中等優先）

**崩潰情境**：公會 A 設截止時間為週五，公會 B 設截止時間為週三，但 `SystemConfig` 表只有一行。

**根本原因**：

```sql
CREATE TABLE "SystemConfig" (
    id SERIAL PRIMARY KEY,  -- 設計為 singleton
    deadline_day_of_week INT,
    deadline_time TIME
);
```

**修復成本**：低（加 guild_id 欄位，unique constraint 改為 per-guild）。

---

## 架構亮點點評

| 設計 | SaaS 轉型價值 | 評分 |
|---|---|---|
| **自製 SqlBuilder + Dapper** | 輕量無 ORM 開銷，$3.5 Lightsail 能撐住。Repository 基類統一注入 GuildId 條件，修改面集中。 | 5/5 |
| **UnitOfWork + Middleware** | 事務邊界清晰，GuildId 可注入到同一 UoW 上下文，所有 Repository 透明取用。 | 5/5 |
| **CQRS-Lite（讀寫分離）** | Query 側 JOIN SQL 無事務，天然適合讀多寫少的排程展示場景。多租戶後 Query 只需加 `guild_id` 過濾。 | 4/5 |
| **雙軌認證** | Session（Admin 長效）+ JWT（玩家短效）在多租戶下依然合適，只需 token 加入 GuildId claim。 | 4/5 |
| **Cloudflare Tunnel** | 不暴露原始 IP，加密傳輸，多租戶共用無額外成本。轉社群最省力的基礎設施優勢。 | 5/5 |
| **Docker Secrets 管理** | `secrets:` 方案比環境變數安全，多租戶場景下敏感 Token 不在 process 環境中暴露。 | 4/5 |

---

## 商業/作品集建議

### README 核心論述框架

**1. 極低資源的多租戶設計**

> 採用單 DB + GuildId 邏輯隔離策略，搭配 Dapper 輕量 ORM 與自製型別安全 SqlBuilder，全端運行於 Lightsail $3.5（512MB RAM）。無 Entity Framework，無 ORM 映射開銷。

**2. 事件驅動的身分同步**

> Discord 角色變更即時同步：`MemberUpdatedHandler` 監聽 Gateway 事件，Admin 降權後系統即時廢止所有 Session，無需手動管理。

**3. Cloudflare Tunnel + 資料隔離承諾**

> 所有公會資料在資料庫層以 GuildId 隔離，服務層強制驗證租戶 Claim，API 層 UnitOfWork 上下文攜帶 GuildId 全程不洩露。流量透過 Cloudflare 加密隧道，不暴露原始伺服器 IP。

---

## 技術叮嚀回應

### PostgreSQL 18 資源壓榨

建議在 `compose.yaml` 加記憶體限制：

```yaml
database:
  deploy:
    resources:
      limits:
        memory: 256M  # 預防 PG shared_buffers 無限吃記憶體
```

`SystemConfig`（截止設定）和 `DiscordRoleMapping`（角色綁定）最適合加 MemoryCache，變動頻率極低，讀取頻率極高。

### Guild Active Check

在 `MemberUpdatedHandler` 的 Guild 過濾邏輯基礎上，擴展 `GuildRegistrationService`：Bot 加入新伺服器時先寫入 `GuildRegistration` 表，未完成管理員認證的 Guild 的所有 Slash Command 直接回應「請先完成公會初始化設定」。

---

## 是否保留自用版？

**不需要。** 策略 A（單 DB + GuildId）天然支援單公會模式，自用版 = 多租戶版只有一筆 GuildRegistration 記錄。

用一個設定開關控制開放程度：

```json
{
  "SaaS": {
    "IsPublicRegistrationOpen": false
  }
}
```

- `false`：Bot 收到 `GuildCreated` 事件時自動拒絕，僅服務白名單公會（自用模式）
- `true`：開放任何公會完成 onboarding

維護兩份 codebase 代價過高（雙倍 bug fix、雙倍 migration、手動 cherry-pick），應廢棄自用分支，以設定檔控制模式即可。

---

## 未解決的問題

1. **自動排程的並行安全**：`AutoAssignAsync` 在多公會高並發報名時，同一 `TeamSlot` 是否有 row-level lock？目前 Dapper 層看不到 `SELECT FOR UPDATE`。
2. **Webhook 加密存儲**：目前 ChannelId 以明文存 DB，多租戶後若支援各公會自訂通知頻道，是否需要加密欄位？
