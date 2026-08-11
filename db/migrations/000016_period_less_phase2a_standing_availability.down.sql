DROP INDEX IF EXISTS idx_character_seeking;
ALTER TABLE "Character" DROP COLUMN "IsSeekingRaid";
DROP TABLE IF EXISTS "PlayerAvailabilityStanding";
