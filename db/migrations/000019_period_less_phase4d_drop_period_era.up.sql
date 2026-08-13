-- 000019 period-less 重構 Phase 4d：拆除 Period 承重牆 + 舊自動排團/報名子系統的資料表/欄。
-- 對應退場的 register/schedule/auto-assign、範本(BossTemplate)、職業分類(JobCategory)、報名截止；
-- leader-led 開隊不再綁 period（改驗 SlotDateTime 不得過去）。見 plans/2026-08-12-period-less-phase4cd-cleanup.md §4d。

-- 先卸掉指向待刪表的欄（TeamSlot.PeriodId→Period、TeamSlot.TemplateId→BossTemplate），索引隨欄自動移除
ALTER TABLE "TeamSlot"
    DROP COLUMN IF EXISTS "PeriodId",
    DROP COLUMN IF EXISTS "TemplateId";

-- 報名截止設定欄退場（SystemConfig 只留退團率警示欄）
ALTER TABLE "SystemConfig"
    DROP COLUMN IF EXISTS "DeadlineDayOfWeek",
    DROP COLUMN IF EXISTS "DeadlineTime",
    DROP COLUMN IF EXISTS "IsDeadlineNotified";

-- 刪表（依賴表先刪）
DROP TABLE IF EXISTS "CharacterRegister";
DROP TABLE IF EXISTS "PlayerAvailability";
DROP TABLE IF EXISTS "PlayerRegister";
DROP TABLE IF EXISTS "BossTemplateRequirement";
DROP TABLE IF EXISTS "BossTemplate";
DROP TABLE IF EXISTS "JobCategory";
DROP TABLE IF EXISTS "Period";
