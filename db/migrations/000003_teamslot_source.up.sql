-- TeamSlot provenance 欄位：IsTemporary(誤名) -> Source(auto|admin)
-- 同時移除死欄位 IsPublished（只寫不讀）

ALTER TABLE "TeamSlot"
    ADD COLUMN "Source" text NOT NULL DEFAULT 'auto';

-- 回填：舊 IsTemporary=true 為管理員建/批次，其餘為系統自動隊
UPDATE "TeamSlot" SET "Source" = CASE WHEN "IsTemporary" THEN 'admin' ELSE 'auto' END;

ALTER TABLE "TeamSlot"
    ADD CONSTRAINT chk_teamslot_source CHECK ("Source" IN ('auto', 'admin'));

ALTER TABLE "TeamSlot"
    DROP COLUMN "IsTemporary",
    DROP COLUMN "IsPublished";
