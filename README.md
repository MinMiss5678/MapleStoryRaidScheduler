# MapleStory Raid Scheduler

MapleStory Raid Scheduler 是一個專為新楓之谷（MapleStory）玩家設計的網頁應用程式，旨在協助玩家組織與排定 Boss 揪團（Raid）行程。

## 專案概述

本專案提供了一個直觀的介面，讓團長可以輕鬆建立揪團、管理團員，並與 Discord 進行整合，提供即時的通知與身分組管理。

## 技術棧

- **後端 (Backend):** .NET 9 (C# 13)
- **前端 (Frontend):** Next.js (App Router, TypeScript)
- **資料庫 (Database):** PostgreSQL (使用 Dapper 作為 ORM)
- **容器化 (Containerization):** Docker
- **外部整合:** Discord API (身分組驗證、機器人通知)

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