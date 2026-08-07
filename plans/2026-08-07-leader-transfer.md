# leader-led：隊長轉讓（需對方同意）

狀態：**規劃中（尚未實作）**。從退隊討論延伸——隊長想把隊交給別人帶，但**對方要同意**（不能把責任硬塞給不願意的人，與 §9.1「入隊需雙方同意」同精神）。

現況：`TeamSlot.LeaderDiscordId` = 隊長（FK `REFERENCES Player(DiscordId) ON DELETE SET NULL`）。leader-led **不分權**（§5，任何登入者可開隊/當隊長）。無轉讓機制。

## 設計：兩步握手（提議 → 同意），形狀同 invite/accept

- **資料**：`TeamSlot` 加一欄 **`PendingLeaderDiscordId bigint NULL`**（`REFERENCES Player(DiscordId) ON DELETE SET NULL`）。**一欄搞定**，不用新表（一隊同時最多一個待處理轉讓）。
- **狀態流**：
  - 現任隊長**提議**轉給 B → `PendingLeaderDiscordId = B`。
  - B **接受** → `LeaderDiscordId = B`、`PendingLeaderDiscordId = null`。
  - B **拒絕** → `PendingLeaderDiscordId = null`（`LeaderDiscordId` 不變）。
  - 新提議**覆寫**舊 pending（隊長改主意；一次一個）。
- **並發**：`xmin` 樂觀鎖更新 `TeamSlot`（**免 advisory lock**——非容量競爭、低併發）。

## API
- `POST /api/teamSlot/{id}/TransferLeader { toDiscordId }` — 現任隊長提議。
- `PUT  /api/teamSlot/{id}/TransferLeader { action: "accept"|"decline" }` — 被指定者回應。
- `GET  /api/Me/LeaderTransfers` — 我收到的待處理轉讓（`PendingLeaderDiscordId = 我`），供收件匣。

## 服務 / 授權
- `ProposeTransferAsync(teamSlotId, toDiscordId, currentDiscordId)`：驗 `LeaderDiscordId == current`（只現任能提議）；`toDiscordId` 須存在 Player（否則 400/404）；不可提議給自己。設 `PendingLeaderDiscordId`。**通知 B**（outbox DM：「X 想把「王」時段的隊長轉給你，請至站內接受或拒絕」）。
- `RespondTransferAsync(teamSlotId, currentDiscordId, action)`：驗 `PendingLeaderDiscordId == current`（只被指定者能回）。accept → 換 `LeaderDiscordId`；decline → 清 pending。**通知原隊長**結果。
- **對象範圍（MVP）**：先限「**轉給本隊 `Confirmed` 成員**」（roster 挑，保證存在＋在隊＋透明化下看得到身分）。**擴充**：任意 Player（by discordId/搜尋）——YAGNI 先不做。

## 前端
- **帶隊 hub**（`/me/led-teams`）：每張我開的隊卡加「**轉讓隊長**」→ 從 `Confirmed` 成員挑一個 → 提議 → `toast`。
- **收件匣**：被指定者在「我的隊伍」加一區「**待處理隊長轉讓**」（或比照邀請卡）→ 接受/拒絕 → `toast` + invalidate（接受後該隊出現在其帶隊 hub）。
- 需要一支「本隊 Confirmed 成員清單（含 discordName）」給挑人用——透明化後可回身分（§9.11 已成隊成員彼此可見）。

## 邊界 / 待解
- **隊長退隊 vs 轉讓**：`LeaderDiscordId` 與「是否為 Confirmed 成員」是兩回事——隊長可不打（掛名帶隊）。**要退出前建議先轉讓**；若直接放著，隊仍有主。
- **原隊長退公會**（Player 刪 → `LeaderDiscordId` SET NULL、隊變未認領）：此時若有 pending，B accept 能否認領空缺隊？（傾向可——等於認領）。邊界，實作時定。
- 提議給自己 / 給非成員（MVP 限成員）→ 擋。
- pending 期間原隊長照常有隊長權（未 accept 前不變）。

## 測試
- 單元：propose 設 pending + 授權（非隊長擋）；accept 換 leader + 授權（非被指定者擋）；decline 清 pending；提議給自己/非成員擋。
- e2e：隊長 A 提議轉給成員 B → B 在收件匣接受 → 該隊出現在 B 的帶隊 hub、A 的帶隊 hub 不再有（或改顯示為成員）。

## 關聯
- 退隊/退團率/去重/透明化見 `plans/2026-08-07-leave-team-and-candidate-dedup.md`（透明化讓「挑成員轉讓」看得到身分）。
