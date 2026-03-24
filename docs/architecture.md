# System Design - MapleStoryRaidScheduler

本文件展示專案整體架構、API 流程、資料庫設計與部署方式，方便快速了解系統設計思路。

---

## 架構圖 (System Architecture)

### 高階系統架構

```mermaid
graph TD
    User["玩家 (Player)"] -->|HTTPS| Frontend["Next.js 前端"]
    Frontend -->|REST API| Backend["ASP.NET Core Web API"]

    subgraph "Docker 容器環境"
        Backend --> Application["Application Layer (DTOs, Interfaces)"]
        Application --> Domain["Domain Layer (Entities, Logic)"]
        Domain --> Infrastructure["Infrastructure Layer (Dapper, Discord)"]

        Infrastructure --> DB[("PostgreSQL")]
        Infrastructure --> DiscordBot["Discord Bot (DSharpPlus)"]
    end

    DiscordBot -->|發送通知| DiscordChannel["Discord 頻道"]
    DiscordChannel -->|查看| User
    User -->|OAuth2 登入| DiscordOAuth["Discord OAuth2"]
    DiscordOAuth -->|授權| Backend
```

## 領域設計 (Domain Design)

本系統的核心業務邏輯圍繞在「角色管理」、「副本登記」以及「自動/手動排程」上。

### 核心實體 (Core Entities)

```mermaid
classDiagram
    class Player {
        +ulong DiscordId
        +string Name
    }
    class Character {
        +string Id (名字)
        +ulong DiscordId
        +string Name
        +string Job
        +int AttackPower
    }
    class Boss {
        +int Id
        +string Name
        +int RequireMembers
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
        +List~Availability~ Availabilities
    }
    class CharacterRegister {
        +int Id
        +string CharacterId
        +int BossId
        +int Rounds
    }
    class TeamSlot {
        +int Id
        +int BossId
        +DateTimeOffset SlotDateTime
        +bool IsPublished
    }
    class TeamSlotCharacter {
        +int Id
        +int TeamSlotId
        +string CharacterId
        +bool IsManual
    }

    Player "1" -- "*" Character : 擁有多個
    Player "1" -- "0..1" Register : 登記時段
    Register "*" -- "1" Period : 屬於特定週期
    Register "1" -- "*" CharacterRegister : 登記具體角色與王
    CharacterRegister "*" -- "1" Boss : 關聯副本
    CharacterRegister "*" -- "1" Character : 關聯角色
    TeamSlot "*" -- "1" Boss : 屬於特定副本
    TeamSlot "1" -- "*" TeamSlotCharacter : 包含多個成員
    TeamSlotCharacter "*" -- "0..1" Character : 關聯角色
```

## 資料庫設計 (Database Design)

使用 PostgreSQL 作為資料儲存，並透過 Dapper 進行輕量級 ORM 操作。

### 實體關係圖 (ERD)

```mermaid
erDiagram
    Player ||--o{ Character : "owns"
    Period ||--o{ PlayerRegister : "defines"
    Player ||--o| PlayerRegister : "registers"
    PlayerRegister ||--o{ CharacterRegister : "contains"
    PlayerRegister ||--o{ PlayerAvailability : "has"
    Boss ||--o{ CharacterRegister : "is target of"
    Character ||--o{ CharacterRegister : "is assigned to"
    Boss ||--o{ TeamSlot : "scheduled for"
    TeamSlot ||--o{ TeamSlotCharacter : "contains"
    Character ||--o{ TeamSlotCharacter : "fills"
    Player ||--o{ Session : "has"

    Player {
        bigint discord_id PK
        string name
    }
    Character {
        string id PK
        bigint discord_id FK
        string name
        string job
        int attack_power
    }
    Boss {
        int id PK
        string name
        int require_members
    }
    Period {
        int id PK
        timestamp start_date
        timestamp end_date
    }
    PlayerRegister {
        int id PK
        bigint discord_id FK
        int period_id FK
    }
    PlayerAvailability {
        int id PK
        int player_register_id FK
        int weekday
        time start_time
        time end_time
    }
    CharacterRegister {
        int id PK
        int player_register_id FK
        string character_id FK
        int boss_id FK
        int rounds
    }
    TeamSlot {
        int id PK
        int boss_id FK
        timestamp slot_date_time
        boolean is_published
    }
    TeamSlotCharacter {
        int id PK
        int team_slot_id FK
        string character_id FK
        boolean is_manual
    }
    Session {
        string session_id PK
        bigint discord_id FK
        string access_token
        string refresh_token
        timestamp expiry
    }
```

## Discord 整合 (Discord Integration)

系統深度整合 Discord，用於身分驗證與通知。

### 1. 認證流程 (OAuth2)
- 玩家點擊前端「Discord 登入」。
- 跳轉至 Discord 授權頁面，取得 `code`。
- 後端 `DiscordOAuthClient` 將 `code` 兌換為 `access_token` 與 `refresh_token`。
- 系統根據 Discord ID 識別玩家，並核發自定義 JWT Token。

### 2. Discord Bot
- 使用 **DSharpPlus** 函式庫。
- **通知功能**: 當管理員發布排班表或有重要變動時，Bot 會在指定頻道發送通知。
- **身分組同步**: 透過 Bot Token 調用 Discord API (`GetUserRolesAsync`)，檢查玩家在伺服器中的身分組，以進行權限控管。

## 系統流程 (Request Flow)

### 1. 建立 Raid 登記
1. **前端**: 使用者選擇登記的王、時段與角色。
2. **API**: 發送 `POST /api/Register` 請求。
3. **Application Layer**: 驗證資料格式，將 DTO 轉換為 Domain Entity。
4. **Domain Layer**: 檢查時間衝突、重複登記等業務邏輯。
5. **Infrastructure Layer**: 
   - 透過 `UnitOfWork` 開啟事務。
   - 使用 Dapper 將資料寫入 `PlayerRegister`、`CharacterRegister` 與 `PlayerAvailability` 表。
   - 提交事務。
6. **回傳**: 回傳成功結果，前端更新狀態。

### 2. 自動排程 (Auto Scheduling)
1. **管理員**: 在前端點擊「自動排程」。
2. **API**: 發送 `POST /api/Schedule/Auto` 請求。
3. **Application Layer**: 呼叫 `IScheduleService`。
4. **Domain Layer**: 
   - 獲取目前週期的所有登記資料。
   - 根據演算法（考慮玩家時段、角色強度、副本需求）生成 `TeamSlot` 與 `TeamSlotCharacter`。
5. **Infrastructure Layer**: 儲存生成的排班草稿（`IsPublished = false`）。
6. **管理員**: 審核並微調後，點擊「發佈」。

### 3. 通知系統
- 當管理員發布排班表或有重要變動時，`IDiscordService` 會被調用。
- Discord Bot 在指定頻道發送 Embed 訊息通知相關玩家。

