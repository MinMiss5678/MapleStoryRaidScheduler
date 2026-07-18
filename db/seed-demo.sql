-- =============================================================
-- 管理員重排 Demo 假資料（可重複執行）
--
-- 建立一個完整可重排的情境：
--   • 當期 Period（涵蓋今天起 7 天）
--   • DEMO 王 + 範本（單一「輸出」類別、需 6 人）+ 職業對照
--   • 12 個池玩家 + 角色 + 報名（皆「明天 20:00-22:00」可用，各 1 場）
--   • 既有【自動隊】(Source=auto, 無 IsManual)  → 重排時整隊被替換
--   • 既有【保留隊】(Source=auto 但含補位 IsManual) → 重排時保留並自動補滿空位
--   • Admin session（本機免 Discord 登入，瀏覽器手動設 cookie 即可）
--
-- 用法：psql "<連線字串>" -f db/seed-demo.sql
-- =============================================================

BEGIN;

-- ---- 先清掉上一輪 demo（順序：先清有外鍵指向 Boss 的資料）----
DELETE FROM "Session" WHERE "SessionId" = 'demo-admin-session';
DELETE FROM "Player"  WHERE "DiscordId" BETWEEN 1001 AND 1013; -- 連帶清 Character / PlayerRegister / 可用時段 / CharacterRegister
DELETE FROM "TeamSlot" WHERE "BossId" IN (SELECT "Id" FROM "Boss" WHERE "Name" = 'DEMO王');
DELETE FROM "Boss"    WHERE "Name" = 'DEMO王';                 -- 連帶清 BossTemplate / Requirement
DELETE FROM "Period"  WHERE "StartDate"::date = CURRENT_DATE AND "EndDate"::date = CURRENT_DATE + 7;
DELETE FROM "JobCategory" WHERE "CategoryName" = '輸出';

DO $$
DECLARE
    v_period_id   int;
    v_boss_id     int;
    v_template_id int;
    v_weekday     int         := EXTRACT(DOW FROM (CURRENT_DATE + 1))::int;                 -- 0=週日..6=週六
    v_slot_ts     timestamptz := ((CURRENT_DATE + 1)::text || ' 12:00:00+00')::timestamptz; -- 明天 20:00（台北 = UTC+8）
    v_auto_team   int;
    v_prot_team   int;
    v_reg         int;
    i             int;
    v_did         bigint;
    v_cid         text;
    jobs          text[] := ARRAY['Hero','Paladin','DarkKnight','Bishop','NightLord','Shadower',
                                   'Bowmaster','Marksman','Corsair','Buccaneer','Aran','Evan'];
BEGIN
    -- 當期
    INSERT INTO "Period"("StartDate","EndDate")
    VALUES (CURRENT_DATE::timestamptz, (CURRENT_DATE + 7)::timestamptz)
    RETURNING "Id" INTO v_period_id;

    -- 王 + 範本（單一「輸出」類別、需 6 人）
    INSERT INTO "Boss"("Name","RequireMembers","RoundConsumption")
    VALUES ('DEMO王', 6, 1) RETURNING "Id" INTO v_boss_id;
    INSERT INTO "BossTemplate"("BossId","Name")
    VALUES (v_boss_id, 'DEMO範本') RETURNING "Id" INTO v_template_id;
    INSERT INTO "BossTemplateRequirement"("BossTemplateId","JobCategory","Count","Priority")
    VALUES (v_template_id, '輸出', 6, 1);

    -- 職業 → 「輸出」類別對照
    INSERT INTO "JobCategory"("JobName","CategoryName")
    SELECT unnest(jobs), '輸出'
    ON CONFLICT ("JobName") DO NOTHING;

    -- Admin 玩家（給 session）
    INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (1001, 'DEMO管理員', 'admin');

    -- 12 個池玩家：角色 + 報名 + 可用時段（明天 20:00-22:00、各 1 場）
    FOR i IN 1..12 LOOP
        v_did := 1001 + i;              -- 1002..1013
        v_cid := 'ch' || v_did;
        INSERT INTO "Player"("DiscordId","DiscordName","Role") VALUES (v_did, 'P' || i, 'user');
        INSERT INTO "Character"("Id","DiscordId","Name","Job","AttackPower")
        VALUES (v_cid, v_did, 'C' || i, jobs[i], 1000 - i * 10);
        INSERT INTO "PlayerRegister"("DiscordId","PeriodId")
        VALUES (v_did, v_period_id) RETURNING "Id" INTO v_reg;
        INSERT INTO "CharacterRegister"("PlayerRegisterId","CharacterId","BossId","Rounds")
        VALUES (v_reg, v_cid, v_boss_id, 1);
        INSERT INTO "PlayerAvailability"("PlayerRegisterId","Weekday","StartTime","EndTime")
        VALUES (v_reg, v_weekday, TIME '20:00', TIME '22:00');
    END LOOP;

    -- 既有【自動隊】(Source=auto、無 IsManual)：明天 19:00，含 P2/P3 → 重排時被替換
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","TemplateId")
    VALUES (v_boss_id, ((CURRENT_DATE + 1)::text || ' 11:00:00+00')::timestamptz, 'auto', v_template_id)
    RETURNING "Id" INTO v_auto_team;
    INSERT INTO "TeamSlotCharacter"
        ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Rounds","IsManual")
    VALUES
        (v_auto_team, 1003, 'P2', 'ch1003', 'C2', jobs[2], 980, 1, false),
        (v_auto_team, 1004, 'P3', 'ch1004', 'C3', jobs[3], 970, 1, false);

    -- 既有【保留隊】(Source=auto 但含補位 IsManual=true 的 P1)：明天 20:00、缺 5 人
    --   → 重排時整隊保留、自動補滿 5 個空位（補入者 IsManual=false）
    INSERT INTO "TeamSlot"("BossId","SlotDateTime","Source","TemplateId")
    VALUES (v_boss_id, v_slot_ts, 'auto', v_template_id)
    RETURNING "Id" INTO v_prot_team;
    INSERT INTO "TeamSlotCharacter"
        ("TeamSlotId","DiscordId","DiscordName","CharacterId","CharacterName","Job","AttackPower","Rounds","IsManual")
    VALUES
        (v_prot_team, 1002, 'P1', 'ch1002', 'C1', jobs[1], 990, 1, true);

    -- Admin session（Expiry 30 天）
    INSERT INTO "Session"("SessionId","DiscordId","AccessToken","RefreshToken","Expiry")
    VALUES ('demo-admin-session', 1001, '', '', now() + interval '30 days');

    RAISE NOTICE 'DEMO 就緒 → periodId=%, bossId=%, templateId=%, 保留隊 slot=%',
        v_period_id, v_boss_id, v_template_id, v_slot_ts;
END $$;

COMMIT;
