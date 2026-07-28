# TeamSlot 管理員編輯併發控制計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：這是承接 TeamSlot 充血聚合（`2026-07-28-teamslot-rich-aggregate.md`）之後，掃描 `TeamSlotService.UpdateAsync`（admin 手動編輯隊伍）發現的**併發控制缺口**。三個問題彼此獨立、成因不同，不能用單一機制打通吃。

## 現況（已驗證，三個問題）

### 問題 1：同瞬間競爭，無悲觀鎖
`TeamSlotAutoAssignService` 有 `pg_advisory_xact_lock`（`RegistrationLock.AcquireAutoAssignLockAsync`）防同 period 併發報名。但 `TeamSlotService.UpdateAsync`（admin 手動編輯）**完全沒有**同等保護：
- 兩管理員同時對同一隊新增成員 → 各自的記憶體快照都顯示「還有空位」→ `AddMember` 各自檢查通過 → 兩筆都寫入 → **超編**（TOCTOU race）。
- `DeleteCharacterAsync` 有隱藏副作用：**移除自動隊最後一個真實成員會連整個 `TeamSlot` 一起砍掉**（`Infrastructure/Repositories/TeamSlotCharacterRepository.cs:49-59`，只清 `Source=auto` 的空團）。若此時有人正要對同一隊新增成員，`TeamSlotCharacter.TeamSlotId` 有外鍵約束（`db/migrations/000001_init_schema.up.sql:78`），會撞**原生 FK violation（23503）**，變成未接住的醜陋錯誤，不是我們設計好的訊息。

### 問題 2：隊伍整個消失（merge / 自動排團的砍掉重灌）
Merge（`TeamSlotRepository.UpdateAsync` = 整組 DELETE 成員列 + 重新 INSERT）與管理員的「自動排團」（`AutoScheduleWithTemplate`，整王批次重算後靠同一支 `PUT /api/teamSlot` 落地）都會讓既有的 `teamSlot.Id` 或其底下 characters 的 Id 失效。若管理員 A 開著頁面時，管理員 B 觸發了砍掉重灌，A 之後送出的舊 Id：
```csharp
var originalTeam = await _teamSlotRepository.GetByIdAsync(teamSlot.Id);
if (originalTeam == null) continue;   // 現況：靜默跳過，前端仍顯示「已儲存！」
```
**目前的行為是靜默失敗 + 假成功訊息**，管理員的編輯悄悄沒有生效，卻以為成功了。

### 問題 3：跨時間過時覆寫（樂觀鎖缺口，鎖救不了）
既有成員（`member.Id != null`）分支是**盲寫全部 8 個欄位**（`TeamSlotCharacterRepository.UpdateAsync`），沒有任何版本比對，`WHERE` 只用 `Id`：
```csharp
sql.Set(x => x.DiscordId, ...).Set(x => x.DiscordName, ...)... .Where(x => x.Id == teamSlotCharacter.Id);
```
前端調查證實：admin UI 對既有成員**沒有任何欄位可編輯**（唯讀顯示，只能整包新增/移除），所以「A 改 Job、B 拿舊資料把 Job 蓋回去」這種**同角色管理員間**的欄位衝突不會發生。但**跨流程**會發生：merge 的 `AbsorbMembers` 會把某個既有空位列從「空」改成「有人」；如果管理員這時候頁面還停在「這格是空的」的舊快照，之後隨便存個檔（哪怕只是想移除別的隊員），送出的完整快照裡那格**還是舊的空位版本** → 盲寫路徑會把 merge 剛填好的人**悄悄蓋回空位**，無聲吃掉 merge 的結果。
**這不是同瞬間競爭，是「拿著五分鐘前的資料存檔」——悲觀鎖握不住人的思考時間，需要樂觀鎖版本比對。**

## 範圍（三個機制，各自獨立可分階段）

### Phase A：悲觀鎖（同瞬間競爭）— 對應問題 1
- `TeamSlotService.UpdateAsync` 處理每個 `teamSlot.Id` 前，先 `pg_advisory_xact_lock(teamSlotId)`（仿 `RegistrationLock` 的作法，用不同的 lock key namespace 區分 auto-assign 用途）。
- 鎖跟著 UoW 交易走，request 結束自動釋放，不需額外處理。
- 這個鎖同時擋住「容量競爭」跟「清團連帶撞 FK」兩種問題——因為鎖序列化後，後進來的請求重新 `GetByIdAsync` 讀到的一定是最新真相（若隊伍已被砍，會落到 Phase B 的「隊伍消失」分支，而不是撞 FK）。

### Phase B：樂觀鎖 + 統一衝突回報 — 對應問題 2 與問題 3（已完成）
- `TeamSlotCharacter` 的版本比對用 Postgres 系統欄位 **`xmin`**（不用額外 migration，讀取時一併回傳，`UPDATE` 的 `WHERE` 帶上 `xmin = @clientVersion::xid`，影響 0 筆 = 版本衝突）。
- **`originalTeam == null`（隊伍消失）與 `xmin` 版本衝突（列被動過），統一收進同一份 `ConflictedTeamSlotIds` 清單**，不中斷其他隊的處理、不丟例外炸掉整個 request：
```csharp
var conflicts = new List<int>();
foreach (var teamSlot in teamSlotUpdateRequest.TeamSlots)
{
    var originalTeam = await _teamSlotRepository.GetByIdAsync(teamSlot.Id);
    if (originalTeam == null) { conflicts.Add(teamSlot.Id); continue; }
    ...
    // 既有成員 UPDATE 改用版本比對，affected==0 也塞進 conflicts、不丟例外
}
return new TeamSlotUpdateResult { ConflictedTeamSlotIds = conflicts };
```

> ⚠️ **修訂（實作後才發現的兩個技術現實，寫回這裡）**：
> 1. **`xmin` 沒辦法直接套進現有 `QueryBuilder`**：既有的 `Select<T>(x => new {...})` 靠 C# 屬性名稱反射組欄位、一律加雙引號（`a."DiscordId"`）；`xmin` 是 Postgres 系統欄位、原生小寫，加雙引號的識別字大小寫敏感會對不起來，也沒有 `SelectRaw` 這種逃生艙。解法：`QueryBuilder` 加 `SelectRaw(sql)`、`UpdateBuilder<T>` 加 `WhereRaw(sql, parameters)`（比照 `DeleteBuilder<T>` 既有的 `WhereRaw` 模式），供這類原生系統欄位/表達式使用。
> 2. **原先「回應只回傳 Id 清單、不內嵌最新資料」的決策，讀了 `TeamSlotController` 才發現前提是錯的**：`PUT /api/teamSlot` **本來就在每次存檔後重新 `GetByBossIdAsync` 回傳整包最新資料**（不是我方案設計的新行為，是既有慣例）。既然如此，`ConflictedTeamSlotIds` 直接跟這份既有的 `teamSlots` 包在同一個回應即可，**前端不需要額外重抓**——比原計畫設計得更簡單，Phase C 也因此不用寫「收到衝突才重抓」的邏輯。

**驗證重點**：mock 單元測只驗得到「service 邏輯有沒有把衝突塞進清單」，驗不到 `xmin::text`/`@version::xid` 這段原生 SQL 轉型與比對本身是否正確——這條事實只有整合測試打真 Postgres 能釘住（`TeamSlotCharacterOptimisticLockIntegrationTests`：正確版本更新成功、過時版本被擋下且資料沒被覆寫）。

### Phase C：前端接住衝突清單（待做，範圍已因上述修訂而縮小）
- `PUT /api/teamSlot` 的回應已經同時帶 `conflictedTeamSlotIds` 跟最新 `teamSlots`（後端已完成，見上）——前端**不需要額外呼叫**，直接用同一個回應裡的資料判斷哪幾張卡要提示。
- **只標示衝突清單裡那幾張卡片**，其餘維持管理員畫面上的樣子不動——不用整個王重來、不用追蹤隊員被分到哪個新隊伍（identity 已經消失就是消失，不強行復原）。
- 提示文案：「此隊已被異動或消失，已略過此處編輯，已顯示最新資料，請重新確認」。

**UI 呈現方式（★ 決策：顏色標記 + 原地不動，不重新排序）**：
- **卡片原地標色**（邊框顏色 + icon + 卡片內文字），**不把衝突的隊伍移到列表最上面**。理由：存檔本來就會整包重抓最新資料重繪一次畫面，若再疊加排序位移，管理員會經歷兩次視覺跳動；而且排序位移會打亂管理員原本靠位置記憶隊伍（「這隊在第 3 個」）的心智模型，一次存檔牽涉多隊時尤其明顯。
- **改用「頂部摘要橫幅 + 捲動跳轉」達成快速定位的效果**：頁面最上方顯示「N 隊有衝突，點此查看」，點擊後**捲動視窗到第一張衝突卡片**（不改變卡片在列表中的實際位置）。同樣解決「衝突卡片可能在很多隊裡不好找」的問題，但不犧牲排序穩定性。

## 非範圍（YAGNI/邊界對，這次不做）

- **不做欄位級樂觀鎖 UI 提示**（如「A 改的地方標黃色」）：admin UI 本來就沒有欄位可編輯，沒有意義。
- **不追蹤「隊員被分到哪個新隊伍」**：身分延續性追蹤（merge/自動排團砍掉重灌後）是另一個量級的工程，超出這次範圍，讓管理員肉眼重新判斷即可。
- **不改前端「新增/移除」以外的既有成員編輯 UI**：目前唯讀顯示是刻意設計，這次不擴充可編輯欄位。
- **不特別標示「沒衝突，但因為別人操作而跟你剛才看到的不一樣」的其他隊伍**（★ 討論過，刻意劃線）：`PUT /api/teamSlot` 存檔後重抓的是**整個王目前全部隊伍**，可能包含這次請求根本沒碰過的隊伍被別的管理員/流程動過。`ConflictedTeamSlotIds` 只標記「這次請求自己嘗試修改、但失敗」的隊伍——**其餘隊伍即使內容跟管理員存檔前看到的不同，也不算衝突，不特別提示**。理由：`conflicts` 機制要防的是「管理員自己的操作意圖被無聲吃掉」；管理員根本沒打算動的隊伍，資料變了只是誠實反映當下真相，沒有「意圖被吃掉」這回事，不需要額外機制。要做「這隊雖然沒編輯但也變了」的全面偵測，得對整份 `teamSlots` 逐隊 diff 存檔前後快照，這是另一個更大範圍的「外部變動感知」功能，超出這次範圍。

## 關鍵決策

### 悲觀鎖與樂觀鎖是互補，不是二選一
悲觀鎖顧「同瞬間兩請求競爭」（critical section 短，一次 request 處理時間內）；樂觀鎖顧「跨人類思考時間的舊資料覆寫」（不可能鎖住幾分鐘的人工操作時間）。兩者解決的問題完全不同，都要做。

### 隊伍消失與版本衝突共用同一套回報機制
兩者對前端而言都是「這個 `teamSlotId` 存檔失敗、其餘都成功」，沒有必要分開設計兩套錯誤格式。

### 版本欄位選 `xmin`，不新增 migration
Postgres 系統欄位天生就是給這個用途，省一次 schema 變更。

### 衝突回報只給 Id 清單，不內嵌最新資料（可討論的取捨）
對齊專案既有 CQRS-lite 讀寫分離慣例；若之後在意多一次網路來回的延遲，可以改成內嵌，屬於可逆決策。

## 驗收
- [x] 悲觀鎖：`IRegistrationLock.AcquireTeamSlotEditLockAsync(teamSlotId)`（classId 1002，與 auto-assign 的 1001 區隔），`TeamSlotService.UpdateAsync` 處理既有隊伍前先取鎖。
- [x] 整合測試（真 Postgres，`pg_try_advisory_xact_lock` 非阻塞探測）：同隊伍互斥、不同隊伍不互卡——`TeamSlotEditLockIntegrationTests`，2 測。
- [x] 單元測試驗證鎖有被正確呼叫（`AcquireTeamSlotEditLockAsync(teamSlotId)` Times.Once）。
- [x] 既有 286 單元測試 + 28 整合測試（含新增的 3 個）全綠、`dotnet format --verify-no-changes` 乾淨。
- [ ] ~~兩個併發請求同時對同一隊新增成員（超過容量）序列化後只有一筆成功~~——鎖本身用確定性探測驗過互斥，不用額外寫「真的並發打兩個 request」的計時測試（容易 flaky，advisory lock 互斥已經是 Postgres 保證的行為）。
- [x] 悲觀鎖：併發「移除最後一人（觸發清團）」與「新增成員」→ 不再出現原生 FK violation，落到「隊伍消失」分支走統一衝突回報。**Phase B 合併後才補的端到端測試**（`TeamSlotConcurrentEditIntegrationTests`，真 Postgres，構造真實 `TeamSlotService` 走完整流程）：先移除最後一人驗證整團真的被砍、再對同一個（已消失的）`teamSlotId` 嘗試新增成員，斷言不拋 FK 例外、正確落入 `ConflictedTeamSlotIds`。之前 Phase A/B 的測試都只各自驗證了鎖跟衝突回報的機制本身，從未串起來驗過這個端到端情境，是遺漏、不是設計如此。

**Phase A 完成。**

- [x] 樂觀鎖：`TeamSlotCharacterRepository.UpdateAsync` 改用 `xmin` 版本比對，`affected == 0` 回傳 `false`。整合測試 `TeamSlotCharacterOptimisticLockIntegrationTests`（真 Postgres）驗證：正確版本更新成功、過時版本被擋下且資料沒被覆寫。
- [x] `originalTeam == null` → 列入 `ConflictedTeamSlotIds`，不丟例外中斷其他隊、不再有「靜默成功」的假訊息（`UpdateAsync_ShouldSkip_WhenTeamSlotNotFound` 改為斷言衝突清單）。
- [x] `QueryBuilder.SelectRaw` / `UpdateBuilder<T>.WhereRaw(sql, params)`：擴充共用 SqlBuilder 支援原生欄位/條件（xmin 這類系統欄位無法用一般 Expression 投影）。
- [x] `TeamSlotCharacterDto`/`TeamSlotMemberDto`/`TeamSlotCharacter`（Domain）都加上 `Version`，讀取路徑（`TeamSlotQuery` 三個方法）補 `xmin::text AS "Version"`。
- [x] `ITeamSlotService.UpdateAsync`/`ITeamSlotCharacterRepository.UpdateAsync` 回傳型別分別改為 `Task<TeamSlotUpdateResult>`/`Task<bool>`；`TeamSlotController` 回應同時帶 `conflictedTeamSlotIds` 與既有的 `teamSlots`（本來就有的重抓邏輯，不是新增行為）。
- [x] 287 個單元測試（含新增的版本衝突/隊伍消失情境測試）+ 29 個整合測試全綠、`dotnet format --verify-no-changes` 乾淨。
- [ ] 前端：讀取衝突清單 → 標示受影響卡片、提示重新確認（Phase C，範圍已縮小，見上方修訂）。

**Phase B（後端）完成。**

## 工時估
- Phase A（悲觀鎖）≈ 半天，仿 auto-assign 既有作法，風險低。
- Phase B（樂觀鎖 + 統一衝突回報，後端）實際花費 ≈ 一天，比預估多：多花在解 `xmin` 跟既有 `QueryBuilder` 的欄位命名慣例衝突（需擴充 `SelectRaw`/`WhereRaw`），以及確認既有 mock 測試因回傳型別改變（`Task` → `Task<bool>`）產生的隱性「意外過關」要一併修成明確斷言。
- Phase C（前端）≈ 半小時~1 小時（比原估更小，因為不用寫「收到衝突才重抓」的邏輯，回應已經帶著最新資料）。

## 附：為什麼這三個問題值得一起處理，而不是各自零散修

三個問題都源自同一個根因：**`TeamSlotService.UpdateAsync` 是唯一完全沒有併發保護的寫入路徑**（對照 auto-assign 有悲觀鎖、merge 演算法本身有 service 層決策序列化）。一次性建立完整的併發控制骨架（悲觀鎖 + 樂觀鎖 + 統一衝突回報），比之後每踩到一個新坑就補一個零散 patch 更省——尤其樂觀鎖的「衝突回報清單」機制一旦搭好，之後不管是哪種原因造成的衝突，都能複用同一條前端處理路徑。
