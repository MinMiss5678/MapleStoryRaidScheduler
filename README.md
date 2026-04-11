# MapleStory Raid Scheduler

> 一個為楓之谷玩家設計的 **Boss 副本排程管理系統**，整合 Discord OAuth2 登入、自動排程引擎與 Bot 通知，從零到部署完整實作。

## 技術亮點

- **自製 SqlBuilder**：不依賴 EF Core，以 Lambda 表達式解析（`Expression<Func<T, bool>>`）實作型別安全的 SQL 建構工具，支援 CTE、條件群組（AND/OR）、NULL 比較，避免字串拼接錯誤。
- **CQRS-Lite 讀寫分離**：寫入路徑走 Service + Repository（逐筆操作、語意清晰），讀取路徑走獨立 Query 介面（不受寫入模型約束，可自由使用 JOIN 等最佳化 SQL 一次查回所需資料）。
- **冪等性保護**：所有 POST/PUT/DELETE 請求強制帶 `X-Idempotency-Key`，由 Middleware 統一攔截並快取結果，防止網路重試造成重複操作。
- **雙軌身分驗證**：一般玩家使用自定義 JWT，管理員使用 Session（儲存於 DB），兩者在同一 Middleware 中統一驗證，依 Discord 身分組自動分流。
- **自動排程引擎**：玩家報名後即時觸發，根據可用時段比對現有隊伍空位，無匹配則建立新隊伍；管理員可手動觸發全局批次排程與隊伍合併優化。
- **補位保護機制**：手動補位的成員標記 `IsManual = true`，自動排程引擎會跳過這些成員所在的隊伍，防止人工調整被覆蓋。

## 技術棧

| 層級 | 技術 |
|---|---|
| **後端** | .NET 9 (C# 13)、ASP.NET Core Web API |
| **前端** | Next.js 15 (App Router)、Tailwind CSS、Shadcn/UI |
| **資料庫** | PostgreSQL 18（Dapper 手寫 SQL，無 EF Core） |
| **身分驗證** | Discord OAuth2、自定義 JWT、DB Session |
| **通知** | DSharpPlus Discord Bot |
| **日誌** | Serilog + Seq（結構化可查詢日誌） |
| **容器化** | Docker Compose |
| **測試** | xUnit + Moq |

## 架構設計

採用**分層架構**，依賴方向由外向內單向流動：

```
Presentation.WebApi  →  Application  →  Domain
                              ↓
                       Infrastructure
```

- **Domain**：純 C# 實體與介面，零外部依賴，可獨立測試。
- **Application**：DTOs、服務介面、查詢介面，定義業務邊界。
- **Infrastructure**：Dapper Repository、Discord 整合、背景作業，實作所有外部依賴。
- **Presentation.WebApi**：Controller + 四層 Middleware 管線。

詳細設計請見 [架構設計文件](docs/architecture.md)。

## 專案結構

| 專案 | 職責 |
|---|---|
| `Domain/` | 核心實體、Repository 介面、業務邏輯，無外部依賴 |
| `Application/` | DTOs、服務介面 (`Interface/`)、查詢介面 (`Queries/`) |
| `Infrastructure/` | Dapper Repository 實作、Discord 整合、背景作業 |
| `Presentation.WebApi/` | ASP.NET Core 控制器、Middleware 管線 |
| `Presentation/` | Discord Bot 主控台應用程式 (DSharpPlus) |
| `web/` | Next.js 15 前端 (App Router, Tailwind, Shadcn/UI) |
| `Test/` | xUnit + Moq 單元測試 |
| `Utils/` | 自製 SqlBuilder、JSON 轉換器 |

## 快速開始

### Docker（推薦）

```bash
docker compose up -d
```

啟動服務：

| 服務 | Port |
|---|---|
| `database` PostgreSQL 18 | 5432 |
| `backend` ASP.NET Core Web API | 5230 |
| `frontend` Next.js | 3000 |
| `bot` Discord Bot | — |
| `cloudflared` Cloudflare Tunnel | — |

### 手動啟動

**後端**
```bash
cd Presentation.WebApi
# 設定 appsettings.json
dotnet run
```

**前端**
```bash
cd web
npm install
# 設定 .env.local
npm run dev
```

## 授權

MIT License — 詳見 [LICENSE](LICENSE)。
