# 業務規則 / 不變量清單 — MapleStory Raid Scheduler

本文件把散落在程式碼與 `architecture.md` 各處的**業務規則**收成單一清單，
用途：① 測試對照（每條規則對應一個驗收案例）② 改動時知道哪些不變量不能破 ③ 快速說明系統行為。

> 這不是 PRD，是「系統必須遵守什麼」的備忘。**機制怎麼實作**見 `architecture.md`；此處只列**規則**。
> 標記：規則旁註來源檔，改行為時同步更新此表。

---

## 一、週期（Period）與日期

| # | 規則 | 來源 |
|---|---|---|
| P1 | 官方重製日 = **週二**（單一事實來源）；改期只改 `SlotDateCalculator.ResetDay`，所有排序/日期/排程跟著推導 | `SlotDateCalculator` |
| P2 | 重製時間 = `period.StartDate` 當日時間 = 週二 **00:00 UTC = 08:00 TPE** | `SlotDateCalculator` |
| P3 | 一個週期 = 重製日 00:00 UTC 起、+6 天 23:59:59 止（整週） | `WeeklyPeriodJob` |
| P4 | `WeeklyPeriodJob` 每個重製日建立下一週期（若該 `StartDate` 尚不存在才建）；每 7 天執行一次 | `WeeklyPeriodJob` |
| P5 | **新週期建立 → 重置 `IsDeadlineNotified`**，讓截止通知能為新週期重發 | `WeeklyPeriodJob` |
| P6 | 週期內天別排序以重製日為首：`[2,3,4,5,6,0,1]` | `SlotDateCalculator.CycleWeekdayOrder` |
| P7 | 落在重製日當天、但時間早於重製時間（08:00）的可用時段 → 視為**上一輪殘留**，排序權重 +7（排到最後） | `SlotDateCalculator.GetBestAvailability` |

## 二、報名（Registration）

| # | 規則 | 來源 |
|---|---|---|
| R1 | 報名（`CreateAsync`）= 建立 register + 可用時段 + 角色報名，接著**立即** `AutoAssignAsync` | `RegisterService.CreateAsync` |
| R2 | **一人一期只能報名一次**：已存在 `(discordId, periodId)` 的報名 → 拋「您已完成本期報名，請勿重複提交」 | `RegisterService.CreateAsync`（`ExistAsync`） |
| R3 | **報名 / 更新僅在報名截止前允許**：活躍週期的 deadline 已過 → 拋「目前已超過報名截止時間」 | `RegisterService.EnsureRegistrationOpen` |
| R4 | **防 IDOR**：更新不信任前端傳的 `registerId`，改由 `(discordId, periodId)` 查出呼叫者自己的 id | `RegisterService.UpdateAsync` |
| R5 | 更新可用時段 = 先刪該報名的所有 availability 再依新資料重建 | `RegisterService.UpdateAsync` |

## 三、隊伍來源與成員標記（模型基礎，先讀這節）

> ⚠️ **舊 `IsTemporary` 布林已廢除**，改由 `TeamSlot.Source` 表示來源。下面的規則都建立在這兩個欄位上。

| # | 規則 | 來源 |
|---|---|---|
| S1 | `TeamSlot.Source`（字串）= **`"auto"`**（玩家報名時系統自動建）\| **`"admin"`**（管理員手動開團 / 批次重排）。**取代舊 `IsTemporary`** | `TeamSlotConstants` / `TeamSlot` |
| S2 | `Source` 驅動三件事：**空隊自動清除（僅 auto）**、**合併資格（僅 auto）**、**重排保留（admin 整隊保留）** | `TeamSlotConstants` 註解 |
| S3 | 成員層級 `TeamSlotCharacter.IsManual`：**玩家補位 / 管理員微調 = true**；**重排自動填入 = false**。由來源端顯式決定，後端不強制 | `TeamSlotService` |

## 四、自動分配（Auto-assign）

| # | 規則 | 來源 |
|---|---|---|
| A1 | 玩家報名即時觸發：每個角色媒合到現有隊空位；無匹配**且可用時段非空**才建新隊（`Source="auto"`）。**可用時段為空 → 跳過建隊，報名本身仍成功** | `TeamSlotAutoAssignService` |
| A2 | 自動分配時**已被分配的角色會跳過**（`IsAlreadyAssigned`），同一角色不重複分配 | `TeamSlotAutoAssignService` |
| A3 | **併發控制**：同一 period 的「讀隊→開新隊」以交易級 advisory lock（`pg_advisory_xact_lock`）序列化 → 兩人同時報名同一 period 不會重複開隊 | `RegistrationLock`；`architecture.md §併發控制` |
| A4 | 不同 period 的鎖不互斥、可並行；鎖在 DB → **多 pod 安全** | `RegistrationLock` |

## 五、合併（Merge）與批次重排（Re-schedule）

| # | 規則 | 來源 |
|---|---|---|
| M1 | 報名後嘗試合併：只在**未滿的 auto 隊**之間（`GetIncompleteTeamsAsync`）；< 2 隊則不合併 | `TeamSlotMergeService` |
| M2 | **手動成員（IsManual）可參與合併**——合併只把兩隊併成一隊、不拆散，認識的人仍同隊；但範本比對嚴格，湊不齊會取消（TryMatchTemplate 回 null） | `TeamSlotMergeService` |
| M3 | 合併避免同一玩家在同隊重複（同 `DiscordId` 出現 > 1 則跳過） | `TeamSlotMergeService` |
| M4 | **批次重排的「保留隊」= `Source="admin"` 的隊 或 含任一 `IsManual` 成員的 auto 隊** → 整隊保留、只自動補滿空位；其餘 auto 隊可被重組 | `ScheduleService` |
| M5 | 重排自動補入空位者標 `IsManual=false`（之後重排仍可調整） | `ScheduleService` |
| M6 | 批次重排產生 `Source="admin"` 的隊，以**負 Id 代表未存檔的預覽**，存檔時走 CREATE。**無 IsTemporary / confirm 旗標** | `ScheduleService` / `TeamSlotService.UpdateAsync` |

## 六、隊伍編輯授權

| # | 規則 | 來源 |
|---|---|---|
| E1 | 非管理員只能改**自己的成員**（`member.DiscordId == currentDiscordId`）；跨成員 / 隊伍層級操作需管理員 | `TeamSlotService.UpdateAsync` |

## 七、通知（Notification）

| # | 規則 | 來源 |
|---|---|---|
| N1 | 每日通知：每天 **09:00**（本地）發當日隊伍到 Discord，同隊玩家聚合成一則；當日無隊伍則不發 | `DailyNotificationService` |
| N2 | 截止通知：截止時間已過 **且** `!IsDeadlineNotified` 時發送；設定變更時被喚醒重算 | `RegistrationDeadlineJob` |
| N3 | 截止時間可設定（`SystemConfig.DeadlineDayOfWeek/DeadlineTime`）；**預設 = 重製日前一天（週一）** | `SystemConfigService` |
| N4 | **改變截止日/時 → 重置 `IsDeadlineNotified`**（讓新截止能重發通知） | `SystemConfigService.UpdateAsync` |
| N5 | 設定變更事件走 **transactional outbox**：與 `UpdateAsync` 同一交易寫 outbox 列（commit 才生效、rollback 丟棄），bot 的 `OutboxDispatcher` 讀已提交列喚醒 job → **跨行程可靠 + crash-safe**（取代原 in-process `AfterCommit`：commit 後 crash 會掉、且跨不了行程） | `Outbox` / `OutboxDispatcher`；`architecture.md §7 Transactional Outbox` |
| N6 | Discord 通知**只由背景 job**發（讀已提交狀態），無請求內 inline 發送 → 無「發了又 rollback」風險 | `DailyNotificationService` / `RegistrationDeadlineJob` |

## 八、認證與授權（Auth）

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

## 九、請求層防護（Idempotency / Rate limit / IP）

| # | 規則 | 來源 |
|---|---|---|
| G1 | POST/PUT/DELETE **必須帶合法 UUID** 的 `X-Idempotency-Key`（缺或非 UUID → 400） | `IdempotencyMiddleware` |
| G2 | 同一 key **60 秒內**重送 → **409**（擋重複提交，非完整冪等：不重播原回應） | `IdempotencyMiddleware`；`architecture.md §重複提交防護` |
| G3 | 登入後按 `discordId` 限流：**100 次 / 10 秒 / 人**（固定視窗，計數存 **Redis** 故**跨 pod 共用上限**；Redis 掛則 fail-open）；未登入不在此限 | `Program.cs` RateLimiter / `RedisFixedWindowRateLimiter` |
| G4 | 真實 client IP 由前端 proxy 從 `cf-connecting-ip` 設定；後端只信**私有網段**送來的 `X-Forwarded-For`（防偽造） | `Program.cs` ForwardedHeaders；`route.ts` |

## 十、交易邊界（Transaction）

| # | 規則 | 來源 |
|---|---|---|
| T1 | 一個 HTTP 請求 = 一個交易（Unit of Work）；所有 Repository 共用同一 scoped 連線/交易 | `UnitOfWorkMiddleware` / `DbContext` |
| T2 | 寫入請求（POST/PUT/PATCH/DELETE）：status < 400 **Commit**，>= 400 或例外 **Rollback**（例外再往外拋） | `UnitOfWorkMiddleware` |
| T3 | 讀取請求（GET 等）**不開交易** | `UnitOfWorkMiddleware` |
| T4 | 需要「Commit 後才生效」的副作用走 **outbox**：事件與業務資料同一交易寫入 → Commit 才可被派發、Rollback 一起丟棄（無鬼影事件）；投遞由 dispatcher 事後讀已提交列 | `Outbox` / `OutboxDispatcher` |

## 十一、角色（Character）

| # | 規則 | 來源 |
|---|---|---|
| C1 | 修改角色**只更新 Name + AttackPower**：`UpdateAsync` 的 SQL 不寫入 Job、Id(code) 是識別鍵不更動 → **職業與代碼不可改，前端繞過也無效（後端強制）** | `CharacterRepository.UpdateAsync` + `CharacterForm`（前端鎖欄位） |
| C2 | 名稱最長 20 字、代碼最長 5 字（前端輸入限制） | `CharacterForm` |

---

## 維護原則

- 改任一行為時，**同步更新對應規則列**（尤其 N4/N5、A2、AU4、G2 這類容易被改壞的不變量）。
- 每條規則理想上對應一個測試——測試是這份清單的**可執行版本**。缺測試的規則列可視為「待補測試」清單。
- 新功能：動手前先在 `plans/` 寫輕量 spec（目標/規則/邊界/驗收），確認後再把穩定規則收進本表。
