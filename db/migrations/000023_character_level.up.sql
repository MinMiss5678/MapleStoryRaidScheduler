-- 000023 人物等級 Level（自填，同 AttackPower 信任模型；見 plans/2026-08-25-character-level.md）。
-- 原則「有攻擊力處就有等級」：
--   Character.Level           — 來源真相（玩家自填 1–200，遊戲現行等級上限）
--   TeamSlotCharacter.Level   — 邀請/申請時快照（對齊 AttackPower 的快照策略）
--   TeamSlotRequirement.MinLevel — 招募門檻（group 層硬篩，非每職業；與 MinClearCount 並列）
-- 專案尚未上線、無現有真資料 → 一律 DEFAULT 0、免回填。
ALTER TABLE "Character"           ADD COLUMN "Level"    integer NOT NULL DEFAULT 0;
ALTER TABLE "TeamSlotCharacter"   ADD COLUMN "Level"    integer NOT NULL DEFAULT 0;
ALTER TABLE "TeamSlotRequirement" ADD COLUMN "MinLevel" integer NOT NULL DEFAULT 0;
