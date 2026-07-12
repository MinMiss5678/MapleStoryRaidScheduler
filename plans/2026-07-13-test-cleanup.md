# 測試清理計畫：砍掉為湊覆蓋率而生的無效測試

## Context

早期為追求測試覆蓋率，新增大量「執行到但沒驗證行為」的測試（getter/setter、ctor `Assert.NotNull`、純轉發 `Verify`）。這些測試**埋 bug 也不會變紅**、還讓重構變脆。目標：依「注入一個真 bug 會不會有測試變紅」為判準，砍垃圾、保留真正測行為的測試。

**判準**：service/類別有沒有自己的邏輯（分支/映射/orchestration/演算法）？有 → 測邏輯（留）；沒有（純轉發/getter/ctor）→ 垃圾（砍）。

---

## ⚠️ 優先：先查一個「真紅燈」（不是垃圾，是可能的 bug）

`SystemConfigEntityTests` 的 3 個測試**一直失敗**，我先前誤判為「日期硬編、與本次無關」——**錯了**。它們用**固定日期**輸入（`periodStart = 2026-04-02`），是**確定性**的：

- `GetDeadlineForPeriod_ShouldReturnCorrectDay_WhenDeadlineIsMonday`：Expected `2026-04-06`（下週一）、Actual `2026-03-30`（**上一個**週一）

→ `SystemConfig.GetDeadlineForPeriod` 疑似算出**週期開始「之前」**的截止日 = **真 bug 或測試過期**。這是 Tier-3 有價值的演算法測試，**要修不是刪**。動清理前先釐清：改 code 還是改預期。

---

## Tier 1：純垃圾，整檔/整組刪（確定，已細讀）

| 目標 | 問題 | 估計數 |
|---|---|---|
| **`InfrastructureConstructorTests.cs`（整檔）** | 全是 `new X(...); Assert.NotNull`——ctor 只賦值、恆真。註解自白「確保 DI 注入路徑有覆蓋率」 | ~19 |
| **`DomainEntitiesTests.cs`（整檔）** | 全 getter/setter＋list 初始化——測 C# 語言 | 9 |
| `AdditionalCoverageTests`：`PlayerRegisterDbRow_Constructor`、`DiscordOAuthClient_Constructor` | record 屬性 / ctor NotNull | 2 |
| `InfrastructureDapperTests`：`DapperRepository_Constructor_*`×3、`DbContext_Repository_ReturnsRepositoryInstance` | ctor / NotNull | 4 |

小計 **~34 個** 零損失可刪。

## Tier 2：低價值/同義反覆，逐檔精簡（判斷後刪或合併）

- `CharacterServiceTests.GetWithDiscordNameAsync_WithBossId_ShouldPassBossIdToQuery` — 純轉發 `Verify`
- `PlayerServiceTests.UpdateRoleAsync_ShouldCallRepository` — 純轉發 `Verify`
- `AppDtoExceptionTests` 的 `BusinessException`/`ForbiddenException` 繼承測 — 編譯期已保證
- 各 Service 的 `Get*_ShouldReturn*` / `Create*_ShouldReturnId` — 「回傳 mock 給的值」pass-through；重複的精簡、有驗內容的可留

## Tier 3：真行為，保留（資產）

- **演算法**：`TeamSlotMergeService*`、`ScheduleService*`（重排）、`SlotDateCalculatorTests`、`UtilsSqlBuilderTests`、`AdditionalCoverageTests` 內 SqlExpressionVisitor/QueryBuilder（SQL 轉譯邊界、拋例外）、`SystemConfigEntityTests`（修好後）
- **分支邏輯**：各 Service 的 `*ShouldThrowNotFound*`（affected==0 → 拋）、`BossService` cascade delete、`CharacterService` 刪除順序、`PlayerService` Create 存在檢查、`TeamSlotCharacterService` 無週期跳過、`PeriodService` UTC+8 轉換
- **交易/UoW**：`InfrastructureDapperTests` 的 DbContext Begin/Commit/Rollback/重複 Begin、UnitOfWork、`AdditionalCoverageTests.DbContext_ExecuteAfterCommit_Throws`
- **型別處理**：`TimeOnlyTypeHandler` Parse 各型別、`BigIntStringConverter`（long/ulong↔string 精度）
- **認證**：`Jwt`/`Auth`/`AuthApp`/`Session`（撤銷）
- **計算屬性**：`AppDtoExceptionTests.LoginResult.IsSession/IsJwt`

## 尚未細讀、需逐檔確認再處理（多為 Service pass-through＋NotFound 分支）

`SystemConfigServiceTests`、`TeamSlotServiceTests`、`TeamSlotServiceQueryTests`、`RegisterQueryServiceTests`、`RegisterServiceTests`、`RegisterServiceUpdateDeleteTests`、`PeriodQueryTests`、`TeamSlotMergeServiceTests`
→ 套同一判準：**NotFound/分支/orchestration 留、純 pass-through getter 與純轉發 Verify 砍**。

---

## 執行步驟

1. **先查 `SystemConfigEntityTests` 紅燈**（改 code 或改預期），讓基線全綠。
2. 刪 Tier 1（兩個整檔 + `AdditionalCoverageTests`/`InfrastructureDapperTests` 內指定方法）。
3. 逐檔處理 Tier 2 與「尚未細讀」清單（砍純轉發/pass-through）。
4. 重構 verbose 的 Tier 2 有回傳值者 → 行為斷言（斷言資料流回，順帶保證參數對）。

## 驗證

- 每步後 `dotnet test`：**通過數下降但不應有任何 Tier-3 行為測試消失**；紅燈只該剩「刻意待修」的項目。
- 心態轉換：覆蓋率當「哪塊漏測」的體檢，不當達標 KPI。刪完覆蓋率%會掉，但**抓得到 bug 的密度上升**——這才是重點。

## 未決問題

- `SystemConfigEntityTests` 是 code bug 還是測試過期？（決定 code 改法）
- `DomainEntitiesTests.Boss_ShouldHaveCorrectDefaultValues`（RoundConsumption 預設 1）要不要留一個守業務預設？（marginal，傾向刪）
- Tier 2 的 pass-through getter 要「全砍」還是「保留有驗內容的一個代表」？（一致性 vs 保守）
