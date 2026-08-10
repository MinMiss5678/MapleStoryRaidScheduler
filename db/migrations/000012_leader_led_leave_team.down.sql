-- 反向：Left 資料先轉回 Rejected（否則 CHECK 縮回會違反），再縮 CHECK、砍 LeftAt。
UPDATE "TeamSlotCharacter" SET "Status" = 'Rejected' WHERE "Status" = 'Left';
ALTER TABLE "TeamSlotCharacter" DROP CONSTRAINT chk_tsc_status;
ALTER TABLE "TeamSlotCharacter" ADD CONSTRAINT chk_tsc_status
    CHECK ("Status" IN ('Applied', 'Invited', 'Confirmed', 'Rejected'));
ALTER TABLE "TeamSlotCharacter" DROP COLUMN "LeftAt";
