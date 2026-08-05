-- 000008_revert_string_column_lengths.up.sql
-- 撤銷 000007 的 varchar(N) 長度約束，改回 text。
-- 理由：PostgreSQL 的 text 與 varchar(n) 無效能/儲存差異（varchar 只多一條長度 CHECK）；
-- 且 API 路徑已由 request DTO 的 [MaxLength] 驗證。保留 DB varchar 只是與 DTO 形成「雙來源」、
-- 需同步維護（易 drift），其唯一殘值（防非 API 寫入路徑）不足以抵銷這份僵硬。
-- 長度上限自此只在應用層（DTO MaxLength）維護，為單一真相。
ALTER TABLE "Character"
    ALTER COLUMN "Id"   TYPE text,
    ALTER COLUMN "Name" TYPE text,
    ALTER COLUMN "Job"  TYPE text;

ALTER TABLE "Boss"
    ALTER COLUMN "Name" TYPE text;

ALTER TABLE "BossTemplate"
    ALTER COLUMN "Name" TYPE text;
