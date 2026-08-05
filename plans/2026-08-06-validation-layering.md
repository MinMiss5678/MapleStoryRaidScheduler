# 驗證分層：app 前線（4xx）＋ DB 後防 並存

> 狀態：草案。日期：2026-08-06。背景：本 session 討論 varchar revert、FK-500、bot 繞過 DTO、DB 後防噴 500 是否正確後收斂的約定。

## 1. 原則（兩層各司其職）

| 層 | 職責 | 對壞輸入的回應 |
|---|---|---|
| **app（第一線）** | 擋**預期內**壞輸入 | **4xx**（400/404/403/409）——可修正、UX 好、不噴 Sentry |
| **DB（最後防線）** | 守資料完整性、**正常永遠打不到** | 打到＝第一線有洞/被繞過 → **500 + 告警**（loud＝功能，叫你去補洞） |

**兩者並存**：app 給乾淨 4xx + 好 UX；DB 給 writer-agnostic 完整性後防 + 響亮示警。**唯一的「設計錯」是讓正常輸入經常打到 DB 而 500**——修法是補前線 app 驗證，不是怪後防。

## 2. 約束放哪（placement matrix）

| 約束 | DB（後防） | app（前線） | 備註 |
|---|---|---|---|
| NOT NULL | ✅ 留 | DTO `[Required]` / 共用 guard | 完整性 |
| FK | ✅ 留 | **存在性檢查 → 404**（Boss/Period 已做） | app 把預期壞 id 轉 404，FK 只當後防 |
| UNIQUE | ✅ 留（race 權威） | 便宜處先 pre-check | **race 違反 → 409**（見 §4） |
| CHECK（enum 類，如 TeamSlot.Source） | ✅ 留 | DTO `[Range]`/enum | |
| **長度** | ❌（用 text） | DTO `[MaxLength]`（+ 多寫入者用共用 guard） | 見本 session varchar revert：Postgres text==varchar，長度只在 app |

## 3. app 驗證放哪（**兩個寫入者**：WebApi + bot）

- **只有 WebApi 寫的欄位** → DTO `[Required]/[MaxLength]/[Range]`（`[ApiController]` 自動驗、給欄位級 400）。
- **多寫入者（WebApi + bot 都碰）** → **放共用 Application service 的 choke point 或 domain 不變式**（如 `AuthAppService` 的 DiscordName guard、`TeamSlot` 聚合、`Register.EnsureRoundsWithinBudget`）。**別只放 DTO**——`[MaxLength]` 只守 WebApi、bot 繞過。
- **真正 writer-agnostic 的後備** → 只有 **DB 約束**（不依賴「所有寫入者都經過某函式」的假設）。

## 4. 違反 → HTTP 映射（現況 + 缺口）

**現況**（`ExceptionHandlerMiddleware`）：`NotFound→404`、`Business/Domain/App→400`、`Forbidden→403`、其餘 `_→500+告警`；`Idempotency→409`；auth→401/403。

**缺口**：`PostgresException` 沒映射 → **所有** DB 約束違反目前一律落 `_→500`。這對「validation bug」類正確，但**並發 race 的 unique 違反不該 500**。

**目標映射**（在 `ExceptionHandlerMiddleware` 補 `PostgresException` 分支，依 SqlState）：

| SqlState | 約束 | → HTTP | 理由 |
|---|---|---|---|
| `23505` unique_violation | UNIQUE | **409** | **並發 race 是預期結果**（idempotency、同時報名、leader-led 重複申請/邀請）→ 不是 bug |
| `23503` foreign_key | FK | **500 + 告警**（維持） | 應被 app 存在性檢查擋成 404；到 DB＝有路徑漏驗＝bug |
| `23514` check | CHECK | **500 + 告警** | 同上（app 應先 `[Range]`/enum 擋） |
| `23502` not_null | NOT NULL | **500 + 告警** | app 應先 `[Required]`/guard 擋 |
| `22001` string_too_long | 長度 | **500 + 告警** | app 應先 `[MaxLength]`/guard 擋（revert 後多數欄已是 text，較少見） |

**關鍵取捨**：只把「並發下真的會發生」的 unique race 轉 409；FK/check/not-null/length **維持 500 + 告警**——因為把它們轉 4xx 會**遮掉「你 app 層漏驗了」的訊號**。

## 5. 行動項

1. ✅ **`ExceptionHandlerMiddleware` 補 `PostgresException` 分支**：`23505 → 409`；其餘 DB 約束違反**維持 500 + 告警**（明確列 SqlState、預設仍 500）。（commit `7d7702e`）
2. ✅ **審 FK 寫入路徑**：player-facing / admin-create-template 每個「INSERT 帶 FK」都有 app 層存在性檢查→404。
   - `PlayerRegister.PeriodId`（Period 存在→404，`RegisterService.CreateAsync`）
   - `CharacterRegister.CharacterId`（**須屬本人**→404，`RegisterService.EnsureCharactersOwnedAsync`；同時擋不存在與冒用他人 id）
   - `CharacterRegister.BossId`（Boss 存在→404，`RegisterService.ValidateBossesAndBudgetAsync`，與場次預算共用同一份 Boss 清單）
   - `TeamSlotCharacter.CharacterId`（補位須屬本人→404，`TeamSlotService.FillSlotAsync`）
   - `BossTemplate.BossId`（Boss 存在→404，`BossService.CreateTemplateAsync`，commit `c63de85`）
   - `TeamSlot.BossId/PeriodId/TemplateId`（admin 建隊，`TeamSlotService.UpdateAsync` 的 `Id<=0` 分支）：Boss/Period/範本存在→404（`BossId` 重用既載入的 `bossesById`；`TemplateId` 為 null 時略過）。**動機＝修掉假告警**：admin 從既有清單挑，壞 id 多為 race（範本剛被刪）或 API 誤用，不該誤觸 §4「23503＝app 漏驗」的告警。
   - **N/A**：`BossTemplateRequirement.BossTemplateId` — 隨父範本一起建、id 由 server 產生（非 client 傳入），無 FK 缺口。
   - **未做（較低優先）**：admin 經 `UpdateAsync` 加成員時的 `TeamSlotCharacter.CharacterId`（建隊 `Id<=0` 分支與既有隊 `Id>0` 的 add-member 兩處）仍無存在性檢查、壞 id 落 500。admin 可代放他人角色→是「存在性」非「擁有權」，需新增 by-id 角色存在查詢（`ICharacterQuery/Repository` 目前只有 by-DiscordId）；殘留假告警面較小，暫緩。
3. ✅ **多寫入者規則**：新增的存在性檢查全放在**共用 Application service**（非 DTO），bot 若走同一 service 亦覆蓋；`DiscordName` guard 已在 `AuthAppService`（commit `9326628`）。
4. ✅ **保留 DB 完整性約束**（NOT NULL/FK/UNIQUE/CHECK）當後防；**長度留 app**（000008 revert + DTO `[MaxLength]`，2026-08-06 重新評估確認前提仍成立：DTO 覆蓋齊、這 5 個欄位無第二寫入者）。
5. ✅ **文件化這份約定**（本檔）供後續 feature 對照。

## 6. 與 leader-led 重規劃的關聯

leader-led §10 併發控制**依賴 DB unique**（跨隊時段重疊、重複申請/邀請去重）——這些在 race 下的違反**正需要 §4 的 `23505 → 409`**。故本計畫是 leader-led **Phase 2（Push 申請/審核）** 的前置：先把「unique race → 409」補上，申請/邀請的併發去重才會回乾淨 409 而非 500。

## 7. 待確認

- FK/check/not-null/length 違反：**維持 500 + 告警**（推薦，保留「補洞」訊號）vs 也轉 4xx（較友善但遮訊號）？本計畫採前者。
- `AdvisoryLockTimeoutException` 目前多在 service 內被吞成 conflicts；若有 bubble 到 middleware 的路徑，要決定 → 409 或 503（現況落 500）。
