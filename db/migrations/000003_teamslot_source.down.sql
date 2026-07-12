-- 還原 TeamSlot provenance 欄位

ALTER TABLE "TeamSlot"
    ADD COLUMN "IsTemporary" boolean NOT NULL DEFAULT false,
    ADD COLUMN "IsPublished" boolean NOT NULL DEFAULT false;

UPDATE "TeamSlot" SET "IsTemporary" = ("Source" = 'admin');

ALTER TABLE "TeamSlot"
    DROP CONSTRAINT chk_teamslot_source;

ALTER TABLE "TeamSlot"
    DROP COLUMN "Source";
