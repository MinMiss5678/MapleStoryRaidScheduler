-- 還原到 {auto, admin}（若已有 'leader' 資料會失敗——contract 前不應有）。
ALTER TABLE "TeamSlot" DROP CONSTRAINT chk_teamslot_source;
ALTER TABLE "TeamSlot" ADD CONSTRAINT chk_teamslot_source CHECK ("Source" IN ('auto', 'admin'));
