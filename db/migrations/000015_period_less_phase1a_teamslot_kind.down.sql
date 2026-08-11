ALTER TABLE "TeamSlot" DROP CONSTRAINT chk_teamslot_runs;
ALTER TABLE "TeamSlot" DROP CONSTRAINT chk_teamslot_kind;
ALTER TABLE "TeamSlot"
    DROP COLUMN "Kind",
    DROP COLUMN "ExpiresAt",
    DROP COLUMN "RunsMin",
    DROP COLUMN "RunsMax";
