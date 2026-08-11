-- 000018 period-less 重構 Phase 3：即時找隊意圖（LFG intent）。玩家表達「接下來要打某王」，帶 TTL、與 period 無關。
-- 供即時看板 + 即時團(Kind=Instant)的候選來源。到期靠讀取過濾 + sweep job 清理。
-- 見 plans/2026-08-11-realtime-team-formation.md §3.3 / Phase 3。
CREATE TABLE "LfgIntent" (
    "Id"          serial      PRIMARY KEY,
    "DiscordId"   bigint      NOT NULL REFERENCES "Player"("DiscordId")  ON DELETE CASCADE,
    "CharacterId" text        NOT NULL REFERENCES "Character"("Id")      ON DELETE CASCADE,
    "BossId"      integer              REFERENCES "Boss"("Id")           ON DELETE CASCADE,  -- null = 任意王
    "ExpiresAt"   timestamptz NOT NULL,
    "CreatedAt"   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_lfgintent_expires ON "LfgIntent"("ExpiresAt");
CREATE INDEX idx_lfgintent_discord ON "LfgIntent"("DiscordId");
