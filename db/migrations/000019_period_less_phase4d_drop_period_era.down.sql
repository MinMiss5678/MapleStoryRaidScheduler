-- 000019 down：還原 Phase 4d 拆除的 Period 承重 + 舊子系統資料表/欄（回到套 000019 之前、即 000018 後的狀態）。
-- 依 FK 建立順序：被參照表先建。定義照 000001（Period/BossTemplate/JobCategory/PlayerRegister/CharacterRegister/
-- PlayerAvailability、SystemConfig 截止欄、TeamSlot.TemplateId）與 000009（TeamSlot.PeriodId + idx_teamslot_period）。

CREATE TABLE "Period" (
    "Id"        serial      PRIMARY KEY,
    "StartDate" timestamptz NOT NULL,
    "EndDate"   timestamptz NOT NULL
);

CREATE TABLE "BossTemplate" (
    "Id"     serial  PRIMARY KEY,
    "BossId" integer NOT NULL REFERENCES "Boss"("Id") ON DELETE CASCADE,
    "Name"   text    NOT NULL
);

CREATE TABLE "BossTemplateRequirement" (
    "Id"             serial  PRIMARY KEY,
    "BossTemplateId" integer NOT NULL REFERENCES "BossTemplate"("Id") ON DELETE CASCADE,
    "JobCategory"    text    NOT NULL,
    "Count"          integer NOT NULL DEFAULT 1,
    "Priority"       integer NOT NULL DEFAULT 0
);

CREATE TABLE "JobCategory" (
    "JobName"      text PRIMARY KEY,
    "CategoryName" text NOT NULL
);

CREATE TABLE "PlayerRegister" (
    "Id"        serial  PRIMARY KEY,
    "DiscordId" bigint  NOT NULL REFERENCES "Player"("DiscordId") ON DELETE CASCADE,
    "PeriodId"  integer NOT NULL REFERENCES "Period"("Id") ON DELETE CASCADE
);
CREATE INDEX idx_player_register_period ON "PlayerRegister"("DiscordId", "PeriodId");

CREATE TABLE "PlayerAvailability" (
    "Id"               serial  PRIMARY KEY,
    "PlayerRegisterId" integer NOT NULL REFERENCES "PlayerRegister"("Id") ON DELETE CASCADE,
    "Weekday"          integer NOT NULL,
    "StartTime"        time    NOT NULL,
    "EndTime"          time    NOT NULL
);

CREATE TABLE "CharacterRegister" (
    "Id"               serial  PRIMARY KEY,
    "PlayerRegisterId" integer NOT NULL REFERENCES "PlayerRegister"("Id") ON DELETE CASCADE,
    "CharacterId"      text    NOT NULL REFERENCES "Character"("Id") ON DELETE CASCADE,
    "BossId"           integer NOT NULL REFERENCES "Boss"("Id"),
    "Rounds"           integer NOT NULL DEFAULT 1
);

-- 還原 SystemConfig 報名截止欄
ALTER TABLE "SystemConfig"
    ADD COLUMN "DeadlineDayOfWeek"  integer  NOT NULL DEFAULT 0,
    ADD COLUMN "DeadlineTime"       interval NOT NULL DEFAULT '00:00:00',
    ADD COLUMN "IsDeadlineNotified" boolean  NOT NULL DEFAULT false;

-- 還原 TeamSlot.TemplateId（000001）+ PeriodId（000009，含索引）
ALTER TABLE "TeamSlot"
    ADD COLUMN "TemplateId" integer REFERENCES "BossTemplate"("Id") ON DELETE SET NULL,
    ADD COLUMN "PeriodId"   integer REFERENCES "Period"("Id")       ON DELETE CASCADE;
CREATE INDEX idx_teamslot_period ON "TeamSlot"("PeriodId");
