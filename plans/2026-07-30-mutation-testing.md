# 變異測試（Mutation Testing）計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 目標

現有 unit test 用 line/branch coverage 衡量（`coverage-merged/Summary.txt`：line 54.6%、branch 69.4%），但**覆蓋率只代表「這行有跑到」，不代表「跑到時有斷言驗證行為」**。變異測試（Stryker）在原始碼注入小改動（mutant，例如 `>=` 改 `>`、`&&` 改 `||`、刪掉一行），重跑測試：測試會失敗 → mutant 被殺（測試有效）；測試照樣全過 → mutant 存活（這段邏輯其實沒被真的驗證到）。

目的是在最近密集重構的核心邏輯上（[[teamslot-rich-aggregate]]、[[teamslot-admin-concurrency-control]]、auto-assign 引擎）找出「測試綠燈但其實沒驗證到」的假安全感，為後續繼續動這些檔案（尤其併發/鎖相關）建立可信的回歸防護網。

## 範圍（依價值排序）

1. **併發/排程核心（★ 最高價值）**
   - `Infrastructure/Services/RegisterService.cs`（175 行）+ `TeamSlotAutoAssignService.cs`（155 行）— auto-assign 引擎
   - `Infrastructure/Services/TeamSlotService.cs`（230 行）+ `TeamSlotMergeService.cs`（267 行）— 排團/合併/編輯鎖
   - `Infrastructure/Services/ScheduleService.cs`（316 行）— 排程核心
2. **富領域模型**
   - `Domain/Entities/TeamSlot.cs`（100 行）— 剛做完的 rich aggregate 重構，邏輯搬進 entity 後測試有沒有跟上，值得驗證
3. **視 Phase 1 結果決定要不要擴**：其餘 `Infrastructure/Services/*`（Auth/Session/Jwt/SystemConfig 等）

### 非範圍（YAGNI）
- **Presentation.WebApi controllers**：薄，邏輯已在 service 層測，mutant 多半是無意義的存活。
- **Infrastructure/Dapper repositories**：純 SQL 字串組裝，mutation testing 對這類程式碼訊噪比低；正確性已由 integration test 打真 DB 驗證。
- **DTOs / 無邏輯的純資料 entity**。
- **前端（Vitest）**：Stryker 也支援 JS/TS，但先把 .NET 核心邏輯這塊做出價值再評估要不要擴，不預先付兩套環境成本。
- **不排進 CI 擋 PR**：跑一輪要重複執行測試上百次，太慢，不適合當 PR gate。先本機/手動跑，之後有需要再評估要不要做成 nightly job。

## 關鍵決策

### 工具：Stryker.NET
- .NET 生態唯一成熟選擇，原生支援 `dotnet test` + xUnit + coverlet，出 HTML report。
- 安裝：`dotnet tool install -g dotnet-stryker`（repo 目前未裝，需先裝）。

### 用哪個測試專案跑
- 用 `Test/Test.csproj`（純 unit test，Moq mock 掉相依，跑起來快）。
- **排除 `Test.Integration`**：mutation run 要重複執行整個測試套件上百次，integration test 每次起 Testcontainers（真 Postgres/Redis）太慢，會讓單輪跑到不可行的時間量級。

### 執行方式
- 針對單一 project 跑，不對全 solution 跑（範圍外的 controller/repository 會稀釋分數又拖時間）：
  ```
  dotnet stryker --project Infrastructure.csproj --test-project ../Test/Test.csproj
  ```
- 用 `stryker-config.json` 的 mutate globs 鎖定「範圍」清單裡的檔案，其餘 `!` 排除。
- 第一輪不設 break threshold（純觀察分數，不擋任何東西），確定 baseline 後才考慮要不要設門檻。

### 怎麼判讀存活 mutant
- Mutation score 預期會**低於**現有 line coverage（54.6%）——這個落差正是要抓的東西：covered 不代表有斷言驗證行為。
- 存活的 mutant 分兩種，跑完要人工過一輪分類：
  - **真的漏測** → 記下來，開 follow-up 補測試（不在本 plan 內動手修）。
  - **等價變異（equivalent mutant）**：改了但外部行為其實沒變（例如邊界值測不到差異、或死碼路徑）→ 在 Stryker config 標記忽略，不用硬補測試。

## 驗收

- [x] Stryker 能在本機對「範圍」清單的核心檔案跑完一輪，產出 HTML report（`Infrastructure/../stryker-output/reports/mutation-report.html`、`Domain/../stryker-output-domain/reports/mutation-report.html`）
- [x] 記錄 baseline mutation score（依檔案），跟現有 line coverage 對照
- [x] auto-assign / TeamSlot 編輯鎖相關的存活 mutant 過一輪人工分類（漏測 vs 等價）
- [x] 漏測 follow-up 清單列在下方（本 plan 內不動手補測試）

### Baseline 結果（2026-07-30，Stryker 4.16.0）

| 檔案 | Mutant 數（有效） | Killed | Survived | Mutation Score |
|---|---|---|---|---|
| `RegisterService.cs`（POC，單獨跑） | 40 | 30 | 10 | 71.43% |
| 4 檔合計：`RegisterService`+`TeamSlotAutoAssignService`+`TeamSlotService`+`TeamSlotMergeService`+`ScheduleService` | 350 | 214 | 130（+6 timeout） | **57.59%** |
| `Domain/Entities/TeamSlot.cs` | 43 | 35 | 8 | **81.40%** |

跟 line coverage 54.6% 對照：整體業務邏輯層 mutation score（57.59%）跟 line coverage 差不多，代表大部分「有跑到的行」也真的有斷言驗證；但 130 個存活 mutant 裡藏了幾個跟本 plan 動機（併發防護信心）直接相關的真缺口，見下方。

### 存活 mutant 分類

**真的漏測（優先序由高到低）**：

1. **`TeamSlotAutoAssignService.cs:44`（★★★ 最高優先）**：`await _registrationLock.AcquireAutoAssignLockAsync(register.PeriodId);` 整行刪掉，unit test 全過。代表**沒有任何 unit test 直接驗證 auto-assign 有先取鎖**——這正是本 plan 想驗證的「併發防護有沒有真的被測住」，目前答案是沒有。對照組：`TeamSlotService.cs:135` 的 `AcquireTeamSlotEditLockAsync`（TeamSlot 編輯鎖）**沒有**出現在存活清單，代表那顆鎖有被測試殺掉——兩個鎖的測試品質不對等，auto-assign 這顆該補。
2. **`TeamSlotAutoAssignService.cs:77`（★★ 高）**：`teamSlots.Add(newTeam);` 刪掉仍過。這行是「新建的隊伍要加回本地 list」——若同一玩家一次報名兩隻角色排同一王，第一隻角色觸發建新隊、第二隻角色應該併進同一隊而不是又開一隊。這行沒測到代表**同批多角色報名可能重複建隊**的迴歸抓不到。
3. **`TeamSlotAutoAssignService.cs:62`**：`character == null || IsAlreadyAssigned(...)` 存活（`||` 改 `&&` 仍過）。代表「角色已被分配過、跳過」這個去重分支沒有獨立測到。
4. **`TeamSlot.cs:56`**：`filledCount > Capacity` 存活（改成 `>=` 仍過）。`SetRoster` 不變式的邊界值——**剛好等於容量（合法）** 這個 case 沒測到，只測了超過容量（不合法）。跟「TeamSlot 不超員」這條核心不變式直接相關，建議補。
5. **`TeamSlot.cs:58`**：重複玩家偵測的 `Any` 改 `All` 仍過，代表沒有「多筆裡只有一組重複、其餘不重複」的混合案例，只測了全有或全無。
6. **系統性 pattern（RegisterService.cs + TeamSlotAutoAssignService.cs 多處 Object-initializer / Statement mutant 存活）**：大量 mock 驗證用 `It.IsAny<T>()` 只驗證「有被呼叫」，沒驗證**傳進去的欄位值**（例如 `CharacterId ?? string.Empty`、`FillSlot` 內容、新建 `TeamSlotCharacter` 的屬性）。不是單一 bug，是測試寫法的通病——之後補測試優先用 `Moq.Callback` 或 `It.Is<T>(...)` 抓實際值，而非只驗證呼叫次數。

**等價變異（不用補測試）**：
- `TeamSlotAutoAssignService.cs:69,148` 的 `DiscordName = "", Job = ""` 佔位字串——緊接著就被 `FillSlot()` 覆蓋，測不出差異是預期的。
- `TeamSlot.cs` / `TeamSlotMergeService.cs` 大量 exception message 的 `$"..."` → `$""` 字串變異——測試用 `Assert.Throws<DomainException>` 只驗證型別、不驗證訊息內容，屬於刻意的測試邊界（訊息文字改動不該讓測試炸），不建議為了殺這些 mutant 去斷言硬編碼的中文錯誤訊息字串。

### Follow-up（不在本 plan 內動手，先列出）
- [x] 補一個 unit test：`TeamSlotAutoAssignService.AutoAssignAsync` 驗證 `_registrationLock.AcquireAutoAssignLockAsync` 有被呼叫。→ `Test/TeamSlotServiceTests.cs` 的 `AutoAssignAsync_ShouldAcquireAutoAssignLock_ForRegisterPeriod`。
- [x] 補一個 unit test：同一 register 兩隻角色排同一王、目前無現成隊伍 → 驗證只建一隊、第二隻角色併入而非重複建隊。→ `AutoAssignAsync_MultipleCharactersSameBoss_ShouldJoinSameNewlyCreatedTeam`。
- [x] 補一個 unit test：`TeamSlot.SetRoster` 剛好等於容量（`filledCount == Capacity`）應該成功，不丟例外。→ `Test/TeamSlotAggregateTests.cs` 的 `SetRoster_Succeeds_WhenFilledCountExactlyEqualsCapacity`。
- [x] `TeamSlotService.cs` 剩餘存活 mutant 分類時發現一個**授權邏輯 bug**（非漏測，是 production code 本身錯）——見下方「額外發現」。已修復 + 補回歸測試。
- [x] `TeamSlotMergeService.cs:102` 邊界值（合併後人數剛好等於容量應該成功，只測了超過）→ `Test/TeamSlotMergeServiceMergeTests.cs` 的 `MergeTeamsAsync_ShouldMergeTeams_WhenCombinedCountExactlyEqualsCapacity`（驗證後確認這條**不是 bug**，邏輯本來就對，純粹補測試殺 mutant）。
- [x] `TeamSlotMergeService.cs` 全部 34 個存活 mutant 已粗看分類，多數屬於等價變異（`Concat`→`Except` 因 `TeamSlotCharacter` 沒 override `Equals` 用參考相等、實質等價；`for` 迴圈邊界因存取寫在內層迴圈內、外層邊界改了也不影響實際執行範圍）或測試深度不足（3+ 隊多輪合併迴圈、跨隊重複玩家的部分重疊情境）；沒再發現第二個像 `TeamSlotService.cs:195` 那種邏輯 bug，這批剩餘的不逐一補測試。
- [x] `ScheduleService.cs` 全部 47 個存活 mutant 已看完，同樣沒發現第二個真 bug。挑出兩個「程式碼本來就對、但邊界窄到目前測試測不到」的補了：
  - `L75` 保留隊成員扣場數用 `-=`，但沒測扣完後的確切數值——若誤植成 `+=`，玩家可能被排超過實際報名場數的團數 → `AutoScheduleWithTemplateAsync_ShouldDeductRounds_NotAddRounds_ForProtectedTeamMembers`。
  - `L230` `if (!scheduledPlayersByDay.ContainsKey(day))` 的惰性初始化，若 `!` 被拿掉，同一天處理第二支保留隊時會把已追蹤的「今天已排」名單整包清空，導致同一個候補角色被重複塞進兩支隊 → `AutoScheduleWithTemplateAsync_ShouldNotDoubleBookPoolCandidate_AcrossTwoProtectedTeamsSameDay`。
  - `L76`（`scheduledPlayersByDay[day].Add(reg.Id)` 的 statement mutation）同類但沒特別補測試，留著。
  - 其餘 30+ 個（排序方向、`First`/`FirstOrDefault` 對非空群組等價、範本比對細節）評估後判斷是等價變異或低價值，不逐一補。

重跑 Stryker 驗證：`TeamSlotService.cs` 62.67%→68.00%、`ScheduleService.cs` 存活數 47→41（L75、L230 目標 mutant 確認轉 Killed）。

### 額外發現：`TeamSlotService.cs:195` 授權邏輯 bug（非計畫內，分類存活 mutant 時撞到）

`UpdateAsync` 裡「非管理員不能修改別人角色」的檢查：
```csharp
if (originalCharacter.DiscordId != currentDiscordId &&
    originalCharacter.CharacterId != null && originalCharacter.DiscordId == 0)
    throw new UnauthorizedAccessException("不能修改他人的角色");
```
多了一個 `&& originalCharacter.DiscordId == 0`。已填人的角色位 `DiscordId` 必為對方真實非 0 ID，不可能同時等於 0 → 這個檢查**實質上永遠不會觸發**，非管理員可任意竄改別人已排定的角色資料（角色、職業、戰力、輪數）。寫測試實測驗證過：非管理員改別人已填角色位，預期丟 `UnauthorizedAccessException`，實際沒丟。現有測試只覆蓋「刪除別人角色」「幫別人新增角色」兩條路徑，完全沒測「修改別人已填角色位」，也沒有「非管理員改自己角色應該成功」的正向測試——這正是這行附近好幾顆 mutant 同時存活的原因。

**已修復**：拿掉多餘的 `&& originalCharacter.DiscordId == 0`，改成 `originalCharacter.DiscordId != currentDiscordId && originalCharacter.CharacterId != null`。補兩個測試（`Test/TeamSlotServiceUpdateTests.cs`）：
- `UpdateAsync_NonAdmin_ShouldThrow_WhenModifyingOthersFilledCharacterSlot`（負向，回歸測試）
- `UpdateAsync_NonAdmin_ShouldSucceed_WhenModifyingOwnFilledCharacterSlot`（正向對照，確認沒誤擋合法情境）

重跑 Stryker 驗證：`TeamSlotService.cs` 單檔分數 62.67% → **68.00%**（47→51 killed），L195 相關 mutant 全部轉 Killed。

**補完後重跑 Stryker 驗證（2026-07-30，全數 290 個 unit test 通過）**：
- `TeamSlotAutoAssignService.cs:44`（鎖）、`:77`（避免重複建隊）兩顆目標 mutant 確認轉為 **Killed**。
- `TeamSlot.cs:56`（容量邊界）確認轉為 **Killed**。
- Infrastructure 4 檔合計分數 57.59% → **58.38%**（214→217 killed）；`TeamSlot.cs` 81.40% → **83.72%**（35→36 killed）。
- 分數漲幅不大是預期的——這三項只解決最高優先的具體漏洞，不是把 130+8 個存活 mutant 全部清掉；重點是驗證「跟本 plan 動機直接相關的缺口」確實被補上，而非刷分數。

## 工時預估（實際）

- 裝置 + 單檔案 POC（`RegisterService.cs`）驗證流程可行：**2.5 分鐘跑完**（遠低於估計的 0.5 天，主要時間花在讀 code/寫 config，跑測試本身很快）。
- 擴大到範圍清單 5 個核心檔 + `TeamSlot.cs` + 存活 mutant 初步分類：**約 1 小時**（4 個 Infra 檔案跑 3m54s、Domain 檔案跑 1m17s，其餘是分析時間）。原估 1~1.5 天是高估——Stryker 執行本身不慢，瓶頸在人工分類存活 mutant 的判讀時間，且本輪只深入分類了 auto-assign/TeamSlot 相關的高優先項目，未逐一過完全部 130+8 個存活項目。

## 未解問題
- Stryker 對 Infrastructure 這種有大量外部相依（Dapper/Redis/Discord client）需要 mock 的 project，mutant 數量/跑分鐘數還沒實測過，可能要先跑小範圍 POC 才知道整體時間量級。
