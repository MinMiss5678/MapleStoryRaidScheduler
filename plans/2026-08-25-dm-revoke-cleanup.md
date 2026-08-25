# 自動撤邀時編輯 DM（消死按鈕）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時 / 已知邊界。
> 觸發：邀請被**自動撤銷**（非本人操作）時，被邀者 DM 仍留著可點的接受/拒絕按鈕（死按鈕），點了才回「找不到對應項目」。要改成撤銷當下**編輯該 DM**成「此邀請已失效」+ 移除按鈕。
> 適用**兩條**自動撤銷路徑：既有「總容量額滿撤全部」（`ConfirmMemberAsync` line 362-370）+ 新的 [[composition-quota]] 「per-job 名額滿撤同職業」。獨立於 composition-quota，但天然搭配。
> 關聯：`Infrastructure/Services/DiscordService.cs`、`Infrastructure/BackgroundJobs/TeamNotificationOutboxHandler.cs`、`Infrastructure/Services/TeamLeaderService.cs`、`Domain/Entities/TeamSlotCharacter`、`Application/Events/TeamNotificationEvent.cs`。

## 目標

被邀者的 DM 邀請被自動撤銷時，bot **主動編輯**那則 DM：內容改「此邀請已失效（隊伍已滿／該職業已滿）」、**移除按鈕**、**保留 embed**（隊伍/時間資訊仍有參考價值，且刪整則訊息會憑空消失、更困惑 → **編輯不刪除**）。

## 背景 / 難點

- 自動撤銷是**別人 confirm 觸發**、**無互動 context**（不像本人點按鈕那條有 `EditOriginalResponseAsync`）→ bot 要主動改某則 DM，需知道 **message id**。
- DM 走 **outbox 非同步派發**（`TeamNotificationOutboxHandler` 送、與建立邀請的 service 隔一層）→ 送出後拿到的 message id 要**倒流回 DB**；且 `ConfirmMemberAsync` 可能跑在 **WebApi**（無 `DiscordClient`）→ 編輯動作也必須**走 outbox**回到 bot，不能在 service 內直接呼叫 Discord。

## 決策

1. **存 message id**：`TeamSlotCharacter`（邀請列）加 `DmMessageId bigint NULL`。**只有 actionable（帶按鈕）DM** 需要存；純通知 DM 不動。channel id **不存**——由被邀者 discord id 重開（DiscordService 已快取 userId→DM channel）。
2. **送出時捕捉 id**：actionable 的 `SendDirectMessageAsync(embed, buttons)` 由回 `Task` → 改回 **`Task<ulong>`**（送出 message id）。`TeamNotificationOutboxHandler` 送完 → **寫回** `TeamSlotCharacter.DmMessageId WHERE Id = ActionId`（事件的 `ActionId` 就是邀請列 id、也是按鈕帶的 id）。bot 有 DB 連線可寫。
3. **撤銷 → 走 outbox 編輯**：`ConfirmMemberAsync` 兩條撤銷路徑（總容量、per-job）撤邀時，對每筆被撤邀請 **enqueue 一個「撤邀清理」outbox 事件**（帶 `DiscordId` + `DmMessageId`）。bot 端 handler → `DiscordService.EditDirectMessageAsync(discordId, messageId, "此邀請已失效…")`：開 DM 頻道（快取）→ 取訊息 → `ModifyAsync`（改內容、清 components、保留 embed）。
4. **編輯不刪除**（見目標）。
5. **id 尚未回寫的殘留**：若撤銷當下 `DmMessageId` 仍 `NULL`（極短競態：邀請 DM 還沒派發就被撤）→ **跳過清理**（退回現況死按鈕，點了回「已處理」）。可接受、罕見。

## 範圍

- **migration**：`ALTER TABLE "TeamSlotCharacter" ADD "DmMessageId" bigint NULL`。
- `DiscordService`：`SendDirectMessageAsync(embed, buttons)` 回傳 message id；新增 `EditDirectMessageAsync(discordId, messageId, content)`（保留 embed、清按鈕）。
- `TeamNotificationOutboxHandler`：送 actionable DM 後寫回 `DmMessageId`；處理新「撤邀清理」事件 → 呼叫 `EditDirectMessageAsync`。
- `TeamNotificationEvent`：加「撤邀清理」事件型別（`DiscordId` + `DmMessageId`）。
- `TeamLeaderService.ConfirmMemberAsync`：兩條撤銷路徑撤邀時，撈被撤邀請的 `DiscordId`+`DmMessageId` → enqueue 清理事件。
- `TeamSlotCharacterRepository`：`RevokePendingInvites*` 回傳被撤列的 `(DiscordId, DmMessageId)`（供 enqueue）。

## 驗收

- [ ] 總容量撤銷：邀 3 人、額滿 → 其餘待接受者 DM **自動變「此邀請已失效」+ 無按鈕 + 保留 embed**（不需對方操作）。
- [ ] per-job 撤銷（搭 composition-quota）：邀 3 黑騎士、1 個接受填滿黑騎士名額 → 另 2 人 DM 自動失效。
- [ ] 被邀者**自己點**接受/拒絕：仍走原互動編輯（不受本變更影響）。
- [ ] `DmMessageId` 尚 NULL 就被撤 → 不爆、跳過清理（死按鈕退化、可點回「已處理」）。
- [ ] DM 已被對方手動刪除 → 編輯遇 `NotFoundException` 吞掉、不爆。
- [ ] 單元（handler：寫回 id / 清理事件呼叫 Edit；Edit 對 NULL id 跳過）+ 整合（真 DB 撤邀 → 事件入 outbox）+ 本機真 bot 驗一次（DM 真的被編輯）。

## 工時估
- migration + DiscordService 兩改 + 回寫 + 新事件 + 撤銷路 enqueue + repo 回傳 ≈ 一天；測試 + 本機真 bot 驗 ≈ 半天。

## 已知邊界（非待決）
- 決策 5 的競態殘留：接受、與現行總容量撤銷的死按鈕行為一致，不惡化。
- 只 actionable DM 存 id；純通知 DM 不受影響。
- 依賴 [[composition-quota]] 才有 per-job 撤銷；但本 plan 對「既有總容量撤銷」即可獨立見效 → **可先於或獨立於 composition-quota 出**。
