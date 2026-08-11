-- 000017 period-less 重構 Phase 2b：可用時段「日期 override（例外）」。
-- 疊在常設之上：某特定日期標「不行(蓋掉常設)」或「額外加開」。候選比對時 override 勝過常設。
-- 見 plans/2026-08-11-realtime-team-formation.md §8 Phase 2 / §8.2。
CREATE TABLE "PlayerAvailabilityOverride" (
    "Id"          serial  PRIMARY KEY,
    "DiscordId"   bigint  NOT NULL REFERENCES "Player"("DiscordId") ON DELETE CASCADE,
    "Date"        date    NOT NULL,
    "StartTime"   time    NOT NULL,
    "EndTime"     time    NOT NULL,
    "IsAvailable" boolean NOT NULL   -- true=額外加開；false=該時段不行（蓋掉常設）
);
CREATE INDEX idx_availoverride_discord_date ON "PlayerAvailabilityOverride"("DiscordId","Date");
CREATE INDEX idx_availoverride_date ON "PlayerAvailabilityOverride"("Date");
