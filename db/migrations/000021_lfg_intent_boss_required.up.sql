-- 移除「任意王」(BossId=NULL)：社群實際找隊都針對特定王，nullable BossId 為揣測性、無真實用途 → 移除。
-- 1) 刪掉現有「任意王」意圖（無法滿足 NOT NULL）。
DELETE FROM "LfgIntent" WHERE "BossId" IS NULL;

-- 2) BossId 改必填。
ALTER TABLE "LfgIntent" ALTER COLUMN "BossId" SET NOT NULL;

-- 3) 重建唯一索引（BossId 已 NOT NULL → 不再需要 NULLS NOT DISTINCT）。
DROP INDEX IF EXISTS uq_lfgintent_char_boss;
CREATE UNIQUE INDEX IF NOT EXISTS uq_lfgintent_char_boss
    ON "LfgIntent" ("CharacterId", "BossId");
