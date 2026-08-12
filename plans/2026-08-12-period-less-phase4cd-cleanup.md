# Phase 4c/4d — 拔 Period 承重 + 退場舊自動排團/報名子系統

> 這份是 period-less 重構收尾（Phase 4c/4d）的**拆除規格**，給接手的新 session 照著執行。
> 承接 `plans/2026-08-11-realtime-team-formation.md`（總計畫 §8 Phase 4）。決策於 2026-08-12 拍板。

## Context（為什麼做這個）

專案已從「每週報名 → 自動排團」**pivot 成 leader-led + period-less 即時/排程組隊**。period-less 重構已完成 Phase 1a–4b（PR #73–#80）：可用時段常設化、profile 取代報名、Instant 車道、led/open 查詢改時間窗、LfgIntent TTL 清理。

**現況殘留**：舊的「自動排團世界」（自動排團 + 補位 + 排團結果）與 `Period` 承重牆仍在，只是多半已無 leader-led 消費者。使用者 2026-08-12 拍板 **方案 A：整包退場**——自動排團初稿、補位、排團結果、報名截止**全砍**；`Period` 拔成純時間軸。理由：leader-led 的「候選頁按職業邀請」已覆蓋核心，舊機器只是重複+維護成本。

**目標**：徹底沒有 `Period` 與舊自動排團/報名子系統；scheduled 團也不綁 period（只認 SlotDateTime + horizon）。

---

## 決策（已定，2026-08-12）
- 自動排團 + admin 排團/重排 → **退場**（連初稿一起；真要「一鍵填滿」日後用 leader-led 重寫小按鈕）。
- 補位 (fill) → **退場**（空缺由 leader-led 邀請/申請補）。
- 排團結果頁 (/scheduleResult) → **退場**。
- 報名截止（deadline config + 首頁倒數 + DeadlineJob + EnsureRegistrationOpen）→ **移除**。
- 舊 register 後端（profile 已取代）→ **退場**。

---

## ⚠️ 改寫點（不是刪除！先做，否則砍下去會爆）

這些是**保留功能**對舊子系統的殘留依賴，**必須改寫、不能直接刪**：

1. **`web/app/teams/new/page.tsx`（leader-led 開隊，保留）** 目前 import：
   - `usePeriod`（`web/hooks/queries/usePeriod`）→ 用來限制打王時段 datetime 的 min/max 在本期內。**period-less 後移除此限制**（改成 `now ~ now+horizon`，或不限）。
   - `useJobMap`（`web/hooks/queries/useScheduleData`）→ 舊「職業分類一鍵勾滿整組」。**定案：JobCategory 整包退場**（開放項 #1）→ 移除 `useJobMap`/`useScheduleData`；`/teams/new` 職業改用 `web/constants/jobs.ts` 的 `JOBS` 複選 + **localStorage 存「上次/命名的需求組合」一鍵套用**（取代分類快選；personal、零後端）。
2. **首頁 `web/app/page.tsx`** 用 `registerService.getDeadline()` 顯示截止倒數 → 移除該區塊（連同 `config.deadlineDayOfWeek/deadlineTime` 顯示）。
3. **`CreateTeamAsync`（`Infrastructure/Services/TeamLeaderService.cs`，約行 80）** scheduled 團仍 `_periodQuery.GetPeriodIdByDateAsync` 解析 PeriodId（唯一 leader-led 的 period 用途）→ **4d 移除**：scheduled 團不再解析/寫 PeriodId，改驗 `SlotDateTime` 合法（不得過去、≤ now+horizon）。移除後 `IPeriodQuery` 注入可拔。
4. **通關數輸入路徑**：`CharacterBossClear`（通關數）**保留**（leader-led 候選 `MinClearCount` 過濾用，`TeamCandidateQuery` 讀它）。但其**輸入**原在舊 register/character 流程 → 確認退場後仍有辦法設（`CharacterBossClearRepository.CreateAsync` 的呼叫端）。若只剩舊 register 在寫 → 需搬到 profile/角色管理，否則 MinClearCount 變成無法設定的死條件。

---

## 4c — 退場舊子系統（大量刪除）

### 後端刪除（service/controller/repo/query/job/entity/DbModel）
- **Service**：`RegisterService`、`RegisterQueryService`、`ScheduleService`、`TeamSlotService`、`TeamSlotAutoAssignService`、`TeamSlotMergeService`、`PeriodService`（+各自 interface）。
- **Controller**：`RegisterController`、`ScheduleController`、`PeriodController`。（`Presentation.WebApi/Controller/`）
- **Repo/Query**：`PlayerRegisterRepository`+`IPlayerRegisterQuery`/`PlayerRegisterQuery`、`CharacterRegisterRepository`、`PlayerAvailabilityRepository`（舊掛 register 的）、`PeriodRepository`（4d）、`PeriodQuery`（4d，移除 CreateTeam 依賴後）。
- **Job**：`RegistrationDeadlineJob`（+ `Presentation/Program.cs` 註冊）；`WeeklyPeriodJob`（4d，+ `Presentation.WebApi/Program.cs` 註冊）。
- **範本 + 職業分類（定案退場，開放項 #1）**：`BossTemplate*`/`BossTemplateRequirement*`、`JobCategory*`(entity/DbModel/`JobCategoryRepository`/`JobCategoryController`)、`BossService` 的**範本方法**、`BossController` 的 `/Templates` 端點、`jobCategoryService.getJobMap`。**保留** `BossService`/`BossController` 的 **boss CRUD**（/teams/new、/teams/instant 選王用）。
- **DTO/Entity**：`RegisterCreateCommand`/`RegisterUpdateCommand`/`RegisterDto`、`Register`/`PlayerCharacterRegister`/`PlayerRegisterSchedule` 聚合、對應 DbModel。
- **DI**：`Presentation.WebApi/Extensions/ServiceCollectionExtensions.cs` 拔掉上述註冊；`Presentation/Program.cs` 拔 Period 相關與 DeadlineJob。
- **保留（勿砍）**：`TeamSlotRepository`、`TeamSlotCharacterRepository`、`TeamSlotRequirementRepository`、`RegistrationLock`（ConfirmMember 的 advisory lock）、`CharacterBossClearRepository`、`BossRepository`、`CharacterQuery/Repository`、`TeamCandidateQuery`、`TeamMembershipQuery`、`ProfileService`、`LfgService`、`AvailabilityOverrideService`、`PlayerAvailabilityStanding/Override` repos、Outbox 全套。

### 前端刪除
- **頁**：`web/app/schedule/`（補位，含 `components/PlayerRaidTeamCard.tsx`）、`web/app/scheduleResult/`、`web/app/admin/schedule/`、`web/app/admin/templates/`。
- **hook/service/util**：`useRegisterForm`、`useBossAssignment`、`useTimeSelection`、`useRegisterData`、`useScheduleData`（含 `useJobMap`—見改寫點）、`registerService.ts`、`scheduleService.ts`、舊 `TimePicker`/`CharacterBossList` 元件、`getMissingSlots`/jobMap util。
- **nav**（`web/components/layout/NavBar.tsx`）：移除「補位」`/schedule`、「排團」`/admin/schedule`、「排團結果」`/scheduleResult`。**保留**「我的資料」`/register`（=profile）、「即時揪團」、「尋隊」、「隊伍列表」、「帶隊」。
- **whitelist**（`web/constants/apiWhitelist.ts`）：移除 `register`、`schedule`、`period`（保留 `profile`/`lfgintent`/`availabilityoverride`/`teamslot`/`character`/`boss`/`me`…）。

### e2e 刪除 + seed
- 刪 `web/e2e/{fill,schedule,admin-conflict,admin-rebuild}.spec.ts`。**保留** `auth`、`smoke`（只驗首頁+登入，不動）、`register`(=profile)、`leader-led*`、`transfer`、`auto-revoke`、`availability-override`、`instant-lfg`。
- `db/seed-e2e.sql`：移除只服務舊 spec 的 fixture（auto 隊/E2E王2 補位隊/E2E王3 重排/E2E王4 衝突、BossTemplate/JobCategory seed）。**保留** leader-led/profile/instant 用的（角色、Boss、standing 鏡射、P-Cand/P-Full/P-Lfg…）。⚠逐一比對 spec 依賴再刪。

---

## 4d — drop Period 承重 + 舊表

> 前置：4c 完成 + 改寫點 3（CreateTeam 去 period）+ 改寫點 4（通關數輸入）都處理好，確認**無任何程式再讀寫這些欄/表**。

- **移除 CreateTeam 的 period 解析**（改寫點 3）→ 拔 `IPeriodQuery`、`WeeklyPeriodJob`、`Period` entity/repo/query/service。
- **Migration（新編號，可回退）drop**：
  - 表：`PlayerRegister`、`CharacterRegister`、`PlayerAvailability`(舊)、`Period`、`BossTemplate`、`BossTemplateRequirement`、`JobCategory`。
  - 欄：`TeamSlot.PeriodId`（+ 相關 index/FK）；`SystemConfig` 的 `DeadlineDayOfWeek`/`DeadlineTime`/`IsDeadlineNotified`。
  - 連帶 Domain `TeamSlot.PeriodId`、`TeamSlotDbModel.PeriodId`、`SystemConfig` deadline 欄位、`GetDeadlineForPeriod`。
- **保留表**：Boss、Character、CharacterBossClear、DiscordRoleMapping、LfgIntent、OutboxMessage、Player、PlayerAvailabilityStanding、PlayerAvailabilityOverride、Session、SystemConfig(精簡)、TeamSlot(-PeriodId)、TeamSlotCharacter、TeamSlotRequirement、TeamSlotRequirementJob。

---

## 開放項

1. **JobCategory → 定案「整包退場」（2026-08-12）**。原本 `/teams/new` 用它做「一鍵勾滿整組」，但那只是便利、又要養全域表且無後台。改成：
   - `/teams/new` 職業用 `JOBS` 常數**複選** + **localStorage 存隊長「上次/命名的需求組合」一鍵套用**（personal、零後端，隱式取代「個人分類」；需跨裝置同步再議，現 YAGNI）。
   - 存什麼：整組需求列（jobs + 攻擊下限 + 數量 + minClear…）；key 綁 discordId 或瀏覽器即可。
   - 砍除清單見 4c「範本+職業分類」；保留 BossService/BossController 的 boss CRUD。
2. **通關數輸入 → 定案「A：補起來」（2026-08-12）**。查證：`CharacterBossClear` 目前**無 app 寫入路徑**（`ICharacterBossClearRepository` 有註冊但沒人注入；連舊 register 都沒寫）→ 只被候選查詢讀、真實玩家皆 0 → MinClearCount>0 會濾光所有人（半殘功能）。定案補起來讓它真的能用：
   - **保留** `CharacterBossClear` 表 + `CharacterBossClearRepository`（**加 upsert**：`INSERT … ON CONFLICT ("CharacterId","BossId") DO UPDATE`，`uq_charbossclear` unique 已存在）+ MinClearCount 需求/候選過濾（/teams/new 設、TeamCandidateQuery 濾）。
   - **新增玩家自填輸入**：per 角色 per 王 的通關數，放**角色管理 `/character`**（最貼 per-character）；新 endpoint（如 `POST /api/Character/{id}/BossClears`）→ 寫 CharacterBossClear。前端 whitelist 已有 `character`。
   - 屬 **4c 的一部分**（補上舊 register 退場後缺的輸入路徑）。

---

## 分片與順序（建議）

> 4c **不是純刪除**——有兩個小「補回」（開放項定案）：`/teams/new` 的 localStorage 需求組合預設、`/character` 的通關數自填。

1. **4c-fe**：刪前端舊頁/hook/service/nav/whitelist；改寫 `/teams/new`（去 usePeriod/useJobMap → `JOBS` 複選 + **localStorage 需求組合預設**）；首頁去截止；`/character` 加**通關數自填** UI；刪舊 e2e/seed。跑 e2e 綠。
2. **4c-be**：刪後端舊 service/controller/repo/job（register/schedule/auto-assign/merge/period-service/template/jobcategory）+ DI + DeadlineJob + EnsureRegistrationOpen；**加** `CharacterBossClearRepository` upsert + `POST /api/Character/{id}/BossClears` 寫入端點；修編譯 + 單元/整合測。
3. **4d**：CreateTeam 去 period → 拔 Period 全套 + WeeklyPeriodJob → drop migration（欄/表，含 JobCategory/BossTemplate*）。
每片獨立 build+測+（前端片 e2e）綠再進下一片，一片一 PR。

## 驗證
- 每片：`dotnet build`、`dotnet test Test`、`dotnet test Test.Integration`（改 query/repo 必跑，Testcontainers 真 DB）、前端 `vitest` + 容器化 e2e 全綠（`compose.e2e.yaml --profile ci`）。
- 重點回歸：leader-led 開隊(scheduled 去 period 後仍能開)、候選(常設+override)、Instant 車道、profile 存取、MinClearCount 過濾（若保留通關數）。
- 4d migration down 可逆（BEGIN…ROLLBACK 驗語法）。
- 相關 memory：`project_pivot-period-less-realtime`（進度/教訓：改 query 要跑 Test.Integration、新 API 要加 apiWhitelist）。
