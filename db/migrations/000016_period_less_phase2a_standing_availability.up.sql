-- 000016 period-less 重構 Phase 2a：可用時段解耦成「常設（掛玩家）」+ 角色參戰 opt-in。
-- 候選池定義改為（B 案）：「參戰中」角色 × 其玩家常設可用時段 overlap 開團時間。
-- pre-launch 硬切：新表 + 新欄；舊 PlayerAvailability(掛 register) 之後由 Phase 4 一併拆。
-- 見 plans/2026-08-11-realtime-team-formation.md §8 Phase 2。

-- 常設可用時段：掛玩家（非每週報名），一份長存
CREATE TABLE "PlayerAvailabilityStanding" (
    "Id"        serial  PRIMARY KEY,
    "DiscordId" bigint  NOT NULL REFERENCES "Player"("DiscordId") ON DELETE CASCADE,
    "Weekday"   integer NOT NULL,
    "StartTime" time    NOT NULL,
    "EndTime"   time    NOT NULL
);
CREATE INDEX idx_availstanding_discord ON "PlayerAvailabilityStanding"("DiscordId");

-- 角色參戰 opt-in（B 案）：逐角色標「這隻要被揪」；多角色玩家可只放主號。預設 false。
ALTER TABLE "Character"
    ADD COLUMN "IsSeekingRaid" boolean NOT NULL DEFAULT false;
CREATE INDEX idx_character_seeking ON "Character"("IsSeekingRaid") WHERE "IsSeekingRaid";
