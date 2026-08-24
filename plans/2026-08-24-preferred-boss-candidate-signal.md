# 偏好王 → 候選匹配軟訊號

> 輕量 plan（動手前 spec）：目標 / 決策 / 範圍 / 階段 / 驗收 / 工時。
> 背景：leader-led 拔掉自動排團後，隊長開隊挑時間/挑人沒有「誰想打這王」的訊號。加「角色可複選偏好王」，當候選匹配的**軟訊號**（排序/標記，非硬篩）→ 補回一點最佳化視野，又不違背「候選 boss-agnostic」原則。

## 目標

角色可在 `/character` 複選「偏好王」；隊長看候選清單時，**偏好本隊王**的候選排前面 + 標記，**但不排除**任何人。

## 關鍵決策

1. **軟訊號、非硬篩**（守 boss-agnostic，見 memory `project_boss-entry-rules-and-candidate-filtering`）：
   - 有設偏好且**含本王** → 排最前 + 🏷️ badge。
   - **沒設任何偏好**（= 什麼王都想打）→ 中性，維持原順序，**不降級**。
   - 設了偏好但**不含本王** → 排最後（仍出現、不排除）。
   - 排序鍵：`prefersThisBoss DESC, hasAnyPreference ASC, （既有次序）`。等價三層：含本王 → 無偏好 → 有偏好但不含本王。
2. **偏好掛 Character**（跟 `IsSeekingRaid`/`CharacterBossClear` 同層；不同職業角色適合不同王）。
3. 比照現有 **BossClears** 全鏈（DTO/Domain/Infra/Service/Controller/DI/測試）。
4. 兩個候選 query（`GetPoolAsync` 排程 + `GetInstantPoolAsync` 即時）都要帶偏好訊號。

## 範圍（四層）

### 1. DB（migration 000022）
- 建表 `CharacterPreferredBoss("CharacterId" text FK→Character, "BossId" int FK→Boss, PK(CharacterId,BossId))`，兩者皆 `ON DELETE CASCADE`。
- up/down 各一。

### 2. Backend
- Domain：`CharacterPreferredBoss` 實體 + `ICharacterPreferredBossRepository`（`GetByCharacterAsync` / `ReplaceAsync(characterId, bossIds)`）。
- Infra：`CharacterPreferredBossDbModel` + `CharacterPreferredBossRepository`（Dapper，Replace = 刪舊插新，同交易）。
- Service：`ICharacterService` 加 `SetPreferredBossesAsync(characterId, bossIds, ownerDiscordId)`（驗擁有者，比照 BossClears）+ 讀取。
- Controller：`GET/PUT /api/Character/{id}/PreferredBosses`（PUT = 整批取代，比照 BossClears）。
- DI：`ServiceCollectionExtensions` 註冊 repo。
- **候選 query**：`TeamCandidateQuery` 兩個 pool SQL 加
  `LEFT JOIN "CharacterPreferredBoss" pbx ON pbx."CharacterId"=c."Id" AND pbx."BossId"=@bossId`（→ `PrefersThisBoss`）
  + `EXISTS(SELECT 1 FROM "CharacterPreferredBoss" WHERE "CharacterId"=c."Id")`（→ `HasAnyPreference`）；`CandidatePoolItem` 加兩欄。
- `TeamCandidateDto` 加 `PrefersThisBoss`（給前端 badge）；`TeamLeaderService.GetCandidates*` 帶入並依上面排序鍵排序。

### 3. 前端（原生 next dev，秒熱重載）
- `/character` 角色卡：偏好王**複選**（比照 `/teams/new` 職業多選 UI；王清單來自 `useBosses`）。存 → `PUT /api/Character/{id}/PreferredBosses`。
- `/teams/[id]/candidates`：`prefersThisBoss` 的候選加 🏷️「偏好此王」badge；清單依後端已排好的順序呈現。

### 4. 測試
- 單元：`TeamCandidateQuery`/`TeamLeaderService` 排序三層（含本王/無偏好/偏好他王）；`CharacterService.SetPreferredBosses` 驗擁有者 + Replace 語意。
- 整合：repo Replace round-trip；候選排序 SQL 真跑一遍。
- E2E：`/character` 設偏好 → 候選頁該角色帶 badge 且排前（可加進既有 leader-led-candidates 或新一支）。

## 驗收

- [ ] migration up/down 可重現；`schema_migrations` 到 22。
- [ ] 設偏好含本王的候選排最前 + badge；沒設偏好者不降級；設了不含本王者殿後、**皆未被排除**。
- [ ] 擁有者以外不能改他人偏好（403）。
- [ ] 單元 + 整合 + E2E 綠（本機先跑 E2E 再 push）。

## 工時估
- DB + backend（含 query 排序）≈ 半天；前端（角色卡複選 + 候選 badge）≈ 半天；測試 + E2E ≈ 半天。

## 非範圍（YAGNI）
- 不做「依偏好自動配隊 / auto-match」（維持 leader-led、隊長決定）。
- 不做偏好權重/優先度細分（先 布林集合）。
- 不動可用時段熱力圖（另案；本案先補「想打這王」訊號）。
