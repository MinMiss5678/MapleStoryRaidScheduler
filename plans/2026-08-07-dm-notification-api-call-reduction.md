# leader-led DM 通知：減少 Discord API 呼叫次數

狀態：**規劃中（YAGNI，尚未實作）**。這是**未來優化**——現況一次一人、偶發通知，離任何限流極遠，先不做；本文件備妥「量大了照這個做」。

關聯：`Infrastructure/Services/DiscordService.cs`、`Infrastructure/BackgroundJobs/TeamNotificationOutboxHandler.cs`、`OutboxDispatcher.cs`。DM 架構＝Transactional Outbox（見 leader-led §11）。

## 1. 背景與現況基準

發一則 DM 目前打 **3 次 Discord REST**（本機 bot log 實測序列）：

| # | 呼叫 | 說明 | 可省？ |
|---|---|---|---|
| — | `GetGuildAsync(GuildId)` | 取公會 | 已由 gateway 快取供應、**不打 REST** |
| 1 | `GET /guilds/{guild}/members/{user}` | 取公會成員物件 | ✅ 走成員快取可省 |
| 2 | `POST /users/@me/channels` | 開/取 DM 頻道（回 channel id） | ✅ DM 頻道 id 持久、可快取可省 |
| 3 | `POST /channels/{dmChannelId}/messages` | 真正送訊息 | ❌ 發訊本體，不可省 |

理論下限 = **1 次呼叫**（只剩 #3）。

現行程式（`SendDirectMessageAsync`）：
```csharp
var guild  = await _discordClient.GetGuildAsync(GuildId);   // 快取
var member = await guild.GetMemberAsync(discordId);          // ← #1 REST fallback
await member.SendMessageAsync(message);                      // ← #2 開 DM + #3 送
```

## 2. 觸發條件（何時才該做，YAGNI 閘門）

以下**任一**成立才實作，否則不動：
- 出現**大量扇出**需求（如整團/全服廣播通知，一次數十~數百人）。
- DM 量成長到會逼近**全域 50 req/s**（每則 ~3 呼叫 → ~16 則/秒天花板）。
- 因關閉私訊者眾，**403 累積**逼近「10,000 無效請求 / 10 分鐘 → 暫時封 IP」。

（限流事實見 Discord 官方 `topics/rate-limits`：全域 50/s、per-route 走 header、10k 無效/10min 封 IP；發訊/DM 無固定公開上限。）

## 3. 三項優化（按 CP 值排序）

### 優化 1：快取 DM channel id（3 → 1，最划算）
- **現況**：每則都 `POST /users/@me/channels` 重新開頻道（#2）。
- **做法**：DM 頻道對同一人 **id 固定不變**。維護 `userId → dmChannelId` 快取，命中就直接 `POST /channels/{cachedId}/messages`，**跳過 #2**。
- **省下**：重複發給同一人（如隊長反覆收申請/邀請回覆）→ 每則少 1 呼叫。
- **取捨/風險**：
  - 快取位置：記憶體（per bot pod，簡單、重啟即失、多 pod 各建各的，可接受）vs DB（跨 pod 共用、多一張表/欄）。**建議先記憶體**（KISS）。
  - 失效：頻道 id 幾乎不變；萬一送 #3 拿到 404（頻道失效）→ 清該筆快取、退回開頻道重試一次。
- **影響檔案**：`DiscordService.cs`（加快取 + 送訊改走 cached channel）。
- **工作量**：小（純 bot 內、不動 outbox 契約）。

### 優化 2：成員查詢改「快取優先」（省 #1）
- **現況**：`guild.GetMemberAsync` 會 REST fallback（#1）。
- **做法**：bot 已開 **GuildMembers intent** + 常駐 gateway → 成員多半已在本地快取。改成**先查快取**（cache-only 取，如 `guild.Members` dict / `TryGetMember`），命中不打 #1；沒命中才 REST fallback（正確性不變）。
- **省下**：常見情境省掉 #1；配合優化 1，**熱路徑可達 1 呼叫**。
- **取捨/風險**：
  - 依 DSharpPlus **v5** 實際 API 名（`Members` / `TryGetMember` / cache-only overload）**實作時需核版本**，別假設方法名。
  - 快取可能未含某成員（剛加入/未 chunk）→ fallback REST，無正確性問題。
  - 記憶體：快取全公會成員有記憶體成本；bot 目前 limit 128Mi，公會不大應無虞，但**大型公會要評估**。
- **影響檔案**：`DiscordService.cs`。
- **工作量**：小~中（要對 DSharpPlus v5 快取 API）。

### 優化 3：同一人的通知「彙整/防抖」（砍訊息則數本身）
- **現況**：每個事件一列 outbox、一則 DM。同一人短時間多事件 → 多則 DM（多次 #3）。
- **做法**：把**同一收件人短時間窗內**的多則通知**合併成一則摘要 DM**（例：隊長 1 分鐘內收到 5 筆申請 → 發 1 則「你有 5 筆新申請，請至站內審核」）。
- **省下**：直接減少 #3 的則數，也降低「多則短時間狂發」撞限流/被判 spam 的風險。
- **取捨/風險**：
  - **這是產品/UX 改動**，非純技術：通知語意從「逐筆」變「摘要」，站內清單仍是權威真相（符合 §11）。
  - 實作點選一：**(a) enqueue 端防抖**（`TeamLeaderService.NotifyAsync` 對同 target 短時間去重/合併，較難、要跨請求狀態）或 **(b) dispatch 端分組**（`OutboxDispatcher` 撈批時把未處理的 `TeamNotification` 依 `TargetDiscordId` 分組、合併訊息後一次送、一起標 processed）。**建議 (b)**——集中在 dispatcher、不散進業務層，且天然吃「一批未處理」。
  - 改動 outbox handler 契約（從「一列一送」變「一組一送」），要保留單列 fallback。
- **影響檔案**：`OutboxDispatcher.cs` + `TeamNotificationOutboxHandler.cs`（或新增 batch handler）。
- **工作量**：中~大（動 dispatch 流程 + 訊息彙整格式 + 測試）。

## 4. 建議實作順序
1. **優化 1（DM channel 快取）** — 最划算、風險最低、純 bot 內。
2. **優化 2（成員快取優先）** — 併同 1 做，熱路徑降到 1 呼叫。
3. **優化 3（同人彙整）** — 只有在「同一人短時間多通知」成為實際痛點時才做（產品決策 + 較大改動）。

1+2 屬「同一批 bot 內優化」，可一個 PR；3 獨立評估。

## 5. 驗證
- **呼叫數**：實作前後各發同批 DM，比對 bot log 的 REST 呼叫序列（#1/#2 應消失於快取命中路徑）。
- **正確性**：DM 仍送達（含快取 miss → REST fallback 分支）；關 DM(403)/退公會(404) 仍照現有 handler 略過、不重試。
- **回歸**：現有通知單元測試（Phase 2b）不破；優化 3 另加「多列同 target → 合併一則」測試。

## 6. 待解問題
- DSharpPlus **v5 nightly** 的成員/ DM 頻道**快取 API 確切名稱與行為**（實作時查）。
- 優化 1 快取要不要跨 pod（先記憶體、不跨；量大再考慮 DB/Redis）。
- 優化 3 的彙整**時間窗**與**訊息格式**（產品決定）。
