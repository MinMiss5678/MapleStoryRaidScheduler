# 移除即時找隊「任意王」(BossId 可為 NULL)

> 輕量 plan：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟。
> 定位：YAGNI 功能移除。「任意王」(LfgIntent.BossId=NULL) 技術上端到端接好、但**社群實際找隊一定針對特定王**、沒人會用 → 砍掉，順手簡化 schema/查詢/前端。

## 背景（現況：任意王是活的但無真實用途）

- `LfgIntent.BossId` 是 `int?`，`null = 任意王`（entity/DbModel/DTO 全 nullable）。
- 前端 `teams/instant` 有 `<option value="">任意王</option>`，選了送 `bossId=null`。
- `TeamCandidateQuery.GetInstantPoolAsync` 用 `li."BossId" IS NULL OR li."BossId" = @bossId` 把任意王配到**任何**即時團。
- 去重靠 `uq_lfgintent_char_boss (CharacterId, BossId) NULLS NOT DISTINCT`（PG15+，讓 NULL 也視為同一組）。

→ 決策：**BossId 改 NOT NULL、必填一隻王**。連帶 `NULLS NOT DISTINCT`、`IS NULL` 比對、前端選項全部拿掉。

## 範圍

### A. 後端 code
1. **Entity** `Domain/Entities/LfgIntent.cs`：`int? BossId` → `int BossId`，移除 `// null = 任意王`。
2. **DbModel** `Infrastructure/Entities/LfgIntentDbModel.cs`：`int? BossId` → `int BossId`。
3. **DTO** `Application/DTOs/LfgDtos.cs`：`LfgIntentCreateCommand.BossId` / `LfgIntentDto.BossId` `int?` → `int`；移除 `BossId null = 任意王` 註解。
4. **Query** `TeamCandidateQuery.GetInstantPoolAsync`（:101）：`AND (li."BossId" IS NULL OR li."BossId" = @bossId)` → `AND li."BossId" = @bossId`；改註解（:79「該王或任意王」→「該王」）。
5. **Repository** `LfgIntentRepository`（:18）：改註解（去掉「含任意王=NULL」「NULLS NOT DISTINCT」字樣）。`ON CONFLICT ("CharacterId","BossId")` 不變。
6. **驗證** `LfgService`（發布意圖）：BossId 現為必填 —— DTO 改 `int` 後，client 漏傳會 bind 成 `0`。加 app 層驗證：BossId 必須是**存在的 Boss**（或至少 `> 0`），否則回 4xx（`BusinessException`）。

### B. Schema migration（新增 000021）
- **先刪殘留 NULL 列**：`DELETE FROM "LfgIntent" WHERE "BossId" IS NULL;`（任意王意圖失效，無法滿足 NOT NULL）。
- `ALTER TABLE "LfgIntent" ALTER COLUMN "BossId" SET NOT NULL;`
- **重建唯一索引**（去掉 NULLS NOT DISTINCT）：`DROP INDEX uq_lfgintent_char_boss;` → `CREATE UNIQUE INDEX uq_lfgintent_char_boss ON "LfgIntent" ("CharacterId","BossId");`
- `down.sql`：反向（drop NOT NULL、索引改回 NULLS NOT DISTINCT）。
- ⚠️ **部署順序**（呼應原 §14 footgun，方向相反）：這次是「移除 nullable + 改查詢」——先滾**新 code**（查詢不再依賴 NULL 比對）還是先 migrate 都安全，但 NOT NULL 約束要在「不再寫入 NULL 的 code」上線後才套最穩 → **先滾新後端（含前端不再送 null）、再 migrate**。實作時定序。

### C. 前端 `web/`
7. `app/teams/instant/page.tsx`：移除 `<option value="">任意王</option>`；boss select 設必選（無空值）；`bossId === "" ? null : Number(bossId)` → 直接 `Number(bossId)`；移除 `item.bossName ?? "任意王"` 的 fallback（改直接顯示 bossName）。
8. `services/lfgService.ts`：`bossId: number | null` → `number`（兩處：request + response 型別）。

### D. 測試
9. 掃 `LfgServiceTests` / `TeamLeaderServiceIntegrationTests`（`Instant_GetCandidates_FromLfgIntent...`）/ e2e `instant-lfg`：若有用 `BossId=null`／任意王，改成具體 bossId；補一個「BossId 必填、缺了回 4xx」的測試。

### E. docs
10. `docs/architecture.md`（§13 LfgIntent 去重、ERD `LfgIntent.BossId`）、`docs/business-rules.md`（LfgIntent 規則）、`技術面試補強_MSRS架構參照.md`（§13 NULLS NOT DISTINCT、§14 migration 順序案例引用了任意王）、`CLAUDE.md`（若提到任意王）→ 全部改成「BossId 必填、無任意王」。
    - §13 的「NULLS NOT DISTINCT 巧妙處理 NULL」整段**移除**（沒 NULL 了）；可改成一句 YAGNI 故事：「原本有任意王(nullable+NULLS NOT DISTINCT)，社群實際不用 → 移除、schema/查詢/前端都簡化」。

### 非範圍
- 不動 `LfgIntent` 其他欄位、TTL 清理 job、instant 開團/候選其餘邏輯。
- `CharacterQuery.GetWithDiscordNameAsync(int? bossId=null)` 的 nullable 是**另一個用途**（bossId 用來算通關數、null=不算），**跟任意王無關，不要動**。

## 驗收
- [ ] `grep -rn "任意王\|BossId.*null\|NULLS NOT DISTINCT"` 在 code/docs 歸零（除本 plan 與 migration 註解）。
- [ ] `dotnet build` 綠；`dotnet test`（單元 + 整合，Docker Postgres）綠。
- [ ] e2e `instant-lfg` 綠（改成具體王）。
- [ ] 手驗：發即時找隊必須選王、缺王回 4xx；即時團候選只配到「該王」的意圖。
- [ ] migration 套用後 `\d "LfgIntent"` 看 BossId NOT NULL、索引無 NULLS NOT DISTINCT。

## 工時估
- 後端 code + migration ≈ 1h；前端 ≈ 20 分；測試 + docs ≈ 40 分；build/test/e2e 驗證 ≈ 40 分。總 ≈ 2.5h。

## 未解決
- 部署順序（先 code 或先 migrate）實作時定案；建議「先滾不再送 null 的前後端、再套 NOT NULL」最保險。
