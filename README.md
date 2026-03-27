# MapleStory Raid Scheduler

MapleStory Raid Scheduler 是一個專為新楓之谷（MapleStory）玩家設計的網頁應用程式，旨在協助玩家組織與排定 Boss 揪團（Raid）行程。

## 主要功能

- **角色管理:** 玩家可以新增、編輯與刪除其遊戲角色，並同步職業與戰力。
- **Boss 登記:** 支援多樣化的 Boss 登記，玩家可指定預計挑戰的副本及數量。
- **彈性時段:** 玩家可自訂每週可參與的時間段（如：週一 20:00 - 22:00）。
- **樣板管理 (Admin Only):** 管理員可定義 Boss 揪團樣板，設定特定職業類別（如：輸出、輔助）的人數需求與優先級，確保成團品質。
- **自動排程:** 玩家完成報名後，系統會**即時自動嘗試**將角色分配至符合時段的現有隊伍或建立新隊伍。這意味著系統在玩家報名階段就已開始初步排位，不完全依賴管理員發布排程，大幅降低手動排班的負擔。管理員後續仍可執行一鍵批次自動排程，並進行合併與微調。
- **補位系統:** 玩家可以在管理員發布排程（將草稿設為已發布）或是系統建立新隊伍後，針對未滿員的隊伍進行「補位」。前端會根據 Boss 樣板自動計算並顯示缺少的職業類別位子，玩家可選擇符合條件的角色填補空缺。**補位後的成員會被標記為手動調整 (`IsManual`)，確保後續管理員執行自動排程優化時，不會覆蓋或拆散已補位的成員與隊伍。** 資料庫僅儲存已確定的成員，不預留空位紀錄。
- **Discord 整合:**
  - **OAuth2 登入:** 透過 Discord 快速驗證身分，並核發對應角色的憑證 (JWT/SessionId)。
  - **排程通知:**
    - **每日提醒:** 每天自動通知玩家當日的隊伍與時段。
    - **報名截止提醒:** 報名截止當天，Bot 會發送訊息提醒玩家，並提供包含排團結果 URL 的連結。
  - **身分組同步:** 在登入時自動檢查伺服器身分組 (使用 Bot Token 調用 API)，確保只有授權玩家可以進行登記，並賦予 `Admin` 或 `User` 權限。

## 技術棧

- **後端 (Backend):** .NET 9 (C# 13, ASP.NET Core)
- **前端 (Frontend):** Next.js 15 (App Router, Tailwind CSS, Shadcn/UI)
- **資料庫 (Database):** PostgreSQL (使用 Dapper 作為 ORM)
- **容器化 (Containerization):** Docker
- **外部整合:** Discord API (OAuth2, DSharpPlus 機器人)
- **排程引擎:** 基於玩家可用時段與副本樣板需求 (Boss Template) 的即時自動配對、批次自動排程、合併與補位系統。報名即自動嘗試入隊，降低對管理員操作的依賴，系統採動態缺額顯示，資料庫不預留空位位子。**引擎會自動識別並保護手動調整 (`IsManual`) 的成員，避免在批次排程中被重分配。**

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
- `database`: PostgreSQL 18 資料庫 (使用 `DateTimeOffset` 儲存時戳)
- `backend`: ASP.NET Core Web API (Port: 5230, .NET 9)
- `frontend`: Next.js 前端 (Port: 3000, 支援深色模式)
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