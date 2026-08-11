-- 000015 period-less 重構 Phase 1a：TeamSlot 加 Kind（排程/即時）+ 即時 TTL + 場數範圍。
-- 純加欄、非破壞：Kind DEFAULT 'Scheduled' 讓既有列與舊 INSERT（不帶 Kind）自動落 Scheduled，行為不變。
-- 見 plans/2026-08-11-realtime-team-formation.md §3.1 / Phase 1。
ALTER TABLE "TeamSlot"
    ADD COLUMN "Kind"      text        NOT NULL DEFAULT 'Scheduled',
    ADD COLUMN "ExpiresAt" timestamptz,          -- 即時團 TTL 到期時刻；Scheduled 為 NULL（用 SlotDateTime 自然到期）
    ADD COLUMN "RunsMin"   integer,              -- 場數範圍（選填、僅告示不強制）：隊長公告這團打幾場
    ADD COLUMN "RunsMax"   integer;

ALTER TABLE "TeamSlot"
    ADD CONSTRAINT chk_teamslot_kind CHECK ("Kind" IN ('Scheduled', 'Instant'));

-- 場數範圍完整性：兩者皆填時 min<=max（DB 後防，見 plans/2026-08-06-validation-layering.md）
ALTER TABLE "TeamSlot"
    ADD CONSTRAINT chk_teamslot_runs CHECK ("RunsMin" IS NULL OR "RunsMax" IS NULL OR "RunsMin" <= "RunsMax");
