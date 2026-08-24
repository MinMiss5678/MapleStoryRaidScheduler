# leader-led 通知策略：深連結 + 噪音精簡

> 輕量 plan（做完的 spec，記錄決策）：背景 / 決策 / 通知清單 / 實作 / 驗收。
> 關聯：`Infrastructure/Services/TeamLeaderService.cs`（`NotifyAsync`）、`Application/Options/AppOptions.cs`、`Presentation.WebApi/Program.cs`、`compose.yaml`/`k8s/backend.yaml`。DM 架構＝Transactional Outbox（見 leader-led §11）。
> 互補計畫：`2026-08-07-dm-notification-api-call-reduction.md`（那份講「送得省」＝減少 Discord REST；本份講「送什麼／送去哪」。該計畫未完成的「優化 3：同人彙整減少淹沒」，本份用**從源頭移除低價值通知**部分達成 → 剩的量若仍痛再談彙整）。

## 背景

leader-led 是「隊長開條件、系統篩候選」→ 隊長常**大量邀請**（Pull）、玩家常**申請多隊**（Push）。原本每個狀態轉移都發一則 DM → 兩個問題：
1. **淹沒**：隊長收一堆「某人拒絕/某人接受」、玩家收一堆「某隊未通過」。
2. **連結不精準**：DM 只附 app 根網址，點進去還要自己找那筆要處理的。

## 決策

### A. 深連結（依動作導到可操作頁）
`NotifyAsync` 加 `path` 參數，訊息末尾接 `{AppUrl}{path}`。
- **玩家** → `/me/teams`（單一清單頁，接受/拒絕/退團/回應轉讓都在這；**無 per-team 玩家頁 → 不需帶 team id**）。
- **隊長** → per-team 頁 `/teams/{id}/applications`（審核）、`/teams/{id}/candidates`（補人）、或 `/me/led-teams`（hub）。

### B. 噪音精簡（只在「需動作」或「正面/低頻」時才發）
移除「你沒進隊」類（拒絕/自動失效）與逐筆隊長 spam；額滿改**一次**。

## 通知清單（定案）

**保留**

| 通知 | 對象 | 深連結 | 理由 |
|---|---|---|---|
| 你被邀請 | 玩家 | `/me/teams` | 需接受/拒絕 |
| 有人申請 | 隊長 | `/teams/{id}/applications` | 需審核 |
| 申請通過、入隊 | 玩家 | `/me/teams` | 正面、想知道 |
| 有成員退團、位子重開 | 隊長 | `/teams/{id}/candidates` | 低頻、可補人 |
| **隊伍滿員（一次）** | 隊長 | `/me/led-teams` | 取代逐筆「有人接受」 |
| 隊伍解散 | 玩家 | `/me/teams` | 低頻、影響玩家 |
| 轉讓邀你 / 轉讓已接受 / 被拒 | 玩家 / 舊隊長 | `/me/teams`、`/me/led-teams` | 需動作 / 罕見 |

**移除（噪音）**

| 通知 | 原因 |
|---|---|
| 邀請被拒 → 隊長 | 大量邀請下純噪音；拒絕狀態 UI 可見 |
| 邀請被接受（逐筆）→ 隊長 | 改「額滿一次」 |
| 申請未通過 → 玩家 | 申請多隊被拒會淹沒；UI 可見 |
| 邀請自動失效（額滿）→ 玩家 | 同「被拒」類噪音；UI 可見 |

## 實作

- `NotifyAsync(int bossId, DateTimeOffset slot, ulong target, string path, Func<...> buildMessage)`：`message += "\n" + _appUrl + path`（`_appUrl` 空則不附）。12 個呼叫點各帶對應 path。
- `AppUrl` 由 `IOptions<AppOptions>` 注入（`Program.cs` 綁 `GetSection("App")`；backend 環境變數 `App__AppUrl` 補進 `compose.yaml` + `k8s/backend.yaml`——backend 原本只有 bot 有）。
- 「隊伍滿員 → 隊長」放在 `ConfirmMemberAsync` 的額滿分支（accept/approve 共用，在 advisory lock 內 → 一次、不競態）；同分支的待接受邀請照樣撤銷、但**不再 DM 那些玩家**。
- 移除的通知：刪對應 `NotifyAsync` 呼叫（`RejectAsync` 的 `EnsureLeaderOwnsTeamAsync` 回傳改丟棄）。

## 驗收

- [x] 單元 277 綠；`Notification_AppendsAppUrlLink` 驗訊息含連結。
- [x] 深連結端到端：live Outbox 訊息 = 「隊長邀請你加入…\n{AppUrl}/me/teams」。
- [x] 移除的通知無測試依賴；E2E 不驗 DM → 可觀察行為不變（leader-led 5/5 綠）。
- [ ] prod：`App__AppUrl` 需在 k8s backend 生效（已補 manifest，部署時驗）。

## 非範圍（YAGNI）
- 玩家端 `#team-{id}` anchor 深捲：單公會邀請少、清單短 → 不做。
- 同人彙整/批次：待量成痛點（見舊 DM 計畫優化 3）。
