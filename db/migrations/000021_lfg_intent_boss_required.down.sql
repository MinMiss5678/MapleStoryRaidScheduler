-- 回復「任意王」：BossId 可為 NULL + NULLS NOT DISTINCT 唯一索引。
DROP INDEX IF EXISTS uq_lfgintent_char_boss;
ALTER TABLE "LfgIntent" ALTER COLUMN "BossId" DROP NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_lfgintent_char_boss
    ON "LfgIntent" ("CharacterId", "BossId") NULLS NOT DISTINCT;
