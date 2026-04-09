# TeamSlotService 重構計畫

## 現況分析

### 基本資訊
- **檔案**：`Infrastructure/Services/TeamSlotService.cs`
- **行數**：685 行
- **建構函式注入**：10 個依賴
- **公開介面**：`ITeamSlotService`（4 個方法）
- **問題**：職責過重（God Class），混合查詢、CRUD、自動分配、隊伍合併四種職責

### 方法清單與職責分群

| 行數 | 方法 | 職責群組 |
|------|------|----------|
| 43-73 | `GetByBossIdAsync` | 查詢 |
| 75-105 | `GetByDiscordIdAsync` | 查詢 |
| 107-193 | `UpdateAsync` | CRUD 更新 |
| 195-227 | `AutoAssignAsync` | 自動分配（入口） |
| 229-233 | `IsAlreadyAssigned` | 自動分配（輔助） |
| 235-257 | `FindMatchingTeam` | 自動分配（輔助） |
| 259-274 | `AssignToSlot` | 自動分配（輔助） |
| 277-302 | `CreateNewTeamAsync` | 自動分配（輔助） |
| 304-325 | `GetBestAvailability` | 時段計算（共用） |
| 327-355 | `GetNextSlotDate` | 時段計算（共用） |
| 357-388 | `IsTimeInAvailability` | 時段計算（共用） |
| 390-400 | `CreateTeamMembers` | 自動分配（輔助） |
| 403-417 | `FillPlayer` | 自動分配（輔助） |
| 419-429 | `MergeTeams` | 合併（入口） |
| 431-519 | `TryMergeTeamsAsync` | 合併（核心） |
| 521-590 | `FindCommonDateTime` | 合併（輔助） |
| 592-629 | `TryMatchTemplate` | 合併（輔助） |
| 631-668 | `PerformMerge` | 合併（輔助） |
| 670-679 | `IsInJobCategory` | 職業分類（共用） |
| 681-684 | `ToIsoWeekday` | 工具方法 |

---

## 識別的問題

### P-1 AssignToSlot 與 FillPlayer 重複
- **位置**：第 259-274 行 vs 第 403-417 行
- **問題**：兩個方法幾乎相同，`AssignToSlot` 多設了 `IsManual = false`，其餘邏輯完全一致
- **影響**：修改一處忘記另一處會造成不一致

### P-2 查詢映射邏輯重複
- **位置**：第 43-73 行 vs 第 75-105 行
- **問題**：`GetByBossIdAsync` 和 `GetByDiscordIdAsync` 的 `GroupBy + Select` 映射完全相同，僅資料來源不同
- **影響**：新增欄位需改兩處

### P-3 直接注入 DbContext
- **位置**：第 22 行、第 202 行
- **問題**：僅在 `AutoAssignAsync` 中用 `_dbContext.Repository<PlayerDbModel>().GetByIdAsync()` 取得玩家資料，繞過 Repository 抽象
- **影響**：破壞分層架構，測試困難

### P-4 TryMergeTeamsAsync 縮排不一致
- **位置**：第 431-519 行
- **問題**：方法體沒有正確縮排（頂格寫），與其他方法風格不一致
- **影響**：可讀性差

### P-5 GetNextSlotDatePublic 測試用包裝
- **位置**：第 327 行
- **問題**：為了測試暴露 private 方法的 public wrapper
- **影響**：污染公開 API（已在 LOW-2 計畫改為 internal）

### P-6 十個建構函式依賴
- **問題**：`ITeamSlotRepository`, `ITeamSlotQuery`, `ITeamSlotCharacterRepository`, `IPlayerAvailabilityRepository`, `IPeriodQuery`, `ICharacterQuery`, `IBossRepository`, `IJobCategoryRepository`, `IUnitOfWork`, `DbContext`
- **影響**：違反單一職責原則，測試 setup 複雜

---

## 重構方案

### 目標架構

將 `TeamSlotService` 拆分為以下四個類別：

```
TeamSlotService (瘦身後，僅保留查詢 + CRUD)
├── GetByBossIdAsync
├── GetByDiscordIdAsync
├── UpdateAsync
└── MapToTeamSlots (提取的共用映射方法)

TeamSlotAutoAssignService (新建，自動分配)
├── AutoAssignAsync
├── IsAlreadyAssigned
├── FindMatchingTeam
├── CreateNewTeamAsync
├── CreateTeamMembers
└── FillPlayer (合併 AssignToSlot)

TeamSlotMergeService (新建，隊伍合併)
├── MergeTeamsAsync
├── TryMergeTeamsAsync
├── FindCommonDateTime
├── TryMatchTemplate
├── PerformMerge
└── IsInJobCategory (或移至 Domain Helper)

SlotDateCalculator (新建，時段計算共用邏輯)
├── GetBestAvailability
├── GetNextSlotDate
├── IsTimeInAvailability
└── ToIsoWeekday
```

### 依賴分配

| 類別 | 需要的依賴 |
|------|-----------|
| `TeamSlotService` | `ITeamSlotRepository`, `ITeamSlotQuery`, `ITeamSlotCharacterRepository`, `IPeriodQuery` |
| `TeamSlotAutoAssignService` | `ITeamSlotRepository`, `ITeamSlotCharacterRepository`, `IPeriodQuery`, `ICharacterQuery`, `IBossRepository`, `IPlayerRepository`（取代 DbContext）, `SlotDateCalculator`, `TeamSlotMergeService` |
| `TeamSlotMergeService` | `ITeamSlotRepository`, `ITeamSlotCharacterRepository`, `IPeriodQuery`, `IBossRepository`, `IPlayerAvailabilityRepository`, `IJobCategoryRepository`, `SlotDateCalculator` |
| `SlotDateCalculator` | 無依賴（純計算，static 或注入） |

---

## 執行步驟

### 第一步 — 消除重複（低風險，不改架構）
1. [ ] 合併 `AssignToSlot` 和 `FillPlayer` 為單一方法 `FillSlot`
2. [ ] 提取 `MapToTeamSlots` 私有方法，消除 `GetByBossIdAsync` / `GetByDiscordIdAsync` 的映射重複
3. [ ] 修正 `TryMergeTeamsAsync` 縮排
4. [ ] 執行現有測試確認無回歸

### 第二步 — 提取 SlotDateCalculator（無外部依賴）
1. [ ] 建立 `Domain/Helpers/SlotDateCalculator.cs`（純計算，無 I/O）
2. [ ] 搬移 `GetBestAvailability`、`GetNextSlotDate`、`IsTimeInAvailability`、`ToIsoWeekday`
3. [ ] `TeamSlotService` 改為呼叫 `SlotDateCalculator`
4. [ ] 將 `GetNextSlotDatePublic` 改為 `internal`（配合 `InternalsVisibleTo`）
5. [ ] 補充 `SlotDateCalculator` 單元測試
6. [ ] 執行測試確認無回歸

### 第三步 — 提取 TeamSlotMergeService
1. [ ] 建立 `Application/Interface/ITeamSlotMergeService.cs`
2. [ ] 建立 `Infrastructure/Services/TeamSlotMergeService.cs`
3. [ ] 搬移 `MergeTeams`、`TryMergeTeamsAsync`、`FindCommonDateTime`、`TryMatchTemplate`、`PerformMerge`
4. [ ] 將 `IsInJobCategory` 移至 `Domain/Helpers/JobCategoryHelper.cs`（已在 H-2 計畫中，若未完成則一併處理）
5. [ ] `TeamSlotService.AutoAssignAsync` 改為呼叫 `ITeamSlotMergeService.MergeTeamsAsync`
6. [ ] 註冊 DI
7. [ ] 補充合併邏輯單元測試
8. [ ] 執行測試確認無回歸

### 第四步 — 提取 TeamSlotAutoAssignService
1. [ ] 建立 `Application/Interface/ITeamSlotAutoAssignService.cs`
2. [ ] 建立 `Infrastructure/Services/TeamSlotAutoAssignService.cs`
3. [ ] 搬移 `AutoAssignAsync`、`IsAlreadyAssigned`、`FindMatchingTeam`、`CreateNewTeamAsync`、`CreateTeamMembers`、`FillSlot`
4. [ ] 移除 `TeamSlotService` 中的 `DbContext` 依賴（改用 `IPlayerRepository.GetByDiscordIdAsync`）
5. [ ] 更新 `ITeamSlotService` 介面：移除 `AutoAssignAsync`，由 `ITeamSlotAutoAssignService` 提供
6. [ ] 更新 Controller / 呼叫端注入
7. [ ] 註冊 DI
8. [ ] 執行測試確認無回歸

### 第五步 — 清理與驗證
1. [ ] 確認 `TeamSlotService` 建構函式依賴降至 4 個以下
2. [ ] 移除 `IUnitOfWork` 若未使用
3. [ ] 全量測試通過
4. [ ] 更新 `docs/refactoring-plan-2026-04-10.md` 標記完成

---

## 預期成果

| 指標 | 重構前 | 重構後 |
|------|--------|--------|
| TeamSlotService 行數 | 685 | ~150 |
| 建構函式依賴數 | 10 | 4 |
| 類別數 | 1 | 4 |
| 重複方法 | 2 組 | 0 |
| DbContext 直接依賴 | 有 | 無 |
| 單元測試覆蓋 | 部分 | 各類別獨立可測 |

---

## 風險與注意事項

1. **介面變更影響**：`ITeamSlotService` 移除 `AutoAssignAsync` 後，需確認所有呼叫端（Controller、ScheduleService、BackgroundJob）更新注入
2. **DI 註冊**：新增的 Service 需在 `Program.cs` 註冊
3. **交易邊界**：合併操作涉及多個 Repository 寫入，需確認 UoW/Middleware 仍能正確包裹
4. **測試遷移**：`TeamSlotServiceTests.cs` 中的測試需依職責分配到對應的測試類別
