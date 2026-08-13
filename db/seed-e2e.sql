-- =============================================================
-- E2E 專用 seed（可重複執行）
--
-- period-less（Phase 4d）：報名/週期/範本/職業分類全退場 → 候選池只讀
-- 「常設可用時段 PlayerAvailabilityStanding + 角色 IsSeekingRaid + CharacterBossClear」。
-- 故本 seed 直接寫這些新世界資料，不再經 register/period 鏡射。
--
-- 只放「保留的 e2e」需要的 fixture：leader-led（Push/Pull/轉讓/自動撤銷）、instant-lfg、profile。
-- 隊長/測試自身的 Player 多由 test-login 於測試中自建（6001/6004/6007/7001/8102…），不在此 seed。
-- 隊伍一律由測試走 UI 開，故本 seed 不建任何 TeamSlot。
--
-- 用法：docker compose -f compose.e2e.yaml exec -T e2e-db env PGPASSWORD=e2e \
--        psql -U postgres -d presentationdb < db/seed-e2e.sql
-- =============================================================

BEGIN;

-- 清交易資料（CASCADE 連帶清 standing/override/lfg/charbossclear/requirement 等子表）
TRUNCATE "TeamSlotCharacter","TeamSlot","Character","Player","Boss"
    RESTART IDENTITY CASCADE;

DO $$
DECLARE
    v_boss_id      int;
    v_boss_full_id int;
BEGIN
    -- 主要王（多人）：leader-led / candidates / transfer / instant 用
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('E2E王', 6, 1) RETURNING "Id" INTO v_boss_id;

    -- 容量 1 的王：auto-revoke（一人接受即滿、另一人邀請被自動撤銷）用
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('E2E王滿', 1, 1) RETURNING "Id" INTO v_boss_full_id;

    -- profile e2e：P-New(2001) 有角色、未參戰 → 供「我的資料」勾參戰角色測試
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (2001, 'P-New', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c2001', 2001, 'CNew', '英雄', 950);

    -- leader-led Push e2e：申請者 P-LL(6002)（直接申請入隊，不需常設時段）
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6002, 'P-LL', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6002', 6002, 'C-LL', '英雄', 950);

    -- leader-led 候選(Pull) e2e：候選 P-Cand(6003) 英雄 + 全週整天常設可用 + 參戰中
    --  → 任何當期內的開團時間都命中（不吃星期/時區）。隊長由 test-login 自建(6004)。
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6003, 'P-Cand', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6003', 6003, 'C-Cand', '英雄', 950);
    INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
    SELECT 6003, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;
    UPDATE "Character" SET "IsSeekingRaid" = true WHERE "Id" = 'c6003';

    -- leader-led 自動撤銷過期邀請(Tier 3) e2e：容量 1 的王 + 兩名全週可用候選
    --  P-Full-A(6005)/P-Full-B(6006)。隊長(6007,test-login 自建)邀兩人 → 其一接受使隊伍額滿
    --  → 另一人的邀請被自動撤銷。
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6005, 'P-Full-A', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6005', 6005, 'C-Full-A', '夜使者', 950);
    INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
    SELECT 6005, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;

    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (6006, 'P-Full-B', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c6006', 6006, 'C-Full-B', '夜使者', 950);
    INSERT INTO "PlayerAvailabilityStanding"("DiscordId","Weekday","StartTime","EndTime")
    SELECT 6006, gs, TIME '00:00', TIME '00:00' FROM generate_series(1, 7) AS gs;

    UPDATE "Character" SET "IsSeekingRaid" = true WHERE "Id" IN ('c6005', 'c6006');

    -- leader-led 隊長轉讓 e2e：未來新隊長 P-Trans(7002)（Push 入隊後被轉讓）
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (7002, 'P-Trans', 'user');
    INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
    VALUES ('c7002', 7002, 'C-Trans', '英雄', 950);

    RAISE NOTICE 'E2E seed 就緒 → bossId=%, bossFullId=%', v_boss_id, v_boss_full_id;
END $$;

-- period-less Phase 3 即時揪團 e2e：P-Lfg(8101)（發找隊 → 被即時團邀請 → 接受）。
-- 隊長 8102 由 test-login 自建。即時候選走 LfgIntent（不需常設時段/IsSeekingRaid）。
INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (8101, 'P-Lfg', 'user');
INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower") VALUES ('c8101', 8101, 'C-Lfg', '夜使者', 950);

COMMIT;
