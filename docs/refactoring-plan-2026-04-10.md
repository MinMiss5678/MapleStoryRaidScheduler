# 重構計畫 — 2026-04-10

## 審查範圍
- Git range: `601542d..e33a856`
- 審查檔案：約 55 個核心檔案（Application、Domain、Infrastructure、Presentation.WebApi 全層）
- **前端審查**：`web/` 目錄（Next.js App Router + TypeScript），涵蓋 7 個 service、9 個頁面、4 個 type 定義

## 前端 API 使用分析

前端透過 `app/api/[...path]/route.ts` 統一代理至後端，白名單：`auth, character, boss, register, schedule, teamslot, period, systemconfig, jobcategory`。

### 前端實際呼叫的端點
| Service | 端點 | HTTP Method |
|---------|------|-------------|
| authService | `/api/auth/login` | POST |
| authService | `/api/auth/logout` | POST |
| bossService | `/api/boss/GetAll` | GET |
| bossService | `/api/boss/{bossId}/Templates` | GET |
| bossService | `/api/boss/Templates/{templateId}` | DELETE |
| bossService | `/api/boss/Templates` | POST |
| bossService | `/api/boss/Templates/{id}` | PUT |
| bossService | `/api/boss/{bossId}` | DELETE |
| bossService | `/api/boss` | POST |
| bossService | `/api/boss/{id}` | PUT |
| bossService | `/api/JobCategory/GetJobMap` | GET |
| characterService | `/api/character/GetWithDiscordName` | GET |
| characterService | `/api/character` | POST |
| characterService | `/api/character/{id}` | PUT / DELETE |
| periodService | `/api/period/GetByNow` | GET |
| registerService | `/api/register` | GET / POST / PUT |
| registerService | `/api/register/GetLast` | GET |
| registerService | `/api/register/{id}` | DELETE |
| registerService | `/api/register/GetByQuery` | GET |
| scheduleService | `/api/teamSlot?bossId=` | GET |
| scheduleService | `/api/schedule/AutoScheduleWithTemplate` | POST |
| scheduleService | `/api/teamSlot` | PUT |
| scheduleService | `/api/teamSlot/GetByDiscordId` | GET |
| systemConfigService | `/api/SystemConfig` | GET / POST |

### 前端未使用的後端功能（確認為孤立）
- `IScheduleService.GetPartiesAsync` — 前端無對應呼叫
- `IScheduleService.JoinTeamAsync` — 前端無對應呼叫
- `IScheduleService.FinalizeScheduleAsync` — 前端無對應呼叫

---

## Critical（立即修復）

### C-1 AuthenticationMiddleware 放行漏洞
- **位置**：`Presentation.WebApi/Middleware/AuthenticationMiddleware.cs:117-134`
- **問題**：無 `[AllowAnonymous]` 且 token 為空時直接放行，所有端點可被繞過
- **修法**：claims 為空且沒有 `[AllowAnonymous]` attribute 時一律回傳 401

### C-2 AuthAppService 登入失敗語意不明確
- **位置**：`Application/Services/AuthAppService.cs:49-53`
- **問題**：角色解析失敗時回傳空 `LoginResult`，呼叫方無法判斷登入失敗
- **修法**：加入 `IsSuccess` flag 或改為拋出明確例外

### C-3 JWT Refresh 時 DiscordUser.Name 遺失
- **位置**：`Infrastructure/Services/AuthService.cs`（RefreshToken 方法）
- **問題**：刷新 token 時 `DiscordUser.Name` 為空，名稱 Claim 遺失
- **修法**：從 DB 取得 Player.DiscordName 再建立新 JWT

### C-4 SystemConfig.RegistrationDeadline setter 為空
- **位置**：`Domain/Entities/SystemConfig.cs:23`
- **問題**：`set { }` 永遠忽略寫入，所有賦值靜默失效
- **修法**：移除此 property，或加上 `[Obsolete("Use GetDeadlineForPeriod()")]` 並讓 setter 拋出 `NotSupportedException`

---

## High（架構問題，近期修復）

### H-1 TeamSlotService 直接注入 DbContext
- **位置**：`Infrastructure/Services/TeamSlotService.cs:28-29`（第 202 行使用）
- **問題**：Service 層直接存取 DbContext，繞過 Repository 抽象；建構函式已有 10 個注入，職責過重
- **修法**：在 `IPlayerRepository` 增加 `GetByDiscordIdAsync(ulong discordId)`，替換 DbContext 直接依賴

### H-2 IsInJobCategory 邏輯重複且不一致
- **位置**：`Infrastructure/Services/TeamSlotService.cs:671`、`Infrastructure/Services/ScheduleService.cs:196`
- **問題**：兩版本行為不同（一個支援 `"任意"` 特殊字串，一個支援逗號/斜線分隔），可能造成排程差異
- **修法**：提取至 Domain Service 或共用 static Helper，統一邏輯後替換兩處呼叫

### H-3 未使用的 IUnitOfWork 注入
- **位置**：`Infrastructure/Services/RegisterService.cs:18`、`Infrastructure/Services/BossService.cs`
- **問題**：注入了 `IUnitOfWork` 但從未呼叫，依賴 Middleware 全域 UoW，造成語意混淆
- **修法**：移除兩個 Service 中未使用的 `IUnitOfWork` 注入及建構函式參數

### H-4 Nullable period 傳入非 nullable 介面
- **位置**：`Infrastructure/Services/TeamSlotService.cs:78`
- **問題**：`GetByNowAsync()` 回傳可能為 null 的 period 直接傳入介面（CS8604 警告）
- **修法**：加上 null 判斷，提前 return 空集合

### H-5 RegistrationDeadlineJob 有 race condition
- **位置**：`Infrastructure/BackgroundJobs/RegistrationDeadlineJob.cs`
- **問題**：`_changeCts` 在主迴圈和 event handler（不同執行緒）間沒有保護
- **修法**：使用 `Interlocked.Exchange` 或 `volatile` 確保原子替換

### H-6 時區計算不一致
- **位置**：`Infrastructure/BackgroundJobs/WeeklyPeriodJob.cs` vs `RegistrationDeadlineJob.cs`
- **問題**：一個用 `DateTimeOffset.UtcNow`，一個用本地 `DateTimeOffset.Now`，計算「下週四 00:00」偏差最多 8 小時
- **修法**：統一使用 `DateTimeOffset.UtcNow`

### H-7 TeamSlotCharacterRepository 殘留未使用欄位
- **位置**：`Infrastructure/Repositories/TeamSlotCharacterRepository.cs:13-14`
- **問題**：`_periodQuery` 宣告但未初始化（CS0169 + CS8618），殘留遺留程式碼
- **修法**：刪除宣告與對應建構函式參數

### H-8 PlayerRegisterQuery 使用 dynamic 接收 SQL 結果
- **位置**：`Infrastructure/Query/PlayerRegisterQuery.cs:65-87`
- **問題**：`dynamic` 接收結果再手動 cast，欄位名稱拼錯或型別變化會在執行時期拋出例外
- **修法**：建立對應的 `PlayerRegisterDbRow` record 取代 `dynamic`

---

## Medium（代碼品質）

### M-1 PeriodQuery.GetByNowAsync 可能取到未來 Period
- **位置**：`Infrastructure/Query/PeriodQuery.cs`（GetByNowAsync）
- **問題**：`ORDER BY StartDate DESC LIMIT 1` 若已建立未來 period 會取到錯誤結果
- **修法**：加上 `WHERE StartDate <= NOW()` 條件

### M-2 TeamSlotRepository.UpdateAsync 刪除重插
- **位置**：`Infrastructure/Repositories/TeamSlotRepository.cs:189-207`
- **問題**：先刪除所有 `TeamSlotCharacter` 再逐一插入，高並發下資料短暫不完整
- **修法**：改用 PostgreSQL upsert（`INSERT ... ON CONFLICT DO UPDATE`）

### M-3 rounds >= 7 硬編碼
- **位置**：`Infrastructure/Services/ScheduleService.cs:99`
- **問題**：`7` 是週次數，遊戲改版時需手動更新
- **修法**：讀自 `Boss.RoundConsumption` 或系統設定

### M-4 Options 類別缺少 required 或預設值
- **位置**：`Application/Options/AppOptions.cs`、`Application/Options/DiscordOptions.cs`
- **問題**：設定欄位無 `required` 修飾詞或預設值，設定檔缺 key 時靜默得到 null
- **修法**：加上 `required` 關鍵字或 `string AppUrl { get; set; } = string.Empty`，並在 `PostConfigure` 驗證非空

### M-5 Domain Entity 包含 UI 操作指令
- **位置**：`Domain/Entities/Register.cs`（DeleteCharacterRegisterIds）、`Domain/Entities/TeamSlot.cs`（DeleteTeamSlotCharacterIds、BossName、PeriodId）
- **問題**：PATCH 操作指令混入 Domain Entity，違反 DDD，TeamSlot 同時承擔 Entity/ViewModel/Command 三個職責
- **前端佐證**：前端 `RegisterFormState.deleteCharacterRegisterIds` 和 `scheduleService.saveSchedule` 的 `deleteTeamSlotIds` 參數直接對應這些欄位，確認此設計是為了前端 PATCH 操作而存在
- **修法**：建立 `RegisterUpdateCommand`、`TeamSlotUpdateCommand` DTO，Domain Entity 只保留純領域狀態；前端 types 無需變動（DTO 欄位名稱保持一致）

### M-6 BossService 零業務邏輯
- **位置**：`Infrastructure/Services/BossService.cs`
- **問題**：所有方法純委派給 `IBossRepository`，注入了 IUnitOfWork 也不用，是典型貧血 Service
- **修法**：評估移除 BossService，Controller 直接注入 `IBossRepository`；或將 Boss+Template+Requirement 的複合操作事務控制移至此 Service

---

## Low（命名、風格）

- **LOW-1**：`Program.cs` 重複兩次 `AddOptions<JwtOptions>()` 呼叫，整合為一次鏈式呼叫
- **LOW-2**：`TeamSlotService.GetNextSlotDatePublic` 是為測試暴露的 public wrapper，改為 `internal` + `[assembly: InternalsVisibleTo("Test")]`
- **LOW-3**：`DbContext.Commit()/Rollback()` 設置 `Transaction = null` 後若再次寫入會靜默以 AutoCommit 執行，考慮加上防護或文件說明
- **LOW-4**：`PlayerRegisterSchedule.Weekdays`、`Timeslots` 欄位從未被賦值，確認是廢棄欄位後刪除
- **LOW-5**：`GetDelayUntilNextReset` 中 `now.TimeOfDay.TotalHours >= 0` 永遠為 true，應改為明確的時間比較

---

## 執行計畫

### 第一週 — Critical + 高風險安全性
- [x] C-1：修復 AuthenticationMiddleware 放行漏洞
- [x] C-2：AuthAppService 登入失敗加入 `IsSuccess` flag
- [x] C-3：JWT Refresh 時從 DB 取得 Player.DiscordName 再建立新 JWT
- [x] C-4：移除 / 修復 SystemConfig.RegistrationDeadline 假 setter
- [x] H-5：用 `Interlocked.Exchange` 保護 `_changeCts`
- [x] H-4：修復 nullable period 提前 return

### 第二週 — 架構清理
- [x] H-1：TeamSlotService 改注入 IPlayerRepository（需同步新增介面方法）
- [x] H-3：移除 RegisterService / BossService 未使用的 IUnitOfWork 注入
- [x] H-7：清理 TeamSlotCharacterRepository 殘留欄位
- [x] H-6：統一使用 `DateTimeOffset.UtcNow`
- [x] H-8：PlayerRegisterQuery 以強型別 record 取代 dynamic

### 第三週 — DDD / 代碼品質
- [x] H-2：統一 `IsInJobCategory` 邏輯，ScheduleService 委派至 `JobCategoryHelper`
- [x] M-1：修正 `PeriodQuery.GetByNowAsync` SQL（加 `StartDate <= NOW()`）
- [ ] M-5：將 Delete 指令從 Domain Entity 分離為 Command DTO
- [x] M-4：Options 類別加上 `required` / 預設值
- [x] M-3：`rounds >= 7` 改讀自 `Boss.RoundConsumption`

### Low 修復
- [x] LOW-1：`Program.cs` 合併重複 `AddOptions<JwtOptions>()` 為一次呼叫
- [x] LOW-2：`GetNextSlotDatePublic` 改為 `internal` + `InternalsVisibleTo("Test")`
- [ ] LOW-3：`DbContext.Commit()/Rollback()` 防護（待評估）
- [x] LOW-4：刪除 `PlayerRegisterSchedule.Weekdays/Timeslots` 廢棄欄位
- [x] LOW-5：修正 `GetDelayUntilNextReset` 永真條件

### TeamSlotService 重構（God Class 拆分）
- [x] 合併 `AssignToSlot` / `FillPlayer` 為 `FillSlot`，消除重複
- [x] 提取 `MapToTeamSlots` 共用映射方法
- [x] 修正 `TryMergeTeamsAsync` 縮排
- [x] 提取 `Domain/Helpers/SlotDateCalculator.cs`（純計算，無 I/O）
- [x] `GetNextSlotDatePublic` 改為 `internal static`
- [x] 提取 `TeamSlotMergeService`（合併邏輯）+ `ITeamSlotMergeService` 介面
- [x] `IsInJobCategory` 委派至 `JobCategoryHelper`
- [x] 提取 `TeamSlotAutoAssignService`（自動分配）+ `ITeamSlotAutoAssignService` 介面
- [x] 移除 `DbContext` 直接依賴，改用 `IPlayerRepository`
- [x] `ITeamSlotService` 移除 `AutoAssignAsync`
- [x] `TeamSlotService` 建構函式依賴從 10 降至 4
- [x] 更新 DI 註冊、Controller、測試
- [x] 全量測試通過（33/33）

### 待確認後處理
- [x] `PlayerRegisterSchedule.Weekdays/Timeslots` — **已確認為廢棄欄位，已刪除**
- [ ] `TeamSlotController` `"admin"` 小寫 vs `AuthAppService` `"Admin"` 大寫 — 前端 `RolesProvider` 使用 JWT claims 中的 role，需確認後端 role 比對是否 case-insensitive
- [x] `IScheduleService` 缺少的三個方法（`GetPartiesAsync`、`JoinTeamAsync`、`FinalizeScheduleAsync`）— **已確認為孤立功能**，前端無任何呼叫，可安全移除介面定義
- [x] `BossController` 的 `POST`（新增 Boss）和 `PUT/{id}`（更新 Boss）— **前端已新增 `createBoss` 和 `updateBoss` 方法**，不再孤立

---

## 正面觀察
- UoW + DbContext 重構（Repository 改由 DbContext 注入）大幅減少重複程式碼，模式一致
- CQRS-lite 查詢/命令分離邊界清晰
- `ConfigChangeNotifier` event-driven 設計優雅解耦 Background Job 和 Config Service
- `GetDeadlineForPeriod` 放在 Entity 方法中，符合 rich entity / DDD 精神
- `TryMatchTemplate`、`FindCommonDateTime` 演算法邊界情況處理完整
- Docker secrets 以 `PostConfigure` 讀取，生產環境安全性到位
- **前端架構良好**：API proxy 統一代理 + 白名單機制、React Query hooks 封裝查詢邏輯、service 層與 types 分離清晰
- **前後端 API 契約一致**：所有前端呼叫的端點在後端 Controller 均有對應，無斷裂的 API 呼叫
