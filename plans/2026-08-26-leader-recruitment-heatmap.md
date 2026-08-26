# 招募熱力圖：隊長設好需求 → 挑「最湊得起來」的開團時段

> 輕量 plan（動手前 spec）：目標 / 背景 / 決策 / 範圍 / 驗收 / 工時 / 已知邊界。（維度已定：單日 × 每小時 × 組成可行性 → 無待你決策的未決）
> 關聯：`Infrastructure/Query/TeamCandidateQuery.cs`（候選池）、`Infrastructure/Services/TeamLeaderService.cs`（`GetCandidatesAsync` 的過濾/可用判定）、`Domain/Helpers/CompositionQuota.cs`（匹配）、`Infrastructure/Repositories/TeamSlotCharacterRepository.cs`（`GetConfirmedDiscordIdsAtAsync`）、`web/app/teams/new/page.tsx`。
> 觸發：需求（職業＋門檻）目前只在「開團後」篩候選清單；反過來——**先設需求、再看未來哪個時段真的湊得齊這套組成**，讓隊長挑 `SlotDateTime`。

## 目標

在 `/teams/new`，隊長設好需求（職業＋攻擊/等級/通關門檻）後，顯示一張 **未來 N 天 × 每小時** 的熱力圖：每格顏色＝**那個真實日期時段，合格候選能填滿這套需求的比例**（組成可行性）。點格即帶入 `SlotDateTime`。

## 背景

- 既有候選管線（`GetPoolAsync` + `GetOverridesForDateAsync` + `GetConfirmedDiscordIdsAtAsync` + 服務內 `IsAvailableAt` + 需求過濾）已能對**單一團時間**算出合格候選。熱力圖＝把這套**掃過未來 N 天的每個整點格**（單日語意：疊 override、扣已 booked，非週 pattern）。
- 「填滿需求」的度量重用 [[composition-quota]] 的二分匹配（`CompositionQuota`）——只是方向從「已確認成員 → 名額」換成「可用候選 → 需求名額」，問**最大匹配數 / ΣCount**。

## 決策（維度已定）

1. **單日（未來 N 天，預設 N=14）**：格＝真實日期＋整點；疊該日 override、扣該精確 datetime 已 Confirmed（不可分身）。非週 pattern（見討論）。
2. **每小時分桶、只畫活躍時段**：時段軸 auto-fit 到候選池常設時段的最早~最晚（無資料時 fallback 18:00–24:00）。格語意＝「該整點**開團**招得到誰」（對齊 `IsAvailableAt` 的起點落點判定）。
3. **組成可行性度量（層次二）**：每格值＝`MaxRequirementSlotsFilled(該格合格候選職業, requirements) / ΣCount`（0–1）。0＝湊不到、1＝整套可填。hover 顯示「3/4 可填（缺英雄）」。
4. **需求為草稿**：`/teams/new` 尚未建隊 → endpoint 收 `bossId` + **草稿 requirements**（同 `CreateTeamRequirementDto` 形狀）+ `days`，不需 teamSlotId。無 team → 無 activeIds/self 排除；booked 排除仍照每格 datetime 算。
5. **只限 Scheduled**：Instant 團時間＝現在、不挑時段 → 熱力圖 N/A。
6. **重用匹配**：`CompositionQuota` 加 `MaxRequirementSlotsFilled(availableJobs, requirements)`（同 Kuhn，名額只展開需求列 Count、無未指定池，回最大匹配數）。
7. **抽候選合格判定**：把 `GetCandidatesAsync` 內「職業∈需求列 且 攻擊≥ 且 等級≥ 且 通關≥」的 predicate 抽成共用（服務私有 helper 或 Domain），供候選清單 + 熱力圖每格共用（DRY，避免兩處走鐘）。

## 範圍

- **後端**：
  - `CompositionQuota.MaxRequirementSlotsFilled`（+ 單元）。
  - 抽候選合格 predicate 共用。
  - `ITeamLeaderService.GetRecruitmentHeatmapAsync(bossId, requirements, days)` → 回 `HeatmapDto`（cells: {date, hour, filledRatio, filledCount, totalRequired, missingJobs[]}）。實作：撈一次池 → 各日期 override 批次 → 每格（date×hour）過濾可用+合格+扣 booked → `MaxRequirementSlotsFilled` → 比例。
  - Controller `POST /api/teams/heatmap`（登入即可，回聚合值、無身分 → 不觸 §9.12）。
- **前端** `/teams/new`：需求設好後「看熱力圖」→ 日期×整點格、依 filledRatio 上色（灰→深綠）、hover 顯示 filled/total + 缺哪職業、點格帶入 `SlotDateTime`（重用 register/profile 時段格樣式）。

## 驗收

- [ ] 需求「1黑騎士+1英雄」→ 某時段只有黑騎士有空 → 該格 0.5（缺英雄）；兩者都有空 → 1.0。
- [ ] 某候選對某日期設「不行」override / 已在該 datetime 別隊 Confirmed → 該格不計他（單日語意）。
- [ ] 只畫活躍時段、每小時桶；點格正確帶入對應日期整點的 `SlotDateTime`。
- [ ] Instant kind → 不顯示熱力圖。
- [ ] 單元（`MaxRequirementSlotsFilled` 多案例：OR/重疊/供給不足）+ 整合（heatmap query 對真 DB 的 override/booked 修正）+ E2E（/teams/new 設需求→熱力圖出現→點格帶入時間→開團）。

## 工時估
- 後端（matching 擴充 + predicate 抽取 + heatmap query + endpoint）≈ 1~1.5 天；前端（格子元件 + 點選帶入）≈ 半天~1 天；測試 ≈ 半天。

## 已知邊界（非待決）
- **潛在池、非保證**：格反映「常設＋override＋未 booked」，仍不保證那些人到時真的接受邀請（同候選清單本質）。
- **效能**：每格跑「過濾池 + 小匹配」，cells≈N天×~12小時、池 P 為公會規模 → O(cells×P) 小；池大時可先撈一次池 + 各日期 override 批次（已納入設計）。真的大再快取。
- **起點落點語意**：格＝開團**起點**在該小時、非「整場都在」（Boss 無時長欄位，同現有候選判定；要「整場」需先給 Boss 時長，另題 YAGNI）。
- **30 分桶**：先 hourly；真有 :30 開團需求再加切換。
