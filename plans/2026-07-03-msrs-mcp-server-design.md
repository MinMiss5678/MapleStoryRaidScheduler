# MSRS MCP Server 設計文件

> 目標：為 MapleStoryRaidScheduler 設計一個自訂 MCP Server，讓 Claude（或其他 MCP Client）能查詢角色 / 隊伍 / 排程、並在受控前提下觸發自動排程。
> 範圍：**設計文件（不含實作）**。技術棧：C# / .NET 9，官方 `ModelContextProtocol` SDK。
> 日期：2026-07-03

---

## 一、定位：MCP 是「第三個 Presentation adapter」

專案現有兩個進入點：

```
Presentation.WebApi  → ASP.NET Core（前端 / 一般使用者）
Presentation         → Discord Bot 主控台（DSharpPlus）
Presentation.Mcp     → ★ 新增：MCP Server（給 AI Client）  ← 本設計
```

**核心決策：MCP Server 直接重用 Application / Infrastructure，不透過 HTTP 呼叫 Web API。**

理由（Clean Architecture 的自然延伸）：
- MCP 跟 Discord Bot、Web API 是**同一層（Presentation）的平行 adapter**，都往內依賴 Application 的 Service / Query 介面。
- 少一個 HTTP hop、少一套序列化，直接注入 `ICharacterQuery`、`IScheduleService` 等既有介面。
- 換句話說：**MCP tool = 對 Application 介面的一層薄包裝**，不重寫任何業務邏輯（DRY）。

> 設計要點：不為 MCP 另寫一套邏輯，而是把它當成 Clean Architecture 的一個新 Presentation adapter，重用既有的 CQRS 介面。

---

## 二、架構圖

```
┌─────────────────────────────────────────────┐
│  MCP Client（Claude Desktop / Claude Code）  │
└───────────────────┬─────────────────────────┘
                    │ stdio（JSON-RPC）
┌───────────────────▼─────────────────────────┐
│  Presentation.Mcp（新專案，Console Host）    │
│   - ModelContextProtocol SDK                 │
│   - [McpServerTool] 方法 = 薄包裝            │
│   - 每個 tool 呼叫前開 UnitOfWork scope       │
└───────────────────┬─────────────────────────┘
                    │ 注入既有介面
┌───────────────────▼─────────────────────────┐
│  Application（ICharacterQuery / ISchedule...）│
│  Infrastructure（Dapper Repo / Services）     │
│  Domain（Entities / Repository interfaces）   │
└───────────────────┬─────────────────────────┘
                    │
              PostgreSQL 18
```

---

## 三、Tool 清單（對應真實 CQRS 介面）

> 設計原則：**讀取 tool 開放、寫入 tool 加護欄**。命名用動詞_名詞，參數用領域詞彙。

### 讀取類（安全，預設開放）

| MCP Tool | 對應介面 | 說明 |
|---|---|---|
| `get_current_period` | `IPeriodQuery.GetByNowAsync` | 取得目前檔期（raid 週期） |
| `get_next_period` | `IPeriodQuery.GetNextPeriodAsync` | 下一檔期 |
| `list_characters` | `ICharacterQuery.GetWithDiscordNameAsync(discordId, bossId?)` | 某玩家的角色（含 Discord 名） |
| `get_team_slots_by_boss` | `ITeamSlotQuery.GetByPeriodAndBossIdAsync(period, bossId)` | 某王某檔期的隊伍編組 |
| `get_team_slots_by_player` | `ITeamSlotQuery.GetByPeriodAndDiscordIdAsync(period, discordId)` | 某玩家在的所有隊伍 |
| `get_registrations` | `IPlayerRegisterQuery.GetByNowPeriodIdAsync(bossId)` | 某王的報名清單 |
| `list_bosses` | `IBossService` | 王 + 模板需求（職業配額） |

### 寫入類（危險，需護欄 → 見第五節）

| MCP Tool | 對應介面 | 危險等級 |
|---|---|---|
| `auto_schedule` | `IScheduleService.AutoScheduleWithTemplateAsync(bossId, templateId)` | 🔴 高（會重排隊伍，可能覆蓋非手動成員） |
| `update_team_slot` | `ITeamSlotService.UpdateAsync(...)` | 🟠 中（改單一隊伍） |
| `register_character` | `IRegisterService` | 🟡 低（新增報名） |

> `IsManual = true` 的成員受保護、不被批次重排——這個既有規則 MCP 必須尊重，不繞過。

---

## 四、Resource 清單（唯讀參考資料）

MCP 的 **Resource** 適合放「AI 需要當背景、但不常變」的資料，Client 可主動載入：

| Resource URI | 內容 |
|---|---|
| `msrs://reference/job-categories` | 職業分類表（`IJobCategoryRepository`） |
| `msrs://reference/boss-templates` | 各王的模板需求（職業配額） |
| `msrs://reference/current-period` | 目前檔期摘要 |

> 差別：**Tool = 動作（AI 主動呼叫）**；**Resource = 背景資料（可掛給對話當 context）**。職業配額這種「解讀排程結果需要的字典」放 Resource 最適合。

---

## 五、認證與安全邊界（★ 最關鍵）

MSRS 有雙認證：**JWT（一般玩家）+ SessionId（管理員）**，加上 `IsManual` 保護與管理員限定操作。MCP **絕不能繞過這些**。

### 設計三道護欄

1. **身分模式（啟動時決定）**
   - MCP Server 啟動用環境變數帶一個「執行身分」：`MSRS_MCP_ROLE=readonly | admin`
   - `readonly`：只註冊讀取 tool；寫入 tool 根本不 expose
   - `admin`：全開，但寫入 tool 仍要二次確認（見下）

2. **寫入 tool 的二次確認**
   - `auto_schedule` 這種高危 tool，回傳前先做 **dry-run**：回「這次會影響哪些隊伍、覆蓋幾個非手動成員」，要求 Client 帶 `confirm: true` 才真的執行。
   - 避免 AI「一句話把整個檔期重排掉」。

3. **稽核 log**
   - 每個寫入 tool 呼叫寫一筆 log（誰、何時、什麼參數、影響筆數），方便事後追。

> 安全設計：MCP 讓 AI 能操作系統，等於開一個新的攻擊面。設計是預設唯讀、高危操作先 dry-run + 二次確認、全程稽核——把 AI agent 當成需要最小權限（least privilege）的使用者對待。

---

## 六、交易與生命週期（重用 UnitOfWork）

Web API 靠 `UnitOfWorkMiddleware` 每個 request 包一個交易。MCP 沒有 middleware，要**每個 tool 呼叫自己管 scope**：

```
每個 [McpServerTool] 方法：
  1. 開一個 DI scope（取得該 scope 的 IUnitOfWork / DbContext）
  2. 讀取 tool：查完直接回，不需 commit（或唯讀交易）
  3. 寫入 tool：呼叫 Service → 成功 commit / 例外 rollback
  4. 釋放 scope
```

> 對應到 CSharp.md 的 DI 生命週期：`IUnitOfWork` / `DbConnection` 是 **Scoped**，MCP 要手動 `CreateScope()`，跟「Singleton 注入 Scoped 要用 IServiceScopeFactory」是同一個觀念。

---

## 七、技術棧與設定

### 專案

```
Presentation.Mcp/
├── Program.cs                 ← Host + MCP server 註冊 + DI
├── Tools/
│   ├── PeriodTools.cs         ← [McpServerToolType]
│   ├── CharacterTools.cs
│   ├── TeamSlotTools.cs
│   └── ScheduleTools.cs（含 dry-run/confirm）
├── Resources/
│   └── ReferenceResources.cs
└── appsettings.json           ← 連線字串、MSRS_MCP_ROLE
```

### 套件

- `ModelContextProtocol`（官方 C# SDK）
- 重用既有的 Infrastructure DI 註冊（連線字串、Dapper、Repository、Service）

### Transport

- **MVP 用 stdio**：本機給 Claude Desktop / Claude Code 用，最單純、免處理網路認證。
- 未來要遠端多人用再上 **HTTP + SSE**（要另外做認證，複雜度跳一級）。

### Claude Code 設定（`.mcp.json` 範例）

```json
{
  "mcpServers": {
    "msrs": {
      "command": "dotnet",
      "args": ["run", "--project", "Presentation.Mcp"],
      "env": { "MSRS_MCP_ROLE": "readonly" }
    }
  }
}
```

---

## 八、分階段實作計畫

| 階段 | 內容 | 產出 |
|---|---|---|
| **P0 MVP** | Console host + stdio + `get_current_period` + `list_characters` 兩個唯讀 tool | 能在 Claude Code 查到即時資料 |
| **P1 讀取全開** | 補齊所有讀取 tool + 3 個 Resource | AI 能完整解讀排程狀態 |
| **P2 寫入 + 護欄** | `update_team_slot` + `auto_schedule`（dry-run/confirm）+ 稽核 log | AI 能受控地執行排程 |
| **P3 遠端（可選）** | 換 HTTP + SSE transport + 認證 | 多人 / 遠端可用 |

> 建議先做完 P0 驗證「MCP 打得通、資料讀得到」，再往上疊。

---

## 九、風險與取捨

| 風險 | 對策 |
|---|---|
| AI 誤觸高危寫入（重排整個檔期） | 預設 readonly、dry-run + confirm、稽核 log |
| MCP 繞過既有權限規則 | 重用 Service 層（權限在裡面），不直接碰 Repository |
| Scoped 生命週期用錯（連線 / 交易外洩） | 每 tool 一個 DI scope，明確 commit/rollback |
| Tool 回傳整包 Entity（含敏感欄位 / 循環參照） | 回傳既有 DTO，不直接吐 Entity |
| stdio 只能本機 | MVP 接受；遠端需求再上 HTTP+SSE |

---

## 十、未解問題（實作前要拍板）

1. **執行身分怎麼給？** 目前設計用環境變數 `MSRS_MCP_ROLE`；未來要不要真的接 Discord OAuth 拿到「這是哪個管理員」，讓稽核 log 有真實身分？
2. **auto_schedule 的 dry-run** 現有 `IScheduleService` 沒有 dry-run 模式——要新增一個「試算不落庫」的方法，還是在 MCP 層自己包一層交易 rollback 模擬？
3. **要不要暴露寫入 tool？** 如果只是自己查資料 / 給 AI 當分析助手，P0-P1（純唯讀）可能就夠，P2 寫入視實際需求再決定。
4. **DTO 夠不夠？** 部分 Query 目前回 `Entity`（如 `Period`、`PlayerRegisterSchedule`），MCP 對外最好統一回 DTO，可能要補幾個。

---

> **下一步**：如果 P0 方向同意，我可以把 P0 MVP（Console host + 2 個唯讀 tool + `.mcp.json`）實作出來，先打通「Claude Code ↔ MSRS 資料」這條線。
