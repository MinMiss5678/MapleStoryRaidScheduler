# 重構計畫：Code Smell 分析與改善

**日期**: 2026-04-15  
**分支**: main  
**狀態**: 草稿

---

## 概覽

針對 MapleStoryRaidScheduler Clean Architecture 專案進行全面 code smell 掃描，共發現 **3 High、8 Medium、1 Low** 問題。

---

## 發現的 Code Smells

### 🔴 HIGH

#### 1. Application Interface 直接依賴 Domain Entity（Clean Architecture 邊界違反）

| 項目 | 內容 |
|---|---|
| 類型 | Inappropriate Intimacy / 架構邊界違反 |
| 嚴重度 | **HIGH** |
| 影響範圍 | 所有 Service Interface 及其實作 |

**問題檔案**:
- `Application/Interface/IRegisterService.cs`
- `Application/Interface/ICharacterService.cs`
- `Application/Interface/ITeamSlotService.cs`

**問題**: Application 層的 Interface 方法簽名直接引用 `Domain.Entities.*`，違反 Clean Architecture 依賴方向（應向內，不應讓 Application 知道 Entity 細節）。

**改善方向**:
- 為每個 Interface 建立對應的 Request/Response DTO
- Interface 方法改用 DTO；Entity 轉換發生在 Infrastructure 層

---

#### 2. User Claims 提取邏輯在多個 Controller 重複

| 項目 | 內容 |
|---|---|
| 類型 | Duplicate Code |
| 嚴重度 | **HIGH** |
| 影響範圍 | 7+ 個 action method |

**問題檔案**:
- `Presentation.WebApi/Controller/RegisterController.cs`（行 24, 36, 54, 69, 84）
- `Presentation.WebApi/Controller/CharacterController.cs`（行 21, 35, 52, 60）
- `Presentation.WebApi/Controller/TeamSlotController.cs`（行 28, 41）

**問題**: 所有 Controller 重複撰寫 `User.Claims.FirstOrDefault(c => c.Type == "discordId")?.Value`。

**改善方向**:
- 建立 `BaseController` 或 `ControllerExtensions`，提供 `GetDiscordId()` / `GetUserRole()` Helper

---

#### 3. Presentation 層 Controller 直接操作 Domain Entity

| 項目 | 內容 |
|---|---|
| 類型 | Inappropriate Intimacy |
| 嚴重度 | **HIGH** |
| 影響範圍 | RegisterController、CharacterController |

**問題檔案**:
- `Presentation.WebApi/Controller/RegisterController.cs`（行 52）
- `Presentation.WebApi/Controller/CharacterController.cs`（行 33, 41）

**問題**: Controller 直接使用 `Register`、`Character` Domain Entity 而非 DTO，導致 Presentation 層滲透到 Domain。

**改善方向**:
- 建立 `RegisterRequest`/`RegisterResponse` DTO
- Controller 只接收/回傳 DTO，由 Application/Infrastructure 層做轉換

---

### 🟡 MEDIUM

#### 4. TeamSlotCharacter 填充邏輯重複

| 項目 | 內容 |
|---|---|
| 類型 | Duplicate Code |
| 嚴重度 | **MEDIUM** |

**問題檔案**:
- `Infrastructure/Services/TeamSlotAutoAssignService.cs`（行 101–117）
- `Infrastructure/Services/TeamSlotMergeService.cs`（行 262–269）

**問題**: `FillSlot()` 與手動複製邏輯重複賦值 `DiscordId`、`DiscordName`、`CharacterId`、`CharacterName`、`Job`、`AttackPower`、`Rounds`、`IsManual` 共 8 個欄位。

**改善方向**: 統一提取為 `TeamSlotCharacter.FillFrom(Register, Character, Player?)` 擴展方法或 Factory 方法。

---

#### 5. JobCategories 字典構建邏輯重複

| 項目 | 內容 |
|---|---|
| 類型 | Duplicate Code |
| 嚴重度 | **MEDIUM** |

**問題檔案**:
- `Infrastructure/Services/ScheduleService.cs`（行 52–54）
- `Infrastructure/Services/TeamSlotMergeService.cs`（行 59–61）

**問題**: 相同的 `GroupBy` + `ToDictionary` 構建 `jobCategories` 邏輯重複出現。

**改善方向**: 提取為 `JobCategoryHelper.BuildDictionary(IEnumerable<JobCategory>)` 靜態方法。

---

#### 6. FillSlot 方法參數過多（Long Parameter List）

| 項目 | 內容 |
|---|---|
| 類型 | Long Parameter List |
| 嚴重度 | **MEDIUM** |

**問題檔案**: `Infrastructure/Services/TeamSlotAutoAssignService.cs`（行 101–107）

**簽名**: `FillSlot(TeamSlotCharacter slot, Register register, Character character, CharacterRegister cr, Player? player, bool isManual = false)` — **6 個參數**

**改善方向**: 建立 `SlotFillContext` 物件封裝 `register`、`character`、`cr`、`player`。

---

#### 7. ScheduleService.AutoScheduleWithTemplateAsync 方法過長

| 項目 | 內容 |
|---|---|
| 類型 | Long Method |
| 嚴重度 | **MEDIUM** |
| 行數 | 132 行（行 28–160） |

**問題檔案**: `Infrastructure/Services/ScheduleService.cs`

**問題**: 單一方法包含初始化、三層迴圈、條件分支、最終排序與返回，難以閱讀與測試。

**改善方向**: 拆分為 `BuildTimeSlots()`、`AssembleTeams()`、`ValidateAndFinalizeTeam()` 子方法。

---

#### 8. TeamSlotService.UpdateAsync 方法過長且職責混雜

| 項目 | 內容 |
|---|---|
| 類型 | Long Method / Single Responsibility 違反 |
| 嚴重度 | **MEDIUM** |
| 行數 | 100 行（行 74–174） |

**問題檔案**: `Infrastructure/Services/TeamSlotService.cs`

**問題**: 同一方法同時處理刪除隊伍、新建隊伍、更新成員三種操作，多層 if-else 控制流。

**改善方向**: 拆分為 `DeleteTeamAsync()`、`CreateTeamAsync()`、`UpdateMemberAsync()` 三個方法。

---

#### 9. AuthenticationMiddleware 職責過多（God Class）

| 項目 | 內容 |
|---|---|
| 類型 | God Class |
| 嚴重度 | **MEDIUM** |
| 行數 | 131 行 |

**問題檔案**: `Presentation.WebApi/Middleware/AuthenticationMiddleware.cs`

**職責**: Session 驗證 + JWT 驗證 + Token 刷新 + Role 解析，全部混在一個 class。

**改善方向**: 拆分為 `SessionAuthHandler`、`JwtAuthHandler`、`TokenRefreshHandler`，透過 Chain of Responsibility 串接。

---

#### 10. Primitive Obsession — 楓之谷週期邏輯硬編碼

| 項目 | 內容 |
|---|---|
| 類型 | Primitive Obsession / Magic Numbers |
| 嚴重度 | **MEDIUM** |

**問題檔案**:
- `Domain/Helpers/SlotDateCalculator.cs`（行 16, 19, 21, 41, 55, 59）— `% 7`、`+ 7`、`(a.Weekday + 3) % 7`
- `Infrastructure/Services/TeamSlotMergeService.cs`（行 155）— `new[] { 4, 5, 6, 0, 1, 2, 3 }`

**問題**: 楓之谷以週四為週期起點的排序邏輯散落在多處，無說明。

**改善方向**: 建立 `MapleWeekday` 值物件或 `MapleStoryCalendar` 靜態類封裝週期常數與計算。

---

#### 11. TeamSlotQuery 查詢 SQL 重複

| 項目 | 內容 |
|---|---|
| 類型 | Duplicate Code |
| 嚴重度 | **MEDIUM** |

**問題檔案**: `Infrastructure/Query/TeamSlotQuery.cs`

**問題**: `GetByPeriodAndBossIdAsync` 與 `GetByPeriodAndDiscordIdAsync` 有大量相同的 SELECT 欄位宣告。

**改善方向**: 提取共用 SQL 片段為常數字串 `TeamSlotSelectColumns`。

---

### 🔵 LOW

#### 12. DiscordBotService 疑似 Dead Code

| 項目 | 內容 |
|---|---|
| 類型 | Dead Code |
| 嚴重度 | **LOW** |

**問題檔案**: `Infrastructure/BackgroundJobs/DiscordBotService.cs`

**問題**: 服務存在但未確認是否在 DI 中正確註冊與使用。

**改善方向**: 確認是否仍需要；若否則刪除。

---

## 建議重構順序（Sprint 規劃）

### Sprint 1 — 架構邊界修復（Critical）

| # | 任務 | 預估影響 |
|---|---|---|
| 1 | Application Interface 改用 DTO，去除 Domain Entity 依賴 | 高，需同步修改所有 Infrastructure 實作 |
| 2 | Controller 改用 Request/Response DTO | 高，需同步修改 Service Interface |
| 3 | 建立 `BaseController` 提取 Claims 邏輯 | 低，單純提取 |

### Sprint 2 — 重複程式碼消除（High）

| # | 任務 | 預估影響 |
|---|---|---|
| 4 | `TeamSlotCharacter.FillFrom()` 統一填充邏輯 | 中 |
| 5 | `JobCategoryHelper.BuildDictionary()` 提取 | 低 |
| 6 | `TeamSlotQuery` SQL 常數提取 | 低 |

### Sprint 3 — 方法拆分與職責分離（Medium）

| # | 任務 | 預估影響 |
|---|---|---|
| 7 | `ScheduleService` 拆分子方法 | 中，需補充單元測試 |
| 8 | `TeamSlotService.UpdateAsync` 拆分 | 中 |
| 9 | `AuthenticationMiddleware` 拆分 | 中，需驗證認證流程 |

### Sprint 4 — 領域概念強化（Medium）

| # | 任務 | 預估影響 |
|---|---|---|
| 10 | `MapleWeekday` / `MapleStoryCalendar` 封裝週期邏輯 | 低，Domain 層新增 |
| 11 | `SlotFillContext` 封裝長參數列表 | 低 |
| 12 | 確認並清除 Dead Code | 低 |

---

## 未解問題

1. Application Interface 改用 DTO 後，Discord Bot（`Presentation/`）是否也需同步修改？需確認 Bot 是否直接使用這些 Interface。
2. `DiscordBotService` 是否有計畫重啟使用，還是確定廢棄？
3. Sprint 1 的 DTO 引入是否需同步更新前端 API contract 文件？
