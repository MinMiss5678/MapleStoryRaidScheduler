-- 000001_init_schema.up.sql
-- Initial schema for MapleStory Raid Scheduler

CREATE TABLE "Player" (
    "DiscordId"   bigint PRIMARY KEY,
    "DiscordName" text   NOT NULL,
    "Role"        text   NOT NULL
);

CREATE TABLE "Character" (
    "Id"          text   PRIMARY KEY,
    "DiscordId"   bigint NOT NULL REFERENCES "Player"("DiscordId") ON DELETE CASCADE,
    "Name"        text   NOT NULL,
    "Job"         text   NOT NULL,
    "AttackPower" integer NOT NULL DEFAULT 0
);

CREATE TABLE "Period" (
    "Id"        serial      PRIMARY KEY,
    "StartDate" timestamptz NOT NULL,
    "EndDate"   timestamptz NOT NULL
);

CREATE TABLE "Boss" (
    "Id"               serial PRIMARY KEY,
    "Name"             text   NOT NULL,
    "RequireMembers"   integer NOT NULL DEFAULT 6,
    "RoundConsumption" integer NOT NULL DEFAULT 1
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

CREATE TABLE "PlayerRegister" (
    "Id"       serial  PRIMARY KEY,
    "DiscordId" bigint NOT NULL REFERENCES "Player"("DiscordId") ON DELETE CASCADE,
    "PeriodId" integer NOT NULL REFERENCES "Period"("Id") ON DELETE CASCADE
);

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

CREATE TABLE "TeamSlot" (
    "Id"          serial      PRIMARY KEY,
    "BossId"      integer     NOT NULL REFERENCES "Boss"("Id"),
    "SlotDateTime" timestamptz NOT NULL,
    "IsTemporary" boolean     NOT NULL DEFAULT false,
    "IsPublished" boolean     NOT NULL DEFAULT false,
    "TemplateId"  integer     REFERENCES "BossTemplate"("Id") ON DELETE SET NULL
);

CREATE TABLE "TeamSlotCharacter" (
    "Id"            serial  PRIMARY KEY,
    "TeamSlotId"    integer NOT NULL REFERENCES "TeamSlot"("Id") ON DELETE CASCADE,
    "DiscordId"     bigint  NOT NULL DEFAULT 0,
    "DiscordName"   text    NOT NULL DEFAULT '',
    "CharacterId"   text    REFERENCES "Character"("Id") ON DELETE SET NULL,
    "CharacterName" text,
    "Job"           text    NOT NULL,
    "AttackPower"   integer NOT NULL DEFAULT 0,
    "Rounds"        integer NOT NULL DEFAULT 0,
    "IsManual"      boolean NOT NULL DEFAULT false
);

CREATE TABLE "JobCategory" (
    "JobName"      text PRIMARY KEY,
    "CategoryName" text NOT NULL
);

CREATE TABLE "DiscordRoleMapping" (
    "DiscordRoleId" bigint       PRIMARY KEY,
    "Role"          varchar(50)  NOT NULL,
    "Priority"      integer      NOT NULL DEFAULT 0
);

CREATE TABLE "Session" (
    "SessionId"    text        PRIMARY KEY,
    "DiscordId"    bigint      NOT NULL,
    "AccessToken"  text        NOT NULL DEFAULT '',
    "RefreshToken" text        NOT NULL DEFAULT '',
    "Expiry"       timestamptz NOT NULL
);

CREATE TABLE "SystemConfig" (
    "Id"                   serial      PRIMARY KEY,
    "DeadlineDayOfWeek"    integer     NOT NULL DEFAULT 0,
    "DeadlineTime"         interval    NOT NULL DEFAULT '00:00:00',
    "IsDeadlineNotified"   boolean     NOT NULL DEFAULT false
);

-- Indexes for common query patterns
CREATE INDEX idx_character_discord      ON "Character"("DiscordId");
CREATE INDEX idx_player_register_period ON "PlayerRegister"("DiscordId", "PeriodId");
CREATE INDEX idx_team_slot_boss_dt      ON "TeamSlot"("BossId", "SlotDateTime");
CREATE INDEX idx_team_slot_char_slot    ON "TeamSlotCharacter"("TeamSlotId");
CREATE INDEX idx_team_slot_char_discord ON "TeamSlotCharacter"("DiscordId");
CREATE INDEX idx_session_discord        ON "Session"("DiscordId");
