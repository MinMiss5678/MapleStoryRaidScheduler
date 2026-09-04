# 多機器多 pod outbox 派發（SKIP LOCKED · 真分散式驗證）

> 輕量 plan（動手前 spec）。定位**誠實**：這是**學習 / 履歷佐證**用的實驗，**不是 prod 需求**——prod 是單節點、bot 1 replica，且 DM 派發吞吐天花板是 Discord per-token rate limit（多 pod 不會更快）。本實驗要證的是**正確性**：`FOR UPDATE SKIP LOCKED` 在**真多節點多 pod** 下 exactly-once、免 Leader Election、pod/node 故障可容錯。

## 目標

1. 把 outbox 派發器拆成 **dispatch-only 角色**，跑 **N replica、跨 ≥2 台機器（node）**，驗證：
   - **每筆 outbox 恰派發一次**（無重複、無遺漏）；
   - **無任何 Leader Election / 協調**；
   - 多 pod 共用一顆 token 送 DM 的 **429 只影響吞吐、不影響正確性**。
2. **Chaos**：派發中途殺 pod / 關一台 node → 驗 **at-least-once**（被鎖但未 commit 的列隨 tx rollback 釋放鎖，被別的 pod 接手），最終 count 不變、無重複。

## 背景 / 為何現在直接加 replica 會壞

- bot 現況是**單一 Deployment**：`DiscordBotService`（連 gateway）+ `OutboxDispatcher`（派發）+ 其他 poller **綁在同一行程**，`replicas: 1`。
- **同一顆 Discord token 只能有一個 gateway session**（非分片）→ 天真地 `replicas: 3` → 三個都連 gateway → 互踢。
- 但**派發（發 DM）走 Discord REST，不需要 gateway** → 只要把 dispatcher 從 gateway 拆開、改走 REST-only，就能安全多 pod。

## 關鍵前置發現（讀 code + 查 DSharpPlus 5.0 確認）

`Infrastructure/Services/DiscordService.cs`：送 DM 注入 gateway 的 `DiscordClient`，`OpenDmChannelAsync` 靠 gateway 成員快取（`guild.Members`，見該檔 line 109-110 註解）。
→ 但 **DSharpPlus 5.0 已把 REST 併進 `DiscordClient`（移除獨立 `DiscordRestClient`）**，REST-only = **建 `DiscordClient` 但不呼叫 `ConnectAsync()`**（不連 gateway → 同 token 不撞）。所以**不需重寫成新 client**，改動比想像小；**REST 與快取可共存**——熱路徑主快取是 per-pod 的 `_dmChannelCache`（與 gateway 無關、照留），只有 gateway 的「成員快取」在 REST-only 為空（但每人只在首次開 DM 頻道查一次成員、命中後不再查，損失極小）。

## 設計 / 工項（由前置到驗證）

### 1. dispatcher 的 REST-only 送出（比原估輕）
- dispatcher 角色:建 `DiscordClient` **不呼叫 `ConnectAsync()`** → 用它的 REST 方法送 DM，無 gateway、同 token 不撞。**不需新 client 類別。**
- 調 `OpenDmChannelAsync`:REST-only 下 `guild.Members` 為空（且 REST 取得的 guild `_members` 可能為 null → 對它 `GetMemberAsync` 恐丟 NRE），改**直接 REST 抓 member + `CreateDm`**；**保留 per-pod `_dmChannelCache`**（首次開頻道後熱路徑仍只 1 次 REST）。
- 依角色 DI：gateway 角色沿用原版 `DiscordService`，dispatcher 角色用 REST-only 版（或同一實作、依有無 Connect 分支 `OpenDmChannel` 路徑）。

### 2. 角色拆分（同一份 code，靠設定旗標）
- `Role=Gateway`：註冊 `DiscordBotService`（連 gateway）+ interaction handler；**不跑** OutboxDispatcher。
- `Role=Dispatcher`：註冊 `OutboxDispatcher`（+ 選 retention/cleanup）+ REST-only DiscordService；**不連 gateway**。
- Program.cs 依 `DISPATCH_ROLE` 環境變數選擇 `AddHostedService<>` 與 `IDiscordService` 實作。
- 結果:同 token 也不撞（只有 gateway 角色連 gateway）。

### 3. 送出安全（demo 不洗真使用者）
- Dispatcher 加 **dry-run / 測試 sink 模式**（`DISPATCH_DRYRUN=1`）：搶列 + 標記已派發，但**不真送**（或送到測試頻道）→ 純驗 SKIP LOCKED，不打真人、不吃 rate limit。
- 真送模式:REST **429 尊重 `Retry-After`**（多 pod 共用 token → 會有 429，屬預期）。

### 4. 多節點 k3s（真多機器）
- k3s **server + ≥1 agent 在不同機器**（EC2 t3.small ×2–3；prod 那台 2GB 太擠、且不動 prod）。
- 兩個 Deployment：`bot-gateway`（replicas=1）、`outbox-dispatcher`（replicas=N）。
- dispatcher 用 **`podAntiAffinity`** 把 replica **分散到不同 node**（確保真多機器、非全擠一台）。

### 5. 驗證 harness
- 種 **N（如 1000）筆** outbox 事件（dry-run 模式）。
- 起 **M 個 dispatcher pod 跨 ≥2 node**。
- 每 pod log 搶到的 outbox id → 彙總斷言：**每 id 恰一次、無重複、總數 = N**、全程無 Leader Election。
- **Chaos**：dispatch 中途 `kubectl delete pod`、甚至 `sudo systemctl stop k3s-agent`（關一台 node）→ 最終仍全數派發、無重複。

## 驗收

- [ ] N 筆 × M pod × ≥2 node（dry-run）→ **每筆恰一次、重複數 = 0、遺漏 = 0**，無選主。
- [ ] Chaos（殺 pod / 關 node 中途）→ 最終 count 不變、無重複（at-least-once + SKIP LOCKED 釋鎖接手）。
- [ ] 真送模式小量驗:429 時尊重 Retry-After、不失敗、不重複送。
- [ ] 記一組數據:N / M / node 數 / 耗時 / 429 次數 / 重複數(=0)——當履歷佐證。

## 非範圍 / 誠實取捨（YAGNI）

- **不追求吞吐線性提升**:Discord rate limit 是 per-token 天花板,多 pod 證**正確性**不是**更快**。結論要誠實這樣寫。
- **多節點對 SKIP LOCKED 正確性不是必要**(那是 DB 層性質、與 node 數無關);多節點加的是「真分散式 + node 故障容錯」的敘事與 HA demo。純驗正確性其實本機 `--scale` 就夠。
- **不改 prod**(維持單節點、bot 1 replica);本實驗是**獨立、時間盒**的環境,跑完即拆(省成本)。

## 風險 / 待確認

- **REST-only 送出（✅ 已實測確認 2026-09-04）**:spike 用 `AddDiscordClient` 建 client、**不 ConnectAsync**，`GetUserAsync(id)` → `CreateDmChannelAsync()` → `SendMessageAsync()` **真送成功、免 gateway**（不撞 prod bot 的 gateway session）。`GetUserAsync` REST/cache-aware、繞過會 NRE 的 `guild._members`。同一份 `OpenDmChannelAsync`（GetUserAsync 版）兩角色通用。見 [DSharpPlus#309](https://github.com/DSharpPlus/DSharpPlus/issues/309)、[discussions/990](https://github.com/DSharpPlus/DSharpPlus/discussions/990)。
- ⚠️ **新 gotcha（spike 抓到）**：**從沒 ConnectAsync 的 `DiscordClient` 被 DI dispose 時，`DisconnectAsync()` 丟 NRE**（nightly：dispose 假設已連線）。不影響送出（只在關機 dispose 時），但 dispatcher 角色關機會噴未處理例外 → 需繞掉（不讓 DI dispose 該 client / shutdown 前處理 / 待 nightly 修）。
- ⚠️ **DM 送出未被 CI E2E 覆蓋**（只在真 Discord 走）→ 改 `OpenDmChannelAsync` 後**必須真送一次 DM 驗證**才可 ship（spike 已驗過送出本身）。
- 多 pod 共用一顆 token 的 **REST 429 密度**;dry-run 模式可完全繞開（純 DB 驗）。
- k3s **跨機器 join**:node token、防火牆(6443 API、8472/UDP flannel VXLAN、10250 kubelet)。
- outbox 目前是「bot 行程專屬」註冊(`TeamNotificationOutboxHandler` 註解);拆角色後確認 dispatcher 角色仍正確載入該 handler、gateway 角色**不**重複跑派發(否則兩邊搶——雖 SKIP LOCKED 也安全,但語意上 dispatcher 專責較清楚)。
