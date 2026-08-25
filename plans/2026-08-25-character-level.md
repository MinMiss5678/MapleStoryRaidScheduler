# 新增角色「人物等級」（全端，原則：有攻擊力就有等級）

> 輕量 plan（動手前 spec）：目標 / 背景 / 盤點 / 決策 / 範圍 / 驗收 / 工時。（決策已定：MinLevel 作硬篩、專案未上線免回填 → 無風險段）
> 觸發：bot-composed-embeds 想在 DM 顯示戰力，但系統只追蹤攻擊力 + 祝福等級、**無人物等級** → 本功能補上。
> 原則（使用者定）：**「有攻擊力（AttackPower / MinAttackPower）的地方，就要有對應等級（Level / MinLevel）」**。

## 目標

新增角色屬性 **`Level`（人物等級，1–300）**，玩家自填（同 AttackPower/MapleBlessingLevel 的信任模型、後端不查證），並在**每個出現攻擊力的地方**補上等級：輸入、快照、顯示、招募門檻、候選過濾、DM embed。

## 背景

- 現況角色戰力欄只有 **`AttackPower`（攻擊力）** + **`MapleBlessingLevel`（楓葉祝福等級）**，**沒有人物等級（角色 Level）**。
- 兩者行為不一致（既有債）：`AttackPower` 在 `TeamSlotCharacter` **快照**（邀請/申請時填）；`MapleBlessingLevel` 卻由 `Character` **live join**（`GetConfirmedMembersAsync` COALESCE）。新 `Level` 要選一致策略（見決策）。

## 盤點（AttackPower → 要補的 Level）

### A. 角色屬性（來源，存 `Level`）
| 檔 | 現有攻擊 | 要加 |
|---|---|---|
| `Domain/Entities/Character.cs` | AttackPower | `Level` |
| `Infrastructure/Entities/CharacterDbModel.cs` | AttackPower | `Level` |
| `Application/DTOs/CharacterDto.cs` / `CharacterRequest.cs` | AttackPower | `Level` |
| `Application/DTOs/ProfileDto.cs` | AttackPower | `Level` |
| `Infrastructure/Repositories/CharacterRepository.cs`（Insert/Update） | AttackPower | `Level` |
| `Infrastructure/Services/CharacterService.cs` / `ProfileService.cs` | AttackPower | `Level` |
| `Infrastructure/Query/CharacterQuery.cs`（SELECT） | AttackPower | `Level` |
| **DB migration** `Character` 表 | AttackPower | `ALTER TABLE "Character" ADD "Level" int NOT NULL DEFAULT 0` |
| **前端** `web/types/character.ts` | attackPower | `level` |
| **前端** `web/app/character/components/CharacterForm.tsx` | 攻擊力 slider/input | 等級 input（1–300 驗證） |
| **前端** `web/app/character/components/CharacterCard.tsx` | 攻擊力顯示 | 等級顯示 |
| `web/__tests__/CharacterForm.test.tsx` | — | 補等級欄位測試 |

### B. 快照 / 顯示（成員/候選/roster）
| 檔 | 角色 | 要加 |
|---|---|---|
| `Domain/Entities/TeamSlotCharacter.cs` + `TeamSlotCharacterDbModel.cs` | 成員快照 | `Level`（若採快照，見決策）+ migration `ALTER TABLE "TeamSlotCharacter" ADD "Level"` |
| `Application/DTOs/MembershipDto.cs`（TeamMemberDto / OpenTeamMemberDto / ApplicantDto / MembershipDto）| 顯示 | `Level` |
| `Application/DTOs/TeamSlotMemberDto.cs` / `TeamCandidateDto.cs` / `LfgDtos.cs` | 顯示 | `Level` |
| `Infrastructure/Query/TeamMembershipQuery.cs` / `TeamCandidateQuery.cs` / `LfgQuery.cs` | SELECT | `Level`（join Character 或讀快照欄，同決策） |
| `Application/Events/TeamNotificationEvent.cs`（`RosterEntry` / `TeamEmbedData.Subject*`）| DM embed | `Level` |
| `Infrastructure/BackgroundJobs/TeamNotificationOutboxHandler.cs`（`BuildActionEmbed` roster/subject 行）| embed 文字 | 等級 |
| `Infrastructure/Services/TeamLeaderService.cs`（`BuildEmbedSnapshotAsync` + Invite/Apply subject）| 組 embed | `Level` |
| **前端** `TeamComposition.tsx` / `me/teams` / `teams/[id]/applications` / `teams/[id]/candidates` / `teams/open` / `teams/instant` / `register` | 顯示攻擊處 | 顯示等級 |
| **前端** `web/types/leaderLed.ts`、`web/services/profileService.ts`、`web/services/lfgService.ts` | type | `level` |

### C. 招募門檻（threshold）— `MinLevel` 放「整列需求」（group 層，**非**每職業）
> 語意：攻擊尺度依職業不同（箭神900/槍神1000）→ `MinAttackPower` 在**每職業**（`TeamSlotRequirementJob`）；
> **等級與職業無關（260 就是 260）→ `MinLevel` 整列填一次**，與 `MinClearCount` 並列（在 `TeamSlotRequirement`）。
| 檔 | 現有（group 層欄位） | 要加 |
|---|---|---|
| `Domain/Entities/TeamSlotRequirement.cs`（+ DbModel + migration `TeamSlotRequirement` 表） | Count / MinClearCount | `MinLevel`（與 MinClearCount 並列） |
| `Application/DTOs/CreateTeamCommand.cs`（`CreateTeamRequirementDto`，**非** Job 子項） | MinClearCount | `MinLevel` |
| `Infrastructure/Repositories/TeamSlotRequirementRepository.cs`（需求列 Insert） | MinClearCount | `MinLevel` |
| `Infrastructure/Query/TeamCandidateQuery.cs`（候選過濾） | 職業 MinAttackPower + MinClearCount 篩 | 加「候選 `Level` ≥ `MinLevel`」硬篩（不分職業） |
| **前端** `web/app/teams/new/page.tsx`（需求列編輯） | 每列 MinClearCount + 每職業攻擊 | 每列加**一個** `MinLevel`（與通關數並列，非每職業） |
| `web/app/teams/[id]/candidates`（招募 hint） | 攻擊/通關需求 | 等級需求 |
> 注意：`TeamSlotRequirementJob` **不加** MinLevel（攻擊才每職業；等級每職業會是重複填同一數）。

### 排除
- `Domain/Entities/CharacterBossClear.cs`：只在**註解**提 AttackPower（無欄位）。
- `Infrastructure/Migrations/MigrationDbContextModelSnapshot.cs`：EF snapshot——本專案用 Dapper + 手寫 SQL migration，此檔疑似**未使用殘留**，不動（另議清理）。

## 決策

1. **`Level` 存 `Character`（來源真相）**：`ALTER TABLE "Character" ADD "Level" int NOT NULL DEFAULT 0`。玩家自填、1–300，前端驗證。
2. **快照策略對齊 `AttackPower`**：攻擊力在 `TeamSlotCharacter` 快照 → **等級也快照**（加 `Level` 欄，邀請/申請時填），語意一致（§3 承諾快照）。→ 順帶把現有「MapleBlessingLevel 走 live join」的不一致**記為已知債**，本計畫不改它。
3. **每個 DTO/query/前端有攻擊處補等級**（見盤點 A/B）。
4. **招募門檻 `MinLevel`＝硬篩、放「整列需求」group 層（已定）**：加在 `TeamSlotRequirement`（與 `MinClearCount` 並列，**非** `TeamSlotRequirementJob`）——攻擊尺度依職業不同故每職業寫、**等級與職業無關故整列寫一次**。候選過濾加「Level ≥ MinLevel」硬篩（不分職業）。與 `MapleBlessingLevel`（楓葉祝福＝隊層級需求、1 個就夠 → 顯示不硬篩）不同：**人物等級是每候選門檻** → 硬篩合理。C 類全做。
5. **`Level` 預設 0、無需回填（已定）**：**專案尚未上線**、無現有真資料 → migration `DEFAULT 0`、seed 直接設值即可，**不做強制回填 UX / 登入提示**。

## 範圍

- A（角色屬性）+ B（快照/顯示）全做。
- C（招募門檻，MinLevel 硬篩）全做（決策 4）。
- migration（Character、TeamSlotCharacter、視情況 TeamSlotRequirementJob）+ 對應 seed（seed-e2e.sql 補 Level）。

## 非範圍（YAGNI）
- 不做等級自動抓取/驗證（同攻擊力信任模型）。
- 不動 `MapleBlessingLevel` 的 live-join 不一致（另議）。
- 不清 `MigrationDbContextModelSnapshot`（另議）。

## 驗收
- [ ] migration 上/下可跑；`Character.Level`、`TeamSlotCharacter.Level`、`TeamSlotRequirement.MinLevel`（group 層）存在。
- [ ] 角色 CRUD：建立/編輯帶 Level、重載仍在（前端表單 + 後端）。
- [ ] 顯示：CharacterCard / 各隊伍組成頁 / DM embed 都出現等級。
- [ ] 候選過濾依 MinLevel **硬篩**生效；teams/new 可設 MinLevel。
- [ ] 單元（Character/Profile/Candidate + embed 映射補 Level）、整合（Character/Membership/Candidate query 回 Level）、**E2E**（角色 profile 設等級→顯示；leader-led 候選/組成顯示等級）。
- [ ] 本機真 bot：邀請/申請 embed 顯示等級。

## 工時估
- A+B（含 migration/DTO/query/前端顯示/embed/測試）≈ 1~1.5 天；C（門檻+過濾+前端需求編輯）≈ 半天；E2E/手動驗 ≈ 半天。
