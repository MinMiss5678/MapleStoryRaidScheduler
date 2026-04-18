-- 000002_add_indexes.up.sql
-- Apply indexes that were defined in init schema but not applied to existing DB

CREATE INDEX IF NOT EXISTS idx_character_discord      ON "Character"("DiscordId");
CREATE INDEX IF NOT EXISTS idx_player_register_period ON "PlayerRegister"("DiscordId", "PeriodId");
CREATE INDEX IF NOT EXISTS idx_team_slot_boss_dt      ON "TeamSlot"("BossId", "SlotDateTime");
CREATE INDEX IF NOT EXISTS idx_team_slot_char_slot    ON "TeamSlotCharacter"("TeamSlotId");
CREATE INDEX IF NOT EXISTS idx_team_slot_char_discord ON "TeamSlotCharacter"("DiscordId");
CREATE INDEX IF NOT EXISTS idx_session_discord        ON "Session"("DiscordId");
