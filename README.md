# MapleStory Raid Scheduler

> 一個為楓之谷玩家設計的 **Boss 副本排程管理系統**，整合 Discord OAuth2 登入、自動排程引擎與 Bot 通知，從零到部署完整實作。

## 技術亮點

- **自製 SqlBuilder**：不依賴 EF Core，以 Lambda 表達式解析（`Expression<Func<T, bool>>`）實作型別安全的 SQL 建構工具，支援 CTE、條件群組（AND/OR）、NULL 比較，避免字串拼接錯誤。
- **CQRS-Lite 讀寫分離**：寫入路徑走 Service + Repository（逐筆操作、語意清晰），讀取路徑走獨立 Query 介面（不受寫入模型約束，可自由使用 JOIN 等最佳化 SQL 一次查回所需資料）。
- **冪等性保護**：所有 POST/PUT/DELETE 請求強制帶 `X-Idempotency-Key`，由 Middleware 統一攔截並快取結果，防止網路重試造成重複操作。
- **雙軌身分驗證**：一般玩家使用自定義 JWT，管理員使用 Session（儲存於 DB），兩者在同一 Middleware 中統一驗證，依 Discord 身分組自動分流。
- **按身分限流**：以已驗證的 `discordId`（session/JWT claim）為 key 做 per-user 限流（換 IP 也繞不掉），Middleware 掛在驗證之後、交易之前，被擋的請求不白開 DB 交易。
- **自動分配引擎**：玩家報名後即時觸發，根據可用時段比對現有隊伍空位，無匹配則建立新暫時隊伍。
- **批次組隊預覽**：管理員以職業範本手動觸發，從所有報名者批次生成完整隊伍建議（IsTemporary），確認後才正式寫入。
- **補位保護機制**：手動補位的成員標記 `IsManual = true`，自動分配引擎會跳過這些成員，防止人工調整被覆蓋。
- **Schema 版本管理**：以 golang-migrate 管理資料庫 migration，up/down 分開維護，Docker Compose 與 Kubernetes 皆整合 migrate 服務，確保各環境 schema 一致。

## 技術棧

| 層級 | 技術 |
|---|---|
| **後端** | .NET 9 (C# 13)、ASP.NET Core Web API |
| **前端** | Next.js 15 (App Router)、Tailwind CSS、Shadcn/UI |
| **資料庫** | PostgreSQL 18（Dapper 手寫 SQL，無 EF Core） |
| **Schema 管理** | golang-migrate（up/down SQL，版本追蹤） |
| **身分驗證** | Discord OAuth2、自定義 JWT、DB Session |
| **通知** | DSharpPlus Discord Bot |
| **日誌** | Serilog + Seq（結構化可查詢日誌） |
| **容器化** | Docker Compose、Kubernetes |
| **測試** | xUnit + Moq（單元）、Testcontainers（整合）、Playwright（E2E）|

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
- **Presentation.WebApi**：Controller + Middleware 管線（例外處理 / 冪等 / 驗證 / 限流 / 交易）。

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
| `Test.Integration/` | 整合測試（Testcontainers 真 Postgres、WebApplicationFactory 限流測試）|
| `Utils/` | 自製 SqlBuilder、JSON 轉換器 |
| `db/migrations/` | golang-migrate SQL 檔案（up/down） |
| `k8s/` | Kubernetes 部署設定 |

## 快速開始

### Docker（推薦）

```bash
# 複製 secrets 範本並填入實際值
cp secrets/example/* secrets/

docker compose up -d
```

啟動順序由 Docker Compose 自動管理：`database` → `migrate`（schema 初始化）→ `backend` / `bot` → `frontend` → `cloudflared`

| 服務 | Port | 說明 |
|---|---|---|
| `database` PostgreSQL 18 | 5432 | — |
| `migrate` golang-migrate | — | 執行 schema migration 後自動退出 |
| `backend` ASP.NET Core Web API | 5230 | — |
| `frontend` Next.js | 3000 | — |
| `bot` Discord Bot | — | — |
| `seq` 日誌收集 | 8080 | Web UI |
| `cloudflared` Cloudflare Tunnel | — | — |

### Kubernetes

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/secrets.yaml   # 先填入真實值
kubectl apply -f k8s/database.yaml
kubectl apply -f k8s/seq.yaml
kubectl apply -f k8s/migrate-job.yaml  # 等 Job 完成後再繼續
kubectl apply -f k8s/backend.yaml
kubectl apply -f k8s/frontend.yaml
kubectl apply -f k8s/bot.yaml
kubectl apply -f k8s/cloudflared.yaml
```

### 新增 Migration

```bash
# 1. 建立 up/down SQL 檔
touch db/migrations/000002_your_change.up.sql
touch db/migrations/000002_your_change.down.sql

# 2. 重新 build migrate image
docker build -f db/Dockerfile.migrate -t minqq/migrate:latest db/
docker push minqq/migrate:latest

# 3. 本機套用
docker compose run --rm migrate
```

### 手動啟動（開發用）

**後端**
```bash
cd Presentation.WebApi
dotnet run
```

**前端**
```bash
cd web
npm install
npm run dev
```

## 授權

MIT License — 詳見 [LICENSE](LICENSE)。
