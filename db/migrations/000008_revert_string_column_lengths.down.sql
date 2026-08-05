-- 還原 000007 的 varchar(N) 約束（＝000007 的 up）。
ALTER TABLE "Character"
    ALTER COLUMN "Id"   TYPE varchar(5),
    ALTER COLUMN "Name" TYPE varchar(20),
    ALTER COLUMN "Job"  TYPE varchar(20);

ALTER TABLE "Boss"
    ALTER COLUMN "Name" TYPE varchar(50);

ALTER TABLE "BossTemplate"
    ALTER COLUMN "Name" TYPE varchar(50);
