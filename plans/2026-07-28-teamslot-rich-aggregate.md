# TeamSlot 充血聚合計畫（把隊伍不變式收進 Domain）

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：這是「貧血 → 充血」的示範重構。現況能動；價值在**把重複散落的不變式集中 + 讓它能純 Domain 單元測**（不是修 bug）。

## 目標

把 **TeamSlot 這個聚合的不變式**（容量、不重複、空位填補、`IsManual` 保護）從 service 搬進 **TeamSlot 實體的行為方法**——讓聚合**自己保證永遠不會超員/塞重複/覆蓋手動成員**,而非靠每個 service 記得檢查。

## 現況（已驗證，規則散在三處且重複）

同一組不變式在 `TeamSlotAutoAssignService` / `TeamSlotMergeService` / `TeamSlotService` **各手刻一遍**：
- **容量**：`Characters.Count(c => c.CharacterId != null) < requireMembers`（auto-assign:96、merge:94/236）。
- **加入**：`Characters.Add(member)`（auto-assign:66、merge:272），有空位則填 `FirstOrDefault(c => c.CharacterId == null)`（merge:257）。
- **不重複**：`Characters.Any(c => c.CharacterId == x)`（auto-assign:81）、`GroupBy(DiscordId).Any(g => g.Count() > 1)`（merge:100）。
- **IsManual 保護**：靠約定散在各處（`slot.IsManual = ...`、註解「重排不覆蓋」）。

問題：**重複 = DRY 破口 + 正確性風險**（某個 service 改了容量判斷、別的沒改，不會被互相擋到）；且這些檢查**綁著 service 才測得到**（要 mock 一堆),不是純 Domain 測。

## 範圍

### 做：TeamSlot 長出行為
```csharp
public class TeamSlot
{
    // 既有屬性 + 載入時填入的容量（見決策）
    public int Capacity { get; init; }              // = Boss.RequireMembers

    public int FilledCount => Characters.Count(c => c.CharacterId != null);
    public bool HasRoom     => FilledCount < Capacity;
    public bool Contains(string characterId) => Characters.Any(c => c.CharacterId == characterId);

    // 加入成員：擋超額、擋重複；有空位就填、否則新增。違反 → BusinessException。
    public void AddMember(TeamSlotCharacter member);

    // 批次重排/合併「可覆蓋」的成員（IsManual 受保護、空位除外）
    public IEnumerable<TeamSlotCharacter> ReschedulableMembers()
        => Characters.Where(c => !c.IsManual && c.CharacterId != null);
}
```
- 三個 service 改成呼叫 `teamSlot.HasRoom` / `teamSlot.AddMember(...)` / `teamSlot.ReschedulableMembers()`,**刪掉手刻的 count/add/duplicate 判斷**。
- 不變式違反丟 `BusinessException`（Application.Exceptions）→ 由既有 `ExceptionHandlerMiddleware` 轉 400（見 MSRS `§6`）。

### 不做（留在 service，YAGNI/邊界對）
- **配額媒合演算法**（auto-assign 依 `BossTemplateRequirement` 的職業/優先級/可用時段挑人）：是**跨 character + requirement 的領域計算**,屬 domain service，**不是 TeamSlot 的內部不變式**。TeamSlot 只負責「把挑好的人加進來且不違反容量/重複」。
- **merge 編排**（哪兩隊合、怎麼配時段）：跨聚合協調,留 service;只用 TeamSlot 的 `AddMember`/`HasRoom`。
- **唯一性（不能重複報名）**：跨所有隊/報名,DB `ExistAsync` + advisory lock 守,單一聚合守不了。
- 持久化不變（Dapper repo 照樣映 TeamSlot + Characters）。

## 關鍵決策

### ★ 容量（`Boss.RequireMembers`）怎麼進 TeamSlot
TeamSlot 只有 `BossId`,容量在 `Boss` 上。要讓「≤ 容量」自我保護,聚合得知道容量：
- **建議：載入時填 `Capacity`（init）**——query/service 拿到 Boss 時把 `RequireMembers` 塞進 TeamSlot。無 schema 改動、聚合自足。
- 替代：`AddMember(member, capacity)` 每次傳入——較弱(每次要被告知)、但零改動。
- → 選**載入時填 `Capacity`**;查詢層或 service 建 TeamSlot 時設定。

### AddMember：丟例外 vs 回 bool
- **`HasRoom` 查詢 + `AddMember` 防禦性丟例外**：auto-assign 迴圈用 `HasRoom` 挑有空位的隊,`AddMember` 保證「即使呼叫錯也絕不超額」（丟 `BusinessException`）。→ 查詢與保護分開,聚合永遠守得住。

### 空位語意
- `CharacterId == null` = 空位。`AddMember` **優先填第一個空位**（沿用 merge:257 的行為）,沒空位才 append。集中在一處、行為一致。

## 驗收
- [ ] `TeamSlot` 單元測（**純 Domain、免 mock**）：
  - 滿員 `AddMember` → 丟 `BusinessException`；`HasRoom` = false。
  - 有空位 → 填空位（不新增列）；無空位 → append。
  - 重複 character → 擋。
  - `ReschedulableMembers` 排除 `IsManual` 與空位。
- [ ] 三個 service 改用 TeamSlot 行為後,既有整合測/單元測仍綠（行為不變、只是集中）。
- [ ] 搜不到 service 裡殘留的手刻容量/重複判斷。

## 工時估
- TeamSlot 行為 + 純 Domain 單元測 ≈ 半天。
- 三個 service 改接 + 回歸 ≈ 半天~一天（merge 最複雜）。

## 附：為何這是「最高價值」的充血人選
- **單一聚合自己狀態的不變式**（容量/重複/空位/IsManual）——判準完全符合「該進 Domain」。
- 現況**重複散在三處** → 集中後 DRY + 消除「改一處漏一處」風險。
- **可純 Domain 單元測**（無 DB/mock）——正是充血模型的好處展示。
- 對照留在 service 的（配額媒合、merge 編排、唯一性）→ 示範「什麼該進、什麼不該進」的判斷。
