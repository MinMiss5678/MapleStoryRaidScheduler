# 計畫：程式碼壞味道清理（leader-led 讀寫/DTO/DRY）

> 輕量 spec。動機：leader-led UAT 一批（PR #91）合併後掃出的壞味道，挑**有根據、值得修**的做。
> 純內部重構——**對外行為與 API 回傳資料不變**，靠既有測試（單元 289 / 整合 48 / e2e 10）當回歸網。

## 目標
消除三個具體味道：需求列組裝重複、`MembershipDto` 情境相依欄位、讀方法混在寫 service（違反自家 CQRS-lite）。不追求「乾淨學」，只修會扎到維護的地方。

## 範圍（IN）

### R1 — 抽掉重複的需求列組裝（DRY）⭐
- **問題**：`Infrastructure/Query/TeamMembershipQuery.cs` 的 `GetOpenTeamsAsync`（~99–111）與 `GetRequirementsAsync`（~211–223）有近乎逐字相同的 `GroupBy(RequirementId) → OpenTeamRequirementDto` 組裝（加 `GetRequirementsAsync` 時複製的）。
- **改**：抽 private `static List<OpenTeamRequirementDto> AssembleRequirements(IEnumerable<ReqRow> rows)`，兩處共用。
- **風險**：極低，純內部；`ReqRow` 已是私有型別。

### R2 — 拆 `ApplicantDto`，讓 `MembershipDto` 誠實
- **問題**：`MembershipDto` 同時服侍「我的邀請 / 我的隊 / 審核佇列」，但 `DiscordName`/`MapleBlessingLevel`/`BossClearCount` **只有審核佇列有值**（註解自承「其他情境為 0」）→ 契約不誠實，讀者分不出哪些欄位當下有效。
- **改**：
  - 新增 `ApplicantDto`：`MemberId, CharacterId, CharacterName, DiscordName, Job, AttackPower, BossClearCount, MapleBlessingLevel, Status`（審核決策所需的最小集）。
  - `GetApplicationsAsync`（query + service + `ITeamLeaderService`）回 `IEnumerable<ApplicantDto>`。
  - `MembershipDto` **移除** `DiscordName`/`MapleBlessingLevel`/`BossClearCount`（回歸「本人自視」的精簡集；`GetByDiscordIdAndStatusAsync` 不再 join Player / Character 拿這些）。
  - 前端：新增 `Applicant` 型別、`getApplications` 回 `Applicant[]`、`applications/page.tsx` 改用之；`Membership` 型別移除那三欄。
- **風險**：中。動到審核頁 → **e2e `leader-led.spec.ts`（Push 核准）會經過**，須本機 E2E 綠。

### R3 — 收斂讀寫混雜（CQRS-lite 一致性）
- **問題**：`TeamLeaderService` 有 7 個 `Get…` 讀方法；其中 `GetOpenTeamsAsync`/`GetLedTeamsAsync` 是**純轉發**（middle-man，直接 `_membershipQuery.XXX`、沒加東西）。
- **改（保守版）**：把這兩個純轉發從 `ITeamLeaderService` 移除，controller 直接注入 `ITeamMembershipQuery` 呼叫。
  - 需授權的讀（`GetTeamRoster`/`GetTeamMembers`/`GetRecruitmentGap`/`GetCandidates`/`GetApplications` 做 `EnsureLeaderOwns` 或狀態編排）**保留在 service**——它們不是純轉發、有真編排，硬搬進 query 反而要把授權下沉，得不償失。
- **風險**：中。動到 controller 端點的依賴注入；`Me` 相關端點路由不變、回傳不變。

## 範圍（OUT，這次不做，YAGNI）
- **`(long)discordId` 到處手動轉型**（~10 repo/query、30+ 處）：既有、廣佈、改動大、ROI 低。要治得做 `DiscordId` 值型別 + Dapper type handler，屬另一個專案級決定，另開計畫。
- **`candidates/page.tsx`（256 行）拆 hook/子元件**：mild，之後有痛感再拆。
- **狀態模式（State pattern）**：已評估不適合（轉移是不同操作 + 不變量在 DB），維持現狀。

## 不可破的不變量（回歸重點）
- 各端點**回傳的 JSON 欄位對前端不變**（除了 R2 蓄意搬移 discordName/通關/祝福到 `ApplicantDto`——前端同步改）。
- 授權不變：`GetApplications`/roster/members/gap 仍限隊長或成員（外人 403）。
- 併發把關（advisory lock / xmin / 唯一索引）**完全不碰**。

## 驗收
- 後端 `dotnet build` + 單元（現 289）綠；整合（現 48）綠。
- 前端 `tsc` + `vitest`（現 21）+ `npm run build` 綠。
- **E2E 本機跑（R2 動審核頁）**：`compose.e2e.yaml --profile ci` 10 支綠；必要時同步更新 `leader-led.spec.ts` 斷言。
- Grep 確認：`MembershipDto` 不再有 `MapleBlessingLevel`/`BossClearCount`/`DiscordName`；`TeamMembershipQuery` 只剩一處 `new OpenTeamRequirementDto`。

## 交付
- 單一 PR，依 R1/R2/R3 分 commit（繁中）。docs 若受影響（`business-rules.md` 的 source 欄提到 `GetApplicationsAsync` 回 `MembershipDto`）順手校正。
