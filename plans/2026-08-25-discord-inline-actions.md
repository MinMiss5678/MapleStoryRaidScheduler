# Discord DM 內建按鈕：輕決定免離開 Discord（pilot＝邀請接受/拒絕）

> 輕量 plan（動手前 spec）：目標 / 範圍 / 關鍵決策 / 測試策略 / 驗收 / 工時。（無待你決策的未決 → 無風險段）
> 承接 `2026-08-25-notification-strategy.md`：那份把「輕決定」通知附連結回網頁；本份把**最高頻、最單純的「邀請接受/拒絕」**升級成 **DM 內按鈕**，玩家不用開網頁/登入。
> 關聯：`Infrastructure/BackgroundJobs/TeamNotificationOutboxHandler.cs`、`Infrastructure/Services/TeamLeaderService.cs`、`Application/Events/*`、bot `Presentation/`（DSharpPlus 5.0 nightly，已有 `IUnitOfWork`）。

## 目標

DM「你被邀請加入 X 隊」直接附「**接受 / 拒絕**」按鈕；點下去在 Discord 內完成，bot 走與網頁相同的 `AcceptInviteAsync`/`DeclineInviteAsync`，再把該則 DM 編輯成結果（停用按鈕）。

## 判準（為何選這個當 pilot）

依「Discord 就夠」五條件（見 notification-strategy）：二選一、context 自足、單筆、無輸入、一次完成 —— **邀請接受/拒絕全中**，且最高頻。開隊/挑候選/個人設定維持連結回網頁（要輸入/瀏覽）。

## 範圍

- **只做**：邀請 accept/decline 的 DM 按鈕 + 互動處理。
- **不做**（本 pilot）：轉讓、退團、申請審核的按鈕（驗證成功再逐一擴，共用同機制）；非可動作通知維持純文字。
- **純按鈕即可**：Discord components 現行客戶端（桌面/手機/網頁）全支援，無「舊客戶端渲染不出」問題 → 不放連結當雜訊。（真要備援只有「bot/互動故障回網頁」，YAGNI 先不做。）

## 關鍵決策

1. **Outbox 事件加結構化 action 中繼**：`TeamNotificationEvent` 增 `ActionKind`（`None`/`InviteResponse`）+ `MemberId`。`None`＝維持現行純文字；`InviteResponse`＝bot 渲染按鈕。**backend 組文字、bot 組按鈕**（按鈕是 Discord-specific，屬 bot；見 notification-strategy「bot 組的優點」）。
2. **按鈕 custom_id 編碼動作**：`inv:accept:{memberId}` / `inv:decline:{memberId}`。
3. **互動處理在 bot**：`ComponentInteractionCreated` handler → 解析 custom_id → **驗點擊者 discordId == 邀請 target**（`AcceptInviteAsync` 內已有 `member.DiscordId != current → Forbidden`，傳點擊者 id 即 enforced）→ 從 **per-event scope 注入的 scoped `IUnitOfWork`+`ITeamLeaderService`**（DSharpPlus 每事件自動開 scope,見 bot-di-scoping 決策 0）`BeginAsync` → `AcceptInviteAsync/DeclineInviteAsync` → `CommitAsync`（例外 `RollbackAsync`）→ **編輯原 DM**（停按鈕、顯示「已接受/已拒絕」）。**前置**：bot-di-scoping 先把 DB 鏈改 scoped → **免手動 `CreateScope`**(每事件 scope 已給新 DbContext,advisory lock 正常)。
4. **授權＝Discord 互動身分**：互動事件帶點擊者 user id，即身分，**免 JWT**。
5. **併發/冪等共用既有防護**：走 `ConfirmMemberAsync`（per-team advisory lock + 重讀容量 + xmin）→ 額滿/狀態已變 → 丟 `BusinessException`，bot **catch 後編輯訊息**（「此邀請已失效」）而非報錯。與網頁 accept 收斂到同一邏輯/狀態。
6. **DI**：bot 需註冊 `ITeamLeaderService`（+其相依）——確認/補上。
7. **DSharpPlus API（已驗 nightly-02542）**：按鈕 `new DiscordButtonComponent(DiscordButtonStyle.Success/Danger, customId, label)`；handler `IEventHandler<ComponentInteractionCreatedEventArgs>`（`e.Id`=custom_id、`e.User`=點擊者、`e.Interaction`=回應）；用 `e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage, DiscordInteractionResponseBuilder)` 編輯原訊息。DM 內按鈕 Discord 允許。⚠️ **3 秒回應窗**：處理可能超過 → 先 `DeferredMessageUpdate` ack、完再 edit。
8. **雙路徑一致（無新不變式）**：網頁與按鈕的 accept 走同一 `AcceptInviteAsync`；一邊先處理 → 另一邊再點 → 「狀態已變」→ bot catch 後編輯訊息（反之亦然）。既有 status 檢查/advisory lock/xmin 已 cover，不需新機制。

## 測試策略（互動無法自動化 → 已決定三層）

- **測試難**：Discord 互動無 test backdoor —— 互動是 Discord 伺服器產生、經 Gateway 推來（有簽章），**無法從外部偽造**；也**沒有 Discord CLI 能模擬點擊**（user token 去點＝selfbot 違 ToS）。HTTP-interactions endpoint 可 craft 假 POST 測，但本 bot 走 Gateway、不適用。
  → 策略：**把 handler 決策邏輯抽成純 seam**（DSharpPlus event args 通常 internal ctor、難 new，只當薄殼），分三層測：
  1. **單元**：`Parse(customId)`（"inv:accept:123"→動作+memberId）+ `Handle(action, clickerId)`（mock `ITeamLeaderService`：非本人擋下、正常呼叫、`BusinessException` 分流）。
  2. **整合**：`AcceptInviteAsync`/`ConfirmMemberAsync` 本體已有測試覆蓋（advisory lock/xmin/容量）。
  3. **薄殼**（收 `ComponentInteractionCreated` → 呼叫 `Handle` → `UpdateMessage`）：唯一無法自動化的一層 → **本機真 bot 手動點一次驗**（同 DM 錄影法）。

## 驗收

- [ ] 收到邀請 DM 含「接受/拒絕」按鈕。
- [ ] 點「接受」→ DB 該 member `Confirmed`、DM 編輯為「已接受」且按鈕停用;點「拒絕」→ `Rejected`、訊息更新。
- [ ] 非 target 點擊 → 擋下（ephemeral 提示）。
- [ ] 已失效（額滿/已處理）再點 → 友善訊息、不例外。
- [ ] 單元：`Parse` custom_id + `Handle` 三分支（非本人/正常/已失效）綠;既有 accept/confirm 整合測試不回歸;薄殼本機手動驗一次。
- [ ] 本機真 bot 手動驗一輪（截圖）。

## 工時估
- Outbox 結構化 + backend 標 ActionKind ≈ 半天;bot handler（渲染按鈕 + 互動 + 交易 + 編輯訊息）≈ 1 天;測試 + 手動驗 ≈ 半天。

## 非範圍（YAGNI）
- 不做 embed 美化/縮圖（先純文字 + 兩顆按鈕）。
- 不把「開隊/挑候選/設定」搬進 Discord（要輸入/瀏覽，網頁本來就對）。
- 其他可動作通知（轉讓/退團/審核）等 pilot 驗證後再共用機制擴。
