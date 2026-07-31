# TeamSlot 補位端點分離計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **狀態：已實作完成**（原訂暫緩，寫完當下就決定直接做）。

## 背景

`Infrastructure/Services/TeamSlotService.cs` 的 `UpdateAsync` 一個方法混合處理 5 種操作：刪隊（admin-only）、建隊（admin-only）、刪角色（擁有權檢查）、加角色（擁有權檢查）、改角色（擁有權檢查 + 補位規則），靠 `isAdmin` 與 `member.Id` 是否為 null 分支判斷。

同一支方法在很短時間內連續出兩次真的 bug：
1. **授權漏洞**（`bf6c3ec`）：非管理員修改他人已填角色位的檢查形同虛設。
2. **補位誤傷**（`bbeeb2d`）：修完漏洞後，玩家補位（整包重送既有成員 + 新增自己）被新的擁有權檢查誤判成竄改他人角色，因為後端無法分辨「payload 裡這筆是真的要改，還是只是跟著整包送回來的既有成員」。

這是「玩家自助單一操作」跟「管理員批次操作」共用同一個 payload 形狀、同一段授權邏輯，才會需要靠比對去猜意圖——分開兩種操作的端點，能讓玩家自助那塊的安全檢查因為「payload 裡本來就放不進別人的資料」而不可能寫錯。

## 目標

把「玩家自助、永遠只碰自己」的操作（補位、移除自己的角色）從 `UpdateAsync` 拆成獨立、窄範圍的端點，跟「管理員批次整隊重排/替他人操作」的既有 `UpdateAsync` 分開，消除「同一份 payload 混合不同授權主體的意圖」這個 bug 來源。

## 範圍

- 新增 `FillSlotAsync`（或類似命名）：玩家把自己的角色加進某個空位。payload 只帶「這一格 + 我的角色資訊」，沒有其他成員的資料，安全檢查退化成「這格真的是空的嗎」+「你填的是不是自己的角色」，不需要比對既有 roster。
- 評估是否連「玩家移除自己的角色」也一併拆出（目前也是走 `UpdateAsync` 的 `DeleteTeamSlotCharacterIds`，同樣有「非管理員只能刪自己的」擁有權檢查，但因為是明確的 ID 清單而非整包重送，目前沒有出過類似的誤傷 bug，優先度較低）。
- 管理員批次操作（`UpdateAsync` 現有行為）不變——那個場景本來就需要「一次送整批最終狀態」的宣告式寫法，不受影響。

### 關鍵決策：抽共用 private helper，避免兩份一樣的邏輯（DRY）

`UpdateAsync` 現有「新增角色」那段（`member.Id == null` 分支，`TeamSlotService.cs:172-185`）做的事只有兩步：① `originalTeam.AddMember(newChar)`（domain 不變式：容量、重複偵測）② `_teamSlotCharacterRepository.CreateAsync(newChar)`（寫進 DB）。這兩步是**做什麼**，補位和管理員新增角色完全一樣，不該複製兩份。

不一樣的只有**誰能做、透過什麼形狀的 payload 進來**：管理員整包送、繞過檢查；玩家補位窄範圍、payload 天生放不進別人資料。

拆分時把「做什麼」抽成共用 private method（例如 `AddCharacterToTeamAsync(originalTeam, newChar)`），前置的「取鎖 → 撈隊伍 → 設 Capacity」也一併抽共用 helper。`UpdateAsync`（管理員）跟新的 `FillSlotAsync`（玩家）都呼叫同一份核心邏輯，只是外層的授權判斷跟接的 payload 型別不同。容量/重複這些 domain 規則只會存在一個地方，改一次兩邊生效；會維護的是「兩個入口的授權判斷」（本來就該不同，這正是分開的目的），不是兩份一樣的核心邏輯。

## 非範圍 / 為什麼暫緩

- 這是中等重構：新端點 + 新 DTO + 新測試 + 前端呼叫點都要動，不是一行修法能解決的範圍。
- 目前已經有一個驗證過、範圍小的修法頂著（`bbeeb2d`：前端補位只送新增成員，不送整包），眼前的 bug 已解決，不是燒眉毛的急事。
- 單公會規模，這個方法的變更頻率不算高——這次連出兩次 bug，某種程度是因為同一個工作階段內密集在動這個檔案（變異測試 + 授權修復），不完全代表長期會持續踩雷。

## 觸發條件（何時該真的動手）

- 同一個方法（`UpdateAsync` 的擁有權檢查 / 整包重送模式）第三次因為類似原因（授權誤判、意圖混淆）出 bug，值得回頭看這份 plan、動手拆分。
- 或未來要新增另一種「玩家自助單一操作」（例如玩家自己換角色、自己調整場數）時，與其塞進 `UpdateAsync` 再多繞一層判斷，不如藉機拆出來。

## 驗收

- [x] `FillSlotAsync` 只接受「teamSlotId + 自己的角色資訊」，型別上不可能帶入他人角色資料（`TeamSlotFillRequest` 沒有 DiscordId 欄位，一律用 `currentDiscordId`）
- [x] 既有 `fill.spec.ts` e2e 測試改走新端點，維持綠燈（本機 docker compose e2e stack 重跑 8/8 全綠）
- [x] 管理員批次排團（`UpdateAsync`）行為不變，既有測試全綠（301 個 unit test 全過，含既有 `TeamSlotServiceUpdateTests.cs` 未改一行）
- [x] 新端點有獨立 unit test：`Test/TeamSlotServiceFillTests.cs`（5 個測試：用登入身分而非 payload 寫入、取鎖、隊伍不存在丟 `BusinessException`、lock timeout 丟 `BusinessException`、重複/超額丟 `DomainException`）

## 實作紀錄

- `AcquireAndLoadTeamSlotAsync` / `AddCharacterToTeamAsync` 兩個共用 private helper 抽出後，`UpdateAsync` 跟 `FillSlotAsync` 共用同一份「取鎖＋撈隊伍＋守不變式＋寫入」核心邏輯，符合當初「避免兩份一樣邏輯」的決策。
- Controller 新端點 `POST /api/teamSlot/Fill` 最終回 **200（`Ok()`，不給物件）**，跟現有其他端點一致——過程中先試過 204，詳見下方發現。

### 意外發現：前端 proxy 從沒處理過 204，改回 200 才是更務實的選擇

第一版 controller 回 `204 No Content`（語意上「成功但無內容」的教科書寫法）。但 `web/app/api/[...path]/route.ts` 的共用 proxy 一律把 `response.arrayBuffer()` 原樣塞進 `NextResponse` 的 body，而 204 是**這個 app 有史以來第一個**回 204 的端點——依 Fetch 規格，204/205/304 這類 null-body status 不能帶 body（連空的 ArrayBuffer 都不行），proxy 建構 `NextResponse` 時直接丟 `TypeError: Response constructor: Invalid response status code 204`，變成前端看到「請求失敗 (500)」。

本機 e2e stack 重現後從 frontend container log 抓到確切例外位置。proxy 本身的 fix（204/205/304 一律傳 `null` body）保留下來——這是共用程式碼的潛藏 bug，之後任何端點想回 204 都會踩到，順手修掉比留著地雷划算，也補了 `web/__tests__/proxy.test.ts` 的回歸測試。

但事後重新權衡：204 對這支端點沒有帶來實際語意價值（純內部 API，不是要給外部消費者看 OpenAPI spec 判斷語意），反而讓它變成全專案唯一一個「正常運作依賴 proxy 剛修好的邊界情況處理」的端點。改回 `Ok()`（200，不給物件）跟其他所有端點一致，且 200 從來不在 null-body status 清單裡，從根本上不會再受這類 body-handling 差異影響。最終決定：**proxy fix 留著（通用、正確），但 `FillAsync` 改回 200**，兩者互不衝突。

### 後續調整：改回傳寫入後重新查詢的最新隊伍資料，前端不再自己拼

原本 `FillAsync` 回 200 但不帶任何 body，前端 `handleJoinTeam` 靠**本地樂觀更新**（自己在瀏覽器端拼一個 `teamSlotCharacter` 塞進 React Query 快取）顯示補位結果——這個本地物件沒有資料庫產生的真實 `id`／`Version`（樂觀鎖版本），因為 `ITeamSlotCharacterRepository.CreateAsync` 用的泛用 `DapperRepository.InsertAsync` 只回受影響列數、拿不到自動產生的 Id。

參考 `TeamSlotController.UpdateAsync` 既有慣例（寫入後在同一個 request 裡重新查詢、包進同一個回應），`FillSlotAsync` 改成回傳型別 `Task<TeamSlotDto>`：寫入成功後呼叫既有的 `GetByBossIdAsync(originalTeam.BossId)` 重查、找出這個 team slot 的最新狀態回傳（查無資料是理論上的邊界情況，丟 `BusinessException` 而非讓例外裸奔）。前端 `scheduleService.fillSlot()` 回傳型別改成 `Promise<TeamSlot>`，`handleJoinTeam` 改用伺服器回傳的權威版本呼叫 `onTeamSlotUpdate`，不再自己拼本地物件。

驗收：`Test/TeamSlotServiceFillTests.cs` 補上重查路徑的 mock（`_periodQueryMock`／`_teamSlotQueryMock`），6 個測試全過；後端 301 個 unit test、前端 40 個 vitest、本機 e2e 8/8 全數重新驗證綠燈。

## 未解問題

- 玩家「移除自己角色」要不要也一併拆，還是繼續留在 `UpdateAsync` 的 `DeleteTeamSlotCharacterIds`（目前沒出過事，優先度低）。
