# 業務規則 / 不變量清單 — MapleStory Raid Scheduler

本文件把散落在程式碼與 `architecture.md` 各處的**業務規則**收成單一清單，
用途：① 測試對照（每條規則對應一個驗收案例）② 改動時知道哪些不變量不能破 ③ 快速說明系統行為。

> 這不是 PRD，是「系統必須遵守什麼」的備忘。**機制怎麼實作**見 `architecture.md`；此處只列**規則**。
> 標記：規則旁註來源檔，改行為時同步更新此表。

---

> **period-less（Phase 4c/4d）**：舊的「每週報名 → 系統自動排團 + 範本批次重排 + 補位」子系統（`Period`／`Register`／`TeamSlotAutoAssignService`／`ScheduleService`／`BossTemplate`／報名截止）已整包退場。現行模型是 **leader-led + 即時／排程**，無週期概念。

## 一、開隊（Create Team）

| # | 規則 | 來源 |
|---|---|---|
| CT1 | **不分權**：任何登入者都可開隊；`LeaderDiscordId` 一律用登入身分，不信任 client 傳值 | `TeamSlotController.CreateTeamAsync` / `TeamLeaderService` |
| CT2 | 兩種 `Kind`：**`Scheduled`**（約定 `SlotDateTime`，**不得早於現在**——period-less 後不解析/綁 `Period`，只驗時間合法）\| **`Instant`**（時間＝現在、`ExpiresAt = now + 3h` TTL） | `TeamLeaderService.CreateTeamAsync` |
| CT3 | 開隊帶一組需求列（`TeamSlotRequirement`：`Count` + `MinClearCount` + `Jobs[{Job, MinAttackPower}]`）；需求**只驅動候選過濾 + 前端招募告示**，不強制隊伍職業組成 | `TeamLeaderService` / `TeamSlotRequirement` |
| CT4 | 隊伍容量 = `Boss.RequireMembers`（唯一硬上限；需求列不改容量） | `Boss` / `ConfirmMemberAsync` |
| CT5 | `TeamSlot.Source` 一律 `"leader"`（舊 `auto`/`admin` 來源隨自動排團退場） | `TeamSlotSource` |

## 二、成員狀態（Membership Status）

| # | 規則 | 來源 |
|---|---|---|
| MS1 | `TeamSlotCharacter.Status`：`Invited`（隊長已邀）\| `Applied`（玩家已申請）\| `Confirmed`（已入隊）\| `Rejected`（婉拒/被拒）\| `Left`（退隊） | `TeamSlotMemberStatus` |
| MS2 | 一筆成員定案成 `Confirmed` 前，其位子不佔容量計算以外的名額；容量只數 `Confirmed`（`CountConfirmedAsync`） | `ConfirmMemberAsync` |
| MS3 | `TeamSlotCharacter.SlotDateTime` 是開團時間的**去正規化快照**（開隊/邀請時填），供跨隊時段重疊唯一索引 | migration `000011` |

## 三、候選過濾（Pull 候選池）

| # | 規則 | 來源 |
|---|---|---|
| CD1 | **排程團**候選池 = 角色 `IsSeekingRaid=true`（參戰 opt-in）× 其玩家 `PlayerAvailabilityStanding`（常設可用時段）與開團時間 weekday+time 重疊 | `TeamCandidateQuery.GetPoolAsync` |
| CD2 | `PlayerAvailabilityOverride`（特定日期例外）**蓋寫**常設時段：該日標不可用 → 即使常設可用也排除 | `AvailabilityOverrideService` / 候選過濾 |
| CD3 | 需求過濾：職業符合任一需求列的 `Jobs` + 攻擊力 ≥ 該職業 `MinAttackPower` + **通關數 ≥ `MinClearCount`**（`CharacterBossClear` 同玩家跨角色對該王加總） | `TeamLeaderService.GetCandidatesAsync` |
| CD4 | **即時團**候選來自 `LfgIntent` 看板（現在想打該王的人），**略過時段比對** | `TeamCandidateQuery.GetInstantPoolAsync` |
| CD5 | 狀態感知去重：排除「其玩家已在本隊 active（Confirmed/Invited/Applied）」與「已在該開團時刻別隊 Confirmed（對齊 `uq_tsc_confirmed_overlap`）」者 | `TeamLeaderService.GetCandidatesAsync` |

## 四、組隊狀態機（Pull / Push / 退隊 / 轉讓）

| # | 規則 | 來源 |
|---|---|---|
| SM1 | **Pull**：隊長 `InviteMember`（→`Invited`）→ 玩家 `AcceptInvite`（→`Confirmed`）/ `DeclineInvite`（→`Rejected`）；**只能接受自己的邀請** | `TeamLeaderService.Invite/Accept/Decline` |
| SM2 | **Push**：玩家 `Apply`（用本人角色，→`Applied`）→ 隊長 `Approve`（→`Confirmed`）/ `Reject`（→`Rejected`）；**非隊長不能審核**（→403） | `TeamLeaderService.Apply/Approve/Reject` |
| SM3 | **退隊**：`Confirmed`→`Left`，釋放位子；只能退自己在該隊的成員資格 | `TeamLeaderService.LeaveTeam` |
| SM4 | **隊長轉讓（需同意）**：`ProposeLeaderTransfer` 設 `PendingLeaderDiscordId` → 對方 `RespondLeaderTransfer` accept（搬進 `LeaderDiscordId`）/ decline | `TeamLeaderService.Propose/RespondLeaderTransfer` |
| SM5 | 重複邀請/申請去重：同隊同玩家一筆有效 `Applied`/`Invited`（`uq_tsc_active_membership`）；違反 → 23505 → 409 | migration `000011` |

## 五、入隊定案的併發控制（Confirm）

| # | 規則 | 來源 |
|---|---|---|
| CF1 | `AcceptInvite`〔玩家〕與 `Approve`〔隊長〕共用 `ConfirmMemberAsync` 定案 | `TeamLeaderService.ConfirmMemberAsync` |
| CF2 | **同隊超編**：定案前對 `(classId=1002, teamSlotId)` 取交易級 advisory lock 序列化，鎖內**重讀** `CountConfirmed` vs `Boss.RequireMembers`，達容量 → 拋「隊伍已滿」；再以 `xmin`（`Version`）樂觀鎖改狀態 | `RegistrationLock.AcquireTeamSlotEditLockAsync` / `ConfirmMemberAsync` |
| CF3 | **跨隊分身**：同玩家同 `SlotDateTime` 的 `Confirmed` 唯一（`uq_tsc_confirmed_overlap`）→ 第二筆 23505 → 409（per-team 鎖管不到跨隊，這是唯一原子擋） | migration `000011` |
| CF4 | 定案使隊伍額滿 → 自動撤銷其餘待接受邀請（`RevokePendingInvitesAsync`，仍在同一把鎖內）+ 各發一則通知 | `ConfirmMemberAsync` |
| CF5 | 入隊後清掉該玩家的 `LfgIntent`（已找到隊、不再掛即時看板）；排程 accept 無意圖 → no-op | `ConfirmMemberAsync` |
| CF6 | `lock_timeout`（預設 5 秒）逾時拋 `AdvisoryLockTimeoutException` → 轉「隊伍忙碌中，請稍後重試」 | `RegistrationLock` |

## 六、通知（Notification）

| # | 規則 | 來源 |
|---|---|---|
| N1 | 每日通知：`DailyNotificationService` 每天發當日隊伍到 Discord，同隊玩家聚合成一則；當日無隊伍則不發 | `DailyNotificationService` |
| N2 | **組隊通知**：leader-led 每個狀態改動（邀請/接受/核准/額滿撤銷…）與狀態寫入**同一交易** enqueue 一則 `TeamNotification` outbox 列 → bot 的 handler 撈去發 Discord DM | `TeamLeaderService.NotifyAsync` / `TeamNotificationOutboxHandler` |
| N3 | 系統設定變更（`SystemConfig` 退團率參數）與 `UpdateAsync` 同一交易寫 `ConfigChanged` outbox → bot 喚醒重讀 | `SystemConfigService.UpdateAsync` |
| N4 | 設定變更事件走 **transactional outbox**：commit 才生效、rollback 丟棄，bot 的 `OutboxDispatcher` 讀已提交列 → **跨行程可靠 + crash-safe**（取代原 in-process `AfterCommit`） | `Outbox` / `OutboxDispatcher`；`architecture.md §7 Transactional Outbox` |
| N5 | Discord 通知**只由 bot 端**發（讀已提交狀態）→ 無「發了又 rollback」風險 | `OutboxDispatcher` |
| N6 | Outbox 已處理列（`ProcessedAt` 非 null）超過 **30 天**由 `OutboxRetentionJob` 每 24 小時清一次；未處理列不管多舊都不刪 | `OutboxRetentionJob`；`architecture.md §7 Transactional Outbox` |

## 七、認證與授權（Auth）

| # | 規則 | 來源 |
|---|---|---|
| AU1 | 一般玩家用 JWT（無狀態、Cookie）；管理員用 SessionId（DB `session` 表） | `AuthenticationMiddleware`；`architecture.md §雙軌身分驗證` |
| AU2 | `AllowAnonymous` 端點跳過驗證直接放行 | `AuthenticationMiddleware` |
| AU3 | Session 查無 → **403** + 刪除 session cookie；sessionId 空 → **401** | `AuthenticationMiddleware` |
| AU4 | JWT 過期 → 嘗試 `RefreshToken`；成功則回寫新 token（role 取自新 token）、放行；**續期失敗 → 401**（過期又續不動不得放行） | `AuthenticationMiddleware` |
| AU5 | 端點要求角色但身分不符 → **403** | `AuthenticationMiddleware` |
| AU6 | Discord 身分組 → 系統角色的對應由 `DiscordRoleMapping` 表管理 | `architecture.md §雙軌身分驗證` |
| AU7 | 管理員 session 快取存 **Redis**（跨 pod 共享）→ 撤銷（登出／拔身分組／踢人）一次刪除**即在所有 pod 立即生效**；讀 miss 退回查 DB 自癒、Redis 掛則 fail-open | `RedisSessionCache`；`architecture.md §雙軌身分驗證` |
| AU8 | 管理員 session 有效期 = **`SessionExpiry`（自己的授權政策，30 天）**，過期 → `GetAsync` 回 null → 403；**不靠 Discord token 續期**（與第三方 token TTL 解耦、驗證不依賴 Discord 端點）。活動時**節流 sliding**：剩餘 < 15 天（過半）才延展，避免每讀必寫 | `SessionService` / `SessionPolicy`；`plans/2026-07-28-session-token-decouple.md` |

## 八、請求層防護（Idempotency / Rate limit / IP）

| # | 規則 | 來源 |
|---|---|---|
| G1 | POST/PUT/DELETE **必須帶合法 UUID** 的 `X-Idempotency-Key`（缺或非 UUID → 400） | `IdempotencyMiddleware` |
| G2 | 同一 key **60 秒內**重送 → **409**（擋重複提交，非完整冪等：不重播原回應） | `IdempotencyMiddleware`；`architecture.md §重複提交防護` |
| G3 | 登入後按 `discordId` 限流：**100 次 / 10 秒 / 人**（固定視窗，計數存 **Redis** 故**跨 pod 共用上限**；Redis 掛則 fail-open）；未登入不在此限 | `Program.cs` RateLimiter / `RedisFixedWindowRateLimiter` |
| G4 | 真實 client IP 由前端 proxy 從 `cf-connecting-ip` 設定；後端只信**私有網段**送來的 `X-Forwarded-For`（防偽造） | `Program.cs` ForwardedHeaders；`route.ts` |

## 九、交易邊界（Transaction）

| # | 規則 | 來源 |
|---|---|---|
| T1 | 一個 HTTP 請求 = 一個交易（Unit of Work）；所有 Repository 共用同一 scoped 連線/交易 | `UnitOfWorkMiddleware` / `DbContext` |
| T2 | 寫入請求（POST/PUT/PATCH/DELETE）：status < 400 **Commit**，>= 400 或例外 **Rollback**（例外再往外拋） | `UnitOfWorkMiddleware` |
| T3 | 讀取請求（GET 等）**不開交易** | `UnitOfWorkMiddleware` |
| T4 | 需要「Commit 後才生效」的副作用走 **outbox**：事件與業務資料同一交易寫入 → Commit 才可被派發、Rollback 一起丟棄（無鬼影事件）；投遞由 dispatcher 事後讀已提交列 | `Outbox` / `OutboxDispatcher` |

## 十、角色（Character）

| # | 規則 | 來源 |
|---|---|---|
| C1 | 修改角色**只更新 Name + AttackPower**：`UpdateAsync` 的 SQL 不寫入 Job、Id(code) 是識別鍵不更動 → **職業與代碼不可改，前端繞過也無效（後端強制）** | `CharacterRepository.UpdateAsync` + `CharacterForm`（前端鎖欄位） |
| C2 | 名稱最長 20 字、代碼最長 5 字（前端輸入限制） | `CharacterForm` |

---

## 維護原則

- 改任一行為時，**同步更新對應規則列**（尤其 CF2/CF3、CD3、N2/N4、AU4、G2 這類容易被改壞的不變量）。
- 每條規則理想上對應一個測試——測試是這份清單的**可執行版本**。缺測試的規則列可視為「待補測試」清單。
- 新功能：動手前先在 `plans/` 寫輕量 spec（目標/規則/邊界/驗收），確認後再把穩定規則收進本表。
