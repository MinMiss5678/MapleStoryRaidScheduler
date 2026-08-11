# 即時組隊 × 全面 period-less 重構

狀態：**規劃中（方向已定：全面 period-less 重構）**。2026-08-11 定案——不走延伸/額外車道，直接把 `Period` 從承重實體拔掉，改成**時間軸驅動**；「排程團」與「即時團」用 `Kind` 區分；可用時段從「每週重報」解耦成「掛玩家的常設 + 日期 override」。
關聯：`plans/2026-08-05-leader-led-team-formation.md`（現行 leader-led 狀態機，全數重用）、報名截止/Period 現況討論。

> 這是跨 **3 表 + 全部 scoping 查詢 + 可用時段模型 + 清理策略 + 報名截止/boss 場數重定義** 的大重構。原則：**分階段、每階段可獨立上線且可回退（feature flag / 欄位相容過渡）**，別一次翻。遇到 2~3 個「計畫沒想到」的設計坑 → 停手、回修計畫、再跑。

## 1. 目標

現行是**每週排程**：玩家按 `Period` 報名 + 宣告可用時段 → 隊長開隊（SlotDateTime 綁 period）→ Pull/Push 成軍。痛點：每週重報時段（排班族尤其痛）、Period 硬綁視窗、報名截止硬鎖擋掉遲到者。

目標：**徹底沒有「週期」概念**——
- 團只有 `SlotDateTime`（＋即時團的 TTL），不歸屬任何 period。
- 可用時段是**玩家的常設 profile + 日期 override**，不每週重報。
- 「排程團（提前規劃）」與「即時團（現揪現打）」是**同一張 `TeamSlot` 用 `Kind` 區分**，不是兩套系統。
- **狀態機（邀請/申請/Confirmed/額滿撤銷/轉讓/退團率）全數重用**，一行不改。

## 2. Period 現在承重什麼 → 各自的替代

| 現況（Period 驅動） | period-less 替代 |
|---|---|
| `PlayerRegister.PeriodId` / `TeamSlot.PeriodId` / `Register.PeriodId` | 移除；團只認 `SlotDateTime`，可用時段掛玩家 |
| 報名＝每週把角色+時段放進 period 池 | 報名＝**常設 profile**（角色 + boss 偏好 + 可用時段），非每週 |
| 候選池 `WHERE pr.PeriodId=@x` | **可用時段 overlap 隊伍 SlotDateTime** |
| 開放隊/我開的隊 `WHERE ts.PeriodId=@x` | `WHERE Kind=@k AND SlotDateTime BETWEEN …`（時間窗） |
| `WeeklyPeriodJob` 每週滾動建 period | **TTL/歸檔 job**（過期團歸檔、過期意圖刪除） |
| 報名截止（相對 period 起始回推） | 重定義或移除（見 §7 開放決策） |
| boss 場數上限（per-period 報名 rounds） | **系統不再追蹤每週場數**；隊長開團自訂此團場數規則、玩家自管（順手拿掉 rounds 驗證/候選邏輯） |
| 開隊時間必須 ∈ 開放 period（§3 不變式） | **移除此不變式**；改時間合法性檢查（不得過去、上限 horizon） |

## 3. 新資料模型

### 3.1 `TeamSlot` 加 `Kind`（取代 Period+Source 的隱含區分）
- `Kind ∈ { Scheduled, Instant }`：**建立時由使用者明選**，不靠「SlotDateTime 距現在多久」硬猜。
- `SlotDateTime`：兩者都有（Instant≈now）。
- `ExpiresAt`：只有 Instant 填（TTL）；Scheduled 用 SlotDateTime 當自然到期。
- `RunsMin` / `RunsMax`（場數範圍，選填）：隊長開團公告「這團打幾場」，`1~3 都可以`＝1/3、固定 2 場＝2/2、留空＝隨意。**僅告示、不強制**（系統不追蹤實際場數，見 §7 #2）——讓候選自己對期望、減少「進了才發現場數不合」。玩家端偏好比對留後續。
- `PeriodId`：過渡期改**可空**、最終移除。

| 面向 | Scheduled（下週要打） | Instant（即時揪） |
|---|---|---|
| SlotDateTime | 未來固定、約好的 | ≈now / 接下來短窗 |
| 有空訊號來源 | 常設可用時段（承諾） | `LfgIntent` 的 TTL 意圖 |
| 壽命 | 存到打王時間 | 短命，湊不成/到期就散 |
| 承諾強度 | 高（Confirmed＝約定，爽約算退團率） | 低（現在湊，湊不到算了） |
| 清理錨點 | SlotDateTime 過 → 歸檔 | ExpiresAt 過 → 刪 |

「週」從承重 DB 實體 → 降級成**前端日期分組**（照樣顯示「本週/下週/更遠」，只是對 SlotDateTime 分桶）。

### 3.2 可用時段：常設 + 日期 override（掛玩家，非每週）
- `PlayerAvailabilityStanding`：掛**玩家**的週期性 pattern（weekday + 時段），宣告一次長存。
- `PlayerAvailabilityOverride`：**特定日期**的例外（那天不行／臨時加開）。
- 候選配對：候選 = 「常設（扣掉 override 不行）＋ override 加開」overlap 隊伍 SlotDateTime；**無 periodId 條件**。
- 支援**往未來填任意日期區間**（排班族按月填、規律族設一次常設）：

| 使用者型態 | 怎麼填 |
|---|---|
| 排班族（班表月出、不規律） | 日曆一次填未來整月；換班/加班改那幾天（override） |
| 規律班（每週固定） | 設一次常設週期性，幾乎不用再動 |
| 混合 | 常設當底 + 日期 override |

- **輸入 UX（排班族關鍵體驗）**：存進 DB 的是精確逐日時段，預設只是加速模板——
  - **常用時段預設**（可多個）：如 `平日 19–22`、`早下班 18–22`。
  - **一鍵蓋章**：週/月曆點某天 → 塞入 active 預設；再點清除/循環。
  - **批次套用**：「未來所有週一」/「8/1–8/31 的週一」一鍵鋪滿。
  - **下班浮動（5 或 6 點）**：逐日微調起始時間，或早/晚兩預設切換。
- **資料過時**不靠逼週填來解，靠三層：①接受邀請當下才 Confirmed（入隊即時再承諾）；②退團率信號（已做）；③可選打王前一天輕提醒。

### 3.3 `LfgIntent`（即時找隊意圖，TTL）
| 欄位 | 說明 |
|---|---|
| `DiscordId` / `CharacterId` | 誰、用哪隻角色（快照職業/攻擊同邀請） |
| `BossId` | 想打哪隻（可 null＝任意） |
| `ExpiresAt` | `now()+N 小時`，讀取 `WHERE ExpiresAt > now()` |

- 與可用時段無關（Instant 只認即時意圖）。接受邀請/入隊時清掉對應 intent。

## 4. 配對：全數重用 leader-led 狀態機
- **Scheduled**：隊長開排程團（選 SlotDateTime）→ 對候選（可用時段 overlap）Pull 邀請 / 玩家 Push 申請 → Invited/Applied → Confirmed。額滿自動撤銷、轉讓、退團率信號全沿用。
- **Instant**：玩家發 `LfgIntent` → 即時看板 → 隊長建 `Kind=Instant` 團挑人 / 玩家申請即時開放隊。同一套狀態機。
- **不做自動 matchmaking**（YAGNI；跟當初 pivot 到 leader-led 的理由一致——要人主導篩選）。真有「掛著自動湊」需求再議。

## 5. 隊長候選篩選（Scheduled 車道）
逐日可用時段 → 把「先定死隊伍時間、候選只是那時誰有空」翻成「**先按日期/時段瀏覽、找到到人最多的 slot、再開隊**」。
- **第一層（先做，便宜）**：隊長選具體日期+時段 → 看板只列那天那段有空的人（沿用 overlap 比對，改吃隊長選的任意日期/時段）。
- **第二層（後做，高價值）**：最佳時段建議——圈想要的人/王 → 掃逐日可用時段 → 呈現重疊最多人的日期+時段。
- **取捨（免重複造 UI）**：第二層若做成**可點擊重疊熱力圖**（點任一格 → 該時段名冊），第一層＝點單一格、被吸收。**先出第一層取即時價值，熱力圖成熟後收編第一層**，別長期並存兩套。

## 6. 清理：TTL/歸檔取代 rollover
- Scheduled 團：`SlotDateTime < now` → 歸檔（打完/過期）。
- Instant 團 + `LfgIntent`：`ExpiresAt < now` → 刪。
- 兩支輕量 job 取代 `WeeklyPeriodJob`。

## 7. 決策（2026-08-11 已定案）
1. **報名截止**：**移除硬鎖**（`EnsureRegistrationOpen`）。已 near-vestigial，period-less 下沒有錨點也沒理由擋遲到者。
2. **boss 場數上限**：**系統不再追蹤每週場數**。改由**隊長開團自訂此團的場數規則、玩家自管自己的入場次數**（App 不記不擋）。→ 連帶**拿掉** `Rounds`/`RoundConsumption` 的報名驗證與候選過濾邏輯（Period 少一件事要替代）。註：這是**每週入場上限**；隊長對候選的「最低通關數要求（MinClearCount，裝備 proxy）」是另一回事、**保留**。隊長改用團上的**場數範圍 `RunsMin`/`RunsMax`（選填、僅告示不強制）**公告這團打幾場，「1~3 都可以」＝1/3（見 §3.1）。
3. **「報名」弱化成「編輯我的資料」**：period-less 下 registration 不再是每週動作，而是玩家的**常設 profile（角色 + boss 偏好 + 可用時段）**。保留資料、去掉「每週報名」的動作語意。
4. **horizon 上限**：**設上限**（建議 4–8 週、config 可調）——開 Scheduled 團的 SlotDateTime 與填可用時段最遠到 `now + horizon`，防無界濫填。
5. **可用時段時區**（註，非決策）：常設 pattern 存 TPE local + 團 SlotDateTime timestamptz，比對沿用現有 `SlotDateCalculator` 時區換算慣例。

## 8. 分階段（每階段獨立上線 + 可回退）

> 依賴：Phase 1（Kind/團解耦）→ Phase 2（可用時段解耦）為兩大基石；Phase 4（拔 Period）要等 1+2 完成。

1. **Phase 1 — `TeamSlot` 加 `Kind`/`ExpiresAt`、`PeriodId` 改可空（非破壞）**
   - 加欄位、預設 `Kind=Scheduled`；新增「時間窗」查詢路徑與既有 period 查詢**並存**。
   - `CreateTeam` 加一條不查 period 的路徑（feature flag 後可切）。可回退＝不切 flag。
2. **Phase 2 — 可用時段解耦成常設 + override**
   - 新 `PlayerAvailabilityStanding` + `PlayerAvailabilityOverride`；把現有 per-register 可用時段**回填**成常設（best-effort）。
   - 候選池查詢從 `WHERE pr.PeriodId` 切成「常設/override overlap SlotDateTime」。
   - 報名寫入端改寫常設 profile。**雙寫過渡**（同時寫舊 register 與新常設）確保可回退。
3. **Phase 3 — 即時車道**：`LfgIntent` + 即時看板 + `Kind=Instant` 開團（繞過 period 不變式）+ 重用狀態機 + 輪詢刷新。
4. **Phase 4 — 拔 Period 承重**
   - 開放隊/我開的隊查詢改時間窗；`WeeklyPeriodJob` 換 TTL/歸檔 job；移除報名截止硬鎖；拿掉系統 rounds/場數追蹤（改隊長 per-team 自訂）。
   - 全部改完後 drop `PeriodId` 欄位（或留 nullable vestigial 一版再拆）。
5. **Phase 5 — 體驗強化**：候選日期/時段 filter（§5 第一層）；可用時段輸入 UX（預設+蓋章+批次）；之後重疊熱力圖 + SSE/web push 取代輪詢 + 打王前提醒。

## 9. 重用 vs 新造
- **重用**：leader-led 狀態機全套（邀請/申請/核准/接受/退隊/額滿撤銷/轉讓/退團率）、Outbox DM 通知（5s 輪詢對即時夠、之後上 SSE）、候選 overlap 比對邏輯、`SlotDateCalculator` 時區換算、`uq_tsc_confirmed_overlap`（跨隊同時段防重疊，即時/排程共存的天然保護，需驗）。
- **新造**：`TeamSlot.Kind`/`ExpiresAt`、`PlayerAvailabilityStanding`/`Override`、`LfgIntent`、時間窗查詢、TTL/歸檔 job、可用時段輸入 UI、候選日期/時段 filter。

## 10. 待驗風險
- 回填舊 per-week 可用時段 → 常設的**語意落差**（舊資料是「那週的」，回填成常設可能失真）→ 回填策略要保守 + 提示玩家確認。
- Phase 2 雙寫過渡的一致性與切換時機。
- 即時團與排程團同一人同時段撞（`uq_tsc_confirmed_overlap` 已擋 Confirmed 跨隊重疊 → 天然共存，需 e2e 驗）。
- boss 場數改為玩家自管（系統不擋）＝**刻意的設計取捨**，非風險：符合 leader-led「規則交給人、系統不硬管」精神。若日後想要軟性提示（如「你這王本週好像打很多次了」），另議、非本重構範圍。
