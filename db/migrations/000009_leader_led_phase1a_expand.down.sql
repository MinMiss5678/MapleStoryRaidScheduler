-- 000009 down：反向撤銷 Phase 1a expand（先 drop 依賴表，再 drop 欄）。
DROP TABLE IF EXISTS "CharacterBossClear";
DROP TABLE IF EXISTS "TeamSlotRequirementJob";
DROP TABLE IF EXISTS "TeamSlotRequirement";

ALTER TABLE "Character"
    DROP COLUMN IF EXISTS "MapleBlessingLevel";

-- 先移 CHECK 再 drop 欄（drop 欄本會連帶移約束，但顯式較清楚）
ALTER TABLE "TeamSlotCharacter"
    DROP CONSTRAINT IF EXISTS chk_tsc_status;
ALTER TABLE "TeamSlotCharacter"
    DROP COLUMN IF EXISTS "Status";

-- 索引隨欄位/表 drop 自動移除；TeamSlot 三欄一併撤（含其 idx_teamslot_period）
ALTER TABLE "TeamSlot"
    DROP COLUMN IF EXISTS "Description",
    DROP COLUMN IF EXISTS "PeriodId",
    DROP COLUMN IF EXISTS "LeaderDiscordId";
