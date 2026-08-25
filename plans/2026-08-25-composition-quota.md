# 組成配額：confirm 端強制職業名額（超額同職業入隊擋下）

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時 / 風險。
> 觸發：leader-led 現況「requirements 只篩候選 + hint、不強制組成」→ 隊長超額邀同職業、又都接受 → 塞爆總容量、擠掉其他職業（如需求「1黑騎士1英雄」卻進 2 黑騎士）。選定**方案 A：confirm 端強制職業配額**。
> 關聯：`Infrastructure/Services/TeamLeaderService.cs`（`ConfirmMemberAsync` / `CreateTeamAsync` / `InviteMemberAsync` / `ApplyAsync`）、`Domain/Entities/TeamSlotRequirement(Job)`、`Infrastructure/Query/TeamMembershipQuery.cs`（confirmed jobs）。

## 目標

accept/approve 定案（`ConfirmMemberAsync`）時，除現有「總容量（`RequireMembers`）」檢查，**再擋「該職業名額已滿」**：確保單一職業不會超過需求配額、把別的職業擠掉。名額滿 → `BusinessException("此職業名額已滿")` → 互動 handler / 前端友善提示（同「隊伍已滿」）。

## 背景

- 現況 `ConfirmMemberAsync` 只檢查 `CountConfirmedAsync >= RequireMembers`（總數），**無職業檢查**（見已合併討論）。requirement 只用於候選過濾 + hint。
- 需求結構：`TeamSlotRequirement`＝`Count` + `Jobs[]`（OR 群組，如「箭神 or 槍神 1 位」）。一隊多列需求。
- 難點：`Jobs` 是 OR 群組、**且支援重疊**（同職業可跨列，如「黑騎士 or 英雄」+「英雄 or 主教」讓英雄跨兩列）、需求可能**不滿** `RequireMembers`（剩「未指定 slot」任意職業可填）→ 配額歸屬不唯一 → **用二分匹配可行性判定**（已定：支援重疊）。

## 決策

1. **可行性模型＝二分匹配（成員 ↔ 名額）**。把「名額」展開成節點：
   - 每列需求 `Ri` 展開成 `Count_i` 個名額節點，只接受 `job ∈ Ri.Jobs` 的成員；
   - **未指定池** `U = RequireMembers − Σ Count_i` 個名額節點，接受**任意職業**；
   - 一組已確認成員「可行」＝存在一組匹配，把**每位**成員各配到一個相容名額（最大匹配數 == 成員數）。
   - `CreateTeamAsync` 仍驗 `Σ Count_i ≤ RequireMembers`（否則 U<0、需求超過容量矛盾）→ 違反 `BusinessException`。
2. **confirm 端強制（核心）**：`ConfirmMemberAsync` 在**現有 advisory-lock 臨界區內**（與總容量檢查同一鎖，防競態）：
   - 撈本隊「已確認成員的職業清單」（`TeamMembershipQuery`）+ 本次要定案的職業 → 組成**試加後**的成員集合。
   - 對該集合跑上述匹配（Kuhn 增廣路徑，成員/名額皆 ≤ `RequireMembers`、小規模、cheap）。
   - 匹配數 == 成員數 → 可行，**放行**；否則（本次職業使某些成員無處可放）→ **擋** `此職業名額已滿`。
   - 例：需求 [黑騎士1]+[英雄1]、`RequireMembers=2`（U=0）→ 第 2 黑騎士試加 → 黑騎士名額滿、U=0 → 匹配數 1<2 → 擋。若 `RequireMembers=6`（U=4）→ 第 2 黑騎士配到未指定池 → 放行（合理的 filler）。
3. **重疊 OR 群組由匹配自然處理**：如 [黑騎士 or 英雄 1]+[英雄 or 主教 1] → 2 英雄可行（各配一列）、3 英雄不可行（第 3 個擋）。**不再限制同職業跨列**。
4. **隊長自帶角色**：`CreateTeam` 帶 `LeaderCharacterId` 直接 Confirmed → 也算進匹配的成員集合（有職業的 confirmed 成員）。
5. **同步防護沿用**：匹配可行性檢查放在 `AcquireTeamSlotEditLockAsync` 內、與 `CountConfirmed` 重讀 + xmin 同一臨界區 → 兩人同職業同時 accept 由 advisory lock 序列化、第二個重跑匹配看到不可行而擋。
6. **per-job 自動撤邀（已定：保留超額搶位、幾乎不白接受）**：**只在 confirm 端擋，不在邀請端預擋**（保留 Pull 一次撒網、超額搶位）。既有 `ConfirmMemberAsync` 已有「本次定案使**總容量**額滿 → `RevokePendingInvitesAsync` 撤全部待接受邀請（不 DM）」（line 362-370）→ **延伸成 job 維度**：本次定案後，對每個仍待接受（`Invited`）邀請的職業，用同一匹配 helper 試加，**若已不可行 → 撤該職業的待接受邀請**（不 DM，同既有語意）。
   - 效果：第 1 黑騎士接受 → 其餘黑騎士待接受邀請自動撤（對方看到「邀請已撤回」而非白接受被打槍）。只剩「同一瞬間兩人都點接受」的極短競態殘留白接受，由 advisory lock 序列化、第 2 個 confirm 被擋——與現行總容量行為一致。
   - **`InviteMemberAsync` 不加 per-job 擋**（僅保留現有「總容量已滿才擋新邀」line 274-280）→ 超額邀不受限。

## 範圍

- `CreateTeamAsync`：加需求驗證（`Σ Count_i ≤ RequireMembers`；**不再限制職業跨列**）。
- **匹配可行性 helper**（純函式、可單元測試）：輸入 `(已確認職業清單, requirements, RequireMembers)` → 回傳可行/不可行。Kuhn 增廣路徑；名額 = 各列 Count 展開 + 未指定池 U。
- `ConfirmMemberAsync`：加匹配可行性檢查（accept〔玩家〕/ approve〔隊長〕共用此路徑，一次到位）+ **per-job 自動撤邀**（定案後對各待接受職業試加、不可行則撤，接在既有總容量 Tier 3 撤銷旁）。
- **repo**：`RevokePendingInvitesByJobAsync(teamSlotId, job)`（或撈待接受成員的 job 清單後逐職業判斷撤）——比照既有 `RevokePendingInvitesAsync`。
- `TeamMembershipQuery`：撈「某隊已確認成員的職業清單」（`GetConfirmedJobsAsync` 重用）+「待接受成員的職業清單」（自動撤邀用）。
- 錯誤訊息 `此職業名額已滿` → 互動 handler（BusinessException 分流）+ 前端已有 BusinessException 呈現。

## 驗收

- [ ] 你的情境：需求「1黑騎士 + 1英雄」（RequireMembers=2）→ 第 1 黑騎士 Confirmed；第 2 黑騎士 accept → **擋「此職業名額已滿」**、不 Confirmed；英雄仍可 accept 入隊。
- [ ] OR 群組：「箭神 or 槍神 1 位」→ 第 1 個（不論箭神槍神）Confirmed；第 2 個該群組職業 → 擋。
- [ ] **重疊 OR 群組**：[黑騎士 or 英雄 1]+[英雄 or 主教 1]、RequireMembers=2 → 2 英雄可行、第 3 英雄擋；2 英雄後主教 accept 擋（已無名額）。
- [ ] 未指定 slot：需求「1黑騎士」但 RequireMembers=6 → 黑騎士 1 個滿後，其餘任意職業可填到 6。
- [ ] 隊長自帶黑騎士 + 需求「1黑騎士」→ 名額已被隊長佔、被邀黑騎士 accept 被擋。
- [ ] 並發：兩黑騎士同時 accept（容量夠但職業配額=1）→ advisory lock 序列化、只 1 個成功、另一個擋。
- [ ] 建隊驗證：`Σ Count > RequireMembers` → 建隊被擋。
- [ ] 單元（匹配 helper：單列滿/未指定 fallback/重疊群組可行與否；CreateTeam 驗證）+ 整合（真 DB 並發 accept）+ E2E（Push/Pull 超額同職業被擋）。

## 工時估
- 匹配 helper（Kuhn）+ CreateTeam 驗證 + ConfirmMember 接線 + query ≈ 一天；測試（匹配 helper 單元多案例 + 並發整合 + E2E）≈ 半天~一天。

## 非範圍（YAGNI）
- 不改候選過濾 / hint（那層照舊；本計畫只加 confirm 強制）。
- 不做即時「各職業 x/y」前端配額條（可另議 UX，非強制邏輯必需）。

## 已知邊界（非待決）

- **只 confirm 端擋、不邀請端預擋**（決策 6）：保留 Pull 超額搶位；被邀者的白接受由 per-job 自動撤邀幾乎消除，殘留僅「同瞬間雙擊」極短競態（advisory lock 序列化第 2 個 confirm 被擋），與現行總容量行為一致。
- **自動撤邀後被邀者 DM 的死按鈕清理**（編輯成「邀請已失效」）另立 [[dm-revoke-cleanup]]，不在本 plan。
