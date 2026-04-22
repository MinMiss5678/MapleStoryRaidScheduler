# SaaS 轉型執行計畫

**日期**：2026-04-18
**參考**：`plans/reports/2026-04-18-saas-architecture-evaluation-high-spec.md`

---

## Phase 1 — 基礎建設（阻塞後續所有 Phase）

> 不完成 Phase 1，任何多租戶功能都無法正確運作。

### 1-1. 資料庫：新增 GuildRegistration 表

```sql
-- db/migrations/000003_add_guild_registration.up.sql
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

### 1-2. 資料庫：SystemConfig、DiscordRoleMapping 加 guild_id

```sql
-- db/migrations/000003_add_guild_registration.up.sql（續）
ALTER TABLE "SystemConfig"       ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
ALTER TABLE "DiscordRoleMapping" ADD COLUMN guild_id BIGINT NOT NULL DEFAULT 0;
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

**Middleware 順序**（`Program.cs`）：

```csharp
app.UseMiddleware<AuthenticationMiddleware>();   // 先驗證，取得 guildId claim
app.UseMiddleware<TenantContextMiddleware>();    // 設定 search_path（no-op 直到 Phase 7）
app.UseMiddleware<UnitOfWorkMiddleware>();       // 寫入才開 transaction
```

### 2-2. OAuth2 Token 加密

**檔案**：`Infrastructure/Repositories/SessionRepository.cs:24-25`

- 新增 `ITokenEncryptionService`（Application 層介面）
- 實作 AES-256-GCM 加密（Infrastructure 層）
- `CreateAsync`、`UpdateAsync` 寫入前加密
- `GetAsync` 讀取後解密

### 驗收條件

- [ ] `TenantContextMiddleware` 註冊於 `AuthenticationMiddleware` 之後、`UnitOfWorkMiddleware` 之前
- [ ] Phase 2 階段 search_path 為 no-op（guildId claim 尚未存在），現有功能不受影響
- [ ] Session 表中 AccessToken/RefreshToken 欄位存的是密文
- [ ] 登入流程可正常解密並使用 Token

---

## Phase 3 — 快取抽象層

> 在 Bot 多公會支援前建立，確保 MemberUpdatedHandler 可以 invalidate 快取。

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

### 3-2. 實作 InProcessCache（MemoryCache）

**檔案**：`Infrastructure/Cache/InProcessCache.cs`

Key 格式強制加 `msr:` 前綴：`msr:{module}:{guildId}:{id}`

### 3-3. 替換現有 MemoryCache 直接使用

- `AuthService`：快取 `msr:roles:{guildId}:{discordId}`，TTL 5 分鐘
- `SystemConfigService`：快取 `msr:config:{guildId}`，TTL 10 分鐘

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
