# 首頁報名截止 banner 與後端實際開放不一致（誤報「已截止」）

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 背景

2026/8/5 對正式環境（Lightsail k3s）跑完整核心流程測試時發現：首頁顯示「報名截止（星期一 23:59:59）**已截止**」，但實際用 UI 報名（週期 2026/8/11 ~ 8/18）後端回 `POST /api/register 200`、成功建立報名並觸發自動排團。前後端對「報名是否截止」的判斷不一致。

根因是兩邊用**不同的基準點**算截止時間：

- **首頁 banner**（`web/app/page.tsx` 的 `getThisWeekDeadline`, 約 L20-27）：相對**當前日曆週**算——`config.deadlineDayOfWeek - now.getDay()`，得到「這個日曆週的星期一 23:59:59」。測試當天是週三（08-05），本週一（08-03）已過 → `formatCountdown` 收到負數 → 顯示「已截止」。
- **後端**（`Domain/Entities/SystemConfig.cs` 的 `GetDeadlineForPeriod`，`RegisterService.cs` L158-161 用它擋）：相對**報名週期的起始日**（`Period.StartDate` = 08-11）回推——公式保證截止日永遠在週期起始前 1~7 天 → 截止 08-10 23:59:59，相對現在（08-04 UTC）仍在未來 → 放行報名。

亦即：banner 看「這個現實日曆週」，後端看「即將開打的那個 raid 週期」。兩者本來就會錯開，banner 因此在「上一週截止時間已過、但下一個週期的報名其實還開著」的空窗期誤報「已截止」。

## 目標

讓首頁 banner 的「報名截止時間 / 是否已截止」與後端 `GetDeadlineForPeriod` 的判斷**一致**——banner 應該反映**目前開放報名的那個週期**的截止時間，而不是當前日曆週回推的時間，避免使用者被誤導以為不能報名。

## 範圍

- `web/app/page.tsx`：`getThisWeekDeadline` 改成「拿目前 latest Period 的 StartDate，套用後端同一套 `GetDeadlineForPeriod` 邏輯」算截止。
  - 前端要能取得 latest Period 的 StartDate。確認 `/api/period` 或現有端點有沒有回這個值；沒有的話評估補一個唯讀端點，或後端直接提供「本週期截止時間」讓前端不必自己重算（比較不會再漂移）。
- 決策傾向：**後端算好截止時間、前端只顯示**（單一事實來源），比前端重寫一份 deadline 計算好——避免以後兩邊公式再度分岔（這次的 bug 本質就是同一個邏輯前後端各寫一份）。

### 非範圍

- 不改後端的截止判斷邏輯（`GetDeadlineForPeriod` 是對的，以它為準）。
- 不動自動排團、報名本身的流程。

## 附帶發現（順手記錄，不一定這個 plan 處理）

- `Domain/Entities/Character.cs` 的 `Job` 欄位標 `[MaxLength(5)]`，但 `constants/jobs.ts` 有 6 字職業名（火毒大魔導士、冰雷大魔導士），且 DB 欄位是 `text`（無長度限制）、Dapper 不驗證 DataAnnotation → 這個標註**完全沒作用**，6 字職業實測能正常存取。屬誤導性殘留標註，建議移除或改成正確長度，但無功能影響、優先級低。

## 驗收

- [ ] 在「上一週截止已過、下一個開放週期報名仍開著」的時間點，首頁 banner 顯示的是**該開放週期**的截止時間與正確倒數（非「已截止」）
- [ ] banner 顯示的截止時間與後端 `GetDeadlineForPeriod` 對同一週期算出的值一致
- [ ] 真的超過開放週期截止時間後，banner 才顯示「已截止」，且此時後端也確實擋下報名（兩邊同步）

## 工時估

- 釐清 latest Period StartDate 前端取得方式 + 決定「後端給截止時間」還是「前端重算」：約 20 分鐘
- 實作（前端顯示 + 必要的後端端點/欄位）：約 30-40 分鐘
- 驗證（本機模擬跨週空窗期）：約 20 分鐘
- 小計：約 1 ~ 1.5 小時

## 未解問題

- 前端目前能不能直接拿到 latest Period 的 StartDate？要讀 `web/services` 與現有 period 相關端點確認，再決定是「前端重算」還是「後端多回一個截止時間欄位」。
