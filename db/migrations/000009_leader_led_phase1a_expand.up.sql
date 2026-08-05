-- 000009 leader-led Phase 1a：EXPAND-only（純加欄/加表、不收緊），舊行為（auto-assign/merge/補位）照跑。
-- 收緊（NOT NULL、Source 收斂 {leader,admin}、廢 DiscordId=0/'' 哨兵與空位 null-row、砍 IsManual）
-- 一律延後到 CONTRACT（1c/3，舊寫入者退場後）。見 plans/2026-08-05-leader-led-team-formation.md §3/§8。

-- ── TeamSlot：隊長歸屬 + 週期權威歸屬（FK）+ 隊伍說明 ──
-- 皆可空/不設 NOT NULL → 舊 AutoAssignAsync 的 INSERT（不帶這些欄）仍合法，行為不變。
ALTER TABLE "TeamSlot"
    ADD COLUMN "LeaderDiscordId" bigint  REFERENCES "Player"("DiscordId") ON DELETE SET NULL,
    ADD COLUMN "PeriodId"        integer REFERENCES "Period"("Id")        ON DELETE CASCADE,
    ADD COLUMN "Description"     text;

-- 一次性歷史對齊：既有隊的 PeriodId 由 SlotDateTime 落在哪個 Period 的 [StartDate,EndDate] 推出。
-- 之後由 app 寫入（硬綁不變式 SlotDateTime ∈ 該 Period 區間）。暫不設 NOT NULL——contract 才收。
UPDATE "TeamSlot" ts
SET "PeriodId" = p."Id"
FROM "Period" p
WHERE ts."SlotDateTime" >= p."StartDate" AND ts."SlotDateTime" <= p."EndDate";

-- ── TeamSlotCharacter：入隊狀態機。既有列皆屬舊模型的實際成員 → 視為 Confirmed。──
-- DEFAULT 'Confirmed' 讓舊 INSERT（不帶 Status）也自動落 Confirmed，行為不變。
ALTER TABLE "TeamSlotCharacter"
    ADD COLUMN "Status" text NOT NULL DEFAULT 'Confirmed';
ALTER TABLE "TeamSlotCharacter"
    ADD CONSTRAINT chk_tsc_status CHECK ("Status" IN ('Applied', 'Invited', 'Confirmed', 'Rejected'));

-- ── Character：楓葉祝福等級（自填，同 AttackPower 信任模型）──
ALTER TABLE "Character"
    ADD COLUMN "MapleBlessingLevel" integer NOT NULL DEFAULT 0;

-- ── 隊伍條件（掛 TeamSlot 實例）：一組可接受職業（各帶攻擊下限）+ 數量 + 通關數門檻 ──
CREATE TABLE "TeamSlotRequirement" (
    "Id"            serial  PRIMARY KEY,
    "TeamSlotId"    integer NOT NULL REFERENCES "TeamSlot"("Id") ON DELETE CASCADE,
    "Count"         integer NOT NULL DEFAULT 1,   -- 這列需要幾人
    "MinClearCount" integer NOT NULL DEFAULT 0    -- 本王通關數門檻（0 = 不限）
);

CREATE TABLE "TeamSlotRequirementJob" (
    "Id"             serial  PRIMARY KEY,
    "RequirementId"  integer NOT NULL REFERENCES "TeamSlotRequirement"("Id") ON DELETE CASCADE,
    "Job"            text    NOT NULL,             -- 分類存檔時已展開成具體職業（快照）
    "MinAttackPower" integer NOT NULL DEFAULT 0    -- 攻擊下限下放到職業層級
);

-- ── 通關次數（玩家自填）：本王總通關 = 同王跨該玩家角色相加（派生單一數字）──
CREATE TABLE "CharacterBossClear" (
    "Id"          serial  PRIMARY KEY,
    "CharacterId" text    NOT NULL REFERENCES "Character"("Id") ON DELETE CASCADE,
    "BossId"      integer NOT NULL REFERENCES "Boss"("Id")      ON DELETE CASCADE,
    "ClearCount"  integer NOT NULL DEFAULT 0
);

-- ── 索引 ──
CREATE INDEX idx_teamslot_period        ON "TeamSlot"("PeriodId");
CREATE INDEX idx_tsc_status             ON "TeamSlotCharacter"("TeamSlotId", "Status");  -- 數 Confirmed 用
CREATE INDEX idx_teamslot_req_slot      ON "TeamSlotRequirement"("TeamSlotId");
CREATE INDEX idx_teamslot_reqjob_req    ON "TeamSlotRequirementJob"("RequirementId");
CREATE UNIQUE INDEX uq_charbossclear    ON "CharacterBossClear"("CharacterId", "BossId"); -- 同角色同王一筆
