# 常設可用時段新鮮度衰退（anti-stale opt-in，防殭屍玩家高估供給）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 非範圍 / 驗收 / 何時才做 / 未解 / 工時。（決策已定，YAGNI gate 未觸發 → 先存 spec、暫不實作）
> 關聯：`Infrastructure/Query/TeamCandidateQuery.cs`（候選池）、`Infrastructure/Services/TeamLeaderService.cs`（`ConfirmMemberAsync` / `GetRecruitmentHeatmapAsync`）、`Domain/Entities/Player.cs`、`Domain/Entities/PlayerAvailabilityStanding.cs`、`Domain/Entities/LfgIntent.cs`（既有 TTL 先例）、`plans/2026-08-26-leader-recruitment-heatmap.md`、`plans/2026-08-25-composition-quota.md`。
> 觸發：熱力圖 / 候選池的供給是**宣告式** opt-in（`IsSeekingRaid` × `PlayerAvailabilityStanding`），棄坑沒關的玩家會被算進供給 → **高估「招得滿」**。目前只有即時路徑（`LfgIntent.ExpiresAt`）有新鮮度，排程路徑（常設時段）沒有。

## 目標

讓候選池 / 熱力圖的常設可用時段供給只反映「**還在玩的人**」，把 stale opt-in 造成的供給高估收掉。用**真參與當隱式心跳** + **讀取時新鮮度過濾**——不靠登入 / JWT / 歷史行為推論，**活躍玩家零打擾**。

## 背景

- 候選池（`TeamCandidateQuery.GetPoolAsync`）＝ `IsSeekingRaid`（角色層開關）× `PlayerAvailabilityStanding`（玩家層常設時段）。**宣告式、無 cold-start**（不需歷史即可用），但代價是 **stale opt-in**：玩家勾了參戰 + 填了時段，之後棄坑卻沒關 → 熱力圖仍把他當供給。
- 即時路徑（`LfgIntent`）已有新鮮度：TTL 到就**靜默過期**、不需回應。排程路徑（常設時段）**無 TTL / 無衰退** → 這 plan 補上。
- **JWT / login 已排除**：那是登入 session 憑證，不是「他想不想打、幾點有空」的活躍訊號。

## 決策

1. **訊號＝真參與**（被 `Confirmed` 進團），**per-player liveness**；不用 login/JWT。
2. **per-player 時鐘、不 per-slot 衰退**。「某 slot 最近沒成團」≠「玩家該時段沒空」——多半只是沒團來揪。per-slot 衰退會**抹掉冷門時段的真供給**（正是熱力圖要幫忙找的），形成「冷 → 沒 join → 更冷」的惡性回饋。
3. **心跳＝使用者在組隊生命週期的任何實質動作**，bump **動作者**的時鐘——**含隊長開團**（`CreateTeamAsync`，比被邀更強的活躍訊號）、邀請/核准/轉讓、申請、接受/婉拒、入隊定案、編輯時段/重開 seeking。判準：`dormant = 連續 N 天對組隊系統零動作`。**不是「滿團」**（隊事件、非 per-player）、**不是 login/JWT**（憑證非參與）。
   - ⚠️ 特別注意：隊長**帶自己角色開團**是在 `CreateTeamAsync` 裡直接寫 `Confirmed`（**不走** `ConfirmMemberAsync`），只掛 `ConfirmMemberAsync` 會漏掉活躍隊長 → 必須在 `CreateTeamAsync` 也 bump。
   - **為何採廣義（含申請/邀請/婉拒等 intent 動作）而非只認 `Confirmed`**：freshness 保護的是 **presence（人還在、時段還準）**、不是 **attendance（會不會出席某團）**。intent 動作＝當下人就在系統裡做組隊事，是 presence 的直接證據。只認 `Confirmed` 會**誤砍最投入的人**——天天申請卻沒被核准者、開團揪人不帶角色的隊長。界線該切在「**deliberate 組隊動作 vs 被動 login/JWT**」，不是「intent vs outcome」。（freshness 只 gate staleness、不決定「誰是好候選」。）
   - 🔴 **層 footgun（別「優化」成 middleware）**：bump 必須放**共用 Application service（`TeamLeaderService`）**，**不可**搬到 HTTP middleware 想「一次攔完消掉散落」——**bot 的接受/婉拒直接呼叫 `ITeamLeaderService.Accept/DeclineInviteAsync`、繞過 HTTP**（`Presentation/Program.cs:99,112`，無 HttpClient 回打 API），middleware 會**漏掉所有 bot 來源活動**。散落在 service 各方法是**正確的層**（web＋bot 共同匯流處），同「bot 繞過 DTO → 共用邏輯放共用 service/domain」的層原則。「怕漏某方法」由驗收的列舉測試守，不是靠換層。
   - **bump 節流（避免寫放大）**：只有 `LastAffirmedAt` 已舊於節流窗（初值 1 天）才真寫，否則同一玩家一天內多次動作會狂 UPDATE `Player`。mirror 既有 Session sliding（AU8：剩 <15 天才延展、避免每讀必寫）。
4. **衰退＝讀取時過濾、非破壞性 mutation**：pool query 加 `LastAffirmedAt > now − 門檻`；refresh ＝ bump `LastAffirmedAt`。過期是「讀不到」，一 bump 立即復原。
5. **門檻由 admin 設定**：`SystemConfig.AvailabilityFreshnessDays`，**預設 30 天**（對齊既有 admin Session 30 天 sliding、`OutboxRetentionJob` 30 天保留）。放既有 `SystemConfig`（同退團率參數）→ admin 可即時調、免改 code、免猜「正確天數」。讀取端每次讀最新值、**免 outbox**（同退團率的 N3 契約）。app 層驗合理範圍（如 ≥ 1，避免 0/負值把全員清空）。
6. **兩種退出分清**：
   - **inferred（ignore／沒動作）** → 新鮮度過濾自動排除，一 bump 即回。**這是載重路徑**：真殭屍只會靜默消失、不會按任何鈕，靠這條收掉。
   - **explicit（decline／「移除我」）** → 立即 opt-out（`IsSeekingRaid=false`），需明確重開。
   - 兩者**都不刪 `PlayerAvailabilityStanding` 資料** → 可逆、回來便宜。
7. **通知 exception-based**：只對「**快過期且未參與**」發**一則**可一鍵留任的 Discord DM（走既有 outbox + DM 按鈕），**非全員定期催**。活躍玩家靠參與自動保鮮、收不到這封。
8. **UI 分工**：布林（留任 / 重開 / seeking on-off）→ Discord 按鈕；**編輯週表 → 網頁**（富 UI）。日常保鮮完全不用開網頁。
9. **誤判可接受**：**真的還在玩、也還有空**（reality-active）、但 30 天零組隊動作、又沒回 nudge 的人，會被過濾（server 分不出「還在只是被動」vs「已棄坑」，兩者看起來一樣）。**成本極小且可逆**：過濾是讀取時的軟狀態、**不刪時段資料** → 他下次做任何動作 / 點「回來」即 bump 復原、時段原封不動＝**一次點擊**。且**觸發面窄**：近期有任何組隊動作者根本收不到 nudge。**不對稱取捨**：不追求「完美分辨誰真的走了」（不可能），改讓誤判**便宜可逆**；小成本 vs 真收益（供給不含殭屍）→ 接受。
10. **一致性**：mirror `LfgIntent` 的靜默 TTL；常設時段因**資料花力氣設定**，才多加一則 courtesy nudge（即時意圖是拋棄式、不加）。

## 範圍

分兩階段，最小版可獨立出貨。

### 階段一（最小版）：讀取過濾 + 心跳 bump
- **Schema**：`Player.LastAffirmedAt timestamptz NULL`（新 migration，additive、可回退）。
- **Admin 設定**：`SystemConfig` 加 `AvailabilityFreshnessDays int NOT NULL DEFAULT 30`（同 migration）。`SystemConfig` 已是 admin POST 綁定型別、有 `SystemConfigController`(GET/POST) + 前端 `web/app/admin/config/page.tsx` → 只需**加一欄位 + 表單一個輸入框**；app 層驗 `≥ 1`。
- **讀取過濾**：`TeamCandidateQuery.GetPoolAsync` 的 `WHERE c."IsSeekingRaid"` 加 `AND (p."LastAffirmedAt" IS NULL OR p."LastAffirmedAt" > now() - @freshness)`，`@freshness` 由 `ISystemConfigService.GetAsync().AvailabilityFreshnessDays` 帶入（每次讀最新、免 outbox）。熱力圖共用同 pool → 自動涵蓋。
- **bump 點**（`BumpLastAffirmed(discordId)` helper，更新 `Player.LastAffirmedAt = now`，**節流：僅當 `now − LastAffirmedAt > 1 天` 才寫**；掛在各生命週期寫入方法、bump 動作者；**放 `TeamLeaderService`／共用 Application 層、不搬 middleware**，見決策 3 層 footgun）：
  - `CreateTeamAsync`（隊長開團——**易漏，帶角色時走此路徑非 `ConfirmMemberAsync`**）；
  - `ConfirmMemberAsync`（被定案者）、`ApplyAsync`（申請者）、`AcceptInvite`/`DeclineInvite`（回應邀請者）、`InviteMember`/`Approve`/`RespondLeaderTransfer`（隊長組織動作）；
  - 玩家編輯常設時段時（availability service）；
  - 玩家 seeking 重開時。

### 階段二（增強）：courtesy nudge + 一鍵留任
- **背景 job**（套 `OutboxRetentionJob` / `LfgIntentCleanupJob` pattern）：撈「`LastAffirmedAt` 逼近門檻、seeking 中、且未提醒」→ enqueue freshness DM（含「留任」/「移除我」按鈕）。需一個「已提醒」標記避免重送。
- **bot handler**：「留任」→ bump `LastAffirmedAt`；「移除我」→ `IsSeekingRaid=false`（不刪時段）。
- （可選）過期後補一則「點一下回來」info DM，幫出遠門回來的人一鍵歸隊。

## 非範圍（YAGNI）
- **per-slot 衰退**（見決策 2）。
- **全員定期強制重新確認**（nag）——背叛「活躍玩家免打擾」初衷。
- **login / JWT / 歷史行為推論活躍度**——用真參與，不推論。
- **DM 編輯週表**——布林才進 Discord，富編輯留網頁。
- **出席預測 / ML**——熱力圖是**宣告供給**非**預測出席**。

## 驗收
- [ ] 玩家 `LastAffirmedAt` 逾門檻 → 不再出現在候選池 / 熱力圖供給；bump（join／留任／編輯）後**立即**回。
- [ ] `ConfirmMemberAsync` 定案 → 該玩家 `LastAffirmedAt` 更新（整合測）。
- [ ] **隊長開團（`CreateTeamAsync`，含不帶自己角色的純揪人）→ 隊長 `LastAffirmedAt` 更新**——防「活躍隊長被誤判 dormant」的易漏路徑。
- [ ] **列舉測試**：每個生命週期動作（開團／申請／接受／婉拒／邀請／核准／轉讓／定案／編輯時段／重開 seeking）**各有一斷言會 bump** `LastAffirmedAt` ——擋「新增動作忘了 bump」（已漏過 `CreateTeamAsync` 一次）。含 **bot 路徑**（accept/decline 經 `TeamLeaderService`）也 bump。
- [ ] **節流**：同玩家一天內多次動作只寫一次 `Player.LastAffirmedAt`（節流窗內不重寫）。
- [ ] decline／移除我 → `IsSeekingRaid=false`、退出池；但 `PlayerAvailabilityStanding` 資料仍在（**未刪**）。
- [ ] `LastAffirmedAt IS NULL` → 視為新鮮、不被過濾（backfill 保守，不誤砍既有玩家）。
- [ ]（階段二）nudge 只對「快過期且未參與」發一則、不重送；近期有 join 者不發。
- [ ] 既有候選過濾 / 熱力圖 / leader-led E2E 不回歸。

## 何時才做（YAGNI gate）
- **現在不做**。觸發條件＝真實社群出現「熱力圖顯示招得滿、實際招不到」的 stale 供給，或使用者回報殭屍玩家干擾。無此訊號前只留本 plan。
- 先做**階段一**（讀取過濾 + bump，無通知）；**階段二**（nudge/DM 按鈕）視噪音與回饋再上。

## 未解（需真實資料，現在不猜）
- ✅ 新鮮度門檻＝`SystemConfig.AvailabilityFreshnessDays`、**預設 30 天、admin 可即時調**（見決策 5）→ 不必事前猜「正確天數」，上線後依社群節奏（週經營型公會 vs 每日活躍）自己調。nudge 提前天數、寬限期長度可先用「門檻前 3 天發 nudge、發後到門檻為寬限」當初值（階段二才需要）。
- backfill：傾向 `LastAffirmedAt = NULL`（＝永久新鮮、不過濾）避免上線瞬間誤砍既有玩家；或給 `now`（下次起算）。決策 4 的讀取過濾已含 `IS NULL` 分支支援前者。

## 工時估
- 階段一：兩個欄位（`Player.LastAffirmedAt` + `SystemConfig.AvailabilityFreshnessDays` + admin 表單一欄）+ 讀取過濾 + bump helper 掛各生命週期方法 + 整合測 ≈ 1 天。
- 階段二：job + DM 按鈕 + handler + 已提醒標記 ≈ 1～2 天。
