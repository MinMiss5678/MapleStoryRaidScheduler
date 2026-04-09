# 重構計畫 — 2026-04-11

## 前次計畫回顧

上一份計畫（`refactoring-plan-2026-04-10.md`）大部分已完成，以下為**遺留未完成項目**：

| 編號 | 項目 | 狀態 |
|------|------|------|
| M-5 | Domain Entity 混入 PATCH 操作指令（`DeleteCharacterRegisterIds`、`DeleteTeamSlotCharacterIds`） | 未完成 |
| LOW-3 | `DbContext.Commit()/Rollback()` 後無防護，後續寫入靜默 AutoCommit | 未完成 |
| M-6 | BossService 零業務邏輯（貧血 Service） | 待評估 |

### 已可關閉的待確認項
- ~~`TeamSlotController "admin"` 小寫 vs `AuthAppService "Admin"` 大寫~~ — **已確認一致**：`AuthAppService` 已改為 `"admin"` 小寫，`TeamSlotController.IsInRole("admin")` 一致，無 case-insensitive 問題

---

## 審查範圍

- Git range: `e33a856..f8eee69`
- 審查檔案：後端全層（Application、Domain、Infrastructure、Presentation.WebApi）+ 前端 `web/`

---

## High（架構問題）

### H-1 ScheduleService 殘留孤立方法實作

- **位置**：`Infrastructure/Services/ScheduleService.cs:31-65`
- **問題**：`GetPartiesAsync`、`JoinTeamAsync`、`FinalizeScheduleAsync` 已從 `IScheduleService` 介面移除，但實作仍殘留在 class 中，成為 dead code
- **修法**：刪除三個方法的實作

### H-2 Domain Entity 混入 PATCH 操作指令（遺留 M-5）

- **位置**：`Domain/Entities/Register.cs`（`DeleteCharacterRegisterIds`）、`Domain/Entities/TeamSlot.cs`（`DeleteTeamSlotCharacterIds`、`BossName`、`PeriodId`）
- **問題**：PATCH 操作指令混入 Domain Entity，違反 DDD；`TeamSlot` 同時承擔 Entity/ViewModel/Command 三個職責
- **前端佐證**：前端 `RegisterFormState.deleteCharacterRegisterIds` 和 `scheduleService.saveSchedule` 的 `deleteTeamSlotIds` 直接對應這些欄位
- **修法**：
  1. 建立 `Application/DTOs/RegisterUpdateCommand.cs`，包含 `DeleteCharacterRegisterIds`
  2. 建立 `Application/DTOs/TeamSlotUpdateCommand.cs`，包含 `DeleteTeamSlotCharacterIds`
  3. Domain Entity 只保留純領域狀態
  4. `RegisterService.UpdateAsync` 和 `TeamSlotService.UpdateAsync` 改接收 Command DTO
  5. 前端 types 無需變動（DTO 欄位名稱保持一致）

### H-3 RegisterService 截止時間檢查重複

- **位置**：`Infrastructure/Services/RegisterService.cs`（`CreateAsync` 第 97-104 行、`UpdateAsync` 第 117-124 行）
- **問題**：`CreateAsync` 和 `UpdateAsync` 各自獨立查詢 `SystemConfig` + `Period` 並檢查截止時間，邏輯完全重複
- **修法**：提取 `private async Task EnsureRegistrationOpen()` 方法，兩處呼叫統一

### H-4 AuthenticationMiddleware JWT 路徑缺少 Role Claim

- **位置**：`Presentation.WebApi/Middleware/AuthenticationMiddleware.cs:68-78`
- **問題**：JWT 驗證成功時只建立 `discordId` claim，未包含 `ClaimTypes.Role`；導致 JWT 使用者呼叫需要 role 的端點時，`User.IsInRole()` 永遠為 false
- **影響**：目前 `AuthorizeRoleAttribute` 檢查會擋住非 session 使用者，但若未來放寬此限制，JWT 使用者將無法通過 role 檢查
- **修法**：JWT 驗證成功後，從 JWT claims 或 DB 取得 role 並加入 identity

### H-5 BossService 貧血 Service（遺留 M-6）

- **位置**：`Infrastructure/Services/BossService.cs`
- **問題**：所有 9 個方法純委派給 `IBossRepository`，無任何業務邏輯
- **修法**：評估兩個方案：
  - **方案 A**：移除 `BossService`，`BossController` 直接注入 `IBossRepository`
  - **方案 B**：將 Boss + Template + Requirement 的複合操作事務控制移至此 Service（如刪除 Boss 時級聯刪除 Template）

---

## Medium（代碼品質）

### M-1 DbContext Commit/Rollback 後無防護（遺留 LOW-3）

- **位置**：`Infrastructure/Dapper/DbContext.cs:22-30`
- **問題**：`Commit()` / `Rollback()` 設置 `Transaction = null` 後，若再次執行寫入操作會靜默以 AutoCommit 模式執行，可能造成資料不一致
- **修法**：
  - 方案 A：加入 `_committed` flag，`Commit/Rollback` 後的寫入操作拋出 `InvalidOperationException`
  - 方案 B：在文件中明確說明此行為，並在 `UnitOfWorkMiddleware` 確保每個 request 只有一次 Begin/Commit 週期

### M-2 RegisterService 建構函式依賴過多

- **位置**：`Infrastructure/Services/RegisterService.cs`（建構函式 8 個參數）
- **問題**：8 個注入依賴，職責可能過重
- **修法**：評估是否可將「截止時間檢查」提取為獨立的 `IRegistrationPolicy` 服務，減少 RegisterService 的直接依賴

### M-3 TeamSlotService.UpdateAsync 缺少事務保護

- **位置**：`Infrastructure/Services/TeamSlotService.cs:70-156`
- **問題**：`UpdateAsync` 包含多個刪除和建立操作（刪除 TeamSlot、刪除 TeamSlotCharacter、建立新 TeamSlot、建立新 Character），若中途失敗會造成資料不完整
- **修法**：確認 `UnitOfWorkMiddleware` 是否已涵蓋此場景；若否，在方法內顯式使用 `IUnitOfWork`

### M-4 ScheduleService.GetDateTimeFromPeriod 為 public async 但無 await

- **位置**：`Infrastructure/Services/ScheduleService.cs:203-232`
- **問題**：方法標記為 `async` 但內部無 `await`，會產生 CS1998 警告；且此方法為純計算，不需要 async
- **修法**：移除 `async`，改為同步方法回傳 `DateTimeOffset`；更新 `IScheduleService` 介面

### M-5 Program.cs DI 註冊過長且無分組

- **位置**：`Presentation.WebApi/Program.cs`（約 60 行 DI 註冊）
- **問題**：所有服務註冊集中在一個檔案，無分組或 extension method，難以維護
- **修法**：建立 `ServiceCollectionExtensions`，按層分組：
  - `AddApplicationServices()`
  - `AddInfrastructureServices()`
  - `AddRepositories()`

### M-6 前端 service 層缺少統一錯誤處理

- **位置**：`web/services/*.ts`
- **問題**：各 service 方法直接呼叫 `fetch`，錯誤處理分散在各頁面
- **修法**：建立共用的 `apiClient` wrapper，統一處理 401/403 跳轉、網路錯誤 toast 等

---

## Low（命名、風格、清理）

### LOW-1 ScheduleService.AutoScheduleWithTemplateAsync 使用 `throw new Exception`

- **位置**：`Infrastructure/Services/ScheduleService.cs:70`
- **問題**：`throw new Exception("Template not found")` 使用泛型 Exception，呼叫方無法精確 catch
- **修法**：改為 `throw new KeyNotFoundException($"Template {templateId} not found")` 或自訂 `NotFoundException`

### LOW-2 RegisterService 使用 `throw new Exception` 表示業務錯誤

- **位置**：`Infrastructure/Services/RegisterService.cs`（截止時間檢查）
- **問題**：`throw new Exception("目前已超過報名截止時間...")` 使用泛型 Exception
- **修法**：建立 `BusinessRuleException` 或使用 `InvalidOperationException`，Controller 層統一轉為 400 回應

### LOW-3 AuthAppService 未實作介面

- **位置**：`Application/Services/AuthAppService.cs`、`Presentation.WebApi/Program.cs:28`
- **問題**：`AuthAppService` 直接以具體類別註冊（`AddScoped<AuthAppService, AuthAppService>`），未抽象為介面，不利於測試替換
- **修法**：建立 `IAuthAppService` 介面，DI 改為 `AddScoped<IAuthAppService, AuthAppService>`

### LOW-4 前端 API Proxy 白名單硬編碼

- **位置**：`web/app/api/[...path]/route.ts`
- **問題**：白名單字串陣列硬編碼在 route handler 中
- **修法**：提取至 `constants/apiWhitelist.ts`

---

## 執行計畫

### 第一階段 — 清理 Dead Code + 快速修復
- [x] H-1：刪除 ScheduleService 三個孤立方法實作
- [x] M-4：`GetDateTimeFromPeriod` 移除 async，改為同步方法
- [x] LOW-1：ScheduleService 改用具體 Exception 類型
- [x] LOW-2：RegisterService 改用具體 Exception 類型

### 第二階段 — DDD 與架構改善
- [x] H-2：Domain Entity 分離 Command DTO（Register + TeamSlot）
- [x] H-3：RegisterService 提取截止時間檢查共用方法
- [x] H-5：BossService 加入級聯刪除業務邏輯（DeleteBossAsync 先刪 Templates 再刪 Boss），保持 clean architecture 分層
- [ ] M-2：評估 RegisterService 依賴簡化

### 第三階段 — 安全與穩定性
- [x] H-4：AuthenticationMiddleware JWT 路徑補充 Role Claim
- [x] M-1：DbContext Commit/Rollback 防護
- [x] M-3：已確認 UnitOfWorkMiddleware 涵蓋所有 PUT/POST/DELETE 請求，TeamSlotService.UpdateAsync 透過 PUT 端點呼叫已在事務內執行

### 第四階段 — 代碼組織與前端改善
- [x] M-5：Program.cs DI 註冊分組
- [x] LOW-3：AuthAppService 抽象為介面
- [x] M-6：前端統一錯誤處理（apiClient wrapper）
- [x] LOW-4：前端 API 白名單提取至 constants

---

## 正面觀察（相較上次審查的改善）
- TeamSlotService 成功從 God Class（10 個依賴）拆分為 4 個依賴，職責清晰
- `IsInJobCategory` 已統一至 `JobCategoryHelper`，消除重複邏輯
- `SlotDateCalculator` 提取為純計算 Helper，符合 Domain 層設計
- AuthenticationMiddleware 已修復放行漏洞，token 為空時正確回傳 401
- `LoginResult.IsSuccess` flag 已加入，登入失敗語意明確
- JWT Refresh 已從 DB 取得 Player.DiscordName
- Options 類別已加上 `required` / 預設值
- 前後端 role 大小寫已統一為 `"admin"`
