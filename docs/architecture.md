# 架構設計文件 — MapleStory Raid Scheduler

本文件說明系統的整體架構、關鍵設計決策與實作細節，適合快速了解系統設計思路。

---

## 系統架構總覽

### 高階架構圖

```mermaid
graph TD
    User["玩家 (Player)"] -->|HTTPS| Frontend["Next.js 15 前端"]
    Frontend -->|REST API| Backend["ASP.NET Core Web API"]

    subgraph "Docker 容器環境"
        Backend --> Middleware["Middleware 管線\n(Auth / UnitOfWork / Idempotency / ExceptionHandler)"]
        Middleware --> Application["Application Layer\n(DTOs, Interfaces, CQRS-Lite)"]
        Application --> Domain["Domain Layer\n(Entities, Repository Interfaces)"]
        Domain --> Infrastructure["Infrastructure Layer\n(Dapper, Discord, Background Jobs)"]

        Infrastructure --> DB[("PostgreSQL 18")]
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

**決策原因**：刻意選擇手寫 SQL，目的是讓每條查詢的行為完全確定且可審計，同時作為自製 SqlBuilder 的技術展示——以 Expression Tree 解析 Lambda 表達式產生型別安全的 SQL，取代字串拼接。

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

**決策原因**：讀取與寫入的需求差異大——寫入需要事務保護與業務驗證，讀取需要最佳化 SQL 與多表 JOIN。混用同一 Repository 會導致讀取路徑被事務拖慢。

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

### 4. 雙軌身分驗證

**決策原因**：一般玩家與管理員的驗證需求不同——玩家走 Discord OAuth2 取得 JWT（無狀態），管理員需要更嚴格的 Session 控管（可強制登出）。

**實作方式**：`AuthenticationMiddleware` 統一入口，依 Token 類型分流：

| 類型 | 機制 | 儲存位置 |
|---|---|---|
| 一般玩家 | 自定義 JWT | 客戶端 Cookie |
| 管理員 | SessionId | DB `session` 表 |

Discord 身分組 → 系統角色的對應由 `DiscordRoleMapping` 表管理，可動態調整。

### 5. 冪等性保護

**決策原因**：前端重試或網路重送可能造成重複操作（如重複報名、重複補位）。

**實作方式**：所有 POST/PUT/DELETE 請求必須帶 `X-Idempotency-Key`，`IdempotencyMiddleware` 以此 Key 為快取鍵，相同 Key 的重複請求直接回傳快取結果，不重新執行業務邏輯。

### 6. Schema 版本管理（golang-migrate）

**決策原因**：手寫 SQL 無 ORM migration 機制，多環境（dev / staging / prod）的 schema 需要有明確的版本追蹤與 rollback 能力，確保環境一致性與部署安全。

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
IdempotencyMiddleware        ← 強制 X-Idempotency-Key，防重複操作
  │
  ▼
AuthenticationMiddleware     ← 驗證 JWT（玩家）或 SessionId（管理員）
  │
  ▼
UnitOfWorkMiddleware         ← 開啟 DB 事務，成功 Commit，例外 Rollback
  │
  ▼
Controller / Service
```

---

## 自動分配引擎

這是系統最核心的業務邏輯，分為三個階段：

### 流程圖

```mermaid
flowchart TD
    A[玩家報名] --> B[即時觸發 AutoAssignAsync]
    B --> C{找到符合時段\n且有空位的隊伍}
    C -->|是| D[加入現有隊伍]
    C -->|否| E[建立新隊伍草稿\nIsPublished = false]
    D --> F[分配完成]
    E --> F

    G[管理員觸發批次組隊] --> H[全局 AutoAssignAsync]
    H --> I[TeamSlotMergeService\n合併零散隊伍]
    I --> J[根據 BossTemplate 優化陣容]
    J --> K[尋找所有成員共同可用時間]
    K --> L[更新隊伍草稿]
```

### 補位保護機制

手動補位的成員標記 `IsManual = true`，批次重新排程時跳過含 `IsManual` 成員的隊伍，防止人工調整被覆蓋。

### IsTemporary 語意

| 值 | 來源 | 說明 |
|---|---|---|
| `false` | `TeamSlotAutoAssignService` | 玩家報名時系統自動建立，空時可自動清除 |
| `true` | Admin 手動開團 / `ScheduleService` 批次組隊預覽 | Admin 建立，空時不自動刪除 |

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
        +bool IsTemporary
        +bool IsPublished
        +int? TemplateId
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
        bool IsTemporary
        bool IsPublished
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
        string AccessToken
        string RefreshToken
        timestamptz Expiry
    }

    SystemConfig {
        int Id PK
        int DeadlineDayOfWeek
        interval DeadlineTime
        bool IsDeadlineNotified
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
    API->>Discord: 兌換 access_token
    Discord-->>API: token + 使用者資訊
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
| **截止提醒** | 報名截止日自動觸發，附上排程結果 URL |
| **身分組同步** | 登入時透過 Bot Token 查詢 Discord Guild Member，判斷 `Admin` / `User` |

---

## 主要服務一覽

| 服務 | 職責 |
|---|---|
| `RegisterService` | 玩家報名寫入，報名後觸發即時自動排程 |
| `TeamSlotAutoAssignService` | 自動排程核心：即時分配 + 批次排程 |
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

Secrets 以 volume mount 方式掛載至 `/run/secrets/`，與 Docker secrets 路徑一致，應用程式設定無需因部署平台而異。