# leader-led：玩家自助退隊 + 候選狀態感知去重

狀態：**規劃中（尚未實作）**。源於情境：玩家因「隊伍已滿」拒絕邀請 → 隊伍內有人退隊、位子重開 → 想把先前拒絕的人再邀回來。追這條發現兩個缺口。

## 決策 2026-08-07：候選透明化（翻掉主計畫 §9.12 匿名）

**候選清單改「透明」**——DTO 回 **`DiscordName`（顯示名，非 raw 數字 discordId）**，隊長看得到候選是誰。理由：
- 這是**公會內固定開團**的產品，**身分本就互通**（角色名一報就知道是誰）→ §9.12 藏 discordId 沒真的匿名、收益薄。
- §9.12 要擋的「盜揪/跨公會/陌生人媒合公平」**不是主模型**（非公會開放已在主計畫 §12 YAGNI 掉）。
- **好處**：隊長認得出可靠老班底 → **固定團**（打硬王最吃默契/穩定）；順帶消掉「同人多角看不出」的黑箱。
- **保留能力欄**（能力＋身分都給，挑不挑仍隊長決定）。
- **實作更簡單**（少維護匿名 + 分組）。
- **留意（非阻擋）**：透明會讓「只揪常客」固化 → 新人(無戰績)更難擠進；日後可加軟機制（保留名額/新人標記），**先不做**。

→ 只回 `DiscordName`（顯示層），**raw discordId 仍留後端**（邀請仍以 CharacterId 為目標，前端不需要數字 id）。主計畫 §9.12「候選藏 discordId/discordName」**此決策翻掉**、須於主計畫標記 superseded。

**顯示名優先用「公會暱稱」（配套，讓透明化真的有用）**：目前 `DiscordName` 存的是**全域 username**（登入 `GET /users/@me` 的 `username`，`AuthAppService` `DiscordName = user.Name`），**公會暱稱 `nick` 被丟掉**（`DiscordOAuthClient.GetUserRolesAsync` 已打 `/guilds/{guild}/members/{id}`、回應含 `nick`，但只取 `roles`）。但公會裡大家**認的是 nick 不是全域 username** → 透明化顯示 username 辨識度差。改為**登入時 `DiscordName = nick ?? global_name ?? username`**：
- `GetUserRolesAsync` 順手多讀一個 `nick`（同一個 member 回應，零額外請求）。
- `/users/@me` 目前只讀 `username`，需補讀 `global_name`（`DiscordUserDto` 加欄）當中間 fallback。
- **一處改、全站受惠**（roster/候選都變公會認得的名字）。
- 限制：是**登入當下快照**，改暱稱要重登才刷（同現況）；`nick` 可 null → 靠 fallback 鏈。
- 影響：`DiscordOAuthClient`（讀 nick + global_name）、`DiscordUserDto`（加 global_name）、`AuthAppService`（fallback 組 DiscordName）。

前置事實（已驗 code/約束）：
- **目前沒有玩家自助退隊**：leader-led 只有 邀請/接受(Accept)/拒絕(Decline)、申請(Apply)/核准/拒絕；`Confirmed` 成員退出只能靠 admin 的 god-method `UpdateAsync`。
- **拒絕過可再被邀請**：Decline → `Rejected`；`uq_tsc_active_membership` 只 `WHERE Status IN ('Applied','Invited')` → 重邀建新 `Invited` 不撞 409。舊 `Rejected` 留作歷史。
- **容量＝Confirmed 計數**（非佔位列）；退隊只是少一個 Confirmed → 位子自動重開（隊重回 開放隊/候選）。
- **候選 DTO 依 §9.12 藏 discordId** → 去重無法在前端做（前端不知道誰是誰），**必須後端**。

## Feature 1：玩家自助退隊（功能缺口）

- **端點**：`POST /api/teamSlot/{id}/Leave`（或 `DELETE .../Members/me`）——玩家退出自己在該隊的 `Confirmed` 成員資格。
- **服務** `LeaveTeamAsync(teamSlotId, currentDiscordId)`：
  - 找該隊、`DiscordId==currentDiscordId` 且 `Status=Confirmed` 的成員列；不存在 → 404/400。
  - 狀態改 → **新增 `Left`**（拍板 2026-08-07；migration 改 CHECK `IN (...,'Rejected','Left')`）＋**加欄 `LeftAt timestamptz NULL`**、退隊時 `LeftAt = now()`（供退團率窗口分子 + 未來時機權重）。**為何不重用 `Rejected`**：要能區分「自願退出」vs「被拒」→ 供**可靠度信號**（見下）。`Left` 行為同 `Rejected`（終態、不占容量、不擋重邀），但語意分得出。**實作稽核**：所有 `== 'Rejected'` 硬比對處要一併處理 `Left`（容量算 Confirmed、去重列 active 正表列 → 多數約束天然涵蓋，但仍需掃過確認）。
  - **xmin 樂觀鎖**單列更新即可——退隊只**減少** Confirmed 數、**不觸容量上限**，故**不需 advisory lock**（跟 Confirm 不同，Confirm 要序列化守上限）。
  - **通知隊長**（outbox）：「X 退出了你「王」時段的隊伍，位子已重開」。
- **前端**：`/me/teams` 已加入卡片加「退隊」鈕 → confirm 對話 → `leaveTeam` mutation → `toast` + invalidate `myTeams`（+ 若隊長端在看，`ledTeams`/`openTeams` 自然刷新）。
- **邊界**：
  - 退隊者**同時是隊長**？退出成員身分 ≠ 解散隊（`LeaderDiscordId` 不變）。允許「掛名隊長但角色不打」。需確認產品可接受。
  - 併發：同一人重複點退隊 → xmin 第二次失敗、無害。

## Feature 1b：退團率＝可靠度信號（admin 設定）

建在 `Left`/`LeftAt` 上，讓隊長避開常落跑的人（raid 社群最恨臨時烙跑）。**用「窗內退團率」而非原始次數**（原始次數會懲罰活躍玩家：打 100 隊退 5 次 ＜ 打 6 隊退 5 次）。
- **指標**：`退團率 = 退團數 ÷ 參加數（最近 N 個月）`
  - 分子：`LeftAt` 落在最近 N 個月的 `Left` 數（by DiscordId，跨隊全域）。
  - 分母：參加數——`Confirmed`(或曾 Confirmed→現 Left) 且 `SlotDateTime` 落在最近 N 個月。（`Confirmed` 無動作時間戳；分母暫錨 `SlotDateTime`，要嚴格對稱日後加 `ConfirmedAt`。）
  - 資料**全從既有 `TeamSlotCharacter`（Status + LeftAt + SlotDateTime）推導、無新表**；走現有 `idx_team_slot_char_discord (DiscordId)` 索引，guild 規模數百~數千列 → 毫秒級、即時算不預存。
- **admin 三旋鈕**（既有 `SystemConfigService` / `/admin/config`）：**時間窗(月) / 門檻率 / 最小樣本數**。
  - **最小樣本數（關鍵）**：參加 < M 次不算率（避免 1/1=100% 小樣本誤判）。
  - **預設關**——名譽信號敏感、公會自決。
  - **後端 gate**：config 關時 DTO **不回** 該欄（別讓 client 拿到只前端藏 → 避免外洩）。
- **時機權重（後續、已具備資料、先不做）**：`LeftAt − SlotDateTime` ＝離打王多近才烙跑（越近越該重罰）。有了 `LeftAt` 就有料，未來可加權。
- **真・黑名單（獨立未來功能，不做）**：自動把某人從候選排除，需新資料模型 + 治理。**別跟信號混**——率是「信號、供人判斷」，黑名單是「自動排除」。
- **騷擾/反覆邀已拒者：不做**（2026-08-07 定）——交**幹部社會性處理**；故 decline 維持 `Rejected`、**不加 `Declined` 狀態**、不數拒絕次數。

## Feature 2：候選狀態感知去重（後端；原以為前端小債，實為後端）

- **現況**：`TeamCandidateQuery.GetPoolAsync` 是 boss-agnostic 報名池；`GetCandidatesAsync` 只按 時段/職業/攻擊/通關 篩，**不看該候選對本隊的現有狀態** → 已被本隊 `Invited`/`Applied`、甚至已 `Confirmed` 的人**仍列為候選**（重整後又出現；再邀 active 撞 409，邀已 Confirmed 者更亂）。
- **做法**：`GetCandidatesAsync` 加一層過濾——**排除「其玩家(DiscordId)在本隊已有 active 成員資格」的候選**：
  - 排除：`Confirmed`（已在隊上）、`Invited`/`Applied`（待處理中）。
  - **保留**：`Rejected`（拒絕/退隊過的）+ 無列者 → **可再邀**（位子重開時邀得回，正是本情境所需）。
  - 去重**以 DiscordId 為準**（非 CharacterId）：`uq_tsc_active_membership` 是 per (TeamSlotId, **DiscordId**) → 一人一隊只能一個 active，故某玩家任一角色已 active，其名下其他角色也不該再列（否則邀了也 409）。
  - SQL：候選池再 `WHERE NOT EXISTS (SELECT 1 FROM "TeamSlotCharacter" tsc WHERE tsc."TeamSlotId"=@teamSlotId AND tsc."DiscordId"=<候選玩家> AND tsc."Status" IN ('Confirmed','Invited','Applied'))`。
- **註**：即使候選改透明（回 `DiscordName`），此「排除已 active」過濾仍**該在後端**——因為要查每個候選對本隊的成員狀態（後端資料）。透明化只是**消掉「同人多角看不出」的黑箱**（隊長現在看得到同名多角），不改「去重放後端」的結論。搭配 F1：加 `discordName` 一併在 `TeamCandidateDto` + `GetCandidatesAsync`/pool query 回傳。

## 兩者合起來讓情境成立
拒絕(滿) → 有人**退隊**(F1，位子重開) → 拒絕者狀態是 `Rejected` → **F2 讓他重新出現在候選** → 隊長**再邀**（`Rejected` 不擋）→ 玩家接受入隊。全鏈路通。

## 驗證（e2e）
- **退隊**：Confirmed 成員退隊 → `/me/teams` 已加入消失 + 隊在開放隊/候選重現（位子重開）。
- **去重**：邀請候選後，重整候選頁該員**不再出現**；退隊/拒絕後**重新出現**、可再邀。
- 併發：退隊 xmin、重邀 409 兜底不破現有 Push/Pull e2e。

## 已定案（2026-08-07）
- 退隊狀態 **`Left`** + **`LeftAt`**。
- 可靠度信號 = **窗內退團率**，admin 三旋鈕（**時間窗/門檻率/最小樣本數**）、預設關、後端 gate。
- **騷擾/反覆邀已拒者：不做**（幹部處理）→ decline 維持 `Rejected`、不加 `Declined`。
- 候選**透明化**（翻 §9.12）+ 顯示名優先**公會暱稱**。

## 待解問題
- 隊長本人退成員身分但續任隊長，產品可接受？（傾向可）
- **黑名單 / 退團時機加權 / `ConfirmedAt` 對稱分母**：獨立未來，先只記待辦（見 Feature 1b）。

## 相關但獨立（已另開計畫）
- **隊長轉讓（需對方同意）**：見 `plans/2026-08-07-leader-transfer.md`（`TeamSlot.PendingLeaderDiscordId` + propose/respond + outbox + xmin，形狀同 invite/accept）。
