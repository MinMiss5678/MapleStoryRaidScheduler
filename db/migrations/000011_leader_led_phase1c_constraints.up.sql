-- 000011 leader-led Phase 1c：跨隊重疊 + 重複邀請/申請 的 DB 約束（§10）。EXPAND（純加）。
-- 跨隊時段重疊 unique 需要 SlotDateTime 在 TeamSlotCharacter 上（Postgres unique 不能跨表）→ 去正規化一份
-- （snapshot 語意，開隊/邀請時填、與成員屬性快照同一種；contract 前舊列由 backfill 補）。

ALTER TABLE "TeamSlotCharacter"
    ADD COLUMN "SlotDateTime" timestamptz;

-- 回填既有列：由所屬 TeamSlot 的 SlotDateTime
UPDATE "TeamSlotCharacter" tsc
SET "SlotDateTime" = ts."SlotDateTime"
FROM "TeamSlot" ts
WHERE ts."Id" = tsc."TeamSlotId";

-- 跨隊時段重疊（硬規則）：同玩家、同 SlotDateTime 的 Confirmed 只能一筆。
-- 排除 vacancy 哨兵（DiscordId=0，其 Status 因 000009 DEFAULT 亦為 'Confirmed'）——contract 廢哨兵後可簡化。
-- per-team 1002 鎖管不到跨隊，這是唯一可靠的原子擋（見 §10）。
CREATE UNIQUE INDEX uq_tsc_confirmed_overlap
    ON "TeamSlotCharacter" ("DiscordId", "SlotDateTime")
    WHERE "Status" = 'Confirmed' AND "DiscordId" <> 0;

-- 重複邀請/申請去重：同隊、同玩家一筆有效 Applied/Invited。
CREATE UNIQUE INDEX uq_tsc_active_membership
    ON "TeamSlotCharacter" ("TeamSlotId", "DiscordId")
    WHERE "Status" IN ('Applied', 'Invited');
