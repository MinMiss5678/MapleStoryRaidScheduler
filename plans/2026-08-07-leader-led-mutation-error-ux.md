# leader-led 前端：mutation 錯誤與「滿隊」UX 收尾

狀態：**規劃中（尚未實作）**。針對已上線的 leader-led 前端 6 頁，補齊 mutation 失敗（尤其「隊伍已滿」）的優雅處理。

動機：併發下「隊伍剩 1 位、多人同時接受」→ 後端 `ConfirmMemberAsync` 序列化裁決，輸家回 **400「隊伍已滿。」**。這是正確行為（防超編），真正對使用者有感的是**前端怎麼呈現**。目前處理最小且不一致。

## 1. 現況缺口（已讀 code 確認）

| 問題 | 現況 | 檔案 |
|---|---|---|
| 失敗**不刷新**清單 | `onError` 只彈訊息、**無 `invalidateQueries`**（只有 `onSuccess` 有） | 各 mutation 頁 |
| **風格不一致/粗糙** | leader-led **6 處用 `alert()`**；全站其他頁用 **react-hot-toast**（`toast.error/success`，layout 已掛 `<Toaster>`） | `teams/new`、`teams/open`(含成功 alert)、`teams/[id]/applications`、`teams/[id]/candidates`、`me/teams` |
| 邀請卡**看不到人數** | `/me/teams` 待處理邀請區不顯示隊伍 confirmed/容量 → 按之前不知快滿；`MembershipDto` **無 `ConfirmedCount/RequireMembers`** 欄（只有 OpenTeam/LedTeam 有） | `me/teams` + `MembershipDto`/`TeamMembershipQuery` |
| **滿隊邀請的命運未定** | accept 失敗後 invite 在 DB 仍 `Invited` → 刷新也不消失，使用者對著一張**永遠接受不了**的邀請 | 產品決策 |

## 2. Tier 1：純前端、低風險（建議先做）

- **失敗也刷新**：mutation 由「`onSuccess` 才 invalidate」改成 **`onSettled` 成功/失敗都 invalidate**（或 `onError` 補 invalidate）。讓計數/狀態與伺服器對齊。
  - 註：對「滿隊 invite」這條，刷新 `myInvitations` **不會**讓該 invite 消失（仍 `Invited`）——那是 Tier 3 的產品問題，Tier 1 只保證「其他狀態不 stale」。
- **`alert()` → `toast`**：6 處全換 react-hot-toast（`toast.error(msg)`；`teams/open` 成功那則用 `toast.success`）。跟全站一致、非阻塞。
- **影響檔案**：上表 5 個頁。**工作量**：小。**風險**：低（純呈現層）。

## 3. Tier 2：讓使用者「按之前就知道」（小後端 + 前端）

- **邀請卡顯示隊伍人數**：`MembershipDto` 加 `ConfirmedCount` + `RequireMembers`，`TeamMembershipQuery.GetByDiscordIdAndStatusAsync` 的 SQL 補算（比照 `GetOpenTeamsAsync` 的 confirmed 子查詢）。前端邀請卡顯示 `confirmed/require`。
- **滿了就標示/禁用接受**：`confirmedCount >= requireMembers` → 接受鈕改「已滿」樣式 + `disabled`（仍保留拒絕）。把「按了才 400」前移成「按不下去」。
  - 註：仍是**快照**、非即時——真正的最後一位競爭仍靠後端裁決（Tier 1 的 toast 兜底）。
- **影響檔案**：`Application/DTOs/MembershipDto.cs`、`Infrastructure/Query/TeamMembershipQuery.cs`、`web/app/me/teams/page.tsx`、`web/types/leaderLed.ts`。**工作量**：小~中。**風險**：低。

## 4. Tier 3：滿隊邀請/申請的命運（產品決策，先不做）

問題：隊伍滿了之後，那些**還沒回應的 `Invited`/`Applied`** 該怎麼辦？選項：
- **(a) 不動**（現況）：使用者自己撞「已滿」。最簡單，但留一堆殭屍邀請。
- **(b) 滿員時自動作廢待處理**：`ConfirmMemberAsync` 讓隊伍達容量那刻，把該隊其餘 `Invited`/`Applied` 轉 `Rejected`（＋通知）。乾淨，但**若之後有人退隊、位子重開**，這些已被作廢者不會自動復活（可接受？）。
- **(c) 顯示「已滿」但不改狀態**：Tier 2 的禁用 + 一個「此隊已滿」標籤；狀態留著等隊長/系統清。
- 邊界：退隊→重開位子、隊長取消邀請、跨隊重疊（`uq_tsc_confirmed_overlap`）交互。

**建議**：先 (c)（＝Tier 2 就涵蓋），(b) 需想清楚重開位子的語意再做。

## 5. 相關（不在本計畫範圍）
- 後端「**鎖前容量預檢 fast-fail**」（已滿隊免進 advisory lock 排隊）——見併發討論，**YAGNI**，除非觀察到高併發鎖等待/`AdvisoryLockTimeoutException`。

## 6. 驗證
- **Tier 1**：手動觸發滿隊 accept → 看到 `toast.error("隊伍已滿。")`（非 alert）＋清單刷新；e2e 可加「滿隊 accept → toast + 卡片仍在」。
- **Tier 2**：邀請卡顯示 `confirmed/require`；滿隊時接受鈕 disabled；`MembershipDto` 新欄有整合/單元覆蓋。
- **回歸**：現有 leader-led e2e（Push/Pull）不破。

## 7. 待解問題
- Tier 3 走 (b) 還是 (c)？「位子重開」要不要復活被作廢的邀請？
- Tier 1 用 `onSettled` 全刷 vs `onError` 補刷（前者簡潔、happy path 多一次 refetch，可接受）。
