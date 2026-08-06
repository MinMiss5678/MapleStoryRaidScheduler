# 架構設計文件 — MapleStory Raid Scheduler

本文件說明系統的整體架構、關鍵設計決策與實作細節，適合快速了解系統設計思路。

---

## 系統架構總覽

### 高階架構圖

```mermaid
graph TD
    User["玩家 (Player)"] -->|HTTPS / TLS 終結| Cloudflare["Cloudflare Edge"]
    Cloudflare -->|Tunnel HTTP + X-Forwarded-*| Cloudflared["cloudflared"]

    subgraph "Docker 容器環境"
        Cloudflared -->|HTTP| Frontend["Next.js 15 前端"]
        Frontend -->|REST API| Backend["ASP.NET Core Web API"]
        Backend --> Middleware["Middleware 管線\n(ExceptionHandler / Idempotency / Auth / RateLimiter / UnitOfWork)"]
        Middleware --> Application["Application Layer\n(DTOs, Interfaces, CQRS-Lite)"]
        Application --> Domain["Domain Layer\n(Entities, Repository Interfaces)"]
        Infrastructure["Infrastructure Layer\n(Dapper, Discord, Background Jobs)"] --> Domain

        Infrastructure --> DB[("PostgreSQL 18")]
        Infrastructure --> Redis[("Redis\n(跨 pod 共享：重複提交去重 / 限流計數 / session 快取)")]
        Infrastructure --> DiscordBot["Discord Bot (DSharpPlus)"]
        Infrastructure --> Seq["Seq (結構化日誌)"]
    end

    DiscordBot -->|Bot 通知| DiscordChannel["Discord 頻道"]
    User -->|查看通知| DiscordChannel
    User -->|OAuth2 登入| DiscordOAuth["Discord OAuth2"]
    DiscordOAuth -->|授權 code| Backend
```

### 分層架構與依賴方向

```
Presentation.WebApi  →  Application  →  Domain
                              ↓
                       Infrastructure
```

| 層級 | 職責 | 設計原則 |
|---|---|---|
| **Domain** | 核心實體、Repository 介面、業務規則 | 零外部依賴，可獨立單元測試 |
| **Application** | DTOs、服務介面、查詢介面 | 定義業務邊界，不含實作細節 |
| **Infrastructure** | Dapper Repository、Discord 整合、背景作業 | 實作所有 I/O，依賴注入替換 |
| **Presentation.WebApi** | Controller、Middleware 管線 | 薄控制器，業務邏輯不在此層 |

---

## 關鍵設計決策

### 1. 不使用 EF Core（Runtime），改用 Dapper + 自製 SqlBuilder

**決策原因**：Runtime 刻意不用 EF Core，改用 Dapper——目的是練習無模型（model-less）的資料存取方式，不是規模考量（EF Core 對此規模其實更省事）。手寫 SQL 的字串散落問題，用自製 SqlBuilder（Expression Tree 解析 Lambda 產生型別安全 SQL）解決。

> **注意**：`Infrastructure/Migrations/MigrationDbContext` 僅供 design-time 使用（詳見第 6 點），不注冊到 DI，不影響 runtime 行為。

**實作方式**：自製 `Utils/SqlBuilder/`，以 C# Expression Tree 解析 Lambda 表達式為 SQL 條件：

```csharp
// 型別安全，編譯期檢查欄位名稱
Sql.Query<CharacterDbModel>()
   .Where(c => c.DiscordId == discordId && c.Job != null)
   .Select(c => new { c.Id, c.Name, c.Job })
   .Build();
```

| 類別 | 功能 |
|---|---|
| `QueryBuilder` / `TypedQueryBuilder` | SELECT 查詢，支援 Lambda 選欄 |
| `InsertBuilder` / `UpdateBuilder` / `DeleteBuilder` | 寫入操作 |
| `CteBuilder` | CTE（WITH 子句）建構 |
| `SqlConditionGroup` | AND/OR 條件群組組合 |
| `SqlExpressionVisitor` | 解析 Lambda 為 SQL，支援 NULL 比較 |

### 2. CQRS-Lite 讀寫分離

**決策原因**：讀取與寫入的**模型不同**——寫入用 entity + `UnitOfWork` 事務保護與業務驗證，讀取需要跨表 JOIN 回**專用 DTO**、不受寫入模型約束。分開讓讀側能自由最佳化查詢。

> 這是 CQRS 的**核心原則**（讀寫責任分離），但本專案是**同一資料庫、強一致的輕量版（CQRS-Lite）**，非「獨立讀庫 + 物化視圖 + 事件溯源 + 最終一致」的完整型 CQRS。停在輕量版對此規模是刻意取捨。

**實作方式**：
- **寫入 (Command)**：`Application/Interface/` 定義介面 → `Infrastructure/Services/` 實作，走 `UnitOfWork` 事務。
- **讀取 (Query)**：`Application/Queries/` 定義介面 → `Infrastructure/Query/` 實作，直接執行 JOIN SQL，不走事務。

```
寫入路徑：Controller → IXxxService → IRepository → DB（UnitOfWork 包裹）
讀取路徑：Controller → IXxxQuery  → QueryBuilder → DB（無事務）
```

### 3. Unit of Work 模式

**決策原因**：一個 HTTP 請求可能涉及多個 Repository 操作，需保證原子性。

**實作方式**：`UnitOfWorkMiddleware` 在請求進入前開啟 `NpgsqlConnection` + `NpgsqlTransaction`，成功後 commit，例外時 rollback。所有 Repository 共享同一連線。

```csharp
// UnitOfWorkMiddleware.cs
await _unitOfWork.BeginTransactionAsync();
await next(context);
await _unitOfWork.CommitAsync();
```

**連線池 headroom**：目前 Npgsql `Maximum Pool Size`（預設 100）與 Postgres `max_connections`（預設 100）之間沒有預留 headroom。負載測試（`plans/2026-07-28-load-testing.md` Phase 1）確認：backend 自己的請求不會因此失敗——超過 pool 上限的請求在 client 端排隊等連線，只是變慢；真正會被拒絕的是**繞過這個 pool 的外部新連線**（`psql`、migration job 等直接連 Postgres 的操作），backend pool 佔滿 `max_connections` 額度時這些連線會直接收到 `FATAL: sorry, too many clients already`。連線閒置要等 `Connection Idle Lifetime`（預設 300 秒）才會被收回，短時間內連續操作容易疊加占用。正式環境部署前應為 Postgres `max_connections` 保留 headroom（backend pool + migrate + 其他服務 + 保留量 < max_connections），或明確設定 Npgsql `Maximum Pool Size` 上限／縮短 `Connection Idle Lifetime`。

### 4. 雙軌身分驗證

**決策原因**：一般玩家與管理員的驗證需求不同——玩家走 Discord OAuth2 取得 JWT（無狀態），管理員需要更嚴格的 Session 控管（可強制登出）。

**實作方式**：`AuthenticationMiddleware` 統一入口，依 Token 類型分流：

| 類型 | 機制 | 儲存位置 |
|---|---|---|
| 一般玩家 | 自定義 JWT | 客戶端 Cookie |
| 管理員 | SessionId | DB `session` 表 |

Discord 身分組 → 系統角色的對應由 `DiscordRoleMapping` 表管理，可動態調整。

**Session 快取（跨 pod 撤銷）**：管理員 session 讀取走快取，存 **Redis**（`ISessionCache` / `RedisSessionCache`）而非 per-pod `IMemoryCache`——所以 `DeleteAsync` / `DeleteByDiscordAsync` 撤銷（登出、拔身分組、踢人）**一次刪除即在所有 pod 立即生效**，不再是「只清當下 pod、其他 pod 等 TTL」。**讀**穿快取 miss 退回查 DB 自癒；Redis 不可用時 fail-open（退回查 DB，DB 為真實來源）。

> 上面「OAuth2 認證流程」那張圖只到**登入當下**（拿到 SessionId/JWT 為止；`SessionService.CreateAsync` 只寫 DB，不碰快取）。下圖補的是**登入之後、每次帶 SessionId 打 API** 的驗證流程，含跨行程撤銷——這段是 cache-aside + 跨行程失效，純文字條列比較難一眼看出時序，用圖比較清楚：

```mermaid
sequenceDiagram
    participant Admin as 管理員瀏覽器
    participant MW as AuthenticationMiddleware（API 行程）
    participant Cache as Redis（session:{discordId}）
    participant DB as Postgres（session 表，真實來源）
    participant Bot as Bot（另一個獨立行程）
    participant Discord as Discord Gateway

    Note over Admin,DB: 正常請求：cache-aside，miss 才退回查 DB
    Admin->>MW: 帶 SessionId 打 API
    MW->>Cache: GetAsync(discordId)
    alt cache 命中且未過期
        Cache-->>MW: Session
    else cache miss（或 Redis 不可用，fail-open 當 miss）
        MW->>DB: 查 session 表
        DB-->>MW: Session（查無 → 403 + 清 cookie）
        MW->>Cache: SetAsync(discordId, session, 短 TTL)
    end
    Note over MW: 剩餘效期 < 門檻才 sliding 延展（寫 DB + 回填 cache）；純讀命中不寫，不會每讀必寫
    MW-->>Admin: 通過驗證，放行

    Note over Bot,Cache: 跨行程撤銷：bot 收到角色異動，API 行程的 cache 立即失效
    Discord->>Bot: MemberUpdated / MemberRemoved（拔身分組、踢人）
    Bot->>DB: DeleteByDiscordAsync：刪 session 表
    Bot->>Cache: RemoveAsync(discordId)：刪共享 key（跟 API 是同一個 Redis，不是 bot 自己的副本）

    Admin->>MW: 下一次請求（還帶著舊 SessionId）
    MW->>Cache: GetAsync(discordId)
    Cache-->>MW: miss（已被 bot 刪除）
    MW->>DB: 查 session 表
    DB-->>MW: null（已被 bot 刪除）
    MW-->>Admin: 403（session 查無）+ 清 cookie
```

### 5. 重複提交防護（非完整冪等）

**決策原因**：前端連點或網路重送可能造成重複寫入（如重複報名、重複補位）。

**實作方式**：POST/PUT/DELETE 必須帶合法 UUID 的 `X-Idempotency-Key`（缺少或非 UUID 回 400）；`IdempotencyMiddleware` 以此 Key 去重，同一 Key 在 60 秒內重送直接回 **409 Conflict**、不進入業務邏輯。去重狀態存 **Redis**（`SET NX EX`，跨 pod 共享；經 `IIdempotencyStore` 抽象）——取代原本 per-pod 的 `IMemoryCache`。Redis 不可用時採 **fail-open**（放行 + 記 log，不因去重快取抖動擋掉寫入；真正的重複由報名 `ExistAsync` + auto-assign advisory lock 兜底）。

> 注意：這是「擋重送」而非完整冪等——重試不會重播第一次的回應內容（只快取一個標記，非結果）。真冪等需快取並重播原始回應；此處刻意簡化為 de-dup，因寫入操作重送應由使用者感知（409）而非默默視為成功。

### 6. Schema 版本管理（golang-migrate）

**決策原因**：手寫 SQL 無 ORM migration 機制，多環境（dev / prod）的 schema 需要明確的版本追蹤與 rollback 能力，確保環境一致性與部署安全。

**實作方式**：`db/migrations/` 存放有序號的 up/down SQL 檔，migrate service 在 backend 啟動前執行。

```
db/migrations/
  000001_init_schema.up.sql    ← 建立全部資料表與索引
  000001_init_schema.down.sql  ← DROP 全部資料表（反向 FK 順序）
  000002_xxx.up.sql            ← 後續 schema 變更
  000002_xxx.down.sql
```

**新增 Migration 工作流程**：

```bash
# 1. 修改 Infrastructure/Entities/*DbModel.cs

# 2. 執行草稿產生器（自動完成 ef diff → SQL 輸出 → 清理 .cs）
bash db/create-migration.sh <MigrationName>

# 3. 審閱並補齊產出的 SQL 草稿：
#    db/migrations/000002_<name>.up.sql   ← 補 FK constraints / indexes
#    db/migrations/000002_<name>.down.sql ← 確認 rollback 正確性
```

`db/create-migration.sh` 流程說明（Zero DB、Zero State File）：
- `ef migrations add <Name>` → rebuild 專案、產生 C# diff、更新 Snapshot
- `ef migrations script 0 <new>` → up SQL 草稿
- `ef migrations script <new> 0` → down SQL 草稿
- 過濾 `__EFMigrationsHistory`、`START TRANSACTION`、`COMMIT`
- 刪除 .cs migration 檔（保留 Snapshot 作為下次 diff 基線）

> **為何 `from=0` 可行**：`ef migrations add` 強制 rebuild，新 DLL 只含剛加入的 migration（舊 .cs 已刪），`script 0 NewMig` 因此只產生該次的增量 SQL。此行為依賴 assembly-based（非 file-based）的 migration 掃描機制。

> **版本鎖定要求**：EF Core 的 SQL 生成行為依賴具體版本。升版前需重新驗證 script 輸出。目前鎖定版本：`Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4`、`Microsoft.EntityFrameworkCore.Design 9.0.4`。

Migration image 以 `db/Dockerfile.migrate` 自製（golang-migrate 基底 + SQL 烤入），新增 migration 只需 rebuild image，k8s YAML 不需改動。

**Rollback 策略**：

| 情境 | 做法 |
|---|---|
| 本機開發 reset | `migrate down 1` → `migrate up` |
| Production 出問題 | PostgreSQL PITR / DB snapshot restore |
| Schema 寫錯 | 補一版 forward fix migration（`000003_fix_xxx.up.sql`） |

`down.sql` 是開發工具，不是 production rollback 機制。Production rollback 依賴 infrastructure 層（DB snapshot），而非應用層 migration。

### 7. 設定變更的可靠投遞（Transactional Outbox）

**決策原因**：設定變更（報名截止）後要喚醒背景 job 重算。原本用 `DbContext.AfterCommit`（in-process post-commit hook），有兩個問題：(1) commit 後、跑動作前 crash 會**遺失**；(2) 設定在 **API** 改、要喚醒的 `RegistrationDeadlineJob` 在 **bot**，in-process 事件**跨不了行程**（API 的 `ConfigChangeNotifier` 無訂閱者 = no-op）→ API 改設定不會即時通知 bot。

**實作方式**：改用 transactional outbox（`OutboxMessage` 表）。

| 端 | 元件 | 做法 |
|---|---|---|
| 寫入 | `IOutbox` / `Outbox` | 用**當前 UoW 的交易** INSERT outbox 列 → 與業務資料**原子提交/rollback**（無鬼影事件） |
| 派發 | `OutboxDispatcher`（跑 bot、自開專屬連線） | 輪詢已提交列 → 依 `Type` 派 `IOutboxHandler` → 標 `ProcessedAt` |

寫入端（API）跟派發端（bot）是**兩個獨立行程**，中間沒有直接呼叫，只靠共享 DB 的已提交列銜接——下圖同時畫出正常路徑，跟「handler 執行完、commit 前中斷」的 crash 重送路徑（`OutboxIntegrationTests.CrashBeforeCommit_...` 實測過的情境，不是只推論）：

```mermaid
sequenceDiagram
    participant API as API（SystemConfigService）
    participant DB as Postgres（OutboxMessage）
    participant Bot as Bot（OutboxDispatcher，另一個行程）
    participant H as IOutboxHandler

    Note over API,DB: 寫入端：跟業務資料同一個 UoW 交易
    API->>DB: UPDATE SystemConfig
    API->>DB: INSERT OutboxMessage(Type=ConfigChanged, ProcessedAt=NULL)
    API->>DB: COMMIT（兩者原子生效；rollback 則兩者都不留痕跡）

    Note over Bot,DB: 派發端：獨立行程、專屬連線，事後輪詢（非即時推送）
    Bot->>DB: SELECT ... WHERE ProcessedAt IS NULL FOR UPDATE SKIP LOCKED
    DB-->>Bot: 撈到該列（開自己的交易）

    alt 正常路徑
        Bot->>H: HandleAsync(payload)
        H-->>Bot: 成功
        Bot->>DB: UPDATE SET ProcessedAt = now()
        Bot->>DB: COMMIT
    else crash 重送（已用整合測試驗證的路徑）
        Bot->>H: HandleAsync(payload)
        H-->>Bot: 成功（副作用已發生）
        Note over Bot: 崩潰／連線中斷，commit 前中斷
        Bot--xDB: 沒有 COMMIT，該列仍是 ProcessedAt=NULL
        Note over Bot,DB: 重啟後，下一輪輪詢
        Bot->>DB: SELECT ... FOR UPDATE SKIP LOCKED（同一列，因為還沒 processed）
        Bot->>H: HandleAsync(payload)（重送，靠 handler 冪等吸收）
        H-->>Bot: 成功
        Bot->>DB: UPDATE SET ProcessedAt = now()
        Bot->>DB: COMMIT（這次才真的標記完成）
    end
```

- **多 pod 分工**：`SELECT ... FOR UPDATE SKIP LOCKED` → 多個 dispatcher 各撈不相交批、互不重投、免選 leader。
- **at-least-once + 冪等**：投遞成功、標 processed 前崩 → 重送；靠 handler 冪等（`ConfigChanged` 只是喚醒 job 重讀）吸收。
- partial index（`WHERE "ProcessedAt" IS NULL`）撈取快；重試上限後放棄、記 `LastError`。
- **保留列清理**：`OutboxDispatcher` 只標 `ProcessedAt`、從不刪列，`OutboxMessage` 會無限成長。`OutboxRetentionJob`（同樣跑 bot、自開專屬連線）每 24 小時清一次「已處理超過 30 天」的列；未處理列不管多舊都不動（還沒投遞完，刪了就真的遺失事件）。

> outbox 把「要送什麼」持久化進交易 → 解掉 AfterCommit 的 crash-loss 與跨行程限制。定位為 readiness（現況 replicas=1、副作用可補），但順帶修掉跨行程 gap。

---

### 8. 可觀測性（日誌 + 錯誤追蹤 + 送第三方前的隱私保護）

日誌走 **Serilog**，兩個 sink 並存、職責分開（`Presentation.WebApi/Program.cs` 的 `UseSerilog`；bot 端 `Presentation/Program.cs` 同套）：

- **Seq（內部結構化日誌）**：`.WriteTo.Seq(Seq:ServerUrl)`，收全部 log，內部除錯用，明碼保留（Seq 是內網服務）。
- **Sentry（錯誤追蹤）**：`.WriteTo.Sentry(...)`，**只在 `Sentry:Dsn` 有設定且 `IsProduction()` 時才掛**（本機開發/沒設 DSN 就不掛，缺設定不噴例外）。

**送到第三方（Sentry）前的隱私保護**——因為 Sentry 是外部服務，`SetBeforeSend` 統一在單一關卡把敏感資料擋掉，不必逐一改每個 log 呼叫點：

- **DiscordId → HMAC-SHA256**：DiscordId 是間接識別個人的資料，不明碼送出。有設 `Sentry:DiscordIdHashKey` 就雜湊成 `discord_id_hash` tag（HMAC 而非純 SHA256——snowflake 非高熵，純 SHA256 可列舉反推，HMAC 沒密鑰連候選值都算不出）；沒設密鑰就不送這個 tag。無論如何原本的 `Extra["DiscordId"]` 明碼一律換成 `[Filtered]`。
- **`ScrubSensitive` 掃三個獨立欄位**：例外訊息（`SentryExceptions[].Value`，如 Npgsql 連線失敗會夾連線字串）、渲染後的 log 訊息（`Message.Formatted`）、breadcrumb 訊息——三者是分開的欄位，只掃一個會漏。pattern 見 `Program.cs` 的 `ScrubSensitive`（密碼、JWT、OAuth token 等）。
- **breadcrumb 門檻**：`MinimumBreadcrumbLevel = Warning`。

> 相關 secret（皆選填，`optional: true`）：`sentry_dsn`（沒設就不掛 Sentry）、`discord_hash_key`（沒設就不送 DiscordId 雜湊 tag）。設計沿革見 `plans/2026-08-03-bugsink-integration.md`、`plans/2026-08-04-sentry-discordid-hmac.md`。

---

## Middleware 管線

請求進入後依序經過：

```
Request
  │
  ▼
ExceptionHandlerMiddleware   ← 全域例外捕捉，統一回傳 ProblemDetails
  │
  ▼
IdempotencyMiddleware        ← 強制 X-Idempotency-Key（否則 400），重送回 409 擋重複提交
  │
  ▼
AuthenticationMiddleware     ← 驗證 JWT（玩家）或 SessionId（管理員），設 discordId claim
  │
  ▼
RateLimiter                  ← 登入後按 discordId 限流（100/10s/人，計數存 Redis 固定視窗故跨 pod 共用上限；Redis 掛則 fail-open 放行）；放此處故被擋的請求不白開 DB 交易
  │
  ▼
UnitOfWorkMiddleware         ← 開啟 DB 事務，成功 Commit，例外 Rollback
  │
  ▼
Controller / Service
```

> **限流為何按身分不按 IP**：`discordId` 來自已驗證的 session/JWT，client 偽造不了，換 IP 也繞不掉；且前端 proxy 會剝除 `X-Forwarded-For`／`cf-connecting-ip`，後端本就看不到真實 IP。未登入端點（登入前暴力破解）不在此限——那需 IP／CAPTCHA／帳號鎖定，屬另案。

> **健康檢查端點例外**：`/health/live`、`/health/ready` 以 `UseHealthChecks` **終端中介軟體**掛在這條管線**之前**，完全繞過後面所有中介軟體。
> 原因：探針不該需要認證、也不該開交易；readiness 的 DB 檢查走專屬 `DatabaseHealthCheck`（不經請求管線）→ DB 掛掉乾淨回 503，liveness 不查 DB 故不誤殺 pod。詳見 `docs/cd-deploy-setup.md`。

### 請求生命週期（交易邊界）

上面是「管線順序」；下圖是「一個請求的時間流」，重點在 **UnitOfWork 的交易邊界**與 **Dapper 沿用同一連線／交易**：

```mermaid
sequenceDiagram
    participant C as Client
    participant MW as Middleware<br/>(Exception→Idempotency→Auth→RateLimiter)
    participant UoW as UnitOfWorkMiddleware
    participant Ctrl as Controller / Service
    participant Repo as Repository (Dapper)
    participant DB as PostgreSQL

    C->>MW: HTTP 請求
    Note over MW: 任一中介軟體擋下即短路回應<br/>（不進 UoW、不開交易）
    MW->>UoW: 通過驗證 / 限流
    alt 寫入（POST/PUT/PATCH/DELETE）
        UoW->>DB: BeginTransaction
        UoW->>Ctrl: next()
        Ctrl->>Repo: 呼叫 Repository
        Repo->>DB: Dapper 執行 SQL（同一連線／交易）
        DB-->>Repo: 結果
        Repo-->>Ctrl: 物化為實體
        Ctrl-->>UoW: 回應（設定 status code）
        alt status < 400
            UoW->>DB: Commit
        else status >= 400 或拋例外
            UoW->>DB: Rollback（例外再往外拋）
        end
    else 讀取（GET 等）
        UoW->>Ctrl: next()（不開交易）
        Ctrl->>Repo: Query
        Repo->>DB: Dapper 讀取（連線自動開／關）
    end
    UoW-->>C: HTTP 回應
```

> 交易邊界由中介軟體統一掌控，**Controller／Repository 不碰交易生命週期**——這是 Unit of Work 的核心：一個請求一個交易，成功才 Commit，任何失敗（4xx 或例外）整批 Rollback。詳見上方「關鍵設計決策 §3」。

---

## 自動分配引擎

這是系統最核心的業務邏輯，實際上是**兩條互相獨立的路徑**（不是同一個 `AutoAssignAsync` 被兩邊共用——這點很容易看錯，底下特別畫清楚）：

### 流程圖

```mermaid
flowchart TD
    A[玩家報名] --> B["TeamSlotAutoAssignService.AutoAssignAsync\n（逐一角色處理）"]
    B --> C{找到符合時段\n且有空位的 auto 隊}
    C -->|是| D[加入現有隊伍 AddMember]
    C -->|否，且可用時段非空| E[建立新隊 Source=auto]
    D --> F["TeamSlotMergeService.MergeTeamsAsync\n（同 boss 有 ≥2 個未滿 auto 隊才嘗試合併）"]
    E --> F
    F --> Fend[分配完成]

    G[管理員選定 boss+範本，觸發批次重排] --> H["ScheduleService.AutoScheduleWithTemplateAsync\n（完全獨立的演算法，不呼叫 AutoAssignAsync/MergeTeamsAsync）"]
    H --> I["保留隊 = Source=admin 的隊 或 含 IsManual 成員的 auto 隊\n→ 整隊保留，FillTeamFromPool 只補空位"]
    I --> J["其餘報名者依時段+場次分組，\n嚴格依 BossTemplateRequirement 優先序貪婪組新隊"]
    J --> K["回傳保留隊（正 Id）+ 新隊預覽（負 Id）\n存檔時才真的 CREATE"]
```

### 補位保護機制

手動補位的成員標記 `IsManual = true`，批次重新分配時跳過含 `IsManual` 成員的隊伍，防止人工調整被覆蓋。

### 併發控制（避免重複開隊）

`AutoAssignAsync` 的「讀現有隊 → 沒有就開新隊」是 **read-then-write**。兩人同時報名同一 period 時，在 READ COMMITTED 下兩交易互相看不到對方未提交的隊 → **各自開一隊 → 重複**（`MergeTeamsAsync` 只能事後補救，併發當下補不了）。

解法：`AutoAssignAsync` 開頭對 `(classId, periodId)` 取 **交易級 advisory lock**（`pg_advisory_xact_lock`，見 `IRegistrationLock`）：

- 同一 period 的自動分配**序列化** → 第二個看得到第一個建的隊
- **不同 period 不互斥**（並行保留，per-key 粒度）
- 交易級鎖隨 UoW commit/rollback **自動釋放**；鎖在 DB → **多 pod 安全**（不像 C# 程序內鎖，多副本會失效）

> 選型：本規模用 advisory lock（targeted + 多 pod 安全 + 一行 SQL）即可；SERIALIZABLE 需重試迴圈、MQ 需常駐 broker，對一團數十人過重。公開路人配對、熱門本高併發 + 尖峰時，才會走「非同步配對管線（佇列 + worker）」重設計。

### Source 語意（provenance）

隊伍來源，取代舊 `IsTemporary`/`IsPublished` 兩布林。驅動：空隊自動清除、合併資格、批次重排保留。

| 值 | 來源 | 說明 |
|---|---|---|
| `auto` | `TeamSlotAutoAssignService` | 玩家報名時系統自動建立，空時可自動清除、可被合併 |
| `admin` | Admin 手動開團 / `ScheduleService` 批次重排 | Admin 建立，空時不自動刪除、不被自動合併、重排時保留 |

---

## TeamSlot 編輯併發控制

編輯既有隊伍（`TeamSlotService.UpdateAsync`，管理員排團存檔 / 玩家補位共用這條路徑）跟上面的自動分配是**不同的併發問題、不同的鎖**：自動分配鎖的是「同一 period 讀現有隊 → 開新隊」的 race；這裡鎖的是「同一隊伍同時被兩個請求編輯」的 race（容量競爭、以及一邊清空觸發連帶砍團、另一邊還在對同一隊寫入）。兩把鎖用不同 `classId`（1001 / 1002），互不影響、可同時持有。

兩階段合起來的完整時序（悲觀鎖序列化 + 拿到鎖後樂觀鎖檢查的三種分支）：

```mermaid
sequenceDiagram
    participant A as 請求 A（先到）
    participant B as 請求 B（後到，同一 teamSlotId）
    participant Lock as advisory lock<br/>(classId=1002, teamSlotId)
    participant DB as TeamSlot / TeamSlotCharacter

    A->>Lock: pg_advisory_xact_lock(1002, 5)
    Lock-->>A: 取得鎖
    B->>Lock: pg_advisory_xact_lock(1002, 5)
    Note over B,Lock: B 卡住等待（同一隊伍序列化，不同隊伍不互擋）

    A->>DB: GetByIdAsync(5)（讀「當下真相」）
    DB-->>A: 隊伍存在，成員 X（version=v1）
    A->>DB: 移除成員 X（隊上其他人都是空位 → 連帶砍團）
    A->>DB: COMMIT（鎖隨交易自動釋放）

    Lock-->>B: 取得鎖（A 已釋放）
    B->>DB: GetByIdAsync(5)（重新讀，不是 B 請求開始時的舊快照）

    alt 隊伍已消失（被 A 連帶砍團）
        DB-->>B: null
        B->>B: 略過此隊，加入 ConflictedTeamSlotIds
    else 隊伍還在，但成員 X 版本不對（xmin ≠ v1，被別的流程動過）
        DB-->>B: TeamSlot（成員 X 已是新版本）
        B->>DB: UPDATE ... WHERE Id=X AND xmin=v1
        DB-->>B: 0 rows affected
        B->>B: 加入 ConflictedTeamSlotIds
    else 隊伍還在、版本也對（沒衝突）
        DB-->>B: TeamSlot（成員 X version=v1 仍符合）
        B->>DB: 正常寫入成功
    end

    B->>DB: COMMIT（不論這隊有沒有衝突，其他隊伍照常處理、不中斷）
```

### Phase A：悲觀鎖（序列化同隊編輯）

`UpdateAsync` 處理既有隊伍前，先對 `(classId=1002, teamSlotId)` 取交易級 `pg_advisory_xact_lock`（`IRegistrationLock.AcquireTeamSlotEditLockAsync`）：

- 同一隊伍的併發編輯**序列化**，第二個請求會等第一個 commit/rollback 才繼續，此時重新讀到的是「當下真相」，不是自己請求開始時的舊快照
- 不同隊伍的鎖互不阻塞，可並行
- 擋住兩類 TOCTOU race：①容量檢查看到的空位數，跟真正寫入時的空位數不一致（超編）②一邊把隊伍最後一人移除觸發連帶砍團，另一邊對同一個（已消失）`teamSlotId` 寫入撞外鍵違反

### Phase B：樂觀鎖 + 統一衝突回報

拿到鎖之後，`UpdateAsync` 用鎖內重新讀到的 `TeamSlot`（而非請求帶來的舊資料）去做兩件事：

1. **隊伍是否還存在**：`GetByIdAsync` 查無 → 隊伍已被別的流程砍掉（merge / 連帶清團），略過此隊、不拋例外中斷其他隊伍的處理
2. **既有成員是否被別人動過**：`TeamSlotCharacterRepository.UpdateAsync` 的 `WHERE` 子句比對 `xmin = @version`（`TeamSlotCharacter.Version`）；對不上（含 row 已被刪、根本查無此 Id）→ 回傳 `false`

兩種情況（隊伍消失 / 版本衝突）**統一收進 `TeamSlotUpdateResult.ConflictedTeamSlotIds`**，不是分別丟不同例外——呼叫端（前端）只需要處理一份「這些隊被略過」的清單。管理員排團頁收到後，衝突的隊伍**原地標紅、不重新排序**，不假裝存檔成功。

> 選型同自動分配鎖：本規模用 advisory lock + xmin 即可，不需要 SERIALIZABLE 重試迴圈或分散式鎖服務。

### lock_timeout 安全邊際

`RegistrationLock` 取鎖前對交易 `SET LOCAL lock_timeout`（`RegistrationLock.cs:47`），預設 5 秒，用來區分「正常排隊等一下」跟「持鎖方異常卡死」：逾時拋 `AdvisoryLockTimeoutException`，`TeamSlotService` 接住後歸類進 `ConflictedTeamSlotIds`（跟版本衝突同一份清單，UI 上不分辨）。

負載測試（`plans/2026-07-28-load-testing.md` Phase 2）對同一 `teamSlotId` 灌到 500 併發編輯（遠超實際使用規模，一支隊正常 6-8 人）驗證：`lock_timeout` 從未誤觸發，即使總延遲飆到 17 秒。原因是 `SET LOCAL lock_timeout` 只計算已進入交易、卡在 `pg_advisory_xact_lock` 本身的等待時間；client 端觀察到的延遲大部分花在進交易之前（Kestrel 請求排隊、等 Npgsql 連線池釋出連線），不算進這個逾時。5 秒預設值在連線池被打爆之前有充足安全邊際；真正的瓶頸是連線池，不是這個 timeout 太短（見本文件「Unit of Work 模式」節的連線池 headroom）。

---

## 領域設計

### 核心實體

```mermaid
classDiagram
    class Player {
        +ulong DiscordId
        +string Name
        +string Role
    }
    class Character {
        +string Id
        +ulong DiscordId
        +string Name
        +string Job
        +int AttackPower
    }
    class Boss {
        +int Id
        +string Name
        +int RequireMembers
        +int RoundConsumption
    }
    class Period {
        +int Id
        +DateTimeOffset StartDate
        +DateTimeOffset EndDate
    }
    class Register {
        +int Id
        +ulong DiscordId
        +int PeriodId
        +List~CharacterRegister~ CharacterRegisters
        +List~PlayerAvailability~ Availabilities
    }
    class CharacterRegister {
        +int? Id
        +int PlayerRegisterId
        +string CharacterId
        +int BossId
        +int Rounds
    }
    class TeamSlot {
        +int Id
        +int BossId
        +DateTimeOffset SlotDateTime
        +string Source
        +int? TemplateId
        +int Capacity
        +int FilledCount
        +bool HasRoom
        +Contains(characterId) bool
        +AddMember(member)
        +SetRoster(roster, dateTime)
        +AbsorbMembers(members, dateTime)
    }
    class TeamSlotCharacter {
        +int? Id
        +int TeamSlotId
        +ulong DiscordId
        +string DiscordName
        +string CharacterId
        +string CharacterName
        +string Job
        +int AttackPower
        +int Level
        +int Rounds
        +bool IsManual
        +string Version
    }
    class PlayerAvailability {
        +int Id
        +int PlayerRegisterId
        +int Weekday
        +TimeOnly StartTime
        +TimeOnly EndTime
    }
    class BossTemplate {
        +int Id
        +int BossId
        +string Name
        +List~BossTemplateRequirement~ Requirements
    }
    class BossTemplateRequirement {
        +int Id
        +int BossTemplateId
        +string JobCategory
        +int Count
        +int Priority
    }
    class JobCategory {
        +string CategoryName
        +string JobName
    }

    Player "1" -- "*" Character : 擁有多個
    Player "1" -- "0..1" Register : 登記時段
    Register "*" -- "1" Period : 屬於特定週期
    Register "1" -- "*" CharacterRegister : 登記具體角色與王
    CharacterRegister "*" -- "1" Boss : 關聯副本
    CharacterRegister "*" -- "1" Character : 關聯角色
    TeamSlot "*" -- "1" Boss : 屬於特定副本
    TeamSlot "1" -- "*" TeamSlotCharacter : 包含多個成員
    Boss "1" -- "*" BossTemplate : 定義樣板
    BossTemplate "1" -- "*" BossTemplateRequirement : 包含需求
    TeamSlot "*" -- "0..1" BossTemplate : 基於樣板
```

> **TeamSlot 是充血聚合**：`Capacity`/`HasRoom`/`Contains`/`AddMember`/`SetRoster`/`AbsorbMembers` 統一守「不超員、不重複、不拆散手動成員」這組不變式，違反丟 `DomainException`（`ExceptionHandlerMiddleware` 映射 400）。只維護記憶體物件圖，持久化仍由 service 端命令式 Dapper 完成（無 change-tracking）。`TeamSlotCharacter.Version` 是 Postgres `xmin` 轉字串，供樂觀鎖比對（見下方「TeamSlot 編輯併發控制」）。

---

## 資料庫設計 (ERD)

使用 PostgreSQL 18，Dapper 手寫 SQL；schema 版本由 golang-migrate 管理（`db/migrations/`）。

```mermaid
erDiagram
    Player {
        bigint DiscordId PK
        string DiscordName
        string Role
    }

    PlayerRegister {
        int Id PK
        bigint DiscordId FK
        int PeriodId FK
    }

    PlayerAvailability {
        int Id PK
        int PlayerRegisterId FK
        int Weekday
        time StartTime
        time EndTime
    }

    CharacterRegister {
        int Id PK
        int PlayerRegisterId FK
        string CharacterId FK
        int BossId FK
        int Rounds
    }

    Character {
        string Id PK
        bigint DiscordId FK
        string Name
        string Job
        int AttackPower
    }

    Boss {
        int Id PK
        string Name
        int RequireMembers
        int RoundConsumption
    }

    BossTemplate {
        int Id PK
        int BossId FK
        string Name
    }

    BossTemplateRequirement {
        int Id PK
        int BossTemplateId FK
        string JobCategory
        int Count
        int Priority
    }

    Period {
        int Id PK
        timestamptz StartDate
        timestamptz EndDate
    }

    TeamSlot {
        int Id PK
        int BossId FK
        timestamptz SlotDateTime
        text Source
        int TemplateId FK
    }

    TeamSlotCharacter {
        int Id PK
        int TeamSlotId FK
        bigint DiscordId
        string DiscordName
        string CharacterId FK
        string CharacterName
        string Job
        int AttackPower
        int Rounds
        bool IsManual
    }

    JobCategory {
        string JobName PK
        string CategoryName
    }

    DiscordRoleMapping {
        bigint DiscordRoleId PK
        string Role
        int Priority
    }

    Session {
        string SessionId PK
        bigint DiscordId
        timestamptz SessionExpiry
    }

    SystemConfig {
        int Id PK
        int DeadlineDayOfWeek
        interval DeadlineTime
        bool IsDeadlineNotified
    }

    OutboxMessage {
        bigint Id PK
        text Type
        jsonb Payload
        timestamptz OccurredAt
        timestamptz ProcessedAt "NULL=待處理"
        int AttemptCount
        text LastError
    }

    Player ||--o{ PlayerRegister : ""
    Period ||--o{ PlayerRegister : ""
    PlayerRegister ||--o{ PlayerAvailability : ""
    PlayerRegister ||--o{ CharacterRegister : ""
    Character ||--o{ CharacterRegister : ""
    Boss ||--o{ CharacterRegister : ""
    Player ||--o{ Character : ""
    Boss ||--o{ BossTemplate : ""
    BossTemplate ||--o{ BossTemplateRequirement : ""
    Boss ||--o{ TeamSlot : ""
    BossTemplate ||--o{ TeamSlot : ""
    TeamSlot ||--o{ TeamSlotCharacter : ""
```
---

## Discord 整合

### OAuth2 認證流程

```mermaid
sequenceDiagram
    participant User as 玩家
    participant Frontend as Next.js
    participant API as ASP.NET Core
    participant Discord as Discord API

    User->>Frontend: 點擊「Discord 登入」
    Frontend->>Discord: 跳轉 OAuth2 授權頁
    Discord-->>Frontend: 回傳 code
    Frontend->>API: POST /api/Auth/Login { code }
    API->>Discord: POST /oauth2/token（code → access_token）
    Discord-->>API: access_token
    API->>Discord: GET /users/@me（用 access_token）
    Discord-->>API: 使用者 id / name
    API->>Discord: 查詢 Guild Member 身分組 (Bot Token)
    Discord-->>API: roles[]
    alt 身分組 = Admin
        API-->>Frontend: SessionId (DB Session)
    else 身分組 = User
        API-->>Frontend: JWT Token
    end
```

### Discord Bot 功能

| 功能 | 說明 |
|---|---|
| **每日提醒** | 背景作業每天掃描當日 `TeamSlot`，Bot 標記玩家提醒行程 |
| **截止提醒** | 報名截止日自動觸發，附上排團結果 URL |
| **身分組同步** | 登入時透過 Bot Token 查詢 Discord Guild Member，判斷 `Admin` / `User` |

---

## 主要服務一覽

| 服務 | 職責 |
|---|---|
| `RegisterService` | 玩家報名寫入，報名後觸發即時自動分配 |
| `TeamSlotAutoAssignService` | 自動分配核心：即時分配 + 批次重排 |
| `TeamSlotMergeService` | 合併零散隊伍，根據 BossTemplate 優化陣容 |
| `TeamSlotCharacterService` | 補位、移除成員，設定 `IsManual` 保護旗標 |
| `AuthAppService` | Discord OAuth2 流程、角色判斷、憑證核發 |
| `JwtService` / `SessionService` | JWT 核發驗證 / DB Session 管理 |
| `DiscordOAuthClient` | Discord REST API 呼叫（token 兌換、身分組查詢） |
| `ScheduleService` | 背景作業協調（每日提醒、截止提醒） |

---

## 部署

### Docker Compose

啟動順序由 health check 與 `depends_on` 控制：

```
database（healthcheck: pg_isready）
  ↓
migrate（golang-migrate up，完成後退出）
  ↓
backend / bot
  ↓
frontend → cloudflared
```

### Kubernetes

`k8s/` 目錄包含各服務的 Deployment / Service / PVC，以及：
- `k8s/migrate-job.yaml`：批次 Job，執行 migration 後完成
- `k8s/secrets.yaml`：Secret template（真實值不入 git）
- backend 掛 **liveness (`/health/live`) / readiness (`/health/ready`)** 探針：DB 掛時停止導流量但不重啟 pod；滾動更新時 readiness 沒綠的新 pod 不會接到流量。**其餘服務也各有探針**：database（`pg_isready`，liveness 容忍 exit 1）、redis（`redis-cli ping` / tcpSocket）、seq / frontend（HTTP `/health`）、cloudflared（`--metrics` 的 `/ready`）——分離 readiness/liveness 的通則同上（依賴掛掉時停導流量、不誤殺 pod）
- 所有服務都設 **resource requests/limits**（受限於單機 ~2GB 主機預算，數值偏保守）

Secrets 以 volume mount 方式掛載至 `/run/secrets/`，與 Docker secrets 路徑一致，應用程式設定無需因部署平台而異。

執行拓樸（namespace `maple-raid`）：

```mermaid
graph TD
    subgraph ns["k8s namespace: maple-raid"]
        mig[["migrate Job（執行後完成）"]]
        be["backend Deployment<br/>liveness / readiness 探針"]
        fe["frontend Deployment"]
        bot["bot Deployment"]
        db[("PostgreSQL<br/>Deployment + PVC")]
        redis[("Redis<br/>Deployment（無 PVC）")]
        seq["Seq 日誌"]
        cf["cloudflared Tunnel"]
        sec{{"Secret（掛載 /run/secrets）"}}
    end
    cf --> fe --> be --> db
    bot --> db
    be --> redis
    be --> seq
    bot --> seq
    mig -->|先於 backend 完成| db
    sec -. 掛載 .-> be
    sec -. 掛載 .-> bot
    sec -. 掛載 .-> db
```

- **CI**（GitHub Actions，`ci.yml`）：PR 綠燈才 merge，純文件變更自動跳過重量級 job。細節見下方流程圖 + `docs/e2e-testing-setup.md`。
- **CD**（GitHub Actions，`deploy.yml`，跟 CI 完全獨立）：`workflow_dispatch` 人工手動觸發，SHA 版本化映像 + Kustomize 滾動更新。細節見 `docs/cd-deploy-setup.md`。
- **手動部署**（不走 CI，本機直接操作）：`docs/deployment.md`（`deploy.ps1` / `rollout.ps1`）。

### CI/CD 流程（總覽）

CI 已遷 **GitHub Actions**（`.github/workflows/ci.yml` + `deploy.yml`），跟 `cd-deploy-setup` / `e2e-testing-setup` 兩份筆記對照：

```mermaid
flowchart LR
    dev["feature 分支"] -->|開 PR| gate["changes job 判斷改動範圍"]
    gate -->|純文件 docs/plans/*.md| skip["其餘 7 個必要 job 標 skipped\n（if 條件，非 paths-ignore）"]
    gate -->|動到程式碼| ci["format → build → unit/integration-test\n→ frontend-test(lint+test+build) → coverage → e2e"]
    skip --> checks{"required status checks\n（含 enforce_admins，admin 也不能繞過）"}
    ci -->|全綠| checks
    checks -->|通過| merge["squash merge 進 main"]
    merge --> mainci["main push 觸發同一份 ci.yml\n（post-merge 再驗一次，非 deploy 的一部分）"]
    merge -.->|另一條、需人工觸發| dispatch["workflow_dispatch：deploy.yml"]
    dispatch --> envgate["production Environment\n（可設 required reviewers）"]
    envgate --> deploy["推 SHA 版本化映像 minqq/*"]
    deploy --> mig["migrate Job"]
    mig --> k["Kustomize：kubectl apply -k"]
    k --> roll["k8s 滾動更新（readiness 綠才收舊 pod）"]
```

> **PR 閘**：`changes` job 用 `dorny/paths-filter` 判斷這次改動是否只碰文件；純文件時其餘 job 用 `if:` 條件跳過（標 skipped），不是在 `on:` 用 `paths-ignore`——後者會讓 workflow 整個不觸發，required status checks 永遠等不到回報、PR 會卡死。GitHub 把 skipped 視為通過，required checks 照樣滿足。
> **`main` 分支保護**：required status checks（上述 7 個 job）+ 禁止 force push/刪除 + **`enforce_admins` 開啟**（admin 帳號也一樣得走 PR，不能直接推）。
> **CI 跟 deploy 是兩條獨立流程**：push 進 `main` 只會重跑一次 `ci.yml`（post-merge 守門，不是 deploy 的前置步驟）；`deploy.yml` 是完全獨立、`workflow_dispatch` 手動觸發、掛 `production` Environment 當核准閘。映像以 git SHA 版本化 → 可追溯、可真 rollback（`kubectl rollout undo` 或指定 SHA）。