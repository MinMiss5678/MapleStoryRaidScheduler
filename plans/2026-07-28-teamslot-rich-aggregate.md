# TeamSlot 充血聚合計畫（把隊伍不變式收進 Domain）

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：「貧血 → 充血」的示範重構。現況能動；價值在**集中重複散落的不變式 + 讓它能純 Domain 單元測**（不是修 bug）。

> ⚠️ **修訂紀錄（2026-07-28，試作後）**：初版沒料到四個坑，已改：
> 1. **Domain 不能丟 `BusinessException`**（那在 Application，依賴方向 Application→Domain）→ 改丟**新增的 `Domain/Exceptions/DomainException`**，middleware 加映射。
> 2. **`Capacity` 沒載在 TeamSlot 上**（在 `Boss`）→ service **載入時填**；忘了填 → `HasRoom` 誤判滿。列前置條件。
> 3. **命令式持久化（無 change-tracking）**：聚合方法只動**記憶體物件圖**，持久化仍由 service 做 INSERT/UPDATE。→ **`AddMember` 只做 append**（對應 service 的 INSERT 新列）；**「填既有空位」是 merge 的另一種語意（UPDATE 空位列），不塞進 AddMember**，否則 DB/記憶體不一致。
> 4. **merge 太複雜** → **分階段**：安全的 read-side/auto-assign 先做，merge 的 write-side 另評估。（本機有 Docker，可跑整合測驗 auto-assign 行為不變——非只能靠 CI。）

## 目標

把 **TeamSlot 聚合的不變式**（容量、不重複、`IsManual` 保護）從 service 搬進 **TeamSlot 的行為方法**,讓聚合自己保證不超員/不重複/不覆蓋手動成員,而非每個 service 記得檢查。

## 現況（已驗證，規則散在三處且重複）

同組不變式在 `TeamSlotAutoAssignService` / `TeamSlotMergeService` / `TeamSlotService` **各手刻一遍**：
- **容量**：`Characters.Count(c => c.CharacterId != null) < requireMembers`（auto-assign:96、merge:94/236）。
- **加入**：`Characters.Add(member)`（auto-assign:66、merge:272）；merge 另有「填空位」`FirstOrDefault(c => c.CharacterId == null)`（merge:257）。
- **不重複**：`Characters.Any(c => c.CharacterId == x)`（auto-assign:81）、`GroupBy(DiscordId)…`（merge:100）。
- **IsManual 保護**：散在各處的約定。

問題：重複 = DRY 破口 + 「改一處漏一處」風險;且綁 service 才測得到（要 mock 一堆）,不是純 Domain 測。

## 範圍（分階段）

### Phase 1（安全，先做）— 聚合 + read-side + auto-assign
- **TeamSlot 長出行為**（純記憶體不變式）：
```csharp
public int Capacity { get; set; }                       // 由 service 載入時填 = Boss.RequireMembers
public int  FilledCount => Characters.Count(c => c.CharacterId != null);
public bool HasRoom     => FilledCount < Capacity;
public bool Contains(string characterId) => Characters.Any(c => c.CharacterId == characterId);
public IEnumerable<TeamSlotCharacter> ReschedulableMembers()   // IsManual 受保護、空位除外
    => Characters.Where(c => !c.IsManual && c.CharacterId != null);
public void AddMember(TeamSlotCharacter member);        // append + 守容量/重複，違反丟 DomainException
```
- **`Domain/Exceptions/DomainException`**（新）+ `ExceptionHandlerMiddleware` 加 `DomainException => 400`。
- **wire `TeamSlotAutoAssignService`**（behavior-preserving）：
  - 載入後填 `ts.Capacity`（從 `Boss.RequireMembers`）。
  - 容量檢查 `Count < require` → `ts.HasRoom`；重複 `Any(...)` → `ts.Contains(...)`。
  - `Characters.Add(newMember)` → `matchingTeam.AddMember(newMember)`（auto-assign 本就 append，行為不變）。
- **純 Domain 單元測**（免 mock/DB）：滿員 → 丟；有空間 → append；重複 → 擋；`ReschedulableMembers` 排除 IsManual。

### Phase 2（風險，另評估）— merge 的 write-side
- merge 用「填既有空位」（UPDATE 既有列，非 INSERT）→ 若要用聚合,需要**專門的填空位方法** + service 端對應 UPDATE 持久化,才不會與命令式 INSERT 混淆。
- merge 演算法本身（挑哪兩隊、配時段、範本配額）**留 service**（跨聚合編排、領域計算）。
- 因複雜 → **不併入 Phase 1**,獨立一輪做。

> ⚠️ **補充（Phase 1 執行後新發現，尚未動手前先記下）**：
> - **兩種持久化模型不一致**：auto-assign 走「逐筆 `INSERT`」（`TeamSlotCharacterRepository.CreateAsync`）；merge 走「整組砍掉重灌」（`TeamSlotRepository.UpdateAsync` = UPDATE 隊伍列 + **DELETE 全部成員列** + 重新 INSERT，見 `TeamSlotRepository.cs:172-202` 註解「先刪除再重新插入（簡單做法）」）。這是 Phase 2 要專門填空位方法的根本原因，不是 Update 語意不清。
> - **現有安全網缺口在哪**：`TeamSlotMergeServiceMergeTests.cs` 已有 7 個單元測試覆蓋合併演算法決策（何時合併/跳過/範本配對/手動成員），但**全部 mock 掉 repository**，沒驗過真持久化。Phase 2 的風險在持久化層，**前置動作應是先補整合測試**（`Test.Integration`，真 Postgres）釘住現況 round-trip（尤其 auto-assign INSERT 的成員列被 merge `UpdateAsync` 砍掉重灌後換新 Id，這條交互只有真 DB 抓得到），再動重構。

### 非範圍（YAGNI/邊界對，永遠留 service）
- **配額媒合演算法**（依 `BossTemplateRequirement` 挑職業/優先級/可用時段）：跨 character+requirement 的**領域計算**,不是 TeamSlot 內部不變式。
- **唯一性（不能重複報名）**：跨所有隊/報名,DB `ExistAsync` + advisory lock 守,單一聚合守不了（auto-assign 的 `IsAlreadyAssigned` 是**跨隊** any,留 service,只是內層改用 `Contains`）。
- **持久化**：命令式 Dapper 不變（聚合只動記憶體,service 仍負責 SQL）。

## 關鍵決策

### 例外放哪（★ 修訂）
- Domain 丟 **`DomainException`**（Domain 自己的），**不是** Application 的 `BusinessException`（依賴方向不允許）。middleware 映射 `DomainException → 400`,與既有 `AppException` 並列。

### Capacity 怎麼進 TeamSlot（★ 前置條件）
- **service 載入時填 `ts.Capacity = Boss.RequireMembers`**（不改 schema/query）。
- ⚠️ 忘了填 → `Capacity = 0` → `HasRoom` 恆 false → `AddMember` 誤判滿。**驗收要含「Capacity 已填」**。

### AddMember 只做 append（★ 修訂，配合命令式持久化）
- `AddMember` = **append + 守容量/重複**,對應 service 的 `CreateAsync`（INSERT 新列）。
- **不做「填空位」**——那是 merge 的 UPDATE 語意,混進來會 DB（INSERT 新列）與記憶體（填舊列）不一致。留 Phase 2 用專門方法處理。

### AddMember 丟例外 vs 回 bool
- `HasRoom` 查詢 + `AddMember` **防禦性丟 `DomainException`**：auto-assign 迴圈用 `HasRoom` 挑有空位的隊,`AddMember` 保證「即使呼叫錯也絕不超額/重複」。

## 驗收
- [x] `TeamSlot` **純 Domain 單元測**（免 mock）：滿員 `AddMember` → `DomainException`、`HasRoom=false`；有空間 → append；重複 → 擋；`ReschedulableMembers` 排除 IsManual/空位。（`Test/TeamSlotAggregateTests.cs`，5 測）
- [x] `DomainException` 由 middleware 轉 400。
- [x] auto-assign：**Capacity 有被填**；改用 `HasRoom`/`Contains`/`AddMember` 後,既有單元測（277 綠）+ **本機整合測**（真 Postgres，Testcontainers，4 測綠）仍過（**行為不變**）。
- [x] 搜不到 auto-assign 裡殘留的手刻容量/重複判斷（已 grep 確認）。
- [x] merge / TeamSlotService **不動**（留 Phase 2）。

**Phase 1 完成。**

## 工時估
- Phase 1（聚合 + 例外 + middleware + 純 Domain 測 + auto-assign 接線）≈ 半天~一天。
- Phase 2（merge write-side + 持久化 reconcile）≈ 另一輪,先不估。

## 附：為何這是「最高價值」的充血人選
- **單一聚合自己狀態的不變式**（容量/重複/IsManual）——完全符合「該進 Domain」判準。
- 現況**重複散在三處** → 集中後 DRY + 消除「改一處漏一處」。
- **可純 Domain 單元測**（無 DB/mock）——正是充血模型的好處展示。
- 對照留在 service 的（配額媒合、merge 編排、唯一性）→ 示範「什麼該進、什麼不該進」的判斷。
