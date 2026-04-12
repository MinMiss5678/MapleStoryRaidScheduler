-- 000001_init_schema.down.sql
-- Rollback: drop all tables in reverse dependency order

DROP TABLE IF EXISTS "SystemConfig";
DROP TABLE IF EXISTS "Session";
DROP TABLE IF EXISTS "DiscordRoleMapping";
DROP TABLE IF EXISTS "JobCategory";
DROP TABLE IF EXISTS "TeamSlotCharacter";
DROP TABLE IF EXISTS "TeamSlot";
DROP TABLE IF EXISTS "CharacterRegister";
DROP TABLE IF EXISTS "PlayerAvailability";
DROP TABLE IF EXISTS "PlayerRegister";
DROP TABLE IF EXISTS "BossTemplateRequirement";
DROP TABLE IF EXISTS "BossTemplate";
DROP TABLE IF EXISTS "Character";
DROP TABLE IF EXISTS "Boss";
DROP TABLE IF EXISTS "Period";
DROP TABLE IF EXISTS "Player";
