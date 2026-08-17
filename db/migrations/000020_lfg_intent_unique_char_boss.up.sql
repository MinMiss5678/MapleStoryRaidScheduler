-- 即時找隊防重複：同角色對同一王（含「任意王」= NULL）只能有一筆找隊意圖。
-- 先去重（每組保留最新 Id），再建 NULLS NOT DISTINCT 唯一索引（PG15+，讓 NULL BossId 也視為相同一組）。
-- 配合 repo 改成 upsert：重貼＝刷新 TTL，不再無限新增列。
DELETE FROM "LfgIntent" a
USING "LfgIntent" b
WHERE a."CharacterId" = b."CharacterId"
  AND a."BossId" IS NOT DISTINCT FROM b."BossId"
  AND a."Id" < b."Id";

CREATE UNIQUE INDEX IF NOT EXISTS uq_lfgintent_char_boss
    ON "LfgIntent" ("CharacterId", "BossId") NULLS NOT DISTINCT;
