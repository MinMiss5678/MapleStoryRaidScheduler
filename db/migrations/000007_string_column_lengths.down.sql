-- 還原為無長度限制的 text（varchar(N)→text 一律安全，text 為最寬型別）。
ALTER TABLE "Character"
    ALTER COLUMN "Id"   TYPE text,
    ALTER COLUMN "Name" TYPE text,
    ALTER COLUMN "Job"  TYPE text;

ALTER TABLE "Boss"
    ALTER COLUMN "Name" TYPE text;

ALTER TABLE "BossTemplate"
    ALTER COLUMN "Name" TYPE text;
