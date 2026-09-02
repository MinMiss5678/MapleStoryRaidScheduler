# leader-led 檢視同步（輪詢，非 realtime 連線）

> 輕量 plan（動手前 spec）。定位：把「他人 out-of-band 改動」反映到隊長/玩家開著的頁，**用最便宜、多 pod 天生安全的手段**，不上有狀態連線。

## 問題

leader-led 的狀態會被**別的行程/別人**改動，但開著的網頁不會自己更新，要手動重整：

- 玩家在 **Discord DM 按接受**（bot 行程）→ 隊長開著的**候選頁**仍顯示「待回覆」。
- 關鍵事實：**隊長「不會」逐筆收到「有人接受」的 Discord 通知**——這是刻意 anti-flood（見 `Infrastructure/Services/TeamLeaderService.cs:453`、額滿才一次性通知）。所以**網頁是隊長唯一能看到「目前誰接受了」的窗口**，它不即時 → 隊長不知道該不該繼續邀其他職業。
- 同理：申請（Push）、退隊、轉讓也都會 out-of-band 改動對應頁。

## 目標

1. **隊長不盯著網頁、離開做別的事、間歇回到候選頁瞄**時，**候選頁回來即為最新**——不論「切別的 App 再回來」（focus 重抓）或「開著頁偶爾瞄」（慢速可見輪詢），都不用手動重整。
2. 涵蓋兩種團的 ~5 分招募節奏。
3. **只動候選頁一頁**；其餘頁面靠既有 mount 重抓，不改。全程**無伺服器有狀態連線、多 pod 天生安全、零新基礎設施**。

## 非範圍（YAGNI）與理由

- **不上 WebSocket / SignalR / gRPC(-Web)。** 單一公會、個位數併發、數秒新鮮度即可 → 輪詢的「浪費」在此規模可忽略（~個位數 req/s，壓測證明熱路徑扛到 500 VUs）。有狀態連線要多 pod backplane + 跨行程（bot 接受在 bot 行程、Hub 在 web 行程）+ WS 穿 Next/cloudflared，成本遠大於它省的。**Discord 本身已是推播層。** 若未來有**次秒級 + 大量同資源觀看**需求才升級，屆時選 **SignalR**（.NET 原生 + 已有 Redis backplane），不是 WS/gRPC。
- **不改「隊長逐筆收接受通知」**（維持 anti-flood）。要走 Discord 通知隊長那條，另案做「批次/節流匯總」。

## 設計

### 查詢分類（per-query 覆寫，不動全域 QueryClient 的 60s 預設）

**核心原則：機制要貼「隊長實際巡檢節奏」。** 觀察現況——**即時團與排程團一樣**，隊長**不會盯著網頁**，開好隊、邀幾個人後就**離開去做別的事（玩遊戲/AFK），間歇回來瞄一下缺哪個職業→再邀**（招募節奏實測 ~5 分/次）。「回來」有兩種，要各自對應：

- **切去別的 App 再切回來** → 觸發 window focus / visibilitychange → **`refetchOnWindowFocus` 蓋掉**。
- **分頁一直開著、視窗在前景、人只是偶爾瞄** → **無任何事件** → focus 重抓不會觸發 → 需要**慢速可見輪詢**補上。

兩者互補：`refetchIntervalInBackground: false` 讓分頁切到背景時**停輪**（去玩遊戲了、由 focus 補），前景才慢輪（人偶爾瞄），不重疊浪費。

**只做候選頁，但該頁要刷兩支查詢。** 決定性差異：React Query **mount 時資料過期會自動重抓** → 任何「點進去」的頁面一到就是新的；且隊長**自己的** mutation 已由 `invalidateTeamQueries` 即時全刷。真正沒人補的是**別人在 Discord DM 的 out-of-band 動作**（接受/婉拒），而隊長**停在候選頁**等這些。故只有候選頁需要 focus 重抓 + 慢輪詢，且要涵蓋該頁**兩支**反映 out-of-band 的查詢：

| 頁面 | hooks（queryKey） | 反映什麼 out-of-band | 策略 |
|---|---|---|---|
| **候選頁**（唯一） | `useCandidates`（["candidates",id]） | 別人**婉拒/退隊** → 位子重開、回到池子 | 共用 `collaborativePolling` |
| 候選頁 | `useRecruitmentGap`（["recruitmentGap",id]） | 別人**接受** → 「還缺 XX ×N」更新 | 共用 `collaborativePolling` |
| 其餘所有頁 | 現況 hooks | — | **不動**（導覽 mount 已重抓 / 自己 mutation 已 invalidate / 走 DM 按鈕） |

> `collaborativePolling` = `staleTime 10s + refetchOnWindowFocus + refetchInterval 30s + refetchIntervalInBackground:false`（抽成共用常數，兩支 hook 各自 spread）。

> 間隔依據是**人的招募節奏（~5 分/次）**：慢輪詢 30–60s 對 5 分迴圈綽綽有餘、每分鐘 ~1 請求可忽略。**比 focus+慢輪詢更快的機制（5s 輪詢 / WS / SignalR）在這節奏下全無意義**——這正是「不上有狀態連線」的依據。

### 分層做法（A+B 一起做；C 才 YAGNI）

- **Level A（focus 重抓）**：候選頁的 `useCandidates` 把 `staleTime` 由 60s 降到 ~10s；`refetchOnWindowFocus` 走預設 `true`。蓋「切去別的 App 再切回來」。近乎零成本。
- **Level B（慢速可見輪詢）**：`useCandidates` 加 `refetchInterval: 30000–60000` + `refetchIntervalInBackground: false`。蓋「分頁開著、人偶爾瞄」（focus 不觸發的情境）。**背景停輪、前景才慢輪**，每分鐘 ~1 請求可忽略。A+B 合起來才完整覆蓋「不盯著、間歇回到候選頁」的動線。
- **Level C（條件輪詢・YAGNI）**：只有當 B 的輪詢負載真成問題才做——加輕量 `GET /api/teams/{id}/version`（回 `confirmedCount/invitedCount/appliedCount` + `updatedAt` 或 team `xmin`），前端只輪 version、變了才 invalidate 候選清單。**先量再說。**

### 觸發後仍 refetch 權威狀態

不論哪層，**輪詢/focus 只是觸發器**，一律重抓伺服器權威狀態覆蓋畫面——不就地拼裝，避免對帳/亂序。這條不變式讓未來若上 SignalR 只是「多一個觸發器」，主幹不動。

## 實作面

- **A + B：純前端**，新增 `hooks/queries/collaborativePolling.ts` 共用常數，`useCandidates` + `useRecruitmentGap` 各自 spread。無後端、無 migration。
- **C：** 需一支輕量後端 version 端點 + 前端只輪 version → 變更時 invalidate 候選清單。**本 plan 只做 A+B，C 列為後續。**

## 驗收

- [ ] **候選頁·切去別的 App 再回來**：**即為最新**（focus 重抓），不用手動重整。
- [ ] **候選頁·分頁開著、人偶爾瞄**：~30–60s 內自己更新（慢速可見輪詢），不需 focus 事件。
- [ ] **候選頁·分頁切到背景**：停止輪詢（`refetchIntervalInBackground: false`），不背景空轉。
- [ ] 其餘頁面（我開的隊 / 我加入的隊 / 審核 / 邀請 / 熱力圖）**未改**：導覽進入即為最新（既有 mount 重抓）、行為不變。
- [ ] 靜態查詢（boss/角色）行為不變。
- [ ] E2E 既有 leader-led 流程不 regression（本改為加法、config-only）。

## 風險 / 待確認

- 只有候選頁在慢輪詢，請求量此規模可忽略；同時開多隊候選頁才需再評估 Level C。
- 只改 `useCandidates` 一支：確認候選頁沒有其他共用該 hook 的地方被連帶影響（應無）。
