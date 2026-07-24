# SaaS 轉型執行計畫

**日期**：2026-04-18
**參考**：`plans/reports/2026-04-18-saas-architecture-evaluation-high-spec.md`

> ⚠️ **2026-07-25 修正紀錄**：本計畫寫於 Redis / outbox / 限流 / session 快取（皆 7 月導入）之前，已對照現況修正：
> 1. 遷移編號 000003/000004 已被占用（`teamslot_source` / `add_outbox_message`）→ 改 **000005**。
> 2. Phase 3 快取改用**既有 Redis 抽象**（`ISessionCache`/`RedisSessionCache`），不再新增 per-pod MemoryCache（多 pod 會重蹈覆轍）。
> 3. Token 加密範圍須**涵蓋 Redis session 快取**（`RedisSessionCache` 以 JSON 明文存 token）。
> 4. Middleware 管線已擴為 **5 層**（例外→冪等→認證→限流→UoW）。
> 5. 通知頻道須改為**每公會欄位**（現為 env config）；outbox/截止通知路徑須 **guild-aware**。
> 6. 多公會通知投遞規模化 → **MQ**（見文末附註）。

---

## Phase 1 — 基礎建設（阻塞後續所有 Phase）

> 不完成 Phase 1，任何多租戶功能都無法正確運作。

### 1-1. 資料庫：新增 GuildRegistration 表

```sql
-- db/migrations/000005_add_guild_registration.up.sql
CREATE TABLE "GuildRegistration" (
    guild_id         BIGINT PRIMARY KEY,
    is_active        BOOLEAN      NOT NULL DEFAULT FALSE,
    schema_migrated  BOOLEAN      NOT NULL DEFAULT FALSE,
    is_dormant       BOOLEAN      NOT NULL DEFAULT FALSE,
    last_active_at   TIMESTAMPTZ,
    registered_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    admin_discord_id BIGINT
);
```

### 1-2. 資料庫：SystemConfig、DiscordRoleMapping 加 guild_id（+ 每公會通知頻道）

```sql
-- db/migrations/000005_add_guild_registration.up.sql（續）
ALTER TABLE "SystemConfig"       ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "DiscordRoleMapping" ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
-- 通知頻道現在是 env config（Discord__ChannelId，單一）→ 多租戶須每公會一個，移進 SystemConfig
ALTER TABLE "SystemConfig"       ADD COLUMN "ChannelId" BIGINT;
-- 原本的 singleton 設定移至 guild_id = 0（預設公會）
```

### 1-3. Domain：新增 GuildRegistration Repository 介面

- `Domain/Repositories/IGuildRegistrationRepository.cs`

### 1-4. Infrastructure：實作 GuildRegistrationRepository

- `Infrastructure/Repositories/GuildRegistrationRepository.cs`

### 驗收條件

- [ ] Migration 執行無錯誤
- [ ] `GuildRegistration` 表可正常 CRUD
- [ ] 現有功能（單公會）不受影響

---

## Phase 2 — 安全修補（可與 Phase 1 並行，不依賴 Phase 3+）

> 這兩個漏洞在多租戶前就應修復。

### 2-1. 新增 TenantContextMiddleware（GET + POST 都需要隔離）

**背景**：`UnitOfWorkMiddleware` 跳過 GET 請求，但 Schema-per-tenant 的 `search_path` 必須對**所有請求**生效，否則 GET（查詢排班表、成員名單）同樣有跨租戶讀取風險。

**責任拆分**：

| Middleware | 責任 |
|---|---|
| `TenantContextMiddleware`（新） | 所有請求：設定 `search_path`，finally 清除 |
| `UnitOfWorkMiddleware`（現有） | 寫入請求：管理 transaction，不負責 search_path |

**新增檔案**：`Presentation.WebApi/Middleware/TenantContextMiddleware.cs`

```csharp
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, DbContext db)
    {
        var guildId = context.User.FindFirst("guildId")?.Value;

        // Phase 2 為 no-op（單租戶），Phase 7 填入實際 search_path 切換
        if (guildId != null)
            await db.Connection.ExecuteAsync(
                $"SET search_path TO \"guild_{guildId}\", public");
        try
        {
            await _next(context);
        }
        finally
        {
            // GET 和 POST 都需清除，確保連線歸還 PgBouncer 前已重設
            if (guildId != null)
                await db.Connection.ExecuteAsync("SET search_path TO public");
        }
    }
}
```

**Middleware 順序**（`Program.cs`，現為 5 層，插入 TenantContext 後成 6 層）：

```csharp
app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();
app.UseMiddleware<AuthenticationMiddleware>();   // 先驗證，取得 guildId claim
app.UseRateLimiter();                            // 被限流的請求不需設 search_path
app.UseMiddleware<TenantContextMiddleware>();    // 設定 search_path（no-op 直到 Phase 7）
app.UseMiddleware<UnitOfWorkMiddleware>();       // 寫入才開 transaction
```
> 放在 RateLimiter 之後：被擋（429）的請求不必白做一次 `SET search_path` 的 DB 往返。

### 2-2. OAuth2 Token 加密

**檔案**：`Infrastructure/Repositories/SessionRepository.cs:24-25`

- 新增 `ITokenEncryptionService`（Application 層介面）
- 實作 AES-256-GCM 加密（Infrastructure 層）
- `CreateAsync`、`UpdateAsync` 寫入前加密
- `GetAsync` 讀取後解密
- ⚠️ **加密須涵蓋 Redis 快取**：7 月導入的 `RedisSessionCache` 以 JSON **明文**存整個 Session（含 AccessToken/RefreshToken）→ 只加密 DB、Redis 仍明文則破功。做法：快取存加密後的值，或快取層乾脆不放 token（只放非敏感欄位）。

### 驗收條件

- [ ] `TenantContextMiddleware` 註冊於 `AuthenticationMiddleware` 之後、`UnitOfWorkMiddleware` 之前
- [ ] Phase 2 階段 search_path 為 no-op（guildId claim 尚未存在），現有功能不受影響
- [ ] Session 表中 AccessToken/RefreshToken 欄位存的是密文
- [ ] 登入流程可正常解密並使用 Token

---

## Phase 3 — 快取抽象層（Redis）

> 在 Bot 多公會支援前建立，確保 MemberUpdatedHandler 可以 invalidate 快取。
> ⚠️ **改用 Redis，非 in-process MemoryCache**：多租戶 SaaS 要水平擴 = 多 pod；per-pod MemoryCache 跨不了 pod（7 月正因此把 session 快取搬 Redis）。沿用既有 `IConnectionMultiplexer` 與 `ISessionCache`/`RedisSessionCache` 的模式，別另開一套 per-pod 快取。

### 3-1. 建立 IAppCache 介面

**檔案**：`Application/Interface/IAppCache.cs`

```csharp
public interface IAppCache
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
    Task RemoveByPrefixAsync(string prefix); // 公會刪除時批次清除
}
```

### 3-2. 實作 RedisAppCache（複用現有 IConnectionMultiplexer）

**檔案**：`Infrastructure/Cache/RedisAppCache.cs`

- JSON 序列化 + **fail-open**，比照 `RedisSessionCache` 的決策。
- Key 格式強制加 `msr:` 前綴：`msr:{module}:{guildId}:{id}`。
- `RemoveByPrefixAsync` 用 **`SCAN` 逐批 `UNLINK`**（勿用會阻塞的 `KEYS`）。

### 3-3. 替換現有直接使用的快取

- `AuthService`：快取 `msr:roles:{guildId}:{discordId}`，TTL 5 分鐘
- `SystemConfigService`：快取 `msr:config:{guildId}`，TTL 10 分鐘
- 既有 `ISessionCache`/`RedisSessionCache` 保留或併入 `IAppCache`（**擇一**，避免兩套 Redis 快取抽象並存）

### 3-4. MemberUpdatedHandler 加 Cache Invalidation

**檔案**：`Infrastructure/Discord/Handlers/MemberUpdatedHandler.cs`

角色變更後呼叫 `_cache.RemoveAsync($"msr:roles:{guildId}:{discordId}")`

### 驗收條件

- [ ] `IAppCache` 注入正常
- [ ] 登入後角色查詢結果被快取
- [ ] 角色變更後快取被清除，下次登入取得新值

---

## Phase 4 — Bot 多公會支援

> 依賴 Phase 1（GuildRegistration 表）和 Phase 3（Cache）。

### 4-1. 移除 GuildId 硬編碼過濾

**檔案**：`Infrastructure/Discord/Handlers/MemberUpdatedHandler.cs`

- 移除對單一 `DiscordOptions.GuildId` 的過濾判斷
- 改從 `GuildRegistration` 表查詢事件所屬公會是否為已註冊公會

### 4-2. Bot GuildCreated 事件處理

**新增**：`Infrastructure/Discord/Handlers/GuildCreatedHandler.cs`

觸發流程採 **Channel\<T\> + DB 雙保險**：

```csharp
// Channel 於 Program.cs 註冊為 Singleton
services.AddSingleton(Channel.CreateUnbounded<long>());

public class GuildCreatedHandler : IEventHandler<GuildCreatedEventArgs>
{
    public async Task HandleEventAsync(DiscordClient sender, GuildCreatedEventArgs e)
    {
        var guildId = (long)e.Guild.Id;
        await _repo.CreateAsync(guildId);      // DB 持久化（重啟不丟失）
        _channel.Writer.TryWrite(guildId);     // 即時通知 TenantMigrationService
    }
}
```

`TenantMigrationService` 雙路消費：
- 啟動時：查 `schema_migrated = false` 補漏（應對重啟遺漏）
- 執行中：`Channel.Reader.ReadAsync()` 即時消費

`Program.cs` 事件註冊：
```csharp
services.ConfigureEventHandlers(b => b
    .AddEventHandlers<MemberUpdatedHandler>()
    .AddEventHandlers<GuildCreatedHandler>()); // 新增
```

- 未完成 setup 的公會回傳引導訊息（`is_active = false` 攔截）

### 4-3. Guild Active Check

- 所有 Bot 功能執行前檢查 `GuildRegistration.is_active`
- `is_active = false` 時回傳「請先完成公會初始化設定」

### 4-4. Outbox / 截止通知路徑改 guild-aware（7 月新增，計畫原版未涵蓋）

- `ConfigChanged` outbox 事件 payload 帶 `guildId`；`ConfigChangedOutboxHandler` 依 guild 喚醒對應排程。
- `RegistrationDeadlineJob` 現為單公會（讀單一 `SystemConfig`）→ 改為**逐公會**讀各自截止設定、各自判斷發送，且發到各公會自己的 `SystemConfig.ChannelId`。
- `DailyNotificationService` 同理，逐公會聚合、逐公會頻道。

### 驗收條件

- [ ] Bot 加入新伺服器後，`GuildRegistration` 有新記錄
- [ ] 未啟用公會的指令被攔截
- [ ] 多公會角色變更事件各自正確處理

---

## Phase 5 — OAuth2 優化（guilds.members.read）

> 依賴 Phase 3（Cache）。

### 5-1. 修改 DiscordOAuthClient.GetUserRolesAsync

**檔案**：`Infrastructure/Services/DiscordOAuthClient.cs:68-78`

```csharp
// 改用 userAccessToken，不用 Bot Token
public async Task<IEnumerable<string>> GetUserRolesAsync(
    ulong discordId, ulong guildId, string userAccessToken)
{
    _http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", userAccessToken);
    var resp = await _http.GetAsync(
        $"https://discord.com/api/users/@me/guilds/{guildId}/member");
    // ...
}
```

### 5-2. OAuth2 授權 URL 加入 guilds.members.read scope

**檔案**：`Infrastructure/Services/DiscordOAuthClient.cs`（ExchangeCodeAsync 前的 redirect URL 組裝）

### 5-3. 登入流程傳入 guildId

- OAuth2 state 參數帶入 guildId
- Callback 解析 state → 傳給 GetUserRolesAsync

### 驗收條件

- [ ] 登入時不再呼叫 `GET /guilds/{id}/members/{userId}`（Bot Token）
- [ ] 改呼叫 `GET /users/@me/guilds/{id}/member`（Bearer Token）
- [ ] 快取命中後不發出任何 Discord API 請求

---

## Phase 6 — Web Admin Panel（Onboarding）

> 依賴 Phase 1、Phase 4。

### 6-1. 新增 Discord Guild 資訊 API

```csharp
GET /api/discord/guild/roles     // 回傳公會所有身分組
GET /api/discord/guild/channels  // 回傳公會所有頻道
```

### 6-2. 新增 Setup API

```csharp
POST /api/setup/role-mapping  // 寫入 DiscordRoleMapping（含 guild_id）
POST /api/setup/notification  // 寫入 SystemConfig.ChannelId（含 guild_id）
```

### 6-3. 前端管理頁面

- `web/app/admin/setup/page.tsx`
- 身分組下拉選單（Admin Role / User Role）
- 通知頻道下拉選單
- 儲存後標記 `GuildRegistration.is_active = true`

### 驗收條件

- [ ] Admin 登入後可看到 Setup 頁面
- [ ] 選擇身分組與頻道後儲存，DB 正確寫入
- [ ] 完成 setup 後 Bot 功能解鎖

---

## Phase 7 — Schema-per-tenant（可選，流量超過 20 公會後評估）

> 最後執行，需完整測試環境驗證 Cross-tenant 隔離。

### 7-0. Catalog 效能注意事項

800 公會規模下 PG Catalog 無顯著效能問題，但需遵守：

- **禁用 `information_schema`**：改用 `pg_catalog.pg_namespace` 直查（少 3–5 層 JOIN）
- **BackgroundService 查詢強制加 `is_dormant = false`**：避免巡檢冷 Schema
- **不做物理歸檔（DROP Schema）**：冷 Schema 只佔 `pg_namespace` 一行，無資料頁，歸檔代價 > 效益

### 7-1. DbContext 加入 search_path 切換

### 7-2. TenantMigrationService BackgroundService

（詳見 high-spec 報告）

### 7-3. UnitOfWorkMiddleware.ResetContextAsync 填入實作

將 Phase 2 預留的 no-op 實作為 `SET search_path TO public`

### 驗收條件

- [ ] 公會 A 的請求無法讀到公會 B 的資料（整合測試）
- [ ] Migration 失敗可完整 rollback
- [ ] `information_schema` 查詢效能無明顯退化

---

## 附：多公會通知投遞的規模化（MQ）

多公會後，對外 Discord 通知量隨**公會數**成長（每公會截止/每日通知各自發、發到各自頻道）→ 撞 Discord API rate limit。此時在**既有 outbox 之後**接一層 message queue（Redis Streams 起步）：consumer 依 rate limit 受控投遞、429 backoff 重試、毒訊息進 DLQ。

**單公會時不需要**（訊息量不隨玩家數成長，只發一則頻道公告）——這是「MQ 第一個真需求」的觸發點。見 `plans/2026-07-25-message-queue.md`。

---

## 執行順序總覽

```
Phase 1（基礎建設）
  ├─ Phase 2（安全修補）← 可並行
  └─ Phase 3（快取抽象）
       └─ Phase 4（Bot 多公會）
            ├─ Phase 5（OAuth2 優化）
            └─ Phase 6（Web Admin Panel）
                 └─ Phase 7（Schema-per-tenant）← 依流量決定是否執行
```
