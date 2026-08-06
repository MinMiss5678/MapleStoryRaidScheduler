DROP INDEX IF EXISTS uq_tsc_active_membership;
DROP INDEX IF EXISTS uq_tsc_confirmed_overlap;
ALTER TABLE "TeamSlotCharacter" DROP COLUMN IF EXISTS "SlotDateTime";
