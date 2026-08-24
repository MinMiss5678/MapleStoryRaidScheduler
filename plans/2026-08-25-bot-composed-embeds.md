# bot 組 embed：結構化通知 + 豐富呈現（首發＝邀請 DM 顯示成員職業/攻擊力）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時。（無待你決策的未決 → 無風險段；相容/embed 上限/快照/payload 皆已決，見決策）
> 定位：把通知的**訊息組裝從 backend 搬進 bot**（Outbox 改帶結構化資料、bot 渲染 embed + component）。這是 `notification-strategy.md` 講的「bot 組的優點」正式版、也是 `discord-inline-actions.md`「backend 組文字、bot 組按鈕」分工的**升級**（改成 bot 全權組）。
> 首個 rich 通知：**邀請 DM 用 embed 列出該隊目前成員的職業 + 攻擊力**，讓被邀玩家決定前看得到隊伍組成。

## 目標

Outbox 事件帶**結構化資料**（type + 參數），bot 依 type **組 embed（欄位/對齊/顏色）+ component**。邀請 DM 呈現「目前成員：職業 × 攻擊力」表格 + 接受/拒絕按鈕。

## 背景

現行：backend 在 `TeamLeaderService.NotifyAsync` 組**成品字串**存 Outbox（`TeamNotificationEvent { TargetDiscordId, Message }`），bot 照送 `e.Message`。純文字能塞成員清單（見 discord-inline-actions ①），但**對齊/美觀受限**、且措辭邏輯散在 backend。要 embed 表格 → 只有 bot 有 Discord embed API → 得把組裝搬過去。

## 決策

1. **Outbox 事件結構化**：`TeamNotificationEvent` 從「一坨 Message」改成 `{ TargetDiscordId, Type(enum: Invited/ApplyReview/…), + 該 type 需要的參數 }`。
2. **資料用「入列時快照、denormalize 進 payload」**（不讓 bot 查 DB）：backend enqueue 時把 bot 渲染要的資料**一次撈好塞進事件**——例：邀請事件帶 `BossName / SlotDateTime / MemberId / Roster:[{Job, AttackPower}]`。理由：
   - bot 渲染**不碰 DB** → 不依賴 `bot-di-scoping`（那個是「按鈕點擊要改 DB」的路，與「渲染」分開）。
   - DM 本來就是**快照**（發出後 roster 變動不回溯）→ 入列時快照語意剛好一致。
   - backend 本來就要多查 roster（不管放哪都要查），放 payload = 查在 backend、bot 純渲染。
   - payload 大小上界＝該王 `RequireMembers`（每王不同、頂多幾十人）→ KB 級，對 JSON 欄位/序列化可忽略。
3. **bot 依 type 組 embed + component（已驗 nightly-02542 API）**：`DiscordEmbedBuilder` → `.WithTitle(王名)`、`.WithColor(new DiscordColor("#rrggbb"))`、`.AddField(name, value, bool inline)`、`.WithFooter/WithDescription(缺額)`;**成員表用「一個 field、value 塞多行字串」**（`"英雄 5400\n夜使者 4800\n…"`、`inline:false`）→ 避開 25 欄位上限、幾十行仍在欄位 1024 字元內。掛到 `DiscordMessageBuilder().AddEmbed(embed).AddActionRowComponent(buttons)`（buttons 見 discord-inline-actions）送出。非可動作類給簡單 embed 或純文字。
4. **相容：事件加版本 / 保留 Message fallback**：滾動部署時 backend/bot 版本錯開 → 新事件 bot 認得、舊事件（純 Message）走 fallback 照送。避免部署窗口漏訊。

## 範圍

- 改 `Application/Events/TeamNotificationEvent`（結構化 + 版本）。
- backend `NotifyAsync`：改 enqueue 結構化事件（邀請類先撈 roster 快照）。
- bot `TeamNotificationOutboxHandler`：依 type 組 embed/component 送出；未知/舊版 → fallback。
- 首發只做**邀請**的 roster embed；其餘通知先維持純文字（漸進遷移，共用同機制）。

## 驗收

- [ ] 邀請 DM 顯示 embed：標題王名 + 成員「職業 攻擊力」表 + 缺額 + 接受/拒絕按鈕。
- [ ] 舊版純 Message 事件（fallback）仍正常送出（模擬部署窗口）。
- [ ] roster 快照正確（入列當下成員）；空隊/滿隊邊界不炸。
- [ ] 單元：事件→embed 映射（給定結構化事件產出預期 embed 欄位）;bot handler type 分派 + fallback。
- [ ] 本機真 bot 手動驗 embed 呈現一輪（截圖）。

## 工時估
- 事件結構化 + backend enqueue 改 + roster 快照查 ≈ 半天;bot embed 渲染 + fallback + type 分派 ≈ 一天;測試 + 手動驗 ≈ 半天。

## 關聯與順序

- **與 `discord-inline-actions`**：那份「backend 組文字 + bot 組按鈕」是輕量起步;本份把**文字也搬進 bot（embed）**。可先做 discord-inline-actions（純文字+按鈕）驗互動,再用本份升級成 embed;或直接一次做 embed 版。
- **與 `bot-di-scoping`**：**渲染路徑不需要**（本 plan 決策 2 不查 DB）;但**按鈕點擊改 DB 的路徑**仍需 bot-di-scoping。兩者正交。
- **與 `notification-strategy`**：本份是其「bot 組」未來的落實;噪音精簡（誰該收）已在那份定案,本份只管「收到的怎麼呈現」。

## 非範圍（YAGNI）
- 不做即時 roster（發出後自動更新）、不做 per-team 玩家即時頁、DM 不連即時頁 —— 快照夠決策,尋隊看組成已有 `/teams/open`。
- 不一次把 12 種通知全 embed 化;先邀請一種,驗證再擴。
- 不引入多通道（email 等）;結構化事件為未來鋪路但先只渲染 Discord。
