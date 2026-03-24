# MapleStory Raid Scheduler

MapleStory Raid Scheduler 是一個專為新楓之谷（MapleStory）玩家設計的網頁應用程式，旨在協助玩家組織與排定 Boss 揪團（Raid）行程。

## 主要功能

- **角色管理:** 玩家可以新增、編輯與刪除其遊戲角色，並同步職業與戰力。
- **Boss 登記:** 支援多樣化的 Boss 登記，玩家可指定預計挑戰的副本及數量。
- **彈性時段:** 玩家可自訂每週可參與的時間段（如：週一 20:00 - 22:00）。
- **自動排程:** 管理員可根據玩家登記與時段，一鍵生成最佳化的隊伍排班表。
- **Discord 整合:**
  - **OAuth2 登入:** 透過 Discord 快速驗證身分。
  - **排程通知:** 排班完成時，Bot 會在頻道發送訊息。
  - **身分組同步:** 自動檢查伺服器身分組，確保只有授權玩家可以進行登記。

## 技術棧

- **後端 (Backend):** .NET 9 (C# 13, ASP.NET Core)
- **前端 (Frontend):** Next.js (App Router, Tailwind CSS, Shadcn/UI)
- **資料庫 (Database):** PostgreSQL (使用 Dapper 作為 ORM)
- **容器化 (Containerization):** Docker
- **外部整合:** Discord API (OAuth2, DSharpPlus 機器人)
- **排程引擎:** 基於玩家可用時段與副本需求的自動配對系統

## 文件 (Documentation)

- [Architecture 設計說明](docs/architecture.md)

## 專案結構

本解決方案採用分層架構：

- **Domain:** 核心實體、介面與業務邏輯。
- **Application:** DTOs、服務介面、應用程式服務與查詢邏輯。
- **Infrastructure:** 實作外部關切點，如資料庫存取 (Dapper)、背景作業與 Discord 整合。
- **Presentation.WebApi:** ASP.NET Core Web API 專案 (控制器、中間件)。
- **web:** Next.js 前端應用程式。
- **Test:** 包含單元測試與整合測試。

## 快速開始

### 使用 Docker (建議)

專案根目錄包含 `compose.yaml`，可以一次啟動所有服務：

```bash
docker compose up -d
```

這將啟動以下服務：
- `database`: PostgreSQL 18 資料庫
- `backend`: ASP.NET Core Web API (Port: 5230)
- `frontend`: Next.js 前端 (Port: 3000)
- `bot`: Discord 機器人服務
- `cloudflared`: Cloudflare Tunnel (用於公開訪問)

### 本地開發

#### 後端
1. 確保已安裝 .NET 9 SDK。
2. 進入 `Presentation.WebApi` 目錄。
3. 配置 `appsettings.json` 或環境變數。
4. 執行 `dotnet run`。

#### 前端
1. 進入 `web` 目錄。
2. 執行 `npm install` 安裝依賴。
3. 配置 `.env.local`。
4. 執行 `npm run dev`。

## 開發規範

- 遵循現有的 C# 與 TypeScript 編碼風格。
- 新功能應儘可能編寫相關測試。
- 使用 Docker 進行本地開發環境建置。

## 授權

本專案採用 MIT 授權。詳見 [LICENSE](LICENSE) 文件。